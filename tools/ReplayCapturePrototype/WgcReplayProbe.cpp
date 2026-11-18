// Native replay acquisition fixture: only its own generated window/process audio.
#include "../../src/WinUiWindowPreviewNative/ReplayCaptureRecorder.h"
#include "../../src/WinUiWindowPreviewNative/CaptureAudioTimeline.h"
#include "../DiscordWebViewPrototype/LiveCaptureTexture.h"
#include <future>
#include <chrono>
#include <cstdio>
#include <string>
#include <thread>
using namespace widgetrail::capture;

class FixtureProcess {
    PROCESS_INFORMATION process{};
    HWND window{};
public:
    explicit FixtureProcess(const wchar_t* executable) {
        std::wstring command=L"\""+std::wstring(executable)+L"\" --audio --width=1280 --height=720";
        STARTUPINFOW startup{};startup.cb=sizeof(startup);
        winrt::check_bool(CreateProcessW(executable,command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,nullptr,&startup,&process));
        CloseHandle(process.hThread);process.hThread=nullptr;
        const auto deadline=GetTickCount64()+5000;
        do {
            EnumWindows([](HWND candidate,LPARAM value)->BOOL {
                auto self=reinterpret_cast<FixtureProcess*>(value);DWORD pid{};GetWindowThreadProcessId(candidate,&pid);
                if(pid==self->process.dwProcessId && IsWindowVisible(candidate)){self->window=candidate;return FALSE;}return TRUE;
            },reinterpret_cast<LPARAM>(this));
            if(window)break;Sleep(10);
        }while(GetTickCount64()<deadline && WaitForSingleObject(process.hProcess,0)!=WAIT_OBJECT_0);
        if(!window){TerminateProcess(process.hProcess,2);CloseHandle(process.hProcess);process.hProcess=nullptr;throw winrt::hresult_error(HRESULT_FROM_WIN32(WAIT_TIMEOUT));}
    }
    ~FixtureProcess(){if(window&&IsWindow(window))PostMessageW(window,WM_CLOSE,0,0);if(process.hProcess){if(WaitForSingleObject(process.hProcess,2000)!=WAIT_OBJECT_0){TerminateProcess(process.hProcess,2);WaitForSingleObject(process.hProcess,2000);}CloseHandle(process.hProcess);}}
    HWND Window() const{return window;}
    DWORD Id() const{return process.dwProcessId;}
    uint64_t Created() const {FILETIME created{},exit{},kernel{},user{};winrt::check_bool(GetProcessTimes(process.hProcess,&created,&exit,&kernel,&user));return (uint64_t(created.dwHighDateTime)<<32)|created.dwLowDateTime;}
};

struct Probe {
    using Clock=std::chrono::steady_clock;
    Clock::time_point deadline=Clock::now()+std::chrono::seconds(12),started{};
    HWND target{};
    std::wstring path;
    std::unique_ptr<ReplayCaptureRecorder> recorder;
    UINT sourceWidth{},sourceHeight{};
    size_t resizes{};
    HRESULT failure{S_OK};
    std::future<void> save;
    bool resized{},testPause{},minimized{},restored{},savedPaused{},savedLive{};
    uint64_t pausedFrames{};
    bool checkedPause{};
    explicit Probe(bool pause):testPause(pause) {
        WrailCaptureOptions options;options.kind=2;options.encoder=2;options.framesPerSecond=30;options.bitrate=6000000;options.audioMode=1;
        recorder=std::make_unique<ReplayCaptureRecorder>(options,3,8*1024*1024);
    }
    void Frame(UINT w,UINT h,UINT sw,UINT sh,int64_t timestamp,ID3D11Texture2D* texture) {
        if(sourceWidth && (sw!=sourceWidth||sh!=sourceHeight))++resizes;
        sourceWidth=sw;sourceHeight=sh;
        if(recorder->Frame(w,h,sw,sh,timestamp,texture) && started==Clock::time_point{})started=Clock::now();
    }
    void Audio(UINT kind,int64_t timestamp,const void* bytes,UINT count){recorder->Audio(kind,timestamp,bytes,count);}
    void Save(const wchar_t* suffix) {
        auto snapshot=recorder->PrepareSave(2);if(!snapshot)throw winrt::hresult_error(E_UNEXPECTED);
        const auto output=path+suffix;
        save=std::async(std::launch::async,[snapshot=std::move(snapshot),output]{winrt::init_apartment(winrt::apartment_type::multi_threaded);
            WriteReplayMp4(output.c_str(),snapshot->video.Get(),snapshot->audio.Get(),*snapshot->snapshot,
                [](size_t){std::this_thread::sleep_for(std::chrono::milliseconds(2));});});
    }
    bool Current() {
        if(FAILED(failure)||Clock::now()>=deadline)return false;
        if(started==Clock::time_point{})return true;
        if(save.valid() && save.wait_for(std::chrono::seconds(0))==std::future_status::ready)save.get();
        const auto elapsed=Clock::now()-started;
        auto state=recorder->Status();
        if(testPause && !minimized && elapsed>=std::chrono::seconds(2)){ShowWindowAsync(target,SW_MINIMIZE);minimized=true;}
        if(testPause && state.phase==ReplayRecorderPhase::Paused && !checkedPause){pausedFrames=state.encodedFrames;checkedPause=true;}
        if(testPause && !savedPaused && elapsed>=std::chrono::seconds(3)) {
            if(!checkedPause||state.phase!=ReplayRecorderPhase::Paused||state.encodedFrames!=pausedFrames)throw winrt::hresult_error(E_UNEXPECTED);
            Save(L".paused.mp4");savedPaused=true;
        }
        if(testPause && !restored && elapsed>=std::chrono::seconds(4)){ShowWindowAsync(target,SW_RESTORE);restored=true;}
        if(!savedLive && !save.valid() && elapsed>=std::chrono::seconds(testPause?5:4)) {Save(L".live.mp4");savedLive=true;}
        if(!resized&&elapsed>=std::chrono::milliseconds(5500)) {
            winrt::check_bool(SetWindowPos(target,nullptr,0,0,800,600,SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE|SWP_ASYNCWINDOWPOS));resized=true;
        }
        return elapsed<std::chrono::seconds(8);
    }
};
static thread_local Probe* active;
static int32_t __stdcall Current(){try{return active->Current()?1:0;}catch(...){active->failure=winrt::to_hresult();return 0;}}
static void __stdcall Frame(UINT w,UINT h,UINT sw,UINT sh,int64_t timestamp,ID3D11Texture2D* texture){try{active->Frame(w,h,sw,sh,timestamp,texture);}catch(...){active->failure=winrt::to_hresult();}}
static void __stdcall Audio(UINT kind,UINT,UINT,int64_t timestamp,const void* bytes,UINT count){try{active->Audio(kind,timestamp,bytes,count);}catch(...){active->failure=winrt::to_hresult();}}

int wmain(int argc,wchar_t** argv) {
    if(argc!=3 && !(argc==4 && std::wstring(argv[3])==L"--pause"))return 2;
    winrt::init_apartment(winrt::apartment_type::multi_threaded);winrt::check_hresult(MFStartup(MF_VERSION));
    try {
        FixtureProcess source(argv[1]);Probe probe(argc==4);probe.target=source.Window();probe.path=argv[2];active=&probe;
        wchar_t className[256]{};winrt::check_bool(GetClassNameW(source.Window(),className,256));
        auto result=WrailLiveCaptureRunTextureV1(reinterpret_cast<uint64_t>(source.Window()),source.Id(),source.Created(),className,1,2560,720,30,0,Current,Audio,Frame);
        winrt::check_hresult(result);winrt::check_hresult(probe.failure);
        probe.recorder->Finish();if(probe.save.valid())probe.save.get();
        auto clip=probe.recorder->PrepareSave(3);if(!clip)throw winrt::hresult_error(E_UNEXPECTED);
        WriteReplayMp4(argv[2],clip->video.Get(),clip->audio.Get(),*clip->snapshot);
        const auto status=probe.recorder->Status();
        std::printf("wgcFrames=%llu frameGaps=%llu encoderDrops=%llu resized=%zu canvas=%ux%u source=%ux%u bufferedSeconds=%.3f resident=%zu audioPackets=%llu pausedAudioDropped=%llu drops=%llu pauseVerified=%d\n",
            static_cast<unsigned long long>(status.encodedFrames),static_cast<unsigned long long>(status.frameGaps),static_cast<unsigned long long>(status.encoderDrops),probe.resizes,
            status.width,status.height,probe.sourceWidth,probe.sourceHeight,static_cast<double>(status.buffer.duration)/10000000,status.buffer.residentBytes,
            static_cast<unsigned long long>(status.audioPackets),static_cast<unsigned long long>(status.pausedAudioPackets),static_cast<unsigned long long>(status.buffer.dropped),probe.checkedPause);
        active=nullptr;return status.encodedFrames>(probe.testPause?130u:150u)&&probe.resizes>0&&status.audioPackets>200&&status.buffer.residentBytes<=8*1024*1024&&
            (!probe.testPause||(probe.checkedPause&&probe.savedPaused&&probe.restored&&status.pausedAudioPackets>20))?0:1;
    }catch(const winrt::hresult_error& error){std::printf("FAILED WGC replay hr=%08X stage=%ls\n",static_cast<unsigned>(error.code().value),VideoEncoder::LastStage());return 1;}
}
