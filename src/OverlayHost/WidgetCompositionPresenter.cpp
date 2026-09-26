#include "WidgetCompositionPresenter.h"
#include <algorithm>
#include <cmath>
#include <functional>
#include <set>
#include <limits>
#include <d2d1effects.h>
#pragma comment(lib, "dxguid.lib")

namespace widgetrail {
namespace {
using Rect = declarative::Rect;
bool Same(Rect a, Rect b) {
    return a.x == b.x && a.y == b.y && a.width == b.width && a.height == b.height;
}
D2D1_RECT_F Box(Rect r, float scale = 1) {
    return {r.x * scale, r.y * scale, (r.x + r.width) * scale, (r.y + r.height) * scale};
}
bool Contains(Rect r, D2D1_POINT_2F p) {
    return p.x >= r.x && p.y >= r.y && p.x < r.x + r.width && p.y < r.y + r.height;
}
D2D1::Matrix3x2F Matrix(D2D1_MATRIX_3X2_F value) {
    return {value._11, value._12, value._21, value._22, value._31, value._32};
}
D2D1::Matrix3x2F Transform(Rect basis, Rect pose) {
    const auto value = animation::Map(basis, pose);
    return {value.scaleX, 0, 0, value.scaleY, value.offsetX, value.offsetY};
}
} // namespace

WidgetCompositionPresenter::WidgetCompositionPresenter(IDCompositionDevice2 *composition,
                                                       ID2D1Device *graphics)
    : device_(composition), graphics_(graphics) {}

std::int64_t WidgetCompositionPresenter::Now() noexcept {
    LARGE_INTEGER v{};
    QueryPerformanceCounter(&v);
    return v.QuadPart;
}
std::int64_t WidgetCompositionPresenter::Frequency() noexcept {
    LARGE_INTEGER v{};
    QueryPerformanceFrequency(&v);
    return v.QuadPart;
}

HRESULT WidgetCompositionPresenter::Upload(Raster &raster, const WidgetCompositionNode &node) {
    if (!node.bitmap && !node.solid)
        return E_INVALIDARG;
    const auto pixels = node.solid ? D2D1::SizeU(1, 1) : node.bitmap->GetPixelSize();
    if (!pixels.width || !pixels.height)
        return E_INVALIDARG;
    if (!raster.visual) {
        Ptr<IDCompositionVisual2> visual;
        auto hr = device_->CreateVisual(visual.GetAddressOf());
        if (FAILED(hr))
            return hr;
        hr = visual.As(&raster.visual);
        if (FAILED(hr))
            return hr;
    }
    Ptr<IDCompositionSurface> surface =
        raster.pixels.width == pixels.width && raster.pixels.height == pixels.height ? raster.surface
                                                                                     : nullptr;
    auto hr = S_OK;
    if (!surface)
        hr = device_->CreateSurface(pixels.width, pixels.height, DXGI_FORMAT_B8G8R8A8_UNORM,
                                    DXGI_ALPHA_MODE_PREMULTIPLIED, surface.GetAddressOf());
    Ptr<ID2D1DeviceContext> target;
    POINT offset{};
    if (SUCCEEDED(hr))
        hr = surface->BeginDraw(nullptr, __uuidof(ID2D1DeviceContext),
                                reinterpret_cast<void **>(target.GetAddressOf()), &offset);
    if (FAILED(hr))
        return hr;
    target->SetDpi(96, 96);
    target->SetTransform(
        D2D1::Matrix3x2F::Translation(static_cast<float>(offset.x), static_cast<float>(offset.y)));
    target->Clear(D2D1::ColorF(0, 0));
    if (node.solid)
        target->Clear(*node.solid);
    else
        target->DrawBitmap(node.bitmap.Get(), D2D1::RectF(0, 0, static_cast<float>(pixels.width),
                                                          static_cast<float>(pixels.height)));
    hr = surface->EndDraw();
    if (FAILED(hr))
        return hr;
    if (SUCCEEDED(hr))
        hr = raster.visual->SetContent(surface.Get());
    const auto scale = scene_->scale;
    if (SUCCEEDED(hr))
        hr = raster.visual->SetOffsetX(node.bounds.x * scale);
    if (SUCCEEDED(hr))
        hr = raster.visual->SetOffsetY(node.bounds.y * scale);
    const D2D_MATRIX_3X2_F matrix{node.bounds.width * scale / pixels.width,   0, 0,
                                  node.bounds.height * scale / pixels.height, 0, 0};
    if (SUCCEEDED(hr))
        hr = static_cast<IDCompositionVisual2 *>(raster.visual.Get())->SetTransform(matrix);
    if (SUCCEEDED(hr)) {
        raster.surface = std::move(surface);
        raster.node = node;
        raster.pixels = pixels;
        ++counters_.rasterUploads;
    }
    return hr;
}

HRESULT WidgetCompositionPresenter::Animate(IDCompositionVisual3 *visual, IDCompositionVisual3 *clipVisual,
                                            const Motion &motion, Rect basis) {
    const auto from = Transform(basis, motion.from.bounds);
    const auto to = Transform(basis, motion.to.bounds);
    const auto scale = scene_->scale;
    if (!motion.duration) {
        D2D_MATRIX_3X2_F matrix{to._11, 0, 0, to._22, to._31 * scale, to._32 * scale};
        auto hr = static_cast<IDCompositionVisual2 *>(visual)->SetTransform(matrix);
        if (SUCCEEDED(hr))
            hr = visual->SetOpacity(motion.to.opacity);
        if (SUCCEEDED(hr))
            hr = clipVisual->SetClip(Box(motion.to.clip, scale));
        return hr;
    }
    Ptr<IDCompositionMatrixTransform> transform;
    auto hr = device_->CreateMatrixTransform(transform.GetAddressOf());
    const auto animate = [&](float a, float b, IDCompositionAnimation **output) {
        return CreateAnimation(a, b, motion, output);
    };
    const float a[]{from._11, from._22, from._31 * scale, from._32 * scale};
    const float b[]{to._11, to._22, to._31 * scale, to._32 * scale};
    const int rows[]{0, 1, 2, 2}, columns[]{0, 1, 0, 1};
    for (int i = 0; SUCCEEDED(hr) && i < 4; ++i) {
        if (a[i] == b[i]) {
            hr = transform->SetMatrixElement(rows[i], columns[i], b[i]);
            continue;
        }
        Ptr<IDCompositionAnimation> animation;
        hr = animate(a[i], b[i], animation.GetAddressOf());
        if (SUCCEEDED(hr))
            hr = transform->SetMatrixElement(rows[i], columns[i], animation.Get());
    }
    if (SUCCEEDED(hr))
        hr = static_cast<IDCompositionVisual2 *>(visual)->SetTransform(transform.Get());
    Ptr<IDCompositionAnimation> opacity;
    if (SUCCEEDED(hr) && motion.from.opacity != motion.to.opacity)
        hr = animate(motion.from.opacity, motion.to.opacity, opacity.GetAddressOf());
    if (SUCCEEDED(hr))
        hr = opacity ? visual->SetOpacity(opacity.Get()) : visual->SetOpacity(motion.to.opacity);
    if (SUCCEEDED(hr) && Same(motion.from.clip, motion.to.clip))
        hr = clipVisual->SetClip(Box(motion.to.clip, scale));
    else if (SUCCEEDED(hr)) {
        Ptr<IDCompositionRectangleClip> clip;
        hr = device_->CreateRectangleClip(clip.GetAddressOf());
        const auto first = Box(motion.from.clip, scale), last = Box(motion.to.clip, scale);
        using Setter = HRESULT (STDMETHODCALLTYPE IDCompositionRectangleClip::*)(IDCompositionAnimation *);
        const Setter setters[]{&IDCompositionRectangleClip::SetLeft, &IDCompositionRectangleClip::SetTop,
                               &IDCompositionRectangleClip::SetRight, &IDCompositionRectangleClip::SetBottom};
        const float fromEdges[]{first.left, first.top, first.right, first.bottom};
        const float toEdges[]{last.left, last.top, last.right, last.bottom};
        for (int i = 0; SUCCEEDED(hr) && i < 4; ++i) {
            Ptr<IDCompositionAnimation> edge;
            hr = animate(fromEdges[i], toEdges[i], edge.GetAddressOf());
            if (SUCCEEDED(hr))
                hr = (clip.Get()->*setters[i])(edge.Get());
        }
        if (SUCCEEDED(hr))
            hr = clipVisual->SetClip(clip.Get());
    }
    if (SUCCEEDED(hr))
        ++counters_.animationStarts;
    return hr;
}

void WidgetCompositionPresenter::DrawGroup(ID2D1RenderTarget *target, const std::wstring &id,
                                           D2D1_MATRIX_3X2_F inherited, float opacity,
                                           std::int64_t now) const {
    const auto found = groups_.find(id);
    if (found == groups_.end() || !scene_)
        return;
    const auto &group = found->second;
    target->SetTransform(inherited);
    target->PushAxisAlignedClip(Box(group.node.clip), D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
    if (group.node.kind == WidgetCompositionKind::FocusSurface) {
        DrawFocusFade(target, group, inherited, opacity, now);
        target->PopAxisAlignedClip();
        return;
    }
    if (group.previous.node.bitmap) {
        const auto exit = Sample(group.exit, now);
        target->PushAxisAlignedClip(Box(exit.clip), D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        target->SetTransform(Transform(group.previous.node.bounds, exit.bounds) * Matrix(inherited));
        target->DrawBitmap(group.previous.node.bitmap.Get(), Box(group.previous.node.bounds),
                           opacity * exit.opacity);
        target->SetTransform(inherited);
        target->PopAxisAlignedClip();
    }
    if (!group.closing) {
        const auto pose = Sample(group.motion, now);
        target->PushAxisAlignedClip(Box(pose.clip), D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        const auto matrix = Transform(group.node.bounds, pose.bounds) * Matrix(inherited);
        for (const auto &node : scene_->nodes) {
            if (node.parent != id)
                continue;
            if (node.kind != WidgetCompositionKind::Raster)
                DrawGroup(target, node.id, matrix, opacity * pose.opacity, now);
            else {
                target->SetTransform(matrix);
                if (node.bitmap)
                    target->DrawBitmap(node.bitmap.Get(), Box(node.bounds), opacity * pose.opacity);
                else if (node.solid) {
                    Ptr<ID2D1SolidColorBrush> brush;
                    auto color = *node.solid;
                    color.a *= opacity * pose.opacity;
                    if (SUCCEEDED(target->CreateSolidColorBrush(color, brush.GetAddressOf())))
                        target->FillRectangle(Box(node.bounds), brush.Get());
                }
            }
        }
        target->SetTransform(inherited);
        target->PopAxisAlignedClip();
    }
    target->SetTransform(inherited);
    target->PopAxisAlignedClip();
}

WidgetCompositionPresenter::Rect WidgetCompositionPresenter::CaptureBounds(const Group &group,
                                                                           std::int64_t now) const noexcept {
    if (group.node.kind == WidgetCompositionKind::Content)
        return group.node.clip;
    auto bounds = group.node.bounds;
    const auto pose = Sample(group.motion, now).bounds;
    const auto right = std::max(bounds.x + bounds.width, pose.x + pose.width);
    const auto bottom = std::max(bounds.y + bounds.height, pose.y + pose.height);
    bounds.x = std::min(bounds.x, pose.x);
    bounds.y = std::min(bounds.y, pose.y);
    bounds.width = right - bounds.x;
    bounds.height = bottom - bounds.y;
    if (group.node.kind == WidgetCompositionKind::Focus) {
        for (const auto &child : scene_->nodes) {
            if (child.parent != group.node.id || child.kind != WidgetCompositionKind::Raster) continue;
            const auto transform = animation::Map(group.node.bounds, pose);
            const Rect painted{child.bounds.x * transform.scaleX + transform.offsetX,
                child.bounds.y * transform.scaleY + transform.offsetY,
                child.bounds.width * transform.scaleX, child.bounds.height * transform.scaleY};
            const auto childRight = std::max(bounds.x + bounds.width, painted.x + painted.width);
            const auto childBottom = std::max(bounds.y + bounds.height, painted.y + painted.height);
            bounds.x = std::min(bounds.x, painted.x); bounds.y = std::min(bounds.y, painted.y);
            bounds.width = childRight - bounds.x; bounds.height = childBottom - bounds.y;
        }
    }
    return bounds;
}

HRESULT WidgetCompositionPresenter::Capture(const std::wstring &id, Raster &raster, std::int64_t now) {
    const auto found = groups_.find(id);
    if (found == groups_.end() || !scene_)
        return E_INVALIDARG;
    if (found->second.node.kind == WidgetCompositionKind::Scrim) {
        for (const auto &node : scene_->nodes)
            if (node.parent == id && node.solid)
                return Upload(raster, node);
    }
    const auto bounds = CompositionRasterBounds(CaptureBounds(found->second, now), scene_->scale);
    const auto width = std::round(bounds.width * scene_->scale),
               height = std::round(bounds.height * scene_->scale);
    if (width <= 0 || height <= 0 || width > 8192 || height > 8192 ||
        width * height * 4 > WidgetCompositionScene::MaximumBytes)
        return E_OUTOFMEMORY;
    Ptr<ID2D1DeviceContext> context;
    auto hr = graphics_->CreateDeviceContext(D2D1_DEVICE_CONTEXT_OPTIONS_NONE, context.GetAddressOf());
    Ptr<ID2D1Bitmap1> bitmap;
    const auto properties =
        D2D1::BitmapProperties1(D2D1_BITMAP_OPTIONS_TARGET,
                                D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE_PREMULTIPLIED),
                                96 * scene_->scale, 96 * scene_->scale);
    if (SUCCEEDED(hr))
        hr = context->CreateBitmap(D2D1::SizeU(static_cast<UINT32>(width), static_cast<UINT32>(height)),
                                   nullptr, 0, properties, bitmap.GetAddressOf());
    if (FAILED(hr))
        return hr;
    context->SetTarget(bitmap.Get());
    // A bitmap's DPI metadata does not set the device context's drawing DPI.
    // Captured poses are in DIPs, just like the live scene, at every UI scale.
    context->SetDpi(96 * scene_->scale,96 * scene_->scale);
    context->BeginDraw();
    context->Clear(D2D1::ColorF(0, 0));
    DrawGroup(context.Get(), id, D2D1::Matrix3x2F::Translation(-bounds.x, -bounds.y), 1, now);
    hr = context->EndDraw();
    context->SetTarget(nullptr);
    if (FAILED(hr))
        return hr;
    WidgetCompositionNode node;
    node.id = id + L"/outgoing";
    node.bounds = bounds;
    node.clip = found->second.node.clip;
    node.bitmap = bitmap;
    hr = Upload(raster, node);
    if (SUCCEEDED(hr))
        ++counters_.interruptionCaptures;
    return hr;
}

HRESULT WidgetCompositionPresenter::Apply(std::shared_ptr<const WidgetCompositionScene> scene,
                                          IDCompositionVisual2 *parent, IDCompositionVisual2 *above,
                                          float offsetX, float offsetY) {
    if (!scene) {
        Clear();
        return S_OK;
    }
    if (scene->nodes.size() > WidgetCompositionScene::MaximumNodes ||
        scene->focusTargets.size() > WidgetCompositionScene::MaximumNodes ||
        scene->rasterBytes > WidgetCompositionScene::MaximumBytes || !std::isfinite(scene->scale) ||
        scene->scale <= 0)
        return E_INVALIDARG;
    const bool hadScene = static_cast<bool>(scene_);
    const bool cold = !scene_ || scene_->authority != scene->authority || scene_->scale != scene->scale ||
                      !Same(scene_->viewport, scene->viewport) || scene_->animations != scene->animations;
    if (cold || scene->directContent)
        Clear();
    const auto now = Now();
    if (scene->directContent) {
        scene_ = std::move(scene);
        return S_OK;
    }
    const auto retainFocus = [&](const Group &group) {
        if (!animation::InPlaceFocus(scene->animations.focus) || scene->reducedMotion) return false;
        return animation::HasStableFocusTarget(
            {group.node.key, group.node.clock, group.node.bounds, group.node.clip}, scene->focusTargets) &&
            std::any_of(scene->nodes.begin(), scene->nodes.end(), [&](const auto &node) {
                return node.kind == WidgetCompositionKind::Focus && node.clock == group.node.clock;
            });
    };
    std::size_t retainedBytes = scene->rasterBytes;
    for (const auto &[id, group] : groups_) {
        const auto next = std::find_if(scene->nodes.begin(), scene->nodes.end(),
                                       [&](const auto &node) { return node.id == id; });
        const bool replacement = next != scene->nodes.end() && next->key != group.node.key &&
                                 group.node.kind == WidgetCompositionKind::Content &&
                                 scene->animations.section != animation::SectionStyle::None;
        const bool dismissal = next == scene->nodes.end() &&
                               !group.closing && ((group.node.kind == WidgetCompositionKind::Modal &&
                               scene->animations.modal != animation::ModalStyle::None) ||
                               (group.node.kind == WidgetCompositionKind::Focus && retainFocus(group)));
        if (replacement || dismissal) {
            const auto bounds = CompositionRasterBounds(CaptureBounds(group, now), scene->scale);
            retainedBytes += static_cast<std::size_t>(std::round(bounds.width * scene->scale) *
                                                      std::round(bounds.height * scene->scale) * 4);
        } else if (group.previous.node.bitmap) {
            const auto pixels = group.previous.node.bitmap->GetPixelSize();
            retainedBytes += static_cast<std::size_t>(pixels.width) * pixels.height * 4;
        }
    }
    if (retainedBytes > WidgetCompositionScene::MaximumBytes && !scene->reducedMotion) {
        auto bounded = std::make_shared<WidgetCompositionScene>(*scene);
        bounded->reducedMotion = true;
        scene = std::move(bounded);
    }
    std::map<std::wstring, Raster> outgoing;
    std::set<std::wstring> desired;
    for (const auto &node : scene->nodes)
        if (node.kind != WidgetCompositionKind::Raster) {
            desired.insert(node.id);
            const auto old = groups_.find(node.id);
            if (!cold && !scene->reducedMotion &&
                scene->animations.section != animation::SectionStyle::None && old != groups_.end() &&
                old->second.node.key != node.key && node.kind == WidgetCompositionKind::Content) {
                const auto hr = Capture(node.id, outgoing[node.id], now);
                if (FAILED(hr))
                    return hr;
            }
        }
    if (!cold && !scene->reducedMotion && scene->animations.modal != animation::ModalStyle::None)
        for (auto &[id, group] : groups_) {
            if (desired.contains(id) || group.closing ||
                (group.node.kind != WidgetCompositionKind::Modal &&
                 group.node.kind != WidgetCompositionKind::Scrim))
                continue;
            const auto opacity = Sample(group.motion, now).opacity;
            auto hr = Capture(id, group.previous, now);
            if (FAILED(hr))
                return hr;
            group.closing = true;
            group.resumeOpacity = group.node.kind == WidgetCompositionKind::Modal ? opacity : 1;
            group.exit =
                animation::ModalExit(scene->animations.modal, group.previous.node.bounds, group.node.clip,
                                     opacity, group.node.kind == WidgetCompositionKind::Scrim)
                    .Start(now, Frequency(), scene->animations.speed);
        }
    if (!cold && !scene->reducedMotion) {
        for (auto &[id, group] : groups_) {
            if (desired.contains(id) || group.closing || group.node.kind != WidgetCompositionKind::Focus ||
                !retainFocus(group)) continue;
            auto hr = Capture(id, group.previous, now);
            if (FAILED(hr)) return hr;
            group.closing = true;
            group.resumeOpacity = Sample(group.motion, now).opacity;
            group.resumeFocusBounds = Sample(group.motion, now).bounds;
            // Capture already contains the sampled alpha; fade that image once.
            group.exit = animation::FocusFade(group.previous.node.bounds, group.node.clip, 1, 0,
                animation::FocusDuration(scene->animations.focus))
                .Start(now, Frequency(), scene->animations.speed);
        }
    }
    // A removed/evicted/moved control must never leave an orphan highlight.
    std::erase_if(groups_, [&](const auto &entry) {
        return entry.second.closing && entry.second.node.kind == WidgetCompositionKind::Focus &&
            !retainFocus(entry.second);
    });
    scene_ = std::move(scene);
    if (!root_) {
        Ptr<IDCompositionVisual2> visual;
        auto hr = device_->CreateVisual(visual.GetAddressOf());
        if (SUCCEEDED(hr))
            hr = visual.As(&root_);
        if (FAILED(hr))
            return hr;
    }
    auto placement = root_->SetOffsetX(offsetX);
    if (SUCCEEDED(placement))
        placement = root_->SetOffsetY(offsetY);
    if (scene_->cornerRadius > 0 && SUCCEEDED(placement)) {
        Ptr<IDCompositionRectangleClip> clip;
        placement = device_->CreateRectangleClip(clip.GetAddressOf());
        const auto bounds = Box(scene_->viewport, scene_->scale);
        const auto radius =
            std::min(scene_->cornerRadius, std::min(scene_->viewport.width, scene_->viewport.height) * .5F) *
            scene_->scale;
        if (SUCCEEDED(placement))
            placement = clip->SetLeft(bounds.left);
        if (SUCCEEDED(placement))
            placement = clip->SetTop(bounds.top);
        if (SUCCEEDED(placement))
            placement = clip->SetRight(bounds.right);
        if (SUCCEEDED(placement))
            placement = clip->SetBottom(bounds.bottom);
        if (SUCCEEDED(placement))
            placement = clip->SetTopLeftRadiusX(radius);
        if (SUCCEEDED(placement))
            placement = clip->SetTopLeftRadiusY(radius);
        if (SUCCEEDED(placement))
            placement = clip->SetTopRightRadiusX(radius);
        if (SUCCEEDED(placement))
            placement = clip->SetTopRightRadiusY(radius);
        if (SUCCEEDED(placement))
            placement = clip->SetBottomLeftRadiusX(radius);
        if (SUCCEEDED(placement))
            placement = clip->SetBottomLeftRadiusY(radius);
        if (SUCCEEDED(placement))
            placement = clip->SetBottomRightRadiusX(radius);
        if (SUCCEEDED(placement))
            placement = clip->SetBottomRightRadiusY(radius);
        if (SUCCEEDED(placement))
            placement = root_->SetClip(clip.Get());
    } else if (SUCCEEDED(placement))
        placement = root_->SetClip(Box(scene_->viewport, scene_->scale));
    if (FAILED(placement))
        return placement;
    std::size_t paintOrder{};
    for (const auto &node : scene_->nodes) {
        const auto nodePaintOrder = paintOrder++;
        if (node.kind == WidgetCompositionKind::Raster) {
            const bool fadeSurface = std::any_of(scene_->nodes.begin(), scene_->nodes.end(), [&](const auto &parentNode) {
                    return parentNode.id == node.parent && parentNode.kind == WidgetCompositionKind::FocusSurface;
                });
            if (fadeSurface) {
                if (!node.bitmap && !node.solid) return E_INVALIDARG;
                auto &raster = rasters_[node.id];
                raster.node = node;
                raster.pixels = node.solid ? D2D1::SizeU(1, 1) : node.bitmap->GetPixelSize();
                continue;
            }
            auto hr = Upload(rasters_[node.id], node);
            if (FAILED(hr))
                return hr;
            continue;
        }
        auto old = groups_.find(node.id);
        const bool existed = old != groups_.end();
        auto &group = groups_[node.id];
        const bool changed = !existed || group.node.key != node.key || group.closing;
        const bool contextChanged = existed && (node.kind == WidgetCompositionKind::Focus || node.kind == WidgetCompositionKind::Control ||
            node.kind == WidgetCompositionKind::FocusSurface) &&
            (group.node.clock != node.clock || group.node.parent != node.parent);
        const bool surfaceFocused = node.kind == WidgetCompositionKind::FocusSurface &&
            std::any_of(scene_->nodes.begin(), scene_->nodes.end(), [&](const auto &focus) {
                return focus.kind == WidgetCompositionKind::Focus && focus.key == node.key && focus.clock == node.clock;
            });
        const bool surfaceChanged = node.kind == WidgetCompositionKind::FocusSurface &&
            group.motion.to.opacity != (surfaceFocused ? 1.0F : 0.0F);
        const bool scaleChanged = existed && node.kind == WidgetCompositionKind::Control &&
            group.node.controlScale != node.controlScale;
        const bool boundsChanged =
            !existed || !Same(group.node.bounds, node.bounds) || !Same(group.node.clip, node.clip);
        auto previous = group.closing ? Sample(group.exit, now) : Sample(group.motion, now);
        const bool reopening = group.closing;
        if (reopening) {
            previous.opacity *= group.resumeOpacity;
            if (node.kind == WidgetCompositionKind::Focus) previous.bounds = group.resumeFocusBounds;
        }
        const auto previousEnd = group.motion.start + group.motion.duration;
        const auto previousMotion = group.motion;
        const auto priorOrder = group.node.order;
        if (changed)
            group.direction = node.order >= priorOrder ? 1 : -1;
        if (!group.root)
            for (auto *destination :
                 {std::addressof(group.root), std::addressof(group.incoming), std::addressof(group.outgoing),
                  std::addressof(group.incomingClip), std::addressof(group.outgoingClip)}) {
                Ptr<IDCompositionVisual2> visual;
                auto hr = device_->CreateVisual(visual.GetAddressOf());
                if (SUCCEEDED(hr))
                    hr = visual.As(destination);
                if (FAILED(hr))
                    return hr;
            }
        group.closing = false;
        group.node = node;
        group.paintOrder = nodePaintOrder;
        if (reopening || scene_->reducedMotion)
            group.previous = {};
        const auto clipped = group.root->SetClip(Box(node.clip, scene_->scale));
        if (FAILED(clipped))
            return clipped;
        if (auto captured = outgoing.find(node.id); captured != outgoing.end())
            group.previous = std::move(captured->second);
        if (changed || boundsChanged || contextChanged || surfaceChanged || scaleChanged || scene_->reducedMotion) {
            auto recipe = animation::Stationary(node.bounds, node.clip);
            if (node.kind == WidgetCompositionKind::Focus) {
                if (animation::InPlaceFocus(scene_->animations.focus)) {
                    // Geometry/scope changes invalidate a sampled pose. First
                    // presentation also snaps; same-target updates retain their clock.
                    if (!cold && !(existed && (boundsChanged || contextChanged)))
                        recipe = animation::FocusEnter(scene_->animations.focus, node.bounds, node.clip,
                            existed ? std::optional<Pose>{previous} : std::nullopt);
                }
            } else if (node.kind == WidgetCompositionKind::Popup) {
                // Only an actual opening gets an entrance. Highlight/content
                // updates retain the clock; reflow and policy changes snap.
                if ((!existed || changed) && (!cold || !hadScene))
                    recipe = animation::PopupEnter(node.bounds, node.clip, node.popupAnchor);
            } else if (node.kind == WidgetCompositionKind::Control) {
                recipe = animation::ControlScale(node.bounds, node.clip, previous, node.controlScale,
                    existed && !changed && !boundsChanged && !contextChanged ? node.controlDuration : 0, node.controlCurve);
            } else if (node.kind == WidgetCompositionKind::FocusSurface) {
                // Fade blends this control's two surfaces at fixed geometry.
                // Every supported focus preset uses these shared fixed-size surfaces.
                const float weight = surfaceFocused ? 1.0F : 0.0F;
                recipe = animation::FocusFade(node.bounds, node.clip,
                    existed && !changed && !boundsChanged && !contextChanged ? previous.opacity : weight, weight,
                    animation::InPlaceFocus(scene_->animations.focus) ? animation::FocusDuration(scene_->animations.focus) : 0);
            } else if (node.kind == WidgetCompositionKind::Content) {
                if (existed && (changed || previousEnd > now)) {
                    const auto plan = animation::Section(scene_->animations.section, node.bounds, node.clip,
                                                         group.previous.node.bounds, group.direction);
                    recipe = plan.incoming;
                    // A same-key layout update changes geometry, not the
                    // animation clock. Rebuild both paths together so the
                    // outgoing reveal stays aligned with the incoming edge.
                    group.exit = plan.outgoing.Start(changed ? now : previousMotion.start, Frequency(), scene_->animations.speed);
                }
            } else if (node.kind == WidgetCompositionKind::Modal ||
                       node.kind == WidgetCompositionKind::Scrim) {
                recipe = animation::ModalEnter(scene_->animations.modal, node.bounds, node.clip,
                                               existed ? previous.opacity
                                               : cold  ? 1
                                                       : 0,
                                               node.kind == WidgetCompositionKind::Scrim);
            } else if (existed) {
                recipe = animation::Layout(scene_->animations.section, node.bounds, node.clip, previous,
                    node.kind == WidgetCompositionKind::Selection);
            }
            group.motion = recipe.Start(now, Frequency(), scene_->animations.speed);
            if (!changed && node.kind != WidgetCompositionKind::Focus && node.kind != WidgetCompositionKind::Control &&
                node.kind != WidgetCompositionKind::Popup && node.kind != WidgetCompositionKind::FocusSurface) {
                if (node.kind == WidgetCompositionKind::Content && previousEnd > now) {
                    group.motion.start = previousMotion.start;
                    group.motion.duration = previousMotion.duration;
                } else
                    group.motion.duration = std::max<std::int64_t>(0, previousEnd - now);
            }
            if ((cold && node.kind != WidgetCompositionKind::Popup) || scene_->reducedMotion)
                group.motion.duration = 0;
            auto hr = node.kind == WidgetCompositionKind::FocusSurface ? S_OK
                : Animate(group.incoming.Get(), group.incomingClip.Get(), group.motion, node.bounds);
            if (FAILED(hr))
                return hr;
        }
        if ((changed || boundsChanged || scene_->reducedMotion) && group.previous.visual) {
            const auto hr = Animate(group.outgoing.Get(), group.outgoingClip.Get(), group.exit,
                                    group.previous.node.bounds);
            if (FAILED(hr))
                return hr;
        }
    }
    std::erase_if(groups_, [&](const auto &entry) {
        return !desired.contains(entry.first) && (!entry.second.closing || scene_->reducedMotion);
    });
    const auto hr = Rebuild(above);
    if (FAILED(hr))
        return hr;
    if (attached_ && parent_) {
        const auto detached = parent_->RemoveVisual(root_.Get());
        if (FAILED(detached))
            return detached;
        attached_ = false;
    }
    parent_ = parent;
    const auto attached = parent_->AddVisual(root_.Get(), FALSE, above);
    if (SUCCEEDED(attached)) {
        attached_ = true;
        ++counters_.sceneCommits;
    }
    return attached;
}

HRESULT WidgetCompositionPresenter::ConfigureFocusSurface(Group &group) {
    const Raster *idle{}, *focused{};
    for (const auto &node : scene_->nodes) {
        if (node.parent != group.node.id || node.kind != WidgetCompositionKind::Raster) continue;
        if (node.order == 0) idle = &rasters_.at(node.id);
        else focused = &rasters_.at(node.id);
    }
    if (!idle || !focused) return E_INVALIDARG;
    return ConfigureFocusFade(group, *idle, *focused);
}

HRESULT WidgetCompositionPresenter::CreateAnimation(float from, float to, const Motion &motion,
                                                     IDCompositionAnimation **output) {
    Ptr<IDCompositionAnimation> value;
    auto hr = device_->CreateAnimation(value.GetAddressOf());
    const double seconds = static_cast<double>(motion.duration) / Frequency();
    const auto coefficients = motion.curve.Coefficients(from, to, seconds);
    LARGE_INTEGER begin{};
    begin.QuadPart = motion.start;
    if (SUCCEEDED(hr)) hr = value->SetAbsoluteBeginTime(begin);
    if (SUCCEEDED(hr)) hr = value->AddCubic(0, coefficients[0], coefficients[1], coefficients[2], coefficients[3]);
    if (SUCCEEDED(hr)) hr = value->End(seconds, to);
    if (SUCCEEDED(hr)) *output = value.Detach();
    return hr;
}

HRESULT WidgetCompositionPresenter::ConfigureFocusFade(Group &group, const Raster &idle, const Raster &focused) {
    // Arithmetic interpolation preserves premultiplied alpha. Two source-over
    // opacity layers would darken translucent themes halfway through the fade.
    Ptr<IDCompositionDevice3> effects;
    auto hr = device_->QueryInterface(IID_PPV_ARGS(effects.GetAddressOf()));
    if (SUCCEEDED(hr) && !group.focusBlend)
        hr = effects->CreateArithmeticCompositeEffect(group.focusBlend.GetAddressOf());
    if (idle.pixels.width != focused.pixels.width || idle.pixels.height != focused.pixels.height ||
        !Same(idle.node.bounds, focused.node.bounds) || !idle.pixels.width || !idle.pixels.height) return E_INVALIDARG;
    // Filter inputs must be effects (or null for the source visual), not raw
    // composition surfaces. Pack both rasters into one source and sample its
    // second half through a translated effect. Keep placement outside this graph.
    const bool vertical = idle.pixels.height <= idle.pixels.width;
    const UINT width = idle.pixels.width, height = idle.pixels.height;
    const auto atlasSize = D2D1::SizeU(width * (vertical ? 1 : 2), height * (vertical ? 2 : 1));
    const float secondX = vertical ? 0.0F : static_cast<float>(width);
    const float secondY = vertical ? static_cast<float>(height) : 0.0F;
    if (SUCCEEDED(hr) && (!group.focusAtlas || group.focusAtlasPixels.width != atlasSize.width ||
        group.focusAtlasPixels.height != atlasSize.height)) {
        group.focusAtlas.Reset();
        hr = device_->CreateSurface(atlasSize.width, atlasSize.height, DXGI_FORMAT_B8G8R8A8_UNORM,
            DXGI_ALPHA_MODE_PREMULTIPLIED, group.focusAtlas.GetAddressOf());
        if (SUCCEEDED(hr)) group.focusAtlasPixels = atlasSize;
    }
    Ptr<ID2D1DeviceContext> target;
    POINT offset{};
    if (SUCCEEDED(hr)) hr = group.focusAtlas->BeginDraw(nullptr, IID_PPV_ARGS(target.GetAddressOf()), &offset);
    if (FAILED(hr)) return hr;
    target->SetDpi(96, 96);
    target->SetTransform(D2D1::Matrix3x2F::Translation(static_cast<float>(offset.x), static_cast<float>(offset.y)));
    target->Clear(D2D1::ColorF(0, 0));
    const auto paint = [&](const Raster &raster, float x, float y) {
        const auto bounds = D2D1::RectF(x, y, x + width, y + height);
        if (raster.node.bitmap) target->DrawBitmap(raster.node.bitmap.Get(), bounds);
        else {
            Ptr<ID2D1SolidColorBrush> brush;
            const auto status = target->CreateSolidColorBrush(*raster.node.solid, brush.GetAddressOf());
            if (FAILED(status)) return status;
            target->FillRectangle(bounds, brush.Get());
        }
        return S_OK;
    };
    hr = paint(idle, 0, 0);
    if (SUCCEEDED(hr)) hr = paint(focused, secondX, secondY);
    const auto ended = group.focusAtlas->EndDraw();
    if (SUCCEEDED(hr)) hr = ended;
    if (SUCCEEDED(hr)) ++counters_.rasterUploads;
    if (SUCCEEDED(hr) && !group.focusSample)
        hr = effects->CreateAffineTransform2DEffect(group.focusSample.GetAddressOf());
    if (SUCCEEDED(hr)) hr = group.focusSample->SetInput(0, nullptr, 0);
    if (SUCCEEDED(hr)) hr = group.focusSample->SetTransformMatrix(D2D1::Matrix3x2F::Translation(-secondX, -secondY));
    if (SUCCEEDED(hr)) hr = group.focusSample->SetInterpolationMode(D2D1_2DAFFINETRANSFORM_INTERPOLATION_MODE_NEAREST_NEIGHBOR);
    if (SUCCEEDED(hr)) hr = group.focusBlend->SetInput(0, nullptr, 0);
    if (SUCCEEDED(hr)) hr = group.focusBlend->SetInput(1, group.focusSample.Get(), 0);
    if (SUCCEEDED(hr)) hr = group.focusBlend->SetCoefficients({0, 1 - group.motion.to.opacity, group.motion.to.opacity, 0});
    if (SUCCEEDED(hr)) hr = group.focusBlend->SetClampOutput(TRUE);
    if (SUCCEEDED(hr) && group.motion.duration > 0) {
        Ptr<IDCompositionAnimation> before, after;
        hr = CreateAnimation(1 - group.motion.from.opacity, 1 - group.motion.to.opacity, group.motion, before.GetAddressOf());
        if (SUCCEEDED(hr)) hr = CreateAnimation(group.motion.from.opacity, group.motion.to.opacity, group.motion, after.GetAddressOf());
        if (SUCCEEDED(hr)) hr = group.focusBlend->SetCoefficient2(before.Get());
        if (SUCCEEDED(hr)) hr = group.focusBlend->SetCoefficient3(after.Get());
    }
    if (SUCCEEDED(hr)) hr = group.root->RemoveAllVisuals();
    if (SUCCEEDED(hr)) hr = group.incoming->RemoveAllVisuals();
    if (SUCCEEDED(hr)) hr = group.incomingClip->RemoveAllVisuals();
    if (SUCCEEDED(hr)) hr = group.outgoingClip->RemoveAllVisuals();
    if (SUCCEEDED(hr)) hr = group.incoming->SetContent(group.focusAtlas.Get());
    if (SUCCEEDED(hr)) hr = group.incoming->SetEffect(group.focusBlend.Get());
    if (SUCCEEDED(hr)) hr = group.outgoingClip->SetClip(D2D1::RectF(0, 0, static_cast<float>(width), static_cast<float>(height)));
    if (SUCCEEDED(hr)) hr = group.outgoingClip->AddVisual(group.incoming.Get(), FALSE, nullptr);
    if (SUCCEEDED(hr)) hr = group.incomingClip->AddVisual(group.outgoingClip.Get(), FALSE, nullptr);
    // Place the clipped result, not the graph's untransformed source input.
    const auto scale = scene_->scale;
    const D2D_MATRIX_3X2_F matrix{idle.node.bounds.width * scale / idle.pixels.width, 0, 0,
        idle.node.bounds.height * scale / idle.pixels.height, idle.node.bounds.x * scale, idle.node.bounds.y * scale};
    if (SUCCEEDED(hr)) hr = static_cast<IDCompositionVisual2 *>(group.incomingClip.Get())->SetTransform(matrix);
    if (SUCCEEDED(hr)) hr = group.root->AddVisual(group.incomingClip.Get(), FALSE, nullptr);
    return hr;
}

void WidgetCompositionPresenter::DrawFocusFade(ID2D1RenderTarget *target, const Group &group,
                                               D2D1_MATRIX_3X2_F inherited, float opacity, std::int64_t now) const {
    Ptr<ID2D1DeviceContext> context;
    if (FAILED(target->QueryInterface(IID_PPV_ARGS(context.GetAddressOf())))) return;
    Ptr<ID2D1Effect> blend;
    if (FAILED(context->CreateEffect(CLSID_D2D1ArithmeticComposite, blend.GetAddressOf()))) return;
    const WidgetCompositionNode *idle{};
    for (const auto &node : scene_->nodes) {
        if (node.parent != group.node.id || node.kind != WidgetCompositionKind::Raster) continue;
        const auto index = node.order == 0 ? 0U : 1U;
        if (index == 0) idle = &node;
        if (node.bitmap) blend->SetInput(index, node.bitmap.Get());
        else if (node.solid) {
            Ptr<ID2D1Effect> flood;
            if (FAILED(context->CreateEffect(CLSID_D2D1Flood, flood.GetAddressOf()))) return;
            flood->SetValue(D2D1_FLOOD_PROP_COLOR, *node.solid);
            blend->SetInputEffect(index, flood.Get());
        }
    }
    if (!idle) return;
    const float weight = Sample(group.motion, now).opacity;
    blend->SetValue(D2D1_ARITHMETICCOMPOSITE_PROP_COEFFICIENTS,
        D2D1::Vector4F(0, (1 - weight) * opacity, weight * opacity, 0));
    blend->SetValue(D2D1_ARITHMETICCOMPOSITE_PROP_CLAMP_OUTPUT, TRUE);
    const auto size = idle->bitmap ? idle->bitmap->GetSize() : D2D1::SizeF(1, 1);
    context->SetTransform(Transform({0, 0, size.width, size.height}, idle->bounds) * Matrix(inherited));
    const auto source = D2D1::RectF(0, 0, size.width, size.height);
    context->DrawImage(blend.Get(), D2D1::Point2F(), source);
    context->SetTransform(inherited);
}

HRESULT WidgetCompositionPresenter::Rebuild(IDCompositionVisual2 *) {
    auto hr = root_->RemoveAllVisuals();
    if (FAILED(hr))
        return hr;
    for (auto &[_, group] : groups_) {
        if (group.node.kind == WidgetCompositionKind::FocusSurface) continue;
        hr = group.root->RemoveAllVisuals();
        if (SUCCEEDED(hr))
            hr = group.incomingClip->RemoveAllVisuals();
        if (SUCCEEDED(hr))
            hr = group.outgoingClip->RemoveAllVisuals();
        if (SUCCEEDED(hr))
            hr = group.incoming->RemoveAllVisuals();
        if (SUCCEEDED(hr))
            hr = group.outgoing->RemoveAllVisuals();
        if (SUCCEEDED(hr) && group.previous.visual)
            hr = group.outgoing->AddVisual(group.previous.visual.Get(), TRUE, nullptr);
        if (SUCCEEDED(hr))
            hr = group.outgoingClip->AddVisual(group.outgoing.Get(), FALSE, nullptr);
        if (SUCCEEDED(hr))
            hr = group.root->AddVisual(group.outgoingClip.Get(), FALSE, nullptr);
        if (FAILED(hr))
            return hr;
        if (!group.closing) {
            hr = group.incomingClip->AddVisual(group.incoming.Get(), FALSE, nullptr);
            if (SUCCEEDED(hr))
                hr = group.root->AddVisual(group.incomingClip.Get(), TRUE, group.outgoingClip.Get());
            if (FAILED(hr))
                return hr;
        } else {
            hr = Animate(group.outgoing.Get(), group.outgoingClip.Get(), group.exit,
                         group.previous.node.bounds);
            if (FAILED(hr))
                return hr;
        }
    }
    std::set<std::wstring> usedRasters;
    std::map<IDCompositionVisual *, IDCompositionVisual *> lastChild;
    const auto append = [&](IDCompositionVisual *parent, IDCompositionVisual *child) {
        auto *&previous = lastChild[parent];
        const auto result = parent->AddVisual(child, previous != nullptr, previous);
        if (SUCCEEDED(result))
            previous = child;
        return result;
    };
    for (const auto &node : scene_->nodes) {
        auto *parent = node.parent.empty() ? root_.Get() : groups_.at(node.parent).incoming.Get();
        if (node.kind == WidgetCompositionKind::Raster) {
            usedRasters.insert(node.id);
            if (node.parent.empty() || groups_.at(node.parent).node.kind != WidgetCompositionKind::FocusSurface)
                hr = append(parent, rasters_.at(node.id).visual.Get());
        } else {
            if (node.kind == WidgetCompositionKind::FocusSurface) {
                hr = ConfigureFocusSurface(groups_.at(node.id));
                if (FAILED(hr)) return hr;
            }
            hr = append(parent, groups_.at(node.id).root.Get());
        }
        if (FAILED(hr))
            return hr;
    }
    std::vector<Group *> closing;
    for (auto &[_, group] : groups_)
        if (group.closing)
            closing.push_back(&group);
    std::sort(closing.begin(), closing.end(),
              [](const auto *left, const auto *right) { return left->paintOrder < right->paintOrder; });
    for (const auto *group : closing) {
        auto *parent = group->node.parent.empty()             ? root_.Get()
                       : groups_.contains(group->node.parent) ? groups_.at(group->node.parent).incoming.Get()
                                                              : root_.Get();
        hr = append(parent, group->root.Get());
        if (FAILED(hr))
            return hr;
    }
    std::erase_if(rasters_, [&](const auto &entry) { return !usedRasters.contains(entry.first); });
    return S_OK;
}

HRESULT WidgetCompositionPresenter::Advance() {
    const auto now = Now();
    bool changed{};
    for (auto &[_, group] : groups_)
        if (group.previous.visual && now >= group.exit.start + group.exit.duration) {
            const auto hr = group.outgoing->RemoveAllVisuals();
            if (FAILED(hr))
                return hr;
            group.previous = {};
            changed = true;
        }
    for (auto it = groups_.begin(); it != groups_.end();) {
        if (!it->second.closing || now < it->second.exit.start + it->second.exit.duration) {
            ++it;
            continue;
        }
        HRESULT hr = S_OK;
        if (it->second.node.parent.empty())
            hr = root_->RemoveVisual(it->second.root.Get());
        else if (auto parent = groups_.find(it->second.node.parent); parent != groups_.end())
            hr = parent->second.incoming->RemoveVisual(it->second.root.Get());
        if (FAILED(hr))
            return hr;
        it = groups_.erase(it);
        changed = true;
    }
    return changed ? device_->Commit() : S_FALSE;
}

bool WidgetCompositionPresenter::active() const noexcept {
    const auto now = Now();
    return std::any_of(groups_.begin(), groups_.end(), [&](const auto &entry) {
        return entry.second.closing || entry.second.previous.visual ||
               now < entry.second.motion.start + entry.second.motion.duration;
    });
}

D2D1_POINT_2F WidgetCompositionPresenter::MapInput(D2D1_POINT_2F point) const noexcept {
    if (!scene_)
        return point;
    const auto now = Now();
    // Walk outer groups before inner groups, reversing only their live incoming
    // transform. Outgoing rasters never contribute an input target.
    std::wstring_view parent;
    for (std::size_t depth = 0; depth < WidgetCompositionScene::MaximumNodes; ++depth) {
        const Group *match{};
        bool movingContent{};
        for (auto it = scene_->nodes.rbegin(); it != scene_->nodes.rend(); ++it) {
            if (it->parent != parent || it->kind == WidgetCompositionKind::Raster ||
                it->kind == WidgetCompositionKind::Selection || it->kind == WidgetCompositionKind::Scrim ||
                it->kind == WidgetCompositionKind::Focus || it->kind == WidgetCompositionKind::FocusSurface)
                continue;
            const auto group = groups_.find(it->id);
            if (group == groups_.end() || group->second.closing)
                continue;
            const auto pose = Sample(group->second.motion, now);
            if ((it->kind == WidgetCompositionKind::Modal || it->kind == WidgetCompositionKind::Popup) && Contains(it->clip, point) &&
                (pose.opacity <= .01F || !Contains(pose.bounds, point)))
                return {std::numeric_limits<float>::quiet_NaN(), std::numeric_limits<float>::quiet_NaN()};
            if (it->kind == WidgetCompositionKind::Content && Contains(it->clip, point))
                movingContent = true;
            if (pose.opacity > .01F && Contains(it->clip, point) && Contains(pose.clip, point) &&
                Contains(pose.bounds, point)) {
                match = &group->second;
                break;
            }
        }
        if (!match) {
            if (movingContent)
                return {std::numeric_limits<float>::quiet_NaN(), std::numeric_limits<float>::quiet_NaN()};
            break;
        }
        const auto pose = Sample(match->motion, now);
        const auto transform = animation::Map(match->node.bounds, pose.bounds);
        if (transform.scaleX <= 0 || transform.scaleY <= 0)
            return {std::numeric_limits<float>::quiet_NaN(), std::numeric_limits<float>::quiet_NaN()};
        if (match->node.kind != WidgetCompositionKind::Control) {
            point.x = (point.x - transform.offsetX) / transform.scaleX;
            point.y = (point.y - transform.offsetY) / transform.scaleY;
        }
        parent = match->node.id;
    }
    return point;
}

void WidgetCompositionPresenter::Clear() noexcept {
    if (attached_ && parent_ && root_)
        parent_->RemoveVisual(root_.Get());
    attached_ = false;
    groups_.clear();
    rasters_.clear();
    root_.Reset();
    parent_.Reset();
    scene_.reset();
}
} // namespace widgetrail
