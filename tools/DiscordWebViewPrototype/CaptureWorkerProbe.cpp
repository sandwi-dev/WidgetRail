#include "CaptureWorkerProtocol.h"
#include <cstdio>
#include <vector>
#include <string>
#include <stdexcept>
#include <psapi.h>

using namespace capture_worker;
namespace {
struct Handle {
    HANDLE value{};
    explicit Handle(HANDLE h=nullptr):value(h){}
    ~Handle(){if(value&&value!=INVALID_HANDLE_VALUE)CloseHandle(value);}
};
void Require(bool value,const char* text){if(!value)throw std::runtime_error(text);}
void Success(int32_t result,const char* text){if(FAILED(result)){std::printf("HRESULT 0x%08x\n",static_cast<uint32_t>(result));throw std::runtime_error(text);}}
std::wstring Directory(){wchar_t path[32768]{};Require(GetModuleFileNameW(nullptr,path,32768)>0,"Executable path");std::wstring result(path);return result.substr(0,result.find_last_of(L"\\/"));}
struct Owner {
    Handle video;
    Handle audio;
    uint32_t* pixels{};int16_t* samples{};void* session{};
    Owner(uint32_t fps,uint32_t flags=TestPattern,bool includeAudio=false,HWND window=nullptr,uint32_t maximumWidth=2560,uint32_t maximumHeight=720){
        const auto videoBytes=maximumWidth*maximumHeight*4;
        video.value=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,videoBytes,nullptr);
        Require(video.value!=nullptr,"Video mapping");pixels=static_cast<uint32_t*>(MapViewOfFile(video.value,FILE_MAP_READ|FILE_MAP_WRITE,0,0,videoBytes));
        Require(pixels!=nullptr,"Video view");Configuration options;options.flags=flags;options.epoch=27;options.maximumWidth=maximumWidth;options.maximumHeight=maximumHeight;options.framesPerSecond=fps;
        if(includeAudio){audio.value=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,AudioBytes,nullptr);Require(audio.value!=nullptr,"Audio mapping");
            samples=static_cast<int16_t*>(MapViewOfFile(audio.value,FILE_MAP_READ|FILE_MAP_WRITE,0,0,AudioBytes));Require(samples!=nullptr,"Audio view");options.includeAudio=1;}
        if(window){
            options.window=reinterpret_cast<uintptr_t>(window);DWORD processId{};GetWindowThreadProcessId(window,&processId);options.processId=processId;
            Handle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,options.processId));FILETIME birth{},exit{},kernel{},user{};
            Require(process.value&&GetProcessTimes(process.value,&birth,&exit,&kernel,&user),"Source creation time");options.created=(uint64_t(birth.dwHighDateTime)<<32)|birth.dwLowDateTime;
            Require(GetClassNameW(window,options.className,256)>0,"Source class");
        }
        Success(WrailCaptureWorkerStart((Directory()+L"\\WebRtcCaptureWorker.exe").c_str(),&options,reinterpret_cast<uintptr_t>(video.value),videoBytes,
            reinterpret_cast<uintptr_t>(audio.value),includeAudio?AudioBytes:0,&session),"Worker start");
    }
    ~Owner(){if(session)WrailCaptureWorkerRelease(session);if(pixels)UnmapViewOfFile(pixels);if(samples)UnmapViewOfFile(samples);}
    Packet Read(){
        const auto deadline=GetTickCount64()+5000;Packet packet;
        while(GetTickCount64()<deadline){
            const auto heartbeat=WrailCaptureWorkerHeartbeat(session,27);if(heartbeat!=RO_E_CLOSED)Success(heartbeat,"Heartbeat");
            const auto hr=WrailCaptureWorkerRead(session,&packet,100);Success(hr,"Read packet");if(hr==S_OK)return packet;
        }
        throw std::runtime_error("Packet timeout");
    }
    void Ack(const Packet& packet){Require(WrailCaptureWorkerAcknowledge(session,packet.epoch,static_cast<uint32_t>(packet.kind),packet.sequence)==S_OK,"Matching credit acknowledgement");}
};
struct ChildIdentity {uint32_t pid{},padding{};uint64_t created{};};
uint64_t Created(HANDLE process){FILETIME birth{},exit{},kernel{},user{};Require(GetProcessTimes(process,&birth,&exit,&kernel,&user),"Read process birth");return(uint64_t(birth.dwHighDateTime)<<32)|birth.dwLowDateTime;}
int ParentDeathChild(const wchar_t* handle){
    auto pipe=reinterpret_cast<HANDLE>(wcstoull(handle,nullptr,16));
    // Intentionally do not release the session: process death must close the
    // sole job owner and kill its worker without any cooperative Stop call.
    auto owner=new Owner(30);auto pid=WrailCaptureWorkerProcessId(owner->session);
    Handle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid));
    ChildIdentity identity{pid,0,Created(process.value)};DWORD written{};
    Require(WriteFile(pipe,&identity,sizeof(identity),&written,nullptr)&&written==sizeof(identity),"Publish child identity");
    CloseHandle(pipe);Sleep(500);ExitProcess(0);
}
void ParentDeath(){
    SECURITY_ATTRIBUTES security{sizeof(security),nullptr,TRUE};Handle read,write;
    Require(CreatePipe(&read.value,&write.value,&security,4096),"Death-probe pipe");Require(SetHandleInformation(read.value,HANDLE_FLAG_INHERIT,0),"Private death reader");
    SIZE_T size{};InitializeProcThreadAttributeList(nullptr,1,0,&size);std::vector<uint8_t> memory(size);
    auto attributes=reinterpret_cast<LPPROC_THREAD_ATTRIBUTE_LIST>(memory.data());
    Require(InitializeProcThreadAttributeList(attributes,1,0,&size),"Death-probe attributes");
    Require(UpdateProcThreadAttribute(attributes,0,PROC_THREAD_ATTRIBUTE_HANDLE_LIST,&write.value,sizeof(HANDLE),nullptr,nullptr),"Explicit death-probe handle list");
    wchar_t handle[32]{};swprintf_s(handle,L"%llx",static_cast<unsigned long long>(reinterpret_cast<uintptr_t>(write.value)));
    const auto exe=Directory()+L"\\CaptureWorkerProbe.exe";auto text=L"\""+exe+L"\" --parent-death-parent "+handle;
    std::vector<wchar_t> command(text.begin(),text.end());command.push_back(0);
    STARTUPINFOEXW startup{};startup.StartupInfo.cb=sizeof(startup);startup.lpAttributeList=attributes;PROCESS_INFORMATION started{};
    const auto created=CreateProcessW(exe.c_str(),command.data(),nullptr,nullptr,TRUE,CREATE_NO_WINDOW|EXTENDED_STARTUPINFO_PRESENT,nullptr,nullptr,&startup.StartupInfo,&started);
    DeleteProcThreadAttributeList(attributes);Require(created,"Death-probe parent creation");
    Handle parent(started.hProcess),thread(started.hThread);CloseHandle(write.value);write.value=nullptr;
    const auto deadline=GetTickCount64()+5000;DWORD available{};
    while(GetTickCount64()<deadline){if(!PeekNamedPipe(read.value,nullptr,0,nullptr,&available,nullptr))break;if(available>=sizeof(ChildIdentity))break;Sleep(5);}
    Require(available>=sizeof(ChildIdentity),"Death parent did not identify worker");
    ChildIdentity identity;DWORD count{};Require(ReadFile(read.value,&identity,sizeof(identity),&count,nullptr)&&count==sizeof(identity),"Read worker identity");
    Handle worker(OpenProcess(SYNCHRONIZE|PROCESS_QUERY_LIMITED_INFORMATION,FALSE,identity.pid));
    Require(worker.value!=nullptr&&Created(worker.value)==identity.created,"Death test binds exact worker incarnation");
    Require(WaitForSingleObject(parent.value,3000)==WAIT_OBJECT_0,"Death-test parent exit");
    Require(WaitForSingleObject(worker.value,1000)==WAIT_OBJECT_0,"Worker survived its parent/job owner");
    std::puts("PASS parent death kills exact child via non-inherited kill-on-close job");
}
void ProductionFlags(){
    Handle mapping(CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,2560*720*4,nullptr));
    Require(mapping.value!=nullptr,"Production negative mapping");
    for(uint32_t flags=1;flags<=3;++flags){
        Configuration c;c.flags=flags;c.epoch=1;c.window=1;c.processId=1;c.created=1;c.className[0]=L'x';
        c.maximumWidth=2560;c.maximumHeight=720;c.framesPerSecond=30;void* result{};
        const auto hr=WrailCaptureWorkerStart((Directory()+L"\\WebRtcCaptureWorker.exe").c_str(),&c,
            reinterpret_cast<uintptr_t>(mapping.value),2560*720*4,0,0,&result);
        if(result)WrailCaptureWorkerRelease(result);
        Require(hr==E_INVALIDARG&&result==nullptr,"Production host accepted a validation flag");
    }
    std::puts("PASS production host rejects every private validation flag before spawning a worker");
}
void Frames(uint32_t fps,uint32_t maximumWidth=2560,uint32_t maximumHeight=720){
    Owner owner(fps,TestPattern,false,nullptr,maximumWidth,maximumHeight);auto packet=owner.Read();Require(packet.kind==Kind::Ready,"Ready packet");
    packet=owner.Read();Require(packet.kind==Kind::Video,"First video packet");
    const uint32_t width=maximumHeight>=1080?maximumWidth:320,height=maximumHeight>=1080?maximumHeight:200;
    Require(packet.width==width&&packet.height==height&&packet.bytes==width*height*4,"Bounded frame metadata");
    Require((owner.pixels[0]&0xffffff00u)==0xff008000u&&owner.pixels[0]==owner.pixels[size_t(width)*height-1],"Native mapping contains expected complete synthetic picture");
    const auto originalPixel=owner.pixels[0];
    Packet idle;
    Require(WrailCaptureWorkerAcknowledge(owner.session,28,1,packet.sequence)==RO_E_CLOSED,"Stale epoch credit rejected");
    Require(WrailCaptureWorkerAcknowledge(owner.session,27,1,packet.sequence+999)==S_FALSE,"Wrong sequence credit rejected");
    Require(WrailCaptureWorkerRead(owner.session,&idle,150)==S_FALSE,"Withheld credit bounds outstanding video");
    Require(owner.pixels[0]==originalPixel,"Worker modified pixels before release of their exact credit");
    owner.Ack(packet);Require(WrailCaptureWorkerAcknowledge(owner.session,27,1,packet.sequence)==S_FALSE,"Duplicate credit rejected");
    int64_t first{},last{};uint32_t frames{};
    while(frames<fps*2+1){packet=owner.Read();if(packet.kind!=Kind::Video)continue;Require(packet.epoch==27,"Exact epoch");
        Require((owner.pixels[0]&0xffffff00u)==0xff008000u&&owner.pixels[0]==owner.pixels[size_t(width)*height-1],"Complete mapped image without tearing");
        if(!frames)first=packet.microseconds;last=packet.microseconds;frames++;owner.Ack(packet);
    }
    const auto rate=(frames-1)*1000000.0/double(last-first);
    if(rate<fps*0.9||rate>fps*1.1)std::printf("Cadence evidence: target=%u measured=%.3f frames=%u elapsedUs=%lld dropped=%u\n",fps,rate,frames,
        static_cast<long long>(last-first),packet.flags);
    Require(rate>=fps*0.9&&rate<=fps*1.1,"Mapped worker target cadence");
    Handle process(OpenProcess(PROCESS_QUERY_INFORMATION|PROCESS_VM_READ,FALSE,WrailCaptureWorkerProcessId(owner.session)));
    PROCESS_MEMORY_COUNTERS_EX memory{};memory.cb=static_cast<DWORD>(sizeof(memory));
    Require(process.value&&K32GetProcessMemoryInfo(process.value,reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&memory),sizeof(memory)),"Read exact synthetic worker memory");
    StopResult stop;const auto started=GetTickCount64();Success(WrailCaptureWorkerStop(owner.session,600,&stop),"Cooperative stop");
    Require(stop.exited&&!stop.forced&&GetTickCount64()-started<1000,"Cooperative stop remains bounded");
    std::printf("PASS %ux%u %u FPS mapped picture/credits/stop; measured %.3f FPS; privateBytes=%llu peakWorkingSet=%llu\n",width,height,fps,rate,
        static_cast<unsigned long long>(memory.PrivateUsage),static_cast<unsigned long long>(memory.PeakWorkingSetSize));
}
void BlockedClose(){
    Owner owner(30,TestPattern|TestBlockedExit);owner.Read();owner.Read();
    StopResult stop;const auto started=GetTickCount64();Success(WrailCaptureWorkerStop(owner.session,150,&stop),"Forced stop");
    const auto elapsed=GetTickCount64()-started;Require(stop.exited&&stop.forced&&elapsed<1000,"Blocked close was not contained");
    std::printf("PASS simulated blocked teardown terminated in %llu ms\n",static_cast<unsigned long long>(elapsed));
}
void Wakeup(){
    Owner owner(60,TestPattern,true);
    Require(owner.Read().kind==Kind::Ready,"Event-wakeup ready packet");
    // Let video/audio writes coalesce into one auto-reset signal. The bounded
    // pipe, not the event count, remains authoritative for queued packets.
    Sleep(100);
    const auto first=owner.Read(),second=owner.Read();
    Require((first.kind==Kind::Video&&second.kind==Kind::Audio)||(first.kind==Kind::Audio&&second.kind==Kind::Video),
        "Coalesced data-ready signals must drain both queued media packets");
    Packet empty;const auto started=GetTickCount64();
    Require(WrailCaptureWorkerRead(owner.session,&empty,40)==S_FALSE,"Withheld credits must time out without polling or duplicate packets");
    Require(GetTickCount64()-started>=25&&GetTickCount64()-started<250,"Read timeout remains bounded");
    owner.Ack(first);owner.Ack(second);
    const auto third=owner.Read(),fourth=owner.Read();
    Require(third.sequence>second.sequence&&fourth.sequence>third.sequence,"Wakeup after credit return preserves packet order");
    // Withhold both new credits, then let cooperative exit and its terminal
    // packet become ready simultaneously. Read must drain the packet first.
    StopResult stopped;Success(WrailCaptureWorkerStop(owner.session,600,&stopped),"Wakeup stop");
    Require(stopped.exited&&!stopped.forced,"Wakeup stop confirms process exit");
    Packet terminal;Require(WrailCaptureWorkerRead(owner.session,&terminal,0)==S_OK&&terminal.kind==Kind::Ended,
        "Queued terminal packet must survive simultaneous worker-exit signal");
    Require(WrailCaptureWorkerRead(owner.session,&empty,0)==HRESULT_FROM_WIN32(ERROR_BROKEN_PIPE),
        "Exited worker is reported only after its pipe is drained");
    std::puts("PASS event wakeup drains coalesced video/audio signals, preserves bounded credit timeout and queued terminal packet on worker exit");
}
void AudioBounds(){
    Owner owner(30,TestPattern,true);Packet held{};int16_t original{};uint32_t videos{};
    const auto deadline=GetTickCount64()+1700;
    while(GetTickCount64()<deadline){
        const auto packet=owner.Read();
        Require(packet.kind!=Kind::Ended,"Audio backpressure must not terminate live capture");
        if(packet.kind==Kind::Video){videos++;owner.Ack(packet);}
        else if(packet.kind==Kind::Audio){
            Require(held.sequence==0,"Audio credit allowed more than one outstanding mapping");held=packet;
            Require(packet.bytes==3840&&owner.samples[0]>0&&owner.samples[0]==owner.samples[1919],"Complete generated PCM in mapping");original=owner.samples[0];
        }
        if(held.sequence)Require(owner.samples[0]==original&&owner.samples[1919]==original,"Queued eviction overwrote the outstanding mapped packet");
    }
    Require(held.sequence!=0&&videos>=35,"Insufficient video progress while audio credit was withheld");
    Require(WrailCaptureWorkerAcknowledge(owner.session,28,2,held.sequence)==RO_E_CLOSED,"Stale audio epoch rejected");
    Require(WrailCaptureWorkerAcknowledge(owner.session,27,2,held.sequence+999)==S_FALSE,"Wrong audio sequence rejected");
    owner.Ack(held);Require(WrailCaptureWorkerAcknowledge(owner.session,27,2,held.sequence)==S_FALSE,"Duplicate audio credit rejected");
    uint32_t recovered{};int64_t last=held.microseconds;auto lastSample=original;
    while(recovered<30){
        const auto packet=owner.Read();Require(packet.kind!=Kind::Ended,"Capture ended during audio recovery");
        if(packet.kind==Kind::Video){owner.Ack(packet);continue;}
        if(packet.kind!=Kind::Audio)continue;
        if(!recovered)Require(packet.microseconds-held.microseconds>1000000,"Recovery replayed a long stale audio backlog");
        Require(packet.microseconds>last&&owner.samples[0]>lastSample&&owner.samples[0]==owner.samples[1919],"Recovered PCM/QPC must remain complete and ordered");
        Require(WrailCaptureWorkerAcknowledge(owner.session,27,2,held.sequence)==S_FALSE,"Old released sequence cannot release a newer packet");
        last=packet.microseconds;lastSample=owner.samples[0];recovered++;owner.Ack(packet);
    }
    StopResult stop;Success(WrailCaptureWorkerStop(owner.session,600,&stop),"Post-backpressure stop");
    Require(stop.exited&&!stop.forced,"Post-backpressure worker did not exit cooperatively");
    std::puts("PASS audio credit/epoch isolation, immutable held mapping, continued video, fresh PCM recovery and clean stop after 1.7s stall");
}
struct SourceWindow {
    Handle process,thread;HWND window{};
    SourceWindow(){
        const auto exe=Directory()+L"\\WindowCaptureFixture.exe";auto text=L"\""+exe+L"\" --large";
        std::vector<wchar_t> command(text.begin(),text.end());command.push_back(0);STARTUPINFOW startup{};startup.cb=sizeof(startup);PROCESS_INFORMATION child{};
        Require(CreateProcessW(exe.c_str(),command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,nullptr,&startup,&child),"Synthetic source creation");
        process.value=child.hProcess;thread.value=child.hThread;
        struct Search {DWORD pid;HWND window{};} search{child.dwProcessId};
        const auto deadline=GetTickCount64()+5000;
        while(!window&&GetTickCount64()<deadline){
            EnumWindows([](HWND candidate,LPARAM state)->BOOL{auto& found=*reinterpret_cast<Search*>(state);DWORD pid{};GetWindowThreadProcessId(candidate,&pid);
                if(pid!=found.pid)return TRUE;wchar_t name[256]{};GetClassNameW(candidate,name,256);
                if(wcscmp(name,L"WidgetRailCaptureFixture")==0){found.window=candidate;return FALSE;}return TRUE;},reinterpret_cast<LPARAM>(&search));
            window=search.window;if(!window)Sleep(10);
        }
        Require(window!=nullptr,"Synthetic window discovery");
    }
    ~SourceWindow(){if(process.value&&WaitForSingleObject(process.value,0)!=WAIT_OBJECT_0){TerminateProcess(process.value,0);WaitForSingleObject(process.value,500);}}
    void Close(){Require(TerminateProcess(process.value,0),"Close exact synthetic source");Require(WaitForSingleObject(process.value,500)==WAIT_OBJECT_0,"Source process exit");}
};
void WindowCapture(){
    SourceWindow source;Owner owner(30,0,false,source.window);uint32_t frames{};int64_t first{},last{};bool picture=false;
    while(frames<91){
        const auto packet=owner.Read();Require(packet.kind!=Kind::Ended,"Real capture ended before measurement");if(packet.kind!=Kind::Video)continue;
        const auto left=owner.pixels[(packet.height/2)*packet.width+packet.width/4];
        const auto right=owner.pixels[(packet.height/2)*packet.width+(packet.width*3)/4];
        picture|=(left&0x00ff0000u)>0x00e00000u&&(right&0x0000ff00u)>0x0000e000u;
        if(frames==1)first=packet.microseconds;last=packet.microseconds;frames++;owner.Ack(packet);
    }
    Require(picture,"WGC mapped picture did not contain the synthetic red/green source");
    const auto rate=89*1000000.0/double(last-first);
    source.Close();bool ended=false;const auto deadline=GetTickCount64()+3000;
    while(GetTickCount64()<deadline){const auto packet=owner.Read();if(packet.kind==Kind::Video)owner.Ack(packet);
        else if(packet.kind==Kind::Ended){Require(FAILED(packet.result),"Source loss reported successful continuation");ended=true;break;}}
    Require(ended,"Source loss did not end the worker");
    StopResult stop;Success(WrailCaptureWorkerStop(owner.session,600,&stop),"Source-loss cleanup");Require(stop.exited&&!stop.forced,"Source loss required forced cleanup");
    std::printf("PASS real WGC mapped picture at %.3f FPS and exact source-loss stop\n",rate);
}
}
int wmain(int argc,wchar_t** argv){
    try{
        if(argc==3&&wcscmp(argv[1],L"--parent-death-parent")==0)return ParentDeathChild(argv[2]);
        if(argc==2&&wcscmp(argv[1],L"--production-flags")==0){ProductionFlags();return 0;}
        if(argc==2&&wcscmp(argv[1],L"--audio")==0){AudioBounds();return 0;}
        if(argc==2&&wcscmp(argv[1],L"--window")==0){WindowCapture();return 0;}
        if(argc==2&&wcscmp(argv[1],L"--high-quality")==0){Frames(60,3840,1080);Frames(60,5120,1440);return 0;}
        if(argc==2&&wcscmp(argv[1],L"--wakeup")==0){Wakeup();return 0;}
        Frames(15);Frames(30);Wakeup();AudioBounds();BlockedClose();ParentDeath();return 0;
    }catch(const std::exception& error){std::fprintf(stderr,"FAIL %s\n",error.what());return 1;}
}
