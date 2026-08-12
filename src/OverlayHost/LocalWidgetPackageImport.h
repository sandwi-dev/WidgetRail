#pragma once

#include "WidgetLifecycle.h"

#include <Windows.h>
#include <ShObjIdl.h>

#include <functional>
#include <optional>
#include <string>
#include <string_view>

namespace gba::packages {

struct LocalWidgetPackageOrigin final {
    std::wstring widgetId;
    std::wstring packageId;
    std::wstring publisherId;
    std::wstring instanceId;
    std::wstring runtimeGeneration;
    std::wstring presentationGeneration;
    WidgetLifecycleState lifecycle{WidgetLifecycleState::Background};
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

    [[nodiscard]] LocalWidgetPackageImportResult Begin(HWND owner);
    void CancelPicker() noexcept;
    [[nodiscard]] bool active() const noexcept { return active_; }

    [[nodiscard]] static bool Admit(const LocalWidgetPackageOrigin& origin) noexcept;

private:
    [[nodiscard]] static bool SameOrigin(
        const LocalWidgetPackageOrigin& left,
        const LocalWidgetPackageOrigin& right) noexcept;
    [[nodiscard]] static std::wstring NewOperationId();

    ILocalWidgetPackagePicker& picker_;
    OriginProvider currentOrigin_;
    Submit submit_;
    bool active_{};
};

} // namespace gba::packages
