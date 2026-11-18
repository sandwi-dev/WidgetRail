// Functional/performance proxy: a bounded owned GPU workload competes with real
// WGC+hardware replay of an owned animated/tone window. This is NOT game FPS.
#include "ReplayWorkerTestSupport.h"
#include "CaptureBorderPixels.h"
#include <d3dcompiler.h>
#include <d3d11_4.h>
#include <algorithm>
#include <vector>
#include <chrono>
#include <cmath>
using namespace widgetrail::capture;
class GpuWorkload {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    ComPtr<ID3D11Texture2D> texture;
    ComPtr<ID3D11RenderTargetView> target;
    ComPtr<ID3D11VertexShader> vertex;
    ComPtr<ID3D11PixelShader> pixel;
    ComPtr<ID3D11Buffer> constants;
    ComPtr<ID3D11Query> begin,end,disjoint;
    ComPtr<ID3D11DeviceContext4> fencedContext;ComPtr<ID3D11Fence> fence;HANDLE completed{};uint64_t fenceValue{};
    UINT width,height;uint64_t frameIndex{};
public:
    UINT iterations{16};
    explicit GpuWorkload(UINT w,UINT h):device(CreateEncodingDevice()),width(w),height(h){
        device->GetImmediateContext(&context);
        const char* shader=R"(
cbuffer Parameters:register(b0){float Time;float Width;float Height;uint Iterations;}
float4 Vertex(uint id:SV_VertexID):SV_POSITION {return float4(id==2?3:-1,id==1?3:-1,0,1);}
float4 Pixel(float4 position:SV_POSITION):SV_TARGET {
    float2 p=position.xy/float2(Width,Height);float3 color=0;
    [loop]for(uint i=0;i<Iterations;++i){
        p=frac(sin(p*float2(12.9898,78.233)+Time*.07+i*.31)*float2(437.5854,176.319));
        color+=float3(p.x,p.y,frac(p.x+p.y));
    }
    return float4(color/Iterations,1);
})";
        ComPtr<ID3DBlob> vs,ps,errors;
        winrt::check_hresult(D3DCompile(shader,strlen(shader),nullptr,nullptr,nullptr,"Vertex","vs_5_0",D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&vs,&errors));
        winrt::check_hresult(D3DCompile(shader,strlen(shader),nullptr,nullptr,nullptr,"Pixel","ps_5_0",D3DCOMPILE_OPTIMIZATION_LEVEL3,0,&ps,&errors));
        winrt::check_hresult(device->CreateVertexShader(vs->GetBufferPointer(),vs->GetBufferSize(),nullptr,&vertex));
        winrt::check_hresult(device->CreatePixelShader(ps->GetBufferPointer(),ps->GetBufferSize(),nullptr,&pixel));
        D3D11_TEXTURE2D_DESC desc{};desc.Width=w;desc.Height=h;desc.MipLevels=1;desc.ArraySize=1;desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;
        desc.SampleDesc.Count=1;desc.BindFlags=D3D11_BIND_RENDER_TARGET;
        winrt::check_hresult(device->CreateTexture2D(&desc,nullptr,&texture));winrt::check_hresult(device->CreateRenderTargetView(texture.Get(),nullptr,&target));
        D3D11_BUFFER_DESC buffer{};buffer.ByteWidth=16;buffer.Usage=D3D11_USAGE_DEFAULT;buffer.BindFlags=D3D11_BIND_CONSTANT_BUFFER;
        winrt::check_hresult(device->CreateBuffer(&buffer,nullptr,&constants));
        const auto query=[&](D3D11_QUERY type,ComPtr<ID3D11Query>& value){D3D11_QUERY_DESC q{type,0};winrt::check_hresult(device->CreateQuery(&q,&value));};
        query(D3D11_QUERY_TIMESTAMP,begin);query(D3D11_QUERY_TIMESTAMP,end);query(D3D11_QUERY_TIMESTAMP_DISJOINT,disjoint);
        ComPtr<ID3D11Device5> fencedDevice;winrt::check_hresult(device.As(&fencedDevice));winrt::check_hresult(context.As(&fencedContext));
        winrt::check_hresult(fencedDevice->CreateFence(0,D3D11_FENCE_FLAG_NONE,IID_PPV_ARGS(&fence)));completed=CreateEventW(nullptr,FALSE,FALSE,nullptr);winrt::check_bool(completed!=nullptr);
        D3D11_VIEWPORT viewport{0,0,float(w),float(h),0,1};context->RSSetViewports(1,&viewport);context->OMSetRenderTargets(1,target.GetAddressOf(),nullptr);
        context->VSSetShader(vertex.Get(),nullptr,0);context->PSSetShader(pixel.Get(),nullptr,0);context->PSSetConstantBuffers(0,1,constants.GetAddressOf());
        context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
    }
    ~GpuWorkload(){if(completed)CloseHandle(completed);}
    std::pair<double,double> Frame(){
        const auto started=std::chrono::steady_clock::now();
        struct Params{float time,width,height;UINT loops;} values{float(frameIndex++/60.0),float(width),float(height),iterations};
        context->UpdateSubresource(constants.Get(),0,nullptr,&values,0,0);
        context->Begin(disjoint.Get());context->End(begin.Get());context->Draw(3,0);context->End(end.Get());context->End(disjoint.Get());
        winrt::check_hresult(fencedContext->Signal(fence.Get(),++fenceValue));winrt::check_hresult(fence->SetEventOnCompletion(fenceValue,completed));context->Flush();
        if(WaitForSingleObject(completed,5000)!=WAIT_OBJECT_0)throw std::runtime_error("GPU workload fence stalled");
        UINT64 first{},last{};D3D11_QUERY_DATA_TIMESTAMP_DISJOINT frequency{};
        winrt::check_hresult(context->GetData(begin.Get(),&first,sizeof(first),0));winrt::check_hresult(context->GetData(end.Get(),&last,sizeof(last),0));
        winrt::check_hresult(context->GetData(disjoint.Get(),&frequency,sizeof(frequency),0));
        if(frequency.Disjoint||!frequency.Frequency||last<first)throw std::runtime_error("GPU clock disjoint");
        return {std::chrono::duration<double,std::milli>(std::chrono::steady_clock::now()-started).count(),double(last-first)*1000/double(frequency.Frequency)};
    }
    void Calibrate(){
        double time{};for(int frame=0;frame<30;++frame){const auto value=Frame();if(frame>=10)time+=value.second;}
        iterations=std::clamp(static_cast<UINT>(std::round(double(iterations)*6.0/(time/20))),8u,2048u);
        printf("WORKLOAD iterations=%u targetGpuMs=6\n",iterations);
    }
};
struct Measurements {
    std::vector<double> wall,gpu;
    void Print(const char* phase) {
        const auto summarize=[](std::vector<double> values){std::sort(values.begin(),values.end());double sum{};for(const auto value:values)sum+=value;
            return std::array<double,4>{sum/double(values.size()),values[values.size()/2],values[static_cast<size_t>(double(values.size()-1)*.95)],values[static_cast<size_t>(double(values.size()-1)*.99)]};};
        const auto w=summarize(wall),g=summarize(gpu);
        printf("PHASE %s frames=%zu wallMeanMs=%.6f wallP50Ms=%.6f wallP95Ms=%.6f wallP99Ms=%.6f gpuMeanMs=%.6f gpuP50Ms=%.6f gpuP95Ms=%.6f gpuP99Ms=%.6f\n",
            phase,wall.size(),w[0],w[1],w[2],w[3],g[0],g[1],g[2],g[3]);fflush(stdout);
    }
};
State Observe(Worker& worker){
    Hr(WrailReplayWorkerHeartbeat(worker.value,worker.epoch));State state;Hr(WrailReplayWorkerRead(worker.value,&state,0));
    if(state.phase==Phase::Failed)throw std::runtime_error("replay failed under GPU contention");
    if(state.residentBytes>32ull*1024*1024||state.bufferedTicks>150000000)throw std::runtime_error("contention exceeded replay bounds");
    return state;
}
Measurements PhaseRun(GpuWorkload& workload,unsigned seconds,Worker* worker=nullptr){
    Measurements measured;const auto start=GetTickCount64();
    while(GetTickCount64()-start<seconds*1000){const auto frame=workload.Frame();if(worker)Observe(*worker);
        if(GetTickCount64()-start>=1500){measured.wall.push_back(frame.first);measured.gpu.push_back(frame.second);}}
    if(measured.wall.size()<20)throw std::runtime_error("insufficient GPU workload samples");return measured;
}
int wmain(int argc,wchar_t** argv){
    if(argc!=8)return 2; // worker,owned fixture,outputprefix,codec,width,height,bitrate
    try{
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        const auto width=static_cast<UINT>(_wtoi(argv[5])),height=static_cast<UINT>(_wtoi(argv[6]));
        CaptureEncodingProfile profile{static_cast<CaptureVideoCodec>(_wtoi(argv[4])),CapturePixelFormat::Nv12,CaptureRateControl::ConstantBitrate,75};
        if(!profile.Valid()||width<640||height<480||width>8192||height>8192||uint64_t(width)*height>3840u*2160u)return 2;
        FixtureProcess source(argv[2],false,width,height);GpuWorkload workload(width,height);workload.Calibrate();
        printf("SCOPE owned GPU compute/raster workload plus owned WGC+application-audio source, codec=%u canvas=%ux%u; not actual game FPS\n",static_cast<unsigned>(profile.codec),width,height);
        auto before=PhaseRun(workload,6);before.Print("off-before");
        const auto borderBefore=Yellow(source.Window());
        {
            Worker worker(argv[1],source,15,&profile,width,height,static_cast<uint32_t>(_wtoi(argv[7])));
            auto on=PhaseRun(workload,12,&worker);on.Print("on");const auto state=Observe(worker);
            printf("BORDER workerBeforeYellow=%llu workerRecordingYellow=%llu\n",borderBefore,Yellow(source.Window()));
            HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|PROCESS_VM_READ,FALSE,WrailReplayWorkerProcessId(worker.value));
            PROCESS_MEMORY_COUNTERS_EX memory{};memory.cb=sizeof(memory);if(process){GetProcessMemoryInfo(process,reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&memory),sizeof(memory));CloseHandle(process);}
            printf("REPLAY residentBytes=%llu privateBytes=%llu dropped=%llu bufferedSeconds=%.6f actual=%ux%u source=%ux%u adapter=%llx\n",state.residentBytes,
                static_cast<unsigned long long>(memory.PrivateUsage),state.droppedFrames,double(state.bufferedTicks)/10000000,state.width,state.height,state.sourceWidth,state.sourceHeight,state.adapterLuid);
            const auto output=std::wstring(argv[3])+L".mp4";uint64_t save{};const auto saveStart=GetTickCount64();Hr(WrailReplayWorkerSave(worker.value,worker.epoch,output.c_str(),5,&save));
            State saved;
            do{workload.Frame();saved=Observe(worker);if(GetTickCount64()-saveStart>10000)throw std::runtime_error("contention save deadline");}while(saved.saveId!=save||saved.saveOutcome==SaveOutcome::Pending);
            Check(saved.saveOutcome==SaveOutcome::Saved,"replay saved during continuing GPU workload");
            printf("SAVE latencyMs=%llu durationSeconds=%.6f frames=%u missed=%u bytes=%llu\n",GetTickCount64()-saveStart,double(saved.savedDurationTicks)/10000000,saved.savedFrames,saved.savedMissed,saved.savedBytes);
            Hr(WrailReplayWorkerRequestStop(worker.value,worker.epoch));const auto stopStart=GetTickCount64();
            for(;;){workload.Frame();State stopped;Hr(WrailReplayWorkerRead(worker.value,&stopped,0));if(stopped.flags&Ended)break;
                Hr(WrailReplayWorkerHeartbeat(worker.value,worker.epoch));if(GetTickCount64()-stopStart>10000)throw std::runtime_error("contention stop deadline");}
        }
        auto after=PhaseRun(workload,6);after.Print("off-after");puts("PASS bounded GPU-contention proxy with real WGC/replay/save; game-specific performance remains unmeasured");return 0;
    }catch(const winrt::hresult_error& e){printf("FAIL contention HRESULT=%08x\n",static_cast<unsigned>(e.code().value));return 1;}
    catch(const std::exception& e){printf("FAIL contention %s\n",e.what());return 1;}
}
