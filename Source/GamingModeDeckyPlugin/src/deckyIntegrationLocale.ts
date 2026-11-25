const rows: Record<string, [string, string]> = {
  en: ["Hide Decky from Steam tabs", "Decky always remains available in the Core menu."],
  it: ["Nascondi Decky dalle tab di Steam", "Decky rimane sempre disponibile nel menu di Core."],
  de: ["Decky in den Steam-Tabs ausblenden", "Decky bleibt im Core-Menü immer verfügbar."],
  es: ["Ocultar Decky de las pestañas de Steam", "Decky sigue siempre disponible en el menú de Core."],
  fr: ["Masquer Decky dans les onglets Steam", "Decky reste toujours disponible dans le menu Core."],
  pt: ["Ocultar Decky das abas do Steam", "Decky permanece sempre disponível no menu do Core."],
  ru: ["Скрыть Decky из вкладок Steam", "Decky всегда остаётся доступным в меню Core."],
  uk: ["Приховати Decky з вкладок Steam", "Decky завжди залишається доступним у меню Core."],
  zh: ["在 Steam 标签页中隐藏 Decky", "Decky 始终可在 Core 菜单中使用。"],
  ja: ["SteamのタブからDeckyを非表示にする", "Deckyは引き続きCoreメニューでいつでも利用できます。"],
  ko: ["Steam 탭에서 Decky 숨기기", "Decky는 Core 메뉴에서 항상 사용할 수 있습니다."],
  hi: ["Steam टैब से Decky छिपाएँ", "Decky हमेशा Core मेनू में उपलब्ध रहता है।"],
};
export function deckyIntegrationLocale(locale: string) {
  const row = rows[locale.split(/[-_]/)[0]] ?? rows.en;
  return { label: row[0], description: row[1] };
}
