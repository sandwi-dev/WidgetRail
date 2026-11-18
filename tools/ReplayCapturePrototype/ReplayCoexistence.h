#pragma once
#include "../DiscordWebViewPrototype/CaptureWorkerProtocol.h"
#include <atomic>
#include <thread>
#include <mutex>
#include <exception>

// Consumer of the production raw streaming worker's existing bounded credits.
// No website, network transport or provider credentials are involved.
class RawReplayCompanion {
    HANDLE video{},audio{};
    void* pixels{};void* pcm{};void* session{};
    std::thread drain;
    std::atomic<bool> stopping{};
    std::mutex failureGate;
    std::exception_ptr failure;
    static constexpr uint64_t Epoch=73;
    static constexpr uint32_t VideoBytes=2560*720*4;
public:
    std::atomic<uint64_t> videos{},audios{};
    std::atomic<int64_t> firstVideo{},lastVideo{};
    RawReplayCompanion(const std::wstring& executable,FixtureProcess& source) {
        try {
            video=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,VideoBytes,nullptr);
            audio=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,capture_worker::AudioBytes,nullptr);
            winrt::check_bool(video&&audio);
            pixels=MapViewOfFile(video,FILE_MAP_READ|FILE_MAP_WRITE,0,0,VideoBytes);
            pcm=MapViewOfFile(audio,FILE_MAP_READ|FILE_MAP_WRITE,0,0,capture_worker::AudioBytes);
            winrt::check_bool(pixels&&pcm);
            capture_worker::Configuration c;c.epoch=Epoch;c.window=reinterpret_cast<uint64_t>(source.Window());
            c.created=source.Created();c.processId=source.Id();c.includeAudio=1;c.maximumWidth=2560;c.maximumHeight=720;c.framesPerSecond=30;
            winrt::check_bool(GetClassNameW(source.Window(),c.className,256)>0);
            Hr(WrailCaptureWorkerStart(executable.c_str(),&c,reinterpret_cast<uint64_t>(video),VideoBytes,
                reinterpret_cast<uint64_t>(audio),capture_worker::AudioBytes,&session));
            drain=std::thread([this]{Drain();});
        }catch(...){Cleanup();throw;}
    }
    ~RawReplayCompanion(){stopping=true;if(drain.joinable())drain.join();Cleanup();}
    void ThrowIfFailed(){std::lock_guard lock(failureGate);if(failure)std::rethrow_exception(failure);}
    void Stop() {
        stopping=true;
        capture_worker::StopResult stopped;Hr(WrailCaptureWorkerStop(session,1000,&stopped));
        if(drain.joinable())drain.join();ThrowIfFailed();
        Check(stopped.exited&&!stopped.forced,"raw streaming worker stops cooperatively without owning replay");
    }
private:
    void Cleanup() noexcept {
        if(session){WrailCaptureWorkerRelease(session);session=nullptr;}
        if(pixels){UnmapViewOfFile(pixels);pixels=nullptr;}if(pcm){UnmapViewOfFile(pcm);pcm=nullptr;}
        if(video){CloseHandle(video);video=nullptr;}if(audio){CloseHandle(audio);audio=nullptr;}
    }
    void Drain() noexcept {
        try {
            while(!stopping) {
                Hr(WrailCaptureWorkerHeartbeat(session,Epoch));
                capture_worker::Packet packet;
                const auto result=WrailCaptureWorkerRead(session,&packet,100);
                if(stopping)break;
                Hr(result);if(result==S_FALSE)continue;
                if(packet.epoch!=Epoch||packet.kind==capture_worker::Kind::Ended)throw std::runtime_error("raw worker lost exact live session");
                if(packet.kind==capture_worker::Kind::Video) {
                    if(packet.bytes!=packet.width*packet.height*4||packet.bytes>VideoBytes||packet.width==0||packet.height==0)
                        throw std::runtime_error("raw video exceeded mapping bound");
                    if(videos.fetch_add(1)==0)firstVideo=packet.microseconds;
                    else if(packet.microseconds<=lastVideo)throw std::runtime_error("raw video time regressed");
                    lastVideo=packet.microseconds;
                }else if(packet.kind==capture_worker::Kind::Audio) {
                    if(packet.bytes==0||packet.bytes>capture_worker::AudioBytes||packet.bytes%4!=0)
                        throw std::runtime_error("raw PCM exceeded mapping bound");
                    ++audios;
                }else if(packet.kind==capture_worker::Kind::Ready)continue;
                else throw std::runtime_error("unexpected raw packet");
                if(WrailCaptureWorkerAcknowledge(session,Epoch,static_cast<uint32_t>(packet.kind),packet.sequence)!=S_OK)
                    throw std::runtime_error("raw worker credit was not exact");
            }
        }catch(...){if(!stopping){std::lock_guard lock(failureGate);failure=std::current_exception();}}
    }
};

void Coexist(Worker& replay,FixtureProcess& source,const std::wstring& workerPath,const std::wstring& output) {
    const auto separator=workerPath.find_last_of(L"\\/");
    Check(separator!=std::wstring::npos,"coexistence uses an explicit production worker directory");
    const auto rawPath=workerPath.substr(0,separator+1)+L"WebRtcCaptureWorker.exe";
    auto bounded=[](const State& state){
        if(state.residentBytes>32ull*1024*1024||state.bufferedTicks>150000000)
            throw std::runtime_error("coexistence exceeded replay memory/duration budget");
    };
    RawReplayCompanion raw(rawPath,source);
    auto state=replay.Wait([&](const State& next){raw.ThrowIfFailed();bounded(next);return next.bufferedTicks>=40000000&&raw.videos>=80&&raw.audios>=50;});
    Check(state.phase==Phase::Recording,"raw streaming video/audio and replay acquisition run concurrently");
    const auto rawBeforeSave=raw.videos.load();
    replay.Save(output+L".concurrent.pending");
    const auto until=GetTickCount64()+1000;
    state=replay.Wait([&](const State& next){raw.ThrowIfFailed();bounded(next);return GetTickCount64()>=until;});
    Check(raw.videos>rawBeforeSave+15,"raw streaming keeps consuming bounded credits during replay Save");
    const auto rate=double(raw.videos-1)*1000000/double(raw.lastVideo-raw.firstVideo);
    printf("COEXIST rawFrames=%llu audioPackets=%llu fps=%.3f replayDropped=%llu residentBytes=%llu\n",
        static_cast<unsigned long long>(raw.videos.load()),static_cast<unsigned long long>(raw.audios.load()),rate,
        static_cast<unsigned long long>(state.droppedFrames),static_cast<unsigned long long>(state.residentBytes));
    Check(rate>=24&&rate<=36,"concurrent owned-source raw cadence stays within functional tolerance");
    raw.Stop();const auto retained=state.bufferedTicks;
    state=replay.Wait([&](const State& next){bounded(next);return next.bufferedTicks>=retained+15000000;});
    Check(state.phase==Phase::Recording,"stopping raw streaming leaves replay recording active");
    replay.Save(output+L".after-raw-stop.pending");
    RawReplayCompanion replacement(rawPath,source);
    replay.Wait([&](const State& next){replacement.ThrowIfFailed();bounded(next);return replacement.videos>=30&&replacement.audios>=20;});
    Hr(WrailReplayWorkerRequestStop(replay.value,replay.epoch));
    replay.Wait([&](const State& next){replacement.ThrowIfFailed();return (next.flags&Ended)!=0;},10000,true);
    const auto remaining=replacement.videos.load();const auto deadline=GetTickCount64()+3000;
    while(replacement.videos<remaining+30&&GetTickCount64()<deadline){replacement.ThrowIfFailed();Sleep(10);}
    Check(replacement.videos>=remaining+30,"stopping replay leaves the independently owned raw streaming worker active");
    replacement.Stop();
    puts("PASS bounded production replay/raw-stream coexistence; no Discord connection or game-FPS claim");
}
