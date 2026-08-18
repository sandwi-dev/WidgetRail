#pragma once

#include "WidgetLifecycle.h"

#include <Windows.h>
#include <ShObjIdl.h>

#include <functional>
#include <optional>
#include <string>
#include <string_view>

namespace widgetrail::packages {

struct LocalWidgetPackageOrigin final {
    std::wstring widgetId;
    std::wstring packageId;
    std::wstring publisherId;
    std::wstring instanceId;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
    WidgetLifecycleState lifecycle{WidgetLifecycleState::Background};
};

enum class LocalWidgetPackageActionDisposition {
    Unrelated,
    Refused,
    Admitted,
};

/// Private host/Settings invocation contract. These identifiers are not a
/// community-widget capability: only the exact bundled Settings presentation
/// may use them, and every current identity/scope field is re-admitted before
/// the picker opens.
struct LocalWidgetPackageActionInvocation final {
    std::optional<LocalWidgetPackageOrigin> origin;
    std::wstring snapshotInstanceId;
    std::wstring activeInputScopeId;
    std::wstring actionId;
    std::wstring sourceElementId;
    std::wstring protocolButton;
    bool pressed{};
    bool enabled{};
    bool busy{};
};

enum class LocalWidgetPackagePickerStatus { Selected, Cancelled, Failed };

struct LocalWidgetPackagePickerResult final {
    LocalWidgetPackagePickerStatus status{LocalWidgetPackagePickerStatus::Failed};
    std::wstring path;
    std::wstring safeMessage;
};

class ILocalWidgetPackagePicker {
public:
    virtual ~ILocalWidgetPackagePicker() = default;
    [[nodiscard]] virtual LocalWidgetPackagePickerResult Select(HWND owner) = 0;
    virtual void Cancel() noexcept = 0;
    [[nodiscard]] virtual bool active() const noexcept = 0;
};

class FileOpenDialogWidgetPackagePicker final : public ILocalWidgetPackagePicker {
public:
    [[nodiscard]] LocalWidgetPackagePickerResult Select(HWND owner) override;
    void Cancel() noexcept override;
    [[nodiscard]] bool active() const noexcept override { return activeDialog_ != nullptr; }

private:
    IFileOpenDialog* activeDialog_{};
};

enum class LocalWidgetPackageImportStatus {
    Submitted,
    Cancelled,
    Refused,
    Busy,
    PickerFailed,
    TransportFailed,
};

struct LocalWidgetPackageImportResult final {
    LocalWidgetPackageImportStatus status{LocalWidgetPackageImportStatus::Refused};
    std::wstring operationId;
    std::wstring safeMessage;
};

struct LocalWidgetPackageActionResult final {
    bool claimed{};
    LocalWidgetPackageImportResult import;
};

class LocalWidgetPackageImport final {
public:
    using OriginProvider = std::function<std::optional<LocalWidgetPackageOrigin>()>;
    using Submit = std::function<bool(
        std::wstring_view,
        const LocalWidgetPackageOrigin&,
        std::wstring_view)>;

    LocalWidgetPackageImport(
        ILocalWidgetPackagePicker& picker,
        OriginProvider currentOrigin,
        Submit submit);

    static constexpr std::wstring_view ActionId = L"host.install-local-widget";
    static constexpr std::wstring_view SourceElementId = L"installed.install-local";
    static constexpr std::wstring_view InputScopeId = L"installed.widgets";

    [[nodiscard]] LocalWidgetPackageActionResult Invoke(
        HWND owner,
        const LocalWidgetPackageActionInvocation& invocation);
    [[nodiscard]] LocalWidgetPackageImportResult Begin(HWND owner);
    void CancelPicker() noexcept;
    [[nodiscard]] std::optional<std::wstring> CancelActiveOperation() noexcept;
    [[nodiscard]] bool Complete(std::wstring_view operationId) noexcept;
    [[nodiscard]] bool active() const noexcept {
        return active_ || !activeOperationId_.empty();
    }
    [[nodiscard]] std::wstring_view activeOperationId() const noexcept {
        return activeOperationId_;
    }

    [[nodiscard]] static bool Admit(const LocalWidgetPackageOrigin& origin) noexcept;
    [[nodiscard]] static LocalWidgetPackageActionDisposition Classify(
        const LocalWidgetPackageActionInvocation& invocation) noexcept;

private:
    [[nodiscard]] static bool SameOrigin(
        const LocalWidgetPackageOrigin& left,
        const LocalWidgetPackageOrigin& right) noexcept;
    [[nodiscard]] static std::wstring NewOperationId();

    ILocalWidgetPackagePicker& picker_;
    OriginProvider currentOrigin_;
    Submit submit_;
    bool active_{};
    std::wstring activeOperationId_;
};

} // namespace widgetrail::packages
