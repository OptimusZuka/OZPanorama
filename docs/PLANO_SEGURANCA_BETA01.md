# OZPanorama Beta 0.1 — Plano de segurança (Etapa 2)

## 1. Decisões de arquitetura

| Decisão | Escolha | Porquê |
|---|---|---|
| Programa | **`OZPanorama.exe` em C#**, compilado com o `csc.exe` do .NET Framework 4.8 que já vem com o Windows 10 e 11 | Não é preciso instalar SDKs no PC de desenvolvimento nem no do utilizador. Acabam os `.ps1` escondidos, um padrão que os antivírus marcam. A lógica de escrita fica testável com falhas injetadas |
| Interface | O mesmo `index.html` e `engine.js`, **embutidos no .exe**, abertos numa janela do Edge em modo aplicação (`msedge --app`), ou no browser predefinido se o Edge não existir | Preserva a interface e o motor já testados. Não altera o Windows |
| Comunicação | `HttpListener` só em `127.0.0.1`, porta livre, token de sessão (o C1 de hoje) | É o mesmo modelo, já validado no teu Windows |
| Motor | **Sem alterações.** `engine.js` continua a ser a única fonte dos cálculos | Regra 8. Os testes de regressão byte a byte mantêm-se |
| Surround | **Fora do Beta**: código guardado, nada exposto na interface nem no .exe | Pedido 6 |
| Dados | `Documentos\OZPanorama\` (backups, registo, perfis). O instalador e a desinstalação **nunca** mexem aí | Os backups sobrevivem a atualizações e desinstalações |
| Distribuição | Beta 0.1 = **um único `.exe` portátil**, num zip assinado por hash (SHA-256 publicado). O instalador Inno Setup fica para uma versão seguinte, se quiseres instalá-lo no teu PC | É simples para os viewers e não instala nada. Funciona sem administrador |

## 2. Estados de validação (o que cada jogo pode fazer)

| Jogo | Estado | Aplicar? | Notas |
|---|---|---|---|
| MSFS 2024 | Testado (rig 56°, 15/11) | Sim | Escreve **só** as rotações das janelas que **já existem**. Os monitores ficam como o utilizador os configurou no jogo. A app pergunta qual das janelas está à esquerda |
| ETS2 | Testado (14/11, visual) **no comportamento validado**: `.sii` + `r_multimon_mode 4` | Sim | A escrita de `r_mode_width/height` (C3 de hoje) fica **desligada** até um teste 5760 vs 5838. A app só **avisa** se a resolução do jogo e a do Surround forem diferentes |
| ATS | Testado pelo autor no rig (confirmado a 15/11) | Sim | Mesmo comportamento do ETS2 (`.sii` + `r_multimon_mode 4`; `r_mode` só com aviso) |
| iRacing | Testado pelo autor no rig (confirmado a 15/11) | Sim | Escreve só as 7 chaves do `rendererDX11Monitor.ini` (NumMonitors, MonitorType, RenderViewPerMonitor, MonitorWidth, ScreenWidth, ScreenAngles, ViewingDist); cada uma tem de existir uma vez |

**Âmbito da Beta 0.1 (decisão do autor, 15/11):** só MSFS 2024, Truck Simulator (ETS2/ATS) e iRacing. Assetto Corsa e ACC **saem da Beta** (interface e servidor); o código do motor fica no repositório, coberto pelos testes de regressão, para uma versão futura.

Os estados vêm de uma tabela única no código, com a data e a evidência de cada um. Nada na interface os pode alterar.

## 3. Operação "Aplicar": sequência obrigatória

1. **Identificar.**
   - Caminho do ficheiro, procurado em candidatos conhecidos; se não for encontrado, a operação para e explica porquê.
   - Existência, tamanho e leitura do ficheiro.
   - Codificação (BOM UTF-8 / UTF-16 / sem BOM) e fins de linha.
   - Formato reconhecido: as chaves esperadas, cada uma uma só vez.
   - Valores atuais, SHA-256 e se o processo do jogo está aberto.
   - Teste de escrita: abrir com `FileShare.None` sem escrever, para detetar bloqueios.
2. **Pré-visualizar.**
   - Tabela de chave, valor atual, valor novo e se muda.
   - Ficheiros, simulador e hash lido.
   - Nada é escrito nesta fase.
3. **Confirmar.** "Está prestes a alterar as configurações do [simulador]. Foi criado e validado um backup dos ficheiros originais. Deseja continuar?" Isto implica que o backup é feito **antes** desta pergunta e validado (passo 4 → 3).
4. **Backup.**
   - Pasta `backups/<jogo>/<UTC aaaammdd-hhmmss-fff>_<id>`, criada com `CreateNew`: se já existir, outro id; **nunca sobrescreve**.
   - Cópia byte a byte de cada ficheiro, relida e com o SHA-256 comparado.
   - `manifesto.json`: `schema`, `versaoApp`, `jogo`, `nomeJogo`, `operacao`, `criadoEm` (UTC e local), ficheiros com `caminho`, `existia`, `shaOriginal`, `shaAplicado` (preenchido depois) e `bytes`.
   - Se falhar, a aplicação fica bloqueada.
5. **Verificar a concorrência.** Imediatamente antes de escrever, o SHA-256 atual tem de ser igual ao da pré-visualização. Se for diferente, para e pede nova pré-visualização. O jogo não pode estar aberto: o programa pede para o fechar e **nunca** o fecha.
6. **Escrever.**
   - Os novos bytes saem do **original byte a byte**: só mudam os valores das linhas-alvo, e a codificação, o BOM e os fins de linha mantêm-se.
   - Excepção: o `multimon_config.sii` do ETS2 é inteiro do OZPanorama e regenera-se, como hoje.
   - Validação estrutural antes de gravar: a releitura do texto novo tem de dar exatamente os valores pretendidos, e todas as outras linhas têm de ser idênticas.
   - Ficheiro temporário na **mesma pasta** (`CreateNew`) → `Flush(true)` → releitura → `File.Replace(temp, alvo, null)` (substituição atómica no NTFS). Se o alvo não existir, `File.Move`.
7. **Verificar.**
   - Relê o ficheiro e confirma que o SHA-256 é igual ao planeado.
   - Volta a fazer a validação estrutural.
   - Regista `shaAplicado` no manifesto, com escrita atómica.
8. **Recuperar.** Se o passo 6 ou 7 falhar:
   - repõe os bytes do backup com a mesma escrita atómica e verifica que o SHA-256 é igual ao original;
   - regista no manifesto o estado `falhou-recuperado` ou `falhou-NAO-recuperado`;
   - mostra uma mensagem clara. Se não recuperou, dá instruções manuais com o caminho do backup e do ficheiro.

A operação só aparece como concluída depois de o passo 7 passar.

## 4. Backups e Restauro

- **Lista:** jogo, data e hora local, versão da app, ficheiros e estado.
- **Ao escolher restaurar, o programa compara o ficheiro atual:**
  - igual a `shaAplicado` → **"Aplicado pelo OZPanorama, sem alterações"** → restauro normal;
  - igual a `shaOriginal` → **"Já está no estado original"** → nada a fazer;
  - outro valor → **"Alterado depois (pelo jogo ou por si)"** → aviso e diferença das chaves conhecidas (atual vs backup). Para continuar, é preciso marcar "Substituir as alterações posteriores".
- **Antes de restaurar:** backup do estado atual (`operacao: "antes-de-restaurar"`).
- **Restaurar:** a mesma escrita atómica, verificada contra `shaOriginal`.
- **Backup inválido** (hash da cópia não confere, ou manifesto ilegível): **não restaura**. Abre a recuperação assistida: abre a pasta do backup e explica que ficheiro copiar para onde.
- **Ficheiro que não existia antes:** o restauro **move-o** para dentro do backup (não apaga), e o estado fica "como antes".
- **Nada apaga backups.** Limpar backups fica para versões futuras, sempre com confirmação.

## 5. Diagnóstico e feedback
- **"Reportar problema":**
  - gera um texto **para o utilizador rever e editar**: versão, Windows, simulador, resoluções, medidas, monitores (se quiser), estado da última operação e erros;
  - sem ficheiros, sem caminhos pessoais (`C:\Users\<nome>` vira `%USERPROFILE%`) e sem backups;
  - botões **Copiar** e **Abrir no GitHub**: abre `github.com/OptimusZuka/OZPanorama/issues/new` com o texto. O GitHub pede login, e a app avisa disso antes.
- **Modelo de issue** (`.github/ISSUE_TEMPLATE/problema.yml`).

## 6. Testes obrigatórios (Etapa 4)
Correm num **executável de testes** (`OZPanorama.Tests.exe`), no Windows, numa pasta temporária. Nunca tocam nos ficheiros dos jogos. As falhas são injetadas por pontos de teste no código: falha a meio da escrita, disco cheio simulado, ficheiro bloqueado, hash alterado entre leitura e escrita, backup corrompido.
- **A:** aplicação normal, ficheiro inexistente, bloqueado, inválido, sem permissões, interrupção a meio da escrita, falha na verificação, alteração externa entre leitura e escrita, jogo aberto.
- **B:** backup correto, backups consecutivos (incluindo vários no mesmo segundo), preservação dos antigos, simulador certo, integridade, recuperação.
- **C:** restauro sem alterações, depois de o jogo alterar o ficheiro, depois de nova aplicação, com backup inválido, com falha a meio, preservação dos backups.
- **Codificação:** UTF-8 com e sem BOM, UTF-16, CRLF e LF, caracteres acentuados: no fim, os bytes têm de bater certo fora das linhas-alvo.
- **No jogo** (manual, pelo utilizador): MSFS e ETS2 com o .exe final.

## 7. O que exige o PC de desenvolvimento (e o teu aval)
- **Compilar e correr os testes:** com o `csc.exe` do Windows, numa pasta própria (`Ambiente de trabalho\OZPanorama\build`). Não instala nada.
- **Testar no jogo** com o `.exe` final: MSFS e ETS2.
- **Segundo PC:** não existe. A proposta é um **teste fechado** com 2–3 viewers antes de fixar o link nos vídeos.
- **Assinatura digital:** tem custo (certificado OV, ou Azure Trusted Signing por cerca de 10 USD/mês). Sem ela, o SmartScreen mostra "Editor desconhecido". Isto fica documentado no README.
