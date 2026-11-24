#include "CaptureWorkerProtocol.h"
#include "CapturePacer.h"
#include "CaptureAudioQueue.h"
#include "CaptureQpcClock.h"
#include "LiveH264Source.h"
#include <vector>
#include <cstring>
#include <memory>
#include <new>

extern "C" int32_t __stdcall WrailLiveCaptureRunV2(uint64_t,uint32_t,uint64_t,const wchar_t*,uint32_t,
    uint32_t,uint32_t,uint32_t,capture_worker::Continue,capture_worker::Frame) noexcept;
using VideoTap=void(__stdcall*)(uint32_t,uint32_t,uint32_t,uint32_t,int64_t,ID3D11Texture2D*);
extern "C" int32_t __stdcall WrailLiveCaptureRunWithEncodingTap(uint64_t,uint32_t,uint64_t,const wchar_t*,uint32_t,
    uint32_t,uint32_t,uint32_t,capture_worker::Continue,capture_worker::Frame,VideoTap) noexcept;
namespace {
using namespace capture_worker;
struct Mapping {
    HANDLE handle{};void* data{};
    Mapping(uint64_t value,size_t bytes):handle(reinterpret_cast<HANDLE>(value)),data(MapViewOfFile(handle,FILE_MAP_READ|FILE_MAP_WRITE,0,0,bytes)){}
    ~Mapping(){if(data)UnmapViewOfFile(data);if(handle)CloseHandle(handle);}
};
struct Worker {
    Control* control;uint8_t* video;uint8_t* audio;HANDLE pipe;HANDLE dataReady;
    uint64_t sequence{};uint32_t dropped{};
    LiveAudioQueue queue;
    std::unique_ptr<LiveH264Source> encoder;
    bool Send(Kind kind,uint32_t width,uint32_t height,int64_t time,uint32_t bytes,int32_t result=0) {
        Packet packet;packet.kind=kind;packet.width=width;packet.height=height;packet.microseconds=time;packet.bytes=bytes;
        packet.epoch=control->configuration.epoch;packet.sequence=++sequence;packet.result=result;packet.flags=dropped;
        MemoryBarrier();DWORD written{};
        // Credits bound this pipe to two outstanding media packets plus startup
        // and termination. It never carries pixel or PCM payloads.
        if(!WriteFile(pipe,&packet,sizeof(packet),&written,nullptr)||written!=sizeof(packet)||!SetEvent(dataReady)){
            InterlockedExchange(&control->reason,ERROR_BROKEN_PIPE);InterlockedExchange(&control->stop,1);return false;
        }
        return true;
    }
    void FlushAudio() {
        queue.DiscardExpired(CaptureQpcMicroseconds());
        if(queue.Empty()||InterlockedCompareExchange(&control->audioCredit,0,1)!=1)return;
        auto packet=queue.Pop();
        memcpy(audio,packet.bytes.data(),packet.bytes.size());
        Send(Kind::Audio,48000,2,packet.time,static_cast<uint32_t>(packet.bytes.size()));
    }
    bool Current() {
        if(InterlockedCompareExchange(&control->stop,0,0))return false;
        if(GetTickCount64()-static_cast<uint64_t>(InterlockedCompareExchange64(&control->heartbeat,0,0))>=10000){
            InterlockedExchange(&control->reason,ERROR_TIMEOUT);InterlockedExchange(&control->stop,1);return false;
        }
        FlushAudio();return !InterlockedCompareExchange(&control->stop,0,0);
    }
    void Deliver(uint32_t kind,uint32_t width,uint32_t height,int64_t time,const void* bytes,uint32_t length) {
        if(!Current())return;
        if(kind==1){
            const auto& c=control->configuration;
            if(!bytes||width<2||height<2||width>c.maximumWidth||height>c.maximumHeight||length!=uint64_t(width)*height*4){
                InterlockedExchange(&control->reason,ERROR_INVALID_DATA);InterlockedExchange(&control->stop,1);return;
            }
            if(InterlockedCompareExchange(&control->videoCredit,0,1)!=1){++dropped;return;}
            // Native pixels are copied directly to WebView's mapping. There is
            // no managed frame array, Marshal.Copy, stream copy or JSON pixels.
            memcpy(video,bytes,length);
            if(encoder)encoder->WriteSideData(video+length);
            Send(Kind::Video,width,height,time,length);
        }else if(kind==2&&audio){
            if(!queue.Push(bytes,length,time,CaptureQpcMicroseconds())){
                InterlockedExchange(&control->reason,ERROR_BUFFER_OVERFLOW);InterlockedExchange(&control->stop,1);return;
            }
            FlushAudio();
        }
    }
};
thread_local Worker* active;
void __stdcall Encode(uint32_t width,uint32_t height,uint32_t,uint32_t,int64_t time,ID3D11Texture2D* texture){
    active->encoder->Submit(width,height,time,texture);
}
int32_t __stdcall Current(){return active&&active->Current()?1:0;}
void __stdcall Deliver(uint32_t k,uint32_t w,uint32_t h,int64_t t,const void* p,uint32_t n){
    try{active->Deliver(k,w,h,t,p,n);}
    catch(const std::bad_alloc&){InterlockedCompareExchange(&active->control->reason,ERROR_NOT_ENOUGH_MEMORY,0);InterlockedExchange(&active->control->stop,1);}
    catch(...){InterlockedCompareExchange(&active->control->reason,ERROR_UNHANDLED_EXCEPTION,0);InterlockedExchange(&active->control->stop,1);}
}
uint64_t Handle(const wchar_t* value){wchar_t* end{};const auto result=wcstoull(value,&end,16);return end&&*end==0?result:0;}
}
int wmain(int argc,wchar_t** argv) {
    if(argc!=6)return ERROR_INVALID_PARAMETER;
    try{
        Mapping controlMap(Handle(argv[1]),sizeof(capture_worker::Control));
        if(!controlMap.data)return ERROR_INVALID_HANDLE;
        auto control=static_cast<capture_worker::Control*>(controlMap.data);
        if(control->magic!=capture_worker::Magic||control->version!=capture_worker::Version||!capture_worker::Valid(control->configuration))return ERROR_INVALID_DATA;
        const auto& c=control->configuration;
        const bool native=(c.flags&capture_worker::NativeH264)!=0;
        Mapping videoMap(Handle(argv[2]),size_t(c.maximumWidth)*c.maximumHeight*4+(native?capture_worker::H264Extra:0));
        std::unique_ptr<Mapping> audioMap;
        if(c.includeAudio)audioMap=std::make_unique<Mapping>(Handle(argv[3]),capture_worker::AudioBytes);
        auto pipe=reinterpret_cast<HANDLE>(Handle(argv[4]));
        auto dataReady=reinterpret_cast<HANDLE>(Handle(argv[5]));
        if(!videoMap.data||(audioMap&&!audioMap->data)||!pipe||!dataReady)return ERROR_INVALID_HANDLE;
        Worker worker{control,static_cast<uint8_t*>(videoMap.data),audioMap?static_cast<uint8_t*>(audioMap->data):nullptr,pipe,dataReady};
        active=&worker;worker.Send(capture_worker::Kind::Ready,GetCurrentProcessId(),0,0,0);
        int32_t result{};
#ifdef CAPTURE_WORKER_VALIDATION
        if(c.flags&capture_worker::TestPattern){
            const uint32_t patternWidth=c.maximumHeight>=1080?c.maximumWidth:320;
            const uint32_t patternHeight=c.maximumHeight>=1080?c.maximumHeight:200;
            std::vector<uint32_t> pixels(size_t(patternWidth)*patternHeight);
            std::vector<int16_t> pcm(960*2);
            CapturePacer pacer;auto next=CapturePacer::Clock::now();uint32_t frame{};
            while(worker.Current()){
                pacer.Until(next);next=CapturePacer::Clock::now()+std::chrono::microseconds(LiveCaptureFrameInterval({c.maximumWidth,c.maximumHeight,c.framesPerSecond}));
                std::fill(pixels.begin(),pixels.end(),0xff008000u|((++frame)&255));
                const auto timestamp=CaptureQpcMicroseconds();
                worker.Deliver(1,patternWidth,patternHeight,timestamp,pixels.data(),static_cast<uint32_t>(pixels.size()*4));
                if(c.includeAudio){std::fill(pcm.begin(),pcm.end(),static_cast<int16_t>(frame%30000));
                    worker.Deliver(2,48000,2,timestamp,pcm.data(),static_cast<uint32_t>(pcm.size()*2));}
            }
        }else
#endif
        if(native){
            winrt::init_apartment(winrt::apartment_type::multi_threaded);
            worker.encoder=std::make_unique<LiveH264Source>(control);
            result=WrailLiveCaptureRunWithEncodingTap(c.window,c.processId,c.created,c.className,c.includeAudio,
                c.maximumWidth,c.maximumHeight,c.framesPerSecond,Current,Deliver,Encode);
            worker.encoder.reset();
            winrt::uninit_apartment();
        }else result=WrailLiveCaptureRunV2(c.window,c.processId,c.created,c.className,c.includeAudio,
            c.maximumWidth,c.maximumHeight,c.framesPerSecond,Current,Deliver);
#ifdef CAPTURE_WORKER_VALIDATION
        if(c.flags&capture_worker::TestBlockedExit)Sleep(INFINITE); // Deterministic teardown-hang fixture only.
#endif
        if(result==S_OK&&control->reason)result=HRESULT_FROM_WIN32(control->reason);
        worker.Send(capture_worker::Kind::Ended,0,0,0,0,result);
        active=nullptr;CloseHandle(pipe);CloseHandle(dataReady);return 0;
    }catch(...){return ERROR_UNHANDLED_EXCEPTION;}
}
