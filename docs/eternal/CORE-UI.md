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

## 3.3.8: how class maps are found (Quick Access was unstyled)

The 3.3.7 snapshot from the TV showed `mapRoutes.quickaccess = "missing"` and `mapRoutes.gamepaddialog = "missing"`, so
every Quick Access rule was skipped (`qam-panel`, `qam-clear`, `qam-title`, `qam-tabs`, `qam-tab-selected`,
`qam-rail-icons`, `qam-tabs-footer`, `qam-footer-shadow`, `qam-controls`). Each matcher hung on one key Steam has since
dropped: `TabContentColumn` (Quick Access) and `FieldChildren` (dialog fields, the rows of Decky's plugin list).

Now every map is recognised by a **signature** (`CORE_UI_MAP_SIGNATURES` in `src/coreUiStyles.ts`): `all` (at most three
keys Steam has carried for years), `any` groups (at least one key of each; a renamed or dropped key has siblings that
stand in for it) and `none` (keys that rule out a lookalike). Rules that used to need one key take `needsAny`:
`qam-clear` needs any of `TabContentColumn`, `AllTabContents`, `ContentTransition`, `TabGroupPanel`; `qam-panel` any of
`Menu`, `QuickAccessMenu`, `Container`; `qam-tabs` any of `Tabs`, `TabsWithFooter`, `TabList` (and clears the new
`TabList` and `BottomTabs`). The Quick Access row targets also resolve under `QuickAccessMenu` and `Container`, and
`library-page` / `library-grid` no longer write a selector for an optional key that is missing.

When a map is still not found, Core walks every class map once and records the closest one (the share of the
signature's keys it has, which keys it lacks, and its key names only, never hashed classes) in the snapshot under
`coreUi.mapCandidates`, and logs `[Core UI] class map <name> not found; closest module (<n>%) lacks <keys>` once. The
next fix is one look at the snapshot.

The real key lists (Steam client, November 2026, read through Steam's CEF debugger) are test fixtures in
`tests/fixtures/steam-class-maps.json`, and `tests/coreUiMatchers.test.mjs` runs every matcher against them:

- Quick Access, webpack module 34544: `PopupBody, ViewPlaceholder, FullHeight, Expanded, QuickAccessMenuEmbedded,
  OSKActive, HeaderAndFooterVisible, IMEVisible, QuickAccessMenuEmbeddedContainer, Open, Title, Container, VR, OOBE,
  QuickAccessMenu, HeaderContainer, Menu, BottomTabs, TabList, Tabs, TabsWithFooter, Tab, Selected,
  ItemFocusAnim-darkerGrey, ItemFocusAnim-darkerGrey-nocolor, ItemFocusAnim-translucent-white-10, VoiceTab,
  ItemFocusAnim-green, Blocked, TabPanelHidden, FriendsTitle, FriendsListTabPanel, PanelOuterNav, PanelExitAnchor,
  TabGroupPanel, FooterBoxShadow, Footer, AllTabContents, ContentTransition, ActiveTab, Up, Enter, EnterActive, Exit,
  ExitActive, Down, KeyboardButton` (no `TabContentColumn`).
- Dialog fields, webpack module 2053 (the keys read; the module has more): `Field, FieldLabel, FieldDescription,
  FieldChildrenInner, FieldChildrenWithIcon, FieldLeftColumn, FieldRightColumn, Button, Toggle, ToggleRail,
  ToggleSwitch, DropDownControlButtonContents, QuickAccess-Menu` (no `FieldChildren`, no `DropDownControlButton`).

### Home in 3.3.8: Consoles and Club only

The user's call from the TV: Home's tabs are now **Consoles** (the default) and **Club**. The Jarvis brief tab (with
its hero) and the On this day tab are gone; Steam's What's new, Recommended and Friends stay removed. A remembered
active tab that pointed at a removed one (`WhatsNew`, `Recommended`, `Friends`) opens Consoles (`activeHomeTab`,
`RETIRED_HOME_TABS` in `src/coreHomeTabs.ts`). The On this day settings section left Quick Access > Core, and
`HomeNews.tsx` no longer wraps Home in `HistoryTabs` (`DailyHistory.tsx` stays in the tree, unused). `CoreBriefHero.tsx`
is deleted; `src/coreBrief.ts` (the brief's data, pure and tested) stays dormant for the next Home pieces. Jarvis keeps
his top-bar orb, his Quick Access tab and the listening pill. No UI text says "sir" any more (the greetings and the
Club's line); the forms of address stay in Jarvis's spoken and chat replies. The Consoles tab's redesign (every
console as a logo tile) comes in the next build.

Home's capsules (TV at 1348 x 758: the focused one grew out of its row and off the screen): Steam's capsules on Home are
drawn at `CORE_HOME_CAPSULE_SCALE` (0.8) inside their slots, with the crimson ring inside the capsule and no lift; Core's
own shelf tiles are 120 x 180 (were 150 x 225) with the ring inside. The Library keeps its own capsule rules.

### Home's tab row (3.3.8)

Home's tabs (Consoles, Club) are Steam's tabbed page, the same `tabbedpage` classes as the
Library, but the `library-tabs` rule is scoped to the Library, so Steam's pill (`rgba(255,255,255,.15)`, 64px radius)
still showed. The `home-tabs` rule gives them the Library's single marker (transparent tabs, steel text, one short
crimson underline on the open tab, no pill) over a clear row, so Home's stars show through it. Its scope is Steam's
tabbed page that holds Core Home's content: `CoreHomeTabs` wraps each tab's content
in `<div data-core-home-tab style="display: contents">`, and the rule targets
`{tabbedpage.GamepadTabbedPage}:has([data-core-home-tab])` (with `{gamepadui.BasicHome}:has(...)` as the fallback). The
Library's and a game page's tab bars are not touched.

### The TV's viewport (3.3.8)

Big Picture on the 4K TV runs at **1348 x 758 CSS px with devicePixelRatio 2.85**, not 1920 x 1080. Sizes Core sets in
CSS px were checked against it:

- Starfield density: `STARFIELD_AREA_PER_STAR` was chosen for a 400 x 1080 menu; at 758 tall the Steam menu drew about
  110 stars and a full page about 380. Now 1900 CSS px² per star: the menu (about 400 x 758) keeps its 160 and a full
  page reaches the maximum (420).
- Rail icons (30px in Steam's 48-56px rail cell), Library tab icons (24px), the tab marker (28 x 3px), the menu logo
  band (80px, 44px mark) and Jarvis's orb (12px): kept. They sit inside Steam's own CSS-px layout, which Steam already
  sizes for this viewport (close to the Steam Deck's 1280 x 800), so they keep their proportion to Steam's elements.
- The Starfield canvas is sized in CSS px, so at devicePixelRatio 2.85 its stars are upscaled (soft). Left as is for
  now; a device-pixel canvas is a possible follow-up.
- Jarvis's listening pill is drawn by the Gaming Mode agent (native, Windows DPI), not by Core UI's CSS.

### Starfield canvases can no longer outlive their page (3.3.8)

TV: after leaving a ROM page of The Collector, a black band with drifting stars covered the bottom half of every page
(Downloads, Settings, a Steam game page). The page host's canvas had stayed: The Collector removes its stylesheet when
its page closes, the element can stay in Steam's tree, and a canvas positioned against an element that is no longer its
containing block was free to sit anywhere on the screen. Now:

- a canvas is drawn only while its surface is on screen: the runtime sets `data-core-ui-live` when it paints it and
  removes it the moment the surface is off screen; without it the stylesheet hides the canvas (`display: none`);
- a plugin's page counts only while its plugin paints it (a transparent background means the plugin let it go);
- the host is checked on every pass to be the canvas's containing block (made `position: relative` if it went static),
  and a page host's canvas is never taller than its host (`max-height: 100%`, no viewport-tall fallback);
- every sync sweeps: a surface whose element left the page, or whose canvas left its element, is released, and any
  Starfield canvas that belongs to no live surface is removed; a MutationObserver runs the same sweep as soon as nodes
  are removed (a route change), not only on the one-second sync.

Tests (`tests/coreUi.test.mjs`): ROM page to another route leaves no canvas, layer or attribute; a page kept in the
tree but no longer painted has its canvas hidden and stays its containing block; orphaned and stray canvases go.

### Quick Access tabs no longer flicker (3.3.8)

TV: tabs kept appearing and disappearing, every tab carrying an inline `display: flex !important`. The cause was
`src/qamTabVisibility.ts`: it wrote `flex` or `none` on every managed tab, on every DOM mutation (its own style
writes and React's included) and every 500 ms, from a map `qamLayoutRuntime.ts` rebuilt from whichever tab array React
had rendered last, so two arrays gave two answers and the observer fought React. Now the map comes from the user's
hidden list alone (only `steam:<key>` entries), only hiding is ever written (a tab to show is left as Steam renders
it), Decky's tab (999), Core's tab, Jarvis's and the layout's plugin shortcuts can never be hidden, a hide applies only
to a node whose `aria-label` still matches the one first seen for that id (an id Steam re-assigns is left alone), and
mutations are coalesced into one pass per 120 ms, for added or removed nodes only. Tests: `tests/qamTabVisibility.test.mjs`
(protected ids, a re-assigned id, the debounce and no pass for style changes) and `tests/qamLayoutRuntime.test.mjs`
(the same answer for different render arrays). The Quick Access window's width (855 of 1348 px on the TV) is Steam's:
Core's full-height code writes only height, box-sizing and overflow.

### The top bar's search and Steam's dialog rows (3.3.8)

- `search-field`: the universal search inside `{header.Header}`, found as the header's element that holds the
  `<input>` (no hashed class): a square gunmetal field, steel text, a #9AA4AE placeholder, a steel magnifier, and on
  focus a 2px crimson underline with no glow; its height is the bar's own.
- `dialog-fields`: Steam's dialog field rows anywhere (`{gamepaddialog.Field}`; Settings rows, Downloads and other
  pages built from them): the focused row gunmetal with the square crimson left bar and a white label, no blue band.
  Quick Access's own row rules are more specific and still win there.

Not reached yet (their class maps are unknown on this client; their names are needed from the live client): the
search's results dropdown, Settings' left navigation and page background, the Downloads page's own panels, the in-game
overlay menu's variant of the Steam menu (Resume game, Controller settings, Notes, Game Recording, Exit game), the
controller layout viewer, and Steam's toggles (the rail's checked colour).

## 3.3.9: the game view, the Consoles tab and Steam's other pages

### Quick Access and the Steam menu are windows of their own (the 3.3.8 root cause)

The 3.3.8 snapshot found every map and applied every rule, yet Quick Access and the Steam menu stayed Steam's: its
`documents` listed only SharedJSContext, Big Picture (1348 px) and a 96 px "Menu". On this client Quick Access is a
BrowserView window titled `QuickAccess_uid21` (855 px wide) and the Steam menu `MainMenu_uid21` (240 px); no window store
lists them, so Core's stylesheet never reached them (the `qamPanel` that matched was Big Picture's `ViewPlaceholder`).
Now `steamUiDocuments` also takes every live BrowserView React holds (`browserViewDocuments` in
`src/onboardingAnchors.ts`, the discovery `qamTabVisibility.ts` already used), re-scanned at most every 3 s
(`POPUP_RESCAN_MS`) because Steam creates popups lazily and recreates them. Each popup gets the stylesheet, the
Starfield and the full-height logic, and the snapshot lists it under `documents`. A panel candidate counts as the
panel while it is under 70% of the screen's width (the widest Steam window), not the document's, since Quick Access
fills its own 855 px window. Tests: fake popup documents in `tests/coreUi.test.mjs`.

### Home after Art Hero (3.3.9)

The user's Art Hero options (Expanded Hero, Linear Gradient, no footer, carousel height 5, larger carousel font, font
height -4, cover and center) are Home's look now (`home-hero`, `CORE_HOME_ART`): the recent section is one screen
tall; the focused game's hero fills it at full brightness, `object-fit: cover`, with a fade over its last 12% only;
the carousel sits 50vh down in a 70vh row; the focused game's name sits 4em above its capsule at 22px; Steam's "Recent
games" header and the green play triangle are hidden. The tabs and shelves are below the fold, on black with the
Starfield. Capsules keep the 3.3.8 overflow fix (0.8 inside their slots, the ring inside).

### Favorites, Home's first tab (3.3.9)

Home's tabs are **Favorites** (the default), Consoles and Club (`HOME_TAB_ORDER` 10, 20, 30). Favorites shows the games
in Steam's own Favorites collection (`collectionStore.GetCollection("favorite")`, else the store's list), Steam games and
ROM shortcuts alike, read from the store on every render (never a copy), most recently played first, then by name
(`favoriteApps`). Each tile is big portrait art, six across at the TV's 1348 px (about 200 x 300): the game's library
capsule, or for a ROM shortcut its portrait grid image, where The Collector publishes its case art. No counts, no
last-played text; the crimson ring sits inside the tile; A opens the game view. Y to add or remove a favourite is left
out: Steam has no documented, simple call for it, and the game's Options menu already does it. Empty, the tab says
"Favorite a game in your Library to see it here" with the Options button's prompt (12 languages, `coreHomeLocale.ts`).
The first Home render after updating opens Favorites once, whatever tab Steam remembered (`activeHomeTab(..., migrate)`,
flag `core.home.favoritesDefault` in local storage); a remembered brief or On this day tab always opens Favorites.

### The top bar: fully transparent (3.3.9)

The top bar is transparent everywhere (over art and over black alike, `HeaderOpaque` included): no bar, no blur, no
gradient, never a Starfield host. Clock, icons and avatar are steel or white with one soft text shadow
(`CORE_HEADER_TEXT_SHADOW`, `0 1px 3px rgba(0,0,0,0.65)`) so they read over bright art. The search is transparent too:
the steel magnifier and text over a 1px steel underline, 2px crimson when focused; no gunmetal field.

### Class maps read live (2026-11-27)

The coordinator read the remaining surfaces from the live client through Steam's CEF debugger
(`steam-class-signatures-2026-11-27`). Their key lists (names only, partial) are fixtures in
`tests/fixtures/steam-class-maps.json`, and `tests/coreUiMatchers.test.mjs` checks each matches its own signature and
no other. New maps (`CORE_UI_MAP_SIGNATURES`):

| Map | Steam module (readable name) | Signature |
| --- | --- | --- |
| `appheader` | sharedappdetailsheader | all `TopCapsule`, `HeaderBackgroundImage`, `ImgSrc`; any `TopGradient`/`BoxSizer`; any `FullscreenEnterStart`/`FullscreenEnterDone` |
| `appdetails` | basicappdetailssectionstyler | all `AppDetailsContainer`, `PlaySection`; any `AppActionButton`/`ActionRow`/`AppButtons`; any `AppDetailsRoot`/`AppDetailsContent`/`AppDetailSectionList` |
| `appsection` | appdetailssection | all `AppDetailsSection`, `AppDetailsSectionContainer`; any `RightColumnSection`/`AppDetailsSectionHasLabel`/`Body` |
| `playsection` | appdetailsplaysection | `CloudStatusLabel` and `CloudSyncProblem` (from Clean Gameview's readable names; **not read live yet**) |
| `settings` | the paged settings dialog | any `PagedSettingsDialog_PageListColumn`/`PageListColumn`; any `..._PageListItem`/`PageListItem`; any `PageListItem_Title`/`..._PageList`/`PageList` |
| `toggle` | the small toggle map | all `Toggle`, `ToggleRail`, `ToggleSwitch`; `ToggleRow`; never `Field` (gamepaddialog has its own copy plus `On`) |
| `downloads`, `downloadbar` | the Downloads page; the bottom download bar | `DownloadsPage` + `SectionItem` + ...; `BottomBar` + `DownloadStatus` + ... |
| `overlay`, `configurator`, `configsummary` | the overlay popup; the controller configurator; its layout summary | `OverlayPopup` + `ControllerConfiguratorWrapper`; `ControllerConfiguratorMain` + `...GamepadPage`; `ControllerConfiguratorSummary` + `ControllerLayout` |
| `searchbar`, `searchresults`, `suggestions` | the top bar's search; the results page; the suggestions dropdown | `SearchBox` + `SearchFieldBackground`; `GamepadSearch` + `ResultsRow`; `Suggestions` + `Suggestion` |

The in-game Steam menu was read as the main menu's own map (`mainmenu` also accepted `AppRunningControls`/`NavigationColumn`).
That was wrong, and it is why the Steam menu stayed blue: 3.3.10 below.

### The game view (Steam's game page)

Steam's game page now matches The Collector's ROM page (Eternal-Collector `collector-game-view`,
`docs/architecture/rom-game-page.md`): the clean game view after Clean Gameview v1.8 Lz (SuchMeme and Leozito, MIT) with
the user's options (cover art, no gradient, locked logo, semi-transparent play bar, cloud as a button). Measures are
`CORE_GAME_VIEW` (`playbar` 80, `footer` 40, `logo` 120, `brightness` 0.6, `button` 48, `enterMs` 600), the same as
The Collector's `GAME_VIEW`.

- **Screen one** (`game-hero`, `game-playbar`, `game-cloud`): `TopCapsule` is `100vh - 120px` tall,
  `HeaderBackgroundImage` 100vh, `ImgSrc` cover at 60% brightness, `TopGradient` cleared, the logo (`BoxSizer`) locked
  bottom-left at most 120px. The play bar (`PlaySection`) is 120px with 40px of bottom padding over the footer and
  transparent (so is `AppDetailsRoot`, Clean Gameview's play bar background). Play (`AppActionButton`) is crimson with
  white text and a 3px white ring on focus; LAST PLAYED and PLAY TIME are Steam's own cells in steel; the square icon
  buttons (`AppButtons`' children: controller, cog; the cloud through `playsection`) are 48px black with a 2px crimson
  edge on focus; the cloud's status text is hidden (cloud as a button).
- **The fullscreen enter** (Play pressed): Clean Gameview animates `TopCapsule` from screen one's height to the full
  screen. Core does it as a transition on `TopCapsule`'s height (`FullscreenEnterActive`, `FullscreenEnterDone` and
  `FullscreenExitStart` are 100vh, `FullscreenExitActive`/`Done` screen one's height), so the art grows instead of
  jumping; transitions out-rank `!important` in the cascade, so Core UI's rules keep their weight. Still under reduced
  motion.
- **Screen two** (`game-screen-two`, `game-sections`): the container (`AppDetailsContainer`) stays clear; Steam's tab
  row takes the Library's single marker over a clear row; the section cards (`AppDetailsSectionContainer`,
  `RightColumnSection`) are gunmetal with the 10px float radius, raised with a 2px crimson outline on focus. The black
  and the stars come from the `game-page` Starfield host.
- **The `game-page` host**: `AppDetailsContainer`, a page host like The Collector's (`viewport`: the canvas covers only
  the part on screen) with two 3.3.9 options. `below` starts the stars under the play bar (`{appdetails.PlaySection}`),
  so the transparent play bar over the art never gets any. `exclude` (`.romm-core-page`) makes a container that holds a
  ROM page not this host: The Collector's page is its own host. Every 3.3.8 guard applies (live only while on screen,
  never taller than the host, swept on removal), and `tests/coreUi.test.mjs` checks stars below the play bar, the ROM
  page exclusion and that leaving the page leaves nothing.
- **Down to screen two** (`coreUi.ts`, `watchGameView`): when focus moves from the play bar into the rest of the page,
  the page's scroller scrolls so screen two's tab row lands just under the top bar (`GAME_VIEW_TOP_INSET`, 40px); back up
  into the play bar, it scrolls to screen one. A ROM page is left to The Collector, which does the same. Screen two has
  no "Steam" or "Collector" chip.
- **What is not touched**: non-Steam shortcuts get the same view (they are Steam game pages); a ROM page gets the same
  header rules from both The Collector and Core (identical values); VR titles' pages are ordinary game pages, and
  SteamVR's own UI is not Big Picture's document, so Core UI never reaches it.

**For Forge (its Mods tab, a separate build).** On screen two, Steam's lower area is the element with
`{appdetails.AppDetailsContainer}` (basicappdetailssectionstyler's `AppDetailsContainer`), the `game-page` host. Inside
it, below the play bar (`{appdetails.PlaySection}`), Steam draws its tabbed page: the tab row is
`{tabbedpage.TabRow}` (`Tab`, `TabTitle`, `Selected`; the same `tabbedpage` map as the Library) and each tab's content
is a list of `{appsection.AppDetailsSection}` cards (`AppDetailsSectionContainer`, right column `RightColumnSection`).
A Mods tab belongs in that tab row (Steam's tabs prop of the tabbed page inside `AppDetailsContainer`, not a sibling
DOM node), so it takes the Core marker and the black-and-stars background with no rule of its own; its content should
use gunmetal cards with `var(--core-radius-float, 10px)` like Steam's sections. These positions come from the class maps
and Clean Gameview; confirm them with the Core UI snapshot on a Steam game page before building on them.

### The Consoles tab

Every console in the library is a tile (board 1, `src/coreConsoles.ts`): each Steam collection Core recognises as a
console (RomM's included; `matchCollection`), one tile per console (the collection with the most games when there are
several), by family order (PlayStation, Nintendo, Sega, Xbox, retro) then name. Eight across as on the board (206 x 150
at 1920 px, 20px apart), scaled to the view (about 145 x 105 at 1348 px). A opens that console's collection
(`/library/collection/<id>`). Focus is a 3px crimson ring inside the tile.

**Art: the user's ColorGrade pack** (personal, no licence: never committed, bundled or used in tests). The tile is the
console's **white logo on gunmetal with the family's tone bar**: at TV size a logo reads at a glance, where the pack's
dimmed hero art turns to noise behind a small tile, and each hero or square icon is one to three megabytes where a logo
is tens of kilobytes. Without a pack logo, the tile shows the console's name. Families are in
`%APPDATA%\GamingMode\icons\colorgrade\families\<family>\`; platforms are imported by the backend
(`quick_settings/colorgrade_pack.py`, `get_colorgrade_pack`) when `...\colorgrade\platforms` is missing, from
`%USERPROFILE%\Downloads\colorgrade-icons-heroes-logos.zip`: every `.../by_platform/<slug>/{icon,hero,logo}.png`
(83 slugs in the user's copy) to `...\colorgrade\platforms\<slug>\`. Only plain slugs and those three names are
written; the new copy replaces the old one only when complete; a broken zip keeps what was there. Quick Access > Core >
Look has **Re-import icon pack**. RomM or collection names map to pack slugs through Core's collection keys plus the
pack's own names (`packSlugFor`: `ps1` to `psx`, `3ds` to `n3ds`, `megadrive` to `genesis`...), the first slug the pack
has wins. Tests: `tests/coreConsoles.test.mjs`, `tests/test_colorgrade_pack.py` (generated zips only).

### Steam's other pages

Black pages, gunmetal rows with the square crimson focus bar, steel text, no Steam blue:

- `settings-pages`: Settings' page list column and content black; list items steel, the active one gunmetal with the
  crimson bar, the focused one raised. `toggles`: the rail raised gunmetal, crimson when on (`On`), a white knob.
- `downloads-page`, `download-bar`: the Downloads page black, items gunmetal with the bar on focus, Steam's hover ring
  off, progress crimson on raised gunmetal; the bottom download bar black.
- `ingame-menu`: the Steam menu while a game runs, its three columns black, rows gunmetal with the bar on focus, the
  current game's background dimmed.
- `overlay-configurator`: the overlay popup and toolbar, the controller configurator and its layout summary black; the
  toolbar, Back to game, Exit game and configurator buttons gunmetal rows with the bar on focus.
- `search-pages`: the search field by its own map (the 3.3.8 structural `search-field` stays as the fallback), the
  results page and suggestions dropdown black with gunmetal rows.

These are built from class maps only; the hashed values were not seen on screen. Check each on the TV (below).

## 3.3.10: TV fixes after 3.3.9

Seven items came back from the 3.3.9 TV pass with four photos and a snapshot taken on Home. Items 1 to 6 are below.
Item 7 (the Consoles redesign) and a larger, lower Home row became a new Home, built from the design the user approved
(boards Main, HomeConsolesPage, HomeAchievements, HomeClub and QAMDecky of the "Core: current vision" canvas): "Home in
3.3.10" and "Quick Access: the rail on the right", after these six.

### 1. The Steam menu matched the wrong class map

Root cause, found live through Steam's CEF debugger: the `mainmenu` signature (all `Menu`, `ItemIcon`, `ItemLabel`;
any of `ActiveDot`, `FooterBoxShadow`, `AppRunningControls`, `NavigationColumn`) also matched the in-game menu's module,
and the search answered that one first. Every menu rule wrote `.s1FCUcjnu-HHPONgHERgk`, a class not on screen; the real
menu in the `MainMenu_uid2` popup is webpack module 56461 (`Container`, `Menu`, `ItemOuter`, `Item`, `Active`,
`ItemIcon`, `ItemLabel`, `ActiveDot`, plus keys only it has: `MainMenuEmbedded`, `mainMenuCloseDurationMS`,
`RunningAppIcon`, `CurrentUserAvatar`, `RunnningAppSeparator`, Steam's spelling). The key list 3.3.9 recorded as
`mainmenu` was the in-game module's.

- `mainmenu` now needs one of the Steam menu's own keys; the in-game menu is its own map, `ingamemenu` (all
  `AppRunningControls`, `NavigationColumn`; never `mainMenuCloseDurationMS` or `MainMenuEmbedded`), and the
  `ingame-menu` rule reads it. The menu's outer `Container` (and `MainMenuEmbedded`) is black too, and the item Steam
  marks `Active` (the page you are on) has no fill of Steam's, only the crimson dot.
- **No first match wins silently.** A signature may name `prefer` keys (keys only the right module has). After the
  first search, one walk over every class map collects every module that matches each found signature
  (`allCoreUiMatches`). When there are two or more, the one with the most `prefer` keys wins, then the one carrying the
  most of the signature's keys; an equal fit keeps the first. The choice is logged once (`[Core UI] class map <name>: N
  modules match; took the one with <keys>`) and recorded in the snapshot under `coreUi.mapAmbiguity` (per map:
  `matches`, `switched`, the chosen module's unique keys and each passed-over module's; key names only). Audited:
  `quickaccess`, `footer`, `header`, `gamepaddialog` and `settings` got `prefer` keys; every other map falls back to
  the signature-key count.
- Fixtures: `mainmenu` (56461) and `ingamemenu` (the old list) in `tests/fixtures/steam-class-maps.json`.
  `tests/coreUiMatchers.test.mjs` checks that the old signature matched both, that the new ones match one each in
  either search order, that a lookalike carrying one unique key loses to the real module, and that every live map
  records a second matching module.
- Popup windows: the TV now titles them `QuickAccess_uid2` and `MainMenu_uid2` (not `uid21`). Nothing reads the number
  (popups are found through React's BrowserView owners, never by title); the tests use `uid2` and `uid7`.

### 2. Quick Access rail icons line up

Live geometry: each tab (`{quickaccess.Tab}`) is 48 x 64; Steam's svg icons are 24px centred by a 20px 12px margin,
plugin icons a 30 x 30 `span` pinned to the tab's top (about 17px high), off the selected fill. Now (`qam-rail-icons`)
every tab is a flex box that centres its child both ways with no padding; its direct child has no margin; every icon
(Steam's direct `svg`, a plugin's `span`/`div` holding an `svg` or `img`, Core's `[data-core-rail-icon]` glyph) is
`CORE_RAIL_ICON_PX` = 24px square (was 30) with no margin, static, untransformed. The selected fill is the whole
square tab; any fill Steam draws in the selected tab's `::before`/`::after` is cleared.

### 3. Settings and the other unstyled screens

The snapshot found every map (`mapRoutes` all `classic`, `mapCandidates` empty), so the remaining blue was not a missing
map but a wrong one, as with the Steam menu. `settings` matches both the paged settings dialog and an older module
without the `PagedSettingsDialog_` prefix; the prefixed module is preferred now. `settings-pages` also paints the
responsive variants (`ResponsivePageList`, `ResponsiveActivePage`) and the page lists black, the title white, separators
gunmetal and disabled items hint grey. New: `settings-text` (map `settingstext`: `SettingsDialogSubHeader` in steel small
caps, body text steel, descriptions hint grey, links crimson text `#EA5A68`, Settings' own buttons gunmetal with the
crimson bar on focus) and `steam-dialogs` (Steam's unhashed `.GenericConfirmDialog` and
`.ModalPosition_Content .DialogContent`: gunmetal with the float radius and a raised edge, their buttons raised gunmetal).
The two rules that need no class map (`corners`, `steam-dialogs`) never make a stylesheet on their own.

### 4. No focus ring on capsules

`capsule-focus` no longer draws a ring (TV: it sat offset from Fight Night's tile, in Steam's accent colour). On the
Library and on Home a focused capsule has no outline and no box shadow, nor do its pseudo-elements or children; Steam's
own focus lift shows focus. Core's lift on Library capsules is gone too. Home keeps the 3.3.8 overflow fix (capsules at
0.8 inside their slots). Core's Favorites tiles lift 6px when focused, with no ring.

### 5. Home's rows have their own space

Causes (photo 1, Favorites): screen one's carousel row was moved down 50vh *relatively* inside a 70vh box, so it reached
120vh, over whatever came next; the hero art had a *minimum* height of one screen, so it could run on under the next
rows; and Steam pins Home's tab row (its wrapper is absolute at the tabbed page's top), which Core had made clear, so
tiles scrolled under it showed through.

- `home-hero`: the recent section is exactly one screen (`height`, `min-height` and `max-height` 100vh) and its own
  stacking context (`isolation: isolate`, `z-index: 0`); the art is exactly 100vh; the carousel row is out of the flow
  (`position: absolute`, left and right 0, `top` 50vh, at most 50vh tall, sized by Steam). `CORE_HOME_ART.carouselHeight`
  is gone. The row's size and position are unchanged otherwise: a lower, larger row waits for the approved design.
- `home-tabs`: Home's tab row wrapper (`{tabbedpage.TabHeaderRowWrapper}` under the Home tab page) is in the flow
  (`position: relative`), so the tab content starts below it and scrolls with it. No fill is added; the Library's tab
  row is untouched.
- `home-layers` (new): Home's tabbed page is `position: relative; z-index: 1` over the hero, in the flow after screen
  one. The Collector's family shelf, the other row that stacked, left Home altogether in Collector win.36 (it no longer
  patches `/library/home`), so nothing of Core's targets it.
- Screen one itself is Core's own since the approved Home (below); the `home-hero` rules stay for Steam's row, the
  fallback.
- Favorites tiles never show an empty box (`src/coreFavoriteArt.ts`): the tile tries Steam's portrait lookups
  (`GetCustomVerticalCapsuleURLs`, ..., now also `GetVerticalCapsuleURLForApp`, which knows the new hashed library-cache
  folders), then the local cache; when no portrait loads, it draws the game's hero (at 70% brightness) with its logo on
  top, else the hero with the name, else the name over a gunmetal-to-black gradient. Hero and logo come from Steam's
  lookups when the client has them, then `customimages` and `assets/<appid>/library_hero.jpg` / `logo.png`.

### 6. The top bar slides out past the first screen

The top bar stays fully transparent at the top of a page. Once the page under it has scrolled past its first screen it
slides up and fades out whole (`data-core-ui-header="hidden"`: opacity 0, `translate: 0 -100%`, no pointer events,
200ms, `CORE_HEADER_HIDE_MS`; instant under reduced motion), and it comes back near the top. The line is the viewport
less two bar heights (at least 80px: 678px on the TV's 758px viewport), with 48px of hysteresis so a small scroll at the
line does not flicker it (`headerHidden`, pure). The runtime listens to `scroll` once per document (capture) and counts
only page scrollers (vertical, at least 60% of the window tall), so a shelf scrolling sideways never moves the bar; every
sync re-checks, and a route change that removes the scroller brings the bar back. No partial background is ever drawn.

## Home in 3.3.10 (the approved design)

Board coordinates are 1920 x 1080; the TV is 1348 x 758 CSS px, so every board measure is scaled by about 0.702 (Core
writes them in viewport units, `HOME_ROW` in `src/coreHomePages.ts`).

### Two pages, two screens each

Home has two pages, **Home** and **Consoles**, named at the top left as `L1 HOME CONSOLES R1` (the open one white with a
3px crimson underline) and switched with L1 and R1 while focus is on screen one (`pageForButton`; no wrap). Each page is
two screens. **Screen one** is one screen tall: the focused item's art fills it with a dark fade toward the bottom
(`rgba(0,0,0,.35)` at the top, clear from 22% to 45%, `.85` at 82%, black at the bottom), and a row sits on the footer.
**Down** jumps the page fully to **screen two**, like the game view; **B** anywhere on screen two, or **up** from its top
row (Home's tab row; the Consoles page's games), jumps straight back to the tile focus left, with the same scroll; on
screen one B stays Steam's (`watchHomeScreens` in `coreUi.ts`, `backToScreenOne`). The top bar stays transparent, its
clock back on the right while Home is shown (the "clock on the left" option yields the left to the page names), and the
search field leaves the bar on Home (`home-pages`); past screen one the bar slides away (fix 6).

L1 and R1: nothing of Core's binds them on Home (Jarvis's combo is L5+R5; the bumpers of Quick Access's Core tab are
its own). Steam's own Home tab row does use them while focus is inside it (its "L1"/"R1" hints): that is screen two,
where they keep switching Favorites, Achievements and Club. Core's handler sits only on screen one (`onButtonDown` of
its `Focusable`), so the two never meet.

How it is drawn: Core's one `/library/home` patch (`HomeNews.tsx`) recognises two of Steam's elements by their props in
the same rendered output: the tabbed page (`strActiveTab`, `setActiveTab`) becomes Core's tabs (`CoreHomeTabs`), and the
recent games section (`autoFocus`, `showBackground`, the contract storeInstallRecents.tsx also uses) draws Core's screen
one (`CoreHomeScreen`, `src/coreHomeScreen.tsx`) with Steam's own list of recent games, read from its carousel's
`games` (`recentGamesIn`). One patch, not two: a second patch would wrap the first one's wrapper on every pass and break
the route's stable identity (`tests/homeNewsLifecycle.test.mjs`). Without Steam's list, with Core UI off or on any
error, Steam's own row is drawn (inside an error boundary).

### Screen one's row

| | Board (1920 x 1080) | TV (1348 x 758) |
| --- | --- | --- |
| Home, focused tile (portrait 2:3) | 208 x 312 (28.9% tall) | 146 x 219 |
| Home, other tiles (dimmed to 82%) | 181 x 272 (25.2%) | 127 x 191 |
| Consoles, focused tile (landscape 16:9) | about 400 x 225 (20.8%) | 281 x 158 |
| Consoles, other tiles (dimmed) | about 340 x 191 (17.7%) | 238 x 134 |
| Gap / left inset | 22 / 64 | 16 / 45 |
| Bottom of the row | 28 above the footer | 62 from the bottom (42 px footer + 20) |
| Focus lift | 10 | 7 |

No game or console name, count or status line anywhere (Steam's own title and playtime are hidden in its fallback row
too); no outline: the focused tile is larger and lifts. The full-screen art follows focus only after it has rested on a
tile for 150 ms (`HERO_DELAY_MS`) and crossfades over 250 ms (`HERO_FADE_MS`; none under reduced motion); the art of the
two tiles either side is loaded ahead (`neighbours`, `HERO_PRELOAD`).

- **Home:** Steam's recent games in Steam's order, each its portrait (`coreFavoriteArt.ts`: Steam's lookups, then the
  local cache; without one, its hero with its logo). The background is the focused game's hero. A opens the game.
- **Consoles:** one landscape card per console, most recently played first (a console never played after, by name;
  `consoleCards` in `coreConsoles.ts`), from the per-console Steam collections The Collector keeps (`PlayStation 1` is
  the PS1 since Collector 0.33; its family collections `PlayStation`, `Nintendo`, `Sega` and `Retro` are never consoles:
  families appear nowhere on Home and stay Library tabs). Each card is the console's ColorGrade hero with its white logo
  centred; A opens the console's collection. **Without ColorGrade art** (the pack has no Xbox 360 or PS4, for example)
  the card is gunmetal `#1A1D21` with the console's logo, else its icon (the user's Library icon set, else Core's own
  drawing), else its name: never an empty box; focused, the background is black with the Starfield. Heroes come one per
  call (`get_colorgrade_hero(slug)`, `read_hero` in `quick_settings/colorgrade_pack.py`: a plain slug the pack has,
  `hero.png` only, at most 8 MB), one at a time, the focused console's and its neighbours' first, kept as blob URLs
  (`wantConsoleHeroes`, `coreConsolePack.ts`). The pack is read in place on the PC and never stored by Core.
- **The club's picks on Consoles:** if the club's current Game of the Week or Game of the Month is in the library, it
  leads the row at the console cards' size: the game's hero (else its box art) with a small square tag at the top in the
  pick's colour (Game of the Week `#8F7CB0`, Game of the Month `#B07A84`); focused, its art fills the background; A opens
  the game page. The console holding that game carries the same tag. At most those two; nothing when neither is owned;
  from the Club's own data (refreshed hourly by `clubStore.ts`; no extra fetch; `consolePicks`).

### Screen two

- **Home:** Steam's tabbed page with Core's tabs **Favorites**, **Achievements**, **Club** (`HOME_TAB_ORDER` 10, 20, 30;
  Consoles is no longer a tab, and a remembered Consoles tab opens Favorites).
  - **Favorites:** a portrait grid, eight across (206 x 309 on the board, about 145 x 217 on the TV): Steam's Favorites
    and The Collector's Favourites collection together, each game once (`collectorFavorites`: `[Favourites]`,
    `RomM: [Favourites] (<host>)`, `Favourites`; never Steam's own or a user's `Favorites`).
  - **Achievements** (`CoreAchievementsTab.tsx`, `coreAchievements.ts`): "Recent unlocks" on the left (icon, name,
    game, when; a source tag, Steam raised grey or RetroAchievements amber `#C98B4A` with black text; a rare Steam
    unlock's "6.1% of players" or an RA "Hardcore" in crimson text) and "Closest to 100%" on the right (crimson bars).
    Steam's come from the client: `SteamClient.Apps.GetMyAchievementsForApp` for the eight most recently played Steam
    games, and its achievement progress cache for the nearest to 100%. RetroAchievements' come only through The
    Collector's read-only bridge `window.__eternalCollectorAchievements` (Collector win.36; version 1;
    `getRecentUnlocks(limit)` and `getClosestToComplete(limit)`, each a promise of `{ ok: true, items }` or
    `{ ok: false, reason }`; limits clamped to 1 to 50; an `appId` of null makes a row not actionable). Every failure
    (`not_configured`, `auth_failed`, `rate_limited`, `server_unreachable`, `backend_error`, `bridge_error`,
    `bad_request`), an absent bridge, an unknown version or shape means Steam's alone, quietly; a bridge that registers
    later is read on its `eternal_collector_achievements_ready` event. The tab draws at once from the last model it
    showed (`core.home.achievements.v1` in local storage) and refreshes in the background, every ten minutes while open:
    it never waits on the network to draw. Rarity: each game's global percentages are cached with the time read
    (`core.home.achievementRarity.v1`, at most 120 games), read again at most every 24 hours per game, and filled in as
    they arrive. A opens the game's achievements.
  - **Club** (`club/ClubTab.tsx`): the four picks as tall full-width cards (4 across, 560 px on the board, 51.9vh): the
    box art fills each card and the details sit over a bottom fade (the tag in the pick colour, title, platform and
    length, days left and beaten count, the "In your library" chip). Below: **Log completion (X)** and **Open in Library
    (Y)**, both acting on the focused pick (the buttons, or X and Y on a pick). Then three equal boxes: Your standing
    (rank and beaten count, the top three), Coming up, Nominations. No refresh button and no "saved data" line: it
    refreshes itself hourly. Focus starts on the first pick; left and right move across the picks and across the boxes;
    down goes picks, buttons, boxes; every box is focusable; focus lifts, no outline.
- **Consoles:** the focused console's recently played games as a portrait row (`ConsoleScreenTwo`), under the label
  "Recently played" (no console name), and nothing else.

**Empty states**, one line of hint `#9AA4AE` text where the content would start, in Core's 12 languages: Favorites
"Star a game to add it here"; Achievements "Unlock an achievement and it shows up here"; Club "The club couldn't be
reached. It'll try again within the hour."

## Quick Access: the rail on the right (3.3.10)

The approved board puts the icon rail on the right of the panel, the content to its left. `qam-rail-right` reverses the
row of `{quickaccess.Menu}` (the rail and the content column are its children; the 3.3.9 snapshot shows the rail at x 0
and the content at x 48); the rail's gunmetal divider moves to its left edge. Fix 2 stays: centred 24px icons and the
full square fill.

The D-pad follows the picture whichever way Steam's navigation reads a row (`src/coreQamRail.ts`, one capture listener
for `vgp_onbuttondown` per Quick Access document): in the rail, left goes into the content (where focus last was there,
else the item level with the open tab) and right goes nowhere; in the content, right from its right edge goes to the
open tab; a slider (`role="slider"`, `aria-valuenow`, `input[type=range]`) keeps left and right; everything else is
Steam's. A move is a DOM `focus()` after `preventDefault()` and `stopPropagation()`, as the On this day cards already
do. Decky's plugin panel and the Jarvis tab are laid out inside the content column, so the reversal does not touch them.

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
  tiles, Core Home's tiles). Capsules and art keep Steam's own radius. Capsules and game tiles have no focus ring
  (3.3.10): Steam's own focus lift, or Core's 6px lift on its own tiles, shows focus.
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
- **L5+R5, Jarvis's only combo (3.3.5):** a **tap** (released within 300 ms) talks without opening anything (listening
  ends on a pause, another tap, the 30 s cap or a mute; the game stays on screen); a **hold** opens Quick Access
  straight to the Jarvis tab with the input focused, or closes it, also during a game, and cancels the recording that
  began on the press. L4 and R4 are A and B and are never in a combo; dictation and the screen helper are buttons on
  the Jarvis tab. Desktop: Gaming Mode > Jarvis > *Hold: open Jarvis in Quick Access*. The listening pill (live input
  bars, "Listening", then "Thinking...", then the answer card): [JARVIS.md](JARVIS.md).
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
shows; `order` places it (Consoles 20, Club 30; Steam tabs Core does not know come after). A
tab whose `isAvailable()` is false or throws is left out; `subscribe` tells Home when that may have changed; the returned
function unregisters it. The Club (`src/club/`, merged from `core-club`) registers this way from `coreHome.tsx` with
its `clubHomeTab` (shown once the Club is on and has data). Its line for the brief (`clubBriefLine()`, "Two days left on
this week's pick.") is dormant since the brief left Home in 3.3.8. Its settings sit in Quick Access > Core next to News.

### The Club tab (Play Retro Games Club)

The backend (`quick_settings/club_feed.py`, `ClubFeed`) reads three public pages of playretrogamesclub.com: `/`,
`/nominations/` and `/leaderboards/` (plus one member page for "Your standing"). Politeness: robots.txt
first and obeyed (Crawl-delay included), a full refresh at most once an hour (3.3.8; was 6 hours) and on demand at most once per
10 minutes, doubling backoff after failures, Retry-After honoured, one page at a time with a pause, a bounded body,
User-Agent `Core/<version> (+contact: club member)`, no cookies or sign-in, cached to disk.

**Parsing (3.3.6).** The first parser was written without the real pages and matched nothing on the PC
(`nominations.categories not found`). It now reads the site's own markup, captured on 2026-11-27 and kept verbatim
(trimmed) in `tests/fixtures/club/` with a README describing it. Decky's frozen Windows Python has no `html.parser`,
so a small regex-built DOM (`parse_dom`) stands in for it.

| Page | Read from | Snapshot field |
| ---- | --------- | -------------- |
| Home | four `a.room.<w\|m\|r\|s>` cards (and `x` for a special): `.tag`, `h3`, `.plat` ("NES · about 2.8 hours"), `.facts` (beaten, at 100%, rating), `.first` lines, `.ends <time datetime>` | `picks` |
| Home | `ol.cal-strip` items: `<time datetime>`, `strong`, event `.tag`s, the closing/until `<time>` | `schedule` |
| Home | "Nominations" `.noms li` (`.tag` + `.num`); "Top players" `ol.list` (`.rn`, `a.pname`, `.num`); `ul.totals` | `nominations` fallback, `top` fallback, `stats` |
| Nominations | each `section.run.<event>#gotm\|rhotm\|gots`: `.tag`, `h2` period, "Vote opens `<time>`", `.stat b` count, `table#nomt-<id>` rows (`data-sys`, `data-s-order`, `data-s-hours`, `data-s-name`, the `retroachievements.org/game/<id>` link, `/img/<id>.png`, `a.pname`) | `nominations` |
| Leaderboards | `table#board-all-all` rows: rank, `a.pname`, `data-s-total`/`-hardcore`/`-completed`/`-mastered`/`-firsts`/`-firsthundred`/`-hours`, `.lb-counts` per event | `leaderboard`, `top` |

The output shape is the one the tab already used; 3.3.6 only adds fields: on picks `event` (Assemble's code: `gotw`,
`gotm`, `rhotm`, `gots`, `special`), `cover`, `at100`, `rating`, `hours`, `first_clear`, `first_100`; on nominations
`event`, `period`, `closes_at` and per row `order`, `ra_game_id`, `cover`, `nominator_slug`; on leaderboard rows `hours`
(and the full stats and `by_event` on `top`); on schedule items `title`, `tags`, `until`; and `carried` on the snapshot.
3.3.8: a pick that is not in the library shows the club's own box art (`CLUB_SITE_ART_ALLOWED = true`, the user's call;
only `https://www.playretrogamesclub.com/img/<id>.png|jpg|webp`, from `image` or `cover`), falling back to the platform
badge when there is none or it fails to load; Steam's art still wins for a matched game. The tab refreshes on its own:
hourly in the background (`clubStore.ts`, `CLUB_BACKGROUND_MS`) and on opening the tab, the backend deciding whether its
copy is old enough; the "Updated N minutes ago" line is gone (only "Showing saved data" when the copy is stale).

**Structure flags.** Each flag is true only when its section was parsed: `home.playing_now`, `home.picks`,
`home.top_players`, `home.schedule`, `home.nominations`, `home.stats`, `nominations.categories`, `leaderboards.rows`
(and `member.found` with a display name). A change logs one warning naming the missing flags. A page without picks never
replaces the cache; a section whose flag is false keeps its last good data (listed in `carried`), except nominations,
where the home page's current counts stand in first. A parser exception keeps the whole cache (`parse_error`). The
member page is still read by visible text: no capture of it exists yet.

**Logging a completion through Assemble.** Eternal-Assemble (the Discord plugin) publishes
`window.__eternalAssemble = { version: 1, openClubCompletion(req) }` and fires `eternal_assemble_ready` when it
(re)loads. `req` is `{ event: "gotw" | "gotm" | "rhotm" | "gots" | "special", title, raUsername? }`; the answer is
`{ success: true, opened: "thread" | "channel" }` or `{ success: false, reason, message }`. With the bridge present, the
"How to log a win" panel shows one **Log completion** button per current pick (not ended); it asks Assemble to open the
pick's Discord thread with `/completed` ready, then shows "Opened in Assemble" or Assemble's own message. The user picks
the options and sends it there: Core never sends anything, and Assemble sends nothing automatically. Core does not pass
`raUsername` (the club display name is not known to be the RetroAchievements username). Without Assemble, the panel keeps
the manual steps. Code: `src/club/clubAssemble.ts` (bridge detection, version check, result validation; never throws).

**Needs the TV:** the Log completion buttons' focus order with a controller, Assemble opening the right thread, and the
failure line when Assemble is closed or signed out.

## Phase 3 notes: the game page

Left for phase 3: the game page keeps Steam's grey-blue content area in 3.3.3. Painting it black needs Steam's
app-details class maps (`appdetails`: the root, the play section and the inner content container) and care around the
header art, which must stay clear; that is not a one-line rule. For The Collector's ROM pages to match Steam's game
pages under one Core design, the shared hooks are: the same class maps found through `steamModules.ts` (The Collector
renders its own game detail UI, so it should reuse Steam's play-bar and tab classes where it can, or set
`data-core-ui-host` / `data-core-collection`-style attributes Core UI already styles), the `--core-radius-float` token,
the family tones (`CORE_FAMILY_TONES`) and the focus rules (gunmetal plus crimson, square).

The Collector already draws its ROM page in the Core design (Eternal-Collector, `src/utils/corePageStyles.ts`) and, from
3.3.7, Core gives that page the animated Starfield (below).

### Hosts from other plugins (3.3.7)

A cooperating plugin can have its own black page take the Core Starfield, as Home and the Library do. The contract:

- **Root class.** The plugin gives the page's root element a fixed class that Core lists in `CORE_UI_HOSTS`
  (`src/coreUiStyles.ts`) as `{ id, selector, minArea, page }` instead of a Steam class map (`map` + `key`). The
  selector must be plain class names only (`resolveHostSelector` refuses anything else). Today: The Collector's ROM game
  page, `{ id: "collector-rom-page", selector: ".romm-core-page", page: true, minArea: 0 }`.
- 3.3.11: Awesome Mix's page is a page host too, `{ id: "awesome-mix-page", selector: ".awesome-mix-core-page", page: true, minArea: 0 }`,
  so it gets the starfield the same way.
- **Attribute.** While Core UI is on, Core sets `data-core-ui-host="1"` on that element and puts its canvas
  (`canvas[data-core-ui-stars="collector-rom-page"]`) first inside it, behind the content. Core UI off (or the page
  closing) removes both. A plugin keys off the attribute: The Collector shows its still fallback field only under
  `html[data-core-ui] .romm-core-page:not([data-core-ui-host="1"])`, so the animated field replaces it, and the page is
  plain black without Core UI.
- **The plugin paints the page**, black, and keeps any art outside the element (The Collector's hero art is Steam's
  header above it). Core paints nothing else on it. The canvas covers only the part of the element on screen
  (`viewport`: `--core-ui-scroll` is how far the element's top is above the screen, `--core-ui-page-height` the visible
  height), so stars never reach past the element and never go behind the art.
- Such a host is never full height and never a panel (no stretch, no clear layers), is held still under reduced motion
  and while a game runs like every page host, and is used only when Core UI found Steam's class maps.
- Diagnostics: `window[Symbol.for("core.ui.diagnostics")]().hosts` counts the elements carrying each host's canvas
  (`{ "collector-rom-page": 1 }` on a ROM page). The snapshot lists every host under `coreUi.hosts` (`id`, `selector`,
  `source`: `steam` or `plugin`, `kind`) and records the page as the surface `collector-rom-page`, without labels.

Another plugin adds a line to `CORE_UI_HOSTS` (and its target to `CORE_UI_TARGETS` for the snapshot); nothing else in
Core changes.

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
  dropdown, Big Picture context menu, dialog fields and the phase 2 maps) by a signature of keys (3.3.8, above), and
  builds the stylesheet. A rule whose map or class
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
`tests/test_core_icons.py` (the icon folder), the 3.3.7 plugin host tests in `tests/coreUi.test.mjs` and
`tests/coreUiSnapshot.test.mjs` (The Collector's page found by its class, the attribute on and off, the canvas only over
the visible part, reduced motion, the snapshot's host list) and
`tests/coreUiMatchers.test.mjs` (3.3.8: every matcher against the real key lists in `tests/fixtures/steam-class-maps.json`,
no matcher hanging on one key, every Quick Access rule applying on today's client, the closest-candidate record, Home's
tab row) and
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
6. Button hints: press a key on a keyboard, move the mouse, dictate from the Jarvis tab: the hints stay A / B / ≡ in the
   controller's own style. With an Xbox controller they are Xbox glyphs; with the Steam Controller, Steam's.
7. Jarvis: the orb sits after the clock (dim). Tap L5+R5 and ask something: it pulses while listening and thinking,
   then shows a crimson ring for 4 s. Hold L5+R5: Quick Access opens on the Jarvis tab with the cursor in the field; hold
   again: closes. In a game: hold opens Quick Access over it; the orb is hidden in the game unless Jarvis is busy.
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

## TV checklist (3.3.7)

Same setup, Core 3.3.7, The Collector with the Core game page (`collector-core-look`).

1. Open a ROM's game page: the black part below the hero art has drifting stars (diagnostics `hosts` shows
   `collector-rom-page: 1`, `state` `moving`); the hero art itself has no stars over it.
2. Scroll down the page: the stars stay behind the whole visible black area and do not slide over the art.
3. Core UI off (Quick Access > Core > Look): the page is plain black, with no stars; on again: the animated
   field is back. With reduced motion, the stars hold still.

## TV checklist (3.3.8)

Same setup, Core 3.3.8.

1. Quick Access: black panel with stars, clear title strip, the rail clear with a thin gunmetal divider, the open tab
   gunmetal (no grey-blue), Core's rail icons full size. Decky's plugin list: gunmetal rows, the crimson left bar on
   focus. The snapshot's `coreUi.mapRoutes` shows `quickaccess` and `gamepaddialog` found, `coreUi.skipped` has no
   `qam-*` rule, and `coreUi.mapCandidates` is empty.
2. Home: only Consoles (open by default) and Club; no brief, no On this day. The tab row has no pill: steel labels,
   one short crimson underline under the open tab, stars visible behind the row. No "sir" anywhere on screen.
3. Library: tabs unchanged from 3.3.7 (black row, one marker). A game page's tabs keep Steam's look.
4. The Steam menu has about as many stars as on a 1080p screen; full pages are denser than in 3.3.7.
5. Open a ROM in The Collector, then go to Downloads, Settings > System and a Steam game page: no black band, no stars
   over the page content (the snapshot's diagnostics `hosts` has no `collector-rom-page`).
6. Quick Access: the rail stays put for a minute (no tab appearing or vanishing); no tab carries an inline
   `display: flex`; Decky's, Core's and Jarvis's tabs are always there.
7. The top bar's search: square gunmetal field, grey placeholder, crimson underline when focused, no blue.
8. Settings: the focused row gunmetal with the crimson left bar (the left navigation may still be Steam's).
9. Home: capsules smaller; the focused one stays inside its row and the screen. Club: box art on every pick (or the
   platform badge if an image fails); no "Updated" line; it refreshes on its own.

## TV checklist (3.3.9)

Same setup, Core 3.3.9.

1. A Steam game page: the art fills the screen above the footer, no top gradient, the logo bottom-left (at most 120px);
   the transparent play bar with crimson Play (white ring on focus), LAST PLAYED and PLAY TIME, square black icon
   buttons with a crimson edge on focus; the cloud as a button. Press Play: the art grows to full screen smoothly.
2. Press down: screen two is black with stars from the play bar down, never over the art; tabs with the single crimson
   underline; section cards gunmetal with rounded corners. The snapshot's diagnostics show `hosts.game-page: 1`.
3. A ROM page (The Collector) looks the same as before and has no Core `game-page` canvas; a non-Steam shortcut's page
   looks like a Steam game's; leaving any game page leaves no stars behind.
4. Home opens on Favorites: big portrait art of the user's favourites, most recent first, the ring inside the tile, A
   opens the game; empty, the hint with the Options prompt. Home > Consoles: one tile per console, eight across, white logos on gunmetal with the family bar (names where the
   pack has no logo); A opens that console's games. Quick Access > Core > Look > Re-import icon pack answers with the count.
5. Settings, Downloads, the in-game Steam menu, the controller configurator and search: black, gunmetal rows, crimson
   focus bar, no blue. Anything still blue: save a Core UI snapshot (its `mapCandidates` names the closest map).
6. Quick Access and the Steam menu are styled at last: black with stars, Core's rows and tabs. The snapshot's
   `documents` lists `QuickAccess_uid*` and `MainMenu_uid*` with `coreStyle: true`.
7. Home: the focused game's art fills the screen at full brightness, the carousel low with the game's name above it, no
   "Recent games" header, no green triangle; scroll down for the tabs and shelves on black with stars.
8. The top bar is transparent on every page, the search too (steel underline, crimson when focused); the clock and
   icons readable over bright art.
9. A Steam game page: down from the play bar lands on screen two with its tab row at the top; up returns to screen one.

## TV checklist (3.3.10)

Same setup, Core 3.3.10.

1. The Steam menu (and the menu while a game runs): black with stars, the ring mark top left, steel rows, the focused row
   gunmetal with the crimson left bar, the current page's dot crimson; no Steam blue anywhere, the area around the rows
   included. Core's log has `[Core UI] class map mainmenu: 2 modules match; took the one with ...` (or no line when only
   one matched), and a saved snapshot's `coreUi.mapAmbiguity` says which module was taken.
2. Quick Access: every rail icon, Steam's and the plugins' (Discord, Awesome Mix, Core, Jarvis), the same size and centred
   in its tab; the selected fill is the full square tab and the icon sits in its middle.
3. Settings: the page list and page black, the open page gunmetal with the crimson bar, sub-headers steel small caps,
   toggles crimson when on; a confirm dialog (for example Exit Big Picture) gunmetal with rounded corners. Anything
   still blue: save a snapshot (`mapAmbiguity`, `mapCandidates`).
4. Home and the Library: no ring or box around a focused capsule (Fight Night included); Steam's own lift shows focus. A
   focused Favorites tile lifts with no ring.
5. Home > Favorites, scrolled down: the tab row above the grid (not over it), nothing of The Collector's shelf behind the
   grid, the shelf in its own row between screen one and the tabs. Street Fighter 6 (or any game without portrait art)
   shows its hero with its logo, or its name, never an empty box.
6. Scroll Home past the first screen: the clock, weather, search and icons slide away; back to the top, they return.
   A shelf scrolled sideways never hides them. The same on the Library past its first screen.
7. Home opens on the Home page: `L1 HOME CONSOLES R1` top left, the clock on the right, no search field; the focused
   game's art fills the screen; the row sits just above the footer (focused about 146 x 219, others 127 x 191), no name
   or playtime anywhere; the art changes only when focus rests, with a short crossfade.
8. Down: the page jumps to Favorites / Achievements / Club. B, or up from the tab row: straight back to the tile.
   L1/R1 on screen one switch pages; on screen two they switch Steam's tabs.
9. R1: Consoles. The club's owned Game of the Week / Month first (tagged), then consoles most recently played first,
   each its ColorGrade hero with its logo; Xbox 360 / PS4 (no pack art) gunmetal with a logo, icon or name, the
   background black with stars. A opens the collection; down: that console's recent games.
10. Achievements: Steam's rows and (with The Collector win.36) RetroAchievements'; rarity fills in after the first
    visit; the tab draws at once on the next. Club: four tall cards, X/Y on the focused pick, three focusable boxes,
    focus starting on the first pick. Empty tabs: one grey line.
11. Quick Access: the rail on the right; left from the rail enters the content, right from the content's edge returns to
    the open tab; sliders still move with left and right; Decky's list and the Jarvis tab look as before.

## 3.3.11

### Class maps: duplicate modules are merged

Steam ships some class modules twice or three times with identical keys and different hashes (the 3.3.10 snapshot:
`mainmenu` 2, `settings` 3, `overlay` 2). Core used to pick one, and the one on screen was often the other: the Steam
menu and Settings stayed in Steam's colours. Now every module that matches a map equally well is merged
(`mergeMaps`): a key holds every distinct class, `|`-separated, and `classSelector` writes `:is(.a.a,.b.b)`. The
snapshot's `mapAmbiguity` records `merged`. The separate `ingamemenu` map is gone: the in-game keys live in the main
menu's module. The light blue in the photos was not a light theme (CSS Loader had nothing active).

### The game page: what matches

The 3.3.10 snapshot of a Steam game page (route `/library/app/3595230`) shows Core's maps do match; it recorded no
game-page surface only because the snapshot had no game-page entries (now `game-hero`, `game-playbar`,
`game-screen-two`, `game-section`). Seen live:

| Part | Map key | Class on the TV (this Steam build) |
| --- | --- | --- |
| Page host (`game-page`, Starfield) | `appdetails.AppDetailsContainer` | `_2-ns_GqXzymHk0jsXBBWJ1` |
| Page root (Clean Gameview's play bar background) | `appdetails.AppDetailsRoot` | `_3fp5y6tdU77MdrCQZt_aS9` |
| Screen two's tab page | `tabbedpage.GamepadTabbedPage` inside the host | `_3IBLc81yyL08OJ7rfKtF00` |
| Screen two's tab bar | `tabbedpage.TabRow` | `_19z_50-qWZWxcgXfWNtSWb` (row wrapper `_2JobstxtZVYF3078DblN2M`) |
| Screen two's tabs | `tabbedpage.Tab` / `Selected` / `TabTitle` | `_3eEbSktrstBdLk0dVpnKVI` / `_3Gp1bACHx__POxmy6Gd3kG` / `_1nq0ibcuVEbRv0z_wk9pIQ` |
| Play section | `appdetails.PlaySection` | `_3scbHORkYB7utTUGfkMCC_` (Clean Gameview; not in the snapshot) |
| Play button | `appdetails.AppActionButton` | `QsZdWtHTlIK9KIKbscNTt` (Clean Gameview) |
| Section cards | `appsection.AppDetailsSectionContainer`, `RightColumnSection`; `appdetails.GameInfoContainer`, `GameInfoQuickLinks`, `GameInfoCollections` | read at runtime |

Use the selectors Core resolves at runtime (the snapshot's `selectors` and `hosts`), never the hashes: they change
with Steam builds. Two maps are new: `appview` (the page's outer view: `HeaderLoaded`, `InnerContainer`,
`AppDetailsOverviewPanel`, `ScrollContainer`, `Glassy`, the module @decky/ui's `appDetailsClasses` finds) and
`actionbutton` (`PlayButtonContainer`, `PlayButton`, `Green`, @decky/ui's `appActionButtonClasses`).

### The game view, Steam and ROM alike

The Collector draws its play bar with Steam's own classes (`ActionRow`, `ActionButtonAndStatusPanel`, `AppButtons`,
`StatusAndStats`, `GameStatsSection`), so one set of rules styles both pages; its own parts are reached by its fixed
classes (`.romm-game-view`, `.romm-tab`, `.romm-core-card`). No Collector change is needed.

- `game-actions`: Play / Install crimson with white text, square, 48px tall and at least 200px wide (Steam's
  `PlayButton` and its green state, The Collector's `.romm-native-play` / `.romm-btn-play`); the stats right after it
  (hint label over a steel value); the icon buttons square black 48px right after the stats (the row is
  `justify-content: flex-start`, nothing grows).
- `game-still-art`: no header video; the logo pinned bottom-left (2.8vw).
- `game-view-clear`: Steam's tinted panels clear (`Glassy`, the light blue under the description and behind screen
  two; the overview panel, the scroll container, the content and the section list; screen two's tabbed page inside
  the game page). Steam's page is black under them; The Collector's page paints its own.
- `game-info-cards`, `game-sections`: gunmetal cards, the crimson focus bar.
- `rom-screen-two`: The Collector's tabs without Steam's focus box; the crimson underline marks the open tab; gunmetal
  cards and chips.

### Home

- Screen one ends 100px above the bottom (`HOME_SCREEN_CUT_PX`: the 42px footer and screen two's 58px tab names), so on
  screen one exactly screen two's tab names show, right above the footer; the art still fills the screen. Screen two's
  tab content starts 42px lower so nothing else peeks. The row is a little smaller (focused 27%, others 23.5%; consoles
  19.5% / 16.5%), 1.6% above the tab names: 137 x 205 focused on the TV, 119 x 178 the others, bottom 112px.
- Core's tiles have no focus ring (`noFocusRing`, and no `::before`/`::after`).
- The Home row holds games only (`isHomeGame`): Steam's games, demos and mods; tools (SteamVR), applications and
  launcher or emulator shortcuts (by name) go. VR games stay.
- Options on any game tile Core draws (the Home row, club picks, Consoles' recently played, Favorites, Achievements)
  opens Steam's own game context menu (`coreGameMenu.ts`: the component Steam's `LibraryContextMenu` module exports,
  shown with DFL's `showContextMenu` for the app's overview).
- A on a console opens its collection through Steam's own route builder, looked up once in Steam's modules
  (`lookUpCollectionRoutes`; only short one-argument functions whose source builds a collection path are called);
  `/library/collection/<id>` is the fallback. Screen two ends with an *All* tile that opens the same collection.
- Club picks match owned games whether installed or not (both of Steam's app lists are read).

### Quick Access

- The rail sits at the right edge of the part of the Quick Access window that is on screen: Steam's window is 855px
  wide with about 350px visible on the TV. `measureQamVisible` reads the main window's Quick Access placeholder (its
  left edge against the main window's width), else the window's own `screenX` against the screen; the Menu takes that
  width (`data-core-qam-visible`, `--core-qam-visible`). Rail icons without a fill use `currentColor`.
- Full height: Steam lays each popup window over its `ViewPlaceholder` in the main window, inset by the top bar and the
  footer (top 40px, bottom 40px; the Quick Access window measured 855 x 679 on a 758 screen). `panels-full-height`
  sets both placeholders (Quick Access and the Steam menu) to the full height; a Quick Access window that comes out
  full height gets a 40px top safe area (`data-core-qam-full`). Popup windows sit above the main window, so the top bar
  and footer never draw over them. Not verified on the TV: whether Steam re-reads the placeholder when it sizes the
  window (TV checklist).
- **Core is one list** (the QAMCore board, `ControlCenter.tsx`): the Core ring and "Core" as the title; *Quick*, always
  open: Display (current output), Sound (current output), Performance (its power and profile controls) and Power
  (Sleep, Restart, Shut down: Steam's own `SuspendPC` / `RestartPC` / `ShutdownPC`), each opening its picker under it;
  then sections that open and close with A and keep their state (`expandedSections`, ids `group/*`): Sound (mixer,
  devices), Display & graphics (display, frame timing, shaders, NVIDIA, Radeon, Lossless Scaling), Controller,
  Downloads (only while a store download runs), Look (Core UI, the Starfield screensaver, *Back buttons and trackpads in
  menus*, snapshot, icon pack, the top bar date), System (device and details, the agent and diagnostics, the old Core
  page's sections, stores, Quick Access shortcuts, replay the introduction).
- Gone from this tab: the inner icon row and the gear (the tab editor), the Decky sub-tab (Decky keeps Steam's own
  rail tab: Core no longer hides it or hosts Decky), News and Club (Club lives on Home).
- Settings saved before 3.3.11 (`layout` missing) migrate once: the group holding the tab that was open starts open,
  and a group whose old tabs were all hidden starts closed. The old ids were swapped against their icons ("audio" was
  Video, "performance" was Audio); `TAB_GROUP` maps them. Order and hidden tabs stay saved for older builds.

### The Steam menu

- Friends & Chat is left out while Core UI is on, told by its route or React key (`friends`, `chat`), never its
  label. It is dropped from the menu's item list before React renders it (`StoreDigitalMenuRegistry.ts`, registry
  implementation 5: an entry may `hide` native items), so focus runs from the entry above to the one below with no
  gap. The SteamMenu board's row list is therefore: Home, Library, Discord, Awesome Mix, Store, Media, Downloads,
  Settings, Power.

### The context menu

A solid gunmetal panel, the float radius, no outline; the crimson focus bar stays. It is no longer a Starfield host
(inside a page host it was marked nested and went transparent). The upstream plugins' entries read *Core Metadata* and
*Core Artworks* (`coreLabels.ts`: exact text nodes in open context menus, and the store's titles); the plugins' IDs and
`plugin.json` names are unchanged. ThemeDeck and TrailerHero keep their names.

### Back buttons and trackpads in menus

This Steam version has no Big Picture controller layout, so outside games the UI hears only the standard buttons.
`coreMenuInput.ts` reads the controller through `SteamClient.Input.RegisterForControllerStateChanges` (as the Jarvis
combos do):

- L4 is A and R4 is B: Steam's focus navigation is told the button went down and up (`OnButtonActionInternal(down,
  1 | 2, 1)`, the call Steam's own input makes). L5/R5 stay Jarvis.
- Both trackpads move the Windows pointer with trackpad-style acceleration; a pad click is a left click. Deltas are
  batched (one call in flight, about 60 a second) through the backend (`core_pointer_input`, one kept-alive loopback
  connection) to the agent's `POST /input/pointer` (token-checked, refused under anti-cheat safe mode;
  `MenuPointerInput.cs`, `SendInput`). The right pad's lower half as a right click was left out (one click bit per
  pad).
- Only outside games: while a game runs (the overlay over it included) nothing is read or sent, and a held click is
  released. The agent's cursor auto-hide shows the pointer on movement and hides it after 2.2s idle; it never hides it
  for good.
- Setting: Look > *Back buttons and trackpads in menus* (on by default, `core.menuInput.enabled`).

### Snapshots

Each capture is its own file, `%APPDATA%\GamingMode\diagnostics\core-ui-snapshot-YYYYMMDD-HHMMSS.json` (local
time; the newest 20 kept; the old single `core-ui-snapshot.json` is left alone). Quick Access > Core > Look > *Save Core
UI snapshot* shows the new path.

### Hosts

Awesome Mix's page (`.awesome-mix-core-page`) is a Starfield page host (above). The context menu is no longer a host.

## TV checklist (3.3.11)

1. Steam menu and Settings: black, gunmetal rows, crimson focus, no Steam blue. Friends & Chat gone; focus moves from
   the entry above it straight to the one below.
2. Steam menu and Quick Access fill the screen top to bottom (if not, Steam ignores the placeholder's height: note it).
   Quick Access's first row sits below the clock's height.
3. Quick Access: the rail on the right edge of what is visible, every icon steel (none black). Core tab: the ring and
   "Core"; Quick rows open their pickers; Power's three buttons work; sections open and close with A and stay that way
   after reopening Quick Access; left/right never jump anywhere unexpected; Decky's own rail tab is there.
4. Downloads appears between Controller and Look only while a store download runs.
5. Home: on screen one the three tab names show fully just above the footer, nothing else of screen two; no grey ring
   on the focused tile; no SteamVR or launchers in the row; Beat Saber stays.
6. Options on a Home tile, a Favorite, an Achievements row, a club pick and a Consoles recently-played tile opens
   Steam's game menu.
7. Consoles: A on a console opens that console's collection (not All Games); the All tile on screen two too.
   Uninstalled club picks show.
8. Context menu: solid gunmetal panel, rounded, no outline; "Core Metadata" and "Core Artworks".
9. Steam game page and ROM page, screen one: crimson Play/Install, the same size and place; stats right after; square
   black icon buttons right after the stats; logo bottom-left; no video.
10. Screen two on both: black with stars, gunmetal cards, crimson underline on the open tab, no white outline box on
    The Collector's tabs, no light-blue panel under the description.
11. Back buttons and trackpads (no game running): L4 selects, R4 goes back; both pads move the pointer smoothly, a pad
    click clicks; the pointer hides again after a few seconds. Start a game: the pads and back buttons do what the
    game's layout says, Core sends nothing. Turn the setting off: nothing happens outside games either.
12. Save Core UI snapshot twice: two files with timestamps; the path shows under the button.
