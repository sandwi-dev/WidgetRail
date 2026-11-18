#pragma once
#include <algorithm>
#include <cstdint>
#include <optional>
#include <stdexcept>

namespace widgetrail::capture {
// Constant-rate media slots follow the source clock, with one frame of phase
// tolerance. Independent rounding aliases regular callbacks near half a slot
// into duplicate/gap pairs. Prefer the next nominal slot inside the tolerance;
// a genuinely faster/slower source still drops/skips rather than drifting.
inline std::optional<uint64_t> CaptureFrameIndex(uint64_t elapsed100ns,uint32_t framesPerSecond,
    uint64_t segmentBase,uint64_t lastIndex,bool hasFrame) {
    if(framesPerSecond==0||framesPerSecond>120||segmentBase>UINT32_MAX||lastIndex>UINT32_MAX)
        throw std::invalid_argument("Invalid capture frame clock");
    constexpr uint64_t ticksPerSecond=10000000;
    const auto clockIndex=segmentBase+(elapsed100ns/ticksPerSecond)*framesPerSecond+
        (elapsed100ns%ticksPerSecond)*framesPerSecond/ticksPerSecond;
    if(!hasFrame)return clockIndex;
    const auto next=lastIndex+1;
    if(next>clockIndex+1)return {};
    return std::max(next,clockIndex);
}
}
