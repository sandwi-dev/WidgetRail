#pragma once
#include "WindowContextCapture.h"
#include "CaptureEncodingProfile.h"
#include <d3d11_4.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mftransform.h>
#include <mferror.h>
#include <codecapi.h>
#include <icodecapi.h>
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
    ComPtr<IMFSinkWriter> encodedAudioWriter;
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
    DWORD audioStream{};
    uint64_t audioFrames{};
    UINT fps{},encoderWidth{},encoderHeight{};
    bool hardware{}, started{}, audioEnabled{},explicitProfile{};
    CaptureEncodingProfile profile;
    bool replayGopConfigured{},replayBFramesDisabled{};
    ComPtr<ICodecAPI> replayKeyframeCodec;
    UINT replayKeyframeIndex{};bool replayKeyframeRequested{};

    void ConfigureReplayCadence() noexcept {
        ComPtr<IMFSinkWriterEx> extended;if(FAILED(writer.As(&extended)))return;
        for(DWORD i=0;i<8;i++) {
            GUID category{};ComPtr<IMFTransform> transform;
            if(FAILED(extended->GetTransformForStream(stream,i,&category,&transform)))break;
            if(category!=MFT_CATEGORY_VIDEO_ENCODER)continue;
            ComPtr<ICodecAPI> codec;if(FAILED(transform.As(&codec)))return;
            const auto set=[&](const GUID& property,UINT32 value){
                VARIANT setting{};setting.vt=VT_UI4;setting.ulVal=value;
                return codec->IsSupported(&property)==S_OK && SUCCEEDED(codec->SetValue(&property,&setting));
            };
            replayGopConfigured=set(CODECAPI_AVEncMPVGOPSize,fps);
            replayBFramesDisabled=set(CODECAPI_AVEncMPVDefaultBPictureCount,0);
            if(codec->IsSupported(&CODECAPI_AVEncVideoForceKeyFrame)==S_OK)replayKeyframeCodec=codec;
            // Optional codec controls are not a guarantee: the replay buffer
            // independently rejects reordering and excessive keyframe age.
            return;
        }
    }

    ComPtr<IMFTransform> EncoderTransform() const {
        ComPtr<IMFSinkWriterEx> extended;winrt::check_hresult(writer.As(&extended));
        for(DWORD index=0;index<8;++index){
            GUID category{};ComPtr<IMFTransform> transform;
            if(FAILED(extended->GetTransformForStream(stream,index,&category,&transform)))break;
            if(category==MFT_CATEGORY_VIDEO_ENCODER)return transform;
        }
        throw winrt::hresult_error(MF_E_TOPO_CODEC_NOT_FOUND);
    }
    void ConfigureProfile(UINT bitrate) {
        auto transform=EncoderTransform();ComPtr<ICodecAPI> codec;winrt::check_hresult(transform.As(&codec));
        const auto set=[&](const GUID& property,UINT32 value){
            if(codec->IsSupported(&property)!=S_OK)throw winrt::hresult_error(MF_E_UNSUPPORTED_RATE);
            VARIANT setting{};setting.vt=VT_UI4;setting.ulVal=value;winrt::check_hresult(codec->SetValue(&property,&setting));
            VARIANT actual{};winrt::check_hresult(codec->GetValue(&property,&actual));
            const auto matches=actual.vt==VT_UI4&&actual.ulVal==value;VariantClear(&actual);
            if(!matches)throw winrt::hresult_error(MF_E_INVALIDMEDIATYPE);
        };
        const auto control=profile.rateControl==CaptureRateControl::ConstantBitrate?eAVEncCommonRateControlMode_CBR:
            profile.rateControl==CaptureRateControl::Quality?eAVEncCommonRateControlMode_Quality:eAVEncCommonRateControlMode_UnconstrainedVBR;
        set(CODECAPI_AVEncCommonRateControlMode,control);
        if(profile.rateControl==CaptureRateControl::Quality)set(CODECAPI_AVEncCommonQuality,profile.quality);
        else set(CODECAPI_AVEncCommonMeanBitRate,bitrate);
    }
    void VerifyProfile() const {
        auto transform=EncoderTransform();ComPtr<IMFMediaType> input,output;
        winrt::check_hresult(transform->GetInputCurrentType(0,&input));winrt::check_hresult(transform->GetOutputCurrentType(0,&output));
        GUID source{},encoded{};UINT32 actualProfile{};
        winrt::check_hresult(input->GetGUID(MF_MT_SUBTYPE,&source));winrt::check_hresult(output->GetGUID(MF_MT_SUBTYPE,&encoded));
        winrt::check_hresult(output->GetUINT32(MF_MT_MPEG2_PROFILE,&actualProfile));
        UINT inputWidth{},inputHeight{},outputWidth{},outputHeight{};
        winrt::check_hresult(MFGetAttributeSize(input.Get(),MF_MT_FRAME_SIZE,&inputWidth,&inputHeight));
        winrt::check_hresult(MFGetAttributeSize(output.Get(),MF_MT_FRAME_SIZE,&outputWidth,&outputHeight));
        if(inputWidth!=encoderWidth||inputHeight!=encoderHeight||outputWidth!=encoderWidth||outputHeight!=encoderHeight||
            source!=profile.InputSubtype()||encoded!=profile.OutputSubtype()||actualProfile!=profile.Profile())
            throw winrt::hresult_error(MF_E_INVALIDMEDIATYPE);
    }

    void InitializeAudio(IMFMediaSink* encodedSink=nullptr) {
        auto output=AudioOutputType();
        auto destination=writer.Get();
        if(encodedSink) {
            ComPtr<IMFAttributes> attributes;winrt::check_hresult(MFCreateAttributes(&attributes,1));
            winrt::check_hresult(attributes->SetUINT32(MF_SINK_WRITER_DISABLE_THROTTLING,FALSE));
            winrt::check_hresult(MFCreateSinkWriterFromMediaSink(encodedSink,attributes.Get(),&encodedAudioWriter));
            destination=encodedAudioWriter.Get();
            ComPtr<IMFStreamSink> streamSink;winrt::check_hresult(encodedSink->GetStreamSinkByIndex(0,&streamSink));
            ComPtr<IMFMediaTypeHandler> handler;winrt::check_hresult(streamSink->GetMediaTypeHandler(&handler));
            winrt::check_hresult(handler->SetCurrentMediaType(output.Get()));audioStream=0;
        }else winrt::check_hresult(destination->AddStream(output.Get(), &audioStream));
        ComPtr<IMFMediaType> input;
        winrt::check_hresult(MFCreateMediaType(&input));
        winrt::check_hresult(input->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio));
        winrt::check_hresult(input->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM));
        winrt::check_hresult(input->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, AudioChannels));
        winrt::check_hresult(input->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, AudioRate));
        winrt::check_hresult(input->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16));
        winrt::check_hresult(input->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT, AudioChannels * 2));
        winrt::check_hresult(input->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, AudioRate * AudioChannels * 2));
        winrt::check_hresult(destination->SetInputMediaType(audioStream, input.Get(), nullptr));
        if(encodedAudioWriter)winrt::check_hresult(encodedAudioWriter->BeginWriting());
    }

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
        UINT flags{};winrt::check_hresult(enumerator->CheckVideoProcessorFormat(profile.SurfaceFormat(),&flags));
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
        desc.Format=profile.SurfaceFormat();desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET;
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
    static constexpr UINT AudioRate = 48000, AudioChannels = 2, AudioBitrate = 128000;
    static ComPtr<IMFMediaType> AudioOutputType() {
        ComPtr<IMFMediaType> output;
        winrt::check_hresult(MFCreateMediaType(&output));
        winrt::check_hresult(output->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio));
        winrt::check_hresult(output->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_AAC));
        winrt::check_hresult(output->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, AudioChannels));
        winrt::check_hresult(output->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, AudioRate));
        winrt::check_hresult(output->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16));
        winrt::check_hresult(output->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, AudioBitrate / 8));
        winrt::check_hresult(output->SetUINT32(MF_MT_AAC_PAYLOAD_TYPE, 0));
        winrt::check_hresult(output->SetUINT32(MF_MT_AAC_AUDIO_PROFILE_LEVEL_INDICATION, 0x29));
        return output;
    }
    static ComPtr<IMFMediaType> VideoOutputType(UINT width,UINT height,const WrailCaptureOptions& options,const CaptureEncodingProfile* encoding=nullptr,UINT liveH264Level=0) {
        ComPtr<IMFMediaType> output;winrt::check_hresult(MFCreateMediaType(&output));
        winrt::check_hresult(output->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video));
        winrt::check_hresult(output->SetGUID(MF_MT_SUBTYPE,encoding?encoding->OutputSubtype():MFVideoFormat_H264));
        if(encoding)winrt::check_hresult(output->SetUINT32(MF_MT_MPEG2_PROFILE,encoding->Profile()));
        if(liveH264Level){
            winrt::check_hresult(output->SetUINT32(MF_MT_MPEG2_PROFILE,eAVEncH264VProfile_Base));
            winrt::check_hresult(output->SetUINT32(MF_MT_MPEG2_LEVEL,liveH264Level));
        }
        winrt::check_hresult(output->SetUINT32(MF_MT_AVG_BITRATE,options.bitrate));
        winrt::check_hresult(output->SetUINT32(MF_MT_INTERLACE_MODE,MFVideoInterlace_Progressive));
        winrt::check_hresult(MFSetAttributeSize(output.Get(),MF_MT_FRAME_SIZE,width,height));
        winrt::check_hresult(MFSetAttributeRatio(output.Get(),MF_MT_FRAME_RATE,options.framesPerSecond,1));
        winrt::check_hresult(MFSetAttributeRatio(output.Get(),MF_MT_PIXEL_ASPECT_RATIO,1,1));
        return output;
    }
    static const wchar_t* LastStage() noexcept{return phase;}
    // A null path probes initialization into a tiny in-memory MP4, without
    // capturing a screen or creating a persistent file.
    VideoEncoder(const wchar_t* path, UINT width, UINT height, const WrailCaptureOptions& options, ID3D11Device* device=nullptr, bool includeAudio=false,
        IMFMediaSink* encodedSink=nullptr,IMFMediaSink* encodedAudioSink=nullptr,const CaptureEncodingProfile* encoding=nullptr,UINT liveH264Level=0)
        : fps(options.framesPerSecond),encoderWidth(width),encoderHeight(height), hardware(options.encoder==2), audioEnabled(includeAudio),explicitProfile(encoding!=nullptr),profile(encoding?*encoding:CaptureEncodingProfile{}) {
        if(encoding&&(!encoding->Valid()||!hardware))throw winrt::hresult_error(E_INVALIDARG);
        phase=L"MFStartup";winrt::check_hresult(MFStartup(MF_VERSION));started=true;
        try {
            // A caller-owned encoded sink is the replay prototype's output tap.
            // Normal recording/probing retains the existing MP4 writer path.
            if((encodedSink && path) || (encodedAudioSink && (!encodedSink || !includeAudio)) || (encodedSink && includeAudio && !encodedAudioSink))
                throw winrt::hresult_error(E_INVALIDARG);
            phase=L"InitializeGpu";
            if(hardware)InitializeGpu(device,width,height,path!=nullptr || encodedSink!=nullptr);
            ComPtr<IMFAttributes> attributes;winrt::check_hresult(MFCreateAttributes(&attributes,6));
            winrt::check_hresult(attributes->SetUINT32(MF_SINK_WRITER_DISABLE_THROTTLING,FALSE));
            winrt::check_hresult(attributes->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS,hardware));
            // Media Foundation forbids DISABLE_CONVERTERS when hardware transforms
            // are enabled. NV12 is supplied directly; verify the chosen MFT below.
            // https://learn.microsoft.com/windows/win32/medfound/mf-readwrite-enable-hardware-transforms
            if(hardware)winrt::check_hresult(attributes->SetUINT32(MF_LOW_LATENCY,TRUE));
            if(hardware)winrt::check_hresult(attributes->SetUnknown(MF_SINK_WRITER_D3D_MANAGER,manager.Get()));
            winrt::Windows::Storage::Streams::InMemoryRandomAccessStream memory{nullptr};ComPtr<IMFByteStream> bytes;
            if(!path && !encodedSink) {
                memory=winrt::Windows::Storage::Streams::InMemoryRandomAccessStream();
                winrt::check_hresult(MFCreateMFByteStreamOnStreamEx(reinterpret_cast<IUnknown*>(winrt::get_abi(memory)),&bytes));
                winrt::check_hresult(attributes->SetGUID(MF_TRANSCODE_CONTAINERTYPE,MFTranscodeContainerType_MPEG4));
                ComPtr<IMFAttributes> byteAttributes;winrt::check_hresult(bytes.As(&byteAttributes));
                winrt::check_hresult(byteAttributes->SetString(MF_BYTESTREAM_CONTENT_TYPE,L"video/mp4"));
            }
            phase=L"CreateSinkWriter";
            if(encodedSink)winrt::check_hresult(MFCreateSinkWriterFromMediaSink(encodedSink,attributes.Get(),&writer));
            else winrt::check_hresult(MFCreateSinkWriterFromURL(path,bytes.Get(),attributes.Get(),&writer));
            auto output=VideoOutputType(width,height,options,encoding,liveH264Level);
            phase=L"AddStream";
            if(encodedSink) {
                ComPtr<IMFStreamSink> destination;winrt::check_hresult(encodedSink->GetStreamSinkByIndex(0,&destination));
                ComPtr<IMFMediaTypeHandler> handler;winrt::check_hresult(destination->GetMediaTypeHandler(&handler));
                winrt::check_hresult(handler->SetCurrentMediaType(output.Get()));stream=0;
            } else winrt::check_hresult(writer->AddStream(output.Get(),&stream));
            ComPtr<IMFMediaType> input;winrt::check_hresult(MFCreateMediaType(&input));
            winrt::check_hresult(input->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video));
            winrt::check_hresult(input->SetGUID(MF_MT_SUBTYPE,hardware?profile.InputSubtype():MFVideoFormat_RGB32));
            winrt::check_hresult(input->SetUINT32(MF_MT_INTERLACE_MODE,MFVideoInterlace_Progressive));
            winrt::check_hresult(MFSetAttributeSize(input.Get(),MF_MT_FRAME_SIZE,width,height));
            winrt::check_hresult(MFSetAttributeRatio(input.Get(),MF_MT_FRAME_RATE,fps,1));
            winrt::check_hresult(MFSetAttributeRatio(input.Get(),MF_MT_PIXEL_ASPECT_RATIO,1,1));
            winrt::check_hresult(input->SetUINT32(MF_MT_DEFAULT_STRIDE,hardware?profile.Stride(width):width*4));
            if(hardware) {
                winrt::check_hresult(input->SetUINT32(MF_MT_VIDEO_PRIMARIES,MFVideoPrimaries_BT709));
                winrt::check_hresult(input->SetUINT32(MF_MT_YUV_MATRIX,MFVideoTransferMatrix_BT709));
                winrt::check_hresult(input->SetUINT32(MF_MT_VIDEO_NOMINAL_RANGE,MFNominalRange_16_235));
            }
            phase=L"SetInputMediaType";
            ComPtr<IMFAttributes> liveSettings;
            if(liveH264Level){
                if(!hardware||!encodedSink||encoding||audioEnabled)throw winrt::hresult_error(E_INVALIDARG);
                liveSettings=LiveRateParameters(options.bitrate);
                winrt::check_hresult(liveSettings->SetUINT32(CODECAPI_AVEncCommonRateControlMode,eAVEncCommonRateControlMode_CBR));
                winrt::check_hresult(liveSettings->SetUINT32(CODECAPI_AVEncMPVGOPSize,4*fps));
                winrt::check_hresult(liveSettings->SetUINT32(CODECAPI_AVEncMPVDefaultBPictureCount,0));
                winrt::check_hresult(liveSettings->SetUINT32(CODECAPI_AVLowLatencyMode,TRUE));
            }
            winrt::check_hresult(writer->SetInputMediaType(stream,input.Get(),liveSettings.Get()));
            if(liveH264Level){
                if(LiveBufferBytes()!=std::max(1u,options.bitrate/80u))throw winrt::hresult_error(MF_E_INVALIDMEDIATYPE);
            }else if(encodedSink)ConfigureReplayCadence();
            if(explicitProfile){phase=L"ConfigureProfile";ConfigureProfile(options.bitrate);}
            if (audioEnabled) { phase=L"InitializeAudio"; InitializeAudio(encodedAudioSink); }
            phase=L"BeginWriting";
            winrt::check_hresult(writer->BeginWriting());
            phase=L"VerifyHardware";
            if(hardware && !HardwareEncoder(writer.Get()))throw winrt::hresult_error(MF_E_TOPO_CODEC_NOT_FOUND);
            if(explicitProfile){phase=L"VerifyProfile";VerifyProfile();}
            phase=L"ready";
        } catch(...) {replayKeyframeCodec.Reset();encodedAudioWriter.Reset();writer.Reset();MFShutdown();started=false;throw;}
    }
    ~VideoEncoder(){replayKeyframeCodec.Reset();encodedAudioWriter.Reset();writer.Reset();if(started)MFShutdown();}
    VideoEncoder(const VideoEncoder&)=delete;
    VideoEncoder& operator=(const VideoEncoder&)=delete;
    // Contiguous PCM; the caller aligns source QPC timestamps to the video epoch
    // and supplies explicit silence for any uncovered portion of that timeline.
    void WriteAudio(const int16_t* samples, UINT frames) {
        if (!audioEnabled || !samples || !frames || frames > AudioRate) throw winrt::hresult_error(E_INVALIDARG);
        const DWORD length = frames * AudioChannels * sizeof(int16_t);
        ComPtr<IMFMediaBuffer> buffer; winrt::check_hresult(MFCreateMemoryBuffer(length, &buffer));
        BYTE* target{}; winrt::check_hresult(buffer->Lock(&target, nullptr, nullptr));
        memcpy(target, samples, length);
        winrt::check_hresult(buffer->Unlock()); winrt::check_hresult(buffer->SetCurrentLength(length));
        ComPtr<IMFSample> sample; winrt::check_hresult(MFCreateSample(&sample));
        winrt::check_hresult(sample->AddBuffer(buffer.Get()));
        const auto start = 10000000LL * audioFrames / AudioRate;
        const auto end = 10000000LL * (audioFrames + frames) / AudioRate;
        winrt::check_hresult(sample->SetSampleTime(start)); winrt::check_hresult(sample->SetSampleDuration(end - start));
        winrt::check_hresult((encodedAudioWriter?encodedAudioWriter.Get():writer.Get())->WriteSample(audioStream, sample.Get()));
        audioFrames += frames;
    }
    void Write(const std::vector<BYTE>& bytes, UINT index) {
        if(hardware)throw winrt::hresult_error(E_INVALIDARG);
        ComPtr<IMFMediaBuffer> buffer;winrt::check_hresult(MFCreateMemoryBuffer(static_cast<DWORD>(bytes.size()),&buffer));
        BYTE* target{};winrt::check_hresult(buffer->Lock(&target,nullptr,nullptr));memcpy(target,bytes.data(),bytes.size());
        winrt::check_hresult(buffer->Unlock());winrt::check_hresult(buffer->SetCurrentLength(static_cast<DWORD>(bytes.size())));
        ComPtr<IMFSample> sample;winrt::check_hresult(MFCreateSample(&sample));winrt::check_hresult(sample->AddBuffer(buffer.Get()));
        Timestamp(sample.Get(),index);winrt::check_hresult(writer->WriteSample(stream,sample.Get()));
    }
    static ComPtr<IMFAttributes> LiveRateParameters(UINT bitrate) {
        ComPtr<IMFAttributes> parameters;winrt::check_hresult(MFCreateAttributes(&parameters,6));
        winrt::check_hresult(parameters->SetUINT32(CODECAPI_AVEncCommonMeanBitRate,bitrate));
        // H.264 HRD is expressed in bytes. Budget 100 ms at the allocated rate.
        winrt::check_hresult(parameters->SetUINT32(CODECAPI_AVEncCommonBufferSize,std::max(1u,bitrate/80u)));
        return parameters;
    }
    void UpdateLiveH264(UINT bitrate,bool keyframe) {
        auto parameters=LiveRateParameters(bitrate);
        if(keyframe)winrt::check_hresult(parameters->SetUINT32(CODECAPI_AVEncVideoForceKeyFrame,1));
        ComPtr<IMFSinkWriterEncoderConfig> config;winrt::check_hresult(writer.As(&config));
        winrt::check_hresult(config->PlaceEncodingParameters(stream,parameters.Get()));
    }
    UINT LiveBufferBytes() {
        auto transform=EncoderTransform();
        ComPtr<ICodecAPI> codec;
        winrt::check_hresult(transform.As(&codec));
        VARIANT actual{};
        winrt::check_hresult(codec->GetValue(&CODECAPI_AVEncCommonBufferSize,&actual));
        const auto bytes=actual.vt==VT_UI4?actual.ulVal:0u;
        VariantClear(&actual);
        return bytes;
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
            // Some hardware encoders accept the GOP setting but extend static
            // scenes. Request the next input keyframe once per replay second;
            // actual encoded cadence is still checked by the bounded ring.
            // This path is absent for ordinary MP4 captures.
            if(replayKeyframeCodec && (!replayKeyframeRequested || index-replayKeyframeIndex>=fps)) {
                VARIANT force{};force.vt=VT_UI4;force.ulVal=1;
                if(FAILED(replayKeyframeCodec->SetValue(&CODECAPI_AVEncVideoForceKeyFrame,&force)))replayKeyframeCodec.Reset();
                replayKeyframeRequested=true;replayKeyframeIndex=index;
            }
            winrt::check_hresult(writer->WriteSample(stream,sample.Get()));
            return true;
        } catch(...) {if(!allocatorOwns)surfaces->busy[slot]=false;throw;}
    }
    void Finish(){winrt::check_hresult(writer->Finalize());if(encodedAudioWriter)winrt::check_hresult(encodedAudioWriter->Finalize());}
    bool Hardware() const noexcept{return hardware;}
    bool ReplayGopConfigured() const noexcept{return replayGopConfigured;}
    bool ReplayBFramesDisabled() const noexcept{return replayBFramesDisabled;}
    ComPtr<IMFMediaType> EncodedType() const {
        ComPtr<IMFSinkWriterEx> extended;winrt::check_hresult(writer.As(&extended));
        for(DWORD i=0;i<8;i++) {
            GUID category{};ComPtr<IMFTransform> transform;
            if(FAILED(extended->GetTransformForStream(stream,i,&category,&transform)))break;
            if(category==MFT_CATEGORY_VIDEO_ENCODER) {
                ComPtr<IMFMediaType> type;winrt::check_hresult(transform->GetOutputCurrentType(0,&type));return type;
            }
        }
        throw winrt::hresult_error(MF_E_TOPO_CODEC_NOT_FOUND);
    }
    ComPtr<IMFMediaType> EncodedAudioType() const {
        if(!audioEnabled)throw winrt::hresult_error(MF_E_INVALIDSTREAMNUMBER);
        ComPtr<IMFSinkWriterEx> extended;
        winrt::check_hresult((encodedAudioWriter?encodedAudioWriter.Get():writer.Get())->QueryInterface(IID_PPV_ARGS(&extended)));
        for(DWORD i=0;i<8;i++) {
            GUID category{};ComPtr<IMFTransform> transform;
            if(FAILED(extended->GetTransformForStream(audioStream,i,&category,&transform)))break;
            if(category==MFT_CATEGORY_AUDIO_ENCODER) {
                ComPtr<IMFMediaType> type;winrt::check_hresult(transform->GetOutputCurrentType(0,&type));return type;
            }
        }
        throw winrt::hresult_error(MF_E_TOPO_CODEC_NOT_FOUND);
    }
};
}
