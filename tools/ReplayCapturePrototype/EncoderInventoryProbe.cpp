// Read-only installed DXGI/Media Foundation inventory; no window capture or encode.
#include <Windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mftransform.h>
#include <wrl/client.h>
#include <winrt/base.h>
#include <cstdio>
using Microsoft::WRL::ComPtr;
int wmain() {
    winrt::init_apartment(winrt::apartment_type::multi_threaded);
    try {
        winrt::check_hresult(MFStartup(MF_VERSION));
        ComPtr<IDXGIFactory1> factory;winrt::check_hresult(CreateDXGIFactory1(IID_PPV_ARGS(&factory)));
        ComPtr<ID3D11Device> device;ComPtr<ID3D11DeviceContext> context;
        winrt::check_hresult(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,D3D11_CREATE_DEVICE_BGRA_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context));
        ComPtr<IDXGIDevice> dxgi;winrt::check_hresult(device.As(&dxgi));ComPtr<IDXGIAdapter> actual;winrt::check_hresult(dxgi->GetAdapter(&actual));
        DXGI_ADAPTER_DESC chosen{};winrt::check_hresult(actual->GetDesc(&chosen));
        wprintf(L"DEFAULT adapter=%ls luid=%08lx:%08lx\n",chosen.Description,static_cast<unsigned long>(chosen.AdapterLuid.HighPart),chosen.AdapterLuid.LowPart);
        struct NamedType{const wchar_t* name;GUID subtype;};
        const NamedType codecs[]={{L"H264",MFVideoFormat_H264},{L"HEVC",MFVideoFormat_HEVC},{L"AV1",MFVideoFormat_AV1}};
        const NamedType formats[]={{L"NV12",MFVideoFormat_NV12},{L"P010",MFVideoFormat_P010},{L"AYUV444",MFVideoFormat_AYUV},{L"Y410444",MFVideoFormat_Y410},{L"RGB32",MFVideoFormat_RGB32},{L"ARGB32",MFVideoFormat_ARGB32}};
        for(UINT index=0;index<8;++index){
            ComPtr<IDXGIAdapter1> adapter;if(factory->EnumAdapters1(index,&adapter)==DXGI_ERROR_NOT_FOUND)break;
            DXGI_ADAPTER_DESC1 desc{};winrt::check_hresult(adapter->GetDesc1(&desc));
            LARGE_INTEGER driver{};const auto driverResult=adapter->CheckInterfaceSupport(__uuidof(IDXGIDevice),&driver);
            wprintf(L"ADAPTER index=%u name=%ls luid=%08lx:%08lx vendor=%04x device=%04x software=%u driverHr=%08x driver=%lld\n",
                index,desc.Description,static_cast<unsigned long>(desc.AdapterLuid.HighPart),desc.AdapterLuid.LowPart,desc.VendorId,desc.DeviceId,
                (desc.Flags&DXGI_ADAPTER_FLAG_SOFTWARE)!=0,static_cast<unsigned>(driverResult),driver.QuadPart);
            if(desc.Flags&DXGI_ADAPTER_FLAG_SOFTWARE)continue;
            ComPtr<IMFAttributes> attributes;winrt::check_hresult(MFCreateAttributes(&attributes,1));
            winrt::check_hresult(attributes->SetBlob(MFT_ENUM_ADAPTER_LUID,reinterpret_cast<const BYTE*>(&desc.AdapterLuid),sizeof(desc.AdapterLuid)));
            for(const auto& codec:codecs)for(const auto& format:formats){
                MFT_REGISTER_TYPE_INFO input{MFMediaType_Video,format.subtype},output{MFMediaType_Video,codec.subtype};
                IMFActivate** transforms{};UINT32 count{};
                const auto result=MFTEnum2(MFT_CATEGORY_VIDEO_ENCODER,MFT_ENUM_FLAG_HARDWARE|MFT_ENUM_FLAG_SORTANDFILTER,&input,&output,attributes.Get(),&transforms,&count);
                wprintf(L"MFT codec=%ls input=%ls hr=%08x count=%u",codec.name,format.name,static_cast<unsigned>(result),count);
                for(UINT32 transform=0;transform<count;++transform){
                    wchar_t name[256]{};UINT32 length{};transforms[transform]->GetString(MFT_FRIENDLY_NAME_Attribute,name,256,&length);
                    UINT32 hardwareLength{};const auto hardware=SUCCEEDED(transforms[transform]->GetStringLength(MFT_ENUM_HARDWARE_URL_Attribute,&hardwareLength))&&hardwareLength;
                    wprintf(L" [%ls hardware=%u]",name,hardware?1u:0u);transforms[transform]->Release();
                }
                if(transforms)CoTaskMemFree(transforms);wprintf(L"\n");
            }
        }
        MFShutdown();return 0;
    }catch(const winrt::hresult_error& error){wprintf(L"FAIL hr=%08x\n",static_cast<unsigned>(error.code().value));MFShutdown();return 1;}
}
