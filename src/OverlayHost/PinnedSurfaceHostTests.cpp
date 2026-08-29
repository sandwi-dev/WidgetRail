#include "AccessibilityProvider.h"
#include "ControllerNavigation.h"
#include "OverlayState.h"
#include "PinnedSurfacePolicy.h"

#include <Windows.h>
#include <UIAutomation.h>
#include <psapi.h>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstddef>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <limits>
#include <numeric>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

namespace fs = std::filesystem;

namespace {

constexpr wchar_t kFixtureClass[] = L"WidgetRailPinnedSurfaceFeasibilityFixture";
constexpr wchar_t kMainClass[] = L"WidgetRailPinnedSurfaceMainOverlayFixture";
constexpr UINT kAccessibilityActionMessage = WM_APP + 0x115;
constexpr auto kIdleObservation = std::chrono::milliseconds(750);
constexpr std::size_t kSemanticUpdates = 256;
constexpr double kDlv016ProjectionP95MaximumMilliseconds = 0.591;
constexpr std::size_t kDlv016VisibleIdlePrivateWorkingSetMaximum =
    1'101'005; // 1.05 MiB, accepted DLV-016 five-process maximum.
constexpr double kControllerResponseBudgetMilliseconds = 50.0;
constexpr std::size_t kMaterialPrivateWorkingSetBytes = 128ULL * 1024ULL * 1024ULL;

int checks{};

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) throw std::runtime_error(std::string(message));
}

std::string ReadSource(const fs::path& path) {
    std::ifstream stream(path, std::ios::binary);
    Check(static_cast<bool>(stream), "host routing source opens");
    return {std::istreambuf_iterator<char>(stream), {}};
}

void TestAcceptedCompactMediaHostContract() {
    const auto source = ReadSource(
        fs::path{__FILE__}.parent_path() / "main.cpp");
    const auto has = [&](const std::string_view text) {
        return source.find(text) != std::string::npos;
    };
    Check(has("if (current == std::numeric_limits<long long>::max()) return std::nullopt;") &&
              has("return current + 1;") &&
              has("NextEmbeddedMediaPlaybackObservationSequence(\n                embeddedMediaPlaybackEventSequence_)") &&
              has("embeddedMediaPlaybackEventSequence_ = publishedEventSequence;") &&
              has("event.mediaKey, event.state, event.positionSeconds") &&
              has("origin->commandSequence != commandSequence") &&
              has("origin->mediaKey != event.mediaKey"),
          "page-event restart projects onto a host-monotonic sequence, fails closed at exhaustion, and retains command/media/origin authority");
    Check(has("XINPUT_GAMEPAD_X") &&
              has("Command::TogglePlayback") &&
              has("XINPUT_GAMEPAD_LEFT_SHOULDER") &&
              has("Command::NavigatePrevious") &&
              has("XINPUT_GAMEPAD_RIGHT_SHOULDER") &&
              has("Command::NavigateNext") &&
              has("frame.leftTriggerPressed") &&
              has("CompactMediaSeekTarget(\n                                widgetrail::input::NavigationDirection::Left)") &&
              has("frame.rightTriggerPressed") &&
              has("CompactMediaSeekTarget(\n                                widgetrail::input::NavigationDirection::Right)"),
          "compact X, LB/RB, and LT/RT retain the accepted typed command routes");
    Check(has("Compact pinned media returned to click-through") &&
              has("pinnedControllerCommand ==\n                    widgetrail::pinned::ControllerCommand::Exit") &&
              has("Dispatch(widgetrail::Command::SampleWidgetBack);") &&
              !has("BeginCompactMediaScrub()") &&
              !has("StepCompactMediaScrub(") &&
              !has("CommitCompactMediaScrub()"),
          "compact B exits to click-through, View uses the tray route, and A/navigation expose no scrub mapping");
}

void TestAcceptedOverlayFullscreenMediaHostContract() {
    const auto source = ReadSource(
        fs::path{__FILE__}.parent_path() / "main.cpp");
    const auto requireOrdered = [&](const std::string_view first,
                                    const std::string_view second,
                                    const std::string_view message) {
        const auto firstOffset = source.find(first);
        const auto secondOffset = source.find(second, firstOffset);
        Check(firstOffset != std::string::npos && secondOffset != std::string::npos &&
                  firstOffset < secondOffset,
              message);
    };
    const auto section = [&](const std::string_view begin,
                             const std::string_view end) {
        const auto beginOffset = source.find(begin);
        Check(beginOffset != std::string::npos,
              "fullscreen host contract section begins");
        const auto endOffset = source.find(end, beginOffset + begin.size());
        Check(endOffset != std::string::npos,
              "fullscreen host contract section ends");
        return source.substr(beginOffset, endOffset - beginOffset);
    };

    const auto fullscreenInput = section(
        "if (OverlayFullscreenMediaRequested()) {",
        "const auto pinnedControllerCommand");
    Check(fullscreenInput.find("DispatchControllerAction(L\"B\", true);") !=
              std::string::npos &&
              fullscreenInput.find("Command::TogglePlayback") != std::string::npos &&
              fullscreenInput.find("frame.leftTriggerPressed") != std::string::npos &&
              fullscreenInput.find("NavigationDirection::Left") != std::string::npos &&
              fullscreenInput.find("frame.rightTriggerPressed") != std::string::npos &&
              fullscreenInput.find("NavigationDirection::Right") != std::string::npos &&
              fullscreenInput.find("XINPUT_GAMEPAD_BACK") == std::string::npos,
          "fullscreen routes B, X, LT, and RT while View cannot reach hidden tray routing");

    const auto fullscreenPaint = section(
        "const bool overlayFullscreen = OverlayFullscreenMediaRequested();",
        "if (layer == CompositionPaintLayer::Tray && trayLayout)");
    Check(fullscreenPaint.find(
              "overlayFullscreen && layer == CompositionPaintLayer::Tray") !=
              std::string::npos &&
              fullscreenPaint.find(
                  "overlayFullscreen && layer == CompositionPaintLayer::Guide") !=
              std::string::npos &&
              fullscreenPaint.find("ClearAccessibilityTree();") != std::string::npos &&
              fullscreenPaint.find("ResolveOverlayFullscreenMediaSurfaceBounds") !=
              std::string::npos &&
              fullscreenPaint.find("mediaViewportRegions.push_back") !=
              std::string::npos,
          "fullscreen suppresses tray, guide, and widget accessibility while retaining one media viewport");

    requireOrdered(
        "CommittedOverlayFullscreenMediaAuthorityCurrent(priorVisibleWidget)",
        "std::forward<Refresh>(refresh)();",
        "fullscreen exit captures committed visual authority before mutable refresh");
    requireOrdered(
        "const bool settleOverlayFullscreenExit =",
        "presentationTransaction_.SettleExtent(",
        "fullscreen exit derives and settles the committed-authority transition");
    requireOrdered(
        "presentationTransaction_.SettleExtent(",
        "ApplyPresentation(presentation);",
        "fullscreen exit settles before composition admission");
    Check(source.find("!settleOverlayFullscreenExit;") != std::string::npos &&
              source.find("committedOverlayFullscreenMediaPresentation_ ||\n            replacedOverlayFullscreenPresentation") !=
              std::string::npos &&
              source.find("ReconcileCommittedEmbeddedMediaSurface();") !=
              std::string::npos,
          "fullscreen exit suppresses motion and immediately reconciles the retained external media plane");
}

void TestAcceptedWidgetOwnedFocusMemoryHostContract() {
    const auto source = ReadSource(
        fs::path{__FILE__}.parent_path() / "main.cpp");
    const auto section = [&](const std::string_view begin,
                             const std::string_view end) {
        const auto beginOffset = source.find(begin);
        Check(beginOffset != std::string::npos,
              "widget focus ownership section begins");
        const auto endOffset = source.find(end, beginOffset + begin.size());
        Check(endOffset != std::string::npos,
              "widget focus ownership section ends");
        return source.substr(beginOffset, endOffset - beginOffset);
    };

    const auto focusOwner = section(
        "[[nodiscard]] bool WidgetOwnsInputFocus(",
        "void HandleAccessibilityActions()");
    Check(focusOwner.find(
              "state_.surface() == widgetrail::Surface::Widget") !=
              std::string::npos &&
              focusOwner.find(
                  "state_.focusRegion() == widgetrail::FocusRegion::Widget") !=
              std::string::npos &&
              focusOwner.find("state_.activeWidget() == widgetId") !=
              std::string::npos,
          "focus memory authority requires the exact active widget-owned surface");
    Check(focusOwner.find(
              "void RememberCurrentFocus(const std::wstring_view widgetId) {\n"
              "        if (!WidgetOwnsInputFocus(widgetId)) return;") !=
              std::string::npos &&
              focusOwner.find(
                  "void RestoreFocusForActiveSurface(const std::wstring_view widgetId) {\n"
                  "        if (!WidgetOwnsInputFocus(widgetId)) return;") !=
              std::string::npos,
          "tray-owned cycling cannot overwrite or restore widget focus memory");

    const auto renderReconciliation = section(
        "declarativeMotionActive_ = !inertRetainedSnapshot && result.animationActive;",
        "if (inertRetainedSnapshot) {");
    Check(renderReconciliation.find(
              "if (WidgetOwnsInputFocus(renderedWidget) &&") !=
              std::string::npos &&
              renderReconciliation.find("ReconcileResponsiveFocusPersistence(") !=
              std::string::npos &&
              renderReconciliation.find("ResolveVisibleFocusTarget(") !=
              std::string::npos,
          "fresh Current rendering cannot reconcile focus while Tray owns input");

    const auto stateTransition = section(
        "template <typename Mutation>\n    void ApplyStateTransition(",
        "void ApplyPresentation(");
    const auto remember = stateTransition.find(
        "if (priorSurface == widgetrail::Surface::Widget && IsBridgeWidget(priorActive)) {\n"
        "            RememberCurrentFocus(priorActive);");
    const auto clearLiveFocus = stateTransition.find(
        "interactionSession_.ClearFocus();", remember);
    const auto reopenAuthority = stateTransition.find(
        "const bool reopenedWidgetFocus =\n"
        "            priorSurface == widgetrail::Surface::Hidden &&\n"
        "            state_.surface() == widgetrail::Surface::Widget &&\n"
        "            state_.focusRegion() == widgetrail::FocusRegion::Widget;",
        clearLiveFocus);
    const auto restore = stateTransition.find(
        "(priorFocusRegion != state_.focusRegion() || reopenedWidgetFocus)) {\n"
        "            RestoreFocusForActiveSurface(state_.activeWidget());",
        reopenAuthority);
    Check(remember != std::string::npos &&
              clearLiveFocus != std::string::npos &&
              reopenAuthority != std::string::npos &&
              restore != std::string::npos &&
              remember < clearLiveFocus && clearLiveFocus < reopenAuthority &&
              reopenAuthority < restore,
          "widget hide remembers exact focus, clears only live focus, and restores on exact Hidden-to-Widget authority before presentation");
}

void TestAcceptedMediaBackOwnershipHostContract() {
    const auto source = ReadSource(
        fs::path{__FILE__}.parent_path() / "main.cpp");
    const auto dispatchBegin = source.find(
        "void DispatchControllerAction(");
    const auto dispatchEnd = source.find(
        "[[nodiscard]] bool TryDispatchNativeMediaAction(", dispatchBegin);
    Check(dispatchBegin != std::string::npos &&
              dispatchEnd != std::string::npos && dispatchBegin < dispatchEnd,
          "controller action owner has one bounded source section");
    const auto dispatch = source.substr(dispatchBegin, dispatchEnd - dispatchBegin);
    const auto fullscreenBack = dispatch.find(
        "button == L\"B\" && OverlayFullscreenMediaRequested()");
    const auto mediaBack = dispatch.find(
        "button == L\"B\" && EmbeddedMediaAuthorityCurrent()");
    const auto genericRoute = dispatch.find(
        "using widgetrail::input::ControllerActionContext", mediaBack);
    Check(fullscreenBack != std::string::npos &&
              mediaBack != std::string::npos && genericRoute != std::string::npos &&
              fullscreenBack < mediaBack && mediaBack < genericRoute,
          "fullscreen, media Back, and generic controller routes retain exact ownership order");
    const auto mediaBackBranch = dispatch.substr(mediaBack, genericRoute - mediaBack);
    Check(mediaBackBranch.find(
              "embeddedMediaAuthority_->projection == EmbeddedMediaProjection::Overlay") !=
              std::string::npos &&
              mediaBackBranch.find(
                  "state_.surface() == widgetrail::Surface::Widget") !=
              std::string::npos &&
              mediaBackBranch.find(
                  "state_.focusRegion() == widgetrail::FocusRegion::Widget") !=
              std::string::npos &&
              mediaBackBranch.find(
                  "state_.activeWidget() == embeddedMediaAuthority_->widgetId") !=
              std::string::npos &&
              mediaBackBranch.find(
                  "Dispatch(widgetrail::Command::SampleWidgetBack);") !=
              std::string::npos,
          "media Back shortcut requires exact overlay media and widget-owned focus authority");
    Check(mediaBackBranch.find("youtube") == std::string::npos &&
              mediaBackBranch.find("YouTube") == std::string::npos,
          "media Back ownership remains provider-neutral");
    Check(dispatch.find(
              "const auto context = state_.focusRegion() == widgetrail::FocusRegion::Tray") !=
              std::string::npos &&
              dispatch.find("case ControllerActionRoute::HostCloseOverlay:") !=
              std::string::npos &&
              dispatch.find("Dispatch(widgetrail::Command::ToggleOverlay);") !=
              std::string::npos,
          "tray-owned B retains the generic host close route after media Back");

    using widgetrail::Command;
    using widgetrail::FocusRegion;
    using widgetrail::OverlayState;
    using widgetrail::Surface;
    using widgetrail::input::ControllerActionContext;
    using widgetrail::input::ControllerActionRoute;
    using widgetrail::input::RouteControllerAction;

    OverlayState mediaState({}, {L"media-peer", L"ordinary-peer"});
    (void)mediaState.Dispatch(Command::ToggleOverlay);
    (void)mediaState.Dispatch(Command::Activate);
    const auto mediaBackEligible = [&](const bool authorityCurrent,
                                       const bool overlayProjection) {
        return authorityCurrent && overlayProjection &&
            mediaState.surface() == Surface::Widget &&
            mediaState.focusRegion() == FocusRegion::Widget &&
            mediaState.activeWidget() == L"media-peer";
    };
    Check(mediaBackEligible(true, true),
          "active provider-neutral media widget admits its first media Back");
    if (mediaBackEligible(true, true))
        (void)mediaState.Dispatch(Command::SampleWidgetBack);
    Check(mediaState.surface() == Surface::Widget &&
              mediaState.focusRegion() == FocusRegion::Tray,
          "first media Back preserves the widget and returns focus to Tray exactly once");
    Check(!mediaBackEligible(true, true),
          "media Back shortcut is no longer eligible after Tray takes focus");
    const auto trayBack = RouteControllerAction(ControllerActionContext::Tray, L"B");
    Check(trayBack == ControllerActionRoute::HostCloseOverlay,
          "second B at Tray resolves to exactly one host-owned close");
    if (trayBack == ControllerActionRoute::HostCloseOverlay)
        (void)mediaState.Dispatch(Command::ToggleOverlay);
    Check(mediaState.surface() == Surface::Hidden,
          "second B closes the overlay without replaying media Back");

    (void)mediaState.Dispatch(Command::ToggleOverlay);
    Check(mediaState.surface() == Surface::Widget &&
              mediaState.focusRegion() == FocusRegion::Tray,
          "reopen restores the visible media widget with Tray input authority");
    (void)mediaState.Dispatch(Command::Activate);
    Check(mediaBackEligible(true, true),
          "restored widget focus re-enables the same exact media command authority");
    Check(!mediaBackEligible(true, false),
          "pinned projection remains outside the overlay media Back shortcut");

    OverlayState ordinaryState({}, {L"ordinary-peer"});
    (void)ordinaryState.Dispatch(Command::ToggleOverlay);
    (void)ordinaryState.Dispatch(Command::Activate);
    Check(ordinaryState.surface() == Surface::Widget &&
              ordinaryState.focusRegion() == FocusRegion::Widget &&
              RouteControllerAction(
                  ControllerActionContext::RootWidgetScope, L"B") ==
                  ControllerActionRoute::Widget,
          "ordinary widget B remains widget-owned and bypasses the media shortcut");
    Check(dispatch.find("case ControllerActionRoute::Widget:") != std::string::npos &&
              dispatch.find("DispatchWidgetAction(") != std::string::npos,
          "playback and all non-Back widget commands retain generic widget dispatch eligibility");
}

struct FixtureWindowState final {
    bool clickThrough{true};
    HBRUSH background{};
    widgetrail::accessibility::ProviderHost* provider{};
};

LRESULT CALLBACK MainWindowProc(
    const HWND window, const UINT message, const WPARAM wParam, const LPARAM lParam) {
    return DefWindowProcW(window, message, wParam, lParam);
}

LRESULT CALLBACK FixtureWindowProc(
    const HWND window, const UINT message, const WPARAM wParam, const LPARAM lParam) {
    auto* state = reinterpret_cast<FixtureWindowState*>(
        GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
        const auto* create = reinterpret_cast<const CREATESTRUCTW*>(lParam);
        state = static_cast<FixtureWindowState*>(create->lpCreateParams);
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(state));
    }
    switch (message) {
    case WM_NCHITTEST:
        return state && state->clickThrough ? HTTRANSPARENT : HTCLIENT;
    case WM_MOUSEACTIVATE:
        return state && state->clickThrough ? MA_NOACTIVATE : MA_ACTIVATE;
    case WM_GETOBJECT:
        if (state && state->provider)
            return state->provider->HandleWmGetObject(wParam, lParam);
        break;
    case WM_ERASEBKGND:
        return 1;
    case WM_PAINT:
    {
        PAINTSTRUCT paint{};
        const HDC dc = BeginPaint(window, &paint);
        RECT client{};
        GetClientRect(window, &client);
        FillRect(dc, &client, state && state->background
            ? state->background
            : static_cast<HBRUSH>(GetStockObject(BLACK_BRUSH)));
        SetBkMode(dc, TRANSPARENT);
        SetTextColor(dc, RGB(255, 255, 255));
        const wchar_t label[] = L"Host-owned pinned feasibility surface";
        DrawTextW(dc, label, -1, &client,
                  DT_CENTER | DT_VCENTER | DT_SINGLELINE | DT_END_ELLIPSIS);
        EndPaint(window, &paint);
        return 0;
    }
    default:
        break;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

[[nodiscard]] unsigned long long FileTimeTicks(const FILETIME value) noexcept {
    ULARGE_INTEGER ticks{};
    ticks.LowPart = value.dwLowDateTime;
    ticks.HighPart = value.dwHighDateTime;
    return ticks.QuadPart;
}

[[nodiscard]] unsigned long long ProcessCpuTicks() {
    FILETIME created{}, exited{}, kernel{}, user{};
    Check(GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user) != FALSE,
          "process CPU time is available");
    return FileTimeTicks(kernel) + FileTimeTicks(user);
}

[[nodiscard]] std::size_t PrivateWorkingSetBytes() {
    std::vector<std::byte> buffer(256 * 1024);
    for (int attempt = 0; attempt < 8; ++attempt) {
        if (QueryWorkingSet(GetCurrentProcess(), buffer.data(),
                            static_cast<DWORD>(buffer.size())) != FALSE) {
            const auto* information =
                reinterpret_cast<const PSAPI_WORKING_SET_INFORMATION*>(buffer.data());
            std::size_t privatePages{};
            for (ULONG_PTR index = 0; index < information->NumberOfEntries; ++index) {
                if (!information->WorkingSetInfo[index].Shared) ++privatePages;
            }
            SYSTEM_INFO system{};
            GetNativeSystemInfo(&system);
            return privatePages * static_cast<std::size_t>(system.dwPageSize);
        }
        Check(GetLastError() == ERROR_BAD_LENGTH,
              "private working-set query fails only for a short buffer");
        buffer.resize(buffer.size() * 2);
    }
    throw std::runtime_error("private working-set query exceeded its bounded buffer");
}

[[nodiscard]] DWORD LogicalProcessorCount() noexcept {
    const DWORD count = GetActiveProcessorCount(ALL_PROCESSOR_GROUPS);
    return count == 0 ? 1 : count;
}

[[nodiscard]] widgetrail::accessibility::Tree BuildAccessibilityTree(
    const widgetrail::pinned::SurfacePolicy& policy) {
    widgetrail::accessibility::Tree tree;
    tree.widgetId = L"host.pinned.feasibility";
    tree.runtimeGeneration = L"host-owned";
    tree.snapshotSequence = 1;
    tree.name = L"WidgetRail pinned surface";
    for (const auto& semantic : policy.ProjectSemantics()) {
        widgetrail::accessibility::Node node;
        node.id = semantic.id;
        node.name = semantic.name;
        node.value = semantic.value;
        node.domain = widgetrail::accessibility::ElementDomain::HostShell;
        node.role = node.id.ends_with(L"heading")
            ? widgetrail::accessibility::Role::Heading
            : widgetrail::accessibility::Role::Status;
        node.keyboardFocusable = semantic.keyboardFocusable;
        node.bounds = node.role == widgetrail::accessibility::Role::Heading
            ? widgetrail::declarative::Rect{16.0F, 16.0F, 448.0F, 64.0F}
            : widgetrail::declarative::Rect{16.0F, 96.0F, 448.0F, 48.0F};
        tree.nodes.push_back(std::move(node));
    }
    return tree;
}

void PublishAccessibility(
    widgetrail::accessibility::ProviderHost& provider,
    const widgetrail::pinned::SurfacePolicy& policy,
    const widgetrail::pinned::ResolvedPlacement& placement) {
    const auto width = placement.bounds.right - placement.bounds.left;
    const auto height = placement.bounds.bottom - placement.bounds.top;
    provider.Publish(
        BuildAccessibilityTree(policy),
        {static_cast<double>(placement.bounds.left),
         static_cast<double>(placement.bounds.top),
         static_cast<double>(placement.dpi) / 96.0,
         static_cast<double>(width), static_cast<double>(height)});
}

struct Measurement final {
    std::size_t privateWorkingSetBefore{};
    std::size_t privateWorkingSetPinned{};
    long long privateWorkingSetDelta{};
    double idleCpuPercent{};
    double semanticProjectionP95Milliseconds{};
    std::size_t semanticNodeCount{};
    std::size_t changedSemanticNodesPerUpdate{};
};

[[nodiscard]] double Percentile(std::vector<double> values, const double percentile) {
    Check(!values.empty(), "percentile input is nonempty");
    std::ranges::sort(values);
    const auto rank = static_cast<std::size_t>(
        std::ceil(percentile * static_cast<double>(values.size())));
    return values[std::clamp<std::size_t>(rank, 1, values.size()) - 1];
}

void WriteEvidence(const fs::path& destination, const Measurement& result) {
    if (destination.empty()) return;
    if (!destination.parent_path().empty()) fs::create_directories(destination.parent_path());
    const auto temporary = destination.wstring() + L".tmp";
    std::ofstream stream(temporary, std::ios::binary | std::ios::trunc);
    Check(static_cast<bool>(stream), "evidence temporary file opens");
    stream << "{\n"
           << "  \"schemaVersion\": 1,\n"
           << "  \"surface\": \"host-owned-win32-tool-window\",\n"
           << "  \"placeholder\": \"deterministic-color-and-status\",\n"
           << "  \"checks\": " << checks << ",\n"
           << "  \"privateWorkingSetBeforeBytes\": " << result.privateWorkingSetBefore << ",\n"
           << "  \"privateWorkingSetPinnedBytes\": " << result.privateWorkingSetPinned << ",\n"
           << "  \"privateWorkingSetDeltaBytes\": " << result.privateWorkingSetDelta << ",\n"
           << "  \"idleCpuPercent\": " << result.idleCpuPercent << ",\n"
           << "  \"semanticProjectionP95Milliseconds\": "
           << result.semanticProjectionP95Milliseconds << ",\n"
           << "  \"semanticNodeCount\": " << result.semanticNodeCount << ",\n"
           << "  \"changedSemanticNodesPerUpdate\": "
           << result.changedSemanticNodesPerUpdate << ",\n"
           << "  \"dlv016Reference\": {\n"
           << "    \"projectionP95MaximumMilliseconds\": "
           << kDlv016ProjectionP95MaximumMilliseconds << ",\n"
           << "    \"visibleIdlePrivateWorkingSetMaximumBytes\": "
           << kDlv016VisibleIdlePrivateWorkingSetMaximum << "\n"
           << "  },\n"
           << "  \"materialGates\": {\n"
           << "    \"controllerResponseMilliseconds\": "
           << kControllerResponseBudgetMilliseconds << ",\n"
           << "    \"incrementalPrivateWorkingSetBytes\": "
           << kMaterialPrivateWorkingSetBytes << "\n"
           << "  },\n"
           << "  \"limitations\": [\n"
           << "    \"No GPU, DWM, game-frame, physical-input, or exclusive-fullscreen measurement.\",\n"
           << "    \"Style and hit-test evidence is not a physical borderless-game compatibility claim.\",\n"
           << "    \"Crash cleanup relies on same-process HWND ownership and Windows process teardown.\"\n"
           << "  ]\n"
           << "}\n";
    stream.close();
    Check(static_cast<bool>(stream), "evidence temporary file is complete");
    std::error_code error;
    fs::rename(temporary, destination, error);
    Check(!error, "evidence publishes atomically");
}

void TestPolicyAndPlacement() {
    using widgetrail::pinned::ControllerCommand;
    widgetrail::pinned::ControllerInputContext controller;
    Check(widgetrail::pinned::ResolveControllerCommand(controller) == ControllerCommand::None,
          "unpinned controller input has no pinned-surface route");
    controller.pinned = true;
    controller.viewPressed = true;
    Check(widgetrail::pinned::ResolveControllerCommand(controller) == ControllerCommand::Enter,
          "View enters the current pin from tray authority regardless of selection");
    controller.sameWidgetOpen = true;
    Check(widgetrail::pinned::ResolveControllerCommand(controller) == ControllerCommand::Enter,
          "View enters the current pin from an open overlay widget");
    controller.viewPressed = false;
    controller.rightStickPressed = true;
    Check(widgetrail::pinned::ResolveControllerCommand(controller) == ControllerCommand::None,
          "right-stick click remains ordinary widget input outside pinned focus");
    controller.controllerFocused = true;
    controller.rightStickPressed = false;
    controller.aPressed = true;
    Check(widgetrail::pinned::ResolveControllerCommand(controller) == ControllerCommand::Activate,
          "A activates only through the focused pinned owner");
    controller.aPressed = false;
    controller.bPressed = true;
    Check(widgetrail::pinned::ResolveControllerCommand(controller) == ControllerCommand::None,
          "B remains selected-projection input while the pin owns focus");
    controller.bPressed = false;
    controller.viewPressed = true;
    Check(widgetrail::pinned::ResolveControllerCommand(controller) == ControllerCommand::Exit,
          "View deterministically returns pinned focus to the tray");
    controller.viewPressed = false;
    controller.xPressed = true;
    Check(widgetrail::pinned::ResolveControllerCommand(controller) == ControllerCommand::None,
          "X alone remains ordinary authored input while pinned focus is active");
    controller.xPressed = false;
    controller.rightStickPressed = true;
    Check(widgetrail::pinned::ResolveControllerCommand(controller) == ControllerCommand::None,
          "right-stick click remains ordinary authored input while pinned focus is active");
    controller.rightStickPressed = false;
    controller.xPressed = true;
    controller.leftShoulderDown = true;
    controller.rightShoulderDown = true;
    Check(widgetrail::pinned::ResolveControllerCommand(controller) ==
              ControllerCommand::EmergencyHide,
          "LB plus RB plus X resolves to the global emergency hide authority");
    controller.placementActive = true;
    controller.xPressed = false;
    controller.bPressed = true;
    controller.viewPressed = true;
    Check(widgetrail::pinned::ResolveControllerCommand(controller) == ControllerCommand::None,
          "placement mode retains exclusive A/B/View controller ownership");

    const auto clickThroughPresentation =
        widgetrail::pinned::ResolveSurfacePresentationPolicy(
            widgetrail::pinned::InteractionMode::ClickThrough);
    const auto interactivePresentation =
        widgetrail::pinned::ResolveSurfacePresentationPolicy(
            widgetrail::pinned::InteractionMode::Focusable);
    Check(clickThroughPresentation.content ==
              widgetrail::pinned::ContentPresentation::AdmittedWidget &&
              !clickThroughPresentation.exposeInteractiveSemantics,
          "click-through preserves admitted content while withholding actions");
    Check(interactivePresentation.content ==
              widgetrail::pinned::ContentPresentation::AdmittedWidget &&
              interactivePresentation.exposeInteractiveSemantics,
          "interactive mode preserves the same content and exposes actions");

    widgetrail::pinned::SurfacePolicy policy;
    Check(policy.state() == widgetrail::pinned::LifecycleState::Unpinned,
          "policy begins unpinned");
    Check(!policy.Pin({}), "empty declarative descriptor is rejected");
    Check(policy.Pin({L"feasibility.surface", L"Pinned feasibility surface"}),
          "data-only declarative descriptor is accepted");
    Check(policy.descriptor()->reducedMotion,
          "placeholder defaults to reduced motion with no ambient animation");
    policy.OnMainOverlayHidden();
    Check(policy.state() == widgetrail::pinned::LifecycleState::Pinned,
          "hiding the main overlay does not destroy the pinned policy");
    policy.SetInteractionMode(widgetrail::pinned::InteractionMode::Focusable);
    Check(policy.ProjectSemantics()[1].keyboardFocusable,
          "focusable mode is exposed by host-owned semantics");
    policy.Stop(widgetrail::pinned::StopReason::Unpin);
    Check(policy.state() == widgetrail::pinned::LifecycleState::Unpinned &&
          !policy.descriptor(), "unpin converges on no surface authority");
    Check(policy.Pin({L"feasibility.surface", L"Pinned feasibility surface"}),
          "surface may be pinned again");
    policy.Stop(widgetrail::pinned::StopReason::HostExit);
    Check(policy.state() == widgetrail::pinned::LifecycleState::Stopped &&
          !policy.descriptor(), "host exit converges on stopped cleanup");
    Check(policy.Pin({L"feasibility.surface", L"Pinned feasibility surface"}),
          "surface may be recreated by a live host");
    policy.Stop(widgetrail::pinned::StopReason::CrashRecovery);
    Check(policy.state() == widgetrail::pinned::LifecycleState::Stopped &&
          !policy.descriptor(), "crash recovery never restores stale window authority");

    const std::vector<widgetrail::pinned::MonitorWorkArea> landscape{
        {L"primary", {0, 0, 1920, 1040}, 96, true},
        {L"secondary", {-2560, 0, 0, 1400}, 144, false},
    };
    const auto saved = widgetrail::pinned::ResolvePlacement(
        landscape, widgetrail::pinned::PersistedPlacement{L"secondary", 80, 90, 480, 270});
    Check(saved && saved->monitorId == L"secondary" && !saved->usedFallback,
          "persisted placement resolves on its current monitor and DPI");
    Check(saved->bounds.left >= -2560 && saved->bounds.right <= 0 &&
          saved->bounds.top >= 0 && saved->bounds.bottom <= 1400,
          "mixed-DPI persisted placement stays inside its work area");

    const std::vector<widgetrail::pinned::MonitorWorkArea> rotated{
        {L"primary", {0, 0, 1080, 1880}, 120, true},
    };
    const auto missing = widgetrail::pinned::ResolvePlacement(
        rotated, widgetrail::pinned::PersistedPlacement{L"secondary", 2200, -50, 4000, 1});
    Check(missing && missing->usedFallback && missing->monitorId == L"primary",
          "monitor loss falls back to the primary work area");
    Check(missing->bounds.left >= 0 && missing->bounds.right <= 1080 &&
          missing->bounds.top >= 0 && missing->bounds.bottom <= 1880,
          "rotation and hot-plug fallback remains fully bounded");
    const auto malformed = widgetrail::pinned::ResolvePlacement(
        rotated, widgetrail::pinned::PersistedPlacement{
            L"primary", std::numeric_limits<float>::quiet_NaN(), 0, -1, 0});
    Check(malformed && malformed->usedFallback,
          "invalid persisted coordinates converge on the host default");
    Check(!widgetrail::pinned::ResolvePlacement({}, std::nullopt),
          "no valid monitor fails closed without inventing a rectangle");
}

Measurement TestRealHostWindow(const fs::path& evidencePath) {
    const HINSTANCE instance = GetModuleHandleW(nullptr);
    WNDCLASSEXW mainClass{sizeof(mainClass)};
    mainClass.lpfnWndProc = MainWindowProc;
    mainClass.hInstance = instance;
    mainClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    mainClass.lpszClassName = kMainClass;
    Check(RegisterClassExW(&mainClass) != 0, "main fixture class registers");

    WNDCLASSEXW fixtureClass{sizeof(fixtureClass)};
    fixtureClass.lpfnWndProc = FixtureWindowProc;
    fixtureClass.hInstance = instance;
    fixtureClass.hCursor = LoadCursorW(nullptr, IDC_ARROW);
    fixtureClass.lpszClassName = kFixtureClass;
    Check(RegisterClassExW(&fixtureClass) != 0, "pinned fixture class registers");

    HWND mainWindow = CreateWindowExW(
        WS_EX_TOOLWINDOW | WS_EX_TOPMOST, kMainClass, L"GBA main overlay fixture",
        WS_POPUP, 40, 40, 320, 180, nullptr, nullptr, instance, nullptr);
    Check(mainWindow != nullptr, "main overlay fixture is created");
    ShowWindow(mainWindow, SW_SHOWNOACTIVATE);

    POINT origin{0, 0};
    const HMONITOR monitor = MonitorFromPoint(origin, MONITOR_DEFAULTTOPRIMARY);
    MONITORINFO monitorInfo{sizeof(monitorInfo)};
    Check(monitor && GetMonitorInfoW(monitor, &monitorInfo),
          "primary monitor work area is available");
    const UINT dpi = GetDpiForWindow(mainWindow);
    const std::vector<widgetrail::pinned::MonitorWorkArea> monitors{
        {L"fixture-primary",
         {monitorInfo.rcWork.left, monitorInfo.rcWork.top,
          monitorInfo.rcWork.right, monitorInfo.rcWork.bottom},
         dpi == 0 ? 96U : dpi, true},
    };
    const auto placement = widgetrail::pinned::ResolvePlacement(monitors, std::nullopt);
    Check(placement.has_value(), "live work-area placement resolves");

    widgetrail::pinned::SurfacePolicy policy;
    Check(policy.Pin({L"feasibility.surface", L"Pinned feasibility surface"}),
          "live fixture pins the data-only surface");
    widgetrail::accessibility::ProviderHost provider;
    FixtureWindowState state;
    state.background = CreateSolidBrush(RGB(36, 65, 90));
    state.provider = &provider;
    Check(state.background != nullptr, "placeholder color brush is created");

    const std::size_t privateBefore = PrivateWorkingSetBytes();
    constexpr DWORD clickThroughStyles = WS_EX_TOOLWINDOW | WS_EX_TOPMOST |
        WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT;
    HWND pinned = CreateWindowExW(
        clickThroughStyles, kFixtureClass,
        L"WidgetRail pinned feasibility surface", WS_POPUP,
        placement->bounds.left, placement->bounds.top,
        placement->bounds.right - placement->bounds.left,
        placement->bounds.bottom - placement->bounds.top,
        nullptr, nullptr, instance, &state);
    Check(pinned != nullptr, "host-owned pinned tool window is created");
    provider.Bind(pinned, kAccessibilityActionMessage);
    PublishAccessibility(provider, policy, *placement);
    provider.SetWindowVisible(true);
    SetLayeredWindowAttributes(pinned, 0, 255, LWA_ALPHA);
    ShowWindow(pinned, SW_SHOWNOACTIVATE);
    UpdateWindow(pinned);
    Check(IsWindowVisible(pinned), "pinned placeholder is visibly established");
    const auto styles = static_cast<DWORD>(GetWindowLongPtrW(pinned, GWL_EXSTYLE));
    Check((styles & WS_EX_TOOLWINDOW) != 0 && (styles & WS_EX_APPWINDOW) == 0,
          "tool-window contract excludes taskbar and Alt-Tab surfaces");
    Check((styles & WS_EX_TOPMOST) != 0,
          "pinned placeholder is in the host-owned topmost band");
    Check((styles & (WS_EX_NOACTIVATE | WS_EX_TRANSPARENT)) ==
              (WS_EX_NOACTIVATE | WS_EX_TRANSPARENT),
          "click-through mode is nonactivating and transparent to hit testing");
    Check(SendMessageW(pinned, WM_NCHITTEST, 0, MAKELPARAM(
              placement->bounds.left + 10, placement->bounds.top + 10)) == HTTRANSPARENT,
          "click-through mode rejects client hit-test ownership");

    IRawElementProviderSimple* root{};
    Check(SUCCEEDED(provider.GetRootProvider(&root)) && root,
          "host-owned UI Automation root is available");
    VARIANT name{};
    VariantInit(&name);
    Check(SUCCEEDED(root->GetPropertyValue(UIA_NamePropertyId, &name)) &&
          name.vt == VT_BSTR && name.bstrVal &&
          std::wstring_view{name.bstrVal} == L"WidgetRail pinned surface",
          "UI Automation root exposes the host-owned accessible name");
    VariantClear(&name);
    root->Release();

    ShowWindow(mainWindow, SW_HIDE);
    policy.OnMainOverlayHidden();
    Check(!IsWindowVisible(mainWindow) && IsWindowVisible(pinned),
          "hiding the main overlay preserves the visible pinned surface");
    DestroyWindow(mainWindow);
    mainWindow = nullptr;
    Check(IsWindow(pinned), "destroying the transient main fixture does not orphan ownership");

    policy.SetInteractionMode(widgetrail::pinned::InteractionMode::Focusable);
    state.clickThrough = false;
    const auto focusableStyles = static_cast<LONG_PTR>(styles &
        ~(WS_EX_NOACTIVATE | WS_EX_TRANSPARENT));
    SetWindowLongPtrW(pinned, GWL_EXSTYLE, focusableStyles);
    SetWindowPos(pinned, HWND_TOPMOST, 0, 0, 0, 0,
                 SWP_NOMOVE | SWP_NOSIZE | SWP_FRAMECHANGED | SWP_SHOWWINDOW);
    SetActiveWindow(pinned);
    SetFocus(pinned);
    provider.SetWindowFocused(true);
    PublishAccessibility(provider, policy, *placement);
    Check(GetFocus() == pinned, "focusable mode accepts explicit host focus");
    Check(SendMessageW(pinned, WM_NCHITTEST, 0, MAKELPARAM(
              placement->bounds.left + 10, placement->bounds.top + 10)) == HTCLIENT,
          "focusable mode owns its client hit test");

    std::vector<double> semanticDurations;
    semanticDurations.reserve(kSemanticUpdates);
    for (std::size_t index = 0; index < kSemanticUpdates; ++index) {
        const auto started = std::chrono::steady_clock::now();
        policy.SetInteractionMode(index % 2 == 0
            ? widgetrail::pinned::InteractionMode::ClickThrough
            : widgetrail::pinned::InteractionMode::Focusable);
        const auto semantics = policy.ProjectSemantics();
        const auto ended = std::chrono::steady_clock::now();
        Check(semantics.size() == 2 && semantics[0].id == L"host.pinned.heading" &&
              semantics[1].id == L"host.pinned.status",
              "semantic projection preserves two stable host-owned nodes");
        semanticDurations.push_back(
            std::chrono::duration<double, std::milli>(ended - started).count());
    }

    std::this_thread::sleep_for(std::chrono::milliseconds(100));
    const std::size_t privatePinned = PrivateWorkingSetBytes();
    const auto cpuBefore = ProcessCpuTicks();
    const auto idleStarted = std::chrono::steady_clock::now();
    std::this_thread::sleep_for(kIdleObservation);
    const auto idleEnded = std::chrono::steady_clock::now();
    const auto cpuAfter = ProcessCpuTicks();
    const double elapsedMilliseconds =
        std::chrono::duration<double, std::milli>(idleEnded - idleStarted).count();
    const double cpuMilliseconds = static_cast<double>(cpuAfter - cpuBefore) / 10000.0;
    const double idleCpu = elapsedMilliseconds <= 0.0 ? 0.0 :
        cpuMilliseconds * 100.0 /
        (elapsedMilliseconds * static_cast<double>(LogicalProcessorCount()));
    const double semanticP95 = Percentile(semanticDurations, 0.95);
    const long long privateDelta = static_cast<long long>(privatePinned) -
                                   static_cast<long long>(privateBefore);
    Check(semanticP95 <= kControllerResponseBudgetMilliseconds,
          "pinned semantic projection remains within the DLV-016 response gate");
    Check(privateDelta <= static_cast<long long>(kMaterialPrivateWorkingSetBytes),
          "pinned window increment remains within the DLV-016 material memory gate");

    policy.Stop(widgetrail::pinned::StopReason::Unpin);
    provider.Clear();
    provider.Detach();
    DestroyWindow(pinned);
    Check(!IsWindow(pinned) && !policy.descriptor(),
          "unpin destroys the HWND and releases declarative authority together");
    DeleteObject(state.background);
    UnregisterClassW(kFixtureClass, instance);
    UnregisterClassW(kMainClass, instance);

    const Measurement result{
        privateBefore,
        privatePinned,
        privateDelta,
        idleCpu,
        semanticP95,
        2,
        1,
    };
    WriteEvidence(evidencePath, result);
    return result;
}

fs::path ParseEvidencePath(const int argc, wchar_t** argv) {
    for (int index = 1; index + 1 < argc; ++index) {
        if (std::wstring_view{argv[index]} == L"--evidence") return argv[index + 1];
    }
    return {};
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    const HRESULT apartment = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    try {
        Check(SUCCEEDED(apartment), "COM apartment initializes");
        (void)SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        TestAcceptedCompactMediaHostContract();
        TestAcceptedOverlayFullscreenMediaHostContract();
        TestAcceptedWidgetOwnedFocusMemoryHostContract();
        TestAcceptedMediaBackOwnershipHostContract();
        TestPolicyAndPlacement();
        const auto result = TestRealHostWindow(ParseEvidencePath(argc, argv));
        std::cout << "PinnedSurfaceHostTests passed (" << checks
                  << " checks, private working-set delta "
                  << result.privateWorkingSetDelta << " bytes, idle CPU "
                  << result.idleCpuPercent << "%, semantic p95 "
                  << result.semanticProjectionP95Milliseconds << " ms)\n";
        CoUninitialize();
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "PinnedSurfaceHostTests failed after " << checks
                  << " checks: " << error.what() << '\n';
        if (SUCCEEDED(apartment)) CoUninitialize();
        return 1;
    }
}
