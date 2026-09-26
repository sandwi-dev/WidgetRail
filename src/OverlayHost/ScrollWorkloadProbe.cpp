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
        Require(argc == 2 || (argc == 3 && (std::wstring_view(argv[2]) == L"--cadence" || std::wstring_view(argv[2]) == L"--admission")),
            "Usage: ScrollWorkloadProbe <renderer-fixture.json> [--cadence|--admission]");
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
        declarative::Rect viewport{1, 1, viewportWidth, viewportHeight};
        std::wstring focus = snapshot->initialFocusId;
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
