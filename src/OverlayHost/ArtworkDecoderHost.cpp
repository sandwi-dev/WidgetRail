#include "ArtworkDecoderProtocol.h"
#include "EncodedArtworkEnvelope.h"

#include <Windows.h>
#include <wincodec.h>
#include <wrl/client.h>

#include <algorithm>
#include <cstdint>
#include <cwchar>
#include <limits>
#include <span>
#include <string>
#include <string_view>

namespace {

using Microsoft::WRL::ComPtr;
using namespace widgetrail::artworkdecoder;

[[nodiscard]] HANDLE ParseHandle(
    const int count,
    wchar_t** values,
    const std::wstring_view name) noexcept {
    const std::wstring prefix = L"--" + std::wstring(name) + L"=";
    for (int index = 1; index < count; ++index) {
        const std::wstring_view value(values[index]);
        if (!value.starts_with(prefix)) continue;
        wchar_t* end = nullptr;
        const auto parsed = std::wcstoull(values[index] + prefix.size(), &end, 0);
        if (!end || *end != L'\0' || parsed == 0) return nullptr;
        return reinterpret_cast<HANDLE>(static_cast<std::uintptr_t>(parsed));
    }
    return nullptr;
}

[[nodiscard]] bool MatchesSignature(
    const ContentType type,
    const std::span<const std::uint8_t> bytes) noexcept {
    if (type == ContentType::Png) return widgetrail::encoded_artwork::IsPng(bytes);
    if (type == ContentType::Jpeg) return widgetrail::encoded_artwork::IsJpeg(bytes);
    return type == ContentType::WebP && widgetrail::encoded_artwork::IsWebP(bytes);
}

[[nodiscard]] HRESULT ValidateRequest(
    const SharedHeader& header,
    const std::byte* const view) {
    if (header.magic != protocolMagic || header.version != protocolVersion ||
        header.state != SharedState::Request || header.correlation == 0 ||
        header.encodedBytes == 0 || header.encodedBytes > maximumEncodedBytes ||
        header.maximumDecodedBytes < 4 ||
        header.maximumDecodedBytes > maximumDecodedBytes ||
        header.maximumPixels == 0 || header.maximumPixels > 16'777'216 ||
        header.maximumDimension == 0 || header.maximumDimension > 4'096)
        return E_INVALIDARG;

    const auto encoded = std::span<const std::uint8_t>(
        reinterpret_cast<const std::uint8_t*>(view + encodedOffset),
        static_cast<std::size_t>(header.encodedBytes));
    if (!MatchesSignature(header.contentType, encoded)) return E_INVALIDARG;
    return S_OK;
}

[[nodiscard]] HRESULT Decode(SharedHeader& header, std::byte* const view) {
    const HRESULT admission = ValidateRequest(header, view);
    if (FAILED(admission)) return admission;

    const auto encoded = std::span<const std::uint8_t>(
        reinterpret_cast<const std::uint8_t*>(view + encodedOffset),
        static_cast<std::size_t>(header.encodedBytes));

    ComPtr<IWICImagingFactory> factory;
    HRESULT result = CoCreateInstance(CLSID_WICImagingFactory2, nullptr,
        CLSCTX_INPROC_SERVER, IID_PPV_ARGS(factory.ReleaseAndGetAddressOf()));
    if (FAILED(result))
        result = CoCreateInstance(CLSID_WICImagingFactory, nullptr,
            CLSCTX_INPROC_SERVER, IID_PPV_ARGS(factory.ReleaseAndGetAddressOf()));
    if (FAILED(result)) return result;

    ComPtr<IWICStream> stream;
    ComPtr<IWICBitmapDecoder> decoder;
    ComPtr<IWICBitmapFrameDecode> frame;
    ComPtr<IWICFormatConverter> converter;
    result = factory->CreateStream(stream.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result))
        result = stream->InitializeFromMemory(
            const_cast<BYTE*>(encoded.data()), static_cast<DWORD>(encoded.size()));
    if (SUCCEEDED(result))
        result = factory->CreateDecoderFromStream(stream.Get(), nullptr,
            WICDecodeMetadataCacheOnLoad, decoder.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result)) result = decoder->GetFrame(0, frame.ReleaseAndGetAddressOf());

    UINT width = 0;
    UINT height = 0;
    if (SUCCEEDED(result)) result = frame->GetSize(&width, &height);
    const std::uint64_t stride = static_cast<std::uint64_t>(width) * 4U;
    const std::uint64_t decoded = stride * height;
    if (SUCCEEDED(result) &&
        (width == 0 || height == 0 || width > header.maximumDimension ||
         height > header.maximumDimension ||
         static_cast<std::uint64_t>(width) * height > header.maximumPixels ||
         stride > std::numeric_limits<UINT>::max() ||
         decoded > header.maximumDecodedBytes || decoded > maximumDecodedBytes ||
         decoded > std::numeric_limits<UINT>::max()))
        result = HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE);
    if (SUCCEEDED(result))
        result = factory->CreateFormatConverter(converter.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result))
        result = converter->Initialize(frame.Get(), GUID_WICPixelFormat32bppPBGRA,
            WICBitmapDitherTypeNone, nullptr, 0.0, WICBitmapPaletteTypeCustom);
    if (SUCCEEDED(result))
        result = converter->CopyPixels(nullptr, static_cast<UINT>(stride),
            static_cast<UINT>(decoded),
            reinterpret_cast<BYTE*>(view + decodedOffset));
    if (SUCCEEDED(result)) {
        header.width = width;
        header.height = height;
        header.stride = static_cast<std::uint32_t>(stride);
        header.decodedBytes = static_cast<std::uint32_t>(decoded);
    }
    return result;
}

#ifdef WRAIL_ARTWORK_DECODER_TESTING
[[nodiscard]] HRESULT DecodeForTesting(SharedHeader& header, std::byte* const view) {
    const HRESULT admission = ValidateRequest(header, view);
    if (FAILED(admission)) return admission;
    if (header.testBehavior == TestBehavior::MissingCodec)
        return WINCODEC_ERR_COMPONENTNOTFOUND;
    if (header.testBehavior != TestBehavior::Succeed) return E_INVALIDARG;
    constexpr std::uint8_t pixels[]{
        75, 50, 25, 255,
        150, 125, 100, 255,
    };
    if (header.maximumDimension < 2 || header.maximumPixels < 2 ||
        header.maximumDecodedBytes < sizeof(pixels))
        return HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE);
    std::copy(std::begin(pixels), std::end(pixels),
        reinterpret_cast<std::uint8_t*>(view + decodedOffset));
    header.width = 2;
    header.height = 1;
    header.stride = 8;
    header.decodedBytes = sizeof(pixels);
    return S_OK;
}
#endif

} // namespace

int wmain(const int count, wchar_t** values) {
    const HANDLE mapping = ParseHandle(count, values, L"mapping");
    const HANDLE request = ParseHandle(count, values, L"request");
    const HANDLE response = ParseHandle(count, values, L"response");
    const HANDLE stop = ParseHandle(count, values, L"stop");
    if (!mapping || !request || !response || !stop) return ERROR_INVALID_PARAMETER;
    auto* const view = static_cast<std::byte*>(
        MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, mappingBytes));
    if (!view) return static_cast<int>(GetLastError());
    const HRESULT initialization = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    if (FAILED(initialization) && initialization != RPC_E_CHANGED_MODE) {
        UnmapViewOfFile(view);
        return static_cast<int>(initialization);
    }

    int exitCode = ERROR_SUCCESS;
    for (;;) {
        const HANDLE waits[]{stop, request};
        const DWORD wait = WaitForMultipleObjects(2, waits, FALSE, INFINITE);
        if (wait == WAIT_OBJECT_0) break;
        if (wait != WAIT_OBJECT_0 + 1) {
            exitCode = static_cast<int>(GetLastError());
            break;
        }
        auto& header = *reinterpret_cast<SharedHeader*>(view);
#ifdef WRAIL_ARTWORK_DECODER_TESTING
        if (header.testBehavior == TestBehavior::Hang) {
            (void)WaitForSingleObject(stop, INFINITE);
            break;
        }
        if (header.testBehavior == TestBehavior::Exit) {
            exitCode = ERROR_PROCESS_ABORTED;
            break;
        }
        if (header.testBehavior == TestBehavior::Succeed ||
            header.testBehavior == TestBehavior::MissingCodec)
            header.result = DecodeForTesting(header, view);
        else
#else
        if (header.testBehavior != TestBehavior::Normal)
            header.result = E_INVALIDARG;
        else
#endif
            header.result = Decode(header, view);
        header.state = SharedState::Response;
        if (!SetEvent(response)) {
            exitCode = static_cast<int>(GetLastError());
            break;
        }
    }
    if (SUCCEEDED(initialization)) CoUninitialize();
    UnmapViewOfFile(view);
    return exitCode;
}
