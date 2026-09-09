#include "../../src/OverlayPlatformInterop/ControllerIsolationReader.h"

#include <array>
#include <atomic>
#include <cstdlib>
#include <iostream>
#include <thread>
#include <type_traits>

namespace {

using namespace widgetrail::isolation;

static_assert(std::is_trivially_copyable_v<ControllerReaderEvent>);

int checks{};

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) {
        std::cerr << "FAILED: " << message << '\n';
        std::exit(EXIT_FAILURE);
    }
}

RoutingAuthority Authority(const std::uint64_t generation = 1) {
    return {generation, generation + 10, generation + 20, generation + 30};
}

SelectedControllerEnrollment Enrollment(const std::uint8_t seed = 1) {
    SelectedControllerEnrollment result;
    result.enrollmentToken = seed;
    result.deviceId[0] = seed;
    result.deviceRootId[0] = static_cast<std::uint8_t>(seed + 1);
    result.containerId[0] = static_cast<std::uint8_t>(seed + 2);
    result.normalizedPnpPathDigest[0] =
        static_cast<std::uint8_t>(seed + 3);
    result.vendorId = 0x045E;
    result.productId = static_cast<std::uint16_t>(0x0200 + seed);
    result.deviceFamily = 1;
    result.connected = true;
    result.gamepadSupported = true;
    return result;
}

void EnrollmentIsExactAndVirtualFailsClosed() {
    auto enrollment = Enrollment();
    Check(enrollment.valid(), "complete physical identity is valid");
    enrollment.normalizedPnpPathDigest = {};
    Check(!enrollment.valid(), "missing PnP digest is rejected");
    enrollment = Enrollment();
    enrollment.knownVirtualOutput = true;
    Check(!enrollment.valid(), "known virtual output is rejected");
    enrollment = Enrollment();
    enrollment.connected = false;
    Check(!enrollment.valid(), "disconnected enrollment is rejected");
    enrollment = Enrollment();
    enrollment.gamepadSupported = false;
    Check(!enrollment.valid(), "non-gamepad enrollment is rejected");
}

void FixedEventsPreserveAuthorityAndOrder() {
    ControllerIsolationReaderIngress ingress;
    const auto authority = Authority();
    const auto enrollment = Enrollment();
    Check(ingress.Open(authority, enrollment.enrollmentToken),
          "closed ingress opens for exact authority");

    GamepadState down;
    down.buttons = 0x1000;
    Check(ingress.Publish(
              ControllerReaderEventKind::Reading, 100, 10, down, true) &&
              ingress.Publish(
                  ControllerReaderEventKind::GuidePressed, 101, 11) &&
              ingress.Publish(
                  ControllerReaderEventKind::GuideReleased, 102, 12) &&
              ingress.Publish(
                  ControllerReaderEventKind::Disconnected, 103, 13, {}, false),
          "reading Guide and disconnect producers publish fixed events");
    Check(ingress.approximateSize() == 4,
          "fixed queue reports its bounded pending size to its consumer");

    const std::array kinds{
        ControllerReaderEventKind::Reading,
        ControllerReaderEventKind::GuidePressed,
        ControllerReaderEventKind::GuideReleased,
        ControllerReaderEventKind::Disconnected};
    for (std::size_t index = 0; index < kinds.size(); ++index) {
        ControllerReaderEvent event;
        Check(ingress.TryPop(event) == ControllerReaderPopResult::Event,
              "one fixed event is available");
        Check(event.kind == kinds[index] &&
                  event.authority == authority &&
                  event.deviceEnrollmentToken == enrollment.enrollmentToken &&
                  event.ingressOrdinal == index + 1 &&
                  event.sourceTimestampMicroseconds == 100 + index,
              "consumer sees exact authority and total ingress order");
    }
    ControllerReaderEvent event;
    Check(ingress.TryPop(event) == ControllerReaderPopResult::Empty,
          "drained fixed queue is empty");
    ingress.Close();
    Check(!ingress.Publish(
              ControllerReaderEventKind::Reading, 104, 14, {}, true) &&
              ingress.fault() == ControllerReaderFault::Closed,
          "closed ingress accepts no late callback");
}

void InvalidAndOverflowingInputFailsClosed() {
    ControllerIsolationReaderIngress invalid;
    const auto authority = Authority();
    Check(!invalid.Open({}, 1), "invalid routing authority cannot open ingress");
    Check(invalid.Open(authority, 1), "valid routing authority opens ingress");
    Check(!invalid.Publish(
              ControllerReaderEventKind::Reading, 1, 1, {}, false) &&
              invalid.fault() == ControllerReaderFault::InvalidEvent,
          "a disconnected reading cannot masquerade as current input");

    ControllerIsolationReaderIngress full;
    Check(full.Open(authority, 1), "full-queue fixture opens");
    for (std::size_t index = 0; index < ControllerReaderCapacity; ++index) {
        Check(full.Publish(
                  ControllerReaderEventKind::Reading, index + 1,
                  index + 1),
              "every preallocated queue cell is usable once");
    }
    Check(!full.Publish(
              ControllerReaderEventKind::Reading,
              ControllerReaderCapacity + 1,
              ControllerReaderCapacity + 1) &&
              full.fault() == ControllerReaderFault::QueueFull,
          "queue pressure faults rather than dropping an input transition");
}

void DistinctCallbackProducersShareOneTotalOrder() {
    ControllerIsolationReaderIngress ingress;
    const auto authority = Authority();
    Check(ingress.Open(authority, 1), "multi-producer fixture opens");
    constexpr std::size_t producerCount = 3;
    constexpr std::size_t eventsPerProducer = 64;
    std::atomic_bool start{};
    std::array<std::thread, producerCount> producers;
    std::array<std::atomic_bool, producerCount> succeeded{};
    for (std::size_t producer = 0; producer < producerCount; ++producer) {
        producers[producer] = std::thread([&, producer] {
            while (!start.load(std::memory_order_acquire))
                std::this_thread::yield();
            bool allPublished = true;
            for (std::size_t index = 0; index < eventsPerProducer; ++index) {
                allPublished = ingress.Publish(
                    ControllerReaderEventKind::Reading,
                    1 + producer * eventsPerProducer + index,
                    1 + producer * eventsPerProducer + index) && allPublished;
            }
            succeeded[producer].store(allPublished, std::memory_order_release);
        });
    }
    start.store(true, std::memory_order_release);
    for (auto& producer : producers) producer.join();
    for (const auto& result : succeeded) {
        Check(result.load(std::memory_order_acquire),
              "each callback producer publishes without loss");
    }
    Check(ingress.fault() == ControllerReaderFault::None,
          "bounded expected producer concurrency remains healthy");

    constexpr std::size_t expected = producerCount * eventsPerProducer;
    std::array<bool, expected> timestamps{};
    for (std::size_t index = 0; index < expected; ++index) {
        ControllerReaderEvent event;
        Check(ingress.TryPop(event) == ControllerReaderPopResult::Event &&
                  event.ingressOrdinal == index + 1,
              "consumer observes one gap-free reservation order");
        Check(event.sourceTimestampMicroseconds >= 1 &&
                  event.sourceTimestampMicroseconds <= expected,
              "producer timestamp remains in its bounded fixture domain");
        const auto timestampIndex = static_cast<std::size_t>(
            event.sourceTimestampMicroseconds - 1);
        Check(!timestamps[timestampIndex],
              "each produced callback event appears exactly once");
        timestamps[timestampIndex] = true;
    }
}

} // namespace

int main() {
    EnrollmentIsExactAndVirtualFailsClosed();
    FixedEventsPreserveAuthorityAndOrder();
    InvalidAndOverflowingInputFailsClosed();
    DistinctCallbackProducersShareOneTotalOrder();
    std::cout << "ControllerIsolationReaderTests passed " << checks
              << " checks.\n";
    return EXIT_SUCCESS;
}
