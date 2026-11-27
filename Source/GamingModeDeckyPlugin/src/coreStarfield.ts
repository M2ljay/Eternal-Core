// Eternal Core: the Core Starfield for Core UI surfaces (docs/eternal/CORE-UI.md, "Black means starfield").
//
// The same flight as the Core Starfield screensaver (screensaver/main.js): the same seeded field (seed 23),
// the same colours (steel and near-white, one in twenty-five crimson), the same perspective projection and
// speed. Only the bouncing mark, its corner flash and the clock constellation stay in the screensaver.
//
// Cheap on purpose: one field is shared by every surface (one set of stars, one clock), fewer stars than the
// screensaver, drawn at most 30 times a second and only while a surface is visible (coreUi.ts decides).
//
// Readable text: a star is never brighter than STARFIELD_MAX_ALPHA over black, so Core's dimmest text
// (#9AA4AE) still has 4.5:1 against the brightest star pixel (tests/coreUi.test.mjs checks the sums).
//
// Self-contained (no imports): tests load it on its own.

export const STARFIELD_SEED = 23;
export const STARFIELD_COLOURS = { crimson: "215,38,61", steel: "201,209,217", white: "230,236,242" } as const;
// Depth travelled per second (the screensaver's .035), and how many stars a surface gets.
export const STARFIELD_SPEED = 0.035;
export const STARFIELD_COUNT = 160;
export const STARFIELD_MAX_ALPHA = 0.24;
export const STARFIELD_FPS = 30;

export type Star = { x: number; y: number; z: number; c: string };

// The screensaver's generator: Park-Miller from seed 23, three draws per star.
export function makeStars(count = STARFIELD_COUNT, seed = STARFIELD_SEED): Star[] {
  let state = seed;
  const rand = () => { state = (state * 16807) % 2147483647; return (state - 1) / 2147483646; };
  return Array.from({ length: Math.max(0, Math.min(420, Math.floor(count))) }, () => {
    const c = rand();
    return { x: rand(), y: rand(), z: rand(), c: c < 0.04 ? STARFIELD_COLOURS.crimson : c < 0.3 ? STARFIELD_COLOURS.white : STARFIELD_COLOURS.steel };
  });
}

type Context = {
  clearRect(x: number, y: number, w: number, h: number): void;
  beginPath(): void; arc(x: number, y: number, r: number, a: number, b: number): void; fill(): void;
  fillStyle: any;
};

export type Starfield = {
  readonly stars: Star[];
  // Moves the shared field on by `seconds` (capped, so a long pause does not jump).
  advance(seconds: number): void;
  // Moves the field to a point on one shared clock (performance.now() milliseconds). Several surfaces drawing in
  // the same frame move it once; a pause longer than a frame does not jump it.
  advanceTo(time: number): void;
  // Draws the field into one surface of size w x h. Returns how many stars were drawn.
  draw(context: Context, width: number, height: number): number;
  depth(): number;
};

export function createStarfield(count = STARFIELD_COUNT): Starfield {
  const stars = makeStars(count);
  let zoff = 0, clock: number | undefined;
  const advance = (seconds: number) => {
    if (!(seconds > 0)) return;
    zoff = (zoff + Math.min(0.05, seconds) * STARFIELD_SPEED) % 1;
  };
  return {
    stars,
    advance,
    advanceTo(time: number) {
      if (!Number.isFinite(time)) return;
      if (clock !== undefined && time > clock) advance((time - clock) / 1000);
      if (clock === undefined || time > clock) clock = time;
    },
    depth: () => zoff,
    draw(context: Context, width: number, height: number) {
      if (!(width > 0) || !(height > 0)) return 0;
      context.clearRect(0, 0, width, height);
      const cx = width / 2, cy = height / 2, f = Math.max(width, height) * 0.55;
      let drawn = 0;
      for (const s of stars) {
        const z = Math.max(0.03, ((s.z - zoff) % 1 + 1) % 1);
        const x = cx + (s.x - 0.5) * f / z, y = cy + (s.y - 0.5) * f / z;
        if (x < 0 || x > width || y < 0 || y > height) continue;
        const alpha = starAlpha(z);
        context.fillStyle = `rgba(${s.c},${alpha.toFixed(3)})`;
        context.beginPath();
        context.arc(x, y, Math.max(0.4, (1 - z) * 1.4), 0, Math.PI * 2);
        context.fill();
        drawn++;
      }
      return drawn;
    },
  };
}

// One field for every Core surface (Core UI's canvases and Core's own black pages).
let shared: Starfield | undefined;
export function sharedStarfield(): Starfield { return shared ??= createStarfield(); }

// The screensaver's fade-in with depth, scaled down to the readable ceiling.
export function starAlpha(z: number): number {
  return Math.max(0, Math.min(1, (1 - z) * 1.2)) * STARFIELD_MAX_ALPHA;
}

// WCAG contrast of text colour `text` ("#RRGGBB") over the brightest possible star pixel ("r,g,b" at
// STARFIELD_MAX_ALPHA on black). Used by the tests; kept here so the rule lives next to the numbers.
export function worstStarContrast(text: string): number {
  const lum = (r: number, g: number, b: number) => {
    const lin = (v: number) => { const c = v / 255; return c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4); };
    return 0.2126 * lin(r) + 0.7152 * lin(g) + 0.0722 * lin(b);
  };
  const value = Number.parseInt(text.slice(1, 7), 16);
  const fg = lum((value >> 16) & 255, (value >> 8) & 255, value & 255);
  let worst = Infinity;
  for (const colour of Object.values(STARFIELD_COLOURS)) {
    const [r, g, b] = colour.split(",").map(Number);
    const bg = lum(r * STARFIELD_MAX_ALPHA, g * STARFIELD_MAX_ALPHA, b * STARFIELD_MAX_ALPHA);
    const ratio = (Math.max(fg, bg) + 0.05) / (Math.min(fg, bg) + 0.05);
    worst = Math.min(worst, ratio);
  }
  return worst;
}
