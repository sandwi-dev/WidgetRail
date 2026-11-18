#include "../../src/WinUiWindowPreviewNative/EncodedReplayBuffer.h"
#include <array>
#include <cstdio>
using namespace widgetrail::capture;
static void Check(bool condition,const char* message){if(!condition)throw std::runtime_error(message);}
int main() {
    try {
        std::array<uint8_t,32> bytes{};
        EncodedReplayBuffer rolling(100,1024);
        Check(!rolling.Push(ReplayStream::Video,0,10,0,false,bytes.data(),bytes.size()),"Non-keyframe cannot begin replay");
        for(int i=0;i<21;++i)Check(rolling.Push(ReplayStream::Video,i*10,10,i*10,i%5==0,bytes.data(),bytes.size()),"Unexpected bounded push failure");
        auto clip=rolling.Snapshot(100);
        Check(clip && clip->start==150 && clip->end==210 && clip->packets.front().key && rolling.Status().duration<=100,"Time eviction must keep a keyframe inside the hard duration bound");
        Check(!rolling.Snapshot(100),"Only one pending snapshot is admitted");
        clip.reset();
        auto shortClip=rolling.Snapshot(30);
        Check(shortClip && shortClip->start==200 && shortClip->end-shortClip->start<=30,"Short replay chooses the next keyframe and reports its shorter interval");
        shortClip.reset();
        EncodedReplayBuffer capped(10000,384);
        for(int i=0;i<10;++i)capped.Push(ReplayStream::Video,i*10,10,i*10,i%5==0,bytes.data(),bytes.size());
        auto held=capped.Snapshot(1000);Check(held && held->packets.size()==10,"Expected full held snapshot");
        EncodedReplayBuffer continuity(10000,384);
        for(int i=0;i<10;++i)continuity.Push(ReplayStream::Video,i*10,10,i*10,i%5==0,bytes.data(),bytes.size());
        auto blockedSave=continuity.Snapshot(1000);
        continuity.Push(ReplayStream::Video,100,10,100,true,bytes.data(),bytes.size());
        continuity.Push(ReplayStream::Video,110,10,110,false,bytes.data(),bytes.size());
        Check(!continuity.Push(ReplayStream::Video,120,10,120,false,bytes.data(),bytes.size()),"A full held save must not exceed the byte budget");
        Check(continuity.Status().duration==120 && continuity.Status().packetCount==12,
            "A stalled save must not wipe valid live history through eviction that cannot reclaim its held payloads");
        for(size_t i=0;i<5;++i)blockedSave->packets[i].payload.reset(); // Writer consumed the first GOP.
        Check(!continuity.Push(ReplayStream::Video,130,10,130,false,bytes.data(),bytes.size()),
            "Freed bytes cannot admit dependent frames after an encoded frame was dropped");
        bool rejectedReorder=false;
        try{continuity.Push(ReplayStream::Video,125,10,125,false,bytes.data(),bytes.size());}
        catch(const ReplayBufferException& error){rejectedReorder=error.failure==ReplayBufferFailure::OutputOrder;}
        Check(rejectedReorder,"Output ordering must remain enforced across dropped frames");
        Check(continuity.Push(ReplayStream::Video,150,10,150,true,bytes.data(),bytes.size()),
            "Written save payload must be reclaimable at the next independent keyframe before the save completes");
        Check(continuity.Status().duration==110 && continuity.Status().packetCount==8 && continuity.Status().residentBytes<=384,
            "Save progress must preserve the remaining live window while making room for current frames");
        Check(continuity.Push(ReplayStream::Video,160,10,160,false,bytes.data(),bytes.size()),"Fresh GOP cannot continue after pressure recovers");
        Check(!continuity.Snapshot(1000),"Consuming saved packets must not admit an overlapping save");
        blockedSave.reset();auto continuous=continuity.Snapshot(1000);
        Check(continuous && continuous->start==50 && continuous->end==170,"Valid history and fresh tail were not retained together");
        for(size_t i=1;i<continuous->packets.size();++i)Check(continuous->packets[i].time==continuous->packets[i-1].time+10 || continuous->packets[i].key,
            "A retained video gap must restart with a keyframe, not a missing dependency");
        for(int i=10;i<200;++i) {
            capped.Push(ReplayStream::Video,i*10,10,i*10,i%5==0,bytes.data(),bytes.size());
            Check(capped.Status().residentBytes<=384,"Save-held payload escaped shared byte budget");
        }
        Check(held->packets.size()==10 && held->packets.front().time==0,"Ring eviction changed a saved snapshot");
        held.reset();
        Check(capped.Push(ReplayStream::Video,2000,10,2000,true,bytes.data(),bytes.size()),"Buffer cannot resume at keyframe after pressure releases");
        auto recovered=capped.Snapshot(1000);Check(recovered && recovered->packets.front().key,"Recovered replay is not decodable");
        recovered.reset();
        EncodedReplayBuffer av(100,1024);
        av.Push(ReplayStream::Video,100,20,100,true,bytes.data(),bytes.size());
        av.Push(ReplayStream::Audio,95,20,95,true,bytes.data(),bytes.size());
        av.Push(ReplayStream::Audio,115,20,115,true,bytes.data(),bytes.size());
        av.Push(ReplayStream::Audio,150,20,150,true,bytes.data(),bytes.size());
        auto synced=av.Snapshot(100);
        Check(synced && synced->packets.size()==3 && synced->packets[0].time==95,"Audio overlap should survive; audio beyond video end must not leak into clip");
        Check(!av.Push(ReplayStream::Video,200,10,200,true,bytes.data(),1025),"Oversized packet must not allocate or overrun");
        Check(av.Status().duration==20,"An oversized incoming packet must preserve the earlier decodable window");
        Check(!av.Push(ReplayStream::Video,210,10,210,false,bytes.data(),bytes.size()),"Oversized drop must fence dependent video frames");
        Check(av.Push(ReplayStream::Video,220,10,220,true,bytes.data(),bytes.size()) && av.Status().duration==10,
            "A new admitted keyframe must recover under the unchanged hard duration cap");
        EncodedReplayBuffer audioPressure(1000,192,true);
        for(int i=0;i<2;++i){audioPressure.Push(ReplayStream::Video,i*10,10,i*10,i==0,bytes.data(),bytes.size());
            audioPressure.Push(ReplayStream::Audio,i*10,10,i*10,true,bytes.data(),bytes.size());}
        auto audioSave=audioPressure.Snapshot(1000);
        audioPressure.Push(ReplayStream::Video,20,10,20,true,bytes.data(),bytes.size());
        audioPressure.Push(ReplayStream::Audio,20,10,20,true,bytes.data(),bytes.size());
        Check(!audioPressure.Push(ReplayStream::Video,30,10,30,false,bytes.data(),bytes.size()) && audioPressure.Status().duration==30,
            "Save pressure must preserve the common audio/video window");
        for(auto& packet:audioSave->packets)if(packet.time==0)packet.payload.reset();
        Check(audioPressure.Push(ReplayStream::Video,40,10,40,true,bytes.data(),bytes.size()) && audioPressure.Status().duration==10,
            "AV eviction must count reclaimable audio and keep the common watermark");
        Check(audioPressure.Push(ReplayStream::Audio,40,10,40,true,bytes.data(),bytes.size()) && audioPressure.Status().duration==30 && audioPressure.Status().residentBytes<=192,
            "Required audio must restore the fresh bounded window after partial save progress");
        EncodedReplayBuffer lateAudio(1000,128);
        for(int i=10;i<14;++i)lateAudio.Push(ReplayStream::Video,i*10,10,i*10,i%2==0,bytes.data(),bytes.size());
        Check(!lateAudio.Push(ReplayStream::Audio,0,10,0,true,bytes.data(),bytes.size()) && lateAudio.Status().duration==40 && lateAudio.Status().packetCount==4,
            "Audio older than the retained video window must not evict valid history to admit an unusable packet");
        EncodedReplayBuffer common(100,1024,true);
        for(int i=0;i<3;i++)common.Push(ReplayStream::Video,i*10,10,i*10,i==0,bytes.data(),bytes.size());
        Check(common.Status().duration==0 && !common.Snapshot(100),"Required audio must reach a watermark before replay becomes available");
        common.Push(ReplayStream::Audio,0,25,0,true,bytes.data(),bytes.size());
        auto aligned=common.Snapshot(100);
        Check(aligned && aligned->end==20 && common.Status().duration==20,"A live snapshot must not outrun encoded audio");
        Check(std::count_if(aligned->packets.begin(),aligned->packets.end(),[](const auto& packet){return packet.stream==ReplayStream::Video;})==2,
            "A partial video frame cannot cross the common AV watermark");
        bool reordered=false;try{common.Push(ReplayStream::Video,5,10,40,false,bytes.data(),bytes.size());}catch(const ReplayBufferException&){reordered=true;}
        Check(reordered,"Unexpected frame reordering must fail explicitly rather than produce broken clips");
        EncodedReplayBuffer earlyAudio(100,1024,true);
        Check(earlyAudio.Push(ReplayStream::Audio,0,20,0,true,bytes.data(),bytes.size()),"AAC warmup preceding first video callback must be retained");
        Check(!earlyAudio.Snapshot(100),"Audio alone cannot produce a replay clip");
        earlyAudio.Push(ReplayStream::Video,0,10,0,true,bytes.data(),bytes.size());
        auto warm=earlyAudio.Snapshot(100);
        Check(warm && warm->packets.size()==2 && earlyAudio.Status().dropped==0,"First complete AV interval lost its warmup audio");
        EncodedReplayBuffer cadence(1000,1024,false,20);
        cadence.Push(ReplayStream::Video,0,10,0,true,bytes.data(),bytes.size());
        bool missingKeys=false;try{cadence.Push(ReplayStream::Video,21,10,21,false,bytes.data(),bytes.size());}catch(const std::runtime_error&){missingKeys=true;}
        Check(missingKeys,"Unsupported keyframe cadence must fail rather than pretend replay remains available");
        std::puts("PASS replay bounds, keyframe eviction, snapshot custody and audio interval (synthetic packets)");return 0;
    }catch(const std::exception& error){std::printf("FAIL %s\n",error.what());return 1;}
}
