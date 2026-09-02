#include "WidgetSurfaceCoordinator.h"

#include <Windows.h>
#include <UIAutomation.h>
#include <d2d1.h>
#include <dwrite.h>
#include <psapi.h>
#include <wrl/client.h>

#include <chrono>
#include <cstddef>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <optional>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <utility>
#include <vector>

namespace {

using Microsoft::WRL::ComPtr;
int checks{};

ComPtr<IUIAutomationElement> FindAutomationId(
    HWND window, const wchar_t* automationId);

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) throw std::runtime_error(std::string(message));
}

void CheckRenderSucceeded(
    const widgetrail::pinned::WidgetSurfacePaintTrace& trace,
    const std::string_view message) {
    ++checks;
    if (trace.declarativeRenderSucceeded) return;
    std::string failure{message};
    if (trace.currentFirstRenderDiagnostic) {
        failure += " [render-diagnostic=";
        for (const wchar_t value : trace.currentFirstRenderDiagnostic->code)
            failure.push_back(value >= 0 && value <= 0x7f
                ? static_cast<char>(value) : '?');
        failure += ']';
    }
    throw std::runtime_error(std::move(failure));
}

bool InvokeAutomationId(const HWND window, const wchar_t* automationId) {
    ComPtr<IUIAutomation> automation;
    if (FAILED(CoCreateInstance(
            CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(automation.ReleaseAndGetAddressOf())))) return false;
    ComPtr<IUIAutomationElement> root;
    if (FAILED(automation->ElementFromHandle(window, root.ReleaseAndGetAddressOf())) || !root)
        return false;
    VARIANT expected{};
    expected.vt = VT_BSTR;
    expected.bstrVal = SysAllocString(automationId);
    ComPtr<IUIAutomationCondition> condition;
    const HRESULT conditionResult = automation->CreatePropertyCondition(
        UIA_AutomationIdPropertyId, expected, condition.ReleaseAndGetAddressOf());
    VariantClear(&expected);
    if (FAILED(conditionResult) || !condition) return false;
    ComPtr<IUIAutomationElement> element;
    if (FAILED(root->FindFirst(
            TreeScope_Descendants, condition.Get(), element.ReleaseAndGetAddressOf())) ||
        !element) return false;
    ComPtr<IUIAutomationInvokePattern> invoke;
    if (FAILED(element->GetCurrentPatternAs(
            UIA_InvokePatternId, IID_PPV_ARGS(invoke.ReleaseAndGetAddressOf()))) || !invoke)
        return false;
    if (FAILED(invoke->Invoke())) return false;
    MSG message{};
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    return true;
}

bool ExpandAutomationId(const HWND window, const wchar_t* automationId) {
    const auto element = FindAutomationId(window, automationId);
    if (!element) return false;
    ComPtr<IUIAutomationExpandCollapsePattern> expand;
    if (FAILED(element->GetCurrentPatternAs(
            UIA_ExpandCollapsePatternId,
            IID_PPV_ARGS(expand.ReleaseAndGetAddressOf()))) || !expand)
        return false;
    if (FAILED(expand->Expand())) return false;
    MSG message{};
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    return true;
}

ComPtr<IUIAutomationElement> FindAutomationId(
    const HWND window, const wchar_t* automationId) {
    ComPtr<IUIAutomation> automation;
    if (FAILED(CoCreateInstance(
            CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
            IID_PPV_ARGS(automation.ReleaseAndGetAddressOf())))) return {};
    ComPtr<IUIAutomationElement> root;
    if (FAILED(automation->ElementFromHandle(window, root.ReleaseAndGetAddressOf())) || !root)
        return {};
    VARIANT expected{};
    expected.vt = VT_BSTR;
    expected.bstrVal = SysAllocString(automationId);
    ComPtr<IUIAutomationCondition> condition;
    const HRESULT conditionResult = automation->CreatePropertyCondition(
        UIA_AutomationIdPropertyId, expected, condition.ReleaseAndGetAddressOf());
    VariantClear(&expected);
    if (FAILED(conditionResult) || !condition) return {};
    ComPtr<IUIAutomationElement> element;
    if (FAILED(root->FindFirst(
            TreeScope_Descendants, condition.Get(), element.ReleaseAndGetAddressOf())))
        return {};
    return element;
}

bool ActionBoundsInsideWindow(const HWND window, const wchar_t* automationId) {
    const auto element = FindAutomationId(window, automationId);
    if (!element) return false;
    RECT bounds{};
    RECT windowBounds{};
    if (FAILED(element->get_CurrentBoundingRectangle(&bounds)) ||
        !GetWindowRect(window, &windowBounds)) return false;
    constexpr double tolerance = 0.51;
    return bounds.right - bounds.left > 0 && bounds.bottom - bounds.top > 0 &&
        bounds.left + tolerance >= windowBounds.left &&
        bounds.top + tolerance >= windowBounds.top &&
        bounds.right <= windowBounds.right + tolerance &&
        bounds.bottom <= windowBounds.bottom + tolerance;
}

bool ReadNamedButtonBounds(const HWND window, const wchar_t* automationId,
                           const wchar_t* expectedName, RECT& bounds) {
    const auto element = FindAutomationId(window, automationId);
    if (!element) return false;
    BSTR name{};
    CONTROLTYPEID controlType{};
    const bool valid = SUCCEEDED(element->get_CurrentName(&name)) && name &&
        std::wstring_view(name) == expectedName &&
        SUCCEEDED(element->get_CurrentControlType(&controlType)) &&
        controlType == UIA_ButtonControlTypeId &&
        SUCCEEDED(element->get_CurrentBoundingRectangle(&bounds));
    SysFreeString(name);
    return valid;
}

bool IsAssertiveLiveRegion(const HWND window, const wchar_t* automationId) {
    const auto element = FindAutomationId(window, automationId);
    if (!element) return false;
    VARIANT value{};
    const HRESULT result = element->GetCurrentPropertyValue(
        UIA_LiveSettingPropertyId, &value);
    const bool assertive = SUCCEEDED(result) && value.vt == VT_I4 &&
        value.lVal == Assertive;
    VariantClear(&value);
    return assertive;
}

bool AutomationValueContains(const HWND window, const wchar_t* automationId,
                             const std::wstring_view expected) {
    const auto element = FindAutomationId(window, automationId);
    if (!element) return false;
    VARIANT value{};
    const HRESULT result = element->GetCurrentPropertyValue(
        UIA_HelpTextPropertyId, &value);
    const bool contains = SUCCEEDED(result) && value.vt == VT_BSTR && value.bstrVal &&
        std::wstring_view(value.bstrVal).find(expected) != std::wstring_view::npos;
    VariantClear(&value);
    return contains;
}

bool HasRangeValueWithoutInvoke(
    const HWND window, const wchar_t* automationId,
    const double expectedValue, const double expectedMaximum) {
    const auto element = FindAutomationId(window, automationId);
    if (!element) return false;
    ComPtr<IUIAutomationRangeValuePattern> range;
    ComPtr<IUIAutomationInvokePattern> invoke;
    double value{};
    double maximum{};
    return SUCCEEDED(element->GetCurrentPatternAs(
               UIA_RangeValuePatternId,
               IID_PPV_ARGS(range.ReleaseAndGetAddressOf()))) && range &&
        SUCCEEDED(range->get_CurrentValue(&value)) &&
        SUCCEEDED(range->get_CurrentMaximum(&maximum)) &&
        value == expectedValue && maximum == expectedMaximum &&
        (FAILED(element->GetCurrentPatternAs(
             UIA_InvokePatternId,
             IID_PPV_ARGS(invoke.ReleaseAndGetAddressOf()))) || !invoke);
}

[[nodiscard]] std::size_t PrivateWorkingSetBytes() {
    std::vector<std::byte> buffer(256 * 1024);
    for (int attempt = 0; attempt < 8; ++attempt) {
        if (QueryWorkingSet(GetCurrentProcess(), buffer.data(),
                            static_cast<DWORD>(buffer.size())) != FALSE) {
            const auto* information =
                reinterpret_cast<const PSAPI_WORKING_SET_INFORMATION*>(buffer.data());
            std::size_t privatePages{};
            for (ULONG_PTR index = 0; index < information->NumberOfEntries; ++index)
                if (!information->WorkingSetInfo[index].Shared) ++privatePages;
            SYSTEM_INFO system{};
            GetNativeSystemInfo(&system);
            return privatePages * static_cast<std::size_t>(system.dwPageSize);
        }
        Check(GetLastError() == ERROR_BAD_LENGTH,
              "private working-set query fails only for a short buffer");
        buffer.resize(buffer.size() * 2);
    }
    throw std::runtime_error("private working-set query exceeded its bounded buffer");
}

widgetrail::WidgetSnapshot Snapshot(const long long sequence = 1) {
    widgetrail::WidgetSnapshot snapshot;
    snapshot.sequence = sequence;
    snapshot.instanceId = L"gallery.instance";
    snapshot.activeInputScopeId = L"root";
    snapshot.initialFocusId = L"pin.fixture.action";
    snapshot.root.id = L"root";
    snapshot.root.kind = L"stack";
    snapshot.root.inputScopeId = L"root";
    widgetrail::WidgetNode heading;
    heading.id = L"pin.fixture.heading";
    heading.kind = L"text";
    heading.text = L"Pinned declarative fixture";
    heading.accessibilityLabel = heading.text;
    heading.inputScopeId = L"root";
    widgetrail::WidgetNode action;
    action.id = L"pin.fixture.action";
    action.kind = L"button";
    action.text = L"Deterministic action";
    action.accessibilityLabel = action.text;
    action.actionId = L"fixture-action";
    action.inputScopeId = L"root";
    action.focusRight = L"pin.fixture.second";
    widgetrail::WidgetNode second;
    second.id = L"pin.fixture.second";
    second.kind = L"button";
    second.text = L"Second deterministic action";
    second.accessibilityLabel = second.text;
    second.actionId = L"fixture-second";
    second.inputScopeId = L"root";
    second.focusLeft = L"pin.fixture.action";
    snapshot.root.children = {
        std::move(heading), std::move(action), std::move(second)};
    return snapshot;
}

widgetrail::WidgetStyleValue Length(const double value) {
    return {L"length", std::to_wstring(value) + L"px", value, L"px"};
}

widgetrail::WidgetStyleValue Number(const double value) {
    return {L"number", std::to_wstring(value), value, {}};
}

widgetrail::WidgetSnapshot ScrollSnapshot(
    const long long sequence = 1,
    const bool paged = false,
    const int itemCount = 8) {
    auto snapshot = Snapshot(sequence);
    snapshot.initialFocusId = L"pin.scroll.item.0";
    snapshot.root.children.clear();

    widgetrail::WidgetNode scroll;
    scroll.id = L"pin.scroll";
    scroll.kind = L"scroll";
    scroll.inputScopeId = L"root";
    scroll.scrollAxis = L"vertical";
    if (paged) {
        scroll.scrollNearEndActionId = L"pin.scroll.next";
        scroll.scrollPaginationThreshold = 1;
    }
    scroll.baseStyle = {
        {L"height", Length(140.0)},
        {L"flex-shrink", Number(0.0)},
    };
    for (int index = 0; index < itemCount; ++index) {
        widgetrail::WidgetNode item;
        item.id = L"pin.scroll.item." + std::to_wstring(index);
        item.kind = L"button";
        item.text = L"Scrollable item " + std::to_wstring(index);
        item.accessibilityLabel = item.text;
        item.actionId = L"scroll-item";
        item.inputScopeId = L"root";
        item.focusRight = L"pin.outside";
        item.baseStyle = {
            {L"min-height", Length(44.0)},
            {L"flex-shrink", Number(0.0)},
        };
        scroll.children.push_back(std::move(item));
    }

    widgetrail::WidgetNode outside;
    outside.id = L"pin.outside";
    outside.kind = L"button";
    outside.text = L"Outside scroll";
    outside.accessibilityLabel = outside.text;
    outside.actionId = L"outside";
    outside.inputScopeId = L"root";
    snapshot.root.children = {std::move(scroll), std::move(outside)};
    return snapshot;
}

widgetrail::WidgetSnapshot FreeScrollSelectSnapshot(
    const long long sequence = 1) {
    auto snapshot = ScrollSnapshot(sequence);
    auto& select = snapshot.root.children[1];
    select.actionId.clear();
    select.isSelect = true;
    select.text = L"Density";
    select.accessibilityLabel = select.text;
    select.accessibilityValue = L"Compact";
    widgetrail::WidgetSelectOption compact;
    compact.id = L"compact";
    compact.label = L"Compact";
    compact.actionId = L"density.compact";
    compact.isSelected = true;
    widgetrail::WidgetSelectOption comfortable;
    comfortable.id = L"comfortable";
    comfortable.label = L"Comfortable";
    comfortable.actionId = L"density.comfortable";
    select.selectOptions = {std::move(compact), std::move(comfortable)};
    return snapshot;
}

widgetrail::WidgetSnapshot CompactMediaSnapshot(const long long sequence = 1) {
    auto snapshot = Snapshot(sequence);
    widgetrail::EmbeddedMediaSurfaceDeclaration media;
    media.id = L"fixture.media";
    media.accessibleName = L"Provider-neutral compact media";
    media.entryAsset = L"index.html";
    media.aspectRatio = 16.0 / 9.0;
    media.commands = {
        L"togglePlayback", L"navigatePrevious", L"navigateNext",
        L"seekBackward", L"seekForward"};
    media.compactPinnedPresentation = true;
    media.mediaSeekStepSeconds = 7.0;
    snapshot.embeddedMedia = std::move(media);
    return snapshot;
}

widgetrail::WidgetSnapshot SliderSnapshot(
    const long long sequence = 1,
    const double value = 40.0) {
    auto snapshot = Snapshot(sequence);
    snapshot.initialFocusId = L"pin.fixture.slider";
    snapshot.root.children.clear();
    widgetrail::WidgetNode slider;
    slider.id = L"pin.fixture.slider";
    slider.kind = L"slider";
    slider.accessibilityLabel = L"Provider-neutral pinned slider";
    slider.valueChangedActionId = L"fixture-slider.changed";
    slider.hasSliderRange = true;
    slider.minimum = 0.0;
    slider.maximum = 100.0;
    slider.value = value;
    slider.step = 5.0;
    slider.sliderInteractionMode = L"activateToAdjust";
    slider.inputScopeId = L"root";
    slider.focusRight = L"pin.fixture.after-slider";
    widgetrail::WidgetNode after;
    after.id = L"pin.fixture.after-slider";
    after.kind = L"button";
    after.accessibilityLabel = L"After slider";
    after.actionId = L"after-slider";
    after.inputScopeId = L"root";
    after.focusLeft = slider.id;
    snapshot.root.children = {std::move(slider), std::move(after)};
    return snapshot;
}

widgetrail::WidgetSnapshot SelectSnapshot(
    const long long sequence = 1,
    const bool disableAll = false,
    const bool changeComfortableAction = false) {
    auto snapshot = Snapshot(sequence);
    snapshot.initialFocusId = L"pin.fixture.select";
    snapshot.root.children.clear();
    widgetrail::WidgetNode select;
    select.id = L"pin.fixture.select";
    select.kind = L"button";
    select.isSelect = true;
    select.text = L"Density";
    select.accessibilityLabel = L"Density";
    select.accessibilityValue = L"Compact";
    select.inputScopeId = L"root";
    widgetrail::WidgetSelectOption compact;
    compact.id = L"compact";
    compact.label = L"Compact";
    compact.actionId = L"density.compact";
    compact.isSelected = true;
    compact.isDisabled = disableAll;
    widgetrail::WidgetSelectOption comfortable;
    comfortable.id = L"comfortable";
    comfortable.label = L"Comfortable";
    comfortable.actionId = changeComfortableAction
        ? L"density.comfortable.changed" : L"density.comfortable";
    comfortable.isDisabled = disableAll;
    widgetrail::WidgetSelectOption spacious;
    spacious.id = L"spacious";
    spacious.label = L"Spacious";
    spacious.actionId = L"density.spacious";
    spacious.isDisabled = disableAll;
    select.selectOptions = {
        std::move(compact), std::move(comfortable), std::move(spacious)};
    snapshot.root.children = {std::move(select)};
    return snapshot;
}

widgetrail::WidgetSnapshot SelectFocusTransitionSnapshot() {
    auto snapshot = Snapshot();
    snapshot.initialFocusId = L"pin.transition.outside";
    snapshot.root.children.clear();

    widgetrail::WidgetNode outside;
    outside.id = L"pin.transition.outside";
    outside.kind = L"button";
    outside.text = L"Outside group";
    outside.accessibilityLabel = outside.text;
    outside.actionId = L"outside";
    outside.inputScopeId = L"root";
    outside.focusDown = L"pin.transition.group";

    widgetrail::WidgetNode slider;
    slider.id = L"pin.transition.slider";
    slider.kind = L"slider";
    slider.accessibilityLabel = L"Transition slider";
    slider.valueChangedActionId = L"transition-slider.changed";
    slider.hasProgress = true;
    slider.hasSliderRange = true;
    slider.minimum = 0.0;
    slider.maximum = 100.0;
    slider.value = 40.0;
    slider.step = 5.0;
    slider.sliderInteractionMode = L"activateToAdjust";
    slider.inputScopeId = L"root";
    slider.focusRight = L"pin.transition.select";
    slider.focusUp = outside.id;

    widgetrail::WidgetNode select;
    select.id = L"pin.transition.select";
    select.kind = L"button";
    select.isSelect = true;
    select.text = L"Density";
    select.accessibilityLabel = select.text;
    select.accessibilityValue = L"Compact";
    select.inputScopeId = L"root";
    select.focusLeft = slider.id;
    select.focusUp = outside.id;
    widgetrail::WidgetSelectOption compact;
    compact.id = L"compact";
    compact.label = L"Compact";
    compact.actionId = L"density.compact";
    compact.isSelected = true;
    widgetrail::WidgetSelectOption comfortable;
    comfortable.id = L"comfortable";
    comfortable.label = L"Comfortable";
    comfortable.actionId = L"density.comfortable";
    select.selectOptions = {std::move(compact), std::move(comfortable)};

    widgetrail::WidgetNode group;
    group.id = L"pin.transition.group";
    group.kind = L"row";
    group.inputScopeId = L"root";
    group.initialChildFocusId = slider.id;
    group.children = {std::move(slider), std::move(select)};

    snapshot.root.children = {std::move(outside), std::move(group)};
    return snapshot;
}

widgetrail::WidgetSnapshot FocusGroupSnapshot(const long long sequence = 1) {
    auto snapshot = Snapshot(sequence);
    snapshot.initialFocusId = L"pin.group.entry";
    snapshot.root.children.clear();

    widgetrail::WidgetNode entry;
    entry.id = L"pin.group.entry";
    entry.kind = L"button";
    entry.text = L"Entry";
    entry.accessibilityLabel = entry.text;
    entry.actionId = L"entry";
    entry.inputScopeId = L"root";
    entry.focusDown = L"pin.group.controls";

    widgetrail::WidgetNode first;
    first.id = L"pin.group.first";
    first.kind = L"button";
    first.text = L"First";
    first.accessibilityLabel = first.text;
    first.actionId = L"first";
    first.inputScopeId = L"root";
    first.focusRight = L"pin.group.second";
    first.focusUp = entry.id;

    widgetrail::WidgetNode second;
    second.id = L"pin.group.second";
    second.kind = L"button";
    second.text = L"Second";
    second.accessibilityLabel = second.text;
    second.actionId = L"second";
    second.inputScopeId = L"root";
    second.focusLeft = first.id;
    second.focusUp = entry.id;

    widgetrail::WidgetNode controls;
    controls.id = L"pin.group.controls";
    controls.kind = L"row";
    controls.inputScopeId = L"root";
    controls.initialChildFocusId = first.id;
    controls.children = {std::move(first), std::move(second)};
    snapshot.root.children = {std::move(entry), std::move(controls)};
    return snapshot;
}

void PumpPendingMessages() {
    MSG message{};
    while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
}

// Mirrors the shape a real media widget publishes: one semantic tree carrying
// both responsive shells, an activation-first scrubber nested several levels
// deep, and a millisecond-scale range whose value advances on every publish.
widgetrail::WidgetSnapshot MediaTimelineSnapshot(
    const long long sequence,
    const double positionMilliseconds,
    const bool transportDisallowed = false,
    const bool seekPending = false) {
    widgetrail::WidgetSnapshot snapshot;
    snapshot.sequence = sequence;
    snapshot.instanceId = L"gallery.instance";
    snapshot.activeInputScopeId = L"media.window";
    snapshot.initialFocusId = L"media.play-toggle";
    snapshot.root.id = L"media.root";
    snapshot.root.kind = L"stack";
    snapshot.root.inputScopeId = L"media.window";

    const auto buildPlayer = [&](const std::wstring& prefix,
                                 const std::wstring& sliderId) {
        widgetrail::WidgetNode slider;
        slider.id = sliderId;
        slider.kind = L"slider";
        slider.accessibilityLabel = L"Playback position";
        slider.valueChangedActionId = L"media.seek";
        slider.focusPersistenceId = L"media.transport.seek";
        slider.hasSliderRange = true;
        slider.minimum = 0.0;
        slider.maximum = 213000.0;
        slider.value = positionMilliseconds;
        slider.step = 5000.0;
        slider.sliderInteractionMode = L"activateToAdjust";
        slider.inputScopeId = L"media.window";
        slider.isBusy = seekPending;
        slider.focusDown = prefix + L".play-toggle";

        widgetrail::WidgetNode elapsed;
        elapsed.id = sliderId + L".elapsed";
        elapsed.kind = L"text";
        elapsed.text = L"1:23";
        elapsed.accessibilityLabel = elapsed.text;
        elapsed.inputScopeId = L"media.window";

        widgetrail::WidgetNode scrubber;
        scrubber.id = prefix + L".seek";
        scrubber.kind = L"stack";
        scrubber.inputScopeId = L"media.window";
        scrubber.children = {std::move(slider), std::move(elapsed)};

        widgetrail::WidgetNode toggle;
        toggle.id = prefix + L".play-toggle";
        toggle.kind = L"button";
        toggle.text = L"Pause";
        toggle.accessibilityLabel = toggle.text;
        toggle.actionId = L"media.play-toggle";
        toggle.inputScopeId = L"media.window";
        toggle.isDisabled = transportDisallowed;
        toggle.focusUp = sliderId;

        widgetrail::WidgetNode controls;
        controls.id = prefix + L".controls";
        controls.kind = L"row";
        controls.inputScopeId = L"media.window";
        controls.children = {std::move(toggle)};

        widgetrail::WidgetNode card;
        card.id = prefix + L".card";
        card.kind = L"stack";
        card.inputScopeId = L"media.window";
        card.children = {std::move(scrubber), std::move(controls)};
        return card;
    };

    widgetrail::WidgetNode wide;
    wide.id = L"media.shell.wide";
    wide.kind = L"row";
    wide.inputScopeId = L"media.window";
    wide.visibleWhen = L"expandedOnly";
    wide.children = {buildPlayer(L"media", L"media.seek.slider")};

    widgetrail::WidgetNode compact;
    compact.id = L"media.shell.compact";
    compact.kind = L"row";
    compact.inputScopeId = L"media.window";
    compact.visibleWhen = L"compactOnly";
    compact.children = {
        buildPlayer(L"media.player.compact", L"media.player.compact.seek.slider")};

    snapshot.root.children = {std::move(wide), std::move(compact)};
    return snapshot;
}

widgetrail::pinned::WidgetSurfaceAdmission Admission(const bool supported = true) {
    return {
        L"widgetrail.samples.sdk-gallery",
        L"gallery.instance",
        L"runtime-1",
        L"presentation-1",
        L"SDK Gallery",
        supported,
        Snapshot(),
    };
}

widgetrail::WidgetDescriptor Descriptor(
    const std::wstring_view runtime = L"runtime-1",
    const bool supported = true) {
    widgetrail::WidgetDescriptor descriptor;
    descriptor.id = L"widgetrail.samples.sdk-gallery";
    descriptor.name = L"SDK Gallery";
    descriptor.instanceId = L"gallery.instance";
    descriptor.runtimeGeneration = runtime;
    descriptor.presentationGeneration = L"presentation-1";
    descriptor.pinningSupported = supported;
    return descriptor;
}

widgetrail::WidgetNode FocusButton(const wchar_t* id) {
    widgetrail::WidgetNode node;
    node.id = id;
    node.kind = L"button";
    node.actionId = L"activate";
    return node;
}

void AddDirectionalGeometry(
    widgetrail::RenderResult& render,
    std::wstring id,
    const widgetrail::declarative::Rect rect,
    const bool revealable = false) {
    render.focusScopes[id] = L"root";
    render.navigationRects[id] = rect;
    render.navigationEnabled[id] = true;
    if (revealable) {
        render.revealableFocusIds.insert(std::move(id));
    } else {
        render.focusRects[id] = rect;
        render.hitRegions.push_back({std::move(id), rect, true});
    }
}

void DirectionalOwnerAndHostRoutingContract() {
    using namespace widgetrail::input;
    const auto crossAxis = [](const bool horizontal) {
        widgetrail::WidgetSnapshot snapshot;
        snapshot.sequence = 165;
        snapshot.instanceId = L"cross-axis.instance";
        snapshot.activeInputScopeId = L"root";
        snapshot.root.id = L"root";
        snapshot.root.kind = L"stack";
        snapshot.root.inputScopeId = L"root";
        widgetrail::WidgetNode scroll;
        scroll.id = horizontal ? L"horizontal.rail" : L"vertical.list";
        scroll.kind = L"scroll";
        scroll.scrollAxis = horizontal ? L"horizontal" : L"vertical";
        scroll.children.push_back(FocusButton(L"inside"));
        snapshot.root.children.push_back(std::move(scroll));
        snapshot.root.children.push_back(FocusButton(L"outside"));
        widgetrail::RenderResult render;
        AddDirectionalGeometry(
            render, L"inside",
            horizontal
                ? widgetrail::declarative::Rect{100.0F, 100.0F, 40.0F, 40.0F}
                : widgetrail::declarative::Rect{100.0F, 100.0F, 40.0F, 40.0F});
        AddDirectionalGeometry(
            render, L"outside",
            horizontal
                ? widgetrail::declarative::Rect{100.0F, 0.0F, 40.0F, 40.0F}
                : widgetrail::declarative::Rect{220.0F, 100.0F, 40.0F, 40.0F});
        render.scrollViewports.emplace(
            snapshot.root.children.front().id,
            widgetrail::RenderScrollViewport{
                horizontal
                    ? widgetrail::declarative::ScrollAxis::Horizontal
                    : widgetrail::declarative::ScrollAxis::Vertical,
                {80.0F, 80.0F, 100.0F, 100.0F}, 0.0F, 200.0F});
        return std::pair{std::move(snapshot), std::move(render)};
    };

    for (const bool horizontal : {true, false}) {
        auto [snapshot, render] = crossAxis(horizontal);
        const auto direction = horizontal
            ? NavigationDirection::Up
            : NavigationDirection::Right;
        const auto owner = ResolveFocusedScrollOwner(
            snapshot.root, L"inside",
            horizontal
                ? widgetrail::declarative::ScrollAxis::Vertical
                : widgetrail::declarative::ScrollAxis::Horizontal,
            snapshot.activeInputScopeId, render);
        const auto resolution =
            SurfaceInteractionTransactions::ResolveDirectionalFocus(
                snapshot, L"inside", direction, render);
        Check(owner.disposition ==
                  FocusedScrollResolutionDisposition::NoEligibleScroll &&
                  resolution.target == L"outside" &&
                  !resolution.requiresScrollBoundaryAdmission,
              horizontal
                  ? "Up/Down may leave a horizontal rail without stale authority"
                  : "Left/Right may leave a vertical list without stale authority");
    }

    auto [staleSnapshot, staleRender] = crossAxis(false);
    staleRender.scrollViewports.clear();
    const auto stale = SurfaceInteractionTransactions::ResolveDirectionalFocus(
        staleSnapshot, L"inside", NavigationDirection::Down, staleRender);
    WidgetInteractionSession stalePagination;
    const WidgetInteractionAuthority staleAuthority{
        L"directional.widget", &staleSnapshot, L"runtime-1",
        L"presentation-1", false};
    const auto blocked = AdmitDirectionalFocusResolution(
        stalePagination, staleAuthority, staleRender, L"inside",
        NavigationDirection::Down, stale,
        ScrollPaginationIntentSource::DirectionalNavigation, 1);
    bool trayReceivedFocus{};
    if (!blocked.retainFocus && !blocked.resolution.target)
        trayReceivedFocus = true;
    Check(stale.disposition == DirectionalFocusDisposition::BlockedAuthority &&
              blocked.retainFocus && !trayReceivedFocus,
          "a missing matching-axis viewport keeps widget focus and suppresses tray transfer");

    widgetrail::WidgetSnapshot nested;
    nested.sequence = 166;
    nested.instanceId = L"nested-scroll.instance";
    nested.activeInputScopeId = L"root";
    nested.root.id = L"root";
    nested.root.kind = L"stack";
    nested.root.inputScopeId = L"root";
    widgetrail::WidgetNode outer;
    outer.id = L"outer.scroll";
    outer.kind = L"scroll";
    outer.scrollAxis = L"vertical";
    outer.scrollNearEndActionId = L"outer.next";
    outer.scrollPaginationThreshold = 2;
    widgetrail::WidgetNode inner;
    inner.id = L"inner.scroll";
    inner.kind = L"scroll";
    inner.scrollAxis = L"vertical";
    inner.scrollNearEndActionId = L"inner.next";
    inner.scrollPaginationThreshold = 1;
    inner.children.push_back(FocusButton(L"inner.last"));
    outer.children.push_back(std::move(inner));
    outer.children.push_back(FocusButton(L"outer.row"));
    nested.root.children.push_back(std::move(outer));
    nested.root.children.push_back(FocusButton(L"surface.footer"));
    widgetrail::RenderResult nestedRender;
    AddDirectionalGeometry(
        nestedRender, L"inner.last", {0.0F, 0.0F, 120.0F, 40.0F});
    AddDirectionalGeometry(
        nestedRender, L"outer.row", {0.0F, 80.0F, 120.0F, 40.0F}, true);
    AddDirectionalGeometry(
        nestedRender, L"surface.footer", {0.0F, 140.0F, 120.0F, 40.0F});
    nestedRender.scrollViewports.emplace(
        L"inner.scroll",
        widgetrail::RenderScrollViewport{
            widgetrail::declarative::ScrollAxis::Vertical,
            {0.0F, 0.0F, 120.0F, 45.0F}, 0.0F, 60.0F});
    nestedRender.scrollViewports.emplace(
        L"outer.scroll",
        widgetrail::RenderScrollViewport{
            widgetrail::declarative::ScrollAxis::Vertical,
            {0.0F, 0.0F, 120.0F, 100.0F}, 0.0F, 130.0F});
    auto nestedResolution =
        SurfaceInteractionTransactions::ResolveDirectionalFocus(
            nested, L"inner.last", NavigationDirection::Down, nestedRender);
    Check(nestedResolution.target == L"outer.row" &&
              nestedResolution.requiresScrollBoundaryAdmission,
          "an outer-scroll candidate cannot bypass the deepest matching-axis owner");
    const WidgetInteractionAuthority nestedAuthority{
        L"nested.widget", &nested, L"runtime-1", L"presentation-1", false};
    WidgetInteractionSession mainOwner;
    WidgetInteractionSession pinnedOwner;
    auto mainDecision = AdmitDirectionalFocusResolution(
        mainOwner, nestedAuthority, nestedRender, L"inner.last",
        NavigationDirection::Down, nestedResolution,
        ScrollPaginationIntentSource::DirectionalNavigation, 10);
    auto pinnedDecision = AdmitDirectionalFocusResolution(
        pinnedOwner, nestedAuthority, nestedRender, L"inner.last",
        NavigationDirection::Down, nestedResolution,
        ScrollPaginationIntentSource::DirectionalNavigation, 10);
    auto [mainRequest, mainAcquire] = mainOwner.AcquireScrollPaginationDispatch(
        nestedAuthority, nestedRender, 11);
    auto [pinnedRequest, pinnedAcquire] =
        pinnedOwner.AcquireScrollPaginationDispatch(
            nestedAuthority, nestedRender, 11);
    Check(mainDecision.retainFocus && pinnedDecision.retainFocus &&
              mainRequest && pinnedRequest &&
              mainAcquire.diagnostics.empty() &&
              pinnedAcquire.diagnostics.empty() &&
              mainRequest->action.scrollId == L"inner.scroll" &&
              pinnedRequest->action.scrollId == mainRequest->action.scrollId &&
              pinnedRequest->action.actionId == mainRequest->action.actionId,
          "main and pinned decisions retain and dispatch the same deepest-owner page authority");

    auto finiteNested = nested;
    auto& finiteInner = finiteNested.root.children[0].children[0];
    finiteInner.scrollNearEndActionId.clear();
    finiteInner.scrollPaginationThreshold = 0;
    auto finiteResolution =
        SurfaceInteractionTransactions::ResolveDirectionalFocus(
            finiteNested, L"inner.last", NavigationDirection::Down,
            nestedRender);
    WidgetInteractionSession finiteOwner;
    const WidgetInteractionAuthority finiteAuthority{
        L"nested.widget", &finiteNested, L"runtime-1", L"presentation-1", false};
    const auto finiteDecision = AdmitDirectionalFocusResolution(
        finiteOwner, finiteAuthority, nestedRender, L"inner.last",
        NavigationDirection::Down, finiteResolution,
        ScrollPaginationIntentSource::DirectionalNavigation, 12);
    Check(finiteResolution.target == L"outer.row" &&
              finiteResolution.requiresScrollBoundaryAdmission &&
              !finiteDecision.retainFocus,
          "a finite inner Scroll may enter its containing outer Scroll after bounded admission");

    auto outerResolution =
        SurfaceInteractionTransactions::ResolveDirectionalFocus(
            nested, L"outer.row", NavigationDirection::Down, nestedRender);
    WidgetInteractionSession outerOwner;
    auto outerDecision = AdmitDirectionalFocusResolution(
        outerOwner, nestedAuthority, nestedRender, L"outer.row",
        NavigationDirection::Down, outerResolution,
        ScrollPaginationIntentSource::DirectionalNavigation, 13);
    auto [outerRequest, outerAcquire] =
        outerOwner.AcquireScrollPaginationDispatch(
            nestedAuthority, nestedRender, 14);
    Check(outerResolution.target == L"surface.footer" &&
              outerResolution.requiresScrollBoundaryAdmission &&
              outerDecision.retainFocus && outerRequest &&
              outerAcquire.diagnostics.empty() &&
              outerRequest->action.scrollId == L"outer.scroll" &&
              outerRequest->action.actionId == L"outer.next",
          "after inner admission the containing Scroll owns its independent outer boundary");

    nested.root.children[0].children[0].children[0].focusDown = L"outer.row";
    const auto explicitExit =
        SurfaceInteractionTransactions::ResolveDirectionalFocus(
            nested, L"inner.last", NavigationDirection::Down, nestedRender);
    Check(explicitExit.disposition == DirectionalFocusDisposition::Explicit &&
              explicitExit.target == L"outer.row" &&
              !explicitExit.requiresScrollBoundaryAdmission,
          "explicit authored links keep their existing pagination bypass precedence");
}

} // namespace

int main() {
    const HRESULT apartment = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    try {
        Check(SUCCEEDED(apartment), "COM apartment initializes");
        (void)SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        ComPtr<ID2D1Factory> d2d;
        ComPtr<IDWriteFactory> write;
        Check(SUCCEEDED(D2D1CreateFactory(
                  D2D1_FACTORY_TYPE_SINGLE_THREADED,
                  IID_PPV_ARGS(d2d.ReleaseAndGetAddressOf()))),
              "D2D factory initializes");
        Check(SUCCEEDED(DWriteCreateFactory(
                  DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
                  reinterpret_cast<IUnknown**>(write.ReleaseAndGetAddressOf()))),
              "DWrite factory initializes");
        DirectionalOwnerAndHostRoutingContract();

        widgetrail::pinned::WidgetSurfaceCoordinator coordinator;
        std::wstring error;
        const auto placementRoot = std::filesystem::temp_directory_path() /
            (L"wrail-widget-surface-" + std::to_wstring(GetCurrentProcessId()));
        Check(coordinator.Initialize(
                  GetModuleHandleW(nullptr), nullptr, WM_APP + 0x410,
                  d2d.Get(), write.Get(), nullptr, error,
                  placementRoot / L"placement.ini"),
              "coordinator initializes with current production primitives");
        coordinator.OnOverlayShown();
        Check(!coordinator.Pin(Admission(false), error) && !error.empty(),
              "non-supporting widget is rejected safely");

        {
            widgetrail::pinned::WidgetSurfaceCoordinator lifetime;
            const auto lifetimeRoot = placementRoot / L"renderer-lifetime";
            Check(lifetime.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x413,
                      d2d.Get(), write.Get(), nullptr, error,
                      lifetimeRoot / L"placement.ini"),
                  "renderer-lifetime fixture initializes through the coordinator owner");
            lifetime.OnOverlayShown();
            widgetrail::testing::ResetRendererWidgetStateRetirementForTesting();
            Check(lifetime.Pin(Admission(), error) && lifetime.CommitSetup(error),
                  "renderer-lifetime fixture commits its first pin");
            PumpPendingMessages();
            SendMessageW(
                lifetime.window(), WM_SIZE, SIZE_RESTORED, MAKELPARAM(640, 420));
            Check(widgetrail::testing::RendererWidgetStateRetirementCountForTesting() == 0,
                  "graphics resource recreation preserves live renderer widget state");
            Check(lifetime.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::Unpin) &&
                      widgetrail::testing::RendererWidgetStateRetirementCountForTesting() == 1 &&
                      widgetrail::testing::LastRetiredRendererWidgetInstanceForTesting() ==
                          L"gallery.instance",
                  "unpin retires the exact renderer widget lifetime once");
            Check(lifetime.Pin(Admission(), error) && lifetime.CommitSetup(error) &&
                      widgetrail::testing::RendererWidgetStateRetirementCountForTesting() == 1,
                  "same-authority repin starts after retirement without another reset");
            const auto unexpectedWindow = lifetime.window();
            Check(unexpectedWindow && DestroyWindow(unexpectedWindow) &&
                      widgetrail::testing::RendererWidgetStateRetirementCountForTesting() == 2 &&
                      widgetrail::testing::LastRetiredRendererWidgetInstanceForTesting() ==
                          L"gallery.instance" &&
                      !lifetime.pinned(),
                  "unexpected pinned-window destruction retires renderer widget state");
            lifetime.Dispose();
        }

        const auto verifyMediaRetirement = [&] (const bool overlayViewportAvailable) {
            widgetrail::pinned::WidgetSurfaceCoordinator retirement;
            const auto retirementRoot = placementRoot /
                (overlayViewportAvailable
                    ? L"media-retirement-visible"
                    : L"media-retirement-retained-hidden");
            Check(retirement.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x414,
                      d2d.Get(), write.Get(), nullptr, error,
                      retirementRoot / L"placement.ini"),
                  "media-retirement fixture initializes through the coordinator owner");
            retirement.OnOverlayShown();
            Check(retirement.Pin(Admission(), error) && retirement.CommitSetup(error),
                  "media-retirement fixture commits its pinned authority");
            enum class Projection { Overlay, Pinned };
            Projection projection = Projection::Pinned;
            bool callbackObserved = false;
            bool retiringWindowStillAlive = false;
            bool reentrantUnpinRejected = false;
            bool transferPending = false;
            const std::wstring exactResidentSession{
                L"gallery|gallery.instance|runtime-1|presentation-1|media.surface"};
            std::wstring residentSession = exactResidentSession;
            retirement.SetBeforeWindowRetirement([&](const auto) {
                callbackObserved = true;
                retiringWindowStillAlive = retirement.window() &&
                    IsWindow(retirement.window());
                Check(!retirement.pinned(),
                      "retirement callback observed stale pinned authority");
                reentrantUnpinRejected = !retirement.Unpin(
                    widgetrail::pinned::WidgetSurfaceStopReason::Unpin);
                projection = Projection::Overlay;
                transferPending = !overlayViewportAvailable;
                const auto reconcileCommittedAndLifecycle = [&] {
                    if (retirement.pinned()) projection = Projection::Pinned;
                };
                reconcileCommittedAndLifecycle();
                reconcileCommittedAndLifecycle();
            });
            Check(retirement.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::Unpin) &&
                      callbackObserved && retiringWindowStillAlive &&
                      reentrantUnpinRejected && !retirement.pinned() &&
                      projection == Projection::Overlay &&
                      transferPending == !overlayViewportAvailable &&
                      residentSession == exactResidentSession,
                  overlayViewportAvailable
                      ? "visible-player unpin completes one Overlay handoff without stale pinned authority"
                      : "retained-hidden unpin stays detached without stale pinned retarget or exact-session retirement");
            if (!overlayViewportAvailable) {
                transferPending = false;
                Check(projection == Projection::Overlay &&
                          residentSession == exactResidentSession,
                      "later visible-player return completes the pending same-session Overlay reattach");
            }
            retirement.Dispose();
            std::error_code retirementCleanup;
            std::filesystem::remove_all(retirementRoot, retirementCleanup);
        };
        verifyMediaRetirement(false);
        verifyMediaRetirement(true);

        {
            widgetrail::pinned::WidgetSurfaceCoordinator layoutCancellation;
            const auto layoutCancellationRoot =
                placementRoot / L"layout-cancellation-retirement";
            Check(layoutCancellation.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x415,
                      d2d.Get(), write.Get(), nullptr, error,
                      layoutCancellationRoot / L"placement.ini"),
                  "layout-cancellation fixture initializes through the coordinator owner");
            layoutCancellation.OnOverlayShown();
            auto admission = Admission();
            admission.pinnedLayouts = {
                {L"compact", L"Compact", 360.0F, 240.0F},
                {L"details", L"Details", 640.0F, 360.0F},
            };
            widgetrail::testing::ResetRendererWidgetStateRetirementForTesting();
            Check(layoutCancellation.Pin(admission, error) &&
                      layoutCancellation.CycleLayout(1) &&
                      layoutCancellation.CommitSetup(error) &&
                      widgetrail::testing::RendererWidgetStateRetirementCountForTesting() == 1,
                  "initial layout selection retires exact renderer instance state once");
            Check(layoutCancellation.BeginSetup(false) &&
                      layoutCancellation.CycleLayout(1) &&
                      widgetrail::testing::RendererWidgetStateRetirementCountForTesting() == 2,
                  "layout preview retires renderer state for the successor layout once");
            Check(layoutCancellation.CancelSetup() &&
                      layoutCancellation.selectedLayoutName() == L"Compact" &&
                      widgetrail::testing::RendererWidgetStateRetirementCountForTesting() == 3 &&
                      widgetrail::testing::LastRetiredRendererWidgetInstanceForTesting() ==
                          admission.instanceId,
                  "reverse layout cancellation retires preview renderer state exactly once");
            Check(layoutCancellation.Unpin(
                      widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "layout-cancellation fixture retires its pinned authority");
            layoutCancellation.Dispose();
            std::error_code layoutCancellationCleanup;
            std::filesystem::remove_all(
                layoutCancellationRoot, layoutCancellationCleanup);
        }

        {
            widgetrail::pinned::WidgetSurfaceCoordinator slider;
            const auto sliderRoot = placementRoot / L"slider";
            Check(slider.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x414,
                      d2d.Get(), write.Get(), nullptr, error,
                      sliderRoot / L"placement.ini"),
                  "pinned slider fixture initializes through the production owner");
            slider.OnOverlayShown();
            auto admission = Admission();
            admission.snapshot = SliderSnapshot();
            Check(slider.Pin(admission, error) && slider.CommitSetup(error),
                  "provider-neutral slider pin commits its full-widget projection");
            Check(slider.SetInteractionMode(
                      widgetrail::pinned::InteractionMode::Focusable) &&
                      slider.EnterControllerFocus() &&
                      slider.focusedElementId() == L"pin.fixture.slider",
                  "controller focus reaches the pinned slider");
            Check(slider.HandleFocusedSliderModeButton(L"a", 1'000),
                  "A enters pinned slider adjustment through the shared interaction session");
            Check(slider.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Right, true),
                  "D-pad Right is consumed by active pinned slider adjustment");
            auto requests = slider.TakeInputRequests();
            Check(requests.size() == 1 && requests[0].sliderActionRequest &&
                      requests[0].requestedValue == 45.0 &&
                      requests[0].sliderActionRequest->actionId ==
                          L"fixture-slider.changed" &&
                      requests[0].protocolButton == L"dPadRight",
                  "active pinned adjustment queues one exact absolute valueChanged action");
            Check(slider.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      SliderSnapshot(2, 40.0)) &&
                      slider.IsCurrentInputRequest(requests[0]),
                  "compatible successor retains queued pinned slider authority");
            Check(slider.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      SliderSnapshot(3, 45.0)),
                  "authoritative successor acknowledges the pinned slider value");
            UpdateWindow(slider.window());
            Check(slider.HandleFocusedSliderModeButton(L"a", 1'100),
                  "A exits active pinned slider adjustment");
            Check(slider.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Right, true) &&
                      slider.focusedElementId() == L"pin.fixture.after-slider",
                  "outside adjustment mode D-pad resumes directional navigation");
            Check(slider.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "pinned slider fixture retires its exact controller authority");
            slider.Dispose();
            std::error_code sliderCleanup;
            std::filesystem::remove_all(sliderRoot, sliderCleanup);
        }

        {
            const HWND focusSink = CreateWindowExW(
                0, L"STATIC", L"Pinned Select UIA focus sink", 0,
                0, 0, 0, 0, HWND_MESSAGE, nullptr,
                GetModuleHandleW(nullptr), nullptr);
            Check(focusSink != nullptr,
                  "pinned Select UIA focus fixture owns a real focus sink");
            widgetrail::pinned::WidgetSurfaceCoordinator selectFocus;
            const auto selectFocusRoot = placementRoot / L"select-uia-focus";
            Check(selectFocus.Initialize(
                      GetModuleHandleW(nullptr), focusSink, WM_APP + 0x41D,
                      d2d.Get(), write.Get(), nullptr, error,
                      selectFocusRoot / L"placement.ini"),
                  "pinned Select UIA focus fixture initializes through the production owner");
            selectFocus.OnOverlayShown();
            auto admission = Admission();
            admission.snapshot = SelectSnapshot();
            Check(selectFocus.Pin(admission, error) &&
                      selectFocus.CommitSetup(error) &&
                      selectFocus.SetInteractionMode(
                          widgetrail::pinned::InteractionMode::Focusable),
                  "collapsed pinned Select enters Focusable UIA mode");
            UpdateWindow(selectFocus.window());
            PumpPendingMessages();
            (void)SetFocus(focusSink);
            Check(GetFocus() == focusSink &&
                      !selectFocus.controllerFocused() &&
                      !selectFocus.selectPopupOpen(),
                  "pinned Select UIA focus begins collapsed without controller or window focus");
            const auto opener = FindAutomationId(
                selectFocus.window(), L"widget:pin.fixture.select");
            Check(opener && SUCCEEDED(opener->SetFocus()),
                  "UIA SetFocus admits the visible collapsed Select opener");
            PumpPendingMessages();
            const auto focusedOpener = FindAutomationId(
                selectFocus.window(), L"widget:pin.fixture.select");
            BOOL providerFocused{};
            Check(selectFocus.controllerFocused() &&
                      GetFocus() == selectFocus.window() &&
                      selectFocus.focusedElementId() == L"pin.fixture.select" &&
                      !selectFocus.selectPopupOpen() && focusedOpener &&
                      SUCCEEDED(focusedOpener->get_CurrentHasKeyboardFocus(
                          &providerFocused)) &&
                      providerFocused == TRUE,
                  "collapsed Select UIA focus publishes matching controller window and provider focus");
            Check(selectFocus.Unpin(
                      widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "pinned Select UIA focus fixture retires exact authority");
            selectFocus.Dispose();
            DestroyWindow(focusSink);
            std::error_code selectFocusCleanup;
            std::filesystem::remove_all(selectFocusRoot, selectFocusCleanup);
        }

        {
            widgetrail::pinned::WidgetSurfaceCoordinator select;
            const auto selectRoot = placementRoot / L"select";
            Check(select.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x41A,
                      d2d.Get(), write.Get(), nullptr, error,
                      selectRoot / L"placement.ini"),
                  "pinned Select fixture initializes through the production owner");
            select.OnOverlayShown();
            auto admission = Admission();
            admission.snapshot = SelectSnapshot();
            Check(select.Pin(admission, error) && select.CommitSetup(error) &&
                      select.SetInteractionMode(
                          widgetrail::pinned::InteractionMode::Focusable),
                  "provider-neutral Select enters pinned pointer/UIA mode");
            UpdateWindow(select.window());
            PumpPendingMessages();
            Check(!select.controllerFocused(),
                  "pinned Select begins without controller focus ownership");

            const auto openerPoint =
                select.PointerPointForTesting(L"pin.fixture.select");
            Check(openerPoint.has_value(),
                  "pinned Select exposes one production pointer hit region");
            SendMessageW(select.window(), WM_LBUTTONDOWN, MK_LBUTTON,
                         MAKELPARAM(openerPoint->x, openerPoint->y));
            SendMessageW(select.window(), WM_LBUTTONUP, 0,
                         MAKELPARAM(openerPoint->x, openerPoint->y));
            PumpPendingMessages();
            Check(select.selectPopupOpen(),
                  "pointer activation opens a Focusable pinned Select without controller focus");
            Check(select.MoveSelectPopupWheel(-WHEEL_DELTA),
                  "pinned wheel movement advances the open Select highlight");
            Check(select.HandleFocusedSelectButton(
                      L"a", widgetrail::ControllerInputOrigin::AccessibilityAutomation),
                  "shared Select activation commits the wheel-highlighted option");
            auto pointerRequests = select.TakeInputRequests();
            Check(pointerRequests.size() == 1 &&
                      pointerRequests[0].selectActionRequest &&
                      pointerRequests[0].selectActionRequest->actionId ==
                          L"density.comfortable" &&
                      pointerRequests[0].origin ==
                          widgetrail::ControllerInputOrigin::AccessibilityAutomation,
                  "pinned pointer/wheel path queues one exact selected option authority");
            Check(select.IsCurrentInputRequest(pointerRequests[0]) &&
                      select.UpdateSnapshot(
                          admission.widgetId, admission.runtimeGeneration,
                          SelectSnapshot(2, false, true)) &&
                      !select.IsCurrentInputRequest(pointerRequests[0]),
                  "changed Select action binding rejects retained pinned input authority");

            Check(ExpandAutomationId(
                      select.window(), L"widget:pin.fixture.select") &&
                      select.selectPopupOpen(),
                  "UI Automation expands the same Focusable pinned Select");
            const std::wstring optionAutomationId =
                L"widget-option:select-option:" +
                std::to_wstring(std::wstring_view(L"pin.fixture.select").size()) +
                L":pin.fixture.select" +
                std::to_wstring(std::wstring_view(L"comfortable").size()) +
                L":comfortable";
            const auto option = FindAutomationId(
                select.window(), optionAutomationId.c_str());
            Check(option && SUCCEEDED(option->SetFocus()),
                  "enabled pinned Select option accepts UIA focus");
            PumpPendingMessages();
            Check(select.controllerFocused(),
                  "pinned option UIA focus acquires controller ownership before highlight");
            Check(GetFocus() == select.window(),
                  "pinned option UIA focus acquires exact window focus before highlight");
            Check(select.selectPopupOpen(),
                  "pinned option UIA focus retains its popup while highlighting");
            Check(select.HandleFocusedSelectButton(L"b") &&
                      !select.selectPopupOpen(),
                  "B closes the pinned Select popup without leaving the surface");

            Check(select.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      SelectSnapshot(3, true)),
                  "pinned Select admits an all-unavailable successor");
            UpdateWindow(select.window());
            PumpPendingMessages();
            const auto unavailablePoint =
                select.PointerPointForTesting(L"pin.fixture.select");
            Check(unavailablePoint.has_value(),
                  "all-unavailable pinned Select retains its visible pointer opener");
            SendMessageW(select.window(), WM_LBUTTONDOWN, MK_LBUTTON,
                         MAKELPARAM(unavailablePoint->x, unavailablePoint->y));
            SendMessageW(select.window(), WM_LBUTTONUP, 0,
                         MAKELPARAM(unavailablePoint->x, unavailablePoint->y));
            PumpPendingMessages();
            Check(!select.selectPopupOpen() && select.TakeInputRequests().empty(),
                  "pinned pointer consumes all-unavailable Select activation without dispatch");
            SendMessageW(select.window(), WM_KEYDOWN, VK_RETURN, 0);
            PumpPendingMessages();
            Check(!select.selectPopupOpen() && select.TakeInputRequests().empty(),
                  "pinned keyboard fallback consumes all-unavailable Select Enter without dispatch");
            Check(select.HandleFocusedSelectButton(
                      L"a", widgetrail::ControllerInputOrigin::PhysicalController) &&
                      !select.selectPopupOpen() && select.TakeInputRequests().empty(),
                  "pinned controller Select owner consumes A before slider and generic dispatch");
            Check(select.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "pinned Select teardown retires popup and input authority");
            select.Dispose();
            std::error_code selectCleanup;
            std::filesystem::remove_all(selectRoot, selectCleanup);
        }

        {
            widgetrail::pinned::WidgetSurfaceCoordinator freeScrollSelect;
            const auto fixtureRoot = placementRoot / L"select-free-scroll-transition";
            Check(freeScrollSelect.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x41C,
                      d2d.Get(), write.Get(), nullptr, error,
                      fixtureRoot / L"placement.ini"),
                  "pinned Select free-scroll fixture initializes through the production owner");
            freeScrollSelect.OnOverlayShown();
            auto admission = Admission();
            admission.snapshot = FreeScrollSelectSnapshot();
            admission.pinnedLayouts = {{
                L"scroll-select-layout", L"Scrollable Select", 420.0F, 320.0F,
                FreeScrollSelectSnapshot(),
            }};
            Check(freeScrollSelect.Pin(admission, error) &&
                      freeScrollSelect.CycleLayout(1) &&
                      freeScrollSelect.CommitSetup(error) &&
                      freeScrollSelect.SetInteractionMode(
                          widgetrail::pinned::InteractionMode::Focusable),
                  "pinned Select free-scroll fixture commits known-good scroll geometry");
            UpdateWindow(freeScrollSelect.window());
            Check(freeScrollSelect.EnterControllerFocus(),
                  "pinned Select free-scroll fixture enters its in-scroll item");
            UpdateWindow(freeScrollSelect.window());
            Check(freeScrollSelect.ScrollFocusedProjection(0, -32'768, 1'000),
                  "known-good pinned geometry establishes renderer-owned free scroll");
            UpdateWindow(freeScrollSelect.window());
            Check(freeScrollSelect.FreeScrollBindingForTesting(),
                  "pinned Select fixture retains free scroll before UIA expansion");
            Check(ExpandAutomationId(
                      freeScrollSelect.window(), L"widget:pin.outside") &&
                      freeScrollSelect.focusedElementId() == L"pin.outside" &&
                      freeScrollSelect.selectPopupOpen() &&
                      !freeScrollSelect.FreeScrollBindingForTesting(),
                  "UIA expansion focuses the outside Select, opens it, and retires free scroll");
            Check(freeScrollSelect.HandleFocusedSelectButton(L"b") &&
                      freeScrollSelect.Unpin(
                          widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "pinned Select free-scroll fixture closes and retires exact authority");
            freeScrollSelect.Dispose();
            std::error_code fixtureCleanup;
            std::filesystem::remove_all(fixtureRoot, fixtureCleanup);
        }

        {
            widgetrail::pinned::WidgetSurfaceCoordinator transition;
            const auto transitionRoot = placementRoot / L"select-focus-transition";
            Check(transition.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x41B,
                      d2d.Get(), write.Get(), nullptr, error,
                      transitionRoot / L"placement.ini"),
                  "pinned Select focus-transition fixture initializes through the production owner");
            transition.OnOverlayShown();
            auto admission = Admission();
            admission.snapshot = SelectFocusTransitionSnapshot();
            Check(transition.Pin(admission, error) && transition.CommitSetup(error) &&
                      transition.SetInteractionMode(
                          widgetrail::pinned::InteractionMode::Focusable),
                  "pinned Select focus-transition fixture establishes its lean interactive surface");
            UpdateWindow(transition.window());
            Check(transition.EnterControllerFocus(),
                  "pinned Select focus-transition fixture enters controller focus");
            UpdateWindow(transition.window());
            Check(transition.focusedElementId() == L"pin.transition.outside" &&
                      transition.MoveControllerFocus(
                          widgetrail::input::NavigationDirection::Down) &&
                      transition.focusedElementId() == L"pin.transition.slider",
                  "pinned focus enters the lean group through its authored initial slider");
            UpdateWindow(transition.window());
            const auto transitionPaint = transition.PaintTraceForTesting();
            const auto sliderPoint = transition.PointerPointForTesting(
                L"pin.transition.slider");
            const auto selectPoint = transition.PointerPointForTesting(
                L"pin.transition.select");
            const auto selectAutomation = FindAutomationId(
                transition.window(), L"widget:pin.transition.select");
            CheckRenderSucceeded(
                transitionPaint,
                "lean Select-in-Row declarative render succeeds");
            Check(transitionPaint.navigationNodeCount == 3,
                  "lean Select-in-Row exposes all three navigation nodes");
            Check(sliderPoint.has_value(),
                  "lean Slider owns a visible pointer production region");
            Check(selectPoint.has_value(),
                  "lean Select owns a visible pointer production region");
            Check(static_cast<bool>(selectAutomation),
                  "lean Select owns a visible UIA production region");
            Check(transition.HandleFocusedSliderModeButton(L"a", 1'010) &&
                      transition.SliderAdjustmentActiveForTesting(
                          L"pin.transition.slider", 1'011),
                  "lean pinned fixture establishes prior activation-first slider state");
            UpdateWindow(transition.window());
            PumpPendingMessages();
            Check(ExpandAutomationId(
                      transition.window(), L"widget:pin.transition.select"),
                  "UIA invokes Expand on the lean nonfocused Select");
            Check(transition.focusedElementId() == L"pin.transition.select",
                  "UIA Select expansion transfers exact pinned focus");
            Check(transition.selectPopupOpen(),
                  "UIA Select expansion opens the pinned popup");
            Check(!transition.SliderAdjustmentActiveForTesting(
                      L"pin.transition.slider", 1'012),
                  "UIA Select focus transition retires prior slider interaction state");
            Check(transition.HandleFocusedSelectButton(L"b") &&
                      transition.MoveControllerFocus(
                          widgetrail::input::NavigationDirection::Up) &&
                      transition.focusedElementId() == L"pin.transition.outside" &&
                      transition.MoveControllerFocus(
                          widgetrail::input::NavigationDirection::Down) &&
                      transition.focusedElementId() == L"pin.transition.select",
                  "pinned group memory restores the Select focused through UIA expansion");
            Check(transition.Unpin(
                      widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "pinned Select focus-transition fixture retires exact authority");
            transition.Dispose();
            std::error_code transitionCleanup;
            std::filesystem::remove_all(transitionRoot, transitionCleanup);
        }

        {
            widgetrail::pinned::WidgetSurfaceCoordinator groups;
            const auto groupRoot = placementRoot / L"focus-groups";
            Check(groups.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x419,
                      d2d.Get(), write.Get(), nullptr, error,
                      groupRoot / L"placement.ini"),
                  "pinned focus-group fixture initializes through the production owner");
            groups.OnOverlayShown();
            auto admission = Admission();
            admission.snapshot = FocusGroupSnapshot();
            Check(groups.Pin(admission, error) && groups.CommitSetup(error) &&
                      groups.SetInteractionMode(
                          widgetrail::pinned::InteractionMode::Focusable) &&
                      groups.EnterControllerFocus(),
                  "pinned focus-group fixture enters its exact initial control");
            PumpPendingMessages();
            Check(groups.focusedElementId() == L"pin.group.entry" &&
                      groups.MoveControllerFocus(
                          widgetrail::input::NavigationDirection::Down) &&
                      groups.focusedElementId() == L"pin.group.first",
                  "fresh pinned group entry resolves the authored initial child");
            Check(groups.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Right) &&
                      groups.focusedElementId() == L"pin.group.second" &&
                      groups.MoveControllerFocus(
                          widgetrail::input::NavigationDirection::Up) &&
                      groups.focusedElementId() == L"pin.group.entry",
                  "pinned owner remembers the exact descendant before leaving the group");
            Check(groups.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      FocusGroupSnapshot(2)) &&
                      groups.MoveControllerFocus(
                          widgetrail::input::NavigationDirection::Down) &&
                      groups.focusedElementId() == L"pin.group.second",
                  "compatible pinned successor restores the remembered group child");
            Check(groups.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "pinned group teardown retires its independent focus authority");
            groups.Dispose();
            std::error_code groupCleanup;
            std::filesystem::remove_all(groupRoot, groupCleanup);
        }

        for (int variant = 0; variant < 2; ++variant) {
            // A real media timeline publishes one semantic tree carrying both
            // responsive shells and republishes progress several times per
            // second. Cover the compact and expanded projections because the
            // selected shell decides which exact slider owns pinned focus.
            const bool expanded = variant == 1;
            widgetrail::pinned::WidgetSurfaceCoordinator media;
            const auto mediaRoot = placementRoot /
                (expanded ? L"media-expanded" : L"media-compact");
            Check(media.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x415,
                      d2d.Get(), write.Get(), nullptr, error,
                      mediaRoot / L"placement.ini"),
                  "media timeline fixture initializes through the production owner");
            media.OnOverlayShown();
            auto admission = Admission();
            admission.snapshot = MediaTimelineSnapshot(1, 123456.0);
            admission.initialContentWidthDip = expanded ? 1040.0F : 480.0F;
            admission.initialContentHeightDip = expanded ? 620.0F : 270.0F;
            admission.placementLimits.maximumWidthDip = 1600.0F;
            admission.placementLimits.maximumHeightDip = 1000.0F;
            Check(media.Pin(admission, error) && media.CommitSetup(error),
                  "media timeline pins its full-widget projection");
            Check(media.SetInteractionMode(
                      widgetrail::pinned::InteractionMode::Focusable) &&
                      media.EnterControllerFocus(),
                  "media timeline enters pinned controller focus");
            PumpPendingMessages();
            const std::wstring timelineId = expanded
                ? L"media.seek.slider"
                : L"media.player.compact.seek.slider";
            for (int attempt = 0;
                 attempt < 6 && media.focusedElementId() != timelineId; ++attempt) {
                (void)media.MoveControllerFocus(
                    widgetrail::input::NavigationDirection::Up, true);
                PumpPendingMessages();
            }
            Check(media.focusedElementId() == timelineId,
                  "pinned controller focus reaches the selected shell timeline");
            Check(media.HandleFocusedSliderModeButton(L"a", GetTickCount64()),
                  "A enters activation-first adjustment on the pinned timeline");
            PumpPendingMessages();

            // The provider republishes progress between the A press and the
            // first D-pad sample.
            long long sequence = 2;
            double position = 123456.0;
            for (int publish = 0; publish < 4; ++publish) {
                position += 220.0;
                Check(media.UpdateSnapshot(
                          admission.widgetId, admission.runtimeGeneration,
                          MediaTimelineSnapshot(sequence++, position)),
                      "media timeline accepts its authoritative successor");
                PumpPendingMessages();
            }

            // A topmost pinned tool window loses Win32 keyboard focus while the
            // controller still owns the projection. That is a pointer/keyboard
            // ownership change, not a loss of controller input authority.
            Check(media.window() != nullptr, "pinned media timeline owns a window");
            SendMessageW(media.window(), WM_KILLFOCUS,
                         reinterpret_cast<WPARAM>(HWND{}), 0);
            PumpPendingMessages();
            Check(media.controllerFocused() &&
                      media.focusedElementId() == timelineId,
                  "Win32 focus loss retains pinned controller focus and its target");

            Check(media.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Right, true),
                  "D-pad Right stays consumed by adjustment after Win32 focus loss");
            auto queued = media.TakeInputRequests();
            Check(queued.size() == 1 && queued[0].sliderActionRequest &&
                      queued[0].nodeId == timelineId &&
                      queued[0].protocolButton == L"dPadRight" &&
                      queued[0].requestedValue == 125000.0 &&
                      queued[0].sliderActionRequest->actionId == L"media.seek" &&
                      queued[0].sliderActionRequest->requestedValue == 125000.0,
                  "adjustment surviving Win32 focus loss queues one exact absolute value");
            Check(media.focusedElementId() == timelineId,
                  "an adjusting pinned timeline never navigates away on D-pad");

            // A widget marks its slider busy while the value change it just
            // accepted is in flight. That must not cancel the adjustment the
            // user is still holding, and a further step must land once the
            // provider settles.
            Check(media.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      MediaTimelineSnapshot(sequence++, position, false, true)),
                  "media timeline accepts a busy in-flight successor");
            PumpPendingMessages();
            Check(media.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Right, true) &&
                      media.focusedElementId() == timelineId,
                  "a busy in-flight slider consumes D-pad without leaving adjustment");
            Check(media.TakeInputRequests().empty(),
                  "a busy in-flight slider queues no further value change");
            Check(media.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      MediaTimelineSnapshot(sequence++, 125000.0)),
                  "media timeline accepts the settled successor");
            PumpPendingMessages();
            Check(media.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Right, true),
                  "adjustment survives the busy round trip for the next step");
            auto secondStep = media.TakeInputRequests();
            Check(secondStep.size() == 1 && secondStep[0].sliderActionRequest &&
                      secondStep[0].requestedValue == 130000.0,
                  "the step after a busy round trip queues the next absolute value");

            // The left stick is the same directional owner as the D-pad.
            Check(media.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      MediaTimelineSnapshot(sequence++, 130000.0)),
                  "media timeline accepts the successor before the stick step");
            PumpPendingMessages();
            Check(media.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Left, true),
                  "the left stick adjusts a selected pinned slider like the D-pad");
            auto stickStep = media.TakeInputRequests();
            Check(stickStep.size() == 1 && stickStep[0].sliderActionRequest &&
                      stickStep[0].protocolButton == L"dPadLeft" &&
                      stickStep[0].requestedValue == 125000.0,
                  "a stick step queues one exact absolute value change");
            Check(media.focusedElementId() == timelineId,
                  "a stick step never navigates away from the adjusting slider");

            // Leave adjustment and prove ordinary pinned input queued one frame
            // before a publish is still delivered exactly once.
            Check(media.HandleFocusedSliderModeButton(L"b", GetTickCount64()),
                  "B exits pinned timeline adjustment");
            Check(media.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Down, true) &&
                      media.focusedElementId() != timelineId,
                  "outside adjustment mode D-pad resumes pinned focus navigation");
            PumpPendingMessages();
            const std::wstring activationTarget{media.focusedElementId()};
            Check(media.QueueFocusedInput(L"a"),
                  "the pinned transport button accepts controller activation");
            Check(media.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      MediaTimelineSnapshot(sequence++, 125000.0)),
                  "media timeline accepts a successor while input is queued");
            const auto survivors = media.TakeInputRequests();
            Check(survivors.size() == 1 && !survivors[0].sliderActionRequest &&
                      survivors[0].protocolButton == L"a" &&
                      survivors[0].nodeId == activationTarget,
                  "compatible queued pinned activation survives one publish exactly once");

            // A successor that withdraws the target still retires the request.
            Check(media.QueueFocusedInput(L"a"),
                  "the pinned transport button queues a second activation");
            Check(media.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      MediaTimelineSnapshot(sequence++, 125000.0, true)),
                  "media timeline accepts a successor disabling its transport");
            Check(media.TakeInputRequests().empty(),
                  "a successor disabling the target retires queued pinned input");

            Check(media.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "media timeline retires its pinned authority");
            media.Dispose();
            std::error_code mediaCleanup;
            std::filesystem::remove_all(mediaRoot, mediaCleanup);
        }

        {
            widgetrail::pinned::WidgetSurfaceCoordinator layouts;
            const auto layoutRoot = placementRoot / L"layouts";
            Check(layouts.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x411,
                      d2d.Get(), write.Get(), nullptr, error,
                      layoutRoot / L"placement.ini"),
                  "layout coordinator reuses the single production surface owner");
            layouts.OnOverlayShown();
            auto admission = Admission();
            auto compactProjection = Snapshot();
            compactProjection.activeInputScopeId = L"compact.root";
            compactProjection.initialFocusId = L"compact.action";
            compactProjection.root.id = L"compact.root";
            compactProjection.root.inputScopeId = L"compact.root";
            widgetrail::WidgetNode compactAction;
            compactAction.id = L"compact.action";
            compactAction.kind = L"button";
            compactAction.text = L"Compact action";
            compactAction.accessibilityLabel = compactAction.text;
            compactAction.actionId = L"compact-action";
            compactAction.inputScopeId = L"compact.root";
            compactProjection.root.children = {std::move(compactAction)};
            admission.pinnedLayouts = {
                {L"compact", L"Compact", 360.0F, 240.0F, compactProjection},
                {L"details", L"Details", 640.0F, 360.0F},
            };
            Check(layouts.Pin(admission, error) && layouts.setupActive() &&
                      layouts.layoutCount() == 3 && layouts.selectedLayoutIndex() == 0 &&
                      layouts.selectedLayoutName() == L"Full widget",
                  "Pin enters setup with the host Full widget fallback first");
            Check(!layouts.Pin(admission, error) &&
                      layouts.pinned() && layouts.layoutCount() == 3,
                  "layout setup retains the one-surface ownership cap");
            Check(layouts.CycleLayout(1) && layouts.selectedLayoutIndex() == 1 &&
                      layouts.selectedLayoutName() == L"Compact",
                  "LT or RT setup cycling selects one bounded authored layout");
            const auto compactDemand = layouts.TakeLayoutSelectionNotifications();
            Check(compactDemand.size() == 1 && compactDemand[0].selected &&
                      compactDemand[0].layoutId == L"compact" &&
                      compactDemand[0].runtimeGeneration == admission.runtimeGeneration,
                  "explicit selection publishes one generation-bound package demand");
            Check(layouts.QueueFocusedInput(L"a"),
                  "selected declarative projection owns focus and action resolution");
            const auto projectedInput = layouts.TakeInputRequests();
            Check(projectedInput.size() == 1 &&
                      projectedInput[0].nodeId == L"compact.action" &&
                      projectedInput[0].activeInputScopeId == L"compact.root" &&
                      layouts.IsCurrentInputRequest(projectedInput[0]),
                  "pinned input authority is bound to the atomically selected projection");
            auto wrongLayoutInput = projectedInput[0];
            wrongLayoutInput.selectedLayoutId = L"details";
            auto futureSequenceInput = projectedInput[0];
            ++futureSequenceInput.snapshotSequence;
            Check(!layouts.IsCurrentInputRequest(wrongLayoutInput) &&
                      !layouts.IsCurrentInputRequest(futureSequenceInput),
                  "wrong-layout and future-sequence pinned input fail closed");
            Check(layouts.QueueFocusedInput(L"b"),
                  "B enters the same selected-projection queue as other authored input");
            const auto backInput = layouts.TakeInputRequests();
            Check(backInput.size() == 1 &&
                      backInput[0].protocolButton == L"b" &&
                      backInput[0].selectedLayoutId == L"compact" &&
                      backInput[0].activeInputScopeId == L"compact.root" &&
                      layouts.IsCurrentInputRequest(backInput[0]),
                  "pinned B retains exact layout scope focus and sequence authority");
            Check(layouts.QueueFocusedInput(L"menu"),
                  "Menu remains ordinary selected-projection input outside host modes");
            const auto menuInput = layouts.TakeInputRequests();
            Check(menuInput.size() == 1 &&
                      menuInput[0].protocolButton == L"menu" &&
                      layouts.IsCurrentInputRequest(menuInput[0]),
                  "pinned Menu retains the same current authority as B");
            Check(layouts.CommitSetup(error) && !layouts.setupActive() &&
                      layouts.interactionMode() ==
                          widgetrail::pinned::InteractionMode::ClickThrough,
                  "A commits layout setup and restores click-through");
            RECT committedSetup{};
            GetWindowRect(layouts.window(), &committedSetup);
            Check(!layouts.CycleLayout(1) && layouts.selectedLayoutName() == L"Compact",
                  "layout triggers cannot consume LT or RT outside setup");
            Check(layouts.BeginSetup(false) && layouts.CycleLayout(1) &&
                      layouts.selectedLayoutName() == L"Details" &&
                      layouts.StepPlacement(
                          widgetrail::pinned::PlacementMode::Move,
                          widgetrail::pinned::PlacementDirection::Left),
                  "Adjust reopens the same layout and placement transaction");
            Check(layouts.CancelSetup() && !layouts.setupActive() &&
                      layouts.selectedLayoutName() == L"Compact",
                  "B cancels Adjust and restores the selected layout checkpoint");
            const auto restoredDemand = layouts.TakeLayoutSelectionNotifications();
            Check(restoredDemand.size() == 4 &&
                      !restoredDemand[0].selected &&
                      restoredDemand[0].layoutId == L"compact" &&
                      restoredDemand[1].selected &&
                      restoredDemand[1].layoutId == L"details" &&
                      !restoredDemand[2].selected &&
                      restoredDemand[2].layoutId == L"details" &&
                      restoredDemand[3].selected &&
                      restoredDemand[3].layoutId == L"compact",
                  "layout preview and cancellation retain exact demand revocation order");
            RECT canceledSetup{};
            GetWindowRect(layouts.window(), &canceledSetup);
            Check(EqualRect(&committedSetup, &canceledSetup),
                  "B restores the exact pre-Adjust rectangle");
            Check(layouts.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration, Snapshot(2), {}) &&
                      layouts.layoutCount() == 1 && layouts.selectedLayoutIndex() == 0 &&
                      layouts.selectedLayoutName() == L"Full widget",
                  "catalog removal falls back to the host Full widget layout");
            Check(layouts.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "layout fixture performs exact single-surface teardown");
            layouts.Dispose();
            std::error_code layoutCleanup;
            std::filesystem::remove_all(layoutRoot, layoutCleanup);
        }

        {
            widgetrail::pinned::WidgetSurfaceCoordinator compact;
            const auto compactRoot = placementRoot / L"compact-media";
            Check(compact.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x414,
                      d2d.Get(), write.Get(), nullptr, error,
                      compactRoot / L"placement.ini"),
                  "compact media fixture reuses the pinned frame and UIA owner");
            compact.OnOverlayShown();
            auto admission = Admission();
            admission.snapshot = CompactMediaSnapshot();
            Check(compact.Pin(admission, error) && compact.CommitSetup(error) &&
                      compact.ToggleInteractionMode() && compact.EnterControllerFocus(),
                  "compact media fixture establishes one focused native presentation");
            compact.UpdateCompactMediaPlayback(3.0, 20.0, false);
            UpdateWindow(compact.window());
            const auto rewind = compact.CompactMediaSeekTarget(
                widgetrail::input::NavigationDirection::Left);
            const auto forward = compact.CompactMediaSeekTarget(
                widgetrail::input::NavigationDirection::Right);
            Check(rewind && *rewind == 0.0 && forward && *forward == 10.0,
                  "compact LT and RT targets use the declared step and clamp exact playback state");
            compact.UpdateCompactMediaPlayback(18.0, 20.0, true);
            const auto clampedForward = compact.CompactMediaSeekTarget(
                widgetrail::input::NavigationDirection::Right);
            Check(clampedForward && *clampedForward == 20.0,
                  "compact forward seek clamps to the exact current duration");
            const auto beforeNavigation = compact.compactMediaState();
            Check(compact.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Left) &&
                      compact.MoveControllerFocus(
                          widgetrail::input::NavigationDirection::Right) &&
                      compact.MoveControllerFocus(
                          widgetrail::input::NavigationDirection::Up) &&
                      compact.MoveControllerFocus(
                          widgetrail::input::NavigationDirection::Down) &&
                      compact.focusedElementId() == L"host.compact-media.seek" &&
                      compact.compactMediaState().previewPositionSeconds ==
                          beforeNavigation.previewPositionSeconds &&
                      !compact.TakeCompactMediaSeekRequest(),
                  "D-pad and left-stick navigation cannot mutate compact seek state");
            Check(!compact.QueueFocusedInput(
                      L"a", widgetrail::ControllerInputOrigin::PhysicalController) &&
                      compact.TakeInputRequests().empty() &&
                      !compact.TakeCompactMediaSeekRequest(),
                  "compact A has no authored or hidden scrub admission path");
            UpdateWindow(compact.window());
            Check(HasRangeValueWithoutInvoke(
                      compact.window(), L"host:host.compact-media.seek", 18.0, 20.0),
                  "compact seek UIA exposes RangeValue without Invoke");
            Check(compact.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "compact media fixture performs exact teardown");
            compact.Dispose();
            std::error_code compactCleanup;
            std::filesystem::remove_all(compactRoot, compactCleanup);
        }

        {
            widgetrail::pinned::WidgetSurfaceCoordinator scheduler;
            const auto schedulerRoot = placementRoot / L"compact-media-scheduler";
            const HWND schedulerNotification = CreateWindowExW(
                0, L"STATIC", L"Pinned media work notification fixture", 0,
                0, 0, 0, 0, HWND_MESSAGE, nullptr,
                GetModuleHandleW(nullptr), nullptr);
            Check(schedulerNotification != nullptr,
                  "compact media scheduler fixture owns one real host notification target");
            Check(scheduler.Initialize(
                      GetModuleHandleW(nullptr), schedulerNotification, WM_APP + 0x415,
                      d2d.Get(), write.Get(), nullptr, error,
                      schedulerRoot / L"placement.ini"),
                  "compact media scheduler fixture reuses the pinned frame owner");
            scheduler.OnOverlayShown();
            auto admission = Admission();
            admission.snapshot = CompactMediaSnapshot();
            Check(scheduler.Pin(admission, error) && scheduler.CommitSetup(error) &&
                      scheduler.ToggleInteractionMode() &&
                      scheduler.EnterControllerFocus(),
                  "compact media scheduler fixture establishes one native presentation");
            UpdateWindow(scheduler.window());
            const auto settledWork = scheduler.workCounters();
            Check(settledWork.paintMessages > 0 &&
                      settledWork.rasterDraws == settledWork.paintMessages &&
                      settledWork.mediaViewportReconciliations == 1,
                  "initial compact frame reconciles its media viewport exactly once");

            scheduler.UpdateCompactMediaPlayback(3.0, 20.0, false);
            scheduler.UpdateCompactMediaPlayback(4.0, 20.0, true);
            scheduler.UpdateCompactMediaPlayback(5.0, 20.0, true);
            UpdateWindow(scheduler.window());
            const auto afterProgress = scheduler.workCounters();
            Check(afterProgress.snapshots == settledWork.snapshots &&
                      afterProgress.invalidations == settledWork.invalidations &&
                      afterProgress.paintMessages == settledWork.paintMessages &&
                      afterProgress.rasterDraws == settledWork.rasterDraws &&
                      afterProgress.mediaViewportReconciliations ==
                          settledWork.mediaViewportReconciliations &&
                      afterProgress.ownerNotifications ==
                          settledWork.ownerNotifications,
                  "compatible playback observations update native media state without raster or owner amplification");

            Check(scheduler.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      CompactMediaSnapshot(2)) &&
                      scheduler.UpdateSnapshot(
                          admission.widgetId, admission.runtimeGeneration,
                          CompactMediaSnapshot(3)) &&
                      scheduler.UpdateSnapshot(
                          admission.widgetId, admission.runtimeGeneration,
                          CompactMediaSnapshot(4)),
                  "compatible compact snapshot burst is admitted without changing media authority");
            const auto queuedBurst = scheduler.workCounters();
            Check(queuedBurst.snapshots == afterProgress.snapshots + 3 &&
                      queuedBurst.invalidations == afterProgress.invalidations + 3 &&
                      queuedBurst.coalescedInvalidations >=
                          afterProgress.coalescedInvalidations + 2 &&
                      queuedBurst.paintMessages == afterProgress.paintMessages,
                  "compatible snapshot burst retains one pending Win32 paint instead of multiplying raster work");
            UpdateWindow(scheduler.window());
            const auto afterBurst = scheduler.workCounters();
            Check(afterBurst.paintMessages == afterProgress.paintMessages + 1 &&
                      afterBurst.rasterDraws == afterProgress.rasterDraws + 1 &&
                      afterBurst.mediaViewportReconciliations ==
                          afterProgress.mediaViewportReconciliations &&
                      afterBurst.ownerNotifications ==
                          afterProgress.ownerNotifications &&
                      scheduler.compactMediaState().positionSeconds == 5.0,
                  "one coalesced paint presents the newest compatible snapshot without media geometry churn");

            auto replacement = CompactMediaSnapshot(5);
            replacement.embeddedMedia->aspectRatio = 4.0 / 3.0;
            Check(scheduler.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      replacement),
                  "genuine compact media geometry successor is admitted");
            UpdateWindow(scheduler.window());
            const auto afterGeometry = scheduler.workCounters();
            Check(afterGeometry.mediaViewportReconciliations ==
                          afterBurst.mediaViewportReconciliations + 1 &&
                      afterGeometry.ownerNotifications ==
                          afterBurst.ownerNotifications + 1,
                  "genuine media geometry replacement reconciles and notifies its owner exactly once");
            Check(scheduler.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "compact media scheduler fixture performs exact teardown");
            scheduler.Dispose();
            DestroyWindow(schedulerNotification);
            std::error_code schedulerCleanup;
            std::filesystem::remove_all(schedulerRoot, schedulerCleanup);
        }

        {
            widgetrail::pinned::WidgetSurfaceCoordinator scrolling;
            const auto scrollRoot = placementRoot / L"scroll-retention";
            Check(scrolling.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x413,
                      d2d.Get(), write.Get(), nullptr, error,
                      scrollRoot / L"placement.ini"),
                  "scroll fixture reuses the production pinned coordinator");
            scrolling.OnOverlayShown();
            auto admission = Admission();
            admission.snapshot = ScrollSnapshot();
            admission.pinnedLayouts = {{
                L"scroll-layout", L"Scrollable", 420.0F, 320.0F,
                ScrollSnapshot(),
            }};
            Check(scrolling.Pin(admission, error) && scrolling.CycleLayout(1) &&
                      scrolling.CommitSetup(error),
                  "scroll fixture commits one authored selected layout");
            Check(scrolling.ToggleInteractionMode(),
                  "scroll fixture enters the existing interactive surface mode");
            UpdateWindow(scrolling.window());
            Check(scrolling.EnterControllerFocus(),
                  "scroll fixture establishes exact pinned controller focus");
            UpdateWindow(scrolling.window());
            Check(scrolling.ScrollFocusedProjection(0, -32'768, 100),
                  "right stick commits one renderer-owned free-scroll offset");
            UpdateWindow(scrolling.window());
            const auto scrolledOffset = scrolling.ScrollOffsetForTesting(L"pin.scroll");
            Check(scrolledOffset && *scrolledOffset > 0.0F &&
                      scrolling.FreeScrollBindingForTesting(),
                  "free scroll retains exact renderer offset and interaction binding");
            Check(scrolling.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Right) &&
                      !scrolling.FreeScrollBindingForTesting() &&
                      scrolling.focusedElementId() == L"pin.outside",
                  "the first direction retires re-entry and leaves the visible list");
            UpdateWindow(scrolling.window());

            auto compatibleProjection = ScrollSnapshot(2);
            Check(scrolling.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      ScrollSnapshot(2), {{
                          L"scroll-layout", L"Scrollable", 420.0F, 320.0F,
                          compatibleProjection,
                      }}),
                  "ordinary compatible pinned snapshot is admitted after re-entry");
            UpdateWindow(scrolling.window());
            const auto retainedOffset = scrolling.ScrollOffsetForTesting(L"pin.scroll");
            Check(retainedOffset && *retainedOffset == *scrolledOffset &&
                      !scrolling.FreeScrollBindingForTesting(),
                  "compatible snapshot retains renderer state independently of binding lifetime");

            Check(scrolling.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      ScrollSnapshot(3), {}) &&
                      scrolling.selectedLayoutName() == L"Full widget",
                  "selected-layout removal falls back through coordinator authority");
            UpdateWindow(scrolling.window());
            const auto replacedOffset = scrolling.ScrollOffsetForTesting(L"pin.scroll");
            Check(replacedOffset && *replacedOffset == 0.0F,
                  "genuine selected-layout replacement forgets incompatible renderer state");
            Check(scrolling.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "scroll fixture performs exact teardown");
            scrolling.Dispose();
            std::error_code scrollCleanup;
            std::filesystem::remove_all(scrollRoot, scrollCleanup);
        }

        {
            widgetrail::pinned::WidgetSurfaceCoordinator pagination;
            const auto paginationRoot = placementRoot / L"scroll-pagination";
            Check(pagination.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x414,
                      d2d.Get(), write.Get(), nullptr, error,
                      paginationRoot / L"placement.ini"),
                  "pinned pagination fixture initializes through the production coordinator");
            pagination.OnOverlayShown();
            auto initial = ScrollSnapshot(1, true, 8);
            auto admission = Admission();
            admission.snapshot = initial;
            admission.pinnedLayouts = {{
                L"paged-layout", L"Paged", 420.0F, 320.0F, initial,
            }};
            Check(pagination.Pin(admission, error) && pagination.CycleLayout(1) &&
                      pagination.CommitSetup(error) &&
                      pagination.ToggleInteractionMode(),
                  "pinned pagination fixture admits one exact authored layout");
            UpdateWindow(pagination.window());
            Check(pagination.EnterControllerFocus(),
                  "pinned pagination fixture owns controller focus");
            UpdateWindow(pagination.window());
            for (int index = 1; index < 8; ++index) {
                Check(pagination.MoveControllerFocus(
                          widgetrail::input::NavigationDirection::Down),
                      "pinned focus advances through the current scroll page");
                UpdateWindow(pagination.window());
            }
            Check(pagination.focusedElementId() == L"pin.scroll.item.7" &&
                      pagination.MoveControllerFocus(
                          widgetrail::input::NavigationDirection::Down) &&
                      pagination.focusedElementId() == L"pin.scroll.item.7",
                  "pinned focus retains the exact last row while pagination is admitted");
            auto pending = pagination.TakePaginationRequests(1000);
            Check(pending.requests.size() == 1 &&
                      pending.requests.front().selectedLayoutId ==
                          L"paged-layout" &&
                      pending.requests.front().request.action.scrollId ==
                          L"pin.scroll" &&
                      pending.requests.front().request.action.actionId ==
                          L"pin.scroll.next" &&
                      pagination.IsCurrentPaginationRequest(
                          pending.requests.front()),
                  "pinned pagination surfaces one exact layout/runtime/action request");
            pagination.CompletePaginationRequest({
                pending.requests.front().request,
                widgetrail::input::ScrollPaginationDispatchDisposition::Admitted,
                {},
                1001,
            });

            auto successor = ScrollSnapshot(2, true, 9);
            Check(pagination.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      successor, {{
                          L"paged-layout", L"Paged", 420.0F, 320.0F,
                          successor,
                      }}),
                  "authoritative pinned successor admits the requested row");
            UpdateWindow(pagination.window());
            Check(pagination.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Down) &&
                      pagination.focusedElementId() == L"pin.scroll.item.8",
                  "the next pinned direction enters the newly admitted row");

            auto finite = ScrollSnapshot(3, false, 9);
            Check(pagination.UpdateSnapshot(
                      admission.widgetId, admission.runtimeGeneration,
                      finite, {{
                          L"paged-layout", L"Paged", 420.0F, 320.0F,
                          finite,
                      }}),
                  "finite pinned successor removes only pagination authority");
            UpdateWindow(pagination.window());
            Check(pagination.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Down) &&
                      pagination.focusedElementId() == L"pin.outside",
                  "a finite pinned boundary permits the external focus move");
            Check(pagination.Unpin(
                      widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "pinned pagination fixture performs exact teardown");
            pagination.Dispose();
            std::error_code paginationCleanup;
            std::filesystem::remove_all(paginationRoot, paginationCleanup);
        }

        {
            const auto blockedParent = placementRoot / L"blocked-placement-parent";
            std::filesystem::create_directories(placementRoot);
            std::ofstream blocker(blockedParent, std::ios::trunc);
            Check(static_cast<bool>(blocker),
                  "failed-store fixture creates one deterministic parent-file blocker");
            blocker.close();
            widgetrail::pinned::WidgetSurfaceCoordinator failedStore;
            Check(failedStore.Initialize(
                      GetModuleHandleW(nullptr), nullptr, WM_APP + 0x412,
                      d2d.Get(), write.Get(), nullptr, error,
                      blockedParent / L"placement.ini"),
                  "failed-store coordinator reuses the production placement owner");
            failedStore.OnOverlayShown();
            Check(failedStore.Pin(Admission(), error),
                  "failed-store fixture enters the ordinary new-pin setup");
            RECT failedOriginal{};
            GetWindowRect(failedStore.window(), &failedOriginal);
            Check(failedStore.StepPlacement(
                      widgetrail::pinned::PlacementMode::Resize,
                      widgetrail::pinned::PlacementDirection::Right),
                  "failed-store fixture produces one legal preview");
            RECT failedPreview{};
            GetWindowRect(failedStore.window(), &failedPreview);
            Check(!EqualRect(&failedOriginal, &failedPreview),
                  "failed-store preview visibly differs from its captured original");
            error.clear();
            Check(!failedStore.CommitPlacement(error) &&
                      error == L"Pinned placement directory is unavailable." &&
                      failedStore.placementMode() ==
                          widgetrail::pinned::PlacementMode::None,
                  "failed persistence reports its precise error and closes placement");
            RECT failedRestored{};
            GetWindowRect(failedStore.window(), &failedRestored);
            Check(EqualRect(&failedOriginal, &failedRestored) &&
                      !std::filesystem::exists(blockedParent / L"placement.ini"),
                  "failed persistence restores exact original bounds without claiming a save");
            Check(failedStore.Unpin(
                      widgetrail::pinned::WidgetSurfaceStopReason::Unpin),
                  "failed-store fixture performs exact teardown");
            failedStore.Dispose();
            std::error_code blockerCleanup;
            std::filesystem::remove(blockedParent, blockerCleanup);
        }

        const auto privateBefore = PrivateWorkingSetBytes();
        if (!coordinator.Pin(Admission(), error)) {
            std::wcerr << L"pin error: " << error << L'\n';
            Check(false, "current supporting widget creates one host-owned surface");
        }
        const HWND surface = coordinator.window();
        Check(surface && IsWindow(surface) && IsWindowVisible(surface),
              "real pinned HWND is visible");
        Check(coordinator.setupActive() && coordinator.controllerFocused() &&
                  coordinator.selectedLayoutName() == L"Full widget",
              "new pin immediately focuses host-owned Full widget setup");
        Check(coordinator.CommitSetup(error),
              "A-equivalent setup commit returns the new pin to click-through");
        Check(coordinator.presentationState() ==
                  widgetrail::pinned::WidgetSurfacePresentationState::PinnedClickThrough,
              "new pin has typed nonactivating presentation state");
        Check(widgetrail::pinned::WidgetSurfacePresentationStateValue(
                  coordinator.presentationState()) == L"pinnedClickThrough",
              "typed click-through state has a closed protocol value");
        const auto styles = static_cast<DWORD>(GetWindowLongPtrW(surface, GWL_EXSTYLE));
        Check((styles & (WS_EX_TOOLWINDOW | WS_EX_TOPMOST)) ==
                  (WS_EX_TOOLWINDOW | WS_EX_TOPMOST) &&
              (styles & WS_EX_APPWINDOW) == 0,
              "real pinned HWND uses the accepted tool-window/topmost contract");
        RECT originalBounds{};
        GetWindowRect(surface, &originalBounds);
        Check(coordinator.BeginPlacement(widgetrail::pinned::PlacementMode::Move) &&
                  coordinator.StepPlacement(widgetrail::pinned::PlacementDirection::Left) &&
                  coordinator.CancelPlacement(),
              "controller move and cancel share one bounded placement session");
        RECT canceledBounds{};
        GetWindowRect(surface, &canceledBounds);
        Check(EqualRect(&originalBounds, &canceledBounds),
              "cancel restores the exact pre-gesture real-HWND rectangle");
        Check(coordinator.BeginPlacement(widgetrail::pinned::PlacementMode::Resize) &&
                  coordinator.StepPlacement(widgetrail::pinned::PlacementDirection::Left),
              "controller resize changes the real HWND through the placement state machine");
        RECT previewBounds{};
        GetWindowRect(surface, &previewBounds);
        const UINT committedDpi = std::max(1U, GetDpiForWindow(surface));
        const int placementStepPixels = MulDiv(
            static_cast<int>(
                widgetrail::surface_geometry::kPlacementAdjustmentStepDip),
            static_cast<int>(committedDpi), 96);
        Check((originalBounds.right - originalBounds.left) -
                  (previewBounds.right - previewBounds.left) == placementStepPixels,
              "coordinator resize consumes the centralized 32-DIP session step");
        Check(coordinator.CommitPlacement(error),
              "current generation atomically commits real-HWND geometry");
        RECT committedBounds{};
        GetWindowRect(surface, &committedBounds);
        Check(EqualRect(&previewBounds, &committedBounds),
              "successful commit retains the exact constrained preview bounds");
        widgetrail::pinned::PinnedPlacementStore committedStore(
            placementRoot / L"placement.ini");
        const auto durableCommitted = committedStore.Load(coordinator.widgetId());
        Check(durableCommitted &&
                  static_cast<int>(std::lround(
                      durableCommitted->widthDip * committedDpi / 96.0F)) ==
                      committedBounds.right - committedBounds.left &&
                  static_cast<int>(std::lround(
                      durableCommitted->heightDip * committedDpi / 96.0F)) ==
                      committedBounds.bottom - committedBounds.top,
              "successful commit persists exact logical bounds for durable reload");
        const auto originalOpacity = coordinator.opacityPercent();
        Check(coordinator.BeginOpacityAdjustment() &&
                  coordinator.StepOpacity(widgetrail::pinned::PlacementDirection::Left) &&
                  coordinator.opacityPercent() != originalOpacity &&
                  coordinator.CancelOpacity() &&
                  coordinator.opacityPercent() == originalOpacity,
              "B-equivalent opacity cancellation restores the exact prior alpha");
        Check(!coordinator.Pin(Admission(), error) &&
                  error.find(L"already pinned") != std::wstring::npos,
              "duplicate pin is bounded");
        auto other = Admission();
        other.widgetId = L"widgetrail.samples.other";
        Check(!coordinator.Pin(std::move(other), error) &&
                  error.find(L"Only one") != std::wstring::npos,
              "simultaneous pin cap is enforced");

        Check(coordinator.SetInteractionMode(widgetrail::pinned::InteractionMode::ClickThrough),
              "fixture returns to closed click-through mode after placement setup");
        RECT clickThroughBounds{};
        GetWindowRect(surface, &clickThroughBounds);
        Check(EqualRect(&committedBounds, &clickThroughBounds),
              "click-through transition retains committed placement bounds");
        UpdateWindow(surface);
        const auto initialClickThroughPaint = coordinator.PaintTraceForTesting();
        Check(initialClickThroughPaint.snapshotSequence == 1 &&
                  coordinator.interactionMode() ==
                      widgetrail::pinned::InteractionMode::ClickThrough &&
                  initialClickThroughPaint.contentPresentation ==
                      widgetrail::pinned::ContentPresentation::AdmittedWidget &&
                  initialClickThroughPaint.declarativeRenderSucceeded &&
                  initialClickThroughPaint.admittedContentPresented &&
                  initialClickThroughPaint.navigationNodeCount >= 2,
              "click-through production paint retains admitted sentinel content without a placeholder");
        Check(!FindAutomationId(surface, L"host:pinned.move") &&
                  !FindAutomationId(surface, L"widget:pin.fixture.action"),
              "click-through UIA tree exposes no hidden interactive controls");
        IRawElementProviderSimple* root{};
        Check(SUCCEEDED(coordinator.window()
                  ? UiaHostProviderFromHwnd(coordinator.window(), &root)
                  : E_FAIL) && root,
              "real pinned HWND has a UI Automation host provider");
        root->Release();
        Check(!coordinator.UpdateSnapshot(
                  coordinator.widgetId(), L"stale-runtime", Snapshot(2)),
              "stale runtime cannot replace pinned content");
        Check(coordinator.UpdateSnapshot(
                  coordinator.widgetId(), coordinator.runtimeGeneration(), Snapshot(2)),
              "current runtime updates the declarative surface");
        RECT refreshedBounds{};
        GetWindowRect(surface, &refreshedBounds);
        Check(EqualRect(&committedBounds, &refreshedBounds),
              "ordinary snapshot publication retains committed placement bounds");

        Check(coordinator.ToggleInteractionMode(),
              "visible overlay can explicitly make the pin interactive");
        RECT interactiveBounds{};
        GetWindowRect(surface, &interactiveBounds);
        Check(EqualRect(&committedBounds, &interactiveBounds),
              "interactive focus transition retains committed placement bounds");
        coordinator.OnOverlayHidden();
        Check(coordinator.pinned() && IsWindowVisible(surface),
              "main overlay hide preserves the visible pinned HWND");
        Check(coordinator.presentationState() ==
                  widgetrail::pinned::WidgetSurfacePresentationState::PinnedClickThrough,
              "overlay hide releases interaction into typed click-through state");
        const auto clickThrough = static_cast<DWORD>(
            GetWindowLongPtrW(surface, GWL_EXSTYLE));
        Check((clickThrough & (WS_EX_NOACTIVATE | WS_EX_TRANSPARENT)) ==
                  (WS_EX_NOACTIVATE | WS_EX_TRANSPARENT),
              "hidden-overlay pin is nonactivating and click-through");
        Check(SendMessageW(surface, WM_NCHITTEST, 0, 0) == HTTRANSPARENT,
              "click-through pin rejects hit-test ownership");
        Check(coordinator.UpdateSnapshot(
                  coordinator.widgetId(), coordinator.runtimeGeneration(), Snapshot(3)),
              "hidden click-through pin admits a current replacement snapshot");
        UpdateWindow(surface);
        const auto hiddenReplacementPaint = coordinator.PaintTraceForTesting();
        Check(hiddenReplacementPaint.snapshotSequence == 3 &&
                  coordinator.interactionMode() ==
                      widgetrail::pinned::InteractionMode::ClickThrough &&
                  hiddenReplacementPaint.contentPresentation ==
                      widgetrail::pinned::ContentPresentation::AdmittedWidget &&
                  hiddenReplacementPaint.declarativeRenderSucceeded &&
                  hiddenReplacementPaint.admittedContentPresented &&
                  hiddenReplacementPaint.navigationNodeCount >= 2,
              "hidden-overlay repaint presents the current sentinel content without reopening");
        SendMessageW(surface, WM_LBUTTONDOWN, MK_LBUTTON, MAKELPARAM(40, 100));
        SendMessageW(surface, WM_LBUTTONUP, 0, MAKELPARAM(40, 100));
        Check(!coordinator.controllerFocused() &&
                  coordinator.TakeInputRequests().empty() &&
                  !FindAutomationId(surface, L"widget:pin.fixture.action") &&
                  !FindAutomationId(surface, L"host:pinned.close"),
              "hidden click-through content retains no focus, input, or actionable UIA authority");
        coordinator.OnOverlayShown();
        Check(coordinator.ToggleInteractionMode() &&
                  coordinator.presentationState() ==
                      widgetrail::pinned::WidgetSurfacePresentationState::PinnedInteractive,
              "reopened overlay can explicitly restore one interactive pin");
        UpdateWindow(surface);
        Check(coordinator.EnterControllerFocus() && coordinator.controllerFocused() &&
                  GetFocus() == surface,
              "one explicit host transition gives controller focus to the pinned HWND");
        RECT focusedBounds{};
        GetWindowRect(surface, &focusedBounds);
        Check(EqualRect(&committedBounds, &focusedBounds),
              "controller focus acquisition retains committed placement bounds");
        UpdateWindow(surface);
        Check(FindAutomationId(surface, L"widget:pin.fixture.action") &&
                  FindAutomationId(surface, L"host:pinned.close") &&
                  FindAutomationId(surface, L"host:pinned.emergency"),
              "interactive UIA composes widget content with Close and emergency host actions");
        Check(AutomationValueContains(surface, L"host:pinned.mode", L"B is widget Back") &&
                  AutomationValueContains(surface, L"host:pinned.mode", L"View returns to the tray"),
              "pinned accessibility semantics distinguish widget B from host View");
        Check(coordinator.MoveControllerFocus(
                  widgetrail::input::NavigationDirection::Right) &&
                  coordinator.focusedElementId() == L"pin.fixture.second" &&
                  coordinator.MoveControllerFocus(
                      widgetrail::input::NavigationDirection::Left) &&
                  coordinator.focusedElementId() == L"pin.fixture.action",
              "controller focus uses the shared authored and geometric navigation owner");
        Check(coordinator.QueueFocusedInput(
                  L"a", widgetrail::ControllerInputOrigin::PhysicalController),
              "focused controller activation enters the bounded pinned input queue");
        auto controllerInputs = coordinator.TakeInputRequests();
        Check(controllerInputs.size() == 1 &&
                  controllerInputs[0].widgetId == coordinator.widgetId() &&
                  controllerInputs[0].runtimeGeneration == coordinator.runtimeGeneration() &&
                  controllerInputs[0].snapshotSequence == 3 &&
                  controllerInputs[0].nodeId == L"pin.fixture.action" &&
                  controllerInputs[0].protocolButton == L"a" &&
                  controllerInputs[0].origin ==
                      widgetrail::ControllerInputOrigin::PhysicalController,
              "queued pinned controller input retains exact current generation and focus");
        coordinator.SetActionFeedback(
            L"Pinned action failed. Reopen the overlay and try again.", true);
        Check(IsAssertiveLiveRegion(surface, L"host:pinned.feedback"),
              "sanitized pinned action failure is an assertive UIA live status");
        coordinator.SetActionFeedback({}, false);

        const auto widgetAction = FindAutomationId(
            surface, L"widget:pin.fixture.action");
        RECT widgetBounds{};
        Check(widgetAction &&
                  SUCCEEDED(widgetAction->get_CurrentBoundingRectangle(&widgetBounds)),
              "pinned widget action exposes real on-screen UIA bounds");
        const auto widgetPoint = coordinator.PointerPointForTesting(
            L"pin.fixture.action");
        Check(widgetPoint.has_value(),
              "pinned widget action retains one production pointer hit region");
        SendMessageW(surface, WM_LBUTTONDOWN, MK_LBUTTON,
                     MAKELPARAM(widgetPoint->x, widgetPoint->y));
        ReleaseCapture();
        Check(coordinator.TakeInputRequests().empty(),
              "pointer capture loss cancels pinned activation without forwarding input");
        SendMessageW(surface, WM_LBUTTONDOWN, MK_LBUTTON,
                     MAKELPARAM(widgetPoint->x, widgetPoint->y));
        SendMessageW(surface, WM_LBUTTONUP, 0,
                     MAKELPARAM(widgetPoint->x, widgetPoint->y));
        auto pointerInputs = coordinator.TakeInputRequests();
        Check(pointerInputs.size() == 1 &&
                  pointerInputs[0].nodeId == L"pin.fixture.action",
              "pointer activation shares exact pinned focus and input admission");

        Check(InvokeAutomationId(surface, L"widget:pin.fixture.action"),
              "real UI Automation invokes the composed pinned widget action");
        auto automationInputs = coordinator.TakeInputRequests();
        Check(automationInputs.size() == 1 &&
                  automationInputs[0].origin ==
                      widgetrail::ControllerInputOrigin::AccessibilityAutomation &&
                  automationInputs[0].nodeId == L"pin.fixture.action",
              "UI Automation action uses the same current bounded input queue");
        Check(ActionBoundsInsideWindow(surface, L"host:pinned.move") &&
                  ActionBoundsInsideWindow(surface, L"host:pinned.resize"),
              "Move and Resize UIA action bounds remain inside the pinned HWND");
        Check(InvokeAutomationId(surface, L"host:pinned.move") &&
                  coordinator.placementMode() == widgetrail::pinned::PlacementMode::Move,
              "real UI Automation Move action enters the shared placement state machine");
        Check(ActionBoundsInsideWindow(surface, L"host:pinned.commit") &&
                  ActionBoundsInsideWindow(surface, L"host:pinned.cancel"),
              "Commit and Cancel UIA action bounds remain inside the pinned HWND");
        Check(coordinator.CancelPlacement(),
              "UI Automation placement can be canceled through the same authority");

        Check(coordinator.BeginPlacement(widgetrail::pinned::PlacementMode::Resize),
              "minimum-size UIA fixture enters the production resize state machine");
        for (int step = 0; step < 40; ++step) {
            (void)coordinator.StepPlacement(widgetrail::pinned::PlacementDirection::Left);
            (void)coordinator.StepPlacement(widgetrail::pinned::PlacementDirection::Up);
        }
        Check(coordinator.CommitPlacement(error),
              "minimum-size real-HWND placement commits through the production store");
        UpdateWindow(surface);
        RECT minimumBounds{};
        GetWindowRect(surface, &minimumBounds);
        const UINT minimumDpi = std::max(1U, GetDpiForWindow(surface));
        Check(minimumBounds.right - minimumBounds.left >= MulDiv(240, minimumDpi, 96) &&
                  minimumBounds.bottom - minimumBounds.top >= MulDiv(135, minimumDpi, 96),
              "real pinned HWND remains at or above the injected minimum size");
        Check(ActionBoundsInsideWindow(surface, L"host:pinned.move") &&
                  ActionBoundsInsideWindow(surface, L"host:pinned.resize"),
              "Move and Resize UIA bounds fit the minimum-size surface");
        RECT moveBounds{};
        RECT resizeBounds{};
        Check(ReadNamedButtonBounds(surface, L"host:pinned.move", L"Move pinned surface",
                                    moveBounds) &&
                  ReadNamedButtonBounds(surface, L"host:pinned.resize",
                                        L"Resize pinned surface",
                                        resizeBounds) &&
                  moveBounds.left < resizeBounds.left,
              "minimum-size UIA publishes ordered named Move and Resize buttons");
        Check(InvokeAutomationId(surface, L"host:pinned.move"),
              "minimum-size surface exposes the Move action");
        Check(ActionBoundsInsideWindow(surface, L"host:pinned.commit") &&
                  ActionBoundsInsideWindow(surface, L"host:pinned.cancel"),
              "Commit and Cancel UIA bounds fit the minimum-size surface");
        RECT commitBounds{};
        RECT cancelBounds{};
        Check(ReadNamedButtonBounds(surface, L"host:pinned.commit", L"Commit placement",
                                    commitBounds) &&
                  ReadNamedButtonBounds(surface, L"host:pinned.cancel", L"Cancel placement",
                                        cancelBounds) &&
                  commitBounds.left < cancelBounds.left,
              "minimum-size UIA publishes ordered named Commit and Cancel buttons");
        Check(coordinator.CancelPlacement(),
              "minimum-size placement cancellation remains exact");

        const HMONITOR currentMonitor =
            MonitorFromWindow(surface, MONITOR_DEFAULTTONEAREST);
        MONITORINFOEXW currentInfo{sizeof(currentInfo)};
        Check(currentMonitor && GetMonitorInfoW(currentMonitor, &currentInfo),
              "real HWND monitor metadata is available for reconciliation");
        coordinator.ReconcileDisplayEnvironmentForTesting({{
            L"fixture-replacement-monitor",
            {currentInfo.rcWork.left, currentInfo.rcWork.top,
             currentInfo.rcWork.right, currentInfo.rcWork.bottom},
            minimumDpi,
            true,
        }});
        RECT reconciledBounds{};
        GetWindowRect(surface, &reconciledBounds);
        Check(reconciledBounds.left >= currentInfo.rcWork.left &&
                  reconciledBounds.top >= currentInfo.rcWork.top &&
                  reconciledBounds.right <= currentInfo.rcWork.right &&
                  reconciledBounds.bottom <= currentInfo.rcWork.bottom,
              "coordinator monitor-loss reconciliation keeps the real HWND fully on-screen");
        Check(coordinator.controllerFocused() && GetFocus() == surface &&
                  !coordinator.focusedElementId().empty(),
              "display reconciliation retains one deterministic valid pinned focus owner");
        Check(ActionBoundsInsideWindow(surface, L"host:pinned.move") &&
                  ActionBoundsInsideWindow(surface, L"host:pinned.resize"),
              "reconciled minimum surface retains bounded host UIA actions");

        auto unrelatedDescriptor = Descriptor();
        unrelatedDescriptor.id = L"widgetrail.tests.unrelated-peer";
        unrelatedDescriptor.name = L"Unrelated peer";
        unrelatedDescriptor.instanceId = L"unrelated.instance";
        unrelatedDescriptor.runtimeGeneration = L"unrelated-runtime";
        unrelatedDescriptor.presentationGeneration = L"unrelated-presentation";
        const HWND catalogPeerSurface = coordinator.window();
        const auto catalogPeerTeardownCount = coordinator.teardownCount();
        coordinator.ReconcileCatalog({Descriptor(), unrelatedDescriptor});
        Check(coordinator.pinned() && coordinator.window() == catalogPeerSurface &&
                  coordinator.teardownCount() == catalogPeerTeardownCount,
              "catalog admission of an unrelated peer leaves the current pin untouched");
        coordinator.ReconcileCatalog({Descriptor()});
        Check(coordinator.pinned() && coordinator.window() == catalogPeerSurface &&
                  coordinator.teardownCount() == catalogPeerTeardownCount,
              "removing an unrelated catalog peer preserves the current pin and HWND");
        coordinator.ReconcileCatalog({Descriptor(L"runtime-2")});
        Check(!coordinator.pinned() && coordinator.teardownCount() == 1 &&
                  !coordinator.controllerFocused() &&
                  coordinator.lastStopReason() ==
                      widgetrail::pinned::WidgetSurfaceStopReason::RuntimeReplaced,
              "runtime replacement tears down exactly once");
        Check(!IsWindow(surface), "replaced runtime leaves no orphaned HWND");

        Check(coordinator.Pin(Admission(), error), "surface can be repinned");
        RECT restoredBounds{};
        GetWindowRect(coordinator.window(), &restoredBounds);
        Check(restoredBounds.right - restoredBounds.left ==
                  reconciledBounds.right - reconciledBounds.left &&
                  restoredBounds.bottom - restoredBounds.top ==
                      reconciledBounds.bottom - reconciledBounds.top,
              "repin restores committed logical size from durable storage");
        coordinator.ReconcileCatalog({});
        Check(!coordinator.pinned() && coordinator.teardownCount() == 2 &&
                  coordinator.lastStopReason() ==
                      widgetrail::pinned::WidgetSurfaceStopReason::WidgetRemoved,
              "catalog removal tears down exactly once");

        Check(coordinator.Pin(Admission(), error), "surface can be pinned for worker loss");
        const HWND failedWorkerSurface = coordinator.window();
        Check(coordinator.Unpin(
                  widgetrail::pinned::WidgetSurfaceStopReason::WorkerUnavailable) &&
                  coordinator.teardownCount() == 3 &&
                  coordinator.lastStopReason() ==
                      widgetrail::pinned::WidgetSurfaceStopReason::WorkerUnavailable,
              "worker loss tears down exactly once with its primary reason");
        Check(!IsWindow(failedWorkerSurface), "worker loss leaves no orphaned HWND");

        Check(coordinator.Pin(Admission(), error),
              "surface can be pinned for UI Automation Close");
        coordinator.OnOverlayShown();
        Check(coordinator.CommitSetup(error),
              "Close fixture commits the new-pin setup transaction");
        Check(coordinator.ToggleInteractionMode(),
              "Close fixture explicitly enters Interactive mode");
        const HWND closeSurface = coordinator.window();
        UpdateWindow(closeSurface);
        Check(InvokeAutomationId(closeSurface, L"host:pinned.close") &&
                  !coordinator.pinned() && !IsWindow(closeSurface) &&
                  coordinator.teardownCount() == 4 &&
                  coordinator.lastStopReason() ==
                      widgetrail::pinned::WidgetSurfaceStopReason::Close,
              "real UI Automation Close performs exact paired teardown");

        Check(coordinator.Pin(Admission(), error),
              "surface can be pinned for emergency hide");
        coordinator.OnOverlayShown();
        Check(coordinator.CommitSetup(error),
              "emergency fixture commits the new-pin setup transaction");
        Check(coordinator.ToggleInteractionMode(),
              "emergency fixture explicitly enters Interactive mode");
        const HWND emergencySurface = coordinator.window();
        UpdateWindow(emergencySurface);
        Check(InvokeAutomationId(emergencySurface, L"host:pinned.emergency") &&
                  !coordinator.pinned() && !IsWindow(emergencySurface) &&
                  coordinator.teardownCount() == 5 &&
                  coordinator.lastStopReason() ==
                      widgetrail::pinned::WidgetSurfaceStopReason::EmergencyHide,
              "accessible host emergency action removes every bounded pin and interaction");

        Check(coordinator.Pin(Admission(), error), "surface can be pinned for host exit");
        const auto privatePinned = PrivateWorkingSetBytes();
        std::this_thread::sleep_for(std::chrono::milliseconds(750));
        Check(coordinator.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::HostExit),
              "host exit performs terminal teardown");
        Check(!coordinator.Unpin(widgetrail::pinned::WidgetSurfaceStopReason::HostExit) &&
                  coordinator.teardownCount() == 6,
              "terminal teardown is idempotent");
        coordinator.Dispose();
        coordinator.Dispose();
        Check(coordinator.teardownCount() == 6,
              "coordinator disposal after cleanup owns no second teardown");
        std::error_code cleanup;
        std::filesystem::remove_all(placementRoot, cleanup);
        const auto privateDelta = static_cast<long long>(privatePinned) -
            static_cast<long long>(privateBefore);
        Check(privateDelta <= 128LL * 1024LL * 1024LL,
              "incremental pinned private working set stays below DLV-016 material gate");

        std::cout << "WidgetSurfaceCoordinatorTests passed (" << checks
                  << " checks, pinned private working-set delta "
                  << privateDelta << " bytes)\n";
        CoUninitialize();
        return 0;
    } catch (const std::exception& exception) {
        std::cerr << "WidgetSurfaceCoordinatorTests failed after " << checks
                  << " checks: " << exception.what() << '\n';
        if (SUCCEEDED(apartment)) CoUninitialize();
        return 1;
    }
}
