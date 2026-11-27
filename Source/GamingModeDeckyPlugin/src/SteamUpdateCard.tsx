import { DFL, SP_REACT as React } from "./decky";
import { call } from "./controlBackend";
import { CORE } from "./corePalette";
import { healthCopy } from "./healthReport";

// Eternal Core: Steam updates in Quick Access > Core (agent SteamUpdateHold.cs, docs/eternal/STEAM-UPDATES.md).
// One line for where Steam stands (up to date, a new version soaking, due, postponed and why, waiting after a
// failed try) and "Update Steam now": the same backup, smoke test and rollback as the automatic update. The
// agent's gate may ask for confirmation first: once when it cannot tell whether the new Steam works with Decky,
// twice when it knows it does not. Each confirmation is another press of the same button.
const { PanelSection, PanelSectionRow, ButtonItem } = DFL as any;

export type SteamUpdateState = { holdPhase?: string; daysLeft?: number; postponedReason?: string | null; busy?: boolean; action?: string | null };
type Copy = Record<"title" | "current" | "soaking" | "due" | "waiting" | "off" | "now" | "again" | "started" | "busy" | "confirmCantTell" | "confirmIncompatible" | "confirmAgain", string>;

const copy: Record<string, Copy> = {
  en: { title: "Steam updates", current: "Steam is up to date.", soaking: "A new Steam version is waiting: Core installs it in {n} days, at night, if it works with Decky.", due: "Core updates Steam on the next update night, when you're not playing.", waiting: "The last Steam update failed its check and was undone. Core tries again when a fix arrives.", off: "Steam updates itself, as usual.", now: "Update Steam now", again: "Update anyway", started: "Updating Steam: Core saves a copy first and is back in a few minutes.", busy: "An update is already running.", confirmCantTell: "Core can't tell whether the new Steam works with Decky. Press again to update anyway: Core saves a copy first and rolls back if something goes wrong.", confirmIncompatible: "The new Steam doesn't work with the installed Decky: plugins will stop loading. Press again if you want to update anyway.", confirmAgain: "Last confirmation: press once more to update Steam knowing that plugins won't work." },
  it: { title: "Aggiornamenti di Steam", current: "Steam è aggiornato.", soaking: "C'è una nuova versione di Steam: Core la installa tra {n} giorni, di notte, se funziona con Decky.", due: "Core aggiorna Steam la prossima notte degli aggiornamenti, quando non giochi.", waiting: "L'ultimo aggiornamento di Steam non ha superato il controllo ed è stato annullato. Core riprova quando arriva una correzione.", off: "Steam si aggiorna da solo, come al solito.", now: "Aggiorna Steam ora", again: "Aggiorna comunque", started: "Aggiornamento di Steam in corso: Core salva prima una copia e torna tra qualche minuto.", busy: "Un aggiornamento è già in corso.", confirmCantTell: "Core non sa se il nuovo Steam funziona con Decky. Premi di nuovo per aggiornare comunque: Core salva prima una copia e torna indietro se qualcosa non va.", confirmIncompatible: "Il nuovo Steam non funziona con il Decky installato: i plugin smetteranno di caricarsi. Premi di nuovo se vuoi aggiornare lo stesso.", confirmAgain: "Ultima conferma: premi ancora una volta per aggiornare Steam sapendo che i plugin non funzioneranno." },
  es: { title: "Actualizaciones de Steam", current: "Steam está actualizado.", soaking: "Hay una nueva versión de Steam: Core la instalará dentro de {n} días, de noche, si funciona con Decky.", due: "Core actualizará Steam la próxima noche de actualizaciones, cuando no estés jugando.", waiting: "La última actualización de Steam no pasó la comprobación y se deshizo. Core lo volverá a intentar cuando llegue una corrección.", off: "Steam se actualiza solo, como siempre.", now: "Actualizar Steam ahora", again: "Actualizar igualmente", started: "Actualizando Steam: Core guarda antes una copia y vuelve en unos minutos.", busy: "Ya hay una actualización en curso.", confirmCantTell: "Core no sabe si el nuevo Steam funciona con Decky. Pulsa de nuevo para actualizar igualmente: Core guarda antes una copia y vuelve atrás si algo falla.", confirmIncompatible: "El nuevo Steam no funciona con el Decky instalado: los plugins dejarán de cargarse. Pulsa de nuevo si quieres actualizar igualmente.", confirmAgain: "Última confirmación: pulsa una vez más para actualizar Steam sabiendo que los plugins no funcionarán." },
  fr: { title: "Mises à jour de Steam", current: "Steam est à jour.", soaking: "Une nouvelle version de Steam attend : Core l'installera dans {n} jours, la nuit, si elle fonctionne avec Decky.", due: "Core mettra Steam à jour la prochaine nuit des mises à jour, quand tu ne joues pas.", waiting: "La dernière mise à jour de Steam n'a pas passé la vérification et a été annulée. Core réessaiera quand un correctif arrivera.", off: "Steam se met à jour tout seul, comme d'habitude.", now: "Mettre à jour Steam maintenant", again: "Mettre à jour quand même", started: "Mise à jour de Steam : Core fait d'abord une copie et revient dans quelques minutes.", busy: "Une mise à jour est déjà en cours.", confirmCantTell: "Core ne sait pas si le nouveau Steam fonctionne avec Decky. Appuie de nouveau pour mettre à jour quand même : Core fait d'abord une copie et revient en arrière si quelque chose ne va pas.", confirmIncompatible: "Le nouveau Steam ne fonctionne pas avec le Decky installé : les plugins ne se chargeront plus. Appuie de nouveau si tu veux mettre à jour quand même.", confirmAgain: "Dernière confirmation : appuie encore une fois pour mettre Steam à jour en sachant que les plugins ne fonctionneront pas." },
  de: { title: "Steam-Updates", current: "Steam ist aktuell.", soaking: "Eine neue Steam-Version wartet: Core installiert sie in {n} Tagen nachts, wenn sie mit Decky funktioniert.", due: "Core aktualisiert Steam in der nächsten Update-Nacht, wenn du nicht spielst.", waiting: "Das letzte Steam-Update hat die Prüfung nicht bestanden und wurde rückgängig gemacht. Core versucht es erneut, sobald es eine Korrektur gibt.", off: "Steam aktualisiert sich wie gewohnt selbst.", now: "Steam jetzt aktualisieren", again: "Trotzdem aktualisieren", started: "Steam wird aktualisiert: Core sichert vorher und ist in ein paar Minuten zurück.", busy: "Ein Update läuft bereits.", confirmCantTell: "Core weiß nicht, ob das neue Steam mit Decky funktioniert. Drücke erneut, um trotzdem zu aktualisieren: Core sichert vorher und setzt zurück, wenn etwas schiefgeht.", confirmIncompatible: "Das neue Steam funktioniert nicht mit dem installierten Decky: Plugins werden nicht mehr geladen. Drücke erneut, wenn du trotzdem aktualisieren willst.", confirmAgain: "Letzte Bestätigung: Drücke noch einmal, um Steam zu aktualisieren, obwohl die Plugins dann nicht funktionieren." },
  pt: { title: "Atualizações do Steam", current: "O Steam está atualizado.", soaking: "Há uma nova versão do Steam esperando: o Core vai instalá-la em {n} dias, de noite, se funcionar com o Decky.", due: "O Core vai atualizar o Steam na próxima noite de atualizações, quando você não estiver jogando.", waiting: "A última atualização do Steam não passou na verificação e foi desfeita. O Core tenta de novo quando chegar uma correção.", off: "O Steam se atualiza sozinho, como de costume.", now: "Atualizar o Steam agora", again: "Atualizar mesmo assim", started: "Atualizando o Steam: o Core salva uma cópia antes e volta em alguns minutos.", busy: "Já há uma atualização em andamento.", confirmCantTell: "O Core não sabe se o novo Steam funciona com o Decky. Pressione de novo para atualizar mesmo assim: o Core salva uma cópia antes e volta atrás se algo der errado.", confirmIncompatible: "O novo Steam não funciona com o Decky instalado: os plugins vão parar de carregar. Pressione de novo se quiser atualizar mesmo assim.", confirmAgain: "Última confirmação: pressione mais uma vez para atualizar o Steam sabendo que os plugins não vão funcionar." },
  uk: { title: "Оновлення Steam", current: "Steam оновлено.", soaking: "Нова версія Steam чекає: Core встановить її через {n} дн., уночі, якщо вона працює з Decky.", due: "Core оновить Steam наступної ночі оновлень, коли ви не граєте.", waiting: "Останнє оновлення Steam не пройшло перевірку й було скасоване. Core спробує знову, коли з'явиться виправлення.", off: "Steam оновлюється сам, як зазвичай.", now: "Оновити Steam зараз", again: "Усе одно оновити", started: "Оновлення Steam: Core спершу зберігає копію й повернеться за кілька хвилин.", busy: "Оновлення вже триває.", confirmCantTell: "Core не знає, чи працює новий Steam з Decky. Натисніть ще раз, щоб усе одно оновити: Core спершу збереже копію й поверне попередню версію, якщо щось піде не так.", confirmIncompatible: "Новий Steam не працює з установленим Decky: плагіни перестануть завантажуватися. Натисніть ще раз, якщо все одно хочете оновити.", confirmAgain: "Останнє підтвердження: натисніть ще раз, щоб оновити Steam, знаючи, що плагіни не працюватимуть." },
  zh: { title: "Steam 更新", current: "Steam 已是最新版本。", soaking: "有新版 Steam 等待安装：如果与 Decky 兼容，Core 会在 {n} 天后的夜里安装。", due: "Core 会在下一个更新之夜、你不玩游戏时更新 Steam。", waiting: "上一次 Steam 更新未通过检查，已被撤销。修复发布后 Core 会再试。", off: "Steam 会照常自行更新。", now: "立即更新 Steam", again: "仍然更新", started: "正在更新 Steam：Core 会先备份，几分钟后回来。", busy: "已有更新正在进行。", confirmCantTell: "Core 无法判断新版 Steam 是否能与 Decky 配合使用。再按一次即可仍然更新：Core 会先备份，出问题时自动回退。", confirmIncompatible: "新版 Steam 无法与已安装的 Decky 配合使用：插件将无法加载。如果仍要更新，请再按一次。", confirmAgain: "最后确认：再按一次即会更新 Steam，但插件将无法使用。" },
  ja: { title: "Steam の更新", current: "Steam は最新です。", soaking: "新しい Steam が待機中です。Decky で動くなら、Core が {n} 日後の夜にインストールします。", due: "プレイしていなければ、Core は次の更新の夜に Steam を更新します。", waiting: "前回の Steam の更新はチェックに通らず、元に戻されました。修正が届いたら Core がもう一度試します。", off: "Steam はいつも通り自動で更新されます。", now: "今すぐ Steam を更新", again: "それでも更新", started: "Steam を更新中：Core は先にコピーを保存し、数分で戻ります。", busy: "すでに更新が実行中です。", confirmCantTell: "新しい Steam が Decky で動くかどうか Core にはわかりません。それでも更新するにはもう一度押してください。Core は先にコピーを保存し、問題があれば元に戻します。", confirmIncompatible: "新しい Steam はインストール済みの Decky では動作せず、プラグインが読み込まれなくなります。それでも更新する場合はもう一度押してください。", confirmAgain: "最終確認：プラグインが動かなくなることを承知で Steam を更新するには、もう一度押してください。" },
  ko: { title: "Steam 업데이트", current: "Steam이 최신 상태입니다.", soaking: "새 Steam 버전이 대기 중입니다. Decky와 작동하면 Core가 {n}일 뒤 밤에 설치합니다.", due: "게임을 하고 있지 않으면 Core가 다음 업데이트 밤에 Steam을 업데이트합니다.", waiting: "마지막 Steam 업데이트가 확인을 통과하지 못해 취소되었습니다. 수정이 나오면 Core가 다시 시도합니다.", off: "Steam이 평소처럼 스스로 업데이트됩니다.", now: "지금 Steam 업데이트", again: "그래도 업데이트", started: "Steam 업데이트 중: Core가 먼저 사본을 저장하고 몇 분 뒤 돌아옵니다.", busy: "이미 업데이트가 진행 중입니다.", confirmCantTell: "Core는 새 Steam이 Decky와 작동하는지 알 수 없습니다. 그래도 업데이트하려면 다시 누르세요. Core가 먼저 사본을 저장하고 문제가 생기면 되돌립니다.", confirmIncompatible: "새 Steam은 설치된 Decky와 작동하지 않아 플러그인이 로드되지 않습니다. 그래도 업데이트하려면 다시 누르세요.", confirmAgain: "마지막 확인: 플러그인이 작동하지 않는다는 것을 알고도 Steam을 업데이트하려면 한 번 더 누르세요." },
  hi: { title: "Steam अपडेट", current: "Steam अपडेट है।", soaking: "Steam का नया संस्करण इंतज़ार में है: अगर वह Decky के साथ काम करता है तो Core उसे {n} दिन बाद रात में इंस्टॉल करेगा।", due: "जब आप नहीं खेल रहे होंगे, Core अगली अपडेट वाली रात Steam को अपडेट करेगा।", waiting: "पिछला Steam अपडेट जाँच में पास नहीं हुआ और वापस ले लिया गया। सुधार आने पर Core फिर कोशिश करेगा।", off: "Steam हमेशा की तरह खुद अपडेट होता है।", now: "Steam अभी अपडेट करें", again: "फिर भी अपडेट करें", started: "Steam अपडेट हो रहा है: Core पहले एक कॉपी सहेजता है और कुछ मिनट में लौटता है।", busy: "एक अपडेट पहले से चल रहा है।", confirmCantTell: "Core नहीं बता सकता कि नया Steam Decky के साथ काम करता है या नहीं। फिर भी अपडेट करने के लिए दोबारा दबाएँ: Core पहले एक कॉपी सहेजता है और कुछ गलत हो तो वापस लौट जाता है।", confirmIncompatible: "नया Steam इंस्टॉल किए गए Decky के साथ काम नहीं करता: प्लगइन लोड होना बंद हो जाएँगे। फिर भी अपडेट करना हो तो दोबारा दबाएँ।", confirmAgain: "आख़िरी पुष्टि: यह जानते हुए कि प्लगइन काम नहीं करेंगे, Steam अपडेट करने के लिए एक बार और दबाएँ।" },
  ru: { title: "Обновления Steam", current: "Steam обновлён.", soaking: "Новая версия Steam ждёт: Core установит её через {n} дн., ночью, если она работает с Decky.", due: "Core обновит Steam в следующую ночь обновлений, когда вы не играете.", waiting: "Последнее обновление Steam не прошло проверку и было отменено. Core попробует снова, когда выйдет исправление.", off: "Steam обновляется сам, как обычно.", now: "Обновить Steam сейчас", again: "Всё равно обновить", started: "Обновление Steam: Core сначала сохраняет копию и вернётся через несколько минут.", busy: "Обновление уже идёт.", confirmCantTell: "Core не знает, работает ли новый Steam с Decky. Нажмите ещё раз, чтобы всё равно обновить: Core сначала сохранит копию и вернёт прежнюю версию, если что-то пойдёт не так.", confirmIncompatible: "Новый Steam не работает с установленным Decky: плагины перестанут загружаться. Нажмите ещё раз, если всё равно хотите обновить.", confirmAgain: "Последнее подтверждение: нажмите ещё раз, чтобы обновить Steam, зная, что плагины не будут работать." },
};

export function steamUpdateCopy(locale: string): Copy {
  const language = String(locale || "en").toLowerCase().split(/[-_]/)[0];
  return copy[language === "ua" ? "uk" : language] ?? copy.en;
}

export const steamUpdateLanguages = Object.keys(copy);

// The status line for the agent's /steam/update/state answer.
export function steamUpdateLine(state: SteamUpdateState | undefined, locale: string): string {
  const text = steamUpdateCopy(locale), health = healthCopy(locale);
  switch (state?.holdPhase) {
    case "Current": return text.current;
    case "Soaking": return text.soaking.replace("{n}", String(Math.max(1, Number(state.daysLeft) || 1)));
    case "Due": return text.due;
    case "Postponed": {
      const reasons: Record<string, string> = { "pending-unsupported": health.stUnsupported, "pending-unknown": health.stUnknown };
      return health.stPostponed.replace("{reason}", reasons[String(state.postponedReason ?? "")] ?? health.stCantCheck);
    }
    case "WaitingForChange": return text.waiting;
    default: return text.off;
  }
}

// What the agent answered to "Update Steam now": a confirmation to ask for, or the job started (or busy).
export function steamUpdateAnswer(answer: any, locale: string): { message: string; confirm: boolean } {
  const text = steamUpdateCopy(locale);
  const code = typeof answer?.confirm === "string" ? answer.confirm : "";
  if (code === "confirm-incompatible") return { message: text.confirmIncompatible, confirm: true };
  if (code === "confirm-incompatible-again") return { message: text.confirmAgain, confirm: true };
  if (code) return { message: text.confirmCantTell, confirm: true };
  if (answer?.ok === true) return { message: text.started, confirm: false };
  return { message: text.busy, confirm: false };
}

export function SteamUpdateCard({ locale }: { locale: string }) {
  const [state, setState] = React.useState<SteamUpdateState | undefined>();
  const [message, setMessage] = React.useState<string | undefined>();
  const [confirmations, setConfirmations] = React.useState(0);
  const [busy, setBusy] = React.useState(false);
  const text = steamUpdateCopy(locale);
  React.useEffect(() => {
    let alive = true;
    const load = () => { call<[], SteamUpdateState>("core_steam_update").then(value => { if (alive) setState(value); }).catch(() => undefined); };
    load();
    const timer = window.setInterval(load, 60000);
    return () => { alive = false; window.clearInterval(timer); };
  }, []);
  if (!state || !state.holdPhase) return null;
  const press = async () => {
    setBusy(true);
    try {
      const answer = await call<[number], any>("core_steam_update_now", confirmations);
      const result = steamUpdateAnswer(answer, locale);
      setMessage(result.message);
      setConfirmations(result.confirm ? Math.min(confirmations + 1, 2) : 0);
    } catch { setMessage(text.busy); }
    finally { setBusy(false); }
  };
  const running = state.busy === true && (state.action === "update-steam" || state.action === "verify-steam");
  return (
    <PanelSection title={text.title}>
      <PanelSectionRow>
        <div style={{ fontSize: 13, lineHeight: 1.5, color: CORE.steel, margin: "0 0 8px", paddingLeft: 9, borderLeft: `3px solid ${state.holdPhase === "Postponed" || state.holdPhase === "WaitingForChange" ? CORE.warning : CORE.gunmetalRaised}` }}>
          {message ?? steamUpdateLine(state, locale)}
        </div>
      </PanelSectionRow>
      <PanelSectionRow>
        <ButtonItem layout="below" disabled={busy || running} onClick={press}>
          {confirmations > 0 ? text.again : text.now}
        </ButtonItem>
      </PanelSectionRow>
    </PanelSection>
  );
}
