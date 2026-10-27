#include "CaptureQuality.h"
#include "CapturePacer.h"
#include "../../src/WinUiWindowPreviewNative/CaptureGeometry.h"
#include <cstdio>
#include <cstring>

int main(int argc,char** argv) {
    const bool policyOnly=argc==2&&std::strcmp(argv[1],"--policy-only")==0;
    if(argc!=1&&!policyOnly){std::fputs("Usage: CaptureQualityProbe [--policy-only]\n",stderr);return 64;}
    static_assert(MaximumLiveCaptureVideoBytes==29491200);
    constexpr CaptureGeometry envelopes[]={{1706,480},{2560,720},{3840,1080},{5120,1440}};
    for(const auto envelope:envelopes)for(const uint32_t fps:{15u,30u,60u}){
        const LiveCaptureQuality quality{envelope.width,envelope.height,fps};
        if(!ValidLiveCaptureQuality(quality)||LiveCaptureFrameInterval(quality)!=1000000/quality.framesPerSecond)return 1;
        for(const auto source: {CaptureGeometry{3840,2160},CaptureGeometry{5120,1440},CaptureGeometry{720,1280},CaptureGeometry{320,200}}){
            const auto output=ConfiguredCaptureSize(static_cast<int32_t>(source.width),static_cast<int32_t>(source.height),quality.maximumWidth,quality.maximumHeight,true);
            if(!output.width||!output.height||output.width>quality.maximumWidth||output.height>quality.maximumHeight||
                output.width>source.width||output.height>source.height||output.width%2||output.height%2)return 2;
        }
    }
    if(ValidLiveCaptureQuality({7680,2160,30})||ValidLiveCaptureQuality({5120,1440,120})||
        ValidLiveCaptureQuality({0,0,30})||ValidLiveCaptureQuality({2560,480,30})||LiveCaptureFrameInterval({2560,720,0}))return 3;
    const auto ultrawide=ConfiguredCaptureSize(5120,1440,2560,720,true);
    if(ultrawide.width!=2560||ultrawide.height!=720)return 4;
    const auto fullWide=ConfiguredCaptureSize(5120,1440,5120,1440,true);
    const auto landscape=ConfiguredCaptureSize(3840,2160,5120,1440,true);
    const auto smallerSource=ConfiguredCaptureSize(640,360,5120,1440,true);
    if(fullWide.width!=5120||fullWide.height!=1440||landscape.width!=2560||landscape.height!=1440||smallerSource.width!=640||smallerSource.height!=360)return 4;
    std::puts("PASS twelve bounded presets, 1440p landscape/ultrawide geometry, no upscaling, and invalid option rejection");
    // Shared CI qualifies deterministic policy. The original no-argument local
    // probe still measures the production pacer, which depends on host load.
    if(policyOnly)return 0;
    CapturePacer pacer;
    for(const uint32_t fps: {15u,30u,60u}){
        const LiveCaptureQuality quality{2560,720,fps};
        auto start=CapturePacer::Clock::now(),next=start;
        const auto intervals=fps*2;
        for(uint32_t frame=0;frame<=intervals;frame++){
            pacer.Until(next);next=CapturePacer::Clock::now()+std::chrono::microseconds(LiveCaptureFrameInterval(quality));
        }
        const auto measured=intervals/std::chrono::duration<double>(CapturePacer::Clock::now()-start).count();
        std::printf("Target %u FPS: measured %.3f FPS with the production pacer\n",fps,measured);
        if(measured<fps*0.9||measured>fps*1.05)return 5;
    }
    return 0;
}
