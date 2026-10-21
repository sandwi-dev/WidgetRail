# Open content across widgets

A supported action can open content in another widget, or update an eligible
existing pin while you stay in the current widget. For example, an ordinary web
link can open Browser; a supported video action can open a video widget. This
works only where the sender and destination provide compatible actions.

## Use a connected action

1. Install and enable the destination widget and finish its service setup.
   Start it again if you previously chose **Stop widget**.
2. Activate the source's link or action. Choose a destination if prompted.
3. Follow that destination's controls. If the action supports delivery into the
   current pin, the pinned content may update without moving controller focus.

Cancelling a chooser leaves the operation unperformed. If no eligible widget
handles a normal web link, WidgetRail can use your default Windows browser.
Account and setup actions can deliberately use Windows directly. Other action
types may report that no compatible destination is available; they do not all
have a browser fallback.

B follows the destination widget's normal navigation. It does not universally
return to the source. Use the rail or radial chooser to switch back.

## What stays under your control

These actions do not install or enable a missing widget, bypass permissions, or
sign in for you. A stopped or unavailable destination is not silently restarted.
Updating an existing pin requires compatible content and placement support;
arbitrary widgets cannot replace any pin they choose.

The developer mechanism is called an **intent**: a named, validated request that
compatible widgets declare they can send or handle. Authors can read the
[intent reference](../reference/widget-intents.md) for declarations and routing.
