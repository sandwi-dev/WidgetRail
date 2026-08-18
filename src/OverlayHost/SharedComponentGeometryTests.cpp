#include "DeclarativeRenderer.h"

#include <Windows.h>
#include <d2d1.h>
#include <dwrite.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <iostream>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace {

using Microsoft::WRL::ComPtr;
using widgetrail::WidgetComputedStyle;
using widgetrail::WidgetNode;
using widgetrail::WidgetSnapshot;
using widgetrail::WidgetStyleValue;
using widgetrail::declarative::Rect;

std::size_t checks{};

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) {
        std::cerr << "SharedComponentGeometryTests: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

void Near(
    const float actual,
    const float expected,
    const std::string_view message,
    const float tolerance = 0.52F) {
    Check(std::abs(actual - expected) <= tolerance, message);
}

WidgetStyleValue Length(const double value) {
    return {L"length", std::to_wstring(value) + L"px", value, L"px"};
}

WidgetStyleValue Length(const double value, std::wstring unit) {
    return {L"length", std::to_wstring(value) + unit, value, std::move(unit)};
}

WidgetStyleValue Number(const double value) {
    return {L"number", std::to_wstring(value), value, {}};
}

WidgetStyleValue Keyword(std::wstring value) {
    return {L"keyword", std::move(value), std::nullopt, {}};
}

WidgetStyleValue LengthList(std::wstring value) {
    return {L"lengthList", std::move(value), std::nullopt, {}};
}

WidgetStyleValue Color(std::wstring value) {
    return {L"color", std::move(value), std::nullopt, {}};
}

WidgetNode Node(std::wstring id, std::wstring kind) {
    WidgetNode result;
    result.id = std::move(id);
    result.kind = std::move(kind);
    return result;
}

WidgetNode Text(
    std::wstring id,
    std::wstring value,
    const float size,
    const float lineHeight,
    const int maximumLines) {
    auto result = Node(std::move(id), L"text");
    result.text = std::move(value);
    result.baseStyle = {
        {L"min-width", Length(0)},
        {L"color", Color(L"#f8f8f8")},
        {L"font-family", {L"fontFamily", L"Segoe UI Variable Text", std::nullopt, {}}},
        {L"font-size", Length(size)},
        {L"font-weight", Number(500)},
        {L"line-height", Number(lineHeight)},
        {L"max-lines", Number(maximumLines)},
        {L"text-overflow", Keyword(L"ellipsis")},
    };
    return result;
}

WidgetNode Button(
    std::wstring id,
    std::wstring label,
    const bool selected = false,
    const bool busy = false,
    const bool disabled = false) {
    auto result = Node(std::move(id), L"button");
    result.text = std::move(label);
    result.glyph = L"play";
    result.actionId = L"activate";
    result.isSelected = selected;
    result.isBusy = busy;
    result.isDisabled = disabled;
    result.baseStyle = {
        {L"width", Length(220)},
        {L"min-height", Length(44)},
        {L"padding", LengthList(L"10px 14px")},
        {L"font-size", Length(15)},
        {L"line-height", Number(1.2)},
        {L"max-lines", Number(2)},
        {L"background", Color(L"#20232b")},
        {L"color", Color(L"#ffffff")},
        {L"justify", Keyword(L"center")},
    };
    return result;
}

WidgetNode SpotifySetupButton() {
    auto result = Node(L"spotify.setup.done", L"button");
    result.text = L"Check configuration";
    result.actionId = L"spotify.setup.done";
    result.baseStyle = {
        {L"min-width", Length(132)},
        {L"min-height", Length(44)},
        {L"padding", LengthList(L"10px 15px")},
        {L"font-size", Length(14)},
        {L"line-height", Number(1.25)},
        {L"max-lines", Number(2)},
        {L"text-align", Keyword(L"center")},
        {L"background", Color(L"#8b7cf6")},
        {L"color", Color(L"#090908")},
    };
    return result;
}

struct Header final {
    WidgetNode node;
    std::wstring contentId;
    std::wstring textId;
    std::wstring eyebrowId;
    std::wstring titleId;
    std::wstring descriptionId;
    std::wstring trailingId;
};

Header SectionHeader(
    const std::wstring_view prefix,
    std::wstring eyebrow,
    std::wstring title,
    std::wstring description,
    const bool trailing,
    const int titleLines = 1) {
    const std::wstring rootId{prefix};
    Header result;
    result.contentId = rootId + L".content";
    result.textId = rootId + L".text";
    result.eyebrowId = rootId + L".eyebrow";
    result.titleId = rootId + L".title";
    result.descriptionId = rootId + L".description";
    result.trailingId = rootId + L".trailing";
    result.node = Node(rootId, L"stack");
    result.node.baseStyle = {
        {L"width", Length(100, L"%")},
        {L"min-width", Length(0)},
        {L"gap", LengthList(L"4px")},
        {L"flex-shrink", Number(0)},
    };
    auto content = Node(result.contentId, L"row");
    content.baseStyle = {
        {L"width", Length(100, L"%")},
        {L"min-width", Length(0)},
        {L"min-height", Length(44)},
        {L"gap", LengthList(L"10px")},
        {L"align", Keyword(L"center")},
        {L"justify", Keyword(L"space-between")},
    };
    auto text = Node(result.textId, L"stack");
    text.baseStyle = {
        {L"min-width", Length(0)},
        {L"flex-grow", Number(1)},
        {L"flex-shrink", Number(1)},
        {L"gap", LengthList(L"2px")},
    };
    auto eyebrowNode = Text(result.eyebrowId, std::move(eyebrow), 11.0F, 1.2F, 1);
    eyebrowNode.baseStyle[L"letter-spacing"] = Length(0.33);
    auto titleNode = Text(result.titleId, std::move(title), 20.0F, 1.2F, titleLines);
    auto descriptionNode = Text(
        result.descriptionId, std::move(description), 13.0F, 1.35F, 2);
    descriptionNode.baseStyle[L"font-weight"] = Number(400);
    text.children = {
        std::move(eyebrowNode), std::move(titleNode), std::move(descriptionNode)};
    content.children.push_back(std::move(text));
    if (trailing) {
        auto trailingRow = Node(result.trailingId, L"row");
        trailingRow.baseStyle = {
            {L"flex-shrink", Number(0)},
            {L"padding", LengthList(L"0px 2px 0px 8px")},
        };
        auto action = Button(rootId + L".action", L"Open");
        action.baseStyle[L"width"] = Length(96);
        action.baseStyle[L"max-width"] = Length(112);
        trailingRow.children = {std::move(action)};
        content.children.push_back(std::move(trailingRow));
    }
    result.node.children = {std::move(content)};
    return result;
}

WidgetNode ActionTile(const std::wstring_view prefix, std::wstring title) {
    const std::wstring rootId{prefix};
    auto tile = Node(rootId, L"actionSurface");
    tile.actionId = L"open";
    tile.baseStyle = {
        {L"width", Length(100, L"%")},
        {L"min-width", Length(0)},
        {L"min-height", Length(82)},
        {L"gap", LengthList(L"12px")},
        {L"padding", LengthList(L"10px")},
        {L"direction", Keyword(L"row")},
        {L"align", Keyword(L"center")},
        {L"overflow", Keyword(L"clip")},
    };
    auto artwork = Node(rootId + L".artwork", L"icon");
    artwork.glyph = L"music";
    artwork.baseStyle = {
        {L"width", Length(56)}, {L"height", Length(56)},
        {L"flex-shrink", Number(0)},
    };
    auto content = Node(rootId + L".content", L"stack");
    content.baseStyle = {
        {L"min-width", Length(0)}, {L"gap", LengthList(L"2px")},
        {L"flex-grow", Number(1)}, {L"flex-shrink", Number(1)},
    };
    content.children = {
        Text(rootId + L".title", std::move(title), 15.0F, 1.25F, 2),
        Text(rootId + L".subtitle", L"A complete shared component subtitle", 13.0F, 1.25F, 2),
        Text(rootId + L".state", L"Ready", 12.0F, 1.2F, 1),
    };
    tile.children = {std::move(artwork), std::move(content)};
    return tile;
}

struct Fixture final {
    std::string name;
    WidgetSnapshot snapshot;
    std::vector<std::wstring> textIds;
    std::optional<Header> header;
    std::wstring tileId;
    std::vector<std::wstring> buttonIds;
};

Fixture MakeFixture(const std::string_view name) {
    Fixture result;
    result.name = std::string(name);
    result.snapshot.instanceId = std::wstring(name.begin(), name.end()) + L".runtime";
    result.snapshot.root = Node(std::wstring(name.begin(), name.end()) + L".root", L"stack");
    result.snapshot.root.baseStyle = {
        {L"width", Length(100, L"%")}, {L"height", Length(100, L"%")},
        {L"min-width", Length(0)}, {L"min-height", Length(0)},
        {L"gap", LengthList(L"10px")}, {L"padding", LengthList(L"16px 18px")},
        {L"overflow", Keyword(L"clip")}, {L"background", Color(L"#101218")},
    };
    if (name == "games") {
        result.buttonIds = {L"games.normal", L"games.selected", L"games.busy", L"games.disabled"};
        result.snapshot.root.children = {
            Button(result.buttonIds[0], L"Application action"),
            Button(result.buttonIds[1], L"Application action", true),
            Button(result.buttonIds[2], L"Application action", false, true),
            Button(result.buttonIds[3], L"Application action", false, false, true),
        };
    } else if (name == "spotify") {
        result.header = SectionHeader(
            L"spotify.library.header", L"LIBRARY", L"Your library",
            L"Saved playlists and albums", true);
        result.textIds = {
            result.header->eyebrowId, result.header->titleId,
            result.header->descriptionId};
        result.tileId = L"spotify.library.tile";
        result.buttonIds = {L"spotify.setup.done"};
        auto setupCard = Node(L"spotify.setup.card", L"stack");
        setupCard.baseStyle = {
            {L"min-width", Length(0)},
            {L"align", Keyword(L"start")},
        };
        setupCard.children = {SpotifySetupButton()};
        result.snapshot.root.children = {
            result.header->node, ActionTile(result.tileId, L"Liked Songs"),
            std::move(setupCard)};
    } else if (name == "now-playing") {
        result.header = SectionHeader(
            L"media.header", L"CONTROL CENTER", L"Now Playing",
            L"Current Windows media session", false);
        result.textIds = {
            result.header->eyebrowId, result.header->titleId,
            result.header->descriptionId};
        result.tileId = L"media.now-playing";
        result.snapshot.root.children = {
            result.header->node, ActionTile(result.tileId, L"A very long current track title")};
    } else if (name == "settings") {
        result.header = SectionHeader(
            L"settings.appearance.header", L"SETTINGS", L"Appearance",
            L"Text, interface, contrast, transparency, and motion", true);
        result.textIds = {
            result.header->eyebrowId, result.header->titleId,
            result.header->descriptionId};
        result.buttonIds = {L"settings.choice"};
        result.snapshot.root.children = {
            result.header->node, Button(result.buttonIds[0], L"High contrast")};
    } else {
        result.header = SectionHeader(
            L"gallery.header", L"COMMUNITY REFERENCE",
            L"Controller-first component gallery",
            L"Shared semantic components across responsive surfaces", true, 2);
        result.textIds = {
            result.header->eyebrowId, result.header->titleId,
            result.header->descriptionId};
        result.tileId = L"gallery.media.tile";
        result.snapshot.root.children = {
            result.header->node, ActionTile(result.tileId, L"Media tile example")};
    }
    return result;
}

void AssertCompleteRect(
    const widgetrail::RenderResult& render,
    const std::wstring& id,
    const std::string_view message) {
    Check(render.elementRects.contains(id) && render.elementVisibleRects.contains(id), message);
    const auto& box = render.elementRects.at(id);
    const auto& visible = render.elementVisibleRects.at(id);
    if (std::abs(visible.x - box.x) > 0.52F ||
        std::abs(visible.y - box.y) > 0.52F ||
        std::abs(visible.width - box.width) > 0.52F ||
        std::abs(visible.height - box.height) > 0.52F) {
        std::cerr << "SharedComponentGeometryTests: " << message
                  << " box=" << box.x << ',' << box.y << ',' << box.width << ',' << box.height
                  << " visible=" << visible.x << ',' << visible.y << ','
                  << visible.width << ',' << visible.height << '\n';
        std::exit(EXIT_FAILURE);
    }
    Near(visible.x, box.x, message);
    Near(visible.y, box.y, message);
    Near(visible.width, box.width, message);
    Near(visible.height, box.height, message);
}

void ProductMatrixUsesSharedGeometry(
    ID2D1Factory* d2d,
    ID2D1RenderTarget* target,
    IDWriteFactory* write) {
    struct Profile final {
        const char* name;
        float width;
        float height;
        float pixelScale;
        float textScale;
        bool highContrast;
        bool reducedTransparency;
    };
    const Profile profiles[] = {
        {"compact", 420.0F, 360.0F, 1.0F, 1.0F, false, false},
        {"standard", 640.0F, 480.0F, 1.25F, 1.0F, false, false},
        {"wide-150", 1120.0F, 620.0F, 1.5F, 1.5F, false, false},
        {"accessible", 760.0F, 620.0F, 1.5F, 1.5F, true, true},
    };
    for (const auto fixtureName : {
             std::string_view{"games"}, std::string_view{"spotify"},
             std::string_view{"now-playing"}, std::string_view{"settings"},
             std::string_view{"sdk-gallery"}}) {
        const auto fixture = MakeFixture(fixtureName);
        for (const auto& profile : profiles) {
            widgetrail::DeclarativeRenderOptions options;
            options.pixelScale = profile.pixelScale;
            options.accessibility.textScale = profile.textScale;
            options.accessibility.reducedTransparency = profile.reducedTransparency;
            if (profile.highContrast) {
                options.accessibility.minimumFontWeight = 600;
                options.accessibility.contrastHook = [](widgetrail::NativeColor, widgetrail::NativeColor) {
                    return widgetrail::NativeColor{1.0F, 1.0F, 1.0F, 1.0F};
                };
            }
            widgetrail::DeclarativeRenderer renderer{d2d, write, nullptr};
            const auto layoutOnly = renderer.Render(
                nullptr, fixture.snapshot, {},
                {0.0F, 0.0F, profile.width, profile.height}, options);
            const auto unexpected = std::find_if(
                layoutOnly.diagnostics.begin(), layoutOnly.diagnostics.end(),
                [](const widgetrail::RenderDiagnostic& diagnostic) {
                    return diagnostic.code != L"missing_render_target";
                });
            if (unexpected != layoutOnly.diagnostics.end()) {
                std::string detail = fixture.name + "/" + profile.name;
                detail += ": ";
                for (const auto character : unexpected->code)
                    detail.push_back(character <= 0x7f ? static_cast<char>(character) : '?');
                detail += " node=";
                for (const auto character : unexpected->nodeId)
                    detail.push_back(character <= 0x7f ? static_cast<char>(character) : '?');
                detail += " message=";
                for (const auto character : unexpected->message)
                    detail.push_back(character <= 0x7f ? static_cast<char>(character) : '?');
                Check(false, detail);
            }
            for (const auto& id : fixture.textIds) {
                std::string nodeContext = fixture.name + "/" + profile.name + "/";
                for (const auto character : id)
                    nodeContext.push_back(character <= 0x7f ? static_cast<char>(character) : '?');
                AssertCompleteRect(layoutOnly, id, nodeContext);
                Check(layoutOnly.elementRects.at(id).height >= 11.0F * profile.textScale,
                    "named shared text retains a complete scaled line box");
            }
            if (fixture.header) {
                AssertCompleteRect(layoutOnly, fixture.header->contentId,
                    "SectionHeader content remains wholly visible");
                const auto& text = layoutOnly.elementRects.at(fixture.header->textId);
                const auto& content = layoutOnly.elementRects.at(fixture.header->contentId);
                Check(text.height <= content.height + 0.01F && text.width >= 80.0F,
                    "SectionHeader preserves required text height and bounded width");
                if (layoutOnly.elementRects.contains(fixture.header->trailingId)) {
                    const auto& trailing = layoutOnly.elementRects.at(fixture.header->trailingId);
                    Check(text.x + text.width <= trailing.x + 0.01F,
                        "SectionHeader trailing content never overlaps the text stack");
                    Check(content.height + 0.01F >= trailing.height,
                        "SectionHeader content height includes its trailing action");
                }
            }
            if (!fixture.tileId.empty()) {
                AssertCompleteRect(layoutOnly, fixture.tileId,
                    "ActionSurface remains wholly visible");
                const auto& tile = layoutOnly.elementRects.at(fixture.tileId);
                for (const auto suffix : {L".artwork", L".content", L".title", L".subtitle", L".state"}) {
                    const auto id = fixture.tileId + suffix;
                    const auto& child = layoutOnly.elementRects.at(id);
                    Check(child.x >= tile.x - 0.01F && child.y >= tile.y - 0.01F &&
                            child.x + child.width <= tile.x + tile.width + 0.01F &&
                            child.y + child.height <= tile.y + tile.height + 0.01F,
                        "ActionSurface presentational content stays inside the shared tile");
                }
            }
            if (target && profile.width <= 1120.0F && profile.height <= 620.0F) {
                widgetrail::DeclarativeRenderer painter{d2d, write, nullptr};
                target->BeginDraw();
                target->Clear(D2D1::ColorF(0.02F, 0.02F, 0.03F, 1.0F));
                const auto painted = painter.Render(
                    target, fixture.snapshot, {},
                    {0.0F, 0.0F, profile.width, profile.height}, options);
                Check(SUCCEEDED(target->EndDraw()) && painted.succeeded &&
                        painted.diagnostics.empty(),
                    "named component profile completes a real DirectWrite paint");
                if (fixture.name == "spotify") {
                    Check(painted.textLineCounts.contains(L"spotify.setup.done") &&
                            painted.textLineCounts.at(L"spotify.setup.done") == 1,
                        "Spotify Check configuration remains one complete painted line");
                }
            }
            for (const auto& id : fixture.buttonIds) {
                Check(layoutOnly.elementRects.at(id).height >= 44.0F,
                    "shared Button retains its interaction height in every state/profile");
            }
        }
    }

    auto states = MakeFixture("games");
    widgetrail::DeclarativeRenderer stateRenderer{d2d, write, nullptr};
    target->BeginDraw();
    target->Clear(D2D1::ColorF(0.02F, 0.02F, 0.03F, 1.0F));
    const auto stateResult = stateRenderer.Render(
        target, states.snapshot, {}, {0.0F, 0.0F, 420.0F, 360.0F});
    Check(SUCCEEDED(target->EndDraw()),
        "state geometry completes a real DirectWrite paint");
    const auto relative = [&](const std::wstring& id) {
        const auto& box = stateResult.elementRects.at(id);
        const auto& placement = stateResult.buttonContentPlacements.at(id);
        return Rect{
            placement.leading.x - box.x,
            placement.leading.y - box.y,
            placement.text.x - box.x,
            placement.trailingStateCue.x > 0.0F
                ? placement.trailingStateCue.y - box.y
                : 0.0F};
    };
    const auto selected = relative(L"games.selected");
    const auto busy = relative(L"games.busy");
    const auto disabled = relative(L"games.disabled");
    Near(selected.x, busy.x, "busy state does not shift shared leading content");
    Near(selected.x, disabled.x, "disabled state does not shift shared leading content");
    Near(selected.y, busy.y, "busy state preserves shared vertical center");
    Near(selected.y, disabled.y, "disabled state preserves shared vertical center");
    Near(selected.height, busy.height, "selected and busy cues share one vertical center");
    Near(selected.height, disabled.height, "selected and disabled cues share one vertical center");
}

} // namespace

int main() {
    const auto initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    Check(SUCCEEDED(initialized), "COM initializes");
    {
        ComPtr<ID2D1Factory> d2d;
        Check(SUCCEEDED(D2D1CreateFactory(
            D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf())),
            "Direct2D factory initializes");
        ComPtr<IDWriteFactory> write;
        Check(SUCCEEDED(DWriteCreateFactory(
            DWRITE_FACTORY_TYPE_SHARED,
            __uuidof(IDWriteFactory),
            reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))),
            "DirectWrite factory initializes");
        ComPtr<IWICImagingFactory> wic;
        Check(SUCCEEDED(CoCreateInstance(
            CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()))),
            "WIC factory initializes");
        ComPtr<IWICBitmap> canvas;
        Check(SUCCEEDED(wic->CreateBitmap(
            1200, 700, GUID_WICPixelFormat32bppPBGRA,
            WICBitmapCacheOnLoad, canvas.ReleaseAndGetAddressOf())),
            "component raster canvas initializes");
        ComPtr<ID2D1RenderTarget> target;
        Check(SUCCEEDED(d2d->CreateWicBitmapRenderTarget(
            canvas.Get(), D2D1::RenderTargetProperties(), target.ReleaseAndGetAddressOf())),
            "component raster target initializes");
        ProductMatrixUsesSharedGeometry(d2d.Get(), target.Get(), write.Get());
    }
    std::cout << "SharedComponentGeometryTests: " << checks << " checks passed\n";
    CoUninitialize();
    return EXIT_SUCCESS;
}
