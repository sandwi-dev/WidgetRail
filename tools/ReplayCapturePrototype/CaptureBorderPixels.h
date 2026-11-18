#pragma once
#include <Windows.h>
#include <winrt/base.h>
#include <vector>
#include <cstdint>
inline uint64_t Yellow(HWND window){
    RECT rect{};winrt::check_bool(GetWindowRect(window,&rect));const int width=rect.right-rect.left+8,height=rect.bottom-rect.top+8;
    const auto screen=GetDC(nullptr),memory=CreateCompatibleDC(screen);const auto bitmap=CreateCompatibleBitmap(screen,width,height);const auto previous=SelectObject(memory,bitmap);
    BitBlt(memory,0,0,width,height,screen,rect.left-4,rect.top-4,SRCCOPY|CAPTUREBLT);
    BITMAPINFO info{};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=width;info.bmiHeader.biHeight=-height;info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;
    std::vector<BYTE> pixels(size_t(width)*height*4);SelectObject(memory,previous);
    const auto copied=GetDIBits(memory,bitmap,0,static_cast<UINT>(height),pixels.data(),&info,DIB_RGB_COLORS);
    if(copied!=height){DeleteObject(bitmap);DeleteDC(memory);ReleaseDC(nullptr,screen);throw winrt::hresult_error(E_FAIL);}
    DeleteObject(bitmap);DeleteDC(memory);ReleaseDC(nullptr,screen);
    uint64_t yellow{};for(int y=0;y<height;++y)for(int x=0;x<width;++x)if(x<8||y<8||x>=width-8||y>=height-8){const auto at=(size_t(y)*width+x)*4;
        if(pixels[at+2]>180&&pixels[at+1]>150&&pixels[at]<100)++yellow;}
    return yellow;
}
