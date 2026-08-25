#include "WidgetBridgeClient.h"
#include "WidgetSurfaceGeometry.h"
#include "WidgetProtocolPresentationContract.generated.h"

#include <winrt/base.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Data.Json.h>

#include <algorithm>
#include <array>
#include <chrono>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <initializer_list>
#include <cmath>
#include <cwctype>
#include <limits>
#include <unordered_set>
#include <thread>
#include <utility>

namespace widgetrail {

const WidgetDescriptorQuickAction* FindDescriptorQuickAction(
    const WidgetDescriptor& descriptor,
    const std::wstring_view quickActionId) noexcept {
    const auto found = std::find_if(
        descriptor.quickActions.begin(), descriptor.quickActions.end(),
        [&](const WidgetDescriptorQuickAction& action) {
            return action.id == quickActionId;
        });
    return found == descriptor.quickActions.end() ? nullptr : &*found;
}

ProtectedWifiSecretFrame::ProtectedWifiSecretFrame(
    std::vector<unsigned char>&& bytes) noexcept : bytes_(std::move(bytes)) {}

ProtectedWifiSecretFrame::~ProtectedWifiSecretFrame() { clear(); }

ProtectedWifiSecretFrame::ProtectedWifiSecretFrame(
    ProtectedWifiSecretFrame&& other) noexcept : bytes_(std::move(other.bytes_)) {
    other.clear();
}

ProtectedWifiSecretFrame& ProtectedWifiSecretFrame::operator=(
    ProtectedWifiSecretFrame&& other) noexcept {
    if (this != &other) {
        clear();
        bytes_ = std::move(other.bytes_);
        other.clear();
    }
    return *this;
}

std::optional<ProtectedWifiSecretFrame> ProtectedWifiSecretFrame::Create(
    const std::span<const wchar_t> secret) {
    if (secret.size() < 8 || secret.size() > 63) return std::nullopt;
    std::vector<unsigned char> bytes(secret.size());
    for (std::size_t index = 0; index < secret.size(); ++index) {
        if (secret[index] < 32 || secret[index] > 126) {
            if (!bytes.empty()) SecureZeroMemory(bytes.data(), bytes.size());
            return std::nullopt;
        }
        bytes[index] = static_cast<unsigned char>(secret[index]);
    }
    return ProtectedWifiSecretFrame(std::move(bytes));
}

void ProtectedWifiSecretFrame::clear() noexcept {
    if (!bytes_.empty()) SecureZeroMemory(bytes_.data(), bytes_.size());
    bytes_.clear();
}

namespace {

using winrt::Windows::Data::Json::JsonArray;
using winrt::Windows::Data::Json::JsonObject;
using winrt::Windows::Data::Json::JsonValue;
using winrt::Windows::Data::Json::JsonValueType;

constexpr DWORD kMaximumFrameBytes = 1024 * 1024;
constexpr uint32_t kMaximumWidgetDescriptors = 256;
constexpr uint32_t kMaximumDescriptorQuickActions = 16;
constexpr std::size_t kMaximumIdentifierLength =
    protocol_contract::MaximumCapabilityIdLength;
constexpr std::size_t kMaximumLabelLength = 256;
constexpr std::size_t kMaximumControllerButtonLength = 32;
constexpr int kMinimumWidgetSnapshotProtocolVersion =
    protocol_contract::MinimumSupportedVersion;
constexpr int kMaximumWidgetSnapshotProtocolVersion = protocol_contract::CurrentVersion;
constexpr std::size_t kMaximumPinnedLayoutCount =
    protocol_contract::MaximumPinnedPresentationLayoutCount;
constexpr std::size_t kMaximumPinnedProjectionAggregateNodes =
    protocol_contract::MaximumPinnedPresentationAggregateNodeCount;
constexpr std::size_t kMaximumPinnedProjectionAggregateCharacters =
    protocol_contract::MaximumPinnedPresentationAggregateStringLength;
constexpr std::size_t kMaximumPinnedProjectionAggregateResources =
    protocol_contract::MaximumPinnedPresentationAggregateResourceCount;
constexpr int kAtomicPresentationUpdateVersion =
    protocol_contract::AtomicPresentationUpdateVersion;
constexpr std::size_t kMaximumPresentationUpdateOperations =
    protocol_contract::MaximumPresentationUpdateOperations;
constexpr std::size_t kMaximumPresentationUpdateBytes =
    protocol_contract::MaximumPresentationUpdateBytes;
constexpr std::size_t kMaximumWidgetNodes = protocol_contract::MaximumNodeCount;
constexpr std::size_t kMaximumWidgetTreeDepth = protocol_contract::MaximumTreeDepth;
constexpr std::uint64_t kMaximumVirtualCollectionItems =
    protocol_contract::MaximumVirtualCollectionItems;
constexpr std::uint64_t kMaximumVirtualCollectionRequestGeneration =
    protocol_contract::MaximumVirtualCollectionRequestGeneration;
constexpr double kMinimumVirtualCollectionItemExtent =
    protocol_contract::MinimumVirtualCollectionItemExtent;
constexpr double kMaximumVirtualCollectionItemExtent =
    protocol_contract::MaximumVirtualCollectionItemExtent;
constexpr double kMaximumVirtualCollectionExtent =
    protocol_contract::MaximumVirtualCollectionExtent;

constexpr uint32_t kMaximumShellStyles = 12;
constexpr uint32_t kMaximumShellProperties = 64;
constexpr std::size_t kMaximumStyleValueTextLength = 4096;
constexpr std::size_t kMaximumStyleUnitLength = 16;

constexpr std::array<std::wstring_view, 12> kShellStyleKeys{
    L"canvas", L"backdrop", L"panel", L"tray", L"tray-item",
    L"tray-item:selected", L"tray-item:focused", L"tray-item:selected:focused",
    L"title", L"body", L"hint", L"status"};

bool HasOnlyProperties(
    const JsonObject& object,
    const std::initializer_list<std::wstring_view> expected) {
    if (object.Size() != expected.size()) return false;
    return std::all_of(expected.begin(), expected.end(), [&](const std::wstring_view property) {
        return object.HasKey(winrt::hstring(property));
    });
}

bool HasNoUnknownProperties(
    const JsonObject& object,
    const std::initializer_list<std::wstring_view> allowed) {
    return std::all_of(object.begin(), object.end(), [&](const auto& pair) {
        const std::wstring_view key(pair.Key());
        return std::find(allowed.begin(), allowed.end(), key) != allowed.end();
    });
}

std::optional<std::vector<std::uint8_t>> DecodeBase64Bounded(
    const std::wstring_view source,
    const std::size_t maximumBytes) {
    if (source.empty() || source.size() % 4 != 0 ||
        source.size() > ((maximumBytes + 2) / 3) * 4) return std::nullopt;
    const auto decode = [](const wchar_t value) noexcept -> int {
        if (value >= L'A' && value <= L'Z') return value - L'A';
        if (value >= L'a' && value <= L'z') return 26 + value - L'a';
        if (value >= L'0' && value <= L'9') return 52 + value - L'0';
        if (value == L'+') return 62;
        if (value == L'/') return 63;
        return -1;
    };
    std::vector<std::uint8_t> result;
    result.reserve((source.size() / 4) * 3);
    for (std::size_t offset = 0; offset < source.size(); offset += 4) {
        const int a = decode(source[offset]);
        const int b = decode(source[offset + 1]);
        const bool padC = source[offset + 2] == L'=';
        const bool padD = source[offset + 3] == L'=';
        const int c = padC ? 0 : decode(source[offset + 2]);
        const int d = padD ? 0 : decode(source[offset + 3]);
        if (a < 0 || b < 0 || c < 0 || d < 0 || padC && !padD ||
            (padC || padD) && offset + 4 != source.size()) return std::nullopt;
        const std::uint32_t value = static_cast<std::uint32_t>(
            (a << 18) | (b << 12) | (c << 6) | d);
        result.push_back(static_cast<std::uint8_t>(value >> 16));
        if (!padC) result.push_back(static_cast<std::uint8_t>(value >> 8));
        if (!padD) result.push_back(static_cast<std::uint8_t>(value));
        if (result.size() > maximumBytes) return std::nullopt;
    }
    return result;
}

bool IsComputedValueKind(const std::wstring_view value) noexcept {
    static constexpr std::array<std::wstring_view, 9> kinds{
        L"color", L"length", L"lengthList", L"number", L"integer", L"ratio",
        L"duration", L"keyword", L"fontFamily"};
    return std::find(kinds.begin(), kinds.end(), value) != kinds.end();
}

bool IsCanonicalThemeVersion(const std::wstring_view value) noexcept {
    if (value.empty() || value.size() > 64) return false;
    std::size_t segments = 0;
    std::size_t start = 0;
    while (start < value.size()) {
        const auto end = value.find(L'.', start);
        const auto length = (end == std::wstring_view::npos ? value.size() : end) - start;
        if (length == 0 || length > 10 ||
            (length > 1 && value[start] == L'0') ||
            !std::all_of(value.begin() + static_cast<std::ptrdiff_t>(start),
                         value.begin() + static_cast<std::ptrdiff_t>(start + length),
                         [](const wchar_t character) { return character >= L'0' && character <= L'9'; })) {
            return false;
        }
        ++segments;
        if (end == std::wstring_view::npos) break;
        start = end + 1;
    }
    return segments >= 2 && segments <= 4;
}

bool IsWidgetGlyph(const std::wstring_view value) noexcept {
    static constexpr std::array<std::wstring_view, 20> glyphs{
        L"music", L"play", L"pause", L"previous", L"next", L"refresh", L"shuffle",
        L"like", L"dislike", L"repeat", L"repeatOne", L"settings", L"warning", L"check", L"connection",
        L"volume", L"muted", L"microphone", L"wifi", L"ethernet"};
    return std::find(glyphs.begin(), glyphs.end(), value) != glyphs.end();
}

std::wstring Quote(const std::filesystem::path& path) {
    return L"\"" + path.wstring() + L"\"";
}

std::wstring Win32Message(const std::wstring_view operation, const DWORD error) {
    return std::wstring(operation) + L" failed with Win32 error " + std::to_wstring(error);
}

struct ExactReadResult final {
    DWORD completed{};
    DWORD error{};
};

ExactReadResult ReadExact(const HANDLE pipe, void* destination, const DWORD length) {
    auto* output = static_cast<std::byte*>(destination);
    DWORD completed = 0;
    while (completed < length) {
        DWORD count = 0;
        if (!ReadFile(pipe, output + completed, length - completed, &count, nullptr) || count == 0) {
            return {completed, GetLastError()};
        }
        completed += count;
    }
    return {completed, ERROR_SUCCESS};
}

struct FrameReadResult final {
    std::optional<std::string> frame;
    bool transportTainted{};
    DWORD error{};
};

FrameReadResult ReadFrameFromPipe(const HANDLE pipe) {
    std::int32_t length = 0;
    const auto header = ReadExact(pipe, &length, sizeof(length));
    if (header.completed != sizeof(length))
        return {std::nullopt, true, header.error};
    if (length <= 0 || static_cast<DWORD>(length) > kMaximumFrameBytes)
        return {std::nullopt, true, ERROR_INVALID_DATA};
    std::string body(static_cast<std::size_t>(length), '\0');
    const auto payload = ReadExact(pipe, body.data(), static_cast<DWORD>(length));
    if (payload.completed != static_cast<DWORD>(length))
        return {std::nullopt, true, payload.error};
    return {std::move(body), false, ERROR_SUCCESS};
}

bool WriteExact(const HANDLE pipe, const void* source, const DWORD length) {
    const auto* input = static_cast<const std::byte*>(source);
    DWORD completed = 0;
    while (completed < length) {
        DWORD count = 0;
        if (!WriteFile(pipe, input + completed, length - completed, &count, nullptr) || count == 0) {
            return false;
        }
        completed += count;
    }
    return true;
}

std::wstring OptionalString(const JsonObject& object, const wchar_t* name) {
    if (!object.HasKey(name) ||
        object.GetNamedValue(name).ValueType() != JsonValueType::String) {
        return {};
    }
    return std::wstring(std::wstring_view(object.GetNamedString(name)));
}

std::wstring_view PresentationTransactionKindName(
    const WidgetPresentationTransactionKind kind) noexcept {
    switch (kind) {
    case WidgetPresentationTransactionKind::IncrementalUpdate:
        return L"incrementalUpdate";
    case WidgetPresentationTransactionKind::OrdinaryCheckpoint:
        return L"ordinaryCheckpoint";
    case WidgetPresentationTransactionKind::RecoveryCheckpoint:
        return L"recoveryCheckpoint";
    }
    return {};
}

std::optional<WidgetPresentationTransactionKind> ParsePresentationTransactionKind(
    const JsonObject& object) {
    const auto value = OptionalString(object, L"transactionKind");
    if (value == L"incrementalUpdate")
        return WidgetPresentationTransactionKind::IncrementalUpdate;
    if (value == L"ordinaryCheckpoint")
        return WidgetPresentationTransactionKind::OrdinaryCheckpoint;
    if (value == L"recoveryCheckpoint")
        return WidgetPresentationTransactionKind::RecoveryCheckpoint;
    return std::nullopt;
}

bool ValidPresentationTransaction(
    const WidgetPresentationTransactionKind kind,
    const long long baseSequence,
    const long long recoveryOriginSequence) noexcept {
    switch (kind) {
    case WidgetPresentationTransactionKind::IncrementalUpdate:
        return baseSequence > 0 && recoveryOriginSequence == 0;
    case WidgetPresentationTransactionKind::OrdinaryCheckpoint:
        return baseSequence == 0 && recoveryOriginSequence == 0;
    case WidgetPresentationTransactionKind::RecoveryCheckpoint:
        return baseSequence == 0 && recoveryOriginSequence > 0;
    }
    return false;
}

bool IsIdentifier(const std::wstring_view value) {
    return !value.empty() && value.size() <= kMaximumIdentifierLength &&
           std::all_of(value.begin(), value.end(), [](const wchar_t character) {
               return (character >= L'a' && character <= L'z') ||
                      (character >= L'A' && character <= L'Z') ||
                      (character >= L'0' && character <= L'9') ||
                      character == L'-' || character == L'_' || character == L'.';
           });
}

bool IsLabel(const std::wstring_view value) {
    return !value.empty() && value.size() <= kMaximumLabelLength &&
           !std::all_of(value.begin(), value.end(), [](const wchar_t character) {
               return std::iswspace(character) != 0;
           }) &&
           std::none_of(value.begin(), value.end(), [](const wchar_t character) {
               return std::iswcntrl(character) != 0;
           });
}

bool ReadDescriptorString(
    const JsonObject& source,
    const wchar_t* property,
    std::wstring& destination,
    const bool identifier,
    std::wstring& error) {
    if (!source.HasKey(property) ||
        source.GetNamedValue(property).ValueType() != JsonValueType::String) {
        error = std::wstring(L"Widget descriptor property '") + property + L"' must be a string.";
        return false;
    }
    destination = std::wstring(std::wstring_view(source.GetNamedString(property)));
    if (identifier ? !IsIdentifier(destination) : !IsLabel(destination)) {
        error = std::wstring(L"Widget descriptor property '") + property + L"' is invalid.";
        return false;
    }
    return true;
}

std::optional<std::vector<WidgetDescriptor>> ParseWidgetDescriptors(
    const JsonObject& payload,
    std::wstring& error) {
    if (!payload.HasKey(L"widgets") ||
        payload.GetNamedValue(L"widgets").ValueType() != JsonValueType::Array) {
        error = L"WidgetBridge widgets payload is missing the widgets array.";
        return std::nullopt;
    }
    const auto widgets = payload.GetNamedArray(L"widgets");
    if (widgets.Size() > kMaximumWidgetDescriptors) {
        error = L"WidgetBridge returned more than 256 widget descriptors.";
        return std::nullopt;
    }

    std::vector<WidgetDescriptor> result;
    result.reserve(widgets.Size());
    std::unordered_set<std::wstring> widgetIds;
    for (uint32_t widgetIndex = 0; widgetIndex < widgets.Size(); ++widgetIndex) {
        if (widgets.GetAt(widgetIndex).ValueType() != JsonValueType::Object) {
            error = L"WidgetBridge returned a non-object widget descriptor.";
            return std::nullopt;
        }
        const auto source = widgets.GetObjectAt(widgetIndex);
        WidgetDescriptor descriptor;
        if (!ReadDescriptorString(source, L"id", descriptor.id, true, error) ||
            !ReadDescriptorString(source, L"name", descriptor.name, false, error) ||
            !ReadDescriptorString(source, L"instanceId", descriptor.instanceId, true, error) ||
            !ReadDescriptorString(source, L"runtimeGeneration", descriptor.runtimeGeneration, true, error) ||
            !ReadDescriptorString(source, L"presentationGeneration", descriptor.presentationGeneration, true, error)) {
            return std::nullopt;
        }
        if (source.HasKey(L"icon")) {
            if (source.GetNamedValue(L"icon").ValueType() != JsonValueType::String) {
                error = L"Widget descriptor property 'icon' must be a string.";
                return std::nullopt;
            }
            descriptor.icon = std::wstring(std::wstring_view(source.GetNamedString(L"icon")));
            if (!IsWidgetGlyph(descriptor.icon)) {
                error = L"Widget descriptor property 'icon' is not a supported WidgetGlyph.";
                return std::nullopt;
            }
        }
        if (source.HasKey(L"pinningSupported")) {
            if (source.GetNamedValue(L"pinningSupported").ValueType() !=
                JsonValueType::Boolean) {
                error = L"Widget descriptor property 'pinningSupported' must be a boolean.";
                return std::nullopt;
            }
            descriptor.pinningSupported = source.GetNamedBoolean(L"pinningSupported");
        }
        if (source.HasKey(L"protectedWifiPromptSupported")) {
            if (source.GetNamedValue(L"protectedWifiPromptSupported").ValueType() !=
                JsonValueType::Boolean) {
                error = L"Widget descriptor property 'protectedWifiPromptSupported' must be a boolean.";
                return std::nullopt;
            }
            descriptor.protectedWifiPromptSupported =
                source.GetNamedBoolean(L"protectedWifiPromptSupported");
        }
        if (!widgetIds.emplace(descriptor.id).second) {
            error = L"WidgetBridge returned duplicate widget ID '" + descriptor.id + L"'.";
            return std::nullopt;
        }
        if (!source.HasKey(L"quickActions") ||
            source.GetNamedValue(L"quickActions").ValueType() != JsonValueType::Array) {
            error = L"Widget descriptor quickActions must be an array.";
            return std::nullopt;
        }
        const auto actions = source.GetNamedArray(L"quickActions");
        if (actions.Size() > kMaximumDescriptorQuickActions) {
            error = L"Widget descriptor contains more than 16 quick actions.";
            return std::nullopt;
        }
        descriptor.quickActions.reserve(actions.Size());
        std::unordered_set<std::wstring> quickActionIds;
        for (uint32_t actionIndex = 0; actionIndex < actions.Size(); ++actionIndex) {
            if (actions.GetAt(actionIndex).ValueType() != JsonValueType::Object) {
                error = L"Widget descriptor contains a non-object quick action.";
                return std::nullopt;
            }
            const auto actionSource = actions.GetObjectAt(actionIndex);
            WidgetDescriptorQuickAction action;
            if (!ReadDescriptorString(actionSource, L"id", action.id, true, error) ||
                !ReadDescriptorString(actionSource, L"label", action.label, false, error) ||
                !ReadDescriptorString(actionSource, L"actionId", action.actionId, true, error) ||
                !ReadDescriptorString(actionSource, L"sourceElementId", action.sourceElementId, true, error)) {
                return std::nullopt;
            }
            if (!quickActionIds.emplace(action.id).second) {
                error = L"Widget descriptor repeats quick action ID '" + action.id + L"'.";
                return std::nullopt;
            }
            if (actionSource.HasKey(L"controllerButton")) {
                const auto buttonValue = actionSource.GetNamedValue(L"controllerButton");
                if (buttonValue.ValueType() == JsonValueType::String) {
                    std::wstring button(std::wstring_view(buttonValue.GetString()));
                    if (button.empty() || button.size() > kMaximumControllerButtonLength ||
                        !std::all_of(button.begin(), button.end(), [](const wchar_t character) {
                            return (character >= L'a' && character <= L'z') ||
                                   (character >= L'A' && character <= L'Z') ||
                                   (character >= L'0' && character <= L'9');
                        })) {
                        error = L"Widget descriptor controllerButton is invalid.";
                        return std::nullopt;
                    }
                    action.controllerButton = std::move(button);
                } else if (buttonValue.ValueType() != JsonValueType::Null) {
                    error = L"Widget descriptor controllerButton must be a string or null.";
                    return std::nullopt;
                }
            }
            descriptor.quickActions.push_back(std::move(action));
        }
        result.push_back(std::move(descriptor));
    }
    return result;
}

std::optional<WidgetStyleValue> ParseShellStyleValue(
    const JsonObject& source,
    const std::wstring_view property,
    std::wstring& error) {
    if (!HasOnlyProperties(source, {L"kind", L"text", L"number", L"unit"}) ||
        source.GetNamedValue(L"kind").ValueType() != JsonValueType::String ||
        source.GetNamedValue(L"text").ValueType() != JsonValueType::String) {
        error = L"Platform appearance style '" + std::wstring(property) +
                L"' has an invalid computed value shape.";
        return std::nullopt;
    }
    WidgetStyleValue value;
    value.kind = std::wstring(std::wstring_view(source.GetNamedString(L"kind")));
    value.text = std::wstring(std::wstring_view(source.GetNamedString(L"text")));
    if (!IsComputedValueKind(value.kind) || value.text.size() > kMaximumStyleValueTextLength ||
        std::any_of(value.text.begin(), value.text.end(), [](const wchar_t character) {
            return std::iswcntrl(character) != 0;
        })) {
        error = L"Platform appearance style '" + std::wstring(property) +
                L"' has an invalid computed value.";
        return std::nullopt;
    }
    const auto number = source.GetNamedValue(L"number");
    if (number.ValueType() == JsonValueType::Number) {
        const double parsed = number.GetNumber();
        if (!std::isfinite(parsed)) {
            error = L"Platform appearance contains a non-finite style number.";
            return std::nullopt;
        }
        value.number = parsed;
    } else if (number.ValueType() != JsonValueType::Null) {
        error = L"Platform appearance style number must be numeric or null.";
        return std::nullopt;
    }
    const auto unit = source.GetNamedValue(L"unit");
    if (unit.ValueType() == JsonValueType::String) {
        value.unit = std::wstring(std::wstring_view(unit.GetString()));
        if (value.unit.size() > kMaximumStyleUnitLength ||
            std::any_of(value.unit.begin(), value.unit.end(), [](const wchar_t character) {
                return !((character >= L'a' && character <= L'z') || character == L'%');
            })) {
            error = L"Platform appearance style unit is invalid.";
            return std::nullopt;
        }
    } else if (unit.ValueType() != JsonValueType::Null) {
        error = L"Platform appearance style unit must be a string or null.";
        return std::nullopt;
    }
    return value;
}

std::optional<PlatformAppearance> ParsePlatformAppearance(
    const JsonObject& payload,
    std::wstring& error) {
    if (!HasOnlyProperties(payload,
            {L"revision", L"themeId", L"themeVersion", L"interfaceScale", L"textScale",
             L"backdropOpacity", L"motion", L"contrast", L"boldText",
             L"transparency", L"animateWidgetSwitching", L"shellStyles"})) {
        error = L"Platform appearance payload has missing or unknown properties.";
        return std::nullopt;
    }
    const auto IsNumber = [&](const wchar_t* property) {
        return payload.GetNamedValue(property).ValueType() == JsonValueType::Number;
    };
    if (!IsNumber(L"revision") || !IsNumber(L"interfaceScale") || !IsNumber(L"textScale") ||
        !IsNumber(L"backdropOpacity") ||
        payload.GetNamedValue(L"themeId").ValueType() != JsonValueType::String ||
        payload.GetNamedValue(L"themeVersion").ValueType() != JsonValueType::String ||
        payload.GetNamedValue(L"motion").ValueType() != JsonValueType::String ||
        payload.GetNamedValue(L"contrast").ValueType() != JsonValueType::String ||
        payload.GetNamedValue(L"boldText").ValueType() != JsonValueType::Boolean ||
        payload.GetNamedValue(L"transparency").ValueType() != JsonValueType::String ||
        payload.GetNamedValue(L"animateWidgetSwitching").ValueType() !=
            JsonValueType::Boolean ||
        payload.GetNamedValue(L"shellStyles").ValueType() != JsonValueType::Object) {
        error = L"Platform appearance payload has invalid property types.";
        return std::nullopt;
    }

    PlatformAppearance appearance;
    const double revision = payload.GetNamedNumber(L"revision");
    appearance.interfaceScale = payload.GetNamedNumber(L"interfaceScale");
    appearance.textScale = payload.GetNamedNumber(L"textScale");
    appearance.backdropOpacity = payload.GetNamedNumber(L"backdropOpacity");
    appearance.themeId = std::wstring(std::wstring_view(payload.GetNamedString(L"themeId")));
    appearance.themeVersion = std::wstring(std::wstring_view(payload.GetNamedString(L"themeVersion")));
    if (!std::isfinite(revision) || revision < 0 ||
        revision > 9'007'199'254'740'991.0 || std::floor(revision) != revision ||
        !std::isfinite(appearance.interfaceScale) || appearance.interfaceScale < 0.8 ||
        appearance.interfaceScale > 1.25 ||
        !std::isfinite(appearance.textScale) || appearance.textScale < 0.85 ||
        appearance.textScale > 1.5 ||
        !std::isfinite(appearance.backdropOpacity) || appearance.backdropOpacity < 0.35 ||
        appearance.backdropOpacity > 0.8 ||
        !IsIdentifier(appearance.themeId) || !IsCanonicalThemeVersion(appearance.themeVersion)) {
        error = L"Platform appearance scalar values are outside their safety bounds.";
        return std::nullopt;
    }
    appearance.revision = static_cast<long long>(revision);
    const std::wstring motion(std::wstring_view(payload.GetNamedString(L"motion")));
    if (motion == L"system") appearance.motion = PlatformMotionPreference::System;
    else if (motion == L"full") appearance.motion = PlatformMotionPreference::Full;
    else if (motion == L"reduced") appearance.motion = PlatformMotionPreference::Reduced;
    else {
        error = L"Platform appearance motion preference is invalid.";
        return std::nullopt;
    }
    const std::wstring contrast(std::wstring_view(payload.GetNamedString(L"contrast")));
    if (contrast == L"system") appearance.contrast = PlatformContrastPreference::System;
    else if (contrast == L"standard") appearance.contrast = PlatformContrastPreference::Standard;
    else if (contrast == L"high") appearance.contrast = PlatformContrastPreference::High;
    else {
        error = L"Platform appearance contrast preference is invalid.";
        return std::nullopt;
    }
    appearance.boldText = payload.GetNamedBoolean(L"boldText");
    appearance.animateWidgetSwitching =
        payload.GetNamedBoolean(L"animateWidgetSwitching");
    const std::wstring transparency(
        std::wstring_view(payload.GetNamedString(L"transparency")));
    if (transparency == L"full")
        appearance.transparency = PlatformTransparencyPreference::Full;
    else if (transparency == L"reduced")
        appearance.transparency = PlatformTransparencyPreference::Reduced;
    else {
        error = L"Platform appearance transparency preference is invalid.";
        return std::nullopt;
    }

    const auto styles = payload.GetNamedObject(L"shellStyles");
    if (styles.Size() > kMaximumShellStyles) {
        error = L"Platform appearance contains too many shell styles.";
        return std::nullopt;
    }
    std::size_t totalProperties = 0;
    for (const auto& pair : styles) {
        const std::wstring key(std::wstring_view(pair.Key()));
        if (std::find(kShellStyleKeys.begin(), kShellStyleKeys.end(), key) ==
                kShellStyleKeys.end() ||
            pair.Value().ValueType() != JsonValueType::Object) {
            error = L"Platform appearance contains an unknown or invalid shell style.";
            return std::nullopt;
        }
        const auto properties = pair.Value().GetObject();
        if (properties.Size() > kMaximumShellProperties ||
            totalProperties + properties.Size() >
                static_cast<std::size_t>(kMaximumShellStyles * kMaximumShellProperties)) {
            error = L"Platform appearance shell style property count exceeds its safety bound.";
            return std::nullopt;
        }
        totalProperties += properties.Size();
        WidgetComputedStyle computed;
        computed.reserve(properties.Size());
        for (const auto& property : properties) {
            const std::wstring propertyName(std::wstring_view(property.Key()));
            if (!IsIdentifier(propertyName) || propertyName.size() > kMaximumIdentifierLength ||
                property.Value().ValueType() != JsonValueType::Object) {
                error = L"Platform appearance contains an invalid shell property.";
                return std::nullopt;
            }
            auto value = ParseShellStyleValue(
                property.Value().GetObject(), propertyName, error);
            if (!value) return std::nullopt;
            computed.emplace(propertyName, std::move(*value));
        }
        appearance.shellStyles.emplace(key, std::move(computed));
    }
    return appearance;
}

bool ReadRequestId(const JsonObject& response, long long& requestId) {
    if (!response.HasKey(L"requestId") ||
        response.GetNamedValue(L"requestId").ValueType() != JsonValueType::Number) return false;
    const double number = response.GetNamedNumber(L"requestId");
    if (!std::isfinite(number) || number < 0 || number > 9'007'199'254'740'991.0 ||
        std::floor(number) != number) return false;
    requestId = static_cast<long long>(number);
    return true;
}

std::wstring SafeBridgeError(const JsonObject& response) {
    if (!response.HasKey(L"payload") ||
        response.GetNamedValue(L"payload").ValueType() != JsonValueType::Object) {
        return L"WidgetBridge returned an error without a valid payload.";
    }
    const auto payload = response.GetNamedObject(L"payload");
    auto message = OptionalString(payload, L"message");
    if (message.empty()) return L"WidgetBridge returned an unspecified error.";
    for (auto& character : message) if (std::iswcntrl(character)) character = L' ';
    if (message.size() > 512) message.resize(512);
    return message;
}

WidgetBridgeRequestFailureCategory BridgeRequestFailureCategoryFromError(
    const JsonObject& response) {
    if (!response.HasKey(L"payload") ||
        response.GetNamedValue(L"payload").ValueType() != JsonValueType::Object)
        return WidgetBridgeRequestFailureCategory::None;
    const auto code = OptionalString(response.GetNamedObject(L"payload"), L"code");
    return code == L"stale_presentation_base"
        ? WidgetBridgeRequestFailureCategory::StalePresentationBase
        : WidgetBridgeRequestFailureCategory::None;
}

WidgetNode ParseNode(const JsonObject& source) {
    WidgetNode node;
    node.id = std::wstring(std::wstring_view(source.GetNamedString(L"id")));
    node.kind = std::wstring(std::wstring_view(source.GetNamedString(L"kind")));
    node.text = OptionalString(source, L"text");
    node.accessibilityLabel = OptionalString(source, L"accessibilityLabel");
    node.accessibilityValue = OptionalString(source, L"accessibilityValue");
    node.actionId = OptionalString(source, L"actionId");
    node.textEntryValue = OptionalString(source, L"textEntryValue");
    node.textEntryPlaceholder = OptionalString(source, L"textEntryPlaceholder");
    if (source.HasKey(L"textEntryMaximumLength")) {
        const auto value = source.GetNamedNumber(L"textEntryMaximumLength");
        if (!std::isfinite(value) || value < 1 ||
            value > protocol_contract::MaximumTextEntryLength ||
            std::floor(value) != value)
            throw winrt::hresult_invalid_argument();
        node.textEntryMaximumLength = static_cast<std::size_t>(value);
    }
    if (node.kind == L"textEntry" &&
        (node.textEntryMaximumLength == 0 ||
         node.textEntryValue.size() > node.textEntryMaximumLength ||
         node.textEntryPlaceholder.size() > protocol_contract::MaximumTextEntryLength ||
         std::any_of(node.textEntryValue.begin(), node.textEntryValue.end(),
             [](const wchar_t value) { return std::iswcntrl(value) != 0; }) ||
         std::any_of(node.textEntryPlaceholder.begin(), node.textEntryPlaceholder.end(),
             [](const wchar_t value) { return std::iswcntrl(value) != 0; })))
        throw winrt::hresult_invalid_argument();
    if (node.kind == L"textEntry") {
        // The trigger participates in the existing button layout/focus/UIA
        // contract. Activation opens a native host-owned edit modal.
        node.isTextEntry = true;
        node.kind = L"button";
    }
    node.valueChangedActionId = OptionalString(source, L"valueChangedActionId");
    node.focusPersistenceId = OptionalString(source, L"focusPersistenceId");
    node.sliderInteractionMode = OptionalString(source, L"sliderInteractionMode");
    if (!node.sliderInteractionMode.empty() &&
        node.sliderInteractionMode != L"direct" &&
        node.sliderInteractionMode != L"activateToAdjust")
        throw winrt::hresult_invalid_argument();
    node.imageSource = OptionalString(source, L"imageSource");
    node.artworkHandle = OptionalString(source, L"artworkHandle");
    if ((!node.artworkHandle.empty() &&
         (node.artworkHandle.size() > kMaximumIdentifierLength ||
          !IsIdentifier(node.artworkHandle))))
        throw winrt::hresult_invalid_argument();
    node.imageFit = OptionalString(source, L"imageFit");
    node.glyph = OptionalString(source, L"glyph");
    node.indicatorSize = OptionalString(source, L"indicatorSize");
    node.visibleWhen = OptionalString(source, L"visibleWhen");
    if (!node.visibleWhen.empty() && node.visibleWhen != L"always" &&
        node.visibleWhen != L"compactOnly" && node.visibleWhen != L"expandedOnly")
        throw winrt::hresult_invalid_argument();
    node.inputScopeId = OptionalString(source, L"inputScopeId");
    node.scrollAxis = OptionalString(source, L"scrollAxis");
    node.scrollNearStartActionId = OptionalString(source, L"scrollNearStartActionId");
    node.scrollNearEndActionId = OptionalString(source, L"scrollNearEndActionId");
    node.collectionAnchorKey = OptionalString(source, L"collectionAnchorKey");
    node.collectionItemKey = OptionalString(source, L"collectionItemKey");
    if ((!node.collectionAnchorKey.empty() &&
         (node.collectionAnchorKey.size() > kMaximumIdentifierLength ||
          !IsIdentifier(node.collectionAnchorKey))) ||
        (!node.collectionItemKey.empty() &&
         (node.collectionItemKey.size() > kMaximumIdentifierLength ||
          !IsIdentifier(node.collectionItemKey))))
        throw winrt::hresult_invalid_argument();
    if (source.HasKey(L"scrollPaginationThreshold")) {
        if (source.GetNamedValue(L"scrollPaginationThreshold").ValueType() !=
            JsonValueType::Number)
            throw winrt::hresult_invalid_argument();
        const auto value = source.GetNamedNumber(L"scrollPaginationThreshold");
        if (!std::isfinite(value) || value < 1.0 ||
            value > protocol_contract::MaximumScrollPaginationThreshold ||
            std::floor(value) != value)
            throw winrt::hresult_invalid_argument();
        node.scrollPaginationThreshold = static_cast<std::size_t>(value);
    }
    if (source.HasKey(L"virtualCollectionWindow")) {
        const auto encoded = source.GetNamedObject(L"virtualCollectionWindow");
        if (!HasNoUnknownProperties(encoded,
                {L"requestGeneration", L"change", L"firstItemIndex",
                 L"totalItemCount", L"hasBefore", L"hasAfter",
                 L"estimatedItemExtent"}))
            throw winrt::hresult_invalid_argument();
        const auto exactInteger = [&encoded](
            const wchar_t* name,
            const bool optional) -> std::optional<std::uint64_t> {
            if (!encoded.HasKey(name)) {
                if (optional) return std::nullopt;
                throw winrt::hresult_invalid_argument();
            }
            const auto value = encoded.GetNamedValue(name);
            if (value.ValueType() == JsonValueType::Null && optional)
                return std::nullopt;
            if (value.ValueType() != JsonValueType::Number)
                throw winrt::hresult_invalid_argument();
            const double number = value.GetNumber();
            if (!std::isfinite(number) || number < 0.0 ||
                number > static_cast<double>(kMaximumVirtualCollectionRequestGeneration) ||
                std::floor(number) != number)
                throw winrt::hresult_invalid_argument();
            return static_cast<std::uint64_t>(number);
        };
        VirtualCollectionWindow window;
        window.requestGeneration = *exactInteger(L"requestGeneration", false);
        if (window.requestGeneration == 0 ||
            window.requestGeneration > kMaximumVirtualCollectionRequestGeneration)
            throw winrt::hresult_invalid_argument();
        const auto change = std::wstring(std::wstring_view(
            encoded.GetNamedString(L"change")));
        if (change == L"replace")
            window.change = VirtualCollectionWindowChange::Replace;
        else if (change == L"append")
            window.change = VirtualCollectionWindowChange::Append;
        else if (change == L"prepend")
            window.change = VirtualCollectionWindowChange::Prepend;
        else
            throw winrt::hresult_invalid_argument();
        window.firstItemIndex = exactInteger(L"firstItemIndex", true);
        window.totalItemCount = exactInteger(L"totalItemCount", true);
        window.hasBefore = encoded.GetNamedBoolean(L"hasBefore");
        window.hasAfter = encoded.GetNamedBoolean(L"hasAfter");
        window.estimatedItemExtent = encoded.GetNamedNumber(L"estimatedItemExtent");
        if (!window.firstItemIndex &&
            window.change != VirtualCollectionWindowChange::Replace)
            throw winrt::hresult_invalid_argument();
        if (!std::isfinite(window.estimatedItemExtent) ||
            window.estimatedItemExtent < kMinimumVirtualCollectionItemExtent ||
            window.estimatedItemExtent > kMaximumVirtualCollectionItemExtent ||
            (window.totalItemCount &&
             *window.totalItemCount > kMaximumVirtualCollectionItems) ||
            (window.totalItemCount &&
             static_cast<double>(*window.totalItemCount) *
                 window.estimatedItemExtent > kMaximumVirtualCollectionExtent) ||
            (window.totalItemCount && !window.firstItemIndex))
            throw winrt::hresult_invalid_argument();
        node.virtualCollectionWindow = window;
    }
    node.actionSurfaceOrientation = OptionalString(source, L"actionSurfaceOrientation");
    if (source.HasKey(L"gridMinimumColumnWidth")) {
        if (source.GetNamedValue(L"gridMinimumColumnWidth").ValueType() != JsonValueType::Number)
            throw winrt::hresult_invalid_argument();
        const auto value = source.GetNamedNumber(L"gridMinimumColumnWidth");
        if (!std::isfinite(value) ||
            value < protocol_contract::MinimumGridColumnWidth ||
            value > protocol_contract::MaximumGridColumnWidth)
            throw winrt::hresult_invalid_argument();
        node.gridMinimumColumnWidth = value;
    }
    if (source.HasKey(L"gridMaximumColumns")) {
        if (source.GetNamedValue(L"gridMaximumColumns").ValueType() != JsonValueType::Number)
            throw winrt::hresult_invalid_argument();
        const auto value = source.GetNamedNumber(L"gridMaximumColumns");
        if (!std::isfinite(value) || value < 1.0 ||
            value > protocol_contract::MaximumGridColumns ||
            std::floor(value) != value)
            throw winrt::hresult_invalid_argument();
        node.gridMaximumColumns = static_cast<std::size_t>(value);
    }
    if (source.HasKey(L"styleClasses")) {
        const auto classes = source.GetNamedArray(L"styleClasses");
        node.styleClasses.reserve(classes.Size());
        for (uint32_t index = 0; index < classes.Size(); ++index) {
            node.styleClasses.emplace_back(
                std::wstring_view(classes.GetStringAt(index)));
        }
    }
    if (source.HasKey(L"shortcuts")) {
        const auto shortcuts = source.GetNamedArray(L"shortcuts");
        node.shortcuts.reserve(shortcuts.Size());
        for (uint32_t index = 0; index < shortcuts.Size(); ++index) {
            const auto shortcut = shortcuts.GetObjectAt(index);
            node.shortcuts.push_back({
                std::wstring(std::wstring_view(shortcut.GetNamedString(L"button"))),
                std::wstring(std::wstring_view(shortcut.GetNamedString(L"actionId"))),
                std::wstring(std::wstring_view(shortcut.GetNamedString(L"phase"))),
            });
        }
    }
    if (source.HasKey(L"focus")) {
        const auto focus = source.GetNamedObject(L"focus");
        node.focusUp = OptionalString(focus, L"up");
        node.focusDown = OptionalString(focus, L"down");
        node.focusLeft = OptionalString(focus, L"left");
        node.focusRight = OptionalString(focus, L"right");
    }
    if (source.HasKey(L"value") && source.HasKey(L"maximum")) {
        node.value = source.GetNamedNumber(L"value");
        node.maximum = source.GetNamedNumber(L"maximum");
        node.hasProgress = true;
    }
    if (source.HasKey(L"minimum") && source.HasKey(L"step")) {
        node.minimum = source.GetNamedNumber(L"minimum");
        node.step = source.GetNamedNumber(L"step");
        node.hasSliderRange = true;
    }
    if (source.HasKey(L"isDisabled")) node.isDisabled = source.GetNamedBoolean(L"isDisabled");
    if (source.HasKey(L"isSelected")) node.isSelected = source.GetNamedBoolean(L"isSelected");
    if (source.HasKey(L"isBusy")) node.isBusy = source.GetNamedBoolean(L"isBusy");
    if (source.HasKey(L"children")) {
        const JsonArray children = source.GetNamedArray(L"children");
        node.children.reserve(children.Size());
        for (uint32_t index = 0; index < children.Size(); ++index) {
            node.children.push_back(ParseNode(children.GetObjectAt(index)));
        }
    }
    if (node.virtualCollectionWindow) {
        if (node.kind != L"scroll" || node.collectionAnchorKey.empty() ||
            node.virtualCollectionWindow->hasBefore !=
                !node.scrollNearStartActionId.empty() ||
            node.virtualCollectionWindow->hasAfter !=
                !node.scrollNearEndActionId.empty())
            throw winrt::hresult_invalid_argument();
        std::size_t itemCount{};
        const auto countItems = [&](const auto& self,
                                    const WidgetNode& current,
                                    const bool root) -> void {
            if (!root && current.kind == L"scroll") return;
            if (!current.collectionItemKey.empty()) {
                ++itemCount;
                return;
            }
            for (const auto& child : current.children)
                self(self, child, false);
        };
        countItems(countItems, node, true);
        const auto& window = *node.virtualCollectionWindow;
        if (itemCount == 0 || itemCount > 256 ||
            (window.firstItemIndex &&
             (*window.firstItemIndex > kMaximumVirtualCollectionItems ||
              itemCount > kMaximumVirtualCollectionItems - *window.firstItemIndex)) ||
            (window.firstItemIndex && window.totalItemCount &&
             (*window.firstItemIndex > *window.totalItemCount ||
              itemCount > *window.totalItemCount - *window.firstItemIndex)) ||
            (window.firstItemIndex == 0 && window.hasBefore) ||
            (window.firstItemIndex && window.totalItemCount &&
             *window.firstItemIndex + itemCount == *window.totalItemCount &&
             window.hasAfter))
            throw winrt::hresult_invalid_argument();
    }
    return node;
}

WidgetComputedStyle ParseComputedStyle(const JsonObject& source) {
    WidgetComputedStyle style;
    style.reserve(source.Size());
    for (const auto& pair : source) {
        const auto value = pair.Value().GetObject();
        WidgetStyleValue property;
        property.kind = std::wstring(std::wstring_view(value.GetNamedString(L"kind")));
        property.text = std::wstring(std::wstring_view(value.GetNamedString(L"text")));
        property.unit = OptionalString(value, L"unit");
        if (value.HasKey(L"number") &&
            value.GetNamedValue(L"number").ValueType() == JsonValueType::Number) {
            property.number = value.GetNamedNumber(L"number");
        }
        style.emplace(std::wstring(std::wstring_view(pair.Key())), std::move(property));
    }
    return style;
}

void ApplyComputedStyles(
    WidgetNode& node,
    const JsonObject& styles,
    const std::wstring_view prefix = {}) {
    const auto key = std::wstring(prefix) + node.id;
    if (styles.HasKey(key)) {
        const auto states = styles.GetNamedObject(key);
        if (states.HasKey(L"base")) {
            node.baseStyle = ParseComputedStyle(states.GetNamedObject(L"base"));
        }
        if (states.HasKey(L"focused")) {
            node.focusedStyle = ParseComputedStyle(states.GetNamedObject(L"focused"));
        }
        if (states.HasKey(L"pressed")) {
            node.pressedStyle = ParseComputedStyle(states.GetNamedObject(L"pressed"));
        }
    }
    for (auto& child : node.children) ApplyComputedStyles(child, styles, prefix);
}

void ApplyComputedStyles(WidgetSnapshot& snapshot, const JsonObject& styles) {
    ApplyComputedStyles(snapshot.root, styles);
    for (auto& layout : snapshot.pinnedLayouts)
        if (layout.root)
            ApplyComputedStyles(*layout.root, styles, layout.id + L"/");
}

void ValidatePinnedProjectionCatalogBounds(const JsonObject& source) {
    if (!source.HasKey(L"pinnedLayouts")) return;
    const auto layouts = source.GetNamedArray(L"pinnedLayouts");
    bool hasProjection{};
    std::size_t characters{};
    std::size_t nodes{};
    std::size_t resources{};
    const auto inspect = [&](const auto& self, const JsonObject& root,
                             const std::size_t depth,
                             std::unordered_set<std::wstring>& ids) -> void {
        if (++nodes > kMaximumPinnedProjectionAggregateNodes ||
            depth > kMaximumWidgetTreeDepth)
            throw winrt::hresult_invalid_argument(
                L"Pinned projection catalog exceeds its structural bound.");
        const auto id = OptionalString(root, L"id");
        if (!IsIdentifier(id) || !ids.insert(id).second)
            throw winrt::hresult_invalid_argument(
                L"Pinned projection contains invalid node identity.");
        if (root.HasKey(L"imageSource") || root.HasKey(L"artworkHandle")) {
            if (++resources > kMaximumPinnedProjectionAggregateResources)
                throw winrt::hresult_invalid_argument(
                    L"Pinned projection catalog exceeds its resource bound.");
        }
        const auto children = root.GetNamedArray(L"children");
        for (std::uint32_t index = 0; index < children.Size(); ++index)
            self(self, children.GetObjectAt(index), depth + 1, ids);
    };
    for (std::uint32_t index = 0; index < layouts.Size(); ++index) {
        const auto layout = layouts.GetObjectAt(index);
        if (!layout.HasKey(L"root")) continue;
        hasProjection = true;
        const auto root = layout.GetNamedObject(L"root");
        characters += std::wstring_view(root.Stringify()).size();
        std::unordered_set<std::wstring> ids;
        inspect(inspect, root, 1, ids);
    }
    if (!hasProjection) return;
    const auto fullRoot = source.GetNamedObject(L"root");
    characters += std::wstring_view(fullRoot.Stringify()).size();
    std::unordered_set<std::wstring> ids;
    inspect(inspect, fullRoot, 1, ids);
    if (characters > kMaximumPinnedProjectionAggregateCharacters)
        throw winrt::hresult_invalid_argument(
            L"Pinned projection catalog exceeds its aggregate string bound.");
}

WidgetSnapshot ParseSnapshot(const JsonObject& source) {
    ValidatePinnedProjectionCatalogBounds(source);
    WidgetSnapshot snapshot;
    if (source.HasKey(L"protocolVersion")) {
        const auto encodedVersion = source.GetNamedValue(L"protocolVersion");
        if (encodedVersion.ValueType() != JsonValueType::Number) {
            throw winrt::hresult_invalid_argument(
                L"Widget snapshot protocolVersion must be a number.");
        }
        const double protocolVersion = encodedVersion.GetNumber();
        if (!std::isfinite(protocolVersion) ||
            std::floor(protocolVersion) != protocolVersion ||
            protocolVersion < kMinimumWidgetSnapshotProtocolVersion ||
            protocolVersion > kMaximumWidgetSnapshotProtocolVersion) {
            throw winrt::hresult_invalid_argument(
                L"Widget snapshot protocolVersion is unsupported.");
        }
        snapshot.protocolVersion = static_cast<int>(protocolVersion);
    }
    snapshot.sequence = static_cast<long long>(source.GetNamedNumber(L"sequence"));
    snapshot.instanceId = std::wstring(std::wstring_view(source.GetNamedString(L"widgetInstanceId")));
    snapshot.activeInputScopeId =
        std::wstring(std::wstring_view(source.GetNamedString(L"activeInputScopeId")));
    snapshot.initialFocusId = OptionalString(source, L"initialFocusId");
    const auto parseSurface = [](const JsonObject& hints) {
        if (!HasNoUnknownProperties(hints,
                {L"mode", L"widthMode", L"heightMode", L"preferredWidth",
                 L"preferredHeight", L"minimumWidth", L"minimumHeight"}))
            throw winrt::hresult_invalid_argument(
                L"Widget surface hints contain an unknown property.");
        WidgetSurfaceHints parsed;
        parsed.mode = OptionalString(hints, L"mode");
        if (hints.HasKey(L"widthMode"))
            parsed.widthMode = OptionalString(hints, L"widthMode");
        if (hints.HasKey(L"heightMode"))
            parsed.heightMode = OptionalString(hints, L"heightMode");
        const auto optionalNumber = [&hints](const wchar_t* name) -> std::optional<double> {
            if (!hints.HasKey(name)) return std::nullopt;
            return hints.GetNamedNumber(name);
        };
        parsed.preferredWidth = optionalNumber(L"preferredWidth");
        parsed.preferredHeight = optionalNumber(L"preferredHeight");
        parsed.minimumWidth = optionalNumber(L"minimumWidth");
        parsed.minimumHeight = optionalNumber(L"minimumHeight");
        const auto validMode = parsed.mode.empty() || parsed.mode == L"adaptive" ||
            parsed.mode == L"compact" || parsed.mode == L"standard" ||
            parsed.mode == L"wide";
        const auto validAxis = [](const std::optional<std::wstring>& value) {
            return !value || *value == L"preferred" || *value == L"content" ||
                *value == L"fillAvailable";
        };
        const auto validPair = [](const std::optional<double> width,
                                  const std::optional<double> height) {
            if (width.has_value() != height.has_value()) return false;
            if (!width) return true;
            return std::isfinite(*width) && std::isfinite(*height) &&
                *width >= surface_geometry::kMinimumAuthoredContentWidthDip &&
                *width <= surface_geometry::kMaximumAuthoredContentWidthDip &&
                *height >= surface_geometry::kMinimumAuthoredContentHeightDip &&
                *height <= surface_geometry::kMaximumAuthoredContentHeightDip;
        };
        if (!validMode || !validAxis(parsed.widthMode) ||
            !validAxis(parsed.heightMode) ||
            !validPair(parsed.preferredWidth, parsed.preferredHeight) ||
            !validPair(parsed.minimumWidth, parsed.minimumHeight) ||
            (parsed.preferredWidth && parsed.minimumWidth &&
             *parsed.minimumWidth > *parsed.preferredWidth) ||
            (parsed.preferredHeight && parsed.minimumHeight &&
             *parsed.minimumHeight > *parsed.preferredHeight))
            throw winrt::hresult_invalid_argument(
                L"Widget surface hints are invalid.");
        return parsed;
    };
    if (source.HasKey(L"surface"))
        snapshot.surface = parseSurface(source.GetNamedObject(L"surface"));
    if (source.HasKey(L"pinnedLayouts")) {
        const JsonArray layouts = source.GetNamedArray(L"pinnedLayouts");
        if (layouts.Size() > kMaximumPinnedLayoutCount)
            throw winrt::hresult_invalid_argument(
                L"Widget snapshot has too many pinned layouts.");
        std::unordered_set<std::wstring> ids;
        snapshot.pinnedLayouts.reserve(layouts.Size());
        for (uint32_t index = 0; index < layouts.Size(); ++index) {
            const auto layout = layouts.GetObjectAt(index);
            if (!HasNoUnknownProperties(layout,
                    {L"id", L"name", L"surface", L"root",
                     L"activeInputScopeId", L"initialFocusId"}))
                throw winrt::hresult_invalid_argument(
                    L"Widget snapshot pinned layout contains an unknown property.");
            WidgetPinnedLayout parsed{
                std::wstring(std::wstring_view(layout.GetNamedString(L"id"))),
                std::wstring(std::wstring_view(layout.GetNamedString(L"name"))),
                parseSurface(layout.GetNamedObject(L"surface")),
            };
            if (parsed.id.empty() ||
                parsed.id.size() > protocol_contract::MaximumCapabilityIdLength ||
                parsed.name.empty() ||
                parsed.name.size() >
                    protocol_contract::MaximumPinnedPresentationLayoutNameLength ||
                !ids.insert(parsed.id).second)
                throw winrt::hresult_invalid_argument(
                    L"Widget snapshot pinned layout identity is invalid.");
            if (layout.HasKey(L"root")) {
                if (snapshot.protocolVersion <
                        protocol_contract::PinnedPresentationProjectionsVersion ||
                    !layout.HasKey(L"activeInputScopeId"))
                    throw winrt::hresult_invalid_argument(
                        L"Widget snapshot pinned projection version is invalid.");
                parsed.root = ParseNode(layout.GetNamedObject(L"root"));
                parsed.activeInputScopeId = OptionalString(layout, L"activeInputScopeId");
                parsed.initialFocusId = OptionalString(layout, L"initialFocusId");
                if (!IsIdentifier(parsed.activeInputScopeId) ||
                    (!parsed.initialFocusId.empty() &&
                     !IsIdentifier(parsed.initialFocusId)))
                    throw winrt::hresult_invalid_argument(
                        L"Widget snapshot pinned projection focus authority is invalid.");
            } else if (layout.HasKey(L"activeInputScopeId") ||
                       layout.HasKey(L"initialFocusId")) {
                throw winrt::hresult_invalid_argument(
                    L"Widget snapshot pinned projection root is missing.");
            }
            snapshot.pinnedLayouts.push_back(std::move(parsed));
        }
        if (!snapshot.pinnedLayouts.empty() &&
            snapshot.protocolVersion < protocol_contract::PinnedPresentationLayoutsVersion)
            throw winrt::hresult_invalid_argument();
    }
    if (source.HasKey(L"embeddedMedia")) {
        if (snapshot.protocolVersion < protocol_contract::EmbeddedMediaSurfaceVersion)
            throw winrt::hresult_invalid_argument(
                L"Embedded media requires protocol version 22 or later.");
        const auto media = source.GetNamedObject(L"embeddedMedia");
        if (!HasNoUnknownProperties(media,
                {L"id", L"accessibleName", L"entryAsset", L"surface",
                 L"aspectRatio", L"resources", L"commands"}))
            throw winrt::hresult_invalid_argument(
                L"Embedded media contains an unknown property.");
        EmbeddedMediaSurfaceDeclaration parsed;
        parsed.id = OptionalString(media, L"id");
        parsed.accessibleName = OptionalString(media, L"accessibleName");
        parsed.entryAsset = OptionalString(media, L"entryAsset");
        parsed.surface = parseSurface(media.GetNamedObject(L"surface"));
        parsed.aspectRatio = media.GetNamedNumber(L"aspectRatio");
        const auto resources = media.GetNamedArray(L"resources");
        const auto commands = media.GetNamedArray(L"commands");
        if (resources.Size() == 0 ||
            resources.Size() > protocol_contract::MaximumEmbeddedMediaResourceCount ||
            commands.Size() > protocol_contract::MaximumEmbeddedMediaCommandCount ||
            !std::isfinite(parsed.aspectRatio) ||
            parsed.aspectRatio < 0.1 || parsed.aspectRatio > 10.0 ||
            !IsIdentifier(parsed.id) || parsed.accessibleName.empty() ||
            !parsed.surface.preferredWidth || !parsed.surface.preferredHeight ||
            !parsed.surface.minimumWidth || !parsed.surface.minimumHeight)
            throw winrt::hresult_invalid_argument(
                L"Embedded media authority or bounds are invalid.");
        std::unordered_set<std::wstring> paths;
        for (uint32_t index = 0; index < resources.Size(); ++index) {
            const auto resource = resources.GetObjectAt(index);
            if (!HasNoUnknownProperties(resource, {L"path", L"contentType"}))
                throw winrt::hresult_invalid_argument(
                    L"Embedded media resource contains an unknown property.");
            EmbeddedMediaResourceDeclaration item{
                OptionalString(resource, L"path"),
                OptionalString(resource, L"contentType")};
            if (item.path.empty() || item.path.size() >
                    protocol_contract::MaximumEmbeddedMediaResourcePathLength ||
                item.contentType.empty() || item.contentType.size() >
                    protocol_contract::MaximumEmbeddedMediaContentTypeLength ||
                !paths.insert(item.path).second)
                throw winrt::hresult_invalid_argument(
                    L"Embedded media resource declaration is invalid.");
            parsed.resources.push_back(std::move(item));
        }
        if (!paths.contains(parsed.entryAsset))
            throw winrt::hresult_invalid_argument(
                L"Embedded media entry asset is not declared.");
        std::unordered_set<std::wstring> commandSet;
        for (uint32_t index = 0; index < commands.Size(); ++index) {
            const auto command = std::wstring(
                std::wstring_view(commands.GetStringAt(index)));
            if (command.empty() || !commandSet.insert(command).second)
                throw winrt::hresult_invalid_argument(
                    L"Embedded media command declaration is invalid.");
            parsed.commands.push_back(command);
        }
        snapshot.embeddedMedia = std::move(parsed);
    }
    if (source.HasKey(L"quickActions")) {
        const JsonArray actions = source.GetNamedArray(L"quickActions");
        snapshot.quickActions.reserve(actions.Size());
        for (uint32_t index = 0; index < actions.Size(); ++index) {
            const auto action = actions.GetObjectAt(index);
            snapshot.quickActions.push_back({
                std::wstring(std::wstring_view(action.GetNamedString(L"button"))),
                std::wstring(std::wstring_view(action.GetNamedString(L"actionId"))),
                std::wstring(std::wstring_view(action.GetNamedString(L"label"))),
            });
        }
    }
    snapshot.root = ParseNode(source.GetNamedObject(L"root"));
    const auto usesVirtualWindow = [&](const auto& self,
                                      const WidgetNode& node) -> bool {
        if (node.virtualCollectionWindow) return true;
        return std::any_of(node.children.begin(), node.children.end(),
            [&](const WidgetNode& child) { return self(self, child); });
    };
    if (usesVirtualWindow(usesVirtualWindow, snapshot.root) &&
        snapshot.protocolVersion < protocol_contract::VirtualCollectionWindowVersion)
        throw winrt::hresult_invalid_argument();
    snapshot.documentJson = std::wstring(std::wstring_view(source.Stringify()));
    return snapshot;
}

WidgetSnapshot ParseStyledSnapshotPayload(const JsonObject& payload) {
    auto snapshot = ParseSnapshot(payload.GetNamedObject(L"snapshot"));
    if (payload.HasKey(L"renderStyles"))
        ApplyComputedStyles(snapshot, payload.GetNamedObject(L"renderStyles"));
    return snapshot;
}

bool IsPresentationGeneration(const std::wstring_view value) noexcept {
    return (value.size() == 32 || value.size() == 64) &&
        std::all_of(value.begin(), value.end(), [](const wchar_t character) {
            return (character >= L'0' && character <= L'9') ||
                (character >= L'a' && character <= L'f') ||
                (character >= L'A' && character <= L'F');
        });
}

long long RequiredIntegral(
    const JsonObject& source,
    const wchar_t* name,
    const long long minimum = 0) {
    if (!source.HasKey(name) ||
        source.GetNamedValue(name).ValueType() != JsonValueType::Number)
        throw winrt::hresult_invalid_argument();
    const double value = source.GetNamedNumber(name);
    if (!std::isfinite(value) || std::floor(value) != value ||
        value < static_cast<double>(minimum) ||
        value > 9'007'199'254'740'991.0)
        throw winrt::hresult_invalid_argument();
    return static_cast<long long>(value);
}

bool IsDocumentPresentationProperty(const std::wstring_view property) noexcept {
    return property == L"activeInputScopeId" || property == L"initialFocusId" ||
        property == L"quickActions" || property == L"surface";
}

bool IsNodePresentationProperty(const std::wstring_view property) noexcept {
    static constexpr std::array<std::wstring_view, 37> properties{
        L"visibleWhen", L"text", L"accessibilityLabel", L"accessibilityValue",
        L"actionId", L"textEntryValue", L"textEntryPlaceholder",
        L"textEntryMaximumLength", L"value", L"minimum", L"maximum", L"step",
        L"valueChangedActionId", L"sliderInteractionMode", L"imageSource",
        L"artworkHandle", L"imageFit", L"glyph", L"indicatorSize",
        L"actionSurfaceOrientation", L"gridMinimumColumnWidth",
        L"gridMaximumColumns", L"isDisabled", L"isSelected", L"isBusy",
        L"focusPersistenceId", L"focus", L"inputScopeId", L"scrollAxis",
        L"scrollNearStartActionId", L"scrollNearEndActionId",
        L"scrollPaginationThreshold", L"virtualCollectionWindow",
        L"collectionAnchorKey",
        L"collectionItemKey", L"styleClasses",
        L"shortcuts"};
    return std::find(properties.begin(), properties.end(), property) != properties.end();
}

bool ValidateWidgetDocumentStructure(
    const JsonObject& document,
    std::wstring& error) {
    if (!HasNoUnknownProperties(document,
            {L"protocolVersion", L"sequence", L"widgetInstanceId",
             L"activeInputScopeId", L"initialFocusId", L"quickActions",
             L"surface", L"pinnedLayouts", L"embeddedMedia", L"root"}) ||
        !document.HasKey(L"root") ||
        document.GetNamedValue(L"root").ValueType() != JsonValueType::Object) {
        error = L"The materialized widget document shape is invalid.";
        return false;
    }
    std::vector<std::pair<JsonObject, std::size_t>> pending{
        {document.GetNamedObject(L"root"), 1}};
    std::unordered_set<std::wstring> ids;
    std::size_t count{};
    while (!pending.empty()) {
        auto [node, depth] = std::move(pending.back());
        pending.pop_back();
        if (++count > kMaximumWidgetNodes || depth > kMaximumWidgetTreeDepth) {
            error = L"The materialized widget tree exceeds its structural bound.";
            return false;
        }
        if (!HasNoUnknownProperties(node,
                {L"id", L"kind", L"visibleWhen", L"text",
                 L"accessibilityLabel", L"accessibilityValue", L"actionId",
                 L"textEntryValue", L"textEntryPlaceholder",
                 L"textEntryMaximumLength", L"value", L"minimum", L"maximum",
                 L"step", L"valueChangedActionId", L"sliderInteractionMode",
                 L"imageSource", L"artworkHandle", L"imageFit", L"glyph",
                 L"indicatorSize", L"actionSurfaceOrientation",
                 L"gridMinimumColumnWidth", L"gridMaximumColumns", L"isDisabled",
                 L"isSelected", L"isBusy", L"focusPersistenceId", L"focus",
                 L"inputScopeId", L"scrollAxis", L"scrollNearStartActionId",
                 L"scrollNearEndActionId", L"scrollPaginationThreshold",
                 L"virtualCollectionWindow",
                 L"collectionAnchorKey", L"collectionItemKey",
                 L"styleClasses", L"shortcuts",
                 L"children"})) {
            error = L"The materialized widget node contains an unknown property.";
            return false;
        }
        const auto id = OptionalString(node, L"id");
        if (!IsIdentifier(id) || !ids.insert(id).second ||
            !node.HasKey(L"kind") ||
            node.GetNamedValue(L"kind").ValueType() != JsonValueType::String ||
            !node.HasKey(L"children") ||
            node.GetNamedValue(L"children").ValueType() != JsonValueType::Array) {
            error = L"The materialized widget tree contains invalid node identity.";
            return false;
        }
        const auto children = node.GetNamedArray(L"children");
        for (std::uint32_t index = 0; index < children.Size(); ++index) {
            if (children.GetAt(index).ValueType() != JsonValueType::Object) {
                error = L"The materialized widget tree contains an invalid child.";
                return false;
            }
            pending.emplace_back(children.GetObjectAt(index), depth + 1);
        }
    }
    error.clear();
    return true;
}

std::optional<JsonObject> FindPresentationNode(
    const JsonObject& document,
    const std::wstring_view id) {
    std::vector<JsonObject> pending{document.GetNamedObject(L"root")};
    while (!pending.empty()) {
        auto node = std::move(pending.back());
        pending.pop_back();
        if (OptionalString(node, L"id") == id) return node;
        const auto children = node.GetNamedArray(L"children");
        for (std::uint32_t index = 0; index < children.Size(); ++index)
            pending.push_back(children.GetObjectAt(index));
    }
    return std::nullopt;
}

struct PresentationNodeParent final {
    JsonArray children;
    std::uint32_t index{};
};

std::optional<PresentationNodeParent> FindPresentationNodeParent(
    const JsonObject& document,
    const std::wstring_view id) {
    std::vector<JsonObject> pending{document.GetNamedObject(L"root")};
    while (!pending.empty()) {
        auto node = std::move(pending.back());
        pending.pop_back();
        const auto children = node.GetNamedArray(L"children");
        for (std::uint32_t index = 0; index < children.Size(); ++index) {
            auto child = children.GetObjectAt(index);
            if (OptionalString(child, L"id") == id) return PresentationNodeParent{children, index};
            pending.push_back(std::move(child));
        }
    }
    return std::nullopt;
}

WidgetPresentationUpdate ParsePresentationUpdatePayload(
    const JsonObject& payload,
    const bool typedTransactionEnvelope = false) {
    const bool payloadPropertiesCurrent = typedTransactionEnvelope
        ? HasNoUnknownProperties(
            payload,
            {L"widgetId", L"transactionKind", L"baseSequence",
             L"recoveryOriginSequence", L"update", L"renderStyles"})
        : HasNoUnknownProperties(payload, {L"widgetId", L"update", L"renderStyles"});
    if (!payloadPropertiesCurrent ||
        !payload.HasKey(L"update") ||
        payload.GetNamedValue(L"update").ValueType() != JsonValueType::Object)
        throw winrt::hresult_invalid_argument();
    const auto source = payload.GetNamedObject(L"update");
    if (winrt::to_string(source.Stringify()).size() >
            kMaximumPresentationUpdateBytes ||
        !HasOnlyProperties(source,
            {L"protocolVersion", L"widgetInstanceId", L"presentationGeneration",
             L"baseSequence", L"sequence", L"operations"}))
        throw winrt::hresult_invalid_argument();

    WidgetPresentationUpdate update;
    update.protocolVersion = static_cast<int>(RequiredIntegral(source, L"protocolVersion"));
    update.widgetInstanceId = OptionalString(source, L"widgetInstanceId");
    update.presentationGeneration = OptionalString(source, L"presentationGeneration");
    update.baseSequence = RequiredIntegral(source, L"baseSequence");
    update.sequence = RequiredIntegral(source, L"sequence", 1);
    if (update.protocolVersion != kAtomicPresentationUpdateVersion ||
        !IsIdentifier(update.widgetInstanceId) ||
        !IsPresentationGeneration(update.presentationGeneration) ||
        update.sequence <= update.baseSequence ||
        source.GetNamedValue(L"operations").ValueType() != JsonValueType::Array)
        throw winrt::hresult_invalid_argument();

    const auto operations = source.GetNamedArray(L"operations");
    if (operations.Size() > kMaximumPresentationUpdateOperations)
        throw winrt::hresult_invalid_argument();
    update.operations.reserve(operations.Size());
    for (std::uint32_t index = 0; index < operations.Size(); ++index) {
        if (operations.GetAt(index).ValueType() != JsonValueType::Object)
            throw winrt::hresult_invalid_argument();
        const auto encoded = operations.GetObjectAt(index);
        if (!HasNoUnknownProperties(encoded,
                {L"kind", L"targetId", L"parentId", L"childId", L"index",
                 L"properties", L"subtree"}))
            throw winrt::hresult_invalid_argument();
        const auto kind = OptionalString(encoded, L"kind");
        WidgetPresentationUpdateOperation operation;
        if (kind == L"setProperties")
            operation.kind = WidgetPresentationUpdateOperationKind::SetProperties;
        else if (kind == L"insertChild")
            operation.kind = WidgetPresentationUpdateOperationKind::InsertChild;
        else if (kind == L"removeChild")
            operation.kind = WidgetPresentationUpdateOperationKind::RemoveChild;
        else if (kind == L"moveChild")
            operation.kind = WidgetPresentationUpdateOperationKind::MoveChild;
        else if (kind == L"replaceSubtree")
            operation.kind = WidgetPresentationUpdateOperationKind::ReplaceSubtree;
        else
            throw winrt::hresult_invalid_argument();
        operation.targetId = OptionalString(encoded, L"targetId");
        operation.parentId = OptionalString(encoded, L"parentId");
        operation.childId = OptionalString(encoded, L"childId");
        if (encoded.HasKey(L"index")) {
            operation.index = static_cast<std::size_t>(
                RequiredIntegral(encoded, L"index"));
        }
        if (encoded.HasKey(L"properties")) {
            if (encoded.GetNamedValue(L"properties").ValueType() != JsonValueType::Array)
                throw winrt::hresult_invalid_argument();
            const auto properties = encoded.GetNamedArray(L"properties");
            if (properties.Size() == 0 || properties.Size() > 64)
                throw winrt::hresult_invalid_argument();
            std::unordered_set<std::wstring> names;
            for (std::uint32_t propertyIndex = 0;
                 propertyIndex < properties.Size(); ++propertyIndex) {
                if (properties.GetAt(propertyIndex).ValueType() != JsonValueType::Object)
                    throw winrt::hresult_invalid_argument();
                const auto change = properties.GetObjectAt(propertyIndex);
                if (!HasOnlyProperties(change, {L"property", L"value"}))
                    throw winrt::hresult_invalid_argument();
                auto property = OptionalString(change, L"property");
                const bool documentProperty = IsDocumentPresentationProperty(property);
                if ((!documentProperty && !IsNodePresentationProperty(property)) ||
                    documentProperty != operation.targetId.empty() ||
                    !names.insert(property).second)
                    throw winrt::hresult_invalid_argument();
                operation.properties.push_back({
                    std::move(property),
                    std::wstring(std::wstring_view(
                        change.GetNamedValue(L"value").Stringify()))});
            }
        }
        if (encoded.HasKey(L"subtree")) {
            if (encoded.GetNamedValue(L"subtree").ValueType() != JsonValueType::Object)
                throw winrt::hresult_invalid_argument();
            operation.subtreeJson = std::wstring(std::wstring_view(
                encoded.GetNamedObject(L"subtree").Stringify()));
        }
        const bool valid = [&] {
            switch (operation.kind) {
            case WidgetPresentationUpdateOperationKind::SetProperties:
                return !operation.properties.empty() &&
                    (operation.targetId.empty() || IsIdentifier(operation.targetId)) &&
                    operation.parentId.empty() &&
                    operation.childId.empty() && !operation.index &&
                    operation.subtreeJson.empty();
            case WidgetPresentationUpdateOperationKind::InsertChild:
                return IsIdentifier(operation.parentId) && operation.index &&
                    !operation.subtreeJson.empty() && operation.targetId.empty() &&
                    operation.childId.empty() && operation.properties.empty();
            case WidgetPresentationUpdateOperationKind::RemoveChild:
                return IsIdentifier(operation.parentId) && IsIdentifier(operation.childId) &&
                    operation.targetId.empty() && !operation.index &&
                    operation.properties.empty() && operation.subtreeJson.empty();
            case WidgetPresentationUpdateOperationKind::MoveChild:
                return IsIdentifier(operation.parentId) && IsIdentifier(operation.childId) &&
                    operation.index && operation.targetId.empty() &&
                    operation.properties.empty() && operation.subtreeJson.empty();
            case WidgetPresentationUpdateOperationKind::ReplaceSubtree:
                return IsIdentifier(operation.targetId) && !operation.subtreeJson.empty() &&
                    operation.parentId.empty() && operation.childId.empty() &&
                    !operation.index && operation.properties.empty();
            }
            return false;
        }();
        if (!valid) throw winrt::hresult_invalid_argument();
        update.operations.push_back(std::move(operation));
    }
    if (payload.HasKey(L"renderStyles")) {
        if (payload.GetNamedValue(L"renderStyles").ValueType() != JsonValueType::Object)
            throw winrt::hresult_invalid_argument();
        update.renderStylesJson = std::wstring(std::wstring_view(
            payload.GetNamedObject(L"renderStyles").Stringify()));
    }
    return update;
}

bool HandleAsyncEvent(
    const JsonObject& event,
    WidgetInvalidationQueue& invalidations,
    WidgetActionFailureQueue& actionFailures,
    WidgetHostEffectQueue& hostEffects,
    PlatformAppearanceRevisionTracker& appearanceChanges,
    WidgetCatalogRevisionTracker& catalogChanges,
    std::wstring& status,
    WidgetArtworkResultQueue* artworkResults = nullptr,
    LocalWidgetPackageInstallResultQueue* localPackageInstallResults = nullptr,
    std::optional<WidgetBridgeRuntimeFailure>* runtimeFailure = nullptr) {
    if (!event.HasKey(L"type") ||
        event.GetNamedValue(L"type").ValueType() != JsonValueType::String ||
        !event.HasKey(L"payload") ||
        event.GetNamedValue(L"payload").ValueType() != JsonValueType::Object) {
        status = L"WidgetBridge returned an invalid asynchronous event.";
        return false;
    }
    const std::wstring type(std::wstring_view(event.GetNamedString(L"type")));
    const auto payload = event.GetNamedObject(L"payload");
    if (type == L"platform-appearance-changed") {
        if (!HasOnlyProperties(payload, {L"revision"}) ||
            payload.GetNamedValue(L"revision").ValueType() != JsonValueType::Number) {
            status = L"WidgetBridge appearance event has an invalid revision.";
            return false;
        }
        const double revision = payload.GetNamedNumber(L"revision");
        if (!std::isfinite(revision) || revision < 0 ||
            revision > 9'007'199'254'740'991.0 || std::floor(revision) != revision ||
            !appearanceChanges.Notify(static_cast<long long>(revision))) {
            status = L"WidgetBridge appearance event has an invalid revision.";
            return false;
        }
        status.clear();
        return true;
    }
    if (type == L"widget-catalog-changed") {
        if (!HasOnlyProperties(payload, {L"revision"}) ||
            payload.GetNamedValue(L"revision").ValueType() != JsonValueType::Number) {
            status = L"WidgetBridge catalog event has an invalid revision.";
            return false;
        }
        const double revision = payload.GetNamedNumber(L"revision");
        if (!std::isfinite(revision) || revision < 0 ||
            revision > 9'007'199'254'740'991.0 || std::floor(revision) != revision ||
            !catalogChanges.Notify(static_cast<long long>(revision))) {
            status = L"WidgetBridge catalog event has an invalid revision.";
            return false;
        }
        status.clear();
        return true;
    }
    if (type == L"local-widget-package-install-completed") {
        if (!localPackageInstallResults || !HasOnlyProperties(
                payload, {L"operationId", L"status", L"widgetId", L"version", L"message"})) {
            status = L"WidgetBridge local package result has an invalid payload.";
            return false;
        }
        const auto operationId = OptionalString(payload, L"operationId");
        const auto resultStatus = OptionalString(payload, L"status");
        const auto resultWidgetId = OptionalString(payload, L"widgetId");
        const auto version = OptionalString(payload, L"version");
        const auto message = OptionalString(payload, L"message");
        const auto mapped = resultStatus == L"installed-disabled"
            ? LocalWidgetPackageInstallStatus::InstalledDisabled
            : resultStatus == L"cancelled"
            ? LocalWidgetPackageInstallStatus::Cancelled
            : LocalWidgetPackageInstallStatus::Failed;
        const bool optionalIdentityValid =
            mapped == LocalWidgetPackageInstallStatus::InstalledDisabled
                ? IsIdentifier(resultWidgetId) && !version.empty() && version.size() <= 64
                : resultWidgetId.empty() && version.empty();
        const bool messageValid = !message.empty() && message.size() <= 512 &&
            message.find_first_of(L"\\/:") == std::wstring::npos &&
            std::none_of(message.begin(), message.end(), [](const wchar_t character) {
                return std::iswcntrl(character) != 0;
            });
        if (!IsIdentifier(operationId) ||
            (resultStatus != L"installed-disabled" && resultStatus != L"cancelled" &&
             resultStatus != L"failed") || !optionalIdentityValid || !messageValid ||
            !localPackageInstallResults->Push({
                operationId, mapped, resultWidgetId, version, message})) {
            status = L"WidgetBridge local package result could not be queued.";
            return false;
        }
        status.clear();
        return true;
    }
    const auto widgetId = OptionalString(payload, L"widgetId");
    if (!IsIdentifier(widgetId)) {
        status = L"WidgetBridge asynchronous event has an invalid widget ID.";
        return false;
    }
    if (type == L"artwork") {
        if (!artworkResults || !HasOnlyProperties(
                payload, {L"widgetId", L"artworkHandle", L"pngBase64"})) {
            status = L"WidgetBridge artwork event has an invalid payload.";
            return false;
        }
        const auto handle = OptionalString(payload, L"artworkHandle");
        const auto png = OptionalString(payload, L"pngBase64");
        if (handle.size() != 44 || !handle.starts_with(L"library.art.") ||
            !IsIdentifier(handle) || png.size() > 16'384 ||
            !artworkResults->Push({widgetId, handle, png})) {
            status = L"WidgetBridge artwork event could not be queued.";
            return false;
        }
        status.clear();
        return true;
    }
    if (type == L"widget-invalidated") {
        if (!payload.HasKey(L"revision") ||
            payload.GetNamedValue(L"revision").ValueType() != JsonValueType::Number) {
            status = L"WidgetBridge invalidation has an invalid revision.";
            return false;
        }
        const double revision = payload.GetNamedNumber(L"revision");
        if (!std::isfinite(revision) || revision < 0 ||
            revision > 9'007'199'254'740'991.0 || std::floor(revision) != revision) {
            status = L"WidgetBridge invalidation has an invalid revision.";
            return false;
        }
        if (!invalidations.Push(widgetId)) {
            status = L"WidgetBridge invalidation could not be queued.";
            return false;
        }
        status.clear();
        return true;
    }
    if (type == L"widget-host-effect") {
        if (!HasOnlyProperties(
                payload,
                {L"widgetId", L"runtimeGeneration", L"effect", L"sequence"}) ||
            payload.GetNamedValue(L"runtimeGeneration").ValueType() != JsonValueType::String ||
            payload.GetNamedValue(L"effect").ValueType() != JsonValueType::String ||
            payload.GetNamedValue(L"sequence").ValueType() != JsonValueType::Number) {
            status = L"WidgetBridge host effect has an invalid payload.";
            return false;
        }
        const auto runtimeGeneration = OptionalString(payload, L"runtimeGeneration");
        const auto effect = OptionalString(payload, L"effect");
        const double sequence = payload.GetNamedNumber(L"sequence");
        if (!IsIdentifier(runtimeGeneration) ||
            effect != L"closeOverlayAfterAppLaunch" ||
            !std::isfinite(sequence) || sequence <= 0 ||
            sequence > 9'007'199'254'740'991.0 || std::floor(sequence) != sequence ||
            !hostEffects.Push({
                static_cast<long long>(sequence),
                widgetId,
                runtimeGeneration,
                WidgetHostEffectKind::CloseOverlayAfterAppLaunch})) {
            status = L"WidgetBridge host effect is invalid.";
            return false;
        }
        status.clear();
        return true;
    }
    if (type == L"widget-failed" &&
        OptionalString(payload, L"reason") == L"controllerActionFailed") {
        if (!HasOnlyProperties(payload,
                {L"widgetId", L"runtimeGeneration", L"reason", L"actionId",
                 L"sourceElementId", L"message", L"canRestart"}) ||
            payload.GetNamedValue(L"runtimeGeneration").ValueType() != JsonValueType::String ||
            payload.GetNamedValue(L"actionId").ValueType() != JsonValueType::String ||
            payload.GetNamedValue(L"sourceElementId").ValueType() != JsonValueType::String ||
            payload.GetNamedValue(L"message").ValueType() != JsonValueType::String ||
            payload.GetNamedValue(L"canRestart").ValueType() != JsonValueType::Boolean) {
            status = L"WidgetBridge action failure has an invalid payload.";
            return false;
        }
        const auto runtimeGeneration = OptionalString(payload, L"runtimeGeneration");
        const auto actionId = OptionalString(payload, L"actionId");
        const auto sourceElementId = OptionalString(payload, L"sourceElementId");
        const auto message = OptionalString(payload, L"message");
        const bool messageIsBounded = message.size() <= 512 &&
            std::none_of(message.begin(), message.end(), [](const wchar_t character) {
                return std::iswcntrl(character) != 0;
            });
        if (!IsIdentifier(runtimeGeneration) || !IsIdentifier(actionId) ||
            !IsIdentifier(sourceElementId) || !messageIsBounded ||
            payload.GetNamedBoolean(L"canRestart") ||
            !actionFailures.Push({
                widgetId,
                runtimeGeneration,
                actionId,
                sourceElementId,
                WidgetActionFailureCode::ControllerActionFailed})) {
            status = L"WidgetBridge action failure is invalid.";
            return false;
        }
        status.clear();
        return true;
    }
    if (type == L"widget-failed") {
        if (!payload.HasKey(L"reason") ||
            payload.GetNamedValue(L"reason").ValueType() != JsonValueType::String ||
            !payload.HasKey(L"restartsUsed") ||
            payload.GetNamedValue(L"restartsUsed").ValueType() != JsonValueType::Number ||
            !payload.HasKey(L"canRestart") ||
            payload.GetNamedValue(L"canRestart").ValueType() != JsonValueType::Boolean) {
            status = L"WidgetBridge worker failure has an invalid payload.";
            return false;
        }
        const auto reason = OptionalString(payload, L"reason");
        const auto diagnostic = OptionalString(payload, L"diagnosticCode");
        if (!diagnostic.empty() && !IsIdentifier(diagnostic)) {
            status = L"WidgetBridge worker failure has an invalid diagnostic code.";
            return false;
        }
        if (runtimeFailure) {
            const auto category = reason == L"connectionFailed"
                ? WidgetBridgeRuntimeFailureCategory::WorkerStart
                : reason == L"processExited"
                    ? WidgetBridgeRuntimeFailureCategory::WorkerExited
                    : WidgetBridgeRuntimeFailureCategory::Other;
            const auto safeMessage = category ==
                    WidgetBridgeRuntimeFailureCategory::WorkerStart
                ? diagnostic.empty()
                    ? std::wstring{L"Widget worker failed to start."}
                    : L"Widget worker failed to start (" + diagnostic + L")."
                : category == WidgetBridgeRuntimeFailureCategory::WorkerExited
                    ? std::wstring{L"Widget worker exited unexpectedly."}
                    : std::wstring{L"Widget worker became unavailable."};
            *runtimeFailure = WidgetBridgeRuntimeFailure{
                widgetId,
                category,
                safeMessage,
            };
        }
        status = diagnostic.empty()
            ? L"Widget '" + widgetId + L"' worker failed; retry to start a fresh worker."
            : L"Widget '" + widgetId + L"' startup failed (" + diagnostic +
                  L"); retry to start a fresh worker.";
        return true;
    }
    status = L"WidgetBridge returned an unknown asynchronous event.";
    return false;
}

WidgetPresentationEffect ImpactForPresentationProperty(
    const std::wstring_view property) noexcept {
    using Effect = WidgetPresentationEffect;
    if (property == L"activeInputScopeId" || property == L"initialFocusId" ||
        property == L"quickActions") {
        // These document authorities can change the visible focus ring or the
        // host-owned controller guide even though they do not alter a node.
        return Effect::Paint | Effect::Authority |
            Effect::Interaction | Effect::Accessibility;
    }
    if (property == L"surface") {
        return Effect::SurfacePlacement | Effect::MeasureLayout |
            Effect::Paint | Effect::Accessibility;
    }
    if (property == L"visibleWhen") {
        return Effect::MeasureLayout | Effect::Paint |
            Effect::Interaction | Effect::Accessibility;
    }
    if (property == L"text" || property == L"textEntryValue" ||
        property == L"textEntryPlaceholder") {
        return Effect::MeasureLayout | Effect::Paint | Effect::Accessibility;
    }
    if (property == L"accessibilityLabel") {
        // Label presence determines whether an otherwise empty text node
        // contributes a renderer accessibility region.
        return Effect::Paint | Effect::Accessibility;
    }
    if (property == L"accessibilityValue") {
        return Effect::Accessibility;
    }
    if (property == L"value")
        return Effect::Paint | Effect::Accessibility;
    if (property == L"imageSource" || property == L"artworkHandle") {
        // Button intrinsic measurement reserves a leading lane whose size
        // depends on whether either resource is present.
        return Effect::Resource | Effect::MeasureLayout |
            Effect::Paint | Effect::Accessibility;
    }
    if (property == L"isDisabled" || property == L"isSelected" ||
        property == L"isBusy") {
        // Button measurement reserves a trailing state-cue lane for each of
        // these flags.
        return Effect::MeasureLayout | Effect::Paint |
            Effect::Interaction | Effect::Accessibility;
    }
    if (property == L"inputScopeId" || property == L"shortcuts") {
        // Scope changes can move visual focus; shortcut changes can alter the
        // host-owned Back affordance.
        return Effect::Paint | Effect::Authority |
            Effect::Interaction | Effect::Accessibility;
    }
    if (property == L"actionId" || property == L"valueChangedActionId" ||
        property == L"focus" || property == L"focusPersistenceId" ||
        property == L"scrollNearStartActionId" ||
        property == L"scrollNearEndActionId") {
        return Effect::Authority | Effect::Interaction | Effect::Accessibility;
    }
    if (property == L"styleClasses" ||
        property == L"gridMinimumColumnWidth" ||
        property == L"gridMaximumColumns" || property == L"minimum" ||
        property == L"maximum" || property == L"step" ||
        property == L"imageFit" ||
        property == L"glyph" || property == L"indicatorSize" ||
        property == L"actionSurfaceOrientation" ||
        property == L"scrollAxis" ||
        property == L"scrollPaginationThreshold" ||
        property == L"virtualCollectionWindow" ||
        property == L"collectionAnchorKey" ||
        property == L"collectionItemKey") {
        return Effect::MeasureLayout | Effect::Paint |
            Effect::Interaction | Effect::Accessibility;
    }
    if (property == L"sliderInteractionMode") {
        return Effect::Paint | Effect::Interaction | Effect::Accessibility;
    }
    if (property == L"textEntryMaximumLength") {
        return Effect::Authority | Effect::Interaction | Effect::Accessibility;
    }
    return Effect::Unknown;
}

WidgetPresentationImpact ClassifyPresentationImpact(
    const WidgetPresentationUpdate& update) {
    using Effect = WidgetPresentationEffect;
    WidgetPresentationImpact impact{
        update.baseSequence, update.sequence, Effect::None, {}, {}, false};
    const auto addTarget = [&](const std::wstring_view id) {
        if (id.empty() ||
            std::find(impact.affectedNodeIds.begin(),
                      impact.affectedNodeIds.end(), id) !=
                impact.affectedNodeIds.end()) {
            return;
        }
        impact.affectedNodeIds.emplace_back(id);
    };
    for (const auto& operation : update.operations) {
        if (operation.kind ==
            WidgetPresentationUpdateOperationKind::SetProperties) {
            auto operationEffects = Effect::None;
            for (const auto& change : operation.properties) {
                const auto propertyEffects =
                    ImpactForPresentationProperty(change.property);
                operationEffects |= propertyEffects;
                if (HasWidgetPresentationEffect(
                        propertyEffects, Effect::MeasureLayout)) {
                    if (change.property == L"text" ||
                        change.property == L"textEntryValue" ||
                        change.property == L"textEntryPlaceholder") {
                        if (!operation.targetId.empty() &&
                            std::find(
                                impact.textMeasurementNodeIds.begin(),
                                impact.textMeasurementNodeIds.end(),
                                operation.targetId) ==
                                impact.textMeasurementNodeIds.end()) {
                            impact.textMeasurementNodeIds.emplace_back(
                                operation.targetId);
                        }
                    } else {
                        impact.hasNonTextMeasureLayout = true;
                    }
                }
            }
            impact.effects |= operationEffects;
            addTarget(operation.targetId);
            if (operation.targetId.empty() &&
                HasWidgetPresentationEffect(
                    operationEffects, Effect::MeasureLayout)) {
                impact.effects |= Effect::SurfacePlacement;
            }
            continue;
        }
        impact.effects |= Effect::Structure | Effect::MeasureLayout |
            Effect::Paint | Effect::Interaction | Effect::Accessibility;
        impact.hasNonTextMeasureLayout = true;
        switch (operation.kind) {
        case WidgetPresentationUpdateOperationKind::InsertChild:
        case WidgetPresentationUpdateOperationKind::RemoveChild:
        case WidgetPresentationUpdateOperationKind::MoveChild:
            addTarget(operation.parentId);
            break;
        case WidgetPresentationUpdateOperationKind::ReplaceSubtree:
            addTarget(operation.targetId);
            break;
        case WidgetPresentationUpdateOperationKind::SetProperties:
            break;
        }
    }
    if (impact.effects == Effect::None) impact.effects = Effect::Unknown;
    return impact;
}

} // namespace

std::optional<WidgetPresentationMaterialization>
MaterializeWidgetPresentationUpdate(
    const WidgetSnapshot& checkpoint,
    const WidgetPresentationUpdate& update,
    const std::wstring_view expectedPresentationGeneration,
    std::wstring& error) {
    try {
        if (update.protocolVersion != kAtomicPresentationUpdateVersion ||
            checkpoint.documentJson.empty() ||
            update.widgetInstanceId != checkpoint.instanceId ||
            update.presentationGeneration != expectedPresentationGeneration ||
            update.baseSequence != checkpoint.sequence ||
            update.sequence <= update.baseSequence ||
            update.operations.size() > kMaximumPresentationUpdateOperations) {
            error = L"The widget presentation update does not match the admitted checkpoint.";
            return std::nullopt;
        }

        auto candidate = JsonObject::Parse(winrt::hstring(checkpoint.documentJson));
        if (!ValidateWidgetDocumentStructure(candidate, error)) return std::nullopt;
        for (const auto& operation : update.operations) {
            switch (operation.kind) {
            case WidgetPresentationUpdateOperationKind::SetProperties: {
                JsonObject target = candidate;
                if (!operation.targetId.empty()) {
                    const auto node = FindPresentationNode(candidate, operation.targetId);
                    if (!node) {
                        error = L"A widget presentation property target is absent.";
                        return std::nullopt;
                    }
                    target = *node;
                }
                for (const auto& change : operation.properties) {
                    const auto value = JsonValue::Parse(winrt::hstring(change.valueJson));
                    if (value.ValueType() == JsonValueType::Null)
                        target.Remove(winrt::hstring(change.property));
                    else
                        target.Insert(winrt::hstring(change.property), value);
                }
                break;
            }
            case WidgetPresentationUpdateOperationKind::InsertChild: {
                const auto parent = FindPresentationNode(candidate, operation.parentId);
                if (!parent) {
                    error = L"A widget presentation insert parent is absent.";
                    return std::nullopt;
                }
                auto children = parent->GetNamedArray(L"children");
                if (!operation.index || *operation.index > children.Size()) {
                    error = L"A widget presentation insert index is invalid.";
                    return std::nullopt;
                }
                children.InsertAt(
                    static_cast<std::uint32_t>(*operation.index),
                    JsonObject::Parse(winrt::hstring(operation.subtreeJson)));
                break;
            }
            case WidgetPresentationUpdateOperationKind::RemoveChild: {
                const auto parent = FindPresentationNode(candidate, operation.parentId);
                if (!parent) {
                    error = L"A widget presentation remove parent is absent.";
                    return std::nullopt;
                }
                auto children = parent->GetNamedArray(L"children");
                bool removed{};
                for (std::uint32_t index = 0; index < children.Size(); ++index) {
                    if (OptionalString(children.GetObjectAt(index), L"id") != operation.childId)
                        continue;
                    children.RemoveAt(index);
                    removed = true;
                    break;
                }
                if (!removed) {
                    error = L"A widget presentation remove child is absent.";
                    return std::nullopt;
                }
                break;
            }
            case WidgetPresentationUpdateOperationKind::MoveChild: {
                const auto parent = FindPresentationNode(candidate, operation.parentId);
                if (!parent || !operation.index) {
                    error = L"A widget presentation move parent is absent.";
                    return std::nullopt;
                }
                auto children = parent->GetNamedArray(L"children");
                std::optional<std::uint32_t> sourceIndex;
                JsonObject child{nullptr};
                for (std::uint32_t index = 0; index < children.Size(); ++index) {
                    auto candidateChild = children.GetObjectAt(index);
                    if (OptionalString(candidateChild, L"id") != operation.childId)
                        continue;
                    sourceIndex = index;
                    child = std::move(candidateChild);
                    break;
                }
                if (!sourceIndex) {
                    error = L"A widget presentation move child is absent.";
                    return std::nullopt;
                }
                children.RemoveAt(*sourceIndex);
                if (*operation.index > children.Size()) {
                    error = L"A widget presentation move index is invalid.";
                    return std::nullopt;
                }
                children.InsertAt(static_cast<std::uint32_t>(*operation.index), child);
                break;
            }
            case WidgetPresentationUpdateOperationKind::ReplaceSubtree: {
                auto subtree = JsonObject::Parse(winrt::hstring(operation.subtreeJson));
                if (OptionalString(candidate.GetNamedObject(L"root"), L"id") ==
                    operation.targetId) {
                    candidate.Insert(L"root", subtree);
                    break;
                }
                const auto parent = FindPresentationNodeParent(candidate, operation.targetId);
                if (!parent) {
                    error = L"A widget presentation replacement target is absent.";
                    return std::nullopt;
                }
                parent->children.SetAt(parent->index, subtree);
                break;
            }
            }
            // Bound every intermediate document before a later operation can
            // search or transform it.
            if (!ValidateWidgetDocumentStructure(candidate, error)) return std::nullopt;
        }

        candidate.Insert(L"protocolVersion", JsonValue::CreateNumberValue(
            static_cast<double>(std::max(
                checkpoint.protocolVersion, kAtomicPresentationUpdateVersion))));
        candidate.Insert(L"sequence", JsonValue::CreateNumberValue(
            static_cast<double>(update.sequence)));
        if (winrt::to_string(candidate.Stringify()).size() > kMaximumFrameBytes ||
            !ValidateWidgetDocumentStructure(candidate, error)) {
            if (error.empty()) error = L"The materialized widget document is too large.";
            return std::nullopt;
        }
        auto materialized = ParseSnapshot(candidate);
        if (!update.renderStylesJson.empty()) {
            ApplyComputedStyles(materialized, JsonObject::Parse(
                winrt::hstring(update.renderStylesJson)));
        }
        auto impact = ClassifyPresentationImpact(update);
        error.clear();
        return WidgetPresentationMaterialization{
            std::move(materialized), std::move(impact)};
    } catch (const winrt::hresult_error& exception) {
        error = L"The widget presentation update is invalid: " +
            std::wstring(exception.message());
        return std::nullopt;
    }
}

bool WidgetInvalidationQueue::Push(std::wstring widgetId) {
    if (!IsIdentifier(widgetId)) return false;
    if (known_.contains(widgetId)) return true;
    if (queued_.size() == MaximumWidgetIds) {
        known_.erase(queued_.front());
        queued_.erase(queued_.begin());
    }
    known_.emplace(widgetId);
    queued_.push_back(std::move(widgetId));
    return true;
}

bool WidgetActionFailureQueue::Push(WidgetActionFailure failure) {
    if (!IsIdentifier(failure.widgetId) ||
        !IsIdentifier(failure.runtimeGeneration) ||
        !IsIdentifier(failure.actionId) ||
        !IsIdentifier(failure.sourceElementId)) return false;
    if (queued_.size() == MaximumFailures) queued_.erase(queued_.begin());
    queued_.push_back(std::move(failure));
    return true;
}

std::vector<WidgetActionFailure> WidgetActionFailureQueue::Take() noexcept {
    return std::exchange(queued_, {});
}

bool WidgetHostEffectQueue::Push(WidgetHostEffect effect) {
    if (effect.sequence <= 0 || !IsIdentifier(effect.widgetId) ||
        !IsIdentifier(effect.runtimeGeneration) ||
        effect.kind != WidgetHostEffectKind::CloseOverlayAfterAppLaunch) {
        return false;
    }
    if (effect.sequence <= lastSequence_) return true;
    lastSequence_ = effect.sequence;
    if (queued_.size() == MaximumEffects) queued_.erase(queued_.begin());
    queued_.push_back(std::move(effect));
    return true;
}

std::vector<WidgetHostEffect> WidgetHostEffectQueue::Take() noexcept {
    return std::exchange(queued_, {});
}

void WidgetHostEffectQueue::Reset() noexcept {
    lastSequence_ = 0;
    queued_.clear();
}

bool WidgetArtworkResultQueue::Push(WidgetArtworkResult result) {
    const auto existing = std::find_if(
        queued_.begin(), queued_.end(), [&](const WidgetArtworkResult& item) {
            return item.widgetId == result.widgetId &&
                   item.artworkHandle == result.artworkHandle;
        });
    if (existing != queued_.end()) *existing = std::move(result);
    else {
        if (queued_.size() >= MaximumResults) return false;
        queued_.push_back(std::move(result));
    }
    return true;
}

std::vector<WidgetArtworkResult> WidgetArtworkResultQueue::Take() noexcept {
    auto result = std::move(queued_);
    queued_.clear();
    return result;
}

bool LocalWidgetPackageInstallResultQueue::Push(LocalWidgetPackageInstallResult result) {
    if (!IsIdentifier(result.operationId) || result.safeMessage.empty() ||
        result.safeMessage.size() > 512) return false;
    const auto existing = std::find_if(
        queued_.begin(), queued_.end(), [&](const LocalWidgetPackageInstallResult& item) {
            return item.operationId == result.operationId;
        });
    if (existing != queued_.end()) *existing = std::move(result);
    else {
        if (queued_.size() >= MaximumPending) return false;
        queued_.push_back(std::move(result));
    }
    return true;
}

std::vector<LocalWidgetPackageInstallResult>
LocalWidgetPackageInstallResultQueue::Take() noexcept {
    return std::exchange(queued_, {});
}

void LocalWidgetPackageInstallResultQueue::Reset() noexcept {
    queued_.clear();
}

std::vector<std::wstring> ChangedWidgetRuntimeIds(
    const std::vector<WidgetDescriptor>& before,
    const std::vector<WidgetDescriptor>& after) {
    std::vector<std::wstring> changed;
    changed.reserve(before.size());
    for (const auto& prior : before) {
        const auto current = std::find_if(
            after.begin(), after.end(), [&](const WidgetDescriptor& candidate) {
                return candidate.id == prior.id;
            });
        if (current == after.end() || current->instanceId != prior.instanceId ||
            current->runtimeGeneration != prior.runtimeGeneration) {
            changed.push_back(prior.id);
        }
    }
    return changed;
}

std::vector<std::wstring> WidgetInvalidationQueue::Take() noexcept {
    known_.clear();
    return std::exchange(queued_, {});
}

bool PlatformAppearanceRevisionTracker::Notify(const long long revision) noexcept {
    if (revision < 0 || revision > 9'007'199'254'740'991LL) return false;
    if (!pending_ || revision > *pending_) pending_ = revision;
    return true;
}

std::optional<long long> PlatformAppearanceRevisionTracker::Take() noexcept {
    return std::exchange(pending_, std::nullopt);
}

bool WidgetCatalogRevisionTracker::Notify(const long long revision) noexcept {
    if (revision < 0 || revision > 9'007'199'254'740'991LL) return false;
    if (revision <= observed_) return true;
    if (inFlight_ && revision <= *inFlight_) return true;
    if (!pending_ || revision > *pending_) pending_ = revision;
    return true;
}

bool WidgetCatalogRevisionTracker::ObserveSnapshot(const long long revision) noexcept {
    if (revision < 0 || revision > 9'007'199'254'740'991LL) return false;
    if (revision < observed_ || (inFlight_ && revision < *inFlight_)) return false;
    if (revision > observed_) observed_ = revision;
    if (inFlight_ && *inFlight_ <= observed_) inFlight_.reset();
    if (pending_ && *pending_ <= observed_) pending_.reset();
    return true;
}

std::optional<long long> WidgetCatalogRevisionTracker::Take() noexcept {
    if (inFlight_ || !pending_) return std::nullopt;
    inFlight_ = std::exchange(pending_, std::nullopt);
    return inFlight_;
}

void WidgetCatalogRevisionTracker::Retry() noexcept {
    if (!inFlight_) return;
    if (!pending_ || *inFlight_ > *pending_) pending_ = *inFlight_;
    inFlight_.reset();
}

void WidgetCatalogRevisionTracker::Abandon() noexcept {
    inFlight_.reset();
}

void WidgetCatalogRevisionTracker::Reset() noexcept {
    observed_ = 0;
    pending_.reset();
    inFlight_.reset();
}

bool PlatformAppearanceState::Publish(PlatformAppearance appearance) {
    if (appearance.revision < 0 ||
        (current_ && appearance.revision <= current_->revision)) {
        return false;
    }
    current_ = std::move(appearance);
    return true;
}

WidgetBridgeClient::~WidgetBridgeClient() {
    Stop();
}

bool WidgetBridgeClient::EnsureStarted(
    const std::wstring& installationDirectory,
    const std::wstring& installedCatalogRoot) {
    std::scoped_lock lock(requestMutex_);
    if (pipe_ != INVALID_HANDLE_VALUE && !transportTainted_) {
        return true;
    }
    if (pipe_ != INVALID_HANDLE_VALUE || process_) CloseTransport();
    lastError_.clear();
    if (Launch(installationDirectory, installedCatalogRoot) && Connect()) return true;
    CloseTransport();
    return false;
}

bool WidgetBridgeClient::Launch(
    const std::wstring& installationDirectory,
    const std::wstring& installedCatalogRoot) {
    const std::filesystem::path root(installationDirectory);
    const auto executable = root / L"runtime" / L"Bridge" / L"WidgetBridge.exe";
    const auto catalog = root / L"widget-catalog.json";
    if (!std::filesystem::is_regular_file(executable) || !std::filesystem::is_regular_file(catalog)) {
        Fail(L"Widget runtime files are missing. Rebuild OverlayHost to package them.");
        return false;
    }

    pipeName_ = L"wrail-host-" + std::to_wstring(GetCurrentProcessId()) + L"-" +
                std::to_wstring(GetTickCount64());
    const auto bridgeSessionGeneration = ++bridgeSessionGeneration_;
    std::wstring command = Quote(executable) + L" --host-pipe " + pipeName_ +
                           L" --catalog " + Quote(catalog) +
                           L" --accept-timeout-ms 10000 --bridge-session-generation " +
                           std::to_wstring(bridgeSessionGeneration);
    if (!installedCatalogRoot.empty()) {
        command += L" --installed-catalog-root " + Quote(installedCatalogRoot);
        wchar_t localAppData[MAX_PATH + 1]{};
        const DWORD length = GetEnvironmentVariableW(
            L"LOCALAPPDATA", localAppData, MAX_PATH);
        if (length == 0 || length > MAX_PATH) {
            Fail(L"Development WidgetBridge launch could not resolve LOCALAPPDATA.");
            return false;
        }
        const auto settingsRoot =
            std::filesystem::path(localAppData) / L"WidgetRail";
        command += L" --settings-root " + Quote(settingsRoot);
    }
    STARTUPINFOW startup{sizeof(startup)};
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE,
                        CREATE_NO_WINDOW, nullptr, root.c_str(), &startup, &process)) {
        Fail(Win32Message(L"CreateProcessW(WidgetBridge)", GetLastError()));
        return false;
    }
    CloseHandle(process.hThread);
    process_ = process.hProcess;
    processId_ = process.dwProcessId;
    return true;
}

bool WidgetBridgeClient::Connect() {
    const std::wstring fullName = L"\\\\.\\pipe\\" + pipeName_;
    const ULONGLONG deadline = GetTickCount64() + 5000;
    while (GetTickCount64() < deadline) {
        pipe_ = CreateFileW(fullName.c_str(), GENERIC_READ | GENERIC_WRITE, 0, nullptr,
                            OPEN_EXISTING, 0, nullptr);
        if (pipe_ != INVALID_HANDLE_VALUE) {
            break;
        }
        if (process_ && WaitForSingleObject(process_, 0) == WAIT_OBJECT_0) {
            Fail(L"WidgetBridge exited before accepting the host connection.");
            return false;
        }
        std::this_thread::sleep_for(std::chrono::milliseconds(20));
    }
    if (pipe_ == INVALID_HANDLE_VALUE) {
        Fail(Win32Message(L"Connect to WidgetBridge", GetLastError()));
        return false;
    }

    JsonObject payload;
    payload.Insert(L"clientName", JsonValue::CreateStringValue(L"OverlayHost"));
    const auto response = [&]() -> std::optional<JsonObject> {
        const long long requestId = ++nextRequestId_;
        JsonObject envelope;
        envelope.Insert(L"protocolVersion", JsonValue::CreateNumberValue(1));
        envelope.Insert(L"type", JsonValue::CreateStringValue(L"hello"));
        envelope.Insert(L"requestId", JsonValue::CreateNumberValue(static_cast<double>(requestId)));
        envelope.Insert(L"payload", payload);
        if (!WriteFrame(winrt::to_string(envelope.Stringify()))) return std::nullopt;
        const auto frame = ReadFrame();
        if (!frame) return std::nullopt;
        return JsonObject::Parse(winrt::to_hstring(*frame));
    }();
    if (!response || response->GetNamedString(L"type") != L"hello-accepted") {
        Fail(L"WidgetBridge rejected the protocol handshake.");
        return false;
    }
    return true;
}

void WidgetBridgeClient::Stop() noexcept {
    std::scoped_lock lock(requestMutex_);
    CloseTransport();
    (void)invalidations_.Take();
    (void)actionFailures_.Take();
    hostEffects_.Reset();
    artworkResults_.Reset();
    localPackageInstallResults_.Reset();
    (void)appearanceChanges_.Take();
    catalogChanges_.Reset();
}

void WidgetBridgeClient::CloseTransport() noexcept {
    if (pipe_ != INVALID_HANDLE_VALUE) {
        CloseHandle(pipe_);
        pipe_ = INVALID_HANDLE_VALUE;
    }
    if (process_) {
        if (WaitForSingleObject(process_, 1000) == WAIT_TIMEOUT) {
            (void)TerminateProcess(process_, ERROR_OPERATION_ABORTED);
            (void)WaitForSingleObject(process_, 1000);
        }
        CloseHandle(process_);
        process_ = nullptr;
    }
    processId_ = 0;
    pipeName_.clear();
    nextRequestId_ = 0;
    transportTainted_ = false;
}

std::optional<std::vector<WidgetDescriptor>> WidgetBridgeClient::ListWidgets() {
    std::scoped_lock lock(requestMutex_);
    if (pipe_ == INVALID_HANDLE_VALUE) return std::nullopt;
    try {
        const long long requestId = ++nextRequestId_;
        JsonObject envelope;
        envelope.Insert(L"protocolVersion", JsonValue::CreateNumberValue(1));
        envelope.Insert(L"type", JsonValue::CreateStringValue(L"list-widgets"));
        envelope.Insert(L"requestId", JsonValue::CreateNumberValue(static_cast<double>(requestId)));
        envelope.Insert(L"payload", JsonObject{});
        if (!WriteFrame(winrt::to_string(envelope.Stringify()))) return std::nullopt;

        while (const auto frame = ReadFrame()) {
            const auto response = JsonObject::Parse(winrt::to_hstring(*frame));
            if (!response.HasKey(L"protocolVersion") ||
                response.GetNamedValue(L"protocolVersion").ValueType() != JsonValueType::Number ||
                response.GetNamedNumber(L"protocolVersion") != 1) {
                Fail(L"WidgetBridge returned an unsupported protocol version.");
                return std::nullopt;
            }
            if (!response.HasKey(L"type") ||
                response.GetNamedValue(L"type").ValueType() != JsonValueType::String) {
                Fail(L"WidgetBridge returned a response without a valid type.");
                return std::nullopt;
            }
            const std::wstring type(std::wstring_view(response.GetNamedString(L"type")));
            if (type.empty() || type.size() > 64) {
                Fail(L"WidgetBridge returned an invalid response type.");
                return std::nullopt;
            }
            long long responseId = 0;
            if (!ReadRequestId(response, responseId)) {
                Fail(L"WidgetBridge returned an invalid request ID.");
                return std::nullopt;
            }
            if (responseId == 0) {
                std::wstring status;
                if (!HandleAsyncEvent(response, invalidations_, actionFailures_, hostEffects_, appearanceChanges_, catalogChanges_, status, &artworkResults_, &localPackageInstallResults_)) {
                    Fail(std::move(status));
                    return std::nullopt;
                }
                if (!status.empty()) lastError_ = std::move(status);
                continue;
            }
            if (responseId != requestId) {
                Fail(L"WidgetBridge returned a mismatched request ID.");
                return std::nullopt;
            }
            if (type == L"error") {
                Fail(SafeBridgeError(response));
                return std::nullopt;
            }
            if (type != L"widgets") {
                Fail(L"WidgetBridge returned an unexpected catalog response.");
                return std::nullopt;
            }
            if (!response.HasKey(L"payload") ||
                response.GetNamedValue(L"payload").ValueType() != JsonValueType::Object) {
                Fail(L"WidgetBridge returned a widgets response without a valid payload.");
                return std::nullopt;
            }
            const auto payload = response.GetNamedObject(L"payload");
            if (!payload.HasKey(L"revision") ||
                payload.GetNamedValue(L"revision").ValueType() != JsonValueType::Number) {
                Fail(L"WidgetBridge widgets response has an invalid catalog revision.");
                return std::nullopt;
            }
            const double revision = payload.GetNamedNumber(L"revision");
            if (!std::isfinite(revision) || revision < 0 ||
                revision > 9'007'199'254'740'991.0 || std::floor(revision) != revision) {
                Fail(L"WidgetBridge widgets response has an invalid catalog revision.");
                return std::nullopt;
            }
            std::wstring parseError;
            auto descriptors = ParseWidgetDescriptors(payload, parseError);
            if (!descriptors) {
                Fail(std::move(parseError));
                return std::nullopt;
            }
            if (!catalogChanges_.ObserveSnapshot(static_cast<long long>(revision))) {
                Fail(L"WidgetBridge widgets response is older than the requested catalog revision.");
                return std::nullopt;
            }
            lastError_.clear();
            return descriptors;
        }
    } catch (const winrt::hresult_error& error) {
        Fail(L"Invalid WidgetBridge catalog JSON: " + std::wstring(error.message()));
    }
    return std::nullopt;
}

std::optional<PlatformAppearance> WidgetBridgeClient::GetPlatformAppearance() {
    std::scoped_lock lock(requestMutex_);
    if (pipe_ == INVALID_HANDLE_VALUE) return std::nullopt;
    try {
        const long long requestId = ++nextRequestId_;
        JsonObject envelope;
        envelope.Insert(L"protocolVersion", JsonValue::CreateNumberValue(1));
        envelope.Insert(L"type", JsonValue::CreateStringValue(L"get-platform-appearance"));
        envelope.Insert(L"requestId", JsonValue::CreateNumberValue(static_cast<double>(requestId)));
        envelope.Insert(L"payload", JsonObject{});
        if (!WriteFrame(winrt::to_string(envelope.Stringify()))) return std::nullopt;

        while (const auto frame = ReadFrame()) {
            const auto response = JsonObject::Parse(winrt::to_hstring(*frame));
            if (!response.HasKey(L"protocolVersion") ||
                response.GetNamedValue(L"protocolVersion").ValueType() != JsonValueType::Number ||
                response.GetNamedNumber(L"protocolVersion") != 1 ||
                !response.HasKey(L"type") ||
                response.GetNamedValue(L"type").ValueType() != JsonValueType::String) {
                Fail(L"WidgetBridge returned an invalid platform appearance response envelope.");
                return std::nullopt;
            }
            const std::wstring type(std::wstring_view(response.GetNamedString(L"type")));
            if (type.empty() || type.size() > 64) {
                Fail(L"WidgetBridge returned an invalid platform appearance response type.");
                return std::nullopt;
            }
            long long responseId = 0;
            if (!ReadRequestId(response, responseId)) {
                Fail(L"WidgetBridge returned an invalid platform appearance request ID.");
                return std::nullopt;
            }
            if (responseId == 0) {
                std::wstring status;
                if (!HandleAsyncEvent(response, invalidations_, actionFailures_, hostEffects_, appearanceChanges_, catalogChanges_, status, &artworkResults_, &localPackageInstallResults_)) {
                    Fail(std::move(status));
                    return std::nullopt;
                }
                if (!status.empty()) lastError_ = std::move(status);
                continue;
            }
            if (responseId != requestId) {
                Fail(L"WidgetBridge returned a mismatched platform appearance request ID.");
                return std::nullopt;
            }
            if (type == L"error") {
                Fail(SafeBridgeError(response));
                return std::nullopt;
            }
            if (type != L"platform-appearance" || !response.HasKey(L"payload") ||
                response.GetNamedValue(L"payload").ValueType() != JsonValueType::Object) {
                Fail(L"WidgetBridge returned an unexpected platform appearance response.");
                return std::nullopt;
            }
            std::wstring parseError;
            auto appearance = ParsePlatformAppearance(
                response.GetNamedObject(L"payload"), parseError);
            if (!appearance) {
                Fail(std::move(parseError));
                return std::nullopt;
            }
            lastError_.clear();
            return appearance;
        }
    } catch (const winrt::hresult_error& error) {
        Fail(L"Invalid WidgetBridge platform appearance JSON: " +
             std::wstring(error.message()));
    }
    return std::nullopt;
}

std::optional<bool> WidgetBridgeClient::SetWidgetLifecycle(
    const std::wstring_view widgetId,
    const std::wstring_view state) {
    std::scoped_lock lock(requestMutex_);
    const bool validState = state == L"background" || state == L"visible" ||
                            state == L"interactive";
    if (pipe_ == INVALID_HANDLE_VALUE || widgetId.empty() ||
        widgetId.size() > kMaximumIdentifierLength || !IsIdentifier(widgetId) ||
        !validState) {
        if (pipe_ != INVALID_HANDLE_VALUE) Fail(L"Widget lifecycle request is invalid.");
        return std::nullopt;
    }
    try {
        JsonObject payload;
        payload.Insert(L"widgetId", JsonValue::CreateStringValue(winrt::hstring(widgetId)));
        payload.Insert(L"state", JsonValue::CreateStringValue(winrt::hstring(state)));
        const long long requestId = ++nextRequestId_;
        JsonObject envelope;
        envelope.Insert(L"protocolVersion", JsonValue::CreateNumberValue(1));
        envelope.Insert(L"type", JsonValue::CreateStringValue(L"set-widget-lifecycle"));
        envelope.Insert(L"requestId", JsonValue::CreateNumberValue(static_cast<double>(requestId)));
        envelope.Insert(L"payload", payload);
        if (!WriteFrame(winrt::to_string(envelope.Stringify()))) return std::nullopt;

        while (const auto frame = ReadFrame()) {
            const auto response = JsonObject::Parse(winrt::to_hstring(*frame));
            long long responseId = 0;
            if (!ReadRequestId(response, responseId)) {
                Fail(L"WidgetBridge returned an invalid lifecycle request ID.");
                return std::nullopt;
            }
            const auto type = response.GetNamedString(L"type");
            if (responseId == 0) {
                std::wstring status;
                if (!HandleAsyncEvent(response, invalidations_, actionFailures_, hostEffects_, appearanceChanges_, catalogChanges_, status, &artworkResults_, &localPackageInstallResults_)) {
                    Fail(std::move(status));
                    return std::nullopt;
                }
                if (!status.empty()) lastError_ = std::move(status);
                continue;
            }
            if (responseId != requestId) {
                Fail(L"WidgetBridge returned a mismatched lifecycle request ID.");
                return std::nullopt;
            }
            if (type == L"error") {
                Fail(SafeBridgeError(response));
                return std::nullopt;
            }
            if (type != L"acknowledged") {
                Fail(L"WidgetBridge returned an unexpected lifecycle response.");
                return std::nullopt;
            }
            return true;
        }
    } catch (const winrt::hresult_error& error) {
        Fail(L"Invalid WidgetBridge lifecycle JSON: " + std::wstring(error.message()));
    }
    return std::nullopt;
}

std::optional<WidgetPresentationPublication>
WidgetBridgeClient::EstablishWidgetPresentation(
    const std::wstring_view widgetId,
    const std::wstring_view state,
    const long long baseSequence,
    const WidgetPresentationTransactionKind transactionKind,
    const long long recoveryOriginSequence) {
    std::scoped_lock lock(requestMutex_);
    lastRuntimeFailure_.reset();
    lastRequestFailureCategory_ = WidgetBridgeRequestFailureCategory::None;
    const bool validState = state == L"visible" || state == L"interactive";
    if (pipe_ == INVALID_HANDLE_VALUE || widgetId.empty() ||
        widgetId.size() > kMaximumIdentifierLength || !IsIdentifier(widgetId) ||
        !validState || !ValidPresentationTransaction(
            transactionKind, baseSequence, recoveryOriginSequence)) {
        if (pipe_ != INVALID_HANDLE_VALUE)
            Fail(L"Widget presentation establishment request is invalid.");
        return std::nullopt;
    }
    try {
        JsonObject payload;
        payload.Insert(L"widgetId", JsonValue::CreateStringValue(winrt::hstring(widgetId)));
        payload.Insert(L"state", JsonValue::CreateStringValue(winrt::hstring(state)));
        JsonObject presentation;
        const bool incremental = transactionKind ==
            WidgetPresentationTransactionKind::IncrementalUpdate;
        JsonObject capabilities;
        capabilities.Insert(L"maximumProtocolVersion", JsonValue::CreateNumberValue(
            incremental ? kAtomicPresentationUpdateVersion : 0));
        capabilities.Insert(L"maximumOperationsPerBatch", JsonValue::CreateNumberValue(
            incremental ? static_cast<double>(kMaximumPresentationUpdateOperations) : 0));
        capabilities.Insert(L"maximumBatchBytes", JsonValue::CreateNumberValue(
            incremental ? static_cast<double>(kMaximumPresentationUpdateBytes) : 0));
        presentation.Insert(L"capabilities", capabilities);
        presentation.Insert(L"baseSequence", JsonValue::CreateNumberValue(
            static_cast<double>(baseSequence)));
        presentation.Insert(L"transactionKind", JsonValue::CreateStringValue(
            winrt::hstring(PresentationTransactionKindName(transactionKind))));
        presentation.Insert(L"recoveryOriginSequence", JsonValue::CreateNumberValue(
            static_cast<double>(recoveryOriginSequence)));
        payload.Insert(L"presentation", presentation);
        const long long requestId = ++nextRequestId_;
        JsonObject envelope;
        envelope.Insert(L"protocolVersion", JsonValue::CreateNumberValue(1));
        envelope.Insert(L"type", JsonValue::CreateStringValue(L"set-widget-lifecycle"));
        envelope.Insert(L"requestId", JsonValue::CreateNumberValue(
            static_cast<double>(requestId)));
        envelope.Insert(L"payload", payload);
        if (!WriteFrame(winrt::to_string(envelope.Stringify()))) return std::nullopt;

        while (const auto frame = ReadFrame()) {
            const auto response = JsonObject::Parse(winrt::to_hstring(*frame));
            long long responseId = 0;
            if (!ReadRequestId(response, responseId)) {
                Fail(L"WidgetBridge returned an invalid presentation request ID.");
                return std::nullopt;
            }
            if (responseId == 0) {
                std::wstring status;
                if (!HandleAsyncEvent(
                        response, invalidations_, actionFailures_, hostEffects_,
                        appearanceChanges_, catalogChanges_, status, &artworkResults_,
                        &localPackageInstallResults_,
                        &lastRuntimeFailure_)) {
                    Fail(std::move(status));
                    return std::nullopt;
                }
                if (!status.empty()) lastError_ = std::move(status);
                continue;
            }
            if (responseId != requestId) {
                Fail(L"WidgetBridge returned a mismatched presentation request ID.");
                return std::nullopt;
            }
            const auto type = response.GetNamedString(L"type");
            if (type == L"error") {
                lastRequestFailureCategory_ =
                    BridgeRequestFailureCategoryFromError(response);
                Fail(SafeBridgeError(response));
                return std::nullopt;
            }
            if ((type != L"snapshot" &&
                 !(incremental && type == L"presentation-update")) ||
                !response.HasKey(L"payload") ||
                response.GetNamedValue(L"payload").ValueType() != JsonValueType::Object) {
                Fail(L"WidgetBridge returned an unexpected presentation response.");
                return std::nullopt;
            }
            const auto responsePayload = response.GetNamedObject(L"payload");
            if (OptionalString(responsePayload, L"widgetId") != widgetId) {
                Fail(L"WidgetBridge established presentation for a different widget ID.");
                return std::nullopt;
            }
            const auto responseTransactionKind =
                ParsePresentationTransactionKind(responsePayload);
            if (!responseTransactionKind || *responseTransactionKind != transactionKind) {
                Fail(L"WidgetBridge returned a different presentation transaction kind.");
                return std::nullopt;
            }
            const auto responseBaseSequence = RequiredIntegral(
                responsePayload, L"baseSequence");
            const auto responseRecoveryOriginSequence = RequiredIntegral(
                responsePayload, L"recoveryOriginSequence");
            if (responseBaseSequence != baseSequence ||
                responseRecoveryOriginSequence != recoveryOriginSequence) {
                Fail(L"WidgetBridge returned different presentation transaction authority.");
                return std::nullopt;
            }
            if (type == L"presentation-update") {
                WidgetPresentationPublication publication;
                publication.transactionKind = transactionKind;
                publication.requestBaseSequence = baseSequence;
                publication.recoveryOriginSequence = recoveryOriginSequence;
                publication.update = ParsePresentationUpdatePayload(
                    responsePayload, true);
                lastRuntimeFailure_.reset();
                lastError_.clear();
                return publication;
            }
            auto snapshot = ParseStyledSnapshotPayload(responsePayload);
            lastRuntimeFailure_.reset();
            lastError_.clear();
            WidgetPresentationPublication publication;
            publication.transactionKind = transactionKind;
            publication.requestBaseSequence = baseSequence;
            publication.recoveryOriginSequence = recoveryOriginSequence;
            publication.checkpoint = std::move(snapshot);
            return publication;
        }
    } catch (const winrt::hresult_error& error) {
        Fail(L"Invalid WidgetBridge presentation JSON: " +
             std::wstring(error.message()));
    }
    return std::nullopt;
}

std::optional<bool> WidgetBridgeClient::RestartWidget(
    const std::wstring_view widgetId) {
    std::scoped_lock lock(requestMutex_);
    if (pipe_ == INVALID_HANDLE_VALUE || widgetId.empty() ||
        widgetId.size() > kMaximumIdentifierLength || !IsIdentifier(widgetId)) {
        if (pipe_ != INVALID_HANDLE_VALUE) Fail(L"Widget restart request is invalid.");
        return std::nullopt;
    }
    try {
        JsonObject payload;
        payload.Insert(L"widgetId", JsonValue::CreateStringValue(winrt::hstring(widgetId)));
        const long long requestId = ++nextRequestId_;
        JsonObject envelope;
        envelope.Insert(L"protocolVersion", JsonValue::CreateNumberValue(1));
        envelope.Insert(L"type", JsonValue::CreateStringValue(L"restart-widget"));
        envelope.Insert(L"requestId", JsonValue::CreateNumberValue(static_cast<double>(requestId)));
        envelope.Insert(L"payload", payload);
        if (!WriteFrame(winrt::to_string(envelope.Stringify()))) return std::nullopt;

        while (const auto frame = ReadFrame()) {
            const auto response = JsonObject::Parse(winrt::to_hstring(*frame));
            long long responseId = 0;
            if (!ReadRequestId(response, responseId)) {
                Fail(L"WidgetBridge returned an invalid restart request ID.");
                return std::nullopt;
            }
            const auto type = response.GetNamedString(L"type");
            if (responseId == 0) {
                std::wstring status;
                if (!HandleAsyncEvent(response, invalidations_, actionFailures_, hostEffects_,
                                      appearanceChanges_, catalogChanges_, status, &artworkResults_,
                                      &localPackageInstallResults_)) {
                    Fail(std::move(status));
                    return std::nullopt;
                }
                if (!status.empty()) lastError_ = std::move(status);
                continue;
            }
            if (responseId != requestId) {
                Fail(L"WidgetBridge returned a mismatched restart request ID.");
                return std::nullopt;
            }
            if (type == L"error") {
                Fail(SafeBridgeError(response));
                return std::nullopt;
            }
            if (type != L"acknowledged" || !response.HasKey(L"payload") ||
                response.GetNamedValue(L"payload").ValueType() != JsonValueType::Object) {
                Fail(L"WidgetBridge returned an unexpected restart response.");
                return std::nullopt;
            }
            const auto acknowledgement = response.GetNamedObject(L"payload");
            if (OptionalString(acknowledgement, L"widgetId") != widgetId) {
                Fail(L"WidgetBridge acknowledged restart for a different widget ID.");
                return std::nullopt;
            }
            const auto state = OptionalString(acknowledgement, L"state");
            if (state != L"background" && state != L"visible" &&
                state != L"interactive") {
                Fail(L"WidgetBridge returned an invalid restored lifecycle state.");
                return std::nullopt;
            }
            lastError_.clear();
            return true;
        }
    } catch (const winrt::hresult_error& error) {
        Fail(L"Invalid WidgetBridge restart JSON: " + std::wstring(error.message()));
    }
    return std::nullopt;
}

std::optional<WidgetPresentationPublication> WidgetBridgeClient::GetSnapshot(
    const std::wstring_view widgetId,
    const long long baseSequence,
    const WidgetPresentationTransactionKind transactionKind,
    const long long recoveryOriginSequence) {
    std::scoped_lock lock(requestMutex_);
    lastRequestFailureCategory_ = WidgetBridgeRequestFailureCategory::None;
    if (pipe_ == INVALID_HANDLE_VALUE || !ValidPresentationTransaction(
            transactionKind, baseSequence, recoveryOriginSequence)) return std::nullopt;
    try {
        JsonObject payload;
        payload.Insert(L"widgetId", JsonValue::CreateStringValue(winrt::hstring(widgetId)));
        const bool incremental = transactionKind ==
            WidgetPresentationTransactionKind::IncrementalUpdate;
        if (incremental) {
            JsonObject capabilities;
            capabilities.Insert(L"maximumProtocolVersion",
                JsonValue::CreateNumberValue(kAtomicPresentationUpdateVersion));
            capabilities.Insert(L"maximumOperationsPerBatch",
                JsonValue::CreateNumberValue(
                    static_cast<double>(kMaximumPresentationUpdateOperations)));
            capabilities.Insert(L"maximumBatchBytes",
                JsonValue::CreateNumberValue(
                    static_cast<double>(kMaximumPresentationUpdateBytes)));
            payload.Insert(L"capabilities", capabilities);
            payload.Insert(L"baseSequence", JsonValue::CreateNumberValue(
                static_cast<double>(baseSequence)));
        }
        payload.Insert(L"transactionKind", JsonValue::CreateStringValue(
            winrt::hstring(PresentationTransactionKindName(transactionKind))));
        payload.Insert(L"recoveryOriginSequence", JsonValue::CreateNumberValue(
            static_cast<double>(recoveryOriginSequence)));
        const long long requestId = ++nextRequestId_;
        JsonObject envelope;
        envelope.Insert(L"protocolVersion", JsonValue::CreateNumberValue(1));
        envelope.Insert(L"type", JsonValue::CreateStringValue(L"get-snapshot"));
        envelope.Insert(L"requestId", JsonValue::CreateNumberValue(static_cast<double>(requestId)));
        envelope.Insert(L"payload", payload);
        if (!WriteFrame(winrt::to_string(envelope.Stringify()))) return std::nullopt;
        while (const auto frame = ReadFrame()) {
            const auto response = JsonObject::Parse(winrt::to_hstring(*frame));
            const auto responseId = static_cast<long long>(response.GetNamedNumber(L"requestId"));
            if (responseId == 0) {
                std::wstring status;
                if (!HandleAsyncEvent(response, invalidations_, actionFailures_, hostEffects_, appearanceChanges_, catalogChanges_, status, &artworkResults_, &localPackageInstallResults_)) {
                    Fail(std::move(status));
                    return std::nullopt;
                }
                if (!status.empty()) lastError_ = std::move(status);
                continue;
            }
            if (responseId != requestId) {
                Fail(L"WidgetBridge returned a mismatched request ID.");
                return std::nullopt;
            }
            const auto type = response.GetNamedString(L"type");
            if (type == L"error") {
                lastRequestFailureCategory_ =
                    BridgeRequestFailureCategoryFromError(response);
                Fail(SafeBridgeError(response));
                return std::nullopt;
            }
            if (type != L"snapshot" &&
                !(incremental && type == L"presentation-update")) {
                Fail(L"WidgetBridge returned an unexpected snapshot response.");
                return std::nullopt;
            }
            const auto responsePayload = response.GetNamedObject(L"payload");
            if (OptionalString(responsePayload, L"widgetId") != widgetId) {
                Fail(L"WidgetBridge returned a snapshot for a different widget ID.");
                return std::nullopt;
            }
            const auto responseTransactionKind =
                ParsePresentationTransactionKind(responsePayload);
            if (!responseTransactionKind || *responseTransactionKind != transactionKind) {
                Fail(L"WidgetBridge returned a different presentation transaction kind.");
                return std::nullopt;
            }
            const auto responseBaseSequence = RequiredIntegral(
                responsePayload, L"baseSequence");
            const auto responseRecoveryOriginSequence = RequiredIntegral(
                responsePayload, L"recoveryOriginSequence");
            if (responseBaseSequence != baseSequence ||
                responseRecoveryOriginSequence != recoveryOriginSequence) {
                Fail(L"WidgetBridge returned different presentation transaction authority.");
                return std::nullopt;
            }
            if (type == L"presentation-update") {
                WidgetPresentationPublication publication;
                publication.transactionKind = transactionKind;
                publication.requestBaseSequence = baseSequence;
                publication.recoveryOriginSequence = recoveryOriginSequence;
                publication.update = ParsePresentationUpdatePayload(
                    responsePayload, true);
                lastError_.clear();
                return publication;
            }
            auto snapshot = ParseStyledSnapshotPayload(responsePayload);
            WidgetPresentationPublication publication;
            publication.transactionKind = transactionKind;
            publication.requestBaseSequence = baseSequence;
            publication.recoveryOriginSequence = recoveryOriginSequence;
            publication.checkpoint = std::move(snapshot);
            lastError_.clear();
            return publication;
        }
    } catch (const winrt::hresult_error& error) {
        Fail(L"Invalid WidgetBridge JSON: " + std::wstring(error.message()));
    }
    return std::nullopt;
}

std::optional<bool> WidgetBridgeClient::RequestArtwork(
    const std::wstring_view widgetId,
    const std::wstring_view artworkHandle) {
    std::scoped_lock lock(requestMutex_);
    if (pipe_ == INVALID_HANDLE_VALUE || widgetId.empty() ||
        widgetId.size() > kMaximumIdentifierLength || !IsIdentifier(widgetId) ||
        artworkHandle.size() != 44 || !artworkHandle.starts_with(L"library.art.") ||
        !IsIdentifier(artworkHandle)) return std::nullopt;
    try {
        JsonObject payload;
        payload.Insert(L"widgetId", JsonValue::CreateStringValue(winrt::hstring(widgetId)));
        payload.Insert(L"artworkHandle",
                       JsonValue::CreateStringValue(winrt::hstring(artworkHandle)));
        const long long requestId = ++nextRequestId_;
        JsonObject envelope;
        envelope.Insert(L"protocolVersion", JsonValue::CreateNumberValue(1));
        envelope.Insert(L"type", JsonValue::CreateStringValue(L"resolve-artwork"));
        envelope.Insert(L"requestId", JsonValue::CreateNumberValue(
            static_cast<double>(requestId)));
        envelope.Insert(L"payload", payload);
        if (!WriteFrame(winrt::to_string(envelope.Stringify()))) return std::nullopt;
        while (const auto frame = ReadFrame()) {
            const auto response = JsonObject::Parse(winrt::to_hstring(*frame));
            const auto responseId = static_cast<long long>(
                response.GetNamedNumber(L"requestId"));
            if (responseId == 0) {
                std::wstring status;
                if (!HandleAsyncEvent(response, invalidations_, actionFailures_, hostEffects_,
                                      appearanceChanges_, catalogChanges_, status, &artworkResults_,
                                      &localPackageInstallResults_))
                    return std::nullopt;
                continue;
            }
            if (responseId != requestId ||
                response.GetNamedString(L"type") != L"acknowledged") return std::nullopt;
            return true;
        }
    } catch (const winrt::hresult_error&) {
    }
    return std::nullopt;
}

std::optional<EmbeddedMediaBundle> WidgetBridgeClient::ResolveEmbeddedMedia(
    const std::wstring_view widgetId,
    const std::wstring_view instanceId,
    const std::wstring_view runtimeGeneration,
    const std::wstring_view presentationGeneration,
    const long long sequence,
    const std::wstring_view surfaceId) {
    std::scoped_lock lock(requestMutex_);
    if (pipe_ == INVALID_HANDLE_VALUE || sequence <= 0 ||
        !IsIdentifier(widgetId) || !IsIdentifier(instanceId) ||
        !IsIdentifier(runtimeGeneration) || !IsIdentifier(presentationGeneration) ||
        !IsIdentifier(surfaceId)) return std::nullopt;
    try {
        JsonObject payload;
        payload.Insert(L"widgetId", JsonValue::CreateStringValue(winrt::hstring(widgetId)));
        payload.Insert(L"instanceId", JsonValue::CreateStringValue(winrt::hstring(instanceId)));
        payload.Insert(L"runtimeGeneration", JsonValue::CreateStringValue(winrt::hstring(runtimeGeneration)));
        payload.Insert(L"presentationGeneration", JsonValue::CreateStringValue(winrt::hstring(presentationGeneration)));
        payload.Insert(L"sequence", JsonValue::CreateNumberValue(static_cast<double>(sequence)));
        payload.Insert(L"surfaceId", JsonValue::CreateStringValue(winrt::hstring(surfaceId)));
        const long long requestId = ++nextRequestId_;
        JsonObject envelope;
        envelope.Insert(L"protocolVersion", JsonValue::CreateNumberValue(1));
        envelope.Insert(L"type", JsonValue::CreateStringValue(L"resolve-embedded-media"));
        envelope.Insert(L"requestId", JsonValue::CreateNumberValue(static_cast<double>(requestId)));
        envelope.Insert(L"payload", payload);
        if (!WriteFrame(winrt::to_string(envelope.Stringify()))) return std::nullopt;
        while (const auto frame = ReadFrame()) {
            const auto response = JsonObject::Parse(winrt::to_hstring(*frame));
            long long responseId{};
            if (!ReadRequestId(response, responseId)) return std::nullopt;
            if (responseId == 0) {
                std::wstring status;
                if (!HandleAsyncEvent(response, invalidations_, actionFailures_, hostEffects_,
                        appearanceChanges_, catalogChanges_, status, &artworkResults_,
                        &localPackageInstallResults_)) return std::nullopt;
                continue;
            }
            if (responseId != requestId) return std::nullopt;
            const auto type = response.GetNamedString(L"type");
            if (type == L"error") {
                Fail(SafeBridgeError(response));
                return std::nullopt;
            }
            if (type != L"embedded-media") return std::nullopt;
            const auto body = response.GetNamedObject(L"payload");
            if (!HasOnlyProperties(body,
                    {L"widgetId", L"instanceId", L"runtimeGeneration",
                     L"presentationGeneration", L"sequence", L"surfaceId",
                     L"entryAsset", L"surface", L"aspectRatio", L"accessibleName",
                     L"commands", L"resources"})) return std::nullopt;
            EmbeddedMediaBundle bundle;
            bundle.widgetId = OptionalString(body, L"widgetId");
            bundle.instanceId = OptionalString(body, L"instanceId");
            bundle.runtimeGeneration = OptionalString(body, L"runtimeGeneration");
            bundle.presentationGeneration = OptionalString(body, L"presentationGeneration");
            bundle.sequence = RequiredIntegral(body, L"sequence");
            bundle.surface.id = OptionalString(body, L"surfaceId");
            bundle.surface.entryAsset = OptionalString(body, L"entryAsset");
            bundle.surface.accessibleName = OptionalString(body, L"accessibleName");
            bundle.surface.aspectRatio = body.GetNamedNumber(L"aspectRatio");
            if (bundle.widgetId != widgetId || bundle.instanceId != instanceId ||
                bundle.runtimeGeneration != runtimeGeneration ||
                bundle.presentationGeneration != presentationGeneration ||
                bundle.sequence != sequence || bundle.surface.id != surfaceId)
                return std::nullopt;
            const auto surface = body.GetNamedObject(L"surface");
            bundle.surface.surface.mode = OptionalString(surface, L"mode");
            const auto number = [&surface](const wchar_t* name) -> std::optional<double> {
                return surface.HasKey(name)
                    ? std::optional<double>{surface.GetNamedNumber(name)}
                    : std::nullopt;
            };
            bundle.surface.surface.preferredWidth = number(L"preferredWidth");
            bundle.surface.surface.preferredHeight = number(L"preferredHeight");
            bundle.surface.surface.minimumWidth = number(L"minimumWidth");
            bundle.surface.surface.minimumHeight = number(L"minimumHeight");
            const auto commands = body.GetNamedArray(L"commands");
            for (uint32_t index = 0; index < commands.Size(); ++index)
                bundle.surface.commands.emplace_back(
                    std::wstring_view(commands.GetStringAt(index)));
            const auto resources = body.GetNamedArray(L"resources");
            if (resources.Size() == 0 ||
                resources.Size() > protocol_contract::MaximumEmbeddedMediaResourceCount)
                return std::nullopt;
            std::size_t aggregate{};
            for (uint32_t index = 0; index < resources.Size(); ++index) {
                const auto item = resources.GetObjectAt(index);
                if (!HasOnlyProperties(item,
                        {L"path", L"contentType", L"sha256", L"contentBase64"}))
                    return std::nullopt;
                EmbeddedMediaResource resource;
                resource.path = OptionalString(item, L"path");
                resource.contentType = OptionalString(item, L"contentType");
                resource.sha256 = OptionalString(item, L"sha256");
                const auto decoded = DecodeBase64Bounded(
                    OptionalString(item, L"contentBase64"),
                    protocol_contract::MaximumEmbeddedMediaResourceBytes);
                if (!decoded || resource.sha256.size() != 64 ||
                    !std::all_of(resource.sha256.begin(), resource.sha256.end(),
                        [](const wchar_t value) { return std::iswxdigit(value) != 0; }))
                    return std::nullopt;
                resource.content = std::move(*decoded);
                aggregate += resource.content.size();
                if (aggregate > protocol_contract::MaximumEmbeddedMediaAggregateBytes)
                    return std::nullopt;
                bundle.resources.push_back(std::move(resource));
            }
            lastError_.clear();
            return bundle;
        }
    } catch (const winrt::hresult_error& error) {
        Fail(L"Invalid embedded media response: " + std::wstring(error.message()));
    }
    return std::nullopt;
}

std::optional<bool> WidgetBridgeClient::SendControllerInput(
    const std::wstring_view widgetId,
    const std::wstring_view button,
    const std::wstring_view context,
    const std::wstring_view focusedElementId,
    const std::wstring_view activeInputScopeId,
    const long long snapshotSequence,
    const long long sequence,
    const long long monotonicTimestampMicroseconds,
    const std::wstring_view phase,
    const std::optional<double> requestedValue,
    const ControllerInputOrigin origin,
    const std::wstring_view runtimeGeneration,
    const std::wstring_view pinnedLayoutId,
    const std::optional<bool> pinnedLayoutSelected) {
    std::scoped_lock lock(requestMutex_);
    lastControllerInputResultCode_.clear();
    if (pipe_ == INVALID_HANDLE_VALUE) {
        lastControllerInputResultCode_ = L"transport-unavailable";
        return std::nullopt;
    }
    try {
        JsonObject input;
        input.Insert(L"button", JsonValue::CreateStringValue(winrt::hstring(button)));
        input.Insert(L"phase", JsonValue::CreateStringValue(winrt::hstring(phase)));
        input.Insert(L"context", JsonValue::CreateStringValue(winrt::hstring(context)));
        switch (origin) {
        case ControllerInputOrigin::PhysicalController:
            break;
        case ControllerInputOrigin::AccessibilityAutomation:
            input.Insert(L"origin", JsonValue::CreateStringValue(L"accessibilityAutomation"));
            break;
        default:
            lastControllerInputResultCode_ = L"invalid-origin";
            Fail(L"Invalid controller input origin.");
            return std::nullopt;
        }
        if (!focusedElementId.empty()) {
            input.Insert(L"focusedElementId",
                         JsonValue::CreateStringValue(winrt::hstring(focusedElementId)));
        }
        if (!activeInputScopeId.empty()) {
            input.Insert(L"activeInputScopeId",
                         JsonValue::CreateStringValue(winrt::hstring(activeInputScopeId)));
        }
        input.Insert(L"snapshotSequence",
                     JsonValue::CreateNumberValue(static_cast<double>(snapshotSequence)));
        input.Insert(L"sequence", JsonValue::CreateNumberValue(static_cast<double>(sequence)));
        input.Insert(L"monotonicTimestampMicroseconds",
                     JsonValue::CreateNumberValue(static_cast<double>(monotonicTimestampMicroseconds)));
        if (requestedValue && std::isfinite(*requestedValue)) {
            input.Insert(L"requestedValue", JsonValue::CreateNumberValue(*requestedValue));
        }
        if (!pinnedLayoutId.empty())
            input.Insert(L"pinnedLayoutId",
                         JsonValue::CreateStringValue(winrt::hstring(pinnedLayoutId)));
        if (pinnedLayoutSelected) {
            input.Insert(L"isPinnedLayoutSelected",
                         JsonValue::CreateBooleanValue(*pinnedLayoutSelected));
        }
        JsonObject payload;
        payload.Insert(L"widgetId", JsonValue::CreateStringValue(winrt::hstring(widgetId)));
        if (!runtimeGeneration.empty())
            payload.Insert(L"runtimeGeneration",
                           JsonValue::CreateStringValue(winrt::hstring(runtimeGeneration)));
        payload.Insert(L"input", input);
        const long long requestId = ++nextRequestId_;
        JsonObject envelope;
        envelope.Insert(L"protocolVersion", JsonValue::CreateNumberValue(1));
        envelope.Insert(L"type", JsonValue::CreateStringValue(L"controller-input"));
        envelope.Insert(L"requestId", JsonValue::CreateNumberValue(static_cast<double>(requestId)));
        envelope.Insert(L"payload", payload);
        if (!WriteFrame(winrt::to_string(envelope.Stringify()))) {
            lastControllerInputResultCode_ = L"transport-write-failed";
            return std::nullopt;
        }
        while (const auto frame = ReadFrame()) {
            const auto response = JsonObject::Parse(winrt::to_hstring(*frame));
            const auto responseId = static_cast<long long>(response.GetNamedNumber(L"requestId"));
            if (responseId == 0) {
                std::wstring status;
                if (!HandleAsyncEvent(response, invalidations_, actionFailures_, hostEffects_, appearanceChanges_, catalogChanges_, status, &artworkResults_, &localPackageInstallResults_)) {
                    lastControllerInputResultCode_ = L"async-event-invalid";
                    Fail(std::move(status));
                    return std::nullopt;
                }
                if (!status.empty()) lastError_ = std::move(status);
                continue;
            }
            if (responseId != requestId) {
                lastControllerInputResultCode_ = L"response-id-mismatch";
                return std::nullopt;
            }
            if (response.GetNamedString(L"type") == L"error") {
                const auto payload = response.GetNamedObject(L"payload");
                const auto code = OptionalString(payload, L"code");
                lastControllerInputResultCode_ =
                    IsIdentifier(code) && code.size() <= 64
                        ? std::move(code)
                        : L"bridge-error-invalid";
                Fail(SafeBridgeError(response));
                return std::nullopt;
            }
            if (response.GetNamedString(L"type") != L"controller-input-result") {
                lastControllerInputResultCode_ = L"response-type-invalid";
                Fail(L"WidgetBridge returned an unexpected controller response.");
                return std::nullopt;
            }
            const auto handled =
                response.GetNamedObject(L"payload").GetNamedBoolean(L"handled");
            lastControllerInputResultCode_ = handled ? L"handled" : L"unhandled";
            return handled;
        }
        lastControllerInputResultCode_ = L"transport-closed";
    } catch (const winrt::hresult_error& error) {
        lastControllerInputResultCode_ = L"response-json-invalid";
        Fail(L"Invalid WidgetBridge JSON: " + std::wstring(error.message()));
    }
    return std::nullopt;
}

std::optional<bool> WidgetBridgeClient::SendAction(
    const std::wstring_view widgetId,
    const std::wstring_view actionId,
    const std::wstring_view sourceElementId,
    const std::wstring_view inputScopeId,
    const std::optional<std::wstring_view> committedText) {
    std::scoped_lock lock(requestMutex_);
    if (pipe_ == INVALID_HANDLE_VALUE || widgetId.empty() || actionId.empty() ||
        sourceElementId.empty() || !IsIdentifier(widgetId) ||
        !IsIdentifier(actionId) || !IsIdentifier(sourceElementId) ||
        (!inputScopeId.empty() && !IsIdentifier(inputScopeId)) ||
        (committedText && (committedText->size() > 96 ||
            std::any_of(committedText->begin(), committedText->end(),
                [](const wchar_t value) { return std::iswcntrl(value) != 0; })))) {
        if (pipe_ != INVALID_HANDLE_VALUE) Fail(L"Widget action request is invalid.");
        return std::nullopt;
    }
    try {
        JsonObject action;
        action.Insert(L"actionId", JsonValue::CreateStringValue(winrt::hstring(actionId)));
        action.Insert(L"sourceElementId",
                      JsonValue::CreateStringValue(winrt::hstring(sourceElementId)));
        action.Insert(L"phase", JsonValue::CreateStringValue(L"pressed"));
        if (!inputScopeId.empty()) {
            action.Insert(L"inputScopeId",
                          JsonValue::CreateStringValue(winrt::hstring(inputScopeId)));
        }
        if (committedText) {
            action.Insert(L"committedText",
                JsonValue::CreateStringValue(winrt::hstring(*committedText)));
        }
        JsonObject payload;
        payload.Insert(L"widgetId", JsonValue::CreateStringValue(winrt::hstring(widgetId)));
        payload.Insert(L"action", action);
        const long long requestId = ++nextRequestId_;
        JsonObject envelope;
        envelope.Insert(L"protocolVersion", JsonValue::CreateNumberValue(1));
        envelope.Insert(L"type", JsonValue::CreateStringValue(L"action"));
        envelope.Insert(L"requestId", JsonValue::CreateNumberValue(
            static_cast<double>(requestId)));
        envelope.Insert(L"payload", payload);
        if (!WriteFrame(winrt::to_string(envelope.Stringify()))) return std::nullopt;

        while (const auto frame = ReadFrame()) {
            const auto response = JsonObject::Parse(winrt::to_hstring(*frame));
            long long responseId{};
            if (!ReadRequestId(response, responseId)) {
                Fail(L"WidgetBridge returned an invalid action request ID.");
                return std::nullopt;
            }
            const auto type = response.GetNamedString(L"type");
            if (responseId == 0) {
                std::wstring status;
                if (!HandleAsyncEvent(response, invalidations_, actionFailures_, hostEffects_,
                                      appearanceChanges_, catalogChanges_, status, &artworkResults_,
                                      &localPackageInstallResults_)) {
                    Fail(std::move(status));
                    return std::nullopt;
                }
                if (!status.empty()) lastError_ = std::move(status);
                continue;
            }
            if (responseId != requestId) {
                Fail(L"WidgetBridge returned a mismatched action request ID.");
                return std::nullopt;
            }
            if (type == L"error") {
                Fail(SafeBridgeError(response));
                return std::nullopt;
            }
            if (type != L"acknowledged") {
                Fail(L"WidgetBridge returned an unexpected action response.");
                return std::nullopt;
            }
            lastError_.clear();
            return true;
        }
    } catch (const winrt::hresult_error& error) {
        Fail(L"Invalid WidgetBridge action JSON: " + std::wstring(error.message()));
    }
    return std::nullopt;
}

std::optional<std::wstring> WidgetBridgeClient::ConnectProtectedWifi(
    const std::wstring_view widgetId,
    const std::wstring_view runtimeGeneration,
    const std::wstring_view sourceElementId,
    const std::span<const wchar_t> secret) {
    std::scoped_lock lock(requestMutex_);
    if (pipe_ == INVALID_HANDLE_VALUE || !IsIdentifier(widgetId) ||
        !IsIdentifier(runtimeGeneration) || !IsIdentifier(sourceElementId) ||
        secret.size() < 8 || secret.size() > 63 ||
        std::any_of(secret.begin(), secret.end(), [](const wchar_t character) {
            return character < 32 || character > 126;
        })) {
        if (pipe_ != INVALID_HANDLE_VALUE) Fail(L"Protected Wi-Fi request is invalid.");
        return std::nullopt;
    }
    try {
        JsonObject payload;
        payload.Insert(L"widgetId", JsonValue::CreateStringValue(winrt::hstring(widgetId)));
        payload.Insert(L"runtimeGeneration",
            JsonValue::CreateStringValue(winrt::hstring(runtimeGeneration)));
        payload.Insert(L"sourceElementId",
            JsonValue::CreateStringValue(winrt::hstring(sourceElementId)));
        payload.Insert(L"secretLength", JsonValue::CreateNumberValue(
            static_cast<double>(secret.size())));
        const long long requestId = ++nextRequestId_;
        JsonObject envelope;
        envelope.Insert(L"protocolVersion", JsonValue::CreateNumberValue(1));
        envelope.Insert(L"type", JsonValue::CreateStringValue(L"connect-protected-wifi"));
        envelope.Insert(L"requestId", JsonValue::CreateNumberValue(
            static_cast<double>(requestId)));
        envelope.Insert(L"payload", payload);
        if (!WriteFrame(winrt::to_string(envelope.Stringify())) ||
            !WriteProtectedWifiSecret(secret)) return std::nullopt;

        while (const auto frame = ReadFrame()) {
            const auto response = JsonObject::Parse(winrt::to_hstring(*frame));
            long long responseId{};
            if (!ReadRequestId(response, responseId)) {
                Fail(L"WidgetBridge returned an invalid protected Wi-Fi request ID.");
                return std::nullopt;
            }
            const auto type = response.GetNamedString(L"type");
            if (responseId == 0) {
                std::wstring status;
                if (!HandleAsyncEvent(response, invalidations_, actionFailures_, hostEffects_,
                        appearanceChanges_, catalogChanges_, status, &artworkResults_,
                        &localPackageInstallResults_)) {
                    Fail(std::move(status));
                    return std::nullopt;
                }
                continue;
            }
            if (responseId != requestId || type != L"acknowledged") {
                if (type == L"error") Fail(SafeBridgeError(response));
                else Fail(L"WidgetBridge returned an unexpected protected Wi-Fi response.");
                return std::nullopt;
            }
            const auto result = response.GetNamedObject(L"payload");
            const auto code = OptionalString(result, L"code");
            if (!IsIdentifier(code)) {
                Fail(L"WidgetBridge returned an invalid protected Wi-Fi result.");
                return std::nullopt;
            }
            lastError_.clear();
            return code;
        }
    } catch (const winrt::hresult_error& error) {
        Fail(L"Invalid WidgetBridge protected Wi-Fi JSON: " +
            std::wstring(error.message()));
    }
    return std::nullopt;
}

std::optional<bool> WidgetBridgeClient::BeginLocalWidgetPackageInstall(
    const std::wstring_view packagePath,
    const LocalWidgetPackageInstallOrigin& origin,
    const std::wstring_view operationId) {
    std::scoped_lock lock(requestMutex_);
    if (pipe_ == INVALID_HANDLE_VALUE || packagePath.empty() ||
        packagePath.size() > 32'767 || !IsIdentifier(operationId) ||
        !IsIdentifier(origin.widgetId) || !IsIdentifier(origin.packageId) ||
        !IsIdentifier(origin.publisherId) || !IsIdentifier(origin.instanceId) ||
        !IsIdentifier(origin.runtimeGeneration) ||
        !IsIdentifier(origin.presentationGeneration)) {
        if (pipe_ != INVALID_HANDLE_VALUE)
            Fail(L"Local widget package install request is invalid.");
        return std::nullopt;
    }
    try {
        JsonObject originPayload;
        originPayload.Insert(L"widgetId", JsonValue::CreateStringValue(origin.widgetId));
        originPayload.Insert(L"packageId", JsonValue::CreateStringValue(origin.packageId));
        originPayload.Insert(L"publisherId", JsonValue::CreateStringValue(origin.publisherId));
        originPayload.Insert(L"instanceId", JsonValue::CreateStringValue(origin.instanceId));
        originPayload.Insert(L"runtimeGeneration",
            JsonValue::CreateStringValue(origin.runtimeGeneration));
        originPayload.Insert(L"presentationGeneration",
            JsonValue::CreateStringValue(origin.presentationGeneration));
        JsonObject payload;
        payload.Insert(L"operationId", JsonValue::CreateStringValue(operationId));
        payload.Insert(L"packagePath", JsonValue::CreateStringValue(packagePath));
        payload.Insert(L"origin", originPayload);
        const long long requestId = ++nextRequestId_;
        JsonObject envelope;
        envelope.Insert(L"protocolVersion", JsonValue::CreateNumberValue(1));
        envelope.Insert(L"type", JsonValue::CreateStringValue(
            L"install-local-widget-package"));
        envelope.Insert(L"requestId", JsonValue::CreateNumberValue(
            static_cast<double>(requestId)));
        envelope.Insert(L"payload", payload);
        if (!WriteFrame(winrt::to_string(envelope.Stringify()))) return std::nullopt;
        while (const auto frame = ReadFrame()) {
            const auto response = JsonObject::Parse(winrt::to_hstring(*frame));
            long long responseId{};
            if (!ReadRequestId(response, responseId)) return std::nullopt;
            const auto type = response.GetNamedString(L"type");
            if (responseId == 0) {
                std::wstring status;
                if (!HandleAsyncEvent(
                        response, invalidations_, actionFailures_, hostEffects_,
                        appearanceChanges_, catalogChanges_, status, &artworkResults_,
                        &localPackageInstallResults_)) {
                    Fail(std::move(status));
                    return std::nullopt;
                }
                continue;
            }
            if (responseId != requestId || type != L"acknowledged") {
                Fail(type == L"error" ? SafeBridgeError(response)
                                      : L"WidgetBridge returned an unexpected local package response.");
                return std::nullopt;
            }
            const auto acknowledgement = response.GetNamedObject(L"payload");
            if (OptionalString(acknowledgement, L"operationId") != operationId) {
                Fail(L"WidgetBridge acknowledged a different local package operation.");
                return std::nullopt;
            }
            lastError_.clear();
            return true;
        }
    } catch (const winrt::hresult_error& error) {
        Fail(L"Invalid WidgetBridge local package response: " +
             std::wstring(error.message()));
    }
    return std::nullopt;
}

std::optional<bool> WidgetBridgeClient::CancelLocalWidgetPackageInstall(
    const std::wstring_view operationId) {
    std::scoped_lock lock(requestMutex_);
    if (pipe_ == INVALID_HANDLE_VALUE || !IsIdentifier(operationId)) return std::nullopt;
    try {
        JsonObject payload;
        payload.Insert(L"operationId", JsonValue::CreateStringValue(operationId));
        const long long requestId = ++nextRequestId_;
        JsonObject envelope;
        envelope.Insert(L"protocolVersion", JsonValue::CreateNumberValue(1));
        envelope.Insert(L"type", JsonValue::CreateStringValue(
            L"cancel-local-widget-package-install"));
        envelope.Insert(L"requestId", JsonValue::CreateNumberValue(
            static_cast<double>(requestId)));
        envelope.Insert(L"payload", payload);
        if (!WriteFrame(winrt::to_string(envelope.Stringify()))) return std::nullopt;
        while (const auto frame = ReadFrame()) {
            const auto response = JsonObject::Parse(winrt::to_hstring(*frame));
            long long responseId{};
            if (!ReadRequestId(response, responseId)) return std::nullopt;
            const auto type = response.GetNamedString(L"type");
            if (responseId == 0) {
                std::wstring status;
                if (!HandleAsyncEvent(
                        response, invalidations_, actionFailures_, hostEffects_,
                        appearanceChanges_, catalogChanges_, status, &artworkResults_,
                        &localPackageInstallResults_)) return std::nullopt;
                continue;
            }
            if (responseId != requestId || type != L"acknowledged") {
                if (type == L"error") Fail(SafeBridgeError(response));
                return std::nullopt;
            }
            return response.GetNamedObject(L"payload").GetNamedBoolean(L"cancelled", false);
        }
    } catch (const winrt::hresult_error& error) {
        Fail(L"Invalid WidgetBridge local package cancellation response: " +
             std::wstring(error.message()));
    }
    return std::nullopt;
}

bool WidgetBridgeClient::WriteProtectedWifiSecret(
    const std::span<const wchar_t> secret) {
    auto frame = ProtectedWifiSecretFrame::Create(secret);
    if (!frame) return false;
    const auto bytes = frame->bytes();
    const std::int32_t length = static_cast<std::int32_t>(bytes.size());
    if (!WriteExact(pipe_, &length, sizeof(length)) ||
        !WriteExact(pipe_, bytes.data(), static_cast<DWORD>(bytes.size()))) {
        transportTainted_ = true;
        Fail(Win32Message(L"WriteFile(WidgetBridge protected Wi-Fi)", GetLastError()));
        return false;
    }
    return true;
}

bool WidgetBridgeClient::WriteFrame(const std::string_view utf8) {
    if (utf8.empty() || utf8.size() > kMaximumFrameBytes ||
        utf8.size() > static_cast<std::size_t>((std::numeric_limits<std::int32_t>::max)())) {
        Fail(L"Outgoing WidgetBridge frame has an invalid size.");
        return false;
    }
    const std::int32_t length = static_cast<std::int32_t>(utf8.size());
    if (!WriteExact(pipe_, &length, sizeof(length)) ||
        !WriteExact(pipe_, utf8.data(), static_cast<DWORD>(utf8.size()))) {
        const DWORD error = GetLastError();
        transportTainted_ = true;
        Fail(Win32Message(L"WriteFile(WidgetBridge)", error));
        return false;
    }
    return true;
}

std::optional<std::string> WidgetBridgeClient::ReadFrame() {
    auto result = ReadFrameFromPipe(pipe_);
    if (result.frame) return std::move(result.frame);
    transportTainted_ = result.transportTainted;
    if (result.error == ERROR_INVALID_DATA) {
        Fail(L"WidgetBridge announced an invalid frame size.");
        return std::nullopt;
    }
    Fail(Win32Message(L"ReadFile(WidgetBridge frame)", result.error));
    return std::nullopt;
}

void WidgetBridgeClient::Fail(std::wstring message) {
    lastError_ = std::move(message);
}

std::wstring WidgetBridgeClient::lastError() const {
    std::scoped_lock lock(requestMutex_);
    return lastError_;
}

std::wstring WidgetBridgeClient::lastControllerInputResultCode() const {
    std::scoped_lock lock(requestMutex_);
    return lastControllerInputResultCode_;
}

std::optional<WidgetBridgeRuntimeFailureCategory>
WidgetBridgeClient::lastRuntimeFailureCategory(
    const std::wstring_view widgetId) const noexcept {
    std::scoped_lock lock(requestMutex_);
    if (!lastRuntimeFailure_ || lastRuntimeFailure_->widgetId != widgetId)
        return std::nullopt;
    return lastRuntimeFailure_->category;
}

WidgetBridgeRequestFailureCategory
WidgetBridgeClient::lastRequestFailureCategory() const noexcept {
    std::scoped_lock lock(requestMutex_);
    return lastRequestFailureCategory_;
}

bool WidgetBridgeClient::PumpEvents() {
    std::unique_lock lock(requestMutex_, std::try_to_lock);
    if (!lock.owns_lock()) return false;
    if (pipe_ == INVALID_HANDLE_VALUE) return false;
    bool consumed = false;
    try {
        for (int count = 0; count < 16; ++count) {
            std::array<std::byte, sizeof(std::int32_t)> header{};
            DWORD copied = 0;
            DWORD available = 0;
            if (!PeekNamedPipe(pipe_, header.data(), static_cast<DWORD>(header.size()),
                               &copied, &available, nullptr)) {
                Fail(Win32Message(L"PeekNamedPipe(WidgetBridge)", GetLastError()));
                return consumed;
            }
            if (available < sizeof(std::int32_t) || copied < sizeof(std::int32_t)) break;
            std::int32_t length = 0;
            std::memcpy(&length, header.data(), sizeof(length));
            if (length <= 0 || static_cast<DWORD>(length) > kMaximumFrameBytes) {
                Fail(L"WidgetBridge announced an invalid asynchronous frame size.");
                return consumed;
            }
            if (available < sizeof(std::int32_t) + static_cast<DWORD>(length)) break;
            const auto frame = ReadFrame();
            if (!frame) return consumed;
            const auto message = JsonObject::Parse(winrt::to_hstring(*frame));
            if (static_cast<long long>(message.GetNamedNumber(L"requestId")) != 0) {
                Fail(L"WidgetBridge produced an unexpected unclaimed response.");
                return consumed;
            }
            std::wstring status;
            std::optional<WidgetBridgeRuntimeFailure> runtimeFailure;
            if (!HandleAsyncEvent(message, invalidations_, actionFailures_, hostEffects_, appearanceChanges_, catalogChanges_, status, &artworkResults_, &localPackageInstallResults_, &runtimeFailure)) {
                Fail(std::move(status));
                return consumed;
            }
            if (runtimeFailure) {
                lastRuntimeFailure_ = *runtimeFailure;
                constexpr std::size_t MaximumRuntimeFailures = 16;
                if (runtimeFailures_.size() == MaximumRuntimeFailures)
                    runtimeFailures_.erase(runtimeFailures_.begin());
                runtimeFailures_.push_back(std::move(*runtimeFailure));
            }
            if (!status.empty()) lastError_ = std::move(status);
            consumed = true;
        }
    } catch (const winrt::hresult_error& error) {
        Fail(L"Invalid WidgetBridge event JSON: " + std::wstring(error.message()));
    }
    return consumed;
}

std::vector<WidgetBridgeRuntimeFailure>
WidgetBridgeClient::TakeRuntimeFailures() noexcept {
    std::unique_lock lock(requestMutex_, std::try_to_lock);
    if (!lock.owns_lock()) return {};
    std::vector<WidgetBridgeRuntimeFailure> result;
    result.swap(runtimeFailures_);
    return result;
}

std::vector<std::wstring> WidgetBridgeClient::TakeInvalidatedWidgetIds() noexcept {
    std::unique_lock lock(requestMutex_, std::try_to_lock);
    if (!lock.owns_lock()) return {};
    return invalidations_.Take();
}

std::vector<WidgetActionFailure> WidgetBridgeClient::TakeActionFailures() noexcept {
    std::unique_lock lock(requestMutex_, std::try_to_lock);
    if (!lock.owns_lock()) return {};
    return actionFailures_.Take();
}

std::vector<WidgetHostEffect> WidgetBridgeClient::TakeHostEffects() noexcept {
    std::unique_lock lock(requestMutex_, std::try_to_lock);
    if (!lock.owns_lock()) return {};
    return hostEffects_.Take();
}

std::vector<WidgetArtworkResult> WidgetBridgeClient::TakeArtworkResults() noexcept {
    std::unique_lock lock(requestMutex_, std::try_to_lock);
    if (!lock.owns_lock()) return {};
    return artworkResults_.Take();
}

std::vector<LocalWidgetPackageInstallResult>
WidgetBridgeClient::TakeLocalWidgetPackageInstallResults() noexcept {
    std::unique_lock lock(requestMutex_, std::try_to_lock);
    if (!lock.owns_lock()) return {};
    return localPackageInstallResults_.Take();
}

std::optional<long long>
WidgetBridgeClient::TakePlatformAppearanceChangedRevision() noexcept {
    std::unique_lock lock(requestMutex_, std::try_to_lock);
    if (!lock.owns_lock()) return std::nullopt;
    return appearanceChanges_.Take();
}

std::optional<long long>
WidgetBridgeClient::TakeWidgetCatalogChangedRevision() noexcept {
    std::unique_lock lock(requestMutex_, std::try_to_lock);
    if (!lock.owns_lock()) return std::nullopt;
    return catalogChanges_.Take();
}

void WidgetBridgeClient::RetryWidgetCatalogChangedRevision() noexcept {
    std::scoped_lock lock(requestMutex_);
    catalogChanges_.Retry();
}

void WidgetBridgeClient::AbandonWidgetCatalogChangedRevision() noexcept {
    std::scoped_lock lock(requestMutex_);
    catalogChanges_.Abandon();
}

bool WidgetBridgeClient::HasWidgetCatalogChangedRevisionInFlight() const noexcept {
    std::scoped_lock lock(requestMutex_);
    return catalogChanges_.hasInFlight();
}

} // namespace widgetrail

#ifdef WRAIL_WIDGET_BRIDGE_CLIENT_TESTING
namespace widgetrail::testing {

BridgeFrameReadResult ReadBridgeFrame(const HANDLE pipe) {
    auto result = ReadFrameFromPipe(pipe);
    return {std::move(result.frame), result.transportTainted, result.error};
}

std::optional<std::vector<WidgetDescriptor>> ParseWidgetDescriptors(
    const std::string_view payloadUtf8,
    std::wstring& error) {
    try {
        const auto payload = JsonObject::Parse(winrt::to_hstring(payloadUtf8));
        return widgetrail::ParseWidgetDescriptors(payload, error);
    } catch (const winrt::hresult_error& exception) {
        error = L"Invalid widget descriptor JSON: " + std::wstring(exception.message());
        return std::nullopt;
    }
}

std::optional<PlatformAppearance> ParsePlatformAppearance(
    const std::string_view payloadUtf8,
    std::wstring& error) {
    try {
        const auto payload = JsonObject::Parse(winrt::to_hstring(payloadUtf8));
        return widgetrail::ParsePlatformAppearance(payload, error);
    } catch (const winrt::hresult_error& exception) {
        error = L"Invalid platform appearance JSON: " +
                std::wstring(exception.message());
        return std::nullopt;
    }
}

std::optional<WidgetSnapshot> ParseWidgetSnapshotResponse(
    const std::string_view payloadUtf8,
    std::wstring& error) {
    try {
        const auto payload = JsonObject::Parse(winrt::to_hstring(payloadUtf8));
        auto snapshot = ParseStyledSnapshotPayload(payload);
        error.clear();
        return snapshot;
    } catch (const winrt::hresult_error& exception) {
        error = L"Invalid widget snapshot JSON: " + std::wstring(exception.message());
        return std::nullopt;
    }
}

std::optional<WidgetPresentationUpdate> ParseWidgetPresentationUpdateResponse(
    const std::string_view payloadUtf8,
    std::wstring& error) {
    try {
        const auto payload = JsonObject::Parse(winrt::to_hstring(payloadUtf8));
        auto update = ParsePresentationUpdatePayload(payload);
        error.clear();
        return update;
    } catch (const winrt::hresult_error& exception) {
        error = L"Invalid widget presentation update JSON: " +
            std::wstring(exception.message());
        return std::nullopt;
    }
}

std::optional<WidgetPresentationUpdate> ParseTypedWidgetPresentationUpdateResponse(
    const std::string_view payloadUtf8,
    std::wstring& error) {
    try {
        const auto payload = JsonObject::Parse(winrt::to_hstring(payloadUtf8));
        auto update = ParsePresentationUpdatePayload(payload, true);
        error.clear();
        return update;
    } catch (const winrt::hresult_error& exception) {
        error = L"Invalid typed widget presentation update JSON: " +
            std::wstring(exception.message());
        return std::nullopt;
    }
}

std::optional<WidgetHostEffect> ParseWidgetHostEffectEvent(
    const std::string_view eventUtf8,
    std::wstring& error) {
    try {
        const auto event = JsonObject::Parse(winrt::to_hstring(eventUtf8));
        WidgetInvalidationQueue invalidations;
        WidgetActionFailureQueue actionFailures;
        WidgetHostEffectQueue effects;
        PlatformAppearanceRevisionTracker appearance;
        WidgetCatalogRevisionTracker catalog;
        std::wstring status;
        if (!HandleAsyncEvent(
                event, invalidations, actionFailures, effects, appearance, catalog, status)) {
            error = std::move(status);
            return std::nullopt;
        }
        auto queued = effects.Take();
        if (queued.size() != 1) {
            error = L"JSON is not a widget host-effect event.";
            return std::nullopt;
        }
        error.clear();
        return std::move(queued.front());
    } catch (const winrt::hresult_error& exception) {
        error = L"Invalid widget host-effect JSON: " +
                std::wstring(exception.message());
        return std::nullopt;
    }
}

std::optional<WidgetActionFailure> ParseWidgetActionFailureEvent(
    const std::string_view eventUtf8,
    std::wstring& error) {
    try {
        const auto event = JsonObject::Parse(winrt::to_hstring(eventUtf8));
        WidgetInvalidationQueue invalidations;
        WidgetActionFailureQueue failures;
        WidgetHostEffectQueue effects;
        PlatformAppearanceRevisionTracker appearance;
        WidgetCatalogRevisionTracker catalog;
        std::wstring status;
        if (!HandleAsyncEvent(
                event, invalidations, failures, effects, appearance, catalog, status)) {
            error = std::move(status);
            return std::nullopt;
        }
        auto queued = failures.Take();
        if (queued.size() != 1) {
            error = L"JSON is not a widget action-failure event.";
            return std::nullopt;
        }
        error.clear();
        return std::move(queued.front());
    } catch (const winrt::hresult_error& exception) {
        error = L"Invalid widget action-failure JSON: " +
                std::wstring(exception.message());
        return std::nullopt;
    }
}

std::optional<WidgetBridgeRuntimeFailure> ParseWidgetRuntimeFailureEvent(
    const std::string_view eventUtf8,
    std::wstring& error) {
    try {
        const auto event = JsonObject::Parse(winrt::to_hstring(eventUtf8));
        WidgetInvalidationQueue invalidations;
        WidgetActionFailureQueue actionFailures;
        WidgetHostEffectQueue effects;
        PlatformAppearanceRevisionTracker appearance;
        WidgetCatalogRevisionTracker catalog;
        std::optional<WidgetBridgeRuntimeFailure> failure;
        std::wstring status;
        if (!HandleAsyncEvent(
                event, invalidations, actionFailures, effects, appearance, catalog,
                status, nullptr, nullptr, &failure)) {
            error = std::move(status);
            return std::nullopt;
        }
        if (!failure) {
            error = L"JSON is not a widget runtime-failure event.";
            return std::nullopt;
        }
        error.clear();
        return failure;
    } catch (const winrt::hresult_error& exception) {
        error = L"Invalid widget runtime-failure JSON: " +
                std::wstring(exception.message());
        return std::nullopt;
    }
}

std::optional<WidgetArtworkResult> ParseWidgetArtworkResultEvent(
    const std::string_view eventUtf8,
    std::wstring& error) {
    try {
        const auto event = JsonObject::Parse(winrt::to_hstring(eventUtf8));
        WidgetInvalidationQueue invalidations;
        WidgetActionFailureQueue failures;
        WidgetHostEffectQueue effects;
        WidgetArtworkResultQueue artwork;
        PlatformAppearanceRevisionTracker appearance;
        WidgetCatalogRevisionTracker catalog;
        std::wstring status;
        if (!HandleAsyncEvent(
                event, invalidations, failures, effects, appearance, catalog,
                status, &artwork)) {
            error = std::move(status);
            return std::nullopt;
        }
        auto queued = artwork.Take();
        if (queued.size() != 1) {
            error = L"JSON is not a trusted artwork completion event.";
            return std::nullopt;
        }
        error.clear();
        return std::move(queued.front());
    } catch (const winrt::hresult_error& exception) {
        error = L"Invalid trusted artwork event JSON: " +
                std::wstring(exception.message());
        return std::nullopt;
    }
}

std::optional<LocalWidgetPackageInstallResult>
ParseLocalWidgetPackageInstallResultEvent(
    const std::string_view eventUtf8,
    std::wstring& error) {
    try {
        const auto event = JsonObject::Parse(winrt::to_hstring(eventUtf8));
        WidgetInvalidationQueue invalidations;
        WidgetActionFailureQueue failures;
        WidgetHostEffectQueue effects;
        WidgetArtworkResultQueue artwork;
        LocalWidgetPackageInstallResultQueue packages;
        PlatformAppearanceRevisionTracker appearance;
        WidgetCatalogRevisionTracker catalog;
        std::wstring status;
        if (!HandleAsyncEvent(
                event, invalidations, failures, effects, appearance, catalog,
                status, &artwork, &packages)) {
            error = std::move(status);
            return std::nullopt;
        }
        auto queued = packages.Take();
        if (queued.size() != 1) {
            error = L"JSON is not a local widget package completion event.";
            return std::nullopt;
        }
        error.clear();
        return std::move(queued.front());
    } catch (const winrt::hresult_error& exception) {
        error = L"Invalid local widget package event JSON: " +
                std::wstring(exception.message());
        return std::nullopt;
    }
}

} // namespace widgetrail::testing
#endif
