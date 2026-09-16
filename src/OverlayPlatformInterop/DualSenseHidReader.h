#pragma once
#include "ControllerIsolationReader.h"
#include "DualSenseReport.h"
#include <functional>

namespace widgetrail::isolation {
inline constexpr std::int8_t NativeDualSenseFamily = 127;
struct DualSenseDevice final {
    SelectedControllerDescriptor descriptor;
    std::wstring path;
    DualSenseTransport transport{};
    std::uint16_t reportBytes{};
};

// Shared PnP/ancestry validation; native HID readers cannot enroll virtual or
// software-enumerated devices merely because their vendor/product IDs match.
bool BuildNativeHidDescriptor(std::wstring_view path, std::uint64_t token,
    std::uint16_t vendor, std::uint16_t product, SelectedControllerDescriptor& descriptor) noexcept;
SelectedControllerDiscoveryStatus DiscoverDualSenseController(std::uint64_t token,
    DualSenseDevice& device, const SelectedControllerEnrollment* expected = nullptr,
    bool requireLiveInput = false) noexcept;

// One overlapped HID reader on a background thread. UI reads only a copied,
// freshness-checked state. Stop cancels pending I/O before releasing its buffers.
class DualSenseHidReader final {
public:
    DualSenseHidReader();
    ~DualSenseHidReader();
    bool Start(const SelectedControllerEnrollment* expected = nullptr,
        ControllerIsolationReaderIngress* ingress = nullptr, std::function<void()> guidePressed = {}) noexcept;
    bool Sample(SelectedControllerCurrent& current, std::uint64_t* connectionGeneration = nullptr) const noexcept;
    bool WaitForReading(std::uint32_t milliseconds) noexcept;
    void Stop() noexcept;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
std::unique_ptr<SelectedControllerSource> CreateDualSenseSelectedControllerReader() noexcept;
} // namespace widgetrail::isolation
