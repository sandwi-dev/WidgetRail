#pragma once

#include <cstddef>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace gba {

enum class Surface {
    Hidden,
    Dashboard,
    Widget,
};

// Presentation and input ownership are intentionally independent. A widget
// surface can remain visible while controller focus is parked on the tray.
enum class FocusRegion {
    Tray,
    Widget,
};

enum class Command {
    ToggleOverlay,
    /// Idempotently hides a visible overlay. Unlike ToggleOverlay, this can
    /// never reopen a surface when a delayed trusted host effect arrives.
    CloseOverlay,
    NavigateLeft,
    NavigateRight,
    Activate,
    Cancel,
    SampleWidgetBack,
    ToggleReorder,
};

struct PersistentState {
    std::vector<std::wstring> order;
    std::optional<std::wstring> lastWidget;
    bool reopenWidget{false};
    friend bool operator==(const PersistentState&, const PersistentState&) = default;
};

class OverlayState final {
public:
    explicit OverlayState(
        PersistentState persisted = {},
        std::vector<std::wstring> availableWidgetIds = {
            L"audio-mixer", L"yt-music", L"performance"});

    [[nodiscard]] bool Dispatch(Command command) noexcept;
    /// Reconciles persisted presentation state with a newly discovered catalog.
    /// Existing relative order is preserved and new IDs are appended.
    [[nodiscard]] bool SetAvailableWidgets(
        std::vector<std::wstring> availableWidgetIds) noexcept;
    /// Selects a tray item by stable widget ID without replaying directional input.
    /// Returns false when the ID is unavailable or the tray does not own input.
    [[nodiscard]] bool TrySelectTrayWidget(std::wstring_view widgetId) noexcept;
    [[nodiscard]] Surface surface() const noexcept { return surface_; }
    [[nodiscard]] FocusRegion focusRegion() const noexcept { return focusRegion_; }
    [[nodiscard]] std::size_t selectedSlot() const noexcept { return selectedSlot_; }
    [[nodiscard]] std::wstring_view selectedWidget() const noexcept;
    [[nodiscard]] std::wstring_view activeWidget() const noexcept;
    [[nodiscard]] bool reorderMode() const noexcept { return reorderMode_; }
    [[nodiscard]] const std::vector<std::wstring>& order() const noexcept { return persistent_.order; }
    [[nodiscard]] const PersistentState& persistent() const noexcept { return persistent_; }

private:
    void Normalize(std::vector<std::wstring> availableWidgetIds) noexcept;
    void MoveSelection(int delta) noexcept;
    void MoveCard(int delta) noexcept;
    void PresentSelectedWidget(FocusRegion focusRegion) noexcept;
    [[nodiscard]] std::size_t FindSlot(std::wstring_view widget) const noexcept;
    [[nodiscard]] bool Contains(std::wstring_view widget) const noexcept;

    PersistentState persistent_;
    Surface surface_{Surface::Hidden};
    // Widget is the compatibility default for a persisted reopen. A first-run
    // dashboard explicitly switches this to Tray when it becomes visible.
    FocusRegion focusRegion_{FocusRegion::Widget};
    std::size_t selectedSlot_{0};
    std::optional<std::wstring> activeWidget_;
    bool reorderMode_{false};
};

} // namespace gba
