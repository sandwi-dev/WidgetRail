#pragma once

#include <cstdint>
#include <optional>
#include <string>
#include <vector>

namespace widgetrail::pinned {

enum class InteractionMode {
    ClickThrough,
    Focusable,
};

enum class ContentPresentation { AdmittedWidget };

struct SurfacePresentationPolicy final {
    ContentPresentation content{ContentPresentation::AdmittedWidget};
    bool exposeInteractiveSemantics{};
};

[[nodiscard]] constexpr SurfacePresentationPolicy ResolveSurfacePresentationPolicy(
    const InteractionMode mode) noexcept {
    return {ContentPresentation::AdmittedWidget, mode == InteractionMode::Focusable};
}

enum class LifecycleState {
    Unpinned,
    Pinned,
    Stopped,
};

enum class StopReason {
    Unpin,
    HostExit,
    CrashRecovery,
};

enum class ControllerCommand {
    None,
    Enter,
    Exit,
    Activate,
    Close,
    EmergencyHide,
};

struct ControllerInputContext final {
    bool pinned{};
    bool placementActive{};
    bool sameWidgetOpen{};
    bool controllerFocused{};
    bool aPressed{};
    bool bPressed{};
    bool xPressed{};
    bool rightStickPressed{};
    bool leftShoulderDown{};
    bool rightShoulderDown{};
};

[[nodiscard]] ControllerCommand ResolveControllerCommand(
    const ControllerInputContext& context) noexcept;

struct SurfaceDescriptor final {
    // Data-only identity. No native window, renderer, process, or provider
    // handle can cross this boundary; the host creates and owns those objects.
    std::wstring id;
    std::wstring accessibleName;
    std::uint32_t backgroundArgb{0xFF24415A};
    bool reducedMotion{true};
};

struct SemanticNode final {
    std::wstring id;
    std::wstring name;
    std::wstring value;
    bool keyboardFocusable{};
};

class SurfacePolicy final {
public:
    [[nodiscard]] bool Pin(SurfaceDescriptor descriptor);
    void SetInteractionMode(InteractionMode mode) noexcept;
    void OnMainOverlayHidden() noexcept;
    void Stop(StopReason reason) noexcept;

    [[nodiscard]] LifecycleState state() const noexcept { return state_; }
    [[nodiscard]] InteractionMode interactionMode() const noexcept { return interactionMode_; }
    [[nodiscard]] const std::optional<SurfaceDescriptor>& descriptor() const noexcept {
        return descriptor_;
    }
    [[nodiscard]] StopReason lastStopReason() const noexcept { return lastStopReason_; }
    [[nodiscard]] std::vector<SemanticNode> ProjectSemantics() const;

private:
    LifecycleState state_{LifecycleState::Unpinned};
    InteractionMode interactionMode_{InteractionMode::ClickThrough};
    std::optional<SurfaceDescriptor> descriptor_;
    StopReason lastStopReason_{StopReason::Unpin};
};

struct PhysicalRect final {
    int left{};
    int top{};
    int right{};
    int bottom{};
};

struct MonitorWorkArea final {
    std::wstring stableId;
    PhysicalRect workArea;
    unsigned int dpi{96};
    bool primary{};
};

struct PersistedPlacement final {
    std::wstring monitorId;
    float leftDip{};
    float topDip{};
    float widthDip{480.0F};
    float heightDip{270.0F};
};

struct ResolvedPlacement final {
    std::wstring monitorId;
    PhysicalRect bounds;
    unsigned int dpi{96};
    bool usedFallback{};
    bool clamped{};
};

/// Resolves a fully work-area-contained physical rectangle. Missing monitors,
/// malformed persisted values, rotation, and work-area shrink all converge on
/// the primary (or first valid) monitor and a top-right host default.
[[nodiscard]] std::optional<ResolvedPlacement> ResolvePlacement(
    const std::vector<MonitorWorkArea>& monitors,
    const std::optional<PersistedPlacement>& persisted) noexcept;

} // namespace widgetrail::pinned
