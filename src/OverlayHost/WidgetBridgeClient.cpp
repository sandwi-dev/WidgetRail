#include "WidgetBridgeClient.h"

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

namespace gba {
namespace {

using winrt::Windows::Data::Json::JsonArray;
using winrt::Windows::Data::Json::JsonObject;
using winrt::Windows::Data::Json::JsonValue;
using winrt::Windows::Data::Json::JsonValueType;

constexpr DWORD kMaximumFrameBytes = 1024 * 1024;
constexpr uint32_t kMaximumWidgetDescriptors = 256;
constexpr uint32_t kMaximumDescriptorQuickActions = 16;
constexpr std::size_t kMaximumIdentifierLength = 128;
constexpr std::size_t kMaximumLabelLength = 256;
constexpr std::size_t kMaximumControllerButtonLength = 32;
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
    static constexpr std::array<std::wstring_view, 19> glyphs{
        L"music", L"play", L"pause", L"previous", L"next", L"refresh", L"shuffle",
        L"like", L"dislike", L"repeat", L"settings", L"warning", L"check", L"connection",
        L"volume", L"muted", L"microphone", L"wifi", L"ethernet"};
    return std::find(glyphs.begin(), glyphs.end(), value) != glyphs.end();
}

std::wstring Quote(const std::filesystem::path& path) {
    return L"\"" + path.wstring() + L"\"";
}

std::wstring Win32Message(const std::wstring_view operation, const DWORD error) {
    return std::wstring(operation) + L" failed with Win32 error " + std::to_wstring(error);
}

bool ReadExact(const HANDLE pipe, void* destination, const DWORD length) {
    auto* output = static_cast<std::byte*>(destination);
    DWORD completed = 0;
    while (completed < length) {
        DWORD count = 0;
        if (!ReadFile(pipe, output + completed, length - completed, &count, nullptr) || count == 0) {
            return false;
        }
        completed += count;
    }
    return true;
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
             L"transparency", L"shellStyles"})) {
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

WidgetNode ParseNode(const JsonObject& source) {
    WidgetNode node;
    node.id = std::wstring(std::wstring_view(source.GetNamedString(L"id")));
    node.kind = std::wstring(std::wstring_view(source.GetNamedString(L"kind")));
    node.text = OptionalString(source, L"text");
    node.accessibilityLabel = OptionalString(source, L"accessibilityLabel");
    node.accessibilityValue = OptionalString(source, L"accessibilityValue");
    node.actionId = OptionalString(source, L"actionId");
    node.valueChangedActionId = OptionalString(source, L"valueChangedActionId");
    node.sliderInteractionMode = OptionalString(source, L"sliderInteractionMode");
    if (!node.sliderInteractionMode.empty() &&
        node.sliderInteractionMode != L"direct" &&
        node.sliderInteractionMode != L"activateToAdjust")
        throw winrt::hresult_invalid_argument();
    node.imageSource = OptionalString(source, L"imageSource");
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
    if (source.HasKey(L"scrollPaginationThreshold")) {
        if (source.GetNamedValue(L"scrollPaginationThreshold").ValueType() !=
            JsonValueType::Number)
            throw winrt::hresult_invalid_argument();
        const auto value = source.GetNamedNumber(L"scrollPaginationThreshold");
        if (!std::isfinite(value) || value < 1.0 || value > 8.0 ||
            std::floor(value) != value)
            throw winrt::hresult_invalid_argument();
        node.scrollPaginationThreshold = static_cast<std::size_t>(value);
    }
    node.actionSurfaceOrientation = OptionalString(source, L"actionSurfaceOrientation");
    if (source.HasKey(L"gridMinimumColumnWidth")) {
        if (source.GetNamedValue(L"gridMinimumColumnWidth").ValueType() != JsonValueType::Number)
            throw winrt::hresult_invalid_argument();
        const auto value = source.GetNamedNumber(L"gridMinimumColumnWidth");
        if (!std::isfinite(value) || value < 44.0 || value > 1600.0)
            throw winrt::hresult_invalid_argument();
        node.gridMinimumColumnWidth = value;
    }
    if (source.HasKey(L"gridMaximumColumns")) {
        if (source.GetNamedValue(L"gridMaximumColumns").ValueType() != JsonValueType::Number)
            throw winrt::hresult_invalid_argument();
        const auto value = source.GetNamedNumber(L"gridMaximumColumns");
        if (!std::isfinite(value) || value < 1.0 || value > 32.0 ||
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

void ApplyComputedStyles(WidgetNode& node, const JsonObject& styles) {
    if (styles.HasKey(node.id)) {
        const auto states = styles.GetNamedObject(node.id);
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
    for (auto& child : node.children) ApplyComputedStyles(child, styles);
}

WidgetSnapshot ParseSnapshot(const JsonObject& source) {
    WidgetSnapshot snapshot;
    snapshot.sequence = static_cast<long long>(source.GetNamedNumber(L"sequence"));
    snapshot.instanceId = std::wstring(std::wstring_view(source.GetNamedString(L"widgetInstanceId")));
    snapshot.activeInputScopeId =
        std::wstring(std::wstring_view(source.GetNamedString(L"activeInputScopeId")));
    snapshot.initialFocusId = OptionalString(source, L"initialFocusId");
    if (source.HasKey(L"surface")) {
        const auto hints = source.GetNamedObject(L"surface");
        WidgetSurfaceHints parsed;
        parsed.mode = OptionalString(hints, L"mode");
        const auto optionalNumber = [&hints](const wchar_t* name) -> std::optional<double> {
            if (!hints.HasKey(name)) return std::nullopt;
            return hints.GetNamedNumber(name);
        };
        parsed.preferredWidth = optionalNumber(L"preferredWidth");
        parsed.preferredHeight = optionalNumber(L"preferredHeight");
        parsed.minimumWidth = optionalNumber(L"minimumWidth");
        parsed.minimumHeight = optionalNumber(L"minimumHeight");
        snapshot.surface = std::move(parsed);
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
    return snapshot;
}

bool HandleAsyncEvent(
    const JsonObject& event,
    WidgetInvalidationQueue& invalidations,
    WidgetHostEffectQueue& hostEffects,
    PlatformAppearanceRevisionTracker& appearanceChanges,
    WidgetCatalogRevisionTracker& catalogChanges,
    std::wstring& status) {
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
    const auto widgetId = OptionalString(payload, L"widgetId");
    if (!IsIdentifier(widgetId)) {
        status = L"WidgetBridge asynchronous event has an invalid widget ID.";
        return false;
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
    if (type == L"widget-failed") {
        status = L"Widget '" + widgetId +
                 L"' worker failed and will be restarted on demand.";
        return true;
    }
    status = L"WidgetBridge returned an unknown asynchronous event.";
    return false;
}

} // namespace

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
    if (pipe_ != INVALID_HANDLE_VALUE) {
        return true;
    }
    lastError_.clear();
    return Launch(installationDirectory, installedCatalogRoot) && Connect();
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

    pipeName_ = L"gba-host-" + std::to_wstring(GetCurrentProcessId()) + L"-" +
                std::to_wstring(GetTickCount64());
    std::wstring command = Quote(executable) + L" --host-pipe " + pipeName_ +
                           L" --catalog " + Quote(catalog) + L" --accept-timeout-ms 10000";
    if (!installedCatalogRoot.empty()) {
        command += L" --installed-catalog-root " + Quote(installedCatalogRoot);
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
    if (pipe_ != INVALID_HANDLE_VALUE) {
        CloseHandle(pipe_);
        pipe_ = INVALID_HANDLE_VALUE;
    }
    if (process_) {
        WaitForSingleObject(process_, 1000);
        CloseHandle(process_);
        process_ = nullptr;
    }
    processId_ = 0;
    nextRequestId_ = 0;
    (void)invalidations_.Take();
    hostEffects_.Reset();
    (void)appearanceChanges_.Take();
    catalogChanges_.Reset();
}

std::optional<std::vector<WidgetDescriptor>> WidgetBridgeClient::ListWidgets() {
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
                if (!HandleAsyncEvent(response, invalidations_, hostEffects_, appearanceChanges_, catalogChanges_, status)) {
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
                if (!HandleAsyncEvent(response, invalidations_, hostEffects_, appearanceChanges_, catalogChanges_, status)) {
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
                if (!HandleAsyncEvent(response, invalidations_, hostEffects_, appearanceChanges_, catalogChanges_, status)) {
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

std::optional<bool> WidgetBridgeClient::RestartWidget(
    const std::wstring_view widgetId) {
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
                if (!HandleAsyncEvent(response, invalidations_, hostEffects_,
                                      appearanceChanges_, catalogChanges_, status)) {
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

std::optional<WidgetSnapshot> WidgetBridgeClient::GetSnapshot(const std::wstring_view widgetId) {
    if (pipe_ == INVALID_HANDLE_VALUE) return std::nullopt;
    try {
        JsonObject payload;
        payload.Insert(L"widgetId", JsonValue::CreateStringValue(winrt::hstring(widgetId)));
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
                if (!HandleAsyncEvent(response, invalidations_, hostEffects_, appearanceChanges_, catalogChanges_, status)) {
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
            if (response.GetNamedString(L"type") == L"error") {
                Fail(std::wstring(std::wstring_view(
                    response.GetNamedObject(L"payload").GetNamedString(L"message"))));
                return std::nullopt;
            }
            const auto responsePayload = response.GetNamedObject(L"payload");
            if (OptionalString(responsePayload, L"widgetId") != widgetId) {
                Fail(L"WidgetBridge returned a snapshot for a different widget ID.");
                return std::nullopt;
            }
            auto snapshot = ParseSnapshot(responsePayload.GetNamedObject(L"snapshot"));
            if (responsePayload.HasKey(L"renderStyles")) {
                ApplyComputedStyles(snapshot.root,
                                    responsePayload.GetNamedObject(L"renderStyles"));
            }
            return snapshot;
        }
    } catch (const winrt::hresult_error& error) {
        Fail(L"Invalid WidgetBridge JSON: " + std::wstring(error.message()));
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
    const std::optional<double> requestedValue) {
    if (pipe_ == INVALID_HANDLE_VALUE) return std::nullopt;
    try {
        JsonObject input;
        input.Insert(L"button", JsonValue::CreateStringValue(winrt::hstring(button)));
        input.Insert(L"phase", JsonValue::CreateStringValue(winrt::hstring(phase)));
        input.Insert(L"context", JsonValue::CreateStringValue(winrt::hstring(context)));
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
        JsonObject payload;
        payload.Insert(L"widgetId", JsonValue::CreateStringValue(winrt::hstring(widgetId)));
        payload.Insert(L"input", input);
        const long long requestId = ++nextRequestId_;
        JsonObject envelope;
        envelope.Insert(L"protocolVersion", JsonValue::CreateNumberValue(1));
        envelope.Insert(L"type", JsonValue::CreateStringValue(L"controller-input"));
        envelope.Insert(L"requestId", JsonValue::CreateNumberValue(static_cast<double>(requestId)));
        envelope.Insert(L"payload", payload);
        if (!WriteFrame(winrt::to_string(envelope.Stringify()))) return std::nullopt;
        while (const auto frame = ReadFrame()) {
            const auto response = JsonObject::Parse(winrt::to_hstring(*frame));
            const auto responseId = static_cast<long long>(response.GetNamedNumber(L"requestId"));
            if (responseId == 0) {
                std::wstring status;
                if (!HandleAsyncEvent(response, invalidations_, hostEffects_, appearanceChanges_, catalogChanges_, status)) {
                    Fail(std::move(status));
                    return std::nullopt;
                }
                if (!status.empty()) lastError_ = std::move(status);
                continue;
            }
            if (responseId != requestId) return std::nullopt;
            if (response.GetNamedString(L"type") == L"error") {
                Fail(std::wstring(std::wstring_view(
                    response.GetNamedObject(L"payload").GetNamedString(L"message"))));
                return std::nullopt;
            }
            if (response.GetNamedString(L"type") != L"controller-input-result") {
                Fail(L"WidgetBridge returned an unexpected controller response.");
                return std::nullopt;
            }
            return response.GetNamedObject(L"payload").GetNamedBoolean(L"handled");
        }
    } catch (const winrt::hresult_error& error) {
        Fail(L"Invalid WidgetBridge JSON: " + std::wstring(error.message()));
    }
    return std::nullopt;
}

std::optional<bool> WidgetBridgeClient::SendAction(
    const std::wstring_view widgetId,
    const std::wstring_view actionId,
    const std::wstring_view sourceElementId,
    const std::wstring_view inputScopeId) {
    if (pipe_ == INVALID_HANDLE_VALUE || widgetId.empty() || actionId.empty() ||
        sourceElementId.empty() || !IsIdentifier(widgetId) ||
        !IsIdentifier(actionId) || !IsIdentifier(sourceElementId) ||
        (!inputScopeId.empty() && !IsIdentifier(inputScopeId))) {
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
                if (!HandleAsyncEvent(response, invalidations_, hostEffects_,
                                      appearanceChanges_, catalogChanges_, status)) {
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

bool WidgetBridgeClient::WriteFrame(const std::string_view utf8) {
    if (utf8.empty() || utf8.size() > kMaximumFrameBytes ||
        utf8.size() > static_cast<std::size_t>((std::numeric_limits<std::int32_t>::max)())) {
        Fail(L"Outgoing WidgetBridge frame has an invalid size.");
        return false;
    }
    const std::int32_t length = static_cast<std::int32_t>(utf8.size());
    if (!WriteExact(pipe_, &length, sizeof(length)) ||
        !WriteExact(pipe_, utf8.data(), static_cast<DWORD>(utf8.size()))) {
        Fail(Win32Message(L"WriteFile(WidgetBridge)", GetLastError()));
        return false;
    }
    return true;
}

std::optional<std::string> WidgetBridgeClient::ReadFrame() {
    std::int32_t length = 0;
    if (!ReadExact(pipe_, &length, sizeof(length))) {
        Fail(Win32Message(L"ReadFile(WidgetBridge header)", GetLastError()));
        return std::nullopt;
    }
    if (length <= 0 || static_cast<DWORD>(length) > kMaximumFrameBytes) {
        Fail(L"WidgetBridge announced an invalid frame size.");
        return std::nullopt;
    }
    std::string body(static_cast<std::size_t>(length), '\0');
    if (!ReadExact(pipe_, body.data(), static_cast<DWORD>(length))) {
        Fail(Win32Message(L"ReadFile(WidgetBridge body)", GetLastError()));
        return std::nullopt;
    }
    return body;
}

void WidgetBridgeClient::Fail(std::wstring message) {
    lastError_ = std::move(message);
}

bool WidgetBridgeClient::PumpEvents() {
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
            if (!HandleAsyncEvent(message, invalidations_, hostEffects_, appearanceChanges_, catalogChanges_, status)) {
                Fail(std::move(status));
                return consumed;
            }
            if (!status.empty()) lastError_ = std::move(status);
            consumed = true;
        }
    } catch (const winrt::hresult_error& error) {
        Fail(L"Invalid WidgetBridge event JSON: " + std::wstring(error.message()));
    }
    return consumed;
}

std::vector<std::wstring> WidgetBridgeClient::TakeInvalidatedWidgetIds() noexcept {
    return invalidations_.Take();
}

std::vector<WidgetHostEffect> WidgetBridgeClient::TakeHostEffects() noexcept {
    return hostEffects_.Take();
}

std::optional<long long>
WidgetBridgeClient::TakePlatformAppearanceChangedRevision() noexcept {
    return appearanceChanges_.Take();
}

std::optional<long long>
WidgetBridgeClient::TakeWidgetCatalogChangedRevision() noexcept {
    return catalogChanges_.Take();
}

void WidgetBridgeClient::RetryWidgetCatalogChangedRevision() noexcept {
    catalogChanges_.Retry();
}

void WidgetBridgeClient::AbandonWidgetCatalogChangedRevision() noexcept {
    catalogChanges_.Abandon();
}

bool WidgetBridgeClient::HasWidgetCatalogChangedRevisionInFlight() const noexcept {
    return catalogChanges_.hasInFlight();
}

} // namespace gba

#ifdef GBA_WIDGET_BRIDGE_CLIENT_TESTING
namespace gba::testing {

std::optional<std::vector<WidgetDescriptor>> ParseWidgetDescriptors(
    const std::string_view payloadUtf8,
    std::wstring& error) {
    try {
        const auto payload = JsonObject::Parse(winrt::to_hstring(payloadUtf8));
        return gba::ParseWidgetDescriptors(payload, error);
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
        return gba::ParsePlatformAppearance(payload, error);
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
        auto snapshot = ParseSnapshot(payload.GetNamedObject(L"snapshot"));
        if (payload.HasKey(L"renderStyles")) {
            ApplyComputedStyles(snapshot.root, payload.GetNamedObject(L"renderStyles"));
        }
        error.clear();
        return snapshot;
    } catch (const winrt::hresult_error& exception) {
        error = L"Invalid widget snapshot JSON: " + std::wstring(exception.message());
        return std::nullopt;
    }
}

std::optional<WidgetHostEffect> ParseWidgetHostEffectEvent(
    const std::string_view eventUtf8,
    std::wstring& error) {
    try {
        const auto event = JsonObject::Parse(winrt::to_hstring(eventUtf8));
        WidgetInvalidationQueue invalidations;
        WidgetHostEffectQueue effects;
        PlatformAppearanceRevisionTracker appearance;
        WidgetCatalogRevisionTracker catalog;
        std::wstring status;
        if (!HandleAsyncEvent(event, invalidations, effects, appearance, catalog, status)) {
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

} // namespace gba::testing
#endif
