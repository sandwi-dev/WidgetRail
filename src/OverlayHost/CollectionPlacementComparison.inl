// Exact stateful comparison of placement reuse with full collection layout.
// Uses exported production widget trees/styles, not reconstructed sample cards.
void CompareCollectionPlacement(const WidgetSnapshot& snapshot, ID2D1Factory* d2d,
    IDWriteFactory* write, RemoteImageCache& images, ID2D1RenderTarget* target, IWICBitmap* canvas) {
    const WidgetNode* collection{};
    const auto choose = [&](const auto& self, const WidgetNode& node) -> void {
        if (node.collectionLayout && (!collection || node.children.size() > collection->children.size())) collection = &node;
        for (const auto& child : node.children) self(self, child);
    };
    choose(choose, snapshot.root);
    Require(collection && !collection->children.empty(), "Placement fixture needs collection items");
    const auto axis = collection->scrollAxis == L"horizontal" ? declarative::ScrollAxis::Horizontal : declarative::ScrollAxis::Vertical;
    for (const float scale : {1.0F, 1.25F}) for (const auto size : {
        declarative::Size{620, 400}, declarative::Size{980, 700}, declarative::Size{1400, 862}}) {
        DeclarativeRenderer retained(d2d, write, &images), full(d2d, write, &images);
        DeclarativeRenderOptions options;
        options.accessibility.reducedMotion = true;
        options.pixelScale = scale; options.responsiveViewport = size;
        options.sizeArtworkToDisplay = false; options.artworkDecodeSize = {192, 320};
        options.collectAccessibility = true;
        options.deferPublication = true;
        target->SetDpi(scale * 96, scale * 96);
        const declarative::Rect viewport{0, 0, size.width, size.height};
        const WICRect area{0, 0, static_cast<INT>(size.width * scale), static_cast<INT>(size.height * scale)};
        const auto stride = static_cast<UINT>(area.Width * 4);
        std::vector<BYTE> actual(stride * area.Height), expected(actual.size());
        std::wstring focus = collection->children.front().id;
        std::size_t reused{}, frames{};
        const auto draw = [&](DeclarativeRenderer& renderer, std::vector<BYTE>& bytes) {
            target->BeginDraw(); target->Clear(D2D1::ColorF(0, 0));
            auto result = renderer.Render(target, snapshot, focus, viewport, options);
            Check(target->EndDraw());
            Require(result.succeeded && renderer.CommitFramePublication(result.publicationId), "Placement comparison frame failed");
            Check(canvas->CopyPixels(&area, stride, static_cast<UINT>(bytes.size()), bytes.data()));
            return result;
        };
        const auto compare = [&] {
            const auto a = draw(retained, actual), b = draw(full, expected);
            if (actual != expected) {
                std::cerr << "PLACEMENT-PIXELS width=" << size.width << " scale=" << scale << " frame=" << frames
                    << " reused=" << (a.fullLayoutBuildCount == 0) << '\n';
                for (const auto& [id, scroll] : a.scrollViewports) {
                    const auto match = b.scrollViewports.find(id);
                    if (match != b.scrollViewports.end()) std::wcerr << L"SCROLL " << id << L" offset=" << scroll.offset
                        << L" expected=" << match->second.offset << L" extent=" << scroll.maximumOffset
                        << L" expected-extent=" << match->second.maximumOffset << L'\n';
                }
                std::size_t changed{}; int left = area.Width, top = area.Height, right{}, bottom{};
                for (int y = 0; y < area.Height; ++y) for (int x = 0; x < area.Width; ++x) {
                    const auto at = static_cast<std::size_t>(y) * stride + x * 4;
                    if (std::equal(actual.begin() + at, actual.begin() + at + 4, expected.begin() + at)) continue;
                    ++changed; left = std::min(left, x); top = std::min(top, y); right = std::max(right, x); bottom = std::max(bottom, y);
                }
                std::cerr << "DIFF pixels=" << changed << " bounds=" << left << ',' << top << ',' << right << ',' << bottom << '\n';
                throw std::runtime_error("Retained placement differs from full-layout pixels");
            }
            Require(a.realizableFocusIds == b.realizableFocusIds, "Placement loses logical focus targets");
            for (const auto& [id, rect] : a.elementVisibleRects) {
                if (rect.width <= 0 || rect.height <= 0) continue;
                const auto found = b.elementVisibleRects.find(id);
                Require(found != b.elementVisibleRects.end(), "Placement lost visible content");
                Require(std::abs(rect.x - found->second.x) < .01F && std::abs(rect.y - found->second.y) < .01F &&
                    std::abs(rect.width - found->second.width) < .01F && std::abs(rect.height - found->second.height) < .01F,
                    "Placement differs from full-layout visible geometry");
            }
            reused += a.fullLayoutBuildCount == 0;
            ++frames;
        };
        // Resolve asynchronous variants before comparing their pixels.
        (void)draw(retained, actual); (void)draw(full, expected);
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
        while (images.GetStats().queuedOrLoading != 0) {
            Require(std::chrono::steady_clock::now() < deadline, "Placement artwork did not settle");
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        }
        compare();
        for (std::size_t step = 0; step < 10; ++step) {
            const auto next = collection->children[step % std::min<std::size_t>(4, collection->children.size())].id;
            if (next == focus) (void)retained.PlanRetainedPaint(snapshot);
            else (void)retained.PlanFocusUpdate(snapshot, focus, next, viewport);
            focus = next;
            compare();
        }
        options.suppressFocusedDescendantFollow = true;
        for (const float delta : {.4F, .4F, 30.0F, 300.0F, 900.0F, -700.0F, -50.0F, -.4F}) {
            const auto a = retained.PlanFocusedFreeScroll(snapshot, focus, axis, delta, viewport, collection->id);
            const auto b = full.PlanFocusedFreeScroll(snapshot, focus, axis, delta, viewport, collection->id);
            Require(a.has_value() == b.has_value(), "Placement changed scroll admission");
            full.CancelPresentationUpdatePlan();
            compare();
        }
        options.realizeElementId = collection->children.back().id;
        (void)retained.PlanRetainedPaint(snapshot);
        compare();
        Require(reused > 0, "Placement comparison never exercised reuse");
        std::cout << "COLLECTION-PLACEMENT width=" << size.width << " scale=" << scale
            << " frames=" << frames << " reused=" << reused << " exact-pixels-and-geometry=passed\n";
    }
}
