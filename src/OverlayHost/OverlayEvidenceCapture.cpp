#include "DeclarativeRenderer.h"
#include "NativeStyle.h"
#include "WidgetBridgeClient.h"

#include <Windows.h>
#include <d2d1.h>
#include <dwrite.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <winrt/base.h>

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <iterator>
#include <optional>
#include <string>
#include <string_view>

namespace {

using Microsoft::WRL::ComPtr;

struct CaptureArguments final {
    std::filesystem::path input;
    std::filesystem::path output;
    UINT width{880};
    UINT height{520};
    float dpi{96.0F};
    float textScale{1.0F};
    bool reducedTransparency{};
    bool highContrast{};
    std::wstring focusId;
};

[[noreturn]] void Fail(const std::wstring_view message) {
    std::wcerr << L"OverlayEvidenceCapture: " << message << L'\n';
    std::exit(1);
}

void Check(const HRESULT result, const std::wstring_view operation) {
    if (FAILED(result)) {
        Fail(std::wstring(operation) + L" failed (HRESULT 0x" +
             std::to_wstring(static_cast<unsigned long>(result)) + L").");
    }
}

CaptureArguments ParseArguments(const int argc, wchar_t** argv) {
    CaptureArguments result;
    for (int index = 1; index < argc; ++index) {
        const std::wstring_view argument{argv[index]};
        const auto requireValue = [&]() -> std::wstring_view {
            if (++index >= argc) Fail(std::wstring(argument) + L" requires a value.");
            return argv[index];
        };
        if (argument == L"--input") result.input = requireValue();
        else if (argument == L"--output") result.output = requireValue();
        else if (argument == L"--width") result.width = static_cast<UINT>(std::stoul(std::wstring(requireValue())));
        else if (argument == L"--height") result.height = static_cast<UINT>(std::stoul(std::wstring(requireValue())));
        else if (argument == L"--dpi") result.dpi = std::stof(std::wstring(requireValue()));
        else if (argument == L"--text-scale") result.textScale = std::stof(std::wstring(requireValue()));
        else if (argument == L"--focus") result.focusId = requireValue();
        else if (argument == L"--reduced-transparency") result.reducedTransparency = true;
        else if (argument == L"--high-contrast") result.highContrast = true;
        else Fail(L"Unknown argument: " + std::wstring(argument));
    }
    if (result.input.empty() || result.output.empty())
        Fail(L"--input and --output are required.");
    if (result.width < 160 || result.width > 4096 || result.height < 120 || result.height > 2160)
        Fail(L"Capture dimensions are outside the 160x120 through 4096x2160 bound.");
    if (!(result.dpi >= 72.0F && result.dpi <= 384.0F) ||
        !(result.textScale >= 0.85F && result.textScale <= 1.5F))
        Fail(L"DPI or text scale is outside the supported evidence profile bound.");
    return result;
}

std::string ReadUtf8(const std::filesystem::path& path) {
    std::ifstream stream(path, std::ios::binary);
    if (!stream) Fail(L"Could not open snapshot input: " + path.wstring());
    return {std::istreambuf_iterator<char>(stream), std::istreambuf_iterator<char>()};
}

void EncodePng(
    IWICImagingFactory* factory,
    IWICBitmap* bitmap,
    const std::filesystem::path& path,
    const UINT width,
    const UINT height) {
    std::filesystem::create_directories(path.parent_path());
    std::error_code ignored;
    std::filesystem::remove(path, ignored);

    ComPtr<IWICStream> stream;
    Check(factory->CreateStream(stream.ReleaseAndGetAddressOf()), L"Create WIC stream");
    Check(stream->InitializeFromFilename(path.c_str(), GENERIC_WRITE), L"Open PNG output");
    ComPtr<IWICBitmapEncoder> encoder;
    Check(factory->CreateEncoder(
        GUID_ContainerFormatPng, nullptr, encoder.ReleaseAndGetAddressOf()),
        L"Create PNG encoder");
    Check(encoder->Initialize(stream.Get(), WICBitmapEncoderNoCache), L"Initialize PNG encoder");
    ComPtr<IWICBitmapFrameEncode> frame;
    ComPtr<IPropertyBag2> properties;
    Check(encoder->CreateNewFrame(frame.ReleaseAndGetAddressOf(), properties.ReleaseAndGetAddressOf()),
        L"Create PNG frame");
    Check(frame->Initialize(properties.Get()), L"Initialize PNG frame");
    Check(frame->SetSize(width, height), L"Set PNG size");
    ComPtr<IWICFormatConverter> converter;
    Check(factory->CreateFormatConverter(converter.ReleaseAndGetAddressOf()),
        L"Create PNG format converter");
    Check(converter->Initialize(
        bitmap,
        GUID_WICPixelFormat32bppBGRA,
        WICBitmapDitherTypeNone,
        nullptr,
        0.0,
        WICBitmapPaletteTypeCustom),
        L"Convert premultiplied renderer pixels");
    WICPixelFormatGUID format = GUID_WICPixelFormat32bppBGRA;
    Check(frame->SetPixelFormat(&format), L"Set PNG pixel format");
    if (format != GUID_WICPixelFormat32bppBGRA)
        Fail(L"PNG encoder rejected the renderer pixel format.");
    Check(frame->WriteSource(converter.Get(), nullptr), L"Write PNG pixels");
    Check(frame->Commit(), L"Commit PNG frame");
    Check(encoder->Commit(), L"Commit PNG file");
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    try {
        const auto arguments = ParseArguments(argc, argv);
        winrt::init_apartment(winrt::apartment_type::single_threaded);

        std::wstring parseError;
        auto snapshot = gba::testing::ParseWidgetSnapshotResponse(
            ReadUtf8(arguments.input), parseError);
        if (!snapshot) Fail(parseError.empty() ? L"Snapshot parsing failed." : parseError);

        ComPtr<ID2D1Factory> d2d;
        Check(D2D1CreateFactory(
            D2D1_FACTORY_TYPE_SINGLE_THREADED, d2d.ReleaseAndGetAddressOf()),
            L"Create Direct2D factory");
        ComPtr<IDWriteFactory> write;
        Check(DWriteCreateFactory(
            DWRITE_FACTORY_TYPE_SHARED,
            __uuidof(IDWriteFactory),
            reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf())),
            L"Create DirectWrite factory");
        ComPtr<IWICImagingFactory> wic;
        Check(CoCreateInstance(
            CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(wic.ReleaseAndGetAddressOf())),
            L"Create WIC factory");
        ComPtr<IWICBitmap> canvas;
        Check(wic->CreateBitmap(
            arguments.width,
            arguments.height,
            GUID_WICPixelFormat32bppPBGRA,
            WICBitmapCacheOnLoad,
            canvas.ReleaseAndGetAddressOf()),
            L"Create WIC canvas");
        ComPtr<ID2D1RenderTarget> target;
        auto targetProperties = D2D1::RenderTargetProperties();
        targetProperties.dpiX = arguments.dpi;
        targetProperties.dpiY = arguments.dpi;
        Check(d2d->CreateWicBitmapRenderTarget(
            canvas.Get(), targetProperties, target.ReleaseAndGetAddressOf()),
            L"Create WIC render target");

        gba::PlatformAppearance appearance;
        appearance.textScale = arguments.textScale;
        appearance.transparency = arguments.reducedTransparency
            ? gba::PlatformTransparencyPreference::Reduced
            : gba::PlatformTransparencyPreference::Full;
        appearance.contrast = arguments.highContrast
            ? gba::PlatformContrastPreference::High
            : gba::PlatformContrastPreference::Standard;
        appearance.motion = gba::PlatformMotionPreference::Reduced;

        gba::DeclarativeRenderOptions options;
        options.pixelScale = arguments.dpi / 96.0F;
        options.surfaceBackground = gba::NativeColor{0.035F, 0.043F, 0.063F, 1.0F};
        options.surfaceCornerRadiusPx = 18.0F;
        options.animationTimestampMilliseconds = 1'000;
        options.accessibility = gba::CreateNativeAccessibilityPolicy(
            appearance, arguments.highContrast, false);

        const auto focusId = arguments.focusId.empty()
            ? snapshot->initialFocusId
            : arguments.focusId;
        const float logicalWidth = static_cast<float>(arguments.width) * 96.0F / arguments.dpi;
        const float logicalHeight = static_cast<float>(arguments.height) * 96.0F / arguments.dpi;
        gba::DeclarativeRenderer renderer{d2d.Get(), write.Get(), nullptr};
        target->BeginDraw();
        target->Clear(D2D1::ColorF(0.035F, 0.043F, 0.063F, 1.0F));
        const auto render = renderer.Render(
            target.Get(), *snapshot, focusId,
            {0.0F, 0.0F, logicalWidth, logicalHeight}, options);
        Check(target->EndDraw(), L"Complete Direct2D draw");
        if (!render.succeeded) {
            std::wstring diagnostic = L"Production renderer rejected the snapshot";
            if (!render.diagnostics.empty())
                diagnostic += L": " + render.diagnostics.front().message;
            Fail(diagnostic);
        }
        if (!render.diagnostics.empty()) {
            std::wstring diagnostic = L"Unexpected production renderer diagnostic";
            diagnostic += L" [" + render.diagnostics.front().code + L"]: " +
                render.diagnostics.front().message;
            Fail(diagnostic);
        }
        EncodePng(wic.Get(), canvas.Get(), arguments.output, arguments.width, arguments.height);
        std::wcout << L"Captured standalone widget body " << arguments.output.wstring() << L" ("
                   << arguments.width << L"x" << arguments.height << L", "
                   << L"0 renderer diagnostics).\n";
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "OverlayEvidenceCapture: " << exception.what() << '\n';
        return 1;
    } catch (...) {
        std::cerr << "OverlayEvidenceCapture: unexpected native failure.\n";
        return 1;
    }
}
