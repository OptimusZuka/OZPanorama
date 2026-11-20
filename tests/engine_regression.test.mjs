// Motor geométrico: AC e ACC saíram da interface da Beta 0.1, mas o cálculo fica protegido contra regressões.
import { test } from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import * as E from "../app/engine.js";
import { zukaRig } from "./rigs.mjs";
const here = path.dirname(fileURLToPath(import.meta.url));
const gold = (g) => fs.readFileSync(path.join(here, `golden_ui/zuka_${g}.txt`), "utf8");

test("Motor: AC igual ao ficheiro de referência", () => assert.equal(E.acIni(zukaRig(56)), gold("ac")));
test("Motor: ACC igual ao ficheiro de referência", () => assert.equal(JSON.stringify(E.accValues(zukaRig(56)), null, 2), gold("acc")));
