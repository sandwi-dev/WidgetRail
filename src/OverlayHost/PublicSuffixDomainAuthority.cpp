#include "PublicSuffixDomainAuthority.h"

#include <Windows.h>
#include <bcrypt.h>

#include <algorithm>
#include <array>
#include <fstream>
#include <iterator>

namespace widgetrail {
namespace {

constexpr std::array<unsigned char, 32> kExpectedSha256{
    0x14,0xEF,0x61,0xB1,0xC2,0x12,0xF7,0x01,0xF3,0x63,0x6C,0x1D,0x01,0xAB,0x92,0x54,
    0xDA,0xF8,0x41,0xF5,0x7E,0xB6,0x43,0x3B,0xCB,0xBE,0xF5,0x6C,0x72,0x6C,0xA6,0x56};
constexpr std::string_view kExpectedVersion{"// VERSION: 2026-08-19_19-18-48_UTC"};
constexpr std::string_view kExpectedCommit{"// COMMIT: e8c9a2b2b2856b6449999dd0ec0d118f364ed0cd"};

bool HasExpectedDigest(const std::vector<unsigned char>& bytes) noexcept {
    BCRYPT_ALG_HANDLE algorithm{};
    BCRYPT_HASH_HANDLE hash{};
    std::array<unsigned char, 32> digest{};
    bool valid = false;
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) >= 0) {
        DWORD objectSize{};
        DWORD resultSize{};
        if (BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH,
                reinterpret_cast<PUCHAR>(&objectSize), sizeof(objectSize),
                &resultSize, 0) >= 0) {
            std::vector<unsigned char> object(objectSize);
            if (BCryptCreateHash(algorithm, &hash, object.data(), objectSize,
                    nullptr, 0, 0) >= 0 &&
                BCryptHashData(hash, const_cast<PUCHAR>(bytes.data()),
                    static_cast<ULONG>(bytes.size()), 0) >= 0 &&
                BCryptFinishHash(hash, digest.data(),
                    static_cast<ULONG>(digest.size()), 0) >= 0)
                valid = digest == kExpectedSha256;
        }
    }
    if (hash) BCryptDestroyHash(hash);
    if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
    return valid;
}

std::optional<std::wstring> Utf8ToAsciiDns(const std::string_view value) noexcept {
    if (value.empty() || value.size() > 1024) return std::nullopt;
    const int wideLength = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS,
        value.data(), static_cast<int>(value.size()), nullptr, 0);
    if (wideLength <= 0) return std::nullopt;
    std::wstring wide(static_cast<std::size_t>(wideLength), L'\0');
    if (MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, value.data(),
            static_cast<int>(value.size()), wide.data(), wideLength) != wideLength)
        return std::nullopt;
    const int asciiLength = IdnToAscii(IDN_USE_STD3_ASCII_RULES, wide.data(),
        wideLength, nullptr, 0);
    if (asciiLength <= 0) return std::nullopt;
    std::wstring ascii(static_cast<std::size_t>(asciiLength), L'\0');
    if (IdnToAscii(IDN_USE_STD3_ASCII_RULES, wide.data(), wideLength,
            ascii.data(), asciiLength) != asciiLength) return std::nullopt;
    std::transform(ascii.begin(), ascii.end(), ascii.begin(), towlower);
    return ascii;
}

std::size_t LabelCount(const std::wstring_view value) noexcept {
    return value.empty() ? 0 : 1 + static_cast<std::size_t>(
        std::count(value.begin(), value.end(), L'.'));
}

bool SuffixMatch(const std::wstring_view domain,
                 const std::wstring_view suffix) noexcept {
    return domain == suffix ||
        (domain.size() > suffix.size() && domain.ends_with(suffix) &&
         domain[domain.size() - suffix.size() - 1] == L'.');
}

} // namespace

PublicSuffixDomainAuthority PublicSuffixDomainAuthority::LoadDefault() noexcept {
    std::array<wchar_t, 32768> modulePath{};
    const DWORD length = GetModuleFileNameW(nullptr, modulePath.data(),
        static_cast<DWORD>(modulePath.size()));
    if (length == 0 || length >= modulePath.size()) return {};
    return Load(std::filesystem::path(modulePath.data()).parent_path() /
        L"public_suffix_list.dat");
}

PublicSuffixDomainAuthority PublicSuffixDomainAuthority::Load(
    const std::filesystem::path& path) noexcept {
    PublicSuffixDomainAuthority authority;
    try {
        std::ifstream input(path, std::ios::binary);
        if (!input) return authority;
        std::vector<unsigned char> bytes(
            std::istreambuf_iterator<char>(input), {});
        if (bytes.size() != 333164 || !HasExpectedDigest(bytes)) return authority;
        const std::string text(bytes.begin(), bytes.end());
        if (text.find(kExpectedVersion) == std::string::npos ||
            text.find(kExpectedCommit) == std::string::npos) return authority;
        std::size_t cursor{};
        while (cursor < text.size()) {
            const auto end = text.find('\n', cursor);
            std::string_view line{text.data() + cursor,
                (end == std::string::npos ? text.size() : end) - cursor};
            if (!line.empty() && line.back() == '\r') line.remove_suffix(1);
            cursor = end == std::string::npos ? text.size() : end + 1;
            if (line.empty() || line.starts_with("//")) continue;
            auto* destination = &authority.exact_;
            if (line.front() == '!') {
                destination = &authority.exception_;
                line.remove_prefix(1);
            } else if (line.starts_with("*.")) {
                destination = &authority.wildcard_;
                line.remove_prefix(2);
            }
            const auto rule = Utf8ToAsciiDns(line);
            if (!rule || rule->empty()) return {};
            destination->insert(*rule);
        }
        if (authority.exact_.size() < 5000 || authority.wildcard_.empty() ||
            authority.exception_.empty()) return {};
        authority.available_ = true;
    } catch (...) {
        return {};
    }
    return authority;
}

std::optional<std::wstring> PublicSuffixDomainAuthority::CanonicalDnsName(
    const std::wstring_view value) noexcept {
    if (value.empty() || value.size() > 253 || value.front() == L'.' ||
        value.back() == L'.') return std::nullopt;
    std::wstring canonical;
    canonical.reserve(value.size());
    std::size_t labelLength{};
    bool leadingHyphen{};
    for (const wchar_t character : value) {
        if (character == L'.') {
            if (labelLength == 0 || labelLength > 63 || leadingHyphen ||
                canonical.back() == L'-') return std::nullopt;
            canonical.push_back(character);
            labelLength = 0;
            leadingHyphen = false;
            continue;
        }
        wchar_t lowered = character;
        if (lowered >= L'A' && lowered <= L'Z') lowered += L'a' - L'A';
        if (!((lowered >= L'a' && lowered <= L'z') ||
              (lowered >= L'0' && lowered <= L'9') || lowered == L'-'))
            return std::nullopt;
        if (labelLength == 0) leadingHyphen = lowered == L'-';
        ++labelLength;
        canonical.push_back(lowered);
    }
    if (labelLength == 0 || labelLength > 63 || leadingHyphen ||
        canonical.back() == L'-') return std::nullopt;
    return canonical;
}

std::size_t PublicSuffixDomainAuthority::PublicSuffixLabelCount(
    const std::wstring_view domain) const noexcept {
    std::size_t best{1};
    for (std::size_t offset{}; offset < domain.size();) {
        const auto suffix = domain.substr(offset);
        if (exception_.contains(std::wstring{suffix}))
            return LabelCount(suffix) - 1;
        if (exact_.contains(std::wstring{suffix}))
            best = std::max(best, LabelCount(suffix));
        const auto dot = domain.find(L'.', offset);
        if (dot == std::wstring_view::npos) break;
        const auto base = domain.substr(dot + 1);
        if (wildcard_.contains(std::wstring{base}))
            best = std::max(best, LabelCount(base) + 1);
        offset = dot + 1;
    }
    return best;
}

bool PublicSuffixDomainAuthority::IsRegistrableDomain(
    const std::wstring_view domain) const noexcept {
    if (!available_) return false;
    const auto canonical = CanonicalDnsName(domain);
    return canonical && *canonical == domain &&
        LabelCount(*canonical) == PublicSuffixLabelCount(*canonical) + 1;
}

bool PublicSuffixDomainAuthority::AllowsHttpsUri(
    const std::wstring_view uri,
    const std::vector<std::wstring>& families) const noexcept {
    if (!available_ || !uri.starts_with(L"https://")) return false;
    const auto authorityEnd = uri.find_first_of(L"/?#", 8);
    const auto authority = uri.substr(8,
        authorityEnd == std::wstring_view::npos ? uri.size() - 8 : authorityEnd - 8);
    if (authority.empty() || authority.find_first_of(L"@:") != std::wstring_view::npos)
        return false;
    const auto host = CanonicalDnsName(authority);
    if (!host) return false;
    return std::any_of(families.begin(), families.end(), [&](const auto& family) {
        return IsRegistrableDomain(family) && SuffixMatch(*host, family);
    });
}

} // namespace widgetrail
