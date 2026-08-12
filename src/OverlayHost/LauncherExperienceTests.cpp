#include "AccessibilityTree.h"
#include "FocusNavigation.h"
#include "LauncherExperienceAdapter.h"

#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
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

} // namespace

int main() {
    const auto initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    Check(SUCCEEDED(initialized), "initialize COM");
    ResponsiveProfilesStayInsideWorkArea();
    InvalidRecipeFallsBackAtomically();
    RenderedSlotsSharePaintPointerFocusAndUiaGeometry();
    std::cout << "LauncherExperienceTests: " << checks << " checks passed\n";
    CoUninitialize();
    return EXIT_SUCCESS;
}
