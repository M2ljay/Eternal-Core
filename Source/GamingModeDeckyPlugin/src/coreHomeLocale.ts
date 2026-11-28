// Eternal Core: Core Home's text in Core's 12 languages (docs/eternal/CORE-UI.md, "Phase 2: Home"). "Jarvis", "Forge"
// and the console names are names and stay the same everywhere. Self-contained: tests load it alone.

export type HomeCopy = {
  brief: string; consoles: string;
  // 3.3.9: the Favorites tab (first on Home) and its empty state.
  favorites: string; favoritesEmpty: string;
  morning: string; afternoon: string; evening: string; night: string;
  lastPlayed: string; nothingYet: string; talk: string;
  mods: string; modsOne: string; updates: string; empty: string; vr: string; retro: string;
} & HomeCopy3310;

// 3.3.10: Home's two pages (Home, Consoles) and the Achievements tab. "Steam" and "RetroAchievements" are names.
export type HomeCopy3310 = {
  home: string; all: string; achievements: string; recentUnlocks: string; closest: string; recentlyPlayed: string; hardcore: string;
  players: string; left: string; achievementsEmpty: string; consolesEmpty: string;
};
const extra: Record<string, HomeCopy3310> = {
  en: { home: "Home", all: "All", achievements: "Achievements", recentUnlocks: "Recent unlocks", closest: "Closest to 100%", recentlyPlayed: "Recently played",
    hardcore: "Hardcore", players: "{n}% of players", left: "{n} left", achievementsEmpty: "Unlock an achievement and it shows up here",
    consolesEmpty: "Sync your RomM library in The Collector to see your consoles here" },
  it: { home: "Home", all: "Tutti", achievements: "Obiettivi", recentUnlocks: "Sbloccati di recente", closest: "Più vicini al 100%", recentlyPlayed: "Giocati di recente",
    hardcore: "Hardcore", players: "{n}% dei giocatori", left: "ne mancano {n}", achievementsEmpty: "Sblocca un obiettivo e comparirà qui",
    consolesEmpty: "Sincronizza la libreria RomM in The Collector per vedere qui le tue console" },
  es: { home: "Inicio", all: "Todos", achievements: "Logros", recentUnlocks: "Desbloqueados hace poco", closest: "Más cerca del 100 %", recentlyPlayed: "Jugados recientemente",
    hardcore: "Hardcore", players: "{n} % de jugadores", left: "faltan {n}", achievementsEmpty: "Desbloquea un logro y aparecerá aquí",
    consolesEmpty: "Sincroniza tu biblioteca de RomM en The Collector para ver aquí tus consolas" },
  fr: { home: "Accueil", all: "Tous", achievements: "Succès", recentUnlocks: "Débloqués récemment", closest: "Les plus proches de 100 %", recentlyPlayed: "Joués récemment",
    hardcore: "Hardcore", players: "{n} % des joueurs", left: "encore {n}", achievementsEmpty: "Débloquez un succès et il apparaîtra ici",
    consolesEmpty: "Synchronisez votre bibliothèque RomM dans The Collector pour voir vos consoles ici" },
  de: { home: "Start", all: "Alle", achievements: "Erfolge", recentUnlocks: "Zuletzt freigeschaltet", closest: "Am nächsten an 100 %", recentlyPlayed: "Zuletzt gespielt",
    hardcore: "Hardcore", players: "{n} % der Spieler", left: "noch {n}", achievementsEmpty: "Schalte einen Erfolg frei, dann erscheint er hier",
    consolesEmpty: "Synchronisiere deine RomM-Bibliothek in The Collector, um deine Konsolen hier zu sehen" },
  pt: { home: "Início", all: "Todos", achievements: "Conquistas", recentUnlocks: "Desbloqueadas recentemente", closest: "Mais perto de 100%", recentlyPlayed: "Jogados recentemente",
    hardcore: "Hardcore", players: "{n}% dos jogadores", left: "faltam {n}", achievementsEmpty: "Desbloqueie uma conquista e ela aparecerá aqui",
    consolesEmpty: "Sincronize sua biblioteca do RomM no The Collector para ver seus consoles aqui" },
  uk: { home: "Головна", all: "Усі", achievements: "Досягнення", recentUnlocks: "Нещодавно здобуті", closest: "Найближчі до 100%", recentlyPlayed: "Нещодавно грали",
    hardcore: "Hardcore", players: "{n}% гравців", left: "лишилось {n}", achievementsEmpty: "Здобудьте досягнення, і воно з'явиться тут",
    consolesEmpty: "Синхронізуйте бібліотеку RomM у The Collector, щоб побачити тут свої консолі" },
  ru: { home: "Главная", all: "Все", achievements: "Достижения", recentUnlocks: "Недавно получены", closest: "Ближе всего к 100%", recentlyPlayed: "Недавние игры",
    hardcore: "Hardcore", players: "{n}% игроков", left: "осталось {n}", achievementsEmpty: "Получите достижение, и оно появится здесь",
    consolesEmpty: "Синхронизируйте библиотеку RomM в The Collector, чтобы увидеть здесь свои консоли" },
  zh: { home: "主页", all: "全部", achievements: "成就", recentUnlocks: "最近解锁", closest: "最接近 100%", recentlyPlayed: "最近玩过",
    hardcore: "硬核", players: "{n}% 的玩家", left: "还剩 {n} 个", achievementsEmpty: "解锁一项成就，它就会显示在这里",
    consolesEmpty: "在 The Collector 中同步你的 RomM 库，即可在这里看到你的主机" },
  ja: { home: "ホーム", all: "すべて", achievements: "実績", recentUnlocks: "最近解除した実績", closest: "100% に最も近い", recentlyPlayed: "最近プレイした",
    hardcore: "ハードコア", players: "プレイヤーの {n}%", left: "残り {n}", achievementsEmpty: "実績を解除するとここに表示されます",
    consolesEmpty: "The Collector で RomM ライブラリを同期すると、ここにコンソールが表示されます" },
  ko: { home: "홈", all: "전체", achievements: "도전 과제", recentUnlocks: "최근 달성", closest: "100%에 가장 가까운 게임", recentlyPlayed: "최근 플레이",
    hardcore: "하드코어", players: "플레이어의 {n}%", left: "{n}개 남음", achievementsEmpty: "도전 과제를 달성하면 여기에 표시됩니다",
    consolesEmpty: "The Collector에서 RomM 라이브러리를 동기화하면 여기에 콘솔이 표시됩니다" },
  hi: { home: "होम", all: "सभी", achievements: "उपलब्धियाँ", recentUnlocks: "हाल में अनलॉक", closest: "100% के सबसे क़रीब", recentlyPlayed: "हाल में खेले गए",
    hardcore: "हार्डकोर", players: "{n}% खिलाड़ी", left: "{n} बाकी", achievementsEmpty: "कोई उपलब्धि अनलॉक करें, वह यहाँ दिखेगी",
    consolesEmpty: "अपने कंसोल यहाँ देखने के लिए The Collector में अपनी RomM लाइब्रेरी सिंक करें" },
};

const en: Omit<HomeCopy, keyof HomeCopy3310> = {
  brief: "Today", consoles: "Consoles", favorites: "Favorites", favoritesEmpty: "Star a game to add it here",
  morning: "Good morning", afternoon: "Good afternoon", evening: "Good evening", night: "Up late",
  lastPlayed: "You were last playing {game}.", nothingYet: "Nothing on the go yet. Pick something and I'll keep track.",
  talk: "Talk to Jarvis", mods: "{count} mods have updates in Forge", modsOne: "1 mod has an update in Forge",
  updates: "News and updates for your games", empty: "No games in this collection yet", vr: "VR", retro: "Retro",
};

const copy: Record<string, Omit<HomeCopy, keyof HomeCopy3310>> = {
  en,
  it: { brief: "Oggi", consoles: "Console", favorites: "Preferiti", favoritesEmpty: "Aggiungi una stella a un gioco per vederlo qui", morning: "Buongiorno", afternoon: "Buon pomeriggio", evening: "Buonasera", night: "Ancora sveglio",
    lastPlayed: "L'ultima volta giocavi a {game}.", nothingYet: "Ancora niente in corso. Scegli un gioco e ne terrò traccia.", talk: "Parla con Jarvis",
    mods: "{count} mod hanno aggiornamenti in Forge", modsOne: "1 mod ha un aggiornamento in Forge", updates: "Notizie e aggiornamenti dei tuoi giochi", empty: "Ancora nessun gioco in questa raccolta", vr: "VR", retro: "Retro" },
  es: { brief: "Hoy", consoles: "Consolas", favorites: "Favoritos", favoritesEmpty: "Marca un juego con una estrella para añadirlo aquí", morning: "Buenos días", afternoon: "Buenas tardes", evening: "Buenas noches", night: "Trasnochando",
    lastPlayed: "Lo último que jugaste fue {game}.", nothingYet: "Nada en curso todavía. Elige algo y llevaré la cuenta.", talk: "Hablar con Jarvis",
    mods: "{count} mods tienen actualizaciones en Forge", modsOne: "1 mod tiene una actualización en Forge", updates: "Noticias y actualizaciones de tus juegos", empty: "Aún no hay juegos en esta colección", vr: "RV", retro: "Retro" },
  fr: { brief: "Aujourd'hui", consoles: "Consoles", favorites: "Favoris", favoritesEmpty: "Ajoutez une étoile à un jeu pour l'ajouter ici", morning: "Bonjour", afternoon: "Bon après-midi", evening: "Bonsoir", night: "Encore debout",
    lastPlayed: "Votre dernière partie : {game}.", nothingYet: "Rien en cours pour l'instant. Choisissez un jeu, je m'occupe du reste.", talk: "Parler à Jarvis",
    mods: "{count} mods ont des mises à jour dans Forge", modsOne: "1 mod a une mise à jour dans Forge", updates: "Actualités et mises à jour de vos jeux", empty: "Aucun jeu dans cette collection pour l'instant", vr: "VR", retro: "Rétro" },
  de: { brief: "Heute", consoles: "Konsolen", favorites: "Favoriten", favoritesEmpty: "Markiere ein Spiel mit einem Stern, um es hier hinzuzufügen", morning: "Guten Morgen", afternoon: "Guten Tag", evening: "Guten Abend", night: "Noch wach",
    lastPlayed: "Zuletzt hast du {game} gespielt.", nothingYet: "Noch nichts angefangen. Such dir etwas aus, ich behalte den Überblick.", talk: "Mit Jarvis sprechen",
    mods: "{count} Mods haben Updates in Forge", modsOne: "1 Mod hat ein Update in Forge", updates: "Neuigkeiten und Updates zu deinen Spielen", empty: "Noch keine Spiele in dieser Sammlung", vr: "VR", retro: "Retro" },
  pt: { brief: "Hoje", consoles: "Consoles", favorites: "Favoritos", favoritesEmpty: "Marque um jogo com uma estrela para adicioná-lo aqui", morning: "Bom dia", afternoon: "Boa tarde", evening: "Boa noite", night: "Acordado até tarde",
    lastPlayed: "Você jogou {game} por último.", nothingYet: "Nada em andamento ainda. Escolha algo e eu acompanho.", talk: "Falar com o Jarvis",
    mods: "{count} mods têm atualizações no Forge", modsOne: "1 mod tem uma atualização no Forge", updates: "Notícias e atualizações dos seus jogos", empty: "Ainda não há jogos nesta coleção", vr: "RV", retro: "Retrô" },
  uk: { brief: "Сьогодні", consoles: "Консолі", favorites: "Улюблені", favoritesEmpty: "Позначте гру зірочкою, щоб додати її сюди", morning: "Доброго ранку", afternoon: "Добрий день", evening: "Добрий вечір", night: "Не спиться",
    lastPlayed: "Востаннє ви грали в {game}.", nothingYet: "Поки нічого не розпочато. Оберіть гру, а я стежитиму.", talk: "Поговорити з Jarvis",
    mods: "Модів з оновленнями у Forge: {count}", modsOne: "1 мод має оновлення у Forge", updates: "Новини й оновлення ваших ігор", empty: "У цій колекції ще немає ігор", vr: "VR", retro: "Ретро" },
  ru: { brief: "Сегодня", consoles: "Консоли", favorites: "Избранное", favoritesEmpty: "Отметьте игру звёздочкой, чтобы добавить её сюда", morning: "Доброе утро", afternoon: "Добрый день", evening: "Добрый вечер", night: "Не спится",
    lastPlayed: "В последний раз вы играли в {game}.", nothingYet: "Пока ничего не начато. Выберите игру, а я буду следить.", talk: "Поговорить с Jarvis",
    mods: "Модов с обновлениями в Forge: {count}", modsOne: "1 мод получил обновление в Forge", updates: "Новости и обновления ваших игр", empty: "В этой коллекции пока нет игр", vr: "VR", retro: "Ретро" },
  zh: { brief: "今天", consoles: "主机", favorites: "收藏", favoritesEmpty: "为游戏加星标即可添加到这里", morning: "早上好", afternoon: "下午好", evening: "晚上好", night: "这么晚还没睡",
    lastPlayed: "你上次在玩 {game}。", nothingYet: "还没有进行中的游戏。选一个吧，我会帮你记着。", talk: "与 Jarvis 对话",
    mods: "Forge 中有 {count} 个模组可更新", modsOne: "Forge 中有 1 个模组可更新", updates: "你的游戏的新闻和更新", empty: "此收藏中还没有游戏", vr: "VR", retro: "复古" },
  ja: { brief: "今日", consoles: "コンソール", favorites: "お気に入り", favoritesEmpty: "ゲームにスターを付けるとここに追加されます", morning: "おはようございます", afternoon: "こんにちは", evening: "こんばんは", night: "夜更かしですね",
    lastPlayed: "最後に遊んだのは {game} です。", nothingYet: "進行中のゲームはまだありません。選んでいただければ記録しておきます。", talk: "Jarvis と話す",
    mods: "Forge で {count} 件の MOD に更新があります", modsOne: "Forge で 1 件の MOD に更新があります", updates: "あなたのゲームのニュースと更新", empty: "このコレクションにはまだゲームがありません", vr: "VR", retro: "レトロ" },
  ko: { brief: "오늘", consoles: "콘솔", favorites: "즐겨찾기", favoritesEmpty: "게임에 별표를 하면 여기에 추가됩니다", morning: "좋은 아침입니다", afternoon: "좋은 오후입니다", evening: "좋은 저녁입니다", night: "늦게까지 계시네요",
    lastPlayed: "마지막으로 {game}을(를) 플레이했습니다.", nothingYet: "아직 진행 중인 게임이 없습니다. 하나 고르시면 기록해 두겠습니다.", talk: "Jarvis와 대화",
    mods: "Forge에 업데이트가 있는 모드 {count}개", modsOne: "Forge에 업데이트가 있는 모드 1개", updates: "내 게임의 소식과 업데이트", empty: "이 컬렉션에는 아직 게임이 없습니다", vr: "VR", retro: "레트로" },
  hi: { brief: "आज", consoles: "कंसोल", favorites: "पसंदीदा", favoritesEmpty: "किसी गेम को यहाँ जोड़ने के लिए उसे स्टार करें", morning: "सुप्रभात", afternoon: "नमस्कार", evening: "शुभ संध्या", night: "देर तक जाग रहे हैं",
    lastPlayed: "आपने आख़िरी बार {game} खेला था।", nothingYet: "अभी कुछ चल नहीं रहा। कुछ चुनिए, मैं ध्यान रखूँगा।", talk: "Jarvis से बात करें",
    mods: "Forge में {count} मॉड के अपडेट हैं", modsOne: "Forge में 1 मॉड का अपडेट है", updates: "आपके गेम की ख़बरें और अपडेट", empty: "इस संग्रह में अभी कोई गेम नहीं है", vr: "VR", retro: "रेट्रो" },
};

export const coreHomeLanguages = Object.keys(copy);

export function coreHomeCopy(locale: string): HomeCopy {
  const language = String(locale || "en").toLowerCase().split(/[-_]/)[0];
  const key = language === "ua" ? "uk" : language;
  return { ...extra.en, ...(copy[key] ? extra[key] ?? {} : {}), ...(copy[key] ?? en) } as HomeCopy;
}

// The greeting for the hour (local time): night 0-4, morning 5-11, afternoon 12-17, evening 18-23. The household
// keeps late hours, so 4am is still "up late".
export function greetingKey(hour: number): "morning" | "afternoon" | "evening" | "night" {
  const h = ((Math.floor(hour) % 24) + 24) % 24;
  return h < 5 ? "night" : h < 12 ? "morning" : h < 18 ? "afternoon" : "evening";
}
