// Eternal Core: the Voice section of Quick Access > Core. Turns the agent's GET /voice/state and
// POST /voice/party answers (GamingMode.Services/MarkIIIHost.Voice.cs) into what VoiceSection.tsx
// shows, in Core's 12 languages. Self-contained on purpose: tests/coreVoiceSection.test.mjs loads
// this file alone.

export type MicCode = "muted" | "live" | "none";
export type VoiceAnswer = {
  mic?: { state?: string; coreMuted?: boolean; defaults?: number; hotkey?: string | null };
  gameBar?: { installed?: boolean | null; open?: boolean | null };
};
export type PartyAnswer = { ok?: boolean; method?: string; reason?: string };
export type MicView = { state: MicCode | "unknown"; line: string; button: string; muted: boolean; detail: string };

type Copy = Record<string, string>;

const en: Copy = {
  title: "Voice", agentDown: "Core's service is not answering.",
  mute: "Mute microphone", unmute: "Unmute microphone", live: "Microphone live", muted: "Microphone muted", none: "No microphone", unknown: "Microphone: unknown",
  mutedElsewhere: "Muted outside Core", hotkey: "Shortcut {keys}: bind it to a controller chord in Steam",
  party: "Xbox party", partyHint: "Opens the Xbox Game Bar over Big Picture. Close it with B or Esc, or press the Steam button, to come back.",
  notInstalled: "The Xbox Game Bar is not installed.", protectedGame: "Not while a game with anti-cheat is running.", alreadyOpen: "The Game Bar is already open.", inputRefused: "Windows did not accept the Game Bar shortcut.",
  discord: "Discord", discordInstalled: "Installed: open it from Decky's tab", discordMissing: "Plugin not installed",
};

const copy: Record<string, Copy> = {
  en,
  it: {
    title: "Voce", agentDown: "Il servizio di Core non risponde.",
    mute: "Silenzia il microfono", unmute: "Riattiva il microfono", live: "Microfono attivo", muted: "Microfono silenziato", none: "Nessun microfono", unknown: "Microfono: sconosciuto",
    mutedElsewhere: "Silenziato fuori da Core", hotkey: "Scorciatoia {keys}: legala a una combinazione del controller in Steam",
    party: "Party Xbox", partyHint: "Apre la Xbox Game Bar sopra la Big Picture. Chiudila con B o Esc, oppure premi il tasto Steam, per tornare.",
    notInstalled: "La Xbox Game Bar non è installata.", protectedGame: "Non mentre è aperto un gioco con anti-cheat.", alreadyOpen: "La Game Bar è già aperta.", inputRefused: "Windows non ha accettato la scorciatoia della Game Bar.",
    discord: "Discord", discordInstalled: "Installato: aprilo dalla scheda di Decky", discordMissing: "Plugin non installato",
  },
  es: {
    title: "Voz", agentDown: "El servicio de Core no responde.",
    mute: "Silenciar micrófono", unmute: "Activar micrófono", live: "Micrófono activo", muted: "Micrófono silenciado", none: "No hay micrófono", unknown: "Micrófono: desconocido",
    mutedElsewhere: "Silenciado fuera de Core", hotkey: "Atajo {keys}: asígnalo a una combinación del mando en Steam",
    party: "Grupo de Xbox", partyHint: "Abre la Xbox Game Bar sobre Big Picture. Ciérrala con B o Esc, o pulsa el botón Steam, para volver.",
    notInstalled: "La Xbox Game Bar no está instalada.", protectedGame: "No mientras haya un juego con antitrampas abierto.", alreadyOpen: "La Game Bar ya está abierta.", inputRefused: "Windows no aceptó el atajo de la Game Bar.",
    discord: "Discord", discordInstalled: "Instalado: ábrelo desde la pestaña de Decky", discordMissing: "Plugin no instalado",
  },
  fr: {
    title: "Voix", agentDown: "Le service de Core ne répond pas.",
    mute: "Couper le micro", unmute: "Réactiver le micro", live: "Micro actif", muted: "Micro coupé", none: "Aucun micro", unknown: "Micro : inconnu",
    mutedElsewhere: "Coupé en dehors de Core", hotkey: "Raccourci {keys} : associez-le à une combinaison de la manette dans Steam",
    party: "Groupe Xbox", partyHint: "Ouvre la Xbox Game Bar par-dessus Big Picture. Fermez-la avec B ou Échap, ou appuyez sur le bouton Steam, pour revenir.",
    notInstalled: "La Xbox Game Bar n'est pas installée.", protectedGame: "Pas pendant un jeu protégé par un anti-triche.", alreadyOpen: "La Game Bar est déjà ouverte.", inputRefused: "Windows a refusé le raccourci de la Game Bar.",
    discord: "Discord", discordInstalled: "Installé : ouvrez-le depuis l'onglet Decky", discordMissing: "Plugin non installé",
  },
  de: {
    title: "Sprache", agentDown: "Der Dienst von Core antwortet nicht.",
    mute: "Mikrofon stummschalten", unmute: "Mikrofon einschalten", live: "Mikrofon an", muted: "Mikrofon stumm", none: "Kein Mikrofon", unknown: "Mikrofon: unbekannt",
    mutedElsewhere: "Außerhalb von Core stummgeschaltet", hotkey: "Tastenkürzel {keys}: lege es in Steam auf eine Controller-Kombination",
    party: "Xbox-Party", partyHint: "Öffnet die Xbox Game Bar über Big Picture. Schließe sie mit B oder Esc oder drücke die Steam-Taste, um zurückzukehren.",
    notInstalled: "Die Xbox Game Bar ist nicht installiert.", protectedGame: "Nicht während ein Spiel mit Anti-Cheat läuft.", alreadyOpen: "Die Game Bar ist schon offen.", inputRefused: "Windows hat das Game-Bar-Kürzel nicht angenommen.",
    discord: "Discord", discordInstalled: "Installiert: öffne es über den Decky-Tab", discordMissing: "Plugin nicht installiert",
  },
  pt: {
    title: "Voz", agentDown: "O serviço do Core não responde.",
    mute: "Silenciar microfone", unmute: "Ativar microfone", live: "Microfone ativo", muted: "Microfone silenciado", none: "Sem microfone", unknown: "Microfone: desconhecido",
    mutedElsewhere: "Silenciado fora do Core", hotkey: "Atalho {keys}: associe-o a uma combinação do controle no Steam",
    party: "Grupo Xbox", partyHint: "Abre a Xbox Game Bar sobre o Big Picture. Feche-a com B ou Esc, ou aperte o botão Steam, para voltar.",
    notInstalled: "A Xbox Game Bar não está instalada.", protectedGame: "Não enquanto um jogo com antitrapaça estiver aberto.", alreadyOpen: "A Game Bar já está aberta.", inputRefused: "O Windows não aceitou o atalho da Game Bar.",
    discord: "Discord", discordInstalled: "Instalado: abra-o pela aba do Decky", discordMissing: "Plugin não instalado",
  },
  uk: {
    title: "Голос", agentDown: "Служба Core не відповідає.",
    mute: "Вимкнути мікрофон", unmute: "Увімкнути мікрофон", live: "Мікрофон увімкнено", muted: "Мікрофон вимкнено", none: "Немає мікрофона", unknown: "Мікрофон: невідомо",
    mutedElsewhere: "Вимкнено поза Core", hotkey: "Скорочення {keys}: призначте його комбінації контролера в Steam",
    party: "Група Xbox", partyHint: "Відкриває Xbox Game Bar поверх Big Picture. Щоб повернутися, закрийте її кнопкою B чи Esc або натисніть кнопку Steam.",
    notInstalled: "Xbox Game Bar не встановлено.", protectedGame: "Не під час гри з античитом.", alreadyOpen: "Game Bar уже відкрито.", inputRefused: "Windows не прийняла скорочення Game Bar.",
    discord: "Discord", discordInstalled: "Встановлено: відкрийте його на вкладці Decky", discordMissing: "Плагін не встановлено",
  },
  ru: {
    title: "Голос", agentDown: "Служба Core не отвечает.",
    mute: "Выключить микрофон", unmute: "Включить микрофон", live: "Микрофон включён", muted: "Микрофон выключен", none: "Нет микрофона", unknown: "Микрофон: неизвестно",
    mutedElsewhere: "Выключен вне Core", hotkey: "Сочетание {keys}: назначьте его комбинации контроллера в Steam",
    party: "Группа Xbox", partyHint: "Открывает Xbox Game Bar поверх Big Picture. Чтобы вернуться, закройте её кнопкой B или Esc либо нажмите кнопку Steam.",
    notInstalled: "Xbox Game Bar не установлена.", protectedGame: "Не во время игры с античитом.", alreadyOpen: "Game Bar уже открыта.", inputRefused: "Windows не приняла сочетание Game Bar.",
    discord: "Discord", discordInstalled: "Установлен: откройте его на вкладке Decky", discordMissing: "Плагин не установлен",
  },
  zh: {
    title: "语音", agentDown: "Core 的服务没有响应。",
    mute: "关闭麦克风", unmute: "打开麦克风", live: "麦克风已开启", muted: "麦克风已静音", none: "没有麦克风", unknown: "麦克风：未知",
    mutedElsewhere: "已在 Core 之外静音", hotkey: "快捷键 {keys}：在 Steam 中将其绑定到手柄组合键",
    party: "Xbox 派对", partyHint: "在 Big Picture 上打开 Xbox Game Bar。按 B 或 Esc 关闭它，或按 Steam 键返回。",
    notInstalled: "未安装 Xbox Game Bar。", protectedGame: "运行带反作弊的游戏时不可用。", alreadyOpen: "Game Bar 已打开。", inputRefused: "Windows 未接受 Game Bar 快捷键。",
    discord: "Discord", discordInstalled: "已安装：从 Decky 标签页打开", discordMissing: "未安装插件",
  },
  ja: {
    title: "ボイス", agentDown: "Core のサービスが応答しません。",
    mute: "マイクをミュート", unmute: "マイクのミュートを解除", live: "マイクはオン", muted: "マイクはミュート中", none: "マイクがありません", unknown: "マイク：不明",
    mutedElsewhere: "Core 以外でミュート", hotkey: "ショートカット {keys}：Steam でコントローラーのコードに割り当ててください",
    party: "Xbox パーティー", partyHint: "Big Picture の上に Xbox Game Bar を開きます。戻るには B か Esc で閉じるか、Steam ボタンを押します。",
    notInstalled: "Xbox Game Bar がインストールされていません。", protectedGame: "アンチチート付きのゲーム中は使えません。", alreadyOpen: "Game Bar はすでに開いています。", inputRefused: "Windows が Game Bar のショートカットを受け付けませんでした。",
    discord: "Discord", discordInstalled: "インストール済み：Decky のタブから開きます", discordMissing: "プラグイン未インストール",
  },
  ko: {
    title: "음성", agentDown: "Core 서비스가 응답하지 않습니다.",
    mute: "마이크 음소거", unmute: "마이크 음소거 해제", live: "마이크 켜짐", muted: "마이크 음소거됨", none: "마이크 없음", unknown: "마이크: 알 수 없음",
    mutedElsewhere: "Core 밖에서 음소거됨", hotkey: "단축키 {keys}: Steam에서 컨트롤러 조합에 연결하세요",
    party: "Xbox 파티", partyHint: "Big Picture 위에 Xbox Game Bar를 엽니다. 돌아오려면 B나 Esc로 닫거나 Steam 버튼을 누르세요.",
    notInstalled: "Xbox Game Bar가 설치되어 있지 않습니다.", protectedGame: "안티치트 게임 실행 중에는 사용할 수 없습니다.", alreadyOpen: "Game Bar가 이미 열려 있습니다.", inputRefused: "Windows가 Game Bar 단축키를 받아들이지 않았습니다.",
    discord: "Discord", discordInstalled: "설치됨: Decky 탭에서 여세요", discordMissing: "플러그인 미설치",
  },
  hi: {
    title: "आवाज़", agentDown: "Core की सेवा जवाब नहीं दे रही है।",
    mute: "माइक्रोफ़ोन म्यूट करें", unmute: "माइक्रोफ़ोन अनम्यूट करें", live: "माइक्रोफ़ोन चालू", muted: "माइक्रोफ़ोन म्यूट", none: "कोई माइक्रोफ़ोन नहीं", unknown: "माइक्रोफ़ोन: अज्ञात",
    mutedElsewhere: "Core के बाहर म्यूट किया गया", hotkey: "शॉर्टकट {keys}: इसे Steam में कंट्रोलर कॉम्बिनेशन से जोड़ें",
    party: "Xbox पार्टी", partyHint: "Big Picture के ऊपर Xbox Game Bar खोलता है। लौटने के लिए इसे B या Esc से बंद करें, या Steam बटन दबाएँ।",
    notInstalled: "Xbox Game Bar इंस्टॉल नहीं है।", protectedGame: "एंटी-चीट वाला गेम चलते समय नहीं।", alreadyOpen: "Game Bar पहले से खुला है।", inputRefused: "Windows ने Game Bar का शॉर्टकट स्वीकार नहीं किया।",
    discord: "Discord", discordInstalled: "इंस्टॉल है: इसे Decky टैब से खोलें", discordMissing: "प्लगइन इंस्टॉल नहीं है",
  },
};

export const voiceLanguages = Object.keys(copy);

export function voiceCopy(locale: string): Copy {
  const language = String(locale || "en").toLowerCase().split(/[-_]/)[0];
  const table = copy[language === "ua" ? "uk" : language] ?? en;
  return new Proxy(table, { get: (target, key: string) => target[key] ?? en[key] ?? key });
}

function fill(template: string, values: Record<string, string>): string {
  return template.replace(/\{(\w+)\}/g, (all, name) => (name in values ? values[name] : all));
}

export function micView(answer: VoiceAnswer | undefined | null, locale: string): MicView {
  const text = voiceCopy(locale);
  const mic = answer?.mic;
  const state = mic?.state === "muted" || mic?.state === "live" || mic?.state === "none" ? mic.state : "unknown";
  const muted = state === "muted";
  const hotkey = typeof mic?.hotkey === "string" && mic.hotkey.trim() && mic.hotkey.length <= 40 ? fill(text.hotkey, { keys: mic.hotkey.trim() }) : "";
  const detail = muted && mic?.coreMuted === false ? text.mutedElsewhere : hotkey;
  return { state, muted, line: text[state], button: muted ? text.unmute : text.mute, detail };
}

// Empty when the bar was opened: Quick Access is already closed by then.
export function partyMessage(answer: PartyAnswer | undefined | null, locale: string): string {
  const text = voiceCopy(locale);
  if (!answer || typeof answer !== "object" || !answer.method) return text.agentDown;
  if (answer.ok) return answer.reason === "alreadyOpen" ? text.alreadyOpen : "";
  return ({ notInstalled: text.notInstalled, protectedGame: text.protectedGame, inputRefused: text.inputRefused } as Record<string, string>)[answer.reason ?? ""] ?? text.inputRefused;
}

// Eternal-Assemble keeps upstream's Decky name "Discord" (Source/Shared/ProtectedPluginForks.json).
export const DISCORD_PLUGIN_NAMES = ["Discord"];

export function discordInstalled(inventory: any): boolean {
  const names = [...(Array.isArray(inventory?.plugins) ? inventory.plugins : []), ...(Array.isArray(inventory?.installedPlugins) ? inventory.installedPlugins : [])]
    .map((plugin: any) => String(typeof plugin === "string" ? plugin : plugin?.name ?? "").toLowerCase());
  return DISCORD_PLUGIN_NAMES.some(name => names.includes(name.toLowerCase()));
}
