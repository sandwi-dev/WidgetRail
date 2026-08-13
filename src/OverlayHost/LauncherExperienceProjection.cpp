#include "LauncherExperienceProjection.h"
#include <wincodec.h>
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <map>
#include <utility>

namespace gba::launcher {
namespace {

constexpr int kDeclarationSchemaVersion = 1;
constexpr std::wstring_view kPresentationKind = L"launcherExperience";

constexpr std::array<std::pair<std::wstring_view, Preset>, 4> kProfiles{{
    {L"heroRail", Preset::HeroRail},
    {L"coverWall", Preset::CoverWall},
    {L"carousel", Preset::Carousel},
    {L"compactGrid", Preset::CompactGrid},
}};

constexpr std::array<std::pair<std::wstring_view, Slot>, 6> kSlots{{
    {L"detailsPanel", Slot::DetailsPanel},
    {L"primaryCollection", Slot::GameRail},
    {L"collectionNavigation", Slot::CollectionTabs},
    {L"sourceStatus", Slot::SourceStatus},
    {L"operationStatus", Slot::OperationStatus},
    {L"controllerHints", Slot::ControllerHints},
}};

bool ContainsAdvancedSlot(const WidgetNode& node) noexcept {
    if (!node.advancedPresentationSlot.empty()) return true;
    return std::any_of(
        node.children.begin(), node.children.end(),
        [](const WidgetNode& child) { return ContainsAdvancedSlot(child); });
}

bool CollectAdvancedSlots(
    const WidgetNode& node,
    std::map<Slot, const WidgetNode*>& extracted) {
    if (!node.advancedPresentationSlot.empty()) {
        const auto found = std::find_if(
            kSlots.begin(), kSlots.end(), [&](const auto& candidate) {
                return candidate.first == node.advancedPresentationSlot;
            });
        if (found == kSlots.end() || !extracted.emplace(found->second, &node).second)
            return false;
        if (std::any_of(
                node.children.begin(), node.children.end(),
                [](const WidgetNode& child) { return ContainsAdvancedSlot(child); }))
            return false;
        return true;
    }
    for (const auto& child : node.children)
        if (!CollectAdvancedSlots(child, extracted)) return false;
    return true;
}

const WidgetNode* FindProjectedNode(
    const WidgetNode& node,
    const std::wstring_view id) {
    if (node.id == id) return &node;
    for (const auto& child : node.children) {
        if (const auto* found = FindProjectedNode(child, id))
            return found;
    }
    return nullptr;
}

void OffsetRect(declarative::Rect& rect, const float x, const float y) noexcept {
    rect.x += x;
    rect.y += y;
}

void OffsetRenderResult(RenderResult& result, const float x, const float y) {
    for (auto& hit : result.hitRegions) OffsetRect(hit.rect, x, y);
    for (auto& region : result.accessibilityRegions) OffsetRect(region.rect, x, y);
    for (auto& [_, rect] : result.focusRects) OffsetRect(rect, x, y);
    for (auto& [_, rect] : result.navigationRects) OffsetRect(rect, x, y);
    if (result.currentFocusRect) OffsetRect(*result.currentFocusRect, x, y);
    if (result.currentFocusOutlineClip)
        OffsetRect(*result.currentFocusOutlineClip, x, y);
#ifdef GBA_DECLARATIVE_RENDERER_TESTING
    for (auto& [_, rect] : result.elementRects) OffsetRect(rect, x, y);
    for (auto& [_, rect] : result.elementVisibleRects) OffsetRect(rect, x, y);
    for (auto& [_, placement] : result.buttonContentPlacements) {
        OffsetRect(placement.leading, x, y);
        OffsetRect(placement.text, x, y);
        OffsetRect(placement.trailingStateCue, x, y);
    }
#endif
}

void AddFallbackDiagnostic(
    RenderResult& render,
    const wchar_t* code,
    const wchar_t* message) {
    render.diagnostics.push_back({
        RenderDiagnosticSeverity::Warning, {}, code, message});
}

const WidgetNode* FindNode(
    const WidgetNode& root,
    const std::wstring_view id) noexcept {
    if (root.id == id) return &root;
    for (const auto& child : root.children) {
        if (const auto* result = FindNode(child, id)) return result;
    }
    return nullptr;
}

template <typename ProjectionType>
const WidgetNode* FocusedGameNode(
    const ProjectionType& projection,
    const std::wstring_view focusedElementId) noexcept {
    const auto rail = std::find_if(
        projection.contents.begin(), projection.contents.end(),
        [](const auto& content) { return content.slot == Slot::GameRail; });
    return rail == projection.contents.end()
        ? nullptr
        : FindNode(rail->snapshot.root, focusedElementId);
}

const WidgetNode* ArtworkNode(const WidgetNode* root) noexcept {
    if (!root) return nullptr;
    if (!root->artworkHandle.empty()) return root;
    for (const auto& child : root->children) {
        if (const auto* result = ArtworkNode(&child)) return result;
    }
    return nullptr;
}

void CollectArtworkNodes(
    const WidgetNode& root,
    std::vector<const WidgetNode*>& result) {
    if (!root.artworkHandle.empty()) result.push_back(&root);
    for (const auto& child : root.children) CollectArtworkNodes(child, result);
}

std::wstring_view BranchName(const Branch branch) noexcept {
    switch (branch) {
    case Branch::Compact: return L"compact";
    case Branch::Standard: return L"standard";
    case Branch::Wide: return L"wide";
    }
    return L"unknown";
}

std::wstring_view OrientationName(
    const std::optional<Orientation> orientation) noexcept {
    if (!orientation) return L"unspecified";
    return *orientation == Orientation::Vertical ? L"vertical" : L"horizontal";
}

std::wstring_view SurfaceName(const std::optional<Surface> surface) noexcept {
    if (!surface) return L"default";
    return *surface == Surface::Glass ? L"glass" : L"solid";
}

} // namespace

std::wstring_view PresetName(const Preset preset) noexcept {
    switch (preset) {
    case Preset::HeroRail: return L"hero-rail";
    case Preset::CoverWall: return L"cover-wall";
    case Preset::Carousel: return L"carousel";
    case Preset::CompactGrid: return L"compact-grid";
    }
    return L"unknown";
}

std::wstring_view EffectQualityName(const EffectQuality quality) noexcept {
    switch (quality) {
    case EffectQuality::Full: return L"full";
    case EffectQuality::OpacityOnly: return L"opacity-only";
    case EffectQuality::Immediate: return L"immediate";
    }
    return L"unknown";
}

bool LauncherExperienceProjection::PublishSelection(
    LauncherExperienceSelection selection,
    std::wstring& diagnostic) {
    diagnostic.clear();
    if (selection.revision <= 0 || selection.presentationRevision.empty()) {
        diagnostic = L"Launcher Experience selection identity is invalid.";
        return false;
    }
    if (selection_ && selection.revision <= selection_->revision) {
        diagnostic = L"Launcher Experience selection revision is stale.";
        return false;
    }
    constexpr std::array<std::pair<declarative::Rect, float>, 6> profiles{{
        {{0, 0, 800, 450}, 1.0F}, {{0, 0, 800, 450}, 1.5F},
        {{0, 0, 1100, 650}, 1.0F}, {{0, 0, 1100, 650}, 1.5F},
        {{0, 0, 1400, 900}, 1.0F}, {{0, 0, 1400, 900}, 1.5F},
    }};
    for (const auto& [viewport, textScale] : profiles) {
        const auto layout = ResolveLayout(
            &selection.recipe, selection.preset, viewport, textScale);
        if (!layout.valid() || layout.usedFallback) {
            diagnostic = L"Launcher Experience recipe failed native compatibility validation.";
            return false;
        }
    }

    std::shared_ptr<const DecodedLauncherAsset> decoded;
    if (selection.packBackground) {
        SealedAssetBytes source;
        source.opaqueAssetId = selection.packBackground->opaqueAssetId;
        source.revision = selection.packBackground->revision;
        source.bytes = selection.packBackground->bytes;
        if (selection.packBackground->format == L"png")
            source.format = StaticImageFormat::Png;
        else if (selection.packBackground->format == L"jpeg")
            source.format = StaticImageFormat::Jpeg;
        else if (selection.packBackground->format == L"webp")
            source.format = StaticImageFormat::WebP;
        else {
            diagnostic = L"Launcher Experience background format is invalid.";
            return false;
        }
        Microsoft::WRL::ComPtr<IWICImagingFactory> imaging;
        if (FAILED(CoCreateInstance(
                CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
                IID_PPV_ARGS(imaging.ReleaseAndGetAddressOf()))) || !imaging) {
            diagnostic = L"Launcher Experience image decoder is unavailable.";
            return false;
        }
        auto decodedResult = DecodeSealedLauncherAsset(imaging.Get(), source);
        if (!decodedResult.succeeded()) {
            diagnostic = decodedResult.diagnostic;
            return false;
        }
        decoded = std::move(decodedResult.asset);
    }
    diagnostic = selection.diagnostic;
    selection_ = std::move(selection);
    packBackground_ = std::move(decoded);
    activePresentationKey_.clear();
    return true;
}

void LauncherExperienceProjection::BeginActivation(const bool safeStart) noexcept {
    safeStartActivation_ = safeStart;
    activePresentationKey_.clear();
}

std::optional<LauncherExperienceProjection::Projection>
LauncherExperienceProjection::Recognize(
    const std::optional<WidgetAdvancedPresentationDeclaration>& declaration,
    const WidgetSnapshot& snapshot,
    bool& presentationRequested) {
    presentationRequested = !snapshot.advancedPresentationKind.empty() ||
        ContainsAdvancedSlot(snapshot.root);
    if (!declaration ||
        declaration->schemaVersion != kDeclarationSchemaVersion ||
        declaration->kind != kPresentationKind ||
        snapshot.advancedPresentationKind != declaration->kind)
        return std::nullopt;

    std::optional<Preset> preset;
    const auto profile = std::find_if(
        kProfiles.begin(), kProfiles.end(), [&](const auto& candidate) {
            return candidate.first == snapshot.advancedPresentationPreset;
        });
    if (profile != kProfiles.end()) preset = profile->second;
    if (!preset) return std::nullopt;

    std::map<Slot, const WidgetNode*> extracted;
    if (!CollectAdvancedSlots(snapshot.root, extracted)) return std::nullopt;
    if (extracted.size() != kSlots.size()) return std::nullopt;

    Projection result;
    result.preset = *preset;
    result.contents.reserve(kSlots.size());
    for (const auto& [_, slot] : kSlots) {
        WidgetSnapshot content = snapshot;
        content.root = *extracted.at(slot);
        result.contents.push_back({slot, std::move(content)});
    }
    return result;
}

bool LauncherExperienceProjection::EnsureStagingTarget(
    ID2D1RenderTarget* target,
    const declarative::Rect viewport) {
    if (!target || !std::isfinite(viewport.width) || !std::isfinite(viewport.height) ||
        viewport.width <= 0.0F || viewport.height <= 0.0F) return false;
    const declarative::Size required{viewport.width, viewport.height};
    if (stagingTarget_ && parentTarget_ == target &&
        std::abs(stagingSize_.width - required.width) <= 0.01F &&
        std::abs(stagingSize_.height - required.height) <= 0.01F) return true;
    stagingTarget_.Reset();
    parentTarget_ = nullptr;
    if (FAILED(target->CreateCompatibleRenderTarget(
            D2D1::SizeF(required.width, required.height),
            stagingTarget_.ReleaseAndGetAddressOf()))) return false;
    parentTarget_ = target;
    stagingSize_ = required;
    return true;
}

LauncherPresentationFrame LauncherExperienceProjection::PreparePresentation(
    RemoteImageCache* imageCache,
    const std::wstring_view widgetId,
    const Projection& projection,
    const WidgetSnapshot& snapshot,
    const std::wstring_view focusedElementId,
    const NativeAccessibilityPolicy& accessibility,
    const std::uint64_t nowMilliseconds) {
    const auto* focused = FocusedGameNode(projection, focusedElementId);
    const auto* artwork = ArtworkNode(focused);
    const std::wstring artworkKey = artwork
        ? RemoteImageCache::TrustedArtworkKey(
            widgetId, artwork->id, artwork->artworkHandle)
        : std::wstring{};
    const auto decoded = imageCache && !artworkKey.empty()
        ? imageCache->GetReadyImage(artworkKey)
        : std::shared_ptr<const RemoteDecodedImage>{};
    const bool decodedValid = decoded && decoded->width > 0 && decoded->height > 0 &&
        decoded->stride == decoded->width * 4U &&
        decoded->premultipliedBgra.size() ==
            static_cast<std::size_t>(decoded->stride) * decoded->height;

    const auto* selected = selection_ && !selection_->followWidgetPreset
        ? &*selection_ : nullptr;
    std::wstring key = (selected ? selected->presentationRevision : L"builtin-live") +
        L"\n" + (safeStartActivation_ ? L"safe-start" : L"selected") + L"\n" +
        std::to_wstring(static_cast<int>(projection.preset)) + L"\n" +
        snapshot.instanceId + L"\n" + std::wstring(focusedElementId) + L"\n" +
        artworkKey + L"\n" + (decodedValid ? L"ready" : L"fallback") + L"\n" +
        (accessibility.reducedMotion ? L"motion-reduced" : L"motion-full") + L"\n" +
        (accessibility.reducedTransparency ? L"transparency-reduced" :
                                             L"transparency-full") + L"\n" +
        (accessibility.contrastHook ? L"contrast-high" : L"contrast-standard");
    if (key != activePresentationKey_) {
        LauncherPresentationRequest request;
        request.revision = selected
            ? selected->presentationRevision
            : L"builtin-live:" + std::wstring(PresetName(projection.preset));
        request.preset = projection.preset;
        request.builtIn = selected ? selected->builtIn : true;
        request.useGlobalAppearance = selected ? selected->useGlobalAppearance : false;
        request.safeStart = safeStartActivation_;
        request.packStyles = selected ? selected->packStyles
                                      : std::map<Slot, WidgetComputedStyle>{};
        request.packBackground = packBackground_;
        const auto backgroundMode = selected
            ? selected->backgroundMode : std::wstring{L"selected-game-artwork"};
        request.backgroundMode = backgroundMode == L"pack-asset"
            ? BackgroundMode::PackAsset
            : backgroundMode == L"selected-game-artwork"
                ? BackgroundMode::SelectedGameArtwork
                : BackgroundMode::Global;
        const auto focus = selected ? selected->focusEffect : std::wstring{L"lift"};
        request.focusEffect = !focused || focus == L"outline"
            ? FocusEffect::None
            : focus == L"scale" ? FocusEffect::Glow : FocusEffect::Lift;
        const auto motion = selected
            ? selected->motionIntensity : std::wstring{L"standard"};
        request.motionIntensity = motion == L"none"
            ? MotionIntensity::None
            : motion == L"reduced" ? MotionIntensity::Reduced
                                    : MotionIntensity::Standard;
        if (request.backgroundMode == BackgroundMode::SelectedGameArtwork && decodedValid) {
            auto selectedBackground = std::make_shared<DecodedLauncherAsset>();
            selectedBackground->opaqueAssetId = focused->id;
            selectedBackground->revision = artworkKey;
            selectedBackground->width = decoded->width;
            selectedBackground->height = decoded->height;
            selectedBackground->stride = decoded->stride;
            selectedBackground->sharedDecodedImage = decoded;
            request.selectedGameArtworkRevision = artworkKey;
            request.selectedGameBackground = std::move(selectedBackground);
        } else if (request.backgroundMode == BackgroundMode::SelectedGameArtwork &&
                   !artworkKey.empty()) {
            request.selectedGameArtworkRevision = artworkKey;
        }
        std::wstring diagnostic;
        if (presentationOwner_.Activate(
                request,
                {.reducedMotion = accessibility.reducedMotion,
                 .reducedTransparency = accessibility.reducedTransparency,
                 .highContrast = static_cast<bool>(accessibility.contrastHook)},
                nowMilliseconds, diagnostic)) {
            activePresentationKey_ = std::move(key);
        }
    }
    return presentationOwner_.Sample(nowMilliseconds);
}

LauncherExperienceProjection::ArtworkAvailability
LauncherExperienceProjection::InspectArtworkAvailability(
    RemoteImageCache* imageCache,
    const std::wstring_view widgetId,
    const Projection& projection) {
    if (!imageCache || widgetId.empty()) return ArtworkAvailability::Pending;
    const auto rail = std::find_if(
        projection.contents.begin(), projection.contents.end(),
        [](const auto& content) { return content.slot == Slot::GameRail; });
    if (rail == projection.contents.end()) return ArtworkAvailability::Pending;
    std::vector<const WidgetNode*> artwork;
    CollectArtworkNodes(rail->snapshot.root, artwork);
    std::size_t ready{};
    std::size_t pending{};
    std::size_t failed{};
    for (const auto* node : artwork) {
        const auto key = RemoteImageCache::TrustedArtworkKey(
            widgetId, node->id, node->artworkHandle);
        switch (imageCache->GetState(key)) {
        case RemoteImageState::Ready: ++ready; break;
        case RemoteImageState::Queued:
        case RemoteImageState::Loading: ++pending; break;
        case RemoteImageState::Failed: ++failed; break;
        case RemoteImageState::Missing: break;
        }
    }
    // Missing entries are off-page or have not yet entered ordinary renderer
    // admission. They do not keep a completed visible page in an empty hero
    // layout after all tracked requests have failed.
    if (failed > 0 && ready == 0 && pending == 0)
        return ArtworkAvailability::AllTerminal;
    if (failed > 0) return ArtworkAvailability::Mixed;
    if (ready > 0) return ArtworkAvailability::Available;
    return ArtworkAvailability::Pending;
}

void LauncherExperienceProjection::RetirePresentation() noexcept {
    presentationOwner_.FinishTransitions();
    activePresentationKey_.clear();
    pendingFocusInputMilliseconds_.reset();
}

ProductionProjectionResult LauncherExperienceProjection::Render(
    DeclarativeRenderer& renderer,
    ID2D1RenderTarget* target,
    RemoteImageCache* imageCache,
    const std::optional<WidgetAdvancedPresentationDeclaration>& declaration,
    const std::wstring_view presentationGeneration,
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot,
    const std::wstring_view focusedElementId,
    const declarative::Rect viewport,
    const DeclarativeRenderOptions& options) {
    const auto renderStarted = std::chrono::steady_clock::now();
    const auto presentationTime = options.animationTimestampMilliseconds.value_or(
        GetTickCount64());
    bool presentationRequested{};
    auto projection = Recognize(declaration, snapshot, presentationRequested);
    if (!projection) {
        ClearCanonical();
        RetirePresentation();
        auto render = renderer.Render(
            target, snapshot, focusedElementId, viewport, options);
        if (presentationRequested) {
            AddFallbackDiagnostic(
                render, L"advanced_presentation_invalid",
                L"The declared advanced presentation was unavailable or malformed; ordinary declarative content remained active.");
            return {std::move(render), ProductionProjectionDisposition::Fallback, {}};
        }
        return {std::move(render), ProductionProjectionDisposition::Ordinary, {}};
    }

    if (selection_ && !selection_->followWidgetPreset)
        projection->preset = selection_->preset;

    const auto artworkAvailability = InspectArtworkAvailability(
        imageCache, widgetId, *projection);
    const bool noArtworkHeroRail =
        projection->preset == Preset::HeroRail &&
        artworkAvailability == ArtworkAvailability::AllTerminal;

    if (!EnsureStagingTarget(target, viewport)) {
        ClearCanonical();
        RetirePresentation();
        auto render = renderer.Render(
            target, snapshot, focusedElementId, viewport, options);
        AddFallbackDiagnostic(
            render, L"launcher_projection_target_unavailable",
            L"The Launcher Experience staging target was unavailable; ordinary declarative content remained active.");
        return {std::move(render), ProductionProjectionDisposition::Fallback,
                projection->preset};
    }

    stagingTarget_->BeginDraw();
    stagingTarget_->SetTransform(D2D1::Matrix3x2F::Identity());
    stagingTarget_->Clear(D2D1::ColorF(0, 0.0F));
    const auto presentation = PreparePresentation(
        imageCache, widgetId, *projection, snapshot, focusedElementId,
        options.accessibility, presentationTime);
    const Recipe* selectedRecipe = selection_ && !selection_->followWidgetPreset &&
            !safeStartActivation_
        ? &selection_->recipe : nullptr;
    std::optional<Recipe> noArtworkRecipe;
    if (noArtworkHeroRail) {
        noArtworkRecipe = BuiltInNoArtworkHeroRailRecipe();
        selectedRecipe = &*noArtworkRecipe;
    }
    auto staged = RenderExperience(
        renderer, stagingTarget_.Get(), selectedRecipe, projection->preset,
        {0, 0, viewport.width, viewport.height}, projection->contents,
        focusedElementId, options, &presentation, &snapshot,
        noArtworkHeroRail
            ? GameRailPresentation::EqualWidthNoArtwork
            : GameRailPresentation::Authored);
    const HRESULT endResult = stagingTarget_->EndDraw();
#ifdef GBA_DECLARATIVE_RENDERER_TESTING
    const bool forcedFailure = std::exchange(failNextAdapterFrameForTesting_, false);
#else
    constexpr bool forcedFailure = false;
#endif
    if (forcedFailure || FAILED(endResult) ||
        !staged.layout.valid() || !staged.render.succeeded) {
        ClearCanonical();
        presentationOwner_.FinishTransitions();
        auto render = renderer.Render(
            target, snapshot, focusedElementId, viewport, options);
        AddFallbackDiagnostic(
            render, L"launcher_projection_render_failed",
            L"The Launcher Experience adapter could not produce a complete frame; ordinary declarative content remained active.");
        return {std::move(render), ProductionProjectionDisposition::Fallback,
                projection->preset};
    }

    Microsoft::WRL::ComPtr<ID2D1Bitmap> bitmap;
    if (FAILED(stagingTarget_->GetBitmap(bitmap.ReleaseAndGetAddressOf())) || !bitmap) {
        ClearCanonical();
        presentationOwner_.FinishTransitions();
        auto render = renderer.Render(
            target, snapshot, focusedElementId, viewport, options);
        AddFallbackDiagnostic(
            render, L"launcher_projection_bitmap_unavailable",
            L"The Launcher Experience frame could not be committed; ordinary declarative content remained active.");
        return {std::move(render), ProductionProjectionDisposition::Fallback,
                projection->preset};
    }
    target->DrawBitmap(
        bitmap.Get(),
        D2D1::RectF(viewport.x, viewport.y,
                    viewport.x + viewport.width, viewport.y + viewport.height),
        1.0F, D2D1_BITMAP_INTERPOLATION_MODE_LINEAR,
        D2D1::RectF(0, 0, viewport.width, viewport.height));
    OffsetRenderResult(staged.render, viewport.x, viewport.y);
    RetainCanonical(
        widgetId, presentationGeneration, std::move(staged.semanticSnapshot));
    const auto committedAt = GetTickCount64();
    const auto renderCommitMilliseconds = std::chrono::duration<double, std::milli>(
        std::chrono::steady_clock::now() - renderStarted).count();
    presentationOwner_.RecordFrameTiming(0.0, renderCommitMilliseconds);
    if (pendingFocusInputMilliseconds_ &&
        committedAt >= *pendingFocusInputMilliseconds_) {
        presentationOwner_.RecordInputToFocus(static_cast<double>(
            committedAt - *pendingFocusInputMilliseconds_));
        pendingFocusInputMilliseconds_.reset();
    }
    const auto nextPresentation = presentationOwner_.Sample(committedAt);
    staged.render.animationActive = staged.render.animationActive ||
        presentation.transitionActive ||
        nextPresentation.effectQuality != presentation.effectQuality;
    ProductionProjectionResult result{
        std::move(staged.render), ProductionProjectionDisposition::Adopted,
        projection->preset};
    result.presentationActive = true;
    result.effectQuality = presentation.effectQuality;
    result.backgroundIsFallback = presentation.backgroundIsFallback;
    result.backgroundTransitionActive = presentation.transitionActive;
    result.previousBackgroundOpacity = presentation.previousBackgroundOpacity;
    result.currentBackgroundOpacity = presentation.currentBackgroundOpacity;
    result.backgroundFocusId = presentation.currentBackground
        ? presentation.currentBackground->opaqueAssetId
        : std::wstring{};
    if (selection_ && !selection_->followWidgetPreset) {
        result.selectionIdentity = selection_->id + L"@" + selection_->version;
        result.selectionUsesGlobalAppearance = selection_->useGlobalAppearance;
    } else {
        result.selectionIdentity = L"widget-preset";
    }
    result.safeStart = safeStartActivation_;
    result.layoutBranch = BranchName(staged.layout.branch);
    const auto* rail = staged.layout.Find(Slot::GameRail);
    const auto* details = staged.layout.Find(Slot::DetailsPanel);
    result.railOrientation = OrientationName(
        rail ? rail->orientation : std::optional<Orientation>{});
    result.detailsSurface = SurfaceName(
        details ? details->surface : std::optional<Surface>{});
    result.artworkAvailability = [&] {
        switch (artworkAvailability) {
        case ArtworkAvailability::Pending: return std::wstring{L"pending"};
        case ArtworkAvailability::Available: return std::wstring{L"available"};
        case ArtworkAvailability::Mixed: return std::wstring{L"mixed"};
        case ArtworkAvailability::AllTerminal: return std::wstring{L"all-terminal"};
        }
        return std::wstring{L"unknown"};
    }();
    result.bodyBounds = viewport;
    if (rail) result.railBounds = rail->bounds;
    result.textScale = options.accessibility.textScale;
    result.reducedMotion = options.accessibility.reducedMotion;
    result.reducedTransparency = options.accessibility.reducedTransparency;
    result.highContrast = static_cast<bool>(options.accessibility.contrastHook);
    result.presentationMetrics = presentationOwner_.metrics();
    return result;
}

void LauncherExperienceProjection::ObserveFocusInput(
    const std::wstring_view widgetId,
    const std::uint64_t nowMilliseconds) noexcept {
    if (!canonicalSnapshot_ || canonicalWidgetId_ != widgetId) return;
    if (!pendingFocusInputMilliseconds_)
        pendingFocusInputMilliseconds_ = nowMilliseconds;
}

const WidgetSnapshot& LauncherExperienceProjection::InteractionSnapshot(
    const std::wstring_view widgetId,
    const std::wstring_view presentationGeneration,
    const WidgetSnapshot& source) const noexcept {
    if (canonicalSnapshot_ && canonicalWidgetId_ == widgetId &&
        canonicalPresentationGeneration_ == presentationGeneration &&
        canonicalSnapshot_->instanceId == source.instanceId &&
        canonicalSnapshot_->sequence == source.sequence &&
        canonicalSnapshot_->activeInputScopeId == source.activeInputScopeId)
        return *canonicalSnapshot_;
    return source;
}

std::optional<std::wstring> LauncherExperienceProjection::ProjectedFocusTarget(
    const std::wstring_view widgetId,
    const std::wstring_view focusedElementId,
    const std::wstring_view direction) const {
    if (!canonicalSnapshot_ || canonicalWidgetId_ != widgetId)
        return std::nullopt;
    const auto& canonical = *canonicalSnapshot_;
    const auto* focused = FindProjectedNode(canonical.root, focusedElementId);
    if (!focused) return std::nullopt;
    const std::wstring* target = nullptr;
    if (direction == L"up") target = &focused->focusUp;
    else if (direction == L"down") target = &focused->focusDown;
    else if (direction == L"left") target = &focused->focusLeft;
    else if (direction == L"right") target = &focused->focusRight;
    if (!target || target->empty()) return std::nullopt;
    const auto* resolved = FindProjectedNode(canonical.root, *target);
    return resolved ? std::optional<std::wstring>{resolved->id} : std::nullopt;
}

void LauncherExperienceProjection::DiscardTargetResources() noexcept {
    stagingTarget_.Reset();
    parentTarget_ = nullptr;
    stagingSize_ = {};
    ClearCanonical();
    RetirePresentation();
}

void LauncherExperienceProjection::RetainCanonical(
    const std::wstring_view widgetId,
    const std::wstring_view presentationGeneration,
    WidgetSnapshot snapshot) {
    canonicalWidgetId_ = widgetId;
    canonicalPresentationGeneration_ = presentationGeneration;
    canonicalSnapshot_ = std::move(snapshot);
}

void LauncherExperienceProjection::ClearCanonical() noexcept {
    canonicalWidgetId_.clear();
    canonicalPresentationGeneration_.clear();
    canonicalSnapshot_.reset();
}

} // namespace gba::launcher
