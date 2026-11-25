# Core brand: gunmetal and crimson

| Role | Colour |
| --- | --- |
| Tile / background | `#1A1D21` gunmetal |
| Ring, unlit | `#3A3F46` |
| Ring, lit; core centre | `#D7263D` crimson |
| Ticks; core | `#C9D1D9` steel |

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
- Chrome: vertical gradient `#F4F7FA` → `#AEB8C2` | `#5B636C` → `#C9D1D9`. Boot background `#0A0B0D`.
- Lettering: an upright, widely spaced sans. Before shipping, outline the text with an open-licensed font (Inter, SIL OFL) so it renders identically everywhere.
