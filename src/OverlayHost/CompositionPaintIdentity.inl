// Included inside RenderPass. Keep paint dependencies alongside native paint;
// action routing/accessibility continue through the uncached geometry pass.
CompositionPaintIdentity CompositionIdentity(const WidgetCompositionNode& raster,
    const std::vector<std::pair<const WidgetNode*, int>>& operations,
    const std::set<std::wstring>& ancestors) {
    CompositionPaintIdentity key;
    key.Text(snapshot->instanceId); key.Text(snapshot->activeInputScopeId);
    key.Text(options.artworkAuthorityId); key.Text(options.artworkWidgetId);
    key.Text(options.artworkRuntimeGeneration); key.Text(options.artworkPresentationGeneration);
    key.Text(options.packageContentDigest);
    key.Scalar(options.pixelScale); key.Scalar(options.playStationControls);
    key.Scalar(options.sizeArtworkToDisplay); key.Scalar(options.artworkDecodeSize.width); key.Scalar(options.artworkDecodeSize.height);
    key.Scalar(options.accessibility.reducedMotion); key.Scalar(options.accessibility.reducedTransparency);
    key.Scalar(static_cast<bool>(options.accessibility.contrastHook));
    // Placement belongs to the compositor. A capture's identity describes only
    // pixels in its own coordinate system, including the effective clips baked
    // into those pixels. Preserve authored subpixel phases; remove only float
    // cancellation noise around the layout's physical-pixel grid.
    const auto localBox = [&](Rect box) {
        key.Box(CompositionLocalRect(box, raster.bounds.x, raster.bounds.y, options.pixelScale));
    };
    const auto localClip = [&](Rect box) {
        localBox(Intersection(box, raster.bounds));
    };
    localClip(viewport);
    key.Scalar(raster.bounds.width); key.Scalar(raster.bounds.height);
    // Only primitives rasterized in capture-local space may ignore placement.
    // Keep less common painter paths conservative until their local pixel
    // invariance has the same coverage (glyphs, state cues, sliders, etc.).
    const bool translationSafe = std::all_of(operations.begin(), operations.end(), [](const auto& operation) {
        const auto& [node, phase] = operation;
        if (phase == 0 || phase == 5) return true;
        if (phase != 1) return false;
        if (node->kind == L"text") return true;
        if (node->kind == L"actionSurface")
            return node->actionSurfacePresentation != L"poster" || node->children.size() == 2;
        return node->kind == L"button" && node->imageSource.empty() && node->artworkHandle.empty() &&
            node->glyph.empty() && !node->packageIcon && !node->isSelected && !node->isDisabled && !node->isBusy;
    });
    key.Scalar(translationSafe);
    if (!translationSafe) key.Box(raster.bounds);
    const auto geometry = [&](const WidgetNode& node) {
        const auto& shown = presentation.at(NarrowStableId(node.id));
        key.Text(node.id); key.Text(node.kind); key.Text(node.inputScopeId); key.Text(node.collectionItemKey);
        key.Scalar(node.collectionResetGeneration.value_or(0));
        localBox(shown.borderBox); localBox(shown.contentBox);
        localClip(shown.visibleBox); localClip(shown.ancestorClip);
        key.Scalar(shown.motion.value.opacity); key.Scalar(shown.motion.value.scale);
    };
    // Ancestors can change descendant clipping/opacity without changing any
    // property of the child. Do not include their unrelated content or actions.
    key.Scalar(ancestors.size());
    for (const auto& id : ancestors) {
        const auto& item = prepared.at(NarrowStableId(id));
        const auto& shown = presentation.at(NarrowStableId(id));
        key.Text(item.node->id); key.Text(item.node->kind); key.Text(item.node->inputScopeId);
        key.Text(item.node->collectionItemKey);
        key.Scalar(item.node->collectionResetGeneration.value_or(0));
        // Traversed ancestors contribute clips, not their screen positions.
        // Rounded tile masks additionally depend on the complete tile shape.
        localClip(shown.visibleBox); localClip(shown.ancestorClip);
        if (item.node->kind == L"actionSurface" && item.baseStyle.overflow() == NativeOverflow::Clip)
            localBox(ScaleRect(shown.borderBox, shown.motion.value.scale));
        key.Scalar(shown.motion.value.opacity); key.Scalar(shown.motion.value.scale);
        key.Style(item.baseStyle);
        if (item.node->kind == L"actionSurface") key.Style(item.paintStyle);
    }
    const auto image = [&](const WidgetNode& node) {
        if (node.imageSource.empty() && node.artworkHandle.empty()) return;
        const auto source = ImageKey(node, CachedImageSize(node));
        key.Text(source);
        auto ready = owner->imageCache_ ? owner->imageCache_->GetReadyImage(source) : nullptr;
        // Pending/failed/previous-size fallbacks run the original demand path.
        // Only an immutable ready image may authorize retaining its pixels.
        if (!ready) key.cacheable = false;
        else key.images.emplace_back(ready);
    };
    key.Scalar(operations.size());
    for (const auto& [nodePointer, phase] : operations) {
        const auto& node = *nodePointer;
        const auto& item = prepared.at(NarrowStableId(node.id));
        key.Scalar(phase); geometry(node);
        const auto focus = compositionFocusStyles.find(node.id);
        key.Style(phase == 5 ? item.baseStyle : phase == 0 && focus != compositionFocusStyles.end() ? focus->second : item.paintStyle);
        // These surfaces have independent time/selection/resource owners.
        // Repaint them until those owners expose immutable capture identities.
        if (node.kind == L"backgroundSurface" || node.kind == L"loadingIndicator") key.cacheable = false;
        if (phase == 0 || phase == 5) continue;
        key.Text(node.text); key.Text(node.textEntryValue); key.Text(node.textEntryPlaceholder); key.Text(node.textEntryInputKind);
        key.Text(node.glyph); key.Text(node.controllerPrompt); key.Text(node.indicatorSize);
        key.Text(node.imageSource); key.Text(node.artworkHandle); key.Text(node.imageFit);
        key.Text(node.actionSurfacePresentation); key.Text(node.actionSurfaceOrientation);
        key.Text(node.contextMenuButton); key.Text(node.collectionLoading); key.Text(node.scrollAxis);
        key.Scalar(node.showScrollbar.value_or(true));
        key.Scalar(node.value); key.Scalar(node.minimum); key.Scalar(node.maximum); key.Scalar(node.step);
        key.Scalar(node.hasProgress); key.Scalar(node.hasSliderRange); key.Scalar(node.isTextEntry); key.Scalar(node.isSelect);
        key.Scalar(node.isDisabled); key.Scalar(node.isBusy); key.Scalar(node.isSelected);
        key.Scalar(node.id == focusedId); key.Scalar(node.id == pressedId); key.Scalar(node.id == options.activeSliderElementId);
        key.Scalar(node.baseStyle.contains(L"text-align")); key.Scalar(node.focusedStyle.contains(L"text-align")); key.Scalar(node.pressedStyle.contains(L"text-align"));
        key.Scalar(node.styleClasses.size()); for (const auto& name : node.styleClasses) key.Text(name);
        key.Scalar(node.contextActions.size());
        for (const auto& action : node.contextActions) { key.Scalar(action.isDisabled); key.Scalar(action.isBusy); }
        key.Scalar(node.selectOptions.size());
        for (const auto& option : node.selectOptions) {
            key.Text(option.label); key.Text(option.glyph); key.Scalar(option.isSelected); key.Scalar(option.isDisabled); key.Scalar(option.isBusy);
            if (option.packageIcon) key.cacheable = false;
        }
        if (node.packageIcon) key.cacheable = false; // Keep icon demand/admission active.
        const auto optimistic = options.sliderValueOverrides.find(node.id);
        key.Scalar(optimistic != options.sliderValueOverrides.end());
        if (optimistic != options.sliderValueOverrides.end()) key.Scalar(optimistic->second);
        if (const auto* box = layout.Find(NarrowStableId(node.id))) {
            key.Scalar(box->scrollOffset); key.Scalar(box->maximumScrollOffset);
        }
        if (node.kind == L"actionSurface" && node.actionSurfacePresentation == L"poster" && node.children.size() == 2) {
            const auto& art = node.children.front();
            key.Text(art.imageSource); key.Text(art.artworkHandle); key.Text(art.imageFit);
            const auto artStyle = prepared.find(NarrowStableId(art.id));
            if (artStyle != prepared.end()) key.Style(artStyle->second.paintStyle);
            image(art);
        }
        image(node);
        if (!node.collectionLoading.empty()) key.cacheable = false;
    }
    return key;
}
