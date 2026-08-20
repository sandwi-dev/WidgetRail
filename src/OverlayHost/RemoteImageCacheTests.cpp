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

    const RemoteImageLimits defaultLimits;
    assert(defaultLimits.maximumEntries == 320);
    assert(defaultLimits.maximumReadyEntries == 256);
    assert(defaultLimits.maximumPendingEntries == 32);
    assert(defaultLimits.maximumDecodedImageBytes == 32U * 1024U * 1024U);
    assert(defaultLimits.maximumDecodedBytes == 96U * 1024U * 1024U);

    {
        std::mutex pendingMutex;
        std::condition_variable pendingChanged;
        bool fetchEntered = false;
        bool releaseFetch = false;
        bool fetchCompleted = false;
        RemoteImageLimits pendingLimits;
        pendingLimits.maximumEntries = 4;
        pendingLimits.maximumReadyEntries = 3;
        pendingLimits.maximumPendingEntries = 1;
        pendingLimits.maximumDecodedImageBytes = 4;
        pendingLimits.maximumDecodedBytes = 12;
        RemoteImageCache pendingCache(
            pendingLimits,
            [&](std::wstring_view, RemoteImageState state) {
                assert(state == RemoteImageState::Ready);
                {
                    std::scoped_lock lock(pendingMutex);
                    fetchCompleted = true;
                }
                pendingChanged.notify_all();
            },
            [&](std::wstring_view, std::stop_token, const RemoteImageLimits&) {
                {
                    std::unique_lock lock(pendingMutex);
                    fetchEntered = true;
                    pendingChanged.notify_all();
                    pendingChanged.wait(lock, [&] { return releaseFetch; });
                }
                RemoteDecodedImage image;
                image.width = 1;
                image.height = 1;
                image.stride = 4;
                image.premultipliedBgra = {0x10, 0x20, 0x30, 0xFF};
                return RemoteImageFetchResult{S_OK, std::move(image), {}};
            });
        assert(pendingCache.Request(L"https://example.test/pending-a") ==
               RemoteImageRequestResult::Queued);
        {
            std::unique_lock lock(pendingMutex);
            assert(pendingChanged.wait_for(
                lock, std::chrono::seconds(2), [&] { return fetchEntered; }));
        }
        assert(pendingCache.Request(L"https://example.test/pending-b") ==
               RemoteImageRequestResult::CapacityExceeded);
        auto pendingStats = pendingCache.GetStats();
        assert(pendingStats.entries == 1);
        assert(pendingStats.queuedOrLoading == 1);
        assert(pendingStats.pendingCapacityRejections == 1);
        {
            std::scoped_lock lock(pendingMutex);
            releaseFetch = true;
        }
        pendingChanged.notify_all();
        {
            std::unique_lock lock(pendingMutex);
            assert(pendingChanged.wait_for(
                lock, std::chrono::seconds(2), [&] { return fetchCompleted; }));
        }
        assert(pendingCache.GetStats().readyEntries == 1);
        pendingCache.Shutdown();
    }

    {
        std::mutex pressureMutex;
        std::condition_variable pressureChanged;
        int completions = 0;
        auto fetchPixel = [](std::wstring_view, std::stop_token,
                             const RemoteImageLimits&) {
            RemoteDecodedImage image;
            image.width = 1;
            image.height = 1;
            image.stride = 4;
            image.premultipliedBgra = {0x10, 0x20, 0x30, 0xFF};
            return RemoteImageFetchResult{S_OK, std::move(image), {}};
        };
        const auto awaitCompletion = [&](const int expected) {
            std::unique_lock lock(pressureMutex);
            assert(pressureChanged.wait_for(
                lock, std::chrono::seconds(2), [&] { return completions == expected; }));
        };

        RemoteImageLimits countLimits;
        countLimits.maximumEntries = 4;
        countLimits.maximumReadyEntries = 2;
        countLimits.maximumPendingEntries = 1;
        countLimits.maximumDecodedImageBytes = 4;
        countLimits.maximumDecodedBytes = 12;
        RemoteImageCache countCache(
            countLimits,
            [&](std::wstring_view, RemoteImageState state) {
                assert(state == RemoteImageState::Ready);
                {
                    std::scoped_lock lock(pressureMutex);
                    ++completions;
                }
                pressureChanged.notify_all();
            },
            fetchPixel);
        assert(countCache.Request(L"https://example.test/count-a") ==
               RemoteImageRequestResult::Queued);
        awaitCompletion(1);
        assert(countCache.Request(L"https://example.test/count-b") ==
               RemoteImageRequestResult::Queued);
        awaitCompletion(2);
        assert(countCache.GetReadyImage(L"https://example.test/count-a"));
        assert(countCache.Request(L"https://example.test/count-c") ==
               RemoteImageRequestResult::Queued);
        awaitCompletion(3);
        const auto countStats = countCache.GetStats();
        assert(countStats.entries == 2);
        assert(countStats.readyEntries == 2);
        assert(countStats.countPressureEvictions == 1);
        assert(countStats.bytePressureEvictions == 0);
        assert(countCache.GetState(L"https://example.test/count-a") ==
               RemoteImageState::Ready);
        assert(countCache.GetState(L"https://example.test/count-b") ==
               RemoteImageState::Missing);
        assert(countCache.GetState(L"https://example.test/count-c") ==
               RemoteImageState::Ready);
        countCache.Shutdown();

        completions = 0;
        RemoteImageLimits byteLimits;
        byteLimits.maximumEntries = 4;
        byteLimits.maximumReadyEntries = 4;
        byteLimits.maximumPendingEntries = 1;
        byteLimits.maximumDecodedImageBytes = 4;
        byteLimits.maximumDecodedBytes = 8;
        RemoteImageCache byteCache(
            byteLimits,
            [&](std::wstring_view, RemoteImageState state) {
                assert(state == RemoteImageState::Ready);
                {
                    std::scoped_lock lock(pressureMutex);
                    ++completions;
                }
                pressureChanged.notify_all();
            },
            fetchPixel);
        assert(byteCache.Request(L"https://example.test/byte-a") ==
               RemoteImageRequestResult::Queued);
        awaitCompletion(1);
        assert(byteCache.Request(L"https://example.test/byte-b") ==
               RemoteImageRequestResult::Queued);
        awaitCompletion(2);
        assert(byteCache.GetReadyImage(L"https://example.test/byte-a"));
        assert(byteCache.Request(L"https://example.test/byte-c") ==
               RemoteImageRequestResult::Queued);
        awaitCompletion(3);
        const auto byteStats = byteCache.GetStats();
        assert(byteStats.entries == 2);
        assert(byteStats.decodedBytes == 8);
        assert(byteStats.countPressureEvictions == 0);
        assert(byteStats.bytePressureEvictions == 1);
        assert(byteCache.GetState(L"https://example.test/byte-a") ==
               RemoteImageState::Ready);
        assert(byteCache.GetState(L"https://example.test/byte-b") ==
               RemoteImageState::Missing);
        assert(byteCache.GetState(L"https://example.test/byte-c") ==
               RemoteImageState::Ready);
        byteCache.Shutdown();
    }

    {
        std::mutex limitMutex;
        std::condition_variable limitChanged;
        bool completed = false;
        RemoteImageLimits perImageLimits;
        perImageLimits.maximumEntries = 2;
        perImageLimits.maximumReadyEntries = 2;
        perImageLimits.maximumPendingEntries = 1;
        perImageLimits.maximumDecodedImageBytes = 4;
        perImageLimits.maximumDecodedBytes = 16;
        RemoteImageCache perImageCache(
            perImageLimits,
            [&](std::wstring_view, RemoteImageState state) {
                assert(state == RemoteImageState::Failed);
                {
                    std::scoped_lock lock(limitMutex);
                    completed = true;
                }
                limitChanged.notify_all();
            },
            [](std::wstring_view, std::stop_token, const RemoteImageLimits&) {
                RemoteDecodedImage image;
                image.width = 2;
                image.height = 1;
                image.stride = 8;
                image.premultipliedBgra = {
                    0x10, 0x20, 0x30, 0xFF, 0x40, 0x50, 0x60, 0xFF};
                return RemoteImageFetchResult{S_OK, std::move(image), {}};
            });
        assert(perImageCache.Request(L"https://example.test/too-large") ==
               RemoteImageRequestResult::Queued);
        {
            std::unique_lock lock(limitMutex);
            assert(limitChanged.wait_for(
                lock, std::chrono::seconds(2), [&] { return completed; }));
        }
        const auto stats = perImageCache.GetStats();
        assert(stats.failedEntries == 1);
        assert(stats.readyEntries == 0);
        assert(stats.decodedBytes == 0);
        assert(stats.bytePressureEvictions == 0);
        perImageCache.Shutdown();
    }

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
