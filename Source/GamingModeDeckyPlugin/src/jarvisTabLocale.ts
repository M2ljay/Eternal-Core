// Eternal Core: the text Jarvis's own Quick Access tab adds (Core UI phase 1), in Core's 12 languages. The rest of
// Jarvis's text is jarvisReport.ts. Self-contained: tests load it alone.

type Copy = { tap: string; tapOff: string; last: string };

const en: Copy = { tap: "Tap {talk} to talk · hold it to open or close Jarvis", tapOff: "Tap {talk} to talk · or hold it while you speak", last: "Last answer" };

const copy: Record<string, Copy> = {
  en,
  it: { tap: "Tocca {talk} per parlare · tienilo premuto per aprire o chiudere Jarvis", tapOff: "Tocca {talk} per parlare · oppure tienilo premuto mentre parli", last: "Ultima risposta" },
  es: { tap: "Toca {talk} para hablar · mantenlo para abrir o cerrar Jarvis", tapOff: "Toca {talk} para hablar · o mantenlo mientras hablas", last: "Última respuesta" },
  fr: { tap: "Touchez {talk} pour parler · maintenez pour ouvrir ou fermer Jarvis", tapOff: "Touchez {talk} pour parler · ou maintenez pendant que vous parlez", last: "Dernière réponse" },
  de: { tap: "{talk} tippen zum Sprechen · halten öffnet oder schließt Jarvis", tapOff: "{talk} tippen zum Sprechen · oder beim Sprechen halten", last: "Letzte Antwort" },
  pt: { tap: "Toque em {talk} para falar · segure para abrir ou fechar o Jarvis", tapOff: "Toque em {talk} para falar · ou segure enquanto fala", last: "Última resposta" },
  uk: { tap: "Торкніться {talk}, щоб говорити · утримуйте, щоб відкрити або закрити Jarvis", tapOff: "Торкніться {talk}, щоб говорити · або утримуйте, поки говорите", last: "Остання відповідь" },
  ru: { tap: "Коснитесь {talk}, чтобы говорить · удерживайте, чтобы открыть или закрыть Jarvis", tapOff: "Коснитесь {talk}, чтобы говорить · или удерживайте, пока говорите", last: "Последний ответ" },
  zh: { tap: "轻按 {talk} 对话 · 按住打开或关闭 Jarvis", tapOff: "轻按 {talk} 对话 · 或说话时按住", last: "最新回答" },
  ja: { tap: "{talk} を軽く押して話す · 長押しで Jarvis を開閉", tapOff: "{talk} を軽く押して話す · または話す間長押し", last: "最新の回答" },
  ko: { tap: "{talk}을(를) 짧게 눌러 대화 · 길게 누르면 Jarvis 열기/닫기", tapOff: "{talk}을(를) 짧게 눌러 대화 · 또는 말하는 동안 누르고 있기", last: "마지막 답변" },
  hi: { tap: "बात करने के लिए {talk} टैप करें · Jarvis खोलने या बंद करने के लिए दबाए रखें", tapOff: "बात करने के लिए {talk} टैप करें · या बोलते समय दबाए रखें", last: "आख़िरी जवाब" },
};

export const jarvisTabLanguages = Object.keys(copy);

export function jarvisTabCopy(locale: string): Copy {
  const language = String(locale || "en").toLowerCase().split(/[-_]/)[0];
  return copy[language === "ua" ? "uk" : language] ?? en;
}

// The tab's L5+R5 line (3.3.5: a tap talks; with tapOpens a hold opens or closes the tab, without it a hold is push-to-talk), from the agent's state (combos.talk, tapOpens). Empty when talking has no combo.
export function jarvisTapLine(state: { combos?: { talk?: unknown }; tapOpens?: unknown } | null | undefined, locale: string): string {
  const talk = typeof state?.combos?.talk === "string" && /^[A-Z0-9+]{2,40}$/.test(state.combos.talk) ? state.combos.talk : "";
  if (!talk) return "";
  const text = jarvisTabCopy(locale);
  return (state?.tapOpens === false ? text.tapOff : text.tap).replace("{talk}", talk);
}
