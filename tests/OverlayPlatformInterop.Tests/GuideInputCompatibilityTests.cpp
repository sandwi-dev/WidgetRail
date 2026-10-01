#include "../../src/OverlayPlatformInterop/GuideInputCompatibility.h"
#include <array>
#include <atomic>
#include <chrono>
#include <cstdlib>
#include <future>
#include <iostream>
#include <memory>
#include <thread>
namespace {
int checks{};
void Check(bool condition, const char* message) {
    ++checks;
    if (!condition) { std::cerr << "FAIL: " << message << '\n'; std::exit(EXIT_FAILURE); }
}
}
int main() {
    {
        struct BlockedDriver {
            std::promise<void> entered;
            std::promise<void> release;
            std::promise<void> returned;
            std::atomic<int> calls{};
        };
        auto driver = std::make_shared<BlockedDriver>();
        auto entered = driver->entered.get_future();
        auto released = driver->release.get_future().share();
        auto returned = driver->returned.get_future();
        auto poller = std::make_unique<widgetrail::input::GuidePollDispatcher>([driver, released] {
            if (driver->calls.fetch_add(1) == 0) {
                driver->entered.set_value();
                released.wait();
                driver->returned.set_value();
            }
            return static_cast<std::uint8_t>(1);
        });
        Check(poller->Poll() == 0, "Guide polling returns before the driver finishes");
        Check(entered.wait_for(std::chrono::seconds(2)) == std::future_status::ready,
            "Guide driver callback starts on worker");
        for (int i = 0; i < 100; ++i)
            Check(poller->Poll() == 0, "blocked Guide polling never waits on UI thread");
        Check(driver->calls.load() == 1, "only one blocked Guide read is outstanding");
        poller.reset();
        driver->release.set_value();
        Check(returned.wait_for(std::chrono::seconds(2)) == std::future_status::ready,
            "retired callback owns its resources until the driver returns");
    }

    {
        auto calls = std::make_shared<std::atomic<int>>(0);
        widgetrail::input::GuidePollDispatcher poller([calls] {
            return static_cast<std::uint8_t>(calls->fetch_add(1) == 0 ? 0x0A : 0);
        });
        std::uint8_t edges{};
        const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(2);
        while (edges == 0 && std::chrono::steady_clock::now() < deadline) {
            edges = poller.Poll();
            std::this_thread::sleep_for(std::chrono::milliseconds(1));
        }
        Check(edges == 0x0A, "asynchronous Guide results preserve controller slot identity");
        Check(poller.Poll() == 0, "a completed Guide edge is consumed once");
    }
    {
        auto completed = std::make_shared<std::promise<void>>();
        auto done = completed->get_future();
        widgetrail::input::GuidePollDispatcher poller([completed] {
            std::this_thread::sleep_for(std::chrono::milliseconds(300));
            completed->set_value();
            return static_cast<std::uint8_t>(1);
        });
        (void)poller.Poll();
        Check(done.wait_for(std::chrono::seconds(2)) == std::future_status::ready,
            "slow Guide driver callback completes");
        Check(poller.Poll() == 0, "driver recovery cannot replay a stale Guide edge");
    }


    widgetrail::input::GuideEdgeTracker tracker;
    std::array<bool, XUSER_MAX_COUNT> state{};
    Check(tracker.Update(state) == 0, "initial neutral state is only a baseline");
    state[0] = true;
    Check(tracker.Update(state) == 0x01, "slot zero rising edge is reported");
    Check(tracker.Update(state) == 0, "held Guide does not repeat");
    state[0] = false;
    Check(tracker.Update(state) == 0, "release does not toggle");
    state[1] = true;
    state[3] = true;
    Check(tracker.Update(state) == 0x0A, "simultaneous slots retain identity");
    state = {};
    Check(tracker.Update(state) == 0, "multi-slot release is quiet");

    widgetrail::input::GuideCompatibilityActivation activation;
    widgetrail::input::GuideCompatibilityActivation::DeviceId first{};
    widgetrail::input::GuideCompatibilityActivation::DeviceId second{};
    first[0] = 1;
    second[0] = 2;
    Check(!activation.active() && activation.deviceCount() == 0,
          "compatibility polling starts dormant");
    Check(activation.Update(first, true) && activation.active(),
          "first legacy connection activates compatibility polling");
    Check(!activation.Update(first, true) && activation.deviceCount() == 1,
          "duplicate connection notification is idempotent");
    Check(!activation.Update(second, true) && activation.deviceCount() == 2,
          "additional legacy devices retain one active timer");
    Check(!activation.Update(first, false) && activation.active(),
          "disconnecting one of two devices retains polling");
    Check(activation.Update(second, false) && !activation.active(),
          "last legacy disconnect disables polling");
    Check(!activation.Update(first, false),
          "duplicate disconnect notification is idempotent");
    Check(activation.Update(first, true),
          "compatibility polling can reactivate after reconnect");
    activation.Reset();
    Check(!activation.active() && activation.deviceCount() == 0,
          "reset clears tracked devices");

    std::cout << "GuideInputCompatibilityTests passed (" << checks << " checks)\n";
    return EXIT_SUCCESS;
}