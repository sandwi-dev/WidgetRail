# Semantic controller guide in the WinUI shell

The guide is a read-only view of the active input surface. SDK authors continue to
use shortcut labels, primary actions and context actions. No separate guide declaration
or widget-specific host mapping is required.

`WidgetViewPresenter.CaptureControllerGuide` reads the active scope and exact focused
ordinary element or admitted indexed row. Indexed rows append their item declaration
to the collection's logical owner path, as input admission does. Missing/loading/retired
rows advertise no item actions. The existing shared `ControllerShortcutResolver`
determines nearest binding and disabled/busy ownership. Unlabeled or unavailable
nearest owners never expose a different ancestor binding.

Context action discovery is shared with actual dispatch. A focused surface wins;
otherwise exactly one eligible scoped container may own a context button. A declared
menu whose actions are all unavailable consumes its button without advertising an
option or falling through to a shortcut. Popups suppress parent hints; their primary
selection action and host Back/Close remain. Modal input scopes exclude parent actions.

The shell continues to own Back/Close. Native guide buttons send normalized buttons
through `RouteButtonAsync`; they do not capture action ids, retain item leases, create
an input queue, or take focus from the widget. Existing root gamepad-key suppression
also covers the guide.

The view preallocates a bounded 16 native cells. WinUI measures the full styled labels
and glyphs. Optional hints fit as whole units; LT/RT and LB/RB pairs fit together. Host
Back/Close are reserved. Invisible cells remain measured to preserve height across
input context changes, and are removed from hit testing and the accessibility content
view. Theme/text scaling and Xbox/PlayStation glyphs use the shared chrome roles.

## Validation

Managed shell suite: 56 passed, 0 failed/skipped, including ten new tests for shortcut
ownership, availability, indexed ancestry, normalization and measured-width admission.
WinUI frontend builds with the Windows App SDK analyzers and no warnings.

Native fixture extensions are in `--validate-shell-chrome` (whole-label fitting,
paired actions, host controls, stable slot and native invocation) and
`--validate-context-menu` (guide matches actual context routing, modal scope and
unavailable-menu precedence). These additions have not been run in this lane because
the physical candidate owns the deployment slot. Run them in the integration checkout
and inspect screenshots before treating native appearance/interaction as verified.
