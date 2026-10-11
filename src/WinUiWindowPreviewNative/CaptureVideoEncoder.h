#pragma once
#include "WindowContextCapture.h"
#include <d3d11_4.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mftransform.h>
#include <mferror.h>
#include <wrl.h>
#include <winrt/base.h>
#include <winrt/Windows.Storage.Streams.h>
#include <array>
#include <atomic>
#include <memory>
#include <vector>

namespace widgetrail::capture {
using Microsoft::WRL::ComPtr;

// Three encoder-owned surfaces put a hard limit on queued GPU frames. A surface
// returns only after Media Foundation releases its tracked sample.
struct EncoderSurfaces {
    std::array<ComPtr<ID3D11Texture2D>, 3> textures;
    std::array<std::atomic_bool, 3> busy{};
};
class ReturnEncoderSurface final : public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>, IMFAsyncCallback> {
    std::shared_ptr<EncoderSurfaces> surfaces;
    size_t index;
public:
    ReturnEncoderSurface(std::shared_ptr<EncoderSurfaces> owner, size_t slot) : surfaces(std::move(owner)), index(slot) {}
    IFACEMETHOD(GetParameters)(DWORD*, DWORD*) override { return E_NOTIMPL; }
    IFACEMETHOD(Invoke)(IMFAsyncResult*) override { surfaces->busy[index].store(false, std::memory_order_release); return S_OK; }
};

class VideoEncoder {
    inline static thread_local const wchar_t* phase=L"idle";
    ComPtr<IMFSinkWriter> writer;
    ComPtr<IMFDXGIDeviceManager> manager;
    ComPtr<ID3D11VideoDevice> videoDevice;
    ComPtr<ID3D11VideoContext> videoContext;
    ComPtr<ID3D11DeviceContext> immediate;
    ComPtr<ID3D11VideoProcessorEnumerator> enumerator;
    ComPtr<ID3D11VideoProcessor> processor;
    ComPtr<ID3D11VideoProcessorInputView> inputView;
    std::array<ComPtr<ID3D11VideoProcessorOutputView>, 3> outputViews;
    std::shared_ptr<EncoderSurfaces> surfaces;
    DWORD stream{};
    UINT fps{};
    bool hardware{}, started{};

    static bool HardwareEncoder(IMFSinkWriter* sink) {
        ComPtr<IMFSinkWriterEx> extended;
        if(FAILED(sink->QueryInterface(IID_PPV_ARGS(&extended))))return false;
        for(DWORD i=0;i<8;i++) {
            GUID category{};ComPtr<IMFTransform> transform;
            if(FAILED(extended->GetTransformForStream(0,i,&category,&transform)))break;
            if(category!=MFT_CATEGORY_VIDEO_ENCODER)continue;
            ComPtr<IMFAttributes> attributes;
            if(FAILED(transform->GetAttributes(&attributes)))return false;
            UINT32 length{};
            // This is the explicit hardware identity provided by the MFT, not
            // an inference from codec name, GPU load, or an enable-hardware flag.
            return SUCCEEDED(attributes->GetStringLength(MFT_ENUM_HARDWARE_URL_Attribute,&length)) && length>0;
        }
        return false;
    }
    void InitializeGpu(ID3D11Device* device, UINT width, UINT height, bool allocateSurfaces) {
        if(!device)throw winrt::hresult_error(E_INVALIDARG);
        device->GetImmediateContext(&immediate);
        ComPtr<ID3D11Multithread> protection;
        winrt::check_hresult(immediate.As(&protection));protection->SetMultithreadProtected(TRUE);
        UINT token{};winrt::check_hresult(MFCreateDXGIDeviceManager(&token,&manager));
        winrt::check_hresult(manager->ResetDevice(device,token));
        winrt::check_hresult(device->QueryInterface(IID_PPV_ARGS(&videoDevice)));
        winrt::check_hresult(immediate.As(&videoContext));
        D3D11_VIDEO_PROCESSOR_CONTENT_DESC content{};
        content.InputFrameFormat=D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
        content.InputWidth=content.OutputWidth=width;content.InputHeight=content.OutputHeight=height;
        content.InputFrameRate={fps,1};content.OutputFrameRate={fps,1};content.Usage=D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
        winrt::check_hresult(videoDevice->CreateVideoProcessorEnumerator(&content,&enumerator));
        UINT flags{};winrt::check_hresult(enumerator->CheckVideoProcessorFormat(DXGI_FORMAT_NV12,&flags));
        if(!(flags&D3D11_VIDEO_PROCESSOR_FORMAT_SUPPORT_OUTPUT))throw winrt::hresult_error(MF_E_INVALIDMEDIATYPE);
        winrt::check_hresult(videoDevice->CreateVideoProcessor(enumerator.Get(),0,&processor));
        D3D11_VIDEO_PROCESSOR_COLOR_SPACE rgb{};rgb.Nominal_Range=D3D11_VIDEO_PROCESSOR_NOMINAL_RANGE_0_255;
        D3D11_VIDEO_PROCESSOR_COLOR_SPACE yuv{};yuv.YCbCr_Matrix=1;yuv.Nominal_Range=D3D11_VIDEO_PROCESSOR_NOMINAL_RANGE_16_235;
        videoContext->VideoProcessorSetStreamColorSpace(processor.Get(),0,&rgb);
        videoContext->VideoProcessorSetOutputColorSpace(processor.Get(),&yuv);
        RECT bounds{0,0,static_cast<LONG>(width),static_cast<LONG>(height)};
        videoContext->VideoProcessorSetStreamFrameFormat(processor.Get(),0,D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE);
        videoContext->VideoProcessorSetStreamSourceRect(processor.Get(),0,TRUE,&bounds);
        videoContext->VideoProcessorSetStreamDestRect(processor.Get(),0,TRUE,&bounds);
        videoContext->VideoProcessorSetOutputTargetRect(processor.Get(),TRUE,&bounds);
        videoContext->VideoProcessorSetStreamAutoProcessingMode(processor.Get(),0,FALSE);
        if(!allocateSurfaces)return;
        surfaces=std::make_shared<EncoderSurfaces>();
        D3D11_TEXTURE2D_DESC desc{};desc.Width=width;desc.Height=height;desc.MipLevels=1;desc.ArraySize=1;
        desc.Format=DXGI_FORMAT_NV12;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET;
        D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC view{};view.ViewDimension=D3D11_VPOV_DIMENSION_TEXTURE2D;
        for(size_t i=0;i<surfaces->textures.size();i++) {
            winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&surfaces->textures[i]));
            winrt::check_hresult(videoDevice->CreateVideoProcessorOutputView(surfaces->textures[i].Get(),enumerator.Get(),&view,&outputViews[i]));
        }
    }
    void Timestamp(IMFSample* sample, UINT index) const {
        const LONGLONG start=10000000LL*index/fps,end=10000000LL*(index+1)/fps;
        winrt::check_hresult(sample->SetSampleTime(start));winrt::check_hresult(sample->SetSampleDuration(end-start));
    }
public:
    static const wchar_t* LastStage() noexcept{return phase;}
    // A null path probes initialization into a tiny in-memory MP4, without
    // capturing a screen or creating a persistent file.
    VideoEncoder(const wchar_t* path, UINT width, UINT height, const WrailCaptureOptions& options, ID3D11Device* device=nullptr)
        : fps(options.framesPerSecond), hardware(options.encoder==2) {
        phase=L"MFStartup";winrt::check_hresult(MFStartup(MF_VERSION));started=true;
        try {
            phase=L"InitializeGpu";
            if(hardware)InitializeGpu(device,width,height,path!=nullptr);
            ComPtr<IMFAttributes> attributes;winrt::check_hresult(MFCreateAttributes(&attributes,6));
            winrt::check_hresult(attributes->SetUINT32(MF_SINK_WRITER_DISABLE_THROTTLING,FALSE));
            winrt::check_hresult(attributes->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS,hardware));
            // Media Foundation forbids DISABLE_CONVERTERS when hardware transforms
            // are enabled. NV12 is supplied directly; verify the chosen MFT below.
            // https://learn.microsoft.com/windows/win32/medfound/mf-readwrite-enable-hardware-transforms
            if(hardware)winrt::check_hresult(attributes->SetUINT32(MF_LOW_LATENCY,TRUE));
            if(hardware)winrt::check_hresult(attributes->SetUnknown(MF_SINK_WRITER_D3D_MANAGER,manager.Get()));
            winrt::Windows::Storage::Streams::InMemoryRandomAccessStream memory{nullptr};ComPtr<IMFByteStream> bytes;
            if(!path) {
                memory=winrt::Windows::Storage::Streams::InMemoryRandomAccessStream();
                winrt::check_hresult(MFCreateMFByteStreamOnStreamEx(reinterpret_cast<IUnknown*>(winrt::get_abi(memory)),&bytes));
                winrt::check_hresult(attributes->SetGUID(MF_TRANSCODE_CONTAINERTYPE,MFTranscodeContainerType_MPEG4));
                ComPtr<IMFAttributes> byteAttributes;winrt::check_hresult(bytes.As(&byteAttributes));
                winrt::check_hresult(byteAttributes->SetString(MF_BYTESTREAM_CONTENT_TYPE,L"video/mp4"));
            }
            phase=L"CreateSinkWriter";
            winrt::check_hresult(MFCreateSinkWriterFromURL(path,bytes.Get(),attributes.Get(),&writer));
            ComPtr<IMFMediaType> output;winrt::check_hresult(MFCreateMediaType(&output));
            winrt::check_hresult(output->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video));
            winrt::check_hresult(output->SetGUID(MF_MT_SUBTYPE,MFVideoFormat_H264));
            winrt::check_hresult(output->SetUINT32(MF_MT_AVG_BITRATE,options.bitrate));
            winrt::check_hresult(output->SetUINT32(MF_MT_INTERLACE_MODE,MFVideoInterlace_Progressive));
            winrt::check_hresult(MFSetAttributeSize(output.Get(),MF_MT_FRAME_SIZE,width,height));
            winrt::check_hresult(MFSetAttributeRatio(output.Get(),MF_MT_FRAME_RATE,fps,1));
            winrt::check_hresult(MFSetAttributeRatio(output.Get(),MF_MT_PIXEL_ASPECT_RATIO,1,1));
            phase=L"AddStream";winrt::check_hresult(writer->AddStream(output.Get(),&stream));
            ComPtr<IMFMediaType> input;winrt::check_hresult(MFCreateMediaType(&input));
            winrt::check_hresult(input->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video));
            winrt::check_hresult(input->SetGUID(MF_MT_SUBTYPE,hardware?MFVideoFormat_NV12:MFVideoFormat_RGB32));
            winrt::check_hresult(input->SetUINT32(MF_MT_INTERLACE_MODE,MFVideoInterlace_Progressive));
            winrt::check_hresult(MFSetAttributeSize(input.Get(),MF_MT_FRAME_SIZE,width,height));
            winrt::check_hresult(MFSetAttributeRatio(input.Get(),MF_MT_FRAME_RATE,fps,1));
            winrt::check_hresult(MFSetAttributeRatio(input.Get(),MF_MT_PIXEL_ASPECT_RATIO,1,1));
            winrt::check_hresult(input->SetUINT32(MF_MT_DEFAULT_STRIDE,hardware?width:width*4));
            if(hardware) {
                winrt::check_hresult(input->SetUINT32(MF_MT_VIDEO_PRIMARIES,MFVideoPrimaries_BT709));
                winrt::check_hresult(input->SetUINT32(MF_MT_YUV_MATRIX,MFVideoTransferMatrix_BT709));
                winrt::check_hresult(input->SetUINT32(MF_MT_VIDEO_NOMINAL_RANGE,MFNominalRange_16_235));
            }
            phase=L"SetInputMediaType";
            winrt::check_hresult(writer->SetInputMediaType(stream,input.Get(),nullptr));
            phase=L"BeginWriting";
            winrt::check_hresult(writer->BeginWriting());
            phase=L"VerifyHardware";
            if(hardware && !HardwareEncoder(writer.Get()))throw winrt::hresult_error(MF_E_TOPO_CODEC_NOT_FOUND);
            phase=L"ready";
        } catch(...) {writer.Reset();MFShutdown();started=false;throw;}
    }
    ~VideoEncoder(){writer.Reset();if(started)MFShutdown();}
    VideoEncoder(const VideoEncoder&)=delete;
    VideoEncoder& operator=(const VideoEncoder&)=delete;
    void Write(const std::vector<BYTE>& bytes, UINT index) {
        if(hardware)throw winrt::hresult_error(E_INVALIDARG);
        ComPtr<IMFMediaBuffer> buffer;winrt::check_hresult(MFCreateMemoryBuffer(static_cast<DWORD>(bytes.size()),&buffer));
        BYTE* target{};winrt::check_hresult(buffer->Lock(&target,nullptr,nullptr));memcpy(target,bytes.data(),bytes.size());
        winrt::check_hresult(buffer->Unlock());winrt::check_hresult(buffer->SetCurrentLength(static_cast<DWORD>(bytes.size())));
        ComPtr<IMFSample> sample;winrt::check_hresult(MFCreateSample(&sample));winrt::check_hresult(sample->AddBuffer(buffer.Get()));
        Timestamp(sample.Get(),index);winrt::check_hresult(writer->WriteSample(stream,sample.Get()));
    }
    bool TryWriteTexture(ID3D11Texture2D* texture, UINT index) {
        if(!hardware || !texture || !surfaces)throw winrt::hresult_error(E_INVALIDARG);
        size_t slot=0;
        for(;slot<surfaces->textures.size();slot++) {bool available=false;if(surfaces->busy[slot].compare_exchange_strong(available,true))break;}
        if(slot==surfaces->textures.size())return false;
        bool allocatorOwns=false;
        try {
            if(!inputView) {
                D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC view{};view.ViewDimension=D3D11_VPIV_DIMENSION_TEXTURE2D;
                winrt::check_hresult(videoDevice->CreateVideoProcessorInputView(texture,enumerator.Get(),&view,&inputView));
            }
            D3D11_VIDEO_PROCESSOR_STREAM input{};input.Enable=TRUE;input.pInputSurface=inputView.Get();
            winrt::check_hresult(videoContext->VideoProcessorBlt(processor.Get(),outputViews[slot].Get(),index,1,&input));
            immediate->Flush();
            ComPtr<IMFMediaBuffer> buffer;winrt::check_hresult(MFCreateDXGISurfaceBuffer(__uuidof(ID3D11Texture2D),surfaces->textures[slot].Get(),0,FALSE,&buffer));
            ComPtr<IMF2DBuffer> image;winrt::check_hresult(buffer.As(&image));DWORD length{};
            winrt::check_hresult(image->GetContiguousLength(&length));winrt::check_hresult(buffer->SetCurrentLength(length));
            ComPtr<IMFTrackedSample> tracked;winrt::check_hresult(MFCreateTrackedSample(&tracked));
            ComPtr<IMFSample> sample;winrt::check_hresult(tracked.As(&sample));
            winrt::check_hresult(sample->AddBuffer(buffer.Get()));Timestamp(sample.Get(),index);
            auto release=Microsoft::WRL::Make<ReturnEncoderSurface>(surfaces,slot);
            if(!release)throw winrt::hresult_error(E_OUTOFMEMORY);
            winrt::check_hresult(tracked->SetAllocator(release.Get(),nullptr));
            allocatorOwns=true;
            winrt::check_hresult(writer->WriteSample(stream,sample.Get()));
            return true;
        } catch(...) {if(!allocatorOwns)surfaces->busy[slot]=false;throw;}
    }
    void Finish(){winrt::check_hresult(writer->Finalize());}
    bool Hardware() const noexcept{return hardware;}
};
}
