#include "AccessibilityTree.h"
#include "DeclarativeRenderer.h"

#include <Windows.h>
#include <psapi.h>

#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstddef>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <iostream>
#include <numeric>
#include <sstream>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

namespace fs = std::filesystem;

namespace {

constexpr std::size_t kColumnCount = 8;
constexpr std::size_t kRowCount = 6;
constexpr std::size_t kActionCount = kColumnCount * kRowCount;
constexpr std::size_t kWarmupUpdates = 32;
constexpr std::size_t kMeasuredUpdates = 256;
constexpr auto kIdleObservation = std::chrono::milliseconds(750);
constexpr double kControllerResponseBudgetMilliseconds = 50.0;
constexpr double kMaterialIdleCpuPercent = 5.0;
constexpr std::size_t kMaterialPrivateWorkingSetBytes = 128ULL * 1024ULL * 1024ULL;
constexpr std::size_t kProtocolSnapshotLimitBytes = 1024ULL * 1024ULL;

int checks{};

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) throw std::runtime_error(std::string(message));
}

struct Clock final {
    LARGE_INTEGER frequency{};

    Clock() {
        if (QueryPerformanceFrequency(&frequency) == FALSE || frequency.QuadPart <= 0)
            throw std::runtime_error("QPC frequency is unavailable");
    }

    [[nodiscard]] long long Now() const {
        LARGE_INTEGER value{};
        if (QueryPerformanceCounter(&value) == FALSE)
            throw std::runtime_error("QPC sample is unavailable");
        return value.QuadPart;
    }

    [[nodiscard]] double Milliseconds(const long long start, const long long end) const {
        return static_cast<double>(end - start) * 1000.0 /
            static_cast<double>(frequency.QuadPart);
    }
};

[[nodiscard]] unsigned long long FileTimeTicks(const FILETIME value) noexcept {
    ULARGE_INTEGER ticks{};
    ticks.LowPart = value.dwLowDateTime;
    ticks.HighPart = value.dwHighDateTime;
    return ticks.QuadPart;
}

[[nodiscard]] unsigned long long ProcessCpuTicks() {
    FILETIME created{}, exited{}, kernel{}, user{};
    Check(GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user) != FALSE,
          "process CPU time is available");
    return FileTimeTicks(kernel) + FileTimeTicks(user);
}

[[nodiscard]] std::size_t PrivateWorkingSetBytes() {
    std::vector<std::byte> buffer(256 * 1024);
    for (int attempt = 0; attempt < 8; ++attempt) {
        if (QueryWorkingSet(GetCurrentProcess(), buffer.data(),
                            static_cast<DWORD>(buffer.size())) != FALSE) {
            const auto* information =
                reinterpret_cast<const PSAPI_WORKING_SET_INFORMATION*>(buffer.data());
            std::size_t privatePages{};
            for (ULONG_PTR index = 0; index < information->NumberOfEntries; ++index) {
                if (!information->WorkingSetInfo[index].Shared) ++privatePages;
            }
            SYSTEM_INFO system{};
            GetNativeSystemInfo(&system);
            return privatePages * static_cast<std::size_t>(system.dwPageSize);
        }
        Check(GetLastError() == ERROR_BAD_LENGTH,
              "private working-set query fails only for a short buffer");
        buffer.resize(buffer.size() * 2);
    }
    throw std::runtime_error("private working-set query exceeded its bounded buffer");
}

[[nodiscard]] std::size_t PrivateBytes() {
    PROCESS_MEMORY_COUNTERS_EX counters{};
    counters.cb = sizeof(counters);
    Check(GetProcessMemoryInfo(
              GetCurrentProcess(), reinterpret_cast<PROCESS_MEMORY_COUNTERS*>(&counters),
              sizeof(counters)) != FALSE,
          "process private bytes are available");
    return static_cast<std::size_t>(counters.PrivateUsage);
}

[[nodiscard]] DWORD LogicalProcessorCount() noexcept {
    const auto count = GetActiveProcessorCount(ALL_PROCESSOR_GROUPS);
    return count == 0 ? 1 : count;
}

widgetrail::WidgetNode MakeButton(const std::size_t index) {
    widgetrail::WidgetNode node;
    node.id = L"performance.action." + std::to_wstring(index);
    node.kind = L"button";
    node.text = L"Representative action " + std::to_wstring(index);
    node.accessibilityLabel = node.text;
    node.accessibilityValue = L"revision 0";
    node.actionId = node.id;
    node.inputScopeId = L"performance.root";
    node.focusLeft = L"performance.action." + std::to_wstring(
        index == 0 ? kActionCount - 1 : index - 1);
    node.focusRight = L"performance.action." + std::to_wstring(
        (index + 1) % kActionCount);
    node.focusUp = L"performance.action." + std::to_wstring(
        (index + kActionCount - kColumnCount) % kActionCount);
    node.focusDown = L"performance.action." + std::to_wstring(
        (index + kColumnCount) % kActionCount);
    node.isSelected = index == 0;
    return node;
}

widgetrail::WidgetSnapshot RepresentativeSnapshot() {
    widgetrail::WidgetSnapshot snapshot;
    snapshot.sequence = 1;
    snapshot.instanceId = L"performance.synthetic.instance";
    snapshot.activeInputScopeId = L"performance.root";
    snapshot.initialFocusId = L"performance.action.0";
    snapshot.root.id = L"performance.root";
    snapshot.root.kind = L"stack";
    snapshot.root.inputScopeId = snapshot.activeInputScopeId;
    for (std::size_t rowIndex = 0; rowIndex < kRowCount; ++rowIndex) {
        widgetrail::WidgetNode row;
        row.id = L"performance.row." + std::to_wstring(rowIndex);
        row.kind = L"row";
        row.inputScopeId = snapshot.activeInputScopeId;
        for (std::size_t column = 0; column < kColumnCount; ++column)
            row.children.push_back(MakeButton(rowIndex * kColumnCount + column));
        snapshot.root.children.push_back(std::move(row));
    }
    return snapshot;
}

widgetrail::WidgetNode& ActionAt(widgetrail::WidgetSnapshot& snapshot, const std::size_t index) {
    return snapshot.root.children[index / kColumnCount].children[index % kColumnCount];
}

[[nodiscard]] std::size_t SnapshotNodeCount(const widgetrail::WidgetNode& root) {
    std::size_t count = 1;
    for (const auto& child : root.children) count += SnapshotNodeCount(child);
    return count;
}

[[nodiscard]] std::size_t CanonicalSnapshotBytes() {
    std::ostringstream stream;
    stream << R"json({"sequence":1,"instanceId":"performance.synthetic.instance","activeInputScopeId":"performance.root","initialFocusId":"performance.action.0","root":{"id":"performance.root","kind":"stack","inputScopeId":"performance.root","children":[)json";
    for (std::size_t row = 0; row < kRowCount; ++row) {
        if (row) stream << ',';
        stream << R"json({"id":"performance.row.)json" << row
               << R"json(","kind":"row","inputScopeId":"performance.root","children":[)json";
        for (std::size_t column = 0; column < kColumnCount; ++column) {
            if (column) stream << ',';
            const auto index = row * kColumnCount + column;
            stream << R"json({"id":"performance.action.)json" << index
                   << R"json(","kind":"button","text":"Representative action )json" << index
                   << R"json(","accessibilityLabel":"Representative action )json" << index
                   << R"json(","accessibilityValue":"revision 0","actionId":"performance.action.)json" << index
                   << R"json(","inputScopeId":"performance.root","focusLeft":"performance.action.)json"
                   << (index == 0 ? kActionCount - 1 : index - 1)
                   << R"json(","focusRight":"performance.action.)json" << (index + 1) % kActionCount
                   << R"json(","focusUp":"performance.action.)json"
                   << (index + kActionCount - kColumnCount) % kActionCount
                   << R"json(","focusDown":"performance.action.)json"
                   << (index + kColumnCount) % kActionCount
                   << R"json(","isSelected":)json" << (index == 0 ? "true" : "false") << '}';
        }
        stream << "]}";
    }
    stream << "]}}";
    return stream.str().size();
}

[[nodiscard]] double Percentile(std::vector<double> values, const double percentile) {
    Check(!values.empty(), "percentile input is nonempty");
    std::ranges::sort(values);
    const auto rank = static_cast<std::size_t>(
        std::ceil(percentile * static_cast<double>(values.size())));
    return values[std::clamp<std::size_t>(rank, 1, values.size()) - 1];
}

[[nodiscard]] bool SameSemanticNode(
    const widgetrail::accessibility::Node& left,
    const widgetrail::accessibility::Node& right) noexcept {
    return left.id == right.id && left.name == right.name && left.value == right.value &&
        left.actionId == right.actionId && left.role == right.role &&
        left.enabled == right.enabled && left.selected == right.selected &&
        left.focused == right.focused && left.bounds.x == right.bounds.x &&
        left.bounds.y == right.bounds.y && left.bounds.width == right.bounds.width &&
        left.bounds.height == right.bounds.height;
}

[[nodiscard]] std::size_t ChangedSemanticNodes(
    const widgetrail::accessibility::Tree& previous,
    const widgetrail::accessibility::Tree& current) {
    Check(previous.nodes.size() == current.nodes.size(),
          "representative semantic projection keeps a stable node count");
    std::size_t changed{};
    for (std::size_t index = 0; index < previous.nodes.size(); ++index) {
        Check(previous.nodes[index].id == current.nodes[index].id,
              "representative semantic projection keeps stable keyed order");
        if (!SameSemanticNode(previous.nodes[index], current.nodes[index])) ++changed;
    }
    return changed;
}

[[nodiscard]] bool HasUnexpectedRenderError(const widgetrail::RenderResult& result) noexcept {
    return std::ranges::any_of(result.diagnostics, [](const auto& diagnostic) {
        return diagnostic.severity == widgetrail::RenderDiagnosticSeverity::Error &&
            diagnostic.code != L"missing_render_target";
    });
}

struct PhaseResult final {
    double elapsedMilliseconds{};
    double cpuMilliseconds{};
    double normalizedCpuPercent{};
    std::size_t privateWorkingSetBefore{};
    std::size_t privateWorkingSetAfter{};
    std::size_t privateBytesBefore{};
    std::size_t privateBytesAfter{};
};

template <typename Work>
PhaseResult MeasurePhase(const Clock& clock, Work&& work) {
    const auto privateWorkingSetBefore = PrivateWorkingSetBytes();
    const auto privateBytesBefore = PrivateBytes();
    const auto cpuBefore = ProcessCpuTicks();
    const auto started = clock.Now();
    work();
    const auto ended = clock.Now();
    const auto cpuAfter = ProcessCpuTicks();
    const auto elapsed = clock.Milliseconds(started, ended);
    const auto cpu = static_cast<double>(cpuAfter - cpuBefore) / 10000.0;
    return {
        elapsed,
        cpu,
        elapsed <= 0.0 ? 0.0 : cpu * 100.0 /
            (elapsed * static_cast<double>(LogicalProcessorCount())),
        privateWorkingSetBefore,
        PrivateWorkingSetBytes(),
        privateBytesBefore,
        PrivateBytes(),
    };
}

struct Measurement final {
    PhaseResult measurementOverhead;
    PhaseResult hidden;
    PhaseResult visibleIdle;
    PhaseResult inputUpdate;
    std::size_t snapshotNodeCount{};
    std::size_t semanticNodeCount{};
    std::size_t updateCount{};
    std::size_t totalProjectedSemanticNodes{};
    std::size_t totalChangedSemanticNodes{};
    std::size_t snapshotBytes{};
    double qpcPairP50Milliseconds{};
    double projectionP50Milliseconds{};
    double projectionP95Milliseconds{};
    double projectionMaxMilliseconds{};
    unsigned long long qpcFrequency{};
};

Measurement RunMeasurement() {
    const Clock clock;
    std::vector<double> qpcPairSamples;
    qpcPairSamples.reserve(kMeasuredUpdates);
    for (std::size_t index = 0; index < kMeasuredUpdates; ++index) {
        const auto started = clock.Now();
        const auto ended = clock.Now();
        qpcPairSamples.push_back(clock.Milliseconds(started, ended));
    }

    const auto measurementOverhead = MeasurePhase(clock, [] {});
    const auto hidden = MeasurePhase(clock, [] { std::this_thread::sleep_for(kIdleObservation); });

    auto snapshot = RepresentativeSnapshot();
    widgetrail::DeclarativeRenderer renderer{nullptr, nullptr, nullptr};
    widgetrail::DeclarativeRenderOptions options;
    options.collectAccessibility = true;
    constexpr widgetrail::declarative::Rect viewport{0, 0, 980, 720};

    auto initialRender = renderer.Render(
        nullptr, snapshot, snapshot.initialFocusId, viewport, options);
    Check(!HasUnexpectedRenderError(initialRender),
          "representative snapshot completes null-target native layout without product errors");
    auto previousTree = widgetrail::accessibility::BuildWidgetTree(
        L"performance", L"synthetic-generation", snapshot, initialRender,
        snapshot.initialFocusId);
    Check(initialRender.accessibilityRegions.size() == kActionCount,
          "representative render exposes every action region");
    Check(previousTree.nodes.size() == kActionCount,
          "representative projection exposes every action exactly once");
    Check(widgetrail::accessibility::HasUniqueElementKeys(previousTree),
          "representative projection has unique stable semantic keys");

    const auto visibleIdle = MeasurePhase(
        clock, [] { std::this_thread::sleep_for(kIdleObservation); });

    std::size_t selectedIndex{};
    for (std::size_t index = 1; index <= kWarmupUpdates; ++index) {
        const auto nextIndex = index % kActionCount;
        ActionAt(snapshot, selectedIndex).isSelected = false;
        ActionAt(snapshot, nextIndex).isSelected = true;
        ActionAt(snapshot, nextIndex).accessibilityValue =
            L"warmup revision " + std::to_wstring(index);
        selectedIndex = nextIndex;
        snapshot.sequence++;
        const auto focus = L"performance.action." + std::to_wstring(selectedIndex);
        const auto render = renderer.Render(nullptr, snapshot, focus, viewport, options);
        previousTree = widgetrail::accessibility::BuildWidgetTree(
            L"performance", L"synthetic-generation", snapshot, render, focus);
        Check(previousTree.focusedNode.has_value(), "warmup projection retains focus");
    }

    std::vector<double> projectionLatencies;
    projectionLatencies.reserve(kMeasuredUpdates);
    std::size_t totalChangedSemanticNodes{};
    std::size_t totalProjectedSemanticNodes{};
    const auto inputUpdate = MeasurePhase(clock, [&] {
        for (std::size_t index = 1; index <= kMeasuredUpdates; ++index) {
            const auto nextIndex = (selectedIndex + 1) % kActionCount;
            const auto started = clock.Now();
            ActionAt(snapshot, selectedIndex).isSelected = false;
            ActionAt(snapshot, nextIndex).isSelected = true;
            ActionAt(snapshot, nextIndex).accessibilityValue =
                L"measured revision " + std::to_wstring(index);
            selectedIndex = nextIndex;
            snapshot.sequence++;
            const auto focus = L"performance.action." + std::to_wstring(selectedIndex);
            const auto render = renderer.Render(nullptr, snapshot, focus, viewport, options);
            Check(!HasUnexpectedRenderError(render),
                  "input update completes null-target native layout without product errors");
            auto currentTree = widgetrail::accessibility::BuildWidgetTree(
                L"performance", L"synthetic-generation", snapshot, render, focus);
            Check(currentTree.focusedNode &&
                      currentTree.nodes[*currentTree.focusedNode].id == focus,
                  "input update reaches the exact semantic projection");
            const auto ended = clock.Now();
            projectionLatencies.push_back(clock.Milliseconds(started, ended));
            totalChangedSemanticNodes += ChangedSemanticNodes(previousTree, currentTree);
            totalProjectedSemanticNodes += currentTree.nodes.size();
            previousTree = std::move(currentTree);
        }
    });

    const Measurement result{
        measurementOverhead,
        hidden,
        visibleIdle,
        inputUpdate,
        SnapshotNodeCount(snapshot.root),
        previousTree.nodes.size(),
        kMeasuredUpdates,
        totalProjectedSemanticNodes,
        totalChangedSemanticNodes,
        CanonicalSnapshotBytes(),
        Percentile(qpcPairSamples, 0.50),
        Percentile(projectionLatencies, 0.50),
        Percentile(projectionLatencies, 0.95),
        *std::ranges::max_element(projectionLatencies),
        static_cast<unsigned long long>(clock.frequency.QuadPart),
    };

    Check(result.snapshotNodeCount == 1 + kRowCount + kActionCount,
          "representative snapshot retains its exact bounded node inventory");
    Check(result.semanticNodeCount == kActionCount,
          "representative semantic projection retains its exact bounded inventory");
    Check(result.totalProjectedSemanticNodes == kMeasuredUpdates * kActionCount,
          "each measured update projects exactly one bounded semantic tree");
    Check(result.totalChangedSemanticNodes >= kMeasuredUpdates * 2 &&
              result.totalChangedSemanticNodes <= kMeasuredUpdates * 4,
          "each measured update keeps semantic changes bounded to the focus/selection neighborhood; observed " +
              std::to_string(result.totalChangedSemanticNodes));
    Check(result.snapshotBytes <= kProtocolSnapshotLimitBytes,
          "representative canonical snapshot remains inside the public frame bound");
    Check(result.projectionP95Milliseconds <= kControllerResponseBudgetMilliseconds,
          "input-to-semantic-projection p95 stays inside the documented response budget");
    Check(result.hidden.normalizedCpuPercent <= kMaterialIdleCpuPercent,
          "hidden harness CPU stays below the material-regression ceiling");
    Check(result.visibleIdle.normalizedCpuPercent <= kMaterialIdleCpuPercent,
          "visible-idle harness CPU stays below the material-regression ceiling");
    Check(std::max({result.hidden.privateWorkingSetAfter,
                    result.visibleIdle.privateWorkingSetAfter,
                    result.inputUpdate.privateWorkingSetAfter}) <=
              kMaterialPrivateWorkingSetBytes,
          "native semantic harness private working set stays bounded");
    return result;
}

fs::path ParseEvidenceFile(const int argc, wchar_t** argv) {
    if (argc == 1) return {};
    Check(argc == 3 && std::wstring_view(argv[1]) == L"--evidence-file",
          "usage: SemanticChurnPerformanceTests [--evidence-file <new-file>]");
    return fs::path(argv[2]);
}

void WritePhase(std::ostream& stream, const PhaseResult& phase) {
    stream << "{\"elapsedMilliseconds\":" << phase.elapsedMilliseconds
           << ",\"cpuMilliseconds\":" << phase.cpuMilliseconds
           << ",\"normalizedCpuPercent\":" << phase.normalizedCpuPercent
           << ",\"privateWorkingSetBeforeBytes\":" << phase.privateWorkingSetBefore
           << ",\"privateWorkingSetAfterBytes\":" << phase.privateWorkingSetAfter
           << ",\"privateBytesBefore\":" << phase.privateBytesBefore
           << ",\"privateBytesAfter\":" << phase.privateBytesAfter << '}';
}

void WriteEvidence(const fs::path& destination, const Measurement& result) {
    if (destination.empty()) return;
    Check(!fs::exists(destination), "performance evidence destination must be new");
    fs::create_directories(destination.parent_path());
    auto temporary = destination;
    temporary += L".tmp";
    Check(!fs::exists(temporary), "performance evidence temporary path must be new");
    SYSTEM_INFO system{};
    GetNativeSystemInfo(&system);
    std::ofstream stream(temporary, std::ios::binary | std::ios::trunc);
    Check(static_cast<bool>(stream), "performance evidence temporary file opens");
    stream << std::fixed << std::setprecision(6)
           << "{\n  \"schemaVersion\":1,\n"
              "  \"contract\":\"dlv016-native-semantic-churn-v1\",\n"
              "  \"configuration\":\""
#ifdef NDEBUG
           << "Release"
#else
           << "Debug"
#endif
           << "\",\n  \"architecture\":\"x64\",\n"
              "  \"logicalProcessors\":" << LogicalProcessorCount() << ",\n"
              "  \"pageBytes\":" << system.dwPageSize << ",\n"
              "  \"qpcFrequency\":" << result.qpcFrequency << ",\n"
              "  \"workload\":{\"snapshotNodeCount\":" << result.snapshotNodeCount
           << ",\"semanticNodeCount\":" << result.semanticNodeCount
           << ",\"updateCount\":" << result.updateCount
           << ",\"totalProjectedSemanticNodes\":" << result.totalProjectedSemanticNodes
           << ",\"totalChangedSemanticNodes\":" << result.totalChangedSemanticNodes
           << ",\"canonicalSnapshotBytes\":" << result.snapshotBytes << "},\n"
              "  \"latencyMilliseconds\":{\"qpcPairP50\":" << result.qpcPairP50Milliseconds
           << ",\"inputToProjectionP50\":" << result.projectionP50Milliseconds
           << ",\"inputToProjectionP95\":" << result.projectionP95Milliseconds
           << ",\"inputToProjectionMax\":" << result.projectionMaxMilliseconds << "},\n"
              "  \"phases\":{\n    \"measurementOverhead\":";
    WritePhase(stream, result.measurementOverhead);
    stream << ",\n    \"hidden\":";
    WritePhase(stream, result.hidden);
    stream << ",\n    \"visibleIdle\":";
    WritePhase(stream, result.visibleIdle);
    stream << ",\n    \"inputUpdate\":";
    WritePhase(stream, result.inputUpdate);
    stream << "\n  },\n"
              "  \"thresholds\":{\"controllerResponseP95Milliseconds\":50.0,"
              "\"protocolSnapshotBytes\":1048576,\"materialIdleCpuPercent\":5.0,"
              "\"materialPrivateWorkingSetBytes\":134217728,"
              "\"maximumChangedSemanticNodesPerUpdate\":4},\n"
              "  \"gate\":{\"status\":\"pass\",\"classification\":\"material-regression\"},\n"
              "  \"limitations\":[\"null-target layout excludes GPU, DWM, paint, and game-frame cost\","
              "\"same-process private resident pages exclude bridge and worker processes\","
              "\"idle CPU uses process-time granularity and a material ceiling rather than the 0.1 percent reference target\","
              "\"QPC pair and phase-measurement overhead are reported separately\"],\n"
              "  \"sanitized\":true\n}\n";
    stream.close();
    Check(static_cast<bool>(stream), "performance evidence temporary file is complete");
    std::error_code error;
    fs::rename(temporary, destination, error);
    Check(!error, "performance evidence publishes atomically");
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    try {
        const auto result = RunMeasurement();
        WriteEvidence(ParseEvidenceFile(argc, argv), result);
        std::cout << "SemanticChurnPerformanceTests passed (" << checks << " checks, "
                  << result.snapshotNodeCount << " snapshot nodes, "
                  << result.semanticNodeCount << " semantic nodes, "
                  << result.updateCount << " updates, projection p50/p95/max "
                  << result.projectionP50Milliseconds << '/'
                  << result.projectionP95Milliseconds << '/'
                  << result.projectionMaxMilliseconds << " ms)\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "SemanticChurnPerformanceTests failed after " << checks
                  << " checks: " << error.what() << '\n';
        return 1;
    }
}
