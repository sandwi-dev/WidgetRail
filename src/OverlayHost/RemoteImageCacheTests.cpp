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
    using namespace widgetrail;
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
    RemoteImageCache* artworkCacheOwner = nullptr;
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
        [](std::wstring_view source, std::stop_token, const RemoteImageLimits&) {
            assert(source.starts_with(L"data:image/png;base64,"));
            RemoteDecodedImage image;
            image.width = 1;
            image.height = 1;
            image.stride = 4;
            image.premultipliedBgra = {0x20, 0x30, 0x40, 0xFF};
            image.mimeType = L"image/png";
            return RemoteImageFetchResult{S_OK, std::move(image), {}};
        },
        [&](std::wstring_view key) {
            assert(key.starts_with(L"wrail-artwork\x1f"));
            constexpr std::wstring_view prefix = L"wrail-artwork\x1f";
            const auto widgetEnd = key.find(L'\x1f', prefix.size());
            const auto handleStart = key.rfind(L'\x1f');
            return artworkCacheOwner->SupplyTrustedArtwork(
                key.substr(prefix.size(), widgetEnd - prefix.size()),
                key.substr(handleStart + 1), L"AAAA");
        });
    artworkCacheOwner = &artworkCache;
    assert(artworkCache.RequestTrustedArtwork(L"https://example.test/not-trusted") ==
           RemoteImageRequestResult::InvalidUrl);
    constexpr int largeCollectionItems = 10'000;
    for (int base = 0; base < largeCollectionItems; base += 32) {
        const int batch = std::min(32, largeCollectionItems - base);
        for (int index = 0; index < batch; ++index) {
            auto suffix = std::to_wstring(base + index);
            suffix.insert(suffix.begin(), 32 - suffix.size(), L'0');
            const auto key = L"wrail-artwork\x1fgames-apps\x1frow-" +
                std::to_wstring(base + index) + L"\x1flibrary.art." + suffix;
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
    const auto oldRevision =
        L"wrail-artwork\x1fgames-apps\x1frow-revision\x1f"
        L"library.art.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    const auto newRevision =
        L"wrail-artwork\x1fgames-apps\x1frow-revision\x1f"
        L"library.art.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    assert(artworkCache.RequestTrustedArtwork(oldRevision) ==
           RemoteImageRequestResult::Queued);
    {
        std::unique_lock lock(artworkMutex);
        assert(artworkCompleted.wait_for(
            lock, std::chrono::seconds(2),
            [&] { return artworkFetches == largeCollectionItems + 1; }));
    }
    assert(artworkCache.GetState(oldRevision) == RemoteImageState::Ready);
    assert(artworkCache.RequestTrustedArtwork(newRevision) ==
           RemoteImageRequestResult::Queued);
    assert(artworkCache.GetState(oldRevision) == RemoteImageState::Missing);
    {
        std::unique_lock lock(artworkMutex);
        assert(artworkCompleted.wait_for(
            lock, std::chrono::seconds(2),
            [&] { return artworkFetches == largeCollectionItems + 2; }));
    }
    assert(artworkCache.GetState(newRevision) == RemoteImageState::Ready);
    artworkCache.Shutdown();

    std::vector<std::pair<std::wstring, RemoteImageState>> artworkTransitions;
    RemoteImageLimits failureLimits;
    failureLimits.maximumEntries = 2;
    failureLimits.maximumDecodedBytes = 8;
    std::mutex failureMutex;
    std::condition_variable failureCompleted;
    RemoteImageCache failureCache(
        failureLimits,
        [&](const std::wstring_view key, const RemoteImageState state) {
            {
                std::scoped_lock lock(failureMutex);
                artworkTransitions.emplace_back(key, state);
            }
            failureCompleted.notify_all();
        },
        [](std::wstring_view, std::stop_token, const RemoteImageLimits&) {
            RemoteDecodedImage image;
            image.width = 1;
            image.height = 1;
            image.stride = 4;
            image.premultipliedBgra = {0xA0, 0x20, 0x10, 0xFF};
            image.mimeType = L"image/png";
            return RemoteImageFetchResult{S_OK, std::move(image), {}};
        },
        [](std::wstring_view) { return true; });
    const auto failedRevision =
        L"wrail-artwork\x1fgame-launcher\x1ftile.artwork\x1f"
        L"library.art.11111111111111111111111111111111";
    const auto recoveredRevision =
        L"wrail-artwork\x1fgame-launcher\x1ftile.artwork\x1f"
        L"library.art.22222222222222222222222222222222";
    assert(failureCache.RequestTrustedArtwork(failedRevision) ==
           RemoteImageRequestResult::Queued);
    assert(failureCache.GetState(failedRevision) == RemoteImageState::Loading);
    assert(failureCache.FailTrustedArtwork(
        L"game-launcher", L"library.art.11111111111111111111111111111111"));
    assert(failureCache.GetState(failedRevision) == RemoteImageState::Failed);
    assert(!failureCache.FailTrustedArtwork(
        L"game-launcher", L"library.art.11111111111111111111111111111111"));
    assert(!failureCache.SupplyTrustedArtwork(
        L"game-launcher", L"library.art.11111111111111111111111111111111", L"AAAA"));
    const auto sharedFailedRevision =
        L"wrail-artwork\x1fgame-launcher\x1fsecond-tile.artwork\x1f"
        L"library.art.11111111111111111111111111111111";
    assert(failureCache.RequestTrustedArtwork(sharedFailedRevision) ==
           RemoteImageRequestResult::AlreadyTracked);
    assert(failureCache.GetState(sharedFailedRevision) == RemoteImageState::Failed);
    {
        std::scoped_lock lock(failureMutex);
        assert(artworkTransitions.size() == 1);
        assert(artworkTransitions.front().first == failedRevision);
        assert(artworkTransitions.front().second == RemoteImageState::Failed);
    }

    assert(failureCache.RequestTrustedArtwork(recoveredRevision) ==
           RemoteImageRequestResult::Queued);
    assert(failureCache.GetState(failedRevision) == RemoteImageState::Missing);
    assert(!failureCache.SupplyTrustedArtwork(
        L"game-launcher", L"library.art.11111111111111111111111111111111", L"AAAA"));
    assert(failureCache.SupplyTrustedArtwork(
        L"game-launcher", L"library.art.22222222222222222222222222222222", L"AAAA"));
    {
        std::unique_lock lock(failureMutex);
        assert(failureCompleted.wait_for(lock, std::chrono::seconds(2), [&] {
            return artworkTransitions.size() == 2;
        }));
        assert(artworkTransitions.back().first == recoveredRevision);
        assert(artworkTransitions.back().second == RemoteImageState::Ready);
    }
    assert(failureCache.GetState(recoveredRevision) == RemoteImageState::Ready);

    failureCache.Clear();
    assert(failureCache.GetState(recoveredRevision) == RemoteImageState::Missing);
    assert(failureCache.RequestTrustedArtwork(failedRevision) ==
           RemoteImageRequestResult::Queued);
    assert(failureCache.FailTrustedArtwork(
        L"game-launcher", L"library.art.11111111111111111111111111111111"));
    {
        std::scoped_lock lock(failureMutex);
        assert(artworkTransitions.size() == 3);
        assert(artworkTransitions.back().first == failedRevision);
        assert(artworkTransitions.back().second == RemoteImageState::Failed);
    }
    assert(failureCache.GetStats().entries <= failureLimits.maximumEntries);
    failureCache.Shutdown();
    std::cout << "RemoteImageCacheTests passed\n";
}
