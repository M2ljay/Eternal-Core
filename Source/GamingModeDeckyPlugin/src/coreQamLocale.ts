// Eternal Core 3.3.11: the words of Quick Access > Core as one list (ControlCenter.tsx). Other languages fall back to
// English.

export type CoreQamCopy = {
  quick: string; alwaysOpen: string; display: string; sound: string; performance: string; power: string;
  sleep: string; restart: string; shutDown: string; displayGraphics: string; controller: string; downloads: string;
  look: string; system: string; stores: string; shortcuts: string;
};

const en: CoreQamCopy = {
  quick: "Quick", alwaysOpen: "always open", display: "Display", sound: "Sound", performance: "Performance", power: "Power",
  sleep: "Sleep", restart: "Restart", shutDown: "Shut down", displayGraphics: "Display & graphics", controller: "Controller",
  downloads: "Downloads", look: "Look", system: "System", stores: "Stores", shortcuts: "Shortcuts",
};

const copies: Record<string, Partial<CoreQamCopy>> = {
  it: { quick: "Rapide", alwaysOpen: "sempre aperto", display: "Schermo", sound: "Audio", performance: "Prestazioni", power: "Alimentazione",
    sleep: "Sospendi", restart: "Riavvia", shutDown: "Spegni", displayGraphics: "Schermo e grafica", controller: "Controller",
    downloads: "Download", look: "Aspetto", system: "Sistema", stores: "Store", shortcuts: "Scorciatoie" },
  es: { quick: "Rápido", alwaysOpen: "siempre abierto", display: "Pantalla", sound: "Sonido", performance: "Rendimiento", power: "Energía",
    sleep: "Suspender", restart: "Reiniciar", shutDown: "Apagar", displayGraphics: "Pantalla y gráficos", controller: "Mando",
    downloads: "Descargas", look: "Aspecto", system: "Sistema", stores: "Tiendas", shortcuts: "Atajos" },
  fr: { quick: "Rapide", alwaysOpen: "toujours ouvert", display: "Affichage", sound: "Son", performance: "Performances", power: "Alimentation",
    sleep: "Veille", restart: "Redémarrer", shutDown: "Éteindre", displayGraphics: "Affichage et graphismes", controller: "Manette",
    downloads: "Téléchargements", look: "Apparence", system: "Système", stores: "Boutiques", shortcuts: "Raccourcis" },
  de: { quick: "Schnell", alwaysOpen: "immer offen", display: "Anzeige", sound: "Ton", performance: "Leistung", power: "Energie",
    sleep: "Ruhezustand", restart: "Neu starten", shutDown: "Herunterfahren", displayGraphics: "Anzeige und Grafik", controller: "Controller",
    downloads: "Downloads", look: "Aussehen", system: "System", stores: "Stores", shortcuts: "Kurzbefehle" },
  pt: { quick: "Rápido", alwaysOpen: "sempre aberto", display: "Tela", sound: "Som", performance: "Desempenho", power: "Energia",
    sleep: "Suspender", restart: "Reiniciar", shutDown: "Desligar", displayGraphics: "Tela e gráficos", controller: "Controle",
    downloads: "Downloads", look: "Aparência", system: "Sistema", stores: "Lojas", shortcuts: "Atalhos" },
};

export function coreQamCopy(locale: string): CoreQamCopy {
  const key = String(locale ?? "").toLowerCase().slice(0, 2);
  return { ...en, ...(copies[key] ?? {}) };
}
