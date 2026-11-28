// Eternal Core 3.3.10: Quick Access with its rail on the right (docs/eternal/CORE-UI.md, "Quick Access: the rail on the
// right"). The stylesheet reverses the row Steam lays the rail and the content in (qam-rail-right); this keeps the
// D-pad true to the picture whichever way Steam's navigation reads a row (by child order or by position):
//
// - in the rail, left goes into the content (to where focus last was there, else its first item level with the open
//   tab) and right goes nowhere (the rail is the panel's right edge);
// - in the content, right from its right edge goes to the open tab in the rail; everything else is Steam's (a slider
//   or any control that takes left and right keeps them, and left from the content's left edge stays Steam's).
//
// Steam's controller input reaches the page as `vgp_onbuttondown` events on the focused element; one capture listener
// per Quick Access document reads them. A move is a DOM focus() on a Steam Focusable (Steam follows DOM focus, as the
// On this day cards already rely on), after preventDefault and stopPropagation so Steam does not move as well.

export const DIR_LEFT = 11;
export const DIR_RIGHT = 12;

export type RailMove = "into-content" | "into-rail" | "stay" | "steam";

// The pure decision. `inRail`: focus is on a rail tab. `rightOfFocus`: the content has a focusable item to the right of
// the focused one. `takesSides`: the focused control uses left and right itself (a slider).
export function railMove(button: unknown, where: { inRail: boolean; rightOfFocus: boolean; takesSides: boolean }): RailMove {
  if (button !== DIR_LEFT && button !== DIR_RIGHT) return "steam";
  if (where.inRail) return button === DIR_LEFT ? "into-content" : "stay";
  if (button === DIR_RIGHT && !where.rightOfFocus && !where.takesSides) return "into-rail";
  return "steam";
}

type Rect = { left: number; right: number; top: number; bottom: number; width: number; height: number };
const rectOf = (element: any): Rect | undefined => { try { return element.getBoundingClientRect(); } catch { return undefined; } };
const visible = (rect: Rect | undefined) => !!rect && rect.width > 0 && rect.height > 0;
// A Steam Focusable with no Focusable inside it: what the D-pad lands on.
function leaves(root: any): any[] {
  let all: any[] = [];
  try { all = Array.from(root.querySelectorAll(".Focusable")); } catch { all = []; }
  return all.filter(node => { try { return !node.querySelector(".Focusable"); } catch { return true; } }).slice(0, 400);
}
export function takesSides(element: any): boolean {
  try { return !!element?.closest?.('[role="slider"], [aria-valuenow], input[type="range"]') || !!element?.querySelector?.('[role="slider"], input[type="range"]'); }
  catch { return false; }
}

export type QamRailSelectors = { menu?: string; rail?: string; tab?: string; selected?: string };

export function installQamRail(doc: Document, select: () => QamRailSelectors): () => void {
  let lastContent: any;
  const handler = (event: any) => {
    try {
      const button = event?.detail?.button;
      if (button !== DIR_LEFT && button !== DIR_RIGHT) return;
      const { menu: menuSel, rail: railSel, tab: tabSel, selected } = select();
      if (!menuSel || !railSel) return;
      const focused: any = (doc as any).activeElement ?? event.target;
      const menu = focused?.closest?.(menuSel);
      if (!menu) return;
      const rail = menu.querySelector(railSel);
      if (!rail) return;
      const inRail = rail.contains(focused);
      const content = Array.from(menu.children as ArrayLike<any>).find((child: any) => child !== rail && !child.contains(rail) && visible(rectOf(child)));
      if (!inRail) lastContent = focused;
      const here = rectOf(focused);
      const rightOfFocus = !inRail && !!here && leaves(content ?? menu).some(node => {
        if (node === focused || rail.contains(node)) return false;
        const r = rectOf(node);
        return visible(r) && r!.left >= here.right - 1 && r!.top < here.bottom && r!.bottom > here.top;
      });
      const move = railMove(button, { inRail, rightOfFocus, takesSides: !inRail && takesSides(focused) });
      if (move === "steam") return;
      event.preventDefault?.(); event.stopPropagation?.();
      if (move === "stay") return;
      if (move === "into-rail") {
        const tab = (selected ? rail.querySelector(selected) : null) ?? (tabSel ? rail.querySelector(tabSel) : null);
        tab?.focus?.();
        return;
      }
      // Into the content: where focus last was there, else the item nearest the open tab's height.
      if (lastContent?.isConnected && content?.contains(lastContent) && visible(rectOf(lastContent))) { lastContent.focus?.(); return; }
      const from = rectOf(focused);
      const targets = leaves(content ?? menu).filter(node => !rail.contains(node) && visible(rectOf(node)));
      const best = targets.sort((a, b) => Math.abs((rectOf(a)!.top + rectOf(a)!.bottom) / 2 - ((from?.top ?? 0) + (from?.bottom ?? 0)) / 2)
        - Math.abs((rectOf(b)!.top + rectOf(b)!.bottom) / 2 - ((from?.top ?? 0) + (from?.bottom ?? 0)) / 2))[0];
      best?.focus?.();
    } catch { /* Steam's own navigation stands. */ }
  };
  try { doc.addEventListener("vgp_onbuttondown", handler, true); } catch { return () => {}; }
  return () => { try { doc.removeEventListener("vgp_onbuttondown", handler, true); } catch { /* Gone with its window. */ } };
}

// 3.3.11: how much of the Quick Access window is on screen. Steam's Quick Access is a window of its own, wider than the
// part of it the user sees (855 px, about 350 px on screen on the TV), so a rail at the window's right edge is off
// screen. Two ways to tell, in order:
// - `placeholder`: the main window's Quick Access placeholder (left edge, in the main window's px) and that window's
//   width: the panel shows from its left edge to the window's right edge;
// - `window`: the Quick Access window's own screenX, width and the screen's width.
// Returns the visible width in px, at most `full` (the Quick Access window's width), or undefined when neither is sane.
export const QAM_MIN_VISIBLE = 160;
export function measureQamVisible(input: { placeholderLeft?: number; hostWidth?: number; screenX?: number; screenWidth?: number; full: number }): number | undefined {
  const full = Number(input.full) || 0;
  if (full < QAM_MIN_VISIBLE) return undefined;
  const sane = (value: number) => Number.isFinite(value) && value >= QAM_MIN_VISIBLE ? Math.round(Math.min(value, full)) : undefined;
  const { placeholderLeft, hostWidth, screenX, screenWidth } = input;
  if (Number.isFinite(placeholderLeft) && Number.isFinite(hostWidth) && (hostWidth as number) > (placeholderLeft as number) && (placeholderLeft as number) >= 0) {
    const fromPlaceholder = sane((hostWidth as number) - (placeholderLeft as number));
    if (fromPlaceholder) return fromPlaceholder;
  }
  if (Number.isFinite(screenX) && Number.isFinite(screenWidth) && (screenWidth as number) > 1 && (screenX as number) >= 0 && (screenX as number) < (screenWidth as number)) {
    return sane((screenWidth as number) - (screenX as number));
  }
  return undefined;
}
