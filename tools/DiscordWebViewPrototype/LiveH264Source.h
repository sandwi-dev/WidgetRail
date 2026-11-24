// Optional companion to an authorized raw video track. Media Foundation owns
// encoding; the capture thread owns the writer and drains the callback queue.
#pragma once
#include "CaptureWorkerProtocol.h"
#include "../../src/WinUiWindowPreviewNative/CaptureVideoEncoder.h"
#include <deque>
#include <future>
#include <mutex>
#include <delayimp.h>

namespace capture_worker {
using Microsoft::WRL::ComPtr;
using widgetrail::capture::VideoEncoder;

// H.264 Annex A limits for full-size progressive frames.
struct LiveH264Format {
    uint32_t width, height, fps;
    UINT Level() const noexcept {
        const uint64_t columns=(width+15u)/16u, rows=(height+15u)/16u;
        const auto picture=columns*rows;
        struct Limit { UINT level, picture, perSecond; };
        for(const auto limit:{Limit{31,3600,108000},Limit{32,5120,216000},
            Limit{40,8192,245760},Limit{42,8704,522240},Limit{50,22080,589824},
            Limit{51,36864,983040},Limit{52,36864,2073600}}){
            if(picture<=limit.picture&&picture*fps<=limit.perSecond&&
                columns*columns<=8u*limit.picture&&rows*rows<=8u*limit.picture)return limit.level;
        }
        return 0;
    }
};

struct AccessUnit {
    int64_t time{};
    bool key{};
    std::vector<uint8_t> data;
};

class LiveEncodedOutput final : public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,IMFSampleGrabberSinkCallback2> {
    std::mutex gate;
    std::deque<AccessUnit> ready;
    std::array<std::pair<uint32_t,int64_t>,64> timestamps{};
    bool bad{};
    uint32_t fps{};
public:
    HRESULT RuntimeClassInitialize(uint32_t frameRate) noexcept { fps=frameRate;return S_OK; }
    void Stamp(uint32_t index,int64_t time) {
        std::lock_guard lock(gate);timestamps[index%timestamps.size()]={index,time};
    }
    void Copy(uint8_t* destination,H264FrameHeader& header) {
        std::lock_guard lock(gate);
        if(bad)throw winrt::hresult_error(MF_E_NOTACCEPTING);
        if(ready.empty())return;
        auto& unit=ready.front();header.kind=unit.key?2u:1u;header.microseconds=unit.time;
        header.bytes=static_cast<uint32_t>(unit.data.size());memcpy(destination,unit.data.data(),header.bytes);
        ready.pop_front();
    }
    IFACEMETHOD(OnClockStart)(MFTIME,LONGLONG) override{return S_OK;}
    IFACEMETHOD(OnClockStop)(MFTIME) override{return S_OK;}
    IFACEMETHOD(OnClockPause)(MFTIME) override{return S_OK;}
    IFACEMETHOD(OnClockRestart)(MFTIME) override{return S_OK;}
    IFACEMETHOD(OnClockSetRate)(MFTIME,float) override{return S_OK;}
    IFACEMETHOD(OnSetPresentationClock)(IMFPresentationClock*) override{return S_OK;}
    IFACEMETHOD(OnShutdown)() override{return S_OK;}
    IFACEMETHOD(OnProcessSample)(REFGUID,DWORD,LONGLONG,LONGLONG,const BYTE*,DWORD) override{return E_NOTIMPL;}
    IFACEMETHOD(OnProcessSampleEx)(REFGUID major,DWORD,LONGLONG pts,LONGLONG,const BYTE* data,DWORD count,IMFAttributes* attrs) override {
        std::lock_guard lock(gate);
        if(bad)return S_OK;
        if(pts<0||pts>(static_cast<int64_t>(UINT32_MAX)*10000000)/fps){bad=true;return S_OK;}
        const auto index=static_cast<uint32_t>((pts*fps+5000000)/10000000);
        const auto stamp=timestamps[index%timestamps.size()];
        if(major!=MFMediaType_Video||!data||!count||count>H264Capacity||stamp.first!=index||!stamp.second||ready.size()>=4){
            bad=true;return S_OK;
        }
        UINT32 clean{};if(attrs)attrs->GetUINT32(MFSampleExtension_CleanPoint,&clean);
        try{ready.push_back({stamp.second,clean!=0,{data,data+count}});}
        catch(...){bad=true;}
        return S_OK;
    }
};

struct LiveH264Writer {
    bool platformStarted{};
    ComPtr<LiveEncodedOutput> output;
    ComPtr<IMFMediaSink> sink;
    std::unique_ptr<VideoEncoder> encoder;
    ~LiveH264Writer() {
        encoder.reset();
        if(sink)sink->Shutdown();
        sink.Reset();
        output.Reset();
        if(platformStarted)MFShutdown();
    }
    void Open(ID3D11Device* device,uint32_t target,LiveH264Format format) {
        // Raw capture also runs on Windows installations without the Media
        // Feature Pack. Resolve optional imports only when encoding is requested.
        static const HRESULT runtime=[]() -> HRESULT {
            struct Library {const wchar_t* systemName;const char* importName;};
            for(const auto& dll:{Library{L"MF.dll","MF.dll"},Library{L"MFPlat.dll","MFPlat.DLL"},Library{L"MFReadWrite.dll","MFReadWrite.dll"}}){
                const auto module=LoadLibraryExW(dll.systemName,nullptr,LOAD_LIBRARY_SEARCH_SYSTEM32);
                if(!module)return HRESULT_FROM_WIN32(GetLastError());
                const auto hr=__HrLoadAllImportsForDll(dll.importName);
                FreeLibrary(module);
                if(FAILED(hr))return hr;
            }
            return S_OK;
        }();
        winrt::check_hresult(runtime);
        winrt::check_hresult(MFStartup(MF_VERSION));platformStarted=true;
        const auto level=format.Level();
        if(!level)throw winrt::hresult_error(MF_E_INVALIDMEDIATYPE);
        WrailCaptureOptions options{};options.kind=2;options.encoder=2;options.framesPerSecond=format.fps;options.bitrate=target;
        auto type=VideoEncoder::VideoOutputType(format.width,format.height,options,nullptr,level);
        winrt::check_hresult(Microsoft::WRL::MakeAndInitialize<LiveEncodedOutput>(&output,format.fps));
        ComPtr<IMFActivate> activation;winrt::check_hresult(MFCreateSampleGrabberSinkActivate(type.Get(),output.Get(),&activation));
        winrt::check_hresult(activation->SetUINT32(MF_SAMPLEGRABBERSINK_IGNORE_CLOCK,TRUE));
        winrt::check_hresult(activation->ActivateObject(IID_PPV_ARGS(&sink)));
        encoder=std::make_unique<VideoEncoder>(nullptr,format.width,format.height,options,device,false,sink.Get(),nullptr,nullptr,level);
    }
    static std::unique_ptr<LiveH264Writer> Create(ID3D11Device* device,uint32_t target,LiveH264Format format) {
        auto writer=std::make_unique<LiveH264Writer>();
        writer->Open(device,target,format);
        return writer;
    }
};

class LiveH264Source {
    Control* control;
    uint32_t generation{},index{},rate{},bufferRate{},preparingRate{};
    int bufferDirection{};
    int64_t bufferChangeSince{};
    LONG keyRequests{};
    bool failed{},prepareFailed{};
    LiveH264Format format{};
    ComPtr<ID3D11Texture2D> activeTexture;
    std::unique_ptr<LiveH264Writer> writer;
    ComPtr<LiveEncodedOutput> draining;
    std::future<std::unique_ptr<LiveH264Writer>> preparing;

    void Reset() noexcept {
        // A pending task owns only its device reference. Invalidate its result
        // on suspend or replacement without waiting on the capture thread.
        preparingRate=0;
        writer.reset();activeTexture.Reset();draining.Reset();
        index=rate=bufferRate=0;bufferDirection=0;prepareFailed=false;
    }
    static int RateDirection(uint64_t target,uint64_t basis) {
        if(target*2>basis*3)return 1;
        return target*3<basis*2?-1:0;
    }
    void CheckRateBuffer(ID3D11Texture2D* texture,uint32_t target,int64_t time) {
        // Apply every allocation immediately. Some hardware accepts the new mean
        // rate but retains its original HRD buffer until the next writer starts.
        const int direction=RateDirection(target,bufferRate);
        if(direction!=bufferDirection){
            bufferDirection=direction;bufferChangeSince=time;prepareFailed=false;preparingRate=0;
        }
        if(preparing.valid()){
            if(preparing.wait_for(std::chrono::seconds(0))!=std::future_status::ready)return;
            std::unique_ptr<LiveH264Writer> next;
            try{next=preparing.get();}
            catch(...){if(preparingRate)prepareFailed=true;return;}
            if(!direction||!preparingRate||RateDirection(target,preparingRate))return;
            // Configure the latest allocation before submitting its first frame.
            if(target!=preparingRate){
                try{next->encoder->UpdateLiveH264(target,false);}
                catch(...){prepareFailed=true;return;}
            }
            writer->encoder->Finish();
            draining=writer->output;
            writer=std::move(next);
            index=0;bufferDirection=0;bufferRate=preparingRate;
            return;
        }
        if(!direction||time-bufferChangeSince<2000000||prepareFailed||draining)return;
        const uint64_t effective=uint64_t(writer->encoder->LiveBufferBytes())*80;
        if(!RateDirection(target,effective)){
            bufferRate=target;bufferDirection=0;return;
        }
        // MFT initialization can take hundreds of milliseconds. The existing
        // writer continues to encode while the replacement opens on the same
        // multithread-protected D3D device. Never queue a second preparation.
        ComPtr<ID3D11Device> device;texture->GetDevice(&device);
        preparingRate=target;
        try{
            preparing=std::async(std::launch::async,[device,target,format=format] {
                winrt::init_apartment(winrt::apartment_type::multi_threaded);
                struct Apartment {~Apartment(){winrt::uninit_apartment();}} apartment;
                return LiveH264Writer::Create(device.Get(),target,format);
            });
        }catch(...){prepareFailed=true;}
    }
public:
    explicit LiveH264Source(Control* value):control(value){}
    ~LiveH264Source(){Reset();}
    void Submit(uint32_t width,uint32_t height,int64_t time,ID3D11Texture2D* texture) noexcept {
        const auto config=static_cast<uint64_t>(InterlockedCompareExchange64(&control->encoding,0,0));
        const auto nextGeneration=static_cast<uint32_t>(config>>32),target=static_cast<uint32_t>(config);
        const LONG requests=InterlockedCompareExchange(&control->keyframeRequests,0,0);
        if(nextGeneration!=generation){Reset();failed=false;generation=nextGeneration;keyRequests=requests;}
        if(!preparingRate&&preparing.valid()&&preparing.wait_for(std::chrono::seconds(0))==std::future_status::ready){
            try{preparing.get();}catch(...){ /* Already invalidated. */ }
        }
        if(!target||!generation){if(writer)Reset();return;}
        if(failed)return;
        try{
            const auto& capture=control->configuration;
            if(!texture||width<2||height<2||(width%2)||(height%2)||
                width>capture.maximumWidth||height>capture.maximumHeight||
                (activeTexture&&(format.width!=width||format.height!=height||activeTexture.Get()!=texture)))
                throw winrt::hresult_error(MF_E_INVALIDMEDIATYPE);
            if(!writer){
                ComPtr<ID3D11Device> device;texture->GetDevice(&device);
                format={width,height,capture.framesPerSecond};
                writer=LiveH264Writer::Create(device.Get(),target,format);
                activeTexture=texture;rate=bufferRate=target;
            }
            if(target!=rate||requests!=keyRequests){writer->encoder->UpdateLiveH264(target,requests!=keyRequests);rate=target;keyRequests=requests;}
            // Only produce a unit on an admitted raw tick. A blocked page
            // cannot accumulate compressed frames in an unbounded queue.
            if(InterlockedCompareExchange(&control->videoCredit,0,0)==0)return;
            CheckRateBuffer(texture,target,time);
            writer->output->Stamp(index,time);
            if(writer->encoder->TryWriteTexture(texture,index))++index;
        }catch(...){Reset();failed=true;}
    }
    void WriteSideData(uint8_t* destination) noexcept {
        H264FrameHeader header{};header.generation=generation;
        if(writer&&!failed){
            try{
                if(draining){
                    draining->Copy(destination+sizeof(header),header);
                    if(!header.kind)draining.Reset();
                }
                if(!header.kind)writer->output->Copy(destination+sizeof(header),header);
            }
            catch(...){Reset();failed=true;}
        }
        if(failed)header.kind=3;
        memcpy(destination,&header,sizeof(header));
    }
};
}
