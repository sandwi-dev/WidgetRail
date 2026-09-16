#include "OverlayState.h"

#include <algorithm>
#include <cstdlib>
#include <iostream>
#include <set>
#include <string_view>
#include <vector>

namespace {

void Check(const bool condition, const std::string_view message) {
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void Send(widgetrail::OverlayState& state, const widgetrail::Command command) {
    (void)state.Dispatch(command);
}

} // namespace

int main() {
    using widgetrail::Command;
    using widgetrail::FocusRegion;
    using widgetrail::OverlayState;
    using widgetrail::PersistentState;
    using widgetrail::Surface;

    OverlayState firstRun;
    Check(firstRun.surface() == Surface::Hidden, "starts hidden");
    Send(firstRun, Command::NavigateRight);
    Check(firstRun.selectedSlot() == 0, "hidden state ignores navigation");
    Send(firstRun, Command::ToggleOverlay);
    Check(firstRun.surface() == Surface::Widget && firstRun.activeWidget() == L"audio-mixer",
          "first toggle presents the selected widget without requiring tray navigation");
    Check(firstRun.focusRegion() == FocusRegion::Tray, "first open focuses the icon tray");
    Send(firstRun, Command::NavigateRight);
    Check(firstRun.surface() == Surface::Widget,
          "cycling the tray automatically presents the selected widget");
    Check(firstRun.focusRegion() == FocusRegion::Tray,
          "automatic presentation keeps controller focus on the tray");
    Check(firstRun.activeWidget() == L"yt-music", "tray selection and visible widget stay aligned");
    Send(firstRun, Command::Activate);
    Check(firstRun.focusRegion() == FocusRegion::Widget, "activate enters visible widget controls");
    Check(firstRun.activeWidget() == L"yt-music", "selected stable widget ID activates");
    Send(firstRun, Command::NavigateRight);
    Check(firstRun.activeWidget() == L"yt-music", "widget owns navigation input");
    const auto widgetSlot = firstRun.selectedSlot();
    const auto widgetOrder = firstRun.order();
    Send(firstRun, Command::NavigateLeft);
    Send(firstRun, Command::Activate);
    Send(firstRun, Command::Cancel);
    Send(firstRun, Command::ToggleReorder);
    Check(firstRun.surface() == Surface::Widget &&
          firstRun.activeWidget() == L"yt-music" &&
          firstRun.selectedSlot() == widgetSlot &&
          firstRun.order() == widgetOrder &&
          !firstRun.reorderMode(),
          "open widget retains host navigation and action ownership");
    Send(firstRun, Command::SampleWidgetBack);
    Check(firstRun.surface() == Surface::Widget && firstRun.activeWidget() == L"yt-music",
          "widget Back preserves the visible panel");
    Check(firstRun.focusRegion() == FocusRegion::Tray,
          "widget Back returns only controller focus to the tray");
    Send(firstRun, Command::NavigateRight);
    Check(firstRun.activeWidget() == L"performance" &&
          firstRun.selectedWidget() == L"performance" &&
          firstRun.focusRegion() == FocusRegion::Tray,
          "tray cycling automatically replaces the visible widget without entering it");
    Send(firstRun, Command::ToggleOverlay);
    Check(firstRun.surface() == Surface::Hidden, "tray Back route can close through ToggleOverlay");
    Send(firstRun, Command::CloseOverlay);
    Check(firstRun.surface() == Surface::Hidden,
          "provider-confirmed close is idempotent and cannot reopen the overlay");
    Send(firstRun, Command::ToggleOverlay);
    Check(firstRun.surface() == Surface::Widget &&
          firstRun.focusRegion() == FocusRegion::Tray,
          "Guide restores both the visible widget and tray focus region");
    Check(firstRun.selectedWidget() == L"performance", "tray selection restores by ID");

    OverlayState persistedSelection(firstRun.persistent());
    Send(persistedSelection, Command::ToggleOverlay);
    Check(persistedSelection.selectedWidget() == L"performance",
          "selection survives process restart by stable identity");

    OverlayState resumeWidget;
    Send(resumeWidget, Command::ToggleOverlay);
    Send(resumeWidget, Command::NavigateRight);
    Send(resumeWidget, Command::Activate);
    Send(resumeWidget, Command::ToggleOverlay);
    Send(resumeWidget, Command::ToggleOverlay);
    Check(resumeWidget.surface() == Surface::Widget && resumeWidget.activeWidget() == L"yt-music" &&
          resumeWidget.focusRegion() == FocusRegion::Widget,
          "Guide restores an open widget");
    Send(resumeWidget, Command::CloseOverlay);
    Check(resumeWidget.surface() == Surface::Hidden,
          "provider-confirmed close hides an open widget");
    Send(resumeWidget, Command::CloseOverlay);
    Check(resumeWidget.surface() == Surface::Hidden,
          "duplicate provider-confirmed close remains hidden");
    Send(resumeWidget, Command::ToggleOverlay);
    Check(resumeWidget.surface() == Surface::Widget &&
          resumeWidget.activeWidget() == L"yt-music",
          "confirmed close retains the last-used widget for the next Guide open");

    OverlayState reorder;
    Send(reorder, Command::ToggleOverlay);
    Send(reorder, Command::ToggleReorder);
    Send(reorder, Command::NavigateRight);
    Check(reorder.order() == std::vector<std::wstring>{L"yt-music", L"audio-mixer", L"performance"},
          "reorder swaps stable IDs");
    Check(reorder.selectedWidget() == L"audio-mixer", "focus follows reordered widget");
    Send(reorder, Command::Activate);
    Check(!reorder.reorderMode() && reorder.surface() == Surface::Widget &&
              reorder.focusRegion() == FocusRegion::Tray,
          "activate confirms reorder while preserving the visible widget and tray focus");

    PersistentState corrupted{{L"yt-music", L"yt-music", L"missing"}, L"missing", true};
    OverlayState repaired(corrupted);
    Check(repaired.order() == std::vector<std::wstring>{L"yt-music", L"audio-mixer", L"performance"},
          "duplicates and unavailable IDs are repaired while order is preserved");
    Send(repaired, Command::ToggleOverlay);
    Check(repaired.surface() == Surface::Widget && repaired.activeWidget() == repaired.selectedWidget(),
          "unavailable last widget falls back to the first available widget");

    PersistentState homeState{{L"performance", L"audio-mixer", L"yt-music"}, L"performance", false};
    OverlayState persistedHome(homeState);
    Send(persistedHome, Command::ToggleOverlay);
    Check(persistedHome.surface() == Surface::Widget &&
          persistedHome.activeWidget() == L"performance" &&
          persistedHome.focusRegion() == FocusRegion::Tray,
          "saved dashboard preference still presents the selected widget on first toggle");

    OverlayState catalogChanges({}, {L"one", L"two"});
    Send(catalogChanges, Command::ToggleOverlay);
    Send(catalogChanges, Command::NavigateRight);
    Check(catalogChanges.selectedWidget() == L"two", "dynamic catalog navigates");
    (void)catalogChanges.SetAvailableWidgets({L"two", L"three"});
    Check(catalogChanges.order() == std::vector<std::wstring>{L"two", L"three"},
          "removed IDs disappear and new IDs append");
    Check(catalogChanges.selectedWidget() == L"two", "selection survives catalog refresh");
    Send(catalogChanges, Command::Activate);
    (void)catalogChanges.SetAvailableWidgets({L"three"});
    Check(catalogChanges.surface() == Surface::Dashboard && catalogChanges.activeWidget().empty(),
          "removing an active widget safely returns to dashboard");

    OverlayState directSelection({}, {L"one", L"two", L"three"});
    Send(directSelection, Command::ToggleOverlay);
    Check(directSelection.TrySelectTrayWidget(L"three") &&
          directSelection.selectedWidget() == L"three" &&
          directSelection.activeWidget() == L"three" &&
          directSelection.focusRegion() == FocusRegion::Tray,
          "tray selection resolves a stable widget ID directly");
    const auto directState = directSelection.persistent();
    Check(!directSelection.TrySelectTrayWidget(L"missing") &&
          directSelection.persistent() == directState,
          "direct tray selection rejects unavailable IDs without mutation");
    Send(directSelection, Command::Activate);
    Check(!directSelection.TrySelectTrayWidget(L"one") &&
          directSelection.selectedWidget() == L"three",
          "widget-owned input rejects direct tray selection");

    std::vector<std::wstring> largeCatalog;
    for (int index = 0; index < 15; ++index)
        largeCatalog.push_back(L"widget-" + std::to_wstring(index));
    OverlayState large({}, largeCatalog);
    Send(large, Command::ToggleOverlay);
    std::set<std::wstring> visited;
    visited.emplace(large.selectedWidget());
    for (std::size_t index = 1; index < largeCatalog.size(); ++index) {
        Send(large, Command::NavigateRight);
        visited.emplace(large.selectedWidget());
        Check(large.focusRegion() == FocusRegion::Tray,
              "large-catalog cycling never enters widget content");
    }
    Check(visited.size() == largeCatalog.size() &&
          large.selectedWidget() == largeCatalog.back(),
          "Right reaches every enabled widget once in stable catalog order");
    for (std::size_t index = 1; index < largeCatalog.size(); ++index)
        Send(large, Command::NavigateLeft);
    Check(large.selectedWidget() == largeCatalog.front(),
          "Left reaches the first widget through the same stable order");

    const std::vector<std::wstring> productionOrder{
        L"settings", L"now-playing", L"games-apps", L"playnite-library",
        L"audio-mixer", L"network-controls", L"yt-music", L"spotify",
    };
    OverlayState ordered({}, {L"settings", L"spotify", L"audio-mixer"});
    Send(ordered, Command::ToggleOverlay);
    Check(ordered.TrySelectTrayWidget(L"spotify"), "select widget before reordering");
    Send(ordered, Command::ToggleReorder);
    Send(ordered, Command::NavigateLeft);
    Send(ordered, Command::Cancel);
    const auto savedOrder = ordered.persistent();
    Check(savedOrder.order.front() == L"spotify", "reorder changes the saved tray order");
    auto restarted = OverlayState::AwaitingCatalog(savedOrder);
    Check(restarted.order().empty() && restarted.selectedWidget().empty() &&
        restarted.persistent() == savedOrder && !restarted.OpenWidgetWithTrayFocus(L"spotify"),
        "pending catalog preserves saved preferences without admitting saved widget identities");
    Send(restarted, Command::ToggleOverlay);
    Send(restarted, Command::Activate);
    Check(restarted.persistent() == savedOrder && restarted.activeWidget().empty(),
        "input before catalog readiness cannot erase the saved order or open an unadmitted widget");
    (void)restarted.SetAvailableWidgets({L"settings", L"audio-mixer", L"spotify", L"network-controls"});
    Check(restarted.order() == std::vector<std::wstring>{L"spotify", L"settings", L"audio-mixer", L"network-controls"},
        "restart retains the reordered tray and appends newly discovered widgets");
    auto secondRestart = OverlayState::AwaitingCatalog(restarted.persistent());
    (void)secondRestart.SetAvailableWidgets({L"network-controls", L"settings", L"spotify", L"audio-mixer"});
    Check(secondRestart.order() == restarted.order(), "a second restart preserves order despite discovery order changes");
    auto removedOnRestart = OverlayState::AwaitingCatalog(savedOrder);
    (void)removedOnRestart.SetAvailableWidgets({L"settings", L"network-controls"});
    Check(removedOnRestart.order() == std::vector<std::wstring>{L"settings", L"network-controls"} &&
        !removedOnRestart.persistent().lastWidget && !removedOnRestart.persistent().reopenWidget,
        "authoritative catalog still removes unavailable saved identities and retires reopen state");
    auto knownEmpty = OverlayState::AwaitingCatalog(savedOrder);
    (void)knownEmpty.SetAvailableWidgets({});
    Check(knownEmpty.order().empty() && knownEmpty.persistent().order.empty(),
        "an explicitly received empty catalog is authoritative");

    OverlayState productionStartup({}, {});
    const auto beforeCatalog = productionStartup.persistent();
    Check(!productionStartup.OpenWidgetWithTrayFocus(L"settings") &&
              productionStartup.surface() == Surface::Hidden &&
              productionStartup.persistent() == beforeCatalog,
          "production startup waits for catalog authority before opening Settings");
    (void)productionStartup.SetAvailableWidgets(productionOrder);
    Check(productionStartup.OpenWidgetWithTrayFocus(L"settings") &&
              productionStartup.surface() == Surface::Widget &&
              productionStartup.selectedWidget() == L"settings" &&
              productionStartup.activeWidget() == L"settings" &&
              productionStartup.focusRegion() == FocusRegion::Tray,
          "catalog-gated first visibility opens real Settings with tray focus");
    Check(productionStartup.surface() != Surface::Dashboard,
          "production startup never exposes the legacy dashboard placeholder");
    Send(productionStartup, Command::ToggleOverlay);
    Check(productionStartup.surface() == Surface::Hidden,
          "Guide hides the startup Settings surface through the ordinary route");
    Send(productionStartup, Command::ToggleOverlay);
    Check(productionStartup.surface() == Surface::Widget &&
              productionStartup.selectedWidget() == L"settings" &&
              productionStartup.activeWidget() == L"settings" &&
              productionStartup.focusRegion() == FocusRegion::Tray,
          "later Guide reopen preserves Settings and the ordinary tray focus route");

    OverlayState catalogTray({}, productionOrder);
    Send(catalogTray, Command::ToggleOverlay);
    for (int index = 0; index < 4; ++index)
        Send(catalogTray, Command::NavigateRight);
    Check(catalogTray.selectedWidget() == L"audio-mixer" &&
              catalogTray.activeWidget() == L"audio-mixer" &&
              catalogTray.focusRegion() == FocusRegion::Tray,
          "production compact selection begins with one exact tray focus owner");
    auto reorderedDiscovery = productionOrder;
    std::reverse(reorderedDiscovery.begin(), reorderedDiscovery.end());
    reorderedDiscovery.push_back(L"catalog-probe");
    (void)catalogTray.SetAvailableWidgets(reorderedDiscovery);
    Check(catalogTray.order() ==
              std::vector<std::wstring>{
                  L"settings", L"now-playing", L"games-apps", L"playnite-library",
                  L"audio-mixer", L"network-controls", L"yt-music", L"spotify",
                  L"catalog-probe"} &&
              catalogTray.selectedWidget() == L"audio-mixer" &&
              catalogTray.activeWidget() == L"audio-mixer" &&
              catalogTray.focusRegion() == FocusRegion::Tray,
          "catalog replacement preserves order, compact selection, and tray focus by identity");
    (void)catalogTray.SetAvailableWidgets(productionOrder);
    Check(catalogTray.order() == productionOrder &&
              catalogTray.selectedWidget() == L"audio-mixer" &&
              catalogTray.activeWidget() == L"audio-mixer",
          "catalog removal retires only the removed identity without shifting selection");

    OverlayState empty({}, {});
    Send(empty, Command::ToggleOverlay);
    Send(empty, Command::Activate);
    Check(empty.surface() == Surface::Dashboard && empty.selectedWidget().empty(),
          "empty catalogs remain controller-safe");
    (void)empty.SetAvailableWidgets({L"settings"});
    Check(empty.surface() == Surface::Widget && empty.activeWidget() == L"settings" &&
              empty.focusRegion() == FocusRegion::Tray,
          "catalog arriving after first toggle replaces the empty dashboard without another input");
    OverlayState hiddenCatalog({}, {});
    (void)hiddenCatalog.SetAvailableWidgets({L"settings"});
    Check(hiddenCatalog.surface() == Surface::Hidden,
          "catalog arrival never opens a hidden overlay by itself");
    Send(hiddenCatalog, Command::ToggleOverlay);
    Check(hiddenCatalog.surface() == Surface::Widget && hiddenCatalog.activeWidget() == L"settings",
          "hidden startup presents its ready catalog on the first toggle");

    OverlayState radial;
    Send(radial, Command::ToggleOverlay);
    Send(radial, Command::Activate);
    const auto originalWidget = std::wstring(radial.activeWidget());
    Send(radial, Command::SampleWidgetBack);
    (void)radial.Dispatch(Command::NavigateRight);
    Check(radial.activeWidget() == radial.selectedWidget() && radial.selectedWidget() != originalWidget &&
          radial.focusRegion() == FocusRegion::Tray,
          "radial highlight previews the selected widget while keeping tray focus");
    const auto previewWidget = std::wstring(radial.selectedWidget());
    Check(radial.ReturnToActiveWidget() && radial.selectedWidget() == previewWidget &&
          radial.focusRegion() == FocusRegion::Widget,
          "closing the wheel returns to the globally selected preview");
    Send(radial, Command::SampleWidgetBack);
    const auto targetWidget = radial.order()[2];
    Check(radial.TrySelectTrayWidget(targetWidget) && radial.activeWidget() == targetWidget &&
          radial.focusRegion() == FocusRegion::Tray,
          "direct radial selection previews its target without entering widget focus");
    (void)radial.Dispatch(Command::Activate);
    Check(radial.activeWidget() == targetWidget && radial.focusRegion() == FocusRegion::Widget,
          "A commits exactly the highlighted radial target");

    // Both entry paths share selection and ordinary visible-widget authority.
    OverlayState hybrid({}, {L"music", L"audio", L"settings"});
    const auto sendHybrid = [&](const Command command) {
        return hybrid.Dispatch(command);
    };
    sendHybrid(Command::ToggleOverlay);
    sendHybrid(Command::Activate);
    const auto initial = std::wstring{hybrid.activeWidget()};
    sendHybrid(Command::SampleWidgetBack);
    Check(hybrid.radialTrayOpen(true) && !hybrid.radialTrayOpen(false),
          "Back opens the wheel only when the radial setting is enabled");
    sendHybrid(Command::NavigateRight);
    Check(hybrid.activeWidget() == hybrid.selectedWidget() && hybrid.selectedWidget() != initial,
          "Back entry previews radial highlights using the ordinary tray lifecycle");
    const auto radialPreview = std::wstring(hybrid.selectedWidget());
    Check(hybrid.ReturnToActiveWidget() && hybrid.selectedWidget() == radialPreview,
          "radial cancel returns to the selected preview");
    sendHybrid(Command::FocusTray);
    Check(!hybrid.radialTrayOpen(true) && hybrid.focusRegion() == FocusRegion::Tray,
          "downward boundary enters the rail even with radial switching enabled");
    sendHybrid(Command::NavigateRight);
    Check(hybrid.activeWidget() == hybrid.selectedWidget() && hybrid.activeWidget() != initial,
          "rail selection previews the selected widget for existing dashboard actions");
    sendHybrid(Command::Activate);
    Check(hybrid.focusRegion() == FocusRegion::Widget && !hybrid.radialTrayOpen(true),
          "rail activation returns ordinary widget focus");
    sendHybrid(Command::SampleWidgetBack);
    Check(hybrid.radialTrayOpen(true), "Back uses the wheel after prior rail navigation");
    (void)hybrid.ReturnToActiveWidget();
    sendHybrid(Command::FocusTray);
    sendHybrid(Command::ToggleReorder);
    Check(hybrid.reorderMode() && !hybrid.radialTrayOpen(true),
          "rail reordering does not switch to the wheel");
    sendHybrid(Command::ToggleOverlay);
    Check(hybrid.surface() == Surface::Hidden && !hybrid.radialTrayOpen(true),
          "the rail close action hides the overlay instead of opening the wheel");
    const auto hiddenEntry = hybrid.trayEntry();
    Check(!sendHybrid(Command::FocusTray) && !sendHybrid(Command::SampleWidgetBack) &&
              hybrid.trayEntry() == hiddenEntry,
          "hidden directional and Back commands cannot change tray presentation");
    std::cout << "OverlayStateTests passed\n";
    return EXIT_SUCCESS;
}
