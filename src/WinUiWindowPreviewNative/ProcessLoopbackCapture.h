#pragma once
#include <Windows.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <mmdeviceapi.h>
#include <wrl.h>
#include <winrt/base.h>
#include <atomic>
#include <algorithm>
#include <deque>
#include <future>
#include <mutex>
#include <thread>
#include <vector>

namespace widgetrail::capture {
using Microsoft::WRL::ComPtr;

class CaptureEvent {
    HANDLE handle;
public:
    explicit CaptureEvent(bool manual = false) : handle(CreateEventW(nullptr, manual, FALSE, nullptr)) {
        if (!handle) winrt::throw_last_error();
    }
    ~CaptureEvent() { CloseHandle(handle); }
    CaptureEvent(const CaptureEvent&) = delete;
    CaptureEvent& operator=(const CaptureEvent&) = delete;
    HANDLE Get() const noexcept { return handle; }
    void Signal() const noexcept { SetEvent(handle); }
};

// The activation callback owns its event/client independently of the caller.
// A timed-out activation can complete safely after its waiting thread exits.
class ProcessAudioActivation final : public Microsoft::WRL::RuntimeClass<
    Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
    IActivateAudioInterfaceCompletionHandler, Microsoft::WRL::FtmBase> {
public:
    CaptureEvent done;
    ComPtr<IAudioClient> client;
    HRESULT result = E_PENDING;
    STDMETHOD(ActivateCompleted)(IActivateAudioInterfaceAsyncOperation* operation) override {
        ComPtr<IUnknown> activated;
        HRESULT activation = E_FAIL;
        result = operation->GetActivateResult(&activation, &activated);
        if (SUCCEEDED(result)) result = activation;
        if (SUCCEEDED(result)) result = activated.As(&client);
        done.Signal();
        return S_OK;
    }
};

struct CapturedAudioPacket {
    uint64_t qpc100ns{};
    std::vector<int16_t> samples;
};

// Fixed PCM is deliberately small: 48 kHz stereo, 16 bit; at most one second
// (192,000 bytes) queued. This captures only the chosen process tree. It never
// falls back to endpoint/system loopback when process capture is unavailable.
class ProcessLoopbackCapture {
    CaptureEvent stop{true};
    std::mutex gate;
    std::deque<CapturedAudioPacket> packets;
    size_t queuedSamples{};
    std::atomic<HRESULT> failure{S_OK};
    std::atomic<size_t> peakSamples{};
    std::thread worker;

    void Run(DWORD processId, std::promise<void> ready) noexcept {
        bool initialized = false, announced = false, started = false;
        ComPtr<IAudioClient> client;
        try {
            winrt::init_apartment(winrt::apartment_type::multi_threaded); initialized = true;
            AUDIOCLIENT_ACTIVATION_PARAMS activation{};
            activation.ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
            activation.ProcessLoopbackParams.TargetProcessId = processId;
            activation.ProcessLoopbackParams.ProcessLoopbackMode = PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
            PROPVARIANT parameters{};
            parameters.vt = VT_BLOB;
            parameters.blob.cbSize = sizeof(activation);
            parameters.blob.pBlobData = reinterpret_cast<BYTE*>(&activation);
            auto completion = Microsoft::WRL::Make<ProcessAudioActivation>();
            if (!completion) throw winrt::hresult_error(E_OUTOFMEMORY);
            ComPtr<IActivateAudioInterfaceAsyncOperation> operation;
            winrt::check_hresult(ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK,
                __uuidof(IAudioClient), &parameters, completion.Get(), &operation));
            if (WaitForSingleObject(completion->done.Get(), 5000) != WAIT_OBJECT_0)
                throw winrt::hresult_error(HRESULT_FROM_WIN32(WAIT_TIMEOUT));
            winrt::check_hresult(completion->result); client = completion->client;
            auto format = Format();
            winrt::check_hresult(client->Initialize(AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,
                0, 0, &format, nullptr));
            CaptureEvent available;
            winrt::check_hresult(client->SetEventHandle(available.Get()));
            ComPtr<IAudioCaptureClient> capture;
            winrt::check_hresult(client->GetService(IID_PPV_ARGS(&capture)));
            winrt::check_hresult(client->Start()); started = true;
            ready.set_value(); announced = true;
            const HANDLE events[] = { stop.Get(), available.Get() };
            bool first = true;
            while (true) {
                const auto wait = WaitForMultipleObjects(2, events, FALSE, 2000);
                if (wait == WAIT_OBJECT_0) break;
                if (wait == WAIT_TIMEOUT) continue; // Silent processes need not emit packets.
                if (wait != WAIT_OBJECT_0 + 1) winrt::throw_last_error();
                UINT32 frames{};
                winrt::check_hresult(capture->GetNextPacketSize(&frames));
                while (frames) {
                    BYTE* bytes{}; DWORD flags{}; UINT64 position{}, qpc{};
                    winrt::check_hresult(capture->GetBuffer(&bytes, &frames, &flags, &position, &qpc));
                    if (!frames) break;
                    struct Release { IAudioCaptureClient* client; UINT32 frames; ~Release() { if (frames) client->ReleaseBuffer(frames); } } release{capture.Get(), frames};
                    if ((flags & AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR) || (!first && (flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY)))
                        throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_INVALID_DATA));
                    first = false;
                    const auto count = size_t(frames) * Channels;
                    if (count > MaximumQueuedSamples) throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_BUFFER_OVERFLOW));
                    CapturedAudioPacket packet{qpc, std::vector<int16_t>(count)};
                    if (!(flags & AUDCLNT_BUFFERFLAGS_SILENT)) memcpy(packet.samples.data(), bytes, count * sizeof(int16_t));
                    {
                        std::lock_guard lock(gate);
                        if (queuedSamples + count > MaximumQueuedSamples)
                            throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_BUFFER_OVERFLOW));
                        queuedSamples += count;
                        peakSamples.store(std::max(peakSamples.load(), queuedSamples));
                        packets.push_back(std::move(packet));
                    }
                    // Release the borrowed WASAPI packet before asking for the next.
                    const auto released = capture->ReleaseBuffer(frames); release.frames = 0;
                    winrt::check_hresult(released);
                    winrt::check_hresult(capture->GetNextPacketSize(&frames));
                }
            }
        } catch (...) {
            failure.store(winrt::to_hresult());
            if (!announced) ready.set_exception(std::current_exception());
        }
        if (started) client->Stop();
        client.Reset();
        if (initialized) winrt::uninit_apartment();
    }
public:
    static constexpr UINT32 Rate = 48000, Channels = 2;
    static constexpr size_t MaximumQueuedSamples = Rate * Channels;
    static WAVEFORMATEX Format() noexcept {
        WAVEFORMATEX format{};
        format.wFormatTag = WAVE_FORMAT_PCM; format.nChannels = Channels; format.nSamplesPerSec = Rate;
        format.wBitsPerSample = 16; format.nBlockAlign = Channels * 2; format.nAvgBytesPerSec = Rate * format.nBlockAlign;
        return format;
    }
    explicit ProcessLoopbackCapture(DWORD processId) {
        if (!processId) throw winrt::hresult_error(E_INVALIDARG);
        std::promise<void> ready; auto initialized = ready.get_future();
        worker = std::thread([this, processId, promise = std::move(ready)]() mutable { Run(processId, std::move(promise)); });
        try { initialized.get(); }
        catch (...) { Stop(); throw; }
    }
    ~ProcessLoopbackCapture() { Stop(); }
    ProcessLoopbackCapture(const ProcessLoopbackCapture&) = delete;
    ProcessLoopbackCapture& operator=(const ProcessLoopbackCapture&) = delete;
    void Stop() noexcept { stop.Signal(); if (worker.joinable()) worker.join(); }
    HRESULT Status() const noexcept { return failure.load(); }
    size_t PeakBytes() const noexcept { return peakSamples.load() * sizeof(int16_t); }
    std::deque<CapturedAudioPacket> Drain() {
        winrt::check_hresult(failure.load());
        std::lock_guard lock(gate);
        auto result = std::move(packets); packets.clear(); queuedSamples = 0;
        return result;
    }
};
}
