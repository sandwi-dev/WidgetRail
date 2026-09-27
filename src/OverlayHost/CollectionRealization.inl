// RenderPass implementation. Collection geometry owns item placement; Taffy
// measures only realized item subtrees beneath one outer scroll boundary.

static bool SameCollectionMeasurement(const WidgetNode& a, const WidgetNode& b) {
    if (a.id != b.id || a.kind != b.kind || a.text != b.text || a.baseStyle != b.baseStyle ||
        a.visibleWhen != b.visibleWhen || a.actionSurfaceOrientation != b.actionSurfaceOrientation ||
        a.actionSurfacePresentation != b.actionSurfacePresentation ||
        a.gridMinimumColumnWidth != b.gridMinimumColumnWidth || a.gridMaximumColumns != b.gridMaximumColumns ||
        a.indicatorSize != b.indicatorSize || a.controllerPrompt != b.controllerPrompt ||
        a.imageSource.empty() != b.imageSource.empty() || a.artworkHandle.empty() != b.artworkHandle.empty() ||
        a.glyph.empty() != b.glyph.empty() || a.packageIcon.has_value() != b.packageIcon.has_value() ||
        a.isSelected != b.isSelected || a.isBusy != b.isBusy || a.isDisabled != b.isDisabled ||
        a.children.size() != b.children.size()) return false;
    for (std::size_t i = 0; i < a.children.size(); ++i)
        if (!SameCollectionMeasurement(a.children[i], b.children[i])) return false;
    return true;
}

CollectionRenderState& CollectionState(const WidgetNode& node) {
    const auto key = ScrollStateKey(node.id);
    auto found = collections.find(key);
    if (found == collections.end()) {
        const auto previous = owner->collections_.find(key);
        found = collections.emplace(key, previous == owner->collections_.end()
            ? CollectionRenderState{} : previous->second).first;
        if (const auto branch = std::ranges::find_if(owner->collectionPreparations_,
                [&](const auto& candidate) { return candidate.Matches(*snapshot); });
            branch != owner->collectionPreparations_.end()) {
            if (const auto warm = branch->collections.find(key); warm != branch->collections.end()) {
                // Reuse measurements, never an uncommitted anchor/extent tree.
                // Source/context comparison below revalidates them even when
                // a newer request supersedes a partially prepared snapshot.
                found->second.items = warm->second.items;
                found->second.context = warm->second.context;
                found->second.contextRevision = warm->second.contextRevision;
                found->second.itemRevision = warm->second.itemRevision;
                found->second.resetGeneration = warm->second.resetGeneration;
            }
        }
    }
    if (!collectionSlots.contains(node.id)) collectionSlots.emplace(node.id, collectionSlots.size());
    collectionNodes[node.id] = &node;
    return found->second;
}

// Placement may reuse a committed measure result only while its realized
// window covers every required/protected item. Crossing that window goes back
// through measurement; estimated geometry never substitutes for an item layout.
enum class CollectionCoverage { VisibleAndProtected, BufferedAndProtected };

bool CollectionPlacementIsCovered(
    const CollectionCoverage coverage = CollectionCoverage::BufferedAndProtected) const {
    if (collectionNodes.empty()) return false;
    for (const auto& [id, node] : collectionNodes) {
        const auto* box = layout.Find(NarrowStableId(id));
        const auto found = collections.find(ScrollStateKey(id));
        if (!box || found == collections.end()) return false;
        const auto& state = found->second;
        if (!state.contextRevision || state.geometry.Size() != node->children.size() ||
            state.resetGeneration != node->collectionResetGeneration.value_or(0)) return false;
        // The outer correction pass can slightly change the final containing
        // block. Do not reuse measurements made against its earlier estimate,
        // even if both dimensions round to the same displayed pixel.
        const auto width = std::max(0.0F, box->unscrolledContentBox.width);
        const auto height = std::max(0.0F, box->unscrolledContentBox.height);
        const auto columnWidth = state.context.horizontal ? width :
            std::max(0.0F, (width - static_cast<float>(state.geometry.Columns() - 1) * state.columnGap) /
                static_cast<float>(state.geometry.Columns()));
        if (state.context.width != columnWidth || state.context.height != height) return false;
        const auto scroll = ScrollState().find(ScrollStateKey(id));
        const auto offset = scroll == ScrollState().end() ? box->scrollOffset : scroll->second.offset;
        const auto available = state.context.horizontal ? width : height;
        const auto range = state.AdmittedScrollRange(available);
        // Focus/UIA reveal uses displayed bounds and can request a fractional
        // overshoot at an estimated provider edge. Measurement must reconcile
        // that request before placement, just as for a new realization window.
        if (offset < range.first - .001F || offset > range.second + .001F) return false;
        std::vector<std::wstring> protectedKeys;
        for (const auto& item : node->children) {
            if (item.id == focusedId || item.id == options.realizeElementId ||
                (node->collectionNavigation && item.id == node->collectionNavigation->targetFocusId))
                protectedKeys.push_back(item.collectionItemKey);
        }
        const auto demand = state.geometry.Plan(offset - state.leadingExtent,
            available, 1, protectedKeys, node->children.size());
        const auto ready = [&](const std::size_t index) {
            if (index >= node->children.size() ||
                std::ranges::find(state.realized, index) == state.realized.end()) return false;
            const auto& item = node->children[index];
            const auto cached = state.items.find(item.collectionItemKey);
            return cached != state.items.end() && cached->second.measuredContext == state.contextRevision &&
                !cached->second.layout.boxes.empty() && layout.Find(NarrowStableId(item.id));
        };
        const auto begin = coverage == CollectionCoverage::BufferedAndProtected ? demand.bufferedBegin : demand.visibleBegin;
        const auto end = coverage == CollectionCoverage::BufferedAndProtected ? demand.bufferedEnd : demand.visibleEnd;
        for (auto index = begin; index < end; ++index)
            if (!ready(index)) return false;
        for (const auto& key : protectedKeys)
            if (const auto index = state.geometry.Find(key); index && !ready(*index)) return false;
    }
    return true;
}

bool PrepareRetainedCollectionLayout() {
    // A completed ahead-of-viewport batch must be adopted through the normal
    // transactional layout path. Otherwise retaining the old placement would
    // discard the newly prepared rows when this frame is acknowledged.
    if (std::ranges::any_of(owner->collectionPreparations_, [&](const auto& branch) {
            return branch.ready && branch.Matches(*snapshot);
        })) return false;
    // The caller has already proved an unchanged admitted snapshot, viewport
    // and measurement options. Copy only committed collection state, never a
    // speculative measurement batch from another frame.
    VisitScrollNodes(snapshot->root, [&](const WidgetNode& node) {
        if (!node.collectionLayout || !layout.Find(NarrowStableId(node.id))) return;
        const auto key = ScrollStateKey(node.id);
        if (const auto prior = owner->collections_.find(key); prior != owner->collections_.end())
            collections.emplace(key, prior->second);
        collectionNodes.emplace(node.id, &node);
    });
    if (!CollectionPlacementIsCovered(CollectionCoverage::VisibleAndProtected)) {
        // A full pass must still be allowed to adopt a ready preparation batch.
        // Do not strand committed-only entries ahead of CollectionState's warm merge.
        collections.clear();
        collectionNodes.clear();
        return false;
    }
    for (const auto& [id, node] : collectionNodes) {
        auto& state = collections.at(ScrollStateKey(id));
        for (const auto& item : node->children)
            if (item.id == focusedId) state.lastFocusedKey = item.collectionItemKey;
    }
    ProjectScrollOffsets(true);
    PrepareAgainstCurrentLayout();
    retainedLayoutPhase = 1;
    AttachCollectionLayouts(layout);
    retainedLayoutPhase = -1;
    return true;
}

void PrepareCollectionItem(const WidgetNode& item, CollectionItemLayout& cached,
    const std::string& parentId, const CollectionRenderState& state,
    const std::wstring_view scope, const std::optional<NativeColor>& background) {
    auto surrounding = std::move(layout);
    layout = cached.layout;
    const auto textScale = std::clamp(options.accessibility.textScale, 0.85F, 1.5F);
    const float itemWidth = state.columnWidth;
    const float parentHeight = state.context.height;
    const float font = state.context.style.fontSizePx() / textScale;
    auto root = PrepareNode(item, parentId, itemWidth, parentHeight, font, background, scope);
    if (cached.measuredContext != state.contextRevision || cached.layout.boxes.empty()) {
        if (preparationBudget) {
            const auto elapsed = std::chrono::duration_cast<std::chrono::microseconds>(
                std::chrono::steady_clock::now() - preparationStarted).count();
            if (preparationMeasurements >= preparationBudget->maximumNewMeasurements ||
                (preparationMeasurements != 0 && static_cast<std::uint64_t>(elapsed) >= preparationBudget->maximumMicroseconds))
                throw CollectionSlicePending{};
            ++preparationMeasurements;
        }
        LayoutOptions settings;
        settings.pixelScale = options.pixelScale;
        settings.fillAutoRoot = false;
        settings.fillAutoRootWidth = true;
        settings.intrinsicRootHeight = !state.context.horizontal;
        settings.responsiveViewport = options.responsiveViewport.value_or(Size{viewport.width, viewport.height});
        const auto measure = [this](const LayoutElement& element, const declarative::MeasureConstraints& constraints) {
            return MeasureLeaf(element, constraints);
        };
        const auto itemRoot = [&] {
            // Measure under a real containing block. Treating the item itself
            // as Taffy's root changes margin placement, shrink-to-fit sizing
            // and horizontal cross-axis stretch.
            LayoutElement containingBlock;
            containingBlock.id = std::string(1, '\x1f') + "item-measure";
            containingBlock.width = itemWidth;
            if (state.context.adaptiveGrid) {
                containingBlock.layoutMode = LayoutMode::ResponsiveGrid;
                containingBlock.gridMinimumColumnWidth = static_cast<float>(protocol_contract::MinimumGridColumnWidth);
                containingBlock.gridMaximumColumns = 1;
            }
            if (state.context.horizontal) {
                containingBlock.height = parentHeight;
                containingBlock.direction = LayoutDirection::Row;
                containingBlock.scrollAxis = declarative::ScrollAxis::Horizontal;
            }
            containingBlock.children.push_back(root);
            return containingBlock;
        };
        {
            PreparationTimer timer{layoutNanoseconds};
            layout = declarative::ComputeLayout(itemRoot(), {0, 0, itemWidth, parentHeight}, measure, settings);
        }
        result.timing.intrinsicMeasures += layout.intrinsicMeasures;
        // Preserve the same parent-relative correction semantics as the normal
        // renderer, now bounded to this item's subtree.
        root = PrepareNode(item, parentId, itemWidth, parentHeight, font, background, scope);
        {
            PreparationTimer timer{layoutNanoseconds};
            layout = declarative::ComputeLayout(itemRoot(), {0, 0, itemWidth, parentHeight}, measure, settings);
        }
        result.timing.intrinsicMeasures += layout.intrinsicMeasures;
        cached.layout = layout;
        cached.measuredContext = state.contextRevision;
        cached.text.clear();
        cached.queries.clear();
        const auto save = [&](const auto& self, const WidgetNode& node) -> void {
            if (const auto text = textMeasurements.find(node.id); text != textMeasurements.end())
                cached.text.emplace(node.id, text->second);
            if (const auto queries = textMeasurementQueries.find(node.id); queries != textMeasurementQueries.end())
                cached.queries.emplace(node.id, queries->second);
            for (const auto& child : node.children) self(self, child);
        };
        save(save, item);
    } else {
        for (const auto& [id, proof] : cached.text) textMeasurements.insert_or_assign(id, proof);
        for (const auto& [id, proofs] : cached.queries) textMeasurementQueries.insert_or_assign(id, proofs);
    }
    layout = std::move(surrounding);
}

LayoutElement PrepareCollection(const WidgetNode& node, LayoutElement element,
    const NativeRenderStyle& style, const std::wstring_view scope,
    const std::optional<NativeColor>& background) {
    auto& state = CollectionState(node);
    const auto& policy = *node.collectionLayout;
    const auto id = NarrowStableId(node.id);
    const bool horizontal = element.scrollAxis == declarative::ScrollAxis::Horizontal;
    const auto* box = layout.Find(id);
    // The estimate pass establishes the actual containing block. No item UI
    // is prepared under the root's guessed parent width.
    if (retainedLayoutPhase != 0 && box) {
        // Track geometry uses unrounded constraints, just like the containing
        // Taffy grid. Snap only after projecting into the actual pixel phase.
        const auto width = std::max(0.0F, box->unscrolledContentBox.width);
        const auto height = std::max(0.0F, box->unscrolledContentBox.height);
        const auto columnGap = style.gapPx().right;
        const auto columns = policy.adaptiveGrid
            ? static_cast<std::size_t>(std::clamp(std::floor(
                (width + columnGap) / (static_cast<float>(*policy.minimumColumnWidth) + columnGap)),
                1.0F, static_cast<float>(policy.maximumColumns.value_or(protocol_contract::MaximumGridColumns))))
            : 1U;
        state.columnWidth = horizontal ? width
            : std::max(0.0F, (width - static_cast<float>(columns - 1) * columnGap) / static_cast<float>(columns));
        state.columnGap = columnGap;
        const CollectionMeasureContext context{style, state.columnWidth, height,
            viewport.width, viewport.height, options.rootFontSizePx, options.pixelScale,
            options.accessibility.textScale, static_cast<int>(options.accessibility.minimumFontWeight), compactMode, horizontal,
            options.playStationControls, policy.adaptiveGrid};
        const auto offsetEntry = ScrollState().find(ScrollStateKey(node.id));
        double offset = offsetEntry == ScrollState().end() ? box->scrollOffset : offsetEntry->second.offset;
        const auto anchor = state.geometry.CaptureAnchor(offset - state.leadingExtent);
        if (state.resetGeneration != node.collectionResetGeneration.value_or(0)) {
            state.items.clear();
            state.lastFocusedKey.clear();
            state.resetGeneration = node.collectionResetGeneration.value_or(0);
        }
        if (!state.contextRevision || state.context != context || options.accessibility.contrastHook) {
            state.context = context;
            ++state.contextRevision;
        }
        std::vector<collection::Item> descriptors;
        descriptors.reserve(node.children.size());
        std::set<std::wstring, std::less<>> admitted;
        for (const auto& item : node.children) {
            admitted.insert(item.collectionItemKey);
            auto& cached = state.items[item.collectionItemKey];
            if (!cached.revision || !cached.source || !SameCollectionMeasurement(*cached.source, item)) {
                cached = {};
                cached.source = std::make_shared<const WidgetNode>(item);
                cached.revision = ++state.itemRevision;
            }
            descriptors.push_back({item.collectionItemKey, cached.revision, policy.estimatedItemExtent});
            if (item.id == focusedId) state.lastFocusedKey = item.collectionItemKey;
        }
        std::erase_if(state.items, [&](const auto& entry) { return !admitted.contains(entry.first); });
        const auto lineGap = horizontal ? style.gapPx().right : style.gapPx().top;
        const auto start = node.virtualCollectionWindow && node.virtualCollectionWindow->firstItemIndex
            ? static_cast<std::int64_t>(*node.virtualCollectionWindow->firstItemIndex)
            : node.collectionStartIndex.value_or(0);
        state.leadingExtent = 0;
        state.trailingExtent = 0;
        if (node.virtualCollectionWindow && node.virtualCollectionWindow->firstItemIndex) {
            const auto first = *node.virtualCollectionWindow->firstItemIndex;
            state.leadingExtent = (first / columns) * (policy.estimatedItemExtent + lineGap);
            if (node.virtualCollectionWindow->totalItemCount) {
                const auto total = *node.virtualCollectionWindow->totalItemCount;
                const auto totalLines = (total + columns - 1) / columns;
                const auto admittedEnd = (first + node.children.size() + columns - 1) / columns;
                if (totalLines > admittedEnd)
                    state.trailingExtent = (totalLines - admittedEnd) * (policy.estimatedItemExtent + lineGap);
            }
        }
        if (!state.geometry.Replace({ScrollStateKey(node.id), node.collectionResetGeneration.value_or(0)},
                {columns, start, lineGap, state.contextRevision}, std::move(descriptors),
                !node.scrollNearStartActionId.empty(), !node.scrollNearEndActionId.empty())) {
            Add(node.id, L"invalid_collection_geometry", L"Collection geometry could not be admitted.", RenderDiagnosticSeverity::Error);
            return element;
        }
        const double available = horizontal ? width : height;
        const bool initialPosition = (newCollectionScrollStates.contains(node.id) || resetCollections.contains(node.id)) &&
            initializedCollectionScrolls.insert(node.id).second;
        if (initialPosition) offset = state.leadingExtent;
        else if (anchor) {
            if (const auto restored = state.geometry.RestoreAnchor(*anchor, available))
                offset = *restored + state.leadingExtent;
        }
        std::vector<std::wstring> protectedKeys;
        if (!state.lastFocusedKey.empty()) protectedKeys.push_back(state.lastFocusedKey);
        if (!options.realizeElementId.empty()) {
            for (const auto& item : node.children)
                if (item.id == options.realizeElementId) protectedKeys.push_back(item.collectionItemKey);
        }
        if (node.collectionNavigation) {
            for (const auto& item : node.children)
                if (item.id == node.collectionNavigation->targetFocusId) protectedKeys.push_back(item.collectionItemKey);
        }
        std::set<std::size_t> preparedItems;
        for (;;) {
            const auto demand = state.geometry.Plan(offset - state.leadingExtent, available, 1, protectedKeys, node.children.size());
            state.realized.clear();
            for (auto index = demand.bufferedBegin; index < demand.bufferedEnd; ++index) state.realized.push_back(index);
            state.realized.insert(state.realized.end(), demand.protectedItems.begin(), demand.protectedItems.end());
            if (preparationBudget) {
                // Required/protected targets precede optional adjacent work.
                // Placement and painting remain in semantic item order.
                std::stable_sort(state.realized.begin(), state.realized.end(), [&](const auto a, const auto b) {
                    const auto priority = [&](const auto index) {
                        return std::find(protectedKeys.begin(), protectedKeys.end(), node.children[index].collectionItemKey) != protectedKeys.end() ? 0
                            : index >= demand.visibleBegin && index < demand.visibleEnd ? 1 : 2;
                    };
                    return priority(a) < priority(b);
                });
            }
            std::vector<collection::Measurement> measurements;
            for (const auto index : state.realized) {
                if (!preparedItems.insert(index).second) continue;
                const auto& item = node.children[index];
                auto& cached = state.items.at(item.collectionItemKey);
                PrepareCollectionItem(item, cached, id, state, scope, background);
                const auto* itemBox = cached.layout.Find(NarrowStableId(item.id));
                if (!itemBox || !cached.layout.valid()) {
                    Add(item.id, L"collection_item_measurement_failed", L"Collection item layout failed.", RenderDiagnosticSeverity::Error);
                    continue;
                }
                const auto& itemStyle = prepared.at(NarrowStableId(item.id)).baseStyle;
                const auto extent = horizontal
                    ? itemBox->unscrolledBorderBox.x + itemBox->unscrolledBorderBox.width + std::max(0.0F, itemStyle.marginPx().right)
                    : itemBox->unscrolledBorderBox.y + itemBox->unscrolledBorderBox.height + std::max(0.0F, itemStyle.marginPx().bottom);
                measurements.push_back({index, std::max<double>(collection::CollectionLayoutState::MinimumItemExtent, extent)});
            }
            if (measurements.empty()) break;
            if (!state.geometry.Measure(state.geometry.Generation(), measurements)) {
                Add(node.id, L"collection_measurement_rejected", L"Collection item measurement exceeded valid bounds.", RenderDiagnosticSeverity::Error);
                break;
            }
            if (!initialPosition && anchor) {
                if (const auto restored = state.geometry.RestoreAnchor(*anchor, available))
                    offset = *restored + state.leadingExtent;
            }
            // Newly measured lines may expose additional items. As in a lazy
            // measure pass, fill until the viewport is covered. Each descriptor
            // is measured at most once in this pass, never a guessed fixed count.
        }
        std::set<std::wstring, std::less<>> retained;
        for (const auto index : state.realized) retained.insert(node.children[index].collectionItemKey);
        for (auto& [key, cached] : state.items) {
            // A sliced focus reveal visits its old viewport before the target
            // viewport. Keep bounded intermediate measurements until the
            // transaction finishes, rather than repeatedly evicting its work.
            if (!preparationBudget && !retained.contains(key)) { cached.layout = {}; cached.text.clear(); cached.queries.clear(); cached.measuredContext = 0; }
        }
        if (!measurementOnly) StoreScrollOffset(ScrollStateKey(node.id), static_cast<float>(offset));
        element.scrollOffset = static_cast<float>(offset);
    }
    element.children.clear();
    element.gap = 0;
    element.crossGap = 0;
    element.wrap = declarative::WrapBehavior::NoWrap;
    element.mainAxisAlignment = declarative::MainAxisAlignment::Start;
    if (!node.children.empty()) {
        LayoutElement content;
        content.id = std::string(1, '\x1f') + "collection." + std::to_string(collectionSlots.at(node.id));
        content.flexGrow = 0;
        content.flexShrink = 0;
        content.estimatesOffWindowScrollExtent = true;
        const auto estimate = state.geometry.Size() == node.children.size()
            ? state.leadingExtent + state.geometry.Extent() + state.trailingExtent
            : node.children.size() * policy.estimatedItemExtent;
        if (horizontal) content.width = static_cast<float>(estimate);
        else content.height = static_cast<float>(estimate);
        element.children.push_back(std::move(content));
    }
    return element;
}

void AttachCollectionLayouts(declarative::LayoutResult& computed) {
    if (retainedLayoutPhase == 0) return;
    for (const auto& [id, node] : collectionNodes) {
        const auto* scroll = computed.Find(NarrowStableId(id));
        if (!scroll) continue;
        const auto& state = collections.at(ScrollStateKey(id));
        RenderLogicalCollection logical;
        logical.policy = *node->collectionLayout;
        logical.containerStyle = node->baseStyle;
        logical.inputScope = prepared.at(NarrowStableId(id)).inputScope;
        logical.columns = state.geometry.Columns();
        logical.firstColumn = state.geometry.Size() ? state.geometry.At(0).column : 0;
        logical.startIndex = node->collectionStartIndex.value_or(0);
        logical.resetGeneration = node->collectionResetGeneration.value_or(0);
        if (node->virtualCollectionWindow) logical.firstItemIndex = node->virtualCollectionWindow->firstItemIndex;
        for (const auto& item : node->children) {
            logical.itemIdentities.emplace_back(item.id, item.collectionItemKey);
            if (scroll->visibleBox.width > 0 && scroll->visibleBox.height > 0) {
                result.realizableFocusIds.insert(item.id);
                result.navigationEnabled[item.id] = !item.isDisabled && !item.isBusy;
                result.focusScopes[item.id] = logical.inputScope;
            }
        }
        result.logicalCollections.insert_or_assign(id, std::move(logical));
        const bool horizontal = scroll->scrollAxis == declarative::ScrollAxis::Horizontal;
        const auto clip = Intersection(scroll->visibleBox, scroll->contentBox);
        const float translatedX = scroll->unscrolledBorderBox.x - scroll->unroundedBorderBox.x + (horizontal ? scroll->scrollOffset : 0);
        const float translatedY = scroll->unscrolledBorderBox.y - scroll->unroundedBorderBox.y + (horizontal ? 0 : scroll->scrollOffset);
        for (const auto index : state.realized) {
            const auto& item = node->children[index];
            const auto& cached = state.items.at(item.collectionItemKey);
            const auto position = state.geometry.At(index);
            const float x = scroll->unscrolledContentBox.x + (horizontal ? static_cast<float>(state.leadingExtent + position.offset)
                : static_cast<float>(position.column) * (state.columnWidth + state.columnGap));
            const float y = scroll->unscrolledContentBox.y + (horizontal ? 0 : static_cast<float>(state.leadingExtent + position.offset));
            const auto project = [&](const auto& self, const WidgetNode& child, Rect ancestorClip) -> void {
                const auto name = NarrowStableId(child.id);
                const auto* local = cached.layout.Find(name);
                if (!local) return;
                auto placed = *local;
                placed.unscrolledBorderBox.x += x;
                placed.unscrolledBorderBox.y += y;
                placed.unscrolledContentBox.x += x;
                placed.unscrolledContentBox.y += y;
                declarative::ProjectLayoutBox(placed, translatedX, translatedY, ancestorClip, options.pixelScale);
                computed.boxes.insert_or_assign(name, placed);
                if (placed.clipsDescendants) ancestorClip = Intersection(ancestorClip, placed.contentBox);
                for (const auto& descendant : child.children) self(self, descendant, ancestorClip);
            };
            project(project, item, clip);
        }
    }
}
