// Eternal Core 3.3.5: the one controller reference, shown in Quick Access > Core > Controls (and in
// docs/eternal/CONSOLE-GUARDS.md). The emulator table must equal EmulatorCatalog.HotkeyTable in the agent
// (GamingMode.Services/EmulatorHealth.cs); tests/coreEmulators.test.mjs compares them. Select is the hold modifier;
// nothing quits an emulator (games are exited through Steam's Exit Game); the back paddles are never in an
// emulator combo: L4 is A, R4 is B, L5 + R5 is Jarvis. Self-contained on purpose (the test loads this file alone).

export const EMULATOR_HOTKEYS: ReadonlyArray<readonly [string, string]> = [
  ["save", "Select + R1"],
  ["load", "Select + L1"],
  ["slot-next", "Select + D-pad right"],
  ["slot-prev", "Select + D-pad left"],
  ["fast-forward", "Select + R2 (hold)"],
  ["rewind", "Select + L2 (hold)"],
  ["pause", "Select + A"],
  ["menu", "Select + Y"],
  ["fps", "Select + X"],
];

type Copy = Record<string, string>;

const en: Copy = {
  title: "Controls", emulators: "Emulator hotkeys (the same in every emulator)", other: "Everywhere",
  save: "Save state", load: "Load state", "slot-next": "Next save slot", "slot-prev": "Previous save slot", "fast-forward": "Fast-forward", rewind: "Rewind", pause: "Pause", menu: "Emulator menu", fps: "FPS display",
  right: "D-pad right", left: "D-pad left", hold: "hold",
  exit: "Exit a game", exitHow: "Steam's Exit Game (Steam button or Quick Access); no combo quits an emulator",
  paddleA: "L4", paddleAHow: "A", paddleB: "R4", paddleBHow: "B",
  jarvisTalk: "Talk to Jarvis", jarvisTalkHow: "Tap L5 + R5", jarvisOpen: "Open Jarvis", jarvisOpenHow: "Hold L5 + R5",
  launchers: "Close store launchers after a game", launchersHelp: "A launcher a game started is closed politely when that game ends, unless it was open before, another of its games runs, or it is downloading.",
  exitNow: "Exit {title}", exitUntracked: "Steam lost track of {title}; Core can close it.",
};

const copy: Record<string, Copy> = {
  en,
  it: { title: "Comandi", emulators: "Scorciatoie degli emulatori (uguali in ogni emulatore)", other: "Ovunque", save: "Salva stato", load: "Carica stato", "slot-next": "Slot successivo", "slot-prev": "Slot precedente", "fast-forward": "Avanti veloce", rewind: "Riavvolgi", pause: "Pausa", menu: "Menu dell'emulatore", fps: "Indicatore FPS", right: "croce destra", left: "croce sinistra", hold: "tieni premuto", exit: "Uscire da un gioco", exitHow: "Esci dal gioco di Steam (tasto Steam o Accesso rapido); nessuna combinazione chiude un emulatore", paddleA: "L4", paddleAHow: "A", paddleB: "R4", paddleBHow: "B", jarvisTalk: "Parlare con Jarvis", jarvisTalkHow: "Tocca L5 + R5", jarvisOpen: "Aprire Jarvis", jarvisOpenHow: "Tieni premuti L5 + R5", launchers: "Chiudi i launcher dei negozi dopo un gioco", launchersHelp: "Un launcher aperto da un gioco viene chiuso con garbo quando il gioco finisce, a meno che fosse già aperto, un altro suo gioco sia in corso o stia scaricando.", exitNow: "Esci da {title}", exitUntracked: "Steam ha perso traccia di {title}; Core può chiuderlo." },
  es: { title: "Controles", emulators: "Atajos de los emuladores (iguales en todos)", other: "En todas partes", save: "Guardar estado", load: "Cargar estado", "slot-next": "Ranura siguiente", "slot-prev": "Ranura anterior", "fast-forward": "Avance rápido", rewind: "Rebobinar", pause: "Pausa", menu: "Menú del emulador", fps: "Indicador de FPS", right: "cruceta derecha", left: "cruceta izquierda", hold: "mantener", exit: "Salir de un juego", exitHow: "Salir del juego de Steam (botón Steam o Acceso rápido); ninguna combinación cierra un emulador", paddleA: "L4", paddleAHow: "A", paddleB: "R4", paddleBHow: "B", jarvisTalk: "Hablar con Jarvis", jarvisTalkHow: "Toca L5 + R5", jarvisOpen: "Abrir Jarvis", jarvisOpenHow: "Mantén L5 + R5", launchers: "Cerrar lanzadores de tiendas tras un juego", launchersHelp: "Un lanzador que abrió un juego se cierra con cuidado al terminar ese juego, salvo que ya estuviera abierto, otro de sus juegos siga o esté descargando.", exitNow: "Salir de {title}", exitUntracked: "Steam perdió el rastro de {title}; Core puede cerrarlo." },
  fr: { title: "Commandes", emulators: "Raccourcis des émulateurs (identiques partout)", other: "Partout", save: "Sauvegarder l'état", load: "Charger l'état", "slot-next": "Emplacement suivant", "slot-prev": "Emplacement précédent", "fast-forward": "Avance rapide", rewind: "Retour arrière", pause: "Pause", menu: "Menu de l'émulateur", fps: "Affichage des FPS", right: "croix droite", left: "croix gauche", hold: "maintenir", exit: "Quitter un jeu", exitHow: "Quitter le jeu de Steam (bouton Steam ou Accès rapide) ; aucune combinaison ne ferme un émulateur", paddleA: "L4", paddleAHow: "A", paddleB: "R4", paddleBHow: "B", jarvisTalk: "Parler à Jarvis", jarvisTalkHow: "Touchez L5 + R5", jarvisOpen: "Ouvrir Jarvis", jarvisOpenHow: "Maintenez L5 + R5", launchers: "Fermer les lanceurs des boutiques après un jeu", launchersHelp: "Un lanceur ouvert par un jeu est fermé poliment à la fin de ce jeu, sauf s'il était déjà ouvert, si un autre de ses jeux tourne ou s'il télécharge.", exitNow: "Quitter {title}", exitUntracked: "Steam a perdu la trace de {title} ; Core peut le fermer." },
  de: { title: "Steuerung", emulators: "Emulator-Tastenkürzel (überall gleich)", other: "Überall", save: "Zustand speichern", load: "Zustand laden", "slot-next": "Nächster Speicherplatz", "slot-prev": "Vorheriger Speicherplatz", "fast-forward": "Vorspulen", rewind: "Zurückspulen", pause: "Pause", menu: "Emulatormenü", fps: "FPS-Anzeige", right: "Steuerkreuz rechts", left: "Steuerkreuz links", hold: "halten", exit: "Ein Spiel beenden", exitHow: "„Spiel beenden“ in Steam (Steam-Taste oder Schnellzugriff); keine Kombination beendet einen Emulator", paddleA: "L4", paddleAHow: "A", paddleB: "R4", paddleBHow: "B", jarvisTalk: "Mit Jarvis sprechen", jarvisTalkHow: "L5 + R5 kurz tippen", jarvisOpen: "Jarvis öffnen", jarvisOpenHow: "L5 + R5 halten", launchers: "Store-Launcher nach einem Spiel schließen", launchersHelp: "Ein Launcher, den ein Spiel gestartet hat, wird nach dem Spiel höflich geschlossen, außer er lief schon vorher, ein anderes seiner Spiele läuft oder er lädt herunter.", exitNow: "{title} beenden", exitUntracked: "Steam hat {title} aus den Augen verloren; Core kann es schließen." },
  pt: { title: "Controles", emulators: "Atalhos dos emuladores (iguais em todos)", other: "Em todo lugar", save: "Salvar estado", load: "Carregar estado", "slot-next": "Próximo slot", "slot-prev": "Slot anterior", "fast-forward": "Avanço rápido", rewind: "Rebobinar", pause: "Pausa", menu: "Menu do emulador", fps: "Indicador de FPS", right: "direcional direita", left: "direcional esquerda", hold: "segurar", exit: "Sair de um jogo", exitHow: "Sair do jogo do Steam (botão Steam ou Acesso rápido); nenhuma combinação fecha um emulador", paddleA: "L4", paddleAHow: "A", paddleB: "R4", paddleBHow: "B", jarvisTalk: "Falar com o Jarvis", jarvisTalkHow: "Toque L5 + R5", jarvisOpen: "Abrir o Jarvis", jarvisOpenHow: "Segure L5 + R5", launchers: "Fechar launchers das lojas depois de um jogo", launchersHelp: "Um launcher que um jogo abriu é fechado com cuidado quando o jogo termina, a menos que já estivesse aberto, outro jogo dele esteja rodando ou ele esteja baixando.", exitNow: "Sair de {title}", exitUntracked: "O Steam perdeu o rastro de {title}; o Core pode fechá-lo." },
  uk: { title: "Керування", emulators: "Гарячі клавіші емуляторів (однакові скрізь)", other: "Скрізь", save: "Зберегти стан", load: "Завантажити стан", "slot-next": "Наступний слот", "slot-prev": "Попередній слот", "fast-forward": "Прискорення", rewind: "Перемотування", pause: "Пауза", menu: "Меню емулятора", fps: "Показ FPS", right: "хрестовина праворуч", left: "хрестовина ліворуч", hold: "утримувати", exit: "Вийти з гри", exitHow: "«Вийти з гри» у Steam (кнопка Steam або Швидкий доступ); жодна комбінація не закриває емулятор", paddleA: "L4", paddleAHow: "A", paddleB: "R4", paddleBHow: "B", jarvisTalk: "Говорити з Jarvis", jarvisTalkHow: "Торкніться L5 + R5", jarvisOpen: "Відкрити Jarvis", jarvisOpenHow: "Утримуйте L5 + R5", launchers: "Закривати лаунчери магазинів після гри", launchersHelp: "Лаунчер, який запустила гра, ввічливо закривається, коли гра завершується, якщо він не був відкритий раніше, не працює інша його гра і він нічого не завантажує.", exitNow: "Вийти з {title}", exitUntracked: "Steam втратив стеження за {title}; Core може її закрити." },
  ru: { title: "Управление", emulators: "Горячие клавиши эмуляторов (одинаковые везде)", other: "Везде", save: "Сохранить состояние", load: "Загрузить состояние", "slot-next": "Следующий слот", "slot-prev": "Предыдущий слот", "fast-forward": "Ускорение", rewind: "Перемотка", pause: "Пауза", menu: "Меню эмулятора", fps: "Показ FPS", right: "крестовина вправо", left: "крестовина влево", hold: "удерживать", exit: "Выйти из игры", exitHow: "«Выйти из игры» в Steam (кнопка Steam или Быстрый доступ); ни одна комбинация не закрывает эмулятор", paddleA: "L4", paddleAHow: "A", paddleB: "R4", paddleBHow: "B", jarvisTalk: "Говорить с Jarvis", jarvisTalkHow: "Коснитесь L5 + R5", jarvisOpen: "Открыть Jarvis", jarvisOpenHow: "Удерживайте L5 + R5", launchers: "Закрывать лаунчеры магазинов после игры", launchersHelp: "Лаунчер, который запустила игра, вежливо закрывается после неё, если он не был открыт раньше, не идёт другая его игра и он ничего не загружает.", exitNow: "Выйти из {title}", exitUntracked: "Steam потерял {title} из виду; Core может её закрыть." },
  zh: { title: "操作", emulators: "模拟器快捷键（所有模拟器相同）", other: "通用", save: "即时存档", load: "读取存档", "slot-next": "下一个存档位", "slot-prev": "上一个存档位", "fast-forward": "快进", rewind: "倒带", pause: "暂停", menu: "模拟器菜单", fps: "FPS 显示", right: "十字键右", left: "十字键左", hold: "按住", exit: "退出游戏", exitHow: "使用 Steam 的“退出游戏”（Steam 键或快速访问）；没有组合键会关闭模拟器", paddleA: "L4", paddleAHow: "A", paddleB: "R4", paddleBHow: "B", jarvisTalk: "和 Jarvis 说话", jarvisTalkHow: "轻按 L5 + R5", jarvisOpen: "打开 Jarvis", jarvisOpenHow: "按住 L5 + R5", launchers: "游戏结束后关闭商店启动器", launchersHelp: "由游戏启动的启动器会在该游戏结束后被礼貌关闭，除非它之前就开着、它的另一款游戏在运行或它正在下载。", exitNow: "退出 {title}", exitUntracked: "Steam 已失去对 {title} 的跟踪；Core 可以关闭它。" },
  ja: { title: "操作", emulators: "エミュレーターのホットキー（どれでも同じ）", other: "共通", save: "ステートセーブ", load: "ステートロード", "slot-next": "次のスロット", "slot-prev": "前のスロット", "fast-forward": "早送り", rewind: "巻き戻し", pause: "一時停止", menu: "エミュレーターのメニュー", fps: "FPS 表示", right: "十字キー右", left: "十字キー左", hold: "長押し", exit: "ゲームの終了", exitHow: "Steam の「ゲームを終了」（Steam ボタンかクイックアクセス）。エミュレーターを閉じる組み合わせはありません", paddleA: "L4", paddleAHow: "A", paddleB: "R4", paddleBHow: "B", jarvisTalk: "Jarvis と話す", jarvisTalkHow: "L5 + R5 を軽く押す", jarvisOpen: "Jarvis を開く", jarvisOpenHow: "L5 + R5 を長押し", launchers: "ゲーム後にストアのランチャーを閉じる", launchersHelp: "ゲームが起動したランチャーは、そのゲームの終了後に丁寧に閉じます。前から開いていた場合、別のゲームが実行中の場合、ダウンロード中の場合は閉じません。", exitNow: "{title} を終了", exitUntracked: "Steam が {title} を見失いました。Core が閉じられます。" },
  ko: { title: "조작", emulators: "에뮬레이터 단축키(모든 에뮬레이터 동일)", other: "어디서나", save: "상태 저장", load: "상태 불러오기", "slot-next": "다음 슬롯", "slot-prev": "이전 슬롯", "fast-forward": "빨리 감기", rewind: "되감기", pause: "일시 정지", menu: "에뮬레이터 메뉴", fps: "FPS 표시", right: "방향키 오른쪽", left: "방향키 왼쪽", hold: "길게", exit: "게임 종료", exitHow: "Steam의 게임 종료(Steam 버튼 또는 빠른 실행). 에뮬레이터를 닫는 조합은 없어요", paddleA: "L4", paddleAHow: "A", paddleB: "R4", paddleBHow: "B", jarvisTalk: "Jarvis와 말하기", jarvisTalkHow: "L5 + R5 짧게 누르기", jarvisOpen: "Jarvis 열기", jarvisOpenHow: "L5 + R5 길게 누르기", launchers: "게임 후 스토어 런처 닫기", launchersHelp: "게임이 연 런처는 그 게임이 끝나면 부드럽게 닫혀요. 원래 열려 있었거나, 다른 게임이 실행 중이거나, 다운로드 중이면 그대로 둬요.", exitNow: "{title} 종료", exitUntracked: "Steam이 {title}을(를) 놓쳤어요. Core가 닫을 수 있어요." },
  hi: { title: "कंट्रोल", emulators: "एम्युलेटर हॉटकी (हर एम्युलेटर में एक जैसी)", other: "हर जगह", save: "स्टेट सेव", load: "स्टेट लोड", "slot-next": "अगला स्लॉट", "slot-prev": "पिछला स्लॉट", "fast-forward": "फ़ास्ट-फ़ॉरवर्ड", rewind: "रिवाइंड", pause: "रोकें", menu: "एम्युलेटर मेन्यू", fps: "FPS दिखाएँ", right: "डी-पैड दाएँ", left: "डी-पैड बाएँ", hold: "दबाए रखें", exit: "गेम से बाहर निकलना", exitHow: "Steam का गेम से बाहर निकलें (Steam बटन या क्विक एक्सेस); कोई कॉम्बो एम्युलेटर बंद नहीं करता", paddleA: "L4", paddleAHow: "A", paddleB: "R4", paddleBHow: "B", jarvisTalk: "Jarvis से बात करें", jarvisTalkHow: "L5 + R5 हल्का दबाएँ", jarvisOpen: "Jarvis खोलें", jarvisOpenHow: "L5 + R5 दबाए रखें", launchers: "गेम के बाद स्टोर लॉन्चर बंद करें", launchersHelp: "जो लॉन्चर किसी गेम ने खोला, वह गेम खत्म होने पर आराम से बंद होता है, जब तक वह पहले से खुला न हो, उसका दूसरा गेम न चल रहा हो या वह डाउनलोड न कर रहा हो।", exitNow: "{title} से बाहर निकलें", exitUntracked: "Steam ने {title} का ट्रैक खो दिया; Core इसे बंद कर सकता है।" },
};

export const controlsLanguages = Object.keys(copy);

export function controlsCopy(locale: string): Copy {
  const language = String(locale || "en").toLowerCase().split(/[-_]/)[0];
  const table = copy[language === "ua" ? "uk" : language] ?? en;
  return new Proxy(table, { get: (target, key: string) => target[key] ?? en[key] ?? key });
}

export function fillControls(template: string, values: Record<string, string>): string {
  return template.replace(/\{(\w+)\}/g, (all, name) => (name in values ? values[name] : all));
}

// The table as lines for the user's language: the button combo with "D-pad right/left" and "(hold)" translated.
export function controlsRows(locale: string): Array<{ label: string; buttons: string; group: "emulators" | "other" }> {
  const text = controlsCopy(locale);
  const rows: Array<{ label: string; buttons: string; group: "emulators" | "other" }> = EMULATOR_HOTKEYS.map(([id, buttons]) => ({
    label: text[id], group: "emulators" as const,
    buttons: buttons.replace("D-pad right", text.right).replace("D-pad left", text.left).replace("(hold)", `(${text.hold})`),
  }));
  rows.push({ label: text.exit, buttons: text.exitHow, group: "other" });
  rows.push({ label: text.paddleA, buttons: text.paddleAHow, group: "other" });
  rows.push({ label: text.paddleB, buttons: text.paddleBHow, group: "other" });
  rows.push({ label: text.jarvisTalk, buttons: text.jarvisTalkHow, group: "other" });
  rows.push({ label: text.jarvisOpen, buttons: text.jarvisOpenHow, group: "other" });
  return rows;
}
