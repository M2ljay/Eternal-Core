import { SP_REACT as React } from "./decky";

// The Core mark (docs/eternal/brand/core-logo-mark.svg) in one colour: ring, dial ticks
// and core. It inherits Steam's normal, focused and theme colours through currentColor.
export function PlayhubIcon() {
  const ticks = [0, 45, 90, 135, 180, 225, 270, 315];
  return (
    <svg viewBox="-80 -80 160 160" width="1em" height="1em" fill="currentColor" aria-hidden="true" focusable="false">
      <circle r="66" fill="none" stroke="currentColor" strokeWidth="11" />
      <g stroke="currentColor" strokeWidth="7" strokeLinecap="round">
        {ticks.map((angle) => <line key={angle} x1="0" y1="-49" x2="0" y2="-38" transform={`rotate(${angle})`} />)}
      </g>
      <path fillRule="evenodd" d="M0 -24A24 24 0 1 1 0 24A24 24 0 1 1 0 -24ZM0 -9A9 9 0 1 0 0 9A9 9 0 1 0 0 -9Z" />
    </svg>
  );
}
