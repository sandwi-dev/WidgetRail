// Included in the renderer test namespace. Compare retained scrolling pixels
// against forced repainting with identical input/layout state, not a mock cache.
void CompositionScrollRetention(float scale, bool grid, bool horizontal, bool benchmark = false,
    bool largeCoordinates = false, bool translatedViewport = true, bool deepScroll = true) {
    using namespace widgetrail;
    using Microsoft::WRL::ComPtr;
    ComPtr<ID2D1Factory> d2d; ComPtr<IDWriteFactory> write; ComPtr<IWICImagingFactory> wic;
    ComPtr<IWICBitmap> canvas; ComPtr<ID2D1RenderTarget> target;
    const auto ok = [](HRESULT hr) { Check(SUCCEEDED(hr), "scroll retention resources"); };
    ok(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.GetAddressOf()));
    ok(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory), reinterpret_cast<IUnknown**>(write.GetAddressOf())));
    ok(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(wic.GetAddressOf())));
    const UINT width = static_cast<UINT>(std::ceil(800 * scale)), height = static_cast<UINT>(std::ceil(600 * scale));
    ok(wic->CreateBitmap(width, height, GUID_WICPixelFormat32bppPBGRA, WICBitmapCacheOnLoad, canvas.GetAddressOf()));
    ok(d2d->CreateWicBitmapRenderTarget(canvas.Get(), D2D1::RenderTargetProperties(), target.GetAddressOf()));
    target->SetDpi(96 * scale, 96 * scale);
    RemoteImageCache artwork({}, {}, [](std::wstring_view, std::stop_token, const RemoteImageLimits&) {
        RemoteDecodedImage image; image.width = image.height = 64; image.stride = 256;
        image.premultipliedBgra.resize(64 * 64 * 4);
        for (unsigned y = 0; y < 64; ++y) for (unsigned x = 0; x < 64; ++x) {
            const auto at = (y * 64 + x) * 4;
            image.premultipliedBgra[at] = static_cast<BYTE>(x * 4);
            image.premultipliedBgra[at + 1] = static_cast<BYTE>(y * 4);
            image.premultipliedBgra[at + 2] = ((x / 8 + y / 8) % 2) ? 230 : 40;
            image.premultipliedBgra[at + 3] = 255;
        }
        return RemoteImageFetchResult{S_OK, std::move(image), {}};
    });
    const std::wstring imageUrl = L"https://example.test/scroll-poster.png";
    const ImageDecodeSize decode{64, 64};
    (void)artwork.Request(imageUrl, decode);
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
    const auto imageKey = RemoteImageCache::VariantKey(imageUrl, decode);
    while (artwork.GetState(imageKey) != RemoteImageState::Ready && std::chrono::steady_clock::now() < deadline)
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    Check(artwork.GetState(imageKey) == RemoteImageState::Ready, "scroll artwork ready");
    DeclarativeRenderer retained(d2d.Get(), write.Get(), &artwork), fresh(d2d.Get(), write.Get(), &artwork);
    WidgetSnapshot snapshot; snapshot.instanceId = L"scroll.retention"; snapshot.sequence = 1;
    snapshot.root = Node(L"root", L"stack");
    snapshot.root.baseStyle = {{L"padding", LengthList(L"16px")}};
    auto scroll = Node(L"scroll", L"scroll"); scroll.scrollAxis = horizontal ? L"horizontal" : L"vertical";
    scroll.showScrollbar = !grid;
    scroll.baseStyle = {{L"width", Length(760)}, {L"height", Length(540)}, {L"padding", LengthList(L"12px")},
        {L"background", Color(L"#171a21")}, {L"flex-shrink", Number(0)}};
    auto items = Node(L"items", grid ? L"grid" : horizontal ? L"row" : L"stack");
    items.gridMinimumColumnWidth = 160; items.gridMaximumColumns = 4;
    items.baseStyle = {{L"gap", LengthList(L"8px")}, {L"flex-shrink", Number(0)}};
    for (int i = 0; i < (largeCoordinates ? 240 : 48); ++i) {
        const auto id = L"item." + std::to_wstring(i);
        auto item = Node(id.c_str(), L"actionSurface"); item.actionId = L"open";
        item.collectionItemKey = id;
        item.baseStyle = {{L"height", Length(grid ? 124 : 56)}, {L"flex-shrink", Number(0)},
            {L"background", Color(L"#263344")}, {L"color", Color(L"#ffffff")},
            {L"corner-radius", Length(12)}, {L"overflow", Keyword(L"clip")},
            {L"padding", LengthList(L"8px")}, {L"surface-shading", Number(.05)},
            {L"shadow-color", Color(L"#00000060")}, {L"shadow-blur", Length(5)}};
        if (horizontal) item.baseStyle[L"width"] = Length(180);
        item.focusedStyle = {{L"background", Color(L"#486388")}};
        item.pressedStyle = {{L"scale", Number(.96)}};
        auto label = Node((id + L".label").c_str(), L"text"); label.text = L"Library entry " + std::to_wstring(i);
        label.baseStyle = {{L"font-size", Length(16)}, {L"color", Color(L"#ffffff")}};
        if (grid) {
            item.actionSurfacePresentation = L"poster";
            item.actionSurfaceOrientation = L"vertical";
            item.baseStyle[L"padding"] = LengthList(L"0px");
            auto image = Node((id + L".art").c_str(), L"image"); image.imageSource = imageUrl; image.imageFit = L"cover";
            auto scrim = Node((id + L".scrim").c_str(), L"stack");
            scrim.baseStyle = {{L"height", Length(40)}, {L"width", Length(100, L"%")},
                {L"padding", LengthList(L"4px")}, {L"background", Color(L"#101010b0")}};
            scrim.children.push_back(label); item.children = {image, scrim};
        } else item.children.push_back(label);
        items.children.push_back(item);
    }
    scroll.children.push_back(items); snapshot.root.children.push_back(scroll);
    const Rect viewport{largeCoordinates && translatedViewport ? 1400.0F : 0.0F,
        largeCoordinates && translatedViewport ? 200.0F : 0.0F, 800, 600};
    DeclarativeRenderOptions options; options.pixelScale = scale;
    options.compositorWidgetTransitions = true; options.accessibility.reducedMotion = true;
    options.sizeArtworkToDisplay = false; options.artworkDecodeSize = decode;
    options.suppressFocusedDescendantFollow = true;
    const auto draw = [&](DeclarativeRenderer& renderer, bool reference, int frame) {
        auto current = options;
        // Authority changes invalidate pixels without resetting scroll state.
        if (reference) current.packageContentDigest = std::to_wstring(frame);
        target->BeginDraw();
        auto result = renderer.Render(target.Get(), snapshot, L"item.0", viewport, current);
        ok(target->EndDraw());
        Check(result.succeeded && result.widgetComposition && !result.widgetComposition->directContent,
            "scroll retention stays in bounded composition mode");
        return result;
    };
    const auto replay = [&](const RenderResult& result) {
        target->BeginDraw(); target->Clear(D2D1::ColorF(0, 0));
        for (const auto& node : result.widgetComposition->nodes) {
            if (!node.bitmap) continue;
            const auto& nodes = result.widgetComposition->nodes;
            const auto parent = std::find_if(nodes.begin(), nodes.end(), [&](const auto& n) { return n.id == node.parent; });
            if (parent != nodes.end() && parent->kind == WidgetCompositionKind::FocusSurface) {
                const bool selected = parent->key.starts_with(L"item.0\x1f");
                if ((node.order == 1) != selected) continue;
            }
            target->DrawBitmap(node.bitmap.Get(), D2D1::RectF(node.bounds.x - viewport.x, node.bounds.y - viewport.y,
                node.bounds.x + node.bounds.width - viewport.x, node.bounds.y + node.bounds.height - viewport.y));
        }
        ok(target->EndDraw());
        std::vector<BYTE> pixels(width * height * 4);
        ok(canvas->CopyPixels(nullptr, width * 4, static_cast<UINT>(pixels.size()), pixels.data()));
        return pixels;
    };
    draw(retained, false, 0); draw(fresh, true, 0);
    if (largeCoordinates && deepScroll) {
        for (auto* renderer : {&retained, &fresh})
            Check(renderer->PlanFocusedFreeScroll(snapshot, L"item.0", declarative::ScrollAxis::Vertical, 3000,
                viewport, L"scroll").has_value(), "deep collection scroll plans");
        draw(retained, false, 0); draw(fresh, true, 0);
    }
    std::uint64_t hits{}, misses{}, bytes{}, referenceBytes{};
    std::vector<std::uint64_t> timings, referenceTimings;
    const int frames = benchmark ? 48 : 16;
    for (int frame = 1; frame <= frames; ++frame) {
        const auto axis = horizontal ? declarative::ScrollAxis::Horizontal : declarative::ScrollAxis::Vertical;
        // Fractional input deltas exercise snapping at noninteger display scales.
        const float delta = frame <= frames / 2 ? 7.3F : -7.3F;
        Check(retained.PlanFocusedFreeScroll(snapshot, L"item.0", axis, delta, viewport, L"scroll").has_value(), "retained scroll plans");
        Check(fresh.PlanFocusedFreeScroll(snapshot, L"item.0", axis, delta, viewport, L"scroll").has_value(), "reference scroll plans");
        const auto actual = draw(retained, false, frame), expected = draw(fresh, true, frame);
        Check(actual.scrollOffsets == expected.scrollOffsets, "retained pixels never change scroll state");
        const auto actualPixels = replay(actual), expectedPixels = replay(expected);
        if (actualPixels != expectedPixels) {
            std::size_t count{}; int largest{};
            for (std::size_t i = 0; i < actualPixels.size(); ++i) {
                if (actualPixels[i] != expectedPixels[i]) ++count;
                largest = std::max(largest, std::abs(static_cast<int>(actualPixels[i]) - expectedPixels[i]));
            }
            std::cerr << "scroll pixel mismatch scale=" << scale << " grid=" << grid << " horizontal=" << horizontal
                << " frame=" << frame << " channels=" << count << " max-difference=" << largest << '\n';
            for (const auto& n : actual.widgetComposition->nodes) {
                if (!n.bitmap) continue;
                const auto& expectedNodes = expected.widgetComposition->nodes;
                const auto other = std::find_if(expectedNodes.begin(), expectedNodes.end(), [&](const auto& v) { return v.id == n.id; });
                if (other == expectedNodes.end()) continue;
                auto one = actual, two = expected;
                auto a = std::make_shared<WidgetCompositionScene>(), b = std::make_shared<WidgetCompositionScene>();
                a->nodes.push_back(n); b->nodes.push_back(*other); one.widgetComposition = a; two.widgetComposition = b;
                if (replay(one) != replay(two)) std::wcerr << L"different raster " << n.id << L" bounds=" << n.bounds.x << L"," << n.bounds.y << L"," << n.bounds.width << L"," << n.bounds.height
                    << L" expected=" << other->bounds.x << L"," << other->bounds.y << L"," << other->bounds.width << L"," << other->bounds.height << L'\n';
            }
        }
        Check(actualPixels == expectedPixels, "translated captures exactly match fresh clipped pixels");
        Check(actual.fullLayoutBuildCount == 0, "scrolling retains layout independently of pixels");
        hits += actual.widgetComposition->paintCacheHits; misses += actual.widgetComposition->paintCacheMisses;
        bytes += actual.widgetComposition->paintedBytes; referenceBytes += expected.widgetComposition->paintedBytes;
        timings.push_back(actual.timing.totalMicroseconds); referenceTimings.push_back(expected.timing.totalMicroseconds);
    }
    std::sort(timings.begin(), timings.end()); std::sort(referenceTimings.begin(), referenceTimings.end());
    std::cout << "SCROLL scale=" << scale << " grid=" << grid << " horizontal=" << horizontal << " large=" << largeCoordinates
        << " translated=" << translatedViewport
        << " deep=" << deepScroll
        << " hits=" << hits << " misses=" << misses << " bytes=" << bytes << " fresh-bytes=" << referenceBytes
        << " median-us=" << timings[timings.size()/2] << " fresh-median-us=" << referenceTimings[referenceTimings.size()/2] << '\n';
    if (!benchmark) {
        Check(hits > misses, "scrolling reuses most capture layers");
        Check(bytes < referenceBytes / 2, "scrolling repaints less than half of fresh capture bytes");
    }
    const auto compareUpdate = [&](int revision) {
        ++snapshot.sequence;
        const auto actual = draw(retained, false, frames + revision), expected = draw(fresh, true, frames + revision);
        Check(replay(actual) == replay(expected), "scrolled captures invalidate exactly after content/clip/cursor changes");
        Check(actual.focusRects.size() == expected.focusRects.size(), "retained capture preserves current focus eligibility");
        for (const auto& [id, bounds] : actual.focusRects) {
            Check(expected.focusRects.contains(id), "retained capture cannot resurrect a retired focus target");
            const auto& reference = expected.focusRects.at(id);
            Near(bounds.x, reference.x, "capture reuse preserves input x");
            Near(bounds.y, reference.y, "capture reuse preserves input y");
            Near(bounds.width, reference.width, "capture reuse preserves input width");
            Near(bounds.height, reference.height, "capture reuse preserves input height");
        }
    };
    auto& liveItems = snapshot.root.children[0].children[0].children;
    auto& changedLabel = grid ? liveItems[1].children[1].children[0] : liveItems[1].children[0];
    changedLabel.text = L"Updated library title";
    liveItems[1].baseStyle[L"background"] = Color(L"#993322");
    compareUpdate(1);
    options.pressedElementId = L"item.1";
    compareUpdate(2);
    options.pressedElementId.clear();
    snapshot.root.children[0].baseStyle[L"width"] = Length(700);
    snapshot.root.children[0].baseStyle[L"height"] = Length(430);
    compareUpdate(3);
    // Cursor eviction and reordering retain only current semantic identities.
    liveItems.erase(liveItems.begin(), liveItems.begin() + 4);
    std::swap(liveItems[0], liveItems[1]);
    snapshot.root.children[0].collectionGeneration = 2;
    compareUpdate(4);
    snapshot.root.children[0].collectionResetGeneration = 2;
    compareUpdate(5);
    snapshot.root.baseStyle[L"translate-y"] = Length(.125);
    compareUpdate(6);
    snapshot.root.kind = L"scroll"; snapshot.root.scrollAxis = L"vertical";
    snapshot.root.baseStyle[L"height"] = Length(380);
    compareUpdate(7);
    Check(retained.PlanFocusedFreeScroll(snapshot, L"item.4", declarative::ScrollAxis::Vertical, 13.7F, viewport, L"root").has_value(),
        "outer nested scroll plans with retained pixels");
    Check(fresh.PlanFocusedFreeScroll(snapshot, L"item.4", declarative::ScrollAxis::Vertical, 13.7F, viewport, L"root").has_value(),
        "outer nested reference scroll plans");
    const auto nested = draw(retained, false, frames + 8), nestedReference = draw(fresh, true, frames + 8);
    Check(replay(nested) == replay(nestedReference), "nested scroll clipping remains exact after translation");
}
