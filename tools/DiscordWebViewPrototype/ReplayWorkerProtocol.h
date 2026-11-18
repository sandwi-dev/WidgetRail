#pragma once
#include <Windows.h>
#include <cstdint>

namespace replay_worker {
constexpr uint32_t Magic=0x57525250,Version=2;
constexpr uint64_t MaximumFileBytes=256ull*1024*1024;
constexpr size_t MaximumPath=32768;
enum class Phase:uint32_t { Starting=1,Recording,Paused,Stopping,Ended,Failed };
enum class Issue:uint32_t { None,Target,Encoder,Audio,Keyframes,Order,Storage,BufferEmpty,Busy,Cancelled,HostLost };
enum class SaveOutcome:uint32_t { None,Pending,Saved,Unavailable,Cancelled,Failed };
struct Configuration {
    uint32_t size{sizeof(Configuration)},version{Version},flags{},reserved{};
    uint64_t epoch{},window{},created{};
    uint32_t processId{},maximumWidth{},maximumHeight{},framesPerSecond{},bitrate{},audioMode{},includeCursor{},durationSeconds{};
    uint64_t memoryBytes{};
    wchar_t className[256]{};
    uint32_t codec{},pixelFormat{},rateControl{},quality{};
    uint64_t adapterLuid{};
    uint32_t expectedSourceWidth{},expectedSourceHeight{};
};
static_assert(sizeof(Configuration)==624);
struct State {
    uint32_t size{sizeof(State)},version{Version};Phase phase{Phase::Starting};Issue issue{};
    uint64_t epoch{},revision{};
    int64_t bufferedTicks{};uint64_t residentBytes{},droppedFrames{};
    uint32_t width{},height{},sourceWidth{},sourceHeight{},flags{};int32_t error{};
    uint64_t saveId{};SaveOutcome saveOutcome{};
    uint32_t savedWidth{},savedHeight{},savedSourceWidth{},savedSourceHeight{},savedFrames{},savedMissed{},savedAudio{};
    uint64_t savedBytes{};int64_t savedDurationTicks{};uint64_t publishedTick{};
    uint32_t codec{},pixelFormat{},rateControl{},quality{};uint64_t adapterLuid{};
};
static_assert(sizeof(State)==168);
constexpr uint32_t Saving=1,Ended=2;
#ifdef CAPTURE_WORKER_VALIDATION
struct Diagnostics {
    uint64_t callbacks{},encoded{},duplicateSlots{},encoderDrops{},compressedDrops{},frameGaps{};
    int64_t firstTime{},lastTime{};
    uint64_t shortIntervals{},regularIntervals{},longIntervals{};
};
#endif
struct Control {
    uint32_t magic{Magic},version{Version};Configuration configuration;
    volatile LONG stop{},abort{},saveReady{},reserved{};
    alignas(8) volatile LONG64 heartbeat{},stateSequence{};
    State state;
    uint64_t saveId{};uint32_t saveDuration{},padding{};
    wchar_t savePath[MaximumPath]{};
    alignas(8) volatile LONG64 cancelSave{};
#ifdef CAPTURE_WORKER_VALIDATION
    volatile LONG testPause{},testSaveDelay{},testPacketDelay{},testStorageFull{};
    Diagnostics diagnostics;
#endif
};
inline bool Valid(const Configuration& value) {
    const bool quality=value.maximumWidth>=2&&value.maximumWidth<=8192&&value.maximumWidth%2==0&&
        value.maximumHeight>=2&&value.maximumHeight<=8192&&value.maximumHeight%2==0&&
        uint64_t(value.maximumWidth)*value.maximumHeight<=3840u*2160u&&
        (value.framesPerSecond==15||value.framesPerSecond==30||value.framesPerSecond==60);
#ifdef CAPTURE_WORKER_VALIDATION
    const bool flags=value.flags<=1;
    const bool target=value.flags==1 || (value.window&&value.processId&&value.created&&value.className[0]&&value.className[255]==0);
    const bool duration=value.durationSeconds>=1&&value.durationSeconds<=120;
#else
    const bool flags=value.flags==0;
    const bool target=value.window&&value.processId&&value.created&&value.className[0]&&value.className[255]==0;
    const bool duration=value.durationSeconds>=15&&value.durationSeconds<=120;
#endif
    const bool encoding=value.codec==0?value.pixelFormat==0&&value.rateControl==0&&value.quality==0&&value.adapterLuid==0&&value.expectedSourceWidth==0&&value.expectedSourceHeight==0:
        value.codec<=3&&value.pixelFormat>=1&&value.pixelFormat<=4&&value.rateControl>=1&&value.rateControl<=3&&value.quality<=100&&value.adapterLuid!=0&&
        value.expectedSourceWidth>=2&&value.expectedSourceWidth<=8192&&value.expectedSourceHeight>=2&&value.expectedSourceHeight<=8192&&
        uint64_t(value.expectedSourceWidth)*value.expectedSourceHeight<=3840u*2160u&&
        (value.codec!=1||value.pixelFormat==1)&&(value.codec!=3||value.pixelFormat<=2);
    return encoding&&value.size==sizeof(Configuration)&&value.version==Version&&value.reserved==0&&value.epoch>0&&value.epoch<=9007199254740991ULL&&
        flags&&target&&quality&&duration&&value.bitrate>=100000&&value.bitrate<=100000000&&value.audioMode<=1&&value.includeCursor<=1&&
        value.memoryBytes>=32ull*1024*1024&&value.memoryBytes<=256ull*1024*1024;
}
}

#ifdef CAPTURE_WORKER_HOST_EXPORTS
#define REPLAY_WORKER_API extern "C" __declspec(dllexport)
#else
#define REPLAY_WORKER_API extern "C" __declspec(dllimport)
#endif
REPLAY_WORKER_API int32_t __stdcall WrailReplayWorkerStart(const wchar_t*,const replay_worker::Configuration*,void**) noexcept;
REPLAY_WORKER_API int32_t __stdcall WrailReplayWorkerRead(void*,replay_worker::State*,uint32_t) noexcept;
REPLAY_WORKER_API int32_t __stdcall WrailReplayWorkerStartV2(const wchar_t*,const replay_worker::Configuration*,void**) noexcept;
REPLAY_WORKER_API int32_t __stdcall WrailReplayWorkerReadV2(void*,replay_worker::State*,uint32_t) noexcept;
REPLAY_WORKER_API int32_t __stdcall WrailReplayWorkerHeartbeat(void*,uint64_t) noexcept;
REPLAY_WORKER_API int32_t __stdcall WrailReplayWorkerSave(void*,uint64_t,const wchar_t*,uint32_t,uint64_t*) noexcept;
REPLAY_WORKER_API int32_t __stdcall WrailReplayWorkerCancelSave(void*,uint64_t,uint64_t) noexcept;
REPLAY_WORKER_API int32_t __stdcall WrailReplayWorkerRequestStop(void*,uint64_t) noexcept;
REPLAY_WORKER_API uint32_t __stdcall WrailReplayWorkerProcessId(void*) noexcept;
REPLAY_WORKER_API void __stdcall WrailReplayWorkerRelease(void*) noexcept;
#ifdef CAPTURE_WORKER_VALIDATION
REPLAY_WORKER_API int32_t __stdcall WrailReplayWorkerTestPause(void*,uint32_t) noexcept;
REPLAY_WORKER_API int32_t __stdcall WrailReplayWorkerTestSaveDelay(void*,uint32_t) noexcept;
REPLAY_WORKER_API int32_t __stdcall WrailReplayWorkerTestPacketDelay(void*,uint32_t) noexcept;
REPLAY_WORKER_API int32_t __stdcall WrailReplayWorkerTestStorageFull(void*,uint32_t) noexcept;
REPLAY_WORKER_API int32_t __stdcall WrailReplayWorkerReadDiagnostics(void*,replay_worker::Diagnostics*) noexcept;
#endif
