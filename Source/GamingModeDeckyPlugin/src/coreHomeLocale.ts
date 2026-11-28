// Eternal Core: Core Home's text in Core's 12 languages (docs/eternal/CORE-UI.md, "Phase 2: Home"). "Jarvis", "Forge"
// and the console names are names and stay the same everywhere. Self-contained: tests load it alone.

export type HomeCopy = {
  brief: string; consoles: string;
  // 3.3.9: the Favorites tab (first on Home) and its empty state.
  favorites: string; favoritesEmpty: string;
  morning: string; afternoon: string; evening: string; night: string;
  lastPlayed: string; nothingYet: string; talk: string;
  mods: string; modsOne: string; updates: string; empty: string; vr: string; retro: string;
};

const en: HomeCopy = {
  brief: "Today", consoles: "Consoles", favorites: "Favorites", favoritesEmpty: "Favorite a game in your Library to see it here",
  morning: "Good morning", afternoon: "Good afternoon", evening: "Good evening", night: "Up late",
  lastPlayed: "You were last playing {game}.", nothingYet: "Nothing on the go yet. Pick something and I'll keep track.",
  talk: "Talk to Jarvis", mods: "{count} mods have updates in Forge", modsOne: "1 mod has an update in Forge",
  updates: "News and updates for your games", empty: "No games in this collection yet", vr: "VR", retro: "Retro",
};

const copy: Record<string, HomeCopy> = {
  en,
  it: { brief: "Oggi", consoles: "Console", favorites: "Preferiti", favoritesEmpty: "Aggiungi un gioco ai preferiti nella Libreria per vederlo qui", morning: "Buongiorno", afternoon: "Buon pomeriggio", evening: "Buonasera", night: "Ancora sveglio",
    lastPlayed: "L'ultima volta giocavi a {game}.", nothingYet: "Ancora niente in corso. Scegli un gioco e ne terrò traccia.", talk: "Parla con Jarvis",
    mods: "{count} mod hanno aggiornamenti in Forge", modsOne: "1 mod ha un aggiornamento in Forge", updates: "Notizie e aggiornamenti dei tuoi giochi", empty: "Ancora nessun gioco in questa raccolta", vr: "VR", retro: "Retro" },
  es: { brief: "Hoy", consoles: "Consolas", favorites: "Favoritos", favoritesEmpty: "Marca un juego como favorito en tu Biblioteca para verlo aquí", morning: "Buenos días", afternoon: "Buenas tardes", evening: "Buenas noches", night: "Trasnochando",
    lastPlayed: "Lo último que jugaste fue {game}.", nothingYet: "Nada en curso todavía. Elige algo y llevaré la cuenta.", talk: "Hablar con Jarvis",
    mods: "{count} mods tienen actualizaciones en Forge", modsOne: "1 mod tiene una actualización en Forge", updates: "Noticias y actualizaciones de tus juegos", empty: "Aún no hay juegos en esta colección", vr: "RV", retro: "Retro" },
  fr: { brief: "Aujourd'hui", consoles: "Consoles", favorites: "Favoris", favoritesEmpty: "Ajoutez un jeu aux favoris dans votre bibliothèque pour le voir ici", morning: "Bonjour", afternoon: "Bon après-midi", evening: "Bonsoir", night: "Encore debout",
    lastPlayed: "Votre dernière partie : {game}.", nothingYet: "Rien en cours pour l'instant. Choisissez un jeu, je m'occupe du reste.", talk: "Parler à Jarvis",
    mods: "{count} mods ont des mises à jour dans Forge", modsOne: "1 mod a une mise à jour dans Forge", updates: "Actualités et mises à jour de vos jeux", empty: "Aucun jeu dans cette collection pour l'instant", vr: "VR", retro: "Rétro" },
  de: { brief: "Heute", consoles: "Konsolen", favorites: "Favoriten", favoritesEmpty: "Markiere ein Spiel in deiner Bibliothek als Favorit, um es hier zu sehen", morning: "Guten Morgen", afternoon: "Guten Tag", evening: "Guten Abend", night: "Noch wach",
    lastPlayed: "Zuletzt hast du {game} gespielt.", nothingYet: "Noch nichts angefangen. Such dir etwas aus, ich behalte den Überblick.", talk: "Mit Jarvis sprechen",
    mods: "{count} Mods haben Updates in Forge", modsOne: "1 Mod hat ein Update in Forge", updates: "Neuigkeiten und Updates zu deinen Spielen", empty: "Noch keine Spiele in dieser Sammlung", vr: "VR", retro: "Retro" },
  pt: { brief: "Hoje", consoles: "Consoles", favorites: "Favoritos", favoritesEmpty: "Marque um jogo como favorito na sua Biblioteca para vê-lo aqui", morning: "Bom dia", afternoon: "Boa tarde", evening: "Boa noite", night: "Acordado até tarde",
    lastPlayed: "Você jogou {game} por último.", nothingYet: "Nada em andamento ainda. Escolha algo e eu acompanho.", talk: "Falar com o Jarvis",
    mods: "{count} mods têm atualizações no Forge", modsOne: "1 mod tem uma atualização no Forge", updates: "Notícias e atualizações dos seus jogos", empty: "Ainda não há jogos nesta coleção", vr: "RV", retro: "Retrô" },
  uk: { brief: "Сьогодні", consoles: "Консолі", favorites: "Улюблені", favoritesEmpty: "Додайте гру до улюблених у Бібліотеці, щоб побачити її тут", morning: "Доброго ранку", afternoon: "Добрий день", evening: "Добрий вечір", night: "Не спиться",
    lastPlayed: "Востаннє ви грали в {game}.", nothingYet: "Поки нічого не розпочато. Оберіть гру, а я стежитиму.", talk: "Поговорити з Jarvis",
    mods: "Модів з оновленнями у Forge: {count}", modsOne: "1 мод має оновлення у Forge", updates: "Новини й оновлення ваших ігор", empty: "У цій колекції ще немає ігор", vr: "VR", retro: "Ретро" },
  ru: { brief: "Сегодня", consoles: "Консоли", favorites: "Избранное", favoritesEmpty: "Добавьте игру в избранное в Библиотеке, чтобы увидеть её здесь", morning: "Доброе утро", afternoon: "Добрый день", evening: "Добрый вечер", night: "Не спится",
    lastPlayed: "В последний раз вы играли в {game}.", nothingYet: "Пока ничего не начато. Выберите игру, а я буду следить.", talk: "Поговорить с Jarvis",
    mods: "Модов с обновлениями в Forge: {count}", modsOne: "1 мод получил обновление в Forge", updates: "Новости и обновления ваших игр", empty: "В этой коллекции пока нет игр", vr: "VR", retro: "Ретро" },
  zh: { brief: "今天", consoles: "主机", favorites: "收藏", favoritesEmpty: "在库中收藏一款游戏，即可在这里看到它", morning: "早上好", afternoon: "下午好", evening: "晚上好", night: "这么晚还没睡",
    lastPlayed: "你上次在玩 {game}。", nothingYet: "还没有进行中的游戏。选一个吧，我会帮你记着。", talk: "与 Jarvis 对话",
    mods: "Forge 中有 {count} 个模组可更新", modsOne: "Forge 中有 1 个模组可更新", updates: "你的游戏的新闻和更新", empty: "此收藏中还没有游戏", vr: "VR", retro: "复古" },
  ja: { brief: "今日", consoles: "コンソール", favorites: "お気に入り", favoritesEmpty: "ライブラリでゲームをお気に入りに追加すると、ここに表示されます", morning: "おはようございます", afternoon: "こんにちは", evening: "こんばんは", night: "夜更かしですね",
    lastPlayed: "最後に遊んだのは {game} です。", nothingYet: "進行中のゲームはまだありません。選んでいただければ記録しておきます。", talk: "Jarvis と話す",
    mods: "Forge で {count} 件の MOD に更新があります", modsOne: "Forge で 1 件の MOD に更新があります", updates: "あなたのゲームのニュースと更新", empty: "このコレクションにはまだゲームがありません", vr: "VR", retro: "レトロ" },
  ko: { brief: "오늘", consoles: "콘솔", favorites: "즐겨찾기", favoritesEmpty: "라이브러리에서 게임을 즐겨찾기에 추가하면 여기에 표시됩니다", morning: "좋은 아침입니다", afternoon: "좋은 오후입니다", evening: "좋은 저녁입니다", night: "늦게까지 계시네요",
    lastPlayed: "마지막으로 {game}을(를) 플레이했습니다.", nothingYet: "아직 진행 중인 게임이 없습니다. 하나 고르시면 기록해 두겠습니다.", talk: "Jarvis와 대화",
    mods: "Forge에 업데이트가 있는 모드 {count}개", modsOne: "Forge에 업데이트가 있는 모드 1개", updates: "내 게임의 소식과 업데이트", empty: "이 컬렉션에는 아직 게임이 없습니다", vr: "VR", retro: "레트로" },
  hi: { brief: "आज", consoles: "कंसोल", favorites: "पसंदीदा", favoritesEmpty: "यहाँ देखने के लिए अपनी लाइब्रेरी में किसी गेम को पसंदीदा बनाएँ", morning: "सुप्रभात", afternoon: "नमस्कार", evening: "शुभ संध्या", night: "देर तक जाग रहे हैं",
    lastPlayed: "आपने आख़िरी बार {game} खेला था।", nothingYet: "अभी कुछ चल नहीं रहा। कुछ चुनिए, मैं ध्यान रखूँगा।", talk: "Jarvis से बात करें",
    mods: "Forge में {count} मॉड के अपडेट हैं", modsOne: "Forge में 1 मॉड का अपडेट है", updates: "आपके गेम की ख़बरें और अपडेट", empty: "इस संग्रह में अभी कोई गेम नहीं है", vr: "VR", retro: "रेट्रो" },
};

export const coreHomeLanguages = Object.keys(copy);

export function coreHomeCopy(locale: string): HomeCopy {
  const language = String(locale || "en").toLowerCase().split(/[-_]/)[0];
  return copy[language === "ua" ? "uk" : language] ?? en;
}

// The greeting for the hour (local time): night 0-4, morning 5-11, afternoon 12-17, evening 18-23. The household
// keeps late hours, so 4am is still "up late".
export function greetingKey(hour: number): "morning" | "afternoon" | "evening" | "night" {
  const h = ((Math.floor(hour) % 24) + 24) % 24;
  return h < 5 ? "night" : h < 12 ? "morning" : h < 18 ? "afternoon" : "evening";
}
