#include "OverlayChrome.h"
#include "OverlayCompositionSurface.h"
#include "OverlayState.h"
#include "TrayLayout.h"

#include <Windows.h>
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
          ensureOwner.find("DashboardGuideContentBottomDip(dashboardHeightDip)") !=
              std::string_view::npos &&
          ensureOwner.find("chromeHeight - session.renderedGuideContentBottom") !=
              std::string_view::npos &&
          ensureOwner.find("centeredStripTop = session.renderedGuideContentBottom") !=
              std::string_view::npos,
          "fixed chrome centers from the exact rendered dashboard or widget guide-content bottom");
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
    Check(!widgetrail::shell::RequiresImageReadyRepaint(false, true),
          "an independent background advance owns its ordinary image-ready wake");
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
        WS_EX_TOOLWINDOW, className, L"content", WS_POPUP,
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
    Check(SUCCEEDED(composition.CommitFrames(motionFrames, true, motionTiming, &motion, nullptr, true, 12.0F)),
          "real compositor accepts resize, opacity and directional entrance together");
    Check(SUCCEEDED(composition.CommitShellZoom(0.97F, true, false)),
          "opening zoom can coexist with resize and entrance transforms");
    Check(SUCCEEDED(composition.SetShellZoomAnchor(800, 600)),
          "container changes update the zoom anchor without replacing its animation");
    const auto paintsBefore = composition.paintCounters().content;
    Check(SUCCEEDED(composition.SnapContentVisible()), "focus can settle compositor opacity without repaint");
    Check(composition.paintCounters().content == paintsBefore,
          "compositor opacity does not rasterize the widget again");
    Check(SUCCEEDED(composition.CommitShellZoom(1.0F, false, false)) &&
          SUCCEEDED(composition.CommitShellZoom(0.985F, true, false)),
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

    DestroyWindow(content);
    Check(!IsWindow(content) && !IsWindow(chrome),
          "normal owner close destroys the fixed chrome endpoint");
    UnregisterClassW(className, windowClass.hInstance);
}

} // namespace

int main() {
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

    CheckFrame(d2d.Get(), wic.Get(), 1.0F);
    CheckFrame(d2d.Get(), wic.Get(), 1.5F);
    CheckPremultipliedFrame(d2d.Get(), wic.Get(), 1.0F);
    CheckPremultipliedFrame(d2d.Get(), wic.Get(), 1.5F);
    CheckCompositionCoordinatePolicies();
    CheckTrayMenuCompositionHeadroom();
    CheckRenderedGuideContentCentering();
    CheckRetainedTrayInvalidation();
    CheckFixedChromeWindowPolicy();
    widgetrail::shell::FillColorKeyRoundedRectangle(nullptr, {}, nullptr);

    wic.Reset();
    d2d.Reset();
    CoUninitialize();
    std::cout << "OverlayChromeTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}
