#include "DeclarativeRenderer.h"
#include "RemoteImageCache.h"

#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <condition_variable>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <limits>
#include <mutex>
#include <string_view>
#include <tuple>
#include <utility>
#include <vector>

namespace {

using widgetrail::DeclarativeRenderer;
using widgetrail::NativeImageFit;
using widgetrail::NativeObjectPosition;
using widgetrail::WidgetNode;
using widgetrail::WidgetSnapshot;
using widgetrail::declarative::Rect;
using widgetrail::declarative::Size;

int checks = 0;

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void Near(
    const float actual,
    const float expected,
    const std::string_view message,
    const float tolerance = 0.01F) {
    ++checks;
    if (std::abs(actual - expected) > tolerance) {
        std::cerr << "FAIL: " << message << " (actual=" << actual
                  << ", expected=" << expected
                  << ", tolerance=" << tolerance << ")\n";
        std::exit(EXIT_FAILURE);
    }
}

WidgetNode Node(const wchar_t* id, const wchar_t* kind) {
    WidgetNode result;
    result.id = id;
    result.kind = kind;
    return result;
}

widgetrail::WidgetStyleValue Length(const double number, std::wstring unit = L"px") {
    return {L"length", std::to_wstring(number) + unit, number, std::move(unit)};
}

widgetrail::WidgetStyleValue LengthList(const wchar_t* value) {
    return {L"lengthList", value, std::nullopt, {}};
}

widgetrail::WidgetStyleValue Number(const double number) {
    return {L"number", std::to_wstring(number), number, {}};
}

widgetrail::WidgetStyleValue Duration(const double milliseconds) {
    return {L"duration", std::to_wstring(milliseconds) + L"ms",
            milliseconds, L"ms"};
}

widgetrail::WidgetStyleValue Keyword(const wchar_t* value) {
    return {L"keyword", value, std::nullopt, {}};
}

widgetrail::WidgetStyleValue Color(const wchar_t* value) {
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

void ButtonContentPlacementUsesSharedOpticalGeometry() {
    const Rect content{0.0F, 0.0F, 170.0F, 60.0F};
    const auto iconAndText = DeclarativeRenderer::ComputeButtonContentPlacement(
        content, 28.0F, 56.0F, true, true, false);
    Near(iconAndText.leading.x, 39.0F,
         "center alignment begins the complete icon-label group symmetrically");
    Near(iconAndText.text.x, 75.0F,
         "leading icon and optical gap precede the centered label");
    Near((iconAndText.leading.x + iconAndText.text.x + iconAndText.text.width) * 0.5F,
         85.0F, "complete icon-label group is centered in the button");
    Near(iconAndText.leading.y, 16.0F,
         "leading icon is vertically centered in the content box");

    const auto withCue = DeclarativeRenderer::ComputeButtonContentPlacement(
        content, 28.0F, 56.0F, true, true, true);
    Near(withCue.leading.x, iconAndText.leading.x,
         "balanced state lanes preserve the centered icon-label group");
    Near(withCue.text.x, iconAndText.text.x,
         "state cue does not move centered button content");
    Near(withCue.trailingStateCue.x, 148.0F,
         "state cue uses the shared trailing optical lane");
    Near(withCue.trailingStateCue.y, 19.0F,
         "state cue is vertically centered with the label and leading icon");

    const auto compactWithCue = DeclarativeRenderer::ComputeButtonContentPlacement(
        {0.0F, 0.0F, 122.0F, 44.0F}, 28.0F, 45.0F, true, true, true);
    Near(compactWithCue.text.width, 26.0F,
         "compact stateful button gives wrapping or trimming a collision-free width");
    Check(compactWithCue.text.x + compactWithCue.text.width + 8.0F <=
              compactWithCue.trailingStateCue.x,
          "compact label retains the optical gap before its state cue");

    const auto textOnly = DeclarativeRenderer::ComputeButtonContentPlacement(
        content, 0.0F, 40.0F, false, true, false);
    Near(textOnly.text.x, 65.0F, "text-only button label is centered by default");
    Near(textOnly.text.width, 40.0F, "text-only button keeps measured label width");

    const auto start = DeclarativeRenderer::ComputeButtonContentPlacement(
        content, 28.0F, 56.0F, true, true, false, widgetrail::NativeTextAlign::Start);
    Near(start.leading.x, 0.0F, "explicit start aligns the complete visual group");
    const auto selectedStart = DeclarativeRenderer::ComputeButtonContentPlacement(
        content, 28.0F, 56.0F, true, true, true, widgetrail::NativeTextAlign::Start);
    Near(selectedStart.leading.x, start.leading.x,
         "trailing state does not move start-aligned selection-row content");
    const auto end = DeclarativeRenderer::ComputeButtonContentPlacement(
        content, 28.0F, 56.0F, true, true, false, widgetrail::NativeTextAlign::End);
    Near(end.text.x + end.text.width, 170.0F,
         "explicit end aligns the complete visual group");
    const auto busyEnd = DeclarativeRenderer::ComputeButtonContentPlacement(
        content, 28.0F, 56.0F, true, true, true, widgetrail::NativeTextAlign::End);
    Near(busyEnd.text.x + busyEnd.text.width, 140.0F,
         "end-aligned busy content clears the shared trailing cue lane");
    Near(busyEnd.trailingStateCue.x - (busyEnd.text.x + busyEnd.text.width), 8.0F,
         "end-aligned content retains the optical cue gap");

    const auto belowPreferred = DeclarativeRenderer::ComputeButtonContentPlacement(
        {0.0F, 0.0F, 20.0F, 20.0F}, 18.0F, 40.0F, true, true, true);
    Near(belowPreferred.text.width, 0.0F,
         "below-preferred geometry collapses label content before overlapping the cue");
    Check(belowPreferred.trailingStateCue.x >= 10.0F &&
            belowPreferred.trailingStateCue.x + belowPreferred.trailingStateCue.width <= 20.0F,
         "below-preferred state cue remains inside the content bounds");

    const auto invalid = DeclarativeRenderer::ComputeButtonContentPlacement(
        {0.0F, 0.0F, -1.0F, 40.0F}, 28.0F, 40.0F, true, true, false);
    Near(invalid.leading.width, 0.0F, "invalid button geometry fails closed");
    Near(invalid.text.width, 0.0F, "invalid button text geometry fails closed");
}

void AccessibleStatePresentation() {
    widgetrail::NativeAccessibilityPolicy normal;
    Near(widgetrail::DeclarativeStateOpacityFactor(true, false, normal), 0.45F,
         "standard disabled content remains muted");
    Near(widgetrail::DeclarativeStateOpacityFactor(false, true, normal), 0.72F,
         "standard busy content remains muted");
    Check(!widgetrail::UseAccessibleDeclarativeStateCue(normal),
          "standard presentation uses opacity for disabled state");

    widgetrail::NativeAccessibilityPolicy reducedTransparency;
    reducedTransparency.reducedTransparency = true;
    Near(widgetrail::DeclarativeStateOpacityFactor(true, false, reducedTransparency), 1.0F,
         "reduced transparency does not fade disabled content");
    Check(widgetrail::UseAccessibleDeclarativeStateCue(reducedTransparency),
          "reduced transparency retains full-opacity state presentation");

    widgetrail::NativeAccessibilityPolicy highContrast;
    highContrast.contrastHook = [](widgetrail::NativeColor color, widgetrail::NativeColor) {
        return color;
    };
    Near(widgetrail::DeclarativeStateOpacityFactor(false, true, highContrast), 1.0F,
         "high contrast does not fade busy content");
    Near(widgetrail::DeclarativeStateOpacityFactor(true, false, highContrast), 1.0F,
         "high contrast does not fade disabled content");
    Check(widgetrail::UseAccessibleDeclarativeStateCue(highContrast),
          "high contrast retains policy-owned state presentation");
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

    const auto base = widgetrail::ResolveDeclarativeComputedStyle(button, false, false);
    Near(static_cast<float>(*base.at(L"opacity").number), 0.4F,
         "base state is unchanged");

    const auto focused = widgetrail::ResolveDeclarativeComputedStyle(button, true, false);
    Near(static_cast<float>(*focused.at(L"opacity").number), 0.7F,
         "focused state overrides base");

    const auto pressed = widgetrail::ResolveDeclarativeComputedStyle(button, true, true);
    Near(static_cast<float>(*pressed.at(L"opacity").number), 1.0F,
         "pressed state overrides focused");
    Near(static_cast<float>(*pressed.at(L"scale").number), 1.08F,
         "pressed state retains focused properties");

    const auto invalid = widgetrail::ResolveDeclarativeComputedStyle(button, false, true);
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
    widgetrail::DeclarativeRenderOptions accessibilityOptions;
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

void FocusAssociatedPresentationUsesNativeFocusAuthority() {
    WidgetSnapshot snapshot;
    snapshot.protocolVersion = 40;
    snapshot.sequence = 1;
    snapshot.instanceId = L"focus-presentation.instance";
    snapshot.activeInputScopeId = L"focus-presentation.content";
    snapshot.initialFocusId = L"focus-presentation.first";
    snapshot.root = Node(L"focus-presentation.root", L"stack");

    auto surface = Node(L"focus-presentation.surface", L"focusPresentationSurface");
    auto defaultFragment = Node(L"focus-presentation.default", L"stack");
    auto defaultText = Node(L"focus-presentation.default.text", L"text");
    defaultText.text = L"Choose an item";
    defaultFragment.children = {defaultText};
    surface.defaultFocusPresentation = {defaultFragment};

    auto content = Node(L"focus-presentation.content", L"row");
    content.inputScopeId = snapshot.activeInputScopeId;
    auto first = Node(L"focus-presentation.first", L"button");
    first.text = L"First";
    first.accessibilityLabel = first.text;
    first.actionId = L"focus-presentation.activate-first";
    auto firstFragment = Node(L"focus-presentation.first.fragment", L"stack");
    auto firstText = Node(L"focus-presentation.first.text", L"text");
    firstText.text = L"First details";
    firstFragment.children = {firstText};
    first.focusPresentation = {firstFragment};

    auto nestedSurface = Node(
        L"focus-presentation.nested.surface", L"focusPresentationSurface");
    auto nestedDefault = Node(L"focus-presentation.nested.default", L"text");
    nestedDefault.text = L"Nested default";
    nestedSurface.defaultFocusPresentation = {nestedDefault};
    auto nestedContent = Node(L"focus-presentation.nested.content", L"row");
    auto second = Node(L"focus-presentation.second", L"button");
    second.text = L"Second";
    second.accessibilityLabel = second.text;
    second.actionId = L"focus-presentation.activate-second";
    auto secondFragment = Node(L"focus-presentation.second.fragment", L"stack");
    auto secondText = Node(L"focus-presentation.second.text", L"text");
    secondText.text = L"Second details";
    secondFragment.children = {secondText};
    second.focusPresentation = {secondFragment};
    nestedContent.children = {second};
    nestedSurface.children = {nestedContent};
    content.children = {first, nestedSurface};
    surface.children = {content};
    snapshot.root.children = {surface};

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    widgetrail::DeclarativeRenderOptions options;
    options.collectAccessibility = true;
    const Rect viewport{0.0F, 0.0F, 800.0F, 480.0F};
    const auto firstResult = renderer.Render(
        nullptr, snapshot, first.id, viewport, options);
    Check(firstResult.elementRects.contains(L"focus-presentation.first.fragment") &&
          firstResult.elementRects.contains(L"focus-presentation.first.text") &&
          !firstResult.elementRects.contains(L"focus-presentation.default") &&
          firstResult.elementRects.contains(L"focus-presentation.nested.default") &&
          !firstResult.elementRects.contains(L"focus-presentation.second.fragment"),
        "exact focused descendant projects its admitted fragment while nested consumers keep an independent default boundary");
    Check(firstResult.focusRects.size() == 2U &&
          firstResult.focusRects.contains(first.id) &&
          firstResult.focusRects.contains(second.id) &&
          !firstResult.focusRects.contains(L"focus-presentation.first.fragment") &&
          !firstResult.navigationRects.contains(L"focus-presentation.first.fragment"),
        "presentation fragments add no focus or navigation authority");
    Check(std::any_of(
              firstResult.accessibilityRegions.begin(),
              firstResult.accessibilityRegions.end(),
              [](const auto& region) {
                  return region.nodeId == L"focus-presentation.first.text";
              }) &&
          std::none_of(
              firstResult.accessibilityRegions.begin(),
              firstResult.accessibilityRegions.end(),
              [](const auto& region) {
                  return region.nodeId == L"focus-presentation.second.text";
              }),
        "accessibility projects only the exact currently selected fragment");

    Check(!renderer.PlanFocusUpdate(
               snapshot, first.id, second.id, viewport).has_value(),
        "fragment-changing focus requests a complete native rerender from the admitted snapshot");
    const auto secondResult = renderer.Render(
        nullptr, snapshot, second.id, viewport, options);
    Check(secondResult.elementRects.contains(L"focus-presentation.default") &&
          !secondResult.elementRects.contains(L"focus-presentation.first.fragment") &&
          secondResult.elementRects.contains(L"focus-presentation.second.fragment") &&
          !secondResult.elementRects.contains(L"focus-presentation.nested.default"),
        "nested consumers are hard boundaries and resolve their own focused descendant fragment");
    Check(secondResult.focusRects.size() == 2U &&
          secondResult.hitRegions.size() == 2U,
        "native fragment projection leaves exact action and hit-test authority unchanged");
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
    widgetrail::DeclarativeRenderOptions accessibilityOptions;
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

    widgetrail::DeclarativeRenderOptions surfaceOptions;
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
        widgetrail::DeclarativeRenderOptions options;
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
    widgetrail::DeclarativeRenderOptions options;
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
    horizontalSnapshot.root.children[0].isSelected = false;
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

WidgetSnapshot PosterTileSnapshot(const std::wstring_view titleText) {
    WidgetSnapshot snapshot;
    snapshot.sequence = 37;
    snapshot.instanceId = L"poster.runtime.v1";
    snapshot.activeInputScopeId = L"poster.root";
    snapshot.initialFocusId = L"poster.card";
    snapshot.root = Node(L"poster.root", L"stack");
    snapshot.root.inputScopeId = L"poster.root";

    auto poster = Node(L"poster.card", L"actionSurface");
    poster.actionId = L"poster.open";
    poster.accessibilityLabel = L"Complete accessible poster description";
    poster.actionSurfaceOrientation = L"vertical";
    poster.actionSurfacePresentation = L"poster";
    poster.baseStyle = {
        {L"width", Length(180)},
        {L"aspect-ratio", Number(2.0 / 3.0)},
        {L"padding", LengthList(L"0px")},
        {L"overflow", Keyword(L"clip")},
        {L"corner-radius", Length(12)},
        {L"flex-shrink", Number(0)},
    };

    auto artwork = Node(L"poster.card.artwork", L"image");
    artwork.imageSource = L"https://cdn.example.test/poster.jpg";
    artwork.imageFit = L"cover";
    artwork.accessibilityLabel = L"Poster artwork";
    artwork.baseStyle = {
        {L"width", Length(180)},
        {L"height", Length(270)},
        {L"corner-radius", Length(12)},
    };

    auto scrim = Node(L"poster.card.scrim", L"stack");
    scrim.baseStyle = {
        {L"width", Length(180)},
        {L"height", Length(106)},
        {L"padding", LengthList(L"12px")},
        {L"background", Color(L"#101010")},
        {L"flex-shrink", Number(0)},
    };
    auto title = Node(L"poster.card.title", L"text");
    title.text = std::wstring(titleText);
    title.accessibilityLabel = std::wstring(titleText);
    title.baseStyle = {
        {L"height", Length(42)},
        {L"line-height", Number(1.2)},
        {L"max-lines", Number(2)},
        {L"text-overflow", Keyword(L"ellipsis")},
    };
    auto details = Node(L"poster.card.details", L"row");
    details.baseStyle = {{L"height", Length(18)}};
    auto state = Node(L"poster.card.state", L"text");
    state.text = L"Available";
    details.children = {std::move(state)};
    scrim.children = {std::move(title), std::move(details)};
    poster.children = {std::move(artwork), std::move(scrim)};
    snapshot.root.children = {std::move(poster)};
    return snapshot;
}

void PosterTileUsesFixedFullBleedGeometry() {
    using Microsoft::WRL::ComPtr;
    ComPtr<IDWriteFactory> write;
    Check(SUCCEEDED(DWriteCreateFactory(
              DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
              reinterpret_cast<IUnknown**>(write.GetAddressOf()))),
        "create DirectWrite factory for poster geometry");
    DeclarativeRenderer renderer{nullptr, write.Get(), nullptr};
    const auto render = [&](const std::wstring_view title) {
        auto snapshot = PosterTileSnapshot(title);
        widgetrail::DeclarativeRenderOptions options;
        options.collectAccessibility = true;
        return renderer.Render(
            nullptr, snapshot, L"poster.card",
            {0.0F, 0.0F, 360.0F, 360.0F}, options);
    };
    const auto shortTitle = render(L"Short");
    const auto twoLines = render(L"A title that occupies the reserved second line");
    const auto overlong = render(
        L"A deliberately overlong title that must clamp after two lines without changing the poster extent");

    const auto& shortRect = shortTitle.elementRects.at(L"poster.card");
    for (const auto* result : {&twoLines, &overlong}) {
        const auto& rect = result->elementRects.at(L"poster.card");
        Near(rect.width, shortRect.width,
            "poster title length cannot change outer width");
        Near(rect.height, shortRect.height,
            "poster title length cannot change fixed-aspect height");
        const auto& artwork = result->posterArtworkRects.at(L"poster.card.artwork");
        Near(artwork.x, rect.x, "poster artwork begins at the surface left edge");
        Near(artwork.y, rect.y, "poster artwork begins at the surface top edge");
        Near(artwork.width, rect.width, "poster artwork fills the surface width");
        Near(artwork.height, rect.height, "poster artwork fills the surface height");
    }
    Check(shortRect.width > 0.0F && shortRect.height > shortRect.width,
        "default poster geometry remains a positive portrait aspect");
    Near(overlong.elementRects.at(L"poster.card.title").height, 42.0F,
        "poster title retains its deterministic reserved two-line box");
    Check(overlong.focusRects.size() == 1U &&
              overlong.focusRects.contains(L"poster.card") &&
              overlong.hitRegions.size() == 1U,
        "poster descendants do not create a second input target");
    Check(overlong.accessibilityRegions.end() != std::find_if(
              overlong.accessibilityRegions.begin(),
              overlong.accessibilityRegions.end(),
              [](const auto& region) { return region.nodeId == L"poster.card"; }),
        "poster retains one full-surface accessibility semantic");
}

void BackgroundSurfacePreservesForegroundAuthority() {
    using Microsoft::WRL::ComPtr;
    ComPtr<ID2D1Factory> d2d;
    Check(SUCCEEDED(D2D1CreateFactory(
              D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf())),
        "create D2D factory for background-surface painting");
    ComPtr<IDWriteFactory> write;
    Check(SUCCEEDED(DWriteCreateFactory(
              DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
              reinterpret_cast<IUnknown**>(write.GetAddressOf()))),
        "create DirectWrite factory for background-surface geometry");
    ComPtr<IWICImagingFactory> wic;
    Check(SUCCEEDED(CoCreateInstance(
              CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
              IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
        "create WIC factory for background-surface painting");
    ComPtr<IWICBitmap> canvas;
    Check(SUCCEEDED(wic->CreateBitmap(
              420, 240, GUID_WICPixelFormat32bppPBGRA,
              WICBitmapCacheOnLoad, canvas.ReleaseAndGetAddressOf())),
        "create WIC canvas for background-surface painting");
    ComPtr<ID2D1RenderTarget> target;
    Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
              canvas.Get(), D2D1::RenderTargetProperties(),
              target.ReleaseAndGetAddressOf())),
        "create render target for background-surface painting");
    std::vector<std::wstring> requestedArtwork;
    widgetrail::RemoteImageCache cache(
        {}, {}, {},
        [&](const std::wstring_view key) {
            requestedArtwork.emplace_back(key);
            return true;
        });
    DeclarativeRenderer renderer{d2d.Get(), write.Get(), &cache};

    WidgetSnapshot snapshot;
    snapshot.sequence = 38;
    snapshot.instanceId = L"background.runtime.v1";
    snapshot.activeInputScopeId = L"background.content";
    snapshot.initialFocusId = L"background.open";
    snapshot.root = Node(L"background.root", L"stack");
    auto background = Node(L"background", L"backgroundSurface");
    background.artworkHandle = L"gallery.background";
    background.imageFit = L"cover";
    background.baseStyle = {
        {L"width", Length(320)},
        {L"corner-radius", Length(16)},
        {L"overflow", Keyword(L"clip")},
    };
    auto content = Node(L"background.content", L"stack");
    content.inputScopeId = L"background.content";
    content.baseStyle = {
        {L"width", Length(320)},
        {L"height", Length(120)},
        {L"padding", LengthList(L"12px")},
    };
    auto button = Node(L"background.open", L"button");
    button.text = L"Open";
    button.actionId = L"open";
    content.children = {std::move(button)};
    background.children = {std::move(content)};
    snapshot.root.children = {std::move(background)};

    widgetrail::DeclarativeRenderOptions options;
    options.collectAccessibility = true;
    options.artworkWidgetId = L"background-widget";
    target->BeginDraw();
    const auto result = renderer.Render(
        target.Get(), snapshot, L"background.open",
        {0.0F, 0.0F, 420.0F, 240.0F}, options);
    Check(SUCCEEDED(target->EndDraw()),
        "ordinary background-surface raster draw completes");
    const auto& root = result.elementRects.at(L"background.root");
    Near(root.width, 420.0F, "ordinary root is bounded to admitted viewport width");
    Near(root.height, 240.0F, "ordinary root is bounded to admitted viewport height");
    const auto& surface = result.elementRects.at(L"background");
    const auto& foreground = result.elementRects.at(L"background.content");
    Near(surface.width, foreground.width,
        "background image cannot expand foreground width");
    Near(surface.height, foreground.height,
        "background image cannot expand foreground height");
    Check(result.focusRects.size() == 1U &&
          result.focusRects.contains(L"background.open") &&
          !result.navigationRects.contains(L"background"),
        "BackgroundSurface contributes no focus or input target");
    Check(std::none_of(
              result.accessibilityRegions.begin(), result.accessibilityRegions.end(),
              [](const auto& region) { return region.nodeId == L"background"; }),
        "BackgroundSurface contributes no duplicate accessibility semantic");
    Check(requestedArtwork.size() == 1U && requestedArtwork.front() ==
              widgetrail::RemoteImageCache::TrustedArtworkKey(
                  L"background-widget", L"background", L"gallery.background"),
        "ordinary rendering requests the exact trusted background behind its foreground child");
    cache.Shutdown();
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
    widgetrail::DeclarativeRenderOptions options;
    options.animationTimestampMilliseconds = 0;
    const auto initial = renderer.Render(
        nullptr, snapshot, {}, {0.0F, 0.0F, 320.0F, 100.0F}, options);
    Check(!initial.animationActive,
          "first declarative observation snaps without an entrance animation");

    options.animationTimestampMilliseconds = 10;
    const auto focused = renderer.Render(
        nullptr, snapshot, L"play", {0.0F, 0.0F, 320.0F, 100.0F}, options);
    Check(focused.animationActive,
          "focused opacity and scale state starts its WRSS transition");

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
    widgetrail::DeclarativeRenderOptions options;
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
    widgetrail::DeclarativeRenderOptions options;
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
    widgetrail::DeclarativeRenderOptions options;
    options.animationTimestampMilliseconds = 0;
    const auto result = renderer.Render(
        nullptr, snapshot, L"translated.focus",
        {0.0F, 0.0F, 240.0F, 120.0F}, options);
    if (result.scrollOffsets.at(L"scroll") <= 40.0F) {
        std::cerr << "FAIL: focus follow accounts for presentation translation, not only static layout"
                  << " (scrollOffset=" << result.scrollOffsets.at(L"scroll") << ")\n";
        std::exit(EXIT_FAILURE);
    }
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
            item.severity == widgetrail::RenderDiagnosticSeverity::Error;
    }), "invalid native scroll axis fails closed with an error");
}

void CursorCollectionPreservesKeyedViewportAnchor() {
    WidgetSnapshot snapshot;
    snapshot.instanceId = L"cursor.collection@1";
    snapshot.activeInputScopeId = L"root";
    snapshot.root = Node(L"collection", L"scroll");
    snapshot.root.scrollAxis = L"vertical";
    snapshot.root.collectionAnchorKey = L"item.2";
    const auto append = [&](WidgetSnapshot& target, const int index) {
        auto item = Node((L"item.node." + std::to_wstring(index)).c_str(), L"button");
        item.text = L"Item";
        item.actionId = L"select";
        item.collectionItemKey = L"item." + std::to_wstring(index);
        item.baseStyle = {
            {L"height", Length(44)},
            {L"min-height", Length(44)},
            {L"flex-shrink", Number(0)},
        };
        target.root.children.push_back(std::move(item));
    };
    for (int index = 0; index < 6; ++index) append(snapshot, index);

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    const auto initial = renderer.Render(
        nullptr, snapshot, L"item.node.2", {0.0F, 0.0F, 240.0F, 100.0F});
    const auto initialY = initial.focusRects.at(L"item.node.2").y;

    auto prepended = snapshot;
    prepended.root.children.clear();
    append(prepended, -2);
    append(prepended, -1);
    prepended.root.children.insert(prepended.root.children.end(),
        snapshot.root.children.begin(), snapshot.root.children.end());
    const auto afterPrepend = renderer.Render(
        nullptr, prepended, L"item.node.2", {0.0F, 0.0F, 240.0F, 100.0F});
    Near(afterPrepend.focusRects.at(L"item.node.2").y, initialY,
         "prepending a cursor page preserves the keyed viewport anchor");
    Check(afterPrepend.scrollOffsets.at(L"collection") >
              initial.scrollOffsets.at(L"collection") + 80.0F,
          "anchor preservation compensates for prepended collection geometry");

    auto deleted = prepended;
    std::erase_if(deleted.root.children, [](const WidgetNode& node) {
        return node.collectionItemKey == L"item.2";
    });
    deleted.root.collectionAnchorKey = L"item.3";
    const auto fallback = renderer.Render(
        nullptr, deleted, L"item.node.3", {0.0F, 0.0F, 240.0F, 100.0F});
    Check(fallback.focusRects.contains(L"item.node.3"),
          "a deleted anchor admits the authored nearest keyed fallback");
}

void VirtualCollectionWindowKeepsNativeWorkBounded() {
    WidgetSnapshot snapshot;
    snapshot.protocolVersion = 19;
    snapshot.sequence = 1;
    snapshot.instanceId = L"virtual.collection@1";
    snapshot.activeInputScopeId = L"root";
    snapshot.root = Node(L"virtual.list", L"scroll");
    snapshot.root.scrollAxis = L"vertical";
    snapshot.root.collectionAnchorKey = L"item.4992";
    snapshot.root.scrollNearStartActionId = L"virtual.before";
    snapshot.root.scrollNearEndActionId = L"virtual.after";
    snapshot.root.scrollPaginationThreshold = 2;
    snapshot.root.virtualCollectionWindow = widgetrail::VirtualCollectionWindow{
        7,
        widgetrail::VirtualCollectionWindowChange::Replace,
        4992,
        10'000,
        true,
        true,
        56.0,
    };
    for (int index = 4992; index < 5024; ++index) {
        auto item = Node((L"virtual.item." + std::to_wstring(index)).c_str(), L"button");
        item.text = index % 3 == 0
            ? L"A variable-content row with a longer accessible title"
            : L"Item";
        item.accessibilityLabel = L"Virtual item " + std::to_wstring(index);
        item.actionId = L"select";
        item.collectionItemKey = L"item." + std::to_wstring(index);
        item.baseStyle = {
            {L"min-height", Length(index % 2 == 0 ? 44.0 : 56.0)},
            {L"flex-shrink", Number(0)},
        };
        snapshot.root.children.push_back(std::move(item));
    }

    using Microsoft::WRL::ComPtr;
    ComPtr<ID2D1Factory> d2d;
    Check(SUCCEEDED(D2D1CreateFactory(
        D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf())),
        "virtual window creates a D2D factory");
    ComPtr<IDWriteFactory> write;
    Check(SUCCEEDED(DWriteCreateFactory(
        DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
        reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))),
        "virtual window creates a DirectWrite factory");
    ComPtr<IWICImagingFactory> wic;
    Check(SUCCEEDED(CoCreateInstance(
        CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
        "virtual window creates a WIC factory");
    ComPtr<IWICBitmap> canvas;
    Check(SUCCEEDED(wic->CreateBitmap(
        420, 280, GUID_WICPixelFormat32bppPBGRA,
        WICBitmapCacheOnLoad, canvas.ReleaseAndGetAddressOf())),
        "virtual window creates a WIC canvas");
    ComPtr<ID2D1RenderTarget> target;
    Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
        canvas.Get(), D2D1::RenderTargetProperties(),
        target.ReleaseAndGetAddressOf())),
        "virtual window creates a WIC render target");
    DeclarativeRenderer renderer{d2d.Get(), write.Get(), nullptr};
    const Rect viewport{0.0F, 0.0F, 420.0F, 280.0F};
    widgetrail::DeclarativeRenderOptions accessibleOptions;
    accessibleOptions.collectAccessibility = true;
    target->BeginDraw();
    const auto initial = renderer.Render(
        target.Get(), snapshot, L"virtual.item.4992", viewport, accessibleOptions);
    Check(SUCCEEDED(target->EndDraw()), "virtual window initial draw completes");
    Check(initial.succeeded && initial.navigationRects.size() == 32,
        "only the admitted virtual window contributes native semantic nodes");
    Check(!initial.accessibilityRegions.empty() &&
          initial.accessibilityRegions.size() < 32,
        "only viewport-visible admitted items create accessibility regions");
    Check(initial.scrollViewports.at(L"virtual.list").maximumOffset > 500'000.0F,
        "known logical extent contributes bounded estimated scroll range");
    Check(initial.focusRects.contains(L"virtual.item.4992"),
        "the first admitted logical item is visible at its global offset");

    const auto replacementWindow = [&snapshot](
        const long long sequence,
        const int firstIndex,
        const int anchorIndex) {
        auto result = snapshot;
        result.sequence = sequence;
        result.root.children.clear();
        result.root.collectionAnchorKey =
            L"item." + std::to_wstring(anchorIndex);
        result.root.virtualCollectionWindow->requestGeneration =
            static_cast<std::uint64_t>(sequence + 6);
        result.root.virtualCollectionWindow->change =
            widgetrail::VirtualCollectionWindowChange::Replace;
        result.root.virtualCollectionWindow->firstItemIndex = firstIndex;
        result.root.virtualCollectionWindow->hasBefore = firstIndex > 0;
        result.root.virtualCollectionWindow->hasAfter = firstIndex + 32 < 10'000;
        for (int index = firstIndex; index < firstIndex + 32; ++index) {
            auto item = Node(
                (L"virtual.item." + std::to_wstring(index)).c_str(),
                L"button");
            item.text = L"Replacement item";
            item.accessibilityLabel = L"Virtual item " + std::to_wstring(index);
            item.actionId = L"select";
            item.collectionItemKey = L"item." + std::to_wstring(index);
            item.baseStyle = {
                {L"min-height", Length(index % 2 == 0 ? 44.0 : 56.0)},
                {L"flex-shrink", Number(0)},
            };
            result.root.children.push_back(std::move(item));
        }
        return result;
    };
    const auto renderVirtual = [&](DeclarativeRenderer& targetRenderer,
                                   const WidgetSnapshot& value) {
        target->BeginDraw();
        const auto result = targetRenderer.Render(
            target.Get(), value, {}, viewport, accessibleOptions);
        Check(SUCCEEDED(target->EndDraw()),
              "virtual replacement draw completes");
        Check(result.succeeded, "virtual replacement render succeeds");
        return result;
    };

    DeclarativeRenderer reopenRenderer{d2d.Get(), write.Get(), nullptr};
    const auto deepCheckpoint = renderVirtual(reopenRenderer, snapshot);
    Check(deepCheckpoint.scrollOffsets.at(L"virtual.list") > 200.0F * 56.0F,
          "virtual reopen fixture retains an offset beyond item 200");
    const auto firstWindow = replacementWindow(2, 0, 0);
    const auto reopened = renderVirtual(reopenRenderer, firstWindow);
    const auto reopenedFirst =
        reopened.elementVisibleRects.at(L"virtual.item.0");
    Check(reopened.scrollOffsets.at(L"virtual.list") < 1.0F &&
              reopenedFirst.width > 0.0F && reopenedFirst.height > 0.0F &&
              reopened.timing.collectionAdmissionSummary.find(
                  L"reconciliation=replace-window") != std::wstring::npos,
          "fresh replacement window retires an unreachable deep offset and exposes its rows");

    DeclarativeRenderer exactAnchorRenderer{d2d.Get(), write.Get(), nullptr};
    const auto exactBefore = renderVirtual(exactAnchorRenderer, snapshot);
    const auto exactAfter = renderVirtual(
        exactAnchorRenderer, replacementWindow(2, 4992, 4992));
    Near(exactAfter.scrollOffsets.at(L"virtual.list"),
         exactBefore.scrollOffsets.at(L"virtual.list"),
         "replacement with the same exact anchor preserves its retained position");

    DeclarativeRenderer overlapRenderer{d2d.Get(), write.Get(), nullptr};
    const auto overlapBefore = renderVirtual(overlapRenderer, snapshot);
    const auto overlapBeforeY =
        overlapBefore.elementRects.at(L"virtual.item.4992").y;
    const auto overlapAfter = renderVirtual(
        overlapRenderer, replacementWindow(2, 4990, 4990));
    Check(overlapAfter.timing.collectionAdmissionSummary.find(
              L"reconciliation=overlap") != std::wstring::npos &&
              overlapAfter.timing.collectionAdmissionSummary.find(
                  L"retained-key=item.4992") != std::wstring::npos,
          "replacement rows already covering the viewport use stable overlap reconciliation");
    Near(overlapAfter.elementRects.at(L"virtual.item.4992").y,
         overlapBeforeY,
         "overlapping keyed replacement preserves the retained row position");

    const auto plan = renderer.PlanFocusedFreeScroll(
        snapshot, L"virtual.item.4992",
        widgetrail::declarative::ScrollAxis::Vertical,
        80.0F, viewport, L"virtual.list");
    Check(plan && plan->scrollId == L"virtual.list" && plan->offset > plan->priorOffset,
        "right-stick free scroll uses the existing global offset authority");
    target->BeginDraw();
    const auto scrolled = renderer.Render(
        target.Get(), snapshot, L"virtual.item.4992", viewport,
        widgetrail::DeclarativeRenderOptions{
            .suppressFocusedDescendantFollow = true,
        });
    Check(SUCCEEDED(target->EndDraw()), "virtual window free-scroll draw completes");
    Near(scrolled.scrollOffsets.at(L"virtual.list"), plan->offset,
        "suppressed focus-follow preserves the virtual free-scroll offset");

    const Rect compactViewport{0.0F, 0.0F, 300.0F, 220.0F};
    target->BeginDraw();
    const auto compact = renderer.Render(
        target.Get(), snapshot, L"virtual.item.4992", compactViewport);
    Check(SUCCEEDED(target->EndDraw()), "compact virtual reflow draw completes");
    const Rect wideViewport{0.0F, 0.0F, 760.0F, 320.0F};
    target->BeginDraw();
    const auto wide = renderer.Render(
        target.Get(), snapshot, L"virtual.item.4992", wideViewport);
    Check(SUCCEEDED(target->EndDraw()), "wide virtual reflow draw completes");
    Check(compact.succeeded && wide.succeeded &&
          compact.navigationRects.size() == 32 &&
          wide.navigationRects.size() == 32 &&
          compact.scrollViewports.at(L"virtual.list").maximumOffset > 500'000.0F &&
          wide.scrollViewports.at(L"virtual.list").maximumOffset > 500'000.0F,
        "compact and wide reflow retain one bounded logical collection window");

    auto nested = snapshot;
    nested.instanceId = L"full-application-reference@1";
    nested.root = Node(L"full-app.root", L"stack");
    nested.root.baseStyle = {
        {L"gap", Length(12)},
        {L"padding", LengthList(L"16px")},
    };
    auto heading = Node(L"full-app.heading", L"text");
    heading.text = L"Reference Library";
    auto library = Node(L"full-app.library", L"stack");
    auto summary = Node(L"full-app.summary", L"text");
    summary.text = L"10,000 private records · 32 projected";
    auto refresh = Node(L"full-app.refresh", L"button");
    refresh.text = L"Refresh";
    refresh.actionId = L"full-app.refresh";
    auto nestedScroll = snapshot.root;
    nestedScroll.id = L"full-app.document-list";
    for (std::size_t index = 0; index < nestedScroll.children.size(); ++index) {
        nestedScroll.children[index].id =
            L"full-app.document-" + std::to_wstring(index);
    }
    library.children = {
        std::move(summary), std::move(refresh), std::move(nestedScroll)};
    nested.root.children = {std::move(heading), std::move(library)};
    nested.initialFocusId = L"full-app.document-0";

    const auto proveNested = [&](const Rect nestedViewport,
                                 const char* description) {
        target->BeginDraw();
        const auto rendered = renderer.Render(
            target.Get(), nested, nested.initialFocusId, nestedViewport,
            accessibleOptions);
        Check(SUCCEEDED(target->EndDraw()),
              "nested virtual reference draw completes");
        const auto scroll = rendered.scrollViewports.find(
            L"full-app.document-list");
        Check(rendered.succeeded && scroll != rendered.scrollViewports.end() &&
                  scroll->second.rect.height > 0.0F &&
                  scroll->second.rect.height < nestedViewport.height &&
                  scroll->second.maximumOffset > 500'000.0F,
              description);
        const auto freeScroll = renderer.PlanFocusedFreeScroll(
            nested, nested.initialFocusId,
            widgetrail::declarative::ScrollAxis::Vertical, 80.0F,
            nestedViewport, L"full-app.document-list");
        Check(freeScroll && freeScroll->offset > freeScroll->priorOffset,
              "nested virtual Scroll creates a nonzero right-stick plan");
        Check(rendered.navigationRects.contains(L"full-app.document-10") &&
                  rendered.revealableFocusIds.contains(
                      L"full-app.document-10"),
              "an admitted off-viewport virtual row remains available to D-pad navigation");
        Check(rendered.navigationRects.size() == 33 &&
                  rendered.accessibilityRegions.size() < 33,
              "nested virtual presentation retains bounded native and UIA work");
    };
    proveNested({0.0F, 0.0F, 820.0F, 620.0F},
                "wide nested virtual Scroll owns the remaining bounded viewport");
    proveNested({0.0F, 0.0F, 360.0F, 300.0F},
                "compact nested virtual Scroll owns the remaining bounded viewport");

    auto shifted = snapshot;
    shifted.sequence = 2;
    shifted.root.virtualCollectionWindow->requestGeneration = 8;
    shifted.root.virtualCollectionWindow->change =
        widgetrail::VirtualCollectionWindowChange::Append;
    shifted.root.virtualCollectionWindow->firstItemIndex = 5000;
    shifted.root.collectionAnchorKey = L"item.5000";
    shifted.root.children.erase(
        shifted.root.children.begin(), shifted.root.children.begin() + 8);
    for (int index = 5024; index < 5032; ++index) {
        auto item = Node((L"virtual.item." + std::to_wstring(index)).c_str(), L"button");
        item.text = L"Shifted item";
        item.accessibilityLabel = L"Virtual item " + std::to_wstring(index);
        item.actionId = L"select";
        item.collectionItemKey = L"item." + std::to_wstring(index);
        item.baseStyle = {{L"min-height", Length(44)}, {L"flex-shrink", Number(0)}};
        shifted.root.children.push_back(std::move(item));
    }
    target->BeginDraw();
    const auto afterShift = renderer.Render(
        target.Get(), shifted, L"virtual.item.5000", viewport);
    Check(SUCCEEDED(target->EndDraw()), "virtual window shift draw completes");
    Check(afterShift.succeeded && afterShift.navigationRects.size() == 32 &&
          afterShift.focusRects.contains(L"virtual.item.5000"),
        "forward window replacement remains bounded and preserves keyed focus");

    auto horizontal = snapshot;
    horizontal.instanceId = L"virtual.horizontal@1";
    horizontal.root.id = L"virtual.horizontal";
    horizontal.root.scrollAxis = L"horizontal";
    horizontal.root.baseStyle.insert_or_assign(
        L"flex-direction", Keyword(L"row"));
    horizontal.root.virtualCollectionWindow->estimatedItemExtent = 72.0;
    for (auto& item : horizontal.root.children) {
        item.baseStyle.insert_or_assign(L"width", Length(72));
        item.baseStyle.insert_or_assign(L"height", Length(120));
    }
    const Rect horizontalViewport{0.0F, 0.0F, 280.0F, 180.0F};
    target->BeginDraw();
    const auto horizontalInitial = renderer.Render(
        target.Get(), horizontal, L"virtual.item.4992", horizontalViewport);
    Check(SUCCEEDED(target->EndDraw()), "horizontal virtual window draw completes");
    Check(horizontalInitial.succeeded &&
          horizontalInitial.scrollViewports.at(L"virtual.horizontal").maximumOffset >
              700'000.0F,
        "horizontal virtual window reserves its bounded logical extent");
    const auto horizontalPlan = renderer.PlanFocusedFreeScroll(
        horizontal, L"virtual.item.4992",
        widgetrail::declarative::ScrollAxis::Horizontal,
        96.0F, horizontalViewport, L"virtual.horizontal");
    Check(horizontalPlan && horizontalPlan->offset > horizontalPlan->priorOffset,
        "right-stick free scroll uses the horizontal virtual owner");
    target->BeginDraw();
    const auto horizontalScrolled = renderer.Render(
        target.Get(), horizontal, L"virtual.item.4992", horizontalViewport,
        widgetrail::DeclarativeRenderOptions{
            .suppressFocusedDescendantFollow = true,
        });
    Check(SUCCEEDED(target->EndDraw()),
        "horizontal virtual free-scroll draw completes");
    Near(horizontalScrolled.scrollOffsets.at(L"virtual.horizontal"),
        horizontalPlan->offset,
        "horizontal virtual offset remains authoritative during free scroll");

    WidgetSnapshot eager = snapshot;
    eager.protocolVersion = 18;
    eager.instanceId = L"eager.collection@1";
    eager.root.virtualCollectionWindow.reset();
    target->BeginDraw();
    const auto eagerResult = renderer.Render(
        target.Get(), eager, L"virtual.item.4992", viewport);
    Check(SUCCEEDED(target->EndDraw()), "eager Scroll draw completes");
    Check(eagerResult.succeeded &&
          eagerResult.scrollViewports.at(L"virtual.list").maximumOffset < 5'000.0F,
        "legacy eager Scroll keeps its existing measured-window behavior");
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
    master.focusDown = input.id;
    input.focusUp = master.id;
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
        for (std::size_t index = 0; index < focusOrder.size(); ++index) {
            const auto& focused = focusOrder[index];
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
            if (index + 1 < focusOrder.size()) {
                Check(result.revealableFocusIds.contains(focusOrder[index + 1]),
                      "next explicit audio target remains host-revealable before focus moves");
            }
            priorOffset = offset;
        }
        Check(priorOffset > 0.0F,
              "final audio session requires whole-widget scrolling");

        for (auto item = focusOrder.rbegin(); item != focusOrder.rend(); ++item) {
            const auto returned = renderer.Render(
                nullptr, snapshot, *item, {0.0F, 0.0F, 520.0F, height});
            Check(returned.focusRects.contains(*item),
                  "reverse audio focus reveals every preceding controller target");
            const auto rect = returned.focusRects.at(*item);
            Check(rect.y >= -0.01F && rect.y + rect.height <= height + 0.01F,
                  "reverse audio target remains wholly inside the host viewport");
            const auto offset = returned.scrollOffsets.at(L"audio.root");
            Check(offset <= priorOffset + 0.01F,
                  "reverse audio focus never increases the root offset");
            priorOffset = offset;
        }
        Near(priorOffset, 0.0F,
             "reverse audio traversal restores the true audio leading edge");
    }

    // The live failure arrived after a larger retained session surface had
    // reconciled to four sessions. Begin with a trailing retained offset,
    // replace the content extent, and render directly at Microphone: Master
    // must remain an admissible authored Up target before any cycle/reopen.
    DeclarativeRenderer retained{nullptr, nullptr, nullptr};
    const auto trailing = retained.Render(
        nullptr, snapshot, focusOrder.back(), {0.0F, 0.0F, 520.0F, 464.0F});
    Check(trailing.scrollOffsets.at(L"audio.root") > 0.0F,
          "large audio surface seeds a retained trailing offset");
    auto liveFour = snapshot;
    liveFour.root.children[5].children.resize(4);
    const auto microphone = retained.Render(
        nullptr, liveFour, focusOrder[1], {0.0F, 0.0F, 520.0F, 464.0F});
    Check(microphone.revealableFocusIds.contains(focusOrder[0]),
          "four-session Microphone retains offscreen Master revealability");
    Check(microphone.focusRects.contains(focusOrder[1]) &&
              microphone.scrollOffsets.at(L"audio.root") > 0.0F,
          "four-session Microphone begins at a retained nonzero offset");
    Check(std::none_of(
              microphone.diagnostics.begin(), microphone.diagnostics.end(),
              [](const auto& item) { return item.code == L"value_clamped"; }),
          "four-session reconciliation publishes only canonical retained state");
    const auto leading = retained.Render(
        nullptr, liveFour, focusOrder[0], {0.0F, 0.0F, 520.0F, 464.0F});
    Near(leading.scrollOffsets.at(L"audio.root"), 0.0F,
         "one authored Up target restores the true four-session leading boundary");
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
        widgetrail::DeclarativeRenderOptions options;
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

void CenteredChildrenDoNotDisableParentStretch() {
    WidgetSnapshot snapshot;
    snapshot.instanceId = L"generic-stretch.runtime";
    snapshot.activeInputScopeId = L"generic-stretch";
    snapshot.root = Node(L"stretch.root", L"stack");
    snapshot.root.inputScopeId = snapshot.activeInputScopeId;
    snapshot.root.baseStyle = {
        {L"min-width", Length(0)},
        {L"min-height", Length(0)},
    };

    auto column = Node(L"stretch.column", L"stack");
    column.baseStyle = {
        {L"min-width", Length(0)},
        {L"min-height", Length(0)},
    };
    auto row = Node(L"stretch.row", L"row");
    row.baseStyle = {
        {L"align", Keyword(L"center")},
        {L"gap", LengthList(L"12px")},
        {L"min-width", Length(0)},
        {L"min-height", Length(52)},
    };
    auto label = Node(L"stretch.label", L"text");
    label.text = L"Volume";
    label.baseStyle = {
        {L"width", Length(80)},
        {L"flex-shrink", Number(0)},
    };
    auto slider = Node(L"stretch.slider", L"slider");
    slider.hasProgress = true;
    slider.hasSliderRange = true;
    slider.minimum = 0.0;
    slider.maximum = 100.0;
    slider.value = 50.0;
    slider.step = 5.0;
    slider.valueChangedActionId = L"volume.changed";
    slider.accessibilityLabel = L"Volume";
    slider.baseStyle = {
        {L"min-width", Length(0)},
        {L"min-height", Length(44)},
        {L"flex-grow", Number(1)},
    };
    row.children = {std::move(label), std::move(slider)};
    column.children = {std::move(row)};
    snapshot.root.children = {std::move(column)};

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    const auto result = renderer.Render(
        nullptr, snapshot, L"stretch.slider", {0.0F, 0.0F, 600.0F, 180.0F});
    const auto root = result.elementRects.at(L"stretch.root");
    const auto nestedColumn = result.elementRects.at(L"stretch.column");
    const auto centeredRow = result.elementRects.at(L"stretch.row");
    const auto sliderRect = result.focusRects.at(L"stretch.slider");
    Near(nestedColumn.width, root.width,
         "auto-width nested column stretches in its parent");
    Near(centeredRow.width, nestedColumn.width,
         "row that centers children still stretches in its parent");
    Near(sliderRect.x + sliderRect.width,
         centeredRow.x + centeredRow.width,
         "flex-grow slider consumes the row's remaining width");
    Check(sliderRect.width >= 480.0F,
          "generic centered row gives its slider substantial remaining width");

    snapshot.root.children[0].children[0].baseStyle[L"width"] = Length(320);
    const auto constrained = renderer.Render(
        nullptr, snapshot, L"stretch.slider", {0.0F, 0.0F, 600.0F, 180.0F});
    const auto constrainedRow = constrained.elementRects.at(L"stretch.row");
    const auto constrainedSlider = constrained.focusRects.at(L"stretch.slider");
    Near(constrainedRow.width, 320.0F,
         "definite width still bounds a centered-children row");
    Near(constrainedSlider.x + constrainedSlider.width,
         constrainedRow.x + constrainedRow.width,
         "bounded row still assigns exact remaining width to the slider");
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
        widgetrail::DeclarativeRenderOptions options;
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
    const widgetrail::WidgetComputedStyle rootStyle{
        {L"gap", LengthList(L"10px")},
        {L"padding", LengthList(L"18px 20px")},
        {L"overflow", Keyword(L"clip")},
    };
    const widgetrail::WidgetComputedStyle cardStyle{
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
        L"CLI: wrail config set widgetrail.samples.spotify client-id YOUR_CLIENT_ID --publisher widgetrail.samples";
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
        widgetrail::DeclarativeRenderOptions options;
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
        const auto setupStepHeight =
            setupResult.elementRects.at(L"spotify.setup-step-2").height;
        if (setupStepHeight <= 20.0F * scenario.textScale) {
            std::cerr << "FAIL: Spotify setup step retains its wrapped intrinsic height"
                      << " (width=" << scenario.width
                      << ", textScale=" << scenario.textScale
                      << ", height=" << setupStepHeight
                      << ", stepWidth="
                      << setupResult.elementRects.at(L"spotify.setup-step-2").width
                      << ", cardWidth="
                      << setupResult.elementRects.at(L"spotify.setup-card").width
                      << ", rootWidth="
                      << setupResult.elementRects.at(L"spotify.setup-root").width
                      << ")\n";
            std::exit(EXIT_FAILURE);
        }
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
          "WRSS wrapped row reaches native layout planning");
    Near(narrow.elementRects.at(L"wrap.action.1").x, 124.0F,
         "WRSS column gap reaches native row geometry");
    Near(narrow.elementRects.at(L"wrap.action.2").x, 0.0F,
         "third action starts the second responsive line");
    Near(narrow.elementRects.at(L"wrap.action.2").y, 52.0F,
         "WRSS row gap reaches native wrapped-line geometry");
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

void OversizedFocusFollowUsesOneAxisSymmetricRevealOwner() {
    const auto axisSnapshot = [](const bool horizontal,
                                 const double prefixExtent,
                                 const double targetExtent,
                                 const double suffixExtent,
                                 const std::wstring_view instance) {
        WidgetSnapshot snapshot;
        snapshot.instanceId = std::wstring(instance);
        snapshot.activeInputScopeId = L"oversized.scroll";
        snapshot.root = Node(L"oversized.scroll", L"scroll");
        snapshot.root.inputScopeId = snapshot.activeInputScopeId;
        snapshot.root.scrollAxis = horizontal ? L"horizontal" : L"vertical";
        if (horizontal)
            snapshot.root.baseStyle.insert_or_assign(
                L"flex-direction", Keyword(L"row"));
        const auto sizedNode = [horizontal](
            const wchar_t* id, const wchar_t* kind, const double extent) {
            auto node = Node(id, kind);
            if (kind == std::wstring_view{L"button"}) {
                node.text = L"Oversized action";
                node.actionId = id;
            }
            if (horizontal) {
                node.baseStyle = {
                    {L"width", Length(extent)},
                    {L"min-width", Length(extent)},
                    {L"height", Length(80)},
                    {L"flex-shrink", Number(0)}};
            } else {
                node.baseStyle = {
                    {L"height", Length(extent)},
                    {L"min-height", Length(extent)},
                    {L"flex-shrink", Number(0)}};
            }
            return node;
        };
        snapshot.root.children = {
            sizedNode(L"oversized.prefix", L"spacer", prefixExtent),
            sizedNode(L"oversized.target", L"button", targetExtent),
            sizedNode(L"oversized.suffix", L"spacer", suffixExtent),
        };
        return snapshot;
    };
    const auto verify = [](const widgetrail::RenderResult& result,
                           const bool horizontal) {
        Check(result.focusRects.contains(L"oversized.target") &&
              result.navigationRects.contains(L"oversized.target"),
            "oversized focus retains visible and logical controller geometry");
        const auto& visible = result.focusRects.at(L"oversized.target");
        const auto& logical = result.navigationRects.at(L"oversized.target");
        Check(horizontal
                ? visible.width < logical.width && visible.width >= 179.9F
                : visible.height < logical.height && visible.height >= 119.9F,
            "oversized focus exposes one complete viewport-sized useful region");
        Check(result.focusFollowPassCount <= 2U && result.focusFollowConverged &&
              !result.focusFollowNoProgress && !result.focusFollowCycle &&
              !result.focusFollowBoundHit,
            "single-scroll oversized focus converges without cycle or pass bound");
        const auto accessible = std::find_if(
            result.accessibilityRegions.begin(), result.accessibilityRegions.end(),
            [](const auto& region) { return region.nodeId == L"oversized.target"; });
        Check(accessible != result.accessibilityRegions.end(),
            "oversized focus retains one visible accessibility region");
    };

    widgetrail::DeclarativeRenderOptions options;
    options.collectAccessibility = true;
    options.pixelScale = 1.5F;
    options.animationTimestampMilliseconds = 100;

    DeclarativeRenderer verticalRenderer{nullptr, nullptr, nullptr};
    const auto verticalSnapshot = axisSnapshot(
        false, 100.0, 300.0, 100.0, L"oversized.vertical");
    const auto vertical = verticalRenderer.Render(
        nullptr, verticalSnapshot, L"oversized.target",
        {0.0F, 0.0F, 240.0F, 120.0F}, options);
    verify(vertical, false);
    Near(vertical.scrollOffsets.at(L"oversized.scroll"), 100.0F,
        "vertical oversized entry deterministically aligns its leading edge");
    const auto verticalRetained = verticalRenderer.Render(
        nullptr, verticalSnapshot, L"oversized.target",
        {0.0F, 0.0F, 240.0F, 120.0F}, options);
    Near(verticalRetained.scrollOffsets.at(L"oversized.scroll"), 100.0F,
        "unchanged oversized focus retains its converged offset");
    Check(verticalRetained.focusFollowPassCount <= 1U &&
          !verticalRetained.focusFollowCycle &&
          !verticalRetained.focusFollowBoundHit,
        "retained oversized focus does not relayout or rediscover a cycle");

    DeclarativeRenderer horizontalRenderer{nullptr, nullptr, nullptr};
    const auto horizontalSnapshot = axisSnapshot(
        true, 100.0, 300.0, 100.0, L"oversized.horizontal");
    const auto horizontal = horizontalRenderer.Render(
        nullptr, horizontalSnapshot, L"oversized.target",
        {0.0F, 0.0F, 180.0F, 120.0F}, options);
    verify(horizontal, true);
    Near(horizontal.scrollOffsets.at(L"oversized.scroll"), 100.0F,
        "horizontal oversized entry uses the same deterministic edge policy");

    DeclarativeRenderer fitRenderer{nullptr, nullptr, nullptr};
    auto fitSnapshot = axisSnapshot(
        false, 200.0, 44.0, 100.0, L"focus-fit.vertical");
    fitSnapshot.root.children[2] = FixedButton(L"fit.after", 100.0);
    const auto fit = fitRenderer.Render(
        nullptr, fitSnapshot, L"oversized.target",
        {0.0F, 0.0F, 240.0F, 180.0F}, options);
    Near(fit.scrollOffsets.at(L"oversized.scroll"), 64.0F,
        "fit-sized focus retains exact full-containment reveal math");
    Near(fit.focusRects.at(L"oversized.target").height, 44.0F,
        "fit-sized focus remains wholly visible");

    DeclarativeRenderer boundaryRenderer{nullptr, nullptr, nullptr};
    const auto leadingSnapshot = axisSnapshot(
        false, 0.0, 300.0, 100.0, L"oversized.leading");
    const auto leading = boundaryRenderer.Render(
        nullptr, leadingSnapshot, L"oversized.target",
        {0.0F, 0.0F, 240.0F, 120.0F}, options);
    Near(leading.scrollOffsets.at(L"oversized.scroll"), 0.0F,
        "oversized leading boundary retains the true content start");
    const auto trailingSnapshot = axisSnapshot(
        false, 100.0, 300.0, 0.0, L"oversized.trailing");
    const auto trailing = boundaryRenderer.Render(
        nullptr, trailingSnapshot, L"oversized.target",
        {0.0F, 0.0F, 240.0F, 120.0F}, options);
    Near(trailing.scrollOffsets.at(L"oversized.scroll"), 280.0F,
        "oversized trailing boundary retains the true content end");

    WidgetSnapshot nested;
    nested.instanceId = L"oversized.nested";
    nested.activeInputScopeId = L"nested.outer";
    nested.root = Node(L"nested.outer", L"scroll");
    nested.root.inputScopeId = nested.activeInputScopeId;
    nested.root.scrollAxis = L"vertical";
    auto inner = Node(L"nested.inner", L"scroll");
    inner.scrollAxis = L"vertical";
    inner.baseStyle = {
        {L"height", Length(120)}, {L"min-height", Length(120)},
        {L"flex-shrink", Number(0)}};
    inner.children = {
        FixedSpacer(L"nested.prefix", 80),
        FixedButton(L"nested.oversized", 250),
        FixedSpacer(L"nested.suffix", 80),
    };
    nested.root.children = {
        FixedSpacer(L"nested.outer-prefix", 100),
        std::move(inner),
        FixedSpacer(L"nested.outer-suffix", 300),
    };
    DeclarativeRenderer nestedRenderer{nullptr, nullptr, nullptr};
    const auto nestedResult = nestedRenderer.Render(
        nullptr, nested, L"nested.oversized",
        {0.0F, 0.0F, 240.0F, 120.0F}, options);
    Check(nestedResult.focusRects.contains(L"nested.oversized") &&
          nestedResult.scrollOffsets.at(L"nested.inner") > 0.0F &&
          nestedResult.scrollOffsets.at(L"nested.outer") > 0.0F,
        "nested oversized focus retains both ancestor scroll owners");
    Check(nestedResult.focusFollowPassCount <= 3U &&
          !nestedResult.focusFollowCycle && !nestedResult.focusFollowBoundHit,
        "nested oversized convergence is bounded by ancestor depth");

    auto translated = verticalSnapshot;
    translated.instanceId = L"oversized.presentation";
    auto& translatedTarget = translated.root.children[1];
    translatedTarget.baseStyle.insert_or_assign(L"translate-y", Length(40));
    DeclarativeRenderer translatedRenderer{nullptr, nullptr, nullptr};
    const auto translatedResult = translatedRenderer.Render(
        nullptr, translated, L"oversized.target",
        {0.0F, 0.0F, 240.0F, 120.0F}, options);
    Check(translatedResult.focusRects.contains(L"oversized.target") &&
          translatedResult.focusFollowPassCount <= 3U &&
          !translatedResult.focusFollowCycle &&
          !translatedResult.focusFollowBoundHit,
        "presentation-phase oversized translation converges without fallback cycling");

    WidgetSnapshot mixed;
    mixed.instanceId = L"oversized.mixed-grid";
    mixed.activeInputScopeId = L"mixed.scroll";
    mixed.root = Node(L"mixed.scroll", L"scroll");
    mixed.root.inputScopeId = mixed.activeInputScopeId;
    mixed.root.scrollAxis = L"vertical";
    auto grid = Node(L"mixed.grid", L"grid");
    grid.gridMinimumColumnWidth = 140.0;
    grid.gridMaximumColumns = 2;
    grid.baseStyle = {{L"gap", LengthList(L"12px")}};
    auto tile = Node(L"mixed.tile", L"actionSurface");
    tile.actionId = L"open-tile";
    tile.baseStyle = {{L"height", Length(120)}};
    auto poster = Node(L"mixed.poster", L"actionSurface");
    poster.actionId = L"open-poster";
    poster.actionSurfacePresentation = L"poster";
    poster.baseStyle = {{L"height", Length(300)}};
    grid.children = {std::move(tile), std::move(poster)};
    mixed.root.children = {
        FixedSpacer(L"mixed.heading", 60),
        std::move(grid),
        FixedSpacer(L"mixed.tail", 60),
    };
    DeclarativeRenderer mixedRenderer{nullptr, nullptr, nullptr};
    const auto mixedResult = mixedRenderer.Render(
        nullptr, mixed, L"mixed.poster",
        {0.0F, 0.0F, 360.0F, 180.0F}, options);
    Check(mixedResult.focusRects.contains(L"mixed.poster") &&
          mixedResult.navigationRects.at(L"mixed.poster").height > 180.0F &&
          mixedResult.focusFollowPassCount <= 2U &&
          !mixedResult.focusFollowCycle && !mixedResult.focusFollowBoundHit,
        "mixed Tile and PosterTile grid shape converges through generic geometry");
}

void IrrevealableClipsDoNotBecomeFocusTraps() {
    WidgetSnapshot rasterEdge;
    rasterEdge.instanceId = L"raster-edge.runtime";
    rasterEdge.activeInputScopeId = L"root";
    rasterEdge.root = Node(L"raster-scroll", L"scroll");
    rasterEdge.root.scrollAxis = L"vertical";
    auto rasterClip = Node(L"raster-clip", L"stack");
    rasterClip.baseStyle = {
        {L"height", Length(44)},
        {L"min-height", Length(44)},
        {L"flex-shrink", Number(0)},
        {L"overflow", {L"keyword", L"clip", std::nullopt, {}}},
    };
    rasterClip.children = {FixedButton(L"raster-target", 44.75)};
    rasterEdge.root.children = {
        FixedSpacer(L"raster-prefix", 100),
        std::move(rasterClip),
        FixedSpacer(L"raster-tail", 100),
    };

    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    const auto rasterResult = renderer.Render(
        nullptr, rasterEdge, {}, {0.0F, 0.0F, 160.0F, 100.0F});
    Check(!rasterResult.focusRects.contains(L"raster-target"),
          "raster-edge target begins outside the Scroll viewport");
    Check(rasterResult.revealableFocusIds.contains(L"raster-target"),
          "single-pixel fixed-clip overlap does not break Scroll reachability");
    const auto rasterFocused = renderer.Render(
        nullptr, rasterEdge, L"raster-target", {0.0F, 0.0F, 160.0F, 100.0F});
    Check(rasterFocused.focusRects.contains(L"raster-target"),
          "raster-edge target becomes visible after focus-follow scrolling");
    Check(rasterFocused.focusRects.at(L"raster-target").height >= 43.99F,
          "fixed clip removes only the tolerated raster edge");

    for (const auto pixelScale : {1.0F, 1.5F}) {
        const auto revealableWithNativeOverlap = [pixelScale](
            const float nativeOverlap,
            const std::wstring_view suffix) {
            WidgetSnapshot boundary;
            boundary.instanceId = L"raster-boundary-" + std::wstring(suffix);
            boundary.activeInputScopeId = L"root";
            boundary.root = Node(L"raster-boundary-scroll", L"scroll");
            boundary.root.scrollAxis = L"vertical";
            auto fixedClip = Node(L"raster-boundary-clip", L"stack");
            fixedClip.baseStyle = {
                {L"height", Length(44)},
                {L"min-height", Length(44)},
                {L"flex-shrink", Number(0)},
                {L"overflow", {L"keyword", L"clip", std::nullopt, {}}},
            };
            auto target = FixedButton(L"raster-boundary-target");
            target.baseStyle.insert_or_assign(
                L"translate-y", Length(nativeOverlap / pixelScale));
            fixedClip.children = {std::move(target)};
            boundary.root.children = {
                FixedSpacer(L"raster-boundary-prefix", 100),
                std::move(fixedClip),
                FixedSpacer(L"raster-boundary-tail", 100),
            };
            widgetrail::DeclarativeRenderOptions options;
            options.pixelScale = pixelScale;
            options.accessibility.reducedMotion = true;
            DeclarativeRenderer boundaryRenderer{nullptr, nullptr, nullptr};
            const auto result = boundaryRenderer.Render(
                nullptr, boundary, {}, {0.0F, 0.0F, 160.0F, 100.0F}, options);
            return result.revealableFocusIds.contains(L"raster-boundary-target");
        };
        Check(revealableWithNativeOverlap(0.99F, L"inside"),
              "fixed clip accepts a just-inside native-pixel raster overlap");
        Check(!revealableWithNativeOverlap(1.01F, L"outside"),
              "fixed clip rejects a just-outside native-pixel raster overlap");
    }

    WidgetSnapshot crossAxis;
    crossAxis.instanceId = L"cross-axis.runtime";
    crossAxis.activeInputScopeId = L"root";
    crossAxis.root = Node(L"vertical-only", L"scroll");
    crossAxis.root.scrollAxis = L"vertical";
    auto displaced = FixedButton(L"cross-axis");
    displaced.baseStyle.insert_or_assign(L"width", Length(44));
    displaced.baseStyle.insert_or_assign(L"min-width", Length(44));
    displaced.baseStyle.insert_or_assign(
        L"margin", widgetrail::WidgetStyleValue{
            L"lengthList", L"0px 0px 0px 140px", std::nullopt, {}});
    crossAxis.root.children.push_back(std::move(displaced));

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

void IncrementalPresentationPlanningRetainsBoundedWork() {
    using Microsoft::WRL::ComPtr;
    ComPtr<ID2D1Factory> d2d;
    Check(SUCCEEDED(D2D1CreateFactory(
        D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf())),
        "incremental planning creates a D2D factory");
    ComPtr<IDWriteFactory> write;
    Check(SUCCEEDED(DWriteCreateFactory(
        DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
        reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))),
        "incremental planning creates a DirectWrite factory");
    ComPtr<IWICImagingFactory> wic;
    Check(SUCCEEDED(CoCreateInstance(
        CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
        "incremental planning creates a WIC factory");
    ComPtr<IWICBitmap> canvas;
    Check(SUCCEEDED(wic->CreateBitmap(
        640, 480, GUID_WICPixelFormat32bppPBGRA,
        WICBitmapCacheOnLoad, canvas.ReleaseAndGetAddressOf())),
        "incremental planning creates a WIC canvas");
    ComPtr<ID2D1RenderTarget> target;
    Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
        canvas.Get(), D2D1::RenderTargetProperties(),
        target.ReleaseAndGetAddressOf())),
        "incremental planning creates a WIC render target");

    const Rect viewport{0.0F, 0.0F, 640.0F, 480.0F};
    WidgetSnapshot snapshot;
    snapshot.protocolVersion = 18;
    snapshot.sequence = 1;
    snapshot.instanceId = L"incremental.runtime";
    snapshot.activeInputScopeId = L"incremental.root";
    snapshot.initialFocusId = L"focus-a";
    snapshot.root = Node(L"incremental.root", L"stack");
    snapshot.root.inputScopeId = snapshot.activeInputScopeId;
    snapshot.root.baseStyle = {
        {L"width", Length(640)},
        {L"height", Length(480)},
        {L"padding", LengthList(L"20")},
    };
    auto boundary = Node(L"safe-boundary", L"stack");
    boundary.baseStyle = {
        {L"width", Length(320)},
        {L"height", Length(250)},
        {L"overflow", Keyword(L"clip")},
        {L"gap", Length(8)},
    };
    auto first = FixedButton(L"focus-a");
    first.text = L"First action";
    first.focusDown = L"focus-b";
    first.baseStyle.insert_or_assign(L"scale", Number(1));
    first.focusedStyle = {
        {L"scale", Number(1)},
        {L"background", Color(L"#334455")},
        {L"outline-color", Color(L"#ffffff")},
        {L"outline-width", Length(2)},
    };
    auto second = FixedButton(L"focus-b");
    second.text = L"Second action";
    second.focusUp = L"focus-a";
    second.baseStyle.insert_or_assign(L"scale", Number(1));
    second.focusedStyle = first.focusedStyle;
    auto progress = Node(L"progress", L"progress");
    progress.hasProgress = true;
    progress.value = 0.25;
    progress.minimum = 0.0;
    progress.maximum = 1.0;
    progress.baseStyle = {
        {L"height", Length(12)},
        {L"min-height", Length(12)},
        {L"flex-shrink", Number(0)},
    };
    auto localTarget = Node(L"local-target", L"spacer");
    localTarget.baseStyle = {
        {L"height", Length(24)},
        {L"min-height", Length(24)},
        {L"background", Color(L"#556677")},
        {L"flex-shrink", Number(0)},
    };
    boundary.children = {first, second, progress, localTarget};
    snapshot.root.children = {boundary};

    DeclarativeRenderer renderer{d2d.Get(), write.Get(), nullptr};
    const auto renderAt = [&](const WidgetSnapshot& value,
                              const std::wstring_view focus,
                              const Rect renderViewport) {
        target->BeginDraw();
        target->Clear(D2D1::ColorF(D2D1::ColorF::Black));
        const auto result = renderer.Render(
            target.Get(), value, focus, renderViewport);
        Check(SUCCEEDED(target->EndDraw()),
              "incremental presentation draw completes");
        Check(result.succeeded, "incremental presentation render succeeds");
        return result;
    };
    const auto render = [&](const WidgetSnapshot& value,
                            const std::wstring_view focus) {
        return renderAt(value, focus, viewport);
    };

    const auto initial = render(snapshot, L"focus-a");
    const auto initialFirst = initial.elementRects.at(L"focus-a");
    const auto initialSecond = initial.elementRects.at(L"focus-b");

    auto authority = snapshot;
    authority.sequence = 2;
    widgetrail::WidgetPresentationImpact authorityImpact;
    authorityImpact.baseSequence = 1;
    authorityImpact.sequence = 2;
    authorityImpact.effects = widgetrail::WidgetPresentationEffect::Authority |
        widgetrail::WidgetPresentationEffect::Accessibility;
    const auto noRaster = renderer.PlanPresentationUpdate(
        authority, authorityImpact, viewport);
    Check(noRaster.has_value() &&
              noRaster->work == widgetrail::IncrementalPresentationWork::NoRaster &&
              noRaster->damage.width == 0.0F && noRaster->damage.height == 0.0F,
          "authority/accessibility-only admission performs no raster work");
    Check(renderer.AcceptNoRasterPresentationUpdate(
              authority, authorityImpact, viewport),
          "authority/accessibility-only admission advances the complete checkpoint");

    auto paint = authority;
    paint.sequence = 3;
    paint.root.children[0].children[2].value = 0.75;
    widgetrail::WidgetPresentationImpact paintImpact;
    paintImpact.baseSequence = 2;
    paintImpact.sequence = 3;
    paintImpact.effects = widgetrail::WidgetPresentationEffect::Paint;
    paintImpact.affectedNodeIds = {L"progress"};
    const auto paintPlan = renderer.PlanPresentationUpdate(
        paint, paintImpact, viewport);
    Check(paintPlan.has_value() &&
              paintPlan->work == widgetrail::IncrementalPresentationWork::PaintOnly,
          "stable Current-to-Current value change retains paint-only work");
    Check(paintPlan->damage.width > 0.0F && paintPlan->damage.height > 0.0F &&
              paintPlan->damage.width * paintPlan->damage.height <
                  viewport.width * viewport.height,
          "paint-only work retains bounded effective damage");
    (void)render(paint, L"focus-a");

    auto local = paint;
    local.sequence = 4;
    local.root.children[0].children[3].baseStyle.insert_or_assign(
        L"height", Length(32));
    local.root.children[0].children[3].baseStyle.insert_or_assign(
        L"min-height", Length(32));
    widgetrail::WidgetPresentationImpact localImpact;
    localImpact.baseSequence = 3;
    localImpact.sequence = 4;
    localImpact.effects = widgetrail::WidgetPresentationEffect::MeasureLayout |
        widgetrail::WidgetPresentationEffect::Paint;
    localImpact.affectedNodeIds = {L"local-target"};
    localImpact.hasNonTextMeasureLayout = true;
    const auto localPlan = renderer.PlanPresentationUpdate(
        local, localImpact, viewport);
    Check(localPlan.has_value() &&
              localPlan->work == widgetrail::IncrementalPresentationWork::LocalLayout,
          "clipped fixed ancestor admits safe local-layout work");
    Check(localPlan->damage.width > 0.0F && localPlan->damage.height > 0.0F &&
              localPlan->damage.width * localPlan->damage.height <
                  viewport.width * viewport.height,
          "safe local-layout work remains inside the committed boundary");
    const auto localResult = render(local, L"focus-a");

    const auto focusPlan = renderer.PlanFocusUpdate(
        local, L"focus-a", L"focus-b", viewport);
    Check(focusPlan.has_value() &&
              focusPlan->work == widgetrail::IncrementalPresentationWork::PaintOnly,
          "ordinary focus movement retains paint-only work");
    Check(focusPlan->damage.width > 0.0F && focusPlan->damage.height > 0.0F &&
              focusPlan->damage.width * focusPlan->damage.height <
                  viewport.width * viewport.height,
          "ordinary focus movement damages only bounded focus visuals");
    const auto focusedSecond = render(local, L"focus-b");
    const auto sameRect = [](const Rect left, const Rect right) {
        return std::abs(left.x - right.x) <= 0.01F &&
            std::abs(left.y - right.y) <= 0.01F &&
            std::abs(left.width - right.width) <= 0.01F &&
            std::abs(left.height - right.height) <= 0.01F;
    };
    Check(sameRect(localResult.elementRects.at(L"focus-a"),
                   focusedSecond.elementRects.at(L"focus-a")) &&
              sameRect(localResult.elementRects.at(L"focus-b"),
                       focusedSecond.elementRects.at(L"focus-b")) &&
              sameRect(initialFirst, focusedSecond.elementRects.at(L"focus-a")) &&
              sameRect(initialSecond, focusedSecond.elementRects.at(L"focus-b")),
          "scale-one focus styling does not move button or text geometry");
    Check(!renderer.PlanFocusUpdate(
               local, L"focus-a", L"focus-b", viewport).has_value(),
          "mismatched committed focus proof preserves full-raster fallback");

    WidgetSnapshot anchored;
    anchored.protocolVersion = 18;
    anchored.sequence = 1;
    anchored.instanceId = L"incremental.anchor.runtime";
    anchored.activeInputScopeId = L"anchor.scope";
    anchored.root = Node(L"anchor.collection", L"scroll");
    anchored.root.scrollAxis = L"vertical";
    anchored.root.inputScopeId = anchored.activeInputScopeId;
    anchored.root.collectionAnchorKey = L"item.2";
    const auto appendAnchorItem = [](WidgetSnapshot& target, const int index) {
        auto item = FixedButton(
            (L"anchor-item-" + std::to_wstring(index)).c_str());
        item.collectionItemKey = L"item." + std::to_wstring(index);
        target.root.children.push_back(std::move(item));
    };
    for (int index = 0; index < 8; ++index)
        appendAnchorItem(anchored, index);

    const Rect anchoredViewport{0.0F, 0.0F, 240.0F, 100.0F};
    const auto anchoredInitial = renderAt(
        anchored, L"anchor-item-2", anchoredViewport);
    const auto initialAnchorOffset =
        anchoredInitial.scrollOffsets.at(L"anchor.collection");
    const auto anchoredFocusPlan = renderer.PlanFocusUpdate(
        anchored,
        L"anchor-item-2",
        L"anchor-item-5",
        anchoredViewport);
    Check(anchoredFocusPlan.has_value() &&
              anchoredFocusPlan->work ==
                  widgetrail::IncrementalPresentationWork::PaintOnly,
          "offscreen anchored-collection focus retains bounded incremental work");
    const auto anchoredFocused = renderAt(
        anchored, L"anchor-item-5", anchoredViewport);
    Check(anchoredFocused.scrollOffsets.at(L"anchor.collection") >
              initialAnchorOffset + 80.0F,
          "focus-follow destination survives the anchored incremental rebuild");
    Check(anchoredFocused.focusRects.contains(L"anchor-item-5"),
          "anchored incremental focus exposes the destination target");
    const auto anchoredFocusRect =
        anchoredFocused.focusRects.at(L"anchor-item-5");
    Check(anchoredFocusRect.y >= anchoredViewport.y - 0.01F &&
              anchoredFocusRect.y + anchoredFocusRect.height <=
                  anchoredViewport.y + anchoredViewport.height + 0.01F,
          "anchored incremental focus returns wholly inside its viewport");
    const auto& focusFollowSummary =
        anchoredFocused.timing.focusFollowSummary;
    Check(focusFollowSummary.empty() ||
              (focusFollowSummary.find(L"passes=1 ") != std::wstring::npos &&
               focusFollowSummary.find(L"disposition=converged") !=
                   std::wstring::npos),
          "anchored incremental focus converges without a cycle or fallback");

    const auto priorAnchorY =
        anchoredFocused.elementRects.at(L"anchor-item-2").y;
    auto prependedAnchor = anchored;
    prependedAnchor.sequence = 2;
    prependedAnchor.root.children.clear();
    appendAnchorItem(prependedAnchor, -2);
    appendAnchorItem(prependedAnchor, -1);
    prependedAnchor.root.children.insert(
        prependedAnchor.root.children.end(),
        anchored.root.children.begin(),
        anchored.root.children.end());
    const auto reconciledAnchor = renderAt(
        prependedAnchor, {}, anchoredViewport);
    Near(reconciledAnchor.elementRects.at(L"anchor-item-2").y,
         priorAnchorY,
         "a genuine collection snapshot change still reconciles its saved anchor");
    Check(reconciledAnchor.scrollOffsets.at(L"anchor.collection") >
              anchoredFocused.scrollOffsets.at(L"anchor.collection") + 80.0F,
          "ordinary full layout retains collection-anchor compensation");

    const auto collectionWindow = [&](const std::uint64_t sequence,
                                      const int firstIndex,
                                      const std::wstring_view anchorKey,
                                      const std::wstring_view requestedFocus) {
        WidgetSnapshot value;
        value.protocolVersion = 18;
        value.sequence = sequence;
        value.instanceId = L"retained-overlap.runtime";
        value.activeInputScopeId = L"retained-overlap.scope";
        value.initialFocusId = requestedFocus;
        value.root = Node(L"retained-overlap.collection", L"scroll");
        value.root.scrollAxis = L"vertical";
        value.root.inputScopeId = value.activeInputScopeId;
        value.root.collectionAnchorKey = anchorKey;
        for (int index = firstIndex; index < firstIndex + 8; ++index) {
            auto item = FixedButton(
                (L"retained-overlap-item-" + std::to_wstring(index)).c_str());
            item.collectionItemKey = L"item." + std::to_wstring(index);
            value.root.children.push_back(std::move(item));
        }
        return value;
    };
    const auto summaryFloat = [](const std::wstring_view summary,
                                 const std::wstring_view field) {
        const auto fieldStart = summary.find(field);
        Check(fieldStart != std::wstring_view::npos,
              "collection diagnostic contains the requested numeric field");
        if (fieldStart == std::wstring_view::npos) return 0.0F;
        const auto valueStart = fieldStart + field.size();
        const auto valueEnd = summary.find_first_of(L",}", valueStart);
        return std::stof(std::wstring{
            summary.substr(valueStart, valueEnd - valueStart)});
    };
    const auto renderCollection = [&](DeclarativeRenderer& collectionRenderer,
                                      const WidgetSnapshot& value,
                                      const std::wstring_view focus,
                                      const Rect renderViewport) {
        target->BeginDraw();
        target->Clear(D2D1::ColorF(D2D1::ColorF::Black));
        const auto result = collectionRenderer.Render(
            target.Get(), value, focus, renderViewport);
        Check(SUCCEEDED(target->EndDraw()),
              "retained-overlap collection draw completes");
        Check(result.succeeded,
              "retained-overlap collection render succeeds");
        return result;
    };

    DeclarativeRenderer overlapRenderer{d2d.Get(), write.Get(), nullptr};
    const auto overlapInitialSnapshot = collectionWindow(
        1, 0, L"item.4", L"retained-overlap-item-7");
    const auto overlapInitial = renderCollection(
        overlapRenderer, overlapInitialSnapshot,
        L"retained-overlap-item-7", anchoredViewport);
    Near(overlapInitial.scrollOffsets.at(L"retained-overlap.collection"),
         252.0F,
         "trailing focus establishes the retained-window fixture offset");
    const auto priorRetainedY =
        overlapInitial.elementRects.at(L"retained-overlap-item-6").y;

    const auto shiftedSnapshot = collectionWindow(
        2, 5, L"item.9", L"retained-overlap-item-12");
    const auto shifted = renderCollection(
        overlapRenderer, shiftedSnapshot,
        L"retained-overlap-item-12", anchoredViewport);
    const auto& shiftedSummary = shifted.timing.collectionAdmissionSummary;
    Check(shiftedSummary.find(L"change=trim-start+append") !=
              std::wstring::npos &&
              shiftedSummary.find(L"reconciliation=overlap") !=
                  std::wstring::npos &&
              shiftedSummary.find(L"retained-key=item.6") !=
                  std::wstring::npos,
          "bounded retained-window churn selects a visible overlapping row");
    const auto overlapOffset = summaryFloat(
        shiftedSummary, L"offset-after=");
    Near(44.0F - overlapOffset, priorRetainedY,
         "overlap reconciliation preserves the retained row screen position before focus-follow");
    const auto shiftedFocus =
        shifted.focusRects.at(L"retained-overlap-item-12");
    Near(shiftedFocus.y + shiftedFocus.height,
         anchoredViewport.y + anchoredViewport.height,
         "focus-follow minimally reveals the newly requested trailing item");
    Check(summaryFloat(shiftedSummary, L"final-offset=") > overlapOffset,
          "focus-follow runs after retained-overlap reconciliation");

    DeclarativeRenderer changedViewportRenderer{
        d2d.Get(), write.Get(), nullptr};
    (void)renderCollection(
        changedViewportRenderer, overlapInitialSnapshot,
        L"retained-overlap-item-7", anchoredViewport);
    const Rect changedViewport{0.0F, 0.0F, 240.0F, 120.0F};
    const auto changedViewportResult = renderCollection(
        changedViewportRenderer, shiftedSnapshot,
        L"retained-overlap-item-12", changedViewport);
    Check(changedViewportResult.timing.collectionAdmissionSummary.find(
              L"reconciliation=none") != std::wstring::npos,
          "changed viewport conservatively declines retained-overlap reconciliation");

    DeclarativeRenderer noOverlapRenderer{d2d.Get(), write.Get(), nullptr};
    (void)renderCollection(
        noOverlapRenderer, overlapInitialSnapshot,
        L"retained-overlap-item-7", anchoredViewport);
    const auto replacedSnapshot = collectionWindow(
        2, 20, L"item.24", L"retained-overlap-item-27");
    const auto replaced = renderCollection(
        noOverlapRenderer, replacedSnapshot,
        L"retained-overlap-item-27", anchoredViewport);
    Check(replaced.timing.collectionAdmissionSummary.find(
              L"reconciliation=none") != std::wstring::npos,
          "missing retained overlap preserves conservative fallback");

    WidgetSnapshot freeScroll;
    freeScroll.protocolVersion = 18;
    freeScroll.sequence = 41;
    freeScroll.instanceId = L"free-scroll.runtime";
    freeScroll.activeInputScopeId = L"free-scroll.scope";
    freeScroll.root = Node(L"outer-scroll", L"scroll");
    freeScroll.root.inputScopeId = freeScroll.activeInputScopeId;
    freeScroll.root.scrollAxis = L"vertical";
    freeScroll.root.baseStyle = {{L"gap", Length(4)}};
    auto innerScroll = Node(L"inner-scroll", L"scroll");
    innerScroll.scrollAxis = L"vertical";
    innerScroll.baseStyle = {
        {L"height", Length(100)},
        {L"min-height", Length(100)},
        {L"flex-shrink", Number(0)},
        {L"gap", Length(4)},
    };
    for (int index = 0; index < 8; ++index) {
        innerScroll.children.push_back(FixedButton(
            (L"free-scroll-item-" + std::to_wstring(index)).c_str()));
    }
    auto activeIndicator = Node(L"free-scroll-motion", L"loadingIndicator");
    activeIndicator.baseStyle = {
        {L"height", Length(24)},
        {L"min-height", Length(24)},
        {L"flex-shrink", Number(0)},
    };
    freeScroll.root.children = {
        std::move(innerScroll), std::move(activeIndicator),
        FixedSpacer(L"outer-tail", 320),
    };
    const Rect freeScrollViewport{0.0F, 0.0F, 320.0F, 180.0F};
    DeclarativeRenderer freeScrollRenderer{d2d.Get(), write.Get(), nullptr};
    const auto renderFreeScroll = [&](
        const widgetrail::DeclarativeRenderOptions& options) {
        target->BeginDraw();
        target->Clear(D2D1::ColorF(D2D1::ColorF::Black));
        const auto result = freeScrollRenderer.Render(
            target.Get(), freeScroll, L"free-scroll-item-0",
            freeScrollViewport, options);
        Check(SUCCEEDED(target->EndDraw()),
              "free-scroll renderer draw completes");
        Check(result.succeeded, "free-scroll renderer succeeds");
        return result;
    };
    widgetrail::DeclarativeRenderOptions freeScrollOptions;
    freeScrollOptions.collectAccessibility = true;
    freeScrollOptions.animationTimestampMilliseconds = 100;
    const auto freeScrollInitial = renderFreeScroll(freeScrollOptions);
    Check(freeScrollInitial.animationActive,
          "renderer-local declarative motion coexists with the retained scroll cache");

    const auto deepestPlan = freeScrollRenderer.PlanFocusedFreeScroll(
        freeScroll, L"free-scroll-item-0",
        widgetrail::declarative::ScrollAxis::Vertical,
        10'000.0F, freeScrollViewport);
    Check(deepestPlan.has_value() && deepestPlan->scrollId == L"inner-scroll" &&
              deepestPlan->render.work ==
                  widgetrail::IncrementalPresentationWork::LocalLayout &&
              deepestPlan->offset == deepestPlan->maximumOffset,
          "right-stick free scroll selects the deepest eligible scroll owner");
    Check(deepestPlan && deepestPlan->render.damage.width > 0.0F &&
              deepestPlan->render.damage.height > 0.0F &&
              deepestPlan->render.damage.width * deepestPlan->render.damage.height <
                  freeScrollViewport.width * freeScrollViewport.height,
          "right-stick free scroll invalidates only the selected nested viewport");
    freeScrollOptions.suppressFocusedDescendantFollow = true;
    freeScrollOptions.animationTimestampMilliseconds = 116;
    const auto innerScrolled = renderFreeScroll(freeScrollOptions);
    Near(innerScrolled.scrollOffsets.at(L"inner-scroll"), deepestPlan->offset,
         "free-scroll render commits the deepest scroll offset");
    Check(innerScrolled.navigationRects.contains(L"free-scroll-item-0") &&
              !innerScrolled.focusRects.contains(L"free-scroll-item-0") &&
              !std::ranges::any_of(
                  innerScrolled.hitRegions,
                  [](const widgetrail::RenderHitRegion& region) {
                      return region.nodeId == L"free-scroll-item-0";
                  }) &&
              !std::ranges::any_of(
                  innerScrolled.accessibilityRegions,
                  [](const widgetrail::RenderAccessibilityRegion& region) {
                      return region.nodeId == L"free-scroll-item-0";
                  }),
          "suppressed focus-follow retains semantic navigation identity without stale visible hit/UIA geometry");

    freeScrollOptions.animationTimestampMilliseconds = 132;
    const auto retainedRefresh = renderFreeScroll(freeScrollOptions);
    Near(retainedRefresh.scrollOffsets.at(L"inner-scroll"), deepestPlan->offset,
         "an exact retained refresh cannot pull the viewport back to semantic focus");
    Check(!retainedRefresh.focusRects.contains(L"free-scroll-item-0"),
          "an exact retained refresh does not republish an offscreen focus visual");

    const auto ancestorFallback = freeScrollRenderer.PlanFocusedFreeScroll(
        freeScroll, L"free-scroll-item-0",
        widgetrail::declarative::ScrollAxis::Vertical,
        10'000.0F, freeScrollViewport);
    Check(ancestorFallback.has_value() &&
              ancestorFallback->scrollId == L"outer-scroll" &&
              ancestorFallback->offset > ancestorFallback->priorOffset,
          "a nested scroll boundary falls back through the existing ancestor chain");
    freeScrollOptions.animationTimestampMilliseconds = 148;
    const auto outerScrolled = renderFreeScroll(freeScrollOptions);
    Near(outerScrolled.scrollOffsets.at(L"outer-scroll"), ancestorFallback->offset,
         "ancestor fallback commits through the sole renderer scroll authority");

    const auto restoredCurrent = freeScrollRenderer.PlanFocusedFreeScroll(
        freeScroll, L"free-scroll-item-0",
        widgetrail::declarative::ScrollAxis::Vertical,
        -24.0F, freeScrollViewport);
    Check(restoredCurrent.has_value() &&
              restoredCurrent->offset < restoredCurrent->priorOffset,
          "a matching current checkpoint restores movement from retained scroll state");
    freeScrollOptions.animationTimestampMilliseconds = 164;
    (void)renderFreeScroll(freeScrollOptions);

    auto pendingImpactSnapshot = freeScroll;
    ++pendingImpactSnapshot.sequence;
    Check(!freeScrollRenderer.PlanFocusedFreeScroll(
              pendingImpactSnapshot, L"free-scroll-item-0",
              widgetrail::declarative::ScrollAxis::Vertical,
              24.0F, freeScrollViewport),
          "a real pending snapshot impact cannot scroll against a stale retained cache");
    Check(!freeScrollRenderer.PlanFocusedFreeScroll(
              freeScroll, L"free-scroll-item-1",
              widgetrail::declarative::ScrollAxis::Vertical,
              24.0F, freeScrollViewport),
          "focus authority mismatch clears free-scroll admission");
    auto replacedRuntime = freeScroll;
    replacedRuntime.instanceId = L"free-scroll.runtime.replaced";
    Check(!freeScrollRenderer.PlanFocusedFreeScroll(
              replacedRuntime, L"free-scroll-item-0",
              widgetrail::declarative::ScrollAxis::Vertical,
              24.0F, freeScrollViewport),
          "runtime replacement clears free-scroll admission");
    Check(!freeScrollRenderer.PlanFocusedFreeScroll(
              freeScroll, L"free-scroll-item-0",
              widgetrail::declarative::ScrollAxis::Vertical,
              24.0F, {0.0F, 0.0F, 321.0F, 180.0F}),
          "viewport replacement clears free-scroll admission");

    auto structural = local;
    structural.sequence = 5;
    widgetrail::WidgetPresentationImpact structuralImpact;
    structuralImpact.baseSequence = 4;
    structuralImpact.sequence = 5;
    structuralImpact.effects = widgetrail::WidgetPresentationEffect::Structure;
    structuralImpact.affectedNodeIds = {L"safe-boundary"};
    Check(!renderer.PlanPresentationUpdate(
               structural, structuralImpact, viewport).has_value(),
          "structural work preserves conservative full-raster fallback");
    auto surfaceImpact = structuralImpact;
    surfaceImpact.effects = widgetrail::WidgetPresentationEffect::SurfacePlacement;
    Check(!renderer.PlanPresentationUpdate(
               structural, surfaceImpact, viewport).has_value(),
          "surface-placement work preserves conservative full-raster fallback");
    auto unknownImpact = structuralImpact;
    unknownImpact.effects = widgetrail::WidgetPresentationEffect::Unknown;
    Check(!renderer.PlanPresentationUpdate(
               structural, unknownImpact, viewport).has_value(),
          "unknown work preserves conservative full-raster fallback");

    const auto themePath = std::filesystem::path(__FILE__).parent_path()
        .parent_path() / "PlatformSettings" / "Themes" / "builtin-default.wrss";
    std::ifstream themeFile(themePath, std::ios::binary);
    const std::string theme{
        std::istreambuf_iterator<char>(themeFile),
        std::istreambuf_iterator<char>()};
    Check(!theme.empty(), "default WRSS theme is available to the native regression");
    const auto buttonStart = theme.find("button {");
    const auto buttonEnd = theme.find('}', buttonStart);
    const auto focusedStart = theme.find("button:focused {");
    const auto focusedEnd = theme.find('}', focusedStart);
    Check(buttonStart != std::string::npos && buttonEnd != std::string::npos &&
              focusedStart != std::string::npos && focusedEnd != std::string::npos,
          "default theme retains button and focused-button rules");
    const auto buttonRule = theme.substr(buttonStart, buttonEnd - buttonStart);
    const auto focusedRule = theme.substr(focusedStart, focusedEnd - focusedStart);
    Check(buttonRule.find("scale: 1;") != std::string::npos &&
              buttonRule.find("scale: 0.99") == std::string::npos &&
              focusedRule.find("scale: 1;") != std::string::npos,
          "text-bearing buttons retain scale one across ordinary focus moves");
    Check(focusedRule.find("background:") != std::string::npos &&
              focusedRule.find("outline-color:") != std::string::npos &&
              focusedRule.find("outline-width:") != std::string::npos,
          "scale-one focus styling retains background and outline cues");
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
    widgetrail::DeclarativeRenderOptions options;
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

    struct DisabledActionRaster final {
        widgetrail::RenderResult result;
        std::vector<BYTE> pixels;
    };
    const auto renderDisabledAction = [&](const wchar_t* kind, const bool disabled) {
        WidgetSnapshot state;
        state.instanceId = L"disabled-action.runtime";
        state.activeInputScopeId = L"disabled-action.root";
        state.root = Node(L"disabled-action.root", L"stack");
        state.root.inputScopeId = state.activeInputScopeId;
        auto action = Node(L"disabled-action.control", kind);
        action.actionId = L"activate";
        action.accessibilityLabel = L"Provider-neutral action";
        action.isDisabled = disabled;
        action.baseStyle = {
            {L"width", Length(220)},
            {L"height", Length(64)},
            {L"background", Color(L"#203040")},
            {L"color", Color(L"#ffffff")},
            {L"border-width", Length(2)},
            {L"border-color", Color(L"#ffffff")},
        };
        if (action.kind == L"button") {
            action.text = L"Provider-neutral action";
            action.glyph = L"play";
        } else {
            action.actionSurfaceOrientation = L"horizontal";
            auto label = Node(L"disabled-action.label", L"text");
            label.text = L"Provider-neutral action";
            action.children.push_back(std::move(label));
        }
        state.root.children.push_back(std::move(action));

        widgetrail::DeclarativeRenderOptions stateOptions;
        stateOptions.accessibility.reducedMotion = true;
        stateOptions.accessibility.contrastHook = [](
                const widgetrail::NativeColor color,
                const widgetrail::NativeColor) { return color; };
        DeclarativeRenderer stateRenderer{d2d.Get(), write.Get(), nullptr};
        target->BeginDraw();
        target->Clear(D2D1::ColorF(D2D1::ColorF::Black));
        auto stateResult = stateRenderer.Render(
            target.Get(), state, L"disabled-action.control",
            {0.0F, 0.0F, 640.0F, 360.0F}, stateOptions);
        Check(SUCCEEDED(target->EndDraw()),
            "disabled action comparison frame draws");
        Check(stateResult.succeeded,
            "disabled action comparison render succeeds");

        ComPtr<IWICBitmapLock> stateLock;
        const WICRect stateLockArea{0, 0, 640, 360};
        Check(SUCCEEDED(canvas->Lock(
            &stateLockArea, WICBitmapLockRead,
            stateLock.ReleaseAndGetAddressOf())),
            "disabled action comparison bitmap locks");
        UINT stateByteCount = 0;
        BYTE* statePixels = nullptr;
        Check(SUCCEEDED(stateLock->GetDataPointer(
            &stateByteCount, &statePixels)),
            "disabled action comparison pixels are available");
        std::vector<BYTE> copiedPixels(statePixels, statePixels + stateByteCount);
        stateLock.Reset();
        return DisabledActionRaster{
            std::move(stateResult), std::move(copiedPixels)};
    };

    for (const auto* kind : {L"button", L"actionSurface"}) {
        const auto enabledAction = renderDisabledAction(kind, false);
        const auto disabledAction = renderDisabledAction(kind, true);
        Check(enabledAction.pixels == disabledAction.pixels,
            "disabled Button and ActionSurface add no diagonal raster cue");
        Check(enabledAction.result.hitRegions.size() == 1 &&
              enabledAction.result.hitRegions.front().enabled &&
              disabledAction.result.hitRegions.size() == 1 &&
              !disabledAction.result.hitRegions.front().enabled,
            "disabled raster parity does not weaken activation suppression");
        Check(disabledAction.result.navigationEnabled.at(L"disabled-action.control") &&
              disabledAction.result.focusRects.contains(L"disabled-action.control"),
            "disabled raster parity retains controller focus and navigation");
    }

    WidgetSnapshot responsiveSnapshot;
    responsiveSnapshot.sequence = 77;
    responsiveSnapshot.instanceId = L"responsive-commit.runtime";
    responsiveSnapshot.activeInputScopeId = L"responsive-commit.root";
    responsiveSnapshot.root = Node(L"responsive-commit.root", L"stack");
    responsiveSnapshot.root.inputScopeId = responsiveSnapshot.activeInputScopeId;
    auto compactBranch = Node(L"responsive-commit.compact", L"stack");
    compactBranch.visibleWhen = L"compactOnly";
    auto compactAction = Node(L"responsive-commit.compact.queue", L"button");
    compactAction.text = L"Queue";
    compactAction.accessibilityLabel = L"Compact Queue";
    compactAction.actionId = L"queue.open";
    compactAction.focusPersistenceId = L"navigation.queue";
    compactBranch.children = {compactAction};
    auto expandedBranch = Node(L"responsive-commit.expanded", L"stack");
    expandedBranch.visibleWhen = L"expandedOnly";
    auto expandedAction = Node(L"responsive-commit.expanded.queue", L"button");
    expandedAction.text = L"Queue";
    expandedAction.accessibilityLabel = L"Expanded Queue";
    expandedAction.actionId = L"queue.open";
    expandedAction.focusPersistenceId = L"navigation.queue";
    expandedBranch.children = {expandedAction};
    responsiveSnapshot.root.children = {compactBranch, expandedBranch};

    widgetrail::DeclarativeRenderOptions expandedOptions;
    expandedOptions.collectAccessibility = true;
    expandedOptions.responsiveViewport = Size{980.0F, 560.0F};
    const auto renderResponsive = [&](const std::wstring_view focus,
                                      const Rect viewport,
                                      const widgetrail::DeclarativeRenderOptions& renderOptions) {
        target->BeginDraw();
        target->Clear(D2D1::ColorF(D2D1::ColorF::Black));
        const auto rendered = renderer.Render(
            target.Get(), responsiveSnapshot, focus, viewport, renderOptions);
        Check(SUCCEEDED(target->EndDraw()),
              "committed responsive geometry draw completes");
        Check(rendered.succeeded && rendered.responsiveSurface.has_value(),
              "successful render publishes one committed responsive decision");
        return rendered;
    };
    const Rect expandedContentViewport{0.0F, 0.0F, 640.0F, 360.0F};
    const auto expandedResponsive = renderResponsive(
        L"responsive-commit.expanded.queue",
        expandedContentViewport, expandedOptions);
    Check(expandedResponsive.responsiveSurface->mode ==
              widgetrail::ResponsiveSurfaceMode::Expanded,
          "authored pre-chrome surface commits the expanded mode");
    Near(expandedResponsive.responsiveSurface->viewport.width, 980.0F,
         "committed responsive width retains authored authority");
    Near(expandedResponsive.responsiveSurface->viewport.height, 560.0F,
         "committed responsive height is not replaced by content height");
    Check(expandedResponsive.focusRects.contains(
              L"responsive-commit.expanded.queue") &&
          !expandedResponsive.focusRects.contains(
              L"responsive-commit.compact.queue") &&
          std::ranges::any_of(
              expandedResponsive.accessibilityRegions,
              [](const auto& region) {
                  return region.nodeId ==
                      L"responsive-commit.expanded.queue";
              }) &&
          std::ranges::none_of(
              expandedResponsive.accessibilityRegions,
              [](const auto& region) {
                  return region.nodeId ==
                      L"responsive-commit.compact.queue";
              }),
          "focus, hit testing, and UIA expose only the committed expanded branch");

    const auto sameSequenceRefresh = renderResponsive(
        L"responsive-commit.expanded.queue",
        expandedContentViewport, expandedOptions);
    Check(sameSequenceRefresh.responsiveSurface->mode ==
              widgetrail::ResponsiveSurfaceMode::Expanded &&
          sameSequenceRefresh.focusRects.contains(
              L"responsive-commit.expanded.queue") &&
          !sameSequenceRefresh.focusRects.contains(
              L"responsive-commit.compact.queue"),
          "same-sequence same-geometry refresh preserves responsive focus geometry");

    auto compactOptions = expandedOptions;
    compactOptions.responsiveViewport = Size{980.0F, 505.0F};
    const auto compactResponsive = renderResponsive(
        L"responsive-commit.compact.queue",
        {0.0F, 0.0F, 640.0F, 320.0F}, compactOptions);
    Check(compactResponsive.responsiveSurface->mode ==
              widgetrail::ResponsiveSurfaceMode::Compact,
          "real committed surface resize selects compact mode once");
    Near(compactResponsive.responsiveSurface->viewport.height, 505.0F,
         "resized committed surface records its exact responsive height");
    Check(compactResponsive.focusRects.contains(
              L"responsive-commit.compact.queue") &&
          !compactResponsive.focusRects.contains(
              L"responsive-commit.expanded.queue") &&
          std::ranges::any_of(
              compactResponsive.accessibilityRegions,
              [](const auto& region) {
                  return region.nodeId ==
                      L"responsive-commit.compact.queue";
              }),
          "resize makes focus, hit testing, and UIA agree on compact geometry");

    WidgetSnapshot loadingSnapshot;
    loadingSnapshot.instanceId = L"loading.runtime";
    loadingSnapshot.activeInputScopeId = L"loading-root";
    loadingSnapshot.root = Node(L"loading-root", L"stack");
    auto loading = Node(L"loading", L"loadingIndicator");
    loading.accessibilityLabel = L"Loading applications";
    loading.indicatorSize = L"standard";
    loadingSnapshot.root.children = {loading};
    widgetrail::DeclarativeRenderOptions loadingOptions;
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
    widgetrail::DeclarativeRenderOptions roundedOptions;
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
    const auto& centerPlacement =
        alignedResult.buttonContentPlacements.at(L"align-center");
    Near((centerPlacement.leading.x + centerPlacement.text.x +
            centerPlacement.text.width) * 0.5F,
        centerRect.x + centerRect.width * 0.5F,
        "center alignment places the measured icon-label group at the exact control center");
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
    const auto minimumPaintedWidth = std::min({
        paintedWidth(startBounds), paintedWidth(centerBounds), paintedWidth(endBounds)});
    const auto maximumPaintedWidth = std::max({
        paintedWidth(startBounds), paintedWidth(centerBounds), paintedWidth(endBounds)});
    Check(maximumPaintedWidth - minimumPaintedWidth <= 1,
        "authored alignment translates the icon-label group within one raster pixel");
    const auto leadingToCenter = centerBounds.minimumX - startBounds.minimumX;
    const auto centerToTrailing = endBounds.minimumX - centerBounds.minimumX;
    Check(std::abs(leadingToCenter - centerToTrailing) <= 1,
        "start, center, and end translate the complete icon-label group symmetrically");
    Check(std::abs(visualCenter(centerBounds) -
            (centerRect.x + centerRect.width * 0.5F)) <= 8.0F,
        "asymmetric icon-label pixels remain within the bounded optical-center tolerance");
    Check(startBounds.maximumX < static_cast<int>(startRect.x + startRect.width * 0.5F),
        "explicit start paints the complete group in the leading half");
    Check(endBounds.minimumX > static_cast<int>(endRect.x + endRect.width * 0.5F),
        "explicit end paints the complete group in the trailing half");
    alignmentLock.Reset();

    const auto matrixButton = [](const wchar_t* id, const wchar_t* text) {
        auto result = Node(id, L"button");
        result.text = text;
        result.glyph = L"refresh";
        result.baseStyle = {
            {L"width", Length(220)},
            {L"min-height", Length(44)},
            {L"padding", LengthList(L"10px")},
            {L"font-size", Length(15)},
            {L"line-height", Number(1.3)},
            {L"max-lines", Number(2)},
        };
        return result;
    };
    const auto geometryCenter = [](const widgetrail::ButtonContentPlacement& placement) {
        const auto left = placement.leading.width > 0.0F
            ? placement.leading.x
            : placement.text.x;
        return (left + placement.text.x + placement.text.width) * 0.5F;
    };
    for (const auto& [surfaceWidth, pixelScale, textScale] : {
             std::tuple{240.0F, 1.0F, 1.0F},
             std::tuple{360.0F, 1.25F, 1.15F},
             std::tuple{540.0F, 1.5F, 1.5F},
         }) {
        WidgetSnapshot matrix;
        matrix.instanceId = L"button-geometry-matrix.runtime";
        matrix.root = Node(L"button-geometry-matrix.root", L"stack");
        matrix.root.baseStyle = {{L"gap", LengthList(L"4px")}};
        auto plain = matrixButton(L"matrix.plain", L"Refresh library");
        auto selected = matrixButton(L"matrix.selected", L"Selected row");
        selected.isSelected = true;
        selected.baseStyle.insert_or_assign(L"justify", Keyword(L"start"));
        auto busy = matrixButton(L"matrix.busy", L"Refreshing library");
        busy.isBusy = true;
        auto disabled = matrixButton(L"matrix.disabled", L"Unavailable action");
        disabled.isDisabled = true;
        auto select = matrixButton(L"matrix.select", L"Quality");
        select.isSelect = true;
        auto wrapped = matrixButton(
            L"matrix.wrapped",
            L"Refresh the selected collection after checking every available source");
        for (auto* candidate : {&plain, &selected, &busy, &disabled, &select, &wrapped})
            candidate->baseStyle.insert_or_assign(L"width", Length(surfaceWidth - 20.0F));
        matrix.root.children = {
            std::move(plain), std::move(selected), std::move(busy),
            std::move(disabled), std::move(select), std::move(wrapped),
        };

        widgetrail::DeclarativeRenderOptions matrixOptions;
        matrixOptions.pixelScale = pixelScale;
        matrixOptions.accessibility.textScale = textScale;
        target->BeginDraw();
        target->Clear(D2D1::ColorF(D2D1::ColorF::Black));
        const auto matrixResult = renderer.Render(
            target.Get(), matrix, L"matrix.plain",
            {0.0F, 0.0F, surfaceWidth, 360.0F}, matrixOptions);
        Check(SUCCEEDED(target->EndDraw()),
            "button geometry profile completes a real Direct2D frame");
        Check(matrixResult.succeeded && matrixResult.buttonContentPlacements.size() == 6,
            "button geometry profile records every shared content placement");

        for (const auto* id : {L"matrix.plain", L"matrix.busy", L"matrix.disabled",
                 L"matrix.wrapped"}) {
            const auto& placement = matrixResult.buttonContentPlacements.at(id);
            const auto& rect = matrixResult.elementRects.at(id);
            Near(geometryCenter(placement), rect.x + rect.width * 0.5F,
                "centered button group remains centered across surface and scale profiles",
                0.51F);
        }
        const auto& selectedPlacement =
            matrixResult.buttonContentPlacements.at(L"matrix.selected");
        const auto& selectedRect = matrixResult.elementRects.at(L"matrix.selected");
        Near(selectedPlacement.leading.x, selectedRect.x + 10.0F,
            "authored justify-start aligns selection-row content at the shared leading inset",
            0.51F);
        Check(selectedPlacement.trailingStateCue.width == 0.0F &&
              selectedPlacement.trailingStateCue.height == 0.0F,
              "selected text button reserves no automatic state-cue lane");
        const auto& busyPlacement =
            matrixResult.buttonContentPlacements.at(L"matrix.busy");
        const auto& busyRect = matrixResult.elementRects.at(L"matrix.busy");
        Near(busyPlacement.trailingStateCue.y +
                busyPlacement.trailingStateCue.height * 0.5F,
            busyRect.y + busyRect.height * 0.5F,
            "busy cue remains vertically centered",
            0.51F);
        Check(busyPlacement.text.x + busyPlacement.text.width + 7.5F <=
                busyPlacement.trailingStateCue.x,
            "busy label remains clear of the shared cue lane");
        const auto& disabledPlacement =
            matrixResult.buttonContentPlacements.at(L"matrix.disabled");
        Check(disabledPlacement.trailingStateCue.width == 0.0F &&
              disabledPlacement.trailingStateCue.height == 0.0F,
              "disabled button reserves no diagonal state-cue lane");
        const auto& selectPlacement =
            matrixResult.buttonContentPlacements.at(L"matrix.select");
        Check(selectPlacement.trailingStateCue.width > 0.0F &&
              selectPlacement.trailingStateCue.height > 0.0F &&
              selectPlacement.text.x + selectPlacement.text.width + 7.5F <=
                  selectPlacement.trailingStateCue.x,
              "Select reserves a bounded host-owned trailing chevron lane");
        if (textScale == 1.5F) {
            Check(matrixResult.elementRects.at(L"matrix.wrapped").height > 44.0F,
                "150-percent wrapped label contributes its measured intrinsic height");
        } else if (textScale == 1.0F) {
            Near(matrixResult.elementRects.at(L"matrix.plain").height, 44.0F,
                "standard single-line button applies the outer interaction minimum once");
        }
    }

    WidgetSnapshot switchSnapshot;
    switchSnapshot.instanceId = L"switch-cue.runtime";
    switchSnapshot.root = Node(L"switch-cue.root", L"stack");
    switchSnapshot.root.baseStyle = {{L"gap", LengthList(L"4px")}};
    auto switchOn = matrixButton(L"switch-cue.on", L"Motion  On");
    switchOn.glyph.clear();
    switchOn.isSelected = true;
    switchOn.styleClasses = {
        L"wrail-switch", L"wrail-switch--on", L"package-switch-accent"};
    switchOn.baseStyle.insert_or_assign(L"foreground", Color(L"#00ff00"));
    auto switchOff = matrixButton(L"switch-cue.off", L"Motion  Off");
    switchOff.glyph.clear();
    switchOff.styleClasses = {L"wrail-switch", L"wrail-switch--off"};
    auto disabledSwitchOn = switchOn;
    disabledSwitchOn.id = L"switch-cue.disabled-on";
    disabledSwitchOn.isDisabled = true;
    auto ordinarySelected = matrixButton(
        L"switch-cue.ordinary-selected", L"Selected row");
    ordinarySelected.glyph.clear();
    ordinarySelected.isSelected = true;
    auto iconOnlySelected = Node(L"switch-cue.icon-selected", L"button");
    iconOnlySelected.glyph = L"like";
    iconOnlySelected.isSelected = true;
    iconOnlySelected.baseStyle = {
        {L"width", Length(44)},
        {L"height", Length(44)},
    };
    switchSnapshot.root.children = {
        std::move(switchOn), std::move(switchOff),
        std::move(disabledSwitchOn), std::move(ordinarySelected),
        std::move(iconOnlySelected),
    };
    widgetrail::DeclarativeRenderOptions switchOptions;
    switchOptions.accessibility.contrastHook = [](
        const widgetrail::NativeColor color,
        const widgetrail::NativeColor) { return color; };
    target->BeginDraw();
    target->Clear(D2D1::ColorF(D2D1::ColorF::Black));
    const auto switchResult = renderer.Render(
        target.Get(), switchSnapshot, L"switch-cue.on",
        {0.0F, 0.0F, 240.0F, 220.0F}, switchOptions);
    Check(SUCCEEDED(target->EndDraw()) && switchResult.succeeded &&
              switchResult.buttonContentPlacements.size() == 5,
          "Switch cue fixture completes one focused high-contrast Direct2D frame");
    const auto& switchOnPlacement =
        switchResult.buttonContentPlacements.at(L"switch-cue.on");
    Check(switchOnPlacement.leading.width == 0.0F &&
              switchOnPlacement.trailingStateCue.width == 0.0F &&
              !switchResult.buttonStateCues.contains(L"switch-cue.on"),
          "focused selected Switch has no leading glyph, automatic check, or reserved cue lane under package styling and high contrast");
    const auto& switchOffPlacement =
        switchResult.buttonContentPlacements.at(L"switch-cue.off");
    Check(switchOffPlacement.leading.width == 0.0F &&
              switchOffPlacement.trailingStateCue.width == 0.0F,
          "off Switch has no leading or trailing selected-state cue");
    const auto& disabledSwitchOnPlacement =
        switchResult.buttonContentPlacements.at(L"switch-cue.disabled-on");
    Check(disabledSwitchOnPlacement.leading.width == 0.0F &&
              disabledSwitchOnPlacement.trailingStateCue.width == 0.0F &&
              !switchResult.buttonStateCues.contains(L"switch-cue.disabled-on"),
          "disabled selected Switch retains semantics and styling without an automatic cue");
    const auto& ordinarySelectedPlacement =
        switchResult.buttonContentPlacements.at(L"switch-cue.ordinary-selected");
    Check(ordinarySelectedPlacement.leading.width == 0.0F &&
              ordinarySelectedPlacement.trailingStateCue.width == 0.0F &&
              !switchResult.buttonStateCues.contains(
                  L"switch-cue.ordinary-selected"),
          "ordinary selected text navigation button has no automatic check or reserved cue lane");
    const auto& iconOnlySelectedPlacement =
        switchResult.buttonContentPlacements.at(L"switch-cue.icon-selected");
    Check(iconOnlySelectedPlacement.leading.width > 0.0F &&
              iconOnlySelectedPlacement.trailingStateCue.width == 0.0F &&
              switchResult.buttonStateCues.at(L"switch-cue.icon-selected") ==
                  L"selected-dot",
          "icon-only selected control retains its compact non-check dot");

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

    widgetrail::RemoteImageLimits limits;
    limits.maximumEntries = 32;
    widgetrail::RemoteImageCache cache(
        limits,
        {},
        [](std::wstring_view, std::stop_token, const widgetrail::RemoteImageLimits&) {
            widgetrail::RemoteDecodedImage image;
            image.width = 1;
            image.height = 1;
            image.stride = 4;
            image.premultipliedBgra = {0x10, 0x20, 0x30, 0xFF};
            image.mimeType = L"image/fake";
            return widgetrail::RemoteImageFetchResult{S_OK, std::move(image), {}};
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
            Check(cache.GetState(url) != widgetrail::RemoteImageState::Missing,
                "visible artwork enters the remote cache");
        } else {
            Check(cache.GetState(url) == widgetrail::RemoteImageState::Missing,
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

void TrustedArtworkTerminalFallbackIsStable() {
    using Microsoft::WRL::ComPtr;
    ComPtr<ID2D1Factory> d2d;
    Check(SUCCEEDED(D2D1CreateFactory(
        D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf())),
        "create D2D factory for trusted artwork fallback");
    ComPtr<IDWriteFactory> write;
    Check(SUCCEEDED(DWriteCreateFactory(
        DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
        reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))),
        "create DirectWrite factory for trusted artwork fallback");
    ComPtr<IWICImagingFactory> wic;
    Check(SUCCEEDED(CoCreateInstance(
        CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
        "create WIC factory for trusted artwork fallback");
    ComPtr<IWICBitmap> canvas;
    Check(SUCCEEDED(wic->CreateBitmap(
        420, 360, GUID_WICPixelFormat32bppPBGRA,
        WICBitmapCacheOnLoad, canvas.ReleaseAndGetAddressOf())),
        "create WIC canvas for trusted artwork fallback");
    ComPtr<ID2D1RenderTarget> target;
    Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
        canvas.Get(), D2D1::RenderTargetProperties(),
        target.ReleaseAndGetAddressOf())),
        "create WIC render target for trusted artwork fallback");

    struct Transition final {
        std::wstring key;
        widgetrail::RemoteImageState state{};
    };
    std::mutex transitionMutex;
    std::condition_variable transitionCompleted;
    std::vector<Transition> transitions;
    std::vector<std::wstring> requested;
    widgetrail::RemoteImageLimits limits;
    limits.maximumEntries = 16;
    limits.maximumDecodedBytes = 16U * 64U * 4U;
    widgetrail::RemoteImageCache cache(
        limits,
        [&](const std::wstring_view key, const widgetrail::RemoteImageState state) {
            {
                std::scoped_lock lock(transitionMutex);
                transitions.push_back({std::wstring(key), state});
            }
            transitionCompleted.notify_all();
        },
        [](std::wstring_view source, std::stop_token, const widgetrail::RemoteImageLimits&) {
            if (!source.starts_with(L"data:image/png;base64,")) {
                return widgetrail::RemoteImageFetchResult{
                    E_INVALIDARG, {}, L"Trusted artwork source lost bounded decode routing."};
            }
            widgetrail::RemoteDecodedImage image;
            image.width = 8;
            image.height = 8;
            image.stride = 32;
            image.premultipliedBgra.resize(8U * 8U * 4U);
            for (std::size_t offset = 0; offset < image.premultipliedBgra.size(); offset += 4) {
                image.premultipliedBgra[offset] = 0xE0;
                image.premultipliedBgra[offset + 1] = 0x20;
                image.premultipliedBgra[offset + 2] = 0x10;
                image.premultipliedBgra[offset + 3] = 0xFF;
            }
            image.mimeType = L"image/png";
            return widgetrail::RemoteImageFetchResult{S_OK, std::move(image), {}};
        },
        [&](const std::wstring_view key) {
            requested.emplace_back(key);
            return true;
        });

    constexpr std::wstring_view availableHandle =
        L"library.art.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    constexpr std::wstring_view pendingHandle =
        L"library.art.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    constexpr std::wstring_view unavailableHandle =
        L"library.art.cccccccccccccccccccccccccccccccc";
    constexpr std::wstring_view recoveredHandle =
        L"library.art.dddddddddddddddddddddddddddddddd";
    constexpr std::wstring_view trustedPngBase64 =
        L"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ"
        L"AAAADUlEQVR42mP8z8BQDwAFgwJ/lK3Q7wAAAABJRU5ErkJggg==";
    const auto makeSnapshot = [&](const std::wstring_view widgetId) {
        WidgetSnapshot snapshot;
        snapshot.sequence = 41;
        snapshot.instanceId = std::wstring(widgetId) + L".runtime";
        snapshot.activeInputScopeId = L"library";
        snapshot.root = Node(L"library.root", L"stack");
        snapshot.root.inputScopeId = L"library";
        snapshot.root.baseStyle = {
            {L"gap", LengthList(L"8px")},
            {L"background", Color(L"#000000")},
        };
        const auto addTile = [&](const wchar_t* state, const std::wstring_view handle) {
            const auto prefix = std::wstring(widgetId) + L"." + state;
            auto tile = Node((prefix + L".tile").c_str(), L"actionSurface");
            tile.actionId = L"launch";
            tile.accessibilityLabel = std::wstring(state) + L" application, Ready";
            tile.actionSurfaceOrientation = L"horizontal";
            tile.baseStyle = {
                {L"height", Length(104)},
                {L"padding", LengthList(L"12px")},
                {L"gap", LengthList(L"12px")},
                {L"background", Color(L"#101010")},
                {L"color", Color(L"#ffffff")},
            };
            auto artwork = Node((prefix + L".artwork").c_str(), L"image");
            artwork.artworkHandle = handle;
            artwork.accessibilityLabel = std::wstring(state) + L" application icon";
            artwork.imageFit = L"cover";
            artwork.baseStyle = {
                {L"width", Length(72)},
                {L"height", Length(72)},
                {L"flex-shrink", Number(0)},
                {L"background", Color(L"#000000")},
                {L"color", Color(L"#ffffff")},
            };
            auto label = Node((prefix + L".title").c_str(), L"text");
            label.text = std::wstring(state) + L" application";
            tile.children = {std::move(artwork), std::move(label)};
            snapshot.root.children.push_back(std::move(tile));
        };
        addTile(L"available", availableHandle);
        addTile(L"available-duplicate", availableHandle);
        addTile(L"pending", pendingHandle);
        addTile(L"unavailable", unavailableHandle);
        snapshot.initialFocusId = snapshot.root.children.front().id;
        return snapshot;
    };
    auto playniteLibrary = makeSnapshot(L"playnite-library");
    auto gamesApps = makeSnapshot(L"games-apps");
    DeclarativeRenderer renderer{d2d.Get(), write.Get(), &cache};
    const Rect viewport{0.0F, 0.0F, 420.0F, 360.0F};
    const auto render = [&](WidgetSnapshot& snapshot, const std::wstring_view widgetId) {
        widgetrail::DeclarativeRenderOptions options;
        options.collectAccessibility = true;
        options.artworkWidgetId = widgetId;
        target->BeginDraw();
        target->Clear(D2D1::ColorF(D2D1::ColorF::Black));
        auto result = renderer.Render(
            target.Get(), snapshot, snapshot.initialFocusId, viewport, options);
        Check(SUCCEEDED(target->EndDraw()),
            "production-shaped trusted artwork frame draws");
        Check(result.succeeded, "production-shaped trusted artwork frame succeeds");
        return result;
    };
    const auto launcherPending = render(playniteLibrary, L"playnite-library");
    const auto gamesPending = render(gamesApps, L"games-apps");
    Check(requested.size() == 6,
        "distinct handles request once per widget while repeated same-handle nodes reuse admission");

    const auto resolve = [&](const std::wstring_view widgetId) {
        Check(cache.SupplyTrustedArtwork(
                widgetId, availableHandle, L"image/png", std::wstring(trustedPngBase64)),
            "available trusted artwork enters bounded decode");
        Check(cache.FailTrustedArtwork(widgetId, unavailableHandle),
            "unavailable trusted artwork enters one terminal state");
    };
    resolve(L"playnite-library");
    resolve(L"games-apps");
    {
        std::unique_lock lock(transitionMutex);
        Check(transitionCompleted.wait_for(lock, std::chrono::seconds(2), [&] {
            return transitions.size() == 4;
        }), "available and unavailable trusted artwork transitions complete");
    }
    Check(cache.GetStats().queuedOrLoading == 2,
        "pending trusted artwork remains pending without a failure transition");
    const auto terminalTransitions = [&] {
        std::scoped_lock lock(transitionMutex);
        return std::count_if(transitions.begin(), transitions.end(), [](const Transition& item) {
            return item.state == widgetrail::RemoteImageState::Failed;
        });
    };
    Check(terminalTransitions() == 2,
        "each widget reports exactly one terminal artwork transition");
    const auto sameRects = [](const auto& left, const auto& right) {
        if (left.size() != right.size()) return false;
        for (const auto& [id, leftRect] : left) {
            const auto found = right.find(id);
            if (found == right.end()) return false;
            const auto& rightRect = found->second;
            if (std::abs(leftRect.x - rightRect.x) > 0.01F ||
                std::abs(leftRect.y - rightRect.y) > 0.01F ||
                std::abs(leftRect.width - rightRect.width) > 0.01F ||
                std::abs(leftRect.height - rightRect.height) > 0.01F) {
                return false;
            }
        }
        return true;
    };

    struct RasterEvidence final {
        std::size_t availableBlue{};
        std::size_t pendingBright{};
        std::size_t unavailableBright{};
    };
    const auto rasterEvidence = [&](const widgetrail::RenderResult& result, const std::wstring_view widgetId) {
        ComPtr<IWICBitmapLock> lock;
        const WICRect lockArea{0, 0, 420, 360};
        Check(SUCCEEDED(canvas->Lock(
            &lockArea, WICBitmapLockRead, lock.ReleaseAndGetAddressOf())),
            "trusted artwork raster locks");
        UINT stride = 0;
        UINT byteCount = 0;
        BYTE* pixels = nullptr;
        Check(SUCCEEDED(lock->GetStride(&stride)), "trusted artwork raster stride is available");
        Check(SUCCEEDED(lock->GetDataPointer(&byteCount, &pixels)),
            "trusted artwork raster pixels are available");
        const auto count = [&](const std::wstring& suffix, const auto& predicate) {
            const auto& rect = result.elementRects.at(std::wstring(widgetId) + suffix);
            std::size_t matches = 0;
            const auto left = static_cast<UINT>(std::max(0.0F, std::floor(rect.x)));
            const auto top = static_cast<UINT>(std::max(0.0F, std::floor(rect.y)));
            const auto right = static_cast<UINT>(std::min(420.0F, std::ceil(rect.x + rect.width)));
            const auto bottom = static_cast<UINT>(std::min(360.0F, std::ceil(rect.y + rect.height)));
            for (auto y = top; y < bottom; ++y) {
                for (auto x = left; x < right; ++x) {
                    const auto* pixel = pixels + y * stride + x * 4U;
                    if (predicate(pixel)) ++matches;
                }
            }
            return matches;
        };
        return RasterEvidence{
            count(L".available.artwork", [](const BYTE* pixel) {
                return pixel[0] > 160U && pixel[1] < 80U && pixel[2] < 80U;
            }),
            count(L".pending.artwork", [](const BYTE* pixel) {
                return std::max({pixel[0], pixel[1], pixel[2]}) > 160U;
            }),
            count(L".unavailable.artwork", [](const BYTE* pixel) {
                return std::max({pixel[0], pixel[1], pixel[2]}) > 160U;
            }),
        };
    };
    const auto verifyFixture = [&](WidgetSnapshot& snapshot,
                                   const std::wstring_view widgetId,
                                   const widgetrail::RenderResult& pendingResult) {
        auto result = render(snapshot, widgetId);
        const auto pixels = rasterEvidence(result, widgetId);
        Check(pixels.availableBlue > 1'000,
            "available trusted artwork paints supplied pixels");
        Check(pixels.pendingBright == 0,
            "pending trusted artwork does not prematurely paint a failure glyph");
        Check(pixels.unavailableBright > 8,
            "terminal trusted artwork paints the shared semantic fallback glyph");
        Check(sameRects(result.focusRects, pendingResult.focusRects) &&
              sameRects(result.navigationRects, pendingResult.navigationRects) &&
              result.hitRegions.size() == pendingResult.hitRegions.size() &&
              result.accessibilityRegions.size() == pendingResult.accessibilityRegions.size(),
            "artwork transitions preserve focus hit testing layout and accessibility semantics");
        Check(std::none_of(result.diagnostics.begin(), result.diagnostics.end(),
            [](const widgetrail::RenderDiagnostic& diagnostic) {
                return diagnostic.code == L"image_failed";
            }), "terminal trusted artwork does not emit a repaint diagnostic");
        const auto repaint = render(snapshot, widgetId);
        snapshot.sequence++;
        const auto republished = render(snapshot, widgetId);
        Check(sameRects(repaint.focusRects, republished.focusRects),
            "repaint and same-snapshot republish preserve exact tile geometry");
        Check(terminalTransitions() == 2,
            "repaint and snapshot refresh do not duplicate terminal diagnostics");
    };
    verifyFixture(playniteLibrary, L"playnite-library", launcherPending);
    verifyFixture(gamesApps, L"games-apps", gamesPending);

    auto& recoveredArtwork = playniteLibrary.root.children[3].children[0];
    recoveredArtwork.artworkHandle = recoveredHandle;
    (void)render(playniteLibrary, L"playnite-library");
    Check(!cache.SupplyTrustedArtwork(
            L"playnite-library", unavailableHandle, L"image/png",
            std::wstring(trustedPngBase64)),
        "late prior revision cannot replace current artwork");
    Check(cache.SupplyTrustedArtwork(
            L"playnite-library", recoveredHandle, L"image/png",
            std::wstring(trustedPngBase64)),
        "new trusted artwork revision can recover from terminal fallback");
    {
        std::unique_lock lock(transitionMutex);
        Check(transitionCompleted.wait_for(lock, std::chrono::seconds(2), [&] {
            return transitions.size() == 5;
        }), "new trusted artwork revision completes");
    }
    const auto recovered = render(playniteLibrary, L"playnite-library");
    const auto recoveredPixels = rasterEvidence(recovered, L"playnite-library");
    Check(recoveredPixels.unavailableBright > 1'000,
        "new revision replaces the terminal fallback with supplied pixels");
    Check(terminalTransitions() == 2,
        "successful revision recovery does not add a failure diagnostic");
    Check(cache.GetStats().entries <= limits.maximumEntries,
        "trusted artwork failure and recovery bookkeeping stays cache bounded");
    cache.Shutdown();
}

void ContentMeasurementUsesResponsiveTaffyGeometry() {
    using Microsoft::WRL::ComPtr;
    ComPtr<ID2D1Factory> d2d;
    Check(SUCCEEDED(D2D1CreateFactory(
        D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf())),
        "create D2D factory for Content measurement");
    ComPtr<IDWriteFactory> write;
    Check(SUCCEEDED(DWriteCreateFactory(
        DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
        reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))),
        "create DirectWrite factory for Content measurement");
    DeclarativeRenderer renderer(d2d.Get(), write.Get(), nullptr);

    WidgetSnapshot snapshot;
    snapshot.protocolVersion = 17;
    snapshot.instanceId = L"content-measure.runtime";
    snapshot.activeInputScopeId = L"content.root";
    snapshot.root = Node(L"content.root", L"grid");
    snapshot.root.gridMinimumColumnWidth = 180.0;
    snapshot.root.gridMaximumColumns = 3;
    snapshot.root.baseStyle = {
        {L"padding", LengthList(L"16px")},
        {L"gap", LengthList(L"12px")},
    };
    for (int index = 0; index < 6; ++index) {
        auto card = Node((L"content.card." + std::to_wstring(index)).c_str(), L"stack");
        card.baseStyle = {
            {L"padding", LengthList(L"10px")},
            {L"gap", LengthList(L"6px")},
        };
        auto title = Node(
            (L"content.title." + std::to_wstring(index)).c_str(), L"text");
        title.text = L"A responsive card title that wraps at compact widths";
        title.baseStyle = {
            {L"font-size", Length(17)},
            {L"line-height", Number(1.3)},
            {L"max-lines", Number(4)},
        };
        auto action = Node(
            (L"content.action." + std::to_wstring(index)).c_str(), L"button");
        action.text = L"Open item";
        action.actionId = L"open";
        action.baseStyle = {{L"min-height", Length(44)}};
        card.children = {std::move(title), std::move(action)};
        snapshot.root.children.push_back(std::move(card));
    }

    const auto wide = renderer.MeasureContent(snapshot, {760.0F, 700.0F}, true);
    const auto compact = renderer.MeasureContent(snapshot, {420.0F, 700.0F}, true);
    Check(wide.succeeded && compact.succeeded,
        "Content measurement accepts the generic responsive grid tree");
    Check(wide.extent.width > 0.0F && wide.extent.width <= 760.0F,
        "wide intrinsic grid remains inside the definite admitted width");
    Check(compact.extent.width > 0.0F && compact.extent.width <= 420.0F &&
              compact.extent.width < wide.extent.width,
        "Content width may shrink while remaining inside compact admission");
    Check(compact.extent.height > wide.extent.height,
        "grid reflow and wrapped text increase intrinsic height at compact width");
    Check(wide.extent.height > 44.0F && compact.extent.height > 44.0F &&
              std::isfinite(wide.extent.height) &&
              std::isfinite(compact.extent.height),
        "intrinsic height remains finite and content-derived before host clamping");

    const auto preferredWidth = renderer.MeasureContent(
        snapshot, {620.0F, 700.0F}, false);
    Check(preferredWidth.succeeded &&
              std::abs(preferredWidth.extent.width - 620.0F) < 0.01F,
        "Content height keeps an independent Preferred width definite");

    const auto invalid = renderer.MeasureContent(
        snapshot, {std::numeric_limits<float>::infinity(), 700.0F}, true);
    Check(!invalid.succeeded && std::any_of(
            invalid.diagnostics.begin(), invalid.diagnostics.end(),
            [](const widgetrail::RenderDiagnostic& diagnostic) {
                return diagnostic.code == L"invalid_measure_extent";
            }),
        "invalid measurement bounds fail closed before Taffy allocation");
}

void MediaViewportUsesFinalDeclarativeGeometry() {
    WidgetSnapshot snapshot;
    snapshot.protocolVersion = 23;
    snapshot.sequence = 7;
    snapshot.instanceId = L"aurora.instance";
    snapshot.activeInputScopeId = L"aurora.root";
    widgetrail::EmbeddedMediaSessionDeclaration mediaDeclaration;
    mediaDeclaration.id = L"aurora.primary";
    mediaDeclaration.accessibleName = L"Aurora local media";
    mediaDeclaration.entryAsset = L"media/aurora.html";
    mediaDeclaration.surface.mode = L"adaptive";
    mediaDeclaration.surface.appearance = L"theme";
    mediaDeclaration.surface.preferredWidth = 640.0;
    mediaDeclaration.surface.preferredHeight = 360.0;
    mediaDeclaration.surface.minimumWidth = 240.0;
    mediaDeclaration.surface.minimumHeight = 180.0;
    mediaDeclaration.aspectRatio = 16.0 / 9.0;
    mediaDeclaration.resources = {{L"media/aurora.html", L"text/html"}};
    mediaDeclaration.commands = {L"activate", L"togglePlayback"};
    snapshot.embeddedMediaSession = std::move(mediaDeclaration);
    snapshot.root = Node(L"aurora.root", L"stack");
    snapshot.root.baseStyle = {
        {L"padding", LengthList(L"12px")},
        {L"gap", LengthList(L"8px")},
        {L"overflow", Keyword(L"clip")},
    };
    auto title = Node(L"aurora.title", L"text");
    title.text = L"Provider-neutral Aurora sample";
    auto viewport = Node(L"aurora.viewport", L"mediaViewport");
    viewport.mediaSessionId = L"aurora.primary";
    viewport.accessibilityLabel = L"Aurora local media";
    viewport.baseStyle = {
        {L"flex-shrink", Number(1)},
        {L"min-width", Length(0)},
        {L"padding", LengthList(L"2px")},
        {L"border-width", Length(1)},
        {L"border-radius", Length(8)},
    };
    auto controls = Node(L"aurora.controls", L"button");
    controls.text = L"Play or pause";
    controls.accessibilityLabel = controls.text;
    controls.actionId = L"aurora.toggle";
    snapshot.root.children = {title, viewport, controls};

    using Microsoft::WRL::ComPtr;
    ComPtr<ID2D1Factory> d2d;
    Check(SUCCEEDED(D2D1CreateFactory(
        D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf())),
        "MediaViewport creates a D2D factory");
    ComPtr<IDWriteFactory> write;
    Check(SUCCEEDED(DWriteCreateFactory(
        DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
        reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))),
        "MediaViewport creates a DirectWrite factory");
    ComPtr<IWICImagingFactory> wic;
    Check(SUCCEEDED(CoCreateInstance(
        CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
        "MediaViewport creates a WIC factory");
    ComPtr<IWICBitmap> canvas;
    Check(SUCCEEDED(wic->CreateBitmap(
        760, 520, GUID_WICPixelFormat32bppPBGRA,
        WICBitmapCacheOnLoad, canvas.ReleaseAndGetAddressOf())),
        "MediaViewport creates a WIC canvas");
    ComPtr<ID2D1RenderTarget> target;
    Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
        canvas.Get(), D2D1::RenderTargetProperties(),
        target.ReleaseAndGetAddressOf())),
        "MediaViewport creates a WIC render target");
    DeclarativeRenderer renderer{d2d.Get(), write.Get(), nullptr};
    const auto renderAt = [&](const Size size, const float scale) {
        widgetrail::DeclarativeRenderOptions options;
        options.pixelScale = scale;
        options.responsiveViewport = size;
        options.collectAccessibility = true;
        target->BeginDraw();
        auto result = renderer.Render(
            target.Get(), snapshot, L"aurora.controls",
            {0.0F, 0.0F, size.width, size.height}, options);
        Check(SUCCEEDED(target->EndDraw()),
            "MediaViewport raster draw completes");
        return result;
    };
    const auto wide = renderAt({760.0F, 520.0F}, 1.0F);
    const auto compact = renderAt({420.0F, 320.0F}, 1.5F);
    for (const auto* result : {&wide, &compact}) {
        Check(result->succeeded, "MediaViewport sample renders successfully");
        Check(result->mediaViewportRegions.size() == 1,
            "exactly one renderer-owned media viewport is published");
        const auto& media = result->mediaViewportRegions.front();
        Check(media.nodeId == L"aurora.viewport" &&
              media.mediaSessionId == L"aurora.primary",
            "media geometry retains exact node and surface identity");
        Check(media.bounds.width > 0.5F && media.bounds.height > 0.5F &&
              media.clip.width > 0.5F && media.clip.height > 0.5F,
            "media geometry and effective clip remain positive");
        Check(media.clip.x >= media.bounds.x - 0.01F &&
              media.clip.y >= media.bounds.y - 0.01F &&
              media.clip.x + media.clip.width <=
                  media.bounds.x + media.bounds.width + 0.01F &&
              media.clip.y + media.clip.height <=
                  media.bounds.y + media.bounds.height + 0.01F,
            "effective media clip is derived from the final native layout box");
        Check(std::any_of(
                result->accessibilityRegions.begin(),
                result->accessibilityRegions.end(),
                [](const auto& region) {
                    return region.nodeId == L"aurora.viewport";
                }),
            "MediaViewport contributes one native accessibility semantic");
        Check(!result->navigationRects.contains(L"aurora.viewport") &&
              std::none_of(
                  result->hitRegions.begin(), result->hitRegions.end(),
                  [](const auto& region) {
                      return region.nodeId == L"aurora.viewport";
                  }),
            "MediaViewport does not create a second focus or pointer graph");
    }
    Check(compact.mediaViewportRegions.front().bounds.width <
              wide.mediaViewportRegions.front().bounds.width,
        "the same provider-neutral viewport responds to compact layout");
}

void BitmapRetentionPolicyIsBounded() {
    widgetrail::DeclarativeRenderer renderer(nullptr, nullptr, nullptr);
    const auto stats = renderer.GetImageBitmapCacheStats();
    Check(stats.entries == 0 && stats.bytes == 0,
        "new bitmap cache starts empty");
    Check(stats.maximumEntries == 256,
        "bitmap cache retains a bounded ready-entry working set");
    Check(stats.maximumEntryBytes == 32U * 1024U * 1024U,
        "bitmap cache preserves the per-image safety ceiling");
    Check(stats.maximumBytes == 96U * 1024U * 1024U,
        "bitmap cache uses the distinct aggregate GPU retention budget");
}

} // namespace

int main() {
    const auto initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    Check(SUCCEEDED(initialized), "initialize COM");
    ImagePlacementMath();
    ButtonContentPlacementUsesSharedOpticalGeometry();
    AccessibleStatePresentation();
    PressedComputedStyleLayersOnFocusedState();
    PlanningMetadataAndKinds();
    FocusAssociatedPresentationUsesNativeFocusAuthority();
    ResponsiveVisibilityExcludesInactiveSubtrees();
    ResponsiveNavigationShellFitsBoundedSurfaces();
    SliderPlanningAndAccessibilityTargets();
    ActionSurfacePlanningAndInteractionGeometry();
    PosterTileUsesFixedFullBleedGeometry();
    BackgroundSurfacePreservesForegroundAuthority();
    ResponsiveGridFlowsThroughNativePlanning();
    FocusMotionUsesStableSnapshotIdentity();
    SubtreeTranslationKeepsPresentationGeometryAligned();
    TranslationRetargetsAndSnapsDeterministically();
    TranslatedFocusConvergesInsideScrollViewport();
    ClippedControlsAreNotFocusCandidates();
    ControllerScrollFollowsFocusAndRestoresState();
    CursorCollectionPreservesKeyedViewportAnchor();
    VirtualCollectionWindowKeepsNativeWorkBounded();
    WholeWidgetScrollRevealsAudioMixerControls();
    SegmentedTabsSurviveConstrainedNetworkSurfaces();
    CenteredChildrenDoNotDisableParentStretch();
    CenteredWrappedStatePreservesTextFlowAndControllerTarget();
    SpotifyStateAndSetupCardsPreserveWrappedTextHeight();
    ResponsiveRowWrapFlowsThroughGbssAndNativePlanning();
    WrappedPermissionCopyContributesToScrollExtent();
    ScrollFocusReachesTrueContentBoundaries();
    NestedScrollFocusFollowReachesFixedPoint();
    OversizedFocusFollowUsesOneAxisSymmetricRevealOwner();
    IrrevealableClipsDoNotBecomeFocusTraps();
    ScrollStateCapEvictsOnlyInactiveLruEntries();
    DeferredFocusOutlineUsesEffectiveVisibilityClip();
    IncrementalPresentationPlanningRetainsBoundedWork();
    RealDirect2DSmoke();
    OffscreenScrollArtworkDoesNotEnterRemoteCache();
    TrustedArtworkTerminalFallbackIsStable();
    ContentMeasurementUsesResponsiveTaffyGeometry();
    MediaViewportUsesFinalDeclarativeGeometry();
    BitmapRetentionPolicyIsBounded();
    std::cout << "DeclarativeRendererTests: " << checks << " checks passed\n";
    CoUninitialize();
    return EXIT_SUCCESS;
}
