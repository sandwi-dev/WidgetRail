#include "CaptureVideoEncoder.h"
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
