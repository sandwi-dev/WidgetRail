#include "../../src/OverlayPlatformInterop/ControllerIsolationReader.h"

#include <algorithm>
#include <array>
#include <atomic>
#include <cstdlib>
#include <iostream>
#include <string_view>
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

ControllerDeviceNodeIdentity NodeIdentity(const std::wstring_view value) {
    ControllerDeviceNodeIdentity result;
    Check(value.size() < result.value.size(),
          "fixture node identity fits the production bound");
    std::copy(value.begin(), value.end(), result.value.begin());
    result.length = value.size();
    return result;
}

SelectedControllerDescriptor Descriptor(
    const std::uint8_t seed,
    const std::wstring_view instanceId) {
    return {Enrollment(seed), NodeIdentity(instanceId)};
}

struct FakeAncestryBackend final : ControllerDeviceAncestryBackend {
    bool resolveSucceeds{true};
    bool locateSucceeds{true};
    bool rootSucceeds{true};
    bool malformedResolvedIdentity{};
    ControllerDeviceNodeToken root{3};
    ControllerDeviceNodeToken first{1};
    ControllerDeviceNodeToken failRead{};
    ControllerDeviceNodeToken failParent{};
    std::array<ControllerDeviceNodeIdentity, 4> identities{
        NodeIdentity(L"unused"), NodeIdentity(L"HID\\PHYSICAL"),
        NodeIdentity(L"USB\\PARENT"), NodeIdentity(L"HTREE\\ROOT\\0")};
    std::array<ControllerDeviceNodeToken, 4> parents{0, 2, 3, 0};

    bool ResolveInterfaceInstanceId(
        std::wstring_view,
        ControllerDeviceNodeIdentity& identity) noexcept override {
        if (!resolveSucceeds) return false;
        identity = identities[first];
        if (malformedResolvedIdentity) {
            identity.value[1] = L'\0';
        }
        return true;
    }

    bool LocateNode(
        const ControllerDeviceNodeIdentity&,
        ControllerDeviceNodeToken& node) noexcept override {
        if (!locateSucceeds) return false;
        node = first;
        return true;
    }

    bool LocateRoot(ControllerDeviceNodeToken& value) noexcept override {
        if (!rootSucceeds) return false;
        value = root;
        return true;
    }

    bool ReadNodeIdentity(
        const ControllerDeviceNodeToken node,
        ControllerDeviceNodeIdentity& identity) noexcept override {
        if (node == failRead || node >= identities.size()) return false;
        identity = identities[node];
        return true;
    }

    bool Parent(
        const ControllerDeviceNodeToken node,
        ControllerDeviceNodeToken& parent) noexcept override {
        if (node == failParent || node >= parents.size()) return false;
        parent = parents[node];
        return true;
    }
};

void AncestryRequiresAnExactRootedPhysicalChain() {
    FakeAncestryBackend aliases;
    Check(IsSelectedControllerDescendant(1, 2, aliases) == true,
          "HID gamepad descendant belongs to the selected Xbox device");
    Check(IsSelectedControllerDescendant(1, 1, aliases) == true,
          "selected HID controller is its own target");
    Check(IsSelectedControllerDescendant(1, 99, aliases) == false,
          "another controller cannot enter the selected subtree");
    Check(!IsSelectedControllerDescendant(1, 3, aliases).has_value(),
          "device-tree root cannot become an all-device hiding scope");
    aliases.failParent = 1;
    Check(!IsSelectedControllerDescendant(1, 2, aliases).has_value(),
          "unreadable ancestry is not permission to hide a device");
    aliases.failParent = 0; aliases.parents[2] = 1;
    Check(!IsSelectedControllerDescendant(1, 99, aliases).has_value(),
          "ancestry cycles fail closed");
    Check(IsControllerHidUsage(1, 4) && IsControllerHidUsage(1, 5) &&
          !IsControllerHidUsage(1, 2) && !IsControllerHidUsage(1, 6) && !IsControllerHidUsage(12, 5),
          "only joystick/gamepad HID collections qualify, not mouse keyboard or consumer controls");
    FakeAncestryBackend physical;
    Check(ClassifyControllerDeviceAncestry(
              L"\\\\?\\HID#PHYSICAL", physical) ==
              ControllerDeviceAncestry::Physical,
          "a complete non-virtual chain terminating at the exact root is physical");

    FakeAncestryBackend virtualAncestor;
    virtualAncestor.identities[2] = NodeIdentity(L"ROOT\\VIGEMBUS\\0000");
    Check(ClassifyControllerDeviceAncestry(
              L"\\\\?\\HID#PHYSICAL", virtualAncestor) ==
              ControllerDeviceAncestry::KnownVirtualOutput,
          "a ViGEm ancestor rejects the selected device");
    Check(ClassifyControllerDeviceAncestry(
              L"\\\\?\\ROOT#NEFARIUS\\VIGEM#0000", physical) ==
              ControllerDeviceAncestry::KnownVirtualOutput,
          "a virtual marker in the interface path fails before traversal");

    FakeAncestryBackend missingProperty;
    missingProperty.resolveSucceeds = false;
    Check(ClassifyControllerDeviceAncestry(
              L"\\\\?\\HID#PHYSICAL", missingProperty) ==
              ControllerDeviceAncestry::Unknown,
          "missing or wrong-type interface identity is unknown, not physical");
    FakeAncestryBackend malformedProperty;
    malformedProperty.malformedResolvedIdentity = true;
    Check(ClassifyControllerDeviceAncestry(
              L"\\\\?\\HID#PHYSICAL", malformedProperty) ==
              ControllerDeviceAncestry::Unknown,
          "malformed embedded-NUL interface identity is unknown");
    FakeAncestryBackend missingRoot;
    missingRoot.rootSucceeds = false;
    Check(ClassifyControllerDeviceAncestry(
              L"\\\\?\\HID#PHYSICAL", missingRoot) ==
              ControllerDeviceAncestry::Unknown,
          "failure to identify the actual device-tree root is unknown");
    FakeAncestryBackend failedRead;
    failedRead.failRead = 2;
    Check(ClassifyControllerDeviceAncestry(
              L"\\\\?\\HID#PHYSICAL", failedRead) ==
              ControllerDeviceAncestry::Unknown,
          "mid-chain identity failure is unknown");
    FakeAncestryBackend failedParent;
    failedParent.failParent = 2;
    Check(ClassifyControllerDeviceAncestry(
              L"\\\\?\\HID#PHYSICAL", failedParent) ==
              ControllerDeviceAncestry::Unknown,
          "mid-chain parent failure is unknown rather than clean termination");
    FakeAncestryBackend cycle;
    cycle.parents[2] = 1;
    Check(ClassifyControllerDeviceAncestry(
              L"\\\\?\\HID#PHYSICAL", cycle) ==
              ControllerDeviceAncestry::Unknown,
          "cyclic ancestry is bounded and unknown");
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

void DiscoveryRequiresExactlyOnePhysicalController() {
    SelectedControllerDescriptor selected = Descriptor(9, L"HID\\STALE");
    SelectedControllerDiscovery none;
    Check(none.Resolve(selected) ==
              SelectedControllerDiscoveryStatus::Unavailable &&
              !selected.valid(),
          "zero connected physical controllers is explicitly unavailable");

    SelectedControllerDiscovery virtualOnly;
    virtualOnly.Observe(
        SelectedControllerCandidateKind::KnownVirtualOutput);
    Check(virtualOnly.Resolve(selected) ==
              SelectedControllerDiscoveryStatus::Unavailable &&
              !selected.valid(),
          "known virtual outputs are excluded rather than selected");

    const auto first = Descriptor(1, L"HID\\PHYSICAL-ONE");
    SelectedControllerDiscovery unique;
    unique.Observe(SelectedControllerCandidateKind::Physical, first);
    unique.Observe(SelectedControllerCandidateKind::Physical, first);
    Check(unique.Resolve(selected) ==
              SelectedControllerDiscoveryStatus::Ready &&
              selected == first,
          "one exact physical controller is selected and duplicate callbacks deduplicate");

    SelectedControllerDiscovery multiple;
    multiple.Observe(SelectedControllerCandidateKind::Physical, first);
    multiple.Observe(
        SelectedControllerCandidateKind::Physical,
        Descriptor(2, L"HID\\PHYSICAL-TWO"));
    Check(multiple.Resolve(selected) ==
              SelectedControllerDiscoveryStatus::Ambiguous &&
              !selected.valid(),
          "multiple distinct physical controllers fail closed as ambiguous");

    SelectedControllerDiscovery unknown;
    unknown.Observe(SelectedControllerCandidateKind::Physical, first);
    unknown.Observe(SelectedControllerCandidateKind::Unknown);
    Check(unknown.Resolve(selected) ==
              SelectedControllerDiscoveryStatus::UnknownIdentity &&
              !selected.valid(),
          "an unclassified connected gamepad prevents physical admission");
}

void FixedEventsPreserveAuthorityAndOrder() {
    const auto first = Descriptor(1, L"HID\\A");
    const auto second = Descriptor(2, L"HID\\B");
    SelectedControllerDescriptor selected;
    SelectedControllerDiscovery forward(true), reverse(true);
    forward.Observe(SelectedControllerCandidateKind::Physical, first);
    forward.Observe(SelectedControllerCandidateKind::Physical, second);
    reverse.Observe(SelectedControllerCandidateKind::Physical, second);
    reverse.Observe(SelectedControllerCandidateKind::Physical, first);
    Check(forward.Resolve(selected) == SelectedControllerDiscoveryStatus::Ready && selected == first,
          "automatic selection chooses stable instance order among multiple controllers");
    Check(reverse.Resolve(selected) == SelectedControllerDiscoveryStatus::Ready && selected == first,
          "automatic selection does not depend on callback enumeration order");
    reverse.Observe(SelectedControllerCandidateKind::Unknown);
    reverse.Observe(SelectedControllerCandidateKind::KnownVirtualOutput);
    Check(reverse.Resolve(selected) == SelectedControllerDiscoveryStatus::Ready && selected == first,
          "ineligible and virtual controllers never replace an eligible physical controller");
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

void ReservedProducerYieldsToTheSerializedConsumer() {
    ControllerIsolationReaderIngress ingress;
    Check(ingress.Open(Authority(), 1), "paused producer fixture opens");
    ingress.PauseNextProducerForTest();
    bool published{};
    std::thread producer([&] {
        published = ingress.Publish(
            ControllerReaderEventKind::Reading, 1, 1);
    });
    for (std::size_t attempt = 0;
         attempt < 10'000 && !ingress.ProducerPausedForTest(); ++attempt) {
        std::this_thread::yield();
    }
    Check(ingress.ProducerPausedForTest() &&
              ingress.reservedThroughOrdinal() == 1,
          "producer test seam pauses only after reserving its exact ticket");
    ControllerReaderEvent event;
    Check(ingress.TryPop(event) == ControllerReaderPopResult::ProducerPending,
          "consumer observes the reserved unpublished head without skipping it");
    ingress.ReleaseProducerForTest();
    producer.join();
    Check(published && ingress.TryPop(event) == ControllerReaderPopResult::Event &&
              event.ingressOrdinal == 1,
          "released producer publishes into the same reserved total order");
}

} // namespace

int main() {
    const auto enrolled = Enrollment();
    auto local = Descriptor(1, L"physical-controller");
    local.enrollment.deviceId[0] = 20;
    local.enrollment.deviceRootId[0] = 21;
    SelectedControllerEnrollment resolved;
    Check(ResolveLocalController(enrolled, SelectedControllerDiscoveryStatus::Ready,
              local, resolved) == LocalControllerResolutionStatus::Ready &&
              resolved == local.enrollment,
          "post-hide GameInput-local IDs rebind to the same stable physical controller");
    auto foreign = local;
    foreign.enrollment.containerId[0] ^= 1;
    Check(ResolveLocalController(enrolled, SelectedControllerDiscoveryStatus::Ready,
              foreign, resolved) == LocalControllerResolutionStatus::StableIdentityMismatch &&
              !resolved.valid(),
          "same local IDs cannot admit a different physical controller");
    foreign = local;
    foreign.enrollment.knownVirtualOutput = true;
    Check(ResolveLocalController(enrolled, SelectedControllerDiscoveryStatus::Ready,
              foreign, resolved) == LocalControllerResolutionStatus::StableIdentityMismatch &&
              !resolved.valid(),
          "virtual output cannot be rebound as the physical reader");
    Check(ResolveLocalController(enrolled, SelectedControllerDiscoveryStatus::Ambiguous,
              local, resolved) == LocalControllerResolutionStatus::Ambiguous &&
              !resolved.valid(),
          "ambiguous discovery never returns a usable enrollment");
    AncestryRequiresAnExactRootedPhysicalChain();
    EnrollmentIsExactAndVirtualFailsClosed();
    DiscoveryRequiresExactlyOnePhysicalController();
    FixedEventsPreserveAuthorityAndOrder();
    InvalidAndOverflowingInputFailsClosed();
    DistinctCallbackProducersShareOneTotalOrder();
    ReservedProducerYieldsToTheSerializedConsumer();
    std::cout << "ControllerIsolationReaderTests passed " << checks
              << " checks.\n";
    return EXIT_SUCCESS;
}
