#include "OverlayState.h"

#include <algorithm>
#include <unordered_set>
#include <utility>

namespace widgetrail {
namespace {

constexpr std::size_t kMaximumWidgets = 256;

} // namespace

OverlayState::OverlayState(
    PersistentState persisted,
    std::vector<std::wstring> availableWidgetIds)
    : persistent_(std::move(persisted)) {
    Normalize(std::move(availableWidgetIds));
    if (persistent_.lastWidget) selectedSlot_ = FindSlot(*persistent_.lastWidget);
}

OverlayState OverlayState::AwaitingCatalog(PersistentState persisted) {
    OverlayState result({}, {});
    result.persistent_ = std::move(persisted);
    return result;
}

bool OverlayState::Dispatch(const Command command, const bool previewTraySelection) noexcept {
    const auto before = persistent_;
    const auto priorSurface = surface_;
    const auto priorFocusRegion = focusRegion_;
    const auto priorSlot = selectedSlot_;
    const auto priorActive = activeWidget_;
    const auto priorReorder = reorderMode_;
    const auto priorTrayEntry = trayEntry_;

    if (command == Command::ToggleOverlay || command == Command::CloseOverlay) {
        if (surface_ != Surface::Hidden) {
            if (surface_ == Surface::Dashboard && !selectedWidget().empty()) {
                persistent_.lastWidget = std::wstring(selectedWidget());
            }
            if (!availableOrder_.empty())
                persistent_.reopenWidget = surface_ == Surface::Widget &&
                                           persistent_.lastWidget.has_value();
            surface_ = Surface::Hidden;
            reorderMode_ = false;
        } else if (command == Command::CloseOverlay) {
            // A stale or duplicate provider-confirmed effect is a no-op. It
            // must never behave like the Guide toggle and reopen the overlay.
        } else if (persistent_.reopenWidget && persistent_.lastWidget &&
                   Contains(*persistent_.lastWidget)) {
            activeWidget_ = *persistent_.lastWidget;
            selectedSlot_ = FindSlot(*activeWidget_);
            surface_ = Surface::Widget;
        } else {
            surface_ = Surface::Dashboard;
            focusRegion_ = FocusRegion::Tray;
            PresentSelectedWidget(FocusRegion::Tray);
        }
    } else if (surface_ == Surface::Hidden) {
        // Hidden means dormant: no navigation input changes host state.
    } else if ((command == Command::SampleWidgetBack && focusRegion_ == FocusRegion::Widget) ||
               command == Command::FocusTray) {
        if (activeWidget_) selectedSlot_ = FindSlot(*activeWidget_);
        focusRegion_ = FocusRegion::Tray;
        trayEntry_ = command == Command::FocusTray ? TrayEntry::Directional : TrayEntry::Back;
        reorderMode_ = false;
    } else if (surface_ == Surface::Dashboard) {
        switch (command) {
        case Command::NavigateLeft:
            if (reorderMode_) {
                MoveCard(-1);
            } else {
                MoveSelection(-1);
                if (previewTraySelection) PresentSelectedWidget(FocusRegion::Tray);
            }
            break;
        case Command::NavigateRight:
            if (reorderMode_) {
                MoveCard(1);
            } else {
                MoveSelection(1);
                if (previewTraySelection) PresentSelectedWidget(FocusRegion::Tray);
            }
            break;
        case Command::Activate:
            if (reorderMode_) {
                reorderMode_ = false;
            } else {
                PresentSelectedWidget(FocusRegion::Widget);
            }
            break;
        case Command::Cancel:
            reorderMode_ = false;
            break;
        case Command::SampleWidgetBack:
        case Command::FocusTray:
            break;
        case Command::ToggleReorder:
            if (!availableOrder_.empty()) reorderMode_ = !reorderMode_;
            break;
        case Command::ToggleOverlay:
        case Command::CloseOverlay:
            break;
        }
    } else if (focusRegion_ == FocusRegion::Tray) {
        switch (command) {
        case Command::NavigateLeft:
            reorderMode_ ? MoveCard(-1) : MoveSelection(-1);
            if (previewTraySelection) PresentSelectedWidget(FocusRegion::Tray);
            break;
        case Command::NavigateRight:
            reorderMode_ ? MoveCard(1) : MoveSelection(1);
            if (previewTraySelection) PresentSelectedWidget(FocusRegion::Tray);
            break;
        case Command::Activate:
            if (reorderMode_) {
                reorderMode_ = false;
            } else {
                PresentSelectedWidget(FocusRegion::Widget);
            }
            break;
        case Command::Cancel:
            reorderMode_ = false;
            break;
        case Command::ToggleReorder:
            if (!availableOrder_.empty()) reorderMode_ = !reorderMode_;
            break;
        case Command::SampleWidgetBack:
        case Command::FocusTray:
        case Command::ToggleOverlay:
        case Command::CloseOverlay:
            break;
        }
    }

    return before != persistent_ || priorSurface != surface_ ||
           priorFocusRegion != focusRegion_ || priorSlot != selectedSlot_ || priorActive != activeWidget_ ||
           priorReorder != reorderMode_ || priorTrayEntry != trayEntry_;
}

bool OverlayState::SetAvailableWidgets(
    std::vector<std::wstring> availableWidgetIds, const bool isComplete) noexcept {
    const auto before = persistent();
    const auto beforeAvailable = availableOrder_;
    const auto priorSurface = surface_;
    const auto priorSelected = std::wstring(selectedWidget());
    const auto priorActive = activeWidget_;
    Normalize(std::move(availableWidgetIds), isComplete);
    if (!priorSelected.empty() && Contains(priorSelected)) {
        selectedSlot_ = FindSlot(priorSelected);
    } else if (persistent_.lastWidget) {
        selectedSlot_ = FindSlot(*persistent_.lastWidget);
    } else {
        selectedSlot_ = 0;
    }
    if (activeWidget_ && !Contains(*activeWidget_)) {
        activeWidget_.reset();
        if (isComplete) persistent_.reopenWidget = false;
        if (surface_ == Surface::Widget) {
            surface_ = Surface::Dashboard;
            focusRegion_ = FocusRegion::Tray;
        }
    }
    if (priorSelected.empty() && surface_ == Surface::Dashboard)
        PresentSelectedWidget(FocusRegion::Tray);
    return before != persistent_ || beforeAvailable != availableOrder_ || priorSurface != surface_ ||
           priorActive != activeWidget_ || priorSelected != selectedWidget();
}

bool OverlayState::OpenWidgetWithTrayFocus(
    const std::wstring_view widgetId) noexcept {
    if (surface_ != Surface::Hidden || !Contains(widgetId)) return false;
    selectedSlot_ = FindSlot(widgetId);
    PresentSelectedWidget(FocusRegion::Tray);
    return true;
}

bool OverlayState::TrySelectTrayWidget(const std::wstring_view widgetId, const bool preview) noexcept {
    if (surface_ == Surface::Hidden ||
        (surface_ == Surface::Widget && focusRegion_ != FocusRegion::Tray) ||
        reorderMode_) {
        return false;
    }
    const auto found = std::find(availableOrder_.begin(), availableOrder_.end(), widgetId);
    if (found == availableOrder_.end()) return false;
    const auto targetSlot = static_cast<std::size_t>(
        std::distance(availableOrder_.begin(), found));
    if (targetSlot == selectedSlot_) return true;
    selectedSlot_ = targetSlot;
    if (preview) PresentSelectedWidget(FocusRegion::Tray);
    return true;
}

bool OverlayState::ReturnToActiveWidget() noexcept {
    if (surface_ != Surface::Widget || focusRegion_ != FocusRegion::Tray || !activeWidget_) return false;
    selectedSlot_ = FindSlot(*activeWidget_);
    focusRegion_ = FocusRegion::Widget;
    reorderMode_ = false;
    return true;
}

std::wstring_view OverlayState::selectedWidget() const noexcept {
    return availableOrder_.empty() || selectedSlot_ >= availableOrder_.size()
               ? std::wstring_view{}
               : std::wstring_view(availableOrder_[selectedSlot_]);
}

std::wstring_view OverlayState::activeWidget() const noexcept {
    return activeWidget_ ? std::wstring_view(*activeWidget_) : std::wstring_view{};
}

void OverlayState::Normalize(
    std::vector<std::wstring> availableWidgetIds, const bool isComplete) noexcept {
    if (availableWidgetIds.size() > kMaximumWidgets) availableWidgetIds.resize(kMaximumWidgets);
    std::vector<std::wstring> available;
    std::unordered_set<std::wstring> seen;
    for (auto& id : availableWidgetIds) {
        if (id.empty() || !seen.insert(id).second) continue;
        available.push_back(std::move(id));
    }

    std::unordered_set<std::wstring> allowed(available.begin(), available.end());
    std::vector<std::wstring> normalized;
    normalized.reserve(available.size());
    seen.clear();
    for (const auto& id : persistent_.order) {
        if (normalized.size() == kMaximumWidgets) break;
        if ((!isComplete || allowed.contains(id)) && !id.empty() && seen.insert(id).second)
            normalized.push_back(id);
    }
    for (const auto& id : available) {
        if (normalized.size() == kMaximumWidgets) break;
        if (seen.insert(id).second) normalized.push_back(id);
    }
    persistent_.order = std::move(normalized);
    availableOrder_.clear();
    for (const auto& id : persistent_.order)
        if (allowed.contains(id)) availableOrder_.push_back(id);
    // At the saved-state bound, keep newly discovered entries usable even if
    // their positions cannot be persisted until complete discovery frees slots.
    for (const auto& id : available)
        if (std::find(availableOrder_.begin(), availableOrder_.end(), id) == availableOrder_.end())
            availableOrder_.push_back(id);
    if (isComplete && persistent_.lastWidget && !allowed.contains(*persistent_.lastWidget)) {
        persistent_.lastWidget.reset();
        persistent_.reopenWidget = false;
    }
    if (!persistent_.lastWidget) persistent_.reopenWidget = false;
    if (selectedSlot_ >= availableOrder_.size()) selectedSlot_ = 0;
}

void OverlayState::MoveSelection(const int delta) noexcept {
    if (availableOrder_.empty()) return;
    const auto count = static_cast<long long>(availableOrder_.size());
    const auto current = static_cast<long long>(selectedSlot_);
    selectedSlot_ = static_cast<std::size_t>((current + delta + count) % count);
}

void OverlayState::MoveCard(const int delta) noexcept {
    if (availableOrder_.empty()) return;
    const auto next = static_cast<long long>(selectedSlot_) + delta;
    if (next < 0 || next >= static_cast<long long>(availableOrder_.size())) return;
    // Swap the visible entries' saved slots, leaving undiscovered entries in
    // place. The visible slot numbers need not match the saved slot numbers.
    const auto currentSaved = std::find(
        persistent_.order.begin(), persistent_.order.end(), availableOrder_[selectedSlot_]);
    const auto nextSaved = std::find(
        persistent_.order.begin(), persistent_.order.end(), availableOrder_[static_cast<std::size_t>(next)]);
    if (currentSaved != persistent_.order.end() && nextSaved != persistent_.order.end())
        std::iter_swap(currentSaved, nextSaved);
    std::swap(availableOrder_[selectedSlot_], availableOrder_[static_cast<std::size_t>(next)]);
    selectedSlot_ = static_cast<std::size_t>(next);
}

void OverlayState::PresentSelectedWidget(const FocusRegion focusRegion) noexcept {
    if (selectedWidget().empty()) return;
    activeWidget_ = std::wstring(selectedWidget());
    persistent_.lastWidget = activeWidget_;
    persistent_.reopenWidget = true;
    surface_ = Surface::Widget;
    focusRegion_ = focusRegion;
}

std::size_t OverlayState::FindSlot(const std::wstring_view widget) const noexcept {
    const auto found = std::find(availableOrder_.begin(), availableOrder_.end(), widget);
    return found == availableOrder_.end()
               ? 0U
               : static_cast<std::size_t>(std::distance(availableOrder_.begin(), found));
}

bool OverlayState::Contains(const std::wstring_view widget) const noexcept {
    return std::find(availableOrder_.begin(), availableOrder_.end(), widget) !=
           availableOrder_.end();
}

} // namespace widgetrail
