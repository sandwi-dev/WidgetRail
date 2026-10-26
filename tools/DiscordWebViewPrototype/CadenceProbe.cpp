#include <Windows.h>
#include <chrono>
#include <cstdio>

int main() {
    using Clock=std::chrono::steady_clock;
    HANDLE timer=CreateWaitableTimerExW(nullptr,nullptr,CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,TIMER_ALL_ACCESS);
    if(!timer)return 2;
    for(int mode=0;mode<2;++mode){
        auto start=Clock::now(),next=start;
        for(int frame=0;frame<90;++frame){
            while(Clock::now()<next){
                if(mode==0)Sleep(2);
                else{
                    LARGE_INTEGER due{};
                    due.QuadPart=-std::chrono::duration_cast<std::chrono::nanoseconds>(next-Clock::now()).count()/100;
                    if(due.QuadPart>=0)break;
                    if(!SetWaitableTimer(timer,&due,0,nullptr,nullptr,FALSE)){CloseHandle(timer);return 3;}
                    if(WaitForSingleObject(timer,1000)!=WAIT_OBJECT_0){CloseHandle(timer);return 4;}
                }
            }
            next=Clock::now()+std::chrono::milliseconds(33);
        }
        const auto elapsed=std::chrono::duration<double>(Clock::now()-start).count();
        std::printf("%s: %.3f fps (%g seconds)\n",mode==0?"Sleep(2)":"High-resolution waitable timer",89/elapsed,elapsed);
        std::fflush(stdout);
    }
    CloseHandle(timer);return 0;
}
