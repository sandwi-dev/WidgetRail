// Native live-media implementation for the isolated web-capture worker and
// validation harness. Existing Game Capture/preview exports are unchanged.
#include "../../src/WinUiWindowPreviewNative/ProcessLoopbackCapture.h"
#include "../../src/WinUiWindowPreviewNative/CaptureGeometry.h"
#include "CapturePacer.h"
#include "CaptureFrameSchedule.h"
#include "CaptureQuality.h"
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
using Microsoft::WRL::ComPtr;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
using Continue = int32_t(__stdcall*)();
// Borrowed data is valid ONLY during callback; no managed exception may cross ABI.
using Frame = void(__stdcall*)(uint32_t kind, uint32_t width, uint32_t height, int64_t microseconds, const void* data, uint32_t bytes);
namespace {
int64_t QpcMicroseconds() {
    LARGE_INTEGER ticks{},rate{};
    winrt::check_bool(QueryPerformanceCounter(&ticks));winrt::check_bool(QueryPerformanceFrequency(&rate));
    return ticks.QuadPart/rate.QuadPart*1000000 + ticks.QuadPart%rate.QuadPart*1000000/rate.QuadPart;
}
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
extern "C" __declspec(dllexport) int32_t __stdcall WrailLiveCaptureRunV2(uint64_t window, uint32_t pid, uint64_t created,
    const wchar_t* className, uint32_t includeAudio, uint32_t maximumWidth, uint32_t maximumHeight,
    uint32_t framesPerSecond, Continue current, Frame deliver) noexcept {
    const LiveCaptureQuality quality{maximumWidth,maximumHeight,framesPerSecond};
    if(!window||!pid||!className||!current||!deliver||includeAudio>1||!ValidLiveCaptureQuality(quality))return E_INVALIDARG;
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        struct Apartment {~Apartment(){winrt::uninit_apartment();}} apartment;
        auto previousDpi=SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        struct Dpi {DPI_AWARENESS_CONTEXT old;~Dpi(){if(old)SetThreadDpiAwarenessContext(old);}} dpi{previousDpi};
        auto hwnd=reinterpret_cast<HWND>(window);
        if(!current()||!Valid(hwnd,pid,created,className))return E_ACCESSDENIED;
        ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> immediate;
        winrt::check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&immediate));
        ComPtr<IDXGIDevice> dxgi;winrt::check_hresult(device.As(&dxgi));
        winrt::com_ptr<IInspectable> inspectable;winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.Get(),inspectable.put()));
        auto captureDevice=inspectable.as<IDirect3DDevice>();
        Capture capture;
        auto interop=winrt::get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();
        winrt::check_hresult(interop->CreateForWindow(hwnd,winrt::guid_of<GraphicsCaptureItem>(),winrt::put_abi(capture.item)));
        auto size=capture.item.Size();
        UINT width{},height{};
        ComPtr<ID3D11Texture2D> output,staging;
        ComPtr<ID2D1Bitmap1> targetBitmap;
        std::vector<uint8_t> pixels;
        ComPtr<ID2D1Factory1> factory;winrt::check_hresult(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,IID_PPV_ARGS(&factory)));
        ComPtr<ID2D1Device> d2d;winrt::check_hresult(factory->CreateDevice(dxgi.Get(),&d2d));
        ComPtr<ID2D1DeviceContext> drawing;winrt::check_hresult(d2d->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE,&drawing));
        const auto configureOutput=[&] {
            if(size.Width<=0||size.Height<=0||size.Width>16384||size.Height>16384||int64_t(size.Width)*size.Height>33554432)
                throw winrt::hresult_error(E_INVALIDARG);
            // Fit the selected bounded envelope; never upscale or distort the
            // source. A resize can grow a previously small source within it.
            const auto geometry=ConfiguredCaptureSize(size.Width,size.Height,quality.maximumWidth,quality.maximumHeight,true);
            if(!geometry.width||!geometry.height)throw winrt::hresult_error(E_INVALIDARG);
            if(width==geometry.width&&height==geometry.height)return;
            drawing->SetTarget(nullptr);targetBitmap.Reset();output.Reset();staging.Reset();
            width=geometry.width;height=geometry.height;
            D3D11_TEXTURE2D_DESC desc{};desc.Width=width;desc.Height=height;desc.MipLevels=1;desc.ArraySize=1;
            desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
            winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&output));
            desc.BindFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
            winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&staging));
            ComPtr<IDXGISurface> target;winrt::check_hresult(output.As(&target));
            auto props=D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET|D2D1_BITMAP_OPTIONS_CANNOT_DRAW,D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_PREMULTIPLIED),96,96);
            winrt::check_hresult(drawing->CreateBitmapFromDxgiSurface(target.Get(),&props,&targetBitmap));
            pixels.resize(size_t(width)*height*4);
        };
        configureOutput();
        capture.pool=Direct3D11CaptureFramePool::CreateFreeThreaded(captureDevice,DirectXPixelFormat::B8G8R8A8UIntNormalized,2,size);
        auto frameAvailable=std::make_shared<widgetrail::capture::CaptureEvent>();
        auto arrival=capture.pool.FrameArrived(winrt::auto_revoke,[frameAvailable](auto&&,auto&&){frameAvailable->Signal();});
        auto closed=capture.item.Closed(winrt::auto_revoke,[frameAvailable](auto&&,auto&&){frameAvailable->Signal();});
        capture.session=capture.pool.CreateCaptureSession(capture.item);capture.session.IsCursorCaptureEnabled(false);
        // Request borderless capture only through the supported Windows session
        // API. Windows still enforces the user's screenshot-border permission,
        // and another capturing application can require the border. Unsupported
        // or denied optional configuration must not fail an admitted share.
        if(auto border=capture.session.try_as<IGraphicsCaptureSession3>()) {
            try { border.IsBorderRequired(false); }
            catch(const winrt::hresult_error&) { /* Keep ordinary bordered capture. */ }
        }
        std::unique_ptr<widgetrail::capture::ProcessLoopbackCapture> audio;
        if(includeAudio)audio=std::make_unique<widgetrail::capture::ProcessLoopbackCapture>(pid);

        capture.session.StartCapture();
        using Clock=std::chrono::steady_clock;
        CapturePacer pacer;
        auto next=Clock::now();auto nextAudio=next;auto lastVideo=next;bool haveVideo=false;
        while(current()) {
            if(!Valid(hwnd,pid,created,className))return HRESULT_FROM_WIN32(ERROR_INVALID_WINDOW_HANDLE);
            if(audio && Clock::now()>=nextAudio) {
                nextAudio=Clock::now()+std::chrono::milliseconds(20);
                auto packets=audio->Drain();
                if(!packets.empty()) {
                    std::vector<int16_t> joined;
                    for(const auto& packet:packets)joined.insert(joined.end(),packet.samples.begin(),packet.samples.end());
                    deliver(2,48000,2,static_cast<int64_t>(packets.front().qpc100ns/10),joined.data(),static_cast<uint32_t>(joined.size()*2));
                }
            }
            if(Clock::now()<next){pacer.Until(audio?std::min(next,nextAudio):next);continue;}
            const auto nextFrame=AdvanceCaptureDeadline<Clock>(next,Clock::now(),
                std::chrono::microseconds(LiveCaptureFrameInterval(quality)));
            DWORD cloaked{};DwmGetWindowAttribute(hwnd,DWMWA_CLOAKED,&cloaked,sizeof(cloaked));
            if(IsIconic(hwnd)||!IsWindowVisible(hwnd)||cloaked){
                // Keep the connection alive with black frames while the selected
                // window is unavailable; never switch to another application.
                std::fill(pixels.begin(),pixels.end(),uint8_t{0});
                deliver(1,width,height,QpcMicroseconds(),pixels.data(),static_cast<uint32_t>(pixels.size()));
                next=nextFrame;
                continue;
            }
            auto frame=capture.pool.TryGetNextFrame();
            if(!frame){
                // Idle content uses a low refresh cadence. Changing content
                // continues at the selected capture cadence above.
                if(haveVideo&&Clock::now()-lastVideo>=std::chrono::milliseconds(250)){
                    deliver(1,width,height,QpcMicroseconds(),pixels.data(),static_cast<uint32_t>(pixels.size()));lastVideo=Clock::now();next=nextFrame;
                }else{
                    auto wake=haveVideo?lastVideo+std::chrono::milliseconds(250):nextFrame;
                    if(audio)wake=std::min(wake,nextAudio);
                    pacer.Until(wake,frameAvailable->Get());
                }
                continue;
            }
            while(auto newer=capture.pool.TryGetNextFrame()){frame.Close();frame=newer;}
            if(frame.ContentSize().Width!=size.Width||frame.ContentSize().Height!=size.Height){
                const auto resized=frame.ContentSize();frame.Close();
                if(resized.Width>0&&resized.Height>0&&resized.Width<=16384&&resized.Height<=16384){size=resized;configureOutput();haveVideo=false;capture.pool.Recreate(captureDevice,DirectXPixelFormat::B8G8R8A8UIntNormalized,2,size);}
                continue;
            }
            auto access=frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
            ComPtr<IDXGISurface> surface;winrt::check_hresult(access->GetInterface(IID_PPV_ARGS(&surface)));
            auto props=D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_NONE,D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_PREMULTIPLIED),96,96);
            ComPtr<ID2D1Bitmap1> bitmap;winrt::check_hresult(drawing->CreateBitmapFromDxgiSurface(surface.Get(),&props,&bitmap));
            drawing->SetTarget(targetBitmap.Get());drawing->BeginDraw();drawing->Clear(D2D1::ColorF(0.f,0.f,0.f,1.f));
            const auto scale=std::min(float(width)/float(size.Width),float(height)/float(size.Height));
            const auto drawnWidth=scale*float(size.Width),drawnHeight=scale*float(size.Height);
            const auto left=(float(width)-drawnWidth)/2,top=(float(height)-drawnHeight)/2;
            drawing->DrawBitmap(bitmap.Get(),D2D1::RectF(left,top,left+drawnWidth,top+drawnHeight),1,D2D1_INTERPOLATION_MODE_LINEAR,D2D1::RectF(0,0,float(size.Width),float(size.Height)));
            auto hr=drawing->EndDraw();drawing->SetTarget(nullptr);winrt::check_hresult(hr);
            immediate->CopyResource(staging.Get(),output.Get());D3D11_MAPPED_SUBRESOURCE mapped{};
            winrt::check_hresult(immediate->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped));
            for(UINT y=0;y<height;++y)memcpy(pixels.data()+size_t(y)*width*4,static_cast<uint8_t*>(mapped.pData)+size_t(y)*mapped.RowPitch,width*4);
            immediate->Unmap(staging.Get(),0);
            deliver(1,width,height,QpcMicroseconds(),pixels.data(),static_cast<uint32_t>(pixels.size()));frame.Close();haveVideo=true;lastVideo=Clock::now();next=nextFrame;
        }
        return S_OK;
    }catch(...){return winrt::to_hresult();}
}

// Preserve the original prototype ABI for existing, separately built callers.
extern "C" __declspec(dllexport) int32_t __stdcall WrailLiveCaptureRun(uint64_t window, uint32_t pid, uint64_t created,
    const wchar_t* className, uint32_t includeAudio, Continue current, Frame deliver) noexcept {
    return WrailLiveCaptureRunV2(window,pid,created,className,includeAudio,2560,720,30,current,deliver);
}
