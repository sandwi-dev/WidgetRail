#include "DeclarativeRenderer.h"
#include "WidgetBridgeClient.h"
#include "ControllerNavigation.h"
#include "FocusNavigation.h"
#include <psapi.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <algorithm>
#include <chrono>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <set>
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
void UseEagerCollections(WidgetNode& node) {
    for (auto& child : node.children) UseEagerCollections(child);
    if (!node.collectionLayout) return;
    const auto policy = *node.collectionLayout;
    node.collectionLayout.reset();
    if (!policy.adaptiveGrid) return;
    WidgetNode grid;
    grid.id = node.id + L".eager-grid";
    grid.kind = L"grid";
    grid.gridMinimumColumnWidth = policy.minimumColumnWidth;
    grid.gridMaximumColumns = policy.maximumColumns;
    if (const auto gap = node.baseStyle.find(L"gap"); gap != node.baseStyle.end())
        grid.baseStyle.emplace(*gap);
    grid.children = std::move(node.children);
    node.children = {std::move(grid)};
}
#include "CollectionWorkloadProfile.inl"
#include "CollectionPlacementComparison.inl"
}

int wmain(int argc, wchar_t** argv) {
    try {
        const bool profile = (argc == 5 || (argc == 6 && std::wstring_view(argv[5]) == L"no-paint-reuse")) && std::wstring_view(argv[2]) == L"--profile" &&
            (std::wstring_view(argv[4]) == L"realized" || std::wstring_view(argv[4]) == L"eager");
        Require(profile || argc == 2 || (argc == 3 && (std::wstring_view(argv[2]) == L"--cadence" || std::wstring_view(argv[2]) == L"--admission" || std::wstring_view(argv[2]) == L"--eager" || std::wstring_view(argv[2]) == L"--compare" || std::wstring_view(argv[2]) == L"--compare-retained")),
            "Usage: ScrollWorkloadProbe <renderer-fixture.json> [--cadence|--admission|--eager|--compare|--compare-retained|--profile COUNT realized|eager [no-paint-reuse]]");
        std::ifstream file(std::filesystem::path(argv[1]), std::ios::binary);
        Require(static_cast<bool>(file), "Fixture missing");
        std::string payload{std::istreambuf_iterator<char>(file), {}};
        std::wstring error;
        auto snapshot = testing::ParseWidgetSnapshotResponse(payload, error);
        if (!snapshot) { std::wcerr << error << '\n'; return 1; }
        if (argc == 3 && std::wstring_view(argv[2]) == L"--eager") UseEagerCollections(snapshot->root);
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
        if (argc == 3 && std::wstring_view(argv[2]) == L"--compare-retained") {
            CompareCollectionPlacement(*snapshot, d2d.Get(), write.Get(), images, target.Get(), canvas.Get());
            return 0;
        }
        if (profile) {
            ProfileCollection(*snapshot, std::stoul(argv[3]), std::wstring_view(argv[4]) == L"eager",
                d2d.Get(), write.Get(), images, target.Get(), argc != 6);
            return 0;
        }
        DeclarativeRenderer renderer(d2d.Get(), write.Get(), &images);
        DeclarativeRenderOptions options;
        options.deferScrollPreparation = true;
        options.pixelScale = 1.25F; options.compositorWidgetTransitions = true;
        options.compositorBackgroundAvailable = true; options.accessibility.reducedMotion = true;
        options.suppressFocusedDescendantFollow = true;
        options.sizeArtworkToDisplay = false; options.artworkDecodeSize = decode;
        const auto& hints = snapshot->surface;
        const float viewportWidth = hints && hints->widthMode != L"fillAvailable" ? static_cast<float>(hints->preferredWidth.value_or(980)) : 3974.0F;
        const float viewportHeight = hints && hints->heightMode != L"fillAvailable" ? static_cast<float>(hints->preferredHeight.value_or(700)) : 862.2F;
        declarative::Rect viewport{1, 1, viewportWidth, viewportHeight};
        std::wstring focus = snapshot->initialFocusId;
        if (argc == 3 && std::wstring_view(argv[2]) == L"--compare") {
            auto eagerSnapshot = *snapshot;
            UseEagerCollections(eagerSnapshot.root);
            auto comparisonOptions = options;
            comparisonOptions.compositorWidgetTransitions = false;
            comparisonOptions.compositorBackgroundAvailable = false;
            comparisonOptions.suppressFocusedDescendantFollow = false;
            for (const auto scale : {1.0F, 1.25F}) for (const auto size : {
                declarative::Size{620, 400}, declarative::Size{980, 700}, declarative::Size{1400, 862}}) {
                comparisonOptions.pixelScale = scale;
                comparisonOptions.responsiveViewport = size;
                target->SetDpi(96 * scale, 96 * scale);
                DeclarativeRenderer lazy(d2d.Get(), write.Get(), &images), eager(d2d.Get(), write.Get(), &images);
                const auto drawComparison = [&](DeclarativeRenderer& painter, const WidgetSnapshot& document) {
                    target->BeginDraw(); target->Clear(D2D1::ColorF(0, 0));
                    auto result = painter.Render(target.Get(), document, focus, {0, 0, size.width, size.height}, comparisonOptions);
                    Check(target->EndDraw());
                    Require(result.succeeded, "Comparison render failed");
                    return result;
                };
                const WICRect pixelBounds{0, 0, static_cast<INT>(size.width * scale), static_cast<INT>(size.height * scale)};
                const UINT stride = pixelBounds.Width * 4;
                std::vector<BYTE> actual(stride * pixelBounds.Height), expected(actual.size());
                // Both paths must observe completed synthetic artwork. First
                // paint requests background variants in addition to item art.
                (void)drawComparison(lazy, *snapshot);
                (void)drawComparison(eager, eagerSnapshot);
                const auto readyDeadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
                for (;;) {
                    if (images.GetStats().queuedOrLoading != 0) {
                        Require(std::chrono::steady_clock::now() < readyDeadline, "Comparison artwork did not settle");
                        std::this_thread::sleep_for(std::chrono::milliseconds(1));
                        continue;
                    }
                    (void)drawComparison(lazy, *snapshot);
                    (void)drawComparison(eager, eagerSnapshot);
                    if (images.GetStats().queuedOrLoading == 0) break;
                }
                const auto actualResult = drawComparison(lazy, *snapshot);
                Check(canvas->CopyPixels(&pixelBounds, stride, static_cast<UINT>(actual.size()), actual.data()));
                const auto expectedResult = drawComparison(eager, eagerSnapshot);
                Check(canvas->CopyPixels(&pixelBounds, stride, static_cast<UINT>(expected.size()), expected.data()));
                // Unmeasured extent intentionally estimates the scrollbar thumb.
                // Compare all content pixels while excluding only each actual
                // scrollbar track; layout and gutter geometry remain unchanged.
                for (const auto& [_, track] : actualResult.scrollbarTracks) {
                    const int left = std::clamp(static_cast<int>(std::floor(track.x * scale)), 0, pixelBounds.Width);
                    const int top = std::clamp(static_cast<int>(std::floor(track.y * scale)), 0, pixelBounds.Height);
                    const int right = std::clamp(static_cast<int>(std::ceil((track.x + track.width) * scale)), 0, pixelBounds.Width);
                    const int bottom = std::clamp(static_cast<int>(std::ceil((track.y + track.height) * scale)), 0, pixelBounds.Height);
                    for (int y = top; y < bottom; ++y) for (int x = left; x < right; ++x) {
                        const auto at = static_cast<std::size_t>(y) * stride + x * 4;
                        std::copy_n(expected.begin() + at, 4, actual.begin() + at);
                    }
                }
                for (const auto& [id, rect] : actualResult.elementRects) {
                    const auto match = expectedResult.elementRects.find(id);
                    Require(match != expectedResult.elementRects.end(), "Realized element absent from eager reference");
                    const auto visible = [&](const RenderResult& result) {
                        const auto box = result.elementVisibleRects.find(id);
                        return box != result.elementVisibleRects.end() && box->second.width > 0 && box->second.height > 0;
                    };
                    // Completely clipped decoration (including a zero-height
                    // poster scrim) does not define visible geometry. Compare
                    // all visible content and all interactive geometry.
                    if (!visible(actualResult) && !visible(expectedResult) &&
                        !actualResult.navigationRects.contains(id)) continue;
                    const auto& reference = match->second;
                    if (std::abs(rect.x - reference.x) > .02F || std::abs(rect.y - reference.y) > .02F ||
                        std::abs(rect.width - reference.width) > .02F || std::abs(rect.height - reference.height) > .02F) {
                        std::wcerr << L"GEOMETRY " << id << L" actual=" << rect.x << L"," << rect.y << L"," << rect.width << L"," << rect.height
                            << L" expected=" << reference.x << L"," << reference.y << L"," << reference.width << L"," << reference.height << L'\n';
                        const auto& a = actualResult.elementUnroundedRects.at(id);
                        const auto& e = expectedResult.elementUnroundedRects.at(id);
                        std::wcerr.precision(12);
                        std::wcerr << L"RAW actual=" << a.x << L"," << a.y << L"," << a.width << L"," << a.height
                            << L" expected=" << e.x << L"," << e.y << L"," << e.width << L"," << e.height << L'\n';
                        throw std::runtime_error("Collection geometry differs from eager reference");
                    }
                }
                if (actual != expected) {
                    std::size_t changed{};
                    int left = pixelBounds.Width, top = pixelBounds.Height, right{}, bottom{};
                    for (int y = 0; y < pixelBounds.Height; ++y) for (int x = 0; x < pixelBounds.Width; ++x) {
                        const auto at = static_cast<std::size_t>(y) * stride + x * 4;
                        if (std::equal(actual.begin() + at, actual.begin() + at + 4, expected.begin() + at)) continue;
                        ++changed; left = std::min(left, x); top = std::min(top, y); right = std::max(right, x); bottom = std::max(bottom, y);
                    }
                    std::cerr << "PIXELS width=" << size.width << " scale=" << scale << " changed=" << changed
                        << " bounds=" << left << ',' << top << ',' << right << ',' << bottom << '\n';
                    for (const auto& [name, pixels] : {std::pair{"actual", &actual}, std::pair{"expected", &expected}}) {
                        std::ofstream dump(std::filesystem::path(argv[1]).parent_path() / (std::string{name} + ".bgra"), std::ios::binary);
                        dump.write(reinterpret_cast<const char*>(pixels->data()), pixels->size());
                    }
                    throw std::runtime_error("Collection pixels differ from eager reference");
                }
                std::cout << "COLLECTION-COMPARE width=" << size.width << " scale=" << scale << " exact-content-pixels=passed prepared="
                    << actualResult.timing.preparedNodes << " eager-prepared=" << expectedResult.timing.preparedNodes << '\n';
            }
            return 0;
        }
        const auto draw = [&] {
            target->BeginDraw(); target->Clear(D2D1::ColorF(0, 0));
            auto result = renderer.Render(target.Get(), *snapshot, focus, viewport, options);
            Check(target->EndDraw());
            Require(result.succeeded, "Fixture render failed");
            if (argc != 3 || std::wstring_view(argv[2]) != L"--admission")
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
        if (argc == 3 && std::wstring_view(argv[2]) == L"--admission") {
            options.collectAccessibility = true;
            options.animationTimestampMilliseconds = 10000;
            options.accessibility.reducedMotion = true;
            DeclarativeRenderer reference(d2d.Get(), write.Get(), &images);
            auto referenceOptions = options;
            referenceOptions.disableRetainedLayoutForTesting = true;
            const auto compareRect = [&](const declarative::Rect& a, const declarative::Rect& b) {
                Require(std::abs(a.x-b.x) < .002F && std::abs(a.y-b.y) < .002F &&
                    std::abs(a.width-b.width) < .002F && std::abs(a.height-b.height) < .002F, "Retained layout differs from full reference");
            };
            const auto referenceDraw = [&] {
                referenceOptions = options;
                referenceOptions.disableRetainedLayoutForTesting = true;
                target->BeginDraw(); target->Clear(D2D1::ColorF(0, 0));
                auto result = reference.Render(target.Get(), *snapshot, focus, viewport, referenceOptions);
                Check(target->EndDraw());
                Require(result.succeeded, "Full reference render failed");
                return result;
            };
            auto baseline = referenceDraw();
            std::vector<std::uint64_t> optimized, full;
            std::size_t retainedMeasures{}, fullMeasures{}, comparisons{};
            auto verify = [&] {
                initial = draw(); baseline = referenceDraw();
                const auto maps = [&](const auto& actual, const auto& expected) {
                    Require(actual.size() == expected.size(), "Geometry membership differs");
                    for (const auto& [id, rect] : expected) { Require(actual.contains(id), "Geometry identity differs"); compareRect(actual.at(id), rect); }
                };
                maps(initial.elementRects, baseline.elementRects);
                maps(initial.elementVisibleRects, baseline.elementVisibleRects);
                maps(initial.navigationRects, baseline.navigationRects);
                maps(initial.focusRects, baseline.focusRects);
                Require(initial.navigationEnabled == baseline.navigationEnabled && initial.focusScopes == baseline.focusScopes &&
                    initial.revealableFocusIds == baseline.revealableFocusIds, "Focus state differs");
                // textLineCounts records executed paint calls, so cache hits
                // after failed-frame recovery legitimately differ. Compare
                // actual text pixels below instead of that execution trace.
                Require(initial.hitRegions.size() == baseline.hitRegions.size() &&
                    initial.accessibilityRegions.size() == baseline.accessibilityRegions.size(), "Semantic geometry count differs");
                for (std::size_t i=0;i<initial.hitRegions.size();++i) {
                    Require(initial.hitRegions[i].nodeId == baseline.hitRegions[i].nodeId && initial.hitRegions[i].enabled == baseline.hitRegions[i].enabled, "Hit identity differs");
                    compareRect(initial.hitRegions[i].rect, baseline.hitRegions[i].rect);
                }
                for (std::size_t i=0;i<initial.accessibilityRegions.size();++i) {
                    Require(initial.accessibilityRegions[i].nodeId == baseline.accessibilityRegions[i].nodeId, "Accessibility order differs");
                    compareRect(initial.accessibilityRegions[i].rect, baseline.accessibilityRegions[i].rect);
                }
                for (const auto& [id, state] : baseline.scrollViewports) {
                    const auto& actual = initial.scrollViewports.at(id);
                    compareRect(actual.rect, state.rect);
                    Require(std::abs(actual.offset-state.offset) < .002F && std::abs(actual.maximumOffset-state.maximumOffset) < .002F, "Cursor extent/anchor differs");
                }
                ++comparisons;
                return initial.timing.preparationMicroseconds;
            };
            verify();
            const auto move = [&](float delta) {
                const auto a = renderer.PlanFocusedFreeScroll(*snapshot, focus, axis, delta, viewport, scrollId);
                const auto b = reference.PlanFocusedFreeScroll(*snapshot, focus, axis, delta, viewport, scrollId);
                Require(a.has_value() == b.has_value(), "Scroll consumption differs");
                verify();
            };
            move(initial.scrollViewports.at(scrollId).maximumOffset * .62F);
            auto* parent = ParentOf(snapshot->root, focus);
            Require(parent != nullptr, "Item parent missing");
            const auto parentId = parent->id;
            const auto prototype = *Find(snapshot->root, focus);
            for (int page=0;page<12;++page) {
                parent = Find(snapshot->root, parentId);
                for (int i=0;i<7;++i) {
                    auto item = prototype; Reidentify(item, L".page." + std::to_wstring(page) + L"." + std::to_wstring(i));
                    parent->children.push_back(std::move(item));
                }
                auto* collection = Find(snapshot->root, scrollId);
                collection->collectionGeneration = collection->collectionGeneration.value_or(0) + 1;
                collection->collectionLoading = L"none";
                if (collection->virtualCollectionWindow) {
                    ++collection->virtualCollectionWindow->requestGeneration;
                    collection->virtualCollectionWindow->change = VirtualCollectionWindowChange::Append;
                    collection->virtualCollectionWindow->totalItemCount.reset();
                }
                ++snapshot->sequence;
                optimized.push_back(verify()); full.push_back(baseline.timing.preparationMicroseconds);
                retainedMeasures += initial.timing.intrinsicMeasures; fullMeasures += baseline.timing.intrinsicMeasures;
                move(page % 2 ? -7.3F : 13.7F);
            }
            // Non-row-aligned eviction, prepend, reorder, missing anchor, and
            // explicit reset all use existing cursor policy in both renderers.
            for (int change=0;change<5;++change) {
                parent = Find(snapshot->root, parentId);
                auto* collection = Find(snapshot->root, scrollId);
                if (change==0) {
                    parent->children.erase(parent->children.begin(), parent->children.begin()+3);
                    collection->collectionStartIndex = collection->collectionStartIndex.value_or(0)+3;
                } else if (change==1) {
                    auto item=prototype; Reidentify(item,L".prepend"); parent->children.insert(parent->children.begin(), std::move(item));
                    collection->collectionStartIndex = collection->collectionStartIndex.value_or(0)-1;
                } else if (change==2) std::reverse(parent->children.begin(),parent->children.end());
                else if (change==3) collection->collectionAnchorKey=L"missing-anchor";
                else collection->collectionResetGeneration=collection->collectionResetGeneration.value_or(0)+1;
                focus=parent->children[4].id;
                collection->collectionGeneration=collection->collectionGeneration.value_or(0)+1;
                if (collection->virtualCollectionWindow) {
                    ++collection->virtualCollectionWindow->requestGeneration;
                    collection->virtualCollectionWindow->change = change==1 ? VirtualCollectionWindowChange::Prepend : VirtualCollectionWindowChange::Replace;
                }
                ++snapshot->sequence; verify();
            }
            // Same-key text and inherited styles, current action/accessibility
            // routing, font/display scale, and responsive column changes.
            const auto mutateText = [&](const auto& self, WidgetNode& node)->bool {
                if (node.kind==L"text") { node.text=L"A substantially longer changed title that wraps at the current width"; return true; }
                for(auto& child:node.children) if(self(self,child)) return true;
                return false;
            };
            auto* changed=Find(snapshot->root,focus); mutateText(mutateText,*changed);
            changed->actionId=L"replacement-action"; changed->accessibilityLabel=L"replacement label";
            changed->isDisabled=true; ++snapshot->sequence; verify();
            for(float scale:{1.0F,1.25F,1.5F,2.0F}) {
                options.pixelScale=scale; options.accessibility.textScale=scale==2 ? 1.5F : 1.0F;
                viewport.width *= .91F; ++snapshot->sequence; verify();
            }
            options.surfaceBackground = NativeColor{.24F,.1F,.36F,1};
            options.accessibility.reducedTransparency=true; ++snapshot->sequence; verify();
            // Failed frame must not commit reusable admission state.
            options.failAfterNodeDrawForTesting=true;
            target->BeginDraw();
            auto rejected=renderer.Render(target.Get(),*snapshot,focus,viewport,options);
            Check(target->EndDraw()); Require(!rejected.succeeded,"Forced failure was committed");
            options.failAfterNodeDrawForTesting=false; verify();
            // Keep modal parent scroll/focus under its original authority, then
            // return to a retained full-page layout after the modal closes.
            auto pageRoot=snapshot->root;
            const auto oldScope=snapshot->activeInputScopeId;
            WidgetNode panel; panel.id=L"admission.modal.panel"; panel.kind=L"stack";
            panel.inputScopeId=L"admission.modal";
            panel.baseStyle[L"width"]={L"length",L"480px",480.0,L"px"};
            panel.baseStyle[L"height"]={L"length",L"320px",320.0,L"px"};
            WidgetNode close; close.id=L"admission.modal.close"; close.kind=L"button";
            close.text=L"Close"; close.actionId=L"close"; panel.children.push_back(close);
            WidgetNode modal; modal.id=L"admission.modal.layer"; modal.kind=L"modalLayer";
            modal.children={pageRoot,panel}; snapshot->root=std::move(modal);
            snapshot->activeInputScopeId=L"admission.modal";
            const auto parentFocus=focus; focus=close.id; ++snapshot->sequence; verify();
            snapshot->root=std::move(pageRoot); snapshot->activeInputScopeId=oldScope;
            focus=parentFocus; ++snapshot->sequence; verify();
            // Byte-for-byte raster reference under deterministic timing. This
            // exercises text wrapping, poster scrims, focused scale and depth.
            options.compositorWidgetTransitions=false;
            const auto pixels=[&] {
                std::vector<BYTE> data(5000*1125*4);
                Check(canvas->CopyPixels(nullptr,5000*4,static_cast<UINT>(data.size()),data.data()));
                return data;
            };
            viewport.width=std::min(viewport.width,2400.0F);
            viewport.height=std::min(viewport.height,540.0F);
            for(float scale:{1.0F,1.25F,1.5F,2.0F}) {
                options.pixelScale=scale;
                target->SetDpi(scale*96.0F,scale*96.0F);
                (void)draw(); const auto actual=pixels();
                (void)referenceDraw(); Require(actual==pixels(),"Retained admission pixels differ from full layout");
            }
            Require(retainedMeasures < fullMeasures, "Retained layout saved no intrinsic work");
            std::sort(optimized.begin(),optimized.end()); std::sort(full.begin(),full.end());

            std::cout << "CURSOR-ADMISSION comparisons=" << comparisons << " prepare-median-us=" << optimized[optimized.size()/2]
                << " full-median-us=" << full[full.size()/2] << " intrinsic=" << retainedMeasures << " full-intrinsic=" << fullMeasures << '\n';
            return 0;
        }
        if (argc == 3 && std::wstring_view(argv[2]) == L"--cadence") {
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
