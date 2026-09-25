#include "WidgetCompositionPresenter.h"
#include <algorithm>
#include <cmath>
#include <functional>
#include <set>
#include <limits>

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
D2D1::Matrix3x2F Transform(Rect basis, Rect pose, bool resize) {
    const auto sx = resize && basis.width > 0 ? pose.width / basis.width : 1;
    const auto sy = resize && basis.height > 0 ? pose.height / basis.height : 1;
    return {sx, 0, 0, sy, pose.x - basis.x * sx, pose.y - basis.y * sy};
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
std::int64_t WidgetCompositionPresenter::Duration() noexcept {
    LARGE_INTEGER v{};
    QueryPerformanceFrequency(&v);
    return v.QuadPart * 180 / 1000;
}

WidgetCompositionPresenter::Pose WidgetCompositionPresenter::Sample(const Motion &motion,
                                                                    std::int64_t now) noexcept {
    if (motion.duration <= 0 || now >= motion.start + motion.duration)
        return motion.to;
    const auto t = std::clamp(static_cast<double>(now - motion.start) / motion.duration, 0.0, 1.0);
    const auto p = static_cast<float>(1 - (1 - t) * (1 - t) * (1 - t));
    const auto mix = [p](float a, float b) { return a + (b - a) * p; };
    return {{mix(motion.from.bounds.x, motion.to.bounds.x), mix(motion.from.bounds.y, motion.to.bounds.y),
             mix(motion.from.bounds.width, motion.to.bounds.width),
             mix(motion.from.bounds.height, motion.to.bounds.height)},
            mix(motion.from.opacity, motion.to.opacity)};
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

HRESULT WidgetCompositionPresenter::Animate(IDCompositionVisual3 *visual, const Motion &motion, Rect basis,
                                            bool resize) {
    const auto from = Transform(basis, motion.from.bounds, resize);
    const auto to = Transform(basis, motion.to.bounds, resize);
    const auto scale = scene_->scale;
    if (!motion.duration) {
        D2D_MATRIX_3X2_F matrix{to._11, 0, 0, to._22, to._31 * scale, to._32 * scale};
        auto hr = static_cast<IDCompositionVisual2 *>(visual)->SetTransform(matrix);
        return FAILED(hr) ? hr : visual->SetOpacity(motion.to.opacity);
    }
    Ptr<IDCompositionMatrixTransform> transform;
    auto hr = device_->CreateMatrixTransform(transform.GetAddressOf());
    LARGE_INTEGER frequency{};
    QueryPerformanceFrequency(&frequency);
    const double seconds = static_cast<double>(motion.duration) / frequency.QuadPart;
    const auto animate = [&](float a, float b, IDCompositionAnimation **output) {
        Ptr<IDCompositionAnimation> animation;
        auto result = device_->CreateAnimation(animation.GetAddressOf());
        const auto delta = static_cast<double>(b - a);
        LARGE_INTEGER begin{};
        begin.QuadPart = motion.start;
        if (SUCCEEDED(result))
            result = animation->SetAbsoluteBeginTime(begin);
        if (SUCCEEDED(result))
            result = animation->AddCubic(0, a, static_cast<float>(3 * delta / seconds),
                                         static_cast<float>(-3 * delta / (seconds * seconds)),
                                         static_cast<float>(delta / (seconds * seconds * seconds)));
        if (SUCCEEDED(result))
            result = animation->End(seconds, b);
        if (SUCCEEDED(result))
            *output = animation.Detach();
        return result;
    };
    const float a[]{from._11, from._22, from._31 * scale, from._32 * scale};
    const float b[]{to._11, to._22, to._31 * scale, to._32 * scale};
    const int rows[]{0, 1, 2, 2}, columns[]{0, 1, 0, 1};
    for (int i = 0; SUCCEEDED(hr) && i < 4; ++i) {
        Ptr<IDCompositionAnimation> animation;
        hr = animate(a[i], b[i], animation.GetAddressOf());
        if (SUCCEEDED(hr))
            hr = transform->SetMatrixElement(rows[i], columns[i], animation.Get());
    }
    if (SUCCEEDED(hr))
        hr = static_cast<IDCompositionVisual2 *>(visual)->SetTransform(transform.Get());
    Ptr<IDCompositionAnimation> opacity;
    if (SUCCEEDED(hr))
        hr = animate(motion.from.opacity, motion.to.opacity, opacity.GetAddressOf());
    if (SUCCEEDED(hr))
        hr = visual->SetOpacity(opacity.Get());
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
    if (group.previous.node.bitmap) {
        const auto exit = Sample(group.exit, now);
        target->SetTransform(Transform(group.previous.node.bounds, exit.bounds, false) * Matrix(inherited));
        target->DrawBitmap(group.previous.node.bitmap.Get(), Box(group.previous.node.bounds),
                           opacity * exit.opacity);
    }
    if (!group.closing) {
        const auto pose = Sample(group.motion, now);
        const auto matrix =
            Transform(group.node.bounds, pose.bounds, group.node.kind == WidgetCompositionKind::Selection) *
            Matrix(inherited);
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
    }
    target->SetTransform(inherited);
    target->PopAxisAlignedClip();
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
    auto bounds = found->second.node.kind == WidgetCompositionKind::Content ? found->second.node.clip
                                                                            : found->second.node.bounds;
    if (found->second.node.kind == WidgetCompositionKind::Modal) {
        const auto pose = Sample(found->second.motion, now).bounds;
        const auto right = std::max(bounds.x + bounds.width, pose.x + pose.width),
                   bottom = std::max(bounds.y + bounds.height, pose.y + pose.height);
        bounds.x = std::min(bounds.x, pose.x);
        bounds.y = std::min(bounds.y, pose.y);
        bounds.width = right - bounds.x;
        bounds.height = bottom - bounds.y;
    }
    const auto width = std::ceil(bounds.width * scene_->scale),
               height = std::ceil(bounds.height * scene_->scale);
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
        scene->rasterBytes > WidgetCompositionScene::MaximumBytes || !std::isfinite(scene->scale) ||
        scene->scale <= 0)
        return E_INVALIDARG;
    const bool cold = !scene_ || scene_->authority != scene->authority || scene_->scale != scene->scale ||
                      !Same(scene_->viewport, scene->viewport);
    if (cold || scene->directContent)
        Clear();
    const auto now = Now();
    if (scene->directContent) {
        scene_ = std::move(scene);
        return S_OK;
    }
    std::size_t retainedBytes = scene->rasterBytes;
    for (const auto &[id, group] : groups_) {
        const auto next = std::find_if(scene->nodes.begin(), scene->nodes.end(),
                                       [&](const auto &node) { return node.id == id; });
        const bool replacement = next != scene->nodes.end() && next->key != group.node.key &&
                                 group.node.kind == WidgetCompositionKind::Content;
        const bool dismissal =
            next == scene->nodes.end() && group.node.kind == WidgetCompositionKind::Modal && !group.closing;
        if (replacement || dismissal) {
            const auto bounds = replacement ? group.node.clip : group.node.bounds;
            retainedBytes += static_cast<std::size_t>(
                std::ceil(bounds.width * scene->scale) *
                std::ceil((bounds.height + (dismissal ? 14 : 0)) * scene->scale) * 4);
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
            if (!cold && !scene->reducedMotion && old != groups_.end() && old->second.node.key != node.key &&
                node.kind == WidgetCompositionKind::Content) {
                const auto hr = Capture(node.id, outgoing[node.id], now);
                if (FAILED(hr))
                    return hr;
            }
        }
    if (!cold && !scene->reducedMotion)
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
            group.exit = {
                {group.previous.node.bounds, group.node.kind == WidgetCompositionKind::Scrim ? opacity : 1},
                {group.previous.node.bounds, 0},
                now,
                Duration()};
            if (group.node.kind == WidgetCompositionKind::Modal)
                group.exit.to.bounds.y += 14 * opacity;
        }
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
            auto hr = Upload(rasters_[node.id], node);
            if (FAILED(hr))
                return hr;
            continue;
        }
        auto old = groups_.find(node.id);
        const bool existed = old != groups_.end();
        auto &group = groups_[node.id];
        const bool changed = !existed || group.node.key != node.key || group.closing;
        const bool boundsChanged = !existed || !Same(group.node.bounds, node.bounds);
        auto previous = group.closing ? Sample(group.exit, now) : Sample(group.motion, now);
        const bool reopening = group.closing;
        if (reopening)
            previous.opacity *= group.resumeOpacity;
        const auto previousEnd = group.motion.start + group.motion.duration;
        const auto priorOrder = group.node.order;
        if (!group.root)
            for (auto *destination : {std::addressof(group.root), std::addressof(group.incoming),
                                      std::addressof(group.outgoing)}) {
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
        if (changed || boundsChanged || scene_->reducedMotion) {
            const auto duration = cold || scene_->reducedMotion ? 0
                                  : changed ? Duration()
                                            : std::max<std::int64_t>(0, previousEnd - now);
            group.motion = {{node.bounds, 1}, {node.bounds, 1}, now, duration};
            if (node.kind == WidgetCompositionKind::Content) {
                if (existed && changed && duration) {
                    group.motion.from.bounds.x += (node.order >= priorOrder ? 24 : -24);
                    group.motion.from.opacity = 0;
                } else if (!changed && duration)
                    group.motion.from = previous;
                else
                    group.motion.duration = 0;
            } else if (node.kind == WidgetCompositionKind::Modal ||
                       node.kind == WidgetCompositionKind::Scrim) {
                group.motion.from.opacity = existed ? previous.opacity : cold ? 1 : 0;
                if (node.kind == WidgetCompositionKind::Modal)
                    group.motion.from.bounds.y += 14 * (1 - group.motion.from.opacity);
            } else if (existed)
                group.motion.from.bounds = previous.bounds;
            else
                group.motion.duration = 0;
            auto hr = Animate(group.incoming.Get(), group.motion, node.bounds,
                              node.kind == WidgetCompositionKind::Selection);
            if (FAILED(hr))
                return hr;
            if (changed && group.previous.node.bitmap) {
                group.exit = {{group.previous.node.bounds, 1},
                              {group.previous.node.bounds, 0},
                              now,
                              group.motion.duration};
                group.exit.to.bounds.x -= (node.order >= priorOrder ? 24 : -24);
            }
        }
        if ((changed || boundsChanged || scene_->reducedMotion) && group.previous.visual) {
            const auto hr = Animate(group.outgoing.Get(), group.exit, group.previous.node.bounds, false);
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

HRESULT WidgetCompositionPresenter::Rebuild(IDCompositionVisual2 *) {
    auto hr = root_->RemoveAllVisuals();
    if (FAILED(hr))
        return hr;
    for (auto &[_, group] : groups_) {
        hr = group.root->RemoveAllVisuals();
        if (SUCCEEDED(hr))
            hr = group.incoming->RemoveAllVisuals();
        if (SUCCEEDED(hr))
            hr = group.outgoing->RemoveAllVisuals();
        if (SUCCEEDED(hr) && group.previous.visual)
            hr = group.outgoing->AddVisual(group.previous.visual.Get(), TRUE, nullptr);
        if (SUCCEEDED(hr))
            hr = group.root->AddVisual(group.outgoing.Get(), TRUE, nullptr);
        if (FAILED(hr))
            return hr;
        if (!group.closing) {
            hr = group.root->AddVisual(group.incoming.Get(), TRUE, group.outgoing.Get());
            if (FAILED(hr))
                return hr;
        } else {
            hr = Animate(group.outgoing.Get(), group.exit, group.previous.node.bounds, false);
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
            hr = append(parent, rasters_.at(node.id).visual.Get());
        } else
            hr = append(parent, groups_.at(node.id).root.Get());
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
                it->kind == WidgetCompositionKind::Selection || it->kind == WidgetCompositionKind::Scrim)
                continue;
            const auto group = groups_.find(it->id);
            if (group == groups_.end() || group->second.closing)
                continue;
            const auto pose = Sample(group->second.motion, now);
            if (it->kind == WidgetCompositionKind::Modal && Contains(it->clip, point) &&
                (pose.opacity <= .01F || !Contains(pose.bounds, point)))
                return {std::numeric_limits<float>::quiet_NaN(), std::numeric_limits<float>::quiet_NaN()};
            if (it->kind == WidgetCompositionKind::Content && Contains(it->clip, point))
                movingContent = true;
            if (pose.opacity > .01F && Contains(it->clip, point) && Contains(pose.bounds, point)) {
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
        point.x -= pose.bounds.x - match->node.bounds.x;
        point.y -= pose.bounds.y - match->node.bounds.y;
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
