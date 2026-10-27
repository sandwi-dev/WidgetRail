#include "CaptureAudioQueue.h"
#include "CaptureQpcClock.h"
#include <cstdio>
#include <algorithm>
#include <stdexcept>
#include <limits>
using namespace capture_worker;
namespace {
void Require(bool value,const char* message){if(!value)throw std::runtime_error(message);}
void Policy(){
    LiveAudioQueue normal;std::vector<uint8_t> pcm(3840);
    for(uint32_t i=0;i<500;++i){
        std::fill(pcm.begin(),pcm.end(),static_cast<uint8_t>(i));const int64_t time=1000000+int64_t(i)*20000;
        Require(normal.Push(pcm.data(),static_cast<uint32_t>(pcm.size()),time,time),"Normal push");normal.DiscardExpired(time+1000);
        auto packet=normal.Pop();Require(packet.bytes==pcm&&packet.time==time,"Normal PCM/QPC must remain exact");
    }
    Require(normal.Empty()&&normal.QueuedBytes()==0&&normal.DroppedFrames()==0,"Normal delivery must be lossless");
    LiveAudioQueue capacity;
    for(uint32_t i=0;i<10000;++i){
        std::fill(pcm.begin(),pcm.end(),static_cast<uint8_t>(i%251));
        Require(capacity.Push(pcm.data(),3840,1000000,1000000),"Capacity push");
        Require(capacity.QueuedBytes()<=AudioBytes&&capacity.PacketCount()<=50,"Payload/count must not grow past the hard bound");
    }
    Require(capacity.DroppedPackets()==9950&&capacity.DroppedFrames()==uint64_t(9950)*960,"Exact whole-packet drop accounting");
    for(uint32_t i=9950;i<10000;++i){auto packet=capacity.Pop();Require(packet.bytes.front()==i%251&&packet.time==1000000,"Newest bounded FIFO suffix retained");}
    LiveAudioQueue age;
    for(int64_t i=0;i<100;++i)Require(age.Push(pcm.data(),3840,1000000+i*20000,1000000+i*20000),"Age push");
    Require(age.PacketCount()==11&&age.DroppedPackets()==89,"Age budget must evict stale packets before capacity pressure");
    int64_t previous{};
    while(!age.Empty()){auto packet=age.Pop();Require(packet.time>=2780000&&packet.time>previous,"QPC ordering and freshness preserved");previous=packet.time;}
    Require(age.Push(pcm.data(),3840,4000000,4000000),"Stall push");age.DiscardExpired(5500000);
    Require(age.Empty(),"Stale audio expires on publication even without new input");
    Require(age.Push(pcm.data(),3840,5500000,5500000),"Recovery push");Require(age.Pop().time==5500000,"Recovery retains original QPC without replay/rebase");
    const auto dropped=age.DroppedFrames();Require(age.Push(pcm.data(),3840,1000000,6000000)&&age.Empty()&&age.DroppedFrames()==dropped+960,"Stale incoming packet is discarded whole");
    Require(!age.Push(nullptr,3840,0,0)&&!age.Push(pcm.data(),0,0,0)&&!age.Push(pcm.data(),3,0,0)&&!age.Push(pcm.data(),AudioBytes+4,0,0),"Malformed packet admission remains strict");
    std::vector<uint8_t> maximum(AudioBytes);Require(age.Push(maximum.data(),AudioBytes,7000000,7000000)&&age.QueuedBytes()==AudioBytes,"A valid maximum-sized packet remains supported");
    age.DiscardExpired(7200001);Require(age.Empty(),"Large stale packet is evicted without partial PCM slicing");
    std::puts("PASS live PCM normal lossless path, hard capacity, bounded age, newest FIFO suffix, exact QPC, malformed input and stall recovery");
}
void Clock(){
    int64_t value{};
    Require(TryQpcMicroseconds(10,3,value)&&value==3333333,"Fractional QPC conversion");
    constexpr auto maximum=(std::numeric_limits<uint64_t>::max)();
    Require(TryQpcMicroseconds(maximum,maximum-1,value)&&value==1000000,"Large intermediate conversion cannot overflow");
    Require(!TryQpcMicroseconds(maximum,1,value)&&!TryQpcMicroseconds(1,0,value),"Overflow and zero-frequency are rejected");
    Require(TryQpcMicroseconds(static_cast<uint64_t>((std::numeric_limits<int64_t>::max)()),1000000,value)&&value==(std::numeric_limits<int64_t>::max)(),"Largest representable timestamp boundary");
    const auto first=CaptureQpcMicroseconds(),second=CaptureQpcMicroseconds();Require(second>=first&&first>0,"Actual performance-counter clock is monotonic");
    std::puts("PASS exact overflow-resistant QPC microsecond conversion and monotonic native clock");
}
}
int main(){try{Policy();Clock();return 0;}catch(const std::exception& error){std::fprintf(stderr,"FAIL %s\n",error.what());return 1;}}
