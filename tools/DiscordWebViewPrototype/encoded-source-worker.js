// Serialized into a dedicated worker by the opt-in encoded-source candidate.
function runEncodedSourceWorker() {
    let source, input, reader, writer, encoder;
    let stopped = false, started = false, pending = 0, firstTimestamp;
    let configured = null, ceiling = null, lastChange = -Infinity, lastSample = -Infinity;
    let previousBits = 0;
    let receivedAllocation = false, needKey = true, lastKey = -Infinity;
    let nativeEncoder = false, nativeSince = null, waitingForKey = true, nativeFallback = null;
    let previousClock, timestampOffset = 0, lastTimestamp, lastOutputAt;
    const counts = { input: 0, encoded: 0, sent: 0, bits: 0, keys: 0, requests: 0, queueDrop: 0, sps: null };

    function sample(now) {
        if (now - lastSample < 1000) return;
        const encodedBitrate = Number.isFinite(lastSample) ? (counts.bits - previousBits) * 1000 / (now - lastSample) : null;
        previousBits = counts.bits;
        lastSample = now;
        postMessage({ kind: 'sample', ...counts, allocated: source.allocatedBitrate,
            configured, ceiling, encodedBitrate, available: source.availableOutgoingBitrate,
            encoderPath:nativeEncoder?'media-foundation':'webcodecs',nativeFallback });
    }
    function stop() {
        stopped = true;
        reader?.cancel().catch(() => {});
        if (encoder && encoder.state !== 'closed') encoder.close();
    }
    function fail(error) {
        if (stopped) return;
        postMessage({ kind: 'failed', error: String(error.message ?? error) });
        stop();
    }
    function leaveNative(reason) {
        if (!nativeEncoder) return;
        nativeEncoder = false; nativeFallback = reason; needKey = true; configured = null;
        postMessage({kind:'nativeClosed'});
    }
    function onChunk(chunk) {
        if (stopped) return;
        try {
            const bytes = new Uint8Array(chunk.byteLength);
            chunk.copyTo(bytes);
            if (chunk.type === 'key') {
                counts.keys++;
                // Verify the SPS before forwarding any keyframe to the negotiated H.264 sender.
                let compatible = false;
                for (let i = 0; i + 6 < bytes.length; i++) {
                    if (bytes[i] || bytes[i + 1] || bytes[i + 2] !== 1 || (bytes[i + 3] & 31) !== 7) continue;
                    counts.sps = Array.from(bytes.subarray(i + 4, i + 7), b => b.toString(16).padStart(2, '0')).join('');
                    compatible = bytes[i + 4] === 0x42 && !!(bytes[i + 5] & 0x40) && bytes[i + 6] >= input.minimumLevel && bytes[i + 6] <= input.maximumLevel;
                    break;
                }
                if (!compatible) {
                    if(nativeEncoder){leaveNative('profile');return;}
                    throw Error('Incompatible H.264 SPS: ' + counts.sps);
                }
            }
            counts.encoded++;
            counts.bits += bytes.length * 8;
            // TrackProcessor and native capture may use different clock epochs.
            // Keep one RTP timeline if a device fails after emitting native frames.
            const now=performance.now();
            if(previousClock!==undefined&&previousClock!==nativeEncoder)
                timestampOffset=lastTimestamp+Math.max(1,Math.round((now-lastOutputAt)*1000))-chunk.timestamp;
            const timestamp=Math.max(lastTimestamp===undefined?0:lastTimestamp+1,chunk.timestamp+timestampOffset);
            firstTimestamp??=timestamp;previousClock=nativeEncoder;lastTimestamp=timestamp;lastOutputAt=now;
            const frame = new RTCEncodedVideoFrame({ type: chunk.type, data: bytes.buffer, mimeType: 'video/H264',
                payloadType: input.payloadType, width: input.width, height: input.height, timestamp,
                rtpTimestampWithoutOffset: Math.round((timestamp-firstTimestamp)*.09)>>>0, contributingSources: [] });
            pending++;
            writer.write(frame).then(() => { pending--; counts.sent++; }, fail);
        } catch (error) { fail(error); }
    }
    async function start() {
        if (!source || !input || started || stopped) return;
        started = true;
        try {
            reader = input.readable.getReader();
            const config = { codec: 'avc1.' + input.profile, width: input.width, height: input.height, framerate: input.fps,
                bitrateMode: 'constant', latencyMode: 'realtime', hardwareAcceleration: 'prefer-hardware', avc: { format: 'annexb' } };
            // Native-only formats need no WebCodecs support until a fallback is required.
            const configureFallback = async () => {
                if (!(await VideoEncoder.isConfigSupported(config)).supported) throw Error('Encoder configuration unavailable');
                if (!stopped) encoder = new VideoEncoder({ error: fail, output: onChunk });
            };
            while (!stopped) {
                const { value, done } = await reader.read();
                if (done) break;
                try {
                    if (value.displayWidth !== input.width || value.displayHeight !== input.height) throw Error('Source dimensions changed');
                    const now = performance.now(), allocation = source.allocatedBitrate;
                    if (allocation > 0) receivedAllocation = true;
                    if (ceiling === 0 || (receivedAllocation && allocation === 0)) {
                        if(nativeEncoder&&configured!==0){
                            postMessage({kind:'nativeRate',bitrate:0,keyframe:false});
                            configured=0;nativeSince=null;waitingForKey=true;
                        }
                        needKey = true;
                        sample(now);
                        continue;
                    }
                    // Bootstrap from the previous sender target if allocation has not arrived.
                    // With no signaled rate at all, leave bitrate to WebCodecs.
                    let target = allocation > 0 ? allocation : (input.initialTarget ?? ceiling);
                    if (ceiling !== null && target !== null) target = Math.min(target, ceiling);
                    if (target > input.codecBitrateLimit) throw Error('Allocation exceeds negotiated H.264 level');
                    if(nativeEncoder && target!==null && target>0){
                        nativeSince??=now;
                        if(target>input.nativeBitrateLimit){leaveNative('bitrate');}
                        else if(now-nativeSince>2500){leaveNative('timeout');}
                        else{
                            if(needKey || (target!==configured && (now-lastChange>=250 || (ceiling!==null && configured>ceiling)))){
                                configured=Math.round(target);lastChange=now;
                                postMessage({kind:'nativeRate',bitrate:configured,keyframe:needKey});
                                needKey=false;
                            }
                            counts.input++;sample(now);continue;
                        }
                    }else if(nativeEncoder){leaveNative('missing_allocation');}
                    if (!encoder) await configureFallback();
                    if (stopped) break;
                    // Coalesce small, rapid feedback updates without a percentage dead band.
                    // A new provider ceiling takes effect immediately.
                    if (encoder.state === 'unconfigured' || (target !== configured &&
                        (now - lastChange >= 250 || (ceiling !== null && configured > ceiling)))) {
                        encoder.configure(target === null ? config : { ...config, bitrate: target });
                        configured = target;
                        lastChange = now;
                    }
                    sample(now);
                    if (encoder.encodeQueueSize >= 3 || pending >= 3) { counts.queueDrop++; continue; }
                    const keyFrame = needKey || now - lastKey >= 4000;
                    encoder.encode(value, { keyFrame });
                    if (keyFrame) lastKey = now;
                    needKey = false;
                    counts.input++;
                } finally { value.close(); }
            }
        } catch (error) { fail(error); }
    }
    self.onrtcsenderencodedsource = event => {
        source = event.encodedSource;
        writer = source.writable.getWriter();
        source.onkeyframerequest = () => { needKey = true; counts.requests++; };
        start();
    };
    self.onmessage = ({ data }) => {
        if (data.kind === 'stop') { stop(); return; }
        if(data.kind==='nativeUnit'){
            if(!nativeEncoder||stopped||!source||configured===0||ceiling===0||(receivedAllocation&&source.allocatedBitrate===0))return;
            const u=data.unit;
            if (u && (u.width !== input.width || u.height !== input.height)) {
                fail(Error('Source dimensions changed'));
                return;
            }
            if(!u||pending>=3){leaveNative(u?'backpressure':'host');return;}
            if(waitingForKey&&u.type!=='key')return;
            waitingForKey=false;nativeSince=performance.now();
            onChunk(new EncodedVideoChunk({type:u.type,timestamp:u.time,data:u.data}));
            return;
        }
        ceiling = Number.isFinite(data.ceiling) && data.ceiling >= 0 ? data.ceiling : null;
        if (data.kind === 'ceiling') return;
        input = data;
        nativeEncoder = data.nativeEncoder === true;
        start();
    };
}
