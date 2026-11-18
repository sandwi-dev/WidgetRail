#include "ReplayWorkerTestSupport.h"
#include "../../src/WinUiWindowPreviewNative/WindowContextCapture.h"
int32_t __stdcall AllowOwnedCapture(void*){return 1;}
int wmain(int argc,wchar_t** argv){
    if(argc!=4&&argc!=5)return 2;
    try{
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        FixtureProcess source(argv[2]);widgetrail::capture::CaptureEncodingProfile profile;
        profile.rateControl=widgetrail::capture::CaptureRateControl::ConstantBitrate;
        {
            Worker stale(argv[1],source,15,&profile,1280,720,6000000,true);
            const auto denied=stale.Wait([](const State& state){return (state.flags&Ended)!=0;},10000,true);
            Check(denied.phase==Phase::Failed&&denied.issue==Issue::Target&&denied.bufferedTicks==0&&denied.residentBytes==0,
                "stale first source geometry is rejected before creating an encoded buffer");
        }
        {
            Worker admitted(argv[1],source,15,&profile);
            auto state=admitted.Wait([](const State& value){return value.phase==Phase::Recording&&value.bufferedTicks>=20000000;});
            Check(state.codec==1&&state.pixelFormat==1&&state.rateControl==1&&state.adapterLuid!=0,
                "native recording reports exact admitted codec/color/rate/adapter");
            Check(SetWindowPos(source.Window(),nullptr,0,0,2560,1440,SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE|SWP_ASYNCWINDOWPOS)!=0,"owned source resized after admission");
            const auto until=GetTickCount64()+1500;
            state=admitted.Wait([&](const State& value){if(value.width!=1280||value.height!=720)throw std::runtime_error("session canvas changed onresize");return GetTickCount64()>=until;});
            Check(state.phase==Phase::Recording,"first-frame source fence does not stop an intentionally resized running session");
            auto clip=admitted.Save(std::wstring(argv[3])+L".mp4",2);
            Check(clip.savedWidth==1280&&clip.savedHeight==720,"resized source saves the original bounded canvas");
            Hr(WrailReplayWorkerRequestStop(admitted.value,admitted.epoch));admitted.Wait([](const State& value){return (value.flags&Ended)!=0;},10000,true);
        }
        {
            Check(SetWindowPos(source.Window(),nullptr,0,0,5120,1440,SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE)!=0,"owned nonstandard source committed");
            Worker exact(argv[1],source,15,&profile,3620,1018,12000000);
            auto state=exact.Wait([](const State& value){return value.phase==Phase::Recording&&value.bufferedTicks>=20000000;});
            Check(state.width==3620&&state.height==1018&&state.sourceWidth==5120&&state.sourceHeight==1440,
                "advanced nonstandard canvas remains exactly the qualified3620x1018 instead of being fitted twice");
            auto clip=exact.Save(std::wstring(argv[3])+L".exact.mp4",2);
            Check(clip.savedWidth==3620&&clip.savedHeight==1018,"saved nonstandard dimensions match the admitted profile");
            Hr(WrailReplayWorkerRequestStop(exact.value,exact.epoch));exact.Wait([](const State& value){return (value.flags&Ended)!=0;},10000,true);
        }
        if(argc==5){
            const auto library=LoadLibraryExW(argv[4],nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);Check(library!=nullptr,"manualV4 native library loaded");
            struct Library{HMODULE value;~Library(){FreeLibrary(value);}} owner{library};
            const auto readIdentity=reinterpret_cast<decltype(&WrailPreviewReadIdentity)>(GetProcAddress(library,"WrailPreviewReadIdentity"));
            const auto capture=reinterpret_cast<decltype(&WrailCaptureWindowConfiguredV4)>(GetProcAddress(library,"WrailCaptureWindowConfiguredV4"));
            const auto environment=reinterpret_cast<decltype(&WrailGetCaptureEncodingEnvironment)>(GetProcAddress(library,"WrailGetCaptureEncodingEnvironment"));
            Check(readIdentity&&capture&&environment,"manualV4 exact-canvas imports available");
            WrailPreviewTarget target;Hr(readIdentity(reinterpret_cast<uint64_t>(source.Window()),&target));WrailCaptureEncodingEnvironment devices;Hr(environment(&devices));
            WrailCaptureEncodingSelection selected;selected.adapterLuid=devices.defaultAdapterLuid;
            WrailCaptureOptions options;options.kind=2;options.encoder=2;options.maximumWidth=3620;options.maximumHeight=1018;options.framesPerSecond=30;options.bitrate=12000000;options.durationSeconds=1;
            SetForegroundWindow(source.Window());Check(GetAncestor(GetForegroundWindow(),GA_ROOT)==source.Window(),"owned manual source holds foreground");
            WrailCaptureResult result;const auto path=std::wstring(argv[3])+L".manual-exact.mp4";
            Hr(capture(&target,path.c_str(),&options,&selected,5120,1440,AllowOwnedCapture,nullptr,&result));
            Check(result.width==3620&&result.height==1018&&result.sourceWidth==5120&&result.sourceHeight==1440&&result.frames>0,
                "manualV4 preserves exact qualified3620x1018 canvas and actual source geometry");
        }
        puts("PASS advanced source-at-start admission and fixed-canvas resize lifecycle");return 0;
    }catch(const winrt::hresult_error& e){printf("FAIL source admission HRESULT=%08x\n",static_cast<unsigned>(e.code().value));return 1;}
    catch(const std::exception& e){printf("FAIL source admission %s\n",e.what());return 1;}
}
