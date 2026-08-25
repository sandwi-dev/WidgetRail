#include "RichMediaSurfaceCoordinator.h"

#include <WebView2EnvironmentOptions.h>
#include <UIAutomation.h>
#include <windowsx.h>
#include <objidl.h>
#include <wrl.h>
#include <winrt/Windows.Data.Json.h>
#include <winrt/Windows.Foundation.Collections.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <filesystem>
#include <format>
#include <string>
#include <vector>

using Microsoft::WRL::Callback;
using Microsoft::WRL::ComPtr;

namespace widgetrail::richmedia {
namespace {

constexpr wchar_t kOrigin[] = L"https://wrail-rich-media.invalid";
constexpr wchar_t kPageUri[] = L"https://wrail-rich-media.invalid/index.html";
constexpr wchar_t kResourceFilter[] = L"*";
constexpr std::size_t kMaximumMessageCharacters = 512;

constexpr char kPage[] = R"HTML(<!doctype html>
<meta charset="utf-8"><meta name="viewport" content="width=device-width">
<style>html,body{margin:0;background:#111;color:#fff;font:24px system-ui}main{padding:28px}
button{font:inherit;margin:8px;padding:12px 22px}button:focus{outline:4px solid #65b8ff}
</style><main><h1>Embedded media proof</h1><audio id="media" src="/tone.wav"></audio>
<button id="back">Back</button><button id="play">Play</button><button id="seek">Seek</button>
<p id="state">Ready</p></main><script>
let generation=0,last=0,focus='play'; const media=document.querySelector('#media');
const buttons=[...document.querySelectorAll('button')];
function emit(type){chrome.webview.postMessage({type,generation,sequence:++last,focus,playing:!media.paused});}
function select(delta){let i=Math.max(0,buttons.indexOf(document.activeElement));i=(i+delta+buttons.length)%buttons.length;buttons[i].focus();focus=buttons[i].id;emit('focus');}
document.querySelector('#back').onclick=()=>emit('back');
document.querySelector('#play').onclick=()=>{media.paused?media.play():media.pause();emit('media');};
document.querySelector('#seek').onclick=()=>{media.currentTime=Math.min(media.duration||1,media.currentTime+.2);emit('media');};
chrome.webview.addEventListener('message',e=>{const m=e.data;if(!m||m.generation!==generation||m.sequence<=last)return;last=m.sequence;
if(m.command==='initialize'){generation=m.nextGeneration;last=0;buttons[1].focus();focus='play';emit('ready');return;}
if(m.command==='previous')select(-1);else if(m.command==='next')select(1);else if(m.command==='activate')document.activeElement.click();
else if(m.command==='back')emit('back');else if(m.command==='toggle')document.querySelector('#play').click();
else if(m.command==='seek-back'){media.currentTime=Math.max(0,media.currentTime-.2);emit('media');}
else if(m.command==='seek-forward')document.querySelector('#seek').click();});
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

RichMediaSurfaceCoordinator::RichMediaSurfaceCoordinator() = default;
RichMediaSurfaceCoordinator::~RichMediaSurfaceCoordinator() { Shutdown(); }

HRESULT RichMediaSurfaceCoordinator::Initialize(Configuration configuration) noexcept {
    if (state_.lifecycle != Lifecycle::Absent || !configuration.ownerWindow ||
        !configuration.compositionTarget || configuration.bounds.right <= configuration.bounds.left ||
        configuration.bounds.bottom <= configuration.bounds.top ||
        configuration.ephemeralProfileDirectory.empty()) return E_INVALIDARG;
    configuration_ = std::move(configuration);
    state_ = {};
    desiredVisible_ = configuration_.initiallyVisible;
    state_.authority.sessionGeneration = 1;
    return BeginEnvironment();
}

HRESULT RichMediaSurfaceCoordinator::Retry() noexcept {
    if (state_.lifecycle != Lifecycle::Faulted) return E_UNEXPECTED;
    const auto generation = state_.authority.sessionGeneration + 1;
    Shutdown();
    state_.authority.sessionGeneration = generation;
    desiredVisible_ = configuration_.initiallyVisible;
    state_.authority.commandSequence = 0;
    return BeginEnvironment();
}

HRESULT RichMediaSurfaceCoordinator::BeginEnvironment() noexcept {
    state_.lifecycle = Lifecycle::EnvironmentCreating;
    Emit(L"Rich media lifecycle=environment-creating generation=" +
         std::to_wstring(state_.authority.sessionGeneration));
    ComPtr<ICoreWebView2EnvironmentOptions> options =
        Microsoft::WRL::Make<CoreWebView2EnvironmentOptions>();
    const HRESULT result = CreateCoreWebView2EnvironmentWithOptions(
        nullptr, configuration_.ephemeralProfileDirectory.c_str(), options.Get(),
        Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
            [this](HRESULT status, ICoreWebView2Environment* environment) {
                return OnEnvironmentCreated(status, environment);
            }).Get());
    if (FAILED(result)) Fault(L"environment-start", result);
    return result;
}

HRESULT RichMediaSurfaceCoordinator::OnEnvironmentCreated(
    const HRESULT result, ICoreWebView2Environment* environment) noexcept {
    if (state_.lifecycle != Lifecycle::EnvironmentCreating) return S_FALSE;
    if (FAILED(result) || !environment) { Fault(L"environment-create", result); return S_OK; }
    environment_ = environment;
    browserProcessExited_ = false;
    if (SUCCEEDED(environment_.As(&environment5_))) {
        (void)environment5_->add_BrowserProcessExited(
            Callback<ICoreWebView2BrowserProcessExitedEventHandler>(
                [this](ICoreWebView2Environment*, ICoreWebView2BrowserProcessExitedEventArgs*) {
                    browserProcessExited_ = true;
                    Emit(L"Rich media browser-process-exited");
                    return S_OK;
                }).Get(), &browserProcessExitedToken_);
    }
    state_.lifecycle = Lifecycle::ControllerCreating;
    Emit(L"Rich media lifecycle=controller-creating");
    ComPtr<ICoreWebView2Environment3> compositionEnvironment;
    const HRESULT environment3 = environment_.As(&compositionEnvironment);
    if (FAILED(environment3)) {
        Fault(L"composition-environment", environment3);
        return S_OK;
    }
    const HRESULT create = compositionEnvironment->CreateCoreWebView2CompositionController(
        configuration_.ownerWindow,
        Callback<ICoreWebView2CreateCoreWebView2CompositionControllerCompletedHandler>(
            [this](HRESULT status, ICoreWebView2CompositionController* controller) {
                return OnControllerCreated(status, controller);
            }).Get());
    if (FAILED(create)) Fault(L"controller-start", create);
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::OnControllerCreated(
    const HRESULT result, ICoreWebView2CompositionController* controller) noexcept {
    if (state_.lifecycle != Lifecycle::ControllerCreating) return S_FALSE;
    if (FAILED(result) || !controller) { Fault(L"controller-create", result); return S_OK; }
    controller_ = controller;
    if (FAILED(controller_.As(&controllerBase_)) || FAILED(controllerBase_->get_CoreWebView2(&core_))) {
        Fault(L"controller-interface", E_NOINTERFACE); return S_OK;
    }
    HRESULT configured = controller_->put_RootVisualTarget(configuration_.compositionTarget.Get());
    if (SUCCEEDED(configured)) configured = UpdateGeometry(configuration_.bounds, configuration_.rasterScale);
    if (SUCCEEDED(configured)) configured = ConfigureCore();
    if (FAILED(configured)) { Fault(L"controller-configure", configured); return S_OK; }
    state_.lifecycle = Lifecycle::ReadyHidden;
    state_.inputEnabled = false;
    pageReady_ = false;
    (void)controllerBase_->put_IsVisible(FALSE);
    Emit(L"Rich media lifecycle=ready-hidden");
    if (configuration_.invalidate) configuration_.invalidate();
    configured = core_->Navigate(kPageUri);
    if (FAILED(configured)) Fault(L"navigate", configured);
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::ConfigureCore() noexcept {
    ComPtr<ICoreWebView2Settings> settings;
    HRESULT result = core_->get_Settings(&settings);
    if (SUCCEEDED(result)) result = settings->put_AreDefaultContextMenusEnabled(FALSE);
    if (SUCCEEDED(result)) result = settings->put_AreDevToolsEnabled(FALSE);
    if (SUCCEEDED(result)) result = settings->put_IsStatusBarEnabled(FALSE);
    if (SUCCEEDED(result)) result = settings->put_IsZoomControlEnabled(FALSE);
    if (SUCCEEDED(result)) result = settings->put_IsBuiltInErrorPageEnabled(FALSE);
    if (SUCCEEDED(result)) result = settings->put_AreDefaultScriptDialogsEnabled(FALSE);
    if (SUCCEEDED(result)) result = settings->put_IsWebMessageEnabled(TRUE);
    ComPtr<ICoreWebView2Settings3> settings3;
    if (SUCCEEDED(result) && SUCCEEDED(settings.As(&settings3)))
        result = settings3->put_AreBrowserAcceleratorKeysEnabled(FALSE);
    if (FAILED(result)) return result;
    result = core_->AddWebResourceRequestedFilter(
        kResourceFilter, COREWEBVIEW2_WEB_RESOURCE_CONTEXT_ALL);
    if (SUCCEEDED(result)) result = core_->add_WebResourceRequested(
        Callback<ICoreWebView2WebResourceRequestedEventHandler>(
            [this](ICoreWebView2*, ICoreWebView2WebResourceRequestedEventArgs* args) {
                return ServeResource(args);
            }).Get(), &webResourceRequestedToken_);
    if (SUCCEEDED(result)) result = core_->add_NavigationStarting(
        Callback<ICoreWebView2NavigationStartingEventHandler>(
            [this](ICoreWebView2*, ICoreWebView2NavigationStartingEventArgs* args) {
                return OnNavigationStarting(args);
            }).Get(), &navigationStartingToken_);
    if (SUCCEEDED(result)) result = core_->add_NavigationCompleted(
        Callback<ICoreWebView2NavigationCompletedEventHandler>(
            [this](ICoreWebView2*, ICoreWebView2NavigationCompletedEventArgs* args) {
                return OnNavigationCompleted(args);
            }).Get(), &navigationCompletedToken_);
    if (SUCCEEDED(result)) result = core_->add_WebMessageReceived(
        Callback<ICoreWebView2WebMessageReceivedEventHandler>(
            [this](ICoreWebView2*, ICoreWebView2WebMessageReceivedEventArgs* args) {
                return OnWebMessage(args);
            }).Get(), &webMessageReceivedToken_);
    if (SUCCEEDED(result)) result = core_->add_ProcessFailed(
        Callback<ICoreWebView2ProcessFailedEventHandler>(
            [this](ICoreWebView2*, ICoreWebView2ProcessFailedEventArgs* args) {
                return OnProcessFailed(args);
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
    if (SUCCEEDED(result) && SUCCEEDED(core_.As(&core4)))
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

HRESULT RichMediaSurfaceCoordinator::ServeResource(
    ICoreWebView2WebResourceRequestedEventArgs* args) noexcept {
    ComPtr<ICoreWebView2WebResourceRequest> request;
    LPWSTR rawUri{};
    HRESULT result = args->get_Request(&request);
    if (SUCCEEDED(result)) result = request->get_Uri(&rawUri);
    const std::wstring uri = rawUri ? rawUri : L"";
    CoTaskMemFree(rawUri);
    ComPtr<IStream> stream;
    const wchar_t* contentType{};
    if (uri == kPageUri) {
        stream = StreamFor(kPage, sizeof(kPage) - 1);
        contentType = L"Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store";
    } else if (uri == std::wstring{kOrigin} + L"/tone.wav") {
        const auto wave = WaveBytes();
        stream = StreamFor(wave.data(), wave.size());
        contentType = L"Content-Type: audio/wav\r\nCache-Control: no-store";
    } else {
        Emit(L"Rich media resource denied");
        contentType = L"Content-Type: text/plain\r\nCache-Control: no-store";
    }
    ComPtr<ICoreWebView2WebResourceResponse> response;
    result = environment_->CreateWebResourceResponse(
        stream.Get(), stream ? 200 : 404, stream ? L"OK" : L"Not Found", contentType, &response);
    if (SUCCEEDED(result)) result = args->put_Response(response.Get());
    return result;
}

HRESULT RichMediaSurfaceCoordinator::OnNavigationStarting(
    ICoreWebView2NavigationStartingEventArgs* args) noexcept {
    LPWSTR rawUri{};
    HRESULT result = args->get_Uri(&rawUri);
    const std::wstring uri = rawUri ? rawUri : L"";
    CoTaskMemFree(rawUri);
    if (FAILED(result)) return result;
    if (uri != kPageUri) {
        Emit(L"Rich media navigation denied");
        return args->put_Cancel(TRUE);
    }
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::OnNavigationCompleted(
    ICoreWebView2NavigationCompletedEventArgs* args) noexcept {
    BOOL success{};
    HRESULT result = args->get_IsSuccess(&success);
    if (FAILED(result) || !success) {
        Fault(L"navigation-complete", FAILED(result) ? result : E_FAIL);
        return S_OK;
    }
    const std::wstring command = std::format(
        L"{{\"command\":\"initialize\",\"generation\":0,\"sequence\":1,\"nextGeneration\":{}}}",
        state_.authority.sessionGeneration);
    result = core_->PostWebMessageAsJson(command.c_str());
    if (FAILED(result)) Fault(L"initialize-command", result);
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::OnWebMessage(
    ICoreWebView2WebMessageReceivedEventArgs* args) noexcept {
    LPWSTR rawSource{};
    LPWSTR rawJson{};
    HRESULT result = args->get_Source(&rawSource);
    if (SUCCEEDED(result)) result = args->get_WebMessageAsJson(&rawJson);
    const std::wstring source = rawSource ? rawSource : L"";
    const std::wstring json = rawJson ? rawJson : L"";
    CoTaskMemFree(rawSource); CoTaskMemFree(rawJson);
    if (FAILED(result) || source != kPageUri || json.size() > kMaximumMessageCharacters) {
        Fault(L"message-envelope", FAILED(result) ? result : E_ACCESSDENIED); return S_OK;
    }
    State next = state_;
    if (!ValidatePageEvent(json, state_.authority.sessionGeneration,
                           state_.authority.commandSequence, next)) {
        Fault(L"message-authority", E_ACCESSDENIED); return S_OK;
    }
    state_ = std::move(next);
    if (const auto type = winrt::Windows::Data::Json::JsonObject::Parse(json)
                              .GetNamedString(L"type");
        type == L"ready") {
        pageReady_ = true;
        if (desiredVisible_) (void)SetVisible(true);
    }
    if (configuration_.invalidate) configuration_.invalidate();
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::OnProcessFailed(
    ICoreWebView2ProcessFailedEventArgs*) noexcept {
    Fault(L"browser-process-failed");
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::SetVisible(const bool visible) noexcept {
    desiredVisible_ = visible;
    if (!controllerBase_) return S_FALSE;
    if (state_.lifecycle != Lifecycle::ReadyHidden && state_.lifecycle != Lifecycle::Visible)
        return E_UNEXPECTED;
    const bool effectiveVisible = visible && pageReady_;
    const HRESULT result = controllerBase_->put_IsVisible(
        effectiveVisible ? TRUE : FALSE);
    if (FAILED(result)) { Fault(L"visibility", result); return result; }
    state_.lifecycle = effectiveVisible
        ? Lifecycle::Visible : Lifecycle::ReadyHidden;
    state_.inputEnabled = effectiveVisible;
    Emit(L"Rich media lifecycle=" + std::wstring{LifecycleName(state_.lifecycle)});
    return S_OK;
}

HRESULT RichMediaSurfaceCoordinator::UpdateGeometry(
    const RECT& bounds, const double rasterScale) noexcept {
    if (bounds.right <= bounds.left || bounds.bottom <= bounds.top ||
        rasterScale < 0.5 || rasterScale > 8.0) return E_INVALIDARG;
    configuration_.bounds = bounds;
    configuration_.rasterScale = rasterScale;
    if (!controllerBase_) return S_FALSE;
    HRESULT result = controllerBase_->put_Bounds(bounds);
    ComPtr<ICoreWebView2Controller3> controller3;
    if (SUCCEEDED(result) && SUCCEEDED(controllerBase_.As(&controller3)))
        result = controller3->put_RasterizationScale(rasterScale);
    return result;
}

std::wstring RichMediaSurfaceCoordinator::CommandJson(
    const Command command, const Authority& authority) {
    const wchar_t* name{};
    switch (command) {
    case Command::NavigatePrevious: name = L"previous"; break;
    case Command::NavigateNext: name = L"next"; break;
    case Command::Activate: name = L"activate"; break;
    case Command::Back: name = L"back"; break;
    case Command::TogglePlayback: name = L"toggle"; break;
    case Command::SeekBackward: name = L"seek-back"; break;
    case Command::SeekForward: name = L"seek-forward"; break;
    }
    return std::format(L"{{\"command\":\"{}\",\"generation\":{},\"sequence\":{}}}",
                       name, authority.sessionGeneration, authority.commandSequence);
}

bool RichMediaSurfaceCoordinator::SendCommand(const Command command) noexcept {
    if (!core_ || state_.lifecycle != Lifecycle::Visible || !state_.inputEnabled) return false;
    Authority authority = state_.authority;
    authority.commandSequence++;
    const std::wstring json = CommandJson(command, authority);
    if (FAILED(core_->PostWebMessageAsJson(json.c_str()))) return false;
    state_.authority.commandSequence = authority.commandSequence;
    return true;
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
    default: return false;
    }
    POINT point{GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam)};
    return SUCCEEDED(controller_->SendMouseInput(
        kind, static_cast<COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS>(LOWORD(wParam)),
        message == WM_MOUSEWHEEL ? GET_WHEEL_DELTA_WPARAM(wParam) : 0, point));
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
    if (!controller_) return S_FALSE;
    ComPtr<ICoreWebView2CompositionController2> controller2;
    HRESULT result = controller_.As(&controller2);
    ComPtr<IUnknown> unknown;
    if (SUCCEEDED(result)) result = controller2->get_AutomationProvider(&unknown);
    return SUCCEEDED(result) && unknown ? unknown.CopyTo(provider) : result;
}

bool RichMediaSurfaceCoordinator::ValidatePageEvent(
    const std::wstring_view json, const std::uint64_t expectedGeneration,
    const std::uint64_t lastSequence, State& next) noexcept {
    if (json.empty() || json.size() > kMaximumMessageCharacters || json.front() != L'{' || json.back() != L'}')
        return false;
    try {
        const auto object = winrt::Windows::Data::Json::JsonObject::Parse(json);
        if (object.Size() != 5 || !object.HasKey(L"type") ||
            !object.HasKey(L"generation") || !object.HasKey(L"sequence") ||
            !object.HasKey(L"focus") || !object.HasKey(L"playing")) return false;
        const std::wstring type = object.GetNamedString(L"type").c_str();
        const std::wstring focus = object.GetNamedString(L"focus").c_str();
        const double generationNumber = object.GetNamedNumber(L"generation");
        const double sequenceNumber = object.GetNamedNumber(L"sequence");
        if (generationNumber < 0.0 || sequenceNumber <= 0.0 ||
            generationNumber != std::floor(generationNumber) ||
            sequenceNumber != std::floor(sequenceNumber) ||
            generationNumber > static_cast<double>(UINT64_MAX) ||
            sequenceNumber > static_cast<double>(UINT64_MAX)) return false;
        const auto generation = static_cast<std::uint64_t>(generationNumber);
        const auto sequence = static_cast<std::uint64_t>(sequenceNumber);
        if (generation != expectedGeneration || sequence <= lastSequence ||
            (type != L"ready" && type != L"focus" && type != L"media" &&
             type != L"back") ||
            (focus != L"back" && focus != L"play" && focus != L"seek")) return false;
        next.authority.sessionGeneration = generation;
        next.authority.commandSequence = sequence;
        next.focusedElement = focus;
        next.playing = object.GetNamedBoolean(L"playing");
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
    Emit(L"Rich media lifecycle=faulted code=" + std::wstring{code} +
         L" hr=" + std::to_wstring(static_cast<long>(result)));
    if (configuration_.invalidate) configuration_.invalidate();
}

void RichMediaSurfaceCoordinator::RemoveEvents() noexcept {
    if (!core_) return;
    if (navigationStartingToken_.value) (void)core_->remove_NavigationStarting(navigationStartingToken_);
    if (navigationCompletedToken_.value) (void)core_->remove_NavigationCompleted(navigationCompletedToken_);
    if (webResourceRequestedToken_.value) (void)core_->remove_WebResourceRequested(webResourceRequestedToken_);
    if (webMessageReceivedToken_.value) (void)core_->remove_WebMessageReceived(webMessageReceivedToken_);
    if (processFailedToken_.value) (void)core_->remove_ProcessFailed(processFailedToken_);
    if (newWindowRequestedToken_.value) (void)core_->remove_NewWindowRequested(newWindowRequestedToken_);
    if (permissionRequestedToken_.value) (void)core_->remove_PermissionRequested(permissionRequestedToken_);
    ComPtr<ICoreWebView2_4> core4;
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
}

void RichMediaSurfaceCoordinator::Shutdown() noexcept {
    if (state_.lifecycle == Lifecycle::Absent) return;
    state_.lifecycle = Lifecycle::Closing;
    state_.inputEnabled = false;
    Emit(L"Rich media lifecycle=closing");
    RemoveEvents();
    if (controllerBase_) (void)controllerBase_->put_IsVisible(FALSE);
    if (controller_) (void)controller_->put_RootVisualTarget(nullptr);
    if (controllerBase_) (void)controllerBase_->Close();
    core_.Reset(); controllerBase_.Reset(); controller_.Reset();
    const auto exitDeadline = std::chrono::steady_clock::now() +
        std::chrono::seconds(5);
    while (environment5_ && !browserProcessExited_ &&
           std::chrono::steady_clock::now() < exitDeadline) {
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        MsgWaitForMultipleObjectsEx(
            0, nullptr, 10, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
    }
    if (environment5_ && browserProcessExitedToken_.value)
        (void)environment5_->remove_BrowserProcessExited(
            browserProcessExitedToken_);
    browserProcessExitedToken_ = {};
    environment5_.Reset();
    environment_.Reset();
    if (!browserProcessExited_)
        Emit(L"Rich media browser-process-exit deadline expired");
    if (!configuration_.ephemeralProfileDirectory.empty()) {
        std::error_code error;
        const auto cleanupDeadline = std::chrono::steady_clock::now() +
            std::chrono::seconds(2);
        do {
            error.clear();
            std::filesystem::remove_all(
                configuration_.ephemeralProfileDirectory, error);
            if (!error ||
                !std::filesystem::exists(configuration_.ephemeralProfileDirectory))
                break;
            MsgWaitForMultipleObjectsEx(
                0, nullptr, 20, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
        } while (std::chrono::steady_clock::now() < cleanupDeadline);
        if (error) Emit(L"Rich media ephemeral-profile removal deferred error=" +
                        std::to_wstring(error.value()));
    }
    state_ = {};
    desiredVisible_ = false;
    pageReady_ = false;
    Emit(L"Rich media lifecycle=absent");
}

void RichMediaSurfaceCoordinator::Emit(std::wstring message) const {
    if (configuration_.diagnostic) configuration_.diagnostic(message);
}

} // namespace widgetrail::richmedia
