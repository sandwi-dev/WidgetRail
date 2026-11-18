#define CAPTURE_WORKER_HOST_EXPORTS
#include "ReplayWorkerProtocol.h"
#include "CaptureWorkerLifetime.h"
#include <memory>
#include <mutex>
#include <cstring>

namespace {
using namespace replay_worker;
using namespace widgetrail::capture::worker;
struct Session {
    Handle job,process,mapping,ready;
    Control* control{};DWORD processId{};
    std::mutex commands;uint64_t nextSave{};
    ~Session(){if(control)UnmapViewOfFile(control);}
};
bool ReadState(Session& session,State& state) {
    for(int attempt=0;attempt<32;++attempt){
        const auto before=InterlockedCompareExchange64(&session.control->stateSequence,0,0);
        if(before&1){SwitchToThread();continue;}
        MemoryBarrier();memcpy(&state,&session.control->state,sizeof(state));MemoryBarrier();
        if(before==InterlockedCompareExchange64(&session.control->stateSequence,0,0))
            return state.size==sizeof(state)&&state.version==Version&&state.epoch==session.control->configuration.epoch;
    }
    return false;
}
bool Current(Session& session,uint64_t epoch){return epoch==session.control->configuration.epoch&&
    !InterlockedCompareExchange(&session.control->abort,0,0)&&WaitForSingleObject(session.process.value,0)!=WAIT_OBJECT_0;}
}
int32_t __stdcall WrailReplayWorkerStart(const wchar_t* executable,const Configuration* configuration,void** result) noexcept {
    if(!result)return E_POINTER;*result=nullptr;
    if(!executable||!configuration||configuration->size!=sizeof(Configuration)||configuration->version!=Version||!Valid(*configuration))return E_INVALIDARG;
    try {
        auto session=std::make_unique<Session>();CreateJob(session->job);
        session->mapping.value=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(Control),nullptr);
        if(!session->mapping.value)throw Error();
        session->control=static_cast<Control*>(MapViewOfFile(session->mapping.value,FILE_MAP_READ|FILE_MAP_WRITE,0,0,sizeof(Control)));
        if(!session->control)throw Error();
        new(session->control) Control();session->control->configuration=*configuration;
        session->control->state.epoch=configuration->epoch;
        session->control->state.codec=configuration->codec?configuration->codec:1;
        session->control->state.pixelFormat=configuration->pixelFormat?configuration->pixelFormat:1;
        session->control->state.rateControl=configuration->rateControl;session->control->state.quality=configuration->quality;
        session->control->state.publishedTick=GetTickCount64();
        InterlockedExchange64(&session->control->heartbeat,static_cast<LONG64>(GetTickCount64()));
        session->ready.value=CreateEventW(nullptr,FALSE,FALSE,nullptr);if(!session->ready.value)throw Error();
        Handle mapping,ready;Inheritable(session->mapping.value,mapping);Inheritable(session->ready.value,ready);
        Launch(executable,Hex(mapping.value)+L" "+Hex(ready.value),{mapping.value,ready.value},session->job,session->process,session->processId);
        *result=session.release();return S_OK;
    }catch(HRESULT hr){return hr;}catch(...){return E_FAIL;}
}
int32_t __stdcall WrailReplayWorkerRead(void* value,State* state,uint32_t timeout) noexcept {
    if(!value||!state||timeout>1000)return E_INVALIDARG;
    auto& session=*static_cast<Session*>(value);
    if(timeout){HANDLE handles[]{session.ready.value,session.process.value};
        const auto waited=WaitForMultipleObjects(2,handles,FALSE,timeout);
        if(waited==WAIT_FAILED)return Error();}
    if(!ReadState(session,*state))return HRESULT_FROM_WIN32(ERROR_RETRY);
    // A stuck encoder/driver cannot be kept alive indefinitely by authority
    // renewal. The independent host observes capture-thread progress, including
    // paused-target publications, and terminates only this owned worker job.
    if(!(state->flags&Ended)&&state->publishedTick&&GetTickCount64()-state->publishedTick>10000){
        if(!TerminateJobObject(session.job.value,ERROR_TIMEOUT))return Error();
        state->phase=Phase::Failed;state->flags|=Ended;state->issue=Issue::HostLost;state->error=HRESULT_FROM_WIN32(ERROR_TIMEOUT);
    }
    if(WaitForSingleObject(session.process.value,0)==WAIT_OBJECT_0 && !(state->flags&Ended)){
        state->phase=Phase::Failed;state->flags|=Ended;state->issue=Issue::HostLost;state->error=HRESULT_FROM_WIN32(ERROR_BROKEN_PIPE);
    }
    return S_OK;
}
int32_t __stdcall WrailReplayWorkerHeartbeat(void* value,uint64_t epoch) noexcept {
    if(!value)return E_INVALIDARG;auto& session=*static_cast<Session*>(value);
    if(!Current(session,epoch))return RO_E_CLOSED;
    InterlockedExchange64(&session.control->heartbeat,static_cast<LONG64>(GetTickCount64()));return S_OK;
}
int32_t __stdcall WrailReplayWorkerSave(void* value,uint64_t epoch,const wchar_t* path,uint32_t duration,uint64_t* id) noexcept {
    if(!value||!path||!id||!duration||duration>120)return E_INVALIDARG;*id=0;
    const auto length=wcsnlen_s(path,MaximumPath);
    if(length<4||length>=MaximumPath||!((path[1]==L':'&&path[2]==L'\\')||(path[0]==L'\\'&&path[1]==L'\\')))return E_INVALIDARG;
    auto& session=*static_cast<Session*>(value);std::lock_guard lock(session.commands);
    if(!Current(session,epoch)||InterlockedCompareExchange(&session.control->stop,0,0))return RO_E_CLOSED;
    State current;if(!ReadState(session,current))return HRESULT_FROM_WIN32(ERROR_RETRY);
    if(current.phase==Phase::Failed||current.phase==Phase::Stopping||current.phase==Phase::Ended||(current.flags&Ended))return RO_E_CLOSED;
    if((current.flags&Saving)||InterlockedCompareExchange(&session.control->saveReady,0,0)||
        (session.nextSave && current.saveId<session.nextSave))return HRESULT_FROM_WIN32(ERROR_BUSY);
    if(duration>session.control->configuration.durationSeconds)return E_INVALIDARG;
    session.control->saveId=++session.nextSave;session.control->saveDuration=duration;
    memcpy(session.control->savePath,path,(length+1)*sizeof(wchar_t));MemoryBarrier();
    InterlockedExchange(&session.control->saveReady,1);*id=session.nextSave;return S_OK;
}
int32_t __stdcall WrailReplayWorkerCancelSave(void* value,uint64_t epoch,uint64_t id) noexcept {
    if(!value||!id)return E_INVALIDARG;auto& session=*static_cast<Session*>(value);std::lock_guard lock(session.commands);
    if(epoch!=session.control->configuration.epoch||id!=session.nextSave)return RO_E_CLOSED;
    InterlockedExchange64(&session.control->cancelSave,static_cast<LONG64>(id));return S_OK;
}
int32_t __stdcall WrailReplayWorkerRequestStop(void* value,uint64_t epoch) noexcept {
    if(!value)return E_INVALIDARG;auto& session=*static_cast<Session*>(value);std::lock_guard lock(session.commands);
    if(epoch!=session.control->configuration.epoch)return RO_E_CLOSED;
    InterlockedExchange(&session.control->stop,1);return S_OK;
}
uint32_t __stdcall WrailReplayWorkerProcessId(void* value) noexcept{return value?static_cast<Session*>(value)->processId:0;}
void __stdcall WrailReplayWorkerRelease(void* value) noexcept {
    if(!value)return;auto session=std::unique_ptr<Session>(static_cast<Session*>(value));
    InterlockedExchange(&session->control->abort,1);InterlockedExchange(&session->control->stop,1);
    if(WaitForSingleObject(session->process.value,100)!=WAIT_OBJECT_0)TerminateJobObject(session->job.value,ERROR_OPERATION_ABORTED);
    WaitForSingleObject(session->process.value,250);
}
#ifdef CAPTURE_WORKER_VALIDATION
int32_t __stdcall WrailReplayWorkerReadDiagnostics(void* value,Diagnostics* result) noexcept {
    if(!value||!result)return E_INVALIDARG;auto& session=*static_cast<Session*>(value);
    for(int i=0;i<32;++i){const auto before=InterlockedCompareExchange64(&session.control->stateSequence,0,0);
        if(before&1){SwitchToThread();continue;}MemoryBarrier();*result=session.control->diagnostics;MemoryBarrier();
        if(before==InterlockedCompareExchange64(&session.control->stateSequence,0,0))return S_OK;}
    return HRESULT_FROM_WIN32(ERROR_RETRY);
}
int32_t __stdcall WrailReplayWorkerTestPause(void* value,uint32_t paused) noexcept {
    if(!value||paused>1)return E_INVALIDARG;auto& session=*static_cast<Session*>(value);
    if(session.control->configuration.flags!=1)return E_ACCESSDENIED;
    InterlockedExchange(&session.control->testPause,static_cast<LONG>(paused));return S_OK;
}
int32_t __stdcall WrailReplayWorkerTestSaveDelay(void* value,uint32_t delay) noexcept {
    if(!value||delay>20000)return E_INVALIDARG;auto& session=*static_cast<Session*>(value);
    if(session.control->configuration.flags!=1)return E_ACCESSDENIED;
    InterlockedExchange(&session.control->testSaveDelay,static_cast<LONG>(delay));return S_OK;
}
int32_t __stdcall WrailReplayWorkerTestPacketDelay(void* value,uint32_t delay) noexcept {
    if(!value||delay>100)return E_INVALIDARG;auto& session=*static_cast<Session*>(value);
    if(session.control->configuration.flags!=1)return E_ACCESSDENIED;
    InterlockedExchange(&session.control->testPacketDelay,static_cast<LONG>(delay));return S_OK;
}
int32_t __stdcall WrailReplayWorkerTestStorageFull(void* value,uint32_t enabled) noexcept {
    if(!value||enabled>1)return E_INVALIDARG;auto& session=*static_cast<Session*>(value);
    if(session.control->configuration.flags!=1)return E_ACCESSDENIED;
    InterlockedExchange(&session.control->testStorageFull,static_cast<LONG>(enabled));return S_OK;
}
#endif

int32_t __stdcall WrailReplayWorkerStartV2(const wchar_t* executable,const Configuration* configuration,void** result) noexcept {
    return WrailReplayWorkerStart(executable,configuration,result);
}
int32_t __stdcall WrailReplayWorkerReadV2(void* session,State* state,uint32_t timeout) noexcept {
    return WrailReplayWorkerRead(session,state,timeout);
}
