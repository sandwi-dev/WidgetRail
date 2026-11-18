#pragma once
#include <Windows.h>
#include <d3d11.h>
#include <mfapi.h>
#include <codecapi.h>
#include <winrt/base.h>

namespace widgetrail::capture {
// Encoded color format is separate from the private GPU input layout.
// Unsupported conversion/encoder combinations are rejected by the real probe.
// Explicit native encoder profile. Existing callers without a profile retain
// the legacy H.264/NV12 configuration and ABI.
enum class CaptureVideoCodec : uint32_t { H264=1,Hevc=2,Av1=3 };
enum class CapturePixelFormat : uint32_t { Nv12=1,P010=2,Yuv4448=3,Yuv44410=4 };
enum class CaptureRateControl : uint32_t { ConstantBitrate=1,Quality=2,VariableBitrate=3 };
struct CaptureEncodingProfile {
    CaptureVideoCodec codec{CaptureVideoCodec::H264};
    CapturePixelFormat format{CapturePixelFormat::Nv12};
    CaptureRateControl rateControl{CaptureRateControl::VariableBitrate};
    uint32_t quality{75};
    bool Valid() const noexcept {
        return codec>=CaptureVideoCodec::H264&&codec<=CaptureVideoCodec::Av1&&
            format>=CapturePixelFormat::Nv12&&format<=CapturePixelFormat::Yuv44410&&
            rateControl>=CaptureRateControl::ConstantBitrate&&rateControl<=CaptureRateControl::VariableBitrate&&quality<=100&&
            (codec!=CaptureVideoCodec::H264||format==CapturePixelFormat::Nv12)&&
            (codec!=CaptureVideoCodec::Av1||format==CapturePixelFormat::Nv12||format==CapturePixelFormat::P010);
    }
    GUID OutputSubtype() const {switch(codec){case CaptureVideoCodec::H264:return MFVideoFormat_H264;case CaptureVideoCodec::Hevc:return MFVideoFormat_HEVC;case CaptureVideoCodec::Av1:return MFVideoFormat_AV1;}throw winrt::hresult_error(E_INVALIDARG);}
    GUID InputSubtype() const {switch(format){case CapturePixelFormat::Nv12:return MFVideoFormat_NV12;case CapturePixelFormat::P010:return MFVideoFormat_P010;case CapturePixelFormat::Yuv4448:return MFVideoFormat_AYUV;case CapturePixelFormat::Yuv44410:return MFVideoFormat_Y410;}throw winrt::hresult_error(E_INVALIDARG);}
    DXGI_FORMAT SurfaceFormat() const {switch(format){case CapturePixelFormat::Nv12:return DXGI_FORMAT_NV12;case CapturePixelFormat::P010:return DXGI_FORMAT_P010;case CapturePixelFormat::Yuv4448:return DXGI_FORMAT_AYUV;case CapturePixelFormat::Yuv44410:return DXGI_FORMAT_Y410;}throw winrt::hresult_error(E_INVALIDARG);}
    UINT Stride(UINT width) const {return width*(format==CapturePixelFormat::Nv12?1:format==CapturePixelFormat::P010?2:4);}
    UINT Profile() const {
        if(codec==CaptureVideoCodec::H264)return eAVEncH264VProfile_High;
        if(codec==CaptureVideoCodec::Av1)return format==CapturePixelFormat::P010?eAVEncAV1VProfile_Main_420_10:eAVEncAV1VProfile_Main_420_8;
        switch(format){case CapturePixelFormat::Nv12:return eAVEncH265VProfile_Main_420_8;case CapturePixelFormat::P010:return eAVEncH265VProfile_Main_420_10;
            case CapturePixelFormat::Yuv4448:return eAVEncH265VProfile_Main_444_8;case CapturePixelFormat::Yuv44410:return eAVEncH265VProfile_Main_444_10;}
        throw winrt::hresult_error(E_INVALIDARG);
    }
};
}
