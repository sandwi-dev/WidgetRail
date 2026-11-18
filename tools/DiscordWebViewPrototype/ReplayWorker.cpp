#include "ReplayWorkerProtocol.h"
#include "LiveCaptureTexture.h"
#include "CapturePacer.h"
#include "../../src/WinUiWindowPreviewNative/ReplayCaptureRecorder.h"
#include <future>
#include <chrono>
#include <thread>

namespace {
using namespace replay_worker;
using namespace widgetrail::capture;
struct Mapping {
    HANDLE handle{};Control* value{};
    explicit Mapping(uint64_t h):handle(reinterpret_cast<HANDLE>(h)),value(static_cast<Control*>(MapViewOfFile(handle,FILE_MAP_READ|FILE_MAP_WRITE,0,0,sizeof(Control)))){}
    ~Mapping(){if(value)UnmapViewOfFile(value);if(handle)CloseHandle(handle);}
};
struct SaveResult {
    SaveOutcome outcome{SaveOutcome::Failed};Issue issue{Issue::Storage};HRESULT error{E_FAIL};
    uint32_t width{},height{},sourceWidth{},sourceHeight{},frames{},missed{},audio{};
    uint64_t bytes{};int64_t duration{};
};
struct Worker {
    Control& control;HANDLE ready;std::optional<CaptureEncodingProfile> encoding;ReplayCaptureRecorder recorder;State state;bool sourceVerified{};
    std::future<SaveResult> saving;uint64_t saveStarted{},lastPublish{};bool stopping{};
    static WrailCaptureOptions Options(const Configuration& c){WrailCaptureOptions options{};
        options.encoder=2;options.framesPerSecond=c.framesPerSecond;options.bitrate=c.bitrate;options.audioMode=c.audioMode;return options;}
    static std::optional<CaptureEncodingProfile> Encoding(const Configuration& c){
        if(!c.codec)return std::nullopt;
        return CaptureEncodingProfile{static_cast<CaptureVideoCodec>(c.codec),static_cast<CapturePixelFormat>(c.pixelFormat),static_cast<CaptureRateControl>(c.rateControl),c.quality};
    }
    Worker(Control& c,HANDLE event):control(c),ready(event),encoding(Encoding(c.configuration)),recorder(Options(c.configuration),static_cast<int>(c.configuration.durationSeconds),static_cast<size_t>(c.configuration.memoryBytes),encoding?&*encoding:nullptr){
        state.epoch=c.configuration.epoch;state.codec=c.configuration.codec?c.configuration.codec:1;state.pixelFormat=c.configuration.pixelFormat?c.configuration.pixelFormat:1;
        state.rateControl=c.configuration.rateControl;state.quality=c.configuration.quality;Publish(true);
    }
    bool Authority() const {return !InterlockedCompareExchange(&control.abort,0,0)&&
        GetTickCount64()-static_cast<uint64_t>(InterlockedCompareExchange64(&control.heartbeat,0,0))<10000;}
    void Publish(bool force=false) {
        const auto now=GetTickCount64();if(!force&&now-lastPublish<100)return;lastPublish=now;
        const auto status=recorder.Status();state.bufferedTicks=status.buffer.duration;state.residentBytes=status.buffer.residentBytes;
        state.droppedFrames=status.frameGaps+status.compressedVideoDrops;state.width=status.width;state.height=status.height;
        state.sourceWidth=status.sourceWidth;state.sourceHeight=status.sourceHeight;
        if(state.phase!=Phase::Failed && state.phase!=Phase::Ended)state.phase=stopping?Phase::Stopping:
            status.phase==ReplayRecorderPhase::Paused?Phase::Paused:status.phase==ReplayRecorderPhase::Recording?Phase::Recording:Phase::Starting;
        state.publishedTick=now;++state.revision;
        InterlockedIncrement64(&control.stateSequence);MemoryBarrier();memcpy(&control.state,&state,sizeof(state));MemoryBarrier();
#ifdef CAPTURE_WORKER_VALIDATION
        control.diagnostics=recorder.Diagnostics<Diagnostics>();MemoryBarrier();
#endif
        InterlockedIncrement64(&control.stateSequence);SetEvent(ready);
    }
    void Fail(HRESULT hr,Issue issue=Issue::Encoder) {
        const auto status=recorder.Status();state.error=hr;
        state.issue=status.bufferFailure==ReplayBufferFailure::KeyframeCadence?Issue::Keyframes:
            status.bufferFailure==ReplayBufferFailure::OutputOrder?Issue::Order:issue;
        state.phase=Phase::Failed;stopping=true;Publish(true);
    }
    SaveResult Write(std::shared_ptr<ReplayPreparedSave> clip,std::wstring path,uint64_t id) {
        SaveResult result;
        const auto check=[&](size_t bytes){
#ifndef CAPTURE_WORKER_VALIDATION
            static_cast<void>(bytes);
#endif
            if(!Authority()||InterlockedCompareExchange64(&control.cancelSave,0,0)==static_cast<LONG64>(id))throw winrt::hresult_canceled();
            if(GetTickCount64()-saveStarted>30000)throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_TIMEOUT));
#ifdef CAPTURE_WORKER_VALIDATION
            if(bytes&&InterlockedCompareExchange(&control.testStorageFull,0,0))throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_DISK_FULL));
            Sleep(static_cast<DWORD>(InterlockedCompareExchange(&control.testPacketDelay,0,0)));
#endif
        };
        try {
            winrt::init_apartment(winrt::apartment_type::multi_threaded);
            struct Apartment {~Apartment(){winrt::uninit_apartment();}} apartment;
#ifdef CAPTURE_WORKER_VALIDATION
            Sleep(static_cast<DWORD>(InterlockedCompareExchange(&control.testSaveDelay,0,0)));
#endif
            check(0);WriteReplayMp4(path.c_str(),clip->video.Get(),clip->audio.Get(),*clip->snapshot,check);check(0);
            WIN32_FILE_ATTRIBUTE_DATA file{};if(!GetFileAttributesExW(path.c_str(),GetFileExInfoStandard,&file))winrt::throw_last_error();
            result.bytes=(uint64_t(file.nFileSizeHigh)<<32)|file.nFileSizeLow;
            if(result.bytes==0||result.bytes>MaximumFileBytes)throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE));
            for(const auto& packet:clip->snapshot->packets)if(packet.stream==ReplayStream::Video)++result.frames;
            result.duration=clip->snapshot->end-clip->snapshot->start;
            const auto expected=static_cast<uint64_t>(std::llround(double(result.duration)*clip->framesPerSecond/10000000));
            result.missed=static_cast<uint32_t>(expected>result.frames?expected-result.frames:0);
            if(!result.frames||result.frames>14400||result.missed>14400||result.duration<=0)throw winrt::hresult_error(E_BOUNDS);
            result.width=clip->width;result.height=clip->height;result.sourceWidth=clip->sourceWidth;result.sourceHeight=clip->sourceHeight;
            result.audio=clip->hasAudio?1:0;result.outcome=SaveOutcome::Saved;result.issue=Issue::None;result.error=S_OK;
        }catch(const winrt::hresult_canceled&){result.outcome=SaveOutcome::Cancelled;result.issue=Issue::Cancelled;result.error=HRESULT_FROM_WIN32(ERROR_CANCELLED);}
        catch(const winrt::hresult_error& error){result.error=error.code();}catch(...){result.error=E_FAIL;}
        if(result.outcome!=SaveOutcome::Saved)DeleteFileW(path.c_str());
        return result;
    }
    void CompleteSave() {
        if(!saving.valid()||saving.wait_for(std::chrono::seconds(0))!=std::future_status::ready)return;
        const auto result=saving.get();state.flags&=~Saving;state.saveOutcome=result.outcome;
        state.savedWidth=result.width;state.savedHeight=result.height;state.savedSourceWidth=result.sourceWidth;state.savedSourceHeight=result.sourceHeight;
        state.savedFrames=result.frames;state.savedMissed=result.missed;state.savedAudio=result.audio;state.savedBytes=result.bytes;state.savedDurationTicks=result.duration;
        if(result.outcome!=SaveOutcome::Saved){state.issue=result.issue;state.error=result.error;}
        Publish(true);
    }
    void TakeSave() {
        if(!InterlockedCompareExchange(&control.saveReady,0,1))return;
        MemoryBarrier();state.saveId=control.saveId;state.saveOutcome=SaveOutcome::Pending;
        const auto duration=control.saveDuration;const std::wstring path(control.savePath);
        if(saving.valid()){state.saveOutcome=SaveOutcome::Unavailable;state.issue=Issue::Busy;Publish(true);return;}
        if(InterlockedCompareExchange64(&control.cancelSave,0,0)==static_cast<LONG64>(state.saveId)){
            state.saveOutcome=SaveOutcome::Cancelled;state.issue=Issue::Cancelled;Publish(true);return;}
        auto clip=recorder.PrepareSave(static_cast<int>(duration));
        if(!clip){state.saveOutcome=SaveOutcome::Unavailable;state.issue=Issue::BufferEmpty;Publish(true);return;}
        state.flags|=Saving;state.issue=Issue::None;state.error=S_OK;saveStarted=GetTickCount64();
        const auto id=state.saveId;saving=std::async(std::launch::async,[this,clip=std::move(clip),path,id]{return Write(clip,path,id);});Publish(true);
    }
    bool Current() {
        if(!Authority()){Fail(HRESULT_FROM_WIN32(ERROR_TIMEOUT),Issue::HostLost);return false;}
        CompleteSave();TakeSave(); // Accepted save takes its snapshot before graceful stop.
        if(InterlockedCompareExchange(&control.stop,0,0)||stopping){stopping=true;Publish(true);return false;}
        Publish();return true;
    }
    void Finish(HRESULT acquisition,uint32_t failureStage=0) {
        if(FAILED(acquisition)&&!stopping)Fail(acquisition,failureStage==1?Issue::Audio:Issue::Target);
        stopping=true;
        try{recorder.Finish();}catch(const winrt::hresult_error& error){Fail(error.code());}
        // Save I/O is isolated in this kill-on-close worker. Even an unresponsive
        // filesystem cannot hold host teardown indefinitely.
        while(saving.valid()){
            CompleteSave();if(!saving.valid())break;
            if(!Authority()||GetTickCount64()-saveStarted>35000){
                state.phase=Phase::Failed;state.issue=Issue::HostLost;state.flags|=Ended;Publish(true);
                TerminateProcess(GetCurrentProcess(),ERROR_TIMEOUT);return;
            }
            Publish(); // The capture thread remains responsive while bounded save I/O drains.
            Sleep(10);
        }
        if(state.phase!=Phase::Failed)state.phase=Phase::Ended;
        state.flags|=Ended;Publish(true);
    }
};
thread_local Worker* active;
int32_t __stdcall Current(){try{return active&&active->Current()?1:0;}catch(const winrt::hresult_error& e){active->Fail(e.code());return 0;}catch(...){active->Fail(E_FAIL);return 0;}}
void __stdcall Audio(uint32_t kind,uint32_t,uint32_t,int64_t time,const void* bytes,uint32_t count){
    try{active->recorder.Audio(kind,time,bytes,count);}catch(const winrt::hresult_error& e){active->Fail(e.code(),Issue::Audio);}catch(...){active->Fail(E_FAIL,Issue::Audio);}}
void __stdcall Texture(uint32_t w,uint32_t h,uint32_t sw,uint32_t sh,int64_t time,ID3D11Texture2D* texture){
    try{
        if(!active->sourceVerified){
            const auto& requested=active->control.configuration;
            if(requested.expectedSourceWidth&&(requested.expectedSourceWidth!=sw||requested.expectedSourceHeight!=sh)){
                active->Fail(HRESULT_FROM_WIN32(ERROR_INVALID_WINDOW_HANDLE),Issue::Target);return;
            }
            ComPtr<ID3D11Device> device;texture->GetDevice(&device);active->state.adapterLuid=EncodingDeviceAdapter(device.Get());
            if(requested.adapterLuid&&requested.adapterLuid!=active->state.adapterLuid){active->Fail(DXGI_ERROR_NOT_FOUND);return;}
            active->sourceVerified=true;
        }
        active->recorder.Frame(w,h,sw,sh,time,texture);
    }catch(const winrt::hresult_error& e){active->Fail(e.code());}catch(...){active->Fail(E_FAIL);}}
uint64_t Handle(const wchar_t* value){wchar_t* end{};const auto result=wcstoull(value,&end,16);return end&&*end==0?result:0;}
#ifdef CAPTURE_WORKER_VALIDATION
HRESULT Synthetic(Worker& worker) {
    Microsoft::WRL::ComPtr<ID3D11Device> device;Microsoft::WRL::ComPtr<ID3D11DeviceContext> context;
    winrt::check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context));
    D3D11_TEXTURE2D_DESC desc{};desc.Width=worker.control.configuration.maximumWidth;desc.Height=worker.control.configuration.maximumHeight;
    desc.MipLevels=1;desc.ArraySize=1;desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
    Microsoft::WRL::ComPtr<ID3D11Texture2D> texture;winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&texture));
    Microsoft::WRL::ComPtr<ID3D11RenderTargetView> view;winrt::check_hresult(device->CreateRenderTargetView(texture.Get(),nullptr,&view));
    const float color[]{0.1f,0.7f,0.3f,1};context->ClearRenderTargetView(view.Get(),color);
    LARGE_INTEGER frequency{},counter{};QueryPerformanceFrequency(&frequency);
    CapturePacer pacer;auto next=CapturePacer::Clock::now();
    while(worker.Current()){
        if(InterlockedCompareExchange(&worker.control.testPause,0,0))worker.recorder.Pause();
        else{QueryPerformanceCounter(&counter);worker.recorder.Frame(desc.Width,desc.Height,desc.Width,desc.Height,
            static_cast<int64_t>(double(counter.QuadPart)*1000000/frequency.QuadPart),texture.Get());}
        next+=std::chrono::microseconds(1000000/worker.control.configuration.framesPerSecond);
        if(next<CapturePacer::Clock::now())next=CapturePacer::Clock::now();
        pacer.Until(next);
    }
    return S_OK;
}
#endif
}
int wmain(int argc,wchar_t** argv){
    if(argc!=3)return ERROR_INVALID_PARAMETER;
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        Mapping mapping(Handle(argv[1]));const auto ready=reinterpret_cast<HANDLE>(Handle(argv[2]));
        if(!mapping.value||!ready||mapping.value->magic!=Magic||mapping.value->version!=Version||!Valid(mapping.value->configuration))return ERROR_INVALID_DATA;
        Worker worker(*mapping.value,ready);active=&worker;const auto& c=mapping.value->configuration;
        HRESULT result{};uint32_t failureStage{};
        try {
#ifdef CAPTURE_WORKER_VALIDATION
        if(c.flags==1)result=Synthetic(worker);else
#endif
        result=c.codec?WrailLiveCaptureRunTextureV4(c.window,c.processId,c.created,c.className,c.audioMode,c.maximumWidth,c.maximumHeight,c.framesPerSecond,c.includeCursor,c.adapterLuid,Current,Audio,Texture,&failureStage):
            WrailLiveCaptureRunTextureV3(c.window,c.processId,c.created,c.className,c.audioMode,c.maximumWidth,c.maximumHeight,c.framesPerSecond,c.includeCursor,0,Current,Audio,Texture,&failureStage);
        }catch(const winrt::hresult_error& error){worker.Fail(error.code());result=error.code();}
        catch(...){worker.Fail(E_FAIL);result=E_FAIL;}
        worker.Finish(result,failureStage);active=nullptr;CloseHandle(ready);return FAILED(result)?1:0;
    }catch(...){return ERROR_UNHANDLED_EXCEPTION;}
}
