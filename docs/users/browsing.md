# Browse with a controller

Browser opens ordinary websites inside WidgetRail. It is preinstalled in
Production; in Bare, install it from Catalogue. Webapps and other hosted pages
share the same controller interaction, with different navigation chrome.

## Page controls

| Input while interacting with the page | Action |
| --- | --- |
| D-pad | Jump to a nearby webpage control; at a list edge, scroll to more content |
| Left stick | Move the pointer freely |
| A | Click; on a text field, open the controller keyboard |
| Hold A and move left stick | Drag; release A to drop |
| Right stick | Scroll |
| LS/L3 | Open Website controls in a webapp, or switch between Browser's toolbar and page |
| LB / RB in Browser | Back / forward in page history |
| X in Browser | Reload, or stop a load |
| Y in Browser | Edit the address or search query |
| LT / RT in Browser | Zoom out / in |
| Menu | Page actions at the pointer |

Webapps expose page commands through **Website controls** rather than a permanent
toolbar. The on-screen guide takes precedence inside dialogs and widget-specific
pages. Some widgets require A to enter a page and B to leave its interaction mode.

D-pad movement does not click automatically. For canvas controls, maps, custom
menus or unusual page layouts, use the free pointer instead. Browser also offers
**Open externally** for work better handled by your desktop browser. Native file
pickers, Windows approval prompts and some authentication flows may need a mouse
or physical keyboard.

## Text entry

On website fields, edits are live: typing, Paste and deletion change the page
immediately. **B** or **Done** closes the keyboard and keeps those changes;
**Send Enter** explicitly sends an Enter key to the site. Closing the keyboard
does not guarantee that a website has submitted or saved its form.

Browser address entry and ordinary widget fields use drafts: **Done/RT** commits,
and **B** cancels. Suggestions depend on the field. A widget can supply loaded
names or service suggestions; eligible fields also support offline word completion.
Suggestions are not a promise that a URL, email address or service item exists.
Private/password fields do not receive ordinary completions.

Hold **X** to delete and **LB/RB** to move the caret. Sustained holds accelerate
from character steps to word steps in supported non-private fields. See
[controller keyboard controls](controls.md#controller-keyboard) for all commands.

## Pages, pop-outs and pins

Ordinary redraws and compatible presentation changes preserve the live page,
its navigation history and scroll position. They do not deliberately reload it.
A page can still navigate or refresh itself.

When a website requests another window, WidgetRail offers **Open in pinned surface**,
**Open over current page**, or **Cancel**. **Remember for this website** saves the
placement choice for this widget/account. Change it in **Website settings → New
windows**. Replacing an existing page or pin still asks for confirmation.

**Open over current page** retains the original page beneath it; B returns there.
An additional page has no permanent toolbar: press LS for its controls.
**Pin pop-out** transfers that live page to a pin; **Return pop-out to widget**
moves it back. A full browser page similarly moves between widget and pin without
creating a second login session. [Pinning](pinning.md) explains focus and removal.

## Sign-ins, background activity and memory

Browser and user-created webapps use persistent profiles. Sign-ins and site
preferences can survive restarting WidgetRail, but a live page, call or unsaved
form is not restored as a running session after quitting. Each widget/account
profile is separate from other profiles and from your desktop browser. A pin or
pop-out belonging to that profile shares its sign-in. Authors can also declare
private browser surfaces; do not assume every widget persists website data.

Hiding WidgetRail is not a universal stop command. Webapps request background
retention; other browser widgets follow their own lifetime policy. Use **Stop
widget** to end its running pages, connections and playback while retaining saved
data. Website policies can still pause playback or require sign-in again.

Browser and media surfaces share browser infrastructure, while retaining separate
profiles. After 15 seconds hidden or in a visible non-interactive pin, WidgetRail
requests WebView2's lower-memory mode. Interaction restores normal mode. This is
a best-effort working-set hint, not a memory cap or suspension: scripts, calls,
notifications and playback may continue, and memory can grow again. Extensions
can add memory and CPU usage in each enabled profile.

Controller navigation improvements help you reach page controls. Memory policies
manage retained browser resources. Neither establishes that WidgetRail is faster
or uses less memory than another browser. See [lifecycle and saved data](lifecycle.md).

## Compatibility and extensions

Standard links and form controls work most predictably. Canvas-only controls,
closed/custom page structures, site-specific shortcuts, protected media and
provider authentication may behave differently. A page rendering successfully
does not establish that all its features work.

[Browser extensions](extensions.md) covers supported imports, per-profile choices
and limitations such as unavailable toolbar popups. Developers can find declaration,
profile and session contracts in the [hosted-browser reference](../reference/widget-web-browser.md).
