// Validation-only browser WebRTC policy, sourced from Discord's browser SDP /
// sender mapping and a default Vesktop-reference8Mbps maximum. A staged, bounded
// test configuration can select codec/ceiling for controlled comparisons.
// Does not set unsupported minBitrate
// or disable bandwidth/CPU adaptation to make a test pass.
(() => {
    const configuration=globalThis.captureProbeConfiguration??{};
    const maximum=configuration.maximumBitrate??8000000,codec=configuration.codec??'Auto',h264Profile=configuration.h264Profile??'Auto';
    if(!Number.isSafeInteger(maximum)||maximum<1000000||maximum>50000000||!['Auto','H264','VP8','VP9','AV1'].includes(codec)||
        !['Auto','42001f','42e01f'].includes(h264Profile)||(h264Profile!=='Auto'&&codec!=='H264'))
        throw Error('Invalid local encoder policy');
    function h264Format(line){
        const unknown=()=>({profileLevelId:null,packetizationMode:null});
        if(typeof line!=='string'||line.length>2048)return unknown();
        const fields=line.split(';');if(fields.length>32)return unknown();
        const values=new Map();
        for(const field of fields){
            const pair=/^\s*(profile-level-id|packetization-mode)\s*=\s*([^;]+?)\s*$/i.exec(field);
            if(!pair)continue;
            const key=pair[1].toLowerCase();if(values.has(key))return unknown();values.set(key,pair[2]);
        }
        const profile=values.get('profile-level-id'),mode=values.get('packetization-mode');
        return {profileLevelId:/^[0-9a-f]{6}$/i.test(profile??'')?profile.toLowerCase():null,
            packetizationMode:/^[01]$/.test(mode??'')?Number(mode):null};
    }
    function chooseCodec(connection){
        if(codec==='Auto')return;
        const capabilities=globalThis.RTCRtpSender?.getCapabilities?.('video');
        const available=capabilities?.codecs;
        if(!Array.isArray(available)||available.length>64)throw Error('Video codec capabilities unavailable');
        const selected=available.filter(value=>{
            if(value.mimeType?.toLowerCase()!=='video/'+codec.toLowerCase())return false;
            if(h264Profile==='Auto')return true;
            const format=h264Format(value.sdpFmtpLine);
            return format.profileLevelId===h264Profile&&format.packetizationMode===1;
        });
        if(!selected.length)throw Error('Requested local codec is unavailable: '+codec);
        const auxiliary=available.filter(value=>['video/rtx','video/red','video/ulpfec','video/flexfec-03'].includes(value.mimeType?.toLowerCase()));
        const transceivers=connection.getTransceivers().filter(value=>value.sender?.track?.kind==='video');
        if(transceivers.length!==1||typeof transceivers[0].setCodecPreferences!=='function')throw Error('Exact video transceiver unavailable');
        transceivers[0].setCodecPreferences([...selected,...auxiliary]);
    }
    function description(value){
        if(!value||typeof value.sdp!=='string'||value.sdp.length>131072)throw Error('Invalid local test SDP');
        const lines=value.sdp.split(/\r?\n/);let video=false;const codecs=[],h264Formats=[];
        for(let index=0;index<lines.length;index++){
            if(lines[index].startsWith('m='))video=lines[index].startsWith('m=video ');
            const match=video?/^a=rtpmap:(\d+) (VP8|VP9|H264|AV1)\/90000$/i.exec(lines[index]):null;
            if(!match)continue;
            const payload=match[1],prefix='a=fmtp:'+payload+' ';
            let end=index+1;while(end<lines.length&&!lines[end].startsWith('m='))end++;
            const existing=lines.findIndex((line,at)=>at>index&&at<end&&line.startsWith(prefix));
            const format=match[2].toUpperCase()==='H264'?h264Format(existing>=0?lines[existing].slice(prefix.length):null):null;
            if(h264Profile!=='Auto'&&(!format||format.profileLevelId!==h264Profile||format.packetizationMode!==1))
                throw Error('Negotiation did not retain the exact H264 profile and packetization mode');
            if(format)h264Formats.push(format);
            if(existing>=0){
                const parameters=lines[existing].slice(prefix.length).split(';').filter(item=>!/^x-google-max-bitrate=/i.test(item.trim()));
                parameters.push('x-google-max-bitrate='+maximum/1000);lines[existing]=prefix+parameters.join(';');
            }else{lines.splice(index+1,0,prefix+'x-google-max-bitrate='+maximum/1000);}
            codecs.push(match[2].toUpperCase());
        }
        if(codecs.length===0)throw Error('No admitted video codec in local test SDP');
        return {description:{type:value.type,sdp:lines.join('\r\n')},codecs:[...new Set(codecs)],h264Formats};
    }
    async function sender(connection,frameRate){
        if(![15,30,60].includes(frameRate))throw Error('Unsupported local test frame rate');
        const senders=connection.getSenders().filter(item=>item.track?.kind==='video');
        if(senders.length!==1)throw Error('Expected one local video sender');
        const current=senders[0].getParameters();
        if(!Array.isArray(current.encodings)||current.encodings.length!==1)throw Error('Expected one local video encoding');
        current.encodings=current.encodings.map(value=>({...value,maxBitrate:maximum,maxFramerate:frameRate}));
        await senders[0].setParameters(current);
        const observed=senders[0].getParameters(),encoding=observed.encodings?.[0];
        if(encoding?.maxBitrate!==maximum||encoding?.maxFramerate!==frameRate)throw Error('Local sender did not accept selected limits');
        return {maxBitrate:encoding.maxBitrate,maxFramerate:encoding.maxFramerate,
            scaleResolutionDownBy:Number.isFinite(encoding.scaleResolutionDownBy)?encoding.scaleResolutionDownBy:null,
            degradationPreference:['maintain-resolution','maintain-framerate','balanced'].includes(observed.degradationPreference)?observed.degradationPreference:null};
    }
    function observe(connection){
        try {
            const senders=connection.getSenders().filter(item=>item.track?.kind==='video');
            if(senders.length!==1)return null;
            const parameters=senders[0].getParameters(),encoding=parameters.encodings?.[0];
            if(!Array.isArray(parameters.encodings)||parameters.encodings.length!==1)return null;
            const number=value=>Number.isFinite(value)&&value>=0?value:null;
            return {maxBitrate:number(encoding.maxBitrate),maxFramerate:number(encoding.maxFramerate),
                scaleResolutionDownBy:number(encoding.scaleResolutionDownBy),
                degradationPreference:['maintain-resolution','maintain-framerate','balanced'].includes(parameters.degradationPreference)?parameters.degradationPreference:null,
                contentHint:['','detail','motion','text'].includes(senders[0].track.contentHint)?senders[0].track.contentHint:null};
        }catch{return null;}
    }
    globalThis.captureProbePolicy=Object.freeze({description,sender,observe,chooseCodec,h264Format,reference:Object.freeze({maximumBitrate:maximum,requestedCodec:codec,requestedH264Profile:h264Profile,
        providerMinimumBitrate:500000,providerTargetBitrate:600000,providerMinimumAndTargetApplied:false})});
})();
