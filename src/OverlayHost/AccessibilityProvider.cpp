#include "AccessibilityProvider.h"

#include <UIAutomation.h>
#include <wrl.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <limits>
#include <mutex>
#include <iterator>
#include <string_view>
#include <utility>

namespace gba::accessibility {
namespace {

using Microsoft::WRL::ClassicCom;
using Microsoft::WRL::ComPtr;
using Microsoft::WRL::Make;
using Microsoft::WRL::RuntimeClass;
using Microsoft::WRL::RuntimeClassFlags;

constexpr std::size_t kMaximumPendingActions = 64;

struct PublishedTree final {
    Tree tree;
    ScreenTransform transform;
};

struct ElementIdentity final {
    std::wstring widgetId;
    std::wstring runtimeGeneration;
    std::wstring nodeId;
};

bool SameAuthority(const ActionRequest& left, const ActionRequest& right) noexcept {
    return left.kind == right.kind && left.widgetId == right.widgetId &&
        left.runtimeGeneration == right.runtimeGeneration &&
        left.nodeId == right.nodeId;
}

std::uint32_t Hash(const std::wstring_view value, std::uint32_t seed) noexcept {
    for (const wchar_t codeUnit : value) {
        seed ^= static_cast<std::uint16_t>(codeUnit);
        seed *= 16777619U;
    }
    return seed;
}

HRESULT StringVariant(const std::wstring_view value, VARIANT* result) noexcept {
    if (!result) return E_INVALIDARG;
    VariantInit(result);
    V_VT(result) = VT_BSTR;
    V_BSTR(result) = SysAllocStringLen(value.data(), static_cast<UINT>(value.size()));
    return V_BSTR(result) || value.empty() ? S_OK : E_OUTOFMEMORY;
}

void BoolVariant(const bool value, VARIANT* result) noexcept {
    VariantInit(result);
    V_VT(result) = VT_BOOL;
    V_BOOL(result) = value ? VARIANT_TRUE : VARIANT_FALSE;
}

void IntVariant(const int value, VARIANT* result) noexcept {
    VariantInit(result);
    V_VT(result) = VT_I4;
    V_I4(result) = value;
}

int ControlType(const Role role) noexcept {
    switch (role) {
    case Role::Button: return UIA_ButtonControlTypeId;
    case Role::Slider: return UIA_SliderControlTypeId;
    case Role::Text: return UIA_TextControlTypeId;
    case Role::Image: return UIA_ImageControlTypeId;
    case Role::Progress: return UIA_ProgressBarControlTypeId;
    }
    return UIA_CustomControlTypeId;
}

bool KeyboardFocusable(const Node& node) noexcept {
    return node.role == Role::Button || node.role == Role::Slider;
}

const WidgetNode* FindNode(
    const WidgetNode& root, const std::wstring_view id,
    const std::wstring_view inheritedScope,
    const std::wstring_view activeScope) noexcept {
    const std::wstring_view scope = !root.inputScopeId.empty()
        ? std::wstring_view{root.inputScopeId}
        : inheritedScope.empty() ? std::wstring_view{root.id} : inheritedScope;
    if (scope == activeScope && root.id == id) return &root;
    for (const auto& child : root.children) {
        if (const auto* found = FindNode(child, id, scope, activeScope)) return found;
    }
    return nullptr;
}

UiaRect ScreenBounds(const Node& node, const ScreenTransform& transform) noexcept {
    return {
        transform.originX + node.bounds.x * transform.pixelsPerDip,
        transform.originY + node.bounds.y * transform.pixelsPerDip,
        node.bounds.width * transform.pixelsPerDip,
        node.bounds.height * transform.pixelsPerDip,
    };
}

bool Contains(const UiaRect& bounds, const double x, const double y) noexcept {
    return x >= bounds.left && y >= bounds.top &&
        x <= bounds.left + bounds.width && y <= bounds.top + bounds.height;
}

} // namespace

struct ProviderState final {
    mutable std::mutex mutex;
    std::shared_ptr<const PublishedTree> current;
    std::vector<ActionRequest> pending;
    HWND window{};
    UINT actionMessage{};

    [[nodiscard]] std::shared_ptr<const PublishedTree> Snapshot() const noexcept {
        std::scoped_lock lock(mutex);
        return current;
    }

    [[nodiscard]] HRESULT Enqueue(ActionRequest request) noexcept {
        std::scoped_lock lock(mutex);
        if (!window || !actionMessage) return UIA_E_ELEMENTNOTAVAILABLE;

        // RangeValue is latest-wins per element. Focus also has only one useful
        // pending destination. Invoke remains lossless until the hard bound.
        if (request.kind != ActionKind::Invoke) {
            const auto existing = std::find_if(
                pending.rbegin(), pending.rend(),
                [&](const ActionRequest& candidate) {
                    return request.kind == ActionKind::Focus
                        ? candidate.kind == ActionKind::Focus
                        : SameAuthority(candidate, request);
                });
            if (existing != pending.rend()) {
                *existing = std::move(request);
                return S_OK;
            }
        }
        if (pending.size() >= kMaximumPendingActions)
            return UIA_E_INVALIDOPERATION;
        const bool notify = pending.empty();
        pending.push_back(std::move(request));
        if (!notify || PostMessageW(window, actionMessage, 0, 0)) return S_OK;
        pending.pop_back();
        const DWORD error = GetLastError();
        return error ? HRESULT_FROM_WIN32(error) : E_FAIL;
    }
};

namespace {

class Provider final : public RuntimeClass<
    RuntimeClassFlags<ClassicCom>,
    IRawElementProviderSimple,
    IRawElementProviderFragment,
    IRawElementProviderFragmentRoot,
    IInvokeProvider,
    IRangeValueProvider> {
public:
    Provider(std::shared_ptr<ProviderState> state,
             std::optional<ElementIdentity> identity)
        : state_(std::move(state)), identity_(std::move(identity)) {}

    IFACEMETHODIMP get_ProviderOptions(ProviderOptions* result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = static_cast<ProviderOptions>(
            ProviderOptions_ServerSideProvider | ProviderOptions_UseComThreading);
        return S_OK;
    }

    IFACEMETHODIMP GetPatternProvider(
        const PATTERNID patternId, IUnknown** result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = nullptr;
        const auto published = state_->Snapshot();
        const auto* node = ResolveNode(published);
        if (identity_ && !node) return UIA_E_ELEMENTNOTAVAILABLE;
        if (!node) return S_OK;
        if (patternId == UIA_InvokePatternId && node->role == Role::Button &&
            !node->actionId.empty()) {
            return QueryInterface(__uuidof(IInvokeProvider), reinterpret_cast<void**>(result));
        }
        if (patternId == UIA_RangeValuePatternId &&
            ((node->role == Role::Slider && !node->valueChangedActionId.empty()) ||
             (node->role == Role::Progress &&
              node->rangeMaximum > node->rangeMinimum))) {
            return QueryInterface(__uuidof(IRangeValueProvider), reinterpret_cast<void**>(result));
        }
        return S_OK;
    }

    IFACEMETHODIMP GetPropertyValue(
        const PROPERTYID propertyId, VARIANT* result) noexcept override {
        if (!result) return E_INVALIDARG;
        VariantInit(result);
        const auto published = state_->Snapshot();
        const auto* node = ResolveNode(published);
        if (identity_ && !node) return UIA_E_ELEMENTNOTAVAILABLE;
        if (!node) {
            switch (propertyId) {
            case UIA_ControlTypePropertyId: IntVariant(UIA_PaneControlTypeId, result); break;
            case UIA_NamePropertyId: return StringVariant(L"Game Bar Alternative", result);
            case UIA_AutomationIdPropertyId:
                return StringVariant(L"GameBarAlternative.Overlay", result);
            case UIA_IsControlElementPropertyId:
            case UIA_IsContentElementPropertyId:
            case UIA_IsEnabledPropertyId: BoolVariant(true, result); break;
            case UIA_HasKeyboardFocusPropertyId:
                BoolVariant(state_->window && ::GetFocus() == state_->window, result); break;
            case UIA_IsKeyboardFocusablePropertyId: BoolVariant(false, result); break;
            case UIA_IsOffscreenPropertyId:
                BoolVariant(!state_->window || !IsWindowVisible(state_->window), result); break;
            default: break;
            }
            return S_OK;
        }
        switch (propertyId) {
        case UIA_ControlTypePropertyId: IntVariant(ControlType(node->role), result); break;
        case UIA_NamePropertyId: return StringVariant(node->name, result);
        case UIA_AutomationIdPropertyId: return StringVariant(node->id, result);
        case UIA_HelpTextPropertyId:
            if (!node->value.empty()) return StringVariant(node->value, result);
            break;
        case UIA_IsControlElementPropertyId:
        case UIA_IsContentElementPropertyId: BoolVariant(true, result); break;
        case UIA_IsEnabledPropertyId: BoolVariant(node->enabled, result); break;
        case UIA_HasKeyboardFocusPropertyId: BoolVariant(node->focused, result); break;
        case UIA_IsKeyboardFocusablePropertyId:
            BoolVariant(KeyboardFocusable(*node), result); break;
        case UIA_IsOffscreenPropertyId: BoolVariant(false, result); break;
        case UIA_SelectionItemIsSelectedPropertyId:
            BoolVariant(node->selected, result); break;
        default: break;
        }
        return S_OK;
    }

    IFACEMETHODIMP get_HostRawElementProvider(
        IRawElementProviderSimple** result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = nullptr;
        if (identity_ || !state_->window) return S_OK;
        return UiaHostProviderFromHwnd(state_->window, result);
    }

    IFACEMETHODIMP Navigate(
        const NavigateDirection direction,
        IRawElementProviderFragment** result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = nullptr;
        const auto published = state_->Snapshot();
        if (!published) return identity_ ? UIA_E_ELEMENTNOTAVAILABLE : S_OK;

        std::optional<ElementIdentity> target;
        if (!identity_) {
            if (direction == NavigateDirection_FirstChild ||
                direction == NavigateDirection_LastChild) {
                const auto& nodes = published->tree.nodes;
                if (direction == NavigateDirection_FirstChild) {
                    const auto found = std::find_if(
                        nodes.begin(), nodes.end(),
                        [](const Node& node) { return !node.parent; });
                    if (found != nodes.end()) target = IdentityFor(*published, *found);
                } else {
                    const auto found = std::find_if(
                        nodes.rbegin(), nodes.rend(),
                        [](const Node& node) { return !node.parent; });
                    if (found != nodes.rend()) target = IdentityFor(*published, *found);
                }
            }
            return target ? CreateFragment(*target, result) : S_OK;
        }

        const auto index = ResolveIndex(*published);
        if (!index) return UIA_E_ELEMENTNOTAVAILABLE;
        const auto& node = published->tree.nodes[*index];
        if (direction == NavigateDirection_Parent) {
            if (node.parent) target = IdentityFor(*published, published->tree.nodes[*node.parent]);
            else return CreateFragment(std::nullopt, result);
        } else if (direction == NavigateDirection_FirstChild && !node.children.empty()) {
            target = IdentityFor(*published, published->tree.nodes[node.children.front()]);
        } else if (direction == NavigateDirection_LastChild && !node.children.empty()) {
            target = IdentityFor(*published, published->tree.nodes[node.children.back()]);
        } else if (direction == NavigateDirection_NextSibling ||
                   direction == NavigateDirection_PreviousSibling) {
            std::vector<std::size_t> siblings;
            if (node.parent) siblings = published->tree.nodes[*node.parent].children;
            else {
                for (std::size_t candidate = 0; candidate < published->tree.nodes.size(); ++candidate)
                    if (!published->tree.nodes[candidate].parent) siblings.push_back(candidate);
            }
            const auto here = std::find(siblings.begin(), siblings.end(), *index);
            if (here != siblings.end()) {
                if (direction == NavigateDirection_NextSibling && std::next(here) != siblings.end())
                    target = IdentityFor(*published, published->tree.nodes[*std::next(here)]);
                if (direction == NavigateDirection_PreviousSibling && here != siblings.begin())
                    target = IdentityFor(*published, published->tree.nodes[*std::prev(here)]);
            }
        }
        return target ? CreateFragment(*target, result) : S_OK;
    }

    IFACEMETHODIMP GetRuntimeId(SAFEARRAY** result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = nullptr;
        if (!identity_) return S_OK;
        const LONG values[] = {
            UiaAppendRuntimeId,
            static_cast<LONG>(Hash(identity_->widgetId, 2166136261U) & 0x7fffffffU),
            static_cast<LONG>(Hash(identity_->runtimeGeneration, 16777619U) & 0x7fffffffU),
            static_cast<LONG>(Hash(identity_->nodeId, 2246822519U) & 0x7fffffffU),
            static_cast<LONG>(Hash(identity_->nodeId, 3266489917U) & 0x7fffffffU),
        };
        SAFEARRAY* array = SafeArrayCreateVector(VT_I4, 0, static_cast<ULONG>(std::size(values)));
        if (!array) return E_OUTOFMEMORY;
        for (LONG index = 0; index < static_cast<LONG>(std::size(values)); ++index) {
            LONG runtimePart = values[index];
            if (FAILED(SafeArrayPutElement(array, &index, &runtimePart))) {
                SafeArrayDestroy(array);
                return E_FAIL;
            }
        }
        *result = array;
        return S_OK;
    }

    IFACEMETHODIMP get_BoundingRectangle(UiaRect* result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = {};
        const auto published = state_->Snapshot();
        const auto* node = ResolveNode(published);
        if (identity_ && !node) return UIA_E_ELEMENTNOTAVAILABLE;
        if (node) *result = ScreenBounds(*node, published->transform);
        else if (published) {
            *result = {published->transform.originX, published->transform.originY,
                       published->transform.width, published->transform.height};
        }
        return S_OK;
    }

    IFACEMETHODIMP GetEmbeddedFragmentRoots(SAFEARRAY** result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = nullptr;
        return S_OK;
    }

    IFACEMETHODIMP SetFocus() noexcept override {
        const auto published = state_->Snapshot();
        const auto* node = ResolveNode(published);
        if (!node) return UIA_E_ELEMENTNOTAVAILABLE;
        if (!KeyboardFocusable(*node)) return UIA_E_NOTSUPPORTED;
        return state_->Enqueue(RequestFor(*published, *node, ActionKind::Focus));
    }

    IFACEMETHODIMP get_FragmentRoot(
        IRawElementProviderFragmentRoot** result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = nullptr;
        ComPtr<Provider> provider = Make<Provider>(state_, std::nullopt);
        return provider
            ? provider->QueryInterface(IID_PPV_ARGS(result))
            : E_OUTOFMEMORY;
    }

    IFACEMETHODIMP ElementProviderFromPoint(
        const double x, const double y,
        IRawElementProviderFragment** result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = nullptr;
        const auto published = state_->Snapshot();
        if (!published) return S_OK;
        const Node* best{};
        double bestArea = std::numeric_limits<double>::max();
        for (const auto& node : published->tree.nodes) {
            const auto bounds = ScreenBounds(node, published->transform);
            const double area = bounds.width * bounds.height;
            if (Contains(bounds, x, y) && area < bestArea) {
                best = &node;
                bestArea = area;
            }
        }
        if (best) return CreateFragment(IdentityFor(*published, *best), result);
        const UiaRect rootBounds{
            published->transform.originX,
            published->transform.originY,
            published->transform.width,
            published->transform.height,
        };
        return Contains(rootBounds, x, y)
            ? CreateFragment(std::nullopt, result)
            : S_OK;
    }

    IFACEMETHODIMP GetFocus(IRawElementProviderFragment** result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = nullptr;
        const auto published = state_->Snapshot();
        if (!published || !published->tree.focusedNode ||
            *published->tree.focusedNode >= published->tree.nodes.size()) return S_OK;
        return CreateFragment(
            IdentityFor(*published, published->tree.nodes[*published->tree.focusedNode]),
            result);
    }

    IFACEMETHODIMP Invoke() noexcept override {
        const auto published = state_->Snapshot();
        const auto* node = ResolveNode(published);
        if (!node) return UIA_E_ELEMENTNOTAVAILABLE;
        if (node->role != Role::Button || node->actionId.empty()) return UIA_E_NOTSUPPORTED;
        if (!node->enabled) return UIA_E_ELEMENTNOTENABLED;
        return state_->Enqueue(RequestFor(*published, *node, ActionKind::Invoke));
    }

    IFACEMETHODIMP SetValue(const double requested) noexcept override {
        const auto published = state_->Snapshot();
        const auto* node = ResolveNode(published);
        if (!node) return UIA_E_ELEMENTNOTAVAILABLE;
        if (node->role != Role::Slider || node->valueChangedActionId.empty())
            return UIA_E_NOTSUPPORTED;
        if (!node->enabled) return UIA_E_ELEMENTNOTENABLED;
        if (!std::isfinite(requested) || requested < node->rangeMinimum ||
            requested > node->rangeMaximum || node->rangeStep <= 0)
            return E_INVALIDARG;
        const double stepCount = std::round(
            (requested - node->rangeMinimum) / node->rangeStep);
        const double quantized = std::clamp(
            node->rangeMinimum + stepCount * node->rangeStep,
            node->rangeMinimum, node->rangeMaximum);
        auto request = RequestFor(*published, *node, ActionKind::SetValue);
        request.requestedValue = quantized;
        return state_->Enqueue(std::move(request));
    }

    IFACEMETHODIMP get_Value(double* result) noexcept override {
        return RangeNumber(result, [](const Node& node) { return node.rangeValue; });
    }

    IFACEMETHODIMP get_IsReadOnly(BOOL* result) noexcept override {
        if (!result) return E_INVALIDARG;
        const auto published = state_->Snapshot();
        const auto* node = ResolveNode(published);
        if (!node) return UIA_E_ELEMENTNOTAVAILABLE;
        *result = node->role == Role::Slider && node->enabled &&
            !node->valueChangedActionId.empty() ? FALSE : TRUE;
        return S_OK;
    }

    IFACEMETHODIMP get_Maximum(double* result) noexcept override {
        return RangeNumber(result, [](const Node& node) { return node.rangeMaximum; });
    }

    IFACEMETHODIMP get_Minimum(double* result) noexcept override {
        return RangeNumber(result, [](const Node& node) { return node.rangeMinimum; });
    }

    IFACEMETHODIMP get_LargeChange(double* result) noexcept override {
        return RangeNumber(result, [](const Node& node) {
            return std::min(node.rangeMaximum - node.rangeMinimum, node.rangeStep * 10.0);
        });
    }

    IFACEMETHODIMP get_SmallChange(double* result) noexcept override {
        return RangeNumber(result, [](const Node& node) { return node.rangeStep; });
    }

private:
    [[nodiscard]] const Node* ResolveNode(
        const std::shared_ptr<const PublishedTree>& published) const noexcept {
        if (!identity_ || !published ||
            published->tree.widgetId != identity_->widgetId ||
            published->tree.runtimeGeneration != identity_->runtimeGeneration) return nullptr;
        const auto found = std::find_if(
            published->tree.nodes.begin(), published->tree.nodes.end(),
            [&](const Node& node) { return node.id == identity_->nodeId; });
        return found == published->tree.nodes.end() ? nullptr : &*found;
    }

    [[nodiscard]] std::optional<std::size_t> ResolveIndex(
        const PublishedTree& published) const noexcept {
        if (!identity_ || published.tree.widgetId != identity_->widgetId ||
            published.tree.runtimeGeneration != identity_->runtimeGeneration) return std::nullopt;
        const auto found = std::find_if(
            published.tree.nodes.begin(), published.tree.nodes.end(),
            [&](const Node& node) { return node.id == identity_->nodeId; });
        return found == published.tree.nodes.end()
            ? std::nullopt
            : std::optional<std::size_t>{static_cast<std::size_t>(
                std::distance(published.tree.nodes.begin(), found))};
    }

    static ElementIdentity IdentityFor(
        const PublishedTree& published, const Node& node) {
        return {published.tree.widgetId, published.tree.runtimeGeneration, node.id};
    }

    ActionRequest RequestFor(
        const PublishedTree& published, const Node& node,
        const ActionKind kind) const {
        return {
            kind,
            published.tree.widgetId,
            published.tree.runtimeGeneration,
            published.tree.snapshotSequence,
            published.tree.activeInputScopeId,
            node.id,
            kind == ActionKind::SetValue ? node.valueChangedActionId : node.actionId,
        };
    }

    HRESULT CreateFragment(
        std::optional<ElementIdentity> identity,
        IRawElementProviderFragment** result) const noexcept {
        ComPtr<Provider> provider = Make<Provider>(state_, std::move(identity));
        return provider
            ? provider->QueryInterface(IID_PPV_ARGS(result))
            : E_OUTOFMEMORY;
    }

    template<typename Getter>
    HRESULT RangeNumber(double* result, Getter getter) const noexcept {
        if (!result) return E_INVALIDARG;
        const auto published = state_->Snapshot();
        const auto* node = ResolveNode(published);
        if (!node) return UIA_E_ELEMENTNOTAVAILABLE;
        if (node->role != Role::Slider && node->role != Role::Progress)
            return UIA_E_NOTSUPPORTED;
        *result = getter(*node);
        return S_OK;
    }

    std::shared_ptr<ProviderState> state_;
    std::optional<ElementIdentity> identity_;
};

} // namespace

ProviderHost::ProviderHost() : state_(std::make_shared<ProviderState>()) {}
ProviderHost::~ProviderHost() = default;

void ProviderHost::Bind(const HWND window, const UINT actionMessage) {
    std::scoped_lock lock(state_->mutex);
    state_->window = window;
    state_->actionMessage = actionMessage;
}

void ProviderHost::Publish(Tree tree, const ScreenTransform transform) {
    auto published = std::make_shared<PublishedTree>(PublishedTree{
        std::move(tree), transform,
    });
    std::scoped_lock lock(state_->mutex);
    state_->current = std::move(published);
}

void ProviderHost::Clear() noexcept {
    std::scoped_lock lock(state_->mutex);
    state_->current.reset();
    state_->pending.clear();
}

LRESULT ProviderHost::HandleWmGetObject(const WPARAM wParam, const LPARAM lParam) {
    ComPtr<IRawElementProviderSimple> provider;
    if (FAILED(GetRootProvider(provider.GetAddressOf()))) return 0;
    return UiaReturnRawElementProvider(state_->window, wParam, lParam, provider.Get());
}

HRESULT ProviderHost::GetRootProvider(IRawElementProviderSimple** provider) const {
    if (!provider) return E_INVALIDARG;
    *provider = nullptr;
    ComPtr<Provider> root = Make<Provider>(state_, std::nullopt);
    return root
        ? root->QueryInterface(IID_PPV_ARGS(provider))
        : E_OUTOFMEMORY;
}

std::vector<ActionRequest> ProviderHost::TakeActions() noexcept {
    std::vector<ActionRequest> actions;
    std::scoped_lock lock(state_->mutex);
    actions.swap(state_->pending);
    return actions;
}

std::optional<ResolvedAction> ResolveActionRequest(
    const ActionRequest& request,
    const std::wstring_view currentWidgetId,
    const std::wstring_view currentRuntimeGeneration,
    const WidgetSnapshot& currentSnapshot) noexcept {
    if (request.widgetId != currentWidgetId ||
        request.runtimeGeneration != currentRuntimeGeneration ||
        request.snapshotSequence != currentSnapshot.sequence ||
        request.activeInputScopeId != currentSnapshot.activeInputScopeId) return std::nullopt;
    const auto* node = FindNode(
        currentSnapshot.root, request.nodeId, std::wstring_view{},
        currentSnapshot.activeInputScopeId);
    if (!node || node->isDisabled || node->isBusy) return std::nullopt;

    ResolvedAction resolved{request.kind, request.nodeId};
    if (request.kind == ActionKind::Focus) {
        if (node->kind != L"button" && node->kind != L"slider" &&
            node->kind != L"actionSurface") return std::nullopt;
        return resolved;
    }
    if (request.kind == ActionKind::Invoke &&
        (node->kind == L"button" || node->kind == L"actionSurface") &&
        !request.actionId.empty() && request.actionId == node->actionId) {
        resolved.protocolButton = L"A";
        return resolved;
    }
    if (request.kind == ActionKind::SetValue && node->kind == L"slider" &&
        request.requestedValue && std::isfinite(*request.requestedValue) &&
        request.actionId == node->valueChangedActionId &&
        *request.requestedValue >= node->minimum &&
        *request.requestedValue <= node->maximum) {
        resolved.protocolButton = *request.requestedValue < node->value
            ? L"DPadLeft"
            : L"DPadRight";
        resolved.requestedValue = request.requestedValue;
        return resolved;
    }
    return std::nullopt;
}

} // namespace gba::accessibility
