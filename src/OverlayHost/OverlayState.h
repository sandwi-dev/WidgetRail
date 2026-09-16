#pragma once

#include <cstddef>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

namespace widgetrail {

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

// Back may use the configured radial chooser. Directional exit always enters
// the ordinary rail, where selection previews the widget and admits shortcuts.
enum class TrayEntry { Back, Directional };

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
    FocusTray,
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

    /// Retains saved preferences without admitting any widget until the first
    /// authoritative catalog arrives. An empty startup catalog is not deletion.
    [[nodiscard]] static OverlayState AwaitingCatalog(PersistentState persisted);

    [[nodiscard]] bool Dispatch(Command command, bool previewTraySelection = true) noexcept;
    /// Reconciles persisted presentation state with a newly discovered catalog.
    /// Existing relative order is preserved and new IDs are appended.
    [[nodiscard]] bool SetAvailableWidgets(
        std::vector<std::wstring> availableWidgetIds) noexcept;
    /// Opens one catalog-admitted widget directly from Hidden while retaining
    /// the tray as the sole input focus owner.
    [[nodiscard]] bool OpenWidgetWithTrayFocus(
        std::wstring_view widgetId) noexcept;
    /// Selects a tray item by stable widget ID without replaying directional input.
    /// Returns false when the ID is unavailable or the tray does not own input.
    [[nodiscard]] bool TrySelectTrayWidget(std::wstring_view widgetId, bool preview = true) noexcept;
    [[nodiscard]] bool ReturnToActiveWidget() noexcept;
    [[nodiscard]] TrayEntry trayEntry() const noexcept { return trayEntry_; }
    [[nodiscard]] bool radialTrayOpen(bool enabled) const noexcept {
        return enabled && surface_ != Surface::Hidden && focusRegion_ == FocusRegion::Tray &&
            trayEntry_ == TrayEntry::Back;
    }
    [[nodiscard]] Surface surface() const noexcept { return surface_; }
    [[nodiscard]] FocusRegion focusRegion() const noexcept { return focusRegion_; }
    [[nodiscard]] std::size_t selectedSlot() const noexcept { return selectedSlot_; }
    [[nodiscard]] std::wstring_view selectedWidget() const noexcept;
    [[nodiscard]] std::wstring_view activeWidget() const noexcept;
    [[nodiscard]] bool reorderMode() const noexcept { return reorderMode_; }
    [[nodiscard]] const std::vector<std::wstring>& order() const noexcept { return persistent_.order; }
    [[nodiscard]] const PersistentState& persistent() const noexcept {
        return pendingPersistent_ ? *pendingPersistent_ : persistent_;
    }

private:
    void Normalize(std::vector<std::wstring> availableWidgetIds) noexcept;
    void MoveSelection(int delta) noexcept;
    void MoveCard(int delta) noexcept;
    void PresentSelectedWidget(FocusRegion focusRegion) noexcept;
    [[nodiscard]] std::size_t FindSlot(std::wstring_view widget) const noexcept;
    [[nodiscard]] bool Contains(std::wstring_view widget) const noexcept;

    PersistentState persistent_;
    std::optional<PersistentState> pendingPersistent_;
    Surface surface_{Surface::Hidden};
    // Widget is the compatibility default for a persisted reopen. A first-run
    // dashboard explicitly switches this to Tray when it becomes visible.
    FocusRegion focusRegion_{FocusRegion::Widget};
    TrayEntry trayEntry_{TrayEntry::Back};
    std::size_t selectedSlot_{0};
    std::optional<std::wstring> activeWidget_;
    bool reorderMode_{false};
};

} // namespace widgetrail
