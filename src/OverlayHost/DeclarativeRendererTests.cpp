#include "DeclarativeRenderer.h"
#include "RemoteImageCache.h"

#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <iterator>
#include <limits>
#include <string_view>
#include <utility>
#include <vector>

namespace {

using gba::DeclarativeRenderer;
using gba::NativeImageFit;
using gba::NativeObjectPosition;
using gba::WidgetNode;
using gba::WidgetSnapshot;
using gba::declarative::Rect;
using gba::declarative::Size;

int checks = 0;

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void Near(const float actual, const float expected, const std::string_view message) {
    Check(std::abs(actual - expected) <= 0.01F, message);
}

WidgetNode Node(const wchar_t* id, const wchar_t* kind) {
    WidgetNode result;
    result.id = id;
    result.kind = kind;
    return result;
}

gba::WidgetStyleValue Length(const double number, std::wstring unit = L"px") {
    return {L"length", std::to_wstring(number) + unit, number, std::move(unit)};
}

gba::WidgetStyleValue LengthList(const wchar_t* value) {
    return {L"lengthList", value, std::nullopt, {}};
}

gba::WidgetStyleValue Number(const double number) {
    return {L"number", std::to_wstring(number), number, {}};
}

gba::WidgetStyleValue Duration(const double milliseconds) {
    return {L"duration", std::to_wstring(milliseconds) + L"ms",
            milliseconds, L"ms"};
}

gba::WidgetStyleValue Keyword(const wchar_t* value) {
    return {L"keyword", value, std::nullopt, {}};
}

gba::WidgetStyleValue Color(const wchar_t* value) {
    return {L"color", value, std::nullopt, {}};
}

void ImagePlacementMath() {
    const Rect destination{10.0F, 20.0F, 100.0F, 100.0F};
    auto cover = DeclarativeRenderer::ComputeImagePlacement(
        Size{200.0F, 100.0F}, destination,
        NativeImageFit::Cover, NativeObjectPosition::Center);
    Near(cover.destination.x, 10.0F, "cover destination x");
    Near(cover.source.x, 50.0F, "cover centered crop x");
    Near(cover.source.width, 100.0F, "cover crop width");

    auto leftCover = DeclarativeRenderer::ComputeImagePlacement(
        Size{200.0F, 100.0F}, destination,
        NativeImageFit::Cover, NativeObjectPosition::Left);
    Near(leftCover.source.x, 0.0F, "cover honors left object position");

    auto contain = DeclarativeRenderer::ComputeImagePlacement(
        Size{200.0F, 100.0F}, destination,
        NativeImageFit::Contain, NativeObjectPosition::Center);
    Near(contain.destination.x, 10.0F, "contain x");
    Near(contain.destination.y, 45.0F, "contain centers vertically");
    Near(contain.destination.width, 100.0F, "contain width");
    Near(contain.destination.height, 50.0F, "contain height");

    auto intrinsic = DeclarativeRenderer::ComputeImagePlacement(
        Size{200.0F, 100.0F}, destination,
        NativeImageFit::None, NativeObjectPosition::Center);
    Near(intrinsic.destination.width, 100.0F, "none clips destination");
    Near(intrinsic.source.x, 50.0F, "none offsets source crop");

    auto invalid = DeclarativeRenderer::ComputeImagePlacement(
        Size{0.0F, 100.0F}, destination,
        NativeImageFit::Fill, NativeObjectPosition::Center);
    Near(invalid.source.width, 0.0F, "invalid source is empty");
}

void ButtonContentPlacementCentersThePrimaryLabel() {
    const Rect content{0.0F, 0.0F, 170.0F, 60.0F};
    const auto iconAndText = DeclarativeRenderer::ComputeButtonContentPlacement(
        content, 28.0F, 56.0F, true, true, false);
    Near(iconAndText.leading.x, 21.0F,
         "leading icon sits immediately before the centered label");
    Near(iconAndText.text.x, 57.0F,
         "button label starts from its exact centered visual bound");
    Near(iconAndText.text.x + iconAndText.text.width * 0.5F,
         85.0F,
         "button label is centered in the complete button");
    Near(iconAndText.leading.y, 16.0F,
         "leading icon is vertically centered in the content box");

    const auto withCue = DeclarativeRenderer::ComputeButtonContentPlacement(
        content, 28.0F, 56.0F, true, true, true);
    Near(withCue.leading.x, 21.0F,
         "trailing state cue does not reserve nonexistent leading space");
    Near(withCue.text.x, 57.0F,
         "state cue preserves the centered button label when it does not collide");

    const auto compactWithCue = DeclarativeRenderer::ComputeButtonContentPlacement(
        {0.0F, 0.0F, 122.0F, 44.0F}, 28.0F, 45.0F, true, true, true);
    Near(compactWithCue.text.width, 45.0F,
         "compact disabled icon-label button retains the complete measured label");
    Check(compactWithCue.text.x + compactWithCue.text.width <= 91.5F,
         "compact button label remains clear of the trailing state cue");

    const auto textOnly = DeclarativeRenderer::ComputeButtonContentPlacement(
        content, 0.0F, 40.0F, false, true, false);
    Near(textOnly.text.x, 65.0F, "text-only button label is centered by default");
    Near(textOnly.text.width, 40.0F, "text-only button keeps measured label width");

    const auto start = DeclarativeRenderer::ComputeButtonContentPlacement(
        content, 28.0F, 56.0F, true, true, false, gba::NativeTextAlign::Start);
    Near(start.leading.x, 0.0F, "explicit start aligns the complete visual group");
    const auto end = DeclarativeRenderer::ComputeButtonContentPlacement(
        content, 28.0F, 56.0F, true, true, false, gba::NativeTextAlign::End);
    Near(end.text.x + end.text.width, 170.0F,
         "explicit end aligns the complete visual group");

    const auto invalid = DeclarativeRenderer::ComputeButtonContentPlacement(
        {0.0F, 0.0F, -1.0F, 40.0F}, 28.0F, 40.0F, true, true, false);
    Near(invalid.leading.width, 0.0F, "invalid button geometry fails closed");
    Near(invalid.text.width, 0.0F, "invalid button text geometry fails closed");
}

void AccessibleStatePresentation() {
    gba::NativeAccessibilityPolicy normal;
    Near(gba::DeclarativeStateOpacityFactor(true, false, normal), 0.45F,
         "standard disabled content remains muted");
    Near(gba::DeclarativeStateOpacityFactor(false, true, normal), 0.72F,
         "standard busy content remains muted");
    Check(!gba::UseAccessibleDeclarativeStateCue(normal),
          "standard presentation retains muted disabled cue");

    gba::NativeAccessibilityPolicy reducedTransparency;
    reducedTransparency.reducedTransparency = true;
    Near(gba::DeclarativeStateOpacityFactor(true, false, reducedTransparency), 1.0F,
         "reduced transparency does not fade disabled content");
    Check(gba::UseAccessibleDeclarativeStateCue(reducedTransparency),
          "reduced transparency uses resolved disabled cue foreground");

    gba::NativeAccessibilityPolicy highContrast;
    highContrast.contrastHook = [](gba::NativeColor color, gba::NativeColor) {
        return color;
    };
    Near(gba::DeclarativeStateOpacityFactor(false, true, highContrast), 1.0F,
         "high contrast does not fade busy content");
    Check(gba::UseAccessibleDeclarativeStateCue(highContrast),
          "high contrast uses policy-owned disabled cue foreground");
}

void PressedComputedStyleLayersOnFocusedState() {
    auto button = Node(L"play", L"button");
    button.baseStyle = {
        {L"opacity", Number(0.4)},
        {L"scale", Number(1.0)},
    };
    button.focusedStyle = {
        {L"opacity", Number(0.7)},
        {L"scale", Number(1.08)},
    };
    button.pressedStyle = {
        {L"opacity", Number(1.0)},
    };

    const auto base = gba::ResolveDeclarativeComputedStyle(button, false, false);
    Near(static_cast<float>(*base.at(L"opacity").number), 0.4F,
         "base state is unchanged");

    const auto focused = gba::ResolveDeclarativeComputedStyle(button, true, false);
    Near(static_cast<float>(*focused.at(L"opacity").number), 0.7F,
         "focused state overrides base");

    const auto pressed = gba::ResolveDeclarativeComputedStyle(button, true, true);
    Near(static_cast<float>(*pressed.at(L"opacity").number), 1.0F,
         "pressed state overrides focused");
    Near(static_cast<float>(*pressed.at(L"scale").number), 1.08F,
         "pressed state retains focused properties");

    const auto invalid = gba::ResolveDeclarativeComputedStyle(button, false, true);
    Near(static_cast<float>(*invalid.at(L"opacity").number), 0.4F,
         "pressed cannot style a non-focused node");
}

void PlanningMetadataAndKinds() {
    WidgetSnapshot snapshot;
    snapshot.root = Node(L"root", L"stack");
    auto row = Node(L"toolbar", L"row");
    auto enabled = Node(L"play", L"button");
    enabled.text = L"Play";
    enabled.actionId = L"play";
    enabled.glyph = L"play";
    auto disabled = Node(L"locked", L"button");
    disabled.text = L"Locked";
    disabled.isDisabled = true;
    auto illustrated = Node(L"illustrated", L"button");
    illustrated.text = L"Application with a complete long name";
    illustrated.actionId = L"launch";
    illustrated.imageSource =
        L"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ"
        L"AAAADUlEQVR42mP8z8BQDwAFgwJ/lK3Q7wAAAABJRU5ErkJggg==";
    illustrated.imageFit = L"contain";
    row.children = {enabled, disabled, illustrated};

    auto label = Node(L"label", L"text");
    label.text = L"Generic declarative renderer";
    auto progress = Node(L"progress", L"progress");
    progress.hasProgress = true;
    progress.value = 1.0;
    progress.maximum = 4.0;
    auto spacer = Node(L"space", L"spacer");
    auto image = Node(L"art", L"image");
    image.imageSource = L"https://example.test/art.png";
    auto icon = Node(L"status", L"icon");
    icon.glyph = L"check";
    snapshot.root.children = {row, label, progress, spacer, image, icon};

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    gba::DeclarativeRenderOptions accessibilityOptions;
    accessibilityOptions.collectAccessibility = true;
    const auto first = renderer.Render(
        nullptr, snapshot, L"play", {0.0F, 0.0F, 960.0F, 540.0F},
        accessibilityOptions);
    Check(!first.succeeded, "null target is reported as unsuccessful");
    Check(first.hitRegions.size() == 3, "buttons produce hit regions");
    Check(first.accessibilityRegions.size() == 7,
          "visible controls and semantic content retain accessibility geometry");
    const auto ordinaryFrame = renderer.Render(
        nullptr, snapshot, L"play", {0.0F, 0.0F, 960.0F, 540.0F});
    Check(ordinaryFrame.accessibilityRegions.empty(),
          "ordinary frames do not retain accessibility geometry");
    Check(first.focusRects.size() == 3, "buttons produce focus rectangles");
    Check(first.currentFocusRect.has_value(), "focused button produces current focus rect");
    Check(first.hitRegions[0].enabled, "normal button is enabled");
    Check(!first.hitRegions[1].enabled, "disabled button is not actionable");
    Check(first.navigationEnabled.at(L"locked"),
          "disabled button remains controller navigable");
    Check(first.focusRects.contains(L"play"), "focus metadata uses stable widget ID");
    Check(first.focusRects.at(L"play").width > 0.0F, "planned button has positive width");
    Check(first.focusRects.at(L"illustrated").width >= 44.0F,
          "leading artwork and long label share one complete button focus target");

    for (const auto& diagnostic : first.diagnostics)
        Check(diagnostic.code != L"unknown_kind", "all public node kinds are recognized");

    const auto second = renderer.Render(nullptr, snapshot, L"play", {0.0F, 0.0F, 960.0F, 540.0F});
    Near(second.focusRects.at(L"play").x, first.focusRects.at(L"play").x,
        "planning is deterministic (x)");
    Near(second.focusRects.at(L"play").y, first.focusRects.at(L"play").y,
        "planning is deterministic (y)");
}

void ResponsiveVisibilityExcludesInactiveSubtrees() {
    WidgetSnapshot snapshot;
    snapshot.instanceId = L"responsive.instance";
    snapshot.activeInputScopeId = L"root";
    snapshot.root = Node(L"root", L"stack");
    snapshot.root.inputScopeId = L"root";

    auto compact = Node(L"compact.branch", L"stack");
    compact.visibleWhen = L"compactOnly";
    auto compactButton = Node(L"compact.action", L"button");
    compactButton.text = L"Compact action";
    compactButton.accessibilityLabel = L"Compact-only action";
    compactButton.actionId = L"compact.activate";
    compactButton.shortcuts.push_back({L"x", L"compact.shortcut", L"pressed"});
    auto compactCopy = Node(L"compact.copy", L"text");
    compactCopy.text = L"Compact-only accessibility copy";
    compactCopy.accessibilityLabel = L"Compact-only accessibility copy";
    compact.children = {compactButton, compactCopy};

    auto expanded = Node(L"expanded.branch", L"stack");
    expanded.visibleWhen = L"expandedOnly";
    auto expandedButton = Node(L"expanded.action", L"button");
    expandedButton.text = L"Expanded action";
    expandedButton.accessibilityLabel = L"Expanded-only action";
    expandedButton.actionId = L"expanded.activate";
    expandedButton.shortcuts.push_back({L"y", L"expanded.shortcut", L"pressed"});
    auto expandedCopy = Node(L"expanded.copy", L"text");
    expandedCopy.text = L"Expanded-only accessibility copy";
    expandedCopy.accessibilityLabel = L"Expanded-only accessibility copy";
    expanded.children = {expandedButton, expandedCopy};
    snapshot.root.children = {compact, expanded};

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    gba::DeclarativeRenderOptions accessibilityOptions;
    accessibilityOptions.collectAccessibility = true;
    const auto expandedResult = renderer.Render(
        nullptr, snapshot, L"expanded.action", {0.0F, 0.0F, 960.0F, 540.0F},
        accessibilityOptions);
    Check(expandedResult.elementRects.contains(L"expanded.branch"),
          "expanded branch participates at the exact non-compact threshold");
    Check(expandedResult.elementRects.contains(L"expanded.copy"),
          "expanded presentational/accessibility content participates");
    Check(expandedResult.focusRects.contains(L"expanded.action"),
          "expanded action participates in focus and hit-test geometry");
    Check(std::any_of(
              expandedResult.accessibilityRegions.begin(),
              expandedResult.accessibilityRegions.end(),
              [](const auto& region) { return region.nodeId == L"expanded.copy"; }) &&
          std::none_of(
              expandedResult.accessibilityRegions.begin(),
              expandedResult.accessibilityRegions.end(),
              [](const auto& region) { return region.nodeId == L"compact.copy"; }),
          "only the active responsive branch retains accessibility geometry");
    Check(!expandedResult.elementRects.contains(L"compact.branch") &&
          !expandedResult.elementRects.contains(L"compact.copy") &&
          !expandedResult.focusRects.contains(L"compact.action") &&
          !expandedResult.navigationRects.contains(L"compact.action") &&
          !expandedResult.focusScopes.contains(L"compact.action"),
          "inactive compact subtree owns no layout, paint, focus, shortcut target, or accessibility geometry");

    const auto compactResult = renderer.Render(
        nullptr, snapshot, L"compact.action", {0.0F, 0.0F, 959.0F, 540.0F},
        accessibilityOptions);
    Check(compactResult.elementRects.contains(L"compact.branch") &&
          compactResult.elementRects.contains(L"compact.copy") &&
          compactResult.focusRects.contains(L"compact.action"),
          "compact branch participates below the existing 960-DIP breakpoint");
    Check(!compactResult.elementRects.contains(L"expanded.branch") &&
          !compactResult.elementRects.contains(L"expanded.copy") &&
          !compactResult.focusRects.contains(L"expanded.action") &&
          !compactResult.navigationRects.contains(L"expanded.action") &&
          !compactResult.focusScopes.contains(L"expanded.action"),
          "inactive expanded subtree owns no layout, paint, focus, shortcut target, or accessibility geometry");

    const auto compactByHeight = renderer.Render(
        nullptr, snapshot, L"compact.action", {0.0F, 0.0F, 1200.0F, 539.0F});
    Check(compactByHeight.focusRects.contains(L"compact.action") &&
          !compactByHeight.focusRects.contains(L"expanded.action"),
          "height uses the same existing 540-DIP compact breakpoint");

    gba::DeclarativeRenderOptions surfaceOptions;
    surfaceOptions.responsiveViewport = Size{980.0F, 560.0F};
    const auto footerReducedContent = renderer.Render(
        nullptr, snapshot, L"expanded.action",
        {0.0F, 0.0F, 960.0F, 505.0F}, surfaceOptions);
    Check(footerReducedContent.focusRects.contains(L"expanded.action") &&
          !footerReducedContent.focusRects.contains(L"compact.action"),
          "responsive branches use the pre-footer surface rather than the reduced content viewport");
}

void ResponsiveNavigationShellFitsBoundedSurfaces() {
    WidgetSnapshot snapshot;
    snapshot.instanceId = L"navigation-shell.instance";
    snapshot.activeInputScopeId = L"navigation-shell.scope";
    snapshot.root = Node(L"navigation-shell", L"stack");
    snapshot.root.inputScopeId = snapshot.activeInputScopeId;
    snapshot.root.baseStyle = {
        {L"gap", LengthList(L"10px")},
        {L"min-width", Length(0)},
        {L"min-height", Length(0)},
        {L"overflow", Keyword(L"clip")},
    };

    auto compact = Node(L"navigation-shell.compact", L"row");
    compact.visibleWhen = L"compactOnly";
    compact.baseStyle = {
        {L"min-width", Length(0)},
        {L"min-height", Length(50)},
        {L"flex-shrink", Number(0)},
        {L"gap", LengthList(L"2px")},
        {L"padding", LengthList(L"3px")},
        {L"overflow", Keyword(L"clip")},
    };

    auto rail = Node(L"navigation-shell.rail", L"stack");
    rail.visibleWhen = L"expandedOnly";
    rail.baseStyle = {
        {L"width", Length(156)},
        {L"min-height", Length(0)},
        {L"flex-shrink", Number(0)},
        {L"gap", LengthList(L"4px")},
        {L"padding", LengthList(L"8px")},
        {L"overflow", Keyword(L"clip")},
    };

    constexpr std::wstring_view labels[] = {
        L"Overview", L"Controls", L"Tiles", L"Utilities",
    };
    for (std::size_t index = 0; index < std::size(labels); ++index) {
        const auto action = L"navigation-shell.destination-" + std::to_wstring(index);
        const auto compactId = L"navigation-shell.compact-" + std::to_wstring(index);
        auto compactItem = Node(compactId.c_str(), L"button");
        compactItem.text = labels[index];
        compactItem.accessibilityLabel = labels[index];
        compactItem.actionId = action;
        compactItem.focusPersistenceId =
            L"navigation-shell.focus-" + std::to_wstring(index);
        compactItem.baseStyle = {
            {L"min-width", Length(0)},
            {L"min-height", Length(44)},
            {L"flex-grow", Number(1)},
            {L"flex-shrink", Number(1)},
            {L"padding", LengthList(L"9px 10px")},
            {L"max-lines", Number(1)},
            {L"text-overflow", Keyword(L"ellipsis")},
        };
        compact.children.push_back(std::move(compactItem));

        const auto railId = L"navigation-shell.rail-" + std::to_wstring(index);
        auto railItem = Node(railId.c_str(), L"button");
        railItem.text = labels[index];
        railItem.accessibilityLabel = labels[index];
        railItem.actionId = action;
        railItem.focusPersistenceId =
            L"navigation-shell.focus-" + std::to_wstring(index);
        railItem.baseStyle = {
            {L"width", Length(140)},
            {L"min-width", Length(0)},
            {L"min-height", Length(44)},
            {L"flex-shrink", Number(0)},
            {L"padding", LengthList(L"9px 11px")},
            {L"max-lines", Number(1)},
            {L"text-overflow", Keyword(L"ellipsis")},
        };
        rail.children.push_back(std::move(railItem));
    }

    auto content = Node(L"navigation-shell.content", L"stack");
    content.baseStyle = {
        {L"min-width", Length(0)},
        {L"min-height", Length(0)},
        {L"flex-grow", Number(1)},
        {L"flex-shrink", Number(1)},
        {L"overflow", Keyword(L"clip")},
    };
    auto contentAction = Node(L"navigation-shell.content-action", L"button");
    contentAction.text = L"Shared content action with responsive text";
    contentAction.accessibilityLabel = contentAction.text;
    contentAction.actionId = L"content.open";
    contentAction.baseStyle = {
        {L"min-width", Length(0)},
        {L"min-height", Length(44)},
        {L"padding", LengthList(L"10px 14px")},
        {L"max-lines", Number(2)},
    };
    content.children = {contentAction};

    auto body = Node(L"navigation-shell.body", L"row");
    body.baseStyle = {
        {L"min-width", Length(0)},
        {L"min-height", Length(0)},
        {L"flex-grow", Number(1)},
        {L"flex-shrink", Number(1)},
        {L"gap", LengthList(L"10px")},
        {L"overflow", Keyword(L"clip")},
    };
    body.children = {rail, content};
    snapshot.root.children = {compact, body};

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    struct Scenario final {
        Size viewport;
        float textScale;
        bool compact;
    };
    constexpr Scenario scenarios[] = {
        {{320.0F, 280.0F}, 1.0F, true},
        {{760.0F, 540.0F}, 1.0F, true},
        {{960.0F, 540.0F}, 1.0F, false},
        {{960.0F, 540.0F}, 1.5F, false},
    };
    for (const auto& scenario : scenarios) {
        gba::DeclarativeRenderOptions options;
        options.responsiveViewport = scenario.viewport;
        options.accessibility.textScale = scenario.textScale;
        const auto focused = scenario.compact
            ? L"navigation-shell.compact-1"
            : L"navigation-shell.rail-1";
        const auto result = renderer.Render(
            nullptr, snapshot, focused,
            {0.0F, 0.0F, scenario.viewport.width, scenario.viewport.height},
            options);
        const auto activePrefix = scenario.compact
            ? std::wstring_view{L"navigation-shell.compact-"}
            : std::wstring_view{L"navigation-shell.rail-"};
        const auto inactivePrefix = scenario.compact
            ? std::wstring_view{L"navigation-shell.rail-"}
            : std::wstring_view{L"navigation-shell.compact-"};
        for (std::size_t index = 0; index < std::size(labels); ++index) {
            const auto active = std::wstring(activePrefix) + std::to_wstring(index);
            const auto inactive = std::wstring(inactivePrefix) + std::to_wstring(index);
            Check(result.focusRects.contains(active),
                "active responsive navigation item remains visible");
            Check(result.focusRects.at(active).height >= 44.0F,
                "responsive navigation retains a 44-DIP controller target");
            Check(!result.focusRects.contains(inactive) &&
                  !result.focusScopes.contains(inactive),
                "inactive navigation presentation owns no focus or accessibility target");
        }
        Check(result.focusRects.contains(L"navigation-shell.content-action"),
            "one shared content subtree remains visible in both responsive modes");
        const auto& focusRect = result.focusRects.at(focused);
        Check(focusRect.x >= 0.0F && focusRect.y >= 0.0F &&
              focusRect.x + focusRect.width <= scenario.viewport.width + 0.01F &&
              focusRect.y + focusRect.height <= scenario.viewport.height + 0.01F,
            "responsive navigation focus remains inside the bounded viewport");
    }
}

void SliderPlanningAndAccessibilityTargets() {
    WidgetSnapshot snapshot;
    snapshot.instanceId = L"slider.runtime";
    snapshot.activeInputScopeId = L"root";
    snapshot.root = Node(L"root", L"stack");
    auto slider = Node(L"volume", L"slider");
    slider.hasProgress = true;
    slider.hasSliderRange = true;
    slider.minimum = 0.0;
    slider.maximum = 1.0;
    slider.value = 0.5;
    slider.step = 0.1;
    slider.valueChangedActionId = L"volume.changed";
    slider.accessibilityLabel = L"Game volume, unmuted, press A to mute";
    slider.accessibilityValue = L"50 percent";
    slider.baseStyle = {
        {L"height", Length(8)},
        {L"width", Length(240)},
    };
    snapshot.root.children = {slider};

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    gba::DeclarativeRenderOptions options;
    options.pixelScale = 1.25F;
    options.sliderValueOverrides.emplace(L"volume", 0.8);
    const auto normal = renderer.Render(
        nullptr, snapshot, L"volume", {0.0F, 0.0F, 320.0F, 80.0F}, options);
    Check(normal.focusRects.contains(L"volume"), "Slider is a focus target");
    Check(normal.focusRects.at(L"volume").height >= 44.0F,
          "Slider enforces a 44-DIP controller target despite narrow track style");
    Check(normal.navigationEnabled.at(L"volume"), "Slider is navigable");

    snapshot.root.children[0].isDisabled = true;
    const auto disabled = renderer.Render(
        nullptr, snapshot, L"volume", {0.0F, 0.0F, 320.0F, 80.0F}, options);
    Check(disabled.focusRects.contains(L"volume") &&
              disabled.navigationEnabled.at(L"volume"),
          "disabled Slider retains exact focus and navigation geometry");
    Check(!disabled.hitRegions.front().enabled,
          "disabled Slider suppresses activation/hit action");

    snapshot.root.children[0].isDisabled = false;
    snapshot.root.children[0].isBusy = true;
    const auto busy = renderer.Render(
        nullptr, snapshot, L"volume", {0.0F, 0.0F, 320.0F, 80.0F}, options);
    Check(busy.focusRects.contains(L"volume") && busy.navigationEnabled.at(L"volume"),
          "busy Slider retains exact focus while adjustment is pending");
    Check(!busy.hitRegions.front().enabled, "busy Slider suppresses activation");

    const auto clipped = renderer.Render(
        nullptr, snapshot, L"volume", {0.0F, 0.0F, 320.0F, 0.25F}, options);
    Check(!clipped.focusRects.contains(L"volume"),
          "fully clipped Slider is excluded even though disabled/busy states remain navigable");
}

WidgetSnapshot RichActionSurfaceSnapshot(const wchar_t* orientation) {
    WidgetSnapshot snapshot;
    snapshot.sequence = 7;
    snapshot.instanceId = L"tiles.runtime.v1";
    snapshot.activeInputScopeId = L"tiles.root";
    snapshot.initialFocusId = L"album.tile";
    snapshot.root = Node(L"tiles.root", L"stack");
    snapshot.root.inputScopeId = L"tiles.root";
    snapshot.root.baseStyle = {
        {L"padding", LengthList(L"8px")},
        {L"overflow", Keyword(L"clip")},
    };

    auto tile = Node(L"album.tile", L"actionSurface");
    tile.actionId = L"open-album";
    tile.accessibilityLabel = L"Open Album title by Artist";
    tile.actionSurfaceOrientation = orientation;
    tile.baseStyle = {
        {L"width", Length(280)},
        {L"min-height", Length(72)},
        {L"padding", LengthList(L"8px")},
        {L"gap", LengthList(L"8px")},
        {L"border-width", Length(1)},
        {L"border-color", Color(L"#8899aa")},
        {L"corner-radius", Length(10)},
        {L"flex-shrink", Number(0)},
    };
    tile.focusedStyle = {
        {L"outline-width", Length(2)},
        {L"outline-offset", Length(-2)},
        {L"outline-color", Color(L"#ffffff")},
    };
    tile.pressedStyle = {{L"scale", Number(0.98)}};

    auto artwork = Node(L"album.artwork", L"icon");
    artwork.glyph = L"music";
    artwork.baseStyle = {
        {L"width", Length(56)},
        {L"height", Length(56)},
        {L"flex-shrink", Number(0)},
    };
    auto copy = Node(L"album.copy", L"stack");
    copy.baseStyle = {
        {L"width", Length(300)},
        {L"gap", LengthList(L"3px")},
        {L"flex-shrink", Number(0)},
    };
    auto title = Node(L"album.title", L"text");
    title.text = L"Album title";
    auto artist = Node(L"album.artist", L"text");
    artist.text = L"Artist";
    copy.children = {std::move(title), std::move(artist)};
    tile.children = {std::move(artwork), std::move(copy)};
    snapshot.root.children = {std::move(tile)};
    return snapshot;
}

void ActionSurfacePlanningAndInteractionGeometry() {
    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    auto horizontalSnapshot = RichActionSurfaceSnapshot(L"horizontal");
    const auto horizontal = renderer.Render(
        nullptr, horizontalSnapshot, L"album.tile",
        {0.0F, 0.0F, 340.0F, 180.0F});

    Check(horizontal.hitRegions.size() == 1,
          "ActionSurface subtree produces one pointer hit region");
    Check(horizontal.focusRects.size() == 1 &&
              horizontal.focusRects.contains(L"album.tile"),
          "ActionSurface subtree produces one full-tile focus target");
    Check(horizontal.navigationRects.size() == 1 &&
              horizontal.navigationRects.contains(L"album.tile"),
          "ActionSurface subtree produces one controller navigation target");
    Check(!horizontal.focusRects.contains(L"album.title") &&
              !horizontal.navigationRects.contains(L"album.artwork"),
          "ActionSurface descendants remain presentational");
    Check(horizontal.currentFocusRect.has_value(),
          "focused ActionSurface publishes current focus geometry");
    const auto tileRect = horizontal.elementRects.at(L"album.tile");
    const auto focusRect = horizontal.focusRects.at(L"album.tile");
    const auto navigationRect = horizontal.navigationRects.at(L"album.tile");
    Near(focusRect.x, tileRect.x, "ActionSurface focus uses the full parent x");
    Near(focusRect.y, tileRect.y, "ActionSurface focus uses the full parent y");
    Near(focusRect.width, tileRect.width,
         "ActionSurface focus uses the full parent width");
    Near(focusRect.height, tileRect.height,
         "ActionSurface focus uses the full parent height");
    Near(navigationRect.width, tileRect.width,
         "ActionSurface navigation uses the full parent width");
    Near(horizontal.hitRegions.front().rect.width, tileRect.width,
         "ActionSurface hit testing uses the full parent width");
    Check(navigationRect.width >= 44.0F && navigationRect.height >= 44.0F,
          "ActionSurface enforces the 44-DIP minimum target");
    Check(horizontal.elementRects.at(L"album.artwork").x <
              horizontal.elementRects.at(L"album.copy").x,
          "horizontal ActionSurface lays presentational children side by side");
    const auto copyVisible = horizontal.elementVisibleRects.at(L"album.copy");
    Check(copyVisible.x + copyVisible.width <= tileRect.x + tileRect.width + 0.01F,
          "ActionSurface clips oversized presentational content to its parent");
    for (const auto& diagnostic : horizontal.diagnostics) {
        Check(diagnostic.code != L"unknown_kind",
              "ActionSurface and its public descendants are recognized");
    }

    auto verticalSnapshot = RichActionSurfaceSnapshot(L"vertical");
    const auto vertical = renderer.Render(
        nullptr, verticalSnapshot, L"album.tile",
        {0.0F, 0.0F, 340.0F, 240.0F});
    Check(vertical.elementRects.at(L"album.artwork").y <
              vertical.elementRects.at(L"album.copy").y,
          "vertical ActionSurface lays presentational children top to bottom");
    Check(vertical.hitRegions.size() == 1 && vertical.focusRects.size() == 1,
          "vertical orientation preserves the single full-tile target");

    horizontalSnapshot.root.children[0].isSelected = true;
    const auto selected = renderer.Render(
        nullptr, horizontalSnapshot, L"album.tile",
        {0.0F, 0.0F, 340.0F, 180.0F});
    Check(selected.hitRegions.front().enabled,
          "selected ActionSurface remains actionable");
    horizontalSnapshot.root.children[0].isDisabled = true;
    const auto disabled = renderer.Render(
        nullptr, horizontalSnapshot, L"album.tile",
        {0.0F, 0.0F, 340.0F, 180.0F});
    Check(disabled.navigationEnabled.at(L"album.tile") &&
              disabled.focusRects.contains(L"album.tile"),
          "disabled ActionSurface retains stable controller focus");
    Check(!disabled.hitRegions.front().enabled,
          "disabled ActionSurface suppresses pointer activation");
    horizontalSnapshot.root.children[0].isDisabled = false;
    horizontalSnapshot.root.children[0].isBusy = true;
    const auto busy = renderer.Render(
        nullptr, horizontalSnapshot, L"album.tile",
        {0.0F, 0.0F, 340.0F, 180.0F});
    Check(busy.navigationEnabled.at(L"album.tile") &&
              !busy.hitRegions.front().enabled,
          "busy ActionSurface stays navigable while activation is suppressed");

    horizontalSnapshot.root.children[0].isBusy = false;
    const auto clipped = renderer.Render(
        nullptr, horizontalSnapshot, L"album.tile",
        {0.0F, 0.0F, 340.0F, 32.0F});
    Check(clipped.navigationRects.at(L"album.tile").height >= 44.0F,
          "clipping never shrinks the logical ActionSurface controller target");
    Check(clipped.focusRects.at(L"album.tile").height <
              clipped.navigationRects.at(L"album.tile").height,
          "visible ActionSurface focus geometry is clipped to the viewport");
    Check(clipped.hitRegions.front().rect.y + clipped.hitRegions.front().rect.height <= 32.01F,
          "clipped ActionSurface hit testing never escapes the viewport");
}

WidgetSnapshot ResponsiveGridSnapshot() {
    WidgetSnapshot snapshot;
    snapshot.sequence = 8;
    snapshot.instanceId = L"grid.runtime.v1";
    snapshot.activeInputScopeId = L"grid.root";
    snapshot.initialFocusId = L"grid.one";
    snapshot.root = Node(L"grid.root", L"stack");
    snapshot.root.inputScopeId = L"grid.root";

    auto grid = Node(L"grid", L"grid");
    grid.gridMinimumColumnWidth = 140.0;
    grid.gridMaximumColumns = 3U;
    grid.baseStyle = {
        {L"width", Length(100, L"%")},
        {L"gap", LengthList(L"10px 12px")},
        {L"padding", LengthList(L"4px")},
    };
    for (const auto* id : {L"grid.one", L"grid.two", L"grid.three", L"grid.four"}) {
        auto button = Node(id, L"button");
        button.text = id;
        button.actionId = id;
        button.baseStyle = {
            {L"min-height", Length(44)},
            {L"padding", LengthList(L"8px")},
        };
        grid.children.push_back(std::move(button));
    }
    snapshot.root.children = {std::move(grid)};
    return snapshot;
}

void ResponsiveGridFlowsThroughNativePlanning() {
    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    const auto snapshot = ResponsiveGridSnapshot();
    const auto wide = renderer.Render(
        nullptr, snapshot, L"grid.one", {0.0F, 0.0F, 760.0F, 260.0F});
    Check(wide.focusRects.size() == 4 && !wide.focusRects.contains(L"grid"),
          "Grid exposes its focusable children without becoming a focus stop");
    const auto one = wide.elementRects.at(L"grid.one");
    const auto two = wide.elementRects.at(L"grid.two");
    const auto three = wide.elementRects.at(L"grid.three");
    const auto four = wide.elementRects.at(L"grid.four");
    Check(one.x < two.x && two.x < three.x,
          "wide Grid uses stable row-major columns");
    Near(one.y, two.y, "first Grid row shares a stable y coordinate");
    Near(two.y, three.y, "maximum three Grid columns share the first row");
    Check(four.y > one.y && four.x <= one.x + 0.01F,
          "Grid maximum-column cap wraps the fourth child to the next row");
    for (const auto& diagnostic : wide.diagnostics)
        Check(diagnostic.code != L"unknown_kind", "Grid is a recognized native container");

    const auto narrow = renderer.Render(
        nullptr, snapshot, L"grid.one", {0.0F, 0.0F, 150.0F, 420.0F});
    const auto narrowOne = narrow.elementRects.at(L"grid.one");
    const auto narrowTwo = narrow.elementRects.at(L"grid.two");
    Near(narrowOne.x, narrowTwo.x, "narrow Grid falls back to one column");
    Check(narrowTwo.y > narrowOne.y,
          "narrow Grid preserves document order down the single column");
    Check(narrow.focusRects.contains(L"grid.four"),
          "Grid reflow preserves the complete child focus graph");
}

void FocusMotionUsesStableSnapshotIdentity() {
    WidgetSnapshot snapshot;
    snapshot.sequence = 1;
    snapshot.instanceId = L"motion.widget@1";
    snapshot.activeInputScopeId = L"root";
    snapshot.root = Node(L"root", L"stack");
    auto button = Node(L"play", L"button");
    button.text = L"Play";
    button.actionId = L"play";
    button.baseStyle = {
        {L"opacity", Number(0.5)},
        {L"scale", Number(1.0)},
        {L"transition-duration", Duration(100.0)},
        {L"transition-easing", Keyword(L"linear")},
    };
    button.focusedStyle = {
        {L"opacity", Number(1.0)},
        {L"scale", Number(1.1)},
    };
    snapshot.root.children = {button};

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    gba::DeclarativeRenderOptions options;
    options.animationTimestampMilliseconds = 0;
    const auto initial = renderer.Render(
        nullptr, snapshot, {}, {0.0F, 0.0F, 320.0F, 100.0F}, options);
    Check(!initial.animationActive,
          "first declarative observation snaps without an entrance animation");

    options.animationTimestampMilliseconds = 10;
    const auto focused = renderer.Render(
        nullptr, snapshot, L"play", {0.0F, 0.0F, 320.0F, 100.0F}, options);
    Check(focused.animationActive,
          "focused opacity and scale state starts its GBSS transition");

    // Snapshot sequence changes do not reset motion. Runtime instance + exact
    // node ID are the stable identity across worker publication revisions.
    snapshot.sequence = 2;
    options.animationTimestampMilliseconds = 60;
    const auto revised = renderer.Render(
        nullptr, snapshot, L"play", {0.0F, 0.0F, 320.0F, 100.0F}, options);
    Check(revised.animationActive,
          "same node continues motion across snapshot sequence changes");

    options.animationTimestampMilliseconds = 110;
    const auto settled = renderer.Render(
        nullptr, snapshot, L"play", {0.0F, 0.0F, 320.0F, 100.0F}, options);
    Check(!settled.animationActive,
          "settled focus motion does not request another host frame");

    options.animationTimestampMilliseconds = 120;
    const auto reversing = renderer.Render(
        nullptr, snapshot, {}, {0.0F, 0.0F, 320.0F, 100.0F}, options);
    Check(reversing.animationActive,
          "focus departure retargets toward the base opacity and scale");
    options.animationTimestampMilliseconds = 130;
    options.accessibility.reducedMotion = true;
    const auto reduced = renderer.Render(
        nullptr, snapshot, {}, {0.0F, 0.0F, 320.0F, 100.0F}, options);
    Check(!reduced.animationActive,
          "reduced motion cancels an in-flight declarative transition");

    renderer.ForgetWidgetState(snapshot.instanceId);
    options.accessibility.reducedMotion = false;
    options.animationTimestampMilliseconds = 140;
    const auto replaced = renderer.Render(
        nullptr, snapshot, L"play", {0.0F, 0.0F, 320.0F, 100.0F}, options);
    Check(!replaced.animationActive,
          "forgotten widget runtime starts from its current authored state");
}

void SubtreeTranslationKeepsPresentationGeometryAligned() {
    WidgetSnapshot snapshot;
    snapshot.sequence = 1;
    snapshot.instanceId = L"translation.widget@1";
    snapshot.activeInputScopeId = L"root";
    snapshot.initialFocusId = L"translated.button";
    snapshot.root = Node(L"root", L"stack");
    snapshot.root.baseStyle = {
        {L"overflow", Keyword(L"clip")},
        {L"translate-x", Length(20.0)},
        {L"translate-y", Length(10.0)},
        {L"transition-duration", Duration(100.0)},
        {L"transition-easing", Keyword(L"linear")},
    };
    auto button = Node(L"translated.button", L"button");
    button.text = L"Translated";
    button.actionId = L"translated.action";
    button.baseStyle = {
        {L"translate-x", Length(-30.0)},
        {L"translate-y", Length(5.0)},
        {L"min-height", Length(44.0)},
    };
    snapshot.root.children = {button};

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    gba::DeclarativeRenderOptions options;
    options.animationTimestampMilliseconds = 0;
    const auto initial = renderer.Render(
        nullptr, snapshot, L"translated.button",
        {0.0F, 0.0F, 200.0F, 100.0F}, options);
    const auto rootRect = initial.elementRects.at(L"root");
    const auto buttonRect = initial.elementRects.at(L"translated.button");
    Near(rootRect.x, 20.0F, "root presentation translation moves its border");
    Near(rootRect.y, 10.0F, "root presentation translation moves its vertical border");
    Near(buttonRect.x, -10.0F,
         "child presentation geometry accumulates parent and local x translation");
    Near(buttonRect.y, 15.0F,
         "child presentation geometry accumulates parent and local y translation");

    const auto visible = initial.elementVisibleRects.at(L"translated.button");
    Near(visible.x, 20.0F,
         "translated clipping ancestor moves the child visibility boundary");
    Near(visible.y, 15.0F,
         "translated child remains visible at its presented vertical position");
    Check(initial.hitRegions.size() == 1,
          "translated control produces one bounded pointer target");
    Near(initial.hitRegions.front().rect.x, visible.x,
         "pointer geometry uses the exact translated visible rectangle");
    Near(initial.focusRects.at(L"translated.button").x, visible.x,
         "focus geometry uses the exact translated visible rectangle");
    Near(initial.navigationRects.at(L"translated.button").x, buttonRect.x,
         "controller navigation uses complete translated geometry");
    Near(initial.currentFocusRect->x, visible.x,
         "current focus geometry follows translated clipping");
    Check(initial.currentFocusOutlineClip.has_value(),
          "translated clipping ancestor produces a focus-outline clip");
    Near(initial.currentFocusOutlineClip->x, 20.0F,
         "focus-outline clip moves with the translated ancestor subtree");
}

void TranslationRetargetsAndSnapsDeterministically() {
    WidgetSnapshot snapshot;
    snapshot.sequence = 1;
    snapshot.instanceId = L"translation.motion@1";
    snapshot.activeInputScopeId = L"root";
    snapshot.root = Node(L"root", L"stack");
    auto button = Node(L"moving.button", L"button");
    button.text = L"Move";
    button.actionId = L"move";
    button.baseStyle = {
        {L"translate-x", Length(10.0)},
        {L"translate-y", Length(-8.0)},
        {L"transition-duration", Duration(100.0)},
        {L"transition-easing", Keyword(L"linear")},
    };
    snapshot.root.children = {button};

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    gba::DeclarativeRenderOptions options;
    options.animationTimestampMilliseconds = 0;
    auto result = renderer.Render(
        nullptr, snapshot, L"moving.button",
        {0.0F, 0.0F, 240.0F, 100.0F}, options);
    Near(result.elementRects.at(L"moving.button").x, 10.0F,
         "first stable-ID translation observation snaps to its target");
    Check(!result.animationActive,
          "first translation observation does not manufacture an entrance loop");

    snapshot.sequence = 2;
    snapshot.root.children.front().baseStyle[L"translate-x"] = Length(50.0);
    snapshot.root.children.front().baseStyle[L"translate-y"] = Length(12.0);
    options.animationTimestampMilliseconds = 10;
    result = renderer.Render(
        nullptr, snapshot, L"moving.button",
        {0.0F, 0.0F, 240.0F, 100.0F}, options);
    Near(result.elementRects.at(L"moving.button").x, 10.0F,
         "stable-ID target change starts at the currently presented x");
    Check(result.animationActive,
          "changed translation target requests a bounded follow-up frame");

    options.animationTimestampMilliseconds = 60;
    options.pixelScale = 2.0F;
    result = renderer.Render(
        nullptr, snapshot, L"moving.button",
        {0.0F, 0.0F, 480.0F, 180.0F}, options);
    Near(result.elementRects.at(L"moving.button").x, 30.0F,
         "translation midpoint is deterministic across resize and DPI change");
    Near(result.elementRects.at(L"moving.button").y, 2.0F,
         "vertical translation midpoint is deterministic across resize and DPI change");

    options.animationTimestampMilliseconds = 70;
    options.accessibility.reducedMotion = true;
    snapshot.root.children.front().baseStyle[L"translate-x"] = Length(-24.0);
    snapshot.root.children.front().baseStyle[L"translate-y"] = Length(6.0);
    result = renderer.Render(
        nullptr, snapshot, L"moving.button",
        {0.0F, 0.0F, 480.0F, 180.0F}, options);
    Near(result.elementRects.at(L"moving.button").x, -24.0F,
         "reduced motion cancels and snaps x translation to the new target");
    Near(result.elementRects.at(L"moving.button").y, 6.0F,
         "reduced motion cancels and snaps y translation to the new target");
    Check(!result.animationActive,
          "reduced-motion translation cannot keep the host frame loop active");

    options.accessibility.reducedMotion = false;
    options.animationTimestampMilliseconds = 80;
    snapshot.instanceId = L"translation.motion@2";
    snapshot.root.children.front().baseStyle[L"translate-x"] = Length(64.0);
    result = renderer.Render(
        nullptr, snapshot, L"moving.button",
        {0.0F, 0.0F, 480.0F, 180.0F}, options);
    Near(result.elementRects.at(L"moving.button").x, 64.0F,
         "replacement widget identity snaps instead of inheriting stale motion");
    Check(!result.animationActive,
          "replacement identity does not retain a hidden predecessor animation");
}

void TranslatedFocusConvergesInsideScrollViewport() {
    WidgetSnapshot snapshot;
    snapshot.instanceId = L"translation.scroll@1";
    snapshot.activeInputScopeId = L"scroll";
    snapshot.initialFocusId = L"translated.focus";
    snapshot.root = Node(L"root", L"stack");
    auto scroll = Node(L"scroll", L"scroll");
    scroll.scrollAxis = L"vertical";
    scroll.inputScopeId = L"scroll";
    scroll.baseStyle = {{L"height", Length(80.0)}};
    auto leading = Node(L"leading", L"spacer");
    leading.baseStyle = {{L"height", Length(40.0)}};
    auto focused = Node(L"translated.focus", L"button");
    focused.text = L"Focus";
    focused.actionId = L"focus";
    focused.baseStyle = {
        {L"height", Length(44.0)},
        {L"translate-y", Length(50.0)},
    };
    auto trailing = Node(L"trailing", L"spacer");
    trailing.baseStyle = {{L"height", Length(100.0)}};
    scroll.children = {leading, focused, trailing};
    snapshot.root.children = {scroll};

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    gba::DeclarativeRenderOptions options;
    options.animationTimestampMilliseconds = 0;
    const auto result = renderer.Render(
        nullptr, snapshot, L"translated.focus",
        {0.0F, 0.0F, 240.0F, 120.0F}, options);
    Check(result.scrollOffsets.at(L"scroll") > 40.0F,
          "focus follow accounts for presentation translation, not only static layout");
    const auto visible = result.focusRects.at(L"translated.focus");
    Check(visible.y >= -0.01F && visible.y + visible.height <= 80.01F,
          "translated focused control converges inside its scroll viewport");
}

void ClippedControlsAreNotFocusCandidates() {
    WidgetSnapshot snapshot;
    snapshot.root = Node(L"root", L"stack");
    auto visible = Node(L"visible", L"button");
    visible.text = L"Visible";
    visible.actionId = L"visible";
    visible.baseStyle = {
        {L"height", Length(44)},
        {L"min-height", Length(44)},
        {L"flex-shrink", Number(0)},
    };
    auto clipped = Node(L"clipped", L"button");
    clipped.text = L"Clipped";
    clipped.actionId = L"clipped";
    clipped.baseStyle = visible.baseStyle;
    snapshot.root.children = {visible, clipped};

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    const auto result = renderer.Render(
        nullptr, snapshot, L"visible", {0.0F, 0.0F, 240.0F, 40.0F});
    Check(result.focusRects.contains(L"visible"),
          "visible control remains a controller focus candidate");
    Check(!result.focusRects.contains(L"clipped"),
          "fully clipped control is excluded from controller focus");
}

void ControllerScrollFollowsFocusAndRestoresState() {
    WidgetSnapshot snapshot;
    snapshot.instanceId = L"audio.runtime.v1";
    snapshot.activeInputScopeId = L"root";
    snapshot.root = Node(L"sessions", L"scroll");
    snapshot.root.scrollAxis = L"vertical";
    for (int index = 0; index < 8; ++index) {
        auto button = Node((L"session-" + std::to_wstring(index)).c_str(), L"button");
        button.text = L"Session";
        button.actionId = button.id;
        button.baseStyle = {
            {L"height", Length(44)},
            {L"min-height", Length(44)},
            {L"flex-shrink", Number(0)},
        };
        snapshot.root.children.push_back(std::move(button));
    }

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    float priorOffset = -1.0F;
    for (int index = 0; index < 8; ++index) {
        const auto focused = L"session-" + std::to_wstring(index);
        const auto result = renderer.Render(
            nullptr, snapshot, focused, {0.0F, 0.0F, 240.0F, 100.0F});
        Check(result.navigationRects.size() == 8,
              "all scroll descendants remain controller navigation candidates");
        Check(result.revealableFocusIds.contains(focused),
              "focused session is marked host-revealable");
        Check(result.focusRects.contains(focused),
              "focus-follow reveals every focused session at a constrained height");
        const auto rect = result.focusRects.at(focused);
        Check(rect.y >= -0.01F && rect.y + rect.height <= 100.01F,
              "focused session is wholly inside the clipped viewport");
        const auto offset = result.scrollOffsets.at(L"sessions");
        Check(offset >= priorOffset, "downward focus produces monotonic bounded offsets");
        priorOffset = offset;
    }
    Check(priorOffset > 0.0F, "trailing session requires a nonzero offset");

    // Closing/reopening does not destroy the renderer or widget runtime. The
    // stable instance/scope/container key therefore restores the same offset.
    const auto reopened = renderer.Render(
        nullptr, snapshot, L"session-7", {0.0F, 0.0F, 240.0F, 100.0F});
    Near(reopened.scrollOffsets.at(L"sessions"), priorOffset,
         "stable runtime and input scope restore scroll position");

    auto otherScope = snapshot;
    otherScope.activeInputScopeId = L"details";
    const auto independent = renderer.Render(
        nullptr, otherScope, L"session-0", {0.0F, 0.0F, 240.0F, 100.0F});
    Near(independent.scrollOffsets.at(L"sessions"), 0.0F,
         "nested input scopes own independent offsets");
    const auto returned = renderer.Render(
        nullptr, snapshot, L"session-7", {0.0F, 0.0F, 240.0F, 100.0F});
    Near(returned.scrollOffsets.at(L"sessions"), priorOffset,
         "returning from a nested scope restores root scroll position");

    auto replacement = snapshot;
    replacement.instanceId = L"audio.runtime.v2";
    const auto fresh = renderer.Render(
        nullptr, replacement, L"session-0", {0.0F, 0.0F, 240.0F, 100.0F});
    Near(fresh.scrollOffsets.at(L"sessions"), 0.0F,
         "runtime replacement cannot inherit stale scroll state");
    renderer.ForgetWidgetState(snapshot.instanceId);

    auto invalid = snapshot;
    invalid.root.scrollAxis = L"diagonal";
    const auto failed = renderer.Render(
        nullptr, invalid, L"session-0", {0.0F, 0.0F, 240.0F, 100.0F});
    Check(std::any_of(failed.diagnostics.begin(), failed.diagnostics.end(), [](const auto& item) {
        return item.code == L"invalid_scroll_axis" &&
            item.severity == gba::RenderDiagnosticSeverity::Error;
    }), "invalid native scroll axis fails closed with an error");
}

WidgetNode FixedSpacer(const wchar_t* id, const double height) {
    auto spacer = Node(id, L"spacer");
    spacer.baseStyle = {
        {L"height", Length(height)},
        {L"min-height", Length(height)},
        {L"flex-shrink", Number(0)},
    };
    return spacer;
}

WidgetNode FixedButton(const wchar_t* id, const double height = 44.0) {
    auto button = Node(id, L"button");
    button.text = L"Action";
    button.actionId = id;
    button.baseStyle = {
        {L"height", Length(height)},
        {L"min-height", Length(height)},
        {L"flex-shrink", Number(0)},
    };
    return button;
}

void WholeWidgetScrollRevealsAudioMixerControls() {
    WidgetSnapshot snapshot;
    snapshot.instanceId = L"audio-mixer.runtime";
    snapshot.activeInputScopeId = L"audio-mixer";
    snapshot.root = Node(L"audio.root", L"scroll");
    snapshot.root.inputScopeId = L"audio-mixer";
    snapshot.root.scrollAxis = L"vertical";
    snapshot.root.baseStyle = {
        {L"gap", Length(8)},
    };

    auto master = FixedButton(L"audio.master.volume.slider");
    auto input = FixedButton(L"audio.input.volume.slider");
    auto sessions = Node(L"audio.sessions.list", L"stack");
    sessions.baseStyle = {
        {L"gap", Length(6)},
        {L"flex-shrink", Number(0)},
    };
    std::vector<std::wstring> focusOrder{
        master.id,
        input.id,
    };
    for (int index = 0; index < 8; ++index) {
        const auto cardId = L"audio.session." + std::to_wstring(index) + L".row";
        const auto sliderId = L"audio.session." + std::to_wstring(index) + L".volume.slider";
        auto card = Node(cardId.c_str(), L"stack");
        card.baseStyle = {
            {L"height", Length(74)},
            {L"min-height", Length(74)},
            {L"flex-shrink", Number(0)},
        };
        card.children = {FixedButton(sliderId.c_str())};
        sessions.children.push_back(std::move(card));
        focusOrder.push_back(sliderId);
    }
    snapshot.root.children = {
        FixedSpacer(L"audio.header", 50),
        std::move(master),
        FixedSpacer(L"audio.devices.card", 69),
        std::move(input),
        FixedSpacer(L"audio.sessions.heading", 14),
        std::move(sessions),
    };

    // 464 DIP is the content height produced by the Audio Mixer's preferred
    // 520-DIP panel after host footer chrome. 304 DIP represents a constrained
    // monitor. In both cases the one root Scroll must reveal every control;
    // fixed, non-scroll content must never starve a nested application list.
    for (const auto height : {464.0F, 304.0F}) {
        DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
        float priorOffset = -1.0F;
        for (const auto& focused : focusOrder) {
            const auto result = renderer.Render(
                nullptr, snapshot, focused, {0.0F, 0.0F, 520.0F, height});
            Check(result.focusRects.contains(focused),
                  "whole-widget audio scroll reveals every controller target");
            const auto rect = result.focusRects.at(focused);
            Check(rect.y >= -0.01F && rect.y + rect.height <= height + 0.01F,
                  "revealed audio target remains wholly inside the host viewport");
            const auto offset = result.scrollOffsets.at(L"audio.root");
            Check(offset + 0.01F >= priorOffset,
                  "audio focus order advances through a monotonic root offset");
            priorOffset = offset;
        }
        Check(priorOffset > 0.0F,
              "final audio session requires whole-widget scrolling");

        const auto returned = renderer.Render(
            nullptr, snapshot, focusOrder.front(), {0.0F, 0.0F, 520.0F, height});
        Near(returned.scrollOffsets.at(L"audio.root"), 0.0F,
             "returning to master output restores the true audio leading edge");
        Check(returned.focusRects.contains(focusOrder.front()),
              "master output remains visible after returning from the last app");
    }
}

void SegmentedTabsSurviveConstrainedNetworkSurfaces() {
    WidgetSnapshot snapshot;
    snapshot.instanceId = L"network-controls.runtime";
    snapshot.activeInputScopeId = L"network-controls";
    snapshot.root = Node(L"network.root", L"stack");
    snapshot.root.inputScopeId = L"network-controls";
    snapshot.root.baseStyle = {
        {L"gap", LengthList(L"8px")},
        {L"padding", LengthList(L"14px")},
    };

    auto tabs = Node(L"network.tabs", L"row");
    tabs.baseStyle = {
        {L"gap", LengthList(L"3px")},
        {L"padding", LengthList(L"3px")},
        {L"min-height", Length(50)},
        {L"flex-shrink", Number(0)},
    };
    for (const auto& [id, label] : {
             std::pair{L"network.tab.wifi", L"Wi-Fi"},
             std::pair{L"network.tab.bluetooth", L"Bluetooth"},
         }) {
        auto tab = Node(id, L"button");
        tab.text = label;
        tab.actionId = L"network.tab.select";
        tab.baseStyle = {
            {L"min-width", Length(96)},
            {L"min-height", Length(44)},
            {L"padding", LengthList(L"9px 14px")},
            {L"flex-grow", Number(1)},
        };
        tab.focusedStyle = {
            {L"outline-width", Length(2)},
            {L"outline-offset", Length(-2)},
        };
        tabs.children.push_back(std::move(tab));
    }

    auto body = Node(L"network.wifi.body.scroll", L"scroll");
    body.scrollAxis = L"vertical";
    body.baseStyle = {
        {L"min-height", Length(120)},
        {L"flex-grow", Number(1)},
        {L"flex-shrink", Number(1)},
        {L"gap", LengthList(L"8px")},
    };
    for (int index = 0; index < 12; ++index) {
        body.children.push_back(FixedSpacer(
            (L"network.row." + std::to_wstring(index)).c_str(), 60));
    }
    snapshot.root.children = {
        FixedSpacer(L"network.header", 60),
        FixedSpacer(L"network.connection.card", 64),
        std::move(tabs),
        std::move(body),
    };

    struct Scenario final {
        float viewportHeight;
        float pixelScale;
        float textScale;
    };
    // 364 DIPs models the standard compact panel content. At maximum interface
    // scale the work-area clamp can reduce the normalized logical viewport, so
    // 300 DIPs plus a 1.25 physical-pixel scale covers that constrained path.
    for (const auto scenario : {
             Scenario{364.0F, 1.0F, 1.0F},
             Scenario{300.0F, 1.25F, 1.5F},
         }) {
        DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
        gba::DeclarativeRenderOptions options;
        options.pixelScale = scenario.pixelScale;
        options.accessibility.textScale = scenario.textScale;
        for (const auto* focused : {L"network.tab.wifi", L"network.tab.bluetooth"}) {
            const auto result = renderer.Render(
                nullptr, snapshot, focused,
                {0.0F, 0.0F, 560.0F, scenario.viewportHeight}, options);
            Check(result.focusRects.contains(focused),
                  "segmented tab remains visible and focusable under vertical pressure");
            Check(result.navigationEnabled.at(focused),
                  "segmented tab remains controller navigable at constrained scale");
            const auto rect = result.focusRects.at(focused);
            Check(rect.height >= 44.0F,
                  "segmented tab preserves its controller target and label height");
            Check(rect.y >= 0.0F && rect.y + rect.height <= scenario.viewportHeight,
                  "segmented tab remains wholly inside the constrained viewport");
            Check(rect.x >= 16.0F && rect.x + rect.width <= 544.0F,
                  "segmented tab retains inset room for an unclipped focus border");
        }
    }
}

void CenteredWrappedStatePreservesTextFlowAndControllerTarget() {
    WidgetSnapshot snapshot;
    snapshot.instanceId = L"empty-state.runtime";
    snapshot.activeInputScopeId = L"empty-state";
    snapshot.root = Node(L"state-root", L"stack");
    snapshot.root.inputScopeId = L"empty-state";
    snapshot.root.baseStyle = {
        {L"gap", LengthList(L"8px")},
        {L"padding", LengthList(L"14px 16px")},
        {L"overflow", Keyword(L"clip")},
    };

    auto header = Node(L"state-header", L"stack");
    header.baseStyle = {
        {L"gap", LengthList(L"2px")},
        {L"flex-shrink", Number(0)},
    };
    auto eyebrow = Node(L"state-eyebrow", L"text");
    eyebrow.text = L"LIBRARY";
    eyebrow.baseStyle = {
        {L"font-size", Length(10)},
        {L"max-lines", Number(1)},
    };
    auto heading = Node(L"state-heading", L"text");
    heading.text = L"Games & Apps";
    heading.baseStyle = {
        {L"font-size", Length(24)},
        {L"line-height", Number(1.2)},
        {L"max-lines", Number(1)},
    };
    header.children = {std::move(eyebrow), std::move(heading)};

    auto state = Node(L"state-card", L"stack");
    state.baseStyle = {
        {L"gap", LengthList(L"8px")},
        {L"padding", LengthList(L"16px")},
        {L"align", Keyword(L"center")},
        {L"justify", Keyword(L"center")},
        {L"min-height", Length(230)},
        {L"flex-grow", Number(1)},
    };

    auto icon = Node(L"state-icon", L"icon");
    icon.glyph = L"play";
    icon.baseStyle = {
        {L"width", Length(28)},
        {L"height", Length(28)},
    };

    auto title = Node(L"state-title", L"text");
    title.text = L"Installed apps could not be loaded";
    title.baseStyle = {
        {L"font-size", Length(18)},
        {L"line-height", Number(1.2)},
        {L"max-width", Length(220)},
        {L"max-lines", Number(2)},
        {L"text-align", Keyword(L"center")},
    };
    auto help = Node(L"state-help", L"text");
    help.text = L"Try again. No paths or command lines were exposed, and the trusted provider returned no private details.";
    help.baseStyle = {
        {L"font-size", Length(13)},
        {L"line-height", Number(1.35)},
        {L"max-width", Length(220)},
        {L"max-lines", Number(3)},
        {L"text-align", Keyword(L"center")},
    };
    auto action = Node(L"state-action", L"button");
    action.text = L"Try again";
    action.actionId = L"retry";
    action.glyph = L"refresh";
    action.baseStyle = {
        {L"min-width", Length(150)},
        {L"min-height", Length(44)},
        {L"padding", LengthList(L"10px 14px")},
        {L"max-lines", Number(2)},
    };
    state.children = {
        std::move(icon), std::move(title), std::move(help), std::move(action)};
    snapshot.root.children = {std::move(header), std::move(state)};

    struct Scenario final {
        float width;
        float height;
        float textScale;
    };
    for (const auto scenario : {
             Scenario{420.0F, 336.0F, 0.85F},
             Scenario{620.0F, 374.0F, 1.0F},
             Scenario{760.0F, 540.0F, 1.5F},
         }) {
        DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
        gba::DeclarativeRenderOptions options;
        options.accessibility.textScale = scenario.textScale;
        const auto result = renderer.Render(
            nullptr, snapshot, L"state-action",
            {0.0F, 0.0F, scenario.width, scenario.height}, options);
        Check(result.navigationRects.contains(L"state-action"),
              "wrapped state retains its controller action in the navigation graph");
        Check(result.navigationRects.at(L"state-action").height >= 44.0F,
              "wrapped action retains at least the controller target height");
        for (const auto* id : {
                 L"state-icon", L"state-title", L"state-help", L"state-action"}) {
            Check(result.elementRects.contains(id) &&
                      result.elementVisibleRects.contains(id),
                  "state diagnostics expose every centered content box");
            const auto rect = result.elementRects.at(id);
            const auto visible = result.elementVisibleRects.at(id);
            Near(visible.x, rect.x,
                 "centered content does not clip its leading horizontal edge");
            Near(visible.y, rect.y,
                 "centered content does not clip its leading vertical edge");
            Near(visible.width, rect.width,
                 "centered content keeps its complete border-box width");
            Near(visible.height, rect.height,
                 "centered content keeps its complete border-box height");
        }
        const auto& titleRect = result.elementRects.at(L"state-title");
        const auto& helpRect = result.elementRects.at(L"state-help");
        const auto& actionRect = result.elementRects.at(L"state-action");
        Check(titleRect.height > 30.0F * scenario.textScale,
              "authored max-width reflows the state title before intrinsic height is fixed");
        Check(helpRect.height > 30.0F * scenario.textScale,
              "authored max-width reflows the state detail before intrinsic height is fixed");
        Check(helpRect.y >= titleRect.y + titleRect.height + 7.99F,
              "wrapped detail follows the complete title without overlap");
        Check(actionRect.y >= helpRect.y + helpRect.height + 7.99F,
              "action follows the complete wrapped detail without overlap");
    }
}

void SpotifyStateAndSetupCardsPreserveWrappedTextHeight() {
    const gba::WidgetComputedStyle rootStyle{
        {L"gap", LengthList(L"10px")},
        {L"padding", LengthList(L"18px 20px")},
        {L"overflow", Keyword(L"clip")},
    };
    const gba::WidgetComputedStyle cardStyle{
        {L"min-height", Length(300)},
        {L"gap", LengthList(L"9px")},
        {L"padding", LengthList(L"28px")},
        {L"align", Keyword(L"center")},
        {L"justify", Keyword(L"center")},
    };

    WidgetSnapshot stateSnapshot;
    stateSnapshot.instanceId = L"spotify-state.runtime";
    stateSnapshot.root = Node(L"spotify.root", L"stack");
    stateSnapshot.root.baseStyle = rootStyle;
    auto stateCard = Node(L"spotify.state-card", L"stack");
    stateCard.baseStyle = cardStyle;
    auto stateIcon = Node(L"spotify.state-icon", L"icon");
    stateIcon.glyph = L"settings";
    stateIcon.baseStyle = {
        {L"width", Length(34)}, {L"height", Length(34)},
        {L"padding", LengthList(L"5px")},
    };
    auto stateTitle = Node(L"spotify.state-title", L"text");
    stateTitle.text = L"Client ID required";
    stateTitle.baseStyle = {
        {L"font-size", Length(20)}, {L"font-weight", Number(500)},
        {L"max-lines", Number(2)},
        {L"text-align", Keyword(L"center")},
    };
    auto stateDetail = Node(L"spotify.state-detail", L"text");
    stateDetail.text =
        L"Add your own Spotify developer Client ID. No client secret belongs in this widget.";
    stateDetail.baseStyle = {
        {L"max-width", Length(520)}, {L"font-size", Length(12)},
        {L"max-lines", Number(3)}, {L"text-align", Keyword(L"center")},
    };
    auto setupAction = FixedButton(L"spotify.setup.open");
    setupAction.text = L"Setup instructions";
    stateCard.children = {
        std::move(stateIcon), std::move(stateTitle),
        std::move(stateDetail), std::move(setupAction)};
    stateSnapshot.root.children = {std::move(stateCard)};

    WidgetSnapshot setupSnapshot;
    setupSnapshot.instanceId = L"spotify-setup.runtime";
    setupSnapshot.root = Node(L"spotify.setup-root", L"stack");
    setupSnapshot.root.baseStyle = rootStyle;
    auto setupCard = Node(L"spotify.setup-card", L"stack");
    setupCard.baseStyle = cardStyle;
    setupCard.baseStyle[L"align"] = Keyword(L"start");
    auto setupTitle = Node(L"spotify.setup-title", L"text");
    setupTitle.text = L"Connect your developer app";
    setupTitle.baseStyle = {
        {L"font-size", Length(20)}, {L"font-weight", Number(500)},
        {L"max-lines", Number(2)},
    };
    auto setupStep = Node(L"spotify.setup-step-2", L"text");
    setupStep.text =
        L"2. Register this exact redirect URI: http://127.0.0.1:43821/callback";
    setupStep.baseStyle = {
        {L"font-size", Length(13)}, {L"max-lines", Number(2)},
    };
    auto setupCommand = Node(L"spotify.setup-command", L"text");
    setupCommand.text =
        L"CLI: gbar config set org.gbar.samples.spotify client-id YOUR_CLIENT_ID --publisher org.gbar.samples";
    setupCommand.baseStyle = {
        {L"width", Length(100, L"%")}, {L"padding", LengthList(L"11px 13px")},
        {L"font-size", Length(11)}, {L"max-lines", Number(2)},
    };
    auto done = FixedButton(L"spotify.setup.close");
    done.text = L"Done";
    setupCard.children = {
        std::move(setupTitle), std::move(setupStep),
        std::move(setupCommand), std::move(done)};
    setupSnapshot.root.children = {std::move(setupCard)};

    struct Scenario final {
        float width;
        float height;
        float textScale;
    };
    for (const auto scenario : {
             Scenario{420.0F, 430.0F, 1.0F},
             Scenario{760.0F, 540.0F, 1.5F},
         }) {
        gba::DeclarativeRenderOptions options;
        options.accessibility.textScale = scenario.textScale;
        DeclarativeRenderer stateRenderer{nullptr, nullptr, nullptr};
        const auto stateResult = stateRenderer.Render(
            nullptr, stateSnapshot, L"spotify.setup.open",
            {0.0F, 0.0F, scenario.width, scenario.height}, options);
        Check(stateResult.elementRects.at(L"spotify.state-title").height >=
                  20.0F * scenario.textScale,
              "Spotify client-ID title retains its complete intrinsic line height");
        Check(stateResult.elementRects.at(L"spotify.state-detail").height >=
                  20.0F * scenario.textScale,
              "Spotify client-ID detail retains its wrapped intrinsic height");

        DeclarativeRenderer setupRenderer{nullptr, nullptr, nullptr};
        const auto setupResult = setupRenderer.Render(
            nullptr, setupSnapshot, L"spotify.setup.close",
            {0.0F, 0.0F, scenario.width, scenario.height}, options);
        for (const auto* id : {
                 L"spotify.setup-title", L"spotify.setup-step-2",
                 L"spotify.setup-command"}) {
            const auto& rect = setupResult.elementRects.at(id);
            const auto& visible = setupResult.elementVisibleRects.at(id);
            Near(visible.height, rect.height,
                 "Spotify setup text keeps its complete visible box");
        }
        Check(setupResult.elementRects.at(L"spotify.setup-title").height >=
                  20.0F * scenario.textScale,
              "Spotify setup title retains its complete intrinsic line height");
        Check(setupResult.elementRects.at(L"spotify.setup-step-2").height >
                  20.0F * scenario.textScale,
              "Spotify setup step retains its wrapped intrinsic height");
        Check(setupResult.elementRects.at(L"spotify.setup-command").height >
                  28.0F * scenario.textScale,
              "Spotify setup command retains wrapped text plus padding");
    }
}

void ResponsiveRowWrapFlowsThroughGbssAndNativePlanning() {
    WidgetSnapshot snapshot;
    snapshot.instanceId = L"responsive-wrap.runtime";
    snapshot.root = Node(L"wrap.root", L"row");
    snapshot.root.baseStyle = {
        {L"flex-wrap", Keyword(L"wrap")},
        {L"gap", LengthList(L"8px 12px")},
        {L"align", Keyword(L"start")},
    };
    for (int index = 0; index < 3; ++index) {
        auto action = Node(
            (L"wrap.action." + std::to_wstring(index)).c_str(), L"button");
        action.text = L"Responsive action";
        action.actionId = L"activate";
        action.baseStyle = {
            {L"width", Length(112)},
            {L"height", Length(44)},
            {L"flex-shrink", Number(0)},
        };
        snapshot.root.children.push_back(std::move(action));
    }

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    const auto narrow = renderer.Render(
        nullptr, snapshot, L"wrap.action.0", {0.0F, 0.0F, 250.0F, 120.0F});
    Check(narrow.elementRects.contains(L"wrap.root") &&
              narrow.elementRects.contains(L"wrap.action.2"),
          "GBSS wrapped row reaches native layout planning");
    Near(narrow.elementRects.at(L"wrap.action.1").x, 124.0F,
         "GBSS column gap reaches native row geometry");
    Near(narrow.elementRects.at(L"wrap.action.2").x, 0.0F,
         "third action starts the second responsive line");
    Near(narrow.elementRects.at(L"wrap.action.2").y, 52.0F,
         "GBSS row gap reaches native wrapped-line geometry");
    Check(narrow.navigationRects.contains(L"wrap.action.2"),
          "wrapped actions remain controller navigation candidates");

    const auto wide = renderer.Render(
        nullptr, snapshot, L"wrap.action.0", {0.0F, 0.0F, 380.0F, 120.0F});
    Near(wide.elementRects.at(L"wrap.action.2").x, 248.0F,
         "wider host surface returns all actions to one line");
    Near(wide.elementRects.at(L"wrap.action.2").y, 0.0F,
         "responsive row does not retain stale line placement");
}

void WrappedPermissionCopyContributesToScrollExtent() {
    WidgetSnapshot snapshot;
    snapshot.instanceId = L"permission-copy.runtime";
    snapshot.activeInputScopeId = L"permission";
    snapshot.root = Node(L"permission-scroll", L"scroll");
    snapshot.root.inputScopeId = L"permission";
    snapshot.root.scrollAxis = L"vertical";
    snapshot.root.baseStyle = {{L"gap", LengthList(L"6px")}};

    auto heading = Node(L"permission-heading", L"text");
    heading.text = L"Store private connection secrets";
    heading.baseStyle = {
        {L"font-size", Length(20)},
        {L"line-height", Number(1.2)},
        {L"max-lines", Number(2)},
    };
    auto description = Node(L"permission-description", L"text");
    description.text =
        L"Create, replace, inspect metadata for, or delete package-scoped secrets in Windows Credential Manager. "
        L"Stored values are never returned to widget code, and exact-port requests remain host mediated.";
    description.baseStyle = {
        {L"font-size", Length(13)},
        {L"line-height", Number(1.35)},
        {L"max-lines", Number(12)},
    };
    auto enforcement = Node(L"permission-enforcement", L"text");
    enforcement.text =
        L"The host allows this only when identity, manifest, consent, and lifecycle state all permit it.";
    enforcement.baseStyle = description.baseStyle;
    auto revoke = FixedButton(L"permission-revoke");
    snapshot.root.children = {
        std::move(heading), std::move(description), std::move(enforcement), std::move(revoke)};

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    const auto trailing = renderer.Render(
        nullptr, snapshot, L"permission-revoke", {0.0F, 0.0F, 260.0F, 150.0F});
    Check(trailing.scrollOffsets.at(L"permission-scroll") > 80.0F,
          "wrapped permission paragraphs contribute their full height to scroll extent");
    Check(trailing.focusRects.contains(L"permission-revoke"),
          "focus-follow reveals the action after long wrapped permission copy");
    Near(trailing.focusRects.at(L"permission-revoke").height, 44.0F,
         "permission action preserves its controller target at the trailing edge");

    const auto leading = renderer.Render(
        nullptr, snapshot, {}, {0.0F, 0.0F, 260.0F, 150.0F});
    Near(leading.scrollOffsets.at(L"permission-scroll"),
         trailing.scrollOffsets.at(L"permission-scroll"),
         "stable scroll scope preserves the user position without a focus teleport");
}

void ScrollFocusReachesTrueContentBoundaries() {
    WidgetSnapshot snapshot;
    snapshot.instanceId = L"bounded-scroll.runtime";
    snapshot.activeInputScopeId = L"scroll";
    snapshot.root = Node(L"scroll", L"scroll");
    snapshot.root.scrollAxis = L"vertical";
    snapshot.root.children = {
        FixedSpacer(L"leading-content", 30),
        FixedButton(L"first"),
        FixedButton(L"middle"),
        FixedButton(L"last"),
        FixedSpacer(L"trailing-content", 30),
    };

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    const auto trailing = renderer.Render(
        nullptr, snapshot, L"last", {0.0F, 0.0F, 240.0F, 100.0F});
    Near(trailing.scrollOffsets.at(L"scroll"), 92.0F,
         "last focus target exposes the true trailing content boundary");
    Check(trailing.focusRects.contains(L"last"),
          "last focus target remains visible at the true trailing boundary");

    const auto leading = renderer.Render(
        nullptr, snapshot, L"first", {0.0F, 0.0F, 240.0F, 100.0F});
    Near(leading.scrollOffsets.at(L"scroll"), 0.0F,
         "first focus target restores the true leading content boundary");
    Check(leading.focusRects.contains(L"first"),
          "first focus target remains visible at the true leading boundary");

    const auto middle = renderer.Render(
        nullptr, snapshot, L"middle", {0.0F, 0.0F, 240.0F, 100.0F});
    Check(middle.scrollOffsets.at(L"scroll") > 0.0F &&
              middle.scrollOffsets.at(L"scroll") < 92.0F,
          "middle focus retains minimal reveal instead of snapping to an edge");
}

void NestedScrollFocusFollowReachesFixedPoint() {
    WidgetSnapshot snapshot;
    snapshot.instanceId = L"nested.runtime";
    snapshot.activeInputScopeId = L"root";
    snapshot.root = Node(L"outer", L"scroll");
    snapshot.root.scrollAxis = L"vertical";

    auto inner = Node(L"inner", L"scroll");
    inner.scrollAxis = L"vertical";
    inner.baseStyle = {
        {L"height", Length(100)},
        {L"min-height", Length(100)},
        {L"flex-shrink", Number(0)},
    };
    for (int index = 0; index < 8; ++index) {
        inner.children.push_back(FixedButton(
            (L"nested-" + std::to_wstring(index)).c_str()));
    }
    snapshot.root.children = {
        FixedSpacer(L"prefix", 100),
        std::move(inner),
        FixedSpacer(L"suffix", 500),
    };

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    const auto result = renderer.Render(
        nullptr, snapshot, L"nested-7", {0.0F, 0.0F, 240.0F, 100.0F});
    Check(result.focusRects.contains(L"nested-7"),
          "nested focus-follow reaches a visible fixed point");
    const auto visible = result.focusRects.at(L"nested-7");
    Check(visible.y >= -0.01F && visible.y + visible.height <= 100.01F,
          "nested focus ends wholly contained by the outer viewport");
    Near(visible.height, result.navigationRects.at(L"nested-7").height,
         "nested focus is fully visible rather than merely intersecting the viewport");
    Check(result.scrollOffsets.at(L"inner") > 0.0F &&
          result.scrollOffsets.at(L"outer") > 0.0F,
          "innermost and outer scroll ancestors both participate");
    Near(result.scrollOffsets.at(L"inner"), 252.0F,
         "inner scroll reaches its trailing bound");
    Near(result.scrollOffsets.at(L"outer"), 156.0F,
         "outer scroll corrects the stale pre-inner target geometry");
}

void IrrevealableClipsDoNotBecomeFocusTraps() {
    WidgetSnapshot crossAxis;
    crossAxis.instanceId = L"cross-axis.runtime";
    crossAxis.activeInputScopeId = L"root";
    crossAxis.root = Node(L"vertical-only", L"scroll");
    crossAxis.root.scrollAxis = L"vertical";
    auto displaced = FixedButton(L"cross-axis");
    displaced.baseStyle.insert_or_assign(L"width", Length(44));
    displaced.baseStyle.insert_or_assign(L"min-width", Length(44));
    displaced.baseStyle.insert_or_assign(
        L"margin", gba::WidgetStyleValue{
            L"lengthList", L"0px 0px 0px 140px", std::nullopt, {}});
    crossAxis.root.children.push_back(std::move(displaced));

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    const auto crossResult = renderer.Render(
        nullptr, crossAxis, {}, {0.0F, 0.0F, 100.0F, 80.0F});
    Check(!crossResult.focusRects.contains(L"cross-axis"),
          "cross-axis target is fully clipped");
    Check(!crossResult.revealableFocusIds.contains(L"cross-axis"),
          "vertical scrolling cannot promise horizontal reveal");

    WidgetSnapshot nestedClip;
    nestedClip.instanceId = L"nested-clip.runtime";
    nestedClip.activeInputScopeId = L"root";
    nestedClip.root = Node(L"outer-scroll", L"scroll");
    nestedClip.root.scrollAxis = L"vertical";
    auto clip = Node(L"fixed-clip", L"stack");
    clip.baseStyle = {
        {L"height", Length(44)},
        {L"min-height", Length(44)},
        {L"flex-shrink", Number(0)},
        {L"overflow", {L"keyword", L"clip", std::nullopt, {}}},
    };
    clip.children = {FixedSpacer(L"clip-prefix", 44), FixedButton(L"trapped")};
    nestedClip.root.children = {std::move(clip), FixedSpacer(L"outer-tail", 200)};

    const auto clipResult = renderer.Render(
        nullptr, nestedClip, {}, {0.0F, 0.0F, 160.0F, 100.0F});
    Check(!clipResult.focusRects.contains(L"trapped"),
          "nested non-scroll clip hides its overflow child");
    Check(!clipResult.revealableFocusIds.contains(L"trapped"),
          "an outer scroll cannot repair clipping inside a fixed nested clip");
}

void ScrollStateCapEvictsOnlyInactiveLruEntries() {
    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    WidgetSnapshot snapshot;
    snapshot.activeInputScopeId = L"root";
    snapshot.root = Node(L"sessions", L"scroll");
    snapshot.root.scrollAxis = L"vertical";
    snapshot.root.children = {
        FixedSpacer(L"leading", 44),
        FixedButton(L"trailing"),
    };

    constexpr int entryCount = 4097;
    for (int index = 0; index < entryCount; ++index) {
        snapshot.instanceId = L"cache.runtime." + std::to_wstring(index);
        const auto populated = renderer.Render(
            nullptr, snapshot, L"trailing", {0.0F, 0.0F, 120.0F, 44.0F});
        Near(populated.scrollOffsets.at(L"sessions"), 44.0F,
             "fixture stores a nonzero scroll offset");
    }

    snapshot.instanceId = L"cache.runtime.4096";
    const auto active = renderer.Render(
        nullptr, snapshot, {}, {0.0F, 0.0F, 120.0F, 44.0F});
    Near(active.scrollOffsets.at(L"sessions"), 44.0F,
         "cap transition preserves the newest active scroll state");

    snapshot.instanceId = L"cache.runtime.0";
    const auto evicted = renderer.Render(
        nullptr, snapshot, {}, {0.0F, 0.0F, 120.0F, 44.0F});
    Near(evicted.scrollOffsets.at(L"sessions"), 0.0F,
         "cap transition evicts the least-recent inactive state only");
}

void DeferredFocusOutlineUsesEffectiveVisibilityClip() {
    WidgetSnapshot normal;
    normal.root = Node(L"visible-overflow", L"stack");
    normal.root.baseStyle = {
        {L"width", Length(80)},
        {L"min-width", Length(80)},
        {L"height", Length(60)},
        {L"overflow", {L"keyword", L"visible", std::nullopt, {}}},
    };
    auto unconstrained = FixedButton(L"normal-focus");
    unconstrained.baseStyle.insert_or_assign(L"width", Length(120));
    unconstrained.baseStyle.insert_or_assign(L"min-width", Length(120));
    normal.root.children = {std::move(unconstrained)};
    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    const auto normalResult = renderer.Render(
        nullptr, normal, L"normal-focus", {0.0F, 0.0F, 160.0F, 90.0F});
    Check(normalResult.currentFocusOutlineClip.has_value(),
          "every deferred outline retains the render-surface clip");
    Near(normalResult.currentFocusOutlineClip->width, 160.0F,
         "ordinary visible-overflow ancestor does not clip focus decoration");
    Near(normalResult.currentFocusOutlineClip->height, 90.0F,
         "ordinary focus decoration keeps the full surface draw window");

    WidgetSnapshot nested;
    nested.root = Node(L"surface-root", L"stack");
    auto fixedClip = Node(L"fixed-clip", L"stack");
    fixedClip.baseStyle = {
        {L"width", Length(80)},
        {L"min-width", Length(80)},
        {L"height", Length(54)},
        {L"min-height", Length(54)},
        {L"overflow", {L"keyword", L"clip", std::nullopt, {}}},
    };
    auto clippedButton = FixedButton(L"nested-focus");
    clippedButton.baseStyle.insert_or_assign(L"width", Length(120));
    clippedButton.baseStyle.insert_or_assign(L"min-width", Length(120));
    clippedButton.focusedStyle = {
        {L"scale", Number(1.15)},
        {L"outline-width", Length(4)},
        {L"outline-offset", Length(2)},
    };
    fixedClip.children = {std::move(clippedButton)};
    nested.root.children = {std::move(fixedClip)};
    const auto nestedResult = renderer.Render(
        nullptr, nested, L"nested-focus", {0.0F, 0.0F, 160.0F, 100.0F});
    Check(nestedResult.currentFocusOutlineClip.has_value(),
          "non-scroll overflow clip constrains deferred focus decoration");
    Near(nestedResult.currentFocusOutlineClip->x, 0.0F,
         "non-scroll outline clip preserves ancestor content x");
    Near(nestedResult.currentFocusOutlineClip->y, 0.0F,
         "non-scroll outline clip preserves ancestor content y");
    Near(nestedResult.currentFocusOutlineClip->width, 80.0F,
         "non-scroll outline clip uses clipping ancestor width");
    Near(nestedResult.currentFocusOutlineClip->height, 54.0F,
         "non-scroll outline clip uses clipping ancestor height");

    WidgetSnapshot rootEdge;
    rootEdge.root = FixedButton(L"root-edge-focus");
    rootEdge.root.isSelected = true;
    rootEdge.root.focusedStyle = {
        {L"scale", Number(1.2)},
        {L"outline-width", Length(4)},
        {L"outline-offset", Length(2)},
    };
    const Rect rootViewport{10.0F, 20.0F, 120.0F, 60.0F};
    const auto rootEdgeResult = renderer.Render(
        nullptr, rootEdge, L"root-edge-focus", rootViewport);
    Check(rootEdgeResult.currentFocusOutlineClip.has_value(),
          "scaled selected root-edge control retains an effective draw clip");
    Near(rootEdgeResult.currentFocusOutlineClip->x, rootViewport.x,
         "root-edge outline cannot escape the surface left edge");
    Near(rootEdgeResult.currentFocusOutlineClip->y, rootViewport.y,
         "root-edge outline cannot escape the surface top edge");
    Near(rootEdgeResult.currentFocusOutlineClip->width, rootViewport.width,
         "root-edge outline cannot escape the surface right edge");
    Near(rootEdgeResult.currentFocusOutlineClip->height, rootViewport.height,
         "root-edge outline cannot escape the surface bottom edge");

    WidgetSnapshot oversized;
    oversized.instanceId = L"outline.runtime";
    oversized.activeInputScopeId = L"root";
    oversized.root = Node(L"outline-scroll", L"scroll");
    oversized.root.scrollAxis = L"vertical";
    oversized.root.children = {FixedButton(L"oversized-focus", 90)};
    const auto clipped = renderer.Render(
        nullptr, oversized, L"oversized-focus", {0.0F, 0.0F, 120.0F, 60.0F});
    Check(clipped.currentFocusRect.has_value() &&
          clipped.currentFocusRect->height <
              clipped.navigationRects.at(L"oversized-focus").height,
          "oversized focus exposes only its visible portion");
    Check(clipped.currentFocusOutlineClip.has_value(),
          "scroll descendant carries a deferred-outline clip");
    Near(clipped.currentFocusOutlineClip->x, 0.0F,
         "outline clip preserves scroll viewport x");
    Near(clipped.currentFocusOutlineClip->y, 0.0F,
         "outline clip preserves scroll viewport y");
    Near(clipped.currentFocusOutlineClip->width, 120.0F,
         "outline clip preserves scroll viewport width");
    Near(clipped.currentFocusOutlineClip->height, 60.0F,
         "outline clip prevents a partial ring from escaping the scroll viewport");
}

void RealDirect2DSmoke() {
    using Microsoft::WRL::ComPtr;
    ComPtr<ID2D1Factory> d2d;
    Check(SUCCEEDED(D2D1CreateFactory(
        D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf())),
        "create D2D factory");
    ComPtr<IDWriteFactory> write;
    Check(SUCCEEDED(DWriteCreateFactory(
        DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
        reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))),
        "create DirectWrite factory");
    ComPtr<IWICImagingFactory> wic;
    Check(SUCCEEDED(CoCreateInstance(
        CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
        "create WIC factory");
    ComPtr<IWICBitmap> canvas;
    Check(SUCCEEDED(wic->CreateBitmap(
        640, 360, GUID_WICPixelFormat32bppPBGRA,
        WICBitmapCacheOnLoad, canvas.ReleaseAndGetAddressOf())),
        "create WIC canvas");
    ComPtr<ID2D1RenderTarget> target;
    Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
        canvas.Get(), D2D1::RenderTargetProperties(),
        target.ReleaseAndGetAddressOf())),
        "create WIC render target");

    WidgetSnapshot snapshot;
    snapshot.root = Node(L"root", L"stack");
    auto button = Node(L"confirm", L"button");
    button.text = L"Confirm";
    button.glyph = L"check";
    auto selectedToggle = Node(L"toggle-on", L"button");
    selectedToggle.text = L"Reduced motion: On";
    selectedToggle.isSelected = true;
    auto offToggle = Node(L"toggle-off", L"button");
    offToggle.text = L"Bold text: Off";
    auto slider = Node(L"volume", L"slider");
    slider.hasProgress = true;
    slider.hasSliderRange = true;
    slider.minimum = 0.0;
    slider.maximum = 100.0;
    slider.value = 50.0;
    slider.step = 5.0;
    slider.valueChangedActionId = L"volume.changed";
    slider.accessibilityLabel = L"Volume";
    slider.accessibilityValue = L"50 percent";
    snapshot.root.children = {button, selectedToggle, offToggle, slider};

    DeclarativeRenderer renderer{d2d.Get(), write.Get(), nullptr};
    target->BeginDraw();
    target->Clear(D2D1::ColorF(0.02F, 0.02F, 0.03F, 1.0F));
    gba::DeclarativeRenderOptions options;
    options.sliderValueOverrides.emplace(L"volume", 75.0);
    const auto result = renderer.Render(
        target.Get(), snapshot, L"volume", {0.0F, 0.0F, 640.0F, 360.0F}, options);
    Check(SUCCEEDED(target->EndDraw()), "complete Direct2D draw");
    Check(result.succeeded, "real Direct2D render succeeds");
    Check(result.currentFocusRect.has_value(), "real render returns focused geometry");
    Check(result.hitRegions.size() == 4,
          "buttons and optimistic Slider render through the real Direct2D path");
    const auto& optimisticSliderRect = result.elementRects.at(L"volume");
    const auto focusedTrackInset = 10.0F;
    const auto focusedTrackWidth = std::max(
        0.0F, optimisticSliderRect.width - focusedTrackInset * 2.0F);
    Near(result.sliderThumbXs.at(L"volume"),
         optimisticSliderRect.x + focusedTrackInset + focusedTrackWidth * 0.75F,
         "optimistic Slider thumb paints at the presented value instead of snapshot value");

    WidgetSnapshot loadingSnapshot;
    loadingSnapshot.instanceId = L"loading.runtime";
    loadingSnapshot.activeInputScopeId = L"loading-root";
    loadingSnapshot.root = Node(L"loading-root", L"stack");
    auto loading = Node(L"loading", L"loadingIndicator");
    loading.accessibilityLabel = L"Loading applications";
    loading.indicatorSize = L"standard";
    loadingSnapshot.root.children = {loading};
    gba::DeclarativeRenderOptions loadingOptions;
    loadingOptions.animationTimestampMilliseconds = 225;
    target->BeginDraw();
    target->Clear(D2D1::ColorF(D2D1::ColorF::Black));
    const auto animatedLoading = renderer.Render(
        target.Get(), loadingSnapshot, {}, {0.0F, 0.0F, 120.0F, 80.0F}, loadingOptions);
    Check(SUCCEEDED(target->EndDraw()), "loading indicator frame draws");
    Check(animatedLoading.succeeded, "loading indicator renders successfully");
    Check(animatedLoading.animationActive,
          "visible loading indicator requests another shell-owned frame");
    Check(!animatedLoading.navigationRects.contains(L"loading") &&
          animatedLoading.hitRegions.empty(),
          "loading indicator is absent from focus and activation geometry");
    Near(animatedLoading.elementRects.at(L"loading").width, 24.0F,
         "standard loading indicator uses bounded intrinsic width");
    Near(animatedLoading.elementRects.at(L"loading").height, 24.0F,
         "standard loading indicator uses bounded intrinsic height");

    loadingOptions.accessibility.reducedMotion = true;
    loadingOptions.animationTimestampMilliseconds = 700;
    target->BeginDraw();
    const auto reducedLoading = renderer.Render(
        target.Get(), loadingSnapshot, {}, {0.0F, 0.0F, 120.0F, 80.0F}, loadingOptions);
    Check(SUCCEEDED(target->EndDraw()), "reduced-motion loading indicator draws");
    Check(!reducedLoading.animationActive,
          "reduced-motion loading indicator is an honest static cue");

    WidgetSnapshot hiddenLoading = loadingSnapshot;
    hiddenLoading.instanceId = L"loading.hidden.runtime";
    hiddenLoading.root = Node(L"loading-scroll", L"scroll");
    hiddenLoading.root.scrollAxis = L"vertical";
    hiddenLoading.root.children = {FixedSpacer(L"loading-prefix", 40), loading};
    loadingOptions.accessibility.reducedMotion = false;
    target->BeginDraw();
    const auto hiddenLoadingResult = renderer.Render(
        target.Get(), hiddenLoading, {}, {0.0F, 0.0F, 120.0F, 20.0F}, loadingOptions);
    Check(SUCCEEDED(target->EndDraw()), "offscreen loading layout draws");
    Check(!hiddenLoadingResult.animationActive,
          "offscreen loading indicator does not keep the shell animation cadence awake");

    WidgetSnapshot roundedSurface;
    roundedSurface.root = Node(L"rounded-root", L"stack");
    roundedSurface.root.baseStyle = {
        {L"background", Color(L"#00ff00")},
    };
    gba::DeclarativeRenderOptions roundedOptions;
    roundedOptions.surfaceCornerRadiusPx = 18.0F;
    target->BeginDraw();
    target->Clear(D2D1::ColorF(D2D1::ColorF::Black));
    const Rect roundedViewport{20.0F, 20.0F, 120.0F, 80.0F};
    const auto roundedResult = renderer.Render(
        target.Get(), roundedSurface, {}, roundedViewport, roundedOptions);
    Check(SUCCEEDED(target->EndDraw()), "rounded surface draw completes");
    Check(roundedResult.succeeded, "rounded host surface clip renders successfully");
    ComPtr<IWICBitmapLock> roundedLock;
    const WICRect roundedLockArea{0, 0, 640, 360};
    Check(SUCCEEDED(canvas->Lock(
        &roundedLockArea, WICBitmapLockRead,
        roundedLock.ReleaseAndGetAddressOf())),
        "rounded surface bitmap locks");
    UINT roundedStride = 0;
    UINT roundedByteCount = 0;
    BYTE* roundedPixels = nullptr;
    Check(SUCCEEDED(roundedLock->GetStride(&roundedStride)),
          "rounded surface stride is available");
    Check(SUCCEEDED(roundedLock->GetDataPointer(
              &roundedByteCount, &roundedPixels)),
          "rounded surface pixels are available");
    const auto greenAt = [&](const UINT x, const UINT y) {
        return roundedPixels[y * roundedStride + x * 4U + 1U];
    };
    Check(greenAt(21, 21) < 32,
          "opaque widget root cannot square off the host panel corner");
    Check(greenAt(40, 40) > 200,
          "rounded clip preserves widget content away from the corner");
    roundedLock.Reset();

    WidgetSnapshot alignedButtons;
    alignedButtons.instanceId = L"button-alignment.runtime";
    alignedButtons.root = Node(L"button-alignment-root", L"stack");
    const auto alignedButton = [](const wchar_t* id, const wchar_t* alignment) {
        auto result = Node(id, L"button");
        result.text = L"Go";
        result.glyph = L"play";
        result.baseStyle = {
            {L"width", Length(180)},
            {L"height", Length(60)},
            {L"padding", LengthList(L"10px")},
            {L"background", Color(L"#000000")},
            {L"color", Color(L"#ffffff")},
            {L"border-width", Length(0)},
            {L"text-align", Keyword(alignment)},
        };
        return result;
    };
    alignedButtons.root.children = {
        alignedButton(L"align-start", L"start"),
        alignedButton(L"align-center", L"center"),
        alignedButton(L"align-end", L"end"),
    };
    target->BeginDraw();
    target->Clear(D2D1::ColorF(D2D1::ColorF::Black));
    const auto alignedResult = renderer.Render(
        target.Get(), alignedButtons, {}, {0.0F, 0.0F, 220.0F, 200.0F});
    Check(SUCCEEDED(target->EndDraw()), "explicit button alignment frame draws");
    Check(alignedResult.succeeded, "explicit button alignment render succeeds");

    ComPtr<IWICBitmapLock> alignmentLock;
    const WICRect alignmentLockArea{0, 0, 640, 360};
    Check(SUCCEEDED(canvas->Lock(
        &alignmentLockArea, WICBitmapLockRead,
        alignmentLock.ReleaseAndGetAddressOf())),
        "explicit button alignment bitmap locks");
    UINT alignmentStride = 0;
    UINT alignmentByteCount = 0;
    BYTE* alignmentPixels = nullptr;
    Check(SUCCEEDED(alignmentLock->GetStride(&alignmentStride)),
        "explicit button alignment stride is available");
    Check(SUCCEEDED(alignmentLock->GetDataPointer(
        &alignmentByteCount, &alignmentPixels)),
        "explicit button alignment pixels are available");
    struct BrightBounds final {
        int minimumX{std::numeric_limits<int>::max()};
        int maximumX{-1};
    };
    const auto brightBounds = [&](const Rect rect) {
        BrightBounds bounds;
        const auto left = static_cast<UINT>(std::max(0.0F, std::floor(rect.x)));
        const auto top = static_cast<UINT>(std::max(0.0F, std::floor(rect.y)));
        const auto right = static_cast<UINT>(std::min(640.0F, std::ceil(rect.x + rect.width)));
        const auto bottom = static_cast<UINT>(std::min(360.0F, std::ceil(rect.y + rect.height)));
        for (auto y = top; y < bottom; ++y) {
            for (auto x = left; x < right; ++x) {
                const auto* pixel = alignmentPixels + y * alignmentStride + x * 4U;
                if (std::max({pixel[0], pixel[1], pixel[2]}) <= 64U) continue;
                bounds.minimumX = std::min(bounds.minimumX, static_cast<int>(x));
                bounds.maximumX = std::max(bounds.maximumX, static_cast<int>(x));
            }
        }
        Check(bounds.maximumX >= bounds.minimumX,
            "explicitly aligned button paints visible icon-label content");
        return bounds;
    };
    const auto startRect = alignedResult.elementRects.at(L"align-start");
    const auto centerRect = alignedResult.elementRects.at(L"align-center");
    const auto endRect = alignedResult.elementRects.at(L"align-end");
    const auto startBounds = brightBounds(startRect);
    const auto centerBounds = brightBounds(centerRect);
    const auto endBounds = brightBounds(endRect);
    const auto visualCenter = [](const BrightBounds bounds) {
        return (static_cast<float>(bounds.minimumX) +
            static_cast<float>(bounds.maximumX)) * 0.5F;
    };
    Check(startBounds.minimumX < centerBounds.minimumX,
        "explicit start keeps the icon-label group at the leading edge");
    Check(centerBounds.minimumX < endBounds.minimumX,
        "explicit end moves the complete icon-label group to the trailing edge");
    const auto paintedWidth = [](const BrightBounds bounds) {
        return bounds.maximumX - bounds.minimumX + 1;
    };
    Check(paintedWidth(startBounds) == paintedWidth(centerBounds) &&
            paintedWidth(centerBounds) == paintedWidth(endBounds),
        "authored alignment translates the icon-label group without splitting it");
    const auto leadingToCenter = centerBounds.minimumX - startBounds.minimumX;
    const auto centerToTrailing = endBounds.minimumX - centerBounds.minimumX;
    Check(leadingToCenter < centerToTrailing,
        "centered labels leave the leading icon on the label's leading side");
    Check(visualCenter(centerBounds) <
            centerRect.x + centerRect.width * 0.5F,
        "centering the primary label intentionally places the complete icon-label group left of center");
    Check(startBounds.maximumX < static_cast<int>(startRect.x + startRect.width * 0.5F),
        "explicit start paints the complete group in the leading half");
    Check(endBounds.minimumX > static_cast<int>(endRect.x + endRect.width * 0.5F),
        "explicit end paints the complete group in the trailing half");
    alignmentLock.Reset();

    slider.baseStyle = {
        {L"background", Color(L"#00ff00")},
        {L"height", Length(44)},
        {L"width", Length(240)},
    };
    snapshot.root.children = {slider};
    target->BeginDraw();
    target->Clear(D2D1::ColorF(D2D1::ColorF::Black));
    const auto minimalSlider = renderer.Render(
        target.Get(), snapshot, L"", {0.0F, 0.0F, 640.0F, 80.0F});
    Check(SUCCEEDED(target->EndDraw()), "minimal Slider draw completes");
    const auto sliderRect = minimalSlider.focusRects.at(L"volume");
    ComPtr<IWICBitmapLock> lock;
    const WICRect lockArea{0, 0, 640, 360};
    Check(SUCCEEDED(canvas->Lock(
        &lockArea, WICBitmapLockRead, lock.ReleaseAndGetAddressOf())),
        "minimal Slider bitmap locks");
    UINT stride = 0;
    UINT byteCount = 0;
    BYTE* pixels = nullptr;
    Check(SUCCEEDED(lock->GetStride(&stride)), "minimal Slider stride is available");
    Check(SUCCEEDED(lock->GetDataPointer(&byteCount, &pixels)),
        "minimal Slider pixels are available");
    const auto sampleX = static_cast<UINT>(std::clamp(
        sliderRect.x + sliderRect.width * 0.75F, 0.0F, 639.0F));
    const auto outsideY = static_cast<UINT>(std::clamp(
        sliderRect.y + 3.0F, 0.0F, 359.0F));
    const auto trackY = static_cast<UINT>(std::clamp(
        sliderRect.y + sliderRect.height * 0.5F, 0.0F, 359.0F));
    const auto outsideGreen = pixels[outsideY * stride + sampleX * 4U + 1U];
    const auto trackGreen = pixels[trackY * stride + sampleX * 4U + 1U];
    Check(outsideGreen < 32,
        "Slider background does not fill its complete 44-DIP hit target");
    Check(trackGreen > 200,
        "Slider background remains the visible thin track color");
    lock.Reset();

    snapshot.root.children = {slider};
    snapshot.root.children[0].minimum = -std::numeric_limits<double>::max();
    snapshot.root.children[0].maximum = std::numeric_limits<double>::max();
    snapshot.root.children[0].value = 0.0;
    snapshot.root.children[0].step = 1.0;
    options.sliderValueOverrides.clear();
    target->BeginDraw();
    const auto extreme = renderer.Render(
        target.Get(), snapshot, L"volume", {0.0F, 0.0F, 640.0F, 360.0F}, options);
    Check(SUCCEEDED(target->EndDraw()),
          "overflowing finite Slider range never sends NaN geometry to Direct2D");
    Check(std::any_of(
              extreme.diagnostics.begin(), extreme.diagnostics.end(),
              [](const auto& diagnostic) {
                  return diagnostic.code == L"invalid_slider_range";
              }),
          "overflowing finite Slider range produces a renderer diagnostic");
}

void OffscreenScrollArtworkDoesNotEnterRemoteCache() {
    using Microsoft::WRL::ComPtr;
    ComPtr<ID2D1Factory> d2d;
    Check(SUCCEEDED(D2D1CreateFactory(
        D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf())),
        "create D2D factory for artwork culling");
    ComPtr<IDWriteFactory> write;
    Check(SUCCEEDED(DWriteCreateFactory(
        DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
        reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))),
        "create DirectWrite factory for artwork culling");
    ComPtr<IWICImagingFactory> wic;
    Check(SUCCEEDED(CoCreateInstance(
        CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
        "create WIC factory for artwork culling");
    ComPtr<IWICBitmap> canvas;
    Check(SUCCEEDED(wic->CreateBitmap(
        200, 120, GUID_WICPixelFormat32bppPBGRA,
        WICBitmapCacheOnLoad, canvas.ReleaseAndGetAddressOf())),
        "create WIC canvas for artwork culling");
    ComPtr<ID2D1RenderTarget> target;
    Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
        canvas.Get(), D2D1::RenderTargetProperties(),
        target.ReleaseAndGetAddressOf())),
        "create render target for artwork culling");

    gba::RemoteImageLimits limits;
    limits.maximumEntries = 32;
    gba::RemoteImageCache cache(
        limits,
        {},
        [](std::wstring_view, std::stop_token, const gba::RemoteImageLimits&) {
            gba::RemoteDecodedImage image;
            image.width = 1;
            image.height = 1;
            image.stride = 4;
            image.premultipliedBgra = {0x10, 0x20, 0x30, 0xFF};
            image.mimeType = L"image/fake";
            return gba::RemoteImageFetchResult{S_OK, std::move(image), {}};
        });

    WidgetSnapshot snapshot;
    snapshot.instanceId = L"artwork-scroll.runtime";
    snapshot.root = Node(L"artwork-scroll", L"scroll");
    snapshot.root.scrollAxis = L"vertical";
    snapshot.root.baseStyle = {
        {L"gap", LengthList(L"8px")},
        {L"overflow", Keyword(L"clip")},
    };

    std::vector<std::pair<std::wstring, std::wstring>> artwork;
    std::size_t buttonCount{};
    for (int index = 0; index < 10; ++index) {
        const auto id = L"artwork-" + std::to_wstring(index);
        const auto url = L"https://example.test/artwork-" +
            std::to_wstring(index) + L".png";
        auto item = Node(id.c_str(), index % 2 == 0 ? L"image" : L"button");
        item.imageSource = url;
        item.baseStyle = {
            {L"height", Length(48)},
            {L"flex-shrink", Number(0)},
        };
        if (index % 2 != 0) {
            item.text = L"Play item " + std::to_wstring(index);
            item.actionId = L"play-item";
            ++buttonCount;
        }
        artwork.emplace_back(id, url);
        snapshot.root.children.push_back(std::move(item));
    }

    DeclarativeRenderer renderer{d2d.Get(), write.Get(), &cache};
    target->BeginDraw();
    const auto result = renderer.Render(
        target.Get(), snapshot, {}, {0.0F, 0.0F, 200.0F, 120.0F});
    Check(SUCCEEDED(target->EndDraw()),
        "offscreen artwork culling keeps Direct2D state balanced");

    std::size_t visibleArtwork{};
    for (const auto& [id, url] : artwork) {
        const auto& visible = result.elementVisibleRects.at(id);
        const bool isVisible = visible.width > 0.5F && visible.height > 0.5F;
        if (isVisible) {
            ++visibleArtwork;
            Check(cache.GetState(url) != gba::RemoteImageState::Missing,
                "visible artwork enters the remote cache");
        } else {
            Check(cache.GetState(url) == gba::RemoteImageState::Missing,
                "fully offscreen artwork never enters the remote cache");
        }
    }
    Check(visibleArtwork == 3,
        "120-DIP scroll viewport exposes exactly two complete and one partial artwork row");
    Check(cache.GetStats().entries == visibleArtwork,
        "remote cache queues only artwork intersecting its presented visible box");
    Check(result.navigationRects.size() == buttonCount,
        "offscreen leading-image buttons remain in the logical navigation graph");
    cache.Shutdown();
}

} // namespace

int main() {
    const auto initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    Check(SUCCEEDED(initialized), "initialize COM");
    ImagePlacementMath();
    ButtonContentPlacementCentersThePrimaryLabel();
    AccessibleStatePresentation();
    PressedComputedStyleLayersOnFocusedState();
    PlanningMetadataAndKinds();
    ResponsiveVisibilityExcludesInactiveSubtrees();
    ResponsiveNavigationShellFitsBoundedSurfaces();
    SliderPlanningAndAccessibilityTargets();
    ActionSurfacePlanningAndInteractionGeometry();
    ResponsiveGridFlowsThroughNativePlanning();
    FocusMotionUsesStableSnapshotIdentity();
    SubtreeTranslationKeepsPresentationGeometryAligned();
    TranslationRetargetsAndSnapsDeterministically();
    TranslatedFocusConvergesInsideScrollViewport();
    ClippedControlsAreNotFocusCandidates();
    ControllerScrollFollowsFocusAndRestoresState();
    WholeWidgetScrollRevealsAudioMixerControls();
    SegmentedTabsSurviveConstrainedNetworkSurfaces();
    CenteredWrappedStatePreservesTextFlowAndControllerTarget();
    SpotifyStateAndSetupCardsPreserveWrappedTextHeight();
    ResponsiveRowWrapFlowsThroughGbssAndNativePlanning();
    WrappedPermissionCopyContributesToScrollExtent();
    ScrollFocusReachesTrueContentBoundaries();
    NestedScrollFocusFollowReachesFixedPoint();
    IrrevealableClipsDoNotBecomeFocusTraps();
    ScrollStateCapEvictsOnlyInactiveLruEntries();
    DeferredFocusOutlineUsesEffectiveVisibilityClip();
    RealDirect2DSmoke();
    OffscreenScrollArtworkDoesNotEnterRemoteCache();
    std::cout << "DeclarativeRendererTests: " << checks << " checks passed\n";
    CoUninitialize();
    return EXIT_SUCCESS;
}
