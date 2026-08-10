#pragma once

#include <Windows.h>

#include <filesystem>
#include <functional>
#include <string>
#include <string_view>
#include <utility>
#include <vector>

namespace gba::host_testing {

class Handle final {
public:
    Handle() = default;
    explicit Handle(HANDLE value) noexcept : value_(value) {}
    ~Handle() { Reset(); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
    Handle(Handle&& other) noexcept : value_(std::exchange(other.value_, nullptr)) {}
    Handle& operator=(Handle&& other) noexcept {
        if (this != &other) Reset(std::exchange(other.value_, nullptr));
        return *this;
    }
    [[nodiscard]] HANDLE Get() const noexcept { return value_; }
    [[nodiscard]] explicit operator bool() const noexcept {
        return value_ && value_ != INVALID_HANDLE_VALUE;
    }
    void Reset(HANDLE value = nullptr) noexcept;

private:
    HANDLE value_{};
};

[[noreturn]] void Fail(const std::string& message);
void Require(bool condition, const std::string& message);
[[nodiscard]] std::string Win32Error(std::string_view operation);

template <typename Predicate>
bool WaitUntil(const DWORD timeoutMilliseconds, Predicate&& predicate) {
    constexpr DWORD pollMilliseconds = 25;
    const ULONGLONG deadline = GetTickCount64() + timeoutMilliseconds;
    do {
        if (std::invoke(predicate)) return true;
        Sleep(pollMilliseconds);
    } while (GetTickCount64() < deadline);
    return std::invoke(predicate);
}

[[nodiscard]] std::wstring QuoteArgument(std::wstring_view argument);
[[nodiscard]] std::string WideToUtf8(std::wstring_view value);
[[nodiscard]] std::string JsonEscape(std::wstring_view value);
void WriteUtf8(const std::filesystem::path& path, std::string_view contents);
[[nodiscard]] std::string ReadUtf8(const std::filesystem::path& path);
[[nodiscard]] std::vector<wchar_t> ChildEnvironment(
    const std::filesystem::path& localAppData);

class HostProcess final {
public:
    HostProcess(
        const std::filesystem::path& installation,
        const std::filesystem::path& localAppData,
        std::wstring_view arguments = L"--show");
    ~HostProcess();
    HostProcess(const HostProcess&) = delete;
    HostProcess& operator=(const HostProcess&) = delete;
    [[nodiscard]] DWORD Id() const noexcept { return processId_; }
    [[nodiscard]] HANDLE Process() const noexcept { return process_.Get(); }

private:
    Handle job_;
    Handle process_;
    Handle thread_;
    DWORD processId_{};
};

[[nodiscard]] HWND LocateHostWindow(
    DWORD processId,
    const wchar_t* windowClass = L"GameBarAlternative.OverlayHost");
void PostKey(HWND window, WPARAM virtualKey);
void SendKey(HWND window, WPARAM virtualKey);
void SendKeyDownAndPostRelease(HWND window, WPARAM virtualKey);

} // namespace gba::host_testing
