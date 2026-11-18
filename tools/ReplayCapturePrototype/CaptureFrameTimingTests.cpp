#include "../../src/WinUiWindowPreviewNative/CaptureFrameTiming.h"
#include <cstdio>
#include <cmath>
using widgetrail::capture::CaptureFrameIndex;
void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);}
int main(){
    try {
        for(const uint32_t fps:{15u,30u,60u}){
            uint64_t last{};
            for(uint64_t frame=1;frame<=10000;++frame){
                const auto elapsed=static_cast<uint64_t>((double(frame)-.5+(frame%2?.001:-.001))*10000000/fps);
                const auto selected=CaptureFrameIndex(elapsed,fps,0,last,true);
                Check(selected&&*selected==frame,"Regular half-slot jitter must not create duplicate/gap pairs");last=*selected;
            }
            for(const auto sourceRate:{double(fps)-1,double(fps)+1,double(fps)/2,double(fps)*2}){
                last=0;uint64_t accepted=1;uint64_t skipped{};
                for(uint64_t callback=1;callback<=100000;++callback){
                    const auto elapsed=static_cast<uint64_t>(double(callback)*10000000/sourceRate);
                    const auto selected=CaptureFrameIndex(elapsed,fps,0,last,true);
                    if(!selected)continue;
                    const auto sourceSlot=double(elapsed)*fps/10000000;
                    Check(*selected>last&&std::abs(double(*selected)-sourceSlot)<=1.000001,"Sustained rate mismatch must remain within one frame of source time");
                    skipped+=*selected-last-1;last=*selected;++accepted;
                }
                Check(sourceRate>fps?accepted<100000:skipped>0,"Real rate mismatch must correct rather than accumulate clock drift");
            }
            const auto delayed=CaptureFrameIndex(100000000,fps,0,1,true);
            Check(delayed&&*delayed==uint64_t(fps)*10,"A long delivery stall must skip to current source time without catch-up frames");
            const auto resumed=CaptureFrameIndex(0,fps,1001,1000,true);
            Check(resumed&&*resumed==1001,"A paused clock resumes at the next active-time frame");
            const auto afterResume=CaptureFrameIndex(10000000/fps,fps,1001,*resumed,true);
            Check(afterResume&&*afterResume==1002,"Resumed cadence must not inherit wall-clock pause duration");
        }
        const auto wide=CaptureFrameIndex(UINT64_MAX,60,UINT32_MAX-1,UINT32_MAX-2,true);
        Check(wide&&*wide>UINT32_MAX,"Long-session overflow must remain visible to the recorder's frame-index guard");
        puts("PASS capture frame phase tolerance, rate mismatch, long stalls, resume and bounded clock error");return 0;
    }catch(const std::exception& e){printf("FAIL %s\n",e.what());return 1;}
}
