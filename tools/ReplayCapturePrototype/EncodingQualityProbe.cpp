#include "EncodingQualityImages.h"
int wmain(int argc,wchar_t** argv){
    if(argc!=7&&!(argc==8&&std::wstring(argv[7])==L"--motion"))return 2; // optional generated texture translation
    const bool motion=argc==8;
    winrt::init_apartment(winrt::apartment_type::multi_threaded);winrt::check_hresult(MFStartup(MF_VERSION));
    const char* stage="source";
    try{
        const std::wstring prefix=argv[1];const UINT width=static_cast<UINT>(_wtoi(argv[5])),height=static_cast<UINT>(_wtoi(argv[6]));
        if(width<640||height<480||width>8192||height>8192||uint64_t(width)*height>3840u*2160u||width%2||height%2)return 2;
        CaptureEncodingProfile profile{static_cast<CaptureVideoCodec>(_wtoi(argv[2])),static_cast<CapturePixelFormat>(_wtoi(argv[3])),CaptureRateControl::ConstantBitrate,75};
        if(!profile.Valid())return 2;
        auto source=Source(width,height);
        if(motion)for(UINT y=height/2;y<height;++y)for(UINT x=0;x<width;++x){
            const auto seed=(x/3)*1103515245u^(y/3)*1013904223u;const auto gray=static_cast<BYTE>(40+(seed>>16)%180);
            const auto at=(size_t(y)*width+x)*4;source[at]=source[at+1]=source[at+2]=gray;
        }
        if(!motion)Png(prefix+L".source.png",width,height,source);
        auto device=CreateEncodingDevice();ComPtr<ID3D11DeviceContext> context;device->GetImmediateContext(&context);
        D3D11_TEXTURE2D_DESC desc{};desc.Width=width;desc.Height=height;desc.MipLevels=1;desc.ArraySize=1;desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
        desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
        D3D11_SUBRESOURCE_DATA data{source.data(),width*4,0};ComPtr<ID3D11Texture2D> texture;winrt::check_hresult(device->CreateTexture2D(&desc,&data,&texture));
        ComPtr<ID3D11Texture2D> original;if(motion)winrt::check_hresult(device->CreateTexture2D(&desc,&data,&original));
        WrailCaptureOptions options;options.kind=2;options.encoder=2;options.framesPerSecond=30;options.bitrate=static_cast<UINT>(_wtoi(argv[4]));
        auto samples=Microsoft::WRL::Make<ReplaySampleOutput>(false,20000000,32*1024*1024);auto sink=CreateReplaySampleSink(VideoEncoder::VideoOutputType(width,height,options,&profile).Get(),samples.Get());
        ComPtr<IMFMediaType> type;stage="encode";auto start=std::chrono::steady_clock::now();
        {
            VideoEncoder encoder(nullptr,width,height,options,device.Get(),false,sink.Get(),nullptr,&profile);
            for(UINT frame=0;frame<90;++frame){
                if(motion){
                    const auto shift=(frame*7)%width;context->CopyResource(texture.Get(),original.Get());
                    if(shift){const D3D11_BOX left{0,height/2,0,width-shift,height,1},right{width-shift,height/2,0,width,height,1};
                        context->CopySubresourceRegion(texture.Get(),0,shift,height/2,0,original.Get(),0,&left);
                        context->CopySubresourceRegion(texture.Get(),0,0,height/2,0,original.Get(),0,&right);}
                }
                const auto deadline=std::chrono::steady_clock::now()+std::chrono::seconds(5);
                while(!encoder.TryWriteTexture(texture.Get(),frame)){if(std::chrono::steady_clock::now()>deadline)throw winrt::hresult_error(HRESULT_FROM_WIN32(WAIT_TIMEOUT));std::this_thread::sleep_for(std::chrono::milliseconds(1));}
            }
            encoder.Finish();type=encoder.EncodedType();
        }
        const auto elapsed=std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-start).count();
        winrt::check_hresult(samples->failure.load());auto clip=samples->buffer.Snapshot(20000000);if(!clip)throw winrt::hresult_error(E_UNEXPECTED);
        const auto bytes=samples->buffer.Status().residentBytes;const auto frames=clip->packets.size();
        if(motion){
            const auto shift=static_cast<UINT>(std::llround(double(clip->start)*30/10000000))*7%width;auto matched=source;
            for(UINT y=height/2;y<height;++y)for(UINT x=0;x<width;++x)memcpy(matched.data()+(size_t(y)*width+x)*4,source.data()+(size_t(y)*width+(x+width-shift)%width)*4,4);
            source=std::move(matched);Png(prefix+L".source.png",width,height,source);
        }
        stage="mux";WriteReplayMp4((prefix+L".mp4").c_str(),type.Get(),nullptr,*clip);sink->Shutdown();
        stage="decode";auto decoded=Decode(prefix+L".mp4",device.Get(),width,height);Png(prefix+L".decoded.png",width,height,decoded);
        double textError{},edgeError{},chromaError{},motionError{};uint64_t textCount{},edgeCount{},chromaCount{},motionCount{};
        for(UINT y=0;y<height;++y)for(UINT x=0;x<width;++x){
            const auto at=(size_t(y)*width+x)*4;double squared{};for(unsigned channel=0;channel<3;++channel){const auto difference=double(source[at+channel])-decoded[at+channel];squared+=difference*difference;}
            if(motion&&y>=height/2){motionError+=squared;motionCount+=3;}
            if(x<width/2&&(!motion||y<height/2)){textError+=squared;textCount+=3;}else if(y<height/3){edgeError+=squared;edgeCount+=3;}else if(y<height*2/3){chromaError+=squared;chromaCount+=3;}
        }
        const auto psnr=[](double error,uint64_t count){return error?10*std::log10(65025.0*double(count)/error):100.0;};
        printf("QUALITY codec=%u format=%u width=%u height=%u bitrate=%u frames=%zu compressedBytes=%zu encodeWallMs=%.1f textPSNR=%.3f monochromeEdgePSNR=%.3f colorEdgePSNR=%.3f\n",
            static_cast<unsigned>(profile.codec),static_cast<unsigned>(profile.format),width,height,options.bitrate,frames,bytes,elapsed,psnr(textError,textCount),psnr(edgeError,edgeCount),psnr(chromaError,chromaCount));
        if(motion)printf("MOTION lowerHalfPSNR=%.3f (matched translated3x3grayscale texture,7pixels/frame)\n",psnr(motionError,motionCount));
        puts("PASS matched native-size generated-source comparison; encodeWallMs is not game-frame overhead");MFShutdown();return 0;
    }catch(const winrt::hresult_error& error){printf("FAIL quality stage=%s encoderStage=%ls hr=%08x\n",stage,VideoEncoder::LastStage(),static_cast<unsigned>(error.code().value));MFShutdown();return 1;}
}
