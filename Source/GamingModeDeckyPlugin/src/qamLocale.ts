import { normalizeStoreLocale, type StoreLocale } from "./pluginStoreLocale";

const labels: Record<StoreLocale, string> = {
  en: "Show Core in quick-access tabs",
  it: "Mostra Core nelle tab di accesso rapido",
  es: "Mostrar Core en las pestañas de acceso rápido",
  fr: "Afficher Core dans les onglets d’accès rapide",
  de: "Core in den Schnellzugriff-Tabs anzeigen",
  pt: "Mostrar Core nas abas de acesso rápido",
  uk: "Показувати Core у вкладках швидкого доступу",
  zh: "在快速访问标签页中显示 Core",
  ja: "クイックアクセスタブに Core を表示",
  ko: "빠른 액세스 탭에 Core 표시",
  hi: "त्वरित ऐक्सेस टैब में Core दिखाएँ",
  ru: "Показывать Core во вкладках быстрого доступа",
};
export function getPlayhubQamLabel(locale: unknown): string {
  return labels[normalizeStoreLocale(locale)];
}
const descriptions: Record<StoreLocale, string> = {
  en: "Shows a dedicated Core tab in the quick-access menu. If you have installed the Shortcuts plugin, this preference stays synchronized while it is active.",
  it: "Mostra una tab dedicata a Core nel menu di accesso rapido. Se hai installato il plugin Shortcuts, questa preferenza viene sincronizzata quando è attivo.",
  es: "Muestra una pestaña dedicada a Core en el menú de acceso rápido. Si has instalado el plugin Shortcuts, esta preferencia se sincroniza mientras está activo.",
  fr: "Affiche un onglet dédié à Core dans le menu d'accès rapide. Si vous avez installé le plugin Shortcuts, cette préférence est synchronisée lorsqu'il est actif.",
  de: "Zeigt einen eigenen Core-Tab im Schnellzugriffsmenü. Wenn du das Shortcuts-Plugin installiert hast, wird diese Einstellung synchronisiert, solange es aktiv ist.",
  pt: "Mostra uma aba dedicada ao Core no menu de acesso rápido. Se você instalou o plugin Shortcuts, esta preferência é sincronizada enquanto ele está ativo.",
  uk: "Показує окрему вкладку Core у меню швидкого доступу. Якщо ви встановили плагін Shortcuts, це налаштування синхронізується, доки він активний.",
  zh: "在快速访问菜单中显示专用的 Core 标签页。如果你已安装 Shortcuts 插件，启用该插件时会同步此偏好设置。",
  ja: "クイックアクセスメニューにCore専用のタブを表示します。Shortcutsプラグインをインストールしている場合、有効にしている間はこの設定が同期されます。",
  ko: "빠른 액세스 메뉴에 Core 전용 탭을 표시합니다. Shortcuts 플러그인을 설치했다면 활성화되어 있는 동안 이 설정이 동기화됩니다.",
  hi: "त्वरित ऐक्सेस मेन्यू में Core का अलग टैब दिखाता है। यदि आपने Shortcuts प्लगइन इंस्टॉल किया है, तो उसके सक्रिय रहने पर यह प्राथमिकता सिंक होती है।",
  ru: "Показывает отдельную вкладку Core в меню быстрого доступа. Если у вас установлен плагин Shortcuts, эта настройка синхронизируется, пока он активен.",
};
export function getPlayhubQamDescription(locale: unknown): string {
  return descriptions[normalizeStoreLocale(locale)];
}

const discover: Record<StoreLocale, [string, string]> = {
  en: ["Discover new plugins", "Explore plugins from Core, the Decky Store and independent projects on GitHub. New possibilities, all in one place."],
  it: ["Scopri nuovi plugin", "Esplora i plugin di Playhub, del Decky Store e dei progetti indipendenti su GitHub. Nuove possibilità, tutte in un unico posto."],
  es: ["Descubre nuevos plugins", "Explora plugins de Playhub, Decky Store y proyectos independientes en GitHub. Nuevas posibilidades, todo en un solo lugar."],
  fr: ["Découvrez de nouveaux plugins", "Explorez les plugins de Playhub, du Decky Store et de projets indépendants sur GitHub. De nouvelles possibilités, réunies au même endroit."],
  de: ["Neue Plugins entdecken", "Entdecke Plugins von Playhub, aus dem Decky Store und von unabhängigen Projekten auf GitHub. Neue Möglichkeiten, alles an einem Ort."],
  pt: ["Descubra novos plugins", "Explore plugins do Playhub, da Decky Store e de projetos independentes no GitHub. Novas possibilidades, tudo em um só lugar."],
  uk: ["Відкрийте нові плагіни", "Знайдіть плагіни Playhub, Decky Store та незалежних проєктів на GitHub. Нові можливості — усе в одному місці."],
  zh: ["发现新插件", "探索来自 Core、Decky Store 和 GitHub 独立项目的插件。在这里，发现更多可能。"],
  ja: ["新しいプラグインを見つけよう", "Core、Decky Store、GitHub の個人・独立開発プロジェクトからプラグインを探そう。新たな可能性が、ここに集まります。"],
  ko: ["새 플러그인 둘러보기", "Core, Decky Store, GitHub의 독립 프로젝트에서 제공하는 플러그인을 만나 보세요. 새로운 가능성이 한곳에 모여 있습니다."],
  hi: ["नए प्लगइन खोजें", "Core, Decky Store और GitHub के स्वतंत्र प्रोजेक्ट के प्लगइन खोजें। नई संभावनाएँ, सब एक ही जगह।"],
  ru: ["Откройте новые плагины", "Находите плагины Playhub, Decky Store и независимых проектов на GitHub. Новые возможности — всё в одном месте."],
};
export function getPlayhubDiscoverCopy(locale: unknown): [string, string] {
  return discover[normalizeStoreLocale(locale)];
}

const modeQuestions: Record<StoreLocale, [string, string]> = {
  en: ["Switch to Gaming Mode?", "Switch to Desktop Mode?"],
  it: ["Vuoi passare alla Gaming Mode?", "Vuoi passare alla Desktop Mode?"],
  es: ["¿Quieres pasar al modo de juego?", "¿Quieres pasar al modo de escritorio?"],
  fr: ["Voulez-vous passer en mode jeu ?", "Voulez-vous passer en mode bureau ?"],
  de: ["In den Gaming-Modus wechseln?", "In den Desktop-Modus wechseln?"],
  pt: ["Deseja mudar para o modo de jogo?", "Deseja mudar para o modo desktop?"],
  uk: ["Перейти в ігровий режим?", "Перейти в режим робочого столу?"],
  zh: ["要切换到游戏模式吗？", "要切换到桌面模式吗？"],
  ja: ["ゲーミングモードに切り替えますか？", "デスクトップモードに切り替えますか？"],
  ko: ["게이밍 모드로 전환할까요?", "데스크톱 모드로 전환할까요?"],
  hi: ["गेमिंग मोड में जाएँ?", "डेस्कटॉप मोड में जाएँ?"],
  ru: ["Перейти в игровой режим?", "Перейти в режим рабочего стола?"],
};
export function getModeQuestion(locale: unknown, mode: "gaming" | "desktop"): string {
  return modeQuestions[normalizeStoreLocale(locale)][mode === "gaming" ? 0 : 1];
}
