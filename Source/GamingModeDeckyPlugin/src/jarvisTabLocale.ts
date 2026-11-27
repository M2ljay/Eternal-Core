// Eternal Core: the text Jarvis's own Quick Access tab adds (Core UI phase 1), in Core's 12 languages. The rest of
// Jarvis's text is jarvisReport.ts. Self-contained: tests load it alone.

type Copy = { tap: string; tapOff: string; last: string };

const en: Copy = { tap: "Tap {talk} to open or close Jarvis · hold it to talk", tapOff: "Hold {talk} to talk to Jarvis", last: "Last answer" };

const copy: Record<string, Copy> = {
  en,
  it: { tap: "Tocca {talk} per aprire o chiudere Jarvis · tienilo premuto per parlare", tapOff: "Tieni premuto {talk} per parlare con Jarvis", last: "Ultima risposta" },
  es: { tap: "Toca {talk} para abrir o cerrar Jarvis · mantenlo pulsado para hablar", tapOff: "Mantén pulsado {talk} para hablar con Jarvis", last: "Última respuesta" },
  fr: { tap: "Appuyez sur {talk} pour ouvrir ou fermer Jarvis · maintenez pour parler", tapOff: "Maintenez {talk} pour parler à Jarvis", last: "Dernière réponse" },
  de: { tap: "Tippe {talk}, um Jarvis zu öffnen oder zu schließen · halten zum Sprechen", tapOff: "Halte {talk}, um mit Jarvis zu sprechen", last: "Letzte Antwort" },
  pt: { tap: "Toque em {talk} para abrir ou fechar o Jarvis · segure para falar", tapOff: "Segure {talk} para falar com o Jarvis", last: "Última resposta" },
  uk: { tap: "Торкніться {talk}, щоб відкрити або закрити Jarvis · утримуйте, щоб говорити", tapOff: "Утримуйте {talk}, щоб говорити з Jarvis", last: "Остання відповідь" },
  ru: { tap: "Нажмите {talk}, чтобы открыть или закрыть Jarvis · удерживайте, чтобы говорить", tapOff: "Удерживайте {talk}, чтобы говорить с Jarvis", last: "Последний ответ" },
  zh: { tap: "轻按 {talk} 打开或关闭 Jarvis · 按住可对话", tapOff: "按住 {talk} 与 Jarvis 对话", last: "最新回答" },
  ja: { tap: "{talk} を短く押すと Jarvis を開閉 · 長押しで話す", tapOff: "{talk} を長押しして Jarvis と話す", last: "最新の回答" },
  ko: { tap: "{talk}을(를) 짧게 눌러 Jarvis 열기/닫기 · 누르고 있으면 대화", tapOff: "{talk}을(를) 누르고 있으면 Jarvis와 대화", last: "마지막 답변" },
  hi: { tap: "Jarvis खोलने या बंद करने के लिए {talk} टैप करें · बात करने के लिए दबाए रखें", tapOff: "Jarvis से बात करने के लिए {talk} दबाए रखें", last: "आख़िरी जवाब" },
};

export const jarvisTabLanguages = Object.keys(copy);

export function jarvisTabCopy(locale: string): Copy {
  const language = String(locale || "en").toLowerCase().split(/[-_]/)[0];
  return copy[language === "ua" ? "uk" : language] ?? en;
}

// The tab's hold-to-talk line, from the agent's state (combos.talk, tapOpens). Empty when talking has no combo.
export function jarvisTapLine(state: { combos?: { talk?: unknown }; tapOpens?: unknown } | null | undefined, locale: string): string {
  const talk = typeof state?.combos?.talk === "string" && /^[A-Z0-9+]{2,40}$/.test(state.combos.talk) ? state.combos.talk : "";
  if (!talk) return "";
  const text = jarvisTabCopy(locale);
  return (state?.tapOpens === false ? text.tapOff : text.tap).replace("{talk}", talk);
}
