import { SP_REACT as React } from "./decky";
import { sharedStarfield, STARFIELD_FPS } from "./coreStarfield";
import { coreUiSetting } from "./coreUiSetting";

// Eternal Core: the Core Starfield behind one of Core's own black pages (the Jarvis page), per "black means
// starfield" (docs/eternal/CORE-UI.md). The parent must be positioned with isolation: isolate. Shares the one field
// with Core UI's canvases; draws at most 30 frames a second, holds still while a game runs, while the page is
// hidden and under reduced motion, and shows nothing while the Core UI setting is off.
export function CoreStars() {
  const on = React.useSyncExternalStore(coreUiSetting.subscribe, coreUiSetting.get, coreUiSetting.get);
  const canvas = React.useRef<HTMLCanvasElement | null>(null);
  React.useEffect(() => {
    const element = canvas.current;
    if (!on || !element) return;
    const view = element.ownerDocument?.defaultView ?? window;
    const field = sharedStarfield();
    let frame: number | undefined, last: number | undefined, stopped = false;
    const still = () => {
      try {
        const running = Number((window as any).SteamUIStore?.MainRunningAppID ?? 0) > 0;
        return running || view.matchMedia?.("(prefers-reduced-motion: reduce)")?.matches === true;
      } catch { return false; }
    };
    const paint = () => {
      const width = Math.round(element.clientWidth), height = Math.round(element.clientHeight);
      if (element.width !== width) element.width = width;
      if (element.height !== height) element.height = height;
      const context = element.getContext("2d");
      if (context) field.draw(context, width, height);
    };
    const tick = (time: number) => {
      frame = undefined;
      if (stopped) return;
      if (element.ownerDocument?.visibilityState === "hidden") { frame = view.requestAnimationFrame(tick); return; }
      if (last === undefined || time - last >= 1000 / STARFIELD_FPS - 1) {
        last = time;
        if (!still()) field.advanceTo(time);
        paint();
      }
      // Held still: check again twice a second instead of every frame.
      if (still()) { const timer = view.setTimeout(() => { if (!stopped) frame = view.requestAnimationFrame(tick); }, 500); frame = -timer; }
      else frame = view.requestAnimationFrame(tick);
    };
    frame = view.requestAnimationFrame(tick);
    return () => {
      stopped = true;
      if (frame !== undefined) { if (frame < 0) view.clearTimeout(-frame); else view.cancelAnimationFrame(frame); }
    };
  }, [on]);
  if (!on) return null;
  return <canvas ref={canvas} aria-hidden="true" data-core-stars="page"
    style={{ position: "absolute", inset: 0, width: "100%", height: "100%", zIndex: -1, pointerEvents: "none", background: "#000000" }} />;
}
