#pragma once

#include "LauncherExperienceLayout.h"
#include "NativeStyle.h"

#include <cstddef>
#include <cstdint>
#include <map>
#include <memory>
#include <optional>
#include <set>
#include <string>
#include <string_view>
#include <vector>

struct IWICImagingFactory;

namespace gba::launcher {

constexpr std::size_t MaximumLauncherAssetBytes = 16U * 1024U * 1024U;
constexpr std::uint32_t MaximumLauncherAssetDimension = 4096;
constexpr std::size_t MaximumLauncherDecodedBytes =
    static_cast<std::size_t>(MaximumLauncherAssetDimension) *
    MaximumLauncherAssetDimension * 4U;

enum class StaticImageFormat { Png, Jpeg, WebP };
enum class BackgroundMode { Global, PackAsset, SelectedGameArtwork };
enum class FocusEffect { None, Lift, Glow };
enum class MotionIntensity { None, Reduced, Standard };
enum class EffectQuality { Full, OpacityOnly, Immediate };

/// Host-only encoded asset value. It deliberately has no path, URL, widget ID,
/// or public-protocol representation. The catalog/media boundary supplies the
/// immutable revision and bytes after package validation.
struct SealedAssetBytes final {
    std::wstring opaqueAssetId;
    std::wstring revision;
    StaticImageFormat format{StaticImageFormat::Png};
    std::vector<std::uint8_t> bytes;
};

struct DecodedLauncherAsset final {
    std::wstring opaqueAssetId;
    std::wstring revision;
    std::uint32_t width{};
    std::uint32_t height{};
    std::uint32_t stride{};
    std::vector<std::uint8_t> premultipliedBgra;
};

struct AssetDecodeResult final {
    std::shared_ptr<const DecodedLauncherAsset> asset;
    std::wstring diagnostic;
    [[nodiscard]] bool succeeded() const noexcept { return asset != nullptr; }
};

/// Decodes bounded static package bytes through the host WIC owner. Callers do
/// not receive or provide native paths, and the decoded allocation is bounded
/// before CopyPixels.
[[nodiscard]] AssetDecodeResult DecodeSealedLauncherAsset(
    IWICImagingFactory* imagingFactory,
    const SealedAssetBytes& source);

struct LauncherAccessibility final {
    bool reducedMotion{};
    bool reducedTransparency{};
    bool highContrast{};
};

struct LauncherPresentationRequest final {
    std::wstring revision;
    Preset preset{Preset::HeroRail};
    bool useGlobalAppearance{};
    bool safeStart{};
    BackgroundMode backgroundMode{BackgroundMode::Global};
    FocusEffect focusEffect{FocusEffect::None};
    MotionIntensity motionIntensity{MotionIntensity::Standard};
    /// Launcher-only pack and user layers. The API is keyed by the closed Slot
    /// enum, so these rules cannot address shell or another widget role.
    std::map<Slot, WidgetComputedStyle> packStyles;
    std::map<Slot, WidgetComputedStyle> userStyles;
    std::shared_ptr<const DecodedLauncherAsset> packBackground;
    std::shared_ptr<const DecodedLauncherAsset> selectedGameBackground;
    std::wstring selectedGameArtworkRevision;
};

struct LauncherPresentationFrame final {
    std::wstring revision;
    Preset preset{Preset::HeroRail};
    bool builtIn{true};
    bool useGlobalAppearance{true};
    bool backgroundBlurEnabled{};
    bool backgroundIsFallback{true};
    EffectQuality effectQuality{EffectQuality::Full};
    std::map<Slot, WidgetComputedStyle> slotStyles;
    WidgetComputedStyle focusedGameStyle;
    std::shared_ptr<const DecodedLauncherAsset> previousBackground;
    std::shared_ptr<const DecodedLauncherAsset> currentBackground;
    float previousBackgroundOpacity{};
    float currentBackgroundOpacity{1.0F};
};

struct LauncherPresentationMetrics final {
    double maximumInputDispatchMilliseconds{};
    double maximumRenderCommitMilliseconds{};
    std::size_t degradedFrameCount{};
};

/// Owns one atomic launcher presentation revision. Layout, actions, focus
/// identity, provider state, and compositor lifetime remain with their existing
/// owners. A failed candidate never becomes partially visible.
class LauncherExperiencePresentationOwner final {
public:
    [[nodiscard]] bool Activate(
        const LauncherPresentationRequest& request,
        LauncherAccessibility accessibility,
        std::uint64_t nowMilliseconds,
        std::wstring& diagnostic);
    void RejectRevision(
        std::wstring_view revision,
        Preset recoveryPreset,
        std::wstring_view diagnostic);
    void RecordFrameTiming(
        double inputDispatchMilliseconds,
        double renderCommitMilliseconds) noexcept;
    [[nodiscard]] LauncherPresentationFrame Sample(
        std::uint64_t nowMilliseconds) const;
    [[nodiscard]] bool IsRevisionDisabled(std::wstring_view revision) const;
    [[nodiscard]] const LauncherPresentationMetrics& metrics() const noexcept {
        return metrics_;
    }

private:
    [[nodiscard]] LauncherPresentationFrame BuiltIn(Preset preset) const;
    void CountFailure(std::wstring_view revision, Preset preset);

    LauncherPresentationFrame current_;
    LauncherAccessibility accessibility_;
    std::uint64_t transitionStartMilliseconds_{};
    std::uint64_t transitionDurationMilliseconds_{};
    EffectQuality quality_{EffectQuality::Full};
    std::map<std::wstring, std::size_t, std::less<>> failures_;
    std::set<std::wstring, std::less<>> disabledRevisions_;
    LauncherPresentationMetrics metrics_;
};

/// Applies the resolved launcher-only layer to a copied host snapshot. This is
/// intentionally downstream of global computed styles and upstream of the
/// renderer's final NativeAccessibilityPolicy.
void ApplyLauncherPresentationStyles(
    Slot slot,
    WidgetSnapshot& snapshot,
    const LauncherPresentationFrame& presentation,
    std::wstring_view focusedElementId);

} // namespace gba::launcher
