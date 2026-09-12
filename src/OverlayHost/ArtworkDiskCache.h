#pragma once
#include <windows.h>
#include <bcrypt.h>
#pragma comment(lib, "bcrypt.lib")
#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <filesystem>
#include <cstring>
#include <cstddef>
#include <cwctype>
#include <stdexcept>
#include <fstream>
#include <mutex>
#include <limits>
#include <optional>
#include <string>
#include <vector>

namespace widgetrail {
// Optional storage used only on artwork worker threads. All disk errors are misses.
class ArtworkDiskCache final {
public:
    struct Value { std::vector<std::uint8_t> bytes; std::wstring mime; };
    static constexpr std::uint64_t MaximumBytes = 256ULL * 1024 * 1024;
    static constexpr std::size_t MaximumFiles = 4096;
    static constexpr std::size_t MaximumImageBytes = 5U * 1024 * 1024;
    explicit ArtworkDiskCache(std::filesystem::path root,
        std::uint64_t maximumBytes = MaximumBytes, std::size_t maximumFiles = MaximumFiles)
        : root_(std::move(root)), maximumBytes_(maximumBytes), maximumFiles_(maximumFiles) {}

    static std::filesystem::path DefaultRoot() noexcept {
        try {
            wchar_t root[32768]{};
            const auto size = GetEnvironmentVariableW(L"LOCALAPPDATA", root, 32768);
            if (!size || size >= 32768) return {};
            return std::filesystem::path(root) / L"WidgetRail" / L"cache" / L"artwork-v1";
        } catch (...) { return {}; }
    }
    static std::uint64_t Now() noexcept {
        return static_cast<std::uint64_t>(std::chrono::duration_cast<std::chrono::seconds>(
            std::chrono::system_clock::now().time_since_epoch()).count());
    }
    // Conservative HTTP freshness: no heuristic caching, private/no-cache/no-store
    // responses are never persisted. Unsupported Vary responses are also excluded.
    static std::optional<std::uint64_t> FreshSeconds(std::wstring control,
        std::wstring vary, const std::uint64_t age = 0) noexcept {
        try {
            std::transform(control.begin(), control.end(), control.begin(), towlower);
            std::transform(vary.begin(), vary.end(), vary.begin(), towlower);
            if (control.find(L"no-store") != control.npos || control.find(L"no-cache") != control.npos ||
                control.find(L"private") != control.npos || (!vary.empty() && vary != L"accept")) return {};
            std::optional<std::uint64_t> lifetime;
            std::size_t start{};
            while (start < control.size()) {
                const auto end = control.find(L',', start);
                auto token = control.substr(start, end == control.npos ? control.size() - start : end - start);
                const auto first = token.find_first_not_of(L" \t");
                if (first != token.npos) token.erase(0, first);
                if (token.starts_with(L"max-age=")) {
                    auto number = token.substr(8);
                    if (!number.empty() && number.front() == L'"' && number.back() == L'"')
                        number = number.substr(1, number.size() - 2);
                    const auto last = number.find_last_not_of(L" \t");
                    if (last != number.npos) number.resize(last + 1);
                    if (number.empty() || number.find_first_not_of(L"0123456789") != number.npos || lifetime) return {};
                    lifetime = std::stoull(number);
                }
                if (end == control.npos) break;
                start = end + 1;
            }
            if (!lifetime || *lifetime <= age) return {};
            return std::min<std::uint64_t>(*lifetime - age, 30ULL * 24 * 60 * 60);
        } catch (...) { return {}; }
    }
    std::optional<Value> Read(std::wstring_view url, std::uint64_t now = Now()) noexcept {
        try {
            std::scoped_lock lock(gate_);
            if (root_.empty()) return {};
            const auto path = Path(url);
            std::ifstream stream(path, std::ios::binary | std::ios::ate);
            if (!stream) return {};
            const auto length = stream.tellg();
            if (length < static_cast<std::streamoff>(sizeof(Header)) ||
                length > static_cast<std::streamoff>(sizeof(Header) + 256 + MaximumImageBytes)) { Remove(path); return {}; }
            stream.seekg(0);
            Header header{};
            stream.read(reinterpret_cast<char*>(&header), sizeof(header));
            if (!stream || header.magic != Magic || header.expiry <= now ||
                !header.bytes || header.bytes > MaximumImageBytes || !header.mimeChars || header.mimeChars > 128 ||
                static_cast<std::uint64_t>(length) != sizeof(Header) + header.mimeChars * sizeof(wchar_t) + header.bytes) {
                stream.close(); Remove(path); return {};
            }
            Value value;
            value.mime.resize(header.mimeChars); value.bytes.resize(header.bytes);
            stream.read(reinterpret_cast<char*>(value.mime.data()), header.mimeChars * sizeof(wchar_t));
            stream.read(reinterpret_cast<char*>(value.bytes.data()), header.bytes);
            if (!stream || ContentDigest(header, value.mime, value.bytes) != header.digest) {
                stream.close(); Remove(path); return {};
            }
            stream.close();
            std::error_code error;
            std::filesystem::last_write_time(path, std::filesystem::file_time_type::clock::now(), error);
            return value;
        } catch (...) { return {}; }
    }
    bool Write(std::wstring_view url, const std::vector<std::uint8_t>& bytes,
        std::wstring_view mime, std::uint64_t lifetime, std::uint64_t now = Now()) noexcept {
        std::filesystem::path temp;
        try {
            std::scoped_lock lock(gate_);
            if (root_.empty() || bytes.empty() || bytes.size() > MaximumImageBytes ||
                mime.empty() || mime.size() > 128 || !lifetime) return false;
            const auto length = sizeof(Header) + mime.size() * sizeof(wchar_t) + bytes.size();
            if (length > maximumBytes_ || !maximumFiles_) return false;
            std::error_code error;
            std::filesystem::create_directories(root_, error);
            if (error) return false;
            ULARGE_INTEGER available{};
            if (!GetDiskFreeSpaceExW(root_.c_str(), &available, nullptr, nullptr) ||
                available.QuadPart < length + 64ULL * 1024 * 1024) return false;
            const auto path = Path(url);
            // Cross-process writers fail open rather than compete for the budget.
            FileLock fileLock(root_ / L"writer.lock");
            if (!fileLock.valid()) return false;
            if (!Trim(path, length)) return false;
            static std::atomic<std::uint64_t> sequence{};
            temp = path.wstring() + L"." + std::to_wstring(GetCurrentProcessId()) + L"." +
                std::to_wstring(++sequence) + L".tmp";
            Header header{}; header.magic = Magic;
            header.expiry = now + std::min<std::uint64_t>(lifetime, 30ULL * 24 * 60 * 60);
            header.bytes = static_cast<std::uint32_t>(bytes.size());
            header.mimeChars = static_cast<std::uint32_t>(mime.size());
            header.digest = ContentDigest(header, mime, bytes);
            {
                std::ofstream stream(temp, std::ios::binary | std::ios::trunc);
                if (!stream) return false;
                stream.write(reinterpret_cast<const char*>(&header), sizeof(header));
                stream.write(reinterpret_cast<const char*>(mime.data()), static_cast<std::streamsize>(mime.size() * sizeof(wchar_t)));
                stream.write(reinterpret_cast<const char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
                stream.flush();
                if (!stream) { stream.close(); Remove(temp); return false; }
            }
            if (!MoveFileExW(temp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING)) { Remove(temp); return false; }
            return true;
        } catch (...) { if (!temp.empty()) Remove(temp); return false; }
    }
    void Erase(std::wstring_view url) noexcept {
        try { std::scoped_lock lock(gate_); if (!root_.empty()) Remove(Path(url)); } catch (...) {}
    }
private:
    static constexpr std::uint32_t Magic = 0x31435257;
    struct Header { std::uint32_t magic{}; std::uint32_t bytes{}; std::uint64_t expiry{};
        std::uint32_t mimeChars{}; std::array<unsigned char, 32> digest{}; };
    struct FileLock {
        HANDLE handle;
        explicit FileLock(const std::filesystem::path& path) : handle(CreateFileW(path.c_str(),
            GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr)) {}
        ~FileLock() { if (valid()) CloseHandle(handle); }
        bool valid() const { return handle != INVALID_HANDLE_VALUE; }
    };
    static std::array<unsigned char, 32> Digest(const void* data, std::size_t bytes) {
        std::array<unsigned char, 32> digest{};
        if (bytes > (std::numeric_limits<ULONG>::max)() || BCryptHash(BCRYPT_SHA256_ALG_HANDLE, nullptr, 0,
            reinterpret_cast<PUCHAR>(const_cast<void*>(data)), static_cast<ULONG>(bytes),
            digest.data(), static_cast<ULONG>(digest.size())) < 0) throw std::runtime_error("Cache hashing unavailable");
        return digest;
    }
    static std::array<unsigned char, 32> ContentDigest(const Header& header,
        std::wstring_view mime, const std::vector<std::uint8_t>& bytes) {
        const auto prefix = offsetof(Header, digest);
        std::vector<std::uint8_t> data(prefix + mime.size() * sizeof(wchar_t) + bytes.size());
        std::memcpy(data.data(), &header, prefix);
        std::memcpy(data.data() + prefix, mime.data(), mime.size() * sizeof(wchar_t));
        std::memcpy(data.data() + prefix + mime.size() * sizeof(wchar_t), bytes.data(), bytes.size());
        return Digest(data.data(), data.size());
    }
    std::filesystem::path Path(std::wstring_view url) const {
        const auto digest = Digest(url.data(), url.size() * sizeof(wchar_t));
        std::wstring name;
        for (auto byte : digest) { name += L"0123456789abcdef"[byte >> 4]; name += L"0123456789abcdef"[byte & 15]; }
        return root_ / (name + L".art");
    }
    static bool Remove(const std::filesystem::path& path) noexcept {
        std::error_code error; return std::filesystem::remove(path, error);
    }
    bool Trim(const std::filesystem::path& incoming, std::uint64_t bytes) {
        struct File { std::filesystem::path path; std::uint64_t bytes; std::filesystem::file_time_type used; };
        std::vector<File> files;
        std::uint64_t total{};
        for (const auto& item : std::filesystem::directory_iterator(root_)) {
            if (!item.is_regular_file()) continue;
            if (item.path().extension() == L".tmp") { if (!Remove(item.path())) return false; continue; }
            if (item.path().extension() != L".art") continue;
            if (item.path() == incoming) { if (!Remove(item.path())) return false; continue; }
            const auto size = item.file_size(); total += size;
            files.push_back({item.path(), size, item.last_write_time()});
            // Bound startup work for an externally inflated/corrupt directory.
            if (files.size() > MaximumFiles * 2) return false;
        }
        std::sort(files.begin(), files.end(), [](const auto& a, const auto& b) { return a.used < b.used; });
        std::size_t removed{};
        while (total + bytes > maximumBytes_ || files.size() - removed >= maximumFiles_) {
            if (removed == files.size() || !Remove(files[removed].path)) return false;
            total -= files[removed++].bytes;
        }
        return true;
    }
    std::filesystem::path root_;
    std::uint64_t maximumBytes_;
    std::size_t maximumFiles_;
    std::mutex gate_;
};
} // namespace widgetrail
