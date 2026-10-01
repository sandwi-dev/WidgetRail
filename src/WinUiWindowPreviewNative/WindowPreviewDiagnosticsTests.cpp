#include "WindowPreviewDiagnostics.h"
#include <Windows.h>
#include <iostream>
#include <stdexcept>
#include <string>
#include <thread>

int main() {
    int checks{};
    auto require = [&](bool ok) { if (!ok) throw std::runtime_error("preview diagnostic check " + std::to_string(checks)); ++checks; };
    PreviewCloseDiagnostics diagnostics;
    WrailPreviewHealth health;
    require(diagnostics.Read(health) && health.phase == 0 && health.slot == 0);
    WrailPreviewTarget target; target.window = 234; target.processId = 56;
    for (auto phase : {PreviewClosePhase::DetachCallback, PreviewClosePhase::CloseSession, PreviewClosePhase::ClosePool}) {
        diagnostics.Begin(phase, 3, 12, target, 78, 901);
        require(diagnostics.Read(health) && health.phase == static_cast<uint32_t>(phase) &&
            health.reason == 3 && health.slot == 12 && health.window == 234 && health.processId == 56 &&
            health.frames == 78 && health.startedAt == 901 && health.error == 0);
        diagnostics.Error(E_FAIL);
        require(diagnostics.Read(health) && health.error == E_FAIL);
    }
    diagnostics.End();
    require(diagnostics.Read(health) && health.phase == 0 && health.error == E_FAIL && health.slot == 12);

    // A writer paused halfway through publication must not spin or block readers.
    // No thread completes this publication until every bounded read has returned.
    ++diagnostics.sequence;
    for (int attempt = 0; attempt < 1000; ++attempt)
        require(!diagnostics.Read(health) && health.phase == 0 && health.slot == 12 && health.error == E_FAIL);
    ++diagnostics.sequence;

    PreviewCloseDiagnostics concurrent;
    std::atomic_bool start{}, done{}, consistent{true};
    std::atomic_uint32_t accepted{};
    std::thread writer([&] {
        while (!start.load()) std::this_thread::yield();
        for (uint32_t value = 1; value <= 100000; ++value) {
            WrailPreviewTarget source; source.window = value; source.processId = value;
            concurrent.Begin(PreviewClosePhase::CloseSession, value, value, source, value, value);
        }
        done.store(true);
    });
    start.store(true);
    for (int attempt = 0; attempt < 100000; ++attempt) {
        WrailPreviewHealth snapshot;
        if (!concurrent.Read(snapshot)) continue;
        ++accepted;
        const auto value = snapshot.slot;
        if (snapshot.window != value || snapshot.processId != value || snapshot.frames != value ||
            snapshot.reason != value || snapshot.startedAt != static_cast<int64_t>(value) || snapshot.error != 0 ||
            snapshot.phase != (value ? static_cast<uint32_t>(PreviewClosePhase::CloseSession) : 0)) consistent.store(false);
    }
    writer.join();
    require(done.load() && consistent.load());
    require(concurrent.Read(health) && health.slot == 100000 && health.frames == 100000);

    // Exercise the actual exported ABI without creating a device, source, or window.
    WrailPreviewEngine* engine{};
    WrailPreviewHealthReader* reader{};
    WrailPreviewHealthReader* other{};
    require(WrailPreviewCreate(&engine) == S_OK && engine);
    require(WrailPreviewAcquireHealth(engine, &reader) == S_OK && reader);
    require(WrailPreviewAcquireHealth(engine, &other) == S_OK && other);
    require(WrailPreviewReadHealth(reader, &health) == S_OK && health.phase == 0);
    WrailPreviewReleaseHealth(other);
    std::thread destroyer([&] { WrailPreviewDestroy(engine); });
    for (int attempt = 0; attempt < 1000; ++attempt)
        require(WrailPreviewReadHealth(reader, &health) == S_OK && health.phase == 0);
    destroyer.join();
    require(WrailPreviewReadHealth(reader, &health) == S_OK && health.size == 56 && health.version == 1);
    health.version = 2;
    require(WrailPreviewReadHealth(reader, &health) == E_INVALIDARG && health.version == 2);
    health.version = 1; health.size = 0;
    require(WrailPreviewReadHealth(reader, &health) == E_INVALIDARG);
    require(WrailPreviewReadHealth(reader, nullptr) == E_INVALIDARG);
    require(WrailPreviewReadHealth(nullptr, &health) == E_INVALIDARG);
    WrailPreviewReleaseHealth(reader);
    WrailPreviewReleaseHealth(nullptr);
    reader = reinterpret_cast<WrailPreviewHealthReader*>(1);
    require(WrailPreviewAcquireHealth(nullptr, &reader) == E_INVALIDARG && !reader);
    require(WrailPreviewAcquireHealth(nullptr, nullptr) == E_POINTER);
    std::cout << "WindowPreviewDiagnostics passed " << checks << " checks; coherent concurrent snapshots=" << accepted.load() << '\n';
}
