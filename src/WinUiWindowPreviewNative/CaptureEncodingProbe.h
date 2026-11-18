#pragma once
#include "CaptureVideoEncoder.h"
#include "ReplaySampleOutput.h"
#include <chrono>
#include <thread>
namespace widgetrail::capture {
// Availability check only: a small owned GPU texture, the production compressed
// replay sink/mux, and the GPU decoder. No source capture, temporary file or
// unbounded benchmark; gaming-performance qualification is a separate test.
inline void ProbeExplicitEncoding(const WrailCaptureOptions& options,const WrailCaptureEncodingSelection& selected,WrailCaptureEncodingProbeV2& result) {
    CaptureEncodingProfile profile{static_cast<CaptureVideoCodec>(selected.codec),static_cast<CapturePixelFormat>(selected.pixelFormat),
        static_cast<CaptureRateControl>(selected.rateControl),selected.quality};
    if(!profile.Valid())throw winrt::hresult_error(E_INVALIDARG);
    result.stage=1;auto device=CreateEncodingDevice(selected.adapterLuid);result.adapterLuid=EncodingDeviceAdapter(device.Get());
    result.codec=selected.codec;result.pixelFormat=selected.pixelFormat;result.rateControl=selected.rateControl;result.quality=selected.quality;
    ComPtr<ID3D11DeviceContext> context;device->GetImmediateContext(&context);
    auto outputType=VideoEncoder::VideoOutputType(options.maximumWidth,options.maximumHeight,options,&profile);
    auto samples=Microsoft::WRL::Make<ReplaySampleOutput>(false,10000000,16*1024*1024);
    auto sink=CreateReplaySampleSink(outputType.Get(),samples.Get());
    struct SinkLifetime{IMFMediaSink* value;~SinkLifetime(){value->Shutdown();}} sinkLifetime{sink.Get()};
    result.stage=2;
    ComPtr<IMFMediaType> encodedType;
    {
        VideoEncoder encoder(nullptr,options.maximumWidth,options.maximumHeight,options,device.Get(),false,sink.Get(),nullptr,&profile);
        result.hardware=encoder.Hardware()?1:0;result.replayGop=encoder.ReplayGopConfigured()?1:0;result.replayNoBFrames=encoder.ReplayBFramesDisabled()?1:0;
        D3D11_TEXTURE2D_DESC desc{};desc.Width=options.maximumWidth;desc.Height=options.maximumHeight;desc.MipLevels=1;desc.ArraySize=1;
        desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET;
        ComPtr<ID3D11Texture2D> texture;ComPtr<ID3D11RenderTargetView> view;
        winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&texture));winrt::check_hresult(device->CreateRenderTargetView(texture.Get(),nullptr,&view));
        result.stage=3;const auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(5);
        for(UINT frame=0;frame<4;++frame){
            const float color[]{.1f*frame,.3f,.6f,1.f};context->ClearRenderTargetView(view.Get(),color);
            while(!encoder.TryWriteTexture(texture.Get(),frame)){
                if(std::chrono::steady_clock::now()>=deadline)throw winrt::hresult_error(HRESULT_FROM_WIN32(WAIT_TIMEOUT));
                std::this_thread::sleep_for(std::chrono::milliseconds(1));
            }
        }
        encoder.Finish();encodedType=encoder.EncodedType();
    }
    winrt::check_hresult(samples->failure.load());result.encodedFrames=static_cast<uint32_t>(samples->encodedCount.load());
    if(result.encodedFrames!=4)throw winrt::hresult_error(MF_E_INVALIDMEDIATYPE);
    auto clip=samples->buffer.Snapshot(10000000);if(!clip)throw winrt::hresult_error(MF_E_INVALIDMEDIATYPE);
    const auto expected=clip->packets.size();
    result.stage=4;
    winrt::Windows::Storage::Streams::InMemoryRandomAccessStream memory;auto writing=memory.CloneStream();ComPtr<IMFByteStream> bytes;
    winrt::check_hresult(MFCreateMFByteStreamOnStreamEx(reinterpret_cast<IUnknown*>(winrt::get_abi(writing)),&bytes));
    ComPtr<IMFAttributes> byteAttributes;winrt::check_hresult(bytes.As(&byteAttributes));winrt::check_hresult(byteAttributes->SetString(MF_BYTESTREAM_CONTENT_TYPE,L"video/mp4"));
    WriteReplayMp4Stream(bytes.Get(),encodedType.Get(),*clip);
    // The sink closes its byte-stream wrapper. A clone keeps the shared backing
    // store alive for an independently owned reader, without copying the media.
    bytes.Reset();winrt::check_hresult(MFCreateMFByteStreamOnStreamEx(reinterpret_cast<IUnknown*>(winrt::get_abi(memory)),&bytes));
    winrt::check_hresult(bytes->SetCurrentPosition(0));
    result.stage=5;
    UINT reset{};ComPtr<IMFDXGIDeviceManager> manager;winrt::check_hresult(MFCreateDXGIDeviceManager(&reset,&manager));
    winrt::check_hresult(manager->ResetDevice(device.Get(),reset));
    ComPtr<IMFAttributes> readerAttributes;winrt::check_hresult(MFCreateAttributes(&readerAttributes,2));
    winrt::check_hresult(readerAttributes->SetUnknown(MF_SOURCE_READER_D3D_MANAGER,manager.Get()));
    winrt::check_hresult(readerAttributes->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS,TRUE));
    ComPtr<IMFSourceReader> reader;winrt::check_hresult(MFCreateSourceReaderFromByteStream(bytes.Get(),readerAttributes.Get(),&reader));
    ComPtr<IMFMediaType> decoded;winrt::check_hresult(MFCreateMediaType(&decoded));winrt::check_hresult(decoded->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video));
    winrt::check_hresult(decoded->SetGUID(MF_MT_SUBTYPE,profile.format==CapturePixelFormat::P010?MFVideoFormat_P010:MFVideoFormat_NV12));
    winrt::check_hresult(reader->SetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),nullptr,decoded.Get()));
    bool ended=false;LONGLONG previous=-1;
    for(UINT iteration=0;iteration<16;++iteration){
        DWORD flags{};LONGLONG timestamp{};ComPtr<IMFSample> sample;
        winrt::check_hresult(reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),0,nullptr,&flags,&timestamp,&sample));
        if(sample){
            ComPtr<IMFMediaType> actual;winrt::check_hresult(reader->GetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),&actual));
            UINT width{},height{};winrt::check_hresult(MFGetAttributeSize(actual.Get(),MF_MT_FRAME_SIZE,&width,&height));
            MFVideoArea aperture{};UINT32 apertureBytes{};
            const auto hasAperture=SUCCEEDED(actual->GetBlob(MF_MT_MINIMUM_DISPLAY_APERTURE,reinterpret_cast<UINT8*>(&aperture),sizeof(aperture),&apertureBytes));
            const auto visibleWidth=hasAperture?static_cast<UINT>(aperture.Area.cx):width;
            const auto visibleHeight=hasAperture?static_cast<UINT>(aperture.Area.cy):height;
            if(visibleWidth!=options.maximumWidth||visibleHeight!=options.maximumHeight||width<visibleWidth||height<visibleHeight||
                (hasAperture&&(apertureBytes!=sizeof(aperture)||aperture.OffsetX.value<0||aperture.OffsetY.value<0||aperture.OffsetX.fract||aperture.OffsetY.fract||
                    static_cast<UINT>(aperture.OffsetX.value)+visibleWidth>width||static_cast<UINT>(aperture.OffsetY.value)+visibleHeight>height)))
                throw winrt::hresult_error(MF_E_INVALIDMEDIATYPE);
            if(timestamp<=previous)throw winrt::hresult_error(MF_E_INVALID_TIMESTAMP);previous=timestamp;++result.decodedFrames;
        }
        if(flags&MF_SOURCE_READERF_ENDOFSTREAM){ended=true;break;}
    }
    if(!ended||result.decodedFrames!=expected)throw winrt::hresult_error(MF_E_INVALIDMEDIATYPE);
    result.stage=6;result.supported=1;result.error=S_OK;
}
}
