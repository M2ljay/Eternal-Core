// Eternal Core: the Club tab's text in Core's 12 languages. Event names ("Game of the Week", ...), game titles,
// member names and the bot command stay as the club writes them: they are the club's own names.
// Self-contained (no imports): tests/club.test.mjs loads it alone.

type Plural = Partial<Record<"zero" | "one" | "two" | "few" | "many" | "other", string>> & { other: string };

export type ClubCopy = {
  tab: string; title: string; playing: string; daysLeft: Plural; lastDay: string; ended: string; noDate: string;
  notInLibrary: string; play: string; details: string; standing: string; beaten: string; streak: string; rank: string;
  rankOf: string; crowns: string; setName: string; notFound: string; top: string; nominations: string; voteOpens: string;
  logTitle: string; logDiscord: string; logOptions: string; logForum: string; logDeadline: string; updated: string;
  saved: string; refresh: string; loading: string; noData: string; setting: string; settingHint: string;
  nameLabel: string; nameSave: string; saveFailed: string;
};

const en: ClubCopy = {
  tab: "Club", title: "Play Retro Games Club", playing: "Playing now",
  daysLeft: { one: "{n} day left", other: "{n} days left" }, lastDay: "Last day", ended: "Ended", noDate: "Dates on the club site",
  notInLibrary: "Not in your library", play: "Play", details: "Game page", standing: "Your standing", beaten: "games beaten",
  streak: "streak", rank: "rank", rankOf: "#{rank} of {of}", crowns: "Crowns",
  setName: "Enter your club name in Quick Access > Core > Club to see your standing.",
  notFound: "No member called \"{name}\" on the leaderboard yet.", top: "Top 5", nominations: "Nominations",
  voteOpens: "Vote opens {date}", logTitle: "How to log a win", logDiscord: "On Discord, in the game's thread:",
  logOptions: "Options: event, ra_username (first time only), screenshot (adds 0.75 to your vote weight). On its own, it sends you a private link to log on the website.",
  logForum: "On the forum: open the game's thread in PRGC General and choose Log a completion.",
  logDeadline: "Log it before the pick ends: completions close the moment it does.", updated: "Updated {ago}",
  saved: "Showing saved data", refresh: "Refresh", loading: "Loading the club…", noData: "Club data isn't available yet.",
  setting: "Show the Club tab", settingHint: "Play Retro Games Club picks, your standing and nominations on the Home.",
  nameLabel: "Your club display name", nameSave: "Save name", saveFailed: "Could not save the Club settings.",
};

const copy: Record<string, ClubCopy> = {
  en,
  it: { ...en, tab: "Club", playing: "In gioco ora", daysLeft: { one: "Manca {n} giorno", other: "Mancano {n} giorni" }, lastDay: "Ultimo giorno", ended: "Terminato", noDate: "Date sul sito del club",
    notInLibrary: "Non è nella tua libreria", play: "Gioca", details: "Pagina del gioco", standing: "La tua posizione", beaten: "giochi completati", streak: "serie", rank: "posizione",
    rankOf: "#{rank} su {of}", crowns: "Corone", setName: "Inserisci il tuo nome nel club in Accesso rapido > Core > Club per vedere la tua posizione.",
    notFound: "Nessun membro chiamato \"{name}\" nella classifica per ora.", top: "Top 5", nominations: "Candidature", voteOpens: "Il voto apre il {date}",
    logTitle: "Come registrare una vittoria", logDiscord: "Su Discord, nel thread del gioco:",
    logOptions: "Opzioni: event, ra_username (solo la prima volta), screenshot (aggiunge 0,75 al peso del tuo voto). Da solo ti invia un link privato per registrarla sul sito.",
    logForum: "Sul forum: apri il thread del gioco in PRGC General e scegli Log a completion.", logDeadline: "Registrala prima che la scelta finisca: le registrazioni chiudono in quel momento.",
    updated: "Aggiornato {ago}", saved: "Dati salvati", refresh: "Aggiorna", loading: "Caricamento del club…", noData: "I dati del club non sono ancora disponibili.",
    setting: "Mostra la scheda Club", settingHint: "Le scelte del Play Retro Games Club, la tua posizione e le candidature nella Home.", nameLabel: "Il tuo nome nel club",
    nameSave: "Salva nome", saveFailed: "Impossibile salvare le impostazioni del Club." },
  es: { ...en, playing: "Jugando ahora", daysLeft: { one: "Queda {n} día", other: "Quedan {n} días" }, lastDay: "Último día", ended: "Terminado", noDate: "Fechas en la web del club",
    notInLibrary: "No está en tu biblioteca", play: "Jugar", details: "Página del juego", standing: "Tu posición", beaten: "juegos superados", streak: "racha", rank: "puesto",
    rankOf: "#{rank} de {of}", crowns: "Coronas", setName: "Escribe tu nombre del club en Acceso rápido > Core > Club para ver tu posición.",
    notFound: "Aún no hay ningún miembro llamado \"{name}\" en la clasificación.", top: "Top 5", nominations: "Nominaciones", voteOpens: "La votación abre el {date}",
    logTitle: "Cómo registrar una victoria", logDiscord: "En Discord, en el hilo del juego:",
    logOptions: "Opciones: event, ra_username (solo la primera vez), screenshot (suma 0,75 al peso de tu voto). Solo, te envía un enlace privado para registrarla en la web.",
    logForum: "En el foro: abre el hilo del juego en PRGC General y elige Log a completion.", logDeadline: "Regístrala antes de que acabe la elección: los registros cierran en ese momento.",
    updated: "Actualizado {ago}", saved: "Datos guardados", refresh: "Actualizar", loading: "Cargando el club…", noData: "Los datos del club aún no están disponibles.",
    setting: "Mostrar la pestaña Club", settingHint: "Las elecciones del Play Retro Games Club, tu posición y las nominaciones en Inicio.", nameLabel: "Tu nombre en el club",
    nameSave: "Guardar nombre", saveFailed: "No se pudo guardar la configuración del Club." },
  fr: { ...en, playing: "En cours", daysLeft: { one: "{n} jour restant", other: "{n} jours restants" }, lastDay: "Dernier jour", ended: "Terminé", noDate: "Dates sur le site du club",
    notInLibrary: "Absent de votre bibliothèque", play: "Jouer", details: "Page du jeu", standing: "Votre classement", beaten: "jeux terminés", streak: "série", rank: "rang",
    rankOf: "#{rank} sur {of}", crowns: "Couronnes", setName: "Saisissez votre nom de membre dans Accès rapide > Core > Club pour voir votre classement.",
    notFound: "Aucun membre nommé « {name} » dans le classement pour l'instant.", top: "Top 5", nominations: "Nominations", voteOpens: "Vote le {date}",
    logTitle: "Enregistrer une victoire", logDiscord: "Sur Discord, dans le fil du jeu :",
    logOptions: "Options : event, ra_username (la première fois seulement), screenshot (ajoute 0,75 au poids de votre vote). Seule, elle vous envoie un lien privé pour l'enregistrer sur le site.",
    logForum: "Sur le forum : ouvrez le fil du jeu dans PRGC General et choisissez Log a completion.", logDeadline: "Enregistrez-la avant la fin de la sélection : les enregistrements ferment à ce moment-là.",
    updated: "Mis à jour {ago}", saved: "Données enregistrées", refresh: "Actualiser", loading: "Chargement du club…", noData: "Les données du club ne sont pas encore disponibles.",
    setting: "Afficher l'onglet Club", settingHint: "Les sélections du Play Retro Games Club, votre classement et les nominations sur l'accueil.", nameLabel: "Votre nom de membre",
    nameSave: "Enregistrer le nom", saveFailed: "Impossible d'enregistrer les paramètres du Club." },
  de: { ...en, playing: "Läuft gerade", daysLeft: { one: "Noch {n} Tag", other: "Noch {n} Tage" }, lastDay: "Letzter Tag", ended: "Beendet", noDate: "Termine auf der Club-Website",
    notInLibrary: "Nicht in deiner Bibliothek", play: "Spielen", details: "Spielseite", standing: "Dein Stand", beaten: "Spiele geschafft", streak: "Serie", rank: "Platz",
    rankOf: "Platz {rank} von {of}", crowns: "Kronen", setName: "Gib deinen Club-Namen unter Schnellzugriff > Core > Club ein, um deinen Stand zu sehen.",
    notFound: "Noch kein Mitglied namens „{name}“ in der Rangliste.", top: "Top 5", nominations: "Nominierungen", voteOpens: "Abstimmung ab {date}",
    logTitle: "So trägst du einen Sieg ein", logDiscord: "Auf Discord, im Thread des Spiels:",
    logOptions: "Optionen: event, ra_username (nur beim ersten Mal), screenshot (erhöht dein Stimmgewicht um 0,75). Allein schickt es dir einen privaten Link zum Eintragen auf der Website.",
    logForum: "Im Forum: Öffne den Thread des Spiels in PRGC General und wähle Log a completion.", logDeadline: "Trag ihn ein, bevor die Wahl endet: Einträge schließen genau dann.",
    updated: "Aktualisiert {ago}", saved: "Gespeicherte Daten", refresh: "Aktualisieren", loading: "Club wird geladen…", noData: "Club-Daten sind noch nicht verfügbar.",
    setting: "Club-Tab anzeigen", settingHint: "Die Picks des Play Retro Games Club, dein Stand und die Nominierungen auf der Startseite.", nameLabel: "Dein Club-Anzeigename",
    nameSave: "Namen speichern", saveFailed: "Club-Einstellungen konnten nicht gespeichert werden." },
  pt: { ...en, playing: "Jogando agora", daysLeft: { one: "Falta {n} dia", other: "Faltam {n} dias" }, lastDay: "Último dia", ended: "Encerrado", noDate: "Datas no site do clube",
    notInLibrary: "Não está na sua biblioteca", play: "Jogar", details: "Página do jogo", standing: "Sua posição", beaten: "jogos zerados", streak: "sequência", rank: "posição",
    rankOf: "#{rank} de {of}", crowns: "Coroas", setName: "Digite seu nome no clube em Acesso rápido > Core > Club para ver sua posição.",
    notFound: "Ainda não há membro chamado \"{name}\" no ranking.", top: "Top 5", nominations: "Indicações", voteOpens: "Votação abre em {date}",
    logTitle: "Como registrar uma vitória", logDiscord: "No Discord, no tópico do jogo:",
    logOptions: "Opções: event, ra_username (só na primeira vez), screenshot (soma 0,75 ao peso do seu voto). Sozinho, envia um link privado para registrar no site.",
    logForum: "No fórum: abra o tópico do jogo em PRGC General e escolha Log a completion.", logDeadline: "Registre antes que a escolha termine: os registros fecham nesse momento.",
    updated: "Atualizado {ago}", saved: "Dados salvos", refresh: "Atualizar", loading: "Carregando o clube…", noData: "Os dados do clube ainda não estão disponíveis.",
    setting: "Mostrar a aba Club", settingHint: "As escolhas do Play Retro Games Club, sua posição e as indicações na página inicial.", nameLabel: "Seu nome no clube",
    nameSave: "Salvar nome", saveFailed: "Não foi possível salvar as configurações do Club." },
  uk: { ...en, tab: "Клуб", playing: "Зараз грають", daysLeft: { one: "Залишився {n} день", few: "Залишилося {n} дні", many: "Залишилося {n} днів", other: "Залишилося {n} дня" },
    lastDay: "Останній день", ended: "Завершено", noDate: "Дати на сайті клубу", notInLibrary: "Немає у вашій бібліотеці", play: "Грати", details: "Сторінка гри",
    standing: "Ваше місце", beaten: "ігор пройдено", streak: "серія", rank: "місце", rankOf: "№{rank} з {of}", crowns: "Корони",
    setName: "Введіть своє ім'я в клубі в Швидкий доступ > Core > Club, щоб бачити своє місце.", notFound: "У рейтингу поки немає учасника «{name}».",
    top: "Топ-5", nominations: "Номінації", voteOpens: "Голосування з {date}", logTitle: "Як зареєструвати перемогу", logDiscord: "У Discord, у гілці гри:",
    logOptions: "Параметри: event, ra_username (лише першого разу), screenshot (додає 0,75 до ваги вашого голосу). Сама команда надсилає приватне посилання для реєстрації на сайті.",
    logForum: "На форумі: відкрийте гілку гри в PRGC General і виберіть Log a completion.", logDeadline: "Зареєструйте до кінця вибору: реєстрація закривається саме тоді.",
    updated: "Оновлено {ago}", saved: "Збережені дані", refresh: "Оновити", loading: "Завантаження клубу…", noData: "Дані клубу ще недоступні.",
    setting: "Показувати вкладку «Клуб»", settingHint: "Вибір Play Retro Games Club, ваше місце й номінації на головній.", nameLabel: "Ваше ім'я в клубі",
    nameSave: "Зберегти ім'я", saveFailed: "Не вдалося зберегти налаштування клубу." },
  ru: { ...en, tab: "Клуб", playing: "Сейчас играют", daysLeft: { one: "Остался {n} день", few: "Осталось {n} дня", many: "Осталось {n} дней", other: "Осталось {n} дня" },
    lastDay: "Последний день", ended: "Завершено", noDate: "Даты на сайте клуба", notInLibrary: "Нет в вашей библиотеке", play: "Играть", details: "Страница игры",
    standing: "Ваше место", beaten: "игр пройдено", streak: "серия", rank: "место", rankOf: "№{rank} из {of}", crowns: "Короны",
    setName: "Введите своё имя в клубе в Быстрый доступ > Core > Club, чтобы видеть своё место.", notFound: "В рейтинге пока нет участника «{name}».",
    top: "Топ-5", nominations: "Номинации", voteOpens: "Голосование с {date}", logTitle: "Как записать победу", logDiscord: "В Discord, в ветке игры:",
    logOptions: "Параметры: event, ra_username (только в первый раз), screenshot (добавляет 0,75 к весу вашего голоса). Сама команда присылает личную ссылку для записи на сайте.",
    logForum: "На форуме: откройте ветку игры в PRGC General и выберите Log a completion.", logDeadline: "Запишите до конца выбора: запись закрывается в этот момент.",
    updated: "Обновлено {ago}", saved: "Сохранённые данные", refresh: "Обновить", loading: "Загрузка клуба…", noData: "Данные клуба пока недоступны.",
    setting: "Показывать вкладку «Клуб»", settingHint: "Выбор Play Retro Games Club, ваше место и номинации на главной.", nameLabel: "Ваше имя в клубе",
    nameSave: "Сохранить имя", saveFailed: "Не удалось сохранить настройки клуба." },
  zh: { ...en, tab: "俱乐部", playing: "正在游玩", daysLeft: { other: "还剩 {n} 天" }, lastDay: "最后一天", ended: "已结束", noDate: "日期见俱乐部网站",
    notInLibrary: "不在你的库中", play: "开始游戏", details: "游戏页面", standing: "你的排名", beaten: "款已通关", streak: "连续", rank: "排名", rankOf: "第 {rank} 名，共 {of} 人",
    crowns: "王冠", setName: "在 快速访问 > Core > Club 中输入你的俱乐部名称即可查看排名。", notFound: "排行榜上还没有名为“{name}”的成员。", top: "前 5 名",
    nominations: "提名", voteOpens: "{date} 开始投票", logTitle: "如何登记通关", logDiscord: "在 Discord 上，于该游戏的帖子中：",
    logOptions: "选项：event、ra_username（仅首次）、screenshot（为你的投票权重加 0.75）。单独使用时会私信你一个在网站上登记的链接。",
    logForum: "在论坛上：打开 PRGC General 中该游戏的帖子，选择 Log a completion。", logDeadline: "请在本期结束前登记：结束时登记即关闭。",
    updated: "更新于{ago}", saved: "显示已保存的数据", refresh: "刷新", loading: "正在加载俱乐部…", noData: "俱乐部数据暂不可用。",
    setting: "显示俱乐部标签页", settingHint: "在主页显示 Play Retro Games Club 的本期游戏、你的排名和提名。", nameLabel: "你的俱乐部显示名称",
    nameSave: "保存名称", saveFailed: "无法保存俱乐部设置。" },
  ja: { ...en, tab: "クラブ", playing: "プレイ中", daysLeft: { other: "残り{n}日" }, lastDay: "最終日", ended: "終了", noDate: "日程はクラブのサイトで",
    notInLibrary: "ライブラリにありません", play: "プレイ", details: "ゲームページ", standing: "あなたの順位", beaten: "本クリア", streak: "連続", rank: "順位",
    rankOf: "{of}人中{rank}位", crowns: "クラウン", setName: "クイックアクセス > Core > Club でクラブの表示名を入力すると順位が表示されます。",
    notFound: "「{name}」というメンバーはまだランキングにいません。", top: "トップ5", nominations: "ノミネート", voteOpens: "{date}に投票開始",
    logTitle: "クリアの記録方法", logDiscord: "Discord のゲームのスレッドで：",
    logOptions: "オプション：event、ra_username（初回のみ）、screenshot（投票の重みに 0.75 加算）。単独で使うと、サイトで記録するための非公開リンクが届きます。",
    logForum: "フォーラムでは：PRGC General のゲームのスレッドを開き、Log a completion を選びます。", logDeadline: "選出期間が終わる前に記録してください。終了と同時に締め切られます。",
    updated: "{ago}に更新", saved: "保存済みのデータを表示中", refresh: "更新", loading: "クラブを読み込み中…", noData: "クラブのデータはまだありません。",
    setting: "クラブタブを表示", settingHint: "ホームに Play Retro Games Club の選出作品、あなたの順位、ノミネートを表示します。", nameLabel: "クラブの表示名",
    nameSave: "名前を保存", saveFailed: "クラブの設定を保存できませんでした。" },
  ko: { ...en, tab: "클럽", playing: "지금 플레이 중", daysLeft: { other: "{n}일 남음" }, lastDay: "마지막 날", ended: "종료", noDate: "날짜는 클럽 사이트에서",
    notInLibrary: "라이브러리에 없음", play: "플레이", details: "게임 페이지", standing: "내 순위", beaten: "개 클리어", streak: "연속", rank: "순위",
    rankOf: "{of}명 중 {rank}위", crowns: "크라운", setName: "빠른 실행 > Core > Club에서 클럽 이름을 입력하면 순위가 표시됩니다.",
    notFound: "아직 순위표에 \"{name}\" 멤버가 없습니다.", top: "상위 5명", nominations: "후보", voteOpens: "{date} 투표 시작",
    logTitle: "클리어 기록 방법", logDiscord: "Discord의 게임 스레드에서:",
    logOptions: "옵션: event, ra_username(처음 한 번만), screenshot(투표 가중치 +0.75). 단독으로 쓰면 웹사이트에서 기록할 비공개 링크를 보내 줍니다.",
    logForum: "포럼에서: PRGC General의 게임 스레드를 열고 Log a completion을 선택하세요.", logDeadline: "선정 기간이 끝나기 전에 기록하세요. 끝나는 순간 마감됩니다.",
    updated: "{ago} 업데이트", saved: "저장된 데이터 표시 중", refresh: "새로 고침", loading: "클럽을 불러오는 중…", noData: "아직 클럽 데이터가 없습니다.",
    setting: "클럽 탭 표시", settingHint: "홈에 Play Retro Games Club 선정작, 내 순위, 후보를 표시합니다.", nameLabel: "클럽 표시 이름",
    nameSave: "이름 저장", saveFailed: "클럽 설정을 저장하지 못했습니다." },
  hi: { ...en, tab: "क्लब", playing: "अभी खेल रहे हैं", daysLeft: { one: "{n} दिन बाकी", other: "{n} दिन बाकी" }, lastDay: "आखिरी दिन", ended: "समाप्त",
    noDate: "तारीखें क्लब की साइट पर", notInLibrary: "आपकी लाइब्रेरी में नहीं", play: "खेलें", details: "गेम पेज", standing: "आपकी स्थिति", beaten: "गेम पूरे किए",
    streak: "लगातार", rank: "रैंक", rankOf: "{of} में #{rank}", crowns: "ताज", setName: "अपनी स्थिति देखने के लिए क्विक एक्सेस > Core > Club में अपना क्लब नाम डालें।",
    notFound: "लीडरबोर्ड पर अभी \"{name}\" नाम का कोई सदस्य नहीं है।", top: "शीर्ष 5", nominations: "नामांकन", voteOpens: "मतदान {date} से",
    logTitle: "जीत कैसे दर्ज करें", logDiscord: "Discord पर, गेम के थ्रेड में:",
    logOptions: "विकल्प: event, ra_username (सिर्फ़ पहली बार), screenshot (आपके वोट के वज़न में 0.75 जोड़ता है)। अकेले चलाने पर वेबसाइट पर दर्ज करने का निजी लिंक भेजता है।",
    logForum: "फ़ोरम पर: PRGC General में गेम का थ्रेड खोलें और Log a completion चुनें।", logDeadline: "चयन खत्म होने से पहले दर्ज करें: उसी पल प्रविष्टियाँ बंद हो जाती हैं।",
    updated: "{ago} अपडेट किया गया", saved: "सहेजा गया डेटा दिखाया जा रहा है", refresh: "रीफ़्रेश करें", loading: "क्लब लोड हो रहा है…", noData: "क्लब का डेटा अभी उपलब्ध नहीं है।",
    setting: "क्लब टैब दिखाएँ", settingHint: "होम पर Play Retro Games Club के चुने गए गेम, आपकी स्थिति और नामांकन।", nameLabel: "आपका क्लब डिस्प्ले नाम",
    nameSave: "नाम सहेजें", saveFailed: "क्लब सेटिंग्स सहेजी नहीं जा सकीं।" },
};

export const clubLanguages = Object.keys(copy);

export function clubLanguage(locale: string): string {
  const language = String(locale || "en").toLowerCase().split(/[-_]/)[0];
  return language === "ua" ? "uk" : language in copy ? language : "en";
}

export function clubCopy(locale: string): ClubCopy {
  return copy[clubLanguage(locale)];
}

export function fill(template: string, values: Record<string, string | number>): string {
  return template.replace(/\{(\w+)\}/g, (match, key) => key in values ? String(values[key]) : match);
}

export function daysLeftText(locale: string, days: number): string {
  const language = clubLanguage(locale);
  const forms = copy[language].daysLeft;
  let category = "other";
  try { category = new Intl.PluralRules(language).select(days); } catch { category = days === 1 ? "one" : "other"; }
  return fill((forms as Record<string, string>)[category] ?? forms.other, { n: days });
}
