// Gera as saídas de referência (rig do Zuka) a partir de uma pasta app/ — usado uma vez sobre o código original.
import fs from "node:fs";
import { chromium, startServer, openPage, clickGame } from "./harness.mjs";
const [appDir, outDir] = process.argv.slice(2);
fs.mkdirSync(outDir, { recursive: true });
const s = await startServer(appDir, { appMode: false });
const b = await chromium.launch({ executablePath: process.env.CHROME || "/usr/bin/google-chrome" });
const { page } = await openPage(b, s.url);
for (const g of ["ets2", "ats", "iracing", "ac", "acc"]) {
  await clickGame(page, g);
  fs.writeFileSync(`${outDir}/zuka_${g}.txt`, await page.textContent("#out"));
}
await b.close(); s.close();
console.log("golden gerado em", outDir);
