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
        result.widgetComposition = std::move(scene);
        return false;
    }
    const auto needsLayers = [&](const auto &self, const WidgetNode &node) -> bool {
        if (node.transition || node.kind == L"modalLayer" || UsesCompositorControlScale(node))
            return true;
        return std::any_of(node.children.begin(), node.children.end(),
                           [&](const auto &child) { return self(self, child); });
    };
    if (focusedId.empty() && !needsLayers(needsLayers, snapshot->root) &&
        owner->compositionInstance_ != snapshot->instanceId) {
        scene->directContent = true;
        result.widgetComposition = std::move(scene);
        return false;
    }
    owner->compositionInstance_ = snapshot->instanceId;
    std::optional<std::size_t> band;
    std::map<std::wstring, std::wstring> focusParents;
    const WidgetNode *sceneFocus{};
    std::wstring focusScope;
    std::wstring focusKey;
    bool focusTargetsOverflow{};
    std::map<std::wstring, std::wstring> parents;
    std::vector<std::wstring> path;
    const auto split = [&] { band.reset(); };
    const auto addGroup = [&](std::wstring id, std::wstring parent, WidgetCompositionKind kind,
                              std::wstring clock, std::wstring key, int order, Rect bounds, Rect clip) {
        split();
        scene->nodes.push_back(
            {id, std::move(parent), std::move(clock), std::move(key), kind, order, bounds, clip});
        return id;
    };
    const auto op = [&](const WidgetNode &node, int phase, const std::wstring &parent, Rect bounds) {
        bounds = Intersection(bounds, viewport);
        if (bounds.width <= .01F || bounds.height <= .01F)
            return;
        if (!band || scene->nodes[*band].parent != parent) {
            band = scene->nodes.size();
            scene->nodes.push_back({L"raster/" + std::to_wstring(*band),
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
    };
    const auto visit = [&](const auto &self, const WidgetNode &node, std::wstring parent,
                           bool surfaceDone, std::wstring scope, std::wstring itemIdentity) -> void {
        if (!IsResponsiveVisible(node))
            return;
        const auto position = presentation.find(NarrowStableId(node.id));
        const auto styleNode = prepared.find(NarrowStableId(node.id));
        if (position == presentation.end() || styleNode == prepared.end())
            return;
        parents[node.id] = path.empty() ? L"" : path.back();
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
        if (spec) {
            if (!spec->layout)
                parent = addGroup(L"content/" + spec->groupId, parent, WidgetCompositionKind::Content,
                                  spec->groupId, spec->key, spec->order, shown.borderBox,
                                  Intersection(shown.borderBox, shown.ancestorClip));
            else
                parent = addGroup(L"layout/" + node.id, parent, WidgetCompositionKind::Layout, spec->groupId,
                                  spec->key, spec->order, shown.borderBox, shown.ancestorClip);
        }
        if (controlScale && visibleControl) {
            parent = addGroup(L"control/" + node.id, parent, WidgetCompositionKind::Control,
                scope, targetKey, 0, shown.borderBox, shown.ancestorClip);
            auto &control = scene->nodes.back();
            control.controlScale = style.scale();
            control.controlDuration = static_cast<unsigned>(style.transitionDurationMilliseconds());
            control.controlCurve = style.transitionEasing() == NativeTransitionEasing::Linear ? animation::Curve{1, 0, 0}
                : style.transitionEasing() == NativeTransitionEasing::EaseOut ? animation::EaseOut : animation::Smooth;
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
        if ((semantic && !compositorBackground) ||
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
        if (node.kind == L"scroll" || node.kind == L"actionSurface")
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
    auto *mainTarget = target;
    bool animationActive{};
    composingScene = true;
    compositionBand = -1;
    target = nullptr;
    DrawNode(snapshot->root);
    target = mainTarget;
    ++owner->compatiblePaintDepth_;
    for (std::size_t index = 0; index < scene->nodes.size(); ++index) {
        auto &node = scene->nodes[index];
        if (node.kind != WidgetCompositionKind::Raster || node.solid)
            continue;
        const auto size = D2D1::SizeF(node.bounds.width, node.bounds.height);
        const auto pixels = D2D1::SizeU(static_cast<UINT32>(std::round(size.width * scale)),
                                        static_cast<UINT32>(std::round(size.height * scale)));
        ComPtr<ID2D1BitmapRenderTarget> surface;
        node.rasterLease = std::make_shared<char>();
        auto &captures = owner->compositionCaptures_;
        for (auto &capture : captures) {
            const auto actual = capture.target->GetPixelSize();
            const auto logical = capture.target->GetSize();
            if (capture.lease.expired() && actual.width == pixels.width && actual.height == pixels.height &&
                std::abs(logical.width - size.width) < .01F &&
                std::abs(logical.height - size.height) < .01F) {
                surface = capture.target;
                capture.lease = node.rasterLease;
                break;
            }
        }
        auto status = S_OK;
        if (!surface) {
            status = mainTarget->CreateCompatibleRenderTarget(
                &size, &pixels, nullptr, D2D1_COMPATIBLE_RENDER_TARGET_OPTIONS_NONE, surface.GetAddressOf());
            const auto bytes = static_cast<std::size_t>(pixels.width) * pixels.height * 4;
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
            if (SUCCEEDED(status) && retained + bytes <= WidgetCompositionScene::MaximumBytes)
                captures.push_back({surface, node.rasterLease, bytes});
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
        target->BeginDraw();
        target->Clear(D2D1::ColorF(0, 0));
        target->SetTextAntialiasMode(D2D1_TEXT_ANTIALIAS_MODE_GRAYSCALE);
        target->SetTransform(D2D1::Matrix3x2F::Translation(-node.bounds.x, -node.bounds.y));
        DrawNode(snapshot->root);
        DrawDeferredFocus();
        status = target->EndDraw();
        if (SUCCEEDED(status))
            status = surface->GetBitmap(node.bitmap.GetAddressOf());
        if (FAILED(status)) {
            Add(node.id, L"composition_capture_failed", L"Widget composition layer paint failed.",
                RenderDiagnosticSeverity::Error);
            break;
        }
        animationActive = animationActive || result.animationActive;
    }
    --owner->compatiblePaintDepth_;
    target = mainTarget;
    composingScene = false;
    compositionBand = -1;
    deferredFocusNode = nullptr;
    deferredFocusStyle = nullptr;
    result.animationActive = result.animationActive || animationActive;
    result.widgetComposition = std::move(scene);
    return true;
}
