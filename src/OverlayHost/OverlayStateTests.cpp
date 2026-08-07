#include "OverlayState.h"

#include <cstdlib>
#include <iostream>
#include <string_view>
#include <vector>

namespace {

void Check(const bool condition, const std::string_view message) {
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void Send(gba::OverlayState& state, const gba::Command command) {
    (void)state.Dispatch(command);
}

} // namespace

int main() {
    using gba::Command;
    using gba::OverlayState;
    using gba::PersistentState;
    using gba::Surface;

    OverlayState firstRun;
    Check(firstRun.surface() == Surface::Hidden, "starts hidden");
    Send(firstRun, Command::NavigateRight);
    Check(firstRun.selectedSlot() == 0, "hidden state ignores navigation");
    Send(firstRun, Command::ToggleOverlay);
    Check(firstRun.surface() == Surface::Dashboard, "first open shows dashboard");
    Send(firstRun, Command::NavigateRight);
    Send(firstRun, Command::Activate);
    Check(firstRun.surface() == Surface::Widget, "activate enters widget");
    Check(firstRun.activeWidget() == L"yt-music", "selected stable widget ID activates");
    Send(firstRun, Command::NavigateRight);
    Check(firstRun.activeWidget() == L"yt-music", "widget owns navigation input");
    Send(firstRun, Command::SampleWidgetBack);
    Check(firstRun.surface() == Surface::Dashboard, "widget Back returns to dashboard");
    Send(firstRun, Command::ToggleOverlay);
    Send(firstRun, Command::ToggleOverlay);
    Check(firstRun.surface() == Surface::Dashboard, "dashboard surface restores");
    Check(firstRun.selectedWidget() == L"yt-music", "dashboard selection restores by ID");

    OverlayState persistedSelection(firstRun.persistent());
    Send(persistedSelection, Command::ToggleOverlay);
    Check(persistedSelection.selectedWidget() == L"yt-music",
          "selection survives process restart by stable identity");

    OverlayState resumeWidget;
    Send(resumeWidget, Command::ToggleOverlay);
    Send(resumeWidget, Command::NavigateRight);
    Send(resumeWidget, Command::Activate);
    Send(resumeWidget, Command::ToggleOverlay);
    Send(resumeWidget, Command::ToggleOverlay);
    Check(resumeWidget.surface() == Surface::Widget && resumeWidget.activeWidget() == L"yt-music",
          "Guide restores an open widget");

    OverlayState reorder;
    Send(reorder, Command::ToggleOverlay);
    Send(reorder, Command::ToggleReorder);
    Send(reorder, Command::NavigateRight);
    Check(reorder.order() == std::vector<std::wstring>{L"yt-music", L"audio-mixer", L"performance"},
          "reorder swaps stable IDs");
    Check(reorder.selectedWidget() == L"audio-mixer", "focus follows reordered widget");
    Send(reorder, Command::Activate);
    Check(!reorder.reorderMode() && reorder.surface() == Surface::Dashboard,
          "activate confirms reorder without opening widget");

    PersistentState corrupted{{L"yt-music", L"yt-music", L"missing"}, L"missing", true};
    OverlayState repaired(corrupted);
    Check(repaired.order() == std::vector<std::wstring>{L"yt-music", L"audio-mixer", L"performance"},
          "duplicates and unavailable IDs are repaired while order is preserved");
    Send(repaired, Command::ToggleOverlay);
    Check(repaired.surface() == Surface::Dashboard, "unavailable last widget is discarded");

    PersistentState homeState{{L"performance", L"audio-mixer", L"yt-music"}, L"performance", false};
    OverlayState persistedHome(homeState);
    Send(persistedHome, Command::ToggleOverlay);
    Check(persistedHome.surface() == Surface::Dashboard &&
          persistedHome.selectedWidget() == L"performance",
          "persisted home order and selection restore by stable identity");

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

    OverlayState empty({}, {});
    Send(empty, Command::ToggleOverlay);
    Send(empty, Command::Activate);
    Check(empty.surface() == Surface::Dashboard && empty.selectedWidget().empty(),
          "empty catalogs remain controller-safe");

    std::cout << "OverlayStateTests passed\n";
    return EXIT_SUCCESS;
}
