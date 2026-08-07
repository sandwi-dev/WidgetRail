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

enum class Command {
    ToggleOverlay,
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
    [[nodiscard]] Surface surface() const noexcept { return surface_; }
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
    [[nodiscard]] std::size_t FindSlot(std::wstring_view widget) const noexcept;
    [[nodiscard]] bool Contains(std::wstring_view widget) const noexcept;

    PersistentState persistent_;
    Surface surface_{Surface::Hidden};
    std::size_t selectedSlot_{0};
    std::optional<std::wstring> activeWidget_;
    bool reorderMode_{false};
};

} // namespace gba
