// Included by the offscreen probe. Each invocation profiles one fixture/window
// in a fresh process; reported latency is host CPU work, not physical display time.
std::size_t ProfilePrivateBytes() {
    PROCESS_MEMORY_COUNTERS_EX counters{};
    Require(K32GetProcessMemoryInfo(GetCurrentProcess(),
        reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters), sizeof(counters)) != FALSE,
        "Process memory accounting unavailable");
    return counters.PrivateUsage;
}

void ProfileCollection(WidgetSnapshot snapshot, const std::size_t requestedCount, const bool eager,
    ID2D1Factory* d2d, IDWriteFactory* write, RemoteImageCache& images, ID2D1RenderTarget* target) {
    WidgetNode* collection{};
    const auto choose = [&](const auto& self, WidgetNode& node) -> void {
        if (node.collectionLayout && (!collection || node.children.size() > collection->children.size())) collection = &node;
        for (auto& child : node.children) self(self, child);
    };
    choose(choose, snapshot.root);
    Require(collection && requestedCount > 0 && requestedCount <= collection->children.size(),
        "Profile count must fit the exported production collection");
    collection->children.resize(requestedCount);
    const auto scrollId = collection->id;
    const auto axis = collection->scrollAxis == L"horizontal"
        ? declarative::ScrollAxis::Horizontal : declarative::ScrollAxis::Vertical;
    std::set<std::wstring> itemIds;
    for (const auto& item : collection->children) itemIds.insert(item.id);
    std::wstring focus = collection->children.front().id;
    collection->collectionAnchorKey = collection->children.front().collectionItemKey;
    collection->collectionStartIndex = 0;
    collection->collectionGeneration = 1;
    collection->collectionResetGeneration = 1;
    collection->virtualCollectionWindow.reset();
    collection->collectionNavigation.reset();
    collection->collectionLoading.clear();
    collection->scrollNearStartActionId.clear();
    collection->scrollNearEndActionId.clear();
    // The profile owns a finite synthetic dataset. Provider waits are measured
    // separately by admission/cadence scenarios, not silently mixed into samples.
    if (eager) UseEagerCollections(snapshot.root);
    const declarative::Rect viewport{0, 0, 980, 700};
    target->SetDpi(120, 120);
    DeclarativeRenderOptions options;
    options.pixelScale = 1.25F;
    options.compositorWidgetTransitions = true;
    options.compositorBackgroundAvailable = true;
    options.deferPublication = true;
    options.animationTimestampMilliseconds = 10000;
    options.sizeArtworkToDisplay = false;
    options.artworkDecodeSize = {192, 320};
    const auto privateBefore = ProfilePrivateBytes();
    DeclarativeRenderer renderer(d2d, write, &images);
    const auto micros = [](const auto start) {
        return static_cast<std::uint64_t>(std::chrono::duration_cast<std::chrono::microseconds>(
            std::chrono::steady_clock::now() - start).count());
    };
    const auto prepare = [&](const std::wstring_view requestedFocus) {
        for (unsigned slices = 1; slices <= 256; ++slices) {
            const auto result = renderer.PrepareCollections(snapshot, requestedFocus, viewport, options, {4, 1500});
            Require(result.status != CollectionPreparationStatus::Failed, "Profile preparation failed");
            if (result.status == CollectionPreparationStatus::Ready) return slices;
        }
        throw std::runtime_error("Profile preparation did not converge");
    };
    const auto draw = [&] {
        target->BeginDraw(); target->Clear(D2D1::ColorF(0, 0));
        auto result = renderer.Render(target, snapshot, focus, viewport, options);
        Check(target->EndDraw());
        Require(result.succeeded && renderer.CommitFramePublication(result.publicationId), "Profile frame not published");
        return result;
    };
    const auto coldStarted = std::chrono::steady_clock::now();
    const auto coldSlices = prepare(focus);
    auto rendered = draw();
    const auto coldUs = micros(coldStarted);
    const auto artworkDeadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
    while (images.GetStats().queuedOrLoading != 0) {
        Require(std::chrono::steady_clock::now() < artworkDeadline, "Profile artwork did not settle");
        std::this_thread::sleep_for(std::chrono::milliseconds(1));
    }
    rendered = draw();
    std::size_t peakPrivate = ProfilePrivateBytes(), peakRaster{};
    for (const bool scrolling : {false, true}) {
        options.suppressFocusedDescendantFollow = scrolling;
        std::vector<std::uint64_t> inputCpu, frames, preparation, nodes, slices;
        std::size_t moved{}, blocked{};
        for (unsigned sample = 0; sample < 96; ++sample) {
            options.animationTimestampMilliseconds = 11000 + (scrolling ? 2000 : 0) + sample * 16;
            const auto started = std::chrono::steady_clock::now();
            unsigned preparedSlices{};
            if (!scrolling) {
                const bool forward = (sample / 24) % 2 == 0;
                const auto direction = axis == declarative::ScrollAxis::Horizontal
                    ? (forward ? input::NavigationDirection::Right : input::NavigationDirection::Left)
                    : (forward ? input::NavigationDirection::Down : input::NavigationDirection::Up);
                const auto next = input::FindDirectionalFocusTargetInOwningSubtrees(snapshot.root, focus, direction, rendered, {});
                if (next.target && itemIds.contains(*next.target)) {
                    // Match FocusRealizationIntent::Stage: measured navigation
                    // targets move directly; only unrealized targets preflight.
                    if (!rendered.navigationRects.contains(*next.target) && rendered.realizableFocusIds.contains(*next.target))
                        preparedSlices = prepare(*next.target);
                    (void)renderer.PlanFocusUpdate(snapshot, focus, *next.target, viewport);
                    focus = *next.target; ++moved;
                } else ++blocked;
            } else {
                const float delta = (sample / 24) % 2 == 0 ? 7.3F : -7.3F;
                bool settled{};
                for (unsigned attempt = 1; attempt <= 256; ++attempt) {
                    FocusedFreeScrollPlanDiagnostic diagnostic;
                    const auto plan = renderer.PlanPreparedFreeScroll(snapshot, focus, axis, delta, viewport,
                        scrollId, options, &diagnostic, {4, 1500});
                    preparedSlices = attempt;
                    if (plan) { ++moved; settled = true; break; }
                    if (diagnostic.disposition != FocusedFreeScrollPlanDisposition::PreparationPending) {
                        Require(diagnostic.disposition != FocusedFreeScrollPlanDisposition::PreparationFailed,
                            "Profile scroll preparation failed");
                        ++blocked; settled = true; break;
                    }
                }
                Require(settled, "Profile scroll preparation did not converge");
            }
            rendered = draw();
            inputCpu.push_back(micros(started));
            if (!scrolling) Require(rendered.focusRects.contains(focus), "Profile focused item was not revealed");
            frames.push_back(rendered.timing.totalMicroseconds);
            preparation.push_back(rendered.timing.preparationMicroseconds);
            nodes.push_back(rendered.timing.preparedNodes);
            slices.push_back(preparedSlices);
            peakPrivate = std::max(peakPrivate, ProfilePrivateBytes());
            if (rendered.widgetComposition) {
                std::set<ID2D1Bitmap*> unique;
                std::size_t bytes{};
                for (const auto& node : rendered.widgetComposition->nodes) if (node.bitmap && unique.insert(node.bitmap.Get()).second) {
                    const auto size = node.bitmap->GetPixelSize();
                    bytes += static_cast<std::size_t>(size.width) * size.height * 4;
                }
                peakRaster = std::max(peakRaster, bytes);
            }
        }
        const auto percentile = [](auto values, const std::size_t percent) {
            std::sort(values.begin(), values.end());
            return values[(values.size() - 1) * percent / 100];
        };
        std::cout << "COLLECTION-PROFILE mode=" << (eager ? "eager" : "realized") << " retained=" << requestedCount
            << " viewport=980x700 scale=1.25 phase=" << (scrolling ? "scroll" : "focus") << " samples=" << inputCpu.size()
            << " moved=" << moved << " blocked=" << blocked << " cold-us=" << coldUs << " cold-slices=" << coldSlices
            << " input-cpu-p50-us=" << percentile(inputCpu, 50) << " input-cpu-p95-us=" << percentile(inputCpu, 95)
            << " frame-p50-us=" << percentile(frames, 50) << " frame-p95-us=" << percentile(frames, 95)
            << " prepare-p50-us=" << percentile(preparation, 50) << " prepare-p95-us=" << percentile(preparation, 95)
            << " prepared-p50=" << percentile(nodes, 50) << " prepared-p95=" << percentile(nodes, 95)
            << " slices-p95=" << percentile(slices, 95)
            << " private-growth-bytes=" << (peakPrivate > privateBefore ? peakPrivate - privateBefore : 0)
            << " active-raster-peak-bytes=" << peakRaster << '\n';
    }
}
