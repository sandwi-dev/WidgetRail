#include "LocalWidgetPackageImport.h"

#include <cstdlib>
#include <iostream>
#include <utility>

namespace {

using namespace widgetrail::packages;

void Require(const bool condition, const char* message) {
    if (condition) return;
    std::cerr << "LocalWidgetPackageImportTests failed: " << message << '\n';
    std::exit(1);
}

LocalWidgetPackageOrigin SettingsOrigin() {
    return {
        L"settings", L"widgetrail.firstparty.settings", L"widgetrail.firstparty",
        L"settings.default", L"runtime-one", L"presentation-one",
        widgetrail::WidgetLifecycleState::Interactive};
}

LocalWidgetPackageActionInvocation SettingsInvocation() {
    return {
        SettingsOrigin(),
        L"settings.default",
        std::wstring(LocalWidgetPackageImport::InputScopeId),
        std::wstring(LocalWidgetPackageImport::ActionId),
        std::wstring(LocalWidgetPackageImport::SourceElementId),
        L"a",
        true,
        true,
        false,
    };
}

struct FakePicker final : ILocalWidgetPackagePicker {
    LocalWidgetPackagePickerResult result{
        LocalWidgetPackagePickerStatus::Selected, L"C:\\fixture.wrwidget", {}};
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
    origin.lifecycle = widgetrail::WidgetLifecycleState::Visible;
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
            Require(path == L"C:\\fixture.wrwidget", "selected path changed");
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

void ExactPrivateActionOwnsTheCompleteOperation() {
    FakePicker picker;
    auto origin = SettingsOrigin();
    int selections{};
    int submissions{};
    picker.duringSelect = [&] { ++selections; };
    LocalWidgetPackageImport importer(
        picker, [&] { return std::optional{origin}; },
        [&](auto, const auto&, auto) { ++submissions; return true; });

    auto invocation = SettingsInvocation();
    const auto accepted = importer.Invoke(reinterpret_cast<HWND>(1), invocation);
    Require(accepted.claimed &&
                accepted.import.status == LocalWidgetPackageImportStatus::Submitted,
            "exact production Settings action did not reach the picker");
    Require(selections == 1 && submissions == 1 && importer.active(),
            "exact action did not retain one submitted operation owner");

    const auto repeated = importer.Invoke(reinterpret_cast<HWND>(1), invocation);
    Require(repeated.claimed &&
                repeated.import.status == LocalWidgetPackageImportStatus::Busy &&
                selections == 1 && submissions == 1,
            "repeated activation duplicated the picker or submission");
    Require(!importer.Complete(L"wrong-operation") && importer.active(),
            "wrong completion retired the active operation");
    Require(importer.Complete(accepted.import.operationId) && !importer.active(),
            "exact completion did not retire the active operation");

    const auto unrelated = [&] {
        auto candidate = SettingsInvocation();
        candidate.actionId = L"installed.select.0";
        candidate.sourceElementId = L"installed.item.0";
        return candidate;
    }();
    Require(LocalWidgetPackageImport::Classify(unrelated) ==
                LocalWidgetPackageActionDisposition::Unrelated,
            "ordinary Settings action was claimed by the private contract");

    const auto refused = [](LocalWidgetPackageActionInvocation candidate) {
        return LocalWidgetPackageImport::Classify(candidate) ==
            LocalWidgetPackageActionDisposition::Refused;
    };
    auto forged = SettingsInvocation();
    forged.origin->widgetId = L"community-settings";
    Require(refused(forged), "forged widget identity was admitted");
    forged = SettingsInvocation();
    forged.origin->packageId = L"community.settings";
    Require(refused(forged), "forged package identity was admitted");
    forged = SettingsInvocation();
    forged.origin->publisherId = L"community.publisher";
    Require(refused(forged), "forged publisher identity was admitted");
    forged = SettingsInvocation();
    forged.origin->instanceId = L"settings.forged";
    Require(refused(forged), "forged instance identity was admitted");
    forged = SettingsInvocation();
    forged.snapshotInstanceId = L"settings.stale";
    Require(refused(forged), "stale snapshot instance was admitted");
    forged = SettingsInvocation();
    forged.origin->runtimeGeneration.clear();
    Require(refused(forged), "missing runtime generation was admitted");
    forged = SettingsInvocation();
    forged.origin->presentationGeneration.clear();
    Require(refused(forged), "missing presentation generation was admitted");
    forged = SettingsInvocation();
    forged.activeInputScopeId = L"installed.details";
    Require(refused(forged), "wrong input scope was admitted");
    forged = SettingsInvocation();
    forged.actionId = L"host.install-other";
    Require(refused(forged), "wrong reserved action pair was admitted");
    forged = SettingsInvocation();
    forged.sourceElementId = L"installed.other";
    Require(refused(forged), "wrong reserved source pair was admitted");
    forged = SettingsInvocation();
    forged.origin->lifecycle = widgetrail::WidgetLifecycleState::Visible;
    Require(refused(forged), "Visible Settings action was admitted");
    forged = SettingsInvocation();
    forged.origin->lifecycle = widgetrail::WidgetLifecycleState::Background;
    Require(refused(forged), "Background Settings action was admitted");
    forged = SettingsInvocation();
    forged.busy = true;
    Require(refused(forged), "busy rendered action was admitted");

    forged = SettingsInvocation();
    origin.runtimeGeneration = L"runtime-two";
    const auto stale = importer.Invoke(reinterpret_cast<HWND>(1), forged);
    Require(stale.claimed &&
                stale.import.status == LocalWidgetPackageImportStatus::Refused &&
                selections == 1 && submissions == 1,
            "stale current generation crossed into the picker");

    origin = SettingsOrigin();
    forged = SettingsInvocation();
    forged.origin->runtimeGeneration = L"runtime-forged";
    const auto forgedRuntime = importer.Invoke(reinterpret_cast<HWND>(1), forged);
    Require(forgedRuntime.claimed &&
                forgedRuntime.import.status ==
                    LocalWidgetPackageImportStatus::Refused &&
                selections == 1 && submissions == 1,
            "forged nonempty runtime generation crossed into the picker");
    forged = SettingsInvocation();
    forged.origin->presentationGeneration = L"presentation-forged";
    const auto forgedPresentation = importer.Invoke(
        reinterpret_cast<HWND>(1), forged);
    Require(forgedPresentation.claimed &&
                forgedPresentation.import.status ==
                    LocalWidgetPackageImportStatus::Refused &&
                selections == 1 && submissions == 1,
            "forged nonempty presentation generation crossed into the picker");

    const auto resubmitted = importer.Invoke(reinterpret_cast<HWND>(1), SettingsInvocation());
    Require(resubmitted.import.status == LocalWidgetPackageImportStatus::Submitted,
            "exact action could not start after exact completion");
    const auto cancelled = importer.CancelActiveOperation();
    Require(cancelled && *cancelled == resubmitted.import.operationId && !importer.active(),
            "overlay-close operation cancellation lost exact ownership");
}

} // namespace

int main() {
    ExactOriginAndQuietCancel();
    DuplicateAndCloseCancellation();
    StaleGenerationAndSubmissionAreBounded();
    ExactPrivateActionOwnsTheCompleteOperation();
    std::cout << "LocalWidgetPackageImportTests passed (4 scenarios)\n";
}
