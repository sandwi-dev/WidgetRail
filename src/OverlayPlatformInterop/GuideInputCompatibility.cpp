#include "GuideInputCompatibility.h"

#include <array>
#include <filesystem>
#include <atomic>
#include <memory>
#include <mutex>

namespace widgetrail::input {
namespace {

constexpr WORD kGuideButtonBit = 0x0400;

HMODULE LoadSystemXInput() noexcept {
    if (const auto module = LoadLibraryExW(
            L"xinput1_4.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32)) {
        return module;
    }
    std::array<wchar_t, MAX_PATH> systemDirectory{};
    const auto length = GetSystemDirectoryW(
        systemDirectory.data(), static_cast<UINT>(systemDirectory.size()));
    if (length == 0 || length >= systemDirectory.size()) return nullptr;
    const auto path = std::filesystem::path(
        std::wstring_view(systemDirectory.data(), length)) / L"xinput1_4.dll";
    return LoadLibraryW(path.c_str());
}

} // namespace

std::uint8_t GuideEdgeTracker::Update(
    const std::array<bool, XUSER_MAX_COUNT>& pressed) noexcept {
    if (!primed_) {
        previous_ = pressed;
        primed_ = true;
        return 0;
    }
    std::uint8_t rising{};
    for (std::size_t slot = 0; slot < pressed.size(); ++slot) {
        if (pressed[slot] && !previous_[slot]) {
            rising |= static_cast<std::uint8_t>(1U << slot);
        }
    }
    previous_ = pressed;
    return rising;
}

bool GuideCompatibilityActivation::Update(
    const DeviceId& deviceId,
    const bool connected) {
    const bool wasActive = active();
    if (connected) {
        devices_.insert(deviceId);
    } else {
        devices_.erase(deviceId);
    }
    return wasActive != active();
}

XInputGuideCompatibility::~XInputGuideCompatibility() {
    Shutdown();
}

struct GuidePollDispatcher::State final {
    explicit State(std::function<std::uint8_t()> callback) : poll(std::move(callback)) {}
    std::function<std::uint8_t()> poll;
    std::atomic<bool> stopped{};
    std::atomic<bool> outstanding{};
    std::mutex resultMutex;
    std::uint8_t edges{};
    ULONGLONG completedAt{};
};

GuidePollDispatcher::GuidePollDispatcher(std::function<std::uint8_t()> poll)
    : state_(std::make_shared<State>(std::move(poll))) {}

GuidePollDispatcher::~GuidePollDispatcher() {
    state_->stopped.store(true);
}

void CALLBACK GuidePollDispatcher::Run(PTP_CALLBACK_INSTANCE, void* context) noexcept {
    const std::unique_ptr<std::shared_ptr<State>> owner(
        static_cast<std::shared_ptr<State>*>(context));
    const auto& state = *owner;
    if (!state->stopped.load()) {
        try {
            const auto started = GetTickCount64();
            const auto edges = state->poll();
            const auto completed = GetTickCount64();
            // A driver recovering from a long stall must not replay an old
            // toggle gesture. The edge tracker is still primed by this scan.
            if (!state->stopped.load() && completed - started <= 250) {
                std::scoped_lock lock(state->resultMutex);
                state->edges |= edges;
                state->completedAt = completed;
            }
        } catch (...) { }
    }
    state->outstanding.store(false);
}

std::uint8_t GuidePollDispatcher::Poll() noexcept {
    std::uint8_t edges{};
    {
        std::scoped_lock lock(state_->resultMutex);
        if (GetTickCount64() - state_->completedAt <= 250) edges = state_->edges;
        state_->edges = 0;
    }
    Request();
    return edges;
}

void GuidePollDispatcher::Request() noexcept {
    if (state_->outstanding.exchange(true)) return;
    try {
        auto owner = std::make_unique<std::shared_ptr<State>>(state_);
        // The interop DLL may be retired while a driver is blocked. Windows
        // holds its callback library reference until this work actually exits.
        HMODULE callbackModule{};
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
                reinterpret_cast<LPCWSTR>(&GuidePollDispatcher::Run), &callbackModule)) {
            state_->outstanding.store(false);
            return;
        }
        TP_CALLBACK_ENVIRON environment;
        InitializeThreadpoolEnvironment(&environment);
        SetThreadpoolCallbackLibrary(&environment, callbackModule);
        SetThreadpoolCallbackRunsLong(&environment);
        const auto submitted = TrySubmitThreadpoolCallback(Run, owner.get(), &environment);
        DestroyThreadpoolEnvironment(&environment);
        FreeLibrary(callbackModule);
        if (submitted) (void)owner.release();
        else state_->outstanding.store(false);
    } catch (...) { state_->outstanding.store(false); }
}

struct XInputGuideCompatibility::Source final {
    using GetState = DWORD(WINAPI*)(DWORD, XINPUT_STATE*);
    HMODULE module{LoadSystemXInput()};
    GetState getStateEx = module ? reinterpret_cast<GetState>(
        GetProcAddress(module, reinterpret_cast<LPCSTR>(100))) : nullptr;
    GetState getState = module ? reinterpret_cast<GetState>(
        GetProcAddress(module, "XInputGetState")) : nullptr;
    GuideEdgeTracker edges;
    std::mutex mutex;
    std::array<XINPUT_STATE, XUSER_MAX_COUNT> cached{};
    std::array<bool, XUSER_MAX_COUNT> connected{};
    ULONGLONG completedAt{};
    ULONGLONG sampledAt{};
    ~Source() { if (module) FreeLibrary(module); }
    std::uint8_t Poll() {
        std::array<XINPUT_STATE, XUSER_MAX_COUNT> states{};
        std::array<bool, XUSER_MAX_COUNT> live{}, pressed{};
        const auto started = GetTickCount64();
        const auto read = getStateEx ? getStateEx : getState;
        for (DWORD slot = 0; slot < XUSER_MAX_COUNT; ++slot) {
            live[slot] = read(slot, &states[slot]) == ERROR_SUCCESS;
            pressed[slot] = live[slot] && getStateEx &&
                (states[slot].Gamepad.wButtons & kGuideButtonBit) != 0;
            states[slot].Gamepad.wButtons &= ~kGuideButtonBit;
        }
        const auto completed = GetTickCount64();
        if (completed - started <= 250) {
            std::scoped_lock lock(mutex);
            cached = states;
            connected = live;
            completedAt = completed;
            sampledAt = started;
        }
        return edges.Update(pressed);
    }
};

bool XInputGuideCompatibility::Initialize() noexcept {
    Shutdown();
    try {
        auto source = std::make_shared<Source>();
        if (!source->getState && !source->getStateEx) return false;
        source_ = source;
        guideAvailable_ = source->getStateEx != nullptr;
        poller_ = std::make_unique<GuidePollDispatcher>([source] { return source->Poll(); });
        poller_->Request();
        return guideAvailable_;
    } catch (...) { Shutdown(); return false; }
}

void XInputGuideCompatibility::Shutdown() noexcept {
    poller_.reset();
    source_.reset();
    guideAvailable_ = false;
}

std::uint8_t XInputGuideCompatibility::PollRisingEdges() noexcept {
    return poller_ ? poller_->Poll() : 0;
}

bool XInputGuideCompatibility::TryReadState(
    DWORD slot, XINPUT_STATE& state, std::uint64_t& sampledAtMilliseconds) noexcept {
    sampledAtMilliseconds = 0;
    state = {};
    if (!poller_ || !source_ || slot >= XUSER_MAX_COUNT) return false;
    poller_->Request();
    std::scoped_lock lock(source_->mutex);
    if (GetTickCount64() - source_->completedAt > 250 || !source_->connected[slot]) return false;
    state = source_->cached[slot];
    sampledAtMilliseconds = source_->sampledAt;
    return true;
}

} // namespace widgetrail::input
