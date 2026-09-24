#pragma once
#include <atomic>
#include <cstdint>
#include <string_view>

namespace widgetrail::controller {
enum class Control { Unknown, A, B, X, Y, LB, RB, LT, RT, L3, R3, LeftStick, RightStick,
    DPad, Horizontal, Vertical, Up, Down, Left, Right, View, Menu, Guide };
inline std::atomic_bool playStationControls{};
inline bool SetPlayStationControls(bool enabled) noexcept {
    return playStationControls.exchange(enabled, std::memory_order_relaxed) != enabled;
}
inline bool UsePlayStationControls() noexcept { return playStationControls.load(std::memory_order_relaxed); }
inline std::uint32_t PromptCharacter(Control control, bool playStation = UsePlayStationControls()) noexcept {
    if (playStation) {
        switch (control) {
        case Control::A: return 0x21E3;
        case Control::B: return 0x21E2;
        case Control::X: return 0x21E0;
        case Control::Y: return 0x21E1;
        case Control::LB: return 0x21B0;
        case Control::RB: return 0x21B1;
        case Control::LT: return 0x21B2;
        case Control::RT: return 0x21B3;
        case Control::L3: return 0x21EF;
        case Control::R3: return 0x21F0;
        case Control::View: return 0x2206;
        case Control::Menu: return 0x2208;
        case Control::Guide: return 0xE000;
        default: break;
        }
    }
    switch (control) {
    case Control::A: return 0x21D3;
    case Control::B: return 0x21D2;
    case Control::X: return 0x21D0;
    case Control::Y: return 0x21D1;
    case Control::LB: return 0x2198;
    case Control::RB: return 0x2199;
    case Control::LT: return 0x2196;
    case Control::RT: return 0x2197;
    case Control::L3: return 0x21BA;
    case Control::R3: return 0x21BB;
    case Control::LeftStick: return 0x21CB;
    case Control::RightStick: return 0x21CC;
    case Control::DPad: return 0x21CE;
    case Control::Horizontal: return 0x21A2;
    case Control::Vertical: return 0x21A3;
    case Control::Up: return 0x219F;
    case Control::Down: return 0x21A1;
    case Control::Left: return 0x219E;
    case Control::Right: return 0x21A0;
    case Control::View: return 0x21FA;
    case Control::Menu: return 0x21FB;
    case Control::Guide: return 0x21F9;
    default: return 0;
    }
}
inline Control ResolveControl(std::wstring_view value) noexcept {
    if(value==L"a"||value==L"A") return Control::A;
    if(value==L"b"||value==L"B") return Control::B;
    if(value==L"x"||value==L"X") return Control::X;
    if(value==L"y"||value==L"Y") return Control::Y;
    if(value==L"leftBumper"||value==L"LB") return Control::LB;
    if(value==L"rightBumper"||value==L"RB") return Control::RB;
    if(value==L"leftTrigger"||value==L"LT"||value==L"L2") return Control::LT;
    if(value==L"rightTrigger"||value==L"RT"||value==L"R2") return Control::RT;
    if(value==L"leftStick"||value==L"LS"||value==L"L3") return Control::L3;
    if(value==L"rightStick"||value==L"RS"||value==L"R3") return Control::R3;
    if(value==L"left-stick-move") return Control::LeftStick;
    if(value==L"right-stick-move") return Control::RightStick;
    if(value==L"dpad"||value==L"D-pad") return Control::DPad;
    if(value==L"dpad-horizontal"||value==L"←→") return Control::Horizontal;
    if(value==L"dpad-vertical"||value==L"↑↓") return Control::Vertical;
    if(value==L"dPadUp"||value==L"↑") return Control::Up;
    if(value==L"dPadDown"||value==L"↓") return Control::Down;
    if(value==L"dPadLeft"||value==L"←") return Control::Left;
    if(value==L"dPadRight"||value==L"→") return Control::Right;
    if(value==L"view"||value==L"View") return Control::View;
    if(value==L"menu"||value==L"Menu") return Control::Menu;
    if(value==L"guide"||value==L"Guide") return Control::Guide;
    return Control::Unknown;
}
inline Control ParsePrompt(std::wstring_view value) noexcept {
    if (value == L"a") return Control::A;
    if (value == L"b") return Control::B;
    if (value == L"x") return Control::X;
    if (value == L"y") return Control::Y;
    if (value == L"leftBumper") return Control::LB;
    if (value == L"rightBumper") return Control::RB;
    if (value == L"leftTrigger") return Control::LT;
    if (value == L"rightTrigger") return Control::RT;
    if (value == L"leftStickPress") return Control::L3;
    if (value == L"rightStickPress") return Control::R3;
    if (value == L"leftStickMove") return Control::LeftStick;
    if (value == L"rightStickMove") return Control::RightStick;
    if (value == L"dPad") return Control::DPad;
    if (value == L"dPadHorizontal") return Control::Horizontal;
    if (value == L"dPadVertical") return Control::Vertical;
    if (value == L"dPadUp") return Control::Up;
    if (value == L"dPadDown") return Control::Down;
    if (value == L"dPadLeft") return Control::Left;
    if (value == L"dPadRight") return Control::Right;
    if (value == L"view") return Control::View;
    if (value == L"menu") return Control::Menu;
    if (value == L"guide") return Control::Guide;
    return Control::Unknown;
}
inline std::wstring_view AccessibleName(Control control, bool playStation) noexcept {
    if (playStation) {
        switch (control) {
        case Control::A: return L"Cross button";
        case Control::B: return L"Circle button";
        case Control::X: return L"Square button";
        case Control::Y: return L"Triangle button";
        case Control::LB: return L"L1 button";
        case Control::RB: return L"R1 button";
        case Control::LT: return L"L2 trigger";
        case Control::RT: return L"R2 trigger";
        case Control::View: return L"Create button";
        case Control::Menu: return L"Options button";
        case Control::Guide: return L"PS button";
        default: break;
        }
    }
    switch (control) {
    case Control::A: return L"A button";
    case Control::B: return L"B button";
    case Control::X: return L"X button";
    case Control::Y: return L"Y button";
    case Control::LB: return L"Left bumper";
    case Control::RB: return L"Right bumper";
    case Control::LT: return L"Left trigger";
    case Control::RT: return L"Right trigger";
    case Control::L3: return L"Press left stick";
    case Control::R3: return L"Press right stick";
    case Control::LeftStick: return L"Move left stick";
    case Control::RightStick: return L"Move right stick";
    case Control::DPad: return L"Directional pad";
    case Control::Horizontal: return L"Directional pad left or right";
    case Control::Vertical: return L"Directional pad up or down";
    case Control::Up: return L"Directional pad up";
    case Control::Down: return L"Directional pad down";
    case Control::Left: return L"Directional pad left";
    case Control::Right: return L"Directional pad right";
    case Control::View: return L"View button";
    case Control::Menu: return L"Menu button";
    case Control::Guide: return L"Guide button";
    default: return L"Controller control";
    }
}
}
