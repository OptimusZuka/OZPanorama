// OZPanorama · Zuka Force — integrações com os simuladores e operações Aplicar / Restaurar (Beta 0.1)
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OZPanorama
{
    /// <summary>Plano de escrita de um ficheiro: regras por linha, ou substituição completa (ficheiro só do OZPanorama).</summary>
    public class FilePlan
    {
        public string Path; public string Label;
        public List<LineRule> Rules = new List<LineRule>();
        public string WholeText;        // != null: o ficheiro inteiro é gerado pelo OZPanorama (ex.: multimon_config.sii)
        public bool MustExist = true;
    }

    public class Plan
    {
        public string Game; public string GameName;
        public List<FilePlan> Files = new List<FilePlan>();
        public List<string> Warnings = new List<string>();
        public List<Dictionary<string, object>> Info = new List<Dictionary<string, object>>();   // dados lidos, só para mostrar
    }

    public abstract class Game
    {
        public string Id, Name, Status;   // Status: "testado" | "experimental" | "nao-validado"
        public string[] Processes = new string[0];
        public bool CanApply { get { return Status == "testado"; } }
        public abstract string[] Paths();
        /// <summary>Lê os ficheiros atuais e constrói o plano. Não escreve nada.</summary>
        public abstract Plan BuildPlan(Dictionary<string, object> values, Dictionary<string, Snapshot> snaps);

        public bool Running()
        {
            foreach (var p in Processes) { var ps = Process.GetProcessesByName(p); bool any = ps.Length > 0; foreach (var x in ps) x.Dispose(); if (any) return true; }
            return false;
        }

        protected static string Str(Dictionary<string, object> v, string k)
        {
            object o; if (v == null || !v.TryGetValue(k, out o) || o == null) throw new OzError("input", "Faltam dados do cálculo (" + k + "). Volte a calcular.");
            return Convert.ToString(o, CultureInfo.InvariantCulture);
        }
        protected static double Num(Dictionary<string, object> v, string k, double min, double max)
        {
            double d;
            if (!double.TryParse(Str(v, k), NumberStyles.Float, CultureInfo.InvariantCulture, out d) || double.IsNaN(d) || d < min || d > max)
                throw new OzError("input", "Valor calculado fora do intervalo permitido (" + k + "). Volte a calcular.");
            return d;
        }
    }

    // ---------------- MSFS 2024 ----------------
    // Validado no rig a 15/11. Na Beta só se escrevem as rotações das duas janelas extra que JÁ EXISTEM.
    // Os números dos monitores (WindowCustomN.Monitor) não são tocados: dependem do PC de cada um (C2).
    public class Msfs2024 : Game
    {
        public static string[] Candidates()
        {
            return new[] {
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft Flight Simulator 2024", "UserCfg.opt"),
                System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", "Microsoft.Limitless_8wekyb3d8bbwe", "LocalCache", "UserCfg.opt") };
        }
        public string OverridePath;   // testes
        public Msfs2024() { Id = "msfs"; Name = "Microsoft Flight Simulator 2024"; Status = "testado"; Processes = new[] { "FlightSimulator2024" }; }

        public override string[] Paths()
        {
            if (OverridePath != null) return new[] { OverridePath };
            foreach (var c in Candidates()) if (File.Exists(c)) return new[] { c };
            return new[] { Candidates()[0] };
        }

        static readonly Regex NumRx = new Regex(@"^-?\d{1,3}(\.\d{1,6})?$");

        static LineRule Rotation(int win, string value)
        {
            var key = "WindowCustom" + win + ".CameraDegRotationAdd";
            return new LineRule
            {
                Key = key,
                Match = new Regex(@"^([ \t]*" + Regex.Escape(key) + @"[ \t]+)(\S+)([ \t]+)(\S+)([ \t]+)(\S+)(.*)$"),
                // Só o 2.º número (rotação horizontal) muda; inclinação, rolamento e espaços ficam como estavam.
                Rewrite = m => m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value + value + m.Groups[5].Value + m.Groups[6].Value + m.Groups[7].Value,
                Current = m => m.Groups[4].Value,
                NewValue = value
            };
        }

        public override Plan BuildPlan(Dictionary<string, object> v, Dictionary<string, Snapshot> snaps)
        {
            var path = Paths()[0]; var s = snaps[path];
            if (!s.Exists) throw new OzError("missing", "Não encontrei o UserCfg.opt do MSFS 2024. Abra o simulador uma vez e saia pelo menu.", path);
            double left = Num(v, "rotLeft", 0, 90), right = Num(v, "rotRight", 0, 90);
            string leftWin = Str(v, "leftWindow");
            if (leftWin != "0" && leftWin != "1") throw new OzError("input", "Indique qual das janelas extra está à esquerda.");
            int lw = leftWin == "0" ? 0 : 1, rw = 1 - lw;
            var text = TextFile.Decode(s.Bytes).Text;

            var p = new Plan { Game = Id, GameName = Name };
            // Estado das janelas, para a pessoa confirmar a esquerda/direita antes de aplicar.
            for (int w = 0; w < 2; w++)
            {
                var info = new Dictionary<string, object> { { "janela", w } };
                foreach (var k in new[] { "Monitor", "WindowActive", "Windowed", "FullscreenBorderless" })
                {
                    var m = Regex.Match(text, @"(?m)^[ \t]*WindowCustom" + w + @"\." + k + @"[ \t]+(\S+)");
                    info[k] = m.Success ? m.Groups[1].Value : null;
                }
                p.Info.Add(info);
                if (info["Monitor"] == null)
                    throw new OzError("msfs-windows", "O MSFS ainda não tem as duas janelas extra. Nas opções de multijanelas do simulador, crie 2 janelas extra, coloque cada uma no seu monitor em ecrã inteiro e saia pelo menu. Nada foi alterado.", "WindowCustom" + w + " em falta");
                if ((string)info["WindowActive"] != "1") p.Warnings.Add("A janela extra " + (w + 1) + " não está ativa no MSFS (WindowActive diferente de 1). O OZPanorama não a ativa; ative-a no simulador.");
            }
            if (Regex.IsMatch(text, @"(?m)^[ \t]*WindowCustom2\.")) p.Warnings.Add("O MSFS tem mais de duas janelas extra. O OZPanorama só ajusta as duas primeiras.");

            var fp = new FilePlan { Path = path, Label = "UserCfg.opt" };
            fp.Rules.Add(Rotation(lw, (-left).ToString("0.000000", CultureInfo.InvariantCulture)));
            fp.Rules.Add(Rotation(rw, right.ToString("0.000000", CultureInfo.InvariantCulture)));
            p.Files.Add(fp);
            return p;
        }
    }

    // ---------------- ETS2 / ATS ----------------
    // ETS2 validado a 14/11 com: multimon_config.sii gerado + r_multimon_mode 4. A escrita de r_mode_width/height
    // (C3) está desligada na Beta: só se avisa quando a resolução do jogo é diferente da do ecrã do Surround.
    public class ScsGame : Game
    {
        public string Folder; public string Root;
        public ScsGame(string id, string name, string folder, string proc, string status)
        { Id = id; Name = name; Folder = folder; Processes = new[] { proc }; Status = status; Root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }
        string Dir { get { return System.IO.Path.Combine(Root, Folder); } }
        public override string[] Paths() { return new[] { System.IO.Path.Combine(Dir, "multimon_config.sii"), System.IO.Path.Combine(Dir, "config.cfg") }; }
        public Func<List<int[]>> ScreensProvider = Displays.Screens;   // [w, h, x, y]

        static string Cvar(string text, string name)
        {
            var m = Regex.Match(text, @"(?m)^\s*uset\s+" + Regex.Escape(name) + @"\s+""([^""]*)""");
            return m.Success ? m.Groups[1].Value : null;
        }

        public override Plan BuildPlan(Dictionary<string, object> v, Dictionary<string, Snapshot> snaps)
        {
            var paths = Paths(); var sii = snaps[paths[0]]; var cfg = snaps[paths[1]];
            if (!Directory.Exists(Dir)) throw new OzError("missing", "Não encontrei a pasta do " + Name + " nos Documentos. Abra o jogo uma vez antes.", Dir);
            if (!cfg.Exists) throw new OzError("missing", "Não encontrei o config.cfg do " + Name + ". Abra o jogo uma vez e saia antes.", paths[1]);
            var siiText = Str(v, "sii").Replace("\r\n", "\n").Trim();
            if (!siiText.StartsWith("SiiNunit") || !siiText.EndsWith("}") || siiText.Length > 20000)
                throw new OzError("input", "O conteúdo calculado do multimon_config.sii não é válido. Volte a calcular.");
            var cfgText = TextFile.Decode(cfg.Bytes).Text;
            var p = new Plan { Game = Id, GameName = Name };

            var full = new FilePlan { Path = paths[0], Label = "multimon_config.sii", WholeText = siiText.Replace("\n", "\r\n") + "\r\n", MustExist = false };
            p.Files.Add(full);
            var fc = new FilePlan { Path = paths[1], Label = "config.cfg" };
            fc.Rules.Add(new LineRule
            {
                Key = "r_multimon_mode",
                Match = new Regex(@"^(\s*uset\s+r_multimon_mode\s+"")([^""]*)("".*)$"),
                Rewrite = m => m.Groups[1].Value + "4" + m.Groups[3].Value,
                Current = m => m.Groups[2].Value, NewValue = "4",
                AppendIfMissing = true, AppendLine = "uset r_multimon_mode \"4\""
            });
            p.Files.Add(fc);

            // Aviso C3 (sem escrita): resolução do jogo vs ecrãs do Windows.
            string w = Cvar(cfgText, "r_mode_width"), h = Cvar(cfgText, "r_mode_height");
            p.Info.Add(new Dictionary<string, object> { { "r_mode_width", w }, { "r_mode_height", h } });
            object ow, oh; int tw, th;
            if (v.TryGetValue("width", out ow) && v.TryGetValue("height", out oh) && int.TryParse(Convert.ToString(ow, CultureInfo.InvariantCulture), out tw) && int.TryParse(Convert.ToString(oh, CultureInfo.InvariantCulture), out th))
            {
                var screens = ScreensProvider();
                var span = screens.FirstOrDefault(x => x[0] == tw && x[1] == th);
                var widest = screens.OrderByDescending(x => x[0]).FirstOrDefault();
                if (span == null && widest != null)
                    p.Warnings.Add("O Windows não tem nenhum ecrã com " + tw + "×" + th + " (o mais largo tem " + widest[0] + "×" + widest[1] + "). Confirme que o Surround está ligado com a correção das molduras.");
                if (w != null && h != null && (w != tw.ToString() || h != th.ToString()))
                    p.Warnings.Add("O jogo está configurado para " + w + "×" + h + " e a área calculada é " + tw + "×" + th + ". Na Beta o OZPanorama não altera a resolução do jogo; se a imagem ficar desalinhada, escolha a resolução certa nas opções do jogo.");
            }
            return p;
        }
    }

    // ---------------- iRacing ----------------
    // Testado pelo autor no rig (confirmado a 15/11). Escreve só as 7 chaves do rendererDX11Monitor.ini;
    // cada uma tem de existir exatamente uma vez (o iRacing cria o ficheiro na primeira execução).
    public class IRacing : Game
    {
        public string Root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        public IRacing() { Id = "iracing"; Name = "iRacing"; Status = "testado"; Processes = new[] { "iRacingSim64DX11", "iRacingSim64" }; }
        public override string[] Paths() { return new[] { System.IO.Path.Combine(Root, "iRacing", "rendererDX11Monitor.ini") }; }

        // chave, mínimo, máximo (inteiros)
        static readonly object[][] Keys = {
            new object[] { "NumMonitors", 3, 3 }, new object[] { "MonitorType", 0, 0 }, new object[] { "RenderViewPerMonitor", 1, 1 },
            new object[] { "MonitorWidth", 100, 3000 }, new object[] { "ScreenWidth", 100, 3000 }, new object[] { "ScreenAngles", 0, 90 },
            new object[] { "ViewingDist", 100, 3000 } };

        public override Plan BuildPlan(Dictionary<string, object> v, Dictionary<string, Snapshot> snaps)
        {
            var path = Paths()[0];
            if (!snaps[path].Exists) throw new OzError("missing", "Não encontrei o rendererDX11Monitor.ini. Abra o iRacing uma vez e saia antes.", path);
            var p = new Plan { Game = Id, GameName = Name };
            var fp = new FilePlan { Path = path, Label = "rendererDX11Monitor.ini" };
            foreach (var k in Keys)
            {
                string key = (string)k[0];
                double d = Num(v, key, (int)k[1], (int)k[2]);
                if (d != Math.Round(d)) throw new OzError("input", "Valor calculado inválido (" + key + "). Volte a calcular.");
                var val = ((int)d).ToString(CultureInfo.InvariantCulture);
                fp.Rules.Add(new LineRule
                {
                    Key = key,
                    // Mantém espaços e comentários depois do valor (ex.: "ScreenAngles=56 ; graus").
                    Match = new Regex(@"^(" + Regex.Escape(key) + @"=)([^\s;]*)(.*)$"),
                    Rewrite = m => m.Groups[1].Value + val + m.Groups[3].Value,
                    Current = m => m.Groups[2].Value, NewValue = val
                });
            }
            p.Files.Add(fp);
            return p;
        }
    }

    /// <summary>Integrações sem aplicação automática na Beta: só pré-visualização/cópia na interface.</summary>
    public class PreviewOnlyGame : Game
    {
        public PreviewOnlyGame(string id, string name) { Id = id; Name = name; Status = "nao-validado"; }
        public override string[] Paths() { return new string[0]; }
        public override Plan BuildPlan(Dictionary<string, object> v, Dictionary<string, Snapshot> s)
        { throw new OzError("not-validated", "A aplicação automática do " + Name + " ainda não foi validada. Nesta Beta pode copiar os valores e colocá-los à mão."); }
    }

    public static class Registry
    {
        public static List<Game> All = new List<Game> {
            new Msfs2024(),
            new ScsGame("ets2", "Euro Truck Simulator 2", "Euro Truck Simulator 2", "eurotrucks2", "testado"),
            new ScsGame("ats", "American Truck Simulator", "American Truck Simulator", "amtrucks", "testado"),   // testado pelo autor no rig (confirmado a 15/11), mesmo comportamento do ETS2
            new IRacing(),
            // AC e ACC ficam fora da Beta 0.1 (pedido do autor: só o que foi testado).
        };
        public static Game Get(string id)
        {
            var g = All.FirstOrDefault(x => x.Id == id);
            if (g == null) throw new OzError("input", "Simulador desconhecido.");
            return g;
        }
    }

    // ---------------- Pré-visualização, aplicação e restauro ----------------

    public class Preview
    {
        public string Id; public Game Game; public Plan Plan; public Dictionary<string, Snapshot> Snaps; public Dictionary<string, byte[]> NewBytes;
        public List<Change> Changes = new List<Change>(); public DateTime At; public Dictionary<string, object> Values; public Backup Backup;
    }

    public static class Ops
    {
        static readonly Dictionary<string, Preview> previews = new Dictionary<string, Preview>();
        static readonly object gate = new object();

        static Dictionary<string, Snapshot> Snap(Game g)
        {
            var d = new Dictionary<string, Snapshot>();
            foreach (var p in g.Paths()) d[p] = Snapshot.Read(p);
            return d;
        }

        /// <summary>Calcula os bytes novos de cada ficheiro e valida-os antes de qualquer escrita.</summary>
        static Dictionary<string, byte[]> Render(Plan plan, Dictionary<string, Snapshot> snaps, List<Change> changes)
        {
            var outp = new Dictionary<string, byte[]>();
            foreach (var f in plan.Files)
            {
                var s = snaps[f.Path];
                if (f.WholeText != null)
                {
                    // Ficheiro inteiro do OZPanorama: mantém a codificação se já existir; novo fica em UTF-8 sem BOM.
                    var tf = s.Exists ? TextFile.Decode(s.Bytes) : null;
                    var bytes = tf != null ? tf.Encode(f.WholeText) : new UTF8Encoding(false).GetBytes(f.WholeText);
                    var cur = tf != null ? tf.Text : null;
                    changes.Add(new Change { File = f.Label, Key = "(ficheiro inteiro)", Current = s.Exists ? (cur.Length + " caracteres") : "(não existe)", New = f.WholeText.Length + " caracteres", Changes = cur != f.WholeText });
                    outp[f.Path] = bytes; continue;
                }
                if (!s.Exists) throw new OzError("missing", "Falta o ficheiro " + f.Label + ".", f.Path);
                var t = TextFile.Decode(s.Bytes);
                var text2 = LinePatcher.Apply(t.Text, f.Rules, f.Label, changes);
                LinePatcher.Validate(t.Text, text2, f.Rules, f.Label);
                outp[f.Path] = t.Encode(text2);
            }
            return outp;
        }

        public static Preview MakePreview(string gameId, Dictionary<string, object> values)
        {
            var g = Registry.Get(gameId);
            if (!g.CanApply) throw new OzError("not-validated", "A aplicação automática do " + g.Name + " ainda não foi validada. Nesta Beta pode copiar os valores e colocá-los à mão.");
            var snaps = Snap(g);
            var plan = g.BuildPlan(values, snaps);
            var pv = new Preview { Id = Guid.NewGuid().ToString("N"), Game = g, Plan = plan, Snaps = snaps, At = DateTime.UtcNow, Values = values };
            pv.NewBytes = Render(plan, snaps, pv.Changes);
            lock (gate) { previews.Clear(); previews[pv.Id] = pv; }   // só a última pré-visualização é válida
            return pv;
        }

        static Preview Get(string id, bool remove)
        {
            lock (gate)
            {
                Preview pv;
                if (id == null || !previews.TryGetValue(id, out pv)) throw new OzError("stale", "A pré-visualização já não é válida. Veja as alterações outra vez antes de aplicar.");
                if ((DateTime.UtcNow - pv.At).TotalMinutes > 15) { previews.Remove(id); throw new OzError("stale", "A pré-visualização expirou. Veja as alterações outra vez antes de aplicar."); }
                if (remove) previews.Remove(id);
                return pv;
            }
        }
        public static Preview Take(string id) { return Get(id, true); }

        static void CheckUnchanged(Preview pv)
        {
            var now = Snap(pv.Game);
            foreach (var kv in pv.Snaps)
                if (now[kv.Key].Exists != kv.Value.Exists || now[kv.Key].Sha != kv.Value.Sha)
                    throw new OzError("changed", "O ficheiro " + System.IO.Path.GetFileName(kv.Key) + " mudou depois da pré-visualização (talvez pelo jogo ou outro programa). Nada foi alterado. Veja as alterações outra vez.", kv.Key);
        }

        /// <summary>Passo "Backup" (antes da confirmação): jogo fechado, ficheiros iguais aos mostrados, permissões, backup validado.
        /// A mensagem de confirmação só aparece depois disto, porque diz que o backup já existe.</summary>
        public static Backup Prepare(string previewId)
        {
            var pv = Get(previewId, false); var g = pv.Game;
            if (pv.Backup != null) return pv.Backup;
            if (g.Running()) throw new OzError("running", "O " + g.Name + " está aberto. Feche-o (saia pelo menu do jogo) e tente outra vez; o jogo pode reescrever a configuração ao sair.");
            CheckUnchanged(pv);
            foreach (var f in pv.Plan.Files) SafeIO.CheckWritable(f.Path);
            pv.Backup = BackupStore.Create(g.Id, g.Name, "aplicar", pv.Plan.Files.Select(f => pv.Snaps[f.Path]).ToList());
            return pv.Backup;
        }

        /// <summary>A pessoa respondeu "não": nada é escrito; o backup fica guardado e marcado (backups nunca são apagados).</summary>
        public static void Cancel(string previewId)
        {
            Preview pv;
            try { pv = Take(previewId); } catch (OzError) { return; }
            if (pv.Backup != null) { pv.Backup.Status = "cancelado"; try { pv.Backup.SaveManifest(); } catch { } }
        }

        public class ApplyResult { public bool Ok; public string Status; public Backup Backup; public string Message; public List<Change> Changes; public List<string> Detail = new List<string>(); }

        /// <summary>Sequência obrigatória: jogo fechado → hash igual ao da pré-visualização → permissões → backup validado →
        /// escrita atómica → verificação → recuperação automática se a verificação falhar.</summary>
        public static ApplyResult Apply(string previewId, bool confirmed)
        {
            if (!confirmed) throw new OzError("confirm", "É preciso confirmar antes de aplicar.");
            var pre = Get(previewId, false);
            if (pre.Backup == null) throw new OzError("no-backup", "Ainda não foi criado o backup desta alteração. Carregue outra vez em Aplicar.");
            var pv = Take(previewId); var g = pv.Game;
            if (g.Running()) throw new OzError("running", "O " + g.Name + " está aberto. Feche-o (saia pelo menu do jogo) e tente outra vez; o jogo pode reescrever a configuração ao sair.");
            // Alterações concorrentes: o ficheiro tem de estar igual ao que foi mostrado e guardado no backup.
            try { CheckUnchanged(pv); }
            catch (OzError) { pv.Backup.Status = "cancelado-alterado"; try { pv.Backup.SaveManifest(); } catch { } throw; }
            var bk = pv.Backup;
            var res = new ApplyResult { Backup = bk, Changes = pv.Changes };
            var written = new List<string>();
            try
            {
                foreach (var f in pv.Plan.Files)
                {
                    // Última confirmação imediatamente antes de substituir.
                    var again = Snapshot.Read(f.Path);
                    if (again.Sha != pv.Snaps[f.Path].Sha) throw new OzError("changed", "O ficheiro " + f.Label + " mudou durante a aplicação.", f.Path);
                    var data = pv.NewBytes[f.Path];
                    if (data.Length == 0 && again.Exists) throw new OzError("verify", "O conteúdo novo ficou vazio; nada foi alterado.", f.Path);
                    SafeIO.AtomicWrite(f.Path, data);
                    written.Add(f.Path);
                    Faults.Hit("after-replace");
                    // 3) Verificação: releitura byte a byte e regras.
                    var back = File.ReadAllBytes(f.Path);
                    if (Faults.Is("verify-tamper")) back = new byte[0];
                    if (Hash.Sha256(back) != Hash.Sha256(data)) throw new OzError("verify", "A releitura do ficheiro " + f.Label + " não confere.", f.Path);
                    var bf = bk.Files.First(x => x.Original == f.Path); bf.ShaApplied = Hash.Sha256(back);
                }
                bk.Status = "aplicado"; bk.SaveManifest();
                res.Ok = true; res.Status = "aplicado"; res.Message = "Configuração aplicada e verificada.";
                AppInfo.Log("aplicar " + g.Id + " ok backup=" + bk.Id);
                return res;
            }
            catch (Exception e)
            {
                // 4) Recuperação automática: repõe os ficheiros a partir do backup que acabámos de validar.
                res.Detail.Add(e.Message);
                var failed = new List<string>();
                foreach (var bf in bk.Files)
                {
                    try
                    {
                        var cur = Snapshot.Read(bf.Original);
                        bool same = bf.Existed ? (cur.Exists && cur.Sha == bf.ShaOriginal) : !cur.Exists;
                        if (same) continue;
                        if (bf.Existed)
                        {
                            var orig = File.ReadAllBytes(bf.Copy);
                            if (Hash.Sha256(orig) != bf.ShaOriginal) throw new IOException("cópia de backup danificada");
                            SafeIO.AtomicWrite(bf.Original, orig);
                            if (Hash.Sha256(File.ReadAllBytes(bf.Original)) != bf.ShaOriginal) throw new IOException("reposição não confere");
                        }
                        else if (written.Contains(bf.Original)) File.Delete(bf.Original);   // ficheiro criado por nós nesta operação
                    }
                    catch (Exception re) { failed.Add(System.IO.Path.GetFileName(bf.Original) + ": " + re.Message); }
                }
                if (failed.Count == 0)
                {
                    bk.Status = "falhou-reposto"; try { bk.SaveManifest(); } catch { }
                    res.Status = "falhou-reposto";
                    res.Message = "Não foi possível aplicar (" + (e is OzError ? e.Message : "erro ao escrever") + "). Os ficheiros originais foram repostos e verificados.";
                }
                else
                {
                    bk.Status = "falhou-sem-reposicao"; try { bk.SaveManifest(); } catch { }
                    res.Status = "falhou-sem-reposicao"; res.Detail.AddRange(failed);
                    res.Message = "Não foi possível aplicar nem repor automaticamente. Os originais estão guardados no backup " + bk.Id + ". Use \"Backups e Restauro\" ou copie os ficheiros da pasta do backup à mão.";
                }
                AppInfo.Log("aplicar " + g.Id + " FALHOU (" + res.Status + "): " + e.Message + (e is OzError ? " | " + ((OzError)e).Detail : ""));
                return res;
            }
        }

        // ---- Restauro ----

        public static string FileState(BackupFile f)
        {
            var cur = Snapshot.Read(f.Original);
            if (!f.Existed) return cur.Exists ? (cur.Sha == f.ShaApplied ? "aplicado" : "alterado") : "original";
            if (!cur.Exists) return "ausente";
            if (cur.Sha == f.ShaOriginal) return "original";
            if (f.ShaApplied != null && cur.Sha == f.ShaApplied) return "aplicado";
            return "alterado";
        }

        public class RestoreResult { public bool Ok; public string Message; public Backup SafetyBackup; public List<string> Detail = new List<string>(); }

        /// <summary>Restaura um backup. Antes de mexer, guarda o estado atual num backup novo.
        /// Ficheiros alterados depois da aplicação só são substituídos com acceptChanged.</summary>
        public static RestoreResult Restore(string backupId, bool confirmed, bool acceptChanged)
        {
            if (!confirmed) throw new OzError("confirm", "É preciso confirmar antes de restaurar.");
            var b = BackupStore.Find(backupId);
            var g = Registry.Get(b.Game);
            if (g.Running()) throw new OzError("running", "O " + g.Name + " está aberto. Feche-o antes de restaurar.");
            var states = b.Files.ToDictionary(f => f.Original, FileState);
            if (states.Values.Any(s => s == "alterado") && !acceptChanged)
                throw new OzError("changed-after", "Pelo menos um ficheiro foi alterado depois do OZPanorama (por si ou pelo jogo). Confirme que quer substituir essas alterações; o estado atual será guardado num backup novo antes.");
            var todo = b.Files.Where(f => states[f.Original] != "original").ToList();
            if (todo.Count == 0) return new RestoreResult { Ok = true, Message = "Os ficheiros já estão iguais ao backup; nada foi alterado." };
            foreach (var f in todo) if (f.Existed) SafeIO.CheckWritable(f.Original);
            foreach (var f in todo) if (f.Existed && Hash.Sha256(File.ReadAllBytes(f.Copy)) != f.ShaOriginal)
                throw new OzError("backup", "A cópia guardada no backup está danificada; nada foi alterado.", f.Copy);

            var safety = BackupStore.Create(g.Id, g.Name, "antes-de-restaurar " + b.Id, todo.Select(f => Snapshot.Read(f.Original)).ToList());
            var r = new RestoreResult { SafetyBackup = safety };
            try
            {
                foreach (var f in todo)
                {
                    if (f.Existed)
                    {
                        var data = File.ReadAllBytes(f.Copy);
                        Faults.Hit("restore-write");
                        SafeIO.AtomicWrite(f.Original, data);
                        if (Hash.Sha256(File.ReadAllBytes(f.Original)) != f.ShaOriginal) throw new OzError("verify", "A releitura depois do restauro não confere.", f.Original);
                    }
                    else if (File.Exists(f.Original))
                    {
                        // O ficheiro não existia antes do OZPanorama: em vez de o apagar, guarda-o no backup de segurança e remove-o.
                        File.Delete(f.Original);
                    }
                }
                foreach (var sf in safety.Files) { var cur = Snapshot.Read(sf.Original); sf.ShaApplied = cur.Sha; }
                safety.Status = "guardado"; safety.SaveManifest();
                r.Ok = true; r.Message = "Backup restaurado e verificado. O estado anterior ficou guardado no backup " + safety.Id + ".";
                AppInfo.Log("restaurar " + b.Id + " ok seguranca=" + safety.Id);
            }
            catch (Exception e)
            {
                r.Ok = false; r.Detail.Add(e.Message);
                r.Message = "O restauro não terminou. O estado anterior está no backup " + safety.Id + "; pode restaurá-lo a partir da lista.";
                AppInfo.Log("restaurar " + b.Id + " FALHOU: " + e.Message);
            }
            return r;
        }
    }
}
