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
plugin on the next start. **3.3.3** adds the fixes from the first TV pass (numbered as on the TV list, below) and phase 2
(Home and the Library). **3.3.4** adds the snapshot (ground truth for every selector, below), switches CSS Loader's legacy
Core themes off while Core UI is on, and answers the second TV pass (Quick Access rows and icons, the Library's tabs,
tiles and grid, the stars, the Jarvis brief hero).

## 3.3.4: what the second TV pass found, and what changed

The TV showed most phase 1 rules not taking effect. Two causes:

1. **CSS Loader's old "Core" theme was still active** (its wordmark, grey-blue focus and grey "MENU" footer) and won over
   Core UI. Nothing in Core installs that theme; the app's *Core profile* button did re-extract `themes.zip` over the
   user's `config_USER.json` files, which can switch bundled themes back on. Now (`ExtraService.ExtractThemesKeepingUserConfig`)
   an existing `config_USER.json` is never overwritten. With Core UI on, Core also switches the legacy themes `Core` and
   `Core Accents` off once per start and whenever Core UI is switched on (`src/coreCssLoader.ts`, `core_ui_legacy_themes`
   in `main.py`): `"active": false` in their `config_USER.json`, recorded in `%APPDATA%\GamingMode\core-ui-css-themes.json`,
   then CSS Loader is told through its own `set_theme_state` (Decky's plugin-call route, legacy route as fallback) or
   reloaded. Core UI off switches back on exactly the themes Core switched off. The user's other themes are never
   touched; the snapshot lists every theme and whether it is on.
2. **Every selector was inferred, never observed.** The snapshot below records the real DOM so the next fixes are exact.

Core UI's rules now win regardless: every class target carries `:not(#core-ui-boost)` (an id's weight; no element has
that id), and Core UI's `<style>` is moved back to the end of `<head>` whenever another stylesheet (CSS Loader's) is
appended after it (`laterStylesheet`, counted in diagnostics as `restyled`).

### The selector table

`CORE_UI_TARGETS` in `src/coreUiStyles.ts` lists every element Core UI aims at by name, with each way of finding it,
best first. A strategy is a template: `{map.Key}` stands for that class map's class (the strategy is dropped when the map
or key is missing), anything else is plain CSS. The 3.3.4 rules (Quick Access selected tab and rows, the Library's tabs and
capsules) and the snapshot read this table, so a fix after the snapshot is one line there.

### The snapshot

Written by the plugin backend to:

    %APPDATA%\GamingMode\diagnostics\core-ui-snapshot.json

(the user's roaming AppData; a service account's `systemprofile` is never used), overwritten each time, at most about
2 MB. It is captured 5 seconds after Core UI applies, then 1.5 seconds after each surface is first seen (at most one
write every 3 seconds), and on demand from Quick Access > Core > Look > **Save Core UI snapshot** (`src/coreUiSnapshot.ts`).

Format (JSON, `kind: "core-ui-snapshot"`, `format: 1`):

- `reason` (`start`, `opened: <surfaces>`, `manual`), `capturedAt`, `screen` (`width`, `height`, `dpr`, screen size).
- `coreUi`: `applied` and `skipped` rule ids, `mapRoutes` (per class map: `classic`, `findVar`, `scope` or `missing`),
  `selectors` (the selector table resolved: each strategy and the selector it gave, or none).
- `documents`: per Steam document, `main` (the Big Picture window) or a popup, its path, size, DPR, and where Core UI's
  stylesheet sits (`coreStyleLast`, `stylesAfterCore`).
- `surfaces`: one record per surface key: `steam-menu`, `qam`, `qam-rail`, `qam-title`, `qam-content:<tab index>:<label>`
  (one per Quick Access tab opened), `context-menu`, `library-tabs`, `library-collection-tile`, `library-grid`, `home`,
  `footer`, `top-bar`. Each has `doc` (index into `documents`), `via` (the target and selector that found it), `rect`,
  `ancestors` (six levels up) and `tree`: at most 6 levels and 400 nodes, breadth first. A node is `t` (tag), `c`
  (classes), `d` (data-* attribute names), `r` ([x, y, w, h]), `s` (computed styles that differ from the default: `bg`,
  `img` as a type only (`url`, `linear-gradient`), `op`, `pos`, `top`, `bot`, `mh`, `ov`, `z`, `br`), `m` (the Core UI
  rule ids whose selectors match it), `cover: 1` (opaque and over 30% of the surface), `l` (a label of at most 30
  characters; never on the grid, Home or the top bar), `k` (children) and `more` (children left out).
- `diagnostics` (the Starfield's state, full height, `restyled`), `icons` (the icon store's last answer: `ok`, `folder`,
  `errors`, counts; and per Library tab its title, key and icon source: `user-name`, `user-key`, `original` or `none`),
  `cssLoader` (loaded, the legacy guard's last result, `<style>` counts per document).
- `backend` (added by `main.py`): the file path, the icon folder (`folderExists`, `mapExists`) and CSS Loader's themes
  (`themes`: folder, name, active, installed; `active`; `legacyActive`; `legacyRecord`).

No text beyond those short labels, no input values, no URLs with queries, nothing the user typed.

### The user's icons (3.3.4)

`get_core_icons` now looks in every candidate AppData folder (`%APPDATA%`, then `<home>\AppData\Roaming` from Decky's
user home, `%USERPROFILE%` and `~`, never `systemprofile`) and takes the one holding `map.json`. The backend logs
`Core UI icons: ok=... folder=... tabs=...` and the frontend logs `[Core UI] icons loaded` or `icons not loaded (...)`
once per change; the snapshot shows which source each tab used.

### Other 3.3.4 changes

- **Quick Access:** rail icons 30px (`CORE_RAIL_ICON_PX`, `src/coreRailIcons.ts`) for Core, Jarvis and the Eternal
  plugins; the glyphs were redrawn for that size (one fill colour, cut-outs as holes, no hairlines, tight square boxes).
  The selected tab is gunmetal through every way Steam may mark it (`qamTabSelected`). Decky's plugin list and every
  `ButtonItem`: gunmetal rows, steel text, focus raised gunmetal with the square crimson left bar (never Steam's white);
  dialog field wrappers clear.
- **Library tabs:** icon (24px) and label on one line with an 8px gap; tabs transparent in steel; **one** marker per tab,
  a 28 x 3px underline centred under the label, shown only on hover or focus (the family tone) and on the open tab
  (crimson); every other underline or marker on the tab is removed (`libraryTabCss`).
- **Collection tiles:** gunmetal cards, at least 160px tall, the icon centred, the collection's name below
  (`data-core-name`), the tone as a 3px bar along the bottom edge; Steam's collage and label hidden only on tiles Core
  draws an icon for (`collectionTileCss`).
- **Library grid:** capsules drawn at 84% inside Steam's slots (`CORE_LIBRARY_CAPSULE_SCALE`), so even black gutters
  show; Steam's slots, columns and focus order are unchanged (more columns needs the snapshot).
- **Stars:** density follows the surface's area (`starsForArea`: the Steam menu keeps 160, a full page up to 420), one
  star in nine is a larger "near" star at the ceiling, and the fade reaches the ceiling sooner. The ceiling (24%) is
  unchanged, so `#9AA4AE` text keeps 4.5:1.
- **Jarvis brief:** a hero panel (`src/CoreBriefHero.tsx`, data in `src/coreBrief.ts`, text in `src/coreBriefLocale.ts`):
  greeting, date, time and the Decky weather plugin's line (`window.__deckyWeatherTopbarState`), a Continue card (the
  last game's hero art, title and time played; A plays it, X opens its page), the Club's pick (art and days left), chips
  (Steam update waiting, downloads in progress, Forge mod updates; achievements nearly done is wired but has no source
  yet), and Jarvis's orb (slow opacity pulse, still under reduced motion) with **Talk to Jarvis**. Inside its own error
  boundary; the 3.3.3 lines are the fallback.

## Phases

| Phase | Scope | Status |
| ----- | ----- | ------ |
| 1 | Global chrome (top bar, button-hint bar), the Steam menu, Quick Access, context menus, controller-only button hints, Steam menu entries (Awesome Mix in, Digital stores moved), Jarvis's Quick Access tab, presence orb and tap/hold combo | 3.3.1; TV fixes in 3.3.3 |
| 2 | Home (Jarvis brief, Consoles, Club, On this day) and the Library (tab icons and tones, collection tiles, black grid) | 3.3.3 |
| 3 | The game page (and The Collector's ROM pages under the same design) | Planned (see "Phase 3 notes") |

Steam's grey-blue backgrounds stay on the game page and in Settings.

## Design rules

From the approved boards (`eternal-row/project/*.dc.html`: Main, Strip, Context, Home, GamePage, Library) and the
Core palette (`src/corePalette.ts`, `docs/eternal/brand/PALETTE.md`):

- AMOLED black backgrounds, crimson `#D7263D` accents, steel `#C9D1D9` text, gunmetal `#1A1D21` surfaces, raised
  `#2A2F36`, crimson as text `#EA5A68`, hint grey `#9AA4AE`, status green `#6FA37F` and amber `#C98B4A` (black text on
  those fills).
- No gold, no haze or nebula, no Steam blue, no grey-blue gradients. Glass only over game art.
- **Corners** (the user's choice, 3.3.3): square (0 px) for the full-height side panels (Steam menu, Quick Access), the
  top bar, the footer and every focus or selection highlight, the crimson bar included. One token,
  `--core-radius-float: 10px` (set on `body` by the stylesheet; `CORE_RADIUS_FLOAT` in `coreUiStyles.ts`), for floating
  things only: context menus and submenus, dialogs (`.GenericConfirmDialog`), modals, toasts and cards (collection
  tiles, Core Home's tiles). Capsules and art keep Steam's own radius; a focus ring around a capsule follows the art.
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
| Steam menu | Black panel with the Starfield, Core's ring mark alone (44px, top left, in its own 80px band, 3.3.3), one 24px left inset for every row, fixed 32px icon cells and 16px labels in steel; focused row gunmetal with a 3px crimson **left bar**; Home's active marker crimson; the "MENU" footer black | Context (48px rows, inset, 16px labels) |
| Quick Access | Black panel with the Starfield behind the tab rail and the content; the title strip, tab rail, plugin panels and any large opaque layer clear over it (3.3.3); no logo (Core's tab title is the plain name); selected tab gunmetal (Core's crimson bar stays); black footer; Core's logos for the Eternal plugins on the rail | Context, Library (tabs) |
| Plugin content in Quick Access | Steam's `DialogButton`s and dropdowns gunmetal; focused: raised gunmetal with a 2px crimson ring (Discord's grey-blue dropdown becomes neutral) | Strip (buttons) |
| Context menus and submenus (a game's Options / "…", Manage, menus from the Steam menu and Quick Access) | Black panel with the Starfield, 1px gunmetal edge, the float corner token (10px), 8px top and bottom; 48px rows, 22px inset, 16px steel text, icons aligned with a 12px gap; 13px hint-grey header; hairline gunmetal separators; focused item gunmetal with the crimson left bar; an open submenu's item keeps it; disabled items hint grey; danger items (Steam's *Destructive*: Uninstall, Remove) crimson text `#EA5A68` | Context |
| Full height | The Steam menu and Quick Access run from the very top of the screen to the very bottom (see below) | Main |

### Full height (TV item 9, 3.3.3)

The panels themselves now span the top edge of the screen to the bottom one (`src/coreFullHeight.ts`). Steam places
the Steam menu and Quick Access between the top bar and the button hints, with a top offset, a max height or a padded
wrapper (Decky's Quick Access wrapper among them; it differs between clients). Rather than guess which, Core UI measures
the panel once a second and stretches it by what is missing, without moving its content:

    margin-top    -= gap above     padding-top    += gap above
    margin-bottom -= gap below     padding-bottom += gap below
    height         = its height + both gaps (border-box), max-height none, min-height 0

So the black box and its stars reach both edges while rows, focus and scroll position stay exactly where Steam put
them; the top bar's clock and icons and the footer's hints stay readable on top. Then it measures again: anything but
edge to edge (a scrolling ancestor, a flex parent that shrinks it back) undoes every change. An ancestor that only clips
(overflow hidden, not scrolling, e.g. Decky's wrapper) is opened vertically. After three failed tries the panel keeps
Steam's layout and the older fallback stays: the Starfield canvas reaches past the panel (`--core-ui-extend-top` /
`--core-ui-extend-bottom`, at most a quarter of the screen each way). Each write is recorded (`InlineEdits`,
`src/coreUiDom.ts`), so Core UI off restores Steam's exact values. Which Quick Access element is the real panel differs
too: `Container`, `QuickAccessMenu` and `Menu` are all candidates, outermost first; the first one at most 60% of the
screen wide is the panel (black, stars), the others go clear. `window[Symbol.for("core.ui.diagnostics")]().full` shows
the result per panel (`stretched`, `full`, `failed`, `gave-up`). Tested with fake DOMs shaped like Steam's
(`tests/coreUiPhase2.test.mjs`).

**Needs the TV:** that both panels reach the very top and bottom, the clock and hints stay readable, and focus and
scrolling behave in a long Quick Access tab.

### The Steam menu's logo (TV items 1 and 8)

Only Core's ring mark (no wordmark), 44px, top left, inside an 80px band of its own: an in-flow `::before` block with
`order: -1` ahead of every row, so no row and no focus fill can overlap it. If Steam's row list ignores the band (an
absolutely placed list), the runtime pushes it down by what is missing (`keepBelowLogo`). Quick Access has no logo: Core's
tab title is the plain name "Core" (the wordmark comes back with Core UI off).

### Top bar and button-hint bar (TV item 6)

Pure `#000000` whenever Steam paints them opaque (`HeaderOpaque`, footer `Opaque`) and whenever they sit over a plain
page: no translucency, tint, blur, gradient or shadow. Over game art (Home's hero, a game page's art, video) they stay
clear. The runtime decides per bar once a second by hit-testing what is behind it (`chromeState`, three points along
the bar; any picture wins) and sets `data-core-ui-chrome="black"` or `"clear"`.

### Quick Access over the stars (TV items 4 and 7)

The stars sit on the real panel (above); everything over them is clear: the title strip, the tab rail, the content
column, Decky's and every plugin's panel sections, and any large opaque layer the runtime finds inside the panel (at
least 30% of its area, a plain colour or gradient, no picture). That covers the grey area around CSS Loader's panel.
Buttons, inputs, sliders, toggles and pictures are never touched, so CSS Loader's controls keep working and looking as
they do. Tagged `data-core-ui-clear`, removed with Core UI off.

### The Starfield's motion (TV item 5)

The stars stood still in the Steam menu: frames were requested from the main Big Picture window, which Steam stops
drawing while the menu (or a popup) is on top, and each step was capped at 50 ms, so a throttled window barely moved
them. Now each frame is requested in the window of the surface being drawn, with a 250 ms timer backstop, on the main
window's clock, and a step may be up to 250 ms. Visibility uses Chromium's `checkVisibility` (ancestors' display,
visibility and opacity) plus the panel's place on screen: stars move while the menu is open, stop when it is closed or
off screen, hold still (one frame) while a game runs and under reduced motion. Diagnostics:
`window[Symbol.for("core.ui.diagnostics")]()` (`state`: `moving`, `idle`, `still-game`, `still-reduced`).

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

### Plugin logos (TV item 12)

Core's artwork for the user's Eternal plugin forks (`docs/eternal/brand/plugin-logos/`, credited in REBRAND.md),
inlined in `src/coreArtwork.ts`, matched by Decky plugin name in `src/corePluginLogos.ts`:

| Decky plugin name | Logo |
| ----------------- | ---- |
| Discord | assemble |
| Awesome Mix, Now Playing Plus | awesome-mix |
| Nexus Mods, Forge | forge |
| romm-dock, RomM-Dock, The Collector | the-collector |

- Quick Access tab rail: a single-colour steel glyph derived from each tile (`currentColor`, white when focused). A
  user-chosen rail icon still wins; another or renamed plugin keeps its own icon. Only while Core UI is on.
- The Steam menu's Awesome Mix entry: the awesome-mix glyph (Steam's music note if it is ever missing).
- Core's store page: the full tiles as covers for `M2ljay/Eternal-Assemble`, `Eternal-Mix`, `Eternal-Forge` (alias
  `vibe-nexus`) and `Eternal-Collector` (alias `eternal-romm`); upstream plugins keep their own covers.

## Phase 2: the Library (TV items 10 and 11)

Follows the Library board: black with stars behind the grid (`GamepadLibrary` is a page host; its canvas covers only
the visible height), steel small-caps group headers, Steam's glow under capsules off, a 3px crimson focus ring lifted
6px (still under reduced motion), and the tab bar styled like Quick Access: black row, steel tabs, no tab counts, the
open tab gunmetal with a 3px crimson underline (square corners), a focused tab also a 2px crimson ring.

### Library icons and tones

What the CSS Loader themes Enhanced Library and Enhanced Collection did by **position**, Core UI now does by **name**
(`src/coreLibraryIcons.ts`), so adding, hiding or reordering a tab or collection never shifts the others:

- **Tabs** (TabMaster's or Steam's, matched by title, case and punctuation ignored): All Games, Installed, VR,
  PlayStation, Nintendo, Sega, Xbox, Retro, Completed. The icon sits before the label (26px).
- **Collection tiles** in Steam's Collections view, matched by collection name; RomM's `RomM: <platform> (<host>)` form
  works too. A tile with an icon shows it over gunmetal (Steam's collage hidden); a tile without one keeps the collage.
- **Tones**: each family tab gets a 24px marker under its label, and its collection tiles a 40px bar, in the family's
  tone; the active tab adds the crimson underline. All Games, Installed, VR and Completed are steel.

| Family | Tone | Collections (by name) |
| ------ | ---- | --------------------- |
| PlayStation | `#6E8CB8` | PlayStation, PlayStation 2-5, PlayStation Portable, PlayStation Vita |
| Nintendo | `#B07A84` | NES, SNES, Nintendo 64, GameCube, Wii, Wii U, Switch, Game Boy (Color, Advance), DS, 3DS |
| Sega | `#5E9E9A` | Master System, Mega Drive/Genesis, 32X, Sega CD, Saturn, Dreamcast, Game Gear |
| Xbox | `#7FA36B` | Xbox, Xbox 360 (One, Series) |
| Retro | `#8F7CB0` | Arcade, Atari, Neo Geo, TurboGrafx-16/PC Engine, MSX, Commodore, Amiga and the like |

Steam's Collections view has no stable class for a tile, so tiles are found as the links into
`/library/collection/<id>` (and `[data-collection-id]`). **Needs the TV:** that the tiles are found that way on this
Steam build, and where the tab icon sits best (beside the label now; above it is a one-line change).

### Your own icons

The icon set the user prefers is derived from a personal-use pack and is **never committed**. Core loads it at run time
from the PC:

    %APPDATA%\GamingMode\icons\
      map.json
      family-playstation.png, ps2.png, ... (any names)

Core's plugin backend reads it (`get_core_icons` in `main.py`; the cleanest route, it already runs on the PC and
answers Core's own frontend) and hands the images over as data URLs. Rules: only plain file names inside that folder
(no paths), PNG, SVG, WebP or JPEG, at most 1 MB each and 16 MB in all; anything else is skipped and named in the answer's
`errors`. Read once at start, again when Core UI is switched on and at most every ten minutes.

`map.json`: three optional sections, each mapping a name to a file. `tabs` by tab title, `collections` by collection
name (with or without RomM's prefix and host), `keys` by Core's icon key (used for any tab or collection that matches
that key: `all-games`, `installed`, `vr`, `completed`, `playstation`, `nintendo`, `sega`, `xbox`, `retro`, and per
console `ps1`, `ps2`, `ps3`, `psp`, `vita`, `nes`, `snes`, `n64`, `gamecube`, `wii`, `wiiu`, `switch`, `gb`, `gbc`,
`gba`, `ds`, `3ds`, `sms`, `megadrive`, `saturn`, `dreamcast`, `gamegear`, `xbox360`, `arcade`, `atari2600`,
`neogeo`, `pcengine`, `generic`). Names are matched ignoring case and punctuation. A map with none of those sections is
read as one flat list for tabs and collections alike.

```json
{
  "version": 1,
  "tabs": { "All Games": "core-steel-all-games.png", "PlayStation": "core-steel-family-playstation.png" },
  "collections": { "PlayStation 2": "core-steel-ps2.png" },
  "keys": { "n64": "core-steel-n64.png", "snes": "core-steel-snes.png" }
}
```

Order for each tab or tile: the user's map by name, then by key, then Core's original drawings
(`docs/eternal/brand/core-icons/original-*.svg`, inlined in `coreArtwork.ts`: All games, Installed, VR, Completed, PS3,
Xbox, Xbox 360 and the generic pad, which the four family tabs without an original drawing use).

## Phase 2: Home

Follows the Home board: Steam's recent-games hero runs taller (62% of the screen) and fades into black through a mask
(nothing painted over the art); Home's page is black with the Starfield behind the shelves (`BasicHome` is a page
host); section labels in steel small caps; Steam's capsule glow off; focused capsules get the 3px crimson ring, lifted
6px. Core's plugin already patched Home (HomeNews.tsx for the news, DailyHistory.tsx for On this day); `src/coreHome.tsx`
extends that patch. The bottom tabs become:

1. **Jarvis brief** (takes Steam's `WhatsNew` id, so a remembered tab still opens): a greeting for the hour ("Good
   evening, sir"; late hours count as night until 5am), a one-line brief (the last game played), mods with updates if
   Forge publishes them, a **Talk to Jarvis** button that opens Jarvis's Quick Access tab with the input focused, then
   Steam's own news and updates for the user's games with Core's gaming news (what the What's new tab held).
2. **Consoles**: one shelf per family (PlayStation, Nintendo, Sega, Xbox, Retro, VR) built from the user's collections
   by name, with the family's icon and tone; games most recently played first, up to 24; A opens the game page.
3. **Club**: Play Retro Games Club (`src/club/`), registered through the registry below; hidden until the Club is
   on and has data.
4. **On this day**: kept (Steam's Recommended slot).

Steam's What's new and Friends tabs are removed; a remembered Friends tab opens the brief. Everything renders inside an
error boundary whose fallback is what Core rendered before (Steam's Home with the news and On this day), and Core UI off
gives Steam's tabs back. Text in `src/coreHomeLocale.ts` (12 languages).

Not available yet: wishlist sales (no local source; Steam's store data is not reachable offline from Home) and Forge's
mod updates (Forge does not publish them yet). The brief reads `window.__eternalForgeUpdates`
(`{ version: 1, count }` or `{ version: 1, games: [{ appId, name, count }] }`) when Forge starts publishing it.

**Needs the TV:** the tab order and titles, the capsule art on the Consoles shelves (Steam's portrait URLs, then the
local cache), the Talk button, and that Home falls back cleanly if Steam changes its tab component.

### Home tabs: the registration point

`src/coreHomeTabs.ts` keeps a small registry so a module can add a Home tab without touching Home's internals:

```ts
registerHomeTab({ id: "club", label: locale => clubCopy(locale).tab, order: 30,
  render: props => <ClubTab {...props} />, isAvailable: clubAvailable, subscribe: subscribeClub });
```

`label` is a string or a function of the locale; `render({ locale, getLocale })` (or `component`) is what the tab
shows; `order` places it (brief 10, Consoles 20, Club 30, On this day 40; Steam tabs Core does not know come after). A
tab whose `isAvailable()` is false or throws is left out; `subscribe` tells Home when that may have changed; the returned
function unregisters it. The Club (`src/club/`, merged from `core-club`) registers this way from `coreHome.tsx` with
its `clubHomeTab` (shown once the Club is on and has data), and its line ("Two days left on this week's pick, sir.")
joins the Jarvis brief through `clubBriefLine()`. Its settings sit in Quick Access > Core next to News and On this day.

## Phase 3 notes: the game page

Left for phase 3: the game page keeps Steam's grey-blue content area in 3.3.3. Painting it black needs Steam's
app-details class maps (`appdetails`: the root, the play section and the inner content container) and care around the
header art, which must stay clear; that is not a one-line rule. For The Collector's ROM pages to match Steam's game
pages under one Core design, the shared hooks are: the same class maps found through `steamModules.ts` (The Collector
renders its own game detail UI, so it should reuse Steam's play-bar and tab classes where it can, or set
`data-core-ui-host` / `data-core-collection`-style attributes Core UI already styles), the `--core-radius-float` token,
the family tones (`CORE_FAMILY_TONES`) and the focus rules (gunmetal plus crimson, square).

## Screensaver

Core registers **Core Starfield** in Steam's screensaver list (`src/circlesScreensaver.ts`). From 3.3.3 it is also the
default, once: right after registration, if Steam's screensaver is its game-hero slideshow or nothing has been chosen,
and Core has not done this before (`core.ui.starfield-default` in Steam's localStorage), Core selects Core Starfield
through Steam's settings store setter (`src/coreScreensaverDefault.ts`; no config file is written). A user who had
already picked another screensaver keeps it, and the flag means a later choice (Steam's slideshow included) is never
overridden. A client without a known setter is left alone and tried again on the next start. Quick Access > Core > Look
> **Use Core Starfield as screensaver** selects it, or goes back to Steam's slideshow.

**Needs the TV:** which setter this Steam build offers (the console logs `[Core] Starfield screensaver default: applied`
or `unavailable`), and that the switch follows a choice made in Steam's own Settings.

## Turning it off

Quick Access > Core > **Look** > **Core UI** (on by default; `core.ui.enabled` in Steam's localStorage). Off removes
the stylesheet, the `data-core-ui` attribute, every Starfield canvas, the Jarvis orb, the controller-prompt pin, the
full-height stretch, the logo band, the Library's icons and tones, the plugin logos on the rail and Core Home's tabs at
once; Steam looks and behaves as without Core UI. The Steam menu entries, Jarvis's tab and the tap/hold combo do not
depend on it.

## CSS Loader

Core UI does not use CSS Loader and does not need it. If a CSS Loader theme is switched back on, Core UI wins on the
areas it styles: every selector starts with `html[data-core-ui]`, repeats its target class (Steam's class map value
twice) plus `:not(#core-ui-boost)` (an id's weight, 3.3.4), every declaration is `!important`, and Core UI's stylesheet
stays last in `<head>`, so Core UI's rules out-rank any class-based theme rule, `!important` included. The legacy Core
themes are switched off while Core UI is on (see "3.3.4" above). Areas Core UI does not style (the game page, Settings) keep the
theme. Enhanced Library and Enhanced Collection are no longer needed: Core UI draws the Library's icons itself. The old Core theme should stay off: its menu, Quick Access and context-menu files duplicate Core UI.

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
- `src/coreUiDom.ts` (clear layers, black chrome, the logo band, undoable inline edits), `src/coreFullHeight.ts`,
  `src/coreLibraryIcons.ts`, `src/corePluginLogos.ts`, `src/coreArtwork.ts`, `src/coreHome.tsx`, `src/coreHomeTabs.ts`,
  `src/coreHomeLocale.ts` (12 languages), `src/coreScreensaverDefault.ts` (3.3.3).
- `src/coreInputPrompts.ts`, `src/coreMenuEntries.ts`, `src/coreUiSetting.ts`, `src/coreUiLocale.ts` (12 languages),
  `src/CoreUiSection.tsx`, `src/CoreStars.tsx`, `src/jarvisPresence.ts`, `src/jarvisTabLocale.ts` (12 languages).

Tests (Linux and CI): `tests/coreUi.test.mjs` (selectors from fake Decky 3 and Decky 4 class maps, missing maps,
brand checks, context menus, full height, the Starfield, the Awesome Mix swap, the setting on and off, the orb, the
controller-prompt pin), `tests/coreUiPhase2.test.mjs` (3.3.3: full height on Steam-shaped fake DOMs, the logo band,
black chrome, clear layers, corners, tab and collection matching, tones, icon fallbacks, the decorator, plugin logos,
Home's tabs and registry, the screensaver default, frames per window), `tests/coreUiSnapshot.test.mjs` (3.3.4: snapshot
pruning, the size cap and scheduling, the selector table, stylesheet order, rail icons, tab, row, tile and capsule
builders, the Starfield's density, the brief's data, the legacy CSS Loader guard), `tests/test_core_ui_snapshot.py`
(3.3.4: the snapshot file, legacy themes off and back on, the AppData and icon folder choice),
`tests/test_core_icons.py` (the icon folder) and
`tests/coreJarvisTab.test.mjs` (tap/hold split, opening and closing the Jarvis tab, the tab
after Core's, the orb states, defaults, text).

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

## TV checklist (3.3.3)

Same setup, Core 3.3.3, Enhanced Library and Enhanced Collection off. Numbers follow the TV list.

1. Steam menu: the ring mark alone, small, top left; move to Home and up: the focus fill never covers the mark.
2. (4, 7) Quick Access: stars behind the whole panel, the tab rail, the title strip and every plugin; CSS Loader's tab
   has no grey area and its controls still work.
3. (5) Open the Steam menu: the stars drift. Close it: they stop (diagnostics `idle`). In a game: still.
4. (6) Top bar and hint bar pure black over the Library and Settings; clear over Home's hero art.
5. (8) No logo in Quick Access; "Core" as the title.
6. (9) Steam menu and Quick Access from the very top edge to the very bottom; clock and hints readable; a long Quick
   Access tab still scrolls and keeps focus.
7. (10, 11) Library: tab icons beside the labels, the family markers in their tones, the active tab's crimson underline;
   Collections view: icons and tone bars on the console tiles. With `%APPDATA%\GamingMode\icons\map.json` in place
   the user's icons replace Core's.
8. (12) Quick Access rail: Discord, Awesome Mix, Forge and The Collector show Core's logos; Awesome Mix's Steam menu
   entry shows its mark.
9. Corners: side panels, bars and focus square; context menus and tiles rounded 10px.
10. Home: Jarvis brief (greeting, last game, Talk opens Jarvis), Consoles shelves, On this day; no What's new or Friends.
11. Screensaver: after the first start, Steam's screensaver is Core Starfield; pick another in Steam, restart Core: it
    stays. The Look switch selects and deselects it.

## TV checklist (3.3.4)

Same setup, Core 3.3.4.

1. After start: `%APPDATA%\GamingMode\diagnostics\core-ui-snapshot.json` exists (reason `start`); open the Steam menu,
   Quick Access (a few tabs), a context menu, the Library (tabs, Collections) and Home: the file is rewritten with those
   surfaces. Quick Access > Core > Look > **Save Core UI snapshot** writes it at once and shows the path.
2. CSS Loader: the `Core` theme is off after start (its `config_USER.json` reads `"active": false`); Core UI off switches
   it back on; the user's other themes keep their state.
3. Steam menu: Core UI's look (ring mark only, gunmetal focus with the square crimson bar, crimson Home marker, black
   MENU footer), not the old theme's.
4. Quick Access: rail icons as large as Steam's own; the open tab gunmetal; Decky's plugin list gunmetal rows with the
   crimson left bar on focus, no white row, no grey-blue band under the title.
5. Library: one short underline per tab, only on focus (tone) and the open tab (crimson); the user's PB-line icons on the
   tabs (the snapshot's `icons.library.tabs` says `user-name`); collection tiles with the icon centred and the name below;
   smaller capsules with black gutters.
6. Home: the Jarvis brief hero (greeting, date, time, weather, Continue with art, the Club's pick, chips, orb, Talk).
