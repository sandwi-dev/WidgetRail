#pragma once

#include "ControllerIsolationProtocol.h"

#include <array>
#include <cstddef>
#include <cstdint>

namespace widgetrail::isolation {

class ControllerIsolationInputEventQueue final {
public:
    static constexpr std::size_t Capacity = 256;

    [[nodiscard]] bool Push(
        const GamepadState& state,
        const std::uint64_t ingressOrdinal) noexcept {
        if (count_ == events_.size() || ingressOrdinal == 0) return false;
        events_[(head_ + count_) % events_.size()] = {state, ingressOrdinal};
        ++count_;
        return true;
    }

    [[nodiscard]] ControllerInputBatch TakeBatch() noexcept {
        ControllerInputBatch batch;
        while (count_ != 0 &&
               batch.count < ControllerIsolationInputBatchCapacity) {
            const auto event = events_[head_];
            head_ = (head_ + 1) % events_.size();
            --count_;
            if (batch.count == 0)
                batch.firstIngressOrdinal = event.ingressOrdinal;
            batch.lastIngressOrdinal = event.ingressOrdinal;
            batch.states[batch.count++] = event.state;
        }
        return batch;
    }

    [[nodiscard]] std::size_t size() const noexcept { return count_; }

private:
    struct Event final {
        GamepadState state{};
        std::uint64_t ingressOrdinal{};
    };
    std::array<Event, Capacity> events_{};
    std::size_t head_{};
    std::size_t count_{};
};

class ControllerIsolationInputBatchQueue final {
public:
    static constexpr std::size_t Capacity = 32;

    [[nodiscard]] bool Push(const ControllerInputBatch& batch) noexcept {
        if (batch.count == 0) return true;
        if (count_ == batches_.size() ||
            batch.count > ControllerIsolationInputBatchCapacity ||
            batch.firstIngressOrdinal == 0 ||
            batch.lastIngressOrdinal < batch.firstIngressOrdinal)
            return false;
        batches_[(head_ + count_) % batches_.size()] = batch;
        ++count_;
        return true;
    }

    [[nodiscard]] ControllerInputBatch TakeBatch() noexcept {
        if (count_ == 0) return {};
        const auto batch = batches_[head_];
        head_ = (head_ + 1) % batches_.size();
        --count_;
        return batch;
    }

    [[nodiscard]] std::size_t size() const noexcept { return count_; }

private:
    std::array<ControllerInputBatch, Capacity> batches_{};
    std::size_t head_{};
    std::size_t count_{};
};

class ControllerIsolationHostStateQueue final {
public:
    static constexpr std::size_t Capacity =
        ControllerIsolationInputBatchCapacity *
        ControllerIsolationInputBatchQueue::Capacity;

    [[nodiscard]] bool Push(const ControllerInputBatch& batch) noexcept {
        if (batch.count > ControllerIsolationInputBatchCapacity ||
            count_ + batch.count > states_.size()) return false;
        for (std::uint32_t index = 0; index < batch.count; ++index) {
            states_[(head_ + count_) % states_.size()] = batch.states[index];
            ++count_;
        }
        return true;
    }

    [[nodiscard]] bool Pop(GamepadState& state) noexcept {
        if (count_ == 0) return false;
        state = states_[head_];
        head_ = (head_ + 1) % states_.size();
        --count_;
        return true;
    }

    void Clear() noexcept { head_ = 0; count_ = 0; }
    [[nodiscard]] std::size_t size() const noexcept { return count_; }

private:
    std::array<GamepadState, Capacity> states_{};
    std::size_t head_{};
    std::size_t count_{};
};

} // namespace widgetrail::isolation
