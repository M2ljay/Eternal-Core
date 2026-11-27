// La versione 1.0.2 di @decky/rollup esporta la funzione solo come default;
// versioni piu' recenti la espongono anche col nome. Accettiamo entrambe le
// forme, cosi' un aggiornamento del pacchetto non ferma la build.
import * as deckyRollup from "@decky/rollup";
import { existsSync, readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const deckyPlugin = deckyRollup.deckyPlugin ?? deckyRollup.default;

const config = deckyPlugin({});
config.plugins.unshift({
  name: "editorial-webp",
  load(id) {
    if (id.endsWith(".webp")) {
      return `export default ${JSON.stringify("data:image/webp;base64," + readFileSync(id).toString("base64"))};`;
    }
  },
});
// Eternal Core: Core Home's optional Club tab (docs/eternal/CORE-UI.md, "Home tabs"). src/club/ is built by a
// separate module; when its two files are present the virtual module re-exports them, otherwise it is null, so the
// build never depends on them.
const clubFiles = ["ClubTab.tsx", "clubAvailable.ts"].map((name) => fileURLToPath(new URL(`./src/club/${name}`, import.meta.url)));
config.plugins.unshift({
  name: "core-optional-club",
  resolveId(id) { return id === "core-optional:club" ? "\0core-optional:club" : null; },
  load(id) {
    if (id !== "\0core-optional:club") return null;
    if (!clubFiles.every((file) => existsSync(file))) return "export default null;";
    return `import { ClubTab } from ${JSON.stringify(clubFiles[0])};\nimport { clubAvailable } from ${JSON.stringify(clubFiles[1])};\nexport default { ClubTab, clubAvailable };`;
  },
});
export default config;
