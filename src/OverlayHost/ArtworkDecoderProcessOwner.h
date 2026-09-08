#pragma once

#include "ArtworkDecoderProtocol.h"
#include "RemoteImageCache.h"

#include <Windows.h>

#include <chrono>
#include <atomic>
#include <cstdint>
#include <deque>
#include <stop_token>
#include <string>
#include <vector>

namespace widgetrail {

struct ArtworkDecoderProcessStats {
    std::uint64_t starts{};
    std::uint64_t completed{};
    std::uint64_t failed{};
    std::uint64_t timedOut{};
    std::uint64_t terminated{};
    std::uint64_t circuitRejected{};
};

class ArtworkDecoderProcessOwner final {
public:
    explicit ArtworkDecoderProcessOwner(
        RemoteImageLimits limits,
        std::wstring executablePath = {});
    ~ArtworkDecoderProcessOwner();

    ArtworkDecoderProcessOwner(const ArtworkDecoderProcessOwner&) = delete;
    ArtworkDecoderProcessOwner& operator=(const ArtworkDecoderProcessOwner&) = delete;

    [[nodiscard]] RemoteImageFetchResult Decode(
        std::vector<std::uint8_t> bytes,
        std::wstring mimeType,
        std::stop_token stopToken,
        artworkdecoder::TestBehavior testBehavior =
            artworkdecoder::TestBehavior::Normal,
        UINT32 requestedWidth = 0,
        UINT32 requestedHeight = 0,
        artworkdecoder::RasterVariant rasterVariant =
            artworkdecoder::RasterVariant::OriginalColor);
    [[nodiscard]] ArtworkDecoderProcessStats Stats() const noexcept;
    void Shutdown() noexcept;

private:
    [[nodiscard]] bool EnsureProcess(std::wstring& error);
    [[nodiscard]] bool StartProcess(std::wstring& error);
    void PoisonProcess(bool terminate) noexcept;
    void CloseProcess(bool terminate) noexcept;
    void RecordPoison() noexcept;
    [[nodiscard]] bool CircuitOpen() noexcept;

    RemoteImageLimits limits_;
    std::wstring executablePath_;
    HANDLE mapping_{};
    std::byte* view_{};
    HANDLE requestEvent_{};
    HANDLE responseEvent_{};
    HANDLE stopEvent_{};
    HANDLE process_{};
    HANDLE job_{};
    std::uint64_t nextCorrelation_{1};
    std::deque<std::chrono::steady_clock::time_point> poisonTimes_;
    std::chrono::steady_clock::time_point circuitUntil_{};
    std::atomic<std::uint64_t> starts_{};
    std::atomic<std::uint64_t> completed_{};
    std::atomic<std::uint64_t> failed_{};
    std::atomic<std::uint64_t> timedOut_{};
    std::atomic<std::uint64_t> terminated_{};
    std::atomic<std::uint64_t> circuitRejected_{};
    bool shuttingDown_{};
};

} // namespace widgetrail
