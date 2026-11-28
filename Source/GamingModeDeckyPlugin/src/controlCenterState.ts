// Stable persisted IDs: audio is Video (third); performance is Audio (fourth).
export const CONTROL_TABS = ["home", "store", "audio", "performance", "graphics", "controller", "decky"] as const;
export type ControlTab = typeof CONTROL_TABS[number];
// 3.3.11: Quick Access > Core is one list. Its collapsible groups (CORE_GROUPS) are sections too, so their open state is
// kept the same way; "news" and "daily-history" stay valid ids (old settings load) though the list no longer shows them.
export const CORE_GROUPS = ["group/sound", "group/display", "group/controller", "group/downloads", "group/look", "group/system"] as const;
export type CoreGroup = typeof CORE_GROUPS[number];
export const CONTROL_SECTION_IDS = ["topbar-date", "device", "device/details", "news", "daily-history", "measurement", "display", "mixer", "devices", "power", "game", "shaders", "nvidia", "radeon", "lossless", "agent", ...CORE_GROUPS] as const;
// The old tabs' persisted ids and the group each moved to. The ids never matched their icons: "audio" was Video (now
// Display & graphics) and "performance" was Audio (now Sound); "graphics" was Performance & graphics.
export const TAB_GROUP: Record<ControlTab, CoreGroup | undefined> = {
  home: "group/system", store: "group/system", audio: "group/display", performance: "group/sound", graphics: "group/display",
  controller: "group/controller", decky: undefined,
};
export const ONE_LIST_LAYOUT = 3311;
// Settings saved before 3.3.11 (no `layout`): the group holding the tab that was open starts open, and a group all of
// whose old tabs were hidden starts closed. Everything else the user saved is kept as it was.
export function migrateToOneList(value: any, expanded: string[]): string[] {
  if (Number(value?.layout) === ONE_LIST_LAYOUT) return expanded;
  const hidden = new Set<string>(Array.isArray(value?.hidden) ? value.hidden.filter((id: unknown) => typeof id === "string") : []);
  const next = new Set(expanded);
  const active = TAB_GROUP[value?.active as ControlTab];
  if (active && !hidden.has(value.active)) next.add(active);
  for (const group of CORE_GROUPS) {
    const tabs = (Object.keys(TAB_GROUP) as ControlTab[]).filter(tab => TAB_GROUP[tab] === group);
    if (tabs.length && tabs.every(tab => hidden.has(tab))) next.delete(group);
  }
  return normalizeExpandedSections([...next]);
}
export function normalizeExpandedSections(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  const expanded = new Set(value.slice(0, 64).filter(id => typeof id === "string"));
  return CONTROL_SECTION_IDS.filter(id => expanded.has(id));
}
export function toggleControlSection(current: string[], id: string): string[] {
  const expanded = normalizeExpandedSections(current);
  if (!(CONTROL_SECTION_IDS as readonly string[]).includes(id)) return expanded;
  return normalizeExpandedSections(expanded.includes(id) ? expanded.filter(value => value !== id) : [...expanded, id]);
}
export interface ControlPreferences {
  order: ControlTab[];
  hidden: ControlTab[];
  collapsed: string[];
  expandedSections: string[];
  active: ControlTab;
  deckyHostEnabled: boolean;
  topbarDateEnabled: boolean;
  topbarDateFormat: string;
  topbarClockLeft: boolean;
  // 3.3.11: the one-list layout's version (ONE_LIST_LAYOUT once old settings are migrated).
  layout: number;
}
export function visibleControlTabs(prefs: ControlPreferences, deckyAvailable: boolean): ControlTab[] {
  const visible = prefs.order.filter(id => !prefs.hidden.includes(id) && (id !== "decky" || deckyAvailable));
  return visible.length ? visible : ["home"];
}
export function normalizeControlPreferences(value: any): ControlPreferences {
  const valid = (v: unknown): v is ControlTab => CONTROL_TABS.includes(v as ControlTab);
  const order = [...new Set<ControlTab>((Array.isArray(value?.order) ? value.order : []).filter(valid))];
  for (const tab of CONTROL_TABS) if (!order.includes(tab)) order.push(tab);
  const hidden = [...new Set<ControlTab>((Array.isArray(value?.hidden) ? value.hidden : []).filter(valid))];
  const deckyHostEnabled = value?.deckyHostEnabled === undefined || value.deckyHostEnabled === true;
  const eligible = (id: ControlTab) => valid(id);
  if (order.every(id => !eligible(id) || hidden.includes(id))) hidden.splice(hidden.indexOf("home"), 1);
  const active = valid(value?.active) && eligible(value.active) && !hidden.includes(value.active) ? value.active : order.find(id => eligible(id) && !hidden.includes(id))!;
  const dateFormats = ["auto", "dd_mm_yyyy", "dd_mm_yy", "yyyy_mm_dd", "dd_month_yyyy", "weekday_dd_month", "weekday_short_dd_month", "month_dd_yyyy", "month_short_dd_yyyy", "iso"];
  const topbarDateFormat = dateFormats.includes(value?.topbarDateFormat) ? value.topbarDateFormat : "auto";
  return { order, hidden, active, deckyHostEnabled, topbarDateEnabled: value?.topbarDateEnabled !== false, topbarDateFormat, topbarClockLeft: value?.topbarClockLeft === true, expandedSections: migrateToOneList(value, normalizeExpandedSections(value?.expandedSections)), layout: ONE_LIST_LAYOUT, collapsed: [...new Set<string>((Array.isArray(value?.collapsed) ? value.collapsed : []).filter((id: unknown) => typeof id === "string" && id.length < 80))] };
}
export function moveControlTab(prefs: ControlPreferences, id: ControlTab, direction: -1 | 1): ControlPreferences {
  const order = [...prefs.order];
  const from = order.indexOf(id), to = from + direction;
  if (from < 0 || to < 0 || to >= order.length) return prefs;
  [order[from], order[to]] = [order[to], order[from]];
  return normalizeControlPreferences({ ...prefs, order });
}
