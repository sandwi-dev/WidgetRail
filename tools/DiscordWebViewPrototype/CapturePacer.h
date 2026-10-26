#pragma once
#include <Windows.h>
#include <chrono>
#include <algorithm>
#include <cstdint>
#include <winrt/base.h>

// Per-capture scheduling; does not change the system/process timer resolution.
// A frame-arrival event can wake an idle capture before its next repeat deadline.
class CapturePacer {
    HANDLE timer;
public:
    using Clock=std::chrono::steady_clock;
    CapturePacer() : timer(CreateWaitableTimerExW(nullptr,nullptr,CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,TIMER_ALL_ACCESS)) {
        if(!timer)winrt::throw_last_error();
    }
    ~CapturePacer(){CloseHandle(timer);}
    CapturePacer(const CapturePacer&)=delete;
    CapturePacer& operator=(const CapturePacer&)=delete;
    void Until(Clock::time_point deadline,HANDLE frameAvailable=nullptr) {
        const auto remaining=std::chrono::duration_cast<std::chrono::nanoseconds>(deadline-Clock::now()).count();
        if(remaining<=0)return;
        LARGE_INTEGER due{};due.QuadPart=-std::max<int64_t>(1,remaining/100);
        winrt::check_bool(SetWaitableTimer(timer,&due,0,nullptr,nullptr,FALSE));
        HANDLE handles[]={timer,frameAvailable};
        const auto result=WaitForMultipleObjects(frameAvailable?2u:1u,handles,FALSE,1000);
        if(result!=WAIT_OBJECT_0&&!(frameAvailable&&result==WAIT_OBJECT_0+1))throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_TIMEOUT));
    }
};
