// Eternal Core batch 2: where Decky serves the swipe typing keyboard files (dist/keyboard/ beside dist/index.js), the
// way VaporType finds them. Its own module because import.meta exists only in the ES-module bundle: the tests that
// load src/index.tsx as CommonJS replace this module.
export function keyboardBase(): URL {
  return new URL("keyboard/", import.meta.url);
}
