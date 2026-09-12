#pragma once

#include "DeclarativeLayout.h"
#include <d2d1.h>
#include <dwrite.h>
#include <memory>
#include <string>

namespace widgetrail::shell {

enum class WirelessStatus { Unknown, NoAdapter, Off, On, Connected };
enum class InternetStatus { Unknown, Offline, Limited, Online };
[[nodiscard]] InternetStatus ResolveInternetStatus(int connectivityLevel) noexcept;

struct TrayStatusSnapshot final {
    std::wstring time;
    std::wstring date;
    InternetStatus internet{InternetStatus::Unknown};
    WirelessStatus bluetooth{WirelessStatus::Unknown};
    [[nodiscard]] std::wstring Description() const;
    friend bool operator==(const TrayStatusSnapshot&, const TrayStatusSnapshot&) = default;
};

// Owns only cached status. Windows radio/profile discovery runs off the UI
// thread; completion owns shared state, never a host pointer or window handle.
class TrayStatusMonitor final {
public:
    TrayStatusMonitor();
    [[nodiscard]] TrayStatusSnapshot Read(bool refreshWireless);
private:
    struct State;
    std::shared_ptr<State> state_;
};

void DrawTrayStatus(ID2D1RenderTarget* target, IDWriteFactory* factory,
    IDWriteTextFormat* format, const TrayStatusSnapshot& status,
    const declarative::Rect& bounds, ID2D1Brush* background,
    ID2D1Brush* foreground, ID2D1Brush* secondary, ID2D1Brush* accent,
    float cornerRadius);

} // namespace widgetrail::shell
