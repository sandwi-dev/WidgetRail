#pragma once
#include "ControllerIsolationCore.h"
#include "HidHideConfigurationAdapter.h"
#include <windows.h>
#include <filesystem>
#include <string>

namespace widgetrail::isolation {

// Held on the routing/recovery thread across all policy and device effects.
class LocalOwnerLease final {
public:
    LocalOwnerLease() = default;
    LocalOwnerLease(const LocalOwnerLease&) = delete;
    LocalOwnerLease& operator=(const LocalOwnerLease&) = delete;
    ~LocalOwnerLease();
    [[nodiscard]] bool Acquire(std::wstring& diagnostic, DWORD timeout = 2'000);
private:
    HANDLE mutex_{};
    bool owned_{};
};

struct LocalPolicyRecord final {
    HidHideJournal policy;
    std::string encoded;
};

class LocalPolicyEffects {
public:
    virtual ~LocalPolicyEffects() = default;
    virtual bool Read(HidHideSnapshot& snapshot, std::uint32_t& error) = 0;
    virtual bool Apply(const HidHideSnapshot& expected, const HidHideSnapshot& desired,
                       HidHideSnapshot& observed, std::uint32_t& error) = 0;
};

class NativeLocalPolicyEffects final : public LocalPolicyEffects {
public:
    bool Read(HidHideSnapshot& snapshot, std::uint32_t& error) override;
    bool Apply(const HidHideSnapshot& expected, const HidHideSnapshot& desired,
               HidHideSnapshot& observed, std::uint32_t& error) override;
private:
    HidHideConfigurationAdapter adapter_;
};

[[nodiscard]] std::filesystem::path LocalControllerJournalPath();
[[nodiscard]] bool LoadLocalPolicy(const std::filesystem::path& path,
    LocalPolicyRecord& record, bool& found, std::wstring& diagnostic);
[[nodiscard]] bool SaveLocalPolicy(const std::filesystem::path& path,
    const HidHideJournal& policy, LocalPolicyRecord& record, std::wstring& diagnostic);
// Caller holds LocalOwnerLease. Record is compared and held write/delete-exclusive
// throughout recovery; deletion addresses that open file, never a replacement path.
[[nodiscard]] bool RestoreLocalPolicy(const std::filesystem::path& path,
    const LocalPolicyRecord& expected, LocalPolicyEffects& effects, std::wstring& diagnostic);
[[nodiscard]] bool RecoverLocalControllerPolicy(const std::filesystem::path& path,
    LocalPolicyEffects& effects, std::wstring& diagnostic);

// Declared before reader/output so those owners retire before scope cleanup.
class LocalPolicyCleanup final {
public:
    LocalPolicyCleanup(const std::filesystem::path& path, const LocalPolicyRecord& record,
                       LocalPolicyEffects& effects, std::wstring& diagnostic) noexcept;
    ~LocalPolicyCleanup() noexcept;
    [[nodiscard]] bool Finish() noexcept;
private:
    const std::filesystem::path& path_;
    const LocalPolicyRecord& record_;
    LocalPolicyEffects& effects_;
    std::wstring& diagnostic_;
    bool armed_{true};
};
} // namespace widgetrail::isolation
