#include "AccessibilityTree.h"
#include "FocusNavigation.h"
#include "LauncherExperienceAdapter.h"
#include "LauncherExperienceProjection.h"
#include "LauncherExperiencePresentation.h"

#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <string_view>

namespace {

using Microsoft::WRL::ComPtr;
using gba::WidgetNode;
using gba::WidgetSnapshot;
using gba::declarative::Rect;
using namespace gba::launcher;

int checks{};

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void Near(const float actual, const float expected, const std::string_view message) {
    Check(std::abs(actual - expected) <= 0.05F, message);
}

bool Contains(const Rect outer, const Rect inner) {
    return inner.x >= outer.x - 0.05F && inner.y >= outer.y - 0.05F &&
        inner.x + inner.width <= outer.x + outer.width + 0.05F &&
        inner.y + inner.height <= outer.y + outer.height + 0.05F;
}

WidgetNode Node(const wchar_t* id, const wchar_t* kind, const wchar_t* label = L"") {
    WidgetNode result;
    result.id = id;
    result.kind = kind;
    result.text = label;
    result.accessibilityLabel = label;
    return result;
}

gba::WidgetStyleValue Length(const double value) {
    return {L"length", std::to_wstring(value) + L"px", value, L"px"};
}

gba::WidgetStyleValue Number(const double value) {
    return {L"number", std::to_wstring(value), value, {}};
}

gba::WidgetStyleValue Color(const wchar_t* value) {
    return {L"color", value, {}, {}};
}

std::vector<std::uint8_t> EncodeSinglePixel(
    IWICImagingFactory* factory,
    const GUID& container) {
    ComPtr<IStream> stream;
    Check(SUCCEEDED(CreateStreamOnHGlobal(nullptr, TRUE, stream.ReleaseAndGetAddressOf())),
          "create in-memory image stream");
    ComPtr<IWICBitmapEncoder> encoder;
    Check(SUCCEEDED(factory->CreateEncoder(
        container, nullptr, encoder.ReleaseAndGetAddressOf())),
        "create static image encoder");
    Check(SUCCEEDED(encoder->Initialize(stream.Get(), WICBitmapEncoderNoCache)),
          "initialize static image encoder");
    ComPtr<IWICBitmapFrameEncode> frame;
    Check(SUCCEEDED(encoder->CreateNewFrame(frame.ReleaseAndGetAddressOf(), nullptr)),
          "create static image frame");
    Check(SUCCEEDED(frame->Initialize(nullptr)), "initialize static image frame");
    Check(SUCCEEDED(frame->SetSize(1, 1)), "size static image frame");
    WICPixelFormatGUID format = GUID_WICPixelFormat32bppBGRA;
    Check(SUCCEEDED(frame->SetPixelFormat(&format)), "set static image pixel format");
    ComPtr<IWICBitmap> source;
    Check(SUCCEEDED(factory->CreateBitmap(
        1, 1, GUID_WICPixelFormat32bppBGRA, WICBitmapCacheOnLoad,
        source.ReleaseAndGetAddressOf())), "create static image source");
    WICRect area{0, 0, 1, 1};
    ComPtr<IWICBitmapLock> lock;
    Check(SUCCEEDED(source->Lock(
        &area, WICBitmapLockWrite, lock.ReleaseAndGetAddressOf())),
        "lock static image source");
    UINT size{};
    BYTE* pixels{};
    Check(SUCCEEDED(lock->GetDataPointer(&size, &pixels)) && size >= 4,
          "write static image source");
    pixels[0] = 0x20;
    pixels[1] = 0x70;
    pixels[2] = 0xE0;
    pixels[3] = 0xFF;
    lock.Reset();
    Check(SUCCEEDED(frame->WriteSource(source.Get(), nullptr)), "encode static image source");
    Check(SUCCEEDED(frame->Commit()) && SUCCEEDED(encoder->Commit()),
          "commit static image source");
    HGLOBAL memory{};
    Check(SUCCEEDED(GetHGlobalFromStream(stream.Get(), &memory)) && memory,
          "read encoded static image memory");
    const auto byteCount = GlobalSize(memory);
    const auto* bytes = static_cast<const std::uint8_t*>(GlobalLock(memory));
    Check(bytes && byteCount > 0, "lock encoded static image memory");
    std::vector<std::uint8_t> result(bytes, bytes + byteCount);
    GlobalUnlock(memory);
    return result;
}

std::shared_ptr<const DecodedLauncherAsset> SolidAsset(
    const wchar_t* id,
    const wchar_t* revision,
    const std::array<std::uint8_t, 4> pixel) {
    auto result = std::make_shared<DecodedLauncherAsset>();
    result->opaqueAssetId = id;
    result->revision = revision;
    result->width = 1;
    result->height = 1;
    result->stride = 4;
    result->premultipliedBgra.assign(pixel.begin(), pixel.end());
    return result;
}

WidgetSnapshot Content(const Slot slot) {
    WidgetSnapshot result;
    result.sequence = 7;
    result.instanceId = L"launcher.fixture";
    result.activeInputScopeId = L"launcher-root";
    switch (slot) {
    case Slot::HeroBackground: {
        result.root = Node(L"launcher.hero", L"image", L"Selected game artwork unavailable");
        break;
    }
    case Slot::GameRail: {
        result.root = Node(L"launcher.game-rail", L"row");
        result.root.inputScopeId = L"launcher-root";
        for (int index = 0; index < 3; ++index) {
            auto game = Node(
                (L"launcher.game." + std::to_wstring(index)).c_str(), L"button",
                index == 0 ? L"A deliberately long selected game title" : L"Installed game");
            game.actionId = L"host.launch.exact-saved-id";
            game.baseStyle.emplace(L"width", Length(96));
            game.baseStyle.emplace(L"height", Length(48));
            result.root.children.push_back(std::move(game));
        }
        result.initialFocusId = L"launcher.game.0";
        break;
    }
    case Slot::DetailsPanel: {
        result.root = Node(L"launcher.details", L"column");
        auto title = Node(L"launcher.details.title", L"text", L"A deliberately long selected game title");
        auto action = Node(L"launcher.primary-action", L"button", L"Play installed game");
        action.actionId = L"host.launch.exact-saved-id";
        result.root.children = {title, action};
        break;
    }
    case Slot::SourceStatus:
        result.root = Node(L"launcher.source-status", L"text", L"Installed - source available");
        break;
    case Slot::OperationStatus:
        result.root = Node(L"launcher.operation-status", L"text", L"No active operation");
        break;
    case Slot::ControllerHints: {
        result.root = Node(L"launcher.controller-hints", L"row");
        auto back = Node(L"launcher.back", L"button", L"B Back");
        back.actionId = L"host.back";
        auto select = Node(L"launcher.select", L"button", L"A Select");
        select.actionId = L"host.activate-focused";
        result.root.children = {back, select};
        break;
    }
    case Slot::CollectionTabs:
        result.root = Node(L"launcher.collection-tabs", L"button", L"All installed");
        result.root.actionId = L"host.collection.next";
        break;
    case Slot::SystemStatus:
        result.root = Node(L"launcher.system-status", L"text", L"System status");
        break;
    }
    if (result.root.inputScopeId.empty()) result.root.inputScopeId = L"launcher-root";
    return result;
}

std::vector<SlotContent> FixtureContents() {
    std::vector<SlotContent> result;
    for (const auto slot : {Slot::HeroBackground, Slot::GameRail, Slot::DetailsPanel,
                            Slot::SourceStatus, Slot::ControllerHints})
        result.push_back({slot, Content(slot)});
    return result;
}

const WidgetNode* FindNode(
    const WidgetNode& root,
    const std::wstring_view id) {
    if (root.id == id) return &root;
    for (const auto& child : root.children) {
        if (const auto* found = FindNode(child, id)) return found;
    }
    return nullptr;
}

WidgetSnapshot ProjectedProductionSnapshot(
    const std::wstring_view profile = L"hero-rail") {
    WidgetSnapshot result;
    result.sequence = 145;
    result.instanceId = L"game-launcher.instance.exact";
    result.activeInputScopeId = L"game-launcher.scope.exact";
    result.initialFocusId = L"launcher.game.0";
    result.surface = gba::WidgetSurfaceHints{L"wide", 1180, 700, 640, 360};
    result.advancedPresentationKind = L"launcherExperience";
    result.advancedPresentationPreset = profile == L"hero-rail" ? L"heroRail" :
        profile == L"cover-wall" ? L"coverWall" :
        profile == L"compact-grid" ? L"compactGrid" : std::wstring{profile};
    result.root = Node(L"game-launcher.root.exact", L"stack");
    result.root.inputScopeId = result.activeInputScopeId;
    result.root.styleClasses = {L"ordinary-author-class"};

    const auto addSlot = [&](const Slot slot, const wchar_t* semanticSlot) {
        auto child = Content(slot).root;
        const auto rewriteScope = [&](const auto& self, WidgetNode& node) -> void {
            node.inputScopeId = result.activeInputScopeId;
            for (auto& descendant : node.children) self(self, descendant);
        };
        rewriteScope(rewriteScope, child);
        child.advancedPresentationSlot = semanticSlot;
        result.root.children.push_back(std::move(child));
    };
    addSlot(Slot::DetailsPanel, L"detailsPanel");
    addSlot(Slot::GameRail, L"primaryCollection");
    addSlot(Slot::CollectionTabs, L"collectionNavigation");
    addSlot(Slot::SourceStatus, L"sourceStatus");
    addSlot(Slot::OperationStatus, L"operationStatus");
    addSlot(Slot::ControllerHints, L"controllerHints");

    auto& rail = result.root.children[1];
    rail.kind = L"scroll";
    rail.scrollAxis = L"horizontal";
    rail.scrollNearStartActionId = L"game-launcher.page.before.exact";
    rail.scrollNearEndActionId = L"game-launcher.page.after.exact";
    rail.scrollPaginationThreshold = 2;
    rail.collectionAnchorKey = L"saved-id-anchor-exact";
    rail.children[0].collectionItemKey = L"saved-id-0-exact";
    rail.children[1].collectionItemKey = L"saved-id-1-exact";
    rail.children[2].collectionItemKey = L"saved-id-2-exact";
    auto firstArtwork = Node(L"launcher.game.0.artwork", L"image", L"Game zero artwork");
    firstArtwork.artworkHandle = L"library.art.00000000000000000000000000000000";
    rail.children[0].children.push_back(std::move(firstArtwork));
    auto secondArtwork = Node(L"launcher.game.1.artwork", L"image", L"Game one artwork");
    secondArtwork.artworkHandle = L"library.art.11111111111111111111111111111111";
    rail.children[1].children.push_back(std::move(secondArtwork));
    return result;
}

WidgetSnapshot ProjectedArtworkMatrixSnapshot(const std::size_t count = 32) {
    auto result = ProjectedProductionSnapshot();
    auto& details = result.root.children[0];
    auto heroArtwork = Node(
        L"matrix.hero.artwork", L"image", L"Selected artwork");
    heroArtwork.artworkHandle =
        L"library.art.ffffffffffffffffffffffffffffffff";
    heroArtwork.baseStyle.emplace(L"min-width", Length(220));
    heroArtwork.baseStyle.emplace(L"min-height", Length(128));
    details.children.push_back(std::move(heroArtwork));

    auto& rail = result.root.children[1];
    rail.children.clear();
    for (std::size_t index = 0; index < count; ++index) {
        const auto suffix = std::to_wstring(index);
        auto game = Node(
            (L"matrix.game." + suffix).c_str(), L"actionSurface",
            (L"Matrix game " + suffix).c_str());
        game.actionId = L"matrix.launch.saved." + suffix;
        game.actionSurfaceOrientation = L"vertical";
        game.collectionItemKey = L"matrix.saved." + suffix;
        game.inputScopeId = result.activeInputScopeId;
        game.baseStyle.emplace(L"min-height", Length(150));
        auto artwork = Node(
            (L"matrix.game." + suffix + L".artwork").c_str(),
            L"image", L"Trusted game artwork");
        wchar_t handle[64]{};
        swprintf_s(
            handle, L"library.art.%032llx",
            static_cast<unsigned long long>(index + 1));
        artwork.artworkHandle = handle;
        artwork.inputScopeId = result.activeInputScopeId;
        auto title = Node(
            (L"matrix.game." + suffix + L".title").c_str(),
            L"text", (L"Matrix game " + suffix).c_str());
        title.inputScopeId = result.activeInputScopeId;
        game.children = {std::move(artwork), std::move(title)};
        rail.children.push_back(std::move(game));
    }
    result.initialFocusId = L"matrix.game.0";
    rail.collectionAnchorKey = L"matrix.saved.0";
    rail.scrollNearStartActionId = L"matrix.page.before";
    rail.scrollNearEndActionId = L"matrix.page.after";
    return result;
}

std::optional<gba::WidgetAdvancedPresentationDeclaration>
AdvancedPresentationDeclaration(const int schemaVersion = 1) {
    return gba::WidgetAdvancedPresentationDeclaration{
        schemaVersion, L"launcherExperience"};
}

void ReidentifySnapshot(WidgetSnapshot& snapshot, const std::wstring_view prefix) {
    const auto rewrite = [&](std::wstring& value) {
        if (!value.empty()) value = std::wstring(prefix) + L"." + value;
    };
    rewrite(snapshot.instanceId);
    rewrite(snapshot.activeInputScopeId);
    rewrite(snapshot.initialFocusId);
    const auto visit = [&](const auto& self, WidgetNode& node) -> void {
        rewrite(node.id);
        rewrite(node.inputScopeId);
        rewrite(node.focusUp);
        rewrite(node.focusDown);
        rewrite(node.focusLeft);
        rewrite(node.focusRight);
        for (auto& child : node.children) self(self, child);
    };
    visit(visit, snapshot.root);
}

Recipe LeftRailRecipe(const bool heroLast = false) {
    auto leaf = [](const Slot slot, const NormalizedRect region,
                   const std::optional<Orientation> orientation = {},
                   const std::optional<Surface> surface = {}) {
        RecipeNode node;
        node.slot = slot;
        node.region = region;
        node.orientation = orientation;
        node.surface = surface;
        return node;
    };
    const auto profile = [&](const bool compact) {
        RecipeNode root;
        root.type = Primitive::Overlay;
        auto hero = leaf(Slot::HeroBackground, {0, 0, 1, 1});
        root.children = compact
            ? std::vector<RecipeNode>{
                hero,
                leaf(Slot::SourceStatus, {0.05F, 0.04F, 0.42F, 0.11F}),
                leaf(Slot::DetailsPanel, {0.05F, 0.18F, 0.9F, 0.22F}, {}, Surface::Glass),
                leaf(Slot::GameRail, {0.05F, 0.44F, 0.9F, 0.34F}, Orientation::Vertical),
                leaf(Slot::ControllerHints, {0.5F, 0.82F, 0.45F, 0.15F}),
            }
            : std::vector<RecipeNode>{
                hero,
                leaf(Slot::GameRail, {0.04F, 0.08F, 0.22F, 0.78F}, Orientation::Vertical),
                leaf(Slot::DetailsPanel, {0.32F, 0.18F, 0.47F, 0.5F}, {}, Surface::Glass),
                leaf(Slot::SourceStatus, {0.81F, 0.08F, 0.15F, 0.12F}),
                leaf(Slot::ControllerHints, {0.58F, 0.9F, 0.38F, 0.07F}),
            };
        if (heroLast) {
            root.children.erase(std::find_if(root.children.begin(), root.children.end(),
                [](const RecipeNode& node) { return node.slot == Slot::HeroBackground; }));
            root.children.push_back(hero);
        }
        return root;
    };
    Recipe recipe;
    recipe.branches.emplace(Branch::Compact, profile(true));
    recipe.branches.emplace(Branch::Standard, profile(false));
    recipe.branches.emplace(Branch::Wide, profile(false));
    return recipe;
}

void ResponsiveProfilesStayInsideWorkArea() {
    const std::array profiles{
        std::pair{Rect{0, 0, 854, 480}, 1.0F},
        std::pair{Rect{0, 0, 1100, 650}, 1.0F},
        std::pair{Rect{0, 0, 1280, 720}, 1.0F},
        std::pair{Rect{0, 0, 1280, 680}, 1.0F},
        std::pair{Rect{0, 0, 1920, 1040}, 1.0F},
        std::pair{Rect{0, 0, 1280, 720}, 1.5F},
    };
    for (const auto preset : {Preset::HeroRail, Preset::CoverWall, Preset::Carousel, Preset::CompactGrid}) {
        for (const auto& [workArea, scale] : profiles) {
            const auto layout = ResolveLayout(nullptr, preset, workArea, scale);
            if (!layout.valid()) {
                std::cerr << "profile " << static_cast<int>(preset) << " "
                          << workArea.width << "x" << workArea.height << " scale=" << scale;
                for (const auto& diagnostic : layout.diagnostics)
                    std::cerr << " " << diagnostic.path << ":" << diagnostic.code;
                std::cerr << '\n';
            }
            Check(layout.valid(), "built-in layout validates for bounded profile");
            Check(!layout.usedFallback, "built-in selection does not report recovery");
            Check(layout.semanticPlacements.size() >= 4, "critical semantic slots are retained");
            for (const auto& placement : layout.paintPlacements)
                Check(Contains(workArea, placement.bounds), "slot remains within live work area");
        }
    }
}

void InvalidRecipeFallsBackAtomically() {
    auto invalid = LeftRailRecipe();
    invalid.branches[Branch::Wide].children[1].region.width = 1.4F;
    const auto layout = ResolveLayout(&invalid, Preset::CompactGrid, {0, 0, 1920, 1040});
    Check(layout.valid() && layout.usedFallback, "incompatible recipe uses matching built-in fallback");
    Check(std::any_of(layout.fallbackDiagnostics.begin(), layout.fallbackDiagnostics.end(),
        [](const auto& item) { return item.code == "out_of_bounds"; }),
        "fallback retains the rejected recipe diagnostic");
    Check(layout.preset == Preset::CompactGrid, "fallback preserves selected recovery preset");
    Check(layout.Find(Slot::GameRail) && layout.Find(Slot::ControllerHints),
          "fallback retains rail and Back affordance slots");
}

void StaticAssetsDecodeWithinTheHostBoundary() {
    ComPtr<IWICImagingFactory> wic;
    Check(SUCCEEDED(CoCreateInstance(
        CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
        "create WIC factory for sealed launcher assets");
    const auto png = EncodeSinglePixel(wic.Get(), GUID_ContainerFormatPng);
    const auto jpeg = EncodeSinglePixel(wic.Get(), GUID_ContainerFormatJpeg);
    const auto pngResult = DecodeSealedLauncherAsset(
        wic.Get(), {L"pack-background", L"pack:1", StaticImageFormat::Png, png});
    const auto jpegResult = DecodeSealedLauncherAsset(
        wic.Get(), {L"pack-preview", L"pack:1", StaticImageFormat::Jpeg, jpeg});
    const std::vector<std::uint8_t> webp{
        0x52,0x49,0x46,0x46,0x1A,0x00,0x00,0x00,0x57,0x45,0x42,0x50,
        0x56,0x50,0x38,0x4C,0x0D,0x00,0x00,0x00,0x2F,0x00,0x00,0x00,
        0x10,0x07,0x10,0x11,0x11,0x88,0x88,0xFE,0x07,0x00};
    const auto webpResult = DecodeSealedLauncherAsset(
        wic.Get(), {L"pack-webp", L"pack:1", StaticImageFormat::WebP, webp});
    Check(pngResult.succeeded() && pngResult.asset->width == 1 &&
          pngResult.asset->premultipliedBgra.size() == 4,
          "bounded PNG bytes decode to sealed premultiplied pixels");
    Check(jpegResult.succeeded() && jpegResult.asset->height == 1 &&
          jpegResult.asset->premultipliedBgra.size() == 4,
          "bounded JPEG bytes decode to sealed premultiplied pixels");
    Check(webpResult.succeeded() && webpResult.asset->width == 1 &&
          webpResult.asset->height == 1,
          "bounded standard VP8L WebP decodes through the host media owner");
    Check(!DecodeSealedLauncherAsset(
              wic.Get(), {L"mismatch", L"pack:1", StaticImageFormat::WebP, png}).succeeded(),
          "declared WebP cannot smuggle PNG bytes");
    Check(!DecodeSealedLauncherAsset(
              wic.Get(), {L"corrupt", L"pack:1", StaticImageFormat::WebP,
                          {'R','I','F','F',0,0,0,0,'W','E','B','P'}}).succeeded(),
          "corrupt WebP retains the professional fallback");
    std::vector<std::uint8_t> oversized(MaximumLauncherAssetBytes + 1, 0);
    Check(!DecodeSealedLauncherAsset(
              wic.Get(), {L"oversized", L"pack:1", StaticImageFormat::Png,
                          std::move(oversized)}).succeeded(),
          "encoded asset bound is enforced before WIC decode");
}

void ScopedCascadeAccessibilityAndRecoveryStayAtomic() {
    LauncherExperiencePresentationOwner owner;
    LauncherPresentationRequest first;
    first.revision = L"dev.example.deep-space:1.0.0:digest-a";
    first.preset = Preset::HeroRail;
    first.backgroundMode = BackgroundMode::PackAsset;
    first.focusEffect = FocusEffect::Lift;
    first.packBackground = SolidAsset(L"pack.background", first.revision.c_str(), {0x20, 0x40, 0x80, 0xFF});
    first.packStyles[Slot::GameRail].emplace(L"background", Color(L"#203050ff"));
    first.packStyles[Slot::GameRail].emplace(L"background-blur", Length(12));
    first.userStyles[Slot::GameRail].emplace(L"background", Color(L"#405080ff"));
    std::wstring diagnostic;
    Check(owner.Activate(first, {}, 100, diagnostic),
          "valid launcher-only pack revision activates atomically");
    auto frame = owner.Sample(100);
    Check(!frame.builtIn && frame.currentBackground == first.packBackground &&
          frame.backgroundBlurEnabled,
          "sealed pack background and bounded effects publish together");
    Check(frame.slotStyles.at(Slot::GameRail).at(L"background").text == L"#405080ff",
          "launcher user adjustment follows the pack layer");
    auto rail = Content(Slot::GameRail);
    auto details = Content(Slot::DetailsPanel);
    ApplyLauncherPresentationStyles(
        Slot::GameRail, rail, frame, L"launcher.game.0");
    ApplyLauncherPresentationStyles(
        Slot::DetailsPanel, details, frame, L"launcher.game.0");
    Check(rail.root.baseStyle.contains(L"background") &&
          !details.root.baseStyle.contains(L"background"),
          "pack rules cannot escape their launcher semantic role");
    Check(rail.root.children.front().focusedStyle.contains(L"scale") &&
          rail.root.children.front().focusedStyle.contains(L"translate-y"),
          "bounded lift effect targets only the exact focused game");

    LauncherPresentationRequest second = first;
    second.revision = L"dev.example.deep-space:1.1.0:digest-b";
    second.backgroundMode = BackgroundMode::SelectedGameArtwork;
    second.selectedGameArtworkRevision = L"game-art:42";
    second.selectedGameBackground = SolidAsset(
        L"selected-game.background", L"game-art:42", {0x80, 0x40, 0x20, 0xFF});
    Check(owner.Activate(second, {}, 200, diagnostic),
          "decoded revision-bound game art stages before experience publication");
    const auto midpoint = owner.Sample(290);
    Near(midpoint.previousBackgroundOpacity, 0.5F,
         "previous background remains during bounded crossfade");
    Near(midpoint.currentBackgroundOpacity, 0.5F,
         "new background becomes visible only after successful decode");
    auto missingArtwork = second;
    missingArtwork.revision = L"dev.example.deep-space:1.2.0:digest-c";
    missingArtwork.selectedGameArtworkRevision = L"game-art:43";
    missingArtwork.selectedGameBackground.reset();
    Check(owner.Activate(missingArtwork, {}, 380, diagnostic),
          "experience styling remains usable when selected artwork is unavailable");
    Check(owner.Sample(380).currentBackground == second.selectedGameBackground,
          "failed selected-game decode retains the prior professional background");

    owner.RecordFrameTiming(2.0, 19.0);
    Check(owner.Sample(290).effectQuality == EffectQuality::OpacityOnly,
          "render-budget pressure removes focus transforms before input latency");
    owner.RecordFrameTiming(9.0, 4.0);
    const auto degraded = owner.Sample(290);
    Check(degraded.effectQuality == EffectQuality::Immediate &&
          !degraded.focusedGameStyle.contains(L"scale") &&
          degraded.previousBackgroundOpacity == 0.0F,
          "input-budget pressure makes presentation effects immediate");
    Check(owner.metrics().degradedFrameCount == 2 &&
          owner.metrics().maximumInputDispatchMilliseconds == 9.0,
          "bounded frame metrics retain the measured degradation evidence");

    LauncherExperiencePresentationOwner accessibleOwner;
    Check(accessibleOwner.Activate(
              first, {.reducedMotion = true, .reducedTransparency = true,
                      .highContrast = true}, 0, diagnostic),
          "accessibility-final launcher revision activates");
    const auto accessible = accessibleOwner.Sample(0);
    Check(!accessible.backgroundBlurEnabled && accessible.backgroundIsFallback &&
          !accessible.focusedGameStyle.contains(L"scale") &&
          !accessible.focusedGameStyle.contains(L"translate-y") &&
          accessible.focusedGameStyle.at(L"outline-width").number == 3.0,
          "high contrast and reduced preferences override every pack effect");

    LauncherExperiencePresentationOwner globalOwner;
    auto global = first;
    global.useGlobalAppearance = true;
    Check(globalOwner.Activate(global, {}, 0, diagnostic),
          "Use global appearance activates without pack styling");
    const auto globalFrame = globalOwner.Sample(0);
    Check(globalFrame.slotStyles.empty() && globalFrame.focusedGameStyle.empty() &&
          globalFrame.backgroundIsFallback,
          "Use global appearance reproduces the pre-pack style path");

    for (int attempt = 0; attempt < 3; ++attempt)
        owner.RejectRevision(second.revision, Preset::HeroRail, L"decode failed");
    Check(owner.IsRevisionDisabled(second.revision) &&
          !owner.IsRevisionDisabled(first.revision) && owner.Sample(400).builtIn,
          "repeated failure disables only the offending revision and restores built-in");
    auto safeStart = first;
    safeStart.safeStart = true;
    Check(owner.Activate(safeStart, {}, 500, diagnostic) && owner.Sample(500).builtIn,
          "safe-start bypass selects the code-owned recovery experience");
}

void RenderedSlotsSharePaintPointerFocusAndUiaGeometry() {
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
        1920, 1080, GUID_WICPixelFormat32bppPBGRA,
        WICBitmapCacheOnLoad, canvas.ReleaseAndGetAddressOf())),
        "create WIC canvas");
    ComPtr<ID2D1RenderTarget> target;
    Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
        canvas.Get(), D2D1::RenderTargetProperties(), target.ReleaseAndGetAddressOf())),
        "create WIC render target");

    gba::DeclarativeRenderer renderer{d2d.Get(), write.Get(), nullptr};
    auto contents = FixtureContents();
    const std::array profiles{
        std::pair{Rect{0, 0, 854, 480}, 1.0F},
        std::pair{Rect{0, 0, 1100, 650}, 1.0F},
        std::pair{Rect{0, 0, 1280, 720}, 1.0F},
        std::pair{Rect{0, 0, 1280, 680}, 1.0F},
        std::pair{Rect{0, 0, 1920, 1040}, 1.0F},
        std::pair{Rect{0, 0, 1280, 720}, 1.5F},
    };
    const auto verify = [&](const Recipe* recipe, const Preset preset,
                            const Rect workArea, const float textScale) {
        gba::DeclarativeRenderOptions options;
        options.collectAccessibility = true;
        options.accessibility.textScale = textScale;
        target->BeginDraw();
        target->Clear(D2D1::ColorF(0.02F, 0.02F, 0.03F, 1));
        auto experience = RenderExperience(
            renderer, target.Get(), recipe, preset, workArea,
            contents, L"launcher.game.0", options);
        Check(SUCCEEDED(target->EndDraw()), "complete launcher profile draw");
        Check(experience.layout.valid() && !experience.layout.usedFallback,
              "profile renders without recovery");
        Check(experience.render.succeeded,
              "all slot contents use the production declarative renderer");
        Check(experience.render.focusRects.contains(L"launcher.game.0"),
              "focused game has painted focus geometry");
        const auto focus = experience.render.focusRects.at(L"launcher.game.0");
        const auto hit = std::find_if(
            experience.render.hitRegions.begin(), experience.render.hitRegions.end(),
            [](const auto& item) { return item.nodeId == L"launcher.game.0"; });
        const auto uia = std::find_if(
            experience.render.accessibilityRegions.begin(),
            experience.render.accessibilityRegions.end(),
            [](const auto& item) { return item.nodeId == L"launcher.game.0"; });
        Check(hit != experience.render.hitRegions.end() &&
              uia != experience.render.accessibilityRegions.end(),
              "focused game has pointer and accessibility geometry");
        Near(focus.x, hit->rect.x, "painted focus and pointer x agree");
        Near(focus.y, hit->rect.y, "painted focus and pointer y agree");
        Near(focus.width, uia->rect.width, "painted focus and UIA width agree");
        Near(focus.height, uia->rect.height, "painted focus and UIA height agree");
        Check(Contains(workArea, focus), "focus stays in the live work area");
        Check(Contains(experience.layout.Find(Slot::GameRail)->bounds, focus),
              "focused game remains inside the recipe rail");
        for (const auto& region : experience.render.hitRegions)
            Check(Contains(workArea, region.rect), "pointer target stays in work area");
        for (const auto& region : experience.render.accessibilityRegions)
            Check(Contains(workArea, region.rect), "UIA target stays in work area");
        const auto pointer = gba::input::FindPointerHitTarget(
            focus.x + focus.width / 2, focus.y + focus.height / 2,
            L"launcher-root", experience.render);
        Check(pointer && pointer->id == L"launcher.game.0" && pointer->enabled,
              "pointer resolves the same host-owned exact game action");
        const auto rail = experience.layout.Find(Slot::GameRail);
        const auto direction = rail->orientation == Orientation::Vertical
            ? gba::input::NavigationDirection::Down
            : gba::input::NavigationDirection::Right;
        Check(gba::input::FindGeometricFocusTarget(
                  L"launcher.game.0", direction, experience.render) == L"launcher.game.1",
              "controller and keyboard traverse the recipe rail orientation");

        const auto tree = gba::accessibility::BuildWidgetTree(
            L"game-launcher", L"fixture-generation", experience.semanticSnapshot,
            experience.render, L"launcher.game.0");
        Check(tree.focusedNode && tree.nodes[*tree.focusedNode].id == L"launcher.game.0",
              "UIA focus matches painted/controller focus");
        Check(std::any_of(tree.nodes.begin(), tree.nodes.end(), [](const auto& node) {
            return node.id == L"launcher.back" && node.actionId == L"host.back";
        }), "Back remains reachable through host-owned semantics");
        Check(std::any_of(tree.nodes.begin(), tree.nodes.end(), [](const auto& node) {
            return node.id == L"launcher.primary-action" &&
                node.actionId == L"host.launch.exact-saved-id";
        }), "primary action remains host-owned and reachable");
        const auto hasHero = experience.layout.Find(Slot::HeroBackground) != nullptr;
        Check(experience.semanticSnapshot.root.children.size() == (hasHero ? 5 : 4),
              "host semantic slot count is stable");
        Check(experience.semanticSnapshot.root.children[0].id == L"launcher.game-rail" &&
              experience.semanticSnapshot.root.children.back().id ==
                  (hasHero ? L"launcher.hero" : L"launcher.controller-hints"),
              "UIA slot order is canonical and independent of paint order");
        Check(experience.semanticSnapshot.root.children[0].kind ==
                  (rail->orientation == Orientation::Vertical ? L"column" : L"row"),
              "paint and semantic rail orientation agree");
        if (recipe) {
            const auto details = experience.layout.Find(Slot::DetailsPanel);
            Check(details && details->surface == Surface::Glass,
                  "reference left rail retains its independent glass details panel");
            Check(rail->orientation == Orientation::Vertical,
                  "reference left rail remains vertical in every responsive branch");
        }
        return experience;
    };

    for (const auto preset :
         {Preset::HeroRail, Preset::CoverWall, Preset::Carousel, Preset::CompactGrid})
        for (const auto& [workArea, scale] : profiles)
            (void)verify(nullptr, preset, workArea, scale);

    auto leftRail = LeftRailRecipe();
    for (const auto& [workArea, scale] : profiles)
        (void)verify(&leftRail, Preset::HeroRail, workArea, scale);

    auto reordered = LeftRailRecipe(true);
    const auto first = verify(&leftRail, Preset::HeroRail, {0, 0, 1280, 720}, 1.0F);
    const auto second = verify(&reordered, Preset::HeroRail, {0, 0, 1280, 720}, 1.0F);
    Near(second.render.focusRects.at(L"launcher.game.0").x,
         first.render.focusRects.at(L"launcher.game.0").x,
         "z-order does not change semantic focus geometry");
    const auto firstTree = gba::accessibility::BuildWidgetTree(
        L"game-launcher", L"fixture-generation", first.semanticSnapshot,
        first.render, L"launcher.game.0");
    const auto secondTree = gba::accessibility::BuildWidgetTree(
        L"game-launcher", L"fixture-generation", second.semanticSnapshot,
        second.render, L"launcher.game.0");
    Check(firstTree.nodes.size() == secondTree.nodes.size(),
          "z-order does not change UIA semantic count or order");
    for (std::size_t index = 0; index < firstTree.nodes.size(); ++index)
        Check(firstTree.nodes[index].id == secondTree.nodes[index].id,
              "z-order remains independent from accessibility order");
}

void ProductionProjectionAdmitsOnlyTheDeclaredSemanticContract() {
    ComPtr<ID2D1Factory> d2d;
    Check(SUCCEEDED(D2D1CreateFactory(
        D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf())),
        "create projection D2D factory");
    ComPtr<IDWriteFactory> write;
    Check(SUCCEEDED(DWriteCreateFactory(
        DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
        reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))),
        "create projection DirectWrite factory");
    ComPtr<IWICImagingFactory> wic;
    Check(SUCCEEDED(CoCreateInstance(
        CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
        "create projection WIC factory");
    ComPtr<IWICBitmap> canvas;
    Check(SUCCEEDED(wic->CreateBitmap(
        1920, 1080, GUID_WICPixelFormat32bppPBGRA,
        WICBitmapCacheOnLoad, canvas.ReleaseAndGetAddressOf())),
        "create projection WIC canvas");
    ComPtr<ID2D1RenderTarget> target;
    Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
        canvas.Get(), D2D1::RenderTargetProperties(),
        target.ReleaseAndGetAddressOf())),
        "create projection render target");

    gba::DeclarativeRenderer renderer{d2d.Get(), write.Get(), nullptr};
    LauncherExperienceProjection projection;
    const std::array profiles{
        std::pair{L"hero-rail", Preset::HeroRail},
        std::pair{L"cover-wall", Preset::CoverWall},
        std::pair{L"carousel", Preset::Carousel},
        std::pair{L"compact-grid", Preset::CompactGrid},
    };
    const std::array viewports{
        std::pair{Rect{13, 17, 854, 480}, 1.0F},
        std::pair{Rect{21, 19, 1100, 650}, 1.0F},
        std::pair{Rect{17, 23, 1280, 720}, 1.5F},
        std::pair{Rect{0, 0, 1920, 1040}, 1.0F},
    };
    for (const auto& [profile, expectedPreset] : profiles) {
        for (const auto& [viewport, textScale] : viewports) {
            auto snapshot = ProjectedProductionSnapshot(profile);
            gba::DeclarativeRenderOptions options;
            options.collectAccessibility = true;
            options.pixelScale = 1.25F;
            options.accessibility.textScale = textScale;
            options.responsiveViewport = {viewport.width, viewport.height};
            target->BeginDraw();
            target->Clear(D2D1::ColorF(0.02F, 0.02F, 0.03F, 1));
            auto adopted = projection.Render(
                renderer, target.Get(), nullptr, AdvancedPresentationDeclaration(),
                L"presentation-generation-a", L"game-launcher", snapshot,
                L"launcher.game.0", viewport, options);
            Check(SUCCEEDED(target->EndDraw()),
                "complete production projection frame");
            Check(adopted.disposition == ProductionProjectionDisposition::Adopted &&
                  adopted.preset == expectedPreset && adopted.render.succeeded,
            "declared Community projection adopts its closed preset");

            const auto& semantic = projection.InteractionSnapshot(
                L"game-launcher", L"presentation-generation-a", snapshot);
            Check(semantic.sequence == snapshot.sequence &&
                  semantic.instanceId == snapshot.instanceId &&
                  semantic.activeInputScopeId == snapshot.activeInputScopeId &&
                  semantic.initialFocusId == snapshot.initialFocusId &&
                  semantic.root.id == snapshot.root.id &&
                  semantic.root.styleClasses == snapshot.root.styleClasses,
                "projection preserves snapshot and root identity");
            const auto canonicalScope = [&](const auto& self,
                                            const WidgetNode& node) -> bool {
                return node.inputScopeId == snapshot.activeInputScopeId &&
                    std::all_of(
                        node.children.begin(), node.children.end(),
                        [&](const auto& child) { return self(self, child); });
            };
            Check(canonicalScope(canonicalScope, semantic.root),
                "projection assigns one canonical active scope to every adopted slot descendant");
            const auto* rail = FindNode(semantic.root, L"launcher.game-rail");
            const auto* game = FindNode(semantic.root, L"launcher.game.0");
            const auto expectedDirection =
                (expectedPreset == Preset::CoverWall ||
                 expectedPreset == Preset::CompactGrid)
                ? std::wstring_view{L"down"}
                : std::wstring_view{L"right"};
            const auto projectedFocus = projection.ProjectedFocusTarget(
                L"game-launcher", L"launcher.game.0",
                expectedDirection);
            const auto* primary = FindNode(semantic.root, L"launcher.primary-action");
            Check(rail && rail->kind == L"scroll" &&
                  rail->scrollNearStartActionId == L"game-launcher.page.before.exact" &&
                  rail->scrollNearEndActionId == L"game-launcher.page.after.exact" &&
                  rail->scrollPaginationThreshold == 2 &&
                  rail->collectionAnchorKey == L"saved-id-anchor-exact",
                "projection preserves collection anchor and pagination semantics");
            Check(game && game->id == L"launcher.game.0" &&
                  game->actionId == L"host.launch.exact-saved-id" &&
                  game->collectionItemKey == L"saved-id-0-exact" &&
                  primary && primary->actionId == L"host.launch.exact-saved-id",
                "projection preserves exact game, action, and collection identities");
            Check(projectedFocus && *projectedFocus == L"launcher.game.1",
                "canonical projection exposes the preset-oriented internal game focus edge");
            Check(adopted.render.focusRects.contains(L"launcher.game.0"),
                "projection publishes exact focused game geometry");
            const auto focus = adopted.render.focusRects.at(L"launcher.game.0");
            const auto hit = std::find_if(
                adopted.render.hitRegions.begin(), adopted.render.hitRegions.end(),
                [](const auto& candidate) {
                    return candidate.nodeId == L"launcher.game.0";
                });
            const auto uia = std::find_if(
                adopted.render.accessibilityRegions.begin(),
                adopted.render.accessibilityRegions.end(),
                [](const auto& candidate) {
                    return candidate.nodeId == L"launcher.game.0";
                });
            Check(hit != adopted.render.hitRegions.end() &&
                  uia != adopted.render.accessibilityRegions.end() &&
                  Contains(viewport, focus) && Contains(viewport, hit->rect) &&
                  Contains(viewport, uia->rect),
                "production paint pointer focus and UIA share viewport-bounded geometry");
            Near(focus.x, hit->rect.x, "projection focus and pointer x agree");
            Near(focus.y, uia->rect.y, "projection focus and UIA y agree");
            const auto tree = gba::accessibility::BuildWidgetTree(
                L"game-launcher", L"production-generation", semantic,
                adopted.render, L"launcher.game.0");
            Check(tree.focusedNode &&
                  tree.nodes[*tree.focusedNode].id == L"launcher.game.0",
                "production UIA focus uses canonical projection geometry");
        }
    }

    const auto renderFallback = [&](WidgetSnapshot snapshot) {
        gba::DeclarativeRenderOptions options;
        options.collectAccessibility = true;
        target->BeginDraw();
        auto result = projection.Render(
            renderer, target.Get(), nullptr, AdvancedPresentationDeclaration(),
            L"presentation-generation-a", L"game-launcher", snapshot,
            L"launcher.game.0", {0, 0, 1280, 720}, options);
        Check(SUCCEEDED(target->EndDraw()), "complete ordinary fallback frame");
        Check(result.disposition == ProductionProjectionDisposition::Fallback &&
              result.render.succeeded,
            "malformed advanced projection atomically keeps ordinary content active");
        Check(std::count_if(
            result.render.diagnostics.begin(), result.render.diagnostics.end(),
            [](const auto& item) {
                return item.code == L"advanced_presentation_invalid";
            }) == 1, "malformed projection emits one bounded fallback diagnostic");
    };
    auto missing = ProjectedProductionSnapshot();
    missing.root.children.pop_back();
    renderFallback(std::move(missing));
    auto duplicate = ProjectedProductionSnapshot();
    duplicate.root.children[0].advancedPresentationSlot =
        L"primaryCollection";
    renderFallback(std::move(duplicate));
    auto unknown = ProjectedProductionSnapshot();
    unknown.advancedPresentationPreset = L"futureProfile";
    renderFallback(std::move(unknown));

    auto adapterFailure = ProjectedProductionSnapshot();
    projection.FailNextAdapterFrameForTesting();
    target->BeginDraw();
    auto recovered = projection.Render(
        renderer, target.Get(), nullptr, AdvancedPresentationDeclaration(),
        L"presentation-generation-a", L"game-launcher", adapterFailure,
        L"launcher.game.0", {0, 0, 1280, 720}, {});
    Check(SUCCEEDED(target->EndDraw()), "complete forced adapter recovery frame");
    Check(recovered.disposition == ProductionProjectionDisposition::Fallback &&
          recovered.render.succeeded &&
          std::count_if(
              recovered.render.diagnostics.begin(),
              recovered.render.diagnostics.end(),
              [](const auto& item) {
                  return item.code == L"launcher_projection_render_failed";
              }) == 1,
        "adapter failure commits no partial frame and retains usable ordinary content");
    Check(&projection.InteractionSnapshot(
              L"game-launcher", L"presentation-generation-a", adapterFailure) ==
              &adapterFailure,
        "adapter failure retires stale canonical interaction geometry");

    auto ordinary = ProjectedProductionSnapshot();
    ordinary.advancedPresentationKind.clear();
    ordinary.advancedPresentationPreset.clear();
    const auto clearSlots = [&](const auto& self, WidgetNode& node) -> void {
        node.advancedPresentationSlot.clear();
        for (auto& child : node.children) self(self, child);
    };
    clearSlots(clearSlots, ordinary.root);
    target->BeginDraw();
    auto ordinaryResult = projection.Render(
        renderer, target.Get(), nullptr, std::nullopt,
        L"presentation-generation-b", L"spotify", ordinary,
        L"launcher.game.0", {0, 0, 1280, 720}, {});
    Check(SUCCEEDED(target->EndDraw()), "complete non-launcher ordinary frame");
    Check(ordinaryResult.disposition == ProductionProjectionDisposition::Ordinary &&
          ordinaryResult.render.succeeded &&
          &projection.InteractionSnapshot(
              L"spotify", L"presentation-generation-b", ordinary) == &ordinary,
        "other widgets remain on the ordinary declarative path");

    auto randomCommunity = ProjectedProductionSnapshot();
    ReidentifySnapshot(randomCommunity, L"random-community");
    randomCommunity.root.styleClasses = {L"totally-different-style"};
    target->BeginDraw();
    auto randomAdopted = projection.Render(
        renderer, target.Get(), nullptr, AdvancedPresentationDeclaration(),
        L"random-package-generation", L"random-surface", randomCommunity,
        L"random-community.launcher.game.0", {0, 0, 1280, 720}, {});
    Check(SUCCEEDED(target->EndDraw()) &&
          randomAdopted.disposition == ProductionProjectionDisposition::Adopted &&
          randomAdopted.render.focusRects.contains(
              L"random-community.launcher.game.0"),
        "random Community identity and style adopt the same public presentation contract");

    auto replacement = randomCommunity;
    Check(&projection.InteractionSnapshot(
              L"random-surface", L"replacement-generation", replacement) ==
              &replacement,
        "package replacement generation cannot retain stale projected interaction authority");

    target->BeginDraw();
    auto missingDeclaration = projection.Render(
        renderer, target.Get(), nullptr, std::nullopt,
        L"missing-declaration", L"random-surface", randomCommunity,
        L"random-community.launcher.game.0", {0, 0, 1280, 720}, {});
    Check(SUCCEEDED(target->EndDraw()) &&
          missingDeclaration.disposition ==
              ProductionProjectionDisposition::Fallback &&
          missingDeclaration.render.succeeded,
        "missing declaration fails closed to the ordinary declarative surface");

    target->BeginDraw();
    auto incompatibleDeclaration = projection.Render(
        renderer, target.Get(), nullptr, AdvancedPresentationDeclaration(99),
        L"future-declaration", L"random-surface", randomCommunity,
        L"random-community.launcher.game.0", {0, 0, 1280, 720}, {});
    Check(SUCCEEDED(target->EndDraw()) &&
          incompatibleDeclaration.disposition ==
              ProductionProjectionDisposition::Fallback &&
          incompatibleDeclaration.render.succeeded,
        "incompatible declaration version fails closed to ordinary content");
}

void ProductionProjectionUsesOneAtomicPresentationFrame() {
    ComPtr<ID2D1Factory> d2d;
    Check(SUCCEEDED(D2D1CreateFactory(
        D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf())),
        "create motion D2D factory");
    ComPtr<IDWriteFactory> write;
    Check(SUCCEEDED(DWriteCreateFactory(
        DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
        reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))),
        "create motion DirectWrite factory");
    ComPtr<IWICImagingFactory> wic;
    Check(SUCCEEDED(CoCreateInstance(
        CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
        "create motion WIC factory");
    ComPtr<IWICBitmap> canvas;
    Check(SUCCEEDED(wic->CreateBitmap(
        1280, 720, GUID_WICPixelFormat32bppPBGRA,
        WICBitmapCacheOnLoad, canvas.ReleaseAndGetAddressOf())),
        "create motion WIC canvas");
    ComPtr<ID2D1RenderTarget> target;
    Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
        canvas.Get(), D2D1::RenderTargetProperties(), target.ReleaseAndGetAddressOf())),
        "create motion render target");

    constexpr std::wstring_view png =
        L"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ"
        L"AAAADUlEQVR42mP8z8BQDwAFgwJ/lK3xWQAAAABJRU5ErkJggg==";
    gba::RemoteImageCache cache(
        {}, {}, {}, [](std::wstring_view) { return true; });
    gba::DeclarativeRenderer renderer{d2d.Get(), write.Get(), &cache};
    LauncherExperienceProjection projection;
    const auto draw = [&](const std::wstring_view profile,
                          const std::wstring_view focus,
                          const std::uint64_t now,
                          const gba::NativeAccessibilityPolicy accessibility = {}) {
        auto snapshot = ProjectedProductionSnapshot(profile);
        gba::DeclarativeRenderOptions options;
        options.collectAccessibility = true;
        options.artworkWidgetId = L"game-launcher";
        options.animationTimestampMilliseconds = now;
        options.accessibility = accessibility;
        target->BeginDraw();
        auto result = projection.Render(
            renderer, target.Get(), &cache, AdvancedPresentationDeclaration(),
            L"motion-generation", L"game-launcher", snapshot,
            focus, {0, 0, 1280, 720}, options);
        Check(SUCCEEDED(target->EndDraw()) && result.render.succeeded,
            "complete immutable presentation frame");
        return result;
    };

    for (const auto profile : {L"hero-rail", L"cover-wall", L"carousel", L"compact-grid"}) {
        const auto first = draw(profile, L"launcher.game.0", 100);
        const auto firstKey = gba::RemoteImageCache::TrustedArtworkKey(
            L"game-launcher", L"launcher.game.0.artwork",
            L"library.art.00000000000000000000000000000000");
        Check(first.presentationActive &&
              (first.backgroundIsFallback || !first.backgroundFocusId.empty()),
            "pending trusted artwork keeps a complete fallback or last-good frame");
        if (!cache.GetReadyImage(firstKey)) {
            Check(cache.SupplyTrustedArtwork(
                L"game-launcher", L"library.art.00000000000000000000000000000000",
                std::wstring(png)), "supply first decoded trusted background");
        }
        for (int attempt = 0; attempt < 200 && !cache.GetReadyImage(firstKey); ++attempt)
            Sleep(5);
        Check(cache.GetReadyImage(firstKey) != nullptr,
            "first trusted background decodes before publication");
        const auto ready = draw(profile, L"launcher.game.0", 200);
        Check(!ready.backgroundIsFallback &&
              ready.backgroundFocusId == L"launcher.game.0" &&
              std::abs(ready.previousBackgroundOpacity +
                       ready.currentBackgroundOpacity - 1.0F) <= 0.01F,
            "decoded background enters one complete adopted frame");

        const auto secondKey = gba::RemoteImageCache::TrustedArtworkKey(
            L"game-launcher", L"launcher.game.1.artwork",
            L"library.art.11111111111111111111111111111111");
        const bool secondWasReady = cache.GetReadyImage(secondKey) != nullptr;
        const auto secondPending = draw(profile, L"launcher.game.1", 240);
        Check(secondWasReady ||
              (secondPending.backgroundFocusId == L"launcher.game.0" &&
               !secondPending.backgroundIsFallback),
            "stale or pending next artwork retains the last-good background");
        if (!secondWasReady) {
            Check(cache.SupplyTrustedArtwork(
                L"game-launcher", L"library.art.11111111111111111111111111111111",
                std::wstring(png)), "supply second decoded trusted background");
        }
        for (int attempt = 0; attempt < 200 && !cache.GetReadyImage(secondKey); ++attempt)
            Sleep(5);
        Check(cache.GetReadyImage(secondKey) != nullptr,
            "second trusted background decodes before publication");
        const auto crossfade = draw(profile, L"launcher.game.1", 300);
        Check(crossfade.backgroundTransitionActive &&
              crossfade.backgroundFocusId == L"launcher.game.1" &&
              crossfade.previousBackgroundOpacity > 0.0F &&
              crossfade.currentBackgroundOpacity >= 0.0F &&
              crossfade.currentBackgroundOpacity < 1.0F,
            "complete next background crossfades from one last-good frame");
        const auto focus = crossfade.render.focusRects.at(L"launcher.game.1");
        const auto hit = std::find_if(
            crossfade.render.hitRegions.begin(), crossfade.render.hitRegions.end(),
            [](const auto& item) { return item.nodeId == L"launcher.game.1"; });
        const auto uia = std::find_if(
            crossfade.render.accessibilityRegions.begin(),
            crossfade.render.accessibilityRegions.end(),
            [](const auto& item) { return item.nodeId == L"launcher.game.1"; });
        Check(hit != crossfade.render.hitRegions.end() &&
              uia != crossfade.render.accessibilityRegions.end(),
            "moving focus retains pointer and UIA geometry");
        Near(focus.x, hit->rect.x, "motion frame focus and pointer x agree");
        Near(focus.y, uia->rect.y, "motion frame focus and UIA y agree");
    }

    gba::NativeAccessibilityPolicy reduced;
    reduced.reducedMotion = true;
    reduced.reducedTransparency = true;
    const auto accessible = draw(
        L"hero-rail", L"launcher.game.1", 600, reduced);
    Check(!accessible.backgroundTransitionActive &&
          accessible.currentBackgroundOpacity == 1.0F,
        "reduced motion snaps the background swap without another frame");

    projection.RecordPresentationTimingForTesting(1.0, 20.0);
    const auto opacityOnly = draw(
        L"hero-rail", L"launcher.game.1", 700);
    Check(opacityOnly.effectQuality == EffectQuality::OpacityOnly,
        "render pressure deterministically degrades transforms before input");
    projection.RecordInputToFocusForTesting(55.0);
    const auto immediate = draw(
        L"hero-rail", L"launcher.game.1", 800);
    Check(immediate.effectQuality == EffectQuality::Immediate &&
          immediate.presentationMetrics.p95InputToFocusMilliseconds == 55.0 &&
          immediate.presentationMetrics.degradedFrameCount >= 2,
        "input-to-focus budget makes effects immediate and retains p95 evidence");

    gba::LauncherExperienceSelection installed;
    installed.revision = 1;
    installed.id = L"dev.example.installed";
    installed.version = L"1.0.0";
    installed.contentDigest = L"digest-installed";
    installed.presentationRevision = L"dev.example.installed:1.0.0:digest-installed";
    installed.preset = Preset::CoverWall;
    installed.backgroundMode = L"global";
    installed.focusEffect = L"lift";
    installed.motionIntensity = L"standard";
    installed.recipe = BuiltInRecipe(Preset::CoverWall);
    std::wstring selectionDiagnostic;
    Check(projection.PublishSelection(std::move(installed), selectionDiagnostic) &&
          projection.selectionRevision() == 1,
        "complete installed selection publishes atomically");
    const auto selected = draw(L"hero-rail", L"launcher.game.1", 900);
    Check(selected.preset == Preset::CoverWall && selected.presentationActive,
        "installed exact selection overrides only the private presentation preset");

    gba::LauncherExperienceSelection invalid;
    invalid.revision = 2;
    invalid.id = L"dev.example.invalid";
    invalid.version = L"2.0.0";
    invalid.contentDigest = L"digest-invalid";
    invalid.presentationRevision = L"dev.example.invalid:2.0.0:digest-invalid";
    invalid.preset = Preset::Carousel;
    invalid.backgroundMode = L"global";
    invalid.focusEffect = L"lift";
    invalid.motionIntensity = L"standard";
    Check(!projection.PublishSelection(std::move(invalid), selectionDiagnostic) &&
          projection.selectionRevision() == 1,
        "native-incompatible replacement retains the last-good exact revision");
    projection.BeginActivation(true);
    const auto safeStart = draw(L"hero-rail", L"launcher.game.1", 1000);
    Check(safeStart.preset == Preset::CoverWall && safeStart.backgroundIsFallback,
        "one-activation safe start uses code-owned recovery without mutating selection");
}

void NoArtworkHeroRailRetainsOneBoundedSemanticRail() {
    ComPtr<ID2D1Factory> d2d;
    Check(SUCCEEDED(D2D1CreateFactory(
        D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf())),
        "create no-artwork D2D factory");
    ComPtr<IDWriteFactory> write;
    Check(SUCCEEDED(DWriteCreateFactory(
        DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
        reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))),
        "create no-artwork DirectWrite factory");
    ComPtr<IWICImagingFactory> wic;
    Check(SUCCEEDED(CoCreateInstance(
        CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
        "create no-artwork WIC factory");

    constexpr std::wstring_view png =
        L"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ"
        L"AAAADUlEQVR42mP8z8BQDwAFgwJ/lK3xWQAAAABJRU5ErkJggg==";
    enum class State { Available, Mixed, AllTerminal };
    const auto render = [&](const Rect viewport, const State requested) {
        gba::RemoteImageLimits limits;
        limits.maximumEntries = 64;
        gba::RemoteImageCache cache(
            limits, {}, {}, [](std::wstring_view) { return true; });
        gba::DeclarativeRenderer renderer{d2d.Get(), write.Get(), &cache};
        LauncherExperienceProjection projection;
        const auto snapshot = ProjectedArtworkMatrixSnapshot();
        ComPtr<IWICBitmap> canvas;
        Check(SUCCEEDED(wic->CreateBitmap(
            static_cast<UINT>(viewport.width), static_cast<UINT>(viewport.height),
            GUID_WICPixelFormat32bppPBGRA, WICBitmapCacheOnLoad,
            canvas.ReleaseAndGetAddressOf())), "create no-artwork canvas");
        ComPtr<ID2D1RenderTarget> target;
        Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
            canvas.Get(), D2D1::RenderTargetProperties(),
            target.ReleaseAndGetAddressOf())), "create no-artwork target");
        gba::DeclarativeRenderOptions options;
        options.collectAccessibility = true;
        options.artworkWidgetId = L"generic-community-launcher";
        options.accessibility.reducedMotion = true;
        options.animationTimestampMilliseconds = 100;
        target->BeginDraw();
        auto first = projection.Render(
            renderer, target.Get(), &cache, AdvancedPresentationDeclaration(),
            L"no-artwork-generation", L"generic-community-launcher", snapshot,
            L"matrix.game.0", viewport, options);
        Check(SUCCEEDED(target->EndDraw()) && first.render.succeeded,
            "initial artwork matrix requests one complete frame");

        std::size_t tracked{};
        for (const auto& game : snapshot.root.children[1].children) {
            const auto& artwork = game.children[0];
            const auto key = gba::RemoteImageCache::TrustedArtworkKey(
                L"generic-community-launcher", artwork.id, artwork.artworkHandle);
            if (cache.GetState(key) != gba::RemoteImageState::Missing) ++tracked;
        }
        Check(tracked >= 6, "artwork matrix admits at least six visible handles");
        std::size_t index{};
        for (const auto& game : snapshot.root.children[1].children) {
            const auto& artwork = game.children[0];
            const auto key = gba::RemoteImageCache::TrustedArtworkKey(
                L"generic-community-launcher", artwork.id, artwork.artworkHandle);
            if (cache.GetState(key) == gba::RemoteImageState::Missing) {
                ++index;
                continue;
            }
            const bool supply = requested == State::Available ||
                (requested == State::Mixed && index == 0);
            if (supply)
                Check(cache.SupplyTrustedArtwork(
                    L"generic-community-launcher", artwork.artworkHandle,
                    std::wstring(png)), "supply matrix artwork");
            else
                Check(cache.FailTrustedArtwork(
                    L"generic-community-launcher", artwork.artworkHandle),
                    "fail matrix artwork");
            ++index;
        }
        if (requested != State::AllTerminal) {
            for (int attempt = 0; attempt < 200; ++attempt) {
                const auto& artwork = snapshot.root.children[1].children[0].children[0];
                const auto key = gba::RemoteImageCache::TrustedArtworkKey(
                    L"generic-community-launcher", artwork.id, artwork.artworkHandle);
                if (cache.GetState(key) == gba::RemoteImageState::Ready) break;
                Sleep(5);
            }
        }
        options.animationTimestampMilliseconds = 200;
        target->BeginDraw();
        auto result = projection.Render(
            renderer, target.Get(), &cache, AdvancedPresentationDeclaration(),
            L"no-artwork-generation", L"generic-community-launcher", snapshot,
            L"matrix.game.0", viewport, options);
        Check(SUCCEEDED(target->EndDraw()) && result.render.succeeded,
            "resolved artwork matrix commits one complete frame");
        const auto& canonical = projection.InteractionSnapshot(
            L"generic-community-launcher", L"no-artwork-generation", snapshot);
        const auto* canonicalFirst = FindNode(canonical.root, L"matrix.game.0");
        const auto* canonicalSecond = FindNode(canonical.root, L"matrix.game.1");
        const auto* canonicalRail = FindNode(canonical.root, L"launcher.game-rail");
        Check(canonicalFirst && canonicalSecond && canonicalRail &&
              canonicalFirst->actionId == L"matrix.launch.saved.0" &&
              canonicalFirst->collectionItemKey == L"matrix.saved.0" &&
              canonicalFirst->focusRight == canonicalSecond->id &&
              canonicalRail->collectionAnchorKey == L"matrix.saved.0" &&
              canonicalRail->scrollNearStartActionId == L"matrix.page.before" &&
              canonicalRail->scrollNearEndActionId == L"matrix.page.after",
            "artwork state never changes action, SavedId, paging, or focus authority");
        for (std::size_t catalogIndex = 0; catalogIndex < 32; ++catalogIndex) {
            const auto id = L"matrix.game." + std::to_wstring(catalogIndex);
            const auto* game = FindNode(canonical.root, id);
            Check(game && game->actionId ==
                      L"matrix.launch.saved." + std::to_wstring(catalogIndex) &&
                  game->collectionItemKey ==
                      L"matrix.saved." + std::to_wstring(catalogIndex),
                "32-game catalog retains exact action and SavedId authority");
            Check(catalogIndex == 0 || game->focusLeft ==
                      L"matrix.game." + std::to_wstring(catalogIndex - 1),
                "32-game catalog retains exact previous focus edge");
            Check(catalogIndex + 1 == 32 || game->focusRight ==
                      L"matrix.game." + std::to_wstring(catalogIndex + 1),
                "32-game catalog retains exact next focus edge");
        }
        return result;
    };

    for (const auto& [viewport, branch] :
         std::array<std::pair<Rect, std::wstring_view>, 3>{{
             {{0, 0, 978, 466}, L"compact"},
             {{0, 0, 1100, 650}, L"standard"},
             {{0, 0, 1400, 900}, L"wide"},
         }}) {
        const auto available = render(viewport, State::Available);
        Check(available.artworkAvailability == L"available" &&
              available.layoutBranch == branch,
            "available artwork retains the ordinary profile branch");
        const auto mixed = render(viewport, State::Mixed);
        Check(mixed.artworkAvailability == L"mixed" &&
              mixed.layoutBranch == branch,
            "mixed artwork retains the ordinary profile branch");
        const auto terminal = render(viewport, State::AllTerminal);
        Check(terminal.artworkAvailability == L"all-terminal" &&
              terminal.layoutBranch == branch,
            "all-terminal artwork selects bounded no-artwork composition");
        Check(Contains(viewport, terminal.railBounds) &&
              terminal.railBounds.height >= 150.0F,
            "all-terminal equal-width rail remains inside the body");
        for (const auto& region : terminal.render.accessibilityRegions)
            Check(Contains(viewport, region.rect),
                "all-terminal visible UIA geometry stays inside the body");
        for (const auto& region : terminal.render.hitRegions)
            Check(Contains(viewport, region.rect),
                "all-terminal pointer geometry stays inside the body");
        std::optional<float> cardWidth;
        for (std::size_t index = 0; index < 6; ++index) {
            const auto id = L"matrix.game." + std::to_wstring(index);
            const auto hit = std::find_if(
                terminal.render.hitRegions.begin(), terminal.render.hitRegions.end(),
                [&](const auto& item) { return item.nodeId == id; });
            const auto uia = std::find_if(
                terminal.render.accessibilityRegions.begin(),
                terminal.render.accessibilityRegions.end(),
                [&](const auto& item) { return item.nodeId == id; });
            Check(hit != terminal.render.hitRegions.end() &&
                  uia != terminal.render.accessibilityRegions.end(),
                "terminal rail game retains hit and UIA geometry");
            Check(Contains(terminal.railBounds, hit->rect) &&
                  Contains(terminal.railBounds, uia->rect),
                "terminal rail game geometry stays inside one rail");
            if (!cardWidth) cardWidth = uia->rect.width;
            Check(std::abs(uia->rect.width - *cardWidth) <= 1.0F,
                "terminal rail game cards have equal width");
        }
    }
}

} // namespace

int main() {
    const auto initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    Check(SUCCEEDED(initialized), "initialize COM");
    ResponsiveProfilesStayInsideWorkArea();
    InvalidRecipeFallsBackAtomically();
    StaticAssetsDecodeWithinTheHostBoundary();
    ScopedCascadeAccessibilityAndRecoveryStayAtomic();
    RenderedSlotsSharePaintPointerFocusAndUiaGeometry();
    ProductionProjectionAdmitsOnlyTheDeclaredSemanticContract();
    ProductionProjectionUsesOneAtomicPresentationFrame();
    NoArtworkHeroRailRetainsOneBoundedSemanticRail();
    std::cout << "LauncherExperienceTests: " << checks << " checks passed\n";
    CoUninitialize();
    return EXIT_SUCCESS;
}
