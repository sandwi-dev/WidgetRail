#pragma once
#include "OverlayPlacement.h"
#include "OverlayPosition.h"
#include "ControllerGlyphVisual.h"
#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>
#include <algorithm>
#include <cmath>
#include <atomic>

namespace widgetrail::guide {
using controller::Control;
using controller::SetPlayStationControls;
using controller::PromptCharacter;
using controller::PromptFont;
using controller::ResolveControl;
using controller::ControlParts;
using controller::ControlWidth;
using controller::ControlsWidth;
using controller::DrawControl;
using controller::DrawControls;
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
    std::span<const ControllerGuideHint> hints,D2D1_RECT_F bounds,const HintPalette& palette,
    OverlayPosition position = OverlayPosition::Center) {
    std::vector<D2D1_RECT_F> boxes;
    const auto width=MeasureHints(factory,format,hints);
    if(!target||!width||*width<=0||bounds.right<=bounds.left||bounds.bottom<=bounds.top) return boxes;
    const float baseScale=HintScale(format);
    const float fit=std::min({1.0F,(bounds.right-bounds.left)/ *width,(bounds.bottom-bounds.top)/(34*baseScale)});
    const float left=bounds.left+((bounds.right-bounds.left)- *width*fit)*HorizontalAnchor(position);
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
