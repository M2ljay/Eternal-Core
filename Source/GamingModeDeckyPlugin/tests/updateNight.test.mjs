import { test } from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import ts from 'typescript';

// The card's time: the agent's scheduled time, or the next 5:00 when only offered.
const source = fs.readFileSync(new URL('../src/UpdateNightCard.tsx', import.meta.url), 'utf8');
const fn = source.slice(source.indexOf('export function nightTime'), source.indexOf('export function UpdateNightCard'));
const js = ts.transpileModule(fn, { compilerOptions: { target: ts.ScriptTarget.ES2022, module: ts.ModuleKind.ESNext } }).outputText;
const nightTime = new Function(js.replace('export function', 'return function'))();

test('offered at 1 a.m.: tonight means 5:00 today', () => {
  const at = nightTime('en-US', { offer: true, hour: 5 }, new Date(2026, 10, 26, 1, 0));
  assert.match(at, /^5:00/);
});
test('scheduled: shows the agent\'s time', () => {
  const when = new Date(2026, 10, 27, 5, 0).toISOString();
  assert.match(nightTime('en-US', { scheduledFor: when }), /^5:00/);
});
