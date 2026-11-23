#include <Windows.h>
#include <d3d11.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <wrl/client.h>
#include <winrt/base.h>
#include <cstdio>
#include <cmath>
#include <string>
#include <limits>
#include <array>
using Microsoft::WRL::ComPtr;

int wmain(int argc,wchar_t** argv) {
    const bool recovery=argc==3 && std::wstring(argv[2])==L"--audio-recovery";
    if(argc!=2 && !(argc==3 && (std::wstring(argv[2])==L"--audio" || std::wstring(argv[2])==L"--quiet-audio" || recovery || std::wstring(argv[2])==L"--gpu")))return 2;
    const bool gpu=argc==3&&std::wstring(argv[2])==L"--gpu";
    const bool expectedAudio=argc==3&&!gpu;
    const bool quiet=expectedAudio&&std::wstring(argv[2])==L"--quiet-audio";
    winrt::init_apartment(winrt::apartment_type::multi_threaded);
    winrt::check_hresult(MFStartup(MF_VERSION));
    const char* phase="open";
    try {
        ComPtr<IMFAttributes> settings;ComPtr<ID3D11Device> device;ComPtr<IMFDXGIDeviceManager> manager;
        if(gpu){
            winrt::check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT|D3D11_CREATE_DEVICE_VIDEO_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,nullptr));
            UINT reset{};winrt::check_hresult(MFCreateDXGIDeviceManager(&reset,&manager));winrt::check_hresult(manager->ResetDevice(device.Get(),reset));
            winrt::check_hresult(MFCreateAttributes(&settings,2));winrt::check_hresult(settings->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS,TRUE));
            winrt::check_hresult(settings->SetUnknown(MF_SOURCE_READER_D3D_MANAGER,manager.Get()));
        }
        ComPtr<IMFSourceReader> reader;winrt::check_hresult(MFCreateSourceReaderFromURL(argv[1],settings.Get(),&reader));
        ComPtr<IMFMediaType> video;winrt::check_hresult(MFCreateMediaType(&video));
        winrt::check_hresult(video->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video));winrt::check_hresult(video->SetGUID(MF_MT_SUBTYPE,MFVideoFormat_NV12));
        phase="set-video-output";winrt::check_hresult(reader->SetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),nullptr,video.Get()));
        if(expectedAudio) {
            ComPtr<IMFMediaType> audio;winrt::check_hresult(MFCreateMediaType(&audio));
            winrt::check_hresult(audio->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Audio));winrt::check_hresult(audio->SetGUID(MF_MT_SUBTYPE,MFAudioFormat_PCM));
            winrt::check_hresult(audio->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE,16));
            winrt::check_hresult(audio->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS,2));
            winrt::check_hresult(audio->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND,48000));
            winrt::check_hresult(reader->SetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM),nullptr,audio.Get()));
        }
        size_t videoFrames{},audioFrames{},crossings{};int16_t previous{};bool havePrevious=false;
        double energy{};int64_t videoFirst=INT64_MAX,audioFirst=INT64_MAX,videoEnd{},audioEnd{};
        // The generated source plays a continuous tone. Require sustained tone, silence,
        // then tone again in the decoded PCM rather than trusting track metadata.
        double windowEnergy{};
        unsigned toneWindows{},silentWindows{},recoveredWindows{};
        LONGLONG previousVideo=-1,longestVideoGap{};
        std::array<double,81> coefficient{},q1{},q2{};
        for(size_t index=0;index<coefficient.size();++index)coefficient[index]=2*std::cos(6.283185307179586*(400+static_cast<double>(index))/48000);
        bool videoDone=false,audioDone=!expectedAudio;
        int leftLuma=-1,rightLuma=-1;
        for(size_t iteration=0;iteration<32768 && !(videoDone&&audioDone);++iteration) {
            DWORD stream{},flags{};LONGLONG timestamp{};ComPtr<IMFSample> sample;
            phase="read-sample";winrt::check_hresult(reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_ANY_STREAM),0,&stream,&flags,&timestamp,&sample));
            ComPtr<IMFMediaType> type;phase="current-type";winrt::check_hresult(reader->GetCurrentMediaType(stream,&type));
            GUID major{};winrt::check_hresult(type->GetGUID(MF_MT_MAJOR_TYPE,&major));
            if(major==MFMediaType_Video&&videoFrames==0){UINT width{},height{};winrt::check_hresult(MFGetAttributeSize(type.Get(),MF_MT_FRAME_SIZE,&width,&height));
                std::printf("decodedWidth=%u decodedHeight=%u\n",width,height);
                const GUID apertures[]{MF_MT_MINIMUM_DISPLAY_APERTURE,MF_MT_GEOMETRIC_APERTURE,MF_MT_PAN_SCAN_APERTURE};
                for(unsigned area=0;area<3;++area){MFVideoArea aperture{};UINT32 length{};
                    const auto hr=type->GetBlob(apertures[area],reinterpret_cast<UINT8*>(&aperture),sizeof(aperture),&length);
                    if(SUCCEEDED(hr))std::printf("aperture=%u width=%ld height=%ld offset=%d/%d\n",area,aperture.Area.cx,aperture.Area.cy,aperture.OffsetX.value,aperture.OffsetY.value);
                }}
            if(flags&MF_SOURCE_READERF_ENDOFSTREAM){if(major==MFMediaType_Video)videoDone=true;else if(major==MFMediaType_Audio)audioDone=true;}
            if(!sample)continue;
            LONGLONG duration{};winrt::check_hresult(sample->GetSampleDuration(&duration));
            if(major==MFMediaType_Video){++videoFrames;videoFirst=std::min(videoFirst,timestamp);videoEnd=std::max(videoEnd,timestamp+duration);
                if(previousVideo>=0)longestVideoGap=std::max(longestVideoGap,timestamp-previousVideo);
                previousVideo=timestamp;
                if(quiet && leftLuma<0) {
                    UINT width{},height{},stride{};winrt::check_hresult(MFGetAttributeSize(type.Get(),MF_MT_FRAME_SIZE,&width,&height));
                    if(FAILED(type->GetUINT32(MF_MT_DEFAULT_STRIDE,&stride)))stride=width;
                    ComPtr<IMFMediaBuffer> pixels;winrt::check_hresult(sample->ConvertToContiguousBuffer(&pixels));
                    BYTE* bytes{};DWORD count{};winrt::check_hresult(pixels->Lock(&bytes,nullptr,&count));
                    if(width>=4 && height>=2 && stride>=width && uint64_t(stride)*height<=count) {
                        leftLuma=bytes[size_t(height/2)*stride+width/4];rightLuma=bytes[size_t(height/2)*stride+width*3/4];
                    }
                    winrt::check_hresult(pixels->Unlock());
                }
            }
            else if(major==MFMediaType_Audio) {
                audioFirst=std::min(audioFirst,timestamp);audioEnd=std::max(audioEnd,timestamp+duration);
                ComPtr<IMFMediaBuffer> buffer;winrt::check_hresult(sample->ConvertToContiguousBuffer(&buffer));
                BYTE* bytes{};DWORD count{};winrt::check_hresult(buffer->Lock(&bytes,nullptr,&count));
                const auto values=reinterpret_cast<const int16_t*>(bytes);
                for(size_t index=0;index<count/4;index++) {
                    const auto value=values[index*2];energy+=static_cast<double>(value)*value;
                    if(recovery){
                        windowEnergy+=static_cast<double>(value)*value;
                        if((audioFrames+1)%4800==0){
                            const auto windowRms=std::sqrt(windowEnergy/4800);
                            windowEnergy=0;
                            if(toneWindows<5)toneWindows=windowRms>100?toneWindows+1:0;
                            else if(silentWindows<10)silentWindows=windowRms<10?silentWindows+1:0;
                            else if(recoveredWindows<5)recoveredWindows=windowRms>100?recoveredWindows+1:0;
                        }
                    }
                    for(size_t bin=0;bin<coefficient.size();++bin){const auto q0=coefficient[bin]*q1[bin]-q2[bin]+value;q2[bin]=q1[bin];q1[bin]=q0;}
                    if(havePrevious && ((previous<0 && value>=0)||(previous>=0 && value<0)))++crossings;
                    previous=value;havePrevious=true;++audioFrames;
                }
                winrt::check_hresult(buffer->Unlock());
            }
        }
        const auto rms=audioFrames?std::sqrt(energy/static_cast<double>(audioFrames)):0;
        const auto frequency=audioFrames?static_cast<double>(crossings)*48000/(2*audioFrames):0;
        double strongest{};size_t peak{};
        for(size_t index=0;index<coefficient.size();++index){const auto power=q1[index]*q1[index]+q2[index]*q2[index]-coefficient[index]*q1[index]*q2[index];
            if(power>strongest){strongest=power;peak=index;}}
        const auto spectralFrequency=400+peak;
        std::printf("decodedVideoFrames=%zu videoStart100ns=%lld videoEnd100ns=%lld decodedAudioFrames=%zu audioStart100ns=%lld audioEnd100ns=%lld rms=%.2f zeroCrossHz=%.2f spectralPeakHz=%zu\n",
            videoFrames,videoFirst,videoEnd,audioFrames,expectedAudio?audioFirst:0,audioEnd,rms,frequency,spectralFrequency);
        if(quiet)std::printf("ownedFixtureLumaLeft=%d ownedFixtureLumaRight=%d\n",leftLuma,rightLuma);
        if(recovery)std::printf("toneWindows=%u silentWindows=%u recoveredWindows=%u longestVideoGapMs=%.1f\n",
            toneWindows,silentWindows,recoveredWindows,double(longestVideoGap)/10000);
        const bool valid=(!recovery || (toneWindows>=5 && silentWindows>=10 && recoveredWindows>=5 && longestVideoGap<7500000)) && videoDone&&audioDone&&videoFrames>0&&videoFirst==0&&videoEnd>0&&(!expectedAudio||
            (audioFrames>0&&audioFirst>=0&&audioFirst<=213334&&std::llabs(videoEnd-audioEnd)<=213334&&rms>(quiet||recovery?100:1000)&&spectralFrequency>=435&&spectralFrequency<=445)) &&
            (!quiet || leftLuma>40 && rightLuma-leftLuma>30);
        reader.Reset();MFShutdown();return valid?0:1;
    }catch(const winrt::hresult_error& error){std::printf("FAILED inspect hr=%08X stage=%s\n",static_cast<unsigned>(error.code().value),phase);MFShutdown();return 1;}
}
