#include "LocalWidgetPackageImport.h"

#include <ShObjIdl.h>
#include <commctrl.h>
#include <wrl/client.h>

#include <algorithm>
#include <filesystem>
#include <utility>

namespace widgetrail::packages {
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
        L"WidgetRail widget packages (*.wrwidget)", L"*.wrwidget"};
    if (SUCCEEDED(result)) result = dialog->SetFileTypes(1, &filter);
    if (SUCCEEDED(result)) result = dialog->SetFileTypeIndex(1);
    if (SUCCEEDED(result)) result = dialog->SetDefaultExtension(L"wrwidget");
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
            L".wrwidget", -1, TRUE) != CSTR_EQUAL) {
        return {LocalWidgetPackagePickerStatus::Failed, {},
                L"Select one .wrwidget package."};
    }
    return {LocalWidgetPackagePickerStatus::Selected, std::move(path), {}};
}

bool FileOpenDialogWidgetPackagePicker::ConfirmFullTrust(HWND owner, std::wstring_view widgetId, std::wstring_view version) {
    if (!owner || !IsWindow(owner) || active()) return false;
    const std::wstring identity = std::wstring(widgetId) + L"  " + std::wstring(version);
    const TASKDIALOG_BUTTON buttons[]{{IDYES, L"Install disabled"}, {IDCANCEL, L"Cancel"}};
    TASKDIALOGCONFIG config{sizeof(config)};
    config.hwndParent = owner;
    config.dwFlags = TDF_ALLOW_DIALOG_CANCELLATION | TDF_POSITION_RELATIVE_TO_WINDOW | TDF_SIZE_TO_CONTENT;
    config.pszWindowTitle = L"WidgetRail - Review full-access widget";
    config.pszMainIcon = TD_WARNING_ICON;
    config.pszMainInstruction = L"Allow installation of a full-access widget?";
    config.pszContent = L"This widget can access your files, network, and other apps with your Windows account's permissions. It is not sandboxed, and its publisher is not verified.\n\nOnly continue if you trust where you downloaded it. It will be installed disabled. You must enable it separately in Settings.";
    config.pszFooter = identity.c_str();
    config.pButtons = buttons;
    config.cButtons = static_cast<UINT>(std::size(buttons));
    config.nDefaultButton = IDCANCEL;
    config.lpCallbackData = reinterpret_cast<LONG_PTR>(this);
    config.pfCallback = [](HWND dialog, UINT event, WPARAM, LPARAM, LONG_PTR data) -> HRESULT {
        auto* picker = reinterpret_cast<FileOpenDialogWidgetPackagePicker*>(data);
        if (event == TDN_CREATED) picker->confirmationDialog_ = dialog;
        else if (event == TDN_DESTROYED) picker->confirmationDialog_ = nullptr;
        return S_OK;
    };
    int selected = IDCANCEL;
    const auto result = TaskDialogIndirect(&config, &selected, nullptr, nullptr);
    confirmationDialog_ = nullptr;
    return SUCCEEDED(result) && selected == IDYES;
}

void FileOpenDialogWidgetPackagePicker::Cancel() noexcept {
    if (activeDialog_) (void)activeDialog_->Close(HRESULT_FROM_WIN32(ERROR_CANCELLED));
    if (confirmationDialog_) PostMessageW(confirmationDialog_, TDM_CLICK_BUTTON, IDCANCEL, 0);
}

LocalWidgetPackageImport::LocalWidgetPackageImport(
    ILocalWidgetPackagePicker& picker,
    OriginProvider currentOrigin,
    Submit submit)
    : picker_(picker),
      currentOrigin_(std::move(currentOrigin)),
      submit_(std::move(submit)) {}

LocalWidgetPackageActionResult LocalWidgetPackageImport::Invoke(
    HWND owner,
    const LocalWidgetPackageActionInvocation& invocation) {
    const auto disposition = Classify(invocation);
    if (disposition == LocalWidgetPackageActionDisposition::Unrelated)
        return {};
    if (disposition == LocalWidgetPackageActionDisposition::Refused)
        return {true, {LocalWidgetPackageImportStatus::Refused, {},
            L"The local widget package action is no longer current."}};
    const auto current = currentOrigin_ ? currentOrigin_() : std::nullopt;
    if (!invocation.origin || !current ||
        !SameOrigin(*invocation.origin, *current)) {
        return {true, {LocalWidgetPackageImportStatus::Refused, {},
            L"Settings changed before the package picker opened."}};
    }
    return {true, Begin(owner, invocation.activeInputScopeId == UpdateInputScopeId
        ? std::wstring_view(invocation.sourceElementId).substr(UpdateSourcePrefix.size()) : std::wstring_view{})};
}

LocalWidgetPackageImportResult LocalWidgetPackageImport::Begin(HWND owner, const std::wstring_view updateTargetHash) {
    if (active_ || picker_.active() || !activeOperationId_.empty())
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

    auto current = currentOrigin_ ? currentOrigin_() : std::nullopt;
    if (!current || !SameOrigin(*origin, *current) || !Admit(*current))
        return {LocalWidgetPackageImportStatus::Refused, {},
                L"Settings changed while the package picker was open."};

    current->updateTargetHash = updateTargetHash;
    const auto operationId = NewOperationId();
    if (operationId.empty() || !submit_ ||
        !submit_(selected.path, *current, operationId))
        return {LocalWidgetPackageImportStatus::TransportFailed, {},
                L"The local widget package install request could not be submitted."};
    activeOperationId_ = operationId;
    activeBridgeSessionGeneration_ = current->bridgeSessionGeneration;
    activeOrigin_ = *current;
    approvalShown_ = false;
    return {LocalWidgetPackageImportStatus::Submitted, operationId,
            L"Installing the selected widget package disabled."};
}

bool LocalWidgetPackageImport::ReviewFullTrust(HWND owner, std::wstring_view operationId,
    long long bridgeSessionGeneration, std::wstring_view widgetId, std::wstring_view version) {
    const auto valid = [&] {
        const auto current = currentOrigin_ ? currentOrigin_() : std::nullopt;
        return !operationId.empty() && operationId == activeOperationId_ &&
            bridgeSessionGeneration == activeBridgeSessionGeneration_ && activeOrigin_ &&
            current && Admit(*current) && SameOrigin(*activeOrigin_, *current);
    };
    if (approvalShown_ || !valid()) return false;
    approvalShown_ = true;
    const bool approved = picker_.ConfirmFullTrust(owner, widgetId, version);
    return approved && valid();
}

void LocalWidgetPackageImport::CancelPicker() noexcept {
    picker_.Cancel();
}

std::optional<std::wstring>
LocalWidgetPackageImport::CancelActiveOperation() noexcept {
    if (activeOperationId_.empty()) return std::nullopt;
    activeBridgeSessionGeneration_ = 0;
    return std::exchange(activeOperationId_, {});
}

std::optional<std::wstring> LocalWidgetPackageImport::RetireBridgeSession(
    const long long currentBridgeSessionGeneration) noexcept {
    if (activeOperationId_.empty() || currentBridgeSessionGeneration <= 0 ||
        activeBridgeSessionGeneration_ == currentBridgeSessionGeneration)
        return std::nullopt;
    return CancelActiveOperation();
}

bool LocalWidgetPackageImport::Complete(
    const std::wstring_view operationId,
    const long long bridgeSessionGeneration) noexcept {
    if (operationId.empty() || operationId != activeOperationId_ ||
        bridgeSessionGeneration <= 0 ||
        bridgeSessionGeneration != activeBridgeSessionGeneration_)
        return false;
    activeOperationId_.clear();
    activeBridgeSessionGeneration_ = 0;
    return true;
}

bool LocalWidgetPackageImport::Admit(const LocalWidgetPackageOrigin& origin) noexcept {
    return origin.lifecycle == WidgetLifecycleState::Interactive &&
           origin.widgetId == L"settings" &&
           origin.packageId == L"widgetrail.firstparty.settings" &&
           origin.publisherId == L"widgetrail.firstparty" &&
           origin.instanceId == L"settings.default" &&
           !origin.runtimeGeneration.empty() &&
           !origin.presentationGeneration.empty() &&
           origin.bridgeSessionGeneration > 0;
}

LocalWidgetPackageActionDisposition LocalWidgetPackageImport::Classify(
    const LocalWidgetPackageActionInvocation& invocation) noexcept {
    const bool reservedAction = invocation.actionId == ActionId;
    const bool updateSource = invocation.sourceElementId.starts_with(UpdateSourcePrefix);
    const auto updateId = updateSource ? std::wstring_view(invocation.sourceElementId).substr(UpdateSourcePrefix.size()) : std::wstring_view{};
    const bool validUpdate = updateSource && updateId.size() == 64 &&
        updateId.find_first_not_of(L"abcdefABCDEF0123456789") == std::wstring_view::npos;
    const bool reservedSource = invocation.sourceElementId == SourceElementId || updateSource;
    const bool validScope = (invocation.sourceElementId == SourceElementId && invocation.activeInputScopeId == InputScopeId) ||
        (validUpdate && invocation.activeInputScopeId == UpdateInputScopeId);
    if (!reservedAction && !reservedSource)
        return LocalWidgetPackageActionDisposition::Unrelated;
    if (!reservedAction || !reservedSource || !invocation.origin ||
        !Admit(*invocation.origin) ||
        invocation.snapshotInstanceId != invocation.origin->instanceId ||
        !validScope ||
        invocation.protocolButton != L"a" || !invocation.pressed ||
        !invocation.enabled || invocation.busy) {
        return LocalWidgetPackageActionDisposition::Refused;
    }
    return LocalWidgetPackageActionDisposition::Admitted;
}

bool LocalWidgetPackageImport::SameOrigin(
    const LocalWidgetPackageOrigin& left,
    const LocalWidgetPackageOrigin& right) noexcept {
    return left.widgetId == right.widgetId && left.packageId == right.packageId &&
           left.publisherId == right.publisherId && left.instanceId == right.instanceId &&
           left.runtimeGeneration == right.runtimeGeneration &&
           left.presentationGeneration == right.presentationGeneration &&
           left.bridgeSessionGeneration == right.bridgeSessionGeneration &&
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

} // namespace widgetrail::packages
