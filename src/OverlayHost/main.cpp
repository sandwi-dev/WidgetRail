#include "OverlayState.h"
#include "../OverlayPlatformInterop/OverlayPlatformInterop.h"
#include "AccessibilityProvider.h"
#include "AccessibilityProjection.h"
#include "AccessibilityTree.h"
#include "DeclarativeRenderer.h"
#include "EmbeddedMediaResourceContract.h"
#include "ControllerNavigation.h"
#include "ControllerShortcutResolver.h"
#include "ControllerInputOwnership.h"
#include "ControllerGuide.h"
#include "FocusNavigation.h"
#include "HostAccessibility.h"
#include "NativeIcons.h"
#include "NativeStyle.h"
#include "OverlayChrome.h"
#include "OverlayCompositionSurface.h"
#include "CompositorBackgroundSurfaceCoordinator.h"
#include "OverlayPlacement.h"
#include "OverlayPresentationTransaction.h"
#include "OverlayProcessOwner.h"
#include "OverlayTargeting.h"
#include "OverlayTransition.h"
#include "RemoteImageCache.h"
#include "RichMediaSurfaceCoordinator.h"
#include "ScrollEvidenceProbe.h"
#include "LocalWidgetPackageImport.h"
#include "MediaSessionManager.h"
#include "WidgetBridgeClient.h"
#include "WidgetActionFeedback.h"
#include "WidgetAdmissionTrace.h"
#include "WidgetLifecycle.h"
#include "WidgetSessionCoordinator.h"
#include "WidgetSurfaceCoordinator.h"
#include "WidgetInteractionSession.h"
#include "TextEntryModal.h"
#include "TextEntryActionAdmission.h"
#include "TrayLayout.h"

#include <Windows.h>
#include <d2d1_1.h>
#include <dwrite.h>
#include <dwmapi.h>
#include <Roapi.h>
#include <ShellScalingApi.h>
#include <Xinput.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cmath>
#include <climits>
#include <filesystem>
#include <fstream>
#include <limits>
#include <map>
#include <memory>
#include <mutex>
#include <optional>
#include <sstream>
#include <string>
#include <string_view>
#include <unordered_map>
#include <unordered_set>
#include <utility>
#include <vector>

using Microsoft::WRL::ComPtr;

namespace {

constexpr wchar_t kWindowClass[] = L"WidgetRail.OverlayHost";
constexpr wchar_t kBackdropWindowClass[] = L"WidgetRail.Backdrop";
constexpr wchar_t kChromeWindowClass[] = L"WidgetRail.Chrome";
constexpr int kPanelWidth = 1180;
constexpr int kDashboardHeight = 180;
constexpr int kWidgetPanelHeight = 700;
constexpr std::wstring_view kStartupWidgetId = L"settings";
constexpr UINT_PTR kControllerTimer = 1;
constexpr UINT_PTR kGuideCompatibilityTimer = 2;
constexpr UINT_PTR kZOrderSettleTimer = 3;
constexpr UINT_PTR kCatalogRetryTimer = 4;
constexpr UINT_PTR kForegroundLossTimer = 5;
constexpr UINT_PTR kActionFeedbackTimer = 6;
constexpr UINT_PTR kPinnedSurfaceTimer = 7;
constexpr UINT_PTR kBridgeControlPlaneTimer = 8;
constexpr UINT kVisibleControllerTimerMilliseconds = 15;
constexpr UINT kPlatformEventMessage = WM_APP + 1;
constexpr UINT kImageReadyMessage = WM_APP + 2;
constexpr UINT kCatalogRefreshMessage = WM_APP + 3;
constexpr UINT kSnapshotRefreshMessage = WM_APP + 4;
constexpr UINT kWidgetSessionCompletionMessage = WM_APP + 13;
constexpr UINT kForegroundChangedMessage = WM_APP + 5;
constexpr UINT kPlacementRefreshMessage = WM_APP + 6;
constexpr UINT kDisplayRefreshMessage = WM_APP + 7;
constexpr UINT kPerformanceResetMessage = WM_APP + 8;
constexpr UINT kAccessibilityActionMessage = WM_APP + 10;
constexpr UINT kPinnedSurfaceChangedMessage = WM_APP + 11;
constexpr UINT kProcessActivationMessage = WM_APP + 12;
constexpr UINT kDevelopmentTrayYHoldMessage = WM_APP + 14;
constexpr UINT kScrollPaginationPrefetchMessage = WM_APP + 15;
constexpr UINT kSharedMediaEnvironmentExitedMessage = WM_APP + 16;
#if defined(WRAIL_PINNED_SLIDER_ROUTE_TESTING)
constexpr ULONG_PTR kPinnedSliderControllerFrameCopyData = 0x5752534cU;
#endif
constexpr BYTE kBackdropOpacity = 164;
constexpr std::uint64_t kSlowCompositionFrameMicroseconds = 100000;
constexpr int kDeveloperHotkey = 1;
constexpr widgetrail::NativeColor kSafeCanvasFallback{
    1.0F / 255.0F, 2.0F / 255.0F, 3.0F / 255.0F, 1.0F};
constexpr widgetrail::NativeColor kDefaultCanvas{
    0x10 / 255.0F, 0x13 / 255.0F, 0x1A / 255.0F, 1.0F};
constexpr widgetrail::NativeColor kDefaultPanel{
    0x1B / 255.0F, 0x1F / 255.0F, 0x29 / 255.0F, 1.0F};

constexpr widgetrail::NativeColor kDefaultAccent{
    0xFC / 255.0F, 0x3F / 255.0F, 0x6C / 255.0F, 1.0F};

[[nodiscard]] constexpr std::optional<long long>
NextEmbeddedMediaPlaybackObservationSequence(
    const long long current) noexcept {
    if (current == std::numeric_limits<long long>::max()) return std::nullopt;
    return current + 1;
}

static_assert(
    NextEmbeddedMediaPlaybackObservationSequence(4).value() == 5,
    "A restarted document event epoch must project above the host watermark.");
static_assert(
    !NextEmbeddedMediaPlaybackObservationSequence(
         std::numeric_limits<long long>::max()).has_value(),
    "The host playback observation sequence must fail closed at exhaustion.");

enum class OverlayShowResult {
    Shown,
    Deferred,
    Failed,
};

enum class FixedChromePlacementReason {
    None,
    NewVisibleSession,
    DisplayEnvironment,
    Appearance,
    CatalogOrder,
};

constexpr std::wstring_view FixedChromePlacementReasonName(
    const FixedChromePlacementReason reason) noexcept {
    switch (reason) {
    case FixedChromePlacementReason::NewVisibleSession:
        return L"new-visible-session";
    case FixedChromePlacementReason::DisplayEnvironment:
        return L"display-environment";
    case FixedChromePlacementReason::Appearance:
        return L"appearance";
    case FixedChromePlacementReason::CatalogOrder:
        return L"catalog-order";
    case FixedChromePlacementReason::None:
    default:
        return L"none";
    }
}

constexpr std::uint32_t PlatformBoolean(const bool value) noexcept {
    return value
        ? WRAIL_OVERLAY_PLATFORM_TRUE
        : WRAIL_OVERLAY_PLATFORM_FALSE;
}

[[nodiscard]] std::optional<widgetrail::OverlayPlacement> ComputePlatformPlacement(
    const RECT& workArea,
    const UINT dpi,
    const float desiredWidthDip,
    const float desiredHeightDip) noexcept {
    WidgetRailOverlayPlatformPlacementInput input;
    input.workLeft = workArea.left;
    input.workTop = workArea.top;
    input.workRight = workArea.right;
    input.workBottom = workArea.bottom;
    input.dpi = dpi;
    input.desiredWidthDip = desiredWidthDip;
    input.desiredHeightDip = desiredHeightDip;
    WidgetRailOverlayPlatformPlacement placement;
    std::uint32_t hasPlacement = WRAIL_OVERLAY_PLATFORM_FALSE;
    if (WidgetRailOverlayPlatformComputePlacement(
            &input, &placement, &hasPlacement) !=
            WidgetRailOverlayPlatformStatus::Ok ||
        hasPlacement == WRAIL_OVERLAY_PLATFORM_FALSE) {
        return std::nullopt;
    }
    return widgetrail::OverlayPlacement{
        placement.x, placement.y, placement.width, placement.height};
}

void CollectCurrentArtworkHandles(
    const widgetrail::WidgetNode& node,
    std::unordered_set<std::wstring>& handles) {
    if (!node.artworkHandle.empty()) handles.insert(node.artworkHandle);
    if (!node.focusBackgroundArtworkHandle.empty())
        handles.insert(node.focusBackgroundArtworkHandle);
    for (const auto& child : node.children)
        CollectCurrentArtworkHandles(child, handles);
    for (const auto& child : node.focusPresentation)
        CollectCurrentArtworkHandles(child, handles);
    for (const auto& child : node.defaultFocusPresentation)
        CollectCurrentArtworkHandles(child, handles);
}

D2D1_COLOR_F D2DColor(const widgetrail::NativeColor& color) noexcept {
    return D2D1::ColorF(color.red, color.green, color.blue, color.alpha);
}

COLORREF GdiColor(const widgetrail::NativeColor& color) noexcept {
    const auto channel = [](const float value) {
        return static_cast<BYTE>(std::lround(std::clamp(value, 0.0F, 1.0F) * 255.0F));
    };
    return RGB(channel(color.red), channel(color.green), channel(color.blue));
}

struct BuiltInWidget final {
    std::wstring_view id;
    std::wstring_view name;
    widgetrail::icons::NativeIcon icon;
};

constexpr std::array<BuiltInWidget, 3> kBuiltInWidgets{{
    {L"audio-mixer", L"Audio Mixer", widgetrail::icons::NativeIcon::Connection},
    {L"yt-music", L"YT Music", widgetrail::icons::NativeIcon::Music},
    {L"performance", L"Performance", widgetrail::icons::NativeIcon::Warning},
}};

const BuiltInWidget* FindBuiltInWidget(const std::wstring_view id) noexcept {
    const auto found = std::find_if(kBuiltInWidgets.begin(), kBuiltInWidgets.end(),
        [id](const BuiltInWidget& widget) { return widget.id == id; });
    return found == kBuiltInWidgets.end() ? nullptr : &*found;
}

std::wstring_view WidgetName(const std::wstring_view id) noexcept {
    const auto* widget = FindBuiltInWidget(id);
    return widget ? widget->name : id;
}

widgetrail::icons::NativeIcon WidgetIcon(const std::wstring_view id) noexcept {
    const auto* widget = FindBuiltInWidget(id);
    return widget ? widget->icon : widgetrail::icons::NativeIcon::Connection;
}

std::wstring InstallationDirectory() {
    std::array<wchar_t, 32768> path{};
    const DWORD length = GetModuleFileNameW(nullptr, path.data(),
                                            static_cast<DWORD>(path.size()));
    if (length == 0 || length >= path.size()) {
        return {};
    }
    return std::filesystem::path(std::wstring_view(path.data(), length))
        .parent_path().wstring();
}

widgetrail::PersistentState LoadPersistentState() {
    widgetrail::PersistentState state;
    wchar_t localAppData[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH) == 0) {
        return state;
    }

    const std::filesystem::path path =
        std::filesystem::path(localAppData) / L"WidgetRail" / L"overlay-state.ini";
    std::wifstream input(path);
    std::wstring first;
    if (!(input >> first)) return {};
    if (first == L"v2") {
        std::size_t count{};
        if (!(input >> count) || count > 256) return {};
        state.order.reserve(count);
        for (std::size_t index = 0; index < count; ++index) {
            std::wstring id;
            if (!(input >> id) || id.empty() || id.size() > 128) return {};
            state.order.push_back(std::move(id));
        }
        std::wstring last;
        int reopen{};
        if (!(input >> last >> reopen)) return {};
        if (last != L"-") state.lastWidget = std::move(last);
        state.reopenWidget = reopen != 0;
        return state;
    }

    // One-time migration from the fixed numeric prototype format.
    try {
        std::array<int, 3> legacy{std::stoi(first), 0, 0};
        int last = -1;
        int reopen = 0;
        if (!(input >> legacy[1] >> legacy[2] >> last)) return {};
        (void)(input >> reopen);
        for (const int index : legacy) {
            if (index < 0 || index >= static_cast<int>(kBuiltInWidgets.size())) return {};
            state.order.emplace_back(kBuiltInWidgets[static_cast<std::size_t>(index)].id);
        }
        if (last >= 0 && last < static_cast<int>(kBuiltInWidgets.size())) {
            state.lastWidget = std::wstring(kBuiltInWidgets[static_cast<std::size_t>(last)].id);
        }
        state.reopenWidget = reopen != 0;
    } catch (...) {
        return {};
    }
    return state;
}

void SavePersistentState(const widgetrail::PersistentState& state) {
    wchar_t localAppData[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH) == 0) {
        return;
    }

    const auto directory = std::filesystem::path(localAppData) / L"WidgetRail";
    std::error_code error;
    std::filesystem::create_directories(directory, error);
    if (error) {
        return;
    }

    const auto path = directory / L"overlay-state.ini";
    const auto temporary = directory / L"overlay-state.tmp";
    {
        std::wofstream output(temporary, std::ios::trunc);
        if (!output) {
            return;
        }
        output << L"v2 " << state.order.size();
        for (const auto& id : state.order) output << L' ' << id;
        output << L' ' << (state.lastWidget ? *state.lastWidget : L"-")
               << L' ' << (state.reopenWidget ? 1 : 0) << L'\n';
    }
    MoveFileExW(temporary.c_str(), path.c_str(),
                MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
}

void SaveStartupError(const std::wstring& message) {
    wchar_t localAppData[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH) == 0) {
        return;
    }

    const auto directory = std::filesystem::path(localAppData) / L"WidgetRail";
    std::error_code error;
    std::filesystem::create_directories(directory, error);
    if (error) {
        return;
    }

    std::wofstream output(directory / L"startup-error.log", std::ios::trunc);
    if (output) {
        output << message << L'\n';
    }
}

void ClearStartupError() {
    wchar_t localAppData[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH) == 0) {
        return;
    }
    std::error_code error;
    std::filesystem::remove(
        std::filesystem::path(localAppData) / L"WidgetRail" / L"startup-error.log",
        error);
}

void AppendDiagnostic(const std::wstring_view message);

// WebView2 delivers controller, navigation, and message callbacks from
// Chromium frames that are compiled without exception support. A C++ exception
// unwinding out of a host callback into those frames has no handler and fails
// the process fast with FAST_FAIL_FATAL_APP_EXIT rather than surfacing. Every
// host callback body handed to RichMediaSurfaceCoordinator must be contained
// here so a host fault is logged and fails that session closed instead of
// terminating the overlay.
template <typename Callable>
void InvokeMediaCallbackGuarded(
    const std::wstring_view site, Callable&& body) noexcept {
    try {
        std::forward<Callable>(body)();
    } catch (...) {
        AppendDiagnostic(
            L"Embedded media callback fault site=" + std::wstring{site} +
            L" " + widgetrail::DescribeCurrentException());
    }
}

void AppendDiagnostic(const std::wstring_view message) {
    static std::mutex logMutex;
    std::lock_guard lock(logMutex);
    constexpr std::uintmax_t maximumFileBytes = 4ULL * 1024ULL * 1024ULL;
    constexpr std::size_t maximumMessageCharacters = 4096;
    constexpr DWORD crossProcessWaitMilliseconds = 50;

    const HANDLE processMutex = CreateMutexW(
        nullptr, FALSE, L"Local\\WidgetRail.OverlayDiagnosticLog.v1");
    if (!processMutex) return;
    const DWORD wait = WaitForSingleObject(
        processMutex, crossProcessWaitMilliseconds);
    if (wait != WAIT_OBJECT_0 && wait != WAIT_ABANDONED) {
        CloseHandle(processMutex);
        return;
    }
    const auto releaseProcessMutex = [&] {
        ReleaseMutex(processMutex);
        CloseHandle(processMutex);
    };

    wchar_t localAppData[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH) == 0) {
        releaseProcessMutex();
        return;
    }
    const auto directory = std::filesystem::path(localAppData) / L"WidgetRail";
    std::error_code error;
    std::filesystem::create_directories(directory, error);
    if (error) {
        releaseProcessMutex();
        return;
    }

    SYSTEMTIME now{};
    GetLocalTime(&now);
    std::wstring boundedMessage(message.substr(
        0, std::min(message.size(), maximumMessageCharacters)));
    std::wostringstream formatted;
    formatted << now.wYear << L'-' << now.wMonth << L'-' << now.wDay << L' '
              << now.wHour << L':' << now.wMinute << L':' << now.wSecond << L'.'
              << now.wMilliseconds << L' ' << boundedMessage << L"\r\n";
    const auto wideLine = formatted.str();
    const int utf8Length = WideCharToMultiByte(
        CP_UTF8, WC_ERR_INVALID_CHARS, wideLine.data(),
        static_cast<int>(wideLine.size()), nullptr, 0, nullptr, nullptr);
    if (utf8Length <= 0) {
        releaseProcessMutex();
        return;
    }
    std::string line(static_cast<std::size_t>(utf8Length), '\0');
    if (WideCharToMultiByte(
            CP_UTF8, WC_ERR_INVALID_CHARS, wideLine.data(),
            static_cast<int>(wideLine.size()), line.data(), utf8Length,
            nullptr, nullptr) != utf8Length) {
        releaseProcessMutex();
        return;
    }

    const auto current = directory / L"overlay.log";
    const auto prior = directory / L"overlay.1.log";
    const auto oldest = directory / L"overlay.2.log";
    const auto boundedMove = [&](const std::filesystem::path& source,
                                 const std::filesystem::path& destination,
                                 const std::filesystem::path& temporary) {
        error.clear();
        if (!std::filesystem::exists(source, error)) return !error;
        const auto size = std::filesystem::file_size(source, error);
        if (error) return false;
        std::filesystem::remove(destination, error);
        if (error && error != std::errc::no_such_file_or_directory) return false;
        error.clear();
        if (size <= maximumFileBytes) {
            std::filesystem::rename(source, destination, error);
            return !error;
        }
        std::filesystem::remove(temporary, error);
        error.clear();
        std::ifstream input(source, std::ios::binary);
        std::ofstream output(temporary, std::ios::binary | std::ios::trunc);
        if (!input || !output) return false;
        input.seekg(-static_cast<std::streamoff>(maximumFileBytes), std::ios::end);
        std::string discarded;
        std::getline(input, discarded);
        output << input.rdbuf();
        output.close();
        if (!output || !MoveFileExW(
                temporary.c_str(), destination.c_str(),
                MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) return false;
        std::filesystem::remove(source, error);
        return !error;
    };

    error.clear();
    const auto currentSize = std::filesystem::exists(current, error)
        ? std::filesystem::file_size(current, error)
        : 0;
    if (error) {
        releaseProcessMutex();
        return;
    }
    if (currentSize + line.size() > maximumFileBytes) {
        std::filesystem::remove(oldest, error);
        if (error && error != std::errc::no_such_file_or_directory) {
            releaseProcessMutex();
            return;
        }
        if (!boundedMove(prior, oldest, directory / L"overlay.2.tmp") ||
            !boundedMove(current, prior, directory / L"overlay.1.tmp")) {
            releaseProcessMutex();
            return;
        }
    }
    std::ofstream output(current, std::ios::binary | std::ios::app);
    if (output) output.write(line.data(), static_cast<std::streamsize>(line.size()));
    releaseProcessMutex();
}

enum class DiagnosticSeverity { Debug, Information, Warning, Error };

constexpr std::wstring_view DiagnosticSeverityValue(
    const DiagnosticSeverity severity) noexcept {
    switch (severity) {
    case DiagnosticSeverity::Debug: return L"debug";
    case DiagnosticSeverity::Information: return L"information";
    case DiagnosticSeverity::Warning: return L"warning";
    case DiagnosticSeverity::Error: return L"error";
    }
    return L"unknown";
}

// High-volume action correlation is isolated from overlay.log and bounded to
// three one-MiB generations. Rotation is best-effort and diagnostics never own
// action routing or transport behavior.
void AppendActionCorrelation(
    std::wstring message,
    const DiagnosticSeverity severity = DiagnosticSeverity::Debug) {
    static std::mutex logMutex;
    std::lock_guard lock(logMutex);
    constexpr std::uintmax_t maximumBytes = 1024 * 1024;
    constexpr std::size_t maximumMessageCharacters = 2048;
    if (message.size() > maximumMessageCharacters)
        message.resize(maximumMessageCharacters);

    wchar_t localAppData[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH) == 0)
        return;
    const auto directory = std::filesystem::path(localAppData) / L"WidgetRail";
    std::error_code error;
    std::filesystem::create_directories(directory, error);
    if (error) return;

    const auto current = directory / L"action-correlation.log";
    const auto prior = directory / L"action-correlation.1.log";
    const auto oldest = directory / L"action-correlation.2.log";
    const bool currentExists = std::filesystem::exists(current, error);
    if (error) return;
    const auto size = currentExists
        ? std::filesystem::file_size(current, error)
        : 0;
    if (error) return;
    if (size >= maximumBytes) {
        std::filesystem::remove(oldest, error);
        if (error && error != std::errc::no_such_file_or_directory) return;
        error.clear();
        const bool priorExists = std::filesystem::exists(prior, error);
        if (error) return;
        if (priorExists) {
            error.clear();
            std::filesystem::rename(prior, oldest, error);
            if (error) return;
        }
        error.clear();
        std::filesystem::rename(current, prior, error);
        if (error) return;
    }

    SYSTEMTIME now{};
    GetLocalTime(&now);
    std::wofstream output(current, std::ios::app);
    if (!output) return;
    output << now.wYear << L'-' << now.wMonth << L'-' << now.wDay << L' '
           << now.wHour << L':' << now.wMinute << L':' << now.wSecond << L'.'
           << now.wMilliseconds << L" level=" << DiagnosticSeverityValue(severity)
           << L" category=widget-action " << message << L'\n';
}

class OverlayApp final {
public:
    // The dashboard is catalog-owned. Persisted IDs are reconciled only after
    // the bridge supplies runnable worker descriptors, so an unavailable
    // bridge cannot expose inert native placeholder tiles.
    OverlayApp()
        : state_(LoadPersistentState(), {}),
          actionFailureFeedback_({
              [] { return GetTickCount64(); },
              [this](const std::optional<std::uint64_t> deadline) {
                  ScheduleActionFeedbackExpiry(deadline);
              },
              [this] {
                  if (window_) InvalidateRect(window_, nullptr, FALSE);
              },
              [this] {
                  return CommitActionFailureFixedChrome();
              },
              [this] {
                  accessibilityProvider_.RaisePendingEvents();
                  chromeAccessibilityProvider_.RaisePendingEvents();
              }}),
          admissionTrace_([](std::wstring message) {
              AppendDiagnostic(message);
          }),
          sessions_(
              widgetrail::WidgetSessionOperations{
                  [this](std::stop_token) {
                      return bridge_.EnsureStarted(
                                 installationDirectory_,
                                 developmentCatalogRoot_.value_or(L""))
                          ? widgetrail::WidgetSessionOperationResult<bool>::Success(true)
                          : widgetrail::WidgetSessionOperationResult<bool>::Failure(
                                widgetrail::WidgetSessionFailureStage::Start,
                                bridge_.lastError());
                  },
                  [this](std::stop_token) {
                      auto value = bridge_.ListWidgets();
                      return value
                          ? widgetrail::WidgetSessionOperationResult<
                                std::vector<widgetrail::WidgetDescriptor>>::Success(
                                    std::move(*value))
                          : widgetrail::WidgetSessionOperationResult<
                                std::vector<widgetrail::WidgetDescriptor>>::Failure(
                                    widgetrail::WidgetSessionFailureStage::Catalog,
                                    bridge_.lastError());
                  },
                  [this](std::stop_token, const std::wstring_view widgetId,
                         const widgetrail::WidgetLifecycleState state,
                         const long long baseSequence,
                         const widgetrail::WidgetPresentationTransactionKind transactionKind,
                         const long long recoveryOriginSequence) {
                      auto value = bridge_.EstablishWidgetPresentation(
                          widgetId, widgetrail::WidgetLifecycleProtocolValue(state),
                          baseSequence, transactionKind, recoveryOriginSequence);
                      if (value) {
                          return widgetrail::WidgetSessionOperationResult<
                              widgetrail::WidgetPresentationPublication>::Success(
                                  std::move(*value));
                      }
                      const auto requestFailure =
                          bridge_.lastRequestFailureCategory();
                      return widgetrail::WidgetSessionOperationResult<
                          widgetrail::WidgetPresentationPublication>::Failure(
                              requestFailure == widgetrail::
                                  WidgetBridgeRequestFailureCategory::StalePresentationBase
                                  ? widgetrail::WidgetSessionFailureStage::Protocol
                                  : bridge_.lastRuntimeFailureCategory(widgetId) ==
                                      widgetrail::WidgetBridgeRuntimeFailureCategory::WorkerStart
                                  ? widgetrail::WidgetSessionFailureStage::Start
                                  : widgetrail::WidgetSessionFailureStage::Snapshot,
                              bridge_.lastError(), requestFailure);
                  },
                  [this](std::stop_token, const std::wstring_view widgetId,
                         const widgetrail::WidgetLifecycleState state) {
                      auto value = bridge_.SetWidgetLifecycle(
                          widgetId, widgetrail::WidgetLifecycleProtocolValue(state));
                      return value
                          ? widgetrail::WidgetSessionOperationResult<bool>::Success(*value)
                          : widgetrail::WidgetSessionOperationResult<bool>::Failure(
                                widgetrail::WidgetSessionFailureStage::Lifecycle,
                                bridge_.lastError());
                  },
                  [this](std::stop_token, const std::wstring_view widgetId,
                         const long long baseSequence,
                         const widgetrail::WidgetPresentationTransactionKind transactionKind,
                         const long long recoveryOriginSequence) {
                      auto value = bridge_.GetSnapshot(
                          widgetId, baseSequence, transactionKind,
                          recoveryOriginSequence);
                      if (value) {
                          return widgetrail::WidgetSessionOperationResult<
                              widgetrail::WidgetPresentationPublication>::Success(
                                  std::move(*value));
                      }
                      const auto requestFailure =
                          bridge_.lastRequestFailureCategory();
                      return widgetrail::WidgetSessionOperationResult<
                          widgetrail::WidgetPresentationPublication>::Failure(
                              requestFailure == widgetrail::
                                  WidgetBridgeRequestFailureCategory::StalePresentationBase
                                  ? widgetrail::WidgetSessionFailureStage::Protocol
                                  : widgetrail::WidgetSessionFailureStage::Snapshot,
                              bridge_.lastError(), requestFailure);
                  },
                  [](const widgetrail::WidgetSnapshot& checkpoint,
                     const widgetrail::WidgetPresentationUpdate& update,
                     const std::wstring_view presentationGeneration) {
                      std::wstring error;
                      auto value = widgetrail::MaterializeWidgetPresentationUpdate(
                          checkpoint, update, presentationGeneration, error);
                      return value
                          ? widgetrail::WidgetSessionOperationResult<
                                widgetrail::WidgetPresentationMaterialization>::Success(
                                std::move(*value))
                          : widgetrail::WidgetSessionOperationResult<
                                widgetrail::WidgetPresentationMaterialization>::Failure(
                                widgetrail::WidgetSessionFailureStage::Protocol,
                                std::move(error));
                  },
                  [this](std::stop_token, const std::wstring_view widgetId) {
                      auto value = bridge_.RestartWidget(widgetId);
                      return value
                          ? widgetrail::WidgetSessionOperationResult<bool>::Success(*value)
                          : widgetrail::WidgetSessionOperationResult<bool>::Failure(
                                widgetrail::WidgetSessionFailureStage::Restart,
                                bridge_.lastError());
                  },
                  [this] { return bridge_.bridgeSessionGeneration(); },
              },
              [this] {
                  if (window_)
                      PostMessageW(window_, kWidgetSessionCompletionMessage, 0, 0);
              },
              [this](const widgetrail::WidgetSessionTraceEvent& event) {
                  admissionTrace_.RecordSession(event);
              },
              [] { return static_cast<std::uint64_t>(GetTickCount64()); }),
          localWidgetPackageImport_(
              localWidgetPackagePicker_,
              [this] { return CurrentLocalWidgetPackageImportOrigin(); },
              [this](const std::wstring_view path,
                     const widgetrail::packages::LocalWidgetPackageOrigin& origin,
                     const std::wstring_view operationId) {
                  widgetrail::LocalWidgetPackageInstallOrigin requestOrigin{
                      origin.widgetId,
                      origin.packageId,
                      origin.publisherId,
                      origin.instanceId,
                      origin.runtimeGeneration,
                      origin.presentationGeneration};
                  const auto submitted = bridge_.BeginLocalWidgetPackageInstall(
                      path, requestOrigin, operationId);
                  return submitted.value_or(false);
              }) {}
    ~OverlayApp() { Shutdown(); }

    [[nodiscard]] const std::wstring& initializationError() const noexcept {
        return initializationError_;
    }

    bool Initialize(HINSTANCE instance, int showCommand) {
        ClearStartupError();
        instance_ = instance;
        installationDirectory_ = InstallationDirectory();
        if (installationDirectory_.empty()) {
            return FailWin32(L"GetModuleFileNameW", GetLastError());
        }
        if (!ParseDevelopmentArguments()) return false;
        if (performanceState_) {
            // Performance observations must not inherit or mutate the user's
            // last-open widget, tray order, or reopen preference. The catalog
            // is still the real installed catalog; only presentation state is
            // ephemeral for this process.
            state_ = widgetrail::OverlayState({}, {});
        }
        const HRESULT runtimeResult = RoInitialize(RO_INIT_SINGLETHREADED);
        if (FAILED(runtimeResult) && runtimeResult != RPC_E_CHANGED_MODE) {
            return FailHresult(L"RoInitialize", runtimeResult);
        }
        runtimeInitialized_ = SUCCEEDED(runtimeResult);
        // The packaged build embeds PerMonitorV2 in app.manifest. Keep the
        // runtime declaration as a defense for developer/CMake builds that do
        // not embed that manifest; the older shcore API can only request PMv1.
        // ERROR_ACCESS_DENIED is expected when the manifest already fixed the
        // process awareness before entry-point execution.
        if (!SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)) {
            const DWORD awarenessError = GetLastError();
            if (awarenessError != ERROR_ACCESS_DENIED) {
                AppendDiagnostic(L"Unable to request Per-Monitor-V2 DPI awareness error=" +
                                 std::to_wstring(awarenessError));
            }
        }

        WNDCLASSEXW windowClass{sizeof(windowClass)};
        windowClass.style = CS_HREDRAW | CS_VREDRAW;
        windowClass.lpfnWndProc = WindowProc;
        windowClass.hInstance = instance_;
        windowClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        // This HWND is a premultiplied DirectComposition target. A class brush
        // is an independent opaque presentation owner and can become visible
        // in uncovered client pixels while a hidden host is reopened. The
        // separate backdrop window remains the only full-monitor black owner.
        windowClass.hbrBackground = nullptr;
        windowClass.lpszClassName = kWindowClass;
        if (!RegisterClassExW(&windowClass)) {
            return FailWin32(L"RegisterClassExW", GetLastError());
        }

        WNDCLASSEXW backdropClass{sizeof(backdropClass)};
        backdropClass.style = CS_HREDRAW | CS_VREDRAW;
        backdropClass.lpfnWndProc = BackdropWindowProc;
        backdropClass.hInstance = instance_;
        backdropClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        backdropClass.hbrBackground = static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH));
        backdropClass.lpszClassName = kBackdropWindowClass;
        if (!RegisterClassExW(&backdropClass)) {
            return FailWin32(L"RegisterClassExW(backdrop)", GetLastError());
        }

        WNDCLASSEXW chromeClass{sizeof(chromeClass)};
        chromeClass.style = CS_HREDRAW | CS_VREDRAW;
        chromeClass.lpfnWndProc = ChromeWindowProc;
        chromeClass.hInstance = instance_;
        chromeClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        chromeClass.hbrBackground = nullptr;
        chromeClass.lpszClassName = kChromeWindowClass;
        if (!RegisterClassExW(&chromeClass)) {
            return FailWin32(L"RegisterClassExW(chrome)", GetLastError());
        }

        window_ = CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP | WS_EX_TOPMOST,
            kWindowClass,
            L"WidgetRail",
            WS_POPUP,
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            kPanelWidth,
            kWidgetPanelHeight,
            nullptr,
            nullptr,
            instance_,
            this);
        if (!window_) {
            return FailWin32(L"CreateWindowExW", GetLastError());
        }
        accessibilityProvider_.Bind(window_, kAccessibilityActionMessage);
        chromeWindow_ = CreateWindowExW(
            widgetrail::shell::FixedChromeWindowExStyle(),
            kChromeWindowClass,
            L"WidgetRail chrome",
            widgetrail::shell::FixedChromeWindowStyle(),
            CW_USEDEFAULT,
            CW_USEDEFAULT,
            1,
            1,
            window_,
            nullptr,
            instance_,
            this);
        if (!chromeWindow_) {
            return FailWin32(L"CreateWindowExW(chrome)", GetLastError());
        }
        chromeAccessibilityProvider_.Bind(chromeWindow_, kAccessibilityActionMessage);
        backdropWindow_ = CreateWindowExW(
            WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
            kBackdropWindowClass,
            L"WidgetRail backdrop",
            WS_POPUP,
            0,
            0,
            1,
            1,
            nullptr,
            nullptr,
            instance_,
            this);
        if (!backdropWindow_) {
            return FailWin32(L"CreateWindowExW(backdrop)", GetLastError());
        }
        WidgetRailOverlayPlatformCreateOptions platformOptions;
        platformOptions.callbackContext = this;
        platformOptions.eventAvailable = OnPlatformEventAvailable;
        platformOptions.diagnostic = OnPlatformDiagnostic;
        if (WidgetRailOverlayPlatformCreate(&platformOptions, &platform_) !=
            WidgetRailOverlayPlatformStatus::Ok) {
            initializationError_ =
                L"Unable to create the versioned native platform boundary.";
            return false;
        }
        (void)WidgetRailOverlayPlatformSetOwnedWindows(
            platform_,
            reinterpret_cast<std::uintptr_t>(window_),
            reinterpret_cast<std::uintptr_t>(backdropWindow_));
        SetLayeredWindowAttributes(backdropWindow_, 0, kBackdropOpacity, LWA_ALPHA);
        const BOOL disableTransitions = TRUE;
        const BOOL excludeFromPeek = TRUE;
        (void)DwmSetWindowAttribute(window_, DWMWA_TRANSITIONS_FORCEDISABLED,
                                    &disableTransitions, sizeof(disableTransitions));
        (void)DwmSetWindowAttribute(window_, DWMWA_EXCLUDED_FROM_PEEK,
                                    &excludeFromPeek, sizeof(excludeFromPeek));
        (void)DwmSetWindowAttribute(backdropWindow_, DWMWA_TRANSITIONS_FORCEDISABLED,
                                    &disableTransitions, sizeof(disableTransitions));
        (void)DwmSetWindowAttribute(backdropWindow_, DWMWA_EXCLUDED_FROM_PEEK,
                                    &excludeFromPeek, sizeof(excludeFromPeek));
        foregroundEventApp_ = this;
        foregroundHook_ = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND,
            EVENT_SYSTEM_FOREGROUND,
            nullptr,
            OnForegroundChanged,
            0,
            0,
            WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
        if (!foregroundHook_) {
            AppendDiagnostic(L"SetWinEventHook(EVENT_SYSTEM_FOREGROUND) failed error=" +
                             std::to_wstring(GetLastError()));
        }

        const HRESULT d2dResult = D2D1CreateFactory(
            D2D1_FACTORY_TYPE_SINGLE_THREADED,
            IID_PPV_ARGS(d2dFactory_.ReleaseAndGetAddressOf()));
        if (FAILED(d2dResult)) {
            return FailHresult(L"D2D1CreateFactory", d2dResult);
        }
        std::wstring compositionError;
        if (widgetrail::shell::InitializeFixedChromeComposition(
                compositionSurface_, window_, chromeWindow_,
                d2dFactory_.Get(), compositionError)) {
            // A premultiplied composition target carries transparency in its
            // alpha channel. Drop the legacy HWND color key and use full
            // authored opacity; the separate backdrop remains the only dimmer.
            appliedOverlayOpacity_.reset();
            AppendDiagnostic(
                L"DirectComposition complete-content presentation owner active "
                L"alpha=premultiplied-clear hwnd=no-redirection "
                L"opacity=composition-effect hwnd-background=none");
        } else {
            EnableLegacyLayeredFallback();
            AppendDiagnostic(
                L"DirectComposition unavailable; retaining HWND render-target fallback: " +
                compositionError);
        }
        const HRESULT dwriteResult = DWriteCreateFactory(
            DWRITE_FACTORY_TYPE_SHARED,
            __uuidof(IDWriteFactory),
            reinterpret_cast<IUnknown**>(writeFactory_.ReleaseAndGetAddressOf()));
        if (FAILED(dwriteResult)) {
            return FailHresult(L"DWriteCreateFactory", dwriteResult);
        }

        const widgetrail::RemoteImageLimits imageLimits;
        widgetrail::RemoteImageCache::ArtworkDecodeDiagnosticCallback
            decodeDiagnostic;
        widgetrail::DeclarativeRenderer::ArtworkRenderDiagnosticCallback
            renderDiagnostic;
        if (artworkRenderDiagnostics_) {
            decodeDiagnostic = [this](
                const widgetrail::TrustedArtworkDecodeDiagnostic& diagnostic) {
                const wchar_t* contentType = L"unknown";
                switch (diagnostic.contentType) {
                case widgetrail::TrustedArtworkContentType::Jpeg:
                    contentType = L"jpeg";
                    break;
                case widgetrail::TrustedArtworkContentType::Png:
                    contentType = L"png";
                    break;
                case widgetrail::TrustedArtworkContentType::WebP:
                    contentType = L"webp";
                    break;
                case widgetrail::TrustedArtworkContentType::Unknown:
                    break;
                }
                PublishArtworkRenderDiagnostic(
                    L"stage=decode resource-hash=" +
                    std::to_wstring(diagnostic.resourceHash) +
                    L" handle-hash=" +
                    std::to_wstring(diagnostic.handleHash) +
                    L" content-type=" + std::wstring{contentType} +
                    L" dimensions=" + std::to_wstring(diagnostic.width) +
                    L"x" + std::to_wstring(diagnostic.height) +
                    L" visible-alpha=" +
                    (diagnostic.hasVisibleAlpha ? L"true" : L"false"));
            };
            renderDiagnostic = [this](const std::wstring_view message) {
                PublishArtworkRenderDiagnostic(std::wstring{message});
            };
            AppendDiagnostic(
                L"Artwork render diagnostics enabled record-limit=" +
                std::to_wstring(kMaximumArtworkRenderDiagnosticRecords));
        }
        imageCache_ = std::make_unique<widgetrail::RemoteImageCache>(
            imageLimits,
            [this](const std::wstring_view source, const widgetrail::RemoteImageState state) {
                constexpr std::wstring_view prefix = L"wrail-artwork\x1f";
                if (state == widgetrail::RemoteImageState::Failed && source.starts_with(prefix)) {
                    const auto widgetEnd = source.find(L'\x1f', prefix.size());
                    const auto handleStart = source.rfind(L'\x1f');
                    if (widgetEnd != std::wstring_view::npos &&
                        handleStart != std::wstring_view::npos &&
                        handleStart > widgetEnd) {
                        AppendDiagnostic(
                            L"Trusted artwork unavailable widget=" +
                            std::wstring(source.substr(
                                prefix.size(), widgetEnd - prefix.size())) +
                            L" handle=" + std::wstring(source.substr(handleStart + 1)) +
                            L" state=terminal");
                    }
                }
                if (window_) PostMessageW(window_, kImageReadyMessage, 0, 0);
            },
            widgetrail::RemoteImageCache::FetchFunction{},
            [this](
                std::wstring_view source,
                const widgetrail::TrustedArtworkDemandAuthority& authority,
                const std::stop_token stopToken) {
                constexpr std::wstring_view prefix = L"wrail-artwork\x1f";
                const auto widgetSeparator = source.find(L'\x1f', prefix.size());
                const auto handleSeparator = source.rfind(L'\x1f');
                if (!source.starts_with(prefix) || widgetSeparator == std::wstring_view::npos ||
                    handleSeparator == widgetSeparator)
                    return widgetrail::TrustedArtworkRequestDisposition::TerminalFailure;
                const auto widgetId = source.substr(
                    prefix.size(), widgetSeparator - prefix.size());
                const auto handle = source.substr(handleSeparator + 1);
                if (widgetId != authority.widgetId)
                    return widgetrail::TrustedArtworkRequestDisposition::TerminalFailure;
                const auto disposition = bridge_.RequestArtwork(
                    widgetId, handle, authority.runtimeGeneration,
                    authority.presentationGeneration, stopToken);
                switch (disposition) {
                case widgetrail::WidgetArtworkRequestDisposition::Accepted:
                    return widgetrail::TrustedArtworkRequestDisposition::Accepted;
                case widgetrail::WidgetArtworkRequestDisposition::OriginRetired:
                    return widgetrail::TrustedArtworkRequestDisposition::OriginRetired;
                case widgetrail::WidgetArtworkRequestDisposition::Cancelled:
                    return widgetrail::TrustedArtworkRequestDisposition::OriginRetired;
                case widgetrail::WidgetArtworkRequestDisposition::TerminalFailure:
                    return widgetrail::TrustedArtworkRequestDisposition::TerminalFailure;
                }
                return widgetrail::TrustedArtworkRequestDisposition::TerminalFailure;
            },
            std::move(decodeDiagnostic));
        declarativeRenderer_ = std::make_unique<widgetrail::DeclarativeRenderer>(
            d2dFactory_.Get(), writeFactory_.Get(), imageCache_.get(),
            std::move(renderDiagnostic));
        const auto bitmapLimits = declarativeRenderer_->GetImageBitmapCacheStats();
        AppendDiagnostic(
            L"Image cache policy lifetime=process metadata-limit=" +
            std::to_wstring(imageLimits.maximumEntries) + L" ready-limit=" +
            std::to_wstring(imageLimits.maximumReadyEntries) + L" pending-limit=" +
            std::to_wstring(imageLimits.maximumPendingEntries) +
            L" decoded-entry-byte-limit=" +
            std::to_wstring(imageLimits.maximumDecodedImageBytes) +
            L" decoded-cache-byte-limit=" +
            std::to_wstring(imageLimits.maximumDecodedBytes) +
            L" bitmap-entry-limit=" +
            std::to_wstring(bitmapLimits.maximumEntries) +
            L" bitmap-entry-byte-limit=" +
            std::to_wstring(bitmapLimits.maximumEntryBytes) +
            L" bitmap-cache-byte-limit=" +
            std::to_wstring(bitmapLimits.maximumBytes));
        std::wstring pinnedSurfaceError;
        if (!pinnedSurfaceCoordinator_.Initialize(
                instance_, window_, kPinnedSurfaceChangedMessage,
                d2dFactory_.Get(), writeFactory_.Get(), imageCache_.get(),
                pinnedSurfaceError)) {
            initializationError_ = std::move(pinnedSurfaceError);
            return false;
        }
        pinnedSurfaceCoordinator_.SetBeforeWindowRetirement(
            [this](const widgetrail::pinned::WidgetSurfaceStopReason reason) {
                HandlePinnedSurfaceWindowRetirement(reason);
            });
        if (!developmentProbeOnly_) {
            RegisterHotKey(window_, kDeveloperHotkey, MOD_NOREPEAT, VK_F1);
            if (WidgetRailOverlayPlatformInitialize(platform_) !=
                WidgetRailOverlayPlatformStatus::Ok) {
                initializationError_ =
                    L"Unable to initialize the native platform boundary.";
                return false;
            }
            if (WidgetRailOverlayPlatformRequiresLegacyGuidePolling(platform_) !=
                WRAIL_OVERLAY_PLATFORM_FALSE) {
                SetTimer(window_, kGuideCompatibilityTimer, 25, nullptr);
            }
        }

        // Platform appearance is bridge-owned but does not cross the lazy
        // widget-worker boundary. Fetch it once at host startup, then only in
        // response to a revision event.
        bool developmentCatalogReady = false;
        std::wstring bridgeStartupFailure;
        if (bridge_.EnsureStarted(
                installationDirectory_, developmentCatalogRoot_.value_or(L""))) {
            AppendDiagnostic(
                L"WidgetBridge startup stage=pipe-ready pid=" +
                std::to_wstring(bridge_.lastStartupProcessId()) +
                L" bridge-session=" +
                std::to_wstring(bridge_.bridgeSessionGeneration()));
            RefreshPlatformAppearance();
            if (auto change = sessions_.EstablishCatalog()) {
                ApplyWidgetCatalogChange(*change);
                developmentCatalogReady = true;
            } else {
                bridgeStartupFailure = bridge_.lastError();
                AppendDiagnostic(
                    L"WidgetBridge startup stage=catalog-admission result=failed pid=" +
                    std::to_wstring(bridge_.lastStartupProcessId()) + L" error=" +
                    bridgeStartupFailure);
            }
        } else {
            bridgeStartupFailure = bridge_.lastError();
            AppendDiagnostic(
                L"WidgetBridge startup stage=pipe-readiness result=failed pid=" +
                std::to_wstring(bridge_.lastStartupProcessId()) + L" error=" +
                bridgeStartupFailure);
            AppendDiagnostic(L"Platform appearance unavailable at startup: " +
                             bridgeStartupFailure);
        }
        if (developmentReadyPath_) {
            if (!developmentCatalogReady || !ExpectedDevelopmentWidgetPresent()) {
                initializationError_ =
                    L"Development bridge catalog did not contain the expected package generation.";
                return false;
            }
            if (!ProbeExpectedDevelopmentWidget()) return false;
            if (developmentProbeOnly_ && !PublishDevelopmentReady()) return false;
        }

        (void)showCommand;
        if (performanceState_) {
            if (!ApplyPerformanceStartupState()) return false;
            return true;
        }
        bool startShown = false;
        for (int i = 1; i < __argc; ++i) {
            if (_wcsicmp(__wargv[i], L"--show") == 0) {
                startShown = true;
            } else if (_wcsicmp(__wargv[i], L"--hidden") == 0) {
                startShown = false;
            }
        }
        if (richMediaProof_) startShown = true;
        if (startShown && !developmentProbeOnly_) {
            if (developmentCatalogRoot_) {
                Dispatch(widgetrail::Command::ToggleOverlay);
            } else {
                bool opened = false;
                ApplyStateTransition([&] {
                    opened = state_.OpenWidgetWithTrayFocus(kStartupWidgetId);
                    return opened;
                });
                if (!opened) {
                    initializationError_ = widgetrail::ProjectStartupSettingsFailure(
                        bridgeStartupFailure);
                    return false;
                }
            }
        }
        if (developmentReadyPath_ && !developmentProbeOnly_) {
            if (!startShown) {
                initializationError_ =
                    L"An interactive development readiness handshake requires --show.";
                return false;
            }
            if (!InteractiveDevelopmentHostReady()) return false;
            if (!PublishDevelopmentReady()) return false;
        }
        if (richMediaProof_ && !InitializeRichMediaProof()) return false;
        return true;
    }

    int Run() {
        MSG message{};
        while (GetMessageW(&message, nullptr, 0, 0) > 0) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        return static_cast<int>(message.wParam);
    }

    void BindProcessActivation(widgetrail::process::OverlayProcessOwner& owner) noexcept {
        owner.BindNotificationWindow(window_, kProcessActivationMessage);
    }


private:
    enum class ResidentShowAction {
        HiddenDispatch,
        VisibleRecovery,
    };

    struct ResidentShowWindowState final {
        bool valid{};
        bool visible{};
        bool topmost{};
        bool hasBounds{};
        RECT bounds{};
    };

    struct ResidentShowPresentationState final {
        widgetrail::Surface logicalSurface{widgetrail::Surface::Hidden};
        ResidentShowWindowState content;
        ResidentShowWindowState chrome;
        ResidentShowWindowState backdrop;
        bool foregroundOwned{};
        bool compositionAvailable{};
        bool transitionActive{};
        float shellOpacity{};
        bool chromeAboveContent{};
        bool contentAboveBackdrop{};
    };

    struct PendingWidgetSwitchSnap final {
        std::wstring widgetId;
        std::wstring runtimeGeneration;
        std::wstring presentationGeneration;
        std::uint64_t correlationId{};
    };

    struct TextEntryModalAuthority final {
        std::wstring widgetId;
        std::wstring runtimeGeneration;
        std::wstring presentationGeneration;
    };

    enum class TextEntryControllerPhase {
        None,
        AwaitingEntryNeutral,
        Active,
        AwaitingExitNeutral,
    };

    struct WidgetSnapshotAdmissionAuthority final {
        std::wstring_view widgetId;
        std::wstring_view runtimeGeneration;
        std::wstring_view presentationGeneration;
        std::uint64_t correlationId{};
    };

    void PublishArtworkRenderDiagnostic(std::wstring message) {
        if (!artworkRenderDiagnostics_) return;
        auto count = artworkRenderDiagnosticRecordCount_.load(
            std::memory_order_relaxed);
        while (count < kMaximumArtworkRenderDiagnosticRecords &&
               !artworkRenderDiagnosticRecordCount_.compare_exchange_weak(
                   count, count + 1,
                   std::memory_order_relaxed,
                   std::memory_order_relaxed)) {
        }
        if (count >= kMaximumArtworkRenderDiagnosticRecords) return;
        AppendDiagnostic(L"Artwork render diagnostic " + std::move(message));
    }

    bool ParseDevelopmentArguments() {
        auto takeValue = [&](const int& index, std::optional<std::wstring>& target,
                             const wchar_t* label) -> bool {
            if (target || index + 1 >= __argc || !__wargv[index + 1] ||
                __wargv[index + 1][0] == L'\0' ||
                wcsncmp(__wargv[index + 1], L"--", 2) == 0) {
                initializationError_ = std::wstring(label) + L" requires one value.";
                return false;
            }
            target = __wargv[index + 1];
            return true;
        };
        for (int i = 1; i < __argc; ++i) {
            if (_wcsicmp(__wargv[i], L"--development-catalog-root") == 0) {
                if (!takeValue(i, developmentCatalogRoot_, L"--development-catalog-root")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--development-ready-path") == 0) {
                if (!takeValue(i, developmentReadyPath_, L"--development-ready-path")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--development-ready-nonce") == 0) {
                if (!takeValue(i, developmentReadyNonce_, L"--development-ready-nonce")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--development-widget-id") == 0) {
                if (!takeValue(i, developmentWidgetId_, L"--development-widget-id")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--development-widget-instance") == 0) {
                if (!takeValue(i, developmentWidgetInstance_, L"--development-widget-instance")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--development-probe-only") == 0) {
                if (developmentProbeOnly_) {
                    initializationError_ = L"--development-probe-only was supplied more than once.";
                    return false;
                }
                developmentProbeOnly_ = true;
            } else if (_wcsicmp(__wargv[i], L"--performance-state") == 0) {
                if (!takeValue(i, performanceState_, L"--performance-state")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--performance-widget-id") == 0) {
                if (!takeValue(i, performanceWidgetId_, L"--performance-widget-id")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--performance-diagnostics-path") == 0) {
                if (!takeValue(i, performanceDiagnosticsPath_, L"--performance-diagnostics-path")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--performance-diagnostics-nonce") == 0) {
                if (!takeValue(i, performanceDiagnosticsNonce_, L"--performance-diagnostics-nonce")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--scroll-evidence-path") == 0) {
                if (!takeValue(i, scrollEvidencePath_, L"--scroll-evidence-path")) return false;
                ++i;
            } else if (_wcsicmp(__wargv[i], L"--rich-media-proof") == 0) {
                if (richMediaProof_) {
                    initializationError_ = L"--rich-media-proof was supplied more than once.";
                    return false;
                }
                richMediaProof_ = true;
            } else if (_wcsicmp(__wargv[i], L"--artwork-render-diagnostics") == 0) {
                if (artworkRenderDiagnostics_) {
                    initializationError_ =
                        L"--artwork-render-diagnostics was supplied more than once.";
                    return false;
                }
                artworkRenderDiagnostics_ = true;
            }
        }
        try {
            if (developmentCatalogRoot_)
                developmentCatalogRoot_ = std::filesystem::absolute(*developmentCatalogRoot_).wstring();
            if (developmentReadyPath_)
                developmentReadyPath_ = std::filesystem::absolute(*developmentReadyPath_).wstring();
            if (performanceDiagnosticsPath_)
                performanceDiagnosticsPath_ =
                    std::filesystem::absolute(*performanceDiagnosticsPath_).wstring();
            if (scrollEvidencePath_)
                scrollEvidencePath_ =
                    std::filesystem::absolute(*scrollEvidencePath_).wstring();
        } catch (const std::filesystem::filesystem_error&) {
            initializationError_ = L"A development path is invalid.";
            return false;
        }
        const bool hasHandshake = developmentReadyPath_ || developmentReadyNonce_ ||
                                  developmentWidgetId_ || developmentWidgetInstance_;
        if (hasHandshake && (!developmentCatalogRoot_ || !developmentReadyPath_ ||
                             !developmentReadyNonce_ || !developmentWidgetId_ ||
                             !developmentWidgetInstance_)) {
            initializationError_ = L"Development readiness arguments must be supplied together.";
            return false;
        }
        if (developmentProbeOnly_ && !hasHandshake) {
            initializationError_ = L"A development probe requires an authenticated readiness handshake.";
            return false;
        }
        if (scrollEvidencePath_ && !hasHandshake) {
            initializationError_ =
                L"Scroll evidence requires an authenticated development readiness handshake.";
            return false;
        }
        if (developmentReadyNonce_ &&
            (developmentReadyNonce_->size() != 64 ||
             !std::all_of(developmentReadyNonce_->begin(), developmentReadyNonce_->end(),
                          [](const wchar_t value) {
                              return (value >= L'0' && value <= L'9') ||
                                     (value >= L'a' && value <= L'f') ||
                                     (value >= L'A' && value <= L'F');
                          }))) {
            initializationError_ = L"The development readiness nonce is invalid.";
            return false;
        }
        const auto validIdentity = [](const std::optional<std::wstring>& value) {
            return value && !value->empty() && value->size() <= 128 &&
                   std::all_of(value->begin(), value->end(), [](const wchar_t character) {
                       return (character >= L'a' && character <= L'z') ||
                              (character >= L'A' && character <= L'Z') ||
                              (character >= L'0' && character <= L'9') ||
                              character == L'-' || character == L'_' || character == L'.';
                   });
        };
        if (hasHandshake &&
            (!validIdentity(developmentWidgetId_) || !validIdentity(developmentWidgetInstance_))) {
            initializationError_ = L"The expected development widget identity is invalid.";
            return false;
        }
        const bool hasPerformanceArguments = performanceState_ || performanceWidgetId_ ||
                                             performanceDiagnosticsPath_ ||
                                             performanceDiagnosticsNonce_;
        if (hasPerformanceArguments &&
            (!performanceState_ || !performanceWidgetId_ ||
             !performanceDiagnosticsPath_ || !performanceDiagnosticsNonce_)) {
            initializationError_ =
                L"Performance diagnostics arguments must be supplied together.";
            return false;
        }
        if (hasPerformanceArguments && (hasHandshake || developmentProbeOnly_)) {
            initializationError_ =
                L"Performance diagnostics cannot be combined with development readiness.";
            return false;
        }
        if (performanceState_) {
            std::transform(performanceState_->begin(), performanceState_->end(),
                           performanceState_->begin(), towlower);
            if (*performanceState_ != L"hidden" && *performanceState_ != L"visible" &&
                *performanceState_ != L"interactive") {
                initializationError_ =
                    L"--performance-state must be hidden, visible, or interactive.";
                return false;
            }
            if (!validIdentity(performanceWidgetId_)) {
                initializationError_ = L"The performance widget identity is invalid.";
                return false;
            }
            if (performanceDiagnosticsNonce_->size() != 64 ||
                !std::all_of(performanceDiagnosticsNonce_->begin(),
                             performanceDiagnosticsNonce_->end(),
                             [](const wchar_t value) {
                                 return (value >= L'0' && value <= L'9') ||
                                        (value >= L'a' && value <= L'f') ||
                                        (value >= L'A' && value <= L'F');
                             })) {
                initializationError_ = L"The performance diagnostics nonce is invalid.";
                return false;
            }
        }
        if (scrollEvidencePath_) {
            if (!scrollEvidenceProbe_.Enable(*scrollEvidencePath_)) {
                initializationError_ = L"The scroll evidence destination is invalid.";
                return false;
            }
            scrollEvidencePath_.reset();
        }
        return true;
    }

    bool ApplyPerformanceStartupState() {
        if (!performanceState_ || !performanceWidgetId_) return false;
        if (*performanceState_ == L"hidden") return true;
        if (state_.order().empty()) {
            initializationError_ = L"Performance diagnostics found no runnable widgets.";
            return false;
        }

        Dispatch(widgetrail::Command::ToggleOverlay);
        if (state_.surface() == widgetrail::Surface::Widget &&
            state_.focusRegion() == widgetrail::FocusRegion::Widget) {
            Dispatch(widgetrail::Command::SampleWidgetBack);
        }
        for (std::size_t remaining = state_.order().size();
             remaining > 0 && state_.selectedWidget() != *performanceWidgetId_;
             --remaining) {
            Dispatch(widgetrail::Command::NavigateRight);
        }
        if (state_.selectedWidget() != *performanceWidgetId_) {
            initializationError_ = L"The requested performance widget is not installed and enabled.";
            return false;
        }
        if (*performanceState_ == L"interactive") {
            Dispatch(widgetrail::Command::Activate);
        }

        const auto expected = *performanceState_ == L"interactive"
            ? widgetrail::WidgetLifecycleState::Interactive
            : widgetrail::WidgetLifecycleState::Visible;
        const auto deadline = std::chrono::steady_clock::now() +
            std::chrono::seconds(5);
        do {
            ProcessWidgetSessionEvents();
            const auto lifecycle = sessions_.Lifecycle(*performanceWidgetId_);
            if (lifecycle && *lifecycle == expected) return true;
            Sleep(5);
        } while (std::chrono::steady_clock::now() < deadline);
        ProcessWidgetSessionEvents();
        const auto lifecycle = sessions_.Lifecycle(*performanceWidgetId_);
        if (!lifecycle || *lifecycle != expected) {
            initializationError_ =
                L"The requested performance lifecycle could not be established.";
            return false;
        }
        return true;
    }

    void ResetPerformanceCounters() noexcept {
        if (!performanceState_) return;
        performanceTimerMessages_ = 0;
        performanceControllerTimerMessages_ = 0;
        performanceGuideCompatibilityTimerMessages_ = 0;
        performancePaintMessages_ = 0;
        performanceSuccessfulFrames_ = 0;
        LARGE_INTEGER now{};
        performanceCountersActive_ = QueryPerformanceCounter(&now) != FALSE;
        performanceCounterStarted_ = performanceCountersActive_
            ? static_cast<unsigned long long>(now.QuadPart)
            : 0;
    }

    bool PublishPerformanceCounters() {
        if (!performanceCountersActive_ || !performanceState_ ||
            !performanceWidgetId_ || !performanceDiagnosticsPath_ ||
            !performanceDiagnosticsNonce_) {
            return false;
        }
        LARGE_INTEGER ended{};
        LARGE_INTEGER frequency{};
        if (!QueryPerformanceCounter(&ended) || !QueryPerformanceFrequency(&frequency) ||
            ended.QuadPart < 0 || frequency.QuadPart <= 0) {
            return false;
        }
        const auto ascii = [](const std::wstring& value) {
            std::string result;
            result.reserve(value.size());
            for (const wchar_t character : value) {
                result.push_back(static_cast<char>(character));
            }
            return result;
        };
        const std::string payload =
            "wrail-performance-runtime-v2\n" +
            ascii(*performanceDiagnosticsNonce_) + "\n" +
            ascii(*performanceState_) + "\n" +
            ascii(*performanceWidgetId_) + "\n" +
            std::to_string(performanceCounterStarted_) + "\n" +
            std::to_string(static_cast<unsigned long long>(ended.QuadPart)) + "\n" +
            std::to_string(static_cast<unsigned long long>(frequency.QuadPart)) + "\n" +
            std::to_string(performanceTimerMessages_) + "\n" +
            std::to_string(performanceControllerTimerMessages_) + "\n" +
            std::to_string(performanceGuideCompatibilityTimerMessages_) + "\n" +
            std::to_string(performancePaintMessages_) + "\n" +
            std::to_string(performanceSuccessfulFrames_) + "\n";

        const std::filesystem::path destination(*performanceDiagnosticsPath_);
        const auto temporary = destination.wstring() + L".tmp-" +
                               std::to_wstring(GetCurrentProcessId());
        HANDLE file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr,
                                  CREATE_NEW, FILE_ATTRIBUTE_TEMPORARY, nullptr);
        if (file == INVALID_HANDLE_VALUE) return false;
        DWORD written = 0;
        const bool wrote = payload.size() <= MAXDWORD &&
                           WriteFile(file, payload.data(),
                                     static_cast<DWORD>(payload.size()),
                                     &written, nullptr) &&
                           written == static_cast<DWORD>(payload.size()) &&
                           FlushFileBuffers(file);
        CloseHandle(file);
        if (!wrote || !MoveFileExW(temporary.c_str(), destination.c_str(),
                                   MOVEFILE_WRITE_THROUGH)) {
            DeleteFileW(temporary.c_str());
            return false;
        }
        performanceCountersActive_ = false;
        return true;
    }

    bool ExpectedDevelopmentWidgetPresent() const noexcept {
        if (!developmentWidgetId_ || !developmentWidgetInstance_) return false;
        return std::any_of(sessions_.descriptors().begin(), sessions_.descriptors().end(),
                           [&](const widgetrail::WidgetDescriptor& descriptor) {
                               return descriptor.id == *developmentWidgetId_ &&
                                      descriptor.instanceId == *developmentWidgetInstance_;
                           });
    }

    bool ProbeExpectedDevelopmentWidget() {
        if (!developmentWidgetId_ || !developmentWidgetInstance_) return false;
        auto snapshot = sessions_.EstablishPresentationForProbe(
            *developmentWidgetId_, widgetrail::WidgetLifecycleState::Visible);
        if (!snapshot) {
            initializationError_ =
                L"Development widget worker did not complete the visible snapshot probe.";
            return false;
        }
        if (snapshot->instanceId != *developmentWidgetInstance_) {
            initializationError_ =
                L"Development widget worker returned a snapshot for the wrong package generation.";
            return false;
        }
        return true;
    }

    bool InteractiveDevelopmentHostReady() {
        if (state_.surface() == widgetrail::Surface::Hidden || !IsWindow(window_) ||
            !IsWindow(backdropWindow_) || !IsWindowVisible(window_) ||
            !IsWindowVisible(backdropWindow_)) {
            initializationError_ =
                L"Development overlay did not complete its visible window transition.";
            return false;
        }
        if (!sessions_.EstablishCatalog()) {
            initializationError_ =
                L"Development bridge did not remain responsive after showing the overlay.";
            return false;
        }
        if (!std::any_of(sessions_.descriptors().begin(), sessions_.descriptors().end(),
                         [&](const widgetrail::WidgetDescriptor& descriptor) {
                             return descriptor.id == *developmentWidgetId_ &&
                                    descriptor.instanceId == *developmentWidgetInstance_;
                         })) {
            initializationError_ =
                L"Development bridge changed package generation while showing the overlay.";
            return false;
        }
        return true;
    }

    bool PublishDevelopmentReady() {
        const std::wstring widePayload =
            L"wrail-dev-ready-v1\n" + *developmentReadyNonce_ + L"\n" +
            *developmentCatalogRoot_ + L"\n" + *developmentWidgetId_ + L"\n" +
            *developmentWidgetInstance_ + L"\n";
        if (widePayload.size() > static_cast<std::size_t>(INT_MAX)) {
            initializationError_ = L"The development readiness record is too large.";
            return false;
        }
        const int utf8Length = WideCharToMultiByte(
            CP_UTF8, WC_ERR_INVALID_CHARS, widePayload.data(),
            static_cast<int>(widePayload.size()), nullptr, 0, nullptr, nullptr);
        if (utf8Length <= 0) {
            initializationError_ = L"Could not encode the development readiness record.";
            return false;
        }
        std::string payload(static_cast<std::size_t>(utf8Length), '\0');
        if (WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, widePayload.data(),
                                static_cast<int>(widePayload.size()), payload.data(), utf8Length,
                                nullptr, nullptr) != utf8Length) {
            initializationError_ = L"Could not encode the development readiness record.";
            return false;
        }
        const std::filesystem::path destination(*developmentReadyPath_);
        const auto temporary = destination.wstring() + L".tmp-" +
                               std::to_wstring(GetCurrentProcessId());
        HANDLE file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
                                  FILE_ATTRIBUTE_TEMPORARY, nullptr);
        if (file == INVALID_HANDLE_VALUE) {
            initializationError_ = L"Could not create the development readiness record.";
            return false;
        }
        DWORD written = 0;
        const bool wrote = payload.size() <= MAXDWORD &&
                           WriteFile(file, payload.data(), static_cast<DWORD>(payload.size()),
                                     &written, nullptr) &&
                           written == static_cast<DWORD>(payload.size()) &&
                           FlushFileBuffers(file);
        CloseHandle(file);
        if (!wrote || !MoveFileExW(temporary.c_str(), destination.c_str(), MOVEFILE_WRITE_THROUGH)) {
            DeleteFileW(temporary.c_str());
            initializationError_ = L"Could not publish the development readiness record.";
            return false;
        }
        return true;
    }

    bool FailWin32(const std::wstring_view operation, const DWORD error) {
        initializationError_ = std::wstring(operation) + L" failed with Win32 error " +
                               std::to_wstring(error) + L".";
        return false;
    }

    bool FailHresult(const std::wstring_view operation, const HRESULT result) {
        initializationError_ = std::wstring(operation) + L" failed with HRESULT " +
                               std::to_wstring(static_cast<unsigned long>(result)) + L".";
        return false;
    }

    static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
        OverlayApp* app = nullptr;
        if (message == WM_NCCREATE) {
            const auto create = reinterpret_cast<CREATESTRUCTW*>(lParam);
            app = static_cast<OverlayApp*>(create->lpCreateParams);
            app->window_ = window;
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(app));
        } else {
            app = reinterpret_cast<OverlayApp*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        }
        return app ? app->HandleMessage(message, wParam, lParam)
                   : DefWindowProcW(window, message, wParam, lParam);
    }

    static LRESULT CALLBACK BackdropWindowProc(
        HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
        OverlayApp* app = nullptr;
        if (message == WM_NCCREATE) {
            const auto create = reinterpret_cast<CREATESTRUCTW*>(lParam);
            app = static_cast<OverlayApp*>(create->lpCreateParams);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(app));
        } else {
            app = reinterpret_cast<OverlayApp*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        }
        if (!app) return DefWindowProcW(window, message, wParam, lParam);
        switch (message) {
        case WM_LBUTTONDOWN:
        case WM_LBUTTONUP:
            if (message == WM_LBUTTONUP && app->state_.surface() != widgetrail::Surface::Hidden) {
                app->Dispatch(widgetrail::Command::ToggleOverlay);
            }
            return 0;
        case WM_ERASEBKGND:
            return 1;
        case WM_PAINT: {
            PAINTSTRUCT paint{};
            const HDC dc = BeginPaint(window, &paint);
            FillRect(dc, &paint.rcPaint,
                     app->backdropBrush_ ? app->backdropBrush_
                                         : static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH)));
            EndPaint(window, &paint);
            return 0;
        }
        default:
            return DefWindowProcW(window, message, wParam, lParam);
        }
    }

    static LRESULT CALLBACK ChromeWindowProc(
        HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
        OverlayApp* app = nullptr;
        if (message == WM_NCCREATE) {
            const auto create = reinterpret_cast<CREATESTRUCTW*>(lParam);
            app = static_cast<OverlayApp*>(create->lpCreateParams);
            app->chromeWindow_ = window;
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(app));
        } else {
            app = reinterpret_cast<OverlayApp*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        }
        if (!app) return DefWindowProcW(window, message, wParam, lParam);
        switch (message) {
        case WM_NCHITTEST: {
            POINT point{static_cast<LONG>(static_cast<short>(LOWORD(lParam))),
                        static_cast<LONG>(static_cast<short>(HIWORD(lParam)))};
            if (!app->IsCurrentFixedChromeHit(point))
                return HTTRANSPARENT;
            return HTCLIENT;
        }
        case WM_ERASEBKGND:
            return 1;
        case WM_PAINT: {
            PAINTSTRUCT paint{};
            BeginPaint(window, &paint);
            EndPaint(window, &paint);
            return 0;
        }
        case WM_LBUTTONUP: {
            (void)widgetrail::shell::RouteFixedChromePointerRelease(
                window, app->window_, lParam, app,
                [](void* context, const float x, const float y) noexcept {
                    static_cast<OverlayApp*>(context)->HandlePointerActivation(x, y);
                });
            return 0;
        }
        case WM_RBUTTONUP: {
            (void)widgetrail::shell::RouteFixedChromePointerRelease(
                window, app->window_, lParam, app,
                [](void* context, const float x, const float y) noexcept {
                    static_cast<OverlayApp*>(context)->HandlePointerActivation(x, y, true);
                });
            return 0;
        }
        case WM_SHOWWINDOW:
            app->chromeAccessibilityProvider_.SetWindowVisible(wParam != FALSE);
            return DefWindowProcW(window, message, wParam, lParam);
        case WM_GETOBJECT:
            if (static_cast<LONG>(lParam) == UiaRootObjectId) {
                if (!app->accessibilityActive_) {
                    app->accessibilityActive_ = true;
                    InvalidateRect(app->window_, nullptr, FALSE);
                    UpdateWindow(app->window_);
                }
                return app->chromeAccessibilityProvider_.HandleWmGetObject(wParam, lParam);
            }
            return DefWindowProcW(window, message, wParam, lParam);
        case kAccessibilityActionMessage:
            app->HandleAccessibilityActions();
            return 0;
        default:
            return DefWindowProcW(window, message, wParam, lParam);
        }
    }

    static void WRAIL_OVERLAY_PLATFORM_CALL OnPlatformEventAvailable(
        void* context) noexcept {
        const auto app = static_cast<OverlayApp*>(context);
        if (app && app->window_) {
            (void)PostMessageW(app->window_, kPlatformEventMessage, 0, 0);
        }
    }

    static void WRAIL_OVERLAY_PLATFORM_CALL OnPlatformDiagnostic(
        void*,
        const wchar_t* message) noexcept {
        if (message) AppendDiagnostic(message);
    }

    void HandlePlatformEvent(const WidgetRailOverlayPlatformEvent& event) {
        switch (event.kind) {
        case WidgetRailOverlayPlatformEventKind::GuideToggleRequested:
            if (textEntryModal_.active()) {
                AppendDiagnostic(L"Guide input consumed by text entry modal");
                break;
            }
            if (event.guideSource ==
                WidgetRailOverlayPlatformGuideSource::LegacyCompatibility) {
                AppendDiagnostic(
                    L"Guide press received from quarantined XInput compatibility adapter; slots=" +
                    std::to_wstring(event.value));
            }
            AppendDiagnostic(L"Guide toggle dispatched on window thread");
            Dispatch(widgetrail::Command::ToggleOverlay);
            break;
        case WidgetRailOverlayPlatformEventKind::LegacyGuidePollingChanged:
            if (event.value != 0) {
                SetTimer(window_, kGuideCompatibilityTimer, 25, nullptr);
                AppendDiagnostic(
                    L"XInput Guide compatibility polling activated for a connected Xbox 360-family device");
            } else {
                KillTimer(window_, kGuideCompatibilityTimer);
                AppendDiagnostic(
                    L"XInput Guide compatibility polling stopped after the last Xbox 360-family device disconnected");
            }
            break;
        case WidgetRailOverlayPlatformEventKind::VisibilityChanged:
        case WidgetRailOverlayPlatformEventKind::FocusChanged:
        case WidgetRailOverlayPlatformEventKind::None:
            break;
        }
    }

    void HandlePlatformEvents() {
        if (!platform_) return;
        for (;;) {
            WidgetRailOverlayPlatformEvent event;
            std::uint32_t hasEvent = WRAIL_OVERLAY_PLATFORM_FALSE;
            if (WidgetRailOverlayPlatformDrainEvent(
                    platform_, GetTickCount64(), &event, &hasEvent) !=
                    WidgetRailOverlayPlatformStatus::Ok ||
                hasEvent == WRAIL_OVERLAY_PLATFORM_FALSE) {
                return;
            }
            HandlePlatformEvent(event);
        }
    }

    static void CALLBACK OnForegroundChanged(
        HWINEVENTHOOK,
        DWORD event,
        HWND foregroundWindow,
        LONG,
        LONG,
        DWORD,
        DWORD) {
        auto* app = foregroundEventApp_;
        if (event == EVENT_SYSTEM_FOREGROUND && app && app->window_) {
            PostMessageW(app->window_, kForegroundChangedMessage, 0,
                         reinterpret_cast<LPARAM>(foregroundWindow));
        }
    }

    void PumpBridgeEvents(const bool controllerTick) {
        // This order is an authority contract: drain transport frames before
        // dispatching failures, input, resources, terminals, revisions,
        // presentation demand, action feedback, and host effects. The hidden
        // control-plane timer fills these queues but deliberately does not
        // consume them until a visible or pinned presentation tick.
        (void)bridge_.PumpEvents();
        for (auto& failure : bridge_.TakeRuntimeFailures()) {
            if (!sessions_.Contains(failure.widgetId)) {
                AppendDiagnostic(
                    L"Dropped stale widget runtime failure for " +
                    failure.widgetId);
                continue;
            }
            RecordWidgetStartupFailure(
                failure.widgetId,
                failure.safeMessage,
                failure.category ==
                        widgetrail::WidgetBridgeRuntimeFailureCategory::WorkerStart
                    ? widgetrail::WidgetSessionFailureStage::Start
                    : widgetrail::WidgetSessionFailureStage::Lifecycle,
                true);
            if (pinnedSurfaceCoordinator_.pinned() &&
                pinnedSurfaceCoordinator_.widgetId() == failure.widgetId) {
                (void)pinnedSurfaceCoordinator_.Unpin(
                    widgetrail::pinned::WidgetSurfaceStopReason::WorkerUnavailable);
            }
        }
        if (controllerTick && state_.surface() != widgetrail::Surface::Hidden)
            PollController();
        for (auto& artwork : bridge_.TakeArtworkResults()) {
            const widgetrail::TrustedArtworkDemandAuthority authority{
                artwork.widgetId,
                artwork.runtimeGeneration,
                artwork.presentationGeneration};
            const auto* descriptor = sessions_.FindDescriptor(artwork.widgetId);
            if (!descriptor ||
                descriptor->runtimeGeneration != artwork.runtimeGeneration ||
                descriptor->presentationGeneration != artwork.presentationGeneration) {
                (void)imageCache_->RetireTrustedArtworkDemand(
                    artwork.widgetId, artwork.artworkHandle, authority);
                continue;
            }
            if (artwork.contentBase64.empty())
                (void)imageCache_->FailTrustedArtwork(
                    artwork.widgetId, artwork.artworkHandle, authority);
            else
            {
                if (!imageCache_->SupplyTrustedArtwork(
                        artwork.widgetId, artwork.artworkHandle,
                        authority,
                        std::move(artwork.contentType),
                        std::move(artwork.contentBase64)))
                    (void)imageCache_->FailTrustedArtwork(
                        artwork.widgetId, artwork.artworkHandle, authority);
            }
        }
        for (auto& result : bridge_.TakeLocalWidgetPackageInstallResults()) {
            if (!localWidgetPackageImport_.Complete(
                    result.operationId,
                    bridge_.bridgeSessionGeneration())) {
                AppendDiagnostic(
                    L"Dropped stale local widget package result operation=" +
                    result.operationId);
                continue;
            }
            lastLocalWidgetPackageInstallResult_ = result;
            if (result.status != widgetrail::LocalWidgetPackageInstallStatus::Cancelled) {
                lastActionWidgetId_ = L"settings";
                lastActionMessage_ = result.safeMessage;
                lastActionExpiresAt_ = GetTickCount64() + 5000;
                AppendDiagnostic(L"Local widget package import: " +
                                 result.safeMessage);
            }
        }
        if (const auto revision = bridge_.TakePlatformAppearanceChangedRevision()) {
            const auto& current = appearanceState_.current();
            if (!current || *revision > current->revision) {
                RefreshPlatformAppearance();
            }
        }
        if (const auto revision = bridge_.TakeWidgetCatalogChangedRevision()) {
            sessions_.ResetCatalogRetry();
            AppendDiagnostic(L"Reconciling widget catalog revision " +
                             std::to_wstring(*revision));
            PostMessageW(window_, kCatalogRefreshMessage, 0, 0);
        }
        for (auto& invalidatedWidget : bridge_.TakeInvalidatedWidgetIds()) {
            sessions_.MarkRefreshRequested(invalidatedWidget);
            const auto currentWidget = state_.surface() == widgetrail::Surface::Widget
                ? state_.activeWidget()
                : state_.selectedWidget();
            const bool pinnedInvalidation =
                pinnedSurfaceCoordinator_.pinned() &&
                pinnedSurfaceCoordinator_.widgetId() == invalidatedWidget;
            if ((state_.surface() != widgetrail::Surface::Hidden &&
                 currentWidget == invalidatedWidget) || pinnedInvalidation) {
                RefreshAndApplyPresentation([&] {
                    RefreshWidgetSnapshot(invalidatedWidget);
                });
            } else {
                // Ordinary invalidation is refresh demand, not proof
                // that the last admitted semantic checkpoint is unsafe.
                // Keep the offscreen widget's own content and envelope;
                // selection will request current state without waking it here.
                renderedSnapshotSequences_.erase(invalidatedWidget);
            }
        }
        const auto actionFailures = bridge_.TakeActionFailures();
        for (const auto& failure : actionFailures)
            ObserveScrollPaginationFailure(failure);
        const auto actionFeedback =
            actionFailureFeedback_.PublishBridgeFailures(actionFailures);
        for (std::size_t index = 0; index < actionFeedback.count; ++index) {
            const auto& failure = actionFailures[index];
            const auto outcome = actionFeedback.OutcomeAt(index);
            if (outcome == widgetrail::WidgetActionFeedbackOutcome::Stale) {
                AppendDiagnostic(
                    L"Dropped stale widget action failure for " + failure.widgetId);
                continue;
            }
            if (outcome == widgetrail::WidgetActionFeedbackOutcome::Refused) {
                AppendDiagnostic(
                    L"Dropped widget action feedback at the bounded presentation seam for " +
                    failure.widgetId);
                continue;
            }
            // Completion failures carry source/action identity but no slider
            // intent generation. They can provide feedback, but cannot safely
            // roll back or consume one value from the bounded local history.
            AppendDiagnostic(
                L"Widget action failed: widget=" + failure.widgetId +
                L" generation=" + failure.runtimeGeneration +
                L" code=" +
                std::wstring(widgetrail::WidgetActionFailureCodeValue(failure.code)) +
                L" action=" + failure.actionId +
                L" source=" + failure.sourceElementId);
        }
        for (const auto& effect : bridge_.TakeHostEffects()) {
            const auto* descriptor = sessions_.FindDescriptor(effect.widgetId);
            const bool currentInteractiveWidget =
                state_.surface() == widgetrail::Surface::Widget &&
                state_.focusRegion() == widgetrail::FocusRegion::Widget &&
                state_.activeWidget() == effect.widgetId &&
                descriptor &&
                descriptor->runtimeGeneration == effect.runtimeGeneration;
            if (!currentInteractiveWidget) {
                AppendDiagnostic(
                    L"Dropped stale or non-interactive widget host effect for " +
                    effect.widgetId);
                continue;
            }
            if (effect.kind ==
                widgetrail::WidgetHostEffectKind::CloseOverlayAfterAppLaunch) {
                AppendDiagnostic(
                    L"Closing overlay after confirmed app launch from " +
                    effect.widgetId);
                Dispatch(widgetrail::Command::CloseOverlay);
                break;
            }
        }
        RecordPinnedSurfaceWorkCounters();
    }

    LRESULT HandleMessage(UINT message, WPARAM wParam, LPARAM lParam) {
        switch (message) {
#if defined(WRAIL_PINNED_SLIDER_ROUTE_TESTING)
        case WM_COPYDATA: {
            const auto* copy = reinterpret_cast<const COPYDATASTRUCT*>(lParam);
            if (!developmentReadyNonce_ || !developmentCatalogRoot_ || !copy ||
                copy->dwData != kPinnedSliderControllerFrameCopyData ||
                copy->cbData != sizeof(WidgetRailOverlayPlatformControllerFrame) ||
                !copy->lpData) {
                return FALSE;
            }
            const auto& frame = *static_cast<const WidgetRailOverlayPlatformControllerFrame*>(
                copy->lpData);
            if (frame.structSize != sizeof(frame) ||
                frame.abiVersion != WRAIL_OVERLAY_PLATFORM_ABI_VERSION) {
                return FALSE;
            }
            testControllerFrame_ = frame;
            PollController();
            return TRUE;
        }
#endif
        case WM_HOTKEY:
            if (textEntryModal_.active()) return 0;
            if (wParam == kDeveloperHotkey) {
                AppendDiagnostic(L"F1 fallback toggle received");
                Dispatch(widgetrail::Command::ToggleOverlay);
            }
            return 0;
        case kPlatformEventMessage:
            HandlePlatformEvents();
            return 0;
        case kImageReadyMessage:
            if (!AdvanceCompositorBackground(GetTickCount64()))
                InvalidateRect(window_, nullptr, FALSE);
            return 0;
        case kCatalogRefreshMessage: {
            if (state_.surface() == widgetrail::Surface::Hidden &&
                !pinnedSurfaceCoordinator_.pinned()) {
                KillTimer(window_, kCatalogRetryTimer);
                bridge_.AbandonWidgetCatalogChangedRevision();
                sessions_.ResetCatalogRetry();
                return 0;
            }
            if (!sessions_.RequestCatalog()) {
                bridge_.AbandonWidgetCatalogChangedRevision();
                sessions_.ResetCatalogRetry();
            }
            return 0;
        }
        case kWidgetSessionCompletionMessage:
            ProcessWidgetSessionEvents();
            return 0;
        case kSnapshotRefreshMessage:
            if (state_.surface() == widgetrail::Surface::Hidden &&
                !pinnedSurfaceCoordinator_.pinned()) return 0;
            {
                const auto correlationId = static_cast<std::uint64_t>(wParam);
                const std::wstring widgetId = state_.surface() == widgetrail::Surface::Hidden
                    ? std::wstring(pinnedSurfaceCoordinator_.widgetId())
                    : state_.surface() == widgetrail::Surface::Widget
                    ? std::wstring(state_.activeWidget())
                    : std::wstring(state_.selectedWidget());
                admissionTrace_.RecordRefreshDequeued(
                    correlationId, widgetId, GetTickCount64());
                const bool coldPresentationPending =
                    IsBridgeWidget(widgetId) && SnapshotFor(widgetId) == nullptr;
                RefreshAndApplyPresentation([&] {
                    if (!coldPresentationPending) {
                        RefreshCurrentBridgeSnapshot(correlationId);
                        return;
                    }
                    SyncWidgetActivity(
                        false, correlationId,
                        correlationId != 0 ? widgetId : std::wstring_view{});
                    if (SnapshotFor(widgetId) && pendingContentRevealWidget_ == widgetId) {
                        pendingContentRevealWidget_.clear();
                        // The last-good content was already fully visible.
                        // Admission is one atomic identity swap, not a fade to
                        // an empty content layer and back.
                        overlayTransition_.SnapContentVisible();
                        AdvanceOverlayTransition(GetTickCount64());
                        RestoreFocusForActiveSurface(widgetId);
                    }
                });
            }
            return 0;
        case kForegroundChangedMessage:
            if (textEntryModal_.active() ||
                textEntryControllerPhase_ != TextEntryControllerPhase::None) {
                // Opening and destroying the owned modal can enqueue transient
                // foreground changes. They belong to the modal transaction and
                // must not become a second overlay-close command while its
                // controller release is still quarantined.
                return 0;
            }
            if (state_.surface() != widgetrail::Surface::Hidden) {
                const HWND foreground = reinterpret_cast<HWND>(lParam);
                const bool valid = foreground && IsWindow(foreground);
                DWORD processId = 0;
                if (valid) (void)GetWindowThreadProcessId(foreground, &processId);
                if (widgetrail::input::DecideVisibleForegroundTransition(
                        true, valid, processId == GetCurrentProcessId()) ==
                    widgetrail::input::VisibleForegroundTransition::CloseOverlay) {
                    (void)WidgetRailOverlayPlatformObserveForegroundTarget(
                        platform_,
                        reinterpret_cast<std::uintptr_t>(foreground),
                        WRAIL_OVERLAY_PLATFORM_TRUE);
                    AppendDiagnostic(
                        L"External foreground activation closed the overlay");
                    Dispatch(widgetrail::Command::CloseOverlay);
                }
            }
            return 0;
        case kPlacementRefreshMessage:
            if (state_.surface() != widgetrail::Surface::Hidden) {
                ShowOverlay();
                InvalidateRect(window_, nullptr, FALSE);
            }
            return 0;
        case kDisplayRefreshMessage:
            ApplyPendingDisplayEnvironmentRefresh();
            return 0;
        case kPerformanceResetMessage:
            ResetPerformanceCounters();
            return 0;
        case kAccessibilityActionMessage:
            HandleAccessibilityActions();
            return 0;
        case kPinnedSurfaceChangedMessage:
            if (wParam ==
                widgetrail::pinned::kBackgroundSurfaceDiagnosticNotification) {
                DrainPinnedSurfaceDiagnostics();
                return 0;
            }
            if (wParam ==
                widgetrail::pinned::kPinnedResizeDiagnosticNotification) {
                DrainPinnedResizeDiagnostics();
                return 0;
            }
            DrainPinnedSurfaceInputs();
            ReconcileCommittedEmbeddedMediaSurface();
            if (!pinnedSurfaceCoordinator_.pinned() ||
                pinnedSurfaceCoordinator_.placementMode() ==
                    widgetrail::pinned::PlacementMode::None) {
                ResetPinnedPlacementNavigation();
            }
            if (state_.surface() == widgetrail::Surface::Hidden) {
                if (pinnedSurfaceCoordinator_.pinned()) {
                    KillTimer(window_, kBridgeControlPlaneTimer);
                    SetTimer(window_, kPinnedSurfaceTimer, 100, nullptr);
                } else {
                    KillTimer(window_, kPinnedSurfaceTimer);
                    SetTimer(window_, kBridgeControlPlaneTimer, 100, nullptr);
                }
            }
            SyncWidgetActivity();
            InvalidateRect(window_, nullptr, FALSE);
            return 0;
        case kProcessActivationMessage:
            HandleAuthenticatedShowActivation();
            return 0;
        case kDevelopmentTrayYHoldMessage:
            // Authenticated development fixtures exercise the same host-owned
            // recognizer/action route as GameInput without adding a production
            // controller remapping or public bridge message.
            if (!developmentReadyNonce_ || !developmentCatalogRoot_) return 0;
            AppendDiagnostic(
                L"Development tray Y phase=" + std::to_wstring(wParam) +
                L" eligible=" + (TrayYRestartEligible() ? L"true" : L"false"));
            if (wParam == 1) {
                trayYGesture_.Press(
                    state_.selectedWidget(), TrayYRestartEligible(), GetTickCount64());
            } else if (wParam == 2) {
                ApplyTrayYGestureAction(trayYGesture_.Update(
                    state_.selectedWidget(), TrayYRestartEligible(), GetTickCount64()));
            } else if (wParam == 3) {
                ApplyTrayYGestureAction(trayYGesture_.Release(
                    state_.selectedWidget(), TrayYRestartEligible(), GetTickCount64()));
            } else if (wParam == 4) {
                const auto now = GetTickCount64();
                trayYGesture_.Press(
                    state_.selectedWidget(), TrayYRestartEligible(),
                    now - widgetrail::input::kTrayWidgetRestartHoldMilliseconds);
                ApplyTrayYGestureAction(trayYGesture_.Update(
                    state_.selectedWidget(), TrayYRestartEligible(), now));
            }
            InvalidateRect(window_, nullptr, FALSE);
            return 0;
        case WM_SETFOCUS:
            accessibilityProvider_.SetWindowFocused(true);
            chromeAccessibilityProvider_.SetWindowFocused(true);
            if (platform_) {
                (void)WidgetRailOverlayPlatformSetWindowState(
                    platform_,
                    PlatformBoolean(
                        state_.surface() != widgetrail::Surface::Hidden),
                    WRAIL_OVERLAY_PLATFORM_TRUE);
            }
            return DefWindowProcW(window_, message, wParam, lParam);
        case WM_KILLFOCUS:
            accessibilityProvider_.SetWindowFocused(false);
            chromeAccessibilityProvider_.SetWindowFocused(false);
            trayYGesture_.Cancel();
            if (platform_) {
                (void)WidgetRailOverlayPlatformSetWindowState(
                    platform_,
                    PlatformBoolean(
                        state_.surface() != widgetrail::Surface::Hidden),
                    WRAIL_OVERLAY_PLATFORM_FALSE);
            }
            InvalidateRect(window_, nullptr, FALSE);
            return DefWindowProcW(window_, message, wParam, lParam);
        case WM_SHOWWINDOW:
            accessibilityProvider_.SetWindowVisible(wParam != FALSE);
            if (auto* session = EmbeddedMediaEndpointOwner(
                    widgetrail::media::Endpoint::Overlay);
                session && session->coordinator && session->authority)
                (void)session->coordinator->SetVisible(
                    wParam != FALSE && session->authority->presentation !=
                        EmbeddedMediaPresentationState::Parked);
            if (wParam == FALSE) {
                trayYGesture_.Reset();
                ClearFreeScrollReentry(L"window-hidden");
                ClearScrollPaginationPrefetch(L"window-hidden");
            }
            if (platform_) {
                (void)WidgetRailOverlayPlatformSetWindowState(
                    platform_,
                    PlatformBoolean(wParam != FALSE),
                    PlatformBoolean(GetFocus() == window_));
            }
            return DefWindowProcW(window_, message, wParam, lParam);
        case WM_GETOBJECT:
            if (static_cast<LONG>(lParam) == UiaRootObjectId) {
                if (!accessibilityActive_) {
                    accessibilityActive_ = true;
                    InvalidateRect(window_, nullptr, FALSE);
                    UpdateWindow(window_);
                }
                return accessibilityProvider_.HandleWmGetObject(wParam, lParam);
            }
            return DefWindowProcW(window_, message, wParam, lParam);
        case WM_NCHITTEST:
            if (const auto* session = RichMediaProofSession();
                session && session->coordinator &&
                session->coordinator->state().lifecycle ==
                    widgetrail::richmedia::Lifecycle::Visible) {
                if (richMediaProof_) return HTCLIENT;
                POINT point{
                    static_cast<LONG>(static_cast<short>(LOWORD(lParam))),
                    static_cast<LONG>(static_cast<short>(HIWORD(lParam))),
                };
                if (ScreenToClient(window_, &point) &&
                    session->clientClip &&
                    PtInRect(&*session->clientClip, point))
                    return HTCLIENT;
            }
            if (compositionSurface_.available() &&
                state_.surface() != widgetrail::Surface::Hidden) {
                POINT point{
                    static_cast<LONG>(static_cast<short>(LOWORD(lParam))),
                    static_cast<LONG>(static_cast<short>(HIWORD(lParam))),
                };
                if (ScreenToClient(window_, &point) &&
                    !HitTestAuthoredCompositionSurface(
                        static_cast<float>(point.x),
                        static_cast<float>(point.y))) {
                    return HTTRANSPARENT;
                }
            }
            return DefWindowProcW(window_, message, wParam, lParam);
        case WM_KEYDOWN:
            if (auto* session = RichMediaProofSession();
                session && session->coordinator &&
                session->coordinator->ForwardKey(message, wParam, lParam))
                return 0;
            HandleKey(
                static_cast<UINT>(wParam),
                (lParam & (1LL << 30)) != 0);
            return 0;
        case WM_MOUSEWHEEL:
            if (interactionSession_.selectPopup()) {
                MoveSelectPopupByWheel(
                    static_cast<short>(HIWORD(wParam)));
                return 0;
            }
            if (auto* session = RichMediaProofSession();
                session && session->coordinator &&
                session->coordinator->ForwardMouse(message, wParam, lParam))
                return 0;
            return DefWindowProcW(window_, message, wParam, lParam);
        case WM_MOUSEMOVE:
        case WM_MOUSELEAVE:
        case WM_LBUTTONDOWN:
        case WM_RBUTTONDOWN:
            if (auto* session = RichMediaProofSession();
                session && session->coordinator &&
                session->coordinator->ForwardMouse(message, wParam, lParam))
                return 0;
            return DefWindowProcW(window_, message, wParam, lParam);
        case WM_LBUTTONUP:
            if (auto* session = RichMediaProofSession();
                session && session->coordinator &&
                session->coordinator->ForwardMouse(message, wParam, lParam))
                return 0;
            HandlePointerActivation(
                static_cast<float>(static_cast<short>(LOWORD(lParam))),
                static_cast<float>(static_cast<short>(HIWORD(lParam))));
            return 0;
        case WM_RBUTTONUP:
            if (auto* session = RichMediaProofSession();
                session && session->coordinator &&
                session->coordinator->ForwardMouse(message, wParam, lParam))
                return 0;
            HandlePointerActivation(
                static_cast<float>(static_cast<short>(LOWORD(lParam))),
                static_cast<float>(static_cast<short>(HIWORD(lParam))), true);
            return 0;
        case WM_TIMER:
            if (wParam == kBridgeControlPlaneTimer) {
                // A fully hidden, unpinned overlay retains only the native
                // Bridge control plane. Pumping frames here fills the client's
                // bounded/coalesced event queues; their presentation effects
                // remain deferred until an ordinary visible/pinned tick.
                (void)bridge_.PumpEvents();
                return 0;
            }
            if (performanceCountersActive_) {
                ++performanceTimerMessages_;
                if (wParam == kControllerTimer) {
                    ++performanceControllerTimerMessages_;
                } else if (wParam == kGuideCompatibilityTimer) {
                    ++performanceGuideCompatibilityTimerMessages_;
                }
            }
            if (wParam == kControllerTimer || wParam == kPinnedSurfaceTimer) {
                const bool controllerTick = wParam == kControllerTimer;
                const auto now = GetTickCount64();
                if (controllerTick) admissionTrace_.ObserveSlow(now);
                // The dedicated deadline timer owns normal expiry. This cheap
                // state check also closes the race if Win32 timer creation is
                // temporarily unavailable; it never repaints unless state changed.
                if (controllerTick) actionFailureFeedback_.OnControllerTimer();
                if (controllerTick &&
                    (overlayTransition_.active() ||
                     presentationTransaction_.extentTransitionActive())) {
                    AdvanceOverlayTransition(now);
                }
                if (controllerTick && awaitingSuccessfulOpenPaint_ &&
                    now >= nextOpenPaintRetryAt_) {
                    nextOpenPaintRetryAt_ = now + 100;
                    InvalidateRect(window_, nullptr, FALSE);
                }
                // Closing changes semantic state immediately. The existing
                // controller timer is retained only long enough to finish the
                // bounded physical fade; no input or worker work runs behind
                // the hidden surface.
                if (state_.surface() == widgetrail::Surface::Hidden &&
                    !pinnedSurfaceCoordinator_.pinned()) return 0;
                // A newly shown or device-lost HWND is deliberately alpha-zero
                // until D2D commits a complete frame. Do not let an invisible
                // surface consume navigation or advance its worker meanwhile;
                // Guide/F1 continue to arrive through their dedicated paths.
                if (awaitingSuccessfulOpenPaint_) return 0;
                if (controllerTick &&
                    lastWidgetRenderResult_.succeeded &&
                    state_.surface() == widgetrail::Surface::Widget &&
                    lastWidgetRenderResult_.backgroundSurfaceSettleWake &&
                    now >= lastWidgetRenderResult_
                        .backgroundSurfaceSettleWake->deadlineMilliseconds &&
                    !HasExactRefreshRetainedVisualCheckpoint()) {
                    if (AdvanceCompositorBackground(now)) {
                        lastWidgetRenderResult_.backgroundSurfaceSettleWake.reset();
                    } else if (InvalidateRect(window_, nullptr, FALSE) != FALSE) {
                        pendingContentRenderPlan_.reset();
                        if (declarativeRenderer_)
                            declarativeRenderer_->CancelPresentationUpdatePlan();
                        lastWidgetRenderResult_.backgroundSurfaceSettleWake.reset();
                    }
                }
                PumpBridgeEvents(controllerTick);
            } else if (wParam == kGuideCompatibilityTimer) {
                WidgetRailOverlayPlatformEvent event;
                std::uint32_t hasEvent = WRAIL_OVERLAY_PLATFORM_FALSE;
                if (WidgetRailOverlayPlatformPollLegacyGuide(
                        platform_, GetTickCount64(), &event, &hasEvent) ==
                        WidgetRailOverlayPlatformStatus::Ok &&
                    hasEvent != WRAIL_OVERLAY_PLATFORM_FALSE) {
                    HandlePlatformEvent(event);
                }
            } else if (wParam == kZOrderSettleTimer) {
                KillTimer(window_, kZOrderSettleTimer);
                ReassertOverlayZOrder();
            } else if (wParam == kCatalogRetryTimer) {
                KillTimer(window_, kCatalogRetryTimer);
                bridge_.RetryWidgetCatalogChangedRevision();
                if (bridge_.TakeWidgetCatalogChangedRevision()) {
                    PostMessageW(window_, kCatalogRefreshMessage, 0, 0);
                }
            } else if (wParam == kForegroundLossTimer) {
                KillTimer(window_, kForegroundLossTimer);
                if (state_.surface() != widgetrail::Surface::Hidden) {
                    const HWND foreground = GetForegroundWindow();
                    const bool valid = foreground && IsWindow(foreground);
                    DWORD processId = 0;
                    if (valid) (void)GetWindowThreadProcessId(foreground, &processId);
                    if (widgetrail::input::DecideVisibleForegroundTransition(
                            true, valid, processId == GetCurrentProcessId()) ==
                        widgetrail::input::VisibleForegroundTransition::CloseOverlay) {
                        (void)WidgetRailOverlayPlatformObserveForegroundTarget(
                            platform_,
                            reinterpret_cast<std::uintptr_t>(foreground),
                            WRAIL_OVERLAY_PLATFORM_TRUE);
                        AppendDiagnostic(
                            L"Application deactivation closed the overlay");
                        Dispatch(widgetrail::Command::CloseOverlay);
                    }
                }
            } else if (wParam == kActionFeedbackTimer) {
                KillTimer(window_, kActionFeedbackTimer);
                actionFailureFeedback_.OnDeadlineTimer();
            }
            return 0;
        case kScrollPaginationPrefetchMessage:
            DispatchQueuedScrollPaginationPrefetch();
            return 0;
        case kSharedMediaEnvironmentExitedMessage: {
            std::unique_ptr<widgetrail::richmedia::EnvironmentExit> exit{
                reinterpret_cast<widgetrail::richmedia::EnvironmentExit*>(lParam)};
            if (exit) HandleSharedMediaEnvironmentExited(*exit);
            return 0;
        }
        case WM_ACTIVATEAPP:
            if (state_.surface() != widgetrail::Surface::Hidden) {
                if (wParam == FALSE) {
                    // Foreground assignment can lag WM_ACTIVATEAPP. Defer one
                    // bounded check and close only with a valid external HWND.
                    SetTimer(window_, kForegroundLossTimer, 40, nullptr);
                } else {
                    KillTimer(window_, kForegroundLossTimer);
                }
            }
            return 0;
        case WM_PAINT:
            if (performanceCountersActive_) ++performancePaintMessages_;
            Paint();
            return 0;
        case WM_SIZE:
        {
            const auto width = static_cast<unsigned int>(LOWORD(lParam));
            const auto height = static_cast<unsigned int>(HIWORD(lParam));
            const auto resize = widgetrail::PlanRenderTargetResize(
                static_cast<bool>(hwndRenderTarget_), wParam == SIZE_MINIMIZED,
                width, height);
            if (!compositionSurface_.available() && resize.resizeInPlace) {
                // Resize keeps the HWND target allocation/lifecycle stable;
                // viewport-derived brushes, text formats, renderer resources,
                // focus geometry, and semantics are rebuilt by the committed
                // paint at the new extent.
                DiscardGraphicsResources(false);
                const HRESULT result = hwndRenderTarget_->Resize(D2D1::SizeU(width, height));
                if (FAILED(result)) {
                    AppendDiagnostic(
                        L"Overlay render target resize failed; recreating target hresult=" +
                        std::to_wstring(static_cast<unsigned long>(result)));
                    DiscardGraphicsResources();
                } else {
                    AppendDiagnostic(
                        L"Overlay render target resized in place width=" +
                        std::to_wstring(width) + L" height=" +
                        std::to_wstring(height));
                }
            }
            if (accessibilityActive_ && !compositionPlacementInProgress_)
                ClearAccessibilityTree();
            if (!compositionPlacementInProgress_) {
                ClearFreeScrollReentry(L"viewport-resized");
            }
            if (resize.invalidate && !compositionPlacementInProgress_)
                InvalidateRect(window_, nullptr, FALSE);
            if (richMediaProof_ && width > 0 && height > 0) {
                const RECT clientBounds{0, 0, static_cast<LONG>(width), static_cast<LONG>(height)};
                if (auto* session = RichMediaProofSession();
                    session && session->coordinator)
                    (void)session->coordinator->UpdateGeometry(
                        clientBounds,
                        static_cast<double>(std::max(1U, GetDpiForWindow(window_))) / 96.0);
                widgetrail::OverlayCompositionSurface::CommitTiming timing;
                (void)compositionSurface_.CommitExternalContentPresentation(
                    clientBounds, true, timing);
            } else if (!compositionPlacementInProgress_) {
                if (auto* session = EmbeddedMediaEndpointOwner(
                        widgetrail::media::Endpoint::Overlay);
                    session && session->coordinator && session->authority) {
                    // A viewport resize revokes the old layout-owned geometry.
                    // The next successful native render republishes one exact box.
                    if (session->committedGeometry)
                        session->committedGeometry->visible = false;
                    // A failing hide faults the controller, which retires this
                    // session and releases the registry reference from inside the
                    // coordinator's own callback.
                    const auto coordinator = session->coordinator;
                    (void)coordinator->SetVisible(false);
                }
            }
            return 0;
        }
        case WM_DPICHANGED:
            ClearFreeScrollReentry(L"dpi-changed");
            if (accessibilityActive_) ClearAccessibilityTree();
            if (richMediaProof_) {
                RECT clientBounds{};
                if (GetClientRect(window_, &clientBounds)) {
                    if (auto* session = RichMediaProofSession();
                        session && session->coordinator)
                        (void)session->coordinator->UpdateGeometry(
                            clientBounds,
                            static_cast<double>(std::max(1U, GetDpiForWindow(window_))) /
                                96.0);
                }
            } else if (auto* session = EmbeddedMediaEndpointOwner(
                           widgetrail::media::Endpoint::Overlay);
                       session && session->coordinator && session->authority) {
                if (session->committedGeometry)
                    session->committedGeometry->visible = false;
                const auto coordinator = session->coordinator;
                (void)coordinator->SetVisible(false);
            }
            QueueDisplayEnvironmentRefresh(widgetrail::DisplayEnvironmentChange::Dpi);
            return 0;
        case WM_DISPLAYCHANGE:
            // Display topology may change without a DPI transition. Recreate
            // the target so viewport-relative shell styles use fresh metrics.
            ClearFreeScrollReentry(L"display-changed");
            if (accessibilityActive_) ClearAccessibilityTree();
            QueueDisplayEnvironmentRefresh(widgetrail::DisplayEnvironmentChange::Topology);
            return 0;
        case WM_SETTINGCHANGE:
            // SPI_SETWORKAREA/taskbar changes and accessibility/theme changes
            // share this notification. Always re-read monitor work-area data,
            // even when the bridge has not supplied an appearance revision.
            ClearFreeScrollReentry(L"display-settings-changed");
            if (accessibilityActive_) ClearAccessibilityTree();
            QueueDisplayEnvironmentRefresh(widgetrail::DisplayEnvironmentChange::SystemSettings);
            return 0;
        case WM_WINDOWPOSCHANGED:
            if (accessibilityActive_ &&
                (reinterpret_cast<const WINDOWPOS*>(lParam)->flags & SWP_NOMOVE) == 0 &&
                !accessibilityTree_.widgetId.empty())
                (void)PublishAccessibilityTree(
                    static_cast<double>(std::max(1U, GetDpiForWindow(window_))) / 96.0);
            if (state_.surface() != widgetrail::Surface::Hidden &&
                (GetWindowLongPtrW(window_, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0) {
                SetTimer(window_, kZOrderSettleTimer, 80, nullptr);
            }
            return DefWindowProcW(window_, message, wParam, lParam);
        case WM_ERASEBKGND:
            return 1;
        case WM_CLOSE:
            if (performanceState_ && performanceCountersActive_ &&
                !PublishPerformanceCounters()) {
                AppendDiagnostic(L"Performance runtime diagnostics could not be published");
            }
            accessibilityProvider_.Detach();
            chromeAccessibilityProvider_.Detach();
            DestroyWindow(window_);
            return 0;
        case WM_DESTROY:
            accessibilityProvider_.Detach();
            chromeAccessibilityProvider_.Detach();
            PostQuitMessage(0);
            return 0;
        default:
            return DefWindowProcW(window_, message, wParam, lParam);
        }
    }

    using EmbeddedMediaCommandOriginAuthority =
        widgetrail::media::CommandOriginAuthority;
    using EmbeddedMediaAuthority = widgetrail::media::SessionAuthority;
    using EmbeddedMediaSession = widgetrail::media::SessionRecord;
    using EmbeddedMediaSessionKey = widgetrail::media::SessionKey;
    using EmbeddedMediaPresentationState = widgetrail::media::PresentationState;
    enum class EmbeddedMediaTransferResult { Completed, Deferred, Failed };

    bool InitializeRichMediaProof() {
        if (!compositionSurface_.available()) {
            initializationError_ =
                L"The bounded rich-media proof requires DirectComposition.";
            return false;
        }
        Microsoft::WRL::ComPtr<IUnknown> target;
        const HRESULT targetResult =
            compositionSurface_.CreateExternalContentTarget(&target);
        if (FAILED(targetResult))
            return FailHresult(L"CreateExternalContentTarget", targetResult);
        RECT bounds{};
        if (!GetClientRect(window_, &bounds))
            return FailWin32(L"GetClientRect(rich-media)", GetLastError());
        wchar_t temporary[MAX_PATH]{};
        if (!GetTempPathW(static_cast<DWORD>(std::size(temporary)), temporary))
            return FailWin32(L"GetTempPathW(rich-media)", GetLastError());
        richMediaProfileDirectory_ =
            (std::filesystem::path{temporary} / L"WidgetRail.RichMedia").wstring();
        const EmbeddedMediaSessionKey sessionKey{
            L"host.rich-media-proof",
            L"host.rich-media-proof.instance",
            L"host.rich-media-proof.runtime",
            L"host.rich-media-proof.surface",
        };
        auto* session = mediaSessions_.Ensure(sessionKey);
        if (!session || !session->coordinator) {
            initializationError_ =
                L"The bounded rich-media proof could not reserve a media session.";
            return false;
        }
        richMediaProofSessionKey_ = sessionKey;
        widgetrail::richmedia::Configuration configuration;
        configuration.ownerWindow = window_;
        configuration.compositionTarget = std::move(target);
        configuration.bounds = bounds;
        configuration.rasterScale =
            static_cast<double>(std::max(1U, GetDpiForWindow(window_))) / 96.0;
        configuration.initiallyVisible = true;
        configuration.profileRootDirectory = richMediaProfileDirectory_;
        configuration.diagnostic = [this](const std::wstring_view message) {
            InvokeMediaCallbackGuarded(L"proof-diagnostic", [&] {
                AppendDiagnostic(std::wstring{message});
            });
        };
        configuration.invalidate = [this, sessionKey] {
            InvokeMediaCallbackGuarded(L"proof-invalidate", [&] {
                OnRichMediaStateChanged(sessionKey);
            });
        };
        configuration.sharedEnvironmentExited = [this](
            const widgetrail::richmedia::EnvironmentExit& exit) {
            QueueSharedMediaEnvironmentExited(exit);
        };
        configuration.setPresentationVisible = [this](const bool visible) {
          InvokeMediaCallbackGuarded(L"proof-presentation-visible", [&] {
            RECT currentBounds{};
            if (!window_ || !GetClientRect(window_, &currentBounds)) return;
            widgetrail::OverlayCompositionSurface::CommitTiming timing;
            const HRESULT result = compositionSurface_.CommitExternalContentPresentation(
                currentBounds, visible, timing);
            if (FAILED(result))
                AppendDiagnostic(L"Rich media presentation commit failed hr=" +
                                 std::to_wstring(static_cast<long>(result)));
          });
        };
        const HRESULT initialize =
            session->coordinator->Initialize(std::move(configuration));
        if (FAILED(initialize))
            return FailHresult(L"RichMediaSurfaceCoordinator::Initialize", initialize);
        widgetrail::OverlayCompositionSurface::CommitTiming timing;
        const HRESULT presentation = compositionSurface_.CommitExternalContentPresentation(
            bounds, false, timing);
        if (FAILED(presentation))
            return FailHresult(L"CommitExternalContentPresentation", presentation);
        AppendDiagnostic(
            L"Rich media proof requested owner=existing-content-hwnd origin=embedded-only");
        return true;
    }

    void QueueSharedMediaEnvironmentExited(
        const widgetrail::richmedia::EnvironmentExit& exit) noexcept {
        auto notification = std::make_unique<widgetrail::richmedia::EnvironmentExit>(exit);
        if (!window_ || !PostMessageW(
                window_, kSharedMediaEnvironmentExitedMessage, 0,
                reinterpret_cast<LPARAM>(notification.get()))) {
            return;
        }
        (void)notification.release();
    }

    void HandleSharedMediaEnvironmentExited(
        const widgetrail::richmedia::EnvironmentExit& exit) {
        if (exit.generation == 0 ||
            exit.generation == retiredSharedMediaEnvironmentGeneration_) {
            return;
        }
        retiredSharedMediaEnvironmentGeneration_ = exit.generation;
        AppendDiagnostic(
            L"Rich media shared environment exited generation=" +
            std::to_wstring(exit.generation) + L" event-tick=" +
            std::to_wstring(exit.observedTick) + L" browser-pid=" +
            (exit.browserProcessIdAvailable
                ? std::to_wstring(exit.browserProcessId) : L"unavailable") +
            L" exit-kind=" +
            (exit.browserProcessExitKindAvailable
                ? std::to_wstring(exit.browserProcessExitKind) : L"unavailable") +
            L" owners=" +
            std::to_wstring(exit.liveControllerOwners) + L" waiters=" +
            std::to_wstring(exit.waiterCount));
        for (const auto& key : mediaSessions_.Keys()) {
            const auto* session = mediaSessions_.Find(key);
            if (!session || !session->coordinator ||
                session->coordinator->waitingForSharedEnvironmentRecovery() ||
                session->coordinator->state().authority.environmentGeneration !=
                    exit.generation) {
                continue;
            }
            StopEmbeddedMediaSession(key, L"shared-environment-exited");
        }
        for (const auto& key : mediaSessions_.Keys()) {
            auto* session = mediaSessions_.Find(key);
            if (!session || !session->coordinator ||
                !session->coordinator->waitingForSharedEnvironmentRecovery()) {
                continue;
            }
            const HRESULT resume =
                session->coordinator->ResumeSharedEnvironmentRecovery();
            if (FAILED(resume) && resume != E_PENDING) {
                AppendDiagnostic(
                    L"Rich media shared environment recovery failed generation=" +
                    std::to_wstring(exit.generation) + L" hr=" +
                    std::to_wstring(static_cast<long>(resume)));
                StopEmbeddedMediaSession(key, L"shared-environment-recovery-failed");
            }
        }
    }

    void OnRichMediaStateChanged(const EmbeddedMediaSessionKey& sessionKey) {
        auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->coordinator) return;
        const auto mediaSurface = session->coordinator;
        auto& authority = session->authority;
        Microsoft::WRL::ComPtr<IRawElementProviderSimple> provider;
        Microsoft::WRL::ComPtr<IRawElementProviderFragmentRoot> fragmentRoot;
        const auto state = mediaSurface->state();
        if (!richMediaProof_ &&
            state.lifecycle == widgetrail::richmedia::Lifecycle::Faulted &&
            authority) {
            const auto widgetId = authority->widgetId;
            const auto failureCode = state.failureCode.empty()
                ? std::wstring{L"media-controller-failed"}
                : state.failureCode;
            StopEmbeddedMediaSession(sessionKey, L"controller-fault");
            sessions_.RecordFailure(
                widgetId, widgetrail::WidgetSessionFailureStage::Protocol,
                L"Embedded media controller failed: " + failureCode);
            if (window_) InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        if (richMediaProof_ &&
            state.lifecycle != widgetrail::richmedia::Lifecycle::Faulted &&
            !state.focusedElement.empty() &&
            SUCCEEDED(mediaSurface->GetAutomationProvider(&provider)) && provider)
            (void)provider.As(&fragmentRoot);
        if (fragmentRoot) {
            accessibilityProvider_.SetEmbeddedFragmentRoot(fragmentRoot.Get());
            embeddedMediaAccessibilityOwner_ = sessionKey;
        } else if (embeddedMediaAccessibilityOwner_ &&
                   *embeddedMediaAccessibilityOwner_ == sessionKey) {
            accessibilityProvider_.SetEmbeddedFragmentRoot(nullptr);
            embeddedMediaAccessibilityOwner_.reset();
        }
        const bool pinnedProjection = authority &&
            authority->presentation ==
                EmbeddedMediaPresentationState::CompactPinned &&
            pinnedSurfaceCoordinator_.pinned() &&
            pinnedSurfaceCoordinator_.widgetId() == authority->widgetId;
        const auto pinnedOwner =
            mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Pinned);
        if (pinnedProjection && pinnedOwner && *pinnedOwner == sessionKey &&
            pinnedSurfaceCoordinator_.compactMediaPresentation()) {
            pinnedSurfaceCoordinator_.UpdateCompactMediaPlayback(
                state.positionSeconds, state.durationSeconds, state.playing);
            ReconcileCompactPinnedMediaChrome(sessionKey);
        }
        DispatchPendingEmbeddedMediaCommand(sessionKey);
        if (window_ && !pinnedProjection) InvalidateRect(window_, nullptr, FALSE);
        if (!richMediaProof_)
            ReconcileCommittedEmbeddedMediaSurface(sessionKey);
    }

    struct CommittedFullscreenPresentationCheckpoint final {
        EmbeddedMediaSessionKey sessionKey;
        widgetrail::EmbeddedMediaDocumentIdentity documentIdentity;
        std::wstring presentationGeneration;
        long long snapshotSequence{};
        widgetrail::media::EndpointGeometry geometry;
    };

    struct FullscreenEntryDecision final {
        std::optional<EmbeddedMediaSessionKey> sessionKey;
        std::wstring presentationGeneration;
        long long authoritySequence{};
        bool sessionPresent{};
        bool exactIdentity{};
        bool presentationAuthorityCurrent{};
        bool overlayOwnerCurrent{};
        bool overlayViewport{};
        bool committedViewport{};
        bool fullscreenCapable{};
        bool pinnedTakeover{};
        bool requestEligible{};
    };

    [[nodiscard]] FullscreenEntryDecision EvaluateFullscreenEntry(
        const std::wstring_view widgetId,
        const widgetrail::WidgetSnapshot& interactionSnapshot) const {
        FullscreenEntryDecision decision;
        decision.sessionKey = CurrentEmbeddedMediaSessionKey(widgetId);
        const auto* session = decision.sessionKey
            ? mediaSessions_.Find(*decision.sessionKey) : nullptr;
        const auto* descriptor = sessions_.FindDescriptor(widgetId);
        const auto overlayOwner =
            mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Overlay);
        decision.sessionPresent = session && session->authority;
        if (!decision.sessionPresent || !descriptor ||
            !interactionSnapshot.embeddedMediaSession) return decision;
        const auto& authority = *session->authority;
        decision.presentationGeneration = authority.presentationGeneration;
        decision.authoritySequence = authority.sequence;
        decision.exactIdentity = authority.widgetId == widgetId &&
            authority.instanceId == interactionSnapshot.instanceId &&
            authority.runtimeGeneration == descriptor->runtimeGeneration &&
            authority.presentationGeneration == descriptor->presentationGeneration &&
            authority.sessionId == interactionSnapshot.embeddedMediaSession->id &&
            widgetrail::SameEmbeddedMediaDocumentIdentity(
                authority.documentIdentity, *interactionSnapshot.embeddedMediaSession);
        decision.presentationAuthorityCurrent =
            EmbeddedMediaPresentationAuthorityCurrent(*decision.sessionKey);
        decision.overlayOwnerCurrent = overlayOwner &&
            *overlayOwner == *decision.sessionKey;
        decision.overlayViewport = authority.presentation ==
            EmbeddedMediaPresentationState::OverlayViewport;
        decision.committedViewport = ResolveOrdinaryOverlayEmbeddedMediaPresentationGeometry(
            *decision.sessionKey, interactionSnapshot.sequence).geometry.has_value();
        decision.fullscreenCapable = widgetrail::SupportsMediaPresentation(
            *interactionSnapshot.embeddedMediaSession,
            widgetrail::MediaPresentationKind::OverlayFullscreen);
        decision.pinnedTakeover = pinnedSurfaceCoordinator_.pinned() &&
            pinnedSurfaceCoordinator_.widgetId() == widgetId;
        decision.requestEligible = decision.exactIdentity &&
            decision.presentationAuthorityCurrent && decision.overlayOwnerCurrent &&
            decision.overlayViewport && decision.committedViewport &&
            decision.fullscreenCapable && !decision.pinnedTakeover;
        return decision;
    }

    [[nodiscard]] bool OverlayFullscreenMediaRequested() const noexcept {
        const auto key = CurrentEmbeddedMediaSessionKey(state_.activeWidget());
        const auto* session = key ? mediaSessions_.Find(*key) : nullptr;
        if (!session || !session->authority || !session->presentationRequest ||
            session->presentationRequest->target !=
                EmbeddedMediaPresentationState::OverlayFullscreen ||
            session->presentationRequest->presentationGeneration !=
                session->authority->presentationGeneration ||
            session->authority->presentation ==
                EmbeddedMediaPresentationState::CompactPinned ||
            !EmbeddedMediaPresentationAuthorityCurrent(*key))
            return false;
        const auto& authority = *session->authority;
        const auto* snapshot = SnapshotFor(state_.activeWidget());
        const auto* descriptor = sessions_.FindDescriptor(state_.activeWidget());
        // The activation never outlives the exact declaration that admitted it.
        // Re-deriving the capability, instance, and surface identity here keeps
        // every retirement path - widget switch, deactivation, a snapshot that
        // drops the capability, runtime replacement, and pinned takeover -
        // closing the mode without a separate imperative teardown per path.
        if (!snapshot || !descriptor || !snapshot->embeddedMediaSession) return false;
        return widgetrail::input::OverlayFullscreenMediaAuthorityCurrent(
            {authority.widgetId, authority.instanceId,
             authority.runtimeGeneration, authority.presentationGeneration,
             authority.sessionId},
            {state_.activeWidget(), snapshot->instanceId,
             descriptor->runtimeGeneration, descriptor->presentationGeneration,
             snapshot->embeddedMediaSession->id},
            state_.surface() == widgetrail::Surface::Widget,
            widgetrail::SupportsMediaPresentation(
                *snapshot->embeddedMediaSession,
                widgetrail::MediaPresentationKind::OverlayFullscreen),
            pinnedSurfaceCoordinator_.pinned() &&
                pinnedSurfaceCoordinator_.widgetId() == state_.activeWidget());
    }

    // Enters the host-owned mode for the exact declaration currently admitted
    // for this widget. Returns false when nothing eligible is resident.
    [[nodiscard]] bool EnterOverlayFullscreenMedia(
        const FullscreenEntryDecision& decision) {
        if (!decision.requestEligible || !decision.sessionKey) return false;
        return mediaSessions_.RequestPresentation(
            *decision.sessionKey, EmbeddedMediaPresentationState::OverlayFullscreen,
            decision.presentationGeneration);
    }

    bool ExitOverlayFullscreenMedia() {
        const auto key = CurrentEmbeddedMediaSessionKey(state_.activeWidget());
        auto* session = key ? mediaSessions_.Find(*key) : nullptr;
        if (!key || !session || !session->authority ||
            (session->authority->presentation !=
                 EmbeddedMediaPresentationState::OverlayFullscreen &&
             (!session->presentationRequest ||
              session->presentationRequest->target !=
                  EmbeddedMediaPresentationState::OverlayFullscreen)))
            return false;
        return mediaSessions_.RequestPresentation(
            *key, EmbeddedMediaPresentationState::OverlayViewport,
            session->authority->presentationGeneration);
    }

    // Drops an activation the predicate has already stopped honouring so the
    // retained identity cannot survive the declaration that admitted it.
    void ClearStaleOverlayFullscreenMediaActivation(
        const EmbeddedMediaSessionKey& sessionKey) {
        auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority || !session->presentationRequest ||
            session->presentationRequest->target !=
                EmbeddedMediaPresentationState::OverlayFullscreen)
            return;
        const auto* snapshot = SnapshotFor(session->authority->widgetId);
        const auto* descriptor = sessions_.FindDescriptor(
            session->authority->widgetId);
        const auto currentKey = CurrentEmbeddedMediaSessionKey(
            session->authority->widgetId);
        const bool current = snapshot && descriptor &&
            snapshot->embeddedMediaSession &&
            currentKey && *currentKey == sessionKey &&
            snapshot->instanceId == session->authority->instanceId &&
            descriptor->runtimeGeneration ==
                session->authority->runtimeGeneration &&
            descriptor->presentationGeneration ==
                session->presentationRequest->presentationGeneration &&
            snapshot->embeddedMediaSession->id ==
                session->authority->sessionId &&
            widgetrail::SameEmbeddedMediaDocumentIdentity(
                session->authority->documentIdentity,
                *snapshot->embeddedMediaSession) &&
            widgetrail::SupportsMediaPresentation(
                *snapshot->embeddedMediaSession,
                widgetrail::MediaPresentationKind::OverlayFullscreen) &&
            !(pinnedSurfaceCoordinator_.pinned() &&
              pinnedSurfaceCoordinator_.widgetId() ==
                  session->authority->widgetId);
        if (!current) {
            mediaSessions_.ClearPresentationRequest(sessionKey);
            if (committedFullscreenPresentation_ &&
                committedFullscreenPresentation_->sessionKey == sessionKey) {
                committedFullscreenPresentation_.reset();
            }
        }
    }

    [[nodiscard]] widgetrail::OverlayPresentationExtent
    OverlayFullscreenPresentationExtentDip() const noexcept {
        if (!fixedChromeAnchor_ || fixedChromeAnchor_->dpi == 0 ||
            fixedChromeAnchor_->interfaceScale <= 0.0F)
            return DesiredContentPanelExtentDip();
        const auto& anchor = *fixedChromeAnchor_;
        const double pixelsPerDip = static_cast<double>(anchor.dpi) / 96.0 *
            static_cast<double>(anchor.interfaceScale);
        const double width = static_cast<double>(
            anchor.workArea.right - anchor.workArea.left) / pixelsPerDip - 48.0;
        const double height = static_cast<double>(
            anchor.workArea.bottom - anchor.workArea.top) / pixelsPerDip - 56.0;
        return {
            std::max(1, static_cast<int>(std::floor(width))),
            std::max(1, static_cast<int>(std::floor(height))),
        };
    }

    [[nodiscard]] bool CommittedOverlayFullscreenMediaAuthorityCurrent(
        const std::wstring_view widgetId) const noexcept {
        const auto presentation = sessions_.Presentation(widgetId);
        const auto* snapshot = presentation.snapshot;
        const auto sessionKey = CurrentEmbeddedMediaSessionKey(widgetId);
        return state_.surface() == widgetrail::Surface::Widget &&
            state_.activeWidget() == widgetId && snapshot && sessionKey &&
            presentation.HasCommittedViewAuthority() &&
            CommittedFullscreenPresentationCurrent(*sessionKey, snapshot->sequence);
    }

    [[nodiscard]] static EmbeddedMediaSessionKey MakeEmbeddedMediaSessionKey(
        const std::wstring_view widgetId,
        const widgetrail::WidgetSnapshot& snapshot,
        const widgetrail::WidgetDescriptor& descriptor,
        const widgetrail::EmbeddedMediaSessionDeclaration& declaration) {
        return {
            std::wstring{widgetId},
            snapshot.instanceId,
            descriptor.runtimeGeneration,
            declaration.id,
        };
    }

    [[nodiscard]] std::optional<EmbeddedMediaSessionKey>
    CurrentEmbeddedMediaSessionKey(
        const std::wstring_view widgetId) const {
        const auto* snapshot = SnapshotFor(widgetId);
        const auto* descriptor = sessions_.FindDescriptor(widgetId);
        if (!snapshot || !descriptor || !snapshot->embeddedMediaSession)
            return std::nullopt;
        return MakeEmbeddedMediaSessionKey(
            widgetId, *snapshot, *descriptor, *snapshot->embeddedMediaSession);
    }

    [[nodiscard]] bool CommittedFullscreenPresentationCurrent(
        const EmbeddedMediaSessionKey& sessionKey,
        const long long currentSequence) const noexcept {
        const auto* session = mediaSessions_.Find(sessionKey);
        const auto* snapshot = session ? SnapshotFor(session->key.widgetId) : nullptr;
        const auto* descriptor = session
            ? sessions_.FindDescriptor(session->key.widgetId) : nullptr;
        const auto currentKey = session
            ? CurrentEmbeddedMediaSessionKey(session->key.widgetId) : std::nullopt;
        const auto owner = mediaSessions_.EndpointOwner(
            widgetrail::media::Endpoint::Overlay);
        const auto& checkpoint = committedFullscreenPresentation_;
        return checkpoint && session && session->authority && snapshot && descriptor &&
            snapshot->embeddedMediaSession && currentKey &&
            *currentKey == sessionKey && checkpoint->sessionKey == sessionKey &&
            checkpoint->snapshotSequence <= currentSequence &&
            snapshot->sequence == currentSequence &&
            checkpoint->presentationGeneration == descriptor->presentationGeneration &&
            widgetrail::SameEmbeddedMediaDocumentIdentity(
                checkpoint->documentIdentity,
                widgetrail::MakeEmbeddedMediaDocumentIdentity(
                    *snapshot->embeddedMediaSession)) &&
            owner && *owner == sessionKey &&
            session->authority->presentation ==
                EmbeddedMediaPresentationState::OverlayFullscreen &&
            session->committedGeometry &&
            widgetrail::media::SameEndpointGeometry(
                checkpoint->geometry, *session->committedGeometry);
    }

    void PublishCommittedFullscreenPresentation(
        const EmbeddedMediaSessionKey& sessionKey,
        const widgetrail::media::EndpointGeometry& geometry) {
        auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority) return;
        const auto* snapshot = SnapshotFor(session->key.widgetId);
        const auto* descriptor = sessions_.FindDescriptor(session->key.widgetId);
        const auto currentKey = CurrentEmbeddedMediaSessionKey(session->key.widgetId);
        if (!snapshot || !descriptor || !snapshot->embeddedMediaSession ||
            !currentKey || *currentKey != sessionKey ||
            session->authority->presentation !=
                EmbeddedMediaPresentationState::OverlayFullscreen ||
            session->authority->sequence != snapshot->sequence ||
            session->authority->presentationGeneration !=
                descriptor->presentationGeneration ||
            !session->committedGeometry ||
            !widgetrail::media::SameEndpointGeometry(
                *session->committedGeometry, geometry)) {
            return;
        }
        committedFullscreenPresentation_ = CommittedFullscreenPresentationCheckpoint{
            sessionKey,
            widgetrail::MakeEmbeddedMediaDocumentIdentity(
                *snapshot->embeddedMediaSession),
            descriptor->presentationGeneration,
            snapshot->sequence,
            geometry,
        };
    }

    [[nodiscard]] EmbeddedMediaSession* CurrentEmbeddedMediaSession(
        const std::wstring_view widgetId) {
        const auto key = CurrentEmbeddedMediaSessionKey(widgetId);
        return key ? mediaSessions_.Find(*key) : nullptr;
    }

    [[nodiscard]] const EmbeddedMediaSession* CurrentEmbeddedMediaSession(
        const std::wstring_view widgetId) const {
        const auto key = CurrentEmbeddedMediaSessionKey(widgetId);
        return key ? mediaSessions_.Find(*key) : nullptr;
    }

    [[nodiscard]] EmbeddedMediaSession* RichMediaProofSession() {
        return richMediaProofSessionKey_
            ? mediaSessions_.Find(*richMediaProofSessionKey_) : nullptr;
    }

    [[nodiscard]] const EmbeddedMediaSession* RichMediaProofSession() const {
        return richMediaProofSessionKey_
            ? mediaSessions_.Find(*richMediaProofSessionKey_) : nullptr;
    }

    [[nodiscard]] EmbeddedMediaSession* EmbeddedMediaEndpointOwner(
        const widgetrail::media::Endpoint endpoint) {
        const auto key = mediaSessions_.EndpointOwner(endpoint);
        return key ? mediaSessions_.Find(*key) : nullptr;
    }

    [[nodiscard]] const EmbeddedMediaSession* EmbeddedMediaEndpointOwner(
        const widgetrail::media::Endpoint endpoint) const {
        const auto key = mediaSessions_.EndpointOwner(endpoint);
        return key ? mediaSessions_.Find(*key) : nullptr;
    }

    [[nodiscard]] HRESULT EnsureEmbeddedMediaParkingTarget(
        const EmbeddedMediaSessionKey& key,
        Microsoft::WRL::ComPtr<IUnknown>& target) {
        target.Reset();
        auto* session = mediaSessions_.Find(key);
        if (!session) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        if (!session->parkingTarget) {
            const HRESULT result = compositionSurface_.CreateExternalContentParkingTarget(
                session->parkingTarget.ReleaseAndGetAddressOf());
            if (FAILED(result)) return result;
        }
        target = session->parkingTarget;
        return target ? S_OK : E_UNEXPECTED;
    }

    [[nodiscard]] float MediaPixelsPerDip(const HWND owner) const noexcept {
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        return static_cast<float>(std::max(1U, GetDpiForWindow(owner))) /
            96.0F * interfaceScale;
    }

    enum class OrdinaryEmbeddedMediaGeometryStatus {
        Valid,
        Unavailable,
        Invalid,
    };

    struct OrdinaryEmbeddedMediaGeometryResolution final {
        OrdinaryEmbeddedMediaGeometryStatus status{
            OrdinaryEmbeddedMediaGeometryStatus::Unavailable};
        std::optional<widgetrail::MediaViewportPresentationGeometry> geometry;
    };

    [[nodiscard]] OrdinaryEmbeddedMediaGeometryResolution
    ResolveOrdinaryOverlayEmbeddedMediaPresentationGeometry(
        const EmbeddedMediaSessionKey& sessionKey,
        const long long expectedSequence) const {
        const auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority) return {};
        const auto& authority = *session->authority;
        const auto* snapshot = SnapshotFor(authority.widgetId);
        const auto* descriptor = sessions_.FindDescriptor(
            authority.widgetId);
        // Geometry has three meanings. Invalid fails closed and destroys the
        // session, so it is reserved for a real contract violation. A frame
        // that simply does not carry this session's viewport yet - a retained
        // or inert refresh, a snapshot whose sequence has moved on, a commit
        // still describing another widget - is Pending: the session stays
        // resident and parks until a frame that does carry it arrives.
        if (!snapshot || !snapshot->embeddedMediaSession ||
            snapshot->embeddedMediaSession->id != authority.sessionId ||
            snapshot->sequence != expectedSequence || !descriptor ||
            descriptor->instanceId != authority.instanceId ||
            descriptor->runtimeGeneration != authority.runtimeGeneration ||
            descriptor->presentationGeneration != authority.presentationGeneration ||
            !window_) {
            return {};
        }
        if (!committedWidgetVisualState_) return {};
        const bool committedIdentityCurrent =
            committedWidgetVisualState_->widgetId ==
                authority.widgetId &&
            committedWidgetVisualState_->instanceId ==
                authority.instanceId &&
            committedWidgetVisualState_->runtimeGeneration ==
                authority.runtimeGeneration &&
            committedWidgetVisualState_->presentationGeneration ==
                authority.presentationGeneration &&
            committedWidgetVisualState_->snapshotSequence == expectedSequence;
        // The committed frame describes another widget or sequence, or the
        // render did not succeed. Both are transient and must not revoke a
        // live session.
        if (!committedIdentityCurrent || !lastWidgetRenderResult_.succeeded)
            return {};
        // A frame without exactly one media viewport has not published this
        // session's geometry yet. Park rather than fail closed.
        if (lastWidgetRenderResult_.mediaViewportRegions.size() != 1) return {};
        const auto& viewport = lastWidgetRenderResult_.mediaViewportRegions.front();
        // A published viewport bound to a different session is an actual
        // contract violation and still fails closed.
        if (viewport.mediaSessionId != authority.sessionId) {
            AppendDiagnostic(
                L"Embedded media geometry invalid reason=viewport-session-mismatch"
                L" widget=" + authority.widgetId +
                L" authority-session=" + authority.sessionId +
                L" viewport-session=" + viewport.mediaSessionId);
            return {OrdinaryEmbeddedMediaGeometryStatus::Invalid, std::nullopt};
        }
        auto geometry = widgetrail::ResolveMediaViewportPresentationGeometry(
            {viewport.bounds.x, viewport.bounds.y,
             viewport.bounds.width, viewport.bounds.height},
            {viewport.clip.x, viewport.clip.y,
             viewport.clip.width, viewport.clip.height},
            MediaPixelsPerDip(window_));
        if (!geometry) {
            AppendDiagnostic(
                L"Embedded media geometry invalid reason=unresolvable"
                L" widget=" + authority.widgetId +
                L" bounds=" + std::to_wstring(viewport.bounds.x) + L"," +
                std::to_wstring(viewport.bounds.y) + L"," +
                std::to_wstring(viewport.bounds.width) + L"," +
                std::to_wstring(viewport.bounds.height) +
                L" clip=" + std::to_wstring(viewport.clip.x) + L"," +
                std::to_wstring(viewport.clip.y) + L"," +
                std::to_wstring(viewport.clip.width) + L"," +
                std::to_wstring(viewport.clip.height) +
                L" scale=" + std::to_wstring(MediaPixelsPerDip(window_)));
            return {OrdinaryEmbeddedMediaGeometryStatus::Invalid, std::nullopt};
        }
        return {OrdinaryEmbeddedMediaGeometryStatus::Valid, std::move(geometry)};
    }

    [[nodiscard]] std::optional<widgetrail::MediaViewportPresentationGeometry>
    ResolveEmbeddedMediaPresentationGeometry(
        const EmbeddedMediaSessionKey& sessionKey,
        const EmbeddedMediaPresentationState presentation,
        const long long expectedSequence) const {
        const auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority) return std::nullopt;
        const auto& authority = *session->authority;
        const auto* snapshot = SnapshotFor(authority.widgetId);
        if (!snapshot || !snapshot->embeddedMediaSession ||
            snapshot->embeddedMediaSession->id != authority.sessionId ||
            snapshot->sequence != expectedSequence)
            return std::nullopt;
        widgetrail::RenderMediaViewportRegion viewport;
        HWND owner{};
        if (presentation == EmbeddedMediaPresentationState::CompactPinned) {
            const auto pinned = pinnedSurfaceCoordinator_.CurrentMediaViewport(
                authority.sessionId);
            if (!pinned) return std::nullopt;
            viewport = pinned->region;
            owner = pinnedSurfaceCoordinator_.window();
        } else if (presentation ==
                   EmbeddedMediaPresentationState::OverlayFullscreen) {
            if (!CommittedFullscreenPresentationCurrent(
                    sessionKey, expectedSequence)) {
                return std::nullopt;
            }
            const auto& committed = committedFullscreenPresentation_->geometry;
            return widgetrail::MediaViewportPresentationGeometry{
                {committed.bounds.left, committed.bounds.top,
                 committed.bounds.right, committed.bounds.bottom},
                {committed.clip.left, committed.clip.top,
                 committed.clip.right, committed.clip.bottom},
                {committed.controllerBounds.left, committed.controllerBounds.top,
                 committed.controllerBounds.right,
                 committed.controllerBounds.bottom}};
        } else return ResolveOrdinaryOverlayEmbeddedMediaPresentationGeometry(
            sessionKey, expectedSequence).geometry;
        if (!owner || viewport.mediaSessionId != authority.sessionId)
            return std::nullopt;
        return widgetrail::ResolveMediaViewportPresentationGeometry(
            {viewport.bounds.x, viewport.bounds.y,
             viewport.bounds.width, viewport.bounds.height},
            {viewport.clip.x, viewport.clip.y,
             viewport.clip.width, viewport.clip.height},
            presentation == EmbeddedMediaPresentationState::CompactPinned
                ? static_cast<float>(std::max(
                      1U, GetDpiForWindow(owner))) / 96.0F
                : MediaPixelsPerDip(owner));
    }

    [[nodiscard]] std::optional<widgetrail::MediaViewportPresentationGeometry>
    ResolveEmbeddedMediaPresentationGeometryForState(
        const EmbeddedMediaSessionKey& sessionKey,
        const EmbeddedMediaPresentationState presentation,
        const long long expectedSequence) {
        return ResolveEmbeddedMediaPresentationGeometry(
            sessionKey, presentation, expectedSequence);
    }

    [[nodiscard]] bool OverlayOwnsEmbeddedMediaViewport(
        const EmbeddedMediaSessionKey& sessionKey) {
        const auto* session = mediaSessions_.Find(sessionKey);
        return session && session->authority &&
            state_.surface() == widgetrail::Surface::Widget &&
            state_.activeWidget() == session->authority->widgetId &&
            ResolveEmbeddedMediaPresentationGeometryForState(
                sessionKey, EmbeddedMediaPresentationState::OverlayViewport,
                session->authority->sequence).has_value();
    }

    [[nodiscard]] bool EmbeddedMediaAuthorityMatches(
        const EmbeddedMediaSessionKey& sessionKey,
        const widgetrail::WidgetSnapshot& snapshot) const noexcept {
        const auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority) return false;
        const auto& authority = *session->authority;
        const auto* descriptor = sessions_.FindDescriptor(authority.widgetId);
        return descriptor && snapshot.embeddedMediaSession &&
            snapshot.sequence == authority.sequence &&
            snapshot.instanceId == authority.instanceId &&
            descriptor->runtimeGeneration == authority.runtimeGeneration &&
            descriptor->presentationGeneration ==
                authority.presentationGeneration &&
            snapshot.embeddedMediaSession->id == authority.sessionId;
    }

    [[nodiscard]] bool EmbeddedMediaAuthorityCurrent(
        const EmbeddedMediaSessionKey& sessionKey) const noexcept {
        const auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority) return false;
        const auto* snapshot = SnapshotFor(session->authority->widgetId);
        return snapshot && EmbeddedMediaAuthorityMatches(sessionKey, *snapshot);
    }

    [[nodiscard]] bool EmbeddedMediaPresentationAuthorityCurrent(
        const EmbeddedMediaSessionKey& sessionKey) const noexcept {
        if (EmbeddedMediaAuthorityCurrent(sessionKey)) return true;
        const auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority)
            return false;
        const auto& authority = *session->authority;
        const auto* descriptor = sessions_.FindDescriptor(authority.widgetId);
        const auto* snapshot = SnapshotFor(authority.widgetId);
        if (!descriptor || !snapshot || !snapshot->embeddedMediaSession) return false;
        const bool identityCurrent =
            snapshot->instanceId == authority.instanceId &&
            descriptor->runtimeGeneration == authority.runtimeGeneration &&
            descriptor->presentationGeneration ==
                authority.presentationGeneration &&
            snapshot->embeddedMediaSession->id == authority.sessionId;
        const bool documentIdentityCurrent =
            widgetrail::SameEmbeddedMediaDocumentIdentity(
                authority.documentIdentity,
                widgetrail::MakeEmbeddedMediaDocumentIdentity(
                    *snapshot->embeddedMediaSession));
        const bool compact = authority.presentation ==
            EmbeddedMediaPresentationState::CompactPinned;
        const bool projectionCurrent = compact
            ? pinnedSurfaceCoordinator_.pinned() &&
                pinnedSurfaceCoordinator_.widgetId() == authority.widgetId
            : state_.surface() == widgetrail::Surface::Widget &&
                state_.activeWidget() == authority.widgetId;
        const bool geometryCurrent = compact
            ? pinnedSurfaceCoordinator_.CurrentMediaViewport(
                  authority.sessionId).has_value()
            : session->committedGeometry.has_value();
        return widgetrail::RetainEmbeddedMediaPresentation({
            identityCurrent,
            documentIdentityCurrent,
            projectionCurrent,
            geometryCurrent,
            authority.sequence,
            snapshot->sequence,
        });
    }

    void ReconcileEmbeddedMediaCommandOrigin(
        const EmbeddedMediaSessionKey& sessionKey,
        const widgetrail::WidgetSnapshot& snapshot) {
        auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority) return;
        auto& authority = *session->authority;
        if (!snapshot.embeddedMediaSession ||
            snapshot.instanceId != authority.instanceId ||
            snapshot.embeddedMediaSession->id != authority.sessionId) {
            authority.commandOrigin.reset();
            return;
        }
        const auto& pending = snapshot.embeddedMediaSession->pendingCommand;
        if (!pending) {
            authority.commandOrigin.reset();
            return;
        }
        const auto& retained = authority.commandOrigin;
        if (retained && retained->commandSequence == pending->sequence &&
            retained->mediaKey == pending->mediaKey &&
            retained->sessionId == authority.sessionId &&
            retained->instanceId == authority.instanceId &&
            retained->runtimeGeneration == authority.runtimeGeneration &&
            retained->presentationGeneration == authority.presentationGeneration) {
            return;
        }
        authority.commandOrigin =
            EmbeddedMediaCommandOriginAuthority{
                snapshot.sequence,
                pending->sequence,
                pending->mediaKey,
                authority.sessionId,
                authority.instanceId,
                authority.runtimeGeneration,
                authority.presentationGeneration,
            };
    }

    void AdvanceCompatibleEmbeddedMediaCommandAuthority(
        const EmbeddedMediaSessionKey& sessionKey,
        const widgetrail::WidgetSnapshot& snapshot,
        const widgetrail::EmbeddedMediaSessionDeclaration& declaration) {
        auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority) return;
        ReconcileEmbeddedMediaCommandOrigin(sessionKey, snapshot);
        session->authority->sequence = snapshot.sequence;
        session->authority->commands = declaration.commands;
        session->authority->declaration = declaration;
        session->authority->declaration.pendingCommand.reset();
    }

    [[nodiscard]] bool OnEmbeddedMediaPlaybackEvent(
        const EmbeddedMediaSessionKey& sessionKey,
        const widgetrail::richmedia::PlaybackEvent& event,
        const widgetrail::richmedia::PlaybackTerminalSource terminalSource =
            widgetrail::richmedia::PlaybackTerminalSource::Page) {
        auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority) return false;
        auto& authority = *session->authority;
        const auto nextPublishedEventSequence =
            NextEmbeddedMediaPlaybackObservationSequence(
                embeddedMediaSessionPlaybackEventSequence_);
        if (!nextPublishedEventSequence) {
            AppendDiagnostic(
                L"Embedded media playback event rejected because the host "
                L"observation sequence is exhausted");
            return false;
        }
        const auto* snapshot = SnapshotFor(authority.widgetId);
        const auto* descriptor =
            sessions_.FindDescriptor(authority.widgetId);
        const long long commandSequence =
            static_cast<long long>(event.commandSequence);
        long long publicationSequence = authority.sequence;
        if (commandSequence > 0) {
            auto& origin = authority.commandOrigin;
            const auto* pending = snapshot && snapshot->embeddedMediaSession &&
                    snapshot->embeddedMediaSession->pendingCommand
                ? &*snapshot->embeddedMediaSession->pendingCommand
                : nullptr;
            if (!origin || !pending ||
                !widgetrail::richmedia::CanPublishPlaybackTerminal(
                    origin->stage, terminalSource) ||
                origin->commandSequence != commandSequence ||
                origin->mediaKey != event.mediaKey ||
                origin->sessionId != authority.sessionId ||
                origin->instanceId != authority.instanceId ||
                origin->runtimeGeneration != authority.runtimeGeneration ||
                origin->presentationGeneration != authority.presentationGeneration ||
                !descriptor || !snapshot->embeddedMediaSession ||
                snapshot->instanceId != origin->instanceId ||
                descriptor->runtimeGeneration != origin->runtimeGeneration ||
                descriptor->presentationGeneration !=
                    origin->presentationGeneration ||
                snapshot->embeddedMediaSession->id != origin->sessionId ||
                !widgetrail::SameEmbeddedMediaDocumentIdentity(
                    authority.documentIdentity,
                    widgetrail::MakeEmbeddedMediaDocumentIdentity(
                        *snapshot->embeddedMediaSession)) ||
                snapshot->sequence < origin->snapshotSequence ||
                pending->sequence != commandSequence ||
                pending->mediaKey != event.mediaKey) {
                AppendDiagnostic(
                    L"Embedded media playback event rejected by command-origin "
                    L"authority widget=" + authority.widgetId +
                    L" surface=" + authority.sessionId +
                    L" command=" + std::to_wstring(commandSequence));
                return false;
            }
            publicationSequence = origin->snapshotSequence;
        } else if (terminalSource !=
                       widgetrail::richmedia::PlaybackTerminalSource::Page ||
                   !EmbeddedMediaAuthorityCurrent(sessionKey)) {
            return false;
        }
        const auto publishedEventSequence = *nextPublishedEventSequence;
        embeddedMediaSessionPlaybackEventSequence_ = publishedEventSequence;
        const auto pinnedOwner =
            mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Pinned);
        if (pinnedOwner && *pinnedOwner == sessionKey &&
            authority.presentation ==
                EmbeddedMediaPresentationState::CompactPinned &&
            pinnedSurfaceCoordinator_.compactMediaPresentation()) {
            pinnedSurfaceCoordinator_.UpdateCompactMediaPlayback(
                event.positionSeconds, event.durationSeconds,
                event.state == L"playing");
            ReconcileCompactPinnedMediaChrome(sessionKey);
        }
        const widgetrail::EmbeddedMediaPlaybackEvent published{
            authority.sessionId,
            publishedEventSequence,
            commandSequence,
            event.mediaKey, event.state, event.positionSeconds,
            event.durationSeconds, event.volume, event.errorCode,
            event.playbackRate, event.muted, event.loop};
        const auto accepted = bridge_.PublishEmbeddedMediaPlaybackEvent(
            authority.widgetId, authority.instanceId,
            authority.runtimeGeneration,
            authority.presentationGeneration,
            publicationSequence, published);
        if (commandSequence > 0 && accepted.value_or(false) &&
            authority.commandOrigin)
            authority.commandOrigin->stage =
                widgetrail::richmedia::PlaybackCommandStage::Terminal;
        AppendDiagnostic(
            L"Embedded media playback event widget=" +
            authority.widgetId + L" surface=" +
            authority.sessionId + L" event=" +
            std::to_wstring(publishedEventSequence) + L" page-event=" +
            std::to_wstring(event.sequence) + L" command=" +
            std::to_wstring(event.commandSequence) + L" origin=" +
            std::to_wstring(publicationSequence) + L" current=" +
            std::to_wstring(authority.sequence) + L" result=" +
            (accepted.value_or(false) ? L"published" : L"rejected"));
        return accepted.value_or(false);
    }

    [[nodiscard]] bool PublishEmbeddedMediaCommandDisposition(
        const EmbeddedMediaSessionKey& sessionKey,
        const std::wstring_view errorCode,
        const widgetrail::richmedia::PlaybackTerminalSource terminalSource) {
        auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority || !session->coordinator ||
            !session->authority->commandOrigin ||
            session->authority->commandOrigin->stage ==
                widgetrail::richmedia::PlaybackCommandStage::Terminal)
            return false;
        const auto mediaState = session->coordinator->state();
        const auto& command = *session->authority->commandOrigin;
        return OnEmbeddedMediaPlaybackEvent(sessionKey, {
            0,
            static_cast<std::uint64_t>(command.commandSequence),
            command.mediaKey,
            L"error",
            std::max(0.0, mediaState.positionSeconds),
            std::max(mediaState.positionSeconds, mediaState.durationSeconds),
            std::clamp(mediaState.volume, 0.0, 1.0),
            std::wstring{errorCode},
            mediaState.playbackRate,
            mediaState.muted,
            mediaState.loop,
            true}, terminalSource);
    }

    void DispatchPendingEmbeddedMediaCommand(
        const EmbeddedMediaSessionKey& sessionKey,
        const widgetrail::WidgetSnapshot& snapshot) {
        auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority || !session->coordinator ||
            !EmbeddedMediaAuthorityMatches(sessionKey, snapshot)) return;
        auto& authority = *session->authority;
        if (!snapshot.embeddedMediaSession->pendingCommand) return;
        ReconcileEmbeddedMediaCommandOrigin(sessionKey, snapshot);
        const auto& pending = *snapshot.embeddedMediaSession->pendingCommand;
        if (!authority.commandOrigin ||
            authority.commandOrigin->stage !=
                widgetrail::richmedia::PlaybackCommandStage::Accepted ||
            pending.sequence <= authority.lastDispatchedPlaybackCommand)
            return;
        using Kind = widgetrail::richmedia::PlaybackCommandKind;
        std::optional<Kind> kind;
        if (pending.kind == L"load") kind = Kind::Load;
        else if (pending.kind == L"cue") kind = Kind::Cue;
        else if (pending.kind == L"play") kind = Kind::Play;
        else if (pending.kind == L"pause") kind = Kind::Pause;
        else if (pending.kind == L"seek") kind = Kind::Seek;
        else if (pending.kind == L"setVolume") kind = Kind::SetVolume;
        else if (pending.kind == L"setPlaybackRate") kind = Kind::SetPlaybackRate;
        else if (pending.kind == L"setMuted") kind = Kind::SetMuted;
        else if (pending.kind == L"setLoop") kind = Kind::SetLoop;
        if (!kind) {
            (void)PublishEmbeddedMediaCommandDisposition(
                sessionKey,
                L"media-command-rejected",
                widgetrail::richmedia::PlaybackTerminalSource::
                    HostDispatchRejection);
            return;
        }
        const auto dispatch = session->coordinator->DispatchPlaybackCommand({
            static_cast<std::uint64_t>(pending.sequence), *kind, pending.mediaKey,
            pending.positionSeconds, pending.volume, pending.playbackRate,
            pending.muted, pending.loop});
        const bool sent = dispatch ==
            widgetrail::richmedia::PlaybackCommandDispatchResult::Sent;
        if (sent) {
            authority.lastDispatchedPlaybackCommand = pending.sequence;
            authority.commandOrigin->stage =
                widgetrail::richmedia::PlaybackCommandStage::Dispatched;
        } else if (dispatch ==
                   widgetrail::richmedia::PlaybackCommandDispatchResult::Rejected) {
            (void)PublishEmbeddedMediaCommandDisposition(
                sessionKey,
                L"media-command-rejected",
                widgetrail::richmedia::PlaybackTerminalSource::
                    HostDispatchRejection);
        }
        AppendDiagnostic(
            L"Embedded media playback command widget=" +
            authority.widgetId + L" surface=" +
            authority.sessionId + L" sequence=" +
            std::to_wstring(pending.sequence) + L" kind=" + pending.kind +
            L" result=" +
            (sent ? L"sent" :
             dispatch == widgetrail::richmedia::PlaybackCommandDispatchResult::Deferred
                ? L"deferred" : L"rejected"));
    }

    void DispatchPendingEmbeddedMediaCommand(
        const EmbeddedMediaSessionKey& sessionKey) {
        const auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority) return;
        const auto* snapshot = SnapshotFor(session->authority->widgetId);
        if (!snapshot) return;
        DispatchPendingEmbeddedMediaCommand(sessionKey, *snapshot);
    }

    [[nodiscard]] bool EmbeddedMediaPresentationVisible(
        const EmbeddedMediaSessionKey& sessionKey) const noexcept {
        const auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority ||
            !EmbeddedMediaPresentationAuthorityCurrent(sessionKey) ||
            session->authority->presentation ==
                EmbeddedMediaPresentationState::Parked) return false;
        if (session->authority->presentation ==
            EmbeddedMediaPresentationState::CompactPinned)
            return pinnedSurfaceCoordinator_.pinned() &&
                pinnedSurfaceCoordinator_.widgetId() == session->authority->widgetId &&
                IsWindowVisible(pinnedSurfaceCoordinator_.window());
        return state_.surface() == widgetrail::Surface::Widget &&
            state_.activeWidget() == session->authority->widgetId &&
            IsWindowVisible(window_);
    }

    [[nodiscard]] bool RichMediaInputCurrent() const noexcept {
        // Public embedded media is controlled exclusively through native
        // declarative actions. Raw browser input remains proof-only.
        return richMediaProof_;
    }

    [[nodiscard]] bool EmbeddedMediaCommandSupported(
        const EmbeddedMediaSessionKey& sessionKey,
        const std::wstring_view command) const noexcept {
        const auto* session = mediaSessions_.Find(sessionKey);
        return richMediaProof_ ||
            (session && session->authority && std::find(
                session->authority->commands.begin(),
                session->authority->commands.end(), command) !=
                    session->authority->commands.end());
    }

    [[nodiscard]] std::optional<double> OverlayFullscreenMediaSeekTarget(
        const widgetrail::input::NavigationDirection direction) const noexcept {
        const auto key = CurrentEmbeddedMediaSessionKey(state_.activeWidget());
        const auto* session = key ? mediaSessions_.Find(*key) : nullptr;
        if (!OverlayFullscreenMediaRequested() || !key || !session ||
            !session->authority || !session->coordinator ||
            session->authority->presentation ==
                EmbeddedMediaPresentationState::Parked ||
            session->authority->presentation ==
                EmbeddedMediaPresentationState::CompactPinned ||
            !EmbeddedMediaAuthorityCurrent(*key) ||
            (direction != widgetrail::input::NavigationDirection::Left &&
             direction != widgetrail::input::NavigationDirection::Right)) {
            return std::nullopt;
        }
        const auto* snapshot = SnapshotFor(session->authority->widgetId);
        if (!snapshot || !snapshot->embeddedMediaSession) return std::nullopt;
        const auto playback = session->coordinator->state();
        if (!std::isfinite(playback.positionSeconds) ||
            !std::isfinite(playback.durationSeconds) ||
            playback.positionSeconds < 0.0 || playback.durationSeconds <= 0.0) {
            return std::nullopt;
        }
        const double step = snapshot->embeddedMediaSession->mediaSeekStepSeconds
            .value_or(
                widgetrail::protocol_contract::
                    DefaultMediaSeekStepSeconds);
        return widgetrail::pinned::ResolveBoundedMediaSeekTarget(
            playback.positionSeconds, playback.durationSeconds, step, direction);
    }

    [[nodiscard]] static widgetrail::OverlayCompositionSurface::ExternalContentEndpoint
    CompositionEndpoint(
        const EmbeddedMediaPresentationState presentation) noexcept {
        return presentation == EmbeddedMediaPresentationState::CompactPinned
            ? widgetrail::OverlayCompositionSurface::ExternalContentEndpoint::Pinned
            : widgetrail::OverlayCompositionSurface::ExternalContentEndpoint::Overlay;
    }

    [[nodiscard]] static std::optional<widgetrail::media::Endpoint>
    MediaEndpoint(
        const EmbeddedMediaPresentationState presentation) noexcept {
        if (presentation == EmbeddedMediaPresentationState::Parked)
            return std::nullopt;
        return presentation == EmbeddedMediaPresentationState::CompactPinned
            ? widgetrail::media::Endpoint::Pinned
            : widgetrail::media::Endpoint::Overlay;
    }

    [[nodiscard]] bool EmbeddedMediaEndpointOwnerCurrent(
        const EmbeddedMediaSessionKey& sessionKey,
        const EmbeddedMediaPresentationState presentation) const noexcept {
        const auto endpoint = MediaEndpoint(presentation);
        const auto owner = endpoint
            ? mediaSessions_.EndpointOwner(*endpoint)
            : std::nullopt;
        const auto* session = mediaSessions_.Find(sessionKey);
        return endpoint && owner && *owner == sessionKey && session &&
            session->authority && session->committedGeometry &&
            session->authority->presentation == presentation;
    }

    void ApplyExternalContentSurfaceRecovery(
        const widgetrail::OverlayCompositionSurface::ExternalContentRetirement& retirement,
        const std::wstring_view reason) {
        if (!retirement.surfaceInvalidated) return;
        DiscardGraphicsResources();
        pendingWidgetPresentationImpact_.reset();
        retainedGuidePaintKey_.clear();
        retainedTrayPaintState_.reset();
        chromeAccessibilityProvider_.Clear();
        presentationTransaction_.RejectCompositionAdmission();
        AppendDiagnostic(
            L"Embedded media composition endpoint invalidated reason=" +
            std::wstring{reason} + L" cleanup-hr=" +
            std::to_wstring(static_cast<long>(retirement.cleanupResult)) +
            L" recovered=" + (retirement.surfaceRecovered ? L"1" : L"0"));
        if (!retirement.surfaceRecovered) {
            DisableCompositionFallback(L"external content endpoint retirement failed");
            return;
        }
        appliedOverlayOpacity_.reset();
        if (window_ && state_.surface() != widgetrail::Surface::Hidden)
            InvalidateRect(window_, nullptr, FALSE);
    }

    [[nodiscard]] widgetrail::OverlayCompositionSurface::ExternalContentRetirement
    RetireEmbeddedMediaCompositionEndpoint(
        const EmbeddedMediaPresentationState presentation,
        const std::wstring_view reason) {
        auto retirement = compositionSurface_.RetireExternalContentEndpoint(
            CompositionEndpoint(presentation),
            presentation == EmbeddedMediaPresentationState::CompactPinned);
        ApplyExternalContentSurfaceRecovery(retirement, reason);
        return retirement;
    }

    void RecordEmbeddedMediaPresentation(
        const widgetrail::OverlayCompositionSurface::ExternalContentEndpoint endpoint,
        const widgetrail::OverlayCompositionSurface::CommitTiming& timing,
        const std::wstring_view reason) {
        const auto counters = compositionSurface_.externalContentCommitCounters(endpoint);
        if (!timing.externalPresentationCommitted && counters.requested % 256 != 0) return;
        AppendDiagnostic(
            L"Embedded media presentation endpoint=" +
            std::wstring{endpoint ==
                    widgetrail::OverlayCompositionSurface::ExternalContentEndpoint::Pinned
                ? L"pinned" : L"overlay"} +
            L" result=" + (timing.externalPresentationCommitted ? L"committed" : L"unchanged") +
            L" requested=" + std::to_wstring(counters.requested) +
            L" committed=" + std::to_wstring(counters.committed) +
            L" reason=" + std::wstring{reason});
    }

    void RecordPinnedSurfaceWorkCounters() {
        if (!pinnedSurfaceCoordinator_.pinned()) {
            pinnedWorkDiagnosticWidgetId_.clear();
            pinnedWorkDiagnosticPublished_ = false;
            pinnedWorkDiagnosticPaintBucket_ = 0;
            pinnedWorkDiagnosticMediaReconciliations_ = 0;
            return;
        }
        const auto counters = pinnedSurfaceCoordinator_.workCounters();
        if (counters.paintMessages == 0) return;
        const std::wstring widgetId{pinnedSurfaceCoordinator_.widgetId()};
        if (widgetId != pinnedWorkDiagnosticWidgetId_) {
            pinnedWorkDiagnosticWidgetId_ = widgetId;
            pinnedWorkDiagnosticPublished_ = false;
            pinnedWorkDiagnosticPaintBucket_ = 0;
            pinnedWorkDiagnosticMediaReconciliations_ = 0;
        }
        constexpr std::uint64_t kPaintsPerDiagnostic = 64;
        const std::uint64_t paintBucket =
            counters.paintMessages / kPaintsPerDiagnostic;
        if (pinnedWorkDiagnosticPublished_ &&
            paintBucket <= pinnedWorkDiagnosticPaintBucket_ &&
            counters.mediaViewportReconciliations <=
                pinnedWorkDiagnosticMediaReconciliations_) {
            return;
        }
        pinnedWorkDiagnosticPublished_ = true;
        pinnedWorkDiagnosticPaintBucket_ = paintBucket;
        pinnedWorkDiagnosticMediaReconciliations_ =
            counters.mediaViewportReconciliations;
        const auto dcomp = compositionSurface_.externalContentCommitCounters(
            widgetrail::OverlayCompositionSurface::ExternalContentEndpoint::Pinned);
        AppendDiagnostic(
            L"Pinned presentation work widget=" + widgetId +
            L" snapshots=" + std::to_wstring(counters.snapshots) +
            L" invalidations=" + std::to_wstring(counters.invalidations) +
            L" coalesced=" +
                std::to_wstring(counters.coalescedInvalidations) +
            L" paints=" + std::to_wstring(counters.paintMessages) +
            L" raster-draws=" + std::to_wstring(counters.rasterDraws) +
            L" media-geometry=" +
                std::to_wstring(counters.mediaViewportReconciliations) +
            L" owner-signals=" +
                std::to_wstring(counters.ownerNotifications) +
            L" dcomp-requested=" + std::to_wstring(dcomp.requested) +
            L" dcomp-committed=" + std::to_wstring(dcomp.committed));
    }

    struct CompactPinnedMediaChromeColors final {
        widgetrail::NativeColor background;
        widgetrail::NativeColor track;
        widgetrail::NativeColor progress;
        widgetrail::NativeColor focus;
    };

    [[nodiscard]] CompactPinnedMediaChromeColors
    ResolveCompactPinnedMediaChromeColors() const noexcept {
        const auto colorOr = [](const std::optional<widgetrail::NativeColor>& value,
                                const widgetrail::NativeColor fallback) {
            return value.value_or(fallback);
        };
        const auto paintedLayer = [](
                const std::optional<widgetrail::NativeColor>& configured,
                const widgetrail::NativeColor fallback,
                const float opacity) {
            auto result = configured.value_or(fallback);
            result.alpha *= std::isfinite(opacity)
                ? std::clamp(opacity, 0.0F, 1.0F)
                : 1.0F;
            return result;
        };
        const widgetrail::NativeColor defaultSecondary{
            0x9B / 255.0F, 0xA3 / 255.0F, 0xB3 / 255.0F, 1.0F};
        auto background = paintedLayer(
            panelStyle_.background(), kDefaultPanel, panelStyle_.opacity());
        background.alpha = std::min(background.alpha, 0.82F);
        auto track = colorOr(hintStyle_.foreground(),
            colorOr(bodyStyle_.foreground(), defaultSecondary));
        track.alpha = std::min(track.alpha, 0.72F);
        const auto progress = trayItemSelectedFocusedStyle_.background()
            ? paintedLayer(trayItemSelectedFocusedStyle_.background(),
                           kDefaultAccent,
                           trayItemSelectedFocusedStyle_.opacity())
            : paintedLayer(trayItemSelectedStyle_.background(),
                           kDefaultAccent, trayItemSelectedStyle_.opacity());
        const auto focus = colorOr(
            trayItemSelectedFocusedStyle_.outlineColor(),
            colorOr(trayItemFocusedStyle_.outlineColor(), kDefaultAccent));
        return {background, track, progress, focus};
    }

    void ReconcileCompactPinnedMediaChrome(
        const EmbeddedMediaSessionKey& sessionKey) {
        auto* session = mediaSessions_.Find(sessionKey);
        const auto pinnedOwner =
            mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Pinned);
        if (!session || !session->authority || !session->clientBounds ||
            !session->committedGeometry ||
            !pinnedOwner || *pinnedOwner != sessionKey ||
            session->authority->presentation !=
                EmbeddedMediaPresentationState::CompactPinned ||
            !pinnedSurfaceCoordinator_.compactMediaPresentation() ||
            pinnedSurfaceCoordinator_.widgetId() !=
                session->authority->widgetId) return;
        const auto compact = pinnedSurfaceCoordinator_.compactMediaState();
        const double progress = compact.durationSeconds > 0.0
            ? std::clamp(compact.previewPositionSeconds / compact.durationSeconds,
                         0.0, 1.0)
            : 0.0;
        const auto colors = ResolveCompactPinnedMediaChromeColors();
        widgetrail::OverlayCompositionSurface::CommitTiming timing;
        const HRESULT result = compositionSurface_.CommitPinnedMediaChrome(
            {*session->clientBounds, compact.seekBarVisible,
             pinnedSurfaceCoordinator_.controllerFocused(),
             compact.scrubActive, progress, D2DColor(colors.background),
             D2DColor(colors.track), D2DColor(colors.progress),
             D2DColor(colors.focus),
             session->committedGeometry->rasterScale}, timing);
        if (FAILED(result)) AppendDiagnostic(
            L"Compact pinned media chrome commit failed hr=" +
            std::to_wstring(static_cast<long>(result)));
    }

    [[nodiscard]] static RECT Win32Rect(
        const widgetrail::PhysicalRect& bounds) noexcept {
        return {bounds.left, bounds.top, bounds.right, bounds.bottom};
    }

    [[nodiscard]] HWND EmbeddedMediaOwnerWindow(
        const EmbeddedMediaPresentationState presentation) const noexcept {
        return presentation == EmbeddedMediaPresentationState::CompactPinned
            ? pinnedSurfaceCoordinator_.window() : window_;
    }

    enum class ParkedOverlayMediaAuthorityReconciliation {
        NotApplicable,
        Current,
        Advanced,
        Rejected,
    };

    [[nodiscard]] ParkedOverlayMediaAuthorityReconciliation
    ReconcileParkedOverlayMediaAuthorityForTransfer(
        const EmbeddedMediaSessionKey& sessionKey,
        const EmbeddedMediaPresentationState destination) {
        auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority ||
            destination != EmbeddedMediaPresentationState::OverlayViewport ||
            session->authority->presentation !=
                EmbeddedMediaPresentationState::Parked) {
            return ParkedOverlayMediaAuthorityReconciliation::NotApplicable;
        }
        auto& authority = *session->authority;

        const auto reject = [this, &authority](const std::wstring_view predicate) {
            AppendActionCorrelation(
                L"stage=embedded-media-parked-authority widget=" +
                authority.widgetId + L" outcome=rejected predicate=" +
                std::wstring{predicate});
            return ParkedOverlayMediaAuthorityReconciliation::Rejected;
        };
        if (state_.surface() != widgetrail::Surface::Widget ||
            state_.activeWidget() != authority.widgetId)
            return reject(L"active-overlay-owner");

        const auto* descriptor = sessions_.FindDescriptor(
            authority.widgetId);
        const auto* snapshot = SnapshotFor(authority.widgetId);
        const auto presentation = sessions_.Presentation(
            authority.widgetId);
        if (!descriptor) return reject(L"descriptor");
        if (!snapshot) return reject(L"snapshot");
        if (!snapshot->embeddedMediaSession) return reject(L"declaration");
        if (!presentation.HasCommittedViewAuthority())
            return reject(L"presentation-authority");
        if (presentation.snapshot != snapshot)
            return reject(L"presentation-snapshot");
        if (descriptor->id != authority.widgetId ||
            descriptor->instanceId != authority.instanceId ||
            snapshot->instanceId != authority.instanceId)
            return reject(L"instance");
        if (descriptor->runtimeGeneration !=
            authority.runtimeGeneration)
            return reject(L"runtime-generation");
        if (descriptor->presentationGeneration !=
            authority.presentationGeneration)
            return reject(L"presentation-generation");
        if (snapshot->embeddedMediaSession->id != authority.sessionId)
            return reject(L"surface");
        const auto declaresViewport = [](const auto& self,
                                         const widgetrail::WidgetNode& node) -> bool {
            if (node.kind == L"mediaViewport") return true;
            return std::any_of(
                node.children.begin(), node.children.end(),
                [&](const auto& child) { return self(self, child); });
        };
        if (declaresViewport(declaresViewport, snapshot->root))
            return reject(L"route-still-declares-viewport");
        if (!widgetrail::SameEmbeddedMediaDocumentIdentity(
                authority.documentIdentity,
                widgetrail::MakeEmbeddedMediaDocumentIdentity(
                    *snapshot->embeddedMediaSession)))
            return reject(L"resource-contract");
        if (snapshot->sequence <= 0 ||
            snapshot->sequence < authority.sequence)
            return reject(L"snapshot-sequence");
        if (snapshot->sequence == authority.sequence)
            return ParkedOverlayMediaAuthorityReconciliation::Current;

        const auto priorSequence = authority.sequence;
        AdvanceCompatibleEmbeddedMediaCommandAuthority(
            sessionKey, *snapshot, *snapshot->embeddedMediaSession);
        AppendActionCorrelation(
            L"stage=embedded-media-parked-authority widget=" +
            authority.widgetId + L" outcome=advanced prior=" +
            std::to_wstring(priorSequence) + L" current=" +
            std::to_wstring(authority.sequence));
        return ParkedOverlayMediaAuthorityReconciliation::Advanced;
    }

    [[nodiscard]] EmbeddedMediaTransferResult
    ExecuteEmbeddedMediaPresentationTransfer(
        EmbeddedMediaSession& session,
        const EmbeddedMediaPresentationState destination,
        const widgetrail::media::EndpointGeometry& desired,
        const std::wstring_view reason,
        bool* const transferPending = nullptr) {
        if (transferPending) *transferPending = false;
        if (!session.authority || !session.coordinator)
            return EmbeddedMediaTransferResult::Failed;
        auto& authority = *session.authority;
        const auto mediaSurface = session.coordinator;
        const auto sessionKey = session.key;
        if (ReconcileParkedOverlayMediaAuthorityForTransfer(
                sessionKey, destination) ==
            ParkedOverlayMediaAuthorityReconciliation::Rejected) {
            return EmbeddedMediaTransferResult::Failed;
        }
        const auto sourceAuthority = authority;
        const auto sourceBounds = session.clientBounds;
        const auto sourceClip = session.clientClip;
        const auto source = sourceAuthority.presentation;
        const bool sourceParked =
            source == EmbeddedMediaPresentationState::Parked;
        const std::wstring widgetId{authority.widgetId};
        const std::wstring transferReason{reason};
        const auto presentationName = [](
                const EmbeddedMediaPresentationState presentation) {
            switch (presentation) {
            case EmbeddedMediaPresentationState::Parked:
                return std::wstring_view{L"parking"};
            case EmbeddedMediaPresentationState::OverlayViewport:
                return std::wstring_view{L"overlay"};
            case EmbeddedMediaPresentationState::OverlayFullscreen:
                return std::wstring_view{L"fullscreen"};
            case EmbeddedMediaPresentationState::CompactPinned:
                return std::wstring_view{L"pinned"};
            }
            return std::wstring_view{L"unknown"};
        };
        const bool resumeDetached = mediaSurface->presentationTransferPending();
        bool destinationEndpointInitialized = false;
        bool destinationVisualCreated = false;
        // Every abandoned transfer below is classified deliberately. Fatal
        // means the session cannot be trusted again: its authority is gone, or
        // its controller or composition graph was left in a state this host
        // cannot reason about. Deferred means the step simply could not run
        // yet; the session stays resident and the next reconcile retries.
        // Both dispositions run the same destination cleanup, because a
        // half-created endpoint must never outlive the attempt that made it.
        const auto abandonTransfer = [
            this, destination, mediaSurface, sessionKey, sourceAuthority,
            sourceBounds, sourceClip, widgetId, transferReason,
            &destinationEndpointInitialized, &destinationVisualCreated](
            const std::wstring_view operation, const HRESULT failure,
            const bool fatal) {
            AppendActionCorrelation(
                std::wstring{fatal
                    ? L"stage=embedded-media-transfer-fault widget="
                    : L"stage=embedded-media-transfer-deferred widget="} +
                widgetId +
                L" operation=" + std::wstring{operation} + L" hr=" +
                std::to_wstring(static_cast<long>(failure)) + L" reason=" +
                transferReason);
            if (destinationVisualCreated) {
                (void)RetireEmbeddedMediaCompositionEndpoint(
                    destination, L"failed transfer destination");
            } else if (destination ==
                           EmbeddedMediaPresentationState::CompactPinned &&
                       destinationEndpointInitialized) {
                (void)RetireEmbeddedMediaCompositionEndpoint(
                    destination, L"failed transfer pinned endpoint");
            }
            // The manager owns the transition after this exact operation
            // reports its disposition. No selected-session state is restored.
            return fatal ? EmbeddedMediaTransferResult::Failed
                         : EmbeddedMediaTransferResult::Deferred;
        };
        const auto failTransfer = [&abandonTransfer](
            const std::wstring_view operation, const HRESULT failure) {
            return abandonTransfer(operation, failure, true);
        };
        const auto deferTransfer = [&abandonTransfer](
            const std::wstring_view operation, const HRESULT failure) {
            return abandonTransfer(operation, failure, false);
        };
        // A composition controller whose RootVisualTarget was cleared is not a
        // reusable presentation owner. Only the live-retarget path below may
        // preserve a session across projection changes.
        if (resumeDetached)
            return failTransfer(L"detached-controller", E_UNEXPECTED);
        // Initialize returns once the shared environment exists, while the
        // controller is still created asynchronously. Attaching a presentation
        // before it exists is rejected by the coordinator admission guard, and
        // the failure cleanup would retire the destination visual and the
        // session that the pending controller is still being created for.
        // Defer instead; OnRichMediaStateChanged reconciles this session again
        // when the controller reaches a presentable lifecycle.
        const auto controllerLifecycle = mediaSurface->state().lifecycle;
        if (controllerLifecycle !=
                widgetrail::richmedia::Lifecycle::ReadyHidden &&
            controllerLifecycle != widgetrail::richmedia::Lifecycle::Visible) {
            if (transferPending) *transferPending = true;
            AppendActionCorrelation(
                L"stage=embedded-media-transfer-deferred widget=" + widgetId +
                L" destination=" +
                std::wstring{presentationName(destination)} +
                L" reason=" + transferReason);
            return EmbeddedMediaTransferResult::Deferred;
        }
        const HWND owner = EmbeddedMediaOwnerWindow(destination);
        if (!owner || desired.ownerWindow != owner)
            return deferTransfer(L"destination-owner", E_HANDLE);
        if (destination == EmbeddedMediaPresentationState::CompactPinned &&
            !pinnedSurfaceCoordinator_.CurrentMediaViewport(
                authority.sessionId))
            return deferTransfer(L"destination-viewport", E_INVALIDARG);
        AppendActionCorrelation(
            L"stage=embedded-media-transfer-begin widget=" +
            widgetId + L" source=" +
            (sourceParked ? std::wstring{L"parking"}
                          : std::wstring{presentationName(source)}) + L" destination=" +
            std::wstring{presentationName(destination)} + L" pending=" +
            (resumeDetached ? L"true" : L"false") + L" reason=" +
            transferReason);
        widgetrail::OverlayCompositionSurface::CommitTiming detachTiming;
        HRESULT result = S_OK;
        if (destination == EmbeddedMediaPresentationState::CompactPinned) {
            std::wstring error;
            if (!compositionSurface_.InitializePinnedExternalContentEndpoint(owner, error)) {
                AppendDiagnostic(L"Embedded media pinned endpoint failed: " + error);
                // The pinned endpoint can be momentarily unavailable while a
                // prior pin retires. Defer so the next reconcile retries
                // instead of destroying a live media session.
                if (transferPending) *transferPending = true;
                AppendActionCorrelation(
                    L"stage=embedded-media-transfer-deferred widget=" + widgetId +
                    L" destination=pinned reason=pinned-endpoint-initialize");
                return EmbeddedMediaTransferResult::Deferred;
            }
            destinationEndpointInitialized = true;
        }
        const RECT hostBounds = desired.bounds;
        const RECT hostClip = desired.clip;
        const RECT controllerBounds = desired.controllerBounds;
        if (hostBounds.right <= hostBounds.left ||
            hostBounds.bottom <= hostBounds.top ||
            hostClip.right <= hostClip.left || hostClip.bottom <= hostClip.top ||
            controllerBounds.right <= controllerBounds.left ||
            controllerBounds.bottom <= controllerBounds.top)
            return deferTransfer(L"geometry-invalid", E_INVALIDARG);
        Microsoft::WRL::ComPtr<IUnknown> target;
        result = compositionSurface_.CreateExternalContentTarget(
            CompositionEndpoint(destination), &target);
        if (FAILED(result))
            return deferTransfer(L"composition-target-create", result);
        destinationVisualCreated = true;
        const auto endpoint = CompositionEndpoint(destination);
        widgetrail::OverlayCompositionSurface::CommitTiming stageTiming;
        result = compositionSurface_.StageExternalContentPresentation(
            endpoint, hostBounds, hostClip, stageTiming);
        if (FAILED(result))
            return deferTransfer(L"destination-presentation-stage", result);
        RecordEmbeddedMediaPresentation(
            endpoint, stageTiming, L"transfer-destination-staged");
        widgetrail::richmedia::PresentationTransferFailureStage failureStage{};
        result = mediaSurface->BeginPresentationRetarget(&failureStage);
        if (FAILED(result))
            return deferTransfer(
                widgetrail::richmedia::PresentationTransferFailureStageValue(
                    failureStage), result);
        AppendActionCorrelation(
            L"stage=embedded-media-transfer-retarget widget=" +
            widgetId + L" source=" +
            (sourceParked ? std::wstring{L"parking"}
                          : std::wstring{presentationName(source)}) + L" destination=" +
            std::wstring{presentationName(destination)});
        widgetrail::richmedia::PresentationTarget presentation;
        presentation.ownerWindow = owner;
        presentation.compositionTarget = std::move(target);
        presentation.bounds = controllerBounds;
        presentation.rasterScale = desired.rasterScale;
        const auto presentationRevealEnabled = std::make_shared<bool>(false);
        presentation.setPresentationVisible =
            [this, sessionKey, destination, endpoint,
             presentationRevealEnabled](const bool visible) {
          InvokeMediaCallbackGuarded(L"transfer-visibility", [&] {
            auto* current = mediaSessions_.Find(sessionKey);
            if (!current || !current->authority ||
                !current->committedGeometry ||
                !EmbeddedMediaEndpointOwnerCurrent(sessionKey, destination))
                return;
            const auto& geometry = *current->committedGeometry;
            widgetrail::OverlayCompositionSurface::CommitTiming timing;
            (void)compositionSurface_.CommitExternalContentPresentation(
                endpoint, geometry.bounds, geometry.clip,
                *presentationRevealEnabled && visible, timing);
            RecordEmbeddedMediaPresentation(endpoint, timing, L"transfer-visibility");
          });
        };
        result = mediaSurface->CompletePresentationTransfer(
            std::move(presentation), &failureStage);
        if (FAILED(result))
            return failTransfer(
                widgetrail::richmedia::PresentationTransferFailureStageValue(
                    failureStage), result);
        auto* current = mediaSessions_.Find(sessionKey);
        if (!current || current->coordinator.get() != mediaSurface.get() ||
            !current->authority)
            return deferTransfer(L"authority-changed", E_UNEXPECTED);
        current->authority->projectionDeferralRecorded = false;
        current->clientBounds = hostBounds;
        current->clientClip = hostClip;
        const auto committedPinnedViewport =
            destination == EmbeddedMediaPresentationState::CompactPinned
                ? pinnedSurfaceCoordinator_.CurrentMediaViewport(
                      current->authority->sessionId)
                : std::nullopt;
        if (destination == EmbeddedMediaPresentationState::CompactPinned &&
            !committedPinnedViewport)
            return deferTransfer(L"destination-viewport", E_INVALIDARG);
        current->authority->pinnedFrameGeneration = committedPinnedViewport
            ? committedPinnedViewport->frameGeneration
            : 0;
        *presentationRevealEnabled = true;
        const bool presentationVisible = desired.visible &&
            mediaSurface->state().lifecycle ==
                widgetrail::richmedia::Lifecycle::Visible;
        result = compositionSurface_.CommitExternalContentPresentation(
            endpoint, hostBounds, hostClip, presentationVisible, detachTiming);
        if (FAILED(result))
            return deferTransfer(L"destination-presentation-commit", result);
        RecordEmbeddedMediaPresentation(
            endpoint, detachTiming, L"transfer-destination-committed");
        const HRESULT visibilityResult = mediaSurface->SetVisible(
            desired.visible);
        if (FAILED(visibilityResult))
            return deferTransfer(L"controller-visibility-finalize", visibilityResult);
        if (desired.visible &&
            mediaSurface->state().lifecycle !=
                widgetrail::richmedia::Lifecycle::Visible) {
            // The controller accepted the request but is not showing yet.
            // Defer so a later reconcile reveals it rather than recording this
            // presentation as complete.
            return deferTransfer(L"controller-visibility-pending", S_FALSE);
        }
        if (!sourceParked &&
            CompositionEndpoint(source) != CompositionEndpoint(destination)) {
            const auto sourceEndpoint = MediaEndpoint(source);
            const auto sourceOwner = sourceEndpoint
                ? mediaSessions_.EndpointOwner(*sourceEndpoint)
                : std::nullopt;
            if (!sourceOwner || *sourceOwner != sessionKey)
                return deferTransfer(L"source-endpoint-authority", E_ACCESSDENIED);
            const auto retirement = RetireEmbeddedMediaCompositionEndpoint(
                source, L"transfer source retirement");
            result = retirement.cleanupResult;
            if (retirement.surfaceInvalidated)
                return failTransfer(L"source-endpoint-retire", result);
        }
        if (FAILED(result))
            return failTransfer(L"source-endpoint-retire", result);
        RecordEmbeddedMediaPresentation(endpoint, detachTiming, L"transfer-complete");
        AppendDiagnostic(
            L"Embedded media transferred widget=" + widgetId +
            L" projection=" +
            std::wstring{presentationName(destination)} +
            L" reason=" + transferReason);
        AppendActionCorrelation(
            L"stage=embedded-media-transfer-complete widget=" +
            widgetId + L" destination=" +
            std::wstring{presentationName(destination)} + L" reason=" +
            transferReason);
        return EmbeddedMediaTransferResult::Completed;
    }

    [[nodiscard]] HRESULT ExecuteEmbeddedMediaPresentationUpdate(
        const EmbeddedMediaSessionKey& sessionKey,
        EmbeddedMediaSession& session,
        const EmbeddedMediaPresentationState presentation,
        const widgetrail::media::EndpointGeometry& desired,
        const std::wstring_view reason) {
        // These are currency preconditions, not corruption. When one does not
        // hold the update simply does not apply to the session's present
        // state, which happens routinely while an overlay hide/show or route
        // change is still settling. Defer so the next reconcile re-plans;
        // failing here would destroy a live media session.
        if (session.key != sessionKey || !session.authority ||
            !session.coordinator || !session.committedGeometry ||
            session.authority->presentation != presentation ||
            !EmbeddedMediaEndpointOwnerCurrent(sessionKey, presentation) ||
            desired.ownerWindow != EmbeddedMediaOwnerWindow(presentation)) {
            AppendDiagnostic(
                L"Embedded media update deferred reason=" +
                std::wstring{reason} +
                L" key-match=" + (session.key == sessionKey ? L"1" : L"0") +
                L" authority=" + (session.authority ? L"1" : L"0") +
                L" coordinator=" + (session.coordinator ? L"1" : L"0") +
                L" committed-geometry=" +
                (session.committedGeometry ? L"1" : L"0") +
                L" presentation-match=" +
                (session.authority &&
                 session.authority->presentation == presentation
                    ? L"1" : L"0") +
                L" endpoint-owner-current=" +
                (EmbeddedMediaEndpointOwnerCurrent(sessionKey, presentation)
                    ? L"1" : L"0") +
                L" owner-window-match=" +
                (desired.ownerWindow == EmbeddedMediaOwnerWindow(presentation)
                    ? L"1" : L"0"));
            return E_PENDING;
        }

        const auto mediaSurface = session.coordinator;
        // A geometry or visibility step that cannot apply right now is not a
        // session fault. Defer so the session survives and the next reconcile
        // reapplies the current geometry.
        HRESULT result = mediaSurface->UpdateGeometry(
            desired.controllerBounds, desired.rasterScale);
        if (FAILED(result)) return E_PENDING;

        const auto endpoint = CompositionEndpoint(presentation);
        if (!desired.visible) {
            result = mediaSurface->SetVisible(false);
            if (FAILED(result)) return E_PENDING;
        }

        widgetrail::OverlayCompositionSurface::CommitTiming timing;
        result = compositionSurface_.CommitExternalContentPresentation(
            endpoint, desired.bounds, desired.clip, desired.visible, timing);
        RecordEmbeddedMediaPresentation(endpoint, timing, reason);
        if (FAILED(result)) {
            (void)mediaSurface->SetVisible(false);
            return E_PENDING;
        }

        if (presentation ==
                EmbeddedMediaPresentationState::OverlayFullscreen) {
            AppendDiagnostic(
                L"Embedded media fullscreen update visible=" +
                std::wstring{desired.visible ? L"1" : L"0"} +
                L" lifecycle=" + std::to_wstring(static_cast<int>(
                    mediaSurface->state().lifecycle)) +
                L" bounds=" + std::to_wstring(desired.bounds.left) + L"," +
                std::to_wstring(desired.bounds.top) + L"," +
                std::to_wstring(desired.bounds.right) + L"," +
                std::to_wstring(desired.bounds.bottom) +
                L" controller=" +
                std::to_wstring(desired.controllerBounds.left) + L"," +
                std::to_wstring(desired.controllerBounds.top) + L"," +
                std::to_wstring(desired.controllerBounds.right) + L"," +
                std::to_wstring(desired.controllerBounds.bottom) +
                L" commit-hr=" + std::to_wstring(static_cast<long>(result)));
        }
        if (desired.visible) {
            result = mediaSurface->SetVisible(true);
            if (FAILED(result)) {
                widgetrail::OverlayCompositionSurface::CommitTiming rollbackTiming;
                (void)compositionSurface_.CommitExternalContentPresentation(
                    endpoint, desired.bounds, desired.clip, false, rollbackTiming);
                RecordEmbeddedMediaPresentation(
                    endpoint, rollbackTiming, L"visibility-rollback");
                return E_PENDING;
            }
            // SetVisible reports success without revealing the controller when
            // the document is not ready yet. Reporting success here would let
            // the manager record this geometry as committed, and its
            // equivalent-presentation dedupe would never retry the reveal,
            // leaving a permanently hidden controller that still plays audio.
            if (mediaSurface->state().lifecycle !=
                widgetrail::richmedia::Lifecycle::Visible) {
                AppendDiagnostic(
                    L"Embedded media reveal deferred reason=" +
                    std::wstring{reason} + L" presentation=" +
                    std::to_wstring(static_cast<int>(presentation)) +
                    L" lifecycle=" + std::to_wstring(static_cast<int>(
                        mediaSurface->state().lifecycle)));
                return E_PENDING;
            }
        }

        session.clientBounds = desired.bounds;
        session.clientClip = desired.clip;
        session.authority->pinnedFrameGeneration =
            presentation == EmbeddedMediaPresentationState::CompactPinned
                ? desired.committedFrameGeneration
                : 0;
        if (presentation == EmbeddedMediaPresentationState::CompactPinned)
            ReconcileCompactPinnedMediaChrome(sessionKey);
        return S_OK;
    }

    [[nodiscard]] widgetrail::media::TransitionOperations
    EmbeddedMediaTransitionOperations(const std::wstring_view reason) {
        const std::wstring ownedReason{reason};
        return {
            [this, ownedReason](
                const EmbeddedMediaSessionKey& exactKey,
                EmbeddedMediaSession& session,
                const widgetrail::media::ParkingReason parkingReason) {
                if (session.key != exactKey) return E_ACCESSDENIED;
                return ExecuteParkEmbeddedMediaSession(
                    session, parkingReason, ownedReason);
            },
            [this, ownedReason](
                const EmbeddedMediaSessionKey& exactKey,
                EmbeddedMediaSession& session,
                const EmbeddedMediaPresentationState presentation,
                const widgetrail::media::EndpointGeometry& desired) {
                if (session.key != exactKey) return E_ACCESSDENIED;
                const auto result = ExecuteEmbeddedMediaPresentationTransfer(
                    session, presentation, desired, ownedReason);
                return result == EmbeddedMediaTransferResult::Completed
                    ? S_OK
                    : result == EmbeddedMediaTransferResult::Deferred
                        ? E_PENDING
                        : E_FAIL;
            },
            [this, ownedReason](
                const EmbeddedMediaSessionKey& exactKey,
                EmbeddedMediaSession& session,
                const EmbeddedMediaPresentationState presentation,
                const widgetrail::media::EndpointGeometry& desired) {
                return ExecuteEmbeddedMediaPresentationUpdate(
                    exactKey, session, presentation, desired,
                    ownedReason + L"-geometry-update");
            },
            [this, ownedReason](
                const EmbeddedMediaSessionKey& exactKey,
                EmbeddedMediaSession& session) {
                if (session.key != exactKey) return E_ACCESSDENIED;
                return ExecuteRetireEmbeddedMediaSession(session, ownedReason);
            },
            [this, ownedReason](
                const EmbeddedMediaSessionKey& exactKey,
                EmbeddedMediaSession& session) {
                if (session.key != exactKey) return E_ACCESSDENIED;
                return ExecuteRetireEmbeddedMediaSession(
                    session, ownedReason + L"-fault");
            },
        };
    }

    [[nodiscard]] HRESULT ReconcileEmbeddedMediaPresentation(
        const EmbeddedMediaSessionKey& sessionKey,
        const EmbeddedMediaPresentationState destination,
        const std::wstring_view reason,
        bool* const transferPending = nullptr,
        const widgetrail::media::ParkingReason requestedParkingReason =
            widgetrail::media::ParkingReason::EndpointUnavailable,
        const std::optional<widgetrail::media::EndpointGeometry>&
            committedGeometryOverride = std::nullopt) {
        if (transferPending) *transferPending = false;
        auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
        const auto& authority = *session->authority;
        const auto currentKey = CurrentEmbeddedMediaSessionKey(authority.widgetId);
        const auto* snapshot = SnapshotFor(authority.widgetId);
        const bool declarationCurrent =
            currentKey && *currentKey == sessionKey && snapshot &&
            snapshot->embeddedMediaSession;
        const bool documentCurrent = declarationCurrent &&
            widgetrail::SameEmbeddedMediaDocumentIdentity(
                authority.documentIdentity,
                widgetrail::MakeEmbeddedMediaDocumentIdentity(
                    *snapshot->embeddedMediaSession));

        widgetrail::media::GeometryState geometry =
            widgetrail::media::GeometryState::Pending;
        std::optional<widgetrail::media::EndpointGeometry> desiredGeometry;
        bool endpointAvailable = destination ==
            EmbeddedMediaPresentationState::Parked;
        const auto endpointGeometry = [this](
            const widgetrail::MediaViewportPresentationGeometry& resolved,
            const HWND owner,
            const double rasterScale,
            const bool visible,
            const std::uint64_t committedFrameGeneration = 0) {
            return widgetrail::media::EndpointGeometry{
                owner,
                {resolved.hostBounds.left, resolved.hostBounds.top,
                 resolved.hostBounds.right, resolved.hostBounds.bottom},
                {resolved.hostClip.left, resolved.hostClip.top,
                 resolved.hostClip.right, resolved.hostClip.bottom},
                {resolved.controllerBounds.left, resolved.controllerBounds.top,
                 resolved.controllerBounds.right,
                 resolved.controllerBounds.bottom},
                rasterScale,
                visible,
                committedFrameGeneration,
            };
        };
        if (destination == EmbeddedMediaPresentationState::OverlayViewport) {
            const auto ordinary =
                ResolveOrdinaryOverlayEmbeddedMediaPresentationGeometry(
                    sessionKey, authority.sequence);
            geometry = ordinary.status == OrdinaryEmbeddedMediaGeometryStatus::Valid
                ? widgetrail::media::GeometryState::Ready
                : ordinary.status ==
                      OrdinaryEmbeddedMediaGeometryStatus::Unavailable
                    ? widgetrail::media::GeometryState::Pending
                    : widgetrail::media::GeometryState::Invalid;
            endpointAvailable = window_ != nullptr;
            if (ordinary.geometry && window_)
                desiredGeometry = endpointGeometry(
                    *ordinary.geometry, window_, MediaPixelsPerDip(window_),
                    state_.surface() == widgetrail::Surface::Widget &&
                        state_.activeWidget() == authority.widgetId &&
                        IsWindowVisible(window_));
        } else if (destination ==
                   EmbeddedMediaPresentationState::OverlayFullscreen) {
            const auto resolved = ResolveEmbeddedMediaPresentationGeometryForState(
                sessionKey, destination, authority.sequence);
            geometry = resolved ? widgetrail::media::GeometryState::Ready
                                : widgetrail::media::GeometryState::Pending;
            endpointAvailable = window_ != nullptr;
            if (resolved && window_)
                desiredGeometry = endpointGeometry(
                    *resolved, window_, MediaPixelsPerDip(window_),
                    state_.surface() == widgetrail::Surface::Widget &&
                        state_.activeWidget() == authority.widgetId &&
                        IsWindowVisible(window_));
        } else if (destination ==
                   EmbeddedMediaPresentationState::CompactPinned) {
            const auto pinned = pinnedSurfaceCoordinator_.CurrentMediaViewport(
                authority.sessionId);
            geometry = pinned ? widgetrail::media::GeometryState::Ready
                              : widgetrail::media::GeometryState::Pending;
            endpointAvailable = pinnedSurfaceCoordinator_.pinned() &&
                pinnedSurfaceCoordinator_.compactMediaPresentation() &&
                pinnedSurfaceCoordinator_.widgetId() == authority.widgetId;
            if (pinned && pinnedSurfaceCoordinator_.window()) {
                if (const auto resolved =
                        ResolveEmbeddedMediaPresentationGeometryForState(
                            sessionKey, destination, authority.sequence)) {
                    desiredGeometry = endpointGeometry(
                        *resolved, pinnedSurfaceCoordinator_.window(),
                        static_cast<double>(std::max(
                            1U, GetDpiForWindow(
                                pinnedSurfaceCoordinator_.window()))) / 96.0,
                        pinnedSurfaceCoordinator_.pinned() &&
                            pinnedSurfaceCoordinator_.widgetId() ==
                                authority.widgetId &&
                            IsWindowVisible(pinnedSurfaceCoordinator_.window()),
                        pinned->frameGeneration);
                }
            }
        }
        if (committedGeometryOverride) {
            desiredGeometry = committedGeometryOverride;
            geometry = widgetrail::media::GeometryState::Ready;
            endpointAvailable = committedGeometryOverride->ownerWindow != nullptr;
        }
        const auto parkingReason = destination ==
                EmbeddedMediaPresentationState::Parked
            ? requestedParkingReason
            : widgetrail::media::ParkingReason::EndpointUnavailable;
        auto operations = EmbeddedMediaTransitionOperations(reason);
        const HRESULT result = mediaSessions_.Reconcile(
            sessionKey,
            {
                declarationCurrent,
                documentCurrent,
                destination != EmbeddedMediaPresentationState::Parked,
                endpointAvailable,
                geometry,
                destination,
                parkingReason,
                desiredGeometry,
            },
            operations);
        // A fault destroys the session, so record the exact reconcile input
        // that produced it. Reconcile reports E_INVALIDARG after running the
        // fault effect.
        if (result == E_INVALIDARG) {
            AppendDiagnostic(
                L"Embedded media reconcile faulted widget=" + authority.widgetId +
                L" session=" + authority.sessionId +
                L" destination=" +
                std::to_wstring(static_cast<int>(destination)) +
                L" geometry=" + std::to_wstring(static_cast<int>(geometry)) +
                L" endpoint-requested=" +
                (destination != EmbeddedMediaPresentationState::Parked
                    ? L"1" : L"0") +
                L" endpoint-available=" + (endpointAvailable ? L"1" : L"0") +
                L" desired-geometry=" + (desiredGeometry ? L"1" : L"0") +
                L" declaration-current=" + (declarationCurrent ? L"1" : L"0") +
                L" document-current=" + (documentCurrent ? L"1" : L"0") +
                L" override=" + (committedGeometryOverride ? L"1" : L"0") +
                L" pinned=" + (pinnedSurfaceCoordinator_.pinned() ? L"1" : L"0") +
                L" compact=" +
                (pinnedSurfaceCoordinator_.compactMediaPresentation()
                    ? L"1" : L"0") +
                L" surface=" + std::to_wstring(
                    static_cast<int>(state_.surface())) +
                L" reason=" + std::wstring{reason});
        }
        if (FAILED(result) && result != E_PENDING) {
            AppendDiagnostic(
                L"Embedded media reconcile failed widget=" + authority.widgetId +
                L" destination=" +
                std::to_wstring(static_cast<int>(destination)) +
                L" geometry=" + std::to_wstring(static_cast<int>(geometry)) +
                L" endpoint-available=" + (endpointAvailable ? L"1" : L"0") +
                L" desired-geometry=" + (desiredGeometry ? L"1" : L"0") +
                L" hr=" + std::to_wstring(static_cast<long>(result)) +
                L" reason=" + std::wstring{reason});
        }
        if (result == E_PENDING && transferPending) *transferPending = true;
        if (SUCCEEDED(result) && destination ==
                EmbeddedMediaPresentationState::CompactPinned)
            ReconcileCompactPinnedMediaChrome(sessionKey);
        return result;
    }

    [[nodiscard]] HRESULT ExecuteRetireEmbeddedMediaSession(
        EmbeddedMediaSession& session,
        const std::wstring_view reason) {
        if (!session.authority || !session.coordinator) return S_FALSE;
        // Retirement erases this record, so keep the controller alive for the
        // ordered teardown below.
        const auto mediaSurface = session.coordinator;
        auto& authority = *session.authority;
        const bool commandRetired =
            !authority.commandOrigin ||
            authority.commandOrigin->stage ==
                widgetrail::richmedia::PlaybackCommandStage::Terminal ||
            PublishEmbeddedMediaCommandDisposition(
                session.key,
                L"media-command-canceled",
                widgetrail::richmedia::PlaybackTerminalSource::
                    AuthorityRetirement);
        if (!commandRetired)
            sessions_.RecordFailure(
                authority.widgetId,
                widgetrail::WidgetSessionFailureStage::Protocol,
                L"Embedded media command cancellation could not be delivered.");
        mediaSurface->BeginSessionTeardown();
        const auto presentation = authority.presentation;
        widgetrail::OverlayCompositionSurface::ExternalContentRetirement retirement{
            S_FALSE, false, false};
        if (const auto endpoint = MediaEndpoint(presentation)) {
            const auto owner = mediaSessions_.EndpointOwner(*endpoint);
            if (owner && *owner == session.key) {
                retirement = RetireEmbeddedMediaCompositionEndpoint(
                    presentation, L"session stop");
            }
        }
        mediaSurface->CompleteSessionTeardown();
        if (embeddedMediaAccessibilityOwner_ &&
            *embeddedMediaAccessibilityOwner_ == session.key) {
            accessibilityProvider_.SetEmbeddedFragmentRoot(nullptr);
            embeddedMediaAccessibilityOwner_.reset();
        }
        AppendDiagnostic(
            L"Embedded media retired widget=" + authority.widgetId +
            L" surface=" + authority.sessionId + L" reason=" +
            std::wstring{reason} + L" cleanup-hr=" +
            std::to_wstring(static_cast<long>(retirement.cleanupResult)) +
            L" terminal=" + (retirement.terminal() ? L"1" : L"0") +
            L" surface-invalidated=" +
            (retirement.surfaceInvalidated ? L"1" : L"0") +
            L" surface-recovered=" +
            (retirement.surfaceRecovered ? L"1" : L"0"));
        mediaSurface->Shutdown();
        // Teardown above is irreversible. A composition cleanup failure is
        // preserved in diagnostics and may rebuild the graph, but it cannot
        // leave this closed coordinator registered as a live endpoint owner.
        return S_OK;
    }

    void StopEmbeddedMediaSession(
        const EmbeddedMediaSessionKey& sessionKey,
        const std::wstring_view reason) {
        if (committedFullscreenPresentation_ &&
            committedFullscreenPresentation_->sessionKey == sessionKey) {
            committedFullscreenPresentation_.reset();
        }
        auto operations = EmbeddedMediaTransitionOperations(reason);
        const widgetrail::media::TransitionInput input{
            false, false, false, false,
            widgetrail::media::GeometryState::Invalid,
            EmbeddedMediaPresentationState::Parked,
            widgetrail::media::ParkingReason::EndpointUnavailable,
        };
        (void)mediaSessions_.Reconcile(sessionKey, input, operations);
        AppendDiagnostic(
            L"Embedded media residency requested=" +
            std::to_wstring(mediaSessions_.size()) +
            L" resident=" +
            std::to_wstring(mediaSessions_.size()) +
            L" limit=" +
            std::to_wstring(widgetrail::media::MaximumResidentSessions));
    }

    void RetireEmbeddedMediaSessionsForWidget(
        const std::wstring_view widgetId,
        const std::wstring_view reason,
        const std::optional<EmbeddedMediaSessionKey>& exceptKey = std::nullopt) {
        for (const auto& key : mediaSessions_.KeysForWidget(widgetId)) {
            if (exceptKey && key == *exceptKey) continue;
            StopEmbeddedMediaSession(key, reason);
        }
    }

    [[nodiscard]] bool RetainedHiddenEmbeddedMediaAuthorityCurrent(
        const EmbeddedMediaSessionKey& sessionKey) const noexcept {
        const auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority ||
            session->authority->presentation !=
                EmbeddedMediaPresentationState::Parked)
            return false;
        const auto& authority = *session->authority;
        const auto* descriptor = sessions_.FindDescriptor(
            authority.widgetId);
        const auto* snapshot = SnapshotFor(authority.widgetId);
        const auto presentation = sessions_.Presentation(
            authority.widgetId);
        const bool presentationAuthorityCurrent =
            presentation.HasCommittedViewAuthority();
        if (!descriptor || !snapshot || !snapshot->embeddedMediaSession)
            return false;
        const auto declaresViewport = [](const auto& self,
                                         const widgetrail::WidgetNode& node) -> bool {
            if (node.kind == L"mediaViewport") return true;
            return std::any_of(
                node.children.begin(), node.children.end(),
                [&](const auto& child) { return self(self, child); });
        };
        return !declaresViewport(declaresViewport, snapshot->root) &&
            presentationAuthorityCurrent &&
            presentation.snapshot == snapshot &&
            descriptor->id == authority.widgetId &&
            descriptor->instanceId == authority.instanceId &&
            snapshot->instanceId == authority.instanceId &&
            descriptor->runtimeGeneration == authority.runtimeGeneration &&
            descriptor->presentationGeneration == authority.presentationGeneration &&
            snapshot->sequence == authority.sequence &&
            snapshot->embeddedMediaSession->id == authority.sessionId &&
            widgetrail::SameEmbeddedMediaDocumentIdentity(
                authority.documentIdentity,
                widgetrail::MakeEmbeddedMediaDocumentIdentity(
                    *snapshot->embeddedMediaSession));
    }

    void HandlePinnedSurfaceWindowRetirement(
        const widgetrail::pinned::WidgetSurfaceStopReason reason) {
        const auto sessionKey =
            mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Pinned);
        auto* session = sessionKey ? mediaSessions_.Find(*sessionKey) : nullptr;
        if (!session || !session->authority ||
            session->authority->presentation !=
                EmbeddedMediaPresentationState::CompactPinned ||
            session->authority->widgetId !=
                pinnedSurfaceCoordinator_.widgetId()) {
            // No media session owns the pinned endpoint, but the endpoint is
            // bound to the HWND that is about to be destroyed. A composition
            // target and content visual left behind here outlive their window
            // and make the next pin inherit an orphaned endpoint, so retire it
            // unconditionally. Retirement is a no-op when nothing is resident.
            const auto orphanRetirement = RetireEmbeddedMediaCompositionEndpoint(
                EmbeddedMediaPresentationState::CompactPinned,
                L"pinned window retirement without media owner");
            if (FAILED(orphanRetirement.cleanupResult) ||
                orphanRetirement.surfaceInvalidated) {
                AppendDiagnostic(
                    L"Embedded media pinned endpoint orphan retirement hr=" +
                    std::to_wstring(static_cast<long>(
                        orphanRetirement.cleanupResult)) +
                    L" surface-invalidated=" +
                    (orphanRetirement.surfaceInvalidated ? L"1" : L"0"));
            }
            return;
        }
        const std::wstring retiringWidgetId{session->authority->widgetId};
        AppendActionCorrelation(
            L"stage=embedded-media-retirement-begin widget=" +
            retiringWidgetId + L" reason=" +
            std::to_wstring(static_cast<int>(reason)) + L" source=pinned");
        const bool endpointOnlyRetirement =
            reason == widgetrail::pinned::WidgetSurfaceStopReason::Unpin ||
            reason == widgetrail::pinned::WidgetSurfaceStopReason::Close ||
            reason ==
                widgetrail::pinned::WidgetSurfaceStopReason::DisplayUnavailable ||
            reason ==
                widgetrail::pinned::WidgetSurfaceStopReason::EmergencyHide;
        if (!endpointOnlyRetirement) {
            AppendActionCorrelation(
                L"stage=embedded-media-retirement-terminal widget=" +
                retiringWidgetId + L" outcome=retired");
            StopEmbeddedMediaSession(*sessionKey, L"pinned-owner-terminal");
            return;
        }
        const bool returnToOverlay =
            OverlayOwnsEmbeddedMediaViewport(*sessionKey);
        HRESULT retirementResult = ReconcileEmbeddedMediaPresentation(
            *sessionKey,
            returnToOverlay
                ? EmbeddedMediaPresentationState::OverlayViewport
                : EmbeddedMediaPresentationState::Parked,
            L"pinned-window-retirement");
        // There is no later retry at window retirement: this HWND is about to
        // be destroyed. A deferred move away from the pinned endpoint must be
        // resolved here, first by forcing the session to park.
        if (retirementResult == E_PENDING && returnToOverlay) {
            retirementResult = ReconcileEmbeddedMediaPresentation(
                *sessionKey, EmbeddedMediaPresentationState::Parked,
                L"pinned-window-retirement-park",
                nullptr, widgetrail::media::ParkingReason::EndpointUnavailable);
        }
        if (retirementResult == E_PENDING) {
            // Parking still could not run. The session stays resident and
            // playing, but its endpoint cannot outlive the window, so retire
            // the composition endpoint directly and let the next reconcile
            // reattach the controller wherever it belongs.
            const auto forced = RetireEmbeddedMediaCompositionEndpoint(
                EmbeddedMediaPresentationState::CompactPinned,
                L"pinned window retirement forced");
            AppendDiagnostic(
                L"Embedded media pinned endpoint force-retired widget=" +
                retiringWidgetId + L" hr=" +
                std::to_wstring(static_cast<long>(forced.cleanupResult)) +
                L" surface-invalidated=" +
                (forced.surfaceInvalidated ? L"1" : L"0"));
            AppendActionCorrelation(
                L"stage=embedded-media-retirement-complete widget=" +
                retiringWidgetId + L" outcome=endpoint-forced");
            return;
        }
        const bool retained = SUCCEEDED(retirementResult);
        if (!retained) {
            AppendActionCorrelation(
                L"stage=embedded-media-retirement-terminal widget=" +
                retiringWidgetId + L" outcome=retired endpoint-result=" +
                std::to_wstring(static_cast<long>(retirementResult)));
            StopEmbeddedMediaSession(
                *sessionKey, L"pinned-endpoint-retirement-failed");
            return;
        }
        AppendActionCorrelation(
            L"stage=embedded-media-retirement-terminal widget=" +
            retiringWidgetId + L" outcome=" +
            (returnToOverlay ? L"overlay" : L"parked"));
    }

    [[nodiscard]] HRESULT ExecuteParkEmbeddedMediaSession(
        EmbeddedMediaSession& session,
        const widgetrail::media::ParkingReason parkingReason,
        const std::wstring_view reason) {
        // Fatal park failures leave the controller or composition graph in a
        // state this host cannot reason about. Everything else is a deferral:
        // the session stays exactly as it was and the next reconcile retries.
        const auto parkFailure = [&](const std::wstring_view stage,
                                     const HRESULT failure) {
            AppendDiagnostic(
                L"Embedded media park failed stage=" + std::wstring{stage} +
                L" reason=" + std::wstring{reason} +
                L" hr=" + std::to_wstring(static_cast<long>(failure)));
            return failure;
        };
        const auto parkDefer = [&](const std::wstring_view stage,
                                   const HRESULT failure) {
            AppendDiagnostic(
                L"Embedded media park deferred stage=" + std::wstring{stage} +
                L" reason=" + std::wstring{reason} +
                L" hr=" + std::to_wstring(static_cast<long>(failure)));
            return E_PENDING;
        };
        if (!session.authority || !session.coordinator)
            return parkFailure(L"authority", E_UNEXPECTED);
        auto& authority = *session.authority;
        const auto mediaSurface = session.coordinator;
        const auto sessionKey = session.key;
        if (mediaSurface->presentationTransferPending()) return E_PENDING;
        if (authority.presentation == EmbeddedMediaPresentationState::Parked) {
            // Already parked. Hiding a controller that is not in a presentable
            // lifecycle is not a session fault; it simply cannot apply yet.
            const HRESULT hidden = mediaSurface->SetVisible(false);
            if (FAILED(hidden)) {
                AppendDiagnostic(
                    L"Embedded media park hide deferred reason=" +
                    std::wstring{reason} + L" lifecycle=" +
                    std::to_wstring(static_cast<int>(
                        mediaSurface->state().lifecycle)) +
                    L" hr=" + std::to_wstring(static_cast<long>(hidden)));
                return E_PENDING;
            }
            return hidden;
        }
        if (!window_) return parkDefer(L"host-window", E_UNEXPECTED);
        // The manager's committed endpoint geometry is the authoritative
        // record; fall back to it when the executor's copy is absent.
        const auto priorBounds = session.clientBounds
            ? session.clientBounds
            : session.committedGeometry
                ? std::optional<RECT>{session.committedGeometry->bounds}
                : std::nullopt;
        if (!priorBounds) {
            // There is no live composition geometry to retarget away from, so
            // there is nothing to park. Hide the controller and let the
            // manager record the parked state.
            const HRESULT hidden = mediaSurface->SetVisible(false);
            if (FAILED(hidden)) {
                AppendDiagnostic(
                    L"Embedded media park hide deferred reason=" +
                    std::wstring{reason} + L" lifecycle=" +
                    std::to_wstring(static_cast<int>(
                        mediaSurface->state().lifecycle)) +
                    L" hr=" + std::to_wstring(static_cast<long>(hidden)));
                return E_PENDING;
            }
            return S_OK;
        }
        const bool retainPresentationIntent =
            parkingReason == widgetrail::media::ParkingReason::HostHidden ||
            parkingReason == widgetrail::media::ParkingReason::WidgetCycled ||
            parkingReason ==
                widgetrail::media::ParkingReason::EndpointUnavailable;
        if (!retainPresentationIntent)
            mediaSessions_.ClearPresentationRequest(sessionKey);
        const LONG width = priorBounds->right - priorBounds->left;
        const LONG height = priorBounds->bottom - priorBounds->top;
        if (width <= 0 || height <= 0)
            return parkDefer(L"prior-bounds", E_INVALIDARG);
        Microsoft::WRL::ComPtr<IUnknown> parkingTarget;
        HRESULT result = EnsureEmbeddedMediaParkingTarget(
            sessionKey, parkingTarget);
        if (FAILED(result)) {
            AppendActionCorrelation(
                L"stage=embedded-media-transfer-deferred widget=" +
                authority.widgetId +
                L" operation=parking-target-create hr=" +
                std::to_wstring(static_cast<long>(result)));
            return parkDefer(L"parking-target-create", result);
        }
        result = mediaSurface->SetVisible(false);
        if (FAILED(result)) return parkDefer(L"hide", result);
        widgetrail::richmedia::PresentationTransferFailureStage failureStage{};
        result = mediaSurface->BeginPresentationRetarget(&failureStage);
        if (FAILED(result)) return parkDefer(L"begin-retarget", result);
        widgetrail::richmedia::PresentationTarget presentation;
        presentation.ownerWindow = window_;
        presentation.compositionTarget = parkingTarget;
        presentation.bounds = {0, 0, width, height};
        presentation.rasterScale = MediaPixelsPerDip(window_);
        presentation.setPresentationVisible = [](const bool) {};
        result = mediaSurface->CompletePresentationTransfer(
            std::move(presentation), &failureStage);
        auto* current = mediaSessions_.Find(sessionKey);
        if (FAILED(result) || !current ||
            current->coordinator.get() != mediaSurface.get() ||
            !current->authority)
            return parkFailure(
                L"complete-retarget", FAILED(result) ? result : E_UNEXPECTED);

        const auto source = current->authority->presentation;
        const auto sourceEndpoint = MediaEndpoint(source);
        const auto sourceOwner = sourceEndpoint
            ? mediaSessions_.EndpointOwner(*sourceEndpoint)
            : std::nullopt;
        if (!sourceEndpoint || !sourceOwner || *sourceOwner != sessionKey)
            return parkDefer(L"source-endpoint-authority", E_ACCESSDENIED);
        const auto retirement = RetireEmbeddedMediaCompositionEndpoint(
            source, L"parking source retirement");
        result = retirement.cleanupResult;
        if (FAILED(result) || retirement.surfaceInvalidated) return result;
        current->authority->projectionDeferralRecorded = false;
        current->authority->pinnedFrameGeneration = 0;
        AppendDiagnostic(
            L"Embedded media retained hidden widget=" +
            current->authority->widgetId + L" reason=" +
            std::wstring{reason} + L" owner=parking parking-reason=" +
            std::to_wstring(static_cast<int>(parkingReason)));
        AppendActionCorrelation(
            L"stage=embedded-media-transfer-complete widget=" +
            current->authority->widgetId +
            L" destination=parking reason=" + std::wstring{reason});
        return S_OK;
    }

    void ReconcileEmbeddedMediaSurface(
        std::wstring_view widgetId,
        const widgetrail::WidgetSnapshot& snapshot,
        const widgetrail::WidgetDescriptor* descriptor) {
        if (richMediaProof_) return;
        const std::wstring ownedWidgetId{widgetId};
        widgetId = ownedWidgetId;
        if (!snapshot.embeddedMediaSession || !descriptor) {
            RetireEmbeddedMediaSessionsForWidget(
                widgetId, L"declaration-removed");
            return;
        }
        const auto& declaration = *snapshot.embeddedMediaSession;
        const auto declaresViewport = [](const auto& self,
                                         const widgetrail::WidgetNode& node) -> bool {
            if (node.kind == L"mediaViewport") return true;
            return std::any_of(
                node.children.begin(), node.children.end(),
                [&](const auto& child) { return self(self, child); });
        };
        const bool hasDeclaredViewport = declaresViewport(
            declaresViewport, snapshot.root);
        const auto sessionKey = MakeEmbeddedMediaSessionKey(
            widgetId, snapshot, *descriptor, declaration);
        const auto activeSessionKey = CurrentEmbeddedMediaSessionKey(
            state_.activeWidget());
        const bool sessionOwnsOverlayFullscreen =
            state_.surface() == widgetrail::Surface::Widget &&
            activeSessionKey && *activeSessionKey == sessionKey &&
            OverlayFullscreenMediaRequested();
        auto* session = mediaSessions_.Find(sessionKey);
        bool creatingSession = session == nullptr;
        if (!hasDeclaredViewport && creatingSession) {
            RetireEmbeddedMediaSessionsForWidget(
                widgetId, L"retained-session-authority-replaced");
            AppendDiagnostic(
                L"Embedded media parked declaration dormant widget=" +
                std::wstring{widgetId} + L" session=" + declaration.id +
                L" reason=no-resident-controller");
            return;
        }
        if (creatingSession) {
            RetireEmbeddedMediaSessionsForWidget(
                widgetId, L"authority-replaced", sessionKey);
            session = mediaSessions_.Ensure(sessionKey);
            if (!session) {
                const std::wstring failure =
                    L"Embedded media session limit reached (4 resident sessions).";
                sessions_.RecordFailure(
                    widgetId, widgetrail::WidgetSessionFailureStage::Snapshot,
                    failure);
                AppendDiagnostic(
                    L"Embedded media admission rejected widget=" +
                    std::wstring{widgetId} + L" surface=" + declaration.id +
                    L" reason=media-session-limit requested=" +
                    std::to_wstring(mediaSessions_.size() + 1) +
                    L" resident=" +
                    std::to_wstring(mediaSessions_.size()) +
                    L" limit=" +
                    std::to_wstring(widgetrail::media::MaximumResidentSessions));
                return;
            }
        } else if (!session || !session->coordinator) {
            AppendDiagnostic(
                L"Embedded media admission rejected widget=" +
                std::wstring{widgetId} + L" surface=" + declaration.id +
                L" reason=media-session-unavailable");
            return;
        }
        auto incompleteAdmission = std::unique_ptr<void, std::function<void(void*)>>{
            reinterpret_cast<void*>(1),
            [this, &creatingSession, sessionKey](void*) {
                if (creatingSession)
                    (void)mediaSessions_.EraseAfterTerminal(sessionKey);
            }};
        const bool retainedIdentityCurrent = session->authority &&
            session->authority->widgetId == widgetId &&
            session->authority->instanceId == snapshot.instanceId &&
            session->authority->runtimeGeneration == descriptor->runtimeGeneration &&
            session->authority->sessionId == declaration.id;
        if (session->authority && !retainedIdentityCurrent) {
            StopEmbeddedMediaSession(sessionKey, L"authority-replaced");
            return;
        }
        if (!hasDeclaredViewport) {
            if (!session->authority ||
                !widgetrail::SameEmbeddedMediaDocumentIdentity(
                    session->authority->documentIdentity,
                    widgetrail::MakeEmbeddedMediaDocumentIdentity(declaration))) {
                StopEmbeddedMediaSession(sessionKey, L"resource-replaced");
                return;
            }
            session->authority->presentationGeneration =
                descriptor->presentationGeneration;
            AdvanceCompatibleEmbeddedMediaCommandAuthority(
                sessionKey, snapshot, declaration);
            const auto pinnedOwner =
                mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Pinned);
            const bool pinnedPresentationCurrent =
                pinnedOwner && *pinnedOwner == sessionKey &&
                session->authority->presentation ==
                    EmbeddedMediaPresentationState::CompactPinned &&
                pinnedSurfaceCoordinator_.pinned() &&
                pinnedSurfaceCoordinator_.widgetId() == widgetId &&
                pinnedSurfaceCoordinator_.CurrentMediaViewport(declaration.id).has_value();
            if (!pinnedPresentationCurrent &&
                FAILED(ReconcileEmbeddedMediaPresentation(
                    sessionKey, EmbeddedMediaPresentationState::Parked,
                    L"declared-retained-hidden", nullptr,
                    widgetrail::media::ParkingReason::DeclaredWithoutViewport))) {
                StopEmbeddedMediaSession(
                    sessionKey, L"retained-hidden-detach-failed");
                return;
            }
            DispatchPendingEmbeddedMediaCommand(sessionKey, snapshot);
            incompleteAdmission.release();
            return;
        }
        const bool pinnedLayoutCurrent =
            pinnedSurfaceCoordinator_.pinned() &&
            pinnedSurfaceCoordinator_.widgetId() == widgetId &&
            pinnedSurfaceCoordinator_.CurrentMediaViewport(declaration.id).has_value();
        const bool overlayLayoutCurrent = committedWidgetVisualState_ &&
            committedWidgetVisualState_->widgetId == widgetId &&
            committedWidgetVisualState_->instanceId == snapshot.instanceId &&
            committedWidgetVisualState_->snapshotSequence == snapshot.sequence &&
            lastWidgetRenderResult_.succeeded;
        const bool fullscreenLayoutCurrent = sessionOwnsOverlayFullscreen &&
            CommittedFullscreenPresentationCurrent(sessionKey, snapshot.sequence);
        const bool layoutCurrent = pinnedLayoutCurrent || overlayLayoutCurrent ||
            fullscreenLayoutCurrent;
        if (!layoutCurrent) {
            const bool documentIdentityCurrent = session->authority &&
                widgetrail::SameEmbeddedMediaDocumentIdentity(
                    session->authority->documentIdentity,
                    widgetrail::MakeEmbeddedMediaDocumentIdentity(declaration));
            if (session->authority && retainedIdentityCurrent &&
                documentIdentityCurrent &&
                session->authority->presentation ==
                    EmbeddedMediaPresentationState::Parked &&
                snapshot.sequence >= session->authority->sequence) {
                const auto committedSequence = session->authority->sequence;
                session->authority->presentationGeneration =
                    descriptor->presentationGeneration;
                AdvanceCompatibleEmbeddedMediaCommandAuthority(
                    sessionKey, snapshot, declaration);
                DispatchPendingEmbeddedMediaCommand(sessionKey, snapshot);
                AppendDiagnostic(
                    L"Embedded media parked authority advanced widget=" +
                    std::wstring{widgetId} + L" surface=" + declaration.id +
                    L" committed-sequence=" +
                    std::to_wstring(committedSequence) +
                    L" successor-sequence=" + std::to_wstring(snapshot.sequence));
                return;
            }
            const auto desiredPresentation =
                pinnedSurfaceCoordinator_.pinned() &&
                pinnedSurfaceCoordinator_.widgetId() == widgetId &&
                pinnedSurfaceCoordinator_.compactMediaPresentation()
                    ? EmbeddedMediaPresentationState::CompactPinned
                    : sessionOwnsOverlayFullscreen
                        ? EmbeddedMediaPresentationState::OverlayFullscreen
                        : EmbeddedMediaPresentationState::OverlayViewport;
            const bool projectionCurrent = session->authority &&
                session->authority->presentation == desiredPresentation &&
                ((desiredPresentation ==
                      EmbeddedMediaPresentationState::CompactPinned &&
                  pinnedSurfaceCoordinator_.pinned() &&
                  pinnedSurfaceCoordinator_.widgetId() == widgetId) ||
                 (desiredPresentation !=
                      EmbeddedMediaPresentationState::CompactPinned &&
                  ((state_.surface() == widgetrail::Surface::Widget &&
                    state_.activeWidget() == widgetId) ||
                   session->coordinator->presentationTransferPending())));
            const bool retainCommittedPlane = session->authority &&
                widgetrail::RetainEmbeddedMediaPresentation({
                    retainedIdentityCurrent,
                    documentIdentityCurrent,
                    projectionCurrent,
                    session->clientBounds.has_value() &&
                        session->clientClip.has_value(),
                    session->authority->sequence,
                    snapshot.sequence,
                });
            if (retainCommittedPlane) {
                const auto committedSequence = session->authority->sequence;
                session->authority->presentationGeneration =
                    descriptor->presentationGeneration;
                AdvanceCompatibleEmbeddedMediaCommandAuthority(
                    sessionKey, snapshot, declaration);
                DispatchPendingEmbeddedMediaCommand(sessionKey, snapshot);
                AppendDiagnostic(
                    L"Embedded media retained during compatible render widget=" +
                    std::wstring{widgetId} + L" surface=" + declaration.id +
                    L" committed-sequence=" +
                    std::to_wstring(committedSequence) +
                    L" successor-sequence=" + std::to_wstring(snapshot.sequence));
                return;
            }
            if (retainedIdentityCurrent && !sessionOwnsOverlayFullscreen)
                (void)session->coordinator->SetVisible(false);
            // Keep the last committed client geometry. The session is still
            // recorded as presenting, and parking it later retargets away from
            // exactly these bounds. Discarding them here made the next park
            // fail its geometry authority and destroy a live session.
            return;
        }
        const auto retainCurrentSession = [&] {
            session->authority->presentationGeneration =
                descriptor->presentationGeneration;
            AdvanceCompatibleEmbeddedMediaCommandAuthority(
                sessionKey, snapshot, declaration);
            const auto desiredPresentation =
                pinnedSurfaceCoordinator_.pinned() &&
                pinnedSurfaceCoordinator_.widgetId() == widgetId &&
                pinnedSurfaceCoordinator_.compactMediaPresentation()
                    ? EmbeddedMediaPresentationState::CompactPinned
                    : sessionOwnsOverlayFullscreen
                        ? EmbeddedMediaPresentationState::OverlayFullscreen
                        : EmbeddedMediaPresentationState::OverlayViewport;
            const auto pinnedPresentation =
                desiredPresentation ==
                        EmbeddedMediaPresentationState::CompactPinned
                    ? pinnedSurfaceCoordinator_.CurrentMediaViewport(declaration.id)
                    : std::nullopt;
            if (desiredPresentation ==
                    EmbeddedMediaPresentationState::CompactPinned &&
                !pinnedPresentation) {
                (void)session->coordinator->SetVisible(false);
                return;
            }
            const HRESULT transfer = ReconcileEmbeddedMediaPresentation(
                sessionKey, desiredPresentation, L"snapshot-reconciliation");
            if (FAILED(transfer) && transfer != E_PENDING) {
                StopEmbeddedMediaSession(
                    sessionKey, L"projection-transfer-failed");
                return;
            }
            if (transfer == E_PENDING) {
                DispatchPendingEmbeddedMediaCommand(sessionKey, snapshot);
                return;
            }
            DispatchPendingEmbeddedMediaCommand(sessionKey);
        };
        if (retainedIdentityCurrent && session->authority &&
            widgetrail::SameEmbeddedMediaDocumentIdentity(
                session->authority->documentIdentity,
                widgetrail::MakeEmbeddedMediaDocumentIdentity(declaration))) {
            retainCurrentSession();
            return;
        }
        auto bundle = bridge_.ResolveEmbeddedMedia(
            widgetId, snapshot.instanceId, descriptor->runtimeGeneration,
            descriptor->presentationGeneration, snapshot.sequence, declaration.id);
        if (!bundle) {
            const auto reason = bridge_.lastError().empty()
                ? std::wstring{L"embedded-media-resolution-unavailable"}
                : bridge_.lastError();
            AppendDiagnostic(
                L"Embedded media admission rejected widget=" + std::wstring{widgetId} +
                L" surface=" + declaration.id + L" reason=" + reason);
            return;
        }
        const auto& resolvedDeclaration = bundle->surface;
        const auto& resolvedHints = resolvedDeclaration.surface;
        const auto& declaredHints = declaration.surface;
        const bool declarationMatches =
            resolvedDeclaration.id == declaration.id &&
            resolvedDeclaration.accessibleName == declaration.accessibleName &&
            resolvedDeclaration.entryAsset == declaration.entryAsset &&
            resolvedDeclaration.aspectRatio == declaration.aspectRatio &&
            resolvedHints.mode == declaredHints.mode &&
            resolvedHints.widthMode == declaredHints.widthMode &&
            resolvedHints.heightMode == declaredHints.heightMode &&
            resolvedHints.preferredWidth == declaredHints.preferredWidth &&
            resolvedHints.preferredHeight == declaredHints.preferredHeight &&
            resolvedHints.minimumWidth == declaredHints.minimumWidth &&
            resolvedHints.minimumHeight == declaredHints.minimumHeight &&
            resolvedDeclaration.commands == declaration.commands &&
            resolvedDeclaration.allowedFrameOrigins == declaration.allowedFrameOrigins &&
            resolvedDeclaration.allowedFrameDomainFamilies ==
                declaration.allowedFrameDomainFamilies &&
            resolvedDeclaration.supportedPresentations ==
                declaration.supportedPresentations &&
            resolvedDeclaration.mediaSeekStepSeconds ==
                declaration.mediaSeekStepSeconds &&
            ((!resolvedDeclaration.pendingCommand && !declaration.pendingCommand) ||
             (resolvedDeclaration.pendingCommand && declaration.pendingCommand &&
              resolvedDeclaration.pendingCommand->sequence ==
                  declaration.pendingCommand->sequence &&
              resolvedDeclaration.pendingCommand->kind == declaration.pendingCommand->kind &&
              resolvedDeclaration.pendingCommand->mediaKey ==
                  declaration.pendingCommand->mediaKey &&
              resolvedDeclaration.pendingCommand->positionSeconds ==
                  declaration.pendingCommand->positionSeconds &&
              resolvedDeclaration.pendingCommand->volume ==
                  declaration.pendingCommand->volume)) &&
            bundle->resources.size() == declaration.resources.size() &&
            std::equal(
                bundle->resources.begin(), bundle->resources.end(),
                declaration.resources.begin(), declaration.resources.end(),
                [](const auto& resolved, const auto& declared) {
                    return resolved.path == declared.path &&
                        resolved.contentType == declared.contentType;
                });
        if (!declarationMatches) {
            AppendDiagnostic(
                L"Embedded media admission rejected widget=" +
                std::wstring{widgetId} + L" surface=" + declaration.id +
                L" reason=resolved-contract-mismatch");
            return;
        }
        if (session->authority) {
            StopEmbeddedMediaSession(sessionKey, L"resource-replaced");
            creatingSession = true;
            session = mediaSessions_.Ensure(sessionKey);
            if (!session) {
                AppendDiagnostic(
                    L"Embedded media replacement failed reason=media-session-limit");
                return;
            }
        }
        if (!compositionSurface_.available()) {
            AppendDiagnostic(L"Embedded media requires DirectComposition");
            return;
        }
        const auto presentation =
            pinnedSurfaceCoordinator_.pinned() &&
            pinnedSurfaceCoordinator_.widgetId() == widgetId &&
            pinnedSurfaceCoordinator_.compactMediaPresentation()
                ? EmbeddedMediaPresentationState::CompactPinned
                : sessionOwnsOverlayFullscreen
                    ? EmbeddedMediaPresentationState::OverlayFullscreen
                    : EmbeddedMediaPresentationState::OverlayViewport;
        EmbeddedMediaAuthority authority;
        authority.widgetId = std::wstring{widgetId};
        authority.instanceId = snapshot.instanceId;
        authority.runtimeGeneration = descriptor->runtimeGeneration;
        authority.presentationGeneration = descriptor->presentationGeneration;
        authority.sessionId = declaration.id;
        authority.sequence = snapshot.sequence;
        authority.documentIdentity =
            widgetrail::MakeEmbeddedMediaDocumentIdentity(declaration);
        authority.declaration = declaration;
        authority.declaration.pendingCommand.reset();
        authority.commands = declaration.commands;
        authority.presentation = EmbeddedMediaPresentationState::Parked;
        authority.parkingReason =
            widgetrail::media::ParkingReason::EndpointUnavailable;
        session->authority = std::move(authority);
        ReconcileEmbeddedMediaCommandOrigin(sessionKey, snapshot);
        const auto resolvedGeometry = ResolveEmbeddedMediaPresentationGeometry(
            sessionKey, presentation, snapshot.sequence);
        if (!resolvedGeometry) {
            session->authority.reset();
            AppendDiagnostic(L"Embedded media bounds could not be resolved");
            return;
        }
        const RECT bounds{
            resolvedGeometry->hostBounds.left, resolvedGeometry->hostBounds.top,
            resolvedGeometry->hostBounds.right, resolvedGeometry->hostBounds.bottom};
        session->clientBounds = bounds;
        session->clientClip = RECT{
            resolvedGeometry->hostClip.left, resolvedGeometry->hostClip.top,
            resolvedGeometry->hostClip.right, resolvedGeometry->hostClip.bottom};
        if (richMediaProfileDirectory_.empty()) {
            wchar_t temporary[MAX_PATH]{};
            if (!GetTempPathW(static_cast<DWORD>(std::size(temporary)), temporary)) {
                session->authority.reset();
                session->clientBounds.reset();
                session->clientClip.reset();
                return;
            }
            richMediaProfileDirectory_ =
                (std::filesystem::path{temporary} / L"WidgetRail.RichMedia").wstring();
        }
        Microsoft::WRL::ComPtr<IUnknown> target;
        const HRESULT targetResult =
            EnsureEmbeddedMediaParkingTarget(sessionKey, target);
        if (FAILED(targetResult)) {
            session->authority.reset();
            session->clientBounds.reset();
            session->clientClip.reset();
            AppendDiagnostic(L"Embedded media target creation failed hr=" +
                std::to_wstring(static_cast<long>(targetResult)));
            return;
        }
        const auto originHash = std::hash<std::wstring>{}(
            std::wstring{widgetId} + L"\n" + snapshot.instanceId + L"\n" +
            descriptor->runtimeGeneration + L"\n" + declaration.id);
        widgetrail::richmedia::Configuration configuration;
        configuration.ownerWindow = window_;
        configuration.compositionTarget = std::move(target);
        configuration.bounds = Win32Rect(resolvedGeometry->controllerBounds);
        configuration.rasterScale = MediaPixelsPerDip(window_);
        configuration.initiallyVisible = false;
        configuration.profileRootDirectory = richMediaProfileDirectory_;
        configuration.origin = std::format(
            L"https://wrail-media-{:016x}.invalid", originHash);
        configuration.entryAsset = bundle->surface.entryAsset;
        for (auto& resource : bundle->resources)
            configuration.resources.push_back({
                std::move(resource.path), std::move(resource.contentType),
                std::move(resource.content)});
        configuration.allowedFrameOrigins = bundle->surface.allowedFrameOrigins;
        configuration.allowedFrameDomainFamilies =
            bundle->surface.allowedFrameDomainFamilies;
        configuration.diagnostic = [this](const std::wstring_view message) {
            InvokeMediaCallbackGuarded(L"diagnostic", [&] {
                AppendDiagnostic(std::wstring{message});
            });
        };
        configuration.invalidate = [this, sessionKey] {
            InvokeMediaCallbackGuarded(L"invalidate", [&] {
                OnRichMediaStateChanged(sessionKey);
            });
        };
        configuration.sharedEnvironmentExited = [this](
            const widgetrail::richmedia::EnvironmentExit& exit) {
            QueueSharedMediaEnvironmentExited(exit);
        };
        configuration.playbackEvent = [this, sessionKey](
            const widgetrail::richmedia::PlaybackEvent& event) {
            InvokeMediaCallbackGuarded(L"playback-event", [&] {
                (void)OnEmbeddedMediaPlaybackEvent(sessionKey, event);
            });
        };
        configuration.setPresentationVisible =
            [this, sessionKey](const bool visible) {
          InvokeMediaCallbackGuarded(L"presentation-visible", [&] {
            const auto* current = mediaSessions_.Find(sessionKey);
            if (!current || !current->authority ||
                !current->committedGeometry) return;
            const auto presentation = current->authority->presentation;
            if (!EmbeddedMediaEndpointOwnerCurrent(sessionKey, presentation))
                return;
            const auto endpoint = CompositionEndpoint(presentation);
            const auto& geometry = *current->committedGeometry;
            widgetrail::OverlayCompositionSurface::CommitTiming timing;
            const HRESULT result = compositionSurface_.CommitExternalContentPresentation(
                endpoint, geometry.bounds, geometry.clip,
                visible, timing);
            RecordEmbeddedMediaPresentation(endpoint, timing, L"visibility");
            if (FAILED(result)) AppendDiagnostic(
                L"Embedded media presentation commit failed hr=" +
                std::to_wstring(static_cast<long>(result)) +
                L" endpoint=" + std::wstring{endpoint ==
                    widgetrail::OverlayCompositionSurface::
                        ExternalContentEndpoint::Pinned ? L"pinned" : L"overlay"} +
                L" visible=" + (visible ? L"1" : L"0") +
                L" bounds=" + std::to_wstring(geometry.bounds.left) + L"," +
                std::to_wstring(geometry.bounds.top) + L"," +
                std::to_wstring(geometry.bounds.right) + L"," +
                std::to_wstring(geometry.bounds.bottom) +
                L" clip=" + std::to_wstring(geometry.clip.left) + L"," +
                std::to_wstring(geometry.clip.top) + L"," +
                std::to_wstring(geometry.clip.right) + L"," +
                std::to_wstring(geometry.clip.bottom) +
                L" rejected=" + std::to_wstring(
                    static_cast<int>(timing.externalCommitRejection)) +
                L" lifecycle=" + std::to_wstring(static_cast<int>(
                    current->coordinator
                        ? current->coordinator->state().lifecycle
                        : widgetrail::richmedia::Lifecycle::Absent)));
          });
        };
        const auto coordinator = session->coordinator;
        const HRESULT initialize = coordinator->Initialize(std::move(configuration));
        session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority || !session->coordinator ||
            session->coordinator.get() != coordinator.get()) {
            return;
        }
        if (initialize == E_PENDING) {
            incompleteAdmission.release();
            AppendDiagnostic(
                L"Embedded media admission retained reason=shared-environment-creating");
            return;
        }
        if (FAILED(initialize)) {
            session->coordinator->BeginSessionTeardown();
            session->coordinator->CompleteSessionTeardown();
            session->coordinator->Shutdown();
            AppendDiagnostic(L"Embedded media initialization failed hr=" +
                std::to_wstring(static_cast<long>(initialize)));
            (void)mediaSessions_.EraseAfterTerminal(sessionKey);
            return;
        }
        incompleteAdmission.release();
        AppendDiagnostic(
            L"Embedded media admitted widget=" + std::wstring{widgetId} +
            L" surface=" + declaration.id + L" sequence=" +
            std::to_wstring(snapshot.sequence));
        // The controller may still be creating. A deferred presentation keeps
        // the session resident until OnRichMediaStateChanged reconciles it.
        const HRESULT presentationResult = ReconcileEmbeddedMediaPresentation(
            sessionKey, presentation, L"initial-admission");
        if (FAILED(presentationResult) && presentationResult != E_PENDING) {
            AppendDiagnostic(
                L"Embedded media initial presentation failed hr=" +
                std::to_wstring(static_cast<long>(presentationResult)));
            return;
        }
        DispatchPendingEmbeddedMediaCommand(sessionKey);
    }

    void ReconcileCommittedEmbeddedMediaPresentation(
        const EmbeddedMediaSessionKey& sessionKey) {
        ClearStaleOverlayFullscreenMediaActivation(sessionKey);
        const auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority) return;
        const std::wstring widgetId{session->authority->widgetId};
        const auto activeSessionKey = CurrentEmbeddedMediaSessionKey(
            state_.activeWidget());
        const bool sessionOwnsOverlayFullscreen =
            state_.surface() == widgetrail::Surface::Widget &&
            activeSessionKey && *activeSessionKey == sessionKey &&
            OverlayFullscreenMediaRequested();
        const auto destination =
            pinnedSurfaceCoordinator_.pinned() &&
            pinnedSurfaceCoordinator_.widgetId() == widgetId &&
            pinnedSurfaceCoordinator_.compactMediaPresentation()
                ? EmbeddedMediaPresentationState::CompactPinned
                : sessionOwnsOverlayFullscreen
                    ? EmbeddedMediaPresentationState::OverlayFullscreen
                    : state_.surface() == widgetrail::Surface::Widget &&
                          state_.activeWidget() == widgetId
                        ? EmbeddedMediaPresentationState::OverlayViewport
                        : EmbeddedMediaPresentationState::Parked;
        (void)ReconcileEmbeddedMediaPresentation(
            sessionKey, destination, L"committed-presentation", nullptr,
            destination == EmbeddedMediaPresentationState::Parked
                ? (state_.surface() == widgetrail::Surface::Hidden
                    ? widgetrail::media::ParkingReason::HostHidden
                    : widgetrail::media::ParkingReason::WidgetCycled)
                : widgetrail::media::ParkingReason::EndpointUnavailable);
    }

    void ReconcileCommittedEmbeddedMediaSurface(
        const EmbeddedMediaSessionKey& sessionKey) {
        if (richMediaProof_) return;
        const auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority) return;
        const std::wstring widgetId{session->authority->widgetId};
        const auto currentKey = CurrentEmbeddedMediaSessionKey(widgetId);
        if (!currentKey || *currentKey != sessionKey) return;
        const auto* snapshot = SnapshotFor(widgetId);
        if (!snapshot) return;
        ReconcileEmbeddedMediaSurface(
            widgetId, *snapshot, sessions_.FindDescriptor(widgetId));
        ReconcileCommittedEmbeddedMediaPresentation(sessionKey);
    }

    void ReconcileCommittedEmbeddedMediaSurface() {
        if (richMediaProof_) return;
        std::vector<EmbeddedMediaSessionKey> reconciledSessionKeys;
        if (state_.surface() == widgetrail::Surface::Widget) {
            const std::wstring widgetId{state_.activeWidget()};
            if (const auto* snapshot = SnapshotFor(widgetId)) {
                ReconcileEmbeddedMediaSurface(
                    widgetId, *snapshot, sessions_.FindDescriptor(widgetId));
            }
            if (const auto activeSessionKey =
                    CurrentEmbeddedMediaSessionKey(widgetId)) {
                ReconcileCommittedEmbeddedMediaPresentation(*activeSessionKey);
                reconciledSessionKeys.push_back(*activeSessionKey);
            }
        }
        const auto reconcileEndpointOwner = [&](const widgetrail::media::Endpoint endpoint) {
            const auto sessionKey = mediaSessions_.EndpointOwner(endpoint);
            if (!sessionKey ||
                std::find(
                    reconciledSessionKeys.begin(), reconciledSessionKeys.end(),
                    *sessionKey) != reconciledSessionKeys.end()) return;
            ReconcileCommittedEmbeddedMediaSurface(*sessionKey);
            reconciledSessionKeys.push_back(*sessionKey);
        };
        reconcileEndpointOwner(widgetrail::media::Endpoint::Overlay);
        reconcileEndpointOwner(widgetrail::media::Endpoint::Pinned);
    }

    void Shutdown() {
        auto* proofSession = richMediaProofSessionKey_
            ? mediaSessions_.Find(*richMediaProofSessionKey_) : nullptr;
        const auto retainedEnvironment =
            proofSession && proofSession->coordinator
                ? proofSession->coordinator->environmentState()
                : widgetrail::richmedia::EnvironmentState{};
        if (richMediaProof_ && proofSession && proofSession->coordinator) {
            accessibilityProvider_.SetEmbeddedFragmentRoot(nullptr);
            proofSession->coordinator->BeginSessionTeardown();
            const auto retirement = RetireEmbeddedMediaCompositionEndpoint(
                EmbeddedMediaPresentationState::OverlayViewport,
                L"proof shutdown");
            proofSession->coordinator->CompleteSessionTeardown();
            const auto teardown =
                proofSession->coordinator->sessionTeardownResult();
            proofSession->coordinator->Shutdown();
            (void)mediaSessions_.EraseAfterTerminal(*richMediaProofSessionKey_);
            AppendDiagnostic(
                L"Rich media external-target-detached hr=" +
                std::to_wstring(static_cast<long>(retirement.cleanupResult)) +
                L" terminal=" + (retirement.terminal() ? L"1" : L"0"));
            AppendDiagnostic(
                L"Rich media process-lifetime environment released generation=" +
                std::to_wstring(retainedEnvironment.generation) +
                L" pid=" + std::to_wstring(retainedEnvironment.browserProcessId) +
                L" profile-marked=1");
            if (teardown.callbackDeadlineExpired)
                AppendDiagnostic(L"Rich media callback-retirement deadline expired");
        } else {
            for (const auto& key : mediaSessions_.Keys())
                StopEmbeddedMediaSession(key, L"host-shutdown");
            richMediaEnvironment_.reset();
            AppendDiagnostic(
                L"Rich media session registry shutdown resident=0 limit=" +
                std::to_wstring(widgetrail::media::MaximumResidentSessions));
        }
        localWidgetPackageImport_.CancelPicker();
        if (const auto operation = localWidgetPackageImport_.CancelActiveOperation()) {
            (void)bridge_.CancelLocalWidgetPackageInstall(
                *operation);
        }
        ResetPinnedPlacementNavigation();
        pinnedSurfaceCoordinator_.Dispose();
        DiscardGraphicsResources();
        widgetrail::shell::ResetFixedChromeComposition(
            compositionSurface_, chromeWindow_);
        retainedGuidePaintKey_.clear();
        retainedTrayPaintState_.reset();
        fixedChromeAnchor_.reset();
        compositionChromeSession_.reset();
        declarativeRenderer_.reset();
        if (imageCache_) {
            imageCache_->Shutdown();
            imageCache_.reset();
        }
        (void)sessions_.DrainLifecycle(std::chrono::milliseconds(1000));
        sessions_.Shutdown();
        actionFailureFeedback_.Stop();
        bridge_.Stop();
        if (window_) {
            accessibilityProvider_.Detach();
            chromeAccessibilityProvider_.Detach();
            KillTimer(window_, kControllerTimer);
            KillTimer(window_, kGuideCompatibilityTimer);
            KillTimer(window_, kZOrderSettleTimer);
            KillTimer(window_, kCatalogRetryTimer);
            KillTimer(window_, kForegroundLossTimer);
            KillTimer(window_, kActionFeedbackTimer);
            KillTimer(window_, kPinnedSurfaceTimer);
            KillTimer(window_, kBridgeControlPlaneTimer);
            UnregisterHotKey(window_, kDeveloperHotkey);
        }
        if (platform_) {
            WidgetRailOverlayPlatformShutdown(platform_);
        }
        if (foregroundHook_) {
            UnhookWinEvent(foregroundHook_);
            foregroundHook_ = nullptr;
        }
        if (foregroundEventApp_ == this) foregroundEventApp_ = nullptr;
        if (backdropWindow_ && IsWindow(backdropWindow_)) {
            DestroyWindow(backdropWindow_);
            backdropWindow_ = nullptr;
        }
        if (chromeWindow_ && IsWindow(chromeWindow_)) {
            DestroyWindow(chromeWindow_);
            chromeWindow_ = nullptr;
        }
        if (backdropBrush_) {
            DeleteObject(backdropBrush_);
            backdropBrush_ = nullptr;
        }
        if (platform_) {
            WidgetRailOverlayPlatformDestroy(platform_);
            platform_ = nullptr;
        }
        if (runtimeInitialized_) {
            RoUninitialize();
            runtimeInitialized_ = false;
        }
    }

    template <typename Mutation>
    void ApplyStateTransition(
        Mutation mutation,
        const std::optional<widgetrail::Command> command = std::nullopt) {
        const auto priorSurface = state_.surface();
        const auto priorFocusRegion = state_.focusRegion();
        const auto priorDesiredExtent = DesiredPresentationExtentDip();
        const auto priorExtent = compositionSurface_.available()
            ? presentationTransaction_.CommittedDestinationExtent(
                priorDesiredExtent)
            : priorDesiredExtent;
        const auto priorPresentedExtent = PresentedPresentationExtentDip();
        const std::wstring priorSelected(state_.selectedWidget());
        const std::wstring priorActive(state_.activeWidget());
        const auto* priorSnapshot = InteractionSnapshotFor(priorActive);
        const std::wstring priorPendingWidget =
            priorSurface == widgetrail::Surface::Widget
                ? priorActive : priorSelected;
        const auto* priorPendingSnapshot =
            InteractionSnapshotFor(priorPendingWidget);
        const auto priorPendingAuthority = priorPendingSnapshot
            ? InteractionAuthority(priorPendingWidget, *priorPendingSnapshot)
            : std::nullopt;
        const bool pendingFocusGroupEntryBeforeTransition =
            priorPendingAuthority &&
            interactionSession_.FocusGroupEntryRequestPending(
                *priorPendingAuthority);
        const auto priorDesiredLifecycle = widgetrail::DesiredWidgetLifecycle(
            priorSurface, priorFocusRegion, priorSelected, priorActive,
            IsBridgeWidget(priorSelected), IsBridgeWidget(priorActive));
        if (priorSurface == widgetrail::Surface::Widget)
            CommitAdmittedWidgetPresentation(priorActive);
        if (priorSurface == widgetrail::Surface::Widget && IsBridgeWidget(priorActive)) {
            RememberCurrentFocus(priorActive);
        }
        const auto before = state_.persistent();
        if (!mutation()) {
            return;
        }
        // A held authored action never crosses an accepted shell transition.
        // A still-physical press must be released and pressed again under the
        // new focus/selection/lifecycle authority.
        heldActionRepeat_.Reset();
        heldActionAuthority_.reset();
        if (trayContextMenu_ &&
            (state_.focusRegion() != widgetrail::FocusRegion::Tray ||
             trayContextMenu_->widgetId != state_.selectedWidget())) {
            trayContextMenu_.reset();
            retainedTrayPaintState_.reset();
        }
        if (widgetContextMenu_ && !WidgetContextMenuAuthorityCurrent())
            widgetContextMenu_.reset();
        // Any accepted shell transition changes focus, selection, presentation,
        // reorder, or lifecycle authority. A pending Y must never survive it;
        // the gesture's own tap action has already retired its capture here.
        if (trayYGesture_.capturing()) trayYGesture_.Cancel();
        const bool temporaryHiddenSameWidgetTransition =
            pendingFocusGroupEntryBeforeTransition &&
            priorSelected == state_.selectedWidget() &&
            ((priorSurface == widgetrail::Surface::Widget &&
              state_.surface() == widgetrail::Surface::Hidden &&
              priorActive == state_.selectedWidget()) ||
             (priorSurface == widgetrail::Surface::Hidden &&
              state_.surface() == widgetrail::Surface::Widget &&
              priorSelected == state_.activeWidget()));
        if (priorSurface != state_.surface() || priorActive != state_.activeWidget() ||
            priorFocusRegion != state_.focusRegion()) {
            ClearFreeScrollReentry(L"shell-authority-changed");
            ClearScrollPaginationPrefetch(L"shell-authority-changed");
            const auto retired = interactionSession_.RetirePresentations();
            if (retired.pressedPresentationChanged)
                InvalidateRect(window_, nullptr, FALSE);
        }
        if (priorSurface == widgetrail::Surface::Widget &&
            priorFocusRegion == widgetrail::FocusRegion::Widget &&
            (state_.surface() != widgetrail::Surface::Widget ||
             state_.focusRegion() != widgetrail::FocusRegion::Widget ||
             priorActive != state_.activeWidget()) && priorSnapshot &&
            !temporaryHiddenSameWidgetTransition) {
            RetirePendingFocusGroupEntryForUserIntent(
                priorActive, *priorSnapshot, L"ordinary-input-lost");
        }
        if (!performanceState_ &&
            (before.order != state_.persistent().order ||
            before.lastWidget != state_.persistent().lastWidget ||
             before.reopenWidget != state_.persistent().reopenWidget)) {
            SavePersistentState(state_.persistent());
        }
        if (priorSurface != state_.surface() || priorActive != state_.activeWidget()) {
            if (temporaryHiddenSameWidgetTransition)
                interactionSession_.ClearLiveFocus();
            else
                interactionSession_.ClearFocus();
            lastWidgetRenderResult_ = {};
            ClearAccessibilityTree();
        }
        const bool deferColdWidgetStart =
            priorSurface == widgetrail::Surface::Widget &&
            state_.surface() == widgetrail::Surface::Widget &&
            priorActive != state_.activeWidget() &&
            IsBridgeWidget(state_.activeWidget()) &&
            SnapshotFor(state_.activeWidget()) == nullptr;
        const bool selectionAuthorityChanged =
            priorSelected != state_.selectedWidget() ||
            priorActive != state_.activeWidget();
        if (selectionAuthorityChanged ||
            state_.surface() == widgetrail::Surface::Hidden) {
            pendingWidgetSwitchSnap_.reset();
        }
        const std::wstring traceWidget = state_.surface() == widgetrail::Surface::Widget
            ? std::wstring(state_.activeWidget())
            : std::wstring(state_.selectedWidget());
        const auto now = GetTickCount64();
        std::uint64_t correlationId{};
        if (selectionAuthorityChanged &&
            state_.surface() != widgetrail::Surface::Hidden &&
            IsBridgeWidget(traceWidget)) {
            correlationId = admissionTrace_.BeginSelection(
                state_.selectedWidget(), state_.activeWidget(),
                deferColdWidgetStart, SnapshotFor(traceWidget) != nullptr, now);
            admissionTraceWidget_ = traceWidget;
            admissionTraceCorrelationId_ = correlationId;
        } else if (command == widgetrail::Command::Activate &&
                   admissionTraceWidget_ == traceWidget) {
            correlationId = admissionTraceCorrelationId_;
        }
        SyncWidgetActivity(
            deferColdWidgetStart, correlationId,
            correlationId != 0 ? traceWidget : std::wstring_view{});
        const auto nextDesiredLifecycle = widgetrail::DesiredWidgetLifecycle(
            state_.surface(), state_.focusRegion(),
            state_.selectedWidget(), state_.activeWidget(),
            IsBridgeWidget(state_.selectedWidget()),
            IsBridgeWidget(state_.activeWidget()));
        if (command == widgetrail::Command::Activate && correlationId != 0 &&
            priorDesiredLifecycle && nextDesiredLifecycle &&
            priorDesiredLifecycle->widgetId == nextDesiredLifecycle->widgetId) {
            admissionTrace_.RecordMeaningfulInteractive(
                correlationId, nextDesiredLifecycle->widgetId,
                priorDesiredLifecycle->state, nextDesiredLifecycle->state, now);
        }
        const bool reopenedWidgetFocus =
            priorSurface == widgetrail::Surface::Hidden &&
            state_.surface() == widgetrail::Surface::Widget &&
            state_.focusRegion() == widgetrail::FocusRegion::Widget;
        if (state_.surface() == widgetrail::Surface::Widget &&
            state_.focusRegion() == widgetrail::FocusRegion::Widget &&
            (priorFocusRegion != state_.focusRegion() || reopenedWidgetFocus)) {
            RestoreFocusForActiveSurface(state_.activeWidget());
        }

        if (priorSurface == widgetrail::Surface::Hidden &&
            state_.surface() != widgetrail::Surface::Hidden) {
            RequestFixedChromeAnchorRefresh(
                FixedChromePlacementReason::NewVisibleSession);
            if (awaitingSuccessfulOpenPaint_ || !IsWindowVisible(window_)) {
                awaitingSuccessfulOpenPaint_ = true;
                nextOpenPaintRetryAt_ = now + 100;
                overlayTransition_.PrepareInitialOpen();
            } else {
                overlayTransition_.BeginOpen(
                    now, CurrentAccessibilityPolicy().reducedMotion);
            }
            AdvanceOverlayTransition(now);
        }
        const bool revealWidgetContent = widgetrail::ShouldRevealWidgetContent(
                priorSurface == widgetrail::Surface::Widget, priorActive,
                state_.surface() == widgetrail::Surface::Widget,
                state_.activeWidget());
        const bool trayDrivenWidgetSwitch =
            priorSurface == widgetrail::Surface::Widget &&
            state_.surface() == widgetrail::Surface::Widget &&
            priorFocusRegion == widgetrail::FocusRegion::Tray &&
            priorActive != state_.activeWidget();
        const bool snapTrayDrivenWidgetSwitch =
            trayDrivenWidgetSwitch && !WidgetSwitchAnimationEnabled();
        if (revealWidgetContent) {
            if (snapTrayDrivenWidgetSwitch)
                SnapWidgetContentVisible();
            else
                RequestWidgetContentReveal(state_.activeWidget());
        }
        if (widgetrail::ShouldSnapWidgetContentVisible(
                state_.surface() == widgetrail::Surface::Widget,
                priorFocusRegion == widgetrail::FocusRegion::Widget,
                state_.focusRegion() == widgetrail::FocusRegion::Widget)) {
            SnapWidgetContentVisible();
        }
        if (state_.surface() == widgetrail::Surface::Hidden) {
            pendingContentRevealWidget_.clear();
        }

        const bool awaitingIncomingSnapshot =
            state_.surface() == widgetrail::Surface::Widget &&
            IsBridgeWidget(state_.activeWidget()) &&
            SnapshotFor(state_.activeWidget()) == nullptr;
        if (snapTrayDrivenWidgetSwitch && awaitingIncomingSnapshot &&
            correlationId != 0) {
            if (const auto* descriptor =
                    sessions_.FindDescriptor(state_.activeWidget())) {
                pendingWidgetSwitchSnap_ = PendingWidgetSwitchSnap{
                    descriptor->id,
                    descriptor->runtimeGeneration,
                    descriptor->presentationGeneration,
                    correlationId,
                };
            }
        }

        if (state_.surface() != widgetrail::Surface::Hidden) {
            const bool enteredBridgeWidget =
                state_.surface() == widgetrail::Surface::Widget && IsBridgeWidget(state_.activeWidget()) &&
                (priorSurface != widgetrail::Surface::Widget || priorActive != state_.activeWidget());
            const bool hoveredBridgeWidget =
                state_.surface() == widgetrail::Surface::Dashboard && IsBridgeWidget(state_.selectedWidget()) &&
                (priorSurface != widgetrail::Surface::Dashboard || priorSelected != state_.selectedWidget());
            const bool startupFailureBlocksSnapshot =
                (enteredBridgeWidget && sessions_.Failure(state_.activeWidget())) ||
                (hoveredBridgeWidget && sessions_.Failure(state_.selectedWidget()));
            if (priorSurface == widgetrail::Surface::Hidden) {
                PostMessageW(window_, kCatalogRefreshMessage, 0, 0);
            }
            if (priorSurface != widgetrail::Surface::Hidden &&
                (enteredBridgeWidget || hoveredBridgeWidget) &&
                !startupFailureBlocksSnapshot) {
                const bool posted = PostMessageW(
                    window_, kSnapshotRefreshMessage,
                    static_cast<WPARAM>(correlationId), 0) != FALSE;
                admissionTrace_.RecordRefreshPosted(
                    correlationId, traceWidget, posted, GetTickCount64());
            }
        }
        const auto nextExtent = DesiredPresentationExtentDip();
        if (compositionSurface_.available() &&
            priorSurface == widgetrail::Surface::Widget &&
            state_.surface() == widgetrail::Surface::Widget &&
            priorActive != state_.activeWidget() &&
            awaitingIncomingSnapshot) {
            presentationTransaction_.HoldPresentedExtent(priorPresentedExtent);
        }
        const bool destinationSnapshotAdmitted =
            state_.surface() == widgetrail::Surface::Widget &&
            (!IsBridgeWidget(state_.activeWidget()) ||
             SnapshotFor(state_.activeWidget()) != nullptr);
        const bool animateWidgetExtent =
            priorSurface == widgetrail::Surface::Widget &&
            state_.surface() == widgetrail::Surface::Widget &&
            priorExtent != nextExtent && destinationSnapshotAdmitted &&
            !snapTrayDrivenWidgetSwitch;
        if (snapTrayDrivenWidgetSwitch && destinationSnapshotAdmitted) {
            presentationTransaction_.SettleExtent(nextExtent, now, true);
        } else if (animateWidgetExtent) {
            BeginWidgetExtentTransition(priorPresentedExtent, nextExtent);
        } else if (compositionSurface_.available() &&
                   destinationSnapshotAdmitted &&
                   priorPresentedExtent == nextExtent) {
            presentationTransaction_.ReleasePresentedExtent();
        } else if (state_.surface() != widgetrail::Surface::Widget) {
            presentationTransaction_.SettleExtent(nextExtent, now, true);
        }
        const auto presentation = widgetrail::DecideOverlayPresentation(
            priorSurface != widgetrail::Surface::Hidden,
            state_.surface() != widgetrail::Surface::Hidden,
            priorExtent,
            nextExtent);
        if (priorSurface == widgetrail::Surface::Widget &&
            state_.surface() == widgetrail::Surface::Widget &&
            (priorActive != state_.activeWidget() || priorExtent != nextExtent)) {
            const bool identityChanged = priorActive != state_.activeWidget();
            const bool extentChanged = priorExtent != nextExtent;
            AppendDiagnostic(
                L"Widget presentation transition from=" + priorActive +
                L" extent=" + std::to_wstring(priorPresentedExtent.widthDip) + L"x" +
                std::to_wstring(priorPresentedExtent.heightDip) + L" to=" +
                std::wstring(state_.activeWidget()) + L" extent=" +
                std::to_wstring(nextExtent.widthDip) + L"x" +
                std::to_wstring(nextExtent.heightDip) + L" cause=" +
                (identityChanged && extentChanged
                    ? L"identity-and-extent"
                    : identityChanged ? L"identity" : L"extent") +
                L" target=" +
                (animateWidgetExtent
                    ? (compositionSurface_.available()
                        ? (CurrentAccessibilityPolicy().reducedMotion
                            ? L"composition-immediate"
                            : L"composition-motion")
                        : CurrentAccessibilityPolicy().reducedMotion
                            ? L"resize-in-place"
                            : L"animated-resize-in-place")
                    : snapTrayDrivenWidgetSwitch && destinationSnapshotAdmitted
                        ? (compositionSurface_.available()
                            ? L"composition-immediate"
                            : L"resize-in-place")
                        : L"retained") +
                L" content=" +
                (awaitingIncomingSnapshot &&
                 presentationTransaction_.retainedPresentation()
                    ? L"retained-until-snapshot"
                    : revealWidgetContent ? L"reveal" : L"stable") +
                L" sizing=" + (awaitingIncomingSnapshot
                    ? L"retained-until-snapshot"
                    : L"snapshot-admitted") +
                (awaitingIncomingSnapshot &&
                 presentationTransaction_.retainedPresentation()
                    ? L" retained-from=" +
                        presentationTransaction_.retainedPresentation()->widgetId
                    : L""));
        }
        ApplyPresentation(presentation);
        if (deferColdWidgetStart && IsWindowVisible(window_)) {
            // Revoke the old widget's lifecycle/input semantics above, but
            // commit its last admitted pixels before the posted cold-start
            // request can block this window thread. The retained snapshot is
            // visual-only and therefore publishes no stale focus or UIA.
            RedrawWindow(window_, nullptr, nullptr,
                RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN);
        }
    }

    void Dispatch(const widgetrail::Command command) {
        ApplyStateTransition(
            [&] { return state_.Dispatch(command); }, command);
    }

    bool SelectTrayWidget(const std::wstring_view widgetId) {
        bool accepted = false;
        ApplyStateTransition([&] {
            const auto priorSelected = state_.selectedWidget();
            accepted = state_.TrySelectTrayWidget(widgetId);
            return accepted && priorSelected != state_.selectedWidget();
        });
        return accepted;
    }

    const widgetrail::WidgetComputedStyle& ShellComputedStyle(
        const std::wstring_view key) const noexcept {
        static const widgetrail::WidgetComputedStyle empty;
        const auto& current = appearanceState_.current();
        if (!current) return empty;
        const auto found = current->shellStyles.find(std::wstring(key));
        return found == current->shellStyles.end() ? empty : found->second;
    }

    widgetrail::NativeAccessibilityPolicy CurrentAccessibilityPolicy() const {
        const auto& current = appearanceState_.current();
        if (!current) return {};

        HIGHCONTRASTW highContrast{sizeof(highContrast)};
        const bool systemHighContrast =
            SystemParametersInfoW(SPI_GETHIGHCONTRAST, sizeof(highContrast),
                                  &highContrast, 0) &&
            (highContrast.dwFlags & HCF_HIGHCONTRASTON) != 0;
        BOOL animationsEnabled = TRUE;
        if (!SystemParametersInfoW(SPI_GETCLIENTAREAANIMATION, 0,
                                   &animationsEnabled, 0)) {
            animationsEnabled = TRUE;
        }
        return widgetrail::CreateNativeAccessibilityPolicy(
            *current, systemHighContrast, animationsEnabled != FALSE);
    }

    [[nodiscard]] bool HighContrastSurfacePolicy() const noexcept {
        const auto& current = appearanceState_.current();
        if (current && current->contrast == widgetrail::PlatformContrastPreference::High)
            return true;
        HIGHCONTRASTW highContrast{sizeof(highContrast)};
        return SystemParametersInfoW(
                   SPI_GETHIGHCONTRAST, sizeof(highContrast), &highContrast, 0) &&
            (highContrast.dwFlags & HCF_HIGHCONTRASTON) != 0;
    }

    [[nodiscard]] widgetrail::surface_appearance::Policy
    CurrentSurfaceAppearancePolicy() const {
        const auto& current = appearanceState_.current();
        if (!current) return {};
        const auto accessibility = CurrentAccessibilityPolicy();
        return widgetrail::surface_appearance::CreatePolicy(
            *current, HighContrastSurfacePolicy(),
            accessibility.reducedTransparency, compositionSurface_.available());
    }

    [[nodiscard]] widgetrail::surface_appearance::Resolution
    ResolveRenderedSurfaceAppearance() const {
        const std::wstring_view activeWidget = state_.activeWidget();
        const auto presentation = sessions_.Presentation(activeWidget);
        const auto& retained = presentationTransaction_.retainedPresentation();
        const auto authority = widgetrail::ResolveWidgetContentAuthority(
            presentation.snapshot != nullptr,
            presentation.HasCommittedViewAuthority(),
            retained.has_value());
        const widgetrail::WidgetSnapshot* snapshot = presentation.snapshot;
        std::wstring_view renderedWidget = activeWidget;
        if (authority == widgetrail::WidgetContentAuthority::RetainedCommittedSnapshot &&
            retained) {
            snapshot = &retained->snapshot;
            renderedWidget = retained->widgetId;
        }
        return widgetrail::surface_appearance::Resolve(
            snapshot && snapshot->surface ? snapshot->surface->appearance : L"theme",
            CurrentSurfaceAppearancePolicy(), renderedWidget);
    }

    widgetrail::NativeRenderStyle AdaptShellStyle(
        const std::wstring_view key,
        const bool focused = false,
        const float viewportWidth = static_cast<float>(kPanelWidth),
        const float viewportHeight = static_cast<float>(kWidgetPanelHeight),
        const std::optional<widgetrail::NativeColor>& inheritedBackground = std::nullopt,
        const std::optional<widgetrail::NativeColor>& fallbackBackground = std::nullopt) const {
        widgetrail::NativeStyleContext context;
        context.viewportWidthPx = viewportWidth;
        context.viewportHeightPx = viewportHeight;
        context.parentWidthPx = context.viewportWidthPx;
        context.parentHeightPx = context.viewportHeightPx;
        context.parentFontSizePx = 16.0F;
        context.rootFontSizePx = 16.0F;
        context.focused = focused;
        context.effectiveBackground = inheritedBackground;
        context.fallbackBackground = fallbackBackground;
        const auto accessibility = CurrentAccessibilityPolicy();
        auto result = widgetrail::NativeStyleAdapter::Adapt(
            ShellComputedStyle(key), context, accessibility);
        for (const auto& diagnostic : result.diagnostics) {
            AppendDiagnostic(L"Platform shell style " + std::wstring(key) + L" " +
                             diagnostic.property + L": " + diagnostic.message);
        }
        return std::move(result.style);
    }

    void RebuildShellStyles(
        const float viewportWidth = static_cast<float>(kPanelWidth),
        const float viewportHeight = static_cast<float>(kWidgetPanelHeight)) {
        canvasStyle_ = AdaptShellStyle(
            L"canvas", false, viewportWidth, viewportHeight,
            kSafeCanvasFallback, kDefaultCanvas);
        effectiveCanvasBackground_ = widgetrail::ResolveNativeSurfaceColor(
            canvasStyle_.background().has_value()
                ? canvasStyle_.background()
                : std::optional<widgetrail::NativeColor>{kDefaultCanvas},
            kSafeCanvasFallback,
            canvasStyle_.opacity());
        backdropStyle_ = AdaptShellStyle(L"backdrop", false, viewportWidth, viewportHeight);
        panelStyle_ = AdaptShellStyle(
            L"panel", false, viewportWidth, viewportHeight,
            effectiveCanvasBackground_, kDefaultPanel);
        effectivePanelBackground_ = widgetrail::ResolveNativeSurfaceColor(
            panelStyle_.background().has_value()
                ? panelStyle_.background()
                : std::optional<widgetrail::NativeColor>{kDefaultPanel},
            effectiveCanvasBackground_,
            panelStyle_.opacity());
        trayStyle_ = AdaptShellStyle(
            L"tray", false, viewportWidth, viewportHeight,
            effectiveCanvasBackground_, effectiveCanvasBackground_);
        const auto trayLayer = trayStyle_.background().has_value()
            ? trayStyle_.background()
            : std::optional<widgetrail::NativeColor>{effectiveCanvasBackground_};
        effectiveTrayBackground_ = widgetrail::ResolveNativeSurfaceColor(
            trayLayer, effectiveCanvasBackground_, trayStyle_.opacity());
        trayItemStyle_ = AdaptShellStyle(
            L"tray-item", false, viewportWidth, viewportHeight,
            effectiveTrayBackground_, trayLayer);
        trayItemSelectedStyle_ = AdaptShellStyle(
            L"tray-item:selected", false, viewportWidth, viewportHeight,
            effectiveTrayBackground_, kDefaultAccent);
        trayItemFocusedStyle_ = AdaptShellStyle(
            L"tray-item:focused", true, viewportWidth, viewportHeight,
            effectiveTrayBackground_, kDefaultAccent);
        trayItemSelectedFocusedStyle_ =
            AdaptShellStyle(L"tray-item:selected:focused", true,
                            viewportWidth, viewportHeight,
                            effectiveTrayBackground_, kDefaultAccent);
        titleStyle_ = AdaptShellStyle(
            L"title", false, viewportWidth, viewportHeight, effectivePanelBackground_);
        bodyStyle_ = AdaptShellStyle(
            L"body", false, viewportWidth, viewportHeight, effectivePanelBackground_);
        hintStyle_ = AdaptShellStyle(
            L"hint", false, viewportWidth, viewportHeight, effectivePanelBackground_);
        statusStyle_ = AdaptShellStyle(
            L"status", false, viewportWidth, viewportHeight, effectivePanelBackground_);
        dashboardTitleStyle_ = AdaptShellStyle(
            L"title", false, viewportWidth, viewportHeight, effectiveCanvasBackground_);
        dashboardHintStyle_ = AdaptShellStyle(
            L"hint", false, viewportWidth, viewportHeight, effectiveCanvasBackground_);
    }

    void ApplyPlatformAppearance(
        const bool applyPresentation = true,
        const bool invalidateWidgetSnapshots = true) {
        const auto& current = appearanceState_.current();
        if (!current) return;
        RebuildShellStyles();

        const auto backdropColor = backdropStyle_.background().value_or(
            widgetrail::NativeColor{0, 0, 0, 1});
        if (HBRUSH replacement = CreateSolidBrush(GdiColor(backdropColor))) {
            if (backdropBrush_) DeleteObject(backdropBrush_);
            backdropBrush_ = replacement;
        }
        targetBackdropOpacity_ = static_cast<BYTE>(std::lround(
            std::clamp(current->backdropOpacity, 0.0, 0.8) * 255.0));
        pinnedSurfaceCoordinator_.SetSurfaceAppearancePolicy(
            CurrentSurfaceAppearancePolicy());
        // Sampling here makes an accessibility change to reduced motion snap
        // an in-flight transition immediately rather than waiting for a timer.
        AdvanceOverlayTransition(GetTickCount64());
        InvalidateRect(backdropWindow_, nullptr, TRUE);

        // The host owns every physical animation. Leaving DWM transitions on
        // can add an unbounded second animation around show/hide and resize.
        const BOOL disableTransitions = TRUE;
        (void)DwmSetWindowAttribute(window_, DWMWA_TRANSITIONS_FORCEDISABLED,
                                    &disableTransitions, sizeof(disableTransitions));
        (void)DwmSetWindowAttribute(backdropWindow_, DWMWA_TRANSITIONS_FORCEDISABLED,
                                    &disableTransitions, sizeof(disableTransitions));

        if (invalidateWidgetSnapshots) {
            // Worker snapshots contain bridge-computed widget styles derived
            // from the same platform revision. Appearance invalidates those
            // derived projections and requests a refresh; it does not erase
            // the last admitted semantic checkpoint or wake hidden workers.
            const std::wstring visibleWidget = state_.surface() == widgetrail::Surface::Widget
                ? std::wstring(state_.activeWidget())
                : std::wstring(state_.selectedWidget());
            const bool hadVisibleSnapshot = SnapshotFor(visibleWidget) != nullptr;
            sessions_.MarkAllRefreshRequested();
            renderedSnapshotSequences_.clear();
            if (state_.surface() != widgetrail::Surface::Hidden && hadVisibleSnapshot &&
                IsBridgeWidget(visibleWidget)) {
                RefreshWidgetSnapshot(visibleWidget);
            }
        }

        if (applyPresentation && state_.surface() != widgetrail::Surface::Hidden) {
            RequestFixedChromeAnchorRefresh(
                FixedChromePlacementReason::Appearance);
        }
        DiscardGraphicsResources();
        if (applyPresentation && state_.surface() != widgetrail::Surface::Hidden) {
            ShowOverlay();
            InvalidateRect(window_, nullptr, FALSE);
        }
        AppendDiagnostic(L"Applied platform appearance revision " +
                         std::to_wstring(current->revision) + L" theme=" +
                         current->themeId + L"@" + current->themeVersion);
    }

    void RefreshPlatformAppearance() {
        auto appearance = bridge_.GetPlatformAppearance();
        if (!appearance) {
            AppendDiagnostic(L"Platform appearance refresh failed; retaining last good state: " +
                             bridge_.lastError());
            return;
        }
        const long long revision = appearance->revision;
        if (!appearanceState_.Publish(std::move(*appearance))) {
            AppendDiagnostic(L"Ignored stale platform appearance revision " +
                             std::to_wstring(revision));
            return;
        }
        ApplyPlatformAppearance();
    }

    void QueueDisplayEnvironmentRefresh(const widgetrail::DisplayEnvironmentChange change) {
        if (displayRefresh_.Enqueue(
                state_.surface() != widgetrail::Surface::Hidden, change)) {
            if (!PostMessageW(window_, kDisplayRefreshMessage, 0, 0)) {
                // A live HWND should accept its private message, but never
                // strand the accumulator if the queue is temporarily full.
                // The synchronous fallback retains correctness; coalescing is
                // an optimization, not a precondition for display recovery.
                ApplyPendingDisplayEnvironmentRefresh();
            }
        }
    }

    void ApplyPendingDisplayEnvironmentRefresh() {
        const auto plan = displayRefresh_.Take();
        ResetPinnedPlacementNavigation();
        pinnedSurfaceCoordinator_.ReconcileDisplayEnvironment();
        if (state_.surface() == widgetrail::Surface::Hidden) return;
        if (!plan.repositionWindows) return;

        RequestFixedChromeAnchorRefresh(
            FixedChromePlacementReason::DisplayEnvironment);

        // Appearance application also requests widget refresh and invalidates
        // target resources. Suppress its placement step so one notification produces
        // one authoritative monitor/work-area/DPI resolve. If appearance is
        // unavailable, the placement refresh still proceeds independently.
        if (plan.reapplyAppearance) {
            // Applying the current appearance already recreates the target
            // resources; do not discard them a second time for the same
            // display-environment notification.
            ApplyPlatformAppearance(false, false);
        } else if (plan.recreateGraphics) {
            DiscardGraphicsResources();
        }
        ShowOverlay();
        InvalidateRect(window_, nullptr, FALSE);
    }

    void ApplyWidgetCatalogChange(const widgetrail::WidgetSessionCatalogChange& change) {
        const auto& descriptors = sessions_.descriptors();
        if (pendingWidgetSwitchSnap_) {
            const auto* descriptor =
                sessions_.FindDescriptor(pendingWidgetSwitchSnap_->widgetId);
            if (!descriptor ||
                descriptor->runtimeGeneration !=
                    pendingWidgetSwitchSnap_->runtimeGeneration ||
                descriptor->presentationGeneration !=
                    pendingWidgetSwitchSnap_->presentationGeneration) {
                pendingWidgetSwitchSnap_.reset();
            }
        }
        pinnedSurfaceCoordinator_.ReconcileCatalog(descriptors);
        if (!actionFailureFeedback_.ReconcileCatalog(descriptors)) {
            AppendDiagnostic(L"Widget action feedback rejected an invalid catalog projection");
        }
        // Runtime identity owns focus memory independently of checkpoint
        // retention. Clear every replaced/removed runtime even if it never
        // admitted a checkpoint in this visible session.
        for (const auto& runtime : change.runtimeChanges) {
            RetireEmbeddedMediaSessionsForWidget(
                runtime.widgetId, L"runtime-replaced");
            ClearScrollPaginationPrefetch(
                L"runtime-replaced", runtime.widgetId);
            interactionSession_.ForgetWidget(runtime.widgetId);
            if (declarativeRenderer_ && !runtime.previousInstanceId.empty()) {
                // Scroll offsets belong to the exact worker runtime, just like
                // focus memory. Never let a replacement package inherit native
                // renderer state merely because it reused public node IDs.
                declarativeRenderer_->ForgetWidgetState(runtime.previousInstanceId);
                interactionSession_.ForgetRuntime(runtime.previousInstanceId);
            }
        }
        std::erase_if(renderedSnapshotSequences_, [&](const auto& entry) {
            return !sessions_.Contains(entry.first);
        });
        const std::wstring runtimeRevealWidget =
            state_.surface() == widgetrail::Surface::Widget &&
                    std::any_of(
                        change.runtimeChanges.begin(), change.runtimeChanges.end(),
                        [&](const widgetrail::WidgetSessionRuntimeChange& runtime) {
                            return runtime.widgetId == state_.activeWidget();
                        })
                ? std::wstring(state_.activeWidget())
                : std::wstring{};
        if (!runtimeRevealWidget.empty()) {
            ClearFreeScrollReentry(L"runtime-replaced");
            interactionSession_.ClearFocus();
            lastWidgetRenderResult_ = {};
        }
        const bool catalogOrderChanged =
            change.availableWidgetIds != state_.order();
        if (catalogOrderChanged &&
            state_.surface() != widgetrail::Surface::Hidden) {
            RequestFixedChromeAnchorRefresh(
                FixedChromePlacementReason::CatalogOrder);
        }
        bool trayStateChanged = false;
        ApplyStateTransition([&] {
            trayStateChanged = state_.SetAvailableWidgets(change.availableWidgetIds);
            return trayStateChanged;
        });
        if (!trayStateChanged) {
            // Descriptor-only replacement can still change the rendered tray
            // name/icon projection. Keep lifecycle ownership synchronized and
            // publish the new catalog even when stable IDs and selection did
            // not move.
            SyncWidgetActivity();
            if (state_.surface() != widgetrail::Surface::Hidden && window_)
                InvalidateRect(window_, nullptr, FALSE);
        }
        bool catalogPlacementReady = true;
        if (catalogOrderChanged &&
            state_.surface() != widgetrail::Surface::Hidden && window_) {
            // A catalog-order refresh retires the fixed-chrome anchor and its
            // composition session. Re-establish that existing placement
            // transaction against the new order before the synchronous paint;
            // otherwise the content draw observes no authoritative chrome
            // session and incorrectly disables DirectComposition.
            catalogPlacementReady =
                ShowOverlay(true) == OverlayShowResult::Shown;
        }
        if (catalogPlacementReady &&
            state_.surface() != widgetrail::Surface::Hidden && window_) {
            // Catalog events and pointer messages share this UI thread. Commit
            // the replacement tray before returning to the message queue so
            // old pixels can never dispatch through new slot geometry.
            UpdateWindow(window_);
        }
        if (!runtimeRevealWidget.empty() &&
            state_.surface() == widgetrail::Surface::Widget &&
            state_.activeWidget() == runtimeRevealWidget) {
            AppendDiagnostic(
                L"Widget presentation transition from=" + runtimeRevealWidget +
                L" to=" + runtimeRevealWidget +
                L" cause=runtime-replaced target=retained content=reveal");
            RequestWidgetContentReveal(runtimeRevealWidget);
        }
    }

    [[nodiscard]] bool IsBridgeWidget(const std::wstring_view id) const noexcept {
        return sessions_.Contains(id);
    }

    void RecordWidgetStartupFailure(
        const std::wstring_view widgetId,
        const std::wstring_view safeFailure,
        const widgetrail::WidgetSessionFailureStage stage =
            widgetrail::WidgetSessionFailureStage::Snapshot,
        const bool revokeCurrentGeneration = false) {
        if (pendingWidgetSwitchSnap_ &&
            pendingWidgetSwitchSnap_->widgetId == widgetId) {
            pendingWidgetSwitchSnap_.reset();
        }
        ClearScrollPaginationPrefetch(L"widget-failed", widgetId);
        std::wstring message = std::wstring(DisplayWidgetName(widgetId)) +
            L" failed: " + std::wstring(safeFailure) + L" Press A to retry.";
        if (message.size() > 640) message.resize(640);
        if (revokeCurrentGeneration)
            sessions_.RecordRuntimeFailure(widgetId, stage, message);
        else
            sessions_.RecordFailure(widgetId, stage, message);
        if (state_.surface() == widgetrail::Surface::Widget &&
            state_.activeWidget() == widgetId) {
            ClearFreeScrollReentry(L"widget-failed");
            (void)interactionSession_.RetirePresentations();
            interactionSession_.ClearFocus();
            lastWidgetRenderResult_ = {};
            ClearAccessibilityTree();
        }
        AppendDiagnostic(message);
        InvalidateRect(window_, nullptr, FALSE);
    }

    void SyncWidgetActivity(
        const bool deferColdWidgetStart = false,
        const std::uint64_t correlationId = 0,
        const std::wstring_view correlationWidgetId = {}) {
        std::map<std::wstring, widgetrail::WidgetLifecycleState, std::less<>> desiredStates;
        const auto overlayDesired = widgetrail::DesiredWidgetLifecycle(
            state_.surface(), state_.focusRegion(),
            state_.selectedWidget(), state_.activeWidget(),
            IsBridgeWidget(state_.selectedWidget()),
            IsBridgeWidget(state_.activeWidget()));
        if (overlayDesired) {
            desiredStates.insert_or_assign(overlayDesired->widgetId, overlayDesired->state);
        }
        if (pinnedSurfaceCoordinator_.pinned()) {
            const std::wstring pinnedId(pinnedSurfaceCoordinator_.widgetId());
            const auto pinnedState =
                pinnedSurfaceCoordinator_.interactionMode() ==
                        widgetrail::pinned::InteractionMode::Focusable
                    ? widgetrail::WidgetLifecycleState::Interactive
                    : widgetrail::WidgetLifecycleState::Visible;
            const auto existing = desiredStates.find(pinnedId);
            if (existing == desiredStates.end() ||
                (existing->second == widgetrail::WidgetLifecycleState::Visible &&
                 pinnedState == widgetrail::WidgetLifecycleState::Interactive)) {
                desiredStates.insert_or_assign(pinnedId, pinnedState);
            }
        }

        sessions_.SetLifecycleTargets(
            desiredStates, deferColdWidgetStart, correlationId,
            correlationWidgetId);
        const auto mediaKeys = mediaSessions_.Keys();
        for (const auto& key : mediaKeys) {
            auto* session = mediaSessions_.Find(key);
            if (!session || !session->authority || !session->coordinator) continue;
            const std::wstring mediaWidgetId{session->authority->widgetId};
            const bool pinnedOwns = pinnedSurfaceCoordinator_.pinned() &&
                pinnedSurfaceCoordinator_.widgetId() == mediaWidgetId &&
                pinnedSurfaceCoordinator_.compactMediaPresentation() &&
                pinnedSurfaceCoordinator_.CurrentMediaViewport(
                    session->authority->sessionId).has_value();
            const bool overlayOwns = OverlayOwnsEmbeddedMediaViewport(key);
            const auto destination = pinnedOwns
                ? EmbeddedMediaPresentationState::CompactPinned
                : overlayOwns
                    ? (session->presentationRequest &&
                       session->presentationRequest->target ==
                           EmbeddedMediaPresentationState::OverlayFullscreen
                        ? EmbeddedMediaPresentationState::OverlayFullscreen
                        : EmbeddedMediaPresentationState::OverlayViewport)
                    : EmbeddedMediaPresentationState::Parked;
            const HRESULT result = ReconcileEmbeddedMediaPresentation(
                key, destination, L"lifecycle-reconciliation", nullptr,
                destination == EmbeddedMediaPresentationState::Parked
                    ? (state_.surface() == widgetrail::Surface::Hidden
                        ? widgetrail::media::ParkingReason::HostHidden
                        : widgetrail::media::ParkingReason::WidgetCycled)
                    : widgetrail::media::ParkingReason::EndpointUnavailable);
            if (FAILED(result) && result != E_PENDING) {
                AppendDiagnostic(
                    L"Embedded media lifecycle reconciliation failed widget=" +
                    mediaWidgetId);
            }
        }
        AppendDiagnostic(
            L"Embedded media residency requested=" +
            std::to_wstring(mediaKeys.size()) + L" resident=" +
            std::to_wstring(mediaSessions_.size()) +
            L" limit=" +
            std::to_wstring(widgetrail::media::MaximumResidentSessions));
    }

    void RetireBridgeSessionPresentationAuthority(
        const widgetrail::WidgetSessionCatalogChange& retired,
        const long long bridgeSessionGeneration) {
        AppendDiagnostic(
            L"WidgetBridge session replaced generation=" +
            std::to_wstring(bridgeSessionGeneration) +
            L" presentation-authority=retired");
        if (const auto operation =
                localWidgetPackageImport_.RetireBridgeSession(
                    bridgeSessionGeneration)) {
            (void)bridge_.CancelLocalWidgetPackageInstall(*operation);
            lastLocalWidgetPackageInstallResult_ =
                widgetrail::LocalWidgetPackageInstallResult{
                    *operation,
                    widgetrail::LocalWidgetPackageInstallStatus::Cancelled,
                    {}, {},
                    L"Local widget package installation was cancelled because the WidgetBridge session changed."};
            AppendDiagnostic(
                L"Local widget package import cancelled operation=" +
                *operation + L" reason=bridge-session-replaced");
        }
        pendingWidgetSwitchSnap_.reset();
        pendingWidgetPresentationImpact_.reset();
        pendingContentRevealWidget_.clear();
        renderedSnapshotSequences_.clear();
        committedWidgetVisualState_.reset();
        pendingContentRenderPlan_.reset();
        activeContentRenderPlan_.reset();
        lastFallbackPresentationCheckpointKey_.clear();
        presentationTransaction_.RetireBridgeSessionAuthority();
        ClearFreeScrollReentry(L"bridge-session-replaced");
        ClearScrollPaginationPrefetch(L"bridge-session-replaced");
        (void)interactionSession_.RetirePresentations();
        interactionSession_.ClearFocus();
        interactionSession_.ResetFocusGroupEntryRequests();
        for (const auto& runtime : retired.runtimeChanges) {
            interactionSession_.ForgetWidget(runtime.widgetId);
            if (!runtime.previousInstanceId.empty()) {
                interactionSession_.ForgetRuntime(runtime.previousInstanceId);
                if (declarativeRenderer_)
                    declarativeRenderer_->ForgetWidgetState(
                        runtime.previousInstanceId);
            }
        }
        for (const auto& key : mediaSessions_.Keys())
            StopEmbeddedMediaSession(key, L"bridge-session-replaced");
        if (pinnedSurfaceCoordinator_.pinned()) {
            (void)pinnedSurfaceCoordinator_.Unpin(
                widgetrail::pinned::WidgetSurfaceStopReason::RuntimeReplaced);
        }
        if (textEntryModal_.active()) textEntryModal_.Close();
        actionFailureFeedback_.Stop();
        lastWidgetRenderResult_ = {};
        ClearAccessibilityTree();
        admissionTraceWidget_.clear();
        admissionTraceCorrelationId_ = 0;
        if (window_ && state_.surface() != widgetrail::Surface::Hidden)
            InvalidateRect(window_, nullptr, FALSE);
    }

    void ProcessWidgetSessionEvents() {
        for (auto& event : sessions_.TakeEvents()) {
            if (event.kind ==
                    widgetrail::WidgetSessionEventKind::BridgeSessionReplaced &&
                event.catalog) {
                RetireBridgeSessionPresentationAuthority(
                    *event.catalog, event.bridgeSessionGeneration);
                continue;
            }
            if (event.kind == widgetrail::WidgetSessionEventKind::CatalogChanged && event.catalog) {
                KillTimer(window_, kCatalogRetryTimer);
                sessions_.ResetCatalogRetry();
                ApplyWidgetCatalogChange(*event.catalog);
                if (textEntryModal_.active() && textEntryModalAuthority_) {
                    const auto* current = sessions_.FindDescriptor(
                        textEntryModalAuthority_->widgetId);
                    if (!current ||
                        current->runtimeGeneration !=
                            textEntryModalAuthority_->runtimeGeneration ||
                        current->presentationGeneration !=
                            textEntryModalAuthority_->presentationGeneration) {
                        textEntryModal_.Close();
                    }
                }
                RefreshCurrentBridgeSnapshot();
                continue;
            }
            if (event.kind == widgetrail::WidgetSessionEventKind::Failed) {
                if (event.widgetId.empty()) {
                    AppendDiagnostic(L"Widget catalog failed: " + event.failure.safeMessage);
                    const bool active = state_.surface() != widgetrail::Surface::Hidden ||
                                        pinnedSurfaceCoordinator_.pinned();
                    const auto delay = sessions_.NextCatalogRetryDelay(
                        false, bridge_.HasWidgetCatalogChangedRevisionInFlight(), active);
                    if (delay) {
                        SetTimer(window_, kCatalogRetryTimer, *delay, nullptr);
                    } else {
                        bridge_.AbandonWidgetCatalogChangedRevision();
                    }
                    continue;
                }
                if (textEntryModal_.active() && textEntryModalAuthority_ &&
                    textEntryModalAuthority_->widgetId == event.widgetId) {
                    textEntryModal_.Close();
                }
                RecordWidgetStartupFailure(
                    event.widgetId, event.failure.safeMessage, event.failure.stage);
                if (pinnedSurfaceCoordinator_.pinned() &&
                    pinnedSurfaceCoordinator_.widgetId() == event.widgetId) {
                    (void)pinnedSurfaceCoordinator_.Unpin(
                        widgetrail::pinned::WidgetSurfaceStopReason::WorkerUnavailable);
                }
                continue;
            }
            if (event.kind == widgetrail::WidgetSessionEventKind::StaleCompletionRejected) {
                AppendDiagnostic(
                    L"Dropped stale widget session completion for " + event.widgetId);
                continue;
            }
            if (event.kind == widgetrail::WidgetSessionEventKind::Restarted) {
                if (textEntryModal_.active() && textEntryModalAuthority_ &&
                    textEntryModalAuthority_->widgetId == event.widgetId) {
                    textEntryModal_.Close();
                }
                RefreshAndApplyPresentation([&] { SyncWidgetActivity(); });
                continue;
            }
            if (event.kind != widgetrail::WidgetSessionEventKind::SnapshotAdmitted) continue;
            const auto* current = SnapshotFor(event.widgetId);
            if (!current) continue;
            if (event.completedRestart) {
                lastActionWidgetId_ = event.widgetId;
                lastActionMessage_ =
                    std::wstring(DisplayWidgetName(event.widgetId)) + L" reloaded";
                lastActionExpiresAt_ = GetTickCount64() + 1800;
                AppendDiagnostic(lastActionMessage_);
            }
            const bool pinnedWidget = pinnedSurfaceCoordinator_.pinned() &&
                pinnedSurfaceCoordinator_.widgetId() == event.widgetId;
            if (pinnedWidget) {
                const auto mediaKey = CurrentEmbeddedMediaSessionKey(event.widgetId);
                const auto* mediaSession = mediaKey
                    ? mediaSessions_.Find(*mediaKey) : nullptr;
                (void)pinnedSurfaceCoordinator_.UpdateSnapshot(
                    event.widgetId,
                    pinnedSurfaceCoordinator_.runtimeGeneration(),
                    *current,
                    ResolvePinnedLayouts(*current),
                    mediaSession && mediaSession->authority &&
                        mediaSession->coordinator);
            }
            const bool deferMediaUntilOrdinaryCommit =
                !pinnedWidget &&
                state_.surface() == widgetrail::Surface::Widget &&
                state_.activeWidget() == event.widgetId;
            if (!deferMediaUntilOrdinaryCommit) {
                ReconcileEmbeddedMediaSurface(
                    event.widgetId, *current,
                    sessions_.FindDescriptor(event.widgetId));
            }
            const auto currentWidget = state_.surface() == widgetrail::Surface::Widget
                ? state_.activeWidget()
                : state_.selectedWidget();
            const auto* admittedDescriptor = sessions_.FindDescriptor(event.widgetId);
            bool focusGroupEntryPending{};
            if (admittedDescriptor) {
                const widgetrail::input::WidgetInteractionAuthority authority{
                    event.widgetId,
                    current,
                    admittedDescriptor->runtimeGeneration,
                    admittedDescriptor->presentationGeneration,
                    false,
                };
                const auto focusGroupEntryAdmission =
                    widgetrail::input::ResolveFocusGroupEntryAdmission({
                        window_ && IsWindowVisible(window_) != FALSE,
                        WidgetOwnsInputFocus(event.widgetId),
                        textEntryModal_.active(),
                        pinnedSurfaceCoordinator_.controllerFocused(),
                        state_.surface() == widgetrail::Surface::Hidden &&
                            state_.selectedWidget() == event.widgetId,
                    });
                const auto disposition =
                    interactionSession_.ObserveFocusGroupEntryRequest(
                        authority, focusGroupEntryAdmission);
                focusGroupEntryPending =
                    disposition ==
                        widgetrail::input::FocusGroupEntryObservation::Pending ||
                    disposition ==
                        widgetrail::input::FocusGroupEntryObservation::Dormant;
                if (current->focusGroupEntryRequest &&
                    disposition != widgetrail::input::FocusGroupEntryObservation::None) {
                    AppendDiagnostic(
                        L"Focus-group entry request widget=" + event.widgetId +
                        L" request=" + std::to_wstring(
                            current->focusGroupEntryRequest->requestId) +
                        L" group=" + current->focusGroupEntryRequest->groupId +
                        L" state=" +
                        (disposition == widgetrail::input::FocusGroupEntryObservation::Pending
                            ? L"pending"
                            : disposition ==
                                  widgetrail::input::FocusGroupEntryObservation::Dormant
                            ? L"dormant" : L"retired"));
                }
            }
            if (currentWidget != event.widgetId) {
                admissionTrace_.RecordAdmissionPresentation(
                    event.correlationId, event.widgetId, false, GetTickCount64());
                continue;
            }
            const bool newerRefreshRequested =
                sessions_.RefreshState(event.widgetId) ==
                    widgetrail::WidgetRefreshState::RefreshRequested;
            const bool selectPopupWasOpen = interactionSession_.selectPopup().has_value();
            const auto interactionReconciliation =
                interactionSession_.ReconcileAdmission(
                    *current,
                    state_.surface() == widgetrail::Surface::Widget &&
                            state_.focusRegion() == widgetrail::FocusRegion::Widget &&
                            state_.activeWidget() == event.widgetId
                        ? std::wstring_view{interactionSession_.focusedElementId()}
                        : std::wstring_view{},
                    GetTickCount64());
            const auto& reconciledSliderNodes =
                interactionReconciliation.sliderDamageNodeIds;
            pendingWidgetPresentationImpact_ =
                std::move(event.presentationImpact);
            if (focusGroupEntryPending) {
                // A request-only checkpoint cannot use the ordinary no-raster
                // fast path: consumption is authorized only by a successful
                // exact-current render and its eligible focus geometry.
                pendingWidgetPresentationImpact_.reset();
            }
            if (pendingWidgetPresentationImpact_ &&
                widgetrail::RequiresCompleteSelectPopupRaster(
                    *pendingWidgetPresentationImpact_, selectPopupWasOpen,
                    interactionSession_.selectPopup().has_value())) {
                // The host-owned popup can extend beyond the authored opener's
                // damage. A changed option collection must clear both the old
                // and new popup pixels in one complete content raster.
                pendingWidgetPresentationImpact_.reset();
            }
            if (pendingWidgetPresentationImpact_ &&
                !reconciledSliderNodes.empty()) {
                pendingWidgetPresentationImpact_->effects |=
                    widgetrail::WidgetPresentationEffect::Paint |
                    widgetrail::WidgetPresentationEffect::Accessibility;
                for (const auto& nodeId : reconciledSliderNodes) {
                    auto& affected = pendingWidgetPresentationImpact_->affectedNodeIds;
                    if (std::find(affected.begin(), affected.end(), nodeId) ==
                        affected.end()) {
                        affected.push_back(nodeId);
                    }
                }
            }
            CommitAdmittedWidgetPresentation(event.widgetId);
            if (pendingContentRevealWidget_ == event.widgetId) {
                pendingContentRevealWidget_.clear();
                overlayTransition_.BeginContentReveal(
                    GetTickCount64(), CurrentAccessibilityPolicy().reducedMotion);
                AdvanceOverlayTransition(GetTickCount64());
            }
            const std::wstring priorFocus = interactionSession_.focusedElementId();
            bool preserveFreeScrollFocus{};
            if (const auto& binding = interactionSession_.freeScrollBinding();
                binding && admittedDescriptor &&
                binding->widgetId == event.widgetId &&
                widgetrail::input::IsExactScrollAuthorityCurrent(
                    current->root, binding->scrollId, binding->axis,
                    lastWidgetRenderResult_)) {
                const widgetrail::input::WidgetInteractionAuthority authority{
                    event.widgetId,
                    current,
                    admittedDescriptor->runtimeGeneration,
                    admittedDescriptor->presentationGeneration,
                    false,
                };
                preserveFreeScrollFocus =
                    interactionSession_.EvaluateFreeScrollAuthority(authority).disposition ==
                    widgetrail::input::FreeScrollAuthorityDisposition::Current;
                if (preserveFreeScrollFocus) {
                    const auto candidate = interactionSession_.FocusRestoreCandidate(
                        event.widgetId, *current);
                    if (candidate != priorFocus) {
                        AppendDiagnostic(
                            L"Free scroll focus-key preserved widget=" + event.widgetId +
                            L" scroll=" + binding->scrollId +
                            L" runtime=" + binding->runtimeGeneration +
                            L" presentation=" + binding->presentationGeneration +
                            L" previous=" + (priorFocus.empty() ? L"none" : priorFocus) +
                            L" new=" + (candidate.empty() ? L"none" : candidate) +
                            L" cause=page-admission");
                    }
                }
            }
            if (!preserveFreeScrollFocus || focusGroupEntryPending)
                RestoreFocusForActiveSurface(event.widgetId);
            const bool pressedVisualChanged =
                interactionSession_.ReconcilePressedPresentation(*current);
            if (pressedVisualChanged)
                InvalidateRect(window_, nullptr, FALSE);
            const bool semanticOnlyImpact = pendingWidgetPresentationImpact_ &&
                !widgetrail::HasWidgetPresentationEffect(
                    pendingWidgetPresentationImpact_->effects,
                    widgetrail::WidgetPresentationEffect::Paint) &&
                !widgetrail::HasWidgetPresentationEffect(
                    pendingWidgetPresentationImpact_->effects,
                    widgetrail::WidgetPresentationEffect::MeasureLayout) &&
                !widgetrail::HasWidgetPresentationEffect(
                    pendingWidgetPresentationImpact_->effects,
                    widgetrail::WidgetPresentationEffect::Resource) &&
                !widgetrail::HasWidgetPresentationEffect(
                    pendingWidgetPresentationImpact_->effects,
                    widgetrail::WidgetPresentationEffect::SurfacePlacement) &&
                !widgetrail::HasWidgetPresentationEffect(
                    pendingWidgetPresentationImpact_->effects,
                    widgetrail::WidgetPresentationEffect::Structure) &&
                !widgetrail::HasWidgetPresentationEffect(
                    pendingWidgetPresentationImpact_->effects,
                    widgetrail::WidgetPresentationEffect::Unknown);
            const bool noRasterCommitted = semanticOnlyImpact &&
                TryApplyNoRasterWidgetPresentation(
                    event.widgetId, *pendingWidgetPresentationImpact_,
                    priorFocus != interactionSession_.focusedElementId(), pressedVisualChanged);
            if (!noRasterCommitted) {
                // A semantic-only admission that cannot reuse the committed
                // frame, or any host visual-state change outside the typed
                // widget impact, must take the conservative complete path.
                if (semanticOnlyImpact || priorFocus != interactionSession_.focusedElementId() ||
                    pressedVisualChanged) {
                    pendingWidgetPresentationImpact_.reset();
                }
                const auto admissionAuthority = admittedDescriptor
                    ? std::optional<WidgetSnapshotAdmissionAuthority>{
                        WidgetSnapshotAdmissionAuthority{
                            event.widgetId,
                            admittedDescriptor->runtimeGeneration,
                            admittedDescriptor->presentationGeneration,
                            event.correlationId,
                        }}
                    : std::nullopt;
                RefreshAndApplyPresentation(
                    [] {}, admissionAuthority ? &*admissionAuthority : nullptr);
            }
            if (newerRefreshRequested) {
                RefreshWidgetSnapshot(event.widgetId, event.correlationId);
            } else {
                admissionTrace_.RecordAdmissionPresentation(
                    event.correlationId, event.widgetId, true, GetTickCount64());
            }
        }
    }

    std::wstring_view DisplayWidgetName(const std::wstring_view id) const noexcept {
        const auto* descriptor = sessions_.FindDescriptor(id);
        return descriptor ? descriptor->name : WidgetName(id);
    }

    [[nodiscard]] std::optional<widgetrail::packages::LocalWidgetPackageOrigin>
    CurrentLocalWidgetPackageImportOrigin() const {
        if (state_.surface() != widgetrail::Surface::Widget ||
            state_.focusRegion() != widgetrail::FocusRegion::Widget ||
            state_.activeWidget() != L"settings") return std::nullopt;
        const auto* descriptor = sessions_.FindDescriptor(L"settings");
        const auto lifecycle = sessions_.Lifecycle(L"settings");
        if (!descriptor || !lifecycle ||
            *lifecycle != widgetrail::WidgetLifecycleState::Interactive)
            return std::nullopt;
        return widgetrail::packages::LocalWidgetPackageOrigin{
            descriptor->id,
            L"widgetrail.firstparty.settings",
            L"widgetrail.firstparty",
            descriptor->instanceId,
            descriptor->runtimeGeneration,
            descriptor->presentationGeneration,
            bridge_.bridgeSessionGeneration(),
            *lifecycle};
    }

    [[nodiscard]] bool TryInvokeLocalWidgetPackageImport(
        const widgetrail::WidgetSnapshot& snapshot,
        const widgetrail::WidgetNode& node,
        const std::wstring_view protocolButton,
        const widgetrail::input::NavigationEventPhase phase) {
        const auto action = localWidgetPackageImport_.Invoke(
            window_,
            {
                CurrentLocalWidgetPackageImportOrigin(),
                snapshot.instanceId,
                snapshot.activeInputScopeId,
                node.actionId,
                node.id,
                std::wstring(protocolButton),
                phase == widgetrail::input::NavigationEventPhase::Pressed,
                !node.isDisabled,
                node.isBusy,
            });
        if (!action.claimed) return false;
        lastActionWidgetId_ = L"settings";
        if (action.import.status !=
            widgetrail::packages::LocalWidgetPackageImportStatus::Cancelled) {
            lastActionMessage_ = action.import.safeMessage;
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            if (!lastActionMessage_.empty())
                AppendDiagnostic(L"Local widget package action: " +
                                 lastActionMessage_);
        }
        InvalidateRect(window_, nullptr, FALSE);
        return true;
    }

    widgetrail::icons::NativeIcon DisplayWidgetIcon(const std::wstring_view id) const noexcept {
        const auto* descriptor = sessions_.FindDescriptor(id);
        if (descriptor) {
            widgetrail::icons::NativeIcon icon{};
            if (widgetrail::icons::TryParseNativeIcon(descriptor->icon, icon)) return icon;
        }
        return WidgetIcon(id);
    }

    void ApplyTransitionWindowOpacity(const float opacityFactor) {
        const auto factor = std::clamp(opacityFactor, 0.0F, 1.0F);
        const BYTE baseOverlayOpacity =
            compositionSurface_.available() ? 255 : targetOverlayOpacity_;
        const auto overlayOpacity = static_cast<BYTE>(std::lround(
            static_cast<float>(baseOverlayOpacity) * factor));
        const auto backdropOpacity = static_cast<BYTE>(std::lround(
            static_cast<float>(targetBackdropOpacity_) * factor));
        if (!appliedOverlayOpacity_ || *appliedOverlayOpacity_ != overlayOpacity) {
            const bool composed = compositionSurface_.available();
            bool applied = false;
            if (composed) {
                widgetrail::OverlayCompositionSurface::CommitTiming timing;
                applied = SUCCEEDED(compositionSurface_.CommitOpacity(
                    static_cast<float>(overlayOpacity) / 255.0F, timing));
            } else {
                applied = SetLayeredWindowAttributes(
                    window_, RGB(1, 2, 3), overlayOpacity,
                    LWA_ALPHA | LWA_COLORKEY) != FALSE;
            }
            if (applied) {
                appliedOverlayOpacity_ = overlayOpacity;
            } else {
                AppendDiagnostic(L"Overlay alpha update failed error=" +
                                 std::to_wstring(GetLastError()));
            }
        }
        if (!appliedBackdropOpacity_ ||
            *appliedBackdropOpacity_ != backdropOpacity) {
            if (SetLayeredWindowAttributes(
                    backdropWindow_, 0, backdropOpacity, LWA_ALPHA)) {
                appliedBackdropOpacity_ = backdropOpacity;
            } else {
                AppendDiagnostic(L"Backdrop alpha update failed error=" +
                                 std::to_wstring(GetLastError()));
            }
        }
    }

    void ReprimeOpenAfterRenderTargetLoss() {
        if (state_.surface() == widgetrail::Surface::Hidden) return;
        awaitingSuccessfulOpenPaint_ = true;
        nextOpenPaintRetryAt_ = GetTickCount64() + 100;
        overlayTransition_.PrepareInitialOpen();
        overlayTransitionSample_ = overlayTransition_.Sample(
            GetTickCount64(), CurrentAccessibilityPolicy().reducedMotion);
        ApplyTransitionWindowOpacity(0.0F);
        InvalidateRect(window_, nullptr, FALSE);
    }

    void BeginOpenAfterSuccessfulPaint() {
        if (!awaitingSuccessfulOpenPaint_ ||
            state_.surface() == widgetrail::Surface::Hidden) return;
        awaitingSuccessfulOpenPaint_ = false;
        nextOpenPaintRetryAt_ = 0;
        const auto now = GetTickCount64();
        overlayTransition_.BeginOpen(
            now, CurrentAccessibilityPolicy().reducedMotion);
        AdvanceOverlayTransition(now);
    }

    void AdvanceOverlayTransition(const ULONGLONG timestamp) {
        const auto previousContentOpacity =
            overlayTransitionSample_.contentOpacity;
        const auto previousExtent = PresentedPresentationExtentDip();
        overlayTransitionSample_ = overlayTransition_.Sample(
            timestamp, CurrentAccessibilityPolicy().reducedMotion);
        if (presentationTransaction_.hasActiveExtent()) {
            if (compositionSurface_.available()) {
                const auto step = presentationTransaction_.PrepareCompositionStep(
                    timestamp, CurrentAccessibilityPolicy().reducedMotion);
                if (step) {
                    const widgetrail::OverlayCompositionSurface::VisualPresentation
                        presentation{
                            step->presentation.scaleX,
                            step->presentation.scaleY,
                            step->presentation.offsetX,
                            step->presentation.offsetY,
                            static_cast<float>(step->destinationPlacement.width),
                            static_cast<float>(step->destinationPlacement.height),
                        };
                    widgetrail::OverlayCompositionSurface::CommitTiming timing;
                    const HRESULT result = compositionSurface_.CommitPresentation(
                        presentation, timing);
                    if (FAILED(result)) {
                        DisableCompositionFallback(
                            L"motion commit failed hresult=" +
                            std::to_wstring(static_cast<unsigned long>(result)));
                        presentationTransaction_.RejectCompositionAdmission();
                    } else {
                        AppendDiagnostic(
                        L"Composition motion step index=" +
                        std::to_wstring(step->index) +
                        L" presented=" +
                        std::to_wstring(static_cast<unsigned int>(
                            std::lround(
                                step->destinationPlacement.width *
                                step->presentation.scaleX))) + L"x" +
                        std::to_wstring(static_cast<unsigned int>(
                            std::lround(
                                step->destinationPlacement.height *
                                step->presentation.scaleY))) +
                        L" commit-us=" + std::to_wstring(timing.commitMicroseconds) +
                        L" waited=false redraw=false");
                        bool accepted = true;
                        bool transactionAccepted = false;
                        if (step->finalFrame) {
                        const auto settledContainer = step->destinationPlacement;
                        const widgetrail::OverlayCompositionSurface::VisualPresentation
                            settledPresentation{
                                1.0F, 1.0F,
                                static_cast<float>(step->destinationPlacement.x -
                                    settledContainer.x),
                                static_cast<float>(step->destinationPlacement.y -
                                    settledContainer.y),
                                static_cast<float>(step->destinationPlacement.width),
                                static_cast<float>(step->destinationPlacement.height),
                            };
                        widgetrail::OverlayCompositionSurface::CommitTiming settleTiming;
                        const HRESULT settleResult =
                            compositionSurface_.CommitPresentation(
                                settledPresentation, settleTiming);
                        BOOL placed = FALSE;
                        if (SUCCEEDED(settleResult)) {
                            // Publish settled transaction state before the
                            // synchronous WM_WINDOWPOSCHANGED/UIA projection.
                            presentationTransaction_.AcceptCompositionStep(*step);
                            transactionAccepted = true;
                            struct PlacementGuard final {
                                bool& active;
                                explicit PlacementGuard(bool& value)
                                    : active(value) { active = true; }
                                ~PlacementGuard() { active = false; }
                            } guard(compositionPlacementInProgress_);
                            placed = SetWindowPos(
                                window_, nullptr,
                                settledContainer.x,
                                settledContainer.y,
                                settledContainer.width,
                                settledContainer.height,
                                SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_NOREDRAW |
                                    SWP_NOZORDER);
                            if (placed)
                                presentationTransaction_.SettleCompositionContainer(
                                    settledContainer);
                        }
                        accepted = SUCCEEDED(settleResult) && placed;
                        if (!accepted) {
                            DisableCompositionFallback(
                                L"motion settlement failed hresult=" +
                                std::to_wstring(static_cast<unsigned long>(
                                    settleResult)) +
                                L" error=" + std::to_wstring(GetLastError()));
                            presentationTransaction_.RejectCompositionAdmission();
                        }
                        if (accepted) {
                        AppendDiagnostic(
                            L"Composition motion final steps=" +
                            std::to_wstring(step->index) +
                            L" geometry=destination-settled bounds=" +
                            std::to_wstring(step->destinationPlacement.x) + L"," +
                            std::to_wstring(step->destinationPlacement.y) + L"," +
                            std::to_wstring(step->destinationPlacement.width) + L"," +
                            std::to_wstring(step->destinationPlacement.height) +
                            L" commit-us=" +
                            std::to_wstring(settleTiming.commitMicroseconds) +
                            L" waited=false redraw=false alpha=premultiplied-clear");
                        }
                        }
                        if (accepted) {
                            if (!transactionAccepted)
                                presentationTransaction_.AcceptCompositionStep(*step);
                            if (accessibilityActive_ &&
                                !accessibilityTree_.widgetId.empty()) {
                                const float pixelScale =
                                    static_cast<float>(
                                        step->destinationPlacement.width) /
                                    static_cast<float>(std::max(
                                        1, DesiredPresentationExtentDip().widthDip));
                                (void)PublishAccessibilityTree(pixelScale);
                            }
                            AppendCompositionCoordinateSample(step->index);
                            // The destination frame owns the final authored
                            // MediaViewport, but the external plane is not a
                            // child of the animated content visual. Reconcile
                            // it only after the exact destination placement is
                            // committed so fullscreen exit does not wait for a
                            // later paint or input to restore the widget plane.
                            ReconcileCommittedEmbeddedMediaSurface();
                        }
                    }
                }
            } else {
                (void)presentationTransaction_.AdvanceFallbackExtent(
                    timestamp, CurrentAccessibilityPolicy().reducedMotion);
                const auto nextExtent = PresentedPresentationExtentDip();
                if (state_.surface() != widgetrail::Surface::Hidden &&
                    previousExtent != nextExtent) {
                    const auto result = ShowOverlay(true);
                    if (result == OverlayShowResult::Shown) {
                        RedrawWindow(window_, nullptr, nullptr,
                            RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN);
                    }
                }
            }
        }
        ApplyTransitionWindowOpacity(overlayTransitionSample_.shellOpacity);
        if (state_.surface() != widgetrail::Surface::Hidden &&
            std::abs(previousContentOpacity -
                     overlayTransitionSample_.contentOpacity) > 0.0001F) {
            InvalidateRect(window_, nullptr, FALSE);
        }
        if (overlayTransition_.TakeHideCompletion() &&
            state_.surface() == widgetrail::Surface::Hidden) {
            HideOverlay();
        }
    }

    void RequestWidgetContentReveal(const std::wstring_view widgetId) {
        if (widgetId.empty()) return;
        if (SnapshotFor(widgetId)) {
            pendingContentRevealWidget_.clear();
            overlayTransition_.BeginContentReveal(
                GetTickCount64(), CurrentAccessibilityPolicy().reducedMotion);
        } else {
            pendingContentRevealWidget_ = widgetId;
            // Retained content (or first-open startup status) is host
            // presentation, not the incoming immutable widget snapshot. Keep
            // it stable and reveal only once that runtime publishes content.
            overlayTransition_.SnapContentVisible();
        }
        AdvanceOverlayTransition(GetTickCount64());
    }

    void SnapWidgetContentVisible() {
        pendingContentRevealWidget_.clear();
        overlayTransition_.SnapContentVisible();
        AdvanceOverlayTransition(GetTickCount64());
    }

    bool PresentCompositionPlacement(
        const widgetrail::OverlayPlacement& placement,
        const widgetrail::OverlayPlacement& containerPlacement,
        const UINT dpi,
        const bool wasVisible) {
        const unsigned int priorWidth = compositionSurface_.width();
        const unsigned int priorHeight = compositionSurface_.height();
        struct PlacementGuard final {
            bool& active;
            explicit PlacementGuard(bool& value) : active(value) { active = true; }
            ~PlacementGuard() { active = false; }
        } guard(compositionPlacementInProgress_);
        if (!EnsureCompositionChromeSession()) {
            DisableCompositionFallback(L"session chrome layout unavailable");
            return false;
        }
        // The content endpoint owns only the active envelope. Fixed guide/tray
        // chrome is committed in its companion target and never expands this HWND.
        const auto composedContainer = containerPlacement;
        CompositionFrameSet frames;
        std::uint64_t drawMicroseconds{};
        const float destinationOffsetX = static_cast<float>(
            placement.x - composedContainer.x);
        const float destinationOffsetY = static_cast<float>(
            placement.y - composedContainer.y);
        if (!RenderCompositionFrames(
                static_cast<unsigned int>(placement.width),
                static_cast<unsigned int>(placement.height), dpi,
                destinationOffsetX, destinationOffsetY,
                composedContainer,
                frames, drawMicroseconds)) {
            DisableCompositionFallback(L"destination draw failed before geometry");
            return false;
        }

        const auto geometryStarted = std::chrono::steady_clock::now();
        const auto directive =
            presentationTransaction_.PrepareCompositionAdmission(
                priorWidth, priorHeight, placement, composedContainer,
                DesiredPresentationExtentDip(), GetTickCount64(),
                CurrentAccessibilityPolicy().reducedMotion, wasVisible);
        const auto& motion = directive.initialPresentation;
        widgetrail::OverlayCompositionSurface::VisualPresentation presentation{
            motion.scaleX, motion.scaleY, motion.offsetX, motion.offsetY,
            static_cast<float>(placement.width),
            static_cast<float>(placement.height),
        };
        widgetrail::OverlayCompositionSurface::CommitTiming commitTiming;
        std::vector<widgetrail::OverlayCompositionSurface::Frame*> framePointers;
        framePointers.reserve(frames.frames.size());
        for (auto& frame : frames.frames) framePointers.push_back(&frame);
        const HRESULT commitResult = compositionSurface_.CommitFrames(
            framePointers, !wasVisible, commitTiming, &presentation);
        if (FAILED(commitResult)) {
            presentationTransaction_.RejectCompositionAdmission();
            DisableCompositionFallback(
                L"destination commit failed hresult=" +
                std::to_wstring(static_cast<unsigned long>(commitResult)));
            return false;
        }

        const int containerWidth = composedContainer.width;
        const int containerHeight = composedContainer.height;
        const int containerX = composedContainer.x;
        const int containerY = composedContainer.y;
        const BOOL placed = SetWindowPos(
            window_, nullptr,
            containerX, containerY, containerWidth, containerHeight,
            SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_NOREDRAW | SWP_NOZORDER);
        const auto geometryMicroseconds = static_cast<std::uint64_t>(
            std::chrono::duration_cast<std::chrono::microseconds>(
                std::chrono::steady_clock::now() - geometryStarted).count());
        if (!placed) {
            presentationTransaction_.RejectCompositionAdmission();
            AppendDiagnostic(
                L"Committed composition surface could not be placed error=" +
                std::to_wstring(GetLastError()));
            return false;
        }
        retainedGuidePaintKey_ = std::move(frames.guideKey);
        retainedTrayPaintState_ = std::move(frames.trayState);
        presentationTransaction_.AcceptCompositionAdmission(
            directive,
            state_.surface() == widgetrail::Surface::Widget
                ? state_.activeWidget()
                : state_.selectedWidget());
        if (frames.overlayFullscreenGeometry) {
            const auto& fullscreen = *frames.overlayFullscreenGeometry;
            const HRESULT mediaResult = ReconcileEmbeddedMediaPresentation(
                fullscreen.sessionKey,
                EmbeddedMediaPresentationState::OverlayFullscreen,
                L"fullscreen-frame-commit", nullptr,
                widgetrail::media::ParkingReason::EndpointUnavailable,
                fullscreen.geometry);
            if (FAILED(mediaResult) && mediaResult != E_PENDING)
                return false;
            if (SUCCEEDED(mediaResult))
                PublishCommittedFullscreenPresentation(
                    fullscreen.sessionKey, fullscreen.geometry);
        }
        if (accessibilityActive_ && !accessibilityTree_.widgetId.empty()) {
            const float pixelScale = static_cast<float>(placement.width) /
                static_cast<float>(std::max(
                    1, directive.destinationExtentDip.widthDip));
            (void)PublishAccessibilityTree(pixelScale, true);
        }

        const int visibleContentX = containerX + static_cast<int>(
            std::lround(presentation.offsetX));
        const int visibleContentY = containerY + static_cast<int>(
            std::lround(presentation.offsetY));
        const int visibleContentWidth = static_cast<int>(std::lround(
            static_cast<float>(placement.width) * presentation.scaleX));
        const int visibleContentHeight = static_cast<int>(std::lround(
            static_cast<float>(placement.height) * presentation.scaleY));

        AppendDiagnostic(
            L"Composition placement committed content=complete from=" +
            std::to_wstring(priorWidth) + L"x" + std::to_wstring(priorHeight) +
            L" to=" + std::to_wstring(placement.width) + L"x" +
            std::to_wstring(placement.height) +
            L" order=" + (directive.animateMotion
                ? L"commit-motion-container"
                : L"commit-place") +
            L" draw-us=" + std::to_wstring(drawMicroseconds) +
            L" commit-us=" + std::to_wstring(commitTiming.commitMicroseconds) +
            L" geometry-us=" + std::to_wstring(geometryMicroseconds) +
            L" waited=" + (commitTiming.waitedForCompletion ? L"true" : L"false") +
            L" alpha=premultiplied-clear" +
            L" host-bounds=" + std::to_wstring(containerX) + L"," +
            std::to_wstring(containerY) + L"," +
            std::to_wstring(containerWidth) + L"," +
            std::to_wstring(containerHeight) +
            L" visible-content-bounds=" + std::to_wstring(visibleContentX) + L"," +
            std::to_wstring(visibleContentY) + L"," +
            std::to_wstring(visibleContentWidth) + L"," +
            std::to_wstring(visibleContentHeight) +
            SlowCompositionStageDiagnostic(drawMicroseconds, frames) +
            L" anchor=bottom first-visible=" +
            std::wstring(wasVisible ? L"false" : L"true"));
        if (directive.animateMotion) {
            AppendDiagnostic(
                L"Composition motion start from=" +
                std::to_wstring(priorWidth) + L"x" + std::to_wstring(priorHeight) +
                L" to=" + std::to_wstring(placement.width) + L"x" +
                std::to_wstring(placement.height) +
                L" container=" + std::to_wstring(containerWidth) + L"x" +
                std::to_wstring(containerHeight) +
                L" destination=complete waited=false redraw=false "
                L"alpha=premultiplied-clear");
        }
        // Keep the transaction's initial coordinate sample inside its own
        // motion interval. The prior order placed the next admission sample
        // before its motion-start boundary and could attribute that widget's
        // different fixed chrome width to the preceding transaction.
        AppendCompositionCoordinateSample(0);
        if (performanceCountersActive_) ++performanceSuccessfulFrames_;
        BeginOpenAfterSuccessfulPaint();
        if (frames.overlayFullscreenGeometry)
            ReconcileCommittedEmbeddedMediaSurface();
        return true;
    }

    OverlayShowResult ShowOverlay(const bool atomicVisibleTransition = false) {
        if (!placementRefreshGate_.TryEnter()) return OverlayShowResult::Deferred;
        struct PlacementScope final {
            widgetrail::PlacementRefreshGate& gate;
            HWND notifyWindow;
            ~PlacementScope() {
                if (gate.Complete() && notifyWindow) {
                    PostMessageW(notifyWindow, kPlacementRefreshMessage, 0, 0);
                }
            }
        } placementScope{placementRefreshGate_, window_};

        const bool wasVisible = IsWindowVisible(window_) != FALSE;
        if (!wasVisible) {
            const HWND foreground = GetForegroundWindow();
            (void)WidgetRailOverlayPlatformObserveForegroundTarget(
                platform_,
                reinterpret_cast<std::uintptr_t>(foreground),
                PlatformBoolean(foreground && IsWindow(foreground)));
        }

        if (!EnsureFixedChromeAnchor()) {
            AppendDiagnostic(L"Unable to resolve visible-session chrome anchor");
            return OverlayShowResult::Failed;
        }
        // Keep placement inputs valid if a composition setup failure below
        // retires the latched anchor while falling back to the legacy HWND.
        const auto fixedChrome = *fixedChromeAnchor_;
        const RECT& work = fixedChrome.workArea;
        const UINT dpi = fixedChrome.dpi;
        const float interfaceScale = fixedChrome.interfaceScale;
        const auto bodyTarget = DesiredWidgetSurfaceTarget();
        float desiredWidthDip = static_cast<float>(kPanelWidth);
        float desiredHeightDip = static_cast<float>(kDashboardHeight);
        if (state_.surface() == widgetrail::Surface::Widget) {
            // A composition admission renders the complete destination once
            // at its authored viewport. Only its visual envelope interpolates
            // from the retained source; the destination layout never does.
            const auto layoutExtent = compositionSurface_.available()
                ? DesiredPresentationExtentDip()
                : PresentedPresentationExtentDip();
            desiredWidthDip = static_cast<float>(layoutExtent.widthDip);
            desiredHeightDip = static_cast<float>(layoutExtent.heightDip);
        }
        auto placement = ComputePlatformPlacement(
            work, dpi,
            desiredWidthDip * interfaceScale,
            desiredHeightDip * interfaceScale);
        if (!placement) {
            AppendDiagnostic(L"Unable to compute a safe overlay placement");
            return OverlayShowResult::Failed;
        }
        auto compositionContainer = ComputePlatformPlacement(
            work, dpi,
            std::max(
                desiredWidthDip * interfaceScale,
                static_cast<float>(compositionSurface_.width()) * 96.0F /
                    static_cast<float>(dpi)),
            std::max(
                desiredHeightDip * interfaceScale,
                static_cast<float>(compositionSurface_.height()) * 96.0F /
                    static_cast<float>(dpi)));
        if (compositionSurface_.available() && !compositionContainer) {
            AppendDiagnostic(L"Unable to compute the composition host container");
            return OverlayShowResult::Failed;
        }
        if (compositionSurface_.available() &&
            !EnsureCompositionChromeSession()) {
            DisableCompositionFallback(
                L"local fixed chrome session unavailable");
        }
        if (compositionSurface_.available() && !OverlayFullscreenMediaRequested()) {
            const auto anchoredPlacement = AnchorContentPlacementToChrome(
                *placement);
            const auto anchoredContainer = compositionContainer
                ? AnchorContentPlacementToChrome(
                    *compositionContainer)
                : std::nullopt;
            if (!anchoredPlacement || !anchoredContainer) {
                DisableCompositionFallback(
                    L"content could not be anchored above local fixed chrome");
            } else {
                placement = anchoredPlacement;
                compositionContainer = anchoredContainer;
            }
        }

        ApplyTransitionWindowOpacity(overlayTransitionSample_.shellOpacity);
        const BOOL backdropPlaced = SetWindowPos(
            backdropWindow_, nullptr,
            fixedChrome.monitorArea.left, fixedChrome.monitorArea.top,
            fixedChrome.monitorArea.right - fixedChrome.monitorArea.left,
            fixedChrome.monitorArea.bottom - fixedChrome.monitorArea.top,
            SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_NOZORDER);
        BOOL overlayPlaced = FALSE;
        if (compositionSurface_.available()) {
            overlayPlaced = PresentCompositionPlacement(
                *placement, *compositionContainer, dpi, wasVisible) ? TRUE : FALSE;
        }
        if (!compositionSurface_.available()) {
            const UINT overlayPlacementFlags = SWP_SHOWWINDOW | SWP_NOACTIVATE |
                (atomicVisibleTransition && wasVisible ? SWP_NOREDRAW : 0U);
            overlayPlaced = SetWindowPos(
                window_, HWND_TOPMOST, placement->x, placement->y,
                placement->width, placement->height,
                overlayPlacementFlags);
        }
        if (!backdropPlaced || !overlayPlaced) {
            AppendDiagnostic(L"Overlay placement failed error=" +
                             std::to_wstring(GetLastError()));
            return OverlayShowResult::Failed;
        }
        if (!compositionSurface_.available()) {
            AppendFallbackPlacementDiagnostic(
                L"set-window-pos", work, dpi, interfaceScale, *placement);
        }
        const auto surfaceRequest = CurrentWidgetSurfaceRequest();
        const auto axisName = [](const widgetrail::WidgetSurfaceAxisMode mode) {
            switch (mode) {
            case widgetrail::WidgetSurfaceAxisMode::Content: return L"content";
            case widgetrail::WidgetSurfaceAxisMode::FillAvailable: return L"fillAvailable";
            case widgetrail::WidgetSurfaceAxisMode::Preferred:
            default: return L"preferred";
            }
        };
        AppendDiagnostic(
            L"Overlay work-area placement work=" +
            std::to_wstring(work.left) + L"," + std::to_wstring(work.top) + L"," +
            std::to_wstring(work.right) + L"," + std::to_wstring(work.bottom) +
            L" host=" + std::to_wstring(compositionSurface_.available()
                ? compositionContainer->x : placement->x) + L"," +
            std::to_wstring(compositionSurface_.available()
                ? compositionContainer->y : placement->y) + L"," +
            std::to_wstring(compositionSurface_.available()
                ? compositionContainer->width : placement->width) + L"," +
            std::to_wstring(compositionSurface_.available()
                ? compositionContainer->height : placement->height) +
            L" dpi=" + std::to_wstring(dpi) +
            L" interface-scale=" + std::to_wstring(interfaceScale) +
            L" surface=" +
            (state_.surface() == widgetrail::Surface::Widget ? L"widget" : L"dashboard") +
            L" shell=" +
            (state_.surface() == widgetrail::Surface::Widget ? L"shared" : L"dashboard") +
            L" body-preferred=" + std::to_wstring(bodyTarget.panelWidthDip) + L"x" +
            std::to_wstring(bodyTarget.panelHeightDip) +
            L" intrinsic-passes=" +
            std::to_wstring(bodyTarget.intrinsicMeasurementPasses) +
            L" surface-axis=" + axisName(surfaceRequest
                ? surfaceRequest->widthMode
                : widgetrail::WidgetSurfaceAxisMode::Preferred) + L"/" +
            axisName(surfaceRequest
                ? surfaceRequest->heightMode
                : widgetrail::WidgetSurfaceAxisMode::Preferred));
        if (!wasVisible) {
            ShowWindow(backdropWindow_, SW_SHOWNOACTIVATE);
            ShowWindow(window_, SW_SHOWNORMAL);
            ShowWindow(chromeWindow_, compositionSurface_.available()
                ? SW_SHOWNOACTIVATE : SW_HIDE);
        }
        if (compositionSurface_.available() && !ApplyOverlayZOrder()) {
            AppendDiagnostic(
                L"Coordinated overlay Z-order placement failed error=" +
                std::to_wstring(GetLastError()));
            return OverlayShowResult::Failed;
        }
        if (!wasVisible) {
            // Activation is one best-effort show-time request. Controller
            // reliability comes from the visible GameInput lease, not a
            // repeated foreground-steal loop.
            (void)AcquireOverlayForegroundInput();
        }
        // A rapid Guide reopen can arrive while the closing HWND remains
        // visible. Successful placement always reacquires the platform's
        // visible input lease; only first-show presentation work is gated by
        // the HWND's prior visibility.
        (void)WidgetRailOverlayPlatformSetWindowState(
            platform_,
            WRAIL_OVERLAY_PLATFORM_TRUE,
            PlatformBoolean(IsOverlayProcessForeground()));
        if (!wasVisible) {
            actionFailureFeedback_.Show();
            KillTimer(window_, kPinnedSurfaceTimer);
            KillTimer(window_, kBridgeControlPlaneTimer);
            SetTimer(window_, kControllerTimer,
                     kVisibleControllerTimerMilliseconds, nullptr);
            PrimeControllerState();
        }
        pinnedSurfaceCoordinator_.OnOverlayShown();
        return OverlayShowResult::Shown;
    }

    [[nodiscard]] static ResidentShowWindowState CaptureResidentShowWindowState(
        const HWND window) noexcept {
        ResidentShowWindowState result;
        result.valid = window && IsWindow(window) != FALSE;
        if (!result.valid) return result;
        result.visible = IsWindowVisible(window) != FALSE;
        result.topmost =
            (GetWindowLongPtrW(window, GWL_EXSTYLE) & WS_EX_TOPMOST) != 0;
        result.hasBounds = GetWindowRect(window, &result.bounds) != FALSE;
        return result;
    }

    [[nodiscard]] ResidentShowPresentationState CaptureResidentShowPresentationState()
        const noexcept {
        ResidentShowPresentationState result;
        result.logicalSurface = state_.surface();
        result.content = CaptureResidentShowWindowState(window_);
        result.chrome = CaptureResidentShowWindowState(chromeWindow_);
        result.backdrop = CaptureResidentShowWindowState(backdropWindow_);
        result.foregroundOwned = IsOverlayProcessForeground();
        result.compositionAvailable = compositionSurface_.available();
        result.transitionActive = overlayTransition_.active();
        result.shellOpacity = overlayTransitionSample_.shellOpacity;
        result.chromeAboveContent =
            result.chrome.valid && result.content.valid &&
            GetWindow(window_, GW_HWNDPREV) == chromeWindow_;
        result.contentAboveBackdrop =
            result.content.valid && result.backdrop.valid &&
            GetWindow(backdropWindow_, GW_HWNDPREV) == window_;
        return result;
    }

    [[nodiscard]] static std::wstring ResidentShowSurfaceName(
        const widgetrail::Surface surface) {
        switch (surface) {
        case widgetrail::Surface::Dashboard:
            return L"dashboard";
        case widgetrail::Surface::Widget:
            return L"widget";
        case widgetrail::Surface::Hidden:
        default:
            return L"hidden";
        }
    }

    [[nodiscard]] static std::wstring ResidentShowResultName(
        const OverlayShowResult result) {
        switch (result) {
        case OverlayShowResult::Shown:
            return L"shown";
        case OverlayShowResult::Deferred:
            return L"deferred";
        case OverlayShowResult::Failed:
        default:
            return L"failed";
        }
    }

    [[nodiscard]] static std::wstring ResidentShowWindowText(
        const ResidentShowWindowState& state) {
        std::wstring value = state.valid ? L"valid" : L"invalid";
        value += L"/visible=" + std::wstring(state.visible ? L"true" : L"false");
        value += L"/topmost=" + std::wstring(state.topmost ? L"true" : L"false");
        value += L"/bounds=";
        if (!state.hasBounds) return value + L"unavailable";
        return value + std::to_wstring(state.bounds.left) + L"," +
            std::to_wstring(state.bounds.top) + L"," +
            std::to_wstring(state.bounds.right - state.bounds.left) + L"," +
            std::to_wstring(state.bounds.bottom - state.bounds.top);
    }

    void AppendResidentShowDiagnostic(
        const ResidentShowAction action,
        const ResidentShowPresentationState& before,
        const ResidentShowPresentationState& after,
        const OverlayShowResult showResult,
        const bool presentationCommitted,
        const bool transitionReopened,
        const bool foregroundAttempted,
        const bool foregroundConfirmed) {
        AppendDiagnostic(
            L"Authenticated resident Show action=" +
            std::wstring(action == ResidentShowAction::HiddenDispatch
                ? L"hidden-dispatch" : L"visible-recovery") +
            L" logical=" + ResidentShowSurfaceName(before.logicalSurface) +
            L"->" + ResidentShowSurfaceName(after.logicalSurface) +
            L" show=" + ResidentShowResultName(showResult) +
            L" presentation=" +
            std::wstring(before.compositionAvailable
                ? L"direct-composition" : L"layered-hwnd") +
            L"->" + std::wstring(after.compositionAvailable
                ? L"direct-composition" : L"layered-hwnd") +
            L" committed=" +
            std::wstring(presentationCommitted ? L"true" : L"false") +
            L" transition-reopened=" +
            std::wstring(transitionReopened ? L"true" : L"false") +
            L" foreground-attempted=" +
            std::wstring(foregroundAttempted ? L"true" : L"false") +
            L" foreground=" +
            std::wstring(before.foregroundOwned ? L"owned" : L"external") +
            L"->" + std::wstring(!foregroundAttempted
                ? (foregroundConfirmed ? L"owned" : L"not-attempted")
                : (foregroundConfirmed ? L"owned" : L"denied")) +
            L" shell=" + std::to_wstring(before.shellOpacity) + L"->" +
            std::to_wstring(after.shellOpacity) +
            L" transition-active=" +
            std::wstring(before.transitionActive ? L"true" : L"false") +
            L"->" + std::wstring(after.transitionActive ? L"true" : L"false") +
            L" content-before=" + ResidentShowWindowText(before.content) +
            L" content-after=" + ResidentShowWindowText(after.content) +
            L" chrome-before=" + ResidentShowWindowText(before.chrome) +
            L" chrome-after=" + ResidentShowWindowText(after.chrome) +
            L" backdrop-before=" + ResidentShowWindowText(before.backdrop) +
            L" backdrop-after=" + ResidentShowWindowText(after.backdrop) +
            L" z-order-before=" +
            std::wstring(before.chromeAboveContent ? L"chrome>content" : L"other") +
            L"/" +
            std::wstring(before.contentAboveBackdrop ? L"content>backdrop" : L"other") +
            L" z-order-after=" +
            std::wstring(after.chromeAboveContent ? L"chrome>content" : L"other") +
            L"/" +
            std::wstring(after.contentAboveBackdrop ? L"content>backdrop" : L"other"));
    }

    void HandleAuthenticatedShowActivation() {
        const auto before = CaptureResidentShowPresentationState();
        const bool logicallyHidden =
            before.logicalSurface == widgetrail::Surface::Hidden;
        OverlayShowResult showResult = OverlayShowResult::Deferred;
        bool presentationCommitted = false;
        bool transitionReopened = false;
        bool foregroundAttempted = false;
        bool foregroundConfirmed = before.foregroundOwned;

        if (logicallyHidden) {
            // Hidden activation retains the ordinary state-machine route. It
            // may request foreground only after the surface is logically and
            // physically shown; this handler never focuses a hidden HWND.
            Dispatch(widgetrail::Command::ToggleOverlay);
            presentationCommitted =
                state_.surface() != widgetrail::Surface::Hidden &&
                IsWindowVisible(window_) != FALSE &&
                !awaitingSuccessfulOpenPaint_;
            showResult = state_.surface() == widgetrail::Surface::Hidden
                ? OverlayShowResult::Failed
                : presentationCommitted
                    ? OverlayShowResult::Shown
                    : OverlayShowResult::Deferred;
            foregroundAttempted =
                state_.surface() != widgetrail::Surface::Hidden;
            foregroundConfirmed = IsOverlayProcessForeground();
        } else {
            // WS_VISIBLE is not a physical presentation contract. Recommit
            // the existing placement/content once, then cancel a stale close
            // or zero-opacity shell through the sole transition owner.
            showResult = ShowOverlay(true);
            if (showResult != OverlayShowResult::Failed) {
                presentationCommitted = showResult == OverlayShowResult::Shown;
                if (presentationCommitted && !compositionSurface_.available()) {
                    presentationCommitted = RedrawWindow(
                        window_, nullptr, nullptr,
                        RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN) != FALSE;
                }

                const auto now = GetTickCount64();
                overlayTransition_.BeginOpen(
                    now, CurrentAccessibilityPolicy().reducedMotion);
                AdvanceOverlayTransition(now);
                transitionReopened = true;
                if (overlayTransition_.active()) {
                    SetTimer(window_, kControllerTimer,
                             kVisibleControllerTimerMilliseconds, nullptr);
                }

                // ShowOverlay already performs the one acquisition attempt
                // when it changes a hidden HWND to visible. An already-visible
                // resident activation needs exactly one attempt here.
                foregroundConfirmed = before.content.visible
                    ? AcquireOverlayForegroundInput()
                    : IsOverlayProcessForeground();
                foregroundAttempted = true;
                if (showResult == OverlayShowResult::Deferred) {
                    // The existing placement gate will post its one coalesced
                    // refresh. Keep the current pixels eligible for that pass.
                    InvalidateRect(window_, nullptr, FALSE);
                }
            }
        }

        const auto after = CaptureResidentShowPresentationState();
        AppendResidentShowDiagnostic(
            logicallyHidden
                ? ResidentShowAction::HiddenDispatch
                : ResidentShowAction::VisibleRecovery,
            before, after, showResult, presentationCommitted,
            transitionReopened, foregroundAttempted, foregroundConfirmed);
    }

    void HideOverlay() {
        textEntryModal_.Close();
        localWidgetPackageImport_.CancelPicker();
        if (const auto operation = localWidgetPackageImport_.CancelActiveOperation()) {
            (void)bridge_.CancelLocalWidgetPackageInstall(
                *operation);
        }
        ResetPinnedPlacementNavigation();
        (void)pinnedSurfaceCoordinator_.CancelPlacement();
        pinnedSurfaceCoordinator_.OnOverlayHidden();
        KillTimer(window_, kControllerTimer);
        KillTimer(window_, kPinnedSurfaceTimer);
        KillTimer(window_, kBridgeControlPlaneTimer);
        if (pinnedSurfaceCoordinator_.pinned())
            SetTimer(window_, kPinnedSurfaceTimer, 100, nullptr);
        else
            SetTimer(window_, kBridgeControlPlaneTimer, 100, nullptr);
        trayYGesture_.Reset();
        if (platform_) {
            (void)WidgetRailOverlayPlatformSetWindowState(
                platform_,
                WRAIL_OVERLAY_PLATFORM_FALSE,
                WRAIL_OVERLAY_PLATFORM_FALSE);
        }
        actionFailureFeedback_.Hide();
        ClearAccessibilityTree();
        lastForegroundOwnership_.reset();
        declarativeMotionActive_ = false;
        pendingContentRevealWidget_.clear();
        awaitingSuccessfulOpenPaint_ = false;
        nextOpenPaintRetryAt_ = 0;
        KillTimer(window_, kCatalogRetryTimer);
        KillTimer(window_, kForegroundLossTimer);
        bridge_.AbandonWidgetCatalogChangedRevision();
        sessions_.ResetCatalogRetry();
        ShowWindow(window_, SW_HIDE);
        ShowWindow(chromeWindow_, SW_HIDE);
        ShowWindow(backdropWindow_, SW_HIDE);
        SetWindowPos(window_, HWND_NOTOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SetWindowPos(backdropWindow_, HWND_NOTOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SetWindowPos(chromeWindow_, HWND_NOTOPMOST, 0, 0, 0, 0,
                     SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        DiscardGraphicsResources();
        const HWND restoreTarget = reinterpret_cast<HWND>(
            WidgetRailOverlayPlatformRememberedForegroundTarget(platform_));
        if (restoreTarget && IsWindow(restoreTarget)) {
            SetForegroundWindow(restoreTarget);
        }
    }

    void RetireCompositionMotionForHiddenState() {
        const bool retiredInFlightCompositionMotion =
            presentationTransaction_.RetireHidden();
        AppendDiagnostic(
            L"Composition motion retired state=hidden in-flight=" +
            std::wstring(retiredInFlightCompositionMotion ? L"true" : L"false") +
            L" future-work=false geometry=discarded");
    }

    void ScheduleActionFeedbackExpiry(
        const std::optional<std::uint64_t> next) {
        KillTimer(window_, kActionFeedbackTimer);
        if (!next) return;
        const auto now = GetTickCount64();
        const auto remaining = *next <= now ? 1ULL : *next - now;
        constexpr ULONGLONG maximumTimerDelay = 0x7FFFFFFFULL;
        SetTimer(
            window_,
            kActionFeedbackTimer,
            static_cast<UINT>(std::min(remaining, maximumTimerDelay)),
            nullptr);
    }

    [[nodiscard]] bool ApplyOverlayZOrder() {
        if (!window_ || !chromeWindow_ || !backdropWindow_ ||
            state_.surface() == widgetrail::Surface::Hidden) return false;
        const auto flags = SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW;
        HDWP positions = BeginDeferWindowPos(3);
        if (!positions) return false;
        positions = DeferWindowPos(
            positions, chromeWindow_, HWND_TOPMOST, 0, 0, 0, 0, flags);
        if (positions) {
            positions = DeferWindowPos(
                positions, window_, chromeWindow_, 0, 0, 0, 0, flags);
        }
        if (positions) {
            positions = DeferWindowPos(
                positions, backdropWindow_, window_, 0, 0, 0, 0, flags);
        }
        return positions && EndDeferWindowPos(positions) != FALSE;
    }

    void ReassertOverlayZOrder() {
        if (state_.surface() == widgetrail::Surface::Hidden) return;
        if (!ApplyOverlayZOrder()) {
            AppendDiagnostic(L"Topmost reassertion failed error=" +
                             std::to_wstring(GetLastError()));
        }
    }

    [[nodiscard]] float CurrentTextScale() const noexcept {
        return appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->textScale)
            : 1.0F;
    }

    struct WidgetSurfaceResolutionCache final {
        std::wstring instanceId;
        long long sequence{};
        widgetrail::WidgetSurfaceRequest request;
        widgetrail::WidgetSurfaceConstraints constraints;
        widgetrail::ResolvedWidgetSurface resolved;
    };

    [[nodiscard]] std::optional<widgetrail::WidgetSurfaceRequest>
    WidgetSurfaceRequestForSnapshot(const widgetrail::WidgetSnapshot& snapshot) const {
        if (!snapshot.surface) return std::nullopt;

        widgetrail::WidgetSurfaceRequest request;
        if (snapshot.surface->mode == L"compact") {
            request.mode = widgetrail::WidgetSurfaceMode::Compact;
        } else if (snapshot.surface->mode == L"standard") {
            request.mode = widgetrail::WidgetSurfaceMode::Standard;
        } else if (snapshot.surface->mode == L"wide") {
            request.mode = widgetrail::WidgetSurfaceMode::Wide;
        } else {
            // Unknown and empty values fail safely to Adaptive. The managed
            // validator rejects them earlier, but native parsing is still an
            // untrusted transport boundary.
            request.mode = widgetrail::WidgetSurfaceMode::Adaptive;
        }
        const auto widthMode = snapshot.surface->widthMode
            ? widgetrail::ParseWidgetSurfaceAxisMode(
                *snapshot.surface->widthMode, snapshot.protocolVersion)
            : std::optional{widgetrail::WidgetSurfaceAxisMode::Preferred};
        const auto heightMode = snapshot.surface->heightMode
            ? widgetrail::ParseWidgetSurfaceAxisMode(
                *snapshot.surface->heightMode, snapshot.protocolVersion)
            : std::optional{widgetrail::WidgetSurfaceAxisMode::Preferred};
        if (!widthMode || !heightMode) return std::nullopt;
        request.widthMode = *widthMode;
        request.heightMode = *heightMode;
        const auto toFloat = [](const std::optional<double> value) -> std::optional<float> {
            if (!value || !std::isfinite(*value) ||
                *value > std::numeric_limits<float>::max() ||
                *value < -std::numeric_limits<float>::max()) return std::nullopt;
            return static_cast<float>(*value);
        };
        request.preferredWidthDip = toFloat(snapshot.surface->preferredWidth);
        request.preferredHeightDip = toFloat(snapshot.surface->preferredHeight);
        request.minimumWidthDip = toFloat(snapshot.surface->minimumWidth);
        request.minimumHeightDip = toFloat(snapshot.surface->minimumHeight);
        return request;
    }

    [[nodiscard]] widgetrail::ResolvedWidgetSurface
    ResolvePinnedContentSurface(const widgetrail::WidgetSnapshot& snapshot) const {
        const auto request = WidgetSurfaceRequestForSnapshot(snapshot);
        RECT workArea{};
        UINT dpi = 96;
        float interfaceScale = 1.0F;
        if (fixedChromeAnchor_) {
            workArea = fixedChromeAnchor_->workArea;
            dpi = fixedChromeAnchor_->dpi;
            interfaceScale = fixedChromeAnchor_->interfaceScale;
        } else {
            const HMONITOR monitor = MonitorFromWindow(
                window_, MONITOR_DEFAULTTONEAREST);
            MONITORINFO monitorInfo{sizeof(monitorInfo)};
            UINT dpiY = 96;
            if (!monitor || !GetMonitorInfoW(monitor, &monitorInfo))
                return widgetrail::ResolveWidgetSurfaceTarget(
                    request, CurrentTextScale());
            workArea = monitorInfo.rcWork;
            if (FAILED(GetDpiForMonitor(
                    monitor, MDT_EFFECTIVE_DPI, &dpi, &dpiY)) ||
                dpi == 0 || dpiY == 0) dpi = 96;
            interfaceScale = appearanceState_.current()
                ? static_cast<float>(appearanceState_.current()->interfaceScale)
                : 1.0F;
        }
        const widgetrail::WidgetSurfaceConstraints constraints{
            {workArea.left, workArea.top, workArea.right, workArea.bottom},
            dpi,
            interfaceScale,
            fixedChromeAnchor_ ? fixedChromeAnchor_->textScale : CurrentTextScale(),
        };
        widgetrail::WidgetSurfaceIntrinsicMeasure measure;
        if (request && declarativeRenderer_ &&
            (request->widthMode == widgetrail::WidgetSurfaceAxisMode::Content ||
             request->heightMode == widgetrail::WidgetSurfaceAxisMode::Content)) {
            const bool intrinsicWidth =
                request->widthMode == widgetrail::WidgetSurfaceAxisMode::Content;
            measure = [this, &snapshot, dpi, interfaceScale, intrinsicWidth](
                const float maximumWidthDip,
                const float maximumHeightDip)
                -> std::optional<widgetrail::WidgetSurfaceIntrinsicExtent> {
                widgetrail::DeclarativeRenderOptions options;
                options.pixelScale = static_cast<float>(dpi) / 96.0F * interfaceScale;
                options.accessibility = CurrentAccessibilityPolicy();
                options.surfaceBackground = effectivePanelBackground_;
                const auto measured = declarativeRenderer_->MeasureContent(
                    snapshot, {maximumWidthDip, maximumHeightDip},
                    intrinsicWidth, options);
                if (!measured.succeeded) return std::nullopt;
                return widgetrail::WidgetSurfaceIntrinsicExtent{
                    measured.extent.width, measured.extent.height};
            };
        }
        return widgetrail::ResolveWidgetSurface(
            request, constraints, measure).value_or(
                widgetrail::ResolveWidgetSurfaceTarget(
                    request, CurrentTextScale()));
    }

    [[nodiscard]] std::vector<widgetrail::pinned::PinnedLayoutOption>
    ResolvePinnedLayouts(const widgetrail::WidgetSnapshot& snapshot) const {
        std::vector<widgetrail::pinned::PinnedLayoutOption> layouts;
        layouts.reserve(snapshot.pinnedLayouts.size());
        for (const auto& layout : snapshot.pinnedLayouts) {
            auto candidate = snapshot;
            candidate.focusGroupEntryRequest.reset();
            candidate.surface = layout.surface;
            if (layout.root) {
                candidate.root = *layout.root;
                candidate.activeInputScopeId = layout.activeInputScopeId;
                candidate.initialFocusId = layout.initialFocusId;
                candidate.pinnedLayouts.clear();
            }
            const auto resolved = ResolvePinnedContentSurface(candidate);
            layouts.push_back({layout.id, layout.name,
                               resolved.panelWidthDip, resolved.panelHeightDip,
                               layout.root ? std::optional{std::move(candidate)}
                                           : std::nullopt});
        }
        return layouts;
    }

    void CommitAdmittedWidgetPresentation(const std::wstring_view widgetId) {
        const auto* snapshot = SnapshotFor(widgetId);
        if (!snapshot) return;
        presentationTransaction_.RetainAdmittedWidget(
            widgetId,
            *snapshot,
            state_.focusRegion() == widgetrail::FocusRegion::Widget
                ? std::wstring_view{interactionSession_.focusedElementId()}
                : std::wstring_view{},
            WidgetSurfaceRequestForSnapshot(*snapshot));
    }

    [[nodiscard]] bool SubmitWidgetContentDamage(
        const widgetrail::IncrementalPresentationPlan& plan,
        const float physicalPixelsPerDip,
        const RECT client,
        const bool paintImmediately = false) {
        pendingContentRenderPlan_ = plan;
        RECT update{
            static_cast<LONG>(std::floor(plan.damage.x * physicalPixelsPerDip)),
            static_cast<LONG>(std::floor(plan.damage.y * physicalPixelsPerDip)),
            static_cast<LONG>(std::ceil(
                (plan.damage.x + plan.damage.width) * physicalPixelsPerDip)),
            static_cast<LONG>(std::ceil(
                (plan.damage.y + plan.damage.height) * physicalPixelsPerDip)),
        };
        update.left = std::clamp<LONG>(
            update.left, 0, static_cast<LONG>(client.right));
        update.top = std::clamp<LONG>(
            update.top, 0, static_cast<LONG>(client.bottom));
        update.right = std::clamp<LONG>(
            update.right, update.left, static_cast<LONG>(client.right));
        update.bottom = std::clamp<LONG>(
            update.bottom, update.top, static_cast<LONG>(client.bottom));
        if (update.right <= update.left || update.bottom <= update.top) {
            pendingContentRenderPlan_.reset();
            if (declarativeRenderer_)
                declarativeRenderer_->CancelPresentationUpdatePlan();
            return false;
        }
        InvalidateRect(window_, &update, FALSE);
        if (paintImmediately) UpdateWindow(window_);
        return true;
    }

    [[nodiscard]] bool SubmitBackgroundSurfaceDamage(
        const widgetrail::declarative::Rect damage) {
        if (!window_ || !declarativeRenderer_ ||
            state_.surface() != widgetrail::Surface::Widget)
            return false;
        RECT pendingPaint{};
        if (pendingContentRenderPlan_ ||
            GetUpdateRect(window_, &pendingPaint, FALSE) != FALSE)
            return false;
        const auto plan = declarativeRenderer_->PlanBackgroundSurfaceAnimationFrame(
            damage);
        RECT client{};
        if (!plan || !GetClientRect(window_, &client)) return false;
        const UINT dpi = std::max(1U, GetDpiForWindow(window_));
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto metrics = widgetrail::ComputeOverlayRenderMetrics(
            client.right - client.left, client.bottom - client.top,
            dpi, interfaceScale);
        return metrics && metrics->physicalPixelsPerDip > 0.0F &&
            SubmitWidgetContentDamage(
                *plan, metrics->physicalPixelsPerDip, client);
    }

    [[nodiscard]] bool AdvanceCompositorBackground(
        const std::uint64_t nowMilliseconds) {
        if (!declarativeRenderer_ || !compositionSurface_.available() ||
            !lastWidgetRenderResult_.compositorBackground) return false;
        RECT client{};
        if (!GetClientRect(window_, &client)) return false;
        const UINT dpi = std::max(1U, GetDpiForWindow(window_));
        const float scale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale) : 1.0F;
        const auto metrics = widgetrail::ComputeOverlayRenderMetrics(
            client.right - client.left, client.bottom - client.top, dpi, scale);
        if (!metrics) return false;
        std::wstring diagnostic;
        const auto contentBefore = compositionSurface_.paintCounters().content;
        const auto disposition = compositorBackgroundCoordinator_.Advance(
            lastWidgetRenderResult_.compositorBackground,
            *declarativeRenderer_, compositionSurface_,
            client.right - client.left, client.bottom - client.top,
            metrics->physicalPixelsPerDip, nowMilliseconds, diagnostic);
        const bool handled = disposition == widgetrail::
                CompositorBackgroundSurfaceCoordinator::AdvanceDisposition::Advanced ||
            disposition == widgetrail::CompositorBackgroundSurfaceCoordinator::
                AdvanceDisposition::HandledPending;
        if (handled) AppendDiagnostic(
            L"Background compositor " + diagnostic + L" content-repaints=" +
            std::to_wstring(compositionSurface_.paintCounters().content -
                contentBefore));
        return handled;
    }

    void InvalidateWidgetFocusChange(
        const std::wstring_view priorFocusedElementId,
        const std::vector<std::wstring>& sliderDamageNodeIds = {}) {
        if (!window_) return;
        const auto full = [&] {
            pendingContentRenderPlan_.reset();
            if (declarativeRenderer_)
                declarativeRenderer_->CancelPresentationUpdatePlan();
            InvalidateRect(window_, nullptr, FALSE);
        };
        if (!declarativeRenderer_ || !compositionSurface_.available() ||
            state_.surface() != widgetrail::Surface::Widget ||
            state_.focusRegion() != widgetrail::FocusRegion::Widget ||
            declarativeMotionActive_ ||
            overlayTransition_.active() ||
            presentationTransaction_.extentTransitionActive() ||
            compositionPlacementInProgress_ ||
            pendingWidgetPresentationImpact_) {
            full();
            return;
        }
        RECT pendingPaint{};
        if (GetUpdateRect(window_, &pendingPaint, FALSE) != FALSE) {
            full();
            return;
        }
        const auto* snapshot = InteractionSnapshotFor(state_.activeWidget());
        RECT client{};
        if (!snapshot || !lastWidgetRenderResult_.succeeded ||
            !GetClientRect(window_, &client)) {
            full();
            return;
        }
        const UINT dpi = std::max(1U, GetDpiForWindow(window_));
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto metrics = widgetrail::ComputeOverlayRenderMetrics(
            client.right - client.left, client.bottom - client.top,
            dpi, interfaceScale);
        const auto geometry = metrics
            ? widgetrail::ComputePanelLocalSurfaceGeometry(
                metrics->viewportWidthDip, metrics->viewportHeightDip)
            : std::nullopt;
        if (!metrics || !geometry || metrics->physicalPixelsPerDip <= 0.0F) {
            full();
            return;
        }
        const widgetrail::declarative::Rect viewport{
            geometry->widgetViewportX,
            geometry->widgetViewportY,
            geometry->widgetViewportWidth,
            geometry->widgetViewportHeight,
        };
        const auto plan = widgetrail::input::SurfaceInteractionTransactions::PlanFocusUpdate(
            *declarativeRenderer_,
            *snapshot, priorFocusedElementId, interactionSession_.focusedElementId(), viewport,
            sliderDamageNodeIds);
        if (!plan || !SubmitWidgetContentDamage(
                *plan, metrics->physicalPixelsPerDip, client)) {
            full();
            return;
        }
    }

    [[nodiscard]] bool TryApplyNoRasterWidgetPresentation(
        const std::wstring_view widgetId,
        const widgetrail::WidgetPresentationImpact& impact,
        const bool focusChanged,
        const bool pressedVisualChanged) {
        if (!window_ || !declarativeRenderer_ || !compositionSurface_.available() ||
            state_.surface() != widgetrail::Surface::Widget ||
            state_.activeWidget() != widgetId || focusChanged || pressedVisualChanged ||
            declarativeMotionActive_ ||
            overlayTransition_.active() ||
            presentationTransaction_.extentTransitionActive() ||
            compositionPlacementInProgress_ ||
            IsWindowVisible(window_) == FALSE) {
            return false;
        }
        RECT pendingPaint{};
        if (GetUpdateRect(window_, &pendingPaint, FALSE) != FALSE) return false;

        const auto* source = SnapshotFor(widgetId);
        const auto* snapshot = InteractionSnapshotFor(widgetId);
        if (!source || snapshot != source || !lastWidgetRenderResult_.succeeded ||
            !committedWidgetVisualState_ ||
            committedWidgetVisualState_->widgetId != widgetId ||
            committedWidgetVisualState_->instanceId != snapshot->instanceId ||
            committedWidgetVisualState_->snapshotSequence != impact.baseSequence)
            return false;
        RECT client{};
        if (!GetClientRect(window_, &client)) return false;
        const UINT dpi = std::max(1U, GetDpiForWindow(window_));
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto metrics = widgetrail::ComputeOverlayRenderMetrics(
            client.right - client.left, client.bottom - client.top,
            dpi, interfaceScale);
        const auto geometry = metrics
            ? widgetrail::ComputePanelLocalSurfaceGeometry(
                metrics->viewportWidthDip, metrics->viewportHeightDip)
            : std::nullopt;
        if (!metrics || !geometry || metrics->physicalPixelsPerDip <= 0.0F)
            return false;
        const widgetrail::declarative::Rect viewport{
            geometry->widgetViewportX,
            geometry->widgetViewportY,
            geometry->widgetViewportWidth,
            geometry->widgetViewportHeight,
        };
        const auto plan = declarativeRenderer_->PlanPresentationUpdate(
            *snapshot, impact, viewport);
        if (!plan || plan->work != widgetrail::IncrementalPresentationWork::NoRaster) {
            declarativeRenderer_->CancelPresentationUpdatePlan();
            return false;
        }

        const auto* descriptor = sessions_.FindDescriptor(widgetId);
        const auto trayLayout = CurrentCompositionTrayLayout();
        if (accessibilityActive_ &&
            (!descriptor || !trayLayout || !compositionChromeSession_ ||
             widgetAccessibilityTree_.widgetId != widgetId ||
             widgetAccessibilityTree_.snapshotSequence != impact.baseSequence)) {
            declarativeRenderer_->CancelPresentationUpdatePlan();
            return false;
        }

        std::optional<widgetrail::accessibility::Tree> semanticTree;
        std::optional<widgetrail::accessibility::ProjectionKey> projectionKey;
        widgetrail::input::InteractionRenderPresentation interactionPresentation;
        if (accessibilityActive_) {
            interactionPresentation = interactionSession_.PrepareRenderPresentation(
                *snapshot, interactionSession_.focusedElementId(),
                false,
                state_.focusRegion() == widgetrail::FocusRegion::Widget);
            const auto policy = appearanceState_.current()
                ? CurrentAccessibilityPolicy()
                : widgetrail::NativeAccessibilityPolicy{};
            projectionKey = widgetrail::accessibility::ProjectionKey{
                std::wstring{widgetId},
                descriptor->runtimeGeneration,
                snapshot->activeInputScopeId,
                state_.focusRegion() == widgetrail::FocusRegion::Widget
                    ? interactionSession_.focusedElementId() : std::wstring{},
                snapshot->sequence,
                interactionPresentation.sliderPresentationRevision,
                appearanceState_.current() ? appearanceState_.current()->revision : 0,
                viewport.x,
                viewport.y,
                viewport.width,
                viewport.height,
                geometry->panelWidth,
                geometry->panelHeight,
                metrics->physicalPixelsPerDip,
                policy.textScale,
                policy.minimumFontWeight,
                policy.reducedMotion,
                policy.reducedTransparency,
            };
            if (widgetAccessibilityProjection_.ShouldCollect(*projectionKey)) {
                const auto selectPopup = CurrentSelectPopupAccessibility(
                    metrics->viewportWidthDip, metrics->viewportHeightDip);
                semanticTree = widgetrail::accessibility::BuildWidgetTree(
                    std::wstring{widgetId}, descriptor->runtimeGeneration,
                    *snapshot, lastWidgetRenderResult_,
                    state_.focusRegion() == widgetrail::FocusRegion::Widget
                        ? std::wstring_view{interactionSession_.focusedElementId()}
                        : std::wstring_view{},
                    interactionPresentation.sliderValueOverrides,
                    selectPopup ? &*selectPopup : nullptr,
                    interactionPresentation.activeSliderElementId);
            }
        }

        if (!declarativeRenderer_->AcceptNoRasterPresentationUpdate(
                *snapshot, impact, viewport)) {
            return false;
        }
        if (semanticTree && projectionKey) {
            widgetAccessibilityTree_ = std::move(*semanticTree);
            ++widgetAccessibilityRevision_;
            widgetAccessibilityProjection_.Published(*projectionKey);
            PublishTrayAccessibility(
                *trayLayout,
                compositionChromeSession_->trayWidth,
                compositionChromeSession_->trayHeight);
        }
        committedWidgetVisualState_->snapshotSequence = snapshot->sequence;
        pendingWidgetPresentationImpact_.reset();
        ReconcileCommittedEmbeddedMediaSurface();
        return true;
    }

    [[nodiscard]] std::optional<widgetrail::WidgetSurfaceRequest>
    CurrentWidgetSurfaceRequest() const {
        if (!IsBridgeWidget(state_.activeWidget())) return std::nullopt;
        const auto* snapshot = SnapshotFor(state_.activeWidget());
        switch (widgetrail::ResolveWidgetExtentAuthority(
            snapshot != nullptr,
            presentationTransaction_.retainedSurfaceAvailable())) {
        case widgetrail::WidgetExtentAuthority::AdmittedSnapshot:
            return WidgetSurfaceRequestForSnapshot(*snapshot);
        case widgetrail::WidgetExtentAuthority::RetainedCommittedSurface:
            return presentationTransaction_.retainedSurfaceRequest();
        case widgetrail::WidgetExtentAuthority::CompactStartupFallback:
            return widgetrail::WidgetSurfaceRequest{widgetrail::WidgetSurfaceMode::Compact};
        }
        return widgetrail::WidgetSurfaceRequest{widgetrail::WidgetSurfaceMode::Compact};
    }

    [[nodiscard]] widgetrail::ResolvedWidgetSurface DesiredWidgetSurfaceTarget() const {
        const auto request = CurrentWidgetSurfaceRequest();
        if (!request ||
            (request->widthMode == widgetrail::WidgetSurfaceAxisMode::Preferred &&
             request->heightMode == widgetrail::WidgetSurfaceAxisMode::Preferred)) {
            return widgetrail::ResolveWidgetSurfaceTarget(request, CurrentTextScale());
        }

        RECT workArea{};
        UINT dpi = 96;
        float interfaceScale = 1.0F;
        if (fixedChromeAnchor_) {
            workArea = fixedChromeAnchor_->workArea;
            dpi = fixedChromeAnchor_->dpi;
            interfaceScale = fixedChromeAnchor_->interfaceScale;
        } else {
            HWND monitorTarget = window_;
            if (platform_) {
                const HWND remembered = reinterpret_cast<HWND>(
                    WidgetRailOverlayPlatformRememberedForegroundTarget(platform_));
                monitorTarget = reinterpret_cast<HWND>(
                    WidgetRailOverlayPlatformResolveForegroundTarget(
                        platform_, reinterpret_cast<std::uintptr_t>(window_),
                        PlatformBoolean(remembered && IsWindow(remembered))));
            }
            const HMONITOR monitor = MonitorFromWindow(
                monitorTarget, MONITOR_DEFAULTTONEAREST);
            MONITORINFO monitorInfo{sizeof(monitorInfo)};
            if (!monitor || !GetMonitorInfoW(monitor, &monitorInfo))
                return widgetrail::ResolveWidgetSurfaceTarget(request, CurrentTextScale());
            workArea = monitorInfo.rcWork;
            UINT dpiY = 96;
            if (FAILED(GetDpiForMonitor(
                    monitor, MDT_EFFECTIVE_DPI, &dpi, &dpiY)) ||
                dpi == 0 || dpiY == 0) {
                dpi = 96;
            }
            interfaceScale = appearanceState_.current()
                ? static_cast<float>(appearanceState_.current()->interfaceScale)
                : 1.0F;
        }
        const widgetrail::WidgetSurfaceConstraints constraints{
            {workArea.left, workArea.top, workArea.right, workArea.bottom},
            dpi,
            interfaceScale,
            fixedChromeAnchor_
                ? fixedChromeAnchor_->textScale
                : CurrentTextScale(),
        };

        const widgetrail::WidgetSnapshot* measurementSnapshot{};
        if (state_.surface() == widgetrail::Surface::Widget) {
            const auto* admitted = SnapshotFor(state_.activeWidget());
            measurementSnapshot = admitted
                ? InteractionSnapshotFor(state_.activeWidget())
                : presentationTransaction_.retainedPresentation()
                    ? &presentationTransaction_.retainedPresentation()->snapshot
                    : nullptr;
        }
        const auto sameRequest = [](const widgetrail::WidgetSurfaceRequest& left,
                                    const widgetrail::WidgetSurfaceRequest& right) {
            return left.mode == right.mode &&
                left.widthMode == right.widthMode &&
                left.heightMode == right.heightMode &&
                left.preferredWidthDip == right.preferredWidthDip &&
                left.preferredHeightDip == right.preferredHeightDip &&
                left.minimumWidthDip == right.minimumWidthDip &&
                left.minimumHeightDip == right.minimumHeightDip;
        };
        const auto sameConstraints = [](const widgetrail::WidgetSurfaceConstraints& left,
                                        const widgetrail::WidgetSurfaceConstraints& right) {
            return left.workArea.left == right.workArea.left &&
                left.workArea.top == right.workArea.top &&
                left.workArea.right == right.workArea.right &&
                left.workArea.bottom == right.workArea.bottom &&
                left.dpi == right.dpi &&
                left.interfaceScale == right.interfaceScale &&
                left.textScale == right.textScale &&
                left.margins.side == right.margins.side &&
                left.margins.top == right.margins.top &&
                left.margins.bottom == right.margins.bottom;
        };
        const std::wstring_view instanceId = measurementSnapshot
            ? std::wstring_view{measurementSnapshot->instanceId}
            : std::wstring_view{};
        const long long sequence = measurementSnapshot
            ? measurementSnapshot->sequence
            : -1;
        if (widgetSurfaceResolutionCache_ &&
            widgetSurfaceResolutionCache_->instanceId == instanceId &&
            widgetSurfaceResolutionCache_->sequence == sequence &&
            sameRequest(widgetSurfaceResolutionCache_->request, *request) &&
            sameConstraints(widgetSurfaceResolutionCache_->constraints, constraints)) {
            return widgetSurfaceResolutionCache_->resolved;
        }
        widgetrail::WidgetSurfaceIntrinsicMeasure measure;
        if (measurementSnapshot && declarativeRenderer_ &&
            (request->widthMode == widgetrail::WidgetSurfaceAxisMode::Content ||
             request->heightMode == widgetrail::WidgetSurfaceAxisMode::Content)) {
            const bool intrinsicWidth =
                request->widthMode == widgetrail::WidgetSurfaceAxisMode::Content;
            measure = [this, measurementSnapshot, dpi, interfaceScale, intrinsicWidth](
                const float maximumWidthDip,
                const float maximumHeightDip)
                -> std::optional<widgetrail::WidgetSurfaceIntrinsicExtent> {
                widgetrail::DeclarativeRenderOptions options;
                options.pixelScale = static_cast<float>(dpi) / 96.0F * interfaceScale;
                options.accessibility = CurrentAccessibilityPolicy();
                options.surfaceBackground = effectivePanelBackground_;
                const auto measured = declarativeRenderer_->MeasureContent(
                    *measurementSnapshot,
                    {maximumWidthDip, maximumHeightDip},
                    intrinsicWidth,
                    options);
                if (!measured.succeeded) return std::nullopt;
                return widgetrail::WidgetSurfaceIntrinsicExtent{
                    measured.extent.width, measured.extent.height};
            };
        }
        const auto resolved = widgetrail::ResolveWidgetSurface(
            request, constraints, measure).value_or(
            widgetrail::ResolveWidgetSurfaceTarget(request, CurrentTextScale()));
        widgetSurfaceResolutionCache_ = WidgetSurfaceResolutionCache{
            std::wstring{instanceId}, sequence, *request, constraints, resolved};
        return resolved;
    }

    [[nodiscard]] widgetrail::OverlayPresentationExtent
    DesiredContentPanelExtentDip() const {
        const auto target = DesiredWidgetSurfaceTarget();
        const auto shellGeometry = widgetrail::ComputeOverlaySurfaceGeometry(
            target.windowWidthDip, target.windowHeightDip,
            target.panelWidthDip, target.panelHeightDip);
        if (!shellGeometry) {
            return {
                std::max(1, static_cast<int>(std::lround(target.panelWidthDip))),
                std::max(1, static_cast<int>(std::lround(target.panelHeightDip))),
            };
        }
        return {
            std::max(1, static_cast<int>(std::lround(shellGeometry->panelWidth))),
            std::max(1, static_cast<int>(std::lround(
                shellGeometry->footerY - shellGeometry->panelY))),
        };
    }

    [[nodiscard]] std::optional<widgetrail::OverlayPresentationExtent>
    ExactRefreshRetainedPresentationExtentDip() const noexcept {
        if (state_.surface() != widgetrail::Surface::Widget ||
            !committedWidgetVisualState_) {
            return std::nullopt;
        }
        const std::wstring_view widgetId = state_.activeWidget();
        const auto presentation = sessions_.Presentation(widgetId);
        const auto* snapshot = presentation.snapshot;
        const auto* descriptor = sessions_.FindDescriptor(widgetId);
        const auto& committed = *committedWidgetVisualState_;
        const auto& destination = presentationTransaction_.committedDestination();
        if (!presentation.RefreshPending() ||
            !snapshot || !descriptor || !destination ||
            committed.widgetId != widgetId ||
            committed.instanceId != snapshot->instanceId ||
            committed.runtimeGeneration != descriptor->runtimeGeneration ||
            committed.presentationGeneration !=
                descriptor->presentationGeneration ||
            committed.snapshotSequence != snapshot->sequence ||
            destination->widgetId != widgetId) {
            return std::nullopt;
        }
        return destination->extentDip;
    }

    [[nodiscard]] std::optional<widgetrail::OverlaySurfaceGeometry>
    ComputeCurrentWidgetSurfaceGeometry(
        const float viewportWidthDip,
        const float viewportHeightDip) const {
        if (compositionSurface_.available()) {
            return widgetrail::ComputePanelLocalSurfaceGeometry(
                viewportWidthDip, viewportHeightDip);
        }
        const auto target = DesiredWidgetSurfaceTarget();
        return widgetrail::ComputeOverlaySurfaceGeometry(
            viewportWidthDip, viewportHeightDip,
            target.panelWidthDip, target.panelHeightDip);
    }

    [[nodiscard]] widgetrail::OverlayPresentationExtent DesiredPresentationExtentDip() const {
        if (state_.surface() != widgetrail::Surface::Widget) {
            return {kPanelWidth, kDashboardHeight};
        }
        if (OverlayFullscreenMediaRequested())
            return OverlayFullscreenPresentationExtentDip();
        if (const auto retained = ExactRefreshRetainedPresentationExtentDip())
            return *retained;
        const auto target = DesiredWidgetSurfaceTarget();
        if (compositionSurface_.available()) {
            return DesiredContentPanelExtentDip();
        }
        return {
            static_cast<int>(std::lround(target.windowWidthDip)),
            static_cast<int>(std::lround(target.windowHeightDip)),
        };
    }

    [[nodiscard]] widgetrail::OverlayPresentationExtent
    PresentedPresentationExtentDip() const {
        return presentationTransaction_.PresentedExtent(
            DesiredPresentationExtentDip(), compositionSurface_.available());
    }

    [[nodiscard]] bool WidgetSwitchAnimationEnabled() const noexcept {
        const auto& appearance = appearanceState_.current();
        return appearance && appearance->animateWidgetSwitching;
    }

    void BeginWidgetExtentTransition(
        const widgetrail::OverlayPresentationExtent from,
        const widgetrail::OverlayPresentationExtent target) {
        presentationTransaction_.BeginExtentTransition(
            from, target, GetTickCount64(),
            CurrentAccessibilityPolicy().reducedMotion,
            compositionSurface_.available());
    }

    void ApplyPresentation(const widgetrail::OverlayPresentationDirective directive) {
        switch (directive) {
        case widgetrail::OverlayPresentationDirective::None:
            return;
        case widgetrail::OverlayPresentationDirective::Hide:
            // Lifecycle/background state was committed before presentation.
            // Shut down semantic input immediately, then defer only HWND
            // hiding, resource discard, and foreground restoration.
            if (platform_) {
                (void)WidgetRailOverlayPlatformSetWindowState(
                    platform_,
                    WRAIL_OVERLAY_PLATFORM_FALSE,
                    WRAIL_OVERLAY_PLATFORM_FALSE);
            }
            lastForegroundOwnership_.reset();
            RetireCompositionMotionForHiddenState();
            overlayTransition_.BeginClose(
                GetTickCount64(), CurrentAccessibilityPolicy().reducedMotion);
            SetTimer(window_, kControllerTimer,
                     kVisibleControllerTimerMilliseconds, nullptr);
            AdvanceOverlayTransition(GetTickCount64());
            return;
        case widgetrail::OverlayPresentationDirective::Place:
        {
            const bool wasWindowVisible = IsWindowVisible(window_) != FALSE;
            const auto result = ShowOverlay(wasWindowVisible);
            if (result == OverlayShowResult::Failed && !wasWindowVisible &&
                state_.surface() != widgetrail::Surface::Hidden) {
                AppendDiagnostic(
                    L"Initial overlay presentation failed; restoring hidden state");
                Dispatch(widgetrail::Command::CloseOverlay);
                return;
            }
            if (result == OverlayShowResult::Shown &&
                compositionSurface_.available()) {
                // ShowOverlay already rendered and transactionally committed
                // the complete destination before exposing its final HWND.
                ValidateRect(window_, nullptr);
                return;
            }
            InvalidateRect(window_, nullptr, FALSE);
            const bool initialFrameCommitRequired =
                result == OverlayShowResult::Shown &&
                !wasWindowVisible && awaitingSuccessfulOpenPaint_;
            if (initialFrameCommitRequired ||
                widgetrail::ShouldCommitVisiblePlacementSynchronously(
                    wasWindowVisible, directive)) {
                RedrawWindow(window_, nullptr, nullptr,
                    RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN);
            }
            return;
        }
        case widgetrail::OverlayPresentationDirective::Repaint:
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
    }

    struct CommittedWidgetVisualState final {
        std::wstring widgetId;
        std::wstring instanceId;
        std::wstring runtimeGeneration;
        std::wstring presentationGeneration;
        std::wstring focusId;
        std::wstring pressedElementId;
        std::wstring activeSliderElementId;
        long long snapshotSequence{};
        std::uint64_t sliderPresentationRevision{};
        long long appearanceRevision{};
        std::map<std::wstring, float, std::less<>> scrollOffsets;
        widgetrail::FocusRegion focusRegion{widgetrail::FocusRegion::Tray};
        bool animationActive{};
        std::optional<widgetrail::OverlayPlacement> contentPlacement;
    };

    [[nodiscard]] std::wstring CurrentActiveSliderElementId(
        const widgetrail::WidgetSnapshot& snapshot) {
        if (state_.focusRegion() != widgetrail::FocusRegion::Widget ||
            interactionSession_.focusedElementId().empty()) {
            return {};
        }
        const auto* focused = widgetrail::input::FindNodeInInputScope(
            snapshot, interactionSession_.focusedElementId(), snapshot.activeInputScopeId);
        const auto authority = InteractionAuthority(state_.activeWidget(), snapshot);
        if (!focused || focused->kind != L"slider" || focused->isDisabled ||
            focused->isBusy || !authority) return {};
        return focused->sliderInteractionMode != L"activateToAdjust" ||
                interactionSession_.SliderAdjustmentModeActive(
                    *authority, *focused)
            ? focused->id : std::wstring{};
    }

    [[nodiscard]] bool CanRetainCommittedWidgetPixels(
        const std::wstring_view widgetId,
        const widgetrail::WidgetSnapshot& snapshot) {
        if (!compositionSurface_.available() || !committedWidgetVisualState_)
            return false;
        const auto& committed = *committedWidgetVisualState_;
        const auto appearanceRevision = appearanceState_.current()
            ? appearanceState_.current()->revision : 0;
        const auto* descriptor = sessions_.FindDescriptor(widgetId);
        const auto interactionPresentation = interactionSession_.Presentation({
            widgetId,
            &snapshot,
            descriptor ? std::wstring_view{descriptor->runtimeGeneration}
                       : std::wstring_view{},
            descriptor ? std::wstring_view{descriptor->presentationGeneration}
                       : std::wstring_view{},
            false,
        });
        std::wstring livePressedElement{interactionPresentation.pressedElementId};
        const auto liveActiveSliderElement =
            CurrentActiveSliderElementId(snapshot);
        const std::wstring_view liveFocus =
            state_.focusRegion() == widgetrail::FocusRegion::Widget
                ? interactionPresentation.focusedElementId
                : std::wstring_view{};
        const auto& placement = presentationTransaction_.contentPlacement();
        const auto samePlacement = [&] {
            if (committed.contentPlacement.has_value() != placement.has_value())
                return false;
            if (!placement) return true;
            return committed.contentPlacement->x == placement->x &&
                committed.contentPlacement->y == placement->y &&
                committed.contentPlacement->width == placement->width &&
                committed.contentPlacement->height == placement->height;
        }();
        RECT pendingPaint{};
        const bool hasPendingPaint = window_ &&
            GetUpdateRect(window_, &pendingPaint, FALSE) != FALSE;
        if (committed.widgetId != widgetId ||
            committed.instanceId != snapshot.instanceId ||
            committed.snapshotSequence != snapshot.sequence ||
            committed.focusId != liveFocus ||
            committed.pressedElementId != livePressedElement ||
            committed.activeSliderElementId != liveActiveSliderElement ||
            committed.sliderPresentationRevision !=
                interactionPresentation.sliderPresentationRevision ||
            committed.appearanceRevision != appearanceRevision ||
            committed.scrollOffsets != lastWidgetRenderResult_.scrollOffsets ||
            committed.focusRegion != state_.focusRegion() ||
            committed.animationActive != declarativeMotionActive_ ||
            !samePlacement || overlayTransition_.active() ||
            presentationTransaction_.extentTransitionActive() ||
            compositionPlacementInProgress_ || pendingContentRenderPlan_ ||
            pendingWidgetPresentationImpact_ || hasPendingPaint) {
            return false;
        }
        return true;
    }

    [[nodiscard]] bool HasExactRefreshRetainedVisualCheckpoint() {
        if (state_.surface() != widgetrail::Surface::Widget) return false;
        const std::wstring_view widgetId = state_.activeWidget();
        const auto presentation = sessions_.Presentation(widgetId);
        if (!presentation.RefreshPending()) {
            return false;
        }
        const auto rendered = renderedSnapshotSequences_.find(
            std::wstring(widgetId));
        const auto& destination = presentationTransaction_.committedDestination();
        return rendered != renderedSnapshotSequences_.end() &&
            rendered->second == presentation.snapshot->sequence &&
            destination && destination->widgetId == widgetId &&
            destination->extentDip == DesiredPresentationExtentDip() &&
            CanRetainCommittedWidgetPixels(widgetId, *presentation.snapshot);
    }

    [[nodiscard]] const widgetrail::WidgetSnapshot* GuideSnapshotFor(
        const std::wstring_view widgetId) noexcept {
        const auto presentation = sessions_.Presentation(widgetId);
        return presentation.HasCommittedViewAuthority()
            ? presentation.snapshot
            : nullptr;
    }

    template <typename Refresh>
    void RefreshAndApplyPresentation(
        Refresh&& refresh,
        const WidgetSnapshotAdmissionAuthority* admissionAuthority = nullptr) {
        const bool wasVisible = state_.surface() != widgetrail::Surface::Hidden;
        const auto priorSurface = state_.surface();
        const std::wstring priorVisibleWidget = priorSurface == widgetrail::Surface::Widget
            ? std::wstring(state_.activeWidget())
            : std::wstring(state_.selectedWidget());
        const auto priorSessionPresentation =
            sessions_.Presentation(priorVisibleWidget);
        const std::wstring priorSnapshotInstance =
            priorSessionPresentation.snapshot
                ? priorSessionPresentation.snapshot->instanceId
                : std::wstring{};
        const long long priorSnapshotSequence =
            priorSessionPresentation.snapshot
                ? priorSessionPresentation.snapshot->sequence
                : 0;
        const bool committedOverlayFullscreen =
            CommittedOverlayFullscreenMediaAuthorityCurrent(priorVisibleWidget);
        const auto priorDesiredExtent = DesiredPresentationExtentDip();
        const auto priorExtent = compositionSurface_.available()
            ? presentationTransaction_.CommittedDestinationExtent(
                priorDesiredExtent)
            : priorDesiredExtent;
        const auto priorPresentedExtent = PresentedPresentationExtentDip();
        const auto& committedDestination =
            presentationTransaction_.committedDestination();
        const std::wstring priorWidget = committedDestination
            ? committedDestination->widgetId
            : state_.surface() == widgetrail::Surface::Widget
                ? std::wstring(state_.activeWidget())
                : std::wstring(state_.selectedWidget());
        std::forward<Refresh>(refresh)();
        // Snapshot admission can retire the captured node/action authority
        // without changing shell state. Close before presentation admission so
        // stale menu state is never rendered or dispatched for the successor.
        if (widgetContextMenu_ && !WidgetContextMenuAuthorityCurrent())
            widgetContextMenu_.reset();
        const bool isVisible = state_.surface() != widgetrail::Surface::Hidden;
        const bool settleOverlayFullscreenExit =
            committedOverlayFullscreen && !OverlayFullscreenMediaRequested();
        const auto nextExtent = DesiredPresentationExtentDip();
        const std::wstring nextVisibleWidget = state_.surface() == widgetrail::Surface::Widget
            ? std::wstring(state_.activeWidget())
            : std::wstring(state_.selectedWidget());
        const auto nextSessionPresentation =
            sessions_.Presentation(nextVisibleWidget);
        const bool pendingSnapAdmission = admissionAuthority &&
            pendingWidgetSwitchSnap_ &&
            state_.surface() == widgetrail::Surface::Widget &&
            state_.activeWidget() == admissionAuthority->widgetId &&
            nextVisibleWidget == pendingWidgetSwitchSnap_->widgetId &&
            admissionAuthority->widgetId == pendingWidgetSwitchSnap_->widgetId &&
            admissionAuthority->runtimeGeneration ==
                pendingWidgetSwitchSnap_->runtimeGeneration &&
            admissionAuthority->presentationGeneration ==
                pendingWidgetSwitchSnap_->presentationGeneration &&
            admissionAuthority->correlationId ==
                pendingWidgetSwitchSnap_->correlationId;
        const bool exactRetainedCheckpoint =
            wasVisible && isVisible && priorSurface == state_.surface() &&
            priorVisibleWidget == nextVisibleWidget &&
            priorSessionPresentation.HasCommittedViewAuthority() &&
            nextSessionPresentation.RefreshPending() &&
            priorSessionPresentation.snapshot &&
            nextSessionPresentation.snapshot &&
            priorSnapshotInstance == nextSessionPresentation.snapshot->instanceId &&
            priorSnapshotSequence == nextSessionPresentation.snapshot->sequence &&
            priorExtent == nextExtent;
        const bool exactRetainedRefresh = exactRetainedCheckpoint &&
            CanRetainCommittedWidgetPixels(
                nextVisibleWidget, *nextSessionPresentation.snapshot);
        if (exactRetainedRefresh) {
            // Refresh demand does not revoke the committed interaction/UIA
            // checkpoint. With no host-owned visual change, preserve its exact
            // pixels and accessibility projection without advancing document
            // or compositor work.
            return;
        }
        const auto& retainedPresentation =
            presentationTransaction_.retainedPresentation();
        const bool snapTrayWidgetSwitch =
            pendingSnapAdmission ||
            (priorWidget != nextVisibleWidget && retainedPresentation &&
             retainedPresentation->widgetId == priorWidget &&
             !WidgetSwitchAnimationEnabled());
        const bool animateWidgetExtent =
            wasVisible && isVisible &&
            state_.surface() == widgetrail::Surface::Widget &&
            priorExtent != nextExtent && !snapTrayWidgetSwitch &&
            !settleOverlayFullscreenExit;
        if (snapTrayWidgetSwitch || settleOverlayFullscreenExit) {
            presentationTransaction_.SettleExtent(
                nextExtent, GetTickCount64(), true);
        } else if (animateWidgetExtent) {
            BeginWidgetExtentTransition(priorPresentedExtent, nextExtent);
        }
        const auto presentation = widgetrail::DecideOverlayPresentation(
            wasVisible, isVisible, priorExtent, nextExtent);
        if (wasVisible && isVisible && priorExtent != nextExtent) {
            const std::wstring currentWidget = state_.surface() == widgetrail::Surface::Widget
                ? std::wstring(state_.activeWidget())
                : std::wstring(state_.selectedWidget());
            AppendDiagnostic(
                L"Widget presentation extent refresh widget=" + currentWidget +
                L" from=" + std::to_wstring(priorPresentedExtent.widthDip) + L"x" +
                std::to_wstring(priorPresentedExtent.heightDip) + L" to=" +
                std::to_wstring(nextExtent.widthDip) + L"x" +
                std::to_wstring(nextExtent.heightDip) + L" identity=" +
                (priorWidget == currentWidget ? L"retained" : L"changed") +
                L" target=" +
                (compositionSurface_.available()
                    ? (animateWidgetExtent && !CurrentAccessibilityPolicy().reducedMotion
                        ? L"composition-motion"
                        : L"composition-surface-commit")
                    : CurrentAccessibilityPolicy().reducedMotion ||
                      snapTrayWidgetSwitch || settleOverlayFullscreenExit
                    ? L"resize-in-place"
                    : L"animated-resize-in-place"));
        }
        ApplyPresentation(presentation);
        if (pendingSnapAdmission &&
            state_.surface() == widgetrail::Surface::Widget &&
            state_.activeWidget() == nextVisibleWidget) {
            const auto& committed =
                presentationTransaction_.committedDestination();
            if (!compositionSurface_.available() ||
                (committed && committed->widgetId == nextVisibleWidget &&
                 committed->extentDip == nextExtent)) {
                pendingWidgetSwitchSnap_.reset();
            }
        }
        // Retire the activation once the settled presentation stops honouring
        // it. Without this the mode would merely go dormant while the overlay is
        // hidden and resurrect on the next open, because the predicate would
        // find the same widget, snapshot, and surface still admitted.
        if (const auto overlayOwner = mediaSessions_.EndpointOwner(
                widgetrail::media::Endpoint::Overlay)) {
            ClearStaleOverlayFullscreenMediaActivation(*overlayOwner);
        }
    }

    struct TrayPointerTarget final {
        std::size_t slot{};
        bool activate{};
    };

    struct CurrentPinActionState final {
        bool enabled{};
        bool selected{};
        std::wstring name;
        std::wstring value;
        std::wstring targetId;
    };

    struct TrayContextMenuState final {
        std::wstring widgetId;
        std::size_t selectedItem{};
    };

    struct TrayContextMenuLayout final {
        widgetrail::declarative::Rect bounds;
        widgetrail::accessibility::TrayContextMenuSemantics semantics;
    };

    struct WidgetContextMenuState final {
        std::wstring widgetId;
        std::wstring instanceId;
        std::wstring runtimeGeneration;
        std::wstring presentationGeneration;
        long long snapshotSequence{};
        std::wstring inputScopeId;
        std::wstring sourceNodeId;
        widgetrail::declarative::Rect anchor;
        std::vector<widgetrail::WidgetContextAction> actions;
        std::size_t selectedItem{};
    };

    struct WidgetContextMenuLayout final {
        widgetrail::declarative::Rect bounds;
        widgetrail::accessibility::TrayContextMenuSemantics semantics;
    };

    [[nodiscard]] const widgetrail::WidgetNode* CurrentSelectPopupNode(
        widgetrail::input::WidgetInteractionAuthority& authority) const {
        if (!interactionSession_.selectPopup() ||
            state_.surface() != widgetrail::Surface::Widget ||
            state_.focusRegion() != widgetrail::FocusRegion::Widget)
            return nullptr;
        const auto& popup = *interactionSession_.selectPopup();
        if (state_.activeWidget() != popup.widgetId) return nullptr;
        const auto* snapshot = InteractionSnapshotFor(popup.widgetId);
        const auto* descriptor = sessions_.FindDescriptor(popup.widgetId);
        if (!snapshot || !descriptor) return nullptr;
        authority = {
            popup.widgetId, snapshot, descriptor->runtimeGeneration,
            descriptor->presentationGeneration, false};
        const auto* node = widgetrail::input::FindNodeInInputScope(
            *snapshot, popup.openerElementId, snapshot->activeInputScopeId);
        return node && interactionSession_.SelectPopupCurrent(authority, *node)
            ? node : nullptr;
    }

    [[nodiscard]] std::optional<widgetrail::input::SelectPopupLayout>
    CurrentSelectPopupLayout(const float width, const float height) const {
        widgetrail::input::WidgetInteractionAuthority authority{};
        const auto* node = CurrentSelectPopupNode(authority);
        if (!node || !interactionSession_.selectPopup()) return std::nullopt;
        const auto anchor = lastWidgetRenderResult_.focusRects.find(node->id);
        if (anchor == lastWidgetRenderResult_.focusRects.end()) return std::nullopt;
        const auto geometry = ComputeCurrentWidgetSurfaceGeometry(width, height);
        if (!geometry) return std::nullopt;
        return widgetrail::input::ComputeSelectPopupLayout(
            anchor->second,
            {geometry->widgetViewportX, geometry->widgetViewportY,
             geometry->widgetViewportWidth, geometry->widgetViewportHeight},
            *interactionSession_.selectPopup());
    }

    [[nodiscard]] std::optional<widgetrail::accessibility::SelectPopupAccessibility>
    CurrentSelectPopupAccessibility(const float width, const float height) const {
        const auto layout = CurrentSelectPopupLayout(width, height);
        const auto& popup = interactionSession_.selectPopup();
        if (!layout || !popup) return std::nullopt;
        widgetrail::accessibility::SelectPopupAccessibility result;
        result.openerElementId = popup->openerElementId;
        result.options = popup->options;
        result.highlightedOption = popup->highlightedOption;
        result.items.reserve(layout->items.size());
        for (const auto& item : layout->items)
            result.items.push_back({item.optionIndex, item.bounds});
        return result;
    }

    [[nodiscard]] widgetrail::input::SelectActivationResult
    OpenFocusedSelectPopup() {
        const std::wstring widget{state_.activeWidget()};
        const auto* snapshot = InteractionSnapshotFor(widget);
        const auto* descriptor = sessions_.FindDescriptor(widget);
        if (!snapshot || !descriptor)
            return widgetrail::input::SelectActivationResult::NotSelect;
        const auto* node = widgetrail::input::FindNodeInInputScope(
            *snapshot, interactionSession_.focusedElementId(),
            snapshot->activeInputScopeId);
        if (!node)
            return widgetrail::input::SelectActivationResult::NotSelect;
        const widgetrail::input::WidgetInteractionAuthority authority{
            widget, snapshot, descriptor->runtimeGeneration,
            descriptor->presentationGeneration, false};
        const auto activation = interactionSession_.OpenSelectPopup(authority, *node);
        if (activation == widgetrail::input::SelectActivationResult::Opened) {
            widgetAccessibilityProjection_.Clear();
            InvalidateRect(window_, nullptr, FALSE);
        }
        return activation;
    }

    void CommitSelectPopup(
        const widgetrail::ControllerInputOrigin origin) {
        widgetrail::input::WidgetInteractionAuthority authority{};
        const auto* node = CurrentSelectPopupNode(authority);
        if (!node) {
            (void)interactionSession_.CloseSelectPopup();
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        auto action = interactionSession_.CommitSelectPopup(authority, *node);
        widgetAccessibilityProjection_.Clear();
        InvalidateRect(window_, nullptr, FALSE);
        if (!action) return;
        const auto correlation = ++controllerSequence_;
        const auto handled = bridge_.SendControllerInput(
            action->request.widgetId, L"a", L"openWidget",
            action->request.sourceElementId, action->request.inputScopeId,
            action->request.snapshotSequence, correlation,
            static_cast<long long>(GetTickCount64() * 1000), L"pressed",
            std::nullopt, origin,
            action->request.runtimeGeneration, {}, std::nullopt, {},
            action->request.actionId);
        if (handled && *handled)
            RefreshAndApplyPresentation([&] {
                RefreshWidgetSnapshot(action->request.widgetId);
            });
    }

    void MoveSelectPopupByWheel(const short wheelDelta) {
        if (wheelDelta == 0) return;
        widgetrail::input::WidgetInteractionAuthority authority{};
        const auto* node = CurrentSelectPopupNode(authority);
        if (!node) return;
        const auto direction = wheelDelta > 0
            ? widgetrail::input::NavigationDirection::Up
            : widgetrail::input::NavigationDirection::Down;
        const auto steps = std::max(
            1, std::abs(static_cast<int>(wheelDelta)) / WHEEL_DELTA);
        bool changed{};
        for (int step = 0; step < steps; ++step)
            changed = interactionSession_.MoveSelectPopup(
                authority, *node, direction) || changed;
        if (changed) {
            widgetAccessibilityProjection_.Clear();
            InvalidateRect(window_, nullptr, FALSE);
        }
    }

    static constexpr float kTrayContextMenuItemHeightDip = 48.0F;
    static constexpr float kTrayContextMenuGapDip = 8.0F;
    static constexpr std::size_t kTrayContextMenuMaximumItems = 3;
    static constexpr float kTrayContextMenuHeadroomDip =
        kTrayContextMenuGapDip +
        kTrayContextMenuItemHeightDip *
            static_cast<float>(kTrayContextMenuMaximumItems);
    static constexpr float kPanelToGuideGapDip = 3.0F;

    [[nodiscard]] static float DashboardGuideContentBottomDip(
        const float height) noexcept {
        const float titleTop = std::min(10.0F, height);
        const float titleBottom = std::max(
            titleTop, std::min(44.0F, height));
        return std::max(titleBottom, std::min(66.0F, height));
    }

    [[nodiscard]] static float WidgetGuideContentBottomDip(
        const float guideHeight) noexcept {
        return guideHeight - std::min(12.0F, guideHeight * 0.25F);
    }

    [[nodiscard]] static widgetrail::shell::TrayBand TrayBandBelowGuide(
        const float height,
        const widgetrail::OverlaySurfaceGeometry* surface = nullptr) noexcept {
        const float guideBottom = surface
            ? surface->footerY +
                WidgetGuideContentBottomDip(surface->footerHeight)
            : DashboardGuideContentBottomDip(height);
        return {
            std::clamp(guideBottom, 0.0F, height),
            height,
        };
    }

    [[nodiscard]] bool IsTrayInteractivePoint(
        const widgetrail::shell::TrayLayout& layout,
        const float x,
        const float y,
        const float viewportWidth) const {
        if (widgetrail::shell::HitTestTray(layout, x, y) ||
            widgetrail::shell::HitTestTrayOverflow(layout, x, y)) {
            return true;
        }
        const auto menu = CurrentTrayContextMenuLayout(layout, viewportWidth);
        return menu && x >= menu->bounds.x && y >= menu->bounds.y &&
            x < menu->bounds.x + menu->bounds.width &&
            y < menu->bounds.y + menu->bounds.height;
    }

    [[nodiscard]] bool WidgetContextMenuAuthorityCurrent() const {
        if (!widgetContextMenu_ ||
            state_.surface() != widgetrail::Surface::Widget ||
            state_.focusRegion() != widgetrail::FocusRegion::Widget ||
            state_.activeWidget() != widgetContextMenu_->widgetId)
            return false;
        const auto* snapshot = InteractionSnapshotFor(widgetContextMenu_->widgetId);
        const auto* descriptor = sessions_.FindDescriptor(widgetContextMenu_->widgetId);
        if (!snapshot || !descriptor ||
            snapshot->instanceId != widgetContextMenu_->instanceId ||
            descriptor->runtimeGeneration != widgetContextMenu_->runtimeGeneration ||
            descriptor->presentationGeneration != widgetContextMenu_->presentationGeneration ||
            snapshot->sequence != widgetContextMenu_->snapshotSequence ||
            snapshot->activeInputScopeId != widgetContextMenu_->inputScopeId)
            return false;
        const auto* node = widgetrail::input::FindNodeInInputScope(
            *snapshot, widgetContextMenu_->sourceNodeId,
            widgetContextMenu_->inputScopeId);
        if (!node || node->kind != L"actionSurface" || node->isDisabled ||
            node->isBusy || node->contextActions.size() !=
                widgetContextMenu_->actions.size())
            return false;
        for (std::size_t index = 0; index < node->contextActions.size(); ++index) {
            const auto& current = node->contextActions[index];
            const auto& origin = widgetContextMenu_->actions[index];
            if (current.actionId != origin.actionId ||
                current.label != origin.label || current.style != origin.style ||
                current.isDisabled != origin.isDisabled ||
                current.isBusy != origin.isBusy)
                return false;
        }
        return true;
    }

    [[nodiscard]] std::optional<WidgetContextMenuLayout>
    CurrentWidgetContextMenuLayout(const float width, const float height) const {
        if (!WidgetContextMenuAuthorityCurrent() ||
            widgetContextMenu_->actions.empty()) return std::nullopt;
        const float menuWidth = std::min(320.0F, std::max(1.0F, width - 16.0F));
        const float menuHeight = kTrayContextMenuItemHeightDip *
            static_cast<float>(widgetContextMenu_->actions.size());
        const float left = std::clamp(
            widgetContextMenu_->anchor.x + widgetContextMenu_->anchor.width * 0.5F -
                menuWidth * 0.5F,
            8.0F, std::max(8.0F, width - menuWidth - 8.0F));
        const float below = widgetContextMenu_->anchor.y +
            widgetContextMenu_->anchor.height + kTrayContextMenuGapDip;
        const float top = below + menuHeight <= height - 8.0F
            ? below
            : std::max(8.0F, widgetContextMenu_->anchor.y -
                kTrayContextMenuGapDip - menuHeight);
        WidgetContextMenuLayout result;
        result.bounds = {left, top, menuWidth, menuHeight};
        result.semantics.targetId = widgetContextMenu_->sourceNodeId;
        const auto selected = std::min(
            widgetContextMenu_->selectedItem,
            widgetContextMenu_->actions.size() - 1);
        for (std::size_t index = 0;
             index < widgetContextMenu_->actions.size(); ++index) {
            const auto& action = widgetContextMenu_->actions[index];
            result.semantics.items.push_back({
                L"host.widget.context." + std::to_wstring(index),
                action.label,
                action.isBusy ? L"Busy" : action.style == L"danger" ? L"Danger" : L"",
                widgetContextMenu_->sourceNodeId,
                {left, top + kTrayContextMenuItemHeightDip * static_cast<float>(index),
                 menuWidth, kTrayContextMenuItemHeightDip},
                widgetrail::accessibility::HostAction::InvokeWidgetContextAction,
                !action.isDisabled && !action.isBusy,
                index == selected,
            });
        }
        return result;
    }

    [[nodiscard]] CurrentPinActionState PinActionFor(
        const std::wstring_view widgetId) const {
        CurrentPinActionState action;
        action.targetId = std::wstring{widgetId};
        const auto* descriptor = sessions_.FindDescriptor(action.targetId);
        const bool samePinnedWidget = pinnedSurfaceCoordinator_.pinned() &&
            pinnedSurfaceCoordinator_.widgetId() == action.targetId;
        if (samePinnedWidget) {
            action.enabled = true;
            action.selected = true;
            action.name = L"Unpin " + std::wstring{DisplayWidgetName(action.targetId)};
            action.value = pinnedSurfaceCoordinator_.interactionMode() ==
                    widgetrail::pinned::InteractionMode::Focusable
                ? L"Pinned, Interactive"
                : L"Pinned, Click-through";
            return action;
        }
        if (pinnedSurfaceCoordinator_.pinned()) {
            action.name = L"Pin unavailable";
            action.value = L"Another widget is pinned";
            return action;
        }
        if (!descriptor || !descriptor->pinningSupported) {
            action.name = L"Pin unavailable";
            action.value = descriptor
                ? L"This widget does not support pinning"
                : L"The current widget is unavailable";
            return action;
        }
        if (!SnapshotFor(action.targetId)) {
            action.name = L"Pin unavailable";
            action.value = L"The current widget is still loading";
            return action;
        }
        action.enabled = true;
        action.name = L"Pin " + std::wstring{DisplayWidgetName(action.targetId)};
        action.value = L"Not pinned; creates a Click-through surface";
        return action;
    }

    [[nodiscard]] std::vector<CurrentPinActionState> CurrentTrayMenuActions() const {
        if (!trayContextMenu_ || trayContextMenu_->widgetId != state_.selectedWidget())
            return {};
        const auto pin = PinActionFor(trayContextMenu_->widgetId);
        if (!pin.selected) return {pin};
        CurrentPinActionState adjust;
        adjust.enabled = true;
        adjust.selected = true;
        adjust.name = L"Adjust pinned widget";
        adjust.value = L"Move with left stick or D-pad; resize with right stick";
        adjust.targetId = pin.targetId;
        CurrentPinActionState opacity;
        opacity.enabled = true;
        opacity.selected = true;
        opacity.name = L"Opacity — " +
            std::to_wstring(pinnedSurfaceCoordinator_.opacityPercent()) + L"%";
        opacity.value = L"Adjust whole pinned surface opacity from 30 to 100 percent";
        opacity.targetId = pin.targetId;
        CurrentPinActionState unpin;
        unpin.enabled = true;
        unpin.selected = true;
        unpin.name = L"Unpin";
        unpin.value = L"Remove the pinned surface";
        unpin.targetId = pin.targetId;
        return {std::move(adjust), std::move(opacity), std::move(unpin)};
    }

    [[nodiscard]] std::optional<TrayContextMenuLayout> CurrentTrayContextMenuLayout(
        const widgetrail::shell::TrayLayout& tray,
        const float width) const {
        const auto actions = CurrentTrayMenuActions();
        if (!trayContextMenu_ || actions.empty()) return std::nullopt;
        const auto tile = std::find_if(
            tray.tiles.begin(), tray.tiles.end(), [&](const auto& candidate) {
                return candidate.slot < state_.order().size() &&
                    state_.order()[candidate.slot] == trayContextMenu_->widgetId;
            });
        if (tile == tray.tiles.end()) return std::nullopt;
        const float menuWidth = std::min(286.0F, std::max(1.0F, width - 16.0F));
        const float menuHeight =
            kTrayContextMenuItemHeightDip * static_cast<float>(actions.size());
        const float left = std::clamp(
            tile->bounds.x + tile->bounds.width * 0.5F - menuWidth * 0.5F,
            8.0F, std::max(8.0F, width - menuWidth - 8.0F));
        const float top =
            tray.stripBounds.y - kTrayContextMenuGapDip - menuHeight;
        TrayContextMenuLayout result;
        result.bounds = {left, top, menuWidth, menuHeight};
        result.semantics.targetId = trayContextMenu_->widgetId;
        result.semantics.items.reserve(actions.size());
        const std::size_t selectedItem = std::min(
            trayContextMenu_->selectedItem, actions.size() - 1);
        for (std::size_t index = 0; index < actions.size(); ++index) {
            const auto& action = actions[index];
            const auto hostAction = !action.selected
                ? widgetrail::accessibility::HostAction::PinTrayWidget
                : index == 0
                    ? widgetrail::accessibility::HostAction::AdjustPinnedSurface
                    : index == 1
                        ? widgetrail::accessibility::HostAction::AdjustPinnedOpacity
                        : widgetrail::accessibility::HostAction::UnpinSurface;
            result.semantics.items.push_back({
                index == 0 ? L"host.tray.context.primary"
                    : index == 1 ? L"host.tray.context.opacity"
                                 : L"host.tray.context.unpin",
                action.name,
                action.value,
                action.targetId,
                {left, top +
                    kTrayContextMenuItemHeightDip * static_cast<float>(index),
                 menuWidth, kTrayContextMenuItemHeightDip},
                hostAction,
                action.enabled,
                index == selectedItem,
            });
        }
        return result;
    }

    [[nodiscard]] std::optional<widgetrail::CompositionMotionPlan>
    CurrentCompositionMotionPlan() const {
        if (!compositionSurface_.available()) return std::nullopt;
        RECT client{};
        if (!window_ || !GetClientRect(window_, &client)) return std::nullopt;
        return presentationTransaction_.CurrentMotionPlan(
            static_cast<unsigned int>(client.right - client.left),
            static_cast<unsigned int>(client.bottom - client.top),
            DesiredPresentationExtentDip());
    }

    [[nodiscard]] std::optional<widgetrail::CompositionChildCoordinateSpaces>
    CurrentCompositionChildCoordinates() const {
        const auto motion = CurrentCompositionMotionPlan();
        const auto& placement = presentationTransaction_.contentPlacement();
        if (!motion || !placement) return std::nullopt;
        const auto chromeOffset =
            presentationTransaction_.ChromeOffsetWithinContainer();
        const auto spaces = widgetrail::PlanCompositionChildCoordinates(
            *motion,
            static_cast<unsigned int>(placement->width),
            static_cast<unsigned int>(placement->height),
            chromeOffset.x, chromeOffset.y);
        if (spaces.content.containerWidth == 0) return std::nullopt;
        auto result = spaces;
        RECT windowBounds{};
        if (compositionChromeSession_ && window_ && GetWindowRect(window_, &windowBounds)) {
            const auto guide = ProjectChromeClientBoundsToScreen(
                compositionChromeSession_->guideClientBounds);
            const auto tray = ProjectChromeClientBoundsToScreen(
                compositionChromeSession_->trayClientBounds);
            if (!guide || !tray) return std::nullopt;
            result = widgetrail::ApplyFixedChromeChildOffsets(
                result,
                {static_cast<float>(guide->left - windowBounds.left),
                 static_cast<float>(guide->top - windowBounds.top)},
                {static_cast<float>(tray->left - windowBounds.left),
                 static_cast<float>(tray->top - windowBounds.top)});
        }
        return result;
    }

    void AppendCompositionCoordinateSample(const std::size_t stepIndex) {
        const auto spaces = CurrentCompositionChildCoordinates();
        RECT windowBounds{};
        if (!spaces || !window_ || !GetWindowRect(window_, &windowBounds)) return;
        const auto contentSize = presentationTransaction_.contentPlacement();
        if (!contentSize) return;
        const auto screenPoint = [&](const widgetrail::CompositionPoint local) {
            return widgetrail::CompositionPoint{
                static_cast<float>(windowBounds.left) + local.x,
                static_cast<float>(windowBounds.top) + local.y,
            };
        };
        const auto contentOrigin = screenPoint(
            widgetrail::ProjectContentPoint(*spaces, {0.0F, 0.0F}));
        const auto actualGuide = compositionChromeSession_
            ? ProjectChromeClientBoundsToScreen(
                compositionChromeSession_->guideClientBounds)
            : std::nullopt;
        const auto actualTray = compositionChromeSession_
            ? ProjectChromeClientBoundsToScreen(
                compositionChromeSession_->trayClientBounds)
            : std::nullopt;
        const auto actualChrome = ActualChromeWindowBounds();
        if (!actualGuide || !actualTray || !actualChrome) return;
        bool chromeExact = false;
        if (fixedChromeAnchor_) {
            fixedChromeAnchor_->actualWindowBounds = *actualChrome;
            chromeExact = EqualRect(
                &fixedChromeAnchor_->intendedWindowBounds,
                &fixedChromeAnchor_->actualWindowBounds) != FALSE;
            if (!chromeExact) {
                AppendDiagnostic(
                    L"Fixed chrome applied rectangle mismatch intended=" +
                    FormatPhysicalBounds(
                        fixedChromeAnchor_->intendedWindowBounds) +
                    L" actual=" + FormatPhysicalBounds(
                        fixedChromeAnchor_->actualWindowBounds) +
                    L" placement-reason=" + std::wstring(
                        FixedChromePlacementReasonName(
                            fixedChromeAnchor_->reason)));
            }
        }
        const widgetrail::CompositionPoint guideOrigin{
            static_cast<float>(actualGuide->left),
            static_cast<float>(actualGuide->top),
        };
        const widgetrail::CompositionPoint trayOrigin{
            static_cast<float>(actualTray->left),
            static_cast<float>(actualTray->top),
        };
        std::wstring selectedBounds = L"missing";
        if (retainedTrayPaintState_) {
            const auto selected = std::find_if(
                retainedTrayPaintState_->items.begin(),
                retainedTrayPaintState_->items.end(),
                [](const widgetrail::shell::RetainedTrayItem& item) {
                    return item.selected;
                });
            if (selected != retainedTrayPaintState_->items.end()) {
                selectedBounds =
                    std::to_wstring(static_cast<int>(trayOrigin.x) +
                                    selected->bounds.left) + L"," +
                    std::to_wstring(static_cast<int>(trayOrigin.y) +
                                    selected->bounds.top) + L"," +
                    std::to_wstring(selected->bounds.right - selected->bounds.left) +
                    L"," +
                    std::to_wstring(selected->bounds.bottom - selected->bounds.top);
            }
        }
        const auto localProbe = widgetrail::CompositionPoint{
            static_cast<float>(contentSize->width) * 0.5F,
            static_cast<float>(contentSize->height) * 0.5F,
        };
        const auto presentedProbe = widgetrail::ProjectContentPoint(*spaces, localProbe);
        const auto inverseProbe = widgetrail::InverseContentPoint(*spaces, presentedProbe);
        const auto counters = compositionSurface_.paintCounters();
        AppendDiagnostic(
            L"Composition child sample step=" + std::to_wstring(stepIndex) +
            L" content=" + std::to_wstring(static_cast<int>(contentOrigin.x)) + L"," +
            std::to_wstring(static_cast<int>(contentOrigin.y)) + L"," +
            std::to_wstring(static_cast<int>(std::lround(
                contentSize->width * spaces->content.scaleX))) + L"," +
            std::to_wstring(static_cast<int>(std::lround(
                contentSize->height * spaces->content.scaleY))) +
            L" guide=" + std::to_wstring(static_cast<int>(guideOrigin.x)) + L"," +
            std::to_wstring(static_cast<int>(guideOrigin.y)) + L"," +
            std::to_wstring(actualGuide->right - actualGuide->left) + L"," +
            std::to_wstring(actualGuide->bottom - actualGuide->top) +
            L" tray=" + std::to_wstring(static_cast<int>(trayOrigin.x)) + L"," +
            std::to_wstring(static_cast<int>(trayOrigin.y)) + L"," +
            std::to_wstring(actualTray->right - actualTray->left) + L"," +
            std::to_wstring(actualTray->bottom - actualTray->top) +
            L" chrome-hwnd=" + FormatPhysicalBounds(*actualChrome) +
            L" chrome-placement-reason=" +
            std::wstring(fixedChromeAnchor_
                ? FixedChromePlacementReasonName(fixedChromeAnchor_->reason)
                : L"missing") +
            L" chrome-placement-count=" +
            std::to_wstring(fixedChromePlacementCount_) +
            L" chrome-applied-exact=" +
            (chromeExact ? std::wstring{L"true"} : L"false") +
            L" selected=" + selectedBounds +
            L" pointer-local=" +
            std::to_wstring(static_cast<int>(std::lround(inverseProbe.x))) + L"," +
            std::to_wstring(static_cast<int>(std::lround(inverseProbe.y))) +
            L" uia-content-transform=matched" +
            L" paints=content:" + std::to_wstring(counters.content) +
            L",guide:" + std::to_wstring(counters.guide) +
            L",tray:" + std::to_wstring(counters.tray));
    }

    void HandlePointerActivation(
        const float clientX,
        const float clientY,
        const bool openContext = false) {
        if (state_.surface() == widgetrail::Surface::Hidden || !window_) return;
        RECT client{};
        if (!GetClientRect(window_, &client)) return;
        const UINT dpi = std::max(1U, GetDpiForWindow(window_));
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto& contentPlacement = presentationTransaction_.contentPlacement();
        const int renderWidth = contentPlacement
            ? contentPlacement->width
            : client.right - client.left;
        const int renderHeight = contentPlacement
            ? contentPlacement->height
            : client.bottom - client.top;
        const auto metrics = widgetrail::ComputeOverlayRenderMetrics(
            renderWidth, renderHeight,
            dpi, interfaceScale);
        if (!metrics) return;
        const auto childSpaces = CurrentCompositionChildCoordinates();
        float localX = clientX;
        float localY = clientY;
        if (childSpaces) {
            const auto local = widgetrail::InverseContentPoint(
                *childSpaces, {localX, localY});
            localX = local.x;
            localY = local.y;
        }
        const float x = localX / metrics->physicalPixelsPerDip;
        const float y = localY / metrics->physicalPixelsPerDip;
        const auto trayPoint = childSpaces
            ? widgetrail::InverseTrayPoint(*childSpaces, {clientX, clientY})
            : widgetrail::CompositionPoint{clientX, clientY};
        const float trayPixelsPerDip = compositionChromeSession_
            ? compositionChromeSession_->pixelsPerDip
            : metrics->physicalPixelsPerDip;
        const float trayX = trayPoint.x / trayPixelsPerDip;
        const float trayY = trayPoint.y / trayPixelsPerDip;

        std::optional<widgetrail::OverlaySurfaceGeometry> surfaceGeometry;
        if (state_.surface() == widgetrail::Surface::Widget) {
            surfaceGeometry = ComputeCurrentWidgetSurfaceGeometry(
                metrics->viewportWidthDip, metrics->viewportHeightDip);
        }
        auto trayLayout = CurrentCompositionTrayLayout();
        if (!trayLayout) {
            trayLayout = widgetrail::shell::ComputeTrayLayout(
                metrics->viewportWidthDip, metrics->viewportHeightDip,
                state_.order().size(), state_.selectedSlot(),
                TrayBandBelowGuide(
                    metrics->viewportHeightDip,
                    surfaceGeometry ? &*surfaceGeometry : nullptr));
        }
        if (interactionSession_.selectPopup()) {
            widgetrail::input::WidgetInteractionAuthority authority{};
            const auto* node = CurrentSelectPopupNode(authority);
            const auto layout = CurrentSelectPopupLayout(
                metrics->viewportWidthDip, metrics->viewportHeightDip);
            if (openContext) {
                (void)interactionSession_.CloseSelectPopup();
                widgetAccessibilityProjection_.Clear();
                InvalidateRect(window_, nullptr, FALSE);
                return;
            }
            const auto option = layout
                ? widgetrail::input::HitTestSelectPopup(*layout, x, y)
                : std::nullopt;
            if (node && option && interactionSession_.HighlightSelectPopupOption(
                    authority, *node, *option)) {
                CommitSelectPopup(
                    widgetrail::ControllerInputOrigin::PhysicalController);
                return;
            }
            (void)interactionSession_.CloseSelectPopup();
            widgetAccessibilityProjection_.Clear();
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        if (widgetContextMenu_) {
            const auto menu = CurrentWidgetContextMenuLayout(
                metrics->viewportWidthDip, metrics->viewportHeightDip);
            if (menu) {
                const auto item = std::find_if(
                    menu->semantics.items.begin(), menu->semantics.items.end(),
                    [&](const auto& candidate) {
                        const auto& bounds = candidate.bounds;
                        return x >= bounds.x && y >= bounds.y &&
                            x <= bounds.x + bounds.width &&
                            y <= bounds.y + bounds.height;
                    });
                if (item != menu->semantics.items.end()) {
                    ActivateWidgetContextMenuItem(static_cast<std::size_t>(
                        std::distance(menu->semantics.items.begin(), item)));
                    return;
                }
            }
            CloseWidgetContextMenu();
            return;
        }
        if (trayContextMenu_ && trayLayout) {
            const auto menu = CurrentTrayContextMenuLayout(
                *trayLayout,
                CurrentTrayViewportWidthDip(metrics->viewportWidthDip));
            if (menu) {
                const auto item = std::find_if(
                    menu->semantics.items.begin(), menu->semantics.items.end(),
                    [&](const auto& candidate) {
                        const auto& bounds = candidate.bounds;
                        return trayX >= bounds.x && trayY >= bounds.y &&
                            trayX <= bounds.x + bounds.width &&
                            trayY <= bounds.y + bounds.height;
                    });
                if (item != menu->semantics.items.end()) {
                    ActivateTrayContextMenuItem(static_cast<std::size_t>(
                        std::distance(menu->semantics.items.begin(), item)));
                    return;
                }
            }
            CloseTrayContextMenu();
            return;
        }

        if (state_.surface() == widgetrail::Surface::Widget) {
            const std::wstring widget{state_.activeWidget()};
            const auto* snapshot = InteractionSnapshotFor(widget);
            if (snapshot) {
                const auto hit = widgetrail::input::FindPointerHitTarget(
                    x, y, snapshot->activeInputScopeId, lastWidgetRenderResult_);
                if (hit) {
                    if (state_.focusRegion() == widgetrail::FocusRegion::Tray) {
                        Dispatch(widgetrail::Command::Activate);
                    }
                    ClearFreeScrollReentry(L"pointer-focus");
                    RetirePendingFocusGroupEntryForUserIntent(
                        widget, *snapshot, L"pointer");
                    ObserveScrollPaginationFocusIntent(
                        widget, *snapshot,
                        interactionSession_.focusedElementId(), hit->id,
                        widgetrail::input::ScrollPaginationIntentSource::Pointer);
                    const auto focus = interactionSession_.MoveFocus(
                        widget, *snapshot, hit->id);
                    InvalidateWidgetFocusChange(
                        focus.priorFocus, focus.sliderDamageNodeIds);
                    if (openContext) {
                        const auto anchor = lastWidgetRenderResult_.focusRects.find(hit->id);
                        (void)OpenWidgetContextMenu(
                            hit->id,
                            anchor == lastWidgetRenderResult_.focusRects.end()
                                ? std::nullopt
                                : std::optional<widgetrail::declarative::Rect>{anchor->second});
                    } else if (hit->enabled) {
                        DispatchControllerAction(L"A");
                    }
                    return;
                }
            }
            if (openContext) return;
        }

        std::optional<TrayPointerTarget> trayTarget;
        if (trayLayout) {
            if (const auto* hit = widgetrail::shell::HitTestTray(*trayLayout, trayX, trayY)) {
                trayTarget = TrayPointerTarget{hit->slot, true};
            } else if (const auto* overflow = widgetrail::shell::HitTestTrayOverflow(
                           *trayLayout, trayX, trayY)) {
                trayTarget = TrayPointerTarget{overflow->targetSlot, false};
            }
        }
        if (!trayTarget || trayTarget->slot >= state_.order().size()) return;
        if (state_.surface() == widgetrail::Surface::Widget &&
            state_.focusRegion() == widgetrail::FocusRegion::Widget) {
            Dispatch(widgetrail::Command::SampleWidgetBack);
        }
        if (state_.reorderMode()) Dispatch(widgetrail::Command::Cancel);
        const std::wstring targetWidget = state_.order()[trayTarget->slot];
        if (!SelectTrayWidget(targetWidget)) return;
        if (openContext) {
            OpenTrayContextMenu(targetWidget);
        } else if (trayTarget->activate && state_.selectedSlot() == trayTarget->slot) {
            Dispatch(widgetrail::Command::Activate);
        }
    }

    [[nodiscard]] bool HitTestAuthoredCompositionSurface(
        float clientX,
        float clientY) const {
        RECT client{};
        if (!window_ || !GetClientRect(window_, &client)) return false;
        const auto& contentPlacement = presentationTransaction_.contentPlacement();
        const int renderWidth = contentPlacement
            ? contentPlacement->width
            : client.right - client.left;
        const int renderHeight = contentPlacement
            ? contentPlacement->height
            : client.bottom - client.top;
        const UINT dpi = std::max(1U, GetDpiForWindow(window_));
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto metrics = widgetrail::ComputeOverlayRenderMetrics(
            renderWidth, renderHeight, dpi, interfaceScale);
        if (!metrics) return false;
        float contentX = clientX;
        float contentY = clientY;
        const auto spaces = CurrentCompositionChildCoordinates();
        if (spaces) {
            const auto local = widgetrail::InverseContentPoint(
                *spaces, {contentX, contentY});
            contentX = local.x;
            contentY = local.y;
        }
        contentX /= metrics->physicalPixelsPerDip;
        contentY /= metrics->physicalPixelsPerDip;
        const auto guidePoint = spaces
            ? widgetrail::InverseGuidePoint(*spaces, {clientX, clientY})
            : widgetrail::CompositionPoint{clientX, clientY};
        const auto trayPoint = spaces
            ? widgetrail::InverseTrayPoint(*spaces, {clientX, clientY})
            : widgetrail::CompositionPoint{clientX, clientY};
        const float chromePixelsPerDip = compositionChromeSession_
            ? compositionChromeSession_->pixelsPerDip
            : metrics->physicalPixelsPerDip;
        const float guideX = guidePoint.x / chromePixelsPerDip;
        const float guideY = guidePoint.y / chromePixelsPerDip;
        const float trayX = trayPoint.x / chromePixelsPerDip;
        const float trayY = trayPoint.y / chromePixelsPerDip;
        const auto contains = [](const float x, const float y,
                                 const widgetrail::declarative::Rect& bounds) {
            return x >= bounds.x && y >= bounds.y &&
                x <= bounds.x + bounds.width &&
                y <= bounds.y + bounds.height;
        };

        std::optional<widgetrail::OverlaySurfaceGeometry> surface;
        if (state_.surface() == widgetrail::Surface::Widget) {
            surface = ComputeCurrentWidgetSurfaceGeometry(
                metrics->viewportWidthDip, metrics->viewportHeightDip);
            const bool transparentSurface = ResolveRenderedSurfaceAppearance().effective ==
                widgetrail::surface_appearance::Mode::Transparent;
            if (surface && !transparentSurface && contains(contentX, contentY, {
                    surface->panelX, surface->panelY, surface->panelWidth,
                    surface->footerY - surface->panelY})) return true;
            if (transparentSurface) {
                if (const auto popup = CurrentSelectPopupLayout(
                        metrics->viewportWidthDip,
                        metrics->viewportHeightDip);
                    popup && contains(contentX, contentY, popup->bounds))
                    return true;
                const auto authoredContains = [&](const auto& region) {
                    return contains(contentX, contentY, region.rect);
                };
                if (std::ranges::any_of(
                        lastWidgetRenderResult_.hitRegions, authoredContains) ||
                    std::ranges::any_of(
                        lastWidgetRenderResult_.accessibilityRegions, authoredContains) ||
                    std::ranges::any_of(
                        lastWidgetRenderResult_.mediaViewportRegions,
                        [&](const auto& region) {
                            return contains(contentX, contentY, region.bounds);
                        })) return true;
            }
            if (compositionChromeSession_ && contains(guideX, guideY,
                    compositionChromeSession_->guideBounds)) return true;
            if (!compositionChromeSession_ && surface && contains(guideX, guideY, {
                    surface->panelX, surface->footerY, surface->panelWidth,
                    surface->footerHeight})) return true;
        }
        const auto tray = CurrentCompositionTrayLayout();
        if (tray) return IsTrayInteractivePoint(
            *tray, trayX, trayY,
            CurrentTrayViewportWidthDip(metrics->viewportWidthDip));
        const auto fallbackTray = widgetrail::shell::ComputeTrayLayout(
            metrics->viewportWidthDip, metrics->viewportHeightDip,
            state_.order().size(), state_.selectedSlot(),
            TrayBandBelowGuide(
                metrics->viewportHeightDip,
                surface ? &*surface : nullptr));
        return fallbackTray && IsTrayInteractivePoint(
            *fallbackTray, trayX, trayY,
            metrics->viewportWidthDip);
    }

    void PinWidget(const std::wstring_view requestedWidgetId) {
        if (pinnedSurfaceCoordinator_.pinned()) return;
        const std::wstring widgetId(requestedWidgetId);
        const auto* descriptor = sessions_.FindDescriptor(widgetId);
        const auto* snapshot = SnapshotFor(widgetId);
        if (!descriptor || !descriptor->pinningSupported || !snapshot) {
            lastActionWidgetId_ = widgetId;
            lastActionMessage_ = descriptor && descriptor->pinningSupported
                ? L"Pinned surface unavailable until the widget has loaded"
                : L"This widget does not support pinned surfaces";
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        std::wstring error;
        const auto pinnedSurface = ResolvePinnedContentSurface(*snapshot);
        widgetrail::pinned::WidgetSurfaceAdmission admission{
                descriptor->id,
                descriptor->instanceId,
                descriptor->runtimeGeneration,
                descriptor->presentationGeneration,
                descriptor->name,
                descriptor->pinningSupported,
                *snapshot,
                pinnedSurface.panelWidthDip,
                pinnedSurface.panelHeightDip,
            };
        admission.surfaceAppearancePolicy = CurrentSurfaceAppearancePolicy();
        admission.pinnedLayouts = ResolvePinnedLayouts(*snapshot);
        const auto mediaKey = CurrentEmbeddedMediaSessionKey(widgetId);
        const auto* mediaSession = mediaKey
            ? mediaSessions_.Find(*mediaKey) : nullptr;
        admission.compactMediaSessionAvailable =
            mediaSession && mediaSession->authority && mediaSession->coordinator;
        if (!pinnedSurfaceCoordinator_.Pin(std::move(admission), error)) {
            lastActionWidgetId_ = widgetId;
            lastActionMessage_ = error;
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            AppendDiagnostic(L"Pinned surface rejected for " + widgetId + L": " + error);
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        ResetPinnedPlacementNavigation();
        lastActionWidgetId_ = widgetId;
        lastActionMessage_ =
            L"Pinned layout setup: LT/RT layout, left stick/D-pad move, right stick resize, A commit, B cancel.";
        lastActionExpiresAt_ = GetTickCount64() + 5000;
        SyncWidgetActivity();
        AppendDiagnostic(L"Pinned surface created for " + widgetId);
        InvalidateRect(window_, nullptr, FALSE);
    }

    void PinCurrentSurface() {
        if (state_.surface() != widgetrail::Surface::Widget) {
            lastActionWidgetId_.clear();
            lastActionMessage_ = L"Open a pinnable widget before pressing P";
            lastActionExpiresAt_ = GetTickCount64() + 3000;
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        PinWidget(state_.activeWidget());
    }

    void CloseTrayContextMenu() {
        if (!trayContextMenu_) return;
        trayContextMenu_.reset();
        retainedTrayPaintState_.reset();
        (void)SetFocus(window_);
        InvalidateRect(window_, nullptr, FALSE);
    }

    void CloseWidgetContextMenu() {
        if (!widgetContextMenu_) return;
        widgetContextMenu_.reset();
        (void)SetFocus(window_);
        InvalidateRect(window_, nullptr, FALSE);
    }

    bool OpenWidgetContextMenu(
        const std::wstring_view nodeId,
        const std::optional<widgetrail::declarative::Rect> pointerAnchor =
            std::nullopt) {
        if (state_.surface() != widgetrail::Surface::Widget ||
            state_.focusRegion() != widgetrail::FocusRegion::Widget ||
            nodeId.empty()) return false;
        const std::wstring widget{state_.activeWidget()};
        const auto* snapshot = InteractionSnapshotFor(widget);
        const auto* descriptor = sessions_.FindDescriptor(widget);
        const auto* node = snapshot
            ? widgetrail::input::FindNodeInInputScope(
                *snapshot, nodeId, snapshot->activeInputScopeId)
            : nullptr;
        const auto focusRect = lastWidgetRenderResult_.focusRects.find(nodeId);
        if (!snapshot || !descriptor || !node ||
            node->kind != L"actionSurface" || node->isDisabled || node->isBusy ||
            node->contextActions.empty() ||
            (!pointerAnchor && focusRect == lastWidgetRenderResult_.focusRects.end()))
            return false;
        trayContextMenu_.reset();
        widgetContextMenu_ = WidgetContextMenuState{
            widget,
            snapshot->instanceId,
            descriptor->runtimeGeneration,
            descriptor->presentationGeneration,
            snapshot->sequence,
            snapshot->activeInputScopeId,
            std::wstring{nodeId},
            pointerAnchor.value_or(focusRect->second),
            node->contextActions,
            0,
        };
        (void)SetFocus(window_);
        InvalidateRect(window_, nullptr, FALSE);
        return true;
    }

    void ActivateWidgetContextMenuItem(const std::size_t itemIndex) {
        if (!WidgetContextMenuAuthorityCurrent() ||
            itemIndex >= widgetContextMenu_->actions.size()) {
            CloseWidgetContextMenu();
            return;
        }
        const auto action = widgetContextMenu_->actions[itemIndex];
        if (action.isDisabled || action.isBusy) return;
        const auto authority = *widgetContextMenu_;
        CloseWidgetContextMenu();
        const auto handled = bridge_.SendAction(
            authority.widgetId, action.actionId, authority.sourceNodeId,
            authority.inputScopeId);
        lastActionWidgetId_ = authority.widgetId;
        lastActionMessage_ = !handled
            ? L"Context action transport failed"
            : *handled ? L"Context action sent" : L"Context action was not handled";
        lastActionExpiresAt_ = GetTickCount64() + 3000;
        if (handled && *handled) {
            RefreshAndApplyPresentation([&] {
                RefreshWidgetSnapshot(authority.widgetId);
            });
        }
        InvalidateRect(window_, nullptr, FALSE);
    }

    void OpenTrayContextMenu(const std::wstring_view widgetId) {
        if (state_.surface() == widgetrail::Surface::Hidden ||
            state_.focusRegion() != widgetrail::FocusRegion::Tray ||
            pinnedSurfaceCoordinator_.controllerFocused() ||
            widgetId.empty() || widgetId != state_.selectedWidget()) return;
        widgetContextMenu_.reset();
        trayContextMenu_ = TrayContextMenuState{std::wstring(widgetId), 0};
        retainedTrayPaintState_.reset();
        (void)SetFocus(window_);
        InvalidateRect(window_, nullptr, FALSE);
    }

    void ActivateTrayContextMenuItem(const std::size_t itemIndex) {
        const auto actions = CurrentTrayMenuActions();
        if (!trayContextMenu_ || itemIndex >= actions.size()) return;
        const auto action = actions[itemIndex];
        const std::wstring widgetId = trayContextMenu_->widgetId;
        if (!action.enabled) {
            lastActionWidgetId_ = widgetId;
            lastActionMessage_ = action.value;
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        CloseTrayContextMenu();
        if (!action.selected) {
            PinWidget(widgetId);
            return;
        }
        if (itemIndex == 0) {
            ResetPinnedPlacementNavigation();
            if (pinnedSurfaceCoordinator_.pinned() &&
                pinnedSurfaceCoordinator_.widgetId() == widgetId &&
                pinnedSurfaceCoordinator_.BeginPlacement(
                    widgetrail::pinned::PlacementMode::Adjust)) {
                (void)interactionSession_.TransitionPressedPresentation(
                    widgetrail::input::PressedInputTransition::Clear);
                lastActionWidgetId_ = widgetId;
                lastActionMessage_ =
                    L"Adjust pinned widget: LT/RT layout, left stick/D-pad move, right stick resize, A commit, B cancel";
                lastActionExpiresAt_ = GetTickCount64() + 6000;
            }
            return;
        }
        if (itemIndex == 1) {
            if (pinnedSurfaceCoordinator_.pinned() &&
                pinnedSurfaceCoordinator_.widgetId() == widgetId &&
                pinnedSurfaceCoordinator_.BeginOpacityAdjustment()) {
                (void)interactionSession_.TransitionPressedPresentation(
                    widgetrail::input::PressedInputTransition::Clear);
                lastActionWidgetId_ = widgetId;
                lastActionMessage_ = L"Opacity: Left/Right adjusts by 10%, A saves, B cancels";
                lastActionExpiresAt_ = GetTickCount64() + 6000;
            }
            return;
        }
        (void)pinnedSurfaceCoordinator_.Unpin(
            widgetrail::pinned::WidgetSurfaceStopReason::Unpin);
        lastActionWidgetId_ = widgetId;
        lastActionMessage_ = L"Pinned surface removed";
        lastActionExpiresAt_ = GetTickCount64() + 2400;
        SyncWidgetActivity();
        AppendDiagnostic(L"Pinned surface removed for " + widgetId);
        InvalidateRect(window_, nullptr, FALSE);
    }

    void ToggleCurrentPinnedSurface() {
        if (pinnedSurfaceCoordinator_.pinned()) {
            const std::wstring widgetId(pinnedSurfaceCoordinator_.widgetId());
            if (state_.surface() != widgetrail::Surface::Widget ||
                state_.activeWidget() != widgetId) {
                lastActionWidgetId_ = widgetId;
                lastActionMessage_ = L"Only one pinned surface is currently supported. Press U to unpin it.";
                lastActionExpiresAt_ = GetTickCount64() + 4000;
                InvalidateRect(window_, nullptr, FALSE);
                return;
            }
            if (pinnedSurfaceCoordinator_.ToggleInteractionMode()) {
                if (pinnedSurfaceCoordinator_.interactionMode() ==
                    widgetrail::pinned::InteractionMode::Focusable) {
                    if (pinnedSurfaceCoordinator_.EnterControllerFocus() &&
                        state_.surface() == widgetrail::Surface::Widget) {
                        const auto* snapshot = InteractionSnapshotFor(
                            state_.activeWidget());
                        if (snapshot) {
                            RetirePendingFocusGroupEntryForUserIntent(
                                state_.activeWidget(), *snapshot,
                                L"pinned-controller-takeover");
                        }
                    }
                }
                lastActionWidgetId_ = widgetId;
                lastActionMessage_ =
                    pinnedSurfaceCoordinator_.interactionMode() ==
                            widgetrail::pinned::InteractionMode::Focusable
                        ? L"Pinned surface is interactive"
                        : L"Pinned surface is click-through";
                lastActionExpiresAt_ = GetTickCount64() + 2400;
                SyncWidgetActivity();
                InvalidateRect(window_, nullptr, FALSE);
            }
            return;
        }
        PinCurrentSurface();
    }

    void DrainPinnedSurfaceDiagnostics() {
        for (const auto& diagnostic :
                 pinnedSurfaceCoordinator_.TakeBackgroundSurfaceDiagnostics()) {
            AppendDiagnostic(L"Renderer pinned " + diagnostic);
        }
    }

    void DrainPinnedResizeDiagnostics() {
        const auto pinnedKey =
            mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Pinned);
        for (const auto& diagnostic :
                 pinnedSurfaceCoordinator_.TakePinnedResizeCommitDiagnostics()) {
            const auto* session = pinnedKey ? mediaSessions_.Find(*pinnedKey) : nullptr;
            const bool exactSession = session && session->authority &&
                session->coordinator &&
                session->authority->sessionId ==
                    diagnostic.committedViewport.region.mediaSessionId &&
                session->authority->presentation ==
                    EmbeddedMediaPresentationState::CompactPinned;
            const auto lifecycle = exactSession
                ? session->coordinator->state().lifecycle
                : widgetrail::richmedia::Lifecycle::Absent;
            const auto dimensionDirection = [&diagnostic](const bool horizontal) {
                if (!diagnostic.previousClientExtent) return std::wstring_view{L"initial"};
                const auto previous = horizontal
                    ? diagnostic.previousClientExtent->cx
                    : diagnostic.previousClientExtent->cy;
                const auto current = horizontal
                    ? diagnostic.currentClientExtent.cx
                    : diagnostic.currentClientExtent.cy;
                return current > previous ? std::wstring_view{L"grow"}
                    : current < previous ? std::wstring_view{L"shrink"}
                                         : std::wstring_view{L"same"};
            };
            const auto& region = diagnostic.committedViewport.region;
            AppendActionCorrelation(
                L"stage=pinned-resize-commit old=" +
                    std::to_wstring(diagnostic.previousClientExtent
                        ? diagnostic.previousClientExtent->cx : -1) + L"x" +
                    std::to_wstring(diagnostic.previousClientExtent
                        ? diagnostic.previousClientExtent->cy : -1) + L" new=" +
                    std::to_wstring(diagnostic.currentClientExtent.cx) + L"x" +
                    std::to_wstring(diagnostic.currentClientExtent.cy) +
                L" horizontal=" + std::wstring(dimensionDirection(true)) +
                L" vertical=" + std::wstring(dimensionDirection(false)) +
                L" coalesced=" + std::to_wstring(diagnostic.coalescedResizeCount) +
                L" bounds=" + std::to_wstring(region.bounds.x) + L"," +
                    std::to_wstring(region.bounds.y) + L"," +
                    std::to_wstring(region.bounds.width) + L"," +
                    std::to_wstring(region.bounds.height) +
                L" clip=" + std::to_wstring(region.clip.x) + L"," +
                    std::to_wstring(region.clip.y) + L"," +
                    std::to_wstring(region.clip.width) + L"," +
                    std::to_wstring(region.clip.height) +
                L" frame=" +
                    std::to_wstring(diagnostic.committedViewport.frameGeneration) +
                L" exact-session=" + (exactSession ? L"1" : L"0") +
                L" lifecycle=" + std::to_wstring(static_cast<int>(lifecycle)));
        }
    }

    void DrainPinnedSurfaceInputs() {
        DrainPinnedSurfaceDiagnostics();
        const auto pinnedKey =
            mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Pinned);
        if (const auto request =
                pinnedSurfaceCoordinator_.TakeCompactMediaSeekRequest()) {
            const EmbeddedMediaSessionKey requestKey{
                request->widgetId, request->instanceId,
                request->runtimeGeneration, request->sessionId};
            auto* session = mediaSessions_.Find(requestKey);
            if (pinnedKey && *pinnedKey == requestKey &&
                pinnedSurfaceCoordinator_.IsCurrentCompactMediaSeekRequest(
                    *request) &&
                session && session->authority && session->coordinator &&
                session->authority->presentation ==
                    EmbeddedMediaPresentationState::CompactPinned &&
                session->authority->presentationGeneration ==
                    request->presentationGeneration &&
                session->authority->sequence == request->snapshotSequence)
                (void)session->coordinator->SendSeekPosition(
                    request->targetSeconds);
        }
        for (const auto& selection :
                 pinnedSurfaceCoordinator_.TakeLayoutSelectionNotifications()) {
            const auto* descriptor = sessions_.FindDescriptor(selection.widgetId);
            const auto* snapshot = SnapshotFor(selection.widgetId);
            const auto correlationSequence = ++controllerSequence_;
            AppendActionCorrelation(
                L"stage=host-admission sequence=" +
                std::to_wstring(correlationSequence) +
                L" context=pinnedLayoutSelection button=view widget=" +
                selection.widgetId +
                L" focus=none scope=none native-snapshot=" +
                std::to_wstring(selection.snapshotSequence) +
                L" worker-snapshot=" +
                    std::to_wstring(snapshot ? snapshot->sequence : 0) +
                L" layout=" + selection.layoutId +
                L" selected=" + (selection.selected ? L"true" : L"false") +
                L" requested-runtime=" + selection.runtimeGeneration +
                L" current-runtime=" +
                    (descriptor ? descriptor->runtimeGeneration : L"none"));
            if (!descriptor || !snapshot ||
                descriptor->runtimeGeneration != selection.runtimeGeneration ||
                snapshot->sequence != selection.snapshotSequence) {
                AppendActionCorrelation(
                    L"stage=host-reply sequence=" +
                    std::to_wstring(correlationSequence) +
                    L" result=host-authority-rejected",
                    DiagnosticSeverity::Warning);
                continue;
            }
            const auto delivered = bridge_.SendControllerInput(
                selection.widgetId, L"view", L"pinnedLayoutSelection",
                L"", L"", selection.snapshotSequence,
                correlationSequence,
                static_cast<long long>(GetTickCount64() * 1000),
                L"pressed", std::nullopt,
                widgetrail::ControllerInputOrigin::PhysicalController,
                selection.runtimeGeneration, selection.layoutId,
                selection.selected);
            const auto replyCode = bridge_.lastControllerInputResultCode();
            AppendActionCorrelation(
                L"stage=host-reply sequence=" +
                std::to_wstring(correlationSequence) +
                L" result=" + (replyCode.empty() ? L"unknown" : replyCode),
                delivered && *delivered
                    ? DiagnosticSeverity::Debug
                    : DiagnosticSeverity::Warning);
            if (!delivered || !*delivered)
                AppendDiagnostic(L"Pinned-layout selection notification failed");
        }
        auto pagination = pinnedSurfaceCoordinator_.TakePaginationRequests(
            GetTickCount64());
        for (const auto& diagnostic : pagination.diagnostics) {
            AppendDiagnostic(
                widgetrail::input::FormatScrollPaginationDiagnostic(diagnostic));
        }
        for (const auto& pending : pagination.requests) {
            const auto* descriptor =
                sessions_.FindDescriptor(pending.request.widgetId);
            widgetrail::input::ScrollPaginationDispatchOutcome dispatch;
            dispatch.request = pending.request;
            dispatch.now = GetTickCount64();
            if (!descriptor ||
                descriptor->runtimeGeneration !=
                    pending.request.runtimeGeneration ||
                state_.surface() == widgetrail::Surface::Hidden ||
                pinnedSurfaceCoordinator_.interactionMode() !=
                    widgetrail::pinned::InteractionMode::Focusable ||
                !pinnedSurfaceCoordinator_.IsCurrentPaginationRequest(pending)) {
                dispatch.disposition =
                    widgetrail::input::ScrollPaginationDispatchDisposition::
                        StaleAuthority;
                dispatch.safeDiagnostic = L"stale-pinned-route";
            } else {
                const auto handled = bridge_.SendAction(
                    pending.request.widgetId,
                    pending.request.action.actionId,
                    pending.request.action.sourceElementId,
                    pending.request.inputScopeId);
                dispatch.disposition = !handled
                    ? widgetrail::input::ScrollPaginationDispatchDisposition::
                        TransportFailure
                    : *handled
                        ? widgetrail::input::ScrollPaginationDispatchDisposition::
                            Admitted
                        : widgetrail::input::ScrollPaginationDispatchDisposition::
                            NotHandled;
                if (!handled) {
                    dispatch.safeDiagnostic = bridge_.lastError();
                } else if (!*handled) {
                    dispatch.safeDiagnostic = L"action-not-handled";
                }
            }
            pinnedSurfaceCoordinator_.CompletePaginationRequest(
                std::move(dispatch));
        }
        for (const auto& request : pinnedSurfaceCoordinator_.TakeInputRequests()) {
            const auto* descriptor = sessions_.FindDescriptor(request.widgetId);
            const auto* workerSnapshot = SnapshotFor(request.widgetId);
            const auto correlationSequence = ++controllerSequence_;
            AppendActionCorrelation(
                L"stage=host-admission sequence=" +
                std::to_wstring(correlationSequence) +
                L" context=pinnedSurface button=" + request.protocolButton +
                L" widget=" + request.widgetId +
                L" focus=" + request.nodeId +
                L" scope=" + request.activeInputScopeId +
                L" native-snapshot=" + std::to_wstring(request.snapshotSequence) +
                L" worker-snapshot=" +
                    std::to_wstring(workerSnapshot ? workerSnapshot->sequence : 0) +
                L" requested-runtime=" + request.runtimeGeneration +
                L" current-runtime=" +
                    (descriptor ? descriptor->runtimeGeneration : L"none"));
            if (!pinnedSurfaceCoordinator_.pinned() ||
                state_.surface() == widgetrail::Surface::Hidden ||
                pinnedSurfaceCoordinator_.interactionMode() !=
                    widgetrail::pinned::InteractionMode::Focusable ||
                !descriptor ||
                descriptor->runtimeGeneration != request.runtimeGeneration ||
                !pinnedSurfaceCoordinator_.IsCurrentInputRequest(request)) {
                AppendActionCorrelation(
                    L"stage=host-reply sequence=" +
                    std::to_wstring(correlationSequence) +
                    L" result=host-authority-rejected",
                    DiagnosticSeverity::Warning);
                pinnedSurfaceCoordinator_.RejectInputRequest(
                    request, GetTickCount64());
                continue;
            }
            const auto* nativeNode = workerSnapshot
                ? widgetrail::input::FindNodeInInputScope(
                    *workerSnapshot, request.nodeId, request.activeInputScopeId)
                : nullptr;
            if (nativeNode &&
                request.protocolButton == L"a" &&
                nativeNode->actionId == request.activationActionId &&
                TryDispatchNativeMediaAction(
                    request.widgetId, *workerSnapshot, *nativeNode,
                    request.protocolButton,
                    widgetrail::input::NavigationEventPhase::Pressed)) {
                if (nativeNode->actionId !=
                    L"host.embeddedMediaSession.enterFullscreen") {
                    pinnedSurfaceCoordinator_.SetActionFeedback(
                        L"Media control updated.", false);
                }
                continue;
            }
            const auto handled = bridge_.SendControllerInput(
                request.widgetId, request.protocolButton, L"pinnedSurface",
                request.nodeId, request.activeInputScopeId, request.snapshotSequence,
                correlationSequence,
                static_cast<long long>(GetTickCount64() * 1000), L"pressed",
                request.requestedValue, request.origin,
                request.runtimeGeneration, request.selectedLayoutId,
                std::nullopt,
                request.sliderActionRequest
                    ? std::wstring_view{request.sliderActionRequest->actionId}
                    : std::wstring_view{},
                request.selectActionRequest
                    ? std::wstring_view{request.selectActionRequest->actionId}
                    : std::wstring_view{});
            const auto replyCode = bridge_.lastControllerInputResultCode();
            AppendActionCorrelation(
                L"stage=host-reply sequence=" +
                std::to_wstring(correlationSequence) +
                L" result=" + (replyCode.empty() ? L"unknown" : replyCode),
                !handled
                    ? DiagnosticSeverity::Warning
                    : *handled
                        ? DiagnosticSeverity::Debug
                        : DiagnosticSeverity::Information);
            if (!handled || !*handled)
                pinnedSurfaceCoordinator_.RejectInputRequest(
                    request, GetTickCount64());
            if (!handled) {
                pinnedSurfaceCoordinator_.SetActionFeedback(
                    L"Pinned action failed. Reopen the overlay and try again.", true);
                AppendDiagnostic(L"Pinned action transport failed for " +
                                 request.widgetId);
            } else if (*handled) {
                pinnedSurfaceCoordinator_.SetActionFeedback(
                    L"Pinned action completed.", false);
                RefreshAndApplyPresentation([&] {
                    RefreshWidgetSnapshot(request.widgetId);
                });
            } else {
                pinnedSurfaceCoordinator_.SetActionFeedback(
                    L"No pinned action is available here.", false);
            }
        }
    }

    void EmergencyHidePinnedSurfaces() {
        if (!pinnedSurfaceCoordinator_.pinned()) return;
        ResetPinnedPlacementNavigation();
        const std::wstring widgetId(pinnedSurfaceCoordinator_.widgetId());
        (void)interactionSession_.TransitionPressedPresentation(
            widgetrail::input::PressedInputTransition::Clear);
        if (pinnedSurfaceCoordinator_.EmergencyHideAll()) {
            lastActionWidgetId_ = widgetId;
            lastActionMessage_ = L"Emergency hide removed all pinned surfaces";
            lastActionExpiresAt_ = GetTickCount64() + 4000;
            AppendDiagnostic(lastActionMessage_);
            SyncWidgetActivity();
            InvalidateRect(window_, nullptr, FALSE);
        }
    }

    void HandleKey(const UINT key, const bool repeated) {
        if (state_.surface() == widgetrail::Surface::Hidden) return;
        if (widgetContextMenu_) {
            if (!WidgetContextMenuAuthorityCurrent()) {
                CloseWidgetContextMenu();
                return;
            }
            const auto count = widgetContextMenu_->actions.size();
            if (count != 0 && key == VK_UP)
                widgetContextMenu_->selectedItem =
                    widgetContextMenu_->selectedItem == 0
                        ? count - 1 : widgetContextMenu_->selectedItem - 1;
            else if (count != 0 && key == VK_DOWN)
                widgetContextMenu_->selectedItem =
                    (widgetContextMenu_->selectedItem + 1) % count;
            else if (!repeated && key == VK_RETURN)
                ActivateWidgetContextMenuItem(widgetContextMenu_->selectedItem);
            else if (!repeated && (key == VK_ESCAPE || key == VK_APPS ||
                                   (key == VK_F10 &&
                                    (GetKeyState(VK_SHIFT) & 0x8000) != 0)))
                CloseWidgetContextMenu();
            else
                return;
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        if (!repeated &&
            (key == VK_APPS ||
             (key == VK_F10 && (GetKeyState(VK_SHIFT) & 0x8000) != 0))) {
            if (state_.focusRegion() == widgetrail::FocusRegion::Tray)
                OpenTrayContextMenu(state_.selectedWidget());
            else
                (void)OpenWidgetContextMenu(
                    interactionSession_.focusedElementId());
            return;
        }
        if (!repeated && key == 'H' &&
            (GetKeyState(VK_CONTROL) & 0x8000) != 0 &&
            (GetKeyState(VK_SHIFT) & 0x8000) != 0) {
            EmergencyHidePinnedSurfaces();
            return;
        }
        if (pinnedSurfaceCoordinator_.opacityAdjustmentActive()) {
            bool changed = false;
            if (key == VK_LEFT)
                changed = pinnedSurfaceCoordinator_.StepOpacity(
                    widgetrail::pinned::PlacementDirection::Left);
            else if (key == VK_RIGHT)
                changed = pinnedSurfaceCoordinator_.StepOpacity(
                    widgetrail::pinned::PlacementDirection::Right);
            else if (!repeated && key == VK_RETURN) {
                std::wstring error;
                changed = pinnedSurfaceCoordinator_.CommitOpacity(error);
                lastActionMessage_ = changed ? L"Pinned opacity saved" : error;
                lastActionExpiresAt_ = GetTickCount64() + 3000;
            } else if (!repeated && key == VK_ESCAPE) {
                changed = pinnedSurfaceCoordinator_.CancelOpacity();
                lastActionMessage_ = L"Pinned opacity canceled";
                lastActionExpiresAt_ = GetTickCount64() + 2400;
            }
            if (changed) InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        if (pinnedSurfaceCoordinator_.placementMode() !=
            widgetrail::pinned::PlacementMode::None) {
            const bool adjusting = pinnedSurfaceCoordinator_.placementMode() ==
                widgetrail::pinned::PlacementMode::Adjust;
            const auto step = [&](const widgetrail::pinned::PlacementDirection direction) {
                return adjusting
                    ? pinnedSurfaceCoordinator_.StepPlacement(
                          widgetrail::pinned::PlacementMode::Move, direction)
                    : pinnedSurfaceCoordinator_.StepPlacement(direction);
            };
            bool changed = false;
            if (key == VK_LEFT)
                changed = step(widgetrail::pinned::PlacementDirection::Left);
            else if (key == VK_RIGHT)
                changed = step(widgetrail::pinned::PlacementDirection::Right);
            else if (key == VK_UP)
                changed = step(widgetrail::pinned::PlacementDirection::Up);
            else if (key == VK_DOWN)
                changed = step(widgetrail::pinned::PlacementDirection::Down);
            else if (!repeated && key == VK_RETURN) {
                std::wstring error;
                changed = pinnedSurfaceCoordinator_.setupActive()
                    ? pinnedSurfaceCoordinator_.CommitSetup(error)
                    : pinnedSurfaceCoordinator_.CommitPlacement(error);
                lastActionMessage_ = changed ? L"Pinned layout and placement saved" : error;
                lastActionExpiresAt_ = GetTickCount64() + 3000;
                if (changed) (void)SetFocus(window_);
            } else if (!repeated && key == VK_ESCAPE) {
                changed = pinnedSurfaceCoordinator_.setupActive()
                    ? pinnedSurfaceCoordinator_.CancelSetup()
                    : pinnedSurfaceCoordinator_.CancelPlacement();
                lastActionMessage_ = L"Pinned layout setup canceled";
                lastActionExpiresAt_ = GetTickCount64() + 2400;
                if (changed) (void)SetFocus(window_);
            }
            if (pinnedSurfaceCoordinator_.placementMode() ==
                widgetrail::pinned::PlacementMode::None) {
                ResetPinnedPlacementNavigation();
            }
            if (changed) InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        if (!repeated && (key == 'M' || key == 'R') &&
            pinnedSurfaceCoordinator_.pinned() &&
            state_.surface() == widgetrail::Surface::Widget &&
            state_.activeWidget() == pinnedSurfaceCoordinator_.widgetId()) {
            const auto mode = key == 'M'
                ? widgetrail::pinned::PlacementMode::Move
                : widgetrail::pinned::PlacementMode::Resize;
            if (pinnedSurfaceCoordinator_.BeginPlacement(mode)) {
                ResetPinnedPlacementNavigation();
                (void)interactionSession_.TransitionPressedPresentation(
                    widgetrail::input::PressedInputTransition::Clear);
                lastActionWidgetId_ = std::wstring(pinnedSurfaceCoordinator_.widgetId());
                lastActionMessage_ = key == 'M'
                    ? L"Move pinned surface: arrows, Enter commit, Esc cancel"
                    : L"Resize pinned surface: arrows, Enter commit, Esc cancel";
                lastActionExpiresAt_ = GetTickCount64() + 5000;
                InvalidateRect(window_, nullptr, FALSE);
            }
            return;
        }
        const auto phase = repeated
            ? widgetrail::input::NavigationEventPhase::Repeated
            : widgetrail::input::NavigationEventPhase::Pressed;
        if (key == 'P') {
            if (!repeated) ToggleCurrentPinnedSurface();
            return;
        }
        if (key == 'U') {
            if (!repeated && pinnedSurfaceCoordinator_.pinned()) {
                const std::wstring widgetId(pinnedSurfaceCoordinator_.widgetId());
                (void)pinnedSurfaceCoordinator_.Unpin(
                    widgetrail::pinned::WidgetSurfaceStopReason::Unpin);
                lastActionWidgetId_ = widgetId;
                lastActionMessage_ = L"Pinned surface removed";
                lastActionExpiresAt_ = GetTickCount64() + 2400;
                SyncWidgetActivity();
            }
            return;
        }
        if (key == VK_F5) {
            if (!repeated) RestartCurrentWidget();
            return;
        }
        switch (widgetrail::input::ResolveBasicKeyboardAction(key)) {
        case widgetrail::input::BasicKeyboardAction::NavigateLeft:
            if (state_.surface() == widgetrail::Surface::Widget &&
                state_.focusRegion() == widgetrail::FocusRegion::Widget) {
                HandleWidgetDirection(widgetrail::input::NavigationDirection::Left, phase, false);
            } else if (!repeated) {
                Dispatch(widgetrail::Command::NavigateLeft);
            }
            break;
        case widgetrail::input::BasicKeyboardAction::NavigateRight:
            if (state_.surface() == widgetrail::Surface::Widget &&
                state_.focusRegion() == widgetrail::FocusRegion::Widget) {
                HandleWidgetDirection(widgetrail::input::NavigationDirection::Right, phase, false);
            } else if (!repeated) {
                Dispatch(widgetrail::Command::NavigateRight);
            }
            break;
        case widgetrail::input::BasicKeyboardAction::NavigateUp:
            if (!repeated && state_.surface() == widgetrail::Surface::Widget) {
                if (state_.focusRegion() == widgetrail::FocusRegion::Tray) {
                    Dispatch(widgetrail::Command::Activate);
                } else {
                    HandleWidgetDirection(
                        widgetrail::input::NavigationDirection::Up, phase, false);
                }
            }
            break;
        case widgetrail::input::BasicKeyboardAction::NavigateDown:
            if (!repeated && state_.surface() == widgetrail::Surface::Widget &&
                state_.focusRegion() == widgetrail::FocusRegion::Widget) {
                HandleWidgetDirection(
                    widgetrail::input::NavigationDirection::Down, phase, false);
            }
            break;
        case widgetrail::input::BasicKeyboardAction::Activate:
            if (!repeated) DispatchControllerAction(L"A");
            break;
        case widgetrail::input::BasicKeyboardAction::Back:
            if (!repeated) DispatchControllerAction(L"B");
            break;
        case widgetrail::input::BasicKeyboardAction::None:
        default:
            break;
        }
    }

    [[nodiscard]] bool IsOverlayProcessForeground() const noexcept {
        const HWND foreground = GetForegroundWindow();
        if (!foreground) return false;
        DWORD foregroundProcess = 0;
        (void)GetWindowThreadProcessId(foreground, &foregroundProcess);
        return foregroundProcess == GetCurrentProcessId();
    }

    [[nodiscard]] bool AcquireOverlayForegroundInput() {
        if (!window_ || !IsWindow(window_) || !IsWindowVisible(window_)) return false;

        HWND foreground = GetForegroundWindow();
        DWORD foregroundProcess = 0;
        const DWORD foregroundThread = foreground
            ? GetWindowThreadProcessId(foreground, &foregroundProcess)
            : 0;
        const DWORD overlayThread = GetCurrentThreadId();
        const auto plan = widgetrail::input::PlanForegroundAcquisition(
            foregroundProcess == GetCurrentProcessId(), overlayThread, foregroundThread);

        if (plan.attemptDirect) {
            (void)SetForegroundWindow(window_);
            (void)SetActiveWindow(window_);
            (void)SetFocus(window_);
        }

        if (!IsOverlayProcessForeground() && plan.attachForegroundThread) {
            // A Guide callback is delivered asynchronously and does not itself
            // grant the UI thread foreground rights. Join the current
            // foreground queue for one bounded activation attempt, then detach
            // immediately. This does not bypass Windows' foreground lock when
            // the OS declines the request.
            const BOOL attached = AttachThreadInput(
                overlayThread, foregroundThread, TRUE);
            if (attached) {
                (void)BringWindowToTop(window_);
                (void)SetForegroundWindow(window_);
                (void)SetActiveWindow(window_);
                (void)SetFocus(window_);
                (void)AttachThreadInput(overlayThread, foregroundThread, FALSE);
            }
        }

        const bool confirmed = IsOverlayProcessForeground();
        if (!lastForegroundOwnership_ || *lastForegroundOwnership_ != confirmed) {
            lastForegroundOwnership_ = confirmed;
            if (confirmed) {
                AppendDiagnostic(
                    WidgetRailOverlayPlatformHasGameInput(platform_) !=
                            WRAIL_OVERLAY_PLATFORM_FALSE
                        ? L"Overlay foreground confirmed; ordinary controller input is "
                          L"GameInput foreground-exclusive"
                        : L"Overlay foreground confirmed; GameInput unavailable, ordinary "
                          L"controller input is non-exclusive XInput compatibility");
            } else {
                AppendDiagnostic(
                    L"Overlay foreground acquisition was not confirmed; the visible "
                    L"GameInput read lease remains active without exclusivity");
            }
        }
        return confirmed;
    }

    void PrimeControllerState() {
        if (!platform_) return;
        (void)WidgetRailOverlayPlatformPrimeController(
            platform_,
            PlatformBoolean(IsOverlayProcessForeground()),
            GetTickCount64());
    }

    void ResetPinnedPlacementNavigation() noexcept {
        placementMoveStickNavigator_.Reset();
        placementDpadNavigator_.Reset();
        placementRightStickNavigator_.Reset();
        placementNavigationPrimed_ = false;
    }

    void PrimePinnedPlacementNavigation(
        const WidgetRailOverlayPlatformControllerFrame& frame,
        const std::uint64_t now) noexcept {
        placementMoveStickNavigator_.Prime(
            frame.state.leftThumbX, frame.state.leftThumbY, now);
        placementDpadNavigator_.Prime(
            widgetrail::input::DigitalNavigationAxis(
                frame.state.buttons,
                XINPUT_GAMEPAD_DPAD_LEFT,
                XINPUT_GAMEPAD_DPAD_RIGHT),
            widgetrail::input::DigitalNavigationAxis(
                frame.state.buttons,
                XINPUT_GAMEPAD_DPAD_DOWN,
                XINPUT_GAMEPAD_DPAD_UP),
            now);
        placementRightStickNavigator_.Prime(
            frame.state.rightThumbX, frame.state.rightThumbY, now);
        placementNavigationPrimed_ = true;
    }

    [[nodiscard]] static std::optional<widgetrail::input::StickNavigationEvent>
    DecodeNavigation(
        const WidgetRailOverlayPlatformNavigationEvent event) noexcept {
        widgetrail::input::NavigationDirection direction{};
        switch (event.direction) {
        case WidgetRailOverlayPlatformNavigationDirection::Left:
            direction = widgetrail::input::NavigationDirection::Left;
            break;
        case WidgetRailOverlayPlatformNavigationDirection::Right:
            direction = widgetrail::input::NavigationDirection::Right;
            break;
        case WidgetRailOverlayPlatformNavigationDirection::Up:
            direction = widgetrail::input::NavigationDirection::Up;
            break;
        case WidgetRailOverlayPlatformNavigationDirection::Down:
            direction = widgetrail::input::NavigationDirection::Down;
            break;
        case WidgetRailOverlayPlatformNavigationDirection::None:
        default:
            return std::nullopt;
        }
        const auto phase = event.phase ==
                WidgetRailOverlayPlatformNavigationPhase::Repeated
            ? widgetrail::input::NavigationEventPhase::Repeated
            : widgetrail::input::NavigationEventPhase::Pressed;
        return widgetrail::input::StickNavigationEvent{direction, phase};
    }

    [[nodiscard]] static bool TextEntryControllerFrameNeutral(
        const WidgetRailOverlayPlatformControllerFrame& frame) noexcept {
        const auto withinDeadZone = [](const std::int16_t value, const int deadZone) {
            return std::abs(static_cast<int>(value)) <= deadZone;
        };
        return frame.state.buttons == 0 &&
            frame.state.leftTrigger < XINPUT_GAMEPAD_TRIGGER_THRESHOLD &&
            frame.state.rightTrigger < XINPUT_GAMEPAD_TRIGGER_THRESHOLD &&
            withinDeadZone(frame.state.leftThumbX, XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE) &&
            withinDeadZone(frame.state.leftThumbY, XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE) &&
            withinDeadZone(frame.state.rightThumbX, XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE) &&
            withinDeadZone(frame.state.rightThumbY, XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE) &&
            !DecodeNavigation(frame.stickNavigation) &&
            !DecodeNavigation(frame.dpadNavigation);
    }

    [[nodiscard]] static std::wstring_view FreeScrollAxisName(
        const widgetrail::declarative::ScrollAxis axis) noexcept {
        switch (axis) {
        case widgetrail::declarative::ScrollAxis::Horizontal:
            return L"horizontal";
        case widgetrail::declarative::ScrollAxis::Vertical:
            return L"vertical";
        case widgetrail::declarative::ScrollAxis::None:
        default:
            return L"none";
        }
    }

    [[nodiscard]] static std::wstring_view FocusedScrollResolutionName(
        const widgetrail::input::FocusedScrollResolutionDisposition disposition) noexcept {
        using Disposition = widgetrail::input::FocusedScrollResolutionDisposition;
        switch (disposition) {
        case Disposition::MissingFocus: return L"missing-focus";
        case Disposition::ScopeMismatch: return L"scope-mismatch";
        case Disposition::NoEligibleScroll: return L"no-eligible-scroll";
        case Disposition::StaleGeometry: return L"stale-geometry";
        case Disposition::Resolved: return L"resolved";
        default: return L"unknown";
        }
    }

    [[nodiscard]] static std::wstring_view FocusedFreeScrollPlanDispositionName(
        const widgetrail::FocusedFreeScrollPlanDisposition disposition) noexcept {
        using Disposition = widgetrail::FocusedFreeScrollPlanDisposition;
        switch (disposition) {
        case Disposition::Planned: return L"planned";
        case Disposition::MissingCheckpoint: return L"missing-checkpoint";
        case Disposition::InstanceMismatch: return L"instance-mismatch";
        case Disposition::SequenceMismatch: return L"sequence-mismatch";
        case Disposition::FocusMismatch: return L"focus-mismatch";
        case Disposition::InvalidAxis: return L"invalid-axis";
        case Disposition::InvalidDelta: return L"invalid-delta";
        case Disposition::ViewportMismatch: return L"viewport-mismatch";
        case Disposition::MissingTarget: return L"missing-target";
        case Disposition::MissingScrollViewport: return L"missing-scroll-viewport";
        case Disposition::MissingScrollBox: return L"missing-scroll-box";
        case Disposition::AxisMismatch: return L"axis-mismatch";
        case Disposition::EmptyScrollViewport: return L"empty-scroll-viewport";
        case Disposition::OffsetBoundary: return L"offset-boundary";
        case Disposition::EmptyDamage: return L"empty-damage";
        default: return L"unknown";
        }
    }

    void FlushRightStickDropDiagnostic() {
        if (rightStickDropCount_ > 1 && !rightStickDropSignature_.empty()) {
            AppendDiagnostic(
                L"Right-stick free scroll drop repeated count=" +
                std::to_wstring(rightStickDropCount_) + L" " +
                rightStickDropSignature_);
        }
        rightStickDropSignature_.clear();
        rightStickDropCount_ = 0;
    }

    void RecordRightStickDrop(
        const std::wstring_view reason,
        const std::wstring_view widget,
        const widgetrail::WidgetDescriptor* descriptor,
        const std::wstring_view scrollId = {}) {
        std::wstring signature = L"reason=" + std::wstring{reason} +
            L" widget=" + (widget.empty() ? L"none" : std::wstring{widget}) +
            L" scroll=" + (scrollId.empty() ? L"none" : std::wstring{scrollId}) +
            L" runtime=" + (descriptor ? descriptor->runtimeGeneration : L"none") +
            L" presentation=" +
                (descriptor ? descriptor->presentationGeneration : L"none") +
            L" focus=" + (interactionSession_.focusedElementId().empty()
                ? L"none" : interactionSession_.focusedElementId());
        if (signature == rightStickDropSignature_) {
            ++rightStickDropCount_;
            return;
        }
        FlushRightStickDropDiagnostic();
        rightStickDropSignature_ = std::move(signature);
        rightStickDropCount_ = 1;
        AppendDiagnostic(L"Right-stick free scroll dropped " + rightStickDropSignature_);
    }

    void ClearFreeScrollReentry(const std::wstring_view reason) {
        if (const auto prior = interactionSession_.ClearFreeScroll()) {
            AppendDiagnostic(
                L"Free scroll cleared widget=" +
                prior->widgetId + L" scroll=" + prior->scrollId +
                L" runtime=" + prior->runtimeGeneration +
                L" presentation=" + prior->presentationGeneration +
                L" focus=" + prior->focusedElementId + L" reason=" +
                std::wstring{reason});
            if (prior->focusedElementId != interactionSession_.focusedElementId()) {
                AppendDiagnostic(
                    L"Free scroll focus-key changed widget=" + prior->widgetId +
                    L" scroll=" + prior->scrollId +
                    L" runtime=" + prior->runtimeGeneration +
                    L" presentation=" + prior->presentationGeneration +
                    L" previous=" + prior->focusedElementId + L" new=" +
                    (interactionSession_.focusedElementId().empty()
                        ? L"none" : interactionSession_.focusedElementId()) +
                    L" cause=" + std::wstring{reason});
            }
        }
    }

    [[nodiscard]] bool FreeScrollBindingMatchesRetainedRefresh(
        const std::wstring_view widgetId,
        const widgetrail::WidgetDescriptor* descriptor) const noexcept {
        if (!descriptor || !interactionSession_.freeScrollBinding()) return false;
        const auto presentation = sessions_.Presentation(widgetId);
        if (presentation.authority !=
                widgetrail::WidgetPresentationAuthority::RefreshRetained ||
            !presentation.snapshot) {
            return false;
        }
        return interactionSession_.EvaluateFreeScrollAuthority({
            widgetId,
            presentation.snapshot,
            descriptor->runtimeGeneration,
            descriptor->presentationGeneration,
            true,
        }).disposition ==
            widgetrail::input::FreeScrollAuthorityDisposition::Retained;
    }

    void SetFreeScrollRefreshDeferred(const bool deferred) {
        if (!interactionSession_.SetRefreshDeferred(deferred)) return;
        const auto& binding = *interactionSession_.freeScrollBinding();
        AppendDiagnostic(
            std::wstring{deferred
                ? L"Free scroll retained during refresh widget="
                : L"Free scroll refresh authority restored widget="} +
            binding.widgetId + L" scroll=" + binding.scrollId);
    }

    [[nodiscard]] bool HandleRightStickFreeScroll(
        const WidgetRailOverlayPlatformControllerFrame& frame,
        const ULONGLONG now) {
        const auto sample = interactionSession_.SampleRightStick(
            frame.state.rightThumbX, frame.state.rightThumbY, now);
        const std::wstring widget = state_.surface() == widgetrail::Surface::Widget
            ? std::wstring{state_.activeWidget()} : std::wstring{};
        const auto* descriptor = widget.empty()
            ? nullptr : sessions_.FindDescriptor(widget);
        const auto reject = [&](const std::wstring_view reason,
                                const std::wstring_view scrollId = {}) {
            if (sample.moving)
                RecordRightStickDrop(reason, widget, descriptor, scrollId);
        };
        std::wstring_view ineligibleReason;
        if (frame.connected == WRAIL_OVERLAY_PLATFORM_FALSE)
            ineligibleReason = L"controller-disconnected";
        else if (state_.surface() != widgetrail::Surface::Widget)
            ineligibleReason = L"no-widget-surface";
        else if (state_.focusRegion() != widgetrail::FocusRegion::Widget)
            ineligibleReason = L"scope-mismatch";
        else if (!declarativeRenderer_)
            ineligibleReason = L"renderer-unavailable";
        else if (!compositionSurface_.available())
            ineligibleReason = L"composition-unavailable";
        else if (overlayTransition_.active())
            ineligibleReason = L"overlay-transition";
        else if (presentationTransaction_.extentTransitionActive())
            ineligibleReason = L"extent-transition";
        else if (compositionPlacementInProgress_)
            ineligibleReason = L"placement-in-progress";
        if (!ineligibleReason.empty()) {
            reject(ineligibleReason);
            ClearFreeScrollReentry(L"inactive-surface");
            return false;
        }
        const auto* snapshot = InteractionSnapshotFor(widget);
        if (!descriptor) {
            reject(L"missing-descriptor");
            ClearFreeScrollReentry(L"missing-authority");
            return false;
        }
        if (sessions_.Failure(widget)) {
            reject(L"widget-failed");
            ClearFreeScrollReentry(L"missing-authority");
            return false;
        }
        if (interactionSession_.focusedElementId().empty()) {
            reject(L"missing-focus");
            ClearFreeScrollReentry(L"missing-authority");
            return false;
        }
        if (!snapshot) {
            if (FreeScrollBindingMatchesRetainedRefresh(widget, descriptor)) {
                SetFreeScrollRefreshDeferred(true);
                // RefreshRetained is visual-only authority. Preserve the
                // binding and consume held-stick motion, but never scroll or
                // re-enter focus until a Current snapshot validates it again.
                reject(
                    L"retained-refresh-gated",
                    interactionSession_.freeScrollBinding()->scrollId);
                return sample.moving;
            }
            reject(L"no-current-interactive-generation");
            ClearFreeScrollReentry(L"missing-authority");
            return false;
        }
        const widgetrail::input::WidgetInteractionAuthority authority{
            widget,
            snapshot,
            descriptor->runtimeGeneration,
            descriptor->presentationGeneration,
            false,
        };
        if (widgetrail::input::SurfaceInteractionTransactions::EvaluateFreeScroll(
                interactionSession_.freeScrollState(), authority,
                interactionSession_.focusedElementId(), lastWidgetRenderResult_)
                .disposition ==
            widgetrail::input::FreeScrollAuthorityDisposition::Replaced) {
            ClearFreeScrollReentry(L"authority-changed");
        }
        if (interactionSession_.freeScrollBinding())
            SetFreeScrollRefreshDeferred(false);
        if (!sample.moving) {
            FlushRightStickDropDiagnostic();
            if (sample.returnedToDeadZone &&
                interactionSession_.freeScrollBinding()) {
                const auto& binding = *interactionSession_.freeScrollBinding();
                AppendDiagnostic(
                    L"Free scroll pending re-entry widget=" + widget +
                    L" scroll=" + binding.scrollId +
                    L" axis=" + std::wstring{FreeScrollAxisName(
                        binding.axis)});
            }
            return false;
        }
        // Renderer-local motion can coexist with an exact retained scroll
        // plan. A pending widget impact cannot: its snapshot/layout authority
        // has not reached the retained cache that PlanFocusedFreeScroll checks.
        if (pendingWidgetPresentationImpact_) {
            reject(L"pending-widget-impact");
            return true;
        }

        RECT pendingPaint{};
        RECT client{};
        if (GetUpdateRect(window_, &pendingPaint, FALSE) != FALSE) {
            reject(L"pending-host-paint");
            return true;
        }
        if (!GetClientRect(window_, &client)) {
            reject(L"client-geometry-unavailable");
            return true;
        }
        const UINT dpi = std::max(1U, GetDpiForWindow(window_));
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        // The content HWND may retain a larger composition container while the
        // admitted child surface keeps the destination widget's authored
        // extent. The renderer checkpoint is built in that child-surface
        // coordinate space, so free-scroll planning must use the same exact
        // dimensions rather than recomputing a viewport from the container.
        const auto contentWidth = compositionSurface_.width();
        const auto contentHeight = compositionSurface_.height();
        if (contentWidth == 0 || contentHeight == 0) {
            reject(L"content-surface-unavailable");
            return true;
        }
        const auto metrics = widgetrail::ComputeOverlayRenderMetrics(
            static_cast<int>(contentWidth), static_cast<int>(contentHeight),
            dpi, interfaceScale);
        const auto geometry = metrics
            ? ComputeCurrentWidgetSurfaceGeometry(
                metrics->viewportWidthDip, metrics->viewportHeightDip)
            : std::nullopt;
        if (!metrics || !geometry || metrics->physicalPixelsPerDip <= 0.0F) {
            reject(L"stale-geometry");
            return true;
        }
        const auto axis = sample.axis == widgetrail::input::FreeScrollAxis::Horizontal
            ? widgetrail::declarative::ScrollAxis::Horizontal
            : widgetrail::declarative::ScrollAxis::Vertical;
        const widgetrail::declarative::Rect viewport{
            geometry->widgetViewportX,
            geometry->widgetViewportY,
            geometry->widgetViewportWidth,
            geometry->widgetViewportHeight,
        };
        const auto priorFreeScrollBinding = interactionSession_.freeScrollBinding()
            ? std::optional<widgetrail::input::FreeScrollBinding>{
                *interactionSession_.freeScrollBinding()}
            : std::nullopt;
        widgetrail::FocusedFreeScrollPlanDiagnostic planDiagnostic;
        const auto plan = widgetrail::input::SurfaceInteractionTransactions::PlanFreeScroll(
            interactionSession_.freeScrollState(), *declarativeRenderer_,
            authority, interactionSession_.focusedElementId(),
            lastWidgetRenderResult_, axis, sample.deltaDip, viewport,
            &planDiagnostic);
        if (!plan) {
            const auto rectText = [](const widgetrail::declarative::Rect value) {
                return std::to_wstring(value.x) + L"," +
                    std::to_wstring(value.y) + L"," +
                    std::to_wstring(value.width) + L"," +
                    std::to_wstring(value.height);
            };
            const std::wstring reason =
                L"renderer-" + std::wstring{FocusedFreeScrollPlanDispositionName(
                    planDiagnostic.disposition)} +
                L" cache-seq=" + std::to_wstring(planDiagnostic.cachedSequence) +
                L" request-seq=" + std::to_wstring(planDiagnostic.requestedSequence) +
                L" cache-viewport=" + rectText(planDiagnostic.cachedViewport) +
                L" request-viewport=" + rectText(planDiagnostic.requestedViewport) +
                L" scroll-viewport=" + rectText(planDiagnostic.scrollViewport) +
                L" scroll-box=" + rectText(planDiagnostic.scrollBox) +
                L" scroll-axis=" + std::wstring{FreeScrollAxisName(
                    planDiagnostic.scrollAxis)} +
                L" offset=" + std::to_wstring(planDiagnostic.priorOffset) +
                L"/" + std::to_wstring(planDiagnostic.maximumOffset);
            reject(
                reason,
                interactionSession_.freeScrollBinding()
                    ? interactionSession_.freeScrollBinding()->scrollId
                    : std::wstring_view{});
            return true;
        }
        if (!SubmitWidgetContentDamage(
                plan->render, metrics->physicalPixelsPerDip, client)) {
            declarativeRenderer_->CancelPresentationUpdatePlan();
            reject(L"damage-rejected", plan->scrollId);
            return true;
        }
        (void)widgetrail::input::SurfaceInteractionTransactions::CommitFreeScroll(
            interactionSession_.freeScrollState(), authority,
            interactionSession_.focusedElementId(), *plan);

        ObserveScrollPaginationIntent(
            widget, *snapshot, plan->scrollId, plan->axis,
            plan->offset < plan->priorOffset
                ? widgetrail::input::ScrollPaginationEdge::Before
                : widgetrail::input::ScrollPaginationEdge::After,
            widgetrail::input::ScrollPaginationIntentSource::RightStick);

        const bool newBinding = !priorFreeScrollBinding ||
            priorFreeScrollBinding->scrollId != plan->scrollId ||
            priorFreeScrollBinding->axis != plan->axis;
        FlushRightStickDropDiagnostic();
        committedWidgetVisualState_.reset();
        widgetAccessibilityProjection_.Clear();
        if (newBinding) {
            AppendDiagnostic(
                L"Free scroll began widget=" + widget + L" scroll=" +
                plan->scrollId + L" axis=" +
                std::wstring{FreeScrollAxisName(plan->axis)} + L" offset=" +
                std::to_wstring(plan->priorOffset) + L"->" +
                std::to_wstring(plan->offset));
        }
        return true;
    }

    [[nodiscard]] bool ConsumeFreeScrollReentry(
        const widgetrail::input::StickNavigationEvent event) {
        if (!interactionSession_.freeScrollBinding()) return false;
        const std::wstring widget{state_.activeWidget()};
        const auto* snapshot = InteractionSnapshotFor(widget);
        const auto* descriptor = sessions_.FindDescriptor(widget);
        if (!snapshot) {
            if (FreeScrollBindingMatchesRetainedRefresh(widget, descriptor)) {
                SetFreeScrollRefreshDeferred(true);
                // Do not execute the directional event against inert retained
                // semantics. Keep the pending target for the next Current
                // snapshot and consume this event without moving focus.
                return true;
            }
            ClearFreeScrollReentry(L"stale-reentry");
            return false;
        }
        if (!descriptor) {
            ClearFreeScrollReentry(L"stale-reentry");
            return false;
        }
        const widgetrail::input::WidgetInteractionAuthority authority{
            widget,
            snapshot,
            descriptor->runtimeGeneration,
            descriptor->presentationGeneration,
            false,
        };
        if (interactionSession_.EvaluateFreeScrollAuthority(authority).disposition !=
            widgetrail::input::FreeScrollAuthorityDisposition::Current) {
            ClearFreeScrollReentry(L"stale-reentry");
            return false;
        }
        SetFreeScrollRefreshDeferred(false);
        auto request = widgetrail::input::SurfaceInteractionTransactions::
            ResolveFreeScrollReentry(
                interactionSession_.freeScrollState(), authority,
                interactionSession_.focusedElementId(), lastWidgetRenderResult_);
        switch (request.disposition) {
        case widgetrail::input::FreeScrollReentryDisposition::None:
        case widgetrail::input::FreeScrollReentryDisposition::ResumeDirectionalInput:
            return false;
        case widgetrail::input::FreeScrollReentryDisposition::RecoveryConsumed:
            break;
        }
        if (!request.retiredBinding) return false;
        const auto& binding = *request.retiredBinding;
        if (!request.target) {
            AppendDiagnostic(
                L"Free scroll re-entry consumed without target widget=" + widget +
                L" scroll=" + binding.scrollId + L" direction=" +
                std::to_wstring(static_cast<int>(event.direction)));
            InvalidateRect(window_, nullptr, FALSE);
            return true;
        }

        const auto focus = interactionSession_.MoveFocus(
            widget, *snapshot, *request.target);
        widgetAccessibilityProjection_.Clear();
        if (focus.changed)
            InvalidateWidgetFocusChange(focus.priorFocus, focus.sliderDamageNodeIds);
        else
            InvalidateRect(window_, nullptr, FALSE);
        AppendDiagnostic(
            L"Free scroll re-entry widget=" + widget + L" scroll=" +
            binding.scrollId + L" axis=" +
            std::wstring{FreeScrollAxisName(binding.axis)} + L" focus=" +
            focus.focusedElementId);
        return true;
    }

    void DispatchStickNavigation(const widgetrail::input::StickNavigationEvent event) {
        using widgetrail::input::NavigationDirection;
        const auto direction = event.direction;
        if (auto* session = RichMediaProofSession();
            session && session->coordinator &&
            session->coordinator->state().lifecycle ==
                widgetrail::richmedia::Lifecycle::Visible) {
            if (direction == NavigationDirection::Left ||
                direction == NavigationDirection::Up) {
                if (!EmbeddedMediaCommandSupported(
                        session->key, L"navigatePrevious")) return;
                (void)session->coordinator->SendCommand(
                    widgetrail::richmedia::Command::NavigatePrevious);
            } else if (direction == NavigationDirection::Right ||
                       direction == NavigationDirection::Down) {
                if (!EmbeddedMediaCommandSupported(
                        session->key, L"navigateNext")) return;
                (void)session->coordinator->SendCommand(
                    widgetrail::richmedia::Command::NavigateNext);
            }
            return;
        }
        if (state_.focusRegion() == widgetrail::FocusRegion::Tray) {
            if (direction == NavigationDirection::Left) {
                Dispatch(widgetrail::Command::NavigateLeft);
            } else if (direction == NavigationDirection::Right) {
                Dispatch(widgetrail::Command::NavigateRight);
            } else if (widgetrail::input::ShouldEnterWidgetFromTray(
                           direction, event.phase)) {
                Dispatch(widgetrail::Command::Activate);
            }
            return;
        }
        if (state_.surface() != widgetrail::Surface::Widget ||
            state_.focusRegion() != widgetrail::FocusRegion::Widget) return;
        switch (direction) {
        case NavigationDirection::Left:
        case NavigationDirection::Right:
            HandleWidgetDirection(direction, event.phase, true);
            break;
        case NavigationDirection::Up: MoveWidgetFocus(L"up", event.phase); break;
        case NavigationDirection::Down: MoveWidgetFocus(L"down", event.phase); break;
        default: break;
        }
    }

    void PollController() {
        const ULONGLONG now = GetTickCount64();
        const bool foregroundOwned = IsOverlayProcessForeground();
        WidgetRailOverlayPlatformControllerFrame frame;
#if defined(WRAIL_PINNED_SLIDER_ROUTE_TESTING)
        if (testControllerFrame_) {
            frame = *testControllerFrame_;
            testControllerFrame_.reset();
        } else
#endif
        if (WidgetRailOverlayPlatformReadController(
                platform_,
                PlatformBoolean(foregroundOwned),
                now,
                &frame) !=
            WidgetRailOverlayPlatformStatus::Ok) {
            heldActionRepeat_.Reset();
            heldActionAuthority_.reset();
            if (textEntryModal_.active())
                textEntryModal_.UpdateControllerRepeat({}, now);
            return;
        }
        const bool connected =
            frame.connected != WRAIL_OVERLAY_PLATFORM_FALSE;
        const WORD buttons = frame.state.buttons;
        const WORD pressed = frame.pressedButtons;
        const WORD released = frame.releasedButtons;
        constexpr WORD repeatRecoveryChord =
            XINPUT_GAMEPAD_BACK | XINPUT_GAMEPAD_START;
        if (!connected || !foregroundOwned ||
            (buttons & repeatRecoveryChord) == repeatRecoveryChord) {
            heldActionRepeat_.Reset();
            heldActionAuthority_.reset();
        } else {
            PumpHeldActionRepeat(frame, now);
        }
        if (auto* session = RichMediaProofSession();
            session && session->coordinator &&
            session->coordinator->state().lifecycle ==
                widgetrail::richmedia::Lifecycle::Visible) {
            if (const auto direction = DecodeNavigation(frame.stickNavigation))
                DispatchStickNavigation(*direction);
            if (const auto direction = DecodeNavigation(frame.dpadNavigation))
                DispatchStickNavigation(*direction);
            if ((pressed & XINPUT_GAMEPAD_A) != 0 &&
                EmbeddedMediaCommandSupported(session->key, L"activate"))
                (void)session->coordinator->SendCommand(
                    widgetrail::richmedia::Command::Activate);
            if ((pressed & XINPUT_GAMEPAD_B) != 0 &&
                EmbeddedMediaCommandSupported(session->key, L"back"))
                (void)session->coordinator->SendCommand(
                    widgetrail::richmedia::Command::Back);
            if ((pressed & XINPUT_GAMEPAD_X) != 0 &&
                EmbeddedMediaCommandSupported(session->key, L"togglePlayback"))
                (void)session->coordinator->SendCommand(
                    widgetrail::richmedia::Command::TogglePlayback);
            if ((pressed & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0 &&
                EmbeddedMediaCommandSupported(session->key, L"seekBackward"))
                (void)session->coordinator->SendCommand(
                    widgetrail::richmedia::Command::SeekBackward);
            if ((pressed & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0 &&
                EmbeddedMediaCommandSupported(session->key, L"seekForward"))
                (void)session->coordinator->SendCommand(
                    widgetrail::richmedia::Command::SeekForward);
            return;
        }
        if (textEntryModal_.active()) {
            if (!foregroundOwned) {
                textEntryModal_.UpdateControllerRepeat({}, now);
                return;
            }
            if (textEntryControllerPhase_ ==
                TextEntryControllerPhase::AwaitingEntryNeutral) {
                textEntryModal_.UpdateControllerRepeat({}, now);
                if (TextEntryControllerFrameNeutral(frame)) {
                    textEntryControllerPhase_ = TextEntryControllerPhase::Active;
                    AppendDiagnostic(
                        L"Text entry controller scope armed after neutral frame");
                }
                // The neutral frame itself only proves release. A later fresh
                // press/navigation event is required to affect the modal.
                return;
            }
            if (textEntryControllerPhase_ != TextEntryControllerPhase::Active)
                return;
            const auto routeDirection = [&](const auto& encoded) {
                if (const auto direction = DecodeNavigation(encoded))
                    HandleWidgetDirection(direction->direction, direction->phase, true);
            };
            routeDirection(frame.stickNavigation);
            routeDirection(frame.dpadNavigation);
            if ((pressed & XINPUT_GAMEPAD_B) != 0)
                textEntryModal_.HandleController(L"B");
            textEntryModal_.UpdateControllerRepeat({
                .activateDown = (buttons & XINPUT_GAMEPAD_A) != 0,
                .activatePressed = (pressed & XINPUT_GAMEPAD_A) != 0,
                .backspaceDown = (buttons & XINPUT_GAMEPAD_X) != 0,
                .backspacePressed = (pressed & XINPUT_GAMEPAD_X) != 0,
                .caretLeftDown = (buttons & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0,
                .caretLeftPressed = (pressed & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0,
                .caretRightDown = (buttons & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0,
                .caretRightPressed = (pressed & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0,
            }, now);
            if (frame.rightTriggerPressed != WRAIL_OVERLAY_PLATFORM_FALSE)
                textEntryModal_.HandleController(L"RT");
            // The modal is the complete controller scope. Every unassigned
            // button, trigger, shortcut, repeat, and right-stick sample is
            // deliberately consumed here rather than reaching overlay state.
            return;
        }
        if (textEntryControllerPhase_ != TextEntryControllerPhase::None) {
            if (TextEntryControllerFrameNeutral(frame)) {
                textEntryControllerPhase_ = TextEntryControllerPhase::None;
                AppendDiagnostic(
                    L"Text entry controller scope retired after neutral frame");
            }
            // Consume the release/neutral sample as part of the modal
            // transaction. Only a later fresh sample can reach the overlay.
            return;
        }
        const auto moveMenuSelection = [&](const widgetrail::input::StickNavigationEvent& event) {
            const auto actions = CurrentTrayMenuActions();
            if (!trayContextMenu_ || actions.empty()) return;
            const bool previous =
                event.direction == widgetrail::input::NavigationDirection::Up;
            const bool next =
                event.direction == widgetrail::input::NavigationDirection::Down;
            if (!previous && !next) return;
            if (previous) {
                trayContextMenu_->selectedItem = trayContextMenu_->selectedItem == 0
                    ? actions.size() - 1 : trayContextMenu_->selectedItem - 1;
            } else {
                trayContextMenu_->selectedItem =
                    (trayContextMenu_->selectedItem + 1) % actions.size();
            }
            retainedTrayPaintState_.reset();
            InvalidateRect(window_, nullptr, FALSE);
        };
        if (interactionSession_.selectPopup()) {
            widgetrail::input::WidgetInteractionAuthority authority{};
            const auto* node = CurrentSelectPopupNode(authority);
            if (!node) {
                (void)interactionSession_.CloseSelectPopup();
                widgetAccessibilityProjection_.Clear();
                InvalidateRect(window_, nullptr, FALSE);
                return;
            }
            const auto moveSelect = [&](
                const widgetrail::input::StickNavigationEvent& event) {
                if (interactionSession_.MoveSelectPopup(
                        authority, *node, event.direction)) {
                    widgetAccessibilityProjection_.Clear();
                    InvalidateRect(window_, nullptr, FALSE);
                }
            };
            if (const auto direction = DecodeNavigation(frame.stickNavigation))
                moveSelect(*direction);
            if (const auto direction = DecodeNavigation(frame.dpadNavigation))
                moveSelect(*direction);
            if ((pressed & XINPUT_GAMEPAD_A) != 0)
                CommitSelectPopup(
                    widgetrail::ControllerInputOrigin::PhysicalController);
            else if ((pressed & (XINPUT_GAMEPAD_B | XINPUT_GAMEPAD_START)) != 0) {
                (void)interactionSession_.CloseSelectPopup();
                widgetAccessibilityProjection_.Clear();
                InvalidateRect(window_, nullptr, FALSE);
            }
            return;
        }
        if (widgetContextMenu_) {
            if (!WidgetContextMenuAuthorityCurrent()) {
                CloseWidgetContextMenu();
                return;
            }
            const auto moveWidgetMenuSelection = [&](
                const widgetrail::input::StickNavigationEvent& event) {
                const auto count = widgetContextMenu_->actions.size();
                if (count == 0) return;
                if (event.direction == widgetrail::input::NavigationDirection::Up)
                    widgetContextMenu_->selectedItem =
                        widgetContextMenu_->selectedItem == 0
                            ? count - 1 : widgetContextMenu_->selectedItem - 1;
                else if (event.direction == widgetrail::input::NavigationDirection::Down)
                    widgetContextMenu_->selectedItem =
                        (widgetContextMenu_->selectedItem + 1) % count;
                else return;
                InvalidateRect(window_, nullptr, FALSE);
            };
            if (const auto direction = DecodeNavigation(frame.stickNavigation))
                moveWidgetMenuSelection(*direction);
            if (const auto direction = DecodeNavigation(frame.dpadNavigation))
                moveWidgetMenuSelection(*direction);
            if ((pressed & XINPUT_GAMEPAD_A) != 0)
                ActivateWidgetContextMenuItem(widgetContextMenu_->selectedItem);
            else if ((pressed & (XINPUT_GAMEPAD_B | XINPUT_GAMEPAD_START)) != 0)
                CloseWidgetContextMenu();
            return;
        }
        if (trayContextMenu_) {
            if (trayContextMenu_->widgetId != state_.selectedWidget() ||
                state_.focusRegion() != widgetrail::FocusRegion::Tray ||
                pinnedSurfaceCoordinator_.controllerFocused()) {
                CloseTrayContextMenu();
                return;
            }
            if (const auto direction = DecodeNavigation(frame.stickNavigation))
                moveMenuSelection(*direction);
            if (const auto direction = DecodeNavigation(frame.dpadNavigation))
                moveMenuSelection(*direction);
            if ((pressed & XINPUT_GAMEPAD_A) != 0)
                ActivateTrayContextMenuItem(trayContextMenu_->selectedItem);
            else if ((pressed & (XINPUT_GAMEPAD_B | XINPUT_GAMEPAD_START)) != 0)
                CloseTrayContextMenu();
            return;
        }
        constexpr WORD recoveryChord = XINPUT_GAMEPAD_BACK | XINPUT_GAMEPAD_START;
        const bool recoveryChordDown = (buttons & recoveryChord) == recoveryChord;
        if (OverlayFullscreenMediaRequested()) {
            const auto fullscreenKey =
                mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Overlay);
            auto* fullscreenSession = fullscreenKey
                ? mediaSessions_.Find(*fullscreenKey) : nullptr;
            if (!fullscreenSession || !fullscreenSession->authority ||
                !fullscreenSession->coordinator ||
                fullscreenSession->authority->presentation !=
                    EmbeddedMediaPresentationState::OverlayFullscreen)
                return;
            if (frame.recoveryChordPressed != WRAIL_OVERLAY_PLATFORM_FALSE) {
                RestartCurrentWidget();
                return;
            }
            if ((pressed & XINPUT_GAMEPAD_B) != 0) {
                DispatchControllerAction(L"B", true);
            } else if ((pressed & XINPUT_GAMEPAD_X) != 0 &&
                       EmbeddedMediaCommandSupported(
                           *fullscreenKey, L"togglePlayback")) {
                (void)fullscreenSession->coordinator->SendCommand(
                    widgetrail::richmedia::Command::TogglePlayback);
            } else if (
                frame.leftTriggerPressed != WRAIL_OVERLAY_PLATFORM_FALSE &&
                EmbeddedMediaCommandSupported(*fullscreenKey, L"seekBackward")) {
                BeginMediaHeldActionRepeat(
                    *fullscreenKey, HeldActionKind::FullscreenMedia,
                    L"leftTrigger", now);
                if (const auto target = OverlayFullscreenMediaSeekTarget(
                        widgetrail::input::NavigationDirection::Left)) {
                    (void)fullscreenSession->coordinator->SendSeekPosition(*target);
                }
            } else if (
                frame.rightTriggerPressed != WRAIL_OVERLAY_PLATFORM_FALSE &&
                EmbeddedMediaCommandSupported(*fullscreenKey, L"seekForward")) {
                BeginMediaHeldActionRepeat(
                    *fullscreenKey, HeldActionKind::FullscreenMedia,
                    L"rightTrigger", now);
                if (const auto target = OverlayFullscreenMediaSeekTarget(
                        widgetrail::input::NavigationDirection::Right)) {
                    (void)fullscreenSession->coordinator->SendSeekPosition(*target);
                }
            }
            return;
        }
        if (!recoveryChordDown &&
            (pressed & XINPUT_GAMEPAD_START) != 0 &&
            state_.focusRegion() == widgetrail::FocusRegion::Tray &&
            !pinnedSurfaceCoordinator_.controllerFocused()) {
            OpenTrayContextMenu(state_.selectedWidget());
            return;
        }
        if (!recoveryChordDown &&
            (pressed & XINPUT_GAMEPAD_START) != 0 &&
            state_.surface() == widgetrail::Surface::Widget &&
            state_.focusRegion() == widgetrail::FocusRegion::Widget) {
            (void)OpenWidgetContextMenu(
                interactionSession_.focusedElementId());
            return;
        }
        const auto pinnedControllerCommand = widgetrail::pinned::ResolveControllerCommand({
            pinnedSurfaceCoordinator_.pinned(),
            pinnedSurfaceCoordinator_.placementMode() !=
                    widgetrail::pinned::PlacementMode::None ||
                pinnedSurfaceCoordinator_.opacityAdjustmentActive(),
            pinnedSurfaceCoordinator_.pinned() &&
                state_.surface() == widgetrail::Surface::Widget &&
                state_.activeWidget() == pinnedSurfaceCoordinator_.widgetId(),
            pinnedSurfaceCoordinator_.controllerFocused(),
            (pressed & XINPUT_GAMEPAD_A) != 0,
            (pressed & XINPUT_GAMEPAD_B) != 0,
            (pressed & XINPUT_GAMEPAD_X) != 0,
            (pressed & XINPUT_GAMEPAD_RIGHT_THUMB) != 0,
            (buttons & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0,
            (buttons & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0,
            !recoveryChordDown && (pressed & XINPUT_GAMEPAD_BACK) != 0,
        });
        if (pinnedControllerCommand ==
            widgetrail::pinned::ControllerCommand::EmergencyHide) {
            EmergencyHidePinnedSurfaces();
            return;
        }
        if (frame.recoveryChordPressed != WRAIL_OVERLAY_PLATFORM_FALSE) {
            RestartCurrentWidget();
        }

        if (pinnedSurfaceCoordinator_.opacityAdjustmentActive()) {
            const auto stepOpacity = [&](
                const widgetrail::input::StickNavigationEvent& event) {
                if (event.direction == widgetrail::input::NavigationDirection::Left)
                    (void)pinnedSurfaceCoordinator_.StepOpacity(
                        widgetrail::pinned::PlacementDirection::Left);
                else if (event.direction ==
                         widgetrail::input::NavigationDirection::Right)
                    (void)pinnedSurfaceCoordinator_.StepOpacity(
                        widgetrail::pinned::PlacementDirection::Right);
            };
            if (const auto direction = DecodeNavigation(frame.stickNavigation))
                stepOpacity(*direction);
            if (const auto direction = DecodeNavigation(frame.dpadNavigation))
                stepOpacity(*direction);
            if ((pressed & XINPUT_GAMEPAD_A) != 0) {
                std::wstring error;
                const bool committed = pinnedSurfaceCoordinator_.CommitOpacity(error);
                lastActionMessage_ = committed ? L"Pinned opacity saved" : error;
                lastActionExpiresAt_ = now + 3000;
            } else if ((pressed & XINPUT_GAMEPAD_B) != 0) {
                (void)pinnedSurfaceCoordinator_.CancelOpacity();
                lastActionMessage_ = L"Pinned opacity canceled";
                lastActionExpiresAt_ = now + 2400;
            }
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }

        const auto stepPinnedPlacement = [&](const widgetrail::input::StickNavigationEvent& event) {
            if (const auto direction =
                    widgetrail::pinned::ResolvePlacementDirection(event.direction))
                (void)pinnedSurfaceCoordinator_.StepPlacement(*direction);
        };
        if (pinnedSurfaceCoordinator_.placementMode() !=
            widgetrail::pinned::PlacementMode::None) {
            const bool adjusting = pinnedSurfaceCoordinator_.placementMode() ==
                widgetrail::pinned::PlacementMode::Adjust;
            const auto step = [&](const widgetrail::input::StickNavigationEvent& event,
                                  const widgetrail::pinned::PlacementMode operation) {
                if (adjusting) {
                    const auto direction =
                        widgetrail::pinned::ResolvePlacementDirection(event.direction);
                    if (!direction) return;
                    (void)pinnedSurfaceCoordinator_.StepPlacement(operation, *direction);
                } else {
                    stepPinnedPlacement(event);
                }
            };
            if (!placementNavigationPrimed_) {
                PrimePinnedPlacementNavigation(frame, now);
            } else {
                if (const auto direction = placementMoveStickNavigator_.UpdateEvent(
                        frame.state.leftThumbX, frame.state.leftThumbY, now))
                    step(*direction, widgetrail::pinned::PlacementMode::Move);
                if (const auto direction = placementDpadNavigator_.UpdateEvent(
                        widgetrail::input::DigitalNavigationAxis(
                            frame.state.buttons,
                            XINPUT_GAMEPAD_DPAD_LEFT,
                            XINPUT_GAMEPAD_DPAD_RIGHT),
                        widgetrail::input::DigitalNavigationAxis(
                            frame.state.buttons,
                            XINPUT_GAMEPAD_DPAD_DOWN,
                            XINPUT_GAMEPAD_DPAD_UP),
                        now))
                    step(*direction, widgetrail::pinned::PlacementMode::Move);
                if (adjusting) {
                    if (const auto resize = placementRightStickNavigator_.UpdateEvent(
                            frame.state.rightThumbX, frame.state.rightThumbY, now)) {
                        const auto direction =
                            widgetrail::pinned::ResolvePlacementDirection(
                                resize->direction);
                        if (direction) (void)pinnedSurfaceCoordinator_.StepPlacement(
                            widgetrail::pinned::PlacementMode::Resize, *direction);
                    }
                } else {
                    placementRightStickNavigator_.Reset();
                }
            }
            if (pinnedSurfaceCoordinator_.setupActive()) {
                if (frame.leftTriggerPressed != WRAIL_OVERLAY_PLATFORM_FALSE)
                    (void)pinnedSurfaceCoordinator_.CycleLayout(-1);
                if (frame.rightTriggerPressed != WRAIL_OVERLAY_PLATFORM_FALSE)
                    (void)pinnedSurfaceCoordinator_.CycleLayout(1);
            }
            if ((pressed & XINPUT_GAMEPAD_A) != 0) {
                std::wstring error;
                const bool committed = pinnedSurfaceCoordinator_.setupActive()
                    ? pinnedSurfaceCoordinator_.CommitSetup(error)
                    : pinnedSurfaceCoordinator_.CommitPlacement(error);
                lastActionMessage_ = committed ? L"Pinned layout and placement saved" : error;
                lastActionExpiresAt_ = now + 3000;
                if (committed) (void)SetFocus(window_);
            } else if ((pressed & XINPUT_GAMEPAD_B) != 0) {
                const bool canceled = pinnedSurfaceCoordinator_.setupActive()
                    ? pinnedSurfaceCoordinator_.CancelSetup()
                    : pinnedSurfaceCoordinator_.CancelPlacement();
                lastActionMessage_ = L"Pinned layout setup canceled";
                lastActionExpiresAt_ = now + 2400;
                if (canceled) (void)SetFocus(window_);
            }
            if (pinnedSurfaceCoordinator_.placementMode() ==
                widgetrail::pinned::PlacementMode::None)
                ResetPinnedPlacementNavigation();
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }
        if (pinnedSurfaceCoordinator_.controllerFocused()) {
            if (pinnedSurfaceCoordinator_.compactMediaPresentation()) {
                const auto moveCompactFocus = [&](
                    const widgetrail::input::StickNavigationEvent& event) {
                    (void)pinnedSurfaceCoordinator_.MoveControllerFocus(
                        event.direction);
                };
                if (const auto direction = DecodeNavigation(frame.stickNavigation))
                    moveCompactFocus(*direction);
                if (const auto direction = DecodeNavigation(frame.dpadNavigation))
                    moveCompactFocus(*direction);
                // View and B are host-shell escape affordances. They remain
                // available while the media endpoint is being transferred or
                // has already retired, and they terminate this sampled frame
                // so no stale media command can follow the transition.
                if (pinnedControllerCommand ==
                    widgetrail::pinned::ControllerCommand::Exit) {
                    ReturnPinnedControllerFocusToOverlay(now);
                    return;
                }
                if ((pressed & XINPUT_GAMEPAD_B) != 0) {
                    if (!pinnedSurfaceCoordinator_.CancelCompactMediaScrub()) {
                        (void)pinnedSurfaceCoordinator_.ExitControllerFocus();
                        (void)pinnedSurfaceCoordinator_.SetInteractionMode(
                            widgetrail::pinned::InteractionMode::ClickThrough);
                        lastActionMessage_ =
                            L"Compact pinned media returned to click-through";
                        lastActionExpiresAt_ = now + 2400;
                    }
                    return;
                }
                const auto compactKey = mediaSessions_.EndpointOwner(
                    widgetrail::media::Endpoint::Pinned);
                auto* compactSession = compactKey
                    ? mediaSessions_.Find(*compactKey) : nullptr;
                if (!compactSession || !compactSession->authority ||
                    !compactSession->coordinator ||
                    compactSession->authority->presentation !=
                        EmbeddedMediaPresentationState::CompactPinned ||
                    compactSession->authority->widgetId !=
                        pinnedSurfaceCoordinator_.widgetId())
                    return;
                if ((pressed & XINPUT_GAMEPAD_X) != 0 &&
                    EmbeddedMediaCommandSupported(
                        *compactKey, L"togglePlayback"))
                    (void)compactSession->coordinator->SendCommand(
                        widgetrail::richmedia::Command::TogglePlayback);
                if ((pressed & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0 &&
                    EmbeddedMediaCommandSupported(
                        *compactKey, L"navigatePrevious"))
                    (void)compactSession->coordinator->SendCommand(
                        widgetrail::richmedia::Command::NavigatePrevious);
                if ((pressed & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0 &&
                    EmbeddedMediaCommandSupported(
                        *compactKey, L"navigateNext"))
                    (void)compactSession->coordinator->SendCommand(
                        widgetrail::richmedia::Command::NavigateNext);
                if (frame.leftTriggerPressed != WRAIL_OVERLAY_PLATFORM_FALSE &&
                    EmbeddedMediaCommandSupported(
                        *compactKey, L"seekBackward")) {
                    BeginMediaHeldActionRepeat(
                        *compactKey, HeldActionKind::CompactMedia,
                        L"leftTrigger", now);
                    if (const auto target =
                            pinnedSurfaceCoordinator_.CompactMediaSeekTarget(
                                widgetrail::input::NavigationDirection::Left))
                        (void)compactSession->coordinator->SendSeekPosition(*target);
                } else if (
                    frame.rightTriggerPressed != WRAIL_OVERLAY_PLATFORM_FALSE &&
                    EmbeddedMediaCommandSupported(
                        *compactKey, L"seekForward")) {
                    BeginMediaHeldActionRepeat(
                        *compactKey, HeldActionKind::CompactMedia,
                        L"rightTrigger", now);
                    if (const auto target =
                            pinnedSurfaceCoordinator_.CompactMediaSeekTarget(
                                widgetrail::input::NavigationDirection::Right))
                        (void)compactSession->coordinator->SendSeekPosition(*target);
                }
                ReconcileCompactPinnedMediaChrome(*compactKey);
                InvalidateRect(window_, nullptr, FALSE);
                return;
            }
            if (pinnedSurfaceCoordinator_.selectPopupOpen()) {
                const auto movePinnedSelect = [&] (
                    const widgetrail::input::StickNavigationEvent& event) {
                    (void)pinnedSurfaceCoordinator_.MoveControllerFocus(
                        event.direction, false);
                };
                if (const auto direction = DecodeNavigation(frame.stickNavigation))
                    movePinnedSelect(*direction);
                if (const auto direction = DecodeNavigation(frame.dpadNavigation))
                    movePinnedSelect(*direction);
                if ((pressed & XINPUT_GAMEPAD_A) != 0 ||
                    pinnedControllerCommand ==
                        widgetrail::pinned::ControllerCommand::Activate) {
                    (void)pinnedSurfaceCoordinator_.HandleFocusedSelectButton(
                        L"a", widgetrail::ControllerInputOrigin::PhysicalController);
                } else if ((pressed & (XINPUT_GAMEPAD_B | XINPUT_GAMEPAD_START)) != 0 ||
                           pinnedControllerCommand ==
                               widgetrail::pinned::ControllerCommand::Exit) {
                    (void)pinnedSurfaceCoordinator_.HandleFocusedSelectButton(
                        L"b", widgetrail::ControllerInputOrigin::PhysicalController);
                }
                InvalidateRect(window_, nullptr, FALSE);
                return;
            }
            (void)pinnedSurfaceCoordinator_.ScrollFocusedProjection(
                frame.state.rightThumbX, frame.state.rightThumbY, now);
            // The left stick and the D-pad are one directional owner on the
            // pinned surface, exactly as they are in the full widget, so both
            // adjust a selected activation-first slider.
            const auto movePinnedFocus = [&](const widgetrail::input::StickNavigationEvent& event) {
                if (pinnedSurfaceCoordinator_.FlushSliderBeforeFocusDeparture(
                        event.direction, now)) {
                    DrainPinnedSurfaceInputs();
                }
                (void)pinnedSurfaceCoordinator_.MoveControllerFocus(
                    event.direction, true);
            };
            if (const auto direction = DecodeNavigation(frame.stickNavigation))
                movePinnedFocus(*direction);
            if (const auto direction = DecodeNavigation(frame.dpadNavigation))
                movePinnedFocus(*direction);
            if (pinnedSurfaceCoordinator_.PumpSliderInteraction(now))
                DrainPinnedSurfaceInputs();
            if (pinnedControllerCommand == widgetrail::pinned::ControllerCommand::Activate) {
                if (!pinnedSurfaceCoordinator_.HandleFocusedSelectButton(L"a") &&
                    !pinnedSurfaceCoordinator_.HandleFocusedSliderModeButton(
                        L"a", now) &&
                    !pinnedSurfaceCoordinator_.QueueFocusedInput(
                        L"a", widgetrail::ControllerInputOrigin::PhysicalController))
                    pinnedSurfaceCoordinator_.SetActionFeedback(
                        L"The focused pinned item is unavailable.", false);
            } else if (pinnedControllerCommand ==
                           widgetrail::pinned::ControllerCommand::Exit &&
                       !pinnedSurfaceCoordinator_.HandleFocusedSelectButton(
                           L"b", widgetrail::ControllerInputOrigin::PhysicalController)) {
                if (pinnedSurfaceCoordinator_.PumpSliderInteraction(now, true))
                    DrainPinnedSurfaceInputs();
                ReturnPinnedControllerFocusToOverlay(now);
            }
            const auto queuePinnedButton = [&](const bool pressedNow,
                                               const std::wstring_view protocolButton) {
                if (!pressedNow) return;
                if (pinnedSurfaceCoordinator_.HandleFocusedSelectButton(
                        protocolButton)) return;
                if (pinnedSurfaceCoordinator_.HandleFocusedSliderModeButton(
                        protocolButton, now)) return;
                if (!pinnedSurfaceCoordinator_.QueueFocusedInput(
                        protocolButton,
                        widgetrail::ControllerInputOrigin::PhysicalController))
                    pinnedSurfaceCoordinator_.SetActionFeedback(
                        L"The focused pinned item is unavailable.", false);
            };
            if (pinnedControllerCommand != widgetrail::pinned::ControllerCommand::Exit) {
                queuePinnedButton((pressed & XINPUT_GAMEPAD_B) != 0, L"b");
                queuePinnedButton((pressed & XINPUT_GAMEPAD_X) != 0, L"x");
                queuePinnedButton((pressed & XINPUT_GAMEPAD_Y) != 0, L"y");
                queuePinnedButton((pressed & XINPUT_GAMEPAD_LEFT_SHOULDER) != 0,
                                  L"leftBumper");
                queuePinnedButton((pressed & XINPUT_GAMEPAD_RIGHT_SHOULDER) != 0,
                                  L"rightBumper");
                queuePinnedButton(frame.leftTriggerPressed != WRAIL_OVERLAY_PLATFORM_FALSE,
                                  L"leftTrigger");
                queuePinnedButton(frame.rightTriggerPressed != WRAIL_OVERLAY_PLATFORM_FALSE,
                                  L"rightTrigger");
                queuePinnedButton((pressed & XINPUT_GAMEPAD_LEFT_THUMB) != 0,
                                  L"leftStick");
                queuePinnedButton((pressed & XINPUT_GAMEPAD_RIGHT_THUMB) != 0,
                                  L"rightStick");
                queuePinnedButton((pressed & XINPUT_GAMEPAD_START) != 0, L"menu");
            }
            InvalidateRect(window_, nullptr, FALSE);
            return;
        }

        if (pinnedControllerCommand == widgetrail::pinned::ControllerCommand::Enter &&
            state_.surface() != widgetrail::Surface::Hidden) {
            if (pinnedSurfaceCoordinator_.interactionMode() !=
                widgetrail::pinned::InteractionMode::Focusable)
                (void)pinnedSurfaceCoordinator_.SetInteractionMode(
                    widgetrail::pinned::InteractionMode::Focusable);
            if (pinnedSurfaceCoordinator_.EnterControllerFocus()) {
                if (state_.surface() == widgetrail::Surface::Widget) {
                    const auto* snapshot = InteractionSnapshotFor(
                        state_.activeWidget());
                    if (snapshot) {
                        RetirePendingFocusGroupEntryForUserIntent(
                            state_.activeWidget(), *snapshot,
                            L"pinned-controller-takeover");
                    }
                }
                (void)interactionSession_.TransitionPressedPresentation(
                    widgetrail::input::PressedInputTransition::Clear);
                lastActionWidgetId_ =
                    std::wstring(pinnedSurfaceCoordinator_.widgetId());
                lastActionMessage_ = pinnedSurfaceCoordinator_.compactMediaPresentation()
                    ? L"Compact pinned focus entered. X plays or pauses; LT and RT seek; B exits to click-through; View returns to the prior overlay location."
                    : L"Pinned focus entered. View returns to the prior overlay location; B stays in the widget.";
                lastActionExpiresAt_ = now + 4000;
                InvalidateRect(window_, nullptr, FALSE);
            }
            return;
        }

        const auto releaseButton = [&](const WORD mask, const std::wstring_view protocolButton) {
            if ((released & mask) != 0 &&
                interactionSession_.TransitionPressedPresentation(
                    widgetrail::input::PressedInputTransition::End,
                    nullptr, {}, protocolButton))
                InvalidateRect(window_, nullptr, FALSE);
        };
        releaseButton(XINPUT_GAMEPAD_A, L"a");
        releaseButton(XINPUT_GAMEPAD_B, L"b");
        releaseButton(XINPUT_GAMEPAD_X, L"x");
        releaseButton(XINPUT_GAMEPAD_LEFT_SHOULDER, L"leftBumper");
        releaseButton(XINPUT_GAMEPAD_RIGHT_SHOULDER, L"rightBumper");
        releaseButton(XINPUT_GAMEPAD_LEFT_THUMB, L"leftStick");
        releaseButton(XINPUT_GAMEPAD_RIGHT_THUMB, L"rightStick");
        releaseButton(XINPUT_GAMEPAD_START, L"menu");

        const auto stickDirection = DecodeNavigation(frame.stickNavigation);
        const auto dpadDirection = DecodeNavigation(frame.dpadNavigation);
        const bool rightStickMoving = HandleRightStickFreeScroll(frame, now);
        const auto reentryDirection = stickDirection ? stickDirection : dpadDirection;
        const bool reentryConsumed = !rightStickMoving && reentryDirection &&
            ConsumeFreeScrollReentry(*reentryDirection);
        if (!rightStickMoving && !reentryConsumed) {
            if (stickDirection) DispatchStickNavigation(*stickDirection);
            if (dpadDirection) DispatchStickNavigation(*dpadDirection);
        }
        if (interactionSession_.SliderReconcileDue(now)) {
            const widgetrail::WidgetSnapshot* currentSnapshot{};
            std::optional<widgetrail::input::WidgetInteractionAuthority>
                currentAuthority;
            if (state_.surface() == widgetrail::Surface::Widget) {
                currentSnapshot = InteractionSnapshotFor(state_.activeWidget());
                if (currentSnapshot)
                    currentAuthority = InteractionAuthority(
                        state_.activeWidget(), *currentSnapshot);
            }
            const auto tick = interactionSession_.Tick(
                currentAuthority ? &*currentAuthority : nullptr,
                interactionSession_.focusedElementId(), now);
            if (currentSnapshot && !tick.sliderDamageNodeIds.empty()) {
                    InvalidateWidgetSliderValues(
                        *currentSnapshot, tick.sliderDamageNodeIds, false);
            }
            for (const auto& request : tick.sliderActionRequests) {
                DispatchWidgetAction(
                    request.sliderDirection ==
                            widgetrail::input::NavigationDirection::Left
                        ? L"DPadLeft" : L"DPadRight",
                    widgetrail::input::NavigationEventPhase::Pressed,
                    request);
            }
        }

        if (pressed & XINPUT_GAMEPAD_A) {
            DispatchRepeatableControllerAction(L"A", now);
            // Opening a modal runs a nested message loop. When it returns, the
            // original activation frame is still on this stack and must not be
            // interpreted a second time after modal authority is retired.
            if (textEntryControllerPhase_ != TextEntryControllerPhase::None)
                return;
        }
        if (pressed & XINPUT_GAMEPAD_B) {
            DispatchRepeatableControllerAction(L"B", now);
        }
        if (pressed & XINPUT_GAMEPAD_X) {
            DispatchRepeatableControllerAction(L"X", now);
        }
        if (pressed & XINPUT_GAMEPAD_LEFT_SHOULDER) {
            DispatchRepeatableControllerAction(L"LB", now);
        }
        if (pressed & XINPUT_GAMEPAD_RIGHT_SHOULDER) {
            DispatchRepeatableControllerAction(L"RB", now);
        }
        if (pressed & XINPUT_GAMEPAD_LEFT_THUMB) {
            DispatchRepeatableControllerAction(L"LS", now);
        }
        if (pressed & XINPUT_GAMEPAD_RIGHT_THUMB) {
            DispatchRepeatableControllerAction(L"RS", now);
        }
        if (!recoveryChordDown && (pressed & XINPUT_GAMEPAD_START)) {
            DispatchRepeatableControllerAction(L"Menu", now);
        }
        if (frame.leftTriggerPressed != WRAIL_OVERLAY_PLATFORM_FALSE) {
            DispatchRepeatableControllerAction(L"LT", now);
        }
        if (frame.rightTriggerPressed != WRAIL_OVERLAY_PLATFORM_FALSE) {
            DispatchRepeatableControllerAction(L"RT", now);
        }
        if (frame.leftTriggerReleased != WRAIL_OVERLAY_PLATFORM_FALSE &&
            interactionSession_.TransitionPressedPresentation(
                widgetrail::input::PressedInputTransition::End,
                nullptr, {}, L"leftTrigger"))
            InvalidateRect(window_, nullptr, FALSE);
        if (frame.rightTriggerReleased != WRAIL_OVERLAY_PLATFORM_FALSE &&
            interactionSession_.TransitionPressedPresentation(
                widgetrail::input::PressedInputTransition::End,
                nullptr, {}, L"rightTrigger"))
            InvalidateRect(window_, nullptr, FALSE);

        // Resolve every other button and direction first. If one changes tray
        // focus, selection, overlay, reorder, or lifecycle on this same sample,
        // the central state transition cancels Y before it can win.
        if (!connected && trayYGesture_.capturing()) {
            trayYGesture_.Cancel();
            InvalidateRect(window_, nullptr, FALSE);
        }
        if (connected && (pressed & XINPUT_GAMEPAD_Y) != 0) {
            if (trayYGesture_.capturing()) {
                // Device reconnect while a canceled capture is waiting for its
                // physical release must not create a second gesture.
            } else if (state_.focusRegion() == widgetrail::FocusRegion::Tray) {
                trayYGesture_.Press(
                    state_.selectedWidget(), TrayYRestartEligible(), now);
                InvalidateRect(window_, nullptr, FALSE);
            } else {
                DispatchRepeatableControllerAction(L"Y", now);
            }
        }
        if (connected && trayYGesture_.capturing() &&
            (buttons & XINPUT_GAMEPAD_Y) != 0) {
            ApplyTrayYGestureAction(trayYGesture_.Update(
                state_.selectedWidget(), TrayYRestartEligible(), now));
            if (trayYGesture_.pendingRestart())
                InvalidateRect(window_, nullptr, FALSE);
        }
        if (connected && (released & XINPUT_GAMEPAD_Y) != 0) {
            if (trayYGesture_.capturing()) {
                ApplyTrayYGestureAction(trayYGesture_.Release(
                    state_.selectedWidget(), TrayYRestartEligible(), now));
                InvalidateRect(window_, nullptr, FALSE);
            } else {
                releaseButton(XINPUT_GAMEPAD_Y, L"y");
            }
        } else if (connected && trayYGesture_.capturing() &&
                   (buttons & XINPUT_GAMEPAD_Y) == 0) {
            // A device can reconnect after the synthetic loss edge. Retire the
            // canceled capture only after its physical Y is observed released.
            ApplyTrayYGestureAction(trayYGesture_.Release(
                state_.selectedWidget(), TrayYRestartEligible(), now));
            InvalidateRect(window_, nullptr, FALSE);
        }
        // The visible overlay already polls controller state at 60 Hz. Reuse
        // that bounded wakeup rather than owning an animation timer; settled
        // declarative content performs no paint invalidations, and this timer
        // is stopped altogether while the overlay is hidden.
        if (declarativeMotionActive_ &&
            !HasExactRefreshRetainedVisualCheckpoint() &&
            (!lastWidgetRenderResult_.backgroundSurfaceAnimationDamage ||
             !SubmitBackgroundSurfaceDamage(
                 *lastWidgetRenderResult_
                     .backgroundSurfaceAnimationDamage))) {
            pendingContentRenderPlan_.reset();
            if (declarativeRenderer_)
                declarativeRenderer_->CancelPresentationUpdatePlan();
            InvalidateRect(window_, nullptr, FALSE);
        }
    }

    const widgetrail::WidgetSnapshot* SnapshotFor(const std::wstring_view widgetId) const noexcept {
        return sessions_.Snapshot(widgetId);
    }

    const widgetrail::WidgetSnapshot* InteractionSnapshotFor(
        const std::wstring_view widgetId) const noexcept {
        const auto presentation = sessions_.Presentation(widgetId);
        if (!presentation.HasCommittedViewAuthority(
                widgetrail::WidgetCommittedViewUse::Interaction))
            return nullptr;
        const auto* source = presentation.snapshot;
        return source;
    }

    struct HostRootBackAuthority final {
        std::wstring widgetId;
        std::wstring runtimeGeneration;
        std::wstring inputScopeId;
        long long snapshotSequence{};
    };

    [[nodiscard]] std::optional<HostRootBackAuthority>
    NonCurrentHostRootBackAuthority() const {
        if (state_.surface() != widgetrail::Surface::Widget ||
            state_.focusRegion() != widgetrail::FocusRegion::Widget ||
            !IsBridgeWidget(state_.activeWidget())) {
            return std::nullopt;
        }

        const std::wstring_view widgetId = state_.activeWidget();
        const auto presentation = sessions_.Presentation(widgetId);
        if (presentation.HasCommittedViewAuthority()) {
            return std::nullopt;
        }
        const auto* descriptor = sessions_.FindDescriptor(widgetId);
        if (!descriptor) return std::nullopt;

        HostRootBackAuthority authority{
            std::wstring{widgetId}, descriptor->runtimeGeneration,
            L"host.root", 0,
        };
        if (!presentation.snapshot) return authority;

        const auto& snapshot = *presentation.snapshot;
        const std::wstring_view rootScope =
            widgetrail::input::RootInputScope(snapshot);
        if (snapshot.activeInputScopeId != rootScope &&
            !sessions_.Failure(widgetId)) {
            return std::nullopt;
        }
        authority.inputScopeId = std::wstring{rootScope};
        authority.snapshotSequence = snapshot.sequence;
        return authority;
    }

    [[nodiscard]] bool WidgetOwnsInputFocus(
        const std::wstring_view widgetId) const noexcept {
        return state_.surface() == widgetrail::Surface::Widget &&
            state_.focusRegion() == widgetrail::FocusRegion::Widget &&
            state_.activeWidget() == widgetId;
    }

    void RememberCurrentFocus(const std::wstring_view widgetId) {
        if (!WidgetOwnsInputFocus(widgetId)) return;
        const auto* snapshot = InteractionSnapshotFor(widgetId);
        if (!snapshot || interactionSession_.focusedElementId().empty()) return;
        interactionSession_.RememberFocus(widgetId, *snapshot);
    }

    void RestoreFocusForActiveSurface(const std::wstring_view widgetId) {
        if (!WidgetOwnsInputFocus(widgetId)) return;
        if (textEntryModal_.active()) {
            interactionSession_.ClearFocus();
            return;
        }
        const auto* snapshot = InteractionSnapshotFor(widgetId);
        if (snapshot)
            (void)interactionSession_.RestoreFocus(widgetId, *snapshot);
        else
            interactionSession_.ClearFocus();
        if (!interactionSession_.focusedElementId().empty())
            (void)scrollEvidenceProbe_.RecordTarget(interactionSession_.focusedElementId(), L"restore");
    }

    void ReturnPinnedControllerFocusToOverlay(const ULONGLONG now) {
        heldActionRepeat_.Reset();
        heldActionAuthority_.reset();
        (void)pinnedSurfaceCoordinator_.ExitControllerFocus();
        (void)pinnedSurfaceCoordinator_.SetInteractionMode(
            widgetrail::pinned::InteractionMode::ClickThrough);

        const bool widgetOwned =
            state_.surface() == widgetrail::Surface::Widget &&
            state_.focusRegion() == widgetrail::FocusRegion::Widget;
        if (widgetOwned)
            RestoreFocusForActiveSurface(state_.activeWidget());

        (void)SetFocus(window_);
        lastActionWidgetId_ = std::wstring(pinnedSurfaceCoordinator_.widgetId());
        lastActionMessage_ = widgetOwned
            ? L"View returned focus to the prior widget location"
            : L"View returned focus to the selected tray item";
        lastActionExpiresAt_ = now + 2400;
        InvalidateRect(window_, nullptr, FALSE);
    }

    void HandleAccessibilityActions() {
        if (textEntryModal_.active()) {
            // Native modal children own the active UIA subtree. Drain any
            // actions queued against the now-inert widget/chrome providers.
            (void)accessibilityProvider_.TakeActions();
            (void)chromeAccessibilityProvider_.TakeActions();
            return;
        }
        auto pendingActions = accessibilityProvider_.TakeActions();
        auto chromeActions = chromeAccessibilityProvider_.TakeActions();
        pendingActions.insert(pendingActions.end(),
                              std::make_move_iterator(chromeActions.begin()),
                              std::make_move_iterator(chromeActions.end()));
        for (const auto& request : pendingActions) {
            const auto publishedNode = std::find_if(
                accessibilityTree_.nodes.begin(), accessibilityTree_.nodes.end(),
                [&](const widgetrail::accessibility::Node& candidate) {
                    return candidate.domain == request.domain &&
                        candidate.id == request.nodeId;
                });
            if (request.hostAction != widgetrail::accessibility::HostAction::None) {
                if (request.widgetId != accessibilityTree_.widgetId ||
                    request.runtimeGeneration != accessibilityTree_.runtimeGeneration ||
                    request.snapshotSequence != accessibilityTree_.snapshotSequence ||
                    request.activeInputScopeId != accessibilityTree_.activeInputScopeId ||
                    publishedNode == accessibilityTree_.nodes.end() ||
                    !publishedNode->enabled ||
                    request.hostAction != publishedNode->hostAction ||
                    request.hostTargetId != publishedNode->hostTargetId) {
                    AppendDiagnostic(L"Dropped stale or invalid host accessibility action");
                    continue;
                }
                if (request.hostAction ==
                    widgetrail::accessibility::HostAction::ActivateTrayItem) {
                    if (request.kind != widgetrail::accessibility::ActionKind::Invoke &&
                        request.kind != widgetrail::accessibility::ActionKind::Focus)
                        continue;
                    if (state_.surface() == widgetrail::Surface::Widget &&
                        state_.focusRegion() == widgetrail::FocusRegion::Widget)
                        Dispatch(widgetrail::Command::SampleWidgetBack);
                    if (state_.focusRegion() != widgetrail::FocusRegion::Tray) continue;
                    if (state_.reorderMode()) Dispatch(widgetrail::Command::Cancel);
                    if (!SelectTrayWidget(request.hostTargetId)) continue;
                    if (request.kind == widgetrail::accessibility::ActionKind::Invoke)
                        Dispatch(widgetrail::Command::Activate);
                } else if (request.hostAction ==
                               widgetrail::accessibility::HostAction::ExpandSelect) {
                    if (request.kind != widgetrail::accessibility::ActionKind::Invoke ||
                        request.hostTargetId != request.nodeId ||
                        state_.surface() != widgetrail::Surface::Widget ||
                        state_.focusRegion() != widgetrail::FocusRegion::Widget)
                        continue;
                    const auto* snapshot = InteractionSnapshotFor(request.widgetId);
                    const auto* select = snapshot
                        ? widgetrail::input::FindNodeInInputScope(
                            *snapshot, request.nodeId, snapshot->activeInputScopeId)
                        : nullptr;
                    if (!select || !select->isSelect || select->isDisabled ||
                        select->isBusy) continue;
                    RetirePendingFocusGroupEntryForUserIntent(
                        request.widgetId, *snapshot, L"accessibility-expand");
                    if (interactionSession_.focusedElementId() != select->id)
                        (void)interactionSession_.MoveFocus(
                            request.widgetId, *snapshot, select->id);
                    (void)OpenFocusedSelectPopup();
                } else if (request.hostAction ==
                               widgetrail::accessibility::HostAction::CollapseSelect) {
                    if (request.kind != widgetrail::accessibility::ActionKind::Invoke ||
                        !interactionSession_.selectPopup() ||
                        interactionSession_.selectPopup()->openerElementId !=
                            request.hostTargetId)
                        continue;
                    (void)interactionSession_.CloseSelectPopup();
                    widgetAccessibilityProjection_.Clear();
                } else if (request.hostAction ==
                               widgetrail::accessibility::HostAction::CommitSelectOption) {
                    if ((request.kind != widgetrail::accessibility::ActionKind::Invoke &&
                         request.kind != widgetrail::accessibility::ActionKind::Focus) ||
                        !interactionSession_.selectPopup() ||
                        interactionSession_.selectPopup()->openerElementId !=
                            request.hostTargetId)
                        continue;
                    widgetrail::input::WidgetInteractionAuthority authority{};
                    const auto* select = CurrentSelectPopupNode(authority);
                    if (!select) continue;
                    const auto option = std::find_if(
                        select->selectOptions.begin(), select->selectOptions.end(),
                        [&](const auto& candidate) {
                            return candidate.actionId == request.actionId;
                        });
                    if (option == select->selectOptions.end()) continue;
                    const auto index = static_cast<std::size_t>(
                        std::distance(select->selectOptions.begin(), option));
                    if (!interactionSession_.HighlightSelectPopupOption(
                            authority, *select, index)) continue;
                    if (request.kind == widgetrail::accessibility::ActionKind::Invoke)
                        CommitSelectPopup(
                            widgetrail::ControllerInputOrigin::AccessibilityAutomation);
                    else {
                        widgetAccessibilityProjection_.Clear();
                        InvalidateRect(window_, nullptr, FALSE);
                    }
                } else if (request.hostAction ==
                               widgetrail::accessibility::HostAction::SelectTrayOverflow) {
                    if (request.kind != widgetrail::accessibility::ActionKind::Invoke ||
                        state_.focusRegion() != widgetrail::FocusRegion::Tray ||
                        state_.reorderMode())
                        continue;
                    if (!SelectTrayWidget(request.hostTargetId)) continue;
                } else if (request.hostAction ==
                               widgetrail::accessibility::HostAction::BackToTray ||
                           request.hostAction ==
                               widgetrail::accessibility::HostAction::BackWithinWidget) {
                    if (request.kind != widgetrail::accessibility::ActionKind::Invoke ||
                        state_.surface() != widgetrail::Surface::Widget ||
                        state_.focusRegion() != widgetrail::FocusRegion::Widget ||
                        state_.activeWidget() != request.widgetId ||
                        !IsBridgeWidget(request.widgetId))
                        continue;
                    if (request.hostAction ==
                        widgetrail::accessibility::HostAction::BackToTray) {
                        const auto hostBack = NonCurrentHostRootBackAuthority();
                        if (hostBack &&
                            request.widgetId == hostBack->widgetId &&
                            request.runtimeGeneration ==
                                hostBack->runtimeGeneration &&
                            request.snapshotSequence ==
                                hostBack->snapshotSequence &&
                            request.activeInputScopeId ==
                                hostBack->inputScopeId &&
                            request.hostTargetId == hostBack->inputScopeId) {
                            Dispatch(widgetrail::Command::SampleWidgetBack);
                            (void)SetFocus(window_);
                            InvalidateRect(window_, nullptr, FALSE);
                            continue;
                        }
                    }
                    const auto* descriptor = sessions_.FindDescriptor(request.widgetId);
                    const auto* snapshot = InteractionSnapshotFor(request.widgetId);
                    if (!descriptor || !snapshot ||
                        descriptor->runtimeGeneration != request.runtimeGeneration ||
                        snapshot->sequence != request.snapshotSequence ||
                        snapshot->activeInputScopeId != request.hostTargetId) {
                        AppendDiagnostic(L"Dropped stale Back accessibility action");
                        continue;
                    }
                    if (!widgetrail::accessibility::IsCurrentBackAction(
                            request.hostAction, request.hostTargetId, *snapshot,
                            interactionSession_.focusedElementId()))
                        continue;
                    if (request.hostAction == widgetrail::accessibility::HostAction::BackToTray) {
                        Dispatch(widgetrail::Command::SampleWidgetBack);
                    } else {
                        const auto handled = bridge_.SendControllerInput(
                            request.widgetId, L"b", L"openWidget", interactionSession_.focusedElementId(),
                            snapshot->activeInputScopeId, snapshot->sequence,
                            ++controllerSequence_,
                            static_cast<long long>(GetTickCount64() * 1000),
                            L"pressed", std::nullopt,
                            widgetrail::ControllerInputOrigin::AccessibilityAutomation,
                            descriptor->runtimeGeneration, {}, std::nullopt,
                            request.actionId);
                        if (!handled) {
                            AppendDiagnostic(
                                L"Accessibility Back transport failed for " +
                                request.widgetId);
                        } else if (*handled) {
                            RefreshAndApplyPresentation([&] {
                                RefreshWidgetSnapshot(request.widgetId);
                            });
                        }
                    }
                } else if (request.hostAction ==
                               widgetrail::accessibility::HostAction::InvokeWidgetContextAction) {
                    if ((request.kind != widgetrail::accessibility::ActionKind::Invoke &&
                         request.kind != widgetrail::accessibility::ActionKind::Focus) ||
                        !WidgetContextMenuAuthorityCurrent() ||
                        request.hostTargetId != widgetContextMenu_->sourceNodeId)
                        continue;
                    const auto layout = CurrentWidgetContextMenuLayout(
                        lastWidgetRenderResult_.responsiveSurface
                            ? lastWidgetRenderResult_.responsiveSurface->viewport.width
                            : 1600.0F,
                        lastWidgetRenderResult_.responsiveSurface
                            ? lastWidgetRenderResult_.responsiveSurface->viewport.height
                            : 1200.0F);
                    if (!layout) continue;
                    const auto item = std::find_if(
                        layout->semantics.items.begin(), layout->semantics.items.end(),
                        [&](const auto& candidate) {
                            return candidate.id == request.nodeId;
                        });
                    if (item == layout->semantics.items.end()) continue;
                    const auto index = static_cast<std::size_t>(
                        std::distance(layout->semantics.items.begin(), item));
                    if (request.kind == widgetrail::accessibility::ActionKind::Focus) {
                        widgetContextMenu_->selectedItem = index;
                    } else {
                        ActivateWidgetContextMenuItem(index);
                    }
                } else if (request.hostAction ==
                               widgetrail::accessibility::HostAction::PinTrayWidget ||
                           request.hostAction ==
                               widgetrail::accessibility::HostAction::AdjustPinnedSurface ||
                           request.hostAction ==
                               widgetrail::accessibility::HostAction::AdjustPinnedOpacity ||
                           request.hostAction ==
                               widgetrail::accessibility::HostAction::UnpinSurface) {
                    if (request.kind != widgetrail::accessibility::ActionKind::Invoke ||
                        !trayContextMenu_ ||
                        request.hostTargetId != trayContextMenu_->widgetId ||
                        request.hostTargetId != state_.selectedWidget())
                        continue;
                    const auto actions = CurrentTrayMenuActions();
                    const auto actionIndex = request.hostAction ==
                            widgetrail::accessibility::HostAction::UnpinSurface
                        ? 2U
                        : request.hostAction ==
                              widgetrail::accessibility::HostAction::AdjustPinnedOpacity
                            ? 1U : 0U;
                    if (actionIndex >= actions.size()) continue;
                    ActivateTrayContextMenuItem(actionIndex);
                } else if (request.hostAction ==
                           widgetrail::accessibility::HostAction::CloseOverlay) {
                    if (request.kind != widgetrail::accessibility::ActionKind::Invoke ||
                        state_.surface() == widgetrail::Surface::Hidden)
                        continue;
                    Dispatch(widgetrail::Command::CloseOverlay);
                } else {
                    continue;
                }
                (void)SetFocus(window_);
                InvalidateRect(window_, nullptr, FALSE);
                continue;
            }
            if (state_.surface() != widgetrail::Surface::Widget ||
                state_.activeWidget() != request.widgetId ||
                !IsBridgeWidget(request.widgetId)) {
                AppendDiagnostic(L"Dropped accessibility action outside the active widget");
                continue;
            }
            const auto* descriptor = sessions_.FindDescriptor(request.widgetId);
            const auto* snapshot = InteractionSnapshotFor(request.widgetId);
            if (!descriptor || !snapshot) {
                AppendDiagnostic(L"Dropped stale accessibility action for " + request.widgetId);
                continue;
            }
            const auto resolved = widgetrail::accessibility::ResolveActionRequest(
                request, state_.activeWidget(), descriptor->runtimeGeneration, *snapshot);
            if (!resolved) {
                AppendDiagnostic(L"Dropped stale or unavailable accessibility action for " +
                                 request.widgetId);
                continue;
            }
            RetirePendingFocusGroupEntryForUserIntent(
                request.widgetId, *snapshot, L"accessibility-action");

            if (resolved->kind == widgetrail::accessibility::ActionKind::Focus) {
                if (state_.focusRegion() == widgetrail::FocusRegion::Tray)
                    Dispatch(widgetrail::Command::Activate);
                if (state_.surface() != widgetrail::Surface::Widget ||
                    state_.focusRegion() != widgetrail::FocusRegion::Widget ||
                    state_.activeWidget() != request.widgetId) continue;
                ClearFreeScrollReentry(L"accessibility-focus");
                ObserveScrollPaginationFocusIntent(
                    request.widgetId, *snapshot,
                    interactionSession_.focusedElementId(), resolved->nodeId,
                    widgetrail::input::ScrollPaginationIntentSource::
                        Accessibility);
                const auto focus = interactionSession_.MoveFocus(
                    request.widgetId, *snapshot, resolved->nodeId);
                (void)SetFocus(window_);
                InvalidateWidgetFocusChange(
                    focus.priorFocus, focus.sliderDamageNodeIds);
                continue;
            }

            // UIA patterns are accessibility automation, not proof of a
            // physical controller gesture. Enter the widget's interactive
            // lifecycle for ordinary action routing while retaining the same
            // generation/snapshot checks and explicit origin below.
            if (state_.focusRegion() == widgetrail::FocusRegion::Tray)
                Dispatch(widgetrail::Command::Activate);
            if (state_.surface() != widgetrail::Surface::Widget ||
                state_.focusRegion() != widgetrail::FocusRegion::Widget ||
                state_.activeWidget() != request.widgetId) continue;

            if (resolved->protocolButton == L"A" &&
                OpenTextEntryModal(request.widgetId, *snapshot, resolved->nodeId))
                continue;
            if (const auto* node = widgetrail::input::FindNodeInInputScope(
                    *snapshot, resolved->nodeId, snapshot->activeInputScopeId);
                node && TryInvokeLocalWidgetPackageImport(
                    *snapshot, *node, resolved->protocolButton,
                    widgetrail::input::NavigationEventPhase::Pressed)) {
                continue;
            }

            const auto* requestedSlider = resolved->requestedValue
                ? widgetrail::input::FindNodeInInputScope(
                    *snapshot, resolved->nodeId, snapshot->activeInputScopeId)
                : nullptr;
            const bool requestedSliderAction =
                requestedSlider && requestedSlider->kind == L"slider";
            bool optimisticSliderStarted{};
            std::optional<widgetrail::input::WidgetInteractionActionRequest>
                exactSliderAction;
            if (requestedSliderAction) {
                const auto interactionAuthority = InteractionAuthority(
                    request.widgetId, *snapshot);
                if (!interactionAuthority) continue;
                auto requestOutcome = interactionSession_.RequestSliderValue(
                    *interactionAuthority, *requestedSlider,
                    *resolved->requestedValue, GetTickCount64());
                optimisticSliderStarted = requestOutcome.consumed;
                exactSliderAction = std::move(requestOutcome.actionRequest);
                if (requestOutcome.visualChanged) {
                    InvalidateWidgetSliderValues(
                        *snapshot, {requestedSlider->id}, true);
                }
                if (requestOutcome.consumed && !exactSliderAction)
                    continue;
                if (!exactSliderAction ||
                    !ValidateWidgetActionRequest(*exactSliderAction, *snapshot)) {
                    if (exactSliderAction) {
                        RejectWidgetActionRequest(
                            *exactSliderAction,
                            L"accessibility authority changed");
                    } else {
                        AppendDiagnostic(
                            L"Accessibility slider action could not capture authority for " +
                            request.widgetId);
                    }
                    continue;
                }
            }

            const auto handled = bridge_.SendControllerInput(
                exactSliderAction
                    ? std::wstring_view{exactSliderAction->widgetId}
                    : std::wstring_view{request.widgetId},
                resolved->protocolButton, L"openWidget",
                exactSliderAction
                    ? std::wstring_view{exactSliderAction->sourceElementId}
                    : std::wstring_view{resolved->nodeId},
                exactSliderAction
                    ? std::wstring_view{exactSliderAction->inputScopeId}
                    : std::wstring_view{snapshot->activeInputScopeId},
                exactSliderAction
                    ? exactSliderAction->snapshotSequence
                    : snapshot->sequence,
                ++controllerSequence_, static_cast<long long>(GetTickCount64() * 1000),
                L"pressed",
                exactSliderAction
                    ? exactSliderAction->requestedValue
                    : resolved->requestedValue,
                widgetrail::ControllerInputOrigin::AccessibilityAutomation,
                descriptor->runtimeGeneration, {}, std::nullopt,
                exactSliderAction
                    ? std::wstring_view{exactSliderAction->actionId}
                    : std::wstring_view{request.actionId});
            if (!handled) {
                if (optimisticSliderStarted && exactSliderAction &&
                    interactionSession_.CancelSliderAction(
                        *exactSliderAction,
                        GetTickCount64()).visualChanged) {
                    InvalidateWidgetSliderValues(
                        *snapshot, {requestedSlider->id}, true);
                }
                AppendDiagnostic(L"Accessibility action transport failed for " + request.widgetId);
            } else if (*handled) {
                // A requested-value acknowledgement admits work to the widget's
                // serial action queue; it does not mean OnActionAsync completed.
                // The widget's post-action invalidation owns authoritative refresh.
                if (!requestedSliderAction) {
                    RefreshAndApplyPresentation([&] {
                        RefreshWidgetSnapshot(request.widgetId);
                    });
                }
            } else if (optimisticSliderStarted && exactSliderAction &&
                       interactionSession_.CancelSliderAction(
                           *exactSliderAction,
                           GetTickCount64()).visualChanged) {
                InvalidateWidgetSliderValues(
                    *snapshot, {requestedSlider->id}, true);
            }
        }
        accessibilityProvider_.RaisePendingEvents();
        chromeAccessibilityProvider_.RaisePendingEvents();
    }

    void ClearAccessibilityTree() noexcept {
        accessibilityTree_ = {};
        widgetAccessibilityTree_ = {};
        accessibilityProvider_.Clear();
        chromeAccessibilityProvider_.Clear();
        accessibilityProjection_.Clear();
        widgetAccessibilityProjection_.Clear();
    }

    [[nodiscard]] widgetrail::accessibility::Tree PartitionAccessibilityTree(
        const bool chrome) const {
        if (!compositionChromeSession_) {
            if (!chrome) return accessibilityTree_;
            widgetrail::accessibility::Tree empty;
            empty.widgetId = accessibilityTree_.widgetId;
            empty.runtimeGeneration = accessibilityTree_.runtimeGeneration;
            empty.snapshotSequence = accessibilityTree_.snapshotSequence;
            empty.activeInputScopeId = accessibilityTree_.activeInputScopeId;
            empty.name = accessibilityTree_.name;
            return empty;
        }
        const auto& session = *compositionChromeSession_;
        const float guideThreshold = state_.surface() == widgetrail::Surface::Widget
            ? -std::numeric_limits<float>::infinity()
            : std::numeric_limits<float>::infinity();
        auto partition = widgetrail::accessibility::PartitionForFixedChrome(
            accessibilityTree_, guideThreshold, 0.0F, 0.0F);
        for (auto& node : partition.chrome.nodes) {
            const RECT& childBounds =
                node.domain == widgetrail::accessibility::ElementDomain::Tray
                ? session.trayClientBounds
                : session.guideClientBounds;
            node.bounds.x += static_cast<float>(childBounds.left) /
                session.pixelsPerDip;
            node.bounds.y += static_cast<float>(childBounds.top) /
                session.pixelsPerDip;
        }
        return chrome ? std::move(partition.chrome) : std::move(partition.content);
    }

    bool PublishAccessibilityTree(
        const double pixelsPerDip,
        const bool forceDuringPlacement = false) {
        if (textEntryModal_.active()) {
            ClearAccessibilityTree();
            return false;
        }
        if (compositionPlacementInProgress_ && !forceDuringPlacement)
            return false;
        POINT origin{};
        RECT client{};
        if (!ClientToScreen(window_, &origin) || !GetClientRect(window_, &client)) {
            ClearAccessibilityTree();
            return false;
        }
        double scaleX = pixelsPerDip;
        double scaleY = pixelsPerDip;
        double visualOffsetX = 0.0;
        double visualOffsetY = 0.0;
        double presentedWidth = static_cast<double>(client.right - client.left);
        double presentedHeight = static_cast<double>(client.bottom - client.top);
        const auto childSpaces = CurrentCompositionChildCoordinates();
        if (childSpaces) {
            const auto& motion = childSpaces->content;
            const auto& contentPlacement = presentationTransaction_.contentPlacement();
            if (!contentPlacement) {
                ClearAccessibilityTree();
                return false;
            }
            scaleX *= static_cast<double>(motion.scaleX);
            scaleY *= static_cast<double>(motion.scaleY);
            visualOffsetX = static_cast<double>(motion.offsetX);
            visualOffsetY = static_cast<double>(motion.offsetY);
            presentedWidth = static_cast<double>(
                contentPlacement->width) * motion.scaleX;
            presentedHeight = static_cast<double>(
                contentPlacement->height) * motion.scaleY;
        }
        accessibilityProvider_.Publish(
            PartitionAccessibilityTree(false),
            {
                static_cast<double>(origin.x),
                static_cast<double>(origin.y),
                pixelsPerDip,
                presentedWidth,
                presentedHeight,
                scaleX,
                scaleY,
                visualOffsetX,
                visualOffsetY,
                0.0,
                0.0,
                false,
            });
        if (compositionChromeSession_ && chromeWindow_) {
            POINT chromeOrigin{};
            RECT chromeClient{};
            if (!ClientToScreen(chromeWindow_, &chromeOrigin) ||
                !GetClientRect(chromeWindow_, &chromeClient)) return false;
            const auto& session = *compositionChromeSession_;
            chromeAccessibilityProvider_.Publish(
                PartitionAccessibilityTree(true),
                {
                    static_cast<double>(chromeOrigin.x),
                    static_cast<double>(chromeOrigin.y),
                    session.pixelsPerDip,
                    static_cast<double>(chromeClient.right - chromeClient.left),
                    static_cast<double>(chromeClient.bottom - chromeClient.top),
                    session.pixelsPerDip,
                    session.pixelsPerDip,
                });
        }
        return true;
    }

    bool CommitActionFailureFixedChrome() {
        if (!window_ || !accessibilityActive_ ||
            state_.surface() == widgetrail::Surface::Hidden) {
            return false;
        }

        if (compositionSurface_.available()) {
            pendingActionFailureAccessibilityProjection_.reset();
            if (!compositionChromeSession_) return false;
            const auto trayLayout = CurrentCompositionTrayLayout();
            if (!trayLayout) return false;
            const auto& chromeSession = *compositionChromeSession_;
            const auto guideKey = CurrentGuidePaintKey(
                chromeSession.guideWidth,
                chromeSession.guideHeight,
                chromeSession.dpi);
            CompositionFrameSet frames;
            const CompositionLayerGeometry guideGeometry{
                chromeSession.guideWidth,
                chromeSession.guideHeight,
            };
            if (!RenderCompositionLayer(
                    chromeSession.guideWidth,
                    chromeSession.guideHeight,
                    chromeSession.dpi,
                    widgetrail::OverlayCompositionSurface::Layer::Guide,
                    CompositionPaintLayer::Guide,
                    guideGeometry,
                    nullptr,
                    frames,
                    &*trayLayout,
                    &chromeSession.guideBounds,
                    true)) {
                pendingActionFailureAccessibilityProjection_.reset();
                DisableCompositionFallback(L"action-feedback guide draw failed");
                return false;
            }
            std::vector<widgetrail::OverlayCompositionSurface::Frame*> framePointers;
            framePointers.reserve(frames.frames.size());
            for (auto& frame : frames.frames) framePointers.push_back(&frame);
            widgetrail::OverlayCompositionSurface::CommitTiming timing;
            const HRESULT result = compositionSurface_.CommitFrames(
                framePointers, false, timing, nullptr);
            if (FAILED(result)) {
                pendingActionFailureAccessibilityProjection_.reset();
                DisableCompositionFallback(
                    L"action-feedback guide commit failed hresult=" +
                    std::to_wstring(static_cast<unsigned long>(result)));
                return false;
            }
            retainedGuidePaintKey_ = guideKey;
            if (!pendingActionFailureAccessibilityProjection_) return false;
        } else {
            InvalidateRect(window_, nullptr, FALSE);
            UpdateWindow(window_);
        }

        const bool dashboardStatus =
            state_.surface() == widgetrail::Surface::Dashboard ||
            state_.focusRegion() == widgetrail::FocusRegion::Tray;
        const auto expectedStatus = dashboardStatus
            ? DashboardStatus()
            : OpenWidgetStatus();
        const std::wstring_view statusId =
            state_.surface() == widgetrail::Surface::Dashboard
            ? L"host.dashboard.status"
            : L"host.open.status";
        const auto semanticStatus = std::find_if(
            accessibilityTree_.nodes.begin(), accessibilityTree_.nodes.end(),
            [&](const widgetrail::accessibility::Node& node) {
                return node.domain ==
                           widgetrail::accessibility::ElementDomain::HostShell &&
                       node.id == statusId;
            });
        AppendDiagnostic(
            L"Action failure accessibility projection stage=semantic-committed" +
            std::wstring{L" expected="} +
            (expectedStatus ? *expectedStatus : L"<absent>") +
            L" node=" +
            (semanticStatus == accessibilityTree_.nodes.end()
                ? L"absent"
                : semanticStatus->id + L" role=" +
                    std::to_wstring(static_cast<int>(semanticStatus->role)) +
                    L" name=" + semanticStatus->name +
                    L" live=" +
                    std::to_wstring(static_cast<int>(semanticStatus->liveSetting)) +
                    L" bounds=" + std::to_wstring(semanticStatus->bounds.x) + L"," +
                    std::to_wstring(semanticStatus->bounds.y) + L"," +
                    std::to_wstring(semanticStatus->bounds.width) + L"," +
                    std::to_wstring(semanticStatus->bounds.height)));
        if (!expectedStatus || semanticStatus == accessibilityTree_.nodes.end() ||
            semanticStatus->name != *expectedStatus ||
            semanticStatus->role != widgetrail::accessibility::Role::Status ||
            semanticStatus->liveSetting !=
                widgetrail::accessibility::LiveSetting::Polite) {
            return false;
        }

        if (compositionSurface_.available()) {
            auto projection = std::move(*pendingActionFailureAccessibilityProjection_);
            pendingActionFailureAccessibilityProjection_.reset();
            if (!PublishAccessibilityTree(projection.pixelsPerDip)) return false;
            accessibilityProjection_.Published(std::move(projection));
        } else {
            RECT client{};
            const UINT dpi = std::max(1U, GetDpiForWindow(window_));
            const float interfaceScale = appearanceState_.current()
                ? static_cast<float>(appearanceState_.current()->interfaceScale)
                : 1.0F;
            const auto metrics = GetClientRect(window_, &client)
                ? widgetrail::ComputeOverlayRenderMetrics(
                    client.right - client.left, client.bottom - client.top,
                    dpi, interfaceScale)
                : std::nullopt;
            if (!metrics ||
                !PublishAccessibilityTree(metrics->physicalPixelsPerDip)) {
                return false;
            }
        }
        return true;
    }

    void PublishNonCurrentHostBackAccessibility() {
        if (!accessibilityActive_ || openWidgetAccessibility_.title.empty()) return;
        const auto hostBack = NonCurrentHostRootBackAuthority();
        if (!hostBack) return;

        widgetrail::accessibility::Tree hostTree;
        hostTree.widgetId = hostBack->widgetId;
        hostTree.runtimeGeneration = hostBack->runtimeGeneration;
        hostTree.snapshotSequence = hostBack->snapshotSequence;
        hostTree.activeInputScopeId = hostBack->inputScopeId;
        auto semantics = openWidgetAccessibility_;
        semantics.backAction = widgetrail::accessibility::HostAction::BackToTray;
        semantics.backTargetId = hostBack->inputScopeId;
        accessibilityTree_ = widgetrail::accessibility::BuildOpenWidgetTree(
            std::move(hostTree), {}, {}, 0, false, semantics);

        RECT client{};
        const UINT dpi = std::max(1U, GetDpiForWindow(window_));
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto metrics = GetClientRect(window_, &client)
            ? widgetrail::ComputeOverlayRenderMetrics(
                client.right - client.left, client.bottom - client.top,
                dpi, interfaceScale)
            : std::nullopt;
        if (!metrics || !PublishAccessibilityTree(metrics->physicalPixelsPerDip)) {
            ClearAccessibilityTree();
        }
    }

    void PublishTrayAccessibility(
        const widgetrail::shell::TrayLayout& layout,
        const float width,
        const float height,
        const widgetrail::accessibility::DashboardSemantics* dashboard = nullptr,
        const bool deferProviderPublication = false) {
        if (!accessibilityActive_) return;
        if (textEntryModal_.active()) {
            ClearAccessibilityTree();
            return;
        }
        std::vector<widgetrail::accessibility::TrayItem> items;
        items.reserve(state_.order().size());
        for (const auto& widgetId : state_.order()) {
            const std::wstring name{DisplayWidgetName(widgetId)};
            items.push_back({widgetId, name});
        }
        const auto menuLayout = CurrentTrayContextMenuLayout(
            layout, CurrentTrayViewportWidthDip(width));
        const auto widgetMenuLayout = CurrentWidgetContextMenuLayout(width, height);
        const widgetrail::accessibility::TrayContextMenuSemantics menuSemantics =
            widgetMenuLayout ? widgetMenuLayout->semantics
            : menuLayout ? menuLayout->semantics
            : widgetrail::accessibility::TrayContextMenuSemantics{};
        if (state_.surface() == widgetrail::Surface::Widget) {
            openWidgetAccessibility_.contextMenu = menuSemantics;
            const bool currentWidgetSemantics =
                InteractionSnapshotFor(state_.activeWidget()) &&
                widgetAccessibilityTree_.widgetId == state_.activeWidget();
            const auto hostBack = NonCurrentHostRootBackAuthority();
            if ((!currentWidgetSemantics &&
                 state_.focusRegion() != widgetrail::FocusRegion::Tray &&
                 !hostBack) ||
                openWidgetAccessibility_.title.empty())
                return;
            widgetrail::accessibility::Tree semanticTree = currentWidgetSemantics
                ? widgetAccessibilityTree_
                : widgetrail::accessibility::Tree{};
            if (!currentWidgetSemantics) {
                semanticTree.widgetId = state_.activeWidget();
                if (hostBack) {
                    semanticTree.runtimeGeneration = hostBack->runtimeGeneration;
                    semanticTree.snapshotSequence = hostBack->snapshotSequence;
                    semanticTree.activeInputScopeId = hostBack->inputScopeId;
                } else {
                    semanticTree.activeInputScopeId = L"host.tray";
                    if (const auto* descriptor = sessions_.FindDescriptor(
                            state_.activeWidget())) {
                        semanticTree.runtimeGeneration = descriptor->runtimeGeneration;
                    }
                }
            }
            const auto semanticRevision =
                widgetrail::accessibility::ComputeOpenWidgetSemanticRevision(
                    items, openWidgetAccessibility_);
            if (deferProviderPublication) {
                accessibilityTree_ = widgetrail::accessibility::BuildOpenWidgetTree(
                    semanticTree, items, layout, state_.selectedSlot(),
                    state_.focusRegion() == widgetrail::FocusRegion::Tray,
                    openWidgetAccessibility_);
            }
            const auto& projectionTree = deferProviderPublication
                ? accessibilityTree_ : semanticTree;
            const auto policy = appearanceState_.current()
                ? CurrentAccessibilityPolicy()
                : widgetrail::NativeAccessibilityPolicy{};
            widgetrail::accessibility::ProjectionKey key{
                projectionTree.widgetId,
                projectionTree.runtimeGeneration,
                projectionTree.activeInputScopeId,
                state_.focusRegion() == widgetrail::FocusRegion::Tray
                    ? std::wstring{state_.selectedWidget()}
                    : interactionSession_.focusedElementId(),
                projectionTree.snapshotSequence,
                widgetAccessibilityRevision_,
                appearanceState_.current() ? appearanceState_.current()->revision : 0,
                layout.stripBounds.x,
                layout.stripBounds.y,
                layout.stripBounds.width,
                layout.stripBounds.height,
                width,
                height,
                static_cast<float>(std::max(1U, GetDpiForWindow(window_))) / 96.0F,
                policy.textScale,
                policy.minimumFontWeight,
                policy.reducedMotion,
                policy.reducedTransparency,
            };
            key.hostSemanticRevision = semanticRevision;
            if (!deferProviderPublication &&
                !accessibilityProjection_.ShouldCollect(key)) return;
            if (deferProviderPublication) {
                pendingActionFailureAccessibilityProjection_ = std::move(key);
                return;
            }
            accessibilityTree_ = widgetrail::accessibility::BuildOpenWidgetTree(
                std::move(semanticTree), items, layout, state_.selectedSlot(),
                state_.focusRegion() == widgetrail::FocusRegion::Tray,
                openWidgetAccessibility_);
            if (PublishAccessibilityTree(key.pixelsPerDip))
                accessibilityProjection_.Published(std::move(key));
            return;
        }
        if (state_.focusRegion() != widgetrail::FocusRegion::Tray) return;
        auto dashboardSemantics = dashboard
            ? *dashboard : widgetrail::accessibility::DashboardSemantics{};
        dashboardSemantics.contextMenu = menuSemantics;
        const auto* effectiveDashboard = dashboard || menuLayout
            ? &dashboardSemantics : nullptr;
        const auto semanticRevision =
            widgetrail::accessibility::ComputeTraySemanticRevision(
                items, effectiveDashboard);
        const auto policy = appearanceState_.current()
            ? CurrentAccessibilityPolicy()
            : widgetrail::NativeAccessibilityPolicy{};
        const widgetrail::accessibility::ProjectionKey key{
            L"host.tray",
            L"host",
            L"host.tray",
            std::wstring{state_.selectedWidget()},
            semanticRevision,
            0,
            appearanceState_.current() ? appearanceState_.current()->revision : 0,
            layout.stripBounds.x,
            layout.stripBounds.y,
            layout.stripBounds.width,
            layout.stripBounds.height,
            width,
            height,
            static_cast<float>(std::max(1U, GetDpiForWindow(window_))) / 96.0F,
            policy.textScale,
            policy.minimumFontWeight,
            policy.reducedMotion,
            policy.reducedTransparency,
        };
        if (!deferProviderPublication &&
            !accessibilityProjection_.ShouldCollect(key)) return;
        accessibilityTree_ = widgetrail::accessibility::BuildTrayTree(
            items, layout, state_.selectedSlot(), ++hostAccessibilitySequence_,
            effectiveDashboard);
        if (deferProviderPublication) {
            pendingActionFailureAccessibilityProjection_ = key;
            return;
        }
        if (PublishAccessibilityTree(key.pixelsPerDip))
            accessibilityProjection_.Published(key);
    }

    bool ReconcileResponsiveFocusPersistence(
        const std::wstring_view widget,
        const widgetrail::WidgetSnapshot& snapshot,
        const widgetrail::RenderResult& renderResult) {
        if (!window_ || state_.surface() != widgetrail::Surface::Widget ||
            state_.focusRegion() != widgetrail::FocusRegion::Widget ||
            state_.activeWidget() != widget || !renderResult.succeeded ||
            !renderResult.responsiveSurface) {
            return false;
        }
        if (interactionSession_.focusedElementId().empty()) return false;

        const auto target = widgetrail::input::ResolveResponsiveFocusPersistenceTarget(
            snapshot,
            interactionSession_.focusedElementId(),
            snapshot.activeInputScopeId,
            renderResult.responsiveSurface->mode ==
                widgetrail::ResponsiveSurfaceMode::Compact);
        if (!target || *target == interactionSession_.focusedElementId()) return false;

        ClearFreeScrollReentry(L"responsive-view-changed");
        const auto focus = interactionSession_.MoveFocus(
            widget, snapshot, *target);
        (void)scrollEvidenceProbe_.RecordTarget(*target, L"responsive");
        InvalidateWidgetFocusChange(focus.priorFocus, focus.sliderDamageNodeIds);
        return true;
    }

    [[nodiscard]] std::optional<widgetrail::input::WidgetInteractionAuthority>
    InteractionAuthority(
        const std::wstring_view widgetId,
        const widgetrail::WidgetSnapshot& snapshot,
        const bool retainedRefresh = false) {
        const auto* descriptor = sessions_.FindDescriptor(widgetId);
        if (!descriptor) return std::nullopt;
        return widgetrail::input::WidgetInteractionAuthority{
            widgetId,
            &snapshot,
            descriptor->runtimeGeneration,
            descriptor->presentationGeneration,
            retainedRefresh,
        };
    }

    void RetirePendingFocusGroupEntryForUserIntent(
        const std::wstring_view widgetId,
        const widgetrail::WidgetSnapshot& snapshot,
        const std::wstring_view reason) {
        const auto authority = InteractionAuthority(widgetId, snapshot);
        if (!authority ||
            !interactionSession_.RetireFocusGroupEntryRequest(*authority)) return;
        AppendDiagnostic(
            L"Focus-group entry request widget=" + std::wstring{widgetId} +
            L" state=retired reason=" + std::wstring{reason});
    }

    void FlushCurrentSliderAction(
        const widgetrail::input::WidgetInteractionAuthority& authority,
        const widgetrail::WidgetNode& node,
        const std::uint64_t now) {
        const auto flush = interactionSession_.TakePendingSliderAction(
            authority, node, now, true);
        if (!flush.actionRequest) return;
        DispatchWidgetAction(
            flush.actionRequest->sliderDirection ==
                    widgetrail::input::NavigationDirection::Left
                ? L"DPadLeft" : L"DPadRight",
            widgetrail::input::NavigationEventPhase::Pressed,
            flush.actionRequest);
    }

    [[nodiscard]] const widgetrail::WidgetNode* ValidateWidgetActionRequest(
        const widgetrail::input::WidgetInteractionActionRequest& request,
        const widgetrail::WidgetSnapshot& snapshot) {
        if (state_.surface() != widgetrail::Surface::Widget ||
            state_.focusRegion() != widgetrail::FocusRegion::Widget ||
            state_.activeWidget() != request.widgetId ||
            interactionSession_.focusedElementId() != request.sourceElementId ||
            !request.requestedValue) {
            return nullptr;
        }
        const auto* descriptor = sessions_.FindDescriptor(request.widgetId);
        const auto* current = InteractionSnapshotFor(request.widgetId);
        if (!descriptor || !current || current != &snapshot ||
            descriptor->runtimeGeneration != request.runtimeGeneration ||
            descriptor->presentationGeneration != request.presentationGeneration ||
            snapshot.instanceId != request.widgetInstanceId ||
            snapshot.activeInputScopeId != request.inputScopeId ||
            snapshot.sequence != request.snapshotSequence) {
            return nullptr;
        }
        const auto* source = widgetrail::input::FindNodeInInputScope(
            snapshot, request.sourceElementId, request.inputScopeId);
        return source && source->kind == L"slider" &&
                source->valueChangedActionId == request.actionId
            ? source
            : nullptr;
    }

    void RejectWidgetActionRequest(
        const widgetrail::input::WidgetInteractionActionRequest& request,
        const std::wstring_view reason) {
        const auto rollback = interactionSession_.CancelSliderAction(
            request, GetTickCount64());
        const auto* snapshot = InteractionSnapshotFor(request.widgetId);
        if (rollback.visualChanged && snapshot &&
            snapshot->instanceId == request.widgetInstanceId &&
            snapshot->activeInputScopeId == request.inputScopeId &&
            snapshot->sequence == request.snapshotSequence) {
            const auto* source = widgetrail::input::FindNodeInInputScope(
                *snapshot, request.sourceElementId, request.inputScopeId);
            if (source && source->kind == L"slider" &&
                source->valueChangedActionId == request.actionId) {
                InvalidateWidgetSliderValues(
                    *snapshot, {request.sourceElementId}, true);
            }
        }
        lastActionWidgetId_ = request.widgetId;
        lastActionMessage_ = std::wstring(DisplayWidgetName(request.widgetId)) +
            L" rejected slider action: " + std::wstring{reason};
        lastActionExpiresAt_ = GetTickCount64() + 1800;
        AppendDiagnostic(lastActionMessage_);
    }

    void InvalidateWidgetSliderValues(
        const widgetrail::WidgetSnapshot& snapshot,
        const std::vector<std::wstring>& nodeIds,
        const bool paintImmediately) {
        if (!window_ || nodeIds.empty()) return;
        const auto full = [&] {
            pendingContentRenderPlan_.reset();
            if (declarativeRenderer_)
                declarativeRenderer_->CancelPresentationUpdatePlan();
            InvalidateRect(window_, nullptr, FALSE);
            if (paintImmediately) UpdateWindow(window_);
        };
        const auto* current = InteractionSnapshotFor(state_.activeWidget());
        if (!declarativeRenderer_ || !compositionSurface_.available() ||
            state_.surface() != widgetrail::Surface::Widget ||
            state_.focusRegion() != widgetrail::FocusRegion::Widget || !current ||
            current->instanceId != snapshot.instanceId ||
            current->sequence != snapshot.sequence ||
            declarativeMotionActive_ ||
            overlayTransition_.active() ||
            presentationTransaction_.extentTransitionActive() ||
            compositionPlacementInProgress_ || pendingWidgetPresentationImpact_ ||
            pendingContentRenderPlan_) {
            full();
            return;
        }
        RECT pendingPaint{};
        RECT client{};
        if (GetUpdateRect(window_, &pendingPaint, FALSE) != FALSE ||
            !lastWidgetRenderResult_.succeeded || !GetClientRect(window_, &client)) {
            full();
            return;
        }
        const UINT dpi = std::max(1U, GetDpiForWindow(window_));
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto metrics = widgetrail::ComputeOverlayRenderMetrics(
            client.right - client.left, client.bottom - client.top,
            dpi, interfaceScale);
        const auto geometry = metrics
            ? widgetrail::ComputePanelLocalSurfaceGeometry(
                metrics->viewportWidthDip, metrics->viewportHeightDip)
            : std::nullopt;
        if (!metrics || !geometry || metrics->physicalPixelsPerDip <= 0.0F) {
            full();
            return;
        }
        const widgetrail::WidgetPresentationImpact impact{
            snapshot.sequence,
            snapshot.sequence,
            widgetrail::WidgetPresentationEffect::Paint |
                widgetrail::WidgetPresentationEffect::Accessibility,
            nodeIds,
        };
        const auto plan = declarativeRenderer_->PlanPresentationUpdate(
            snapshot, impact,
            {
                geometry->widgetViewportX,
                geometry->widgetViewportY,
                geometry->widgetViewportWidth,
                geometry->widgetViewportHeight,
            });
        if (!plan || plan->work != widgetrail::IncrementalPresentationWork::PaintOnly) {
            full();
            return;
        }
        if (!SubmitWidgetContentDamage(
                *plan, metrics->physicalPixelsPerDip, client, paintImmediately)) {
            full();
            return;
        }
    }

    void PublishScrollPaginationOutcome(
        const widgetrail::input::ScrollPaginationSessionOutcome& outcome) {
        for (const auto& diagnostic : outcome.diagnostics) {
            AppendDiagnostic(
                widgetrail::input::FormatScrollPaginationDiagnostic(diagnostic));
        }
        if (outcome.dispatchReady) {
            (void)PostMessageW(
                window_, kScrollPaginationPrefetchMessage, 0, 0);
        }
    }

    void ObserveScrollPaginationIntent(
        const std::wstring_view widgetId,
        const widgetrail::WidgetSnapshot& snapshot,
        const std::wstring_view scrollId,
        const widgetrail::declarative::ScrollAxis axis,
        const widgetrail::input::ScrollPaginationEdge edge,
        const widgetrail::input::ScrollPaginationIntentSource source) {
        const auto authority = InteractionAuthority(widgetId, snapshot);
        if (!authority) return;
        PublishScrollPaginationOutcome(
            interactionSession_.ObserveScrollPaginationIntent(
                *authority,
                scrollId, axis, edge, source, GetTickCount64()));
    }

    void ObserveScrollPaginationFocusIntent(
        const std::wstring_view widgetId,
        const widgetrail::WidgetSnapshot& snapshot,
        const std::wstring_view priorFocus,
        const std::wstring_view nextFocus,
        const widgetrail::input::ScrollPaginationIntentSource source) {
        const auto authority = InteractionAuthority(widgetId, snapshot);
        if (!authority) return;
        PublishScrollPaginationOutcome(
            interactionSession_.ObserveScrollPaginationFocusIntent(
                *authority,
                lastWidgetRenderResult_, priorFocus, nextFocus, source,
                GetTickCount64()));
    }

    void ClearScrollPaginationPrefetch(
        const std::wstring_view reason,
        const std::wstring_view widgetId = {}) {
        PublishScrollPaginationOutcome(
            interactionSession_.RetireScrollPagination(widgetId, reason));
    }

    void ReconcileScrollPaginationPrefetch(
        const std::wstring_view widgetId,
        const widgetrail::WidgetSnapshot& snapshot,
        const widgetrail::WidgetDescriptor& descriptor,
        const widgetrail::RenderResult& renderResult) {
        PublishScrollPaginationOutcome(
            interactionSession_.ReconcileScrollPagination(
                {
                    widgetId,
                    &snapshot,
                    descriptor.runtimeGeneration,
                    descriptor.presentationGeneration,
                    false,
                },
                renderResult,
                GetTickCount64()));
    }

    [[nodiscard]] bool IsCurrentScrollPaginationRequest(
        const widgetrail::input::ScrollPaginationPrefetchRequest& request,
        const widgetrail::WidgetSnapshot& snapshot,
        const widgetrail::WidgetDescriptor& descriptor) const {
        if (request.widgetId != state_.activeWidget() ||
            request.widgetInstanceId != snapshot.instanceId ||
            request.runtimeGeneration != descriptor.runtimeGeneration ||
            request.presentationGeneration != descriptor.presentationGeneration ||
            request.inputScopeId != snapshot.activeInputScopeId) {
            return false;
        }
        const auto actions = widgetrail::input::FindScrollPaginationActions(
            snapshot.root, snapshot.activeInputScopeId, lastWidgetRenderResult_);
        return std::ranges::any_of(actions, [&](const auto& action) {
            return action.scrollId == request.action.scrollId &&
                action.actionId == request.action.actionId &&
                action.sourceElementId == request.action.sourceElementId &&
                action.edge == request.action.edge &&
                action.edgeKey == request.action.edgeKey;
        });
    }

    void DispatchQueuedScrollPaginationPrefetch() {
        constexpr std::size_t maximumDispatches = 16;
        for (std::size_t index = 0; index < maximumDispatches; ++index) {
            if (state_.surface() != widgetrail::Surface::Widget) {
                ClearScrollPaginationPrefetch(L"stale-route");
                return;
            }
            const std::wstring widgetId{state_.activeWidget()};
            const auto* snapshot = InteractionSnapshotFor(widgetId);
            const auto* descriptor = sessions_.FindDescriptor(widgetId);
            if (!snapshot || !descriptor || sessions_.Failure(widgetId)) {
                ClearScrollPaginationPrefetch(L"stale-route");
                return;
            }
            auto [request, session] =
                interactionSession_.AcquireScrollPaginationDispatch(
                    {
                        widgetId,
                        snapshot,
                        descriptor->runtimeGeneration,
                        descriptor->presentationGeneration,
                        false,
                    },
                    lastWidgetRenderResult_,
                    GetTickCount64());
            PublishScrollPaginationOutcome(session);
            if (!request) return;

            widgetrail::input::ScrollPaginationDispatchOutcome dispatch;
            dispatch.request = *request;
            dispatch.now = GetTickCount64();
            if (!IsCurrentScrollPaginationRequest(
                    *request, *snapshot, *descriptor)) {
                dispatch.disposition =
                    widgetrail::input::ScrollPaginationDispatchDisposition::
                        StaleAuthority;
                dispatch.safeDiagnostic = L"stale-route";
            } else {
                const auto handled = bridge_.SendAction(
                    request->widgetId,
                    request->action.actionId,
                    request->action.sourceElementId,
                    request->inputScopeId);
                dispatch.disposition = !handled
                    ? widgetrail::input::ScrollPaginationDispatchDisposition::
                        TransportFailure
                    : *handled
                        ? widgetrail::input::ScrollPaginationDispatchDisposition::
                            Admitted
                        : widgetrail::input::ScrollPaginationDispatchDisposition::
                            NotHandled;
                if (!handled) {
                    dispatch.safeDiagnostic = bridge_.lastError();
                } else if (!*handled) {
                    dispatch.safeDiagnostic = L"action-not-handled";
                }
            }
            PublishScrollPaginationOutcome(
                interactionSession_.CompleteScrollPaginationDispatch(dispatch));
            // Acknowledgement is queue admission, not page completion. The
            // widget's existing invalidation owns the authoritative refresh.
        }
    }

    void ObserveScrollPaginationFailure(
        const widgetrail::WidgetActionFailure& failure) {
        PublishScrollPaginationOutcome(
            interactionSession_.ObserveScrollPaginationFailure(
                failure, GetTickCount64()));
    }
    void HandleWidgetDirection(
        const widgetrail::input::NavigationDirection direction,
        const widgetrail::input::NavigationEventPhase phase,
        const bool repeatedCanNavigate) {
        if (textEntryModal_.active()) {
            const auto button = direction == widgetrail::input::NavigationDirection::Left ? L"DPadLeft" :
                direction == widgetrail::input::NavigationDirection::Right ? L"DPadRight" :
                direction == widgetrail::input::NavigationDirection::Up ? L"DPadUp" : L"DPadDown";
            textEntryModal_.HandleController(button);
            return;
        }
        if (state_.surface() != widgetrail::Surface::Widget ||
            state_.focusRegion() != widgetrail::FocusRegion::Widget) return;
        const std::wstring_view widgetId = state_.activeWidget();
        const auto* snapshot = InteractionSnapshotFor(widgetId);
        if (!snapshot) return;
        const auto focusGroupEntryAuthority =
            InteractionAuthority(widgetId, *snapshot);
        if (focusGroupEntryAuthority &&
            interactionSession_.FocusGroupEntryRequestPending(
                *focusGroupEntryAuthority)) {
            if (phase != widgetrail::input::NavigationEventPhase::Pressed)
                return;
            switch (direction) {
            case widgetrail::input::NavigationDirection::Left:
                MoveWidgetFocus(L"left", phase);
                break;
            case widgetrail::input::NavigationDirection::Right:
                MoveWidgetFocus(L"right", phase);
                break;
            case widgetrail::input::NavigationDirection::Up:
                MoveWidgetFocus(L"up", phase);
                break;
            case widgetrail::input::NavigationDirection::Down:
                MoveWidgetFocus(L"down", phase);
                break;
            default:
                break;
            }
            return;
        }
        const auto visible = widgetrail::input::ResolveVisibleFocusTarget(
            interactionSession_.focusedElementId(), snapshot->activeInputScopeId, lastWidgetRenderResult_);
        if (!visible) return;
        if (*visible != interactionSession_.focusedElementId()) {
            const auto focus = interactionSession_.MoveFocus(
                widgetId, *snapshot, *visible, true, false);
            InvalidateWidgetFocusChange(
                focus.priorFocus, focus.sliderDamageNodeIds);
        }
        const auto* focused = widgetrail::input::FindNodeInInputScope(
            *snapshot, interactionSession_.focusedElementId(), snapshot->activeInputScopeId);
        if (!focused) return;
        const auto interactionAuthority = InteractionAuthority(widgetId, *snapshot);
        if (interactionSession_.selectPopup()) {
            if (!interactionAuthority || !focused->isSelect ||
                !interactionSession_.SelectPopupCurrent(
                    *interactionAuthority, *focused)) {
                (void)interactionSession_.CloseSelectPopup();
            } else if (interactionSession_.MoveSelectPopup(
                           *interactionAuthority, *focused, direction)) {
                widgetAccessibilityProjection_.Clear();
                InvalidateRect(window_, nullptr, FALSE);
            }
            return;
        }
        const bool activationRequired =
            focused->sliderInteractionMode == L"activateToAdjust";
        const bool adjustmentActive = activationRequired && interactionAuthority &&
            interactionSession_.SliderAdjustmentModeActive(
                *interactionAuthority, *focused);
        const auto route = widgetrail::input::RouteFocusedDirection(
            focused->kind, focused->isDisabled, focused->isBusy,
            activationRequired, adjustmentActive, direction);
        if (route == widgetrail::input::FocusedDirectionRoute::Consume) return;
        if (route == widgetrail::input::FocusedDirectionRoute::SliderAdjustment) {
            if (!interactionAuthority) return;
            const auto adjustment = interactionSession_.AdjustSlider(
                *interactionAuthority, *focused, direction, GetTickCount64());
            if (adjustment.visualChanged) {
                // The host-owned thumb moves immediately; the latest absolute
                // value is dispatched only after the shared trailing settle.
                InvalidateWidgetSliderValues(
                    *snapshot, {focused->id}, true);
            }
            if (adjustment.actionRequest) {
                const auto button = direction == widgetrail::input::NavigationDirection::Left
                    ? std::wstring_view{L"DPadLeft"}
                    : std::wstring_view{L"DPadRight"};
                DispatchWidgetAction(
                    button, phase, adjustment.actionRequest);
            }
            return;
        }
        if (phase == widgetrail::input::NavigationEventPhase::Repeated && !repeatedCanNavigate)
            return;
        switch (direction) {
        case widgetrail::input::NavigationDirection::Left: MoveWidgetFocus(L"left", phase); break;
        case widgetrail::input::NavigationDirection::Right: MoveWidgetFocus(L"right", phase); break;
        case widgetrail::input::NavigationDirection::Up: MoveWidgetFocus(L"up", phase); break;
        case widgetrail::input::NavigationDirection::Down: MoveWidgetFocus(L"down", phase); break;
        default: break;
        }
    }

    void RefreshCurrentBridgeSnapshot(const std::uint64_t correlationId = 0) {
        if (state_.surface() == widgetrail::Surface::Hidden &&
            !pinnedSurfaceCoordinator_.pinned()) return;
        const std::wstring_view widgetId = state_.surface() == widgetrail::Surface::Hidden
            ? pinnedSurfaceCoordinator_.widgetId()
            : state_.surface() == widgetrail::Surface::Widget
            ? state_.activeWidget()
            : state_.selectedWidget();
        if (IsBridgeWidget(widgetId))
            RefreshWidgetSnapshot(widgetId, correlationId);
    }

    [[nodiscard]] bool TrayYRestartEligible() const noexcept {
        return state_.surface() != widgetrail::Surface::Hidden &&
               state_.focusRegion() == widgetrail::FocusRegion::Tray &&
               !state_.reorderMode() &&
               IsBridgeWidget(state_.selectedWidget());
    }

    void ApplyTrayYGestureAction(const widgetrail::input::TrayYGestureAction action) {
        switch (action) {
        case widgetrail::input::TrayYGestureAction::ToggleReorder:
            Dispatch(widgetrail::Command::ToggleReorder);
            break;
        case widgetrail::input::TrayYGestureAction::RestartSelectedWidget:
            // The recognizer revalidated the exact selected bridge widget and
            // tray context. Resolve it once more through the same restart
            // authority as F5; tray Y never depends on a widget-authored action.
            RestartCurrentWidget();
            break;
        case widgetrail::input::TrayYGestureAction::None:
            break;
        }
    }

    void RestartCurrentWidget() {
        const std::wstring widgetId{widgetrail::input::ResolveCurrentWidgetReloadTarget(
            state_.surface() != widgetrail::Surface::Hidden,
            state_.focusRegion() == widgetrail::FocusRegion::Tray,
            state_.selectedWidget(), state_.activeWidget())};
        if (!IsBridgeWidget(widgetId)) return;
        if (pendingWidgetSwitchSnap_ &&
            pendingWidgetSwitchSnap_->widgetId == widgetId) {
            pendingWidgetSwitchSnap_.reset();
        }
        if (pinnedSurfaceCoordinator_.pinned() &&
            pinnedSurfaceCoordinator_.widgetId() == widgetId) {
            (void)pinnedSurfaceCoordinator_.Unpin(
                widgetrail::pinned::WidgetSurfaceStopReason::RuntimeReplaced);
        }
        const auto* descriptor = sessions_.FindDescriptor(widgetId);
        CommitAdmittedWidgetPresentation(widgetId);
        if (descriptor) {
            if (declarativeRenderer_ && !descriptor->instanceId.empty())
                declarativeRenderer_->ForgetWidgetState(descriptor->instanceId);
            interactionSession_.ForgetRuntime(descriptor->instanceId);
        }
        (void)interactionSession_.TransitionPressedPresentation(
            widgetrail::input::PressedInputTransition::Clear);
        ClearFreeScrollReentry(L"widget-restart");
        ClearScrollPaginationPrefetch(L"widget-restart");
        interactionSession_.ForgetWidget(widgetId);
        interactionSession_.ClearFocus();
        sessions_.RemoveSnapshot(widgetId);
        renderedSnapshotSequences_.erase(widgetId);
        pendingContentRevealWidget_ = widgetId;
        overlayTransition_.SnapContentVisible();
        ClearAccessibilityTree();
        if (state_.surface() == widgetrail::Surface::Widget &&
            state_.activeWidget() == widgetId && IsWindowVisible(window_)) {
            RedrawWindow(window_, nullptr, nullptr,
                RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN);
        }

        if (!sessions_.RequestRestart(widgetId)) {
            pendingContentRevealWidget_.clear();
            RecordWidgetStartupFailure(widgetId, L"The restart request queue is full.");
            return;
        }

        lastActionWidgetId_ = widgetId;
        lastActionMessage_ = std::wstring(DisplayWidgetName(widgetId)) + L" reloading";
        lastActionExpiresAt_ = GetTickCount64() + 1800;
        AppendDiagnostic(lastActionMessage_);
        InvalidateRect(window_, nullptr, FALSE);
    }

    void RefreshWidgetSnapshot(
        const std::wstring_view widgetId,
        const std::uint64_t correlationId = 0) {
        if (!IsBridgeWidget(widgetId)) return;
        // A failed admission owns the presentation until an explicit retry.
        // Fetching a snapshot from the still-background registration would
        // replace the actionable startup diagnostic with a missing-cache error.
        if (sessions_.Failure(widgetId)) return;
        const auto currentWidget = state_.surface() == widgetrail::Surface::Widget
            ? state_.activeWidget()
            : state_.selectedWidget();
        if (currentWidget == widgetId) RememberCurrentFocus(widgetId);
        if (!sessions_.RequestSnapshot(widgetId, false, correlationId)) {
            RecordWidgetStartupFailure(widgetId, L"The snapshot request queue is full.");
        }
    }

    void MoveWidgetFocus(
        const std::wstring_view direction,
        const widgetrail::input::NavigationEventPhase phase) {
        if (state_.surface() != widgetrail::Surface::Widget ||
            state_.focusRegion() != widgetrail::FocusRegion::Widget) {
            return;
        }
        const std::wstring_view widgetId = state_.activeWidget();
        const auto* snapshot = InteractionSnapshotFor(widgetId);
        if (!snapshot) return;
        const auto activeScope = std::wstring_view(snapshot->activeInputScopeId);
        widgetrail::input::NavigationDirection navigationDirection =
            widgetrail::input::NavigationDirection::None;
        if (direction == L"left") navigationDirection = widgetrail::input::NavigationDirection::Left;
        else if (direction == L"right") navigationDirection = widgetrail::input::NavigationDirection::Right;
        else if (direction == L"up") navigationDirection = widgetrail::input::NavigationDirection::Up;
        else if (direction == L"down") navigationDirection = widgetrail::input::NavigationDirection::Down;
        const auto authority = InteractionAuthority(widgetId, *snapshot);
        if (!authority) return;
        const auto flushFocusedSlider = [&] {
            const auto* focused = widgetrail::input::FindNodeInInputScope(
                *snapshot, interactionSession_.focusedElementId(), activeScope);
            if (focused && focused->kind == L"slider")
                FlushCurrentSliderAction(*authority, *focused, GetTickCount64());
        };
        const bool pendingFocusGroupEntry =
            interactionSession_.FocusGroupEntryRequestPending(*authority);
        if (pendingFocusGroupEntry &&
            phase != widgetrail::input::NavigationEventPhase::Pressed) {
            return;
        }
        auto resolution = interactionSession_.ResolveDirectionalFocus(
            widgetId, *snapshot, navigationDirection, lastWidgetRenderResult_);
        if (resolution.disposition ==
            widgetrail::input::DirectionalFocusDisposition::MissingVisibleFocus) {
            // A constrained viewport or responsive branch can leave a valid
            // root snapshot with no currently reachable control. Preserve the
            // same lower-boundary contract as an ordinary last row instead of
            // trapping controller focus in invisible widget geometry.
            if (widgetrail::input::ShouldTransferFocusToTray(
                    navigationDirection,
                    activeScope == widgetrail::input::RootInputScope(*snapshot),
                    false,
                    false)) {
                flushFocusedSlider();
                Dispatch(widgetrail::Command::SampleWidgetBack);
            }
            return;
        }
        auto admission = widgetrail::input::AdmitDirectionalFocusResolution(
            interactionSession_, *authority, lastWidgetRenderResult_,
            interactionSession_.focusedElementId(), navigationDirection,
            std::move(resolution),
            widgetrail::input::ScrollPaginationIntentSource::
                DirectionalNavigation,
            GetTickCount64());
        PublishScrollPaginationOutcome(admission.pagination);
        if (admission.retainFocus) return;
        resolution = std::move(admission.resolution);
        if (resolution.target) {
            const auto flushInstanceId = snapshot->instanceId;
            const auto flushSequence = snapshot->sequence;
            const std::wstring flushRuntime{authority->runtimeGeneration};
            const std::wstring flushPresentation{
                authority->presentationGeneration};
            flushFocusedSlider();
            const auto* postFlushSnapshot = InteractionSnapshotFor(widgetId);
            const auto postFlushAuthority = postFlushSnapshot
                ? InteractionAuthority(widgetId, *postFlushSnapshot)
                : std::nullopt;
            if (postFlushSnapshot != snapshot || !postFlushAuthority ||
                postFlushSnapshot->instanceId != flushInstanceId ||
                postFlushSnapshot->sequence != flushSequence ||
                postFlushAuthority->runtimeGeneration != flushRuntime ||
                postFlushAuthority->presentationGeneration != flushPresentation) {
                return;
            }
            if (pendingFocusGroupEntry) {
                RetirePendingFocusGroupEntryForUserIntent(
                    widgetId, *snapshot, L"directional-input");
            }
            ObserveScrollPaginationFocusIntent(
                widgetId, *snapshot, interactionSession_.focusedElementId(),
                *resolution.target,
                widgetrail::input::ScrollPaginationIntentSource::
                    DirectionalNavigation);
            const auto focus = interactionSession_.MoveFocus(
                widgetId, *snapshot, *resolution.target);
            if (resolution.disposition ==
                    widgetrail::input::DirectionalFocusDisposition::Explicit ||
                resolution.disposition ==
                    widgetrail::input::DirectionalFocusDisposition::Geometric) {
                (void)scrollEvidenceProbe_.RecordTarget(*resolution.target, direction);
            }
            InvalidateWidgetFocusChange(
                focus.priorFocus, focus.sliderDamageNodeIds);
            return;
        }
        if (pendingFocusGroupEntry) return;
        if (widgetrail::input::ShouldTransferFocusToTray(
                navigationDirection,
                activeScope == widgetrail::input::RootInputScope(*snapshot),
                false,
                false)) {
            flushFocusedSlider();
            Dispatch(widgetrail::Command::SampleWidgetBack);
        }
    }

    static std::wstring_view ProtocolButton(const std::wstring_view button) {
        if (button == L"A") return L"a";
        if (button == L"B") return L"b";
        if (button == L"X") return L"x";
        if (button == L"Y") return L"y";
        if (button == L"LB") return L"leftBumper";
        if (button == L"RB") return L"rightBumper";
        if (button == L"LT") return L"leftTrigger";
        if (button == L"RT") return L"rightTrigger";
        if (button == L"LS") return L"leftStick";
        if (button == L"RS") return L"rightStick";
        if (button == L"Menu") return L"menu";
        if (button == L"View") return L"view";
        if (button == L"DPadLeft") return L"dPadLeft";
        if (button == L"DPadRight") return L"dPadRight";
        return L"";
    }

    enum class HeldActionKind { Authored, CompactMedia, FullscreenMedia };

    /// Why a resolve produced no authority. A binding whose owner is only
    /// momentarily unavailable is still this hold's binding.
    enum class HeldActionResolve { Resolved, Deferred, Retired };

    struct HeldActionAuthority final {
        HeldActionKind kind{HeldActionKind::Authored};
        std::wstring widgetId;
        std::wstring instanceId;
        std::wstring runtimeGeneration;
        std::wstring presentationGeneration;
        std::wstring mediaSessionId;
        std::wstring inputScopeId;
        std::wstring focusElementId;
        std::wstring sourceElementId;
        std::wstring protocolButton;
        std::wstring actionId;
        bool dashboard{};
        bool pinned{};
    };

    [[nodiscard]] static widgetrail::input::AuthoredHeldActionBindingView
    HeldActionBinding(const HeldActionAuthority& authority) noexcept {
        return {
            authority.focusElementId,
            authority.sourceElementId,
            authority.actionId,
        };
    }

    /// The shell conditions that admit an authored held action. None of them
    /// read the widget's interaction snapshot, so they remain answerable while
    /// a refresh is holding presentation authority.
    [[nodiscard]] bool AuthoredHeldActionAdmissible() const {
        const auto modalDecision = widgetrail::input::DecideAuthoredHeldAction(
            widgetrail::input::AuthoredHeldActionDecisionPhase::Arm,
            interactionSession_.selectPopup().has_value(), {});
        return modalDecision.disposition ==
                widgetrail::input::AuthoredHeldActionDisposition::Dispatch &&
            state_.surface() != widgetrail::Surface::Hidden &&
            !textEntryModal_.active() && !trayContextMenu_ &&
            !widgetContextMenu_ &&
            !OverlayFullscreenMediaRequested() &&
            !pinnedSurfaceCoordinator_.controllerFocused() &&
            pinnedSurfaceCoordinator_.placementMode() ==
                widgetrail::pinned::PlacementMode::None &&
            !pinnedSurfaceCoordinator_.opacityAdjustmentActive() &&
            !RichMediaInputCurrent();
    }

    [[nodiscard]] std::optional<HeldActionAuthority> ResolveAuthoredHeldAction(
        const std::wstring_view protocolButton,
        std::wstring* reason = nullptr,
        HeldActionResolve* outcome = nullptr) const {
        if (outcome) *outcome = HeldActionResolve::Retired;
        const auto bail = [&](const wchar_t* why,
                              const HeldActionResolve resolve =
                                  HeldActionResolve::Retired) {
            if (reason) *reason = why;
            if (outcome) *outcome = resolve;
        };
        if (!AuthoredHeldActionAdmissible()) {
            bail(L"not-admissible");
            return std::nullopt;
        }
        const bool dashboard = state_.focusRegion() == widgetrail::FocusRegion::Tray;
        const std::wstring_view widgetId = dashboard
            ? state_.selectedWidget() : state_.activeWidget();
        if (!dashboard && (state_.surface() != widgetrail::Surface::Widget ||
                           state_.focusRegion() != widgetrail::FocusRegion::Widget)) {
            bail(L"not-open-widget");
            return std::nullopt;
        }
        const auto* snapshot = InteractionSnapshotFor(widgetId);
        const auto* descriptor = sessions_.FindDescriptor(widgetId);
        if (!snapshot || !descriptor) {
            bail(!snapshot ? L"no-snapshot" : L"no-descriptor");
            return std::nullopt;
        }

        HeldActionAuthority authority{
            HeldActionKind::Authored, std::wstring{widgetId}, snapshot->instanceId,
            descriptor->runtimeGeneration, descriptor->presentationGeneration,
            {}, snapshot->activeInputScopeId, {}, {},
            std::wstring{protocolButton}, {},
            dashboard, pinnedSurfaceCoordinator_.pinned()};
        if (dashboard) {
            const auto action = std::find_if(
                snapshot->quickActions.begin(), snapshot->quickActions.end(),
                [&](const auto& candidate) {
                    return candidate.button == protocolButton &&
                        candidate.repeatPolicy == L"whileHeld";
                });
            if (action == snapshot->quickActions.end()) {
                bail(L"no-quick-action");
                return std::nullopt;
            }
            authority.sourceElementId = L"dashboard-card";
            authority.actionId = action->actionId;
            return authority;
        }

        const auto* scopeRoot = widgetrail::input::FindControllerShortcutScopeRoot(
            snapshot->root, snapshot->activeInputScopeId);
        if (!scopeRoot) {
            bail(L"no-scope-root");
            return std::nullopt;
        }
        const auto focusedElementId = interactionSession_.focusedElementId();
        const std::optional<std::wstring_view> shortcutFocus =
            focusedElementId.empty()
                ? std::nullopt
                : std::optional<std::wstring_view>{focusedElementId};
        const auto visible = focusedElementId.empty()
            ? std::optional<std::wstring>{}
            : widgetrail::input::ResolveVisibleFocusTarget(
                focusedElementId, snapshot->activeInputScopeId,
                lastWidgetRenderResult_);
        const auto shortcut = widgetrail::input::ResolveHostControllerShortcut(
            *scopeRoot, shortcutFocus,
            visible ? std::optional<std::wstring_view>{*visible} : std::nullopt,
            protocolButton, L"pressed");
        if (shortcut.status ==
            widgetrail::input::ControllerShortcutResolutionStatus::FocusNotFound) {
            bail(L"stale-or-hidden-focus");
            return std::nullopt;
        }
        if ((shortcut.status ==
                 widgetrail::input::ControllerShortcutResolutionStatus::Resolved ||
             shortcut.status ==
                 widgetrail::input::ControllerShortcutResolutionStatus::OwnerUnavailable) &&
            shortcut.repeatPolicy != L"whileHeld") {
            bail(L"no-repeat-shortcut");
            return std::nullopt;
        }
        if (shortcut.status ==
            widgetrail::input::ControllerShortcutResolutionStatus::OwnerUnavailable) {
            bail(shortcut.owner->isDisabled ? L"owner-disabled" : L"owner-busy",
                 HeldActionResolve::Deferred);
            return std::nullopt;
        }
        if (shortcut.status !=
            widgetrail::input::ControllerShortcutResolutionStatus::Resolved) {
            bail(L"no-repeat-shortcut");
            return std::nullopt;
        }
        authority.focusElementId = focusedElementId;
        authority.sourceElementId = shortcut.owner->id;
        authority.actionId = shortcut.actionId;
        if (outcome) *outcome = HeldActionResolve::Resolved;
        return authority;
    }

    [[nodiscard]] bool MediaHeldActionAuthorityCurrent(
        const HeldActionAuthority& captured) const {
        const EmbeddedMediaSessionKey sessionKey{
            captured.widgetId,
            captured.instanceId,
            captured.runtimeGeneration,
            captured.mediaSessionId,
        };
        const auto* session = mediaSessions_.Find(sessionKey);
        if (!session || !session->authority || !session->coordinator ||
            !EmbeddedMediaAuthorityCurrent(sessionKey) ||
            session->authority->presentation ==
                EmbeddedMediaPresentationState::Parked ||
            session->authority->presentationGeneration !=
                captured.presentationGeneration ||
            pinnedSurfaceCoordinator_.pinned() != captured.pinned ||
            !EmbeddedMediaCommandSupported(
                sessionKey,
                captured.protocolButton == L"leftTrigger"
                    ? L"seekBackward" : L"seekForward"))
            return false;
        const auto endpoint = captured.kind == HeldActionKind::CompactMedia
            ? widgetrail::media::Endpoint::Pinned
            : widgetrail::media::Endpoint::Overlay;
        const auto endpointOwner = mediaSessions_.EndpointOwner(endpoint);
        if (!endpointOwner || *endpointOwner != sessionKey) return false;
        return captured.kind == HeldActionKind::CompactMedia
            ? session->authority->presentation ==
                    EmbeddedMediaPresentationState::CompactPinned &&
                pinnedSurfaceCoordinator_.controllerFocused() &&
                pinnedSurfaceCoordinator_.compactMediaPresentation() &&
                pinnedSurfaceCoordinator_.widgetId() == captured.widgetId
            : OverlayFullscreenMediaRequested() &&
                session->authority->presentation ==
                    EmbeddedMediaPresentationState::OverlayFullscreen;
    }

    /// A handled authored action always requests the widget's next snapshot,
    /// and that refresh withholds presentation authority for a few frames. The
    /// gap is this held action's own consequence, not a loss of its authority,
    /// so it defers the next emission rather than cancelling the hold. Every
    /// other change -- a different widget, scope, binding, runtime generation,
    /// pin transition, or a failure -- retires the hold for good.
    [[nodiscard]] widgetrail::input::HeldButtonAuthorityState
    HeldActionAuthorityState(
        const HeldActionAuthority& captured,
        std::wstring* reason = nullptr) const {
        using widgetrail::input::HeldButtonAuthorityState;
        if (captured.kind != HeldActionKind::Authored)
            return MediaHeldActionAuthorityCurrent(captured)
                ? HeldButtonAuthorityState::Current
                : HeldButtonAuthorityState::Retired;
        if (!AuthoredHeldActionAdmissible())
            return HeldButtonAuthorityState::Retired;
        const bool dashboard =
            state_.focusRegion() == widgetrail::FocusRegion::Tray;
        const std::wstring_view widgetId = dashboard
            ? state_.selectedWidget() : state_.activeWidget();
        const auto* descriptor = sessions_.FindDescriptor(widgetId);
        if (dashboard != captured.dashboard || widgetId != captured.widgetId ||
            pinnedSurfaceCoordinator_.pinned() != captured.pinned ||
            !descriptor ||
            descriptor->runtimeGeneration != captured.runtimeGeneration ||
            descriptor->presentationGeneration != captured.presentationGeneration) {
            if (reason)
                *reason = std::wstring(L"shell:") +
                    (dashboard != captured.dashboard ? L"dashboard " : L"") +
                    (widgetId != captured.widgetId ? L"widgetId " : L"") +
                    (pinnedSurfaceCoordinator_.pinned() != captured.pinned
                        ? L"pinned " : L"") +
                    (!descriptor ? L"descriptor " : L"") +
                    (descriptor && descriptor->runtimeGeneration !=
                        captured.runtimeGeneration ? L"runtimeGen " : L"") +
                    (descriptor && descriptor->presentationGeneration !=
                        captured.presentationGeneration ? L"presentationGen " : L"");
            return HeldButtonAuthorityState::Retired;
        }
        if (!InteractionSnapshotFor(widgetId) &&
            sessions_.Presentation(widgetId).authority ==
                widgetrail::WidgetPresentationAuthority::RefreshRetained)
            return HeldButtonAuthorityState::Deferred;
        std::wstring resolveReason;
        auto resolveOutcome = HeldActionResolve::Retired;
        const auto current = ResolveAuthoredHeldAction(
            captured.protocolButton, &resolveReason, &resolveOutcome);
        if (!current) {
            if (reason) *reason = L"resolve:" + resolveReason;
            return resolveOutcome == HeldActionResolve::Deferred
                ? HeldButtonAuthorityState::Deferred
                : HeldButtonAuthorityState::Retired;
        }
        const auto bindingDecision = widgetrail::input::DecideAuthoredHeldAction(
            widgetrail::input::AuthoredHeldActionDecisionPhase::Repeat,
            interactionSession_.selectPopup().has_value(),
            HeldActionBinding(captured), HeldActionBinding(*current));
        if (bindingDecision.disposition !=
            widgetrail::input::AuthoredHeldActionDisposition::Dispatch) {
            if (reason) *reason = L"binding-authority";
            return HeldButtonAuthorityState::Retired;
        }
        std::wstring mismatch;
        const auto compare = [&](const wchar_t* name,
                                 const std::wstring& a, const std::wstring& b) {
            if (a != b)
                mismatch += std::wstring(name) + L"[" + a + L"!=" + b + L"] ";
        };
        compare(L"widgetId", current->widgetId, captured.widgetId);
        compare(L"instanceId", current->instanceId, captured.instanceId);
        compare(L"runtimeGen", current->runtimeGeneration, captured.runtimeGeneration);
        compare(L"presentationGen",
            current->presentationGeneration, captured.presentationGeneration);
        compare(L"mediaSessionId",
            current->mediaSessionId, captured.mediaSessionId);
        compare(L"inputScope", current->inputScopeId, captured.inputScopeId);
        if (current->kind != captured.kind) mismatch += L"kind ";
        if (current->dashboard != captured.dashboard) mismatch += L"dashboard ";
        if (current->pinned != captured.pinned) mismatch += L"pinned ";
        if (mismatch.empty()) return HeldButtonAuthorityState::Current;
        if (reason) *reason = L"field:" + mismatch;
        return HeldButtonAuthorityState::Retired;
    }

    [[nodiscard]] static std::uint32_t RepeatButtonKey(
        const std::wstring_view protocolButton) noexcept {
        if (protocolButton == L"leftTrigger") return 0x1'0000;
        if (protocolButton == L"rightTrigger") return 0x2'0000;
        if (protocolButton == L"x") return XINPUT_GAMEPAD_X;
        if (protocolButton == L"y") return XINPUT_GAMEPAD_Y;
        if (protocolButton == L"leftBumper") return XINPUT_GAMEPAD_LEFT_SHOULDER;
        if (protocolButton == L"rightBumper") return XINPUT_GAMEPAD_RIGHT_SHOULDER;
        if (protocolButton == L"leftStick") return XINPUT_GAMEPAD_LEFT_THUMB;
        if (protocolButton == L"rightStick") return XINPUT_GAMEPAD_RIGHT_THUMB;
        return 0;
    }

    [[nodiscard]] static bool RepeatButtonDown(
        const WidgetRailOverlayPlatformControllerFrame& frame,
        const std::uint32_t key) noexcept {
        if (key == 0x1'0000)
            return frame.state.leftTrigger >= XINPUT_GAMEPAD_TRIGGER_THRESHOLD;
        if (key == 0x2'0000)
            return frame.state.rightTrigger >= XINPUT_GAMEPAD_TRIGGER_THRESHOLD;
        return key != 0 && (frame.state.buttons & static_cast<WORD>(key)) != 0;
    }

    void BeginHeldActionRepeat(
        HeldActionAuthority authority,
        const ULONGLONG now) {
        const auto key = RepeatButtonKey(authority.protocolButton);
        if (key == 0) return;
        heldActionAuthority_ = std::move(authority);
        heldActionRepeat_.Begin(key, now);
    }

    void BeginMediaHeldActionRepeat(
        const EmbeddedMediaSessionKey& key,
        const HeldActionKind kind,
        const std::wstring_view protocolButton,
        const ULONGLONG now) {
        const auto* session = mediaSessions_.Find(key);
        if (!session || !session->authority) return;
        const auto& authority = *session->authority;
        BeginHeldActionRepeat({
            kind,
            authority.widgetId,
            authority.instanceId,
            authority.runtimeGeneration,
            authority.presentationGeneration,
            authority.sessionId,
            {}, {}, {}, std::wstring{protocolButton}, {}, false,
            pinnedSurfaceCoordinator_.pinned(),
        }, now);
    }

    void DispatchRepeatableControllerAction(
        const std::wstring_view button,
        const ULONGLONG now) {
        const auto protocolButton = ProtocolButton(button);
        if (RepeatButtonKey(protocolButton) != 0) {
            const auto authority = ResolveAuthoredHeldAction(protocolButton);
            AppendActionCorrelation(
                L"stage=held-repeat-arm button=" + std::wstring(protocolButton) +
                L" resolved=" + (authority ? L"1" : L"0") +
                L" admissible=" + (AuthoredHeldActionAdmissible() ? L"1" : L"0") +
                L" surface=" + std::to_wstring(static_cast<int>(state_.surface())) +
                L" focusRegion=" + std::to_wstring(
                    static_cast<int>(state_.focusRegion())) +
                L" fullscreenMedia=" +
                    (OverlayFullscreenMediaRequested() ? L"1" : L"0") +
                L" pinnedFocused=" +
                    (pinnedSurfaceCoordinator_.controllerFocused() ? L"1" : L"0") +
                L" richMediaProof=" + (RichMediaInputCurrent() ? L"1" : L"0") +
                L" action=" + (authority ? authority->actionId : L"none") +
                L" source=" + (authority ? authority->sourceElementId : L"none") +
                L" scope=" + (authority ? authority->inputScopeId : L"none"),
                DiagnosticSeverity::Debug);
            if (authority) {
                BeginHeldActionRepeat(*authority, now);
                DispatchWidgetAction(
                    button, widgetrail::input::NavigationEventPhase::Pressed,
                    std::nullopt, true, &*authority);
                return;
            }
        }
        DispatchControllerAction(button, true);
    }

    void PumpHeldActionRepeat(
        const WidgetRailOverlayPlatformControllerFrame& frame,
        const ULONGLONG now) {
        if (!heldActionAuthority_) return;
        const auto key = heldActionRepeat_.button();
        const bool down = RepeatButtonDown(frame, key);
        std::wstring authorityReason;
        const auto authorityState =
            HeldActionAuthorityState(*heldActionAuthority_, &authorityReason);
        const int verdict = down ? static_cast<int>(authorityState) + 1 : 0;
        if (verdict != lastHeldRepeatVerdict_) {
            lastHeldRepeatVerdict_ = verdict;
            AppendActionCorrelation(
                L"stage=held-repeat-pump button=" +
                    heldActionAuthority_->protocolButton +
                L" down=" + (down ? L"1" : L"0") +
                L" rawLeft=" + std::to_wstring(frame.state.leftTrigger) +
                L" rawRight=" + std::to_wstring(frame.state.rightTrigger) +
                L" authority=" + std::to_wstring(static_cast<int>(authorityState)) +
                L" snapshot=" +
                    (InteractionSnapshotFor(heldActionAuthority_->widgetId)
                        ? L"current" : L"absent") +
                L" presentation=" + std::to_wstring(static_cast<int>(
                    sessions_.Presentation(
                        heldActionAuthority_->widgetId).authority)) +
                L" why=" + (authorityReason.empty() ? L"none" : authorityReason),
                DiagnosticSeverity::Debug);
        }
        if (!heldActionRepeat_.Update(down, authorityState, now)) {
            if (!heldActionRepeat_.active()) {
                heldActionAuthority_.reset();
                lastHeldRepeatVerdict_ = -1;
            }
            return;
        }
        // Dispatching can retire the hold, so nothing below reads the captured
        // authority through a reference the dispatch itself may destroy.
        const auto authority = *heldActionAuthority_;
        if (authority.kind == HeldActionKind::Authored) {
            DispatchWidgetAction(
                DisplayButton(authority.protocolButton),
                widgetrail::input::NavigationEventPhase::Repeated,
                std::nullopt, false, &authority);
            return;
        }
        const auto direction = authority.protocolButton == L"leftTrigger"
            ? widgetrail::input::NavigationDirection::Left
            : widgetrail::input::NavigationDirection::Right;
        const auto target = authority.kind == HeldActionKind::CompactMedia
            ? pinnedSurfaceCoordinator_.CompactMediaSeekTarget(direction)
            : OverlayFullscreenMediaSeekTarget(direction);
        const EmbeddedMediaSessionKey sessionKey{
            authority.widgetId,
            authority.instanceId,
            authority.runtimeGeneration,
            authority.mediaSessionId,
        };
        auto* session = mediaSessions_.Find(sessionKey);
        if (target && session && session->coordinator)
            (void)session->coordinator->SendSeekPosition(*target);
    }

    bool HandleFocusedSliderModeButton(const std::wstring_view button) {
        if (button != L"A" && button != L"B") return false;
        if (state_.surface() != widgetrail::Surface::Widget ||
            state_.focusRegion() != widgetrail::FocusRegion::Widget) return false;
        const std::wstring_view widget = state_.activeWidget();
        const auto* snapshot = InteractionSnapshotFor(widget);
        if (!snapshot) return false;
        const auto visible = widgetrail::input::ResolveVisibleFocusTarget(
            interactionSession_.focusedElementId(), snapshot->activeInputScopeId, lastWidgetRenderResult_);
        if (!visible) return false;
        if (*visible != interactionSession_.focusedElementId()) {
            const auto focus = interactionSession_.MoveFocus(
                widget, *snapshot, *visible);
            InvalidateWidgetFocusChange(
                focus.priorFocus, focus.sliderDamageNodeIds);
        }
        const auto* focused = widgetrail::input::FindNodeInInputScope(
            *snapshot, interactionSession_.focusedElementId(), snapshot->activeInputScopeId);
        if (!focused) return false;
        auto interactionAuthority = InteractionAuthority(widget, *snapshot);
        if (!interactionAuthority) return false;
        const bool activationRequired =
            focused->sliderInteractionMode == L"activateToAdjust";
        const bool adjustmentActive = activationRequired &&
            interactionSession_.SliderAdjustmentModeActive(
                *interactionAuthority, *focused);
        using widgetrail::input::FocusedSliderButtonRoute;
        switch (widgetrail::input::RouteFocusedSliderButton(
            focused->kind, activationRequired, adjustmentActive, button)) {
        case FocusedSliderButtonRoute::EnterAdjustment:
            (void)interactionSession_.TransitionSliderAdjustmentMode(
                *interactionAuthority, *focused,
                widgetrail::input::SliderAdjustmentModeTransition::Enter,
                GetTickCount64());
            InvalidateRect(window_, nullptr, FALSE);
            return true;
        case FocusedSliderButtonRoute::ExitAdjustment:
            FlushCurrentSliderAction(
                *interactionAuthority, *focused, GetTickCount64());
            snapshot = InteractionSnapshotFor(widget);
            if (!snapshot) return true;
            interactionAuthority = InteractionAuthority(widget, *snapshot);
            focused = widgetrail::input::FindNodeInInputScope(
                *snapshot, interactionSession_.focusedElementId(),
                snapshot->activeInputScopeId);
            if (!interactionAuthority || !focused || focused->kind != L"slider")
                return true;
            (void)interactionSession_.TransitionSliderAdjustmentMode(
                *interactionAuthority, *focused,
                widgetrail::input::SliderAdjustmentModeTransition::Exit,
                GetTickCount64());
            InvalidateRect(window_, nullptr, FALSE);
            return true;
        case FocusedSliderButtonRoute::Widget:
            return false;
        }
        return false;
    }

    [[nodiscard]] bool HandleOverlayMediaBackButton(
        const std::wstring_view button) {
        const auto overlayKey =
            mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Overlay);
        const auto* overlaySession = overlayKey
            ? mediaSessions_.Find(*overlayKey) : nullptr;
        const bool overlayMediaAuthorityCurrent = overlayKey && overlaySession &&
            overlaySession->authority &&
            EmbeddedMediaAuthorityCurrent(*overlayKey) &&
            overlaySession->authority->presentation !=
                EmbeddedMediaPresentationState::Parked;
        switch (widgetrail::input::RouteOverlayMediaBackButton(
            button, OverlayFullscreenMediaRequested(), overlayMediaAuthorityCurrent,
            overlayMediaAuthorityCurrent &&
                state_.surface() == widgetrail::Surface::Widget &&
                state_.focusRegion() == widgetrail::FocusRegion::Widget &&
                state_.activeWidget() ==
                    overlaySession->authority->widgetId)) {
        case widgetrail::input::OverlayMediaBackRoute::ExitOverlayFullscreen:
            if (ExitOverlayFullscreenMedia()) {
                RefreshAndApplyPresentation([] {});
                InvalidateRect(window_, nullptr, FALSE);
            }
            return true;
        case widgetrail::input::OverlayMediaBackRoute::HostWidgetBack:
            Dispatch(widgetrail::Command::SampleWidgetBack);
            return true;
        case widgetrail::input::OverlayMediaBackRoute::Widget:
            return false;
        }
        return false;
    }

    void DispatchControllerAction(
        const std::wstring_view button,
        const bool physicalPress = false) {
        if (interactionSession_.selectPopup()) {
            if (button == L"A") CommitSelectPopup(
                physicalPress
                    ? widgetrail::ControllerInputOrigin::PhysicalController
                    : widgetrail::ControllerInputOrigin::AccessibilityAutomation);
            else if (button == L"B") {
                (void)interactionSession_.CloseSelectPopup();
                widgetAccessibilityProjection_.Clear();
                InvalidateRect(window_, nullptr, FALSE);
            }
            return;
        }
        if (textEntryModal_.active()) {
            textEntryModal_.HandleController(button);
            return;
        }
        if (button == L"A" &&
            state_.surface() == widgetrail::Surface::Widget &&
            state_.focusRegion() == widgetrail::FocusRegion::Widget) {
            const auto* snapshot = InteractionSnapshotFor(state_.activeWidget());
            if (snapshot) {
                const auto authority =
                    InteractionAuthority(state_.activeWidget(), *snapshot);
                if (authority &&
                    interactionSession_.FocusGroupEntryRequestPending(*authority) &&
                    interactionSession_.focusedElementId().empty()) {
                    return;
                }
                RetirePendingFocusGroupEntryForUserIntent(
                    state_.activeWidget(), *snapshot, L"activation");
                }
        }
        if (button == L"B" &&
            state_.surface() == widgetrail::Surface::Widget &&
            state_.focusRegion() == widgetrail::FocusRegion::Widget) {
            const auto* snapshot = InteractionSnapshotFor(state_.activeWidget());
            const auto authority = snapshot
                ? InteractionAuthority(state_.activeWidget(), *snapshot)
                : std::nullopt;
            const auto* focused = snapshot
                ? widgetrail::input::FindNodeInInputScope(
                    *snapshot, interactionSession_.focusedElementId(),
                    snapshot->activeInputScopeId)
                : nullptr;
            if (authority && focused && focused->kind == L"slider")
                FlushCurrentSliderAction(*authority, *focused, GetTickCount64());
        }
        if (HandleFocusedSliderModeButton(button)) return;
        if (button == L"A") {
            const auto selectActivation = OpenFocusedSelectPopup();
            if (selectActivation !=
                widgetrail::input::SelectActivationResult::NotSelect)
                return;
        }
        if (button == L"B" && NonCurrentHostRootBackAuthority()) {
            Dispatch(widgetrail::Command::SampleWidgetBack);
            return;
        }
        if (HandleOverlayMediaBackButton(button)) return;
        using widgetrail::input::ControllerActionContext;
        using widgetrail::input::ControllerActionRoute;
        const auto context = state_.focusRegion() == widgetrail::FocusRegion::Tray
            ? ControllerActionContext::Tray
            : ControllerActionContext::RootWidgetScope;
        switch (widgetrail::input::RouteControllerAction(context, button)) {
        case ControllerActionRoute::HostActivate:
            Dispatch(widgetrail::Command::Activate);
            return;
        case ControllerActionRoute::HostToggleReorder:
            Dispatch(widgetrail::Command::ToggleReorder);
            return;
        case ControllerActionRoute::HostCloseOverlay:
            Dispatch(widgetrail::Command::ToggleOverlay);
            return;
        case ControllerActionRoute::Widget:
            DispatchWidgetAction(
                button,
                widgetrail::input::NavigationEventPhase::Pressed,
                std::nullopt,
                physicalPress);
            return;
        case ControllerActionRoute::HostBackToDashboard:
        case ControllerActionRoute::None:
            return;
        }
    }

    void AttemptOverlayFullscreenMediaEntry(
        const std::wstring_view widgetId,
        const widgetrail::WidgetSnapshot& snapshot,
        const std::wstring_view source,
        const std::wstring_view protocolButton) {
        const auto decision = EvaluateFullscreenEntry(widgetId, snapshot);
        const bool requested = decision.requestEligible &&
            EnterOverlayFullscreenMedia(decision);
        AppendActionCorrelation(
            L"stage=embedded-media-fullscreen-entry source=" + std::wstring(source) +
            L" button=" + std::wstring(protocolButton) + L" interaction-sequence=" +
            std::to_wstring(snapshot.sequence) + L" authority-sequence=" +
            std::to_wstring(decision.authoritySequence) + L" session=" +
            (decision.sessionPresent ? L"1" : L"0") + L" identity=" +
            (decision.exactIdentity ? L"1" : L"0") + L" authority=" +
            (decision.presentationAuthorityCurrent ? L"1" : L"0") + L" owner=" +
            (decision.overlayOwnerCurrent ? L"1" : L"0") + L" viewport=" +
            (decision.committedViewport ? L"1" : L"0") + L" capability=" +
            (decision.fullscreenCapable ? L"1" : L"0") + L" pinned=" +
            (decision.pinnedTakeover ? L"1" : L"0") + L" requested=" +
            (requested ? L"1" : L"0") + L" overlay-viewport=" +
            (decision.overlayViewport ? L"1" : L"0") + L" a-pressed=" +
            (protocolButton == L"a" ? L"1" : L"0"));
        if (requested) RefreshAndApplyPresentation([] {});
        else {
            lastActionWidgetId_ = std::wstring(widgetId);
            lastActionMessage_ = L"Fullscreen is unavailable for the current media session";
            lastActionExpiresAt_ = GetTickCount64() + 2400;
            if (pinnedSurfaceCoordinator_.pinned() &&
                pinnedSurfaceCoordinator_.widgetId() == widgetId) {
                pinnedSurfaceCoordinator_.SetActionFeedback(
                    lastActionMessage_, true);
            }
            AppendDiagnostic(lastActionMessage_);
        }
        InvalidateRect(window_, nullptr, FALSE);
    }

    [[nodiscard]] bool TryDispatchNativeMediaAction(
        const std::wstring_view widgetId,
        const widgetrail::WidgetSnapshot& snapshot,
        const widgetrail::WidgetNode& node,
        const std::wstring_view protocolButton,
        const widgetrail::input::NavigationEventPhase phase) {
        if (node.actionId == L"host.embeddedMediaSession.enterFullscreen") {
            if (protocolButton != L"a" ||
                phase != widgetrail::input::NavigationEventPhase::Pressed)
                return false;
            AttemptOverlayFullscreenMediaEntry(
                widgetId, snapshot, L"focused-node", protocolButton);
            return true;
        }
        const auto sessionKey = CurrentEmbeddedMediaSessionKey(widgetId);
        auto* session = sessionKey ? mediaSessions_.Find(*sessionKey) : nullptr;
        if (protocolButton != L"a" ||
            phase != widgetrail::input::NavigationEventPhase::Pressed ||
            !sessionKey || !session || !session->authority ||
            !EmbeddedMediaAuthorityCurrent(*sessionKey) ||
            session->authority->widgetId != widgetId ||
            session->authority->sequence != snapshot.sequence)
            return false;
        const auto overlayOwner =
            mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Overlay);
        if (node.actionId == L"host.embeddedMediaSession.back") {
            if (overlayOwner && *overlayOwner == *sessionKey &&
                session->authority->presentation ==
                    EmbeddedMediaPresentationState::OverlayViewport) {
                Dispatch(widgetrail::Command::SampleWidgetBack);
                return true;
            }
            return false;
        }
        // Native media controls remain ordinary package actions. The package
        // publishes one typed playback command in its next exact snapshot;
        // only the host-owned current session may execute that command.
        return false;
    }

    void DispatchWidgetAction(
        const std::wstring_view button,
        const widgetrail::input::NavigationEventPhase phase =
            widgetrail::input::NavigationEventPhase::Pressed,
        const std::optional<widgetrail::input::WidgetInteractionActionRequest>&
            exactActionRequest = std::nullopt,
        const bool physicalPress = false,
        const HeldActionAuthority* exactHeldAction = nullptr) {
        if (state_.surface() == widgetrail::Surface::Hidden) {
            if (exactActionRequest) {
                RejectWidgetActionRequest(
                    *exactActionRequest, L"overlay hidden");
            }
            return;
        }

        const bool interactiveWidget = state_.surface() == widgetrail::Surface::Widget &&
            state_.focusRegion() == widgetrail::FocusRegion::Widget;
        const std::wstring_view widget = interactiveWidget
            ? state_.activeWidget()
            : state_.selectedWidget();
        if (exactHeldAction &&
            (exactHeldAction->kind != HeldActionKind::Authored ||
             exactHeldAction->widgetId != widget ||
             exactHeldAction->dashboard == interactiveWidget))
            return;
        const auto exactHeldDecision = exactHeldAction
            ? std::optional<widgetrail::input::AuthoredHeldActionDecision>{
                widgetrail::input::DecideAuthoredHeldAction(
                    widgetrail::input::AuthoredHeldActionDecisionPhase::
                        InitialDispatch,
                    interactionSession_.selectPopup().has_value(),
                    HeldActionBinding(*exactHeldAction))}
            : std::nullopt;
        if (exactHeldDecision && exactHeldDecision->disposition !=
                widgetrail::input::AuthoredHeldActionDisposition::Dispatch)
            return;
        if (exactActionRequest &&
            (!interactiveWidget || widget != exactActionRequest->widgetId)) {
            RejectWidgetActionRequest(
                *exactActionRequest, L"widget authority changed");
            return;
        }

        if (IsBridgeWidget(widget)) {
            if (sessions_.Failure(widget)) {
                if (exactActionRequest) {
                    RejectWidgetActionRequest(
                        *exactActionRequest, L"widget failed");
                    return;
                }
                const auto route = widgetrail::input::RouteFailedWidgetAction(
                    interactiveWidget, phase, button);
                if (route == widgetrail::input::FailedWidgetActionRoute::Retry) {
                    RestartCurrentWidget();
                } else if (route ==
                           widgetrail::input::FailedWidgetActionRoute::HostBackToDashboard) {
                    Dispatch(widgetrail::Command::SampleWidgetBack);
                }
                return;
            }
            if (!SnapshotFor(widget) && exactActionRequest) {
                RejectWidgetActionRequest(
                    *exactActionRequest, L"admitted snapshot unavailable");
                return;
            }
            if (!SnapshotFor(widget)) {
                RefreshAndApplyPresentation([&] { RefreshWidgetSnapshot(widget); });
            }
            const auto protocolButton = ProtocolButton(button);
            const auto* snapshot = InteractionSnapshotFor(widget);
            if (protocolButton.empty() || !snapshot) {
                if (exactActionRequest) {
                    RejectWidgetActionRequest(
                        *exactActionRequest, L"input authority unavailable");
                }
                return;
            }
            if (!exactHeldAction && interactiveWidget &&
                phase == widgetrail::input::NavigationEventPhase::Pressed) {
                RetirePendingFocusGroupEntryForUserIntent(
                    widget, *snapshot, L"activation");
            }
            const bool isOpen = interactiveWidget;
            const auto* requestedSlider = exactActionRequest
                ? ValidateWidgetActionRequest(*exactActionRequest, *snapshot)
                : nullptr;
            if (exactActionRequest && !requestedSlider) {
                RejectWidgetActionRequest(
                    *exactActionRequest, L"stale admitted authority");
                return;
            }
            std::optional<std::wstring> visibleFocus;
            if (exactActionRequest) {
                visibleFocus = exactActionRequest->sourceElementId;
            } else if (exactHeldDecision) {
                if (exactHeldDecision->transportFocus)
                    visibleFocus = *exactHeldDecision->transportFocus;
            } else if (isOpen) {
                visibleFocus = widgetrail::input::ResolveVisibleFocusTarget(
                    interactionSession_.focusedElementId(),
                    snapshot->activeInputScopeId, lastWidgetRenderResult_);
            }
            const bool focusedNodeHandling = !exactHeldDecision ||
                exactHeldDecision->focusedNodeHandling;
            const bool requestedSliderAction = exactActionRequest.has_value();
            if (!exactHeldAction && isOpen && visibleFocus &&
                *visibleFocus != interactionSession_.focusedElementId()) {
                const auto focus = interactionSession_.MoveFocus(
                    widget, *snapshot, *visibleFocus);
                InvalidateWidgetFocusChange(
                    focus.priorFocus, focus.sliderDamageNodeIds);
            }
            if (physicalPress && focusedNodeHandling && isOpen && visibleFocus &&
                phase == widgetrail::input::NavigationEventPhase::Pressed &&
                interactionSession_.TransitionPressedPresentation(
                    widgetrail::input::PressedInputTransition::Begin,
                    snapshot, *visibleFocus, protocolButton)) {
                InvalidateRect(window_, nullptr, FALSE);
                UpdateWindow(window_);
            }
            if (focusedNodeHandling && isOpen && visibleFocus &&
                protocolButton == L"a" &&
                phase == widgetrail::input::NavigationEventPhase::Pressed &&
                OpenTextEntryModal(widget, *snapshot, *visibleFocus)) return;
            if (focusedNodeHandling && isOpen && visibleFocus) {
                const auto* focusedNode = widgetrail::input::FindNodeInInputScope(
                    *snapshot, *visibleFocus, snapshot->activeInputScopeId);
                if (focusedNode && TryDispatchNativeMediaAction(
                        widget, *snapshot, *focusedNode, protocolButton, phase))
                    return;
                if (focusedNode && TryInvokeLocalWidgetPackageImport(
                        *snapshot, *focusedNode, protocolButton, phase)) return;
            }
            if (isOpen && protocolButton != L"a" &&
                phase == widgetrail::input::NavigationEventPhase::Pressed) {
                const auto* scopeRoot = widgetrail::input::FindControllerShortcutScopeRoot(
                    snapshot->root, snapshot->activeInputScopeId);
                if (scopeRoot) {
                    const auto shortcut = widgetrail::input::ResolveHostControllerShortcut(
                        *scopeRoot,
                        visibleFocus ? std::optional<std::wstring_view>{*visibleFocus}
                                     : std::nullopt,
                        visibleFocus ? std::optional<std::wstring_view>{*visibleFocus}
                                     : std::nullopt,
                        protocolButton, L"pressed");
                    if (shortcut.status == widgetrail::input::
                            ControllerShortcutResolutionStatus::Resolved &&
                        shortcut.actionId ==
                            L"host.embeddedMediaSession.enterFullscreen") {
                        AttemptOverlayFullscreenMediaEntry(
                            widget, *snapshot, L"scope-shortcut", protocolButton);
                        return;
                    }
                }
            }
            const auto* descriptor = sessions_.FindDescriptor(widget);
            const auto* workerSnapshot = SnapshotFor(widget);
            const auto nativeSnapshotSequence = exactActionRequest
                ? exactActionRequest->snapshotSequence
                : snapshot->sequence;
            const auto correlationSequence = ++controllerSequence_;
            const std::wstring_view correlationFocus = exactActionRequest
                ? std::wstring_view{exactActionRequest->sourceElementId}
                : exactHeldDecision
                    ? exactHeldDecision->transportFocus.value_or(
                        std::wstring_view{})
                : isOpen && visibleFocus
                    ? std::wstring_view(*visibleFocus)
                    : std::wstring_view{};
            const std::wstring_view correlationScope = exactActionRequest
                ? std::wstring_view{exactActionRequest->inputScopeId}
                : std::wstring_view{snapshot->activeInputScopeId};
            AppendActionCorrelation(
                L"stage=host-admission sequence=" +
                std::to_wstring(correlationSequence) +
                L" context=" + (isOpen ? L"openWidget" : L"dashboardQuickAction") +
                L" button=" + std::wstring(protocolButton) +
                L" widget=" + std::wstring(widget) +
                L" focus=" + std::wstring(correlationFocus) +
                L" scope=" + std::wstring(correlationScope) +
                L" native-snapshot=" + std::to_wstring(nativeSnapshotSequence) +
                L" worker-snapshot=" +
                    std::to_wstring(workerSnapshot ? workerSnapshot->sequence : 0) +
                L" current-runtime=" +
                    (descriptor ? descriptor->runtimeGeneration : L"none"));
            const auto handled = bridge_.SendControllerInput(
                exactActionRequest
                    ? std::wstring_view{exactActionRequest->widgetId}
                    : widget,
                protocolButton,
                isOpen ? L"openWidget" : L"dashboardQuickAction",
                exactActionRequest
                    ? std::wstring_view{exactActionRequest->sourceElementId}
                    : exactHeldDecision
                    ? exactHeldDecision->transportFocus.value_or(
                        std::wstring_view{})
                    : isOpen && visibleFocus
                    ? std::wstring_view(*visibleFocus)
                    : std::wstring_view{},
                exactActionRequest
                    ? std::wstring_view{exactActionRequest->inputScopeId}
                    : std::wstring_view{snapshot->activeInputScopeId},
                nativeSnapshotSequence,
                correlationSequence, static_cast<long long>(GetTickCount64() * 1000),
                phase == widgetrail::input::NavigationEventPhase::Repeated
                    ? std::wstring_view{L"repeated"}
                    : std::wstring_view{L"pressed"},
                exactActionRequest
                    ? exactActionRequest->requestedValue
                    : std::nullopt,
                widgetrail::ControllerInputOrigin::PhysicalController,
                descriptor ? std::wstring_view{descriptor->runtimeGeneration}
                           : std::wstring_view{},
                {}, std::nullopt,
                exactActionRequest
                    ? std::wstring_view{exactActionRequest->actionId}
                    : std::wstring_view{});
            const auto replyCode = bridge_.lastControllerInputResultCode();
            AppendActionCorrelation(
                L"stage=host-reply sequence=" +
                std::to_wstring(correlationSequence) +
                L" result=" + (replyCode.empty() ? L"unknown" : replyCode),
                !handled
                    ? DiagnosticSeverity::Warning
                    : *handled
                        ? DiagnosticSeverity::Debug
                        : DiagnosticSeverity::Information);
            lastActionWidgetId_ = widget;
            if (!handled) {
                if (interactionSession_.TransitionPressedPresentation(
                        widgetrail::input::PressedInputTransition::Cancel,
                        nullptr, {}, protocolButton))
                    InvalidateRect(window_, nullptr, FALSE);
                if (requestedSliderAction &&
                    interactionSession_.CancelSliderAction(
                        *exactActionRequest,
                        GetTickCount64()).visualChanged) {
                    InvalidateWidgetSliderValues(
                        *snapshot, {requestedSlider->id}, true);
                }
                lastActionMessage_ = std::wstring(DisplayWidgetName(widget)) +
                                     L" input failed: " + bridge_.lastError();
            } else if (*handled) {
                lastActionMessage_ = std::wstring(DisplayWidgetName(widget)) +
                                     L" handled " + std::wstring(button);
                // Slider handling acknowledges queue admission only. Keep the
                // optimistic pixels until the widget's post-action invalidation
                // requests the authoritative snapshot.
                if (!requestedSliderAction)
                    RefreshAndApplyPresentation([&] { RefreshWidgetSnapshot(widget); });
            } else {
                lastActionMessage_ = std::wstring(DisplayWidgetName(widget)) +
                                     L" has no " + std::wstring(button) + L" action here";
                if (requestedSliderAction &&
                    interactionSession_.CancelSliderAction(
                        *exactActionRequest,
                        GetTickCount64()).visualChanged) {
                    InvalidateWidgetSliderValues(
                        *snapshot, {requestedSlider->id}, true);
                }
                snapshot = InteractionSnapshotFor(widget);
                const auto unhandledContext = snapshot &&
                    std::wstring_view(snapshot->activeInputScopeId) ==
                        widgetrail::input::RootInputScope(*snapshot)
                    ? widgetrail::input::ControllerActionContext::RootWidgetScope
                    : widgetrail::input::ControllerActionContext::NestedWidgetScope;
                if (isOpen && widgetrail::input::RouteUnhandledControllerAction(
                        unhandledContext, button) ==
                        widgetrail::input::ControllerActionRoute::HostBackToDashboard) {
                    Dispatch(widgetrail::Command::SampleWidgetBack);
                    return;
                }
            }
            lastActionExpiresAt_ = GetTickCount64() + 1800;
            AppendDiagnostic(lastActionMessage_);
            if (!requestedSliderAction)
                InvalidateRect(window_, nullptr, FALSE);
            return;
        }

        if (exactActionRequest) {
            RejectWidgetActionRequest(
                *exactActionRequest, L"runtime authority changed");
            return;
        }

        if (interactiveWidget &&
            widgetrail::input::RouteUnhandledControllerAction(
                widgetrail::input::ControllerActionContext::RootWidgetScope, button) ==
                widgetrail::input::ControllerActionRoute::HostBackToDashboard) {
            Dispatch(widgetrail::Command::SampleWidgetBack);
            return;
        }

        lastActionWidgetId_ = widget;
        lastActionMessage_ = std::wstring(DisplayWidgetName(widget)) +
                             L": forwarded " + std::wstring(button);
        lastActionExpiresAt_ = GetTickCount64() + 1800;
        AppendDiagnostic(L"Widget action " + lastActionMessage_);
        InvalidateRect(window_, nullptr, FALSE);
    }

    [[nodiscard]] widgetrail::input::TextEntryModalTheme CurrentTextEntryTheme() const {
        const auto colorOr = [](const std::optional<widgetrail::NativeColor>& value,
                                const widgetrail::NativeColor fallback) {
            return GdiColor(value.value_or(fallback));
        };
        const widgetrail::NativeColor defaultText{
            0xF7 / 255.0F, 0xF7 / 255.0F, 0xFA / 255.0F, 1.0F};
        const widgetrail::NativeColor defaultSecondary{
            0x9B / 255.0F, 0xA3 / 255.0F, 0xB3 / 255.0F, 1.0F};
        const widgetrail::NativeColor defaultControl{
            0x24 / 255.0F, 0x2A / 255.0F, 0x37 / 255.0F, 1.0F};
        const auto& appearance = appearanceState_.current();
        widgetrail::input::TextEntryModalTheme theme;
        theme.canvas = GdiColor(effectiveCanvasBackground_);
        theme.panel = GdiColor(effectivePanelBackground_);
        theme.control = colorOr(trayItemStyle_.background(), defaultControl);
        theme.controlFocused = colorOr(
            trayItemSelectedFocusedStyle_.background(), kDefaultAccent);
        theme.text = colorOr(bodyStyle_.foreground(), defaultText);
        theme.secondaryText = colorOr(hintStyle_.foreground(), defaultSecondary);
        theme.focus = colorOr(
            trayItemFocusedStyle_.outlineColor(), defaultText);
        theme.interfaceScale = appearance ? appearance->interfaceScale : 1.0;
        theme.textScale = appearance ? appearance->textScale : 1.0;
        theme.fontFamily = bodyStyle_.fontFamily().empty()
            ? L"Segoe UI" : bodyStyle_.fontFamily();
        return theme;
    }

    bool OpenTextEntryModal(
        const std::wstring_view widget,
        const widgetrail::WidgetSnapshot& snapshot,
        const std::wstring_view nodeId) {
        const auto* node = widgetrail::input::FindNodeInInputScope(
            snapshot, nodeId, snapshot.activeInputScopeId);
        if (!node || !node->isTextEntry) return false;
        const auto* descriptor = sessions_.FindDescriptor(widget);
        if (!descriptor) return true;
        const auto request = widgetrail::input::CaptureTextEntryActionRequest(
            widget, descriptor->runtimeGeneration,
            descriptor->presentationGeneration, snapshot, nodeId);
        if (!request) return true;

        (void)interactionSession_.MoveFocus(
            widget, snapshot, request->nodeId);
        const std::wstring exactFocusToRestore{
            interactionSession_.focusedElementId()};
        const bool protectedWifi = descriptor->protectedWifiPromptSupported &&
            request->actionId == L"wifi.connect.protected";
        const bool sensitive = request->inputKind == L"sensitive";
        const auto modalTitle = protectedWifi
            ? std::wstring(L"Password for ") + request->placeholder
            : request->placeholder;
        // Establish the modal semantic/input scope before its HWND becomes
        // visible. The prior focus identity is retained only for validated
        // restoration after the modal reaches a terminal outcome.
        (void)interactionSession_.RetirePresentations();
        interactionSession_.ClearFocus();
        ClearAccessibilityTree();
        InvalidateRect(window_, nullptr, FALSE);
        if (chromeWindow_) {
            EnableWindow(chromeWindow_, FALSE);
            InvalidateRect(chromeWindow_, nullptr, FALSE);
        }
        if (backdropWindow_) EnableWindow(backdropWindow_, FALSE);
        textEntryModalAuthority_ = TextEntryModalAuthority{
            request->widgetId,
            request->runtimeGeneration,
            request->presentationGeneration,
        };
        textEntryControllerPhase_ =
            TextEntryControllerPhase::AwaitingEntryNeutral;
        auto modalResult = textEntryModal_.Show(
            instance_, window_, request->value,
            modalTitle, request->maximumLength, sensitive || protectedWifi,
            CurrentTextEntryTheme());
        // The terminal modal sample (B/RT/mouse/keyboard) and every other held
        // controller category remain quarantined until one complete neutral
        // frame has been consumed by the sole platform frame owner.
        textEntryControllerPhase_ =
            TextEntryControllerPhase::AwaitingExitNeutral;
        textEntryModalAuthority_.reset();
        if (backdropWindow_ && IsWindow(backdropWindow_))
            EnableWindow(backdropWindow_, TRUE);
        if (chromeWindow_ && IsWindow(chromeWindow_))
            EnableWindow(chromeWindow_, TRUE);
        bool actionDispatched{};
        if (modalResult.outcome == widgetrail::input::TextEntryModalOutcome::Committed &&
            modalResult.committedText) {
            const auto* currentDescriptor = sessions_.FindDescriptor(request->widgetId);
            const auto* currentSnapshot = InteractionSnapshotFor(request->widgetId);
            const auto target = currentDescriptor && currentSnapshot
                ? widgetrail::input::ResolveTextEntryActionTarget(
                    *request,
                    state_.surface() == widgetrail::Surface::Widget &&
                        state_.focusRegion() == widgetrail::FocusRegion::Widget,
                    state_.activeWidget(),
                    currentDescriptor->runtimeGeneration,
                    currentDescriptor->presentationGeneration,
                    *currentSnapshot)
                : std::nullopt;
            if (target) {
                std::optional<bool> handled;
                if (protectedWifi) {
                    const auto result = bridge_.ConnectProtectedWifi(
                        request->widgetId,
                        request->runtimeGeneration,
                        target->sourceElementId,
                        modalResult.committedText->view());
                    handled = result.has_value();
                    lastActionWidgetId_ = request->widgetId;
                    lastActionMessage_ = result && *result == L"connecting"
                        ? L"Connecting to protected Wi-Fi"
                        : L"Protected Wi-Fi connection was not started";
                    lastActionExpiresAt_ = GetTickCount64() + 3000;
                } else {
                    handled = bridge_.SendAction(
                        request->widgetId, target->actionId, target->sourceElementId,
                        target->activeInputScopeId,
                        std::wstring_view(
                            modalResult.committedText->view().data(),
                            modalResult.committedText->view().size()));
                }
                actionDispatched = handled && *handled;
                if (handled && *handled) {
                    RefreshAndApplyPresentation([&] {
                        RefreshWidgetSnapshot(request->widgetId);
                    });
                }
            }
            lastActionWidgetId_ = request->widgetId;
            lastActionMessage_ = actionDispatched
                ? L"Entry sent" : L"Could not send entry";
            lastActionExpiresAt_ = GetTickCount64() + 3000;
            modalResult.committedText->clear();
        }
        if (state_.surface() == widgetrail::Surface::Widget) {
            const std::wstring_view activeWidget = state_.activeWidget();
            const auto* currentSnapshot = InteractionSnapshotFor(activeWidget);
            const auto exactVisible = currentSnapshot && activeWidget == request->widgetId
                ? widgetrail::input::ResolveVisibleFocusTarget(
                    exactFocusToRestore,
                    currentSnapshot->activeInputScopeId,
                    lastWidgetRenderResult_)
                : std::nullopt;
            if (currentSnapshot && exactVisible && *exactVisible == exactFocusToRestore) {
                (void)interactionSession_.MoveFocus(
                    activeWidget, *currentSnapshot, exactFocusToRestore);
            } else {
                RestoreFocusForActiveSurface(activeWidget);
            }
        }
        const auto outcome = [&] {
            switch (modalResult.outcome) {
            case widgetrail::input::TextEntryModalOutcome::Failed: return L"failed";
            case widgetrail::input::TextEntryModalOutcome::Cancelled: return L"cancel";
            case widgetrail::input::TextEntryModalOutcome::Closed: return L"close";
            case widgetrail::input::TextEntryModalOutcome::Committed: return L"enter";
            }
            return L"unknown";
        }();
        std::wstring preservation{L"not-applicable"};
        if (modalResult.outcome != widgetrail::input::TextEntryModalOutcome::Committed) {
            const auto* currentSnapshot = InteractionSnapshotFor(request->widgetId);
            const auto* currentNode = currentSnapshot
                ? widgetrail::input::FindNodeInInputScope(
                    *currentSnapshot, request->nodeId,
                    currentSnapshot->activeInputScopeId)
                : nullptr;
            preservation = !currentNode || !currentNode->isTextEntry
                ? L"unavailable"
                : currentNode->textEntryValue == request->value
                    ? L"preserved" : L"changed";
        }
        AppendDiagnostic(
            L"Text entry modal outcome=" + std::wstring(outcome) +
            L" action-dispatched=" + (actionDispatched ? L"true" : L"false") +
            L" committed-value=" + preservation +
            L" focus=" + (interactionSession_.focusedElementId().empty() ? L"none" : interactionSession_.focusedElementId()));
        (void)SetFocus(window_);
        InvalidateRect(window_, nullptr, FALSE);
        return true;
    }

    [[nodiscard]] bool GraphicsResourcesReady() const noexcept {
        return backgroundBrush_ && cardBrush_ && solidCardBrush_ && textBrush_ && secondaryBrush_ &&
               dashboardSecondaryBrush_ && dashboardTextBrush_ && accentBrush_ &&
               successBrush_ && trayItemBrush_ && trayItemTextBrush_ &&
               traySelectedBrush_ && traySelectedTextBrush_ &&
               selectedTextBrush_ && focusBrush_ && titleFormat_ && bodyFormat_ &&
               hintFormat_ && iconFormat_;
    }

    bool EnsureGraphicsResources(
        const unsigned int requestedWidth = 0,
        const unsigned int requestedHeight = 0,
        const UINT requestedDpi = 0) {
        if (renderTarget_ && GraphicsResourcesReady()) return true;
        RECT client{};
        GetClientRect(window_, &client);
        const auto size = D2D1::SizeU(
            requestedWidth != 0
                ? requestedWidth
                : static_cast<UINT32>(client.right - client.left),
            requestedHeight != 0
                ? requestedHeight
                : static_cast<UINT32>(client.bottom - client.top));
        if (!renderTarget_) {
            if (FAILED(d2dFactory_->CreateHwndRenderTarget(
                    D2D1::RenderTargetProperties(),
                    D2D1::HwndRenderTargetProperties(window_, size),
                    hwndRenderTarget_.ReleaseAndGetAddressOf()))) {
                return false;
            }
            renderTarget_ = hwndRenderTarget_;
        }
        const float windowDpi = static_cast<float>(
            requestedDpi != 0 ? requestedDpi : GetDpiForWindow(window_));
        renderTarget_->SetDpi(windowDpi > 0 ? windowDpi : 96.0F,
                              windowDpi > 0 ? windowDpi : 96.0F);

        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        if (const auto metrics = widgetrail::ComputeOverlayRenderMetrics(
                static_cast<int>(size.width), static_cast<int>(size.height),
                windowDpi > 0 ? static_cast<UINT>(windowDpi) : 96U,
                interfaceScale)) {
            RebuildShellStyles(metrics->viewportWidthDip, metrics->viewportHeightDip);
        }

        const auto colorOr = [](const std::optional<widgetrail::NativeColor>& value,
                                const widgetrail::NativeColor fallback) {
            return value.value_or(fallback);
        };
        const widgetrail::NativeColor defaultText{0xF7 / 255.0F, 0xF7 / 255.0F,
                                            0xFA / 255.0F, 1};
        const widgetrail::NativeColor defaultSecondary{0x9B / 255.0F, 0xA3 / 255.0F,
                                                 0xB3 / 255.0F, 1};
        const widgetrail::NativeColor defaultSuccess{0x45 / 255.0F, 0xD4 / 255.0F,
                                               0x83 / 255.0F, 1};
        const auto PaintedLayer = [](const std::optional<widgetrail::NativeColor>& configured,
                                     const widgetrail::NativeColor fallback,
                                     const float opacity) {
            auto result = configured.value_or(fallback);
            result.alpha *= std::isfinite(opacity)
                ? std::clamp(opacity, 0.0F, 1.0F)
                : 1.0F;
            return result;
        };
        const auto trayBackground = PaintedLayer(
            trayStyle_.background(), effectiveCanvasBackground_, trayStyle_.opacity());
        const auto panelBackground = PaintedLayer(
            panelStyle_.background(), kDefaultPanel, panelStyle_.opacity());
        const auto foreground = colorOr(titleStyle_.foreground(),
            colorOr(canvasStyle_.foreground(), defaultText));
        const auto secondary = colorOr(hintStyle_.foreground(),
            colorOr(bodyStyle_.foreground(), defaultSecondary));
        const auto dashboardForeground = colorOr(
            dashboardTitleStyle_.foreground(),
            colorOr(canvasStyle_.foreground(), defaultText));
        const auto dashboardSecondary = colorOr(
            dashboardHintStyle_.foreground(),
            colorOr(canvasStyle_.foreground(), defaultSecondary));
        const auto selectedBackground = trayItemSelectedFocusedStyle_.background()
            ? PaintedLayer(trayItemSelectedFocusedStyle_.background(), kDefaultAccent,
                           trayItemSelectedFocusedStyle_.opacity())
            : PaintedLayer(trayItemSelectedStyle_.background(), kDefaultAccent,
                           trayItemSelectedStyle_.opacity());
        const auto itemBackground = PaintedLayer(
            trayItemStyle_.background(), trayBackground, trayItemStyle_.opacity());
        const auto traySelectedBackground = PaintedLayer(
            trayItemSelectedStyle_.background(), kDefaultAccent,
            trayItemSelectedStyle_.opacity());
        const auto trayItemForeground = colorOr(
            trayItemStyle_.foreground(), dashboardForeground);
        const auto traySelectedForeground = colorOr(
            trayItemSelectedStyle_.foreground(), foreground);
        const auto selectedForeground = colorOr(
            trayItemSelectedFocusedStyle_.foreground(),
            colorOr(trayItemSelectedStyle_.foreground(), foreground));
        const auto focusColor = colorOr(
            trayItemSelectedFocusedStyle_.outlineColor(),
            colorOr(trayItemFocusedStyle_.outlineColor(), kDefaultAccent));

        renderTarget_->CreateSolidColorBrush(
            D2DColor(trayBackground), backgroundBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(panelBackground), cardBrush_.ReleaseAndGetAddressOf());
        auto solidPanelBackground = panelBackground;
        solidPanelBackground.alpha = 1.0F;
        renderTarget_->CreateSolidColorBrush(
            D2DColor(solidPanelBackground), solidCardBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(foreground), textBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(secondary), secondaryBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(dashboardForeground),
            dashboardTextBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(dashboardSecondary),
            dashboardSecondaryBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(selectedBackground), accentBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(colorOr(statusStyle_.foreground(), defaultSuccess)),
            successBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(itemBackground), trayItemBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(trayItemForeground),
            trayItemTextBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(traySelectedBackground),
            traySelectedBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(traySelectedForeground),
            traySelectedTextBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(selectedForeground), selectedTextBrush_.ReleaseAndGetAddressOf());
        renderTarget_->CreateSolidColorBrush(
            D2DColor(focusColor), focusBrush_.ReleaseAndGetAddressOf());

        const auto hasProperty = [&](const std::wstring_view role,
                                     const std::wstring_view property) {
            return ShellComputedStyle(role).contains(std::wstring(property));
        };
        const float textScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->textScale)
            : 1.0F;
        const auto fontSize = [&](const std::wstring_view role,
                                  const widgetrail::NativeRenderStyle& style,
                                  const float fallback) {
            return (hasProperty(role, L"font-size") ? style.fontSizePx() : fallback) *
                   textScale;
        };
        const auto fontFamily = [&](const std::wstring_view role,
                                    const widgetrail::NativeRenderStyle& style,
                                    const wchar_t* fallback) -> const wchar_t* {
            return hasProperty(role, L"font-family") ? style.fontFamily().c_str() : fallback;
        };
        const auto fontWeight = [&](const std::wstring_view role,
                                    const widgetrail::NativeRenderStyle& style,
                                    const DWRITE_FONT_WEIGHT fallback) {
            return hasProperty(role, L"font-weight")
                ? static_cast<DWRITE_FONT_WEIGHT>(std::clamp(style.fontWeight(), 100, 950))
                : fallback;
        };

        writeFactory_->CreateTextFormat(fontFamily(L"title", titleStyle_, L"Segoe UI Variable Display"), nullptr,
                                        fontWeight(L"title", titleStyle_, DWRITE_FONT_WEIGHT_SEMI_BOLD),
                                        DWRITE_FONT_STYLE_NORMAL,
                                        DWRITE_FONT_STRETCH_NORMAL,
                                        fontSize(L"title", titleStyle_, 25.0F), L"en-us",
                                        titleFormat_.ReleaseAndGetAddressOf());
        writeFactory_->CreateTextFormat(fontFamily(L"body", bodyStyle_, L"Segoe UI Variable Text"), nullptr,
                                        fontWeight(L"body", bodyStyle_, DWRITE_FONT_WEIGHT_NORMAL),
                                        DWRITE_FONT_STYLE_NORMAL,
                                        DWRITE_FONT_STRETCH_NORMAL,
                                        fontSize(L"body", bodyStyle_, 16.0F), L"en-us",
                                        bodyFormat_.ReleaseAndGetAddressOf());
        writeFactory_->CreateTextFormat(fontFamily(L"hint", hintStyle_, L"Segoe UI Variable Text"), nullptr,
                                        fontWeight(L"hint", hintStyle_, DWRITE_FONT_WEIGHT_SEMI_BOLD),
                                        DWRITE_FONT_STYLE_NORMAL,
                                        DWRITE_FONT_STRETCH_NORMAL,
                                        fontSize(L"hint", hintStyle_, 14.0F), L"en-us",
                                        hintFormat_.ReleaseAndGetAddressOf());
        if (hintFormat_) {
            // Host chrome has one reserved row. Wrapping controller mappings
            // into a clipped second line is never a valid responsive state;
            // DrawWidgetFooter selects a shorter semantic guide instead.
            hintFormat_->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP);
        }
        writeFactory_->CreateTextFormat(L"Segoe UI Variable Display", nullptr,
                                        DWRITE_FONT_WEIGHT_SEMI_BOLD,
                                        DWRITE_FONT_STYLE_NORMAL,
                                        DWRITE_FONT_STRETCH_NORMAL,
                                        23.0F, L"en-us", iconFormat_.ReleaseAndGetAddressOf());
        if (iconFormat_) {
            iconFormat_->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
            iconFormat_->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        }
        panelCornerRadius_ = hasProperty(L"panel", L"corner-radius")
            ? panelStyle_.cornerRadiusPx() : 18.0F;
        trayCornerRadius_ = hasProperty(L"tray", L"corner-radius")
            ? trayStyle_.cornerRadiusPx() : 22.0F;
        trayItemCornerRadius_ = hasProperty(L"tray-item", L"corner-radius")
            ? trayItemStyle_.cornerRadiusPx() : 16.0F;
        focusOutlineWidth_ = hasProperty(L"tray-item:selected:focused", L"outline-width")
            ? trayItemSelectedFocusedStyle_.outlineWidthPx()
            : hasProperty(L"tray-item:focused", L"outline-width")
                ? trayItemFocusedStyle_.outlineWidthPx() : 2.0F;
        return GraphicsResourcesReady();
    }

    void DiscardGraphicsResources(const bool discardRenderTarget = true) {
        if (declarativeRenderer_) declarativeRenderer_->DiscardTargetResources();
        lastFallbackPresentationCheckpointKey_.clear();
        pendingContentRenderPlan_.reset();
        activeContentRenderPlan_.reset();
        // Hit and focus rectangles are valid only for the render target's
        // logical viewport. Never dispatch controller focus through geometry
        // retained across a resize, DPI migration, or appearance rebuild.
        lastWidgetRenderResult_ = {};
        committedWidgetVisualState_.reset();
        committedFullscreenPresentation_.reset();
        iconFormat_.Reset();
        hintFormat_.Reset();
        bodyFormat_.Reset();
        titleFormat_.Reset();
        focusBrush_.Reset();
        selectedTextBrush_.Reset();
        traySelectedTextBrush_.Reset();
        traySelectedBrush_.Reset();
        trayItemTextBrush_.Reset();
        trayItemBrush_.Reset();
        accentBrush_.Reset();
        successBrush_.Reset();
        secondaryBrush_.Reset();
        textBrush_.Reset();
        dashboardSecondaryBrush_.Reset();
        dashboardTextBrush_.Reset();
        cardBrush_.Reset();
        solidCardBrush_.Reset();
        backgroundBrush_.Reset();
        if (discardRenderTarget) {
            renderTarget_.Reset();
            hwndRenderTarget_.Reset();
        }
    }

    void DrawTextLine(std::wstring_view text,
                      IDWriteTextFormat* format,
                      const D2D1_RECT_F& rectangle,
                      ID2D1Brush* brush) const {
        renderTarget_->DrawTextW(text.data(), static_cast<UINT32>(text.size()), format,
                                 rectangle, brush, D2D1_DRAW_TEXT_OPTIONS_CLIP);
    }

    [[nodiscard]] std::optional<float> MeasureGuideTextWidth(
        const std::wstring_view text) const {
        if (!writeFactory_ || !hintFormat_ || text.empty() ||
            text.size() > static_cast<std::size_t>(UINT32_MAX)) return std::nullopt;
        ComPtr<IDWriteTextLayout> layout;
        if (FAILED(writeFactory_->CreateTextLayout(
                text.data(), static_cast<UINT32>(text.size()), hintFormat_.Get(),
                16384.0F, 256.0F, layout.ReleaseAndGetAddressOf()))) {
            return std::nullopt;
        }
        DWRITE_TEXT_METRICS metrics{};
        if (FAILED(layout->GetMetrics(&metrics))) return std::nullopt;
        return metrics.widthIncludingTrailingWhitespace;
    }

    enum class CompositionPaintLayer {
        Combined,
        Content,
        Guide,
        Tray,
    };

    void DrawCurrentFrame(
        const unsigned int width,
        const unsigned int height,
        const UINT dpi,
        const POINT updateOffset,
        const RECT updateArea,
        const CompositionPaintLayer layer = CompositionPaintLayer::Combined,
        const widgetrail::shell::TrayLayout* trayLayout = nullptr,
        const widgetrail::declarative::Rect* guideBounds = nullptr,
        std::optional<widgetrail::CompositionUpdateRasterMapping>*
            rasterMapping = nullptr,
        const bool deferFixedChromeAccessibilityPublication = false) {
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto metrics = widgetrail::ComputeOverlayRenderMetrics(
            static_cast<int>(width), static_cast<int>(height),
            dpi != 0 ? dpi : 96U, interfaceScale);
        if (!metrics) return;

        const bool compositionRaster = compositionSurface_.available();
        std::optional<widgetrail::CompositionUpdateRasterMapping> mapping;
        D2D1_RECT_F updateClip{};
        D2D1_POINT_2F sceneTranslation{};
        float sceneScale = metrics->interfaceScale;
        if (compositionRaster) {
            // BeginDraw's guard and atlas offset are physical pixels. Draw the
            // complete logical scene in a 96-DPI pixel target so full and
            // bounded updates share one exact raster origin; monitor DPI and
            // interface scale are represented only by scenePixelsPerDip.
            mapping = widgetrail::PlanCompositionUpdateRasterMapping(
                updateArea, updateOffset, metrics->physicalPixelsPerDip);
            renderTarget_->SetDpi(96.0F, 96.0F);
            updateClip = D2D1::RectF(
                static_cast<float>(updateOffset.x),
                static_cast<float>(updateOffset.y),
                static_cast<float>(updateOffset.x +
                    updateArea.right - updateArea.left),
                static_cast<float>(updateOffset.y +
                    updateArea.bottom - updateArea.top));
            sceneScale = mapping->scenePixelsPerDip;
            sceneTranslation = mapping->sceneTranslationPixels;
        } else {
            const auto updateOffsetDip =
                widgetrail::NormalizeCompositionUpdateOffset(updateOffset, dpi);
            const auto requestedOriginDip =
                widgetrail::NormalizeCompositionUpdateOffset(
                    POINT{updateArea.left, updateArea.top}, dpi);
            const auto requestedExtentDip =
                widgetrail::NormalizeCompositionUpdateOffset(
                    POINT{
                        updateArea.right - updateArea.left,
                        updateArea.bottom - updateArea.top},
                    dpi);
            updateClip = D2D1::RectF(
                updateOffsetDip.x,
                updateOffsetDip.y,
                updateOffsetDip.x + requestedExtentDip.x,
                updateOffsetDip.y + requestedExtentDip.y);
            sceneTranslation = {
                updateOffsetDip.x - requestedOriginDip.x,
                updateOffsetDip.y - requestedOriginDip.y,
            };
        }
        if (rasterMapping) *rasterMapping = mapping;
        renderTarget_->SetTransform(D2D1::Matrix3x2F::Identity());
        renderTarget_->PushAxisAlignedClip(
            updateClip,
            D2D1_ANTIALIAS_MODE_ALIASED);
        renderTarget_->Clear(compositionSurface_.available()
            ? D2D1::ColorF(0.0F, 0.0F, 0.0F, 0.0F)
            : D2DColor(kSafeCanvasFallback));
        renderTarget_->SetTransform(
            D2D1::Matrix3x2F::Scale(
                sceneScale, sceneScale) *
            D2D1::Matrix3x2F::Translation(
                sceneTranslation.x, sceneTranslation.y));

        const auto finishUpdate = [&] {
            renderTarget_->SetTransform(D2D1::Matrix3x2F::Identity());
            renderTarget_->PopAxisAlignedClip();
        };

        const bool overlayFullscreen = OverlayFullscreenMediaRequested();
        if (overlayFullscreen && layer == CompositionPaintLayer::Tray) {
            finishUpdate();
            return;
        }
        if (overlayFullscreen && layer == CompositionPaintLayer::Guide && guideBounds) {
            openWidgetAccessibility_ = {};
            pendingActionFailureAccessibilityProjection_.reset();
            ClearAccessibilityTree();
            finishUpdate();
            return;
        }
        if (overlayFullscreen && layer == CompositionPaintLayer::Content) {
            ClearAccessibilityTree();
            finishUpdate();
            return;
        }

        if (layer == CompositionPaintLayer::Tray && trayLayout) {
            DrawIconStrip(
                metrics->viewportWidthDip, metrics->viewportHeightDip,
                nullptr, nullptr, trayLayout, false);
            finishUpdate();
            return;
        }
        if (layer == CompositionPaintLayer::Guide && guideBounds) {
            if (state_.surface() == widgetrail::Surface::Widget) {
                widgetrail::OverlaySurfaceGeometry localGuideGeometry;
                localGuideGeometry.panelWidth = guideBounds->width;
                localGuideGeometry.footerHeight = guideBounds->height;
                DrawWidgetFooter(localGuideGeometry, guideBounds);
                if (accessibilityActive_ && trayLayout) {
                    PublishTrayAccessibility(
                        *trayLayout, width, height, nullptr,
                        deferFixedChromeAccessibilityPublication);
                }
            }
            finishUpdate();
            return;
        }

        if (state_.surface() == widgetrail::Surface::Widget) {
            DrawWidget(metrics->viewportWidthDip, metrics->viewportHeightDip,
                       metrics->physicalPixelsPerDip, layer,
                       trayLayout, guideBounds);
        } else {
            declarativeMotionActive_ = false;
            DrawDashboard(metrics->viewportWidthDip, metrics->viewportHeightDip,
                          layer, trayLayout);
        }
        finishUpdate();
    }

    struct CompositionLayerGeometry final {
        unsigned int width{};
        unsigned int height{};
    };

    struct CompositionFrameSet final {
        enum class ContentTransportWork {
            None,
            BoundedUpdate,
            FullSurface,
        } contentTransportWork{ContentTransportWork::None};

        struct StageTiming final {
            std::uint64_t beginFrameMicroseconds{};
            std::uint64_t resourceSetupMicroseconds{};
            std::uint64_t drawCurrentFrameMicroseconds{};
            std::uint64_t endFrameMicroseconds{};
        } stageTiming;
        std::optional<widgetrail::DeclarativeRenderTiming> declarativeTiming;
        std::optional<widgetrail::IncrementalPresentationWork> contentRendererWork;
        std::optional<widgetrail::CompositionUpdateRasterMapping>
            contentRasterMapping;
        std::vector<widgetrail::OverlayCompositionSurface::Frame> frames;
        std::optional<widgetrail::OverlayCompositionSurface::
            BackgroundPresentation> backgroundPresentation;
        std::optional<std::uint64_t> backgroundObservationTransaction;
        bool retireBackground{};
        std::optional<widgetrail::shell::RetainedTrayState> trayState;
        struct OverlayFullscreenGeometry final {
            EmbeddedMediaSessionKey sessionKey;
            widgetrail::media::EndpointGeometry geometry;
        };
        std::optional<OverlayFullscreenGeometry> overlayFullscreenGeometry;
        std::wstring guideKey;
    };

    [[nodiscard]] static std::wstring SlowCompositionStageDiagnostic(
        const std::uint64_t totalMicroseconds,
        const CompositionFrameSet& frames) {
        const bool hasFocusFollowSummary = frames.declarativeTiming &&
            !frames.declarativeTiming->focusFollowSummary.empty();
        if (totalMicroseconds <= kSlowCompositionFrameMicroseconds &&
            !hasFocusFollowSummary) {
            return {};
        }
        const auto measured =
            frames.stageTiming.beginFrameMicroseconds +
            frames.stageTiming.resourceSetupMicroseconds +
            frames.stageTiming.drawCurrentFrameMicroseconds +
            frames.stageTiming.endFrameMicroseconds;
        const auto other = totalMicroseconds > measured
            ? totalMicroseconds - measured
            : 0;
        std::wstring diagnostic =
            L" slow-stage-begin-us=" +
            std::to_wstring(frames.stageTiming.beginFrameMicroseconds) +
            L" slow-stage-resources-us=" +
            std::to_wstring(frames.stageTiming.resourceSetupMicroseconds) +
            L" slow-stage-draw-us=" +
            std::to_wstring(frames.stageTiming.drawCurrentFrameMicroseconds) +
            L" slow-stage-end-us=" +
            std::to_wstring(frames.stageTiming.endFrameMicroseconds) +
            L" slow-stage-other-us=" + std::to_wstring(other);
        if (frames.declarativeTiming) {
            const auto& renderer = *frames.declarativeTiming;
            diagnostic +=
                L" slow-render-total-us=" +
                std::to_wstring(renderer.totalMicroseconds) +
                L" slow-render-prepare-us=" +
                std::to_wstring(renderer.preparationMicroseconds) +
                L" slow-render-presentation-us=" +
                std::to_wstring(renderer.presentationMicroseconds) +
                L" slow-render-clip-us=" +
                std::to_wstring(renderer.clipSetupMicroseconds) +
                L" slow-render-node-us=" +
                std::to_wstring(renderer.nodeDrawMicroseconds) +
                L" slow-render-focus-us=" +
                std::to_wstring(renderer.deferredFocusMicroseconds) +
                L" slow-render-finalize-us=" +
                std::to_wstring(renderer.finalizationMicroseconds);
            if (!renderer.focusFollowSummary.empty())
                diagnostic += L" " + renderer.focusFollowSummary;
        }
        diagnostic += L" slow-content-transport=";
        switch (frames.contentTransportWork) {
        case CompositionFrameSet::ContentTransportWork::None:
            diagnostic += L"none";
            break;
        case CompositionFrameSet::ContentTransportWork::BoundedUpdate:
            diagnostic += L"bounded-update";
            break;
        case CompositionFrameSet::ContentTransportWork::FullSurface:
            diagnostic += L"full-surface";
            break;
        }
        diagnostic += L" slow-renderer-work=";
        if (!frames.contentRendererWork) {
            diagnostic += L"none";
        } else {
            switch (*frames.contentRendererWork) {
            case widgetrail::IncrementalPresentationWork::NoRaster:
                diagnostic += L"no-raster";
                break;
            case widgetrail::IncrementalPresentationWork::PaintOnly:
                diagnostic += L"retained-paint-only";
                break;
            case widgetrail::IncrementalPresentationWork::LocalLayout:
                diagnostic += L"retained-local-layout";
                break;
            case widgetrail::IncrementalPresentationWork::FullRaster:
                diagnostic += L"full-raster-fallback";
                break;
            }
        }
        return diagnostic;
    }

    [[nodiscard]] static bool ShouldPromoteLargeCompositionUpdate(
        const RECT update,
        const unsigned int surfaceWidth,
        const unsigned int surfaceHeight) noexcept {
        if (update.right <= update.left || update.bottom <= update.top ||
            surfaceWidth == 0 || surfaceHeight == 0) return false;
        const auto updateArea =
            static_cast<std::uint64_t>(update.right - update.left) *
            static_cast<std::uint64_t>(update.bottom - update.top);
        const auto surfaceArea =
            static_cast<std::uint64_t>(surfaceWidth) *
            static_cast<std::uint64_t>(surfaceHeight);
        // PID 23628's eight renderer stalls all used 52.6-54.3% physical
        // updates. Small focus/slider damage retains the incremental path;
        // a half-surface update instead takes the consistently bounded full
        // DirectComposition allocation/raster path.
        return updateArea >= (surfaceArea + 1U) / 2U;
    }

    // One visible overlay session owns this small chrome raster and its
    // absolute screen anchor. It is intentionally independent of whichever
    // destination widget currently owns the content viewport.
    struct FixedChromeAnchor final {
        HMONITOR monitor{};
        RECT monitorArea{};
        RECT workArea{};
        UINT dpi{96};
        float interfaceScale{1.0F};
        float textScale{1.0F};
        std::uint64_t appearanceRevision{};
        std::vector<std::wstring> catalogOrder;
        RECT intendedWindowBounds{};
        RECT actualWindowBounds{};
        FixedChromePlacementReason reason{FixedChromePlacementReason::None};
    };

    struct CompositionChromeSession final {
        unsigned int canvasWidth{};
        unsigned int canvasHeight{};
        unsigned int guideWidth{};
        unsigned int guideHeight{};
        unsigned int trayWidth{};
        unsigned int trayHeight{};
        unsigned int trayMenuHeadroom{};
        unsigned int trayFocusPadding{};
        float trayCapacityWidthDip{};
        UINT dpi{};
        std::uint64_t appearanceRevision{};
        float pixelsPerDip{};
        widgetrail::declarative::Rect guideBounds;
        RECT trayClientBounds{};
        RECT guideClientBounds{};
        LONG renderedGuideContentBottom{};
        RECT windowBounds{};
        widgetrail::shell::FixedChromeSessionKey key;
    };

    [[nodiscard]] float CurrentTrayViewportWidthDip(
        const float fallback) const noexcept {
        return compositionChromeSession_ &&
                compositionChromeSession_->pixelsPerDip > 0.0F
            ? static_cast<float>(compositionChromeSession_->trayWidth) /
                compositionChromeSession_->pixelsPerDip
            : fallback;
    }

    void RequestFixedChromeAnchorRefresh(
        const FixedChromePlacementReason reason) {
        fixedChromeAnchor_.reset();
        compositionChromeSession_.reset();
        pendingFixedChromePlacementReason_ = reason;
    }

    [[nodiscard]] static std::wstring FormatPhysicalBounds(
        const RECT& bounds) {
        return std::to_wstring(bounds.left) + L"," +
            std::to_wstring(bounds.top) + L"," +
            std::to_wstring(bounds.right - bounds.left) + L"," +
            std::to_wstring(bounds.bottom - bounds.top);
    }

    bool AppendFallbackPlacementDiagnostic(
        const std::wstring_view phase,
        const RECT& workArea,
        const UINT dpi,
        const float interfaceScale,
        const widgetrail::OverlayPlacement& targetPlacement) {
        const auto validBounds = [](const RECT& bounds) noexcept {
            return bounds.right > bounds.left && bounds.bottom > bounds.top;
        };
        if (!window_ || !validBounds(workArea) || targetPlacement.width <= 0 ||
            targetPlacement.height <= 0) {
            return false;
        }

        const RECT targetBounds{
            targetPlacement.x,
            targetPlacement.y,
            targetPlacement.x + targetPlacement.width,
            targetPlacement.y + targetPlacement.height,
        };
        if (!validBounds(targetBounds) || targetBounds.left < workArea.left ||
            targetBounds.top < workArea.top || targetBounds.right > workArea.right ||
            targetBounds.bottom > workArea.bottom) {
            return false;
        }

        RECT windowBounds{};
        RECT clientBounds{};
        POINT clientOrigin{};
        if (!GetWindowRect(window_, &windowBounds) ||
            !GetClientRect(window_, &clientBounds) ||
            !ClientToScreen(window_, &clientOrigin) || !validBounds(windowBounds) ||
            clientBounds.right <= clientBounds.left ||
            clientBounds.bottom <= clientBounds.top) {
            return false;
        }
        const RECT clientScreenBounds{
            clientOrigin.x,
            clientOrigin.y,
            clientOrigin.x + clientBounds.right - clientBounds.left,
            clientOrigin.y + clientBounds.bottom - clientBounds.top,
        };
        if (!validBounds(clientScreenBounds)) return false;

        const std::wstring_view widgetId = state_.surface() == widgetrail::Surface::Widget
            ? state_.activeWidget()
            : state_.selectedWidget();
        const auto* descriptor = widgetId.empty()
            ? nullptr : sessions_.FindDescriptor(widgetId);
        const auto* snapshot = widgetId.empty()
            ? nullptr : SnapshotFor(widgetId);
        AppendDiagnostic(
            L"Fallback placement mode=hwnd-fallback phase=" + std::wstring(phase) +
            L" work=" + FormatPhysicalBounds(workArea) +
            L" dpi=" + std::to_wstring(dpi) +
            L" interface-scale=" + std::to_wstring(interfaceScale) +
            L" target=" + FormatPhysicalBounds(targetBounds) +
            L" content-window=" + FormatPhysicalBounds(windowBounds) +
            L" content-client-screen=" + FormatPhysicalBounds(clientScreenBounds) +
            L" widget=" + (widgetId.empty() ? L"none" : std::wstring(widgetId)) +
            L" instance=" + (snapshot ? snapshot->instanceId : L"none") +
            L" runtime=" + (descriptor ? descriptor->runtimeGeneration : L"none") +
            L" presentation=" +
                (descriptor ? descriptor->presentationGeneration : L"none") +
            L" sequence=" + std::to_wstring(snapshot ? snapshot->sequence : -1));
        return true;
    }

    void AppendFallbackPresentationCheckpoint() {
        if (compositionSurface_.available() || !window_ ||
            state_.surface() != widgetrail::Surface::Widget) {
            return;
        }
        const std::wstring_view widgetId = state_.activeWidget();
        if (widgetId.empty()) return;
        const auto* descriptor = sessions_.FindDescriptor(widgetId);
        const auto* snapshot = SnapshotFor(widgetId);
        const auto presentation = sessions_.Presentation(widgetId);
        if (!descriptor || !snapshot || snapshot->instanceId.empty() ||
            snapshot->sequence <= 0 || descriptor->runtimeGeneration.empty() ||
            descriptor->presentationGeneration.empty() ||
            presentation.authority != widgetrail::WidgetPresentationAuthority::Current ||
            presentation.snapshot != snapshot) {
            return;
        }

        const HMONITOR monitor = MonitorFromWindow(window_, MONITOR_DEFAULTTONEAREST);
        MONITORINFO monitorInfo{sizeof(monitorInfo)};
        if (!monitor || !GetMonitorInfoW(monitor, &monitorInfo)) return;
        const UINT dpi = GetDpiForWindow(window_);
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto extent = DesiredPresentationExtentDip();
        const auto target = ComputePlatformPlacement(
            monitorInfo.rcWork, dpi,
            static_cast<float>(extent.widthDip) * interfaceScale,
            static_cast<float>(extent.heightDip) * interfaceScale);
        if (!target) return;

        RECT windowBounds{};
        RECT clientBounds{};
        POINT clientOrigin{};
        if (!GetWindowRect(window_, &windowBounds) ||
            !GetClientRect(window_, &clientBounds) ||
            !ClientToScreen(window_, &clientOrigin)) {
            return;
        }
        const RECT clientScreenBounds{
            clientOrigin.x,
            clientOrigin.y,
            clientOrigin.x + clientBounds.right - clientBounds.left,
            clientOrigin.y + clientBounds.bottom - clientBounds.top,
        };
        const std::wstring checkpointKey =
            std::wstring(widgetId) + L"|" + snapshot->instanceId + L"|" +
            descriptor->runtimeGeneration + L"|" + descriptor->presentationGeneration +
            L"|" + std::to_wstring(snapshot->sequence) + L"|" +
            FormatPhysicalBounds(monitorInfo.rcWork) + L"|" + std::to_wstring(dpi) +
            L"|" + std::to_wstring(interfaceScale) + L"|" +
            std::to_wstring(target->x) + L"," + std::to_wstring(target->y) + L"," +
            std::to_wstring(target->width) + L"," + std::to_wstring(target->height) +
            L"|" + FormatPhysicalBounds(windowBounds) + L"|" +
            FormatPhysicalBounds(clientScreenBounds);
        if (checkpointKey == lastFallbackPresentationCheckpointKey_) return;
        if (AppendFallbackPlacementDiagnostic(
                L"presentation-checkpoint", monitorInfo.rcWork, dpi, interfaceScale,
                *target)) {
            lastFallbackPresentationCheckpointKey_ = checkpointKey;
        }
    }

    [[nodiscard]] std::optional<RECT> ActualChromeWindowBounds() const {
        RECT actual{};
        return chromeWindow_ && GetWindowRect(chromeWindow_, &actual)
            ? std::optional<RECT>{actual}
            : std::nullopt;
    }

    [[nodiscard]] std::optional<RECT> ProjectChromeClientBoundsToScreen(
        const RECT& clientBounds) const {
        RECT client{};
        if (!chromeWindow_ || !GetClientRect(chromeWindow_, &client) ||
            clientBounds.left < client.left ||
            clientBounds.top < client.top ||
            clientBounds.right > client.right ||
            clientBounds.bottom > client.bottom ||
            clientBounds.right <= clientBounds.left ||
            clientBounds.bottom <= clientBounds.top) {
            return std::nullopt;
        }
        POINT topLeft{clientBounds.left, clientBounds.top};
        POINT bottomRight{clientBounds.right, clientBounds.bottom};
        if (!ClientToScreen(chromeWindow_, &topLeft) ||
            !ClientToScreen(chromeWindow_, &bottomRight)) {
            return std::nullopt;
        }
        return RECT{
            topLeft.x, topLeft.y, bottomRight.x, bottomRight.y,
        };
    }

    [[nodiscard]] std::optional<RECT> ProjectTrayBoundsToScreen(
        const widgetrail::declarative::Rect& bounds) const {
        if (!compositionChromeSession_ ||
            compositionChromeSession_->pixelsPerDip <= 0.0F) {
            return std::nullopt;
        }
        const auto& session = *compositionChromeSession_;
        const RECT physical{
            session.trayClientBounds.left + static_cast<LONG>(std::floor(
                bounds.x * session.pixelsPerDip)),
            session.trayClientBounds.top + static_cast<LONG>(std::floor(
                bounds.y * session.pixelsPerDip)),
            session.trayClientBounds.left + static_cast<LONG>(std::ceil(
                (bounds.x + bounds.width) * session.pixelsPerDip)),
            session.trayClientBounds.top + static_cast<LONG>(std::ceil(
                (bounds.y + bounds.height) * session.pixelsPerDip)),
        };
        return ProjectChromeClientBoundsToScreen(physical);
    }

    [[nodiscard]] bool IsCurrentFixedChromeHit(
        const POINT screenPoint) const {
        if (!compositionChromeSession_ ||
            compositionChromeSession_->pixelsPerDip <= 0.0F) return false;
        const auto trayLayout = CurrentCompositionTrayLayout();
        if (!trayLayout) return false;
        const auto traySurface = ProjectChromeClientBoundsToScreen(
            compositionChromeSession_->trayClientBounds);
        if (!traySurface) return false;
        const float x = static_cast<float>(screenPoint.x - traySurface->left) /
            compositionChromeSession_->pixelsPerDip;
        const float y = static_cast<float>(screenPoint.y - traySurface->top) /
            compositionChromeSession_->pixelsPerDip;
        return IsTrayInteractivePoint(
            *trayLayout, x, y, CurrentTrayViewportWidthDip(0.0F));
    }

    [[nodiscard]] bool EnsureFixedChromeAnchor() {
        if (fixedChromeAnchor_) return true;
        if (pendingFixedChromePlacementReason_ ==
            FixedChromePlacementReason::None) {
            AppendDiagnostic(
                L"Fixed chrome anchor selection rejected reason=content-route");
            return false;
        }

        const HWND remembered = reinterpret_cast<HWND>(
            WidgetRailOverlayPlatformRememberedForegroundTarget(platform_));
        const HWND targetWindow = reinterpret_cast<HWND>(
            WidgetRailOverlayPlatformResolveForegroundTarget(
                platform_, reinterpret_cast<std::uintptr_t>(window_),
                PlatformBoolean(remembered && IsWindow(remembered))));
        const HMONITOR monitor = MonitorFromWindow(
            targetWindow, MONITOR_DEFAULTTONEAREST);
        MONITORINFO monitorInfo{sizeof(monitorInfo)};
        if (!monitor || !GetMonitorInfoW(monitor, &monitorInfo)) {
            AppendDiagnostic(
                L"Unable to resolve fixed chrome monitor for placement reason=" +
                std::wstring(FixedChromePlacementReasonName(
                    pendingFixedChromePlacementReason_)));
            return false;
        }
        UINT dpi = 96;
        UINT dpiY = 96;
        if (FAILED(GetDpiForMonitor(
                monitor, MDT_EFFECTIVE_DPI, &dpi, &dpiY)) ||
            dpi == 0 || dpiY == 0) {
            dpi = 96;
        }
        const auto& appearance = appearanceState_.current();
        FixedChromeAnchor anchor;
        anchor.monitor = monitor;
        anchor.monitorArea = monitorInfo.rcMonitor;
        anchor.workArea = monitorInfo.rcWork;
        anchor.dpi = dpi;
        anchor.interfaceScale = appearance
            ? static_cast<float>(appearance->interfaceScale) : 1.0F;
        anchor.textScale = appearance
            ? static_cast<float>(appearance->textScale) : 1.0F;
        anchor.appearanceRevision = static_cast<std::uint64_t>(
            std::max<long long>(0, appearance ? appearance->revision : 0));
        anchor.catalogOrder = state_.order();
        anchor.reason = pendingFixedChromePlacementReason_;
        fixedChromeAnchor_ = std::move(anchor);
        return true;
    }

    [[nodiscard]] static RECT PhysicalCrop(
        const widgetrail::declarative::Rect& logical,
        const float pixelsPerDip,
        const unsigned int fullWidth,
        const unsigned int fullHeight) noexcept {
        RECT result{
            static_cast<LONG>(std::floor(logical.x * pixelsPerDip)),
            static_cast<LONG>(std::floor(logical.y * pixelsPerDip)),
            static_cast<LONG>(std::ceil((logical.x + logical.width) * pixelsPerDip)),
            static_cast<LONG>(std::ceil((logical.y + logical.height) * pixelsPerDip)),
        };
        result.left = std::clamp(result.left, 0L, static_cast<LONG>(fullWidth));
        result.top = std::clamp(result.top, 0L, static_cast<LONG>(fullHeight));
        result.right = std::clamp(
            result.right, result.left + 1, static_cast<LONG>(fullWidth));
        result.bottom = std::clamp(
            result.bottom, result.top + 1, static_cast<LONG>(fullHeight));
        return result;
    }

    [[nodiscard]] bool EnsureCompositionChromeSession() {
        if (compositionChromeSession_) return true;
        if (!fixedChromeAnchor_ ||
            pendingFixedChromePlacementReason_ ==
                FixedChromePlacementReason::None) {
            AppendDiagnostic(
                L"Fixed chrome session creation rejected reason=content-route");
            return false;
        }
        auto& anchor = *fixedChromeAnchor_;
        const UINT effectiveDpi = anchor.dpi != 0 ? anchor.dpi : 96U;
        const float interfaceScale = anchor.interfaceScale;
        const RECT& workArea = anchor.workArea;
        const unsigned int workWidth =
            static_cast<unsigned int>(workArea.right - workArea.left);
        const unsigned int workHeight =
            static_cast<unsigned int>(workArea.bottom - workArea.top);
        widgetrail::shell::FixedChromeSessionKey key{
            workArea,
            effectiveDpi,
            interfaceScale,
            anchor.appearanceRevision,
            anchor.catalogOrder,
        };
        const auto metrics = widgetrail::ComputeOverlayRenderMetrics(
            static_cast<int>(workWidth), static_cast<int>(workHeight),
            effectiveDpi, interfaceScale);
        if (!metrics) return false;
        const auto policyLayout = widgetrail::shell::ComputeTrayLayout(
            metrics->viewportWidthDip, metrics->viewportHeightDip,
            state_.order().size(), state_.selectedSlot());
        if (!policyLayout) return false;
        const float trayCapacityWidthDip =
            widgetrail::shell::ComputeTrayCapacityWidth(
                metrics->viewportWidthDip);
        if (trayCapacityWidthDip <= 0.0F) return false;

        constexpr float kGuideHeightDip = 58.0F;
        constexpr float kGuideToTrayGapDip = 46.0F;
        const LONG focusPadding = std::max(2L, static_cast<LONG>(std::ceil(
            (focusOutlineWidth_ + 2.0F) * metrics->physicalPixelsPerDip)));
        const LONG trayWidth = std::max(
            1L, static_cast<LONG>(std::ceil(
                trayCapacityWidthDip *
                metrics->physicalPixelsPerDip))) + focusPadding * 2;
        const LONG trayHeight = std::max(
            1L, static_cast<LONG>(std::ceil(
                policyLayout->stripBounds.height *
                metrics->physicalPixelsPerDip))) + focusPadding * 2;
        const LONG trayMenuHeadroom = std::max(
            1L, static_cast<LONG>(std::ceil(
                kTrayContextMenuHeadroomDip *
                metrics->physicalPixelsPerDip)));
        const LONG traySurfaceHeight = trayHeight + trayMenuHeadroom;
        const auto guideGeometry = widgetrail::ComputeOverlaySurfaceGeometry(
            metrics->viewportWidthDip, metrics->viewportHeightDip,
            static_cast<float>(kPanelWidth));
        const float guideWidthDip = std::max(
            policyLayout->stripBounds.width,
            guideGeometry
                ? guideGeometry->panelWidth
                : policyLayout->stripBounds.width);
        const LONG guideWidth = std::max(
            1L, static_cast<LONG>(std::ceil(
                guideWidthDip * metrics->physicalPixelsPerDip)));
        const LONG guideHeight = std::max(
            1L, static_cast<LONG>(std::ceil(
                kGuideHeightDip * metrics->physicalPixelsPerDip)));
        const LONG guideToTrayGap = std::max(
            0L, static_cast<LONG>(std::lround(
                kGuideToTrayGapDip * metrics->physicalPixelsPerDip)));
        const LONG chromeWidth = std::max(trayWidth, guideWidth);
        LONG chromeHeight =
            trayMenuHeadroom + guideHeight + guideToTrayGap + trayHeight;
        if (chromeWidth <= 0 || chromeHeight <= 0) return false;

        CompositionChromeSession session;
        session.canvasWidth = static_cast<unsigned int>(chromeWidth);
        session.canvasHeight = static_cast<unsigned int>(chromeHeight);
        session.guideWidth = static_cast<unsigned int>(guideWidth);
        session.guideHeight = static_cast<unsigned int>(guideHeight);
        session.trayWidth = static_cast<unsigned int>(trayWidth);
        session.trayHeight = static_cast<unsigned int>(traySurfaceHeight);
        session.trayMenuHeadroom =
            static_cast<unsigned int>(trayMenuHeadroom);
        session.trayFocusPadding = static_cast<unsigned int>(focusPadding);
        session.trayCapacityWidthDip = trayCapacityWidthDip;
        session.dpi = effectiveDpi;
        session.key = std::move(key);
        session.pixelsPerDip = metrics->physicalPixelsPerDip;
        const LONG guideLeft = (chromeWidth - guideWidth) / 2;
        const LONG trayLeft = (chromeWidth - trayWidth) / 2;
        session.guideClientBounds = {
            guideLeft, trayMenuHeadroom,
            guideLeft + guideWidth, trayMenuHeadroom + guideHeight,
        };
        session.windowBounds = widgetrail::shell::ComputeFixedChromeWindowBounds(
            workArea, chromeWidth, chromeHeight);
        if (state_.surface() == widgetrail::Surface::Widget) {
            const float guideHeightDip = static_cast<float>(guideHeight) /
                session.pixelsPerDip;
            session.renderedGuideContentBottom =
                session.guideClientBounds.top + static_cast<LONG>(std::lround(
                    WidgetGuideContentBottomDip(guideHeightDip) *
                    session.pixelsPerDip));
        } else {
            const auto dashboardPlacement = ComputePlatformPlacement(
                workArea, effectiveDpi,
                static_cast<float>(kPanelWidth) * interfaceScale,
                static_cast<float>(kDashboardHeight) * interfaceScale);
            if (!dashboardPlacement) return false;
            const int panelToGuideGap = static_cast<int>(std::lround(
                kPanelToGuideGapDip * session.pixelsPerDip));
            const auto dashboardBounds =
                widgetrail::shell::ComputeContentWindowBoundsAboveGuide(
                    workArea,
                    session.windowBounds.top + session.guideClientBounds.top,
                    dashboardPlacement->width, dashboardPlacement->height,
                    panelToGuideGap);
            if (!dashboardBounds) return false;
            const float dashboardHeightDip =
                static_cast<float>(dashboardPlacement->height) /
                session.pixelsPerDip;
            session.renderedGuideContentBottom =
                dashboardBounds->top - session.windowBounds.top +
                static_cast<LONG>(std::lround(
                    DashboardGuideContentBottomDip(dashboardHeightDip) *
                    session.pixelsPerDip));
        }
        session.renderedGuideContentBottom = std::clamp(
            session.renderedGuideContentBottom, 0L, chromeHeight);
        const auto localTrayLayout = ComputeCompositionTrayLayout(session);
        if (!localTrayLayout) return false;
        const LONG localStripTop = static_cast<LONG>(std::floor(
            localTrayLayout->stripBounds.y * session.pixelsPerDip));
        const LONG localStripBottom = static_cast<LONG>(std::ceil(
            (localTrayLayout->stripBounds.y + localTrayLayout->stripBounds.height) *
            session.pixelsPerDip));
        const LONG stripHeight = std::max(1L, localStripBottom - localStripTop);
        const LONG freeBandHeight = std::max(
            0L, chromeHeight - session.renderedGuideContentBottom);
        LONG centeredStripTop = session.renderedGuideContentBottom +
            std::max(0L, (freeBandHeight - stripHeight) / 2L);
        LONG requestedTrayTop = centeredStripTop - localStripTop;
        if (requestedTrayTop < 0) {
            // The tray child keeps its bounded menu headroom above the strip.
            // Grow the short chrome window upward only as much as that
            // headroom requires, and shift the guide by the same amount so
            // its established screen anchor and content placement do not move.
            const LONG availableWorkHeight = std::max(
                0L, workArea.bottom - workArea.top);
            const LONG topExpansion = std::min(
                -requestedTrayTop,
                std::max(0L, availableWorkHeight - chromeHeight));
            if (topExpansion > 0) {
                chromeHeight += topExpansion;
                session.canvasHeight = static_cast<unsigned int>(chromeHeight);
                session.guideClientBounds.top += topExpansion;
                session.guideClientBounds.bottom += topExpansion;
                session.renderedGuideContentBottom += topExpansion;
                centeredStripTop += topExpansion;
                requestedTrayTop += topExpansion;
                session.windowBounds =
                    widgetrail::shell::ComputeFixedChromeWindowBounds(
                        workArea, chromeWidth, chromeHeight);
            }
        }
        const LONG maximumTrayTop = std::max(0L, chromeHeight - traySurfaceHeight);
        const LONG trayTop = std::clamp(requestedTrayTop, 0L, maximumTrayTop);
        session.trayClientBounds = {
            trayLeft, trayTop,
            trayLeft + trayWidth, trayTop + traySurfaceHeight,
        };
        const LONG visibleGapAbove = trayTop + localStripTop -
            session.renderedGuideContentBottom;
        const LONG visibleGapBelow = chromeHeight -
            (trayTop + localStripBottom);
        session.guideBounds = {
            0.0F,
            0.0F,
            static_cast<float>(guideWidth) / session.pixelsPerDip,
            static_cast<float>(guideHeight) / session.pixelsPerDip,
        };
        if (!widgetrail::shell::ApplyFixedChromeWindow(
                window_, chromeWindow_, session.windowBounds, true)) {
            AppendDiagnostic(L"Fixed chrome placement failed error=" +
                             std::to_wstring(GetLastError()));
            return false;
        }
        RECT actualWindowBounds{};
        if (!GetWindowRect(chromeWindow_, &actualWindowBounds)) {
            AppendDiagnostic(
                L"Fixed chrome applied rectangle unavailable error=" +
                std::to_wstring(GetLastError()));
            return false;
        }
        RECT actualClient{};
        const auto guideScreen = ProjectChromeClientBoundsToScreen(
            session.guideClientBounds);
        const auto trayScreen = ProjectChromeClientBoundsToScreen(
            session.trayClientBounds);
        if (!GetClientRect(chromeWindow_, &actualClient) ||
            actualClient.right - actualClient.left != chromeWidth ||
            actualClient.bottom - actualClient.top != chromeHeight ||
            !guideScreen || !trayScreen) {
            AppendDiagnostic(
                L"Fixed chrome client layout did not fit its applied HWND");
            return false;
        }
        const widgetrail::OverlayCompositionSurface::ChromePresentation chrome{
            static_cast<float>(session.guideClientBounds.left),
            static_cast<float>(session.guideClientBounds.top),
            static_cast<float>(session.trayClientBounds.left),
            static_cast<float>(session.trayClientBounds.top),
        };
        widgetrail::OverlayCompositionSurface::CommitTiming chromeTiming;
        const HRESULT chromeResult =
            compositionSurface_.CommitChromePresentation(chrome, chromeTiming);
        if (FAILED(chromeResult)) {
            AppendDiagnostic(
                L"Fixed chrome child presentation failed hresult=" +
                std::to_wstring(static_cast<unsigned long>(chromeResult)));
            return false;
        }
        ++fixedChromePlacementCount_;
        anchor.intendedWindowBounds = session.windowBounds;
        anchor.actualWindowBounds = actualWindowBounds;
        const bool exact = EqualRect(
            &anchor.intendedWindowBounds, &anchor.actualWindowBounds) != FALSE;
        AppendDiagnostic(
            L"Fixed chrome placement reason=" +
            std::wstring(FixedChromePlacementReasonName(anchor.reason)) +
            L" monitor=" + std::to_wstring(
                reinterpret_cast<std::uintptr_t>(anchor.monitor)) +
            L" intended=" + FormatPhysicalBounds(anchor.intendedWindowBounds) +
            L" actual=" + FormatPhysicalBounds(anchor.actualWindowBounds) +
            L" client=" + FormatPhysicalBounds(actualClient) +
            L" guide-client=" +
            FormatPhysicalBounds(session.guideClientBounds) +
            L" tray-client=" +
            FormatPhysicalBounds(session.trayClientBounds) +
            L" guide-content-bottom=" +
            std::to_wstring(session.renderedGuideContentBottom) +
            L" tray-visual-gaps=" + std::to_wstring(visibleGapAbove) +
            L"," + std::to_wstring(visibleGapBelow) +
            L" guide-screen=" + FormatPhysicalBounds(*guideScreen) +
            L" tray-screen=" + FormatPhysicalBounds(*trayScreen) +
            L" exact=" + (exact ? std::wstring{L"true"} : L"false") +
            L" chrome-commit-us=" +
            std::to_wstring(chromeTiming.commitMicroseconds) +
            L" placement-count=" +
            std::to_wstring(fixedChromePlacementCount_));
        if (!exact) return false;
        compositionChromeSession_ = std::move(session);
        pendingFixedChromePlacementReason_ = FixedChromePlacementReason::None;
        return true;
    }

    [[nodiscard]] std::optional<widgetrail::OverlayPlacement>
    AnchorContentPlacementToChrome(
        widgetrail::OverlayPlacement placement) const {
        if (!fixedChromeAnchor_ || !compositionChromeSession_ ||
            placement.width <= 0 || placement.height <= 0) {
            return std::nullopt;
        }
        const auto guide = ProjectChromeClientBoundsToScreen(
            compositionChromeSession_->guideClientBounds);
        if (!guide) return std::nullopt;

        const int panelToGuideGap = static_cast<int>(std::lround(
            kPanelToGuideGapDip * fixedChromeAnchor_->dpi / 96.0F *
            fixedChromeAnchor_->interfaceScale));
        const auto bounds = widgetrail::shell::ComputeContentWindowBoundsAboveGuide(
            fixedChromeAnchor_->workArea, guide->top,
            placement.width, placement.height, panelToGuideGap);
        if (!bounds) return std::nullopt;
        placement.x = bounds->left;
        placement.y = bounds->top;
        return placement;
    }

    [[nodiscard]] std::optional<widgetrail::shell::TrayLayout>
    ComputeCompositionTrayLayout(
        const CompositionChromeSession& session) const {
        const float interfaceScale =
            static_cast<float>(session.key.interfaceScale);
        const unsigned int contentWidth = session.trayWidth -
            std::min(session.trayWidth, session.trayFocusPadding * 2U);
        const unsigned int contentHeight = session.trayHeight -
            std::min(session.trayHeight, session.trayFocusPadding * 2U);
        if (contentWidth == 0 || contentHeight == 0) return std::nullopt;
        const auto metrics = widgetrail::ComputeOverlayRenderMetrics(
            static_cast<int>(contentWidth), static_cast<int>(contentHeight),
            session.dpi, interfaceScale);
        if (!metrics) return std::nullopt;
        const float inset = static_cast<float>(session.trayFocusPadding) /
            session.pixelsPerDip;
        const float menuHeadroom =
            static_cast<float>(session.trayMenuHeadroom) /
            session.pixelsPerDip;
        auto layout = widgetrail::shell::ComputeTrayLayout(
            session.trayCapacityWidthDip, metrics->viewportHeightDip,
            state_.order().size(), state_.selectedSlot(),
            widgetrail::shell::TrayBand{
                menuHeadroom, metrics->viewportHeightDip},
            widgetrail::shell::TrayWidthBasis::ExactCapacity);
        if (!layout) return std::nullopt;
        const auto offset = [inset](widgetrail::declarative::Rect& bounds) {
            bounds.x += inset;
            bounds.y += inset;
        };
        offset(layout->stripBounds);
        for (auto& tile : layout->tiles) offset(tile.bounds);
        if (layout->previousOverflow) offset(layout->previousOverflow->bounds);
        if (layout->nextOverflow) offset(layout->nextOverflow->bounds);
        return layout;
    }

    [[nodiscard]] std::optional<widgetrail::shell::TrayLayout>
    CurrentCompositionTrayLayout() const {
        return compositionChromeSession_
            ? ComputeCompositionTrayLayout(*compositionChromeSession_)
            : std::nullopt;
    }

    [[nodiscard]] std::wstring CurrentGuidePaintKey(
        const unsigned int width,
        const unsigned int height,
        const UINT dpi) {
        if (state_.surface() != widgetrail::Surface::Widget) return L"dashboard:none";
        std::wstring key = std::wstring{state_.activeWidget()} + L"\n" +
            std::to_wstring(width) + L"x" + std::to_wstring(height) + L"\n" +
            std::to_wstring(dpi) + L"\n" +
            std::to_wstring(appearanceState_.current()
                ? appearanceState_.current()->revision : 0) + L"\n" +
            std::to_wstring(static_cast<int>(state_.focusRegion()));
        key += OverlayFullscreenMediaRequested()
            ? L"\noverlay-fullscreen=true"
            : L"\noverlay-fullscreen=false";
        if (state_.focusRegion() == widgetrail::FocusRegion::Tray) {
            if (const auto status = DashboardStatus()) key += L"\n" + *status;
            key += L"\n" + DashboardHint(static_cast<float>(width));
        } else {
            if (const auto status = OpenWidgetStatus()) key += L"\n" + *status;
            key += L"\n" + ResolveOpenWidgetGuide(
                static_cast<float>(width)).accessible;
            if (trayContextMenu_) {
                key += L"\ntray-context=" + trayContextMenu_->widgetId + L":" +
                    std::to_wstring(trayContextMenu_->selectedItem);
            }
            if (widgetContextMenu_) {
                key += L"\nwidget-context=" + widgetContextMenu_->sourceNodeId + L":" +
                    std::to_wstring(widgetContextMenu_->selectedItem);
            }
            if (const auto* snapshot = GuideSnapshotFor(state_.activeWidget()))
                key += L"\n" + snapshot->activeInputScopeId;
        }
        return key;
    }

    [[nodiscard]] std::optional<widgetrail::shell::RetainedTrayState>
    CurrentTrayPaintState(
        const widgetrail::shell::TrayLayout& layout,
        const unsigned int width,
        const unsigned int height,
        const float pixelsPerDip) const {
        widgetrail::shell::RetainedTrayState state;
        state.width = width;
        state.height = height;
        state.appearanceRevision = static_cast<std::uint64_t>(std::max<long long>(
            0, appearanceState_.current() ? appearanceState_.current()->revision : 0));
        const auto relative = [&](const widgetrail::declarative::Rect& bounds) {
            RECT result = PhysicalCrop(
                bounds, pixelsPerDip,
                state.width, state.height);
            result.left = std::clamp(result.left, 0L, static_cast<LONG>(state.width));
            result.top = std::clamp(result.top, 0L, static_cast<LONG>(state.height));
            result.right = std::clamp(
                result.right, result.left + 1, static_cast<LONG>(state.width));
            result.bottom = std::clamp(
                result.bottom, result.top + 1, static_cast<LONG>(state.height));
            return result;
        };
        if (layout.previousOverflow) {
            state.items.push_back({
                relative(layout.previousOverflow->bounds),
                L"overflow:previous:" +
                    std::to_wstring(layout.previousOverflow->targetSlot),
                false, false});
        }
        for (const auto& tile : layout.tiles) {
            if (tile.slot >= state_.order().size()) return std::nullopt;
            const bool selected = tile.slot == state_.selectedSlot();
            state.items.push_back({
                relative(tile.bounds),
                std::wstring{state_.order()[tile.slot]} + L":" +
                    std::to_wstring(static_cast<int>(
                        DisplayWidgetIcon(state_.order()[tile.slot]))),
                selected,
                selected &&
                    state_.focusRegion() == widgetrail::FocusRegion::Tray,
            });
        }
        if (layout.nextOverflow) {
            state.items.push_back({
                relative(layout.nextOverflow->bounds),
                L"overflow:next:" +
                    std::to_wstring(layout.nextOverflow->targetSlot),
                false, false});
        }
        return state;
    }

    bool RenderCompositionLayer(
        const unsigned int fullWidth,
        const unsigned int fullHeight,
        const UINT dpi,
        const widgetrail::OverlayCompositionSurface::Layer layer,
        const CompositionPaintLayer paintLayer,
        const CompositionLayerGeometry& geometry,
        const RECT* update,
        CompositionFrameSet& set,
        const widgetrail::shell::TrayLayout* trayLayout = nullptr,
        const widgetrail::declarative::Rect* guideBounds = nullptr,
        const bool deferFixedChromeAccessibilityPublication = false) {
        widgetrail::OverlayCompositionSurface::Frame frame;
        const auto beginStarted = std::chrono::steady_clock::now();
        HRESULT result = compositionSurface_.BeginFrame(
            layer, geometry.width, geometry.height,
            0.0F, 0.0F, update, frame);
        set.stageTiming.beginFrameMicroseconds +=
            static_cast<std::uint64_t>(
                std::chrono::duration_cast<std::chrono::microseconds>(
                    std::chrono::steady_clock::now() - beginStarted).count());
        if (FAILED(result)) {
            AppendDiagnostic(
                L"DirectComposition child BeginDraw failed hresult=" +
                std::to_wstring(static_cast<unsigned long>(result)));
            return false;
        }

        renderTarget_ = frame.target;
        const auto resourcesStarted = std::chrono::steady_clock::now();
        renderTarget_->SetDpi(
            static_cast<float>(dpi != 0 ? dpi : 96U),
            static_cast<float>(dpi != 0 ? dpi : 96U));
        if (!EnsureGraphicsResources(fullWidth, fullHeight, dpi)) {
            renderTarget_.Reset();
            compositionSurface_.AbandonFrame(frame);
            AppendDiagnostic(
                L"DirectComposition child resources could not be created");
            return false;
        }
        set.stageTiming.resourceSetupMicroseconds +=
            static_cast<std::uint64_t>(
                std::chrono::duration_cast<std::chrono::microseconds>(
                    std::chrono::steady_clock::now() - resourcesStarted).count());
        currentCompositionRenderTiming_.reset();
        const auto drawStarted = std::chrono::steady_clock::now();
        std::optional<widgetrail::CompositionUpdateRasterMapping> rasterMapping;
        DrawCurrentFrame(
            fullWidth, fullHeight, dpi, frame.updateOffset, frame.updateArea,
            paintLayer, trayLayout, guideBounds,
            paintLayer == CompositionPaintLayer::Content
                ? &rasterMapping
                : nullptr,
            deferFixedChromeAccessibilityPublication);
        if (paintLayer == CompositionPaintLayer::Content)
            set.contentRasterMapping = rasterMapping;
        if (paintLayer == CompositionPaintLayer::Content &&
            currentCompositionRenderTiming_) {
            set.declarativeTiming = currentCompositionRenderTiming_;
        }
        currentCompositionRenderTiming_.reset();
        set.stageTiming.drawCurrentFrameMicroseconds +=
            static_cast<std::uint64_t>(
                std::chrono::duration_cast<std::chrono::microseconds>(
                    std::chrono::steady_clock::now() - drawStarted).count());
        renderTarget_.Reset();
        const auto endStarted = std::chrono::steady_clock::now();
        result = compositionSurface_.EndFrame(frame);
        set.stageTiming.endFrameMicroseconds +=
            static_cast<std::uint64_t>(
                std::chrono::duration_cast<std::chrono::microseconds>(
                    std::chrono::steady_clock::now() - endStarted).count());
        if (FAILED(result)) {
            AppendDiagnostic(
                L"DirectComposition child EndDraw failed hresult=" +
                std::to_wstring(static_cast<unsigned long>(result)));
            return false;
        }
        set.frames.push_back(std::move(frame));
        return true;
    }

    bool RenderCompositionFrames(
        const unsigned int width,
        const unsigned int height,
        const UINT dpi,
        const float destinationOffsetX,
        const float destinationOffsetY,
        const widgetrail::OverlayPlacement& containerPlacement,
        CompositionFrameSet& set,
        std::uint64_t& drawMicroseconds) {
        static_cast<void>(destinationOffsetX);
        static_cast<void>(destinationOffsetY);
        static_cast<void>(containerPlacement);
        const auto started = std::chrono::steady_clock::now();
        set = {};
        if (!compositionChromeSession_) return false;
        const auto& chromeSession = *compositionChromeSession_;
        const auto trayLayout = CurrentCompositionTrayLayout();
        if (!trayLayout) return false;

        const bool replaceContent = !compositionSurface_.hasContent(
            widgetrail::OverlayCompositionSurface::Layer::Content) ||
            compositionSurface_.width() != width ||
            compositionSurface_.height() != height;
        if (replaceContent) DiscardGraphicsResources();
        const bool retainPendingRefreshPixels =
            !replaceContent && HasExactRefreshRetainedVisualCheckpoint();
        const CompositionLayerGeometry contentGeometry{
            width, height,
        };
        std::optional<RECT> contentUpdate;
        std::optional<widgetrail::IncrementalPresentationPlan> renderPlan =
            pendingContentRenderPlan_;
        const float interfaceScale = appearanceState_.current()
            ? static_cast<float>(appearanceState_.current()->interfaceScale)
            : 1.0F;
        const auto metrics = widgetrail::ComputeOverlayRenderMetrics(
            static_cast<int>(width), static_cast<int>(height),
            dpi != 0 ? dpi : 96U, interfaceScale);
        const auto geometry = metrics
            ? widgetrail::ComputePanelLocalSurfaceGeometry(
                metrics->viewportWidthDip, metrics->viewportHeightDip)
            : std::nullopt;
        if (pendingWidgetPresentationImpact_ && declarativeRenderer_ &&
            state_.surface() == widgetrail::Surface::Widget &&
            IsBridgeWidget(state_.activeWidget())) {
            const auto* snapshot = InteractionSnapshotFor(state_.activeWidget());
            renderPlan = snapshot && geometry
                ? declarativeRenderer_->PlanPresentationUpdate(
                    *snapshot,
                    *pendingWidgetPresentationImpact_,
                    {
                        geometry->widgetViewportX,
                        geometry->widgetViewportY,
                        geometry->widgetViewportWidth,
                        geometry->widgetViewportHeight,
                    })
                : std::nullopt;
        }
        if (renderPlan &&
            renderPlan->work != widgetrail::IncrementalPresentationWork::NoRaster &&
            metrics && metrics->physicalPixelsPerDip > 0.0F) {
            const auto scale = metrics->physicalPixelsPerDip;
            RECT update{
                static_cast<LONG>(std::floor(renderPlan->damage.x * scale)),
                static_cast<LONG>(std::floor(renderPlan->damage.y * scale)),
                static_cast<LONG>(std::ceil(
                    (renderPlan->damage.x + renderPlan->damage.width) * scale)),
                static_cast<LONG>(std::ceil(
                    (renderPlan->damage.y + renderPlan->damage.height) * scale)),
            };
            update.left = std::clamp<LONG>(
                update.left, 0, static_cast<LONG>(width));
            update.top = std::clamp<LONG>(
                update.top, 0, static_cast<LONG>(height));
            update.right = std::clamp<LONG>(
                update.right, update.left, static_cast<LONG>(width));
            update.bottom = std::clamp<LONG>(
                update.bottom, update.top, static_cast<LONG>(height));
            if (update.right > update.left && update.bottom > update.top)
                contentUpdate = update;
        }
        // DirectComposition's partial surface path may defer preservation work
        // until the first D2D command. The retained production trace isolates
        // that cost to updates covering at least half the surface, while full
        // updates remain bounded. Promote only the transport rectangle; the
        // renderer keeps its independently validated incremental plan and
        // layout cache for the same presentation.
        const bool promoteContentTransport = contentUpdate &&
            ShouldPromoteLargeCompositionUpdate(*contentUpdate, width, height);
        if (!contentUpdate && declarativeRenderer_)
            declarativeRenderer_->CancelPresentationUpdatePlan();
        if (contentUpdate && renderPlan) {
            activeContentRenderPlan_ = *renderPlan;
        } else {
            const widgetrail::declarative::Rect fullDamage = geometry
                ? widgetrail::declarative::Rect{
                    0.0F, 0.0F,
                    metrics->viewportWidthDip,
                    metrics->viewportHeightDip}
                : widgetrail::declarative::Rect{
                    0.0F, 0.0F,
                    static_cast<float>(width), static_cast<float>(height)};
            activeContentRenderPlan_ = widgetrail::IncrementalPresentationPlan{
                widgetrail::IncrementalPresentationWork::FullRaster, fullDamage};
        }
        if (retainPendingRefreshPixels) {
            activeContentRenderPlan_ = widgetrail::IncrementalPresentationPlan{
                widgetrail::IncrementalPresentationWork::NoRaster, {}};
            set.contentRendererWork = widgetrail::IncrementalPresentationWork::NoRaster;
        } else {
            set.contentTransportWork = contentUpdate && !promoteContentTransport
                ? CompositionFrameSet::ContentTransportWork::BoundedUpdate
                : CompositionFrameSet::ContentTransportWork::FullSurface;
            set.contentRendererWork = activeContentRenderPlan_->work;
            const RECT* transportUpdate = contentUpdate && !promoteContentTransport
                ? &*contentUpdate
                : nullptr;
            if (!RenderCompositionLayer(
                    width, height, dpi,
                    widgetrail::OverlayCompositionSurface::Layer::Content,
                    CompositionPaintLayer::Content, contentGeometry,
                    transportUpdate, set,
                    &*trayLayout)) {
                return false;
            }
            if (lastWidgetRenderResult_.compositorBackground && metrics) {
                auto observation = compositorBackgroundCoordinator_.Observe(
                        *lastWidgetRenderResult_.compositorBackground,
                        *declarativeRenderer_, compositionSurface_, width, height,
                        metrics->physicalPixelsPerDip, GetTickCount64());
                if (!observation) return false;
                for (auto& frame : observation->frames)
                    set.frames.push_back(std::move(frame));
                set.backgroundPresentation =
                    std::move(observation->presentation);
                set.backgroundObservationTransaction = observation->transactionId;
                AppendDiagnostic(
                    L"Background compositor " + observation->diagnostic);
            } else {
                set.retireBackground = true;
            }
        }

        const auto guideKey = CurrentGuidePaintKey(
            chromeSession.guideWidth, chromeSession.guideHeight, chromeSession.dpi);
        const bool guideDirty =
            !compositionSurface_.hasContent(
                widgetrail::OverlayCompositionSurface::Layer::Guide) ||
            guideKey != retainedGuidePaintKey_;
        const CompositionLayerGeometry guideGeometry{
            chromeSession.guideWidth, chromeSession.guideHeight,
        };
        if (guideDirty && !RenderCompositionLayer(
                chromeSession.guideWidth, chromeSession.guideHeight,
                chromeSession.dpi,
                widgetrail::OverlayCompositionSurface::Layer::Guide,
                CompositionPaintLayer::Guide, guideGeometry, nullptr, set,
                &*trayLayout, &chromeSession.guideBounds)) {
            return false;
        }
        set.guideKey = guideKey;

        auto nextTrayState = CurrentTrayPaintState(
            *trayLayout, chromeSession.trayWidth, chromeSession.trayHeight,
            chromeSession.pixelsPerDip);
        if (!nextTrayState) return false;
        if (OverlayFullscreenMediaRequested()) nextTrayState->items.clear();
        const bool trayDirty = widgetrail::shell::RequiresTrayRepaint(
            retainedTrayPaintState_ ? &*retainedTrayPaintState_ : nullptr,
            *nextTrayState);
        const bool repaintTray = trayDirty || !compositionSurface_.hasContent(
            widgetrail::OverlayCompositionSurface::Layer::Tray);
        const CompositionLayerGeometry trayGeometry{
            chromeSession.trayWidth, chromeSession.trayHeight,
        };
        if (repaintTray) {
            if (!RenderCompositionLayer(
                    chromeSession.trayWidth, chromeSession.trayHeight,
                    chromeSession.dpi,
                    widgetrail::OverlayCompositionSurface::Layer::Tray,
                    CompositionPaintLayer::Tray, trayGeometry, nullptr, set,
                    &*trayLayout)) {
                return false;
            }
        }
        set.trayState = std::move(nextTrayState);
        if (OverlayFullscreenMediaRequested() && metrics && window_) {
            const auto* snapshot = SnapshotFor(state_.activeWidget());
            const auto* descriptor = sessions_.FindDescriptor(state_.activeWidget());
            const auto sessionKey = CurrentEmbeddedMediaSessionKey(
                state_.activeWidget());
            if (snapshot && descriptor && sessionKey &&
                snapshot->embeddedMediaSession) {
                const auto bounds =
                    widgetrail::ResolveOverlayFullscreenMediaSurfaceBounds(
                        {0.0F, 0.0F, metrics->viewportWidthDip,
                         metrics->viewportHeightDip},
                        static_cast<float>(snapshot->embeddedMediaSession->surface.minimumWidth.value_or(1.0)),
                        static_cast<float>(snapshot->embeddedMediaSession->surface.minimumHeight.value_or(1.0)),
                        static_cast<float>(snapshot->embeddedMediaSession->aspectRatio));
                const auto resolved = bounds
                    ? widgetrail::ResolveMediaViewportPresentationGeometry(
                        {bounds->x, bounds->y, bounds->width, bounds->height},
                        {bounds->x, bounds->y, bounds->width, bounds->height},
                        metrics->physicalPixelsPerDip)
                    : std::nullopt;
                if (resolved) {
                    AppendDiagnostic(
                        L"Embedded media fullscreen geometry widget=" +
                        std::wstring{state_.activeWidget()} +
                        L" window-visible=" +
                        (IsWindowVisible(window_) ? L"1" : L"0") +
                        L" host=" +
                        std::to_wstring(resolved->hostBounds.left) + L"," +
                        std::to_wstring(resolved->hostBounds.top) + L"," +
                        std::to_wstring(resolved->hostBounds.right) + L"," +
                        std::to_wstring(resolved->hostBounds.bottom) +
                        L" controller=" +
                        std::to_wstring(resolved->controllerBounds.left) + L"," +
                        std::to_wstring(resolved->controllerBounds.top) + L"," +
                        std::to_wstring(resolved->controllerBounds.right) + L"," +
                        std::to_wstring(resolved->controllerBounds.bottom) +
                        L" scale=" + std::to_wstring(
                            metrics->physicalPixelsPerDip) +
                        L" viewport-dip=" +
                        std::to_wstring(metrics->viewportWidthDip) + L"x" +
                        std::to_wstring(metrics->viewportHeightDip) +
                        L" surface=" +
                        std::to_wstring(compositionSurface_.width()) + L"x" +
                        std::to_wstring(compositionSurface_.height()) +
                        L" desired-extent=" +
                        std::to_wstring(DesiredPresentationExtentDip().widthDip) +
                        L"x" +
                        std::to_wstring(DesiredPresentationExtentDip().heightDip));
                    set.overlayFullscreenGeometry =
                        CompositionFrameSet::OverlayFullscreenGeometry{
                            *sessionKey,
                            {window_,
                             {resolved->hostBounds.left, resolved->hostBounds.top,
                              resolved->hostBounds.right, resolved->hostBounds.bottom},
                             {resolved->hostClip.left, resolved->hostClip.top,
                              resolved->hostClip.right, resolved->hostClip.bottom},
                             {resolved->controllerBounds.left,
                              resolved->controllerBounds.top,
                              resolved->controllerBounds.right,
                              resolved->controllerBounds.bottom},
                             static_cast<double>(metrics->physicalPixelsPerDip),
                             IsWindowVisible(window_) != FALSE,
                             0}};
                }
            }
        }
        drawMicroseconds = static_cast<std::uint64_t>(
            std::chrono::duration_cast<std::chrono::microseconds>(
                std::chrono::steady_clock::now() - started).count());
        return true;
    }

    void DisableCompositionFallback(const std::wstring_view reason) {
        const auto fallbackAnchor = fixedChromeAnchor_;
        AppendDiagnostic(
            L"DirectComposition presentation disabled; using HWND fallback: " +
            std::wstring(reason));
        DiscardGraphicsResources();
        pendingWidgetPresentationImpact_.reset();
        widgetrail::shell::ResetFixedChromeComposition(
            compositionSurface_, chromeWindow_);
        compositorBackgroundCoordinator_.Abandon();
        chromeAccessibilityProvider_.Clear();
        retainedGuidePaintKey_.clear();
        retainedTrayPaintState_.reset();
        fixedChromeAnchor_.reset();
        compositionChromeSession_.reset();
        pendingFixedChromePlacementReason_ =
            FixedChromePlacementReason::NewVisibleSession;
        presentationTransaction_.RejectCompositionAdmission();
        EnableLegacyLayeredFallback();
        appliedOverlayOpacity_.reset();
        ApplyTransitionWindowOpacity(overlayTransitionSample_.shellOpacity);
        if (fallbackAnchor && window_ &&
            state_.surface() != widgetrail::Surface::Hidden) {
            const auto extent = state_.surface() == widgetrail::Surface::Widget
                ? PresentedPresentationExtentDip()
                : widgetrail::OverlayPresentationExtent{kPanelWidth, kDashboardHeight};
            if (const auto target = ComputePlatformPlacement(
                    fallbackAnchor->workArea, fallbackAnchor->dpi,
                    static_cast<float>(extent.widthDip) * fallbackAnchor->interfaceScale,
                    static_cast<float>(extent.heightDip) * fallbackAnchor->interfaceScale)) {
                AppendFallbackPlacementDiagnostic(
                    L"composition-transition", fallbackAnchor->workArea,
                    fallbackAnchor->dpi, fallbackAnchor->interfaceScale, *target);
            }
        }
        if (window_ && state_.surface() != widgetrail::Surface::Hidden)
            InvalidateRect(window_, nullptr, FALSE);
    }

    void EnableLegacyLayeredFallback() {
        if (!window_) return;
        const auto current = static_cast<DWORD>(GetWindowLongPtrW(window_, GWL_EXSTYLE));
        const auto fallback =
            (current & ~WS_EX_NOREDIRECTIONBITMAP) | WS_EX_LAYERED;
        if (fallback != current) {
            SetWindowLongPtrW(window_, GWL_EXSTYLE, static_cast<LONG_PTR>(fallback));
            (void)SetWindowPos(
                window_, nullptr, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE |
                    SWP_FRAMECHANGED | SWP_NOREDRAW);
        }
        (void)SetLayeredWindowAttributes(
            window_, RGB(1, 2, 3), targetOverlayOpacity_,
            LWA_ALPHA | LWA_COLORKEY);
    }

    bool CommitCompositionRepaint(
        const unsigned int width,
        const unsigned int height,
        const UINT dpi) {
        const std::wstring priorPresentationPaintKey =
            lastWidgetPresentationPaintKey_;
        const bool replacement =
            compositionSurface_.width() != width ||
            compositionSurface_.height() != height;
        RECT client{};
        GetClientRect(window_, &client);
        const auto settledMotion = widgetrail::PlanCompositionMotion(
            static_cast<unsigned int>(client.right - client.left),
            static_cast<unsigned int>(client.bottom - client.top),
            width, height, static_cast<float>(width), static_cast<float>(height),
            widgetrail::CompositionVerticalAnchor::Bottom);
        const auto settledSpaces = widgetrail::PlanCompositionChildCoordinates(
            settledMotion, width, height);
        const float destinationOffsetX = settledSpaces.chromeOffsetX;
        const float destinationOffsetY = settledSpaces.chromeOffsetY;
        CompositionFrameSet frames;
        std::uint64_t drawMicroseconds{};
        RECT windowBounds{};
        if (!GetWindowRect(window_, &windowBounds)) return false;
        const widgetrail::OverlayPlacement containerPlacement{
            windowBounds.left, windowBounds.top,
            client.right - client.left, client.bottom - client.top};
        if (!RenderCompositionFrames(
                width, height, dpi, destinationOffsetX, destinationOffsetY,
                containerPlacement,
                frames, drawMicroseconds)) {
            DisableCompositionFallback(L"destination draw failed");
            return false;
        }
        if (frames.frames.empty()) {
            if (frames.overlayFullscreenGeometry) {
                const auto& fullscreen = *frames.overlayFullscreenGeometry;
                const HRESULT mediaResult = ReconcileEmbeddedMediaPresentation(
                    fullscreen.sessionKey,
                    EmbeddedMediaPresentationState::OverlayFullscreen,
                    L"fullscreen-empty-frame-repaint", nullptr,
                    widgetrail::media::ParkingReason::EndpointUnavailable,
                    fullscreen.geometry);
                if (FAILED(mediaResult) && mediaResult != E_PENDING)
                    return false;
                if (SUCCEEDED(mediaResult))
                    PublishCommittedFullscreenPresentation(
                        fullscreen.sessionKey, fullscreen.geometry);
            }
            pendingWidgetPresentationImpact_.reset();
            pendingContentRenderPlan_.reset();
            activeContentRenderPlan_.reset();
            BeginOpenAfterSuccessfulPaint();
            ReconcileCommittedEmbeddedMediaSurface();
            return true;
        }
        std::vector<widgetrail::OverlayCompositionSurface::Frame*> framePointers;
        framePointers.reserve(frames.frames.size());
        for (auto& frame : frames.frames) framePointers.push_back(&frame);
        widgetrail::OverlayCompositionSurface::CommitTiming timing;
        const HRESULT result = compositionSurface_.CommitFrames(
            framePointers, replacement, timing, nullptr,
            frames.backgroundPresentation
                ? &*frames.backgroundPresentation : nullptr);
        if (FAILED(result)) {
            if (frames.backgroundObservationTransaction)
                compositorBackgroundCoordinator_.CancelObservation(
                    *frames.backgroundObservationTransaction);
            DisableCompositionFallback(
                L"surface commit failed hresult=" +
                std::to_wstring(static_cast<unsigned long>(result)));
            return false;
        }
        if (frames.backgroundObservationTransaction &&
            !compositorBackgroundCoordinator_.CommitObservation(
                *frames.backgroundObservationTransaction)) {
            DisableCompositionFallback(
                L"background observation transaction lost authority");
            return false;
        }
        if (frames.retireBackground)
            compositorBackgroundCoordinator_.Retire(compositionSurface_);
        if (lastWidgetRenderResult_.compositorBackground) {
            if (const auto deadline = compositorBackgroundCoordinator_.deadline())
                lastWidgetRenderResult_.backgroundSurfaceSettleWake =
                    widgetrail::BackgroundSurfaceSettleWake{
                        lastWidgetRenderResult_.compositorBackground->bounds,
                        *deadline};
        }
        retainedGuidePaintKey_ = std::move(frames.guideKey);
        retainedTrayPaintState_ = std::move(frames.trayState);
        presentationTransaction_.AcceptCompositionRepaint(
            state_.surface() == widgetrail::Surface::Widget
                ? state_.activeWidget()
                : state_.selectedWidget());
        if (frames.overlayFullscreenGeometry) {
            const auto& fullscreen = *frames.overlayFullscreenGeometry;
            const HRESULT mediaResult = ReconcileEmbeddedMediaPresentation(
                fullscreen.sessionKey,
                EmbeddedMediaPresentationState::OverlayFullscreen,
                L"fullscreen-frame-repaint", nullptr,
                widgetrail::media::ParkingReason::EndpointUnavailable,
                fullscreen.geometry);
            if (FAILED(mediaResult) && mediaResult != E_PENDING)
                return false;
            if (SUCCEEDED(mediaResult))
                PublishCommittedFullscreenPresentation(
                    fullscreen.sessionKey, fullscreen.geometry);
        }
        AppendCompositionCoordinateSample(0);
        const bool presentationChanged =
            priorPresentationPaintKey != lastWidgetPresentationPaintKey_;
        const bool hasFocusFollowSummary = frames.declarativeTiming &&
            !frames.declarativeTiming->focusFollowSummary.empty();
        const bool hasCollectionAdmissionSummary = frames.declarativeTiming &&
            !frames.declarativeTiming->collectionAdmissionSummary.empty();
        if (replacement || presentationChanged || performanceCountersActive_ ||
            drawMicroseconds > kSlowCompositionFrameMicroseconds ||
            hasFocusFollowSummary || hasCollectionAdmissionSummary) {
            std::wstring diagnostic =
                L"Composition frame committed content=complete size=" +
                std::to_wstring(width) + L"x" + std::to_wstring(height) +
                L" order=commit-no-geometry" +
                L" draw-us=" + std::to_wstring(drawMicroseconds) +
                L" commit-us=" + std::to_wstring(timing.commitMicroseconds) +
                L" geometry-us=0" +
                L" waited=" + (timing.waitedForCompletion ? L"true" : L"false") +
                L" geometry=unchanged" +
                SlowCompositionStageDiagnostic(drawMicroseconds, frames);
            if (frames.contentRasterMapping) {
                const auto& mapping = *frames.contentRasterMapping;
                const float errorX = mapping.mappedRequestedOriginPixels.x -
                    static_cast<float>(mapping.atlasOffsetPixels.x);
                const float errorY = mapping.mappedRequestedOriginPixels.y -
                    static_cast<float>(mapping.atlasOffsetPixels.y);
                diagnostic += L" raster-space=physical-pixels";
                diagnostic +=
                    L" raster-request=" +
                    std::to_wstring(mapping.requestedPixels.left) + L"," +
                    std::to_wstring(mapping.requestedPixels.top) + L"," +
                    std::to_wstring(mapping.requestedPixels.right) + L"," +
                    std::to_wstring(mapping.requestedPixels.bottom) +
                    L" raster-atlas=" +
                    std::to_wstring(mapping.atlasOffsetPixels.x) + L"," +
                    std::to_wstring(mapping.atlasOffsetPixels.y) +
                    L" raster-scale=" +
                    std::to_wstring(mapping.scenePixelsPerDip) +
                    L" raster-logical-origin=" +
                    std::to_wstring(mapping.requestedLogicalOriginDip.x) + L"," +
                    std::to_wstring(mapping.requestedLogicalOriginDip.y) +
                    L" raster-scene-translation=" +
                    std::to_wstring(mapping.sceneTranslationPixels.x) + L"," +
                    std::to_wstring(mapping.sceneTranslationPixels.y) +
                    L" raster-origin-error=" +
                    std::to_wstring(errorX) + L"," + std::to_wstring(errorY);
            }
            if (hasCollectionAdmissionSummary) {
                diagnostic += L" " +
                    frames.declarativeTiming->collectionAdmissionSummary;
            }
            AppendDiagnostic(diagnostic);
        }
        if (performanceCountersActive_) ++performanceSuccessfulFrames_;
        pendingWidgetPresentationImpact_.reset();
        pendingContentRenderPlan_.reset();
        activeContentRenderPlan_.reset();
        BeginOpenAfterSuccessfulPaint();
        ReconcileCommittedEmbeddedMediaSurface();
        return true;
    }

    void Paint() {
        PAINTSTRUCT paint{};
        BeginPaint(window_, &paint);
        if (state_.surface() == widgetrail::Surface::Hidden) {
            declarativeMotionActive_ = false;
            EndPaint(window_, &paint);
            return;
        }

        RECT client{};
        GetClientRect(window_, &client);
        const auto& contentPlacement = presentationTransaction_.contentPlacement();
        const auto width = contentPlacement
            ? static_cast<unsigned int>(contentPlacement->width)
            : static_cast<unsigned int>(client.right - client.left);
        const auto height = contentPlacement
            ? static_cast<unsigned int>(contentPlacement->height)
            : static_cast<unsigned int>(client.bottom - client.top);
        const UINT windowDpi = GetDpiForWindow(window_);
        if (compositionSurface_.available()) {
            (void)CommitCompositionRepaint(width, height, windowDpi);
            EndPaint(window_, &paint);
            return;
        }
        if (!EnsureGraphicsResources(width, height, windowDpi)) {
            declarativeMotionActive_ = false;
            EndPaint(window_, &paint);
            return;
        }

        float dpiX = 96.0F;
        float dpiY = 96.0F;
        renderTarget_->GetDpi(&dpiX, &dpiY);
        renderTarget_->BeginDraw();
        activeContentRenderPlan_ = widgetrail::IncrementalPresentationPlan{
            widgetrail::IncrementalPresentationWork::FullRaster,
            {0.0F, 0.0F, static_cast<float>(width), static_cast<float>(height)}};
        // The HWND is a color-keyed layered window above a separately dimmed
        // full-screen backdrop. Only the authored panel/tray surfaces belong
        // to this window; painting the theme canvas across the client creates
        // an opaque rectangular box around those content-shaped surfaces.
        // Keep the canvas color for style inheritance/contrast, but clear the
        // unused client area to the exact transparency key.
        const RECT fullUpdate{
            0, 0, static_cast<LONG>(width), static_cast<LONG>(height)};
        DrawCurrentFrame(
            width, height,
            dpiX > 0 ? static_cast<UINT>(std::lround(dpiX)) : 96U,
            POINT{}, fullUpdate);

        const HRESULT result = renderTarget_->EndDraw();
        if (result == D2DERR_RECREATE_TARGET) {
            DiscardGraphicsResources();
            ReprimeOpenAfterRenderTargetLoss();
        } else if (SUCCEEDED(result)) {
            if (performanceCountersActive_) ++performanceSuccessfulFrames_;
            AppendFallbackPresentationCheckpoint();
            pendingWidgetPresentationImpact_.reset();
            pendingContentRenderPlan_.reset();
            activeContentRenderPlan_.reset();
            BeginOpenAfterSuccessfulPaint();
            ReconcileCommittedEmbeddedMediaSurface();
        }
        EndPaint(window_, &paint);
    }

    void DrawIconStrip(
        const float width,
        const float height,
        const widgetrail::OverlaySurfaceGeometry* surfaceGeometry = nullptr,
        const widgetrail::accessibility::DashboardSemantics* dashboard = nullptr,
        const widgetrail::shell::TrayLayout* frameLayout = nullptr,
        const bool publishAccessibility = true) {
        const auto computedLayout = frameLayout
            ? std::optional<widgetrail::shell::TrayLayout>{}
            : widgetrail::shell::ComputeTrayLayout(
                width, height, state_.order().size(), state_.selectedSlot(),
                TrayBandBelowGuide(height, surfaceGeometry));
        const auto* layout = frameLayout
            ? frameLayout
            : computedLayout ? &*computedLayout : nullptr;
        if (!layout) return;

        const auto drawOverflow = [&](const widgetrail::shell::TrayOverflowLayout& overflow) {
            const auto& bounds = overflow.bounds;
            const D2D1_ROUNDED_RECT control{
                D2D1::RectF(
                    bounds.x, bounds.y,
                    bounds.x + bounds.width, bounds.y + bounds.height),
                std::min(trayItemCornerRadius_, bounds.width * 0.35F),
                std::min(trayItemCornerRadius_, bounds.height * 0.35F)};
            renderTarget_->FillRoundedRectangle(control, trayItemBrush_.Get());
            const float inset = std::max(5.0F, bounds.width * 0.22F);
            (void)widgetrail::icons::DrawNativeIcon(
                renderTarget_.Get(),
                overflow.direction == widgetrail::shell::TrayOverflowDirection::Previous
                    ? widgetrail::icons::NativeIcon::Previous
                    : widgetrail::icons::NativeIcon::Next,
                D2D1::RectF(
                    bounds.x + inset, bounds.y + inset,
                    bounds.x + bounds.width - inset,
                    bounds.y + bounds.height - inset),
                trayItemTextBrush_.Get(), 1.8F);
        };
        if (layout->previousOverflow) drawOverflow(*layout->previousOverflow);

        for (const auto& tileLayout : layout->tiles) {
            const std::size_t slot = tileLayout.slot;
            const float x = tileLayout.bounds.x;
            const float top = tileLayout.bounds.y;
            const float tileSize = tileLayout.bounds.width;
            const D2D1_ROUNDED_RECT tile{
                D2D1::RectF(x, top, x + tileSize, top + tileSize),
                trayItemCornerRadius_, trayItemCornerRadius_};
            renderTarget_->FillRoundedRectangle(
                tile, slot == state_.selectedSlot() ? traySelectedBrush_.Get()
                                                    : trayItemBrush_.Get());
            if (slot == state_.selectedSlot()) {
                const float indicatorInset = std::min(18.0F, tileSize * 0.28F);
                const float indicatorHeight = std::min(4.0F, tileSize * 0.12F);
                const D2D1_ROUNDED_RECT indicator{
                    D2D1::RectF(x + indicatorInset, top + tileSize - indicatorHeight,
                                x + tileSize - indicatorInset, top + tileSize),
                    2.0F, 2.0F};
                renderTarget_->FillRoundedRectangle(
                    indicator, traySelectedTextBrush_.Get());
                if (state_.focusRegion() == widgetrail::FocusRegion::Tray) {
                    renderTarget_->DrawRoundedRectangle(
                        tile, focusBrush_.Get(), focusOutlineWidth_);
                }
            }

            const std::wstring_view widget = state_.order()[slot];
            const float iconInset = std::min(15.0F, tileSize * 0.24F);
            (void)widgetrail::icons::DrawNativeIcon(
                renderTarget_.Get(), DisplayWidgetIcon(widget),
                D2D1::RectF(x + iconInset, top + iconInset,
                            x + tileSize - iconInset, top + tileSize - iconInset),
                slot == state_.selectedSlot()
                    ? traySelectedTextBrush_.Get()
                    : trayItemTextBrush_.Get(),
                2.35F);
        }
        if (layout->nextOverflow) drawOverflow(*layout->nextOverflow);
        if (const auto menu = CurrentTrayContextMenuLayout(
                *layout, CurrentTrayViewportWidthDip(width))) {
            const D2D1_ROUNDED_RECT panel{
                D2D1::RectF(
                    menu->bounds.x, menu->bounds.y,
                    menu->bounds.x + menu->bounds.width,
                    menu->bounds.y + menu->bounds.height),
                trayItemCornerRadius_, trayItemCornerRadius_};
            renderTarget_->FillRoundedRectangle(panel, backgroundBrush_.Get());
            renderTarget_->DrawRoundedRectangle(
                panel, focusBrush_.Get(), focusOutlineWidth_);
            for (const auto& item : menu->semantics.items) {
                const auto& bounds = item.bounds;
                if (item.selected) {
                    const D2D1_ROUNDED_RECT selection{
                        D2D1::RectF(
                            bounds.x + 3.0F, bounds.y + 3.0F,
                            bounds.x + bounds.width - 3.0F,
                            bounds.y + bounds.height - 3.0F),
                        trayItemCornerRadius_ * 0.65F,
                        trayItemCornerRadius_ * 0.65F};
                    renderTarget_->FillRoundedRectangle(selection, accentBrush_.Get());
                }
                DrawTextLine(
                    item.name, hintFormat_.Get(),
                    D2D1::RectF(
                        bounds.x + 14.0F, bounds.y + 5.0F,
                        bounds.x + bounds.width - 12.0F,
                        bounds.y + 25.0F),
                    item.enabled ? trayItemTextBrush_.Get()
                                 : dashboardSecondaryBrush_.Get());
                if (!item.value.empty()) {
                    DrawTextLine(
                        item.value, hintFormat_.Get(),
                        D2D1::RectF(
                            bounds.x + 14.0F, bounds.y + 25.0F,
                            bounds.x + bounds.width - 12.0F,
                            bounds.y + bounds.height - 4.0F),
                        dashboardSecondaryBrush_.Get());
                }
            }
        }
        if (publishAccessibility)
            PublishTrayAccessibility(*layout, width, height, dashboard);
    }

    static std::wstring_view DisplayButton(const std::wstring_view button) {
        if (button == L"leftBumper") return L"LB";
        if (button == L"rightBumper") return L"RB";
        if (button == L"leftTrigger") return L"LT";
        if (button == L"rightTrigger") return L"RT";
        if (button == L"leftStick") return L"LS";
        if (button == L"rightStick") return L"RS";
        if (button == L"a") return L"A";
        if (button == L"b") return L"B";
        if (button == L"x") return L"X";
        if (button == L"y") return L"Y";
        if (button == L"menu") return L"Menu";
        if (button == L"view") return L"View";
        return button;
    }

    std::optional<std::wstring> DashboardStatus() const {
        const auto now = GetTickCount64();
        if (const auto* startup = sessions_.Failure(state_.selectedWidget()))
            return startup->safeMessage;
        if (const auto failure = actionFailureFeedback_.MessageForSurface(
                widgetrail::WidgetActionFeedbackSurface::Dashboard,
                state_.selectedWidget(),
                state_.activeWidget())) return std::wstring{*failure};
        if (lastActionExpiresAt_ > now && !lastActionMessage_.empty() &&
            lastActionWidgetId_ == state_.selectedWidget()) {
            return lastActionMessage_;
        }
        return std::nullopt;
    }

    std::wstring DashboardHint(const float availableWidth) {
        if (trayContextMenu_)
            return L"D-pad/left stick  Navigate    A  Select    B/Menu  Close";
        if (trayYGesture_.pendingRestart()) {
            return L"Hold Y to restart " +
                std::wstring(DisplayWidgetName(trayYGesture_.selectedWidget())) +
                L" — " +
                std::to_wstring(trayYGesture_.progressPercent(GetTickCount64())) +
                L"%";
        }
        std::vector<widgetrail::ControllerGuideAction> quickActions;
        const auto* snapshot = GuideSnapshotFor(state_.selectedWidget());
        if (IsBridgeWidget(state_.selectedWidget()) && snapshot) {
            quickActions.reserve(snapshot->quickActions.size());
            for (const auto& action : snapshot->quickActions) {
                // A/Y/B remain shell navigation while focus is on the tray.
                if (action.button == L"a" || action.button == L"y" ||
                    action.button == L"b") continue;
                quickActions.push_back({DisplayButton(action.button), action.label});
            }
        }
        auto guide = widgetrail::BuildTrayControllerGuide(
            widgetrail::ResolveControllerGuideDensity(
                availableWidth, CurrentTextScale()),
            state_.reorderMode(), TrayYRestartEligible(), availableWidth,
            [this](const std::wstring_view text) {
                return MeasureGuideTextWidth(text);
            },
            quickActions);
        return guide;
    }

    std::wstring DashboardAccessibilityHint(const std::wstring_view visualHint) const {
        if (trayYGesture_.pendingRestart()) return std::wstring{visualHint};
        if (state_.reorderMode() || !TrayYRestartEligible())
            return std::wstring{visualHint};
        return std::wstring{visualHint} +
            L". Tap Y to reorder. Hold Y to restart the selected widget.";
    }

    void DrawDashboard(
        const float width, const float height,
        const CompositionPaintLayer layer = CompositionPaintLayer::Combined,
        const widgetrail::shell::TrayLayout* frameTrayLayout = nullptr) {
        if (!accessibilityActive_) {
            if (!accessibilityTree_.widgetId.empty()) ClearAccessibilityTree();
        }
        if (width <= 0.0F || height <= 0.0F) return;
        const float horizontalInset = std::min(34.0F, width * 0.1F);
        const float contentLeft = horizontalInset;
        const float contentRight = std::max(contentLeft, width - horizontalInset);
        const float titleTop = std::min(10.0F, height);
        const float titleBottom = std::max(titleTop, std::min(44.0F, height));
        const float hintTop = titleBottom;
        const float hintBottom = DashboardGuideContentBottomDip(height);
        const std::wstring_view title = state_.reorderMode()
            ? L"Reorder widgets"
            : DisplayWidgetName(state_.selectedWidget());
        const auto status = DashboardStatus();
        const std::wstring help = DashboardHint(contentRight - contentLeft);
        const std::wstring accessibleHelp = DashboardAccessibilityHint(help);
        const std::wstring displayedHint = status ? *status : help;
        const widgetrail::declarative::Rect hintBounds{
            contentLeft, hintTop, contentRight - contentLeft, hintBottom - hintTop,
        };
        const widgetrail::accessibility::DashboardSemantics dashboard{
            std::wstring{title},
            {contentLeft, titleTop, contentRight - contentLeft, titleBottom - titleTop},
            status ? std::wstring{} : accessibleHelp,
            hintBounds,
            status ? *status : std::wstring{},
            hintBounds,
        };
        if (layer == CompositionPaintLayer::Combined ||
            layer == CompositionPaintLayer::Content) {
            DrawTextLine(title, titleFormat_.Get(),
                         D2D1::RectF(contentLeft, titleTop, contentRight, titleBottom),
                         dashboardTextBrush_.Get());
            DrawTextLine(displayedHint, hintFormat_.Get(),
                         D2D1::RectF(contentLeft, hintTop, contentRight, hintBottom),
                         dashboardSecondaryBrush_.Get());
        }
        if (layer == CompositionPaintLayer::Combined ||
            layer == CompositionPaintLayer::Tray) {
            DrawIconStrip(width, height, nullptr, &dashboard, frameTrayLayout);
        } else if (accessibilityActive_) {
            const auto layout = frameTrayLayout
                ? std::optional<widgetrail::shell::TrayLayout>{*frameTrayLayout}
                : widgetrail::shell::ComputeTrayLayout(
                    width, height, state_.order().size(), state_.selectedSlot(),
                    TrayBandBelowGuide(height));
            if (layout) PublishTrayAccessibility(*layout, width, height, &dashboard);
        }
    }

    std::optional<std::wstring> OpenWidgetStatus() const {
        const auto now = GetTickCount64();
        if (const auto* startup = sessions_.Failure(state_.activeWidget()))
            return startup->safeMessage;
        if (const auto failure = actionFailureFeedback_.MessageForSurface(
                widgetrail::WidgetActionFeedbackSurface::OpenWidget,
                state_.selectedWidget(),
                state_.activeWidget())) return std::wstring{*failure};
        if (lastActionExpiresAt_ > now && !lastActionMessage_.empty() &&
            lastActionWidgetId_ == state_.activeWidget()) {
            return lastActionMessage_;
        }
        return std::nullopt;
    }

    [[nodiscard]] widgetrail::guide::OpenWidgetLine ResolveOpenWidgetGuide(
        const float availableWidth) {
        const auto* snapshot = InteractionSnapshotFor(state_.activeWidget());
        const auto hostBack = NonCurrentHostRootBackAuthority();
        const bool rootScope = snapshot &&
            std::wstring_view(snapshot->activeInputScopeId) ==
                widgetrail::input::RootInputScope(*snapshot);
        const bool nestedBack = snapshot &&
            widgetrail::accessibility::HasActiveScopeBackShortcut(
                *snapshot, interactionSession_.focusedElementId());
        widgetrail::guide::OpenWidgetAuthority authority;
        if (snapshot) {
            authority = widgetrail::guide::ResolveOpenWidgetAuthority(
                *snapshot, interactionSession_.focusedElementId());
        }
        return widgetrail::guide::BuildOpenWidgetLine(
            widgetrail::ResolveControllerGuideDensity(
                availableWidth, CurrentTextScale()),
            authority,
            rootScope || nestedBack || hostBack.has_value(),
            availableWidth,
            [this](const std::wstring_view text) {
                return MeasureGuideTextWidth(text);
            });
    }

    void DrawWidgetFooter(
        const widgetrail::OverlaySurfaceGeometry& geometry,
        const widgetrail::declarative::Rect* fixedGuideBounds = nullptr) {
        openWidgetAccessibility_ = {};
        if (geometry.footerHeight <= 0.0F || geometry.panelWidth <= 0.0F) return;
        const float panelLeft = fixedGuideBounds
            ? fixedGuideBounds->x : geometry.panelX;
        const float panelRight = fixedGuideBounds
            ? fixedGuideBounds->x + fixedGuideBounds->width
            : geometry.panelX + geometry.panelWidth;
        const float guideWidth = panelRight - panelLeft;
        const float horizontalInset = std::min(30.0F, guideWidth * 0.1F);
        const float contentLeft = panelLeft + horizontalInset;
        const float contentRight = std::max(contentLeft, panelRight - horizontalInset);
        const float guideTop = fixedGuideBounds
            ? fixedGuideBounds->y : geometry.footerY;
        const float guideHeight = fixedGuideBounds
            ? fixedGuideBounds->height : geometry.footerHeight;
        const float textTop = guideTop +
            std::min(15.0F, guideHeight * 0.35F);
        const float textBottom = guideTop +
            WidgetGuideContentBottomDip(guideHeight);
        if (textBottom <= textTop + 1.0F) return;
        const widgetrail::declarative::Rect footerBounds{
            contentLeft, textTop, contentRight - contentLeft, textBottom - textTop,
        };
        openWidgetAccessibility_.title =
            std::wstring{DisplayWidgetName(state_.activeWidget())};
        if (state_.focusRegion() == widgetrail::FocusRegion::Tray) {
            const auto status = DashboardStatus();
            const std::wstring help = DashboardHint(contentRight - contentLeft);
            const std::wstring accessibleHelp = DashboardAccessibilityHint(help);
            const std::wstring footer = status ? *status : help;
            const float footerWidth = MeasureGuideTextWidth(footer).value_or(
                footerBounds.width);
            const float footerLeft = contentLeft + std::max(
                0.0F, (footerBounds.width - footerWidth) * 0.5F);
            const widgetrail::declarative::Rect renderedBounds{
                footerLeft, textTop,
                std::min(footerWidth, footerBounds.width), textBottom - textTop,
            };
            openWidgetAccessibility_.closeBounds = renderedBounds;
            if (status) {
                openWidgetAccessibility_.status = *status;
                openWidgetAccessibility_.statusBounds = renderedBounds;
            } else {
                openWidgetAccessibility_.help = accessibleHelp;
                openWidgetAccessibility_.helpBounds = renderedBounds;
            }
            DrawTextLine(footer, hintFormat_.Get(),
                         D2D1::RectF(footerLeft, textTop, contentRight, textBottom),
                         dashboardSecondaryBrush_.Get());
            return;
        }
        const auto status = OpenWidgetStatus();
        const auto* snapshot = InteractionSnapshotFor(state_.activeWidget());
        const auto hostBack = NonCurrentHostRootBackAuthority();
        const bool rootScope = snapshot &&
            std::wstring_view(snapshot->activeInputScopeId) ==
                widgetrail::input::RootInputScope(*snapshot);
        const bool nestedBack = snapshot && !rootScope &&
            widgetrail::accessibility::HasActiveScopeBackShortcut(
                *snapshot, interactionSession_.focusedElementId());
        const bool hasBack = rootScope || nestedBack || hostBack.has_value();
        const auto guide = ResolveOpenWidgetGuide(contentRight - contentLeft);
        const std::wstring& help = guide.accessible;
        const std::wstring prompt = status ? *status : guide.contextual;
        const std::wstring& hostPrompt = guide.host;
        if (hasBack) {
            openWidgetAccessibility_.backAction = rootScope || hostBack
                ? widgetrail::accessibility::HostAction::BackToTray
                : widgetrail::accessibility::HostAction::BackWithinWidget;
            openWidgetAccessibility_.backTargetId = hostBack
                ? hostBack->inputScopeId
                : snapshot->activeInputScopeId;
        }
        const std::wstring row = prompt.empty()
            ? hostPrompt
            : prompt + L"   " + hostPrompt;
        const float rowWidth = std::min(
            footerBounds.width,
            MeasureGuideTextWidth(row).value_or(footerBounds.width));
        const float rowLeft = contentLeft + std::max(
            0.0F, (footerBounds.width - rowWidth) * 0.5F);
        const widgetrail::declarative::Rect rowBounds{
            rowLeft, textTop, rowWidth, textBottom - textTop,
        };
        const float promptWidth = prompt.empty()
            ? 0.0F
            : MeasureGuideTextWidth(prompt + L"   ").value_or(0.0F);
        const float backPromptWidth = hasBack
            ? MeasureGuideTextWidth(L"B Back   ").value_or(0.0F)
            : 0.0F;
        const float hostPromptLeft = rowLeft + promptWidth;
        if (!prompt.empty()) {
            const widgetrail::declarative::Rect promptBounds{
                rowLeft, textTop, promptWidth,
                textBottom - textTop,
            };
            if (status) {
                openWidgetAccessibility_.status = *status;
                openWidgetAccessibility_.statusBounds = promptBounds;
            } else {
                openWidgetAccessibility_.help = help;
                openWidgetAccessibility_.helpBounds = rowBounds;
            }
            float actionLeft = hostPromptLeft;
            if (hasBack) {
                openWidgetAccessibility_.backBounds = {
                    actionLeft, textTop, backPromptWidth, textBottom - textTop,
                };
                actionLeft += backPromptWidth;
                openWidgetAccessibility_.closeBounds = {
                    actionLeft, textTop, rowLeft + rowWidth - actionLeft,
                    textBottom - textTop,
                };
            } else {
                openWidgetAccessibility_.closeBounds = {
                    actionLeft, textTop, rowLeft + rowWidth - actionLeft,
                    textBottom - textTop,
                };
            }
            DrawTextLine(row, hintFormat_.Get(),
                         D2D1::RectF(rowLeft, textTop,
                                     contentRight, textBottom),
                         secondaryBrush_.Get());
        } else {
            openWidgetAccessibility_.help = help;
            openWidgetAccessibility_.helpBounds = rowBounds;
            float actionLeft = rowLeft;
            if (hasBack) {
                openWidgetAccessibility_.backBounds = {
                    actionLeft, footerBounds.y, backPromptWidth, footerBounds.height,
                };
                actionLeft += backPromptWidth;
                openWidgetAccessibility_.closeBounds = {
                    actionLeft, footerBounds.y,
                    rowLeft + rowWidth - actionLeft,
                    footerBounds.height,
                };
            } else {
                openWidgetAccessibility_.closeBounds = {
                    actionLeft, footerBounds.y, rowWidth,
                    footerBounds.height,
                };
            }
            DrawTextLine(hostPrompt, hintFormat_.Get(),
                         D2D1::RectF(rowLeft, textTop, contentRight, textBottom),
                         secondaryBrush_.Get());
        }
    }

    void DrawWidgetContextMenu(const float width, const float height) {
        const auto menu = CurrentWidgetContextMenuLayout(width, height);
        if (!menu) return;
        const D2D1_ROUNDED_RECT panel{
            D2D1::RectF(
                menu->bounds.x, menu->bounds.y,
                menu->bounds.x + menu->bounds.width,
                menu->bounds.y + menu->bounds.height),
            trayItemCornerRadius_, trayItemCornerRadius_};
        renderTarget_->FillRoundedRectangle(panel, backgroundBrush_.Get());
        renderTarget_->DrawRoundedRectangle(
            panel, focusBrush_.Get(), focusOutlineWidth_);
        for (std::size_t index = 0;
             index < menu->semantics.items.size(); ++index) {
            const auto& item = menu->semantics.items[index];
            const auto& action = widgetContextMenu_->actions[index];
            const auto& bounds = item.bounds;
            if (item.selected) {
                const D2D1_ROUNDED_RECT selection{
                    D2D1::RectF(
                        bounds.x + 3.0F, bounds.y + 3.0F,
                        bounds.x + bounds.width - 3.0F,
                        bounds.y + bounds.height - 3.0F),
                    trayItemCornerRadius_ * 0.65F,
                    trayItemCornerRadius_ * 0.65F};
                renderTarget_->FillRoundedRectangle(selection, accentBrush_.Get());
            }
            const std::wstring visual = action.style == L"danger"
                ? L"Danger · " + item.name : item.name;
            DrawTextLine(
                visual, hintFormat_.Get(),
                D2D1::RectF(
                    bounds.x + 14.0F, bounds.y + 10.0F,
                    bounds.x + bounds.width - 12.0F,
                    bounds.y + bounds.height - 8.0F),
                item.enabled ? textBrush_.Get() : secondaryBrush_.Get());
        }
    }

    void DrawSelectPopup(const float width, const float height) {
        const auto layout = CurrentSelectPopupLayout(width, height);
        const auto& popup = interactionSession_.selectPopup();
        if (!layout || !popup || layout->items.empty()) return;
        const D2D1_ROUNDED_RECT panel{
            D2D1::RectF(
                layout->bounds.x, layout->bounds.y,
                layout->bounds.x + layout->bounds.width,
                layout->bounds.y + layout->bounds.height),
            trayItemCornerRadius_, trayItemCornerRadius_};
        renderTarget_->FillRoundedRectangle(panel, backgroundBrush_.Get());
        renderTarget_->DrawRoundedRectangle(
            panel, focusBrush_.Get(), focusOutlineWidth_);
        const auto priorParagraphAlignment = hintFormat_->GetParagraphAlignment();
        const bool popupTextCentered = SUCCEEDED(hintFormat_->SetParagraphAlignment(
            DWRITE_PARAGRAPH_ALIGNMENT_CENTER));
        for (const auto& item : layout->items) {
            if (item.optionIndex >= popup->options.size()) continue;
            const auto& option = popup->options[item.optionIndex];
            if (item.optionIndex == popup->highlightedOption) {
                const D2D1_ROUNDED_RECT selected{
                    D2D1::RectF(
                        item.bounds.x + 3.0F, item.bounds.y + 3.0F,
                        item.bounds.x + item.bounds.width - 3.0F,
                        item.bounds.y + item.bounds.height - 3.0F),
                    trayItemCornerRadius_ * 0.65F,
                    trayItemCornerRadius_ * 0.65F};
                renderTarget_->FillRoundedRectangle(selected, accentBrush_.Get());
            }
            widgetrail::icons::NativeIcon icon{};
            const bool hasIcon = !option.glyph.empty() &&
                widgetrail::icons::TryParseNativeIcon(option.glyph, icon);
            const auto content = widgetrail::input::ComputeSelectPopupContentLayout(
                item.bounds, option.isSelected, hasIcon, 12.0F, 10.0F);
            if (content.checkmarkBounds) {
                const auto& bounds = *content.checkmarkBounds;
                DrawTextLine(
                    L"✓", hintFormat_.Get(),
                    D2D1::RectF(
                        bounds.x, bounds.y,
                        bounds.x + bounds.width, bounds.y + bounds.height),
                    option.isDisabled || option.isBusy
                        ? secondaryBrush_.Get() : textBrush_.Get());
            }
            if (content.glyphBounds) {
                const auto& bounds = *content.glyphBounds;
                (void)widgetrail::icons::DrawNativeIcon(
                    renderTarget_.Get(), icon,
                    D2D1::RectF(
                        bounds.x, bounds.y,
                        bounds.x + bounds.width, bounds.y + bounds.height),
                    option.isDisabled || option.isBusy
                        ? secondaryBrush_.Get() : textBrush_.Get(),
                    1.7F);
            }
            const auto& labelBounds = content.labelBounds;
            DrawTextLine(
                option.label, hintFormat_.Get(),
                D2D1::RectF(
                    labelBounds.x, labelBounds.y,
                    labelBounds.x + labelBounds.width,
                    labelBounds.y + labelBounds.height),
                option.isDisabled || option.isBusy
                    ? secondaryBrush_.Get() : textBrush_.Get());
        }
        if (popupTextCentered) {
            (void)hintFormat_->SetParagraphAlignment(priorParagraphAlignment);
        }
    }

    void DrawWidget(
        const float width,
        const float height,
        const float physicalPixelsPerDip,
        const CompositionPaintLayer layer = CompositionPaintLayer::Combined,
        const widgetrail::shell::TrayLayout* frameTrayLayout = nullptr,
        const widgetrail::declarative::Rect* frameGuideBounds = nullptr) {
        declarativeMotionActive_ = false;
        const std::wstring_view widget = state_.activeWidget();
        const bool bridgeWidget = IsBridgeWidget(widget);
        const auto geometry = layer == CompositionPaintLayer::Content &&
                compositionSurface_.available()
            ? widgetrail::ComputePanelLocalSurfaceGeometry(width, height)
            : [&]() {
                const auto widgetSurface = DesiredWidgetSurfaceTarget();
                return widgetrail::ComputeOverlaySurfaceGeometry(
                    width, height,
                    bridgeWidget ? widgetSurface.panelWidthDip : 720.0F,
                    bridgeWidget
                        ? std::optional<float>{widgetSurface.panelHeightDip}
                        : std::nullopt);
            }();
        if (!geometry) return;
        const auto sessionTrayLayout = !frameTrayLayout && compositionSurface_.available()
            ? CurrentCompositionTrayLayout()
            : std::optional<widgetrail::shell::TrayLayout>{};
        const auto computedTrayLayout = frameTrayLayout || sessionTrayLayout
            ? std::optional<widgetrail::shell::TrayLayout>{}
            : widgetrail::shell::ComputeTrayLayout(
                width, height, state_.order().size(), state_.selectedSlot(),
                TrayBandBelowGuide(height, &*geometry));
        const auto* trayLayout = frameTrayLayout
            ? frameTrayLayout
            : sessionTrayLayout ? &*sessionTrayLayout
            : computedTrayLayout ? &*computedTrayLayout : nullptr;
        const float panelLeft = geometry->panelX;
        const float panelTop = geometry->panelY;
        const float panelWidth = geometry->panelWidth;
        const bool drawContent = layer == CompositionPaintLayer::Combined ||
            layer == CompositionPaintLayer::Content;
        const bool drawGuide = layer == CompositionPaintLayer::Combined ||
            layer == CompositionPaintLayer::Guide;
        const bool drawTray = layer == CompositionPaintLayer::Combined ||
            layer == CompositionPaintLayer::Tray;
        const std::optional<widgetrail::declarative::Rect> fixedGuideBounds = frameGuideBounds
            ? std::optional<widgetrail::declarative::Rect>{*frameGuideBounds}
            : trayLayout
            ? std::optional<widgetrail::declarative::Rect>{widgetrail::declarative::Rect{
                trayLayout->stripBounds.x,
                geometry->footerY,
                trayLayout->stripBounds.width,
                geometry->footerHeight,
            }}
            : std::nullopt;

        if (!drawContent) {
            if (drawGuide) DrawWidgetFooter(
                *geometry, fixedGuideBounds ? &*fixedGuideBounds : nullptr);
            if (drawTray) {
                DrawIconStrip(width, height, &*geometry, nullptr,
                              trayLayout);
            } else if (accessibilityActive_ && trayLayout) {
                PublishTrayAccessibility(*trayLayout, width, height);
            }
            return;
        }
        // The footer is host chrome, not widget content. End the authored card
        // at the content boundary; the immediately following fixed-height
        // guide then connects that visible panel edge to the stationary tray
        // without masquerading as third-party widget content.
        const float visualPanelBottom = geometry->footerY;
        const D2D1_ROUNDED_RECT panel{
            D2D1::RectF(panelLeft, panelTop, panelLeft + panelWidth, visualPanelBottom),
            panelCornerRadius_, panelCornerRadius_};
        const auto surfaceAppearance = ResolveRenderedSurfaceAppearance();
        const std::wstring surfaceDiagnosticKey =
            std::wstring(widgetrail::surface_appearance::Name(surfaceAppearance.declared)) + L"|" +
            std::wstring(widgetrail::surface_appearance::Name(surfaceAppearance.requested)) + L"|" +
            std::wstring(widgetrail::surface_appearance::Name(surfaceAppearance.effective)) + L"|" +
            std::wstring(surfaceAppearance.fallbackReason);
        if (surfaceDiagnosticKey != lastSurfaceAppearanceDiagnostic_) {
            lastSurfaceAppearanceDiagnostic_ = surfaceDiagnosticKey;
            AppendDiagnostic(
                L"Widget surface appearance widget=" + std::wstring(widget) +
                L" declared=" + std::wstring(widgetrail::surface_appearance::Name(surfaceAppearance.declared)) +
                L" requested=" + std::wstring(widgetrail::surface_appearance::Name(surfaceAppearance.requested)) +
                L" effective=" + std::wstring(widgetrail::surface_appearance::Name(surfaceAppearance.effective)) +
                (surfaceAppearance.fallbackReason.empty()
                    ? std::wstring{}
                    : L" fallback=" + std::wstring(surfaceAppearance.fallbackReason)));
        }
        if (surfaceAppearance.effective !=
            widgetrail::surface_appearance::Mode::Transparent) {
            widgetrail::shell::FillColorKeyRoundedRectangle(
                renderTarget_.Get(), panel,
                surfaceAppearance.effective == widgetrail::surface_appearance::Mode::Solid
                    ? solidCardBrush_.Get() : cardBrush_.Get(),
                compositionSurface_.available()
                    ? widgetrail::shell::OuterChromeBoundary::PremultipliedAlpha
                    : widgetrail::shell::OuterChromeBoundary::ColorKeyAliased);
        }

        if (bridgeWidget) {
            ComPtr<ID2D1Layer> contentLayer;
            bool contentLayerPushed = false;
            if (overlayTransitionSample_.contentOpacity < 0.999F &&
                SUCCEEDED(renderTarget_->CreateLayer(
                    nullptr, contentLayer.ReleaseAndGetAddressOf()))) {
                auto layerParameters = D2D1::LayerParameters();
                layerParameters.contentBounds = D2D1::RectF(
                    geometry->widgetViewportX,
                    geometry->widgetViewportY,
                    geometry->widgetViewportX + geometry->widgetViewportWidth,
                    geometry->widgetViewportY + geometry->widgetViewportHeight);
                layerParameters.opacity = std::clamp(
                    overlayTransitionSample_.contentOpacity, 0.0F, 1.0F);
                renderTarget_->PushLayer(layerParameters, contentLayer.Get());
                contentLayerPushed = true;
            }
            const auto sessionPresentation = sessions_.Presentation(widget);
            const auto& retainedPresentation =
                presentationTransaction_.retainedPresentation();
            const auto contentAuthority = widgetrail::ResolveWidgetContentAuthority(
                sessionPresentation.snapshot != nullptr,
                sessionPresentation.HasCommittedViewAuthority(),
                retainedPresentation.has_value());
            const bool sessionRetainedSnapshot =
                contentAuthority == widgetrail::WidgetContentAuthority::InertRetainedSnapshot;
            const bool transitionRetainedSnapshot =
                contentAuthority == widgetrail::WidgetContentAuthority::RetainedCommittedSnapshot;
            const bool inertRetainedSnapshot =
                sessionRetainedSnapshot || transitionRetainedSnapshot;
            const auto* snapshot = sessionRetainedSnapshot
                ? sessionPresentation.snapshot
                : transitionRetainedSnapshot
                    ? &retainedPresentation->snapshot
                    : sessionPresentation.snapshot;
            const std::wstring_view renderedWidget = transitionRetainedSnapshot
                ? std::wstring_view{retainedPresentation->widgetId}
                : widget;
            const auto* descriptor = sessions_.FindDescriptor(renderedWidget);
            const bool retainedRefresh = sessionPresentation.RefreshPending();
            const auto freeScrollDecision = snapshot && descriptor &&
                    renderedWidget == state_.activeWidget()
                ? widgetrail::input::SurfaceInteractionTransactions::EvaluateFreeScroll(
                      interactionSession_.freeScrollState(),
                      {
                          renderedWidget,
                          snapshot,
                          descriptor->runtimeGeneration,
                          descriptor->presentationGeneration,
                          retainedRefresh,
                      },
                      interactionSession_.focusedElementId(),
                      lastWidgetRenderResult_)
                : widgetrail::input::FreeScrollAuthorityDecision{};
            const bool retainedRefreshFreeScroll =
                freeScrollDecision.disposition ==
                widgetrail::input::FreeScrollAuthorityDisposition::Retained;
            const std::wstring_view currentFocusId =
                state_.focusRegion() == widgetrail::FocusRegion::Widget
                    ? std::wstring_view{interactionSession_.focusedElementId()}
                    : std::wstring_view{};
            const std::wstring_view refreshRetainedFocusId =
                retainedRefreshFreeScroll && interactionSession_.freeScrollBinding()
                    ? std::wstring_view{
                        interactionSession_.freeScrollBinding()->focusedElementId}
                    : std::wstring_view{};
            const std::wstring_view retainedCommittedFocusId =
                transitionRetainedSnapshot
                    ? std::wstring_view{retainedPresentation->focusId}
                    : std::wstring_view{};
            std::wstring renderedFocusId{widgetrail::ResolveWidgetContentFocusId(
                contentAuthority,
                {currentFocusId, refreshRetainedFocusId, retainedCommittedFocusId})};
            if (snapshot && declarativeRenderer_) {
                const widgetrail::declarative::Rect viewport{
                    geometry->widgetViewportX,
                    geometry->widgetViewportY,
                    geometry->widgetViewportWidth,
                    geometry->widgetViewportHeight,
                };
                const auto accessibilityPolicy = appearanceState_.current()
                    ? CurrentAccessibilityPolicy()
                    : widgetrail::NativeAccessibilityPolicy{};
                const auto presentationTime = GetTickCount64();
                widgetrail::DeclarativeRenderOptions options;
                options.pixelScale = physicalPixelsPerDip;
                options.responsiveViewport = widgetrail::declarative::Size{
                    geometry->panelWidth,
                    geometry->panelHeight,
                };
                options.surfaceBackground = effectivePanelBackground_;
                const float panelContentInset = std::max(
                    0.0F, geometry->widgetViewportX - panelLeft);
                options.surfaceCornerRadiusPx = std::max(
                    0.0F, panelCornerRadius_ - panelContentInset);
                if (appearanceState_.current())
                    options.accessibility = accessibilityPolicy;
                options.animationTimestampMilliseconds = presentationTime;
                options.artworkWidgetId = std::wstring{renderedWidget};
                if (descriptor) {
                    options.artworkRuntimeGeneration = descriptor->runtimeGeneration;
                    options.artworkPresentationGeneration =
                        descriptor->presentationGeneration;
                }
                options.compositorBackgroundAvailable =
                    compositionSurface_.available() && !inertRetainedSnapshot;
                if (descriptor) {
                    options.artworkAuthorityId =
                        std::wstring{renderedWidget} + L"\x1f" +
                        descriptor->runtimeGeneration + L"\x1f" +
                        descriptor->presentationGeneration;
                }
                const bool matchingFreeScrollBinding =
                    interactionSession_.freeScrollBinding() &&
                    (freeScrollDecision.disposition ==
                         widgetrail::input::FreeScrollAuthorityDisposition::Current ||
                     freeScrollDecision.disposition ==
                         widgetrail::input::FreeScrollAuthorityDisposition::Retained);
                if (interactionSession_.freeScrollBinding() &&
                    renderedWidget == state_.activeWidget() &&
                    freeScrollDecision.disposition ==
                        widgetrail::input::FreeScrollAuthorityDisposition::Replaced) {
                    ClearFreeScrollReentry(L"render-authority-changed");
                }
                if (retainedRefreshFreeScroll)
                    SetFreeScrollRefreshDeferred(true);
                else if (!inertRetainedSnapshot && matchingFreeScrollBinding)
                    SetFreeScrollRefreshDeferred(false);
                options.suppressFocusedDescendantFollow =
                    freeScrollDecision.followSuppressed &&
                    (!inertRetainedSnapshot || retainedRefreshFreeScroll);

                const std::wstring provisionalRenderedFocusId = renderedFocusId;
                std::optional<widgetrail::input::WidgetInteractionAuthority>
                    focusGroupEntryAuthority;
                std::optional<std::wstring> preparedFocusGroupEntryTarget;
                bool focusGroupEntryPrepared{};
                bool focusGroupEntryWaiting{};
                if (descriptor && WidgetOwnsInputFocus(renderedWidget) &&
                    !textEntryModal_.active() && !inertRetainedSnapshot) {
                    focusGroupEntryAuthority =
                        widgetrail::input::WidgetInteractionAuthority{
                            renderedWidget,
                            snapshot,
                            descriptor->runtimeGeneration,
                            descriptor->presentationGeneration,
                            false,
                        };
                    if (interactionSession_.FocusGroupEntryRequestPending(
                            *focusGroupEntryAuthority)) {
                        const auto preparation =
                            declarativeRenderer_->PrepareFocusEntry(
                                *snapshot, renderedFocusId, viewport, options);
                        const auto preview = preparation.succeeded
                            ? interactionSession_.PreviewFocusGroupEntryRequest(
                                *focusGroupEntryAuthority, preparation)
                            : widgetrail::input::FocusGroupEntryPreview{};
                        if (preview.current) {
                            if (preview.target) {
                                focusGroupEntryPrepared = true;
                                preparedFocusGroupEntryTarget = preview.target;
                                renderedFocusId = *preview.target;
                            } else {
                                focusGroupEntryWaiting = true;
                            }
                        } else {
                            (void)interactionSession_.RetireFocusGroupEntryRequest(
                                *focusGroupEntryAuthority);
                            AppendDiagnostic(
                                L"Focus-group entry request widget=" +
                                std::wstring{renderedWidget} +
                                L" state=preparation-retired");
                        }
                    }
                }

                auto interactionPresentation =
                    interactionSession_.PrepareRenderPresentation(
                        *snapshot, renderedFocusId, !inertRetainedSnapshot,
                        !inertRetainedSnapshot &&
                            state_.focusRegion() == widgetrail::FocusRegion::Widget);
                options.sliderValueOverrides =
                    interactionPresentation.sliderValueOverrides;
                options.pressedElementId =
                    interactionPresentation.pressedElementId;
                options.activeSliderElementId =
                    interactionPresentation.activeSliderElementId;
                if (focusGroupEntryPrepared && preparedFocusGroupEntryTarget) {
                    const auto validation =
                        declarativeRenderer_->PrepareFocusEntry(
                            *snapshot, renderedFocusId, viewport, options);
                    const auto confirmed = validation.succeeded
                        ? interactionSession_.PreviewFocusGroupEntryRequest(
                            *focusGroupEntryAuthority, validation)
                        : widgetrail::input::FocusGroupEntryPreview{};
                    if (!confirmed.current ||
                        confirmed.target != preparedFocusGroupEntryTarget) {
                        (void)interactionSession_.RetireFocusGroupEntryRequest(
                            *focusGroupEntryAuthority);
                        focusGroupEntryPrepared = false;
                        focusGroupEntryWaiting = false;
                        preparedFocusGroupEntryTarget.reset();
                        renderedFocusId = provisionalRenderedFocusId;
                        interactionPresentation =
                            interactionSession_.PrepareRenderPresentation(
                                *snapshot, renderedFocusId, !inertRetainedSnapshot,
                                !inertRetainedSnapshot &&
                                    state_.focusRegion() ==
                                        widgetrail::FocusRegion::Widget);
                        options.sliderValueOverrides =
                            interactionPresentation.sliderValueOverrides;
                        options.pressedElementId =
                            interactionPresentation.pressedElementId;
                        options.activeSliderElementId =
                            interactionPresentation.activeSliderElementId;
                        AppendDiagnostic(
                            L"Focus-group entry request widget=" +
                            std::wstring{renderedWidget} +
                            L" state=settlement-retired");
                    }
                }
                const widgetrail::accessibility::ProjectionKey projectionKey{
                    std::wstring{renderedWidget},
                    descriptor
                        ? descriptor->runtimeGeneration
                        : std::wstring{},
                    snapshot->activeInputScopeId,
                    renderedFocusId,
                    snapshot->sequence,
                    interactionPresentation.sliderPresentationRevision,
                    appearanceState_.current() ? appearanceState_.current()->revision : 0,
                    viewport.x,
                    viewport.y,
                    viewport.width,
                    viewport.height,
                    geometry->panelWidth,
                    geometry->panelHeight,
                    physicalPixelsPerDip,
                    accessibilityPolicy.textScale,
                    accessibilityPolicy.minimumFontWeight,
                    accessibilityPolicy.reducedMotion,
                    accessibilityPolicy.reducedTransparency,
                };
                const bool collectAccessibility = !inertRetainedSnapshot &&
                    accessibilityActive_ && descriptor &&
                    widgetAccessibilityProjection_.ShouldCollect(projectionKey);
                options.collectAccessibility = collectAccessibility;
                auto result = declarativeRenderer_->Render(
                    renderTarget_.Get(), *snapshot, renderedFocusId, viewport, options);
                for (const auto& diagnostic : result.diagnostics) {
                    if (diagnostic.code.starts_with(L"background_crossfade_")) {
                        AppendDiagnostic(
                            L"Renderer " + std::wstring{renderedWidget} + L" " +
                            diagnostic.code + L" [" + diagnostic.nodeId + L"] " +
                            diagnostic.message);
                    }
                }
                currentCompositionRenderTiming_ = result.timing;
                if (options.suppressFocusedDescendantFollow &&
                    interactionSession_.freeScrollBinding()) {
                    const auto& binding = *interactionSession_.freeScrollBinding();
                    const auto scroll = result.scrollViewports.find(
                        binding.scrollId);
                    if (scroll == result.scrollViewports.end() ||
                        scroll->second.axis != binding.axis) {
                        ClearFreeScrollReentry(L"scroll-geometry-changed");
                        InvalidateRect(window_, nullptr, FALSE);
                    }
                }
                const auto& semanticSnapshot = *snapshot;
                bool focusGroupEntryMoved{};
                if (result.succeeded && focusGroupEntryPrepared &&
                    focusGroupEntryAuthority) {
                    const auto entry =
                        interactionSession_.CommitPreparedFocusGroupEntryRequest(
                            *focusGroupEntryAuthority,
                            preparedFocusGroupEntryTarget);
                    if (entry.consumed) {
                        const auto priorFocus =
                            interactionSession_.focusedElementId();
                        if (entry.target && *entry.target != priorFocus) {
                            ClearFreeScrollReentry(L"focus-group-entry-request");
                            const auto focus = interactionSession_.MoveFocus(
                                renderedWidget, semanticSnapshot, *entry.target,
                                false, false);
                            focusGroupEntryMoved = focus.changed;
                            (void)scrollEvidenceProbe_.RecordTarget(
                                *entry.target, L"focus-group-entry-request");
                        }
                        AppendDiagnostic(
                            L"Focus-group entry request widget=" +
                            std::wstring{renderedWidget} +
                            L" state=settled-consumed target=" +
                            (entry.target ? *entry.target : L"none"));
                    }
                }
                if (result.succeeded) {
                    const std::wstring inputOwner =
                        state_.focusRegion() == widgetrail::FocusRegion::Tray
                            ? L"tray"
                            : L"widget";
                    const std::wstring semanticFocus =
                        state_.focusRegion() == widgetrail::FocusRegion::Tray
                            ? L"tray:" + std::wstring(state_.selectedWidget())
                            : (inertRetainedSnapshot && !retainedRefreshFreeScroll) ||
                                    interactionSession_.focusedElementId().empty()
                                ? L"none"
                                : L"widget:" + interactionSession_.focusedElementId();
                    const bool selectedTrayItemVisible = trayLayout &&
                        std::any_of(
                            trayLayout->tiles.begin(), trayLayout->tiles.end(),
                            [&](const widgetrail::shell::TrayTileLayout& tile) {
                                return tile.slot == state_.selectedSlot();
                            });
                    const widgetrail::shell::TrayTileLayout* selectedTrayItem = nullptr;
                    if (trayLayout) {
                        const auto selected = std::find_if(
                            trayLayout->tiles.begin(), trayLayout->tiles.end(),
                            [&](const widgetrail::shell::TrayTileLayout& tile) {
                                return tile.slot == state_.selectedSlot();
                            });
                        if (selected != trayLayout->tiles.end()) {
                            selectedTrayItem = &*selected;
                        }
                    }
                    const std::wstring trayState = trayLayout
                        ? L"\n" + std::to_wstring(trayLayout->totalCount) + L"\n" +
                            std::to_wstring(trayLayout->tiles.size()) + L"\n" +
                            (trayLayout->previousOverflow ? L"1" : L"0") + L"\n" +
                            (trayLayout->nextOverflow ? L"1" : L"0") + L"\n" +
                            (selectedTrayItemVisible ? L"1" : L"0") + L"\n" +
                            std::to_wstring(width) + L"x" +
                            std::to_wstring(height) + L"\n" +
                            std::to_wstring(trayLayout->stripBounds.x) + L"," +
                            std::to_wstring(trayLayout->stripBounds.y) + L"," +
                            std::to_wstring(trayLayout->stripBounds.width) + L"," +
                            std::to_wstring(trayLayout->stripBounds.height)
                        : L"\nmissing";
                    const std::wstring responsivePresentationKey =
                        result.responsiveSurface
                            ? L"\nresponsive-surface\n" +
                                std::to_wstring(
                                    result.responsiveSurface->viewport.width) +
                                L"x" + std::to_wstring(
                                    result.responsiveSurface->viewport.height) +
                                L"\n" +
                                (result.responsiveSurface->mode ==
                                        widgetrail::ResponsiveSurfaceMode::Compact
                                    ? L"compact"
                                    : L"expanded")
                            : L"\nresponsive-surface\nunavailable";
                    const std::wstring paintKey =
                        std::wstring(widget) + L"\n" + std::wstring(renderedWidget) +
                        L"\n" + std::to_wstring(snapshot->sequence) + L"\n" +
                        (sessionRetainedSnapshot
                            ? (sessionPresentation.authority ==
                                    widgetrail::WidgetPresentationAuthority::FailureRetained
                                ? L"failure-retained" : L"refresh-retained")
                            : transitionRetainedSnapshot ? L"retained" : L"admitted") +
                        L"\n" + inputOwner + L"\n" + std::wstring(renderedFocusId) +
                        L"\n" + semanticFocus + trayState + responsivePresentationKey;
                    if (paintKey != lastWidgetPresentationPaintKey_) {
                        lastWidgetPresentationPaintKey_ = paintKey;
                        const auto actualTrayBounds = compositionChromeSession_
                            ? ProjectChromeClientBoundsToScreen(
                                compositionChromeSession_->trayClientBounds)
                            : std::nullopt;
                        const auto actualChromeBounds = ActualChromeWindowBounds();
                        const auto workClass = [&]() -> std::wstring_view {
                            if (!activeContentRenderPlan_) return L"full";
                            switch (activeContentRenderPlan_->work) {
                            case widgetrail::IncrementalPresentationWork::NoRaster:
                                return L"none";
                            case widgetrail::IncrementalPresentationWork::PaintOnly:
                                return L"paint-only";
                            case widgetrail::IncrementalPresentationWork::LocalLayout:
                                return L"local-layout";
                            case widgetrail::IncrementalPresentationWork::FullRaster:
                                return L"full";
                            }
                            return L"full";
                        }();
                        const auto damage = activeContentRenderPlan_
                            ? activeContentRenderPlan_->damage
                            : widgetrail::declarative::Rect{};
                        const auto bitmapCache =
                            declarativeRenderer_->GetImageBitmapCacheStats();
                        const auto pinnedBitmapCache =
                            pinnedSurfaceCoordinator_.GetImageBitmapCacheStats();
                        const auto decodedCache = imageCache_->GetStats();
                        std::unordered_set<std::wstring> currentArtworkSet;
                        CollectCurrentArtworkHandles(snapshot->root, currentArtworkSet);
                        for (const auto& layout : snapshot->pinnedLayouts) {
                            if (layout.root)
                                CollectCurrentArtworkHandles(
                                    *layout.root, currentArtworkSet);
                        }
                        const std::vector<std::wstring> currentArtworkHandles{
                            currentArtworkSet.begin(), currentArtworkSet.end()};
                        const auto currentArtwork =
                            imageCache_->GetTrustedArtworkResidency(
                                renderedWidget, currentArtworkHandles);
                        const auto bitmapDomain = [&]() -> std::wstring_view {
                            switch (bitmapCache.resourceDomain) {
                            case widgetrail::ImageBitmapResourceDomain::Device:
                                return L"device";
                            case widgetrail::ImageBitmapResourceDomain::RenderTarget:
                                return L"render-target";
                            case widgetrail::ImageBitmapResourceDomain::None:
                            default:
                                return L"none";
                            }
                        }();
                        AppendDiagnostic(
                            L"Widget presentation paint target=" + std::wstring(widget) +
                            L" content=" +
                            (sessionRetainedSnapshot
                                ? (sessionPresentation.authority ==
                                        widgetrail::WidgetPresentationAuthority::FailureRetained
                                    ? L"failure-retained" : L"refresh-retained")
                                : transitionRetainedSnapshot ? L"retained" : L"admitted") +
                            L" rendered=" + std::wstring(renderedWidget) +
                            L" sequence=" + std::to_wstring(snapshot->sequence) +
                            L" semantics=" +
                            (inertRetainedSnapshot ? L"inert" : L"current") +
                            L" input-owner=" + inputOwner +
                            L" selected=" + std::wstring(state_.selectedWidget()) +
                            L" visual-focus=" +
                            (renderedFocusId.empty()
                                ? std::wstring{L"none"}
                                : std::wstring{renderedFocusId}) +
                            L" semantic-focus=" + semanticFocus +
                            L" responsive-mode=" +
                            (result.responsiveSurface
                                ? result.responsiveSurface->mode ==
                                        widgetrail::ResponsiveSurfaceMode::Compact
                                    ? L"compact"
                                    : L"expanded"
                                : L"unavailable") +
                            L" responsive-surface=" +
                            (result.responsiveSurface
                                ? std::to_wstring(
                                      result.responsiveSurface->viewport.width) +
                                    L"x" + std::to_wstring(
                                      result.responsiveSurface->viewport.height)
                                : std::wstring{L"unavailable"}) +
                            L" work=" + std::wstring(workClass) +
                            L" damage=" + std::to_wstring(damage.x) + L"," +
                            std::to_wstring(damage.y) + L"," +
                            std::to_wstring(damage.width) + L"," +
                            std::to_wstring(damage.height) +
                            L" bitmap-entries=" +
                            std::to_wstring(bitmapCache.entries) +
                            L" bitmap-bytes=" +
                            std::to_wstring(bitmapCache.bytes) +
                            L" bitmap-hits=" +
                            std::to_wstring(bitmapCache.hits) +
                            L" bitmap-creates=" +
                            std::to_wstring(bitmapCache.creates) +
                            L" bitmap-evictions=" +
                            std::to_wstring(bitmapCache.evictions) +
                            L" bitmap-count-evictions=" +
                            std::to_wstring(bitmapCache.countPressureEvictions) +
                            L" bitmap-byte-evictions=" +
                            std::to_wstring(bitmapCache.bytePressureEvictions) +
                            L" bitmap-superseded-evictions=" +
                            std::to_wstring(bitmapCache.supersededArtworkEvictions) +
                            L" bitmap-entry-limit=" +
                            std::to_wstring(bitmapCache.maximumEntries) +
                            L" bitmap-entry-byte-limit=" +
                            std::to_wstring(bitmapCache.maximumEntryBytes) +
                            L" bitmap-cache-byte-limit=" +
                            std::to_wstring(bitmapCache.maximumBytes) +
                            L" bitmap-resource-domain=" + std::wstring(bitmapDomain) +
                            L" bitmap-resource-invalidations=" +
                            std::to_wstring(bitmapCache.resourceInvalidations) +
                            L" bitmap-resource-generation=" +
                            std::to_wstring(bitmapCache.resourceGeneration) +
                            L" bitmap-artwork-visible=" +
                            std::to_wstring(bitmapCache.visibleArtworkObservations) +
                            L" bitmap-artwork-clipped=" +
                            std::to_wstring(bitmapCache.clippedArtworkObservations) +
                            L" bitmap-requested-paint-pixels=" +
                            std::to_wstring(bitmapCache.requestedPaintPixels) +
                            L" bitmap-max-requested-paint-pixels=" +
                            std::to_wstring(bitmapCache.maximumRequestedPaintPixels) +
                            L" bitmap-max-requested-paint=" +
                            std::to_wstring(bitmapCache.maximumRequestedPaintWidth) + L"x" +
                            std::to_wstring(bitmapCache.maximumRequestedPaintHeight) +
                            L" pinned-bitmap-entries=" +
                            std::to_wstring(pinnedBitmapCache.entries) +
                            L" pinned-bitmap-bytes=" +
                            std::to_wstring(pinnedBitmapCache.bytes) +
                            L" pinned-bitmap-hits=" +
                            std::to_wstring(pinnedBitmapCache.hits) +
                            L" pinned-bitmap-creates=" +
                            std::to_wstring(pinnedBitmapCache.creates) +
                            L" pinned-bitmap-evictions=" +
                            std::to_wstring(pinnedBitmapCache.evictions) +
                            L" pinned-artwork-visible=" +
                            std::to_wstring(
                                pinnedBitmapCache.visibleArtworkObservations) +
                            L" pinned-artwork-clipped=" +
                            std::to_wstring(
                                pinnedBitmapCache.clippedArtworkObservations) +
                            L" pinned-requested-paint-pixels=" +
                            std::to_wstring(pinnedBitmapCache.requestedPaintPixels) +
                            L" pinned-max-requested-paint=" +
                            std::to_wstring(
                                pinnedBitmapCache.maximumRequestedPaintWidth) + L"x" +
                            std::to_wstring(
                                pinnedBitmapCache.maximumRequestedPaintHeight) +
                            L" decoded-entries=" +
                            std::to_wstring(decodedCache.entries) +
                            L" decoded-ready=" +
                            std::to_wstring(decodedCache.readyEntries) +
                            L" decoded-pending=" +
                            std::to_wstring(decodedCache.queuedOrLoading) +
                            L" decoded-failed=" +
                            std::to_wstring(decodedCache.failedEntries) +
                            L" decoded-bytes=" +
                            std::to_wstring(decodedCache.decodedBytes) +
                            L" decoded-https=" +
                            std::to_wstring(decodedCache.httpsEntries) +
                            L" decoded-inline=" +
                            std::to_wstring(decodedCache.inlineEntries) +
                            L" decoded-trusted=" +
                            std::to_wstring(decodedCache.trustedArtworkEntries) +
                            L" decoded-evictions=" +
                            std::to_wstring(decodedCache.evictions) +
                            L" decoded-count-evictions=" +
                            std::to_wstring(decodedCache.countPressureEvictions) +
                            L" decoded-byte-evictions=" +
                            std::to_wstring(decodedCache.bytePressureEvictions) +
                            L" decoded-superseded=" +
                            std::to_wstring(decodedCache.supersededEntries) +
                            L" decoded-count-rejections=" +
                            std::to_wstring(decodedCache.countCapacityRejections) +
                            L" decoded-pending-rejections=" +
                            std::to_wstring(decodedCache.pendingCapacityRejections) +
                            L" encoded-artwork-bytes=" +
                            std::to_wstring(decodedCache.encodedArtworkBytes) +
                            L" artwork-requests=" +
                            std::to_wstring(decodedCache.trustedArtworkRequests) +
                            L" artwork-hits=" +
                            std::to_wstring(decodedCache.trustedArtworkHits) +
                            L" artwork-supplies=" +
                            std::to_wstring(decodedCache.trustedArtworkSupplies) +
                            L" artwork-stale-completions=" +
                            std::to_wstring(decodedCache.staleArtworkCompletions) +
                            L" decoded-source-pixels=" +
                            std::to_wstring(decodedCache.readySourcePixels) +
                            L" decoded-max-source=" +
                            std::to_wstring(decodedCache.maximumSourceWidth) + L"x" +
                            std::to_wstring(decodedCache.maximumSourceHeight) +
                            L" current-artwork-entries=" +
                            std::to_wstring(currentArtwork.entries) +
                            L" current-artwork-encoded-bytes=" +
                            std::to_wstring(currentArtwork.encodedBytes) +
                            L" current-artwork-decoded-bytes=" +
                            std::to_wstring(currentArtwork.decodedBytes) +
                            L" current-artwork-ready=" +
                            std::to_wstring(currentArtwork.readyEntries) +
                            L" current-artwork-inflight=" +
                            std::to_wstring(currentArtwork.inFlightEntries) +
                            L" tray-total=" +
                            std::to_wstring(trayLayout ? trayLayout->totalCount : 0) +
                            L" tray-visible=" +
                            std::to_wstring(trayLayout ? trayLayout->tiles.size() : 0) +
                            L" tray-previous=" +
                            (trayLayout && trayLayout->previousOverflow ? L"true" : L"false") +
                            L" tray-next=" +
                            (trayLayout && trayLayout->nextOverflow ? L"true" : L"false") +
                            L" tray-selected-visible=" +
                            (selectedTrayItemVisible ? L"true" : L"false") +
                            L" shell-bounds=0,0," + std::to_wstring(width) + L"," +
                            std::to_wstring(height) +
                            L" desired-extent=" +
                            std::to_wstring(DesiredPresentationExtentDip().widthDip) +
                            L"x" +
                            std::to_wstring(DesiredPresentationExtentDip().heightDip) +
                            L" presented-extent=" +
                            std::to_wstring(PresentedPresentationExtentDip().widthDip) +
                            L"x" +
                            std::to_wstring(PresentedPresentationExtentDip().heightDip) +
                            L" body-bounds=" + std::to_wstring(geometry->panelX) + L"," +
                            std::to_wstring(geometry->panelY) + L"," +
                            std::to_wstring(geometry->panelWidth) + L"," +
                            std::to_wstring(geometry->panelHeight) +
                            L" viewport-bounds=" +
                            std::to_wstring(geometry->widgetViewportX) + L"," +
                            std::to_wstring(geometry->widgetViewportY) + L"," +
                            std::to_wstring(geometry->widgetViewportWidth) + L"," +
                            std::to_wstring(geometry->widgetViewportHeight) +
                            L" tray-bounds=" +
                            (actualTrayBounds
                                ? FormatPhysicalBounds(*actualTrayBounds)
                                : trayLayout
                                ? std::to_wstring(trayLayout->stripBounds.x) + L"," +
                                    std::to_wstring(trayLayout->stripBounds.y) + L"," +
                                    std::to_wstring(trayLayout->stripBounds.width) + L"," +
                                    std::to_wstring(trayLayout->stripBounds.height)
                                : std::wstring{L"missing"}) +
                            L" chrome-hwnd=" +
                            (actualChromeBounds
                                ? FormatPhysicalBounds(*actualChromeBounds)
                                : std::wstring{L"missing"}) +
                            L" chrome-placement-count=" +
                            std::to_wstring(fixedChromePlacementCount_) +
                            L" tray-selected-bounds=" +
                            (selectedTrayItem
                                ? std::to_wstring(selectedTrayItem->bounds.x) + L"," +
                                    std::to_wstring(selectedTrayItem->bounds.y) + L"," +
                                    std::to_wstring(selectedTrayItem->bounds.width) + L"," +
                                    std::to_wstring(selectedTrayItem->bounds.height)
                                : std::wstring{L"missing"}));
                    }
                }
                declarativeMotionActive_ = !inertRetainedSnapshot && result.animationActive;
                if (WidgetOwnsInputFocus(renderedWidget) &&
                    !textEntryModal_.active() && !inertRetainedSnapshot &&
                    !focusGroupEntryWaiting) {
                    if (!options.suppressFocusedDescendantFollow ||
                        focusGroupEntryMoved) {
                        (void)ReconcileResponsiveFocusPersistence(
                            renderedWidget, semanticSnapshot, result);
                        if (const auto visibleFocus = widgetrail::input::ResolveVisibleFocusTarget(
                            interactionSession_.focusedElementId(), semanticSnapshot.activeInputScopeId, result);
                            visibleFocus && *visibleFocus != interactionSession_.focusedElementId()) {
                            const auto focus = interactionSession_.MoveFocus(
                                widget, semanticSnapshot, *visibleFocus,
                                false, true);
                            (void)scrollEvidenceProbe_.RecordTarget(
                                *visibleFocus, L"reconcile");
                            // The completed pass used the old focus state. Schedule one
                            // more paint so the recovered target receives its ring.
                            InvalidateWidgetFocusChange(
                                focus.priorFocus, focus.sliderDamageNodeIds);
                        }
                    }
                }
                if (inertRetainedSnapshot) {
                    // A conservative retained-authority raster is deliberately
                    // not the committed Current visual checkpoint. Prevent a
                    // later refresh from treating those inert pixels as exact.
                    committedWidgetVisualState_.reset();
                    ClearAccessibilityTree();
                } else if (accessibilityActive_ && !result.succeeded) {
                    lastWidgetRenderResult_ = result;
                    ClearAccessibilityTree();
                } else {
                    lastWidgetRenderResult_ = result;
                }
                if (!inertRetainedSnapshot && result.succeeded) {
                    ReconcileScrollPaginationPrefetch(
                        widget, semanticSnapshot, *descriptor, result);
                }
                if (!inertRetainedSnapshot && result.succeeded) {
                    committedWidgetVisualState_ = CommittedWidgetVisualState{
                        std::wstring{renderedWidget},
                        snapshot->instanceId,
                        descriptor ? descriptor->runtimeGeneration : std::wstring{},
                        descriptor ? descriptor->presentationGeneration : std::wstring{},
                        std::wstring{renderedFocusId},
                        options.pressedElementId,
                        options.activeSliderElementId,
                        snapshot->sequence,
                        interactionPresentation.sliderPresentationRevision,
                        appearanceState_.current()
                            ? appearanceState_.current()->revision : 0,
                        result.scrollOffsets,
                        state_.focusRegion(),
                        result.animationActive,
                        presentationTransaction_.contentPlacement(),
                    };
                }
                if (!inertRetainedSnapshot && renderedFocusId == interactionSession_.focusedElementId()) {
                    (void)scrollEvidenceProbe_.Publish(
                        widget, semanticSnapshot, result, renderedFocusId,
                        options.pixelScale, options.accessibility.textScale);
                }
                if (!inertRetainedSnapshot && collectAccessibility) {
                    const auto selectPopup = CurrentSelectPopupAccessibility(
                        width, height);
                    widgetAccessibilityTree_ = widgetrail::accessibility::BuildWidgetTree(
                        std::wstring{widget}, descriptor->runtimeGeneration,
                        semanticSnapshot, result,
                        state_.focusRegion() == widgetrail::FocusRegion::Widget
                            ? std::wstring_view{interactionSession_.focusedElementId()}
                            : std::wstring_view{},
                        options.sliderValueOverrides,
                        selectPopup ? &*selectPopup : nullptr,
                        interactionPresentation.activeSliderElementId);
                    ++widgetAccessibilityRevision_;
                    widgetAccessibilityProjection_.Published(projectionKey);
                }
                if (!inertRetainedSnapshot && accessibilityActive_ &&
                    widgetAccessibilityProjection_.ObserveFrame(result.animationActive))
                    InvalidateRect(window_, nullptr, FALSE);
                const auto lastSequence = renderedSnapshotSequences_.find(std::wstring(widget));
                if (!inertRetainedSnapshot &&
                    (lastSequence == renderedSnapshotSequences_.end() ||
                     lastSequence->second != snapshot->sequence)) {
                    for (const auto& diagnostic : result.diagnostics) {
                        if (diagnostic.code.starts_with(L"background_crossfade_"))
                            continue;
                        AppendDiagnostic(
                            L"Renderer " + std::wstring(widget) + L" " + diagnostic.code + L" [" + diagnostic.nodeId +
                            L"] " + diagnostic.message);
                    }
                    renderedSnapshotSequences_.insert_or_assign(
                        std::wstring(widget), snapshot->sequence);
                }
            } else {
                ClearAccessibilityTree();
                DrawTextLine(sessions_.Failure(widget)
                                 ? std::wstring{L"Widget unavailable. Press A to retry."}
                                 : L"Starting isolated " +
                                       std::wstring(DisplayWidgetName(widget)) + L" widget…",
                             bodyFormat_.Get(),
                             D2D1::RectF(panelLeft + 30, 52,
                                         panelLeft + panelWidth - 30, 110),
                             secondaryBrush_.Get());
            }
            if (contentLayerPushed) renderTarget_->PopLayer();
            if (widgetContextMenu_)
                DrawWidgetContextMenu(width, height);
            if (interactionSession_.selectPopup())
                DrawSelectPopup(width, height);
            if (drawGuide) DrawWidgetFooter(
                *geometry, fixedGuideBounds ? &*fixedGuideBounds : nullptr);
            if (drawTray) {
                DrawIconStrip(width, height, &*geometry, nullptr,
                              trayLayout ? &*trayLayout : nullptr);
            } else if (accessibilityActive_ && trayLayout) {
                PublishTrayAccessibility(*trayLayout, width, height);
            }
            return;
        }

        DrawTextLine(L"Widget unavailable", titleFormat_.Get(),
                     D2D1::RectF(panelLeft + 30, 48, panelLeft + panelWidth - 30, 88),
                     textBrush_.Get());
        DrawTextLine(L"This widget is no longer present in the active catalog.", bodyFormat_.Get(),
                     D2D1::RectF(panelLeft + 30, 94, panelLeft + panelWidth - 30, 122),
                     secondaryBrush_.Get());
        DrawTextLine(L"Return to the dashboard while the catalog is refreshed.",
                     bodyFormat_.Get(),
                     D2D1::RectF(panelLeft + 30, 146, panelLeft + panelWidth - 30, 202),
                     secondaryBrush_.Get());
        if (drawGuide) DrawWidgetFooter(
            *geometry, fixedGuideBounds ? &*fixedGuideBounds : nullptr);
        if (drawTray) {
            DrawIconStrip(width, height, &*geometry, nullptr,
                          trayLayout ? &*trayLayout : nullptr);
        } else if (accessibilityActive_ && trayLayout) {
            PublishTrayAccessibility(*trayLayout, width, height);
        }
    }

    HINSTANCE instance_{};
    HWND window_{};
    HWND chromeWindow_{};
    WidgetRailOverlayPlatformHandle* platform_{};
#if defined(WRAIL_PINNED_SLIDER_ROUTE_TESTING)
    std::optional<WidgetRailOverlayPlatformControllerFrame> testControllerFrame_;
#endif
    widgetrail::PlacementRefreshGate placementRefreshGate_;
    widgetrail::DisplayRefreshAccumulator displayRefresh_;
    HWND backdropWindow_{};
    HBRUSH backdropBrush_{};
    HWINEVENTHOOK foregroundHook_{};
    inline static OverlayApp* foregroundEventApp_{};
    std::wstring initializationError_;
    std::wstring installationDirectory_;
    std::optional<std::wstring> developmentCatalogRoot_;
    std::optional<std::wstring> developmentReadyPath_;
    std::optional<std::wstring> developmentReadyNonce_;
    std::optional<std::wstring> developmentWidgetId_;
    std::optional<std::wstring> developmentWidgetInstance_;
    bool developmentProbeOnly_{};
    std::optional<std::wstring> performanceState_;
    std::optional<std::wstring> performanceWidgetId_;
    std::optional<std::wstring> performanceDiagnosticsPath_;
    std::optional<std::wstring> performanceDiagnosticsNonce_;
    std::optional<std::wstring> scrollEvidencePath_;
    widgetrail::ScrollEvidenceProbe scrollEvidenceProbe_;
    bool richMediaProof_{};
    bool artworkRenderDiagnostics_{};
    static constexpr std::uint32_t kMaximumArtworkRenderDiagnosticRecords = 64;
    std::atomic<std::uint32_t> artworkRenderDiagnosticRecordCount_{};
    std::wstring richMediaProfileDirectory_;
    widgetrail::richmedia::RichMediaEnvironmentHandle richMediaEnvironment_{
        widgetrail::richmedia::RichMediaSurfaceCoordinator::CreateSharedEnvironment()};
    widgetrail::media::MediaSessionManager mediaSessions_{richMediaEnvironment_};
    std::uint64_t retiredSharedMediaEnvironmentGeneration_{};
    std::optional<EmbeddedMediaSessionKey> richMediaProofSessionKey_;
    std::optional<EmbeddedMediaSessionKey> embeddedMediaAccessibilityOwner_;
    long long embeddedMediaSessionPlaybackEventSequence_{};
    bool performanceCountersActive_{};
    unsigned long long performanceCounterStarted_{};
    unsigned long long performanceTimerMessages_{};
    unsigned long long performanceControllerTimerMessages_{};
    unsigned long long performanceGuideCompatibilityTimerMessages_{};
    unsigned long long performancePaintMessages_{};
    unsigned long long performanceSuccessfulFrames_{};
    widgetrail::OverlayState state_;
    widgetrail::input::TrayYGesture trayYGesture_;
    widgetrail::input::HeldButtonActionRepeat heldActionRepeat_;
    std::optional<HeldActionAuthority> heldActionAuthority_;
    int lastHeldRepeatVerdict_{-1};
    widgetrail::input::WidgetInteractionSession interactionSession_;
    std::wstring rightStickDropSignature_;
    std::uint64_t rightStickDropCount_{};
    std::optional<bool> lastForegroundOwnership_;
    long long controllerSequence_{};
    std::wstring lastActionMessage_;
    std::wstring lastActionWidgetId_;
    ULONGLONG lastActionExpiresAt_{};
    std::optional<TrayContextMenuState> trayContextMenu_;
    std::optional<WidgetContextMenuState> widgetContextMenu_;
    widgetrail::input::StickNavigator placementMoveStickNavigator_{
        widgetrail::input::kPinnedPlacementNavigationOptions};
    widgetrail::input::StickNavigator placementDpadNavigator_{
        widgetrail::input::kPinnedPlacementNavigationOptions};
    widgetrail::input::StickNavigator placementRightStickNavigator_{
        widgetrail::input::kPinnedPlacementNavigationOptions};
    bool placementNavigationPrimed_{};
    std::wstring admissionTraceWidget_;
    std::uint64_t admissionTraceCorrelationId_{};
    std::optional<PendingWidgetSwitchSnap> pendingWidgetSwitchSnap_;
    widgetrail::WidgetActionFeedbackHost actionFailureFeedback_;
    widgetrail::WidgetAdmissionTrace admissionTrace_;
    widgetrail::accessibility::Tree accessibilityTree_;
    widgetrail::accessibility::Tree widgetAccessibilityTree_;
    widgetrail::accessibility::OpenWidgetSemantics openWidgetAccessibility_;
    widgetrail::accessibility::ProviderHost accessibilityProvider_;
    widgetrail::accessibility::ProviderHost chromeAccessibilityProvider_;
    bool accessibilityActive_{};
    widgetrail::accessibility::ProjectionTracker accessibilityProjection_;
    widgetrail::accessibility::ProjectionTracker widgetAccessibilityProjection_;
    std::uint64_t widgetAccessibilityRevision_{};
    long long hostAccessibilitySequence_{};
    widgetrail::input::TextEntryModal textEntryModal_;
    std::optional<TextEntryModalAuthority> textEntryModalAuthority_;
    TextEntryControllerPhase textEntryControllerPhase_{
        TextEntryControllerPhase::None};
    widgetrail::WidgetBridgeClient bridge_;
    widgetrail::WidgetSessionCoordinator sessions_;
    widgetrail::packages::FileOpenDialogWidgetPackagePicker localWidgetPackagePicker_;
    widgetrail::packages::LocalWidgetPackageImport localWidgetPackageImport_;
    std::optional<widgetrail::LocalWidgetPackageInstallResult>
        lastLocalWidgetPackageInstallResult_;
    widgetrail::PlatformAppearanceState appearanceState_;
    widgetrail::NativeRenderStyle canvasStyle_;
    widgetrail::NativeRenderStyle backdropStyle_;
    widgetrail::NativeRenderStyle panelStyle_;
    widgetrail::NativeRenderStyle trayStyle_;
    widgetrail::NativeColor effectiveCanvasBackground_{kDefaultCanvas};
    widgetrail::NativeColor effectivePanelBackground_{kDefaultPanel};
    widgetrail::NativeColor effectiveTrayBackground_{kDefaultCanvas};
    widgetrail::NativeRenderStyle trayItemStyle_;
    widgetrail::NativeRenderStyle trayItemSelectedStyle_;
    widgetrail::NativeRenderStyle trayItemFocusedStyle_;
    widgetrail::NativeRenderStyle trayItemSelectedFocusedStyle_;
    widgetrail::NativeRenderStyle titleStyle_;
    widgetrail::NativeRenderStyle bodyStyle_;
    widgetrail::NativeRenderStyle hintStyle_;
    widgetrail::NativeRenderStyle statusStyle_;
    widgetrail::NativeRenderStyle dashboardTitleStyle_;
    widgetrail::NativeRenderStyle dashboardHintStyle_;
    float panelCornerRadius_{18.0F};
    float trayCornerRadius_{22.0F};
    float trayItemCornerRadius_{16.0F};
    float focusOutlineWidth_{2.0F};
    std::unique_ptr<widgetrail::RemoteImageCache> imageCache_;
    std::unique_ptr<widgetrail::DeclarativeRenderer> declarativeRenderer_;
    widgetrail::pinned::WidgetSurfaceCoordinator pinnedSurfaceCoordinator_;
    std::wstring pinnedWorkDiagnosticWidgetId_;
    std::uint64_t pinnedWorkDiagnosticPaintBucket_{};
    std::uint64_t pinnedWorkDiagnosticMediaReconciliations_{};
    bool pinnedWorkDiagnosticPublished_{};
    bool runtimeInitialized_{};
    std::unordered_map<std::wstring, long long> renderedSnapshotSequences_;
    widgetrail::RenderResult lastWidgetRenderResult_;
    std::optional<widgetrail::DeclarativeRenderTiming>
        currentCompositionRenderTiming_;
    std::optional<CommittedWidgetVisualState> committedWidgetVisualState_;
    std::optional<CommittedFullscreenPresentationCheckpoint>
        committedFullscreenPresentation_;
    std::optional<widgetrail::WidgetPresentationImpact>
        pendingWidgetPresentationImpact_;
    std::optional<widgetrail::IncrementalPresentationPlan>
        pendingContentRenderPlan_;
    std::optional<widgetrail::IncrementalPresentationPlan>
        activeContentRenderPlan_;
    bool declarativeMotionActive_{};
    widgetrail::OverlayTransitionTimeline overlayTransition_;
    widgetrail::OverlayTransitionSample overlayTransitionSample_{};
    widgetrail::OverlayPresentationTransaction presentationTransaction_;
    std::wstring retainedGuidePaintKey_;
    std::optional<widgetrail::shell::RetainedTrayState> retainedTrayPaintState_;
    std::optional<FixedChromeAnchor> fixedChromeAnchor_;
    std::optional<CompositionChromeSession> compositionChromeSession_;
    FixedChromePlacementReason pendingFixedChromePlacementReason_{
        FixedChromePlacementReason::NewVisibleSession};
    std::uint64_t fixedChromePlacementCount_{};
    std::optional<widgetrail::accessibility::ProjectionKey>
        pendingActionFailureAccessibilityProjection_;
    bool awaitingSuccessfulOpenPaint_{};
    ULONGLONG nextOpenPaintRetryAt_{};
    std::wstring pendingContentRevealWidget_;
    mutable std::optional<WidgetSurfaceResolutionCache>
        widgetSurfaceResolutionCache_;
    std::wstring lastWidgetPresentationPaintKey_;
    std::wstring lastSurfaceAppearanceDiagnostic_;
    std::wstring lastFallbackPresentationCheckpointKey_;
    BYTE targetOverlayOpacity_{248};
    BYTE targetBackdropOpacity_{kBackdropOpacity};
    std::optional<BYTE> appliedOverlayOpacity_;
    std::optional<BYTE> appliedBackdropOpacity_;

    ComPtr<ID2D1Factory1> d2dFactory_;
    ComPtr<IDWriteFactory> writeFactory_;
    widgetrail::OverlayCompositionSurface compositionSurface_;
    widgetrail::CompositorBackgroundSurfaceCoordinator
        compositorBackgroundCoordinator_;
    bool compositionPlacementInProgress_{};
    ComPtr<ID2D1HwndRenderTarget> hwndRenderTarget_;
    ComPtr<ID2D1RenderTarget> renderTarget_;
    ComPtr<ID2D1SolidColorBrush> backgroundBrush_;
    ComPtr<ID2D1SolidColorBrush> cardBrush_;
    ComPtr<ID2D1SolidColorBrush> solidCardBrush_;
    ComPtr<ID2D1SolidColorBrush> textBrush_;
    ComPtr<ID2D1SolidColorBrush> secondaryBrush_;
    ComPtr<ID2D1SolidColorBrush> dashboardTextBrush_;
    ComPtr<ID2D1SolidColorBrush> dashboardSecondaryBrush_;
    ComPtr<ID2D1SolidColorBrush> accentBrush_;
    ComPtr<ID2D1SolidColorBrush> successBrush_;
    ComPtr<ID2D1SolidColorBrush> trayItemBrush_;
    ComPtr<ID2D1SolidColorBrush> trayItemTextBrush_;
    ComPtr<ID2D1SolidColorBrush> traySelectedBrush_;
    ComPtr<ID2D1SolidColorBrush> traySelectedTextBrush_;
    ComPtr<ID2D1SolidColorBrush> selectedTextBrush_;
    ComPtr<ID2D1SolidColorBrush> focusBrush_;
    ComPtr<IDWriteTextFormat> titleFormat_;
    ComPtr<IDWriteTextFormat> bodyFormat_;
    ComPtr<IDWriteTextFormat> hintFormat_;
    ComPtr<IDWriteTextFormat> iconFormat_;
};

} // namespace

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int showCommand) {
    std::wstring processProfile = L"production";
    bool processOwnerProbe = false;
    for (int index = 1; index < __argc; ++index) {
        if (_wcsicmp(__wargv[index], L"--process-profile") == 0 &&
            index + 1 < __argc) {
            processProfile = __wargv[++index];
        } else if (_wcsicmp(__wargv[index], L"--process-owner-probe") == 0) {
            processOwnerProbe = true;
        }
    }
    widgetrail::process::OverlayProcessOwner processOwner;
    std::wstring ownershipError;
    const auto ownership = processOwner.Begin(
        processProfile, std::chrono::milliseconds(3000), ownershipError);
    if (ownership == widgetrail::process::OwnershipResult::ClientAcknowledged) {
        AppendDiagnostic(
            L"Activation client forwarded Show and exited profile=" + processProfile +
            L" pid=" + std::to_wstring(GetCurrentProcessId()));
        return EXIT_SUCCESS;
    }
    if (ownership != widgetrail::process::OwnershipResult::Owner) {
        AppendDiagnostic(L"Activation client failed: " + ownershipError);
        return EXIT_FAILURE;
    }
    AppendDiagnostic(
        L"OverlayHost process owner elected profile=" + processProfile +
        L" pid=" + std::to_wstring(GetCurrentProcessId()));
    if (processOwnerProbe) {
        const bool received = processOwner.WaitForShow(std::chrono::seconds(10));
        AppendDiagnostic(received
            ? L"Process-owner probe received Show without initializing OverlayApp"
            : L"Process-owner probe timed out");
        return received ? EXIT_SUCCESS : EXIT_FAILURE;
    }
    OverlayApp app;
    if (!app.Initialize(instance, showCommand)) {
        const std::wstring message = L"OverlayHost failed to initialize.\n\n" +
                                     app.initializationError();
        SaveStartupError(message);
        MessageBoxW(nullptr, message.c_str(),
                    L"WidgetRail", MB_OK | MB_ICONERROR);
        return EXIT_FAILURE;
    }
    app.BindProcessActivation(processOwner);
    return app.Run();
}
