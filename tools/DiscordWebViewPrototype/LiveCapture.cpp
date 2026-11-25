// Native live-media implementation for the isolated web-capture worker and
// validation harness. Existing Game Capture/preview exports are unchanged.
#include "../../src/WinUiWindowPreviewNative/ProcessLoopbackCapture.h"
#include "../../src/WinUiWindowPreviewNative/CaptureGeometry.h"
#include "CapturePacer.h"
#include "CaptureFrameSchedule.h"
#include "CaptureQuality.h"
#include "../../src/WinUiWindowPreviewNative/CaptureEncodingEnvironment.h"
#include "../../src/WinUiWindowPreviewNative/CaptureSdrColor.h"
#include <d3d11.h>
#include <d2d1_1.h>
#include <dxgi1_2.h>
#include <dwmapi.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <chrono>
#include <memory>
#ifdef CAPTURE_WORKER_VALIDATION
#include <cstdio>
#endif
using Microsoft::WRL::ComPtr;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
using Continue = int32_t(__stdcall*)();
// Borrowed data is valid ONLY during callback; no managed exception may cross ABI.
using Frame = void(__stdcall*)(uint32_t kind, uint32_t width, uint32_t height, int64_t microseconds, const void* data, uint32_t bytes);
// GPU output is borrowed for the synchronous callback. An encoder must copy it
// into its own bounded surfaces before returning; no texture is queued here.
using TextureFrame = void(__stdcall*)(uint32_t width,uint32_t height,uint32_t sourceWidth,uint32_t sourceHeight,int64_t microseconds,ID3D11Texture2D* texture);
namespace {
int64_t QpcMicroseconds() {
    LARGE_INTEGER ticks{},rate{};
    winrt::check_bool(QueryPerformanceCounter(&ticks));winrt::check_bool(QueryPerformanceFrequency(&rate));
    return ticks.QuadPart/rate.QuadPart*1000000 + ticks.QuadPart%rate.QuadPart*1000000/rate.QuadPart;
}
enum class CaptureBorderPolicy { SystemDefault,RequestBorderless };
struct Capture {
    GraphicsCaptureItem item{nullptr}; Direct3D11CaptureFramePool pool{nullptr}; GraphicsCaptureSession session{nullptr};
    ~Capture() { try { if(session) session.Close(); } catch(...) {} try { if(pool) pool.Close(); } catch(...) {} }
};
bool Valid(HWND hwnd, DWORD pid, uint64_t created, const wchar_t* name) {
    DWORD actual{}, affinity{}, cloaked{}; GetWindowThreadProcessId(hwnd,&actual);
    if(actual!=pid || !IsWindow(hwnd))return false;
    wchar_t current[256]{}; if(!GetClassNameW(hwnd,current,256) || wcscmp(current,name))return false;
    if(!GetWindowDisplayAffinity(hwnd,&affinity) || affinity!=WDA_NONE || FAILED(DwmGetWindowAttribute(hwnd,DWMWA_CLOAKED,&cloaked,sizeof(cloaked))))return false;
    HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid); if(!process)return false;
    FILETIME birth{},exit{},kernel{},user{}; const BOOL ok=GetProcessTimes(process,&birth,&exit,&kernel,&user);CloseHandle(process);
    return ok && ((uint64_t(birth.dwHighDateTime)<<32)|birth.dwLowDateTime)==created;
}
}
static int32_t RunLiveCapture(uint64_t window, uint32_t pid, uint64_t created,
    const wchar_t* className, uint32_t includeAudio, uint32_t maximumWidth, uint32_t maximumHeight,
    uint32_t framesPerSecond, Continue current, Frame deliver,TextureFrame textureFrame=nullptr,uint32_t includeCursor=0,uint32_t* failureStage=nullptr,CaptureBorderPolicy borderPolicy=CaptureBorderPolicy::SystemDefault,uint64_t adapterLuid=0,bool exactCanvas=false,void(__stdcall *audioUnavailable)(int32_t)=nullptr,TextureFrame liveTap=nullptr) noexcept {
    if(failureStage)*failureStage=0;
    const LiveCaptureQuality quality{maximumWidth,maximumHeight,framesPerSecond};
    const bool boundedRecording=maximumWidth>=2&&maximumWidth<=8192&&maximumHeight>=2&&maximumHeight<=8192&&
        maximumWidth%2==0&&maximumHeight%2==0&&uint64_t(maximumWidth)*maximumHeight<=3840u*2160u&&
        (framesPerSecond==15||framesPerSecond==30||framesPerSecond==60);
    if(!window||!pid||!className||!current||!deliver||includeAudio>1||includeCursor>1||
        !(textureFrame?boundedRecording:ValidLiveCaptureQuality(quality)))return E_INVALIDARG;
    const int64_t frameInterval=1000000/framesPerSecond;
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        struct Apartment {~Apartment(){winrt::uninit_apartment();}} apartment;
        auto previousDpi=SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        struct Dpi {DPI_AWARENESS_CONTEXT old;~Dpi(){if(old)SetThreadDpiAwarenessContext(old);}} dpi{previousDpi};
        auto hwnd=reinterpret_cast<HWND>(window);
        if(!current()||!Valid(hwnd,pid,created,className))return E_ACCESSDENIED;
        ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> immediate;
        if(textureFrame){device=widgetrail::capture::CreateEncodingDevice(adapterLuid);device->GetImmediateContext(&immediate);}
        else winrt::check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&immediate));
        ComPtr<IDXGIDevice> dxgi;winrt::check_hresult(device.As(&dxgi));
        winrt::com_ptr<IInspectable> inspectable;winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.Get(),inspectable.put()));
        auto captureDevice=inspectable.as<IDirect3DDevice>();
        Capture capture;
        auto interop=winrt::get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();
        winrt::check_hresult(interop->CreateForWindow(hwnd,winrt::guid_of<GraphicsCaptureItem>(),winrt::put_abi(capture.item)));
        auto size=capture.item.Size();
        UINT width{},height{},renderedSourceWidth{},renderedSourceHeight{};
        ComPtr<ID3D11Texture2D> output,staging;
        ComPtr<ID3D11RenderTargetView> outputView;
        ComPtr<ID2D1Bitmap1> targetBitmap;
        std::vector<uint8_t> pixels;
        ComPtr<ID2D1Factory1> factory;winrt::check_hresult(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,IID_PPV_ARGS(&factory)));
        ComPtr<ID2D1Device> d2d;winrt::check_hresult(factory->CreateDevice(dxgi.Get(),&d2d));
        ComPtr<ID2D1DeviceContext> drawing;winrt::check_hresult(d2d->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE,&drawing));
        const auto configureOutput=[&] {
            if(size.Width<=0||size.Height<=0||size.Width>16384||size.Height>16384||int64_t(size.Width)*size.Height>33554432)
                throw winrt::hresult_error(E_INVALIDARG);
            // Replay keeps one codec type and letterboxes a resized source into
            // the initial canvas. Ordinary live-stream resizing is unchanged.
            if(textureFrame && output)return;
            // Fit the selected bounded envelope; never upscale or distort the
            // source. A resize can grow a previously small source within it.
            const auto geometry=textureFrame&&exactCanvas?CaptureGeometry{quality.maximumWidth,quality.maximumHeight}:
                textureFrame?ConfiguredReplayCaptureSize(size.Width,size.Height,quality.maximumWidth,quality.maximumHeight):
                    ConfiguredCaptureSize(size.Width,size.Height,quality.maximumWidth,quality.maximumHeight,true);
            if(!geometry.width||!geometry.height)throw winrt::hresult_error(E_INVALIDARG);
            if(width==geometry.width&&height==geometry.height)return;
            drawing->SetTarget(nullptr);targetBitmap.Reset();outputView.Reset();output.Reset();staging.Reset();
            width=geometry.width;height=geometry.height;
            D3D11_TEXTURE2D_DESC desc{};desc.Width=width;desc.Height=height;desc.MipLevels=1;desc.ArraySize=1;
            desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
            winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&output));
            winrt::check_hresult(device->CreateRenderTargetView(output.Get(),nullptr,&outputView));
            if(!textureFrame) {
                desc.BindFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
                winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&staging));
            }
            ComPtr<IDXGISurface> target;winrt::check_hresult(output.As(&target));
            auto props=D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET|D2D1_BITMAP_OPTIONS_CANNOT_DRAW,D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_PREMULTIPLIED),96,96);
            winrt::check_hresult(drawing->CreateBitmapFromDxgiSurface(target.Get(),&props,&targetBitmap));
            if(!textureFrame)pixels.resize(size_t(width)*height*4);
        };
        // The native encoder uses the exact scaled texture already used by the raw track.
        auto deliverRaw=[&](ID3D11Texture2D* texture){
            const auto time=QpcMicroseconds();
            if(liveTap)liveTap(width,height,width,height,time,texture);
            deliver(1,width,height,time,pixels.data(),static_cast<uint32_t>(pixels.size()));
        };
        configureOutput();
        widgetrail::capture::CaptureSdrColor color;
        if(textureFrame)color.Update(hwnd);
        capture.pool=Direct3D11CaptureFramePool::CreateFreeThreaded(captureDevice,color.PoolFormat(),2,size);
        auto frameAvailable=std::make_shared<widgetrail::capture::CaptureEvent>();
        auto arrival=capture.pool.FrameArrived(winrt::auto_revoke,[frameAvailable](auto&&,auto&&){frameAvailable->Signal();});
        auto closed=capture.item.Closed(winrt::auto_revoke,[frameAvailable](auto&&,auto&&){frameAvailable->Signal();});
        using Clock=std::chrono::steady_clock;
        IGraphicsCaptureSession5 acquisitionPacing{nullptr};
        winrt::Windows::Foundation::TimeSpan defaultAcquisitionInterval{};
        const auto fasterAcquisitionInterval=std::chrono::microseconds(frameInterval/2);
        auto acquisitionSampleStart=Clock::now();
        unsigned freshFrames{};
        bool fasterAcquisition=false;
        const auto observeAcquisition=[&](bool fresh) {
            if(!acquisitionPacing)return;
            if(fresh)++freshFrames;
            const auto now=Clock::now();
            const auto elapsed=now-acquisitionSampleStart;
            if(elapsed<std::chrono::milliseconds(750))return;
            const auto freshRate=freshFrames/std::chrono::duration<double>(elapsed).count();
            acquisitionSampleStart=now;
            freshFrames=0;

            // A shorter WGC interval can preserve updates between 60 FPS send
            // ticks when the source is changing unevenly. Stay with the system
            // interval for idle and already steady sources. Hysteresis avoids
            // flipping back as soon as faster acquisition yields extra frames.
            const bool speedUp=freshRate>=17.0&&freshRate<=(fasterAcquisition?40.0:36.0);
            if(speedUp==fasterAcquisition)return;
            try {
                acquisitionPacing.MinUpdateInterval(speedUp?fasterAcquisitionInterval:defaultAcquisitionInterval);
                fasterAcquisition=speedUp;
            }
            catch(const winrt::hresult_error&) { acquisitionPacing=nullptr; }
        };
        const auto configureSession=[&] {
            capture.session=capture.pool.CreateCaptureSession(capture.item);capture.session.IsCursorCaptureEnabled(includeCursor!=0);
            acquisitionPacing=nullptr;
            if(!textureFrame&&framesPerSecond==60)if(auto pacing=capture.session.try_as<IGraphicsCaptureSession5>()) {
                try {
                    defaultAcquisitionInterval=pacing.MinUpdateInterval();
                    if(defaultAcquisitionInterval>fasterAcquisitionInterval)acquisitionPacing=pacing;
                }
                catch(const winrt::hresult_error&) { /* Keep default acquisition pacing. */ }
                acquisitionSampleStart=Clock::now();
                freshFrames=0;
                fasterAcquisition=false;
            }
            // Request borderless capture only through the supported Windows session
            // API. Windows still enforces the user's screenshot-border permission,
            // and another capturing application can require the border. Unsupported
            // or denied optional configuration must not fail an admitted share.
            if(borderPolicy==CaptureBorderPolicy::RequestBorderless)if(auto border=capture.session.try_as<IGraphicsCaptureSession3>()) {
                try { border.IsBorderRequired(false); }
                catch(const winrt::hresult_error&) { /* Keep ordinary bordered capture. */ }
            }
        };
        configureSession();
        std::unique_ptr<widgetrail::capture::ProcessLoopbackCapture> audio;
        if(includeAudio&&!textureFrame)audio=std::make_unique<widgetrail::capture::ProcessLoopbackCapture>(pid);

        capture.session.StartCapture();
        CapturePacer pacer;
        auto next=Clock::now();auto nextAudio=next;auto lastVideo=next;bool haveVideo=false,liveTextureBlack=false;
        auto retryAudioAt=Clock::time_point::min();
        auto retryDelay=std::chrono::seconds(1);
#ifdef CAPTURE_WORKER_VALIDATION
        wchar_t testAudioMode[16]{};
        const bool testAudioLoss = audioUnavailable && GetEnvironmentVariableW(L"WRAIL_TEST_REPLAY_AUDIO_LOSS",testAudioMode,16)>0;
        const bool testAudioReturn = wcscmp(testAudioMode,L"recover")==0;
        auto testAudioDeadline = Clock::time_point::max();
#endif
        while(current()) {
            if(!Valid(hwnd,pid,created,className))return HRESULT_FROM_WIN32(ERROR_INVALID_WINDOW_HANDLE);
            // Negotiate the hardware recorder on its first texture before
            // starting loopback; codec warmup must not accumulate stale PCM.
            if(includeAudio&&textureFrame&&!audio&&haveVideo&&Clock::now()>=retryAudioAt){
                if(failureStage)*failureStage=1;
                using namespace widgetrail::capture;
                audio=std::make_unique<ProcessLoopbackCapture>(pid,audioUnavailable?ProcessLoopbackMode::Replay:ProcessLoopbackMode::Strict);
                if(failureStage)*failureStage=0;
            }
            if(audio && Clock::now()>=nextAudio) {
                nextAudio=Clock::now()+std::chrono::milliseconds(20);
                if(failureStage)*failureStage=1;
                std::deque<widgetrail::capture::CapturedAudioPacket> packets;
                try {
#ifdef CAPTURE_WORKER_VALIDATION
                    if(testAudioLoss && Clock::now()>=testAudioDeadline &&
                        (!testAudioReturn || Clock::now()-testAudioDeadline<std::chrono::seconds(4)))throw winrt::hresult_error(AUDCLNT_E_DEVICE_INVALIDATED);
#endif
                    packets=audio->Drain();
                }
                catch(const winrt::hresult_error& error) {
                    if(!audioUnavailable || error.code()==E_OUTOFMEMORY) throw;
                    audio.reset();
                    retryAudioAt=Clock::now()+retryDelay;
                    retryDelay=std::min(retryDelay*2,std::chrono::seconds(8));
                    audioUnavailable(error.code());
                }
                if(failureStage)*failureStage=0;
                if(!packets.empty()){
                    retryDelay=std::chrono::seconds(1);
#ifdef CAPTURE_WORKER_VALIDATION
                    if(testAudioLoss && testAudioDeadline==Clock::time_point::max())
                        testAudioDeadline=Clock::now()+std::chrono::seconds(2);
#endif
                }
                if(textureFrame) {
                    // Preserve each WASAPI timestamp for the shared recording
                    // timeline; live WebRTC retains its existing joined transport.
                    for(const auto& packet:packets)deliver(2,48000,2,static_cast<int64_t>(packet.qpc100ns/10),
                        packet.samples.data(),static_cast<uint32_t>(packet.samples.size()*2));
                }else if(!packets.empty()) {
                    std::vector<int16_t> joined;
                    for(const auto& packet:packets)joined.insert(joined.end(),packet.samples.begin(),packet.samples.end());
                    deliver(2,48000,2,static_cast<int64_t>(packets.front().qpc100ns/10),joined.data(),static_cast<uint32_t>(joined.size()*2));
                }
            }
            if(Clock::now()<next){pacer.Until(audio?std::min(next,nextAudio):next);continue;}
            const auto nextFrame=AdvanceCaptureDeadline<Clock>(next,Clock::now(),
                std::chrono::microseconds(frameInterval));
            DWORD cloaked{};DwmGetWindowAttribute(hwnd,DWMWA_CLOAKED,&cloaked,sizeof(cloaked));
            if(IsIconic(hwnd)||!IsWindowVisible(hwnd)||cloaked){
                if(textureFrame) {
                    // Recorder policy receives loss of its selected picture;
                    // never substitute desktop pixels or silently encode black.
                    haveVideo=false;deliver(3,width,height,QpcMicroseconds(),nullptr,0);next=nextFrame;continue;
                }
                // Keep the connection alive with black frames while the selected
                // window is unavailable; never switch to another application.
                std::fill(pixels.begin(),pixels.end(),uint8_t{0});
                if(liveTap){
                    // Preserve the same privacy behavior and encoder across a
                    // temporary minimize. Never submit the last visible texture.
                    if(!liveTextureBlack){
                        const float black[4]={0,0,0,1};
                        immediate->ClearRenderTargetView(outputView.Get(),black);liveTextureBlack=true;
                    }
                    haveVideo=false;
                }
                deliverRaw(liveTap?output.Get():nullptr);
                next=nextFrame;
                continue;
            }
            auto frame=capture.pool.TryGetNextFrame();
            if(haveVideo)observeAcquisition(static_cast<bool>(frame));
            if(!frame){
                // A live share delivers the cached pixels on the scheduled tick when
                // WGC has no newer picture. The recorder times repeats from its last texture.
                const auto repeatAt = lastVideo + std::chrono::microseconds(frameInterval);
                if (haveVideo && (!textureFrame || Clock::now() >= repeatAt)) {
                    if(textureFrame)textureFrame(width,height,renderedSourceWidth,renderedSourceHeight,QpcMicroseconds(),output.Get());
                    else deliverRaw(output.Get());
                    lastVideo=Clock::now();next=nextFrame;
                }else{
                    auto wake=haveVideo?repeatAt:nextFrame;
                    if(audio)wake=std::min(wake,nextAudio);
                    pacer.Until(wake,frameAvailable->Get());
                }
                continue;
            }
            while(auto newer=capture.pool.TryGetNextFrame()){frame.Close();frame=newer;}
            const bool colorChanged=textureFrame && color.Update(hwnd);
            if(colorChanged || frame.ContentSize().Width!=size.Width||frame.ContentSize().Height!=size.Height){
                const auto resized=frame.ContentSize();
#ifdef CAPTURE_WORKER_VALIDATION
                std::fprintf(stderr,"CAPTURE resize observed=%dx%d previous=%dx%d rendered=%ux%u\n",resized.Width,resized.Height,size.Width,size.Height,renderedSourceWidth,renderedSourceHeight);
#endif
                frame.Close();
                if(resized.Width>0&&resized.Height>0&&resized.Width<=16384&&resized.Height<=16384){
                    size=resized;configureOutput();liveTextureBlack=false;if(!textureFrame)haveVideo=false;
                    // A static source may not publish another dirty frame after
                    // Recreate alone. Restart only this same admitted acquisition
                    // session to request its initial complete new-size picture.
                    // The encoder, output canvas, audio and replay buffer survive.
                    arrival.revoke();capture.session.Close();capture.pool.Close();
                    if(!current())return S_OK; // Preserve the caller's cooperative stop/error reason.
                    if(!Valid(hwnd,pid,created,className))return HRESULT_FROM_WIN32(ERROR_INVALID_WINDOW_HANDLE);
                    capture.pool=Direct3D11CaptureFramePool::CreateFreeThreaded(captureDevice,color.PoolFormat(),2,size);
                    arrival=capture.pool.FrameArrived(winrt::auto_revoke,[frameAvailable](auto&&,auto&&){frameAvailable->Signal();});
                    configureSession();capture.session.StartCapture();
                }
                continue;
            }
            auto access=frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
            const bool nativeSize=textureFrame && static_cast<UINT>(size.Width)<=width && static_cast<UINT>(size.Height)<=height;
            if(nativeSize && !color.Hdr()) {
                // Exact pixels, including odd native sizes padded for chroma.
                // A resized smaller source stays at 1:1 inside the fixed canvas.
                ComPtr<ID3D11Texture2D> source;winrt::check_hresult(access->GetInterface(IID_PPV_ARGS(&source)));
                const float black[]{0,0,0,1};
                if(static_cast<UINT>(size.Width)!=width||static_cast<UINT>(size.Height)!=height)immediate->ClearRenderTargetView(outputView.Get(),black);
                const D3D11_BOX pixelsBox{0,0,0,static_cast<UINT>(size.Width),static_cast<UINT>(size.Height),1};
                immediate->CopySubresourceRegion(output.Get(),0,(width-static_cast<UINT>(size.Width))/2,(height-static_cast<UINT>(size.Height))/2,0,source.Get(),0,&pixelsBox);
            }else {
                ComPtr<IDXGISurface> surface;winrt::check_hresult(access->GetInterface(IID_PPV_ARGS(&surface)));
                drawing->SetTarget(targetBitmap.Get());drawing->BeginDraw();drawing->Clear(D2D1::ColorF(0.f,0.f,0.f,1.f));
                const auto scale=nativeSize?1.f:std::min(float(width)/float(size.Width),float(height)/float(size.Height));
                const auto drawnWidth=scale*float(size.Width),drawnHeight=scale*float(size.Height);
                const auto left=nativeSize?float((width-size.Width)/2):(float(width)-drawnWidth)/2;
                const auto top=nativeSize?float((height-size.Height)/2):(float(height)-drawnHeight)/2;
                color.Draw(drawing.Get(),surface.Get(),D2D1::RectF(0,0,float(size.Width),float(size.Height)),
                    D2D1::RectF(left,top,left+drawnWidth,top+drawnHeight));
                auto hr=drawing->EndDraw();drawing->SetTarget(nullptr);winrt::check_hresult(hr);
                color.ReleaseInput();
            }
#ifdef CAPTURE_WORKER_VALIDATION
            if(renderedSourceWidth!=static_cast<uint32_t>(size.Width)||renderedSourceHeight!=static_cast<uint32_t>(size.Height))
                std::fprintf(stderr,"CAPTURE fresh source=%dx%d canvas=%ux%u\n",size.Width,size.Height,width,height);
#endif
            renderedSourceWidth=static_cast<uint32_t>(size.Width);renderedSourceHeight=static_cast<uint32_t>(size.Height);
            if(textureFrame)textureFrame(width,height,renderedSourceWidth,renderedSourceHeight,QpcMicroseconds(),output.Get());
            else {
                immediate->CopyResource(staging.Get(),output.Get());D3D11_MAPPED_SUBRESOURCE mapped{};
                winrt::check_hresult(immediate->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped));
                for(UINT y=0;y<height;++y)memcpy(pixels.data()+size_t(y)*width*4,static_cast<uint8_t*>(mapped.pData)+size_t(y)*mapped.RowPitch,width*4);
                immediate->Unmap(staging.Get(),0);
                deliverRaw(output.Get());
            }
            frame.Close();haveVideo=true;liveTextureBlack=false;lastVideo=Clock::now();next=nextFrame;
        }
        return S_OK;
    }catch(...){return winrt::to_hresult();}
}

extern "C" __declspec(dllexport) int32_t __stdcall WrailLiveCaptureRunV2(uint64_t window,uint32_t pid,uint64_t created,
    const wchar_t* className,uint32_t includeAudio,uint32_t maximumWidth,uint32_t maximumHeight,uint32_t framesPerSecond,Continue current,Frame deliver) noexcept {
    return RunLiveCapture(window,pid,created,className,includeAudio,maximumWidth,maximumHeight,framesPerSecond,current,deliver,nullptr,0,nullptr,CaptureBorderPolicy::RequestBorderless);
}

extern "C" __declspec(dllexport) int32_t __stdcall WrailLiveCaptureRunTextureV1(uint64_t window,uint32_t pid,uint64_t created,
    const wchar_t* className,uint32_t includeAudio,uint32_t maximumWidth,uint32_t maximumHeight,uint32_t framesPerSecond,uint32_t includeCursor,
    Continue current,Frame audioAndState,TextureFrame texture) noexcept {
    if(!texture)return E_INVALIDARG;
    return RunLiveCapture(window,pid,created,className,includeAudio,maximumWidth,maximumHeight,framesPerSecond,current,audioAndState,texture,includeCursor,nullptr,CaptureBorderPolicy::RequestBorderless);
}

extern "C" __declspec(dllexport) int32_t __stdcall WrailLiveCaptureRunTextureV2(uint64_t window,uint32_t pid,uint64_t created,
    const wchar_t* className,uint32_t includeAudio,uint32_t maximumWidth,uint32_t maximumHeight,uint32_t framesPerSecond,uint32_t includeCursor,
    Continue current,Frame audioAndState,TextureFrame texture,uint32_t* failureStage) noexcept {
    if(!texture||!failureStage)return E_INVALIDARG;
    return RunLiveCapture(window,pid,created,className,includeAudio,maximumWidth,maximumHeight,framesPerSecond,current,audioAndState,texture,includeCursor,failureStage,CaptureBorderPolicy::RequestBorderless);
}

// Preserve the original prototype ABI for existing, separately built callers.
extern "C" __declspec(dllexport) int32_t __stdcall WrailLiveCaptureRun(uint64_t window, uint32_t pid, uint64_t created,
    const wchar_t* className, uint32_t includeAudio, Continue current, Frame deliver) noexcept {
    return WrailLiveCaptureRunV2(window,pid,created,className,includeAudio,2560,720,30,current,deliver);
}

extern "C" __declspec(dllexport) int32_t __stdcall WrailLiveCaptureRunTextureV3(uint64_t window,uint32_t pid,uint64_t created,
    const wchar_t* className,uint32_t includeAudio,uint32_t maximumWidth,uint32_t maximumHeight,uint32_t framesPerSecond,uint32_t includeCursor,uint64_t adapterLuid,
    Continue current,Frame audioAndState,TextureFrame texture,uint32_t* failureStage) noexcept {
    if(!texture||!failureStage)return E_INVALIDARG;
    return RunLiveCapture(window,pid,created,className,includeAudio,maximumWidth,maximumHeight,framesPerSecond,current,audioAndState,texture,includeCursor,failureStage,
        CaptureBorderPolicy::RequestBorderless,adapterLuid);
}

// Advanced host resolution has already qualified this exact even canvas. Do not
// aspect-fit it a second time; fit/pad source pixels inside that admitted canvas.
extern "C" __declspec(dllexport) int32_t __stdcall WrailLiveCaptureRunTextureV4(uint64_t window,uint32_t pid,uint64_t created,
    const wchar_t* className,uint32_t includeAudio,uint32_t width,uint32_t height,uint32_t framesPerSecond,uint32_t includeCursor,uint64_t adapterLuid,
    Continue current,Frame audioAndState,TextureFrame texture,uint32_t* failureStage) noexcept {
    if(!texture||!failureStage||!adapterLuid)return E_INVALIDARG;
    return RunLiveCapture(window,pid,created,className,includeAudio,width,height,framesPerSecond,current,audioAndState,texture,includeCursor,failureStage,
        CaptureBorderPolicy::RequestBorderless,adapterLuid,true);
}

extern "C" __declspec(dllexport) int32_t __stdcall WrailLiveCaptureRunReplay(uint64_t window,uint32_t pid,uint64_t created,
    const wchar_t* className,uint32_t includeAudio,uint32_t width,uint32_t height,uint32_t framesPerSecond,uint32_t includeCursor,uint64_t adapterLuid,
    uint32_t exactCanvas,Continue current,Frame audioAndState,TextureFrame texture,uint32_t* failureStage,void(__stdcall *audioUnavailable)(int32_t)) noexcept {
    if(!texture||!failureStage||!audioUnavailable||exactCanvas>1||(exactCanvas&&!adapterLuid))return E_INVALIDARG;
    return RunLiveCapture(window,pid,created,className,includeAudio,width,height,framesPerSecond,current,audioAndState,texture,includeCursor,failureStage,
        CaptureBorderPolicy::RequestBorderless,adapterLuid,exactCanvas!=0,audioUnavailable);
}

extern "C" __declspec(dllexport) int32_t __stdcall WrailLiveCaptureRunWithEncodingTap(uint64_t window,uint32_t pid,uint64_t created,
    const wchar_t* className,uint32_t includeAudio,uint32_t maximumWidth,uint32_t maximumHeight,uint32_t fps,
    Continue current,Frame raw,TextureFrame tap) noexcept {
    return RunLiveCapture(window,pid,created,className,includeAudio,maximumWidth,maximumHeight,fps,current,raw,nullptr,0,nullptr,
        CaptureBorderPolicy::RequestBorderless,0,false,nullptr,tap);
}
