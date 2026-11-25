#pragma once
#include <Windows.h>
#include <d2d1_1.h>
#include <d2d1effects_2.h>
#include <dxgi1_6.h>
#include <winrt/Windows.Graphics.DirectX.h>
#include <wrl/client.h>
#include <algorithm>
#include <chrono>
#include <cmath>
#include <vector>
#pragma comment(lib, "dxguid.lib")

namespace widgetrail::capture {
// WGC supplies linear scRGB to an FP16 pool. One scRGB unit represents 80 nits.
// Our encoded files use SDR RGB primaries and a limited-range YUV conversion.
class CaptureSdrColor {
    using Clock = std::chrono::steady_clock;
    template<typename T> using Ptr = Microsoft::WRL::ComPtr<T>;
    Ptr<IDXGIFactory1> factory;
    Ptr<IDXGIOutput6> output;
    HMONITOR monitor{};
    Clock::time_point nextCheck{};
    DISPLAYCONFIG_SDR_WHITE_LEVEL desktopWhite{};
    float peak{}, white{};
    Ptr<ID2D1Effect> tone, normalize, encode;

    void FindOutput(HMONITOR target) {
        output.Reset();
        desktopWhite = {};
        if (!factory || !factory->IsCurrent()) {
            factory.Reset();
            winrt::check_hresult(CreateDXGIFactory1(IID_PPV_ARGS(&factory)));
        }
        for (UINT a = 0; ; ++a) {
            Ptr<IDXGIAdapter1> adapter;
            if (factory->EnumAdapters1(a, &adapter) != S_OK) break;
            for (UINT i = 0; ; ++i) {
                Ptr<IDXGIOutput> candidate;
                if (adapter->EnumOutputs(i, &candidate) != S_OK) break;
                DXGI_OUTPUT_DESC desc{};
                if (SUCCEEDED(candidate->GetDesc(&desc)) && desc.Monitor == target) {
                    (void)candidate.As(&output);
                    FindWhiteLevel(desc.DeviceName);
                    return;
                }
            }
        }
    }

    void FindWhiteLevel(const wchar_t* sourceName) {
        UINT32 pathCount{}, modeCount{};
        if (GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &pathCount, &modeCount) != ERROR_SUCCESS) return;
        std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
        std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);
        if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &pathCount, paths.data(), &modeCount, modes.data(), nullptr) != ERROR_SUCCESS) return;
        for (UINT32 i = 0; i < pathCount; ++i) {
            const auto& path = paths[i];
            DISPLAYCONFIG_SOURCE_DEVICE_NAME source{};
            source.header = {DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, sizeof(source), path.sourceInfo.adapterId, path.sourceInfo.id};
            if (DisplayConfigGetDeviceInfo(&source.header) == ERROR_SUCCESS && _wcsicmp(source.viewGdiDeviceName, sourceName) == 0) {
                desktopWhite.header = {DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL, sizeof(desktopWhite), path.targetInfo.adapterId, path.targetInfo.id};
                return;
            }
        }
    }

public:
    bool Hdr() const { return peak > 0; }
    auto PoolFormat() const {
        using winrt::Windows::Graphics::DirectX::DirectXPixelFormat;
        return Hdr() ? DirectXPixelFormat::R16G16B16A16Float : DirectXPixelFormat::B8G8R8A8UIntNormalized;
    }

    // A codec session can outlive a move to another display or a change in HDR mode.
    // Only a pixel-format transition requires replacing the WGC frame pool.
    bool Update(HWND window) {
        const auto now = Clock::now();
        if (now < nextCheck) return false;
        nextCheck = now + std::chrono::seconds(1);
        const auto target = MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST);
        if (!output || target != monitor || !factory->IsCurrent()) {
            FindOutput(target);
            monitor = target;
        }
        DXGI_OUTPUT_DESC1 desc{};
        const bool hdr = output && SUCCEEDED(output->GetDesc1(&desc)) &&
            desc.ColorSpace == DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020;
        // The compositor supplies no per-frame mastering metadata. Use the source
        // display's reported peak and the Windows SDR-content brightness setting.
        const float newPeak = hdr ? (std::isfinite(desc.MaxLuminance) && desc.MaxLuminance > 0 ?
            std::min(desc.MaxLuminance, 10000.f) : 1000.f) : 0.f;
        float newWhite = 80.f;
        if (hdr && desktopWhite.header.size && DisplayConfigGetDeviceInfo(&desktopWhite.header) == ERROR_SUCCESS)
            newWhite = std::clamp(80.f * desktopWhite.SDRWhiteLevel / 1000.f, 80.f, 1000.f);
        return Configure(newPeak, newWhite);
    }

    bool Configure(float newPeak, float newWhite) {
        const bool wasHdr = Hdr();
        if (peak != newPeak || white != newWhite) {
            peak = newPeak;
            white = newWhite;
            encode.Reset(); normalize.Reset(); tone.Reset();
        }
        return Hdr() != wasHdr;
    }

    void Draw(ID2D1DeviceContext* dc, IDXGISurface* surface, D2D1_RECT_F source, D2D1_RECT_F dest) {
        auto props = D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_NONE, D2D1::PixelFormat(
            Hdr() ? DXGI_FORMAT_R16G16B16A16_FLOAT : DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED), 96, 96);
        Ptr<ID2D1Bitmap1> bitmap;
        winrt::check_hresult(dc->CreateBitmapFromDxgiSurface(surface, &props, &bitmap));
        if (!Hdr()) {
            dc->DrawBitmap(bitmap.Get(), dest, 1.f, D2D1_INTERPOLATION_MODE_LINEAR, source);
            return;
        }
        if (!encode) {
            winrt::check_hresult(dc->CreateEffect(CLSID_D2D1HdrToneMap, &tone));
            winrt::check_hresult(tone->SetValue(D2D1_HDRTONEMAP_PROP_DISPLAY_MODE, D2D1_HDRTONEMAP_DISPLAY_MODE_SDR));
            winrt::check_hresult(tone->SetValue(D2D1_HDRTONEMAP_PROP_INPUT_MAX_LUMINANCE, std::max(peak, white)));
            winrt::check_hresult(tone->SetValue(D2D1_HDRTONEMAP_PROP_OUTPUT_MAX_LUMINANCE, white));
            winrt::check_hresult(tone->SetValue(D2D1_PROPERTY_PRECISION, D2D1_BUFFER_PRECISION_16BPC_FLOAT));
            winrt::check_hresult(dc->CreateEffect(CLSID_D2D1WhiteLevelAdjustment, &normalize));
            normalize->SetInputEffect(0, tone.Get());
            // The effect multiplies by InputWhiteLevel / OutputWhiteLevel.
            // An absolute scRGB value of white / 80 becomes 1 before sRGB encoding.
            winrt::check_hresult(normalize->SetValue(D2D1_WHITELEVELADJUSTMENT_PROP_INPUT_WHITE_LEVEL, 80.f));
            winrt::check_hresult(normalize->SetValue(D2D1_WHITELEVELADJUSTMENT_PROP_OUTPUT_WHITE_LEVEL, white));
            winrt::check_hresult(normalize->SetValue(D2D1_PROPERTY_PRECISION, D2D1_BUFFER_PRECISION_16BPC_FLOAT));
            Ptr<ID2D1ColorContext> linear, srgb;
            winrt::check_hresult(dc->CreateColorContext(D2D1_COLOR_SPACE_SCRGB, nullptr, 0, &linear));
            winrt::check_hresult(dc->CreateColorContext(D2D1_COLOR_SPACE_SRGB, nullptr, 0, &srgb));
            winrt::check_hresult(dc->CreateEffect(CLSID_D2D1ColorManagement, &encode));
            winrt::check_hresult(encode->SetValue(D2D1_COLORMANAGEMENT_PROP_SOURCE_COLOR_CONTEXT, linear.Get()));
            winrt::check_hresult(encode->SetValue(D2D1_COLORMANAGEMENT_PROP_DESTINATION_COLOR_CONTEXT, srgb.Get()));
            encode->SetInputEffect(0, normalize.Get());
        }
        tone->SetInput(0, bitmap.Get());
        D2D1_MATRIX_3X2_F previous{};
        dc->GetTransform(&previous);
        dc->SetTransform(D2D1::Matrix3x2F::Scale((dest.right-dest.left)/(source.right-source.left),
            (dest.bottom-dest.top)/(source.bottom-source.top)) * D2D1::Matrix3x2F::Translation(dest.left, dest.top));
        dc->DrawImage(encode.Get(), D2D1::Point2F(0,0), source, D2D1_INTERPOLATION_MODE_LINEAR);
        dc->SetTransform(previous);
    }

    // Release the acquired frame after EndDraw has submitted the graph.
    void ReleaseInput() { if (tone) tone->SetInput(0, nullptr); }
};
}
