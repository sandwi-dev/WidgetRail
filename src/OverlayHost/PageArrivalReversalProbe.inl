// Opt-in diagnostic regression. Uses the real renderer, but a deterministic
// one-preparation-job allowance instead of the Windows/DWM event loop.
// This isolates ordering; its ticks are NOT measured display frames or FPS.
struct PageReversalResult final {
    unsigned blocked{}, currentSlices{}, incomingSlices{}, admissionTick{999}, anchorFailures{}, incomplete{};
    bool pendingAfterMotion{};
};

PageReversalResult ProbePageReversal(bool grid, float scale, bool prepend,
    unsigned arrivalTick, int policy) {
    using namespace widgetrail;
    MotionRasterFixture fixture, reference;
    auto& renderer = *fixture.renderer;
    WidgetSnapshot current;
    current.instanceId = L"page.reversal"; current.sequence = 1;
    current.root = Node(L"items", L"scroll");
    current.root.scrollAxis = L"vertical"; current.root.showScrollbar = false;
    current.root.collectionAnchorKey = L"key.0";
    current.root.collectionLayout = WidgetNode::CollectionLayout{grid, 100,
        grid ? std::optional<double>{140} : std::nullopt, std::nullopt};
    current.root.baseStyle = {{L"gap", Length(8)}, {L"background", Color(L"#112233")}};
    const auto item = [&](int index) {
        auto node = FixedButton((L"item." + std::to_wstring(index)).c_str(), 100);
        node.collectionItemKey = L"key." + std::to_wstring(index);
        node.baseStyle[L"width"] = Length(100, L"%");
        node.baseStyle[L"background"] = Color(index % 2 ? L"#223344" : L"#445566");
        return node;
    };
    for (int i = 0; i < 80; ++i) current.root.children.push_back(item(i));
    const std::wstring focus = prepend ? L"item.0" : L"item.79";
    const Rect viewport{.4F, .8F, 580, 240};
    DeclarativeRenderOptions options;
    options.pixelScale = scale; options.deferScrollPreparation = true;
    options.accessibility.reducedMotion = true;
    (void)fixture.Draw(current, focus.c_str(), viewport, options);
    (void)reference.Draw(current, focus.c_str(), viewport, options);
    options.suppressFocusedDescendantFollow = true;
    auto shown = fixture.Draw(current, focus.c_str(), viewport, options);
    (void)reference.Draw(current, focus.c_str(), viewport, options);
    auto incoming = current; ++incoming.sequence;
    if (prepend) {
        std::vector<WidgetNode> before;
        for (int i = -16; i < 0; ++i) before.push_back(item(i));
        incoming.root.children.insert(incoming.root.children.begin(), before.begin(), before.end());
        incoming.root.collectionStartIndex = -16;
    } else {
        for (int i = 80; i < 96; ++i) incoming.root.children.push_back(item(i));
    }
    bool delivered{}, ready{}, admitted{};
    PageReversalResult stats;
    for (unsigned tick = 0; tick < 128; ++tick) {
        if (tick == 64) {
            stats.pendingAfterMotion = policy && !admitted;
            renderer.CancelScrollPreparation();
        }
        if (tick >= 64 && (!policy || admitted)) break;
        if (policy && tick == arrivalTick) delivered = true;
        if (ready && !admitted) {
            // A preflight never publishes scroll state. Admission must use
            // the viewport reached by intervening input, not its old position.
            auto admissionOptions = options;
            if (policy == 3) admissionOptions.disablePreparedCollectionFrameForTesting = true;
            auto next = fixture.Draw(incoming, focus.c_str(), viewport, admissionOptions);
            const auto synchronous = reference.Draw(incoming, focus.c_str(), viewport, options);
            bool anchorChecked{};
            for (const auto& node : current.root.children) {
                const auto old = shown.elementRects.find(node.id);
                const auto fresh = next.elementRects.find(node.id);
                if (old != shown.elementRects.end() && fresh != next.elementRects.end() &&
                    old->second.y >= viewport.y && old->second.y + old->second.height <= viewport.y + viewport.height) {
                    if (std::abs(old->second.y - fresh->second.y) > .01F) ++stats.anchorFailures;
                    anchorChecked = true; break;
                }
            }
            if (!anchorChecked) ++stats.anchorFailures;
            if (stats.anchorFailures) {
                std::cout << "ANCHOR-FAIL grid=" << grid << " scale=" << scale
                << " prepend=" << prepend << " policy=" << policy << " tick=" << tick
                << " before=" << shown.scrollOffsets.at(L"items") << " after=" << next.scrollOffsets.at(L"items")
                << " synchronous=" << synchronous.scrollOffsets.at(L"items")
                << " reused=" << next.timing.reusedCollectionPreparation << '\n';
                return stats; // Explicit red case, never continue on a jumped reference.
            }
            current = incoming; shown = std::move(next);
            admitted = true; stats.admissionTick = tick;
        }
        // Reverse away from the loaded edge while the provider result is
        // delayed, being prepared, and finally admitted.
        const float delta = prepend ? 24.0F : -24.0F;
        FocusedFreeScrollPlanDiagnostic diagnostic;
        const auto move = tick < 64 ? renderer.PlanPreparedFreeScroll(current, focus,
            declarative::ScrollAxis::Vertical, delta, viewport, L"items", options, &diagnostic)
            : std::optional<FocusedFreeScrollPlan>{};
        if (move) {
            Check(reference.renderer->PlanFocusedFreeScroll(current, focus,
                declarative::ScrollAxis::Vertical, delta, viewport, L"items").has_value(),
                "synchronous reference accepts reversal through loaded content");
            reference.renderer->CancelPresentationUpdatePlan();
            shown = fixture.Draw(current, focus.c_str(), viewport, options);
            const auto full = reference.Draw(current, focus.c_str(), viewport, options);
            Near(shown.scrollOffsets.at(L"items"), full.scrollOffsets.at(L"items"),
                "reversal has no accumulated distance or viewport jump");
        } else if (tick < 64) {
            Check(diagnostic.disposition == FocusedFreeScrollPlanDisposition::PreparationPending,
                "all held reversal samples are realization holds, not content edges");
            ++stats.blocked;
        }
        const auto prepareCurrent = [&] {
            const auto result = renderer.PreparePendingScroll(current, focus, {1, 1000000});
            Check(result.status != CollectionPreparationStatus::Failed, "current-view preparation remains valid");
            ++stats.currentSlices;
        };
        // Policy 1 mirrors the host order. Policy 2 is an experimental ordering
        // only: blocked current work first, optional lookahead still last.
        if (policy == 2 && !move && renderer.HasPendingScrollPreparation()) prepareCurrent();
        else if (delivered && !admitted && !ready) {
            const auto result = renderer.PrepareCollections(incoming, focus, viewport, options, {1, 1000000});
            Check(result.status != CollectionPreparationStatus::Failed, "incoming-page preparation remains valid");
            ++stats.incomingSlices;
            ready = result.status == CollectionPreparationStatus::Ready;
        } else if (renderer.HasPendingScrollPreparation()) prepareCurrent();
    }
    stats.incomplete = policy && !admitted;
    std::cout << "PAGE-REVERSAL grid=" << grid << " scale=" << scale << " prepend=" << prepend
        << " arrival=" << arrivalTick << " policy=" << policy << " blocked=" << stats.blocked
        << " current-slices=" << stats.currentSlices << " incoming-slices=" << stats.incomingSlices
        << " admitted=" << stats.admissionTick << " pending-after-motion=" << stats.pendingAfterMotion
        << " incomplete=" << stats.incomplete << '\n';
    return stats;
}

bool PageArrivalReversalProbe() {
    unsigned regressions{}, anchorRegressions{}, priorityFixes{}, lateAdmissions{}, incomplete{}, rebuildAnchorFixes{};
    for (bool grid : {false, true}) for (float scale : {1.0F, 1.25F}) for (bool prepend : {false, true}) {
        const auto baseline = ProbePageReversal(grid, scale, prepend, 0, 0);
        for (unsigned arrival : {0U, 6U, 18U}) {
            const auto host = ProbePageReversal(grid, scale, prepend, arrival, 1);
            const auto currentFirst = ProbePageReversal(grid, scale, prepend, arrival, 2);
            if (host.pendingAfterMotion || currentFirst.pendingAfterMotion) ++lateAdmissions;
            if (host.incomplete || currentFirst.incomplete) ++incomplete;
            if (host.anchorFailures || currentFirst.anchorFailures) {
                ++anchorRegressions;
                const auto rebuilt = ProbePageReversal(grid, scale, prepend, arrival, 3);
                if (!rebuilt.anchorFailures && !rebuilt.incomplete) ++rebuildAnchorFixes;
                continue;
            }
            if (host.blocked > baseline.blocked) {
                ++regressions;
                if (currentFirst.blocked <= baseline.blocked) ++priorityFixes;
            }
            std::cout << "REVERSAL-COMPARE extra-host-holds=" << static_cast<int>(host.blocked) - static_cast<int>(baseline.blocked)
                << " extra-current-first-holds=" << static_cast<int>(currentFirst.blocked) - static_cast<int>(baseline.blocked) << '\n';
        }
    }
    std::cout << "PAGE-REVERSAL extra-hold-cases=" << regressions << " anchor-failure-cases="
        << anchorRegressions << " priority-only-fixes=" << priorityFixes
        << " admissions-after-motion=" << lateAdmissions << " incomplete=" << incomplete << " cases=24\n";
    std::cout << "PAGE-REVERSAL rebuild-anchor-fixes=" << rebuildAnchorFixes << '\n';
    return regressions == 0 && anchorRegressions == 0 && incomplete == 0;
}
