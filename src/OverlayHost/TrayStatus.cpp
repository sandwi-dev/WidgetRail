#include "TrayStatus.h"
#include "NativeIcons.h"
#include <Windows.h>
#include <winrt/Windows.Devices.Radios.h>
#include <winrt/Windows.Networking.Connectivity.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <wrl/client.h>
#include <algorithm>
#include <mutex>

namespace widgetrail::shell {
namespace {
std::wstring StatusText(WirelessStatus state, bool bluetooth) {
    const std::wstring prefix = bluetooth ? L"Bluetooth " : L"Wi-Fi ";
    switch (state) {
    case WirelessStatus::NoAdapter: return prefix + L"not available";
    case WirelessStatus::Off: return prefix + L"off";
    case WirelessStatus::On: return prefix + (bluetooth ? L"on" : L"not connected");
    case WirelessStatus::Connected: return prefix + L"connected";
    default: return prefix + L"status unavailable";
    }
}
}

std::wstring TrayStatusSnapshot::Description() const {
    const auto connection = internet == InternetStatus::Online ? L"Internet access" :
        internet == InternetStatus::Offline ? L"No internet access" :
        internet == InternetStatus::Limited ? L"Limited network access or sign-in required" : L"Internet status unavailable";
    return time + L", " + date + L". " + connection + L". " + StatusText(bluetooth, true);
}

InternetStatus ResolveInternetStatus(int connectivityLevel) noexcept {
    switch (connectivityLevel) {
    case 0: return InternetStatus::Offline;
    case 1: case 2: return InternetStatus::Limited;
    case 3: return InternetStatus::Online;
    default: return InternetStatus::Unknown;
    }
}

struct TrayStatusMonitor::State final {
    std::mutex mutex;
    InternetStatus internet{InternetStatus::Unknown};
    WirelessStatus bluetooth{WirelessStatus::Unknown};
    bool pending{};
    ULONGLONG requestedAt{};
    ULONGLONG completedAt{};

    static winrt::fire_and_forget Refresh(std::shared_ptr<State> state) {
        co_await winrt::resume_background();
        InternetStatus internet = InternetStatus::Unknown;
        WirelessStatus bluetooth = WirelessStatus::Unknown;
        try {
            using namespace winrt::Windows::Devices::Radios;
            const auto radios = co_await Radio::GetRadiosAsync();
            bluetooth = WirelessStatus::NoAdapter;
            for (const auto& radio : radios) {
                auto* value = radio.Kind() == RadioKind::Bluetooth ? &bluetooth : nullptr;
                if (!value) continue;
                const auto current = radio.State();
                if (current == RadioState::On) *value = WirelessStatus::On;
                else if (*value != WirelessStatus::On)
                    *value = current == RadioState::Off || current == RadioState::Disabled
                        ? WirelessStatus::Off : WirelessStatus::Unknown;
            }
        } catch (...) { /* Unavailable is distinct from a powered-off radio. */ }
        try {
            using namespace winrt::Windows::Networking::Connectivity;
            const auto profile = NetworkInformation::GetInternetConnectionProfile();
            internet = profile ? ResolveInternetStatus(static_cast<int>(profile.GetNetworkConnectivityLevel()))
                : InternetStatus::Offline;
        } catch (...) { }
        std::scoped_lock lock(state->mutex);
        state->internet = internet;
        state->bluetooth = bluetooth;
        state->completedAt = GetTickCount64();
        state->pending = false;
    }
};

TrayStatusMonitor::TrayStatusMonitor() : state_(std::make_shared<State>()) {}

TrayStatusSnapshot TrayStatusMonitor::Read(bool refreshWireless) {
    TrayStatusSnapshot result;
    wchar_t time[128]{};
    wchar_t date[128]{};
    if (GetTimeFormatEx(LOCALE_NAME_USER_DEFAULT, TIME_NOSECONDS, nullptr, nullptr, time, 128))
        result.time = time;
    if (GetDateFormatEx(LOCALE_NAME_USER_DEFAULT, DATE_SHORTDATE, nullptr, nullptr, date, 128, nullptr))
        result.date = date;
    bool start{};
    {
        const auto now = GetTickCount64();
        std::scoped_lock lock(state_->mutex);
        if (state_->completedAt && now - state_->completedAt < 30000) {
            result.internet = state_->internet;
            result.bluetooth = state_->bluetooth;
        }
        if (refreshWireless && !state_->pending &&
            (!state_->requestedAt || now - state_->requestedAt >= 5000)) {
            state_->pending = true;
            state_->requestedAt = now;
            start = true;
        }
    }
    if (start) State::Refresh(state_);
    return result;
}

void DrawTrayStatus(ID2D1RenderTarget* target, IDWriteFactory* factory,
    IDWriteTextFormat* format, const TrayStatusSnapshot& status,
    const declarative::Rect& bounds, ID2D1Brush* background,
    ID2D1Brush* foreground, ID2D1Brush* secondary, ID2D1Brush* accent,
    float cornerRadius) {
    if (!target || !factory || !format || !background || !foreground || !secondary || !accent) return;
    const auto box = D2D1::RectF(bounds.x, bounds.y, bounds.x + bounds.width, bounds.y + bounds.height);
    target->FillRoundedRectangle(D2D1::RoundedRect(box, cornerRadius, cornerRadius), background);
    const bool wide = bounds.width >= 160.0F && bounds.height >= 44.0F;
    const float padding = std::min(12.0F, bounds.width * 0.08F);
    const float textLeft = bounds.x + (wide ? 50.0F : padding);
    const float textWidth = std::max(1.0F, bounds.x + bounds.width - padding - textLeft);
    const auto drawText = [&](const std::wstring& value, float y, float height, float size, ID2D1Brush* brush, bool bold) {
        Microsoft::WRL::ComPtr<IDWriteTextLayout> text;
        if (value.empty() || FAILED(factory->CreateTextLayout(value.data(), static_cast<UINT32>(value.size()),
                format, textWidth, height, &text))) return textLeft;
        const DWRITE_TEXT_RANGE range{0, static_cast<UINT32>(value.size())};
        text->SetFontSize(size, range);
        if (bold) text->SetFontWeight(std::max(format->GetFontWeight(), DWRITE_FONT_WEIGHT_SEMI_BOLD), range);
        text->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_TRAILING);
        text->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        text->SetWordWrapping(DWRITE_WORD_WRAPPING_NO_WRAP);
        DWRITE_TEXT_METRICS metrics{};
        if (SUCCEEDED(text->GetMetrics(&metrics)) && metrics.widthIncludingTrailingWhitespace > textWidth) {
            text->SetFontSize(size * textWidth / metrics.widthIncludingTrailingWhitespace, range);
            (void)text->GetMetrics(&metrics);
        }
        target->DrawTextLayout(D2D1::Point2F(textLeft, y), text.Get(), brush, D2D1_DRAW_TEXT_OPTIONS_CLIP);
        return textLeft + std::max(0.0F, textWidth - metrics.widthIncludingTrailingWhitespace);
    };
    const float scale = std::clamp(format->GetFontSize() / 14.0F, 1.0F, 1.5F);
    const bool dateVisible = bounds.height >= 44.0F;
    float textStart = drawText(status.time, bounds.y + (dateVisible ? 4.0F : 0), dateVisible ? bounds.height * .53F : bounds.height,
        (wide ? 22.0F : 19.0F) * scale, foreground, true);
    if (dateVisible) textStart = std::min(textStart, drawText(status.date, bounds.y + bounds.height * .56F,
        bounds.height * .34F, 11.0F * scale, secondary, false));
    if (!wide) return;
    // Follow the actual text, not the unused width of its right-aligned box.
    const float x = std::max(bounds.x + padding, textStart - 32.0F);
    const auto indicator = [&](WirelessStatus state, float y, bool bluetooth) {
        const bool lit = state == WirelessStatus::Connected || (bluetooth && state == WirelessStatus::On);
        auto* brush = lit ? accent : secondary;
        if (bluetooth) {
            const D2D1_POINT_2F points[] = {{x+5,y+3},{x+15,y+13},{x+10,y+18},{x+10,y-2},{x+15,y+3},{x+5,y+13}};
            for (int i = 1; i < 6; ++i) target->DrawLine(points[i-1], points[i], brush, 1.8F);
        } else {
            const auto center = D2D1::Point2F(x+10,y+8);
            target->DrawEllipse(D2D1::Ellipse(center,9,9),brush,1.6F);
            target->DrawEllipse(D2D1::Ellipse(center,4,9),brush,1.3F);
            target->DrawLine(D2D1::Point2F(x+1,y+8),D2D1::Point2F(x+19,y+8),brush,1.3F);
        }
        if (state == WirelessStatus::Off || state == WirelessStatus::NoAdapter)
            target->DrawLine(D2D1::Point2F(x+1,y+18), D2D1::Point2F(x+19,y-1), secondary, 1.5F);
        if (state == WirelessStatus::Unknown)
            target->FillEllipse(D2D1::Ellipse(D2D1::Point2F(x+22,y+14), 2, 2), secondary);
    };
    indicator(status.internet == InternetStatus::Online ? WirelessStatus::Connected :
        status.internet == InternetStatus::Offline ? WirelessStatus::Off :
        status.internet == InternetStatus::Limited ? WirelessStatus::On : WirelessStatus::Unknown,
        bounds.y + bounds.height * .20F, false);
    indicator(status.bluetooth, bounds.y + bounds.height * .60F, true);
}
} // namespace widgetrail::shell
