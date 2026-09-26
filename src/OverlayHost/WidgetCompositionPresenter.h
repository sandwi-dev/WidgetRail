#pragma once
#include "WidgetCompositionScene.h"
#include <dcomp.h>
#include <d2d1_1.h>
#include <map>

namespace widgetrail {
class WidgetCompositionPresenter final {
  public:
    struct Counters {
        std::uint64_t sceneCommits{}, rasterUploads{}, animationStarts{}, interruptionCaptures{};
    };
    WidgetCompositionPresenter(IDCompositionDevice2 *composition, ID2D1Device *graphics);
    ~WidgetCompositionPresenter() {
        Clear();
    }
    HRESULT Apply(std::shared_ptr<const WidgetCompositionScene> scene, IDCompositionVisual2 *parent,
                  IDCompositionVisual2 *above, float offsetX, float offsetY);
    HRESULT Advance(); // retires completed visuals; never requests a raster frame
    void Clear() noexcept;
    [[nodiscard]] Counters counters() const noexcept {
        return counters_;
    }
    [[nodiscard]] D2D1_POINT_2F MapInput(D2D1_POINT_2F point) const noexcept;
    [[nodiscard]] bool active() const noexcept;
    HRESULT SetOpacity(float opacity) {
        return root_ ? root_->SetOpacity(opacity) : S_OK;
    }
    HRESULT SetOpacity(IDCompositionAnimation *opacity) {
        return root_ ? root_->SetOpacity(opacity) : S_OK;
    }

  private:
    template <class T> using Ptr = Microsoft::WRL::ComPtr<T>;
    using Rect = declarative::Rect;
    using Pose = animation::Pose;
    using Motion = animation::Motion;
    struct Raster {
        WidgetCompositionNode node;
        Ptr<IDCompositionSurface> surface;
        Ptr<IDCompositionVisual3> visual;
        D2D1_SIZE_U pixels{};
    };
    struct Group {
        WidgetCompositionNode node;
        Ptr<IDCompositionVisual3> root, incoming, outgoing, incomingClip, outgoingClip;
        Motion motion, exit;
        std::optional<animation::FocusTarget> focusOrigin;
        Motion focusReveal;
        Ptr<IDCompositionArithmeticCompositeEffect> focusBlend;
        Rect focusExtent;
        std::array<Ptr<IDCompositionVisual3>, 5> surfaceClips, surfacePixels;
        Raster previous;
        bool closing{};
        int direction{1};
        float resumeOpacity{1};
        std::size_t paintOrder{};
    };
    IDCompositionDevice2 *device_{};
    ID2D1Device *graphics_{};
    Ptr<IDCompositionVisual3> root_;
    Ptr<IDCompositionVisual2> parent_;
    std::shared_ptr<const WidgetCompositionScene> scene_;
    std::map<std::wstring, Group> groups_;
    std::map<std::wstring, Raster> rasters_;
    Counters counters_;
    bool attached_{};
    static std::int64_t Now() noexcept;
    static std::int64_t Frequency() noexcept;
    Rect CaptureBounds(const Group &group, std::int64_t now) const noexcept;
    HRESULT Upload(Raster &raster, const WidgetCompositionNode &node);
    HRESULT Animate(IDCompositionVisual3 *visual, IDCompositionVisual3 *clipVisual, const Motion &motion,
                    Rect basis);
    HRESULT Capture(const std::wstring &group, Raster &result, std::int64_t now);
    void DrawGroup(ID2D1RenderTarget *target, const std::wstring &group, D2D1_MATRIX_3X2_F transform,
                   float opacity, std::int64_t now) const;
    HRESULT Rebuild(IDCompositionVisual2 *above);
    HRESULT ConfigureFocusSurface(Group &group);
    HRESULT ConfigureFocusFade(Group &group, const Raster &idle, const Raster &focused);
    HRESULT CreateAnimation(float from, float to, const Motion &motion, IDCompositionAnimation **output);
    void DrawFocusFade(ID2D1RenderTarget *target, const Group &group,
                       D2D1_MATRIX_3X2_F transform, float opacity, std::int64_t now) const;
};
} // namespace widgetrail
