#pragma once
// Matched 1:1 owned UI/text/colour source versus decoded recording. Readback is
// test-only; the production encoder keeps its GPU-resident three-surface path.
#include "../../src/WinUiWindowPreviewNative/CaptureVideoEncoder.h"
#include "../../src/WinUiWindowPreviewNative/ReplaySampleOutput.h"
#include <wincodec.h>
#include <cstdio>
#include <cmath>
#include <string>
#include <chrono>
#include <thread>
using namespace widgetrail::capture;
inline void Png(const std::wstring& path,UINT width,UINT height,const std::vector<BYTE>& pixels){
    ComPtr<IWICImagingFactory> factory;winrt::check_hresult(CoCreateInstance(CLSID_WICImagingFactory2,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&factory)));
    ComPtr<IWICStream> stream;winrt::check_hresult(factory->CreateStream(&stream));winrt::check_hresult(stream->InitializeFromFilename(path.c_str(),GENERIC_WRITE));
    ComPtr<IWICBitmapEncoder> encoder;winrt::check_hresult(factory->CreateEncoder(GUID_ContainerFormatPng,nullptr,&encoder));winrt::check_hresult(encoder->Initialize(stream.Get(),WICBitmapEncoderNoCache));
    ComPtr<IWICBitmapFrameEncode> frame;winrt::check_hresult(encoder->CreateNewFrame(&frame,nullptr));winrt::check_hresult(frame->Initialize(nullptr));
    winrt::check_hresult(frame->SetSize(width,height));auto format=GUID_WICPixelFormat32bppBGRA;winrt::check_hresult(frame->SetPixelFormat(&format));
    if(format!=GUID_WICPixelFormat32bppBGRA)throw winrt::hresult_error(E_UNEXPECTED);
    winrt::check_hresult(frame->WritePixels(height,width*4,static_cast<UINT>(pixels.size()),const_cast<BYTE*>(pixels.data())));
    winrt::check_hresult(frame->Commit());winrt::check_hresult(encoder->Commit());
}
inline std::vector<BYTE> Source(UINT width,UINT height){
    std::vector<BYTE> pixels(size_t(width)*height*4);
    for(UINT y=0;y<height;++y)for(UINT x=0;x<width;++x){
        auto p=&pixels[(size_t(y)*width+x)*4];p[3]=255;
        if(x<width/2){p[0]=p[1]=p[2]=static_cast<BYTE>(24+y*32/height);}
        else if(y<height/3){const auto value=static_cast<BYTE>((x&1)?235:25);p[0]=p[1]=p[2]=value;}
        else if(y<height*2/3){p[0]=static_cast<BYTE>((x&1)?245:15);p[1]=35;p[2]=static_cast<BYTE>((x&1)?15:245);}
        else{p[0]=static_cast<BYTE>(x*255/width);p[1]=static_cast<BYTE>(y*255/height);p[2]=static_cast<BYTE>((x+y)*255/(width+height));}
    }
    BITMAPINFO info{};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=static_cast<LONG>(width);info.bmiHeader.biHeight=-static_cast<LONG>(height);
    info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;info.bmiHeader.biCompression=BI_RGB;
    void* bits{};const auto bitmap=CreateDIBSection(nullptr,&info,DIB_RGB_COLORS,&bits,nullptr,0);winrt::check_bool(bitmap!=nullptr);
    const auto dc=CreateCompatibleDC(nullptr);winrt::check_bool(dc!=nullptr);const auto previous=SelectObject(dc,bitmap);memcpy(bits,pixels.data(),pixels.size());
    SetBkMode(dc,TRANSPARENT);
    for(int size: {14,18,24,32}){
        const auto font=CreateFontW(-size,0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,DEFAULT_CHARSET,OUT_DEFAULT_PRECIS,CLIP_DEFAULT_PRECIS,ANTIALIASED_QUALITY,FIXED_PITCH,L"Consolas");
        const auto oldFont=SelectObject(dc,font);const auto top=(size-14)*16+30;
        for(int row=0;row<4;++row){SetTextColor(dc,row==0?RGB(240,240,240):row==1?RGB(255,85,85):row==2?RGB(80,190,255):RGB(100,255,150));
            const wchar_t* text=L"WidgetRail 1:1 AaBbCc 0123456789 / [] <>";
            TextOutW(dc,24,top+row*(size+4),text,static_cast<int>(wcslen(text)));}
        SelectObject(dc,oldFont);DeleteObject(font);
    }
    GdiFlush();memcpy(pixels.data(),bits,pixels.size());SelectObject(dc,previous);DeleteDC(dc);DeleteObject(bitmap);
    for(size_t index=3;index<pixels.size();index+=4)pixels[index]=255;
    return pixels;
}
inline std::vector<BYTE> Decode(const std::wstring& path,ID3D11Device* device,UINT expectedWidth,UINT expectedHeight){
    UINT reset{};ComPtr<IMFDXGIDeviceManager> manager;winrt::check_hresult(MFCreateDXGIDeviceManager(&reset,&manager));winrt::check_hresult(manager->ResetDevice(device,reset));
    ComPtr<IMFAttributes> attributes;winrt::check_hresult(MFCreateAttributes(&attributes,3));
    winrt::check_hresult(attributes->SetUnknown(MF_SOURCE_READER_D3D_MANAGER,manager.Get()));winrt::check_hresult(attributes->SetUINT32(MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS,TRUE));
    winrt::check_hresult(attributes->SetUINT32(MF_SOURCE_READER_ENABLE_ADVANCED_VIDEO_PROCESSING,TRUE));
    ComPtr<IMFSourceReader> reader;winrt::check_hresult(MFCreateSourceReaderFromURL(path.c_str(),attributes.Get(),&reader));
    ComPtr<IMFMediaType> output;winrt::check_hresult(MFCreateMediaType(&output));winrt::check_hresult(output->SetGUID(MF_MT_MAJOR_TYPE,MFMediaType_Video));winrt::check_hresult(output->SetGUID(MF_MT_SUBTYPE,MFVideoFormat_RGB32));
    winrt::check_hresult(reader->SetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),nullptr,output.Get()));
    for(int attempt=0;attempt<16;++attempt){
        DWORD flags{};ComPtr<IMFSample> sample;winrt::check_hresult(reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),0,nullptr,&flags,nullptr,&sample));
        if(!sample)continue;
        ComPtr<IMFMediaType> actual;winrt::check_hresult(reader->GetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_VIDEO_STREAM),&actual));
        UINT width{},height{};winrt::check_hresult(MFGetAttributeSize(actual.Get(),MF_MT_FRAME_SIZE,&width,&height));
        MFVideoArea area{};UINT32 size{};const auto aperture=SUCCEEDED(actual->GetBlob(MF_MT_MINIMUM_DISPLAY_APERTURE,reinterpret_cast<UINT8*>(&area),sizeof(area),&size));
        const UINT visibleW=aperture?static_cast<UINT>(area.Area.cx):width,visibleH=aperture?static_cast<UINT>(area.Area.cy):height;
        if(visibleW!=expectedWidth||visibleH!=expectedHeight||aperture&&(area.OffsetX.value!=0||area.OffsetY.value!=0||area.OffsetX.fract||area.OffsetY.fract))throw winrt::hresult_error(MF_E_INVALIDMEDIATYPE);
        ComPtr<IMFMediaBuffer> buffer;winrt::check_hresult(sample->ConvertToContiguousBuffer(&buffer));
        ComPtr<IMF2DBuffer> planar;winrt::check_hresult(buffer.As(&planar));BYTE* row{};LONG pitch{};winrt::check_hresult(planar->Lock2D(&row,&pitch));
        std::vector<BYTE> result(size_t(visibleW)*visibleH*4);
        for(UINT y=0;y<visibleH;++y)memcpy(result.data()+size_t(y)*visibleW*4,row+int64_t(y)*pitch,size_t(visibleW)*4);
        winrt::check_hresult(planar->Unlock2D());for(size_t index=3;index<result.size();index+=4)result[index]=255;return result;
    }
    throw winrt::hresult_error(MF_E_END_OF_STREAM);
}
