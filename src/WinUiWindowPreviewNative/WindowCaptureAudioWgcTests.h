#pragma once
// Standalone synthetic window, owned tones and decoded media checks. Never
// inspect or record a user application to qualify the audio path.
class PulseWindow {
    std::thread worker;
    HWND window{};
    static LRESULT CALLBACK Procedure(HWND hwnd, UINT message, WPARAM w, LPARAM l) {
        if (message == WM_TIMER) { InvalidateRect(hwnd, nullptr, FALSE); return 0; }
        if (message == WM_PAINT) {
            PAINTSTRUCT paint{}; const auto dc = BeginPaint(hwnd, &paint);
            RECT bounds{}; GetClientRect(hwnd, &bounds);
            FillRect(dc, &bounds, static_cast<HBRUSH>(GetStockObject(CaptureQpc100ns() / 5000000 % 2 == 0 ? WHITE_BRUSH : BLACK_BRUSH)));
            EndPaint(hwnd, &paint); return 0;
        }
        if (message == WM_DESTROY) { PostQuitMessage(0); return 0; }
        return DefWindowProcW(hwnd, message, w, l);
    }
public:
    PulseWindow() {
        std::promise<HWND> ready; auto result = ready.get_future();
        worker = std::thread([promise = std::move(ready)]() mutable {
            SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            const auto instance = GetModuleHandleW(nullptr);
            WNDCLASSW type{}; type.hInstance = instance; type.lpfnWndProc = Procedure;
            type.lpszClassName = L"WidgetRailAudioCaptureProbe"; RegisterClassW(&type);
            const auto hwnd = CreateWindowExW(WS_EX_TOPMOST, type.lpszClassName, L"WidgetRail synthetic audiovisual capture",
                WS_POPUP | WS_VISIBLE, 500, 100, 3840, 2160, nullptr, nullptr, instance, nullptr);
            promise.set_value(hwnd); if (!hwnd) return;
            SetTimer(hwnd, 1, 10, nullptr); ShowWindow(hwnd, SW_SHOW); UpdateWindow(hwnd); SetForegroundWindow(hwnd);
            MSG message{};
            while (GetMessageW(&message, nullptr, 0, 0) > 0) { TranslateMessage(&message); DispatchMessageW(&message); }
        });
        window = result.get();
        if (!window) { worker.join(); throw winrt::hresult_error(E_FAIL); }
    }
    ~PulseWindow() { if (IsWindow(window)) PostMessageW(window, WM_CLOSE, 0, 0); if (worker.joinable()) worker.join(); }
};
struct AudioVideoDecode {
    std::vector<int16_t> pcm;
    std::vector<std::pair<double, bool>> videoEdges;
    double audioEnd{}, videoEnd{};
    UINT frames{};
};
static AudioVideoDecode DecodeAudioVideo(const std::wstring& path) {
    winrt::check_hresult(MFStartup(MF_VERSION));
    struct Shutdown { ~Shutdown() { MFShutdown(); } } shutdown;
    AudioVideoDecode result;
    for (const auto video : {false, true}) {
        ComPtr<IMFAttributes> attributes; winrt::check_hresult(MFCreateAttributes(&attributes, 1));
        winrt::check_hresult(attributes->SetUINT32(MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, TRUE));
        ComPtr<IMFSourceReader> reader; winrt::check_hresult(MFCreateSourceReaderFromURL(path.c_str(), attributes.Get(), &reader));
        const auto stream = static_cast<DWORD>(video ? MF_SOURCE_READER_FIRST_VIDEO_STREAM : MF_SOURCE_READER_FIRST_AUDIO_STREAM);
        winrt::check_hresult(reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_ALL_STREAMS), FALSE));
        winrt::check_hresult(reader->SetStreamSelection(stream, TRUE));
        ComPtr<IMFMediaType> format; winrt::check_hresult(MFCreateMediaType(&format));
        winrt::check_hresult(format->SetGUID(MF_MT_MAJOR_TYPE, video ? MFMediaType_Video : MFMediaType_Audio));
        winrt::check_hresult(format->SetGUID(MF_MT_SUBTYPE, video ? MFVideoFormat_RGB32 : MFAudioFormat_PCM));
        if (!video) {
            winrt::check_hresult(format->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, 2));
            winrt::check_hresult(format->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, 48000));
            winrt::check_hresult(format->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16));
        }
        winrt::check_hresult(reader->SetCurrentMediaType(stream, nullptr, format.Get()));
        LONGLONG previous = -1; bool light = false;
        while (true) {
            DWORD flags{}; LONGLONG time{}; ComPtr<IMFSample> sample;
            winrt::check_hresult(reader->ReadSample(stream, 0, nullptr, &flags, &time, &sample));
            if (flags & MF_SOURCE_READERF_ENDOFSTREAM) break;
            if (!sample) continue;
            if (time < previous) throw winrt::hresult_error(HRESULT_FROM_WIN32(ERROR_INVALID_TIME));
            previous = time; LONGLONG duration{}; winrt::check_hresult(sample->GetSampleDuration(&duration));
            ComPtr<IMFMediaBuffer> buffer; winrt::check_hresult(sample->ConvertToContiguousBuffer(&buffer));
            BYTE* bytes{}; DWORD length{}; winrt::check_hresult(buffer->Lock(&bytes, nullptr, &length));
            if (video) {
                const auto value = length >= 4 && bytes[(length / 8) * 4 + 2] > 128;
                if (result.frames > 0 && value != light) result.videoEdges.emplace_back(double(time) / 10000000, value);
                light = value; ++result.frames; result.videoEnd = double(time + duration) / 10000000;
            } else {
                const auto values = reinterpret_cast<int16_t*>(bytes);
                result.pcm.insert(result.pcm.end(), values, values + length / 2);
                result.audioEnd = double(time + duration) / 10000000;
            }
            buffer->Unlock();
        }
    }
    return result;
}
struct CaptureAuthority {
    ULONGLONG started = GetTickCount64();
    int afterMs{}, decision = 1;
    HWND closeWindow{};
    static int32_t __stdcall Read(void* context) {
        auto& value = *static_cast<CaptureAuthority*>(context);
        if (value.afterMs && GetTickCount64() - value.started >= static_cast<ULONGLONG>(value.afterMs)) {
            if (value.closeWindow) { PostMessageW(value.closeWindow, WM_CLOSE, 0, 0); value.closeWindow = nullptr; }
            return value.decision;
        }
        return 1;
    }
};
static int RunAudioWgc(const std::wstring& directory) {
    const auto prefix = L"Local\\WidgetRail.AudioWgc." + std::to_wstring(GetCurrentProcessId()) + L"." + std::to_wstring(GetTickCount64());
    const auto stopName = prefix + L".stop", readyName = prefix + L".ready", otherName = prefix + L".other";
    NamedEvent stop(stopName), ready(readyName), otherReady(otherName);
    auto target = std::make_unique<ToneProcess>(L"--pulse-tree " + stopName + L" " + readyName);
    auto competitor = std::make_unique<ToneProcess>(L"--tone 990 " + stopName + L" " + otherName);
    struct Cleanup { HANDLE event; ~Cleanup() { SetEvent(event); } } cleanup{stop.value};
    Check(WaitForSingleObject(ready.value, 5000) == WAIT_OBJECT_0 && WaitForSingleObject(otherReady.value, 5000) == WAIT_OBJECT_0,
        "Synthetic audiovisual target and competing tone are ready");
    struct Find { DWORD pid; HWND window{}; } found{target->Id()};
    EnumWindows([](HWND hwnd, LPARAM pointer) -> BOOL {
        auto& value = *reinterpret_cast<Find*>(pointer); DWORD pid{}; GetWindowThreadProcessId(hwnd, &pid);
        wchar_t name[80]{}; GetClassNameW(hwnd, name, 80);
        if (pid == value.pid && std::wstring(name) == L"WidgetRailAudioCaptureProbe") { value.window = hwnd; return FALSE; }
        return TRUE;
    }, reinterpret_cast<LPARAM>(&found));
    Check(found.window != nullptr, "Capture target is the owned synthetic window");
    WrailPreviewTarget identity;
    winrt::check_hresult(WrailPreviewReadIdentity(reinterpret_cast<uint64_t>(found.window), &identity));
    WrailCaptureEncodingProbe audioProbe;
    winrt::check_hresult(WrailProbeCaptureAudio(&audioProbe));
    Check(audioProbe.supported == 1, "Runtime process-audio initialization probe succeeds");
    struct Profile { UINT width, height, fps, bitrate, encoder; };
    const Profile profiles[] = {{1280,720,15,3000000,1},{1280,720,15,3000000,2},
        {1920,1080,30,8000000,2},{1920,1080,60,12000000,2},{3840,2160,30,20000000,2}};
    for (const auto& profile : profiles) {
        WrailCaptureOptions options;
        options.kind = 2; options.audioMode = 1; options.durationSeconds = 2;
        options.maximumWidth = profile.width; options.maximumHeight = profile.height;
        options.framesPerSecond = profile.fps; options.bitrate = profile.bitrate; options.encoder = profile.encoder;
        WrailCaptureEncodingProbe probe;
        winrt::check_hresult(WrailProbeCaptureEncoding(&options, &probe));
        Check(probe.supported == 1, "Qualified video profile initializes with AAC");
        SetForegroundWindow(found.window); Sleep(80);
        Check(GetForegroundWindow() == found.window, "Synthetic target is foreground at capture admission");
        WrailCaptureResult output; CaptureAuthority authority;
        const auto path = directory + L"/wgc-audio-" + std::to_wstring(profile.height) + L"-" + std::to_wstring(profile.fps) + L"-" + std::to_wstring(profile.encoder) + L".mp4";
        const auto cpu = CpuMilliseconds(); const auto start = GetTickCount64();
        winrt::check_hresult(WrailCaptureWindowConfigured(&identity, path.c_str(), &options, CaptureAuthority::Read, &authority, &output));
        std::printf("WGC audio profile=%ux%u/%u encoder=%u actual=%ux%u frames=%u missed=%u elapsedMs=%llu CPUms=%.1f\n",
            profile.width,profile.height,profile.fps,profile.encoder,output.width,output.height,output.frames,output.missedFrames,
            GetTickCount64()-start,CpuMilliseconds()-cpu);
        const auto decoded = DecodeAudioVideo(path);
        Check(output.audioMode == 1 && output.encoder == profile.encoder && decoded.frames == output.frames,
            "Native metadata agrees with decoded audio/video output");
        Check(output.width == profile.width && output.height == profile.height,
            "The full advertised output resolution was exercised");
        Check(std::abs(decoded.audioEnd - decoded.videoEnd) <= .05 && std::abs(decoded.videoEnd - output.durationSeconds) < .001,
            "Audio and video durations agree within AAC padding");
        const auto included = Amplitude(decoded.pcm, 660), excluded = Amplitude(decoded.pcm, 990);
        Check(included > 50 && excluded < included * .03, "WGC recording includes the child and excludes competing process audio");
        std::vector<std::pair<double, bool>> edges;
        bool audible = false;
        for (size_t offset = 0; offset + 4800 <= decoded.pcm.size(); offset += 960) {
            std::vector<int16_t> slice(decoded.pcm.begin() + offset, decoded.pcm.begin() + offset + 4800);
            const bool value = Amplitude(slice, 440) > 300;
            if (offset && value != audible) edges.emplace_back(double(offset / 2 + 1200) / 48000, value);
            audible = value;
        }
        double worst = 0; size_t matched = 0;
        for (const auto& visual : decoded.videoEdges) {
            if (visual.first < .15 || visual.first > 1.85) continue;
            double nearest = 10;
            for (const auto& sound : edges) if (sound.second == visual.second) nearest = std::min(nearest, std::abs(sound.first - visual.first));
            std::printf("Sync edge video=%.4f state=%d nearestAudioDeltaMs=%.1f\n",visual.first,visual.second,nearest*1000);
            worst = std::max(worst, nearest); ++matched;
        }
        std::printf("AV sync transitions=%zu worstMs=%.1f videoEnd=%.4f audioEnd=%.4f\n",matched,worst*1000,decoded.videoEnd,decoded.audioEnd);
        Check(matched >= 2 && worst <= 1.0 / profile.fps + .05, "Decoded visual/audio transitions align within one video frame plus 50 ms");
    }
    WrailCaptureOptions options; options.kind = 2; options.audioMode = 1; options.durationSeconds = 5;
    options.maximumWidth = 1280; options.maximumHeight = 720; options.encoder = 2; options.framesPerSecond = 30;
    {
        auto silent = options; silent.audioMode = 0; silent.maximumWidth = 3840; silent.maximumHeight = 2160;
        silent.framesPerSecond = 30; silent.bitrate = 20000000; silent.durationSeconds = 2;
        SetForegroundWindow(found.window); Sleep(50);
        WrailCaptureResult baseline; CaptureAuthority continuing;
        const auto cpu = CpuMilliseconds(); const auto start = GetTickCount64();
        const auto path = directory + L"/wgc-silent-2160-30.mp4";
        winrt::check_hresult(WrailCaptureWindowConfigured(&identity,path.c_str(),&silent,CaptureAuthority::Read,&continuing,&baseline));
        Check(baseline.audioMode == 0 && baseline.frames > 0, "Silent recording remains available with no audio track requested");
        std::printf("WGC silent comparison 3840x2160/30 frames=%u missed=%u elapsedMs=%llu CPUms=%.1f\n",
            baseline.frames,baseline.missedFrames,GetTickCount64()-start,CpuMilliseconds()-cpu);
    }
    for (const auto decision : {2, 0, 1}) {
        SetForegroundWindow(found.window); Sleep(50);
        CaptureAuthority authority; authority.afterMs = 1200; authority.decision = decision;
        if (decision == 1) authority.closeWindow = found.window;
        WrailCaptureResult output;
        const auto path = directory + L"/wgc-audio-stop-" + std::to_wstring(decision) + L".mp4";
        const auto status = WrailCaptureWindowConfigured(&identity, path.c_str(), &options, CaptureAuthority::Read, &authority, &output);
        if (decision == 2) {
            winrt::check_hresult(status); const auto decoded = DecodeAudioVideo(path);
            Check(output.durationSeconds > 0 && output.durationSeconds < 2 && std::abs(decoded.audioEnd - decoded.videoEnd) < .05,
                "Early stop keeps a playable recording with no long audio tail");
        } else Check(status == E_ABORT, decision == 0 ? "Cancellation aborts audiovisual capture" : "Target exit aborts audiovisual capture");
        if (decision != 1) {
            options.durationSeconds = 1; WrailCaptureResult retry; CaptureAuthority continuing;
            const auto reuse = directory + L"/wgc-audio-reuse-" + std::to_wstring(decision) + L".mp4";
            winrt::check_hresult(WrailCaptureWindowConfigured(&identity, reuse.c_str(), &options, CaptureAuthority::Read, &continuing, &retry));
            Check(retry.audioMode == 1 && retry.frames > 0, "Capture is reusable after stop/cancel");
            options.durationSeconds = 5;
        }
    }
    SetEvent(stop.value); target.reset(); competitor.reset();
    return 0;
}
