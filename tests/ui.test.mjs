// Interface da Beta 0.1 num browser real, com o OZPanorama.exe simulado (harness.mjs).
// Executar: PLAYWRIGHT_PATH=... CHROME=... node --test --test-concurrency=1 tests/*.test.mjs
import test, { before, after } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { chromium, startServer, openPage, clickGame, setField } from "./harness.mjs";
import * as E from "../app/engine.js";
import { zukaRig } from "./rigs.mjs";
const here = path.dirname(fileURLToPath(import.meta.url)), appDir = path.join(here, "..", "app");

let browser;
const servers = [];
before(async () => { browser = await chromium.launch({ executablePath: process.env.CHROME || "/usr/bin/google-chrome" }); });
after(async () => { await browser.close(); servers.forEach((s) => s.close()); });

async function app(opts = {}) {
  const srv = await startServer(appDir, { appMode: opts.appMode !== false }); servers.push(srv);
  if (opts.setup) opts.setup(srv);
  const p = await openPage(browser, srv.url, { lang: opts.lang || "pt-BR" });
  return { srv, ...p };
}
const out = (page) => page.textContent("#out");
const alerts = (page) => page.textContent("#alerts");
const applyDisabled = (page) => page.$eval("#apply", (b) => b.disabled);
const step2 = async (page, g) => { await page.click('#nav button[data-step="2"]'); if (g) await clickGame(page, g); await page.waitForTimeout(450); };
const msfsCfg = () => E.msfs2024Config(zukaRig(56), { hideBezel: true });

test("Fluxo completo MSFS: pré-visualização → backup → mensagem exata → aplicar a configuração atual", async () => {
  const { srv, page, errors } = await app();
  await page.click("#presetZuka");
  await step2(page, "msfs");
  assert.equal(await applyDisabled(page), true, "sem escolher a janela da esquerda não aplica");
  assert.match(await page.textContent("#pvAlerts"), /Escolha qual janela está à esquerda/);
  await page.click('#msfsWin button[data-w="0"]'); await page.waitForTimeout(400);
  assert.equal(await applyDisabled(page), false);
  assert.match(await page.textContent("#preview"), /-91\.530000/);
  await page.click("#apply");
  await page.waitForSelector("#modal:not([hidden])");
  assert.equal(await page.textContent("#modalText"),
    "Está prestes a alterar as configurações do Microsoft Flight Simulator 2024. Foi criado e validado um backup dos arquivos originais. Deseja continuar?");
  const paths = srv.requests.map((r) => r.path);
  assert.ok(paths.includes("/api/prepare") && !paths.includes("/api/apply"), "o backup é criado antes da pergunta; nada é escrito antes do sim");
  await page.click("#modalYes"); await page.waitForTimeout(400);
  assert.equal(srv.applied.length, 1);
  const cfg = msfsCfg();
  assert.deepEqual(srv.applied[0].values, { rotLeft: +cfg.rl.toFixed(6), rotRight: +cfg.rr.toFixed(6), leftWindow: "0" });
  assert.match(await page.textContent("#applyMsg"), /Aplicado e verificado/);
  assert.deepEqual(errors, []);
});

test("Responder não: nada é escrito e o backup fica guardado como cancelado", async () => {
  const { srv, page } = await app();
  await page.click("#presetZuka"); await step2(page, "iracing");
  await page.click("#apply"); await page.waitForSelector("#modal:not([hidden])");
  await page.click("#modalNo"); await page.waitForTimeout(300);
  assert.equal(srv.applied.length, 0);
  assert.ok(srv.requests.some((r) => r.path === "/api/cancel"));
  assert.equal(srv.backups[0].estado, "cancelado");
  assert.match(await page.textContent("#applyMsg"), /Nada foi alterado/);
});

test("B1: dados inválidos bloqueiam aplicar e nada vai para o servidor; corrigido, aplica os valores novos", async () => {
  const { srv, page } = await app();
  await page.click("#presetZuka");
  await setField(page, "dist", 50);
  assert.match(await alerts(page), /Distância dos olhos fora/);
  await step2(page, "iracing");
  assert.equal(await out(page), "");
  assert.equal(await applyDisabled(page), true);
  await page.evaluate(() => { const b = document.getElementById("apply"); b.disabled = false; b.click(); });
  await page.waitForTimeout(300);
  assert.ok(!srv.requests.some((r) => r.path === "/api/prepare"), "sem configuração válida não se prepara nada");
  await page.click('#nav button[data-step="1"]'); await setField(page, "dist", 720);
  await step2(page, "iracing");
  await page.click("#apply"); await page.waitForSelector("#modal:not([hidden])"); await page.click("#modalYes"); await page.waitForTimeout(300);
  assert.equal(srv.applied[0].values.ViewingDist, 720);
});

test("Mudar de jogo invalida a pré-visualização anterior (não se aplica o ficheiro de outro jogo)", async () => {
  const { srv, page } = await app();
  await page.click("#presetZuka"); await step2(page, "iracing");
  await clickGame(page, "ets2"); await page.waitForTimeout(400);
  await page.click("#apply"); await page.waitForSelector("#modal:not([hidden])"); await page.click("#modalYes"); await page.waitForTimeout(300);
  assert.equal(srv.applied[0].game, "ets2");
  assert.ok(srv.applied[0].values.sii.startsWith("SiiNunit"));
  assert.deepEqual([srv.applied[0].values.width, srv.applied[0].values.height], [5838, 1080]);
});

test("Jogo aberto: a pré-visualização avisa e aplicar fica bloqueado", async () => {
  const { page } = await app({ setup: (s) => (s.state.running.msfs = true) });
  await page.click("#presetZuka"); await step2(page, "msfs");
  await page.click('#msfsWin button[data-w="0"]'); await page.waitForTimeout(400);
  assert.equal(await applyDisabled(page), true);
  assert.match(await page.textContent("#pvAlerts"), /O jogo está aberto/);
});

test("Erro ao preparar o backup: mensagem traduzida, sem pergunta e sem escrita", async () => {
  const { srv, page } = await app({ setup: (s) => (s.state.prepareError = "locked") });
  await page.click("#presetZuka"); await step2(page, "iracing");
  await page.click("#apply"); await page.waitForTimeout(400);
  assert.equal(await page.$eval("#modal", (m) => m.hidden), true);
  assert.match(await page.textContent("#applyMsg"), /sendo usado por outro programa/);
  assert.equal(srv.applied.length, 0);
});

test("Falha na escrita com reposição automática é mostrada como tal", async () => {
  const { page } = await app({ setup: (s) => (s.state.failApply = "falhou-reposto") });
  await page.click("#presetZuka"); await step2(page, "iracing");
  await page.click("#apply"); await page.waitForSelector("#modal:not([hidden])"); await page.click("#modalYes"); await page.waitForTimeout(400);
  assert.match(await page.textContent("#applyMsg"), /Não foi possível aplicar.*repostos e verificados/s);
});

test("Integração não validada: sem aplicação automática", async () => {
  const { page } = await app({ setup: (s) => (s.state.status.iracing = "nao-validado") });
  await page.click("#presetZuka"); await step2(page, "iracing");
  assert.equal(await applyDisabled(page), true);
  assert.equal(await page.$eval("#notValidated", (e) => e.hidden), false);
  assert.match(await page.textContent('#games button[data-k="iracing"] .badge'), /Não validado/);
});

test("Backups e restauro: lista, confirmação e pedido de restauro", async () => {
  const { srv, page } = await app();
  await page.click("#presetZuka"); await step2(page, "iracing");
  await page.click("#apply"); await page.waitForSelector("#modal:not([hidden])"); await page.click("#modalYes"); await page.waitForTimeout(300);
  await page.click('#nav button[data-step="3"]'); await page.waitForTimeout(400);
  assert.match(await page.textContent("#bkList"), /aplicado pelo OZPanorama/);
  await page.click("[data-restore]"); await page.waitForSelector("#modal:not([hidden])");
  assert.match(await page.textContent("#modalText"), /estado atual é guardado num backup novo/);
  await page.click("#modalYes"); await page.waitForTimeout(400);
  const r = srv.requests.find((x) => x.path === "/api/restore");
  assert.deepEqual([r.body.confirm, r.body.acceptChanged], [true, false]);
  assert.match(await page.textContent("#bkMsg"), /Restaurado e verificado/);
});

test("Restauro de ficheiro alterado depois pede uma segunda confirmação", async () => {
  const { srv, page } = await app();
  await page.click("#presetZuka"); await step2(page, "iracing");
  await page.click("#apply"); await page.waitForSelector("#modal:not([hidden])"); await page.click("#modalYes"); await page.waitForTimeout(300);
  srv.backups[0].ficheiros[0].situacao = "alterado";
  await page.click('#nav button[data-step="3"]'); await page.waitForTimeout(400);
  assert.match(await page.textContent("#bkList"), /alterado depois/);
  await page.click("[data-restore]"); await page.waitForSelector("#modal:not([hidden])"); await page.click("#modalYes");
  await page.waitForTimeout(200);
  assert.match(await page.textContent("#modalTitle"), /Arquivos alterados depois/);
  await page.click("#modalNo"); await page.waitForTimeout(200);
  assert.ok(!srv.requests.some((x) => x.path === "/api/restore"), "recusar a segunda pergunta não restaura");
});

test("Reportar problema: relatório revisto pela pessoa e enviado só ao carregar", async () => {
  const { srv, page } = await app();
  await page.click("#presetZuka");
  await page.click('#nav button[data-step="4"]');
  await page.fill("#repDesc", "A imagem ficou torta");
  await page.click("#repMake"); await page.waitForTimeout(300);
  const txt = await page.inputValue("#repText");
  assert.match(txt, /A imagem ficou torta/); assert.match(txt, /dist=710/);
  assert.ok(!srv.requests.some((x) => x.path === "/api/open-issue"));
  await page.fill("#repText", txt + "\nnota minha");
  await page.click("#repOpen"); await page.waitForTimeout(200);
  const r = srv.requests.find((x) => x.path === "/api/open-issue");
  assert.match(r.body.text, /nota minha$/); assert.match(r.body.title, /^\[Beta\] A imagem ficou torta/);
});

test("B2: rig novo vazio não inventa medidas", async () => {
  const { page } = await app();
  const a = await alerts(page);
  for (const re of [/Falta preencher:/, /distância/, /ângulo do lateral esquerdo/, /folga/, /diagonal/, /resolução horizontal do monitor central/]) assert.match(a, re);
  await step2(page, "msfs");
  assert.equal(await out(page), ""); assert.equal(await applyDisabled(page), true);
});

for (const [id, re] of [["angL", /ângulo do lateral esquerdo/], ["angR", /ângulo do lateral direito/], ["gap", /folga/], ["dist", /distância/], ["eyeUp", /altura dos olhos/], ["xC", /resolução horizontal do monitor central/], ["rC", /raio do monitor central/]]) {
  test(`B2: campo ${id} apagado gera erro em vez de valor predefinido`, async () => {
    const { page } = await app();
    await page.click("#presetZuka"); await setField(page, id, "");
    assert.match(await alerts(page), re);
    assert.equal(await out(page), "");
  });
}

test("B2: largura sem altura é erro; zero é válido; raio impossível recusado", async () => {
  const { page } = await app();
  await page.click("#presetZuka");
  await setField(page, "hL", ""); assert.match(await alerts(page), /Monitor esquerdo: preencha a largura e a altura/);
  await setField(page, "hL", 336.2); await setField(page, "gap", 0); assert.ok((await out(page)).length > 0);
  await setField(page, "rC", 150); assert.match(await alerts(page), /impossível/); assert.equal(await out(page), "");
});

test("B3: no MSFS, o erro mostrado é o da configuração escrita; FOV inválido bloqueia", async () => {
  const { page } = await app();
  await page.click("#presetZuka"); await step2(page, "msfs");
  const cfg = msfsCfg();
  assert.equal((await page.textContent("#fix .big")).replace("°", "").replace(",", "."), cfg.max.toFixed(1));
  assert.equal(await out(page), cfg.lines.join("\n"));
  await page.fill("#mHfov", "3"); await page.dispatchEvent("#mHfov", "change");
  assert.match(await page.textContent("#alerts2"), /FOV horizontal do MSFS inválido/);
  assert.equal(await out(page), ""); assert.equal(await applyDisabled(page), true);
});

test("Regressão: rig do Zuka dá os mesmos ficheiros que antes (ETS2, ATS, iRacing)", async () => {
  const { page, errors } = await app({ appMode: false });
  await page.click("#presetZuka"); await step2(page);
  for (const g of ["ets2", "ats", "iracing"]) {
    await clickGame(page, g);
    const gold = fs.readFileSync(path.join(here, `golden_ui/zuka_${g}.txt`), "utf8");
    if (g === "iracing") assert.equal(await out(page), gold + "\nViewingDist=710", g);
    else assert.equal(await out(page), gold, g);
  }
  assert.deepEqual(errors, []);
});

test("Sem o programa (só a página): calcula e copia, sem aplicar nem backups", async () => {
  const { page } = await app({ appMode: false });
  await page.click("#presetZuka"); await step2(page, "msfs");
  assert.equal(await page.$eval("#offline", (e) => e.hidden), false);
  assert.equal(await page.$eval("#apply", (b) => b.disabled), true);
  assert.equal(await page.$eval('#nav button[data-step="3"]', (b) => b.hidden), true);
  assert.equal(await page.$eval("#detect", (b) => b.hidden), true);
});

test("Surround: sem clique só se lê o estado (nada muda no Windows); token em todos os pedidos", async () => {
  const { srv, page } = await app();
  await page.click("#presetZuka"); await step2(page, "msfs");
  assert.ok(!srv.requests.some((r) => /surround/.test(r.path) && r.method !== "GET"));
  const token = await page.$eval('meta[name="ozp-token"]', (m) => m.content);
  assert.ok(srv.requests.length > 0 && srv.requests.every((r) => r.token === token));
  assert.ok(srv.requests.filter((r) => r.method === "POST").every((r) => r.type === "application/json"));
  const ids = await page.$$eval("#games button[data-k]", (els) => els.map((e) => e.dataset.k));
  assert.deepEqual(ids.sort(), ["ats", "ets2", "iracing", "msfs"]);
});

test("Línguas: as mesmas do RealFFB, escolhidas pelo Windows e trocáveis", async () => {
  const opts = await (async () => { const { page } = await app({ lang: "fr-FR" }); return page; })();
  assert.equal(await opts.$eval("#lang", (s) => s.value), "fr");
  assert.deepEqual(await opts.$$eval("#lang option", (o) => o.map((x) => x.textContent)), ["English", "Português (Brasil)", "Español", "Français"]);
  assert.equal(await opts.textContent('#nav button[data-step="2"] em'), "Jeu et application");
  await opts.selectOption("#lang", "es");
  assert.equal(await opts.textContent('#nav button[data-step="2"] em'), "Juego y aplicar");
  assert.match(await opts.textContent("#alerts"), /Falta rellenar/);
  await opts.selectOption("#lang", "en");
  assert.equal(await opts.textContent('#nav button[data-step="2"] em'), "Game and apply");
  const { page: de } = await app({ lang: "de-DE" });
  assert.equal(await de.$eval("#lang", (s) => s.value), "en", "língua sem tradução cai para inglês, como no RealFFB");
});

test("Ângulo pela distância entre as pontas de fora do esquerdo e do direito", async () => {
  const { page } = await app();
  await page.click("#presetZuka");
  await page.fill("#tD", "1300");
  assert.equal(await page.$eval("#angL", (i) => i.value), "55.9");
  assert.equal(await page.$eval("#angR", (i) => i.value), "55.9");
  await page.fill("#tD", "2000");
  assert.match(await page.textContent("#tDOut"), /\S/);
  assert.equal(await page.$eval("#angL", (i) => i.value), "55.9");
});
