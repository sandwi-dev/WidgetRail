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

gba::pinned::MonitorWorkArea Primary() {
    return {L"DISPLAY-A", {-1920, 0, 0, 1040}, 96, true};
}

gba::pinned::MonitorWorkArea Secondary() {
    return {L"DISPLAY-B", {0, -200, 2560, 1240}, 144, false};
}

} // namespace

int main() {
    try {
        const std::vector monitors{Primary(), Secondary()};
        const auto fallback = gba::pinned::ResolveDurablePlacement(monitors, std::nullopt);
        Check(fallback && fallback->monitorId == L"DISPLAY-A" &&
                  fallback->bounds.left < fallback->bounds.right &&
                  fallback->bounds.top >= Primary().workArea.top &&
                  fallback->bounds.right <= Primary().workArea.right,
              "default placement is fully contained on the primary work area");

        const gba::pinned::DurablePinnedPlacement persisted{
            1, L"DISPLAY-B", 0.25, 0.75, 640.0F, 360.0F};
        const auto mixedDpi = gba::pinned::ResolveDurablePlacement(monitors, persisted);
        Check(mixedDpi && mixedDpi->monitorId == L"DISPLAY-B" &&
                  mixedDpi->dpi == 144 && !mixedDpi->usedFallback &&
                  mixedDpi->bounds.right - mixedDpi->bounds.left == 960 &&
                  mixedDpi->bounds.bottom - mixedDpi->bounds.top == 540,
              "normalized placement restores logical size on mixed DPI");
        const auto captured = gba::pinned::CaptureDurablePlacement(
            Secondary(), mixedDpi->bounds);
        Check(captured && std::abs(captured->anchorX - 0.25) < 0.001 &&
                  std::abs(captured->anchorY - 0.75) < 0.001 &&
                  std::abs(captured->widthDip - 640.0F) < 0.01F,
              "physical bounds round-trip to normalized work-area anchors");

        auto invalid = persisted;
        invalid.anchorX = std::numeric_limits<double>::quiet_NaN();
        const auto invalidReset = gba::pinned::ResolveDurablePlacement(monitors, invalid);
        Check(invalidReset && invalidReset->monitorId == L"DISPLAY-A" &&
                  invalidReset->usedFallback,
              "invalid persisted placement resets as one whole record");

        const auto monitorLoss = gba::pinned::ResolveDurablePlacement({Primary()}, persisted);
        Check(monitorLoss && monitorLoss->monitorId == L"DISPLAY-A" &&
                  monitorLoss->usedFallback && monitorLoss->bounds.left >= -1920 &&
                  monitorLoss->bounds.right <= 0,
              "missing monitor reflows the durable logical placement fully on-screen");
        const gba::pinned::MonitorWorkArea tooSmall{
            L"TINY", {0, 0, 120, 80}, 96, true};
        Check(!gba::pinned::ResolveDurablePlacement({tooSmall}, std::nullopt),
              "work area below the declared minimum fails closed");

        auto session = gba::pinned::BeginPlacementSession(
            gba::pinned::PlacementMode::Move, fallback->bounds, L"runtime-1", L"view-1");
        Check(session.has_value(), "move gesture captures exact generation and original bounds");
        const auto original = session->original;
        Check(gba::pinned::StepPlacementSession(
                  *session, gba::pinned::PlacementDirection::Left, Primary()) &&
                  session->current.left < original.left,
              "controller move step changes one constrained logical position");
        Check(!gba::pinned::CommitPlacementSession(
                  *session, L"stale", L"view-1", Primary()),
              "stale runtime cannot commit placement");
        const auto committed = gba::pinned::CommitPlacementSession(
            *session, L"runtime-1", L"view-1", Primary());
        Check(committed.has_value(), "current exact generation commits normalized placement");
        Check(session->original.left == original.left &&
                  session->original.top == original.top,
              "cancel authority retains the exact pre-gesture rectangle");

        auto resize = gba::pinned::BeginPlacementSession(
            gba::pinned::PlacementMode::Resize, fallback->bounds, L"runtime-1", L"view-1");
        Check(gba::pinned::StepPlacementSession(
                  *resize, gba::pinned::PlacementDirection::Left, Primary(), {}, 10000.0F) &&
                  resize->current.right - resize->current.left >= 240,
              "resize clamps at the injected minimum");
        Check(gba::pinned::SetPlacementSessionBounds(
                  *resize, {-99999, -99999, 99999, 99999}, Primary()) &&
                  resize->current.left >= Primary().workArea.left &&
                  resize->current.top >= Primary().workArea.top &&
                  resize->current.right <= Primary().workArea.right &&
                  resize->current.bottom <= Primary().workArea.bottom,
              "pointer-sized proposal is finite and work-area constrained");

        const auto storeRoot = std::filesystem::temp_directory_path() /
            (L"wrail-dlv068-" + std::to_wstring(GetCurrentProcessId()));
        const auto storePath = storeRoot / L"placement.ini";
        gba::pinned::PinnedPlacementStore store(storePath);
        std::wstring error;
        Check(store.Save(L"dev.example.widget", *committed, error),
              "placement store commits atomically");
        const auto loaded = store.Load(L"dev.example.widget");
        Check(loaded && loaded->monitorId == committed->monitorId &&
                  std::abs(loaded->anchorX - committed->anchorX) < 0.000001,
              "placement store restores the exact schema-1 record");
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
