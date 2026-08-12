#include "LocalWidgetPackageImport.h"

#include <cstdlib>
#include <iostream>
#include <utility>

namespace {

using namespace gba::packages;

void Require(const bool condition, const char* message) {
    if (condition) return;
    std::cerr << "LocalWidgetPackageImportTests failed: " << message << '\n';
    std::exit(1);
}

LocalWidgetPackageOrigin SettingsOrigin() {
    return {
        L"settings", L"org.gbar.firstparty.settings", L"org.gbar.firstparty",
        L"settings.default", L"runtime-one", L"presentation-one",
        gba::WidgetLifecycleState::Interactive};
}

struct FakePicker final : ILocalWidgetPackagePicker {
    LocalWidgetPackagePickerResult result{
        LocalWidgetPackagePickerStatus::Selected, L"C:\\fixture.gbarwidget", {}};
    std::function<void()> duringSelect;
    bool open{};
    bool cancelled{};

    LocalWidgetPackagePickerResult Select(HWND) override {
        open = true;
        if (duringSelect) duringSelect();
        open = false;
        if (cancelled)
            return {LocalWidgetPackagePickerStatus::Cancelled};
        return result;
    }
    void Cancel() noexcept override { cancelled = true; }
    bool active() const noexcept override { return open; }
};

void ExactOriginAndQuietCancel() {
    auto origin = SettingsOrigin();
    Require(LocalWidgetPackageImport::Admit(origin), "exact Settings origin refused");
    origin.lifecycle = gba::WidgetLifecycleState::Visible;
    Require(!LocalWidgetPackageImport::Admit(origin), "non-Interactive origin admitted");
    origin = SettingsOrigin();
    origin.packageId = L"community.settings";
    Require(!LocalWidgetPackageImport::Admit(origin), "forged package origin admitted");

    FakePicker picker;
    picker.result = {LocalWidgetPackagePickerStatus::Cancelled};
    int submissions{};
    LocalWidgetPackageImport importer(
        picker, [] { return std::optional{SettingsOrigin()}; },
        [&](auto, const auto&, auto) { ++submissions; return true; });
    const auto cancelled = importer.Begin(reinterpret_cast<HWND>(1));
    Require(cancelled.status == LocalWidgetPackageImportStatus::Cancelled,
            "picker cancel was not quiet");
    Require(cancelled.safeMessage.empty() && submissions == 0,
            "cancel produced a message or submission");
}

void DuplicateAndCloseCancellation() {
    FakePicker picker;
    LocalWidgetPackageImport* importerPointer{};
    LocalWidgetPackageImportResult nested;
    LocalWidgetPackageImport importer(
        picker, [] { return std::optional{SettingsOrigin()}; },
        [](auto, const auto&, auto) { return true; });
    importerPointer = &importer;
    picker.duringSelect = [&] {
        nested = importerPointer->Begin(reinterpret_cast<HWND>(1));
        importerPointer->CancelPicker();
    };
    const auto outer = importer.Begin(reinterpret_cast<HWND>(1));
    Require(nested.status == LocalWidgetPackageImportStatus::Busy,
            "duplicate picker was admitted");
    Require(outer.status == LocalWidgetPackageImportStatus::Cancelled,
            "overlay-close picker cancellation was not quiet");
    Require(picker.cancelled, "overlay-close cancellation did not reach picker");
}

void StaleGenerationAndSubmissionAreBounded() {
    FakePicker picker;
    auto origin = SettingsOrigin();
    bool submitted{};
    LocalWidgetPackageImport importer(
        picker, [&] { return std::optional{origin}; },
        [&](const std::wstring_view path, const auto& admitted,
            const std::wstring_view operationId) {
            Require(path == L"C:\\fixture.gbarwidget", "selected path changed");
            Require(admitted.runtimeGeneration == L"runtime-one",
                    "wrong runtime generation submitted");
            Require(!operationId.empty(), "operation ID was empty");
            submitted = true;
            return true;
        });
    picker.duringSelect = [&] { origin.runtimeGeneration = L"runtime-two"; };
    const auto stale = importer.Begin(reinterpret_cast<HWND>(1));
    Require(stale.status == LocalWidgetPackageImportStatus::Refused && !submitted,
            "stale picker origin was submitted");

    origin = SettingsOrigin();
    picker.duringSelect = {};
    const auto accepted = importer.Begin(reinterpret_cast<HWND>(1));
    Require(accepted.status == LocalWidgetPackageImportStatus::Submitted,
            "valid picker origin was refused");
    Require(!accepted.operationId.empty() && submitted,
            "valid selection did not submit");
    Require(accepted.safeMessage.find(L"C:\\") == std::wstring::npos,
            "host result exposed the selected path");
}

} // namespace

int main() {
    ExactOriginAndQuietCancel();
    DuplicateAndCloseCancellation();
    StaleGenerationAndSubmissionAreBounded();
    std::cout << "LocalWidgetPackageImportTests passed (3 scenarios)\n";
}
