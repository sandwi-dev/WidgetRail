#include "../../src/WinUiWindowPreviewNative/WindowContextCapture.h"
#include <cstdio>
#include <stdexcept>
int wmain(int argc,wchar_t** argv){
    if(argc!=2)return 2;
    const auto module=LoadLibraryExW(argv[1],nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if(!module){printf("FAIL load=%lu\n",GetLastError());return 1;}
    struct Close{HMODULE value;~Close(){FreeLibrary(value);}} close{module};
    const auto environment=reinterpret_cast<decltype(&WrailGetCaptureEncodingEnvironment)>(GetProcAddress(module,"WrailGetCaptureEncodingEnvironment"));
    const auto probe=reinterpret_cast<decltype(&WrailProbeCaptureEncodingV2)>(GetProcAddress(module,"WrailProbeCaptureEncodingV2"));
    if(!environment||!probe)return 1;
    WrailCaptureEncodingEnvironment adapters;const auto result=environment(&adapters);
    printf("ENVIRONMENT hr=%08x size=%u adapters=%u default=%llx\n",static_cast<unsigned>(result),adapters.size,adapters.count,adapters.defaultAdapterLuid);
    if(FAILED(result)||adapters.count==0)return 1;
    WrailCaptureOptions options;options.kind=2;options.encoder=2;options.maximumWidth=1280;options.maximumHeight=720;
    options.framesPerSecond=30;options.bitrate=6000000;
    bool baseline=false;
    for(uint32_t codec=1;codec<=3;++codec)for(uint32_t format=1;format<=4;++format){
        if(codec==1&&format>1||codec==3&&format>2)continue;
        for(uint32_t rate=1;rate<=3;++rate){
            WrailCaptureEncodingSelection selected;selected.codec=codec;selected.pixelFormat=format;selected.rateControl=rate;selected.adapterLuid=adapters.defaultAdapterLuid;
            WrailCaptureEncodingProbeV2 answer;const auto hr=probe(&options,&selected,&answer);
            printf("PROFILE codec=%u format=%u rate=%u call=%08x supported=%u hardware=%u error=%08x stage=%u encoded=%u decoded=%u adapter=%llx elapsed=%.1fms\n",
                codec,format,rate,static_cast<unsigned>(hr),answer.supported,answer.hardware,static_cast<unsigned>(answer.error),answer.stage,
                answer.encodedFrames,answer.decodedFrames,answer.adapterLuid,answer.elapsedMilliseconds);fflush(stdout);
            if(codec==1&&format==1&&rate==1)baseline=answer.supported!=0;
            if(FAILED(hr)||answer.supported&&(!answer.hardware||answer.encodedFrames!=4||answer.decodedFrames==0||answer.adapterLuid!=selected.adapterLuid))return 1;
        }
    }
    WrailCaptureEncodingSelection missing;missing.adapterLuid=UINT64_MAX;
    WrailCaptureEncodingProbeV2 denied;const auto hr=probe(&options,&missing,&denied);
    if(FAILED(hr)||denied.supported||denied.stage!=1||denied.error!=DXGI_ERROR_NOT_FOUND)return 1;
    puts("PASS missing explicit adapter cannot fall back to another GPU");return baseline?0:1;
}
