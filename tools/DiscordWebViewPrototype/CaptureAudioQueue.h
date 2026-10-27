#pragma once
#include "CaptureWorkerProtocol.h"
#include <deque>
#include <vector>
#include <utility>

namespace capture_worker {
struct AudioPacket {std::vector<uint8_t> bytes;int64_t time;};

// Live transport only. Recording/WASAPI acquisition policy is unchanged.
// 200 ms is an explicit queued-audio latency budget, not additional capacity.
// The single already-published mapping remains owned by its exact credit.
class LiveAudioQueue {
    std::deque<AudioPacket> packets;
    uint32_t bytes{};
    uint64_t droppedFrames{},droppedPackets{};
    static bool Expired(int64_t timestamp,int64_t now) noexcept {
        return now>MaximumAgeMicroseconds&&timestamp<now-MaximumAgeMicroseconds;
    }
    void RecordDrop(uint32_t length) noexcept {droppedFrames+=length/4;droppedPackets++;}
    void DropFront() {
        const auto length=static_cast<uint32_t>(packets.front().bytes.size());
        bytes-=length;RecordDrop(length);packets.pop_front();
    }
public:
    static constexpr int64_t MaximumAgeMicroseconds=200000;
    bool Push(const void* source,uint32_t length,int64_t timestamp,int64_t now) {
        // Retain existing complete stereo PCM and hard packet-size admission.
        if(!source||!length||length>AudioBytes||length%4)return false;
        DiscardExpired(now);
        if(Expired(timestamp,now)){RecordDrop(length);return true;}
        while(bytes>AudioBytes-length)DropFront();
        const auto data=static_cast<const uint8_t*>(source);
        packets.push_back({std::vector<uint8_t>(data,data+length),timestamp});bytes+=length;
        return true;
    }
    void DiscardExpired(int64_t now) {while(!packets.empty()&&Expired(packets.front().time,now))DropFront();}
    bool Empty() const noexcept{return packets.empty();}
    uint32_t QueuedBytes() const noexcept{return bytes;}
    size_t PacketCount() const noexcept{return packets.size();}
    uint64_t DroppedFrames() const noexcept{return droppedFrames;}
    uint64_t DroppedPackets() const noexcept{return droppedPackets;}
    AudioPacket Pop() {
        auto packet=std::move(packets.front());packets.pop_front();bytes-=static_cast<uint32_t>(packet.bytes.size());return packet;
    }
};
}
