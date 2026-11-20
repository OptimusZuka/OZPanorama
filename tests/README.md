# Testes do OZPanorama

Requisitos: Node 18+ e Playwright com um Chrome/Chromium (só para os testes de interface).

```
node --test tests/engine_msfs.test.mjs                 # motor (sem browser)
PLAYWRIGHT_PATH=<caminho>/playwright CHROME=<chrome> node --test --test-concurrency=1 tests/*.test.mjs
```

- `ui.test.mjs` — interface da Beta 0.1 com o `OZPanorama.exe` simulado (`harness.mjs`): pré-visualização → backup → confirmação → aplicar, cancelamento, erros, backups e restauro, relatório, línguas, B1/B2/B3 e regressão.
- `i18n.test.mjs` — todas as frases existem em en, pt-BR, es e fr.
- O núcleo de escrita (bytes, backups, falhas injetadas, restauro) é testado em `src/Tests.cs` (`build.cmd test`, só no Windows).
- `engine_msfs.test.mjs` — B3: o erro mostrado no MSFS é recalculado a partir das linhas escritas no UserCfg.opt.
- `engine_regression.test.mjs` — AC e ACC saíram da interface da Beta 0.1; o motor continua comparado com `golden_ui/zuka_ac.txt` e `zuka_acc.txt`.
- `rigs.mjs` — rig de referência. Serve só para reproduzir resultados conhecidos; não é predefinição da app.
