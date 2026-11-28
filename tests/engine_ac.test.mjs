// Assetto Corsa: os pontos do triple_screen.ini reproduzem a posição física de cada píxel visível.
// O AC divide a imagem em três terços iguais; com o Surround cada terço cobre alguns píxeis escondidos.
import { test } from "node:test";
import assert from "node:assert/strict";
import * as E from "../app/engine.js";
import { zukaRig } from "./rigs.mjs";

function worstMm(rig) {
  const p = E.acPlan(rig), th = p.width / 3;
  const pts = [...p.text.matchAll(/_POINT=(-?[\d.]+),(-?[\d.]+)/g)].map((m) => [+m[1] * 1000, +m[2] * 1000]);
  let worst = 0;
  for (const m of rig.monitors) {
    const [dx, , rx] = m.desktop_px;
    for (let k = 0; k < rx; k += 5) {
      const g = dx + k + 0.5, i = Math.min(2, Math.floor(g / th)), t = (g - i * th) / th, a = pts[2 * i], b = pts[2 * i + 1];
      const ph = E.pointOn(m, (k + 0.5) / rx, 0.5);
      worst = Math.max(worst, Math.hypot(a[0] + (b[0] - a[0]) * t - ph[0], a[1] + (b[1] - a[1]) * t + ph[2]));
    }
  }
  return worst;
}

test("AC: sem Surround (0 px escondidos) os pontos batem certo", () => assert.ok(worstMm(zukaRig(56, { gap: 0 })) < 0.01));
test("AC: Surround com 39 px por junção", () => { const r = zukaRig(56); assert.deepEqual(E.acPlan(r).problems, []); assert.ok(worstMm(r) < 0.01); });
test("AC: Surround com 55 px por junção (rig atual)", () => { const r = zukaRig(55.3, { gap: 17.1 }); assert.equal(E.acPlan(r).width, 5870); assert.ok(worstMm(r) < 0.01); });
test("AC: ângulos extremos e distâncias curtas", () => { for (const a of [0, 30, 70, 90]) for (const d of [300, 710, 1200]) assert.ok(worstMm(zukaRig(a, { dist: d })) < 0.01, `${a}° ${d} mm`); });
test("AC: unidades do ficheiro (metros, y para a frente)", () => {
  const t = E.acIni(zukaRig(56, { gap: 0 }));
  const c = /\[CENTER_SCREEN\]\nLEFT_POINT=(-?[\d.]+),([\d.]+)\nRIGHT_POINT=(-?[\d.]+),([\d.]+)/.exec(t);
  assert.equal(+c[2], 0.71); assert.equal(+c[4], 0.71); assert.ok(Math.abs(+c[3] - +c[1] - 0.593) < 0.001);
});
test("AC: resoluções diferentes são recusadas (o AC só faz terços iguais)", () => {
  const C = { name: "center", res_px: [2560, 1440], width_mm: 596.7, height_mm: 335.6 };
  const S = (n) => ({ name: n, res_px: [1920, 1080], width_mm: 597.7, height_mm: 336.2, hinge_deg: 56, gap_mm: 12 });
  assert.ok(E.acPlan(E.buildRow(C, [S("left")], [S("right")], 710)).problems.length > 0);
});
test("AC: ângulos diferentes assinalados (os SPINNERS só têm um ângulo)", () => {
  const S = (n, a) => ({ name: n, res_px: [1920, 1080], width_mm: 597.7, height_mm: 336.2, hinge_deg: a, gap_mm: 12 });
  const C = { name: "center", res_px: [1920, 1080], width_mm: 597.7, height_mm: 336.2 };
  const p = E.acPlan(E.buildRow(C, [S("left", 40)], [S("right", 60)], 710));
  assert.equal(p.asymmetric, true); assert.ok(worstMm(E.buildRow(C, [S("left", 40)], [S("right", 60)], 710)) < 0.01);
});
test("AC: margem 0 com o Surround a esconder as molduras (validado no rig); folga física sem ele", () => {
  assert.match(E.acIni(zukaRig(55.3, { gap: 17.1 })), /SP_MARGINS=0\n/);
  const r = zukaRig(56, { gap: 12.1 }); for (const m of r.monitors) m.desktop_px[0] = { left: 0, center: 1920, right: 3840 }[m.name];
  assert.match(E.acIni(r), /SP_MARGINS=12\n/);
});

// Le Mans Ultimate: o ajuste ao modelo do jogo (três terços, planos articulados) deixa cada píxel visível na direção real.
test("LMU: erro angular dos píxeis visíveis abaixo de 0,15° (Surround 39 e 55 px, sem Surround)", () => {
  for (const r of [zukaRig(56), zukaRig(55.3, { gap: 17.1 }), zukaRig(56, { gap: 0 }), zukaRig(30, { dist: 900 }), zukaRig(70, { dist: 500 })]) {
    const p = E.lmuPlan(r); assert.deepEqual(p.problems, []); assert.ok(p.errDeg < 0.15, `erro ${p.errDeg}`);
    for (const t of [p.view, p.left, p.right]) assert.match(t, /^-?\d\.\d{3}, \d\.\d{3}, \d\.\d{3}, \d{1,2}\.\d{3}, -?\d\.\d{3}$/);
  }
});
test("LMU: ângulos diferentes vão para LeftView e RightView", () => {
  const S = (n, a) => ({ name: n, res_px: [1920, 1080], width_mm: 597.7, height_mm: 336.2, hinge_deg: a, gap_mm: 12 });
  const p = E.lmuPlan(E.buildRow({ name: "center", res_px: [1920, 1080], width_mm: 597.7, height_mm: 336.2 }, [S("left", 40)], [S("right", 60)], 710));
  assert.match(p.left, /, 40\.000, /); assert.match(p.right, /, 60\.000, /); assert.equal(p.asymmetric, true); assert.ok(p.errDeg < 0.2);
});
