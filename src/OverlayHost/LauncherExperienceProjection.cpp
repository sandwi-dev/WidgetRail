#include "LauncherExperienceProjection.h"
#include <algorithm>
#include <array>
#include <chrono>
#include <cmath>
#include <map>
#include <utility>

namespace gba::launcher {
namespace {

constexpr std::wstring_view kWidgetId = L"game-launcher";
constexpr std::wstring_view kMarker = L"game-launcher-experience";
constexpr std::wstring_view kProfilePrefix = L"game-launcher-experience--";
constexpr std::wstring_view kSlotClass = L"game-launcher-slot";
constexpr std::wstring_view kSlotPrefix = L"game-launcher-slot--";

constexpr std::array<std::pair<std::wstring_view, Preset>, 4> kProfiles{{
    {L"hero-rail", Preset::HeroRail},
    {L"cover-wall", Preset::CoverWall},
    {L"carousel", Preset::Carousel},
    {L"compact-grid", Preset::CompactGrid},
}};

constexpr std::array<std::pair<std::wstring_view, Slot>, 6> kSlots{{
    {L"details-panel", Slot::DetailsPanel},
    {L"game-rail", Slot::GameRail},
    {L"collection-tabs", Slot::CollectionTabs},
    {L"source-status", Slot::SourceStatus},
    {L"operation-status", Slot::OperationStatus},
    {L"controller-hints", Slot::ControllerHints},
}};

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

std::size_t ClassCount(
    const WidgetNode& node,
    const std::wstring_view expected) noexcept {
    return static_cast<std::size_t>(std::count(
        node.styleClasses.begin(), node.styleClasses.end(), expected));
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

std::optional<LauncherExperienceProjection::Projection>
LauncherExperienceProjection::Recognize(
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot,
    bool& markerPresent) {
    markerPresent = ClassCount(snapshot.root, kMarker) != 0;
    if (widgetId != kWidgetId || !markerPresent) return std::nullopt;
    if (ClassCount(snapshot.root, kMarker) != 1 ||
        snapshot.root.children.size() != kSlots.size()) return std::nullopt;

    std::optional<Preset> preset;
    std::size_t profileClassCount{};
    for (const auto& styleClass : snapshot.root.styleClasses) {
        if (!std::wstring_view{styleClass}.starts_with(kProfilePrefix)) continue;
        ++profileClassCount;
        const auto value = std::wstring_view{styleClass}.substr(kProfilePrefix.size());
        const auto found = std::find_if(
            kProfiles.begin(), kProfiles.end(),
            [value](const auto& candidate) { return candidate.first == value; });
        if (found != kProfiles.end()) preset = found->second;
    }
    if (profileClassCount != 1 || !preset) return std::nullopt;

    std::map<Slot, const WidgetNode*> extracted;
    for (const auto& child : snapshot.root.children) {
        if (ClassCount(child, kSlotClass) != 1) return std::nullopt;
        std::optional<Slot> slot;
        std::size_t slotClassCount{};
        for (const auto& styleClass : child.styleClasses) {
            if (!std::wstring_view{styleClass}.starts_with(kSlotPrefix)) continue;
            ++slotClassCount;
            const auto value = std::wstring_view{styleClass}.substr(kSlotPrefix.size());
            const auto found = std::find_if(
                kSlots.begin(), kSlots.end(),
                [value](const auto& candidate) { return candidate.first == value; });
            if (found != kSlots.end()) slot = found->second;
        }
        if (slotClassCount != 1 || !slot || !extracted.emplace(*slot, &child).second)
            return std::nullopt;
    }
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

    std::wstring key = std::to_wstring(static_cast<int>(projection.preset)) + L"\n" +
        snapshot.instanceId + L"\n" + std::wstring(focusedElementId) + L"\n" +
        artworkKey + L"\n" + (decodedValid ? L"ready" : L"fallback") + L"\n" +
        (accessibility.reducedMotion ? L"motion-reduced" : L"motion-full") + L"\n" +
        (accessibility.reducedTransparency ? L"transparency-reduced" :
                                             L"transparency-full") + L"\n" +
        (accessibility.contrastHook ? L"contrast-high" : L"contrast-standard");
    if (key != activePresentationKey_) {
        LauncherPresentationRequest request;
        request.revision = L"builtin-live:" +
            std::wstring(PresetName(projection.preset));
        request.preset = projection.preset;
        request.builtIn = true;
        request.useGlobalAppearance = false;
        request.backgroundMode = BackgroundMode::SelectedGameArtwork;
        request.focusEffect = focused ? FocusEffect::Lift : FocusEffect::None;
        request.motionIntensity = MotionIntensity::Standard;
        if (decodedValid) {
            auto background = std::make_shared<DecodedLauncherAsset>();
            background->opaqueAssetId = focused->id;
            background->revision = artworkKey;
            background->width = decoded->width;
            background->height = decoded->height;
            background->stride = decoded->stride;
            background->sharedDecodedImage = decoded;
            request.selectedGameArtworkRevision = artworkKey;
            request.selectedGameBackground = std::move(background);
        } else if (!artworkKey.empty()) {
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

void LauncherExperienceProjection::RetirePresentation() noexcept {
    presentationOwner_.FinishTransitions();
    activePresentationKey_.clear();
    pendingFocusInputMilliseconds_.reset();
}

ProductionProjectionResult LauncherExperienceProjection::Render(
    DeclarativeRenderer& renderer,
    ID2D1RenderTarget* target,
    RemoteImageCache* imageCache,
    const std::wstring_view widgetId,
    const WidgetSnapshot& snapshot,
    const std::wstring_view focusedElementId,
    const declarative::Rect viewport,
    const DeclarativeRenderOptions& options) {
    const auto renderStarted = std::chrono::steady_clock::now();
    const auto presentationTime = options.animationTimestampMilliseconds.value_or(
        GetTickCount64());
    bool markerPresent{};
    auto projection = Recognize(widgetId, snapshot, markerPresent);
    if (!projection) {
        ClearCanonical();
        RetirePresentation();
        auto render = renderer.Render(
            target, snapshot, focusedElementId, viewport, options);
        if (markerPresent && widgetId == kWidgetId) {
            AddFallbackDiagnostic(
                render, L"launcher_projection_invalid",
                L"The first-party Launcher Experience projection was malformed; ordinary declarative content remained active.");
            return {std::move(render), ProductionProjectionDisposition::Fallback, {}};
        }
        return {std::move(render), ProductionProjectionDisposition::Ordinary, {}};
    }

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
    auto staged = RenderExperience(
        renderer, stagingTarget_.Get(), nullptr, projection->preset,
        {0, 0, viewport.width, viewport.height}, projection->contents,
        focusedElementId, options, &presentation, &snapshot);
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
    RetainCanonical(widgetId, std::move(staged.semanticSnapshot));
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
    result.presentationMetrics = presentationOwner_.metrics();
    return result;
}

void LauncherExperienceProjection::ObserveFocusInput(
    const std::wstring_view widgetId,
    const std::uint64_t nowMilliseconds) noexcept {
    if (widgetId != kWidgetId) return;
    if (!pendingFocusInputMilliseconds_)
        pendingFocusInputMilliseconds_ = nowMilliseconds;
}

const WidgetSnapshot& LauncherExperienceProjection::InteractionSnapshot(
    const std::wstring_view widgetId,
    const WidgetSnapshot& source) const noexcept {
    if (canonicalSnapshot_ && canonicalWidgetId_ == widgetId &&
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
    WidgetSnapshot snapshot) {
    canonicalWidgetId_ = widgetId;
    canonicalSnapshot_ = std::move(snapshot);
}

void LauncherExperienceProjection::ClearCanonical() noexcept {
    canonicalWidgetId_.clear();
    canonicalSnapshot_.reset();
}

} // namespace gba::launcher
