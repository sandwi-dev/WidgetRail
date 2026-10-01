#pragma once
#include "WindowPreviewNative.h"
#include <atomic>

static_assert(std::atomic_uint64_t::is_always_lock_free && std::atomic_int64_t::is_always_lock_free &&
    std::atomic_uint32_t::is_always_lock_free && std::atomic_int32_t::is_always_lock_free);

enum class PreviewClosePhase : uint32_t { Idle, DetachCallback, CloseSession, ClosePool };

// One writer (native owner), independent bounded readers. Atomic fields avoid
// data races; the sequence rejects a mixed snapshot without spinning or locking.
struct PreviewCloseDiagnostics final {
    std::atomic_uint64_t sequence{}, window{}, slot{}, frames{};
    std::atomic_uint32_t phase{}, reason{}, processId{};
    std::atomic_int32_t error{};
    std::atomic_int64_t startedAt{};
    void Begin(PreviewClosePhase stage, uint32_t why, uint64_t id,
        const WrailPreviewTarget& target, uint64_t count, int64_t now) noexcept {
        ++sequence;
        phase.store(static_cast<uint32_t>(stage)); reason.store(why); slot.store(id);
        window.store(target.window); processId.store(target.processId); frames.store(count);
        startedAt.store(now); error.store(0);
        ++sequence;
    }
    void Error(int32_t value) noexcept { ++sequence; error.store(value); ++sequence; }
    void End() noexcept { ++sequence; phase.store(0); ++sequence; }
    bool Read(WrailPreviewHealth& result) const noexcept {
        const auto before = sequence.load();
        if (before & 1) return false;
        WrailPreviewHealth snapshot;
        snapshot.phase = phase.load(); snapshot.reason = reason.load(); snapshot.slot = slot.load();
        snapshot.window = window.load(); snapshot.processId = processId.load(); snapshot.frames = frames.load();
        snapshot.startedAt = startedAt.load(); snapshot.error = error.load();
        if (sequence.load() != before) return false;
        result = snapshot;
        return true;
    }
};
