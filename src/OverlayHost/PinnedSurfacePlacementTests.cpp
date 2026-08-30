#include "PinnedSurfacePlacement.h"

#include <Windows.h>

#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>
#include <string_view>
#include <vector>

namespace {

int checks{};

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) throw std::runtime_error(std::string(message));
}

widgetrail::pinned::MonitorWorkArea Primary() {
    return {L"DISPLAY-A", {-1920, 0, 0, 1040}, 96, true};
}

widgetrail::pinned::MonitorWorkArea Secondary() {
    return {L"DISPLAY-B", {0, -200, 2560, 1240}, 144, false};
}

} // namespace

int main() {
    try {
        using widgetrail::input::NavigationDirection;
        using widgetrail::pinned::PlacementDirection;
        Check(!widgetrail::pinned::ResolvePlacementDirection(NavigationDirection::None),
              "neutral navigation has no placement direction");
        Check(widgetrail::pinned::ResolvePlacementDirection(NavigationDirection::Left) ==
                  PlacementDirection::Left,
              "left navigation maps to left placement");
        Check(widgetrail::pinned::ResolvePlacementDirection(NavigationDirection::Right) ==
                  PlacementDirection::Right,
              "right navigation maps to right placement");
        Check(widgetrail::pinned::ResolvePlacementDirection(NavigationDirection::Up) ==
                  PlacementDirection::Up,
              "up navigation maps to up placement");
        Check(widgetrail::pinned::ResolvePlacementDirection(NavigationDirection::Down) ==
                  PlacementDirection::Down,
              "down navigation maps to down placement");
        Check(!widgetrail::pinned::ResolvePlacementDirection(
                  static_cast<NavigationDirection>(255)),
              "invalid navigation remains explicitly unmapped");

        const std::vector monitors{Primary(), Secondary()};
        const auto fallback = widgetrail::pinned::ResolveDurablePlacement(monitors, std::nullopt);
        Check(fallback && fallback->monitorId == L"DISPLAY-A" &&
                  fallback->bounds.left < fallback->bounds.right &&
                  fallback->bounds.top >= Primary().workArea.top &&
                  fallback->bounds.right <= Primary().workArea.right,
              "default placement is fully contained on the primary work area");

        const widgetrail::pinned::DurablePinnedPlacement persisted{
            1, L"DISPLAY-B", 0.25, 0.75, 640.0F, 360.0F};
        const auto mixedDpi = widgetrail::pinned::ResolveDurablePlacement(monitors, persisted);
        Check(mixedDpi && mixedDpi->monitorId == L"DISPLAY-B" &&
                  mixedDpi->dpi == 144 && !mixedDpi->usedFallback &&
                  mixedDpi->bounds.right - mixedDpi->bounds.left == 960 &&
                  mixedDpi->bounds.bottom - mixedDpi->bounds.top == 540,
              "normalized placement restores logical size on mixed DPI");
        const auto captured = widgetrail::pinned::CaptureDurablePlacement(
            Secondary(), mixedDpi->bounds);
        Check(captured && std::abs(captured->anchorX - 0.25) < 0.001 &&
                  std::abs(captured->anchorY - 0.75) < 0.001 &&
                  std::abs(captured->widthDip - 640.0F) < 0.01F,
              "physical bounds round-trip to normalized work-area anchors");

        auto invalid = persisted;
        invalid.anchorX = std::numeric_limits<double>::quiet_NaN();
        const auto invalidReset = widgetrail::pinned::ResolveDurablePlacement(monitors, invalid);
        Check(invalidReset && invalidReset->monitorId == L"DISPLAY-A" &&
                  invalidReset->usedFallback,
              "invalid persisted placement resets as one whole record");

        const auto monitorLoss = widgetrail::pinned::ResolveDurablePlacement({Primary()}, persisted);
        Check(monitorLoss && monitorLoss->monitorId == L"DISPLAY-A" &&
                  monitorLoss->usedFallback && monitorLoss->bounds.left >= -1920 &&
                  monitorLoss->bounds.right <= 0,
              "missing monitor reflows the durable logical placement fully on-screen");
        const widgetrail::pinned::MonitorWorkArea tooSmall{
            L"TINY", {0, 0, 120, 80}, 96, true};
        Check(!widgetrail::pinned::ResolveDurablePlacement({tooSmall}, std::nullopt),
              "work area below the declared minimum fails closed");

        auto session = widgetrail::pinned::BeginPlacementSession(
            widgetrail::pinned::PlacementMode::Move, fallback->bounds, L"runtime-1", L"view-1");
        Check(session.has_value(), "move gesture captures exact generation and original bounds");
        const auto original = session->original;
        Check(widgetrail::pinned::StepPlacementSession(
                  *session, widgetrail::pinned::PlacementDirection::Left, Primary()) &&
                  session->current.left < original.left,
              "controller move step changes one constrained logical position");
        Check(original.left - session->current.left ==
                  static_cast<int>(
                      widgetrail::surface_geometry::kPlacementAdjustmentStepDip),
              "placement session consumes the one centralized 32-DIP step");
        Check(!widgetrail::pinned::CommitPlacementSession(
                  *session, L"stale", L"view-1", Primary()),
              "stale runtime cannot commit placement");
        const auto committed = widgetrail::pinned::CommitPlacementSession(
            *session, L"runtime-1", L"view-1", Primary());
        Check(committed.has_value(), "current exact generation commits normalized placement");
        Check(session->original.left == original.left &&
                  session->original.top == original.top,
              "cancel authority retains the exact pre-gesture rectangle");

        auto resize = widgetrail::pinned::BeginPlacementSession(
            widgetrail::pinned::PlacementMode::Resize, fallback->bounds, L"runtime-1", L"view-1");
        Check(widgetrail::pinned::StepPlacementSession(
                  *resize, widgetrail::pinned::PlacementDirection::Left, Primary(), {}, 10000.0F) &&
                  resize->current.right - resize->current.left >= 240,
              "resize clamps at the injected minimum");
        Check(widgetrail::pinned::SetPlacementSessionBounds(
                  *resize, {-99999, -99999, 99999, 99999}, Primary()) &&
                  resize->current.left >= Primary().workArea.left &&
                  resize->current.top >= Primary().workArea.top &&
                  resize->current.right <= Primary().workArea.right &&
                  resize->current.bottom <= Primary().workArea.bottom,
              "pointer-sized proposal is finite and work-area constrained");

        const widgetrail::pinned::PlacementLimits expandedLimits{
            widgetrail::surface_geometry::kMinimumPinnedWidthDip,
            widgetrail::surface_geometry::kMinimumPinnedHeightDip,
            widgetrail::surface_geometry::kMaximumPinnedWidthDip,
            widgetrail::surface_geometry::kMaximumPinnedHeightDip,
        };
        auto expandedResize = widgetrail::pinned::BeginPlacementSession(
            widgetrail::pinned::PlacementMode::Resize,
            {-1600, 100, -600, 700}, L"runtime-1", L"view-1");
        Check(expandedResize && widgetrail::pinned::StepPlacementSession(
                  *expandedResize, widgetrail::pinned::PlacementDirection::Right,
                  Primary(), expandedLimits) &&
                  expandedResize->current.right - expandedResize->current.left == 1032,
              "expanded preview applies the shared 32-DIP resize within live limits");
        const auto expandedCommitted = widgetrail::pinned::CommitPlacementSession(
            *expandedResize, L"runtime-1", L"view-1", Primary(), expandedLimits);
        Check(expandedCommitted &&
                  std::abs(expandedCommitted->widthDip - 1032.0F) < 0.01F &&
                  std::abs(expandedCommitted->heightDip - 600.0F) < 0.01F,
              "expanded preview commits its exact constrained geometry");

        const auto storeRoot = std::filesystem::temp_directory_path() /
            (L"wrail-dlv068-" + std::to_wstring(GetCurrentProcessId()));
        const auto storePath = storeRoot / L"placement.ini";
        widgetrail::pinned::PinnedPlacementStore store(storePath);
        std::wstring error;
        auto selectedLayout = *committed;
        selectedLayout.opacityPercent = 70;
        selectedLayout.selectedLayoutId = L"compact-now-playing";
        Check(store.Save(L"dev.example.widget", selectedLayout, error),
              "placement store commits atomically");
        const auto loaded = store.Load(L"dev.example.widget");
        Check(loaded && loaded->monitorId == committed->monitorId &&
                  std::abs(loaded->anchorX - committed->anchorX) < 0.000001 &&
                  loaded->opacityPercent == 70 &&
                  loaded->selectedLayoutId == L"compact-now-playing",
              "placement store restores geometry opacity and selected layout together");
        auto expandedStored = *expandedCommitted;
        expandedStored.opacityPercent = 90;
        expandedStored.selectedLayoutId = L"host.full-widget";
        Check(store.Save(L"dev.example.expanded", expandedStored, error),
              "durable storage accepts geometry admitted beyond legacy default maxima");
        const auto expandedReloaded = store.Load(L"dev.example.expanded");
        Check(expandedReloaded &&
                  std::abs(expandedReloaded->widthDip - expandedStored.widthDip) < 0.01F &&
                  std::abs(expandedReloaded->heightDip - expandedStored.heightDip) < 0.01F,
              "committed expanded geometry reloads without snap-back");
        {
            std::wofstream legacy(storePath, std::ios::trunc);
            legacy << L"wrail-pinned-placement-v2 1\n"
                   << L"\"dev.example.widget\" 1 \"DISPLAY-A\" 0.5 0.5 480 270 80\n";
        }
        const auto legacyLoaded = store.Load(L"dev.example.widget");
        Check(legacyLoaded && legacyLoaded->opacityPercent == 80 &&
                  legacyLoaded->selectedLayoutId == L"host.full-widget",
              "pre-layout placement falls back to the host Full widget checkpoint");
        {
            std::wofstream malformed(storePath, std::ios::trunc);
            malformed << L"wrail-pinned-placement-v1 1\n\"bad\" 99 \"DISPLAY-A\" 0 0 480 270\n";
        }
        Check(!store.Load(L"bad"), "malformed or incompatible persistence resets closed");
        std::error_code cleanup;
        std::filesystem::remove_all(storeRoot, cleanup);

        std::cout << "PinnedSurfacePlacementTests passed (" << checks << " checks)\n";
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "PinnedSurfacePlacementTests failed after " << checks
                  << " checks: " << exception.what() << '\n';
        return 1;
    }
}
