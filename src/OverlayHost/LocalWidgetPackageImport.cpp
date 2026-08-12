#include "LocalWidgetPackageImport.h"

#include <ShObjIdl.h>
#include <wrl/client.h>

#include <algorithm>
#include <filesystem>
#include <utility>

namespace gba::packages {
namespace {

using Microsoft::WRL::ComPtr;

struct ActiveReset final {
    bool& value;
    ~ActiveReset() { value = false; }
};

} // namespace

LocalWidgetPackagePickerResult FileOpenDialogWidgetPackagePicker::Select(HWND owner) {
    if (!owner || !IsWindow(owner) || activeDialog_)
        return {LocalWidgetPackagePickerStatus::Failed, {},
                L"The local widget package picker is unavailable."};

    ComPtr<IFileOpenDialog> dialog;
    HRESULT result = CoCreateInstance(
        CLSID_FileOpenDialog, nullptr, CLSCTX_INPROC_SERVER,
        IID_PPV_ARGS(&dialog));
    if (FAILED(result))
        return {LocalWidgetPackagePickerStatus::Failed, {},
                L"The local widget package picker could not be created."};

    DWORD options{};
    result = dialog->GetOptions(&options);
    if (SUCCEEDED(result)) {
        result = dialog->SetOptions(
            options | FOS_FORCEFILESYSTEM | FOS_FILEMUSTEXIST |
            FOS_PATHMUSTEXIST | FOS_NOCHANGEDIR | FOS_DONTADDTORECENT);
    }
    const COMDLG_FILTERSPEC filter{
        L"Game Bar widget packages (*.gbarwidget)", L"*.gbarwidget"};
    if (SUCCEEDED(result)) result = dialog->SetFileTypes(1, &filter);
    if (SUCCEEDED(result)) result = dialog->SetFileTypeIndex(1);
    if (SUCCEEDED(result)) result = dialog->SetDefaultExtension(L"gbarwidget");
    if (FAILED(result))
        return {LocalWidgetPackagePickerStatus::Failed, {},
                L"The local widget package picker could not be configured."};

    activeDialog_ = dialog.Get();
    result = dialog->Show(owner);
    activeDialog_ = nullptr;

    if (IsWindow(owner) && IsWindowVisible(owner) && GetForegroundWindow() == owner) {
        SetActiveWindow(owner);
        SetFocus(owner);
    }
    if (result == HRESULT_FROM_WIN32(ERROR_CANCELLED))
        return {LocalWidgetPackagePickerStatus::Cancelled};
    if (FAILED(result))
        return {LocalWidgetPackagePickerStatus::Failed, {},
                L"The local widget package picker failed."};

    ComPtr<IShellItem> item;
    result = dialog->GetResult(&item);
    PWSTR selected{};
    if (SUCCEEDED(result)) result = item->GetDisplayName(SIGDN_FILESYSPATH, &selected);
    if (FAILED(result) || !selected) {
        if (selected) CoTaskMemFree(selected);
        return {LocalWidgetPackagePickerStatus::Failed, {},
                L"The selected widget package is unavailable."};
    }
    std::wstring path(selected);
    CoTaskMemFree(selected);
    const auto extension = std::filesystem::path(path).extension().wstring();
    if (path.empty() || path.size() > 32'767 ||
        CompareStringOrdinal(
            extension.c_str(), static_cast<int>(extension.size()),
            L".gbarwidget", -1, TRUE) != CSTR_EQUAL) {
        return {LocalWidgetPackagePickerStatus::Failed, {},
                L"Select one .gbarwidget package."};
    }
    return {LocalWidgetPackagePickerStatus::Selected, std::move(path), {}};
}

void FileOpenDialogWidgetPackagePicker::Cancel() noexcept {
    if (activeDialog_) (void)activeDialog_->Close(HRESULT_FROM_WIN32(ERROR_CANCELLED));
}

LocalWidgetPackageImport::LocalWidgetPackageImport(
    ILocalWidgetPackagePicker& picker,
    OriginProvider currentOrigin,
    Submit submit)
    : picker_(picker),
      currentOrigin_(std::move(currentOrigin)),
      submit_(std::move(submit)) {}

LocalWidgetPackageImportResult LocalWidgetPackageImport::Begin(HWND owner) {
    if (active_ || picker_.active())
        return {LocalWidgetPackageImportStatus::Busy, {},
                L"A local widget package picker is already open."};
    auto origin = currentOrigin_ ? currentOrigin_() : std::nullopt;
    if (!origin || !Admit(*origin))
        return {LocalWidgetPackageImportStatus::Refused, {},
                L"Local widget installation is available only from Interactive Settings."};

    active_ = true;
    ActiveReset reset{active_};
    auto selected = picker_.Select(owner);
    if (selected.status == LocalWidgetPackagePickerStatus::Cancelled)
        return {LocalWidgetPackageImportStatus::Cancelled};
    if (selected.status != LocalWidgetPackagePickerStatus::Selected)
        return {LocalWidgetPackageImportStatus::PickerFailed, {},
                selected.safeMessage.empty()
                    ? L"The local widget package picker failed."
                    : std::move(selected.safeMessage)};

    const auto current = currentOrigin_ ? currentOrigin_() : std::nullopt;
    if (!current || !SameOrigin(*origin, *current) || !Admit(*current))
        return {LocalWidgetPackageImportStatus::Refused, {},
                L"Settings changed while the package picker was open."};

    const auto operationId = NewOperationId();
    if (operationId.empty() || !submit_ ||
        !submit_(selected.path, *current, operationId))
        return {LocalWidgetPackageImportStatus::TransportFailed, {},
                L"The local widget package install request could not be submitted."};
    return {LocalWidgetPackageImportStatus::Submitted, operationId,
            L"Installing the selected widget package disabled."};
}

void LocalWidgetPackageImport::CancelPicker() noexcept {
    picker_.Cancel();
}

bool LocalWidgetPackageImport::Admit(const LocalWidgetPackageOrigin& origin) noexcept {
    return origin.lifecycle == WidgetLifecycleState::Interactive &&
           origin.widgetId == L"settings" &&
           origin.packageId == L"org.gbar.firstparty.settings" &&
           origin.publisherId == L"org.gbar.firstparty" &&
           origin.instanceId == L"settings.default" &&
           !origin.runtimeGeneration.empty() &&
           !origin.presentationGeneration.empty();
}

bool LocalWidgetPackageImport::SameOrigin(
    const LocalWidgetPackageOrigin& left,
    const LocalWidgetPackageOrigin& right) noexcept {
    return left.widgetId == right.widgetId && left.packageId == right.packageId &&
           left.publisherId == right.publisherId && left.instanceId == right.instanceId &&
           left.runtimeGeneration == right.runtimeGeneration &&
           left.presentationGeneration == right.presentationGeneration &&
           left.lifecycle == right.lifecycle;
}

std::wstring LocalWidgetPackageImport::NewOperationId() {
    GUID id{};
    if (FAILED(CoCreateGuid(&id))) return {};
    wchar_t text[40]{};
    if (StringFromGUID2(id, text, static_cast<int>(std::size(text))) <= 0) return {};
    std::wstring result(text);
    result.erase(std::remove(result.begin(), result.end(), L'{'), result.end());
    result.erase(std::remove(result.begin(), result.end(), L'}'), result.end());
    return result;
}

} // namespace gba::packages
