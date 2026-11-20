// Todas as frases da interface existem nas 4 línguas (en, pt-BR, es, fr) e mantêm os mesmos marcadores {0}.
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { LANGS, ROWS } from "../app/i18n.js";
const html = fs.readFileSync(path.join(path.dirname(fileURLToPath(import.meta.url)), "..", "app", "index.html"), "utf8");

test("Mesmas línguas do FS25 RealFFB", () => assert.deepEqual(LANGS.map((l) => l.name), ["English", "Português (Brasil)", "Español", "Français"]));

test("Cada frase da interface tem tradução", () => {
  const keys = new Set(ROWS.map((r) => r[0])), missing = [];
  const add = (k) => { if (!keys.has(k)) missing.push(k); };
  for (const m of html.matchAll(/data-t="([^"]*)"/g)) add(m[1].replace(/&quot;/g, '"'));
  for (const m of html.matchAll(/\b[TF]\("((?:[^"\\]|\\.)*)"/g)) add(JSON.parse(`"${m[1]}"`));
  assert.deepEqual(missing, []);
});

test("Traduções completas e com os mesmos marcadores", () => {
  for (const r of ROWS) {
    assert.equal(r.length, 4, r[0]);
    const marks = (s) => (s.match(/\{\d\}/g) || []).sort().join();
    for (const t of r.slice(1)) { assert.ok(t && t.trim(), r[0]); assert.equal(marks(t), marks(r[0]), `${r[0]} → ${t}`); }
  }
});
