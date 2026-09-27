// Included inside DeclarativeRenderer::RenderPass. Painting is split at semantic
// region boundaries, not copied from an already flattened window. Every band
// traverses the same clipping tree but paints only its assigned operations.
bool PaintCompositionPhase(std::wstring_view id, int phase) const {
    if (!composingScene)
        return true;
    const auto found = compositionPhases.find({std::wstring(id), phase});
    return found != compositionPhases.end() && static_cast<int>(found->second) == compositionBand;
}

bool CompositorControlsEnabled() {
    if (!compositorControlSupport) {
        const auto live = [&](const auto &self, const WidgetNode &node) -> bool {
            return node.kind == L"mediaViewport" || node.kind == L"windowPreview" ||
                std::any_of(node.children.begin(), node.children.end(), [&](const auto &child) { return self(self, child); });
        };
        compositorControlSupport = options.compositorWidgetTransitions && !live(live, snapshot->root) &&
            !RasterWidgetMotionEnabled();
    }
    return *compositorControlSupport;
}

bool UsesCompositorControlScale(const WidgetNode &node) {
    if ((node.kind != L"button" && node.kind != L"actionSurface") || !CompositorControlsEnabled()) return false;
    bool scales{};
    for (const auto *style : {&node.baseStyle, &node.focusedStyle, &node.pressedStyle}) {
        // Keep the existing exact spring evaluator until the compositor can
        // represent that curve too; never silently reinterpret authored easing.
        const auto easing = style->find(L"transition-easing");
        if (easing != style->end() && easing->second.text == L"spring") return false;
        const auto scale = style->find(L"scale");
        scales = scales || (scale != style->end() && scale->second.number && *scale->second.number != 1);
    }
    return scales;
}

void RestoreControlScaleFallback() {
    for (auto &[id, shown] : presentation) {
        const auto style = prepared.find(id);
        if (style != prepared.end() && UsesCompositorControlScale(*style->second.node))
            shown.motion.value.scale = style->second.paintStyle.scale();
    }
}

bool DrawWidgetComposition() {
    if (!options.compositorWidgetTransitions || !target)
        return false;
    auto scene = std::make_shared<WidgetCompositionScene>();
    scene->authority =
        snapshot->instanceId + L"\x1f" + options.artworkAuthorityId + L"\x1f" + options.packageContentDigest;
    scene->viewport = viewport;
    scene->scale = options.pixelScale;
    scene->cornerRadius = options.surfaceCornerRadiusPx;
    scene->reducedMotion = options.accessibility.reducedMotion || options.suppressWidgetCompositionMotion;
    scene->animations = options.widgetAnimations;
    const auto hasLiveSurface = [&](const auto &self, const WidgetNode &node) -> bool {
        return node.kind == L"mediaViewport" || node.kind == L"windowPreview" ||
               std::any_of(node.children.begin(), node.children.end(),
                           [&](const auto &child) { return self(self, child); });
    };
    // Live external surfaces have their own compositor placement. Keep their
    // containing widget stationary until that placement can share this scene.
    if (hasLiveSurface(hasLiveSurface, snapshot->root)) {
        scene->directContent = true;
        scene->reducedMotion = true;
        scene->ProtectResources();
    result.widgetComposition = std::move(scene);
        return false;
    }
    const auto needsLayers = [&](const auto &self, const WidgetNode &node) -> bool {
        if (node.transition || node.kind == L"modalLayer" || node.kind == L"loadingIndicator" ||
            (!node.collectionLoading.empty() && node.collectionLoading != L"idle") || UsesCompositorControlScale(node))
            return true;
        return std::any_of(node.children.begin(), node.children.end(),
                           [&](const auto &child) { return self(self, child); });
    };
    if (focusedId.empty() && !needsLayers(needsLayers, snapshot->root) &&
        owner->compositionInstance_ != snapshot->instanceId) {
        scene->directContent = true;
        scene->ProtectResources();
    result.widgetComposition = std::move(scene);
        return false;
    }
    std::optional<std::size_t> band;
    std::map<std::wstring, std::wstring> focusParents;
    const WidgetNode *sceneFocus{};
    std::wstring focusScope;
    std::wstring focusKey;
    bool focusTargetsOverflow{};
    std::map<std::wstring, std::wstring> parents;
    std::map<const WidgetNode*, std::size_t> paintOrder;
    std::vector<std::wstring> path;
    std::map<std::size_t, std::vector<std::pair<const WidgetNode*, int>>> paintOperations;
    std::map<std::wstring, const WidgetNode*> captureRoots;
    std::map<std::size_t, const WidgetNode*> captureBands;
    const auto captureBounds = [&](const WidgetNode& root) {
        const auto id = NarrowStableId(root.id);
        const auto& box = presentation.at(id).borderBox;
        const auto& item = prepared.at(id);
        auto bounds = UnionRect(widgetrail::surface::PaintBounds(box, item.baseStyle, options.pixelScale),
            widgetrail::surface::PaintBounds(box, item.paintStyle, options.pixelScale));
        if (const auto focused = compositionFocusStyles.find(root.id); focused != compositionFocusStyles.end())
            bounds = UnionRect(bounds, widgetrail::surface::PaintBounds(box, focused->second, options.pixelScale));
        return bounds;
    };
    const auto split = [&] { band.reset(); };
    const auto addGroup = [&](std::wstring id, std::wstring parent, WidgetCompositionKind kind,
                              std::wstring clock, std::wstring key, int order, Rect bounds, Rect clip) {
        split();
        if (const auto root = captureRoots.find(parent); root != captureRoots.end()) captureRoots[id] = root->second;
        scene->nodes.push_back(
            {id, std::move(parent), std::move(clock), std::move(key), kind, order, bounds, clip});
        return id;
    };
    const auto op = [&](const WidgetNode &node, int phase, const std::wstring &parent, Rect bounds) {
        const auto capture = captureRoots.find(parent);
        const bool independentClip = (phase < 3 || phase == 5) && capture != captureRoots.end();
        if (independentClip) {
            const auto& shown = presentation.at(NarrowStableId(node.id));
            const auto& style = prepared.at(NarrowStableId(node.id)).paintStyle;
            bounds = phase == 0 || phase == 5
                ? widgetrail::surface::PaintBounds(shown.borderBox, style, options.pixelScale)
                : shown.borderBox;
            if (&node == capture->second && (phase == 0 || phase == 5)) bounds = captureBounds(node);
            bounds = Intersection(bounds, captureBounds(*capture->second));
        } else bounds = Intersection(bounds, viewport);
        if (bounds.width <= .01F || bounds.height <= .01F)
            return;
        if (!band || scene->nodes[*band].parent != parent) {
            band = scene->nodes.size();
            std::wstring rasterId;
            animation::AppendMotionIdentity(rasterId, parent);
            animation::AppendMotionIdentity(rasterId, node.id);
            animation::AppendMotionIdentity(rasterId, std::to_wstring(phase));
            scene->nodes.push_back({L"raster/" + rasterId,
                                    parent,
                                    {},
                                    {},
                                    WidgetCompositionKind::Raster,
                                    0,
                                    bounds,
                                    viewport});
        } else
            scene->nodes[*band].bounds = UnionRect(scene->nodes[*band].bounds, bounds);
        compositionPhases[{node.id, phase}] = *band;
        paintOperations[*band].push_back({&node, phase});
        if (independentClip) captureBands[*band] = capture->second;
    };
    const auto visit = [&](const auto &self, const WidgetNode &node, std::wstring parent,
                           bool surfaceDone, std::wstring scope, std::wstring itemIdentity) -> void {
        if (!IsResponsiveVisible(node))
            return;
        const auto position = presentation.find(NarrowStableId(node.id));
        const auto styleNode = prepared.find(NarrowStableId(node.id));
        if (position == presentation.end() || styleNode == prepared.end())
            return;
        const auto& candidate = position->second;
        const auto& candidateStyle = styleNode->second.paintStyle;
        const auto visualBounds = Intersection(
            widgetrail::surface::PaintBounds(candidate.borderBox, candidateStyle, options.pixelScale), candidate.ancestorClip);
        if ((node.children.empty() || (node.kind != L"backgroundSurface" &&
                node.kind != L"focusPresentationSurface" && ClipsDescendants(node, styleNode->second.baseStyle))) &&
            (visualBounds.width <= .01F || visualBounds.height <= .01F)) {
            // Clipped descendants cannot contribute pixels. Their logical
            // navigation/scroll geometry is still published independently.
            return;
        }
        parents[node.id] = path.empty() ? L"" : path.back();
        paintOrder.emplace(&node, paintOrder.size());
        path.push_back(node.id);
        const auto &shown = position->second;
        const auto &style = styleNode->second.paintStyle;
        const auto &spec = node.transition;
        // Boundaries are semantic, not snapshot sequence numbers. Appended
        // cursor pages and routine value updates retain their focus context.
        if (!node.inputScopeId.empty()) animation::AppendMotionIdentity(scope, L"scope:" + node.inputScopeId);
        if (!node.initialChildFocusId.empty()) animation::AppendMotionIdentity(scope, L"group:" + node.id);
        if (node.kind == L"scroll") animation::AppendMotionIdentity(scope, L"scroll:" + node.id);
        if (node.collectionResetGeneration)
            animation::AppendMotionIdentity(scope, L"reset:" + std::to_wstring(*node.collectionResetGeneration));
        if (spec && !spec->layout) {
            animation::AppendMotionIdentity(scope, L"section:" + spec->groupId);
            animation::AppendMotionIdentity(scope, spec->key);
        }
        if (!node.collectionItemKey.empty()) animation::AppendMotionIdentity(itemIdentity, node.collectionItemKey);
        const auto targetKey = node.id + L"\x1f" + itemIdentity;
        const bool controlScale = UsesCompositorControlScale(node);
        const bool visibleControl = (node.kind == L"button" || node.kind == L"actionSurface") &&
            shown.visibleBox.width > .5F && shown.visibleBox.height > .5F;
        bool separateSurface{};
        if (visibleControl) {
            const auto &context = styleNode->second.context;
            const auto focusStyle = Adapt(node, true, false, context.parentWidthPx, context.parentHeightPx,
                context.parentFontSizePx, context.effectiveBackground).style;
            if (animation::HasFocusSurfaceChange(styleNode->second.baseStyle, focusStyle)) {
                separateSurface = !surfaceDone && !node.transition && node.id != pressedId &&
                    animation::CanSeparateFocusSurface(styleNode->second.baseStyle, focusStyle,
                        controlScale);
                if (separateSurface) compositionFocusStyles.emplace(node.id, focusStyle);
            }
        }
        if ((node.kind == L"button" || node.kind == L"slider" || node.kind == L"actionSurface") &&
            shown.visibleBox.width > .5F && shown.visibleBox.height > .5F) {
            if (scene->focusTargets.size() < WidgetCompositionScene::MaximumNodes)
                scene->focusTargets.push_back({targetKey, scope,
                                               shown.borderBox, shown.ancestorClip});
            else
                focusTargetsOverflow = true;
        }
        const auto beforeParent = parent;
        const auto supportsCapture = [&](const auto& self, const WidgetNode& child) -> bool {
            if (child.transition || child.kind == L"scroll" || child.kind == L"modalLayer" ||
                child.kind == L"backgroundSurface" || child.kind == L"focusPresentationSurface") return false;
            for (const auto& descendant : child.children) if (!self(self, descendant)) return false;
            return true;
        };
        const bool inScroll = std::any_of(path.begin(), path.end(), [&](const auto& id) {
            return prepared.at(NarrowStableId(id)).node->kind == L"scroll";
        });
        const bool roundedAncestor = std::any_of(path.begin(), path.end(), [&](const auto& id) {
            const auto& ancestor = prepared.at(NarrowStableId(id));
            return id != node.id && ancestor.node->kind == L"actionSurface" && ancestor.baseStyle.overflow() == NativeOverflow::Clip;
        });
        const bool translatedAncestor = std::any_of(path.begin(), path.end(), [&](const auto& id) {
            const auto& motion = presentation.at(NarrowStableId(id)).motion.value;
            return motion.translationX != 0 || motion.translationY != 0;
        });
        // Moving an antialiased fractional clip from the painter to a visual
        // can apply edge coverage differently. Preserve that painter path for
        // authored subpixel translations instead of changing their pixels.
        bool independentCapture = visibleControl && inScroll && !roundedAncestor &&
            !translatedAncestor &&
            (node.children.empty() || ClipsDescendants(node, styleNode->second.baseStyle)) && supportsCapture(supportsCapture, node);
#ifdef WRAIL_DECLARATIVE_RENDERER_TESTING
        independentCapture = independentCapture && !options.disableIndependentCapturesForTesting;
#endif
        if (spec) {
            if (!spec->layout)
                parent = addGroup(L"content/" + spec->groupId, parent, WidgetCompositionKind::Content,
                                  spec->groupId, spec->key, spec->order, shown.borderBox,
                                  Intersection(shown.borderBox, shown.ancestorClip));
            else
                parent = addGroup(L"layout/" + node.id, parent, WidgetCompositionKind::Layout, spec->groupId,
                                  spec->key, spec->order, shown.borderBox, shown.ancestorClip);
        }
        if ((controlScale || independentCapture) && visibleControl) {
            parent = addGroup(L"control/" + node.id, parent, WidgetCompositionKind::Control,
                scope, targetKey, 0, shown.borderBox, shown.ancestorClip);
            auto &control = scene->nodes.back();
            control.controlScale = controlScale ? style.scale() : 1.0F;
            control.controlDuration = controlScale ? static_cast<unsigned>(style.transitionDurationMilliseconds()) : 0;
            control.controlCurve = style.transitionEasing() == NativeTransitionEasing::Linear ? animation::Curve{1, 0, 0}
                : style.transitionEasing() == NativeTransitionEasing::EaseOut ? animation::EaseOut : animation::Smooth;
            if (independentCapture) captureRoots[parent] = &node;
        }
        const bool compositorBackground =
            node.kind == L"backgroundSurface" &&
            TrySelectCompositorBackground(node, style, shown.borderBox, shown.motion.value.opacity);
        const auto edges = style.borderEdges();
        const auto edgePaints = [](const auto &edge) {
            return edge.widthPx > 0 && edge.color && edge.color->alpha > 0;
        };
        const bool surfacePaints = (style.background() && style.background()->alpha > 0) ||
                                   edgePaints(edges.top) || edgePaints(edges.right) ||
                                   edgePaints(edges.bottom) || edgePaints(edges.left) || style.shadowColor();
        if (separateSurface) {
            const auto surface = addGroup(L"focus-surface/" + node.id, parent, WidgetCompositionKind::FocusSurface,
                scope, targetKey, 0, shown.borderBox, shown.ancestorClip);
            const auto surfaceBounds = Intersection(UnionRect(
                widgetrail::surface::PaintBounds(shown.borderBox, styleNode->second.baseStyle, options.pixelScale),
                widgetrail::surface::PaintBounds(shown.borderBox, compositionFocusStyles.at(node.id), options.pixelScale)), shown.ancestorClip);
            op(node, 5, surface, surfaceBounds);
            scene->nodes.back().order = 0;
            split();
            op(node, 0, surface, surfaceBounds);
            scene->nodes.back().order = 1;
            split();
        } else if (!surfaceDone && surfacePaints && !compositorBackground && node.kind != L"modalLayer" &&
            node.kind != L"slider")
            op(node, 0, parent, Intersection(widgetrail::surface::PaintBounds(shown.borderBox, style, options.pixelScale), shown.ancestorClip));
        const bool semantic = node.kind != L"stack" && node.kind != L"row" && node.kind != L"scroll" &&
                              node.kind != L"grid" && node.kind != L"spacer" &&
                              node.kind != L"focusPresentationSurface" && node.kind != L"modalLayer";
        const auto indicatorRect = Intersection(shown.contentBox, shown.visibleBox);
        const auto indicatorDiameter = std::min(indicatorRect.width, indicatorRect.height);
        const Rect indicatorBounds{indicatorRect.x + (indicatorRect.width - indicatorDiameter) * .5F,
            indicatorRect.y + (indicatorRect.height - indicatorDiameter) * .5F, indicatorDiameter, indicatorDiameter};
        const bool promoteIndicator = node.kind == L"loadingIndicator" && !roundedAncestor && !translatedAncestor && indicatorDiameter > .5F;
        if (promoteIndicator) {
            const auto rotation = addGroup(L"loading/" + node.id, parent, WidgetCompositionKind::IndeterminateRotation,
                scope, targetKey, 0, indicatorBounds, shown.visibleBox);
            captureRoots.erase(rotation);
            compositionLoadingIndicators.insert(node.id);
            op(node, 1, rotation, indicatorBounds);
            split();
        } else if ((semantic && !compositorBackground) ||
            (compositorBackground && (style.imageTint() || style.scrimColor())))
            op(node, 1, parent, shown.visibleBox);
        for (const auto &child : node.children) {
            if (!child.transition || !child.transition->selection || child.isSelected)
                continue;
            const auto childPos = presentation.find(NarrowStableId(child.id));
            if (childPos != presentation.end())
                op(child, 0, parent, Intersection(widgetrail::surface::PaintBounds(childPos->second.borderBox,
                    prepared.at(NarrowStableId(child.id)).paintStyle, options.pixelScale), childPos->second.ancestorClip));
        }
        for (const auto &child : node.children) {
            if (!child.transition || !child.transition->selection || !child.isSelected)
                continue;
            const auto childPos = presentation.find(NarrowStableId(child.id));
            if (childPos == presentation.end())
                continue;
            const auto &transition = *child.transition;
            const auto selection =
                addGroup(L"selection/" + transition.groupId, parent, WidgetCompositionKind::Selection,
                         transition.groupId, transition.key, transition.order, childPos->second.borderBox,
                         shown.visibleBox);
            transitionSelections[child.id] = childPos->second.borderBox;
            op(child, 0, selection, Intersection(widgetrail::surface::PaintBounds(childPos->second.borderBox,
                prepared.at(NarrowStableId(child.id)).paintStyle, options.pixelScale), childPos->second.ancestorClip));
            split();
        }
        if (node.kind == L"focusPresentationSurface") {
            if (const auto *fragment = PresentationFor(node))
                self(self, *fragment, parent, false, scope, itemIdentity);
        }
        for (std::size_t index = 0; index < node.children.size(); ++index) {
            const auto &child = node.children[index];
            if (node.kind == L"modalLayer" && index == 1) {
                const auto scrimGroup = addGroup(L"modal.scrim", parent, WidgetCompositionKind::Scrim,
                                                 L"$modal", child.id, 0, viewport, viewport);
                WidgetCompositionNode scrim{L"modal.scrim.pixels",         scrimGroup, {},       {},
                                            WidgetCompositionKind::Raster, 0,          viewport, viewport};
                scrim.solid = D2DColor(style.background().value_or(NativeColor{0, 0, 0, .6F}));
                scene->nodes.push_back(std::move(scrim));
                const auto box = presentation.find(NarrowStableId(child.id));
                const auto modal =
                    addGroup(L"modal.panel", parent, WidgetCompositionKind::Modal, L"$modal", child.id, 0,
                             box == presentation.end() ? viewport : box->second.borderBox, viewport);
                auto modalScope = scope;
                animation::AppendMotionIdentity(modalScope, L"modal:" + child.id);
                self(self, child, modal, false, modalScope, itemIdentity);
                split();
            } else
                self(self, child, parent, child.transition && child.transition->selection, scope, itemIdentity);
        }
        if (node.kind == L"scroll") {
            const auto indicator = PrepareScrollIndicator(node, style, shown);
            if (const auto loading = CollectionLoadingBounds(node, style, Intersection(shown.contentBox, shown.visibleBox))) {
                split();
                op(node, 7, parent, Intersection(Inset(loading->badge, -1), loading->clip));
                split();
                if (!roundedAncestor && !translatedAncestor) {
                    const auto rotation = addGroup(L"loading/" + node.id, parent, WidgetCompositionKind::IndeterminateRotation,
                        scope, targetKey, 0, loading->indicator, loading->clip);
                    captureRoots.erase(rotation);
                    compositionLoadingIndicators.insert(node.id);
                    op(node, 6, rotation, loading->indicator);
                    split();
                }
            }
            if (indicator) {
                // Preserve direct-paint order: loading badge, then scrollbar.
                // Both captures stay bounded to their stationary chrome.
                split();
                op(node, 2, parent, Intersection(indicator->track, indicator->clip));
                split();
            }
        } else if (node.kind == L"actionSurface" && (node.isSelected || node.isDisabled || node.isBusy))
            op(node, 2, parent, shown.visibleBox);
        if (node.id == focusedId) {
            focusParents[node.id] = parent;
            sceneFocus = &node;
            focusScope = scope;
            focusKey = targetKey;
        }
        if (parent != beforeParent)
            split();
        path.pop_back();
    };
    auto initialScope = snapshot->activeInputScopeId;
    animation::AppendMotionIdentity(initialScope, snapshot->root.id);
    visit(visit, snapshot->root, {}, false, initialScope, {});
    if (focusTargetsOverflow) scene->focusTargets.clear();
    if (sceneFocus && focusParents.contains(focusedId)) {
        split();
        const auto found = presentation.find(NarrowStableId(focusedId));
        if (found != presentation.end()) {
            const auto &shown = found->second;
            const auto focusGroup = addGroup(L"$focus/" + focusKey, focusParents.at(focusedId), WidgetCompositionKind::Focus,
                focusScope, focusKey, 0,
                shown.borderBox, shown.ancestorClip);
            const auto &style = prepared.at(NarrowStableId(focusedId)).paintStyle;
            const auto paintBox = ScaleRect(shown.borderBox, shown.motion.value.scale);
            const float outset = std::max(12.0F, style.outlineOffsetPx() +
                std::max(options.accessibility.minimumFocusRingPx, (style.outlineWidthPx() > 0 ? style.outlineWidthPx() : 2.0F)));
            op(*sceneFocus, 3, focusGroup, Intersection(Inset(paintBox, -outset), shown.ancestorClip));
            split();
            // In-place focus animates only the highlight. The controller hint is immediate,
            // while still inheriting its control's scale.
            if (sceneFocus->kind == L"actionSurface" && input::HasAvailableContextMenuActions(sceneFocus->contextActions)) {
                const auto badge = ContextMenuIndicatorBounds(paintBox);
                op(*sceneFocus, 4, focusParents.at(focusedId), Intersection(badge, shown.visibleBox));
            }
        }
    }
    const auto scale = std::isfinite(scene->scale) && scene->scale > 0 ? scene->scale : 1;
    for (auto &node : scene->nodes) {
        if (node.kind != WidgetCompositionKind::Raster || node.solid)
            continue;
        node.bounds = CompositionRasterBounds(node.bounds, scale);
        const auto width = std::round(node.bounds.width * scale),
                   height = std::round(node.bounds.height * scale);
        if (width > 8192 || height > 8192) {
            scene->rasterBytes = WidgetCompositionScene::MaximumBytes + 1;
            break;
        }
        scene->rasterBytes += static_cast<std::size_t>(width * height * 4);
    }
    if (scene->nodes.size() > WidgetCompositionScene::MaximumNodes ||
        scene->rasterBytes > WidgetCompositionScene::MaximumBytes) {
        scene->nodes.clear();
        scene->focusTargets.clear();
        scene->rasterBytes = 0;
        scene->directContent = true;
        scene->reducedMotion = true;
        compositionPhases.clear();
        transitionSelections.clear();
        RestoreControlScaleFallback();
        scene->ProtectResources();
    result.widgetComposition = std::move(scene);
        return false;
    }
    for (const auto &[phase, index] : compositionPhases) {
        auto id = phase.first;
        while (!id.empty()) {
            compositionBandNodes[index].insert(id);
            const auto parent = parents.find(id);
            if (parent == parents.end())
                break;
            id = parent->second;
        }
    }
    // Each capture follows only its contribution paths. In particular, painting
    // one cell must not scan every sibling in a large retained cursor window.
    // Retain authored order, including selection surfaces and modal children.
    for (const auto& [index, members] : compositionBandNodes) {
        auto& children = compositionBandChildren[index];
        for (const auto& id : members) {
            const auto parent = parents.find(id);
            if (parent == parents.end() || parent->second.empty()) continue;
            const auto* node = prepared.at(NarrowStableId(id)).node;
            const auto* ancestor = prepared.at(NarrowStableId(parent->second)).node;
            if (ancestor->kind == L"focusPresentationSurface") continue; // Its fragment has an explicit traversal path.
            children[ancestor].push_back(node);
        }
        for (auto& [_, siblings] : children)
            std::sort(siblings.begin(), siblings.end(), [&](auto* a, auto* b) { return paintOrder.at(a) < paintOrder.at(b); });
    }
    auto *mainTarget = target;
    bool animationActive{};
    composingScene = true;
    compositionBand = -1;
    target = nullptr;
    DrawNode(snapshot->root);
    target = mainTarget;
    std::map<std::wstring, CompositionPaintEntry> nextPaintCache;
    std::size_t identityBytes{};
    ++owner->compatiblePaintDepth_;
    for (std::size_t index = 0; index < scene->nodes.size(); ++index) {
        auto &node = scene->nodes[index];
        if (node.kind != WidgetCompositionKind::Raster || node.solid)
            continue;
        const auto size = D2D1::SizeF(node.bounds.width, node.bounds.height);
        const auto pixels = D2D1::SizeU(static_cast<UINT32>(std::round(size.width * scale)),
                                        static_cast<UINT32>(std::round(size.height * scale)));
        struct RestoreProjection final {
            std::vector<std::pair<PresentationNode*, PresentationNode>> saved;
            ~RestoreProjection() { for (auto& [location, value] : saved) *location = value; }
        } projection;
        std::set<std::wstring> captureMembers;
        const auto captureBand = captureBands.find(index);
        const WidgetNode* captureRoot = captureBand == captureBands.end() ? nullptr : captureBand->second;
        if (captureRoot) {
            const auto project = [&](const auto& self, const WidgetNode& part, Rect clip) -> void {
                if (!compositionBandNodes[index].contains(part.id)) return;
                auto& shown = presentation.at(NarrowStableId(part.id));
                projection.saved.emplace_back(&shown, shown);
                captureMembers.insert(part.id);
                shown.ancestorClip = clip;
                shown.visibleBox = Intersection(shown.borderBox, clip);
                const auto& style = prepared.at(NarrowStableId(part.id)).baseStyle;
                const auto childClip = ClipsDescendants(part, style) ? Intersection(clip, shown.contentBox) : clip;
                for (const auto& child : part.children) self(self, child, childClip);
            };
            // The raster itself bounds the capture. Do not introduce an extra
            // fractional clip at the shadow's unsnapped paint envelope: adding
            // that envelope to a translated float coordinate changes its phase.
            project(project, *captureRoot, CompositionRasterBounds(captureBounds(*captureRoot), scale));
        }
        auto identity = CompositionIdentity(node, paintOperations[index], compositionBandNodes[index],
            captureRoot ? &captureMembers : nullptr);
        const auto prior = owner->compositionPaintCache_.find(node.id);
        if (identity.cacheable && prior != owner->compositionPaintCache_.end() && identity == prior->second.identity) {
            node.bitmap = prior->second.bitmap;
            node.rasterLease = prior->second.lease;
            if (identityBytes + identity.Bytes() <= CompositionPaintIdentity::MaximumRetainedBytes) {
                identityBytes += identity.Bytes();
                nextPaintCache.emplace(node.id, CompositionPaintEntry{std::move(identity), node.bitmap, node.rasterLease});
            }
            ++scene->paintCacheHits;
            continue;
        }
        ++scene->paintCacheMisses;
        scene->paintedBytes += static_cast<std::size_t>(pixels.width) * pixels.height * 4;
        // Keep the old raster lease alive until the new frame is submitted.
        // A failed draw must not recycle a bitmap still used by committed pixels.
        resources::UiResource<ID2D1BitmapRenderTarget> surface;
        CompositionCapture* leasedCapture{};
        node.rasterLease = std::make_shared<char>();
        auto &captures = owner->compositionCaptures_;
        for (auto &capture : captures) {
            const auto actual = capture.pixels;
            const auto logical = capture.logical;
            if (capture.lease.expired() && actual.width == pixels.width && actual.height == pixels.height &&
                std::abs(logical.width - size.width) < .01F &&
                std::abs(logical.height - size.height) < .01F) {
                surface = capture.target;
                capture.lease = node.rasterLease;
                leasedCapture = &capture;
                break;
            }
        }
        auto status = S_OK;
        if (!surface) {
            const auto bytes = static_cast<std::size_t>(pixels.width) * pixels.height * 4;
            status = resources::UiResource<ID2D1BitmapRenderTarget>::Create(owner->resourceBudget_, resources::Kind::RetainedRaster,
                bytes, [&](ID2D1BitmapRenderTarget** output) { return mainTarget->CreateCompatibleRenderTarget(
                    &size, &pixels, nullptr, D2D1_COMPATIBLE_RENDER_TARGET_OPTIONS_NONE, output); }, surface);
            std::size_t retained{};
            for (const auto &capture : captures)
                retained += capture.bytes;
            while (retained + bytes > WidgetCompositionScene::MaximumBytes) {
                const auto unused = std::find_if(captures.begin(), captures.end(),
                                                 [](const auto &capture) { return capture.lease.expired(); });
                if (unused == captures.end())
                    break;
                retained -= unused->bytes;
                captures.erase(unused);
            }
            if (SUCCEEDED(status) && retained + bytes <= WidgetCompositionScene::MaximumBytes) {
                // Avoid querying every pooled COM target on each miss.
                surface->SetDpi(96 * scale, 96 * scale);
                captures.push_back({surface, node.rasterLease, bytes, surface->GetPixelSize(), surface->GetSize()});
                leasedCapture = &captures.back();
            }
        }
        if (FAILED(status)) {
            Add(node.id, L"composition_capture_failed", L"Could not prepare widget composition layer.",
                RenderDiagnosticSeverity::Error);
            break;
        }
        compositionBand = static_cast<int>(index);
        transitionSurfacesPainted.clear();
        target = surface.Get();
        target->SetDpi(96 * scale, 96 * scale);
        if (leasedCapture) leasedCapture->logical = target->GetSize();
        target->BeginDraw();
        target->Clear(D2D1::ColorF(0, 0));
        target->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
        target->SetTransform(D2D1::Matrix3x2F::Translation(-node.bounds.x, -node.bounds.y));
        DrawNode(captureRoot ? *captureRoot : snapshot->root,
            captureRoot ? prepared.at(NarrowStableId(captureRoot->id)).inputScope : std::wstring_view{});
        DrawDeferredFocus();
        status = target->EndDraw();
        if (SUCCEEDED(status))
            status = resources::UiResource<ID2D1Bitmap>::Alias(surface,
                [&](ID2D1Bitmap** output) { return surface->GetBitmap(output); }, node.bitmap);
        if (FAILED(status)) {
            Add(node.id, L"composition_capture_failed", L"Widget composition layer paint failed.",
                RenderDiagnosticSeverity::Error);
            break;
        }
        if (identity.cacheable && identityBytes + identity.Bytes() <= CompositionPaintIdentity::MaximumRetainedBytes) {
            identityBytes += identity.Bytes();
            nextPaintCache.emplace(node.id, CompositionPaintEntry{std::move(identity), node.bitmap, node.rasterLease});
        }
        animationActive = animationActive || result.animationActive;
    }
    nextCompositionPaintCache = std::move(nextPaintCache);
    --owner->compatiblePaintDepth_;
    target = mainTarget;
    composingScene = false;
    compositionBand = -1;
    deferredFocusNode = nullptr;
    deferredFocusStyle = nullptr;
    result.animationActive = result.animationActive || animationActive;
    scene->ProtectResources();
    result.widgetComposition = std::move(scene);
    return true;
}
