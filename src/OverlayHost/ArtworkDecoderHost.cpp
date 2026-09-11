#include "ArtworkDecoderProtocol.h"
#include "ImageDecodeSize.h"
#include "EncodedArtworkEnvelope.h"

#include <Windows.h>
#include <d2d1_3.h>
#include <d3d11_4.h>
#include <dxgi1_2.h>
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
    if (type == ContentType::WebP) return widgetrail::encoded_artwork::IsWebP(bytes);
    if (type != ContentType::Svg) return false;
    constexpr std::string_view signature{"<svg"};
    return bytes.size() >= signature.size() &&
        std::equal(signature.begin(), signature.end(), bytes.begin());
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
        header.maximumDimension == 0 || header.maximumDimension > 4'096 ||
        (header.contentType == ContentType::Svg &&
            (header.requestedWidth == 0 || header.requestedHeight == 0 ||
             header.requestedWidth > 512 || header.requestedHeight > 512)) ||
        (header.contentType != ContentType::Svg &&
            (!widgetrail::ImageDecodeSize{header.requestedWidth, header.requestedHeight}.valid() ||
             header.rasterVariant != RasterVariant::OriginalColor)))
        return E_INVALIDARG;

    const auto encoded = std::span<const std::uint8_t>(
        reinterpret_cast<const std::uint8_t*>(view + encodedOffset),
        static_cast<std::size_t>(header.encodedBytes));
    if (!MatchesSignature(header.contentType, encoded)) return E_INVALIDARG;
    return S_OK;
}

[[nodiscard]] HRESULT DecodeSvg(SharedHeader& header, std::byte* const view) {
    const auto encoded = std::span<const std::uint8_t>(
        reinterpret_cast<const std::uint8_t*>(view + encodedOffset),
        static_cast<std::size_t>(header.encodedBytes));
    ComPtr<IWICImagingFactory> wic;
    HRESULT result = CoCreateInstance(CLSID_WICImagingFactory2, nullptr,
        CLSCTX_INPROC_SERVER, IID_PPV_ARGS(wic.ReleaseAndGetAddressOf()));
    if (FAILED(result)) return result;
    ComPtr<IWICStream> stream;
    result = wic->CreateStream(stream.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result)) result = stream->InitializeFromMemory(
        const_cast<BYTE*>(encoded.data()), static_cast<DWORD>(encoded.size()));

    ComPtr<ID3D11Device> d3d;
    D3D_FEATURE_LEVEL feature{};
    if (SUCCEEDED(result)) result = D3D11CreateDevice(
        nullptr, D3D_DRIVER_TYPE_WARP, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT,
        nullptr, 0, D3D11_SDK_VERSION, d3d.ReleaseAndGetAddressOf(), &feature, nullptr);
    ComPtr<IDXGIDevice> dxgi;
    if (SUCCEEDED(result)) result = d3d.As(&dxgi);
    D2D1_FACTORY_OPTIONS factoryOptions{};
    ComPtr<ID2D1Factory1> factory;
    if (SUCCEEDED(result)) result = D2D1CreateFactory(
        D2D1_FACTORY_TYPE_SINGLE_THREADED, __uuidof(ID2D1Factory1),
        &factoryOptions, reinterpret_cast<void**>(factory.ReleaseAndGetAddressOf()));
    ComPtr<ID2D1Device> device;
    if (SUCCEEDED(result)) result = factory->CreateDevice(
        dxgi.Get(), device.ReleaseAndGetAddressOf());
    ComPtr<ID2D1DeviceContext> baseContext;
    if (SUCCEEDED(result)) result = device->CreateDeviceContext(
        D2D1_DEVICE_CONTEXT_OPTIONS_NONE, baseContext.ReleaseAndGetAddressOf());
    ComPtr<ID2D1DeviceContext5> context;
    if (SUCCEEDED(result)) result = baseContext.As(&context);

    const D2D1_SIZE_U pixelSize{header.requestedWidth, header.requestedHeight};
    const D2D1_BITMAP_PROPERTIES1 targetProperties{
        {DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED},
        96.0F, 96.0F, D2D1_BITMAP_OPTIONS_TARGET, nullptr};
    ComPtr<ID2D1Bitmap1> target;
    if (SUCCEEDED(result)) result = context->CreateBitmap(
        pixelSize, nullptr, 0, &targetProperties, target.ReleaseAndGetAddressOf());
    ComPtr<ID2D1SvgDocument> document;
    if (SUCCEEDED(result)) result = context->CreateSvgDocument(
        stream.Get(), D2D1::SizeF(
            static_cast<float>(header.requestedWidth),
            static_cast<float>(header.requestedHeight)),
        document.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result)) {
        context->SetTarget(target.Get());
        context->BeginDraw();
        context->Clear(D2D1::ColorF(0, 0.0F));
        context->DrawSvgDocument(document.Get());
        result = context->EndDraw();
    }

    const D2D1_BITMAP_PROPERTIES1 readProperties{
        {DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED},
        96.0F, 96.0F,
        D2D1_BITMAP_OPTIONS_CPU_READ | D2D1_BITMAP_OPTIONS_CANNOT_DRAW, nullptr};
    ComPtr<ID2D1Bitmap1> readable;
    if (SUCCEEDED(result)) result = context->CreateBitmap(
        pixelSize, nullptr, 0, &readProperties, readable.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result)) result = readable->CopyFromBitmap(nullptr, target.Get(), nullptr);
    D2D1_MAPPED_RECT mapped{};
    if (SUCCEEDED(result)) result = readable->Map(D2D1_MAP_OPTIONS_READ, &mapped);
    const std::uint64_t stride = static_cast<std::uint64_t>(header.requestedWidth) * 4U;
    const std::uint64_t decoded = stride * header.requestedHeight;
    if (SUCCEEDED(result) &&
        (decoded > header.maximumDecodedBytes || decoded > maximumDecodedBytes ||
         decoded > std::numeric_limits<std::uint32_t>::max()))
        result = HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE);
    if (SUCCEEDED(result)) {
        auto* destination = reinterpret_cast<std::uint8_t*>(view + decodedOffset);
        for (UINT32 row = 0; row < header.requestedHeight; ++row) {
            std::copy_n(mapped.bits + static_cast<std::size_t>(row) * mapped.pitch,
                static_cast<std::size_t>(stride),
                destination + static_cast<std::size_t>(row) * stride);
        }
        if (header.rasterVariant == RasterVariant::AlphaMask) {
            for (std::size_t offset = 0; offset < decoded; offset += 4) {
                const auto alpha = destination[offset + 3];
                destination[offset] = alpha;
                destination[offset + 1] = alpha;
                destination[offset + 2] = alpha;
            }
        }
        header.width = header.requestedWidth;
        header.height = header.requestedHeight;
        header.stride = static_cast<std::uint32_t>(stride);
        header.decodedBytes = static_cast<std::uint32_t>(decoded);
    }
    if (mapped.bits) readable->Unmap();
    return result;
}

[[nodiscard]] HRESULT Decode(SharedHeader& header, std::byte* const view) {
    const HRESULT admission = ValidateRequest(header, view);
    if (FAILED(admission)) return admission;
    if (header.contentType == ContentType::Svg) return DecodeSvg(header, view);

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
    ComPtr<IWICBitmapScaler> scaler;
    const auto output = widgetrail::FitDecodedImage(width, height, {header.requestedWidth, header.requestedHeight});
    if (SUCCEEDED(result) && (output.width != width || output.height != height)) {
        result = factory->CreateBitmapScaler(scaler.ReleaseAndGetAddressOf());
        if (SUCCEEDED(result)) result = scaler->Initialize(frame.Get(), output.width, output.height, WICBitmapInterpolationModeFant);
        width = output.width; height = output.height;
    }
    if (SUCCEEDED(result))
        result = factory->CreateFormatConverter(converter.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result))
        result = converter->Initialize(scaler ? static_cast<IWICBitmapSource*>(scaler.Get()) : frame.Get(), GUID_WICPixelFormat32bppPBGRA,
            WICBitmapDitherTypeNone, nullptr, 0.0, WICBitmapPaletteTypeCustom);
    if (SUCCEEDED(result))
        result = converter->CopyPixels(nullptr, width * 4,
            width * height * 4,
            reinterpret_cast<BYTE*>(view + decodedOffset));
    if (SUCCEEDED(result)) {
        header.width = width;
        header.height = height;
        header.stride = width * 4;
        header.decodedBytes = width * height * 4;
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
