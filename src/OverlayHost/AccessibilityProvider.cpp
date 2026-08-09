#include "AccessibilityProvider.h"
#include "AccessibilityEvents.h"

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
#include <type_traits>
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
    case Role::ListItem: return UIA_ListItemControlTypeId;
    }
    return UIA_CustomControlTypeId;
}

bool KeyboardFocusable(const Node& node) noexcept {
    return node.role == Role::Button || node.role == Role::Slider ||
        node.role == Role::ListItem;
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
    std::shared_ptr<const PublishedTree> announced;
    std::vector<ActionRequest> pending;
    HWND window{};
    UINT actionMessage{};
    bool eventMessagePending{};
    bool windowFocused{};
    bool windowVisible{};
    std::uint64_t bindingGeneration{};

    [[nodiscard]] std::shared_ptr<const PublishedTree> Snapshot(
        const std::uint64_t expectedBinding) const noexcept {
        std::scoped_lock lock(mutex);
        return window && bindingGeneration == expectedBinding ? current : nullptr;
    }

    [[nodiscard]] bool IsAttached(const std::uint64_t expectedBinding) const noexcept {
        std::scoped_lock lock(mutex);
        return window && bindingGeneration == expectedBinding;
    }

    [[nodiscard]] bool WindowFocused(const std::uint64_t expectedBinding) const noexcept {
        std::scoped_lock lock(mutex);
        return window && bindingGeneration == expectedBinding && windowFocused;
    }

    [[nodiscard]] bool WindowVisible(const std::uint64_t expectedBinding) const noexcept {
        std::scoped_lock lock(mutex);
        return window && bindingGeneration == expectedBinding && windowVisible;
    }

    [[nodiscard]] HWND WindowHandle(const std::uint64_t expectedBinding) const noexcept {
        std::scoped_lock lock(mutex);
        return window && bindingGeneration == expectedBinding ? window : nullptr;
    }

    [[nodiscard]] HRESULT Enqueue(
        ActionRequest request, const std::uint64_t expectedBinding) noexcept {
        std::scoped_lock lock(mutex);
        if (!window || !actionMessage || bindingGeneration != expectedBinding)
            return UIA_E_ELEMENTNOTAVAILABLE;

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
    IRangeValueProvider,
    ISelectionItemProvider,
    ISelectionProvider> {
public:
    Provider(std::shared_ptr<ProviderState> state,
             std::optional<ElementIdentity> identity,
             const std::uint64_t bindingGeneration)
        : state_(std::move(state)), identity_(std::move(identity)),
          bindingGeneration_(bindingGeneration) {}

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
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
        const auto published = state_->Snapshot(bindingGeneration_);
        const auto* node = ResolveNode(published);
        if (identity_ && !node) return UIA_E_ELEMENTNOTAVAILABLE;
        if (!node) {
            if (patternId == UIA_SelectionPatternId && published &&
                std::any_of(
                    published->tree.nodes.begin(), published->tree.nodes.end(),
                    [](const Node& candidate) {
                        return candidate.role == Role::ListItem;
                    }))
                return QueryInterface(
                    __uuidof(ISelectionProvider), reinterpret_cast<void**>(result));
            return S_OK;
        }
        if (patternId == UIA_InvokePatternId &&
            (node->role == Role::Button || node->role == Role::ListItem) &&
            (!node->actionId.empty() || node->hostAction != HostAction::None)) {
            return QueryInterface(__uuidof(IInvokeProvider), reinterpret_cast<void**>(result));
        }
        if (patternId == UIA_RangeValuePatternId &&
            ((node->role == Role::Slider && !node->valueChangedActionId.empty()) ||
             (node->role == Role::Progress &&
              node->rangeMaximum > node->rangeMinimum))) {
            return QueryInterface(__uuidof(IRangeValueProvider), reinterpret_cast<void**>(result));
        }
        if (patternId == UIA_SelectionItemPatternId && node->role == Role::ListItem)
            return QueryInterface(
                __uuidof(ISelectionItemProvider), reinterpret_cast<void**>(result));
        return S_OK;
    }

    IFACEMETHODIMP GetPropertyValue(
        const PROPERTYID propertyId, VARIANT* result) noexcept override {
        if (!result) return E_INVALIDARG;
        VariantInit(result);
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
        const auto published = state_->Snapshot(bindingGeneration_);
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
                BoolVariant(state_->WindowFocused(bindingGeneration_), result); break;
            case UIA_IsKeyboardFocusablePropertyId: BoolVariant(false, result); break;
            case UIA_IsOffscreenPropertyId:
                BoolVariant(!state_->WindowVisible(bindingGeneration_), result); break;
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
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
        if (identity_) return S_OK;
        const HWND window = state_->WindowHandle(bindingGeneration_);
        return window ? UiaHostProviderFromHwnd(window, result) : UIA_E_ELEMENTNOTAVAILABLE;
    }

    IFACEMETHODIMP Navigate(
        const NavigateDirection direction,
        IRawElementProviderFragment** result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = nullptr;
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
        const auto published = state_->Snapshot(bindingGeneration_);
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
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
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
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
        const auto published = state_->Snapshot(bindingGeneration_);
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
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
        return S_OK;
    }

    IFACEMETHODIMP SetFocus() noexcept override {
        const auto published = state_->Snapshot(bindingGeneration_);
        const auto* node = ResolveNode(published);
        if (!node) return UIA_E_ELEMENTNOTAVAILABLE;
        if (!KeyboardFocusable(*node)) return UIA_E_NOTSUPPORTED;
        return state_->Enqueue(
            RequestFor(*published, *node, ActionKind::Focus), bindingGeneration_);
    }

    IFACEMETHODIMP get_FragmentRoot(
        IRawElementProviderFragmentRoot** result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = nullptr;
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
        ComPtr<Provider> provider = Make<Provider>(
            state_, std::nullopt, bindingGeneration_);
        return provider
            ? provider->QueryInterface(IID_PPV_ARGS(result))
            : E_OUTOFMEMORY;
    }

    IFACEMETHODIMP ElementProviderFromPoint(
        const double x, const double y,
        IRawElementProviderFragment** result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = nullptr;
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
        const auto published = state_->Snapshot(bindingGeneration_);
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
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
        const auto published = state_->Snapshot(bindingGeneration_);
        if (!published || !published->tree.focusedNode ||
            *published->tree.focusedNode >= published->tree.nodes.size()) return S_OK;
        return CreateFragment(
            IdentityFor(*published, published->tree.nodes[*published->tree.focusedNode]),
            result);
    }

    IFACEMETHODIMP Invoke() noexcept override {
        const auto published = state_->Snapshot(bindingGeneration_);
        const auto* node = ResolveNode(published);
        if (!node) return UIA_E_ELEMENTNOTAVAILABLE;
        if ((node->role != Role::Button && node->role != Role::ListItem) ||
            (node->actionId.empty() && node->hostAction == HostAction::None))
            return UIA_E_NOTSUPPORTED;
        if (!node->enabled) return UIA_E_ELEMENTNOTENABLED;
        return state_->Enqueue(
            RequestFor(*published, *node, ActionKind::Invoke), bindingGeneration_);
    }

    IFACEMETHODIMP SetValue(const double requested) noexcept override {
        const auto published = state_->Snapshot(bindingGeneration_);
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
        return state_->Enqueue(std::move(request), bindingGeneration_);
    }

    IFACEMETHODIMP get_Value(double* result) noexcept override {
        return RangeNumber(result, [](const Node& node) { return node.rangeValue; });
    }

    IFACEMETHODIMP get_IsReadOnly(BOOL* result) noexcept override {
        if (!result) return E_INVALIDARG;
        const auto published = state_->Snapshot(bindingGeneration_);
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

    IFACEMETHODIMP Select() noexcept override {
        const auto published = state_->Snapshot(bindingGeneration_);
        const auto* node = ResolveNode(published);
        if (!node) return UIA_E_ELEMENTNOTAVAILABLE;
        if (node->role != Role::ListItem) return UIA_E_NOTSUPPORTED;
        return state_->Enqueue(
            RequestFor(*published, *node, ActionKind::Focus), bindingGeneration_);
    }

    IFACEMETHODIMP AddToSelection() noexcept override {
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
        return UIA_E_INVALIDOPERATION;
    }

    IFACEMETHODIMP RemoveFromSelection() noexcept override {
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
        return UIA_E_INVALIDOPERATION;
    }

    IFACEMETHODIMP get_IsSelected(BOOL* result) noexcept override {
        if (!result) return E_INVALIDARG;
        const auto published = state_->Snapshot(bindingGeneration_);
        const auto* node = ResolveNode(published);
        if (!node) return UIA_E_ELEMENTNOTAVAILABLE;
        if (node->role != Role::ListItem) return UIA_E_NOTSUPPORTED;
        *result = node->selected ? TRUE : FALSE;
        return S_OK;
    }

    IFACEMETHODIMP get_SelectionContainer(
        IRawElementProviderSimple** result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = nullptr;
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
        if (!identity_) return UIA_E_NOTSUPPORTED;
        ComPtr<Provider> root = Make<Provider>(
            state_, std::nullopt, bindingGeneration_);
        return root ? root->QueryInterface(IID_PPV_ARGS(result)) : E_OUTOFMEMORY;
    }

    IFACEMETHODIMP GetSelection(SAFEARRAY** result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = nullptr;
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
        if (identity_) return UIA_E_NOTSUPPORTED;
        const auto published = state_->Snapshot(bindingGeneration_);
        if (!published) return S_OK;
        const auto selected = std::find_if(
            published->tree.nodes.begin(), published->tree.nodes.end(),
            [](const Node& node) {
                return node.role == Role::ListItem && node.selected;
            });
        if (selected == published->tree.nodes.end()) {
            *result = SafeArrayCreateVector(VT_UNKNOWN, 0, 0);
            return *result ? S_OK : E_OUTOFMEMORY;
        }
        SAFEARRAY* array = SafeArrayCreateVector(VT_UNKNOWN, 0, 1);
        if (!array) return E_OUTOFMEMORY;
        ComPtr<IRawElementProviderFragment> fragment;
        HRESULT status = CreateFragment(
            IdentityFor(*published, *selected), fragment.GetAddressOf());
        ComPtr<IUnknown> unknown;
        if (SUCCEEDED(status)) status = fragment.As(&unknown);
        LONG index{};
        IUnknown* selectedProvider = unknown.Get();
        if (SUCCEEDED(status))
            status = SafeArrayPutElement(array, &index, selectedProvider);
        if (FAILED(status)) {
            SafeArrayDestroy(array);
            return status;
        }
        *result = array;
        return S_OK;
    }

    IFACEMETHODIMP get_CanSelectMultiple(BOOL* result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = FALSE;
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
        return identity_ ? UIA_E_NOTSUPPORTED : S_OK;
    }

    IFACEMETHODIMP get_IsSelectionRequired(BOOL* result) noexcept override {
        if (!result) return E_INVALIDARG;
        *result = TRUE;
        if (!Available()) return UIA_E_ELEMENTNOTAVAILABLE;
        return identity_ ? UIA_E_NOTSUPPORTED : S_OK;
    }

private:
    [[nodiscard]] bool Available() const noexcept {
        return state_->IsAttached(bindingGeneration_);
    }

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
            node.hostAction,
            node.hostTargetId,
        };
    }

    HRESULT CreateFragment(
        std::optional<ElementIdentity> identity,
        IRawElementProviderFragment** result) const noexcept {
        ComPtr<Provider> provider = Make<Provider>(
            state_, std::move(identity), bindingGeneration_);
        return provider
            ? provider->QueryInterface(IID_PPV_ARGS(result))
            : E_OUTOFMEMORY;
    }

    template<typename Getter>
    HRESULT RangeNumber(double* result, Getter getter) const noexcept {
        if (!result) return E_INVALIDARG;
        const auto published = state_->Snapshot(bindingGeneration_);
        const auto* node = ResolveNode(published);
        if (!node) return UIA_E_ELEMENTNOTAVAILABLE;
        if (node->role != Role::Slider && node->role != Role::Progress)
            return UIA_E_NOTSUPPORTED;
        *result = getter(*node);
        return S_OK;
    }

    std::shared_ptr<ProviderState> state_;
    std::optional<ElementIdentity> identity_;
    std::uint64_t bindingGeneration_{};
};

} // namespace

ProviderHost::ProviderHost() : state_(std::make_shared<ProviderState>()) {}
ProviderHost::~ProviderHost() { Detach(); }

void ProviderHost::Bind(const HWND window, const UINT actionMessage) {
    Detach();
    std::scoped_lock lock(state_->mutex);
    ++state_->bindingGeneration;
    state_->window = window;
    state_->actionMessage = actionMessage;
    state_->windowFocused = window && GetFocus() == window;
    state_->windowVisible = window && IsWindowVisible(window);
}

void ProviderHost::Detach() noexcept {
    ComPtr<IRawElementProviderSimple> root;
    std::uint64_t binding{};
    {
        std::scoped_lock lock(state_->mutex);
        if (!state_->window) return;
        binding = state_->bindingGeneration;
    }
    if (SUCCEEDED(GetRootProvider(root.GetAddressOf())) && root)
        (void)UiaDisconnectProvider(root.Get());
    std::scoped_lock lock(state_->mutex);
    if (state_->bindingGeneration != binding) return;
    state_->window = nullptr;
    state_->actionMessage = 0;
    state_->windowFocused = false;
    state_->windowVisible = false;
    state_->eventMessagePending = false;
    state_->current.reset();
    state_->announced.reset();
    state_->pending.clear();
    ++state_->bindingGeneration;
}

void ProviderHost::SetWindowFocused(const bool focused) noexcept {
    std::scoped_lock lock(state_->mutex);
    if (state_->window) state_->windowFocused = focused;
}

void ProviderHost::SetWindowVisible(const bool visible) noexcept {
    std::scoped_lock lock(state_->mutex);
    if (state_->window) state_->windowVisible = visible;
}

void ProviderHost::Publish(Tree tree, const ScreenTransform transform) {
    auto published = std::make_shared<PublishedTree>(PublishedTree{
        std::move(tree), transform,
    });
    std::scoped_lock lock(state_->mutex);
    if (!state_->window) return;
    state_->current = std::move(published);
    if (!state_->eventMessagePending && state_->window && state_->actionMessage &&
        PostMessageW(state_->window, state_->actionMessage, 0, 0))
        state_->eventMessagePending = true;
}

void ProviderHost::Clear() noexcept {
    std::scoped_lock lock(state_->mutex);
    state_->current.reset();
    state_->pending.clear();
    if (!state_->eventMessagePending && state_->announced && state_->window &&
        state_->actionMessage && PostMessageW(state_->window, state_->actionMessage, 0, 0))
        state_->eventMessagePending = true;
}

LRESULT ProviderHost::HandleWmGetObject(const WPARAM wParam, const LPARAM lParam) {
    ComPtr<IRawElementProviderSimple> provider;
    if (FAILED(GetRootProvider(provider.GetAddressOf()))) return 0;
    HWND window{};
    {
        std::scoped_lock lock(state_->mutex);
        window = state_->window;
    }
    return window
        ? UiaReturnRawElementProvider(window, wParam, lParam, provider.Get())
        : 0;
}

HRESULT ProviderHost::GetRootProvider(IRawElementProviderSimple** provider) const {
    if (!provider) return E_INVALIDARG;
    *provider = nullptr;
    std::uint64_t binding{};
    {
        std::scoped_lock lock(state_->mutex);
        if (!state_->window) return UIA_E_ELEMENTNOTAVAILABLE;
        binding = state_->bindingGeneration;
    }
    ComPtr<Provider> root = Make<Provider>(state_, std::nullopt, binding);
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

void ProviderHost::RaisePendingEvents() noexcept {
    std::shared_ptr<const PublishedTree> previous;
    std::shared_ptr<const PublishedTree> current;
    std::uint64_t binding{};
    {
        std::scoped_lock lock(state_->mutex);
        if (!state_->eventMessagePending || !state_->window) return;
        state_->eventMessagePending = false;
        previous = state_->announced;
        current = state_->current;
        state_->announced = current;
        binding = state_->bindingGeneration;
    }
    auto plan = PlanEvents(
        previous ? &previous->tree : nullptr,
        current ? &current->tree : nullptr);
    const bool transformChanged = previous && current &&
        (previous->transform.originX != current->transform.originX ||
         previous->transform.originY != current->transform.originY ||
         previous->transform.pixelsPerDip != current->transform.pixelsPerDip ||
         previous->transform.width != current->transform.width ||
         previous->transform.height != current->transform.height);
    const bool sameRuntime = previous && current &&
        previous->tree.widgetId == current->tree.widgetId &&
        previous->tree.runtimeGeneration == current->tree.runtimeGeneration;
    if (transformChanged) {
        plan.properties.push_back({
            L"", PropertyKind::Bounds,
            PropertyValue{declarative::Rect{
                0, 0,
                static_cast<float>(previous->transform.width /
                                   previous->transform.pixelsPerDip),
                static_cast<float>(previous->transform.height /
                                   previous->transform.pixelsPerDip),
            }},
            PropertyValue{declarative::Rect{
                0, 0,
                static_cast<float>(current->transform.width /
                                   current->transform.pixelsPerDip),
                static_cast<float>(current->transform.height /
                                   current->transform.pixelsPerDip),
            }},
        });
        if (sameRuntime) {
            for (const auto& node : current->tree.nodes) {
                const auto before = std::find_if(
                    previous->tree.nodes.begin(), previous->tree.nodes.end(),
                    [&](const Node& candidate) { return candidate.id == node.id; });
                const bool alreadyPlanned = std::any_of(
                    plan.properties.begin(), plan.properties.end(),
                    [&](const PropertyChange& change) {
                        return change.nodeId == node.id &&
                            change.kind == PropertyKind::Bounds;
                    });
                if (before != previous->tree.nodes.end() && !alreadyPlanned)
                    plan.properties.push_back({
                        node.id, PropertyKind::Bounds, before->bounds, node.bounds,
                    });
            }
        }
    }
    ComPtr<IRawElementProviderSimple> root;
    if (FAILED(GetRootProvider(root.GetAddressOf())) || !root) return;
    if (plan.structureChanged)
        (void)UiaRaiseStructureChangedEvent(
            root.Get(), StructureChangeType_ChildrenInvalidated, nullptr, 0);

    const auto providerFor = [&](const std::wstring_view nodeId) {
        ComPtr<IRawElementProviderSimple> result;
        if (!current) return result;
        ComPtr<Provider> provider = Make<Provider>(
            state_, ElementIdentity{
                current->tree.widgetId,
                current->tree.runtimeGeneration,
                std::wstring{nodeId},
            }, binding);
        if (provider) (void)provider.As(&result);
        return result;
    };
    if (plan.focusChanged && plan.focusedNodeId) {
        auto focused = providerFor(*plan.focusedNodeId);
        if (focused)
            (void)UiaRaiseAutomationEvent(
                focused.Get(), UIA_AutomationFocusChangedEventId);
    }

    const auto propertyId = [](const PropertyKind kind) -> PROPERTYID {
        switch (kind) {
        case PropertyKind::Name: return UIA_NamePropertyId;
        case PropertyKind::HelpText: return UIA_HelpTextPropertyId;
        case PropertyKind::Enabled: return UIA_IsEnabledPropertyId;
        case PropertyKind::Selected: return UIA_SelectionItemIsSelectedPropertyId;
        case PropertyKind::RangeValue: return UIA_RangeValueValuePropertyId;
        case PropertyKind::RangeMinimum: return UIA_RangeValueMinimumPropertyId;
        case PropertyKind::RangeMaximum: return UIA_RangeValueMaximumPropertyId;
        case PropertyKind::RangeSmallChange: return UIA_RangeValueSmallChangePropertyId;
        case PropertyKind::RangeLargeChange: return UIA_RangeValueLargeChangePropertyId;
        case PropertyKind::RangeReadOnly: return UIA_RangeValueIsReadOnlyPropertyId;
        case PropertyKind::Bounds: return UIA_BoundingRectanglePropertyId;
        }
        return 0;
    };
    const auto variant = [](const PropertyValue& value, const ScreenTransform* transform) {
        VARIANT result{};
        std::visit([&](const auto& item) {
            using Value = std::decay_t<decltype(item)>;
            if constexpr (std::is_same_v<Value, std::wstring>) {
                V_VT(&result) = VT_BSTR;
                V_BSTR(&result) = SysAllocStringLen(
                    item.data(), static_cast<UINT>(item.size()));
            } else if constexpr (std::is_same_v<Value, bool>) {
                V_VT(&result) = VT_BOOL;
                V_BOOL(&result) = item ? VARIANT_TRUE : VARIANT_FALSE;
            } else if constexpr (std::is_same_v<Value, double>) {
                V_VT(&result) = VT_R8;
                V_R8(&result) = item;
            } else {
                SAFEARRAY* array = SafeArrayCreateVector(VT_R8, 0, 4);
                if (!array) return;
                const double scale = transform ? transform->pixelsPerDip : 1.0;
                const double parts[]{
                    (transform ? transform->originX : 0.0) + item.x * scale,
                    (transform ? transform->originY : 0.0) + item.y * scale,
                    item.width * scale,
                    item.height * scale,
                };
                for (LONG index = 0; index < 4; ++index) {
                    double part = parts[index];
                    (void)SafeArrayPutElement(array, &index, &part);
                }
                V_VT(&result) = VT_ARRAY | VT_R8;
                V_ARRAY(&result) = array;
            }
        }, value);
        return result;
    };
    for (const auto& change : plan.properties) {
        auto provider = change.nodeId.empty() ? root : providerFor(change.nodeId);
        if (!provider) continue;
        VARIANT oldValue = variant(
            change.oldValue, previous ? &previous->transform : nullptr);
        VARIANT newValue = variant(
            change.newValue, current ? &current->transform : nullptr);
        (void)UiaRaiseAutomationPropertyChangedEvent(
            provider.Get(), propertyId(change.kind), oldValue, newValue);
        VariantClear(&oldValue);
        VariantClear(&newValue);
    }
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
