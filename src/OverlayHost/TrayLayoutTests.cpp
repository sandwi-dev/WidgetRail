#include "TrayLayout.h"

#include <algorithm>
#include <cstdlib>
#include <iostream>
#include <optional>
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
    const std::optional<gba::shell::TrayBand> band,
    const char* message) {
    std::vector<bool> reached(count);
    std::vector<std::size_t> pending{initialSelection};
    reached[initialSelection] = true;
    for (std::size_t cursor = 0; cursor < pending.size(); ++cursor) {
        const auto selected = pending[cursor];
        const auto layout = gba::shell::ComputeTrayLayout(
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

} // namespace

int main() {
    const auto standard = gba::shell::ComputeTrayLayout(800, 600, 3, 0);
    Check(standard && standard->tiles.size() == 3, "all fitting widgets are visible");
    Check(standard->stripBounds.y == 488 && standard->stripBounds.height == 98,
          "dashboard fallback band matches the rendered shell");
    Check(standard->tiles[0].slot == 0 && standard->tiles[2].slot == 2,
          "visible slots preserve catalog order");
    Check(standard->tiles[0].bounds.width == 64 &&
          standard->tiles[0].bounds.height == 64,
          "standard tiles retain their preferred square size");
    const auto* firstHit = gba::shell::HitTestTray(
        *standard, standard->tiles[0].bounds.x, standard->tiles[0].bounds.y);
    Check(firstHit && firstHit->slot == 0, "top-left edge is inside a tile");
    Check(!gba::shell::HitTestTray(
              *standard,
              standard->tiles[0].bounds.x + standard->tiles[0].bounds.width,
              standard->tiles[0].bounds.y),
          "right edge is outside a tile");

    const auto paged = gba::shell::ComputeTrayLayout(300, 600, 10, 9);
    Check(paged && paged->tiles.size() == 2, "narrow tray reserves explicit overflow controls");
    Check(paged->tiles.front().slot == 8 && paged->tiles.back().slot == 9,
          "selected tail slot remains visible without reordering");
    Check(paged->previousOverflow && !paged->nextOverflow &&
          paged->previousOverflow->hiddenCount == 8 &&
          paged->previousOverflow->targetSlot == 7,
          "tail window exposes every preceding item through one previous control");
    const auto* previousHit = gba::shell::HitTestTrayOverflow(
        *paged, paged->previousOverflow->bounds.x, paged->previousOverflow->bounds.y);
    Check(previousHit && previousHit->targetSlot == 7,
          "overflow hit testing uses the same visible control bounds");

    const auto firstPage = gba::shell::ComputeTrayLayout(300, 600, 10, 0);
    Check(firstPage && !firstPage->previousOverflow && firstPage->nextOverflow &&
          firstPage->nextOverflow->hiddenCount == 8 &&
          firstPage->nextOverflow->targetSlot == 2,
          "first window exposes the exact next off-page item");
    const auto middlePage = gba::shell::ComputeTrayLayout(300, 600, 10, 5);
    Check(middlePage && middlePage->previousOverflow && middlePage->nextOverflow &&
          middlePage->tiles.front().slot <= 5 && middlePage->tiles.back().slot >= 5,
          "middle selection stays visible between both overflow controls");

    for (const float scaledCompactWidth : {540.0F, 432.0F, 360.0F}) {
        for (const std::size_t selected : {0U, 7U, 14U}) {
            const auto scaled = gba::shell::ComputeTrayLayout(
                scaledCompactWidth, 700, 15, selected,
                gba::shell::TrayBand{590, 680});
            Check(scaled && !scaled->tiles.empty(),
                  "compact 100/125/150 percent profile retains visible tiles");
            const auto selectedTile = std::find_if(
                scaled->tiles.begin(), scaled->tiles.end(),
                [selected](const auto& tile) { return tile.slot == selected; });
            Check(selectedTile != scaled->tiles.end(),
                  "first, middle, and last selections stay visible at every scale");
            Check((selected == 0 || scaled->previousOverflow) &&
                  (selected == 14 || scaled->nextOverflow),
                  "scaled compact windows expose every hidden direction");
            const auto insideStrip = [&](const gba::declarative::Rect& bounds) {
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

    const auto exactFit = gba::shell::ComputeTrayLayout(262, 600, 3, 1);
    const auto exactFitPlusOne = gba::shell::ComputeTrayLayout(262, 600, 4, 1);
    Check(exactFit && exactFit->tiles.size() == 3 &&
          !exactFit->previousOverflow && !exactFit->nextOverflow,
          "exact-fit catalog needs no overflow representation");
    Check(exactFitPlusOne && exactFitPlusOne->tiles.size() == 1 &&
          exactFitPlusOne->nextOverflow,
          "exact-fit plus one switches to an explicit reachable overflow window");

    const auto embedded = gba::shell::ComputeTrayLayout(
        720, 540, 2, 1, gba::shell::TrayBand{420, 510});
    Check(embedded && embedded->stripBounds.y == 420 &&
          embedded->stripBounds.height == 90,
          "widget-surface tray uses the supplied final band");

    const auto productionBand = [](const float height) {
        return gba::shell::TrayBand{height - 112.0F, height - 14.0F};
    };
    const auto compactEight = gba::shell::ComputeTrayLayout(
        592, 698, 8, 0, productionBand(698));
    const auto wideEight = gba::shell::ComputeTrayLayout(
        1052, 878, 8, 7, productionBand(878));
    Check(compactEight && wideEight && compactEight->tiles.size() == 8 &&
          wideEight->tiles.size() == 8 &&
          !compactEight->previousOverflow && !compactEight->nextOverflow &&
          !wideEight->previousOverflow && !wideEight->nextOverflow,
          "current catalog retains one full tray capacity across widget widths");
    Check(std::abs(
              compactEight->stripBounds.width - wideEight->stripBounds.width) < 0.01F &&
          std::abs(
              (compactEight->stripBounds.x + compactEight->stripBounds.width * 0.5F) -
              592.0F * 0.5F) < 0.01F &&
          std::abs(
              (wideEight->stripBounds.x + wideEight->stripBounds.width * 0.5F) -
              1052.0F * 0.5F) < 0.01F,
          "stable tray bounds remain centered on the shared screen anchor");
    const auto compactNine = gba::shell::ComputeTrayLayout(
        592, 698, 9, 0, productionBand(698));
    Check(compactNine && compactNine->tiles.size() == 9 &&
          !compactNine->previousOverflow && !compactNine->nextOverflow &&
          compactNine->tiles.front().bounds.width >= 44.0F,
          "bounded catalog addition retains full capacity and controller targets");
    CheckCompleteReachability(
        592, 698, 8, 0, productionBand(698),
        "Audio Mixer compact overflow reaches the complete production catalog");
    CheckCompleteReachability(
        632, 878, 8, 4, productionBand(878),
        "Network Controls compact overflow reaches both catalog edges");
    CheckCompleteReachability(
        1052, 878, 8, 7, productionBand(878),
        "Game Launcher wide tray reaches every identity without overflow loss");
    const auto compact = gba::shell::ComputeTrayLayout(40, 120, 4, 2);
    Check(compact && compact->tiles.size() == 1 &&
          compact->tiles[0].slot == 2 && compact->tiles[0].bounds.width == 28,
          "below-preferred width degrades to one bounded selected tile");
    Check(compact && !compact->previousOverflow && !compact->nextOverflow,
          "sub-minimum diagnostic width avoids unreadably scaled controls");
    Check(!gba::shell::ComputeTrayLayout(0, 600, 1, 0) &&
          !gba::shell::ComputeTrayLayout(800, 600, 0, 0),
          "empty or invalid surfaces publish no tray geometry");

    std::cout << "TrayLayoutTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
