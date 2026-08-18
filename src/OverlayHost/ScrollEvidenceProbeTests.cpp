#include "ScrollEvidenceProbe.h"

#include "DeclarativeRenderer.h"
#include "WidgetBridgeClient.h"

#include <Windows.h>

#include <cmath>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string>

namespace fs = std::filesystem;

namespace {

int checks{};

void Check(const bool condition, const char* message) {
    ++checks;
    if (!condition) throw std::runtime_error(message);
}

std::string ReadUtf8(const fs::path& path) {
    std::ifstream stream(path, std::ios::binary);
    return {std::istreambuf_iterator<char>(stream), std::istreambuf_iterator<char>()};
}

class TemporaryDirectory final {
public:
    TemporaryDirectory() {
        wchar_t buffer[MAX_PATH + 1]{};
        const auto length = GetTempPathW(MAX_PATH, buffer);
        if (length == 0 || length > MAX_PATH) throw std::runtime_error("GetTempPathW failed");
        path_ = fs::path(buffer) /
            (L"wrail-scroll-evidence-probe-" + std::to_wstring(GetCurrentProcessId()) +
             L"-" + std::to_wstring(GetTickCount64()));
        fs::create_directory(path_);
    }

    ~TemporaryDirectory() { std::error_code ignored; fs::remove_all(path_, ignored); }

    [[nodiscard]] const fs::path& path() const noexcept { return path_; }

private:
    fs::path path_;
};

widgetrail::WidgetSnapshot Snapshot(const long long sequence = 1) {
    widgetrail::WidgetSnapshot snapshot;
    snapshot.instanceId = L"audio-mixer.default";
    snapshot.activeInputScopeId = L"audio-mixer";
    snapshot.sequence = sequence;
    return snapshot;
}

widgetrail::RenderResult Result(
    const std::wstring_view focus,
    const float coordinate = 1.0F) {
    widgetrail::RenderResult result;
    result.navigationRects.emplace(
        std::wstring(focus), widgetrail::declarative::Rect{coordinate, 2.0F, 44.0F, 44.0F});
    result.focusRects.emplace(
        std::wstring(focus), widgetrail::declarative::Rect{coordinate, 3.0F, 44.0F, 43.5F});
    result.revealableFocusIds.emplace(focus);
    result.scrollOffsets.emplace(L"audio.root", coordinate * 10.0F);
    return result;
}

void DisabledProbeDoesNotWrite() {
    TemporaryDirectory temporary;
    const auto destination = temporary.path() / L"disabled.txt";
    widgetrail::ScrollEvidenceProbe probe;
    const auto snapshot = Snapshot();
    const auto result = Result(L"audio.master.volume.slider");
    Check(probe.Publish(
              L"audio-mixer", snapshot, result,
              L"audio.master.volume.slider", 1.0F, 1.0F) ==
              widgetrail::ScrollEvidencePublishResult::Disabled,
          "disabled probe reports disabled publication");
    Check(!fs::exists(destination), "disabled probe creates no evidence file");
    Check(!probe.RecordTarget(L"audio.master.volume.slider", L"restore"),
          "disabled probe retains no target state");
    Check(!probe.Enable({}), "empty evidence path is rejected");
    Check(probe.Enable(destination), "valid path enables the probe");
    Check(!probe.Enable(L"relative-evidence.txt"), "relative evidence path is rejected");
    Check(!probe.enabled(), "invalid reconfiguration disables the prior destination");
}

void InvalidAndUnavailableFramesFailClosed() {
    TemporaryDirectory temporary;
    widgetrail::ScrollEvidenceProbe unavailable;
    const auto missing = temporary.path() / L"missing" / L"evidence.txt";
    Check(unavailable.Enable(missing), "absolute unavailable destination is admitted");
    const auto snapshot = Snapshot();
    const auto result = Result(L"audio.master.volume.slider");
    Check(unavailable.Publish(
              L"audio-mixer", snapshot, result,
              L"audio.master.volume.slider", 1.0F, 1.0F) ==
              widgetrail::ScrollEvidencePublishResult::UnavailablePath,
          "unavailable parent fails publication without creating directories");
    Check(!fs::exists(missing), "unavailable publication leaves no destination");

    widgetrail::ScrollEvidenceProbe bounded;
    const auto destination = temporary.path() / L"bounded.txt";
    Check(bounded.Enable(destination), "valid absolute evidence path is admitted");
    auto oversized = snapshot;
    oversized.instanceId.assign(4'097, L'x');
    Check(bounded.Publish(
              L"audio-mixer", oversized, result,
              L"audio.master.volume.slider", 1.0F, 1.0F) ==
              widgetrail::ScrollEvidencePublishResult::InvalidFrame,
          "oversized field fails closed");
    Check(bounded.Publish(
              L"audio-mixer", snapshot, result,
              L"audio.master.volume.slider",
              std::numeric_limits<float>::quiet_NaN(), 1.0F) ==
              widgetrail::ScrollEvidencePublishResult::InvalidFrame,
          "non-finite scale fails closed");
    Check(!fs::exists(destination), "invalid frames never create the destination");
}

void AtomicReplacementSerializesCurrentTarget() {
    TemporaryDirectory temporary;
    const auto destination = temporary.path() / L"current.txt";
    widgetrail::ScrollEvidenceProbe probe;
    Check(probe.Enable(destination), "valid evidence destination enables probe");

    const auto focus = std::wstring(L"audio.session.01.volume.slider");
    auto snapshot = Snapshot(1);
    auto result = Result(focus, 1.0F);
    Check(probe.Publish(
              L"audio-mixer", snapshot, result, focus, 1.25F, 1.0F) ==
              widgetrail::ScrollEvidencePublishResult::Published,
          "first current frame publishes");
    const auto first = ReadUtf8(destination);
    Check(first.find("sequence=1\n") != std::string::npos,
          "first frame records its sequence");
    Check(first.find("explicitTarget=audio.session.01.volume.slider\n") !=
              std::string::npos,
          "unset target falls back to current rendered focus");

    Check(probe.RecordTarget(L"audio.session.02.volume.slider", L"down"),
          "valid target and direction update is retained");
    snapshot.sequence = 2;
    result = Result(focus, 9.0F);
    Check(probe.Publish(
              L"audio-mixer", snapshot, result, focus, 1.5F, 1.25F) ==
              widgetrail::ScrollEvidencePublishResult::Published,
          "replacement current frame publishes");
    const auto second = ReadUtf8(destination);
    Check(second.find("sequence=2\n") != std::string::npos &&
              second.find("sequence=1\n") == std::string::npos,
          "atomic replacement exposes only the current sequence");
    Check(second.find("explicitTarget=audio.session.02.volume.slider\n") !=
              std::string::npos && second.find("direction=down\n") != std::string::npos,
          "replacement records the exact explicit target and direction");
    Check(second.find("navigation=9.000000,2.000000,44.000000,44.000000\n") !=
              std::string::npos,
          "replacement serializes current-frame navigation geometry");

    Check(!probe.RecordTarget(L"invalid\ntarget", L"up"),
          "invalid target update is rejected");
    auto invalid = snapshot;
    invalid.instanceId.assign(4'097, L'y');
    Check(probe.Publish(
              L"audio-mixer", invalid, result, focus, 1.5F, 1.25F) ==
              widgetrail::ScrollEvidencePublishResult::InvalidFrame,
          "invalid replacement is rejected");
    Check(ReadUtf8(destination) == second,
          "failed replacement preserves the last complete current frame");
    Check(!fs::exists(destination.wstring() + L".tmp-" +
                      std::to_wstring(GetCurrentProcessId())),
          "atomic publication leaves no temporary sidecar");

    Check(probe.RecordTarget(L"audio.session.01.volume.slider", L"up"),
          "later reverse target update replaces prior state");
    snapshot.sequence = 3;
    Check(probe.Publish(
              L"audio-mixer", snapshot, result, focus, 1.5F, 1.25F) ==
              widgetrail::ScrollEvidencePublishResult::Published,
          "reverse target frame publishes");
    const auto third = ReadUtf8(destination);
    Check(third.find("explicitTarget=audio.session.01.volume.slider\n") !=
              std::string::npos && third.find("direction=up\n") != std::string::npos,
          "target and direction updates are replacement-owned");
}

void AuthoredUpTargetSerializesBeforeInput() {
    TemporaryDirectory temporary;
    const auto destination = temporary.path() / L"edge.txt";
    widgetrail::ScrollEvidenceProbe probe;
    Check(probe.Enable(destination), "edge evidence destination enables probe");
    auto snapshot = Snapshot();
    snapshot.root.id = L"audio.root";
    snapshot.root.kind = L"scroll";
    widgetrail::WidgetNode microphone;
    microphone.id = L"audio.input.volume.slider";
    microphone.kind = L"slider";
    microphone.focusUp = L"audio.master.volume.slider";
    widgetrail::WidgetNode master;
    master.id = L"audio.master.volume.slider";
    master.kind = L"slider";
    snapshot.root.children = {master, microphone};
    auto result = Result(microphone.id, 4.0F);
    result.navigationRects.emplace(
        master.id, widgetrail::declarative::Rect{4.0F, -52.0F, 44.0F, 44.0F});
    result.navigationEnabled.emplace(master.id, true);
    result.revealableFocusIds.emplace(master.id);
    Check(probe.Publish(
              L"audio-mixer", snapshot, result, microphone.id, 1.25F, 1.0F) ==
              widgetrail::ScrollEvidencePublishResult::Published,
          "authored edge frame publishes before input");
    const auto payload = ReadUtf8(destination);
    Check(payload.find("upTarget=audio.master.volume.slider\n") !=
              std::string::npos,
          "authored Up target is serialized exactly");
    Check(payload.find("upRevealable=true\n") != std::string::npos,
          "authored Up target retains native revealability");
    Check(payload.find("upNavigation=4.000000,-52.000000,44.000000,44.000000\n") !=
              std::string::npos,
          "authored Up target retains offscreen logical geometry");
}

} // namespace

int main() {
    try {
        DisabledProbeDoesNotWrite();
        InvalidAndUnavailableFramesFailClosed();
        AtomicReplacementSerializesCurrentTarget();
        AuthoredUpTargetSerializesBeforeInput();
        std::cout << "ScrollEvidenceProbeTests passed (" << checks << " checks)\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "ScrollEvidenceProbeTests failed: " << error.what() << '\n';
        return 1;
    }
}
