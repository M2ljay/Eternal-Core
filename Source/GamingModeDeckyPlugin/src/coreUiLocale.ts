// Eternal Core: the Core UI setting's text in Core's 12 languages (Quick Access > Core). "Core UI" is the
// feature's name and stays the same in every language. Self-contained: tests/coreUi.test.mjs loads it alone.

type Copy = { section: string; label: string; hint: string };

const en: Copy = {
  section: "Look", label: "Core UI",
  hint: "Core's black, gunmetal and crimson look, with the Starfield, on the Steam menu, Quick Access and the top and bottom bars. Off gives them back to Steam.",
};

const copy: Record<string, Copy> = {
  en,
  it: { section: "Aspetto", label: "Core UI", hint: "L'aspetto di Core in nero, canna di fucile e cremisi, con il campo stellare, sul menu di Steam, sull'Accesso rapido e sulle barre in alto e in basso. Spento li restituisce a Steam." },
  es: { section: "Aspecto", label: "Core UI", hint: "El aspecto de Core en negro, gris metal y carmesí, con el campo de estrellas, en el menú de Steam, el Acceso rápido y las barras superior e inferior. Desactivado los devuelve a Steam." },
  fr: { section: "Apparence", label: "Core UI", hint: "L'apparence de Core en noir, gris acier et cramoisi, avec le champ d'étoiles, sur le menu Steam, l'Accès rapide et les barres du haut et du bas. Désactivé, Steam reprend la sienne." },
  de: { section: "Aussehen", label: "Core UI", hint: "Der Look von Core in Schwarz, Gunmetal und Karmesin mit dem Sternenfeld im Steam-Menü, im Schnellzugriff und in der oberen und unteren Leiste. Aus gibt sie an Steam zurück." },
  pt: { section: "Aparência", label: "Core UI", hint: "O visual do Core em preto, cinza-chumbo e carmesim, com o campo estelar, no menu Steam, no Acesso rápido e nas barras superior e inferior. Desligado devolve tudo ao Steam." },
  uk: { section: "Вигляд", label: "Core UI", hint: "Вигляд Core у чорному, сталевому й багряному кольорах із зоряним полем у меню Steam, швидкому доступі та на верхній і нижній панелях. Вимкнення повертає вигляд Steam." },
  ru: { section: "Внешний вид", label: "Core UI", hint: "Оформление Core в чёрном, стальном и багровом цветах со звёздным полем в меню Steam, быстром доступе и на верхней и нижней панелях. Если выключить, вернётся оформление Steam." },
  zh: { section: "外观", label: "Core UI", hint: "在 Steam 菜单、快速访问以及顶部和底部栏使用 Core 的黑色、炮铜色和深红色外观，并显示星空。关闭后恢复 Steam 的外观。" },
  ja: { section: "外観", label: "Core UI", hint: "Steam メニュー、クイックアクセス、上下のバーに、星空と黒・ガンメタル・クリムゾンの Core の外観を使います。オフにすると Steam の外観に戻ります。" },
  ko: { section: "모양", label: "Core UI", hint: "Steam 메뉴, 빠른 실행, 위아래 막대에 별빛 배경과 검정·건메탈·진홍색의 Core 모양을 적용합니다. 끄면 Steam 모양으로 돌아갑니다." },
  hi: { section: "रूप", label: "Core UI", hint: "Steam मेनू, क्विक एक्सेस और ऊपर-नीचे की पट्टियों पर तारों भरी पृष्ठभूमि के साथ Core का काला, गनमेटल और क्रिमसन रूप। बंद करने पर Steam का रूप लौट आता है।" },
};

export const coreUiLanguages = Object.keys(copy);

export function coreUiCopy(locale: string): Copy {
  const language = String(locale || "en").toLowerCase().split(/[-_]/)[0];
  return copy[language === "ua" ? "uk" : language] ?? en;
}
