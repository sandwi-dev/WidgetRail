#include "ArtworkDecoderProcessOwner.h"

#include <algorithm>
#include <cstring>
#include <limits>
#include <sstream>
#include <utility>
#include <wincodec.h>

namespace widgetrail {
namespace {

[[nodiscard]] RemoteImageFetchResult Failure(
    const HRESULT result,
    std::wstring error) {
    return {result, {}, std::move(error)};
}

[[nodiscard]] HRESULT LastErrorResult() noexcept {
    const DWORD error = GetLastError();
    return HRESULT_FROM_WIN32(error == ERROR_SUCCESS ? ERROR_GEN_FAILURE : error);
}

void CloseHandleIfPresent(HANDLE& handle) noexcept {
    if (handle) CloseHandle(std::exchange(handle, nullptr));
}

[[nodiscard]] std::wstring DefaultDecoderPath() {
    std::vector<wchar_t> path(32'768);
    const DWORD length = GetModuleFileNameW(
        nullptr, path.data(), static_cast<DWORD>(path.size()));
    if (length == 0 || length >= path.size()) return {};
    std::wstring result(path.data(), length);
    const auto separator = result.find_last_of(L"\\/");
    if (separator == std::wstring::npos) return {};
    result.resize(separator + 1);
    result += L"ArtworkDecoderHost.exe";
    return result;
}

[[nodiscard]] std::wstring HandleArgument(
    const std::wstring_view name,
    const HANDLE handle) {
    std::wostringstream stream;
    stream << L" --" << name << L"=0x" << std::hex
           << reinterpret_cast<std::uintptr_t>(handle);
    return stream.str();
}

} // namespace

ArtworkDecoderProcessOwner::ArtworkDecoderProcessOwner(
    RemoteImageLimits limits,
    std::wstring executablePath)
    : limits_(limits),
      executablePath_(executablePath.empty()
          ? DefaultDecoderPath()
          : std::move(executablePath)) {}

ArtworkDecoderProcessOwner::~ArtworkDecoderProcessOwner() {
    Shutdown();
}

RemoteImageFetchResult ArtworkDecoderProcessOwner::Decode(
    std::vector<std::uint8_t> bytes,
    std::wstring mimeType,
    const std::stop_token stopToken,
    const artworkdecoder::TestBehavior testBehavior,
    const UINT32 requestedWidth,
    const UINT32 requestedHeight,
    const artworkdecoder::RasterVariant rasterVariant) {
    using namespace artworkdecoder;
    if (shuttingDown_ || stopToken.stop_requested())
        return Failure(E_ABORT, L"Trusted artwork decode was cancelled.");
    const ContentType contentType = mimeType == L"image/jpeg"
        ? ContentType::Jpeg
        : mimeType == L"image/png" ? ContentType::Png
        : mimeType == L"image/webp" ? ContentType::WebP
        : mimeType == L"image/svg+xml" ? ContentType::Svg : ContentType::Invalid;
    if (contentType == ContentType::Invalid || bytes.empty() ||
        bytes.size() > limits_.maximumEncodedArtworkBytes ||
        bytes.size() > maximumEncodedBytes ||
        (contentType == ContentType::Svg &&
            (requestedWidth == 0 || requestedHeight == 0 ||
             requestedWidth > 512 || requestedHeight > 512)) ||
        (contentType != ContentType::Svg &&
            (requestedWidth != 0 || requestedHeight != 0 ||
             rasterVariant != RasterVariant::OriginalColor)))
        return Failure(E_INVALIDARG, L"Trusted artwork decode input was invalid.");

    std::wstring startError;
    if (!EnsureProcess(startError))
        return Failure(HRESULT_FROM_WIN32(ERROR_RETRY), std::move(startError));

    const auto encodedBytes = bytes.size();
    const std::uint64_t correlation = nextCorrelation_++;
    if (nextCorrelation_ == 0) nextCorrelation_ = 1;
    auto* const header = reinterpret_cast<SharedHeader*>(view_);
    *header = {};
    header->magic = protocolMagic;
    header->version = protocolVersion;
    header->state = SharedState::Request;
    header->contentType = contentType;
    header->testBehavior = testBehavior;
    header->correlation = correlation;
    header->encodedBytes = encodedBytes;
    header->maximumDecodedBytes = limits_.maximumDecodedImageBytes;
    header->maximumPixels = limits_.maximumArtworkPixels;
    header->maximumDimension = limits_.maximumArtworkDimension;
    header->requestedWidth = requestedWidth;
    header->requestedHeight = requestedHeight;
    header->rasterVariant = rasterVariant;
    std::memcpy(view_ + encodedOffset, bytes.data(), bytes.size());
    SecureZeroMemory(bytes.data(), bytes.size());
    ResetEvent(responseEvent_);
    if (!SetEvent(requestEvent_)) {
        PoisonProcess(true);
        return Failure(LastErrorResult(), L"Trusted artwork decoder request failed.");
    }

    const ULONGLONG started = GetTickCount64();
    for (;;) {
        const ULONGLONG elapsed = GetTickCount64() - started;
        if (elapsed >= limits_.maximumArtworkDecodeMilliseconds) {
            ++timedOut_;
            PoisonProcess(true);
            return Failure(HRESULT_FROM_WIN32(ERROR_TIMEOUT),
                L"Trusted artwork decode exceeded its time budget.");
        }
        if (stopToken.stop_requested() || shuttingDown_) {
            PoisonProcess(true);
            return Failure(E_ABORT, L"Trusted artwork decode was cancelled.");
        }
        const DWORD remaining = static_cast<DWORD>(
            limits_.maximumArtworkDecodeMilliseconds - elapsed);
        const DWORD waitSlice = std::min<DWORD>(remaining, 25);
        const HANDLE waits[]{responseEvent_, process_};
        const DWORD wait = WaitForMultipleObjects(2, waits, FALSE, waitSlice);
        if (wait == WAIT_TIMEOUT) continue;
        if (wait == WAIT_OBJECT_0 + 1) {
            ++failed_;
            PoisonProcess(false);
            return Failure(HRESULT_FROM_WIN32(ERROR_BROKEN_PIPE),
                L"Trusted artwork decoder exited unexpectedly.");
        }
        if (wait != WAIT_OBJECT_0) {
            ++failed_;
            PoisonProcess(true);
            return Failure(LastErrorResult(),
                L"Trusted artwork decoder wait failed.");
        }
        break;
    }

    if (header->magic != protocolMagic || header->version != protocolVersion ||
        header->state != SharedState::Response ||
        header->correlation != correlation) {
        ++failed_;
        PoisonProcess(true);
        return Failure(HRESULT_FROM_WIN32(ERROR_INVALID_DATA),
            L"Trusted artwork decoder returned an invalid response.");
    }

    if (FAILED(header->result)) {
        ++failed_;
        SecureZeroMemory(view_ + encodedOffset, encodedBytes);
        return Failure(header->result,
            header->result == HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE)
                ? L"Trusted artwork dimensions exceed the allowed bound."
                : contentType == ContentType::WebP &&
                    header->result == WINCODEC_ERR_COMPONENTNOTFOUND
                ? L"Trusted artwork WebP decoder is unavailable."
                : L"Trusted artwork decoder rejected the image.");
    }

    if (header->decodedBytes > limits_.maximumDecodedImageBytes ||
        header->decodedBytes > maximumDecodedBytes ||
        header->width == 0 || header->height == 0 ||
        header->stride != static_cast<std::uint64_t>(header->width) * 4U ||
        static_cast<std::uint64_t>(header->stride) * header->height !=
            header->decodedBytes) {
        ++failed_;
        PoisonProcess(true);
        return Failure(HRESULT_FROM_WIN32(ERROR_INVALID_DATA),
            L"Trusted artwork decoder returned an invalid response.");
    }

    RemoteDecodedImage image;
    image.width = header->width;
    image.height = header->height;
    image.stride = header->stride;
    image.mimeType = std::move(mimeType);
    image.premultipliedBgra.resize(header->decodedBytes);
    std::memcpy(image.premultipliedBgra.data(),
                view_ + decodedOffset, header->decodedBytes);
    SecureZeroMemory(view_ + encodedOffset, encodedBytes);
    SecureZeroMemory(view_ + decodedOffset, header->decodedBytes);
    ++completed_;
    return {S_OK, std::move(image), {}};
}

ArtworkDecoderProcessStats ArtworkDecoderProcessOwner::Stats() const noexcept {
    return {
        starts_.load(),
        completed_.load(),
        failed_.load(),
        timedOut_.load(),
        terminated_.load(),
        circuitRejected_.load(),
    };
}

bool ArtworkDecoderProcessOwner::EnsureProcess(std::wstring& error) {
    if (process_) {
        if (WaitForSingleObject(process_, 0) == WAIT_TIMEOUT) return true;
        ++failed_;
        PoisonProcess(false);
    }
    if (CircuitOpen()) {
        ++circuitRejected_;
        error = L"Trusted artwork decoder restart circuit is open.";
        return false;
    }
    if (StartProcess(error)) return true;
    RecordPoison();
    return false;
}

bool ArtworkDecoderProcessOwner::StartProcess(std::wstring& error) {
    if (executablePath_.empty()) {
        error = L"Trusted artwork decoder executable was unavailable.";
        return false;
    }
    SECURITY_ATTRIBUTES security{sizeof(security), nullptr, TRUE};
    if (!mapping_) {
        constexpr std::uint64_t bytes = artworkdecoder::mappingBytes;
        mapping_ = CreateFileMappingW(INVALID_HANDLE_VALUE, &security, PAGE_READWRITE,
            static_cast<DWORD>(bytes >> 32), static_cast<DWORD>(bytes), nullptr);
        if (!mapping_) {
            error = L"Trusted artwork decoder shared memory creation failed.";
            return false;
        }
        view_ = static_cast<std::byte*>(MapViewOfFile(
            mapping_, FILE_MAP_ALL_ACCESS, 0, 0, artworkdecoder::mappingBytes));
        requestEvent_ = CreateEventW(&security, FALSE, FALSE, nullptr);
        responseEvent_ = CreateEventW(&security, FALSE, FALSE, nullptr);
        stopEvent_ = CreateEventW(&security, TRUE, FALSE, nullptr);
        if (!view_ || !requestEvent_ || !responseEvent_ || !stopEvent_) {
            error = L"Trusted artwork decoder IPC creation failed.";
            if (view_) UnmapViewOfFile(std::exchange(view_, nullptr));
            CloseHandleIfPresent(mapping_);
            CloseHandleIfPresent(requestEvent_);
            CloseHandleIfPresent(responseEvent_);
            CloseHandleIfPresent(stopEvent_);
            return false;
        }
    }
    ResetEvent(requestEvent_);
    ResetEvent(responseEvent_);
    ResetEvent(stopEvent_);

    job_ = CreateJobObjectW(nullptr, nullptr);
    JOBOBJECT_EXTENDED_LIMIT_INFORMATION jobLimits{};
    jobLimits.BasicLimitInformation.LimitFlags =
        JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE | JOB_OBJECT_LIMIT_ACTIVE_PROCESS;
    jobLimits.BasicLimitInformation.ActiveProcessLimit = 1;
    if (!job_ || !SetInformationJobObject(job_, JobObjectExtendedLimitInformation,
            &jobLimits, sizeof(jobLimits))) {
        error = L"Trusted artwork decoder job creation failed.";
        CloseHandleIfPresent(job_);
        return false;
    }

    HANDLE inherited[]{mapping_, requestEvent_, responseEvent_, stopEvent_};
    SIZE_T attributeBytes = 0;
    (void)InitializeProcThreadAttributeList(nullptr, 1, 0, &attributeBytes);
    std::vector<std::byte> attributes(attributeBytes);
    auto* const attributeList = reinterpret_cast<PPROC_THREAD_ATTRIBUTE_LIST>(
        attributes.data());
    if (!InitializeProcThreadAttributeList(attributeList, 1, 0, &attributeBytes)) {
        error = L"Trusted artwork decoder handle admission failed.";
        CloseHandleIfPresent(job_);
        return false;
    }
    if (!UpdateProcThreadAttribute(attributeList, 0,
            PROC_THREAD_ATTRIBUTE_HANDLE_LIST, inherited, sizeof(inherited),
            nullptr, nullptr)) {
        error = L"Trusted artwork decoder handle admission failed.";
        DeleteProcThreadAttributeList(attributeList);
        CloseHandleIfPresent(job_);
        return false;
    }

    std::wstring command = L"\"" + executablePath_ + L"\"";
    command += HandleArgument(L"mapping", mapping_);
    command += HandleArgument(L"request", requestEvent_);
    command += HandleArgument(L"response", responseEvent_);
    command += HandleArgument(L"stop", stopEvent_);
    STARTUPINFOEXW startup{};
    startup.StartupInfo.cb = sizeof(startup);
    startup.lpAttributeList = attributeList;
    PROCESS_INFORMATION process{};
    const BOOL created = CreateProcessW(
        executablePath_.c_str(), command.data(), nullptr, nullptr, TRUE,
        CREATE_SUSPENDED | CREATE_NO_WINDOW | EXTENDED_STARTUPINFO_PRESENT,
        nullptr, nullptr, &startup.StartupInfo, &process);
    DeleteProcThreadAttributeList(attributeList);
    if (!created) {
        error = L"Trusted artwork decoder process creation failed.";
        CloseHandleIfPresent(job_);
        return false;
    }
    if (!AssignProcessToJobObject(job_, process.hProcess) ||
        ResumeThread(process.hThread) == static_cast<DWORD>(-1)) {
        (void)TerminateProcess(process.hProcess, ERROR_PROCESS_ABORTED);
        (void)WaitForSingleObject(process.hProcess, 1'000);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        CloseHandleIfPresent(job_);
        error = L"Trusted artwork decoder process admission failed.";
        return false;
    }
    CloseHandle(process.hThread);
    process_ = process.hProcess;
    ++starts_;
    return true;
}

void ArtworkDecoderProcessOwner::RecordPoison() noexcept {
    const auto now = std::chrono::steady_clock::now();
    poisonTimes_.push_back(now);
    const auto window = std::chrono::milliseconds(
        limits_.artworkDecoderRestartWindowMilliseconds);
    while (!poisonTimes_.empty() && now - poisonTimes_.front() > window)
        poisonTimes_.pop_front();
    if (poisonTimes_.size() >= limits_.maximumArtworkDecoderRestarts)
        circuitUntil_ = now + std::chrono::milliseconds(
            limits_.artworkDecoderCircuitBreakerMilliseconds);
}

bool ArtworkDecoderProcessOwner::CircuitOpen() noexcept {
    const auto now = std::chrono::steady_clock::now();
    if (circuitUntil_ == std::chrono::steady_clock::time_point{}) return false;
    if (now < circuitUntil_) return true;
    circuitUntil_ = {};
    poisonTimes_.clear();
    return false;
}

void ArtworkDecoderProcessOwner::PoisonProcess(const bool terminate) noexcept {
    RecordPoison();
    CloseProcess(terminate);
}

void ArtworkDecoderProcessOwner::CloseProcess(const bool terminate) noexcept {
    if (process_ && terminate && WaitForSingleObject(process_, 0) == WAIT_TIMEOUT) {
        (void)TerminateProcess(process_, ERROR_TIMEOUT);
        ++terminated_;
    }
    if (process_) (void)WaitForSingleObject(process_, 1'000);
    CloseHandleIfPresent(process_);
    CloseHandleIfPresent(job_);
}

void ArtworkDecoderProcessOwner::Shutdown() noexcept {
    if (shuttingDown_) return;
    shuttingDown_ = true;
    if (stopEvent_) SetEvent(stopEvent_);
    if (process_ && WaitForSingleObject(
            process_, limits_.artworkDecoderShutdownMilliseconds) == WAIT_TIMEOUT)
        CloseProcess(true);
    else
        CloseProcess(false);
    if (view_) UnmapViewOfFile(std::exchange(view_, nullptr));
    CloseHandleIfPresent(mapping_);
    CloseHandleIfPresent(requestEvent_);
    CloseHandleIfPresent(responseEvent_);
    CloseHandleIfPresent(stopEvent_);
}

} // namespace widgetrail
