#include "DeveloperInspector.h"
#include <commctrl.h>
#include <iostream>
#include <stdexcept>
#include <string>

namespace {
void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
std::wstring Text(HWND window) {
    const auto length = GetWindowTextLengthW(window);
    std::wstring value(static_cast<std::size_t>(length) + 1, L'\0');
    GetWindowTextW(window, value.data(), length + 1); value.resize(length); return value;
}
HWND InspectorWindow() {
    HWND found{};
    EnumThreadWindows(GetCurrentThreadId(), [](HWND window, LPARAM data) -> BOOL {
        wchar_t name[128]{};
        GetClassNameW(window, name, 128);
        if (std::wstring_view(name) == L"WidgetRail.DeveloperInspector")
            *reinterpret_cast<HWND*>(data) = window;
        return TRUE;
    }, reinterpret_cast<LPARAM>(&found));
    return found;
}
}

int main() {
    try {
        widgetrail::WidgetSnapshot snapshot;
        snapshot.instanceId = L"inspector.fixture"; snapshot.sequence = 8;
        snapshot.root.id = L"root"; snapshot.root.kind = L"scroll";
        snapshot.root.scrollAxis = L"vertical"; snapshot.root.collectionLoading = L"after";
        snapshot.root.text = L"SECRET_DISPLAY";
        snapshot.root.textEntryValue = L"SECRET_TEXT";
        snapshot.root.accessibilityValue = L"SECRET_ACCESSIBILITY";
        snapshot.root.imageSource = L"SECRET_URL";
        snapshot.root.artworkHandle = L"SECRET_ART";
        snapshot.root.windowId = L"SECRET_WINDOW";
        widgetrail::RenderResult result; result.succeeded = true;
        Require(widgetrail::BuildDeveloperInspectorFrame(L"fixture", snapshot, result, L"root").nodes.empty(),
            "Absent capture was synthesized into inspector data");
        auto capture = std::make_shared<widgetrail::RenderInspection>();
        capture->viewport = {0, 0, 200, 100};
        widgetrail::RenderInspectionNode node;
        node.id = L"root"; node.kind = L"scroll"; node.laidOut = true;
        node.bounds = node.visibleBounds = capture->viewport; capture->nodes.push_back(node);
        result.inspection = capture; result.scrollViewports[L"root"] = {widgetrail::declarative::ScrollAxis::Vertical, capture->viewport, 12, 80};
        result.timing.totalMicroseconds = 245; result.focusRects[L"root"] = capture->viewport;
        auto frame = widgetrail::BuildDeveloperInspectorFrame(L"fixture", snapshot, result, L"root");
        Require(frame.nodes.size() == 1, "Inspector omitted an actual captured node");
        Require(frame.status.find(L"245 us") != std::wstring::npos, "Inspector omitted renderer timing");
        Require(frame.nodes[0].details.find(L"offset: 12 / 80") != std::wstring::npos, "Inspector omitted actual scroll state");
        Require(frame.nodes[0].details.find(L"SECRET") == std::wstring::npos, "Inspector exposed values or media authority");
        const auto owner = CreateWindowExW(0, L"STATIC", L"Inspector test owner", WS_OVERLAPPEDWINDOW,
            0, 0, 100, 100, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
        Require(owner != nullptr, "Synthetic owner creation failed");
        {
            widgetrail::DeveloperInspectorWindow inspector;
            inspector.Publish(owner, frame);
            auto window = InspectorWindow();
            Require(window && GetForegroundWindow() != window, "Inspector creation failed or stole foreground");
            const auto tree = FindWindowExW(window, nullptr, WC_TREEVIEWW, nullptr);
            const auto details = FindWindowExW(window, nullptr, L"EDIT", nullptr);
            Require(tree && details && TreeView_GetCount(tree) == 1, "Inspector controls lack captured nodes");
            TreeView_SelectItem(tree, TreeView_GetRoot(tree));
            Require(Text(details).find(L"SCROLL / CURSOR") != std::wstring::npos, "Node selection did not show details");
            SendMessageW(window, WM_COMMAND, 1, 0);
            Require(!inspector.WantsCapture(), "Pause did not suspend capture");
            const auto frozenText = Text(details);
            frame.nodes[0].details = L"new frame";
            inspector.Publish(owner, frame);
            Require(Text(details) == frozenText, "Paused inspector adopted a new frame");
            SendMessageW(window, WM_COMMAND, 1, 0);
            inspector.Publish(owner, frame);
            Require(Text(details) == L"new frame", "Resume did not adopt the latest frame");
            inspector.RetainLastFrame(L"Overlay hidden");
            Require(TreeView_GetCount(tree) == 1 && Text(details) == L"new frame" &&
                Text(FindWindowExW(window, nullptr, L"STATIC", nullptr)).find(L"INACTIVE") != std::wstring::npos,
                "Hidden overlay lost its captured frame or was presented as live");
            inspector.Publish(owner, frame);
            Require(Text(FindWindowExW(window, nullptr, L"STATIC", nullptr)).find(L"LIVE") != std::wstring::npos,
                "A new capture did not clear the inactive state");
            SendMessageW(window, WM_CLOSE, 0, 0);
            Require(!InspectorWindow() && IsWindow(owner) && !inspector.WantsCapture(), "Inspector close affected its owner");
            inspector.Reopen(); inspector.Publish(owner, frame);
            Require(InspectorWindow() != nullptr, "Inspector could not reopen");
            inspector.Unavailable(L"awaiting committed frame");
            Require(TreeView_GetCount(FindWindowExW(InspectorWindow(), nullptr, WC_TREEVIEWW, nullptr)) == 0,
                "Unavailable frame retained stale node details");
        }
        DestroyWindow(owner);
        Require(!InspectorWindow(), "Inspector destructor leaked its window");
        std::cout << "DeveloperInspectorTests passed\n";
        return 0;
    } catch (const std::exception& error) {
        std::cerr << "DeveloperInspectorTests failed: " << error.what() << '\n';
        return 1;
    }
}
