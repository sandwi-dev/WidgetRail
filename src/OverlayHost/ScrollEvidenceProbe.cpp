#include "ScrollEvidenceProbe.h"

#include "DeclarativeRenderer.h"
#include "WidgetBridgeClient.h"

#include <Windows.h>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <limits>
#include <string>

namespace gba {
namespace {

constexpr std::size_t kMaximumPathCharacters = 32'767;
constexpr std::size_t kMaximumFieldCharacters = 4'096;
constexpr std::size_t kMaximumDirectionCharacters = 64;
constexpr std::size_t kMaximumScrollOffsets = 4'096;
constexpr std::size_t kMaximumPayloadBytes = 64 * 1'024;

[[nodiscard]] bool SafeField(
    const std::wstring_view value,
    const std::size_t maximumCharacters,
    const bool allowEmpty = false) noexcept {
    return (allowEmpty || !value.empty()) && value.size() <= maximumCharacters &&
        std::none_of(value.begin(), value.end(), [](const wchar_t character) {
            return character == L'\0' || character == L'\r' || character == L'\n';
        });
}

[[nodiscard]] bool FiniteRect(const declarative::Rect& rect) noexcept {
    return std::isfinite(rect.x) && std::isfinite(rect.y) &&
        std::isfinite(rect.width) && std::isfinite(rect.height) &&
        rect.width >= 0.0F && rect.height >= 0.0F;
}

[[nodiscard]] const WidgetNode* FindNode(
    const WidgetNode& node,
    const std::wstring_view id) noexcept {
    if (node.id == id) return &node;
    for (const auto& child : node.children) {
        if (const auto* found = FindNode(child, id)) return found;
    }
    return nullptr;
}

void AppendRect(
    std::wstring& payload,
    const std::wstring_view key,
    const declarative::Rect& rect) {
    payload += key;
    payload += L"=" + std::to_wstring(rect.x) + L"," +
        std::to_wstring(rect.y) + L"," + std::to_wstring(rect.width) + L"," +
        std::to_wstring(rect.height) + L"\n";
}

[[nodiscard]] bool EncodeUtf8(
    const std::wstring_view payload,
    std::string& encoded) {
    if (payload.empty() || payload.size() > kMaximumPayloadBytes ||
        payload.size() > static_cast<std::size_t>(std::numeric_limits<int>::max())) {
        return false;
    }
    const int length = WideCharToMultiByte(
        CP_UTF8, WC_ERR_INVALID_CHARS, payload.data(),
        static_cast<int>(payload.size()), nullptr, 0, nullptr, nullptr);
    if (length <= 0 || static_cast<std::size_t>(length) > kMaximumPayloadBytes)
        return false;
    encoded.assign(static_cast<std::size_t>(length), '\0');
    return WideCharToMultiByte(
               CP_UTF8, WC_ERR_INVALID_CHARS, payload.data(),
               static_cast<int>(payload.size()), encoded.data(), length,
               nullptr, nullptr) == length;
}

[[nodiscard]] bool PublishAtomically(
    const std::filesystem::path& destination,
    const std::string_view payload) {
    if (payload.empty() || payload.size() > kMaximumPayloadBytes ||
        payload.size() > MAXDWORD) return false;
    const auto temporary = destination.wstring() + L".tmp-" +
        std::to_wstring(GetCurrentProcessId());
    DeleteFileW(temporary.c_str());
    HANDLE file = CreateFileW(
        temporary.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_NEW,
        FILE_ATTRIBUTE_TEMPORARY, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    DWORD written{};
    const bool wrote = WriteFile(
                           file, payload.data(), static_cast<DWORD>(payload.size()),
                           &written, nullptr) &&
        written == static_cast<DWORD>(payload.size()) && FlushFileBuffers(file);
    CloseHandle(file);
    if (wrote && MoveFileExW(
                     temporary.c_str(), destination.c_str(),
                     MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        return true;
    }
    DeleteFileW(temporary.c_str());
    return false;
}

} // namespace

bool ScrollEvidenceProbe::Enable(const std::filesystem::path& destination) {
    const auto native = destination.native();
    if (destination.empty() || !destination.is_absolute() ||
        native.size() > kMaximumPathCharacters) {
        destination_.clear();
        explicitTarget_.clear();
        direction_.clear();
        return false;
    }
    destination_ = destination;
    explicitTarget_.clear();
    direction_.clear();
    return true;
}

bool ScrollEvidenceProbe::RecordTarget(
    const std::wstring_view target,
    const std::wstring_view direction) {
    if (!enabled() || !SafeField(target, kMaximumFieldCharacters) ||
        !SafeField(direction, kMaximumDirectionCharacters)) {
        return false;
    }
    explicitTarget_ = target;
    direction_ = direction;
    return true;
}

ScrollEvidencePublishResult ScrollEvidenceProbe::Publish(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot,
    const RenderResult& result,
    const std::wstring_view renderedFocusId,
    const float pixelScale,
    const float textScale) const {
    if (!enabled()) return ScrollEvidencePublishResult::Disabled;
    const auto navigation = result.navigationRects.find(renderedFocusId);
    const auto presentation = result.focusRects.find(renderedFocusId);
    const auto* focusedNode = FindNode(snapshot.root, renderedFocusId);
    const auto upTarget = focusedNode
        ? std::wstring_view(focusedNode->focusUp) : std::wstring_view{};
    const auto upNavigation = result.navigationRects.find(upTarget);
    const auto target = explicitTarget_.empty()
        ? renderedFocusId : std::wstring_view(explicitTarget_);
    if (!SafeField(widgetId, kMaximumFieldCharacters) ||
        !SafeField(snapshot.instanceId, kMaximumFieldCharacters) ||
        !SafeField(snapshot.activeInputScopeId, kMaximumFieldCharacters) ||
        !SafeField(renderedFocusId, kMaximumFieldCharacters) ||
        !SafeField(target, kMaximumFieldCharacters) ||
        !SafeField(direction_, kMaximumDirectionCharacters, true) ||
        snapshot.sequence < 0 || !std::isfinite(pixelScale) || pixelScale <= 0.0F ||
        !std::isfinite(textScale) || textScale <= 0.0F ||
        navigation == result.navigationRects.end() ||
        presentation == result.focusRects.end() ||
        !FiniteRect(navigation->second) || !FiniteRect(presentation->second) ||
        result.scrollOffsets.size() > kMaximumScrollOffsets) {
        return ScrollEvidencePublishResult::InvalidFrame;
    }
    for (const auto& [id, offset] : result.scrollOffsets) {
        if (!SafeField(id, kMaximumFieldCharacters) || !std::isfinite(offset))
            return ScrollEvidencePublishResult::InvalidFrame;
    }

    std::wstring payload =
        L"gbar-scroll-evidence-v1\nwidget=" + std::wstring(widgetId) +
        L"\ninstance=" + snapshot.instanceId +
        L"\nsequence=" + std::to_wstring(snapshot.sequence) +
        L"\nscope=" + snapshot.activeInputScopeId +
        L"\nfocus=" + std::wstring(renderedFocusId) +
        L"\nexplicitTarget=" + std::wstring(target) +
        L"\ndirection=" + direction_ +
        L"\npixelScale=" + std::to_wstring(pixelScale) +
        L"\ntextScale=" + std::to_wstring(textScale) +
        L"\nrevealable=" +
            (result.revealableFocusIds.contains(renderedFocusId) ? L"true" : L"false") +
        L"\n";
    AppendRect(payload, L"navigation", navigation->second);
    AppendRect(payload, L"presentation", presentation->second);
    if (!upTarget.empty() && SafeField(upTarget, kMaximumFieldCharacters) &&
        upNavigation != result.navigationRects.end() &&
        FiniteRect(upNavigation->second)) {
        payload += L"upTarget=" + std::wstring(upTarget) + L"\nupRevealable=" +
            (result.revealableFocusIds.contains(upTarget) ? L"true" : L"false") + L"\n";
        AppendRect(payload, L"upNavigation", upNavigation->second);
    }
    for (const auto& [id, offset] : result.scrollOffsets)
        payload += L"scroll=" + id + L"," + std::to_wstring(offset) + L"\n";

    std::string encoded;
    if (!EncodeUtf8(payload, encoded))
        return ScrollEvidencePublishResult::InvalidFrame;
    return PublishAtomically(destination_, encoded)
        ? ScrollEvidencePublishResult::Published
        : ScrollEvidencePublishResult::UnavailablePath;
}

} // namespace gba
