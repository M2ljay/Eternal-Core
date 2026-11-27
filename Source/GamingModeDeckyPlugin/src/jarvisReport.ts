// Eternal Core: Jarvis in Quick Access (docs/eternal/JARVIS.md). The text in Core's 12 languages and the pure functions
// that turn the agent's answers (GET /jarvis/state and the action answers, MarkIIIHost.Jarvis.cs) into lines.
// Self-contained on purpose: tests/coreJarvis.test.mjs loads this file alone. Jarvis's own answers are in English
// (the persona is a British butler, and Aincrad's Whisper and Piper are English); everything around them is localised.

type Copy = Record<string, string>;

const en: Copy = {
  title: "Jarvis", placeholder: "Ask Jarvis…", send: "Send", dictate: "Dictate", dictateStop: "Stop and type", talk: "Talk to Jarvis", talkStop: "Stop and send",
  screen: "What's on screen?", more: "More", speak: "Speak", clear: "Clear", page: "Open full page", you: "You", listening: "Listening… press again to stop", thinking: "Thinking…",
  stConnectedHa: "Connected through Home Assistant", stConnectedClaude: "Connected to the Claude API", stReady: "Ready", stOff: "Off",
  stNotSetUp: "Not set up: enter the token in Core on the desktop (Gaming Mode > Jarvis)", stHaDown: "Home Assistant unreachable", stClaudeDown: "Claude API unreachable",
  stToken: "Home Assistant refused the token", stKey: "Anthropic refused the key", stAgent: "Home Assistant has no Jarvis agent with that name",
  eBusy: "Jarvis is still answering", eMuted: "Microphone muted: unmute it in Voice first", eNoMic: "No microphone", eSilent: "Nothing heard", eShort: "Too short: keep holding while you speak",
  eNotUnderstood: "Jarvis didn't catch that", eNeedsKey: "The screen helper needs your Anthropic key (Core on the desktop > Gaming Mode > Jarvis)", eNoPicture: "Windows gave no picture of the screen",
  eFailed: "Something went wrong; nothing was changed", eUnreachable: "Can't reach Jarvis", eAntiCheat: "Typing is paused while anti-cheat protection is on; the text is in Quick Access",
  eNoRoute: "No speech recognition available: save the Home Assistant token", eRate: "Too many requests; try again in a moment", eOff: "Jarvis is off",
  screenshot: "Screenshot taken", party: "Your Xbox party hears you too", typed: "Typed: {text}", heard: "Heard: {text}",
  combos: "Tap {talk} to talk (it stops on a pause) · hold it to open Jarvis · dictation and the screen helper: the buttons above, or ask by voice", comboOff: "off",
  hint: "Nothing is sent until you ask. Locks, the garage and the alarm need your phone.", helperOff: "Screen helper: add your Anthropic key on the desktop to use it",
};

const copy: Record<string, Copy> = {
  en,
  it: {
    title: "Jarvis", placeholder: "Chiedi a Jarvis…", send: "Invia", dictate: "Detta", dictateStop: "Ferma e scrivi", talk: "Parla con Jarvis", talkStop: "Ferma e invia",
    screen: "Cosa c'è sullo schermo?", more: "Altro", speak: "Leggi", clear: "Cancella", page: "Apri a pagina intera", you: "Tu", listening: "In ascolto… premi di nuovo per fermare", thinking: "Ci penso…",
    stConnectedHa: "Collegato tramite Home Assistant", stConnectedClaude: "Collegato all'API di Claude", stReady: "Pronto", stOff: "Spento",
    stNotSetUp: "Non configurato: inserisci il token in Core sul desktop (Gaming Mode > Jarvis)", stHaDown: "Home Assistant non raggiungibile", stClaudeDown: "API di Claude non raggiungibile",
    stToken: "Home Assistant ha rifiutato il token", stKey: "Anthropic ha rifiutato la chiave", stAgent: "Home Assistant non ha un agente Jarvis con quel nome",
    eBusy: "Jarvis sta ancora rispondendo", eMuted: "Microfono silenziato: riattivalo prima in Voce", eNoMic: "Nessun microfono", eSilent: "Non ho sentito niente", eShort: "Troppo breve: tieni premuto mentre parli",
    eNotUnderstood: "Jarvis non ha capito", eNeedsKey: "L'aiuto sullo schermo richiede la tua chiave di Anthropic (Core sul desktop > Gaming Mode > Jarvis)", eNoPicture: "Windows non ha dato un'immagine dello schermo",
    eFailed: "Qualcosa è andato storto; non è cambiato niente", eUnreachable: "Non riesco a raggiungere Jarvis", eAntiCheat: "La scrittura è in pausa mentre la protezione anti-cheat è attiva; il testo è in Accesso rapido",
    eNoRoute: "Nessun riconoscimento vocale disponibile: salva il token di Home Assistant", eRate: "Troppe richieste; riprova tra poco", eOff: "Jarvis è spento",
    screenshot: "Screenshot fatto", party: "Anche il tuo party Xbox ti sente", typed: "Scritto: {text}", heard: "Sentito: {text}",
    combos: "Tocca {talk} per parlare (si ferma a una pausa) · tienilo premuto per aprire Jarvis · dettatura e aiuto sullo schermo: i pulsanti qui sopra, o chiedi a voce", comboOff: "spenta",
    hint: "Non viene inviato niente finché non lo chiedi. Serrature, garage e allarme richiedono il telefono.", helperOff: "Aiuto sullo schermo: aggiungi la chiave di Anthropic sul desktop per usarlo",
  },
  es: {
    title: "Jarvis", placeholder: "Pregunta a Jarvis…", send: "Enviar", dictate: "Dictar", dictateStop: "Parar y escribir", talk: "Hablar con Jarvis", talkStop: "Parar y enviar",
    screen: "¿Qué hay en pantalla?", more: "Más", speak: "Leer", clear: "Borrar", page: "Abrir a pantalla completa", you: "Tú", listening: "Escuchando… pulsa otra vez para parar", thinking: "Pensando…",
    stConnectedHa: "Conectado mediante Home Assistant", stConnectedClaude: "Conectado a la API de Claude", stReady: "Listo", stOff: "Desactivado",
    stNotSetUp: "Sin configurar: introduce el token en Core en el escritorio (Gaming Mode > Jarvis)", stHaDown: "No se puede contactar con Home Assistant", stClaudeDown: "No se puede contactar con la API de Claude",
    stToken: "Home Assistant rechazó el token", stKey: "Anthropic rechazó la clave", stAgent: "Home Assistant no tiene un agente Jarvis con ese nombre",
    eBusy: "Jarvis aún está respondiendo", eMuted: "Micrófono silenciado: actívalo primero en Voz", eNoMic: "No hay micrófono", eSilent: "No se oyó nada", eShort: "Demasiado corto: mantén pulsado mientras hablas",
    eNotUnderstood: "Jarvis no lo entendió", eNeedsKey: "La ayuda en pantalla necesita tu clave de Anthropic (Core en el escritorio > Gaming Mode > Jarvis)", eNoPicture: "Windows no dio ninguna imagen de la pantalla",
    eFailed: "Algo salió mal; no se cambió nada", eUnreachable: "No se puede contactar con Jarvis", eAntiCheat: "La escritura está en pausa con la protección antitrampas activa; el texto está en Acceso rápido",
    eNoRoute: "No hay reconocimiento de voz disponible: guarda el token de Home Assistant", eRate: "Demasiadas solicitudes; inténtalo de nuevo en un momento", eOff: "Jarvis está desactivado",
    screenshot: "Captura hecha", party: "Tu grupo de Xbox también te oye", typed: "Escrito: {text}", heard: "Oído: {text}",
    combos: "Toca {talk} para hablar (se detiene en una pausa) · mantenlo para abrir Jarvis · dictado y ayuda en pantalla: los botones de arriba, o pídelo por voz", comboOff: "desactivada",
    hint: "No se envía nada hasta que lo pidas. Cerraduras, garaje y alarma necesitan tu teléfono.", helperOff: "Ayuda en pantalla: añade tu clave de Anthropic en el escritorio para usarla",
  },
  fr: {
    title: "Jarvis", placeholder: "Demande à Jarvis…", send: "Envoyer", dictate: "Dicter", dictateStop: "Arrêter et écrire", talk: "Parler à Jarvis", talkStop: "Arrêter et envoyer",
    screen: "Qu'y a-t-il à l'écran ?", more: "Plus", speak: "Lire", clear: "Effacer", page: "Ouvrir en plein écran", you: "Toi", listening: "J'écoute… appuie à nouveau pour arrêter", thinking: "Je réfléchis…",
    stConnectedHa: "Connecté via Home Assistant", stConnectedClaude: "Connecté à l'API Claude", stReady: "Prêt", stOff: "Désactivé",
    stNotSetUp: "Non configuré : saisis le jeton dans Core sur le bureau (Gaming Mode > Jarvis)", stHaDown: "Home Assistant injoignable", stClaudeDown: "API Claude injoignable",
    stToken: "Home Assistant a refusé le jeton", stKey: "Anthropic a refusé la clé", stAgent: "Home Assistant n'a pas d'agent Jarvis de ce nom",
    eBusy: "Jarvis répond encore", eMuted: "Micro coupé : réactive-le d'abord dans Voix", eNoMic: "Aucun micro", eSilent: "Rien entendu", eShort: "Trop court : maintiens pendant que tu parles",
    eNotUnderstood: "Jarvis n'a pas compris", eNeedsKey: "L'aide à l'écran a besoin de ta clé Anthropic (Core sur le bureau > Gaming Mode > Jarvis)", eNoPicture: "Windows n'a donné aucune image de l'écran",
    eFailed: "Un problème est survenu ; rien n'a été modifié", eUnreachable: "Jarvis est injoignable", eAntiCheat: "La saisie est en pause tant que la protection anti-triche est active ; le texte est dans l'accès rapide",
    eNoRoute: "Aucune reconnaissance vocale disponible : enregistre le jeton Home Assistant", eRate: "Trop de requêtes ; réessaie dans un instant", eOff: "Jarvis est désactivé",
    screenshot: "Capture prise", party: "Ta partie Xbox t'entend aussi", typed: "Écrit : {text}", heard: "Entendu : {text}",
    combos: "Touchez {talk} pour parler (s'arrête à une pause) · maintenez pour ouvrir Jarvis · dictée et aide à l'écran : les boutons ci-dessus, ou demandez à voix haute", comboOff: "désactivée",
    hint: "Rien n'est envoyé tant que tu ne le demandes pas. Serrures, garage et alarme passent par ton téléphone.", helperOff: "Aide à l'écran : ajoute ta clé Anthropic sur le bureau pour l'utiliser",
  },
  de: {
    title: "Jarvis", placeholder: "Frag Jarvis…", send: "Senden", dictate: "Diktieren", dictateStop: "Stopp und schreiben", talk: "Mit Jarvis sprechen", talkStop: "Stopp und senden",
    screen: "Was ist auf dem Bildschirm?", more: "Mehr", speak: "Vorlesen", clear: "Löschen", page: "Ganze Seite öffnen", you: "Du", listening: "Ich höre zu… noch einmal drücken zum Beenden", thinking: "Ich denke nach…",
    stConnectedHa: "Über Home Assistant verbunden", stConnectedClaude: "Mit der Claude-API verbunden", stReady: "Bereit", stOff: "Aus",
    stNotSetUp: "Nicht eingerichtet: Token in Core auf dem Desktop eingeben (Gaming Mode > Jarvis)", stHaDown: "Home Assistant nicht erreichbar", stClaudeDown: "Claude-API nicht erreichbar",
    stToken: "Home Assistant hat das Token abgelehnt", stKey: "Anthropic hat den Schlüssel abgelehnt", stAgent: "Home Assistant hat keinen Jarvis-Agenten mit diesem Namen",
    eBusy: "Jarvis antwortet noch", eMuted: "Mikrofon stumm: zuerst unter Stimme einschalten", eNoMic: "Kein Mikrofon", eSilent: "Nichts gehört", eShort: "Zu kurz: beim Sprechen gedrückt halten",
    eNotUnderstood: "Jarvis hat das nicht verstanden", eNeedsKey: "Die Bildschirmhilfe braucht deinen Anthropic-Schlüssel (Core auf dem Desktop > Gaming Mode > Jarvis)", eNoPicture: "Windows hat kein Bild vom Bildschirm geliefert",
    eFailed: "Etwas ist schiefgegangen; nichts wurde geändert", eUnreachable: "Jarvis ist nicht erreichbar", eAntiCheat: "Tippen ist pausiert, solange der Anti-Cheat-Schutz aktiv ist; der Text steht im Schnellzugriff",
    eNoRoute: "Keine Spracherkennung verfügbar: Home-Assistant-Token speichern", eRate: "Zu viele Anfragen; gleich noch einmal versuchen", eOff: "Jarvis ist aus",
    screenshot: "Screenshot gemacht", party: "Deine Xbox-Party hört dich auch", typed: "Geschrieben: {text}", heard: "Gehört: {text}",
    combos: "{talk} tippen zum Sprechen (endet bei einer Pause) · halten öffnet Jarvis · Diktat und Bildschirmhilfe: die Tasten oben, oder per Sprache", comboOff: "aus",
    hint: "Es wird nichts gesendet, bis du es willst. Schlösser, Garage und Alarmanlage brauchen dein Handy.", helperOff: "Bildschirmhilfe: Anthropic-Schlüssel auf dem Desktop hinzufügen, um sie zu nutzen",
  },
  pt: {
    title: "Jarvis", placeholder: "Pergunte ao Jarvis…", send: "Enviar", dictate: "Ditar", dictateStop: "Parar e digitar", talk: "Falar com o Jarvis", talkStop: "Parar e enviar",
    screen: "O que há na tela?", more: "Mais", speak: "Ler", clear: "Limpar", page: "Abrir em tela cheia", you: "Você", listening: "Ouvindo… aperte de novo para parar", thinking: "Pensando…",
    stConnectedHa: "Conectado pelo Home Assistant", stConnectedClaude: "Conectado à API do Claude", stReady: "Pronto", stOff: "Desligado",
    stNotSetUp: "Não configurado: digite o token no Core no desktop (Gaming Mode > Jarvis)", stHaDown: "Não foi possível alcançar o Home Assistant", stClaudeDown: "Não foi possível alcançar a API do Claude",
    stToken: "O Home Assistant recusou o token", stKey: "A Anthropic recusou a chave", stAgent: "O Home Assistant não tem um agente Jarvis com esse nome",
    eBusy: "O Jarvis ainda está respondendo", eMuted: "Microfone silenciado: ative-o primeiro em Voz", eNoMic: "Nenhum microfone", eSilent: "Nada foi ouvido", eShort: "Curto demais: segure enquanto fala",
    eNotUnderstood: "O Jarvis não entendeu", eNeedsKey: "A ajuda na tela precisa da sua chave da Anthropic (Core no desktop > Gaming Mode > Jarvis)", eNoPicture: "O Windows não deu nenhuma imagem da tela",
    eFailed: "Algo deu errado; nada foi alterado", eUnreachable: "Não foi possível alcançar o Jarvis", eAntiCheat: "A digitação está pausada com a proteção anti-cheat ativa; o texto está no Acesso rápido",
    eNoRoute: "Nenhum reconhecimento de voz disponível: salve o token do Home Assistant", eRate: "Pedidos demais; tente de novo em instantes", eOff: "O Jarvis está desligado",
    screenshot: "Captura feita", party: "Sua party do Xbox também ouve você", typed: "Digitado: {text}", heard: "Ouvido: {text}",
    combos: "Toque em {talk} para falar (para numa pausa) · segure para abrir o Jarvis · ditado e ajuda na tela: os botões acima, ou peça por voz", comboOff: "desligada",
    hint: "Nada é enviado até você pedir. Fechaduras, garagem e alarme precisam do seu celular.", helperOff: "Ajuda na tela: adicione sua chave da Anthropic no desktop para usá-la",
  },
  uk: {
    title: "Jarvis", placeholder: "Запитайте Jarvis…", send: "Надіслати", dictate: "Диктувати", dictateStop: "Зупинити й надрукувати", talk: "Говорити з Jarvis", talkStop: "Зупинити й надіслати",
    screen: "Що на екрані?", more: "Більше", speak: "Озвучити", clear: "Очистити", page: "Відкрити на всю сторінку", you: "Ви", listening: "Слухаю… натисніть ще раз, щоб зупинити", thinking: "Думаю…",
    stConnectedHa: "Під’єднано через Home Assistant", stConnectedClaude: "Під’єднано до API Claude", stReady: "Готово", stOff: "Вимкнено",
    stNotSetUp: "Не налаштовано: введіть токен у Core на робочому столі (Gaming Mode > Jarvis)", stHaDown: "Home Assistant недоступний", stClaudeDown: "API Claude недоступне",
    stToken: "Home Assistant відхилив токен", stKey: "Anthropic відхилив ключ", stAgent: "У Home Assistant немає агента Jarvis із такою назвою",
    eBusy: "Jarvis ще відповідає", eMuted: "Мікрофон вимкнено: спершу увімкніть його в розділі «Голос»", eNoMic: "Немає мікрофона", eSilent: "Нічого не почуто", eShort: "Закоротко: утримуйте, поки говорите",
    eNotUnderstood: "Jarvis не розчув", eNeedsKey: "Допомозі з екраном потрібен ваш ключ Anthropic (Core на робочому столі > Gaming Mode > Jarvis)", eNoPicture: "Windows не надав зображення екрана",
    eFailed: "Щось пішло не так; нічого не змінено", eUnreachable: "Не вдається зв’язатися з Jarvis", eAntiCheat: "Друк призупинено, поки ввімкнено захист від читів; текст у Швидкому доступі",
    eNoRoute: "Розпізнавання мовлення недоступне: збережіть токен Home Assistant", eRate: "Забагато запитів; спробуйте трохи згодом", eOff: "Jarvis вимкнено",
    screenshot: "Знімок екрана зроблено", party: "Ваша вечірка Xbox теж вас чує", typed: "Надруковано: {text}", heard: "Почуто: {text}",
    combos: "Торкніться {talk}, щоб говорити (зупиняється на паузі) · утримуйте, щоб відкрити Jarvis · диктування й допомога з екраном: кнопки вище або голосом", comboOff: "вимкнено",
    hint: "Нічого не надсилається, доки ви не попросите. Замки, гараж і сигналізація — лише через телефон.", helperOff: "Допомога з екраном: додайте ключ Anthropic на робочому столі",
  },
  zh: {
    title: "Jarvis", placeholder: "问问 Jarvis…", send: "发送", dictate: "听写", dictateStop: "停止并输入", talk: "与 Jarvis 对话", talkStop: "停止并发送",
    screen: "屏幕上是什么？", more: "更多", speak: "朗读", clear: "清除", page: "打开整页", you: "你", listening: "正在聆听…再按一次停止", thinking: "正在思考…",
    stConnectedHa: "已通过 Home Assistant 连接", stConnectedClaude: "已连接 Claude API", stReady: "就绪", stOff: "已关闭",
    stNotSetUp: "未设置：在桌面上的 Core 中输入令牌（Gaming Mode > Jarvis）", stHaDown: "无法连接 Home Assistant", stClaudeDown: "无法连接 Claude API",
    stToken: "Home Assistant 拒绝了令牌", stKey: "Anthropic 拒绝了密钥", stAgent: "Home Assistant 中没有该名称的 Jarvis 代理",
    eBusy: "Jarvis 仍在回答", eMuted: "麦克风已静音：请先在“语音”中取消静音", eNoMic: "没有麦克风", eSilent: "没有听到声音", eShort: "太短了：说话时请一直按住",
    eNotUnderstood: "Jarvis 没听清", eNeedsKey: "屏幕助手需要你的 Anthropic 密钥（桌面上的 Core > Gaming Mode > Jarvis）", eNoPicture: "Windows 没有提供屏幕画面",
    eFailed: "出了点问题；没有做任何更改", eUnreachable: "无法连接 Jarvis", eAntiCheat: "反作弊保护开启时暂停输入；文字在快速访问中",
    eNoRoute: "没有可用的语音识别：请保存 Home Assistant 令牌", eRate: "请求太多；请稍后再试", eOff: "Jarvis 已关闭",
    screenshot: "已截图", party: "你的 Xbox 派对也能听到你", typed: "已输入：{text}", heard: "听到：{text}",
    combos: "轻按 {talk} 对话（停顿时结束）· 按住打开 Jarvis · 听写和屏幕助手：用上方按钮或语音", comboOff: "关闭",
    hint: "在你要求之前不会发送任何内容。门锁、车库和警报需要你的手机。", helperOff: "屏幕助手：在桌面上添加 Anthropic 密钥即可使用",
  },
  ja: {
    title: "Jarvis", placeholder: "Jarvis に聞く…", send: "送信", dictate: "音声入力", dictateStop: "止めて入力", talk: "Jarvis と話す", talkStop: "止めて送信",
    screen: "画面に何がある？", more: "もっと", speak: "読み上げ", clear: "消去", page: "全画面で開く", you: "あなた", listening: "聞いています… もう一度押すと止まります", thinking: "考え中…",
    stConnectedHa: "Home Assistant 経由で接続", stConnectedClaude: "Claude API に接続", stReady: "準備完了", stOff: "オフ",
    stNotSetUp: "未設定：デスクトップの Core でトークンを入力してください（Gaming Mode > Jarvis）", stHaDown: "Home Assistant に接続できません", stClaudeDown: "Claude API に接続できません",
    stToken: "Home Assistant がトークンを拒否しました", stKey: "Anthropic がキーを拒否しました", stAgent: "Home Assistant にその名前の Jarvis エージェントがありません",
    eBusy: "Jarvis はまだ応答中です", eMuted: "マイクがミュートです：先にボイスでミュートを解除してください", eNoMic: "マイクがありません", eSilent: "何も聞こえませんでした", eShort: "短すぎます：話している間は押し続けてください",
    eNotUnderstood: "Jarvis は聞き取れませんでした", eNeedsKey: "画面ヘルパーには Anthropic のキーが必要です（デスクトップの Core > Gaming Mode > Jarvis）", eNoPicture: "Windows から画面の画像が得られませんでした",
    eFailed: "問題が起きました。何も変更していません", eUnreachable: "Jarvis に接続できません", eAntiCheat: "チート対策保護が有効な間は入力を止めています。テキストはクイックアクセスにあります",
    eNoRoute: "音声認識が使えません：Home Assistant のトークンを保存してください", eRate: "リクエストが多すぎます。少し待ってから試してください", eOff: "Jarvis はオフです",
    screenshot: "スクリーンショットを撮りました", party: "Xbox パーティーにも聞こえています", typed: "入力しました：{text}", heard: "聞こえた内容：{text}",
    combos: "{talk} を軽く押して話す（間があくと終了）· 長押しで Jarvis を開く · 音声入力と画面ヘルパーは上のボタンか声で", comboOff: "オフ",
    hint: "頼むまで何も送信されません。鍵、ガレージ、アラームはスマホが必要です。", helperOff: "画面ヘルパー：デスクトップで Anthropic のキーを追加すると使えます",
  },
  ko: {
    title: "Jarvis", placeholder: "Jarvis에게 물어보기…", send: "보내기", dictate: "받아쓰기", dictateStop: "멈추고 입력", talk: "Jarvis와 대화", talkStop: "멈추고 보내기",
    screen: "화면에 뭐가 있지?", more: "더 보기", speak: "읽어 주기", clear: "지우기", page: "전체 페이지로 열기", you: "나", listening: "듣는 중… 다시 누르면 멈춰요", thinking: "생각하는 중…",
    stConnectedHa: "Home Assistant로 연결됨", stConnectedClaude: "Claude API에 연결됨", stReady: "준비됨", stOff: "꺼짐",
    stNotSetUp: "설정 안 됨: 데스크톱의 Core에서 토큰을 입력하세요(Gaming Mode > Jarvis)", stHaDown: "Home Assistant에 연결할 수 없음", stClaudeDown: "Claude API에 연결할 수 없음",
    stToken: "Home Assistant가 토큰을 거부함", stKey: "Anthropic이 키를 거부함", stAgent: "Home Assistant에 그 이름의 Jarvis 에이전트가 없음",
    eBusy: "Jarvis가 아직 답하는 중이에요", eMuted: "마이크가 음소거됨: 먼저 음성에서 켜세요", eNoMic: "마이크 없음", eSilent: "아무 소리도 안 들렸어요", eShort: "너무 짧아요: 말하는 동안 계속 누르세요",
    eNotUnderstood: "Jarvis가 알아듣지 못했어요", eNeedsKey: "화면 도우미에는 Anthropic 키가 필요해요(데스크톱의 Core > Gaming Mode > Jarvis)", eNoPicture: "Windows가 화면 이미지를 주지 않았어요",
    eFailed: "문제가 생겼어요. 아무것도 바뀌지 않았어요", eUnreachable: "Jarvis에 연결할 수 없어요", eAntiCheat: "안티치트 보호가 켜져 있는 동안 입력을 멈춰요. 텍스트는 빠른 실행에 있어요",
    eNoRoute: "음성 인식을 쓸 수 없어요: Home Assistant 토큰을 저장하세요", eRate: "요청이 너무 많아요. 잠시 후 다시 시도하세요", eOff: "Jarvis가 꺼져 있어요",
    screenshot: "스크린샷을 찍었어요", party: "Xbox 파티에도 들려요", typed: "입력함: {text}", heard: "들은 내용: {text}",
    combos: "{talk}을(를) 짧게 눌러 대화(말이 멈추면 끝) · 길게 누르면 Jarvis 열기 · 받아쓰기와 화면 도우미: 위 버튼 또는 음성으로", comboOff: "꺼짐",
    hint: "요청하기 전에는 아무것도 보내지 않아요. 잠금장치, 차고, 경보는 휴대폰이 필요해요.", helperOff: "화면 도우미: 데스크톱에서 Anthropic 키를 추가하면 쓸 수 있어요",
  },
  hi: {
    title: "Jarvis", placeholder: "Jarvis से पूछें…", send: "भेजें", dictate: "बोलकर लिखें", dictateStop: "रोकें और लिखें", talk: "Jarvis से बात करें", talkStop: "रोकें और भेजें",
    screen: "स्क्रीन पर क्या है?", more: "और", speak: "पढ़कर सुनाएँ", clear: "मिटाएँ", page: "पूरे पेज पर खोलें", you: "आप", listening: "सुन रहा है… रोकने के लिए फिर दबाएँ", thinking: "सोच रहा है…",
    stConnectedHa: "Home Assistant से जुड़ा", stConnectedClaude: "Claude API से जुड़ा", stReady: "तैयार", stOff: "बंद",
    stNotSetUp: "सेट नहीं है: डेस्कटॉप पर Core में टोकन डालें (Gaming Mode > Jarvis)", stHaDown: "Home Assistant तक नहीं पहुँच पा रहे", stClaudeDown: "Claude API तक नहीं पहुँच पा रहे",
    stToken: "Home Assistant ने टोकन ठुकराया", stKey: "Anthropic ने कुंजी ठुकराई", stAgent: "Home Assistant में इस नाम का कोई Jarvis एजेंट नहीं है",
    eBusy: "Jarvis अभी जवाब दे रहा है", eMuted: "माइक म्यूट है: पहले Voice में चालू करें", eNoMic: "कोई माइक्रोफ़ोन नहीं", eSilent: "कुछ सुनाई नहीं दिया", eShort: "बहुत छोटा: बोलते समय दबाए रखें",
    eNotUnderstood: "Jarvis समझ नहीं पाया", eNeedsKey: "स्क्रीन सहायक को आपकी Anthropic कुंजी चाहिए (डेस्कटॉप पर Core > Gaming Mode > Jarvis)", eNoPicture: "Windows ने स्क्रीन की कोई तस्वीर नहीं दी",
    eFailed: "कुछ गड़बड़ हुई; कुछ नहीं बदला गया", eUnreachable: "Jarvis तक नहीं पहुँच पा रहे", eAntiCheat: "एंटी-चीट सुरक्षा चालू रहते टाइपिंग रुकी है; टेक्स्ट क्विक एक्सेस में है",
    eNoRoute: "कोई वाक् पहचान उपलब्ध नहीं: Home Assistant टोकन सहेजें", eRate: "बहुत ज़्यादा अनुरोध; थोड़ी देर में फिर कोशिश करें", eOff: "Jarvis बंद है",
    screenshot: "स्क्रीनशॉट लिया गया", party: "आपकी Xbox पार्टी भी आपको सुन रही है", typed: "लिखा गया: {text}", heard: "सुना गया: {text}",
    combos: "बात करने के लिए {talk} टैप करें (रुकने पर बंद) · Jarvis खोलने के लिए दबाए रखें · डिक्टेशन और स्क्रीन सहायक: ऊपर के बटन, या आवाज़ से", comboOff: "बंद",
    hint: "जब तक आप न कहें, कुछ नहीं भेजा जाता। ताले, गैराज और अलार्म के लिए फ़ोन चाहिए।", helperOff: "स्क्रीन सहायक: इस्तेमाल के लिए डेस्कटॉप पर Anthropic कुंजी जोड़ें",
  },
  ru: {
    title: "Jarvis", placeholder: "Спросите Jarvis…", send: "Отправить", dictate: "Диктовать", dictateStop: "Остановить и напечатать", talk: "Говорить с Jarvis", talkStop: "Остановить и отправить",
    screen: "Что на экране?", more: "Ещё", speak: "Озвучить", clear: "Очистить", page: "Открыть на всю страницу", you: "Вы", listening: "Слушаю… нажмите ещё раз, чтобы остановить", thinking: "Думаю…",
    stConnectedHa: "Подключено через Home Assistant", stConnectedClaude: "Подключено к API Claude", stReady: "Готово", stOff: "Выключено",
    stNotSetUp: "Не настроено: введите токен в Core на рабочем столе (Gaming Mode > Jarvis)", stHaDown: "Home Assistant недоступен", stClaudeDown: "API Claude недоступен",
    stToken: "Home Assistant отклонил токен", stKey: "Anthropic отклонил ключ", stAgent: "В Home Assistant нет агента Jarvis с таким именем",
    eBusy: "Jarvis ещё отвечает", eMuted: "Микрофон выключен: сначала включите его в разделе «Голос»", eNoMic: "Нет микрофона", eSilent: "Ничего не слышно", eShort: "Слишком коротко: удерживайте, пока говорите",
    eNotUnderstood: "Jarvis не расслышал", eNeedsKey: "Помощи с экраном нужен ваш ключ Anthropic (Core на рабочем столе > Gaming Mode > Jarvis)", eNoPicture: "Windows не дал изображения экрана",
    eFailed: "Что-то пошло не так; ничего не изменено", eUnreachable: "Не удаётся связаться с Jarvis", eAntiCheat: "Печать приостановлена, пока включена защита от читов; текст в Быстром доступе",
    eNoRoute: "Распознавание речи недоступно: сохраните токен Home Assistant", eRate: "Слишком много запросов; попробуйте чуть позже", eOff: "Jarvis выключен",
    screenshot: "Снимок экрана сделан", party: "Ваша вечеринка Xbox тоже вас слышит", typed: "Напечатано: {text}", heard: "Услышано: {text}",
    combos: "Коснитесь {talk}, чтобы говорить (останавливается на паузе) · удерживайте, чтобы открыть Jarvis · диктовка и помощь с экраном: кнопки выше или голосом", comboOff: "выкл.",
    hint: "Ничего не отправляется, пока вы не попросите. Замки, гараж и сигнализация — только через телефон.", helperOff: "Помощь с экраном: добавьте ключ Anthropic на рабочем столе",
  },
};

export const jarvisLanguages = Object.keys(copy);

export function jarvisCopy(locale: string): Copy {
  const language = String(locale || "en").toLowerCase().split(/[-_]/)[0];
  const table = copy[language === "ua" ? "uk" : language] ?? en;
  return new Proxy(table, { get: (target, key: string) => target[key] ?? en[key] ?? key });
}

export function fill(template: string, values: Record<string, string | number>): string {
  return template.replace(/\{(\w+)\}/g, (all, name) => (name in values ? String(values[name]) : all));
}

export type JarvisTurn = { role: "user" | "assistant"; text: string };
export type JarvisState = {
  enabled?: boolean; backend?: string; status?: string; screenHelper?: boolean; spoilerSafe?: boolean; voice?: boolean;
  combos?: { dictate?: string | null; talk?: string | null; screen?: string | null };
  listening?: { active?: boolean; mode?: string | null }; history?: JarvisTurn[]; screen?: { text?: string; more?: boolean } | null; busy?: boolean; antiCheat?: boolean;
  // Core UI phase 1: a short tap on the talk combo opens Jarvis's Quick Access tab (default on).
  tapOpens?: boolean;
};

// The status line under the title.
export function statusLine(state: JarvisState | null | undefined, locale: string): string {
  const text = jarvisCopy(locale);
  if (!state) return text.eUnreachable;
  if (state.enabled === false || state.status === "off") return text.stOff;
  switch (state.status) {
    case "connected": return state.backend === "claude" ? text.stConnectedClaude : text.stConnectedHa;
    case "ready": return text.stReady;
    case "not-set-up": return text.stNotSetUp;
    case "ha-unreachable": return text.stHaDown;
    case "claude-unreachable": return text.stClaudeDown;
    case "token-refused": return text.stToken;
    case "key-refused": return text.stKey;
    case "agent-missing": return text.stAgent;
    default: return text.stReady;
  }
}

// The tone of the status line: green, amber, crimson or steel (corePalette's CORE_STATUS).
export function statusTone(state: JarvisState | null | undefined): "ok" | "warning" | "error" | "neutral" {
  if (!state || state.enabled === false) return "neutral";
  if (state.status === "connected") return "ok";
  if (state.status === "not-set-up" || state.status === "ready") return "neutral";
  return state.status === "token-refused" || state.status === "key-refused" ? "error" : "warning";
}

// An error code from the agent, in words.
export function errorLine(code: unknown, locale: string): string {
  const text = jarvisCopy(locale);
  const map: Record<string, string> = {
    busy: text.eBusy, muted: text.eMuted, "no-mic": text.eNoMic, silent: text.eSilent, "too-short": text.eShort, "not-understood": text.eNotUnderstood,
    "needs-key": text.eNeedsKey, "no-picture": text.eNoPicture, unreachable: text.eUnreachable, timeout: text.eUnreachable, "anti-cheat": text.eAntiCheat,
    "no-route": text.eNoRoute, "rate-limited": text.eRate, overloaded: text.eRate, off: text.eOff, "not-set-up": text.stNotSetUp,
    "token-refused": text.stToken, "key-refused": text.stKey, "not-found": text.stAgent, "server-error": text.eUnreachable,
  };
  return typeof code === "string" && map[code] ? map[code] : text.eFailed;
}

export function combosLine(state: JarvisState | null | undefined, locale: string): string {
  const text = jarvisCopy(locale);
  const show = (value: unknown) => (typeof value === "string" && /^[A-Z0-9+]{3,40}$/.test(value) ? value : text.comboOff);
  // 3.3.5: only L5+R5 (tap talks, hold opens); dictation and the screen helper are buttons and voice.
  return fill(text.combos, { talk: show(state?.combos?.talk) });
}

// The last few turns, cleaned: only user/assistant, strings, at most `limit` entries, each capped.
export function historyLines(state: JarvisState | null | undefined, limit = 6): JarvisTurn[] {
  const turns = Array.isArray(state?.history) ? state!.history! : [];
  return turns
    .filter(turn => turn && (turn.role === "user" || turn.role === "assistant") && typeof turn.text === "string" && turn.text.trim().length > 0)
    .slice(-limit)
    .map(turn => ({ role: turn.role, text: turn.text.trim().slice(0, 1200) }));
}
