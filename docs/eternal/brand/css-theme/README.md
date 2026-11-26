# Core CSS Loader theme

`Core/` is the CSS Loader theme that gives Steam Big Picture the Core look. Copy the folder to
`%USERPROFILE%\homebrew\themes\Core`, reload CSS Loader and switch **Core** on.

- Steam menu: the Core wordmark aligned with the icons, the panel on AMOLED black with two faint layers of drifting
  stars (Eternal's space look; still under reduced motion), a gunmetal selected item with a
  3px crimson line.
- Quick Access: black tab column and header, gunmetal selected tab with a crimson bar, gunmetal header buttons.
- Notifications: black with a crimson edge and steel text.

Every `inject` entry names its windows (`SP`, `MainMenu`, `QuickAccess`): with an empty list CSS Loader injects the
file nowhere. Selectors use Steam's older hashed (`._23IDiI-F_FxXotrgpY71r8`) or readable module names
(`.quickaccessmenu_Tab`), which CSS Loader rewrites through `css_translations.json` for the running Steam build. An
exact-match attribute selector (`[class="…"]`) is not rewritten, so it is not used.

Colours: crimson `#D7263D`, steel `#C9D1D9`, gunmetal `#1A1D21`, black `#000000`.
