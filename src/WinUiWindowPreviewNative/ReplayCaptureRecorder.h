#pragma once
#include "ReplaySampleOutput.h"
#include "CaptureAudioTimeline.h"
#include "CaptureFrameTiming.h"
#include <limits>
#include <optional>

namespace widgetrail::capture {
enum class ReplayRecorderPhase { Starting, Recording, Paused, Finished };
struct ReplayRecorderStatus {
    ReplayRecorderPhase phase{};
    ReplayBufferStatus buffer{};
    uint64_t encodedFrames{},frameGaps{},encoderDrops{},audioPackets{},pausedAudioPackets{},compressedVideoDrops{};
    UINT width{},height{},sourceWidth{},sourceHeight{};
    HRESULT error{S_OK};ReplayBufferFailure bufferFailure{};
};

struct ReplayPreparedSave {
    struct Foundation {Foundation(){winrt::check_hresult(MFStartup(MF_VERSION));}~Foundation(){MFShutdown();}} foundation;
    std::shared_ptr<ReplaySnapshot> snapshot;
    ComPtr<IMFMediaType> video,audio;
    UINT width{},height{},sourceWidth{},sourceHeight{},framesPerSecond{};
    bool hasAudio{};
};

// One capture-thread owner; MF callbacks only touch ReplaySampleOutput's atomics
// and locked compressed ring. An independent save thread receives frozen types
// and a shared bounded snapshot, never the running encoder or GPU texture.
class ReplayCaptureRecorder {
    const DWORD thread=GetCurrentThreadId();
    WrailCaptureOptions options;
    std::optional<CaptureEncodingProfile> encoding;
    const bool withAudio;
    bool foundation{},finished{},paused{};
    ComPtr<ReplaySampleOutput> samples;
    ComPtr<IMFMediaSink> videoSink,audioSink;
    std::unique_ptr<VideoEncoder> encoder;
    std::unique_ptr<CaptureAudioTimeline> audio;
    uint64_t origin{},segmentBase{},lastIndex{},encodedFrames{},frameGaps{},encoderDrops{},audioPackets{},pausedAudioPackets{};
    int64_t lastRawTime{};
    UINT width{},height{},sourceWidth{},sourceHeight{};
#ifdef CAPTURE_WORKER_VALIDATION
    uint64_t callbacks{},duplicateSlots{},shortIntervals{},regularIntervals{},longIntervals{};
    int64_t firstCallbackTime{},lastCallbackTime{};
#endif
    void Own() const {if(GetCurrentThreadId()!=thread||finished)throw winrt::hresult_error(RO_E_CLOSED);winrt::check_hresult(samples->failure.load());}
    uint64_t SegmentAudioEnd() const {return encodedFrames&&lastIndex>=segmentBase?(lastIndex-segmentBase+1)*VideoEncoder::AudioRate/options.framesPerSecond:0;}
    static ComPtr<IMFMediaType> Freeze(IMFMediaType* source){ComPtr<IMFMediaType> result;winrt::check_hresult(MFCreateMediaType(&result));winrt::check_hresult(source->CopyAllItems(result.Get()));return result;}
public:
    ReplayCaptureRecorder(const WrailCaptureOptions& settings,int durationSeconds,size_t budget,const CaptureEncodingProfile* profile=nullptr)
        :options(settings),encoding(profile?std::optional<CaptureEncodingProfile>(*profile):std::nullopt),withAudio(settings.audioMode==1) {
        if(options.encoder!=2 || (options.framesPerSecond!=15&&options.framesPerSecond!=30&&options.framesPerSecond!=60) ||
            options.audioMode>1 || options.bitrate<100000 || options.bitrate>100000000 || durationSeconds<1 || durationSeconds>120 ||
            budget<1024*1024 || budget>256u*1024*1024)throw winrt::hresult_error(E_INVALIDARG);
        winrt::check_hresult(MFStartup(MF_VERSION));foundation=true;
        // Leave a conservative per-packet/container allowance at the maximum
        // saved-file size; the configured budget remains an upper memory bound.
        constexpr size_t muxReserve=32768u*128u+128u*1024u;
        samples=Microsoft::WRL::Make<ReplaySampleOutput>(withAudio,int64_t(durationSeconds)*10000000,std::min<size_t>(budget,256u*1024*1024-muxReserve));
        if(!samples){MFShutdown();foundation=false;throw std::bad_alloc();}
    }
    ~ReplayCaptureRecorder(){audio.reset();encoder.reset();if(audioSink)audioSink->Shutdown();if(videoSink)videoSink->Shutdown();
        audioSink.Reset();videoSink.Reset();samples.Reset();if(foundation)MFShutdown();}
    ReplayCaptureRecorder(const ReplayCaptureRecorder&)=delete;
    ReplayCaptureRecorder& operator=(const ReplayCaptureRecorder&)=delete;

    void Pause() {
        Own();if(paused)return;
        if(audio){audio->Finish(SegmentAudioEnd());audio.reset();}
        origin=0;lastRawTime=0;paused=true;
    }
    void Audio(uint32_t kind,int64_t timestamp,const void* bytes,uint32_t count) {
        Own();
        if(kind==3){Pause();return;}
        if(kind!=2 || !withAudio || timestamp<0 || timestamp>INT64_MAX/10 || !bytes || count==0 || count%4 || count>192000)
            throw winrt::hresult_error(E_INVALIDARG);
        ++audioPackets;
        if(!audio || !origin || paused){++pausedAudioPackets;return;}
        const auto pcm=static_cast<const int16_t*>(bytes);
        std::deque<CapturedAudioPacket> packets;
        packets.push_back({static_cast<uint64_t>(timestamp)*10,std::vector<int16_t>(pcm,pcm+count/2)});
        audio->Queue(std::move(packets));
    }
    bool Frame(UINT w,UINT h,UINT sourceW,UINT sourceH,int64_t timestamp,ID3D11Texture2D* texture) {
        Own();
        if(!texture || w<2 || h<2 || w>8192 || h>8192 || uint64_t(w)*h>3840u*2160u || w%2 || h%2 || timestamp<0 || timestamp>INT64_MAX/10)
            throw winrt::hresult_error(E_INVALIDARG);
        if(!encoder) {
            ComPtr<ID3D11Device> device;texture->GetDevice(&device);width=w;height=h;sourceWidth=sourceW;sourceHeight=sourceH;
            videoSink=CreateReplaySampleSink(VideoEncoder::VideoOutputType(w,h,options,encoding?&*encoding:nullptr).Get(),samples.Get());
            if(withAudio)audioSink=CreateReplaySampleSink(VideoEncoder::AudioOutputType().Get(),samples.Get());
            encoder=std::make_unique<VideoEncoder>(nullptr,w,h,options,device.Get(),withAudio,videoSink.Get(),audioSink.Get(),encoding?&*encoding:nullptr);
            // Codec warmup can take time. Wait for the next genuinely fresh
            // frame instead of starting with an old image and newer audio.
            return false;
        }
#ifdef CAPTURE_WORKER_VALIDATION
        ++callbacks;
        if(!firstCallbackTime)firstCallbackTime=timestamp;
        if(lastCallbackTime){const auto delta=timestamp-lastCallbackTime;
            if(delta<750000/options.framesPerSecond)++shortIntervals;
            else if(delta>1250000/options.framesPerSecond)++longIntervals;else ++regularIntervals;}
        lastCallbackTime=timestamp;
#endif
        if(width!=w||height!=h)throw winrt::hresult_error(MF_E_INVALIDMEDIATYPE);
        if(lastRawTime && timestamp<lastRawTime)throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_INVALID_TIME));
        if(lastRawTime && timestamp-lastRawTime>2000000)Pause(); // sleep/device scheduling discontinuity
        if(!origin) {
            segmentBase=encodedFrames?lastIndex+1:0;origin=static_cast<uint64_t>(timestamp)*10;paused=false;
            if(withAudio)audio=std::make_unique<CaptureAudioTimeline>(*encoder,origin,UINT64_MAX/2);
        }
        lastRawTime=timestamp;
        const auto elapsed=static_cast<uint64_t>(timestamp)*10-origin;
        const auto selected=CaptureFrameIndex(elapsed,options.framesPerSecond,segmentBase,lastIndex,encodedFrames!=0);
        if(!selected){
#ifdef CAPTURE_WORKER_VALIDATION
            ++duplicateSlots;
#endif
            return false;
        }
        const auto frame=*selected;
        if(frame>=UINT_MAX)throw winrt::hresult_error(E_BOUNDS);
        const bool accepted=encoder->TryWriteTexture(texture,static_cast<UINT>(frame));
        if(accepted){if(encodedFrames && frame>lastIndex+1)frameGaps+=frame-lastIndex-1;lastIndex=frame;++encodedFrames;}
        else ++encoderDrops;
        if(audio && encodedFrames) {
            const auto covered=SegmentAudioEnd();
            // Empty game audio produces no packets on some devices. Fill only
            // behind a 100 ms arrival watermark; never queue minutes of silence.
            audio->FlushThrough(covered,covered>VideoEncoder::AudioRate/10?covered-VideoEncoder::AudioRate/10:0);
        }
        winrt::check_hresult(samples->failure.load());return accepted;
    }
    ReplayRecorderStatus Status() const {
        if(GetCurrentThreadId()!=thread)throw winrt::hresult_error(E_UNEXPECTED);
        return {finished?ReplayRecorderPhase::Finished:paused?ReplayRecorderPhase::Paused:origin?ReplayRecorderPhase::Recording:ReplayRecorderPhase::Starting,
            samples->buffer.Status(),encodedFrames,frameGaps,encoderDrops,audioPackets,pausedAudioPackets,samples->videoDropped.load(),width,height,sourceWidth,sourceHeight,
            samples->failure.load(),samples->bufferFailure.load()};
    }
#ifdef CAPTURE_WORKER_VALIDATION
    template<class Diagnostic> Diagnostic Diagnostics() const {
        if(GetCurrentThreadId()!=thread)throw winrt::hresult_error(E_UNEXPECTED);
        return {callbacks,encodedFrames,duplicateSlots,encoderDrops,samples->videoDropped.load(),frameGaps,
            firstCallbackTime,lastCallbackTime,shortIntervals,regularIntervals,longIntervals};
    }
#endif
    std::shared_ptr<ReplayPreparedSave> PrepareSave(int durationSeconds) {
        if(GetCurrentThreadId()!=thread)throw winrt::hresult_error(E_UNEXPECTED);
        if(durationSeconds<1 || durationSeconds>120)throw winrt::hresult_error(E_INVALIDARG);
        if(!encoder)return {};
        auto snapshot=samples->buffer.Snapshot(int64_t(durationSeconds)*10000000);if(!snapshot)return {};
        if(withAudio && std::none_of(snapshot->packets.begin(),snapshot->packets.end(),[&](const auto& packet){return packet.stream==ReplayStream::Audio &&
            packet.time>=snapshot->start && packet.time+packet.duration<=snapshot->end;}))return {};
        auto result=std::make_shared<ReplayPreparedSave>();result->snapshot=std::move(snapshot);
        result->video=Freeze(encoder->EncodedType().Get());if(withAudio)result->audio=Freeze(encoder->EncodedAudioType().Get());
        result->width=width;result->height=height;result->sourceWidth=sourceWidth;result->sourceHeight=sourceHeight;
        result->framesPerSecond=options.framesPerSecond;result->hasAudio=withAudio;return result;
    }
    void Finish() {
        if(GetCurrentThreadId()!=thread)throw winrt::hresult_error(E_UNEXPECTED);
        if(finished)return;
        if(audio)audio->Finish(SegmentAudioEnd());
        if(encoder)encoder->Finish();finished=true;
    }
};
}
