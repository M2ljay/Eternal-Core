// Eternal Core 3.3.11: visible names of the upstream plugin suite say Core (REBRAND.md, "Visible plugin names").
// The plugins keep their IDs and plugin.json names (matched by name for updates and fork protection); only the text a
// user reads changes: the store's titles and the entries those plugins add to Steam's context menu.

export const CORE_PLUGIN_LABELS: Readonly<Record<string, string>> = {
  "Playhub Metadata": "Core Metadata",
  "Playhub Artworks": "Core Artworks",
};

// The visible label for a plugin name: exact matches only, anything else unchanged.
export function corePluginLabel(name: string): string {
  const key = String(name ?? "");
  return Object.prototype.hasOwnProperty.call(CORE_PLUGIN_LABELS, key.trim()) ? CORE_PLUGIN_LABELS[key.trim()] : key;
}

// Rewrites text nodes under `root` whose whole (trimmed) text is a mapped name. At most `limit` text nodes are read.
// Returns how many were changed.
export function relabelTree(root: any, limit = 400): number {
  const doc = root?.ownerDocument ?? root;
  const walkerOf = doc?.createTreeWalker;
  if (typeof walkerOf !== "function") return 0;
  let changed = 0, seen = 0;
  try {
    const walker = doc.createTreeWalker(root, 4 /* NodeFilter.SHOW_TEXT */);
    for (let node = walker.nextNode(); node && seen < limit; node = walker.nextNode(), seen++) {
      const text = String(node.nodeValue ?? "");
      const label = corePluginLabel(text.trim());
      if (label !== text.trim()) { node.nodeValue = text.replace(text.trim(), label); changed++; }
    }
  } catch { /* Left as the plugin wrote it. */ }
  return changed;
}

// Every open context menu in `doc` (found by `selector`, Steam's contextMenuContents class).
export function relabelContextMenus(doc: any, selector: string | undefined): number {
  if (!doc || !selector) return 0;
  let menus: any[] = [];
  try { menus = Array.from(doc.querySelectorAll(selector)).slice(0, 4); } catch { menus = []; }
  return menus.reduce((sum, menu) => sum + relabelTree(menu), 0);
}
