#define CAPTURE_WORKER_HOST_EXPORTS
#include "CaptureWorkerProtocol.h"
#include "CaptureWorkerLifetime.h"
#include <WebView2.h>
#include <WebView2Interop.h>
#include <wrl/client.h>
#include <vector>
#include <string>
#include <memory>
#include <mutex>
#include <new>

namespace {
using Microsoft::WRL::ComPtr;
using namespace capture_worker;
using namespace widgetrail::capture::worker;
struct Session {
    Handle job,process,reader,controlMapping,dataReady;
    Control* control{};
    DWORD processId{};
    alignas(8) volatile LONG64 pendingVideo{},pendingAudio{};
    uint64_t lastSequence{};
    std::mutex stopGate;
    StopResult stopped{};
    ~Session(){if(control)UnmapViewOfFile(control);}
};
struct Buffers {
    ComPtr<ICoreWebView2SharedBuffer> video,audio;
    ~Buffers(){if(video)video->Close();if(audio)audio->Close();}
};
ComPtr<ICoreWebView2> NativeCore(IUnknown* projected){
    if(!projected)throw E_INVALIDARG;
    ComPtr<ICoreWebView2Interop2> interop;
    const auto query=projected->QueryInterface(IID_PPV_ARGS(&interop));if(FAILED(query))throw query;
    ComPtr<ICoreWebView2> core;const auto hr=interop->GetComICoreWebView2(&core);if(FAILED(hr))throw hr;return core;
}
}

int32_t __stdcall WrailCaptureWorkerStart(const wchar_t* executable,const Configuration* configuration,
    uint64_t videoMapping,uint32_t videoBytes,uint64_t audioMapping,uint32_t audioBytes,void** result) noexcept {
    if(!result)return E_POINTER;*result=nullptr;
    if(!executable||!configuration||!Valid(*configuration)||!videoMapping||
        videoBytes!=uint64_t(configuration->maximumWidth)*configuration->maximumHeight*4+((configuration->flags&NativeH264)?H264Extra:0)||
        (configuration->includeAudio?(audioMapping==0||audioBytes!=AudioBytes):(audioMapping!=0||audioBytes!=0)))return E_INVALIDARG;
    try{
        auto session=std::make_unique<Session>();
        CreateJob(session->job);
        session->controlMapping.value=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(Control),nullptr);
        if(!session->controlMapping.value)throw Error();
        session->control=static_cast<Control*>(MapViewOfFile(session->controlMapping.value,FILE_MAP_READ|FILE_MAP_WRITE,0,0,sizeof(Control)));
        if(!session->control)throw Error();
        new(session->control) Control();session->control->configuration=*configuration;
        InterlockedExchange64(&session->control->heartbeat,static_cast<LONG64>(GetTickCount64()));
        session->dataReady.value=CreateEventW(nullptr,FALSE,FALSE,nullptr);if(!session->dataReady.value)throw Error();
        Handle control,video,audio,writer,ready;
        Inheritable(session->controlMapping.value,control);
        Inheritable(reinterpret_cast<HANDLE>(videoMapping),video);
        if(configuration->includeAudio)Inheritable(reinterpret_cast<HANDLE>(audioMapping),audio);
        Inheritable(session->dataReady.value,ready);
        SECURITY_ATTRIBUTES security{sizeof(security),nullptr,TRUE};
        Check(CreatePipe(&session->reader.value,&writer.value,&security,4096));
        Check(SetHandleInformation(session->reader.value,HANDLE_FLAG_INHERIT,0));
        std::vector<HANDLE> inherited{control.value,video.value,writer.value,ready.value};
        if(audio.value)inherited.push_back(audio.value);
        Launch(executable,Hex(control.value)+L" "+Hex(video.value)+L" "+Hex(audio.value)+L" "+Hex(writer.value)+L" "+Hex(ready.value),
            inherited,session->job,session->process,session->processId);
        *result=session.release();return S_OK;
    }catch(HRESULT hr){return hr;}catch(...){return E_FAIL;}
}

int32_t __stdcall WrailCaptureWorkerRead(void* value,Packet* packet,uint32_t timeout) noexcept {
    if(!value||!packet||timeout>1000)return E_INVALIDARG;
    const auto session=static_cast<Session*>(value);const auto deadline=GetTickCount64()+timeout;
    for(;;){
        DWORD available{};
        if(!PeekNamedPipe(session->reader.value,nullptr,0,nullptr,&available,nullptr))return Error();
        if(available>=sizeof(Packet)){
            DWORD read{};if(!ReadFile(session->reader.value,packet,sizeof(Packet),&read,nullptr))return Error();
            if(read!=sizeof(Packet)||packet->magic!=Magic||packet->version!=Version||packet->epoch!=session->control->configuration.epoch||packet->sequence<=session->lastSequence||
                static_cast<uint32_t>(packet->kind)<1||static_cast<uint32_t>(packet->kind)>4)return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            const auto& configuration=session->control->configuration;
            if(packet->kind==Kind::Video&&(packet->width<2||packet->height<2||packet->width>configuration.maximumWidth||packet->height>configuration.maximumHeight||
                packet->width%2||packet->height%2||packet->bytes!=uint64_t(packet->width)*packet->height*4))return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            if(packet->kind==Kind::Audio&&(!configuration.includeAudio||packet->width!=48000||packet->height!=2||!packet->bytes||packet->bytes>AudioBytes||packet->bytes%4))
                return HRESULT_FROM_WIN32(ERROR_INVALID_DATA);
            session->lastSequence=packet->sequence;
            if(packet->kind==Kind::Video)InterlockedExchange64(&session->pendingVideo,static_cast<LONG64>(packet->sequence));
            else if(packet->kind==Kind::Audio)InterlockedExchange64(&session->pendingAudio,static_cast<LONG64>(packet->sequence));
            MemoryBarrier();return S_OK;
        }
        if(WaitForSingleObject(session->process.value,0)==WAIT_OBJECT_0)return HRESULT_FROM_WIN32(ERROR_BROKEN_PIPE);
        const auto now=GetTickCount64();if(now>=deadline)return S_FALSE;
        // Always drain the bounded pipe first: auto-reset signals may coalesce,
        // and a terminal packet can be queued when process exit also signals.
        // A write between Peek and Wait leaves this event signaled, so no wake
        // is lost. No scheduler-granularity polling sits in the frame path.
        HANDLE handles[]{session->dataReady.value,session->process.value};
        const auto waited=WaitForMultipleObjects(2,handles,FALSE,static_cast<DWORD>(deadline-now));
        if(waited!=WAIT_OBJECT_0&&waited!=WAIT_OBJECT_0+1&&waited!=WAIT_TIMEOUT)return Error();
    }
}
int32_t __stdcall WrailCaptureWorkerAcknowledge(void* value,uint64_t epoch,uint32_t kind,uint64_t sequence) noexcept {
    if(!value||!sequence||kind<1||kind>2)return E_INVALIDARG;
    const auto session=static_cast<Session*>(value);
    if(epoch!=session->control->configuration.epoch||InterlockedCompareExchange(&session->control->stop,0,0))return RO_E_CLOSED;
    auto pending=kind==1?&session->pendingVideo:&session->pendingAudio;
    if(InterlockedCompareExchange64(pending,0,static_cast<LONG64>(sequence))!=static_cast<LONG64>(sequence))return S_FALSE;
    InterlockedExchange(kind==1?&session->control->videoCredit:&session->control->audioCredit,1);return S_OK;
}
int32_t __stdcall WrailCaptureWorkerEncoding(void* value,uint64_t epoch,uint32_t generation,uint32_t bitrate,uint32_t key) noexcept {
    if(!value||generation>INT32_MAX||bitrate>INT32_MAX||key>1)return E_INVALIDARG;
    auto c=static_cast<Session*>(value)->control;
    if(!(c->configuration.flags&NativeH264)||epoch!=c->configuration.epoch||InterlockedCompareExchange(&c->stop,0,0))return RO_E_CLOSED;
    InterlockedExchange64(&c->encoding,(static_cast<LONG64>(generation)<<32)|bitrate);
    if(key)InterlockedIncrement(&c->keyframeRequests);
    return S_OK;
}
int32_t __stdcall WrailCaptureWorkerHeartbeat(void* value,uint64_t epoch) noexcept {
    if(!value)return E_INVALIDARG;const auto session=static_cast<Session*>(value);
    if(epoch!=session->control->configuration.epoch||InterlockedCompareExchange(&session->control->stop,0,0))return RO_E_CLOSED;
    InterlockedExchange64(&session->control->heartbeat,static_cast<LONG64>(GetTickCount64()));return S_OK;
}
int32_t __stdcall WrailCaptureWorkerStop(void* value,uint32_t timeout,StopResult* result) noexcept {
    if(!value||!result||timeout>2000)return E_INVALIDARG;const auto session=static_cast<Session*>(value);
    std::lock_guard lock(session->stopGate);
    InterlockedExchange(&session->control->stop,1);session->stopped.processId=session->processId;
    if(WaitForSingleObject(session->process.value,timeout)!=WAIT_OBJECT_0){
        session->stopped.forced=1;
        if(!TerminateJobObject(session->job.value,ERROR_OPERATION_ABORTED)){session->stopped.result=Error();*result=session->stopped;return session->stopped.result;}
    }
    session->stopped.exited=WaitForSingleObject(session->process.value,250)==WAIT_OBJECT_0;
    session->stopped.result=session->stopped.exited?S_OK:HRESULT_FROM_WIN32(ERROR_TIMEOUT);
    *result=session->stopped;return session->stopped.result;
}
uint32_t __stdcall WrailCaptureWorkerProcessId(void* value) noexcept {return value?static_cast<Session*>(value)->processId:0;}
void __stdcall WrailCaptureWorkerRelease(void* value) noexcept {
    if(!value)return;
    StopResult ignored{};WrailCaptureWorkerStop(value,0,&ignored);delete static_cast<Session*>(value);
}

int32_t __stdcall WrailCaptureBuffersCreate(IUnknown* projected,uint32_t videoBytes,uint32_t audioBytes,void** result) noexcept {
    if(!result)return E_POINTER;*result=nullptr;
    if(videoBytes==0||videoBytes>MaximumLiveCaptureVideoBytes+H264Extra||audioBytes>AudioBytes)return E_INVALIDARG;
    try{
        auto core=NativeCore(projected);ComPtr<ICoreWebView2_2> core2;auto hr=core.As(&core2);if(FAILED(hr))return hr;
        ComPtr<ICoreWebView2Environment> environment;hr=core2->get_Environment(&environment);if(FAILED(hr))return hr;
        ComPtr<ICoreWebView2Environment12> environment12;hr=environment.As(&environment12);if(FAILED(hr))return hr;
        auto buffers=std::make_unique<Buffers>();hr=environment12->CreateSharedBuffer(videoBytes,&buffers->video);if(FAILED(hr))return hr;
        if(audioBytes){hr=environment12->CreateSharedBuffer(audioBytes,&buffers->audio);if(FAILED(hr))return hr;}
        *result=buffers.release();return S_OK;
    }catch(HRESULT hr){return hr;}catch(...){return E_FAIL;}
}
int32_t __stdcall WrailCaptureBuffersMappings(void* value,uint64_t* video,uint64_t* audio) noexcept {
    if(!value||!video||!audio)return E_INVALIDARG;
    const auto buffers=static_cast<Buffers*>(value);HANDLE v{},a{};
    auto hr=buffers->video->get_FileMappingHandle(&v);if(FAILED(hr))return hr;
    if(buffers->audio){hr=buffers->audio->get_FileMappingHandle(&a);if(FAILED(hr))return hr;}
    *video=reinterpret_cast<uintptr_t>(v);*audio=reinterpret_cast<uintptr_t>(a);return S_OK;
}
int32_t __stdcall WrailCaptureBuffersPost(IUnknown* projected,void* value,uint32_t kind,const wchar_t* metadata) noexcept {
    if(!value||!metadata||kind<1||kind>2)return E_INVALIDARG;
    try{
        const auto buffers=static_cast<Buffers*>(value);const auto buffer=kind==1?buffers->video.Get():buffers->audio.Get();if(!buffer)return E_INVALIDARG;
        auto core=NativeCore(projected);ComPtr<ICoreWebView2_17> core17;const auto hr=core.As(&core17);if(FAILED(hr))return hr;
        return core17->PostSharedBufferToScript(buffer,COREWEBVIEW2_SHARED_BUFFER_ACCESS_READ_ONLY,metadata);
    }catch(HRESULT hr){return hr;}catch(...){return E_FAIL;}
}
void __stdcall WrailCaptureBuffersRelease(void* value) noexcept {delete static_cast<Buffers*>(value);}
