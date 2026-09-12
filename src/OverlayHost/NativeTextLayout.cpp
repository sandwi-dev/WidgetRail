#include "NativeTextLayout.h"

#include <dwrite_1.h>

#include <algorithm>
#include <cmath>
#include <cwctype>
#include <string>

namespace widgetrail {
namespace {

using Microsoft::WRL::ComPtr;

constexpr std::size_t kMaximumTextCharacters = 4096;

[[nodiscard]] std::wstring TransformText(
    std::wstring text,
    const NativeTextTransform transform) {
    if (transform == NativeTextTransform::Uppercase) {
        std::transform(text.begin(), text.end(), text.begin(), [](const wchar_t value) {
            return static_cast<wchar_t>(std::towupper(value));
        });
    } else if (transform == NativeTextTransform::Lowercase) {
        std::transform(text.begin(), text.end(), text.begin(), [](const wchar_t value) {
            return static_cast<wchar_t>(std::towlower(value));
        });
    }
    if (text.size() > kMaximumTextCharacters) text.resize(kMaximumTextCharacters);
    return text;
}

[[nodiscard]] ComPtr<IDWriteTextFormat> CreateTextFormat(
    IDWriteFactory* factory,
    const NativeRenderStyle& style) {
    ComPtr<IDWriteTextFormat> format;
    if (!factory) return format;
    const auto family = style.fontFamily().empty()
        ? L"Segoe UI Variable Text"
        : style.fontFamily().c_str();
    if (FAILED(factory->CreateTextFormat(
            family,
            nullptr,
            static_cast<DWRITE_FONT_WEIGHT>(std::clamp(style.fontWeight(), 100, 900)),
            DWRITE_FONT_STYLE_NORMAL,
            DWRITE_FONT_STRETCH_NORMAL,
            std::clamp(style.fontSizePx(), 8.0F, 128.0F),
            L"",
            format.ReleaseAndGetAddressOf()))) {
        return {};
    }
    switch (style.textAlign()) {
    case NativeTextAlign::Center:
        (void)format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER);
        break;
    case NativeTextAlign::End:
        (void)format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_TRAILING);
        break;
    default:
        (void)format->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_LEADING);
        break;
    }
    // Component layout owns vertical placement. Keeping the DirectWrite
    // paragraph at the leading edge makes intrinsic height independent of the
    // maximum line budget; buttons center the complete measured plan later.
    (void)format->SetParagraphAlignment(DWRITE_PARAGRAPH_ALIGNMENT_NEAR);
    const auto wrapping = style.maxLines() == 1
        ? DWRITE_WORD_WRAPPING_NO_WRAP
        : style.overflowWrap() == NativeOverflowWrap::Anywhere
            ? DWRITE_WORD_WRAPPING_EMERGENCY_BREAK
            : DWRITE_WORD_WRAPPING_WRAP;
    (void)format->SetWordWrapping(wrapping);
    if (style.textOverflow() == NativeTextOverflow::Ellipsis) {
        DWRITE_TRIMMING trimming{DWRITE_TRIMMING_GRANULARITY_CHARACTER, 0, 0};
        ComPtr<IDWriteInlineObject> sign;
        if (SUCCEEDED(factory->CreateEllipsisTrimmingSign(
                format.Get(), sign.ReleaseAndGetAddressOf()))) {
            (void)format->SetTrimming(&trimming, sign.Get());
        }
    }
    return format;
}

[[nodiscard]] float ResolveBaseline(
    IDWriteTextFormat* format,
    const NativeRenderStyle& style,
    const float lineHeight) noexcept {
    if (!format) return lineHeight * 0.8F;
    ComPtr<IDWriteFontCollection> collection;
    UINT32 familyIndex{};
    BOOL familyExists{};
    ComPtr<IDWriteFontFamily> family;
    ComPtr<IDWriteFont> font;
    ComPtr<IDWriteFontFace> face;
    if (FAILED(format->GetFontCollection(collection.ReleaseAndGetAddressOf())) ||
        FAILED(collection->FindFamilyName(
            style.fontFamily().empty() ? L"Segoe UI Variable Text" : style.fontFamily().c_str(),
            &familyIndex,
            &familyExists)) || !familyExists ||
        FAILED(collection->GetFontFamily(familyIndex, family.ReleaseAndGetAddressOf())) ||
        FAILED(family->GetFirstMatchingFont(
            static_cast<DWRITE_FONT_WEIGHT>(std::clamp(style.fontWeight(), 100, 900)),
            DWRITE_FONT_STRETCH_NORMAL,
            DWRITE_FONT_STYLE_NORMAL,
            font.ReleaseAndGetAddressOf())) ||
        FAILED(font->CreateFontFace(face.ReleaseAndGetAddressOf()))) {
        return lineHeight * 0.8F;
    }
    DWRITE_FONT_METRICS metrics{};
    face->GetMetrics(&metrics);
    if (metrics.designUnitsPerEm == 0) return lineHeight * 0.8F;
    const auto scale = style.fontSizePx() /
        static_cast<float>(metrics.designUnitsPerEm);
    const auto ascent = static_cast<float>(metrics.ascent) * scale;
    const auto descent = static_cast<float>(metrics.descent) * scale;
    const auto centered = (lineHeight - ascent - descent) * 0.5F + ascent;
    return std::clamp(centered, 0.0F, lineHeight);
}

} // namespace

float NativeTextLayoutPlan::LayoutOriginY(
    const float availableY,
    const float availableHeight,
    const NativeTextVerticalAlignment alignment) const noexcept {
    const auto spare = std::max(0.0F, availableHeight - measuredHeight);
    const auto outerY = availableY +
        (alignment == NativeTextVerticalAlignment::Center ? spare * 0.5F : 0.0F);
    return outerY + inkInsetTop;
}

NativeTextLayoutPlan CreateNativeTextLayoutPlan(
    IDWriteFactory* factory,
    const std::wstring_view sourceText,
    const NativeRenderStyle& style,
    const float maximumWidth,
    const float maximumHeight) {
    NativeTextLayoutPlan result;
    if (!factory || !std::isfinite(maximumWidth) || maximumWidth <= 0.0F ||
        !std::isfinite(maximumHeight) || maximumHeight <= 0.0F) {
        return result;
    }
    auto format = CreateTextFormat(factory, style);
    if (!format) return result;
    const auto text = TransformText(std::wstring(sourceText), style.textTransform());
    const auto width = std::max(1.0F, maximumWidth);
    const auto lineHeight = std::max(1.0F, style.fontSizePx() * style.lineHeight());
    const auto height = std::max(
        lineHeight,
        std::min(maximumHeight,
            lineHeight * static_cast<float>(std::max(1, style.maxLines()))));
    if (FAILED(factory->CreateTextLayout(
            text.data(),
            static_cast<UINT32>(text.size()),
            format.Get(),
            width,
            height,
            result.layout.ReleaseAndGetAddressOf()))) {
        return {};
    }
    result.baseline = ResolveBaseline(format.Get(), style, lineHeight);
    (void)result.layout->SetLineSpacing(
        DWRITE_LINE_SPACING_METHOD_UNIFORM, lineHeight, result.baseline);
    if (std::abs(style.letterSpacingPx()) > 0.001F) {
        ComPtr<IDWriteTextLayout1> layout1;
        if (SUCCEEDED(result.layout.As(&layout1))) {
            (void)layout1->SetCharacterSpacing(
                0.0F,
                style.letterSpacingPx(),
                0.0F,
                DWRITE_TEXT_RANGE{0, static_cast<UINT32>(text.size())});
        }
    }
    DWRITE_TEXT_METRICS metrics{};
    if (FAILED(result.layout->GetMetrics(&metrics))) return {};
    DWRITE_OVERHANG_METRICS overhang{};
    if (FAILED(result.layout->GetOverhangMetrics(&overhang))) return {};
    result.layoutWidth = width;
    result.layoutHeight = height;
    result.inkInsetTop = std::max(0.0F, overhang.top);
    result.inkInsetBottom = std::max(0.0F, overhang.bottom);
    result.measuredWidth = std::min(
        width,
        std::max(0.0F, metrics.widthIncludingTrailingWhitespace));
    result.measuredHeight = std::min(
        maximumHeight,
        std::max(lineHeight,
            metrics.height + result.inkInsetTop + result.inkInsetBottom));
    return result;
}

void NativeTextLayoutCache::Clear() {
    entries_.clear();
    uses_.clear();
    characters_ = 0;
    factory_.Reset();
}

NativeTextLayoutPlan NativeTextLayoutCache::Get(
    IDWriteFactory* factory, const std::wstring_view text,
    const NativeRenderStyle& style, const float width, const float height) {
    // Reject invalid constraints before constructing an ordered floating-point key.
    if (!factory || !std::isfinite(width) || !std::isfinite(height) ||
        width <= 0 || height <= 0)
        return {};
    if (factory_.Get() != factory) { Clear(); factory_ = factory; }
    Key key{std::wstring(text.substr(0, kMaximumTextCharacters)), style.fontFamily(),
        style.fontWeight(), style.fontSizePx(), style.letterSpacingPx(),
        style.lineHeight(), style.maxLines(), style.textOverflow(),
        style.overflowWrap(), style.textTransform(), style.textAlign(), width, height};
    if (auto found = entries_.find(key); found != entries_.end()) {
        ++hits;
        uses_.splice(uses_.begin(), uses_, found->second.use);
        return found->second.plan;
    }
    ++misses;
    auto plan = CreateNativeTextLayoutPlan(factory, text, style, width, height);
    if (!plan.IsValid()) return plan;
    // Bound both layout count and retained source text; evict only the oldest entries.
    const auto characters = std::get<0>(key).size() + std::get<1>(key).size();
    while (!uses_.empty() && (entries_.size() >= 1024 || characters_ + characters > 131072)) {
        characters_ -= std::get<0>(uses_.back()).size() + std::get<1>(uses_.back()).size();
        entries_.erase(uses_.back());
        uses_.pop_back();
    }
    characters_ += characters;
    uses_.push_front(key);
    entries_.emplace(std::move(key), Entry{plan, uses_.begin()});
    return plan;
}

} // namespace widgetrail
