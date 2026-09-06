#include "RemoteImageCache.h"
#include "ArtworkDecoderProcessOwner.h"

#include <wincodec.h>

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
#include <thread>

namespace {

std::vector<std::uint8_t> EncodeWicImage(
    const GUID& containerFormat, const UINT width, const UINT height) {
    using Microsoft::WRL::ComPtr;
    ComPtr<IWICImagingFactory> factory;
    assert(SUCCEEDED(CoCreateInstance(
        CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(factory.ReleaseAndGetAddressOf()))));
    ComPtr<IStream> stream;
    assert(SUCCEEDED(CreateStreamOnHGlobal(nullptr, TRUE, stream.ReleaseAndGetAddressOf())));
    ComPtr<IWICBitmapEncoder> encoder;
    assert(SUCCEEDED(factory->CreateEncoder(
        containerFormat, nullptr, encoder.ReleaseAndGetAddressOf())));
    assert(SUCCEEDED(encoder->Initialize(stream.Get(), WICBitmapEncoderNoCache)));
    ComPtr<IWICBitmapFrameEncode> frame;
    assert(SUCCEEDED(encoder->CreateNewFrame(frame.ReleaseAndGetAddressOf(), nullptr)));
    assert(SUCCEEDED(frame->Initialize(nullptr)));
    assert(SUCCEEDED(frame->SetSize(width, height)));
    WICPixelFormatGUID pixelFormat = GUID_WICPixelFormat24bppBGR;
    assert(SUCCEEDED(frame->SetPixelFormat(&pixelFormat)));
    assert(IsEqualGUID(pixelFormat, GUID_WICPixelFormat24bppBGR));
    const UINT stride = width * 3U;
    std::vector<std::uint8_t> pixels(static_cast<std::size_t>(stride) * height);
    for (std::size_t offset = 0; offset < pixels.size(); offset += 3U) {
        pixels[offset] = 0xD0;
        pixels[offset + 1] = 0x60;
        pixels[offset + 2] = 0x20;
    }
    assert(SUCCEEDED(frame->WritePixels(
        height, stride, static_cast<UINT>(pixels.size()), pixels.data())));
    assert(SUCCEEDED(frame->Commit()));
    assert(SUCCEEDED(encoder->Commit()));
    STATSTG stat{};
    assert(SUCCEEDED(stream->Stat(&stat, STATFLAG_NONAME)));
    assert(stat.cbSize.QuadPart > 0 && stat.cbSize.QuadPart <= 8U * 1024U * 1024U);
    LARGE_INTEGER beginning{};
    assert(SUCCEEDED(stream->Seek(beginning, STREAM_SEEK_SET, nullptr)));
    std::vector<std::uint8_t> encoded(static_cast<std::size_t>(stat.cbSize.QuadPart));
    ULONG read = 0;
    assert(SUCCEEDED(stream->Read(encoded.data(), static_cast<ULONG>(encoded.size()), &read)));
    assert(read == encoded.size());
    return encoded;
}

std::wstring Base64(const std::vector<std::uint8_t>& bytes) {
    constexpr wchar_t alphabet[] =
        L"ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::wstring result;
    result.reserve(((bytes.size() + 2U) / 3U) * 4U);
    for (std::size_t index = 0; index < bytes.size(); index += 3U) {
        const auto remaining = bytes.size() - index;
        const std::uint32_t value = static_cast<std::uint32_t>(bytes[index]) << 16U |
            (remaining > 1U ? static_cast<std::uint32_t>(bytes[index + 1]) << 8U : 0U) |
            (remaining > 2U ? bytes[index + 2] : 0U);
        result.push_back(alphabet[(value >> 18U) & 0x3fU]);
        result.push_back(alphabet[(value >> 12U) & 0x3fU]);
        result.push_back(remaining > 1U ? alphabet[(value >> 6U) & 0x3fU] : L'=');
        result.push_back(remaining > 2U ? alphabet[value & 0x3fU] : L'=');
    }
    return result;
}

std::wstring ExecutableSibling(const wchar_t* const name) {
    std::wstring path(32'768, L'\0');
    const DWORD length = GetModuleFileNameW(
        nullptr, path.data(), static_cast<DWORD>(path.size()));
    assert(length > 0 && length < path.size());
    path.resize(length);
    const auto separator = path.find_last_of(L"\\/");
    assert(separator != std::wstring::npos);
    path.resize(separator + 1);
    path.append(name);
    return path;
}

std::vector<std::uint8_t> StillWebP() {
    return {
        0x52, 0x49, 0x46, 0x46, 0x1e, 0x00, 0x00, 0x00,
        0x57, 0x45, 0x42, 0x50, 0x56, 0x50, 0x38, 0x4c,
        0x11, 0x00, 0x00, 0x00, 0x2f, 0x01, 0x00, 0x00,
        0x00, 0x07, 0x50, 0x99, 0x66, 0x74, 0xa9, 0xff,
        0x81, 0x88, 0xe8, 0x7f, 0x00, 0x00,
    };
}

std::vector<std::uint8_t> AnimatedWebP() {
    return {
        0x52,0x49,0x46,0x46,0x88,0x00,0x00,0x00,0x57,0x45,0x42,0x50,
        0x56,0x50,0x38,0x58,0x0a,0x00,0x00,0x00,0x02,0x00,0x00,0x00,
        0x01,0x00,0x00,0x00,0x00,0x00,0x41,0x4e,0x49,0x4d,0x06,0x00,
        0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x41,0x4e,0x4d,0x46,
        0x2a,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x01,0x00,
        0x00,0x00,0x00,0x00,0x64,0x00,0x00,0x02,0x56,0x50,0x38,0x4c,
        0x11,0x00,0x00,0x00,0x2f,0x01,0x00,0x00,0x00,0x07,0x50,0x99,
        0x66,0x74,0xa9,0xff,0x81,0x88,0xe8,0x7f,0x00,0x00,0x41,0x4e,
        0x4d,0x46,0x2a,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
        0x01,0x00,0x00,0x00,0x00,0x00,0x64,0x00,0x00,0x00,0x56,0x50,
        0x38,0x4c,0x11,0x00,0x00,0x00,0x2f,0x01,0x00,0x00,0x00,0x07,
        0xd0,0xbe,0x92,0xd5,0xb2,0xff,0x81,0x88,0xe8,0x7f,0x00,0x00,
    };
}

} // namespace

int main() {
    using namespace widgetrail;
    assert(RemoteImageCache::IsAllowedHttpsUrl(L"https://example.test/image.png"));
    assert(!RemoteImageCache::IsAllowedHttpsUrl(L"http://example.test/image.png"));
    assert(!RemoteImageCache::IsAllowedHttpsUrl(L"https://user:secret@example.test/image.png"));
    constexpr auto inlinePng =
        L"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ"
        L"AAAADUlEQVR42mP8z8BQDwAFgwJ/lK3Q7wAAAABJRU5ErkJggg==";
    constexpr auto trustedPngBase64 =
        L"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ"
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
    assert(defaultLimits.maximumDecodedImageBytes == 64U * 1024U * 1024U);
    assert(defaultLimits.maximumDecodedBytes == 192U * 1024U * 1024U);
    assert(defaultLimits.maximumEncodedArtworkBytes == 8U * 1024U * 1024U);
    assert(defaultLimits.maximumArtworkDimension == 4'096);
    assert(defaultLimits.maximumArtworkPixels == 16'777'216);

    {
        const auto initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        assert(SUCCEEDED(initialized) || initialized == RPC_E_CHANGED_MODE);
        const auto png4096 = EncodeWicImage(GUID_ContainerFormatPng, 4'096, 1);
        const auto jpeg = EncodeWicImage(GUID_ContainerFormatJpeg, 1, 1);
        const auto png = EncodeWicImage(GUID_ContainerFormatPng, 2, 1);
        const auto stillWebP = StillWebP();
        const auto animatedWebP = AnimatedWebP();

        {
            RemoteImageLimits decoderLimits;
            decoderLimits.maximumArtworkDecodeMilliseconds = 100;
            decoderLimits.maximumArtworkDecoderRestarts = 2;
            decoderLimits.artworkDecoderRestartWindowMilliseconds = 5'000;
            decoderLimits.artworkDecoderCircuitBreakerMilliseconds = 5'000;
            decoderLimits.artworkDecoderShutdownMilliseconds = 250;
            ArtworkDecoderProcessOwner decoder(
                decoderLimits, ExecutableSibling(L"ArtworkDecoderTestHost.exe"));

            auto decodedJpeg = decoder.Decode(jpeg, L"image/jpeg", {});
            assert(SUCCEEDED(decodedJpeg.result));
            assert(decodedJpeg.image.width == 1 && decodedJpeg.image.height == 1);
            assert(decodedJpeg.image.mimeType == L"image/jpeg");
            auto decodedPng = decoder.Decode(png, L"image/png", {});
            assert(SUCCEEDED(decodedPng.result));
            assert(decodedPng.image.width == 2 && decodedPng.image.height == 1);
            assert(decodedPng.image.mimeType == L"image/png");
            auto decoderStats = decoder.Stats();
            assert(decoderStats.starts == 1);
            assert(decoderStats.completed == 2);

            auto decodedStillWebP = decoder.Decode(
                stillWebP, L"image/webp", {}, artworkdecoder::TestBehavior::Succeed);
            assert(SUCCEEDED(decodedStillWebP.result));
            assert(decodedStillWebP.image.width == 2 &&
                   decodedStillWebP.image.height == 1);
            assert(decodedStillWebP.image.mimeType == L"image/webp");
            auto decodedAnimatedWebP = decoder.Decode(
                animatedWebP, L"image/webp", {}, artworkdecoder::TestBehavior::Succeed);
            assert(SUCCEEDED(decodedAnimatedWebP.result));
            assert(decodedAnimatedWebP.image.width == 2 &&
                   decodedAnimatedWebP.image.height == 1);
            const auto missingCodec = decoder.Decode(
                stillWebP, L"image/webp", {}, artworkdecoder::TestBehavior::MissingCodec);
            assert(missingCodec.result == WINCODEC_ERR_COMPONENTNOTFOUND);
            assert(missingCodec.error == L"Trusted artwork WebP decoder is unavailable.");
            auto afterMissingCodec = decoder.Decode(png, L"image/png", {});
            assert(SUCCEEDED(afterMissingCodec.result));
            decoderStats = decoder.Stats();
            assert(decoderStats.starts == 1);
            assert(decoderStats.completed == 5);
            assert(decoderStats.failed == 1);

            const auto timeoutStarted = std::chrono::steady_clock::now();
            const auto timeout = decoder.Decode(
                png, L"image/png", {}, artworkdecoder::TestBehavior::Hang);
            assert(timeout.result == HRESULT_FROM_WIN32(ERROR_TIMEOUT));
            assert(std::chrono::steady_clock::now() - timeoutStarted <
                   std::chrono::seconds(1));
            auto recovered = decoder.Decode(png, L"image/png", {});
            assert(SUCCEEDED(recovered.result));
            decoderStats = decoder.Stats();
            assert(decoderStats.starts == 2);
            assert(decoderStats.timedOut == 1);
            assert(decoderStats.terminated == 1);

            const auto exited = decoder.Decode(
                png, L"image/png", {}, artworkdecoder::TestBehavior::Exit);
            assert(exited.result == HRESULT_FROM_WIN32(ERROR_BROKEN_PIPE));
            const auto circuit = decoder.Decode(png, L"image/png", {});
            assert(FAILED(circuit.result));
            assert(decoder.Stats().circuitRejected == 1);
            decoder.Shutdown();
        }

        {
            RemoteImageLimits shutdownLimits;
            shutdownLimits.maximumArtworkDecodeMilliseconds = 10'000;
            shutdownLimits.artworkDecoderShutdownMilliseconds = 250;
            ArtworkDecoderProcessOwner decoder(
                shutdownLimits, ExecutableSibling(L"ArtworkDecoderTestHost.exe"));
            std::jthread request([&](const std::stop_token token) {
                const auto result = decoder.Decode(
                    png, L"image/png", token, artworkdecoder::TestBehavior::Hang);
                assert(result.result == E_ABORT);
            });
            const auto waitDeadline = std::chrono::steady_clock::now() +
                std::chrono::seconds(2);
            while (decoder.Stats().starts == 0 &&
                   std::chrono::steady_clock::now() < waitDeadline)
                std::this_thread::yield();
            assert(decoder.Stats().starts == 1);
            const auto cancellationStarted = std::chrono::steady_clock::now();
            request.request_stop();
            request.join();
            assert(std::chrono::steady_clock::now() - cancellationStarted <
                   std::chrono::seconds(1));
            assert(decoder.Stats().terminated == 1);
            decoder.Shutdown();
        }

        auto maximumPng = EncodeWicImage(GUID_ContainerFormatPng, 1, 1);
        maximumPng.resize(defaultLimits.maximumEncodedArtworkBytes, 0);
        std::mutex nativeMutex;
        std::condition_variable nativeChanged;
        int nativeReady = 0;
        int nativeFailed = 0;
        std::vector<TrustedArtworkDecodeDiagnostic> decodeDiagnostics;
        RemoteImageCache nativeCache(
            defaultLimits,
            [&](std::wstring_view, const RemoteImageState state) {
                {
                    std::scoped_lock lock(nativeMutex);
                    if (state == RemoteImageState::Ready) ++nativeReady;
                    else if (state == RemoteImageState::Failed) ++nativeFailed;
                    else assert(false && "trusted artwork published a non-terminal callback");
                }
                nativeChanged.notify_all();
            },
            {},
            [](std::wstring_view, std::stop_token) { return true; },
            [&](const TrustedArtworkDecodeDiagnostic& diagnostic) {
                std::scoped_lock lock(nativeMutex);
                decodeDiagnostics.push_back(diagnostic);
            });
        const auto admit = [&](const std::wstring_view handle,
                               const std::wstring_view mime,
                               const std::vector<std::uint8_t>& bytes,
                               const int expectedReady) {
            const auto key = RemoteImageCache::TrustedArtworkKey(
                L"native-artwork", L"fixture-node", handle);
            assert(nativeCache.RequestTrustedArtwork(key) == RemoteImageRequestResult::Queued);
            assert(nativeCache.SupplyTrustedArtwork(
                L"native-artwork", handle, std::wstring(mime), Base64(bytes)));
            std::unique_lock lock(nativeMutex);
            assert(nativeChanged.wait_for(lock, std::chrono::seconds(3), [&] {
                return nativeReady == expectedReady;
            }));
            lock.unlock();
            assert(nativeCache.GetState(key) == RemoteImageState::Ready);
            return key;
        };
        const auto pngKey = admit(L"artwork.png-4096", L"image/png", png4096, 1);
        const auto jpegKey = admit(L"artwork.jpeg", L"image/jpeg", jpeg, 2);
        const auto maximumKey = admit(L"artwork.png-8mib", L"image/png", maximumPng, 3);
        assert(nativeCache.GetReadyImage(pngKey)->width == 4'096);
        assert(nativeCache.GetReadyImage(jpegKey)->mimeType == L"image/jpeg");
        assert(nativeCache.GetReadyImage(maximumKey)->width == 1);
        {
            std::scoped_lock lock(nativeMutex);
            assert(decodeDiagnostics.size() == 3);
            assert(std::all_of(
                decodeDiagnostics.begin(), decodeDiagnostics.end(),
                [](const TrustedArtworkDecodeDiagnostic& diagnostic) {
                    return diagnostic.resourceHash != 0 &&
                        diagnostic.handleHash != 0 &&
                        diagnostic.width > 0 && diagnostic.height > 0 &&
                        diagnostic.hasVisibleAlpha;
                }));
            assert(decodeDiagnostics[0].contentType == TrustedArtworkContentType::Png);
            assert(decodeDiagnostics[1].contentType == TrustedArtworkContentType::Jpeg);
            assert(decodeDiagnostics[2].contentType == TrustedArtworkContentType::Png);
        }
        (void)nativeCache.GetReadyImage(pngKey);
        {
            std::scoped_lock lock(nativeMutex);
            assert(decodeDiagnostics.size() == 3 &&
                   "ready-resource reuse cannot repeat the one-time alpha scan");
        }

        const auto oversizedKey = RemoteImageCache::TrustedArtworkKey(
            L"native-artwork", L"oversized-node", L"artwork.oversized");
        assert(nativeCache.RequestTrustedArtwork(oversizedKey) == RemoteImageRequestResult::Queued);
        auto oversized = maximumPng;
        oversized.push_back(0);
        assert(!nativeCache.SupplyTrustedArtwork(
            L"native-artwork", L"artwork.oversized", L"image/png", Base64(oversized)));
        assert(nativeCache.FailTrustedArtwork(L"native-artwork", L"artwork.oversized"));

        const auto mismatchKey = RemoteImageCache::TrustedArtworkKey(
            L"native-artwork", L"mismatch-node", L"artwork.mismatch");
        assert(nativeCache.RequestTrustedArtwork(mismatchKey) == RemoteImageRequestResult::Queued);
        assert(!nativeCache.SupplyTrustedArtwork(
            L"native-artwork", L"artwork.mismatch", L"image/jpeg", Base64(png4096)));
        assert(nativeCache.FailTrustedArtwork(L"native-artwork", L"artwork.mismatch"));
        const auto malformedWebPKey = RemoteImageCache::TrustedArtworkKey(
            L"native-artwork", L"webp-node", L"artwork.webp-malformed");
        assert(nativeCache.RequestTrustedArtwork(malformedWebPKey) ==
               RemoteImageRequestResult::Queued);
        auto malformedWebP = stillWebP;
        malformedWebP[4]++;
        assert(!nativeCache.SupplyTrustedArtwork(
            L"native-artwork", L"artwork.webp-malformed",
            L"image/webp", Base64(malformedWebP)));
        assert(nativeCache.FailTrustedArtwork(
            L"native-artwork", L"artwork.webp-malformed"));
        {
            std::scoped_lock lock(nativeMutex);
            assert(nativeReady == 3);
            assert(nativeFailed == 3);
        }
        nativeCache.Shutdown();
        if (SUCCEEDED(initialized)) CoUninitialize();
    }

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

    {
        std::mutex demandMutex;
        std::condition_variable demandChanged;
        bool secondDemandEntered = false;
        bool releaseSecondDemand = false;
        bool secondDemandReturned = false;
        int demandCalls = 0;
        std::vector<std::pair<std::wstring, RemoteImageState>> transitions;
        RemoteImageLimits demandLimits;
        demandLimits.maximumEntries = 3;
        demandLimits.maximumPendingEntries = 2;
        demandLimits.maximumReadyEntries = 2;
        demandLimits.maximumDecodedBytes = 8;
        RemoteImageCache demandCache(
            demandLimits,
            [&](const std::wstring_view key, const RemoteImageState state) {
                {
                    std::scoped_lock lock(demandMutex);
                    transitions.emplace_back(key, state);
                }
                demandChanged.notify_all();
            },
            [](std::wstring_view, std::stop_token,
               const RemoteImageLimits&) {
                RemoteDecodedImage image;
                image.width = 1;
                image.height = 1;
                image.stride = 4;
                image.premultipliedBgra = {0x20, 0x30, 0x40, 0xFF};
                image.mimeType = L"image/png";
                return RemoteImageFetchResult{S_OK, std::move(image), {}};
            },
            [&](const std::wstring_view key, const std::stop_token token) {
                std::unique_lock lock(demandMutex);
                ++demandCalls;
                if (key.ends_with(L"artwork.async-b")) {
                    secondDemandEntered = true;
                    demandChanged.notify_all();
                    demandChanged.wait(lock, [&] {
                        return releaseSecondDemand || token.stop_requested();
                    });
                    secondDemandReturned = true;
                    demandChanged.notify_all();
                    return false;
                }
                return key.ends_with(L"artwork.async-a");
            });
        const auto firstKey = RemoteImageCache::TrustedArtworkKey(
            L"async-artwork", L"tile-a", L"artwork.async-a");
        const auto refusedKey = RemoteImageCache::TrustedArtworkKey(
            L"async-artwork", L"tile-b", L"artwork.async-b");
        const auto capacityKey = RemoteImageCache::TrustedArtworkKey(
            L"async-artwork", L"tile-c", L"artwork.async-c");
        assert(demandCache.RequestTrustedArtwork(firstKey) ==
               RemoteImageRequestResult::Queued);
        {
            std::unique_lock lock(demandMutex);
            assert(demandChanged.wait_for(lock, std::chrono::seconds(2), [&] {
                return demandCalls == 1;
            }));
        }
        assert(demandCache.RequestTrustedArtwork(firstKey) ==
               RemoteImageRequestResult::AlreadyTracked);
        assert(demandCache.RequestTrustedArtwork(refusedKey) ==
               RemoteImageRequestResult::Queued);
        assert(demandCache.RequestTrustedArtwork(capacityKey) ==
               RemoteImageRequestResult::CapacityExceeded);
        assert(demandCache.SupplyTrustedArtwork(
            L"async-artwork", L"artwork.async-a", L"image/png",
            trustedPngBase64));
        {
            std::unique_lock lock(demandMutex);
            assert(demandChanged.wait_for(lock, std::chrono::seconds(2), [&] {
                return secondDemandEntered;
            }));
            assert(!secondDemandReturned);
        }
        {
            std::unique_lock lock(demandMutex);
            assert(demandChanged.wait_for(lock, std::chrono::seconds(2), [&] {
                return demandCache.GetState(firstKey) == RemoteImageState::Ready;
            }));
        }
        const auto httpsKey = L"https://example.test/independent.png";
        assert(demandCache.Request(httpsKey) == RemoteImageRequestResult::Queued);
        {
            std::unique_lock lock(demandMutex);
            assert(demandChanged.wait_for(lock, std::chrono::seconds(2), [&] {
                return demandCache.GetState(httpsKey) == RemoteImageState::Ready;
            }));
            assert(!secondDemandReturned);
            releaseSecondDemand = true;
        }
        demandChanged.notify_all();
        {
            std::unique_lock lock(demandMutex);
            assert(demandChanged.wait_for(lock, std::chrono::seconds(2), [&] {
                return secondDemandReturned &&
                    demandCache.GetState(refusedKey) == RemoteImageState::Failed;
            }));
        }
        assert(demandCache.RequestTrustedArtwork(capacityKey) ==
               RemoteImageRequestResult::Queued);
        {
            std::unique_lock lock(demandMutex);
            assert(demandChanged.wait_for(lock, std::chrono::seconds(2), [&] {
                return demandCalls == 3 &&
                    demandCache.GetState(capacityKey) == RemoteImageState::Failed;
            }));
        }
        const auto demandStats = demandCache.GetStats();
        assert(demandStats.entries <= demandLimits.maximumEntries);
        assert(demandStats.queuedOrLoading == 0);
        assert(demandStats.pendingCapacityRejections == 1);
        assert(demandStats.trustedArtworkHits == 1);
        demandCache.Shutdown();
    }

    {
        std::mutex shutdownMutex;
        std::condition_variable_any shutdownChanged;
        bool shutdownDemandEntered = false;
        RemoteImageCache shutdownCache(
            {}, {}, {},
            [&](std::wstring_view, const std::stop_token token) {
                std::unique_lock lock(shutdownMutex);
                shutdownDemandEntered = true;
                shutdownChanged.notify_all();
                (void)shutdownChanged.wait(lock, token, [] { return false; });
                return false;
            });
        const auto shutdownKey = RemoteImageCache::TrustedArtworkKey(
            L"shutdown-artwork", L"tile", L"artwork.shutdown");
        assert(shutdownCache.RequestTrustedArtwork(shutdownKey) ==
               RemoteImageRequestResult::Queued);
        {
            std::unique_lock lock(shutdownMutex);
            assert(shutdownChanged.wait_for(lock, std::chrono::seconds(2), [&] {
                return shutdownDemandEntered;
            }));
        }
        const auto shutdownStarted = std::chrono::steady_clock::now();
        shutdownCache.Shutdown();
        assert(std::chrono::steady_clock::now() - shutdownStarted <
               std::chrono::seconds(1));
        assert(shutdownCache.RequestTrustedArtwork(shutdownKey) ==
               RemoteImageRequestResult::ShuttingDown);
    }

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
            assert(source == L"data:image/png;base64,validated");
            RemoteDecodedImage image;
            image.width = 1;
            image.height = 1;
            image.stride = 4;
            image.premultipliedBgra = {0x20, 0x30, 0x40, 0xFF};
            image.mimeType = L"image/png";
            return RemoteImageFetchResult{S_OK, std::move(image), {}};
        },
        [&](std::wstring_view key, std::stop_token) {
            assert(key.starts_with(L"wrail-artwork\x1f"));
            constexpr std::wstring_view prefix = L"wrail-artwork\x1f";
            const auto widgetEnd = key.find(L'\x1f', prefix.size());
            const auto handleStart = key.rfind(L'\x1f');
            return artworkCacheOwner->SupplyTrustedArtwork(
                key.substr(prefix.size(), widgetEnd - prefix.size()),
                key.substr(handleStart + 1), L"image/png", trustedPngBase64);
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
    const auto oldRevision = RemoteImageCache::TrustedArtworkKey(
        L"games-apps", L"row-revision", L"library.art.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    const auto newRevision = RemoteImageCache::TrustedArtworkKey(
        L"games-apps", L"row-revision", L"library.art.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
    assert(oldRevision == RemoteImageCache::TrustedArtworkKey(
        L"games-apps", L"another-node", L"library.art.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"));
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
    assert(artworkCache.GetState(oldRevision) == RemoteImageState::Ready);
    {
        std::unique_lock lock(artworkMutex);
        assert(artworkCompleted.wait_for(
            lock, std::chrono::seconds(2),
            [&] { return artworkFetches == largeCollectionItems + 2; }));
    }
    assert(artworkCache.GetState(newRevision) == RemoteImageState::Ready);
    assert(artworkCache.GetState(oldRevision) == RemoteImageState::Ready);
    const auto artworkStats = artworkCache.GetStats();
    assert(artworkStats.trustedArtworkRequests ==
           static_cast<std::uint64_t>(largeCollectionItems * 2 + 2));
    assert(artworkStats.trustedArtworkHits ==
           static_cast<std::uint64_t>(largeCollectionItems));
    assert(artworkStats.trustedArtworkSupplies ==
           static_cast<std::uint64_t>(largeCollectionItems + 2));
    assert(artworkStats.staleArtworkCompletions == 0);
    assert(artworkStats.readySourcePixels == artworkStats.readyEntries);
    assert(artworkStats.maximumSourceWidth == 1 &&
           artworkStats.maximumSourceHeight == 1);
    const std::vector<std::wstring> currentHandles{
        L"library.art.aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
        L"library.art.bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
        L"library.art.not-current-not-retained"};
    const auto currentResidency = artworkCache.GetTrustedArtworkResidency(
        L"games-apps", currentHandles);
    assert(currentResidency.entries == 2);
    assert(currentResidency.readyEntries == 2);
    assert(currentResidency.inFlightEntries == 0);
    assert(currentResidency.decodedBytes == 8);
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
        [](std::wstring_view, std::stop_token) { return true; });
    const auto failedRevision =
        L"wrail-artwork\x1fplaynite-library\x1ftile.artwork\x1f"
        L"library.art.11111111111111111111111111111111";
    const auto recoveredRevision =
        L"wrail-artwork\x1fplaynite-library\x1ftile.artwork\x1f"
        L"library.art.22222222222222222222222222222222";
    assert(failureCache.RequestTrustedArtwork(failedRevision) ==
           RemoteImageRequestResult::Queued);
    assert(failureCache.GetState(failedRevision) == RemoteImageState::Loading);
    assert(failureCache.FailTrustedArtwork(
        L"playnite-library", L"library.art.11111111111111111111111111111111"));
    assert(failureCache.GetState(failedRevision) == RemoteImageState::Failed);
    assert(!failureCache.FailTrustedArtwork(
        L"playnite-library", L"library.art.11111111111111111111111111111111"));
    assert(!failureCache.SupplyTrustedArtwork(
        L"playnite-library", L"library.art.11111111111111111111111111111111",
        L"image/png", trustedPngBase64));
    assert(failureCache.GetStats().staleArtworkCompletions == 1);
    const auto sharedFailedRevision =
        L"wrail-artwork\x1fplaynite-library\x1fsecond-tile.artwork\x1f"
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
        L"playnite-library", L"library.art.11111111111111111111111111111111",
        L"image/png", trustedPngBase64));
    assert(failureCache.SupplyTrustedArtwork(
        L"playnite-library", L"library.art.22222222222222222222222222222222",
        L"image/png", trustedPngBase64));
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
        L"playnite-library", L"library.art.11111111111111111111111111111111"));
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
