// The real src/corePalette.ts, compiled for tests that load a component in a vm with a hand-made
// require(): map "./corePalette" (or "../corePalette") to this.
import { readFileSync } from "node:fs";
import vm from "node:vm";
import ts from "typescript";

export const corePalette = {};
vm.runInNewContext(ts.transpileModule(readFileSync(new URL("../src/corePalette.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText, { exports: corePalette });
export const isCorePalette = name => name === "./corePalette" || name === "../corePalette";
