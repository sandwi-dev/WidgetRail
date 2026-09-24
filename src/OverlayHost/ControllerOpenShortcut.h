#pragma once

#include <algorithm>
#include <array>
#include <cstdint>
#include <map>
#include <memory>
#include <optional>
#include <span>

namespace widgetrail::input {

struct OpenShortcutSample final {
    std::uintptr_t source{};
    // Bit 0 = View, bit 1 = Menu, from one controller.
    std::uint8_t buttons{};
};

class ViewMenuChordTracker final {
public:
    [[nodiscard]] bool Update(std::span<const OpenShortcutSample> samples);
    void Reset() noexcept { armed_.clear(); consumed_ = false; }
    [[nodiscard]] bool consumed() const noexcept { return consumed_; }
private:
    std::map<std::uintptr_t, bool> armed_;
    bool consumed_{};
};

enum class OpenShortcutSampling { AllControllers, NativeOnly };

// Shared HID input supplements the public readers; isolation supplies one
// authoritative sample instead. Keep aggregation on the tested polling path.
template<class ReadShared>
bool PollViewMenuSources(ViewMenuChordTracker& tracker,
    std::optional<OpenShortcutSample> native,
    OpenShortcutSampling sampling, ReadShared&& readShared) {
    constexpr std::size_t maximumSharedSamples = 20; // Four XInput slots + 16 GameInput devices.
    std::array<OpenShortcutSample, maximumSharedSamples + 1> samples{};
    std::size_t count{};
    if (native) samples[count++] = *native;
    if (sampling == OpenShortcutSampling::AllControllers) {
        const auto destination = std::span(samples).subspan(count, maximumSharedSamples);
        count += std::min(readShared(destination), maximumSharedSamples);
    }
    return tracker.Update(std::span(samples.data(), count));
}

// Optional background observer. It does not acquire exclusive input or change
// controller output. GameInput devices and public XInput slots are read alike.
class ControllerOpenShortcut final {
public:
    ControllerOpenShortcut();
    ~ControllerOpenShortcut();
    ControllerOpenShortcut(const ControllerOpenShortcut&) = delete;
    ControllerOpenShortcut& operator=(const ControllerOpenShortcut&) = delete;
    void Start();
    void Stop() noexcept;
    [[nodiscard]] bool Poll(std::optional<std::uint16_t> nativeButtons = std::nullopt,
        OpenShortcutSampling sampling = OpenShortcutSampling::AllControllers);
    [[nodiscard]] bool consumed() const noexcept;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
}
