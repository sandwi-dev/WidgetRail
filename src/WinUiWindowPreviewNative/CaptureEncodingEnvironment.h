#pragma once
#include <Windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <wrl/client.h>
#include <winrt/base.h>
#include <array>
#include <cstdint>

struct WrailEncodingAdapter {
    uint64_t luid{},driverVersion{};
    uint32_t vendorId{},deviceId{},flags{},reserved{};
    wchar_t name[128]{};
};
static_assert(sizeof(WrailEncodingAdapter)==288);
struct WrailCaptureEncodingEnvironment {
    uint32_t size{sizeof(WrailCaptureEncodingEnvironment)},version{1},count{},reserved{};
    uint64_t defaultAdapterLuid{};
    WrailEncodingAdapter adapters[8]{};
};
static_assert(sizeof(WrailCaptureEncodingEnvironment)==2328);
namespace widgetrail::capture {
using Microsoft::WRL::ComPtr;
inline uint64_t EncodingAdapterId(LUID luid) noexcept {return (uint64_t(static_cast<uint32_t>(luid.HighPart))<<32)|luid.LowPart;}
inline WrailEncodingAdapter DescribeEncodingAdapter(IDXGIAdapter1* adapter) {
    DXGI_ADAPTER_DESC1 desc{};winrt::check_hresult(adapter->GetDesc1(&desc));
    LARGE_INTEGER driver{};winrt::check_hresult(adapter->CheckInterfaceSupport(__uuidof(IDXGIDevice),&driver));
    WrailEncodingAdapter result;result.luid=EncodingAdapterId(desc.AdapterLuid);result.driverVersion=static_cast<uint64_t>(driver.QuadPart);
    result.vendorId=desc.VendorId;result.deviceId=desc.DeviceId;result.flags=desc.Flags;
    wcsncpy_s(result.name,desc.Description,_TRUNCATE);return result;
}
inline WrailCaptureEncodingEnvironment ReadEncodingEnvironment() {
    ComPtr<IDXGIFactory1> factory;winrt::check_hresult(CreateDXGIFactory1(IID_PPV_ARGS(&factory)));
    WrailCaptureEncodingEnvironment result;
    for(UINT index=0;index<32;++index){
        ComPtr<IDXGIAdapter1> adapter;const auto hr=factory->EnumAdapters1(index,&adapter);
        if(hr==DXGI_ERROR_NOT_FOUND)break;winrt::check_hresult(hr);
        DXGI_ADAPTER_DESC1 desc{};winrt::check_hresult(adapter->GetDesc1(&desc));
        if(index==0)result.defaultAdapterLuid=EncodingAdapterId(desc.AdapterLuid);
        if(desc.Flags&DXGI_ADAPTER_FLAG_SOFTWARE)continue;
        if(result.count==std::size(result.adapters))throw winrt::hresult_error(E_BOUNDS);
        result.adapters[result.count++]=DescribeEncodingAdapter(adapter.Get());
    }
    return result;
}
inline ComPtr<ID3D11Device> CreateEncodingDevice(uint64_t selectedAdapter=0) {
    ComPtr<IDXGIAdapter1> adapter;
    if(selectedAdapter){
        ComPtr<IDXGIFactory1> factory;winrt::check_hresult(CreateDXGIFactory1(IID_PPV_ARGS(&factory)));
        for(UINT index=0;index<32;++index){
            ComPtr<IDXGIAdapter1> candidate;const auto hr=factory->EnumAdapters1(index,&candidate);
            if(hr==DXGI_ERROR_NOT_FOUND)break;winrt::check_hresult(hr);
            DXGI_ADAPTER_DESC1 desc{};winrt::check_hresult(candidate->GetDesc1(&desc));
            if(EncodingAdapterId(desc.AdapterLuid)==selectedAdapter&&!(desc.Flags&DXGI_ADAPTER_FLAG_SOFTWARE)){adapter=candidate;break;}
        }
        if(!adapter)throw winrt::hresult_error(DXGI_ERROR_NOT_FOUND);
    }
    ComPtr<ID3D11Device> device;
    winrt::check_hresult(D3D11CreateDevice(adapter.Get(),adapter?D3D_DRIVER_TYPE_UNKNOWN:D3D_DRIVER_TYPE_HARDWARE,nullptr,
        D3D11_CREATE_DEVICE_BGRA_SUPPORT|D3D11_CREATE_DEVICE_VIDEO_SUPPORT,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,nullptr));
    return device;
}
inline uint64_t EncodingDeviceAdapter(ID3D11Device* device) {
    ComPtr<IDXGIDevice> dxgi;winrt::check_hresult(device->QueryInterface(IID_PPV_ARGS(&dxgi)));
    ComPtr<IDXGIAdapter> adapter;winrt::check_hresult(dxgi->GetAdapter(&adapter));DXGI_ADAPTER_DESC desc{};winrt::check_hresult(adapter->GetDesc(&desc));
    return EncodingAdapterId(desc.AdapterLuid);
}
}
