#pragma once

#include <cstdint>
#include <map>
#include <memory>
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
    [[nodiscard]] bool Poll();
    [[nodiscard]] bool consumed() const noexcept;
private:
    struct Impl;
    std::unique_ptr<Impl> impl_;
};
}
