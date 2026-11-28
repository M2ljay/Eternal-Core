# Events and Canon Events — agreed specification

Naming: the massive tier is called **Canon Events** (a nod to Spider-Man: Across the Spider-Verse, where a canon event
is a fixed moment that must happen); everyday ones are plain **events**. The Look switch reads "Canon Events".

Status: approved by the owner on 2026-11-28; not built yet. Build after 3.3.12. Design each massive event on the
"Core: current vision" canvas before building it (Christmas first).

## What an event changes

Only accents, never layout or the base design rules (AMOLED black, crimson, square panels, no glow):

- starfield variant (e.g. snowfall in December, fireworks at New Year, tinted stars for a film)
- boot screen / startup animation variant, optionally with a short music cue
- glow / accent colour
- Jarvis greeting line
- Home hero art and countdowns (see tiers)

Events switch themselves on and off by date. One switch in Quick Access > Core > Look, labelled "Canon Events", turns events off.

## Tiers

**Canon Events (massive)** — individually designed, multi-day, with a countdown on Home in the run-up:

- Christmas and Halloween: full seasonal treatment (own starfield, boot screen, Home look, Jarvis lines, multi-day
  run-up).
- Every Avengers film and every Spider-Man film: countdown before release, then a full themed takeover for release week
  (e.g. Doctor Doom green for Avengers: Doomsday, Spider-Man red and blue). Confirm release dates before building.
- Music: a Lil Uzi Vert release is the biggest possible event. A Drake release is the only one that compares (one notch
  below Uzi). Countdown, release-day takeover of Awesome Mix's home page, Core-wide theme for the week, Jarvis announces
  it when it drops, the album pinned first in the speed dial until the owner unpins it.
- Large game releases: countdown, pre-load reminder, the game's art takes over Home's hero on launch day.

**Events (standard)** — every other holiday or release: an accent, a Jarvis greeting, and "Out now" in the speed dial for music.

## Overlap rules

1. Each event owns its own surfaces: music events own Awesome Mix; film and game events own Core's look. Two same-day
   events both run in full (e.g. Uzi + Doomsday: Doom look everywhere in Core, Uzi's album as the Home hero that day,
   Awesome Mix fully Uzi, Jarvis mentions both).
2. A release-day event outranks a season's look, but they blend rather than erase (Christmas snow keeps falling, tinted
   Doom green; festive touches and the Christmas Club pick stay).
3. Christmas Eve and Christmas Day always win over any other event. A film week resumes on the 26th.
4. Film events end no later than 7 days after release.
5. Countdowns stack on Home (e.g. "Doomsday in 3 days · Christmas in 9").

## Sources

- Standard holidays are built in.
- The owner's own events come from a dedicated "Core Events" calendar (Google or iCloud; the calendar may be named "Canon Events") edited from his phone; Core reads
  its private ICS link. The event title picks the theme ("Christmas", "Doomsday", …); unknown titles get a generic
  accent plus a Jarvis greeting naming the occasion. Multi-day events are supported. Angel may add events if the
  calendar is shared with her.
- Music: Awesome Mix checks YouTube Music for new releases from followed artists automatically; anticipated albums go
  on the same calendar as "Album: <title> – <artist>". On release day the album is pinned first in the speed dial with
  an "Out now" tag and Core shows a toast; after a week it returns to normal rotation unless pinned (massive artists:
  stays pinned until unpinned).
- Later: the same calendar flows through Home Assistant so the house can join in (e.g. green lights for Doomsday week).

## Music in the interface

- Regular days: nothing plays automatically. Game pages may play the game's theme via ThemeDeck once it works for all
  games; Home shows a small "now playing" touch while Awesome Mix plays.
- Standard events: a short themed sound on the boot screen; a fitting playlist offered first in the speed dial.
- Massive events: an event soundtrack plays softly on Home through Awesome Mix (e.g. the Avengers theme in Doomsday
  week, the new album on Uzi day); the boot screen gets its own music cue. Stops the instant a game starts; off switch.

## Related agreed items

- Club tab shows a variable number of picks (December has five: the Christmas special is its own card).
