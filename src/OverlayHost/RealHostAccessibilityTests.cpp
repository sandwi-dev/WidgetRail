#include "AccessibilityProvider.h"
#include "AccessibilityTree.h"
#include "HostAccessibility.h"
#include "TrayLayout.h"

#include <Windows.h>
#include <ole2.h>
#include <UIAutomation.h>
#include <wrl/client.h>

#include <algorithm>
#include <filesystem>
#include <fstream>
#include <future>
#include <iostream>
#include <map>
#include <set>
#include <stdexcept>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

using Microsoft::WRL::ComPtr;
namespace fs = std::filesystem;

namespace {

int checks{};

void Check(const bool condition, const std::string_view message) {
    ++checks;
    if (!condition) throw std::runtime_error(std::string(message));
}

struct Profile final {
    std::string name;
    float width{};
    float height{};
    double scale{};
};

struct Fixture final {
    std::wstring widgetId;
    std::wstring focusedId;
    gba::accessibility::Tree tree;
    std::vector<std::wstring> expectedOrder;
};

gba::WidgetNode SemanticNode(
    const wchar_t* id,
    const wchar_t* kind,
    const wchar_t* name,
    const gba::declarative::Rect bounds,
    gba::RenderResult& render) {
    gba::WidgetNode node;
    node.id = id;
    node.kind = kind;
    node.accessibilityLabel = name;
    render.accessibilityRegions.push_back({node.id, bounds});
    return node;
}

Fixture ComposeFixture(
    const std::wstring_view widgetId,
    const std::wstring_view title,
    const Profile& profile,
    std::vector<gba::WidgetNode> nodes,
    gba::RenderResult render,
    const std::wstring_view focusedId,
    const long long sequence) {
    gba::WidgetSnapshot snapshot;
    snapshot.instanceId = std::wstring(widgetId) + L".accessibility";
    snapshot.sequence = sequence;
    snapshot.activeInputScopeId = std::wstring(widgetId) + L".root";
    snapshot.root.id = std::wstring(widgetId) + L".root";
    snapshot.root.kind = L"stack";
    snapshot.root.inputScopeId = snapshot.activeInputScopeId;
    snapshot.root.children = std::move(nodes);

    std::vector<std::wstring> expected;
    for (const auto& node : snapshot.root.children) {
        if (std::ranges::any_of(render.accessibilityRegions, [&](const auto& region) {
                return region.nodeId == node.id;
            })) expected.push_back(node.id);
    }
    auto widgetTree = gba::accessibility::BuildWidgetTree(
        std::wstring(widgetId), std::wstring(widgetId) + L"-generation",
        snapshot, render, focusedId);
    const std::vector<gba::accessibility::TrayItem> trayItems{
        {std::wstring(widgetId), std::wstring(title)},
    };
    const auto tray = gba::shell::ComputeTrayLayout(
        profile.width, profile.height, trayItems.size(), 0);
    Check(tray.has_value(), "named fixture has a valid host tray layout");
    const gba::accessibility::OpenWidgetSemantics open{
        std::wstring(title), gba::accessibility::HostAction::BackToTray,
        snapshot.activeInputScopeId,
        {profile.width - 196.0F, profile.height - 80.0F, 80.0F, 32.0F},
        {profile.width - 108.0F, profile.height - 80.0F, 88.0F, 32.0F},
        L"A Select  B Back", {20.0F, profile.height - 80.0F, 260.0F, 32.0F},
        L"", {20.0F, profile.height - 80.0F, 260.0F, 32.0F},
    };
    return {
        std::wstring(widgetId), std::wstring(focusedId),
        gba::accessibility::BuildOpenWidgetTree(
            std::move(widgetTree), trayItems, *tray, 0, false, open),
        std::move(expected),
    };
}

Fixture SettingsFixture(const Profile& profile, const bool error) {
    gba::RenderResult render;
    std::vector<gba::WidgetNode> nodes;
    nodes.push_back(SemanticNode(
        L"settings.title", L"text", L"Settings",
        {24, 24, profile.width - 48, 36}, render));
    auto appearance = SemanticNode(
        L"settings.page.appearance", L"button", L"Appearance",
        {24, 84, profile.width - 48, 48}, render);
    appearance.actionId = L"settings.page.appearance";
    nodes.push_back(std::move(appearance));
    auto permissions = SemanticNode(
        L"settings.page.permissions", L"button", L"Permissions unavailable",
        {24, 144, profile.width - 48, 48}, render);
    permissions.actionId = L"settings.page.permissions";
    permissions.isDisabled = true;
    nodes.push_back(std::move(permissions));
    if (error) {
        nodes.push_back(SemanticNode(
            L"settings.error", L"text", L"Settings could not be loaded",
            {24, 204, profile.width - 48, 40}, render));
    } else {
        nodes.push_back(SemanticNode(
            L"settings.loading", L"loadingIndicator", L"Loading settings",
            {24, 204, 40, 40}, render));
    }
    auto hidden = SemanticNode(
        L"settings.hidden.reset", L"button", L"Hidden reset",
        {24, 260, profile.width - 48, 48}, render);
    hidden.actionId = L"settings.reset";
    render.accessibilityRegions.pop_back();
    nodes.push_back(std::move(hidden));
    return ComposeFixture(
        L"settings", L"Settings", profile, std::move(nodes), std::move(render),
        L"settings.page.appearance", error ? 102 : 101);
}

Fixture YtMusicFixture(const Profile& profile) {
    gba::RenderResult render;
    std::vector<gba::WidgetNode> nodes;
    nodes.push_back(SemanticNode(
        L"ytmusic.artwork", L"image", L"DLV-015 Song album artwork",
        {24, 24, 160, 160}, render));
    nodes.push_back(SemanticNode(
        L"ytmusic.track.title", L"text", L"DLV-015 Song by Fixture Artist",
        {204, 36, profile.width - 228, 44}, render));
    auto progress = SemanticNode(
        L"ytmusic.progress.slider", L"slider", L"Playback position",
        {204, 100, profile.width - 252, 44}, render);
    progress.accessibilityValue = L"0:12 of 3:00";
    progress.value = 12;
    progress.minimum = 0;
    progress.maximum = 180;
    progress.step = 5;
    progress.valueChangedActionId = L"ytmusic.seek";
    nodes.push_back(std::move(progress));
    auto pending = SemanticNode(
        L"ytmusic.play-pause", L"button", L"Pause, command pending",
        {204, 160, 180, 48}, render);
    pending.actionId = L"ytmusic.play-pause";
    pending.isBusy = true;
    nodes.push_back(std::move(pending));
    nodes.push_back(SemanticNode(
        L"ytmusic.error", L"text", L"Connection failed. Try again",
        {24, 236, profile.width - 48, 40}, render));
    auto retry = SemanticNode(
        L"ytmusic.retry", L"button", L"Retry connection",
        {24, 292, 220, 48}, render);
    retry.actionId = L"ytmusic.retry";
    nodes.push_back(std::move(retry));
    return ComposeFixture(
        L"ytmusic", L"YT Music", profile, std::move(nodes), std::move(render),
        L"ytmusic.retry", 201);
}

Fixture SpotifyFixture(const Profile& profile) {
    gba::RenderResult render;
    std::vector<gba::WidgetNode> nodes;
    nodes.push_back(SemanticNode(
        L"spotify.title", L"text", L"Spotify",
        {24, 24, profile.width - 48, 40}, render));
    auto player = SemanticNode(
        L"spotify.nav.wide.player", L"actionSurface", L"Player, selected",
        {24, 84, 180, 48}, render);
    player.actionId = L"spotify.nav.player";
    player.isSelected = true;
    nodes.push_back(std::move(player));
    auto seek = SemanticNode(
        L"spotify.seek.slider", L"slider", L"Playback position",
        {228, 84, profile.width - 252, 44}, render);
    seek.accessibilityValue = L"1:05 of 3:30";
    seek.value = 65;
    seek.minimum = 0;
    seek.maximum = 210;
    seek.step = 5;
    seek.valueChangedActionId = L"spotify.seek";
    nodes.push_back(std::move(seek));
    auto pending = SemanticNode(
        L"spotify.play-toggle", L"button", L"Pause, command pending",
        {24, 156, 180, 48}, render);
    pending.actionId = L"spotify.play-toggle";
    pending.isBusy = true;
    nodes.push_back(std::move(pending));
    auto next = SemanticNode(
        L"spotify.next", L"button", L"Next track",
        {220, 156, 180, 48}, render);
    next.actionId = L"spotify.next";
    nodes.push_back(std::move(next));
    return ComposeFixture(
        L"spotify", L"Spotify", profile, std::move(nodes), std::move(render),
        L"spotify.nav.wide.player", 301);
}

LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    auto* host = reinterpret_cast<gba::accessibility::ProviderHost*>(
        GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
        const auto* create = reinterpret_cast<CREATESTRUCTW*>(lParam);
        host = static_cast<gba::accessibility::ProviderHost*>(create->lpCreateParams);
        SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(host));
    }
    if (message == WM_GETOBJECT && host)
        return host->HandleWmGetObject(wParam, lParam);
    if (message == WM_CLOSE) {
        DestroyWindow(window);
        return 0;
    }
    if (message == WM_DESTROY) {
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

ComPtr<IUIAutomationElement> FindById(
    IUIAutomation* client,
    IUIAutomationElement* root,
    const std::wstring_view id) {
    VARIANT value{};
    V_VT(&value) = VT_BSTR;
    V_BSTR(&value) = SysAllocStringLen(id.data(), static_cast<UINT>(id.size()));
    ComPtr<IUIAutomationCondition> condition;
    if (!V_BSTR(&value) || FAILED(client->CreatePropertyCondition(
            UIA_AutomationIdPropertyId, value, condition.GetAddressOf()))) {
        VariantClear(&value);
        return {};
    }
    VariantClear(&value);
    ComPtr<IUIAutomationElement> element;
    if (FAILED(root->FindFirst(
            TreeScope_Descendants, condition.Get(), element.GetAddressOf()))) return {};
    return element;
}

std::wstring StringProperty(IUIAutomationElement* element, const PROPERTYID property) {
    VARIANT value{};
    if (!element || FAILED(element->GetCurrentPropertyValue(property, &value))) return {};
    std::wstring result;
    if (V_VT(&value) == VT_BSTR && V_BSTR(&value))
        result.assign(V_BSTR(&value), SysStringLen(V_BSTR(&value)));
    VariantClear(&value);
    return result;
}

CONTROLTYPEID ControlType(const gba::accessibility::Role role) {
    switch (role) {
    case gba::accessibility::Role::Button: return UIA_ButtonControlTypeId;
    case gba::accessibility::Role::Slider: return UIA_SliderControlTypeId;
    case gba::accessibility::Role::Image: return UIA_ImageControlTypeId;
    case gba::accessibility::Role::Progress: return UIA_ProgressBarControlTypeId;
    case gba::accessibility::Role::ListItem: return UIA_ListItemControlTypeId;
    case gba::accessibility::Role::Status: return UIA_StatusBarControlTypeId;
    case gba::accessibility::Role::Heading:
    case gba::accessibility::Role::Text: return UIA_TextControlTypeId;
    }
    return UIA_CustomControlTypeId;
}

void VerifyFixture(
    gba::accessibility::ProviderHost& host,
    IUIAutomation* client,
    IUIAutomationElement* root,
    const Fixture& fixture,
    const Profile& profile) {
    host.Publish(fixture.tree, {
        80.0, 120.0, profile.scale,
        profile.width * profile.scale, profile.height * profile.scale,
    });
    host.RaisePendingEvents();

    Check(StringProperty(root, UIA_NamePropertyId) ==
              fixture.tree.name,
          "named fixture root exposes exact widget and host context");
    std::set<std::wstring, std::less<>> unique;
    std::vector<std::wstring> actualOrder;
    ComPtr<IUIAutomationCondition> all;
    ComPtr<IUIAutomationElementArray> descendants;
    Check(SUCCEEDED(client->CreateTrueCondition(all.GetAddressOf())) && all &&
              SUCCEEDED(root->FindAll(
                  TreeScope_Descendants, all.Get(), descendants.GetAddressOf())) &&
              descendants,
          "named fixture UIA descendants are enumerable");
    int descendantCount{};
    Check(SUCCEEDED(descendants->get_Length(&descendantCount)),
          "named fixture UIA descendant count is available");
    for (int index = 0; index < descendantCount; ++index) {
        ComPtr<IUIAutomationElement> element;
        if (FAILED(descendants->GetElement(index, element.GetAddressOf())) || !element)
            continue;
        const auto automationId = StringProperty(
            element.Get(), UIA_AutomationIdPropertyId);
        if (!automationId.starts_with(L"widget:")) continue;
        Check(unique.insert(automationId).second,
              "named fixture semantic identities are unique");
        actualOrder.push_back(automationId.substr(7));
    }
    Check(actualOrder == fixture.expectedOrder,
          "named fixture semantic order follows the production adapter preorder");

    for (const auto& id : fixture.expectedOrder) {
        const auto automationId = L"widget:" + id;
        auto element = FindById(client, root, automationId);
        Check(static_cast<bool>(element),
              "named fixture exposes every expected semantic element");
        const auto node = std::ranges::find_if(fixture.tree.nodes, [&](const auto& item) {
            return item.domain == gba::accessibility::ElementDomain::Widget &&
                item.id == id;
        });
        Check(node != fixture.tree.nodes.end(),
              "named fixture UIA identity resolves to the immutable adapter node");
        CONTROLTYPEID type{};
        BOOL enabled{};
        Check(StringProperty(element.Get(), UIA_NamePropertyId) == node->name &&
                  StringProperty(element.Get(), UIA_HelpTextPropertyId) == node->value &&
                  SUCCEEDED(element->get_CurrentControlType(&type)) &&
                  type == ControlType(node->role) &&
                  SUCCEEDED(element->get_CurrentIsEnabled(&enabled)) &&
                  enabled == (node->enabled ? TRUE : FALSE),
              "named fixture UIA name, role, value, and enabled state match one adapter revision");
        RECT bounds{};
        Check(SUCCEEDED(element->get_CurrentBoundingRectangle(&bounds)) &&
                  bounds.right > bounds.left && bounds.bottom > bounds.top &&
                  bounds.left >= 80 && bounds.top >= 120 &&
                  bounds.right <= 80 + static_cast<LONG>(profile.width * profile.scale) &&
                  bounds.bottom <= 120 + static_cast<LONG>(profile.height * profile.scale),
              "named fixture semantic bounds remain nonempty and inside the host root");
    }
    Check(!FindById(client, root, L"widget:settings.hidden.reset"),
          "hidden interactive controls are excluded from the reachable tree");

    auto focused = FindById(client, root, L"widget:" + fixture.focusedId);
    BOOL hasFocus{};
    Check(focused && SUCCEEDED(focused->get_CurrentHasKeyboardFocus(&hasFocus)) && hasFocus,
          "named fixture exposes the exact logical focus owner");
    Check(SUCCEEDED(focused->SetFocus()),
          "named fixture focus action is admitted through the real UIA client");
    auto actions = host.TakeActions();
    Check(!actions.empty() && std::ranges::all_of(actions, [&](const auto& action) {
              return action.kind == gba::accessibility::ActionKind::Focus &&
                  action.widgetId == fixture.widgetId &&
                  action.nodeId == fixture.focusedId;
          }),
          "UIA focus action retains exact widget and semantic identity");
}

void VerifyInvoke(
    gba::accessibility::ProviderHost& host,
    IUIAutomation* client,
    IUIAutomationElement* root,
    const wchar_t* id,
    const wchar_t* actionId) {
    auto element = FindById(client, root, L"widget:" + std::wstring(id));
    ComPtr<IUnknown> unknown;
    ComPtr<IUIAutomationInvokePattern> invoke;
    Check(element && SUCCEEDED(element->GetCurrentPattern(
              UIA_InvokePatternId, unknown.GetAddressOf())) && unknown &&
              SUCCEEDED(unknown.As(&invoke)) && SUCCEEDED(invoke->Invoke()),
          "named fixture button exposes a working Invoke pattern");
    const auto actions = host.TakeActions();
    const auto invoked = std::ranges::count_if(actions, [&](const auto& action) {
        return action.kind == gba::accessibility::ActionKind::Invoke &&
            action.nodeId == id && action.actionId == actionId;
    });
    Check(invoked == 1 && std::ranges::all_of(actions, [&](const auto& action) {
              return (action.kind == gba::accessibility::ActionKind::Invoke ||
                      action.kind == gba::accessibility::ActionKind::Focus) &&
                  action.nodeId == id;
          }),
          "named fixture Invoke retains its exact action authority");
}

void VerifySlider(
    gba::accessibility::ProviderHost& host,
    IUIAutomation* client,
    IUIAutomationElement* root,
    const wchar_t* id,
    const double expectedValue,
    const double requestedValue) {
    auto element = FindById(client, root, L"widget:" + std::wstring(id));
    ComPtr<IUnknown> unknown;
    ComPtr<IUIAutomationRangeValuePattern> range;
    double value{};
    Check(element && SUCCEEDED(element->GetCurrentPattern(
              UIA_RangeValuePatternId, unknown.GetAddressOf())) && unknown &&
              SUCCEEDED(unknown.As(&range)) &&
              SUCCEEDED(range->get_CurrentValue(&value)) && value == expectedValue,
          "named fixture slider exposes its exact current value");
    Check(SUCCEEDED(range->SetValue(requestedValue)),
          "named fixture slider admits an in-range UIA value");
    const auto actions = host.TakeActions();
    const auto values = std::ranges::count_if(actions, [&](const auto& action) {
        return action.kind == gba::accessibility::ActionKind::SetValue &&
            action.nodeId == id && action.requestedValue == requestedValue;
    });
    Check(values == 1 && std::ranges::all_of(actions, [&](const auto& action) {
              return (action.kind == gba::accessibility::ActionKind::SetValue ||
                      action.kind == gba::accessibility::ActionKind::Focus) &&
                  action.nodeId == id;
          }),
          "named fixture RangeValue retains exact semantic identity and value");
}

void WriteEvidence(const fs::path& root, const std::vector<Profile>& profiles) {
    if (root.empty()) return;
    fs::create_directories(root);
    std::ofstream stream(root / L"manifest.json", std::ios::binary | std::ios::trunc);
    Check(static_cast<bool>(stream), "retained accessibility evidence path is writable");
    stream << "{\n  \"contract\":\"dlv015-real-host-accessibility-v1\",\n"
              "  \"widgets\":[\"settings\",\"ytmusic\",\"spotify\"],\n"
              "  \"profiles\":[";
    for (std::size_t index = 0; index < profiles.size(); ++index) {
        if (index) stream << ',';
        stream << "\"" << profiles[index].name << "\"";
    }
    stream << "],\n  \"screenshots\":false,\n"
              "  \"fixtures\":[\n"
              "    {\"widget\":\"settings\",\"profile\":\"compact-100\","
              "\"states\":[\"loading\",\"error\",\"disabled\"]},\n"
              "    {\"widget\":\"ytmusic\",\"profile\":\"standard-100\","
              "\"states\":[\"error\",\"busy\",\"range-value\"]},\n"
              "    {\"widget\":\"spotify\",\"profile\":\"standard-150\","
              "\"states\":[\"selected\",\"busy\",\"range-value\"]}\n"
              "  ],\n"
              "  \"contracts\":[\"names\",\"roles\",\"values\",\"bounds\","
              "\"order\",\"invoke\",\"range-value\",\"focus\","
              "\"focus-restoration\",\"hidden-exclusion\"],\n"
              "  \"physicalNarrator\":\"manual-pending\",\n"
              "  \"sanitized\":true\n}\n";
    Check(static_cast<bool>(stream), "retained accessibility evidence is complete");
}

fs::path ParseEvidenceRoot(const int argc, wchar_t** argv) {
    if (argc == 1) return {};
    Check(argc == 3 && std::wstring_view(argv[1]) == L"--evidence-root",
          "usage: RealHostAccessibilityTests [--evidence-root <directory>]");
    return fs::path(argv[2]);
}

} // namespace

int wmain(const int argc, wchar_t** argv) {
    const HRESULT initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    gba::accessibility::ProviderHost host;
    HWND window{};
    std::jthread windowThread;
    try {
        Check(SUCCEEDED(initialized), "COM initializes for real-host accessibility proof");
        std::promise<HWND> windowPromise;
        auto windowFuture = windowPromise.get_future();
        windowThread = std::jthread([&] {
            const HRESULT apartment = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
            WNDCLASSW windowClass{};
            windowClass.lpfnWndProc = WindowProc;
            windowClass.hInstance = GetModuleHandleW(nullptr);
            windowClass.lpszClassName = L"GameBarAlternative.RealHostAccessibilityTests";
            RegisterClassW(&windowClass);
            HWND window = CreateWindowExW(
                0, windowClass.lpszClassName, L"RealHostAccessibilityTests",
                WS_OVERLAPPED, 0, 0, 980, 720, nullptr, nullptr,
                windowClass.hInstance, &host);
            host.Bind(window, WM_APP + 15);
            host.SetWindowFocused(true);
            host.SetWindowVisible(true);
            windowPromise.set_value(window);
            if (window) {
                MSG message{};
                while (GetMessageW(&message, nullptr, 0, 0) > 0) {
                    TranslateMessage(&message);
                    DispatchMessageW(&message);
                }
            }
            UnregisterClassW(windowClass.lpszClassName, windowClass.hInstance);
            if (SUCCEEDED(apartment)) CoUninitialize();
        });
        window = windowFuture.get();
        Check(window != nullptr, "real-host accessibility test HWND is created");
        ComPtr<IUIAutomation> client;
        ComPtr<IUIAutomationElement> root;
        Check(SUCCEEDED(CoCreateInstance(
                  CLSID_CUIAutomation, nullptr, CLSCTX_INPROC_SERVER,
                  IID_PPV_ARGS(client.GetAddressOf()))) && client &&
                  SUCCEEDED(client->ElementFromHandle(window, root.GetAddressOf())) && root,
              "Windows UIA client obtains the production provider through WM_GETOBJECT");

        const std::vector<Profile> profiles{
            {"compact-100", 520, 620, 1.0},
            {"standard-100", 980, 720, 1.0},
            {"standard-150", 980, 720, 1.5},
        };
        auto settings = SettingsFixture(profiles[0], false);
        VerifyFixture(host, client.Get(), root.Get(), settings, profiles[0]);
        VerifyInvoke(
            host, client.Get(), root.Get(), L"settings.page.appearance",
            L"settings.page.appearance");
        auto disabled = FindById(
            client.Get(), root.Get(), L"widget:settings.page.permissions");
        BOOL enabled{TRUE};
        Check(disabled && SUCCEEDED(disabled->get_CurrentIsEnabled(&enabled)) && !enabled,
              "Settings disabled state reaches the real UIA client");
        CONTROLTYPEID loadingType{};
        auto loading = FindById(client.Get(), root.Get(), L"widget:settings.loading");
        Check(loading && SUCCEEDED(loading->get_CurrentControlType(&loadingType)) &&
                  loadingType == UIA_ProgressBarControlTypeId,
              "Settings loading state reaches UIA as Progress");

        auto settingsError = SettingsFixture(profiles[0], true);
        VerifyFixture(host, client.Get(), root.Get(), settingsError, profiles[0]);
        Check(!FindById(client.Get(), root.Get(), L"widget:settings.loading") &&
                  FindById(client.Get(), root.Get(), L"widget:settings.error"),
              "Settings error replacement removes stale loading semantics");

        auto ytMusic = YtMusicFixture(profiles[1]);
        VerifyFixture(host, client.Get(), root.Get(), ytMusic, profiles[1]);
        VerifyInvoke(
            host, client.Get(), root.Get(), L"ytmusic.retry", L"ytmusic.retry");
        VerifySlider(
            host, client.Get(), root.Get(), L"ytmusic.progress.slider", 12, 25);
        auto pendingYt = FindById(client.Get(), root.Get(), L"widget:ytmusic.play-pause");
        Check(pendingYt && SUCCEEDED(pendingYt->get_CurrentIsEnabled(&enabled)) && !enabled,
              "YT Music busy action reaches UIA as unavailable");

        auto spotify = SpotifyFixture(profiles[2]);
        Check(std::ranges::any_of(spotify.tree.nodes, [](const auto& node) {
                  return node.id == L"spotify.nav.wide.player" && node.selected;
              }),
              "Spotify fixture retains selected state before provider publication");
        VerifyFixture(host, client.Get(), root.Get(), spotify, profiles[2]);
        VerifySlider(
            host, client.Get(), root.Get(), L"spotify.seek.slider", 65, 90);
        auto selectedPlayer = FindById(
            client.Get(), root.Get(), L"widget:spotify.nav.wide.player");
        Check(selectedPlayer &&
                  StringProperty(selectedPlayer.Get(), UIA_NamePropertyId) ==
                      L"Player, selected",
              "Spotify selected Button state reaches UIA through its exact accessible name");
        auto pendingSpotify = FindById(
            client.Get(), root.Get(), L"widget:spotify.play-toggle");
        Check(pendingSpotify &&
                  SUCCEEDED(pendingSpotify->get_CurrentIsEnabled(&enabled)) && !enabled,
              "Spotify busy playback action reaches UIA as unavailable");

        // Re-publish the compact Settings tree after two unrelated widget
        // generations. Exact semantic identity, not stale provider position,
        // restores the singular logical focus owner.
        VerifyFixture(host, client.Get(), root.Get(), settings, profiles[0]);
        auto restored = FindById(
            client.Get(), root.Get(), L"widget:settings.page.appearance");
        BOOL restoredFocus{};
        Check(restored && SUCCEEDED(restored->get_CurrentHasKeyboardFocus(&restoredFocus)) &&
                  restoredFocus,
              "Settings focus restores after YT Music and Spotify generations");

        WriteEvidence(ParseEvidenceRoot(argc, argv), profiles);
        host.Detach();
        PostMessageW(window, WM_CLOSE, 0, 0);
        windowThread.join();
        if (SUCCEEDED(initialized)) CoUninitialize();
        std::cout << "RealHostAccessibilityTests passed (" << checks << " checks)\n";
        return 0;
    } catch (const std::exception& error) {
        host.Detach();
        if (window) PostMessageW(window, WM_CLOSE, 0, 0);
        if (windowThread.joinable()) windowThread.join();
        if (SUCCEEDED(initialized)) CoUninitialize();
        std::cerr << "RealHostAccessibilityTests failed after " << checks
                  << " checks: " << error.what() << '\n';
        return 1;
    }
}
