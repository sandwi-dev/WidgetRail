#pragma once

#include "ControllerIsolationProtocol.h"

#include <windows.h>

#include <cstdint>
#include <optional>
#include <string>

namespace widgetrail::isolation {

[[nodiscard]] std::wstring ControllerIsolationPipeName(
    const RoutingAuthority& authority);
[[nodiscard]] std::wstring ControllerIsolationControlPipeName(
    const RoutingAuthority& authority);

class ControllerIsolationPipeServer final {
public:
    ControllerIsolationPipeServer() noexcept = default;
    ~ControllerIsolationPipeServer();
    ControllerIsolationPipeServer(const ControllerIsolationPipeServer&) = delete;
    ControllerIsolationPipeServer& operator=(
        const ControllerIsolationPipeServer&) = delete;

    [[nodiscard]] bool Open(
        const std::wstring& pipeName,
        std::uint32_t& nativeError) noexcept;
    [[nodiscard]] bool Accept(
        DWORD timeoutMilliseconds,
        std::uint32_t& nativeError,
        HANDLE acceptPostedEvent = nullptr) noexcept;
    [[nodiscard]] bool Receive(
        ControlFrame& frame,
        DWORD timeoutMilliseconds,
        std::uint32_t& nativeError) noexcept;
    [[nodiscard]] bool Reply(
        const ControlFrame& frame,
        std::uint32_t& nativeError) noexcept;
    [[nodiscard]] std::optional<std::uint32_t> clientProcessId() const noexcept;
    void Disconnect() noexcept;

private:
    HANDLE pipe_{INVALID_HANDLE_VALUE};
    bool connected_{};
};

class ControllerIsolationPipeClient final {
public:
    ControllerIsolationPipeClient() noexcept = default;
    ~ControllerIsolationPipeClient();
    ControllerIsolationPipeClient(const ControllerIsolationPipeClient&) = delete;
    ControllerIsolationPipeClient& operator=(
        const ControllerIsolationPipeClient&) = delete;

    [[nodiscard]] bool Connect(
        const std::wstring& pipeName,
        DWORD timeoutMilliseconds,
        std::uint32_t& nativeError) noexcept;
    [[nodiscard]] bool Exchange(
        const ControlFrame& request,
        ControlFrame& response,
        DWORD timeoutMilliseconds,
        std::uint32_t& nativeError) noexcept;
    [[nodiscard]] std::optional<std::uint32_t> serverProcessId() const noexcept;
    void Close() noexcept;

private:
    HANDLE pipe_{INVALID_HANDLE_VALUE};
};

} // namespace widgetrail::isolation
