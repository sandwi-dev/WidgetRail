#include "OverlayChrome.h"
#include "OverlayCompositionSurface.h"
#include "OverlayState.h"
#include "TrayLayout.h"
#include "ControllerGuideVisual.h"
#include "PopupCompositionScene.h"
#include "SurfaceDepth.h"
#include "NativeIcons.h"
#include "PreparationFrameBudget.h"
#pragma comment(lib, "dwrite.lib")

#include <Windows.h>
#include <dwmapi.h>
#pragma comment(lib, "dwmapi.lib")
#pragma comment(lib, "gdi32.lib")
#include <d2d1.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <string>
#include <string_view>
#include <utility>

namespace {

using Microsoft::WRL::ComPtr;

int checks{};

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void CheckCompositionCoordinatePolicies() {
    widgetrail::shell::CompositionRecoverySchedule recovery;
    Check(!recovery.delay() && !recovery.BeginAttempt(), "healthy composition schedules no work");
    recovery.Request();
    for (const UINT expected : {250U, 1000U, 3000U}) {
        Check(recovery.delay() == expected && recovery.BeginAttempt(),
              "device recovery backs off between bounded attempts");
        recovery.Request();
    }
    Check(!recovery.delay() && !recovery.BeginAttempt(),
          "repeated failed paints cannot restart an exhausted recovery loop");
    Check(recovery.softwareAttempt(), "final recovery attempt uses the software compositor");
    recovery.Reopen();
    Check(recovery.delay() == 250 && recovery.BeginAttempt(),
          "a later explicit open rearms exhausted recovery");
    recovery.Complete();
    Check(!recovery.delay(), "successful recovery cancels retries");
    recovery.Request();
    Check(recovery.delay() == 250, "a later device loss starts a new bounded recovery");
    const auto dpi120 = widgetrail::NormalizeCompositionUpdateOffset({5, -10}, 120);
    const auto dpi144 = widgetrail::NormalizeCompositionUpdateOffset({3, 9}, 144);
    const auto fallback = widgetrail::NormalizeCompositionUpdateOffset({7, -4}, 0);
    Check(std::abs(dpi120.x - 4.0F) < 0.001F &&
              std::abs(dpi120.y + 8.0F) < 0.001F,
          "125-percent BeginDraw pixels normalize to DIPs");
    Check(std::abs(dpi144.x - 2.0F) < 0.001F &&
              std::abs(dpi144.y - 6.0F) < 0.001F,
          "150-percent BeginDraw pixels normalize to DIPs");
    Check(fallback.x == 7.0F && fallback.y == -4.0F,
          "missing DPI uses the 96-DPI update-offset contract");

    const auto approximatelyEqual = [](const float left, const float right) {
        return std::abs(left - right) < 0.001F;
    };
    const auto surfacePoint = [](
        const widgetrail::CompositionUpdateRasterMapping& mapping,
        const D2D1_POINT_2F logicalPoint) {
        const D2D1_POINT_2F atlasPoint{
            logicalPoint.x * mapping.scenePixelsPerDip +
                mapping.sceneTranslationPixels.x,
            logicalPoint.y * mapping.scenePixelsPerDip +
                mapping.sceneTranslationPixels.y,
        };
        return D2D1_POINT_2F{
            static_cast<float>(mapping.requestedPixels.left) + atlasPoint.x -
                static_cast<float>(mapping.atlasOffsetPixels.x),
            static_cast<float>(mapping.requestedPixels.top) + atlasPoint.y -
                static_cast<float>(mapping.atlasOffsetPixels.y),
        };
    };
    struct RasterCase final {
        float physicalPixelsPerDip;
        RECT fullRequest;
        RECT boundedRequest;
        POINT fullAtlasOffset;
        POINT boundedAtlasOffset;
    };
    constexpr std::array rasterCases{
        RasterCase{1.0F, {0, 0, 980, 505}, {155, 235, 962, 312},
                   {31, 47}, {53, 79}},
        RasterCase{1.25F, {0, 0, 1225, 631}, {193, 293, 1202, 390},
                   {37, 59}, {61, 83}},
        RasterCase{1.5F, {0, 0, 1470, 758}, {232, 352, 1443, 468},
                   {41, 67}, {71, 97}},
        RasterCase{1.5625F, {0, 0, 1532, 790}, {242, 367, 1503, 488},
                   {43, 73}, {89, 101}},
    };
    for (const auto& test : rasterCases) {
        const auto full = widgetrail::PlanCompositionUpdateRasterMapping(
            test.fullRequest, test.fullAtlasOffset,
            test.physicalPixelsPerDip);
        const auto bounded = widgetrail::PlanCompositionUpdateRasterMapping(
            test.boundedRequest, test.boundedAtlasOffset,
            test.physicalPixelsPerDip);
        Check(approximatelyEqual(
                  full.mappedRequestedOriginPixels.x,
                  static_cast<float>(test.fullAtlasOffset.x)) &&
                  approximatelyEqual(
                      full.mappedRequestedOriginPixels.y,
                      static_cast<float>(test.fullAtlasOffset.y)) &&
                  approximatelyEqual(
                      bounded.mappedRequestedOriginPixels.x,
                      static_cast<float>(test.boundedAtlasOffset.x)) &&
                  approximatelyEqual(
                      bounded.mappedRequestedOriginPixels.y,
                      static_cast<float>(test.boundedAtlasOffset.y)),
              "full and bounded update origins map to their physical atlas offsets");

        const auto fullSceneOrigin = surfacePoint(full, {0.0F, 0.0F});
        const auto boundedSceneOrigin = surfacePoint(bounded, {0.0F, 0.0F});
        Check(approximatelyEqual(fullSceneOrigin.x, 0.0F) &&
                  approximatelyEqual(fullSceneOrigin.y, 0.0F) &&
                  approximatelyEqual(boundedSceneOrigin.x, 0.0F) &&
                  approximatelyEqual(boundedSceneOrigin.y, 0.0F),
              "full and bounded updates retain the same physical scene origin");

        const D2D1_POINT_2F logicalSample{
            static_cast<float>(test.boundedRequest.left + 17) /
                test.physicalPixelsPerDip,
            static_cast<float>(test.boundedRequest.top + 11) /
                test.physicalPixelsPerDip,
        };
        const auto fullSample = surfacePoint(full, logicalSample);
        const auto boundedSample = surfacePoint(bounded, logicalSample);
        Check(approximatelyEqual(fullSample.x, boundedSample.x) &&
                  approximatelyEqual(fullSample.y, boundedSample.y) &&
                  approximatelyEqual(
                      fullSample.x,
                      logicalSample.x * test.physicalPixelsPerDip) &&
                  approximatelyEqual(
                      fullSample.y,
                      logicalSample.y * test.physicalPixelsPerDip),
              "full and bounded requests rasterize logical content at identical physical pixels");
    }

    widgetrail::OverlayCompositionSurface::Frame content;
    content.layer = widgetrail::OverlayCompositionSurface::Layer::Content;
    content.replacement = false;
    widgetrail::OverlayCompositionSurface::Frame guide;
    guide.layer = widgetrail::OverlayCompositionSurface::Layer::Guide;
    guide.replacement = false;
    widgetrail::OverlayCompositionSurface::Frame tray;
    tray.layer = widgetrail::OverlayCompositionSurface::Layer::Tray;
    tray.replacement = true;
    Check(widgetrail::OverlayCompositionSurface::FrameOwnsVisualOffset(content),
          "ordinary content repaint owns its visual offset");
    Check(!widgetrail::OverlayCompositionSurface::FrameOwnsVisualOffset(guide),
          "ordinary guide repaint preserves its latched chrome offset");
    Check(!widgetrail::OverlayCompositionSurface::FrameOwnsVisualOffset(tray),
          "tray surface replacement preserves its latched chrome offset");

    constexpr RECT work{0, 0, 1920, 1080};
    const auto panel = widgetrail::shell::ComputeContentWindowBoundsAboveGuide(
        work, 900, 760, 385, 5);
    Check(panel && panel->left == 580 && panel->right == 1340 &&
              panel->top == 510 && panel->bottom == 895,
          "panel-local content is centered and ends at the authored guide gap");
    Check(!widgetrail::shell::ComputeContentWindowBoundsAboveGuide(
              work, 900, 0, 385, 5) &&
              !widgetrail::shell::ComputeContentWindowBoundsAboveGuide(
                  work, 900, 760, 385, -1),
          "invalid panel-local content placement fails closed");
}

void CheckTrayMenuCompositionHeadroom() {
    constexpr float itemHeight = 48.0F;
    constexpr float menuGap = 8.0F;
    constexpr std::size_t maximumItems = 3;
    constexpr float headroom = menuGap + itemHeight * maximumItems;
    constexpr float viewportWidth = 640.0F;
    constexpr float viewportHeight = 360.0F;
    constexpr float pixelsPerDip = 1.5F;

    const auto tray = widgetrail::shell::ComputeTrayLayout(
        viewportWidth, viewportHeight, 4, 1,
        widgetrail::shell::TrayBand{headroom, viewportHeight});
    Check(tray && tray->tiles.size() == 4,
          "headroom fixture retains the full current tray catalog");

    const auto selectedTile = std::find_if(
        tray->tiles.begin(), tray->tiles.end(),
        [](const auto& tile) { return tile.slot == 1; });
    Check(selectedTile != tray->tiles.end(),
          "headroom fixture resolves its selected catalog identity");
    const auto& selected = selectedTile->bounds;
    const float menuWidth = std::min(286.0F, viewportWidth - 16.0F);
    const float menuHeight = itemHeight * maximumItems;
    const float menuLeft = std::clamp(
        selected.x + selected.width * 0.5F - menuWidth * 0.5F,
        8.0F, viewportWidth - menuWidth - 8.0F);
    const widgetrail::declarative::Rect menu{
        menuLeft, tray->stripBounds.y - menuGap - menuHeight,
        menuWidth, menuHeight};
    Check(menu.y >= 0.0F &&
              menu.y + menu.height + menuGap == tray->stripBounds.y,
          "three-row menu fits wholly above the tray in reserved headroom");
    Check(menu.x >= 8.0F && menu.x + menu.width <= viewportWidth - 8.0F,
          "menu remains horizontally clamped inside the chrome viewport");

    const RECT workArea{100, 50, 1700, 950};
    const LONG chromeWidth = static_cast<LONG>(std::ceil(
        viewportWidth * pixelsPerDip));
    const LONG chromeHeight = static_cast<LONG>(std::ceil(
        viewportHeight * pixelsPerDip));
    const RECT chrome = widgetrail::shell::ComputeFixedChromeWindowBounds(
        workArea, chromeWidth, chromeHeight);
    const auto project = [&](const widgetrail::declarative::Rect& bounds) {
        return RECT{
            chrome.left + static_cast<LONG>(std::floor(bounds.x * pixelsPerDip)),
            chrome.top + static_cast<LONG>(std::floor(bounds.y * pixelsPerDip)),
            chrome.left + static_cast<LONG>(std::ceil(
                (bounds.x + bounds.width) * pixelsPerDip)),
            chrome.top + static_cast<LONG>(std::ceil(
                (bounds.y + bounds.height) * pixelsPerDip)),
        };
    };
    const RECT menuScreen = project(menu);
    const RECT trayScreen = project(tray->stripBounds);
    Check(menuScreen.left >= chrome.left && menuScreen.top >= chrome.top &&
              menuScreen.right <= chrome.right && menuScreen.bottom <= chrome.bottom,
          "above-tray menu pixels remain inside the topmost chrome HWND client extent");

    const POINT menuPoint{
        (menuScreen.left + menuScreen.right) / 2,
        (menuScreen.top + menuScreen.bottom) / 2};
    const POINT transparentHeadroomPoint{
        chrome.left + 2,
        menuScreen.top + 2};
    Check(PtInRect(&menuScreen, menuPoint) != FALSE &&
              !widgetrail::shell::IsFixedChromeHit(
                  menuPoint, RECT{}, trayScreen),
          "menu is the sole extra hit owner above the ordinary guide/tray policy");
    Check(PtInRect(&menuScreen, transparentHeadroomPoint) == FALSE &&
              !widgetrail::shell::IsFixedChromeHit(
                  transparentHeadroomPoint, RECT{}, trayScreen),
          "non-menu composition headroom remains transparent to pointer hit testing");

    std::array<widgetrail::declarative::Rect, maximumItems> rows{};
    for (std::size_t index = 0; index < rows.size(); ++index) {
        rows[index] = {
            menu.x, menu.y + itemHeight * static_cast<float>(index),
            menu.width, itemHeight};
    }
    Check(rows.front().x == menu.x && rows.front().y == menu.y &&
              rows.back().x + rows.back().width == menu.x + menu.width &&
              rows.back().y + rows.back().height == menu.y + menu.height,
          "one row geometry exactly partitions render, pointer, and UIA menu bounds");
}

void CheckRenderedGuideContentCentering() {
    struct CenteredChrome final {
        LONG chromeHeight{};
        LONG guideTop{};
        LONG guideContentBottom{};
        LONG trayTop{};
        LONG gapAbove{};
        LONG gapBelow{};
    };
    const auto center = [](
        LONG chromeHeight,
        const LONG workHeight,
        LONG guideTop,
        LONG guideContentBottom,
        const LONG traySurfaceHeight,
        const LONG localStripTop,
        const LONG localStripBottom) {
        const LONG stripHeight = localStripBottom - localStripTop;
        const LONG freeBandHeight = chromeHeight - guideContentBottom;
        LONG centeredStripTop = guideContentBottom +
            (freeBandHeight - stripHeight) / 2;
        LONG requestedTrayTop = centeredStripTop - localStripTop;
        if (requestedTrayTop < 0) {
            const LONG expansion = std::min(
                -requestedTrayTop, workHeight - chromeHeight);
            chromeHeight += expansion;
            guideTop += expansion;
            guideContentBottom += expansion;
            centeredStripTop += expansion;
            requestedTrayTop += expansion;
        }
        const LONG trayTop = std::clamp(
            requestedTrayTop, 0L, chromeHeight - traySurfaceHeight);
        return CenteredChrome{
            chromeHeight,
            guideTop,
            guideContentBottom,
            trayTop,
            trayTop + localStripTop - guideContentBottom,
            chromeHeight - (trayTop + localStripBottom),
        };
    };

    constexpr LONG workHeight = 1080;
    constexpr LONG baseChromeHeight = 328;
    constexpr LONG baseGuideTop = 152;
    constexpr LONG traySurfaceHeight = 224;
    constexpr LONG menuHeadroom = 152;
    constexpr LONG localStripTop = 156;
    constexpr LONG localStripBottom = 220;
    const auto openWidget = center(
        baseChromeHeight, workHeight, baseGuideTop, 198,
        traySurfaceHeight, localStripTop, localStripBottom);
    Check(openWidget.chromeHeight == baseChromeHeight &&
              std::abs(openWidget.gapAbove - openWidget.gapBelow) <= 1,
          "open-widget tray centers below the rendered footer text rather than the guide client bottom");

    const auto dashboard = center(
        baseChromeHeight, workHeight, baseGuideTop, 35,
        traySurfaceHeight, localStripTop, localStripBottom);
    const LONG guideScreenBefore = workHeight - baseChromeHeight + baseGuideTop;
    const LONG guideScreenAfter =
        workHeight - dashboard.chromeHeight + dashboard.guideTop;
    Check(dashboard.chromeHeight > baseChromeHeight &&
              dashboard.trayTop >= 0 &&
              dashboard.trayTop + localStripTop - menuHeadroom >= 0 &&
              guideScreenAfter == guideScreenBefore &&
              std::abs(dashboard.gapAbove - dashboard.gapBelow) <= 1,
          "dashboard centering grows chrome upward boundedly while preserving guide anchor and menu headroom");

    const auto sourcePath =
        std::filesystem::path{__FILE__}.parent_path() / "main.cpp";
    std::ifstream sourceStream(sourcePath, std::ios::binary);
    const std::string source{
        std::istreambuf_iterator<char>{sourceStream},
        std::istreambuf_iterator<char>{}};
    const auto ensureBegin = source.find("bool EnsureCompositionChromeSession()");
    const auto ensureEnd = source.find(
        "AnchorContentPlacementToChrome(", ensureBegin);
    Check(sourceStream.good() || sourceStream.eof(),
          "fixed-chrome production source is readable");
    Check(ensureBegin != std::string::npos && ensureEnd != std::string::npos,
          "fixed-chrome session owner has a bounded source slice");
    const std::string_view ensureOwner{
        source.data() + ensureBegin, ensureEnd - ensureBegin};
    Check(ensureOwner.find("WidgetGuideContentBottomDip(guideHeightDip)") !=
              std::string_view::npos &&
          ensureOwner.find("DashboardGuideContentBottomDip") ==
              std::string_view::npos &&
          ensureOwner.find("chromeHeight - session.renderedGuideContentBottom") !=
              std::string_view::npos &&
          ensureOwner.find("centeredStripTop = session.renderedGuideContentBottom") !=
              std::string_view::npos,
          "fixed chrome always reserves its own guide independently of startup content mode");
    Check(ensureOwner.find("session.guideClientBounds.top += topExpansion") !=
              std::string_view::npos &&
          ensureOwner.find("session.renderedGuideContentBottom += topExpansion") !=
              std::string_view::npos &&
          ensureOwner.find("ComputeFixedChromeWindowBounds") !=
              std::string_view::npos &&
          ensureOwner.find("tray-visual-gaps=") != std::string_view::npos,
          "bounded upward expansion preserves the guide screen anchor and reports both visual gaps");
    Check(ensureOwner.find(
              "ComputeTrayCapacityWidth(\n                metrics->viewportWidthDip)") !=
                  std::string_view::npos &&
              ensureOwner.find("session.trayCapacityWidthDip = trayCapacityWidthDip") !=
                  std::string_view::npos,
          "fixed chrome resolves the monitor sixty-percent capacity exactly once");

    const auto localLayoutBegin = source.find(
        "ComputeCompositionTrayLayout(\n        const CompositionChromeSession& session)");
    const auto localLayoutEnd = source.find(
        "CurrentCompositionTrayLayout() const", localLayoutBegin);
    Check(localLayoutBegin != std::string::npos &&
              localLayoutEnd != std::string::npos,
          "fixed-chrome child layout owner has a bounded source slice");
    const std::string_view localLayoutOwner{
        source.data() + localLayoutBegin, localLayoutEnd - localLayoutBegin};
    Check(localLayoutOwner.find("session.trayCapacityWidthDip") !=
              std::string_view::npos &&
              localLayoutOwner.find("TrayWidthBasis::ExactCapacity") !=
              std::string_view::npos &&
              localLayoutOwner.find("ComputeTrayCapacityWidth") ==
              std::string_view::npos,
          "fixed-chrome child layout consumes exact capacity without applying sixty percent again");

    const auto identityBegin = source.find("TrayWidgetPaintIdentity(");
    const auto identityEnd = source.find(
        "void ApplyTransitionWindowOpacity(", identityBegin);
    const auto stateBegin = source.find("CurrentTrayPaintState(");
    const auto stateEnd = source.find("bool RenderCompositionLayer(", stateBegin);
    const auto drawBegin = source.find("void DrawIconStrip(");
    const auto drawEnd = source.find("static std::wstring_view DisplayButton", drawBegin);
    Check(identityBegin != std::string::npos && identityEnd > identityBegin &&
              stateBegin != std::string::npos && stateEnd != std::string::npos &&
              drawBegin != std::string::npos && drawEnd != std::string::npos,
          "tray visual-policy owners have bounded source slices");
    const std::string_view retainedOwner{
        source.data() + stateBegin, stateEnd - stateBegin};
    const std::string_view identityOwner{
        source.data() + identityBegin, identityEnd - identityBegin};
    const std::string_view drawOwner{
        source.data() + drawBegin, drawEnd - drawBegin};
    Check(retainedOwner.find(
              "selected &&\n                    state_.focusRegion() == widgetrail::FocusRegion::Tray") !=
              std::string_view::npos &&
          retainedOwner.find("TrayWidgetPaintIdentity(") !=
              std::string_view::npos &&
          identityOwner.find("ResolvePackageIconDemandAuthority(") !=
              std::string_view::npos &&
          identityOwner.find("GetPackageIconState(") !=
              std::string_view::npos &&
          identityOwner.find("? L\":ready\" : L\":fallback\"") !=
              std::string_view::npos &&
          drawOwner.find("traySelectedBrush_.Get()") != std::string_view::npos &&
          drawOwner.find("TrayPackageIconBounds(tileLayout.bounds)") !=
              std::string_view::npos &&
          drawOwner.find("iconOptions.pixelScale = physicalPixelsPerDip") !=
              std::string_view::npos &&
          drawOwner.find(
              "state_.focusRegion() == widgetrail::FocusRegion::Tray") !=
              std::string_view::npos,
          "selection and tray-owned focus remain distinct retained tile visuals");
    const auto imageCompletionBegin = source.find(
        "[this](const std::wstring_view source, const widgetrail::RemoteImageState state)");
    const auto imageCompletionEnd = source.find(
        "widgetrail::RemoteImageCache::FetchFunction{}", imageCompletionBegin);
    const auto imageMessageBegin = source.find("case kImageReadyMessage:");
    const auto imageMessageEnd = source.find(
        "case kCatalogRefreshMessage:", imageMessageBegin);
    Check(imageCompletionBegin != std::string::npos &&
              imageCompletionEnd > imageCompletionBegin &&
              imageMessageBegin != std::string::npos &&
              imageMessageEnd > imageMessageBegin,
          "image completion and message owners have bounded source slices");
    const std::string_view completionOwner{
        source.data() + imageCompletionBegin,
        imageCompletionEnd - imageCompletionBegin};
    const std::string_view messageOwner{
        source.data() + imageMessageBegin,
        imageMessageEnd - imageMessageBegin};
    Check(completionOwner.find(
              "source.starts_with(packageIconPrefix) ? 1 : 0") !=
                  std::string_view::npos &&
          messageOwner.find("RequiresImageReadyRepaint(") !=
              std::string_view::npos &&
          messageOwner.find("AdvanceCompositorBackground(GetTickCount64())") !=
              std::string_view::npos,
          "package icon completion preserves its scoped tray comparison alongside background advancement");
    Check(drawOwner.find("FillColorKeyRoundedRectangle") ==
              std::string_view::npos,
          "floating tray paint reserves no outer background panel");

    const auto pointerBegin = source.find("void HandlePointerActivation(");
    const auto pointerEnd = source.find(
        "HitTestAuthoredCompositionSurface(", pointerBegin);
    const auto accessibilityBegin = source.find("void PublishTrayAccessibility(");
    const auto accessibilityEnd = source.find(
        "ReconcileResponsiveFocusPersistence(", accessibilityBegin);
    const auto frameBegin = source.find("void DrawCurrentFrame(");
    const auto frameEnd = source.find(
        "struct CompositionLayerGeometry final", frameBegin);
    const auto renderBegin = source.find("bool RenderCompositionLayer(");
    const auto renderEnd = source.find("bool RenderCompositionFrames(", renderBegin);
    Check(pointerBegin != std::string::npos &&
              pointerEnd != std::string::npos &&
              accessibilityBegin != std::string::npos &&
              accessibilityEnd != std::string::npos &&
              frameBegin != std::string::npos &&
              frameEnd != std::string::npos && frameBegin < frameEnd &&
              renderBegin != std::string::npos &&
              renderEnd != std::string::npos && renderBegin < renderEnd,
          "tray pointer, accessibility, and paint owners have bounded source slices");
    const std::string_view pointerOwner{
        source.data() + pointerBegin, pointerEnd - pointerBegin};
    const std::string_view accessibilityOwner{
        source.data() + accessibilityBegin,
        accessibilityEnd - accessibilityBegin};
    const std::string_view frameOwner{
        source.data() + frameBegin, frameEnd - frameBegin};
    const std::string_view renderOwner{
        source.data() + renderBegin, renderEnd - renderBegin};
    Check(pointerOwner.find("CurrentCompositionTrayLayout()") !=
              std::string_view::npos &&
              pointerOwner.find("HitTestTray(*trayLayout, trayX, trayY)") !=
              std::string_view::npos &&
              pointerOwner.find("HitTestTrayOverflow(\n                           *trayLayout") !=
              std::string_view::npos,
          "pointer routing consumes the exact current fixed-chrome tray layout");
    Check(accessibilityOwner.find("BuildTrayTree(") != std::string_view::npos &&
              accessibilityOwner.find("items, layout, state_.selectedSlot()") !=
                  std::string_view::npos,
          "accessibility consumes the supplied final tray layout authority");
    Check(renderOwner.find("DrawCurrentFrame(") != std::string_view::npos &&
              renderOwner.find("paintLayer, trayLayout, guideBounds") !=
                  std::string_view::npos,
          "composition forwards the exact tray layout to the current frame owner");
    Check(frameOwner.find(
              "layer == CompositionPaintLayer::Tray && trayLayout") !=
                  std::string_view::npos &&
              frameOwner.find(
                  "metrics->viewportWidthDip, metrics->viewportHeightDip") !=
                  std::string_view::npos &&
              frameOwner.find("nullptr, nullptr, trayLayout, false") !=
                  std::string_view::npos,
          "the current frame tray branch paints the exact forwarded layout once");
}

void CheckControllerGlyphs(ID2D1Factory* d2d, IWICImagingFactory* wic) {
    ComPtr<IDWriteFactory> write;
    Check(SUCCEEDED(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED,
        __uuidof(IDWriteFactory), reinterpret_cast<IUnknown**>(write.GetAddressOf()))),
        "glyph test creates DirectWrite factory");
    for (const bool playStation : {false, true}) {
    (void)widgetrail::guide::SetPlayStationControls(playStation);
    Check(widgetrail::guide::PromptCharacter(widgetrail::guide::Control::A) == (playStation ? 0xE04CU : 0xE005U),
        "active controller changes Cross versus A glyph");
    for (const auto [size, fontSize] : {std::pair{24.0F,14.0F}, {28.0F,14.0F}, {36.0F,21.0F}}) {
        const auto render = [&](widgetrail::guide::Control control) {
            constexpr UINT side = 48;
            ComPtr<IWICBitmap> bitmap;
            Check(SUCCEEDED(wic->CreateBitmap(side, side, GUID_WICPixelFormat32bppPBGRA,
                WICBitmapCacheOnLoad, bitmap.GetAddressOf())), "glyph bitmap is created");
            ComPtr<ID2D1RenderTarget> target;
            Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(bitmap.Get(),
                D2D1::RenderTargetProperties(), target.GetAddressOf())), "glyph target is created");
            ComPtr<IDWriteTextFormat> format;
            Check(SUCCEEDED(write->CreateTextFormat(L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_SEMI_BOLD,
                DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, fontSize, L"",
                format.GetAddressOf())), "glyph format is created");
            format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_LEADING);
            format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
            ComPtr<ID2D1SolidColorBrush> brush;
            Check(SUCCEEDED(target->CreateSolidColorBrush(D2D1::ColorF(D2D1::ColorF::White),
                brush.GetAddressOf())), "glyph brush is created");
            target->BeginDraw();
            target->Clear(D2D1::ColorF(D2D1::ColorF::Black));
            target->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
            Check(widgetrail::guide::DrawPrompt(target.Get(), control,
                {4,4,4+size,4+size}, brush.Get()), "bundled font contains the controller glyph");
            target->Clear(D2D1::ColorF(D2D1::ColorF::Black));
            widgetrail::guide::DrawControl(target.Get(), format.Get(), control,
                {4,4,4+size,4+size}, brush.Get());
            Check(SUCCEEDED(target->EndDraw()), "glyph drawing completes");
            Check(format->GetTextAlignment() == DWRITE_TEXT_ALIGNMENT_LEADING &&
                format->GetParagraphAlignment() == DWRITE_PARAGRAPH_ALIGNMENT_CENTER,
                "glyph drawing restores shared text alignment");
            std::array<BYTE, side*side*4> pixels{};
            Check(SUCCEEDED(bitmap->CopyPixels(nullptr, side*4,
                static_cast<UINT>(pixels.size()), pixels.data())), "glyph pixels are readable");
            return pixels;
        };
        const auto left = render(widgetrail::guide::Control::LeftStick);
        const auto right = render(widgetrail::guide::Control::RightStick);
        Check(left != right, "left and right movement glyphs remain distinguishable");
        Check(right != render(widgetrail::guide::Control::R3), "stick movement differs from stick click");
        for (int value = static_cast<int>(widgetrail::guide::Control::A);
             value <= static_cast<int>(widgetrail::guide::Control::Guide); ++value) {
            const auto pixels = render(static_cast<widgetrail::guide::Control>(value));
            bool visible{};
            for (UINT y = 0; y < 48; ++y) for (UINT x = 0; x < 48; ++x) {
                if (!pixels[(y * 48 + x) * 4]) continue;
                visible = true;
                Check(x >= 3 && y >= 3 && x <= 5 + size && y <= 5 + size,
                    "controller glyph ink fits its allocated bounds");
            }
            Check(visible, "controller glyph paints visible ink at each guide size");
        }
    }
    }
    (void)widgetrail::guide::SetPlayStationControls(false);
}

void CheckFrame(
    ID2D1Factory* d2d,
    IWICImagingFactory* wic,
    const float scale) {
    constexpr UINT width = 144;
    constexpr UINT height = 96;
    ComPtr<IWICBitmap> bitmap;
    Check(SUCCEEDED(wic->CreateBitmap(
              width, height, GUID_WICPixelFormat32bppPBGRA,
              WICBitmapCacheOnLoad, bitmap.ReleaseAndGetAddressOf())),
          "WIC color-key frame is created");
    ComPtr<ID2D1RenderTarget> target;
    Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
              bitmap.Get(), D2D1::RenderTargetProperties(),
              target.ReleaseAndGetAddressOf())),
          "Direct2D color-key target is created");
    ComPtr<ID2D1SolidColorBrush> surface;
    Check(SUCCEEDED(target->CreateSolidColorBrush(
              D2D1::ColorF(0x2D / 255.0F, 0x35 / 255.0F, 0x48 / 255.0F, 1.0F),
              surface.ReleaseAndGetAddressOf())),
          "shell surface brush is created");

    target->BeginDraw();
    target->Clear(D2D1::ColorF(1 / 255.0F, 2 / 255.0F, 3 / 255.0F, 1.0F));
    target->SetTransform(D2D1::Matrix3x2F::Scale(scale, scale));
    target->SetAntialiasMode(D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
    widgetrail::shell::FillColorKeyRoundedRectangle(
        target.Get(),
        D2D1::RoundedRect(D2D1::RectF(8.0F, 8.0F, 88.0F, 56.0F), 12.0F, 12.0F),
        surface.Get());
    Check(target->GetAntialiasMode() == D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
          "outer chrome paint restores the caller antialias mode");
    target->SetTransform(D2D1::Matrix3x2F::Identity());
    Check(SUCCEEDED(target->EndDraw()), "color-key frame draw completes");

    WICRect area{0, 0, static_cast<INT>(width), static_cast<INT>(height)};
    ComPtr<IWICBitmapLock> lock;
    Check(SUCCEEDED(bitmap->Lock(
              &area, WICBitmapLockRead, lock.ReleaseAndGetAddressOf())),
          "color-key frame pixels are readable");
    UINT stride{};
    UINT byteCount{};
    BYTE* bytes{};
    Check(SUCCEEDED(lock->GetStride(&stride)) &&
              SUCCEEDED(lock->GetDataPointer(&byteCount, &bytes)) && bytes,
          "color-key pixel buffer is available");

    const auto pixel = [&](const UINT x, const UINT y) {
        const auto* value = bytes + y * stride + x * 4U;
        return std::array<BYTE, 4>{value[0], value[1], value[2], value[3]};
    };
    const auto key = pixel(0, 0);
    const auto inside = pixel(
        static_cast<UINT>(48.0F * scale),
        static_cast<UINT>(32.0F * scale));
    Check(key != inside, "rounded shell has distinct key and surface colors");
    Check(pixel(static_cast<UINT>(8.0F * scale),
                static_cast<UINT>(8.0F * scale)) == key,
          "rounded outer corner remains exactly color-key transparent");

    std::size_t blended{};
    std::size_t keyPixels{};
    std::size_t surfacePixels{};
    for (UINT y = 0; y < height; ++y) {
        for (UINT x = 0; x < width; ++x) {
            const auto value = pixel(x, y);
            if (value == key) ++keyPixels;
            else if (value == inside) ++surfacePixels;
            else ++blended;
        }
    }
    Check(keyPixels != 0 && surfacePixels != 0,
          "frame retains both transparent canvas and authored chrome");
    Check(blended == 0,
          "color-key boundary contains no opaque antialias fringe pixels");
}

void CheckPremultipliedFrame(
    ID2D1Factory* d2d,
    IWICImagingFactory* wic,
    const float scale) {
    constexpr UINT width = 144;
    constexpr UINT height = 96;
    ComPtr<IWICBitmap> bitmap;
    Check(SUCCEEDED(wic->CreateBitmap(
              width, height, GUID_WICPixelFormat32bppPBGRA,
              WICBitmapCacheOnLoad, bitmap.ReleaseAndGetAddressOf())),
          "WIC premultiplied-alpha frame is created");
    ComPtr<ID2D1RenderTarget> target;
    Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
              bitmap.Get(), D2D1::RenderTargetProperties(),
              target.ReleaseAndGetAddressOf())),
          "Direct2D premultiplied-alpha target is created");
    ComPtr<ID2D1SolidColorBrush> surface;
    Check(SUCCEEDED(target->CreateSolidColorBrush(
              D2D1::ColorF(0x2D / 255.0F, 0x35 / 255.0F, 0x48 / 255.0F, 1.0F),
              surface.ReleaseAndGetAddressOf())),
          "premultiplied shell surface brush is created");

    target->BeginDraw();
    target->Clear(D2D1::ColorF(0.0F, 0.0F, 0.0F, 0.0F));
    target->SetTransform(D2D1::Matrix3x2F::Scale(scale, scale));
    target->SetAntialiasMode(D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
    widgetrail::shell::FillColorKeyRoundedRectangle(
        target.Get(),
        D2D1::RoundedRect(D2D1::RectF(8.0F, 8.0F, 88.0F, 56.0F), 12.0F, 12.0F),
        surface.Get(), widgetrail::shell::OuterChromeBoundary::PremultipliedAlpha);
    Check(target->GetAntialiasMode() == D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
          "premultiplied chrome retains antialiasing");
    target->SetTransform(D2D1::Matrix3x2F::Identity());
    Check(SUCCEEDED(target->EndDraw()), "premultiplied frame draw completes");

    WICRect area{0, 0, static_cast<INT>(width), static_cast<INT>(height)};
    ComPtr<IWICBitmapLock> lock;
    Check(SUCCEEDED(bitmap->Lock(
              &area, WICBitmapLockRead, lock.ReleaseAndGetAddressOf())),
          "premultiplied pixel buffer is readable");
    UINT stride{};
    UINT byteCount{};
    BYTE* bytes{};
    Check(SUCCEEDED(lock->GetStride(&stride)) &&
              SUCCEEDED(lock->GetDataPointer(&byteCount, &bytes)) && bytes,
          "premultiplied pixel bytes are available");
    const auto alphaAt = [&](const UINT x, const UINT y) {
        return bytes[y * stride + x * 4U + 3U];
    };
    Check(alphaAt(0, 0) == 0,
          "unused composition client remains genuinely transparent");
    Check(alphaAt(static_cast<UINT>(48.0F * scale),
                  static_cast<UINT>(32.0F * scale)) == 255,
          "authored chrome remains fully opaque");
    std::size_t transparent{};
    std::size_t opaque{};
    std::size_t antialiased{};
    for (UINT y = 0; y < height; ++y) {
        for (UINT x = 0; x < width; ++x) {
            const BYTE alpha = alphaAt(x, y);
            if (alpha == 0) ++transparent;
            else if (alpha == 255) ++opaque;
            else ++antialiased;
        }
    }
    Check(transparent != 0 && opaque != 0 && antialiased != 0,
          "premultiplied frame retains transparent, authored, and rounded edge pixels");
}

void CheckRetainedTrayInvalidation() {
    widgetrail::shell::RetainedTrayState initial{
        420, 84, 7,
        {
            {{8, 8, 68, 68}, L"settings", true, false},
            {{76, 8, 136, 68}, L"network", false, false},
            {{144, 8, 204, 68}, L"audio", false, false},
        },
    };
    Check(widgetrail::shell::RequiresTrayRepaint(nullptr, initial),
          "first tray frame rebuilds its retained child surface");
    Check(!widgetrail::shell::RequiresTrayRepaint(&initial, initial),
          "content-only publication retains tray pixels");

    auto focused = initial;
    auto clockChanged = initial;
    clockChanged.statusKey = L"10:42 AM. Internet access";
    Check(widgetrail::shell::RequiresTrayRepaint(&initial, clockChanged),
        "time or connection changes invalidate only retained tray state");
    Check(!widgetrail::shell::RequiresTrayRepaint(&clockChanged, clockChanged),
        "unchanged status polling retains tray pixels");
    focused.items[0].focused = true;
    Check(widgetrail::shell::RequiresTrayRepaint(&initial, focused),
          "tray focus adds the selected-tile outline without changing selection");

    auto selected = initial;
    selected.items[0].selected = false;
    selected.items[1].selected = true;
    Check(widgetrail::shell::RequiresTrayRepaint(&initial, selected),
          "selection replaces the retained tray independently of focus");

    auto selectedFocused = selected;
    selectedFocused.items[1].focused = true;
    Check(widgetrail::shell::RequiresTrayRepaint(&selected, selectedFocused),
          "focused outline follows only the selected tile while tray owns focus");

    auto reordered = selectedFocused;
    std::swap(reordered.items[1].identity, reordered.items[2].identity);
    Check(widgetrail::shell::RequiresTrayRepaint(&selectedFocused, reordered),
          "reorder replaces the complete retained tray surface");

    auto provider = reordered;
    Check(!widgetrail::shell::RequiresTrayRepaint(&reordered, provider),
          "provider, slider, scroll, and motion retain unchanged tray pixels");

    auto packagePending = provider;
    packagePending.items[1].identity =
        L"network:connection:package:runtime-a:presentation-a:digest:mark:1:source:normalized:48x48:fallback";
    Check(widgetrail::shell::RequiresTrayRepaint(&provider, packagePending),
          "a package icon identity replaces the same semantic-glyph tray tile");
    auto packageReady = packagePending;
    packageReady.items[1].identity.replace(
        packageReady.items[1].identity.rfind(L"fallback"), 8, L"ready");
    Check(widgetrail::shell::RequiresTrayRepaint(&packagePending, packageReady),
          "asynchronous package icon readiness repaints without selection movement");
    auto packageReplacement = packageReady;
    packageReplacement.items[1].identity.replace(
        packageReplacement.items[1].identity.find(L"runtime-a"), 9, L"runtime-b");
    Check(widgetrail::shell::RequiresTrayRepaint(
              &packageReady, packageReplacement),
          "same-glyph package replacement changes exact retained icon authority");

    auto appearance = provider;
    ++appearance.appearanceRevision;
    Check(widgetrail::shell::RequiresTrayRepaint(&provider, appearance),
          "appearance revision rebuilds the tray child surface");
    Check(widgetrail::shell::RequiresImageReadyRepaint(false, true, true),
          "visible poster completion repaints content even while background advancement owns the wake");
    Check(!widgetrail::shell::RequiresImageReadyRepaint(false, true),
          "an independent background advance owns its background-only image-ready wake");
    for (const bool backgroundAdvanced : {false, true}) {
        Check(!widgetrail::shell::RequiresImageReadyRepaint(false, backgroundAdvanced, false, true),
              "known offscreen artwork completion cannot repaint content with or without a compositor background");
        Check(widgetrail::shell::RequiresImageReadyRepaint(false, backgroundAdvanced, true, true),
              "known visible artwork completion always schedules content paint");
        Check(widgetrail::shell::RequiresImageReadyRepaint(true, backgroundAdvanced, false, true),
              "package icon wake remains independent of widget image demand");
    }
    Check(widgetrail::shell::RequiresImageReadyRepaint(false, false) &&
              widgetrail::shell::RequiresImageReadyRepaint(true, false) &&
              widgetrail::shell::RequiresImageReadyRepaint(true, true),
          "a package icon completion always schedules retained tray comparison even when background advances");
}

struct FixedChromePointerTestContext final {
    HWND content{};
    widgetrail::OverlayState state{
        {}, {L"settings", L"network", L"audio"}};
    widgetrail::shell::TrayLayout layout;
    unsigned int activationCount{};
};

void ActivateFixedChromeTray(
    void* opaque, const float x, const float y) noexcept {
    auto& context = *static_cast<FixedChromePointerTestContext*>(opaque);
    const auto* hit = widgetrail::shell::HitTestTray(context.layout, x, y);
    if (!hit || hit->slot >= context.state.order().size()) return;
    ++context.activationCount;
    const auto& widget = context.state.order()[hit->slot];
    if (context.state.TrySelectTrayWidget(widget) &&
        context.state.selectedSlot() == hit->slot) {
        (void)context.state.Dispatch(widgetrail::Command::Activate);
    }
}

LRESULT CALLBACK FixedChromeTestWindowProc(
    HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == WM_NCCREATE) {
        const auto create = reinterpret_cast<CREATESTRUCTW*>(lParam);
        SetWindowLongPtrW(
            window, GWLP_USERDATA,
            reinterpret_cast<LONG_PTR>(create->lpCreateParams));
    } else if (message == WM_LBUTTONUP) {
        auto* context = reinterpret_cast<FixedChromePointerTestContext*>(
            GetWindowLongPtrW(window, GWLP_USERDATA));
        if (context) {
            (void)widgetrail::shell::RouteFixedChromePointerRelease(
                window, context->content, lParam, context,
                ActivateFixedChromeTray);
            return 0;
        }
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

bool RejectChromeTarget(
    widgetrail::OverlayCompositionSurface& surface,
    HWND,
    std::wstring& error) {
    Check(surface.available(),
          "content target exists when deterministic chrome-target failure occurs");
    error = L"deterministic second-target failure";
    return false;
}

void CheckFixedChromeWindowPolicy() {
    Check(widgetrail::shell::FixedChromeWindowStyle() == WS_POPUP,
          "fixed chrome is a popup endpoint");
    const auto expectedExStyle = WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP |
        WS_EX_NOACTIVATE | WS_EX_TOPMOST;
    Check(widgetrail::shell::FixedChromeWindowExStyle() == expectedExStyle,
          "fixed chrome is non-activating, topmost, and absent from task switching");

    const RECT oddWork{0, 0, 2185, 1400};
    const RECT evenWork{0, 0, 2186, 1400};
    const auto odd747 = widgetrail::shell::ComputeFixedChromeWindowBounds(oddWork, 747, 141);
    const auto odd748 = widgetrail::shell::ComputeFixedChromeWindowBounds(oddWork, 748, 141);
    const auto even747 = widgetrail::shell::ComputeFixedChromeWindowBounds(evenWork, 747, 141);
    const auto even748 = widgetrail::shell::ComputeFixedChromeWindowBounds(evenWork, 748, 141);
    Check(odd747.right - odd747.left == 747 && odd748.right - odd748.left == 748 &&
              even747.right - even747.left == 747 && even748.right - even748.left == 748,
          "fractional-DPI parity keeps the authored physical chrome width exact");
    Check(odd747.bottom == oddWork.bottom && odd748.bottom == oddWork.bottom &&
              even747.bottom == evenWork.bottom && even748.bottom == evenWork.bottom,
          "odd and even work areas retain one physical bottom anchor");
    const widgetrail::shell::FixedChromeSessionKey session{
        oddWork, 144, 1.0, 7, {L"settings", L"network"}};
    Check(widgetrail::shell::SameFixedChromeSession(session, session),
          "equal applied work-area and catalog authority reuses fixed chrome");
    auto changedWork = session;
    changedWork.workArea = {100, 0, 2285, 1400};
    auto changedCatalog = session;
    changedCatalog.catalogOrder.push_back(L"audio");
    auto changedScale = session;
    changedScale.interfaceScale = 1.25;
    auto changedAppearance = session;
    ++changedAppearance.appearanceRevision;
    Check(!widgetrail::shell::SameFixedChromeSession(session, changedWork) &&
              !widgetrail::shell::SameFixedChromeSession(session, changedCatalog) &&
              !widgetrail::shell::SameFixedChromeSession(session, changedScale) &&
              !widgetrail::shell::SameFixedChromeSession(session, changedAppearance),
          "work-area, catalog, scale, and appearance changes rebuild fixed chrome");

    const float monitorCapacity =
        widgetrail::shell::ComputeTrayCapacityWidth(1920.0F);
    const auto localCapacityLayout = widgetrail::shell::ComputeTrayLayout(
        monitorCapacity, 224.0F, 15, 14,
        widgetrail::shell::TrayBand{152.0F, 224.0F},
        widgetrail::shell::TrayWidthBasis::ExactCapacity);
    const auto selectedTile = localCapacityLayout
        ? std::find_if(
              localCapacityLayout->tiles.begin(),
              localCapacityLayout->tiles.end(),
              [](const auto& tile) { return tile.slot == 14; })
        : std::vector<widgetrail::shell::TrayTileLayout>::const_iterator{};
    Check(monitorCapacity == 1152.0F && localCapacityLayout &&
              !localCapacityLayout->previousOverflow &&
              !localCapacityLayout->nextOverflow &&
              localCapacityLayout->tiles.size() == 15 &&
              selectedTile != localCapacityLayout->tiles.end() &&
              selectedTile->bounds.width >= 44.0F &&
              selectedTile->bounds.width <= 64.0F &&
              std::abs(selectedTile->bounds.width - 61.86667F) < 0.001F &&
              std::all_of(
                  localCapacityLayout->tiles.begin(),
                  localCapacityLayout->tiles.end(),
                  [&](const auto& tile) {
                      return tile.slot < 15 &&
                          std::count_if(
                              localCapacityLayout->tiles.begin(),
                              localCapacityLayout->tiles.end(),
                              [&](const auto& candidate) {
                                  return candidate.slot == tile.slot;
                              }) == 1;
                  }) &&
              std::abs(selectedTile->bounds.x +
                       selectedTile->bounds.width * 0.5F -
                       monitorCapacity * 0.5F) < 0.01F,
          "fixed chrome consumes one sixty-percent capacity with a centered cyclic selection");

    const wchar_t className[] = L"WidgetRail.FixedChromePolicyTests";
    WNDCLASSW windowClass{};
    windowClass.lpfnWndProc = FixedChromeTestWindowProc;
    windowClass.hInstance = GetModuleHandleW(nullptr);
    windowClass.lpszClassName = className;
    Check(RegisterClassW(&windowClass) != 0, "fixed chrome test class registers");
    FixedChromePointerTestContext pointerContext;
    HWND content = CreateWindowExW(
        WS_EX_TOOLWINDOW | WS_EX_NOREDIRECTIONBITMAP, className, L"content", WS_POPUP,
        640, 600, 1000, 500, nullptr, nullptr, windowClass.hInstance, nullptr);
    pointerContext.content = content;
    HWND chrome = CreateWindowExW(
        widgetrail::shell::FixedChromeWindowExStyle(), className, L"chrome",
        widgetrail::shell::FixedChromeWindowStyle(), 0, 0, 1, 1,
        nullptr, nullptr, windowClass.hInstance, &pointerContext);
    Check(content && chrome, "content and chrome policy HWNDs are created");
    ShowWindow(content, SW_SHOWNOACTIVATE);
    const RECT fixed{720, 900, 1468, 1041};
    Check(widgetrail::shell::ApplyFixedChromeWindow(content, chrome, fixed, true),
          "production policy applies the owned chrome rectangle");
    Check(GetWindow(chrome, GW_OWNER) == content,
          "chrome HWND has the content session window as its Win32 owner");
    const auto chromeAboveContent = [&] {
        for (HWND candidate = GetWindow(content, GW_HWNDPREV);
             candidate; candidate = GetWindow(candidate, GW_HWNDPREV)) {
            if (candidate == chrome) return true;
        }
        return false;
    };
    Check(chromeAboveContent(),
          "owned chrome remains above its content owner in the real Z order");
    Check(IsWindowVisible(content) && IsWindowVisible(chrome),
          "owned content and chrome endpoints are visible as one session");
    const auto actualExStyle = static_cast<DWORD>(GetWindowLongPtrW(chrome, GWL_EXSTYLE));
    Check((actualExStyle & expectedExStyle) == expectedExStyle,
          "applied chrome retains nonactivation and taskbar policy");
    RECT actual{};
    Check(GetWindowRect(chrome, &actual) && EqualRect(&actual, &fixed),
          "applied chrome rectangle equals the external Win32 rectangle");

    constexpr std::array<SIZE, 8> contentExtents{{
        {592, 698}, {632, 878}, {880, 520}, {760, 440},
        {1180, 700}, {520, 360}, {978, 466}, {1280, 720},
    }};
    for (const auto direction : {1, -1}) {
        for (int step = 0; step < static_cast<int>(contentExtents.size()); ++step) {
            const int index = direction > 0 ? step :
                static_cast<int>(contentExtents.size()) - 1 - step;
            const auto extent = contentExtents[static_cast<std::size_t>(index)];
            Check(SetWindowPos(content, HWND_TOPMOST, 40 + step, 50 + step,
                               extent.cx, extent.cy, SWP_NOACTIVATE) != FALSE,
                  "content endpoint accepts a distinct authored extent");
            RECT retained{};
            Check(GetWindowRect(chrome, &retained) && EqualRect(&retained, &fixed) &&
                      chromeAboveContent(),
                  "all eight forward and reverse content extents retain chrome corners");
        }
    }
    ShowWindow(content, SW_HIDE);
    Check(widgetrail::shell::ApplyFixedChromeWindow(content, chrome, fixed, false) &&
              !IsWindowVisible(content) && !IsWindowVisible(chrome),
          "paired hide removes the chrome endpoint");
    ShowWindow(content, SW_SHOWNOACTIVATE);
    Check(widgetrail::shell::ApplyFixedChromeWindow(content, chrome, fixed, true) &&
              GetWindowRect(chrome, &actual) && EqualRect(&actual, &fixed),
          "reopen with equal inputs restores identical chrome corners");
    const RECT guide{850, 900, 1338, 950};
    const RECT tray{720, 950, 1468, 1041};
    Check(widgetrail::shell::IsFixedChromeHit({900, 920}, guide, tray) &&
              widgetrail::shell::IsFixedChromeHit({800, 1000}, guide, tray) &&
              !widgetrail::shell::IsFixedChromeHit({721, 901}, guide, tray),
          "chrome hit testing admits guide/tray and passes transparent gaps through");

    Check(SetWindowPos(
              content, HWND_TOPMOST, 640, 600, 1000, 500,
              SWP_NOACTIVATE) != FALSE,
          "content endpoint is placed for the applied chrome pointer route");
    Check(pointerContext.state.Dispatch(widgetrail::Command::ToggleOverlay),
          "existing overlay state activation owner becomes visible");
    const auto trayLayout = widgetrail::shell::ComputeTrayLayout(
        1000.0F, 500.0F, pointerContext.state.order().size(),
        pointerContext.state.selectedSlot(), widgetrail::shell::TrayBand{300.0F, 400.0F});
    Check(trayLayout.has_value() && trayLayout->tiles.size() == 3,
          "production tray layout exposes the target tile");
    pointerContext.layout = *trayLayout;
    const auto target = std::find_if(
        pointerContext.layout.tiles.begin(), pointerContext.layout.tiles.end(),
        [](const auto& tile) { return tile.slot == 1; });
    Check(target != pointerContext.layout.tiles.end(),
          "pointer fixture resolves the network catalog identity");
    const auto& targetTile = target->bounds;
    POINT release{
        static_cast<LONG>(targetTile.x + targetTile.width * 0.5F),
        static_cast<LONG>(targetTile.y + targetTile.height * 0.5F),
    };
    Check(ClientToScreen(content, &release) && ScreenToClient(chrome, &release),
          "target tile center maps into the applied chrome HWND");
    SendMessageW(
        chrome, WM_LBUTTONUP, 0,
        MAKELPARAM(static_cast<short>(release.x), static_cast<short>(release.y)));
    Check(pointerContext.activationCount == 1 &&
              pointerContext.state.selectedWidget() == L"network" &&
              pointerContext.state.activeWidget() == L"network" &&
              pointerContext.state.focusRegion() == widgetrail::FocusRegion::Widget,
          "real chrome HWND release maps once through the existing tray selection and activation owner");

    ComPtr<ID2D1Factory1> compositionFactory;
    Check(SUCCEEDED(D2D1CreateFactory(
              D2D1_FACTORY_TYPE_SINGLE_THREADED,
              IID_PPV_ARGS(compositionFactory.ReleaseAndGetAddressOf()))),
          "Direct2D factory is created for paired endpoint recovery");
    widgetrail::OverlayCompositionSurface composition;
    std::wstring compositionError;
    ShowWindow(chrome, SW_SHOWNOACTIVATE);
    Check(!widgetrail::shell::InitializeFixedChromeComposition(
              composition, content, chrome, compositionFactory.Get(),
              compositionError, RejectChromeTarget) &&
              !composition.available() && !IsWindowVisible(chrome) &&
              compositionError == L"deterministic second-target failure",
          "second-target initialization failure resets the half-session and hides chrome");
    Check(widgetrail::shell::InitializeFixedChromeComposition(
              composition, content, chrome, compositionFactory.Get(),
              compositionError),
          "real paired composition endpoints initialize for runtime recovery");
    widgetrail::OverlayCompositionSurface::Frame animatedFrame;
    Check(SUCCEEDED(composition.BeginFrame(400, 300, animatedFrame)),
          "resize fixture acquires a real compositor surface");
    animatedFrame.target->Clear(D2D1::ColorF(0.1F, 0.2F, 0.3F, 1.0F));
    Check(SUCCEEDED(composition.EndFrame(animatedFrame)), "destination frame is complete before motion");
    widgetrail::OverlayCompositionSurface::VisualPresentation motion{
        0.5F, 0.5F, 100.0F, 150.0F, 400.0F, 300.0F,
        140, 1.0F, 1.0F, 0.0F, 0.0F};
    widgetrail::OverlayCompositionSurface::CommitTiming motionTiming;
    std::array<widgetrail::OverlayCompositionSurface::Frame*, 1> motionFrames{&animatedFrame};
    Check(SUCCEEDED(composition.CommitFrames(motionFrames, true, motionTiming, &motion, nullptr, true)),
          "real compositor accepts resize and opacity together");
    Check(SUCCEEDED(composition.CommitShellZoom(0.88F, true, false)),
          "opening zoom can coexist with the resize transform");
    Check(SUCCEEDED(composition.SetShellZoomAnchor(800, 600)),
          "container changes update the zoom anchor without replacing its animation");
    const auto paintsBefore = composition.paintCounters().content;
    Check(SUCCEEDED(composition.SnapContentVisible()), "focus can settle compositor opacity without repaint");
    Check(composition.paintCounters().content == paintsBefore,
          "compositor opacity does not rasterize the widget again");
    Check(SUCCEEDED(composition.CommitShellZoom(1.0F, false, false)) &&
          SUCCEEDED(composition.CommitShellZoom(0.94F, true, false)),
          "closing and immediate reopening replace zoom from its current scale");
    Check(SUCCEEDED(composition.CommitShellZoom(1.0F, true, true)),
          "reduced motion snaps zoom independently of resize");
    Check(composition.paintCounters().content == paintsBefore,
          "opening and closing zoom do not repaint content");
    widgetrail::OverlayCompositionSurface::Frame repaint;
    Check(SUCCEEDED(composition.BeginFrame(400, 300, repaint)), "same-size repaint is admitted during motion");
    repaint.target->Clear(D2D1::ColorF(0.3F, 0.2F, 0.1F, 1.0F));
    Check(SUCCEEDED(composition.EndFrame(repaint)) &&
          SUCCEEDED(composition.CommitFrame(repaint, true, motionTiming)),
          "content-only commit preserves an independently owned animated transform");
    auto invalidMotion = motion;
    invalidMotion.durationMilliseconds = 201;
    Check(FAILED(composition.CommitPresentation(invalidMotion, motionTiming)),
          "compositor rejects unbounded animation durations");
    motion.durationMilliseconds = 0;
    motion.scaleX = motion.scaleY = 1.0F;
    motion.offsetX = motion.offsetY = 0.0F;
    Check(SUCCEEDED(composition.CommitPresentation(motion, motionTiming)),
          "reduced motion settles to a static transform");
    ShowWindow(chrome, SW_SHOWNOACTIVATE);
    widgetrail::shell::ResetFixedChromeComposition(composition, chrome);
    Check(!composition.available() && !IsWindowVisible(chrome),
          "runtime composition or device failure resets and hides both endpoints");

    // Exercise failure -> new device -> full content/chrome frames against
    // real HWNDs. Keep an old-device reference to verify actual replacement.
    for (int cycle = 0; cycle != 3; ++cycle) {
        Check(!widgetrail::shell::SetContentCompositionMode(content, false) &&
                  (GetWindowLongPtrW(content, GWL_EXSTYLE) & WS_EX_LAYERED) == 0,
              "no-redirection HWND rejects unsupported fallback without poisoning its style");
        const auto originalStyles = GetWindowLongPtrW(content, GWL_EXSTYLE);
        SetWindowLongPtrW(content, GWL_EXSTYLE, originalStyles | WS_EX_LAYERED);
        Check(SetLayeredWindowAttributes(content, RGB(1, 2, 3), 0,
                  LWA_ALPHA | LWA_COLORKEY) != FALSE,
              "fixture reproduces alpha-zero layered state from the old fallback");
        Check(widgetrail::shell::SetContentCompositionMode(content, true),
              "recovery preserves the window's original composition mode");
        Check(!widgetrail::shell::InitializeFixedChromeComposition(
                  composition, content, chrome, compositionFactory.Get(),
                  compositionError, RejectChromeTarget) && !composition.available(),
              "failed recovery releases the partially recreated device");
        Check(widgetrail::shell::InitializeFixedChromeComposition(
                  composition, content, chrome, compositionFactory.Get(), compositionError),
              "paired endpoints recover after an initialization failure");
        ComPtr<ID3D11Device> oldDevice = composition.graphicsDevice();
        widgetrail::shell::ResetFixedChromeComposition(composition, chrome);
        Check(widgetrail::shell::InitializeFixedChromeComposition(
                  composition, content, chrome, compositionFactory.Get(), compositionError,
                  nullptr, cycle == 2) &&
                  composition.graphicsDevice() != oldDevice.Get(),
              "recovery creates a new device rather than reusing the invalid one");
        std::array<widgetrail::OverlayCompositionSurface::Frame, 3> restored;
        std::array<widgetrail::OverlayCompositionSurface::Frame*, 3> restoredPointers{};
        using Layer = widgetrail::OverlayCompositionSurface::Layer;
        const std::array layers{Layer::Content, Layer::Guide, Layer::Tray};
        for (std::size_t i = 0; i != layers.size(); ++i) {
            Check(SUCCEEDED(composition.BeginFrame(layers[i], 200, 100, 0, 0,
                      nullptr, restored[i])), "recreated device admits each presentation layer");
            restored[i].target->Clear(D2D1::ColorF(0.3F, 0.6F, 0.9F, 1.0F));
            Check(SUCCEEDED(composition.EndFrame(restored[i])),
                  "recreated presentation layer completes drawing");
            restoredPointers[i] = &restored[i];
        }
        Check(SUCCEEDED(composition.CommitFrames(restoredPointers, true, motionTiming)),
              "recovered device commits widget, guide and tray together");
        Check(widgetrail::shell::ApplyFixedChromeWindow(content, chrome, fixed, true) &&
                  IsWindowVisible(chrome), "recovered chrome is visible at its restored bounds");
        widgetrail::shell::ResetFixedChromeComposition(composition, chrome);
    }

    ShowWindow(content, SW_HIDE);
    Check(widgetrail::shell::SetContentCompositionMode(content, true) &&
              widgetrail::shell::InitializeFixedChromeComposition(
                  composition, content, chrome, compositionFactory.Get(), compositionError) &&
              !IsWindowVisible(content) && !IsWindowVisible(chrome),
          "hidden device recovery never opens either endpoint");
    widgetrail::shell::ResetFixedChromeComposition(composition, chrome);

    HWND legacy = CreateWindowExW(WS_EX_TOOLWINDOW, className, L"legacy", WS_POPUP,
        0, 0, 10, 10, nullptr, nullptr, windowClass.hInstance, nullptr);
    Check(legacy && widgetrail::shell::SetContentCompositionMode(legacy, false) &&
              SetLayeredWindowAttributes(legacy, RGB(1, 2, 3), 0, LWA_ALPHA | LWA_COLORKEY) &&
              widgetrail::shell::SetContentCompositionMode(legacy, true) &&
              (GetWindowLongPtrW(legacy, GWL_EXSTYLE) & WS_EX_LAYERED) == 0,
          "redirected legacy windows can discard stale layered alpha on recovery");
    DestroyWindow(legacy);

    DestroyWindow(content);
    Check(!IsWindow(content) && !IsWindow(chrome),
          "normal owner close destroys the fixed chrome endpoint");
    UnregisterClassW(className, windowClass.hInstance);
}

void CheckWidgetAnimationPolicies() {
    using namespace widgetrail::animation;
    const Rect bounds{20, 30, 200, 120};
    for (const Rect anchor : {Rect{0, 50, 10, 10}, Rect{230, 50, 10, 10},
                              Rect{80, 0, 10, 10}, Rect{80, 160, 10, 10}}) {
        const auto popup = PopupEnter(bounds, bounds, anchor);
        Check(popup.milliseconds == 160 && popup.from.opacity == 1 && popup.to.opacity == 1,
            "popup entrance has bounded duration and constant opacity");
        for (int tick = 0; tick <= 160; ++tick) {
            const auto pose = Sample(popup.Start(0, 1000), tick);
            Check(pose.bounds.x >= bounds.x - .001F && pose.bounds.y >= bounds.y - .001F &&
                  pose.bounds.x + pose.bounds.width <= bounds.x + bounds.width + .001F &&
                  pose.bounds.y + pose.bounds.height <= bounds.y + bounds.height + .001F,
                "anchored popup entrance stays within its final placement");
        }
        Check(popup.Start(0, 1000, .5).duration == 320 && popup.Start(0, 1000, 2).duration == 80,
            "popup entrance follows the common animation speed limits");
    }
    const auto plan = Section(SectionStyle::Paging, bounds, bounds, bounds, 1);
    Check(plan.incoming.milliseconds == 208 && plan.outgoing.milliseconds == 208,
          "paging has one shared duration");
    const auto incoming = plan.incoming.Start(100, 1000), outgoing = plan.outgoing.Start(100, 1000);
    for (int tick = 0; tick <= 208; ++tick) {
        const auto front = Sample(incoming, 100 + tick), back = Sample(outgoing, 100 + tick);
        Check(front.opacity == 1 && back.opacity == 1, "paging never fades either page");
        Check(std::abs(back.clip.y + back.clip.height - front.bounds.y) < .001F,
              "reveal boundary follows incoming page without translucent overlap");
        Check(back.bounds.width <= bounds.width && back.bounds.width >= bounds.width * .96F - .001F,
              "outgoing page recedes only slightly");
        const auto matrix = Map(bounds, back.bounds);
        const float screenX = 95 * matrix.scaleX + matrix.offsetX;
        Check(std::abs((screenX - matrix.offsetX) / matrix.scaleX - 95) < .001F,
              "input inverse matches the scaled interruption pose");
    }
    for (const auto curve : {Smooth, EaseOut}) {
        const auto coefficients = curve.Coefficients(12, 91, .26);
        for (int i = 0; i <= 100; ++i) {
            const double t = i / 100.0, seconds = t * .26;
            const auto emitted =
                ((coefficients[3] * seconds + coefficients[2]) * seconds + coefficients[1]) * seconds +
                coefficients[0];
            Check(std::abs(emitted - (12 + 79 * curve.Evaluate(t))) < .001,
                  "DComp polynomial and CPU sample share identical easing");
        }
    }
    for (int direction : {-1, 1}) {
        const auto slide = Section(SectionStyle::Slide, bounds, bounds, bounds, direction);
        Check(slide.incoming.from.opacity == 1 && slide.incoming.to.opacity == 1 &&
                  slide.outgoing.from.opacity == 1 && slide.outgoing.to.opacity == 1,
              "slide preset does not retain the rejected crossfade");
        Check(slide.incoming.from.bounds.x == bounds.x + direction * bounds.width &&
                  slide.outgoing.to.bounds.x == bounds.x - direction * bounds.width,
              "slide travels a complete page in navigation order");
    }
    Check(Section(SectionStyle::None, bounds, bounds, bounds, 1).incoming.milliseconds == 0,
          "none preset has no motion");
    for (const auto preset : SectionPresets)
        Check(ParseSectionStyle(preset.id) == preset.style && SectionStyleId(preset.style) == preset.id,
              "preset settings IDs round-trip independently of enum ordinals");
    Check(ParseSectionStyle(L"future-unknown") == SectionStyle::Slide,
          "unknown settings ID has a safe default");
    Check(ModalEnter(ModalStyle::None, bounds, bounds, 0, false).milliseconds == 0,
          "modal preference independently disables modal motion");
    Check(Options{}.section == SectionStyle::Slide && SectionPresets.front().style == SectionStyle::Slide,
        "Slide is the default and first preset");
    Check(Options{}.modal == ModalStyle::Zoom, "Zoom is the default dialog preset");
    for (const auto speed : {.5, 1.0, 2.0}) {
        const auto page = Section(SectionStyle::Slide,bounds,bounds,bounds,1).incoming.Start(0,1000,speed);
        const auto modal = ModalEnter(ModalStyle::Lift,bounds,bounds,0,false).Start(0,1000,speed);
        Check(page.duration == static_cast<std::int64_t>(208/speed) && modal.duration == static_cast<std::int64_t>(260/speed),
            "bounded speed controls page and gentler modal timing consistently");
        Check(std::abs(Sample(modal,modal.duration/2).opacity-.5F)<.001F,
            "modal uses gentle acceleration instead of the old fast initial fade");
    }
    for (int direction : {-1, 1}) {
        const auto vertical = Section(SectionStyle::VerticalSlide, bounds, bounds, bounds, direction);
        Check(vertical.incoming.from.bounds.y == bounds.y + direction * bounds.height &&
              vertical.outgoing.to.bounds.y == bounds.y - direction * bounds.height,
              "vertical slide follows navigation direction across a complete page");
        for (const auto style : {SectionStyle::Reveal, SectionStyle::CoverSlide}) {
            const auto reveal = Section(style, bounds, bounds, bounds, direction);
            const auto frontMotion = reveal.incoming.Start(0, 1000);
            const auto backMotion = reveal.outgoing.Start(0, 1000);
            for (int tick = 0; tick <= 208; ++tick) {
                const auto front = Sample(frontMotion, tick), back = Sample(backMotion, tick);
                const auto edge = direction > 0 ? back.clip.x + back.clip.width : front.clip.x + front.clip.width;
                const auto other = direction > 0 ? front.clip.x : back.clip.x;
                Check(std::abs(edge - other) < .001F &&
                      std::abs(front.clip.width + back.clip.width - bounds.width) < .001F,
                      "reveal and cover clips meet without gaps or translucent overlap");
                Check(front.opacity == 1 && back.opacity == 1,
                      "new section presets keep brightness constant");
                Check(back.bounds.x == bounds.x && back.bounds.y == bounds.y,
                      "cover and reveal leave the outgoing page stationary");
                if (style == SectionStyle::Reveal)
                    Check(front.bounds.x == bounds.x, "reveal clips instead of moving content");
                else
                    Check(std::abs((direction > 0 ? front.bounds.x : front.bounds.x + front.bounds.width) - edge) < .001F,
                          "cover reveal edge tracks the moving incoming page");
            }
        }
    }
    const auto zoom = ModalEnter(ModalStyle::Zoom, bounds, bounds, 0, false);
    Check(std::abs(zoom.from.bounds.width - bounds.width * .9F) < .001F &&
          std::abs(zoom.from.bounds.x + zoom.from.bounds.width / 2 - (bounds.x + bounds.width / 2)) < .001F,
          "modal zoom begins at a noticeable 90 percent with a centered horizontal anchor");
    const auto zoomScrim = ModalEnter(ModalStyle::Zoom, bounds, bounds, 0, true);
    Check(zoomScrim.from.bounds.width == bounds.width && zoomScrim.from.bounds.y == bounds.y,
          "modal scrim fades without scaling or moving");
    const auto zoomExit = ModalExit(ModalStyle::Zoom, bounds, bounds, 1, false);
    Check(zoomExit.to.bounds.width == zoom.from.bounds.width && zoomExit.to.bounds.y == zoom.from.bounds.y,
          "modal zoom exit returns to the entrance pose");
    const Pose oldLabel{{5, 10, 80, 40}, 1, bounds};
    const auto label = Layout(SectionStyle::Paging, bounds, bounds, oldLabel, false);
    const auto selection = Layout(SectionStyle::Paging, bounds, bounds, oldLabel, true);
    Check(label.from.bounds.x == oldLabel.bounds.x && label.from.bounds.width == bounds.width &&
              selection.from.bounds.width == oldLabel.bounds.width,
          "layout motion moves text without scaling it while selection surfaces can resize");
    const FocusTarget first{L"first/item-a", L"library/grid", {20, 30, 60, 40}, {0, 0, 300, 300}};
    auto recycled = first; recycled.key = L"recycled";
    Check(HasStableFocusTarget(first, std::vector<FocusTarget>{first}), "unchanged focus source can fade out");
    Check(!HasStableFocusTarget(first, std::vector<FocusTarget>{recycled}), "recycled cursor source cannot retain focus decoration");
    Check(Options{}.focus == FocusStyle::Fade && ParseFocusStyle(L"unknown") == FocusStyle::Fade &&
        ParseFocusStyle(L"settle") == FocusStyle::Settle && ParseFocusStyle(L"fade") == FocusStyle::Fade &&
        ParseFocusStyle(L"none") == FocusStyle::None && ParseFocusStyle(L"slide") == FocusStyle::Fade,
        "focus defaults and persisted preset IDs are stable");
    const Rect settleBounds{10, 10, 100, 44}, settleClip{0, 0, 300, 200};
    const auto settle = FocusSettle(settleBounds, settleClip).Start(0, 1000);
    Check(settle.duration == 220 && Sample(settle, 0).opacity == .65F &&
        SameFocusRect(Sample(settle, 0).bounds, {16, 16, 88, 32}) &&
        SameFocusRect(Sample(settle, 220).bounds, settleBounds), "settle expands only the outline from a fixed DIP inset");
    const auto sampled = Sample(settle, 60);
    const auto resumed = FocusSettle(settleBounds, settleClip, sampled).Start(80, 1000);
    Check(SameFocusRect(Sample(resumed, 80).bounds, sampled.bounds) && Sample(resumed, 80).opacity == sampled.opacity,
        "interrupted settle resumes current geometry and alpha without restarting its initial pose");
    const Rect tightBounds{0, 0, 100, 44};
    Check(SameFocusRect(FocusSettle(tightBounds, tightBounds).from.bounds, {6, 6, 88, 32}) &&
        SameFocusRect(FocusSettleBounds({10, 1, 100, 44}), {16, 7, 88, 32}),
        "tight row clipping cannot suppress settle movement or move its center");
    Check(FocusSettle(settleBounds, settleClip).Start(0, 1000, 2).duration == 110 &&
        FocusDuration(FocusStyle::Settle) == 220 && FocusDuration(FocusStyle::None) == 0,
        "settle and its background share the configured timeline");
    const auto fade = FocusFade(first.bounds, first.clip, 0, 1).Start(0, 1000);
    Check(fade.duration == 240 && Sample(fade, 120).opacity == .5F &&
        SameFocusRect(Sample(fade, 120).bounds, first.bounds), "focus fade changes only alpha at fixed bounds");
    const auto reversed = FocusFade(first.bounds, first.clip, Sample(fade, 30).opacity, 0).Start(30, 1000, 2);
    Check(reversed.duration == 120 && Sample(reversed, 30).opacity == Sample(fade, 30).opacity &&
        Sample(reversed, 150).opacity == 0, "rapid fade reversal continues from current opacity at configured speed");

}

#include "LoadingIndicatorCompositionTests.inl"

void CheckWidgetCompositorPixels(const bool popupOnly = false, const bool orderingOnly = false) {
    using namespace widgetrail;
    Check(SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2)!=0,
        "pixel fixture uses physical screen coordinates");
    const wchar_t *name = L"WidgetRail.CompositorMotionPixelTest";
    WNDCLASSW wc{};
    wc.lpfnWndProc = DefWindowProcW;
    wc.hInstance = GetModuleHandleW(nullptr);
    wc.lpszClassName = name;
    Check(RegisterClassW(&wc) != 0, "compositor pixel class");
    HWND window = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP, name,
                                  L"Widget motion test", WS_POPUP, 40, 40, 128, 128, nullptr, nullptr,
                                  wc.hInstance, nullptr);
    Check(window != nullptr, "compositor pixel window");
    ComPtr<ID2D1Factory1> factory;
    Check(SUCCEEDED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, factory.GetAddressOf())),
          "composition factory");
    OverlayCompositionSurface surface;
    std::wstring error;
    Check(surface.Initialize(window, factory.Get(), error), "widget compositor initializes");
    const auto makeScene = [](const wchar_t *key, D2D1_COLOR_F color) {
        auto scene = std::make_shared<WidgetCompositionScene>();
        scene->authority = L"motion.test";
        scene->animations.section = animation::SectionStyle::Paging;
        scene->viewport = {0, 0, 128, 128};
        WidgetCompositionNode background;
        background.id = L"background";
        background.bounds = background.clip = scene->viewport;
        background.solid = D2D1::ColorF(D2D1::ColorF::Black);
        scene->nodes.push_back(background);
        WidgetCompositionNode group;
        group.id = L"section";
        group.kind = WidgetCompositionKind::Content;
        group.clock = L"tabs";
        group.key = key;
        group.order = key[0] == L'h' ? 0 : 1;
        group.bounds = group.clip = {16, 16, 96, 96};
        scene->nodes.push_back(group);
        WidgetCompositionNode pixels;
        pixels.id = L"section.pixels";
        pixels.parent = group.id;
        pixels.bounds = group.bounds;
        pixels.clip = group.clip;
        pixels.solid = color;
        scene->nodes.push_back(pixels);
        return scene;
    };
    const auto commit = [&](std::shared_ptr<const WidgetCompositionScene> scene) {
        OverlayCompositionSurface::Frame frame;
        Check(SUCCEEDED(surface.BeginFrame(128, 128, frame)), "motion frame begins");
        frame.target->Clear(D2D1::ColorF(0, 0));
        frame.widgetScene = std::move(scene);
        Check(SUCCEEDED(surface.EndFrame(frame)), "motion frame ends");
        OverlayCompositionSurface::CommitTiming timing;
        Check(SUCCEEDED(surface.CommitFrame(frame, true, timing)), "motion scene commits");
    };
    const auto pixel = [&](int x = 64, int y = 64, HWND sampleWindow = nullptr) {
        POINT position{x, y};
        ClientToScreen(sampleWindow ? sampleWindow : window, &position);
        const auto desktop = GetDC(nullptr);
        const auto value = GetPixel(desktop, position.x, position.y);
        ReleaseDC(nullptr, desktop);
        Check(value != CLR_INVALID, "composition pixel is readable");
        return value;
    };
    commit(makeScene(L"home", D2D1::ColorF(D2D1::ColorF::Red)));
    ShowWindow(window,SW_SHOWNOACTIVATE);
    Check(SetWindowPos(window, HWND_TOPMOST, 40, 40, 128, 128, SWP_NOACTIVATE | SWP_SHOWWINDOW)!=0,
        "pixel test window is shown");
    // Initialize the shown HWND/composition connection before starting the
    // measured animation. The timed interval below still pumps no messages.
    const auto startupUntil = GetTickCount64() + 225;
    while (GetTickCount64() < startupUntil) {
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&message); DispatchMessageW(&message);
        }
        Sleep(1);
    }
    DwmFlush();
    Sleep(50);
    DwmFlush();
    Check(GetRValue(pixel()) > 220, "initial compositor section is red");
    if (!popupOnly) {
    if (!orderingOnly) {
    commit(makeScene(L"library", D2D1::ColorF(D2D1::ColorF::Lime)));
    const auto movingInput = surface.MapWidgetCompositionInput({64, 64});
    Check(!std::isfinite(movingInput.x) || (movingInput.x == 64 && movingInput.y <= 64),
          "outgoing page has no input while incoming page enters from below");
    DwmFlush();
    const auto initial = pixel();
    const auto paints = surface.paintCounters().content;
    const auto counters = surface.widgetCompositionCounters();
    // Deliberately no message pumping, painting, scene updates or animation
    // samples on this thread. DWM must advance the submitted visual curves.
    Sleep(160);
    DwmFlush();
    const auto advanced = pixel();
    Check(GetGValue(advanced) > GetGValue(initial) + 10,
          "DWM advances widget pixels while the application thread sleeps");
    const auto enteringInput = surface.MapWidgetCompositionInput({64, 64});
    Check(enteringInput.x == 64 && enteringInput.y >= 16 && enteringInput.y <= 64,
          "incoming paging input follows the same compositor transform");
    Check(surface.paintCounters().content == paints &&
              surface.widgetCompositionCounters().rasterUploads == counters.rasterUploads,
          "motion produces zero application raster uploads");
    commit(makeScene(L"library", D2D1::ColorF(D2D1::ColorF::Blue)));
    Check(surface.widgetCompositionCounters().animationStarts == counters.animationStarts,
          "content update does not restart compositor motion");
    Sleep(110);
    DwmFlush();
    Check(GetBValue(pixel()) > 220, "updated section reaches its original animation endpoint");
    Check(SUCCEEDED(surface.AdvanceWidgetComposition()), "completed visuals retire without drawing");
    commit(makeScene(L"home", D2D1::ColorF(0, 1, 0, .5F)));
    Sleep(160);
    DwmFlush();
    const auto transparentPixel = pixel();
    Check(GetBValue(transparentPixel) < 10 && GetGValue(transparentPixel) > 110 &&
              GetGValue(transparentPixel) < 145,
          "transparent incoming page reveals its background, never the old page");
    const auto captures = surface.widgetCompositionCounters().interruptionCaptures;
    commit(makeScene(L"library", D2D1::ColorF(D2D1::ColorF::Blue)));
    Check(surface.widgetCompositionCounters().interruptionCaptures == captures + 1,
          "rapid paging captures the displayed scale and reveal once");
    DwmFlush();
    Check(GetBValue(pixel()) < 10 && GetGValue(pixel()) > 110,
          "interruption preserves the already revealed transparent page without old-color flash");
    Sleep(290);
    DwmFlush();
    Check(GetBValue(pixel()) > 220, "interrupted paging converges to the latest page");
    }
    const auto modalScene = [&] {
        auto scene = makeScene(L"library", D2D1::ColorF(D2D1::ColorF::Blue));
        // Playnite replaces its leading background raster when opening details,
        // while retaining later siblings. A new first child must be inserted
        // behind those retained visuals, not just ordered correctly on a cold tree.
        scene->nodes.front().id = L"modal.background";
        WidgetCompositionNode scrim;
        scrim.id = L"modal.scrim";
        scrim.kind = WidgetCompositionKind::Scrim;
        scrim.clock = L"modal";
        scrim.key = L"dialog";
        scrim.bounds = scrim.clip = scene->viewport;
        scene->nodes.push_back(scrim);
        WidgetCompositionNode shade;
        shade.id = L"scrim.pixels";
        shade.parent = scrim.id;
        shade.bounds = shade.clip = scene->viewport;
        shade.solid = D2D1::ColorF(0, .6F);
        scene->nodes.push_back(shade);
        WidgetCompositionNode panel;
        panel.id = L"modal.panel";
        panel.kind = WidgetCompositionKind::Modal;
        panel.clock = L"modal";
        panel.key = L"dialog";
        panel.bounds = {24, 24, 80, 80};
        panel.clip = scene->viewport;
        scene->nodes.push_back(panel);
        WidgetCompositionNode panelPixels;
        panelPixels.id = L"modal.pixels";
        panelPixels.parent = panel.id;
        panelPixels.bounds = panel.bounds;
        panelPixels.clip = panel.clip;
        panelPixels.solid = D2D1::ColorF(D2D1::ColorF::Red);
        scene->nodes.push_back(panelPixels);
        return scene;
    };
    commit(modalScene());
    Sleep(290);
    DwmFlush();
    Check(GetRValue(pixel()) > 220, "modal pixels are above their backdrop");
    const auto stableModal = surface.widgetCompositionCounters();
    commit(modalScene());
    DwmFlush();
    Check(GetRValue(pixel()) > 220, "unchanged modal retains correct ordering after background replacement");
    Check(surface.widgetCompositionCounters().visualAdds == stableModal.visualAdds &&
              surface.widgetCompositionCounters().visualRemoves == stableModal.visualRemoves,
          "stable modal ordering requires no visual-tree mutations");
    const auto outsideModal = surface.MapWidgetCompositionInput({18, 18});
    Check(!std::isfinite(outsideModal.x), "modal backdrop never maps input into the parent content");
    commit(makeScene(L"library", D2D1::ColorF(D2D1::ColorF::Blue)));
    const auto dismissedInput = surface.MapWidgetCompositionInput({64, 64});
    Check(dismissedInput.x == 64 && dismissedInput.y == 64,
          "dismissed modal pixels immediately stop contributing input transforms");
    DwmFlush();
    Check(GetRValue(pixel()) > 150, "modal exit preserves panel-over-backdrop ordering");
    const auto closingPaints = surface.paintCounters().content;
    Sleep(55);
    DwmFlush();
    Check(surface.paintCounters().content == closingPaints, "modal exit needs no UI-thread paint");
    commit(modalScene());
    Sleep(290);
    DwmFlush();
    Check(GetRValue(pixel()) > 220, "an interrupted modal exit reopens correctly");
    if (orderingOnly) {
        DestroyWindow(window);
        std::cout << "Widget modal ordering pixel checks passed.\n";
        return;
    }
    auto reduced = makeScene(L"home", D2D1::ColorF(D2D1::ColorF::Lime));
    reduced->reducedMotion = true;
    commit(reduced);
    DwmFlush();
    Check(GetGValue(pixel()) > 220, "reduced motion immediately removes outgoing and modal pixels");
    auto scaled = makeScene(L"library", D2D1::ColorF(D2D1::ColorF::Blue));
    scaled->scale = 1.25F;
    const auto starts = surface.widgetCompositionCounters().animationStarts;
    commit(scaled);
    Check(surface.widgetCompositionCounters().animationStarts == starts,
          "scale change cannot resume stale widget animations");
    commit(makeScene(L"home", D2D1::ColorF(D2D1::ColorF::Red)));
    commit(makeScene(L"library", D2D1::ColorF(D2D1::ColorF::Lime)));
    const auto preferenceStarts = surface.widgetCompositionCounters().animationStarts;
    auto slide = makeScene(L"library", D2D1::ColorF(D2D1::ColorF::Lime));
    slide->animations.section = animation::SectionStyle::Slide;
    commit(slide);
    DwmFlush();
    Check(surface.widgetCompositionCounters().animationStarts == preferenceStarts && GetGValue(pixel()) > 220,
          "changing animation preference during paging settles the current page");
    auto slidingBack = makeScene(L"home", D2D1::ColorF(D2D1::ColorF::Red));
    slidingBack->animations.section = animation::SectionStyle::Slide;
    commit(slidingBack);
    Sleep(160);
    DwmFlush();
    Check(GetRValue(pixel()) > 220 && GetGValue(pixel()) < 10,
          "slide preset uses opaque pages instead of the rejected crossfade");
    auto disabled = makeScene(L"library", D2D1::ColorF(D2D1::ColorF::Blue));
    disabled->animations.section = animation::SectionStyle::None;
    const auto disableStarts = surface.widgetCompositionCounters().animationStarts;
    commit(disabled);
    DwmFlush();
    Check(GetBValue(pixel()) > 220 && surface.widgetCompositionCounters().animationStarts == disableStarts,
          "None preset immediately retires a running transition");
    commit(makeScene(L"home", D2D1::ColorF(D2D1::ColorF::Red)));
    commit(makeScene(L"library", D2D1::ColorF(D2D1::ColorF::Lime)));
    Sleep(160);
    auto resizedContent = makeScene(L"library", D2D1::ColorF(D2D1::ColorF::Blue));
    resizedContent->nodes[1].bounds = {16, 12, 96, 100};
    resizedContent->nodes[2].bounds = resizedContent->nodes[1].bounds;
    commit(resizedContent);
    Sleep(110);
    DwmFlush();
    Check(GetBValue(pixel()) > 220, "same-section geometry update preserves the original paging end time");
    auto scaledHome = makeScene(L"home",D2D1::ColorF(D2D1::ColorF::Red));
    scaledHome->scale = 1.25F;
    commit(scaledHome);
    DwmFlush();
    Check(GetRValue(pixel(124,30))>220,"scaled live page fills its physical extent before capture");
    auto scaledLibrary = makeScene(L"library",D2D1::ColorF(D2D1::ColorF::Lime));
    scaledLibrary->scale = 1.25F;
    commit(scaledLibrary); Sleep(70); DwmFlush();
    Check(GetRValue(pixel(124,30))>220,
        "outgoing capture fills its physical extent at enlarged overlay scale");
    Sleep(210); DwmFlush();
    Check(GetGValue(pixel())>220,"scaled paging settles to the incoming page");
    auto slowHome = makeScene(L"home",D2D1::ColorF(D2D1::ColorF::Red));
    slowHome->animations = {animation::SectionStyle::Slide,animation::ModalStyle::Lift,.5};
    commit(slowHome);
    auto slowLibrary = makeScene(L"library",D2D1::ColorF(D2D1::ColorF::Lime));
    slowLibrary->animations = slowHome->animations;
    commit(slowLibrary);
    const auto speedPaints = surface.paintCounters().content;
    // Leave headroom before the 208 ms midpoint for compositor scheduling.
    Sleep(120); DwmFlush();
    Check(GetRValue(pixel())>220,"half-speed slide is still on its outgoing half after 120ms");
    Sleep(390); DwmFlush();
    Check(GetGValue(pixel())>220 && surface.paintCounters().content==speedPaints,
        "half-speed motion reaches its endpoint without UI-thread painting");
    for (const auto style : {animation::SectionStyle::VerticalSlide, animation::SectionStyle::Reveal,
                             animation::SectionStyle::CoverSlide}) {
        auto home = makeScene(L"home", D2D1::ColorF(D2D1::ColorF::Red));
        home->animations = {style, animation::ModalStyle::Zoom, .5};
        commit(home);
        Sleep(450); DwmFlush();
        for (bool reverse : {false, true}) {
            auto page = makeScene(reverse ? L"home" : L"library", D2D1::ColorF(0, 1, 0, .5F));
            page->animations = home->animations;
            commit(page);
            const auto pagePaints = surface.paintCounters().content;
            const auto uploads = surface.widgetCompositionCounters().rasterUploads;
            Sleep(280); DwmFlush();
            const auto revealed = pixel();
            Check(GetRValue(revealed) < 10 && GetGValue(revealed) > 110 && GetGValue(revealed) < 145,
                  "new presets reveal transparent incoming content without old-page bleed in both directions");
            const auto mapped = surface.MapWidgetCompositionInput({64, 64});
            Check(std::isfinite(mapped.x) && std::isfinite(mapped.y),
                  "revealed page accepts input through its compositor transform");
            const auto priorCaptures = surface.widgetCompositionCounters().interruptionCaptures;
            auto interrupted = makeScene(reverse ? L"library" : L"home", D2D1::ColorF(D2D1::ColorF::Blue));
            interrupted->animations = home->animations;
            Check(surface.paintCounters().content == pagePaints &&
                  surface.widgetCompositionCounters().rasterUploads == uploads,
                  "new presets animate without application paints or raster uploads");
            commit(interrupted);
            Check(surface.widgetCompositionCounters().interruptionCaptures == priorCaptures + 1,
                  "new presets capture interrupted geometry once");
            Sleep(450); DwmFlush();
            Check(GetBValue(pixel()) > 220, "interrupted preset settles to the newest page");
            // Restore the correct starting page for reverse navigation.
            if (!reverse) {
                auto library = makeScene(L"library", D2D1::ColorF(D2D1::ColorF::Red));
                library->animations = home->animations;
                commit(library);
                Sleep(450); DwmFlush();
            }
        }
    }
    auto zoomBase = makeScene(L"library", D2D1::ColorF(D2D1::ColorF::Blue));
    zoomBase->animations = {animation::SectionStyle::Slide, animation::ModalStyle::Zoom, .5};
    commit(zoomBase);
    auto zoomModal = modalScene();
    zoomModal->animations = zoomBase->animations;
    commit(zoomModal);
    Sleep(140); DwmFlush();
    Check(GetRValue(pixel(24, 64)) < 20 && GetRValue(pixel()) > 20,
          "zoom modal starts visibly inset while its center appears");
    commit(zoomBase);
    Sleep(70); DwmFlush();
    commit(zoomModal);
    const auto zoomPaints = surface.paintCounters().content;
    Sleep(550); DwmFlush();
    Check(GetRValue(pixel(25, 64)) > 220 && surface.paintCounters().content == zoomPaints,
          "interrupted zoom modal reaches full size without UI paints");
    const auto scaleScene = [&](float amount) {
        auto scene = std::make_shared<WidgetCompositionScene>();
        scene->authority = L"scale-proof"; scene->viewport = {0, 0, 128, 128};
        WidgetCompositionNode background;
        background.id = L"background"; background.bounds = background.clip = scene->viewport;
        background.solid = D2D1::ColorF(D2D1::ColorF::Black); scene->nodes.push_back(background);
        WidgetCompositionNode control;
        control.id = L"control"; control.kind = WidgetCompositionKind::Control; control.key = L"item";
        control.clock = L"page"; control.bounds = {40, 40, 40, 40}; control.clip = scene->viewport;
        control.controlScale = amount; control.controlDuration = 200; scene->nodes.push_back(control);
        auto image = background; image.id = L"control.pixels"; image.parent = control.id;
        image.bounds = control.bounds; image.solid = D2D1::ColorF(D2D1::ColorF::Red); scene->nodes.push_back(image);
        auto text = image; text.id = L"control.text"; text.bounds = {50, 50, 4, 4};
        text.solid = D2D1::ColorF(D2D1::ColorF::White); scene->nodes.push_back(text);
        return scene;
    };
    commit(scaleScene(1)); DwmFlush();
    Check(GetRValue(pixel(38, 60)) < 5, "unscaled control leaves its surrounding space clear");
    commit(scaleScene(1.2F));
    const auto scalePaints = surface.paintCounters().content;
    const auto scaledInput = surface.MapWidgetCompositionInput({50, 50});
    Check(scaledInput.x == 50 && scaledInput.y == 50, "whole-control scale leaves logical hit targets unchanged");
    Sleep(240); DwmFlush();
    Check(GetRValue(pixel(38, 60)) > 220 && GetGValue(pixel(49, 49)) > 220 && surface.paintCounters().content == scalePaints,
          "compositor scale grows artwork and text together without UI frame painting");
    commit(scaleScene(.98F));
    Sleep(45); DwmFlush();
    commit(scaleScene(1.04F));
    Sleep(240); DwmFlush();
    Check(GetRValue(pixel(38, 60)) < 5, "pressed-to-focused scale retargets one transform rather than stacking scales");
    }
    const auto popupFrame = [&](const wchar_t* key, bool reduced = false, float scale = 1.0F,
                                bool captureOnly = false) {
        OverlayCompositionSurface::Frame frame;
        Check(SUCCEEDED(surface.BeginFrame(128, 128, frame)), "popup frame begins");
        frame.target->Clear(D2D1::ColorF(D2D1::ColorF::Blue));
        if (key) {
            animation::Options animations;
            animations.speed = .5;
            frame.popupScene = shell::CapturePopupComposition(frame.target.Get(), {16, 16, 96, 96},
                {16, 112, 24, 12}, {0, 0, 128, 128}, scale, key, reduced, animations,
                [&](ID2D1RenderTarget* popupTarget) {
                    ComPtr<ID2D1SolidColorBrush> fill;
                    Check(SUCCEEDED(popupTarget->CreateSolidColorBrush(D2D1::ColorF(D2D1::ColorF::Lime),
                        fill.GetAddressOf())), "popup capture brush");
                    popupTarget->FillRectangle(D2D1::RectF(16, 16, 112, 112), fill.Get());
                }, surface.ResourceBudget());
            Check(frame.popupScene && frame.popupScene->nodes.size() == 2,
                "popup is one bounded raster and one compositor group");
            if (captureOnly) frame.popupScene.reset();
        }
        Check(SUCCEEDED(surface.EndFrame(frame)), "popup frame ends");
        OverlayCompositionSurface::CommitTiming timing;
        Check(SUCCEEDED(surface.CommitFrame(frame, true, timing)), "popup frame commits");
    };
    popupFrame(L"capture-check", false, 1, true); DwmFlush();
    Check(GetBValue(pixel(64, 64)) > 220, "popup capture does not paint its parent surface");
    const auto popupTestStarted = GetTickCount64();
    popupFrame(L"open-1"); DwmFlush();
    const auto popupCommitElapsed = GetTickCount64() - popupTestStarted;
    const auto popupStarts = surface.popupCompositionCounters(OverlayCompositionSurface::Layer::Content).animationStarts;
    Check(popupStarts > 0, "a new popup starts an entrance even on its first scene");
    const auto outsidePopup = surface.MapPopupCompositionInput(OverlayCompositionSurface::Layer::Content, {111, 17});
    Check(!std::isfinite(outsidePopup.x), "unrevealed popup pixels cannot activate final-layout options");
    const auto openingPixel = pixel(110, 18);
    const auto outsidePixel = pixel(4, 4);
    if (GetBValue(openingPixel) <= 220)
        std::cerr << "popup opening pixel RGB=" << static_cast<int>(GetRValue(openingPixel)) << ','
            << static_cast<int>(GetGValue(openingPixel)) << ',' << static_cast<int>(GetBValue(openingPixel))
            << " commit-ms=" << popupCommitElapsed << " sampled-ms=" << GetTickCount64() - popupTestStarted << '\n';
    Check(GetBValue(outsidePixel) > 220, "popup capture cannot paint outside its final bounds");
    const bool intermediateFrameObserved = GetBValue(openingPixel) > 220;
    popupFrame(L"open-1");
    Check(surface.popupCompositionCounters(OverlayCompositionSurface::Layer::Content).animationStarts == popupStarts,
        "highlight and content updates do not restart popup entry");
    const auto popupPaints = surface.paintCounters().content;
    Sleep(380); DwmFlush();
    Check(GetGValue(pixel(110, 18)) > 220 && surface.paintCounters().content == popupPaints,
        "whole popup reaches final bounds without UI-thread animation paints");
    popupFrame(nullptr); DwmFlush();
    Check(GetBValue(pixel(64, 64)) > 220,
        "dismissal removes popup pixels immediately without retained input or closing ghosts");
    popupFrame(L"close-during-entry");
    popupFrame(nullptr); DwmFlush();
    Check(GetBValue(pixel(64, 64)) > 220, "dismissal during entry cancels all popup pixels");
    const auto reopenedStarts = surface.popupCompositionCounters(OverlayCompositionSurface::Layer::Content).animationStarts;
    Check(reopenedStarts > popupStarts, "reopening starts a fresh popup entrance");
    popupFrame(L"open-2", true); DwmFlush();
    Check(GetGValue(pixel(110, 18)) > 220 &&
          surface.popupCompositionCounters(OverlayCompositionSurface::Layer::Content).animationStarts == reopenedStarts,
        "reduced motion draws the complete popup immediately");
    popupFrame(L"open-2", false, 1.25F);
    Check(surface.popupCompositionCounters(OverlayCompositionSurface::Layer::Content).animationStarts == reopenedStarts,
        "DPI or scale changes snap the popup without replaying its entrance");
    const auto chromeWindow = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE |
        WS_EX_NOREDIRECTIONBITMAP, name, L"Tray popup test", WS_POPUP, 180, 40, 128, 128,
        nullptr, nullptr, wc.hInstance, nullptr);
    Check(chromeWindow && surface.InitializeChromeTarget(chromeWindow, error), "tray popup has its chrome endpoint");
    OverlayCompositionSurface::Frame trayFrame;
    Check(SUCCEEDED(surface.BeginFrame(OverlayCompositionSurface::Layer::Tray, 128, 128, 0, 0,
        nullptr, trayFrame)), "tray popup frame begins");
    trayFrame.target->Clear(D2D1::ColorF(D2D1::ColorF::Blue));
    trayFrame.popupScene = shell::CapturePopupComposition(trayFrame.target.Get(), {16, 16, 96, 96},
        {48, 112, 24, 12}, {0, 0, 128, 128}, 1, L"tray-open", true, {},
        [&](ID2D1RenderTarget* target) {
            ComPtr<ID2D1SolidColorBrush> fill;
            Check(SUCCEEDED(target->CreateSolidColorBrush(D2D1::ColorF(D2D1::ColorF::Lime),
                fill.GetAddressOf())), "tray popup brush");
            target->FillRectangle(D2D1::RectF(16, 16, 112, 112), fill.Get());
        }, surface.ResourceBudget());
    Check(SUCCEEDED(surface.EndFrame(trayFrame)), "tray popup frame ends");
    OverlayCompositionSurface::CommitTiming trayTiming;
    Check(SUCCEEDED(surface.CommitFrame(trayFrame, true, trayTiming)), "tray popup commits on chrome");
    ShowWindow(chromeWindow, SW_SHOWNOACTIVATE); DwmFlush();
    Check(GetGValue(pixel(64, 64, chromeWindow)) > 220 && GetBValue(pixel(4, 4, chromeWindow)) > 220,
        "tray popup paints above its own chrome surface without escaping its bounds");
    surface.Reset();
    DestroyWindow(chromeWindow);
    DestroyWindow(window);
    UnregisterClassW(name, wc.hInstance);
    std::cout << "Popup capture, layering, input mapping, timeline continuity, dismissal and reduced motion checks passed\n";
    Check(intermediateFrameObserved, "DWM popup intermediate frame was not observed by desktop readback");
    std::cout << "Widget compositor pixel proof passed: UI thread paused, zero animation rasters, update "
                 "preserved timeline\n";
}

} // namespace


void CheckFocusFadeCompositor(bool pixels, float pixelScale = 1, bool bitmaps = true,
                              widgetrail::animation::FocusStyle focusStyle = widgetrail::animation::FocusStyle::Fade,
                              bool scaleControls = true, bool tightRow = false, bool depth = false, bool scrollRetarget = false) {
    using namespace widgetrail;
    const wchar_t *name = L"WidgetRail.FocusFadeTest";
    WNDCLASSW wc{}; wc.lpfnWndProc = DefWindowProcW;
    wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = name;
    Check(RegisterClassW(&wc) != 0, "focus fade window class");
    HWND window = CreateWindowExW(WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_NOREDIRECTIONBITMAP,
        name, L"Focus fade test", WS_POPUP, 40, 40, static_cast<int>(160 * pixelScale), static_cast<int>(100 * pixelScale), nullptr, nullptr, wc.hInstance, nullptr);
    Check(window != nullptr, "focus fade test window");
    ComPtr<ID2D1Factory1> factory;
    Check(SUCCEEDED(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, factory.GetAddressOf())), "fade factory");
    OverlayCompositionSurface surface;
    std::wstring error;
    Check(surface.Initialize(window, factory.Get(), error), "focus fade compositor initializes");
    const auto sceneFor = [&](bool right) {
        auto scene = std::make_shared<WidgetCompositionScene>();
        scene->authority = L"fade-proof"; scene->viewport = {0, 0, 160, 100};
        scene->animations.speed = .5; scene->scale = pixelScale; scene->animations.focus = focusStyle;
        WidgetCompositionNode background;
        background.id = L"background"; background.bounds = background.clip = scene->viewport;
        background.solid = D2D1::ColorF(D2D1::ColorF::Blue); scene->nodes.push_back(background);
        scene->focusTargets = {{L"left", L"page", {10, 20, 40, 40}, scene->viewport},
                               {L"right", L"page", {100, 20, 40, 40}, scene->viewport}};
        if (tightRow) for (auto &item : scene->focusTargets) item.clip = {0, 20, 160, 40};
        for (const auto &item : scene->focusTargets) {
            WidgetCompositionNode control;
            control.id = L"control/" + item.key; control.key = item.key; control.clock = item.scope;
            control.kind = WidgetCompositionKind::Control; control.bounds = item.bounds; control.clip = item.clip;
            control.controlScale = scaleControls && (item.key == (right ? L"right" : L"left")) ? 1.25F : 1.0F;
            control.controlDuration = 140; scene->nodes.push_back(control);
            WidgetCompositionNode group;
            group.id = L"surface/" + item.key; group.key = item.key; group.clock = item.scope;
            group.parent = control.id; group.kind = WidgetCompositionKind::FocusSurface; group.bounds = item.bounds; group.clip = item.clip;
            scene->nodes.push_back(group);
            WidgetCompositionNode idle;
            idle.id = group.id + L"/idle"; idle.parent = group.id; idle.bounds = group.bounds; idle.clip = group.clip;
            if (depth) idle.bounds = {item.bounds.x - 8, item.bounds.y - 8, 56, 56};
            idle.solid = D2D1::ColorF(1, 0, 0, .5F); scene->nodes.push_back(idle);
            auto focused = idle; focused.id = group.id + L"/focused"; focused.order = 1;
            focused.solid = D2D1::ColorF(0, 1, 0, .25F); scene->nodes.push_back(focused);
        }
        const auto &item = scene->focusTargets[right ? 1 : 0];
        WidgetCompositionNode focus;
        focus.id = L"focus/" + item.key; focus.parent = L"control/" + item.key; focus.key = item.key; focus.clock = item.scope;
        focus.kind = WidgetCompositionKind::Focus; focus.bounds = item.bounds; focus.clip = item.clip;
        scene->nodes.push_back(focus);
        WidgetCompositionNode marker;
        marker.id = L"marker"; marker.parent = focus.id;
        marker.bounds = tightRow ? item.bounds : declarative::Rect{item.bounds.x, 65, 40, 5}; marker.clip = item.clip;
        marker.solid = D2D1::ColorF(D2D1::ColorF::White); scene->nodes.push_back(marker);
        return scene;
    };
    std::map<std::wstring, std::pair<resources::UiResource<ID2D1Bitmap>, std::shared_ptr<void>>> retainedPixels;
    const auto commit = [&](std::shared_ptr<WidgetCompositionScene> scene) {
        OverlayCompositionSurface::Frame frame;
        Check(SUCCEEDED(surface.BeginFrame(static_cast<UINT>(160 * pixelScale), static_cast<UINT>(100 * pixelScale), frame)), "fade frame begins");
        frame.target->Clear(D2D1::ColorF(0, 0));
        // Exercise real raster surfaces, not only the one-pixel solid shortcut.
        for (auto &node : scene->nodes) {
            const bool outline = tightRow && node.id == L"marker";
            if (!node.solid || (!outline && (!bitmaps || node.parent.find(L"surface/") != 0))) continue;
            const auto retained = retainedPixels.find(node.id);
            if (!outline && retained != retainedPixels.end()) {
                node.bitmap = retained->second.first;
                node.rasterLease = retained->second.second;
                node.solid.reset();
                continue;
            }
            ComPtr<ID2D1BitmapRenderTarget> bitmapTarget;
            const auto size = D2D1::SizeF(node.bounds.width, node.bounds.height);
            const auto bitmapPixels = D2D1::SizeU(static_cast<UINT>(std::ceil(size.width * pixelScale)),
                static_cast<UINT>(std::ceil(size.height * pixelScale)));
            Check(SUCCEEDED(frame.target->CreateCompatibleRenderTarget(&size, &bitmapPixels, nullptr,
                D2D1_COMPATIBLE_RENDER_TARGET_OPTIONS_NONE, bitmapTarget.GetAddressOf())), "fade bitmap target");
            bitmapTarget->BeginDraw(); bitmapTarget->Clear(D2D1::ColorF(0, 0));
            ComPtr<ID2D1SolidColorBrush> brush;
            Check(SUCCEEDED(bitmapTarget->CreateSolidColorBrush(*node.solid, brush.GetAddressOf())), "fade bitmap brush");
            if (outline)
                bitmapTarget->DrawRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(1, 1, size.width - 1, size.height - 1), 5, 5), brush.Get(), 2);
            else if (depth) {
                widgetrail::surface::ShadowCache shadows;
                Check(shadows.Draw(bitmapTarget.Get(), {8, 8, 40, 40}, 5, 4, 0, 2,
                    D2D1::ColorF(0, .3F), pixelScale), "depth shadow rasterizes on compatible target");
                Check(widgetrail::surface::Fill(bitmapTarget.Get(), D2D1::RoundedRect({8, 8, 48, 48}, 5, 5),
                    widgetrail::surface::Shade(*node.solid, .2F), widgetrail::surface::Shade(*node.solid, -.2F)),
                    "depth gradient rasterizes on compatible target");
            }
            else
                bitmapTarget->FillRoundedRectangle(D2D1::RoundedRect(D2D1::RectF(0, 0, size.width, size.height), 5, 5), brush.Get());
            Check(SUCCEEDED(bitmapTarget->EndDraw()), "fade bitmap paint");
            ComPtr<ID2D1Bitmap> pixels;
            Check(SUCCEEDED(bitmapTarget->GetBitmap(pixels.GetAddressOf())), "fade bitmap capture");
            node.bitmap = resources::UiResource<ID2D1Bitmap>::External(std::move(pixels));
            if (!outline) {
                node.rasterLease = std::make_shared<char>();
                retainedPixels[node.id] = {node.bitmap, node.rasterLease};
            }
            node.solid.reset();
        }
        frame.widgetScene = std::move(scene);
        Check(SUCCEEDED(surface.EndFrame(frame)), "fade frame ends");
        OverlayCompositionSurface::CommitTiming timing;
        Check(SUCCEEDED(surface.CommitFrame(frame, true, timing)), "fade effect commits");
    };
    const auto pixel = [&](int x, int y) {
        POINT pt{static_cast<LONG>(x * pixelScale), static_cast<LONG>(y * pixelScale)}; ClientToScreen(window, &pt);
        HDC dc = GetDC(nullptr); auto color = GetPixel(dc, pt.x, pt.y); ReleaseDC(nullptr, dc); return color;
    };
    commit(sceneFor(false));
    if (scrollRetarget) {
        ShowWindow(window, SW_SHOWNOACTIVATE); DwmFlush();
        commit(sceneFor(true)); Sleep(30); DwmFlush();
        auto scrolled = sceneFor(false);
        for (auto& item : scrolled->focusTargets) { item.bounds.y -= 8; item.clip = {0, 20, 160, 40}; }
        for (auto& node : scrolled->nodes) if (node.id != L"background") {
            node.bounds.y -= 8; node.clip = {0, 20, 160, 40};
        }
        const auto beforeScroll = surface.widgetCompositionCounters();
        commit(scrolled); DwmFlush();
        const auto afterScroll = surface.widgetCompositionCounters();
        Check(afterScroll.rasterUploads == beforeScroll.rasterUploads &&
            afterScroll.focusAtlasReuses > beforeScroll.focusAtlasReuses,
            "translated immutable captures reuse GPU pixels and focus atlases");
        Sleep(animation::FocusDuration(focusStyle) * 2 + 80); DwmFlush();
        const auto old = pixel(120, 32), current = pixel(30, 32);
        std::cout << "Scroll retarget scale=" << pixelScale << " old RGB=" << (int)GetRValue(old) << ','
            << (int)GetGValue(old) << ',' << (int)GetBValue(old) << " current RGB=" << (int)GetRValue(current) << ','
            << (int)GetGValue(current) << ',' << (int)GetBValue(current) << std::endl;
        Check(GetRValue(old) > 120 && GetGValue(old) < 5 && GetGValue(current) >= 60,
            "scroll during focus fade cannot retain the previous control's highlighted background");
        const auto outside = pixel(30, 16);
        Check(GetRValue(outside) < 5 && GetGValue(outside) < 5 && GetBValue(outside) > 245,
            "compositor clips a full retained item capture at the stationary scroll viewport");
        Check(surface.ResourceBudget()->Read().allocatedBytes > 0 && surface.ResourceBudget()->Read().protectedBytes > 0,
            "compositor backing stores remain accounted and protected while displayed");
        surface.Reset();
        Check(surface.ResourceBudget()->Read().allocatedBytes == 0 && surface.ResourceBudget()->Read().protectedBytes == 0,
            "compositor reset retires surface, atlas, popup and frame accounting");
        DestroyWindow(window); UnregisterClassW(name, wc.hInstance);
        return;
    }
    if (depth) {
        if (pixels) { ShowWindow(window, SW_SHOWNOACTIVATE); DwmFlush(); Sleep(50); DwmFlush(); }
        const auto initial = pixel(30, 40);
        const auto shadow = pixel(30, 63);
        if (pixels) {
            Check(GetBValue(shadow) > 180 && GetBValue(shadow) < 255,
                "expanded capture preserves the soft shadow outside the control");
            Check(GetRValue(pixel(120, 25)) > GetRValue(pixel(120, 55)) + 10,
                "focus atlas preserves vertical surface shading");
        }
        commit(sceneFor(true));
        const auto paints = surface.paintCounters().content;
        const auto uploads = surface.widgetCompositionCounters().rasterUploads;
        Sleep(80); DwmFlush();
        if (pixels) {
            const auto during = pixel(30, 40);
            Check(GetRValue(during) > GetRValue(initial) && GetGValue(during) < GetGValue(initial),
                "shaded translucent focus interpolates rather than snapping");
        }
        Sleep(animation::FocusDuration(focusStyle) * 2 + 40); DwmFlush();
        if (pixels) {
            const auto final = pixel(120, 40);
            Check(std::abs(GetRValue(final) - GetRValue(initial)) <= 3 &&
                  std::abs(GetGValue(final) - GetGValue(initial)) <= 3 &&
                  std::abs(GetBValue(final) - GetBValue(initial)) <= 3,
                "depth fade arrives at the exact focused surface color without stretching its expanded atlas");
            Check(std::abs(GetBValue(pixel(120, 63)) - GetBValue(shadow)) <= 3,
                "depth fade retains the same shadow extent and alpha");
        }
        Check(surface.paintCounters().content == paints && surface.widgetCompositionCounters().rasterUploads == uploads,
            "depth focus animation requires no per-frame UI painting or mask upload");
        retainedPixels.erase(L"surface/right/focused");
        auto recolored = sceneFor(true);
        for (auto& node : recolored->nodes)
            if (node.id == L"surface/right/focused") node.solid = D2D1::ColorF(1, 1, 0, .25F);
        commit(recolored); DwmFlush();
        Check(surface.widgetCompositionCounters().rasterUploads == uploads + 1,
            "a changed source uploads only its focus atlas; unrelated GPU pixels are reused");
        if (pixels) Check(GetRValue(pixel(120, 40)) > GetRValue(initial) + 40,
            "retained atlas invalidation presents the new theme color without stale pixels");
        Check(surface.ResourceBudget()->Read().allocatedBytes > 0 && surface.ResourceBudget()->Read().protectedBytes > 0,
            "compositor backing stores remain accounted and protected while displayed");
        surface.Reset();
        Check(surface.ResourceBudget()->Read().allocatedBytes == 0 && surface.ResourceBudget()->Read().protectedBytes == 0,
            "compositor reset retires surface, atlas, popup and frame accounting");
        DestroyWindow(window); UnregisterClassW(name, wc.hInstance);
        std::cout << "Surface depth compositor scale=" << pixelScale << " passed" << std::endl;
        return;
    }
    if (pixels) {
        ShowWindow(window, SW_SHOWNOACTIVATE); DwmFlush(); Sleep(50); DwmFlush();
        const auto color = pixel(30, 40);
        Check(GetRValue(color) < 5 && GetGValue(color) >= 60 && GetGValue(color) <= 68 && GetBValue(color) >= 185,
            "fade starts with exact translucent focused color");
        Check(GetGValue(pixel(scaleControls ? 52 : 48, 40)) >= 60, "focused surface fills the control to its expected edge");
        Check(GetRValue(pixel(4, 14)) == 0 && GetGValue(pixel(4, 14)) == 0,
            "scaled rounded surface does not spill outside the card");
    }
    commit(sceneFor(true));
    auto counters = surface.widgetCompositionCounters();
    if (bitmaps) Check(counters.focusAtlasReuses >= 2,
        "focus changes reuse immutable idle/focused GPU atlases");
    const auto paints = surface.paintCounters().content;
    Sleep(60); DwmFlush();
    if (pixels) {
        Check(GetRValue(pixel(75, 74)) < 5, "fade outline never crosses the gap");
        const auto color = pixel(30, 40);
        Check(GetRValue(color) > 0 && GetRValue(color) < 128 && GetGValue(color) > 0,
            "old focus surface interpolates in place");
        // Premultiplied colors plus the blue background retain total energy.
        Check(std::abs(GetRValue(color) + GetGValue(color) + GetBValue(color) - 255) <= 4,
            "translucent crossfade does not accumulate or lose alpha");
    }
    Check(surface.paintCounters().content == paints && surface.widgetCompositionCounters().rasterUploads == counters.rasterUploads,
        "focus fade advances without UI paints or uploads");
    if (pixels && tightRow) {
        Check(GetGValue(pixel(120, 26)) > 130 && GetGValue(pixel(120, 21)) < 100,
            "tightly clipped row visibly expands its outline from inside the control");
        Check(GetGValue(pixel(120, 19)) == 0, "settle does not escape the row clip");
        Sleep(430); DwmFlush();
        Check(GetGValue(pixel(120, 21)) > 240 && GetGValue(pixel(120, 26)) < 100,
            "tight row settle arrives at the exact final border");
        Check(surface.paintCounters().content == paints && surface.widgetCompositionCounters().rasterUploads == counters.rasterUploads,
            "tight row movement stays compositor-owned");
        Check(surface.ResourceBudget()->Read().allocatedBytes > 0 && surface.ResourceBudget()->Read().protectedBytes > 0,
            "compositor backing stores remain accounted and protected while displayed");
        surface.Reset();
        Check(surface.ResourceBudget()->Read().allocatedBytes == 0 && surface.ResourceBudget()->Read().protectedBytes == 0,
            "compositor reset retires surface, atlas, popup and frame accounting");
        DestroyWindow(window); UnregisterClassW(name, wc.hInstance);
        std::cout << "Tight-row settle scale=" << pixelScale << " passed" << std::endl;
        return;
    }
    if (pixels && focusStyle == animation::FocusStyle::Settle && !scaleControls) {
        Check(GetRValue(pixel(120, 60)) > 15 && GetRValue(pixel(120, 68)) < 5,
            "settle expands the incoming decoration without resizing the control");
        Check(GetGValue(pixel(143, 40)) == 0,
            "settle does not enlarge the control background alongside its outline");
    }
    commit(sceneFor(false)); // reverse before completion
    const auto captures = surface.widgetCompositionCounters().interruptionCaptures;
    commit(sceneFor(false)); // same-key updates must not capture/restart
    Check(surface.widgetCompositionCounters().interruptionCaptures == captures,
        "same target updates do not recapture the focus visual");
    if (pixels && focusStyle == animation::FocusStyle::Settle && !scaleControls) {
        commit(sceneFor(true)); // resume a still-inset, fading-out outline
        Sleep(40); DwmFlush();
        bool visibleDecoration{};
        for (int y = 54; y < 90; ++y) {
            if (GetRValue(pixel(120, y)) <= 15) continue;
            visibleDecoration = true;
            Check(y <= 77, "rapid settle re-entry uses its sampled control pose, not captured raster bounds");
        }
        Check(visibleDecoration, "rapid settle re-entry retains a visible destination outline");
    }
    const auto mapped = surface.MapWidgetCompositionInput({30, 40});
    Check(mapped.x == 30 && mapped.y == 40, "fade leaves logical input geometry unchanged");
    auto removed = sceneFor(false); removed->focusTargets.resize(1);
    commit(removed); // evict the outgoing cursor identity during its fade
    auto reduced = sceneFor(true); reduced->reducedMotion = true;
    commit(reduced); DwmFlush();
    if (pixels) Check(GetRValue(pixel(120, scaleControls ? 74 : 67)) > 245 && GetRValue(pixel(30, scaleControls ? 74 : 67)) < 5,
        "reduced motion snaps new focus and retires old outline");
    auto none = sceneFor(false); none->animations.focus = animation::FocusStyle::None;
    commit(none); DwmFlush();
    if (pixels) Check(GetGValue(pixel(30, 40)) >= 60 && GetRValue(pixel(30, 40)) < 5,
        "None preserves the same final themed color as Fade");
    Sleep(260); surface.AdvanceWidgetComposition();
    // Fail after a replacement surface has been assigned to the visual. Both
    // displayed and staged storage must remain counted until graph reset.
    const auto oldWidth = static_cast<UINT>(160 * pixelScale), oldHeight = static_cast<UINT>(100 * pixelScale);
    OverlayCompositionSurface::Frame failedFrame;
    Check(SUCCEEDED(surface.BeginFrame(oldWidth + 8, oldHeight + 8, failedFrame)), "staged accounting frame begins");
    failedFrame.target->Clear(D2D1::ColorF(0, 0));
    Check(SUCCEEDED(surface.EndFrame(failedFrame)), "staged accounting frame ends");
    OverlayCompositionSurface::VisualPresentation invalidPlacement;
    invalidPlacement.scaleX = 0;
    OverlayCompositionSurface::CommitTiming failedTiming;
    Check(FAILED(surface.CommitFrame(failedFrame, false, failedTiming, &invalidPlacement)), "invalid placement rejects staged frame");
    const auto staged = surface.ResourceBudget()->Read();
    const std::size_t expectedSurfaces = (static_cast<std::size_t>(oldWidth) * oldHeight +
        static_cast<std::size_t>(oldWidth + 8) * (oldHeight + 8)) * 4;
    Check(staged.allocatedByKind[static_cast<std::size_t>(resources::Kind::CompositorSurface)] >= expectedSurfaces &&
        staged.protectedByKind[static_cast<std::size_t>(resources::Kind::CompositorSurface)] >= expectedSurfaces,
        "failed commit preserves accounting for committed and staged surface ownership");
    std::cout << "Focus style=" << static_cast<int>(focusStyle) << " bitmap=" << bitmaps << " scale=" << pixelScale << " control-scale=" << scaleControls << " passed" << std::endl;
    Check(surface.ResourceBudget()->Read().allocatedBytes > 0 && surface.ResourceBudget()->Read().protectedBytes > 0,
        "compositor backing stores remain accounted and protected while displayed");
    surface.Reset();
    Check(surface.ResourceBudget()->Read().allocatedBytes == 0 && surface.ResourceBudget()->Read().protectedBytes == 0,
        "compositor reset retires surface, atlas, popup and frame accounting");
    DestroyWindow(window); UnregisterClassW(name, wc.hInstance);
}

int main(int argc, char** argv) {
    CheckWidgetAnimationPolicies();
    if (argc == 2 && std::string_view(argv[1]) == "--focus-scroll-pixels") {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        Check(SUCCEEDED(CoInitializeEx(nullptr, COINIT_MULTITHREADED)), "scroll focus COM initialization");
        for (float scale : {1.0F, 1.25F, 1.5F, 2.0F})
            CheckFocusFadeCompositor(true, scale, true, widgetrail::animation::FocusStyle::Fade, false, false, false, true);
        CoUninitialize(); return EXIT_SUCCESS;
    }
    if (argc == 2 && std::string_view(argv[1]) == "--loading-indicator-pixels") {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        Check(SUCCEEDED(CoInitializeEx(nullptr, COINIT_MULTITHREADED)), "loading proof COM initialization");
        for (float scale : {1.0F, 1.25F, 2.0F}) CheckLoadingIndicatorComposition(true, scale);
        CoUninitialize(); return EXIT_SUCCESS;
    }
    if (argc == 2 && std::string_view(argv[1]) == "--surface-depth-pixels") {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        Check(SUCCEEDED(CoInitializeEx(nullptr, COINIT_MULTITHREADED)), "depth COM initialization");
        for (float scale : {1.0F, 1.25F, 1.5F, 2.0F})
            CheckFocusFadeCompositor(true, scale, true, widgetrail::animation::FocusStyle::Fade, false, false, true);
        CoUninitialize(); return EXIT_SUCCESS;
    }
    if (argc == 2 && std::string_view(argv[1]) == "--focus-fade-pixels") {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        Check(SUCCEEDED(CoInitializeEx(nullptr, COINIT_MULTITHREADED)), "fade COM initialization");
        CheckFocusFadeCompositor(true, 1, false);
        for (float scale : {.85F, 1.0F, 1.25F, 1.5F}) {
            CheckFocusFadeCompositor(true, scale);
            CheckFocusFadeCompositor(true, scale, true, widgetrail::animation::FocusStyle::Settle, false);
            CheckFocusFadeCompositor(true, scale, true, widgetrail::animation::FocusStyle::Settle, false, true);
        }
        CheckFocusFadeCompositor(true, 1.25F, true, widgetrail::animation::FocusStyle::Settle);
        CoUninitialize(); return EXIT_SUCCESS;
    }
    if (argc==2 && (std::string_view(argv[1])=="--widget-motion-pixels" ||
                   std::string_view(argv[1])=="--popup-motion-pixels" ||
                   std::string_view(argv[1])=="--widget-ordering-pixels")) {
        Check(SUCCEEDED(CoInitializeEx(nullptr,COINIT_MULTITHREADED)),"pixel proof COM initialization");
        CheckWidgetCompositorPixels(std::string_view(argv[1])=="--popup-motion-pixels",
            std::string_view(argv[1])=="--widget-ordering-pixels");
        CoUninitialize(); return EXIT_SUCCESS;
    }
    for (const float scale : {0.75F, 1.0F, 1.25F, 1.5F, 2.0F}) {
        const RECT work{-1920, -100, 0, 980};
        const RECT window{-1500, 650, -420, 980};
        const RECT guide{0, 100, 1080, 158};
        const RECT tray{0, 0, 1080, 330};
        const auto radial = widgetrail::shell::ComputeRadialChromePlacement(
            work, window, guide, tray, scale);
        Check(radial.wheelSize == static_cast<LONG>(std::floor(400.0F * scale)),
              "wheel uses a preferred diameter of 400 logical pixels at each scale");
        Check(radial.windowBounds.top + radial.guideClientBounds.top == window.top + guide.top &&
              radial.windowBounds.top + radial.guideClientBounds.bottom == window.top + guide.bottom,
              "radial canvas expansion preserves both guide screen edges at every scale");
        Check(radial.windowBounds.top + radial.trayClientBounds.top + radial.railOffset == window.top + tray.top &&
              radial.windowBounds.top + radial.trayClientBounds.bottom == window.top + tray.bottom,
              "radial canvas expansion preserves the original rail screen anchors");
        const auto before = widgetrail::shell::ComputeContentWindowBoundsAboveGuide(
            work, window.top + guide.top, 700, 500, 12);
        const auto after = widgetrail::shell::ComputeContentWindowBoundsAboveGuide(
            work, radial.windowBounds.top + radial.guideClientBounds.top, 700, 500, 12);
        Check(before && after && EqualRect(&*before, &*after),
              "radial overlay cannot change widget screen bounds or available height");
        Check(radial.windowBounds.top >= work.top && radial.wheelSize > 0 &&
              radial.trayClientBounds.top + radial.wheelSize < radial.guideClientBounds.top,
              "wheel fits on the monitor above the stationary guide");
    }
    const HRESULT initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    Check(SUCCEEDED(initialized), "COM initializes for WIC");
    ComPtr<ID2D1Factory> d2d;
    Check(SUCCEEDED(D2D1CreateFactory(
              D2D1_FACTORY_TYPE_SINGLE_THREADED,
              d2d.ReleaseAndGetAddressOf())),
          "Direct2D factory is created");
    ComPtr<IWICImagingFactory> wic;
    Check(SUCCEEDED(CoCreateInstance(
              CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
              IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
          "WIC factory is created");

    CheckFocusFadeCompositor(false);
    CheckLoadingIndicatorComposition(false, 1);
    widgetrail::PreparationFrameBudget budget;
    Check(budget.Available(10000, 0, 16667, true) == 0, "preparation never precedes an outstanding paint");
    Check(budget.Available(16000, 0, 16667, false) == 0, "preparation leaves a frame deadline margin");
    Check(budget.Available(1000, 0, 8333, false) > 0, "high refresh frame has bounded preparation headroom");
    budget.Observe(8000);
    Check(budget.Available(13000, 0, 16667, false) == 0, "observed work cost prevents starting a slice that cannot fit");
    Check(budget.Available(18000, 0, 16667, false) > 0, "deferred preparation resumes on the next frame");
    budget.Observe(8000); budget.Observe(8000);
    Check(budget.Available(20000, 0, 2778, false) == 0, "expensive work initially yields on a high-refresh display");
    Check(budget.Available(120000, 0, 2778, false) > 0, "aged indivisible work cannot starve on a high-refresh display");
    for (const std::uint64_t period : {2778, 4166, 8333, 16667}) {
        for (std::uint64_t phase = 0; phase < period; phase += 37) {
            widgetrail::PreparationFrameBudget past, future;
            const auto now = period * 10 + phase;
            Check(past.Available(now, period * 8, period, false) ==
                future.Available(now, period * 12, period, false),
                "past and future DWM references produce the same cadence budget");
        }
        widgetrail::PreparationFrameBudget edge;
        const auto now = period * 10;
        Check(edge.Available(now, now + 1, period, false) == 0,
            "future vblank immediately ahead reserves the deadline margin");
        Check(edge.Available(now, now + period, period, false) > 0,
            "exact future cadence boundary allows the new frame");
        Check(edge.Available(now, now + 1, period, true) == 0,
            "paint priority is preserved for future timing");
    }
    widgetrail::PreparationFrameBudget fallback;
    Check(fallback.Available(0, 0, 0, false) == 1000, "missing timing uses bounded fallback");
    fallback.Observe(8000);
    Check(fallback.Available(0, 0, 0, false) == 0,
        "fallback honors observed cost even when the clock starts at zero");
    Check(fallback.Available(99999, 0, 0, false) == 0, "fallback does not prematurely promote expensive work");
    Check(fallback.Available(100000, 0, 0, false) == 1000, "fallback eventually promotes aged work");
    Check(fallback.Available(100001, 0, 0, true) == 0, "aged fallback never precedes pending paint");
    Check(fallback.Available(1, 0, 0, false) == 0, "clock rollback restarts the starvation interval");
    widgetrail::PreparationFrameBudget futureCost;
    futureCost.Observe(8000);
    Check(futureCost.Available(10000, 12500, 10000, false) == 0,
        "future timing honors measured cost instead of falling back");
    Check(futureCost.Available(110000, 112500, 10000, false) == 1000,
        "future timing preserves aged-work promotion");
    Check(futureCost.Available(110001, 110002, 10000, false) == 0,
        "aged work cannot spend the deadline reserve");
    widgetrail::PreparationFrameBudget progressCost, wholeBatchCost;
    unsigned progressSlices{}, batchSlices{};
    // At 240 Hz, a 3 ms batch may contain several sub-millisecond steps.
    // Requiring space for the previous full batch causes repeated 100 ms waits.
    for (std::uint64_t frame = 0; frame < 120; ++frame) {
        const auto now = 10000 + frame * 4166;
        if (progressCost.Available(now, now + 3241, 4166, false)) {
            ++progressSlices;
            progressCost.Observe(900);
        }
        if (wholeBatchCost.Available(now, now + 3241, 4166, false)) {
            ++batchSlices;
            wholeBatchCost.Observe(3000);
        }
    }
    Check(progressSlices == 120 && batchSlices < 20,
        "admitting useful steps avoids batch-cost feedback and recurring starvation delays");
    CheckFocusFadeCompositor(false, 1, true, widgetrail::animation::FocusStyle::Settle);
    CheckFrame(d2d.Get(), wic.Get(), 1.0F);
    CheckControllerGlyphs(d2d.Get(), wic.Get());
    CheckFrame(d2d.Get(), wic.Get(), 1.5F);
    CheckPremultipliedFrame(d2d.Get(), wic.Get(), 1.0F);
    CheckPremultipliedFrame(d2d.Get(), wic.Get(), 1.5F);
    CheckCompositionCoordinatePolicies();
    CheckTrayMenuCompositionHeadroom();
    CheckRenderedGuideContentCentering();
    CheckRetainedTrayInvalidation();
    CheckFixedChromeWindowPolicy();
    for (const RECT work : {RECT{0, 0, 1920, 1040}, RECT{-2560, -200, 0, 1240}}) {
        for (const auto position : {widgetrail::OverlayPosition::BottomLeft, widgetrail::OverlayPosition::BottomRight}) {
            const auto chrome = widgetrail::shell::ComputeFixedChromeWindowBounds(work, 1100, 220, position, 24);
            for (const LONG width : {400L, 801L, 1100L}) {
                const auto content = widgetrail::shell::ComputeContentWindowBoundsAboveGuide(
                    work, chrome.top, width, 500, 8, position, 24);
                Check(content.has_value(), "corner content fits negative-origin and primary monitors");
                Check(position == widgetrail::OverlayPosition::BottomLeft
                    ? content->left == chrome.left : content->right == chrome.right,
                    "varying widget widths share the fixed chrome outside edge");
                Check(content->bottom + 8 == chrome.top, "corner content keeps the guide gap");
            }
            const auto fullWidth = widgetrail::shell::ComputeContentWindowBoundsAboveGuide(
                work, chrome.top, work.right - work.left, 500, 8, position, 24);
            Check(fullWidth && fullWidth->left == work.left && fullWidth->right == work.right,
                "oversized content collapses side margins to stay within the display");
        }
    }
    widgetrail::shell::FillColorKeyRoundedRectangle(nullptr, {}, nullptr);

    wic.Reset();
    d2d.Reset();
    CoUninitialize();
    std::cout << "OverlayChromeTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
