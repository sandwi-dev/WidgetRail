#include "EncodingQualityImages.h"
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <dwmapi.h>
#include <mutex>
#include <atomic>
using TextureCallback=void(__stdcall*)(uint32_t,uint32_t,uint32_t,uint32_t,int64_t,ID3D11Texture2D*);
using ContinueCallback=int32_t(__stdcall*)();
using AudioCallback=void(__stdcall*)(uint32_t,uint32_t,uint32_t,int64_t,const void*,uint32_t);
using RunTexture=int32_t(__stdcall*)(uint64_t,uint32_t,uint64_t,const wchar_t*,uint32_t,uint32_t,uint32_t,uint32_t,uint32_t,uint64_t,ContinueCallback,AudioCallback,TextureCallback,uint32_t*) noexcept;
struct OwnedWindow {
    HWND window{};HANDLE ready{CreateEventW(nullptr,TRUE,FALSE,nullptr)};std::thread ui;std::mutex gate;
    UINT width{1919},height{839};std::vector<BYTE> pixels{Source(width,height)};
    static LRESULT CALLBACK Procedure(HWND hwnd,UINT message,WPARAM wparam,LPARAM lparam){
        auto self=reinterpret_cast<OwnedWindow*>(GetWindowLongPtrW(hwnd,GWLP_USERDATA));
        if(message==WM_NCCREATE){self=static_cast<OwnedWindow*>(reinterpret_cast<CREATESTRUCTW*>(lparam)->lpCreateParams);SetWindowLongPtrW(hwnd,GWLP_USERDATA,reinterpret_cast<LONG_PTR>(self));}
        if(self&&message==WM_PAINT){PAINTSTRUCT paint{};const auto dc=BeginPaint(hwnd,&paint);std::lock_guard lock(self->gate);
            BITMAPINFO info{};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=static_cast<LONG>(self->width);info.bmiHeader.biHeight=-static_cast<LONG>(self->height);
            info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
            SetDIBitsToDevice(dc,0,0,self->width,self->height,0,0,0,self->height,self->pixels.data(),&info,DIB_RGB_COLORS);EndPaint(hwnd,&paint);SetEvent(self->ready);return 0;}
        if(self&&message==WM_APP+1){
            if(!SetWindowPos(hwnd,nullptr,0,0,static_cast<int>(wparam),static_cast<int>(lparam),SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE))return 0;
            InvalidateRect(hwnd,nullptr,FALSE);UpdateWindow(hwnd);return 1;
        }
        if(message==WM_CLOSE){DestroyWindow(hwnd);return 0;}if(message==WM_DESTROY){PostQuitMessage(0);return 0;}
        return DefWindowProcW(hwnd,message,wparam,lparam);
    }
    OwnedWindow(){
        ui=std::thread([this]{SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);WNDCLASSW type{};type.style=CS_HREDRAW|CS_VREDRAW;type.lpfnWndProc=Procedure;type.hInstance=GetModuleHandleW(nullptr);type.lpszClassName=L"WidgetRailQualityPixelFixture";
            RegisterClassW(&type);window=CreateWindowExW(WS_EX_TOPMOST,type.lpszClassName,L"Owned capture pixel fixture",WS_POPUP|WS_VISIBLE,40,40,static_cast<int>(width),static_cast<int>(height),nullptr,nullptr,type.hInstance,this);
            if(!window){SetEvent(ready);return;}UpdateWindow(window);MSG message;while(GetMessageW(&message,nullptr,0,0)>0){TranslateMessage(&message);DispatchMessageW(&message);}});
        if(WaitForSingleObject(ready,5000)!=WAIT_OBJECT_0||!window)throw std::runtime_error("owned pixel window not ready");
    }
    void Resize(UINT w,UINT h){ResetEvent(ready);{std::lock_guard lock(gate);width=w;height=h;pixels=Source(w,h);}
        DWORD_PTR resized{};winrt::check_bool(SendMessageTimeoutW(window,WM_APP+1,w,h,SMTO_ABORTIFHUNG,5000,&resized)!=0&&resized==1);
        if(WaitForSingleObject(ready,5000)!=WAIT_OBJECT_0)throw std::runtime_error("owned resized window not ready");
        RECT actual{};winrt::check_bool(GetWindowRect(window,&actual));if(actual.right-actual.left!=static_cast<LONG>(w)||actual.bottom-actual.top!=static_cast<LONG>(h))throw std::runtime_error("owned resize geometry did not commit");
        printf("WINDOW source=%ux%u committed=1\n",w,h);fflush(stdout);
    }
    ~OwnedWindow(){if(window)PostMessageW(window,WM_CLOSE,0,0);if(ui.joinable())ui.join();CloseHandle(ready);}
};
struct PixelCase{UINT sourceW{},sourceH{},canvasW{},canvasH{},left{},top{};std::vector<BYTE> reference;bool scaled{},passed{};uint64_t mismatched{};};
std::mutex caseGate;PixelCase currentCase;std::atomic<bool> running{true};std::wstring outputPrefix;std::atomic<int32_t> runResult{S_OK};
int32_t __stdcall Continue(){return running?1:0;}
void __stdcall Audio(uint32_t,uint32_t,uint32_t,int64_t,const void*,uint32_t){}
void __stdcall Frame(uint32_t width,uint32_t height,uint32_t sourceW,uint32_t sourceH,int64_t,ID3D11Texture2D* texture){
    try{
        std::lock_guard lock(caseGate);auto& expected=currentCase;
        if(expected.passed||sourceW!=expected.sourceW||sourceH!=expected.sourceH)return;
        if(width!=expected.canvasW||height!=expected.canvasH)throw std::runtime_error("unexpected padded/fixed canvas");
        ComPtr<ID3D11Device> device;texture->GetDevice(&device);ComPtr<ID3D11DeviceContext> context;device->GetImmediateContext(&context);
        D3D11_TEXTURE2D_DESC desc{};texture->GetDesc(&desc);desc.Usage=D3D11_USAGE_STAGING;desc.BindFlags=0;desc.CPUAccessFlags=D3D11_CPU_ACCESS_READ;
        ComPtr<ID3D11Texture2D> readback;winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&readback));context->CopyResource(readback.Get(),texture);
        D3D11_MAPPED_SUBRESOURCE mapped{};winrt::check_hresult(context->Map(readback.Get(),0,D3D11_MAP_READ,0,&mapped));
        std::vector<BYTE> pixels(size_t(width)*height*4);for(UINT y=0;y<height;++y)memcpy(pixels.data()+size_t(y)*width*4,static_cast<BYTE*>(mapped.pData)+size_t(y)*mapped.RowPitch,size_t(width)*4);context->Unmap(readback.Get(),0);
        uint64_t mismatched{},padding{};
        if(expected.scaled){
            // 2560x1440 must fit the retained1920x840canvas: full-height picture
            // spans approximatelyx213.33..1706.67, with black sidebars.
            for(UINT y=0;y<height;++y)for(UINT x=0;x<width;++x)if(x<212||x>=1708){const auto at=(size_t(y)*width+x)*4;if(pixels[at]||pixels[at+1]||pixels[at+2])++padding;}
            const auto left=(size_t(200)*width+225)*4,right=(size_t(700)*width+1690)*4;
            if(!padding&&(pixels[left]||pixels[left+1]||pixels[left+2])&&(pixels[right]||pixels[right+1]||pixels[right+2])){
                expected.passed=true;Png(outputPrefix+L".larger-source-fit.png",width,height,pixels);
                printf("FIT source=%ux%u canvas=%ux%u blackOutsideFit=%llu imageAtBothFitEdges=1\n",sourceW,sourceH,width,height,padding);fflush(stdout);
            }
            expected.mismatched=padding;return;
        }
        for(UINT y=0;y<height;++y)for(UINT x=0;x<width;++x){const auto at=(size_t(y)*width+x)*4;
            if(x>=expected.left&&x<expected.left+sourceW&&y>=expected.top&&y<expected.top+sourceH){const auto sx=x-expected.left,sy=y-expected.top;
                // Exclude only window-edge decoration/corner masks; all text,
                // one-pixel grids and gradients remain exact interior checks.
                if(sx<16||sy<16||sx+16>=sourceW||sy+16>=sourceH)continue;
                const auto original=(size_t(sy)*sourceW+sx)*4;for(unsigned channel=0;channel<3;++channel)if(pixels[at+channel]!=expected.reference[original+channel])++mismatched;
            }else if(pixels[at]||pixels[at+1]||pixels[at+2])++padding;
        }
        expected.mismatched=mismatched+padding;
        if(!mismatched&&!padding){expected.passed=true;Png(outputPrefix+(sourceW==1919?L".odd-native.png":L".smaller-source.png"),width,height,pixels);
            printf("PIXELS source=%ux%u canvas=%ux%u offset=%u,%u interiorMismatch=%llu paddingMismatch=%llu\n",sourceW,sourceH,width,height,expected.left,expected.top,mismatched,padding);fflush(stdout);}
    }catch(...){runResult=E_FAIL;running=false;}
}
#include "CaptureBorderPixels.h"

int wmain(int argc,wchar_t** argv){
    if(argc!=3)return 2;winrt::init_apartment(winrt::apartment_type::multi_threaded);SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    const auto module=LoadLibraryExW(argv[1],nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);if(!module)return 1;
    struct Library{HMODULE value;~Library(){FreeLibrary(value);}} library{module};
    auto capture=reinterpret_cast<RunTexture>(GetProcAddress(module,"WrailLiveCaptureRunTextureV3"));if(!capture)return 1;
    try{
        outputPrefix=argv[2];OwnedWindow source;const auto before=Yellow(source.window);
        currentCase={1919,839,1920,840,0,0,Source(1919,839)};
        FILETIME created{},exited{},kernel{},user{};winrt::check_bool(GetProcessTimes(GetCurrentProcess(),&created,&exited,&kernel,&user));
        const auto birth=(uint64_t(created.dwHighDateTime)<<32)|created.dwLowDateTime;
        std::thread recording([&]{uint32_t failed{};runResult=capture(reinterpret_cast<uint64_t>(source.window),GetCurrentProcessId(),birth,L"WidgetRailQualityPixelFixture",0,1920,1080,30,0,0,Continue,Audio,Frame,&failed);});
        struct CaptureEnd{std::thread& thread;~CaptureEnd(){running=false;if(thread.joinable())thread.join();}} captureEnd{recording};
        const auto wait=[] {const auto deadline=GetTickCount64()+8000;while(GetTickCount64()<deadline){
            {std::lock_guard lock(caseGate);if(currentCase.passed)return;}if(!running||FAILED(runResult))break;Sleep(10);}
            std::lock_guard lock(caseGate);printf("pixelMismatch=%llu\n",currentCase.mismatched);throw std::runtime_error("exact native pixel comparison failed");};
        wait();const auto requested=Yellow(source.window);
        // A simultaneous ordinary bordered session is the positive control for
        // the screen-edge detector, and exercises Windows' shared indicator rule.
        uint64_t forced{};
        {
            using namespace winrt::Windows::Graphics::Capture;
            auto device=CreateEncodingDevice();ComPtr<IDXGIDevice> dxgi;winrt::check_hresult(device.As(&dxgi));
            winrt::com_ptr<IInspectable> inspectable;winrt::check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.Get(),inspectable.put()));
            auto direct=inspectable.as<winrt::Windows::Graphics::DirectX::Direct3D11::IDirect3DDevice>();
            GraphicsCaptureItem item{nullptr};auto interop=winrt::get_activation_factory<GraphicsCaptureItem,IGraphicsCaptureItemInterop>();
            winrt::check_hresult(interop->CreateForWindow(source.window,winrt::guid_of<GraphicsCaptureItem>(),winrt::put_abi(item)));
            auto pool=Direct3D11CaptureFramePool::CreateFreeThreaded(direct,winrt::Windows::Graphics::DirectX::DirectXPixelFormat::B8G8R8A8UIntNormalized,2,item.Size());
            auto control=pool.CreateCaptureSession(item);control.IsBorderRequired(true);control.StartCapture();
            const auto deadline=GetTickCount64()+3000;do{DwmFlush();forced=Yellow(source.window);if(forced>200)break;Sleep(10);}while(GetTickCount64()<deadline);
            control.Close();pool.Close();
            if(forced<=200)throw std::runtime_error("border positive control did not produce visible indicator");
        }
        {std::lock_guard lock(caseGate);currentCase={800,600,1920,840,560,120,Source(800,600)};}
        source.Resize(800,600);wait();
        {std::lock_guard lock(caseGate);currentCase={2560,1440,1920,840,0,0,{},true};}
        source.Resize(2560,1440);wait();
        printf("BORDER beforeYellowPixels=%llu requestedBorderlessYellowPixels=%llu forcedBorderPositivePixels=%llu (optionalpermissionfallback allowed)\n",before,requested,forced);
        running=false;recording.join();if(FAILED(runResult))throw std::runtime_error("texture capture failed");
        puts("PASS real WGC odd-source padding and smaller-source exact copy; larger source remains bounded in fixed canvas");return 0;
    }catch(const std::exception& e){printf("FAIL pixels %s native=%08x\n",e.what(),static_cast<unsigned>(runResult.load()));return 1;}
    catch(const winrt::hresult_error& e){printf("FAIL pixels HRESULT=%08x\n",static_cast<unsigned>(e.code().value));return 1;}
}
