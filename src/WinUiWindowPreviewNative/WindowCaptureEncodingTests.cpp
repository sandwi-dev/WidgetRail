#include "CaptureVideoEncoder.h"
#include "CaptureSdrColor.h"
#include <cstdio>
#include <chrono>
#include <thread>
#include <string>
#include <psapi.h>

using Microsoft::WRL::ComPtr;
using widgetrail::capture::VideoEncoder;
static double CpuMilliseconds(){FILETIME created{},exited{},kernel{},user{};GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user);
    return double((uint64_t(kernel.dwHighDateTime)<<32|kernel.dwLowDateTime)+(uint64_t(user.dwHighDateTime)<<32|user.dwLowDateTime))/10000;}
static size_t Memory(){PROCESS_MEMORY_COUNTERS counters{};counters.cb=sizeof(counters);GetProcessMemoryInfo(GetCurrentProcess(),&counters,sizeof(counters));return counters.WorkingSetSize;}
static void CheckHdrToSdr(ID3D11Device* device, ID3D11DeviceContext* immediate, const wchar_t* outputDirectory) {
    // Linear scRGB grays: black, 20, 80, 160, 320, 640 and 1000 nits.
    const uint16_t grays[]{0,0x3400,0x3c00,0x4000,0x4400,0x4800,0x4a40};
    constexpr UINT width = ARRAYSIZE(grays);
    uint16_t rgba[width][4]{};
    for (UINT i=0;i<width;++i) {
        rgba[i][0]=rgba[i][1]=rgba[i][2]=grays[i];
        rgba[i][3]=0x3c00;
    }
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width=width;desc.Height=desc.ArraySize=desc.MipLevels=desc.SampleDesc.Count=1;
    desc.Format=DXGI_FORMAT_R16G16B16A16_FLOAT;desc.BindFlags=D3D11_BIND_SHADER_RESOURCE;
    D3D11_SUBRESOURCE_DATA init{rgba,sizeof(rgba),0};
    ComPtr<ID3D11Texture2D> source,dest,read;
    winrt::check_hresult(device->CreateTexture2D(&desc,&init,&source));
    desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.BindFlags=D3D11_BIND_RENDER_TARGET;
    winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&dest));
    desc.Usage=D3D11_USAGE_STAGING;desc.BindFlags=0;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
    winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&read));
    ComPtr<IDXGIDevice> dxgi;winrt::check_hresult(device->QueryInterface(IID_PPV_ARGS(&dxgi)));
    ComPtr<ID2D1Factory1> factory;winrt::check_hresult(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,IID_PPV_ARGS(&factory)));
    ComPtr<ID2D1Device> d2d;winrt::check_hresult(factory->CreateDevice(dxgi.Get(),&d2d));
    ComPtr<ID2D1DeviceContext> dc;winrt::check_hresult(d2d->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE,&dc));
    ComPtr<IDXGISurface> srcSurface,destSurface;winrt::check_hresult(source.As(&srcSurface));winrt::check_hresult(dest.As(&destSurface));
    const auto props=D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET|D2D1_BITMAP_OPTIONS_CANNOT_DRAW,
        D2D1::PixelFormat(desc.Format,D2D1_ALPHA_MODE_PREMULTIPLIED),96,96);
    ComPtr<ID2D1Bitmap1> target;winrt::check_hresult(dc->CreateBitmapFromDxgiSurface(destSurface.Get(),&props,&target));
    widgetrail::capture::CaptureSdrColor color;
    for (const auto white : {80.f,200.f,400.f}) {
        color.Configure(1000.f,white);
        const auto bounds=D2D1::RectF(0,0,float(width),1);
        dc->SetTarget(target.Get());dc->BeginDraw();dc->Clear();
        color.Draw(dc.Get(),srcSurface.Get(),bounds,bounds);
        winrt::check_hresult(dc->EndDraw());dc->SetTarget(nullptr);color.ReleaseInput();
        immediate->CopyResource(read.Get(),dest.Get());
        D3D11_MAPPED_SUBRESOURCE mapped{};winrt::check_hresult(immediate->Map(read.Get(),0,D3D11_MAP_READ,0,&mapped));
        const auto bytes=static_cast<const uint8_t*>(mapped.pData);
        bool valid=bytes[0]<=2 && bytes[4*(width-1)]>=240;
        std::printf("HDR to SDR, white=%.0f nits:",white);
        for (UINT i=0;i<width;++i) {
            const auto pixel=bytes+4*i;
            valid=valid && (!i || pixel[0]>pixel[-4]) && std::abs(pixel[0]-pixel[2])<=2;
            std::printf(" %u",pixel[0]);
        }
        std::printf("\n");immediate->Unmap(read.Get(),0);
        if (!valid)throw winrt::hresult_error(E_UNEXPECTED);
    }
    // Send the tone-mapped GPU output through the same hardware encoder as a
    // one-off clip, so the gray ramp can also be checked in a decoded MP4.
    constexpr UINT videoWidth=448, videoHeight=256;
    desc.Width=videoWidth;desc.Height=videoHeight;desc.Usage=D3D11_USAGE_DEFAULT;
    desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;desc.CPUAccessFlags=0;
    ComPtr<ID3D11Texture2D> canvas;ComPtr<IDXGISurface> surface;ComPtr<ID2D1Bitmap1> videoTarget;
    winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&canvas));winrt::check_hresult(canvas.As(&surface));
    winrt::check_hresult(dc->CreateBitmapFromDxgiSurface(surface.Get(),&props,&videoTarget));
    dc->SetTarget(videoTarget.Get());dc->BeginDraw();dc->Clear();
    color.Draw(dc.Get(),srcSurface.Get(),D2D1::RectF(0,0,float(width),1),D2D1::RectF(0,0,videoWidth,videoHeight));
    winrt::check_hresult(dc->EndDraw());dc->SetTarget(nullptr);color.ReleaseInput();
    WrailCaptureOptions options;options.kind=2;options.maximumWidth=videoWidth;options.maximumHeight=videoHeight;
    options.framesPerSecond=30;options.durationSeconds=1;options.encoder=2;options.bitrate=3000000;
    const auto path=std::wstring(outputDirectory)+L"/hdr-ramp.mp4";
    VideoEncoder encoder(path.c_str(),videoWidth,videoHeight,options,device);
    for (UINT index=0;index<8;++index) {
        if(!encoder.TryWriteTexture(canvas.Get(),index))throw winrt::hresult_error(E_UNEXPECTED);
        std::this_thread::sleep_for(std::chrono::milliseconds(33));
    }
    encoder.Finish();
}
static void Readback(const wchar_t* path,UINT width,UINT height,UINT expectedFrames){
    winrt::check_hresult(MFStartup(MF_VERSION));
    try {
        ComPtr<IMFAttributes> attributes;winrt::check_hresult(MFCreateAttributes(&attributes,1));
        winrt::check_hresult(attributes->SetUINT32(MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING,TRUE));
        ComPtr<IMFSourceReader> reader;winrt::check_hresult(MFCreateSourceReaderFromURL(path,attributes.Get(),&reader));
        ComPtr<IMFMediaType> audio;
        if(SUCCEEDED(reader->GetNativeMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM),0,&audio)))throw winrt::hresult_error(E_UNEXPECTED);
        ComPtr<IMFMediaType> format;winrt::check_hresult(MFCreateMediaType(&format));
        winrt::check_hresult(format->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video));winrt::check_hresult(format->SetGUID(MF_MT_SUBTYPE,MFVideoFormat_RGB32));
        winrt::check_hresult(reader->SetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),nullptr,format.Get()));
        ComPtr<IMFMediaType> actual;winrt::check_hresult(reader->GetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),&actual));
        UINT actualWidth{},actualHeight{};winrt::check_hresult(MFGetAttributeSize(actual.Get(),MF_MT_FRAME_SIZE,&actualWidth,&actualHeight));
        if(actualWidth!=width || actualHeight!=height)throw winrt::hresult_error(E_UNEXPECTED);
        UINT frames=0;LONGLONG previous=-1;
        while(true){DWORD flags{};LONGLONG timestamp{};ComPtr<IMFSample> sample;
            winrt::check_hresult(reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),0,nullptr,&flags,&timestamp,&sample));
            if(flags&MF_SOURCE_READERF_ENDOFSTREAM)break;
            if(!sample)continue;
            if(timestamp<previous)throw winrt::hresult_error(E_UNEXPECTED);previous=timestamp;
            if(frames++==0){ComPtr<IMFMediaBuffer> buffer;winrt::check_hresult(sample->ConvertToContiguousBuffer(&buffer));
                BYTE* data{};DWORD length{};winrt::check_hresult(buffer->Lock(&data,nullptr,&length));
                const bool valid=length>=width*height*4 && data[2]>180 && data[1]<100 && data[0]<130;
                buffer->Unlock();if(!valid)throw winrt::hresult_error(E_UNEXPECTED);}
        }
        if(frames!=expectedFrames)throw winrt::hresult_error(E_UNEXPECTED);
        std::printf("DECODE OK frames=%u dimensions=%ux%u colors=correct audio=none timestamps=ordered\n",frames,width,height);
    }catch(...){MFShutdown();throw;}
    MFShutdown();
}

int wmain(int argc,wchar_t** argv){
    if(argc!=2)return 2;
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        winrt::check_hresult(MFStartup(MF_VERSION));
        IMFActivate** transforms{};UINT count{};
        MFT_REGISTER_TYPE_INFO input{MFMediaType_Video,MFVideoFormat_NV12},output{MFMediaType_Video,MFVideoFormat_H264};
        winrt::check_hresult(MFTEnumEx(MFT_CATEGORY_VIDEO_ENCODER,MFT_ENUM_FLAG_HARDWARE|MFT_ENUM_FLAG_SORTANDFILTER,&input,&output,&transforms,&count));
        std::printf("Hardware H264 transforms: %u\n",count);
        for(UINT i=0;i<count;i++){
            wchar_t* name{};UINT length{};
            if(SUCCEEDED(transforms[i]->GetAllocatedString(MFT_FRIENDLY_NAME_Attribute,&name,&length))){std::wprintf(L"Encoder: %s\n",name);CoTaskMemFree(name);}
            transforms[i]->Release();
        }
        CoTaskMemFree(transforms);MFShutdown();
        ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> immediate;
        winrt::check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT|D3D11_CREATE_DEVICE_VIDEO_SUPPORT,
            nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&immediate));
        CheckHdrToSdr(device.Get(),immediate.Get(),argv[1]);
        struct Profile {UINT width,height,fps,bitrate,encoder;};
        const Profile profiles[]={{1280,720,15,3000000,1},{1280,720,15,3000000,2},{1920,1080,30,8000000,2},
            {1920,1080,60,12000000,2},{3840,2160,30,20000000,2}};
        unsigned supported=0;
        for(const auto& profile:profiles){
            WrailCaptureOptions options;options.kind=2;options.maximumWidth=profile.width;options.maximumHeight=profile.height;
            options.framesPerSecond=profile.fps;options.bitrate=profile.bitrate;options.encoder=profile.encoder;options.durationSeconds=2;
            try {
                {VideoEncoder probe(nullptr,profile.width,profile.height,options,device.Get());}
                std::printf("PROBE OK %ux%u/%u encoder=%u\n",profile.width,profile.height,profile.fps,profile.encoder);
                const auto file=std::wstring(argv[1])+L"/"+std::to_wstring(profile.width)+L"-"+std::to_wstring(profile.height)+L"-"+
                    std::to_wstring(profile.fps)+L"-"+std::to_wstring(profile.encoder)+L".mp4";
                D3D11_TEXTURE2D_DESC desc{};desc.Width=profile.width;desc.Height=profile.height;desc.MipLevels=1;desc.ArraySize=1;
                desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
                ComPtr<ID3D11Texture2D> texture;ComPtr<ID3D11RenderTargetView> view;
                winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&texture));winrt::check_hresult(device->CreateRenderTargetView(texture.Get(),nullptr,&view));
                std::vector<BYTE> software(profile.encoder==1?profile.width*profile.height*4:0);
                const auto beforeCpu=CpuMilliseconds();const auto beforeMemory=Memory();const auto start=std::chrono::steady_clock::now();
                auto encoder=std::make_unique<VideoEncoder>(file.c_str(),profile.width,profile.height,options,device.Get());
                std::printf("INIT OK %ux%u/%u encoder=%u\n",profile.width,profile.height,profile.fps,profile.encoder);std::fflush(stdout);
                const auto begun=std::chrono::steady_clock::now();UINT encoded=0,missed=0;
                for(UINT frame=0;frame<profile.fps*2;frame++){
                    std::this_thread::sleep_until(begun+std::chrono::microseconds(1000000LL*frame/profile.fps));
                    if(profile.encoder==2){const float color[]={frame%2?.1f:.9f,.2f,.3f,1};immediate->ClearRenderTargetView(view.Get(),color);
                        if(encoder->TryWriteTexture(texture.Get(),frame))++encoded;else ++missed;}
                    else {for(size_t i=0;i<software.size();i+=4){software[i]=64;software[i+1]=32;software[i+2]=frame%2?32:224;software[i+3]=255;}
                        encoder->Write(software,frame);++encoded;}
                }
                encoder->Finish();const auto elapsed=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();
                std::printf("ENCODE OK %ux%u/%u encoder=%u frames=%u missed=%u elapsedMs=%.1f cpuMs=%.1f memoryBefore=%zu memoryAfter=%zu\n",
                    profile.width,profile.height,profile.fps,profile.encoder,encoded,missed,elapsed,CpuMilliseconds()-beforeCpu,beforeMemory,Memory());
                encoder.reset();Readback(file.c_str(),profile.width,profile.height,encoded);
                ++supported;
            }catch(const winrt::hresult_error& error){std::printf("UNAVAILABLE %ux%u/%u encoder=%u hr=%08X stage=%ls\n",profile.width,profile.height,profile.fps,profile.encoder,static_cast<unsigned>(error.code().value),VideoEncoder::LastStage());}
            std::fflush(stdout);
        }
        return supported?0:1;
    }catch(const winrt::hresult_error& error){std::printf("FAILED %08X\n",static_cast<unsigned>(error.code().value));return 1;}
}
