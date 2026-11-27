// Eternal Core: Core Home's text in Core's 12 languages (docs/eternal/CORE-UI.md, "Phase 2: Home"). "Jarvis", "Forge"
// and the console names are names and stay the same everywhere. Self-contained: tests load it alone.

export type HomeCopy = {
  brief: string; consoles: string;
  morning: string; afternoon: string; evening: string; night: string;
  lastPlayed: string; nothingYet: string; talk: string;
  mods: string; modsOne: string; updates: string; empty: string; vr: string; retro: string;
};

const en: HomeCopy = {
  brief: "Jarvis brief", consoles: "Consoles",
  morning: "Good morning, sir", afternoon: "Good afternoon, sir", evening: "Good evening, sir", night: "Up late, sir",
  lastPlayed: "You were last playing {game}.", nothingYet: "Nothing on the go yet. Pick something and I'll keep track.",
  talk: "Talk to Jarvis", mods: "{count} mods have updates in Forge", modsOne: "1 mod has an update in Forge",
  updates: "News and updates for your games", empty: "No games in this collection yet", vr: "VR", retro: "Retro",
};

const copy: Record<string, HomeCopy> = {
  en,
  it: { brief: "Il punto di Jarvis", consoles: "Console", morning: "Buongiorno, signore", afternoon: "Buon pomeriggio, signore", evening: "Buonasera, signore", night: "Ancora sveglio, signore",
    lastPlayed: "L'ultima volta giocavi a {game}.", nothingYet: "Ancora niente in corso. Scegli un gioco e ne terrò traccia.", talk: "Parla con Jarvis",
    mods: "{count} mod hanno aggiornamenti in Forge", modsOne: "1 mod ha un aggiornamento in Forge", updates: "Notizie e aggiornamenti dei tuoi giochi", empty: "Ancora nessun gioco in questa raccolta", vr: "VR", retro: "Retro" },
  es: { brief: "Resumen de Jarvis", consoles: "Consolas", morning: "Buenos días, señor", afternoon: "Buenas tardes, señor", evening: "Buenas noches, señor", night: "Trasnochando, señor",
    lastPlayed: "Lo último que jugaste fue {game}.", nothingYet: "Nada en curso todavía. Elige algo y llevaré la cuenta.", talk: "Hablar con Jarvis",
    mods: "{count} mods tienen actualizaciones en Forge", modsOne: "1 mod tiene una actualización en Forge", updates: "Noticias y actualizaciones de tus juegos", empty: "Aún no hay juegos en esta colección", vr: "RV", retro: "Retro" },
  fr: { brief: "Le point de Jarvis", consoles: "Consoles", morning: "Bonjour, monsieur", afternoon: "Bon après-midi, monsieur", evening: "Bonsoir, monsieur", night: "Encore debout, monsieur",
    lastPlayed: "Votre dernière partie : {game}.", nothingYet: "Rien en cours pour l'instant. Choisissez un jeu, je m'occupe du reste.", talk: "Parler à Jarvis",
    mods: "{count} mods ont des mises à jour dans Forge", modsOne: "1 mod a une mise à jour dans Forge", updates: "Actualités et mises à jour de vos jeux", empty: "Aucun jeu dans cette collection pour l'instant", vr: "VR", retro: "Rétro" },
  de: { brief: "Jarvis' Überblick", consoles: "Konsolen", morning: "Guten Morgen, Sir", afternoon: "Guten Tag, Sir", evening: "Guten Abend, Sir", night: "Noch wach, Sir",
    lastPlayed: "Zuletzt hast du {game} gespielt.", nothingYet: "Noch nichts angefangen. Such dir etwas aus, ich behalte den Überblick.", talk: "Mit Jarvis sprechen",
    mods: "{count} Mods haben Updates in Forge", modsOne: "1 Mod hat ein Update in Forge", updates: "Neuigkeiten und Updates zu deinen Spielen", empty: "Noch keine Spiele in dieser Sammlung", vr: "VR", retro: "Retro" },
  pt: { brief: "Resumo do Jarvis", consoles: "Consoles", morning: "Bom dia, senhor", afternoon: "Boa tarde, senhor", evening: "Boa noite, senhor", night: "Acordado até tarde, senhor",
    lastPlayed: "Você jogou {game} por último.", nothingYet: "Nada em andamento ainda. Escolha algo e eu acompanho.", talk: "Falar com o Jarvis",
    mods: "{count} mods têm atualizações no Forge", modsOne: "1 mod tem uma atualização no Forge", updates: "Notícias e atualizações dos seus jogos", empty: "Ainda não há jogos nesta coleção", vr: "RV", retro: "Retrô" },
  uk: { brief: "Підсумок від Jarvis", consoles: "Консолі", morning: "Доброго ранку, сер", afternoon: "Добрий день, сер", evening: "Добрий вечір, сер", night: "Не спиться, сер",
    lastPlayed: "Востаннє ви грали в {game}.", nothingYet: "Поки нічого не розпочато. Оберіть гру, а я стежитиму.", talk: "Поговорити з Jarvis",
    mods: "Модів з оновленнями у Forge: {count}", modsOne: "1 мод має оновлення у Forge", updates: "Новини й оновлення ваших ігор", empty: "У цій колекції ще немає ігор", vr: "VR", retro: "Ретро" },
  ru: { brief: "Сводка Jarvis", consoles: "Консоли", morning: "Доброе утро, сэр", afternoon: "Добрый день, сэр", evening: "Добрый вечер, сэр", night: "Не спится, сэр",
    lastPlayed: "В последний раз вы играли в {game}.", nothingYet: "Пока ничего не начато. Выберите игру, а я буду следить.", talk: "Поговорить с Jarvis",
    mods: "Модов с обновлениями в Forge: {count}", modsOne: "1 мод получил обновление в Forge", updates: "Новости и обновления ваших игр", empty: "В этой коллекции пока нет игр", vr: "VR", retro: "Ретро" },
  zh: { brief: "Jarvis 简报", consoles: "主机", morning: "早上好，先生", afternoon: "下午好，先生", evening: "晚上好，先生", night: "这么晚还没睡，先生",
    lastPlayed: "你上次在玩 {game}。", nothingYet: "还没有进行中的游戏。选一个吧，我会帮你记着。", talk: "与 Jarvis 对话",
    mods: "Forge 中有 {count} 个模组可更新", modsOne: "Forge 中有 1 个模组可更新", updates: "你的游戏的新闻和更新", empty: "此收藏中还没有游戏", vr: "VR", retro: "复古" },
  ja: { brief: "Jarvis のブリーフィング", consoles: "コンソール", morning: "おはようございます", afternoon: "こんにちは", evening: "こんばんは", night: "夜更かしですね",
    lastPlayed: "最後に遊んだのは {game} です。", nothingYet: "進行中のゲームはまだありません。選んでいただければ記録しておきます。", talk: "Jarvis と話す",
    mods: "Forge で {count} 件の MOD に更新があります", modsOne: "Forge で 1 件の MOD に更新があります", updates: "あなたのゲームのニュースと更新", empty: "このコレクションにはまだゲームがありません", vr: "VR", retro: "レトロ" },
  ko: { brief: "Jarvis 브리핑", consoles: "콘솔", morning: "좋은 아침입니다", afternoon: "좋은 오후입니다", evening: "좋은 저녁입니다", night: "늦게까지 계시네요",
    lastPlayed: "마지막으로 {game}을(를) 플레이했습니다.", nothingYet: "아직 진행 중인 게임이 없습니다. 하나 고르시면 기록해 두겠습니다.", talk: "Jarvis와 대화",
    mods: "Forge에 업데이트가 있는 모드 {count}개", modsOne: "Forge에 업데이트가 있는 모드 1개", updates: "내 게임의 소식과 업데이트", empty: "이 컬렉션에는 아직 게임이 없습니다", vr: "VR", retro: "레트로" },
  hi: { brief: "Jarvis की ब्रीफ़", consoles: "कंसोल", morning: "सुप्रभात, सर", afternoon: "नमस्कार, सर", evening: "शुभ संध्या, सर", night: "देर तक जाग रहे हैं, सर",
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
