#include "UiResourceBudget.h"
#include <barrier>
#include <cstdlib>
#include <iostream>
#include <thread>
#include <vector>

using namespace widgetrail::resources;
namespace {
std::size_t checks{};
void Check(bool condition, const char* message) {
    ++checks;
    if (!condition) { std::cerr << "FAIL: " << message << '\n'; std::exit(1); }
}
void SharedStorageAndProtection() {
    UiResourceBudget budget(100);
    auto cache = budget.Reserve(Kind::RetainedRaster, 64, Admission::Optional);
    auto published = cache, candidate = cache;
    auto oldFrame = cache->Protect(), nextFrame = cache->Protect();
    Check(budget.Read().liveBytes == 64 && budget.Read().allocations == 1, "sharing a backing store counts it once");
    Check(budget.Read().protectedBytes == 64, "overlapping frames protect shared storage once");
    cache.reset(); published.reset(); candidate.reset(); oldFrame.reset();
    Check(budget.Read().liveBytes == 64 && budget.Read().protectedBytes == 64, "an in-use frame survives cache and prior-frame retirement");
    nextFrame.reset();
    Check(budget.Read().liveBytes == 0 && budget.Read().protectedBytes == 0 && budget.Read().allocations == 0,
        "last owner returns accounted storage exactly once");
    Check(budget.Read().peakBytes == 64, "peak survives reclamation");
}
void PressureAndRequiredWorkingSet() {
    UiResourceBudget budget(100);
    auto decoded = budget.Reserve(Kind::DecodedImage, 60, Admission::Optional);
    auto current = budget.Reserve(Kind::CompositorSurface, 30, Admission::Required);
    auto frame = current->Protect();
    Check(!budget.Reserve(Kind::GpuImage, 20, Admission::Optional), "optional retention requests pressure before exceeding the target");
    Check(budget.Read().needsReclamation() && budget.Read().reclaimToBytes == 80, "denied demand requests sufficient reclaim headroom");
    decoded.reset();
    Check(!budget.Read().needsReclamation() && budget.Read().reclaimToBytes == 100, "releasing idle ownership clears the pressure request");
    auto replacement = budget.Reserve(Kind::AnimationSurface, 90, Admission::Required);
    auto replacingFrame = replacement->Protect();
    Check(budget.Read().liveBytes == 120 && budget.Read().protectedBytes == 120 && budget.Read().overTargetBytes() == 20,
        "required frame overlap remains explicit and accounted above the soft target");
    Check(!budget.Read().needsReclamation(), "live-only oversubscription cannot demand eviction of an in-use frame");
    frame.reset(); current.reset();
    Check(budget.Read().liveBytes == 90 && budget.Read().overTargetBytes() == 0, "completed overlap returns below target");
    budget.SetRetentionTarget(20);
    Check(replacement->protectedFromEviction() && !budget.Read().needsReclamation(), "lowering the target preserves live resources");
    replacingFrame.reset();
    Check(budget.Read().needsReclamation() && !replacement->protectedFromEviction(), "retired frames expose idle storage for owner-thread eviction");
    replacement.reset();
    Check(budget.Read().liveBytes == 0 && !budget.Read().needsReclamation(), "owner eviction reaches intended bounds");
    Check(!budget.Reserve(Kind::GpuImage, 21, Admission::Optional) && budget.Read().reclaimToBytes == 20,
        "an oversized optional request does not demand flushing unrelated storage");
}
void WorkerAndRenderOwnership() {
    UiResourceBudget budget(16);
    std::barrier ready(5), release(5);
    std::vector<std::jthread> workers;
    for (int i = 0; i < 4; ++i) workers.emplace_back([&] {
        auto allocation = budget.Reserve(Kind::DecodedImage, 8, Admission::Required);
        auto protection = allocation->Protect();
        ready.arrive_and_wait(); release.arrive_and_wait();
    });
    ready.arrive_and_wait();
    const auto peak = budget.Read();
    Check(peak.liveBytes == 32 && peak.protectedBytes == 32 && peak.allocations == 4, "concurrent producer ownership publishes a coherent snapshot");
    Check(peak.bytesByKind[static_cast<std::size_t>(Kind::DecodedImage)] == 32, "category totals share the same accounting boundary");
    release.arrive_and_wait(); workers.clear();
    Check(budget.Read().liveBytes == 0 && budget.Read().peakBytes == 32, "concurrent final releases restore accounting without owner callbacks");
    UiResourceBudget::Pin late;
    std::weak_ptr<UiResourceBudget::Allocation> observed;
    { UiResourceBudget retiring; auto allocation = retiring.Reserve(Kind::GpuImage, 12, Admission::Required); observed = allocation; late = allocation->Protect(); }
    Check(!observed.expired(), "frame ownership outlives the budget facade");
    late.reset(); // Allocation state outlives the facade and every initiating owner.
    Check(observed.expired(), "late frame retirement releases the final allocation after owner destruction");
}
void InvalidAndOverflow() {
    UiResourceBudget budget;
    Check(!budget.Reserve(Kind::Count, 1, Admission::Required) && !budget.Reserve(Kind::DecodedImage, 0, Admission::Required), "invalid requests do not create accounting entries");
    auto maximum = budget.Reserve(Kind::GpuImage, std::numeric_limits<std::size_t>::max(), Admission::Required);
    Check(maximum && !budget.Reserve(Kind::GpuImage, 1, Admission::Required), "aggregate byte overflow is rejected atomically");
    maximum.reset();
    Check(budget.Read().liveBytes == 0 && budget.Read().allocations == 0, "overflow rejection leaves balanced accounting");
}
void AllocationCommitAndCopy() {
    UiResourceBudget budget(100);
    auto pending = budget.Reserve(Kind::GpuImage, 40, Admission::Required);
    Check(budget.Read().liveBytes == 40 && budget.Read().allocatedBytes == 0, "reservation does not report an uncreated texture as allocated");
    pending.reset();
    Check(budget.Read().peakAllocatedBytes == 0 && budget.Read().liveBytes == 0, "failed creation releases its reservation without inflating allocation peak");
    auto source = budget.Reserve(Kind::DecodedImage, 20, Admission::Required);
    source->Commit(); source->Commit();
    Check(budget.Read().allocatedBytes == 20, "allocation commit is idempotent");
    auto copied = source->Duplicate(24); copied->Commit();
    Check(budget.Read().allocatedBytes == 44 && budget.Read().allocations == 2, "deep copies have distinct storage accounting");
    source.reset(); copied.reset();
    Check(budget.Read().allocatedBytes == 0 && budget.Read().peakAllocatedBytes == 44, "allocation retirement balances current bytes and preserves peak");
}
}
int main() {
    SharedStorageAndProtection(); PressureAndRequiredWorkingSet(); WorkerAndRenderOwnership(); InvalidAndOverflow(); AllocationCommitAndCopy();
    std::cout << "UiResourceBudgetTests passed (" << checks << " checks)\n";
}
