#pragma once
#include "CaptureVideoEncoder.h"
#include "ProcessLoopbackCapture.h"
#include <cmath>

namespace widgetrail::capture {
// All timestamps use WASAPI's QPC-derived 100ns clock; wall-clock/date changes
// cannot affect synchronization. At most one second of PCM is handled at once.
inline uint64_t CaptureQpc100ns() {
    LARGE_INTEGER counter{}, frequency{};
    winrt::check_bool(QueryPerformanceCounter(&counter));
    winrt::check_bool(QueryPerformanceFrequency(&frequency));
    return uint64_t(counter.QuadPart / frequency.QuadPart) * 10000000 +
        uint64_t(counter.QuadPart % frequency.QuadPart) * 10000000 / uint64_t(frequency.QuadPart);
}
class CaptureAudioTimeline {
    VideoEncoder& encoder;
    uint64_t origin, maximumFrames, written{};
    std::array<int16_t, 960 * 2> silence{};
    void SilenceUntil(uint64_t end) {
        end = std::min(end, maximumFrames);
        while (written < end) {
            const auto frames = static_cast<UINT>(std::min<uint64_t>(960, end - written));
            encoder.WriteAudio(silence.data(), frames); written += frames;
        }
    }
public:
    CaptureAudioTimeline(VideoEncoder& destination, uint64_t epoch, uint64_t maximum)
        : encoder(destination), origin(epoch), maximumFrames(maximum) {}
    void Append(const CapturedAudioPacket& packet) {
        static_assert(VideoEncoder::AudioRate == ProcessLoopbackCapture::Rate && VideoEncoder::AudioChannels == ProcessLoopbackCapture::Channels);
        if (packet.samples.size() % 2 || packet.samples.size() > ProcessLoopbackCapture::MaximumQueuedSamples)
            throw winrt::hresult_error(E_INVALIDARG);
        const auto relative = static_cast<int64_t>(packet.qpc100ns) - static_cast<int64_t>(origin);
        // Round to the closest sample instead of accumulating per-packet timing
        // rounding. Pre-epoch samples are clipped; gaps become explicit silence.
        const auto first = static_cast<int64_t>(std::llround(double(relative) * VideoEncoder::AudioRate / 10000000));
        const auto count = packet.samples.size() / 2;
        const auto end = first + static_cast<int64_t>(count);
        if (end <= static_cast<int64_t>(written) || written >= maximumFrames) return;
        if (first > static_cast<int64_t>(written)) SilenceUntil(static_cast<uint64_t>(first));
        if (written >= maximumFrames) return;
        const auto skipped = static_cast<size_t>(static_cast<int64_t>(written) - first);
        const auto frames = static_cast<UINT>(std::min<uint64_t>(count - skipped, maximumFrames - written));
        if (frames) { encoder.WriteAudio(packet.samples.data() + skipped * 2, frames); written += frames; }
    }
    void Finish(uint64_t frames) { SilenceUntil(frames); }
    uint64_t WrittenFrames() const noexcept { return written; }
};
}
