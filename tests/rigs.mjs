// Rig de referência (só para reproduzir resultados conhecidos — nunca predefinição da app).
import * as E from "../app/engine.js";
export function zukaRig(angle, { gap = 12.1, dist = 710 } = {}) {
  const C = { name: "center", res_px: [1920, 1080], width_mm: 596.9, height_mm: 336.3, curve_radius_mm: 1500 };
  const side = (n) => ({ name: n, res_px: [1920, 1080], width_mm: 597.7, height_mm: 336.2, hinge_deg: angle, gap_mm: gap });
  const L = side("left"), R = side("right");
  for (const s of [L, R]) s.desktop_gap_px = Math.round(gap * 1920 / E.specSize(s)[0]);
  return E.buildRow(C, [L], [R], dist, 0, 0);
}
