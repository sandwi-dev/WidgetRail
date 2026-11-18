// Isolated feasibility probe. No window/audio capture, service or production replay API.
#include "../../src/WinUiWindowPreviewNative/CaptureVideoEncoder.h"
#include "../../src/WinUiWindowPreviewNative/ReplaySampleOutput.h"
#include <cstdio>
#include <mutex>
#include <thread>
#include <chrono>
#include <string>
#include <future>
#include <cmath>

using Microsoft::WRL::ComPtr;
using widgetrail::capture::VideoEncoder;
using namespace widgetrail::capture;
int wmain(int argc,wchar_t** argv) {
    if(argc<2)return 2;
    bool includeAudio=false,explicitProfile=false;CaptureEncodingProfile profile;
    for(int index=2;index<argc;++index){
        const std::wstring argument=argv[index];
        if(argument==L"--audio")includeAudio=true;
        else if(argument==L"--profile"&&index+4<argc){
            explicitProfile=true;profile.codec=static_cast<CaptureVideoCodec>(_wtoi(argv[++index]));
            profile.format=static_cast<CapturePixelFormat>(_wtoi(argv[++index]));profile.rateControl=static_cast<CaptureRateControl>(_wtoi(argv[++index]));profile.quality=static_cast<uint32_t>(_wtoi(argv[++index]));
            if(!profile.Valid())return 2;
        }else return 2;
    }
    winrt::init_apartment(winrt::apartment_type::multi_threaded);
    winrt::check_hresult(MFStartup(MF_VERSION));
    try {
        constexpr UINT width=1280,height=720,fps=30;
        WrailCaptureOptions options;options.kind=2;options.encoder=2;options.framesPerSecond=fps;options.bitrate=6000000;
        ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> immediate;
        winrt::check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&immediate));
        auto type=VideoEncoder::VideoOutputType(width,height,options,explicitProfile?&profile:nullptr);
        auto samples=Microsoft::WRL::Make<ReplaySampleOutput>(includeAudio,20000000,4*1024*1024);ComPtr<IMFActivate> activation;
        winrt::check_hresult(MFCreateSampleGrabberSinkActivate(type.Get(),samples.Get(),&activation));
        winrt::check_hresult(activation->SetUINT32(MF_SAMPLEGRABBERSINK_IGNORE_CLOCK,TRUE));
        ComPtr<IMFMediaSink> sink;winrt::check_hresult(activation->ActivateObject(IID_PPV_ARGS(&sink)));
        ComPtr<IMFMediaSink> audioSink;
        if(includeAudio) {
            ComPtr<IMFMediaType> audio;winrt::check_hresult(MFCreateMediaType(&audio));
            winrt::check_hresult(audio->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Audio));winrt::check_hresult(audio->SetGUID(MF_MT_SUBTYPE,MFAudioFormat_AAC));
            winrt::check_hresult(audio->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS,VideoEncoder::AudioChannels));
            winrt::check_hresult(audio->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND,VideoEncoder::AudioRate));
            winrt::check_hresult(audio->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE,16));
            winrt::check_hresult(audio->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND,VideoEncoder::AudioBitrate/8));
            winrt::check_hresult(audio->SetUINT32(MF_MT_AAC_PAYLOAD_TYPE,0));
            ComPtr<IMFActivate> activateAudio;winrt::check_hresult(MFCreateSampleGrabberSinkActivate(audio.Get(),samples.Get(),&activateAudio));
            winrt::check_hresult(activateAudio->SetUINT32(MF_SAMPLEGRABBERSINK_IGNORE_CLOCK,TRUE));
            winrt::check_hresult(activateAudio->ActivateObject(IID_PPV_ARGS(&audioSink)));
        }
        D3D11_TEXTURE2D_DESC desc{};desc.Width=width;desc.Height=height;desc.MipLevels=1;desc.ArraySize=1;desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET;
        ComPtr<ID3D11Texture2D> texture;ComPtr<ID3D11RenderTargetView> view;
        winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&texture));winrt::check_hresult(device->CreateRenderTargetView(texture.Get(),nullptr,&view));
        ComPtr<IMFMediaType> encodedType;
        ComPtr<IMFMediaType> encodedAudioType;
        std::future<void> liveSave;
        {
            VideoEncoder encoder(nullptr,width,height,options,device.Get(),includeAudio,sink.Get(),audioSink.Get(),explicitProfile?&profile:nullptr);
            std::printf("codec=%u format=%u rate=%u quality=%u\n",static_cast<unsigned>(profile.codec),static_cast<unsigned>(profile.format),static_cast<unsigned>(profile.rateControl),profile.quality);
            std::printf("hardware=%d requested1sGop=%d requestedNoBFrames=%d\n",encoder.Hardware(),encoder.ReplayGopConfigured(),encoder.ReplayBFramesDisabled());std::fflush(stdout);
            for(UINT frame=0;frame<fps*3;frame++) {
                const float color[]={float(frame%fps)/fps,.2f,.3f,1.f};immediate->ClearRenderTargetView(view.Get(),color);
                const auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(3);
                while(!encoder.TryWriteTexture(texture.Get(),frame)) {
                    if(std::chrono::steady_clock::now()>deadline)throw winrt::hresult_error(HRESULT_FROM_WIN32(WAIT_TIMEOUT));
                    std::this_thread::sleep_for(std::chrono::milliseconds(1));
                }
                if(includeAudio) {
                    std::vector<int16_t> pcm(VideoEncoder::AudioRate/fps*VideoEncoder::AudioChannels);
                    for(size_t index=0;index<pcm.size()/2;++index) {
                        const auto time=(static_cast<double>(frame)*VideoEncoder::AudioRate/fps+static_cast<double>(index))/VideoEncoder::AudioRate;
                        pcm[index*2]=pcm[index*2+1]=static_cast<int16_t>(std::sin(time*440*6.283185307179586)*12000);
                    }
                    encoder.WriteAudio(pcm.data(),VideoEncoder::AudioRate/fps);
                }
                if(frame==75) {
                    auto live=samples->buffer.Snapshot(10000000);
                    if(!live)throw winrt::hresult_error(E_UNEXPECTED);
                    const auto livePath=std::wstring(argv[1])+L".live.mp4";
                    std::printf("savedWhileEncoding=%zu\n",live->packets.size());
                    liveSave=std::async(std::launch::async,[live=std::move(live),format=encoder.EncodedType(),audioFormat=includeAudio?encoder.EncodedAudioType():ComPtr<IMFMediaType>{},livePath] {
                        winrt::init_apartment(winrt::apartment_type::multi_threaded);
                        WriteReplayMp4(livePath.c_str(),format.Get(),audioFormat.Get(),*live,[](size_t){std::this_thread::sleep_for(std::chrono::milliseconds(2));});
                    });
                }
            }
            encoder.Finish();encodedType=encoder.EncodedType();
            UINT encodedWidth{},encodedHeight{};winrt::check_hresult(MFGetAttributeSize(encodedType.Get(),MF_MT_FRAME_SIZE,&encodedWidth,&encodedHeight));
            UINT32 headerSize{};encodedType->GetBlobSize(MF_MT_MPEG_SEQUENCE_HEADER,&headerSize);
            std::printf("encodedTypeWidth=%u encodedTypeHeight=%u sequenceHeaderBytes=%u\n",encodedWidth,encodedHeight,headerSize);
            if(headerSize&&headerSize<=256){std::vector<UINT8> header(headerSize);winrt::check_hresult(encodedType->GetBlob(MF_MT_MPEG_SEQUENCE_HEADER,header.data(),headerSize,nullptr));
                std::printf("sequenceHeader=");for(const auto byte:header)std::printf("%02x",byte);std::printf("\n");}if(includeAudio)encodedAudioType=encoder.EncodedAudioType();
        }
        if(liveSave.valid())liveSave.get();
        sink->Shutdown();
        if(audioSink)audioSink->Shutdown();
        auto clip=samples->buffer.Snapshot(20000000);if(!clip)throw winrt::hresult_error(E_UNEXPECTED);
        if(explicitProfile&&profile.codec==CaptureVideoCodec::Av1){
            const auto first=std::find_if(clip->packets.begin(),clip->packets.end(),[](const auto& packet){return packet.stream==ReplayStream::Video;});
            if(first!=clip->packets.end()){std::printf("firstPacket=");for(size_t byte=0;byte<std::min<size_t>(96,first->payload->bytes.size());++byte)std::printf("%02x",first->payload->bytes[byte]);std::printf("\n");}
        }
        size_t keys=0,videoPackets=0,audioPackets=0;int64_t firstAudio=INT64_MAX;
        for(const auto& packet:clip->packets)if(packet.stream==ReplayStream::Video){++videoPackets;if(packet.key)++keys;}
        else{++audioPackets;if(packet.time>=clip->start)firstAudio=std::min(firstAudio,packet.time);}
        auto status=samples->buffer.Status();
        std::printf("encoded=%zu retained=%zu keyframes=%zu bytes=%zu dropped=%llu\n",samples->encodedCount.load(),clip->packets.size(),keys,status.residentBytes,static_cast<unsigned long long>(status.dropped));std::fflush(stdout);
        if(includeAudio)std::printf("aac=%zu retainedAudio=%zu initialAudioDropped=%zu leadingAudioGap100ns=%lld\n",samples->audioCount.load(),audioPackets,samples->audioDropped.load(),firstAudio-clip->start);
        WriteReplayMp4(argv[1],encodedType.Get(),encodedAudioType.Get(),*clip);
        MFShutdown();return samples->encodedCount==fps*3 && videoPackets>=fps && videoPackets<fps*3 && keys>0 &&
            status.dropped==samples->audioDropped.load() && (!includeAudio || audioPackets>40 && firstAudio-clip->start<=213334)?0:1;
    }catch(const winrt::hresult_error& error){std::printf("FAILED hr=%08X stage=%ls\n",static_cast<unsigned>(error.code().value),VideoEncoder::LastStage());MFShutdown();return 1;}
}
