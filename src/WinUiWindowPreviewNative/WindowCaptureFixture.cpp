#include <Windows.h>
#include <cstdio>
#include <cstdlib>
LRESULT CALLBACK WindowProc(HWND hwnd, UINT msg, WPARAM w, LPARAM l) {
    if(msg==WM_TIMER){RECT band{};GetClientRect(hwnd,&band);band.top=band.bottom>40?band.bottom-40:0;InvalidateRect(hwnd,&band,FALSE);return 0;}
    if(msg==WM_PAINT){PAINTSTRUCT paint{};HDC dc=BeginPaint(hwnd,&paint);RECT r{};GetClientRect(hwnd,&r);auto left=r;left.right=r.right/2;auto right=r;right.left=left.right;
        auto red=CreateSolidBrush(RGB(255,0,0)),green=CreateSolidBrush(RGB(0,255,0));FillRect(dc,&left,red);FillRect(dc,&right,green);DeleteObject(red);DeleteObject(green);
        RECT marker{static_cast<LONG>((GetTickCount64()/16)%static_cast<ULONGLONG>(r.right+64))-64,r.bottom-40,0,r.bottom};marker.right=marker.left+64;
        auto blue=CreateSolidBrush(RGB(0,0,255));FillRect(dc,&marker,blue);DeleteObject(blue);EndPaint(hwnd,&paint);return 0;}
    if(msg==WM_DESTROY){
#ifndef WRAIL_CAPTURE_VALIDATION
        PostQuitMessage(0);
#endif
        return 0;}
    return DefWindowProcW(hwnd,msg,w,l);
}
HWND CreateFixture(int width=656,int height=400,bool borderless=false){auto instance=GetModuleHandleW(nullptr);WNDCLASSW type{};type.hInstance=instance;type.lpszClassName=L"WidgetRailCaptureFixture";type.lpfnWndProc=WindowProc;
    if(!RegisterClassW(&type) && GetLastError()!=ERROR_CLASS_ALREADY_EXISTS)return nullptr;
    auto hwnd=CreateWindowExW(WS_EX_TOPMOST,type.lpszClassName,L"WidgetRail synthetic capture target",(borderless?WS_POPUP:WS_OVERLAPPEDWINDOW)|WS_VISIBLE,1000,100,width,height,nullptr,nullptr,instance,nullptr);
    if(!hwnd)return nullptr;SetTimer(hwnd,1,16,nullptr);ShowWindow(hwnd,SW_SHOW);UpdateWindow(hwnd);SetForegroundWindow(hwnd);return hwnd;}
#ifdef WRAIL_CAPTURE_VALIDATION
extern "C" __declspec(dllexport) HWND __stdcall WrailCreateCaptureFixture(){return CreateFixture();}
extern "C" __declspec(dllexport) void __stdcall WrailCloseCaptureFixture(HWND hwnd){DestroyWindow(hwnd);}
#else
int wmain(int argc,wchar_t** argv){
    const int width=argc==3?_wtoi(argv[1]):656,height=argc==3?_wtoi(argv[2]):400;
    if(width<1 || height<1 || width>8192 || height>8192 || static_cast<long long>(width)*height>33554432)return 2;
    if(argc==3)SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    auto hwnd=CreateFixture(width,height,argc==3);if(!hwnd)return 2;std::printf("%llu\n",reinterpret_cast<unsigned long long>(hwnd));std::fflush(stdout);
    MSG msg{};while(GetMessageW(&msg,nullptr,0,0)>0){TranslateMessage(&msg);DispatchMessageW(&msg);}return 0;}
#endif
