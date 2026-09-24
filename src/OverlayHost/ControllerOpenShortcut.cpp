#include "ControllerOpenShortcut.h"
#include <Windows.h>
#include <GameInput.h>
#include <Xinput.h>
#include <wrl/client.h>
#include <algorithm>
#include <array>
#include <mutex>

using namespace GameInput::v3;

namespace widgetrail::input {
bool ViewMenuChordTracker::Update(const std::span<const OpenShortcutSample> samples) {
    std::erase_if(armed_, [&](const auto& item) {
        return std::ranges::none_of(samples, [&](const auto& sample) { return sample.source == item.first; });
    });
    bool anyButtons{};
    bool trigger{};
    for (const auto& sample : samples) {
        const auto buttons = sample.buttons & 3;
        anyButtons |= buttons != 0;
        const auto entry = armed_.try_emplace(sample.source, false).first;
        if (buttons == 0) entry->second = true;
        else if (buttons == 3 && entry->second && !consumed_) trigger = true;
    }
    if (!anyButtons) consumed_ = false;
    if (trigger) consumed_ = true;
    return trigger;
}

struct ControllerOpenShortcut::Impl final {
    using Device = Microsoft::WRL::ComPtr<IGameInputDevice>;
    Microsoft::WRL::ComPtr<IGameInput> input;
    GameInputCallbackToken token{};
    std::mutex mutex;
    std::map<IGameInputDevice*, Device> devices;
    ViewMenuChordTracker tracker;
    bool active{};

    static void CALLBACK OnDevice(GameInputCallbackToken, void* context,
        IGameInputDevice* device, std::uint64_t, GameInputDeviceStatus current, GameInputDeviceStatus) noexcept {
        auto& self = *static_cast<Impl*>(context);
        if (!device) return;
        try {
            std::scoped_lock lock(self.mutex);
            if ((current & GameInputDeviceConnected) == GameInputDeviceNoStatus) self.devices.erase(device);
            else if (self.devices.size() < 16) self.devices.try_emplace(device, device);
        } catch (...) { /* A device omitted from this observer cannot generate a shortcut. */ }
    }
};

ControllerOpenShortcut::ControllerOpenShortcut() : impl_(std::make_unique<Impl>()) {}
ControllerOpenShortcut::~ControllerOpenShortcut() { Stop(); }
void ControllerOpenShortcut::Start() {
    Stop();
    impl_->active = true;
    if (SUCCEEDED(GameInputCreate(impl_->input.GetAddressOf()))) {
        impl_->input->SetFocusPolicy(GameInputEnableBackgroundInput);
        if (FAILED(impl_->input->RegisterDeviceCallback(nullptr, GameInputKindGamepad,
            GameInputDeviceConnected, GameInputAsyncEnumeration, impl_.get(), Impl::OnDevice, &impl_->token))) {
            impl_->token = 0;
            impl_->input.Reset();
        }
    }
    (void)Poll(); // A shortcut held while enabling it must first be released.
}
void ControllerOpenShortcut::Stop() noexcept {
    if (impl_->input && impl_->token) {
        impl_->input->StopCallback(impl_->token);
        impl_->input->UnregisterCallback(impl_->token);
    }
    impl_->token = 0;
    impl_->input.Reset();
    { std::scoped_lock lock(impl_->mutex); impl_->devices.clear(); }
    impl_->tracker.Reset();
    impl_->active = false;
}
bool ControllerOpenShortcut::consumed() const noexcept { return impl_->tracker.consumed(); }
bool ControllerOpenShortcut::Poll(std::optional<std::uint16_t> nativeButtons,
    OpenShortcutSampling sampling) {
    if (!impl_->active) return false;
    std::optional<OpenShortcutSample> native;
    if (nativeButtons) {
        native = OpenShortcutSample{static_cast<std::uintptr_t>(-1), static_cast<std::uint8_t>(
            ((*nativeButtons & XINPUT_GAMEPAD_BACK) ? 1 : 0) |
            ((*nativeButtons & XINPUT_GAMEPAD_START) ? 2 : 0))};
    }
    return PollViewMenuSources(impl_->tracker, native, sampling,
        [&](std::span<OpenShortcutSample> samples) {
            std::size_t count{};
            for (DWORD slot = 0; slot < XUSER_MAX_COUNT; ++slot) {
                XINPUT_STATE state{};
                if (XInputGetState(slot, &state) == ERROR_SUCCESS) {
                    samples[count++] = {slot + 1, static_cast<std::uint8_t>(
                        ((state.Gamepad.wButtons & XINPUT_GAMEPAD_BACK) ? 1 : 0) |
                        ((state.Gamepad.wButtons & XINPUT_GAMEPAD_START) ? 2 : 0))};
                }
            }
            std::array<Impl::Device, 16> devices;
            std::size_t deviceCount{};
            { std::scoped_lock lock(impl_->mutex); for (const auto& [key, device] : impl_->devices) devices[deviceCount++] = device; }
            for (std::size_t index = 0; index < deviceCount; ++index) {
                Microsoft::WRL::ComPtr<IGameInputReading> reading;
                GameInputGamepadState state{};
                if (impl_->input && SUCCEEDED(impl_->input->GetCurrentReading(GameInputKindGamepad,
                    devices[index].Get(), reading.GetAddressOf())) && reading->GetGamepadState(&state)) {
                    samples[count++] = {reinterpret_cast<std::uintptr_t>(devices[index].Get()), static_cast<std::uint8_t>(
                        ((state.buttons & GameInputGamepadView) ? 1 : 0) |
                        ((state.buttons & GameInputGamepadMenu) ? 2 : 0))};
                }
            }
            return count;
    });
}
}
