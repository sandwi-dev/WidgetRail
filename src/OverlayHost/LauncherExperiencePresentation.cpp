#include "LauncherExperiencePresentation.h"

#include <Windows.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
#include <cmath>
#include <limits>

namespace gba::launcher {
namespace {

using Microsoft::WRL::ComPtr;

constexpr std::size_t kFailureLimit = 3;
constexpr double kInputLatencyBudgetMilliseconds = 8.0;
constexpr double kRenderBudgetMilliseconds = 16.0;

WidgetStyleValue Number(const double value) {
    return {L"number", std::to_wstring(value), value, {}};
}

WidgetStyleValue Length(const double value) {
    return {L"length", std::to_wstring(value) + L"px", value, L"px"};
}

WidgetStyleValue Duration(const double value) {
    return {L"duration", std::to_wstring(value) + L"ms", value, L"ms"};
}

void Merge(WidgetComputedStyle& destination, const WidgetComputedStyle& source) {
    for (const auto& [property, value] : source)
        destination.insert_or_assign(property, value);
}

void ApplyAccessibility(
    WidgetComputedStyle& style,
    const LauncherAccessibility accessibility) {
    if (accessibility.reducedTransparency || accessibility.highContrast) {
        style.erase(L"background-blur");
        style.erase(L"shadow-blur");
        style.erase(L"shadow-color");
        style.insert_or_assign(L"opacity", Number(1.0));
    }
    if (accessibility.reducedMotion || accessibility.highContrast) {
        style.erase(L"scale");
        style.erase(L"translate-x");
        style.erase(L"translate-y");
        style.insert_or_assign(L"transition-duration", Duration(0.0));
    }
    if (accessibility.highContrast)
        style.insert_or_assign(L"outline-width", Length(3.0));
}

bool IsExpectedContainer(const GUID& actual, const StaticImageFormat expected) noexcept {
    if (expected == StaticImageFormat::Png) return IsEqualGUID(actual, GUID_ContainerFormatPng);
    if (expected == StaticImageFormat::Jpeg) return IsEqualGUID(actual, GUID_ContainerFormatJpeg);
    return IsEqualGUID(actual, GUID_ContainerFormatWebp);
}

std::uint64_t TransitionDuration(const MotionIntensity intensity) noexcept {
    if (intensity == MotionIntensity::None) return 0;
    return intensity == MotionIntensity::Reduced ? 90 : 180;
}

void ApplyFocusEffect(
    WidgetComputedStyle& style,
    const FocusEffect effect,
    const MotionIntensity intensity,
    const LauncherAccessibility accessibility) {
    if (effect == FocusEffect::Lift) {
        style.insert_or_assign(L"scale", Number(1.04));
        style.insert_or_assign(L"translate-y", Length(-6.0));
    } else if (effect == FocusEffect::Glow) {
        style.insert_or_assign(L"shadow-blur", Length(12.0));
        style.insert_or_assign(L"outline-width", Length(2.0));
    }
    style.insert_or_assign(
        L"transition-duration", Duration(static_cast<double>(TransitionDuration(intensity))));
    ApplyAccessibility(style, accessibility);
}

void ApplyToFocusedNode(
    WidgetNode& node,
    const std::wstring_view focusedElementId,
    const WidgetComputedStyle& focusedStyle) {
    if (node.id == focusedElementId) Merge(node.focusedStyle, focusedStyle);
    for (auto& child : node.children)
        ApplyToFocusedNode(child, focusedElementId, focusedStyle);
}

} // namespace

AssetDecodeResult DecodeSealedLauncherAsset(
    IWICImagingFactory* imagingFactory,
    const SealedAssetBytes& source) {
    const auto Failure = [](std::wstring message) {
        return AssetDecodeResult{nullptr, std::move(message)};
    };
    if (!imagingFactory) return Failure(L"Launcher asset decoder is unavailable.");
    if (source.opaqueAssetId.empty() || source.revision.empty())
        return Failure(L"Launcher asset identity or revision is absent.");
    if (source.bytes.empty() || source.bytes.size() > MaximumLauncherAssetBytes ||
        source.bytes.size() > std::numeric_limits<DWORD>::max())
        return Failure(L"Launcher asset exceeds the encoded-byte bound.");

    ComPtr<IWICStream> stream;
    ComPtr<IWICBitmapDecoder> decoder;
    ComPtr<IWICBitmapFrameDecode> frame;
    ComPtr<IWICFormatConverter> converter;
    GUID container{};
    UINT frameCount{};
    UINT width{};
    UINT height{};
    auto result = imagingFactory->CreateStream(stream.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result)) {
        result = stream->InitializeFromMemory(
            const_cast<BYTE*>(source.bytes.data()), static_cast<DWORD>(source.bytes.size()));
    }
    if (SUCCEEDED(result)) {
        result = imagingFactory->CreateDecoderFromStream(
            stream.Get(), nullptr, WICDecodeMetadataCacheOnLoad,
            decoder.ReleaseAndGetAddressOf());
    }
    if (SUCCEEDED(result)) result = decoder->GetContainerFormat(&container);
    if (SUCCEEDED(result) && !IsExpectedContainer(container, source.format))
        return Failure(L"Launcher asset bytes do not match the declared static format.");
    if (SUCCEEDED(result)) result = decoder->GetFrameCount(&frameCount);
    if (SUCCEEDED(result) && frameCount != 1)
        return Failure(L"Animated or multi-frame launcher assets are not supported.");
    if (SUCCEEDED(result)) result = decoder->GetFrame(0, frame.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result)) result = frame->GetSize(&width, &height);
    if (FAILED(result) || width == 0 || height == 0 ||
        width > MaximumLauncherAssetDimension || height > MaximumLauncherAssetDimension)
        return Failure(L"Launcher asset decode or dimension validation failed.");

    const auto stride64 = static_cast<std::uint64_t>(width) * 4U;
    const auto decoded64 = stride64 * height;
    if (stride64 > std::numeric_limits<UINT>::max() ||
        decoded64 > MaximumLauncherDecodedBytes ||
        decoded64 > std::numeric_limits<UINT>::max())
        return Failure(L"Launcher asset exceeds the decoded-pixel bound.");

    result = imagingFactory->CreateFormatConverter(converter.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result)) {
        result = converter->Initialize(
            frame.Get(), GUID_WICPixelFormat32bppPBGRA,
            WICBitmapDitherTypeNone, nullptr, 0.0, WICBitmapPaletteTypeCustom);
    }
    auto decoded = std::make_shared<DecodedLauncherAsset>();
    decoded->opaqueAssetId = source.opaqueAssetId;
    decoded->revision = source.revision;
    decoded->width = width;
    decoded->height = height;
    decoded->stride = static_cast<std::uint32_t>(stride64);
    decoded->premultipliedBgra.resize(static_cast<std::size_t>(decoded64));
    if (SUCCEEDED(result)) {
        result = converter->CopyPixels(
            nullptr, decoded->stride, static_cast<UINT>(decoded->premultipliedBgra.size()),
            decoded->premultipliedBgra.data());
    }
    if (FAILED(result)) return Failure(L"WIC rejected or could not decode the launcher asset.");
    return {std::move(decoded), {}};
}

LauncherPresentationFrame LauncherExperiencePresentationOwner::BuiltIn(
    const Preset preset) const {
    LauncherPresentationFrame result;
    result.revision = L"builtin:" + std::to_wstring(static_cast<int>(preset));
    result.preset = preset;
    result.builtIn = true;
    result.useGlobalAppearance = true;
    result.backgroundIsFallback = true;
    result.effectQuality = quality_;
    return result;
}

void LauncherExperiencePresentationOwner::CountFailure(
    const std::wstring_view revision,
    const Preset preset) {
    if (revision.empty()) return;
    auto& count = failures_[std::wstring(revision)];
    ++count;
    if (count >= kFailureLimit) {
        disabledRevisions_.insert(std::wstring(revision));
        current_ = BuiltIn(preset);
        transitionDurationMilliseconds_ = 0;
    }
}

bool LauncherExperiencePresentationOwner::Activate(
    const LauncherPresentationRequest& request,
    const LauncherAccessibility accessibility,
    const std::uint64_t nowMilliseconds,
    std::wstring& diagnostic) {
    diagnostic.clear();
    if (request.safeStart) {
        current_ = BuiltIn(request.preset);
        accessibility_ = accessibility;
        transitionDurationMilliseconds_ = 0;
        diagnostic = L"Safe start selected the built-in launcher experience.";
        return true;
    }
    if (request.revision.empty()) {
        diagnostic = L"Launcher experience revision is absent.";
        return false;
    }
    if (IsRevisionDisabled(request.revision)) {
        diagnostic = L"Launcher experience revision is disabled after repeated failures.";
        return false;
    }

    LauncherPresentationFrame candidate;
    candidate.revision = request.revision;
    candidate.preset = request.preset;
    candidate.builtIn = false;
    candidate.useGlobalAppearance = request.useGlobalAppearance;
    candidate.backgroundBlurEnabled = !accessibility.reducedTransparency &&
        !accessibility.highContrast;
    candidate.effectQuality = quality_;
    if (!request.useGlobalAppearance) {
        candidate.slotStyles = request.packStyles;
        for (const auto& [slot, style] : request.userStyles)
            Merge(candidate.slotStyles[slot], style);
        for (auto& [slot, style] : candidate.slotStyles) {
            (void)slot;
            ApplyAccessibility(style, accessibility);
            const auto adapted = NativeStyleAdapter::Adapt(style, {}, {});
            if (!adapted.diagnostics.empty()) {
                diagnostic = L"Launcher style layer failed native validation.";
                CountFailure(request.revision, request.preset);
                return false;
            }
        }
        ApplyFocusEffect(
            candidate.focusedGameStyle, request.focusEffect,
            request.motionIntensity, accessibility);
    }

    std::shared_ptr<const DecodedLauncherAsset> background;
    if (!accessibility.highContrast && !request.useGlobalAppearance) {
        if (request.backgroundMode == BackgroundMode::PackAsset) {
            if (request.packBackground && request.packBackground->revision == request.revision)
                background = request.packBackground;
            else
                diagnostic = L"Pack background was unavailable; the host fallback is retained.";
        } else if (request.backgroundMode == BackgroundMode::SelectedGameArtwork) {
            if (request.selectedGameBackground &&
                request.selectedGameBackground->revision == request.selectedGameArtworkRevision)
                background = request.selectedGameBackground;
            else
                diagnostic = L"Selected-game artwork was stale or unavailable; the host fallback is retained.";
        }
    }
    if (!background && !accessibility.highContrast && !request.useGlobalAppearance &&
        request.backgroundMode != BackgroundMode::Global)
        background = current_.currentBackground;
    candidate.previousBackground = current_.currentBackground;
    candidate.currentBackground = std::move(background);
    if (candidate.previousBackground == candidate.currentBackground)
        candidate.previousBackground.reset();
    candidate.backgroundIsFallback = candidate.currentBackground == nullptr;
    candidate.previousBackgroundOpacity = candidate.previousBackground ? 1.0F : 0.0F;
    candidate.currentBackgroundOpacity =
        candidate.currentBackground && candidate.previousBackground ? 0.0F : 1.0F;

    current_ = std::move(candidate);
    accessibility_ = accessibility;
    transitionStartMilliseconds_ = nowMilliseconds;
    transitionDurationMilliseconds_ = accessibility.reducedMotion
        ? 0 : TransitionDuration(request.motionIntensity);
    failures_.erase(request.revision);
    return true;
}

void LauncherExperiencePresentationOwner::RejectRevision(
    const std::wstring_view revision,
    const Preset recoveryPreset,
    const std::wstring_view) {
    CountFailure(revision, recoveryPreset);
}

void LauncherExperiencePresentationOwner::RecordFrameTiming(
    const double inputDispatchMilliseconds,
    const double renderCommitMilliseconds) noexcept {
    if (std::isfinite(inputDispatchMilliseconds))
        metrics_.maximumInputDispatchMilliseconds = std::max(
            metrics_.maximumInputDispatchMilliseconds, inputDispatchMilliseconds);
    if (std::isfinite(renderCommitMilliseconds))
        metrics_.maximumRenderCommitMilliseconds = std::max(
            metrics_.maximumRenderCommitMilliseconds, renderCommitMilliseconds);
    const auto previous = quality_;
    if (!std::isfinite(inputDispatchMilliseconds) ||
        inputDispatchMilliseconds > kInputLatencyBudgetMilliseconds) {
        quality_ = EffectQuality::Immediate;
    } else if (quality_ == EffectQuality::Full &&
               (!std::isfinite(renderCommitMilliseconds) ||
                renderCommitMilliseconds > kRenderBudgetMilliseconds)) {
        quality_ = EffectQuality::OpacityOnly;
    }
    if (quality_ != previous) ++metrics_.degradedFrameCount;
}

LauncherPresentationFrame LauncherExperiencePresentationOwner::Sample(
    const std::uint64_t nowMilliseconds) const {
    auto result = current_.revision.empty() ? BuiltIn(Preset::HeroRail) : current_;
    result.effectQuality = quality_;
    if (quality_ != EffectQuality::Full || accessibility_.reducedMotion) {
        result.focusedGameStyle.erase(L"scale");
        result.focusedGameStyle.erase(L"translate-x");
        result.focusedGameStyle.erase(L"translate-y");
    }
    if (quality_ == EffectQuality::Immediate) {
        result.focusedGameStyle.insert_or_assign(L"transition-duration", Duration(0.0));
        result.previousBackgroundOpacity = 0;
        result.currentBackgroundOpacity = 1;
        return result;
    }
    if (!result.currentBackground || !result.previousBackground ||
        transitionDurationMilliseconds_ == 0) {
        result.previousBackgroundOpacity = 0;
        result.currentBackgroundOpacity = 1;
        return result;
    }
    const auto elapsed = nowMilliseconds > transitionStartMilliseconds_
        ? nowMilliseconds - transitionStartMilliseconds_ : 0;
    const auto progress = std::clamp(
        static_cast<float>(elapsed) / static_cast<float>(transitionDurationMilliseconds_),
        0.0F, 1.0F);
    result.previousBackgroundOpacity = 1.0F - progress;
    result.currentBackgroundOpacity = progress;
    return result;
}

bool LauncherExperiencePresentationOwner::IsRevisionDisabled(
    const std::wstring_view revision) const {
    return disabledRevisions_.contains(revision);
}

void ApplyLauncherPresentationStyles(
    const Slot slot,
    WidgetSnapshot& snapshot,
    const LauncherPresentationFrame& presentation,
    const std::wstring_view focusedElementId) {
    if (presentation.useGlobalAppearance) return;
    if (const auto found = presentation.slotStyles.find(slot);
        found != presentation.slotStyles.end())
        Merge(snapshot.root.baseStyle, found->second);
    if (slot == Slot::GameRail)
        ApplyToFocusedNode(snapshot.root, focusedElementId, presentation.focusedGameStyle);
}

} // namespace gba::launcher
