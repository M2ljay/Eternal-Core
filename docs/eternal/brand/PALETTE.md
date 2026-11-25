# Core brand: gunmetal and crimson

| Role | Colour |
| --- | --- |
| Tile | `#1A1D21` gunmetal |
| Ring, unlit | `#3A3F46` |
| Ring, lit; core centre | `#D7263D` crimson |
| Ticks; core | `#C9D1D9` steel |
| Dark backgrounds (boot, curtain, full-screen) | `#000000` AMOLED black |

- `core-logo.svg`: the mark (ring half lit, "powering up"), on its tile. App icon, installer, store.
- `core-logo-mark.svg`: the same without the tile, for dark screens.
- `core-logo-charged.svg`: ring fully lit, for "on / ready" states.
- `core-logo-wake.svg`: the wake-up animation, played after Eternal's: the ring charges from empty, the chrome ticks turn like a lock dial, click into place and light, the chrome core pulses once and "CORE" appears in the same spaced lettering as "ETERNAL" (respects reduced motion).

Chosen 2026-11-25. Crimson echoes Iron Man, gunmetal and steel echo Doctor Doom's armour: the two helmets that sit under the TV.

## Brand architecture

Like Sony and PlayStation: **Eternal** is the maker, **Core** is the platform.

- On screen the platform is "Core"; where a maker is named (About, credits, update notices), it is "Core by Eternal".
- Eternal's first-party plugins: The Collector (games), Awesome Mix (music), Forge (mods), Assemble (Discord).
- Marks (Mark I, II, …) are Core's system software versions.
- Boot sequence: the Eternal logo, then Core's wake-up animation.
- Still needed: an Eternal maker logo/wordmark, distinct from Core's ring.

## Eternal (maker) logo

- `eternal-logo.svg`: the still mark. Chrome spaced capitals "ETERNAL" over a short crimson bar with a key cut hanging from its centre.
- `eternal-wake.svg`: the boot animation. A long grey line charges crimson in from both ends, meets at the centre and drops into the key cut (which reads as an "E"), then glints; that glint hands over to Core's wake-up.
- Chrome: vertical gradient `#F4F7FA` → `#AEB8C2` | `#5B636C` → `#C9D1D9`. Boot background AMOLED black `#000000`.
- Lettering: an upright, widely spaced sans. Before shipping, outline the text with an open-licensed font (Inter, SIL OFL) so it renders identically everywhere.

## App icons (Eternal's first-party plugins)

Gunmetal tile, one chrome symbol, one crimson accent: `icons/the-collector.svg` (cartridge with a D-pad label),
`icons/awesome-mix.svg` (cassette), `icons/forge.svg` (anvil and sparks), `icons/assemble.svg` (channel `#` with a ping).

## Boot sound

Chosen: **arcade** (2026-11-25). `sound/make_boot_sound.py <style>` synthesises it (numpy + scipy, all original) in time
with the two animations: two rising tones closing in; four lane-switch blips as the key goes in; a bumper pop that
drops in pitch, with two rebounds, on the E's glint (2.9 s); then Core's rising hum and dial detent clicks, the lock
thump (6.6 s) and a warm D add9 swell as CORE appears. Other styles kept for later: `key`, `tumbler`, `vault`.
`sound/eternal-core-boot.ogg` is the chosen render. `preview/index.html` plays the sequence with any of them.


## Dark backgrounds

Every dark background is AMOLED black `#000000` (decided 2026-11-25): the boot sequence, the launch curtain and any
full-screen dark surface. Gunmetal stays for tiles, cards and icons.
