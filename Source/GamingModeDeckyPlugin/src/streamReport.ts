// Eternal Core: the Stream mode section of Quick Access > Core. Turns the agent's GET /stream/state
// answer (GamingMode.Services/MarkIIIHost.Stream.cs) into what StreamSection.tsx shows, in Core's 12
// languages. Self-contained on purpose: tests/coreStreamMode.test.mjs loads this file alone.

export type StreamAnswer = {
  active?: boolean; forced?: boolean; detected?: boolean; source?: string; device?: string | null; sources?: string[]; since?: string | null;
  audio?: { enabled?: boolean; mode?: string | null; device?: string | null; pending?: number };
  apollo?: { installed?: boolean; running?: boolean; virtualDisplay?: boolean };
  sunshine?: { installed?: boolean; running?: boolean };
};
// state: "on" or "off" ("unknown" without an answer). line: the row's value. detail: what it means.
export type StreamView = { state: "on" | "off" | "unknown"; line: string; detail: string; forced: boolean; audio: string; apollo: string };

type Copy = Record<string, string>;

// Product names: the same in every language.
export const STREAM_SOURCES: Record<string, string> = { steam: "Steam Remote Play", steamvr: "SteamVR", sunshine: "Sunshine", apollo: "Apollo" };

const en: Copy = {
  title: "Stream mode", agentDown: "Core's service is not answering.", off: "Off", onDevice: "On · {device}", onSource: "On · {source}", forcedOn: "On · kept on by you", keep: "Keep TV out of it", keepHint: "Stream mode stays on even when Core sees no stream.", heldDetail: "Core does not wake the TV, switch its input or send it anything, and holds its notices.", offDetail: "Turns on by itself when this PC streams to another device.", audioVirtual: "Sound: on the stream ({device})", audioAlready: "Sound: already on the stream", audioMuted: "Sound: TV speaker muted", audioNone: "Sound: no TV speaker to mute", audioOff: "Sound: left as it is (turned off in settings)", apolloMissing: "Apollo: not installed (optional, see Core's guide)", apolloIdle: "Apollo: installed, not streaming", apolloDisplay: "Apollo: virtual display active", apolloNoDisplay: "Apollo: streaming without a virtual display",
};

const copy: Record<string, Copy> = {
  en,
  it: {
    title: "Modalità streaming", agentDown: "Il servizio di Core non risponde.", off: "Spenta", onDevice: "Attiva · {device}", onSource: "Attiva · {source}", forcedOn: "Attiva · tenuta accesa da te", keep: "Lascia stare la TV", keepHint: "La modalità streaming resta attiva anche quando Core non vede uno streaming.", heldDetail: "Core non accende la TV, non cambia ingresso, non le invia nulla e trattiene i suoi avvisi.", offDetail: "Si attiva da sola quando questo PC trasmette a un altro dispositivo.", audioVirtual: "Audio: sullo streaming ({device})", audioAlready: "Audio: già sullo streaming", audioMuted: "Audio: altoparlante della TV silenziato", audioNone: "Audio: nessun altoparlante della TV da silenziare", audioOff: "Audio: lasciato com'è (disattivato nelle impostazioni)", apolloMissing: "Apollo: non installato (facoltativo, vedi la guida di Core)", apolloIdle: "Apollo: installato, nessuno streaming", apolloDisplay: "Apollo: schermo virtuale attivo", apolloNoDisplay: "Apollo: streaming senza schermo virtuale",
  },
  es: {
    title: "Modo streaming", agentDown: "El servicio de Core no responde.", off: "Desactivado", onDevice: "Activado · {device}", onSource: "Activado · {source}", forcedOn: "Activado · lo mantienes tú", keep: "Deja la TV tranquila", keepHint: "El modo streaming sigue activado aunque Core no vea ninguna transmisión.", heldDetail: "Core no enciende la TV, no cambia su entrada ni le envía nada, y retiene sus avisos.", offDetail: "Se activa solo cuando este PC transmite a otro dispositivo.", audioVirtual: "Sonido: en la transmisión ({device})", audioAlready: "Sonido: ya en la transmisión", audioMuted: "Sonido: altavoz de la TV silenciado", audioNone: "Sonido: no hay altavoz de la TV que silenciar", audioOff: "Sonido: sin cambios (desactivado en los ajustes)", apolloMissing: "Apollo: no instalado (opcional, ver la guía de Core)", apolloIdle: "Apollo: instalado, sin transmitir", apolloDisplay: "Apollo: pantalla virtual activa", apolloNoDisplay: "Apollo: transmitiendo sin pantalla virtual",
  },
  fr: {
    title: "Mode streaming", agentDown: "Le service de Core ne répond pas.", off: "Désactivé", onDevice: "Activé · {device}", onSource: "Activé · {source}", forcedOn: "Activé · maintenu par vous", keep: "Laisser la TV tranquille", keepHint: "Le mode streaming reste actif même si Core ne voit aucun streaming.", heldDetail: "Core n'allume pas la TV, ne change pas son entrée, ne lui envoie rien et retient ses avis.", offDetail: "S'active tout seul quand ce PC diffuse vers un autre appareil.", audioVirtual: "Son : sur le streaming ({device})", audioAlready: "Son : déjà sur le streaming", audioMuted: "Son : haut-parleur de la TV coupé", audioNone: "Son : aucun haut-parleur de TV à couper", audioOff: "Son : inchangé (désactivé dans les réglages)", apolloMissing: "Apollo : non installé (facultatif, voir le guide de Core)", apolloIdle: "Apollo : installé, pas de streaming", apolloDisplay: "Apollo : écran virtuel actif", apolloNoDisplay: "Apollo : streaming sans écran virtuel",
  },
  de: {
    title: "Streaming-Modus", agentDown: "Der Dienst von Core antwortet nicht.", off: "Aus", onDevice: "An · {device}", onSource: "An · {source}", forcedOn: "An · von dir eingeschaltet", keep: "Fernseher in Ruhe lassen", keepHint: "Der Streaming-Modus bleibt an, auch wenn Core kein Streaming sieht.", heldDetail: "Core schaltet den Fernseher nicht ein, wechselt nicht den Eingang, sendet ihm nichts und hält seine Hinweise zurück.", offDetail: "Schaltet sich selbst ein, wenn dieser PC an ein anderes Gerät streamt.", audioVirtual: "Ton: im Stream ({device})", audioAlready: "Ton: schon im Stream", audioMuted: "Ton: Fernseherlautsprecher stumm", audioNone: "Ton: kein Fernseherlautsprecher zum Stummschalten", audioOff: "Ton: unverändert (in den Einstellungen aus)", apolloMissing: "Apollo: nicht installiert (optional, siehe Core-Anleitung)", apolloIdle: "Apollo: installiert, kein Streaming", apolloDisplay: "Apollo: virtueller Bildschirm aktiv", apolloNoDisplay: "Apollo: Streaming ohne virtuellen Bildschirm",
  },
  pt: {
    title: "Modo streaming", agentDown: "O serviço do Core não responde.", off: "Desligado", onDevice: "Ligado · {device}", onSource: "Ligado · {source}", forcedOn: "Ligado · mantido por você", keep: "Deixe a TV em paz", keepHint: "O modo streaming continua ligado mesmo quando o Core não vê nenhuma transmissão.", heldDetail: "O Core não liga a TV, não troca a entrada, não envia nada a ela e segura os avisos.", offDetail: "Liga sozinho quando este PC transmite para outro dispositivo.", audioVirtual: "Som: na transmissão ({device})", audioAlready: "Som: já na transmissão", audioMuted: "Som: alto-falante da TV silenciado", audioNone: "Som: nenhum alto-falante da TV para silenciar", audioOff: "Som: sem mudanças (desligado nas configurações)", apolloMissing: "Apollo: não instalado (opcional, veja o guia do Core)", apolloIdle: "Apollo: instalado, sem transmitir", apolloDisplay: "Apollo: tela virtual ativa", apolloNoDisplay: "Apollo: transmitindo sem tela virtual",
  },
  uk: {
    title: "Режим трансляції", agentDown: "Служба Core не відповідає.", off: "Вимкнено", onDevice: "Увімкнено · {device}", onSource: "Увімкнено · {source}", forcedOn: "Увімкнено · вами", keep: "Не чіпати телевізор", keepHint: "Режим трансляції лишається ввімкненим, навіть коли Core не бачить трансляції.", heldDetail: "Core не вмикає телевізор, не перемикає вхід, нічого йому не надсилає і притримує свої сповіщення.", offDetail: "Вмикається сам, коли цей ПК транслює на інший пристрій.", audioVirtual: "Звук: у трансляції ({device})", audioAlready: "Звук: уже в трансляції", audioMuted: "Звук: динамік телевізора вимкнено", audioNone: "Звук: немає динаміка телевізора, щоб вимкнути", audioOff: "Звук: без змін (вимкнено в налаштуваннях)", apolloMissing: "Apollo: не встановлено (необов'язково, див. посібник Core)", apolloIdle: "Apollo: встановлено, трансляції немає", apolloDisplay: "Apollo: віртуальний екран активний", apolloNoDisplay: "Apollo: трансляція без віртуального екрана",
  },
  ru: {
    title: "Режим трансляции", agentDown: "Служба Core не отвечает.", off: "Выключен", onDevice: "Включён · {device}", onSource: "Включён · {source}", forcedOn: "Включён · вами", keep: "Не трогать телевизор", keepHint: "Режим трансляции остаётся включённым, даже когда Core не видит трансляции.", heldDetail: "Core не включает телевизор, не переключает вход, ничего ему не отправляет и придерживает свои уведомления.", offDetail: "Включается сам, когда этот ПК транслирует на другое устройство.", audioVirtual: "Звук: в трансляции ({device})", audioAlready: "Звук: уже в трансляции", audioMuted: "Звук: динамик телевизора выключен", audioNone: "Звук: нет динамика телевизора, чтобы выключить", audioOff: "Звук: без изменений (выключено в настройках)", apolloMissing: "Apollo: не установлен (необязательно, см. руководство Core)", apolloIdle: "Apollo: установлен, трансляции нет", apolloDisplay: "Apollo: виртуальный экран активен", apolloNoDisplay: "Apollo: трансляция без виртуального экрана",
  },
  zh: {
    title: "串流模式", agentDown: "Core 的服务没有响应。", off: "关", onDevice: "开 · {device}", onSource: "开 · {source}", forcedOn: "开 · 由你保持开启", keep: "不要动电视", keepHint: "即使 Core 没有检测到串流，串流模式也保持开启。", heldDetail: "Core 不会开启电视、切换输入或向电视发送任何内容，并暂缓它的通知。", offDetail: "当这台电脑向其他设备串流时自动开启。", audioVirtual: "声音：在串流中（{device}）", audioAlready: "声音：已在串流中", audioMuted: "声音：电视扬声器已静音", audioNone: "声音：没有可静音的电视扬声器", audioOff: "声音：保持不变（已在设置中关闭）", apolloMissing: "Apollo：未安装（可选，见 Core 指南）", apolloIdle: "Apollo：已安装，未在串流", apolloDisplay: "Apollo：虚拟显示器已启用", apolloNoDisplay: "Apollo：串流中，没有虚拟显示器",
  },
  ja: {
    title: "ストリーミングモード", agentDown: "Core のサービスが応答しません。", off: "オフ", onDevice: "オン · {device}", onSource: "オン · {source}", forcedOn: "オン · 手動でオン", keep: "テレビに触れない", keepHint: "Core がストリーミングを検出しなくても、ストリーミングモードをオンのままにします。", heldDetail: "Core はテレビの電源を入れず、入力を切り替えず、何も送らず、通知も控えます。", offDetail: "この PC が別のデバイスへストリーミングすると自動でオンになります。", audioVirtual: "音声：ストリーム側（{device}）", audioAlready: "音声：すでにストリーム側", audioMuted: "音声：テレビのスピーカーをミュート", audioNone: "音声：ミュートするテレビのスピーカーがありません", audioOff: "音声：変更なし（設定でオフ）", apolloMissing: "Apollo：未インストール（任意、Core のガイドを参照）", apolloIdle: "Apollo：インストール済み、ストリーミングなし", apolloDisplay: "Apollo：仮想ディスプレイ有効", apolloNoDisplay: "Apollo：仮想ディスプレイなしでストリーミング中",
  },
  ko: {
    title: "스트리밍 모드", agentDown: "Core 서비스가 응답하지 않습니다.", off: "꺼짐", onDevice: "켜짐 · {device}", onSource: "켜짐 · {source}", forcedOn: "켜짐 · 직접 켬", keep: "TV 건드리지 않기", keepHint: "Core가 스트리밍을 감지하지 않아도 스트리밍 모드를 켜 둡니다.", heldDetail: "Core는 TV를 켜거나 입력을 바꾸거나 아무것도 보내지 않으며 알림도 보류합니다.", offDetail: "이 PC가 다른 기기로 스트리밍하면 자동으로 켜집니다.", audioVirtual: "소리: 스트림으로 ({device})", audioAlready: "소리: 이미 스트림으로", audioMuted: "소리: TV 스피커 음소거", audioNone: "소리: 음소거할 TV 스피커 없음", audioOff: "소리: 그대로 (설정에서 꺼짐)", apolloMissing: "Apollo: 설치되지 않음 (선택 사항, Core 가이드 참고)", apolloIdle: "Apollo: 설치됨, 스트리밍 안 함", apolloDisplay: "Apollo: 가상 디스플레이 사용 중", apolloNoDisplay: "Apollo: 가상 디스플레이 없이 스트리밍 중",
  },
  hi: {
    title: "स्ट्रीम मोड", agentDown: "Core की सेवा जवाब नहीं दे रही है।", off: "बंद", onDevice: "चालू · {device}", onSource: "चालू · {source}", forcedOn: "चालू · आपने चालू रखा", keep: "टीवी को न छुएँ", keepHint: "Core को कोई स्ट्रीम न दिखे तब भी स्ट्रीम मोड चालू रहता है।", heldDetail: "Core टीवी को चालू नहीं करता, इनपुट नहीं बदलता, उसे कुछ नहीं भेजता और अपनी सूचनाएँ रोक कर रखता है।", offDetail: "जब यह पीसी किसी दूसरे डिवाइस पर स्ट्रीम करता है तो अपने आप चालू होता है।", audioVirtual: "आवाज़: स्ट्रीम पर ({device})", audioAlready: "आवाज़: पहले से स्ट्रीम पर", audioMuted: "आवाज़: टीवी स्पीकर म्यूट", audioNone: "आवाज़: म्यूट करने के लिए कोई टीवी स्पीकर नहीं", audioOff: "आवाज़: जैसी थी वैसी (सेटिंग में बंद)", apolloMissing: "Apollo: इंस्टॉल नहीं (वैकल्पिक, Core गाइड देखें)", apolloIdle: "Apollo: इंस्टॉल है, स्ट्रीम नहीं", apolloDisplay: "Apollo: वर्चुअल डिस्प्ले चालू", apolloNoDisplay: "Apollo: वर्चुअल डिस्प्ले के बिना स्ट्रीम",
  },
};

export const streamLanguages = Object.keys(copy);

export function streamCopy(locale: string): Copy {
  const language = String(locale || "en").toLowerCase().split(/[-_]/)[0];
  const table = copy[language === "ua" ? "uk" : language] ?? en;
  return new Proxy(table, { get: (target, key: string) => target[key] ?? en[key] ?? key });
}

function fill(template: string, values: Record<string, string>): string {
  return template.replace(/\{(\w+)\}/g, (all, name) => (name in values ? values[name] : all));
}

function clean(value: unknown): string {
  return typeof value === "string" ? value.replace(/[\u0000-\u001f\u007f]/g, "").trim().slice(0, 64) : "";
}

export function streamSourceLabel(source: unknown): string {
  return typeof source === "string" && STREAM_SOURCES[source] ? STREAM_SOURCES[source] : "";
}

export function streamView(answer: StreamAnswer | null | undefined, locale: string): StreamView {
  const text = streamCopy(locale);
  if (!answer || typeof answer.active !== "boolean") return { state: "unknown", line: answer === null ? text.agentDown : "…", detail: "", forced: false, audio: "", apollo: "" };
  const forced = answer.forced === true;
  const apollo = apolloLine(answer, text);
  if (!answer.active) return { state: "off", line: text.off, detail: text.offDetail, forced, audio: "", apollo };
  const device = clean(answer.device), source = streamSourceLabel(answer.source);
  const line = answer.detected ? (device ? fill(text.onDevice, { device }) : source ? fill(text.onSource, { source }) : text.forcedOn) : text.forcedOn;
  return { state: "on", line, detail: text.heldDetail, forced, audio: audioLine(answer, text), apollo };
}

function audioLine(answer: StreamAnswer, text: Copy): string {
  const audio = answer.audio ?? {};
  if (audio.enabled === false) return text.audioOff;
  switch (audio.mode) {
    case "virtual": return fill(text.audioVirtual, { device: clean(audio.device) || "Steam Streaming Speakers" });
    case "already": return text.audioAlready;
    case "muted": return text.audioMuted;
    case "none": return text.audioNone;
    default: return "";
  }
}

// Apollo is optional (Core never installs it); the line only says what Core sees.
function apolloLine(answer: StreamAnswer, text: Copy): string {
  const apollo = answer.apollo ?? {};
  if (apollo.installed !== true) return text.apolloMissing;
  if (apollo.running !== true) return text.apolloIdle;
  return apollo.virtualDisplay === true ? text.apolloDisplay : text.apolloNoDisplay;
}
