// Isolated WGC input for WgcReplayWorkerProbe. Seven constant scRGB patches
// let a decoder verify that an actual saved clip retains values above SDR white.
#include <windows.h>
#include <mmsystem.h>
#include <d3d11_1.h>
#include <dxgi1_6.h>
#include <wrl/client.h>
#include <winrt/base.h>
#include <cstdio>
#include <array>

using Microsoft::WRL::ComPtr;
static LRESULT CALLBACK Events(HWND window, UINT message, WPARAM w, LPARAM l) {
    if (message==WM_DESTROY) { PostQuitMessage(0); return 0; }
    return DefWindowProcW(window,message,w,l);
}
int wmain(int argc, wchar_t** argv) {
    try {
        const bool list = argc == 2 && wcscmp(argv[1],L"--list") == 0;
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        ComPtr<IDXGIFactory2> factory;
        winrt::check_hresult(CreateDXGIFactory1(IID_PPV_ARGS(&factory)));
        DXGI_OUTPUT_DESC1 display{};
        ComPtr<IDXGIAdapter1> selected;
        for (UINT a=0; list || !selected; ++a) {
            ComPtr<IDXGIAdapter1> adapter;
            if (factory->EnumAdapters1(a,&adapter)!=S_OK) break;
            for (UINT i=0;;++i) {
                ComPtr<IDXGIOutput> output;
                if (adapter->EnumOutputs(i,&output)!=S_OK) break;
                ComPtr<IDXGIOutput6> hdr;
                if (FAILED(output.As(&hdr)) || FAILED(hdr->GetDesc1(&display))) continue;
                if (list) {
                    std::printf("%ls active=%d colorSpace=%d peak=%.1f fullScreen=%.1f nits\n",
                        display.DeviceName,display.AttachedToDesktop,int(display.ColorSpace),display.MaxLuminance,display.MaxFullFrameLuminance);
                } else if (display.AttachedToDesktop && display.ColorSpace==DXGI_COLOR_SPACE_RGB_FULL_G2084_NONE_P2020) { selected=adapter; break; }
            }
        }
        if (list) return 0;
        if (!selected) { puts("No active HDR output");return 77; }
        const auto instance=GetModuleHandleW(nullptr);
        WNDCLASSW type{}; type.hInstance=instance; type.lpszClassName=L"WidgetRailHdrRamp";type.lpfnWndProc=Events;
        winrt::check_bool(RegisterClassW(&type));
        constexpr UINT width=1280,height=720, patches=7;
        auto hwnd=CreateWindowExW(WS_EX_NOACTIVATE,type.lpszClassName,L"WidgetRail HDR capture test",WS_POPUP,
            display.DesktopCoordinates.left+20,display.DesktopCoordinates.top+20,width,height,nullptr,nullptr,instance,nullptr);
        winrt::check_bool(hwnd!=nullptr);
        ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> immediate; ComPtr<ID3D11DeviceContext1> dc;
        winrt::check_hresult(D3D11CreateDevice(selected.Get(),D3D_DRIVER_TYPE_UNKNOWN,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,
            nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&immediate));
        winrt::check_hresult(immediate.As(&dc));
        DXGI_SWAP_CHAIN_DESC1 desc{};desc.Width=width;desc.Height=height;desc.Format=DXGI_FORMAT_R16G16B16A16_FLOAT;
        desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_DISCARD;desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT;desc.BufferCount=2;desc.SampleDesc.Count=1;
        ComPtr<IDXGISwapChain1> chain;ComPtr<IDXGISwapChain3> color;
        winrt::check_hresult(factory->CreateSwapChainForHwnd(device.Get(),hwnd,&desc,nullptr,nullptr,&chain));
        winrt::check_hresult(chain.As(&color));
        winrt::check_hresult(color->SetColorSpace1(DXGI_COLOR_SPACE_RGB_FULL_G10_NONE_P709));
        ComPtr<ID3D11Texture2D> surface;ComPtr<ID3D11RenderTargetView> view;
        winrt::check_hresult(chain->GetBuffer(0,IID_PPV_ARGS(&surface)));
        winrt::check_hresult(device->CreateRenderTargetView(surface.Get(),nullptr,&view));
        ShowWindow(hwnd,SW_SHOWNOACTIVATE);
        // An active silent PCM stream exercises the application-audio replay path.
        std::array<unsigned char, 44 + 16000> wave{};
        const unsigned char header[]{'R','I','F','F',0xa4,0x3e,0,0,'W','A','V','E','f','m','t',' ',16,0,0,0,
            1,0,1,0,0x40,0x1f,0,0,0x80,0x3e,0,0,2,0,16,0,'d','a','t','a',0x80,0x3e,0,0};
        memcpy(wave.data(),header,sizeof(header));
        winrt::check_bool(PlaySoundW(reinterpret_cast<const wchar_t*>(wave.data()),nullptr,SND_MEMORY|SND_ASYNC|SND_LOOP|SND_NODEFAULT));
        constexpr float gray[patches]={0,.25f,1,2,4,8,12.5f};
        MSG msg{};
        while (IsWindow(hwnd)) {
            while(PeekMessageW(&msg,nullptr,0,0,PM_REMOVE)) {TranslateMessage(&msg);DispatchMessageW(&msg);}
            if (!IsWindow(hwnd)) break;
            for (UINT i=0;i<patches;++i) {
                RECT rect{LONG(width*i/patches),0,LONG(width*(i+1)/patches),LONG(height)};
                float rgb[]{gray[i],gray[i],gray[i],1};
                dc->ClearView(view.Get(),rgb,&rect,1);
            }
            winrt::check_hresult(chain->Present(1,0));
            Sleep(12);
        }
        PlaySoundW(nullptr,nullptr,0);
        return 0;
    } catch (const winrt::hresult_error& error) { std::printf("HDR fixture HRESULT %08x\n",unsigned(error.code().value));return 1; }
}
