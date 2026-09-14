#include "TrayLayout.h"

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <optional>
#include <set>
#include <vector>

namespace {

int checks{};

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void CheckCompleteReachability(
    const float width,
    const float height,
    const std::size_t count,
    const std::size_t initialSelection,
    const std::optional<widgetrail::shell::TrayBand> band,
    const char* message) {
    std::vector<bool> reached(count);
    std::vector<std::size_t> pending{initialSelection};
    reached[initialSelection] = true;
    for (std::size_t cursor = 0; cursor < pending.size(); ++cursor) {
        const auto selected = pending[cursor];
        const auto layout = widgetrail::shell::ComputeTrayLayout(
            width, height, count, selected, band);
        Check(layout.has_value(), "reachable tray state produced no layout");
        Check(std::any_of(
                  layout->tiles.begin(), layout->tiles.end(),
                  [selected](const auto& tile) { return tile.slot == selected; }),
              "reachable tray state omitted its selected identity");
        const auto discover = [&](const std::size_t slot) {
            Check(slot < count, "tray projection exposed an invalid catalog slot");
            if (!reached[slot]) {
                reached[slot] = true;
                pending.push_back(slot);
            }
        };
        for (const auto& tile : layout->tiles) discover(tile.slot);
        if (layout->previousOverflow)
            discover(layout->previousOverflow->targetSlot);
        if (layout->nextOverflow)
            discover(layout->nextOverflow->targetSlot);
    }
    Check(std::all_of(reached.begin(), reached.end(), [](const bool value) {
              return value;
          }), message);
}

const widgetrail::shell::TrayTileLayout* FindTile(
    const widgetrail::shell::TrayLayout& layout,
    const std::size_t slot) {
    const auto found = std::find_if(
        layout.tiles.begin(), layout.tiles.end(),
        [slot](const auto& tile) { return tile.slot == slot; });
    return found == layout.tiles.end() ? nullptr : &*found;
}

void CheckCenteredSelection(
    const widgetrail::shell::TrayLayout& layout,
    const float viewportWidth,
    const std::size_t selectedSlot,
    const char* message) {
    const auto* selected = FindTile(layout, selectedSlot);
    Check(selected &&
              std::abs(
                  selected->bounds.x + selected->bounds.width * 0.5F -
                  viewportWidth * 0.5F) < 0.01F,
          message);
}

void CheckUniqueProjection(
    const widgetrail::shell::TrayLayout& layout,
    const char* message) {
    std::set<std::size_t> slots;
    for (const auto& tile : layout.tiles) slots.insert(tile.slot);
    Check(slots.size() == layout.tiles.size(), message);
}

} // namespace

int main() {
    const auto standard = widgetrail::shell::ComputeTrayLayout(800, 600, 3, 0);
    Check(standard && standard->tiles.size() == 3, "all fitting widgets are visible");
    Check(standard->stripBounds.y == 505 && standard->stripBounds.height == 64 &&
          standard->stripBounds.x == standard->tiles.front().bounds.x &&
          standard->stripBounds.x + standard->stripBounds.width ==
              standard->tiles.back().bounds.x +
                  standard->tiles.back().bounds.width,
          "dashboard tray publishes the tight visual control envelope centered in its band");
    Check(standard->tiles[0].slot == 2 && standard->tiles[1].slot == 0 &&
              standard->tiles[2].slot == 1,
          "visible slots preserve cyclic catalog order around the selection");
    CheckCenteredSelection(
        *standard, 800.0F, 0,
        "an underfilled odd catalog keeps the selected tile at the viewport center");
    Check(standard->tiles[0].bounds.width == 64 &&
          standard->tiles[0].bounds.height == 64,
          "standard tiles retain their preferred square size");
    const auto* firstHit = widgetrail::shell::HitTestTray(
        *standard, standard->tiles[0].bounds.x, standard->tiles[0].bounds.y);
    Check(firstHit && firstHit->slot == 2, "top-left edge is inside its projected tile");
    Check(!widgetrail::shell::HitTestTray(
              *standard,
              standard->tiles[0].bounds.x + standard->tiles[0].bounds.width,
              standard->tiles[0].bounds.y),
          "right edge is outside a tile");
    const float firstGapX = standard->tiles[0].bounds.x +
        standard->tiles[0].bounds.width + 1.0F;
    Check(firstGapX < standard->tiles[1].bounds.x &&
          !widgetrail::shell::HitTestTray(
              *standard, firstGapX, standard->tiles[0].bounds.y + 1.0F) &&
          !widgetrail::shell::HitTestTrayOverflow(
              *standard, firstGapX, standard->tiles[0].bounds.y + 1.0F),
          "transparent space inside the tight envelope owns no pointer target");

    Check(std::abs(widgetrail::shell::ComputeTrayCapacityWidth(1920.0F) -
                   1152.0F) < 0.01F &&
              std::abs(widgetrail::shell::ComputeTrayCapacityWidth(1280.0F) -
                       768.0F) < 0.01F &&
              std::abs(widgetrail::shell::ComputeTrayCapacityWidth(640.0F) -
                       384.0F) < 0.01F,
          "tray capacity applies sixty percent once across supported monitor widths");
    Check(widgetrail::shell::ComputeTrayCapacityWidth(191.0F) == 191.0F &&
              widgetrail::shell::ComputeTrayCapacityWidth(192.0F) == 192.0F,
          "the narrow envelope remains contained at the exact overflow threshold");

    const auto paged = widgetrail::shell::ComputeTrayLayout(1000, 600, 10, 9);
    Check(paged && paged->tiles.size() == 5,
          "overflowed tray retains a symmetric odd visible capacity");
    Check(paged->tiles[0].slot == 7 && paged->tiles[1].slot == 8 &&
              paged->tiles[2].slot == 9 && paged->tiles[3].slot == 0 &&
              paged->tiles[4].slot == 1,
          "tail selection wraps through exact cyclic catalog order");
    Check(paged->previousOverflow && paged->nextOverflow &&
              paged->previousOverflow->hiddenCount == 5 &&
              paged->previousOverflow->targetSlot == 6 &&
              paged->nextOverflow->hiddenCount == 5 &&
              paged->nextOverflow->targetSlot == 2,
          "both overflow controls expose the exact adjacent hidden identities");
    CheckCenteredSelection(
        *paged, 1000.0F, 9,
        "overflowed tail selection remains fixed at the viewport center");
    CheckUniqueProjection(
        *paged, "cyclic overflow projection never duplicates a widget identity");
    const auto* previousHit = widgetrail::shell::HitTestTrayOverflow(
        *paged, paged->previousOverflow->bounds.x, paged->previousOverflow->bounds.y);
    Check(previousHit && previousHit->targetSlot == 6,
          "overflow hit testing uses the same visible control bounds");

    const auto firstPage = widgetrail::shell::ComputeTrayLayout(1000, 600, 10, 0);
    const auto middlePage = widgetrail::shell::ComputeTrayLayout(1000, 600, 10, 5);
    Check(firstPage && firstPage->previousOverflow && firstPage->nextOverflow &&
              firstPage->tiles.front().slot == 8 &&
              firstPage->tiles.back().slot == 2,
          "first selection exposes its cyclic previous and next neighbors");
    Check(middlePage && middlePage->previousOverflow && middlePage->nextOverflow &&
              middlePage->tiles.front().slot == 3 &&
              middlePage->tiles.back().slot == 7,
          "middle selection retains the same fixed-center cyclic window");

    for (const float scaledCompactWidth : {540.0F, 432.0F, 360.0F}) {
        for (const std::size_t selected : {0U, 7U, 14U}) {
            const auto scaled = widgetrail::shell::ComputeTrayLayout(
                scaledCompactWidth, 700, 15, selected,
                widgetrail::shell::TrayBand{590, 680});
            Check(scaled && !scaled->tiles.empty(),
                  "compact 100/125/150 percent profile retains visible tiles");
            const auto selectedTile = std::find_if(
                scaled->tiles.begin(), scaled->tiles.end(),
                [selected](const auto& tile) { return tile.slot == selected; });
            Check(selectedTile != scaled->tiles.end(),
                  "first, middle, and last selections stay visible at every scale");
            Check(scaled->previousOverflow && scaled->nextOverflow,
                  "scaled cyclic windows expose both hidden directions");
            CheckCenteredSelection(
                *scaled, scaledCompactWidth, selected,
                "scaled selection retains the fixed horizontal center");
            CheckUniqueProjection(
                *scaled, "scaled cyclic projection retains unique identities");
            const auto insideStrip = [&](const widgetrail::declarative::Rect& bounds) {
                return bounds.x >= scaled->stripBounds.x &&
                    bounds.y >= scaled->stripBounds.y &&
                    bounds.x + bounds.width <=
                        scaled->stripBounds.x + scaled->stripBounds.width &&
                    bounds.y + bounds.height <=
                        scaled->stripBounds.y + scaled->stripBounds.height;
            };
            Check(std::all_of(
                      scaled->tiles.begin(), scaled->tiles.end(),
                      [&](const auto& tile) { return insideStrip(tile.bounds); }) &&
                  (!scaled->previousOverflow ||
                   insideStrip(scaled->previousOverflow->bounds)) &&
                  (!scaled->nextOverflow ||
                   insideStrip(scaled->nextOverflow->bounds)),
                  "tiles and overflow controls stay inside the final tray band");
        }
    }

    const auto exactFit = widgetrail::shell::ComputeTrayLayout(
        262, 600, 3, 1, std::nullopt,
        widgetrail::shell::TrayWidthBasis::ExactCapacity);
    const auto exactFitPlusOne = widgetrail::shell::ComputeTrayLayout(
        262, 600, 4, 1, std::nullopt,
        widgetrail::shell::TrayWidthBasis::ExactCapacity);
    Check(exactFit && exactFit->tiles.size() == 3 &&
          !exactFit->previousOverflow && !exactFit->nextOverflow,
          "exact-fit catalog needs no overflow representation");
    Check(exactFitPlusOne && exactFitPlusOne->tiles.size() == 1 &&
          exactFitPlusOne->nextOverflow,
          "exact-fit plus one switches to an explicit reachable overflow window");
    CheckCenteredSelection(
        *exactFit, 262.0F, 1,
        "an exact-capacity child surface does not apply sixty percent a second time");

    const auto evenCatalog = widgetrail::shell::ComputeTrayLayout(800, 600, 4, 1);
    Check(evenCatalog && evenCatalog->tiles.size() == 4 &&
              !evenCatalog->previousOverflow && !evenCatalog->nextOverflow &&
              evenCatalog->tiles[0].slot == 3 &&
              evenCatalog->tiles[1].slot == 0 &&
              evenCatalog->tiles[2].slot == 1 &&
              evenCatalog->tiles[3].slot == 2,
          "an underfilled even catalog reserves a spare position without duplication");
    CheckCenteredSelection(
        *evenCatalog, 800.0F, 1,
        "an underfilled even catalog retains the selected center anchor");
    CheckUniqueProjection(
        *evenCatalog, "an underfilled even catalog publishes each identity once");

    const auto embedded = widgetrail::shell::ComputeTrayLayout(
        720, 540, 2, 1, widgetrail::shell::TrayBand{420, 510});
    Check(embedded && embedded->stripBounds.y == 433 &&
          embedded->stripBounds.height == 64 &&
          embedded->stripBounds.y + embedded->stripBounds.height * 0.5F ==
              (420.0F + 510.0F) * 0.5F,
          "open-widget tray centers its tight envelope in the supplied post-guide band");
    const auto dashboardGuideBand = widgetrail::shell::ComputeTrayLayout(
        800, 180, 3, 0, widgetrail::shell::TrayBand{66, 180});
    const auto openWidgetGuideBand = widgetrail::shell::ComputeTrayLayout(
        800, 328, 3, 0, widgetrail::shell::TrayBand{198, 328});
    Check(dashboardGuideBand && openWidgetGuideBand &&
          dashboardGuideBand->stripBounds.y * 2.0F +
                  dashboardGuideBand->stripBounds.height == 66.0F + 180.0F &&
          openWidgetGuideBand->stripBounds.y * 2.0F +
                  openWidgetGuideBand->stripBounds.height == 198.0F + 328.0F,
          "dashboard and open-widget rendered-guide inputs produce equal visual gaps");

    const auto productionBand = [](const float height) {
        return widgetrail::shell::TrayBand{height - 112.0F, height - 14.0F};
    };
    const auto compactEight = widgetrail::shell::ComputeTrayLayout(
        592, 698, 8, 0, productionBand(698));
    const auto wideEight = widgetrail::shell::ComputeTrayLayout(
        1052, 878, 8, 7, productionBand(878));
    Check(compactEight && wideEight && compactEight->tiles.size() == 3 &&
              wideEight->tiles.size() == 8 &&
              compactEight->previousOverflow && compactEight->nextOverflow &&
              !wideEight->previousOverflow && !wideEight->nextOverflow &&
              wideEight->tiles.front().bounds.width >= 44.0F,
          "supported compact and wide capacities choose overflow only when required");
    CheckCenteredSelection(
        *compactEight, 592.0F, 0,
        "compact monitor capacity centers the selected production identity");
    CheckCenteredSelection(
        *wideEight, 1052.0F, 7,
        "wide monitor capacity centers the selected production identity");
    const auto wideNine = widgetrail::shell::ComputeTrayLayout(
        1920, 1080, 9, 0, productionBand(1080));
    Check(wideNine && wideNine->tiles.size() == 9 &&
              !wideNine->previousOverflow && !wideNine->nextOverflow &&
              wideNine->tiles.front().bounds.width == 64.0F,
          "sixty-percent wide capacity fits nine normal-size catalog targets");
    CheckCompleteReachability(
        592, 698, 8, 0, productionBand(698),
        "Audio Mixer compact overflow reaches the complete production catalog");
    CheckCompleteReachability(
        632, 878, 8, 4, productionBand(878),
        "Network Controls compact overflow reaches both catalog edges");
    CheckCompleteReachability(
        1052, 878, 8, 7, productionBand(878),
        "Playnite Library wide tray reaches every identity without overflow loss");
    const auto compact = widgetrail::shell::ComputeTrayLayout(40, 120, 4, 2);
    Check(compact && compact->tiles.size() == 1 &&
          compact->tiles[0].slot == 2 && compact->tiles[0].bounds.width == 28,
          "below-preferred width degrades to one bounded selected tile");
    Check(compact && !compact->previousOverflow && !compact->nextOverflow,
          "sub-minimum diagnostic width avoids unreadably scaled controls");
    const auto minimumReachable = widgetrail::shell::ComputeTrayLayout(
        192, 240, 4, 2, widgetrail::shell::TrayBand{120, 220},
        widgetrail::shell::TrayWidthBasis::ExactCapacity);
    Check(minimumReachable && minimumReachable->tiles.size() == 1 &&
              minimumReachable->previousOverflow &&
              minimumReachable->nextOverflow,
          "192 logical DIPs is the minimum fully reachable tray envelope");
    CheckCenteredSelection(
        *minimumReachable, 192.0F, 2,
        "minimum supported tray envelope retains the fixed center selection");
    Check(!widgetrail::shell::ComputeTrayLayout(0, 600, 1, 0) &&
          !widgetrail::shell::ComputeTrayLayout(800, 600, 0, 0),
          "empty or invalid surfaces publish no tray geometry");

    const auto sameBounds = [](const auto& a, const auto& b) {
        return std::abs(a.x - b.x) < .01F && std::abs(a.y - b.y) < .01F &&
            std::abs(a.width - b.width) < .01F && std::abs(a.height - b.height) < .01F;
    };
    for (float width : {192.0F, 250.0F, 420.0F, 680.0F, 1100.0F, 1920.0F, 2800.0F}) {
        for (std::size_t count : {1U, 2U, 11U, 40U}) {
            for (std::size_t selected = 0; selected < count; ++selected) {
                const auto original = widgetrail::shell::ComputeTrayLayout(
                    width, 160, count, selected, widgetrail::shell::TrayBand{40,140});
                const auto status = widgetrail::shell::ComputeTrayStatusLayout(
                    width, 160, count, selected, widgetrail::shell::TrayBand{40,140});
                Check(original && status, "status layout preserves valid icon layout");
                Check(original->tiles.size() == status->tiles.size(), "status preserves visible icon capacity");
                for (std::size_t i = 0; i < original->tiles.size(); ++i) {
                    Check(original->tiles[i].slot == status->tiles[i].slot &&
                        sameBounds(original->tiles[i].bounds, status->tiles[i].bounds),
                        "status preserves icon identities, positions and sizes");
                }
                const auto sameOverflow = [&](const auto& a, const auto& b) {
                    return a.has_value() == b.has_value() && (!a ||
                        (a->targetSlot == b->targetSlot && a->hiddenCount == b->hiddenCount &&
                         sameBounds(a->bounds, b->bounds)));
                };
                Check(sameOverflow(original->previousOverflow, status->previousOverflow) &&
                    sameOverflow(original->nextOverflow, status->nextOverflow),
                    "status preserves overflow geometry and navigation targets");
                CheckCenteredSelection(*status, width, selected, "status does not move the centered selection");

                const float childWidth = widgetrail::shell::ComputeTrayStatusSurfaceWidth(width);
                const auto child = widgetrail::shell::ComputeTrayStatusLayout(
                    childWidth, 160, count, selected, widgetrail::shell::TrayBand{40,140},
                    widgetrail::shell::TrayWidthBasis::ExactCapacity,
                    widgetrail::shell::ComputeTrayCapacityWidth(width));
                Check(child && child->tiles.size() == status->tiles.size(),
                    "expanded child surface preserves monitor icon capacity");
                CheckCenteredSelection(*child, childWidth, selected, "child selection stays centered");
                Check(child->statusBounds.has_value() == status->statusBounds.has_value(),
                    "child and monitor agree on status availability");
                const float childOffset = (width - childWidth) * .5F;
                for (std::size_t i = 0; i < status->tiles.size(); ++i) {
                    Check(child->tiles[i].slot == status->tiles[i].slot &&
                        std::abs(child->tiles[i].bounds.x + childOffset - status->tiles[i].bounds.x) < .01F &&
                        child->tiles[i].bounds.width == status->tiles[i].bounds.width,
                        "child and fallback project identical monitor icon geometry");
                }
                if (!status->statusBounds) continue;
                const auto& bounds = *status->statusBounds;
                Check(bounds.x >= 0 && bounds.x + bounds.width <= width + .01F,
                    "clock remains within the tray surface");
                Check(!widgetrail::shell::HitTestTray(*status, bounds.x + 1, bounds.y + 1) &&
                    !widgetrail::shell::HitTestTrayOverflow(*status, bounds.x + 1, bounds.y + 1),
                    "status never steals tray pointer actions");
                for (const auto& tile : status->tiles)
                    Check(tile.bounds.x + tile.bounds.width <= bounds.x, "status cannot overlap an icon");
                if (status->nextOverflow)
                    Check(status->nextOverflow->bounds.x + status->nextOverflow->bounds.width < bounds.x,
                        "status keeps a gap after the next arrow");
            }
        }
    }
    const auto narrow = widgetrail::shell::ComputeTrayStatusLayout(192, 160, 40, 0);
    Check(narrow && !narrow->statusBounds && narrow->previousOverflow && narrow->nextOverflow,
        "narrow surfaces omit status instead of sacrificing icon navigation");
    const auto compactStatus = widgetrail::shell::ComputeTrayStatusLayout(680, 160, 40, 0);
    Check(compactStatus && compactStatus->statusBounds && compactStatus->statusBounds->width == 100,
        "limited spare space keeps a compact clock");
    const auto wideStatus = widgetrail::shell::ComputeTrayStatusLayout(1920, 160, 40, 0);
    Check(wideStatus && wideStatus->statusBounds && wideStatus->statusBounds->width == 188,
        "wide surfaces retain clock and connectivity indicators");
    std::cout << "TrayLayoutTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
