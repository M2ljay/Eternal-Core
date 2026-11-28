// Eternal Core: the Today tab's hero (the brief)'s text in Core's 12 languages (docs/eternal/CORE-UI.md, "Phase 2: Home", 3.3.4).
// The greeting, "Talk to Jarvis" and the mods line stay in coreHomeLocale.ts. Self-contained: tests load it alone.

export type BriefCopy = {
  continueLabel: string; play: string; hours: string; hoursOne: string; minutes: string; neverPlayed: string;
  clubLabel: string; daysLeft: string; dayLeft: string; lastDay: string;
  downloads: string; downloadsOne: string; steamUpdate: string; mods: string; modsOne: string; achievements: string;
};

const en: BriefCopy = {
  continueLabel: "Continue", play: "Play", hours: "{count} hours played", hoursOne: "1 hour played", minutes: "{count} minutes played", neverPlayed: "Not played yet",
  clubLabel: "Club pick", daysLeft: "{count} days left", dayLeft: "1 day left", lastDay: "Last day",
  downloads: "{count} downloading", downloadsOne: "1 downloading", steamUpdate: "Steam update waiting", mods: "{count} mod updates", modsOne: "1 mod update",
  achievements: "{count} nearly done",
};

const copy: Record<string, BriefCopy> = {
  en,
  it: { continueLabel: "Continua", play: "Gioca", hours: "{count} ore di gioco", hoursOne: "1 ora di gioco", minutes: "{count} minuti di gioco", neverPlayed: "Non ancora giocato",
    clubLabel: "Scelta del Club", daysLeft: "Ancora {count} giorni", dayLeft: "Ancora 1 giorno", lastDay: "Ultimo giorno",
    downloads: "{count} download in corso", downloadsOne: "1 download in corso", steamUpdate: "Aggiornamento di Steam in attesa", mods: "{count} mod da aggiornare", modsOne: "1 mod da aggiornare",
    achievements: "{count} quasi completati" },
  es: { continueLabel: "Continuar", play: "Jugar", hours: "{count} horas jugadas", hoursOne: "1 hora jugada", minutes: "{count} minutos jugados", neverPlayed: "Aún sin jugar",
    clubLabel: "Elección del Club", daysLeft: "Quedan {count} días", dayLeft: "Queda 1 día", lastDay: "Último día",
    downloads: "{count} descargando", downloadsOne: "1 descargando", steamUpdate: "Actualización de Steam pendiente", mods: "{count} mods por actualizar", modsOne: "1 mod por actualizar",
    achievements: "{count} casi completados" },
  fr: { continueLabel: "Continuer", play: "Jouer", hours: "{count} heures de jeu", hoursOne: "1 heure de jeu", minutes: "{count} minutes de jeu", neverPlayed: "Pas encore joué",
    clubLabel: "Choix du Club", daysLeft: "Encore {count} jours", dayLeft: "Encore 1 jour", lastDay: "Dernier jour",
    downloads: "{count} en téléchargement", downloadsOne: "1 en téléchargement", steamUpdate: "Mise à jour de Steam en attente", mods: "{count} mods à mettre à jour", modsOne: "1 mod à mettre à jour",
    achievements: "{count} presque terminés" },
  de: { continueLabel: "Weiterspielen", play: "Spielen", hours: "{count} Stunden gespielt", hoursOne: "1 Stunde gespielt", minutes: "{count} Minuten gespielt", neverPlayed: "Noch nicht gespielt",
    clubLabel: "Club-Wahl", daysLeft: "Noch {count} Tage", dayLeft: "Noch 1 Tag", lastDay: "Letzter Tag",
    downloads: "{count} Downloads laufen", downloadsOne: "1 Download läuft", steamUpdate: "Steam-Update wartet", mods: "{count} Mod-Updates", modsOne: "1 Mod-Update",
    achievements: "{count} fast geschafft" },
  pt: { continueLabel: "Continuar", play: "Jogar", hours: "{count} horas jogadas", hoursOne: "1 hora jogada", minutes: "{count} minutos jogados", neverPlayed: "Ainda não jogado",
    clubLabel: "Escolha do Club", daysLeft: "Faltam {count} dias", dayLeft: "Falta 1 dia", lastDay: "Último dia",
    downloads: "{count} baixando", downloadsOne: "1 baixando", steamUpdate: "Atualização do Steam aguardando", mods: "{count} mods para atualizar", modsOne: "1 mod para atualizar",
    achievements: "{count} quase concluídos" },
  uk: { continueLabel: "Продовжити", play: "Грати", hours: "Зіграно годин: {count}", hoursOne: "Зіграно 1 годину", minutes: "Зіграно хвилин: {count}", neverPlayed: "Ще не грали",
    clubLabel: "Вибір клубу", daysLeft: "Залишилося днів: {count}", dayLeft: "Залишився 1 день", lastDay: "Останній день",
    downloads: "Завантажується: {count}", downloadsOne: "Завантажується: 1", steamUpdate: "Оновлення Steam очікує", mods: "Оновлень модів: {count}", modsOne: "1 оновлення мода",
    achievements: "Майже завершено: {count}" },
  ru: { continueLabel: "Продолжить", play: "Играть", hours: "Сыграно часов: {count}", hoursOne: "Сыгран 1 час", minutes: "Сыграно минут: {count}", neverPlayed: "Ещё не играли",
    clubLabel: "Выбор клуба", daysLeft: "Осталось дней: {count}", dayLeft: "Остался 1 день", lastDay: "Последний день",
    downloads: "Загружается: {count}", downloadsOne: "Загружается: 1", steamUpdate: "Обновление Steam ожидает", mods: "Обновлений модов: {count}", modsOne: "1 обновление мода",
    achievements: "Почти выполнено: {count}" },
  zh: { continueLabel: "继续", play: "开始游戏", hours: "已玩 {count} 小时", hoursOne: "已玩 1 小时", minutes: "已玩 {count} 分钟", neverPlayed: "尚未游玩",
    clubLabel: "俱乐部精选", daysLeft: "还剩 {count} 天", dayLeft: "还剩 1 天", lastDay: "最后一天",
    downloads: "{count} 个正在下载", downloadsOne: "1 个正在下载", steamUpdate: "Steam 更新待安装", mods: "{count} 个模组可更新", modsOne: "1 个模组可更新",
    achievements: "{count} 个即将完成" },
  ja: { continueLabel: "続きから", play: "プレイ", hours: "{count} 時間プレイ", hoursOne: "1 時間プレイ", minutes: "{count} 分プレイ", neverPlayed: "未プレイ",
    clubLabel: "クラブの選出", daysLeft: "残り {count} 日", dayLeft: "残り 1 日", lastDay: "最終日",
    downloads: "{count} 件ダウンロード中", downloadsOne: "1 件ダウンロード中", steamUpdate: "Steam の更新が待機中", mods: "MOD の更新 {count} 件", modsOne: "MOD の更新 1 件",
    achievements: "あと少し {count} 件" },
  ko: { continueLabel: "이어서 하기", play: "플레이", hours: "{count}시간 플레이", hoursOne: "1시간 플레이", minutes: "{count}분 플레이", neverPlayed: "아직 플레이하지 않음",
    clubLabel: "클럽 선정작", daysLeft: "{count}일 남음", dayLeft: "1일 남음", lastDay: "마지막 날",
    downloads: "{count}개 다운로드 중", downloadsOne: "1개 다운로드 중", steamUpdate: "Steam 업데이트 대기 중", mods: "모드 업데이트 {count}개", modsOne: "모드 업데이트 1개",
    achievements: "{count}개 거의 완료" },
  hi: { continueLabel: "जारी रखें", play: "खेलें", hours: "{count} घंटे खेला", hoursOne: "1 घंटा खेला", minutes: "{count} मिनट खेला", neverPlayed: "अभी खेला नहीं",
    clubLabel: "क्लब की पसंद", daysLeft: "{count} दिन बाकी", dayLeft: "1 दिन बाकी", lastDay: "आख़िरी दिन",
    downloads: "{count} डाउनलोड हो रहे हैं", downloadsOne: "1 डाउनलोड हो रहा है", steamUpdate: "Steam अपडेट प्रतीक्षा में", mods: "{count} मॉड अपडेट", modsOne: "1 मॉड अपडेट",
    achievements: "{count} लगभग पूरे" },
};

export const coreBriefLanguages = Object.keys(copy);

export function coreBriefCopy(locale: string): BriefCopy {
  const language = String(locale || "en").toLowerCase().split(/[-_]/)[0];
  return copy[language === "ua" ? "uk" : language] ?? en;
}
