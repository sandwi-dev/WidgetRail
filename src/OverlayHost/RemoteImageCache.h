#pragma once

#include <Windows.h>
#include <d2d1.h>
#include <wrl/client.h>

#include <condition_variable>
#include <cstddef>
#include <cstdint>
#include <deque>
#include <functional>
#include <memory>
#include <mutex>
#include <stop_token>
#include <string>
#include <string_view>
#include <thread>
#include <unordered_map>
#include <vector>

namespace gba {

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

struct RemoteImageLimits {
    std::size_t maximumEntries{32};
    std::size_t maximumDecodedBytes{32U * 1024U * 1024U};
    std::size_t maximumDownloadBytes{5U * 1024U * 1024U};
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

struct RemoteImageCacheStats {
    std::size_t entries{};
    std::size_t decodedBytes{};
    std::size_t queuedOrLoading{};
};

/// Thread-safe CPU image cache. Network and WIC decode execute on its worker.
/// Completion runs on that worker thread, so UI users should PostMessage from
/// the callback and call CreateBitmap only on their render thread.
class RemoteImageCache final {
public:
    using CompletionCallback =
        std::function<void(std::wstring_view url, RemoteImageState state)>;
    using FetchFunction = std::function<RemoteImageFetchResult(
        std::wstring_view url,
        std::stop_token stopToken,
        const RemoteImageLimits& limits)>;

    explicit RemoteImageCache(
        RemoteImageLimits limits = {},
        CompletionCallback completion = {},
        FetchFunction fetch = {});
    ~RemoteImageCache();

    RemoteImageCache(const RemoteImageCache&) = delete;
    RemoteImageCache& operator=(const RemoteImageCache&) = delete;

    [[nodiscard]] RemoteImageRequestResult Request(std::wstring url);
    [[nodiscard]] RemoteImageRequestResult Retry(std::wstring url);
    [[nodiscard]] RemoteImageState GetState(std::wstring_view url) const;
    [[nodiscard]] std::wstring GetError(std::wstring_view url) const;
    [[nodiscard]] RemoteImageCacheStats GetStats() const;

    /// Creates a render-target-owned bitmap from a ready CPU cache entry.
    /// Returns E_PENDING while queued/loading and HRESULT_FROM_WIN32(ERROR_NOT_FOUND)
    /// for missing/failed entries.
    [[nodiscard]] HRESULT CreateBitmap(
        ID2D1RenderTarget* renderTarget,
        std::wstring_view url,
        ID2D1Bitmap** bitmap);

    void Clear();
    void Shutdown() noexcept;

    /// Public deterministic policy seam for native tests and callers.
    [[nodiscard]] static bool IsAllowedHttpsUrl(std::wstring_view url) noexcept;
    /// HTTPS or a strictly bounded canonical PNG data source. Inline pixels
    /// are decoded locally and never reach WinHTTP.
    [[nodiscard]] static bool IsAllowedImageSource(std::wstring_view source) noexcept;

private:
    struct Entry {
        RemoteImageState state{RemoteImageState::Queued};
        std::shared_ptr<const RemoteDecodedImage> image;
        std::wstring error;
        std::uint64_t lastUse{};
    };

    [[nodiscard]] RemoteImageRequestResult QueueLocked(std::wstring url, bool retry);
    [[nodiscard]] bool EvictOneLocked(std::wstring_view protectedUrl);
    void WorkerLoop(std::stop_token stopToken);
    void CompleteLocked(const std::wstring& url, RemoteImageFetchResult result);
    [[nodiscard]] static RemoteImageFetchResult FetchAndDecode(
        std::wstring_view url,
        std::stop_token stopToken,
        const RemoteImageLimits& limits);

    RemoteImageLimits limits_;
    CompletionCallback completion_;
    FetchFunction fetch_;
    mutable std::mutex mutex_;
    std::condition_variable condition_;
    std::unordered_map<std::wstring, Entry> entries_;
    std::deque<std::wstring> queue_;
    std::jthread worker_;
    std::size_t decodedBytes_{};
    std::uint64_t useCounter_{};
    bool shuttingDown_{};
};

} // namespace gba
