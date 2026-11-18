#pragma once
#include <algorithm>
#include <atomic>
#include <cstdint>
#include <deque>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <vector>

namespace widgetrail::capture {
enum class ReplayStream { Video, Audio };
enum class ReplayBufferFailure { None, OutputOrder, KeyframeCadence };
class ReplayBufferException : public std::runtime_error {
public:
    const ReplayBufferFailure failure;
    ReplayBufferException(ReplayBufferFailure value,const char* text):std::runtime_error(text),failure(value){}
};
struct ReplayBudget { std::atomic_size_t resident{}; };
struct ReplayPayload {
    std::shared_ptr<ReplayBudget> budget;
    std::vector<uint8_t> bytes;
    ReplayPayload(std::shared_ptr<ReplayBudget> owner,const uint8_t* data,size_t count):budget(std::move(owner)),bytes(data,data+count)
    {budget->resident.fetch_add(count);}
    ~ReplayPayload(){budget->resident.fetch_sub(bytes.size());}
};
struct ReplayPacket {
    ReplayStream stream{};
    int64_t time{},duration{},decode{};
    bool key{};
    std::shared_ptr<const ReplayPayload> payload;
};
struct ReplaySnapshot {
    std::vector<ReplayPacket> packets;
    int64_t start{},end{};
};
struct ReplayBufferStatus { size_t residentBytes{},bufferBytes{},packetCount{};int64_t duration{};uint64_t dropped{}; };

// Bounded compressed originals; one save snapshot may retain packets after eviction.
// Its retained payload is charged against the SAME byte budget, not an invisible copy.
class EncodedReplayBuffer {
    static constexpr size_t MaximumPackets=32768;
    mutable std::mutex gate;
    const int64_t durationLimit;
    const size_t byteLimit;
    const bool requireAudio;
    const int64_t maximumKeyframeInterval;
    std::shared_ptr<ReplayBudget> budget=std::make_shared<ReplayBudget>();
    std::deque<ReplayPacket> video,audio;
    std::weak_ptr<ReplaySnapshot> saving;
    size_t buffered{};
    int64_t end{};
    int64_t lastKeyTime{-1},waitingKeySince{-1};
    int64_t lastVideoTime{-1},lastAudioTime{-1},lastAudioDecode{-1};
    bool awaitingKeyframe{};
    uint64_t dropped{};
    void DropFront(std::deque<ReplayPacket>& queue){buffered-=queue.front().payload->bytes.size();queue.pop_front();}
    void TrimAudio() {
        while(!audio.empty() && (video.empty() || audio.front().time+audio.front().duration<=video.front().time))DropFront(audio);
    }
    bool DropGop() {
        if(video.empty())return false;
        DropFront(video);
        while(!video.empty() && !video.front().key)DropFront(video);
        TrimAudio();return true;
    }
    bool MakeRoom(size_t count,bool replacementKeyframe,int64_t incomingTime) {
        const auto total=video.size()+audio.size();
        const auto fits=[&](size_t reclaimed,size_t removed) {
            const auto resident=budget->resident.load();
            return (resident<=byteLimit-count || reclaimed>=resident-(byteLimit-count)) && total-removed<MaximumPackets;
        };
        if(fits(0,0))return true;
        size_t removeVideo{},removeAudio{},reclaimed{};
        const auto reclaim=[&](const ReplayPacket& packet){
            // A save may concurrently release its reference, but cannot acquire
            // a new ring reference while this gate is held. This is therefore a
            // conservative estimate of bytes actually freed by dropping a GOP.
            if(packet.payload.use_count()==1)reclaimed+=packet.payload->bytes.size();
        };
        const auto commit=[&] {
            for(size_t i=0;i<removeVideo;++i)DropFront(video);
            for(size_t i=0;i<removeAudio;++i)DropFront(audio);
        };
        if(video.empty()) {
            while(removeAudio<audio.size()) {
                reclaim(audio[removeAudio++]);
                if(fits(reclaimed,removeAudio)){commit();return true;}
            }
            return false;
        }
        while(removeVideo<video.size()) {
            do {reclaim(video[removeVideo++]);}while(removeVideo<video.size()&&!video[removeVideo].key);
            // A dependent frame cannot replace its own reference chain. Keep
            // the last valid GOP until a new keyframe can actually be admitted.
            if(removeVideo==video.size()&&!replacementKeyframe)return false;
            const auto cutoff=removeVideo<video.size()?video[removeVideo].time:incomingTime;
            while(removeAudio<audio.size()&&audio[removeAudio].time+audio[removeAudio].duration<=cutoff)reclaim(audio[removeAudio++]);
            if(fits(reclaimed,removeVideo+removeAudio)){commit();return true;}
        }
        // In particular, never erase the live window merely because its bytes
        // remain charged to a stalled save and eviction cannot admit anything.
        return false;
    }
    int64_t AvailableEnd() const {
        if(!requireAudio)return end;
        if(audio.empty()||video.empty())return 0;
        const auto watermark=std::min(end,audio.back().time+audio.back().duration);
        int64_t available{};
        for(const auto& packet:video)if(packet.time+packet.duration<=watermark)available=std::max(available,packet.time+packet.duration);
        return available;
    }
public:
    EncodedReplayBuffer(int64_t duration100ns,size_t maximumBytes,bool audioRequired=false,int64_t keyframeInterval100ns=20000000)
        :durationLimit(duration100ns),byteLimit(maximumBytes),requireAudio(audioRequired),maximumKeyframeInterval(keyframeInterval100ns) {
        if(durationLimit<=0 || byteLimit==0 || maximumKeyframeInterval<=0)throw std::invalid_argument("Replay limits must be positive");
    }
    bool Push(ReplayStream stream,int64_t time,int64_t duration,int64_t decode,bool key,const uint8_t* data,size_t count) {
        if((stream!=ReplayStream::Video && stream!=ReplayStream::Audio) || time<0 || duration<=0 || duration>100000000 || time>INT64_MAX-duration || !data || count==0)throw std::invalid_argument("Invalid encoded packet");
        std::lock_guard lock(gate);
        // Replay currently requires low-latency, presentation-ordered output.
        // A reordering encoder must be negotiated differently before adoption;
        // never assemble an apparently valid clip with missing future references.
        if((stream==ReplayStream::Video && (decode!=time||time<lastVideoTime)) ||
            (stream==ReplayStream::Audio && (time<lastAudioTime||decode<lastAudioDecode)))
            throw ReplayBufferException(ReplayBufferFailure::OutputOrder,"Reordered replay output is unsupported");
        if(stream==ReplayStream::Video)lastVideoTime=time;else{lastAudioTime=time;lastAudioDecode=decode;}
        if(stream==ReplayStream::Video) {
            if(key){lastKeyTime=time;waitingKeySince=-1;}
            else {
                if(lastKeyTime<0 && waitingKeySince<0)waitingKeySince=time;
                const auto since=lastKeyTime>=0?lastKeyTime:waitingKeySince;
                if(time-since>maximumKeyframeInterval)throw ReplayBufferException(ReplayBufferFailure::KeyframeCadence,"Replay encoder did not provide bounded keyframes");
            }
        }
        if(stream==ReplayStream::Video && (video.empty()||awaitingKeyframe) && !key){++dropped;return false;}
        // AAC callbacks can lag video eviction. A packet wholly before the
        // retained keyframe cannot help a future save, so it must not displace
        // useful GOPs merely to be trimmed on the next video callback.
        if(stream==ReplayStream::Audio && !video.empty() && time+duration<=video.front().time){++dropped;return false;}
        if(stream==ReplayStream::Audio && video.empty() && !requireAudio){++dropped;return false;}
        if(count>byteLimit || !MakeRoom(count,stream==ReplayStream::Video&&key,time)) {
            ++dropped;if(stream==ReplayStream::Video)awaitingKeyframe=true;return false;
        }
        auto payload=std::make_shared<ReplayPayload>(budget,data,count);
        (stream==ReplayStream::Video?video:audio).push_back({stream,time,duration,decode,key,std::move(payload)});buffered+=count;
        if(stream==ReplayStream::Video) {
            if(key)awaitingKeyframe=false;
            end=std::max(end,time+duration);
            // Hard upper duration bound: drop the preceding GOP instead of
            // retaining an arbitrarily old keyframe. Actual replay may be shorter.
            while(!video.empty() && end-video.front().time>durationLimit)DropGop();
            TrimAudio();
        }else if(video.empty()) {
            // AAC may finish before the first hardware video callback. Hold at
            // most one second under the same byte budget; no unbounded warmup.
            const auto cutoff=time+duration-std::min<int64_t>(durationLimit,10000000);
            while(!audio.empty() && audio.front().time+audio.front().duration<=cutoff)DropFront(audio);
        }
        return true;
    }
    std::shared_ptr<ReplaySnapshot> Snapshot(int64_t requestedDuration) {
        std::lock_guard lock(gate);
        if(requestedDuration<=0 || video.empty() || !saving.expired())return {};
        const auto available=AvailableEnd();
        if(available<=video.front().time)return {};
        const auto cutoff=available-std::min(requestedDuration,durationLimit);
        auto first=std::find_if(video.begin(),video.end(),[&](const auto& packet){return packet.key && packet.time>=cutoff && packet.time<available;});
        if(first==video.end())return {};
        auto result=std::make_shared<ReplaySnapshot>();result->start=first->time;result->end=available;
        for(auto packet=first;packet!=video.end() && packet->time+packet->duration<=available;++packet)result->packets.push_back(*packet);
        for(const auto& packet:audio)
            if(packet.time+packet.duration>result->start && packet.time<result->end)result->packets.push_back(packet);
        std::stable_sort(result->packets.begin(),result->packets.end(),[](const auto& left,const auto& right){return left.decode<right.decode;});
        saving=result;return result;
    }
    ReplayBufferStatus Status() const {
        std::lock_guard lock(gate);
        return {budget->resident.load(),buffered,video.size()+audio.size(),video.empty()?0:std::max<int64_t>(0,AvailableEnd()-video.front().time),dropped};
    }
};
}
