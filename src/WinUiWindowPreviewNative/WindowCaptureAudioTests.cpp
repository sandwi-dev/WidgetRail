#include "ProcessLoopbackCapture.h"
#include "CaptureAudioTimeline.h"
#include <cmath>
#include <cstdio>
#include <string>
#include <chrono>
#include <memory>

using namespace widgetrail::capture;
constexpr double Pi = 3.14159265358979323846;
static void Check(bool valid, const char* message) {
    if (!valid) { std::printf("FAIL: %s\n", message); throw winrt::hresult_error(E_FAIL); }
    std::printf("PASS: %s\n", message); std::fflush(stdout);
}
struct NamedEvent {
    HANDLE value;
    explicit NamedEvent(const std::wstring& name) : value(CreateEventW(nullptr, TRUE, FALSE, name.c_str())) {
        if (!value) winrt::throw_last_error();
    }
    ~NamedEvent() { CloseHandle(value); }
};
class ToneProcess {
    PROCESS_INFORMATION process{};
public:
    explicit ToneProcess(const std::wstring& arguments) {
        wchar_t executable[32768]{};
        if (!GetModuleFileNameW(nullptr, executable, 32768)) winrt::throw_last_error();
        auto command = L"\"" + std::wstring(executable) + L"\" " + arguments;
        STARTUPINFOW startup{}; startup.cb = sizeof(startup);
        winrt::check_bool(CreateProcessW(executable, command.data(), nullptr, nullptr, FALSE,
            CREATE_NO_WINDOW, nullptr, nullptr, &startup, &process));
        CloseHandle(process.hThread); process.hThread = nullptr;
    }
    ~ToneProcess() {
        if (WaitForSingleObject(process.hProcess, 2000) != WAIT_OBJECT_0) {
            // Only this fixture's own synthetic child, never a user process.
            TerminateProcess(process.hProcess, 2); WaitForSingleObject(process.hProcess, 2000);
        }
        CloseHandle(process.hProcess);
    }
    DWORD Id() const noexcept { return process.dwProcessId; }
};

static void PlayTone(double frequency, const std::wstring& stopName, const std::wstring& readyName, bool pulse = false) {
    NamedEvent stop(stopName), ready(readyName);
    ComPtr<IMMDeviceEnumerator> devices;
    winrt::check_hresult(CoCreateInstance(__uuidof(MMDeviceEnumerator), nullptr, CLSCTX_ALL, IID_PPV_ARGS(&devices)));
    ComPtr<IMMDevice> endpoint;
    winrt::check_hresult(devices->GetDefaultAudioEndpoint(eRender, eConsole, &endpoint));
    ComPtr<IAudioClient> client;
    winrt::check_hresult(endpoint->Activate(__uuidof(IAudioClient), CLSCTX_ALL, nullptr, &client));
    auto format = ProcessLoopbackCapture::Format();
    winrt::check_hresult(client->Initialize(AUDCLNT_SHAREMODE_SHARED,
        AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,
        0, 0, &format, nullptr));
    CaptureEvent available;
    winrt::check_hresult(client->SetEventHandle(available.Get()));
    UINT32 capacity{}; winrt::check_hresult(client->GetBufferSize(&capacity));
    ComPtr<IAudioRenderClient> renderer; winrt::check_hresult(client->GetService(IID_PPV_ARGS(&renderer)));
    uint64_t sampleIndex = 0;
    REFERENCE_TIME latency{}; winrt::check_hresult(client->GetStreamLatency(&latency));
    const auto fill = [&] {
        UINT32 padding{}; winrt::check_hresult(client->GetCurrentPadding(&padding));
        const auto frames = capacity - padding; if (!frames) return;
        BYTE* bytes{}; winrt::check_hresult(renderer->GetBuffer(frames, &bytes));
        auto samples = reinterpret_cast<int16_t*>(bytes);
        const auto presentation = CaptureQpc100ns() + latency + 10000000ULL * padding / ProcessLoopbackCapture::Rate;
        for (UINT32 i = 0; i < frames; ++i, ++sampleIndex) {
            const auto audible = !pulse || ((presentation + 10000000ULL * i / ProcessLoopbackCapture::Rate) / 5000000) % 2 == 0;
            const auto value = static_cast<int16_t>((audible ? 819 : 0) * std::sin(2 * Pi * frequency * double(sampleIndex) / ProcessLoopbackCapture::Rate));
            samples[2 * i] = samples[2 * i + 1] = value;
        }
        winrt::check_hresult(renderer->ReleaseBuffer(frames, 0));
    };
    fill(); winrt::check_hresult(client->Start()); SetEvent(ready.value);
    const HANDLE events[] = {stop.value, available.Get()};
    const auto deadline = GetTickCount64() + 60000;
    while (GetTickCount64() < deadline) {
        const auto wait = WaitForMultipleObjects(2, events, FALSE, 1000);
        if (wait == WAIT_OBJECT_0) break;
        if (wait == WAIT_OBJECT_0 + 1) fill();
        else if (wait != WAIT_TIMEOUT) winrt::throw_last_error();
    }
    client->Stop();
}

static double Amplitude(const std::vector<int16_t>& pcm, double frequency) {
    // One-channel coherent frequency measurement, independent of starting phase.
    double real = 0, imaginary = 0;
    const auto count = pcm.size() / 2;
    for (size_t i = 0; i < count; ++i) {
        const auto phase = 2 * Pi * frequency * double(i) / ProcessLoopbackCapture::Rate;
        real += pcm[i * 2] * std::cos(phase); imaginary += pcm[i * 2] * std::sin(phase);
    }
    return count ? 2 * std::hypot(real, imaginary) / double(count) : 0;
}
static double CpuMilliseconds() {
    FILETIME created{}, exited{}, kernel{}, user{};
    winrt::check_bool(GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user));
    return double((uint64_t(kernel.dwHighDateTime) << 32 | kernel.dwLowDateTime) +
        (uint64_t(user.dwHighDateTime) << 32 | user.dwLowDateTime)) / 10000;
}
static std::vector<int16_t> Collect(ProcessLoopbackCapture& capture, int milliseconds) {
    std::vector<int16_t> samples;
    const auto deadline = GetTickCount64() + milliseconds;
    uint64_t previous = 0;
    while (GetTickCount64() < deadline) {
        Sleep(15);
        for (auto& packet : capture.Drain()) {
            if (packet.qpc100ns < previous) throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_INVALID_TIME));
            previous = packet.qpc100ns;
            samples.insert(samples.end(), packet.samples.begin(), packet.samples.end());
        }
    }
    return samples;
}
static void VerifyMux(DWORD target, const std::wstring& path) {
    WrailCaptureOptions options;
    options.kind = 2; options.maximumWidth = 320; options.maximumHeight = 180;
    options.framesPerSecond = 15; options.bitrate = 1000000; options.durationSeconds = 2;
    {
        VideoEncoder encoder(path.c_str(), 320, 180, options, nullptr, true);
        ProcessLoopbackCapture capture(target);
        const auto epoch = CaptureQpc100ns();
        CaptureAudioTimeline timeline(encoder, epoch, 2 * VideoEncoder::AudioRate);
        const auto start = std::chrono::steady_clock::now();
        std::vector<BYTE> pixels(320 * 180 * 4, 0);
        for (size_t i = 0; i < pixels.size(); i += 4) { pixels[i + 2] = 220; pixels[i + 3] = 255; }
        for (UINT frame = 0; frame < 30; ++frame) {
            std::this_thread::sleep_until(start + std::chrono::microseconds(1000000LL * frame / 15));
            encoder.Write(pixels, frame);
            for (auto& packet : capture.Drain()) timeline.Append(packet);
        }
        std::this_thread::sleep_until(start + std::chrono::seconds(2));
        capture.Stop();
        for (auto& packet : capture.Drain()) timeline.Append(packet);
        timeline.Finish(2 * VideoEncoder::AudioRate);
        Check(timeline.WrittenFrames() == 96000, "QPC-aligned PCM timeline has exactly two seconds before AAC encoding");
        encoder.Finish();
    }
    winrt::check_hresult(MFStartup(MF_VERSION));
    struct ShutdownMf { ~ShutdownMf() { MFShutdown(); } } shutdown;
    ComPtr<IMFSourceReader> reader;
    winrt::check_hresult(MFCreateSourceReaderFromURL(path.c_str(), nullptr, &reader));
    ComPtr<IMFMediaType> native;
    winrt::check_hresult(reader->GetNativeMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, &native));
    GUID subtype{}; winrt::check_hresult(native->GetGUID(MF_MT_SUBTYPE, &subtype));
    Check(subtype == MFAudioFormat_AAC, "MP4 contains an AAC audio track");
    ComPtr<IMFMediaType> format; winrt::check_hresult(MFCreateMediaType(&format));
    winrt::check_hresult(format->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio));
    winrt::check_hresult(format->SetGUID(MF_MT_SUBTYPE, MFAudioFormat_PCM));
    winrt::check_hresult(format->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, 2));
    winrt::check_hresult(format->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, 48000));
    winrt::check_hresult(format->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16));
    winrt::check_hresult(reader->SetCurrentMediaType(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), nullptr, format.Get()));
    std::vector<int16_t> pcm;
    LONGLONG previous = -1, audioEnd = 0;
    while (true) {
        DWORD flags{}; LONGLONG timestamp{}; ComPtr<IMFSample> sample;
        winrt::check_hresult(reader->ReadSample(static_cast<DWORD>(MF_SOURCE_READER_FIRST_AUDIO_STREAM), 0, nullptr, &flags, &timestamp, &sample));
        if (flags & MF_SOURCE_READERF_ENDOFSTREAM) break;
        if (!sample) continue;
        if (timestamp < previous) throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_INVALID_TIME));
        previous = timestamp; LONGLONG duration{}; winrt::check_hresult(sample->GetSampleDuration(&duration)); audioEnd = timestamp + duration;
        ComPtr<IMFMediaBuffer> buffer; winrt::check_hresult(sample->ConvertToContiguousBuffer(&buffer));
        BYTE* data{}; DWORD bytes{}; winrt::check_hresult(buffer->Lock(&data, nullptr, &bytes));
        const auto values = reinterpret_cast<int16_t*>(data); pcm.insert(pcm.end(), values, values + bytes / 2);
        buffer->Unlock();
    }
    const auto root = Amplitude(pcm, 440), child = Amplitude(pcm, 660), other = Amplitude(pcm, 990);
    std::printf("AAC decodedFrames=%zu end100ns=%lld root440=%.2f child660=%.2f excluded990=%.2f\n", pcm.size() / 2, audioEnd, root, child, other);
    Check(pcm.size() / 2 >= 94000 && pcm.size() / 2 <= 100000 && std::abs(audioEnd - 20000000LL) <= 500000,
        "AAC round trip stays within 50 ms of the video duration with ordered timestamps");
    Check(root > 50 && child > 50 && other < std::min(root, child) * .03, "AAC round trip preserves process-tree audio isolation");
}
#include "WindowCaptureAudioWgcTests.h"

int wmain(int argc, wchar_t** argv) {
    try {
        winrt::init_apartment(winrt::apartment_type::multi_threaded);
        if (argc == 5 && std::wstring(argv[1]) == L"--tone") {
            PlayTone(std::wcstod(argv[2], nullptr), argv[3], argv[4]); return 0;
        }
        if (argc == 4 && std::wstring(argv[1]) == L"--silent") {
            NamedEvent stop(argv[2]), ready(argv[3]);
            SetEvent(ready.value); WaitForSingleObject(stop.value, 20000); return 0;
        }
        if (argc == 4 && (std::wstring(argv[1]) == L"--tone-tree" || std::wstring(argv[1]) == L"--pulse-tree")) {
            const auto childReady = std::wstring(argv[3]) + L"-child";
            NamedEvent ready(childReady);
            ToneProcess child(L"--tone 660 " + std::wstring(argv[2]) + L" " + childReady);
            if (WaitForSingleObject(ready.value, 5000) != WAIT_OBJECT_0) throw winrt::hresult_error(HRESULT_FROM_WIN32(WAIT_TIMEOUT));
            std::unique_ptr<PulseWindow> window;
            const auto pulse = std::wstring(argv[1]) == L"--pulse-tree";
            if (pulse) window = std::make_unique<PulseWindow>();
            PlayTone(440, argv[2], argv[3], pulse); return 0;
        }
        if (argc == 3 && std::wstring(argv[1]) == L"--wgc") return RunAudioWgc(argv[2]);
        if (argc != 2) return 2;
        const auto prefix = L"Local\\WidgetRail.AudioProbe." + std::to_wstring(GetCurrentProcessId()) + L"." + std::to_wstring(GetTickCount64());
        const auto stopName = prefix + L".stop", targetReadyName = prefix + L".target", otherReadyName = prefix + L".other";
        NamedEvent stop(stopName), targetReady(targetReadyName), otherReady(otherReadyName);
        auto target = std::make_unique<ToneProcess>(L"--tone-tree " + stopName + L" " + targetReadyName);
        auto other = std::make_unique<ToneProcess>(L"--tone 990 " + stopName + L" " + otherReadyName);
        struct StopTones { HANDLE event; ~StopTones() { SetEvent(event); } } cleanup{stop.value};
        Check(WaitForSingleObject(targetReady.value, 5000) == WAIT_OBJECT_0 &&
              WaitForSingleObject(otherReady.value, 5000) == WAIT_OBJECT_0, "Synthetic target, child and competing tone are playing");
        for (int run = 0; run < 2; ++run) {
            const auto cpu = CpuMilliseconds();
            const auto started = GetTickCount64();
            ProcessLoopbackCapture capture(target->Id());
            auto samples = Collect(capture, 2000);
            capture.Stop();
            winrt::check_hresult(capture.Status());
            const auto root = Amplitude(samples, 440), child = Amplitude(samples, 660), excluded = Amplitude(samples, 990);
            std::printf("run=%d frames=%zu root440=%.2f child660=%.2f excluded990=%.2f peakQueueBytes=%zu elapsedMs=%llu cpuMs=%.1f\n",
                run, samples.size() / 2, root, child, excluded, capture.PeakBytes(), GetTickCount64() - started, CpuMilliseconds() - cpu);
            Check(samples.size() / 2 >= 48000, "At least one second captured with monotonic QPC timestamps");
            Check(root > 50 && child > 50 && excluded < std::min(root, child) * .03, "Process tree included; competing process tone excluded");
            Check(capture.PeakBytes() <= ProcessLoopbackCapture::MaximumQueuedSamples * 2, "PCM queue stays within its one-second bound");
        }
        VerifyMux(target->Id(), std::wstring(argv[1]) + L"/application-audio.mp4");
        {
            ProcessLoopbackCapture capture(target->Id()); Sleep(1500);
            Check(capture.Status() == HRESULT_FROM_WIN32(ERROR_BUFFER_OVERFLOW), "Stalled consumer fails explicitly instead of growing or dropping audio");
        }
        {
            const auto silentReadyName = prefix + L".silent";
            NamedEvent silentReady(silentReadyName);
            auto silentProcess = std::make_unique<ToneProcess>(L"--silent " + stopName + L" " + silentReadyName);
            Check(WaitForSingleObject(silentReady.value, 5000) == WAIT_OBJECT_0, "Independent silent target is ready");
            ProcessLoopbackCapture silent(silentProcess->Id());
            auto samples = Collect(silent, 600); silent.Stop(); winrt::check_hresult(silent.Status());
            int peak = 0;
            for (auto value : samples) peak = std::max(peak, std::abs(int(value)));
            std::printf("silent frames=%zu peak16bit=%d root440=%.3f child660=%.3f excluded990=%.3f\n",
                samples.size() / 2, peak, Amplitude(samples, 440), Amplitude(samples, 660), Amplitude(samples, 990));
            // Float-to-PCM conversion may dither silence by one least-significant
            // bit. Keep the bound explicit; the competing tones are ~800 LSB.
            Check(peak <= 2, "Silent target has at most two LSB conversion noise and excludes competing audio");
            SetEvent(stop.value);
        }
        SetEvent(stop.value); target.reset(); other.reset();
        std::printf("PASS: process-loopback and synthetic AAC mux qualification; real WGC synchronization remains pending\n");
        return 0;
    } catch (const winrt::hresult_error& error) {
        std::printf("FAILED hr=%08X\n", static_cast<unsigned>(error.code().value)); return 1;
    } catch (...) { std::printf("FAILED unexpected exception\n"); return 1; }
}
