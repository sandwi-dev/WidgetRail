#include "RichMediaSurfaceCoordinator.h"
#include "WidgetProtocolPresentationContract.generated.h"

#include <WebView2EnvironmentOptions.h>
#include <UIAutomation.h>
#include <appmodel.h>
#include <windowsx.h>
#include <objidl.h>
#include <wrl.h>
#include <winrt/Windows.Data.Json.h>
#include <winrt/Windows.Foundation.Collections.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <format>
#include <string>
#include <unordered_set>
#include <vector>

using Microsoft::WRL::Callback;
using Microsoft::WRL::ComPtr;

namespace widgetrail::richmedia {
namespace {

constexpr wchar_t kOrigin[] = L"https://wrail-rich-media.invalid";
constexpr wchar_t kPageUri[] = L"https://wrail-rich-media.invalid/index.html";
constexpr wchar_t kResourceFilter[] = L"*";
constexpr std::size_t kMaximumMessageCharacters = 512;

bool IsDocumentLocalIdentifier(const std::wstring_view value) noexcept {
    return !value.empty() &&
        value.size() <= protocol_contract::MaximumCapabilityIdLength &&
        std::all_of(value.begin(), value.end(), [](const wchar_t character) {
            return (character >= L'a' && character <= L'z') ||
                (character >= L'A' && character <= L'Z') ||
                (character >= L'0' && character <= L'9') ||
                character == L'-' || character == L'_' || character == L'.';
        });
}

bool CanonicalHttpsOrigin(const std::wstring_view origin) noexcept {
    if (!origin.starts_with(L"https://") || origin.size() <= 8 ||
        origin.find_first_of(L"/?#@*:", 8) != std::wstring_view::npos)
        return false;
    return std::all_of(origin.begin() + 8, origin.end(), [](const wchar_t character) {
        return (character >= L'a' && character <= L'z') ||
            (character >= L'0' && character <= L'9') ||
            character == L'-' || character == L'.';
    });
}

std::wstring BoundedSyntheticPath(const std::wstring_view origin,
                                  const std::wstring_view uri) {
    const std::wstring prefix = std::wstring{origin} + L"/";
    if (origin.empty() || !uri.starts_with(prefix)) return L"<outside-origin>";
    const auto path = uri.substr(prefix.size(), 128);
    if (path.empty() || !std::all_of(path.begin(), path.end(), [](const wchar_t character) {
            return (character >= L'a' && character <= L'z') ||
                (character >= L'A' && character <= L'Z') ||
                (character >= L'0' && character <= L'9') ||
                character == L'-' || character == L'_' || character == L'.' ||
                character == L'/';
        })) return L"<invalid-path>";
    return std::wstring{path};
}

bool IsValidPublicAdapterConfiguration(const Configuration& configuration) {
    if (configuration.resources.empty())
        return configuration.origin.empty() && configuration.entryAsset.empty();
    if (!configuration.origin.starts_with(L"https://wrail-media-") ||
        !configuration.origin.ends_with(L".invalid") ||
        configuration.origin.find_first_of(L"/?#@", 8) != std::wstring::npos ||
        configuration.entryAsset.empty() || configuration.resources.size() > 16)
        return false;
    if (configuration.allowedFrameOrigins.size() >
        protocol_contract::MaximumEmbeddedMediaFrameOriginCount)
        return false;
    std::unordered_set<std::wstring> frameOrigins;
    for (const auto& origin : configuration.allowedFrameOrigins) {
        if (!CanonicalHttpsOrigin(origin) ||
            origin.size() > protocol_contract::MaximumEmbeddedMediaFrameOriginLength ||
            !frameOrigins.insert(origin).second)
            return false;
    }
    if (configuration.allowedFrameDomainFamilies.size() >
        protocol_contract::MaximumEmbeddedMediaFrameDomainFamilyCount)
        return false;
    static const auto suffixAuthority = PublicSuffixDomainAuthority::LoadDefault();
    std::unordered_set<std::wstring> frameFamilies;
    std::size_t familyAggregate{};
    for (const auto& family : configuration.allowedFrameDomainFamilies) {
        familyAggregate += family.size();
        if (family.size() >
                protocol_contract::MaximumEmbeddedMediaFrameDomainFamilyLength ||
            familyAggregate >
                protocol_contract::MaximumEmbeddedMediaFrameDomainFamilyAggregateLength ||
            !suffixAuthority.IsRegistrableDomain(family) ||
            !frameFamilies.insert(family).second)
            return false;
    }
    std::size_t aggregate{};
    bool hasEntry{};
    std::unordered_set<std::wstring> paths;
    for (const auto& resource : configuration.resources) {
        if (resource.path.empty() || resource.path.starts_with(L"/") ||
            resource.path.find(L"..") != std::wstring::npos ||
            resource.path.find(L'\\') != std::wstring::npos ||
            resource.content.empty() || resource.content.size() > 256 * 1024 ||
            !paths.insert(resource.path).second) return false;
        aggregate += resource.content.size();
        if (aggregate > 512 * 1024) return false;
        hasEntry = hasEntry || resource.path == configuration.entryAsset;
    }
    return hasEntry;
}

constexpr char kPage[] = R"HTML(<!doctype html>
<meta charset="utf-8"><meta name="viewport" content="width=device-width">
<style>html,body{margin:0;background:#111;color:#fff;font:24px system-ui}main{padding:28px}
button{font:inherit;margin:8px;padding:12px 22px}button:focus{outline:4px solid #65b8ff}
</style><main><h1>Embedded media proof</h1><audio id="media" src="/tone.wav" loop></audio>
<button id="back">Back</button><button id="play">Play</button><button id="seek">Seek</button>
<p id="state">Ready</p></main><script>
let environmentGeneration=0,surfaceGeneration=0,sessionGeneration=0,controllerGeneration=0,documentGeneration=0;
let eventSequence=0,focus='play',spatialCommandId=0,spatialCommandSequence=0; const media=document.querySelector('#media');
const buttons=[...document.querySelectorAll('button')];
function bounds(){const r=document.activeElement.getBoundingClientRect();return{x:r.x,y:r.y,width:r.width,height:r.height};}
function emit(type,commandId,commandSequence=0){chrome.webview.postMessage({type,environmentGeneration,surfaceGeneration,sessionGeneration,controllerGeneration,documentGeneration,eventSequence:++eventSequence,commandId,commandSequence,focus,playing:!media.paused,bounds:bounds()});}
function takeSpatial(){const command={id:spatialCommandId,sequence:spatialCommandSequence};spatialCommandId=0;spatialCommandSequence=0;return command;}
function select(delta){let i=Math.max(0,buttons.indexOf(document.activeElement));i=(i+delta+buttons.length)%buttons.length;buttons[i].focus();focus=buttons[i].id;}
document.querySelector('#back').onclick=()=>{const command=takeSpatial();emit('back',command.id,command.sequence);};
document.querySelector('#play').onclick=async()=>{const command=takeSpatial();if(media.paused)await media.play();else media.pause();emit('media',command.id,command.sequence);};
document.querySelector('#seek').onclick=()=>{const command=takeSpatial();media.currentTime=Math.min(media.duration||1,media.currentTime+.2);emit('media',command.id,command.sequence);};
chrome.webview.addEventListener('message',async e=>{const m=e.data;if(!m)return;
if(m.command==='initialize'){environmentGeneration=m.environmentGeneration;surfaceGeneration=m.surfaceGeneration;sessionGeneration=m.sessionGeneration;controllerGeneration=m.controllerGeneration;documentGeneration=m.documentGeneration;eventSequence=0;buttons[1].focus();focus='play';emit('ready',m.commandId);return;}
if(m.environmentGeneration!==environmentGeneration||m.surfaceGeneration!==surfaceGeneration||m.sessionGeneration!==sessionGeneration||m.controllerGeneration!==controllerGeneration||m.documentGeneration!==documentGeneration)return;
if(m.command==='arm-activate'){spatialCommandId=m.commandId;spatialCommandSequence=m.commandSequence||0;emit('armed',m.commandId,spatialCommandSequence);return;}
let type='focus';if(m.command==='previous')select(-1);else if(m.command==='next')select(1);else if(m.command==='activate'){if(document.activeElement.id==='back')type='back';else if(document.activeElement.id==='play'){if(media.paused)await media.play();else media.pause();type='media';}else if(document.activeElement.id==='seek'){media.currentTime=Math.min(media.duration||1,media.currentTime+.2);type='media';}}
else if(m.command==='back')type='back';else if(m.command==='toggle'){if(media.paused)await media.play();else media.pause();type='media';}
else if(m.command==='seek-back'){media.currentTime=Math.max(0,media.currentTime-.2);type='media';}
else if(m.command==='seek-forward'){media.currentTime=Math.min(media.duration||1,media.currentTime+.2);type='media';}
else return;emit(type,m.commandId,m.commandSequence||0);});
</script>)HTML";

std::vector<std::byte> WaveBytes() {
    constexpr std::uint32_t sampleRate = 8000;
    constexpr std::uint32_t sampleCount = 1600;
    constexpr std::uint32_t dataBytes = sampleCount * 2;
    std::vector<std::byte> bytes(44 + dataBytes);
    const auto write16 = [&](const std::size_t offset, const std::uint16_t value) {
        bytes[offset] = static_cast<std::byte>(value & 0xff);
        bytes[offset + 1] = static_cast<std::byte>((value >> 8) & 0xff);
    };
    const auto write32 = [&](const std::size_t offset, const std::uint32_t value) {
        write16(offset, static_cast<std::uint16_t>(value));
        write16(offset + 2, static_cast<std::uint16_t>(value >> 16));
    };
    std::copy_n(reinterpret_cast<const std::byte*>("RIFF"), 4, bytes.begin());
    write32(4, 36 + dataBytes);
    std::copy_n(reinterpret_cast<const std::byte*>("WAVEfmt "), 8, bytes.begin() + 8);
    write32(16, 16); write16(20, 1); write16(22, 1); write32(24, sampleRate);
    write32(28, sampleRate * 2); write16(32, 2); write16(34, 16);
    std::copy_n(reinterpret_cast<const std::byte*>("data"), 4, bytes.begin() + 36);
    write32(40, dataBytes);
    for (std::uint32_t index = 0; index < sampleCount; ++index) {
        const std::int16_t sample = ((index / 10) % 2) ? 5000 : -5000;
        write16(44 + index * 2, static_cast<std::uint16_t>(sample));
    }
    return bytes;
}

ComPtr<IStream> StreamFor(const void* data, const std::size_t size) {
    HGLOBAL memory = GlobalAlloc(GMEM_MOVEABLE, size);
    if (!memory) return {};
    void* target = GlobalLock(memory);
    if (!target) { GlobalFree(memory); return {}; }
    std::memcpy(target, data, size);
    GlobalUnlock(memory);
    ComPtr<IStream> stream;
    if (FAILED(CreateStreamOnHGlobal(memory, TRUE, &stream))) GlobalFree(memory);
    return stream;
}

bool IsSealedMediaType(const std::wstring_view contentType) noexcept {
    return contentType.starts_with(L"audio/") || contentType.starts_with(L"video/");
}

const wchar_t* LifecycleName(const Lifecycle lifecycle) {
    switch (lifecycle) {
    case Lifecycle::Absent: return L"absent";
    case Lifecycle::EnvironmentCreating: return L"environment-creating";
    case Lifecycle::ControllerCreating: return L"controller-creating";
    case Lifecycle::ReadyHidden: return L"ready-hidden";
    case Lifecycle::Visible: return L"visible";
    case Lifecycle::Faulted: return L"faulted";
    case Lifecycle::Closing: return L"closing";
    }
    return L"unknown";
}

} // namespace

struct RichMediaSurfaceCoordinator::CallbackLease final {
    std::atomic<RichMediaSurfaceCoordinator*> owner{};
    std::atomic_uint outstandingCreates{};
    Authority authority;
};

struct RichMediaSurfaceCoordinator::EnvironmentSignal final {
    std::atomic_bool browserProcessExited{};
    std::atomic<DWORD> browserProcessId{};
    std::atomic_uint32_t browserProcessExitKind{};
};

class RichMediaEnvironment final {
public:
    ~RichMediaEnvironment() {
        if (environment5 && browserProcessExitedToken.value)
            (void)environment5->remove_BrowserProcessExited(browserProcessExitedToken);
        environment5.Reset();
        environment.Reset();
        if (!profileDirectory.empty()) {
            try {
                std::filesystem::create_directories(profileDirectory);
                const auto marker = std::filesystem::path{profileDirectory} /
                    (L".wrail-retired-owner-" +
                     std::to_wstring(GetCurrentProcessId()));
                std::ofstream stream(marker, std::ios::binary | std::ios::trunc);
                stream << "WidgetRail rich-media deferred cleanup\n";
            } catch (...) {
            }
        }
    }

private:
    ComPtr<ICoreWebView2Environment> environment;
    ComPtr<ICoreWebView2Environment5> environment5;
    std::shared_ptr<RichMediaSurfaceCoordinator::EnvironmentSignal> signal;
    EventRegistrationToken browserProcessExitedToken{};
    EnvironmentLifecycle lifecycle{EnvironmentLifecycle::Cold};
    std::wstring profileRootDirectory;
    std::wstring profileDirectory;
    std::vector<std::shared_ptr<RichMediaSurfaceCoordinator::CallbackLease>> waiters;
    std::uint64_t generation{};
    std::size_t liveControllerOwners{};
    bool faulted{};
    bool browserExitNotified{};
    std::function<void(const EnvironmentExit&)> browserExitNotification;

    friend class RichMediaSurfaceCoordinator;
    friend class RichMediaSurfaceCoordinatorTestPeer;
};

struct RichMediaSurfaceCoordinator::FrameSubscription final {
    ComPtr<ICoreWebView2Frame2> frame;
    EventRegistrationToken navigationStartingToken{};
};

void RichMediaSurfaceCoordinator::RecordBrowserProcessExit(
    const RichMediaEnvironmentHandle& sharedEnvironment,
    const DWORD processId, const bool processIdAvailable,
    const std::uint32_t exitKind, const bool exitKindAvailable) noexcept {
    if (!sharedEnvironment || !sharedEnvironment->signal) return;
    const auto& signal = sharedEnvironment->signal;
    if (processIdAvailable && processId != 0 &&
        signal->browserProcessId.load(std::memory_order_acquire) == 0)
        signal->browserProcessId.store(processId, std::memory_order_release);
    if (exitKindAvailable)
        signal->browserProcessExitKind.store(exitKind, std::memory_order_release);
    if (signal->browserProcessExited.exchange(true, std::memory_order_acq_rel)) return;
    if (!sharedEnvironment->browserExitNotified &&
        sharedEnvironment->browserExitNotification) {
        sharedEnvironment->browserExitNotified = true;
        sharedEnvironment->browserExitNotification({
            sharedEnvironment->generation,
            GetTickCount64(),
            signal->browserProcessId.load(std::memory_order_acquire),
            processIdAvailable,
            exitKind,
            exitKindAvailable,
            sharedEnvironment->liveControllerOwners,
            sharedEnvironment->waiters.size(),
        });
    }
}

bool RichMediaSurfaceCoordinator::BrowserProcessExitObserved() const noexcept {
    return environmentSignal_ &&
        environmentSignal_->browserProcessExited.load(std::memory_order_acquire);
}

bool RichMediaSurfaceCoordinator::BrowserProcessExitObserverActive() const noexcept {
    return sharedEnvironment_ && sharedEnvironment_->environment5 &&
        sharedEnvironment_->browserProcessExitedToken.value;
}

RichMediaEnvironmentHandle RichMediaSurfaceCoordinator::CreateSharedEnvironment() {
    return std::make_shared<RichMediaEnvironment>();
}

RichMediaSurfaceCoordinator::RichMediaSurfaceCoordinator()
    : sharedEnvironment_(CreateSharedEnvironment()) {}

RichMediaSurfaceCoordinator::RichMediaSurfaceCoordinator(
    RichMediaEnvironmentHandle environment)
    : sharedEnvironment_(environment ? std::move(environment) : CreateSharedEnvironment()) {}
RichMediaSurfaceCoordinator::~RichMediaSurfaceCoordinator() { Shutdown(); }

EnvironmentState RichMediaSurfaceCoordinator::environmentState() const noexcept {
    const auto& shared = *sharedEnvironment_;
    return {
        shared.lifecycle, shared.generation,
        shared.signal
            ? shared.signal->browserProcessId.load(std::memory_order_acquire) : 0,
        shared.signal && shared.signal->browserProcessExited.load(std::memory_order_acquire),
        shared.faulted, BrowserProcessExitObserverActive(), shared.profileDirectory};
}

HRESULT RichMediaSurfaceCoordinator::Initialize(Configuration configuration) noexcept {
    if (state_.lifecycle != Lifecycle::Absent || !configuration.ownerWindow ||
        !configuration.compositionTarget || configuration.bounds.right <= configuration.bounds.left ||
        configuration.bounds.bottom <= configuration.bounds.top ||
        configuration.profileRootDirectory.empty() ||
        !IsValidPublicAdapterConfiguration(configuration)) return E_INVALIDARG;
    auto& shared = *sharedEnvironment_;
    if (shared.lifecycle == EnvironmentLifecycle::ShuttingDown) return E_UNEXPECTED;
    if (!shared.profileRootDirectory.empty() &&
        shared.profileRootDirectory != configuration.profileRootDirectory) return E_INVALIDARG;
    profileRootDirectory_ = configuration.profileRootDirectory;
    shared.profileRootDirectory = configuration.profileRootDirectory;
    if (!shared.browserExitNotification && configuration.sharedEnvironmentExited)
        shared.browserExitNotification = configuration.sharedEnvironmentExited;
    const HRESULT recovery = RecoverExitedSharedEnvironment();
    if (installedAppReferer_.empty()) {
        const auto referer = ResolveInstalledAppReferer();
        if (referer) installedAppReferer_ = *referer;
    }
    configuration_ = std::move(configuration);
    state_ = {};
    waitingForSharedEnvironmentRecovery_ = false;
    controllerGeometryApplied_ = false;
    desiredVisible_ = configuration_.initiallyVisible;
    state_.authority.surfaceGeneration = ++nextSurfaceGeneration_;
    state_.authority.sessionGeneration = ++nextSessionGeneration_;
    if (recovery == E_PENDING) {
        waitingForSharedEnvironmentRecovery_ = true;
        state_.lifecycle = Lifecycle::EnvironmentCreating;
        Emit(L"Rich media lifecycle=environment-recovery-waiting generation=" +
             std::to_wstring(state_.authority.sessionGeneration));
        return E_PENDING;
    }
    if (FAILED(recovery)) {
        Fault(L"shared-environment-exited-with-live-controller", recovery);
        return S_OK;
    }
    if (recovery == S_OK)
        Emit(L"Rich media shared environment retired reason=browser-exited");
    if (shared.lifecycle == EnvironmentLifecycle::Ready &&
        !shared.faulted && shared.environment &&
        (!shared.signal || !shared.signal->browserProcessExited.load(std::memory_order_acquire))) {
        environment_ = shared.environment;
        environment5_ = shared.environment5;
        environmentSignal_ = shared.signal;
        environmentLifecycle_ = EnvironmentLifecycle::Ready;
        nextEnvironmentGeneration_ = shared.generation;
        environmentProfileDirectory_ = shared.profileDirectory;
        state_.authority.environmentGeneration = shared.generation;
        return BeginController();
    }
    if (shared.lifecycle == EnvironmentLifecycle::Creating)
        return AwaitSharedEnvironment();
    if (shared.lifecycle != EnvironmentLifecycle::Cold) return E_UNEXPECTED;
    return BeginEnvironment();
}

HRESULT RichMediaSurfaceCoordinator::RecoverExitedSharedEnvironment() noexcept {
    auto& shared = *sharedEnvironment_;
    if (shared.lifecycle != EnvironmentLifecycle::Ready || !shared.signal ||
        !shared.signal->browserProcessExited.load(std::memory_order_acquire))
        return S_FALSE;
    if (shared.liveControllerOwners != 0 || !shared.waiters.empty())
        return E_PENDING;
    if (shared.environment5 && shared.browserProcessExitedToken.value) {
        const HRESULT remove = shared.environment5->remove_BrowserProcessExited(
            shared.browserProcessExitedToken);
        if (FAILED(remove)) return remove;
    }
    shared.lifecycle = EnvironmentLifecycle::ShuttingDown;
    environmentProfileDirectory_ = shared.profileDirectory;
    shared.browserProcessExitedToken = {};
    shared.environment5.Reset();
    shared.environment.Reset();
    MarkCurrentProfileForDeferredCleanup();
    shared.signal.reset();
    shared.profileDirectory.clear();
    shared.faulted = false;
    shared.browserExitNotified = false;
    shared.lifecycle = EnvironmentLifecycle::Cold;
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::ResumeSharedEnvironmentRecovery() noexcept {
    if (!waitingForSharedEnvironmentRecovery_ ||
        state_.lifecycle != Lifecycle::EnvironmentCreating) {
        return S_FALSE;
    }
    const HRESULT recovery = RecoverExitedSharedEnvironment();
    if (recovery == E_PENDING) return E_PENDING;
    if (FAILED(recovery)) return recovery;
    waitingForSharedEnvironmentRecovery_ = false;
    auto& shared = *sharedEnvironment_;
    if (shared.lifecycle == EnvironmentLifecycle::Ready && shared.environment &&
        (!shared.signal ||
         !shared.signal->browserProcessExited.load(std::memory_order_acquire))) {
        environment_ = shared.environment;
        environment5_ = shared.environment5;
        environmentSignal_ = shared.signal;
        environmentLifecycle_ = EnvironmentLifecycle::Ready;
        nextEnvironmentGeneration_ = shared.generation;
        environmentProfileDirectory_ = shared.profileDirectory;
        state_.authority.environmentGeneration = shared.generation;
        return BeginController();
    }
    if (shared.lifecycle == EnvironmentLifecycle::Creating)
        return AwaitSharedEnvironment();
    if (shared.lifecycle != EnvironmentLifecycle::Cold) return E_UNEXPECTED;
    return BeginEnvironment();
}

HRESULT RichMediaSurfaceCoordinator::Retry(Configuration configuration) noexcept {
    if (state_.lifecycle != Lifecycle::Absent || !retrySurfaceGeneration_ ||
        !configuration.ownerWindow ||
        !configuration.compositionTarget || configuration.profileRootDirectory.empty() ||
        !IsValidPublicAdapterConfiguration(configuration) ||
        configuration.profileRootDirectory != profileRootDirectory_) return E_UNEXPECTED;
    if (sharedEnvironment_->faulted ||
        (sharedEnvironment_->signal &&
         sharedEnvironment_->signal->browserProcessExited.load(std::memory_order_acquire)))
        return E_UNEXPECTED;
    configuration_ = std::move(configuration);
    state_ = {};
    controllerGeometryApplied_ = false;
    state_.authority.surfaceGeneration = retrySurfaceGeneration_;
    state_.authority.sessionGeneration = ++nextSessionGeneration_;
    retrySurfaceGeneration_ = 0;
    desiredVisible_ = configuration_.initiallyVisible;
    if (sharedEnvironment_->lifecycle == EnvironmentLifecycle::Ready) {
        environmentFaulted_ = false;
        environment_ = sharedEnvironment_->environment;
        environment5_ = sharedEnvironment_->environment5;
        environmentSignal_ = sharedEnvironment_->signal;
        environmentLifecycle_ = EnvironmentLifecycle::Ready;
        nextEnvironmentGeneration_ = sharedEnvironment_->generation;
        state_.authority.environmentGeneration = sharedEnvironment_->generation;
        return BeginController();
    }
    return BeginEnvironment();
}

std::shared_ptr<RichMediaSurfaceCoordinator::CallbackLease>
RichMediaSurfaceCoordinator::CreateCallbackLease() noexcept {
    auto lease = std::make_shared<CallbackLease>();
    lease->owner.store(this, std::memory_order_release);
    lease->authority = state_.authority;
    callbackLease_ = lease;
    return lease;
}

bool RichMediaSurfaceCoordinator::IsCurrentCallback(
    const std::shared_ptr<CallbackLease>& lease, const bool requireDocument) const noexcept {
    if (!lease || lease->owner.load(std::memory_order_acquire) != this) return false;
    return lease->authority.environmentGeneration ==
            state_.authority.environmentGeneration &&
        lease->authority.surfaceGeneration == state_.authority.surfaceGeneration &&
        lease->authority.sessionGeneration == state_.authority.sessionGeneration &&
        lease->authority.controllerGeneration == state_.authority.controllerGeneration &&
        (!requireDocument ||
         lease->authority.documentGeneration == state_.authority.documentGeneration);
}

void RichMediaSurfaceCoordinator::RetireCallbacks() noexcept {
    if (callbackLease_) {
        callbackLease_->owner.store(nullptr, std::memory_order_release);
        retiredLeases_.push_back(callbackLease_);
    }
    callbackLease_.reset();
}

HRESULT RichMediaSurfaceCoordinator::BeginEnvironment() noexcept {
    auto& shared = *sharedEnvironment_;
    if (shared.lifecycle != EnvironmentLifecycle::Cold ||
        profileRootDirectory_.empty()) return E_UNEXPECTED;
    CleanupMarkedPriorProfiles();
    shared.lifecycle = EnvironmentLifecycle::Creating;
    shared.faulted = false;
    shared.generation++;
    shared.profileRootDirectory = profileRootDirectory_;
    shared.profileDirectory =
        (std::filesystem::path{profileRootDirectory_} /
         (L"wrail-rich-media-" + std::to_wstring(GetCurrentProcessId()) + L"-" +
          std::to_wstring(shared.generation))).wstring();
    environmentLifecycle_ = EnvironmentLifecycle::Creating;
    environmentFaulted_ = false;
    nextEnvironmentGeneration_ = shared.generation;
    state_.authority.environmentGeneration = shared.generation;
    environmentProfileDirectory_ = shared.profileDirectory;
    state_.authority.controllerGeneration = 0;
    state_.authority.documentGeneration = 0;
    state_.authority.eventSequence = 0;
    pendingCommand_.reset();
    pendingNavigationId_ = 0;
    const auto lease = CreateCallbackLease();
    state_.lifecycle = Lifecycle::EnvironmentCreating;
    shared.waiters.push_back(lease);
    Emit(L"Rich media lifecycle=environment-creating generation=" +
         std::to_wstring(state_.authority.sessionGeneration));
    ComPtr<ICoreWebView2EnvironmentOptions> options =
        Microsoft::WRL::Make<CoreWebView2EnvironmentOptions>();
    lease->outstandingCreates.fetch_add(1, std::memory_order_relaxed);
    const HRESULT result = CreateCoreWebView2EnvironmentWithOptions(
        nullptr, environmentProfileDirectory_.c_str(), options.Get(),
        Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
            [sharedEnvironment = sharedEnvironment_, lease](
                HRESULT status, ICoreWebView2Environment* environment) {
                const HRESULT callbackResult =
                    CompleteSharedEnvironmentCreation(
                        sharedEnvironment, lease, status, environment);
                lease->outstandingCreates.fetch_sub(1, std::memory_order_release);
                return callbackResult;
            }).Get());
    if (FAILED(result)) {
        lease->outstandingCreates.fetch_sub(1, std::memory_order_release);
        (void)CompleteSharedEnvironmentCreation(
            sharedEnvironment_, lease, result, nullptr);
    }
    return result;
}

HRESULT RichMediaSurfaceCoordinator::AwaitSharedEnvironment() noexcept {
    auto& shared = *sharedEnvironment_;
    if (shared.lifecycle != EnvironmentLifecycle::Creating ||
        shared.generation == 0 || shared.profileDirectory.empty()) return E_UNEXPECTED;
    environmentLifecycle_ = EnvironmentLifecycle::Creating;
    environmentFaulted_ = false;
    nextEnvironmentGeneration_ = shared.generation;
    state_.authority.environmentGeneration = shared.generation;
    environmentProfileDirectory_ = shared.profileDirectory;
    state_.authority.controllerGeneration = 0;
    state_.authority.documentGeneration = 0;
    state_.authority.eventSequence = 0;
    pendingCommand_.reset();
    pendingNavigationId_ = 0;
    const auto lease = CreateCallbackLease();
    state_.lifecycle = Lifecycle::EnvironmentCreating;
    shared.waiters.push_back(lease);
    Emit(L"Rich media lifecycle=environment-waiting generation=" +
         std::to_wstring(state_.authority.sessionGeneration));
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::ResumeSharedEnvironment(
    const std::shared_ptr<CallbackLease>& lease) noexcept {
    if (!IsCurrentCallback(lease, false) ||
        state_.lifecycle != Lifecycle::EnvironmentCreating) return S_FALSE;
    const auto& shared = *sharedEnvironment_;
    if (shared.lifecycle != EnvironmentLifecycle::Ready || shared.faulted ||
        !shared.environment || !shared.signal ||
        shared.signal->browserProcessExited.load(std::memory_order_acquire))
        return E_UNEXPECTED;
    environment_ = shared.environment;
    environment5_ = shared.environment5;
    environmentSignal_ = shared.signal;
    environmentLifecycle_ = EnvironmentLifecycle::Ready;
    nextEnvironmentGeneration_ = shared.generation;
    environmentProfileDirectory_ = shared.profileDirectory;
    state_.authority.environmentGeneration = shared.generation;
    Emit(L"Rich media lifecycle=environment-resumed generation=" +
         std::to_wstring(state_.authority.sessionGeneration));
    return BeginController();
}

void RichMediaSurfaceCoordinator::FailSharedEnvironment(
    const std::shared_ptr<CallbackLease>& lease, const HRESULT result) noexcept {
    if (!IsCurrentCallback(lease, false) ||
        state_.lifecycle != Lifecycle::EnvironmentCreating) return;
    environmentFaulted_ = true;
    Fault(L"environment-create", result);
}

void RichMediaSurfaceCoordinator::ResumeSharedEnvironmentWaiters() noexcept {
    auto waiters = std::move(sharedEnvironment_->waiters);
    sharedEnvironment_->waiters.clear();
    for (const auto& waiter : waiters) {
        auto* owner = waiter->owner.load(std::memory_order_acquire);
        if (!owner) continue;
        const HRESULT resume = owner->ResumeSharedEnvironment(waiter);
        if (FAILED(resume)) owner->FailSharedEnvironment(waiter, resume);
    }
}

void RichMediaSurfaceCoordinator::HoldSharedEnvironmentCreatingForTest() noexcept {
    sharedEnvironment_->lifecycle = EnvironmentLifecycle::Creating;
}

void RichMediaSurfaceCoordinator::ReleaseSharedEnvironmentReadyForTest() noexcept {
    sharedEnvironment_->lifecycle = EnvironmentLifecycle::Ready;
    ResumeSharedEnvironmentWaiters();
}

HRESULT RichMediaSurfaceCoordinator::CompleteSharedEnvironmentCreation(
    const RichMediaEnvironmentHandle& sharedEnvironment,
    const std::shared_ptr<CallbackLease>& initiatingLease,
    const HRESULT result,
    ICoreWebView2Environment* environment) noexcept {
    if (!sharedEnvironment || !initiatingLease ||
        sharedEnvironment->lifecycle != EnvironmentLifecycle::Creating ||
        sharedEnvironment->generation !=
            initiatingLease->authority.environmentGeneration)
        return S_FALSE;
    auto waiters = std::move(sharedEnvironment->waiters);
    sharedEnvironment->waiters.clear();
    if (FAILED(result) || !environment) {
        sharedEnvironment->faulted = true;
        sharedEnvironment->lifecycle = EnvironmentLifecycle::Cold;
        const HRESULT failure = FAILED(result) ? result : E_FAIL;
        for (const auto& waiter : waiters) {
            if (auto* owner = waiter->owner.load(std::memory_order_acquire))
                owner->FailSharedEnvironment(waiter, failure);
        }
        return S_OK;
    }
    sharedEnvironment->environment = environment;
    sharedEnvironment->lifecycle = EnvironmentLifecycle::Ready;
    sharedEnvironment->faulted = false;
    sharedEnvironment->browserExitNotified = false;
    sharedEnvironment->signal = std::make_shared<EnvironmentSignal>();
    if (SUCCEEDED(sharedEnvironment->environment.As(
            &sharedEnvironment->environment5))) {
        const std::weak_ptr<RichMediaEnvironment> weakEnvironment = sharedEnvironment;
        (void)sharedEnvironment->environment5->add_BrowserProcessExited(
            Callback<ICoreWebView2BrowserProcessExitedEventHandler>(
                [weakEnvironment](ICoreWebView2Environment*, ICoreWebView2BrowserProcessExitedEventArgs* args) {
                    const auto environmentHandle = weakEnvironment.lock();
                    if (!environmentHandle) return S_OK;
                    UINT32 browserProcessId{};
                    const bool browserProcessIdAvailable = args &&
                        SUCCEEDED(args->get_BrowserProcessId(&browserProcessId));
                    COREWEBVIEW2_BROWSER_PROCESS_EXIT_KIND exitKind{};
                    const bool exitKindAvailable = args &&
                        SUCCEEDED(args->get_BrowserProcessExitKind(&exitKind));
                    RecordBrowserProcessExit(
                        environmentHandle, browserProcessId,
                        browserProcessIdAvailable,
                        static_cast<std::uint32_t>(exitKind), exitKindAvailable);
                    return S_OK;
                }).Get(), &sharedEnvironment->browserProcessExitedToken);
    }
    for (const auto& waiter : waiters) {
        auto* owner = waiter->owner.load(std::memory_order_acquire);
        if (!owner) continue;
        const HRESULT resume = owner->ResumeSharedEnvironment(waiter);
        if (FAILED(resume)) owner->FailSharedEnvironment(waiter, resume);
    }
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::BeginController() noexcept {
    if (!environment_ || environmentLifecycle_ != EnvironmentLifecycle::Ready ||
        environmentFaulted_ || BrowserProcessExitObserved()) return E_UNEXPECTED;
    const auto lease = callbackLease_ ? callbackLease_ : CreateCallbackLease();
    state_.authority.controllerGeneration = ++nextControllerGeneration_;
    lease->authority.controllerGeneration = state_.authority.controllerGeneration;
    state_.lifecycle = Lifecycle::ControllerCreating;
    Emit(L"Rich media lifecycle=controller-creating");
    ComPtr<ICoreWebView2Environment3> compositionEnvironment;
    const HRESULT environment3 = environment_.As(&compositionEnvironment);
    if (FAILED(environment3)) {
        environmentFaulted_ = true;
        Fault(L"composition-environment", environment3);
        return S_OK;
    }
    lease->outstandingCreates.fetch_add(1, std::memory_order_relaxed);
    const HRESULT create = compositionEnvironment->CreateCoreWebView2CompositionController(
        configuration_.ownerWindow,
        Callback<ICoreWebView2CreateCoreWebView2CompositionControllerCompletedHandler>(
            [lease](HRESULT status, ICoreWebView2CompositionController* controller) {
                auto* owner = lease->owner.load(std::memory_order_acquire);
                const HRESULT callbackResult = owner
                    ? owner->OnControllerCreated(lease, status, controller) : S_FALSE;
                lease->outstandingCreates.fetch_sub(1, std::memory_order_release);
                return callbackResult;
            }).Get());
    if (FAILED(create)) {
        lease->outstandingCreates.fetch_sub(1, std::memory_order_release);
        Fault(L"controller-start", create);
    }
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::OnControllerCreated(
    const std::shared_ptr<CallbackLease>& lease, const HRESULT result,
    ICoreWebView2CompositionController* controller) noexcept {
    if (teardownRequested_ || !IsCurrentCallback(lease, false) ||
        state_.lifecycle != Lifecycle::ControllerCreating) return S_FALSE;
    if (FAILED(result) || !controller) { Fault(L"controller-create", result); return S_OK; }
    controller_ = controller;
    if (!ownsSharedController_) {
        ++sharedEnvironment_->liveControllerOwners;
        ownsSharedController_ = true;
    }
    if (FAILED(controller_.As(&controllerBase_)) || FAILED(controllerBase_->get_CoreWebView2(&core_))) {
        Fault(L"controller-interface", E_NOINTERFACE); return S_OK;
    }
    UINT32 browserProcessId{};
    if (environmentSignal_ && SUCCEEDED(core_->get_BrowserProcessId(&browserProcessId)))
        environmentSignal_->browserProcessId.store(browserProcessId, std::memory_order_release);
    HRESULT configured = controller_->put_RootVisualTarget(configuration_.compositionTarget.Get());
    if (SUCCEEDED(configured)) configured = UpdateGeometry(configuration_.bounds, configuration_.rasterScale);
    if (SUCCEEDED(configured)) configured = ConfigureCore();
    if (FAILED(configured)) { Fault(L"controller-configure", configured); return S_OK; }
    state_.lifecycle = Lifecycle::ReadyHidden;
    state_.inputEnabled = false;
    pageReady_ = false;
    (void)controllerBase_->put_IsVisible(FALSE);
    if (configuration_.setPresentationVisible) configuration_.setPresentationVisible(false);
    Emit(L"Rich media lifecycle=ready-hidden");
    if (configuration_.invalidate) configuration_.invalidate();
    // The host callbacks above run arbitrary reconciliation and can retire
    // this session, which resets core_ and the callback lease underneath this
    // frame. Re-establish that this coordinator still owns a live document
    // before navigating it.
    if (teardownRequested_ || teardownBegun_ ||
        !IsCurrentCallback(lease, false) || !core_) return S_OK;
    state_.authority.documentGeneration = ++nextDocumentGeneration_;
    lease->authority.documentGeneration = state_.authority.documentGeneration;
    pageUri_ = configuration_.origin.empty()
        ? std::wstring{kPageUri}
        : configuration_.origin + L"/" + configuration_.entryAsset;
    configured = core_->Navigate(pageUri_.c_str());
    if (FAILED(configured)) Fault(L"navigate", configured);
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::ConfigureCore() noexcept {
    const auto lease = callbackLease_;
    ComPtr<ICoreWebView2Settings> settings;
    HRESULT result = core_->get_Settings(&settings);
    if (SUCCEEDED(result)) result = settings->put_AreDefaultContextMenusEnabled(FALSE);
    if (SUCCEEDED(result)) result = settings->put_AreDevToolsEnabled(FALSE);
    if (SUCCEEDED(result)) result = settings->put_IsStatusBarEnabled(FALSE);
    if (SUCCEEDED(result)) result = settings->put_IsZoomControlEnabled(FALSE);
    if (SUCCEEDED(result)) result = settings->put_IsBuiltInErrorPageEnabled(FALSE);
    if (SUCCEEDED(result)) result = settings->put_AreDefaultScriptDialogsEnabled(FALSE);
    if (SUCCEEDED(result)) result = settings->put_IsWebMessageEnabled(TRUE);
    ComPtr<ICoreWebView2_8> core8;
    if (SUCCEEDED(result)) result = core_.As(&core8);
    if (SUCCEEDED(result)) result = core8->put_IsMuted(FALSE);
    ComPtr<ICoreWebView2Settings3> settings3;
    if (SUCCEEDED(result) && SUCCEEDED(settings.As(&settings3)))
        result = settings3->put_AreBrowserAcceleratorKeysEnabled(FALSE);
    if (FAILED(result)) return result;
    result = core_->AddWebResourceRequestedFilter(
        kResourceFilter, COREWEBVIEW2_WEB_RESOURCE_CONTEXT_ALL);
    if (SUCCEEDED(result)) result = core_->add_WebResourceRequested(
        Callback<ICoreWebView2WebResourceRequestedEventHandler>(
            [lease](ICoreWebView2*, ICoreWebView2WebResourceRequestedEventArgs* args) {
                auto* owner = lease->owner.load(std::memory_order_acquire);
                return owner && owner->IsCurrentCallback(lease, true)
                    ? owner->ServeResource(args) : S_FALSE;
            }).Get(), &webResourceRequestedToken_);
    if (SUCCEEDED(result)) result = core_->add_NavigationStarting(
        Callback<ICoreWebView2NavigationStartingEventHandler>(
            [lease](ICoreWebView2*, ICoreWebView2NavigationStartingEventArgs* args) {
                auto* owner = lease->owner.load(std::memory_order_acquire);
                return owner && owner->IsCurrentCallback(lease, true)
                    ? owner->OnNavigationStarting(args) : S_FALSE;
            }).Get(), &navigationStartingToken_);
    if (SUCCEEDED(result)) result = core_->add_NavigationCompleted(
        Callback<ICoreWebView2NavigationCompletedEventHandler>(
            [lease](ICoreWebView2*, ICoreWebView2NavigationCompletedEventArgs* args) {
                auto* owner = lease->owner.load(std::memory_order_acquire);
                return owner && owner->IsCurrentCallback(lease, true)
                    ? owner->OnNavigationCompleted(args) : S_FALSE;
            }).Get(), &navigationCompletedToken_);
    if (SUCCEEDED(result)) result = core_->add_WebMessageReceived(
        Callback<ICoreWebView2WebMessageReceivedEventHandler>(
            [lease](ICoreWebView2*, ICoreWebView2WebMessageReceivedEventArgs* args) {
                auto* owner = lease->owner.load(std::memory_order_acquire);
                return owner && owner->IsCurrentCallback(lease, true)
                    ? owner->OnWebMessage(args) : S_FALSE;
            }).Get(), &webMessageReceivedToken_);
    if (SUCCEEDED(result)) result = core_->add_ProcessFailed(
        Callback<ICoreWebView2ProcessFailedEventHandler>(
            [lease](ICoreWebView2*, ICoreWebView2ProcessFailedEventArgs* args) {
                auto* owner = lease->owner.load(std::memory_order_acquire);
                return owner && owner->IsCurrentCallback(lease, true)
                    ? owner->OnProcessFailed(args) : S_FALSE;
            }).Get(), &processFailedToken_);
    if (SUCCEEDED(result)) result = core_->add_NewWindowRequested(
        Callback<ICoreWebView2NewWindowRequestedEventHandler>(
            [](ICoreWebView2*, ICoreWebView2NewWindowRequestedEventArgs* args) {
                return args->put_Handled(TRUE);
            }).Get(), &newWindowRequestedToken_);
    if (SUCCEEDED(result)) result = core_->add_PermissionRequested(
        Callback<ICoreWebView2PermissionRequestedEventHandler>(
            [](ICoreWebView2*, ICoreWebView2PermissionRequestedEventArgs* args) {
                return args->put_State(COREWEBVIEW2_PERMISSION_STATE_DENY);
            }).Get(), &permissionRequestedToken_);
    ComPtr<ICoreWebView2_4> core4;
    if (SUCCEEDED(result) && SUCCEEDED(core_.As(&core4))) {
        result = core4->add_FrameCreated(
            Callback<ICoreWebView2FrameCreatedEventHandler>(
                [lease](ICoreWebView2*, ICoreWebView2FrameCreatedEventArgs* args) {
                    auto* owner = lease->owner.load(std::memory_order_acquire);
                    return owner && owner->IsCurrentCallback(lease, true)
                        ? owner->OnFrameCreated(args) : S_FALSE;
                }).Get(), &frameCreatedToken_);
    }
    if (SUCCEEDED(result) && core4)
        result = core4->add_DownloadStarting(
            Callback<ICoreWebView2DownloadStartingEventHandler>(
                [](ICoreWebView2*, ICoreWebView2DownloadStartingEventArgs* args) {
                    HRESULT cancel = args->put_Cancel(TRUE);
                    return SUCCEEDED(cancel) ? args->put_Handled(TRUE) : cancel;
                }).Get(), &downloadStartingToken_);
    ComPtr<ICoreWebView2_10> core10;
    if (SUCCEEDED(result) && SUCCEEDED(core_.As(&core10)))
        result = core10->add_BasicAuthenticationRequested(
            Callback<ICoreWebView2BasicAuthenticationRequestedEventHandler>(
                [](ICoreWebView2*, ICoreWebView2BasicAuthenticationRequestedEventArgs* args) {
                    return args->put_Cancel(TRUE);
                }).Get(), &basicAuthenticationToken_);
    ComPtr<ICoreWebView2_14> core14;
    if (SUCCEEDED(result) && SUCCEEDED(core_.As(&core14)))
        result = core14->add_ServerCertificateErrorDetected(
            Callback<ICoreWebView2ServerCertificateErrorDetectedEventHandler>(
                [](ICoreWebView2*, ICoreWebView2ServerCertificateErrorDetectedEventArgs* args) {
                    return args->put_Action(
                        COREWEBVIEW2_SERVER_CERTIFICATE_ERROR_ACTION_CANCEL);
                }).Get(), &serverCertificateToken_);
    ComPtr<ICoreWebView2_18> core18;
    if (SUCCEEDED(result) && SUCCEEDED(core_.As(&core18)))
        result = core18->add_LaunchingExternalUriScheme(
            Callback<ICoreWebView2LaunchingExternalUriSchemeEventHandler>(
                [](ICoreWebView2*, ICoreWebView2LaunchingExternalUriSchemeEventArgs* args) {
                    return args->put_Cancel(TRUE);
                }).Get(), &externalUriToken_);
    ComPtr<ICoreWebView2_13> core13;
    ComPtr<ICoreWebView2Profile> profile;
    ComPtr<ICoreWebView2Profile6> profile6;
    if (SUCCEEDED(result) && SUCCEEDED(core_.As(&core13)) &&
        SUCCEEDED(core13->get_Profile(&profile)) &&
        SUCCEEDED(profile.As(&profile6))) {
        result = profile6->put_IsPasswordAutosaveEnabled(FALSE);
        if (SUCCEEDED(result))
            result = profile6->put_IsGeneralAutofillEnabled(FALSE);
    }
    return result;
}

bool RichMediaSurfaceCoordinator::IsAllowedNavigation(
    const std::wstring_view uri) noexcept {
    return uri == kPageUri;
}

bool RichMediaSurfaceCoordinator::IsAllowedNavigation(
    const std::wstring_view uri, const std::wstring_view exactPageUri) noexcept {
    return !exactPageUri.empty() && uri == exactPageUri;
}

bool RichMediaSurfaceCoordinator::IsAllowedMessageSource(
    const std::wstring_view source, const std::wstring_view exactPageUri) noexcept {
    return !exactPageUri.empty() && source == exactPageUri;
}

bool RichMediaSurfaceCoordinator::IsCanonicalHttpsOrigin(
    const std::wstring_view origin) noexcept {
    return CanonicalHttpsOrigin(origin);
}

bool RichMediaSurfaceCoordinator::IsAllowedFrameResource(
    const std::wstring_view uri,
    const std::vector<std::wstring>& allowedOrigins,
    const std::vector<std::wstring>& allowedFamilies) noexcept {
    const bool exact = std::any_of(allowedOrigins.begin(), allowedOrigins.end(),
        [&](const std::wstring& origin) {
            if (!IsCanonicalHttpsOrigin(origin) || !uri.starts_with(origin))
                return false;
            return uri.size() == origin.size() ||
                (uri.size() > origin.size() &&
                    (uri[origin.size()] == L'/' || uri[origin.size()] == L'?' ||
                     uri[origin.size()] == L'#'));
        });
    if (exact) return true;
    static const auto suffixAuthority = PublicSuffixDomainAuthority::LoadDefault();
    return suffixAuthority.AllowsHttpsUri(uri, allowedFamilies);
}

bool RichMediaSurfaceCoordinator::IsAllowedFrameNavigation(
    const std::wstring_view uri,
    const std::vector<std::wstring>& allowedOrigins) noexcept {
    return IsAllowedFrameResource(uri, allowedOrigins);
}

bool RichMediaSurfaceCoordinator::IsPlaybackCommandCorrelated(
    const std::uint64_t commandSequence, const std::wstring_view mediaKey,
    const std::optional<std::uint64_t> pendingSequence,
    const std::wstring_view pendingMediaKey) noexcept {
    return commandSequence == 0 ||
        (pendingSequence && commandSequence == *pendingSequence &&
            mediaKey == pendingMediaKey);
}

RichMediaSurfaceCoordinator::SealedResourceResponsePlan
RichMediaSurfaceCoordinator::PlanSealedResourceResponse(
    std::wstring_view rangeHeader, const std::wstring_view contentType,
    const std::size_t resourceLength, const bool rangeEligible) noexcept {
    SealedResourceResponsePlan plan;
    plan.length = resourceLength;
    const auto commonHeaders = [&] {
        std::wstring headers = L"Content-Type: " + std::wstring{contentType} +
            L"\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff";
        if (rangeEligible) headers += L"\r\nAccept-Ranges: bytes";
        return headers;
    };
    if (!rangeEligible || rangeHeader.empty()) {
        plan.headers = commonHeaders() + L"\r\nContent-Length: " +
            std::to_wstring(resourceLength);
        return plan;
    }
    const auto reject = [&] {
        plan.status = 416;
        plan.offset = 0;
        plan.length = 0;
        plan.headers = commonHeaders() + L"\r\nContent-Range: bytes */" +
            std::to_wstring(resourceLength) + L"\r\nContent-Length: 0";
        return plan;
    };
    if (!rangeHeader.starts_with(L"bytes=") || resourceLength == 0)
        return reject();
    std::wstring_view range = rangeHeader.substr(6);
    while (!range.empty() && (range.front() == L' ' || range.front() == L'\t'))
        range.remove_prefix(1);
    while (!range.empty() && (range.back() == L' ' || range.back() == L'\t'))
        range.remove_suffix(1);
    if (range.empty() || range.find(L',') != std::wstring_view::npos)
        return reject();
    const auto hyphen = range.find(L'-');
    if (hyphen == std::wstring_view::npos ||
        range.find(L'-', hyphen + 1) != std::wstring_view::npos)
        return reject();
    const auto parse = [](const std::wstring_view text,
                          std::size_t& value) noexcept {
        if (text.empty()) return false;
        value = 0;
        for (const wchar_t character : text) {
            if (character < L'0' || character > L'9') return false;
            const auto digit = static_cast<std::size_t>(character - L'0');
            if (value > (SIZE_MAX - digit) / 10) return false;
            value = value * 10 + digit;
        }
        return true;
    };
    const auto first = range.substr(0, hyphen);
    const auto last = range.substr(hyphen + 1);
    std::size_t start{};
    std::size_t end{};
    if (first.empty()) {
        std::size_t suffix{};
        if (!parse(last, suffix) || suffix == 0 || suffix > resourceLength)
            return reject();
        start = resourceLength - suffix;
        end = resourceLength - 1;
    } else {
        if (!parse(first, start) || start >= resourceLength) return reject();
        if (last.empty()) {
            end = resourceLength - 1;
        } else if (!parse(last, end) || end < start || end >= resourceLength) {
            return reject();
        }
    }
    plan.status = 206;
    plan.offset = start;
    plan.length = end - start + 1;
    plan.headers = commonHeaders() + L"\r\nContent-Range: bytes " +
        std::to_wstring(start) + L"-" + std::to_wstring(end) + L"/" +
        std::to_wstring(resourceLength) + L"\r\nContent-Length: " +
        std::to_wstring(plan.length);
    return plan;
}

bool RichMediaSurfaceCoordinator::IsValidAdapterConfiguration(
    const Configuration& configuration) noexcept {
    return IsValidPublicAdapterConfiguration(configuration);
}

std::optional<std::wstring>
RichMediaSurfaceCoordinator::ManifestAssemblyIdentity(
    const std::wstring_view manifest) noexcept {
    constexpr std::wstring_view elementName{L"assemblyIdentity"};
    const auto element = manifest.find(L"<assemblyIdentity");
    if (element == std::wstring_view::npos) return std::nullopt;
    const auto nameEnd = element + 1 + elementName.size();
    if (nameEnd >= manifest.size() ||
        (manifest[nameEnd] != L'>' && manifest[nameEnd] != L'/' &&
         manifest[nameEnd] != L' ' && manifest[nameEnd] != L'\t' &&
         manifest[nameEnd] != L'\r' && manifest[nameEnd] != L'\n'))
        return std::nullopt;
    const auto elementEnd = manifest.find(L'>', nameEnd);
    if (elementEnd == std::wstring_view::npos) return std::nullopt;
    std::optional<std::wstring> identity;
    auto cursor = nameEnd;
    while (cursor < elementEnd) {
        while (cursor < elementEnd &&
               (manifest[cursor] == L' ' || manifest[cursor] == L'\t' ||
                manifest[cursor] == L'\r' || manifest[cursor] == L'\n' ||
                manifest[cursor] == L'/')) ++cursor;
        if (cursor >= elementEnd) break;
        const auto attributeStart = cursor;
        while (cursor < elementEnd &&
               ((manifest[cursor] >= L'a' && manifest[cursor] <= L'z') ||
                (manifest[cursor] >= L'A' && manifest[cursor] <= L'Z') ||
                (manifest[cursor] >= L'0' && manifest[cursor] <= L'9') ||
                manifest[cursor] == L':' || manifest[cursor] == L'_' ||
                manifest[cursor] == L'-')) ++cursor;
        if (cursor == attributeStart) return std::nullopt;
        const auto attribute = manifest.substr(attributeStart, cursor - attributeStart);
        while (cursor < elementEnd &&
               (manifest[cursor] == L' ' || manifest[cursor] == L'\t' ||
                manifest[cursor] == L'\r' || manifest[cursor] == L'\n')) ++cursor;
        if (cursor >= elementEnd || manifest[cursor++] != L'=') return std::nullopt;
        while (cursor < elementEnd &&
               (manifest[cursor] == L' ' || manifest[cursor] == L'\t' ||
                manifest[cursor] == L'\r' || manifest[cursor] == L'\n')) ++cursor;
        if (cursor >= elementEnd ||
            (manifest[cursor] != L'\'' && manifest[cursor] != L'\"'))
            return std::nullopt;
        const wchar_t quote = manifest[cursor++];
        const auto valueStart = cursor;
        const auto valueEnd = manifest.find(quote, valueStart);
        if (valueEnd == std::wstring_view::npos || valueEnd > elementEnd)
            return std::nullopt;
        if (attribute == L"name") {
            if (identity || valueEnd == valueStart) return std::nullopt;
            identity = std::wstring{manifest.substr(valueStart, valueEnd - valueStart)};
        }
        cursor = valueEnd + 1;
    }
    return identity;
}

std::optional<std::wstring>
RichMediaSurfaceCoordinator::FormatInstalledAppReferer(
    const std::wstring_view identity) noexcept {
    if (identity.empty() || identity.size() > 253 || identity.front() == L'.' ||
        identity.back() == L'.') return std::nullopt;
    std::wstring canonical;
    canonical.reserve(identity.size());
    std::size_t labelLength{};
    bool labelStartsWithHyphen{};
    for (const wchar_t character : identity) {
        if (character == L'.') {
            if (labelLength == 0 || labelLength > 63 || labelStartsWithHyphen ||
                canonical.back() == L'-') return std::nullopt;
            canonical.push_back(L'.');
            labelLength = 0;
            labelStartsWithHyphen = false;
            continue;
        }
        wchar_t lowered = character;
        if (lowered >= L'A' && lowered <= L'Z') lowered += L'a' - L'A';
        if (!((lowered >= L'a' && lowered <= L'z') ||
              (lowered >= L'0' && lowered <= L'9') || lowered == L'-'))
            return std::nullopt;
        if (labelLength == 0) labelStartsWithHyphen = lowered == L'-';
        ++labelLength;
        canonical.push_back(lowered);
    }
    if (labelLength == 0 || labelLength > 63 || labelStartsWithHyphen ||
        canonical.back() == L'-') return std::nullopt;
    return L"https://" + canonical + L"/";
}

std::optional<std::wstring>
RichMediaSurfaceCoordinator::SelectInstalledAppReferer(
    const bool packaged, const std::wstring_view packageIdentity,
    const std::wstring_view manifestIdentity) noexcept {
    return FormatInstalledAppReferer(packaged ? packageIdentity : manifestIdentity);
}

std::optional<std::wstring>
RichMediaSurfaceCoordinator::ResolveInstalledAppReferer() noexcept {
    UINT32 packageBufferLength{};
    const LONG packageProbe = GetCurrentPackageId(&packageBufferLength, nullptr);
    if (packageProbe == ERROR_INSUFFICIENT_BUFFER && packageBufferLength > 0) {
        std::vector<std::uint8_t> packageBuffer(packageBufferLength);
        auto* package = reinterpret_cast<PACKAGE_ID*>(packageBuffer.data());
        if (GetCurrentPackageId(&packageBufferLength, packageBuffer.data()) != ERROR_SUCCESS ||
            !package->name) return std::nullopt;
        return SelectInstalledAppReferer(true, package->name, {});
    }
    if (packageProbe != APPMODEL_ERROR_NO_PACKAGE) return std::nullopt;

    const HMODULE module = GetModuleHandleW(nullptr);
    const HRSRC resource = module
        ? FindResourceW(module, MAKEINTRESOURCEW(1), RT_MANIFEST) : nullptr;
    if (!resource) return std::nullopt;
    const DWORD byteCount = SizeofResource(module, resource);
    if (byteCount == 0 || byteCount > 64 * 1024) return std::nullopt;
    const HGLOBAL loaded = LoadResource(module, resource);
    const auto* bytes = loaded
        ? static_cast<const char*>(LockResource(loaded)) : nullptr;
    if (!bytes) return std::nullopt;
    const int characterCount = MultiByteToWideChar(
        CP_UTF8, MB_ERR_INVALID_CHARS, bytes, static_cast<int>(byteCount), nullptr, 0);
    if (characterCount <= 0) return std::nullopt;
    std::wstring manifest(static_cast<std::size_t>(characterCount), L'\0');
    if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes,
            static_cast<int>(byteCount), manifest.data(), characterCount) != characterCount)
        return std::nullopt;
    const auto identity = ManifestAssemblyIdentity(manifest);
    return identity ? SelectInstalledAppReferer(false, {}, *identity) : std::nullopt;
}

RichMediaSurfaceCoordinator::InstalledAppRefererResult
RichMediaSurfaceCoordinator::ApplyInstalledAppReferer(
    const std::wstring_view uri,
    const std::vector<std::wstring>& allowedOrigins,
    const std::wstring_view referer,
    const std::function<HRESULT(const wchar_t*, const wchar_t*)>& setHeader,
    const std::vector<std::wstring>& allowedFamilies) noexcept {
    if (!IsAllowedFrameResource(uri, allowedOrigins, allowedFamilies))
        return InstalledAppRefererResult::NotApplicable;
    if (referer.empty() || !setHeader ||
        !CanonicalHttpsOrigin(referer.ends_with(L'/')
            ? referer.substr(0, referer.size() - 1) : std::wstring_view{}))
        return InstalledAppRefererResult::Rejected;
    const std::wstring value{referer};
    return SUCCEEDED(setHeader(L"Referer", value.c_str()))
        ? InstalledAppRefererResult::Applied
        : InstalledAppRefererResult::Rejected;
}

HRESULT RichMediaSurfaceCoordinator::ServeResource(
    ICoreWebView2WebResourceRequestedEventArgs* args) noexcept {
    ComPtr<ICoreWebView2WebResourceRequest> request;
    LPWSTR rawUri{};
    HRESULT result = args->get_Request(&request);
    if (SUCCEEDED(result)) result = request->get_Uri(&rawUri);
    const std::wstring uri = rawUri ? rawUri : L"";
    CoTaskMemFree(rawUri);
    std::wstring rangeHeader;
    ComPtr<ICoreWebView2HttpRequestHeaders> requestHeaders;
    if (SUCCEEDED(result) && SUCCEEDED(request->get_Headers(&requestHeaders))) {
        LPWSTR rawRange{};
        if (SUCCEEDED(requestHeaders->GetHeader(L"Range", &rawRange)) && rawRange)
            rangeHeader.assign(rawRange);
        CoTaskMemFree(rawRange);
    }
    COREWEBVIEW2_WEB_RESOURCE_CONTEXT resourceContext{};
    const HRESULT contextResult = args->get_ResourceContext(&resourceContext);
    COREWEBVIEW2_WEB_RESOURCE_REQUEST_SOURCE_KINDS sourceKind{};
    ComPtr<ICoreWebView2WebResourceRequestedEventArgs2> args2;
    HRESULT sourceResult = args->QueryInterface(IID_PPV_ARGS(&args2));
    if (SUCCEEDED(sourceResult)) sourceResult = args2->get_RequestedSourceKind(&sourceKind);
    if (SUCCEEDED(result)) {
        const auto refererResult = ApplyInstalledAppReferer(
            uri, configuration_.allowedFrameOrigins, installedAppReferer_,
            requestHeaders
                ? std::function<HRESULT(const wchar_t*, const wchar_t*)>{
                    [&](const wchar_t* name, const wchar_t* value) {
                        return requestHeaders->SetHeader(name, value);
                    }}
                : std::function<HRESULT(const wchar_t*, const wchar_t*)>{},
            configuration_.allowedFrameDomainFamilies);
        if (refererResult == InstalledAppRefererResult::Applied) {
            Emit(L"Rich media declared frame resource continued with host referer");
            return S_OK;
        }
        if (refererResult == InstalledAppRefererResult::Rejected)
            Emit(L"Rich media declared frame resource denied: host referer unavailable");
    }
    ComPtr<IStream> stream;
    const wchar_t* contentType{};
    bool matchedEntry{};
    bool matchedAsset{};
    int responseStatus{200};
    std::wstring responseReason{L"OK"};
    std::wstring_view matchedMime{L"<none>"};
    if (configuration_.resources.empty() && uri == kPageUri) {
        stream = StreamFor(kPage, sizeof(kPage) - 1);
        contentType = L"Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store";
    } else if (configuration_.resources.empty() &&
               uri == std::wstring{kOrigin} + L"/tone.wav") {
        const auto wave = WaveBytes();
        stream = StreamFor(wave.data(), wave.size());
        contentType = L"Content-Type: audio/wav\r\nCache-Control: no-store";
    } else {
        const auto prefix = configuration_.origin + L"/";
        const auto found = !prefix.empty() && uri.starts_with(prefix)
            ? std::find_if(
                configuration_.resources.begin(), configuration_.resources.end(),
                [&](const Configuration::Resource& resource) {
                    return uri == prefix + resource.path;
                })
            : configuration_.resources.end();
        if (found != configuration_.resources.end()) {
            matchedEntry = found->path == configuration_.entryAsset;
            matchedAsset = true;
            matchedMime = std::wstring_view{found->contentType}.substr(0, 64);
            const auto plan = PlanSealedResourceResponse(
                rangeHeader, found->contentType, found->content.size(),
                IsSealedMediaType(found->contentType));
            responseStatus = plan.status;
            responseReason = responseStatus == 206 ? L"Partial Content" :
                responseStatus == 416 ? L"Range Not Satisfiable" : L"OK";
            if (responseStatus != 416)
                stream = StreamFor(found->content.data() + plan.offset, plan.length);
            static thread_local std::wstring headers;
            headers = plan.headers;
            contentType = headers.c_str();
        } else {
            Emit(L"Rich media resource denied");
            contentType = L"Content-Type: text/plain\r\nCache-Control: no-store";
        }
    }
    if (!configuration_.resources.empty()) {
        Emit(std::format(
            L"Rich media load resource origin={} path={} context={} source={} "
            L"environment={} controller={} document={} entry={} lookup={} mime={} status={}",
            configuration_.origin, BoundedSyntheticPath(configuration_.origin, uri),
            SUCCEEDED(contextResult) ? static_cast<int>(resourceContext) : -1,
            SUCCEEDED(sourceResult) ? static_cast<unsigned int>(sourceKind) : 0,
            state_.authority.environmentGeneration,
            state_.authority.controllerGeneration,
            state_.authority.documentGeneration,
            matchedEntry ? L"yes" : L"no", matchedAsset ? L"matched" : L"missing",
            matchedMime, matchedAsset ? responseStatus : 404));
    }
    ComPtr<ICoreWebView2WebResourceResponse> response;
    result = environment_->CreateWebResourceResponse(
        stream.Get(), matchedAsset ? responseStatus : (stream ? 200 : 404),
        matchedAsset ? responseReason.c_str() : (stream ? L"OK" : L"Not Found"),
        contentType, &response);
    if (SUCCEEDED(result)) result = args->put_Response(response.Get());
    return result;
}

HRESULT RichMediaSurfaceCoordinator::OnNavigationStarting(
    ICoreWebView2NavigationStartingEventArgs* args) noexcept {
    if (state_.lifecycle == Lifecycle::Faulted ||
        state_.lifecycle == Lifecycle::Closing) return S_FALSE;
    LPWSTR rawUri{};
    HRESULT result = args->get_Uri(&rawUri);
    const std::wstring uri = rawUri ? rawUri : L"";
    CoTaskMemFree(rawUri);
    if (FAILED(result)) return result;
    if (!configuration_.resources.empty()) {
        Emit(std::format(
            L"Rich media load milestone=navigation-start origin={} path={} controller={} document={}",
            configuration_.origin, BoundedSyntheticPath(configuration_.origin, uri),
            state_.authority.controllerGeneration,
            state_.authority.documentGeneration));
    }
    if (!IsAllowedNavigation(uri, pageUri_)) {
        Emit(L"Rich media navigation denied");
        return args->put_Cancel(TRUE);
    }
    std::uint64_t navigationId{};
    if (FAILED(result = args->get_NavigationId(&navigationId))) return result;
    pendingNavigationId_ = navigationId;
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::OnFrameCreated(
    ICoreWebView2FrameCreatedEventArgs* args) noexcept {
    if (state_.lifecycle == Lifecycle::Faulted ||
        state_.lifecycle == Lifecycle::Closing) return S_FALSE;
    ComPtr<ICoreWebView2Frame> frame;
    HRESULT result = args ? args->get_Frame(&frame) : E_POINTER;
    ComPtr<ICoreWebView2Frame2> frame2;
    if (SUCCEEDED(result)) result = frame.As(&frame2);
    if (FAILED(result) || !frame2) return FAILED(result) ? result : E_NOINTERFACE;
    if (frameSubscriptions_.size() >= 16) {
        Fault(L"frame-count", E_BOUNDS);
        return S_OK;
    }
    const auto lease = callbackLease_;
    FrameSubscription subscription;
    subscription.frame = frame2;
    result = frame2->add_NavigationStarting(
        Callback<ICoreWebView2FrameNavigationStartingEventHandler>(
            [lease](ICoreWebView2Frame*, ICoreWebView2NavigationStartingEventArgs* eventArgs) {
                auto* owner = lease->owner.load(std::memory_order_acquire);
                return owner && owner->IsCurrentCallback(lease, true)
                    ? owner->OnFrameNavigationStarting(eventArgs) : S_FALSE;
            }).Get(), &subscription.navigationStartingToken);
    if (SUCCEEDED(result)) frameSubscriptions_.push_back(std::move(subscription));
    return result;
}

HRESULT RichMediaSurfaceCoordinator::OnFrameNavigationStarting(
    ICoreWebView2NavigationStartingEventArgs* args) noexcept {
    if (state_.lifecycle == Lifecycle::Faulted ||
        state_.lifecycle == Lifecycle::Closing) return S_FALSE;
    LPWSTR rawUri{};
    const HRESULT result = args ? args->get_Uri(&rawUri) : E_POINTER;
    const std::wstring uri = rawUri ? rawUri : L"";
    CoTaskMemFree(rawUri);
    if (FAILED(result)) return result;
    if (IsAllowedNavigation(uri, pageUri_)) return S_OK;
    if (IsAllowedFrameNavigation(uri, configuration_.allowedFrameOrigins))
        return S_OK;
    Emit(L"Rich media frame navigation denied");
    return args->put_Cancel(TRUE);
}

HRESULT RichMediaSurfaceCoordinator::OnNavigationCompleted(
    ICoreWebView2NavigationCompletedEventArgs* args) noexcept {
    if (state_.lifecycle == Lifecycle::Faulted ||
        state_.lifecycle == Lifecycle::Closing) return S_FALSE;
    BOOL success{};
    HRESULT result = args->get_IsSuccess(&success);
    std::uint64_t navigationId{};
    if (SUCCEEDED(result)) result = args->get_NavigationId(&navigationId);
    if (SUCCEEDED(result) && navigationId != pendingNavigationId_) {
        Fault(L"navigation-generation", E_ACCESSDENIED);
        return S_OK;
    }
    if (FAILED(result) || !success) {
        Fault(L"navigation-complete", FAILED(result) ? result : E_FAIL);
        return S_OK;
    }
    if (!configuration_.resources.empty()) {
        Emit(std::format(
            L"Rich media load milestone=navigation-complete controller={} document={} navigation={}",
            state_.authority.controllerGeneration,
            state_.authority.documentGeneration, navigationId));
    }
    const auto commandId = ++nextCommandId_;
    pendingCommand_ = PendingCommand{
        commandId, Command::Activate, 0, {}, PendingPhase::AwaitingEvent};
    const std::wstring command = std::format(
        L"{{\"command\":\"initialize\",\"environmentGeneration\":{},\"surfaceGeneration\":{},\"sessionGeneration\":{},\"controllerGeneration\":{},\"documentGeneration\":{},\"commandId\":{}}}",
        state_.authority.environmentGeneration, state_.authority.surfaceGeneration,
        state_.authority.sessionGeneration,
        state_.authority.controllerGeneration, state_.authority.documentGeneration,
        commandId);
    result = core_->PostWebMessageAsJson(command.c_str());
    if (SUCCEEDED(result) && !configuration_.resources.empty()) {
        Emit(std::format(
            L"Rich media load milestone=initialize-sent controller={} document={} command={}",
            state_.authority.controllerGeneration,
            state_.authority.documentGeneration, commandId));
    }
    if (FAILED(result)) Fault(L"initialize-command", result);
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::OnWebMessage(
    ICoreWebView2WebMessageReceivedEventArgs* args) noexcept {
    if (state_.lifecycle == Lifecycle::Faulted ||
        state_.lifecycle == Lifecycle::Closing) return S_FALSE;
    LPWSTR rawSource{};
    LPWSTR rawJson{};
    HRESULT result = args->get_Source(&rawSource);
    if (SUCCEEDED(result)) result = args->get_WebMessageAsJson(&rawJson);
    const std::wstring source = rawSource ? rawSource : L"";
    const std::wstring json = rawJson ? rawJson : L"";
    CoTaskMemFree(rawSource); CoTaskMemFree(rawJson);
    if (!configuration_.resources.empty()) {
        Emit(std::format(
            L"Rich media load milestone=page-event-received source={} controller={} document={} bytes={}",
            IsAllowedMessageSource(source, pageUri_) ? L"exact" : L"rejected",
            state_.authority.controllerGeneration,
            state_.authority.documentGeneration, json.size()));
    }
    if (FAILED(result) || !IsAllowedMessageSource(source, pageUri_) ||
        json.size() > kMaximumMessageCharacters) {
        Fault(L"message-envelope", FAILED(result) ? result : E_ACCESSDENIED); return S_OK;
    }
    State next = state_;
    const std::optional<std::uint64_t> pendingId = pendingCommand_
        ? std::optional<std::uint64_t>{pendingCommand_->id} : std::nullopt;
    if (!ValidatePageEvent(json, state_.authority, state_.authority.eventSequence,
                           pendingId, next)) {
        Fault(L"message-authority", E_ACCESSDENIED); return S_OK;
    }
    const auto type = winrt::Windows::Data::Json::JsonObject::Parse(json)
                          .GetNamedString(L"type");
    if (type == L"armed") {
        state_ = std::move(next);
        if (!pendingCommand_ || pendingCommand_->command != Command::Activate ||
            pendingCommand_->phase != PendingPhase::AwaitingEvent) {
            Fault(L"spatial-activation-authority", E_ACCESSDENIED);
            return S_OK;
        }
        pendingCommand_->phase = PendingPhase::AwaitingSpatialActivation;
        const bool activationSent = SendFocusedSpatialActivation();
        Emit(std::format(
            L"Rich media spatial activation command={} sequence={} page-event={} result={}",
            pendingCommand_->id, pendingCommand_->playbackSequence,
            state_.authority.eventSequence,
            activationSent ? L"sent" : L"refused"));
        if (!activationSent)
            Fault(L"spatial-activation-input", E_FAIL);
        return S_OK;
    }
    std::optional<PlaybackEvent> playbackEvent;
    if (type == L"ready" || type == L"media") {
        try {
            const auto message = winrt::Windows::Data::Json::JsonObject::Parse(json);
            if (message.HasKey(L"mediaKey") && message.HasKey(L"positionSeconds") &&
                message.HasKey(L"durationSeconds") && message.HasKey(L"volume") &&
                message.HasKey(L"playbackState")) {
                const bool hasPreferences = message.HasKey(L"playbackRate");
                playbackEvent = PlaybackEvent{
                    next.authority.eventSequence,
                    static_cast<std::uint64_t>(
                        message.GetNamedNumber(L"commandSequence")),
                    std::wstring(std::wstring_view(message.GetNamedString(L"mediaKey"))),
                    std::wstring(std::wstring_view(message.GetNamedString(L"playbackState"))),
                    message.GetNamedNumber(L"positionSeconds"),
                    message.GetNamedNumber(L"durationSeconds"),
                    message.GetNamedNumber(L"volume"),
                    message.HasKey(L"errorCode")
                        ? std::wstring(std::wstring_view(message.GetNamedString(L"errorCode")))
                        : std::wstring{},
                    hasPreferences ? message.GetNamedNumber(L"playbackRate") : 1.0,
                    hasPreferences ? message.GetNamedBoolean(L"muted") : false,
                    hasPreferences ? message.GetNamedBoolean(L"loop") : false,
                    hasPreferences};
            }
        } catch (...) {
            Fault(L"message-playback-state", E_ACCESSDENIED);
            return S_OK;
        }
    }
    const bool unsolicitedObservation = next.lastAcknowledgedCommandId == 0;
    const bool playbackCorrelated = !playbackEvent ||
        (unsolicitedObservation
            ? playbackEvent->commandSequence == 0 &&
                !state_.mediaKey.empty() &&
                playbackEvent->mediaKey == state_.mediaKey
            : pendingCommand_ &&
                (pendingCommand_->playbackSequence == 0
                    ? playbackEvent->commandSequence == 0
                    : IsPlaybackCommandCorrelated(
                        playbackEvent->commandSequence,
                        playbackEvent->mediaKey,
                        pendingCommand_->playbackSequence,
                        pendingCommand_->mediaKey)));
    if (!playbackCorrelated) {
        Fault(L"message-playback-correlation", E_ACCESSDENIED);
        return S_OK;
    }
    if (playbackEvent && pendingCommand_ &&
        playbackEvent->commandSequence == pendingCommand_->playbackSequence &&
        pendingCommand_->playbackKind &&
        (*pendingCommand_->playbackKind == PlaybackCommandKind::SetPlaybackRate ||
         *pendingCommand_->playbackKind == PlaybackCommandKind::SetMuted ||
         *pendingCommand_->playbackKind == PlaybackCommandKind::SetLoop)) {
        const bool failed = !playbackEvent->errorCode.empty();
        const bool applied = playbackEvent->hasPlaybackPreferences &&
            (*pendingCommand_->playbackKind != PlaybackCommandKind::SetPlaybackRate ||
             (pendingCommand_->expectedPlaybackRate &&
              playbackEvent->playbackRate == *pendingCommand_->expectedPlaybackRate)) &&
            (*pendingCommand_->playbackKind != PlaybackCommandKind::SetMuted ||
             (pendingCommand_->expectedMuted &&
              playbackEvent->muted == *pendingCommand_->expectedMuted)) &&
            (*pendingCommand_->playbackKind != PlaybackCommandKind::SetLoop ||
             (pendingCommand_->expectedLoop &&
              playbackEvent->loop == *pendingCommand_->expectedLoop));
        if (!failed && !applied) {
            Fault(L"message-playback-preference-state", E_ACCESSDENIED);
            return S_OK;
        }
    }
    state_ = std::move(next);
    if (pendingCommand_ &&
        state_.lastAcknowledgedCommandId == pendingCommand_->id) pendingCommand_.reset();
    if (type == L"ready") {
        pageReady_ = true;
        if (desiredVisible_) (void)SetVisible(true);
    }
    if (playbackEvent && configuration_.playbackEvent)
        configuration_.playbackEvent(*playbackEvent);
    if (configuration_.invalidate) configuration_.invalidate();
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::OnProcessFailed(
    ICoreWebView2ProcessFailedEventArgs* args) noexcept {
    if (state_.lifecycle == Lifecycle::Faulted ||
        state_.lifecycle == Lifecycle::Closing) return S_FALSE;
    COREWEBVIEW2_PROCESS_FAILED_KIND kind{};
    if (!args || FAILED(args->get_ProcessFailedKind(&kind))) {
        Fault(L"browser-process-failed", E_FAIL);
        return S_OK;
    }
    if (kind == COREWEBVIEW2_PROCESS_FAILED_KIND_BROWSER_PROCESS_EXITED) {
        environmentFaulted_ = true;
        sharedEnvironment_->faulted = true;
    }
    Fault(L"browser-process-failed");
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::SetVisible(const bool visible) noexcept {
    desiredVisible_ = visible;
    if (!controllerBase_) return S_FALSE;
    if (state_.lifecycle != Lifecycle::ReadyHidden && state_.lifecycle != Lifecycle::Visible)
        return E_UNEXPECTED;
    const bool effectiveVisible = visible && pageReady_;
    const auto desiredLifecycle = effectiveVisible
        ? Lifecycle::Visible : Lifecycle::ReadyHidden;
    if (state_.lifecycle == desiredLifecycle &&
        state_.inputEnabled == effectiveVisible) return S_FALSE;
    const HRESULT result = controllerBase_->put_IsVisible(
        effectiveVisible ? TRUE : FALSE);
    if (FAILED(result)) { Fault(L"visibility", result); return result; }
    state_.lifecycle = desiredLifecycle;
    state_.inputEnabled = effectiveVisible;
    if (configuration_.setPresentationVisible)
        configuration_.setPresentationVisible(effectiveVisible);
    Emit(L"Rich media lifecycle=" + std::wstring{LifecycleName(state_.lifecycle)});
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::UpdateGeometry(
    const RECT& bounds, const double rasterScale) noexcept {
    if (bounds.right <= bounds.left || bounds.bottom <= bounds.top ||
        rasterScale < 0.5 || rasterScale > 8.0) return E_INVALIDARG;
    const bool geometryChanged = configuration_.bounds.left != bounds.left ||
        configuration_.bounds.top != bounds.top ||
        configuration_.bounds.right != bounds.right ||
        configuration_.bounds.bottom != bounds.bottom ||
        configuration_.rasterScale != rasterScale;
    configuration_.bounds = bounds;
    configuration_.rasterScale = rasterScale;
    if (geometryChanged && configuration_.resources.empty())
        state_.focusedActionBoundsCurrent = false;
    else if (!configuration_.resources.empty() && pageReady_) {
        state_.focusedActionBounds = ActionBounds{
            0.0, 0.0, static_cast<double>(bounds.right - bounds.left),
            static_cast<double>(bounds.bottom - bounds.top)};
        state_.focusedActionBoundsCurrent = true;
    }
    if (!controllerBase_) return S_FALSE;
    if (controllerGeometryApplied_ && !geometryChanged) return S_FALSE;
    // The host owns placement in its DirectComposition tree. WebView2 owns
    // pixels in controller-local coordinates only; retaining the host offset
    // here would apply placement twice.
    const RECT controllerBounds{
        0, 0, bounds.right - bounds.left, bounds.bottom - bounds.top};
    HRESULT result = controllerBase_->put_Bounds(controllerBounds);
    ComPtr<ICoreWebView2Controller3> controller3;
    if (SUCCEEDED(result) && SUCCEEDED(controllerBase_.As(&controller3)))
        result = controller3->put_RasterizationScale(rasterScale);
    if (SUCCEEDED(result)) controllerGeometryApplied_ = true;
    return result;
}

HRESULT RichMediaSurfaceCoordinator::BeginPresentationTransfer(
    PresentationTransferFailureStage* const failureStage) noexcept {
    return BeginPresentationTransfer(true, failureStage);
}

HRESULT RichMediaSurfaceCoordinator::BeginPresentationRetarget(
    PresentationTransferFailureStage* const failureStage) noexcept {
    return BeginPresentationTransfer(false, failureStage);
}

HRESULT RichMediaSurfaceCoordinator::BeginPresentationTransfer(
    const bool detachRoot,
    PresentationTransferFailureStage* const failureStage) noexcept {
    if (failureStage) *failureStage = PresentationTransferFailureStage::None;
    if (presentationTransferPending_ ||
        !controller_ || !controllerBase_ ||
        (state_.lifecycle != Lifecycle::ReadyHidden &&
         state_.lifecycle != Lifecycle::Visible)) {
        if (failureStage)
            *failureStage = PresentationTransferFailureStage::Admission;
        return E_UNEXPECTED;
    }
    transferDesiredVisible_ = desiredVisible_;
    state_.inputEnabled = false;
    if (configuration_.setPresentationVisible)
        configuration_.setPresentationVisible(false);
    HRESULT result = controllerBase_->put_IsVisible(FALSE);
    if (FAILED(result)) {
        if (failureStage)
            *failureStage = PresentationTransferFailureStage::VisibilityDetach;
        Fault(L"presentation-transfer-visibility", result);
        return result;
    }
    if (detachRoot) result = controller_->put_RootVisualTarget(nullptr);
    if (FAILED(result)) {
        if (failureStage)
            *failureStage = PresentationTransferFailureStage::RootTargetDetach;
        Fault(L"presentation-transfer-root-target-detach", result);
        return result;
    }
    if (detachRoot) {
        configuration_.ownerWindow = nullptr;
        configuration_.compositionTarget.Reset();
        configuration_.setPresentationVisible = {};
    }
    state_.lifecycle = Lifecycle::ReadyHidden;
    state_.focusedActionBoundsCurrent = false;
    controllerGeometryApplied_ = false;
    presentationTransferDetached_ = detachRoot;
    presentationTransferPending_ = true;
    Emit(detachRoot
        ? L"Rich media presentation transfer=detached"
        : L"Rich media presentation transfer=retargeting");
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::CompletePresentationTransfer(
    PresentationTarget target,
    PresentationTransferFailureStage* const failureStage) noexcept {
    return CompletePresentationTransfer(std::move(target), true, failureStage);
}

HRESULT RichMediaSurfaceCoordinator::CompletePresentationTransfer(
    PresentationTarget target, const bool rootFirst,
    PresentationTransferFailureStage* const failureStage) noexcept {
    if (failureStage) *failureStage = PresentationTransferFailureStage::None;
    if (!presentationTransferPending_ ||
        !controller_ || !controllerBase_ ||
        !target.ownerWindow || !target.compositionTarget ||
        target.bounds.right <= target.bounds.left ||
        target.bounds.bottom <= target.bounds.top ||
        target.rasterScale < 0.5 || target.rasterScale > 8.0 ||
        !target.setPresentationVisible) {
        if (failureStage)
            *failureStage = PresentationTransferFailureStage::Admission;
        return E_INVALIDARG;
    }
    configuration_.ownerWindow = target.ownerWindow;
    configuration_.compositionTarget = std::move(target.compositionTarget);
    configuration_.bounds = target.bounds;
    configuration_.rasterScale = target.rasterScale;
    configuration_.setPresentationVisible = std::move(target.setPresentationVisible);
    const auto attachRoot = [&] {
        return controller_->put_RootVisualTarget(
            configuration_.compositionTarget.Get());
    };
    const auto attachParent = [&] {
        return controllerBase_->put_ParentWindow(target.ownerWindow);
    };
    auto failedStage = rootFirst
        ? PresentationTransferFailureStage::RootTargetAttach
        : PresentationTransferFailureStage::ParentWindowAttach;
    HRESULT result = rootFirst ? attachRoot() : attachParent();
    if (SUCCEEDED(result)) {
        failedStage = rootFirst
            ? PresentationTransferFailureStage::ParentWindowAttach
            : PresentationTransferFailureStage::RootTargetAttach;
        result = rootFirst ? attachParent() : attachRoot();
    }
    if (SUCCEEDED(result)) {
        failedStage = PresentationTransferFailureStage::GeometryAttach;
        result = UpdateGeometry(target.bounds, target.rasterScale);
    }
    const bool visible = transferDesiredVisible_ && pageReady_;
    if (SUCCEEDED(result)) {
        failedStage = PresentationTransferFailureStage::VisibilityAttach;
        result = controllerBase_->put_IsVisible(visible ? TRUE : FALSE);
    }
    if (FAILED(result)) {
        if (failureStage) *failureStage = failedStage;
        presentationTransferPending_ = false;
        presentationTransferDetached_ = false;
        transferDesiredVisible_ = false;
        Fault(L"presentation-transfer-attach", result);
        return result;
    }
    desiredVisible_ = transferDesiredVisible_;
    transferDesiredVisible_ = false;
    presentationTransferPending_ = false;
    presentationTransferDetached_ = false;
    state_.lifecycle = visible ? Lifecycle::Visible : Lifecycle::ReadyHidden;
    state_.inputEnabled = visible;
    configuration_.setPresentationVisible(visible);
    Emit(L"Rich media presentation transfer=attached");
    return S_OK;
}

std::wstring RichMediaSurfaceCoordinator::CommandJson(
    const Command command, const Authority& authority, const std::uint64_t commandId) {
    const wchar_t* name{};
    switch (command) {
    case Command::NavigatePrevious: name = L"previous"; break;
    case Command::NavigateNext: name = L"next"; break;
    case Command::Activate: name = L"arm-activate"; break;
    case Command::Back: name = L"back"; break;
    case Command::TogglePlayback: name = L"toggle"; break;
    case Command::SeekBackward: name = L"seek-back"; break;
    case Command::SeekForward: name = L"seek-forward"; break;
    }
    return std::format(
        L"{{\"command\":\"{}\",\"environmentGeneration\":{},\"surfaceGeneration\":{},\"sessionGeneration\":{},\"controllerGeneration\":{},\"documentGeneration\":{},\"commandId\":{}}}",
        name, authority.environmentGeneration, authority.surfaceGeneration,
        authority.sessionGeneration,
        authority.controllerGeneration, authority.documentGeneration, commandId);
}

bool RichMediaSurfaceCoordinator::SendFocusedSpatialActivation() noexcept {
    if (!controller_ || !state_.inputEnabled || !pendingCommand_ ||
        pendingCommand_->command != Command::Activate ||
        pendingCommand_->phase != PendingPhase::AwaitingSpatialActivation ||
        !state_.focusedActionBoundsCurrent) return false;
    const auto& bounds = state_.focusedActionBounds;
    POINT point{};
    if (!FocusedActionPoint(bounds, configuration_.bounds, point)) return false;
    HRESULT result = controller_->SendMouseInput(
        COREWEBVIEW2_MOUSE_EVENT_KIND_MOVE,
        COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_NONE, 0, point);
    if (SUCCEEDED(result)) result = controller_->SendMouseInput(
        COREWEBVIEW2_MOUSE_EVENT_KIND_LEFT_BUTTON_DOWN,
        COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_LEFT_BUTTON, 0, point);
    if (SUCCEEDED(result)) result = controller_->SendMouseInput(
        COREWEBVIEW2_MOUSE_EVENT_KIND_LEFT_BUTTON_UP,
        COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_NONE, 0, point);
    // This host synthesizes a complete, bounded pointer gesture rather than
    // forwarding a system mouse stream. WebView2 requires composition hosts to
    // terminate injected movement with LEAVE; otherwise hover/hit-test state can
    // survive across later controller activations and presentation changes.
    const HRESULT leaveResult = controller_->SendMouseInput(
        COREWEBVIEW2_MOUSE_EVENT_KIND_LEAVE,
        COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_NONE, 0, POINT{});
    return SUCCEEDED(result) && SUCCEEDED(leaveResult);
}

bool RichMediaSurfaceCoordinator::FocusedActionPoint(
    const ActionBounds& actionBounds, const RECT& surfaceBounds,
    POINT& point) noexcept {
    const double centerX = actionBounds.x + actionBounds.width / 2.0;
    const double centerY = actionBounds.y + actionBounds.height / 2.0;
    const double surfaceWidth = surfaceBounds.right - surfaceBounds.left;
    const double surfaceHeight = surfaceBounds.bottom - surfaceBounds.top;
    if (!std::isfinite(centerX) || !std::isfinite(centerY) || centerX < 0.0 ||
        centerY < 0.0 || centerX >= surfaceWidth || centerY >= surfaceHeight) return false;
    point = {static_cast<LONG>(std::lround(centerX)),
             static_cast<LONG>(std::lround(centerY))};
    return true;
}

bool RichMediaSurfaceCoordinator::SendCommand(const Command command) noexcept {
    if (!core_ || state_.lifecycle != Lifecycle::Visible || !state_.inputEnabled ||
        pendingCommand_ || (command == Command::Activate &&
                            !state_.focusedActionBoundsCurrent)) return false;
    const auto commandId = ++nextCommandId_;
    const std::wstring json = CommandJson(command, state_.authority, commandId);
    if (FAILED(core_->PostWebMessageAsJson(json.c_str()))) return false;
    pendingCommand_ = PendingCommand{
        commandId, command, 0, {}, PendingPhase::AwaitingEvent};
    return true;
}

bool RichMediaSurfaceCoordinator::SendSeekPosition(
    const double positionSeconds) noexcept {
    if (!core_ || state_.lifecycle != Lifecycle::Visible || !state_.inputEnabled ||
        pendingCommand_ || state_.mediaKey.empty() ||
        !std::isfinite(positionSeconds) || positionSeconds < 0.0 ||
        (state_.durationSeconds > 0.0 && positionSeconds > state_.durationSeconds))
        return false;
    const auto commandId = ++nextCommandId_;
    const std::wstring json = std::format(
        L"{{\"command\":\"seek\",\"environmentGeneration\":{},\"surfaceGeneration\":{},\"sessionGeneration\":{},\"controllerGeneration\":{},\"documentGeneration\":{},\"commandId\":{},\"commandSequence\":0,\"mediaKey\":\"{}\",\"positionSeconds\":{}}}",
        state_.authority.environmentGeneration, state_.authority.surfaceGeneration,
        state_.authority.sessionGeneration, state_.authority.controllerGeneration,
        state_.authority.documentGeneration, commandId, state_.mediaKey,
        positionSeconds);
    if (FAILED(core_->PostWebMessageAsJson(json.c_str()))) return false;
    pendingCommand_ = PendingCommand{
        commandId, Command::SeekForward, 0, state_.mediaKey,
        PendingPhase::AwaitingEvent};
    return true;
}

PlaybackCommandDispatchResult RichMediaSurfaceCoordinator::DispatchPlaybackCommand(
    const PlaybackCommand& command) noexcept {
    const bool typedCommandLifecycle =
        state_.lifecycle == Lifecycle::Visible ||
        state_.lifecycle == Lifecycle::ReadyHidden;
    if (command.sequence == 0 || command.mediaKey.empty() ||
        state_.lifecycle == Lifecycle::Absent ||
        state_.lifecycle == Lifecycle::Faulted ||
        state_.lifecycle == Lifecycle::Closing)
        return PlaybackCommandDispatchResult::Rejected;
    if (!core_ || !pageReady_ || !typedCommandLifecycle ||
        (state_.lifecycle == Lifecycle::Visible && !state_.inputEnabled) ||
        pendingCommand_)
        return PlaybackCommandDispatchResult::Deferred;
    Command transport{};
    std::wstring name;
    switch (command.kind) {
    case PlaybackCommandKind::Load: name = L"load"; transport = Command::TogglePlayback; break;
    case PlaybackCommandKind::Cue: name = L"cue"; transport = Command::TogglePlayback; break;
    case PlaybackCommandKind::Play: name = L"arm-activate"; transport = Command::Activate; break;
    case PlaybackCommandKind::Pause: name = L"pause"; transport = Command::TogglePlayback; break;
    case PlaybackCommandKind::Seek: name = L"seek"; transport = Command::SeekForward; break;
    case PlaybackCommandKind::SetVolume: name = L"volume"; transport = Command::TogglePlayback; break;
    case PlaybackCommandKind::SetPlaybackRate: name = L"playback-rate"; transport = Command::TogglePlayback; break;
    case PlaybackCommandKind::SetMuted: name = L"muted"; transport = Command::TogglePlayback; break;
    case PlaybackCommandKind::SetLoop: name = L"loop"; transport = Command::TogglePlayback; break;
    }
    if (transport == Command::Activate && !state_.focusedActionBoundsCurrent)
        return PlaybackCommandDispatchResult::Rejected;
    const auto commandId = ++nextCommandId_;
    std::wstring json = std::format(
        L"{{\"command\":\"{}\",\"environmentGeneration\":{},\"surfaceGeneration\":{},\"sessionGeneration\":{},\"controllerGeneration\":{},\"documentGeneration\":{},\"commandId\":{},\"commandSequence\":{},\"mediaKey\":\"{}\"",
        name, state_.authority.environmentGeneration, state_.authority.surfaceGeneration,
        state_.authority.sessionGeneration, state_.authority.controllerGeneration,
        state_.authority.documentGeneration, commandId, command.sequence,
        command.mediaKey);
    if (command.positionSeconds)
        json += std::format(L",\"positionSeconds\":{}", *command.positionSeconds);
    if (command.volume) json += std::format(L",\"volume\":{}", *command.volume);
    if (command.playbackRate)
        json += std::format(L",\"playbackRate\":{}", *command.playbackRate);
    if (command.muted)
        json += std::format(L",\"muted\":{}", *command.muted ? L"true" : L"false");
    if (command.loop)
        json += std::format(L",\"loop\":{}", *command.loop ? L"true" : L"false");
    json += L"}";
    if (FAILED(core_->PostWebMessageAsJson(json.c_str())))
        return PlaybackCommandDispatchResult::Rejected;
    pendingCommand_ = PendingCommand{
        commandId, transport, command.sequence, command.mediaKey,
        PendingPhase::AwaitingEvent, command.kind, command.playbackRate,
        command.muted, command.loop};
    return PlaybackCommandDispatchResult::Sent;
}

bool RichMediaSurfaceCoordinator::SendPlaybackCommand(
    const PlaybackCommand& command) noexcept {
    return DispatchPlaybackCommand(command) == PlaybackCommandDispatchResult::Sent;
}

bool RichMediaSurfaceCoordinator::ForwardMouse(
    const UINT message, const WPARAM wParam, const LPARAM lParam) noexcept {
    if (!controller_ || !state_.inputEnabled) return false;
    COREWEBVIEW2_MOUSE_EVENT_KIND kind{};
    switch (message) {
    case WM_MOUSEMOVE: kind = COREWEBVIEW2_MOUSE_EVENT_KIND_MOVE; break;
    case WM_LBUTTONDOWN: kind = COREWEBVIEW2_MOUSE_EVENT_KIND_LEFT_BUTTON_DOWN; break;
    case WM_LBUTTONUP: kind = COREWEBVIEW2_MOUSE_EVENT_KIND_LEFT_BUTTON_UP; break;
    case WM_RBUTTONDOWN: kind = COREWEBVIEW2_MOUSE_EVENT_KIND_RIGHT_BUTTON_DOWN; break;
    case WM_RBUTTONUP: kind = COREWEBVIEW2_MOUSE_EVENT_KIND_RIGHT_BUTTON_UP; break;
    case WM_MOUSEWHEEL: kind = COREWEBVIEW2_MOUSE_EVENT_KIND_WHEEL; break;
    case WM_MOUSELEAVE: kind = COREWEBVIEW2_MOUSE_EVENT_KIND_LEAVE; break;
    default: return false;
    }
    POINT point = lastMousePoint_;
    if (message != WM_MOUSELEAVE &&
        !SurfaceLocalPoint(configuration_.ownerWindow, configuration_.bounds,
                           message, lParam, point)) {
        if (mouseInside_) {
            mouseInside_ = false;
            (void)controller_->SendMouseInput(
                COREWEBVIEW2_MOUSE_EVENT_KIND_LEAVE,
                COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS_NONE, 0, POINT{});
        }
        return false;
    }
    if (message == WM_MOUSELEAVE) point = {};
    if (message == WM_MOUSEMOVE && !mouseInside_) {
        TRACKMOUSEEVENT tracking{sizeof(tracking), TME_LEAVE,
                                 configuration_.ownerWindow, 0};
        (void)TrackMouseEvent(&tracking);
    }
    mouseInside_ = message != WM_MOUSELEAVE;
    lastMousePoint_ = point;
    return SUCCEEDED(controller_->SendMouseInput(
        kind, static_cast<COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS>(LOWORD(wParam)),
        message == WM_MOUSEWHEEL ? GET_WHEEL_DELTA_WPARAM(wParam) : 0, point));
}

bool RichMediaSurfaceCoordinator::SurfaceLocalPoint(
    HWND ownerWindow, const RECT& bounds, const UINT message, const LPARAM lParam,
    POINT& point) noexcept {
    point = {GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
    if (message == WM_MOUSEWHEEL &&
        (!ownerWindow || !ScreenToClient(ownerWindow, &point))) return false;
    point.x -= bounds.left;
    point.y -= bounds.top;
    return point.x >= 0 && point.y >= 0 &&
        point.x < bounds.right - bounds.left &&
        point.y < bounds.bottom - bounds.top;
}

bool RichMediaSurfaceCoordinator::ForwardKey(
    const UINT message, const WPARAM wParam, const LPARAM lParam) noexcept {
    if (!state_.inputEnabled || (message != WM_KEYDOWN && message != WM_SYSKEYDOWN) ||
        (lParam & (1U << 30)) != 0) return false;
    switch (wParam) {
    case VK_LEFT: case VK_UP: return SendCommand(Command::NavigatePrevious);
    case VK_RIGHT: case VK_DOWN: return SendCommand(Command::NavigateNext);
    case VK_RETURN: case VK_SPACE: return SendCommand(Command::Activate);
    case VK_ESCAPE: return SendCommand(Command::Back);
    default: return false;
    }
}

HRESULT RichMediaSurfaceCoordinator::GetAutomationProvider(
    IRawElementProviderSimple** provider) const noexcept {
    if (!provider) return E_POINTER;
    *provider = nullptr;
    if (!controller_ || state_.lifecycle != Lifecycle::Visible ||
        !state_.inputEnabled || !pageReady_) return S_FALSE;
    ComPtr<ICoreWebView2CompositionController2> controller2;
    HRESULT result = controller_.As(&controller2);
    ComPtr<IUnknown> unknown;
    if (SUCCEEDED(result)) result = controller2->get_AutomationProvider(&unknown);
    return SUCCEEDED(result) && unknown ? unknown.CopyTo(provider) : result;
}

bool RichMediaSurfaceCoordinator::ValidatePageEvent(
    const std::wstring_view json, const Authority& expectedAuthority,
    const std::uint64_t lastSequence,
    const std::optional<std::uint64_t> pendingCommandId, State& next) noexcept {
    if (json.empty() || json.size() > kMaximumMessageCharacters || json.front() != L'{' || json.back() != L'}')
        return false;
    try {
        const auto object = winrt::Windows::Data::Json::JsonObject::Parse(json);
        constexpr std::array required{
            L"type", L"environmentGeneration", L"surfaceGeneration", L"sessionGeneration",
            L"controllerGeneration", L"documentGeneration", L"eventSequence",
            L"commandId", L"commandSequence", L"focus", L"playing", L"bounds"};
        constexpr std::array playbackKeys{
            L"mediaKey", L"playbackState", L"positionSeconds",
            L"durationSeconds", L"volume"};
        constexpr std::array preferenceKeys{L"playbackRate", L"muted", L"loop"};
        const bool hasPlayback = object.HasKey(L"mediaKey") ||
            object.HasKey(L"playbackState") || object.HasKey(L"positionSeconds") ||
            object.HasKey(L"durationSeconds") || object.HasKey(L"volume");
        const bool hasAnyPreference = std::any_of(
            preferenceKeys.begin(), preferenceKeys.end(),
            [&](const wchar_t* key) { return object.HasKey(key); });
        const bool hasAllPreferences = std::all_of(
            preferenceKeys.begin(), preferenceKeys.end(),
            [&](const wchar_t* key) { return object.HasKey(key); });
        const std::size_t expectedSize = required.size() +
            (hasPlayback ? playbackKeys.size() : 0) +
            (hasAnyPreference ? preferenceKeys.size() : 0) +
            (object.HasKey(L"errorCode") ? 1 : 0);
        if (object.Size() != expectedSize ||
            std::any_of(required.begin(), required.end(),
                        [&](const wchar_t* key) { return !object.HasKey(key); }) ||
            (hasPlayback && std::any_of(playbackKeys.begin(), playbackKeys.end(),
                        [&](const wchar_t* key) { return !object.HasKey(key); })) ||
            (hasAnyPreference && (!hasPlayback || !hasAllPreferences))) return false;
        const std::wstring type = object.GetNamedString(L"type").c_str();
        const std::wstring focus = object.GetNamedString(L"focus").c_str();
        const auto number = [&](const wchar_t* key, std::uint64_t& value) {
            const double raw = object.GetNamedNumber(key);
            if (raw < 0.0 || raw != std::floor(raw) ||
                raw > static_cast<double>(UINT64_MAX)) return false;
            value = static_cast<std::uint64_t>(raw);
            return true;
        };
        Authority received;
        std::uint64_t commandId{};
        std::uint64_t commandSequence{};
        if (!number(L"environmentGeneration", received.environmentGeneration) ||
            !number(L"surfaceGeneration", received.surfaceGeneration) ||
            !number(L"sessionGeneration", received.sessionGeneration) ||
            !number(L"controllerGeneration", received.controllerGeneration) ||
            !number(L"documentGeneration", received.documentGeneration) ||
            !number(L"eventSequence", received.eventSequence) ||
            !number(L"commandId", commandId) ||
            !number(L"commandSequence", commandSequence) ||
            received.eventSequence == 0) return false;
        const bool unsolicitedObservation = commandId == 0;
        if (received.environmentGeneration != expectedAuthority.environmentGeneration ||
            received.surfaceGeneration != expectedAuthority.surfaceGeneration ||
            received.sessionGeneration != expectedAuthority.sessionGeneration ||
            received.controllerGeneration != expectedAuthority.controllerGeneration ||
            received.documentGeneration != expectedAuthority.documentGeneration ||
            received.eventSequence <= lastSequence ||
            (unsolicitedObservation
                ? commandSequence != 0
                : (!pendingCommandId || commandId != *pendingCommandId)) ||
            (type != L"ready" && type != L"focus" && type != L"media" &&
             type != L"back" && type != L"armed") ||
            !IsDocumentLocalIdentifier(focus)) return false;
        const auto boundsObject = object.GetNamedObject(L"bounds");
        constexpr std::array boundsKeys{L"x", L"y", L"width", L"height"};
        if (boundsObject.Size() != boundsKeys.size() ||
            std::any_of(boundsKeys.begin(), boundsKeys.end(),
                        [&](const wchar_t* key) { return !boundsObject.HasKey(key); })) return false;
        ActionBounds bounds{
            boundsObject.GetNamedNumber(L"x"), boundsObject.GetNamedNumber(L"y"),
            boundsObject.GetNamedNumber(L"width"), boundsObject.GetNamedNumber(L"height")};
        if (!std::isfinite(bounds.x) || !std::isfinite(bounds.y) ||
            !std::isfinite(bounds.width) || !std::isfinite(bounds.height) ||
            bounds.x < 0.0 || bounds.y < 0.0 || bounds.width <= 0.0 ||
            bounds.height <= 0.0 || bounds.x + bounds.width > 8192.0 ||
            bounds.y + bounds.height > 8192.0) return false;
        next.authority = received;
        next.lastAcknowledgedCommandId = commandId;
        next.focusedActionBounds = bounds;
        next.focusedActionBoundsCurrent = true;
        next.focusedElement = focus;
        next.playing = object.GetNamedBoolean(L"playing");
        if (hasPlayback) {
            const auto mediaKey = std::wstring(
                std::wstring_view(object.GetNamedString(L"mediaKey")));
            const auto playbackState = std::wstring(
                std::wstring_view(object.GetNamedString(L"playbackState")));
            constexpr std::array<std::wstring_view, 6> states{
                L"loading", L"ready", L"playing", L"paused", L"ended", L"error"};
            const double position = object.GetNamedNumber(L"positionSeconds");
            const double duration = object.GetNamedNumber(L"durationSeconds");
            const double volume = object.GetNamedNumber(L"volume");
            if (!IsDocumentLocalIdentifier(mediaKey) ||
                std::find(states.begin(), states.end(), playbackState) == states.end() ||
                !std::isfinite(position) || !std::isfinite(duration) ||
                !std::isfinite(volume) || position < 0.0 || duration < 0.0 ||
                duration > 86400.0 || position > duration || volume < 0.0 || volume > 1.0)
                return false;
            if (hasAllPreferences) {
                const double playbackRate = object.GetNamedNumber(L"playbackRate");
                if (!std::isfinite(playbackRate) ||
                    playbackRate < protocol_contract::MinimumEmbeddedMediaPlaybackRate ||
                    playbackRate > protocol_contract::MaximumEmbeddedMediaPlaybackRate)
                    return false;
                next.playbackRate = playbackRate;
                next.muted = object.GetNamedBoolean(L"muted");
                next.loop = object.GetNamedBoolean(L"loop");
            }
            if (object.HasKey(L"errorCode")) {
                const auto error = std::wstring(
                    std::wstring_view(object.GetNamedString(L"errorCode")));
                if (!IsDocumentLocalIdentifier(error)) return false;
            }
            next.mediaKey = mediaKey;
            next.positionSeconds = position;
            next.durationSeconds = duration;
            next.volume = volume;
        }
        return true;
    } catch (...) {
        return false;
    }
}

void RichMediaSurfaceCoordinator::Fault(
    const std::wstring_view code, const HRESULT result) noexcept {
    state_.lifecycle = Lifecycle::Faulted;
    state_.inputEnabled = false;
    state_.failureCode.assign(code);
    if (controllerBase_) (void)controllerBase_->put_IsVisible(FALSE);
    pendingCommand_.reset();
    pageReady_ = false;
    if (configuration_.setPresentationVisible) configuration_.setPresentationVisible(false);
    Emit(L"Rich media lifecycle=faulted code=" + std::wstring{code} +
         L" hr=" + std::to_wstring(static_cast<long>(result)));
    if (configuration_.invalidate) configuration_.invalidate();
}

void RichMediaSurfaceCoordinator::RemoveEvents() noexcept {
    if (!core_) return;
    for (auto& subscription : frameSubscriptions_) {
        if (subscription.frame && subscription.navigationStartingToken.value)
            (void)subscription.frame->remove_NavigationStarting(
                subscription.navigationStartingToken);
    }
    frameSubscriptions_.clear();
    if (navigationStartingToken_.value) (void)core_->remove_NavigationStarting(navigationStartingToken_);
    if (navigationCompletedToken_.value) (void)core_->remove_NavigationCompleted(navigationCompletedToken_);
    if (webResourceRequestedToken_.value) (void)core_->remove_WebResourceRequested(webResourceRequestedToken_);
    if (webMessageReceivedToken_.value) (void)core_->remove_WebMessageReceived(webMessageReceivedToken_);
    if (processFailedToken_.value) (void)core_->remove_ProcessFailed(processFailedToken_);
    if (newWindowRequestedToken_.value) (void)core_->remove_NewWindowRequested(newWindowRequestedToken_);
    if (permissionRequestedToken_.value) (void)core_->remove_PermissionRequested(permissionRequestedToken_);
    ComPtr<ICoreWebView2_4> core4;
    if (frameCreatedToken_.value && SUCCEEDED(core_.As(&core4)))
        (void)core4->remove_FrameCreated(frameCreatedToken_);
    if (downloadStartingToken_.value && SUCCEEDED(core_.As(&core4)))
        (void)core4->remove_DownloadStarting(downloadStartingToken_);
    ComPtr<ICoreWebView2_10> core10;
    if (basicAuthenticationToken_.value && SUCCEEDED(core_.As(&core10)))
        (void)core10->remove_BasicAuthenticationRequested(basicAuthenticationToken_);
    ComPtr<ICoreWebView2_14> core14;
    if (serverCertificateToken_.value && SUCCEEDED(core_.As(&core14)))
        (void)core14->remove_ServerCertificateErrorDetected(serverCertificateToken_);
    ComPtr<ICoreWebView2_18> core18;
    if (externalUriToken_.value && SUCCEEDED(core_.As(&core18)))
        (void)core18->remove_LaunchingExternalUriScheme(externalUriToken_);
    navigationStartingToken_ = {}; navigationCompletedToken_ = {};
    webResourceRequestedToken_ = {}; webMessageReceivedToken_ = {};
    processFailedToken_ = {}; newWindowRequestedToken_ = {}; permissionRequestedToken_ = {};
    downloadStartingToken_ = {}; basicAuthenticationToken_ = {};
    serverCertificateToken_ = {}; externalUriToken_ = {};
    frameCreatedToken_ = {};
}

void RichMediaSurfaceCoordinator::Shutdown() noexcept {
    shutdownRequested_ = true;
    if (teardownRequested_) return;
    BeginSessionTeardown();
    CompleteSessionTeardown();
    ReleaseEnvironment(true);
    profileRootDirectory_.clear();
    shutdownRequested_ = false;
}

void RichMediaSurfaceCoordinator::BeginSessionTeardown() noexcept {
    if (state_.lifecycle == Lifecycle::Absent || teardownBegun_ ||
        teardownRequested_) return;
    // The wait below dispatches messages, which lets WebView2 deliver a
    // pending controller creation for this exact coordinator. Refuse that
    // adoption up front so the doomed callback cannot run a full
    // initialization and notify the host about a session being retired.
    teardownRequested_ = true;
    sessionTeardownResult_ = {};
    retrySurfaceGeneration_ = state_.lifecycle == Lifecycle::Faulted
        ? state_.authority.surfaceGeneration : 0;
    const bool creationPending =
        state_.lifecycle == Lifecycle::EnvironmentCreating ||
        state_.lifecycle == Lifecycle::ControllerCreating;
    // Publish Closing before any callback or message dispatch. Shared
    // environment completion must not start another controller during teardown.
    state_.lifecycle = Lifecycle::Closing;
    const auto creatingLease = callbackLease_;
    if (creatingLease && creationPending) {
        desiredVisible_ = false;
        state_.inputEnabled = false;
        if (configuration_.setPresentationVisible)
            configuration_.setPresentationVisible(false);
        const auto creationDeadline = std::chrono::steady_clock::now() +
            std::chrono::seconds(5);
        while (creatingLease->outstandingCreates.load(std::memory_order_acquire) != 0 &&
               std::chrono::steady_clock::now() < creationDeadline) {
            MSG message{};
            while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
                TranslateMessage(&message);
                DispatchMessageW(&message);
            }
            MsgWaitForMultipleObjectsEx(
                0, nullptr, 10, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
        }
        if (creatingLease->outstandingCreates.load(std::memory_order_acquire) != 0)
            Emit(L"Rich media callback-retirement deadline expired");
    }
    state_.lifecycle = Lifecycle::Closing;
    presentationTransferPending_ = false;
    transferDesiredVisible_ = false;
    state_.inputEnabled = false;
    if (ownsSharedController_) {
        if (sharedEnvironment_->liveControllerOwners != 0)
            --sharedEnvironment_->liveControllerOwners;
        ownsSharedController_ = false;
    }
    pendingCommand_.reset();
    Emit(L"Rich media lifecycle=closing");
    if (configuration_.setPresentationVisible) configuration_.setPresentationVisible(false);
    RetireCallbacks();
    const auto retiredLeases = retiredLeases_;
    RemoveEvents();
    sessionTeardownResult_.browserProcessId = environmentSignal_
        ? environmentSignal_->browserProcessId.load(std::memory_order_acquire) : 0;
    sessionTeardownResult_.visibilityResult = controllerBase_
        ? controllerBase_->put_IsVisible(FALSE) : S_FALSE;
    sessionTeardownResult_.rootVisualResult = controller_
        ? controller_->put_RootVisualTarget(nullptr) : S_FALSE;
    sessionTeardownResult_.controllerCloseResult = controllerBase_
        ? controllerBase_->Close() : S_FALSE;
    core_.Reset(); controllerBase_.Reset(); controller_.Reset();
    controllerGeometryApplied_ = false;
    const auto callbackDeadline = std::chrono::steady_clock::now() +
        std::chrono::seconds(5);
    const auto callbacksPending = [&] {
        return std::any_of(retiredLeases.begin(), retiredLeases.end(),
            [](const auto& lease) {
                return lease->outstandingCreates.load(std::memory_order_acquire) != 0;
            });
    };
    while (callbacksPending() &&
           std::chrono::steady_clock::now() < callbackDeadline) {
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        MsgWaitForMultipleObjectsEx(0, nullptr, 10, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
    }
    const bool callbackDeadlineExpired = callbacksPending();
    if (callbackDeadlineExpired)
        Emit(L"Rich media callback-retirement deadline expired");
    retiredLeases_.clear();
    sessionTeardownResult_.callbackDeadlineExpired = callbackDeadlineExpired;
    configuration_ = {};
    sessionTeardownResult_.sessionOwnersEmpty =
        !core_ && !controllerBase_ && !controller_ && !callbackLease_ &&
        retiredLeases_.empty() && frameSubscriptions_.empty() &&
        !configuration_.compositionTarget && !configuration_.diagnostic &&
        !configuration_.invalidate && !configuration_.playbackEvent &&
        !configuration_.setPresentationVisible;
    sessionTeardownResult_.environmentRetained =
        environmentLifecycle_ == EnvironmentLifecycle::Ready && environment_;
    teardownBegun_ = true;
    teardownRequested_ = false;
}

void RichMediaSurfaceCoordinator::CompleteSessionTeardown() noexcept {
    if (teardownRequested_ || !teardownBegun_) return;
    state_ = {};
    waitingForSharedEnvironmentRecovery_ = false;
    desiredVisible_ = false;
    controllerGeometryApplied_ = false;
    pageReady_ = false;
    mouseInside_ = false;
    pendingNavigationId_ = 0;
    presentationTransferPending_ = false;
    transferDesiredVisible_ = false;
    teardownBegun_ = false;
    if (shutdownRequested_) {
        ReleaseEnvironment(true);
        profileRootDirectory_.clear();
        shutdownRequested_ = false;
    }
}

void RichMediaSurfaceCoordinator::ReleaseEnvironment(
    const bool markForDeferredCleanup) noexcept {
    if (environmentLifecycle_ == EnvironmentLifecycle::Cold) return;
    environmentLifecycle_ = EnvironmentLifecycle::ShuttingDown;
    browserProcessExitedToken_ = {};
    environment5_.Reset();
    environment_.Reset();
    environmentSignal_.reset();
    environmentFaulted_ = false;
    environmentLifecycle_ = EnvironmentLifecycle::Cold;
    if (sharedEnvironment_.use_count() == 1) {
        auto& shared = *sharedEnvironment_;
        shared.lifecycle = EnvironmentLifecycle::ShuttingDown;
        if (shared.environment5 && shared.browserProcessExitedToken.value)
            (void)shared.environment5->remove_BrowserProcessExited(
                shared.browserProcessExitedToken);
        shared.browserProcessExitedToken = {};
        shared.environment5.Reset();
        shared.environment.Reset();
        if (markForDeferredCleanup) MarkCurrentProfileForDeferredCleanup();
        shared.signal.reset();
        shared.faulted = false;
        shared.lifecycle = EnvironmentLifecycle::Cold;
    }
}

void RichMediaSurfaceCoordinator::MarkCurrentProfileForDeferredCleanup() const noexcept {
    if (environmentProfileDirectory_.empty()) return;
    try {
        std::filesystem::create_directories(environmentProfileDirectory_);
        const auto marker = std::filesystem::path{environmentProfileDirectory_} /
            (L".wrail-retired-owner-" + std::to_wstring(GetCurrentProcessId()));
        std::ofstream stream(marker, std::ios::binary | std::ios::trunc);
        stream << "WidgetRail rich-media deferred cleanup\n";
    } catch (...) {
    }
}

void RichMediaSurfaceCoordinator::CleanupMarkedPriorProfiles() const noexcept {
    if (profileRootDirectory_.empty()) return;
    try {
        std::size_t inspected{};
        for (const auto& entry : std::filesystem::directory_iterator(profileRootDirectory_)) {
            if (++inspected > 32) break;
            if (!entry.is_directory()) continue;
            const auto name = entry.path().filename().wstring();
            if (!name.starts_with(L"wrail-rich-media-")) continue;
            for (const auto& marker : std::filesystem::directory_iterator(entry.path())) {
                const auto markerName = marker.path().filename().wstring();
                constexpr std::wstring_view prefix = L".wrail-retired-owner-";
                if (!marker.is_regular_file() || !markerName.starts_with(prefix)) continue;
                const auto ownerText = markerName.substr(prefix.size());
                wchar_t* end{};
                const unsigned long owner = std::wcstoul(ownerText.c_str(), &end, 10);
                if (!end || *end != L'\0' || owner == 0 || owner > MAXDWORD) break;
                HANDLE process = OpenProcess(SYNCHRONIZE, FALSE, static_cast<DWORD>(owner));
                if (process) {
                    const bool alive = WaitForSingleObject(process, 0) != WAIT_OBJECT_0;
                    CloseHandle(process);
                    if (alive) break;
                } else if (GetLastError() != ERROR_INVALID_PARAMETER) {
                    break;
                }
                std::error_code error;
                std::filesystem::remove_all(entry.path(), error);
                break;
            }
        }
    } catch (...) {
    }
}

void RichMediaSurfaceCoordinator::Emit(std::wstring message) const {
    if (configuration_.diagnostic) configuration_.diagnostic(message);
}

} // namespace widgetrail::richmedia
