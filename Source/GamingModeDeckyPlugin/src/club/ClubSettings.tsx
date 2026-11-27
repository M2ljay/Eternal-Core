import { SP_REACT as React, DFL, toaster } from "../decky";
import { clubCopy } from "./clubLocale";
import { clubState, loadClub, saveClubSettings, subscribeClub } from "./clubStore";

// Eternal Core: the Club's settings (Quick Access > Core): "Show the Club tab" (on by default) and the member's
// club display name, typed once with Steam's on-screen keyboard (Steam opens it when the field is activated).

const { Focusable, ToggleField, TextField, DialogButton } = DFL as any;

export function ClubSettings({ locale }: { locale: string }) {
  const copy = clubCopy(locale);
  const state = React.useSyncExternalStore(subscribeClub, clubState, clubState);
  const settings = state.settings;
  const [name, setName] = React.useState<string | null>(null);
  const [busy, setBusy] = React.useState(false);
  React.useEffect(() => { if (!state.settings) void loadClub(false); }, []);
  const value = name ?? settings?.display_name ?? "";
  const save = async (enabled: boolean, display_name: string) => {
    if (busy) return;
    setBusy(true);
    try { await saveClubSettings({ enabled, display_name }); setName(null); }
    catch { toaster.toast({ title: "Core", body: copy.saveFailed }); }
    finally { setBusy(false); }
  };
  return <Focusable flow-children="column" style={{ padding: "8px 0", display: "flex", flexDirection: "column", gap: 12 }}>
    <ToggleField label={copy.setting} description={copy.settingHint} bottomSeparator="none" checked={settings?.enabled !== false}
      disabled={busy || !settings} onChange={(enabled: boolean) => void save(enabled, settings?.display_name ?? "")} />
    <TextField label={copy.nameLabel} value={value} disabled={busy || !settings}
      onChange={(event: any) => setName(String(event?.target?.value ?? "").slice(0, 64))}
      onKeyDown={(event: any) => { if (event?.key === "Enter") void save(settings?.enabled !== false, value); }} />
    <DialogButton disabled={busy || !settings || value.trim() === (settings?.display_name ?? "")}
      onClick={() => void save(settings?.enabled !== false, value)}>{copy.nameSave}</DialogButton>
  </Focusable>;
}
