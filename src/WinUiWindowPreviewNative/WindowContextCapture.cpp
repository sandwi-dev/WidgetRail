#include "WindowContextCapture.h"
#include "CaptureGeometry.h"
#include "CaptureSdrColor.h"
#include "CaptureVideoEncoder.h"
#include "CaptureAudioTimeline.h"
#include "CaptureEncodingProbe.h"
#include <Windows.h>
#include <d3d11.h>
#include <d2d1_1.h>
#include <dxgi1_3.h>
#include <dwmapi.h>
#include <wincodec.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <wrl/client.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <algorithm>
#include <atomic>
#include <chrono>
#include <cmath>
#include <memory>
#include <optional>
#include <vector>
using Microsoft::WRL::ComPtr;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
namespace {
std::atomic_bool busy{};
using Clock = std::chrono::steady_clock;
struct Event { HANDLE value{CreateEventW(nullptr, FALSE, FALSE, nullptr)}; ~Event() { if(value) CloseHandle(value); } };
struct CaptureDpiContext {
    DPI_AWARENESS_CONTEXT previous{SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)};
    ~CaptureDpiContext(){if(previous)SetThreadDpiAwarenessContext(previous);}
};
bool Current(const WrailPreviewTarget& expected, WrailPreviewAuthority authority, void* context) {
    if (!authority(context)) return false;
    const auto hwnd = reinterpret_cast<HWND>(expected.window);
    WrailPreviewTarget actual{};
    if (FAILED(WrailPreviewReadIdentity(expected.window, &actual)) || actual.processId != expected.processId ||
        actual.processCreated != expected.processCreated || wcscmp(actual.className, expected.className) ||
        !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
    DWORD affinity{}, cloaked{};
    return GetWindowDisplayAffinity(hwnd, &affinity) && affinity == WDA_NONE &&
        SUCCEEDED(DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, &cloaked, sizeof(cloaked))) && !cloaked;
}
struct Capture {
    GraphicsCaptureItem item{nullptr}; Direct3D11CaptureFramePool pool{nullptr}; GraphicsCaptureSession session{nullptr};
    winrt::event_token arrived{};
    ~Capture() {
        try { if(pool && arrived.value) pool.FrameArrived(arrived); } catch (...) {}
        try { if(session) session.Close(); } catch (...) {}
        try { if(pool) pool.Close(); } catch (...) {}
    }
};
void Png(const wchar_t* path, UINT w, UINT h, const std::vector<BYTE>& bytes) {
    ComPtr<IWICImagingFactory> factory; winrt::check_hresult(CoCreateInstance(CLSID_WICImagingFactory2, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&factory)));
    ComPtr<IWICStream> stream; winrt::check_hresult(factory->CreateStream(&stream)); winrt::check_hresult(stream->InitializeFromFilename(path, GENERIC_WRITE));
    ComPtr<IWICBitmapEncoder> encoder; winrt::check_hresult(factory->CreateEncoder(GUID_ContainerFormatPng, nullptr, &encoder));
    winrt::check_hresult(encoder->Initialize(stream.Get(), WICBitmapEncoderNoCache));
    ComPtr<IWICBitmapFrameEncode> frame; winrt::check_hresult(encoder->CreateNewFrame(&frame, nullptr)); winrt::check_hresult(frame->Initialize(nullptr));
    winrt::check_hresult(frame->SetSize(w,h)); auto format = GUID_WICPixelFormat32bppBGRA;
    winrt::check_hresult(frame->SetPixelFormat(&format)); if(format != GUID_WICPixelFormat32bppBGRA) throw winrt::hresult_error(E_FAIL);
    winrt::check_hresult(frame->WritePixels(h, w*4, static_cast<UINT>(bytes.size()), const_cast<BYTE*>(bytes.data())));
    winrt::check_hresult(frame->Commit()); winrt::check_hresult(encoder->Commit());
}
using Video = widgetrail::capture::VideoEncoder;
}
extern "C" int32_t __stdcall WrailProbeCaptureEncoding(const WrailCaptureOptions* options,WrailCaptureEncodingProbe* result) noexcept {
    if(!options || options->size!=sizeof(*options) || options->version!=3 || options->kind!=2 || options->audioMode>1 || options->reserved ||
        options->maximumWidth<2 || options->maximumHeight<2 || options->maximumWidth>8192 || options->maximumHeight>8192 ||
        options->maximumWidth%2 || options->maximumHeight%2 || uint64_t(options->maximumWidth)*options->maximumHeight>33554432ULL ||
        options->framesPerSecond<1 || options->framesPerSecond>120 || options->bitrate<100000 || options->bitrate>100000000 ||
        (options->encoder!=1 && options->encoder!=2) || !result || result->size!=sizeof(*result) || result->version!=1) return E_INVALIDARG;
    const auto start=Clock::now();
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        struct Apartment {~Apartment(){winrt::uninit_apartment();}} apartment;
        ComPtr<ID3D11Device> device;
        if(options->encoder==2)winrt::check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,
            D3D11_CREATE_DEVICE_BGRA_SUPPORT|D3D11_CREATE_DEVICE_VIDEO_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,nullptr));
        Video probe(nullptr,options->maximumWidth,options->maximumHeight,*options,device.Get(),options->audioMode==1);
        result->supported=1;result->hardware=probe.Hardware()?1:0;result->error=S_OK;
    }catch(const winrt::hresult_error& error){result->supported=0;result->error=error.code();}
     catch(...){result->supported=0;result->error=E_FAIL;}
    result->elapsedMilliseconds=std::chrono::duration<double,std::milli>(Clock::now()-start).count();
    return S_OK; // Unsupported encoders are capability results, not a failed IPC call.
}
extern "C" int32_t __stdcall WrailProbeCaptureAudio(WrailCaptureEncodingProbe* result) noexcept {
    if(!result || result->size!=sizeof(*result) || result->version!=1)return E_INVALIDARG;
    const auto start=Clock::now();
    try {
        // Probe only our own process; never open another application's audio
        // merely to populate a settings list. No PCM is retained or published.
        widgetrail::capture::ProcessLoopbackCapture audio(GetCurrentProcessId());audio.Stop();
        winrt::check_hresult(audio.Status());result->supported=1;result->error=S_OK;
    }catch(const winrt::hresult_error& error){result->supported=0;result->error=error.code();}
     catch(...){result->supported=0;result->error=E_FAIL;}
    result->elapsedMilliseconds=std::chrono::duration<double,std::milli>(Clock::now()-start).count();
    return S_OK;
}
static void CheckCaptureOutputSize(const wchar_t* path,uint64_t maximumBytes) {
    WIN32_FILE_ATTRIBUTE_DATA attributes{};
    if(!GetFileAttributesExW(path,GetFileExInfoStandard,&attributes)) {
        const auto error=GetLastError();
        throw winrt::hresult_error(HRESULT_FROM_WIN32(error?error:ERROR_READ_FAULT));
    }
    if(attributes.dwFileAttributes&FILE_ATTRIBUTE_DIRECTORY)throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_INVALID_DATA));
    if(((uint64_t(attributes.nFileSizeHigh)<<32)|attributes.nFileSizeLow)>maximumBytes)
        throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE));
}

static int32_t RunConfiguredCapture(const WrailPreviewTarget* target,const wchar_t* path,const WrailCaptureOptions* options,
    WrailPreviewAuthority authority,void* context,WrailCaptureResult* result,const WrailCaptureEncodingSelection* selection=nullptr,
    uint32_t expectedSourceWidth=0,uint32_t expectedSourceHeight=0,uint64_t maximumOutputBytes=WrailCaptureTemporaryMaximumBytes, HWND screenshotOverlay=nullptr) noexcept {
    if(!target || target->size!=sizeof(*target) || target->version!=1 || !path || !*path || !authority || !result ||
        result->size!=sizeof(*result) || result->version!=3 || !options || options->size!=sizeof(*options) || options->version!=3 ||
        options->audioMode>1 || options->reserved || (options->kind!=2 && options->audioMode!=0) ||
        options->kind<1 || options->kind>2 || options->includeCursor>1 || options->maximumBytes<1024 || options->maximumBytes>maximumOutputBytes ||
        options->framesPerSecond<1 || options->framesPerSecond>120 || options->durationSeconds<1 || options->durationSeconds>120 ||
        options->bitrate<100000 || options->bitrate>100000000 || options->maximumWidth>16384 || options->maximumHeight>16384) return E_INVALIDARG;
    if(options->kind==2 && options->encoder!=1 && options->encoder!=2) return E_INVALIDARG;
    const auto kind=options->kind, fps=options->framesPerSecond;
    std::optional<widgetrail::capture::CaptureEncodingProfile> encoding;
    if(selection){
        if(selection->size!=sizeof(*selection)||selection->version!=1||!selection->adapterLuid||kind!=2||options->encoder!=2||
            options->maximumWidth<2||options->maximumHeight<2||options->maximumWidth>8192||options->maximumHeight>8192||options->maximumWidth%2||options->maximumHeight%2||uint64_t(options->maximumWidth)*options->maximumHeight>3840u*2160u||
            expectedSourceWidth<2||expectedSourceHeight<2||expectedSourceWidth>8192||expectedSourceHeight>8192||uint64_t(expectedSourceWidth)*expectedSourceHeight>3840u*2160u)return E_INVALIDARG;
        encoding=widgetrail::capture::CaptureEncodingProfile{static_cast<widgetrail::capture::CaptureVideoCodec>(selection->codec),
            static_cast<widgetrail::capture::CapturePixelFormat>(selection->pixelFormat),static_cast<widgetrail::capture::CaptureRateControl>(selection->rateControl),selection->quality};
        if(!encoding->Valid())return E_INVALIDARG;
    }
    const bool gpuVideo=kind==2 && options->encoder==2;
    if(busy.exchange(true)) return HRESULT_FROM_WIN32(ERROR_BUSY);
    struct BusyGuard { ~BusyGuard(){busy=false;} } busyGuard;
    try {
        // Thread-pool threads may inherit different DPI contexts after WinRT calls.
        // WGC item dimensions must always describe physical source pixels.
        CaptureDpiContext dpi;
        if(!dpi.previous)throw winrt::hresult_error(HRESULT_FROM_WIN32(GetLastError()));
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        struct Apartment { ~Apartment(){winrt::uninit_apartment();} } apartment;
        const auto sourceWindow = reinterpret_cast<HWND>(target->window);
        const auto foreground = GetAncestor(GetForegroundWindow(), GA_ROOT);
        if (!Current(*target, authority, context) || foreground != (screenshotOverlay ? screenshotOverlay : sourceWindow))
            throw winrt::hresult_error(E_ACCESSDENIED);
        if (screenshotOverlay) {
            DWORD overlayPid{};
            if (kind != 1 || !GetWindowThreadProcessId(screenshotOverlay, &overlayPid) ||
                overlayPid != GetCurrentProcessId() || target->processId == overlayPid ||
                sourceWindow != GetAncestor(sourceWindow, GA_ROOT) || sourceWindow == GetDesktopWindow() || sourceWindow == GetShellWindow())
                throw winrt::hresult_error(E_ACCESSDENIED);
        }
        if(!GraphicsCaptureSession::IsSupported()) throw winrt::hresult_error(E_NOTIMPL);
        ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> immediate;
        if(selection){device=widgetrail::capture::CreateEncodingDevice(selection->adapterLuid);device->GetImmediateContext(&immediate);}
        else winrt::check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT|(gpuVideo?D3D11_CREATE_DEVICE_VIDEO_SUPPORT:0),
            nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&immediate));
        ComPtr<IDXGIDevice> dxgi; winrt::check_hresult(device.As(&dxgi));
        winrt::com_ptr<IInspectable> inspectable; winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.Get(),inspectable.put()));
        auto captureDevice=inspectable.as<IDirect3DDevice>();
        Capture capture;
        auto interop=winrt::get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();
        winrt::check_hresult(interop->CreateForWindow(reinterpret_cast<HWND>(target->window),winrt::guid_of<GraphicsCaptureItem>(),winrt::put_abi(capture.item)));
        auto size=capture.item.Size();
        if(selection&&(expectedSourceWidth!=static_cast<uint32_t>(size.Width)||expectedSourceHeight!=static_cast<uint32_t>(size.Height)))return HRESULT_FROM_WIN32(ERROR_INVALID_WINDOW_HANDLE);
        const auto geometry = selection?CaptureGeometry{options->maximumWidth,options->maximumHeight}:
            ConfiguredCaptureSize(size.Width, size.Height, options->maximumWidth, options->maximumHeight, kind==2);
        if (!geometry.width || !geometry.height) throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE));
        const UINT width = geometry.width, height = geometry.height;
        D3D11_TEXTURE2D_DESC desc{};desc.Width=width;desc.Height=height;desc.MipLevels=1;desc.ArraySize=1;
        desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
        ComPtr<ID3D11Texture2D> output;winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&output));
        desc.BindFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Texture2D> staging;if(!gpuVideo)winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&staging));
        ComPtr<ID2D1Factory1> factory;winrt::check_hresult(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,IID_PPV_ARGS(&factory)));
        ComPtr<ID2D1Device> d2d;winrt::check_hresult(factory->CreateDevice(dxgi.Get(),&d2d));
        ComPtr<ID2D1DeviceContext> drawing;winrt::check_hresult(d2d->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE,&drawing));
        ComPtr<IDXGISurface> outputSurface;winrt::check_hresult(output.As(&outputSurface));
        auto targetProperties=D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET|D2D1_BITMAP_OPTIONS_CANNOT_DRAW,D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_PREMULTIPLIED),96,96);
        ComPtr<ID2D1Bitmap1> outputBitmap;winrt::check_hresult(drawing->CreateBitmapFromDxgiSurface(outputSurface.Get(),&targetProperties,&outputBitmap));
        auto signal=std::make_shared<Event>();if(!signal->value) throw winrt::hresult_error(E_OUTOFMEMORY);
        widgetrail::capture::CaptureSdrColor color;
        color.Update(sourceWindow);
        capture.pool=Direct3D11CaptureFramePool::CreateFreeThreaded(captureDevice,color.PoolFormat(),2,size);
        capture.arrived=capture.pool.FrameArrived([signal](auto&&,auto&&){SetEvent(signal->value);});
        capture.session=capture.pool.CreateCaptureSession(capture.item);capture.session.IsCursorCaptureEnabled(options->includeCursor!=0);
        std::unique_ptr<Video> video;
        if(kind==2)video=std::make_unique<Video>(path,width,height,*options,device.Get(),options->audioMode==1,nullptr,nullptr,encoding?&*encoding:nullptr);
        std::unique_ptr<widgetrail::capture::ProcessLoopbackCapture> audio;
        std::unique_ptr<widgetrail::capture::CaptureAudioTimeline> audioTimeline;
        if(options->audioMode==1)audio=std::make_unique<widgetrail::capture::ProcessLoopbackCapture>(target->processId);
        capture.session.StartCapture();
        std::vector<BYTE> pixels(gpuVideo?0:width*height*4);
        bool haveFrame=false, stoppedEarly=false;auto firstDeadline=Clock::now()+std::chrono::seconds(3);auto started=Clock::now();
        uint64_t audioEpoch{};
        UINT frames=0, nextSlot=0, missed=0, lastWrittenSlot=0;
        auto nextSizeCheck=Clock::now()+std::chrono::seconds(1);
        const UINT totalSlots=kind==2?fps*options->durationSeconds:1;
        while(nextSlot<totalSlots) {
            const auto decision=authority(context);
            if(!decision) throw winrt::hresult_error(E_ABORT);
            if(decision==2 && kind==2) {if(!frames)throw winrt::hresult_error(E_ABORT);stoppedEarly=true;break;}
            if(audio) {
                winrt::check_hresult(audio->Status());
                if(audioTimeline) {
                    audioTimeline->Queue(audio->Drain());
                    if(frames)audioTimeline->FlushThrough(uint64_t(lastWrittenSlot+1)*Video::AudioRate/fps);
                }
            }
            if(haveFrame && Clock::now()<started+std::chrono::milliseconds(1000*nextSlot/fps)) {WaitForSingleObject(signal->value,10);continue;}
            if(!Current(*target,authority,context) ||
                (screenshotOverlay && GetAncestor(GetForegroundWindow(), GA_ROOT) != screenshotOverlay))
                throw winrt::hresult_error(E_ABORT);
            auto frame=capture.pool.TryGetNextFrame();
            // WGC may produce faster than the requested recording rate. Reading
            // only one buffered frame per slot preserves an old frame and adds
            // visible/audio latency. Retire stale frames before sampling pixels.
            for(unsigned drain=0;frame && drain<4;++drain) {
                auto latest=capture.pool.TryGetNextFrame();if(!latest)break;
                frame.Close();frame=latest;
            }
            if(frame) {
                if(color.Update(sourceWindow)) {
                    frame.Close();capture.pool.Recreate(captureDevice,color.PoolFormat(),2,size);
                    continue;
                }
                struct FrameGuard {Direct3D11CaptureFrame& frame;~FrameGuard(){try{if(frame)frame.Close();}catch(...){}}} guard{frame};
                if(frame.ContentSize().Width!=size.Width || frame.ContentSize().Height!=size.Height) throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_RETRY));
                auto access=frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
                const bool nativeSize=selection&&static_cast<UINT>(size.Width)<=width&&static_cast<UINT>(size.Height)<=height;
                if(nativeSize&&!color.Hdr()){
                    ComPtr<ID3D11Texture2D> source;winrt::check_hresult(access->GetInterface(IID_PPV_ARGS(&source)));
                    if(static_cast<UINT>(size.Width)!=width||static_cast<UINT>(size.Height)!=height){
                        drawing->SetTarget(outputBitmap.Get());drawing->BeginDraw();drawing->Clear(D2D1::ColorF(0.f,0.f,0.f,1.f));
                        const auto cleared=drawing->EndDraw();drawing->SetTarget(nullptr);winrt::check_hresult(cleared);
                    }
                    const D3D11_BOX pixelsBox{0,0,0,static_cast<UINT>(size.Width),static_cast<UINT>(size.Height),1};
                    immediate->CopySubresourceRegion(output.Get(),0,(width-static_cast<UINT>(size.Width))/2,(height-static_cast<UINT>(size.Height))/2,0,source.Get(),0,&pixelsBox);
                }else {
                    ComPtr<IDXGISurface> source;winrt::check_hresult(access->GetInterface(IID_PPV_ARGS(&source)));
                    drawing->SetTarget(outputBitmap.Get());drawing->BeginDraw();drawing->Clear(D2D1::ColorF(0.f,0.f,0.f,1.f));
                    auto destination=D2D1::RectF(0,0,float(width),float(height));
                    if(selection){
                        const auto scale=nativeSize?1.f:std::min(float(width)/float(size.Width),float(height)/float(size.Height));
                        const auto drawnWidth=scale*float(size.Width),drawnHeight=scale*float(size.Height);
                        const auto left=nativeSize?float((width-size.Width)/2):(float(width)-drawnWidth)/2;
                        const auto top=nativeSize?float((height-size.Height)/2):(float(height)-drawnHeight)/2;
                        destination=D2D1::RectF(left,top,left+drawnWidth,top+drawnHeight);
                    }
                    color.Draw(drawing.Get(),source.Get(),D2D1::RectF(0,0,float(size.Width),float(size.Height)),destination);
                    auto hr=drawing->EndDraw();drawing->SetTarget(nullptr);winrt::check_hresult(hr);
                    color.ReleaseInput();
                }
                if(!gpuVideo) {
                    immediate->CopyResource(staging.Get(),output.Get());D3D11_MAPPED_SUBRESOURCE mapped{};
                    winrt::check_hresult(immediate->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped));
                    for(UINT y=0;y<height;++y)memcpy(pixels.data()+y*width*4,static_cast<BYTE*>(mapped.pData)+y*mapped.RowPitch,width*4);
                    immediate->Unmap(staging.Get(),0);
                }
                if(!haveFrame){
                    haveFrame=true;started=Clock::now();
                    if(audio) {
                        audioEpoch=static_cast<uint64_t>(frame.SystemRelativeTime().count());
                        audioTimeline=std::make_unique<widgetrail::capture::CaptureAudioTimeline>(*video,audioEpoch,
                            uint64_t(options->durationSeconds)*Video::AudioRate);
                    }
                }
            }
            auto now=Clock::now();
            if(!haveFrame){if(now>=firstDeadline)throw winrt::hresult_error(HRESULT_FROM_WIN32(WAIT_TIMEOUT));}
            else if(now>=started+std::chrono::milliseconds(1000*nextSlot/fps)) {
                const auto elapsed=std::chrono::duration<double>(now-started).count();
                const UINT slot=kind==2?std::max(nextSlot,static_cast<UINT>(elapsed*fps)):0;
                if(slot>=totalSlots){missed+=totalSlots-nextSlot;break;}
                missed+=slot-nextSlot;
                if(kind!=2)Png(path,width,height,pixels);
                else if(gpuVideo) {
                    if(!video->TryWriteTexture(output.Get(),slot)) {++missed;nextSlot=slot+1;continue;}
                } else video->Write(pixels,slot);
                ++frames;lastWrittenSlot=slot;nextSlot=slot+1;
                if(now>=nextSizeCheck || kind!=2) {
                    nextSizeCheck=now+std::chrono::seconds(1);
                    CheckCaptureOutputSize(path,options->maximumBytes);
                }
            }
            if(kind==2 && haveFrame && now-started>std::chrono::seconds(options->durationSeconds+3))throw winrt::hresult_error(HRESULT_FROM_WIN32(WAIT_TIMEOUT));
            if(nextSlot<totalSlots)WaitForSingleObject(signal->value,10);
        }
        if(!frames)throw winrt::hresult_error(HRESULT_FROM_WIN32(WAIT_TIMEOUT));
        if(audio) {
            const auto endFrames=uint64_t(lastWrittenSlot+1)*Video::AudioRate/fps;
            const auto endQpc=audioEpoch+10000000ULL*(lastWrittenSlot+1)/fps;
            // The final video frame represents a full frame interval. For a
            // normal duration stop, collect its remaining sound (<=1 frame).
            // An explicit early stop ends capture immediately and pads silence.
            while(!stoppedEarly && widgetrail::capture::CaptureQpc100ns()<endQpc) {
                const auto decision=authority(context);
                if(!decision || !Current(*target,authority,context))throw winrt::hresult_error(E_ABORT);
                if(decision==2)break;
                audioTimeline->Queue(audio->Drain());audioTimeline->FlushThrough(endFrames);Sleep(5);
            }
            audio->Stop();audioTimeline->Queue(audio->Drain());audioTimeline->Finish(endFrames);
        }
        if(video)video->Finish();
        CheckCaptureOutputSize(path,options->maximumBytes);
        result->width=width;result->height=height;result->frames=frames;result->durationSeconds=kind==2?double(lastWrittenSlot+1)/fps:0.0;result->error=S_OK;
        result->sourceWidth=size.Width;result->sourceHeight=size.Height;result->missedFrames=missed;result->encoder=kind==2?options->encoder:0;
        result->audioMode=options->audioMode;
        return S_OK;
    } catch(const winrt::hresult_error& error){result->error=error.code();return error.code();}
      catch(...){result->error=E_FAIL;return E_FAIL;}
}

extern "C" int32_t __stdcall WrailCaptureOverlayScreenshot(const WrailPreviewTarget* target, void* overlay, const wchar_t* path,
    const WrailCaptureOptions* options, WrailPreviewAuthority authority, void* context, WrailCaptureResult* result) noexcept {
    if (!overlay || !options || options->kind != 1) return E_INVALIDARG;
    return RunConfiguredCapture(target, path, options, authority, context, result, nullptr, 0, 0, WrailCaptureTemporaryMaximumBytes, static_cast<HWND>(overlay));
}
extern "C" int32_t __stdcall WrailCaptureWindowConfigured(const WrailPreviewTarget* target,const wchar_t* path,const WrailCaptureOptions* options,
    WrailPreviewAuthority authority,void* context,WrailCaptureResult* result) noexcept {
    return RunConfiguredCapture(target,path,options,authority,context,result);
}
extern "C" int32_t __stdcall WrailCaptureWindowConfiguredV4(const WrailPreviewTarget* target,const wchar_t* path,const WrailCaptureOptions* options,
    const WrailCaptureEncodingSelection* selection,uint32_t sourceWidth,uint32_t sourceHeight,WrailPreviewAuthority authority,void* context,WrailCaptureResult* result) noexcept {
    if(!selection)return E_INVALIDARG;
    return RunConfiguredCapture(target,path,options,authority,context,result,selection,sourceWidth,sourceHeight);
}
extern "C" int32_t __stdcall WrailCaptureWindowLibraryV1(const WrailPreviewTarget* target,const wchar_t* path,const WrailCaptureOptions* options,
    const WrailCaptureEncodingSelection* selection,uint32_t sourceWidth,uint32_t sourceHeight,WrailPreviewAuthority authority,void* context,WrailCaptureResult* result) noexcept {
    if(!options||options->kind!=2||(!selection&&(sourceWidth||sourceHeight)))return E_INVALIDARG;
    return RunConfiguredCapture(target,path,options,authority,context,result,selection,sourceWidth,sourceHeight,WrailCaptureLibraryMaximumBytes);
}
extern "C" int32_t __stdcall WrailCaptureWindow(const WrailPreviewTarget* target, const wchar_t* path, uint32_t kind,
    WrailPreviewAuthority authority, void* context, WrailContextCaptureResult* result) noexcept {
    if(!result || result->size!=sizeof(*result) || result->version!=1 || kind<1 || kind>3) return E_INVALIDARG;
    WrailCaptureOptions options;
    options.kind=kind==2?2:1;
    options.maximumWidth=kind==3?0:kind==2?1280:1920;
    options.maximumHeight=kind==3?0:kind==2?720:1080;
    options.maximumBytes=kind==3?128ULL*1024*1024:8ULL*1024*1024;
    WrailCaptureResult output;
    const auto status=WrailCaptureWindowConfigured(target,path,&options,authority,context,&output);
    result->width=output.width;result->height=output.height;result->frames=output.frames;
    result->durationSeconds=output.durationSeconds;result->error=status;
    return status;
}

extern "C" int32_t __stdcall WrailGetCaptureEncodingEnvironment(WrailCaptureEncodingEnvironment* result) noexcept {
    if(!result||result->size!=sizeof(*result)||result->version!=1)return E_INVALIDARG;
    try{*result=widgetrail::capture::ReadEncodingEnvironment();return S_OK;}catch(...){return winrt::to_hresult();}
}

extern "C" int32_t __stdcall WrailReadCaptureSourceSize(const WrailPreviewTarget* target,uint32_t* width,uint32_t* height) noexcept {
    if(!target||target->size!=sizeof(*target)||target->version!=1||target->reserved||!width||!height)return E_INVALIDARG;
    *width=*height=0;
    try {
        CaptureDpiContext dpi;
        if(!dpi.previous)throw winrt::hresult_error(HRESULT_FROM_WIN32(GetLastError()));
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        struct Apartment{~Apartment(){winrt::uninit_apartment();}} apartment;
        const auto metadataAuthority=[](void*)->int32_t{return 1;};
        if(!Current(*target,metadataAuthority,nullptr))return E_ACCESSDENIED;
        GraphicsCaptureItem item{nullptr};auto interop=winrt::get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();
        winrt::check_hresult(interop->CreateForWindow(reinterpret_cast<HWND>(target->window),winrt::guid_of<GraphicsCaptureItem>(),winrt::put_abi(item)));
        const auto size=item.Size();
        if(size.Width<2||size.Height<2||size.Width>16384||size.Height>16384||int64_t(size.Width)*size.Height>33554432)return E_INVALIDARG;
        if(!Current(*target,metadataAuthority,nullptr))return E_ACCESSDENIED;
        *width=static_cast<uint32_t>(size.Width);*height=static_cast<uint32_t>(size.Height);return S_OK;
    }catch(...){return winrt::to_hresult();}
}

extern "C" int32_t __stdcall WrailProbeCaptureEncodingV2(const WrailCaptureOptions* options,const WrailCaptureEncodingSelection* selection,
    WrailCaptureEncodingProbeV2* result) noexcept {
    if(!options||options->size!=sizeof(*options)||options->version!=3||options->kind!=2||options->encoder!=2||options->audioMode>1||options->reserved||
        options->maximumWidth<2||options->maximumHeight<2||options->maximumWidth>8192||options->maximumHeight>8192||
        options->maximumWidth%2||options->maximumHeight%2||uint64_t(options->maximumWidth)*options->maximumHeight>3840u*2160u||
        (options->framesPerSecond!=15&&options->framesPerSecond!=30&&options->framesPerSecond!=60)||options->bitrate<100000||options->bitrate>100000000||
        !selection||selection->size!=sizeof(*selection)||selection->version!=1||!result||result->size!=sizeof(*result)||result->version!=2)return E_INVALIDARG;
    const auto start=Clock::now();
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);struct Apartment{~Apartment(){winrt::uninit_apartment();}} apartment;
        winrt::check_hresult(MFStartup(MF_VERSION));struct Foundation{~Foundation(){MFShutdown();}} foundation;
        widgetrail::capture::ProbeExplicitEncoding(*options,*selection,*result);
    }catch(...){result->supported=0;result->error=winrt::to_hresult();}
    result->elapsedMilliseconds=std::chrono::duration<double,std::milli>(Clock::now()-start).count();return S_OK;
}
