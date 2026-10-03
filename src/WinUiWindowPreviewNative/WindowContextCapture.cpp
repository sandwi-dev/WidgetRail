#include "WindowContextCapture.h"
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
#include <vector>
using Microsoft::WRL::ComPtr;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
namespace {
std::atomic_bool busy{};
using Clock = std::chrono::steady_clock;
constexpr uint32_t Fps = 15, VideoFrames = 75;
struct Event { HANDLE value{CreateEventW(nullptr, FALSE, FALSE, nullptr)}; ~Event() { if(value) CloseHandle(value); } };
bool Current(const WrailPreviewTarget& expected, WrailPreviewAuthority authority, void* context) {
    if (!authority(context)) return false;
    const auto hwnd = reinterpret_cast<HWND>(expected.window);
    WrailPreviewTarget actual{};
    if (FAILED(WrailPreviewReadIdentity(expected.window, &actual)) || actual.processId != expected.processId ||
        actual.processCreated != expected.processCreated || wcscmp(actual.className, expected.className) ||
        GetAncestor(GetForegroundWindow(), GA_ROOT) != hwnd || !IsWindowVisible(hwnd) || IsIconic(hwnd)) return false;
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
struct Video {
    ComPtr<IMFSinkWriter> writer; DWORD stream{}; bool started{};
    Video(const wchar_t* path, UINT w, UINT h) {
        winrt::check_hresult(MFStartup(MF_VERSION)); started=true;
        try {
            ComPtr<IMFAttributes> attributes; winrt::check_hresult(MFCreateAttributes(&attributes, 1));
            winrt::check_hresult(attributes->SetUINT32(MF_SINK_WRITER_DISABLE_THROTTLING, TRUE));
            winrt::check_hresult(MFCreateSinkWriterFromURL(path, nullptr, attributes.Get(), &writer));
            ComPtr<IMFMediaType> output; winrt::check_hresult(MFCreateMediaType(&output));
            winrt::check_hresult(output->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video)); winrt::check_hresult(output->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_H264));
            winrt::check_hresult(output->SetUINT32(MF_MT_AVG_BITRATE, 3000000)); winrt::check_hresult(output->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
            winrt::check_hresult(MFSetAttributeSize(output.Get(), MF_MT_FRAME_SIZE, w,h)); winrt::check_hresult(MFSetAttributeRatio(output.Get(), MF_MT_FRAME_RATE, Fps,1));
            winrt::check_hresult(MFSetAttributeRatio(output.Get(), MF_MT_PIXEL_ASPECT_RATIO,1,1)); winrt::check_hresult(writer->AddStream(output.Get(), &stream));
            ComPtr<IMFMediaType> input; winrt::check_hresult(MFCreateMediaType(&input));
            winrt::check_hresult(input->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video)); winrt::check_hresult(input->SetGUID(MF_MT_SUBTYPE, MFVideoFormat_RGB32));
            winrt::check_hresult(input->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
            winrt::check_hresult(MFSetAttributeSize(input.Get(), MF_MT_FRAME_SIZE,w,h)); winrt::check_hresult(MFSetAttributeRatio(input.Get(), MF_MT_FRAME_RATE,Fps,1));
            winrt::check_hresult(MFSetAttributeRatio(input.Get(), MF_MT_PIXEL_ASPECT_RATIO,1,1));
            winrt::check_hresult(input->SetUINT32(MF_MT_DEFAULT_STRIDE,w*4));
            winrt::check_hresult(writer->SetInputMediaType(stream,input.Get(),nullptr)); winrt::check_hresult(writer->BeginWriting());
        } catch (...) { writer.Reset(); MFShutdown(); started=false; throw; }
    }
    ~Video() { writer.Reset(); if(started) MFShutdown(); }
    void Write(const std::vector<BYTE>& bytes, UINT index) {
        ComPtr<IMFMediaBuffer> buffer; winrt::check_hresult(MFCreateMemoryBuffer(static_cast<DWORD>(bytes.size()),&buffer));
        BYTE* target{}; winrt::check_hresult(buffer->Lock(&target,nullptr,nullptr)); memcpy(target,bytes.data(),bytes.size());
        winrt::check_hresult(buffer->Unlock()); winrt::check_hresult(buffer->SetCurrentLength(static_cast<DWORD>(bytes.size())));
        ComPtr<IMFSample> sample; winrt::check_hresult(MFCreateSample(&sample)); winrt::check_hresult(sample->AddBuffer(buffer.Get()));
        const LONGLONG start=10000000LL*index/Fps, end=10000000LL*(index+1)/Fps;
        winrt::check_hresult(sample->SetSampleTime(start)); winrt::check_hresult(sample->SetSampleDuration(end-start));
        winrt::check_hresult(writer->WriteSample(stream,sample.Get()));
    }
    void Finish() { winrt::check_hresult(writer->Finalize()); }
};
}
extern "C" int32_t __stdcall WrailCaptureWindow(const WrailPreviewTarget* target, const wchar_t* path, uint32_t kind,
    WrailPreviewAuthority authority, void* context, WrailContextCaptureResult* result) noexcept {
    if(!target || target->size!=sizeof(*target) || target->version!=1 || !path || !*path || !authority || !result ||
        result->size!=sizeof(*result) || result->version!=1 || (kind!=1 && kind!=2)) return E_INVALIDARG;
    if(busy.exchange(true)) return HRESULT_FROM_WIN32(ERROR_BUSY);
    struct BusyGuard { ~BusyGuard(){busy=false;} } busyGuard;
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        struct Apartment { ~Apartment(){winrt::uninit_apartment();} } apartment;
        if(!Current(*target,authority,context)) throw winrt::hresult_error(E_ACCESSDENIED);
        if(!GraphicsCaptureSession::IsSupported()) throw winrt::hresult_error(E_NOTIMPL);
        ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> immediate;
        winrt::check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&immediate));
        ComPtr<IDXGIDevice> dxgi; winrt::check_hresult(device.As(&dxgi));
        winrt::com_ptr<IInspectable> inspectable; winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.Get(),inspectable.put()));
        auto captureDevice=inspectable.as<IDirect3DDevice>();
        Capture capture;
        auto interop=winrt::get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();
        winrt::check_hresult(interop->CreateForWindow(reinterpret_cast<HWND>(target->window),winrt::guid_of<GraphicsCaptureItem>(),winrt::put_abi(capture.item)));
        auto size=capture.item.Size();
        if(size.Width<=0 || size.Height<=0 || size.Width>8192 || size.Height>8192 || int64_t(size.Width)*size.Height>20000000) throw winrt::hresult_error(E_INVALIDARG);
        auto scale=std::min({1.0,(kind==1?1920.0:1280.0)/size.Width,(kind==1?1080.0:720.0)/size.Height});
        UINT width=std::max(2U,static_cast<UINT>(size.Width*scale)&~1U), height=std::max(2U,static_cast<UINT>(size.Height*scale)&~1U);
        D3D11_TEXTURE2D_DESC desc{};desc.Width=width;desc.Height=height;desc.MipLevels=1;desc.ArraySize=1;
        desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
        ComPtr<ID3D11Texture2D> output;winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&output));
        desc.BindFlags=0;desc.Usage=D3D11_USAGE_STAGING;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Texture2D> staging;winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&staging));
        ComPtr<ID2D1Factory1> factory;winrt::check_hresult(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,IID_PPV_ARGS(&factory)));
        ComPtr<ID2D1Device> d2d;winrt::check_hresult(factory->CreateDevice(dxgi.Get(),&d2d));
        ComPtr<ID2D1DeviceContext> drawing;winrt::check_hresult(d2d->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE,&drawing));
        ComPtr<IDXGISurface> outputSurface;winrt::check_hresult(output.As(&outputSurface));
        auto targetProperties=D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET|D2D1_BITMAP_OPTIONS_CANNOT_DRAW,D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_PREMULTIPLIED),96,96);
        ComPtr<ID2D1Bitmap1> outputBitmap;winrt::check_hresult(drawing->CreateBitmapFromDxgiSurface(outputSurface.Get(),&targetProperties,&outputBitmap));
        auto signal=std::make_shared<Event>();if(!signal->value) throw winrt::hresult_error(E_OUTOFMEMORY);
        capture.pool=Direct3D11CaptureFramePool::CreateFreeThreaded(captureDevice,DirectXPixelFormat::B8G8R8A8UIntNormalized,2,size);
        capture.arrived=capture.pool.FrameArrived([signal](auto&&,auto&&){SetEvent(signal->value);});
        capture.session=capture.pool.CreateCaptureSession(capture.item);capture.session.IsCursorCaptureEnabled(false);
        capture.session.StartCapture();
        std::unique_ptr<Video> video;
        std::vector<BYTE> pixels(width*height*4);
        bool haveFrame=false;auto firstDeadline=Clock::now()+std::chrono::seconds(3);auto started=Clock::now();
        UINT frames=0;
        while(frames<(kind==1?1U:VideoFrames)) {
            if(!authority(context)) throw winrt::hresult_error(E_ABORT);
            if(haveFrame && Clock::now()<started+std::chrono::milliseconds(1000*frames/Fps)) {WaitForSingleObject(signal->value,10);continue;}
            if(!Current(*target,authority,context)) throw winrt::hresult_error(E_ABORT);
            auto frame=capture.pool.TryGetNextFrame();
            if(frame) {
                struct FrameGuard {Direct3D11CaptureFrame& frame;~FrameGuard(){try{if(frame)frame.Close();}catch(...){}}} guard{frame};
                if(frame.ContentSize().Width!=size.Width || frame.ContentSize().Height!=size.Height) throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_RETRY));
                auto access=frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>();
                ComPtr<IDXGISurface> source;winrt::check_hresult(access->GetInterface(IID_PPV_ARGS(&source)));
                auto properties=D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_NONE,D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_PREMULTIPLIED),96,96);
                ComPtr<ID2D1Bitmap1> bitmap;winrt::check_hresult(drawing->CreateBitmapFromDxgiSurface(source.Get(),&properties,&bitmap));
                drawing->SetTarget(outputBitmap.Get());drawing->BeginDraw();drawing->Clear(D2D1::ColorF(0.f,0.f,0.f,1.f));
                drawing->DrawBitmap(bitmap.Get(),D2D1::RectF(0,0,float(width),float(height)),1,D2D1_INTERPOLATION_MODE_LINEAR,D2D1::RectF(0,0,float(size.Width),float(size.Height)));
                auto hr=drawing->EndDraw();drawing->SetTarget(nullptr);winrt::check_hresult(hr);
                immediate->CopyResource(staging.Get(),output.Get());D3D11_MAPPED_SUBRESOURCE mapped{};
                winrt::check_hresult(immediate->Map(staging.Get(),0,D3D11_MAP_READ,0,&mapped));
                for(UINT y=0;y<height;++y)memcpy(pixels.data()+y*width*4,static_cast<BYTE*>(mapped.pData)+y*mapped.RowPitch,width*4);
                immediate->Unmap(staging.Get(),0);
                if(!haveFrame){haveFrame=true;started=Clock::now();if(kind==2)video=std::make_unique<Video>(path,width,height);}
            }
            auto now=Clock::now();
            if(!haveFrame){if(now>=firstDeadline)throw winrt::hresult_error(HRESULT_FROM_WIN32(WAIT_TIMEOUT));}
            else if(now>=started+std::chrono::milliseconds(1000*frames/Fps)) {
                if(kind==1)Png(path,width,height,pixels);else video->Write(pixels,frames);
                ++frames;
            }
            if(kind==2 && haveFrame && now-started>std::chrono::seconds(8))throw winrt::hresult_error(HRESULT_FROM_WIN32(WAIT_TIMEOUT));
            if(frames<(kind==1?1U:VideoFrames))WaitForSingleObject(signal->value,10);
        }
        if(video)video->Finish();
        result->width=width;result->height=height;result->frames=frames;result->durationSeconds=kind==2?5.0:0.0;result->error=S_OK;
        return S_OK;
    } catch(const winrt::hresult_error& error){result->error=error.code();return error.code();}
      catch(...){result->error=E_FAIL;return E_FAIL;}
}
