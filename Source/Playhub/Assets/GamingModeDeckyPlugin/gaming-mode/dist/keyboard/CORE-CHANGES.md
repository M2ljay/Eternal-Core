# Swipe typing in Core: VaporType's keyboard

These files are VaporType's keyboard runtime, shipped by Core's Decky plugin for swipe typing on Steam's on-screen
keyboard (Quick Access > Core > Swipe typing; docs/eternal/BATCH-2.md).

- Upstream: <https://github.com/dmitrii-galantsev/VaporType>, version 0.3.2, commit
  `53b499c6351d821ab2a3cd3f857387540e70fb4c` (2026-11-18).
- Code: MIT, Copyright (c) 2026 VaporType contributors (`LICENSE`, kept in full with its word-data notices).
- Word data: `swipe/words.js` and `swipe/lm.js` are CC BY-SA 4.0 (wordfreq data), with the SCOWL notices; sources
  and the changes VaporType made are in `swipe/DATA-LICENSES.md` and `swipe/SCOWL-COPYRIGHT.txt`. Core did not
  change the data.

| File | From | Core's change |
| ---- | ---- | ------------- |
| `patch.js` | `keyboard/src/patch.js` | One hook: `coreLookup()` reads `window.__coreOskLookups` (set by Core's plugin, `src/swipeTyping.ts`) for Steam's trackpad-typing settings store and the keyboard's CSS class map, found with Core's Decky 3/4 helpers (`src/steamModules.ts`). When the hook has no answer, VaporType's own search runs unchanged. |
| `modes.js` | `keyboard/src/modes.js` | none |
| `swipe/engine.js` | `keyboard/src/engine.js` | none |
| `swipe/words.js`, `swipe/lm.js` | `keyboard/data/` | none |
| `swipe/DATA-LICENSES.md`, `swipe/SCOWL-COPYRIGHT.txt`, `LICENSE` | upstream | none |

Not taken: VaporType's backend self-update (`main.py`: GitHub release check, download, `fcntl` lock; `src/updates.ts`).
Core updates these files with Core. Its loader (`src/keyboard.ts`) became Core's `src/swipeTyping.ts`, and the
learned-words storage moved into Core's `main.py` (`core_typing_*`), keeping VaporType's file names and its
"forget everything" bookkeeping.
