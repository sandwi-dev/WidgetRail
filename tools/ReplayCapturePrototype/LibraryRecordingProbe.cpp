#include "ReplayWorkerTestSupport.h"
#include "../../src/WinUiWindowPreviewNative/WindowContextCapture.h"
#include <filesystem>

static_assert(sizeof(WrailCaptureOptions)==56 && sizeof(WrailCaptureResult)==56 && sizeof(WrailCaptureEncodingSelection)==32);
static_assert(WrailCaptureTemporaryMaximumBytes==128ULL*1024*1024);
static_assert(WrailCaptureLibraryMaximumBytes==2ULL*1024*1024*1024);
struct LibraryAuthority {
    uint64_t after{};int decision{1};uint32_t calls{};
    static int32_t __stdcall Read(void* context) {
        auto& self=*static_cast<LibraryAuthority*>(context);++self.calls;
        return self.after&&GetTickCount64()>=self.after?self.decision:1;
    }
};
int wmain(int argc,wchar_t** argv) {
    if(argc!=4)return 2;
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        const auto module=LoadLibraryExW(argv[1],nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
        Check(module!=nullptr,"qualified native capture DLL loaded");
        struct Module {HMODULE value;~Module(){FreeLibrary(value);}} native{module};
        const auto library=reinterpret_cast<decltype(&WrailCaptureWindowLibraryV1)>(GetProcAddress(module,"WrailCaptureWindowLibraryV1"));
        const auto legacy=reinterpret_cast<decltype(&WrailCaptureWindowConfigured)>(GetProcAddress(module,"WrailCaptureWindowConfigured"));
        const auto advanced=reinterpret_cast<decltype(&WrailCaptureWindowConfiguredV4)>(GetProcAddress(module,"WrailCaptureWindowConfiguredV4"));
        const auto identity=reinterpret_cast<decltype(&WrailPreviewReadIdentity)>(GetProcAddress(module,"WrailPreviewReadIdentity"));
        const auto environment=reinterpret_cast<decltype(&WrailGetCaptureEncodingEnvironment)>(GetProcAddress(module,"WrailGetCaptureEncodingEnvironment"));
        Check(library&&legacy&&advanced&&identity&&environment,"library export is additive to existing configured capture exports");
        FixtureProcess source(argv[2]);WrailPreviewTarget target;Hr(identity(reinterpret_cast<uint64_t>(source.Window()),&target));
        WrailCaptureEncodingEnvironment devices;Hr(environment(&devices));
        WrailCaptureEncodingSelection selection;selection.adapterLuid=devices.defaultAdapterLuid;
        const std::filesystem::path directory(argv[3]);std::filesystem::create_directories(directory);
        auto path=[&](const wchar_t* name){return(directory/name).wstring();};
        auto foreground=[&]{SetForegroundWindow(source.Window());Check(GetAncestor(GetForegroundWindow(),GA_ROOT)==source.Window(),"only owned fixture is foreground at capture admission");};
        WrailCaptureOptions options;options.kind=2;options.encoder=2;options.maximumWidth=1280;options.maximumHeight=720;
        options.framesPerSecond=30;options.bitrate=6000000;options.durationSeconds=1;
        options.maximumBytes=WrailCaptureTemporaryMaximumBytes+1;
        LibraryAuthority authority;WrailCaptureResult result;
        auto rejected=path(L"rejected.mp4");
        Check(legacy(&target,rejected.c_str(),&options,LibraryAuthority::Read,&authority,&result)==E_INVALIDARG&&authority.calls==0,
            "legacy configured capture still rejects budgets above128MiB before source access");
        Check(advanced(&target,rejected.c_str(),&options,&selection,1280,720,LibraryAuthority::Read,&authority,&result)==E_INVALIDARG&&authority.calls==0,
            "advanced temporary capture retains the same128MiB cap");
        options.maximumBytes=WrailCaptureLibraryMaximumBytes+1;
        Check(library(&target,rejected.c_str(),&options,nullptr,0,0,LibraryAuthority::Read,&authority,&result)==E_INVALIDARG&&authority.calls==0,
            "library capture rejects budgets above2GiB before source access");
        options.maximumBytes=WrailCaptureLibraryMaximumBytes;options.kind=1;
        Check(library(&target,rejected.c_str(),&options,nullptr,0,0,LibraryAuthority::Read,&authority,&result)==E_INVALIDARG&&authority.calls==0,
            "library recording export does not widen screenshot authority");
        options.kind=2;
        Check(library(&target,rejected.c_str(),&options,nullptr,1280,720,LibraryAuthority::Read,&authority,&result)==E_INVALIDARG&&authority.calls==0,
            "legacy-profile library admission cannot pretend to validate a source-size fence");
        Check(!std::filesystem::exists(rejected),"rejected requests create no output file");
        foreground();options.maximumBytes=WrailCaptureTemporaryMaximumBytes+1;
        auto legacyFile=path(L"library-legacy.mp4");
        Hr(library(&target,legacyFile.c_str(),&options,nullptr,0,0,LibraryAuthority::Read,&authority,&result));
        Check(result.frames>0&&result.width==1280&&result.height==720&&result.encoder==2&&std::filesystem::file_size(legacyFile)<=options.maximumBytes,
            "library lane streams a hardware recording with a budget above the temporary cap");
        foreground();options.maximumBytes=WrailCaptureLibraryMaximumBytes;
        auto exactFile=path(L"library-exact.mp4");result={};
        Hr(library(&target,exactFile.c_str(),&options,&selection,1280,720,LibraryAuthority::Read,&authority,&result));
        Check(result.frames>0&&result.width==1280&&result.height==720&&result.sourceWidth==1280&&result.sourceHeight==720&&result.encoder==2&&std::filesystem::file_size(exactFile)<=options.maximumBytes,
            "2GiB budget preserves exact advanced codec canvas and source admission without preallocating video");
        result={};
        Check(library(&target,rejected.c_str(),&options,&selection,1282,720,LibraryAuthority::Read,&authority,&result)==HRESULT_FROM_WIN32(ERROR_INVALID_WINDOW_HANDLE),
            "library recording preserves stale-source rejection");
        foreground();options.maximumBytes=1024;result={};auto over=path(L"over-budget.mp4");
        Check(library(&target,over.c_str(),&options,nullptr,0,0,LibraryAuthority::Read,&authority,&result)==HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE)&&result.error<0,
            "actual output exceeding a smaller supplied budget is rejected rather than published");
        Check(!std::filesystem::exists(over)||std::filesystem::remove(over),"failed native output releases its file for caller cleanup");
        foreground();options.maximumBytes=WrailCaptureLibraryMaximumBytes;options.durationSeconds=3;result={};
        LibraryAuthority cancel{GetTickCount64()+250,0};auto cancelled=path(L"cancelled.mp4");
        Check(library(&target,cancelled.c_str(),&options,nullptr,0,0,LibraryAuthority::Read,&cancel,&result)==E_ABORT,
            "library capture retains cooperative cancellation");
        Check(!std::filesystem::exists(cancelled)||std::filesystem::remove(cancelled),"cancelled native output releases its file for caller cleanup");
        foreground();result={};LibraryAuthority stop{GetTickCount64()+300,2};auto stopped=path(L"stopped.mp4");
        Hr(library(&target,stopped.c_str(),&options,nullptr,0,0,LibraryAuthority::Read,&stop,&result));
        Check(result.frames>0&&result.durationSeconds>0&&result.durationSeconds<2,"explicit Stop finalizes a shorter library recording");
        foreground();options.maximumBytes=8ULL*1024*1024;options.durationSeconds=1;result={};auto ordinary=path(L"ordinary-8m.mp4");
        Hr(legacy(&target,ordinary.c_str(),&options,LibraryAuthority::Read,&authority,&result));
        Check(result.frames>0&&std::filesystem::file_size(ordinary)<=options.maximumBytes,"ordinary8MiB caller remains usable after library cancellation and stop");
        puts("PASS library recording uses the existing bounded streaming capture engine; declared2GiB budget is not a2GiB allocation");
        return 0;
    } catch(const winrt::hresult_error& error){printf("FAIL library capture HRESULT=%08x\n",static_cast<unsigned>(error.code().value));return 1;}
      catch(const std::exception& error){printf("FAIL library capture %s\n",error.what());return 1;}
}
