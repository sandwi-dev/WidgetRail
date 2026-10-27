#pragma once
#include <Windows.h>
#include <cstdint>
#include "CaptureQuality.h"

namespace capture_worker {
constexpr uint32_t Magic=0x57524357;
// Version2 adds an inherited data-ready event to the private worker launch.
// Packet/configuration layouts and public host entry points are unchanged.
constexpr uint32_t Version=2;
constexpr uint32_t AudioBytes=192000;
constexpr uint32_t TestPattern=1;
constexpr uint32_t TestBlockedExit=2;
struct Configuration {
    uint32_t size{sizeof(Configuration)},flags{};
    uint64_t epoch{},window{},created{};
    uint32_t processId{},includeAudio{},maximumWidth{},maximumHeight{},framesPerSecond{},reserved{};
    wchar_t className[256]{};
};
static_assert(sizeof(Configuration)==568);
struct Control {
    uint32_t magic{Magic},version{Version};
    Configuration configuration;
    volatile LONG stop{},videoCredit{1},audioCredit{1},reason{};
    volatile LONG64 heartbeat{};
};
enum class Kind:uint32_t {Video=1,Audio=2,Ready=3,Ended=4};
struct Packet {
    uint32_t magic{Magic},version{Version};
    Kind kind{};uint32_t bytes{};
    uint64_t epoch{},sequence{};
    uint32_t width{},height{};
    int64_t microseconds{};
    int32_t result{};uint32_t flags{};
};
static_assert(sizeof(Packet)==56);
struct StopResult {uint32_t forced{},exited{};int32_t result{};uint32_t processId{};};
static_assert(sizeof(StopResult)==16);
inline bool Valid(const Configuration& c) {
    return c.size==sizeof(Configuration)&&c.epoch>0&&c.epoch<=9007199254740991ULL&&c.reserved==0&&

#ifdef CAPTURE_WORKER_VALIDATION
        c.flags<=3&&
#else
        c.flags==0&&
#endif
        c.includeAudio<=1&&ValidLiveCaptureQuality({c.maximumWidth,c.maximumHeight,c.framesPerSecond})&&
        ((c.flags&TestPattern)||(c.window&&c.processId&&c.created&&c.className[0]&&c.className[255]==0));
}
using Continue=int32_t(__stdcall*)();
using Frame=void(__stdcall*)(uint32_t,uint32_t,uint32_t,int64_t,const void*,uint32_t);
}

#ifdef CAPTURE_WORKER_HOST_EXPORTS
#define CAPTURE_WORKER_API extern "C" __declspec(dllexport)
#else
#define CAPTURE_WORKER_API extern "C" __declspec(dllimport)
#endif
CAPTURE_WORKER_API int32_t __stdcall WrailCaptureWorkerStart(const wchar_t*,const capture_worker::Configuration*,
    uint64_t,uint32_t,uint64_t,uint32_t,void**) noexcept;
CAPTURE_WORKER_API int32_t __stdcall WrailCaptureWorkerRead(void*,capture_worker::Packet*,uint32_t) noexcept;
CAPTURE_WORKER_API int32_t __stdcall WrailCaptureWorkerAcknowledge(void*,uint64_t,uint32_t,uint64_t) noexcept;
CAPTURE_WORKER_API int32_t __stdcall WrailCaptureWorkerHeartbeat(void*,uint64_t) noexcept;
CAPTURE_WORKER_API int32_t __stdcall WrailCaptureWorkerStop(void*,uint32_t,capture_worker::StopResult*) noexcept;
CAPTURE_WORKER_API uint32_t __stdcall WrailCaptureWorkerProcessId(void*) noexcept;
CAPTURE_WORKER_API void __stdcall WrailCaptureWorkerRelease(void*) noexcept;

// WinRT's shared-buffer projection omits FileMappingHandle. These host-only APIs
// use the documented ICoreWebView2Interop2 bridge and native shared-buffer API.
CAPTURE_WORKER_API int32_t __stdcall WrailCaptureBuffersCreate(IUnknown*,uint32_t,uint32_t,void**) noexcept;
CAPTURE_WORKER_API int32_t __stdcall WrailCaptureBuffersMappings(void*,uint64_t*,uint64_t*) noexcept;
CAPTURE_WORKER_API int32_t __stdcall WrailCaptureBuffersPost(IUnknown*,void*,uint32_t,const wchar_t*) noexcept;
CAPTURE_WORKER_API void __stdcall WrailCaptureBuffersRelease(void*) noexcept;
