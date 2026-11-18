#pragma once
// Shared owned-source fixture and production worker control for native probes.
#include "../../src/WinUiWindowPreviewNative/CaptureEncodingProfile.h"
#include "../../src/WinUiWindowPreviewNative/CaptureEncodingEnvironment.h"
#include <windows.graphics.capture.interop.h>
#include <winrt/Windows.Graphics.Capture.h>
#include "../DiscordWebViewPrototype/ReplayWorkerProtocol.h"
#include <winrt/base.h>
#include <cstdio>
#include <string>
#include <functional>
#include <stdexcept>
#include <psapi.h>
using namespace replay_worker;
class FixtureProcess {
    PROCESS_INFORMATION process{};
    HWND window{};
    struct StopEvent {HANDLE value{};~StopEvent(){if(value)CloseHandle(value);}} stop;
public:
    explicit FixtureProcess(const wchar_t* executable,bool pulse=false,uint32_t width=1280,uint32_t height=720) {
        std::wstring arguments=L" --audio --width="+std::to_wstring(width)+L" --height="+std::to_wstring(height);
        if(pulse){
            const auto prefix=L"Local\\WidgetRail.ReplayAudio."+std::to_wstring(GetCurrentProcessId())+L"."+std::to_wstring(GetTickCount64());
            stop.value=CreateEventW(nullptr,TRUE,FALSE,(prefix+L".stop").c_str());winrt::check_bool(stop.value!=nullptr);
            arguments=L" --pulse-tree "+prefix+L".stop "+prefix+L".ready";
        }
        std::wstring command=L"\""+std::wstring(executable)+L"\""+arguments;
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
        if(!window){if(stop.value)SetEvent(stop.value);TerminateProcess(process.hProcess,2);CloseHandle(process.hProcess);process.hProcess=nullptr;throw winrt::hresult_error(HRESULT_FROM_WIN32(WAIT_TIMEOUT));}
        if(pulse)winrt::check_bool(SetWindowPos(window,nullptr,0,0,1280,720,SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE|SWP_ASYNCWINDOWPOS));
    }
    ~FixtureProcess(){if(stop.value)SetEvent(stop.value);if(window&&IsWindow(window))PostMessageW(window,WM_CLOSE,0,0);if(process.hProcess){if(WaitForSingleObject(process.hProcess,2000)!=WAIT_OBJECT_0){TerminateProcess(process.hProcess,2);WaitForSingleObject(process.hProcess,2000);}CloseHandle(process.hProcess);}}
    HWND Window() const{return window;}
    DWORD Id() const{return process.dwProcessId;}
    uint64_t Created() const {FILETIME created{},exit{},kernel{},user{};winrt::check_bool(GetProcessTimes(process.hProcess,&created,&exit,&kernel,&user));return (uint64_t(created.dwHighDateTime)<<32)|created.dwLowDateTime;}
};


void Check(bool value,const char* label){if(!value)throw std::runtime_error(label);printf("PASS %s\n",label);fflush(stdout);}
void Hr(int32_t value){if(value<0)throw winrt::hresult_error(value);}
struct Worker {
    void* value{};uint64_t epoch{41};
    Worker(const wchar_t* executable,FixtureProcess& fixture,uint32_t duration=15,const widgetrail::capture::CaptureEncodingProfile* profile=nullptr,uint32_t maximumWidth=1280,uint32_t maximumHeight=720,uint32_t bitrate=6000000,bool staleSource=false) {
        Configuration c;c.epoch=epoch;c.window=reinterpret_cast<uint64_t>(fixture.Window());c.processId=fixture.Id();c.created=fixture.Created();
        Check(GetClassNameW(fixture.Window(),c.className,256)>0,"exact fixture identity");
        c.maximumWidth=maximumWidth;c.maximumHeight=maximumHeight;c.framesPerSecond=30;c.bitrate=bitrate;c.audioMode=1;c.durationSeconds=duration;
        c.memoryBytes=(duration==60?128ull:32ull)*1024*1024;
        if(profile){
            c.codec=static_cast<uint32_t>(profile->codec);c.pixelFormat=static_cast<uint32_t>(profile->format);
            c.rateControl=static_cast<uint32_t>(profile->rateControl);c.quality=profile->quality;
            c.adapterLuid=widgetrail::capture::ReadEncodingEnvironment().defaultAdapterLuid;
            using namespace winrt::Windows::Graphics::Capture;
            const auto previousDpi=SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            struct DpiScope{DPI_AWARENESS_CONTEXT previous;~DpiScope(){if(previous)SetThreadDpiAwarenessContext(previous);}} dpiScope{previousDpi};
            auto interop=winrt::get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();GraphicsCaptureItem item{nullptr};
            winrt::check_hresult(interop->CreateForWindow(fixture.Window(),winrt::guid_of<GraphicsCaptureItem>(),winrt::put_abi(item)));
            const auto size=item.Size();c.expectedSourceWidth=static_cast<uint32_t>(size.Width)+(staleSource?2u:0u);c.expectedSourceHeight=static_cast<uint32_t>(size.Height);
        }
        Hr(WrailReplayWorkerStart(executable,&c,&value));
    }
    ~Worker(){if(value)WrailReplayWorkerRelease(value);}
    State Wait(const std::function<bool(const State&)>& predicate,uint64_t timeout=10000,bool expectedExit=false){
        auto deadline=GetTickCount64()+timeout;State state;
        do {auto heartbeat=WrailReplayWorkerHeartbeat(value,epoch);Hr(WrailReplayWorkerRead(value,&state,100));
            if(predicate(state))return state;
            if(!expectedExit&&(heartbeat<0||state.phase==Phase::Failed)){printf("issue=%u error=%08x\n",static_cast<unsigned>(state.issue),static_cast<unsigned>(state.error));throw std::runtime_error("native worker failed");}
        }while(GetTickCount64()<deadline);throw std::runtime_error("worker state timeout");
    }
    State Save(const std::wstring& path,uint32_t duration=3) {
        uint64_t id{};Hr(WrailReplayWorkerSave(value,epoch,path.c_str(),duration,&id));
        auto state=Wait([&](const State& next){return next.saveId==id&&next.saveOutcome>=SaveOutcome::Saved;});
        Check(state.saveOutcome==SaveOutcome::Saved&&state.savedDurationTicks>0&&state.savedDurationTicks<=int64_t(duration)*10000000&&state.savedAudio==1,
            "saved actual application audio and bounded video interval");
        printf("saved frames=%u missed=%u duration=%.6f bytes=%llu canvas=%ux%u\n",state.savedFrames,state.savedMissed,
            double(state.savedDurationTicks)/10000000,static_cast<unsigned long long>(state.savedBytes),state.savedWidth,state.savedHeight);
        return state;
    }
};
