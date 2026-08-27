#include "TextEntryActionAdmission.h"

#include "TextEntryModal.h"
#include "WidgetSurfaceFocus.h"

namespace widgetrail::input {

std::optional<TextEntryActionRequest> CaptureTextEntryActionRequest(
    const std::wstring_view widgetId,
    const std::wstring_view runtimeGeneration,
    const std::wstring_view presentationGeneration,
    const WidgetSnapshot& snapshot,
    const std::wstring_view nodeId) {
    const auto* node = FindNodeInInputScope(
        snapshot, nodeId, snapshot.activeInputScopeId);
    if (widgetId.empty() || runtimeGeneration.empty() ||
        presentationGeneration.empty() || !node ||
        !node->isTextEntry || node->isDisabled || node->isBusy ||
        node->actionId.empty() || node->textEntryMaximumLength == 0 ||
        node->textEntryMaximumLength > TextEntryModal::MaximumLength ||
        node->textEntryValue.size() > node->textEntryMaximumLength ||
        node->textEntryPlaceholder.size() > TextEntryModal::MaximumLength ||
        (node->textEntryInputKind != L"sensitive" &&
         !node->textEntryInputKind.empty() &&
         node->textEntryInputKind != L"ordinary") ||
        (node->textEntryInputKind == L"sensitive" &&
         !node->textEntryValue.empty())) {
        return std::nullopt;
    }
    return TextEntryActionRequest{
        std::wstring(widgetId),
        std::wstring(runtimeGeneration),
        std::wstring(presentationGeneration),
        snapshot.sequence,
        node->id,
        node->actionId,
        snapshot.activeInputScopeId,
        node->textEntryValue,
        node->textEntryPlaceholder,
        node->textEntryInputKind,
        node->textEntryMaximumLength,
    };
}

std::optional<TextEntryActionTarget> ResolveTextEntryActionTarget(
    const TextEntryActionRequest& request,
    const bool currentInteractiveSurface,
    const std::wstring_view activeWidgetId,
    const std::wstring_view runtimeGeneration,
    const std::wstring_view presentationGeneration,
    const WidgetSnapshot& snapshot) {
    if (!currentInteractiveSurface || activeWidgetId != request.widgetId ||
        runtimeGeneration != request.runtimeGeneration ||
        presentationGeneration != request.presentationGeneration ||
        snapshot.sequence < request.snapshotSequence ||
        snapshot.activeInputScopeId != request.activeInputScopeId) {
        return std::nullopt;
    }
    const auto* node = FindNodeInInputScope(
        snapshot, request.nodeId, snapshot.activeInputScopeId);
    if (!node || !node->isTextEntry || node->isDisabled || node->isBusy ||
        node->id != request.nodeId || node->actionId != request.actionId ||
        node->textEntryMaximumLength != request.maximumLength ||
        node->textEntryValue != request.value ||
        node->textEntryInputKind != request.inputKind) {
        return std::nullopt;
    }
    return TextEntryActionTarget{
        node->actionId,
        node->id,
        snapshot.activeInputScopeId,
    };
}

} // namespace widgetrail::input
