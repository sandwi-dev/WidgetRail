#pragma once
#include "ControllerPrompt.h"
#include "ControllerPromptFont.h"
#include <algorithm>
#include <cmath>
#include <vector>

namespace widgetrail::controller {
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
    Control control, D2D1_RECT_F bounds, ID2D1SolidColorBrush* brush, float progress=-1, bool playStation = UsePlayStationControls(), bool useFont = true) {
    const float x=bounds.left,y=bounds.top,w=bounds.right-x,h=bounds.bottom-y;
    const float cx=x+w*.5F,cy=y+h*.5F,stroke=std::max(1.2F,h*.065F);
    const auto line=[&](float x1,float y1,float x2,float y2) {
        target->DrawLine({x+x1*w,y+y1*h},{x+x2*w,y+y2*h},brush,stroke);
    };
    const auto ring=[&](float radius) { target->DrawEllipse({{cx,cy},radius,radius},brush,stroke); };
    std::wstring_view label;
    auto labelBounds = bounds;
    if (!useFont || !DrawPrompt(target, control, bounds, brush, playStation)) {
    switch(control) {
    case Control::A:
        if (playStation) { line(.25F,.25F,.75F,.75F); line(.75F,.25F,.25F,.75F); }
        else { label=L"A"; ring(h*.43F); } break;
    case Control::B: if (!playStation) label=L"B"; ring(h*.43F); break;
    case Control::X: if (playStation) target->DrawRectangle({x+w*.18F,y+h*.18F,x+w*.82F,y+h*.82F},brush,stroke); else { label=L"X"; ring(h*.43F); } break;
    case Control::Y: if (playStation) { line(.5F,.12F,.88F,.82F); line(.88F,.82F,.12F,.82F); line(.12F,.82F,.5F,.12F); } else { label=L"Y"; ring(h*.43F); } break;
    case Control::LB: case Control::RB:
        target->DrawRoundedRectangle({{x+stroke,y+h*.12F,bounds.right-stroke,y+h*.88F},h*.17F,h*.17F},brush,stroke);
        line(.15F,.12F,.85F,.12F); label=control==Control::LB?(playStation?L"L1":L"LB"):(playStation?L"R1":L"RB"); break;
    case Control::LT: case Control::RT:
        line(.12F,.08F,.88F,.08F); line(.88F,.08F,.72F,.92F);
        line(.72F,.92F,.28F,.92F); line(.28F,.92F,.12F,.08F);
        label=control==Control::LT?(playStation?L"L2":L"LT"):(playStation?L"R2":L"RT"); break;
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
}
