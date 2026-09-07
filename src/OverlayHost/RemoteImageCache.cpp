#include "RemoteImageCache.h"
#include "ArtworkDecoderProcessOwner.h"
#include "EncodedArtworkEnvelope.h"

#include <WinHttp.h>
#include <wincodec.h>

#include <algorithm>
#include <chrono>
#include <cwctype>
#include <limits>
#include <optional>
#include <span>
#include <stdexcept>
#include <utility>
#include <unordered_set>

namespace widgetrail {
namespace {

using Microsoft::WRL::ComPtr;

class InternetHandle final {
public:
    InternetHandle() = default;
    explicit InternetHandle(HINTERNET value) noexcept : value_(value) {}
    ~InternetHandle() { reset(); }
    InternetHandle(const InternetHandle&) = delete;
    InternetHandle& operator=(const InternetHandle&) = delete;
    InternetHandle(InternetHandle&& other) noexcept : value_(std::exchange(other.value_, nullptr)) {}
    InternetHandle& operator=(InternetHandle&& other) noexcept {
        if (this != &other) {
            reset();
            value_ = std::exchange(other.value_, nullptr);
        }
        return *this;
    }
    [[nodiscard]] HINTERNET get() const noexcept { return value_; }
    [[nodiscard]] explicit operator bool() const noexcept { return value_ != nullptr; }
    void reset(HINTERNET value = nullptr) noexcept {
        if (value_) WinHttpCloseHandle(value_);
        value_ = value;
    }

private:
    HINTERNET value_{};
};

struct ParsedUrl {
    std::wstring host;
    std::wstring resource;
    INTERNET_PORT port{};
};

[[nodiscard]] std::optional<ParsedUrl> ParseHttpsUrl(std::wstring_view url) {
    if (url.empty() || url.size() > 8'192) return std::nullopt;
    URL_COMPONENTS parts{sizeof(parts)};
    parts.dwSchemeLength = static_cast<DWORD>(-1);
    parts.dwHostNameLength = static_cast<DWORD>(-1);
    parts.dwUrlPathLength = static_cast<DWORD>(-1);
    parts.dwExtraInfoLength = static_cast<DWORD>(-1);
    parts.dwUserNameLength = static_cast<DWORD>(-1);
    parts.dwPasswordLength = static_cast<DWORD>(-1);
    std::wstring owned(url);
    if (!WinHttpCrackUrl(owned.c_str(), static_cast<DWORD>(owned.size()), 0, &parts) ||
        parts.nScheme != INTERNET_SCHEME_HTTPS || parts.dwHostNameLength == 0 ||
        parts.dwUserNameLength != 0 || parts.dwPasswordLength != 0) {
        return std::nullopt;
    }

    ParsedUrl parsed;
    parsed.host.assign(parts.lpszHostName, parts.dwHostNameLength);
    parsed.resource.assign(parts.lpszUrlPath, parts.dwUrlPathLength);
    parsed.resource.append(parts.lpszExtraInfo, parts.dwExtraInfoLength);
    if (const auto fragment = parsed.resource.find(L'#'); fragment != std::wstring::npos)
        parsed.resource.resize(fragment);
    if (parsed.resource.empty()) parsed.resource = L"/";
    parsed.port = parts.nPort;
    return parsed;
}

[[nodiscard]] std::wstring QueryHeader(HINTERNET request, DWORD query) {
    DWORD bytes = 0;
    WinHttpQueryHeaders(request, query, WINHTTP_HEADER_NAME_BY_INDEX,
                        WINHTTP_NO_OUTPUT_BUFFER, &bytes, WINHTTP_NO_HEADER_INDEX);
    if (GetLastError() != ERROR_INSUFFICIENT_BUFFER || bytes < sizeof(wchar_t)) return {};
    std::vector<wchar_t> value(bytes / sizeof(wchar_t));
    if (!WinHttpQueryHeaders(request, query, WINHTTP_HEADER_NAME_BY_INDEX,
                             value.data(), &bytes, WINHTTP_NO_HEADER_INDEX)) return {};
    return std::wstring(value.data());
}

[[nodiscard]] bool IsImageMime(std::wstring value) {
    const auto semicolon = value.find(L';');
    if (semicolon != std::wstring::npos) value.resize(semicolon);
    while (!value.empty() && std::iswspace(value.back())) value.pop_back();
    value.erase(value.begin(), std::find_if(value.begin(), value.end(),
        [](wchar_t character) { return !std::iswspace(character); }));
    std::transform(value.begin(), value.end(), value.begin(),
                   [](wchar_t character) { return static_cast<wchar_t>(std::towlower(character)); });
    return value.size() > 6 && value.starts_with(L"image/");
}

[[nodiscard]] RemoteImageFetchResult Failure(HRESULT result, std::wstring error) {
    return {result, {}, std::move(error)};
}

[[nodiscard]] HRESULT LastErrorResult() noexcept {
    const DWORD error = GetLastError();
    return HRESULT_FROM_WIN32(error == ERROR_SUCCESS ? ERROR_GEN_FAILURE : error);
}

[[nodiscard]] RemoteImageFetchResult DecodeWithWic(
    std::vector<std::uint8_t> bytes,
    std::wstring mime,
    const RemoteImageLimits& limits) {
    const HRESULT initialization = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    const bool uninitialize = SUCCEEDED(initialization);
    if (FAILED(initialization) && initialization != RPC_E_CHANGED_MODE)
        return Failure(initialization, L"COM initialization failed.");

    ComPtr<IWICImagingFactory> factory;
    HRESULT result = CoCreateInstance(CLSID_WICImagingFactory2, nullptr, CLSCTX_INPROC_SERVER,
                                      IID_PPV_ARGS(factory.ReleaseAndGetAddressOf()));
    if (FAILED(result)) {
        result = CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER,
                                  IID_PPV_ARGS(factory.ReleaseAndGetAddressOf()));
    }
    if (FAILED(result)) {
        if (uninitialize) CoUninitialize();
        return Failure(result, L"WIC factory creation failed.");
    }

    ComPtr<IWICStream> stream;
    ComPtr<IWICBitmapDecoder> decoder;
    ComPtr<IWICBitmapFrameDecode> frame;
    ComPtr<IWICFormatConverter> converter;
    result = factory->CreateStream(stream.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result)) {
        result = stream->InitializeFromMemory(bytes.data(), static_cast<DWORD>(bytes.size()));
    }
    if (SUCCEEDED(result)) {
        result = factory->CreateDecoderFromStream(stream.Get(), nullptr,
            WICDecodeMetadataCacheOnLoad, decoder.ReleaseAndGetAddressOf());
    }
    if (SUCCEEDED(result)) result = decoder->GetFrame(0, frame.ReleaseAndGetAddressOf());

    UINT width = 0;
    UINT height = 0;
    if (SUCCEEDED(result)) result = frame->GetSize(&width, &height);
    const std::uint64_t stride64 = static_cast<std::uint64_t>(width) * 4U;
    const std::uint64_t decoded64 = stride64 * height;
    if (SUCCEEDED(result) &&
        (width == 0 || height == 0 || stride64 > std::numeric_limits<UINT>::max() ||
         decoded64 > limits.maximumDecodedImageBytes ||
         decoded64 > std::numeric_limits<UINT>::max())) {
        result = HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE);
    }
    if (SUCCEEDED(result)) result = factory->CreateFormatConverter(converter.ReleaseAndGetAddressOf());
    if (SUCCEEDED(result)) {
        result = converter->Initialize(frame.Get(), GUID_WICPixelFormat32bppPBGRA,
            WICBitmapDitherTypeNone, nullptr, 0.0, WICBitmapPaletteTypeCustom);
    }

    RemoteDecodedImage image;
    if (SUCCEEDED(result)) {
        image.width = width;
        image.height = height;
        image.stride = static_cast<UINT32>(stride64);
        image.premultipliedBgra.resize(static_cast<std::size_t>(decoded64));
        image.mimeType = std::move(mime);
        result = converter->CopyPixels(nullptr, image.stride,
            static_cast<UINT>(image.premultipliedBgra.size()), image.premultipliedBgra.data());
    }
    if (uninitialize) CoUninitialize();
    if (FAILED(result)) return Failure(result, L"WIC rejected or could not decode the image.");
    return {S_OK, std::move(image), {}};
}

constexpr std::wstring_view inlinePngPrefix = L"data:image/png;base64,";
constexpr std::size_t maximumInlinePngBytes = 12U * 1024U;
constexpr UINT32 maximumInlinePngDimension = 64;

[[nodiscard]] int DecodeBase64Character(wchar_t value) noexcept {
    if (value >= L'A' && value <= L'Z') return value - L'A';
    if (value >= L'a' && value <= L'z') return value - L'a' + 26;
    if (value >= L'0' && value <= L'9') return value - L'0' + 52;
    if (value == L'+') return 62;
    if (value == L'/') return 63;
    return -1;
}

[[nodiscard]] std::optional<std::vector<std::uint8_t>> ParseInlinePng(
    std::wstring_view source) {
    if (!source.starts_with(inlinePngPrefix)) return std::nullopt;
    source.remove_prefix(inlinePngPrefix.size());
    if (source.empty() || source.size() % 4 != 0 ||
        source.size() > ((maximumInlinePngBytes + 2U) / 3U) * 4U)
        return std::nullopt;

    std::vector<std::uint8_t> decoded;
    decoded.reserve(source.size() / 4U * 3U);
    for (std::size_t offset = 0; offset < source.size(); offset += 4) {
        const bool last = offset + 4 == source.size();
        const int a = DecodeBase64Character(source[offset]);
        const int b = DecodeBase64Character(source[offset + 1]);
        const int c = source[offset + 2] == L'=' ? -2 : DecodeBase64Character(source[offset + 2]);
        const int d = source[offset + 3] == L'=' ? -2 : DecodeBase64Character(source[offset + 3]);
        if (a < 0 || b < 0 || c == -1 || d == -1 ||
            (c == -2 && d != -2) || (!last && (c == -2 || d == -2)))
            return std::nullopt;
        decoded.push_back(static_cast<std::uint8_t>((a << 2) | (b >> 4)));
        if (c != -2) {
            decoded.push_back(static_cast<std::uint8_t>((b << 4) | (c >> 2)));
            if (d != -2)
                decoded.push_back(static_cast<std::uint8_t>((c << 6) | d));
            else if ((c & 0x03) != 0)
                return std::nullopt;
        } else if ((b & 0x0f) != 0) {
            return std::nullopt;
        }
    }
    if (decoded.size() < 45 || decoded.size() > maximumInlinePngBytes)
        return std::nullopt;
    constexpr std::uint8_t signature[]{137, 80, 78, 71, 13, 10, 26, 10};
    if (!std::equal(std::begin(signature), std::end(signature), decoded.begin()))
        return std::nullopt;
    return decoded;
}

[[nodiscard]] std::optional<std::vector<std::uint8_t>> DecodeBoundedBase64(
    std::wstring_view source,
    const std::size_t maximumBytes) {
    if (source.empty() || source.size() % 4 != 0 ||
        source.size() > ((maximumBytes + 2U) / 3U) * 4U)
        return std::nullopt;
    std::vector<std::uint8_t> decoded;
    decoded.reserve(source.size() / 4U * 3U);
    for (std::size_t offset = 0; offset < source.size(); offset += 4) {
        const bool last = offset + 4 == source.size();
        const int a = DecodeBase64Character(source[offset]);
        const int b = DecodeBase64Character(source[offset + 1]);
        const int c = source[offset + 2] == L'=' ? -2 : DecodeBase64Character(source[offset + 2]);
        const int d = source[offset + 3] == L'=' ? -2 : DecodeBase64Character(source[offset + 3]);
        if (a < 0 || b < 0 || c == -1 || d == -1 ||
            (c == -2 && d != -2) || (!last && (c == -2 || d == -2)))
            return std::nullopt;
        decoded.push_back(static_cast<std::uint8_t>((a << 2) | (b >> 4)));
        if (c != -2) {
            decoded.push_back(static_cast<std::uint8_t>((b << 4) | (c >> 2)));
            if (d != -2)
                decoded.push_back(static_cast<std::uint8_t>((c << 6) | d));
            else if ((c & 0x03) != 0)
                return std::nullopt;
        } else if ((b & 0x0f) != 0) {
            return std::nullopt;
        }
    }
    if (decoded.empty() || decoded.size() > maximumBytes) return std::nullopt;
    return decoded;
}

[[nodiscard]] bool MatchesArtworkSignature(
    const std::wstring_view contentType,
    const std::span<const std::uint8_t> bytes) noexcept {
    return encoded_artwork::Matches(contentType, bytes);
}

} // namespace

RemoteImageCache::RemoteImageCache(
    RemoteImageLimits limits,
    CompletionCallback completion,
    FetchFunction fetch,
    ArtworkRequestFunction artworkRequest,
    ArtworkDecodeDiagnosticCallback artworkDecodeDiagnostic)
    : limits_(limits),
      completion_(std::move(completion)),
      usesCustomFetch_(static_cast<bool>(fetch)),
      fetch_(fetch ? std::move(fetch) : FetchAndDecodeSource),
      artworkRequest_(std::move(artworkRequest)),
      artworkDecodeDiagnostic_(std::move(artworkDecodeDiagnostic)) {
    if (limits_.maximumEntries == 0 || limits_.maximumEntries > 1'024 ||
        limits_.maximumReadyEntries == 0 || limits_.maximumReadyEntries > 1'024 ||
        limits_.maximumPendingEntries == 0 || limits_.maximumPendingEntries > 1'024 ||
        limits_.maximumDecodedImageBytes < 4 ||
        limits_.maximumDecodedImageBytes > 64U * 1024U * 1024U ||
        limits_.maximumDecodedBytes < 4 ||
        limits_.maximumDecodedBytes > 256U * 1024U * 1024U ||
        limits_.maximumDownloadBytes == 0 ||
        limits_.maximumDownloadBytes > 5U * 1024U * 1024U ||
        limits_.maximumEncodedArtworkBytes == 0 ||
        limits_.maximumEncodedArtworkBytes > 8U * 1024U * 1024U ||
        limits_.maximumEncodedArtworkBytesPerWidget < limits_.maximumEncodedArtworkBytes ||
        limits_.maximumEncodedArtworkBytesTotal < limits_.maximumEncodedArtworkBytesPerWidget ||
        limits_.maximumEncodedArtworkBytesTotal > 256U * 1024U * 1024U ||
        limits_.maximumArtworkPixels < 1 || limits_.maximumArtworkPixels > 16'777'216 ||
        limits_.maximumArtworkDimension < 1 || limits_.maximumArtworkDimension > 4'096 ||
        limits_.maximumArtworkDecodeMilliseconds == 0 ||
        limits_.maximumArtworkDecodeMilliseconds > 60'000 ||
        limits_.maximumArtworkDecoderRestarts == 0 ||
        limits_.maximumArtworkDecoderRestarts > 16 ||
        limits_.artworkDecoderRestartWindowMilliseconds == 0 ||
        limits_.artworkDecoderRestartWindowMilliseconds > 300'000 ||
        limits_.artworkDecoderCircuitBreakerMilliseconds == 0 ||
        limits_.artworkDecoderCircuitBreakerMilliseconds > 300'000 ||
        limits_.artworkDecoderShutdownMilliseconds == 0 ||
        limits_.artworkDecoderShutdownMilliseconds > 10'000 ||
        limits_.maximumRedirects > 10 ||
        limits_.resolveTimeoutMilliseconds == 0 || limits_.resolveTimeoutMilliseconds > 60'000 ||
        limits_.connectTimeoutMilliseconds == 0 || limits_.connectTimeoutMilliseconds > 60'000 ||
        limits_.sendTimeoutMilliseconds == 0 || limits_.sendTimeoutMilliseconds > 60'000 ||
        limits_.receiveTimeoutMilliseconds == 0 || limits_.receiveTimeoutMilliseconds > 60'000) {
        throw std::invalid_argument("Invalid remote image cache limits.");
    }
    if (!usesCustomFetch_)
        artworkDecoder_ = std::make_unique<ArtworkDecoderProcessOwner>(limits_);
    worker_ = std::jthread([this](std::stop_token token) { WorkerLoop(token); });
    if (artworkRequest_) {
        artworkDemandWorker_ = std::jthread(
            [this](std::stop_token token) { ArtworkDemandLoop(token); });
    }
}

RemoteImageCache::~RemoteImageCache() {
    Shutdown();
}

RemoteImageRequestResult RemoteImageCache::Request(std::wstring url) {
    if (!IsAllowedImageSource(url)) return RemoteImageRequestResult::InvalidUrl;
    std::scoped_lock lock(mutex_);
    return QueueLocked(std::move(url), false);
}

RemoteImageRequestResult RemoteImageCache::RequestTrustedArtwork(std::wstring key) {
    constexpr std::wstring_view prefix = L"wrail-artwork\x1f";
    const auto widgetEnd = key.find(L'\x1f', prefix.size());
    if (!key.starts_with(prefix) || widgetEnd == std::wstring::npos)
        return RemoteImageRequestResult::InvalidUrl;
    auto widgetId = std::wstring{std::wstring_view(key).substr(
        prefix.size(), widgetEnd - prefix.size())};
    return RequestTrustedArtwork(
        std::move(key),
        {std::move(widgetId), L"legacy-runtime", L"legacy-presentation"});
}

RemoteImageRequestResult RemoteImageCache::RequestTrustedArtwork(
    std::wstring key,
    TrustedArtworkDemandAuthority authority) {
    constexpr std::wstring_view prefix = L"wrail-artwork\x1f";
    if (!key.starts_with(prefix) || key.size() > 384) return RemoteImageRequestResult::InvalidUrl;
    if (!artworkRequest_) {
        std::scoped_lock lock(mutex_);
        return QueueLocked(std::move(key), false);
    }
    if (authority.widgetId.empty() || authority.runtimeGeneration.empty() ||
        authority.presentationGeneration.empty()) {
        return RemoteImageRequestResult::InvalidUrl;
    }
    const auto widgetEnd = key.find(L'\x1f', prefix.size());
    if (widgetEnd == std::wstring::npos ||
        std::wstring_view(key).substr(
            prefix.size(), widgetEnd - prefix.size()) != authority.widgetId) {
        return RemoteImageRequestResult::InvalidUrl;
    }
    {
        std::scoped_lock lock(mutex_);
        if (trustedArtworkRequests_ != UINT64_MAX) ++trustedArtworkRequests_;
        if (shuttingDown_) return RemoteImageRequestResult::ShuttingDown;
        if (const auto found = entries_.find(key); found != entries_.end()) {
            if (found->second.state == RemoteImageState::Loading &&
                found->second.demandAuthority &&
                *found->second.demandAuthority != authority) {
                const auto generation = ++artworkDemandGeneration_;
                found->second.demandAuthority = authority;
                found->second.demandGeneration = generation;
                std::erase_if(artworkDemandQueue_, [&](const ArtworkDemand& pending) {
                    return pending.key == key;
                });
                artworkDemandQueue_.push_back(
                    ArtworkDemand{std::move(key), std::move(authority), generation});
                artworkDemandCondition_.notify_one();
                return RemoteImageRequestResult::Queued;
            }
            if (trustedArtworkHits_ != UINT64_MAX) ++trustedArtworkHits_;
            found->second.lastUse = ++useCounter_;
            return RemoteImageRequestResult::AlreadyTracked;
        }
        const auto handleSeparator = key.rfind(L'\x1f');
        if (handleSeparator == std::wstring::npos)
            return RemoteImageRequestResult::InvalidUrl;
        const auto handleSuffix = key.substr(handleSeparator);
        const bool handleIsTerminal = std::any_of(
            entries_.begin(), entries_.end(), [&](const auto& item) {
                return widgetEnd != std::wstring::npos &&
                    item.first.starts_with(key.substr(0, widgetEnd + 1)) &&
                    item.first.ends_with(handleSuffix) &&
                    item.second.state == RemoteImageState::Failed;
            });
        if (!handleIsTerminal &&
            PendingCountLocked() >=
                std::min(limits_.maximumPendingEntries, limits_.maximumEntries)) {
            ++pendingCapacityRejections_;
            return RemoteImageRequestResult::CapacityExceeded;
        }
        while (entries_.size() >= limits_.maximumEntries) {
            if (!EvictOneLocked(key, EvictionReason::CountPressure)) {
                ++countCapacityRejections_;
                return RemoteImageRequestResult::CapacityExceeded;
            }
        }
        if (handleIsTerminal) {
            entries_.emplace(key, Entry{
                RemoteImageState::Failed, {}, L"Trusted artwork is unavailable.", {}, {}, {},
                ++useCounter_});
            return RemoteImageRequestResult::AlreadyTracked;
        }
        const auto generation = ++artworkDemandGeneration_;
        auto [inserted, _] = entries_.emplace(key, Entry{
            RemoteImageState::Loading, {}, {}, {}, {}, {}, ++useCounter_});
        inserted->second.demandAuthority = authority;
        inserted->second.demandGeneration = generation;
        artworkDemandQueue_.push_back(
            ArtworkDemand{std::move(key), std::move(authority), generation});
    }
    artworkDemandCondition_.notify_one();
    return RemoteImageRequestResult::Queued;
}

bool RemoteImageCache::SupplyTrustedArtwork(
    const std::wstring_view widgetId,
    const std::wstring_view artworkHandle,
    std::wstring contentType,
    std::wstring contentBase64) {
    std::optional<TrustedArtworkDemandAuthority> authority;
    {
        std::scoped_lock lock(mutex_);
        const auto prefix = L"wrail-artwork\x1f" + std::wstring(widgetId) + L"\x1f";
        const auto suffix = L"\x1f" + std::wstring(artworkHandle);
        const auto found = std::find_if(entries_.begin(), entries_.end(), [&](const auto& item) {
            return item.first.starts_with(prefix) && item.first.ends_with(suffix) &&
                item.second.state == RemoteImageState::Loading &&
                item.second.demandAuthority.has_value();
        });
        if (found != entries_.end()) authority = found->second.demandAuthority;
    }
    return authority && SupplyTrustedArtwork(
        widgetId, artworkHandle, *authority,
        std::move(contentType), std::move(contentBase64));
}

bool RemoteImageCache::SupplyTrustedArtwork(
    const std::wstring_view widgetId,
    const std::wstring_view artworkHandle,
    const TrustedArtworkDemandAuthority& authority,
    std::wstring contentType,
    std::wstring contentBase64) {
    auto bytes = DecodeBoundedBase64(contentBase64, limits_.maximumEncodedArtworkBytes);
    if (!bytes || !MatchesArtworkSignature(contentType, *bytes)) return false;
    std::scoped_lock lock(mutex_);
    if (shuttingDown_) return false;
    const auto prefix = L"wrail-artwork\x1f" + std::wstring(widgetId) + L"\x1f";
    const auto suffix = L"\x1f" + std::wstring(artworkHandle);
    std::size_t widgetEncodedBytes = 0;
    for (const auto& [key, entry] : entries_)
        if (key.starts_with(prefix)) widgetEncodedBytes += entry.pendingBytes.size();
    if (encodedArtworkBytes_ + bytes->size() > limits_.maximumEncodedArtworkBytesTotal ||
        widgetEncodedBytes + bytes->size() > limits_.maximumEncodedArtworkBytesPerWidget)
        return false;
    bool supplied = false;
    for (auto& [key, entry] : entries_) {
        if (!key.starts_with(prefix) || !key.ends_with(suffix) ||
            entry.state != RemoteImageState::Loading ||
            !entry.demandAuthority || *entry.demandAuthority != authority) continue;
        entry.pendingBytes = *bytes;
        entry.pendingMimeType = contentType;
        encodedArtworkBytes_ += entry.pendingBytes.size();
        entry.state = RemoteImageState::Queued;
        queue_.push_back(key);
        supplied = true;
    }
    if (!supplied) {
        if (staleArtworkCompletions_ != UINT64_MAX) ++staleArtworkCompletions_;
        return false;
    }
    if (trustedArtworkSupplies_ != UINT64_MAX) ++trustedArtworkSupplies_;
    condition_.notify_one();
    return true;
}

bool RemoteImageCache::FailTrustedArtwork(
    const std::wstring_view widgetId,
    const std::wstring_view artworkHandle,
    const TrustedArtworkDemandAuthority& authority) {
    CompletionCallback completion;
    std::wstring transitionKey;
    {
        std::scoped_lock lock(mutex_);
        if (shuttingDown_) return false;
        const auto prefix = L"wrail-artwork\x1f" + std::wstring(widgetId) + L"\x1f";
        const auto suffix = L"\x1f" + std::wstring(artworkHandle);
        bool failed = false;
        for (auto& [key, entry] : entries_) {
            if (!key.starts_with(prefix) || !key.ends_with(suffix) ||
                entry.state != RemoteImageState::Loading ||
                (!authority.widgetId.empty() &&
                 (!entry.demandAuthority || *entry.demandAuthority != authority))) continue;
            entry.state = RemoteImageState::Failed;
            entry.error = L"Trusted artwork is unavailable.";
            if (transitionKey.empty()) transitionKey = key;
            failed = true;
        }
        if (!failed) return false;
        completion = completion_;
    }
    if (completion) {
        // One result event may cover repeated nodes that share the same opaque
        // handle. Publish one state transition, not one callback per cache key;
        // repeated failures are already rejected by the Loading-state guard.
        try { completion(transitionKey, RemoteImageState::Failed); }
        catch (...) { }
    }
    return true;
}

bool RemoteImageCache::RetireTrustedArtworkDemand(
    const std::wstring_view widgetId,
    const std::wstring_view artworkHandle,
    const TrustedArtworkDemandAuthority& authority) {
    CompletionCallback completion;
    std::wstring transitionKey;
    {
        std::scoped_lock lock(mutex_);
        if (shuttingDown_) return false;
        const auto prefix = L"wrail-artwork\x1f" + std::wstring(widgetId) + L"\x1f";
        const auto suffix = L"\x1f" + std::wstring(artworkHandle);
        const auto found = std::find_if(entries_.begin(), entries_.end(), [&](const auto& item) {
            return item.first.starts_with(prefix) && item.first.ends_with(suffix) &&
                item.second.state == RemoteImageState::Loading &&
                item.second.demandAuthority && *item.second.demandAuthority == authority;
        });
        if (found == entries_.end()) return false;
        transitionKey = found->first;
        entries_.erase(found);
        std::erase_if(artworkDemandQueue_, [&](const ArtworkDemand& pending) {
            return pending.key == transitionKey && pending.authority == authority;
        });
        completion = completion_;
    }
    if (completion) {
        try { completion(transitionKey, RemoteImageState::Missing); }
        catch (...) { }
    }
    return true;
}

RemoteImageRequestResult RemoteImageCache::Retry(std::wstring url) {
    if (!IsAllowedImageSource(url)) return RemoteImageRequestResult::InvalidUrl;
    std::scoped_lock lock(mutex_);
    return QueueLocked(std::move(url), true);
}

RemoteImageState RemoteImageCache::GetState(std::wstring_view url) const {
    std::scoped_lock lock(mutex_);
    const auto found = entries_.find(std::wstring(url));
    return found == entries_.end() ? RemoteImageState::Missing : found->second.state;
}

RemoteImageState RemoteImageCache::GetTrustedArtworkState(
    const std::wstring_view key,
    const TrustedArtworkDemandAuthority& authority) const {
    std::scoped_lock lock(mutex_);
    const auto found = entries_.find(std::wstring(key));
    if (found == entries_.end()) return RemoteImageState::Missing;
    if (found->second.state == RemoteImageState::Loading &&
        found->second.demandAuthority &&
        *found->second.demandAuthority != authority) {
        return RemoteImageState::Missing;
    }
    return found->second.state;
}

std::wstring RemoteImageCache::GetError(std::wstring_view url) const {
    std::scoped_lock lock(mutex_);
    const auto found = entries_.find(std::wstring(url));
    return found == entries_.end() ? std::wstring{} : found->second.error;
}

RemoteImageCacheStats RemoteImageCache::GetStats() const {
    std::scoped_lock lock(mutex_);
    std::size_t pending = 0;
    std::size_t ready = 0;
    std::size_t failed = 0;
    std::size_t https = 0;
    std::size_t inlineImages = 0;
    std::size_t trustedArtwork = 0;
    std::uint64_t readySourcePixels = 0;
    UINT32 maximumSourceWidth = 0;
    UINT32 maximumSourceHeight = 0;
    for (const auto& [url, entry] : entries_) {
        if (entry.state == RemoteImageState::Queued || entry.state == RemoteImageState::Loading) ++pending;
        else if (entry.state == RemoteImageState::Ready) ++ready;
        else if (entry.state == RemoteImageState::Failed) ++failed;
        if (url.starts_with(L"wrail-artwork\x1f")) ++trustedArtwork;
        else if (url.starts_with(inlinePngPrefix)) ++inlineImages;
        else ++https;
        if (entry.state == RemoteImageState::Ready && entry.image) {
            readySourcePixels += static_cast<std::uint64_t>(entry.image->width) *
                static_cast<std::uint64_t>(entry.image->height);
            maximumSourceWidth = std::max(maximumSourceWidth, entry.image->width);
            maximumSourceHeight = std::max(maximumSourceHeight, entry.image->height);
        }
    }
    return {
        entries_.size(), decodedBytes_, pending, ready, failed,
        https, inlineImages, trustedArtwork, evictions_,
        countPressureEvictions_, bytePressureEvictions_, supersededEntries_,
        countCapacityRejections_, pendingCapacityRejections_,
        encodedArtworkBytes_, trustedArtworkRequests_, trustedArtworkHits_,
        trustedArtworkSupplies_, staleArtworkCompletions_, readySourcePixels,
        maximumSourceWidth, maximumSourceHeight,
    };
}

TrustedArtworkResidencyStats RemoteImageCache::GetTrustedArtworkResidency(
    const std::wstring_view widgetId,
    const std::span<const std::wstring> currentArtworkHandles) const {
    TrustedArtworkResidencyStats result;
    if (widgetId.empty() || currentArtworkHandles.empty()) return result;
    const auto prefix = L"wrail-artwork\x1f" + std::wstring(widgetId) + L"\x1f";
    std::unordered_set<std::wstring_view> handles;
    handles.reserve(currentArtworkHandles.size());
    for (const auto& handle : currentArtworkHandles)
        if (!handle.empty()) handles.insert(handle);
    std::scoped_lock lock(mutex_);
    for (const auto& [key, entry] : entries_) {
        if (!key.starts_with(prefix)) continue;
        const auto separator = key.rfind(L'\x1f');
        if (separator == std::wstring::npos ||
            !handles.contains(std::wstring_view(key).substr(separator + 1))) continue;
        ++result.entries;
        result.encodedBytes += entry.pendingBytes.size();
        if (entry.state == RemoteImageState::Ready && entry.image) {
            ++result.readyEntries;
            result.decodedBytes += entry.image->premultipliedBgra.size();
        } else if (entry.state == RemoteImageState::Queued ||
                   entry.state == RemoteImageState::Loading) {
            ++result.inFlightEntries;
        }
    }
    return result;
}

std::shared_ptr<const RemoteDecodedImage> RemoteImageCache::GetReadyImage(
    const std::wstring_view key) {
    std::lock_guard lock(mutex_);
    const auto found = entries_.find(std::wstring(key));
    if (found == entries_.end() || found->second.state != RemoteImageState::Ready ||
        !found->second.image)
        return {};
    found->second.lastUse = ++useCounter_;
    return found->second.image;
}

std::wstring RemoteImageCache::TrustedArtworkKey(
    const std::wstring_view widgetId,
    const std::wstring_view nodeId,
    const std::wstring_view artworkHandle) {
    if (widgetId.empty() || nodeId.empty() || artworkHandle.empty()) return {};
    (void)nodeId;
    std::wstring result = L"wrail-artwork\x1f";
    result.append(widgetId);
    result.push_back(L'\x1f');
    result.append(L"resource");
    result.push_back(L'\x1f');
    result.append(artworkHandle);
    return result;
}

std::uint64_t RemoteImageCache::OpaqueDiagnosticHash(
    const std::wstring_view value) noexcept {
    std::uint64_t result = 1469598103934665603ULL;
    for (const wchar_t character : value) {
        result ^= static_cast<std::uint16_t>(character);
        result *= 1099511628211ULL;
    }
    return result;
}

HRESULT RemoteImageCache::CreateBitmap(
    ID2D1RenderTarget* renderTarget,
    std::wstring_view url,
    ID2D1Bitmap** bitmap) {
    if (!renderTarget || !bitmap) return E_POINTER;
    *bitmap = nullptr;
    std::shared_ptr<const RemoteDecodedImage> image;
    RemoteImageState state = RemoteImageState::Missing;
    {
        std::scoped_lock lock(mutex_);
        const auto found = entries_.find(std::wstring(url));
        if (found != entries_.end()) {
            state = found->second.state;
            found->second.lastUse = ++useCounter_;
            image = found->second.image;
        }
    }
    if (state == RemoteImageState::Queued || state == RemoteImageState::Loading) return E_PENDING;
    if (state != RemoteImageState::Ready || !image)
        return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    const auto properties = D2D1::BitmapProperties(
        D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED),
        96.0F, 96.0F);
    return renderTarget->CreateBitmap(
        D2D1::SizeU(image->width, image->height),
        image->premultipliedBgra.data(), image->stride, properties, bitmap);
}

void RemoteImageCache::Clear() {
    std::scoped_lock lock(mutex_);
    for (auto iterator = entries_.begin(); iterator != entries_.end();) {
        if (iterator->second.state == RemoteImageState::Queued ||
            iterator->second.state == RemoteImageState::Loading) {
            ++iterator;
        } else {
            iterator = entries_.erase(iterator);
        }
    }
    decodedBytes_ = 0;
}

void RemoteImageCache::Shutdown() noexcept {
    {
        std::scoped_lock lock(mutex_);
        if (shuttingDown_) return;
        shuttingDown_ = true;
        artworkDemandQueue_.clear();
        queue_.clear();
    }
    artworkDemandWorker_.request_stop();
    artworkDemandCondition_.notify_all();
    if (artworkDemandWorker_.joinable()) artworkDemandWorker_.join();
    worker_.request_stop();
    condition_.notify_all();
    if (worker_.joinable()) worker_.join();
    if (artworkDecoder_) artworkDecoder_->Shutdown();
}

bool RemoteImageCache::IsAllowedHttpsUrl(std::wstring_view url) noexcept {
    try {
        return ParseHttpsUrl(url).has_value();
    } catch (...) {
        return false;
    }
}

bool RemoteImageCache::IsAllowedImageSource(std::wstring_view source) noexcept {
    try {
        return ParseHttpsUrl(source).has_value() || ParseInlinePng(source).has_value();
    } catch (...) {
        return false;
    }
}

RemoteImageRequestResult RemoteImageCache::QueueLocked(std::wstring url, bool retry) {
    if (shuttingDown_) return RemoteImageRequestResult::ShuttingDown;
    if (const auto found = entries_.find(url); found != entries_.end()) {
        found->second.lastUse = ++useCounter_;
        if (!retry || found->second.state != RemoteImageState::Failed)
            return RemoteImageRequestResult::AlreadyTracked;
        if (PendingCountLocked() >=
            std::min(limits_.maximumPendingEntries, limits_.maximumEntries)) {
            ++pendingCapacityRejections_;
            return RemoteImageRequestResult::CapacityExceeded;
        }
        found->second.state = RemoteImageState::Queued;
        found->second.error.clear();
        queue_.push_back(std::move(url));
        condition_.notify_one();
        return RemoteImageRequestResult::Queued;
    }
    if (PendingCountLocked() >=
        std::min(limits_.maximumPendingEntries, limits_.maximumEntries)) {
        ++pendingCapacityRejections_;
        return RemoteImageRequestResult::CapacityExceeded;
    }
    while (entries_.size() >= limits_.maximumEntries) {
        if (!EvictOneLocked(url, EvictionReason::CountPressure)) {
            ++countCapacityRejections_;
            return RemoteImageRequestResult::CapacityExceeded;
        }
    }
    entries_.emplace(url, Entry{RemoteImageState::Queued, {}, {}, {}, {}, {}, ++useCounter_});
    queue_.push_back(std::move(url));
    condition_.notify_one();
    return RemoteImageRequestResult::Queued;
}

std::size_t RemoteImageCache::PendingCountLocked() const noexcept {
    return static_cast<std::size_t>(std::count_if(
        entries_.begin(), entries_.end(), [](const auto& item) {
            return item.second.state == RemoteImageState::Queued ||
                item.second.state == RemoteImageState::Loading;
        }));
}

std::size_t RemoteImageCache::ReadyCountLocked() const noexcept {
    return static_cast<std::size_t>(std::count_if(
        entries_.begin(), entries_.end(), [](const auto& item) {
            return item.second.state == RemoteImageState::Ready;
        }));
}

bool RemoteImageCache::EvictOneLocked(
    const std::wstring_view protectedUrl,
    const EvictionReason reason,
    const bool readyOnly) {
    auto candidate = entries_.end();
    for (auto iterator = entries_.begin(); iterator != entries_.end(); ++iterator) {
        if (iterator->first == protectedUrl ||
            iterator->second.state == RemoteImageState::Queued ||
            iterator->second.state == RemoteImageState::Loading) continue;
        if (readyOnly && iterator->second.state != RemoteImageState::Ready) continue;
        if (candidate == entries_.end() || iterator->second.lastUse < candidate->second.lastUse)
            candidate = iterator;
    }
    if (candidate == entries_.end()) return false;
    if (candidate->second.image)
        decodedBytes_ -= candidate->second.image->premultipliedBgra.size();
    entries_.erase(candidate);
    ++evictions_;
    if (reason == EvictionReason::BytePressure) ++bytePressureEvictions_;
    else ++countPressureEvictions_;
    return true;
}

void RemoteImageCache::WorkerLoop(std::stop_token stopToken) {
    while (!stopToken.stop_requested()) {
        std::wstring url;
        {
            std::unique_lock lock(mutex_);
            condition_.wait(lock, [this, &stopToken] {
                return shuttingDown_ || stopToken.stop_requested() || !queue_.empty();
            });
            if (shuttingDown_ || stopToken.stop_requested()) break;
            url = std::move(queue_.front());
            queue_.pop_front();
            const auto found = entries_.find(url);
            if (found == entries_.end() || found->second.state != RemoteImageState::Queued)
                continue;
            found->second.state = RemoteImageState::Loading;
        }

        std::wstring source = url;
        std::vector<std::uint8_t> encodedArtwork;
        std::wstring encodedArtworkMime;
        {
            std::scoped_lock lock(mutex_);
            const auto found = entries_.find(url);
            if (found != entries_.end() && !found->second.pendingSource.empty()) {
                source = std::move(found->second.pendingSource);
                found->second.pendingSource.clear();
            }
            if (found != entries_.end() && !found->second.pendingBytes.empty()) {
                encodedArtwork = std::move(found->second.pendingBytes);
                encodedArtworkMime = std::move(found->second.pendingMimeType);
                encodedArtworkBytes_ -= encodedArtwork.size();
            }
        }
        RemoteImageFetchResult result;
        if (!encodedArtwork.empty()) {
            if (usesCustomFetch_) {
                source = L"data:" + encodedArtworkMime + L";base64,validated";
                result = fetch_(source, stopToken, limits_);
            } else {
                result = artworkDecoder_->Decode(
                    std::move(encodedArtwork), std::move(encodedArtworkMime), stopToken);
            }
            if (stopToken.stop_requested())
                result = Failure(E_ABORT, L"Trusted artwork decode was cancelled.");
            else if (result.succeeded() &&
                (result.image.width > limits_.maximumArtworkDimension ||
                 result.image.height > limits_.maximumArtworkDimension ||
                 static_cast<std::uint64_t>(result.image.width) * result.image.height >
                    limits_.maximumArtworkPixels))
                result = Failure(HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE),
                    L"Trusted artwork dimensions exceed the allowed bound.");
        } else {
            result = fetch_(source, stopToken, limits_);
        }
        RemoteImageState finalState = RemoteImageState::Failed;
        std::shared_ptr<const RemoteDecodedImage> diagnosticImage;
        std::uint64_t diagnosticResourceHash{};
        std::uint64_t diagnosticHandleHash{};
        {
            std::scoped_lock lock(mutex_);
            if (shuttingDown_ || stopToken.stop_requested()) break;
            CompleteLocked(url, std::move(result));
            const auto found = entries_.find(url);
            if (found != entries_.end()) {
                finalState = found->second.state;
                constexpr std::wstring_view prefix = L"wrail-artwork\x1f";
                const auto handleStart = url.rfind(L'\x1f');
                if (artworkDecodeDiagnostic_ &&
                    finalState == RemoteImageState::Ready &&
                    found->second.image && url.starts_with(prefix) &&
                    handleStart != std::wstring::npos &&
                    handleStart + 1 < url.size()) {
                    const auto resourceHash = OpaqueDiagnosticHash(url);
                    const bool seen = std::find(
                        artworkDiagnosticKeys_.begin(),
                        artworkDiagnosticKeys_.begin() +
                            static_cast<std::ptrdiff_t>(artworkDiagnosticKeyCount_),
                        resourceHash) != artworkDiagnosticKeys_.begin() +
                            static_cast<std::ptrdiff_t>(artworkDiagnosticKeyCount_);
                    if (!seen && artworkDiagnosticKeyCount_ <
                            maximumArtworkDiagnosticRecords_) {
                        artworkDiagnosticKeys_[artworkDiagnosticKeyCount_++] = resourceHash;
                        diagnosticImage = found->second.image;
                        diagnosticResourceHash = resourceHash;
                        diagnosticHandleHash = OpaqueDiagnosticHash(
                            std::wstring_view(url).substr(handleStart + 1));
                    }
                }
            }
        }
        if (diagnosticImage && artworkDecodeDiagnostic_ &&
            !stopToken.stop_requested()) {
            bool visibleAlpha = false;
            for (std::size_t index = 3;
                 index < diagnosticImage->premultipliedBgra.size();
                 index += 4) {
                if (diagnosticImage->premultipliedBgra[index] != 0) {
                    visibleAlpha = true;
                    break;
                }
            }
            const auto contentType = diagnosticImage->mimeType == L"image/jpeg"
                ? TrustedArtworkContentType::Jpeg
                : diagnosticImage->mimeType == L"image/png"
                ? TrustedArtworkContentType::Png
                : diagnosticImage->mimeType == L"image/webp"
                ? TrustedArtworkContentType::WebP
                : TrustedArtworkContentType::Unknown;
            try {
                artworkDecodeDiagnostic_({
                    diagnosticResourceHash,
                    diagnosticHandleHash,
                    contentType,
                    diagnosticImage->width,
                    diagnosticImage->height,
                    visibleAlpha,
                });
            } catch (...) {
                // Diagnostic callbacks never own cache-worker lifetime.
            }
        }
        if (completion_ && !stopToken.stop_requested()) {
            try { completion_(url, finalState); }
            catch (...) { /* Client callbacks cannot terminate the cache worker. */ }
        }
    }
}

void RemoteImageCache::ArtworkDemandLoop(std::stop_token stopToken) {
    while (!stopToken.stop_requested()) {
        ArtworkDemand demand;
        {
            std::unique_lock lock(mutex_);
            artworkDemandCondition_.wait(lock, [this, &stopToken] {
                return shuttingDown_ || stopToken.stop_requested() ||
                    !artworkDemandQueue_.empty();
            });
            if (shuttingDown_ || stopToken.stop_requested()) break;
            demand = std::move(artworkDemandQueue_.front());
            artworkDemandQueue_.pop_front();
            const auto found = entries_.find(demand.key);
            if (found == entries_.end() ||
                found->second.state != RemoteImageState::Loading ||
                found->second.demandGeneration != demand.generation ||
                !found->second.demandAuthority ||
                *found->second.demandAuthority != demand.authority ||
                !found->second.pendingBytes.empty()) {
                continue;
            }
        }

        auto disposition = TrustedArtworkRequestDisposition::TerminalFailure;
        try {
            disposition = artworkRequest_(demand.key, demand.authority, stopToken);
        } catch (...) {
            disposition = TrustedArtworkRequestDisposition::TerminalFailure;
        }
        if (stopToken.stop_requested()) break;
        if (disposition == TrustedArtworkRequestDisposition::Accepted) continue;
        CompleteArtworkDemand(demand, disposition);
    }
}

void RemoteImageCache::CompleteArtworkDemand(
    const ArtworkDemand& demand,
    const TrustedArtworkRequestDisposition disposition) {
    CompletionCallback completion;
    RemoteImageState state{};
    {
        std::scoped_lock lock(mutex_);
        if (shuttingDown_) return;
        const auto found = entries_.find(demand.key);
        if (found == entries_.end() ||
            found->second.state != RemoteImageState::Loading ||
            found->second.demandGeneration != demand.generation ||
            !found->second.demandAuthority ||
            *found->second.demandAuthority != demand.authority) {
            return;
        }
        if (disposition == TrustedArtworkRequestDisposition::OriginRetired) {
            entries_.erase(found);
            state = RemoteImageState::Missing;
        } else {
            found->second.state = RemoteImageState::Failed;
            found->second.error = L"Trusted artwork is unavailable.";
            state = RemoteImageState::Failed;
        }
        completion = completion_;
    }
    if (completion) {
        try { completion(demand.key, state); }
        catch (...) { }
    }
}

void RemoteImageCache::CompleteLocked(const std::wstring& url, RemoteImageFetchResult result) {
    const auto found = entries_.find(url);
    if (found == entries_.end()) return;
    auto& entry = found->second;
    entry.lastUse = ++useCounter_;
    const std::uint64_t expectedStride = static_cast<std::uint64_t>(result.image.width) * 4U;
    const std::uint64_t expectedBytes = expectedStride * result.image.height;
    if (result.succeeded() &&
        (result.image.width == 0 || result.image.height == 0 ||
         result.image.stride != expectedStride ||
         expectedBytes != result.image.premultipliedBgra.size() ||
         expectedBytes > limits_.maximumDecodedImageBytes)) {
        result = Failure(E_INVALIDARG, L"Fetcher returned invalid decoded image data.");
    }
    if (result.succeeded()) {
        const auto bytes = result.image.premultipliedBgra.size();
        while (ReadyCountLocked() >=
               std::min(limits_.maximumReadyEntries, limits_.maximumEntries)) {
            if (!EvictOneLocked(url, EvictionReason::CountPressure, true)) {
                result = Failure(HRESULT_FROM_WIN32(ERROR_NOT_ENOUGH_MEMORY),
                                 L"Ready image exceeds the cache entry budget.");
                break;
            }
        }
        while (result.succeeded() &&
               decodedBytes_ + bytes > limits_.maximumDecodedBytes) {
            if (!EvictOneLocked(url, EvictionReason::BytePressure, true)) {
                result = Failure(HRESULT_FROM_WIN32(ERROR_NOT_ENOUGH_MEMORY),
                                 L"Decoded image exceeds the available cache budget.");
                break;
            }
        }
        if (result.succeeded()) {
            entry.image = std::make_shared<RemoteDecodedImage>(std::move(result.image));
            entry.error.clear();
            entry.state = RemoteImageState::Ready;
            decodedBytes_ += bytes;
            return;
        }
    }
    entry.image.reset();
    entry.error = result.error.empty() ? L"Remote image request failed." : std::move(result.error);
    entry.state = RemoteImageState::Failed;
}

RemoteImageFetchResult RemoteImageCache::FetchAndDecodeSource(
    std::wstring_view url,
    std::stop_token stopToken,
    const RemoteImageLimits& limits) {
    if (auto inlinePng = ParseInlinePng(url)) {
        if (stopToken.stop_requested()) return Failure(E_ABORT, L"Image request was cancelled.");
        auto decoded = DecodeWithWic(std::move(*inlinePng), L"image/png", limits);
        if (decoded.succeeded() &&
            (decoded.image.width > maximumInlinePngDimension ||
             decoded.image.height > maximumInlinePngDimension))
            return Failure(E_INVALIDARG, L"Inline PNG dimensions exceed the allowed bound.");
        return decoded;
    }
    const auto parsed = ParseHttpsUrl(url);
    if (!parsed) return Failure(E_INVALIDARG, L"Only credential-free HTTPS URLs are allowed.");

    InternetHandle session(WinHttpOpen(
        L"WidgetRail/0.1",
        WINHTTP_ACCESS_TYPE_NO_PROXY,
        WINHTTP_NO_PROXY_NAME,
        WINHTTP_NO_PROXY_BYPASS,
        0));
    if (!session) return Failure(LastErrorResult(), L"WinHTTP session creation failed.");
    if (!WinHttpSetTimeouts(session.get(),
            static_cast<int>(limits.resolveTimeoutMilliseconds),
            static_cast<int>(limits.connectTimeoutMilliseconds),
            static_cast<int>(limits.sendTimeoutMilliseconds),
            static_cast<int>(limits.receiveTimeoutMilliseconds))) {
        return Failure(LastErrorResult(), L"WinHTTP timeout policy failed.");
    }
    DWORD secureProtocols = WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_2;
#ifdef WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_3
    secureProtocols |= WINHTTP_FLAG_SECURE_PROTOCOL_TLS1_3;
#endif
    if (!WinHttpSetOption(session.get(), WINHTTP_OPTION_SECURE_PROTOCOLS,
                          &secureProtocols, sizeof(secureProtocols))) {
        return Failure(LastErrorResult(), L"WinHTTP TLS policy failed.");
    }
    DWORD redirectPolicy = WINHTTP_OPTION_REDIRECT_POLICY_DISALLOW_HTTPS_TO_HTTP;
    if (!WinHttpSetOption(session.get(), WINHTTP_OPTION_REDIRECT_POLICY,
                          &redirectPolicy, sizeof(redirectPolicy))) {
        return Failure(LastErrorResult(), L"WinHTTP redirect policy failed.");
    }
    DWORD redirects = limits.maximumRedirects;
    if (!WinHttpSetOption(session.get(), WINHTTP_OPTION_MAX_HTTP_AUTOMATIC_REDIRECTS,
                          &redirects, sizeof(redirects))) {
        return Failure(LastErrorResult(), L"WinHTTP redirect limit failed.");
    }

    InternetHandle connection(WinHttpConnect(session.get(), parsed->host.c_str(), parsed->port, 0));
    if (!connection) return Failure(LastErrorResult(), L"WinHTTP connection creation failed.");
    InternetHandle request(WinHttpOpenRequest(
        connection.get(), L"GET", parsed->resource.c_str(), nullptr,
        WINHTTP_NO_REFERER, WINHTTP_DEFAULT_ACCEPT_TYPES, WINHTTP_FLAG_SECURE));
    if (!request) return Failure(LastErrorResult(), L"WinHTTP request creation failed.");
    constexpr wchar_t headers[] = L"Accept: image/*\r\nCache-Control: no-transform\r\n";
    if (!WinHttpSendRequest(request.get(), headers, static_cast<DWORD>(-1),
                            WINHTTP_NO_REQUEST_DATA, 0, 0, 0) ||
        !WinHttpReceiveResponse(request.get(), nullptr)) {
        return Failure(LastErrorResult(), L"HTTPS image request failed.");
    }
    if (stopToken.stop_requested()) return Failure(E_ABORT, L"Image request was cancelled.");

    DWORD status = 0;
    DWORD statusBytes = sizeof(status);
    if (!WinHttpQueryHeaders(request.get(), WINHTTP_QUERY_STATUS_CODE | WINHTTP_QUERY_FLAG_NUMBER,
                             WINHTTP_HEADER_NAME_BY_INDEX, &status, &statusBytes,
                             WINHTTP_NO_HEADER_INDEX) || status < 200 || status >= 300) {
        return Failure(HRESULT_FROM_WIN32(ERROR_WINHTTP_INVALID_SERVER_RESPONSE),
                       L"Image endpoint did not return a successful status.");
    }

    DWORD finalUrlBytes = 0;
    WinHttpQueryOption(request.get(), WINHTTP_OPTION_URL, nullptr, &finalUrlBytes);
    std::vector<wchar_t> finalUrl(finalUrlBytes / sizeof(wchar_t) + 1);
    if (finalUrlBytes == 0 ||
        !WinHttpQueryOption(request.get(), WINHTTP_OPTION_URL, finalUrl.data(), &finalUrlBytes) ||
        !IsAllowedHttpsUrl(finalUrl.data())) {
        return Failure(E_ACCESSDENIED, L"Redirect target was not an allowed HTTPS URL.");
    }

    std::wstring mime = QueryHeader(request.get(), WINHTTP_QUERY_CONTENT_TYPE);
    if (!IsImageMime(mime))
        return Failure(HRESULT_FROM_WIN32(ERROR_INVALID_DATA), L"Response MIME type is not an image.");
    const std::wstring contentLength = QueryHeader(request.get(), WINHTTP_QUERY_CONTENT_LENGTH);
    if (!contentLength.empty()) {
        wchar_t* end = nullptr;
        const unsigned long long announced = std::wcstoull(contentLength.c_str(), &end, 10);
        if (end == contentLength.c_str() || announced > limits.maximumDownloadBytes)
            return Failure(HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE), L"Image response is too large.");
    }

    std::vector<std::uint8_t> bytes;
    for (;;) {
        if (stopToken.stop_requested()) return Failure(E_ABORT, L"Image request was cancelled.");
        DWORD available = 0;
        if (!WinHttpQueryDataAvailable(request.get(), &available))
            return Failure(LastErrorResult(), L"Image response read failed.");
        if (available == 0) break;
        if (bytes.size() + available > limits.maximumDownloadBytes)
            return Failure(HRESULT_FROM_WIN32(ERROR_FILE_TOO_LARGE), L"Image response exceeded 5 MiB.");
        const auto offset = bytes.size();
        bytes.resize(offset + available);
        DWORD read = 0;
        if (!WinHttpReadData(request.get(), bytes.data() + offset, available, &read))
            return Failure(LastErrorResult(), L"Image response read failed.");
        bytes.resize(offset + read);
    }
    if (bytes.empty()) return Failure(HRESULT_FROM_WIN32(ERROR_INVALID_DATA), L"Image response was empty.");
    return DecodeWithWic(std::move(bytes), std::move(mime), limits);
}

} // namespace widgetrail
