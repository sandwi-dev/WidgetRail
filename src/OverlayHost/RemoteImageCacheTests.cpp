#include "RemoteImageCache.h"
#include "ArtworkDiskCache.h"
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
    {
        using widgetrail::ArtworkDiskCache;
        assert(ArtworkDiskCache::FreshSeconds(L"public, max-age=3600", L"", 100) == 3500);
        assert(!ArtworkDiskCache::FreshSeconds(L"public, max-age=0", L""));
        assert(!ArtworkDiskCache::FreshSeconds(L"no-store, max-age=9999", L""));
        assert(!ArtworkDiskCache::FreshSeconds(L"private, max-age=9999", L""));
        assert(!ArtworkDiskCache::FreshSeconds(L"no-cache, max-age=9999", L""));
        assert(!ArtworkDiskCache::FreshSeconds(L"max-age=9999", L"Cookie"));
        assert(!ArtworkDiskCache::FreshSeconds(L"max-age=invalid", L""));
        assert(!ArtworkDiskCache::FreshSeconds(L"public", L""));
        const auto root = std::filesystem::temp_directory_path() /
            (L"wrail-artwork-cache-test-" + std::to_wstring(GetCurrentProcessId()) + L"-" + std::to_wstring(GetTickCount64()));
        const std::vector<std::uint8_t> bytes{1, 2, 3, 4};
        {
            ArtworkDiskCache cache(root, 4096, 2);
            assert(cache.Write(L"https://example.test/one", bytes, L"image/png", 60, 1000));
            ArtworkDiskCache reopened(root, 4096, 2);
            const auto found = reopened.Read(L"https://example.test/one", 1001);
            assert(found && found->bytes == bytes && found->mime == L"image/png");
            assert(!reopened.Read(L"https://example.test/other", 1001));
            assert(!reopened.Read(L"https://example.test/one", 1060));
            assert(cache.Write(L"https://example.test/one", bytes, L"image/png", 60, 1000));
            assert(cache.Write(L"https://example.test/two", bytes, L"image/png", 60, 1000));
            assert(cache.Read(L"https://example.test/one", 1001));
            assert(cache.Write(L"https://example.test/three", bytes, L"image/png", 60, 1000));
            assert(!cache.Read(L"https://example.test/two", 1001));
            assert(cache.Read(L"https://example.test/one", 1001));
            cache.Erase(L"https://example.test/one");
            // Corrupt a persisted file: checksums/bounds turn it into a miss.
            for (const auto& item : std::filesystem::directory_iterator(root)) {
                if (item.path().extension() != L".art") continue;
                std::fstream stream(item.path(), std::ios::binary | std::ios::in | std::ios::out);
                stream.seekp(-1, std::ios::end); stream.put(99);
            }
            assert(!cache.Read(L"https://example.test/three", 1001));
            ArtworkDiskCache tooSmall(root / L"small", 16);
            assert(!tooSmall.Write(L"https://example.test/one", bytes, L"image/png", 60));
            const auto blocker = root / L"blocked";
            { std::ofstream file(blocker); file << "not a directory"; }
            ArtworkDiskCache unavailable(blocker);
            assert(!unavailable.Write(L"https://example.test/one", bytes, L"image/png", 60));
            assert(!unavailable.Read(L"https://example.test/one"));
        }
        std::filesystem::remove_all(root);
    }

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
    assert(defaultLimits.maximumDecodedBytes == 160U * 1024U * 1024U);
    assert(defaultLimits.maximumEncodedArtworkBytes == 8U * 1024U * 1024U);
    assert(defaultLimits.maximumArtworkDimension == 4'096);
    assert(defaultLimits.maximumArtworkPixels == 16'777'216);

    {
        const auto initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
        assert(SUCCEEDED(initialized) || initialized == RPC_E_CHANGED_MODE);
        const auto png4096 = EncodeWicImage(GUID_ContainerFormatPng, 4'096, 1);
        const auto jpeg = EncodeWicImage(GUID_ContainerFormatJpeg, 1, 1);
        const auto png = EncodeWicImage(GUID_ContainerFormatPng, 2, 1);
        // Display-sized variants must shrink the actual decoded buffer, retain
        // aspect ratio, and keep original source admission limits intact.
        {
            ArtworkDecoderProcessOwner decoder(defaultLimits, ExecutableSibling(L"ArtworkDecoderTestHost.exe"));
            for (const auto& format : {GUID_ContainerFormatPng, GUID_ContainerFormatJpeg}) {
                const auto encoded = EncodeWicImage(format, 1200, 1800);
                const auto mime = IsEqualGUID(format, GUID_ContainerFormatPng) ? L"image/png" : L"image/jpeg";
                const auto poster = decoder.Decode(encoded, mime, {}, artworkdecoder::TestBehavior::Normal, 256, 384);
                assert(poster.succeeded() && poster.image.width == 256 && poster.image.height == 384);
                assert(poster.image.premultipliedBgra.size() == 256U * 384U * 4U);
                const auto background = decoder.Decode(encoded, mime, {}, artworkdecoder::TestBehavior::Normal, 1024, 576);
                assert(background.succeeded() && background.image.width > poster.image.width);
                assert(background.image.premultipliedBgra.size() <= (2U * 1024U * 576U + 4096U) * 4U);
                assert(!decoder.Decode(encoded, mime, {}, artworkdecoder::TestBehavior::Normal, 0, 384).succeeded());
                assert(!decoder.Decode(encoded, mime, {}, artworkdecoder::TestBehavior::Normal, 4096, 384).succeeded());
            }
            const auto smallImage = decoder.Decode(png, L"image/png", {}, artworkdecoder::TestBehavior::Normal, 256, 384);
            assert(smallImage.succeeded() && smallImage.image.width == 2 && smallImage.image.height == 1);
            const auto oversized = EncodeWicImage(GUID_ContainerFormatPng, 4097, 1);
            assert(!decoder.Decode(oversized, L"image/png", {}, artworkdecoder::TestBehavior::Normal, 64, 64).succeeded());
            const auto bucket = DisplayImageSize(120, 180, 1.05F);
            assert(bucket.width == 128 && bucket.height == 192);
        }
        {
            const auto encoded = EncodeWicImage(GUID_ContainerFormatPng, 1200, 1800);
            const TrustedArtworkDemandAuthority authority{L"variant-test",L"runtime",L"presentation"};
            const auto posterKey = RemoteImageCache::TrustedArtworkKey(authority.widgetId,L"poster",L"cover",{256,384});
            const auto backgroundKey = RemoteImageCache::TrustedArtworkKey(authority.widgetId,L"background",L"cover",{512,768});
            assert(posterKey != backgroundKey);
            assert(posterKey == RemoteImageCache::TrustedArtworkKey(authority.widgetId,L"another-poster",L"cover",{256,384}));
            const auto accepted=[](std::wstring_view,const TrustedArtworkDemandAuthority&,std::stop_token) { return TrustedArtworkRequestDisposition::Accepted; };
            RemoteImageCache variants(defaultLimits, {}, {}, accepted);
            assert(variants.RequestTrustedArtwork(posterKey,authority,{256,384}) == RemoteImageRequestResult::Queued);
            assert(variants.RequestTrustedArtwork(backgroundKey,authority,{512,768}) == RemoteImageRequestResult::Queued);
            assert(variants.SupplyTrustedArtwork(authority.widgetId,L"cover",authority,L"image/png",Base64(encoded)));
            const auto until=std::chrono::steady_clock::now()+std::chrono::seconds(3);
            while (variants.GetStats().readyEntries<2 && std::chrono::steady_clock::now()<until)
                std::this_thread::sleep_for(std::chrono::milliseconds(1));
            const auto poster=variants.GetReadyImage(posterKey);
            const auto background=variants.GetReadyImage(backgroundKey);
            assert(poster && poster->width==256 && poster->height==384);
            assert(background && background->width==512 && background->height==768);
            auto tight=defaultLimits;
            tight.maximumEncodedArtworkBytesTotal=encoded.size();
            tight.maximumEncodedArtworkBytesPerWidget=encoded.size();
            tight.maximumEncodedArtworkBytes=encoded.size();
            RemoteImageCache bounded(tight, {}, {}, accepted);
            (void)bounded.RequestTrustedArtwork(posterKey,authority,{256,384});
            (void)bounded.RequestTrustedArtwork(backgroundKey,authority,{512,768});
            assert(!bounded.SupplyTrustedArtwork(authority.widgetId,L"cover",authority,L"image/png",Base64(encoded)));
            assert(bounded.GetStats().encodedArtworkBytes==0);
        }
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

        const std::string packageSvgText =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\">"
            "<path fill=\"#16a34a\" d=\"M2 12 L12 2 L22 12 L12 22 Z\"/></svg>";
        const std::vector<std::uint8_t> packageSvg(
            packageSvgText.begin(), packageSvgText.end());
        {
            RemoteImageLimits svgLimits;
            ArtworkDecoderProcessOwner decoder(
                svgLimits, ExecutableSibling(L"ArtworkDecoderTestHost.exe"));
            const auto color = decoder.Decode(
                packageSvg, L"image/svg+xml", {},
                artworkdecoder::TestBehavior::Normal, 32, 24,
                artworkdecoder::RasterVariant::OriginalColor);
            assert(SUCCEEDED(color.result));
            assert(color.image.width == 32 && color.image.height == 24);
            assert(color.image.mimeType == L"image/svg+xml");
            const auto mask = decoder.Decode(
                packageSvg, L"image/svg+xml", {},
                artworkdecoder::TestBehavior::Normal, 32, 24,
                artworkdecoder::RasterVariant::AlphaMask);
            assert(SUCCEEDED(mask.result));
            assert(mask.image.width == 32 && mask.image.height == 24);
            for (std::size_t offset = 0;
                 offset < mask.image.premultipliedBgra.size(); offset += 4) {
                const auto alpha = mask.image.premultipliedBgra[offset + 3];
                assert(mask.image.premultipliedBgra[offset] == alpha);
                assert(mask.image.premultipliedBgra[offset + 1] == alpha);
                assert(mask.image.premultipliedBgra[offset + 2] == alpha);
            }
            decoder.Shutdown();
        }

        {
            std::mutex iconMutex;
            std::condition_variable iconChanged;
            int iconReady{};
            std::size_t iconRequests{};
            RemoteImageCache iconCache(
                defaultLimits,
                [&](std::wstring_view, const RemoteImageState state) {
                    {
                        std::scoped_lock lock(iconMutex);
                        if (state == RemoteImageState::Ready) ++iconReady;
                    }
                    iconChanged.notify_all();
                },
                {}, {}, {},
                [&](const PackageIconDemandAuthority&, std::stop_token) {
                    ++iconRequests;
                    return PackageIconRequest{
                        PackageIconRequestDisposition::Resolved, packageSvg};
                });
            PackageIconDemandAuthority authority{
                L"dev.test.package-icon", L"runtime-a", L"presentation-a",
                std::wstring(64, L'a'), L"test.mark",
                std::wstring(64, L'b'), std::wstring(64, L'c'),
                32, 24, PackageIconRasterVariant::AlphaMask};
            const auto maskKey = RemoteImageCache::PackageIconKey(authority);
            assert(iconCache.RequestPackageIcon(maskKey, authority) ==
                   RemoteImageRequestResult::Queued);
            {
                std::unique_lock lock(iconMutex);
                assert(iconChanged.wait_for(lock, std::chrono::seconds(3), [&] {
                    return iconReady == 1;
                }));
            }
            assert(iconCache.GetState(maskKey) == RemoteImageState::Ready);
            auto mask = iconCache.GetReadyImage(maskKey);
            assert(mask && mask->width == 32 && mask->height == 24);
            auto replacementAuthority = authority;
            replacementAuthority.runtimeGeneration = L"runtime-b";
            replacementAuthority.presentationGeneration = L"presentation-b";
            assert(RemoteImageCache::PackageIconKey(replacementAuthority) == maskKey);
            assert(iconCache.RequestPackageIcon(maskKey, replacementAuthority) ==
                   RemoteImageRequestResult::AlreadyTracked);
            replacementAuthority.rasterVariant = PackageIconRasterVariant::OriginalColor;
            const auto colorKey = RemoteImageCache::PackageIconKey(replacementAuthority);
            assert(colorKey != maskKey);
            assert(iconCache.RequestPackageIcon(colorKey, replacementAuthority) ==
                   RemoteImageRequestResult::Queued);
            {
                std::unique_lock lock(iconMutex);
                assert(iconChanged.wait_for(lock, std::chrono::seconds(3), [&] {
                    return iconReady == 2;
                }));
            }
            assert(iconRequests == 2);
            iconCache.Shutdown();
        }

        {
            std::mutex replacementMutex;
            std::condition_variable replacementChanged;
            bool oldRequestStarted{};
            bool releaseOldRequest{};
            int readyCompletions{};
            int requests{};
            RemoteImageCache replacementCache(
                defaultLimits,
                [&](std::wstring_view, const RemoteImageState state) {
                    if (state == RemoteImageState::Ready) {
                        {
                            std::scoped_lock lock(replacementMutex);
                            ++readyCompletions;
                        }
                        replacementChanged.notify_all();
                    }
                },
                {}, {}, {},
                [&](const PackageIconDemandAuthority& authority,
                    std::stop_token) {
                    std::unique_lock lock(replacementMutex);
                    ++requests;
                    if (authority.runtimeGeneration == L"runtime-old") {
                        oldRequestStarted = true;
                        replacementChanged.notify_all();
                        replacementChanged.wait(lock, [&] {
                            return releaseOldRequest;
                        });
                        return PackageIconRequest{
                            PackageIconRequestDisposition::OriginRetired, {}};
                    }
                    return PackageIconRequest{
                        PackageIconRequestDisposition::Resolved, packageSvg};
                });
            PackageIconDemandAuthority oldAuthority{
                L"dev.test.package-icon-replacement", L"runtime-old",
                L"presentation-old", std::wstring(64, L'f'), L"test.mark",
                std::wstring(64, L'a'), std::wstring(64, L'b'),
                32, 24, PackageIconRasterVariant::AlphaMask};
            const auto replacementKey =
                RemoteImageCache::PackageIconKey(oldAuthority);
            assert(replacementCache.RequestPackageIcon(
                       replacementKey, oldAuthority) ==
                   RemoteImageRequestResult::Queued);
            {
                std::unique_lock lock(replacementMutex);
                assert(replacementChanged.wait_for(
                    lock, std::chrono::seconds(3), [&] {
                        return oldRequestStarted;
                    }));
            }
            auto currentAuthority = oldAuthority;
            currentAuthority.runtimeGeneration = L"runtime-current";
            currentAuthority.presentationGeneration = L"presentation-current";
            assert(RemoteImageCache::PackageIconKey(currentAuthority) ==
                   replacementKey);
            assert(replacementCache.GetPackageIconState(
                       replacementKey, currentAuthority) ==
                   RemoteImageState::Missing);
            assert(replacementCache.RequestPackageIcon(
                       replacementKey, currentAuthority) ==
                   RemoteImageRequestResult::Queued);
            {
                std::scoped_lock lock(replacementMutex);
                releaseOldRequest = true;
            }
            replacementChanged.notify_all();
            {
                std::unique_lock lock(replacementMutex);
                assert(replacementChanged.wait_for(
                    lock, std::chrono::seconds(3), [&] {
                        return readyCompletions == 1;
                    }));
                assert(requests == 2);
            }
            assert(replacementCache.GetPackageIconState(
                       replacementKey, currentAuthority) ==
                   RemoteImageState::Ready);
            const auto currentImage =
                replacementCache.GetReadyImage(replacementKey);
            assert(currentImage && currentImage->width == 32 &&
                   currentImage->height == 24);
            auto laterAuthority = currentAuthority;
            laterAuthority.runtimeGeneration = L"runtime-later";
            laterAuthority.presentationGeneration = L"presentation-later";
            assert(replacementCache.GetPackageIconState(
                       replacementKey, laterAuthority) ==
                   RemoteImageState::Ready);
            assert(replacementCache.RequestPackageIcon(
                       replacementKey, laterAuthority) ==
                   RemoteImageRequestResult::AlreadyTracked);
            {
                std::scoped_lock lock(replacementMutex);
                assert(requests == 2 && readyCompletions == 1);
            }
            replacementCache.Shutdown();
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
            [](std::wstring_view, const TrustedArtworkDemandAuthority&,
               std::stop_token) {
                return TrustedArtworkRequestDisposition::Accepted;
            },
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

    {
        // Two protected visible images survive speculative churn at the same
        // fixed budget. Releasing one permits ordinary LRU eviction again.
        RemoteImageLimits pressure;
        pressure.maximumDecodedImageBytes = 4;
        pressure.maximumDecodedBytes = 12;
        RemoteImageCache cache(pressure, {}, [](std::wstring_view source, std::stop_token, const RemoteImageLimits& limits) {
            assert(source.starts_with(L"https://example.test/"));
            assert(limits.decodeSize.width == 64 && limits.decodeSize.height == 64);
            RemoteDecodedImage image;
            image.width = image.height = 1; image.stride = 4;
            image.premultipliedBgra = {0x10, 0x20, 0x30, 0xFF};
            return RemoteImageFetchResult{S_OK, std::move(image), {}};
        });
        const auto key = [](std::wstring source) { return RemoteImageCache::VariantKey(source, {64,64}); };
        const auto load = [&](std::wstring source) {
            assert(cache.Request(source, {64,64}) == RemoteImageRequestResult::Queued);
            const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
            while (cache.GetState(key(source)) != RemoteImageState::Ready && std::chrono::steady_clock::now() < deadline)
                std::this_thread::sleep_for(std::chrono::milliseconds(1));
            assert(cache.GetState(key(source)) == RemoteImageState::Ready);
            assert(cache.GetStats().decodedBytes <= 12);
        };
        const auto tray = key(L"https://example.test/tray");
        const auto poster = key(L"https://example.test/visible");
        cache.ProtectImages(&cache, {tray, poster});
        load(L"https://example.test/tray"); load(L"https://example.test/visible");
        for (int i=0; i<20; ++i) load(L"https://example.test/speculative-" + std::to_wstring(i));
        assert(cache.GetState(tray) == RemoteImageState::Ready);
        assert(cache.GetState(poster) == RemoteImageState::Ready);
        assert(!cache.CanPrefetch(L"https://example.test/not-visible"));
        assert(cache.CanPrefetch(tray));
        const auto retained = key(L"https://example.test/speculative-19");
        cache.ProtectImages(&cache, {tray, poster, retained});
        const auto denied = key(L"https://example.test/denied");
        assert(cache.Request(L"https://example.test/denied", {64,64}) == RemoteImageRequestResult::Queued);
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
        while (cache.GetState(denied) != RemoteImageState::Failed && std::chrono::steady_clock::now() < deadline)
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        assert(cache.BudgetRejected(denied));
        assert(!cache.ReleaseBudgetRejection(denied));
        assert(cache.GetStats().decodedBytes == 12);
        cache.ProtectImages(&cache, {tray, poster});
        assert(cache.ReleaseBudgetRejection(denied));
        load(L"https://example.test/denied");
        assert(cache.Request(L"http://example.test/unsafe", {64,64}) == RemoteImageRequestResult::InvalidUrl);
        cache.ProtectImages(&cache, {poster});
        load(L"https://example.test/new");
        assert(cache.GetState(tray) == RemoteImageState::Missing);
        assert(cache.GetState(poster) == RemoteImageState::Ready);
        cache.ReleaseImageProtection(&cache);
        load(L"https://example.test/final");
        assert(cache.GetState(poster) == RemoteImageState::Missing);
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
            [&](const std::wstring_view key,
                const TrustedArtworkDemandAuthority&,
                const std::stop_token token) {
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
                    return TrustedArtworkRequestDisposition::TerminalFailure;
                }
                return key.ends_with(L"artwork.async-a")
                    ? TrustedArtworkRequestDisposition::Accepted
                    : TrustedArtworkRequestDisposition::TerminalFailure;
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

    for (const auto staleDisposition : {
             TrustedArtworkRequestDisposition::OriginRetired,
             TrustedArtworkRequestDisposition::TerminalFailure}) {
        std::mutex replacementMutex;
        std::condition_variable replacementChanged;
        bool originAEntered = false;
        bool releaseOriginA = false;
        std::vector<TrustedArtworkDemandAuthority> observedOrigins;
        const TrustedArtworkDemandAuthority originA{
            L"replacement-artwork", L"runtime-a", L"presentation-a"};
        const TrustedArtworkDemandAuthority originB{
            L"replacement-artwork", L"runtime-b", L"presentation-b"};
        RemoteImageCache replacementCache(
            {},
            [&](std::wstring_view, RemoteImageState) {
                replacementChanged.notify_all();
            },
            {},
            [&](std::wstring_view,
                const TrustedArtworkDemandAuthority& authority,
                std::stop_token) {
                std::unique_lock lock(replacementMutex);
                const bool firstOriginA = observedOrigins.empty();
                observedOrigins.push_back(authority);
                replacementChanged.notify_all();
                if (authority == originA && firstOriginA) {
                    originAEntered = true;
                    replacementChanged.notify_all();
                    replacementChanged.wait(lock, [&] { return releaseOriginA; });
                    return staleDisposition;
                }
                return TrustedArtworkRequestDisposition::Accepted;
            });
        const auto replacementKey = RemoteImageCache::TrustedArtworkKey(
            L"replacement-artwork", L"tile", L"artwork.same-handle");
        assert(replacementCache.RequestTrustedArtwork(replacementKey, originA) ==
               RemoteImageRequestResult::Queued);
        {
            std::unique_lock lock(replacementMutex);
            assert(replacementChanged.wait_for(
                lock, std::chrono::seconds(2), [&] { return originAEntered; }));
        }
        assert(replacementCache.GetTrustedArtworkState(replacementKey, originB) ==
               RemoteImageState::Missing);
        assert(replacementCache.RequestTrustedArtwork(replacementKey, originB) ==
               RemoteImageRequestResult::Queued);
        assert(replacementCache.GetTrustedArtworkState(replacementKey, originA) ==
               RemoteImageState::Missing);
        assert(replacementCache.RequestTrustedArtwork(replacementKey, originA) ==
               RemoteImageRequestResult::Queued);
        assert(replacementCache.GetStats().entries == 1 &&
               replacementCache.GetStats().queuedOrLoading == 1);
        {
            std::scoped_lock lock(replacementMutex);
            releaseOriginA = true;
        }
        replacementChanged.notify_all();
        {
            std::unique_lock lock(replacementMutex);
            assert(replacementChanged.wait_for(lock, std::chrono::seconds(2), [&] {
                return observedOrigins.size() == 2;
            }));
        }
        assert(observedOrigins[0] == originA && observedOrigins[1] == originA);
        assert(replacementCache.GetState(replacementKey) ==
               RemoteImageState::Loading);
        assert(!replacementCache.SupplyTrustedArtwork(
            L"replacement-artwork", L"artwork.same-handle", originB,
            L"image/png", trustedPngBase64));
        assert(replacementCache.SupplyTrustedArtwork(
            L"replacement-artwork", L"artwork.same-handle", originA,
            L"image/png", trustedPngBase64));
        {
            std::unique_lock lock(replacementMutex);
            assert(replacementChanged.wait_for(lock, std::chrono::seconds(2), [&] {
                return replacementCache.GetState(replacementKey) ==
                    RemoteImageState::Ready;
            }));
        }
        assert(replacementCache.GetStats().queuedOrLoading == 0);
        replacementCache.Shutdown();
    }

    {
        std::mutex shutdownMutex;
        std::condition_variable_any shutdownChanged;
        bool shutdownDemandEntered = false;
        RemoteImageCache shutdownCache(
            {}, {}, {},
            [&](std::wstring_view, const TrustedArtworkDemandAuthority&,
                const std::stop_token token) {
                std::unique_lock lock(shutdownMutex);
                shutdownDemandEntered = true;
                shutdownChanged.notify_all();
                (void)shutdownChanged.wait(lock, token, [] { return false; });
                return TrustedArtworkRequestDisposition::TerminalFailure;
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
        [&](std::wstring_view key, const TrustedArtworkDemandAuthority&,
            std::stop_token) {
            assert(key.starts_with(L"wrail-artwork\x1f"));
            constexpr std::wstring_view prefix = L"wrail-artwork\x1f";
            const auto widgetEnd = key.find(L'\x1f', prefix.size());
            const auto handleStart = key.rfind(L'\x1f');
            return artworkCacheOwner->SupplyTrustedArtwork(
                key.substr(prefix.size(), widgetEnd - prefix.size()),
                key.substr(handleStart + 1), L"image/png", trustedPngBase64)
                ? TrustedArtworkRequestDisposition::Accepted
                : TrustedArtworkRequestDisposition::TerminalFailure;
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
        [](std::wstring_view, const TrustedArtworkDemandAuthority&,
           std::stop_token) {
            return TrustedArtworkRequestDisposition::Accepted;
        });
    const auto failedRevision =
        L"wrail-artwork\x1fplaynite-library\x1ftile.artwork\x1f"
        L"library.art.11111111111111111111111111111111";
    const auto recoveredRevision =
        L"wrail-artwork\x1fplaynite-library\x1ftile.artwork\x1f"
        L"library.art.22222222222222222222222222222222";
    const TrustedArtworkDemandAuthority failedAuthority{
        L"playnite-library", L"failure-runtime", L"failure-presentation"};
    assert(failureCache.RequestTrustedArtwork(failedRevision, failedAuthority) ==
           RemoteImageRequestResult::Queued);
    assert(failureCache.GetState(failedRevision) == RemoteImageState::Loading);
    assert(failureCache.FailTrustedArtwork(
        L"playnite-library", L"library.art.11111111111111111111111111111111",
        failedAuthority));
    assert(failureCache.GetState(failedRevision) == RemoteImageState::Failed);
    assert(!failureCache.FailTrustedArtwork(
        L"playnite-library", L"library.art.11111111111111111111111111111111",
        failedAuthority));
    assert(!failureCache.SupplyTrustedArtwork(
        L"playnite-library", L"library.art.11111111111111111111111111111111",
        failedAuthority, L"image/png", trustedPngBase64));
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
