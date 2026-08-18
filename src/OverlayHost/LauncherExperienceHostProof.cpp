#include "LauncherExperienceHostProof.h"

#include "AccessibilityTree.h"
#include "FocusNavigation.h"
#include "LauncherExperienceAdapter.h"
#include "LauncherExperiencePresentation.h"

#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>

namespace widgetrail::launcher {
namespace {

using Microsoft::WRL::ComPtr;
using declarative::Rect;

WidgetNode Node(const wchar_t* id, const wchar_t* kind, const wchar_t* label) {
    WidgetNode node;
    node.id = id;
    node.kind = kind;
    node.text = label;
    node.accessibilityLabel = label;
    node.inputScopeId = L"launcher-root";
    return node;
}

std::vector<SlotContent> HostContents() {
    std::vector<SlotContent> contents;
    const auto add = [&](const Slot slot, WidgetNode root, const wchar_t* initialFocus = L"") {
        WidgetSnapshot snapshot;
        snapshot.sequence = 1;
        snapshot.instanceId = L"launcher.production-host-proof";
        snapshot.activeInputScopeId = L"launcher-root";
        snapshot.initialFocusId = initialFocus;
        snapshot.root = std::move(root);
        contents.push_back({slot, std::move(snapshot)});
    };

    add(Slot::HeroBackground,
        Node(L"launcher.hero", L"image", L"Selected game artwork unavailable"));
    auto rail = Node(L"launcher.game-rail", L"row", L"Installed games");
    for (int index = 0; index < 2; ++index) {
        auto game = Node(
            index == 0 ? L"launcher.game.0" : L"launcher.game.1",
            L"button", index == 0 ? L"Selected installed game" : L"Installed game");
        game.actionId = L"host.launch.exact-saved-id";
        rail.children.push_back(std::move(game));
    }
    add(Slot::GameRail, std::move(rail), L"launcher.game.0");
    auto details = Node(L"launcher.details", L"column", L"Selected game details");
    auto primary = Node(L"launcher.primary-action", L"button", L"Play installed game");
    primary.actionId = L"host.launch.exact-saved-id";
    details.children.push_back(std::move(primary));
    add(Slot::DetailsPanel, std::move(details));
    add(Slot::SourceStatus,
        Node(L"launcher.source-status", L"text", L"Installed source available"));
    auto hints = Node(L"launcher.controller-hints", L"row", L"Controller hints");
    auto back = Node(L"launcher.back", L"button", L"B Back");
    back.actionId = L"host.back";
    hints.children.push_back(std::move(back));
    add(Slot::ControllerHints, std::move(hints));
    return contents;
}

bool Contains(const Rect outer, const Rect inner) noexcept {
    return inner.x >= outer.x - 0.05F && inner.y >= outer.y - 0.05F &&
        inner.x + inner.width <= outer.x + outer.width + 0.05F &&
        inner.y + inner.height <= outer.y + outer.height + 0.05F;
}

std::shared_ptr<const DecodedLauncherAsset> Background(
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

} // namespace

bool RunProductionHostSemanticProof(std::wstring& diagnostic) {
    const auto initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(initialized) && initialized != RPC_E_CHANGED_MODE) {
        diagnostic = L"COM initialization failed";
        return false;
    }
    struct ComApartment final {
        bool initialized{};
        ~ComApartment() { if (initialized) CoUninitialize(); }
    } apartment{SUCCEEDED(initialized)};
    const auto finish = [&](const bool result, const wchar_t* message) {
        diagnostic = message;
        return result;
    };

    ComPtr<ID2D1Factory> d2d;
    ComPtr<IDWriteFactory> write;
    ComPtr<IWICImagingFactory> wic;
    ComPtr<IWICBitmap> canvas;
    ComPtr<ID2D1RenderTarget> target;
    if (FAILED(D2D1CreateFactory(
            D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf())) ||
        FAILED(DWriteCreateFactory(
            DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
            reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))) ||
        FAILED(CoCreateInstance(
            CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))) ||
        FAILED(wic->CreateBitmap(
            1280, 720, GUID_WICPixelFormat32bppPBGRA,
            WICBitmapCacheOnLoad, canvas.ReleaseAndGetAddressOf())) ||
        FAILED(d2d->CreateWicBitmapRenderTarget(
            canvas.Get(), D2D1::RenderTargetProperties(), target.ReleaseAndGetAddressOf())))
        return finish(false, L"rendering dependency initialization failed");

    DeclarativeRenderer renderer{d2d.Get(), write.Get(), nullptr};
    DeclarativeRenderOptions options;
    options.collectAccessibility = true;
    const Rect workArea{0, 0, 1280, 720};
    LauncherExperiencePresentationOwner presentationOwner;
    LauncherPresentationRequest first;
    first.revision = L"production-proof:1";
    first.preset = Preset::HeroRail;
    first.backgroundMode = BackgroundMode::PackAsset;
    first.focusEffect = FocusEffect::Lift;
    first.packBackground = Background(
        L"production.pack-background", first.revision.c_str(),
        {0x28, 0x38, 0x78, 0xFF});
    first.packStyles[Slot::GameRail].emplace(
        L"background", WidgetStyleValue{L"color", L"#203060ff", {}, {}});
    std::wstring presentationDiagnostic;
    if (!presentationOwner.Activate(first, {}, 100, presentationDiagnostic))
        return finish(false, L"production presentation activation failed");
    auto second = first;
    second.revision = L"production-proof:2";
    second.backgroundMode = BackgroundMode::SelectedGameArtwork;
    second.selectedGameArtworkRevision = L"selected-game:7";
    second.selectedGameBackground = Background(
        L"production.selected-game-background", L"selected-game:7",
        {0x78, 0x38, 0x28, 0xFF});
    if (!presentationOwner.Activate(second, {}, 200, presentationDiagnostic))
        return finish(false, L"production presentation switch failed");
    const auto presentation = presentationOwner.Sample(290);
    if (!presentation.previousBackground || !presentation.currentBackground ||
        presentation.previousBackgroundOpacity <= 0 ||
        presentation.currentBackgroundOpacity <= 0)
        return finish(false, L"production decode-before-crossfade lifecycle failed");

    target->BeginDraw();
    const auto experience = RenderExperience(
        renderer, target.Get(), nullptr, Preset::HeroRail, workArea,
        HostContents(), L"launcher.game.0", options, &presentation);
    if (FAILED(target->EndDraw()) || !experience.layout.valid() ||
        !experience.render.succeeded ||
        !experience.render.focusRects.contains(L"launcher.game.0"))
        return finish(false, L"production adapter render failed");

    const auto focus = experience.render.focusRects.at(L"launcher.game.0");
    const auto pointer = input::FindPointerHitTarget(
        focus.x + focus.width / 2, focus.y + focus.height / 2,
        L"launcher-root", experience.render);
    const auto tree = accessibility::BuildWidgetTree(
        L"game-launcher", L"production-host-proof", experience.semanticSnapshot,
        experience.render, L"launcher.game.0");
    const auto action = [&](const std::wstring_view id, const std::wstring_view actionId) {
        return std::any_of(tree.nodes.begin(), tree.nodes.end(), [&](const auto& node) {
            return node.id == id && node.actionId == actionId && Contains(workArea, node.bounds);
        });
    };
    if (!pointer || pointer->id != L"launcher.game.0" || !pointer->enabled ||
        !tree.focusedNode || tree.nodes[*tree.focusedNode].id != L"launcher.game.0" ||
        !action(L"launcher.game.0", L"host.launch.exact-saved-id") ||
        !action(L"launcher.primary-action", L"host.launch.exact-saved-id") ||
        !action(L"launcher.back", L"host.back"))
        return finish(false, L"production pointer, focus, UIA, or action agreement failed");

    for (int attempt = 0; attempt < 3; ++attempt)
        presentationOwner.RejectRevision(
            second.revision, Preset::HeroRail, L"fixture decode failure");
    const auto recovery = presentationOwner.Sample(500);
    if (!recovery.builtIn || !presentationOwner.IsRevisionDisabled(second.revision) ||
        presentationOwner.IsRevisionDisabled(first.revision))
        return finish(false, L"production revision-scoped recovery failed");
    target->BeginDraw();
    const auto recoveredExperience = RenderExperience(
        renderer, target.Get(), nullptr, Preset::HeroRail, workArea,
        HostContents(), L"launcher.game.0", options, &recovery);
    if (FAILED(target->EndDraw()) || !recoveredExperience.render.succeeded ||
        !recoveredExperience.render.focusRects.contains(L"launcher.game.0"))
        return finish(false, L"production built-in recovery render failed");
    const auto recoveredTree = accessibility::BuildWidgetTree(
        L"game-launcher", L"production-host-proof-recovery",
        recoveredExperience.semanticSnapshot, recoveredExperience.render,
        L"launcher.game.0");
    if (!std::any_of(recoveredTree.nodes.begin(), recoveredTree.nodes.end(),
            [](const auto& node) {
                return node.id == L"launcher.primary-action" &&
                    node.actionId == L"host.launch.exact-saved-id";
            }))
        return finish(false, L"production recovery changed host action authority");
    return finish(true, L"presentation lifecycle passed");
}

} // namespace widgetrail::launcher
