// Eternal Core: the Core UI setting's text in Core's 12 languages (Quick Access > Core). "Core UI" is the
// feature's name and stays the same in every language. Self-contained: tests/coreUi.test.mjs loads it alone.

type Copy = { section: string; label: string; hint: string; screensaver: string; screensaverHint: string };

const en: Copy = {
  section: "Look", label: "Core UI",
  hint: "Core's black, gunmetal and crimson look, with the Starfield, on the Steam menu, Quick Access and the top and bottom bars. Off gives them back to Steam.",
  screensaver: "Use Core Starfield as screensaver", screensaverHint: "Steam's screensaver becomes the Core Starfield. Off goes back to Steam's slideshow.",
};

const copy: Record<string, Copy> = {
  en,
  it: { section: "Aspetto", label: "Core UI", hint: "L'aspetto di Core in nero, canna di fucile e cremisi, con il campo stellare, sul menu di Steam, sull'Accesso rapido e sulle barre in alto e in basso. Spento li restituisce a Steam." , screensaver: "Usa Core Starfield come salvaschermo", screensaverHint: "Il salvaschermo di Steam diventa il campo stellare di Core. Spento torna alla presentazione di Steam." },
  es: { section: "Aspecto", label: "Core UI", hint: "El aspecto de Core en negro, gris metal y carmesí, con el campo de estrellas, en el menú de Steam, el Acceso rápido y las barras superior e inferior. Desactivado los devuelve a Steam." , screensaver: "Usar Core Starfield como salvapantallas", screensaverHint: "El salvapantallas de Steam pasa a ser el campo de estrellas de Core. Desactivado vuelve a la presentación de Steam." },
  fr: { section: "Apparence", label: "Core UI", hint: "L'apparence de Core en noir, gris acier et cramoisi, avec le champ d'étoiles, sur le menu Steam, l'Accès rapide et les barres du haut et du bas. Désactivé, Steam reprend la sienne." , screensaver: "Utiliser Core Starfield comme économiseur d'écran", screensaverHint: "L'économiseur d'écran de Steam devient le champ d'étoiles de Core. Désactivé, Steam reprend son diaporama." },
  de: { section: "Aussehen", label: "Core UI", hint: "Der Look von Core in Schwarz, Gunmetal und Karmesin mit dem Sternenfeld im Steam-Menü, im Schnellzugriff und in der oberen und unteren Leiste. Aus gibt sie an Steam zurück." , screensaver: "Core Starfield als Bildschirmschoner", screensaverHint: "Steams Bildschirmschoner wird das Sternenfeld von Core. Aus kehrt zu Steams Diashow zurück." },
  pt: { section: "Aparência", label: "Core UI", hint: "O visual do Core em preto, cinza-chumbo e carmesim, com o campo estelar, no menu Steam, no Acesso rápido e nas barras superior e inferior. Desligado devolve tudo ao Steam." , screensaver: "Usar Core Starfield como protetor de tela", screensaverHint: "O protetor de tela do Steam passa a ser o campo estelar do Core. Desligado volta à apresentação do Steam." },
  uk: { section: "Вигляд", label: "Core UI", hint: "Вигляд Core у чорному, сталевому й багряному кольорах із зоряним полем у меню Steam, швидкому доступі та на верхній і нижній панелях. Вимкнення повертає вигляд Steam." , screensaver: "Core Starfield як заставка", screensaverHint: "Заставкою Steam стає зоряне поле Core. Вимкнення повертає слайд-шоу Steam." },
  ru: { section: "Внешний вид", label: "Core UI", hint: "Оформление Core в чёрном, стальном и багровом цветах со звёздным полем в меню Steam, быстром доступе и на верхней и нижней панелях. Если выключить, вернётся оформление Steam." , screensaver: "Core Starfield как заставка", screensaverHint: "Заставкой Steam станет звёздное поле Core. Если выключить, вернётся слайд-шоу Steam." },
  zh: { section: "外观", label: "Core UI", hint: "在 Steam 菜单、快速访问以及顶部和底部栏使用 Core 的黑色、炮铜色和深红色外观，并显示星空。关闭后恢复 Steam 的外观。" , screensaver: "使用 Core Starfield 作为屏幕保护程序", screensaverHint: "Steam 的屏幕保护程序改为 Core 星空。关闭后恢复 Steam 的幻灯片。" },
  ja: { section: "外観", label: "Core UI", hint: "Steam メニュー、クイックアクセス、上下のバーに、星空と黒・ガンメタル・クリムゾンの Core の外観を使います。オフにすると Steam の外観に戻ります。" , screensaver: "Core Starfield をスクリーンセーバーにする", screensaverHint: "Steam のスクリーンセーバーを Core の星空にします。オフにすると Steam のスライドショーに戻ります。" },
  ko: { section: "모양", label: "Core UI", hint: "Steam 메뉴, 빠른 실행, 위아래 막대에 별빛 배경과 검정·건메탈·진홍색의 Core 모양을 적용합니다. 끄면 Steam 모양으로 돌아갑니다." , screensaver: "Core Starfield를 화면 보호기로 사용", screensaverHint: "Steam 화면 보호기가 Core 별빛으로 바뀝니다. 끄면 Steam 슬라이드쇼로 돌아갑니다." },
  hi: { section: "रूप", label: "Core UI", hint: "Steam मेनू, क्विक एक्सेस और ऊपर-नीचे की पट्टियों पर तारों भरी पृष्ठभूमि के साथ Core का काला, गनमेटल और क्रिमसन रूप। बंद करने पर Steam का रूप लौट आता है।" , screensaver: "Core Starfield को स्क्रीनसेवर बनाएँ", screensaverHint: "Steam का स्क्रीनसेवर Core का तारामंडल बन जाता है। बंद करने पर Steam का स्लाइडशो लौट आता है।" },
};

export const coreUiLanguages = Object.keys(copy);

export function coreUiCopy(locale: string): Copy {
  const language = String(locale || "en").toLowerCase().split(/[-_]/)[0];
  return copy[language === "ua" ? "uk" : language] ?? en;
}
