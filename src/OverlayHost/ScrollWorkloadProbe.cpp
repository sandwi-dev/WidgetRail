#include "DeclarativeRenderer.h"
#include "WidgetBridgeClient.h"
#include "ControllerNavigation.h"
#include <wincodec.h>
#include <wrl/client.h>
#include <algorithm>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <thread>

using Microsoft::WRL::ComPtr;
using namespace widgetrail;

namespace {
void Require(bool condition, const char* message) {
    if (!condition) throw std::runtime_error(message);
}
void Check(HRESULT result) { Require(SUCCEEDED(result), "Native probe resource failed"); }
void ReplaceFixtureArtwork(WidgetNode& node) {
    // Only synthetic fixtures enter this probe. Network access is replaced by
    // a deterministic decoder; geometry, node identities and resolved styles
    // come from the production widget/Bridge export.
    if (!node.artworkHandle.empty() || !node.imageSource.empty()) {
        node.artworkHandle.clear(); node.imageSource = L"https://example.test/scroll-probe.png";
    }
    for (auto& child : node.children) ReplaceFixtureArtwork(child);
}
const WidgetNode* Find(const WidgetNode& node, std::wstring_view id) {
    if (node.id == id) return &node;
    for (const auto& child : node.children) if (auto* found = Find(child, id)) return found;
    return nullptr;
}
WidgetNode* Find(WidgetNode& node, std::wstring_view id) {
    if (node.id == id) return &node;
    for (auto& child : node.children) if (auto* found = Find(child, id)) return found;
    return nullptr;
}
const WidgetNode* FirstControl(const WidgetNode& node) {
    if (node.kind == L"actionSurface" || node.kind == L"button") return &node;
    for (const auto& child : node.children) if (auto* found = FirstControl(child)) return found;
    return nullptr;
}
WidgetNode* ParentOf(WidgetNode& node, std::wstring_view id) {
    for (auto& child : node.children) {
        if (child.id == id) return &node;
        if (auto* parent = ParentOf(child, id)) return parent;
    }
    return nullptr;
}
void Reidentify(WidgetNode& node, const std::wstring& suffix) {
    node.id += suffix;
    if (!node.collectionItemKey.empty()) node.collectionItemKey += suffix;
    for (auto& child : node.children) Reidentify(child, suffix);
}
}

int wmain(int argc, wchar_t** argv) {
    try {
        Require(argc == 2 || (argc == 3 && std::wstring_view(argv[2]) == L"--cadence"),
            "Usage: ScrollWorkloadProbe <renderer-fixture.json> [--cadence]");
        std::ifstream file(std::filesystem::path(argv[1]), std::ios::binary);
        Require(static_cast<bool>(file), "Fixture missing");
        std::string payload{std::istreambuf_iterator<char>(file), {}};
        std::wstring error;
        auto snapshot = testing::ParseWidgetSnapshotResponse(payload, error);
        if (!snapshot) { std::wcerr << error << '\n'; return 1; }
        ReplaceFixtureArtwork(snapshot->root);
        Check(CoInitializeEx(nullptr, COINIT_MULTITHREADED));
        struct Apartment final { ~Apartment() { CoUninitialize(); } } apartment;
        ComPtr<ID2D1Factory> d2d; ComPtr<IDWriteFactory> write; ComPtr<IWICImagingFactory> wic;
        Check(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.GetAddressOf()));
        Check(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), reinterpret_cast<IUnknown**>(write.GetAddressOf())));
        Check(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(wic.GetAddressOf())));
        ComPtr<IWICBitmap> canvas; ComPtr<ID2D1RenderTarget> target;
        Check(wic->CreateBitmap(5000, 1125, GUID_WICPixelFormat32bppPBGRA, WICBitmapCacheOnLoad, canvas.GetAddressOf()));
        Check(d2d->CreateWicBitmapRenderTarget(canvas.Get(), D2D1::RenderTargetProperties(), target.GetAddressOf()));
        target->SetDpi(120, 120);
        RemoteImageCache images({}, {}, [](std::wstring_view, std::stop_token, const RemoteImageLimits&) {
            RemoteDecodedImage image; image.width = 192; image.height = 320; image.stride = 768;
            image.premultipliedBgra.resize(image.stride * image.height, 200);
            for (std::size_t i = 3; i < image.premultipliedBgra.size(); i += 4) image.premultipliedBgra[i] = 255;
            return RemoteImageFetchResult{S_OK, std::move(image), {}};
        });
        const ImageDecodeSize decode{192, 320};
        (void)images.Request(L"https://example.test/scroll-probe.png", decode);
        const auto key = RemoteImageCache::VariantKey(L"https://example.test/scroll-probe.png", decode);
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
        while (images.GetState(key) != RemoteImageState::Ready && std::chrono::steady_clock::now() < deadline)
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        Require(images.GetState(key) == RemoteImageState::Ready, "Fixture artwork unavailable");
        DeclarativeRenderer renderer(d2d.Get(), write.Get(), &images);
        DeclarativeRenderOptions options;
        options.pixelScale = 1.25F; options.compositorWidgetTransitions = true;
        options.compositorBackgroundAvailable = true; options.accessibility.reducedMotion = true;
        options.suppressFocusedDescendantFollow = true;
        options.sizeArtworkToDisplay = false; options.artworkDecodeSize = decode;
        const auto& hints = snapshot->surface;
        const float viewportWidth = hints && hints->widthMode != L"fillAvailable" ? static_cast<float>(hints->preferredWidth.value_or(980)) : 3974.0F;
        const float viewportHeight = hints && hints->heightMode != L"fillAvailable" ? static_cast<float>(hints->preferredHeight.value_or(700)) : 862.2F;
        const declarative::Rect viewport{1, 1, viewportWidth, viewportHeight};
        std::wstring focus = snapshot->initialFocusId;
        const auto draw = [&] {
            target->BeginDraw();
            auto result = renderer.Render(target.Get(), *snapshot, focus, viewport, options);
            Check(target->EndDraw());
            Require(result.succeeded, "Fixture render failed");
            Require(result.widgetComposition && !result.widgetComposition->directContent, "Fixture fell back from layered composition");
            return result;
        };
        auto initial = draw();
        const auto scroll = std::max_element(initial.scrollViewports.begin(), initial.scrollViewports.end(),
            [](const auto& a, const auto& b) { return a.second.maximumOffset < b.second.maximumOffset; });
        Require(scroll != initial.scrollViewports.end() && scroll->second.maximumOffset > 1, "Fixture has no scrollable collection");
        const auto scrollId = scroll->first;
        const auto axis = scroll->second.axis;
        const auto* root = Find(snapshot->root, scrollId);
        const auto* control = root ? FirstControl(*root) : nullptr;
        Require(control != nullptr, "Fixture scroll has no control"); focus = control->id;
        for (int i = 0; i < 3; ++i) initial = draw();
        if (argc == 3) {
            using namespace widgetrail::input;
            ContinuousScrollFrames frames;
            const auto movementAxis = axis == declarative::ScrollAxis::Vertical ? FreeScrollAxis::Vertical : FreeScrollAxis::Horizontal;
            const auto offer = [&](float delta, std::wstring_view authority = L"view-1") {
                return frames.Offer({movementAxis, delta, true, false}, authority);
            };
            std::size_t inputs{}, paints{};
            for (int frame = 0; frame < 12; ++frame) {
                for (int input = 0; input < 4; ++input) { Require(offer(2), "Input not queued"); ++inputs; }
                const auto sample = frames.Take(L"view-1");
                Require(sample && sample->deltaDip == 8 && !frames.Take(L"view-1"), "A paint did not consume exactly one coalesced movement");
                const auto plan = renderer.PlanFocusedFreeScroll(*snapshot, focus, axis, sample->deltaDip, viewport, scrollId);
                Require(plan.has_value(), "Cadence scroll did not plan");
                initial = draw(); ++paints;
            }
            Require(renderer.PlanFocusedFreeScroll(*snapshot, focus, axis, 1e9F, viewport, scrollId).has_value(), "Cannot reach loaded boundary");
            initial = draw();
            const float boundary = initial.scrollViewports.at(scrollId).offset;
            Require(offer(12), "Boundary probe not queued");
            const auto edgeSample = frames.Take(L"view-1");
            FocusedFreeScrollPlanDiagnostic diagnostic;
            Require(!renderer.PlanFocusedFreeScroll(*snapshot, focus, axis, edgeSample->deltaDip, viewport, scrollId, &diagnostic) &&
                diagnostic.disposition == FocusedFreeScrollPlanDisposition::OffsetBoundary, "Loaded boundary did not stop movement");
            frames.BlockLastFrame();
            for (int input = 0; input < 200; ++input) Require(!offer(12), "Blocked input manufactured another frame");
            Require(!frames.pending(), "Loading boundary retained movement debt");

            auto* parent = ParentOf(snapshot->root, focus);
            Require(parent != nullptr, "Fixture item parent missing");
            const auto prototype = *Find(snapshot->root, focus);
            for (int item = 0; item < 24; ++item) {
                auto next = prototype;
                Reidentify(next, L".arrived." + std::to_wstring(item));
                parent->children.push_back(std::move(next));
            }
            auto* arrivingScroll = Find(snapshot->root, scrollId);
            arrivingScroll->collectionGeneration = arrivingScroll->collectionGeneration.value_or(0) + 1;
            if (auto& window = arrivingScroll->virtualCollectionWindow; window) {
                ++window->requestGeneration;
                window->change = VirtualCollectionWindowChange::Append;
                // The synthetic page extends the provider's earlier estimate.
                // Keep it unknown instead of publishing a total smaller than
                // the now-realized window. This is an append, never a reset.
                window->totalItemCount.reset();
                window->hasAfter = false;
            }
            ++snapshot->sequence;
            initial = draw();
            Require(std::abs(initial.scrollViewports.at(scrollId).offset - boundary) < .01F, "Page arrival moved the existing viewport anchor");
            Require(offer(3, L"view-2"), "New range failed to resume");
            const auto arrived = frames.Take(L"view-2");
            Require(arrived && arrived->deltaDip == 3, "New range replayed old movement");
            const auto resumed = renderer.PlanFocusedFreeScroll(*snapshot, focus, axis, arrived->deltaDip, viewport, scrollId);
            Require(resumed && std::abs(resumed->offset - boundary - 3) < .01F, "Newly loaded items failed to extend the range");
            initial = draw();
            (void)offer(5, L"view-2"); (void)offer(-4, L"view-2");
            const auto reverse = frames.Take(L"view-2");
            Require(reverse && reverse->deltaDip == -4, "Reversal retained old direction");
            Require(renderer.PlanFocusedFreeScroll(*snapshot, focus, axis, reverse->deltaDip, viewport, scrollId).has_value(), "Reverse scroll failed");
            initial = draw();
            (void)offer(5, L"view-2");
            Require(!frames.Take(L"modal-scope") && !frames.pending(), "Another input scope inherited pending movement");
            std::cout << "SCROLL-CADENCE inputs=" << inputs << " paints=" << paints
                << " blocked-inputs=200 page-arrival=passed reversal=passed scope-cancel=passed\n";
            return 0;
        }
        for (const float fraction : {0.0F, .65F}) {
            const auto state = initial.scrollViewports.at(scrollId);
            const float distance = state.maximumOffset * fraction - state.offset;
            if (std::abs(distance) > .001F)
                Require(renderer.PlanFocusedFreeScroll(*snapshot, focus, axis, distance, viewport, scrollId).has_value(), "Positioning failed");
            initial = draw();
            std::vector<std::uint64_t> times, paint, preparation;
            std::uint64_t hits{}, misses{}, bytes{}, preparedNodes{}, deferredItems{};
            std::map<std::wstring, std::weak_ptr<void>> leases;
            std::map<std::wstring, std::uint64_t> changedBytes;
            for (const auto& node : initial.widgetComposition->nodes) leases[node.id] = node.rasterLease;
            for (int i = 0; i < 48; ++i) {
                Require(renderer.PlanFocusedFreeScroll(*snapshot, focus, axis, i < 24 ? 7.3F : -7.3F, viewport, scrollId).has_value(), "Scrolling failed");
                initial = draw(); const auto& timing = initial.timing;
                times.push_back(timing.totalMicroseconds); paint.push_back(timing.nodeDrawMicroseconds); preparation.push_back(timing.preparationMicroseconds);
                hits += timing.compositionPaintHits; misses += timing.compositionPaintMisses; bytes += timing.compositionPaintedBytes;
                preparedNodes += timing.preparedNodes; deferredItems += timing.deferredViewportItems;
                for (const auto& node : initial.widgetComposition->nodes) {
                    if (!node.bitmap || !node.rasterLease) continue;
                    const std::weak_ptr<void> current = node.rasterLease;
                    const auto& old = leases[node.id];
                    if (old.owner_before(current) || current.owner_before(old)) {
                        const auto pixels = node.bitmap->GetPixelSize();
                        changedBytes[node.id] += static_cast<std::uint64_t>(pixels.width) * pixels.height * 4;
                    }
                    leases[node.id] = current;
                }
            }
            std::sort(times.begin(), times.end()); std::sort(paint.begin(), paint.end()); std::sort(preparation.begin(), preparation.end());
            std::cout << "WIDGET-SCROLL fraction=" << fraction << " median-us=" << times[24] << " p95-us=" << times[45]
                << " paint-median-us=" << paint[24] << " prepare-median-us=" << preparation[24]
                << " hits=" << hits << " misses=" << misses << " painted-bytes=" << bytes
                << " compositor-background=" << initial.compositorBackground.has_value()
                << " prepared-nodes=" << preparedNodes / 48 << " deferred-items=" << deferredItems / 48 << '\n';
            std::vector<std::pair<std::uint64_t, std::wstring>> largest;
            for (const auto& [id, size] : changedBytes) largest.emplace_back(size, id);
            std::sort(largest.rbegin(), largest.rend());
            for (std::size_t i = 0; i < std::min<std::size_t>(5, largest.size()); ++i)
                std::wcout << L"HOT-RASTER bytes=" << largest[i].first << L" id=" << largest[i].second << L'\n';
        }
        return 0;
    } catch (const std::exception& error) { std::cerr << error.what() << '\n'; return 1; }
}
