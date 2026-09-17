#pragma once

#include <GameInput.h>
#include <wrl/client.h>
#include <mutex>

namespace widgetrail::platform {

// Queries must not repeatedly tear down GameInput while device arrival is in
// flight. Keep one query-only runtime until process shutdown. This instance
// never changes focus policy or retains device/reading callbacks; the input
// readers continue to own their separate policies and callback lifetimes.
inline Microsoft::WRL::ComPtr<GameInput::v3::IGameInput> AcquireGameInputQueryRuntime() noexcept {
    static std::mutex mutex;
    static Microsoft::WRL::ComPtr<GameInput::v3::IGameInput> runtime;
    const std::scoped_lock lock(mutex);
    if (!runtime) {
        Microsoft::WRL::ComPtr<GameInput::v3::IGameInput> candidate;
        if (SUCCEEDED(GameInput::v3::GameInputCreate(candidate.GetAddressOf())))
            runtime = std::move(candidate);
    }
    return runtime;
}

} // namespace widgetrail::platform
