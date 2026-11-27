// Ponte verso le librerie che Decky mette a disposizione a tempo di
// esecuzione. Stessa forma usata da Launch Curtain: si importa da qui, non
// direttamente dai pacchetti, cosi' il punto di aggancio e' uno solo.
import { routerHook, definePlugin, toaster } from "@decky/api";
import * as DeckyUI from "@decky/ui";
import * as SP_REACT from "react";
import * as SP_JSX from "react/jsx-runtime";
import { withModuleFallbacks } from "./steamModules";

// Eternal Core: the same library, with module lookups that also reach the ES-module Steam
// client when the running loader is Decky 4 (steamModules.ts). On Decky 3 it behaves as before.
// Not named DFL here: the bundle maps @decky/ui to the global DFL, and a local of that name
// would shadow it.
const CompatUI = withModuleFallbacks(DeckyUI);

export { SP_REACT, SP_JSX, CompatUI as DFL, routerHook, definePlugin, toaster };
