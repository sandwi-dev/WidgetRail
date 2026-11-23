#include "ReplayWorkerTestSupport.h"
#include "ReplayCoexistence.h"

void Measure(Worker& worker,const std::wstring& output,bool diagnose=false) {
    const auto process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|PROCESS_VM_READ,FALSE,WrailReplayWorkerProcessId(worker.value));
    Check(process!=nullptr,"measurement owns exact worker process");
    struct Close {HANDLE value;~Close(){CloseHandle(value);}} close{process};
    const auto start=GetTickCount64();uint64_t next{};
    worker.Wait([&](const State& state){
        const auto elapsed=GetTickCount64()-start;
        if(state.bufferedTicks>(diagnose?150000000:600000000)||state.residentBytes>(diagnose?32ull:128ull)*1024*1024)throw std::runtime_error("measured replay exceeded bounds");
        if(elapsed>=next){
            PROCESS_MEMORY_COUNTERS_EX memory{};memory.cb=sizeof(memory);
            Check(GetProcessMemoryInfo(process,reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&memory),sizeof(memory))!=0,"read worker memory");
            FILETIME created{},exited{},kernel{},user{};Check(GetProcessTimes(process,&created,&exited,&kernel,&user)!=0,"read worker CPU");
            const auto cpu=(uint64_t(kernel.dwHighDateTime)<<32|kernel.dwLowDateTime)+(uint64_t(user.dwHighDateTime)<<32|user.dwLowDateTime);
            printf("MEASURE {\"elapsedSeconds\":%.3f,\"privateBytes\":%llu,\"workingSet\":%llu,\"cpuMilliseconds\":%.3f,\"residentBytes\":%llu,\"bufferedSeconds\":%.7f,\"droppedFrames\":%llu}\n",
                double(elapsed)/1000,static_cast<unsigned long long>(memory.PrivateUsage),static_cast<unsigned long long>(memory.WorkingSetSize),
                double(cpu)/10000,static_cast<unsigned long long>(state.residentBytes),double(state.bufferedTicks)/10000000,
                static_cast<unsigned long long>(state.droppedFrames));
#ifdef CAPTURE_WORKER_VALIDATION
            Diagnostics d;Hr(WrailReplayWorkerReadDiagnostics(worker.value,&d));
            printf("DIAGNOSTICS {\"callbacks\":%llu,\"encoded\":%llu,\"duplicateSlots\":%llu,\"encoderDrops\":%llu,\"compressedDrops\":%llu,\"frameGaps\":%llu,\"observedFps\":%.6f,\"shortIntervals\":%llu,\"regularIntervals\":%llu,\"longIntervals\":%llu}\n",
                d.callbacks,d.encoded,d.duplicateSlots,d.encoderDrops,d.compressedDrops,d.frameGaps,
                d.lastTime>d.firstTime?double(d.callbacks-1)*1000000/double(d.lastTime-d.firstTime):0,d.shortIntervals,d.regularIntervals,d.longIntervals);
#endif
            fflush(stdout);next+=diagnose?1000:15000;
        }
        return elapsed>=(diagnose?20000u:76000u);
    },diagnose?24000u:80000u);
    worker.Save(output+L".measured.pending",diagnose?15u:60u);
    Hr(WrailReplayWorkerRequestStop(worker.value,worker.epoch));worker.Wait([](const State& s){return (s.flags&Ended)!=0;},10000,true);
    puts("PASS WGC+application-audio fixture measurement; real game rendering impact is not measured");
}
int wmain(int argc,wchar_t** argv){
    if(argc!=4&&!(argc==5&&(std::wstring(argv[4])==L"--measure"||std::wstring(argv[4])==L"--pulse"||std::wstring(argv[4])==L"--diagnose"||std::wstring(argv[4])==L"--coexist"||std::wstring(argv[4])==L"--audio-loss"||std::wstring(argv[4])==L"--audio-recovery")))return 2;
    try{
        const bool measure=argc==5&&std::wstring(argv[4])==L"--measure";
        FixtureProcess source(argv[2],argc==5&&std::wstring(argv[4])==L"--pulse");
        const bool testAudioLoss=argc==5&&std::wstring(argv[4])==L"--audio-loss";
        const bool testAudioReturn=argc==5&&std::wstring(argv[4])==L"--audio-recovery";
        if(testAudioLoss||testAudioReturn)winrt::check_bool(SetEnvironmentVariableW(L"WRAIL_TEST_REPLAY_AUDIO_LOSS",testAudioReturn?L"recover":L"1"));
        Worker worker(argv[1],source,measure?60u:15u);std::wstring output=argv[3];
        if(testAudioLoss||testAudioReturn){
            auto lost=worker.Wait([](const State& s){return s.phase==Phase::Recording&&(s.flags&AudioLost);});
            worker.Wait([&](const State& s){return s.bufferedTicks>=lost.bufferedTicks+20000000;});
            if(testAudioReturn){
                auto restored=worker.Wait([](const State& s){return s.phase==Phase::Recording && !(s.flags&AudioLost);},20000);
                worker.Wait([&](const State& s){return s.bufferedTicks>=restored.bufferedTicks+15000000;});
            }
            auto saved=worker.Save(output+(testAudioReturn?L".audio-recovery.mp4":L".audio-loss.mp4"),testAudioReturn?15u:3u);
            Check(((saved.flags&AudioLost)==0)==testAudioReturn && saved.savedFrames>0 && saved.savedAudio==1,
                "Replay saves the same session with the expected audio availability");
            Hr(WrailReplayWorkerRequestStop(worker.value,worker.epoch));
            worker.Wait([](const State& s){return (s.flags&Ended)!=0;},10000,true);
            return 0;
        }
        if(argc==5&&std::wstring(argv[4])==L"--coexist"){Coexist(worker,source,argv[1],output);return 0;}
        if(measure||argc==5&&std::wstring(argv[4])==L"--diagnose"){Measure(worker,output,!measure);return 0;}
        auto state=worker.Wait([](const State& s){return s.phase==Phase::Recording&&s.bufferedTicks>=30000000;});
        Check(state.width==1280&&state.height==720,"actual WGC worker encoded fixed canvas");
        ShowWindowAsync(source.Window(),SW_MINIMIZE);
        state=worker.Wait([](const State& s){return s.phase==Phase::Paused;});
        const auto paused=state.bufferedTicks;
        const auto until=GetTickCount64()+1000;
        worker.Wait([&](const State& s){if(s.phase!=Phase::Paused||s.bufferedTicks!=paused)throw std::runtime_error("minimized replay changed");return GetTickCount64()>=until;},2000);
        Check(true,"minimized target retains replay interval");
        worker.Save(output+L".paused.pending");
        ShowWindowAsync(source.Window(),SW_RESTORE);
        worker.Wait([&](const State& s){return s.phase==Phase::Recording&&s.bufferedTicks>paused+10000000;});
        Check(SetWindowPos(source.Window(),nullptr,0,0,800,600,SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE|SWP_ASYNCWINDOWPOS)!=0,"owned source resized");
        const auto resumeUntil=GetTickCount64()+1000;worker.Wait([&](const State&){return GetTickCount64()>=resumeUntil;});
        state=worker.Save(output+L".resumed.pending");Check(state.savedWidth==1280&&state.savedHeight==720,"resize preserves encoded canvas");
        PostMessageW(source.Window(),WM_CLOSE,0,0);
        state=worker.Wait([](const State& s){return (s.flags&Ended)!=0;},10000,true);
        Check((state.flags&Ended)!=0&&state.issue==Issue::Target,"target exit ends exact source session");
        puts("WGC replay worker checks passed");return 0;
    }catch(const winrt::hresult_error& e){printf("FAIL HRESULT %08x\n",static_cast<unsigned>(e.code()));return 1;}
    catch(const std::exception& e){printf("FAIL %s\n",e.what());return 1;}
}
