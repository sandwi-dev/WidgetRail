#include <Windows.h>
#include <cstdio>
LRESULT CALLBACK WindowProc(HWND hwnd, UINT msg, WPARAM w, LPARAM l) {
    if(msg==WM_PAINT){PAINTSTRUCT paint{};HDC dc=BeginPaint(hwnd,&paint);RECT r{};GetClientRect(hwnd,&r);auto left=r;left.right=r.right/2;auto right=r;right.left=left.right;
        auto red=CreateSolidBrush(RGB(255,0,0)),green=CreateSolidBrush(RGB(0,255,0));FillRect(dc,&left,red);FillRect(dc,&right,green);DeleteObject(red);DeleteObject(green);EndPaint(hwnd,&paint);return 0;}
    if(msg==WM_DESTROY){
#ifndef WRAIL_CAPTURE_VALIDATION
        PostQuitMessage(0);
#endif
        return 0;}
    return DefWindowProcW(hwnd,msg,w,l);
}
HWND CreateFixture(){auto instance=GetModuleHandleW(nullptr);WNDCLASSW type{};type.hInstance=instance;type.lpszClassName=L"WidgetRailCaptureFixture";type.lpfnWndProc=WindowProc;
    if(!RegisterClassW(&type) && GetLastError()!=ERROR_CLASS_ALREADY_EXISTS)return nullptr;
    auto hwnd=CreateWindowExW(WS_EX_TOPMOST,type.lpszClassName,L"WidgetRail synthetic capture target",WS_OVERLAPPEDWINDOW|WS_VISIBLE,1000,100,656,400,nullptr,nullptr,instance,nullptr);
    if(!hwnd)return nullptr;ShowWindow(hwnd,SW_SHOW);UpdateWindow(hwnd);SetForegroundWindow(hwnd);return hwnd;}
#ifdef WRAIL_CAPTURE_VALIDATION
extern "C" __declspec(dllexport) HWND __stdcall WrailCreateCaptureFixture(){return CreateFixture();}
extern "C" __declspec(dllexport) void __stdcall WrailCloseCaptureFixture(HWND hwnd){DestroyWindow(hwnd);}
#else
int wmain(){auto hwnd=CreateFixture();if(!hwnd)return 2;std::printf("%llu\n",reinterpret_cast<unsigned long long>(hwnd));std::fflush(stdout);
    MSG msg{};while(GetMessageW(&msg,nullptr,0,0)>0){TranslateMessage(&msg);DispatchMessageW(&msg);}return 0;}
#endif
