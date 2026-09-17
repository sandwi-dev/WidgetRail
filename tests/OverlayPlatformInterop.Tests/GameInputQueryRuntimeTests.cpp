#include "../../src/OverlayPlatformInterop/GameInputQueryRuntime.h"
#include <atomic>
#include <iostream>
#include <thread>

using namespace GameInput::v3;
using widgetrail::platform::AcquireGameInputQueryRuntime;

namespace {
void CALLBACK ObserveDevice(GameInputCallbackToken, void* context, IGameInputDevice*,
    std::uint64_t, GameInputDeviceStatus, GameInputDeviceStatus) noexcept {
    static_cast<std::atomic_uint*>(context)->fetch_add(1);
}
}

int main() {
    auto first = AcquireGameInputQueryRuntime();
    if (!first) {
        // GameInput is optional on build machines; the host supports XInput.
        std::cout << "SKIP GameInput query lifetime: runtime unavailable\n";
        return 0;
    }
    auto* identity = first.Get();
    first.Reset(); // A gap between queries must not retire the runtime.
    std::atomic_bool failed{};
    auto query = [&] {
        for (unsigned iteration = 0; iteration != 10000; ++iteration) {
            auto input = AcquireGameInputQueryRuntime();
            if (!input || input.Get() != identity) { failed = true; return; }
            if (iteration >= 2) continue;
            std::atomic_uint observations{};
            GameInputCallbackToken token{};
            const auto result = input->RegisterDeviceCallback(nullptr, GameInputKindGamepad,
                GameInputDeviceConnected, GameInputBlockingEnumeration, &observations, ObserveDevice, &token);
            if (FAILED(result) || !token) { failed = true; return; }
            input->StopCallback(token);
            if (!input->UnregisterCallback(token)) { failed = true; return; }
        }
    };
    { std::jthread concurrent(query); query(); }
    if (failed) { std::cerr << "Retained query identity or device enumeration failed\n"; return 1; }
    std::cout << "GameInput query lifetime: 20000 acquisitions and four enumerations retained one runtime\n";
    return 0;
}
