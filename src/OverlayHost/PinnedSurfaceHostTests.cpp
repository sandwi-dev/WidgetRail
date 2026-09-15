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
    const auto section = [&](const std::string_view begin,
                             const std::string_view end) {
        const auto beginOffset = source.find(begin);
        Check(beginOffset != std::string::npos,
              "retained media host contract section begins");
        const auto endOffset = source.find(end, beginOffset + begin.size());
        Check(endOffset != std::string::npos,
              "retained media host contract section ends");
        return source.substr(beginOffset, endOffset - beginOffset);
    };
    Check(has("if (current == std::numeric_limits<long long>::max()) return std::nullopt;") &&
              has("return current + 1;") &&
              has("NextEmbeddedMediaPlaybackObservationSequence(\n                embeddedMediaSessionPlaybackEventSequence_)") &&
              has("embeddedMediaSessionPlaybackEventSequence_ = publishedEventSequence;") &&
              has("event.mediaKey, event.state, event.positionSeconds") &&
              has("origin->commandSequence != commandSequence") &&
              has("origin->mediaKey != event.mediaKey"),
          "page-event restart projects onto a host-monotonic sequence, fails closed at exhaustion, and retains command/media/origin authority");
    const auto managerHeader = ReadSource(
        fs::path{__FILE__}.parent_path() / "MediaSessionManager.h");
    const auto managerSource = ReadSource(
        fs::path{__FILE__}.parent_path() / "MediaSessionManager.cpp");
    Check(managerHeader.find("struct SessionKey final {") != std::string::npos &&
              managerHeader.find("std::wstring widgetId;") != std::string::npos &&
              managerHeader.find("std::wstring instanceId;") != std::string::npos &&
              managerHeader.find("std::wstring runtimeGeneration;") != std::string::npos &&
              managerHeader.find("std::wstring sessionId;") != std::string::npos &&
              managerHeader.find("struct SessionRecord final {") != std::string::npos &&
              managerHeader.find(
                  "std::shared_ptr<richmedia::RichMediaSurfaceCoordinator> coordinator;") !=
                  std::string::npos &&
              managerHeader.find("std::optional<SessionAuthority> authority;") !=
                  std::string::npos &&
              managerHeader.find("playbackEventSequence") == std::string::npos,
          "resident session retains exact durable identity, controller, and authority without owning the process playback observation epoch");
    Check(managerSource.find("if (sessions_.size() >= MaximumResidentSessions)") !=
                  std::string::npos &&
              managerSource.find("sessions_.find(key.value())") !=
                  std::string::npos &&
              managerSource.find("EndpointOwner(") != std::string::npos &&
              managerSource.find("ClearEndpointOwnership(key);") !=
                  std::string::npos &&
              managerSource.find("sessions_.erase(key.value());") !=
                  std::string::npos &&
              !has("residentEmbeddedMediaSessions_") &&
              !has("boundEmbeddedMediaSessionKey_") &&
              !has("richMediaSurface_") &&
              !has("embeddedMediaAuthority_"),
          "one bounded exact-key manager owns resident lifetime and endpoint retirement without a parallel bound-session owner");
    Check(managerSource.find(
              "input.geometry == GeometryState::Pending") !=
                  std::string::npos &&
              managerSource.find("TransitionEffect::Park") !=
                  std::string::npos &&
              managerSource.find("TransitionEffect::Present") !=
                  std::string::npos &&
              managerSource.find("TransitionEffect::Update") !=
                  std::string::npos &&
              has("EmbeddedMediaTransitionOperations(") &&
              has("if (session.key != exactKey) return E_ACCESSDENIED;") &&
              has("mediaSessions_.Reconcile(") &&
              has("SameEmbeddedMediaDocumentIdentity("),
          "manager transition planning and exact-key host effects defer pending geometry and reject stale document or endpoint authority");
    const auto pinnedCoordinator = ReadSource(
        fs::path{__FILE__}.parent_path() / "WidgetSurfaceCoordinator.cpp");
    const auto unpinBegin = pinnedCoordinator.find(
        "bool WidgetSurfaceCoordinator::Unpin(");
    const auto callback = pinnedCoordinator.find(
        "if (beforeWindowRetirement_) beforeWindowRetirement_(reason);", unpinBegin);
    const auto retirementAuthority = pinnedCoordinator.find(
        "tearingDown_ = true;", unpinBegin);
    const auto pinnedOwner = pinnedCoordinator.find(
        "bool WidgetSurfaceCoordinator::pinned() const noexcept");
    const auto releaseGraphics = pinnedCoordinator.find(
        "ReleaseGraphicsResources();", callback);
    const auto releaseCachedImages = pinnedCoordinator.find(
        "renderer_->ReleaseCachedImages();", releaseGraphics);
    Check(unpinBegin != std::string::npos && callback != std::string::npos &&
              retirementAuthority != std::string::npos &&
              retirementAuthority < callback && pinnedOwner != std::string::npos &&
              pinnedCoordinator.find("return !tearingDown_", pinnedOwner) !=
                  std::string::npos,
          "pinned retirement revokes presentation authority before its reentrant media callback");
    Check(releaseGraphics != std::string::npos &&
              releaseCachedImages != std::string::npos &&
              releaseGraphics < releaseCachedImages &&
              releaseCachedImages < pinnedOwner,
          "pinned endpoint retirement releases target and cached image resources without changing ordinary renderer ownership");
    const auto bridgeClient = ReadSource(
        fs::path{__FILE__}.parent_path() / "WidgetBridgeClient.cpp");
    Check(bridgeClient.find(
              "!artworkResults->Push({std::move(widgetId), std::move(handle),") !=
              std::string::npos &&
              bridgeClient.find("std::move(contentType), std::move(content)}") !=
              std::string::npos,
          "validated artwork event strings move into the bounded host queue without another encoded payload copy");
    const auto retirementCallback = section(
        "void HandlePinnedSurfaceWindowRetirement(",
        "[[nodiscard]] HRESULT ExecuteParkEmbeddedMediaSession(");
    Check(retirementCallback.find(
              "mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Pinned)") !=
                  std::string::npos &&
              retirementCallback.find(
                  "session->authority->presentation !=\n                EmbeddedMediaPresentationState::CompactPinned") !=
                  std::string::npos &&
              retirementCallback.find("ReconcileEmbeddedMediaPresentation(") !=
                  std::string::npos &&
              retirementCallback.find("StopEmbeddedMediaSession(*sessionKey") !=
                  std::string::npos &&
              retirementCallback.find("retiringWidgetId") != std::string::npos,
          "pinned retirement retains exact endpoint identity and a bounded park, overlay-return, or terminal outcome");
    const auto coordinatorTransfer = ReadSource(
        fs::path{__FILE__}.parent_path() / "RichMediaSurfaceCoordinator.cpp");
    Check(coordinatorTransfer.find(
              "Fault(L\"presentation-transfer-visibility\", result)") !=
                  std::string::npos &&
              coordinatorTransfer.find(
              "Fault(L\"presentation-transfer-root-target-detach\", result)") !=
                  std::string::npos,
          "controller detach failures retain distinct visibility and root-target terminal stages");
    const auto residency = section(
        "void SyncWidgetActivity(",
        "void RetireBridgeSessionPresentationAuthority(");
    Check(residency.find("const auto mediaKeys = mediaSessions_.Keys();") !=
                  std::string::npos &&
              residency.find("mediaSessions_.Find(key)") != std::string::npos &&
              residency.find(
                  "? EmbeddedMediaPresentationState::CompactPinned") !=
                  std::string::npos &&
              residency.find(
                  "ReconcileEmbeddedMediaPresentation(\n                key, destination, L\"lifecycle-reconciliation\"") !=
                  std::string::npos &&
              residency.find("ParkingReason::HostHidden") !=
                  std::string::npos &&
              residency.find("ParkingReason::WidgetCycled") !=
                  std::string::npos,
          "lifecycle reconciliation retains every exact session and routes hidden, cycled, overlay, and compact ownership through the manager");
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
    const auto compactController = section(
        "if (pinnedSurfaceCoordinator_.controllerFocused()) {",
        "if (pinnedSurfaceCoordinator_.selectPopupOpen()) {");
    Check(compactController.find(
              "ReturnPinnedControllerFocusToOverlay(now);") !=
                  std::string::npos &&
              compactController.find("CancelCompactMediaScrub()") !=
                  std::string::npos &&
              compactController.find(
                  "pinnedSurfaceCoordinator_.ExitControllerFocus()") !=
                  std::string::npos &&
              compactController.find(
                  "widgetrail::pinned::InteractionMode::ClickThrough") !=
                  std::string::npos &&
              compactController.find(
                  "Dispatch(widgetrail::Command::SampleWidgetBack);") ==
                  std::string::npos &&
              !has("BeginCompactMediaScrub()") &&
              !has("StepCompactMediaScrub(") &&
              !has("CommitCompactMediaScrub()"),
          "compact B retains its click-through exit, View uses the overlay-return owner, and A/navigation expose no scrub mapping");
}

void TestBoundedOverlayDiagnosticContract() {
    const auto source = ReadSource(fs::path{__FILE__}.parent_path() / "main.cpp");
    Check(source.find("maximumFileBytes = 4ULL * 1024ULL * 1024ULL") !=
              std::string::npos &&
              source.find("Local\\\\WidgetRail.OverlayDiagnosticLog.v1") !=
              std::string::npos &&
              source.find("crossProcessWaitMilliseconds = 50") !=
              std::string::npos,
          "overlay diagnostics share one bounded cross-process rotation owner");
    Check(source.find("overlay.1.log") != std::string::npos &&
              source.find("overlay.2.log") != std::string::npos &&
              source.find("input.seekg(-static_cast<std::streamoff>(maximumFileBytes)") !=
              std::string::npos &&
              source.find("MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH") !=
              std::string::npos,
          "overlay diagnostics retain two bounded recent generations and trim legacy oversized logs");
}

void TestPinnedBackgroundCrossfadeDiagnosticWakeContract() {
    const auto coordinator = ReadSource(
        fs::path{__FILE__}.parent_path() / "WidgetSurfaceCoordinator.cpp");
    const auto diagnosticQueue = coordinator.find(
        "backgroundSurfaceDiagnosticsQueued = true;");
    const auto diagnosticWake = coordinator.find(
        "NotifyOwner(kBackgroundSurfaceDiagnosticNotification);",
        diagnosticQueue);
    Check(diagnosticQueue != std::string::npos &&
              diagnosticWake != std::string::npos,
          "pinned crossfade diagnostics wake the host after bounded queue admission");

    const auto host = ReadSource(fs::path{__FILE__}.parent_path() / "main.cpp");
    const auto handler = host.find("case kPinnedSurfaceChangedMessage:");
    const auto normalDrain = host.find("DrainPinnedSurfaceInputs();", handler);
    Check(handler != std::string::npos && normalDrain != std::string::npos,
          "pinned owner-message handler and ordinary drain are present");
    const auto diagnosticOnly = host.substr(handler, normalDrain - handler);
    Check(diagnosticOnly.find(
              "kBackgroundSurfaceDiagnosticNotification") !=
                  std::string::npos &&
              diagnosticOnly.find("DrainPinnedSurfaceDiagnostics();") !=
                  std::string::npos &&
              diagnosticOnly.find("return 0;") != std::string::npos &&
              diagnosticOnly.find("InvalidateRect") == std::string::npos &&
              diagnosticOnly.find("ReconcileEmbeddedMediaProjection") ==
                  std::string::npos,
          "diagnostic-only wake drains to overlay.log without presentation reconciliation or repaint");

    const auto controllerTimer = host.find(
        "if (wParam == kControllerTimer || wParam == kPinnedSurfaceTimer)");
    const auto bridgePump = host.find(
        "PumpBridgeEvents(controllerTick);", controllerTimer);
    Check(controllerTimer != std::string::npos &&
              bridgePump != std::string::npos,
          "ordinary controller timer and Bridge pump are present");
    const auto ordinaryWake = host.substr(
        controllerTimer, bridgePump - controllerTimer);
    const auto settleAdmission = ordinaryWake.find(
        "lastWidgetRenderResult_.backgroundSurfaceSettleWake");
    const auto fullInvalidation = ordinaryWake.find(
        "InvalidateRect(window_, nullptr, FALSE) != FALSE", settleAdmission);
    const auto planReset = ordinaryWake.find(
        "pendingContentRenderPlan_.reset();", fullInvalidation);
    Check(settleAdmission != std::string::npos &&
              ordinaryWake.find("lastWidgetRenderResult_.succeeded") <
                  settleAdmission &&
              ordinaryWake.find(
                  "state_.surface() == widgetrail::Surface::Widget") <
                  settleAdmission &&
              ordinaryWake.find("HasExactRefreshRetainedVisualCheckpoint()") !=
                  std::string::npos &&
              fullInvalidation != std::string::npos &&
              planReset != std::string::npos &&
              fullInvalidation < planReset &&
              ordinaryWake.find(
                  "lastWidgetRenderResult_.backgroundSurfaceSettleWake.reset();",
                  planReset) != std::string::npos,
          "ordinary settle wake invalidates a full successful frame before consuming its one-shot authority");

    const auto ordinaryDamageBegin = host.find(
        "[[nodiscard]] bool SubmitBackgroundSurfaceDamage(");
    const auto ordinaryDamageEnd = host.find(
        "void InvalidateWidgetFocusChange(", ordinaryDamageBegin);
    Check(ordinaryDamageBegin != std::string::npos &&
              ordinaryDamageEnd != std::string::npos,
          "ordinary background damage owner is present");
    const auto ordinaryDamage = host.substr(
        ordinaryDamageBegin, ordinaryDamageEnd - ordinaryDamageBegin);
    Check(ordinaryDamage.find("pendingContentRenderPlan_") !=
                  std::string::npos &&
              ordinaryDamage.find("GetUpdateRect(") != std::string::npos &&
              ordinaryDamage.find("return false;") != std::string::npos,
          "ordinary narrow background damage fails closed on a pending plan or paint collision");
    const auto ordinaryPumpBegin = host.find(
        "if (declarativeMotionActive_ &&");
    const auto ordinaryPumpEnd = host.find(
        "const widgetrail::WidgetSnapshot* SnapshotFor", ordinaryPumpBegin);
    Check(ordinaryPumpBegin != std::string::npos &&
              ordinaryPumpEnd != std::string::npos,
          "ordinary declarative animation pump is present");
    const auto ordinaryPump = host.substr(
        ordinaryPumpBegin, ordinaryPumpEnd - ordinaryPumpBegin);
    Check(ordinaryPump.find("SubmitBackgroundSurfaceDamage(") !=
                  std::string::npos &&
              ordinaryPump.find("pendingContentRenderPlan_.reset();") !=
                  std::string::npos &&
              ordinaryPump.find("CancelPresentationUpdatePlan();") !=
                  std::string::npos &&
              ordinaryPump.find("InvalidateRect(window_, nullptr, FALSE);") !=
                  std::string::npos,
          "ordinary animation collisions cancel narrow work and request a conservative full frame");

    const auto pinnedTimer = coordinator.find(
        "if (wParam == kBackgroundSurfaceAnimationTimer)");
    const auto pinnedTimerEnd = coordinator.find(
        "return DefWindowProcW(window_, message, wParam, lParam);", pinnedTimer);
    Check(pinnedTimer != std::string::npos &&
              pinnedTimerEnd != std::string::npos,
          "pinned background timer owner is present");
    const auto pinnedWake = coordinator.substr(
        pinnedTimer, pinnedTimerEnd - pinnedTimer);
    Check(pinnedWake.find("!renderer_ || !lastRenderResult_.succeeded") !=
                  std::string::npos &&
              pinnedWake.find("backgroundSurfaceSettleWake") !=
                  std::string::npos &&
              pinnedWake.find("renderer_->CancelPresentationUpdatePlan();") !=
                  std::string::npos &&
              pinnedWake.find("GetUpdateRect(") != std::string::npos &&
              pinnedWake.find("if (!plan)") != std::string::npos &&
              std::count(
                  pinnedWake.begin(), pinnedWake.end(), '\n') > 20,
          "pinned timer retains settle authority and promotes damage collisions to a full repaint");

    const auto pinnedPaint = coordinator.find(
        "const HRESULT result = renderTarget_->EndDraw();");
    const auto pinnedPaintEnd = coordinator.find(
        "void WidgetSurfaceCoordinator::PublishAccessibility()", pinnedPaint);
    Check(pinnedPaint != std::string::npos &&
              pinnedPaintEnd != std::string::npos,
          "pinned paint completion owner is present");
    const auto pinnedSchedule = coordinator.substr(
        pinnedPaint, pinnedPaintEnd - pinnedPaint);
    Check(pinnedSchedule.find("else if (SUCCEEDED(result))") !=
                  std::string::npos &&
              pinnedSchedule.find("if (!renderResult.succeeded)") !=
                  std::string::npos &&
              pinnedSchedule.find("backgroundSurfaceAnimationDamage") !=
                  std::string::npos &&
              pinnedSchedule.find("backgroundSurfaceSettleWake") !=
                  std::string::npos &&
              pinnedSchedule.find("else if (renderResult.animationActive)") !=
                  std::string::npos &&
              pinnedSchedule.find("SetTimer(") != std::string::npos &&
              pinnedSchedule.find("RequestPaint();") != std::string::npos,
          "pinned scheduling publishes only successful frames and falls back safely when timers cannot arm");
}

void TestWidgetContextActionHostContract() {
    const auto source = ReadSource(
        fs::path{__FILE__}.parent_path() / "main.cpp");
    const auto begin = source.find("struct WidgetContextMenuState final {");
    const auto end = source.find("struct WidgetContextMenuLayout final {", begin);
    Check(begin != std::string::npos && end != std::string::npos,
          "widget context menu owns one bounded authority record");
    const auto authority = source.substr(begin, end - begin);
    Check(authority.find("instanceId") != std::string::npos &&
              authority.find("runtimeGeneration") != std::string::npos &&
              authority.find("presentationGeneration") != std::string::npos &&
              authority.find("snapshotSequence") != std::string::npos &&
              authority.find("inputScopeId") != std::string::npos &&
              authority.find("sourceNodeId") != std::string::npos,
          "context menu captures exact widget, generation, sequence, scope, and node authority");
    const auto validate = source.find("bool WidgetContextMenuAuthorityCurrent() const");
    const auto layout = source.find("CurrentWidgetContextMenuLayout", validate);
    Check(validate != std::string::npos && layout != std::string::npos,
          "context menu revalidates authority before projection");
    const auto dispatch = source.find("void ActivateWidgetContextMenuItem(");
    const auto trayOpen = source.find("void OpenTrayContextMenu", dispatch);
    Check(dispatch != std::string::npos && trayOpen != std::string::npos,
          "context action dispatch has one bounded owner");
    const auto dispatchSlice = source.substr(dispatch, trayOpen - dispatch);
    Check(dispatchSlice.find("WidgetContextMenuAuthorityCurrent()") != std::string::npos &&
              dispatchSlice.find("bridge_.SendAction(") != std::string::npos &&
              dispatchSlice.find("CloseWidgetContextMenu();") != std::string::npos,
          "context action revalidates, dismisses, and dispatches exactly once");
    Check(source.find("HostAction::InvokeWidgetContextAction") != std::string::npos &&
              source.find("key == VK_APPS") != std::string::npos &&
              source.find("widgetrail::input::ResolveContextMenuSource(") != std::string::npos &&
              source.find("OpenWidgetContextMenu(*source)") != std::string::npos,
          "context menu exposes UIA, keyboard, and controller entry paths");
}

void TestAcceptedOverlayFullscreenMediaHostContract() {
    const auto source = ReadSource(
        fs::path{__FILE__}.parent_path() / "main.cpp");
    const auto managerSource = ReadSource(
        fs::path{__FILE__}.parent_path() / "MediaSessionManager.cpp");
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
                             const std::string_view end,
                             const std::string_view message) {
        const auto beginOffset = source.find(begin);
        Check(beginOffset != std::string::npos, message);
        const auto endOffset = source.find(end, beginOffset + begin.size());
        Check(endOffset != std::string::npos, message);
        return source.substr(beginOffset, endOffset - beginOffset);
    };

    const auto fullscreenAuthority = section(
        "[[nodiscard]] FullscreenEntryDecision EvaluateFullscreenEntry(",
        "[[nodiscard]] bool OverlayFullscreenMediaRequested() const noexcept {",
        "fullscreen request authority owner exists");
    Check(fullscreenAuthority.find("CurrentEmbeddedMediaSessionKey(") !=
                  std::string::npos &&
              fullscreenAuthority.find("mediaSessions_.Find(*decision.sessionKey)") !=
                  std::string::npos &&
              fullscreenAuthority.find(
                  "authority.presentationGeneration == descriptor->presentationGeneration") !=
                  std::string::npos &&
              fullscreenAuthority.find(
                  "EmbeddedMediaPresentationAuthorityCurrent(*decision.sessionKey)") !=
                  std::string::npos &&
              fullscreenAuthority.find(
                  "mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Overlay)") !=
                  std::string::npos &&
              fullscreenAuthority.find("decision.overlayViewport") !=
                  std::string::npos &&
              fullscreenAuthority.find("decision.committedViewport") !=
                  std::string::npos &&
              fullscreenAuthority.find(
                  "MediaPresentationKind::OverlayFullscreen") !=
                  std::string::npos &&
              fullscreenAuthority.find("decision.pinnedTakeover") !=
                  std::string::npos &&
              fullscreenAuthority.find("decision.requestEligible") !=
                  std::string::npos,
          "fullscreen activation requires the exact current session, generation, capability, and non-pinned authority");

    const auto fullscreenEntryExit = section(
        "[[nodiscard]] bool EnterOverlayFullscreenMedia(",
        "void ClearStaleOverlayFullscreenMediaActivation(",
        "fullscreen entry and exit owners exist");
    Check(fullscreenEntryExit.find(
              "if (!decision.requestEligible || !decision.sessionKey) return false;") !=
                  std::string::npos &&
              fullscreenEntryExit.find(
                  "*decision.sessionKey, EmbeddedMediaPresentationState::OverlayFullscreen") !=
                  std::string::npos &&
              fullscreenEntryExit.find(
                  "const auto key = CurrentEmbeddedMediaSessionKey(state_.activeWidget())") !=
                  std::string::npos &&
              fullscreenEntryExit.find(
                  "session->authority->presentation !=\n                 EmbeddedMediaPresentationState::OverlayFullscreen") !=
                  std::string::npos &&
              fullscreenEntryExit.find(
                  "*key, EmbeddedMediaPresentationState::OverlayViewport") !=
                  std::string::npos &&
              fullscreenEntryExit.find("mediaSessions_.RequestPresentation(") !=
                  std::string::npos,
          "host-owned fullscreen entry and exit mutate only the exact manager presentation request");

    const auto fullscreenInput = section(
        "constexpr WORD recoveryChord = XINPUT_GAMEPAD_BACK | XINPUT_GAMEPAD_START;",
        "const auto pinnedControllerCommand",
        "fullscreen controller route exists");
    const auto fullscreenBranch = source.find(
        "if (OverlayFullscreenMediaRequested()) {",
        source.find("constexpr WORD recoveryChord"));
    const auto fullscreenTerminal = source.find(
        "            return;\n        }", fullscreenBranch);
    const auto pinnedViewRoute = source.find(
        "const auto pinnedControllerCommand", fullscreenTerminal);
    Check(fullscreenInput.find(
              "mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Overlay)") !=
                  std::string::npos &&
              fullscreenInput.find("mediaSessions_.Find(*fullscreenKey)") !=
                  std::string::npos &&
              fullscreenInput.find(
                  "EmbeddedMediaPresentationState::OverlayFullscreen") !=
                  std::string::npos &&
              fullscreenInput.find("DispatchControllerAction(L\"B\", true);") !=
              std::string::npos &&
              fullscreenInput.find("Command::TogglePlayback") != std::string::npos &&
              fullscreenInput.find("frame.leftTriggerPressed") != std::string::npos &&
              fullscreenInput.find("NavigationDirection::Left") != std::string::npos &&
              fullscreenInput.find("frame.rightTriggerPressed") != std::string::npos &&
              fullscreenInput.find("NavigationDirection::Right") != std::string::npos &&
              fullscreenBranch != std::string::npos &&
              fullscreenTerminal != std::string::npos &&
              pinnedViewRoute != std::string::npos &&
              fullscreenTerminal < pinnedViewRoute,
          "fullscreen routes B, X, LT, and RT while View cannot reach hidden tray routing");

    const auto fullscreenPaint = section(
        "const bool overlayFullscreen = OverlayFullscreenMediaRequested();",
        "if (layer == CompositionPaintLayer::Tray && trayLayout)",
        "fullscreen paint suppression owner exists");
    Check(fullscreenPaint.find(
              "overlayFullscreen && layer == CompositionPaintLayer::Tray") !=
              std::string::npos &&
              fullscreenPaint.find(
                  "overlayFullscreen && layer == CompositionPaintLayer::Guide") !=
              std::string::npos &&
              fullscreenPaint.find(
                  "overlayFullscreen && layer == CompositionPaintLayer::Content") !=
              std::string::npos &&
              fullscreenPaint.find("ClearAccessibilityTree();") !=
                  std::string::npos,
          "fullscreen suppresses tray, guide, content paint, and widget accessibility");

    const auto fullscreenGeometry = section(
        "if (OverlayFullscreenMediaRequested() && metrics && window_) {",
        "drawMicroseconds = static_cast<std::uint64_t>(",
        "fullscreen geometry projection owner exists");
    Check(fullscreenGeometry.find("CurrentEmbeddedMediaSessionKey(") !=
                  std::string::npos &&
              fullscreenGeometry.find(
                  "ResolveOverlayFullscreenMediaSurfaceBounds(") !=
                  std::string::npos &&
              fullscreenGeometry.find(
                  "ResolveMediaViewportPresentationGeometry(") !=
                  std::string::npos &&
              fullscreenGeometry.find("set.overlayFullscreenGeometry =") !=
                  std::string::npos &&
              fullscreenGeometry.find("*sessionKey") != std::string::npos &&
              source.find(
                  "ReconcileEmbeddedMediaPresentation(\n                fullscreen.sessionKey,\n                EmbeddedMediaPresentationState::OverlayFullscreen") !=
                  std::string::npos,
          "fullscreen uses one exact session-keyed aspect-fit geometry and manager presentation owner");

    const auto fullscreenCheckpoint = section(
        "struct CommittedFullscreenPresentationCheckpoint final {",
        "[[nodiscard]] bool OverlayFullscreenMediaRequested() const noexcept {",
        "fullscreen committed checkpoint owner exists");
    Check(fullscreenCheckpoint.find("EmbeddedMediaSessionKey sessionKey") !=
                  std::string::npos &&
              fullscreenCheckpoint.find("documentIdentity") != std::string::npos &&
              fullscreenCheckpoint.find("presentationGeneration") !=
                  std::string::npos &&
              fullscreenCheckpoint.find("snapshotSequence") != std::string::npos &&
              fullscreenCheckpoint.find("EndpointGeometry geometry") !=
                  std::string::npos,
          "fullscreen checkpoint retains exact session, document, generation, sequence, and endpoint geometry authority");
    const auto checkpointAuthority = section(
        "[[nodiscard]] bool CommittedFullscreenPresentationCurrent(",
        "void PublishCommittedFullscreenPresentation(",
        "fullscreen checkpoint current predicate exists");
    Check(checkpointAuthority.find("*currentKey == sessionKey") !=
                  std::string::npos &&
              checkpointAuthority.find("checkpoint->snapshotSequence <= currentSequence") !=
                  std::string::npos &&
              checkpointAuthority.find("checkpoint->presentationGeneration == descriptor->presentationGeneration") !=
                  std::string::npos &&
              checkpointAuthority.find("SameEndpointGeometry") != std::string::npos,
          "fullscreen checkpoint accepts only compatible successor authority for the exact committed session");
    const auto checkpointPublish = section(
        "void PublishCommittedFullscreenPresentation(",
        "[[nodiscard]] EmbeddedMediaSession* CurrentEmbeddedMediaSession(",
        "fullscreen checkpoint publication owner exists");
    Check(checkpointPublish.find("session->authority->sequence != snapshot->sequence") !=
                  std::string::npos &&
              checkpointPublish.find("session->committedGeometry") !=
                  std::string::npos &&
              checkpointPublish.find("committedFullscreenPresentation_ =") !=
                  std::string::npos,
          "fullscreen checkpoint publishes only after exact current manager geometry is committed");
    const auto fullscreenRepaint = section(
        "bool CommitCompositionRepaint(",
        "void Paint() {",
        "fullscreen repaint owner exists");
    Check(fullscreenRepaint.find("if (frames.frames.empty()) {") !=
                  std::string::npos &&
              fullscreenRepaint.find("fullscreen-empty-frame-repaint") !=
                  std::string::npos &&
              fullscreenRepaint.find("fullscreen-frame-repaint") !=
                  std::string::npos &&
              fullscreenRepaint.find("PublishCommittedFullscreenPresentation(") !=
                  std::string::npos,
          "fullscreen repaint reconciles staged geometry before an empty-frame return and publishes only after success");
    const auto fullscreenLayout = section(
        "const bool overlayLayoutCurrent = committedWidgetVisualState_",
        "if (!layoutCurrent) {",
        "fullscreen generic layout owner exists");
    Check(fullscreenLayout.find("fullscreenLayoutCurrent") != std::string::npos &&
              fullscreenLayout.find("sessionOwnsOverlayFullscreen") !=
                  std::string::npos &&
              fullscreenLayout.find("CommittedFullscreenPresentationCurrent(sessionKey, snapshot.sequence)") !=
                  std::string::npos,
          "ordinary non-fullscreen layout cannot reuse the fullscreen checkpoint");
    Check(source.find("!compositionPlacementInProgress_") != std::string::npos &&
              source.find("committedFullscreenPresentation_->sessionKey == sessionKey") !=
                  std::string::npos &&
              source.find("ClearStaleOverlayFullscreenMediaActivation(sessionKey)") !=
                  std::string::npos,
          "transaction-owned resize preserves fullscreen visibility while exact stale or stopped sessions retire their checkpoint");

    Check(managerSource.find(
              "case PresentationState::OverlayFullscreen:\n        return Endpoint::Overlay;") !=
                  std::string::npos &&
              managerSource.find("auto& owner = *endpoint == Endpoint::Overlay") !=
                  std::string::npos &&
              managerSource.find("if (owner && *owner != key)") !=
                  std::string::npos,
          "the manager admits one overlay endpoint owner and parks any displaced session before fullscreen presentation");

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
              source.find(
                  "mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Overlay)") !=
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

void TestOneShotFocusGroupEntryHostContract() {
    const auto source = ReadSource(
        fs::path{__FILE__}.parent_path() / "main.cpp");
    const auto admissionBegin = source.find(
        "const auto currentWidget = state_.surface() == widgetrail::Surface::Widget");
    const auto admissionEnd = source.find(
        "if (newerRefreshRequested)", admissionBegin);
    Check(admissionBegin != std::string::npos &&
              admissionEnd != std::string::npos &&
              admissionBegin < admissionEnd,
          "focus-group entry has one bounded ordinary admission section");
    const auto admission = source.substr(
        admissionBegin, admissionEnd - admissionBegin);
    const auto eligibilityBegin = admission.find(
        "ResolveFocusGroupEntryAdmission({");
    const auto eligibilityEnd = admission.find("});", eligibilityBegin);
    Check(eligibilityBegin != std::string::npos &&
              eligibilityEnd != std::string::npos &&
              eligibilityBegin < eligibilityEnd,
          "focus-group entry eligibility has one bounded host call");
    const auto eligibility = admission.substr(
        eligibilityBegin, eligibilityEnd - eligibilityBegin);
    Check(eligibility.find("IsWindowVisible(window_) != FALSE") != std::string::npos &&
              eligibility.find("WidgetOwnsInputFocus(event.widgetId)") != std::string::npos &&
              eligibility.find("textEntryModal_.active()") != std::string::npos &&
              eligibility.find("pinnedSurfaceCoordinator_.controllerFocused()") !=
                  std::string::npos &&
              eligibility.find(
                  "state_.surface() == widgetrail::Surface::Hidden") !=
                  std::string::npos &&
              eligibility.find(
                  "state_.selectedWidget() == event.widgetId") !=
                  std::string::npos &&
              eligibility.find("pinnedSurfaceCoordinator_.pinned()") ==
                  std::string::npos,
          "only temporary hidden same-widget authority is dormant while tray modal and pinned input retire entry");
    const auto observe = admission.find("ObserveFocusGroupEntryRequest(");
    const auto noRasterBarrier = admission.find(
        "if (focusGroupEntryPending)", observe);
    const auto noRasterAttempt = admission.find(
        "TryApplyNoRasterWidgetPresentation(", noRasterBarrier);
    Check(observe != std::string::npos &&
              noRasterBarrier != std::string::npos &&
              noRasterAttempt != std::string::npos &&
              observe < noRasterBarrier && noRasterBarrier < noRasterAttempt &&
              admission.find("pendingWidgetPresentationImpact_.reset();",
                  noRasterBarrier) < noRasterAttempt,
          "a request-only checkpoint forces a coherent render before consumption");

    const auto renderBegin = source.find(
        "const std::wstring provisionalRenderedFocusId = renderedFocusId;");
    const auto renderEnd = source.find(
        "if (inertRetainedSnapshot)", renderBegin);
    Check(renderBegin != std::string::npos && renderEnd != std::string::npos &&
              renderBegin < renderEnd,
          "focus-group entry has one bounded successful-render section");
    const auto render = source.substr(renderBegin, renderEnd - renderBegin);
    const auto candidatePreparation = render.find("PrepareFocusEntry(");
    const auto candidatePreview = render.find(
        "PreviewFocusGroupEntryRequest(", candidatePreparation);
    const auto candidateTargetGate = render.find(
        "if (preview.target)", candidatePreview);
    const auto finalPreparation = render.find(
        "PrepareFocusEntry(", candidatePreparation + 1);
    const auto finalPreview = render.find(
        "PreviewFocusGroupEntryRequest(", finalPreparation);
    const auto projection = render.find(
        "const widgetrail::accessibility::ProjectionKey projectionKey", finalPreview);
    const auto actualRender = render.find(
        "declarativeRenderer_->Render(", projection);
    const auto duplicateActualRender = render.find(
        "declarativeRenderer_->Render(", actualRender + 1);
    const auto succeeded = render.find(
        "if (result.succeeded && focusGroupEntryPrepared", actualRender);
    const auto commit = render.find(
        "CommitPreparedFocusGroupEntryRequest(", succeeded);
    const auto move = render.find("interactionSession_.MoveFocus(", commit);
    const auto semanticPublication = render.find(
        "if (result.succeeded)", move);
    Check(candidatePreparation != std::string::npos &&
              candidatePreview != std::string::npos &&
              candidateTargetGate != std::string::npos &&
              finalPreparation != std::string::npos &&
              finalPreview != std::string::npos &&
              projection != std::string::npos && actualRender != std::string::npos &&
              duplicateActualRender == std::string::npos &&
              succeeded != std::string::npos && commit != std::string::npos &&
              move != std::string::npos && semanticPublication != std::string::npos &&
              candidatePreparation < candidatePreview &&
              candidatePreview < candidateTargetGate &&
              candidateTargetGate < finalPreparation && finalPreparation < finalPreview &&
              finalPreview < projection && projection < actualRender &&
              actualRender < succeeded && succeeded < commit && commit < move &&
              move < semanticPublication,
          "candidate and final focus preparation settle before exactly one actual raster, then commit before semantic publication");
    Check(render.find("focusGroupEntryPrepared = true;", candidatePreview) >
              candidateTargetGate,
          "targetless deferred entry remains pending instead of preparing a null target");
    Check(render.find("focusGroupEntryWaiting = true;", candidateTargetGate) !=
              std::string::npos &&
              render.find("!focusGroupEntryWaiting", actualRender) !=
                  std::string::npos,
          "targetless pending entry suppresses final responsive and first-hit focus recovery");
    const auto committedFocus = render.substr(commit, semanticPublication - commit);
    Check(committedFocus.find("false, false") != std::string::npos &&
              committedFocus.find("InvalidateRect(") == std::string::npos,
          "the already-rendered focus target commits without retiring presentation state or scheduling a fallback frame");
    Check(render.find("state=preparation-retired", candidatePreparation) <
              actualRender &&
              render.find("state=settlement-retired", finalPreparation) <
              actualRender,
          "failed or unstable preparation retires before the only target-backed raster");

    const auto pinnedProjection = source.find(
        "candidate.focusGroupEntryRequest.reset();");
    const auto bridgeRetirement = source.find(
        "interactionSession_.ResetFocusGroupEntryRequests();");
    Check(pinnedProjection != std::string::npos &&
              bridgeRetirement != std::string::npos,
          "pinned projection and Bridge-session replacement cannot replay ordinary entry");

    const auto directionBegin = source.find("void HandleWidgetDirection(");
    const auto directionEnd = source.find(
        "void RefreshCurrentBridgeSnapshot", directionBegin);
    const auto moveDirectionBegin = source.find("void MoveWidgetFocus(");
    const auto moveDirectionEnd = source.find(
        "bool AuthoredHeldActionAdmissible", moveDirectionBegin);
    const auto actionBegin = source.find("void DispatchControllerAction(");
    const auto actionEnd = source.find(
        "void AttemptOverlayFullscreenMediaEntry", actionBegin);
    const auto pointerBegin = source.find("void HandlePointerActivation(");
    const auto pointerEnd = source.find("void Draw", pointerBegin);
    const auto restoreBegin = source.find("void RestoreFocusForActiveSurface(");
    const auto restoreEnd = source.find(
        "void ReturnPinnedControllerFocusToOverlay", restoreBegin);
    const bool directionSliceAvailable =
        directionBegin != std::string::npos && directionEnd != std::string::npos &&
        directionBegin < directionEnd;
    const bool moveDirectionSliceAvailable =
        moveDirectionBegin != std::string::npos &&
        moveDirectionEnd != std::string::npos &&
        moveDirectionBegin < moveDirectionEnd;
    const auto direction = directionSliceAvailable
        ? source.substr(directionBegin, directionEnd - directionBegin)
        : std::string{};
    const auto moveDirection = moveDirectionSliceAvailable
        ? source.substr(moveDirectionBegin, moveDirectionEnd - moveDirectionBegin)
        : std::string{};
    const auto directionalResolve = moveDirection.find(
        "ResolveDirectionalFocus(");
    const auto directionalRetire = moveDirection.find(
        "RetirePendingFocusGroupEntryForUserIntent(", directionalResolve);
    const auto directionalCommit = moveDirection.find(
        "interactionSession_.MoveFocus(", directionalRetire);
    Check(directionSliceAvailable &&
              direction.find("FocusGroupEntryRequestPending(") !=
                  std::string::npos &&
              direction.find(
                  "phase != widgetrail::input::NavigationEventPhase::Pressed") !=
                  std::string::npos &&
              moveDirectionSliceAvailable &&
              directionalResolve != std::string::npos &&
              directionalRetire != std::string::npos &&
              directionalCommit != std::string::npos &&
              directionalResolve < directionalRetire &&
              directionalRetire < directionalCommit &&
              direction.find("MoveWidgetFocus(") != std::string::npos &&
              actionBegin != std::string::npos && actionEnd != std::string::npos &&
              source.substr(actionBegin, actionEnd - actionBegin).find(
                  "RetirePendingFocusGroupEntryForUserIntent(") != std::string::npos &&
              pointerBegin != std::string::npos && pointerEnd != std::string::npos &&
              source.substr(pointerBegin, pointerEnd - pointerBegin).find(
                  "RetirePendingFocusGroupEntryForUserIntent(") != std::string::npos,
          "fresh direction resolves one exact target before retirement and commit while activation and pointer retain explicit cancellation");
    const auto action = source.substr(actionBegin, actionEnd - actionBegin);
    const auto focuslessActivationGuard = action.find(
        "interactionSession_.focusedElementId().empty()");
    const auto activationRetire = action.find(
        "RetirePendingFocusGroupEntryForUserIntent(");
    Check(focuslessActivationGuard != std::string::npos &&
              activationRetire != std::string::npos &&
              focuslessActivationGuard < activationRetire,
          "focusless A returns before retirement or first-hit activation");
    Check(restoreBegin != std::string::npos && restoreEnd != std::string::npos &&
              source.substr(restoreBegin, restoreEnd - restoreBegin).find(
                  "RetirePendingFocusGroupEntryForUserIntent(") == std::string::npos,
          "automatic focus restoration cannot cancel deferred entry intent");
    Check(source.find("L\"accessibility-action\"") != std::string::npos &&
              source.find("L\"ordinary-input-lost\"") != std::string::npos &&
              source.find("L\"pinned-controller-takeover\"") != std::string::npos,
          "accessibility and exact ordinary-input ownership changes retire deferred entry");
    const auto stateTransitionBegin = source.find(
        "template <typename Mutation>\n    void ApplyStateTransition(");
    const auto stateTransitionEnd = source.find(
        "void ApplyPresentation(", stateTransitionBegin);
    const auto stateTransition = source.substr(
        stateTransitionBegin, stateTransitionEnd - stateTransitionBegin);
    Check(stateTransition.find("temporaryHiddenSameWidgetTransition") !=
              std::string::npos &&
              stateTransition.find("interactionSession_.ClearLiveFocus();") !=
                  std::string::npos,
          "temporary hide and reopen preserve only the same-widget pending request while clearing live focus");
    const auto responsiveBegin = source.find(
        "bool ReconcileResponsiveFocusPersistence(");
    const auto responsiveEnd = source.find(
        "std::optional<widgetrail::input::WidgetInteractionAuthority>",
        responsiveBegin);
    Check(responsiveBegin != std::string::npos &&
              responsiveEnd != std::string::npos &&
              source.substr(responsiveBegin, responsiveEnd - responsiveBegin).find(
                  "RetirePendingFocusGroupEntryForUserIntent(") == std::string::npos,
          "automatic responsive focus normalization cannot cancel deferred entry intent");
}

void TestPinnedViewReturnsToPriorOverlayFocusContract() {
    const auto source = ReadSource(
        fs::path{__FILE__}.parent_path() / "main.cpp");
    const auto helperBegin = source.find(
        "void ReturnPinnedControllerFocusToOverlay(const ULONGLONG now) {");
    const auto helperEnd = source.find(
        "void HandleAccessibilityActions()", helperBegin);
    Check(helperBegin != std::string::npos && helperEnd != std::string::npos &&
              helperBegin < helperEnd,
          "pinned View has one bounded overlay-focus return owner");
    const auto helper = source.substr(helperBegin, helperEnd - helperBegin);
    const auto repeatRetirement = helper.find("heldActionRepeat_.Reset();");
    const auto pinnedExit = helper.find(
        "pinnedSurfaceCoordinator_.ExitControllerFocus();", repeatRetirement);
    const auto clickThrough = helper.find(
        "widgetrail::pinned::InteractionMode::ClickThrough", pinnedExit);
    const auto widgetOwner = helper.find(
        "state_.focusRegion() == widgetrail::FocusRegion::Widget", clickThrough);
    const auto restore = helper.find(
        "RestoreFocusForActiveSurface(state_.activeWidget());", widgetOwner);
    const auto mainFocus = helper.find("SetFocus(window_);", restore);
    const auto repaint = helper.find(
        "InvalidateRect(window_, nullptr, FALSE);", mainFocus);
    Check(repeatRetirement != std::string::npos &&
              helper.find("heldActionAuthority_.reset();", repeatRetirement) !=
                  std::string::npos &&
              pinnedExit != std::string::npos &&
              clickThrough != std::string::npos &&
              widgetOwner != std::string::npos && restore != std::string::npos &&
              mainFocus != std::string::npos && repaint != std::string::npos &&
              helper.find("SampleWidgetBack") == std::string::npos &&
              repeatRetirement < pinnedExit && pinnedExit < clickThrough &&
              clickThrough < widgetOwner && widgetOwner < restore &&
              restore < mainFocus && mainFocus < repaint,
          "View retires pinned-only authority and restores the retained Widget or Tray owner without forcing tray state");

    const auto focusedBegin = source.find(
        "if (pinnedSurfaceCoordinator_.controllerFocused()) {");
    const auto focusedEnd = source.find(
        "if (pinnedControllerCommand == widgetrail::pinned::ControllerCommand::Enter",
        focusedBegin);
    Check(focusedBegin != std::string::npos && focusedEnd != std::string::npos &&
              focusedBegin < focusedEnd,
          "focused pinned controller routing has a bounded source section");
    const auto focused = source.substr(focusedBegin, focusedEnd - focusedBegin);
    constexpr std::string_view returnCall =
        "ReturnPinnedControllerFocusToOverlay(now);";
    const auto firstReturn = focused.find(returnCall);
    Check(firstReturn != std::string::npos,
          "compact View exit uses the shared overlay-focus return owner");
    const auto secondReturn = focused.find(returnCall, firstReturn + returnCall.size());
    Check(secondReturn != std::string::npos &&
              focused.find(returnCall, secondReturn + returnCall.size()) ==
                  std::string::npos &&
              focused.find("Dispatch(widgetrail::Command::SampleWidgetBack);") ==
                  std::string::npos,
          "compact and ordinary View exits share the return owner and neither dispatches the forced tray transition");
    Check(focused.find(
              "queuePinnedButton((pressed & XINPUT_GAMEPAD_B) != 0, L\"b\");") !=
                  std::string::npos,
          "ordinary pinned B remains routed as widget-owned input");

    widgetrail::PersistentState persisted;
    persisted.order = {L"fixture.widget"};
    widgetrail::OverlayState overlay(persisted, {L"fixture.widget"});
    Check(overlay.Dispatch(widgetrail::Command::ToggleOverlay) &&
              overlay.surface() == widgetrail::Surface::Widget &&
              overlay.focusRegion() == widgetrail::FocusRegion::Tray &&
              overlay.selectedWidget() == L"fixture.widget",
          "tray-to-pin ownership begins at the exact selected tray item");
    Check(overlay.Dispatch(widgetrail::Command::Activate) &&
              overlay.surface() == widgetrail::Surface::Widget &&
              overlay.focusRegion() == widgetrail::FocusRegion::Widget &&
              overlay.activeWidget() == L"fixture.widget",
          "widget-to-pin ownership begins at the exact active widget");
    Check(overlay.Dispatch(widgetrail::Command::SampleWidgetBack) &&
              overlay.focusRegion() == widgetrail::FocusRegion::Tray,
          "the removed SampleWidgetBack transition is the operation that forced widget focus to tray");
}

void TestAcceptedHiddenBridgeControlPlaneContract() {
    const auto source = ReadSource(
        fs::path{__FILE__}.parent_path() / "main.cpp");
    const auto section = [&](const std::string_view begin,
                             const std::string_view end,
                             const std::string_view message) {
        const auto beginOffset = source.find(begin);
        Check(beginOffset != std::string::npos, message);
        const auto endOffset = source.find(end, beginOffset + begin.size());
        Check(endOffset != std::string::npos, message);
        return source.substr(beginOffset, endOffset - beginOffset);
    };

    const auto hiddenTimer = section(
        "if (wParam == kBridgeControlPlaneTimer) {",
        "if (performanceCountersActive_)",
        "hidden Bridge timer owns one bounded WM_TIMER branch");
    Check(hiddenTimer.find("(void)bridge_.PumpEvents();") != std::string::npos &&
              hiddenTimer.find("return 0;") != std::string::npos,
          "hidden Bridge timer drains the native control plane and terminates its branch");
    Check(hiddenTimer.find("TakeInvalidatedWidgetIds") == std::string::npos &&
              hiddenTimer.find("TakeRuntimeFailures") == std::string::npos &&
              hiddenTimer.find("PollController") == std::string::npos &&
              hiddenTimer.find("PumpBridgeEvents") == std::string::npos &&
              hiddenTimer.find("InvalidateRect") == std::string::npos &&
              hiddenTimer.find("ApplyPresentation") == std::string::npos &&
              hiddenTimer.find("Reconcile") == std::string::npos,
          "hidden Bridge timer cannot consume presentation, input, render, layout, paint, or composition work");

    const auto pump = section(
        "void PumpBridgeEvents(const bool controllerTick) {",
        "LRESULT HandleMessage(",
        "visible and pinned Bridge event pump owns one named dispatch boundary");
    const std::vector<std::string_view> orderedDispatches{
        "bridge_.PumpEvents()",
        "bridge_.TakeRuntimeFailures()",
        "PollController()",
        "bridge_.TakeArtworkResults()",
        "bridge_.TakeLocalWidgetPackageInstallResults()",
        "bridge_.TakePlatformAppearanceChangedRevision()",
        "bridge_.TakeWidgetCatalogChangedRevision()",
        "bridge_.TakeInvalidatedWidgetIds()",
        "bridge_.TakeActionFailures()",
        "actionFailureFeedback_.PublishBridgeFailures(actionFailures)",
        "bridge_.TakeHostEffects()",
        "RecordPinnedSurfaceWorkCounters()",
    };
    std::size_t priorDispatch{};
    for (const auto token : orderedDispatches) {
        const auto dispatch = pump.find(token, priorDispatch);
        Check(dispatch != std::string::npos,
              "Bridge event pump preserves every ordered dispatch stage");
        priorDispatch = dispatch + token.size();
    }
    Check(pump.find(
              "localWidgetPackageImport_.Complete(\n"
              "                    result.operationId,\n"
              "                    bridge_.bridgeSessionGeneration())") !=
              std::string::npos,
          "Bridge event pump preserves exact WIDGE-71 install terminal authority");

    const auto presentationTimer = section(
        "if (wParam == kControllerTimer || wParam == kPinnedSurfaceTimer) {",
        "} else if (wParam == kGuideCompatibilityTimer)",
        "visible and pinned timers own one presentation dispatch branch");
    Check(presentationTimer.find("PumpBridgeEvents(controllerTick);") !=
              std::string::npos &&
              presentationTimer.find("bridge_.TakeRuntimeFailures()") ==
              std::string::npos &&
              presentationTimer.find("bridge_.TakeHostEffects()") ==
              std::string::npos,
          "presentation timers delegate Bridge dispatch exactly once to the named pump");

    const auto hide = section(
        "void HideOverlay() {",
        "void RetireCompositionMotionForHiddenState()",
        "hidden overlay timer ownership section exists");
    Check(hide.find("KillTimer(window_, kControllerTimer);") != std::string::npos &&
              hide.find("KillTimer(window_, kPinnedSurfaceTimer);") != std::string::npos &&
              hide.find("KillTimer(window_, kBridgeControlPlaneTimer);") != std::string::npos &&
              hide.find("if (pinnedSurfaceCoordinator_.pinned())\n"
                        "            SetTimer(window_, kPinnedSurfaceTimer, 100, nullptr);\n"
                        "        else\n"
                        "            SetTimer(window_, kBridgeControlPlaneTimer, 100, nullptr);") !=
                  std::string::npos,
          "hidden unpinned state owns only the Bridge timer while hidden pinned state retains its existing timer");

    const auto show = section(
        "if (!wasVisible) {\n            actionFailureFeedback_.Show();",
        "pinnedSurfaceCoordinator_.OnOverlayShown();",
        "visible overlay timer ownership section exists");
    Check(show.find("KillTimer(window_, kBridgeControlPlaneTimer);") !=
              std::string::npos &&
              source.find("constexpr UINT kVisibleControllerTimerMilliseconds = 15;") !=
                  std::string::npos &&
              show.find("SetTimer(window_, kControllerTimer,\n"
                        "                     kVisibleControllerTimerMilliseconds, nullptr);") !=
                  std::string::npos,
          "visible overlay retires the hidden Bridge timer before controller sampling");
}

void TestSelectActivationRoutingContract() {
    const auto directory = fs::path{__FILE__}.parent_path();
    const auto host = ReadSource(directory / "main.cpp");
    const auto pinned = ReadSource(directory / "WidgetSurfaceCoordinator.cpp");

    const auto openBegin = host.find("OpenFocusedSelectPopup() {");
    const auto openEnd = host.find("void CommitSelectPopup(", openBegin);
    Check(openBegin != std::string::npos && openEnd != std::string::npos &&
              openBegin < openEnd,
          "full-widget Select activation has one bounded shared owner");
    const auto open = host.substr(openBegin, openEnd - openBegin);
    Check(open.find("interactionSession_.OpenSelectPopup(authority, *node)") !=
              std::string::npos &&
              open.find("SelectActivationResult::NotSelect") !=
                  std::string::npos &&
              open.find("SelectActivationResult::Opened") !=
                  std::string::npos,
          "full-widget Select owner preserves not-Select consumed-closed and opened outcomes");

    const auto dispatchBegin = host.find("void DispatchControllerAction(");
    const auto dispatchEnd = host.find(
        "[[nodiscard]] bool TryDispatchNativeMediaAction(", dispatchBegin);
    Check(dispatchBegin != std::string::npos &&
              dispatchEnd != std::string::npos && dispatchBegin < dispatchEnd,
          "full-widget controller dispatch has one bounded source section");
    const auto dispatch = host.substr(
        dispatchBegin, dispatchEnd - dispatchBegin);
    const auto selectActivation = dispatch.find(
        "const auto selectActivation = OpenFocusedSelectPopup();");
    const auto consumed = dispatch.find(
        "if (selectActivation !=\n"
        "                widgetrail::input::SelectActivationResult::NotSelect)\n"
        "                return;",
        selectActivation);
    const auto widgetDispatch = dispatch.find(
        "DispatchWidgetAction(", consumed);
    Check(selectActivation != std::string::npos &&
              consumed != std::string::npos &&
              widgetDispatch != std::string::npos &&
              selectActivation < consumed && consumed < widgetDispatch,
          "full-widget A consumes every Select result before generic widget or Bridge dispatch");

    const auto pinnedControllerBegin = host.find(
        "if (pinnedControllerCommand == widgetrail::pinned::ControllerCommand::Activate)");
    const auto pinnedControllerEnd = host.find(
        "} else if (pinnedControllerCommand ==", pinnedControllerBegin);
    Check(pinnedControllerBegin != std::string::npos &&
              pinnedControllerEnd != std::string::npos &&
              pinnedControllerBegin < pinnedControllerEnd,
          "pinned controller activation has one bounded fallback chain");
    const auto pinnedController = host.substr(
        pinnedControllerBegin, pinnedControllerEnd - pinnedControllerBegin);
    const auto pinnedSelect = pinnedController.find(
        "HandleFocusedSelectButton(L\"a\")");
    const auto pinnedSlider = pinnedController.find(
        "HandleFocusedSliderModeButton(");
    const auto pinnedGeneric = pinnedController.find("QueueFocusedInput(");
    Check(pinnedSelect != std::string::npos &&
              pinnedSlider != std::string::npos &&
              pinnedGeneric != std::string::npos &&
              pinnedSelect < pinnedSlider && pinnedSlider < pinnedGeneric,
          "pinned controller A consumes Select before slider and generic input fallback");

    const auto pointerBegin = pinned.find("case WM_LBUTTONUP:");
    const auto pointerEnd = pinned.find("case WM_CAPTURECHANGED:", pointerBegin);
    Check(pointerBegin != std::string::npos &&
              pointerEnd != std::string::npos && pointerBegin < pointerEnd,
          "pinned pointer activation has one bounded fallback chain");
    const auto pointer = pinned.substr(pointerBegin, pointerEnd - pointerBegin);
    const auto pointerSelect = pointer.find("HandleFocusedSelectButton(");
    const auto pointerGeneric = pointer.find("QueueResolvedInput(");
    Check(pointerSelect != std::string::npos &&
              pointerGeneric != std::string::npos &&
              pointerSelect < pointerGeneric,
          "pinned pointer consumes Select before generic input fallback");

    const auto keyboardBegin = pinned.find(
        "controllerFocused_ && wParam == VK_RETURN");
    const auto keyboardEnd = pinned.find(
        "wParam == VK_ESCAPE && controllerFocused_", keyboardBegin);
    Check(keyboardBegin != std::string::npos &&
              keyboardEnd != std::string::npos && keyboardBegin < keyboardEnd,
          "pinned keyboard activation has one bounded fallback chain");
    const auto keyboard = pinned.substr(
        keyboardBegin, keyboardEnd - keyboardBegin);
    const auto keyboardSelect = keyboard.find("HandleFocusedSelectButton(");
    const auto keyboardGeneric = keyboard.find("QueueFocusedInput(");
    Check(keyboardSelect != std::string::npos &&
              keyboardGeneric != std::string::npos &&
              keyboardSelect < keyboardGeneric,
          "pinned Enter consumes Select before generic input fallback");
}

void TestFullscreenShortcutDispatchContract() {
    const auto source = ReadSource(
        fs::path{__FILE__}.parent_path() / "main.cpp");
    const auto helperBegin = source.find(
        "void AttemptOverlayFullscreenMediaEntry(");
    const auto focusedBegin = source.find(
        "[[nodiscard]] bool TryDispatchNativeMediaAction(", helperBegin);
    const auto dispatchBegin = source.find(
        "void DispatchWidgetAction(", focusedBegin);
    Check(helperBegin != std::string::npos &&
              focusedBegin != std::string::npos &&
              dispatchBegin != std::string::npos &&
              helperBegin < focusedBegin && focusedBegin < dispatchBegin,
          "fullscreen A and scope shortcut routes share one bounded attempt owner");

    const auto helper = source.substr(helperBegin, focusedBegin - helperBegin);
    const auto firstAttempt = source.find(
        "EnterOverlayFullscreenMedia(decision)");
    Check(firstAttempt != std::string::npos &&
              source.find("EnterOverlayFullscreenMedia(decision)", firstAttempt + 1) ==
                  std::string::npos &&
              helper.find("EvaluateFullscreenEntry(widgetId, snapshot)") !=
                  std::string::npos &&
              helper.find("RefreshAndApplyPresentation([] {});") !=
                  std::string::npos &&
              helper.find("Fullscreen is unavailable for the current media session") !=
                  std::string::npos,
          "one fullscreen attempt owner preserves admission refresh and feedback");

    const auto focused = source.substr(focusedBegin, dispatchBegin - focusedBegin);
    Check(focused.find("protocolButton != L\"a\"") != std::string::npos &&
              focused.find("NavigationEventPhase::Pressed") != std::string::npos &&
              focused.find(
                  "widgetId, snapshot, L\"focused-node\", protocolButton") !=
                  std::string::npos,
          "focused fullscreen activation remains exact A Pressed authority");

    const auto scopeBegin = source.find(
        "if (isOpen && protocolButton != L\"a\"", dispatchBegin);
    const auto genericBegin = source.find(
        "const auto* descriptor = sessions_.FindDescriptor(widget);", scopeBegin);
    Check(scopeBegin != std::string::npos &&
              genericBegin != std::string::npos && scopeBegin < genericBegin,
          "scope fullscreen shortcut precedes generic package dispatch");
    const auto scope = source.substr(scopeBegin, genericBegin - scopeBegin);
    Check(scope.find("ResolveHostControllerShortcut(") != std::string::npos &&
              scope.find("ControllerShortcutResolutionStatus::Resolved") !=
                  std::string::npos &&
              scope.find("host.embeddedMediaSession.enterFullscreen") !=
                  std::string::npos &&
              scope.find(
                  "widget, *snapshot, L\"scope-shortcut\", protocolButton") !=
                  std::string::npos &&
              scope.find("return;") != std::string::npos,
          "page-wide non-A shortcut consumes only the exact admitted fullscreen action");
}

void TestDashboardQuickActionAuthorityContract() {
    const auto directory = fs::path{__FILE__}.parent_path();
    const auto host = ReadSource(directory / "main.cpp");
    const auto session = ReadSource(directory / "WidgetSessionCoordinator.h");

    Check(session.find("case WidgetCommittedViewUse::DashboardQuickAction:") !=
              std::string::npos &&
              session.find("authority == WidgetPresentationAuthority::Current") !=
                  std::string::npos &&
              session.find("lifecycle == WidgetLifecycleState::Visible") !=
                  std::string::npos,
          "dashboard quick actions have one explicit Current and Visible session policy");

    const auto interactionBegin = host.find(
        "const widgetrail::WidgetSnapshot* InteractionSnapshotFor(");
    const auto dashboardBegin = host.find(
        "const widgetrail::WidgetSnapshot* DashboardActionSnapshotFor(",
        interactionBegin);
    const auto controllerBegin = host.find(
        "const widgetrail::WidgetSnapshot* ControllerActionSnapshotFor(",
        dashboardBegin);
    const auto hostBackBegin = host.find(
        "struct HostRootBackAuthority final", controllerBegin);
    Check(interactionBegin != std::string::npos &&
              dashboardBegin != std::string::npos &&
              controllerBegin != std::string::npos &&
              hostBackBegin != std::string::npos &&
              interactionBegin < dashboardBegin && dashboardBegin < controllerBegin &&
              controllerBegin < hostBackBegin,
          "open-widget and dashboard controller snapshots have separate bounded owners");
    const auto snapshotOwners = host.substr(
        interactionBegin, hostBackBegin - interactionBegin);
    Check(snapshotOwners.find("WidgetCommittedViewUse::Interaction") !=
              std::string::npos &&
              snapshotOwners.find("WidgetCommittedViewUse::DashboardQuickAction") !=
                  std::string::npos &&
              snapshotOwners.find("? InteractionSnapshotFor(widgetId)") !=
                  std::string::npos &&
              snapshotOwners.find(": DashboardActionSnapshotFor(widgetId)") !=
                  std::string::npos,
          "controller context selects the exact open or dashboard lifecycle policy");

    const auto heldBegin = host.find(
        "std::optional<HeldActionAuthority> ResolveAuthoredHeldAction(");
    const auto heldEnd = host.find(
        "bool MediaHeldActionAuthorityCurrent(", heldBegin);
    Check(heldBegin != std::string::npos && heldEnd != std::string::npos &&
              heldBegin < heldEnd,
          "authored held actions have one bounded resolution section");
    const auto held = host.substr(heldBegin, heldEnd - heldBegin);
    Check(held.find("const bool dashboard =") != std::string::npos &&
              held.find("ControllerActionSnapshotFor(widgetId, !dashboard)") !=
                  std::string::npos &&
              held.find("snapshot->quickActions.begin()") != std::string::npos,
          "held dashboard actions select Visible authority before resolving bindings");

    const auto dispatchBegin = host.find("void DispatchWidgetAction(");
    const auto dispatchEnd = host.find(
        "[[nodiscard]] widgetrail::input::TextEntryModalTheme CurrentTextEntryTheme()",
        dispatchBegin);
    Check(dispatchBegin != std::string::npos && dispatchEnd != std::string::npos &&
              dispatchBegin < dispatchEnd,
          "widget action dispatch has one bounded host section");
    const auto dispatch = host.substr(dispatchBegin, dispatchEnd - dispatchBegin);
    const auto context = dispatch.find("const bool isOpen = interactiveWidget;");
    const auto snapshot = dispatch.find(
        "ControllerActionSnapshotFor(widget, isOpen)", context);
    const auto transport = dispatch.find("bridge_.SendControllerInput(", snapshot);
    Check(context != std::string::npos && snapshot != std::string::npos &&
              transport != std::string::npos && context < snapshot &&
              snapshot < transport &&
              dispatch.find("isOpen ? L\"openWidget\" : L\"dashboardQuickAction\"",
                            transport) != std::string::npos,
          "one-shot dispatch admits the context-specific snapshot before exact transport");

    const auto guideBegin = host.find(
        "const widgetrail::WidgetSnapshot* GuideSnapshotFor(");
    const auto guideEnd = host.find("template <typename Refresh>", guideBegin);
    Check(guideBegin != std::string::npos && guideEnd != std::string::npos &&
              guideBegin < guideEnd,
          "informational guide lookup has one bounded section");
    const auto guide = host.substr(guideBegin, guideEnd - guideBegin);
    Check(guide.find("presentation.HasCommittedViewAuthority()") !=
              std::string::npos &&
              guide.find("DashboardActionSnapshotFor") == std::string::npos,
          "guide labels remain stable while action admission stays context-specific");
}

void TestAcceptedMediaBackOwnershipHostContract() {
    const auto source = ReadSource(
        fs::path{__FILE__}.parent_path() / "main.cpp");
    const auto mediaBackBegin = source.find(
        "[[nodiscard]] bool HandleOverlayMediaBackButton(");
    const auto dispatchBegin = source.find(
        "void DispatchControllerAction(", mediaBackBegin);
    const auto dispatchEnd = source.find(
        "[[nodiscard]] bool TryDispatchNativeMediaAction(", dispatchBegin);
    Check(mediaBackBegin != std::string::npos &&
              dispatchBegin != std::string::npos &&
              dispatchEnd != std::string::npos && dispatchBegin < dispatchEnd,
          "controller action owner has one bounded source section");
    const auto mediaBack = source.substr(
        mediaBackBegin, dispatchBegin - mediaBackBegin);
    const auto dispatch = source.substr(dispatchBegin, dispatchEnd - dispatchBegin);
    // The precedence between the fullscreen and media-Back routes is owned by
    // RouteOverlayMediaBackButton and pinned exhaustively by
    // ControllerNavigationTests. This host helper owns the exact authority and
    // side effects; the dispatcher must defer to it before the generic route.
    const auto mediaBackRouter = mediaBack.find(
        "widgetrail::input::RouteOverlayMediaBackButton(");
    Check(mediaBackRouter != std::string::npos,
          "media Back helper resolves through the shared route owner");
    // Overlay fullscreen is host-owned on both edges. B must clear the host's
    // activation rather than reach the widget, so no package can strand a user
    // in a presentation that paints no tray, guide, or accessibility tree.
    const auto exitRoute = mediaBack.find(
        "OverlayMediaBackRoute::ExitOverlayFullscreen");
    const auto hostExit = mediaBack.find("ExitOverlayFullscreenMedia()", exitRoute);
    const auto widgetBack = mediaBack.find(
        "OverlayMediaBackRoute::HostWidgetBack", exitRoute);
    Check(exitRoute != std::string::npos && hostExit != std::string::npos &&
              widgetBack != std::string::npos && exitRoute < hostExit &&
              hostExit < widgetBack &&
              mediaBack.find("DispatchWidgetAction", exitRoute) == std::string::npos,
          "fullscreen B clears host-owned activation and never dispatches to the widget");
    const auto mediaBackOwnerBegin = mediaBack.find(
        "const auto overlayKey =");
    const auto mediaBackAuthorityBegin = mediaBack.find(
        "const bool overlayMediaAuthorityCurrent =");
    Check(mediaBackOwnerBegin != std::string::npos &&
              mediaBackAuthorityBegin != std::string::npos &&
              mediaBackOwnerBegin < mediaBackAuthorityBegin &&
              mediaBackAuthorityBegin < mediaBackRouter,
          "media Back authority is assembled before the shared route");
    const auto mediaBackBranch =
        mediaBack.substr(mediaBackOwnerBegin);
    Check(mediaBackBranch.find(
              "mediaSessions_.EndpointOwner(widgetrail::media::Endpoint::Overlay)") !=
              std::string::npos &&
              mediaBackBranch.find("mediaSessions_.Find(*overlayKey)") !=
              std::string::npos &&
              mediaBackBranch.find("EmbeddedMediaAuthorityCurrent(*overlayKey)") !=
              std::string::npos &&
              mediaBackBranch.find(
                  "overlaySession->authority->presentation !=\n                EmbeddedMediaPresentationState::Parked") !=
              std::string::npos &&
              mediaBackBranch.find(
                  "state_.surface() == widgetrail::Surface::Widget") !=
              std::string::npos &&
              mediaBackBranch.find(
                  "state_.focusRegion() == widgetrail::FocusRegion::Widget") !=
              std::string::npos &&
              mediaBackBranch.find(
                  "state_.activeWidget() ==\n                    overlaySession->authority->widgetId") !=
              std::string::npos &&
              mediaBackBranch.find(
                  "Dispatch(widgetrail::Command::SampleWidgetBack);") !=
              std::string::npos,
          "media Back shortcut requires exact overlay media and widget-owned focus authority");
    Check(mediaBackBranch.find("youtube") == std::string::npos &&
              mediaBackBranch.find("YouTube") == std::string::npos,
          "media Back ownership remains provider-neutral");
    const auto helperDispatch = dispatch.find(
        "HandleOverlayMediaBackButton(button)");
    const auto genericRoute = dispatch.find(
        "using widgetrail::input::ControllerActionContext", helperDispatch);
    Check(helperDispatch != std::string::npos &&
              genericRoute != std::string::npos && helperDispatch < genericRoute,
          "media Back resolves through the shared route owner before generic controller routing");
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
        TestBoundedOverlayDiagnosticContract();
        TestPinnedBackgroundCrossfadeDiagnosticWakeContract();
        TestWidgetContextActionHostContract();
        TestAcceptedOverlayFullscreenMediaHostContract();
        TestAcceptedWidgetOwnedFocusMemoryHostContract();
        TestOneShotFocusGroupEntryHostContract();
        TestPinnedViewReturnsToPriorOverlayFocusContract();
        TestAcceptedHiddenBridgeControlPlaneContract();
        TestSelectActivationRoutingContract();
        TestDashboardQuickActionAuthorityContract();
        TestFullscreenShortcutDispatchContract();
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
