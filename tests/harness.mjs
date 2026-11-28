// Servidor de teste: serve app/ e simula a API do OZPanorama.exe (src/Host.cs), registando os pedidos.
// O comportamento real da escrita (backups, hashes, recuperação) é testado em src/Tests.cs no Windows.
import http from "node:http";
import fs from "node:fs";
import path from "node:path";
import { createRequire } from "node:module";
const require = createRequire(import.meta.url);
export const { chromium } = require(process.env.PLAYWRIGHT_PATH || "playwright");

export function startServer(appDir, { appMode = true } = {}) {
  const requests = [], previews = new Map(), applied = [], backups = [];
  const state = { status: {}, running: { msfs: false }, cur: { "WindowCustom0.CameraDegRotationAdd": "0.000000", "WindowCustom1.CameraDegRotationAdd": "0.000000" }, failApply: null, prepareError: null };
  const games = () => [
    { id: "msfs", name: "Microsoft Flight Simulator 2024", status: state.status.msfs || "testado", canApply: (state.status.msfs || "testado") === "testado", running: state.running.msfs, files: [{ nome: "UserCfg.opt", existe: true }] },
    { id: "ets2", name: "Euro Truck Simulator 2", status: "testado", canApply: true, running: false, files: [{ nome: "multimon_config.sii", existe: false }, { nome: "config.cfg", existe: true }] },
    { id: "ats", name: "American Truck Simulator", status: "testado", canApply: true, running: false, files: [{ nome: "multimon_config.sii", existe: true }, { nome: "config.cfg", existe: true }] },
    { id: "lmu", name: "Le Mans Ultimate", status: "testado", canApply: true, running: false, files: [{ nome: "Config_DX11.ini", existe: true }] },
    { id: "ac", name: "Assetto Corsa", status: "testado", canApply: true, running: false, files: [{ nome: "video.ini", existe: true }, { nome: "triple_screen.ini", existe: true }] },
    { id: "iracing", name: "iRacing", status: state.status.iracing || "testado", canApply: (state.status.iracing || "testado") === "testado", running: false, files: [{ nome: "rendererDX11Monitor.ini", existe: true }] },
  ];
  const changesFor = (game, v) => {
    if (game === "msfs") {
      const lw = v.leftWindow === "1" ? 1 : 0, rw = 1 - lw;
      return [[`WindowCustom${lw}.CameraDegRotationAdd`, (-v.rotLeft).toFixed(6)], [`WindowCustom${rw}.CameraDegRotationAdd`, (+v.rotRight).toFixed(6)]]
        .map(([k, n]) => ({ ficheiro: "UserCfg.opt", chave: k, atual: state.cur[k], novo: n, muda: state.cur[k] !== n }));
    }
    if (game === "lmu") return ["SubViews", "UseSubViewParams", "ViewParams", "LeftView", "RightView"].map((k) => ({ ficheiro: "Config_DX11.ini", chave: k, atual: "0", novo: String(v[k] ?? "2"), muda: true }));
    if (game === "ac") return [{ ficheiro: "triple_screen.ini", chave: "(ficheiro inteiro)", atual: "310 caracteres", novo: v.triple.length + " caracteres", muda: true },
      { ficheiro: "video.ini", chave: "[CAMERA] MODE", atual: "DEFAULT", novo: "TRIPLE", muda: true }];
    if (game === "iracing") return Object.entries(v).map(([k, n]) => ({ ficheiro: "rendererDX11Monitor.ini", chave: k, atual: "0", novo: String(n), muda: true }));
    return [{ ficheiro: "multimon_config.sii", chave: "(ficheiro inteiro)", atual: "(não existe)", novo: v.sii.length + " caracteres", muda: true },
      { ficheiro: "config.cfg", chave: "r_multimon_mode", atual: "0", novo: "4", muda: true }];
  };
  const types = { ".html": "text/html", ".js": "text/javascript", ".png": "image/png", ".ico": "image/x-icon" };
  const srv = http.createServer((req, res) => {
    const u = new URL(req.url, "http://x");
    const json = (o, code = 200) => { res.writeHead(code, { "Content-Type": "application/json" }); res.end(JSON.stringify(o)); };
    if (u.pathname.startsWith("/api/")) {
      if (!appMode) { res.writeHead(404); return res.end(); }
      let raw = ""; req.on("data", (c) => (raw += c));
      req.on("end", () => {
        const b = raw ? JSON.parse(raw) : {};
        requests.push({ path: u.pathname, method: req.method, token: req.headers["x-ozp-token"], type: req.headers["content-type"], body: b });
        const p = u.pathname;
        if (p === "/api/info") return json({ ok: true, version: "0.1.8-beta", issuesUrl: "https://github.com/OptimusZuka/OZPanorama/issues/new", screens: [{ w: 5838, h: 1080, x: 0, y: 0, primary: true }], games: games() });
        if (p === "/api/preview") {
          const id = "pv" + previews.size;
          previews.set(id, { game: b.game, values: b.values });
          return json({ ok: true, previewId: id, game: b.game, running: !!state.running[b.game], changes: changesFor(b.game, b.values), warnings: [],
            info: b.game === "msfs" ? [{ janela: 0, Monitor: "1", WindowActive: "1" }, { janela: 1, Monitor: "3", WindowActive: "1" }] : [],
            files: [{ nome: "x", caminho: "%USERPROFILE%\\x", existe: true, codificacao: "UTF-8" }] });
        }
        if (p === "/api/prepare") {
          if (state.prepareError) return json({ ok: false, code: state.prepareError, error: "recusado" }, 409);
          const pv = previews.get(b.previewId); if (!pv) return json({ ok: false, code: "stale", error: "x" }, 409);
          pv.backup = `${pv.game}/20261115-120000-000_${String(backups.length).padStart(6, "0")}`;
          backups.push({ id: pv.backup, jogo: pv.game, nomeJogo: pv.game, operacao: "aplicar", estado: "criado", criado: "2026-11-15 12:00:00", versao: "0.1.0-beta", ficheiros: [{ nome: "UserCfg.opt", caminho: "x", existia: true, situacao: "original" }] });
          return json({ ok: true, backup: pv.backup });
        }
        if (p === "/api/cancel") { const pv = previews.get(b.previewId); if (pv && pv.backup) backups.find((x) => x.id === pv.backup).estado = "cancelado"; previews.delete(b.previewId); return json({ ok: true }); }
        if (p === "/api/apply") {
          const pv = previews.get(b.previewId);
          if (!b.confirm) return json({ ok: false, code: "confirm", error: "x" }, 409);
          if (!pv || !pv.backup) return json({ ok: false, code: "no-backup", error: "x" }, 409);
          previews.delete(b.previewId);
          const bk = backups.find((x) => x.id === pv.backup);
          if (state.failApply) { bk.estado = state.failApply; return json({ ok: false, status: state.failApply, backup: pv.backup, message: "x", changes: [], detail: ["Falha injetada"] }); }
          applied.push({ game: pv.game, values: pv.values });
          if (pv.game === "msfs") for (const c of changesFor(pv.game, pv.values)) state.cur[c.chave] = c.novo;
          bk.estado = "aplicado"; bk.ficheiros[0].situacao = "aplicado";
          return json({ ok: true, status: "aplicado", backup: pv.backup, changes: [], detail: [] });
        }
        if (p === "/api/backups") return json({ ok: true, backups: [...backups].reverse() });
        if (p === "/api/restore") {
          const bk = backups.find((x) => x.id === b.id);
          if (bk.ficheiros.some((f) => f.situacao === "alterado") && !b.acceptChanged) return json({ ok: false, code: "changed-after", error: "x" }, 409);
          bk.ficheiros.forEach((f) => (f.situacao = "original"));
          state.cur = { "WindowCustom0.CameraDegRotationAdd": "0.000000", "WindowCustom1.CameraDegRotationAdd": "0.000000" };
          return json({ ok: true, message: "x", safetyBackup: bk.jogo + "/20261115-130000-000_aaaaaa", detail: [] });
        }
        if (p === "/api/report") return json({ ok: true, text: "### Versão\nOZPanorama 0.1.0-beta\n### Monitores e posição\n```\n" + (b.ui && b.ui.rig) + "\n```\n", issuesUrl: "x" });
        return json({ ok: true });
      });
      return;
    }
    const f = path.join(appDir, u.pathname === "/" ? "index.html" : u.pathname);
    if (!f.startsWith(appDir) || !fs.existsSync(f)) { res.writeHead(404); return res.end(); }
    res.writeHead(200, { "Content-Type": types[path.extname(f)] || "application/octet-stream" }); res.end(fs.readFileSync(f));
  });
  return new Promise((ok) => srv.listen(0, "127.0.0.1", () => ok({ url: `http://127.0.0.1:${srv.address().port}/`, requests, previews, applied, backups, state, close: () => { srv.close(); srv.closeAllConnections(); } })));
}

export async function openPage(browser, url, { lang = "pt-BR" } = {}) {
  const ctx = await browser.newContext({ locale: lang, viewport: { width: 1440, height: 900 } });
  const page = await ctx.newPage();
  const errors = []; page.on("pageerror", (e) => errors.push(e.message));
  await page.goto(url, { waitUntil: "domcontentloaded" });
  await page.waitForFunction(() => document.querySelector("#quality") && document.querySelector("#quality").innerHTML !== "");
  return { page, errors };
}
export const clickGame = (page, k) => page.click(`#games button[data-k="${k}"]`);
export async function setField(page, id, value) {
  await page.fill("#" + id, String(value));
  await page.dispatchEvent("#form", "input");
}
