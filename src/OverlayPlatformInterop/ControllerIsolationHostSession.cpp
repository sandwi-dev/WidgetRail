#include "ControllerIsolationHostSession.h"

#include "ControllerIsolationInputTransport.h"
#include "ControllerIsolationReader.h"
#include "ControllerIsolationRoutingSession.h"
#include "HidHideConfigurationAdapter.h"
#include "ViGEmOutputAdapter.h"

#include <windows.h>
#include <shlobj.h>

#include <array>
#include <condition_variable>
#include <deque>
#include <filesystem>
#include <fstream>
#include <mutex>
#include <set>
#include <stop_token>
#include <thread>
#include <utility>
#include <vector>

namespace widgetrail::isolation {
namespace {

constexpr std::size_t kMaximumJournalBytes = 128 * 1024;
constexpr std::size_t kMaximumJournalEntries = 512;
constexpr std::uint64_t kMeasuredReadingIntervalMilliseconds = 10;

[[nodiscard]] std::filesystem::path CurrentExecutable() {
    std::array<wchar_t, 32'768> path{};
    const auto length = GetModuleFileNameW(nullptr, path.data(),
                                           static_cast<DWORD>(path.size()));
    return length == 0 || length >= path.size()
        ? std::filesystem::path{}
        : std::filesystem::path(std::wstring_view(path.data(), length));
}

[[nodiscard]] std::filesystem::path LocalJournalPath() {
    PWSTR localAppData{};
    if (FAILED(SHGetKnownFolderPath(FOLDERID_LocalAppData, KF_FLAG_DEFAULT,
                                    nullptr, &localAppData)) || !localAppData)
        return {};
    std::filesystem::path result(localAppData);
    CoTaskMemFree(localAppData);
    return result / L"WidgetRail" / L"controller-isolation" /
        L"local-session.v1";
}

[[nodiscard]] std::filesystem::path LegacyJournalPath() {
    auto path = LocalJournalPath();
    return path.empty() ? path : path.parent_path() / L"session.bin";
}

[[nodiscard]] std::string Hex(const std::wstring& value) {
    constexpr char digits[] = "0123456789abcdef";
    std::string result;
    const auto* bytes = reinterpret_cast<const unsigned char*>(value.data());
    result.reserve(value.size() * sizeof(wchar_t) * 2);
    for (std::size_t i = 0; i < value.size() * sizeof(wchar_t); ++i) {
        result.push_back(digits[bytes[i] >> 4]);
        result.push_back(digits[bytes[i] & 15]);
    }
    return result;
}

[[nodiscard]] bool Unhex(const std::string& value, std::wstring& result) {
    if (value.empty() || value.size() % (sizeof(wchar_t) * 2) != 0 ||
        value.size() > 32'768 * sizeof(wchar_t) * 2) return false;
    const auto digit = [](char c) -> int {
        if (c >= '0' && c <= '9') return c - '0';
        if (c >= 'a' && c <= 'f') return c - 'a' + 10;
        return -1;
    };
    std::vector<unsigned char> bytes(value.size() / 2);
    for (std::size_t i = 0; i < bytes.size(); ++i) {
        const auto high = digit(value[i * 2]);
        const auto low = digit(value[i * 2 + 1]);
        if (high < 0 || low < 0) return false;
        bytes[i] = static_cast<unsigned char>((high << 4) | low);
    }
    result.assign(reinterpret_cast<const wchar_t*>(bytes.data()),
                  bytes.size() / sizeof(wchar_t));
    return result.find(L'\0') == std::wstring::npos;
}

[[nodiscard]] bool SaveJournal(const std::filesystem::path& path,
                               const HidHideJournal& journal,
                               std::wstring& diagnostic) {
    std::string text = "WidgetRailControllerIsolationLocal=1\n";
    text += std::string("before-active=") + (journal.before.active ? "1\n" : "0\n");
    text += std::string("before-inverse=") +
        (journal.before.applicationListInverted ? "1\n" : "0\n");
    text += std::string("activated=") + (journal.activatedBySession ? "1\n" : "0\n");
    const auto append = [&](const char* prefix,
                            const std::set<std::wstring>& values) {
        for (const auto& value : values) text += prefix + Hex(value) + "\n";
    };
    append("before-app=", journal.before.applicationPaths);
    append("before-device=", journal.before.deviceInstanceIds);
    append("owned-app=", journal.ownedApplicationPaths);
    append("owned-device=", journal.ownedDeviceInstanceIds);
    if (text.size() > kMaximumJournalBytes) {
        diagnostic = L"Controller isolation journal exceeds its bound.";
        return false;
    }
    std::error_code ec;
    std::filesystem::create_directories(path.parent_path(), ec);
    if (ec) { diagnostic = L"Controller isolation journal directory failed."; return false; }
    auto temporary = path; temporary += L".tmp";
    HANDLE file = CreateFileW(temporary.c_str(), GENERIC_WRITE, 0, nullptr,
                              CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) {
        diagnostic = L"Controller isolation journal creation failed."; return false;
    }
    DWORD written{};
    const bool writtenAll = WriteFile(file, text.data(),
        static_cast<DWORD>(text.size()), &written, nullptr) &&
        written == text.size() && FlushFileBuffers(file);
    CloseHandle(file);
    if (!writtenAll || !MoveFileExW(temporary.c_str(), path.c_str(),
            MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
        diagnostic = L"Controller isolation journal commit failed."; return false;
    }
    return true;
}

[[nodiscard]] bool LoadJournal(const std::filesystem::path& path,
                               HidHideJournal& journal, bool& found,
                               std::wstring& diagnostic) {
    found = std::filesystem::exists(path);
    if (!found) return true;
    std::error_code ec;
    const auto size = std::filesystem::file_size(path, ec);
    if (ec || size == 0 || size > kMaximumJournalBytes) {
        diagnostic = L"Controller isolation local journal is invalid."; return false;
    }
    std::ifstream input(path, std::ios::binary);
    std::string text(static_cast<std::size_t>(size), '\0');
    if (!input.read(text.data(), static_cast<std::streamsize>(text.size())) ||
        !text.starts_with("WidgetRailControllerIsolationLocal=1\n")) {
        diagnostic = L"Controller isolation local journal is unrecognized."; return false;
    }
    journal = {};
    bool active{}, inverse{}, activated{}, sawActive{}, sawInverse{}, sawActivated{};
    std::size_t entries{};
    for (std::size_t cursor = text.find('\n') + 1; cursor < text.size();) {
        const auto end = text.find('\n', cursor);
        if (end == std::string::npos || ++entries > kMaximumJournalEntries) return false;
        const auto line = text.substr(cursor, end - cursor); cursor = end + 1;
        const auto boolean = [&](const char* key, bool& target, bool& saw) {
            if (!line.starts_with(key)) return false;
            const auto value = line.substr(std::char_traits<char>::length(key));
            if (saw || (value != "0" && value != "1")) return false;
            target = value == "1"; saw = true; return true;
        };
        if (boolean("before-active=", active, sawActive) ||
            boolean("before-inverse=", inverse, sawInverse) ||
            boolean("activated=", activated, sawActivated)) continue;
        const auto add = [&](const char* key, std::set<std::wstring>& values) {
            if (!line.starts_with(key)) return false;
            std::wstring decoded;
            return Unhex(line.substr(std::char_traits<char>::length(key)), decoded) &&
                   values.insert(std::move(decoded)).second;
        };
        if (add("before-app=", journal.before.applicationPaths) ||
            add("before-device=", journal.before.deviceInstanceIds) ||
            add("owned-app=", journal.ownedApplicationPaths) ||
            add("owned-device=", journal.ownedDeviceInstanceIds)) continue;
        diagnostic = L"Controller isolation local journal has an unknown field.";
        return false;
    }
    if (!sawActive || !sawInverse || !sawActivated) return false;
    journal.before.active = active;
    journal.before.applicationListInverted = inverse;
    journal.activatedBySession = activated;
    return true;
}

class LocalOutput final : public ControllerIsolationOutput {
public:
    LocalOutput() noexcept : adapter_(OfficialViGEmApi()) {}
    bool OpenOwnedTarget() noexcept override { return adapter_.Open(); }
    bool Submit(const GamepadState& state) noexcept override { return adapter_.Submit(state); }
    bool TakeLatestFeedback(ControllerRumbleState& state) noexcept override {
        return adapter_.TakeLatestFeedback(state);
    }
    void RemoveOwnedTarget() noexcept override { adapter_.RemoveOwnedTarget(); }
private:
    ViGEmOutputAdapter adapter_;
};

class LocalQueues final : public ControllerIsolationGuideSink,
                          public ControllerIsolationHostInputSink {
public:
    bool BeginInteraction() noexcept { std::scoped_lock l(mutex_); return input_.BeginInteraction(); }
    void RetireInteraction() noexcept { std::scoped_lock l(mutex_); input_.RetireInteraction(); }
    bool PublishInput(const GamepadState& state, std::uint64_t ordinal) noexcept override {
        std::scoped_lock l(mutex_); return input_.Push(state, ordinal);
    }
    bool PublishGuide(bool pressed, std::uint64_t, std::uint64_t ordinal) noexcept override {
        std::scoped_lock l(mutex_);
        if (guides_.size() == 32) return false;
        guides_.push_back(ordinal | (pressed ? 0ULL : (1ULL << 63))); return true;
    }
    bool Pop(GamepadState& state, std::uint32_t& remaining) noexcept {
        std::scoped_lock l(mutex_);
        const auto next = input_.TakeNext();
        if (!next) return false;
        state = *next;
        remaining = static_cast<std::uint32_t>(input_.size()); return true;
    }
    std::uint64_t TakeGuide() noexcept {
        std::scoped_lock l(mutex_);
        if (guides_.empty()) return 0;
        const auto result = guides_.front(); guides_.pop_front(); return result;
    }
private:
    std::mutex mutex_;
    ControllerIsolationInputEventQueue input_;
    std::deque<std::uint64_t> guides_;
};

[[nodiscard]] LocalControllerProgress ConvertProgress(ControllerIsolationRoutingState state) {
    switch (state) {
    case ControllerIsolationRoutingState::PreparedNeutral: return LocalControllerProgress::Preparing;
    case ControllerIsolationRoutingState::AwaitingPlaying: return LocalControllerProgress::AwaitingNeutral;
    case ControllerIsolationRoutingState::Playing: return LocalControllerProgress::Playing;
    case ControllerIsolationRoutingState::OverlayInteraction: return LocalControllerProgress::Contained;
    case ControllerIsolationRoutingState::Fault: return LocalControllerProgress::Fault;
    case ControllerIsolationRoutingState::Disabled: return LocalControllerProgress::Disabled;
    }
    return LocalControllerProgress::Fault;
}

} // namespace

struct ControllerIsolationHostSession::Impl final {
    std::mutex mutex;
    std::condition_variable changed;
    std::jthread thread;
    LocalQueues queues;
    LocalControllerProgress progress{LocalControllerProgress::Disabled};
    GamepadState latest{};
    std::wstring diagnostic;
    bool startupDone{}, enterRequested{}, closeRequested{};

    void FinishStartup(const std::wstring& message) noexcept {
        std::scoped_lock lock(mutex);
        diagnostic = message; progress = LocalControllerProgress::Fault;
        startupDone = true; changed.notify_all();
    }

    static bool Restore(const HidHideJournal& journal,
                        HidHideConfigurationAdapter& adapter,
                        const std::filesystem::path& path) noexcept {
        HidHideSnapshot current, observed; std::uint32_t error{};
        if (adapter.ReadSnapshot(current, error) != HidHideConfigurationStatus::Ready) return false;
        const auto restore = PlanHidHideRestore(journal, current);
        if (!restore.desired || restore.status == HidHideRestoreStatus::Conflict) return false;
        return adapter.ApplySnapshot(current, *restore.desired, observed, error) ==
                   HidHideConfigurationStatus::Ready && DeleteFileW(path.c_str());
    }

    void Run(std::stop_token stop) noexcept {
        const auto journalPath = LocalJournalPath();
        if (journalPath.empty() || std::filesystem::exists(LegacyJournalPath())) {
            FinishStartup(std::filesystem::exists(LegacyJournalPath())
                ? L"A legacy controller-isolation journal requires explicit recovery."
                : L"Controller isolation LocalAppData is unavailable."); return;
        }
        HidHideConfigurationAdapter hidhide;
        HidHideJournal prior; bool priorFound{}; std::wstring failure;
        if (!LoadJournal(journalPath, prior, priorFound, failure)) { FinishStartup(failure); return; }
        if (priorFound && !Restore(prior, hidhide, journalPath)) {
            FinishStartup(L"Controller isolation crash recovery found foreign drift or failed."); return;
        }
        SelectedControllerDescriptor descriptor;
        if (DiscoverCurrentPhysicalController(GetTickCount64() | 1, descriptor) !=
            SelectedControllerDiscoveryStatus::Ready) {
            FinishStartup(L"Controller isolation requires exactly one known physical controller."); return;
        }
        HidHideSnapshot before; std::uint32_t error{}; std::wstring executableDevicePath;
        if (hidhide.ReadSnapshot(before, error) != HidHideConfigurationStatus::Ready ||
            !ControllerIsolationDosDevicePath(CurrentExecutable().wstring(), executableDevicePath, error)) {
            FinishStartup(L"Controller isolation HidHide preflight failed."); return;
        }
        const auto plan = PlanHidHideApply(before, executableDevicePath,
            {std::wstring(descriptor.deviceInstanceId.view())});
        if (!plan.plan || !SaveJournal(journalPath, plan.plan->journal, failure)) {
            FinishStartup(failure.empty() ? L"Controller isolation policy plan was rejected." : failure); return;
        }
        HidHideSnapshot observed;
        if (hidhide.ApplySnapshot(before, plan.plan->desired, observed, error) !=
            HidHideConfigurationStatus::Ready) {
            if (Restore(plan.plan->journal, hidhide, journalPath)) {
                FinishStartup(L"Controller isolation HidHide apply failed and was restored.");
            } else {
                FinishStartup(L"Controller isolation HidHide apply failed; recovery journal retained.");
            }
            return;
        }
        auto source = CreateGameInputSelectedControllerReader(); LocalOutput output;
        if (!source) { FinishStartup(L"Controller isolation reader allocation failed."); return; }
        ControllerIsolationRoutingSession routing(*source, output, queues, {}, &queues);
        const RoutingAuthority authority{1, 1, 1, GetTickCount64() | 1};
        auto result = routing.PrepareSession(authority, descriptor.enrollment, GetTickCount64());
        if (result == ControllerIsolationRoutingResult::Applied)
            result = routing.CommitPlaying(authority, kMeasuredReadingIntervalMilliseconds, GetTickCount64());
        const auto startupAt = GetTickCount64();
        while (!stop.stop_requested() && routing.state() != ControllerIsolationRoutingState::Playing &&
               result != ControllerIsolationRoutingResult::Faulted &&
               GetTickCount64() - startupAt < 2'000) {
            result = routing.Pump(GetTickCount64()); Sleep(1);
        }
        if (routing.state() != ControllerIsolationRoutingState::Playing) {
            if (routing.state() != ControllerIsolationRoutingState::Disabled) (void)routing.Stop(authority);
            (void)Restore(plan.plan->journal, hidhide, journalPath);
            FinishStartup(L"Controller isolation could not enter gameplay routing."); return;
        }
        { std::scoped_lock lock(mutex); progress = LocalControllerProgress::Playing;
          startupDone = true; changed.notify_all(); }
        while (!stop.stop_requested()) {
            bool enter{}, close{};
            { std::scoped_lock lock(mutex); enter = std::exchange(enterRequested, false);
              close = std::exchange(closeRequested, false); }
            if (enter && routing.state() == ControllerIsolationRoutingState::Playing) {
                if (!queues.BeginInteraction() || routing.EnterOverlay(authority, GetTickCount64()) ==
                    ControllerIsolationRoutingResult::Faulted) break;
            }
            if (close && routing.state() == ControllerIsolationRoutingState::OverlayInteraction) {
                queues.RetireInteraction();
                if (routing.CloseOverlay(authority, kMeasuredReadingIntervalMilliseconds,
                                         GetTickCount64()) == ControllerIsolationRoutingResult::Faulted) break;
            }
            result = routing.Pump(GetTickCount64());
            { std::scoped_lock lock(mutex); progress = ConvertProgress(routing.state());
              latest = routing.currentState(); changed.notify_all(); }
            if (result == ControllerIsolationRoutingResult::Faulted) break;
            Sleep(1);
        }
        queues.RetireInteraction();
        if (routing.state() != ControllerIsolationRoutingState::Disabled) (void)routing.Stop(authority);
        if (!Restore(plan.plan->journal, hidhide, journalPath)) {
            std::scoped_lock lock(mutex);
            diagnostic = L"Controller isolation shutdown recovery failed; journal retained.";
            progress = LocalControllerProgress::Fault; changed.notify_all(); return;
        }
        std::scoped_lock lock(mutex); progress = LocalControllerProgress::Disabled; changed.notify_all();
    }
};

ControllerIsolationHostSession::ControllerIsolationHostSession() noexcept
    : impl_(std::make_unique<Impl>()) {}
ControllerIsolationHostSession::~ControllerIsolationHostSession() { Stop(); }

bool ControllerIsolationHostSession::Start(bool enabled, std::wstring& diagnostic) noexcept {
    if (!enabled) return false;
    impl_->thread = std::jthread([this](std::stop_token stop) { impl_->Run(stop); });
    std::unique_lock lock(impl_->mutex);
    impl_->changed.wait(lock, [this] { return impl_->startupDone; });
    diagnostic = impl_->diagnostic;
    return impl_->progress == LocalControllerProgress::Playing;
}

bool ControllerIsolationHostSession::PrepareOverlay(std::wstring& diagnostic) noexcept {
    std::unique_lock lock(impl_->mutex);
    if (impl_->progress == LocalControllerProgress::Fault) { diagnostic = impl_->diagnostic; return false; }
    impl_->enterRequested = true;
    (void)impl_->changed.wait_for(lock, std::chrono::milliseconds(500), [this] {
        return impl_->progress == LocalControllerProgress::Contained ||
               impl_->progress == LocalControllerProgress::Fault;
    });
    diagnostic = impl_->diagnostic;
    return impl_->progress == LocalControllerProgress::Contained;
}

void ControllerIsolationHostSession::CloseOverlay() noexcept {
    std::scoped_lock lock(impl_->mutex); impl_->closeRequested = true;
}

bool ControllerIsolationHostSession::Poll(ControllerIsolationHostReading& reading,
                                           std::wstring& diagnostic) noexcept {
    reading = {};
    std::scoped_lock lock(impl_->mutex);
    reading.progress = impl_->progress; reading.state = impl_->latest;
    (void)impl_->queues.Pop(reading.state, reading.remainingInputStates);
    reading.guideEvent = impl_->queues.TakeGuide(); diagnostic = impl_->diagnostic;
    return impl_->progress != LocalControllerProgress::Fault;
}

bool ControllerIsolationHostSession::PollGuide(std::uint64_t& guideEvent,
                                                std::wstring& diagnostic) noexcept {
    std::scoped_lock lock(impl_->mutex);
    guideEvent = impl_->queues.TakeGuide(); diagnostic = impl_->diagnostic;
    return impl_->progress != LocalControllerProgress::Fault;
}

bool ControllerIsolationHostSession::active() const noexcept {
    std::scoped_lock lock(impl_->mutex);
    return impl_->progress != LocalControllerProgress::Disabled &&
           impl_->progress != LocalControllerProgress::Fault;
}

void ControllerIsolationHostSession::Stop() noexcept {
    if (impl_->thread.joinable()) { impl_->thread.request_stop(); impl_->thread.join(); }
}

} // namespace widgetrail::isolation
