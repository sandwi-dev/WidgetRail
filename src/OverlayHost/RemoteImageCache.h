#pragma once
#include "ImageDecodeSize.h"
#include <set>
#include <map>

#include <Windows.h>
#include <d2d1.h>
#include <wrl/client.h>

#include <array>
#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <functional>
#include <memory>
#include <mutex>
#include <optional>
#include <stop_token>
#include <string>
#include <string_view>
#include <thread>
#include <unordered_map>
#include <vector>

namespace widgetrail {

class ArtworkDecoderProcessOwner;

enum class RemoteImageState {
    Missing,
    Queued,
    Loading,
    Ready,
    Failed,
};

enum class RemoteImageRequestResult {
    Queued,
    AlreadyTracked,
    InvalidUrl,
    CapacityExceeded,
    ShuttingDown,
};

struct TrustedArtworkDemandAuthority final {
    std::wstring widgetId;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;

    friend bool operator==(
        const TrustedArtworkDemandAuthority&,
        const TrustedArtworkDemandAuthority&) = default;
};

enum class PackageIconRasterVariant {
    OriginalColor,
    AlphaMask,
};

struct PackageIconDemandAuthority final {
    std::wstring widgetId;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
    std::wstring packageContentDigest;
    std::wstring assetId;
    std::wstring sourceSha256;
    std::wstring normalizedSha256;
    UINT32 physicalWidth{};
    UINT32 physicalHeight{};
    PackageIconRasterVariant rasterVariant{PackageIconRasterVariant::OriginalColor};

    friend bool operator==(
        const PackageIconDemandAuthority&,
        const PackageIconDemandAuthority&) = default;
};

enum class PackageIconRequestDisposition {
    Resolved,
    OriginRetired,
    TerminalFailure,
};

struct PackageIconRequest final {
    PackageIconRequestDisposition disposition{
        PackageIconRequestDisposition::TerminalFailure};
    std::vector<std::uint8_t> normalizedSvg;
};

enum class TrustedArtworkRequestDisposition {
    Accepted,
    OriginRetired,
    TerminalFailure,
};

struct RemoteImageLimits {
    ImageDecodeSize decodeSize{};
    // Entry metadata and ready decoded pixels have independent bounds from
    // pending fetch/decode admission. A full pending queue must not evict a
    // useful ready image merely to track another request.
    std::size_t maximumEntries{320};
    std::size_t maximumReadyEntries{256};
    std::size_t maximumPendingEntries{32};
    // Each untrusted decoded image remains capped independently from the
    // bounded process-wide retention budget.
    std::size_t maximumDecodedImageBytes{64U * 1024U * 1024U};
    std::size_t maximumDecodedBytes{192U * 1024U * 1024U};
    std::size_t maximumDownloadBytes{5U * 1024U * 1024U};
    std::size_t maximumEncodedArtworkBytes{8U * 1024U * 1024U};
    std::size_t maximumEncodedArtworkBytesPerWidget{32U * 1024U * 1024U};
    std::size_t maximumEncodedArtworkBytesTotal{64U * 1024U * 1024U};
    std::uint64_t maximumArtworkPixels{16'777'216};
    UINT32 maximumArtworkDimension{4'096};
    DWORD maximumArtworkDecodeMilliseconds{2'000};
    std::size_t maximumArtworkDecoderRestarts{3};
    DWORD artworkDecoderRestartWindowMilliseconds{60'000};
    DWORD artworkDecoderCircuitBreakerMilliseconds{30'000};
    DWORD artworkDecoderShutdownMilliseconds{1'000};
    DWORD resolveTimeoutMilliseconds{2'000};
    DWORD connectTimeoutMilliseconds{3'000};
    DWORD sendTimeoutMilliseconds{3'000};
    DWORD receiveTimeoutMilliseconds{5'000};
    DWORD maximumRedirects{3};
};

struct RemoteDecodedImage {
    UINT32 width{};
    UINT32 height{};
    UINT32 stride{};
    std::vector<std::uint8_t> premultipliedBgra;
    std::wstring mimeType;
};

struct RemoteImageFetchResult {
    HRESULT result{E_FAIL};
    RemoteDecodedImage image;
    std::wstring error;

    [[nodiscard]] bool succeeded() const noexcept { return SUCCEEDED(result); }
};

enum class TrustedArtworkContentType {
    Unknown,
    Jpeg,
    Png,
    WebP,
};

struct TrustedArtworkDecodeDiagnostic final {
    std::uint64_t resourceHash{};
    std::uint64_t handleHash{};
    TrustedArtworkContentType contentType{TrustedArtworkContentType::Unknown};
    UINT32 width{};
    UINT32 height{};
    bool hasVisibleAlpha{};
};

struct RemoteImageCacheStats {
    std::size_t entries{};
    std::size_t decodedBytes{};
    std::size_t queuedOrLoading{};
    std::size_t readyEntries{};
    std::size_t failedEntries{};
    std::size_t httpsEntries{};
    std::size_t inlineEntries{};
    std::size_t trustedArtworkEntries{};
    std::uint64_t evictions{};
    std::uint64_t countPressureEvictions{};
    std::uint64_t bytePressureEvictions{};
    std::uint64_t supersededEntries{};
    std::uint64_t countCapacityRejections{};
    std::uint64_t pendingCapacityRejections{};
    std::size_t encodedArtworkBytes{};
    std::uint64_t trustedArtworkRequests{};
    std::uint64_t trustedArtworkHits{};
    std::uint64_t trustedArtworkSupplies{};
    std::uint64_t staleArtworkCompletions{};
    std::uint64_t readySourcePixels{};
    UINT32 maximumSourceWidth{};
    UINT32 maximumSourceHeight{};
};

/// Thread-safe CPU image cache. Network and legacy image decode execute on its
/// worker; admitted trusted artwork decode is isolated in its private process.
/// Completion runs on that worker thread, so UI users should PostMessage from
/// the callback and call CreateBitmap only on their render thread.
class RemoteImageCache final {
public:
    /// Receives the canonical cache key whose visible state changed. Trusted
    /// artwork failures publish one representative full key for the matching
    /// widget/opaque-handle transition, even when several nodes were pending.
    using CompletionCallback =
        std::function<void(std::wstring_view url, RemoteImageState state)>;
    using FetchFunction = std::function<RemoteImageFetchResult(
        std::wstring_view url,
        std::stop_token stopToken,
        const RemoteImageLimits& limits)>;
    using ArtworkRequestFunction = std::function<TrustedArtworkRequestDisposition(
        std::wstring_view key,
        const TrustedArtworkDemandAuthority& authority,
        std::stop_token stopToken)>;
    using ArtworkDecodeDiagnosticCallback =
        std::function<void(const TrustedArtworkDecodeDiagnostic& diagnostic)>;
    using PackageIconRequestFunction = std::function<PackageIconRequest(
            const PackageIconDemandAuthority& authority,
            std::stop_token stopToken)>;

    explicit RemoteImageCache(
        RemoteImageLimits limits = {},
        CompletionCallback completion = {},
        FetchFunction fetch = {},
        ArtworkRequestFunction artworkRequest = {},
        ArtworkDecodeDiagnosticCallback artworkDecodeDiagnostic = {},
        PackageIconRequestFunction packageIconRequest = {});
    ~RemoteImageCache();

    RemoteImageCache(const RemoteImageCache&) = delete;
    RemoteImageCache& operator=(const RemoteImageCache&) = delete;

    [[nodiscard]] RemoteImageRequestResult Request(std::wstring url, ImageDecodeSize size = {});
    /// Queues a host-created opaque artwork cache key. Snapshot image sources
    /// cannot enter this path; only the renderer constructs these keys from a
    /// validated protocol-v14 artwork handle and current widget ID.
    [[nodiscard]] RemoteImageRequestResult RequestTrustedArtwork(std::wstring key);
    [[nodiscard]] RemoteImageRequestResult RequestTrustedArtwork(
        std::wstring key,
        TrustedArtworkDemandAuthority authority, ImageDecodeSize size = {});
    [[nodiscard]] RemoteImageRequestResult RequestPackageIcon(
        std::wstring key,
        PackageIconDemandAuthority authority);
    /// Supplies a correlated host-only completion for a previously requested
    /// trusted artwork key. Late, evicted, or retired keys are ignored.
    [[nodiscard]] bool SupplyTrustedArtwork(
        std::wstring_view widgetId,
        std::wstring_view artworkHandle,
        std::wstring contentType,
        std::wstring contentBase64);
    [[nodiscard]] bool SupplyTrustedArtwork(
        std::wstring_view widgetId,
        std::wstring_view artworkHandle,
        const TrustedArtworkDemandAuthority& authority,
        std::wstring contentType,
        std::wstring contentBase64);
    [[nodiscard]] bool FailTrustedArtwork(
        std::wstring_view widgetId,
        std::wstring_view artworkHandle,
        const TrustedArtworkDemandAuthority& authority = {});
    [[nodiscard]] bool RetireTrustedArtworkDemand(
        std::wstring_view widgetId,
        std::wstring_view artworkHandle,
        const TrustedArtworkDemandAuthority& authority);
    [[nodiscard]] RemoteImageRequestResult Retry(std::wstring url);
    [[nodiscard]] RemoteImageState GetState(std::wstring_view url) const;
    [[nodiscard]] RemoteImageState GetPackageIconState(
        std::wstring_view key,
        const PackageIconDemandAuthority& authority) const;
    [[nodiscard]] RemoteImageState GetTrustedArtworkState(
        std::wstring_view key,
        const TrustedArtworkDemandAuthority& authority) const;
    [[nodiscard]] std::wstring GetError(std::wstring_view url) const;
    [[nodiscard]] RemoteImageCacheStats GetStats() const;
    /// Returns one immutable, fully decoded cache entry without creating a
    /// render-target resource. Launcher presentation uses this only after the
    /// ordinary trusted-artwork request path has completed, so a background
    /// swap never exposes a partial decode or another image lifetime owner.
    [[nodiscard]] std::shared_ptr<const RemoteDecodedImage> GetReadyImage(
        std::wstring_view key);

    /// Canonical host-only key used by the ordinary renderer. Widget snapshots
    /// cannot author this namespace directly.
    [[nodiscard]] static std::wstring TrustedArtworkKey(
        std::wstring_view widgetId,
        std::wstring_view nodeId,
        std::wstring_view artworkHandle, ImageDecodeSize size = {});
    [[nodiscard]] static std::wstring PackageIconKey(
        const PackageIconDemandAuthority& authority);
    [[nodiscard]] static std::uint64_t OpaqueDiagnosticHash(
        std::wstring_view value) noexcept;

    /// Creates a render-target-owned bitmap from a ready CPU cache entry.
    /// Returns E_PENDING while queued/loading and HRESULT_FROM_WIN32(ERROR_NOT_FOUND)
    /// for missing/failed entries.
    [[nodiscard]] HRESULT CreateBitmap(
        ID2D1RenderTarget* renderTarget,
        std::wstring_view url,
        ID2D1Bitmap** bitmap);

    static std::wstring VariantKey(std::wstring_view source, ImageDecodeSize size);
    void ProtectImages(const void* owner, std::set<std::wstring> keys);
    void ReleaseImageProtection(const void* owner);
    bool CanPrefetch(std::wstring_view key) const;
    bool BudgetRejected(std::wstring_view key) const;
    bool ReleaseBudgetRejection(std::wstring_view key);
    void Clear();
    void Shutdown() noexcept;

    /// Public deterministic policy seam for native tests and callers.
    [[nodiscard]] static bool IsAllowedHttpsUrl(std::wstring_view url) noexcept;
    /// HTTPS or a strictly bounded canonical PNG data source. Inline pixels
    /// are decoded locally and never reach WinHTTP.
    [[nodiscard]] static bool IsAllowedImageSource(std::wstring_view source) noexcept;
    [[nodiscard]] static RemoteImageFetchResult FetchAndDecodeSource(
        std::wstring_view source,
        std::stop_token stopToken,
        const RemoteImageLimits& limits);

private:
    enum class EvictionReason {
        CountPressure,
        BytePressure,
    };

    struct Entry {
        RemoteImageState state{RemoteImageState::Queued};
        std::shared_ptr<const RemoteDecodedImage> image;
        std::wstring error;
        std::wstring pendingSource;
        std::vector<std::uint8_t> pendingBytes;
        std::wstring pendingMimeType;
        std::uint64_t lastUse{};
        std::optional<TrustedArtworkDemandAuthority> demandAuthority;
        std::uint64_t demandGeneration{};
        std::optional<PackageIconDemandAuthority> packageIconAuthority;
        bool packageIconQueued{};
        ImageDecodeSize decodeSize{};
        bool budgetRejected{};
        std::size_t rejectedBytes{};
    };

    struct ArtworkDemand final {
        std::wstring key;
        TrustedArtworkDemandAuthority authority;
        std::uint64_t generation{};
    };

    [[nodiscard]] RemoteImageRequestResult QueueLocked(std::wstring url, bool retry);
    [[nodiscard]] std::size_t PendingCountLocked() const noexcept;
    [[nodiscard]] std::size_t ReadyCountLocked() const noexcept;
    [[nodiscard]] bool EvictOneLocked(
        std::wstring_view protectedUrl,
        EvictionReason reason,
        bool readyOnly = false);
    void WorkerLoop(std::stop_token stopToken);
    void ArtworkDemandLoop(std::stop_token stopToken);
    void CompleteArtworkDemand(
        const ArtworkDemand& demand,
        TrustedArtworkRequestDisposition disposition);
    void CompleteLocked(const std::wstring& url, RemoteImageFetchResult result);
    bool ProtectedLocked(std::wstring_view key) const;
    std::map<const void*, std::set<std::wstring>> protectedImages_;
    RemoteImageLimits limits_;
    CompletionCallback completion_;
    bool usesCustomFetch_{};
    FetchFunction fetch_;
    ArtworkRequestFunction artworkRequest_;
    ArtworkDecodeDiagnosticCallback artworkDecodeDiagnostic_;
    PackageIconRequestFunction packageIconRequest_;
    std::unique_ptr<ArtworkDecoderProcessOwner> artworkDecoder_;
    mutable std::mutex mutex_;
    std::condition_variable condition_;
    std::condition_variable artworkDemandCondition_;
    std::unordered_map<std::wstring, Entry> entries_;
    std::deque<ArtworkDemand> artworkDemandQueue_;
    std::deque<std::wstring> queue_;
    std::jthread artworkDemandWorker_;
    std::jthread worker_;
    std::size_t decodedBytes_{};
    std::size_t encodedArtworkBytes_{};
    std::uint64_t useCounter_{};
    std::uint64_t artworkDemandGeneration_{};
    std::uint64_t evictions_{};
    std::uint64_t countPressureEvictions_{};
    std::uint64_t bytePressureEvictions_{};
    std::uint64_t supersededEntries_{};
    std::uint64_t countCapacityRejections_{};
    std::uint64_t pendingCapacityRejections_{};
    static constexpr std::size_t maximumArtworkDiagnosticRecords_{64};
    std::array<std::uint64_t, maximumArtworkDiagnosticRecords_>
        artworkDiagnosticKeys_{};
    std::size_t artworkDiagnosticKeyCount_{};
    std::uint64_t trustedArtworkRequests_{};
    std::uint64_t trustedArtworkHits_{};
    std::uint64_t trustedArtworkSupplies_{};
    std::uint64_t staleArtworkCompletions_{};
    bool shuttingDown_{};
};

} // namespace widgetrail
