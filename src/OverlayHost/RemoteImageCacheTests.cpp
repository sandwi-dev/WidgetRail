#include "RemoteImageCache.h"

#include <algorithm>
#ifdef NDEBUG
#undef NDEBUG
#endif
#include <cassert>
#include <chrono>
#include <condition_variable>
#include <iostream>
#include <mutex>
#include <string>

int main() {
    using namespace gba;
    assert(RemoteImageCache::IsAllowedHttpsUrl(L"https://example.test/image.png"));
    assert(!RemoteImageCache::IsAllowedHttpsUrl(L"http://example.test/image.png"));
    assert(!RemoteImageCache::IsAllowedHttpsUrl(L"https://user:secret@example.test/image.png"));
    constexpr auto inlinePng =
        L"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ"
        L"AAAADUlEQVR42mP8z8BQDwAFgwJ/lK3Q7wAAAABJRU5ErkJggg==";
    (void)inlinePng;
    assert(RemoteImageCache::IsAllowedImageSource(inlinePng));
    assert(RemoteImageCache::IsAllowedImageSource(L"https://example.test/image.png"));
    assert(!RemoteImageCache::IsAllowedImageSource(L"data:image/png;base64,not-base64"));
    assert(!RemoteImageCache::IsAllowedImageSource(L"data:image/svg+xml;base64,PHN2Zz4="));

    std::mutex mutex;
    std::condition_variable completed;
    int completionCount = 0;
    RemoteImageLimits limits;
    limits.maximumEntries = 2;
    limits.maximumDecodedBytes = 8;
    RemoteImageCache cache(
        limits,
        [&](std::wstring_view, RemoteImageState state) {
            (void)state;
            assert(state == RemoteImageState::Ready);
            {
                std::scoped_lock lock(mutex);
                ++completionCount;
            }
            completed.notify_one();
        },
        [](std::wstring_view, std::stop_token, const RemoteImageLimits&) {
            RemoteDecodedImage image;
            image.width = 1;
            image.height = 1;
            image.stride = 4;
            image.premultipliedBgra = {0x10, 0x20, 0x30, 0xFF};
            image.mimeType = L"image/fake";
            return RemoteImageFetchResult{S_OK, std::move(image), {}};
        });

    assert(cache.Request(L"http://example.test/a") == RemoteImageRequestResult::InvalidUrl);
    assert(cache.Request(L"https://example.test/a") == RemoteImageRequestResult::Queued);
    assert(cache.Request(L"https://example.test/a") == RemoteImageRequestResult::AlreadyTracked);
    {
        std::unique_lock lock(mutex);
        assert(completed.wait_for(lock, std::chrono::seconds(2), [&] { return completionCount == 1; }));
    }
    assert(cache.GetState(L"https://example.test/a") == RemoteImageState::Ready);
    assert(cache.GetStats().decodedBytes == 4);

    assert(cache.Request(L"https://example.test/b") == RemoteImageRequestResult::Queued);
    {
        std::unique_lock lock(mutex);
        assert(completed.wait_for(lock, std::chrono::seconds(2), [&] { return completionCount == 2; }));
    }
    assert(cache.GetStats().entries == 2);
    assert(cache.GetStats().decodedBytes == 8);
    assert(cache.Request(inlinePng) == RemoteImageRequestResult::Queued);
    {
        std::unique_lock lock(mutex);
        assert(completed.wait_for(lock, std::chrono::seconds(2), [&] { return completionCount == 3; }));
    }
    assert(cache.GetState(inlinePng) == RemoteImageState::Ready);
    cache.Shutdown();
    assert(cache.Request(L"https://example.test/c") == RemoteImageRequestResult::ShuttingDown);

    std::mutex artworkMutex;
    std::condition_variable artworkCompleted;
    int artworkFetches = 0;
    RemoteImageLimits artworkLimits;
    artworkLimits.maximumEntries = 32;
    artworkLimits.maximumDecodedBytes = 32 * 4;
    RemoteImageCache artworkCache(
        artworkLimits,
        [&](std::wstring_view, RemoteImageState state) {
            assert(state == RemoteImageState::Ready);
            {
                std::scoped_lock lock(artworkMutex);
                ++artworkFetches;
            }
            artworkCompleted.notify_all();
        },
        [](std::wstring_view key, std::stop_token, const RemoteImageLimits&) {
            assert(key.starts_with(L"gbar-artwork\x1f"));
            RemoteDecodedImage image;
            image.width = 1;
            image.height = 1;
            image.stride = 4;
            image.premultipliedBgra = {0x20, 0x30, 0x40, 0xFF};
            image.mimeType = L"image/png";
            return RemoteImageFetchResult{S_OK, std::move(image), {}};
        });
    assert(artworkCache.RequestTrustedArtwork(L"https://example.test/not-trusted") ==
           RemoteImageRequestResult::InvalidUrl);
    constexpr int largeCollectionItems = 10'000;
    for (int base = 0; base < largeCollectionItems; base += 32) {
        const int batch = std::min(32, largeCollectionItems - base);
        for (int index = 0; index < batch; ++index) {
            auto suffix = std::to_wstring(base + index);
            suffix.insert(suffix.begin(), 32 - suffix.size(), L'0');
            const auto key = L"gbar-artwork\x1fgames-apps\x1flibrary.art." +
                suffix;
            assert(artworkCache.RequestTrustedArtwork(key) ==
                   RemoteImageRequestResult::Queued);
            assert(artworkCache.RequestTrustedArtwork(key) ==
                   RemoteImageRequestResult::AlreadyTracked);
        }
        std::unique_lock lock(artworkMutex);
        assert(artworkCompleted.wait_for(
            lock, std::chrono::seconds(2),
            [&] { return artworkFetches >= base + batch; }));
        const auto stats = artworkCache.GetStats();
        assert(stats.entries <= 32);
        assert(stats.decodedBytes <= 32 * 4);
    }
    assert(artworkFetches == largeCollectionItems);
    artworkCache.Shutdown();
    std::cout << "RemoteImageCacheTests passed\n";
}
