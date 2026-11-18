#pragma once
#include "CaptureVideoEncoder.h"
#include "EncodedReplayBuffer.h"
#include <functional>

namespace widgetrail::capture {
// Encoded callbacks copy only the compressed packet into the shared bounded
// ring. Capture acquisition, authorization and file destination stay with callers.
class ReplaySampleOutput final : public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>, IMFSampleGrabberSinkCallback2> {
public:
    EncodedReplayBuffer buffer;
    ReplaySampleOutput(bool audio,int64_t duration,size_t bytes):buffer(duration,bytes,audio){}
    std::atomic_size_t encodedCount{};
    std::atomic_size_t audioCount{},audioDropped{};
    std::atomic<HRESULT> failure{S_OK};
    std::atomic<ReplayBufferFailure> bufferFailure{ReplayBufferFailure::None};
    std::atomic_size_t videoDropped{};
    IFACEMETHOD(OnClockStart)(MFTIME,LONGLONG) override{return S_OK;}
    IFACEMETHOD(OnClockStop)(MFTIME) override{return S_OK;}
    IFACEMETHOD(OnClockPause)(MFTIME) override{return S_OK;}
    IFACEMETHOD(OnClockRestart)(MFTIME) override{return S_OK;}
    IFACEMETHOD(OnClockSetRate)(MFTIME,float) override{return S_OK;}
    IFACEMETHOD(OnSetPresentationClock)(IMFPresentationClock*) override{return S_OK;}
    IFACEMETHOD(OnShutdown)() override{return S_OK;}
    IFACEMETHOD(OnProcessSample)(REFGUID,DWORD,LONGLONG,LONGLONG,const BYTE*,DWORD) override{return E_NOTIMPL;}
    IFACEMETHOD(OnProcessSampleEx)(REFGUID major,DWORD,LONGLONG time,LONGLONG duration,const BYTE* data,DWORD count,IMFAttributes* attributes) override {
        try {
            if((major!=MFMediaType_Video && major!=MFMediaType_Audio) || time<0 || duration<=0 || !data || count==0){failure=E_INVALIDARG;return E_INVALIDARG;}
            UINT32 clean{};UINT64 decode=static_cast<UINT64>(time);
            if(attributes){attributes->GetUINT32(MFSampleExtension_CleanPoint,&clean);attributes->GetUINT64(MFSampleExtension_DecodeTimestamp,&decode);}
            const bool audio=major==MFMediaType_Audio;
            const auto accepted=buffer.Push(audio?ReplayStream::Audio:ReplayStream::Video,time,duration,static_cast<LONGLONG>(decode),audio||clean!=0,data,count);
            if(audio){audioCount.fetch_add(1);if(!accepted)audioDropped.fetch_add(1);}else{encodedCount.fetch_add(1);if(!accepted)videoDropped.fetch_add(1);}
            return S_OK;
        }catch(const ReplayBufferException& error){bufferFailure=error.failure;failure=MF_E_INVALIDMEDIATYPE;return MF_E_INVALIDMEDIATYPE;}
        catch(const std::bad_alloc&){failure=E_OUTOFMEMORY;return E_OUTOFMEMORY;}
        catch(...){failure=E_FAIL;return E_FAIL;}
    }
};

inline ComPtr<IMFMediaSink> CreateReplaySampleSink(IMFMediaType* type,ReplaySampleOutput* samples) {
    ComPtr<IMFActivate> activation;winrt::check_hresult(MFCreateSampleGrabberSinkActivate(type,samples,&activation));
    winrt::check_hresult(activation->SetUINT32(MF_SAMPLEGRABBERSINK_IGNORE_CLOCK,TRUE));
    ComPtr<IMFMediaSink> sink;winrt::check_hresult(activation->ActivateObject(IID_PPV_ARGS(&sink)));return sink;
}

// Single consuming write. Keep packet metadata for the result, but release each
// snapshot payload after it has entered the sink so the live ring can reclaim it.
inline void WriteReplayMp4Core(const wchar_t* path,IMFByteStream* byteStream,IMFMediaType* type,IMFMediaType* audioType,ReplaySnapshot& clip,
    const std::function<void(size_t)>& afterPacket={}) {
    winrt::check_hresult(MFStartup(MF_VERSION));
    struct Foundation {~Foundation(){MFShutdown();}} foundation;
    auto& packets=clip.packets;
    if(std::any_of(packets.begin(),packets.end(),[](const auto& packet){return !packet.payload;}))throw winrt::hresult_error(E_ILLEGAL_METHOD_CALL);
    const auto first=std::find_if(packets.begin(),packets.end(),[](const auto& packet){return packet.stream==ReplayStream::Video;});
    if(first==packets.end() || !first->key)throw winrt::hresult_error(E_UNEXPECTED);
    ComPtr<IMFAttributes> options;winrt::check_hresult(MFCreateAttributes(&options,2));
    winrt::check_hresult(options->SetUINT32(MF_READWRITE_DISABLE_CONVERTERS,TRUE));
    winrt::check_hresult(options->SetGUID(MF_TRANSCODE_CONTAINERTYPE,MFTranscodeContainerType_MPEG4));
    ComPtr<IMFSinkWriter> output;winrt::check_hresult(MFCreateSinkWriterFromURL(path,byteStream,options.Get(),&output));
    DWORD stream{};winrt::check_hresult(output->AddStream(type,&stream));
    winrt::check_hresult(output->SetInputMediaType(stream,type,nullptr));
    DWORD audioStream{};
    if(audioType){winrt::check_hresult(output->AddStream(audioType,&audioStream));winrt::check_hresult(output->SetInputMediaType(audioStream,audioType,nullptr));}
    winrt::check_hresult(output->BeginWriting());
    const auto start=std::min(first->time,first->decode);
    for(auto& packet:packets) {
        // Compressed AAC cannot be sample-trimmed without re-encoding. Keep
        // complete frames contained inside the video interval, with their real
        // timestamp. Both edge gaps are at most one AAC frame; audio cannot
        // lengthen a clip beyond the hard requested-duration bound.
        if(packet.stream==ReplayStream::Audio && (!audioType || packet.time<start || packet.time+packet.duration>clip.end)){packet.payload.reset();continue;}
        const auto& payload=packet.payload->bytes;
        const auto byteCount=payload.size();
        ComPtr<IMFMediaBuffer> buffer;winrt::check_hresult(MFCreateMemoryBuffer(static_cast<DWORD>(payload.size()),&buffer));
        BYTE* bytes{};winrt::check_hresult(buffer->Lock(&bytes,nullptr,nullptr));memcpy(bytes,payload.data(),payload.size());
        winrt::check_hresult(buffer->Unlock());winrt::check_hresult(buffer->SetCurrentLength(static_cast<DWORD>(payload.size())));
        ComPtr<IMFSample> sample;winrt::check_hresult(MFCreateSample(&sample));winrt::check_hresult(sample->AddBuffer(buffer.Get()));
        winrt::check_hresult(sample->SetSampleTime(packet.time-start));winrt::check_hresult(sample->SetSampleDuration(packet.duration));
        winrt::check_hresult(sample->SetUINT32(MFSampleExtension_CleanPoint,packet.key));
        winrt::check_hresult(sample->SetUINT64(MFSampleExtension_DecodeTimestamp,static_cast<UINT64>(packet.decode-start)));
        winrt::check_hresult(output->WriteSample(packet.stream==ReplayStream::Audio?audioStream:stream,sample.Get()));
        packet.payload.reset();
        if(afterPacket)afterPacket(byteCount);
    }
    winrt::check_hresult(output->Finalize());
}
inline void WriteReplayMp4(const wchar_t* path,IMFMediaType* type,IMFMediaType* audioType,ReplaySnapshot& clip,
    const std::function<void(size_t)>& afterPacket={}) {
    WriteReplayMp4Core(path,nullptr,type,audioType,clip,afterPacket);
}
inline void WriteReplayMp4Stream(IMFByteStream* stream,IMFMediaType* type,ReplaySnapshot& clip) {
    if(!stream)throw winrt::hresult_error(E_INVALIDARG);
    WriteReplayMp4Core(nullptr,stream,type,nullptr,clip);
}

}
