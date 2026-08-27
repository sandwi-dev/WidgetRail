#pragma once

#include <filesystem>
#include <optional>
#include <string>
#include <string_view>
#include <unordered_set>
#include <vector>

namespace widgetrail {

// Immutable, integrity-checked authority derived from the checked-in Public
// Suffix List snapshot. Missing or corrupt data produces an unavailable
// authority and therefore rejects every domain-family declaration.
class PublicSuffixDomainAuthority final {
public:
    [[nodiscard]] static PublicSuffixDomainAuthority LoadDefault() noexcept;
    [[nodiscard]] static PublicSuffixDomainAuthority Load(
        const std::filesystem::path& path) noexcept;

    [[nodiscard]] bool available() const noexcept { return available_; }
    [[nodiscard]] bool IsRegistrableDomain(std::wstring_view domain) const noexcept;
    [[nodiscard]] bool AllowsHttpsUri(
        std::wstring_view uri,
        const std::vector<std::wstring>& families) const noexcept;

private:
    [[nodiscard]] static std::optional<std::wstring> CanonicalDnsName(
        std::wstring_view value) noexcept;
    [[nodiscard]] std::size_t PublicSuffixLabelCount(
        std::wstring_view domain) const noexcept;

    bool available_{};
    std::unordered_set<std::wstring> exact_;
    std::unordered_set<std::wstring> wildcard_;
    std::unordered_set<std::wstring> exception_;
};

} // namespace widgetrail
