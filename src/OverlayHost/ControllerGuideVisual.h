#pragma once
#include "OverlayPlacement.h"
#include "ControllerPromptFont.h"
#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>

namespace widgetrail::guide {
enum class Control { Unknown, A, B, X, Y, LB, RB, LT, RT, L3, R3, LeftStick, RightStick,
    DPad, Horizontal, Vertical, Up, Down, Left, Right, View, Menu, Guide };
inline UINT32 PromptCharacter(Control control) noexcept {
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
inline std::vector<std::wstring_view> ControlParts(std::wstring_view value) {
    std::vector<std::wstring_view> result;
    while(!value.empty()) {
        const auto end=value.find_first_of(L"+/");
        auto part=value.substr(0,end);
        while(!part.empty() && part.front()==L' ') part.remove_prefix(1);
        while(!part.empty() && part.back()==L' ') part.remove_suffix(1);
        if(!part.empty()) result.push_back(part);
        if(end==std::wstring_view::npos) break;
        value.remove_prefix(end+1);
    }
    return result;
}
inline float ControlWidth(Control control,float size) noexcept {
    return (control==Control::LB||control==Control::RB||control==Control::LT||control==Control::RT)
        ? size*1.35F : size;
}
inline float ControlsWidth(std::wstring_view value,float size) {
    float width=0;
    for(auto part:ControlParts(value)) { if(width>0) width+=size*.5F; width+=ControlWidth(ResolveControl(part),size); }
    return width;
}
inline void DrawControl(ID2D1RenderTarget* target, IDWriteTextFormat* format,
    Control control, D2D1_RECT_F bounds, ID2D1SolidColorBrush* brush, float progress=-1) {
    const float x=bounds.left,y=bounds.top,w=bounds.right-x,h=bounds.bottom-y;
    const float cx=x+w*.5F,cy=y+h*.5F,stroke=std::max(1.2F,h*.065F);
    const auto line=[&](float x1,float y1,float x2,float y2) {
        target->DrawLine({x+x1*w,y+y1*h},{x+x2*w,y+y2*h},brush,stroke);
    };
    const auto ring=[&](float radius) { target->DrawEllipse({{cx,cy},radius,radius},brush,stroke); };
    std::wstring_view label;
    auto labelBounds = bounds;
    if (!PromptFont().Draw(target, PromptCharacter(control), bounds, brush)) {
    switch(control) {
    case Control::A: label=L"A"; ring(h*.43F); break;
    case Control::B: label=L"B"; ring(h*.43F); break;
    case Control::X: label=L"X"; ring(h*.43F); break;
    case Control::Y: label=L"Y"; ring(h*.43F); break;
    case Control::LB: case Control::RB:
        target->DrawRoundedRectangle({{x+stroke,y+h*.12F,bounds.right-stroke,y+h*.88F},h*.17F,h*.17F},brush,stroke);
        line(.15F,.12F,.85F,.12F); label=control==Control::LB?L"LB":L"RB"; break;
    case Control::LT: case Control::RT:
        line(.12F,.08F,.88F,.08F); line(.88F,.08F,.72F,.92F);
        line(.72F,.92F,.28F,.92F); line(.28F,.92F,.12F,.08F);
        label=control==Control::LT?L"LT":L"RT"; break;
    case Control::L3: case Control::R3:
        ring(h*.44F); label=control==Control::L3?L"L3":L"R3";
        line(.36F,.82F,.5F,.95F); line(.5F,.95F,.64F,.82F); break;
    case Control::LeftStick: case Control::RightStick:
        target->DrawEllipse({{cx,y+h*.78F},w*.38F,h*.12F},brush,stroke);
        line(.5F,.45F,.5F,.78F);
        target->DrawEllipse({{cx,y+h*.32F},w*.28F,h*.24F},brush,stroke);
        labelBounds = {x+w*.22F,y+h*.08F,x+w*.78F,y+h*.56F};
        label=control==Control::LeftStick?L"L":L"R"; break;
    case Control::View:
        target->DrawRectangle({x+w*.12F,y+h*.15F,x+w*.65F,y+h*.65F},brush,stroke);
        target->DrawRectangle({x+w*.36F,y+h*.4F,x+w*.89F,y+h*.9F},brush,stroke); break;
    case Control::Menu:
        for(float at:{.25F,.5F,.75F}) line(.16F,at,.84F,at); break;
    case Control::Guide:
        ring(h*.45F); line(.22F,.48F,.5F,.23F); line(.5F,.23F,.78F,.48F);
        line(.3F,.43F,.3F,.74F); line(.3F,.74F,.7F,.74F); line(.7F,.74F,.7F,.43F); break;
    case Control::DPad: case Control::Horizontal: case Control::Vertical:
    case Control::Up: case Control::Down: case Control::Left: case Control::Right: {
        // A recognizable cross with direction markers; the shape carries meaning without color.
        line(.12F,.5F,.88F,.5F); line(.5F,.12F,.5F,.88F);
        const bool all=control==Control::DPad;
        if(all||control==Control::Horizontal||control==Control::Left) {line(.12F,.5F,.3F,.34F);line(.12F,.5F,.3F,.66F);}
        if(all||control==Control::Horizontal||control==Control::Right) {line(.88F,.5F,.7F,.34F);line(.88F,.5F,.7F,.66F);}
        if(all||control==Control::Vertical||control==Control::Up) {line(.5F,.12F,.34F,.3F);line(.5F,.12F,.66F,.3F);}
        if(all||control==Control::Vertical||control==Control::Down) {line(.5F,.88F,.34F,.7F);line(.5F,.88F,.66F,.7F);}
        break;
    }
    default: label=L"?"; break;
    }
    if(!label.empty()) {
        const auto prior=format->GetTextAlignment(); format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
        const auto priorParagraph = format->GetParagraphAlignment();
        format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER);
        target->DrawTextW(label.data(),static_cast<UINT32>(label.size()),format,labelBounds,brush,
            control==Control::LeftStick || control==Control::RightStick
                ? D2D1_DRAW_TEXT_OPTIONS_NONE : D2D1_DRAW_TEXT_OPTIONS_CLIP);
        format->SetParagraphAlignment(priorParagraph);
        format->SetTextAlignment(prior);
    }
    }
    if(progress>=0) {
        const int segments=static_cast<int>(std::clamp(progress,0.0F,1.0F)*48);
        for(int i=0;i<segments;++i) {
            const float a=(i/48.0F)*6.2831853F-1.5707963F,b=((i+1)/48.0F)*6.2831853F-1.5707963F;
            const float radius=h*.51F;
            target->DrawLine({cx+std::cos(a)*radius,cy+std::sin(a)*radius},
                {cx+std::cos(b)*radius,cy+std::sin(b)*radius},brush,stroke*1.5F);
        }
    }
}
inline void DrawControls(ID2D1RenderTarget* target, IDWriteTextFormat* format,
    std::wstring_view buttons,D2D1_POINT_2F origin,float size,ID2D1SolidColorBrush* brush,float progress) {
    bool first=true;
    for(auto part:ControlParts(buttons)) {
        if(!first) {
            const wchar_t separator=buttons.find(L'/')!=std::wstring_view::npos?L'/':L'+';
            const D2D1_RECT_F rect{origin.x,origin.y,origin.x+size*.5F,origin.y+size};
            const auto prior=format->GetTextAlignment(); format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
            target->DrawTextW(&separator,1,format,rect,brush);
            format->SetTextAlignment(prior); origin.x+=size*.5F;
        }
        const auto control=ResolveControl(part); const float width=ControlWidth(control,size);
        DrawControl(target,format,control,{origin.x,origin.y,origin.x+width,origin.y+size},brush,progress);
        origin.x+=width; first=false;
    }
}
struct HintPalette { D2D1_COLOR_F background, foreground, primaryBackground, primaryForeground; bool highContrast{}; };
// Let text grow without multiplying decorative padding until the fixed guide
// height forces it to shrink again. Large-text mode uses the space for letters.
inline float HintScale(IDWriteTextFormat* format) noexcept {
    return std::clamp(format->GetFontSize()/14.0F,.75F,1.35F);
}
inline std::optional<float> MeasureHints(IDWriteFactory* factory,IDWriteTextFormat* format,std::span<const ControllerGuideHint> hints) {
    if(!factory||!format) return std::nullopt;
    const float scale=HintScale(format), size=24*scale;
    float width=0;
    for(const auto& hint:hints) {
        Microsoft::WRL::ComPtr<IDWriteTextLayout> layout;
        if(FAILED(factory->CreateTextLayout(hint.label.data(),static_cast<UINT32>(hint.label.size()),format,16384,256,&layout))) return std::nullopt;
        DWRITE_TEXT_METRICS metrics{}; if(FAILED(layout->GetMetrics(&metrics))) return std::nullopt;
        if(width>0) width+=10*scale;
        width+=20*scale+metrics.widthIncludingTrailingWhitespace;
        if(!hint.button.empty()) width+=ControlsWidth(hint.button,size)+8*scale;
    }
    return width;
}
inline std::vector<D2D1_RECT_F> PaintHints(ID2D1RenderTarget* target,IDWriteFactory* factory,IDWriteTextFormat* format,
    std::span<const ControllerGuideHint> hints,D2D1_RECT_F bounds,const HintPalette& palette) {
    std::vector<D2D1_RECT_F> boxes;
    const auto width=MeasureHints(factory,format,hints);
    if(!target||!width||*width<=0||bounds.right<=bounds.left||bounds.bottom<=bounds.top) return boxes;
    const float baseScale=HintScale(format);
    const float fit=std::min({1.0F,(bounds.right-bounds.left)/ *width,(bounds.bottom-bounds.top)/(34*baseScale)});
    const float left=bounds.left+((bounds.right-bounds.left)- *width*fit)*.5F;
    const float top=bounds.top+((bounds.bottom-bounds.top)-34*baseScale*fit)*.5F;
    D2D1_MATRIX_3X2_F original; target->GetTransform(&original);
    const auto transform=D2D1::Matrix3x2F::Scale(fit,fit)*D2D1::Matrix3x2F::Translation(left,top)*original;
    Microsoft::WRL::ComPtr<ID2D1SolidColorBrush> fill,ink;
    if(FAILED(target->CreateSolidColorBrush(palette.background,&fill))||FAILED(target->CreateSolidColorBrush(palette.foreground,&ink))) return boxes;
    const auto priorAA=target->GetAntialiasMode(); target->SetAntialiasMode(D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
    const auto priorParagraph=format->GetParagraphAlignment(); const auto priorAlign=format->GetTextAlignment();
    format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_CENTER); format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_LEADING);
    target->SetTransform(transform);
    float x=0;
    for(const auto& hint:hints) {
        const float chipWidth=MeasureHints(factory,format,std::span{&hint,std::size_t{1}}).value_or(0);
        const bool primary=ResolveControl(hint.button)==Control::A;
        fill->SetColor(primary?palette.primaryBackground:palette.background);
        ink->SetColor(primary?palette.primaryForeground:palette.foreground);
        const D2D1_RECT_F chip{x,0,x+chipWidth,34*baseScale};
        target->FillRoundedRectangle({chip,8*baseScale,8*baseScale},fill.Get());
        const float oldOpacity=ink->GetOpacity(); ink->SetOpacity(palette.highContrast?1.0F:.25F);
        target->DrawRoundedRectangle({chip,8*baseScale,8*baseScale},ink.Get(),baseScale); ink->SetOpacity(oldOpacity);
        float labelLeft=x+10*baseScale;
        if(!hint.button.empty()) {
            DrawControls(target,format,hint.button,{labelLeft,5*baseScale},24*baseScale,ink.Get(),hint.holdProgress);
            labelLeft+=ControlsWidth(hint.button,24*baseScale)+8*baseScale;
        }
        target->DrawTextW(hint.label.data(),static_cast<UINT32>(hint.label.size()),format,
            {labelLeft,0,x+chipWidth-10*baseScale,34*baseScale},ink.Get(),D2D1_DRAW_TEXT_OPTIONS_CLIP);
        boxes.push_back({left+x*fit,top,left+(x+chipWidth)*fit,top+34*baseScale*fit});
        x+=chipWidth+10*baseScale;
    }
    target->SetTransform(original); target->SetAntialiasMode(priorAA);
    format->SetParagraphAlignment(priorParagraph); format->SetTextAlignment(priorAlign);
    return boxes;
}
} // namespace widgetrail::guide
