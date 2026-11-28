// OZPanorama — motor geométrico em JavaScript (port do motor Python de referência).
// Mesmas convenções: mm, olho na origem, x direita, y cima, z para trás; câmara olha para -z.
// Validado contra tests/golden/*.json (ver web/test_engine.mjs).

const deg = (r) => (r * 180) / Math.PI;
const rad = (d) => (d * Math.PI) / 180;
const add = (a, b) => [a[0] + b[0], a[1] + b[1], a[2] + b[2]];
const sub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const mul = (a, k) => [a[0] * k, a[1] * k, a[2] * k];
const dot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const cross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const norm = (a) => Math.hypot(a[0], a[1], a[2]);
const unit = (a) => mul(a, 1 / norm(a));
const mat = (m, v) => [dot(m[0], v), dot(m[1], v), dot(m[2], v)];

function rotY(t) {
  const c = Math.cos(t), s = Math.sin(t);
  return [[c, 0, s], [0, 1, 0], [-s, 0, c]];
}
function rotAxis(axis, t) {
  const a = unit(axis), c = Math.cos(t), s = Math.sin(t), C = 1 - c;
  const [x, y, z] = a;
  return [
    [c + x * x * C, x * y * C - z * s, x * z * C + y * s],
    [y * x * C + z * s, c + y * y * C, y * z * C - x * s],
    [z * x * C - y * s, z * y * C + x * s, c + z * z * C],
  ];
}
const applyTilt = (right, up, tiltDeg) => (tiltDeg ? mat(rotAxis(right, -rad(tiltDeg)), up) : up);

// Monitor curvo: width_mm é o arco (largura ao longo do ecrã) e curve_radius_mm o raio (1500R → 1500).
// O motor usa o ecrã plano equivalente pela corda (erro: curvedFlatErrorDeg).
export function chordFromArc(arc, R) {
  const th = arc / R;
  return [2 * R * Math.sin(th / 2), R * (1 - Math.cos(th / 2))];
}

export function curvedFlatErrorDeg(arc, R, D, n = 101) {
  const [chord, sag] = chordFromArc(arc, R);
  let worst = 0;
  for (let i = 0; i < n; i++) {
    const s = arc * (i / (n - 1) - 0.5), x = R * Math.sin(s / R), z = R * (1 - Math.cos(s / R));
    worst = Math.max(worst, Math.abs(deg(Math.atan2(x, D + sag - z) - Math.atan2((s * chord) / arc, D))));
  }
  return worst;
}

export function specSize(s) {
  const [w, h, o] = flatSize(s);
  return [s.curve_radius_mm ? chordFromArc(w, s.curve_radius_mm)[0] : w, h, o];
}

function flatSize(s) {
  if (s.width_mm != null && s.height_mm != null) return [s.width_mm, s.height_mm, "medido"];
  if (s.diagonal_in != null) {
    const [aw, ah] = s.aspect || s.res_px;
    const k = (s.diagonal_in * 25.4) / Math.hypot(aw, ah);
    return [aw * k, ah * k, "calculado"];
  }
  throw new Error(`${s.name}: falta largura/altura ou diagonal`);
}

const defaults = (s) => ({ hinge_deg: 0, gap_mm: 0, gap_split: 0.5, vertical_offset_mm: 0, tilt_deg: 0, desktop_gap_px: 0, curve_radius_mm: null, ...s });

export function buildRow(center, left, right, D, eyeUp = 0, eyeRight = 0) {
  if (!(D > 0)) throw new Error("A distância aos olhos tem de ser positiva.");
  center = defaults(center); left = left.map(defaults); right = right.map(defaults);
  const poses = {};
  const [wc, hc, oc] = specSize(center);
  const cR = [1, 0, 0];
  poses[center.name] = { right: cR, up: applyTilt(cR, [0, 1, 0], center.tilt_deg), center: [-eyeRight, -eyeUp, -D], spec: center, origin: oc, w: wc, h: hc };
  for (const [side, specs] of [["left", left], ["right", right]]) {
    const sign = side === "left" ? -1 : 1;
    let inner = center.name;
    for (const s of specs) {
      const ip = poses[inner];
      const [w, h, o] = specSize(s);
      let base = unit([ip.right[0], 0, ip.right[2]]);
      const edge = add(ip.center, mul(base, sign * ip.w / 2));
      if (s.gap_split < 0 || s.gap_split > 1) throw new Error(`${s.name}: gap_split entre 0 e 1`);
      const hinge = add(edge, mul(base, sign * s.gap_mm * s.gap_split));
      const nr = mat(rotY(rad(-sign * s.hinge_deg)), base);
      const c = add(hinge, mul(nr, sign * (s.gap_mm * (1 - s.gap_split) + w / 2)));
      c[1] = ip.center[1] + s.vertical_offset_mm;
      poses[s.name] = { right: nr, up: applyTilt(nr, [0, 1, 0], s.tilt_deg), center: c, spec: s, origin: o, w, h };
      inner = s.name;
    }
  }
  const order = [...[...left].reverse().map((s) => s.name), center.name, ...right.map((s) => s.name)];
  const leftNames = new Set(left.map((s) => s.name));
  let x = 0;
  const monitors = order.map((name) => {
    const p = poses[name];
    if (!leftNames.has(name) && name !== center.name) x += p.spec.desktop_gap_px;
    const [rx, ry] = p.spec.res_px;
    const m = { name, center: p.center, right: p.right, up: p.up, width_mm: p.w, height_mm: p.h, res_px: [rx, ry], desktop_px: [x, 0, rx, ry], origin: p.origin, curve_radius_mm: p.spec.curve_radius_mm };
    x += rx;
    if (leftNames.has(name)) x += p.spec.desktop_gap_px;
    return m;
  });
  return { monitors, primary: center.name };
}

export const monitorOf = (rig, name) => rig.monitors.find((m) => m.name === name);

export function corners(m) {
  const hw = m.width_mm / 2, hh = m.height_mm / 2;
  return {
    bl: add(add(m.center, mul(m.right, -hw)), mul(m.up, -hh)),
    br: add(add(m.center, mul(m.right, hw)), mul(m.up, -hh)),
    tl: add(add(m.center, mul(m.right, -hw)), mul(m.up, hh)),
    tr: add(add(m.center, mul(m.right, hw)), mul(m.up, hh)),
  };
}
export const pointOn = (m, s, t) => add(add(m.center, mul(m.right, (s - 0.5) * m.width_mm)), mul(m.up, (t - 0.5) * m.height_mm));
export const normalOf = (m) => unit(cross(m.right, m.up));

export function orientation(m) {
  const n = normalOf(m), v = mul(n, -1);
  const yaw = deg(Math.atan2(n[0], n[2]));
  const pitch = deg(Math.asin(Math.max(-1, Math.min(1, v[1]))));
  let refUp = sub([0, 1, 0], mul(v, v[1]));
  let roll = 0;
  if (norm(refUp) > 1e-9) {
    refUp = unit(refUp);
    const refRight = cross(v, refUp);
    roll = deg(Math.atan2(-dot(m.up, refRight), dot(m.up, refUp)));
  }
  return { yaw, pitch, roll };
}

export function frustum(m) {
  const c = corners(m);
  const vr = unit(sub(c.br, c.bl)), vu = unit(sub(c.tl, c.bl)), vn = unit(cross(vr, vu));
  const d = -dot(c.bl, vn);
  if (d <= 0) throw new Error(`${m.name}: o olho está atrás do plano do ecrã.`);
  const o = orientation(m);
  return {
    name: m.name, left: dot(vr, c.bl) / d, right: dot(vr, c.br) / d, bottom: dot(vu, c.bl) / d, top: dot(vu, c.tl) / d,
    yaw_deg: o.yaw, pitch_deg: o.pitch, roll_deg: o.roll, distance_mm: d,
  };
}

function camRot(y, p, r) {
  [y, p, r] = [rad(y), rad(p), rad(r)];
  const ry = [[Math.cos(y), 0, Math.sin(y)], [0, 1, 0], [-Math.sin(y), 0, Math.cos(y)]];
  const rx = [[1, 0, 0], [0, Math.cos(p), -Math.sin(p)], [0, Math.sin(p), Math.cos(p)]];
  const rz = [[Math.cos(r), -Math.sin(r), 0], [Math.sin(r), Math.cos(r), 0], [0, 0, 1]];
  const mm = (A, B) => A.map((row) => [0, 1, 2].map((j) => row[0] * B[0][j] + row[1] * B[1][j] + row[2] * B[2][j]));
  return mm(mm(ry, rx), rz);
}
const angleBetween = (a, b) => deg(Math.atan2(norm(cross(a, b)), dot(a, b)));

export function cameraError(m, cam, n = 9) {
  const R = camRot(cam.yaw, cam.pitch, cam.roll);
  let max = 0;
  for (let i = 0; i < n; i++) for (let j = 0; j < n; j++) {
    const s = i / (n - 1), t = j / (n - 1);
    const drawn = mat(R, [cam.l + s * (cam.r - cam.l), cam.b + t * (cam.t - cam.b), -1]);
    max = Math.max(max, angleBetween(pointOn(m, s, t), drawn));
  }
  return max;
}

export function stretchedError(rig, m, n = 9) {
  const main = monitorOf(rig, rig.primary), f = frustum(main);
  const mmx = main.width_mm / main.res_px[0], mmy = main.height_mm / main.res_px[1];
  const [mx, my, mw, mh] = main.desktop_px, cx = mx + mw / 2, cy = my + mh / 2;
  const ct = (f.left + f.right) / 2, cv = (f.bottom + f.top) / 2;
  const R = camRot(f.yaw_deg, f.pitch_deg, f.roll_deg);
  const [dx, dy, dw, dh] = m.desktop_px;
  let max = 0;
  for (let i = 0; i < n; i++) for (let j = 0; j < n; j++) {
    const s = i / (n - 1), t = j / (n - 1);
    const xt = ct + ((dx + s * dw - cx) * mmx) / f.distance_mm;
    const yt = cv - ((dy + (1 - t) * dh - cy) * mmy) / f.distance_mm;
    max = Math.max(max, angleBetween(pointOn(m, s, t), mat(R, [xt, yt, -1])));
  }
  return max;
}

export function compareMethods(rig) {
  const out = {};
  for (const m of rig.monitors) {
    const f = frustum(m), hw = (f.right - f.left) / 2, hh = (f.top - f.bottom) / 2;
    out[m.name] = {
      exata: cameraError(m, { yaw: f.yaw_deg, pitch: f.pitch_deg, roll: f.roll_deg, l: f.left, r: f.right, b: f.bottom, t: f.top }),
      yawSimetrico: cameraError(m, { yaw: f.yaw_deg, pitch: f.pitch_deg, roll: f.roll_deg, l: -hw, r: hw, b: -hh, t: hh }),
      esticada: stretchedError(rig, m),
    };
  }
  return out;
}

export function apertureDeg(rig) {
  const a = [];
  for (const m of rig.monitors) for (const k of ["bl", "br"]) { const v = corners(m)[k]; a.push(deg(Math.atan2(v[0], -v[2]))); }
  return { total: Math.max(...a) - Math.min(...a), min: Math.min(...a), max: Math.max(...a) };
}

// --- ETS2 / ATS (modo 4) -------------------------------------------------------
export function scsPlan(rig) {
  const W = Math.max(...rig.monitors.map((m) => m.desktop_px[0] + m.desktop_px[2])) - Math.min(...rig.monitors.map((m) => m.desktop_px[0]));
  const H = Math.max(...rig.monitors.map((m) => m.desktop_px[1] + m.desktop_px[3])) - Math.min(...rig.monitors.map((m) => m.desktop_px[1]));
  const x0 = Math.min(...rig.monitors.map((m) => m.desktop_px[0])), y0 = Math.min(...rig.monitors.map((m) => m.desktop_px[1]));
  const ordered = [...rig.monitors].sort((a, b) => (a.name !== rig.primary) - (b.name !== rig.primary));
  const monitors = ordered.map((m) => {
    const f = frustum(m), [px, py, pw, ph] = m.desktop_px, A = pw / ph;
    const th = Math.max(Math.abs(f.left), Math.abs(f.right), A * Math.max(Math.abs(f.bottom), Math.abs(f.top))), tv = th / A;
    return {
      name: m.name,
      normalized: [(px - x0) / W, 1 - ((py - y0) + ph) / H, pw / W, ph / H],
      heading: f.yaw_deg, pitch: f.pitch_deg, roll: f.roll_deg,
      hfov: deg(2 * Math.atan(th)), vfov: deg(2 * Math.atan(tv)),
      subrect: [(f.left + th) / (2 * th), (f.bottom + tv) / (2 * tv), (f.right - f.left) / (2 * th), (f.top - f.bottom) / (2 * tv)],
    };
  });
  const main = monitorOf(rig, rig.primary);
  // width/height: área total do ambiente de trabalho (com os píxeis escondidos do Surround). O jogo tem de
  // desenhar exatamente com este tamanho (r_mode_width/r_mode_height), senão as vistas não batem nas junções.
  return { ui: [(main.desktop_px[0] - x0) / W, main.desktop_px[2] / W], monitors, width: W, height: H };
}

const f6 = (v) => (Math.abs(v) < 5e-7 ? 0 : v).toFixed(6);
export function scsSii(p) {
  const ptr = p.monitors.map((_, i) => `_nameless.ozp.mon${i}`);
  const L = ["SiiNunit", "{", "multimon_config : _nameless.ozp.view {", ` normalized_ui_x: ${f6(p.ui[0])}`, ` normalized_ui_width: ${f6(p.ui[1])}`, ` monitors: ${p.monitors.length}`];
  ptr.forEach((x, i) => L.push(` monitors[${i}]: ${x}`));
  L.push("}", "");
  p.monitors.forEach((m, i) => {
    const [x, y, w, h] = m.normalized, [sx, sy, sw, sh] = m.subrect;
    L.push(`monitor_config : ${ptr[i]} {`, ` name: ${m.name}`, ` normalized_x: ${f6(x)}`, ` normalized_y: ${f6(y)}`, ` normalized_width: ${f6(w)}`,
      ` normalized_height: ${f6(h)}`, " horizontal_fov_relative_offset: 0.000000", " vertical_fov_relative_offset: 0.000000",
      ` heading_offset: ${f6(m.heading)}`, ` pitch_offset: ${f6(m.pitch)}`, ` roll_offset: ${f6(m.roll)}`,
      " camera_space_offset: (0.000000, 0.000000, 0.000000)", ` horizontal_fov_override: ${f6(m.hfov)}`, ` vertical_fov_override: ${f6(m.vfov)}`,
      ` frustum_subrect_x: ${f6(sx)}`, ` frustum_subrect_y: ${f6(sy)}`, ` frustum_subrect_width: ${f6(sw)}`, ` frustum_subrect_height: ${f6(sh)}`,
      " render_interior: true", " render_exterior: true", "}", "");
  });
  L.push("}");
  return L.join("\n") + "\n";
}

// --- Folga física a partir da corda (dobradiça a meio) ---------------------------
function physicalGap(rig) {
  const c = monitorOf(rig, "center"), l = monitorOf(rig, "left");
  const chord = norm(sub(corners(c).bl, corners(l).br));
  return chord / Math.cos(rad(orientation(l).yaw) / 2);
}

// --- Assetto Corsa ---------------------------------------------------------------
// triple_screen.ini (Documentos\Assetto Corsa\cfg): pontos em metros, vistos de cima, x para a direita e y para a frente
// (confirmado no ficheiro do rig: centro de 680 mm a 720 mm = ±0,34 e 0,72; lateral de 0,68 m rodado 34°).
// O AC divide a imagem em três terços iguais da largura total. Com a correção de molduras do Surround, cada terço
// inclui alguns píxeis escondidos: o terço é o plano do próprio monitor prolongado até aos píxeis que cobre. Só
// píxeis escondidos ficam fora da área visível, por isso a imagem visível fica exata.
export function acPlan(rig) {
  const order = ["left", "center", "right"].map((n) => monitorOf(rig, n));
  const x0 = Math.min(...order.map((m) => m.desktop_px[0]));
  const W = Math.max(...order.map((m) => m.desktop_px[0] + m.desktop_px[2])) - x0, H = Math.max(...order.map((m) => m.desktop_px[3]));
  const third = W / 3, problems = [], pts = [];
  order.forEach((m, i) => {
    const [dx, , rx] = m.desktop_px, pitch = m.width_mm / rx, rel = dx - x0;
    const s0 = i * third - rel, s1 = (i + 1) * third - rel;
    // O terço só pode invadir píxeis escondidos (folga do Surround), nunca píxeis visíveis de outro monitor.
    const prevEnd = i === 0 ? 0 : order[i - 1].desktop_px[0] + order[i - 1].desktop_px[2] - x0;
    const nextStart = i === 2 ? W : order[i + 1].desktop_px[0] - x0;
    if (s0 > 1 || i * third < prevEnd - 1 || s1 < rx - 1 || (i + 1) * third > nextStart + 1) problems.push(m.name);
    const p = (s) => add(m.center, mul(m.right, s * pitch - m.width_mm / 2));
    pts.push([p(s0), p(s1)]);
  });
  const c = monitorOf(rig, "center"), yl = orientation(order[0]).yaw, yr = -orientation(order[2]).yaw;
  const sec = ["LEFT_SCREEN", "CENTER_SCREEN", "RIGHT_SCREEN"];
  const xy = (v) => `${f6(v[0] / 1000)},${f6(-v[2] / 1000)}`;
  const L = ["[TRIPLE_SCREEN]", "ACTIVE=1", ""];
  pts.forEach(([a, b], i) => L.push(`[${sec[i]}]`, `LEFT_POINT=${xy(a)}`, `RIGHT_POINT=${xy(b)}`, ""));
  // Os SPINNERS também contam para o jogo (validado no rig a 23/11: com SP_MARGINS=17 e o Surround a esconder 55 px, a
  // imagem ficava cortada nas molduras; com 0 ficou certa). Com a correção de molduras do Surround a moldura já está
  // nos píxeis escondidos, por isso a margem é 0; sem ela, a margem é a folga física entre áreas visíveis.
  const hidden = order.some((m, i) => i > 0 && m.desktop_px[0] - (order[i - 1].desktop_px[0] + order[i - 1].desktop_px[2]) > 0);
  L.push("[SPINNERS]", `SP_MARGINS=${hidden ? 0 : Math.round(physicalGap(rig))}`, `SP_ROTATION=${Math.round((yl + yr) / 2)}`, `SP_DISTANCE=${Math.round(-c.center[2])}`, `SP_WIDTH=${Math.round(c.width_mm)}`);
  return { text: L.join("\n") + "\n", width: W, height: H, problems, asymmetric: Math.abs(yl - yr) > 0.5 };
}
export const acIni = (rig) => acPlan(rig).text;

// --- Le Mans Ultimate (motor do rFactor 2) ----------------------------------------
// Config_DX11.ini ([COMPONENTS], na pasta UserData da instalação): SubViews=2 liga as três vistas;
// ViewParams/LeftView/RightView=(largura m, altura m, distância dos olhos m, ângulo lateral °, folga m).
// O jogo divide a imagem em três terços iguais e modela três planos: o central à distância dos olhos e os laterais
// articulados na ponta do central (+ folga), rodados pelo ângulo. Como cada terço do Surround inclui píxeis
// escondidos, a largura e a folga certas não são as medidas físicas: procura-se a largura e a folga do modelo do jogo
// que põem cada píxel visível na direção física real (erro angular visto dos olhos), com o ângulo e a distância reais.
export function lmuModel(w, g, aL, aR, eye) {
  const rad = (a) => (a * Math.PI) / 180;
  return (i, t) => {
    if (i === 1) return [(t - 0.5) * w, eye];
    const s = i === 0 ? -1 : 1, a = rad(i === 0 ? aL : aR), u = (i === 0 ? 1 - t : t) * w;
    return [s * (w / 2 + g) + s * u * Math.cos(a), eye - u * Math.sin(a)];
  };
}
export function lmuPlan(rig) {
  const order = ["left", "center", "right"].map((n) => monitorOf(rig, n));
  const x0 = Math.min(...order.map((m) => m.desktop_px[0]));
  const W = Math.max(...order.map((m) => m.desktop_px[0] + m.desktop_px[2])) - x0, H = Math.max(...order.map((m) => m.desktop_px[3]));
  const third = W / 3, problems = [];
  order.forEach((m, i) => {
    const s0 = i * third - (m.desktop_px[0] - x0), s1 = (i + 1) * third - (m.desktop_px[0] - x0);
    const prevEnd = i === 0 ? 0 : order[i - 1].desktop_px[0] + order[i - 1].desktop_px[2] - x0, nextStart = i === 2 ? W : order[i + 1].desktop_px[0] - x0;
    if (s0 > 1 || i * third < prevEnd - 1 || s1 < m.desktop_px[2] - 1 || (i + 1) * third > nextStart + 1) problems.push(m.name);
  });
  const c = monitorOf(rig, "center"), eye = -c.center[2];
  const aL = orientation(order[0]).yaw, aR = -orientation(order[2]).yaw;
  // Direção (vista de cima) de amostras de píxeis visíveis.
  const samples = [];
  for (const m of order) { const [dx, , rx] = m.desktop_px; for (let k = 0; k < rx; k += 12) { const gp = dx - x0 + k + 0.5, i = Math.min(2, Math.floor(gp / third)), p = pointOn(m, (k + 0.5) / rx, 0.5); samples.push([i, (gp - i * third) / third, Math.atan2(p[0], -p[2])]); } }
  const worst = (w, g) => { const f = lmuModel(w, g, aL, aR, eye); let e = 0; for (const [i, t, ang] of samples) { const q = f(i, t); e = Math.max(e, Math.abs(Math.atan2(q[0], q[1]) - ang)); } return (e * 180) / Math.PI; };
  const w0 = third * (c.width_mm / c.desktop_px[2]);
  let best = [Infinity, w0, 0];
  for (let w = w0 * 0.92; w <= w0 * 1.08; w += 2) for (let g = -20; g <= 40; g += 2) { const e = worst(w, g); if (e < best[0]) best = [e, w, g]; }
  for (let w = best[1] - 2; w <= best[1] + 2; w += 0.1) for (let g = best[2] - 2; g <= best[2] + 2; g += 0.1) { const e = worst(w, g); if (e < best[0]) best = [e, w, g]; }
  const [err, w, g] = best, h = (w * H) / third;
  const f3 = (v) => (Math.abs(v) < 5e-4 ? 0 : v).toFixed(3);
  const tuple = (a) => `${f3(w / 1000)}, ${f3(h / 1000)}, ${f3(eye / 1000)}, ${f3(a)}, ${f3(g / 1000)}`;
  return { width: W, height: H, problems, errDeg: err, view: tuple((aL + aR) / 2), left: tuple(aL), right: tuple(aR), asymmetric: Math.abs(aL - aR) > 0.5 };
}


// --- ACC e iRacing ---------------------------------------------------------------
export function accValues(rig) {
  const c = monitorOf(rig, "center"), yl = orientation(monitorOf(rig, "left")).yaw, yr = orientation(monitorOf(rig, "right")).yaw;
  return { tripleDistance: Math.round(-c.center[2]), tripleWidth: Math.round(c.width_mm), tripleBezel: Math.round(physicalGap(rig)), tripleAngle: Math.round((yl - yr) / 2) };
}
export function iracingValues(rig) {
  const c = monitorOf(rig, "center"), L = monitorOf(rig, "left"), R = monitorOf(rig, "right");
  const yl = orientation(L).yaw, yr = orientation(R).yaw, gap = physicalGap(rig);
  // Surround com correção de moldura: o driver já esconde a folga; moldura zero no iRacing (E10).
  const hidden = c.desktop_px[0] - (L.desktop_px[0] + L.desktop_px[2]);
  const mw = hidden > 0 ? Math.round((c.width_mm + L.width_mm + R.width_mm) / 3 + gap) : Math.round(c.width_mm + gap);
  const sw = hidden > 0 ? mw : Math.round(c.width_mm);
  return {
    hiddenPx: Math.max(hidden, 0),
    setup: { NumMonitors: 3, MonitorType: 0, RenderViewPerMonitor: 1, MonitorWidth: mw, ScreenWidth: sw, ScreenAngles: Math.round((yl - yr) / 2) },
    viewingDistance: Math.round(-c.center[2]), totalHfov: apertureDeg(rig).total,
  };
}

// --- MSFS ----------------------------------------------------------------------
export function msfsPlan(rig) {
  const fc = frustum(monitorOf(rig, rig.primary)), tv = (fc.top - fc.bottom) / 2;
  const views = rig.monitors.map((m) => {
    const f = frustum(m), th = tv * (m.res_px[0] / m.res_px[1]);
    return { name: m.name, lateral: -f.yaw_deg + 0, vertical: f.pitch_deg + 0, roll: f.roll_deg + 0,
      error: cameraError(m, { yaw: f.yaw_deg, pitch: f.pitch_deg, roll: f.roll_deg, l: -th, r: th, b: -tv, t: tv }) };
  });
  return { vfov: deg(2 * Math.atan(tv)), views };
}

// Compromisso para rig fixo: rotação e FOV únicos que minimizam o pior erro (igual ao Python, msfs.compromise).
function msfsErrors(rig, rot, vfov, n) {
  const tv = Math.tan(rad(vfov / 2)), out = {};
  for (const m of rig.monitors) {
    const f = frustum(m), th = tv * (m.res_px[0] / m.res_px[1]);
    const yaw = Math.abs(f.yaw_deg) < 1e-6 ? 0 : Math.sign(f.yaw_deg) * rot;
    out[m.name] = cameraError(m, { yaw, pitch: f.pitch_deg, roll: f.roll_deg, l: -th, r: th, b: -tv, t: tv }, n);
  }
  return out;
}

export function msfsCompromise(rig) {
  const p = msfsPlan(rig), side = p.views.filter((v) => Math.abs(v.lateral) > 1e-6).map((v) => Math.abs(v.lateral));
  if (!side.length) return { rotation: 0, vfov: p.vfov, errors: Object.fromEntries(p.views.map((v) => [v.name, v.error])), max: Math.max(...p.views.map((v) => v.error)) };
  const r0 = side.reduce((a, b) => a + b, 0) / side.length, v0 = p.vfov;
  let best = null;
  const scan = (rLo, rHi, rStep, vLo, vHi, vStep, n) => {
    let b = null;
    for (let r = rLo; r <= rHi + 1e-9; r += rStep)
      for (let v = Math.max(vLo, 26); v <= Math.min(vHi, 104) + 1e-9; v += vStep) {
        const e = msfsErrors(rig, r, v, n), mx = Math.max(...Object.values(e));
        if (!b || mx < b.max) b = { rotation: r, vfov: v, errors: e, max: mx };
      }
    return b;
  };
  best = scan(r0 - 20, r0 + 10, 1, v0 - 4, v0 + 10, 0.5, 5);
  best = scan(best.rotation - 1, best.rotation + 1, 0.2, best.vfov - 0.5, best.vfov + 0.5, 0.1, 9);
  return { ...best, rotation: Math.round(best.rotation * 10) / 10, vfov: Math.round(best.vfov * 100) / 100 };
}

export function msfsIdealAngle(center, side, isLeft, D, eyeUp = 0, eyeRight = 0) {
  const err = (a) => {
    const s = { ...side, hinge_deg: a };
    const rig = buildRow(center, isLeft ? [s] : [], isLeft ? [] : [s], D, eyeUp, eyeRight);
    return msfsPlan(rig).views.find((v) => v.name === side.name).error;
  };
  let best = [Infinity, 0];
  for (let a = 0; a <= 90; a += 0.5) { const e = err(a); if (e < best[0]) best = [e, a]; }
  const lo = Math.max(0, best[1] - 0.5), hi = Math.min(90, best[1] + 0.5);
  for (let i = 0; i <= 50; i++) { const a = lo + (i * (hi - lo)) / 50, e = err(a); if (e < best[0]) best = [e, a]; }
  return { ideal: Math.round(best[1] * 10) / 10, idealError: best[0], currentError: err(side.hinge_deg ?? 0) };
}

// --- MSFS 2024 sem Surround (igual ao Python: msfs.config_2024) -------------------------------
// Medido no rig (14/11/2026): guinada real = valor × hFOV / 90 → com 90 as janelas emendam em qualquer zoom.
export const MSFS2024_REF_HFOV = 90;
export function msfsBezelGap(rig, side) {
  const c = monitorOf(rig, rig.primary), s = monitorOf(rig, side);
  const a = side === "right" ? pointOn(c, 1, 0.5) : pointOn(c, 0, 0.5);
  const b = side === "right" ? pointOn(s, 0, 0.5) : pointOn(s, 1, 0.5);
  return deg(Math.acos(Math.max(-1, Math.min(1, dot(a, b) / (norm(a) * norm(b))))));
}
// Valor escrito em CameraDegRotationAdd (arredondado a 2 casas, como no ficheiro).
// Com moldura escondida: guinada real = hFOV + moldura; com imagem colada: guinada real = hFOV.
function msfs2024Value(rig, side, hfov, hideBezel) {
  return Math.round(MSFS2024_REF_HFOV * (hfov + (hideBezel ? msfsBezelGap(rig, side) : 0)) / hfov * 100) / 100;
}

// Erro da configuração que vai ser escrita: a rotação NÃO é livre, é a que resulta do valor escrito
// (guinada real = valor × hFOV / 90). Mesmo FOV em todas as janelas, imagem centrada (sem recorte).
export function msfs2024Errors(rig, hfov, hideBezel = true, n = 9) {
  const c = monitorOf(rig, rig.primary), aspect = c.res_px[0] / c.res_px[1];
  const tv = Math.tan(rad(hfov / 2)) / aspect, out = {};
  for (const m of rig.monitors) {
    const f = frustum(m), th = tv * (m.res_px[0] / m.res_px[1]);
    let yaw = 0;
    if (m.name !== rig.primary) {
      const side = f.yaw_deg > 0 ? "left" : "right";
      yaw = Math.sign(f.yaw_deg) * msfs2024Value(rig, side, hfov, hideBezel) * hfov / MSFS2024_REF_HFOV;
    }
    out[m.name] = cameraError(m, { yaw, pitch: f.pitch_deg, roll: f.roll_deg, l: -th, r: th, b: -tv, t: tv }, n);
  }
  return out;
}

// Melhor FOV horizontal para o MSFS 2024: uma só variável (a rotação é condicionada pelo FOV).
// MSFS2024_HFOV_RANGE: intervalo aceite na entrada manual (sanidade, não é um limite do jogo).
// MSFS_VFOV_LIMIT: MEDIDO no MSFS 2024 a 15/11/2026 (câmara do cockpit, Cirrus Vision Jet, modo de programador >
// Debug > Aircraft > Camera Blend, "Camera Infos: Fov"): zoom máximo 0,31 rad, zoom mínimo 1,35 rad (2 casas decimais).
// Limite usado = a parte garantidamente alcançável: 0,315 rad = 18,05° e 1,345 rad = 77,06°.
// O intervalo 26°–104° da Fly Elise-ng aplica-se à exportação multi-vista DELES, não ao zoom do simulador.
// A otimização respeita este limite e devolve também o ótimo sem limite, para a interface o mostrar.
export const MSFS2024_HFOV_RANGE = [10, 150];
export const MSFS_VFOV_LIMIT = [18.1, 77.0];
const hfovFromVfov = (vfov, aspect) => deg(2 * Math.atan(aspect * Math.tan(rad(vfov / 2))));
export function msfs2024BestHfov(rig, hideBezel = true, { limit = true } = {}) {
  const c = monitorOf(rig, rig.primary), aspect = c.res_px[0] / c.res_px[1];
  let [lo, hi] = MSFS2024_HFOV_RANGE;
  if (limit) { lo = Math.max(lo, hfovFromVfov(MSFS_VFOV_LIMIT[0], aspect)); hi = Math.min(hi, hfovFromVfov(MSFS_VFOV_LIMIT[1], aspect)); }
  const worst = (h, n) => Math.max(...Object.values(msfs2024Errors(rig, h, hideBezel, n)));
  let best = [Infinity, lo];
  for (let h = lo; h <= hi + 1e-9; h += 0.25) { const e = worst(h, 5); if (e < best[0]) best = [e, h]; }
  const a = Math.max(lo, best[1] - 0.5), b = Math.min(hi, best[1] + 0.5);
  best = [Infinity, a];
  for (let i = 0; i <= 100; i++) { const h = a + (i * (b - a)) / 100, e = worst(h, 9); if (e < best[0]) best = [e, h]; }
  // arredonda para dentro do intervalo (o limite inferior não pode ser ultrapassado pelo arredondamento)
  return Math.min(Math.max(Math.round(best[1] * 100) / 100, Math.ceil(lo * 100) / 100), Math.floor(hi * 100) / 100);
}

export function msfs2024Config(rig, { hideBezel = true, hfov = null, swap = false, mon = { left: 1, center: 2, right: 3 } } = {}) {
  const c = monitorOf(rig, rig.primary), aspect = c.res_px[0] / c.res_px[1];
  const auto = hfov == null;
  if (auto) hfov = msfs2024BestHfov(rig, hideBezel);
  if (!(hfov > 0 && hfov < 180)) throw new Error("FOV horizontal tem de estar entre 0° e 180°.");
  const m = { ...mon };
  if (swap) [m.left, m.right] = [m.right, m.left];
  const rl = msfs2024Value(rig, "left", hfov, hideBezel), rr = msfs2024Value(rig, "right", hfov, hideBezel);
  const errors = msfs2024Errors(rig, hfov, hideBezel), max = Math.max(...Object.values(errors));
  const vfov = deg(2 * Math.atan(Math.tan(rad(hfov / 2)) / aspect));
  const outOfLimit = vfov < MSFS_VFOV_LIMIT[0] - 1e-6 || vfov > MSFS_VFOV_LIMIT[1] + 1e-6;
  // Se o limite documentado prendeu a otimização, diz quanto se perde por causa dele.
  let unconstrained = null;
  if (auto) {
    const hf = msfs2024BestHfov(rig, hideBezel, { limit: false }), mf = Math.max(...Object.values(msfs2024Errors(rig, hf, hideBezel)));
    if (mf < max - 0.01) unconstrained = { hfov: hf, vfov: deg(2 * Math.atan(Math.tan(rad(hf / 2)) / aspect)), max: mf };
  }
  const lines = [`Monitor ${m.center}`, "Windowed 0", "FullscreenBorderless 1"];
  [["left", -rl], ["right", rr]].forEach(([s, r], i) => lines.push(
    `WindowCustom${i}.Monitor ${m[s]}`, `WindowCustom${i}.Windowed 0`, `WindowCustom${i}.FullscreenBorderless 1`,
    `WindowCustom${i}.WindowActive 1`, `WindowCustom${i}.CameraDegRotationAdd 0.000000 ${r.toFixed(6)} 0.000000`));
  return { hfov, auto, errors, max, vfov, outOfLimit, unconstrained, rl, rr, mon: m,
    gapL: msfsBezelGap(rig, "left"), gapR: msfsBezelGap(rig, "right"), lines };
}
