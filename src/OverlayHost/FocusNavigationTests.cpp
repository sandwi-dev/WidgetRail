#include "FocusNavigation.h"

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

void Add(gba::RenderResult& result, std::wstring id, gba::declarative::Rect rect,
         const bool enabled = true, std::wstring scope = L"root") {
    result.focusScopes[id] = std::move(scope);
    result.focusRects[id] = rect;
    result.hitRegions.push_back({std::move(id), rect, enabled});
}

} // namespace

int main() {
    using gba::input::FindGeometricFocusTarget;
    using gba::input::NavigationDirection;
    gba::RenderResult result;
    Add(result, L"play", {100, 50, 60, 60});
    Add(result, L"previous", {30, 55, 48, 48});
    Add(result, L"next", {182, 55, 48, 48});
    Add(result, L"like", {109, 132, 42, 42});
    Add(result, L"disabled", {109, 190, 42, 42}, false);
    Add(result, L"far-down", {300, 210, 42, 42});
    Add(result, L"modal-button", {109, 100, 42, 42}, true, L"modal");

    Check(FindGeometricFocusTarget(L"play", NavigationDirection::Left, result) == L"previous",
          "row navigation chooses previous");
    Check(FindGeometricFocusTarget(L"play", NavigationDirection::Right, result) == L"next",
          "row navigation chooses next");
    Check(FindGeometricFocusTarget(L"play", NavigationDirection::Down, result) == L"like",
          "column navigation chooses like");
    Check(FindGeometricFocusTarget(L"like", NavigationDirection::Down, result) == L"far-down",
          "disabled target is skipped");
    Check(FindGeometricFocusTarget(L"previous", NavigationDirection::Up, result) == std::nullopt,
          "missing direction stays put");
    Check(!gba::input::IsEnabledFocusTarget(L"disabled", result),
          "disabled component is not focusable");
    Check(FindGeometricFocusTarget(L"play", NavigationDirection::Down, result) != L"modal-button",
          "geometric fallback cannot cross nested input scopes");

    std::cout << "FocusNavigationTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
