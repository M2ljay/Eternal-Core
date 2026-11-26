// Eternal Core: the Voice section of Quick Access > Core. Turns the agent's GET /voice/state and
// POST /voice/party answers (GamingMode.Services/MarkIIIHost.Voice.cs) into what VoiceSection.tsx
// shows, in Core's 12 languages. Self-contained on purpose: tests/coreVoiceSection.test.mjs loads
// this file alone.

export type MicCode = "muted" | "live" | "partial" | "none";
export type VoiceDevice = { id?: string; name?: string | null; console?: boolean; communications?: boolean; muted?: boolean };
export type VoiceAnswer = {
  mic?: { state?: string; coreMuted?: boolean; defaults?: number; communications?: string | null; pending?: number; hotkey?: string | null; scope?: string };
  devices?: VoiceDevice[];
  callsMic?: { id?: string | null; name?: string | null; status?: string; everything?: boolean; error?: string | null };
  result?: string | null;
  gameBar?: { installed?: boolean | null; open?: boolean | null };
};
export type PickerOption = { data: string; label: string };
// The calls mic picker. selected: "" for Windows' own choice. line: the status under the picker
// (calm, information only when the mic is not connected).
export type CallsMicView = { options: PickerOption[]; selected: string; status: string; line: string; everything: boolean; canToggleEverything: boolean };
export type ScopeView = { options: PickerOption[]; selected: "all" | "calls"; hint: string };
export type PartyAnswer = { ok?: boolean; method?: string; reason?: string };
export type MicView = { state: MicCode | "unknown"; line: string; button: string; muted: boolean; detail: string };

type Copy = Record<string, string>;

const en: Copy = {
  title: "Voice", agentDown: "Core's service is not answering.",
  mute: "Mute microphone", unmute: "Unmute microphone", live: "Microphone live", muted: "Microphone muted", none: "No microphone", unknown: "Microphone: unknown", partial: "Microphone partly muted", partialDetail: "One default microphone is muted, the other is live. Check which one your call uses.",
  mutedElsewhere: "Muted outside Core", hotkey: "Shortcut {keys}: bind it to a controller chord in Steam",
  party: "Xbox party", partyHint: "Opens the Xbox Game Bar over Big Picture. Close it with B or Esc, or press the Steam button, to come back.",
  notInstalled: "The Xbox Game Bar is not installed.", protectedGame: "Not while a game with anti-cheat is running.", alreadyOpen: "The Game Bar is already open.", inputRefused: "Windows did not accept the Game Bar shortcut.", uriFailed: "Windows could not open the Game Bar.",
  discord: "Discord", discordInstalled: "Installed: open it from Decky's tab", discordMissing: "Plugin not installed",
  callsMic: "Calls mic", callsMicHint: "Windows' default microphone for calls: Xbox party, Discord.", windowsChoice: "Windows' choice", unnamed: "Microphone", tagCalls: "calls", tagApps: "games and apps", notConnected: "not connected", cmActive: "Connected and used for calls.", cmAbsent: "Not connected. Calls use Windows' default microphone.", cmChanged: "Windows uses another microphone for calls. Pick it again to use it.", cmFailed: "Windows did not let Core set the default. Pick it again, or set it in Windows' sound settings.", cmNotFound: "That microphone is no longer connected.",
  everything: "Use for everything", everythingHint: "Also the default for games and apps.", scope: "Mute", scopeAll: "Mute everything", scopeCalls: "Mute calls only", scopeAllHint: "Mutes the calls mic and the games and apps mic.", scopeCallsHint: "Mutes only the calls mic; the games and apps mic stays live. Windows mutes a microphone for every app at once, so an app that records from the calls mic itself (like OBS) is muted too.",
};

const copy: Record<string, Copy> = {
  en,
  it: {
    title: "Voce", agentDown: "Il servizio di Core non risponde.",
    mute: "Silenzia il microfono", unmute: "Riattiva il microfono", live: "Microfono attivo", muted: "Microfono silenziato", none: "Nessun microfono", unknown: "Microfono: sconosciuto", partial: "Microfono silenziato in parte", partialDetail: "Un microfono predefinito è silenziato, l'altro è attivo. Controlla quale usa la chiamata.",
    mutedElsewhere: "Silenziato fuori da Core", hotkey: "Scorciatoia {keys}: legala a una combinazione del controller in Steam",
    party: "Party Xbox", partyHint: "Apre la Xbox Game Bar sopra la Big Picture. Chiudila con B o Esc, oppure premi il tasto Steam, per tornare.",
    notInstalled: "La Xbox Game Bar non è installata.", protectedGame: "Non mentre è aperto un gioco con anti-cheat.", alreadyOpen: "La Game Bar è già aperta.", inputRefused: "Windows non ha accettato la scorciatoia della Game Bar.", uriFailed: "Windows non è riuscito ad aprire la Game Bar.",
    discord: "Discord", discordInstalled: "Installato: aprilo dalla scheda di Decky", discordMissing: "Plugin non installato",
    callsMic: "Microfono per le chiamate", callsMicHint: "Il microfono predefinito di Windows per le chiamate: party Xbox, Discord.", windowsChoice: "Scelta di Windows", unnamed: "Microfono", tagCalls: "chiamate", tagApps: "giochi e app", notConnected: "non collegato", cmActive: "Collegato e usato per le chiamate.", cmAbsent: "Non collegato. Le chiamate usano il microfono predefinito di Windows.", cmChanged: "Windows usa un altro microfono per le chiamate. Sceglilo di nuovo per usarlo.", cmFailed: "Windows non ha permesso a Core di impostarlo come predefinito. Sceglilo di nuovo o impostalo nelle impostazioni audio di Windows.", cmNotFound: "Quel microfono non è più collegato.",
    everything: "Usalo per tutto", everythingHint: "Predefinito anche per giochi e app.", scope: "Silenzia", scopeAll: "Silenzia tutto", scopeCalls: "Silenzia solo le chiamate", scopeAllHint: "Silenzia il microfono delle chiamate e quello di giochi e app.", scopeCallsHint: "Silenzia solo il microfono delle chiamate; quello di giochi e app resta attivo. Windows silenzia un microfono per tutte le app insieme, quindi anche un'app che registra direttamente dal microfono delle chiamate (come OBS) viene silenziata.",
  },
  es: {
    title: "Voz", agentDown: "El servicio de Core no responde.",
    mute: "Silenciar micrófono", unmute: "Activar micrófono", live: "Micrófono activo", muted: "Micrófono silenciado", none: "No hay micrófono", unknown: "Micrófono: desconocido", partial: "Micrófono silenciado en parte", partialDetail: "Un micrófono predeterminado está silenciado y el otro activo. Comprueba cuál usa la llamada.",
    mutedElsewhere: "Silenciado fuera de Core", hotkey: "Atajo {keys}: asígnalo a una combinación del mando en Steam",
    party: "Grupo de Xbox", partyHint: "Abre la Xbox Game Bar sobre Big Picture. Ciérrala con B o Esc, o pulsa el botón Steam, para volver.",
    notInstalled: "La Xbox Game Bar no está instalada.", protectedGame: "No mientras haya un juego con antitrampas abierto.", alreadyOpen: "La Game Bar ya está abierta.", inputRefused: "Windows no aceptó el atajo de la Game Bar.", uriFailed: "Windows no pudo abrir la Game Bar.",
    discord: "Discord", discordInstalled: "Instalado: ábrelo desde la pestaña de Decky", discordMissing: "Plugin no instalado",
    callsMic: "Micrófono de llamadas", callsMicHint: "El micrófono predeterminado de Windows para llamadas: grupo de Xbox, Discord.", windowsChoice: "Elección de Windows", unnamed: "Micrófono", tagCalls: "llamadas", tagApps: "juegos y apps", notConnected: "no conectado", cmActive: "Conectado y usado para llamadas.", cmAbsent: "No conectado. Las llamadas usan el micrófono predeterminado de Windows.", cmChanged: "Windows usa otro micrófono para llamadas. Elígelo de nuevo para usarlo.", cmFailed: "Windows no dejó que Core lo pusiera como predeterminado. Elígelo de nuevo o cámbialo en la configuración de sonido de Windows.", cmNotFound: "Ese micrófono ya no está conectado.",
    everything: "Usar para todo", everythingHint: "También predeterminado para juegos y apps.", scope: "Silenciar", scopeAll: "Silenciar todo", scopeCalls: "Silenciar solo llamadas", scopeAllHint: "Silencia el micrófono de llamadas y el de juegos y apps.", scopeCallsHint: "Silencia solo el micrófono de llamadas; el de juegos y apps sigue activo. Windows silencia un micrófono para todas las apps a la vez, así que una app que grabe directamente del micrófono de llamadas (como OBS) también queda silenciada.",
  },
  fr: {
    title: "Voix", agentDown: "Le service de Core ne répond pas.",
    mute: "Couper le micro", unmute: "Réactiver le micro", live: "Micro actif", muted: "Micro coupé", none: "Aucun micro", unknown: "Micro : inconnu", partial: "Micro coupé en partie", partialDetail: "Un micro par défaut est coupé, l'autre est actif. Vérifiez celui qu'utilise l'appel.",
    mutedElsewhere: "Coupé en dehors de Core", hotkey: "Raccourci {keys} : associez-le à une combinaison de la manette dans Steam",
    party: "Groupe Xbox", partyHint: "Ouvre la Xbox Game Bar par-dessus Big Picture. Fermez-la avec B ou Échap, ou appuyez sur le bouton Steam, pour revenir.",
    notInstalled: "La Xbox Game Bar n'est pas installée.", protectedGame: "Pas pendant un jeu protégé par un anti-triche.", alreadyOpen: "La Game Bar est déjà ouverte.", inputRefused: "Windows a refusé le raccourci de la Game Bar.", uriFailed: "Windows n'a pas pu ouvrir la Game Bar.",
    discord: "Discord", discordInstalled: "Installé : ouvrez-le depuis l'onglet Decky", discordMissing: "Plugin non installé",
    callsMic: "Micro des appels", callsMicHint: "Le micro par défaut de Windows pour les appels : groupe Xbox, Discord.", windowsChoice: "Choix de Windows", unnamed: "Micro", tagCalls: "appels", tagApps: "jeux et applis", notConnected: "non connecté", cmActive: "Connecté et utilisé pour les appels.", cmAbsent: "Non connecté. Les appels utilisent le micro par défaut de Windows.", cmChanged: "Windows utilise un autre micro pour les appels. Choisissez-le à nouveau pour l'utiliser.", cmFailed: "Windows n'a pas laissé Core le définir par défaut. Choisissez-le à nouveau ou réglez-le dans les paramètres son de Windows.", cmNotFound: "Ce micro n'est plus connecté.",
    everything: "Utiliser pour tout", everythingHint: "Aussi par défaut pour les jeux et les applis.", scope: "Couper", scopeAll: "Tout couper", scopeCalls: "Couper les appels seulement", scopeAllHint: "Coupe le micro des appels et celui des jeux et applis.", scopeCallsHint: "Coupe seulement le micro des appels ; celui des jeux et applis reste actif. Windows coupe un micro pour toutes les applis à la fois : une appli qui enregistre directement depuis le micro des appels (comme OBS) est coupée aussi.",
  },
  de: {
    title: "Sprache", agentDown: "Der Dienst von Core antwortet nicht.",
    mute: "Mikrofon stummschalten", unmute: "Mikrofon einschalten", live: "Mikrofon an", muted: "Mikrofon stumm", none: "Kein Mikrofon", unknown: "Mikrofon: unbekannt", partial: "Mikrofon teilweise stumm", partialDetail: "Ein Standardmikrofon ist stumm, das andere an. Prüfe, welches der Anruf nutzt.",
    mutedElsewhere: "Außerhalb von Core stummgeschaltet", hotkey: "Tastenkürzel {keys}: lege es in Steam auf eine Controller-Kombination",
    party: "Xbox-Party", partyHint: "Öffnet die Xbox Game Bar über Big Picture. Schließe sie mit B oder Esc oder drücke die Steam-Taste, um zurückzukehren.",
    notInstalled: "Die Xbox Game Bar ist nicht installiert.", protectedGame: "Nicht während ein Spiel mit Anti-Cheat läuft.", alreadyOpen: "Die Game Bar ist schon offen.", inputRefused: "Windows hat das Game-Bar-Kürzel nicht angenommen.", uriFailed: "Windows konnte die Game Bar nicht öffnen.",
    discord: "Discord", discordInstalled: "Installiert: öffne es über den Decky-Tab", discordMissing: "Plugin nicht installiert",
    callsMic: "Anruf-Mikrofon", callsMicHint: "Das Standardmikrofon von Windows für Anrufe: Xbox-Party, Discord.", windowsChoice: "Auswahl von Windows", unnamed: "Mikrofon", tagCalls: "Anrufe", tagApps: "Spiele und Apps", notConnected: "nicht verbunden", cmActive: "Verbunden und für Anrufe genutzt.", cmAbsent: "Nicht verbunden. Anrufe nutzen das Standardmikrofon von Windows.", cmChanged: "Windows nutzt ein anderes Mikrofon für Anrufe. Wähle es erneut, um es zu nutzen.", cmFailed: "Windows hat Core nicht erlaubt, es als Standard festzulegen. Wähle es erneut oder stelle es in den Soundeinstellungen von Windows ein.", cmNotFound: "Dieses Mikrofon ist nicht mehr verbunden.",
    everything: "Für alles verwenden", everythingHint: "Auch Standard für Spiele und Apps.", scope: "Stummschalten", scopeAll: "Alles stummschalten", scopeCalls: "Nur Anrufe stummschalten", scopeAllHint: "Schaltet das Anruf-Mikrofon und das Mikrofon für Spiele und Apps stumm.", scopeCallsHint: "Schaltet nur das Anruf-Mikrofon stumm; das für Spiele und Apps bleibt an. Windows schaltet ein Mikrofon für alle Apps zugleich stumm, also auch eine App, die direkt vom Anruf-Mikrofon aufnimmt (etwa OBS).",
  },
  pt: {
    title: "Voz", agentDown: "O serviço do Core não responde.",
    mute: "Silenciar microfone", unmute: "Ativar microfone", live: "Microfone ativo", muted: "Microfone silenciado", none: "Sem microfone", unknown: "Microfone: desconhecido", partial: "Microfone silenciado em parte", partialDetail: "Um microfone padrão está silenciado e o outro ativo. Veja qual a chamada usa.",
    mutedElsewhere: "Silenciado fora do Core", hotkey: "Atalho {keys}: associe-o a uma combinação do controle no Steam",
    party: "Grupo Xbox", partyHint: "Abre a Xbox Game Bar sobre o Big Picture. Feche-a com B ou Esc, ou aperte o botão Steam, para voltar.",
    notInstalled: "A Xbox Game Bar não está instalada.", protectedGame: "Não enquanto um jogo com antitrapaça estiver aberto.", alreadyOpen: "A Game Bar já está aberta.", inputRefused: "O Windows não aceitou o atalho da Game Bar.", uriFailed: "O Windows não conseguiu abrir a Game Bar.",
    discord: "Discord", discordInstalled: "Instalado: abra-o pela aba do Decky", discordMissing: "Plugin não instalado",
    callsMic: "Microfone das chamadas", callsMicHint: "O microfone padrão do Windows para chamadas: grupo Xbox, Discord.", windowsChoice: "Escolha do Windows", unnamed: "Microfone", tagCalls: "chamadas", tagApps: "jogos e apps", notConnected: "não conectado", cmActive: "Conectado e usado nas chamadas.", cmAbsent: "Não conectado. As chamadas usam o microfone padrão do Windows.", cmChanged: "O Windows usa outro microfone nas chamadas. Escolha-o de novo para usá-lo.", cmFailed: "O Windows não deixou o Core defini-lo como padrão. Escolha-o de novo ou defina-o nas configurações de som do Windows.", cmNotFound: "Esse microfone não está mais conectado.",
    everything: "Usar para tudo", everythingHint: "Também padrão para jogos e apps.", scope: "Silenciar", scopeAll: "Silenciar tudo", scopeCalls: "Silenciar só as chamadas", scopeAllHint: "Silencia o microfone das chamadas e o de jogos e apps.", scopeCallsHint: "Silencia só o microfone das chamadas; o de jogos e apps continua ativo. O Windows silencia um microfone para todos os apps ao mesmo tempo, então um app que grava direto do microfone das chamadas (como o OBS) também fica silenciado.",
  },
  uk: {
    title: "Голос", agentDown: "Служба Core не відповідає.",
    mute: "Вимкнути мікрофон", unmute: "Увімкнути мікрофон", live: "Мікрофон увімкнено", muted: "Мікрофон вимкнено", none: "Немає мікрофона", unknown: "Мікрофон: невідомо", partial: "Мікрофон вимкнено частково", partialDetail: "Один мікрофон за замовчуванням вимкнено, інший увімкнено. Перевірте, який використовує дзвінок.",
    mutedElsewhere: "Вимкнено поза Core", hotkey: "Скорочення {keys}: призначте його комбінації контролера в Steam",
    party: "Група Xbox", partyHint: "Відкриває Xbox Game Bar поверх Big Picture. Щоб повернутися, закрийте її кнопкою B чи Esc або натисніть кнопку Steam.",
    notInstalled: "Xbox Game Bar не встановлено.", protectedGame: "Не під час гри з античитом.", alreadyOpen: "Game Bar уже відкрито.", inputRefused: "Windows не прийняла скорочення Game Bar.", uriFailed: "Windows не вдалося відкрити Game Bar.",
    discord: "Discord", discordInstalled: "Встановлено: відкрийте його на вкладці Decky", discordMissing: "Плагін не встановлено",
    callsMic: "Мікрофон для дзвінків", callsMicHint: "Мікрофон Windows за замовчуванням для дзвінків: група Xbox, Discord.", windowsChoice: "Вибір Windows", unnamed: "Мікрофон", tagCalls: "дзвінки", tagApps: "ігри й програми", notConnected: "не підключено", cmActive: "Підключено й використовується для дзвінків.", cmAbsent: "Не підключено. Дзвінки використовують мікрофон Windows за замовчуванням.", cmChanged: "Windows використовує інший мікрофон для дзвінків. Виберіть його знову, щоб використовувати.", cmFailed: "Windows не дозволила Core зробити його мікрофоном за замовчуванням. Виберіть його знову або налаштуйте в параметрах звуку Windows.", cmNotFound: "Цей мікрофон більше не підключено.",
    everything: "Використовувати для всього", everythingHint: "Також за замовчуванням для ігор і програм.", scope: "Вимкнення", scopeAll: "Вимикати все", scopeCalls: "Вимикати лише дзвінки", scopeAllHint: "Вимикає мікрофон для дзвінків і мікрофон для ігор і програм.", scopeCallsHint: "Вимикає лише мікрофон для дзвінків; мікрофон для ігор і програм лишається ввімкненим. Windows вимикає мікрофон одразу для всіх програм, тож програма, що записує прямо з мікрофона для дзвінків (як OBS), теж вимикається.",
  },
  ru: {
    title: "Голос", agentDown: "Служба Core не отвечает.",
    mute: "Выключить микрофон", unmute: "Включить микрофон", live: "Микрофон включён", muted: "Микрофон выключен", none: "Нет микрофона", unknown: "Микрофон: неизвестно", partial: "Микрофон выключен частично", partialDetail: "Один микрофон по умолчанию выключен, другой включён. Проверьте, какой использует звонок.",
    mutedElsewhere: "Выключен вне Core", hotkey: "Сочетание {keys}: назначьте его комбинации контроллера в Steam",
    party: "Группа Xbox", partyHint: "Открывает Xbox Game Bar поверх Big Picture. Чтобы вернуться, закройте её кнопкой B или Esc либо нажмите кнопку Steam.",
    notInstalled: "Xbox Game Bar не установлена.", protectedGame: "Не во время игры с античитом.", alreadyOpen: "Game Bar уже открыта.", inputRefused: "Windows не приняла сочетание Game Bar.", uriFailed: "Windows не удалось открыть Game Bar.",
    discord: "Discord", discordInstalled: "Установлен: откройте его на вкладке Decky", discordMissing: "Плагин не установлен",
    callsMic: "Микрофон для звонков", callsMicHint: "Микрофон Windows по умолчанию для звонков: группа Xbox, Discord.", windowsChoice: "Выбор Windows", unnamed: "Микрофон", tagCalls: "звонки", tagApps: "игры и приложения", notConnected: "не подключён", cmActive: "Подключён и используется для звонков.", cmAbsent: "Не подключён. Звонки используют микрофон Windows по умолчанию.", cmChanged: "Windows использует другой микрофон для звонков. Выберите его снова, чтобы использовать.", cmFailed: "Windows не позволила Core сделать его микрофоном по умолчанию. Выберите его снова или настройте в параметрах звука Windows.", cmNotFound: "Этот микрофон больше не подключён.",
    everything: "Использовать для всего", everythingHint: "Также по умолчанию для игр и приложений.", scope: "Выключение", scopeAll: "Выключать всё", scopeCalls: "Выключать только звонки", scopeAllHint: "Выключает микрофон для звонков и микрофон для игр и приложений.", scopeCallsHint: "Выключает только микрофон для звонков; микрофон для игр и приложений остаётся включённым. Windows выключает микрофон сразу для всех приложений, поэтому приложение, которое пишет прямо с микрофона для звонков (например, OBS), тоже выключается.",
  },
  zh: {
    title: "语音", agentDown: "Core 的服务没有响应。",
    mute: "关闭麦克风", unmute: "打开麦克风", live: "麦克风已开启", muted: "麦克风已静音", none: "没有麦克风", unknown: "麦克风：未知", partial: "麦克风部分静音", partialDetail: "一个默认麦克风已静音，另一个已开启。请检查通话使用的是哪一个。",
    mutedElsewhere: "已在 Core 之外静音", hotkey: "快捷键 {keys}：在 Steam 中将其绑定到手柄组合键",
    party: "Xbox 派对", partyHint: "在 Big Picture 上打开 Xbox Game Bar。按 B 或 Esc 关闭它，或按 Steam 键返回。",
    notInstalled: "未安装 Xbox Game Bar。", protectedGame: "运行带反作弊的游戏时不可用。", alreadyOpen: "Game Bar 已打开。", inputRefused: "Windows 未接受 Game Bar 快捷键。", uriFailed: "Windows 无法打开 Game Bar。",
    discord: "Discord", discordInstalled: "已安装：从 Decky 标签页打开", discordMissing: "未安装插件",
    callsMic: "通话麦克风", callsMicHint: "Windows 用于通话的默认麦克风：Xbox 派对、Discord。", windowsChoice: "由 Windows 决定", unnamed: "麦克风", tagCalls: "通话", tagApps: "游戏和应用", notConnected: "未连接", cmActive: "已连接，用于通话。", cmAbsent: "未连接。通话使用 Windows 的默认麦克风。", cmChanged: "Windows 正在用另一个麦克风通话。再次选择它即可使用。", cmFailed: "Windows 不允许 Core 将其设为默认。请再次选择，或在 Windows 声音设置中设置。", cmNotFound: "该麦克风已不再连接。",
    everything: "用于所有用途", everythingHint: "同时设为游戏和应用的默认麦克风。", scope: "静音", scopeAll: "全部静音", scopeCalls: "仅静音通话", scopeAllHint: "静音通话麦克风以及游戏和应用的麦克风。", scopeCallsHint: "只静音通话麦克风；游戏和应用的麦克风保持开启。Windows 会对所有应用同时静音一个麦克风，所以直接从通话麦克风录音的应用（例如 OBS）也会被静音。",
  },
  ja: {
    title: "ボイス", agentDown: "Core のサービスが応答しません。",
    mute: "マイクをミュート", unmute: "マイクのミュートを解除", live: "マイクはオン", muted: "マイクはミュート中", none: "マイクがありません", unknown: "マイク：不明", partial: "マイクは一部ミュート中", partialDetail: "既定のマイクの一方はミュート、もう一方はオンです。通話がどちらを使うか確認してください。",
    mutedElsewhere: "Core 以外でミュート", hotkey: "ショートカット {keys}：Steam でコントローラーのコードに割り当ててください",
    party: "Xbox パーティー", partyHint: "Big Picture の上に Xbox Game Bar を開きます。戻るには B か Esc で閉じるか、Steam ボタンを押します。",
    notInstalled: "Xbox Game Bar がインストールされていません。", protectedGame: "アンチチート付きのゲーム中は使えません。", alreadyOpen: "Game Bar はすでに開いています。", inputRefused: "Windows が Game Bar のショートカットを受け付けませんでした。", uriFailed: "Windows で Game Bar を開けませんでした。",
    discord: "Discord", discordInstalled: "インストール済み：Decky のタブから開きます", discordMissing: "プラグイン未インストール",
    callsMic: "通話用マイク", callsMicHint: "通話（Xbox パーティー、Discord）に使う Windows の既定のマイクです。", windowsChoice: "Windows の選択", unnamed: "マイク", tagCalls: "通話", tagApps: "ゲームとアプリ", notConnected: "未接続", cmActive: "接続済みで、通話に使われています。", cmAbsent: "未接続です。通話には Windows の既定のマイクが使われます。", cmChanged: "Windows は通話に別のマイクを使っています。使うにはもう一度選んでください。", cmFailed: "Windows が Core による既定の設定を許可しませんでした。もう一度選ぶか、Windows のサウンド設定で設定してください。", cmNotFound: "そのマイクはもう接続されていません。",
    everything: "すべてに使う", everythingHint: "ゲームとアプリの既定にもします。", scope: "ミュート", scopeAll: "すべてミュート", scopeCalls: "通話のみミュート", scopeAllHint: "通話用マイクと、ゲームとアプリ用のマイクをミュートします。", scopeCallsHint: "通話用マイクだけをミュートし、ゲームとアプリ用のマイクはオンのままです。Windows はマイクをすべてのアプリで同時にミュートするため、通話用マイクから直接録音するアプリ（OBS など）もミュートされます。",
  },
  ko: {
    title: "음성", agentDown: "Core 서비스가 응답하지 않습니다.",
    mute: "마이크 음소거", unmute: "마이크 음소거 해제", live: "마이크 켜짐", muted: "마이크 음소거됨", none: "마이크 없음", unknown: "마이크: 알 수 없음", partial: "마이크 일부 음소거됨", partialDetail: "기본 마이크 하나는 음소거되어 있고 다른 하나는 켜져 있습니다. 통화에 쓰이는 마이크를 확인하세요.",
    mutedElsewhere: "Core 밖에서 음소거됨", hotkey: "단축키 {keys}: Steam에서 컨트롤러 조합에 연결하세요",
    party: "Xbox 파티", partyHint: "Big Picture 위에 Xbox Game Bar를 엽니다. 돌아오려면 B나 Esc로 닫거나 Steam 버튼을 누르세요.",
    notInstalled: "Xbox Game Bar가 설치되어 있지 않습니다.", protectedGame: "안티치트 게임 실행 중에는 사용할 수 없습니다.", alreadyOpen: "Game Bar가 이미 열려 있습니다.", inputRefused: "Windows가 Game Bar 단축키를 받아들이지 않았습니다.", uriFailed: "Windows에서 Game Bar를 열지 못했습니다.",
    discord: "Discord", discordInstalled: "설치됨: Decky 탭에서 여세요", discordMissing: "플러그인 미설치",
    callsMic: "통화용 마이크", callsMicHint: "통화(Xbox 파티, Discord)에 쓰는 Windows 기본 마이크입니다.", windowsChoice: "Windows 선택", unnamed: "마이크", tagCalls: "통화", tagApps: "게임 및 앱", notConnected: "연결 안 됨", cmActive: "연결되어 통화에 쓰이고 있습니다.", cmAbsent: "연결되지 않았습니다. 통화에는 Windows 기본 마이크가 쓰입니다.", cmChanged: "Windows가 통화에 다른 마이크를 쓰고 있습니다. 쓰려면 다시 선택하세요.", cmFailed: "Windows가 Core의 기본값 설정을 허용하지 않았습니다. 다시 선택하거나 Windows 소리 설정에서 설정하세요.", cmNotFound: "그 마이크는 더 이상 연결되어 있지 않습니다.",
    everything: "모든 용도에 사용", everythingHint: "게임 및 앱의 기본값으로도 설정합니다.", scope: "음소거", scopeAll: "모두 음소거", scopeCalls: "통화만 음소거", scopeAllHint: "통화용 마이크와 게임 및 앱용 마이크를 음소거합니다.", scopeCallsHint: "통화용 마이크만 음소거하고 게임 및 앱용 마이크는 켜 둡니다. Windows는 마이크를 모든 앱에서 한꺼번에 음소거하므로, 통화용 마이크에서 직접 녹음하는 앱(OBS 등)도 음소거됩니다.",
  },
  hi: {
    title: "आवाज़", agentDown: "Core की सेवा जवाब नहीं दे रही है।",
    mute: "माइक्रोफ़ोन म्यूट करें", unmute: "माइक्रोफ़ोन अनम्यूट करें", live: "माइक्रोफ़ोन चालू", muted: "माइक्रोफ़ोन म्यूट", none: "कोई माइक्रोफ़ोन नहीं", unknown: "माइक्रोफ़ोन: अज्ञात", partial: "माइक्रोफ़ोन आंशिक रूप से म्यूट", partialDetail: "एक डिफ़ॉल्ट माइक्रोफ़ोन म्यूट है, दूसरा चालू है। देखें कि कॉल कौन-सा इस्तेमाल करती है।",
    mutedElsewhere: "Core के बाहर म्यूट किया गया", hotkey: "शॉर्टकट {keys}: इसे Steam में कंट्रोलर कॉम्बिनेशन से जोड़ें",
    party: "Xbox पार्टी", partyHint: "Big Picture के ऊपर Xbox Game Bar खोलता है। लौटने के लिए इसे B या Esc से बंद करें, या Steam बटन दबाएँ।",
    notInstalled: "Xbox Game Bar इंस्टॉल नहीं है।", protectedGame: "एंटी-चीट वाला गेम चलते समय नहीं।", alreadyOpen: "Game Bar पहले से खुला है।", inputRefused: "Windows ने Game Bar का शॉर्टकट स्वीकार नहीं किया।", uriFailed: "Windows Game Bar नहीं खोल सका।",
    discord: "Discord", discordInstalled: "इंस्टॉल है: इसे Decky टैब से खोलें", discordMissing: "प्लगइन इंस्टॉल नहीं है",
    callsMic: "कॉल माइक", callsMicHint: "कॉल (Xbox पार्टी, Discord) के लिए Windows का डिफ़ॉल्ट माइक्रोफ़ोन।", windowsChoice: "Windows की पसंद", unnamed: "माइक्रोफ़ोन", tagCalls: "कॉल", tagApps: "गेम और ऐप", notConnected: "कनेक्ट नहीं", cmActive: "कनेक्ट है और कॉल के लिए इस्तेमाल हो रहा है।", cmAbsent: "कनेक्ट नहीं है। कॉल Windows का डिफ़ॉल्ट माइक्रोफ़ोन इस्तेमाल करती हैं।", cmChanged: "Windows कॉल के लिए दूसरा माइक्रोफ़ोन इस्तेमाल कर रहा है। इसे इस्तेमाल करने के लिए फिर से चुनें।", cmFailed: "Windows ने Core को इसे डिफ़ॉल्ट बनाने नहीं दिया। फिर से चुनें, या Windows की साउंड सेटिंग में सेट करें।", cmNotFound: "वह माइक्रोफ़ोन अब कनेक्ट नहीं है।",
    everything: "हर चीज़ के लिए इस्तेमाल करें", everythingHint: "गेम और ऐप के लिए भी डिफ़ॉल्ट।", scope: "म्यूट", scopeAll: "सब म्यूट करें", scopeCalls: "सिर्फ़ कॉल म्यूट करें", scopeAllHint: "कॉल माइक और गेम व ऐप का माइक म्यूट करता है।", scopeCallsHint: "सिर्फ़ कॉल माइक म्यूट करता है; गेम और ऐप का माइक चालू रहता है। Windows किसी माइक्रोफ़ोन को सभी ऐप के लिए एक साथ म्यूट करता है, इसलिए कॉल माइक से सीधे रिकॉर्ड करने वाला ऐप (जैसे OBS) भी म्यूट हो जाता है।",
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
  const state = mic?.state === "muted" || mic?.state === "live" || mic?.state === "partial" || mic?.state === "none" ? mic.state : "unknown";
  // Partial (one default microphone muted, the other live) is shown like muted, with a warning. The
  // button goes the way the agent's toggle does: the Communications default (the call mic) decides.
  const muted = state === "muted" || state === "partial";
  const unmutes = state === "muted" || (state === "partial" && mic?.communications === "muted");
  const hotkey = typeof mic?.hotkey === "string" && mic.hotkey.trim() && mic.hotkey.length <= 40 ? fill(text.hotkey, { keys: mic.hotkey.trim() }) : "";
  const detail = state === "partial" ? text.partialDetail : muted && mic?.coreMuted === false ? text.mutedElsewhere : hotkey;
  return { state, muted, line: text[state], button: unmutes ? text.unmute : text.mute, detail };
}

function deviceName(name: unknown, text: Copy): string {
  const value = typeof name === "string" ? name.trim() : "";
  return value ? (value.length > 80 ? value.slice(0, 79) + "…" : value) : text.unnamed;
}

// Connected devices, each marked with the defaults it holds; a remembered calls mic that is not
// connected stays listed (marked "not connected") so the picker shows what was chosen.
export function callsMicView(answer: VoiceAnswer | undefined | null, locale: string): CallsMicView {
  const text = voiceCopy(locale);
  const devices = (Array.isArray(answer?.devices) ? answer!.devices : []).filter(device => device && typeof device.id === "string" && device.id);
  const options: PickerOption[] = [{ data: "", label: text.windowsChoice }];
  for (const device of devices) {
    const tags = [device.communications ? text.tagCalls : "", device.console ? text.tagApps : ""].filter(Boolean);
    options.push({ data: device.id!, label: tags.length ? `${deviceName(device.name, text)} · ${tags.join(", ")}` : deviceName(device.name, text) });
  }
  const calls = answer?.callsMic;
  const status = typeof calls?.status === "string" ? calls.status : "none";
  const chosen = status !== "none" && typeof calls?.id === "string" ? calls.id : "";
  if (chosen && !options.some(option => option.data === chosen)) options.push({ data: chosen, label: `${deviceName(calls?.name, text)} (${text.notConnected})` });
  const line = ({ active: text.cmActive, absent: text.cmAbsent, changed: text.cmChanged, failed: text.cmFailed } as Record<string, string>)[status] ?? "";
  return { options, selected: chosen, status, line, everything: calls?.everything === true, canToggleEverything: chosen !== "" && status !== "absent" };
}

export function scopeView(answer: VoiceAnswer | undefined | null, locale: string): ScopeView {
  const text = voiceCopy(locale);
  const selected = answer?.mic?.scope === "calls" ? "calls" : "all";
  return { options: [{ data: "all", label: text.scopeAll }, { data: "calls", label: text.scopeCalls }], selected, hint: selected === "calls" ? text.scopeCallsHint : text.scopeAllHint };
}

// After a pick: a message only when it did not work. A refusal by Windows is already the status
// line ("failed"), so it is said once, there.
export function callsMicMessage(answer: VoiceAnswer | undefined | null, locale: string): string {
  const text = voiceCopy(locale);
  if (!answer || typeof answer !== "object" || !answer.mic) return text.agentDown;
  if (answer.result === "notFound") return text.cmNotFound;
  if ((answer.result === "refused" || answer.result === "unavailable") && answer.callsMic?.status !== "failed") return text.cmFailed;
  return "";
}

// Empty when the bar was opened: Quick Access is already closed by then.
export function partyMessage(answer: PartyAnswer | undefined | null, locale: string): string {
  const text = voiceCopy(locale);
  if (!answer || typeof answer !== "object" || !answer.method) return text.agentDown;
  if (answer.ok) return answer.reason === "alreadyOpen" ? text.alreadyOpen : "";
  return ({ notInstalled: text.notInstalled, protectedGame: text.protectedGame, inputRefused: text.inputRefused, uriFailed: text.uriFailed } as Record<string, string>)[answer.reason ?? ""] ?? text.inputRefused;
}

// Eternal-Assemble keeps upstream's Decky name "Discord" (Source/Shared/ProtectedPluginForks.json).
export const DISCORD_PLUGIN_NAMES = ["Discord"];

export function discordInstalled(inventory: any): boolean {
  const names = [...(Array.isArray(inventory?.plugins) ? inventory.plugins : []), ...(Array.isArray(inventory?.installedPlugins) ? inventory.installedPlugins : [])]
    .map((plugin: any) => String(typeof plugin === "string" ? plugin : plugin?.name ?? "").toLowerCase());
  return DISCORD_PLUGIN_NAMES.some(name => names.includes(name.toLowerCase()));
}
