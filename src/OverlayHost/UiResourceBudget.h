#pragma once

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <memory>
#include <mutex>

namespace widgetrail::resources {

enum class Kind { DecodedImage, GpuImage, RetainedRaster, CompositorSurface, AnimationSurface, ShadowMask, EncodedArtwork, DecoderTransport, Count };
enum class Admission { Required, Optional };

// Accounts known application-owned storage, not driver residency. One allocation
// lease follows one backing store; copying it to another owner never adds bytes.
// Owners perform eviction on their own threads. This class never calls owners
// while holding its lock, including from decoder-worker admission or release.
class UiResourceBudget final {
    static constexpr std::size_t KindCount = static_cast<std::size_t>(Kind::Count);
public:
    struct Snapshot {
        std::size_t liveBytes{}, peakBytes{}, protectedBytes{}, allocations{}, peakAllocations{};
        std::array<std::size_t, KindCount> bytesByKind{}, protectedByKind{};
        std::size_t retentionTarget{}, reclaimToBytes{};
        std::uint64_t optionalDenials{}, pressureRequests{};
        // liveBytes includes pending reservations; allocatedBytes counts only
        // successfully created/adopted backing stores. Neither is GPU residency.
        std::size_t allocatedBytes{}, peakAllocatedBytes{};
        std::array<std::size_t, KindCount> allocatedByKind{};
        [[nodiscard]] bool needsReclamation() const noexcept {
            return liveBytes > std::max(reclaimToBytes, protectedBytes);
        }
        [[nodiscard]] std::size_t overTargetBytes() const noexcept {
            return liveBytes > retentionTarget ? liveBytes - retentionTarget : 0;
        }
    };
private:
    struct State {
        std::mutex mutex;
        Snapshot snapshot;
    };
public:
    class Allocation;
    class Protection;
    using Lease = std::shared_ptr<Allocation>;
    using Pin = std::shared_ptr<Protection>;
    static constexpr std::size_t DefaultRetentionTarget = 384U * 1024U * 1024U;

    explicit UiResourceBudget(std::size_t retentionTarget = DefaultRetentionTarget) : state_(std::make_shared<State>()) {
        state_->snapshot.retentionTarget = state_->snapshot.reclaimToBytes = retentionTarget;
    }
    UiResourceBudget(const UiResourceBudget&) = delete;
    UiResourceBudget& operator=(const UiResourceBudget&) = delete;
    [[nodiscard]] Lease Reserve(Kind kind, std::size_t bytes, Admission admission);
    [[nodiscard]] bool Owns(const Lease& allocation) const noexcept;
    [[nodiscard]] Snapshot Read() const {
        std::scoped_lock lock(state_->mutex);
        return state_->snapshot;
    }
    // A lower retention target requests reclamation, never destroys live data.
    void SetRetentionTarget(std::size_t bytes) {
        std::scoped_lock lock(state_->mutex);
        state_->snapshot.retentionTarget = state_->snapshot.reclaimToBytes = bytes;
        if (state_->snapshot.liveBytes > bytes) ++state_->snapshot.pressureRequests;
    }
private:
    static Lease ReserveInState(const std::shared_ptr<State>& state, Kind kind, std::size_t bytes, Admission admission);
    std::shared_ptr<State> state_;
};

class UiResourceBudget::Allocation final : public std::enable_shared_from_this<Allocation> {
    friend class UiResourceBudget;
    friend class Protection;
    Allocation(std::shared_ptr<State> state, Kind kind, std::size_t bytes, Admission admission)
        : state_(std::move(state)), kind_(kind), bytes_(bytes), admission_(admission) {}
public:
    ~Allocation() {
        if (!registered_) return;
        std::scoped_lock lock(state_->mutex);
        auto& snapshot = state_->snapshot;
        snapshot.liveBytes -= bytes_;
        snapshot.bytesByKind[static_cast<std::size_t>(kind_)] -= bytes_;
        if (allocated_) {
            snapshot.allocatedBytes -= bytes_;
            snapshot.allocatedByKind[static_cast<std::size_t>(kind_)] -= bytes_;
        }
        --snapshot.allocations;
        if (snapshot.liveBytes <= snapshot.reclaimToBytes) snapshot.reclaimToBytes = snapshot.retentionTarget;
    }
    Allocation(const Allocation&) = delete;
    Allocation& operator=(const Allocation&) = delete;
    [[nodiscard]] std::size_t bytes() const {
        std::scoped_lock lock(state_->mutex);
        return bytes_;
    }
    [[nodiscard]] Kind kind() const noexcept { return kind_; }
    // Adjust required in-flight storage to actual capacity before publication.
    [[nodiscard]] bool ResizeRequiredReservation(std::size_t bytes) {
        std::scoped_lock lock(state_->mutex);
        if (allocated_ || protections_ != 0 || admission_ != Admission::Required || bytes == 0) return false;
        auto& snapshot = state_->snapshot;
        const auto otherBytes = snapshot.liveBytes - bytes_;
        if (bytes > std::numeric_limits<std::size_t>::max() - otherBytes) return false;
        snapshot.liveBytes = otherBytes + bytes;
        snapshot.bytesByKind[static_cast<std::size_t>(kind_)] -= bytes_;
        snapshot.bytesByKind[static_cast<std::size_t>(kind_)] += bytes;
        bytes_ = bytes;
        snapshot.peakBytes = std::max(snapshot.peakBytes, snapshot.liveBytes);
        if (snapshot.liveBytes <= snapshot.reclaimToBytes) snapshot.reclaimToBytes = snapshot.retentionTarget;
        if (snapshot.liveBytes > snapshot.retentionTarget) ++snapshot.pressureRequests;
        return true;
    }
    void Commit() {
        std::scoped_lock lock(state_->mutex);
        if (allocated_) return;
        allocated_ = true;
        auto& snapshot = state_->snapshot;
        snapshot.allocatedBytes += bytes_;
        snapshot.allocatedByKind[static_cast<std::size_t>(kind_)] += bytes_;
        snapshot.peakAllocatedBytes = std::max(snapshot.peakAllocatedBytes, snapshot.allocatedBytes);
    }
    // A deep copy is another backing store. Ordinary shared ownership instead
    // copies the Lease and keeps a single allocation record.
    [[nodiscard]] Lease Duplicate(std::size_t bytes) const {
        return UiResourceBudget::ReserveInState(state_, kind_, bytes, Admission::Required);
    }
    [[nodiscard]] bool protectedFromEviction() const {
        std::scoped_lock lock(state_->mutex);
        return protections_ != 0;
    }
    [[nodiscard]] Pin Protect();
private:
    std::shared_ptr<State> state_;
    Kind kind_;
    std::size_t bytes_, protections_{};
    Admission admission_;
    std::weak_ptr<Protection> sharedProtection_;
    bool registered_{};
    bool allocated_{};
};

class UiResourceBudget::Protection final {
    friend class Allocation;
    explicit Protection(Lease allocation) : allocation_(std::move(allocation)) {}
public:
    ~Protection() {
        if (!registered_) return;
        std::scoped_lock lock(allocation_->state_->mutex);
        if (--allocation_->protections_ == 0) {
            auto& snapshot = allocation_->state_->snapshot;
            snapshot.protectedBytes -= allocation_->bytes_;
            snapshot.protectedByKind[static_cast<std::size_t>(allocation_->kind_)] -= allocation_->bytes_;
        }
    }
    Protection(const Protection&) = delete;
    Protection& operator=(const Protection&) = delete;
private:
    Lease allocation_;
    bool registered_{};
};

inline UiResourceBudget::Pin UiResourceBudget::Allocation::Protect() {
    // Frames share a protection object as well as an allocation. Stable rasters
    // need no per-frame heap allocation; the weak back-reference avoids cycles.
    {
        std::scoped_lock lock(state_->mutex);
        if (auto pin = sharedProtection_.lock()) return pin;
    }
    // Allocate outside the lock; a failed/control-block destructor may release
    // the allocation and must never reacquire a held budget mutex.
    auto pin = Pin(new Protection(shared_from_this()));
    std::scoped_lock lock(state_->mutex);
    if (auto existing = sharedProtection_.lock()) return existing;
    sharedProtection_ = pin;
    if (protections_++ == 0) {
        state_->snapshot.protectedBytes += bytes_;
        state_->snapshot.protectedByKind[static_cast<std::size_t>(kind_)] += bytes_;
    }
    pin->registered_ = true;
    return pin;
}

inline UiResourceBudget::Lease UiResourceBudget::Reserve(Kind kind, std::size_t bytes, Admission admission) {
    return ReserveInState(state_, kind, bytes, admission);
}
inline bool UiResourceBudget::Owns(const Lease& allocation) const noexcept {
    return allocation && allocation->state_ == state_;
}

inline UiResourceBudget::Lease UiResourceBudget::ReserveInState(
    const std::shared_ptr<State>& state, Kind kind, std::size_t bytes, Admission admission) {
    if (static_cast<std::size_t>(kind) >= KindCount || bytes == 0 ||
        (admission != Admission::Required && admission != Admission::Optional)) return {};
    auto allocation = Lease(new Allocation(state, kind, bytes, admission));
    std::scoped_lock lock(state->mutex);
    auto& snapshot = state->snapshot;
    if (bytes > std::numeric_limits<std::size_t>::max() - snapshot.liveBytes) return {};
    if (admission == Admission::Optional &&
        (bytes > snapshot.retentionTarget || snapshot.liveBytes > snapshot.retentionTarget - bytes)) {
        ++snapshot.optionalDenials;
        // Oversized optional objects cannot fit even after eviction; do not
        // empty useful caches trying to admit one.
        if (bytes <= snapshot.retentionTarget) {
            snapshot.reclaimToBytes = std::min(snapshot.reclaimToBytes, snapshot.retentionTarget - bytes);
            ++snapshot.pressureRequests;
        }
        return {};
    }
    snapshot.liveBytes += bytes;
    snapshot.bytesByKind[static_cast<std::size_t>(kind)] += bytes;
    snapshot.peakBytes = std::max(snapshot.peakBytes, snapshot.liveBytes);
    ++snapshot.allocations;
    snapshot.peakAllocations = std::max(snapshot.peakAllocations, snapshot.allocations);
    if (snapshot.liveBytes > snapshot.retentionTarget) ++snapshot.pressureRequests;
    allocation->registered_ = true;
    return allocation;
}
} // namespace widgetrail::resources
