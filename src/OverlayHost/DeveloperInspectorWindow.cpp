#include "DeveloperInspector.h"
#include <commctrl.h>
#include <algorithm>
#include <cmath>
#include <map>

namespace widgetrail {
struct DeveloperInspectorWindow::Impl final {
    HWND window{}, tree{}, details{}, status{}, map{}, owner{};
    HFONT font{};
    bool dismissed{}, frozen{}, rebuilding{};
    std::wstring selected, navigation, inactiveReason;
    DeveloperInspectorFrame frame;
    std::map<std::wstring, HTREEITEM, std::less<>> items;
    std::map<std::wstring, std::wstring, std::less<>> parents;
    static constexpr wchar_t windowClass[] = L"WidgetRail.DeveloperInspector";
    static constexpr wchar_t mapClass[] = L"WidgetRail.DeveloperInspector.Map";

    ~Impl() { if (window) DestroyWindow(window); if (font) DeleteObject(font); }
    static std::wstring Safe(std::wstring_view value) {
        std::wstring result(value.substr(0, 180));
        for (auto& ch : result) if (ch < L' ') ch = L' ';
        return result;
    }
    int Px(int value) const { return MulDiv(value, static_cast<int>(window ? GetDpiForWindow(window) : 96), 96); }
    void UpdateFont() {
        const auto next = CreateFontW(-MulDiv(10, static_cast<int>(GetDpiForWindow(window)), 72), 0, 0, 0, FW_NORMAL,
            FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI");
        if (!next) return;
        for (const auto child : {status, tree, details, map}) if (child) SendMessageW(child, WM_SETFONT, reinterpret_cast<WPARAM>(next), TRUE);
        if (font) DeleteObject(font);
        font = next;
    }
    void Layout() {
        RECT r{}; GetClientRect(window, &r);
        const int width = r.right, height = r.bottom;
        const int left = std::clamp(width / 4, Px(190), Px(310));
        const int right = std::clamp(width / 3, Px(260), Px(420));
        MoveWindow(status, Px(14), Px(12), std::max(1, width - Px(28)), Px(92), TRUE);
        MoveWindow(tree, Px(12), Px(108), left, std::max(1, height - Px(120)), TRUE);
        MoveWindow(map, left + Px(24), Px(108), std::max(1, width - left - right - Px(48)), std::max(1, height - Px(120)), TRUE);
        MoveWindow(details, std::max(left + Px(40), width - right - Px(12)), Px(108), right, std::max(1, height - Px(120)), TRUE);
    }
    void RefreshDetails() {
        const auto found = std::find_if(frame.nodes.begin(), frame.nodes.end(), [&](const auto& node) { return node.visual.id == selected; });
        const std::wstring text = found == frame.nodes.end() ? L"Select a node to inspect geometry and resolved styles." : found->details;
        // Preserve the selected detail pane's scroll position on unchanged frames.
        const int length = GetWindowTextLengthW(details);
        std::wstring previous(static_cast<std::size_t>(length) + 1, L'\0');
        GetWindowTextW(details, previous.data(), length + 1); previous.resize(length);
        if (previous != text) SetWindowTextW(details, text.c_str());
        const std::wstring heading = (frozen ? L"PAUSED  |  " : inactiveReason.empty() ? L"LIVE  |  " : L"INACTIVE  |  ") +
            (inactiveReason.empty() ? frame.status : inactiveReason) + L"\r\nLast navigation: " + navigation;
        SetWindowTextW(status, heading.c_str());
        InvalidateRect(map, nullptr, FALSE);
    }
    void RefreshTree() {
        const bool same = items.size() == frame.nodes.size() && std::all_of(frame.nodes.begin(), frame.nodes.end(), [&](const auto& node) {
            return items.contains(node.visual.id) && parents[node.visual.id] == node.visual.parentId;
        });
        rebuilding = true;
        SendMessageW(tree, WM_SETREDRAW, FALSE, 0);
        if (!same) { TreeView_DeleteAllItems(tree); items.clear(); parents.clear(); }
        for (const auto& node : frame.nodes) {
            std::wstring label = (node.visual.id == frame.focusedId ? L"> " : L"") + Safe(node.visual.id) + L"  [" + Safe(node.visual.kind) + L"]";
            if (!node.visual.laidOut) label += L" (hidden)";
            TVITEMW item{}; item.mask = TVIF_TEXT; item.pszText = label.data();
            if (same) { item.hItem = items.at(node.visual.id); TreeView_SetItem(tree, &item); }
            else {
                TVINSERTSTRUCTW insert{}; insert.hParent = items.contains(node.visual.parentId) ? items.at(node.visual.parentId) : TVI_ROOT;
                insert.hInsertAfter = TVI_LAST; insert.item = item;
                const auto handle = TreeView_InsertItem(tree, &insert);
                items.emplace(node.visual.id, handle); parents.emplace(node.visual.id, node.visual.parentId);
                if (node.visual.parentId.empty()) TreeView_Expand(tree, handle, TVE_EXPAND);
            }
        }
        if (selected.empty() || !items.contains(selected))
            selected = items.contains(frame.focusedId) ? frame.focusedId : frame.nodes.empty() ? L"" : frame.nodes.front().visual.id;
        if (!same && items.contains(selected)) TreeView_SelectItem(tree, items.at(selected));
        SendMessageW(tree, WM_SETREDRAW, TRUE, 0);
        rebuilding = false; InvalidateRect(tree, nullptr, TRUE); RefreshDetails();
    }
    struct Projection { float scale{}, x{}, y{}; };
    Projection Project() const {
        RECT r{}; GetClientRect(map, &r);
        const auto scale = std::min(static_cast<float>(std::max(1L, r.right - Px(24))) / std::max(1.0F, frame.viewport.width),
            static_cast<float>(std::max(1L, r.bottom - Px(55))) / std::max(1.0F, frame.viewport.height));
        return {scale, static_cast<float>(Px(12)) - frame.viewport.x * scale, static_cast<float>(Px(40)) - frame.viewport.y * scale};
    }
    RECT MapRect(declarative::Rect bounds) const {
        const auto p = Project();
        return {static_cast<LONG>(std::lround(bounds.x * p.scale + p.x)), static_cast<LONG>(std::lround(bounds.y * p.scale + p.y)),
            static_cast<LONG>(std::lround((bounds.x + bounds.width) * p.scale + p.x)), static_cast<LONG>(std::lround((bounds.y + bounds.height) * p.scale + p.y))};
    }
    void PaintMap() {
        PAINTSTRUCT ps{}; const auto target = BeginPaint(map, &ps); RECT client{}; GetClientRect(map, &client);
        const auto dc = CreateCompatibleDC(target);
        const auto bitmap = CreateCompatibleBitmap(target, std::max(1L, client.right), std::max(1L, client.bottom));
        if (!dc || !bitmap) {
            if (bitmap) DeleteObject(bitmap); if (dc) DeleteDC(dc);
            FillRect(target, &client, GetSysColorBrush(COLOR_WINDOW)); EndPaint(map, &ps); return;
        }
        const auto oldBitmap = SelectObject(dc, bitmap);
        FillRect(dc, &client, GetSysColorBrush(COLOR_WINDOW));
        SetBkMode(dc, TRANSPARENT); SetTextColor(dc, GetSysColor(COLOR_WINDOWTEXT));
        const auto oldFont = SelectObject(dc, font);
        constexpr wchar_t title[] = L"LAYOUT MAP | rendered DIPs";
        TextOutW(dc, Px(10), Px(10), title, static_cast<int>(std::size(title) - 1));
        const auto oldBrush = SelectObject(dc, GetStockObject(NULL_BRUSH));
        const auto oldPen = SelectObject(dc, GetStockObject(DC_PEN));
        const auto viewport = MapRect(frame.viewport); SetDCPenColor(dc, RGB(150, 160, 175));
        Rectangle(dc, viewport.left, viewport.top, viewport.right, viewport.bottom);
        const auto saved = SaveDC(dc); IntersectClipRect(dc, viewport.left, viewport.top, viewport.right + 1, viewport.bottom + 1);
        for (const auto& node : frame.nodes) {
            if (!node.visual.laidOut || node.visual.visibleBounds.width <= 0 || node.visual.visibleBounds.height <= 0) continue;
            const auto bounds = MapRect(node.visual.visibleBounds);
            SetDCPenColor(dc, node.visual.id == selected ? RGB(30, 100, 220) : node.visual.id == frame.focusedId ? RGB(0, 150, 90) : RGB(200, 207, 218));
            Rectangle(dc, bounds.left, bounds.top, bounds.right, bounds.bottom);
            if (node.visual.id == selected) {
                RECT inner = bounds; InflateRect(&inner, -1, -1);
                Rectangle(dc, inner.left, inner.top, inner.right, inner.bottom);
            }
        }
        RestoreDC(dc, saved); SelectObject(dc, oldPen); SelectObject(dc, oldBrush); SelectObject(dc, oldFont);
        BitBlt(target, 0, 0, client.right, client.bottom, dc, 0, 0, SRCCOPY);
        SelectObject(dc, oldBitmap); DeleteObject(bitmap); DeleteDC(dc); EndPaint(map, &ps);
    }
    static LRESULT CALLBACK MapProc(HWND hwnd, UINT message, WPARAM wp, LPARAM lp) {
        auto* self = reinterpret_cast<Impl*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
        if (message == WM_NCCREATE) {
            self = static_cast<Impl*>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams);
            SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        }
        if (self && message == WM_PAINT) { self->PaintMap(); return 0; }
        if (message == WM_ERASEBKGND) return 1;
        if (self && message == WM_LBUTTONDOWN) {
            const POINT point{static_cast<short>(LOWORD(lp)), static_cast<short>(HIWORD(lp))};
            const auto viewport = self->MapRect(self->frame.viewport);
            if (!PtInRect(&viewport, point)) return 0;
            for (auto it = self->frame.nodes.rbegin(); it != self->frame.nodes.rend(); ++it) {
                const auto rect = self->MapRect(it->visual.visibleBounds);
                if (it->visual.laidOut && PtInRect(&rect, point)) {
                    self->selected = it->visual.id;
                    if (self->items.contains(self->selected)) TreeView_SelectItem(self->tree, self->items.at(self->selected));
                    self->RefreshDetails(); break;
                }
            }
            return 0;
        }
        return DefWindowProcW(hwnd, message, wp, lp);
    }
    static LRESULT CALLBACK Proc(HWND hwnd, UINT message, WPARAM wp, LPARAM lp) {
        auto* self = reinterpret_cast<Impl*>(GetWindowLongPtrW(hwnd, GWLP_USERDATA));
        if (message == WM_NCCREATE) {
            self = static_cast<Impl*>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams);
            self->window = hwnd; SetWindowLongPtrW(hwnd, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        }
        if (!self) return DefWindowProcW(hwnd, message, wp, lp);
        switch (message) {
        case WM_SIZE: if (self->tree) self->Layout(); return 0;
        case WM_GETMINMAXINFO: reinterpret_cast<MINMAXINFO*>(lp)->ptMinTrackSize = {self->Px(820), self->Px(470)}; return 0;
        case WM_DPICHANGED: {
            const auto* rect = reinterpret_cast<RECT*>(lp);
            SetWindowPos(hwnd, nullptr, rect->left, rect->top, rect->right - rect->left, rect->bottom - rect->top, SWP_NOZORDER | SWP_NOACTIVATE);
            self->UpdateFont(); self->Layout(); return 0;
        }
        case WM_COMMAND:
            if (LOWORD(wp) == 1) {
                self->frozen = !self->frozen; self->RefreshDetails();
                CheckMenuItem(GetMenu(hwnd), 1, MF_BYCOMMAND | (self->frozen ? MF_CHECKED : MF_UNCHECKED));
                if (!self->frozen && self->owner) InvalidateRect(self->owner, nullptr, FALSE);
            }
            return 0;
        case WM_NOTIFY:
            if (!self->rebuilding && reinterpret_cast<NMHDR*>(lp)->code == TVN_SELCHANGEDW) {
                const auto handle = reinterpret_cast<NMTREEVIEWW*>(lp)->itemNew.hItem;
                for (const auto& [id, item] : self->items) if (item == handle) { self->selected = id; break; }
                self->RefreshDetails();
            }
            return 0;
        case WM_CLOSE:
            self->dismissed = true;
            if (self->owner) InvalidateRect(self->owner, nullptr, FALSE);
            DestroyWindow(hwnd); return 0;
        case WM_NCDESTROY:
            self->window = nullptr; self->tree = nullptr;
            self->items.clear(); self->parents.clear(); self->frame = {}; break;
        }
        return DefWindowProcW(hwnd, message, wp, lp);
    }
    bool Create(HWND parent) {
        owner = parent;
        const auto instance = GetModuleHandleW(nullptr);
        INITCOMMONCONTROLSEX controls{sizeof(controls), ICC_TREEVIEW_CLASSES}; InitCommonControlsEx(&controls);
        WNDCLASSW wc{}; wc.lpfnWndProc = Proc; wc.hInstance = instance; wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);
        wc.hbrBackground = GetSysColorBrush(COLOR_BTNFACE); wc.lpszClassName = windowClass;
        if (!RegisterClassW(&wc) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return false;
        wc.lpfnWndProc = MapProc; wc.lpszClassName = mapClass; wc.hbrBackground = GetSysColorBrush(COLOR_WINDOW);
        if (!RegisterClassW(&wc) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) return false;
        const auto menu = CreateMenu(); AppendMenuW(menu, MF_STRING, 1, L"Pause capture");
        MONITORINFO monitor{sizeof(monitor)};
        if (!GetMonitorInfoW(MonitorFromWindow(parent, MONITOR_DEFAULTTONEAREST), &monitor))
            monitor.rcWork = {0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN)};
        RECT ownerRect{}; GetWindowRect(parent, &ownerRect);
        const int dpi = static_cast<int>(GetDpiForWindow(parent));
        const int width = std::min(MulDiv(1180, dpi, 96), static_cast<int>(std::max(820L, monitor.rcWork.right - monitor.rcWork.left - 24)));
        const int height = std::min(MulDiv(720, dpi, 96), static_cast<int>(std::max(470L, monitor.rcWork.bottom - monitor.rcWork.top - 24)));
        const int x = ownerRect.right + 12 + width <= monitor.rcWork.right ? ownerRect.right + 12 :
            ownerRect.left - width - 12 >= monitor.rcWork.left ? ownerRect.left - width - 12 : monitor.rcWork.right - width - 12;
        const int y = monitor.rcWork.top + 12;
        if (!CreateWindowExW(WS_EX_TOOLWINDOW, windowClass, L"WidgetRail Developer Inspector", WS_OVERLAPPEDWINDOW,
                x, y, width, height, parent, menu, instance, this)) { DestroyMenu(menu); return false; }
        status = CreateWindowExW(0, L"STATIC", L"Waiting for a committed widget frame", WS_CHILD | WS_VISIBLE, 0, 0, 1, 1, window, nullptr, instance, nullptr);
        tree = CreateWindowExW(WS_EX_CLIENTEDGE, WC_TREEVIEWW, L"Widget nodes", WS_CHILD | WS_VISIBLE | WS_TABSTOP | TVS_HASLINES | TVS_LINESATROOT | TVS_HASBUTTONS | TVS_SHOWSELALWAYS,
            0, 0, 1, 1, window, nullptr, instance, nullptr);
        details = CreateWindowExW(WS_EX_CLIENTEDGE, L"EDIT", L"", WS_CHILD | WS_VISIBLE | WS_TABSTOP | WS_VSCROLL | ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL,
            0, 0, 1, 1, window, nullptr, instance, nullptr);
        map = CreateWindowExW(WS_EX_CLIENTEDGE, mapClass, L"Rendered layout geometry", WS_CHILD | WS_VISIBLE, 0, 0, 1, 1, window, nullptr, instance, this);
        if (!status || !tree || !details || !map) { DestroyWindow(window); return false; }
        UpdateFont();
        Layout(); ShowWindow(window, SW_SHOWNOACTIVATE); return true;
    }
};

DeveloperInspectorWindow::DeveloperInspectorWindow() : impl_(std::make_unique<Impl>()) {}
DeveloperInspectorWindow::~DeveloperInspectorWindow() = default;
bool DeveloperInspectorWindow::WantsCapture() const noexcept { return !impl_->dismissed && !impl_->frozen; }
void DeveloperInspectorWindow::Publish(HWND owner, DeveloperInspectorFrame frame) {
    if (!WantsCapture()) return;
    if (!impl_->window && !impl_->Create(owner)) { impl_->dismissed = true; return; }
    impl_->inactiveReason.clear(); impl_->frame = std::move(frame); impl_->RefreshTree();
}
void DeveloperInspectorWindow::Unavailable(std::wstring_view reason) {
    if (!impl_->window || impl_->frozen) return;
    if (impl_->frame.nodes.empty() && impl_->frame.status == Impl::Safe(reason)) return;
    impl_->inactiveReason.clear(); impl_->frame = {}; impl_->frame.status = Impl::Safe(reason); impl_->RefreshTree();
}
void DeveloperInspectorWindow::RetainLastFrame(std::wstring_view reason) {
    if (!impl_->window || impl_->frozen) return;
    if (impl_->frame.nodes.empty()) { Unavailable(L"Overlay hidden. Reopen it to capture the development widget."); return; }
    const auto next = Impl::Safe(reason);
    if (impl_->inactiveReason == next) return;
    impl_->inactiveReason = next; impl_->RefreshDetails();
}
void DeveloperInspectorWindow::Navigation(std::wstring_view from, std::wstring_view direction, std::wstring_view target, std::wstring_view resolution) {
    if (!WantsCapture()) return;
    impl_->navigation = Impl::Safe(from) + L" --" + Impl::Safe(direction) + L"--> " + Impl::Safe(target) + L" (" + Impl::Safe(resolution) + L")";
    if (impl_->window) impl_->RefreshDetails();
}
void DeveloperInspectorWindow::Reopen() noexcept {
    impl_->dismissed = false; impl_->frozen = false;
    if (impl_->window) {
        CheckMenuItem(GetMenu(impl_->window), 1, MF_BYCOMMAND | MF_UNCHECKED);
        ShowWindow(impl_->window, SW_SHOWNOACTIVATE);
    }
}
}
