#pragma once
#include <Windows.h>
#include <cstdint>
#include <limits>
#include <intrin.h>
#include <winrt/base.h>

namespace capture_worker {
// WASAPI provides QPC position converted to 100 ns units; its producer divides
// by ten. Use the same performance-counter clock, never GetTickCount64, for age.
inline bool TryQpcMicroseconds(uint64_t counter,uint64_t frequency,int64_t& result) noexcept {
    if(!frequency)return false;
    constexpr uint64_t scale=1000000;
    constexpr auto maximum=static_cast<uint64_t>((std::numeric_limits<int64_t>::max)());
    const auto seconds=counter/frequency;
    if(seconds>maximum/scale)return false;
    uint64_t high{},remainder{};
    const auto low=_umul128(counter%frequency,scale,&high);
    const auto fraction=_udiv128(high,low,frequency,&remainder);
    const auto whole=seconds*scale;
    if(fraction>maximum-whole)return false;
    result=static_cast<int64_t>(whole+fraction);return true;
}
inline int64_t CaptureQpcMicroseconds() {
    static const uint64_t frequency=[] {
        LARGE_INTEGER value{};winrt::check_bool(QueryPerformanceFrequency(&value));
        if(value.QuadPart<=0)throw winrt::hresult_error(E_UNEXPECTED);
        return static_cast<uint64_t>(value.QuadPart);
    }();
    LARGE_INTEGER counter{};winrt::check_bool(QueryPerformanceCounter(&counter));int64_t result{};
    if(counter.QuadPart<0||!TryQpcMicroseconds(static_cast<uint64_t>(counter.QuadPart),frequency,result))
        throw winrt::hresult_error(E_BOUNDS);
    return result;
}
}
