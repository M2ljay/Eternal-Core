export type UpdateNotice = { available?: boolean; version?: string };
type Dependencies = {
  main: () => unknown;
  check: () => Promise<UpdateNotice>;
  read: () => string | null;
  write: (version: string) => void;
  notify: (version: string) => void;
  schedule: (callback: () => void, delay: number) => unknown;
  cancel: (timer: unknown) => void;
};

export function installPlayhubUpdateNotice(deps: Dependencies): () => void {
  let disposed = false, busy = false, previous: unknown, timer: unknown;
  const shown = new Set<string>();
  async function check(main: unknown) {
    if (busy) return;
    busy = true;
    try {
      const reply = await deps.check();
      const version = reply?.version;
      if (disposed || deps.main() !== main || reply?.available !== true || typeof version !== "string"
        || !/^\d+\.\d+\.\d+(?:\.\d+)?$/.test(version) || shown.has(version)) return;
      let saved: string | null = null;
      try { saved = deps.read(); } catch { /* Storage may be unavailable in a Steam window. */ }
      if (saved && /^\d+\.\d+\.\d+(?:\.\d+)?$/.test(saved)) {
        const old = saved.split(".").map(Number), next = version.split(".").map(Number);
        let newer = false;
        for (let i = 0; i < 4; i++) {
          if ((next[i] ?? 0) !== (old[i] ?? 0)) { newer = (next[i] ?? 0) > (old[i] ?? 0); break; }
        }
        if (!newer) return;
      }
      deps.notify(version);
      shown.add(version);
      try { deps.write(version); } catch { /* The current session still prevents duplicate notices. */ }
    } catch { /* Update checks leave Steam usable while offline. */ }
    finally { busy = false; }
  }
  function tick() {
    if (disposed) return;
    let main: unknown;
    try { main = deps.main(); } catch { main = undefined; }
    if (!main) previous = undefined;
    if (!busy) {
      if (main && main !== previous) void check(main);
      previous = main;
    }
    timer = deps.schedule(tick, 2000);
  }
  tick();
  return () => { disposed = true; deps.cancel(timer); };
}

export function updateNoticeBody(locale: string, version: string): string {
  const copy: Record<string, string> = {
    en: "Core {version} is available. Open Core on your PC to update.",
    it: "È disponibile Core {version}. Apri Core sul PC per aggiornare.",
    es: "Core {version} está disponible. Abre Core en tu PC para actualizar.",
    fr: "Core {version} est disponible. Ouvrez Core sur votre PC pour le mettre à jour.",
    de: "Core {version} ist verfügbar. Öffne Core auf deinem PC, um es zu aktualisieren.",
    pt: "O Core {version} está disponível. Abra o Core no PC para atualizar.",
    uk: "Доступна версія Core {version}. Відкрийте Core на ПК, щоб оновити.",
    zh: "Core {version} 已推出。请在电脑上打开 Core 进行更新。",
    ja: "Core {version} が利用できます。PC で Core を開いて更新してください。",
    ko: "Core {version}을 사용할 수 있습니다. PC에서 Core를 열어 업데이트하세요.",
    hi: "Core {version} उपलब्ध है। अपडेट करने के लिए अपने PC पर Core खोलें।",
    ru: "Доступна версия Core {version}. Откройте Core на ПК, чтобы обновить.",
  };
  return (copy[locale] ?? copy.en).replace("{version}", version);
}
