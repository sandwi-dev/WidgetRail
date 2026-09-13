# Accessibility

Use semantic SDK components and meaningful labels. The host turns their visible
roles and state into Windows UI Automation information.

## What controls expose

Buttons and action surfaces expose activation. Sliders expose their value and
range. Progress can expose a read-only value. Text and image descriptions help
explain nearby controls.

An action surface owns the interaction; its decorative children should not add
separate activation stops. Controls in an inactive page or modal scope should
not appear as available actions.

The host uses the same presented slider value for drawing and accessibility.
A locally displayed adjustment should not announce a contradictory old value.

## Author useful state

- Name actions by their result, such as “Mute microphone”.
- Distinguish focus from selection and connection status.
- Explain unavailable actions instead of relying only on color.
- Keep stable IDs so presentation updates can preserve identity.
- Test larger text, high contrast, and reduced motion.

Routine controller hints are readable guidance. Transient action feedback can
be announced as a status change without making the guide a new focus target.

## Host behavior

UI Automation reads a host-owned projection; it does not call into a widget
while answering a screen reader. Requested actions return to the window thread
and are checked against the current widget, scope, control, and action.

The projection follows clipping, scrolling, DPI, and active scopes. Stale
providers must stop working after their window generation is destroyed, even
if Windows later reuses the same handle.

Contributors can start in
[`AccessibilityProjection.h`](../../src/OverlayHost/AccessibilityProjection.h)
and [`AccessibilityProvider.cpp`](../../src/OverlayHost/AccessibilityProvider.cpp).
Real-client UI Automation tests complement ordinary element tests; visual
appearance alone does not prove accessibility behavior.
