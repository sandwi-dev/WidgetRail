#include "DeclarativeRenderer.h"

#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <string_view>
#include <utility>

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

gba::WidgetStyleValue Number(const double number) {
    return {L"number", std::to_wstring(number), number, {}};
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
    row.children = {enabled, disabled};

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
    const auto first = renderer.Render(nullptr, snapshot, L"play", {0.0F, 0.0F, 960.0F, 540.0F});
    Check(!first.succeeded, "null target is reported as unsuccessful");
    Check(first.hitRegions.size() == 2, "buttons produce hit regions");
    Check(first.focusRects.size() == 2, "buttons produce focus rectangles");
    Check(first.currentFocusRect.has_value(), "focused button produces current focus rect");
    Check(first.hitRegions[0].enabled, "normal button is enabled");
    Check(!first.hitRegions[1].enabled, "disabled button is not actionable");
    Check(first.focusRects.contains(L"play"), "focus metadata uses stable widget ID");
    Check(first.focusRects.at(L"play").width > 0.0F, "planned button has positive width");

    for (const auto& diagnostic : first.diagnostics)
        Check(diagnostic.code != L"unknown_kind", "all public node kinds are recognized");

    const auto second = renderer.Render(nullptr, snapshot, L"play", {0.0F, 0.0F, 960.0F, 540.0F});
    Near(second.focusRects.at(L"play").x, first.focusRects.at(L"play").x,
        "planning is deterministic (x)");
    Near(second.focusRects.at(L"play").y, first.focusRects.at(L"play").y,
        "planning is deterministic (y)");
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

void DeferredFocusOutlineUsesOnlyScrollVisibilityClip() {
    WidgetSnapshot normal;
    normal.root = FixedButton(L"normal-focus");
    DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    const auto normalResult = renderer.Render(
        nullptr, normal, L"normal-focus", {0.0F, 0.0F, 120.0F, 90.0F});
    Check(!normalResult.currentFocusOutlineClip.has_value(),
          "ordinary deferred outline remains free of layout clipping");

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
    snapshot.root.children = {button, selectedToggle, offToggle};

    DeclarativeRenderer renderer{d2d.Get(), write.Get(), nullptr};
    target->BeginDraw();
    target->Clear(D2D1::ColorF(0.02F, 0.02F, 0.03F, 1.0F));
    const auto result = renderer.Render(
        target.Get(), snapshot, L"confirm", {0.0F, 0.0F, 640.0F, 360.0F});
    Check(SUCCEEDED(target->EndDraw()), "complete Direct2D draw");
    Check(result.succeeded, "real Direct2D render succeeds");
    Check(result.currentFocusRect.has_value(), "real render returns focused geometry");
    Check(result.hitRegions.size() == 3,
          "buttons with selected state cues render without requiring primary glyphs");
}

} // namespace

int main() {
    const auto initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    Check(SUCCEEDED(initialized), "initialize COM");
    ImagePlacementMath();
    AccessibleStatePresentation();
    PlanningMetadataAndKinds();
    ClippedControlsAreNotFocusCandidates();
    ControllerScrollFollowsFocusAndRestoresState();
    NestedScrollFocusFollowReachesFixedPoint();
    IrrevealableClipsDoNotBecomeFocusTraps();
    ScrollStateCapEvictsOnlyInactiveLruEntries();
    DeferredFocusOutlineUsesOnlyScrollVisibilityClip();
    RealDirect2DSmoke();
    std::cout << "DeclarativeRendererTests: " << checks << " checks passed\n";
    CoUninitialize();
    return EXIT_SUCCESS;
}
