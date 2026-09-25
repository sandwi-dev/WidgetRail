#pragma once

#include <atomic>
#include <chrono>
#include <cstdint>
#include <deque>
#include <filesystem>
#include <fstream>
#include <iomanip>
#include <locale>
#include <mutex>
#include <sstream>
#include <string>
#include <string_view>
#include <vector>

namespace widgetrail {

// Opt-in process-local evidence only. No IO on Record; callers supply only
// numbers, closed reason codes, and opaque identifiers, never provider content.
class ScrollDiagnostics final {
public:
    static constexpr std::size_t MaximumRecords = 8192;
    static constexpr std::size_t MaximumRecordBytes = 1024;
    using Clock = std::chrono::steady_clock;
    struct Capture final {
        std::vector<std::string> records;
        std::uint64_t dropped{}, failures{}, recordingUs{}, logUs{}, logCalls{}, suppressedLogs{};
    };

    template<class Write>
    void Record(std::string_view event, Write&& write) noexcept {
        const auto started = Clock::now();
        try {
            std::ostringstream line;
            line.imbue(std::locale::classic());
            line << std::fixed << std::setprecision(4);
            line << "t-us=" << Micros(started - started_) << " event=" << event << ' ';
            write(line);
            auto value = line.str();
            if (value.size() > MaximumRecordBytes) {
                value.resize(MaximumRecordBytes - 15);
                value += " truncated=true";
            }
            std::lock_guard lock(mutex_);
            if (records_.size() == MaximumRecords) { records_.pop_front(); ++dropped_; }
            records_.push_back(std::move(value));
        } catch (...) { ++failures_; }
        recordingUs_ += Micros(Clock::now() - started);
    }

    [[nodiscard]] Capture Snapshot() const {
        std::lock_guard lock(mutex_);
        return {{records_.begin(), records_.end()}, dropped_, failures_.load(), recordingUs_.load(),
            logUs.load(), logCalls.load(), suppressedLogs.load()};
    }

    // Called after hiding or on shutdown, never from the scrolling paint path.
    [[nodiscard]] bool Save(const std::filesystem::path& path) const noexcept {
        try {
            const auto capture = Snapshot();
            std::filesystem::create_directories(path.parent_path());
            std::ofstream output(path, std::ios::binary | std::ios::trunc);
            output << "scroll-trace-v1 records=" << capture.records.size() << " dropped=" << capture.dropped
                << " failures=" << capture.failures << " record-us=" << capture.recordingUs
                << " normal-log-us=" << capture.logUs << " normal-log-calls=" << capture.logCalls
                << " suppressed-logs=" << capture.suppressedLogs << '\n';
            for (const auto& record : capture.records) output << record << '\n';
            output.flush();
            return output.good();
        } catch (...) { return false; }
    }

    template<class Duration>
    static std::uint64_t Micros(Duration duration) noexcept {
        return static_cast<std::uint64_t>(std::chrono::duration_cast<std::chrono::microseconds>(duration).count());
    }

    std::atomic<std::uint64_t> logUs{}, logCalls{}, suppressedLogs{};
private:
    const Clock::time_point started_{Clock::now()};
    mutable std::mutex mutex_;
    std::deque<std::string> records_;
    std::uint64_t dropped_{};
    std::atomic<std::uint64_t> failures_{}, recordingUs_{};
};

// Measures ordinary synchronous logging separately from diagnostic recording.
struct ScrollDiagnosticLogTimer final {
    ScrollDiagnostics* trace{};
    ScrollDiagnostics::Clock::time_point started{};
    explicit ScrollDiagnosticLogTimer(ScrollDiagnostics* value) noexcept : trace(value) {
        if (trace) started = ScrollDiagnostics::Clock::now();
    }
    ~ScrollDiagnosticLogTimer() {
        if (trace) {
            trace->logUs += ScrollDiagnostics::Micros(ScrollDiagnostics::Clock::now() - started);
            ++trace->logCalls;
        }
    }
};

} // namespace widgetrail
