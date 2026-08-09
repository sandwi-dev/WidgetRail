#include "TrayLayout.h"

#include <cstdlib>
#include <iostream>

namespace {

int checks{};

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
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
    Check(paged && paged->tiles.size() == 3, "narrow tray retains a bounded window");
    Check(paged->tiles.front().slot == 7 && paged->tiles.back().slot == 9,
          "selected tail slot remains visible without reordering");

    const auto embedded = gba::shell::ComputeTrayLayout(
        720, 540, 2, 1, gba::shell::TrayBand{420, 510});
    Check(embedded && embedded->stripBounds.y == 420 &&
          embedded->stripBounds.height == 90,
          "widget-surface tray uses the supplied final band");
    const auto compact = gba::shell::ComputeTrayLayout(40, 120, 4, 2);
    Check(compact && compact->tiles.size() == 1 &&
          compact->tiles[0].slot == 2 && compact->tiles[0].bounds.width == 28,
          "below-preferred width degrades to one bounded selected tile");
    Check(!gba::shell::ComputeTrayLayout(0, 600, 1, 0) &&
          !gba::shell::ComputeTrayLayout(800, 600, 0, 0),
          "empty or invalid surfaces publish no tray geometry");

    std::cout << "TrayLayoutTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
