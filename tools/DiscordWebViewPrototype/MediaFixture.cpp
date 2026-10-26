#define WRAIL_CAPTURE_VALIDATION
#include "../../src/WinUiWindowPreviewNative/WindowCaptureFixture.cpp"
#include <mmsystem.h>
#include <cmath>
#include <vector>
#include <cstdint>
#include <cstring>

// Synthetic content only. A quiet generated tone allows browser receive-side
// statistics to verify PCM transport without recording microphone/user audio.
int wmain(int argc,wchar_t** argv) {
    bool audio=false,stationary=false;
    for(int i=1;i<argc;++i){audio|=wcscmp(argv[i],L"--audio")==0;stationary|=wcscmp(argv[i],L"--static")==0;}
    std::vector<uint8_t> wave(44+48000*4);
    if(audio) {
        const auto u32=[&](size_t offset,uint32_t value){memcpy(wave.data()+offset,&value,4);};
        const auto u16=[&](size_t offset,uint16_t value){memcpy(wave.data()+offset,&value,2);};
        memcpy(wave.data(),"RIFF",4);u32(4,static_cast<uint32_t>(wave.size()-8));memcpy(wave.data()+8,"WAVEfmt ",8);
        u32(16,16);u16(20,1);u16(22,2);u32(24,48000);u32(28,192000);u16(32,4);u16(34,16);
        memcpy(wave.data()+36,"data",4);u32(40,192000);
        for(size_t i=0;i<48000;++i){auto value=static_cast<int16_t>(327*std::sin(2*3.141592653589793*440*double(i)/48000));memcpy(wave.data()+44+i*4,&value,2);memcpy(wave.data()+46+i*4,&value,2);}
        if(!PlaySoundW(reinterpret_cast<const wchar_t*>(wave.data()),nullptr,SND_MEMORY|SND_ASYNC|SND_LOOP|SND_NODEFAULT))return 3;
    }
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    auto hwnd=CreateFixture(656,400);if(!hwnd)return 2;
    // A stationary fixture must repaint its new client area on shrink as well as
    // grow; otherwise Windows can reuse old pixels without a new captured frame.
    SetClassLongPtrW(hwnd,GCL_STYLE,CS_HREDRAW|CS_VREDRAW);
    if(stationary)KillTimer(hwnd,1);
    MSG msg{};
    while(IsWindow(hwnd)&&GetMessageW(&msg,nullptr,0,0)>0){TranslateMessage(&msg);DispatchMessageW(&msg);}
    PlaySoundW(nullptr,nullptr,0);return 0;
}
