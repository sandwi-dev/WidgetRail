#include "DualSenseHidReader.h"
#include "DualSenseConnectionProbe.h"
#include <Windows.h>
#include <setupapi.h>
#include <hidsdi.h>
#include <hidpi.h>
#include <chrono>
#include <condition_variable>
#include <mutex>
#include <thread>
#include <cwctype>

namespace widgetrail::isolation {
namespace {
class Handle final {
public:
    explicit Handle(HANDLE value = INVALID_HANDLE_VALUE) noexcept : value_(value) {}
    ~Handle() { if (valid()) CloseHandle(value_); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
    bool valid() const noexcept { return value_ && value_ != INVALID_HANDLE_VALUE; }
    HANDLE get() const noexcept { return value_; }
private:
    HANDLE value_;
};
struct Devices final {
    HDEVINFO value;
    ~Devices() { if (value != INVALID_HANDLE_VALUE) SetupDiDestroyDeviceInfoList(value); }
};
std::uint64_t Timestamp() noexcept {
    return static_cast<std::uint64_t>(std::chrono::duration_cast<std::chrono::microseconds>(
        std::chrono::steady_clock::now().time_since_epoch()).count());
}
} // namespace

SelectedControllerDiscoveryStatus DiscoverDualSenseController(std::uint64_t token,
    DualSenseDevice& selected, const SelectedControllerEnrollment* expected, bool requireLiveInput) noexcept {
    selected = {};
    try {
        GUID guid{}; HidD_GetHidGuid(&guid);
        Devices devices{SetupDiGetClassDevsW(&guid, nullptr, nullptr, DIGCF_DEVICEINTERFACE | DIGCF_PRESENT)};
        if (devices.value == INVALID_HANDLE_VALUE) return SelectedControllerDiscoveryStatus::Unavailable;
        // Bound the entire liveness pass, including multiple remembered devices.
        const auto liveDeadline = GetTickCount64() + 750;
        SP_DEVICE_INTERFACE_DATA entry{sizeof(entry)};
        for (DWORD index = 0; index < 512 && SetupDiEnumDeviceInterfaces(devices.value, nullptr, &guid, index, &entry); ++index) {
            DWORD bytes{};
            (void)SetupDiGetDeviceInterfaceDetailW(devices.value, &entry, nullptr, 0, &bytes, nullptr);
            if (bytes < sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_W) || bytes > 4096) continue;
            std::vector<BYTE> buffer(bytes);
            auto* detail = reinterpret_cast<SP_DEVICE_INTERFACE_DETAIL_DATA_W*>(buffer.data());
            detail->cbSize = sizeof(*detail);
            if (!SetupDiGetDeviceInterfaceDetailW(devices.value, &entry, detail, bytes, nullptr, nullptr)) continue;
            // Metadata identifies candidates. Isolation additionally requires a live
            // probe below, then reopens its retained reader after hiding the device.
            Handle handle(CreateFileW(detail->DevicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE,
                nullptr, OPEN_EXISTING, 0, nullptr));
            HIDD_ATTRIBUTES attributes{sizeof(attributes)};
            if (!handle.valid() || !HidD_GetAttributes(handle.get(), &attributes) ||
                !IsDualSenseProduct(attributes.VendorID, attributes.ProductID)) continue;
            PHIDP_PREPARSED_DATA data{};
            if (!HidD_GetPreparsedData(handle.get(), &data)) continue;
            HIDP_CAPS caps{};
            const auto capsResult = HidP_GetCaps(data, &caps);
            HidD_FreePreparsedData(data);
            if (capsResult != HIDP_STATUS_SUCCESS || !IsControllerHidUsage(caps.UsagePage, caps.Usage) ||
                (caps.InputReportByteLength != 64 && caps.InputReportByteLength != 78 && caps.InputReportByteLength != 10)) continue;
            DualSenseDevice device;
            device.path = detail->DevicePath;
            device.reportBytes = caps.InputReportByteLength;
            device.transport = caps.InputReportByteLength == 64 ? DualSenseTransport::Usb : DualSenseTransport::Bluetooth;
            if (!BuildNativeHidDescriptor(device.path, token, attributes.VendorID, attributes.ProductID, device.descriptor) ||
                (expected && !SameStableControllerIdentity(*expected, device.descriptor.enrollment))) continue;
            if (selected.descriptor.valid() &&
                device.descriptor.deviceInstanceId.view() >= selected.descriptor.deviceInstanceId.view()) continue;
            if (requireLiveInput) {
                const auto now = GetTickCount64();
                if (now >= liveDeadline) break;
                const auto wait = static_cast<std::uint32_t>(std::min<ULONGLONG>(250, liveDeadline - now));
                DualSenseHidReader probe;
                if (!ProbeDualSenseConnection(probe, device.descriptor.enrollment, wait)) continue;
            }
            selected = std::move(device);
        }
        return selected.descriptor.valid() ? SelectedControllerDiscoveryStatus::Ready : SelectedControllerDiscoveryStatus::Unavailable;
    } catch (...) { selected = {}; return SelectedControllerDiscoveryStatus::Unavailable; }
}

struct DualSenseHidReader::Impl final {
    mutable std::mutex mutex;
    std::condition_variable changed;
    std::jthread thread;
    Handle stopEvent{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    SelectedControllerCurrent current{};
    std::uint64_t connectionGeneration{};
    std::optional<SelectedControllerEnrollment> expected;
    ControllerIsolationReaderIngress* ingress{};
    std::function<void()> guidePressed;

    void Disconnect() noexcept {
        bool wasConnected{};
        { std::scoped_lock lock(mutex); wasConnected = current.connected; current = {}; }
        if (wasConnected && ingress)
            (void)ingress->Publish(ControllerReaderEventKind::Disconnected, Timestamp(), GetTickCount64(), {}, false);
        changed.notify_all();
    }

    void ReadDevice(const DualSenseDevice& device, std::stop_token stop) {
        Handle handle(CreateFileW(device.path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE,
            nullptr, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, nullptr));
        HIDD_ATTRIBUTES attributes{sizeof(attributes)};
        SelectedControllerDescriptor actual;
        if (!handle.valid() || !HidD_GetAttributes(handle.get(), &attributes) ||
            !BuildNativeHidDescriptor(device.path, device.descriptor.enrollment.enrollmentToken,
                attributes.VendorID, attributes.ProductID, actual) ||
            !SameStableControllerIdentity(device.descriptor.enrollment, actual.enrollment)) return;
        Handle readEvent(CreateEventW(nullptr, TRUE, FALSE, nullptr));
        if (!readEvent.valid()) return;
        bool first = true, guideDown = false;
        std::array<std::uint8_t, 256> bytes{};
        while (!stop.stop_requested()) {
            OVERLAPPED operation{}; operation.hEvent = readEvent.get();
            ResetEvent(readEvent.get());
            DWORD count{};
            BOOL read = ReadFile(handle.get(), bytes.data(), device.reportBytes, &count, &operation);
            if (!read && GetLastError() == ERROR_IO_PENDING) {
                const HANDLE events[]{stopEvent.get(), readEvent.get()};
                const auto waited = WaitForMultipleObjects(2, events, FALSE, 250);
                if (waited != WAIT_OBJECT_0 + 1) CancelIoEx(handle.get(), &operation);
                // Drain cancellation before the stack-owned OVERLAPPED or buffer
                // can go away. This occurs only on the reader thread.
                read = GetOverlappedResult(handle.get(), &operation, &count, TRUE);
            }
            if (!read || stop.stop_requested() || count > bytes.size()) break;
            DualSenseReport report;
            if (!DecodeDualSenseReport(std::span(bytes).first(count), device.transport, report)) continue;
            const auto timestamp = Timestamp(), now = GetTickCount64();
            {
                std::scoped_lock lock(mutex);
                if (first) ++connectionGeneration;
                current = {timestamp, now, report.state, true};
                if (ingress) {
                    (void)ingress->Publish(ControllerReaderEventKind::Reading, timestamp, now, report.state);
                    if (!first && report.guide != guideDown)
                        (void)ingress->Publish(report.guide ? ControllerReaderEventKind::GuidePressed :
                            ControllerReaderEventKind::GuideReleased, timestamp, now);
                }
            }
            changed.notify_all();
            if (!first && report.guide && !guideDown && guidePressed) guidePressed();
            first = false; guideDown = report.guide;
        }
    }

    void Run(std::stop_token stop) noexcept {
        try {
            while (!stop.stop_requested()) {
                DualSenseDevice device;
                if (DiscoverDualSenseController(expected ? expected->enrollmentToken : 1, device,
                        expected ? &*expected : nullptr) == SelectedControllerDiscoveryStatus::Ready)
                    ReadDevice(device, stop);
                Disconnect();
                // Isolation owns reconnection and enrollment. Ordinary reads may
                // discover a new device, but never hot-swap a live connection.
                if (expected || WaitForSingleObject(stopEvent.get(), 1000) == WAIT_OBJECT_0) break;
            }
        } catch (...) { Disconnect(); }
    }
};

DualSenseHidReader::DualSenseHidReader() = default;
DualSenseHidReader::~DualSenseHidReader() { Stop(); }
bool DualSenseHidReader::Start(const SelectedControllerEnrollment* expected,
    ControllerIsolationReaderIngress* ingress, std::function<void()> guidePressed) noexcept {
    Stop();
    try {
        impl_ = std::make_unique<Impl>();
        if (!impl_->stopEvent.valid()) { impl_.reset(); return false; }
        if (expected) impl_->expected = *expected;
        impl_->ingress = ingress; impl_->guidePressed = std::move(guidePressed);
        impl_->thread = std::jthread([this](std::stop_token stop) { impl_->Run(stop); });
        return true;
    } catch (...) { Stop(); return false; }
}
bool DualSenseHidReader::Sample(SelectedControllerCurrent& current, std::uint64_t* connectionGeneration) const noexcept {
    current = {};
    if (!impl_) return false;
    std::scoped_lock lock(impl_->mutex);
    if (!impl_->current.connected || GetTickCount64() - impl_->current.observedAtMilliseconds > 250) return false;
    current = impl_->current;
    if (connectionGeneration) *connectionGeneration = impl_->connectionGeneration;
    return true;
}
bool DualSenseHidReader::WaitForReading(std::uint32_t milliseconds) noexcept {
    if (!impl_) return false;
    std::unique_lock lock(impl_->mutex);
    return impl_->changed.wait_for(lock, std::chrono::milliseconds(milliseconds), [&] { return impl_->current.connected; });
}
void DualSenseHidReader::Stop() noexcept {
    if (!impl_) return;
    if (impl_->thread.joinable()) {
        impl_->thread.request_stop(); SetEvent(impl_->stopEvent.get()); impl_->thread.join();
    }
    impl_.reset();
}

namespace {
class DualSenseSelectedControllerReader final : public SelectedControllerSource {
    DualSenseHidReader reader_;
public:
    SelectedControllerPrepareStatus Prepare(const SelectedControllerEnrollment& enrollment,
        ControllerIsolationReaderIngress& ingress, SelectedControllerEnrollment& prepared) noexcept override {
        prepared = {};
        if (!enrollment.valid() || enrollment.deviceFamily != NativeDualSenseFamily)
            return SelectedControllerPrepareStatus::InvalidEnrollment;
        DualSenseDevice device;
        if (DiscoverDualSenseController(enrollment.enrollmentToken, device, &enrollment) != SelectedControllerDiscoveryStatus::Ready)
            return SelectedControllerPrepareStatus::Unavailable;
        if (!reader_.Start(&enrollment, &ingress) || !reader_.WaitForReading(750)) {
            reader_.Stop(); return SelectedControllerPrepareStatus::CurrentReadingUnavailable;
        }
        prepared = device.descriptor.enrollment;
        return SelectedControllerPrepareStatus::Ready;
    }
    bool SampleCurrent(SelectedControllerCurrent& current) noexcept override { return reader_.Sample(current); }
    // Input-only support does not send output/feature reports to the controller.
    bool SupportsRumble() const noexcept override { return false; }
    void Stop() noexcept override { reader_.Stop(); }
};
} // namespace
std::unique_ptr<SelectedControllerSource> CreateDualSenseSelectedControllerReader() noexcept {
    return std::unique_ptr<SelectedControllerSource>(new (std::nothrow) DualSenseSelectedControllerReader());
}
} // namespace widgetrail::isolation
