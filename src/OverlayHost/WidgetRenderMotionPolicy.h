#pragma once
#include "DeclarativeRenderer.h"

namespace widgetrail {
// Shared by host preparation and painting. Callers pass the candidate snapshot
// when preparing, rather than inferring media eligibility from the old page.
inline void ApplyWidgetRenderMotionPolicy(DeclarativeRenderOptions& options,
    const PlatformAppearance* appearance, const WidgetSnapshot& snapshot,
    bool compositorContent) {
    options.compositorWidgetTransitions = compositorContent && !snapshot.embeddedMediaSession;
    options.widgetAnimations = {};
    if (!appearance) return;
    options.widgetAnimations.focus = animation::ParseFocusStyle(appearance->focusAnimation);
    options.widgetAnimations.section = animation::ParseSectionStyle(appearance->sectionAnimation);
    options.widgetAnimations.speed = appearance->widgetAnimationSpeed;
    options.widgetAnimations.modal = appearance->animateWidgetModals
        ? (appearance->modalAnimation == L"zoom" ? animation::ModalStyle::Zoom : animation::ModalStyle::Lift)
        : animation::ModalStyle::None;
}
}
