#include "../DiscordWebViewPrototype/ReplayWorkerProtocol.h"
#include <cstdio>
#include <string>
#include <stdexcept>
#include <functional>
#include <array>

using namespace replay_worker;
void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);printf("PASS %s\n",message);fflush(stdout);}
void Hr(int32_t value){if(value<0){printf("HRESULT %08x\n",static_cast<uint32_t>(value));throw std::runtime_error("native call failed");}}
struct Session {
    void* handle{};uint64_t epoch{17};
    Session(const wchar_t* path,uint32_t width=1280,uint32_t height=720,uint32_t fps=30){Configuration config;
        config.flags=1;config.epoch=epoch;config.maximumWidth=width;config.maximumHeight=height;
        config.framesPerSecond=fps;config.bitrate=width>1920?24000000:fps==60?12000000:6000000;
        config.durationSeconds=3;config.memoryBytes=32ull*1024*1024;
        Hr(WrailReplayWorkerStart(path,&config,&handle));}
    ~Session(){if(handle)WrailReplayWorkerRelease(handle);}
    State Wait(const std::function<bool(const State&)>& predicate,uint64_t timeout=10000,bool renew=true){
        const auto start=GetTickCount64();State state;
        do{const auto heartbeat=renew?WrailReplayWorkerHeartbeat(handle,epoch):S_OK;Hr(WrailReplayWorkerRead(handle,&state,100));
            if(predicate(state))return state;
            Hr(heartbeat);
            if(state.phase==Phase::Failed){printf("phase failed issue=%u error=%08x\n",static_cast<unsigned>(state.issue),static_cast<uint32_t>(state.error));throw std::runtime_error("worker failed");}
        }while(GetTickCount64()-start<timeout);
        throw std::runtime_error("worker wait timeout");
    }
};
void Profiles(const wchar_t* worker,const std::wstring& output) {
    const std::array<std::array<uint32_t,3>,5> profiles{{{1280,720,15},{1280,720,30},{1920,1080,30},{1920,1080,60},{3840,2160,30}}};
    for(const auto& profile:profiles){
        Session session(worker,profile[0],profile[1],profile[2]);
        auto state=session.Wait([](const State& value){return value.bufferedTicks>=20000000;});
        const auto path=output+L"."+std::to_wstring(profile[0])+L"x"+std::to_wstring(profile[1])+L"-"+std::to_wstring(profile[2])+L".pending";
        uint64_t id{};Hr(WrailReplayWorkerSave(session.handle,session.epoch,path.c_str(),2,&id));
        state=session.Wait([&](const State& value){return value.saveId==id&&value.saveOutcome>=SaveOutcome::Saved;});
        Check(state.saveOutcome==SaveOutcome::Saved&&state.savedWidth==profile[0]&&state.savedHeight==profile[1]&&
            state.savedDurationTicks>0&&state.savedDurationTicks<=20000000&&state.savedFrames<=profile[2]*2,"verified replay profile produces a bounded clip");
        printf("PROFILE %ux%u/%u frames=%u missed=%u duration=%.6f bytes=%llu\n",profile[0],profile[1],profile[2],state.savedFrames,state.savedMissed,
            double(state.savedDurationTicks)/10000000,static_cast<unsigned long long>(state.savedBytes));
        Hr(WrailReplayWorkerRequestStop(session.handle,session.epoch));session.Wait([](const State& value){return (value.flags&Ended)!=0;});
    }
}
int wmain(int argc,wchar_t** argv){
    if(argc!=3&&!(argc==4&&std::wstring(argv[3])==L"--profiles"))return 2;
    try {
        if(argc==4){Profiles(argv[1],argv[2]);return 0;}
        Check(sizeof(Configuration)==624&&sizeof(State)==168,"ABI structure sizes");
        Session session(argv[1]);auto state=session.Wait([](const State& s){return s.bufferedTicks>=20000000;});
        Check(state.phase==Phase::Recording&&state.bufferedTicks<=30000000&&state.residentBytes<=32ull*1024*1024,"bounded encoded window");
        Check(WrailReplayWorkerHeartbeat(session.handle,18)<0,"wrong epoch rejected");
        Hr(WrailReplayWorkerTestPause(session.handle,1));state=session.Wait([](const State& s){return s.phase==Phase::Paused;});
        const auto paused=state.bufferedTicks;Sleep(300);Hr(WrailReplayWorkerRead(session.handle,&state,0));
        Check(state.bufferedTicks==paused,"pause retains last valid buffer");
        std::wstring output=argv[2];uint64_t id{};
        Hr(WrailReplayWorkerSave(session.handle,17,(output+L".paused.pending").c_str(),3,&id));
        state=session.Wait([&](const State& s){return s.saveId==id&&s.saveOutcome>=SaveOutcome::Saved;});
        Check(state.saveOutcome==SaveOutcome::Saved&&state.savedDurationTicks<=30000000&&state.savedFrames<=90&&state.savedBytes>0,"paused save uses MP4 staging extension and bounded metadata");
        Hr(WrailReplayWorkerTestPause(session.handle,0));session.Wait([](const State& s){return s.phase==Phase::Recording;});
        Hr(WrailReplayWorkerTestPacketDelay(session.handle,20));
        const auto cancelPath=output+L".cancel.pending";
        Hr(WrailReplayWorkerSave(session.handle,17,cancelPath.c_str(),3,&id));
        session.Wait([&](const State& s){return s.saveId==id&&(s.flags&Saving)&&GetFileAttributesW(cancelPath.c_str())!=INVALID_FILE_ATTRIBUTES;});
        uint64_t duplicate{};Check(WrailReplayWorkerSave(session.handle,17,(output+L".duplicate.pending").c_str(),3,&duplicate)<0,"one save at a time");
        Hr(WrailReplayWorkerCancelSave(session.handle,17,id));
        state=session.Wait([&](const State& s){return s.saveId==id&&s.saveOutcome==SaveOutcome::Cancelled;});
        Check(GetFileAttributesW(cancelPath.c_str())==INVALID_FILE_ATTRIBUTES,"cancel removes partial staging file");
        Hr(WrailReplayWorkerTestPacketDelay(session.handle,0));
        Hr(WrailReplayWorkerTestStorageFull(session.handle,1));
        const auto failedPath=output+L".storage-full.pending";
        Hr(WrailReplayWorkerSave(session.handle,17,failedPath.c_str(),3,&id));
        state=session.Wait([&](const State& s){return s.saveId==id&&s.saveOutcome==SaveOutcome::Failed;});
        Check(state.phase==Phase::Recording&&state.issue==Issue::Storage&&GetFileAttributesW(failedPath.c_str())==INVALID_FILE_ATTRIBUTES,
            "injected disk-full write failure cleans partial file without stopping recorder");
        Hr(WrailReplayWorkerTestStorageFull(session.handle,0));
        Hr(WrailReplayWorkerTestSaveDelay(session.handle,12000));
        Hr(WrailReplayWorkerSave(session.handle,17,(output+L".stop.pending").c_str(),3,&id));
        Hr(WrailReplayWorkerRequestStop(session.handle,17));
        state=session.Wait([](const State& s){return (s.flags&Ended)!=0;},16000);
        Check(state.saveId==id&&state.saveOutcome==SaveOutcome::Saved,"immediate stop drains accepted slow save while progress watchdog remains active");
        Session expired(argv[1]);expired.Wait([](const State& s){return s.phase==Phase::Recording;});
        const auto start=GetTickCount64();
        do{Hr(WrailReplayWorkerRead(expired.handle,&state,250));}while(!(state.flags&Ended)&&GetTickCount64()-start<12000);
        Check((state.flags&Ended)&&state.issue==Issue::HostLost,"authority heartbeat expiration ends worker");
        Session disposed(argv[1]);disposed.Wait([](const State& s){return s.phase==Phase::Recording;});
        HANDLE process=OpenProcess(SYNCHRONIZE,FALSE,WrailReplayWorkerProcessId(disposed.handle));Check(process!=nullptr,"worker process identity available");
        const auto begin=GetTickCount64();WrailReplayWorkerRelease(disposed.handle);disposed.handle=nullptr;
        Check(WaitForSingleObject(process,500)==WAIT_OBJECT_0&&GetTickCount64()-begin<1000,"emergency disposal terminates only owned worker promptly");CloseHandle(process);
        puts("Replay worker checks passed");return 0;
    }catch(const std::exception& error){printf("FAIL %s\n",error.what());return 1;}
}
