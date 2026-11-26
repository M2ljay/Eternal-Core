import { DFL, SP_REACT as React } from "./decky";
import { call } from "./controlBackend";

// Eternal Core Mark III: "update night". Couch hardening holds Windows Update restarts
// while someone is signed in, so when Windows has a restart pending (or a month has
// passed) this card offers to restart at 5:00. The agent does the restart, never during a
// game (GamingMode.Services/UpdateNight.cs). Shown only when there is something to say.
const { PanelSection, PanelSectionRow, ButtonItem } = DFL as any;

type NightState = { offer?: boolean; pending?: boolean; scheduledFor?: string | null; hour?: number };
type Copy = { title: string; offer: string; restart: string; scheduled: string; cancel: string };

const copy: Record<string, Copy> = {
  en: { title: "Windows updates", offer: "Windows has updates waiting. Restart tonight at {time}?", restart: "Restart at {time}", scheduled: "Core will restart at {time} for Windows updates, unless a game is running.", cancel: "Cancel" },
  it: { title: "Aggiornamenti di Windows", offer: "Windows ha degli aggiornamenti in attesa. Riavviare stanotte alle {time}?", restart: "Riavvia alle {time}", scheduled: "Core si riavvierà alle {time} per gli aggiornamenti di Windows, a meno che un gioco sia aperto.", cancel: "Annulla" },
  es: { title: "Actualizaciones de Windows", offer: "Windows tiene actualizaciones pendientes. ¿Reiniciar esta noche a las {time}?", restart: "Reiniciar a las {time}", scheduled: "Core se reiniciará a las {time} para las actualizaciones de Windows, salvo que haya un juego abierto.", cancel: "Cancelar" },
  fr: { title: "Mises à jour de Windows", offer: "Windows a des mises à jour en attente. Redémarrer cette nuit à {time} ?", restart: "Redémarrer à {time}", scheduled: "Core redémarrera à {time} pour les mises à jour de Windows, sauf si un jeu est ouvert.", cancel: "Annuler" },
  de: { title: "Windows-Updates", offer: "Windows hat ausstehende Updates. Heute Nacht um {time} neu starten?", restart: "Um {time} neu starten", scheduled: "Core startet um {time} für Windows-Updates neu, außer ein Spiel läuft.", cancel: "Abbrechen" },
  pt: { title: "Atualizações do Windows", offer: "O Windows tem atualizações pendentes. Reiniciar esta noite às {time}?", restart: "Reiniciar às {time}", scheduled: "O Core vai reiniciar às {time} para as atualizações do Windows, a menos que um jogo esteja aberto.", cancel: "Cancelar" },
  uk: { title: "Оновлення Windows", offer: "Windows має оновлення, що очікують. Перезапустити сьогодні вночі о {time}?", restart: "Перезапустити о {time}", scheduled: "Core перезапуститься о {time} для оновлень Windows, якщо не запущено гру.", cancel: "Скасувати" },
  ru: { title: "Обновления Windows", offer: "У Windows есть ожидающие обновления. Перезагрузить сегодня ночью в {time}?", restart: "Перезагрузить в {time}", scheduled: "Core перезагрузится в {time} для обновлений Windows, если не запущена игра.", cancel: "Отмена" },
  zh: { title: "Windows 更新", offer: "Windows 有待安装的更新。今晚 {time} 重启吗？", restart: "{time} 重启", scheduled: "Core 将在 {time} 为 Windows 更新重启，除非有游戏在运行。", cancel: "取消" },
  ja: { title: "Windows の更新", offer: "Windows に保留中の更新があります。今夜 {time} に再起動しますか？", restart: "{time} に再起動", scheduled: "ゲーム中でなければ、Core は Windows の更新のため {time} に再起動します。", cancel: "キャンセル" },
  ko: { title: "Windows 업데이트", offer: "Windows에 대기 중인 업데이트가 있습니다. 오늘 밤 {time}에 다시 시작할까요?", restart: "{time}에 다시 시작", scheduled: "게임이 실행 중이 아니면 Core가 Windows 업데이트를 위해 {time}에 다시 시작합니다.", cancel: "취소" },
  hi: { title: "Windows अपडेट", offer: "Windows के अपडेट इंतज़ार में हैं। आज रात {time} बजे रीस्टार्ट करें?", restart: "{time} बजे रीस्टार्ट करें", scheduled: "कोई गेम न चल रहा हो तो Core Windows अपडेट के लिए {time} बजे रीस्टार्ट होगा।", cancel: "रद्द करें" },
};

export function nightTime(locale: string, state: NightState, now = new Date()): string {
  let when = state.scheduledFor ? new Date(state.scheduledFor) : undefined;
  if (!when || Number.isNaN(when.getTime())) {
    when = new Date(now); when.setHours(state.hour ?? 5, 0, 0, 0);
    if (when <= now) when.setDate(when.getDate() + 1);
  }
  try { return when.toLocaleTimeString(locale, { hour: "numeric", minute: "2-digit" }); }
  catch { return `${when.getHours()}:00`; }
}

export function UpdateNightCard({ locale }: { locale: string }) {
  const [state, setState] = React.useState<NightState | undefined>();
  const [busy, setBusy] = React.useState(false);
  const text = copy[locale] ?? copy.en;
  React.useEffect(() => {
    let alive = true;
    const load = () => { call<[], NightState>("core_update_night").then(value => { if (alive) setState(value); }).catch(() => undefined); };
    load();
    const timer = window.setInterval(load, 60000);
    return () => { alive = false; window.clearInterval(timer); };
  }, []);
  if (!state || (!state.offer && !state.scheduledFor)) return null;
  const time = nightTime(locale, state);
  const act = async (method: string) => {
    setBusy(true);
    try { setState(await call<[], NightState>(method)); } catch { /* The agent may be restarting; the next poll retries. */ }
    finally { setBusy(false); }
  };
  return (
    <PanelSection title={text.title}>
      <PanelSectionRow>
        <div style={{ fontSize: 13, lineHeight: 1.5, opacity: .85, margin: "0 0 8px" }}>
          {(state.scheduledFor ? text.scheduled : text.offer).replace("{time}", time)}
        </div>
      </PanelSectionRow>
      <PanelSectionRow>
        <ButtonItem layout="below" disabled={busy}
          onClick={() => act(state.scheduledFor ? "core_update_night_cancel" : "core_update_night_schedule")}>
          {state.scheduledFor ? text.cancel : text.restart.replace("{time}", time)}
        </ButtonItem>
      </PanelSectionRow>
    </PanelSection>
  );
}
