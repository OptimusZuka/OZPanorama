// B3: o erro do MSFS tem de ser o da configuração escrita. Executar: node --test tests/
import test from "node:test";
import assert from "node:assert/strict";
import * as E from "../app/engine.js";
import { zukaRig } from "./rigs.mjs";
const rad = (d) => (d * Math.PI) / 180;

// Verificação independente: reconstrói a guinada real a partir das LINHAS escritas no UserCfg.opt.
function errorFromWrittenLines(rig, cfg) {
  const val = {};
  for (const l of cfg.lines) { const m = l.match(/^WindowCustom(\d)\.CameraDegRotationAdd \S+ (\S+) /); if (m) val[m[1]] = Number(m[2]); }
  const left = Math.abs(val[0]), right = Math.abs(val[1]);
  const tv = Math.tan(rad(cfg.vfov / 2));
  let max = 0;
  for (const m of rig.monitors) {
    const f = E.frustum(m), th = tv * m.res_px[0] / m.res_px[1];
    const real = m.name === "center" ? 0 : (m.name === "left" ? left : right) * cfg.hfov / 90;   // medido: real = valor × hFOV / 90
    max = Math.max(max, E.cameraError(m, { yaw: Math.sign(f.yaw_deg) * real, pitch: f.pitch_deg, roll: f.roll_deg, l: -th, r: th, b: -tv, t: tv }));
  }
  return max;
}

for (const angle of [30, 45, 56, 60]) for (const hideBezel of [true, false]) {
  test(`MSFS ${angle}° ${hideBezel ? "moldura" : "colada"}: erro mostrado = erro do ficheiro escrito`, () => {
    const rig = zukaRig(angle), cfg = E.msfs2024Config(rig, { hideBezel });
    assert.ok(Math.abs(cfg.max - errorFromWrittenLines(rig, cfg)) < 1e-9, `${cfg.max} vs ${errorFromWrittenLines(rig, cfg)}`);
  });
}

test("MSFS: com FOV dado pelo utilizador, o erro continua a ser o do ficheiro", () => {
  const rig = zukaRig(56);
  for (const hfov of [40, 47.81, 60]) {
    const cfg = E.msfs2024Config(rig, { hfov });
    assert.ok(Math.abs(cfg.max - errorFromWrittenLines(rig, cfg)) < 1e-9);
    assert.equal(cfg.auto, false);
  }
});

test("MSFS: o FOV otimizado é um mínimo (vizinhos não são melhores)", () => {
  for (const angle of [30, 56, 60]) {
    const rig = zukaRig(angle), cfg = E.msfs2024Config(rig);
    for (const d of [-1, -0.3, 0.3, 1]) {
      const n = E.msfs2024Config(rig, { hfov: cfg.hfov + d });
      if (n.outOfLimit) continue;   // vizinhos fora de 26–104° não são candidatos
      assert.ok(n.max >= cfg.max - 1e-6, `${angle}° d=${d}`);
    }
  }
});

test("MSFS 30°: o ótimo (24,9° vertical) está dentro do zoom medido no MSFS 2024 (18,1°–77°)", () => {
  const cfg = E.msfs2024Config(zukaRig(30));
  assert.ok(cfg.vfov < 26 && cfg.vfov >= 18.1, `vfov ${cfg.vfov}`);
  assert.equal(cfg.unconstrained, null);
  assert.ok(Math.abs(cfg.max - 2.0) < 0.05, `erro ${cfg.max}`);
});

test("MSFS: FOV manual fora do zoom medido é sinalizado, não escondido", () => {
  const rig = zukaRig(56);
  assert.equal(E.msfs2024Config(rig, { hfov: 25 }).outOfLimit, true);    // 25° h ≈ 14° v
  assert.equal(E.msfs2024Config(rig, { hfov: 40 }).outOfLimit, false);   // ≈ 23° v
  assert.equal(E.msfs2024Config(rig, { hfov: 25 }).unconstrained, null); // só quando o programa escolhe
});

test("MSFS 30°/45°/56°/60°: o limite não interfere quando o ótimo está dentro", () => {
  for (const a of [30, 45, 56, 60]) assert.equal(E.msfs2024Config(zukaRig(a)).unconstrained, null, `${a}°`);
});

test("MSFS: regra de moldura medida — valor = 90 × (hFOV + moldura) / hFOV", () => {
  const rig = zukaRig(56), cfg = E.msfs2024Config(rig, { hfov: 47.81 });
  assert.equal(cfg.rl, Math.round(90 * (47.81 + cfg.gapL) / 47.81 * 100) / 100);
  assert.equal(E.msfs2024Config(rig, { hfov: 47.81, hideBezel: false }).rr, 90);
});

test("MSFS: FOV impossível é recusado", () => {
  assert.throws(() => E.msfs2024Config(zukaRig(56), { hfov: 0 }));
  assert.throws(() => E.msfs2024Config(zukaRig(56), { hfov: 200 }));
});

test("MSFS 56° (rig de teste): pior configuração antiga reproduzida e melhorada", () => {
  // Valores reproduzidos a 15/11/2026: o código antigo mostrava 1,39° e escrevia uma configuração com 2,63°.
  const rig = zukaRig(56), old = E.msfs2024Config(rig, { hfov: 47.81 });
  assert.ok(Math.abs(old.max - 2.63) < 0.01, `antigo ${old.max}`);
  const best = E.msfs2024Config(rig);
  assert.ok(best.max < 1.75 + 1e-9, `novo ${best.max}`);   // auditoria: 1,75° com moldura
});
