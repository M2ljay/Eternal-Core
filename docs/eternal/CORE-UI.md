# Core UI

Core UI is Core's own look for Steam Big Picture, built into Core's Decky plugin (`Source/GamingModeDeckyPlugin`)
instead of a CSS Loader theme. It replaces the old **Core** CSS Loader theme (`docs/eternal/brand/css-theme/Core/`,
kept for reference) because:

- CSS Loader rewrites hashed class names through `css_translations.json`, which has no entries for the ES-module Steam
  client (beta from November 2026, Decky 4). Core UI builds every selector at run time from Steam's own class maps
  through `src/steamModules.ts`, which reaches both clients (classic class-map search on Decky 3, `findVar` /
  `findModuleScopeByVar` on Decky 4).
- It can do what a stylesheet cannot: the Starfield canvas, a full-height panel backdrop, controller-only button
  hints, Jarvis's orb and tab.

First shipped in **3.3.1** (Mark III). The Gaming Mode agent's file version rose with it, so Core re-copies its Decky
plugin on the next start.

## Phases

| Phase | Scope | Status |
| ----- | ----- | ------ |
| 1 | Global chrome (top bar, button-hint bar), the Steam menu, Quick Access, context menus, controller-only button hints, Steam menu entries (Awesome Mix in, Digital stores moved), Jarvis's Quick Access tab, presence orb and tap/hold combo | This release |
| 2 | Home (see "Phase 2 plan" below) | Planned |
| 3 | The game page and the Library | Planned |

Steam's grey-blue page backgrounds (Home, game page, Library, Settings) are untouched in phase 1.

## Design rules

From the approved boards (`eternal-row/project/*.dc.html`: Main, Strip, Context, Home, GamePage, Library) and the
Core palette (`src/corePalette.ts`, `docs/eternal/brand/PALETTE.md`):

- AMOLED black backgrounds, crimson `#D7263D` accents, steel `#C9D1D9` text, gunmetal `#1A1D21` surfaces, raised
  `#2A2F36`, crimson as text `#EA5A68`, hint grey `#9AA4AE`, status green `#6FA37F` and amber `#C98B4A` (black text on
  those fills).
- No gold, no haze or nebula, no Steam blue, no grey-blue gradients. Glass only over game art.
- **Focus** is a gunmetal fill plus a crimson ring, underline or left bar, with no glow. The Starfield screensaver's
  corner flash and the Jarvis orb's slow pulse (opacity only) are the only exceptions.
- **Black means starfield.** Every AMOLED-black surface Core UI paints gets the Core Starfield behind its content.
  This is a design rule for every future phase (Home, the game page, the Library): a black panel or page gets stars;
  stars never go behind game art or video. The stars stay subtle: none is brighter than 24% over black, so Core's
  dimmest text (`#9AA4AE`) still has 4.5:1 against the brightest star pixel (checked in `tests/coreUi.test.mjs`).

## What phase 1 styles

| Area | Result | Board |
| ---- | ------ | ----- |
| Top bar | Clear (black or the game art shows through); opaque state black with stars | Home, Library (48px bar, 32px inset) |
| Button-hint bar | Clear; opaque state black with stars; no grey strip | Home |
| Steam menu | Black panel with the Starfield, the Core wordmark above Home, one 24px left inset for every row, fixed 32px icon cells and 16px labels in steel; focused row gunmetal with a 3px crimson **left bar**; Home's active marker crimson; the "MENU" footer black | Context (48px rows, inset, 16px labels) |
| Quick Access | Black panel with the Starfield behind the tab rail and the content; flat gunmetal title strip (no gradient bars); selected tab gunmetal (Core's crimson bar stays); black footer | Context, Library (tabs) |
| Plugin content in Quick Access | Steam's `DialogButton`s and dropdowns gunmetal; focused: raised gunmetal with a 2px crimson ring (Discord's grey-blue dropdown becomes neutral) | Strip (buttons) |
| Context menus and submenus (a game's Options / "…", Manage, menus from the Steam menu and Quick Access) | Black panel with the Starfield, 1px gunmetal edge, 10px corners, 8px top and bottom; 48px rows, 22px inset, 16px steel text, icons aligned with a 12px gap; 13px hint-grey header; hairline gunmetal separators; focused item gunmetal with the crimson left bar; an open submenu's item keeps it; disabled items hint grey; danger items (Steam's *Destructive*: Uninstall, Remove) crimson text `#EA5A68` | Context |
| Full height | The Steam menu and Quick Access run from the very top of the screen to the very bottom (see below) | Main |

### Full height

Steam places both panels between the top bar and the button hints. Core UI does not move or resize the panel (that
would shift focus and break its scrolling). Instead the panel's Starfield canvas, which is black, reaches past the
panel: Core UI measures the gap above and below the panel each second (`getBoundingClientRect`, at most a quarter of
the screen each way) and extends the canvas by that much (`--core-ui-extend-top` / `--core-ui-extend-bottom` on the
canvas only). The top bar's clock or icons and the footer hints stay readable on top of the black and stars. Works the
same at 1080p and 4K: the numbers are CSS pixels, and the canvas draws at one device pixel per CSS pixel (stars are
slightly softer at 4K, by design, to keep it cheap).

**Needs the TV:** if Steam clips the panel (`overflow: hidden` on an ancestor), the extension is cut off and the gaps
stay as Steam draws them, now black rather than grey (the top bar and footer are clear). That would mean Steam's menu
container needs its own rule; note what is seen.

### Controller-only button hints

Steam's footer hints ("A SELECT / B BACK / ≡ OPTIONS") and other glyph prompts follow Big Picture's **navigation
source**: `FocusNavController.m_navigationSource` (also reached as `GamepadNavTree.m_context.m_controller`), a
subscribable value `{ eActivationSourceType, nActiveGamepadIndex, nLastActiveGamepadIndex }` that
`ChangeNavigationSource` sets on every input (shape from `steam-sharedjscontext-types`, as of 2026-11-27). There is no
Steam setting or `SteamClient` call for it.

Core UI wraps that one value's `Set` (`src/coreInputPrompts.ts`): a keyboard or mouse source is rewritten to the last
controller source seen, with the same controller index, so Steam keeps choosing that controller's glyph family
(Steam Controller, Xbox, PlayStation). The number Steam uses for "controller" is learned from a value that names a
controller (`nActiveGamepadIndex >= 0`), never assumed, and remembered (`core.ui.controller-source-type` in Steam's
localStorage) so the first hints after a restart are already right. Keyboard and mouse input still work. An unknown
shape is left alone (Core UI tries again for two minutes, then logs once). Part of the Core UI setting.

**Needs the TV:** that a stray keyboard or mouse event (Windows, swipe typing, SendInput dictation, a remote) no
longer flips the hints, and that mouse clicks still land.

### Steam menu entries

- **Awesome Mix** (the Eternal-Mix Decky plugin, folder and name "Awesome Mix") replaces Core's *Digital stores*
  entry. It opens Awesome Mix's full-screen YouTube Music page (`/awesome-mix/youtube-music-big-picture`, registered
  by Awesome Mix). The entry exists only while Decky has the plugin loaded (checked at start, on Decky's plugin
  updates and every 5 seconds): no plugin, no entry (`src/coreMenuEntries.ts`).
- **Digital stores** opens from Quick Access > Core > the store section (*Digital stores*, above the Plugin Store).
- No Jarvis entry in the Steam menu (decided 2026-11-27): Jarvis has its own Quick Access tab instead.

The registry is the shared Steam menu contract also used by the Discord plugin (`src/StoreDigitalMenuRegistry.ts`);
Core's rows are styled with Steam's own.

### Jarvis

- **Quick Access tab** with an orb icon (a crimson orb in a steel ring with a crimson quarter, Core's mark in
  miniature), right after Core's tab. It shows the input field and *Send* at the top, *What's on screen?* and *Talk to
  Jarvis*, the hold-to-talk hint, the last answer, then the chat; *Dictate*, *Clear* and *Open full page* below. The
  Jarvis section is no longer inside Core's tab in Quick Access. Core owns both tabs the same way (`src/qam.ts`,
  `extraTabs`; through the Shortcuts bridge when Shortcuts is installed).
- **Presence orb** in the top bar, right after Steam's clock: dim steel ring when idle, a slow crimson pulse while
  listening or thinking (no glow; still under reduced motion), a crimson ring for 4 seconds when an answer arrives.
  Hidden during a game unless Jarvis is busy or has just answered (`src/jarvisPresence.ts`). Part of Core UI.
- **The talk combo, default L5+R5** (dictation moved to L4+R4; the screen helper stays L4+L5): a **short tap**
  (released within 300 ms) opens Quick Access straight to the Jarvis tab with the input focused, or closes it if it is
  already open there, also during a game (Quick Access opens over it); **holding** it is push-to-talk as before.
  Recording starts on the press either way and a tap cancels it. Desktop: Gaming Mode > Jarvis > *Short tap: open
  Jarvis in Quick Access*. Details: [JARVIS.md](JARVIS.md).
- The full Jarvis page is black with the Starfield.

## Turning it off

Quick Access > Core > **Look** > **Core UI** (on by default; `core.ui.enabled` in Steam's localStorage). Off removes
the stylesheet, the `data-core-ui` attribute, every Starfield canvas, the Jarvis orb, and the controller-prompt pin at
once; Steam looks and behaves as without Core UI. The Steam menu entries, Jarvis's tab and the tap/hold combo do not
depend on it.

## CSS Loader

Core UI does not use CSS Loader and does not need it. If a CSS Loader theme is switched back on, Core UI wins on the
areas it styles: every selector starts with `html[data-core-ui]`, repeats its target class (Steam's class map value
twice), and every declaration is `!important`, so Core UI's rules out-rank a theme's ordinary rules and its
`!important` rules up to three classes deep. A theme with deeper selectors can still win on a property it sets; switch
that part of the theme off. Areas Core UI does not style (Home, the game page, the Library in phase 1) keep the
theme. The old Core theme should stay off: its menu, Quick Access and context-menu files duplicate Core UI.

## How it works

- `src/coreUiStyles.ts`: finds Steam's class maps (main menu, Quick Access, Quick Access controls, footer, header,
  dropdown, Big Picture context menu) by keys only that map has, and builds the stylesheet. A rule whose map or class
  is missing on the running client is left out and logged once (`[Core UI] <rule>: Steam class map not found`); the
  rest still applies. No hashed class name is written in the source.
- `src/coreUi.ts`: injects the one stylesheet into every Steam UI document (Big Picture and its popups), places the
  Starfield canvases and the orb, rebuilds while class maps are still loading (up to 20 tries), and tears everything
  down on *off*.
- `src/coreStarfield.ts`: the Core Starfield's flight (the screensaver's seeded field, colours, projection and speed;
  `screensaver/main.js` keeps the bouncing mark and clock). One shared field and one animation loop for every surface,
  160 stars, at most 30 frames a second, drawn only while a surface is on screen and large enough; held still (one
  frame) while a game runs and under reduced motion. A closed menu costs one cheap check a second.
- `src/coreInputPrompts.ts`, `src/coreMenuEntries.ts`, `src/coreUiSetting.ts`, `src/coreUiLocale.ts` (12 languages),
  `src/CoreUiSection.tsx`, `src/CoreStars.tsx`, `src/jarvisPresence.ts`, `src/jarvisTabLocale.ts` (12 languages).

Tests (Linux and CI): `tests/coreUi.test.mjs` (selectors from fake Decky 3 and Decky 4 class maps, missing maps,
brand checks, context menus, full height, the Starfield, the Awesome Mix swap, the setting on and off, the orb, the
controller-prompt pin) and `tests/coreJarvisTab.test.mjs` (tap/hold split, opening and closing the Jarvis tab, the tab
after Core's, the orb states, defaults, text).

## Phase 2 plan: Home

Not built yet. Home's bottom tabs become:

- **Jarvis brief**: a greeting ("Good evening, sir"), what's new for your games (updates, wishlist sales), mods with
  updates, and a **Talk** button. This absorbs the planned Jarvis greeting card.
- **Consoles**: one shelf per console (PlayStation, Nintendo, Sega, Xbox, Retro, VR), matching the Library's tabs.
- **On this day**: kept.

Steam's *What's new* and *Friends* tabs are removed. Home's page background turns black with the Starfield ("black
means starfield"), except behind the hero art, which fades into black (Home board).

## TV checklist (phase 1)

Stable Steam, Decky 3.3.1, every CSS Loader theme off, Core 3.3.1 installed.

1. Steam menu: black panel with drifting stars from the top edge of the screen to the bottom, the Core wordmark above
   Home, every icon and label on the same left line (Home, Library, Discord, Awesome Mix, Store, Friends & Chat, Media,
   Downloads, Settings, Power). Move down: the focused row is gunmetal with a crimson bar on its left, no underline,
   no glow. Home's marker is crimson. The "MENU" footer is black.
2. Awesome Mix in the Steam menu opens its YouTube Music page. Disable Awesome Mix in Decky: the entry disappears
   within 5 seconds; enable it: it comes back. *Digital stores* is gone from the Steam menu and opens from Quick Access
   > Core > store section.
3. Quick Access: black with stars from top to bottom, no grey or blue-grey bars around the title, the open tab
   gunmetal with the crimson bar, black footer with readable A / B hints. Discord's tab: the *Servers* dropdown and the
   buttons are gunmetal; focused ones get the crimson ring.
4. Context menus: a game tile's Options, then *Manage*: black with stars, hairline separators, focused item gunmetal
   with the crimson left bar, *Uninstall* in crimson text, disabled items grey. The menus opened from the Steam menu
   and Quick Access look the same.
5. Top bar and hint bar: clear over Home's art; no grey strip under the Steam menu or Quick Access.
6. Button hints: press a key on a keyboard, move the mouse, dictate with L4+R4: the hints stay A / B / ≡ in the
   controller's own style. With an Xbox controller they are Xbox glyphs; with the Steam Controller, Steam's.
7. Jarvis: the orb sits after the clock (dim). Hold L5+R5 and ask something: it pulses, then shows a crimson ring.
   Tap L5+R5: Quick Access opens on the Jarvis tab with the cursor in the field; tap again: closes. In a game: tap
   opens Quick Access over it; the orb is hidden in the game unless Jarvis is busy.
8. Quick Access > Core > Look > **Core UI** off: everything above returns to Steam's look at once (no restart);
   on again: back.
9. Re-enable one CSS Loader theme that styles the Steam menu: Core UI's menu still wins; Home follows the theme.
10. Leave the Steam menu open for a minute with a game running: the stars hold still. Settings > Accessibility >
    reduce motion (Windows animation effects off): stars still, no pulse.
11. 4K output: the same layout; stars a little softer.

Inferred and to confirm on the TV: which element Steam's `Menu` class sits on in the Steam menu and Quick Access (the
star canvas and wordmark go there), whether the menus' ancestors clip the full-height extension, Steam's
`OpenQuickAccessMenu` accepting Core's tab ids, and the navigation-source pin (above).
