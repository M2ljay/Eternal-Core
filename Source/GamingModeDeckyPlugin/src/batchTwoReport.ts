// Eternal Core batch 2 (docs/eternal/BATCH-2.md): the Quick Access text for swipe typing and the automations summary,
// in Core's 12 languages, and the pure functions that turn the agent's answers into lines. Self-contained on purpose:
// tests/coreBatchTwo.test.mjs loads this file alone. The Health rows' text lives in healthReport.ts.

type Copy = Record<string, string>;

const en: Copy = {
  typing: "Swipe typing", typingHint: "Swipe across the trackpads on Steam's keyboard, with word suggestions and autocorrect. Off brings back Steam's own keyboard.",
  private: "Private mode", privateHint: "Pauses learning, autocorrect and the typing log. You can also hold Esc with a trackpad.",
  log: "Keep typing log", logHint: "Saves typed words with swipe paths on this PC, to help tuning. Nothing is sent anywhere.",
  settings: "Keyboard settings", settingsToast: "Open the keyboard (STEAM + X) to show its settings.", noKeyboard: "No keyboard opened. You can also hold the emoji key on the keyboard.",
  forget: "Forget everything", forgetConfirm: "Press again to forget everything", forgetting: "Deleting typing data…",
  forgotOk: "Learned words, blocked words and the typing log were deleted.", forgotFail: "Some data could not be deleted. Core tries again.",
  stOn: "Active on Steam's keyboard", stOff: "Off: Steam's own keyboard", stLoading: "Loading…", stModules: "Not available on this Steam client yet",
  stStandalone: "The VaporType plugin is installed and handles the keyboard", stPaused: "Paused: {error}",
  english: "Swiping, suggestions and autocorrect need the English QWERTY layout.", credit: "Based on VaporType (MIT). Word data: wordfreq, CC BY-SA 4.0.",
  autos: "Automations", none: "No automations are on", edit: "Edit them in Core on the desktop: Gaming Mode > Automations.",
  t_controller: "When a controller connects", t_wake: "When Core wakes", t_gameStart: "When a game starts", t_gameStop: "When a game ends", t_streamOn: "When a stream starts", t_streamOff: "When a stream ends",
  a_tv: "wake the TV and switch to Core", a_display: "restore the display", a_sound: "restore the sound output", a_streamStart: "keep the TV out (Stream mode)", a_streamStop: "give the TV back", a_ha: "tell Home Assistant ({event})",
  ha: "Home Assistant", haOff: "Off", haNotConfigured: "Not set up", haConnected: "Connected to {host}", haConnecting: "Connecting to {host}…", haRefused: "Login refused by {host}", haUnreachable: "Can't reach {host}",
  sleep: "Sleep after streaming", sleepOff: "Off", sleepOn: "After {n} min with nobody here", sleepAt: "Sleeping at {time} unless someone plays here",
};

const copy: Record<string, Copy> = {
  en,
  it: {
    typing: "Scrittura a scorrimento", typingHint: "Scorri sui trackpad sulla tastiera di Steam, con suggerimenti e correzione automatica. Spento torna la tastiera normale di Steam.",
    private: "Modalità privata", privateHint: "Mette in pausa apprendimento, correzione automatica e registro di scrittura. Puoi anche tenere premuto Esc con un trackpad.",
    log: "Tieni il registro di scrittura", logHint: "Salva su questo PC le parole scritte con i percorsi dello scorrimento, per migliorare la precisione. Non viene inviato niente.",
    settings: "Impostazioni della tastiera", settingsToast: "Apri la tastiera (STEAM + X) per vedere le sue impostazioni.", noKeyboard: "Nessuna tastiera aperta. Puoi anche tenere premuto il tasto emoji sulla tastiera.",
    forget: "Dimentica tutto", forgetConfirm: "Premi di nuovo per dimenticare tutto", forgetting: "Cancellazione dei dati di scrittura…",
    forgotOk: "Parole imparate, parole bloccate e registro di scrittura cancellati.", forgotFail: "Non è stato possibile cancellare alcuni dati. Core riprova.",
    stOn: "Attiva sulla tastiera di Steam", stOff: "Spenta: tastiera normale di Steam", stLoading: "Caricamento…", stModules: "Non ancora disponibile su questo client di Steam",
    stStandalone: "È installato il plugin VaporType, che gestisce la tastiera", stPaused: "In pausa: {error}",
    english: "Scorrimento, suggerimenti e correzione automatica richiedono il layout inglese QWERTY.", credit: "Basato su VaporType (MIT). Dati delle parole: wordfreq, CC BY-SA 4.0.",
    autos: "Automazioni", none: "Nessuna automazione attiva", edit: "Si modificano in Core sul desktop: Gaming Mode > Automazioni.",
    t_controller: "Quando si collega un controller", t_wake: "Quando Core si risveglia", t_gameStart: "Quando parte un gioco", t_gameStop: "Quando finisce un gioco", t_streamOn: "Quando inizia uno streaming", t_streamOff: "Quando finisce uno streaming",
    a_tv: "accendi la TV e passa a Core", a_display: "ripristina lo schermo", a_sound: "ripristina l'uscita audio", a_streamStart: "lascia stare la TV (modalità streaming)", a_streamStop: "restituisci la TV", a_ha: "avvisa Home Assistant ({event})",
    ha: "Home Assistant", haOff: "Spento", haNotConfigured: "Non configurato", haConnected: "Collegato a {host}", haConnecting: "Collegamento a {host}…", haRefused: "Accesso rifiutato da {host}", haUnreachable: "{host} non raggiungibile",
    sleep: "Sospensione dopo lo streaming", sleepOff: "Spenta", sleepOn: "Dopo {n} min senza nessuno qui", sleepAt: "Sospensione alle {time}, a meno che qualcuno giochi qui",
  },
  es: {
    typing: "Escritura deslizando", typingHint: "Desliza por los trackpads en el teclado de Steam, con sugerencias y autocorrección. Desactivado vuelve el teclado normal de Steam.",
    private: "Modo privado", privateHint: "Pausa el aprendizaje, la autocorrección y el registro de escritura. También puedes mantener Esc con un trackpad.",
    log: "Guardar registro de escritura", logHint: "Guarda en este PC las palabras escritas con sus trazos, para afinar la precisión. No se envía nada.",
    settings: "Ajustes del teclado", settingsToast: "Abre el teclado (STEAM + X) para ver sus ajustes.", noKeyboard: "No se abrió ningún teclado. También puedes mantener la tecla de emoji del teclado.",
    forget: "Olvidarlo todo", forgetConfirm: "Pulsa otra vez para olvidarlo todo", forgetting: "Borrando los datos de escritura…",
    forgotOk: "Se borraron las palabras aprendidas, las bloqueadas y el registro de escritura.", forgotFail: "No se pudieron borrar algunos datos. Core lo reintentará.",
    stOn: "Activa en el teclado de Steam", stOff: "Desactivada: teclado normal de Steam", stLoading: "Cargando…", stModules: "Aún no disponible en este cliente de Steam",
    stStandalone: "El plugin VaporType está instalado y gestiona el teclado", stPaused: "En pausa: {error}",
    english: "Deslizar, las sugerencias y la autocorrección necesitan la distribución inglesa QWERTY.", credit: "Basado en VaporType (MIT). Datos de palabras: wordfreq, CC BY-SA 4.0.",
    autos: "Automatizaciones", none: "No hay automatizaciones activas", edit: "Se editan en Core en el escritorio: Gaming Mode > Automatizaciones.",
    t_controller: "Cuando se conecta un mando", t_wake: "Cuando Core se activa", t_gameStart: "Cuando empieza un juego", t_gameStop: "Cuando termina un juego", t_streamOn: "Cuando empieza una transmisión", t_streamOff: "Cuando termina una transmisión",
    a_tv: "encender la TV y cambiar a Core", a_display: "restaurar la pantalla", a_sound: "restaurar la salida de sonido", a_streamStart: "dejar la TV en paz (modo transmisión)", a_streamStop: "devolver la TV", a_ha: "avisar a Home Assistant ({event})",
    ha: "Home Assistant", haOff: "Desactivado", haNotConfigured: "Sin configurar", haConnected: "Conectado a {host}", haConnecting: "Conectando a {host}…", haRefused: "{host} rechazó el inicio de sesión", haUnreachable: "No se puede contactar con {host}",
    sleep: "Suspensión tras la transmisión", sleepOff: "Desactivada", sleepOn: "Tras {n} min sin nadie aquí", sleepAt: "Suspensión a las {time} salvo que alguien juegue aquí",
  },
  fr: {
    typing: "Saisie par glissement", typingHint: "Glisse sur les trackpads avec le clavier de Steam, avec suggestions et correction automatique. Désactivé, le clavier normal de Steam revient.",
    private: "Mode privé", privateHint: "Met en pause l'apprentissage, la correction automatique et le journal de saisie. Tu peux aussi maintenir Échap avec un trackpad.",
    log: "Garder le journal de saisie", logHint: "Enregistre sur ce PC les mots tapés et leurs tracés, pour affiner la précision. Rien n'est envoyé.",
    settings: "Réglages du clavier", settingsToast: "Ouvre le clavier (STEAM + X) pour afficher ses réglages.", noKeyboard: "Aucun clavier ouvert. Tu peux aussi maintenir la touche emoji du clavier.",
    forget: "Tout oublier", forgetConfirm: "Appuie encore pour tout oublier", forgetting: "Suppression des données de saisie…",
    forgotOk: "Les mots appris, les mots bloqués et le journal de saisie ont été supprimés.", forgotFail: "Certaines données n'ont pas pu être supprimées. Core réessaiera.",
    stOn: "Active sur le clavier de Steam", stOff: "Désactivée : clavier normal de Steam", stLoading: "Chargement…", stModules: "Pas encore disponible sur ce client Steam",
    stStandalone: "Le plugin VaporType est installé et gère le clavier", stPaused: "En pause : {error}",
    english: "Le glissement, les suggestions et la correction automatique demandent la disposition anglaise QWERTY.", credit: "Basé sur VaporType (MIT). Données des mots : wordfreq, CC BY-SA 4.0.",
    autos: "Automatisations", none: "Aucune automatisation active", edit: "Elles se modifient dans Core sur le bureau : Gaming Mode > Automatisations.",
    t_controller: "Quand une manette se connecte", t_wake: "Quand Core sort de veille", t_gameStart: "Quand un jeu démarre", t_gameStop: "Quand un jeu se termine", t_streamOn: "Quand un streaming commence", t_streamOff: "Quand un streaming se termine",
    a_tv: "allumer la TV et passer sur Core", a_display: "rétablir l'affichage", a_sound: "rétablir la sortie audio", a_streamStart: "laisser la TV tranquille (mode streaming)", a_streamStop: "rendre la TV", a_ha: "prévenir Home Assistant ({event})",
    ha: "Home Assistant", haOff: "Désactivé", haNotConfigured: "Non configuré", haConnected: "Connecté à {host}", haConnecting: "Connexion à {host}…", haRefused: "Connexion refusée par {host}", haUnreachable: "{host} injoignable",
    sleep: "Veille après le streaming", sleepOff: "Désactivée", sleepOn: "Après {n} min sans personne ici", sleepAt: "Veille à {time} sauf si quelqu'un joue ici",
  },
  de: {
    typing: "Wischtippen", typingHint: "Wische auf Steams Tastatur über die Trackpads, mit Wortvorschlägen und Autokorrektur. Aus bringt Steams normale Tastatur zurück.",
    private: "Privatmodus", privateHint: "Pausiert Lernen, Autokorrektur und das Tipp-Protokoll. Du kannst auch Esc mit einem Trackpad halten.",
    log: "Tipp-Protokoll behalten", logHint: "Speichert getippte Wörter mit Wischpfaden auf diesem PC, um die Genauigkeit zu verbessern. Nichts wird gesendet.",
    settings: "Tastatureinstellungen", settingsToast: "Öffne die Tastatur (STEAM + X), um ihre Einstellungen zu sehen.", noKeyboard: "Keine Tastatur geöffnet. Du kannst auch die Emoji-Taste auf der Tastatur halten.",
    forget: "Alles vergessen", forgetConfirm: "Noch einmal drücken, um alles zu vergessen", forgetting: "Tippdaten werden gelöscht…",
    forgotOk: "Gelernte Wörter, gesperrte Wörter und das Tipp-Protokoll wurden gelöscht.", forgotFail: "Einige Daten konnten nicht gelöscht werden. Core versucht es erneut.",
    stOn: "Aktiv auf Steams Tastatur", stOff: "Aus: Steams normale Tastatur", stLoading: "Wird geladen…", stModules: "Auf diesem Steam-Client noch nicht verfügbar",
    stStandalone: "Das Plugin VaporType ist installiert und steuert die Tastatur", stPaused: "Pausiert: {error}",
    english: "Wischen, Vorschläge und Autokorrektur brauchen das englische QWERTY-Layout.", credit: "Basiert auf VaporType (MIT). Wortdaten: wordfreq, CC BY-SA 4.0.",
    autos: "Automationen", none: "Keine Automation aktiv", edit: "Bearbeiten in Core auf dem Desktop: Gaming Mode > Automationen.",
    t_controller: "Wenn ein Controller verbunden wird", t_wake: "Wenn Core aufwacht", t_gameStart: "Wenn ein Spiel startet", t_gameStop: "Wenn ein Spiel endet", t_streamOn: "Wenn ein Stream beginnt", t_streamOff: "Wenn ein Stream endet",
    a_tv: "TV einschalten und zu Core wechseln", a_display: "Anzeige wiederherstellen", a_sound: "Tonausgabe wiederherstellen", a_streamStart: "TV in Ruhe lassen (Stream-Modus)", a_streamStop: "TV zurückgeben", a_ha: "Home Assistant melden ({event})",
    ha: "Home Assistant", haOff: "Aus", haNotConfigured: "Nicht eingerichtet", haConnected: "Verbunden mit {host}", haConnecting: "Verbinde mit {host}…", haRefused: "Anmeldung von {host} abgelehnt", haUnreachable: "{host} nicht erreichbar",
    sleep: "Standby nach dem Streamen", sleepOff: "Aus", sleepOn: "Nach {n} Min. ohne jemanden hier", sleepAt: "Standby um {time}, außer jemand spielt hier",
  },
  pt: {
    typing: "Digitação por deslize", typingHint: "Deslize pelos trackpads no teclado do Steam, com sugestões e correção automática. Desligado, volta o teclado normal do Steam.",
    private: "Modo privado", privateHint: "Pausa o aprendizado, a correção automática e o registro de digitação. Você também pode segurar Esc com um trackpad.",
    log: "Manter registro de digitação", logHint: "Salva neste PC as palavras digitadas com os traços, para melhorar a precisão. Nada é enviado.",
    settings: "Configurações do teclado", settingsToast: "Abra o teclado (STEAM + X) para ver as configurações dele.", noKeyboard: "Nenhum teclado aberto. Você também pode segurar a tecla de emoji do teclado.",
    forget: "Esquecer tudo", forgetConfirm: "Aperte de novo para esquecer tudo", forgetting: "Apagando os dados de digitação…",
    forgotOk: "Palavras aprendidas, palavras bloqueadas e o registro de digitação foram apagados.", forgotFail: "Alguns dados não puderam ser apagados. O Core tenta de novo.",
    stOn: "Ativa no teclado do Steam", stOff: "Desligada: teclado normal do Steam", stLoading: "Carregando…", stModules: "Ainda não disponível neste cliente do Steam",
    stStandalone: "O plugin VaporType está instalado e cuida do teclado", stPaused: "Pausada: {error}",
    english: "Deslize, sugestões e correção automática exigem o layout inglês QWERTY.", credit: "Baseado no VaporType (MIT). Dados de palavras: wordfreq, CC BY-SA 4.0.",
    autos: "Automações", none: "Nenhuma automação ativa", edit: "Edite no Core na área de trabalho: Gaming Mode > Automações.",
    t_controller: "Quando um controle se conecta", t_wake: "Quando o Core desperta", t_gameStart: "Quando um jogo começa", t_gameStop: "Quando um jogo termina", t_streamOn: "Quando uma transmissão começa", t_streamOff: "Quando uma transmissão termina",
    a_tv: "ligar a TV e mudar para o Core", a_display: "restaurar a tela", a_sound: "restaurar a saída de som", a_streamStart: "deixar a TV em paz (modo transmissão)", a_streamStop: "devolver a TV", a_ha: "avisar o Home Assistant ({event})",
    ha: "Home Assistant", haOff: "Desligado", haNotConfigured: "Não configurado", haConnected: "Conectado a {host}", haConnecting: "Conectando a {host}…", haRefused: "Login recusado por {host}", haUnreachable: "Não foi possível alcançar {host}",
    sleep: "Suspensão após a transmissão", sleepOff: "Desligada", sleepOn: "Após {n} min sem ninguém aqui", sleepAt: "Suspensão às {time}, a menos que alguém jogue aqui",
  },
  uk: {
    typing: "Введення ковзанням", typingHint: "Ковзайте трекпадами по клавіатурі Steam, з підказками слів і автовиправленням. Вимкнено — повертається звичайна клавіатура Steam.",
    private: "Приватний режим", privateHint: "Призупиняє навчання, автовиправлення і журнал введення. Також можна утримати Esc трекпадом.",
    log: "Вести журнал введення", logHint: "Зберігає на цьому ПК введені слова з траєкторіями, щоб покращити точність. Нічого не надсилається.",
    settings: "Налаштування клавіатури", settingsToast: "Відкрийте клавіатуру (STEAM + X), щоб побачити її налаштування.", noKeyboard: "Клавіатура не відкрилася. Також можна утримати клавішу емодзі на клавіатурі.",
    forget: "Забути все", forgetConfirm: "Натисніть ще раз, щоб забути все", forgetting: "Видалення даних введення…",
    forgotOk: "Вивчені слова, заблоковані слова та журнал введення видалено.", forgotFail: "Деякі дані не вдалося видалити. Core спробує ще раз.",
    stOn: "Увімкнено на клавіатурі Steam", stOff: "Вимкнено: звичайна клавіатура Steam", stLoading: "Завантаження…", stModules: "Поки що недоступно в цьому клієнті Steam",
    stStandalone: "Установлено плагін VaporType, він керує клавіатурою", stPaused: "Призупинено: {error}",
    english: "Ковзання, підказки й автовиправлення потребують англійської розкладки QWERTY.", credit: "На основі VaporType (MIT). Дані слів: wordfreq, CC BY-SA 4.0.",
    autos: "Автоматизації", none: "Немає увімкнених автоматизацій", edit: "Редагуйте в Core на робочому столі: Gaming Mode > Автоматизації.",
    t_controller: "Коли підключається контролер", t_wake: "Коли Core прокидається", t_gameStart: "Коли запускається гра", t_gameStop: "Коли гра завершується", t_streamOn: "Коли починається трансляція", t_streamOff: "Коли трансляція завершується",
    a_tv: "увімкнути телевізор і перейти на Core", a_display: "відновити екран", a_sound: "відновити аудіовихід", a_streamStart: "не чіпати телевізор (режим трансляції)", a_streamStop: "повернути телевізор", a_ha: "повідомити Home Assistant ({event})",
    ha: "Home Assistant", haOff: "Вимкнено", haNotConfigured: "Не налаштовано", haConnected: "Підключено до {host}", haConnecting: "Підключення до {host}…", haRefused: "{host} відхилив вхід", haUnreachable: "{host} недоступний",
    sleep: "Сон після трансляції", sleepOff: "Вимкнено", sleepOn: "Через {n} хв, якщо тут нікого", sleepAt: "Сон о {time}, якщо тут ніхто не грає",
  },
  zh: {
    typing: "滑行输入", typingHint: "在 Steam 键盘上用触控板滑行输入，带单词建议和自动更正。关闭后恢复 Steam 原来的键盘。",
    private: "隐私模式", privateHint: "暂停学习、自动更正和输入记录。也可以用触控板按住 Esc。",
    log: "保留输入记录", logHint: "在这台电脑上保存输入的单词和滑行轨迹，用于调优。不会发送任何内容。",
    settings: "键盘设置", settingsToast: "打开键盘（STEAM + X）即可显示其设置。", noKeyboard: "没有打开键盘。也可以在键盘上按住表情键。",
    forget: "全部忘记", forgetConfirm: "再按一次以全部忘记", forgetting: "正在删除输入数据…",
    forgotOk: "已删除学习的单词、屏蔽的单词和输入记录。", forgotFail: "部分数据无法删除。Core 会重试。",
    stOn: "已在 Steam 键盘上启用", stOff: "已关闭：Steam 原来的键盘", stLoading: "正在加载…", stModules: "此 Steam 客户端暂不支持",
    stStandalone: "已安装 VaporType 插件，由它处理键盘", stPaused: "已暂停：{error}",
    english: "滑行、建议和自动更正需要英文 QWERTY 布局。", credit: "基于 VaporType（MIT）。词汇数据：wordfreq，CC BY-SA 4.0。",
    autos: "自动化", none: "没有启用的自动化", edit: "请在桌面版 Core 中编辑：Gaming Mode > 自动化。",
    t_controller: "控制器连接时", t_wake: "Core 唤醒时", t_gameStart: "游戏开始时", t_gameStop: "游戏结束时", t_streamOn: "串流开始时", t_streamOff: "串流结束时",
    a_tv: "打开电视并切换到 Core", a_display: "恢复显示设置", a_sound: "恢复声音输出", a_streamStart: "不打扰电视（串流模式）", a_streamStop: "交还电视", a_ha: "通知 Home Assistant（{event}）",
    ha: "Home Assistant", haOff: "已关闭", haNotConfigured: "未设置", haConnected: "已连接到 {host}", haConnecting: "正在连接 {host}…", haRefused: "{host} 拒绝登录", haUnreachable: "无法连接 {host}",
    sleep: "串流后睡眠", sleepOff: "已关闭", sleepOn: "无人使用 {n} 分钟后", sleepAt: "将在 {time} 睡眠，除非有人在这里玩",
  },
  ja: {
    typing: "スワイプ入力", typingHint: "Steam のキーボードでトラックパッドをなぞって入力。単語の候補と自動修正付き。オフにすると Steam の通常のキーボードに戻ります。",
    private: "プライベートモード", privateHint: "学習、自動修正、入力ログを一時停止します。トラックパッドで Esc を長押ししても切り替えられます。",
    log: "入力ログを保存", logHint: "入力した単語とスワイプの軌跡をこの PC に保存し、精度の調整に使います。何も送信されません。",
    settings: "キーボードの設定", settingsToast: "キーボード（STEAM + X）を開くと設定が表示されます。", noKeyboard: "キーボードが開きませんでした。キーボードの絵文字キーを長押ししても開けます。",
    forget: "すべて忘れる", forgetConfirm: "もう一度押すとすべて忘れます", forgetting: "入力データを削除しています…",
    forgotOk: "学習した単語、ブロックした単語、入力ログを削除しました。", forgotFail: "一部のデータを削除できませんでした。Core が再試行します。",
    stOn: "Steam のキーボードで有効", stOff: "オフ：Steam の通常のキーボード", stLoading: "読み込み中…", stModules: "この Steam クライアントではまだ使えません",
    stStandalone: "VaporType プラグインがインストールされていて、キーボードを担当しています", stPaused: "一時停止：{error}",
    english: "スワイプ、候補、自動修正には英語 QWERTY 配列が必要です。", credit: "VaporType（MIT）をもとにしています。単語データ：wordfreq、CC BY-SA 4.0。",
    autos: "オートメーション", none: "有効なオートメーションはありません", edit: "デスクトップの Core で編集します：Gaming Mode > オートメーション。",
    t_controller: "コントローラーが接続されたとき", t_wake: "Core がスリープから復帰したとき", t_gameStart: "ゲームが始まったとき", t_gameStop: "ゲームが終わったとき", t_streamOn: "ストリーミングが始まったとき", t_streamOff: "ストリーミングが終わったとき",
    a_tv: "テレビをつけて Core に切り替える", a_display: "ディスプレイを元に戻す", a_sound: "サウンド出力を元に戻す", a_streamStart: "テレビに触れない（ストリームモード）", a_streamStop: "テレビを戻す", a_ha: "Home Assistant に通知（{event}）",
    ha: "Home Assistant", haOff: "オフ", haNotConfigured: "未設定", haConnected: "{host} に接続中", haConnecting: "{host} に接続しています…", haRefused: "{host} にログインを拒否されました", haUnreachable: "{host} に接続できません",
    sleep: "ストリーミング後にスリープ", sleepOff: "オフ", sleepOn: "誰もいない状態が {n} 分続いたら", sleepAt: "{time} にスリープ（ここで誰かが遊ばない限り）",
  },
  ko: {
    typing: "스와이프 입력", typingHint: "Steam 키보드에서 트랙패드로 밀어서 입력하고, 단어 추천과 자동 수정을 씁니다. 끄면 Steam 기본 키보드로 돌아갑니다.",
    private: "개인 정보 모드", privateHint: "학습, 자동 수정, 입력 기록을 일시 중지합니다. 트랙패드로 Esc를 길게 눌러도 됩니다.",
    log: "입력 기록 보관", logHint: "입력한 단어와 스와이프 경로를 이 PC에 저장해 정확도를 높이는 데 씁니다. 아무것도 전송하지 않습니다.",
    settings: "키보드 설정", settingsToast: "키보드(STEAM + X)를 열면 설정이 보입니다.", noKeyboard: "키보드가 열리지 않았습니다. 키보드의 이모지 키를 길게 눌러도 됩니다.",
    forget: "모두 잊기", forgetConfirm: "한 번 더 누르면 모두 잊습니다", forgetting: "입력 데이터를 삭제하는 중…",
    forgotOk: "학습한 단어, 차단한 단어, 입력 기록을 삭제했습니다.", forgotFail: "일부 데이터를 삭제하지 못했습니다. Core가 다시 시도합니다.",
    stOn: "Steam 키보드에서 사용 중", stOff: "꺼짐: Steam 기본 키보드", stLoading: "불러오는 중…", stModules: "이 Steam 클라이언트에서는 아직 사용할 수 없음",
    stStandalone: "VaporType 플러그인이 설치되어 키보드를 맡고 있음", stPaused: "일시 중지: {error}",
    english: "스와이프, 추천, 자동 수정은 영어 QWERTY 배열이 필요합니다.", credit: "VaporType(MIT) 기반. 단어 데이터: wordfreq, CC BY-SA 4.0.",
    autos: "자동화", none: "켜진 자동화가 없습니다", edit: "데스크톱의 Core에서 편집하세요: Gaming Mode > 자동화.",
    t_controller: "컨트롤러가 연결되면", t_wake: "Core가 깨어나면", t_gameStart: "게임이 시작되면", t_gameStop: "게임이 끝나면", t_streamOn: "스트리밍이 시작되면", t_streamOff: "스트리밍이 끝나면",
    a_tv: "TV를 켜고 Core로 전환", a_display: "디스플레이 복원", a_sound: "사운드 출력 복원", a_streamStart: "TV 건드리지 않기(스트리밍 모드)", a_streamStop: "TV 돌려주기", a_ha: "Home Assistant에 알림({event})",
    ha: "Home Assistant", haOff: "꺼짐", haNotConfigured: "설정 안 됨", haConnected: "{host}에 연결됨", haConnecting: "{host}에 연결하는 중…", haRefused: "{host}에서 로그인 거부", haUnreachable: "{host}에 연결할 수 없음",
    sleep: "스트리밍 후 절전", sleepOff: "꺼짐", sleepOn: "아무도 없이 {n}분 지나면", sleepAt: "여기서 누가 플레이하지 않으면 {time}에 절전",
  },
  hi: {
    typing: "स्वाइप टाइपिंग", typingHint: "Steam कीबोर्ड पर ट्रैकपैड से स्वाइप करके लिखें, शब्द सुझाव और ऑटोकरेक्ट के साथ। बंद करने पर Steam का सामान्य कीबोर्ड लौट आता है।",
    private: "निजी मोड", privateHint: "सीखना, ऑटोकरेक्ट और टाइपिंग लॉग रोक देता है। ट्रैकपैड से Esc दबाए रखकर भी कर सकते हैं।",
    log: "टाइपिंग लॉग रखें", logHint: "टाइप किए शब्द और स्वाइप रास्ते इस PC पर सहेजता है, सटीकता सुधारने के लिए। कुछ भी कहीं नहीं भेजा जाता।",
    settings: "कीबोर्ड सेटिंग्स", settingsToast: "सेटिंग्स देखने के लिए कीबोर्ड (STEAM + X) खोलें।", noKeyboard: "कोई कीबोर्ड नहीं खुला। आप कीबोर्ड पर इमोजी कुंजी दबाए भी रख सकते हैं।",
    forget: "सब भूल जाएँ", forgetConfirm: "सब भूलने के लिए फिर से दबाएँ", forgetting: "टाइपिंग डेटा मिटाया जा रहा है…",
    forgotOk: "सीखे हुए शब्द, ब्लॉक किए शब्द और टाइपिंग लॉग मिटा दिए गए।", forgotFail: "कुछ डेटा मिटाया नहीं जा सका। Core फिर कोशिश करेगा।",
    stOn: "Steam कीबोर्ड पर चालू", stOff: "बंद: Steam का सामान्य कीबोर्ड", stLoading: "लोड हो रहा है…", stModules: "इस Steam क्लाइंट पर अभी उपलब्ध नहीं",
    stStandalone: "VaporType प्लगइन इंस्टॉल है और कीबोर्ड वही संभालता है", stPaused: "रुका हुआ: {error}",
    english: "स्वाइप, सुझाव और ऑटोकरेक्ट के लिए अंग्रेज़ी QWERTY लेआउट चाहिए।", credit: "VaporType (MIT) पर आधारित। शब्द डेटा: wordfreq, CC BY-SA 4.0।",
    autos: "ऑटोमेशन", none: "कोई ऑटोमेशन चालू नहीं", edit: "डेस्कटॉप पर Core में बदलें: Gaming Mode > ऑटोमेशन।",
    t_controller: "जब कोई कंट्रोलर जुड़े", t_wake: "जब Core जागे", t_gameStart: "जब कोई गेम शुरू हो", t_gameStop: "जब कोई गेम खत्म हो", t_streamOn: "जब स्ट्रीम शुरू हो", t_streamOff: "जब स्ट्रीम खत्म हो",
    a_tv: "TV चालू करें और Core पर जाएँ", a_display: "डिस्प्ले वापस करें", a_sound: "साउंड आउटपुट वापस करें", a_streamStart: "TV को न छेड़ें (स्ट्रीम मोड)", a_streamStop: "TV वापस दें", a_ha: "Home Assistant को बताएँ ({event})",
    ha: "Home Assistant", haOff: "बंद", haNotConfigured: "सेट नहीं है", haConnected: "{host} से जुड़ा", haConnecting: "{host} से जुड़ रहा है…", haRefused: "{host} ने लॉगिन ठुकराया", haUnreachable: "{host} तक नहीं पहुँच पा रहे",
    sleep: "स्ट्रीमिंग के बाद स्लीप", sleepOff: "बंद", sleepOn: "यहाँ किसी के न होने पर {n} मिनट बाद", sleepAt: "{time} पर स्लीप, जब तक कोई यहाँ न खेले",
  },
  ru: {
    typing: "Ввод скольжением", typingHint: "Скользите трекпадами по клавиатуре Steam, с подсказками слов и автоисправлением. Выключено — возвращается обычная клавиатура Steam.",
    private: "Приватный режим", privateHint: "Приостанавливает обучение, автоисправление и журнал ввода. Также можно удерживать Esc трекпадом.",
    log: "Вести журнал ввода", logHint: "Сохраняет на этом ПК введённые слова с траекториями, чтобы улучшить точность. Ничего не отправляется.",
    settings: "Настройки клавиатуры", settingsToast: "Откройте клавиатуру (STEAM + X), чтобы увидеть её настройки.", noKeyboard: "Клавиатура не открылась. Можно также удерживать клавишу эмодзи на клавиатуре.",
    forget: "Забыть всё", forgetConfirm: "Нажмите ещё раз, чтобы забыть всё", forgetting: "Удаление данных ввода…",
    forgotOk: "Выученные слова, заблокированные слова и журнал ввода удалены.", forgotFail: "Некоторые данные не удалось удалить. Core попробует ещё раз.",
    stOn: "Включено на клавиатуре Steam", stOff: "Выключено: обычная клавиатура Steam", stLoading: "Загрузка…", stModules: "Пока недоступно в этом клиенте Steam",
    stStandalone: "Установлен плагин VaporType, он управляет клавиатурой", stPaused: "Приостановлено: {error}",
    english: "Скольжение, подсказки и автоисправление требуют английской раскладки QWERTY.", credit: "На основе VaporType (MIT). Данные слов: wordfreq, CC BY-SA 4.0.",
    autos: "Автоматизации", none: "Нет включённых автоматизаций", edit: "Редактируйте в Core на рабочем столе: Gaming Mode > Автоматизации.",
    t_controller: "Когда подключается контроллер", t_wake: "Когда Core просыпается", t_gameStart: "Когда запускается игра", t_gameStop: "Когда игра заканчивается", t_streamOn: "Когда начинается трансляция", t_streamOff: "Когда трансляция заканчивается",
    a_tv: "включить телевизор и переключить на Core", a_display: "восстановить экран", a_sound: "восстановить аудиовыход", a_streamStart: "не трогать телевизор (режим трансляции)", a_streamStop: "вернуть телевизор", a_ha: "сообщить Home Assistant ({event})",
    ha: "Home Assistant", haOff: "Выключено", haNotConfigured: "Не настроено", haConnected: "Подключено к {host}", haConnecting: "Подключение к {host}…", haRefused: "{host} отклонил вход", haUnreachable: "{host} недоступен",
    sleep: "Сон после трансляции", sleepOff: "Выключено", sleepOn: "Через {n} мин, если здесь никого", sleepAt: "Сон в {time}, если здесь никто не играет",
  },
};

export const batchTwoLanguages = Object.keys(copy);

export function batchTwoCopy(locale: string): Copy {
  const language = String(locale || "en").toLowerCase().split(/[-_]/)[0];
  const table = copy[language === "ua" ? "uk" : language] ?? en;
  return new Proxy(table, { get: (target, key: string) => target[key] ?? en[key] ?? key });
}

export function fill(template: string, values: Record<string, string | number>): string {
  return template.replace(/\{(\w+)\}/g, (whole, name) => (name in values ? String(values[name]) : whole));
}

function clean(value: unknown, limit: number): string {
  return typeof value === "string" ? value.replace(/[\u0000-\u001f\u007f]/g, " ").trim().slice(0, limit) : "";
}

// ---------- swipe typing status ----------

export type TypingState = "on" | "off" | "loading" | "modules" | "standalone" | "paused";

export function typingStatus(state: TypingState, locale: string, error?: string): string {
  const text = batchTwoCopy(locale);
  switch (state) {
    case "on": return text.stOn;
    case "off": return text.stOff;
    case "modules": return text.stModules;
    case "standalone": return text.stStandalone;
    case "paused": return fill(text.stPaused, { error: clean(error, 80) || "?" });
    default: return text.stLoading;
  }
}

// ---------- automations summary ----------

export type AutomationRule = { id?: unknown; enabled?: unknown; trigger?: unknown; actions?: unknown; event?: unknown };
export type AutomationsAnswer = {
  rules?: AutomationRule[]; tvOnWake?: unknown;
  bridge?: { enabled?: unknown; connected?: unknown; state?: unknown; host?: unknown };
  streamSleep?: { enabled?: unknown; minutes?: unknown; at?: unknown };
  typing?: { enabled?: unknown };
};

const TRIGGERS: Record<string, string> = {
  "controller-connected": "t_controller", wake: "t_wake", "game-start": "t_gameStart", "game-stop": "t_gameStop", "stream-on": "t_streamOn", "stream-off": "t_streamOff",
};
const ACTIONS: Record<string, string> = {
  "tv-wake": "a_tv", "display-restore": "a_display", "sound-restore": "a_sound", "stream-start": "a_streamStart", "stream-stop": "a_streamStop", "ha-event": "a_ha",
};

// One line per enabled rule ("When a controller connects: wake the TV and switch to Core"). TV-on-wake is shown as a
// rule too: it is the same TV wake path (CoreAutomations.WakeTvOnResume).
export function automationLines(answer: AutomationsAnswer | null | undefined, locale: string): string[] {
  const text = batchTwoCopy(locale);
  const lines: string[] = [];
  let wakeTvShown = false;
  for (const rule of Array.isArray(answer?.rules) ? answer!.rules : []) {
    if (rule?.enabled !== true || typeof rule.trigger !== "string" || !(rule.trigger in TRIGGERS)) continue;
    const actions = (Array.isArray(rule.actions) ? rule.actions : []).filter((action): action is string => typeof action === "string" && action in ACTIONS);
    if (!actions.length) continue;
    if (rule.trigger === "wake" && actions.includes("tv-wake")) wakeTvShown = true;
    const event = clean(rule.event, 32) || rule.trigger;
    const what = actions.map(action => fill(text[ACTIONS[action]], { event }));
    lines.push(`${text[TRIGGERS[rule.trigger]]}: ${what.join(", ")}`);
    if (lines.length >= 8) break;
  }
  if (answer?.tvOnWake === true && !wakeTvShown) lines.unshift(`${text.t_wake}: ${text.a_tv}`);
  return lines;
}

export function bridgeLine(answer: AutomationsAnswer | null | undefined, locale: string): string {
  const text = batchTwoCopy(locale);
  const bridge = answer?.bridge;
  const host = clean(bridge?.host, 64) || "serverv3";
  if (bridge?.enabled !== true) return text.haOff;
  switch (bridge.state) {
    case "connected": return fill(text.haConnected, { host });
    case "refused": return fill(text.haRefused, { host });
    case "unreachable": return fill(text.haUnreachable, { host });
    case "not-configured": return text.haNotConfigured;
    default: return fill(text.haConnecting, { host });
  }
}

export function sleepLine(answer: AutomationsAnswer | null | undefined, locale: string): string {
  const text = batchTwoCopy(locale);
  const sleep = answer?.streamSleep;
  if (sleep?.enabled !== true) return text.sleepOff;
  const at = typeof sleep.at === "string" ? new Date(sleep.at) : null;
  if (at && !Number.isNaN(at.getTime())) {
    let time: string;
    try { time = at.toLocaleTimeString(locale, { hour: "2-digit", minute: "2-digit" }); } catch { time = at.toISOString().slice(11, 16); }
    return fill(text.sleepAt, { time });
  }
  const minutes = Number(sleep.minutes);
  return fill(text.sleepOn, { n: Number.isInteger(minutes) && minutes >= 1 && minutes <= 120 ? minutes : 10 });
}
