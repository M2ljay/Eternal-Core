# Core CSS Loader theme

`Core/` is the CSS Loader theme that gives Steam Big Picture the Core look. Copy the folder to
`%USERPROFILE%\homebrew\themes\Core`, reload CSS Loader and switch **Core** on.

- Steam menu: the Core wordmark aligned with the icons, the panel on AMOLED black with two faint layers of drifting
  stars (Eternal's space look; still under reduced motion), a gunmetal selected item with a
  3px crimson line.
- Quick Access: black tab column and header, gunmetal selected tab with a crimson bar, gunmetal header buttons.
- Notifications: black with a crimson edge, white titles and icons, steel text.
- Screens: four options, each **On** by default and each can be switched **Off** on its own (useful when one looks
  wrong on the TV). Solid black everywhere except where a panel sits over game art; no stars on these screens.
  - **Context menus** (`contextmenu.css`): black panel with a 1px gunmetal edge, gunmetal selected item with the Steam
    menu's 3px crimson underline, gunmetal separators, steel header. Items Steam marks destructive are steel, and
    crimson text when selected.
  - **Home** (`home.css`): black page, hero art fading into black, no glow behind the focused game, section titles in
    steel small caps, focused game with a 3px crimson ring lifted 6px (no lift under reduced motion).
  - **Game page** (`gamepage.css` + `tabs.css`): hero art kept and fading into black, solid crimson Play with a white
    ring when focused, play bar and Steam's "glassy" panels in translucent gunmetal with a blur, panels lower down
    solid near-black (`#0E1013`), steel small-caps section headers, tabs as in the Library.
  - **Library** (`library.css` + `tabs.css`): black grid, tabs flat in steel with the open one gunmetal and a crimson
    underline (a focused tab also gets a thin white ring), group and collection headers in steel small caps, focused
    cover with the crimson ring and lift as on Home.
- Tidy-ups (option, off by default): crimson toggles, sliders, progress bars and selected play/download buttons; a
  clear top bar; no Home labels, Library counts, "new" banners or input icons on covers; a still focus ring; a round
  avatar; a tighter storage page. They replace ten single-job themes, written fresh for Core.

Every `inject` entry names its windows (`SP`, `MainMenu`, `QuickAccess`): with an empty list CSS Loader injects the
file nowhere. Selectors use Steam's older hashed (`._23IDiI-F_FxXotrgpY71r8`) or readable module names
(`.quickaccessmenu_Tab`), which CSS Loader rewrites through `css_translations.json` for the running Steam build. An
exact-match attribute selector (`[class="…"]`) is not rewritten, so it is not used.

Colours: crimson `#D7263D`, steel `#C9D1D9`, gunmetal `#1A1D21`, black `#000000`.

`check_classes.py` checks that every module class in the theme's CSS exists in CSS Loader's `css_translations.json`
(on the PC: `%USERPROFILE%\homebrew\themes\css_translations.json`):
`python -I check_classes.py <css_translations.json> Core`. A class that is listed is translated for the running Steam
build; whether that element is on the screen the rule aims at can only be checked on the TV.
