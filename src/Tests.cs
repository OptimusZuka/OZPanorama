// OZPanorama · Zuka Force — testes do núcleo seguro (Etapa 4). Corre em pastas temporárias; não toca nos jogos reais.
// Compilar: build.cmd (gera build\OZPanorama.Tests.exe). Saída: lista PASSOU/FALHOU e código 0 só se tudo passar.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace OZPanorama.Tests
{
    public static class TestMain
    {
        static int pass, fail; static string Root;
        static readonly List<string> Failures = new List<string>();

        static void Check(bool cond, string name) { if (cond) { pass++; Console.WriteLine("PASSOU  " + name); } else { fail++; Failures.Add(name); Console.WriteLine("FALHOU  " + name); } }
        static void Throws(string code, Action a, string name)
        {
            try { a(); Check(false, name + " (não lançou erro)"); }
            catch (OzError e) { Check(e.Code == code, name + (e.Code == code ? "" : " (código " + e.Code + ")")); }
            catch (Exception e) { Check(false, name + " (" + e.GetType().Name + ": " + e.Message + ")"); }
        }
        static string Sha(string p) { return Hash.Sha256(File.ReadAllBytes(p)); }

        // UserCfg.opt de exemplo (estrutura real, valores neutros). Linhas que não são do OZPanorama têm de ficar intactas.
        static string MsfsSample(string eol)
        {
            var l = new[] {
                "Version 52", "{Graphics", "\tMonitor 2", "\tWindowed 0", "\tFullscreenBorderless 1", "\tWindowCustom0.Monitor 1", "\tWindowCustom0.WindowActive 1",
                "\tWindowCustom0.Windowed 0", "\tWindowCustom0.FullscreenBorderless 1", "\tWindowCustom0.CameraDegRotationAdd 0.000000 -10.000000 0.500000",
                "\tWindowCustom1.Monitor 3", "\tWindowCustom1.WindowActive 1", "\tWindowCustom1.Windowed 0", "\tWindowCustom1.FullscreenBorderless 1",
                "\tWindowCustom1.CameraDegRotationAdd 0.000000 10.000000 0.000000", "\tComentário çãé “aspas”", "}" };
            return string.Join(eol, l) + eol;
        }
        static Dictionary<string, object> MsfsValues(double l, double r, string leftWin) { return new Dictionary<string, object> { { "rotLeft", l }, { "rotRight", r }, { "leftWindow", leftWin } }; }

        static Msfs2024 Msfs(string file) { var g = (Msfs2024)Registry.Get("msfs"); g.OverridePath = file; return g; }
        static ScsGame Ets(string docs) { var g = (ScsGame)Registry.Get("ets2"); g.Root = docs; g.ScreensProvider = () => new List<int[]> { new[] { 5760, 1080, 0, 0, 1 } }; return g; }
        const string Sii = "SiiNunit\n{\nmultimon_config : _nameless.ozp.view {\n normalized_ui_x: 0.000000\n normalized_ui_width: 1.000000\n monitors: 1\n monitors[0]: _nameless.ozp.mon0\n}\n\nmonitor_config : _nameless.ozp.mon0 {\n name: center\n heading_offset: -55.873000\n camera_space_offset: (0.000000, 0.000000, 0.000000)\n render_interior: true\n}\n}";

        // Fluxo completo da interface: pré-visualização → backup → confirmação → aplicar.
        static Ops.ApplyResult Go(string game, Dictionary<string, object> v)
        {
            var pv = Ops.MakePreview(game, v); Ops.Prepare(pv.Id); return Ops.Apply(pv.Id, true);
        }
        static Ops.ApplyResult Go(Preview pv) { Ops.Prepare(pv.Id); return Ops.Apply(pv.Id, true); }

        static string Fresh(string name) { var d = Path.Combine(Root, name); Directory.CreateDirectory(d); return d; }

        public static int Main()
        {
            Root = Path.Combine(Path.GetTempPath(), "ozp_testes_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(Root);
            AppInfo.DataDir = Path.Combine(Root, "dados"); BackupStore.Root = Path.Combine(AppInfo.DataDir, "backups"); AppInfo.LocalDir = Path.Combine(Root, "local");
            Console.OutputEncoding = Encoding.UTF8;
            try { Run(); }
            catch (Exception e) { fail++; Console.WriteLine("ERRO INESPERADO " + e); }
            Console.WriteLine();
            Console.WriteLine(pass + " passaram, " + fail + " falharam. Pasta de teste: " + Root);
            foreach (var f in Failures) Console.WriteLine("  - " + f);
            return fail == 0 ? 0 : 1;
        }

        static void Run()
        {
            // ---------- Codificação e fins de linha preservados ----------
            var encs = new[] {
                new { n = "UTF-8 sem BOM CRLF", e = (Encoding)new UTF8Encoding(false), eol = "\r\n" },
                new { n = "UTF-8 com BOM CRLF", e = (Encoding)new UTF8Encoding(true), eol = "\r\n" },
                new { n = "UTF-8 LF", e = (Encoding)new UTF8Encoding(false), eol = "\n" },
                new { n = "Windows-1252 CRLF", e = Encoding.GetEncoding(1252), eol = "\r\n" },
                new { n = "UTF-16 LE CRLF", e = (Encoding)new UnicodeEncoding(false, true), eol = "\r\n" } };
            foreach (var c in encs)
            {
                var d = Fresh("enc_" + c.n.Replace(' ', '_')); var f = Path.Combine(d, "UserCfg.opt");
                var original = c.e.GetPreamble().Concat(c.e.GetBytes(MsfsSample(c.eol))).ToArray();
                File.WriteAllBytes(f, original); Msfs(f);
                var pv = Ops.MakePreview("msfs", MsfsValues(31.5, 31.5, "0"));
                var r = Go(pv);
                var after = File.ReadAllBytes(f);
                var expect = c.e.GetPreamble().Concat(c.e.GetBytes(MsfsSample(c.eol)
                    .Replace("CameraDegRotationAdd 0.000000 -10.000000 0.500000", "CameraDegRotationAdd 0.000000 -31.500000 0.500000")
                    .Replace("CameraDegRotationAdd 0.000000 10.000000 0.000000", "CameraDegRotationAdd 0.000000 31.500000 0.000000"))).ToArray();
                Check(r.Ok && Hash.Sha256(after) == Hash.Sha256(expect), "A/" + c.n + ": só as rotações mudam, bytes restantes iguais");
                Check(r.Backup != null && Hash.Sha256(File.ReadAllBytes(r.Backup.Files[0].Copy)) == Hash.Sha256(original), "A/" + c.n + ": backup igual ao original");
            }

            // ---------- Janela esquerda trocada ----------
            {
                var f = Path.Combine(Fresh("swap"), "UserCfg.opt"); File.WriteAllText(f, MsfsSample("\r\n")); Msfs(f);
                Go("msfs", MsfsValues(20, 25, "1"));
                var t = File.ReadAllText(f);
                Check(t.Contains("WindowCustom1.CameraDegRotationAdd 0.000000 -20.000000 0.000000") && t.Contains("WindowCustom0.CameraDegRotationAdd 0.000000 25.000000 0.500000"), "A/MSFS: janela da esquerda escolhida pelo utilizador");
                Check(t.Contains("WindowCustom0.Monitor 1") && t.Contains("WindowCustom1.Monitor 3"), "A/MSFS: números dos monitores não são alterados");
            }

            // ---------- Dois monitores: uma janela extra ----------
            {
                var f = Path.Combine(Fresh("one"), "UserCfg.opt"); File.WriteAllText(f, MsfsSample("\r\n")); Msfs(f);
                var pv = Ops.MakePreview("msfs", new Dictionary<string, object> { { "windows", "1" }, { "side", "right" }, { "rotRight", 30.0 } });
                Check(pv.Plan.Warnings.Any(w => w.Contains("mais de uma janela extra")), "A/MSFS 2 monitores: avisa que há outra janela extra");
                Go(pv);
                var t = File.ReadAllText(f);
                Check(t.Contains("WindowCustom0.CameraDegRotationAdd 0.000000 30.000000 0.500000") && t.Contains("WindowCustom1.CameraDegRotationAdd 0.000000 10.000000 0.000000"), "A/MSFS 2 monitores: só a janela 1 muda, para a direita");
                File.WriteAllText(f, MsfsSample("\r\n"));
                Go("msfs", new Dictionary<string, object> { { "windows", "1" }, { "side", "left" }, { "rotLeft", 30.0 } });
                Check(File.ReadAllText(f).Contains("WindowCustom0.CameraDegRotationAdd 0.000000 -30.000000 0.500000"), "A/MSFS 2 monitores: à esquerda a rotação é negativa");
                var bad = false; try { Ops.MakePreview("msfs", new Dictionary<string, object> { { "windows", "1" }, { "side", "x" }, { "rotLeft", 30.0 } }); } catch (OzError) { bad = true; }
                Check(bad, "A/MSFS 2 monitores: lado inválido recusado");
            }

            // ---------- Recusas sem escrita ----------
            {
                var d = Fresh("recusas");
                var f1 = Path.Combine(d, "dup.opt"); File.WriteAllText(f1, MsfsSample("\r\n") + "\tWindowCustom0.CameraDegRotationAdd 0 1 0\r\n"); Msfs(f1);
                var h1 = Sha(f1); Throws("format", () => Ops.MakePreview("msfs", MsfsValues(30, 30, "0")), "B/chave repetida recusada"); Check(Sha(f1) == h1, "B/chave repetida: ficheiro intacto");
                var f2 = Path.Combine(d, "sem.opt"); File.WriteAllText(f2, "Version 52\r\n{Graphics\r\n\tMonitor 1\r\n}\r\n"); Msfs(f2);
                Throws("msfs-windows", () => Ops.MakePreview("msfs", MsfsValues(30, 30, "0")), "B/MSFS sem janelas extra recusado");
                var f3 = Path.Combine(d, "x.opt"); File.WriteAllText(f3, MsfsSample("\r\n")); Msfs(f3);
                Throws("input", () => Ops.MakePreview("msfs", MsfsValues(120, 30, "0")), "B/rotação fora do intervalo recusada");
                Throws("input", () => Ops.MakePreview("msfs", MsfsValues(30, 30, "2")), "B/janela esquerda inválida recusada");
                var pv = Ops.MakePreview("msfs", MsfsValues(30, 30, "0"));
                Throws("confirm", () => Ops.Apply(pv.Id, false), "B/aplicar sem confirmação recusado");
                Ops.Cancel(pv.Id);
                Throws("stale", () => Ops.Apply("inexistente", true), "B/pré-visualização desconhecida recusada");
                Check(File.ReadAllText(f3) == MsfsSample("\r\n"), "B/recusas: ficheiro intacto");
            }

            // ---------- Alteração concorrente ----------
            {
                var f = Path.Combine(Fresh("concorrente"), "UserCfg.opt"); File.WriteAllText(f, MsfsSample("\r\n")); Msfs(f);
                var pv = Ops.MakePreview("msfs", MsfsValues(30, 30, "0"));
                File.AppendAllText(f, "\tNovaLinha 1\r\n"); var h = Sha(f);
                Throws("changed", () => Ops.Prepare(pv.Id), "B/ficheiro mudado depois da pré-visualização: recusado");
                Check(Sha(f) == h, "B/concorrente: alteração alheia preservada");
                pv = Ops.MakePreview("msfs", MsfsValues(30, 30, "0")); var bk = Ops.Prepare(pv.Id);
                File.AppendAllText(f, "\tOutra 2\r\n"); h = Sha(f);
                Throws("changed", () => Ops.Apply(pv.Id, true), "B/ficheiro mudado depois do backup e antes da confirmação: recusado");
                Check(Sha(f) == h && BackupStore.Find(bk.Id).Status == "cancelado-alterado", "B/concorrente após backup: alteração alheia preservada, backup guardado");
                Throws("stale", () => Ops.Apply(pv.Id, true), "B/pré-visualização só serve uma vez");
                pv = Ops.MakePreview("msfs", MsfsValues(30, 30, "0"));
                Throws("no-backup", () => Ops.Apply(pv.Id, true), "B/aplicar sem backup preparado recusado");
                bk = Ops.Prepare(pv.Id); Ops.Cancel(pv.Id);
                Check(Sha(f) == h && BackupStore.Find(bk.Id).Status == "cancelado" && File.Exists(bk.Files[0].Copy), "B/resposta 'não': nada escrito, backup guardado e marcado");
            }

            // ---------- Falhas injetadas: o original fica sempre válido ----------
            foreach (var point in new[] { "tmp-write", "replace", "after-replace", "verify-tamper" })
            {
                var d = Fresh("falha_" + point); var f = Path.Combine(d, "UserCfg.opt"); File.WriteAllText(f, MsfsSample("\r\n")); Msfs(f);
                var h = Sha(f); var pv = Ops.MakePreview("msfs", MsfsValues(30, 30, "0"));
                Ops.Prepare(pv.Id);
                Faults.Active = point; Ops.ApplyResult r;
                try { r = Ops.Apply(pv.Id, true); } finally { Faults.Active = null; }
                Check(!r.Ok && r.Status == "falhou-reposto", "C/falha em " + point + ": reportada e reposta");
                Check(Sha(f) == h, "C/falha em " + point + ": original intacto");
                Check(Directory.GetFiles(d).Length == 1, "C/falha em " + point + ": sem temporários deixados");
            }
            // OZP-02: o ficheiro muda (o jogo gravou) entre a confirmação e a primeira escrita: nada é escrito e a
            // mensagem não manda restaurar (o que apagaria a alteração do outro programa).
            {
                var d = Fresh("antes_primeira"); var f = Path.Combine(d, "UserCfg.opt"); File.WriteAllText(f, MsfsSample("\r\n")); Msfs(f);
                var pv = Ops.MakePreview("msfs", MsfsValues(30, 30, "0")); Ops.Prepare(pv.Id);
                var alheio = MsfsSample("\r\n") + "\r\n// gravado pelo jogo\r\n";
                Faults.OnPoint = pt => { if (pt == "before-first-write") { Faults.OnPoint = null; File.WriteAllText(f, alheio); } };
                Ops.ApplyResult r; try { r = Ops.Apply(pv.Id, true); } finally { Faults.OnPoint = null; }
                Check(!r.Ok && r.Status == "falhou-nada-escrito" && File.ReadAllText(f) == alheio, "B/OZP-02: alterado antes da primeira escrita: nada escrito, alteração alheia intacta");
                Check(!r.Message.Contains("Backups e Restauro") && BackupStore.Find(r.Backup.Id).Status == "falhou-nada-escrito", "B/OZP-02: mensagem não manda restaurar; backup marcado");
            }
            // OZP-01: com "esconder atrás das molduras" o valor do MSFS passa de 90 (ex.: 91,52 no rig de referência).
            {
                var d = Fresh("msfs_acima_90"); var f = Path.Combine(d, "UserCfg.opt"); File.WriteAllText(f, MsfsSample("\r\n")); Msfs(f);
                var r = Go("msfs", MsfsValues(91.52, 91.52, "0"));
                Check(r.Ok, "A/OZP-01: MSFS aceita os valores acima de 90 que o motor calcula");
                Throws("input", () => Ops.MakePreview("msfs", MsfsValues(181, 30, "0")), "B/OZP-01: acima de 180 continua recusado");
            }
            {
                var d = Fresh("falha_backup"); var f = Path.Combine(d, "UserCfg.opt"); File.WriteAllText(f, MsfsSample("\r\n")); Msfs(f);
                var h = Sha(f); var pv = Ops.MakePreview("msfs", MsfsValues(30, 30, "0"));
                Faults.Active = "backup-copy";
                try { Ops.Prepare(pv.Id); Check(false, "C/falha no backup: devia parar"); } catch (IOException) { Check(true, "C/falha no backup: para antes de escrever"); } finally { Faults.Active = null; }
                Check(Sha(f) == h, "C/falha no backup: original intacto");
            }
            {
                var d = Fresh("so_leitura"); var f = Path.Combine(d, "UserCfg.opt"); File.WriteAllText(f, MsfsSample("\r\n")); Msfs(f);
                var pv = Ops.MakePreview("msfs", MsfsValues(30, 30, "0"));
                File.SetAttributes(f, FileAttributes.ReadOnly);
                try { Throws("perm", () => Ops.Prepare(pv.Id), "C/ficheiro só de leitura: recusado com mensagem"); } finally { File.SetAttributes(f, FileAttributes.Normal); }
                Msfs(f); pv = Ops.MakePreview("msfs", MsfsValues(30, 30, "0"));
                using (new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None))
                    Throws("locked", () => Ops.Prepare(pv.Id), "C/ficheiro bloqueado por outro programa: recusado");
            }

            // ---------- ETS2 ----------
            {
                var docs = Fresh("docs_ets2"); var dir = Path.Combine(docs, "Euro Truck Simulator 2"); Directory.CreateDirectory(dir);
                var cfg = Path.Combine(dir, "config.cfg");
                var cfgText = "# prism3d variable config data\r\nuset g_lang \"pt_pt\"\r\nuset r_mode_width \"5838\"\r\nuset r_mode_height \"1080\"\r\nuset r_multimon_mode \"0\"\r\nuset s_volume \"0.8\"";   // sem fim de linha no fim
                File.WriteAllText(cfg, cfgText, Encoding.GetEncoding(1252)); var g = Ets(docs);
                var vals = new Dictionary<string, object> { { "sii", Sii }, { "width", 5760 }, { "height", 1080 } };
                var pv = Ops.MakePreview("ets2", vals);
                Check(pv.Plan.Warnings.Any(w => w.Contains("5838×1080")), "A/ETS2: aviso de resolução diferente (sem a alterar)");
                var r = Go(pv);
                var t = File.ReadAllText(cfg, Encoding.GetEncoding(1252));
                Check(r.Ok && t == cfgText.Replace("r_multimon_mode \"0\"", "r_multimon_mode \"4\""), "A/ETS2: só r_multimon_mode muda; r_mode_width fica 5838");
                Check(File.ReadAllText(Path.Combine(dir, "multimon_config.sii")) == Sii.Replace("\n", "\r\n") + "\r\n", "A/ETS2: multimon_config.sii criado");
                Check(r.Backup.Files.Any(x => !x.Existed && x.Original.EndsWith("multimon_config.sii")), "A/ETS2: backup regista que o .sii não existia");

                var cfg2Text = "uset g_lang \"pt_pt\"";
                File.WriteAllText(cfg, cfg2Text); g = Ets(docs);
                Go("ets2", vals);
                Check(File.ReadAllText(cfg) == cfg2Text + "\n" + "uset r_multimon_mode \"4\"\n", "A/ETS2: r_multimon_mode acrescentado quando falta");
            }

            // ---------- ATS (mesmo comportamento do ETS2) ----------
            {
                var docs = Fresh("docs_ats"); var dir = Path.Combine(docs, "American Truck Simulator"); Directory.CreateDirectory(dir);
                var cfg = Path.Combine(dir, "config.cfg"); File.WriteAllText(cfg, "uset r_multimon_mode \"0\"\r\nuset r_mode_width \"5760\"\r\nuset r_mode_height \"1080\"\r\n");
                var g = (ScsGame)Registry.Get("ats"); g.Root = docs; g.ScreensProvider = () => new List<int[]> { new[] { 5760, 1080, 0, 0, 1 } };
                var pv = Ops.MakePreview("ats", new Dictionary<string, object> { { "sii", Sii }, { "width", 5760 }, { "height", 1080 } });
                Check(pv.Plan.Warnings.Count == 0, "A/ATS: sem avisos quando a resolução bate certo");
                var r = Go(pv);
                Check(r.Ok && File.ReadAllText(cfg) == "uset r_multimon_mode \"4\"\r\nuset r_mode_width \"5760\"\r\nuset r_mode_height \"1080\"\r\n", "A/ATS: só r_multimon_mode muda");
            }

            // ---------- iRacing ----------
            {
                var docs = Fresh("docs_iracing"); var dir = Path.Combine(docs, "iRacing"); Directory.CreateDirectory(dir);
                var f = Path.Combine(dir, "rendererDX11Monitor.ini");
                var ini = "[Monitor]\r\nNumMonitors=1\r\nMonitorType=0\r\nRenderViewPerMonitor=0\r\nMonitorWidth=600 ; mm\r\nScreenWidth=600\r\nScreenAngles=0\r\nViewingDist=800\r\nOutraChave=7\r\n";
                File.WriteAllText(f, ini); ((IRacing)Registry.Get("iracing")).Root = docs;
                var vals = new Dictionary<string, object> { { "NumMonitors", 3 }, { "MonitorType", 0 }, { "RenderViewPerMonitor", 1 }, { "MonitorWidth", 608 }, { "ScreenWidth", 608 }, { "ScreenAngles", 56 }, { "ViewingDist", 710 } };
                var r = Go("iracing", vals);
                Check(r.Ok && File.ReadAllText(f) == "[Monitor]\r\nNumMonitors=3\r\nMonitorType=0\r\nRenderViewPerMonitor=1\r\nMonitorWidth=608 ; mm\r\nScreenWidth=608\r\nScreenAngles=56\r\nViewingDist=710\r\nOutraChave=7\r\n", "A/iRacing: só as 7 chaves mudam, comentários e outras chaves intactos");
                vals["ScreenAngles"] = 120;
                Throws("input", () => Ops.MakePreview("iracing", vals), "B/iRacing: ângulo fora do intervalo recusado");
                File.WriteAllText(f, "NumMonitors=1\r\n"); ((IRacing)Registry.Get("iracing")).Root = docs; vals["ScreenAngles"] = 56;
                Throws("format", () => Ops.MakePreview("iracing", vals), "B/iRacing: ficheiro sem as chaves recusado");
            }

            // ---------- RaceRoom ----------
            {
                var docs = Fresh("docs_r3e"); var dir = Path.Combine(docs, "My Games", "SimBin", "RaceRoom Racing Experience", "UserData"); Directory.CreateDirectory(dir);
                var f = Path.Combine(dir, "graphics_options.xml");
                Func<string, string, string, string> L = (k, t, val) => "  <" + k + " type=\"" + t + "\">" + val + "</" + k + ">\r\n";
                Func<string, string, string, string, string, string, string> X = (dist, size, bez, ang, tri, sep) =>
                    "<game__GraphicsOptions ID=\"/options/graphics\">\r\n" + L("screenWidth", "uint32", "5838") + L("fovScale", "float32", "1.00000000") +
                    L("advancedFov", "bool", tri) + L("distanceToScreen", "float32", dist) + L("eyeHeightOffset", "float32", "0.00000000") +
                    L("screenSizeMiddle", "float32", size) + L("screenSizeLeft", "float32", size) + L("screenSizeRight", "float32", size) +
                    L("screenBezelMiddle", "float32", bez) + L("screenBezelLeft", "float32", bez) + L("screenBezelRight", "float32", bez) +
                    L("screenAngleLeft", "float32", ang) + L("screenAngleRight", "float32", ang) + L("tripleScreen", "bool", tri) +
                    L("tripleScreenAdvancedCulling", "bool", "true") + L("tripleScreenSeparateSizes", "bool", sep) + "</game__GraphicsOptions>\r\n";
                File.WriteAllText(f, X("80.00000000", "69.77307129", "0.00000000", "20.00000000", "false", "false"));
                ((RaceRoom)Registry.Get("raceroom")).Root = docs;
                var vals = new Dictionary<string, object> { { "distanceToScreen", 71.0 }, { "screenSizeMiddle", 68.55 }, { "screenSizeLeft", 68.55 }, { "screenSizeRight", 68.55 },
                    { "screenBezelMiddle", 0.0 }, { "screenBezelLeft", 0.0 }, { "screenBezelRight", 0.0 }, { "screenAngleLeft", 56.0 }, { "screenAngleRight", 56.0 }, { "tripleScreenSeparateSizes", "false" } };
                var r = Go("raceroom", vals);
                Check(r.Ok && File.ReadAllText(f) == X("71.00000000", "68.55000000", "0.00000000", "56.00000000", "true", "false"), "A/RaceRoom: só as chaves de triplos mudam, formato e CRLF intactos");
                vals["screenAngleLeft"] = 95.0;
                Throws("input", () => Ops.MakePreview("raceroom", vals), "B/RaceRoom: ângulo fora do intervalo recusado");
                File.WriteAllText(f, "<game__GraphicsOptions ID=\"/options/graphics\">\r\n</game__GraphicsOptions>\r\n"); vals["screenAngleLeft"] = 56.0;
                Throws("format", () => Ops.MakePreview("raceroom", vals), "B/RaceRoom: ficheiro sem as chaves recusado");
            }

            // ---------- Le Mans Ultimate ----------
            {
                var inst = Fresh("lmu_install"); var ud = Path.Combine(inst, "UserData"); Directory.CreateDirectory(ud);
                var cfg = Path.Combine(ud, "Config_DX11.ini");
                // Estrutura do ficheiro gerado pelo jogo no primeiro arranque (CRLF, cabeçalho antes da secção).
                Func<string, string, string, string, string, string> C = (sv, us, vp, lv, rv) =>
                    "//[[gMa1.002f (c)2016    ]] [[            ]]\r\n[COMPONENTS]\r\nVideoResW=5870\r\nVideoResH=1080\r\nWindowedMode=0\r\nBorderless=0\r\nSubViews=" + sv + "\r\nUseSubViewParams=" + us + "\r\nWorkaroundFlags=0\r\n" +
                    "ViewParams=(" + vp + ") // screen width (m), screen height (m), eye distance (m), side angle (deg), bezel gap (m)\r\nLeftView=(" + lv + ")\r\nRightView=(" + rv + ")\r\nCustomVidRes=(0, 0)\r\n\r\n[INSTALL]\r\nInstallFrom=\r\n";
                const string D0 = "0.540, 0.340, 0.600, 15.000, 0.037";
                File.WriteAllBytes(cfg, Encoding.ASCII.GetBytes(C("0", "1", D0, D0, D0)));
                var g = (LeMansUltimate)Registry.Get("lmu"); g.InstallProvider = () => inst; g.ScreensProvider = () => new List<int[]> { new[] { 5870, 1080, 0, 0, 1 } };
                const string V = "0.606, 0.335, 0.710, 55.300, 0.000";
                var vals = new Dictionary<string, object> { { "ViewParams", V }, { "LeftView", V }, { "RightView", V }, { "width", 5870 }, { "height", 1080 } };
                var r = Go("lmu", vals);
                Check(r.Ok && File.ReadAllText(cfg) == C("2", "2", V, V, V), "A/LMU: SubViews, UseSubViewParams e as três vistas escritos; comentário e resto intactos");
                var rs = Ops.Restore(r.Backup.Id, true, false);
                Check(rs.Ok && File.ReadAllText(cfg) == C("0", "1", D0, D0, D0), "A/LMU: restauro repõe o original");
                foreach (var bad in new[] { "0.606, 0.335, 0.710, 95.000, 0.000", "0.606,0.335,0.710,55.300,0.000", "0.606, 0.335, 0.710, 55.300", "9.000, 0.335, 0.710, 55.300, 0.000", "0.606, 0.335, 0.710, 55.300, 0.500", "" })
                    Throws("input", () => Ops.MakePreview("lmu", new Dictionary<string, object> { { "ViewParams", bad }, { "LeftView", V }, { "RightView", V } }), "B/LMU: vista inválida recusada");
                File.WriteAllText(cfg, "[COMPONENTS]\r\nSubViews=0\r\nViewParams=(" + D0 + ")\r\n");
                Throws("format", () => Ops.MakePreview("lmu", vals), "B/LMU: ficheiro sem LeftView/RightView recusado");
                g.InstallProvider = () => null;
                Throws("missing", () => Ops.MakePreview("lmu", vals), "B/LMU: sem instalação recusado");
                g.InstallProvider = () => inst; File.Delete(cfg);
                Throws("missing", () => Ops.MakePreview("lmu", vals), "B/LMU: sem Config_DX11.ini (jogo nunca aberto) recusado");
            }

            // ---------- Assetto Corsa ----------
            {
                var docs = Fresh("docs_ac"); var dir = Path.Combine(docs, "Assetto Corsa", "cfg"); Directory.CreateDirectory(dir);
                var video = Path.Combine(dir, "video.ini"); var tri = Path.Combine(dir, "triple_screen.ini");
                // Estrutura real do video.ini (LF, como o jogo o grava); MODE também aparece noutra secção para testar a regra por secção.
                Func<string, string> V = mode => "[ASSETTOCORSA]\nHIDE_ARMS=1\n\n[CAMERA]\nMODE=" + mode + "\n\n[FAKE_SECTION]\nMODE=NAO_MEXER\n\n[VIDEO]\nFULLSCREEN=1\nHEIGHT=1080\nWIDTH=5838\n";
                File.WriteAllText(video, V("DEFAULT"), new UTF8Encoding(false));
                var g = (AssettoCorsa)Registry.Get("ac"); g.Root = docs; g.ScreensProvider = () => new List<int[]> { new[] { 5870, 1080, 0, 0, 1 } };
                // Saída do motor (engine.js acPlan) para o rig de referência com 55 px por junção.
                const string T = "[TRIPLE_SCREEN]\nACTIVE=1\n\n[LEFT_SCREEN]\nLEFT_POINT=-0.650160,0.211575\nRIGHT_POINT=-0.303404,0.712355\n\n[CENTER_SCREEN]\nLEFT_POINT=-0.302147,0.710000\nRIGHT_POINT=0.302147,0.710000\n\n[RIGHT_SCREEN]\nLEFT_POINT=0.303404,0.712355\nRIGHT_POINT=0.650160,0.211575\n\n[SPINNERS]\nSP_MARGINS=17\nSP_ROTATION=55\nSP_DISTANCE=710\nSP_WIDTH=593\n";
                var vals = new Dictionary<string, object> { { "triple", T }, { "width", 5870 }, { "height", 1080 } };
                var pv = Ops.MakePreview("ac", vals);
                Check(pv.Plan.Warnings.Any(w => w.Contains("5838×1080")), "A/AC: aviso de resolução do jogo diferente da do Surround (sem a alterar)");
                var r = Go(pv);
                Check(r.Ok && File.ReadAllText(video) == V("TRIPLE"), "A/AC: só [CAMERA] MODE muda; MODE de outra secção e resto intactos");
                Check(File.Exists(tri) && File.ReadAllText(tri) == T.Replace("\n", "\r\n"), "A/AC: triple_screen.ini criado com CRLF");
                Check(r.Backup.Files.Any(f => f.Original == tri && !f.Existed), "A/AC: backup regista que o triple_screen.ini não existia");
                // Ficheiro existente (valores do launcher): substituído e guardado no backup; o restauro repõe-no.
                var old = "[TRIPLE_SCREEN]\r\nACTIVE=1\r\n\r\n[LEFT_SCREEN]\r\nLEFT_POINT=-0.903749,0.339754\r\nRIGHT_POINT=-0.34,0.72\r\n";
                File.WriteAllText(tri, old); File.WriteAllText(video, V("DEFAULT"), new UTF8Encoding(false));
                var r2 = Go("ac", vals);
                Check(r2.Ok && File.ReadAllText(tri) == T.Replace("\n", "\r\n"), "A/AC: triple_screen.ini existente substituído");
                var rs = Ops.Restore(r2.Backup.Id, true, false);
                Check(rs.Ok && File.ReadAllText(tri) == old && File.ReadAllText(video) == V("DEFAULT"), "A/AC: restauro repõe os dois ficheiros originais");
                // OZP-05: o restauro falha entre os dois ficheiros: o primeiro, já restaurado, volta ao estado de antes do restauro.
                {
                    var r3 = Go("ac", vals);
                    Faults.OnPoint = pt => { if (pt == "restore-between-files") { Faults.OnPoint = null; throw new IOException("falha simulada"); } };
                    Ops.RestoreResult rf; try { rf = Ops.Restore(r3.Backup.Id, true, false); } finally { Faults.OnPoint = null; }
                    Check(!rf.Ok && File.ReadAllText(tri) == T.Replace("\n", "\r\n") && File.ReadAllText(video) == V("TRIPLE"), "C/OZP-05: restauro interrompido revertido para o estado de antes");
                    Check(BackupStore.Find(rf.SafetyBackup.Id).Status == "restauro-revertido" && rf.Message.Contains("voltaram"), "C/OZP-05: estado e mensagem do restauro revertido");
                }
                // Conteúdo inválido nunca chega ao disco.
                foreach (var bad in new[] { T.Replace("0.710000", "abc"), T.Replace("SP_WIDTH=593", "SP_WIDTH=593\nEXTRA=1"), T.Replace("[CENTER_SCREEN]", "[OUTRA]"), T.Replace("-0.650160", "-99.000000"), "" })
                    Throws("input", () => Ops.MakePreview("ac", new Dictionary<string, object> { { "triple", bad }, { "width", 5870 }, { "height", 1080 } }), "B/AC: triple_screen.ini inválido recusado");
                File.WriteAllText(video, "[VIDEO]\nWIDTH=5870\n");
                Throws("format", () => Ops.MakePreview("ac", vals), "B/AC: video.ini sem [CAMERA] MODE recusado");
                File.WriteAllText(video, "[CAMERA]\nMODE=DEFAULT\nMODE=TRIPLE\n");
                Throws("format", () => Ops.MakePreview("ac", vals), "B/AC: MODE repetido em [CAMERA] recusado");
            }

            // ---------- Auditoria 0.1.3 ----------
            {
                // M5: só o formato exato do .sii gerado pelo motor é aceite.
                Check(ScsGame.SiiValid(Sii), "M5/.sii gerado aceite");
                Check(!ScsGame.SiiValid(Sii.Replace(" name: center", " name: center\n camera_fov: 1")), "M5/.sii com chave estranha recusado");
                Check(!ScsGame.SiiValid(Sii.Replace("-55.873000", "-55.873000;x")), "M5/.sii com valor não numérico recusado");
                Check(!ScsGame.SiiValid(Sii.Replace("monitor_config : _nameless.ozp.mon0 {", "outra_coisa : x {")), "M5/.sii com bloco estranho recusado");

                // M1: um ficheiro mudado por outro programa a meio da aplicação não é reposto por cima.
                var docs = Fresh("docs_m1"); var dir = Path.Combine(docs, "Euro Truck Simulator 2"); Directory.CreateDirectory(dir);
                var cfg = Path.Combine(dir, "config.cfg"); File.WriteAllText(cfg, "uset r_multimon_mode \"0\"\r\n"); Ets(docs);
                var sii = Path.Combine(dir, "multimon_config.sii");
                var pv = Ops.MakePreview("ets2", new Dictionary<string, object> { { "sii", Sii }, { "width", 5760 }, { "height", 1080 } });
                Ops.Prepare(pv.Id);
                Faults.OnPoint = pt => { if (pt == "between-files") { Faults.OnPoint = null; File.WriteAllText(cfg, "uset r_multimon_mode \"2\"\r\nuset outro \"1\"\r\n"); } };
                var r = Ops.Apply(pv.Id, true); Faults.OnPoint = null;
                Check(!r.Ok && File.ReadAllText(cfg) == "uset r_multimon_mode \"2\"\r\nuset outro \"1\"\r\n", "M1/alteração alheia ao config.cfg preservada");
                Check(!File.Exists(sii), "M1/.sii criado nesta operação foi retirado");
                Check(r.Status == "falhou-sem-reposicao", "M1/estado indica que não houve reposição completa");

                // B4: o restauro só aceita caminhos do próprio jogo e nomes de cópia simples.
                var f = Path.Combine(Fresh("b4"), "UserCfg.opt"); File.WriteAllText(f, MsfsSample("\r\n")); Msfs(f);
                var ok = Go("msfs", MsfsValues(31, 31, "0"));
                var man = Path.Combine(ok.Backup.Dir, "manifesto.json"); var txt = File.ReadAllText(man);
                File.WriteAllText(man, txt.Replace(new JavaScriptSerializer().Serialize(f).Trim('"'), new JavaScriptSerializer().Serialize(Path.Combine(Root, "outro.txt")).Trim('"')));
                Throws("backup", () => Ops.Restore(ok.Backup.Id, true, true), "B4/restauro com caminho alheio recusado");
                File.WriteAllText(man, txt.Replace("\"copia\":\"1_UserCfg.opt\"", "\"copia\":\"..\\\\..\\\\x\""));
                Throws("backup", () => BackupStore.Find(ok.Backup.Id), "B4/nome de cópia com .. recusado");
                File.WriteAllText(man, txt);

                // B5: anonimização sem diferença de maiúsculas.
                var prof = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var an = Report.Anonymize(prof.ToUpperInvariant() + @"\Documents\x.cfg");
                Check(!an.ToUpperInvariant().Contains(prof.ToUpperInvariant()), "B5/perfil anonimizado mesmo com outras maiúsculas");
            }

            // ---------- Backups nunca sobrescritos ----------
            {
                var f = Path.Combine(Fresh("unicos"), "UserCfg.opt"); File.WriteAllText(f, MsfsSample("\r\n")); Msfs(f);
                var ids = new HashSet<string>();
                for (int i = 0; i < 5; i++) ids.Add(Go("msfs", MsfsValues(20 + i, 20 + i, "0")).Backup.Id);
                Check(ids.Count == 5, "A/backups seguidos têm identificadores diferentes");
                Check(BackupStore.List().Count(b => b.Game == "msfs") >= 5, "A/lista de backups lê os manifestos");
            }

            // ---------- Restauro com 3 estados ----------
            {
                var f = Path.Combine(Fresh("restauro"), "UserCfg.opt"); var orig = MsfsSample("\r\n"); File.WriteAllText(f, orig); Msfs(f);
                var r = Go("msfs", MsfsValues(30, 30, "0"));
                var bf = r.Backup.Files[0];
                Check(Ops.FileState(bf) == "aplicado", "C/restauro: estado 'aplicado pelo OZPanorama'");
                var rr = Ops.Restore(r.Backup.Id, true, false);
                Check(rr.Ok && File.ReadAllText(f) == orig, "C/restauro de ficheiro não alterado depois: original reposto");
                Check(Ops.FileState(bf) == "original", "C/restauro: estado 'original'");
                Check(rr.SafetyBackup != null && File.Exists(rr.SafetyBackup.Files[0].Copy), "C/restauro: estado anterior guardado num backup novo");

                var r2 = Go("msfs", MsfsValues(33, 33, "0"));
                File.AppendAllText(f, "\tAlteradoPeloJogo 1\r\n"); var changed = File.ReadAllText(f);
                Check(Ops.FileState(r2.Backup.Files[0]) == "alterado", "C/restauro: estado 'alterado depois'");
                Throws("changed-after", () => Ops.Restore(r2.Backup.Id, true, false), "C/restauro de ficheiro alterado depois exige confirmação extra");
                Check(File.ReadAllText(f) == changed, "C/restauro recusado: alterações posteriores intactas");
                var rr2 = Ops.Restore(r2.Backup.Id, true, true);
                Check(rr2.Ok && File.ReadAllText(f) == orig && File.ReadAllText(rr2.SafetyBackup.Files[0].Copy) == changed, "C/restauro aceite: original reposto e alterações guardadas");
                Throws("confirm", () => Ops.Restore(r2.Backup.Id, false, true), "C/restauro sem confirmação recusado");

                var r3 = Go("msfs", MsfsValues(34, 34, "0"));
                Faults.Active = "restore-write"; Ops.RestoreResult rr3;
                try { rr3 = Ops.Restore(r3.Backup.Id, true, false); } finally { Faults.Active = null; }
                Check(!rr3.Ok && rr3.SafetyBackup != null && Hash.Sha256(File.ReadAllBytes(rr3.SafetyBackup.Files[0].Copy)) == Sha(f), "C/falha no restauro: ficheiro atual igual ao backup de segurança");
                Throws("backup", () => BackupStore.Find("../../Windows"), "C/identificador de backup com caminho recusado");
            }

            // ---------- EDID (detetar monitores, também com Surround) ----------
            {
                var e = new byte[128]; e[8] = 0x22; e[9] = 0xF0; e[10] = 0x34; e[11] = 0x12; e[21] = 60; e[22] = 34;
                e[66] = 597 & 0xFF; e[67] = 336 & 0xFF; e[68] = (byte)(((597 >> 8) << 4) | (336 >> 8));
                e[72 + 3] = 0xFC; var nm = Encoding.ASCII.GetBytes("HP X27c\n"); Array.Copy(nm, 0, e, 72 + 5, nm.Length);
                var p = Edid.Parse(e);
                Check(p != null && (string)p[0] == "HP X27c" && (int)p[1] == 597 && (int)p[2] == 336, "EDID: nome e tamanho em mm lidos");
            }
            // ---------- EDID: dados reais de um PC com Surround (3 monitores no Surround + 1 fora) ----------
            {
                Func<string, byte[]> B = s => Enumerable.Range(0, s.Length / 2).Select(i => Convert.ToByte(s.Substring(i * 2, 2), 16)).ToArray();
                string X128 = "00FFFFFFFFFFFF00220E333701010101231F0104A53C22783B1125AD5061A525165054A10800D1C081C0A9C09500B300810081800101023A801871382D40582C450055502100001E000000FD003CA5D2D232010A202020202020000000FC00485020583237630A2020202020000000FF004F5A505445535430303030300A013D", X256 = "00FFFFFFFFFFFF00220E333701010101231F0104A53C22783B1125AD5061A525165054A10800D1C081C0A9C09500B300810081800101023A801871382D40582C450055502100001E000000FD003CA5D2D232010A202020202020000000FC00485020583237630A2020202020000000FF004F5A505445535430303030300A013D020325F147903F400403020123090707830100006D1A000002013CA5000000000000E2006B089B80A070384D403020350055502100001A5A8780A070384D403020350055502100001A0474801871382D40582C450055502100001E2A4480A0703827403020350055502100001A00000000000000000000000000000000000042", AOC = "00FFFFFFFFFFFF0005E302B40100000001220103803C22782EE335AB5046AA270C5054BFEF00D1C081803168317C4568457C6168617C023A801871382D40582C450056502100001E000000FF004F5A505445535430303030300A000000FC00323747340A2020202020202020000000FD0030B41EC83C000A202020202020012E02034BF14F101F0514041303120211015D5E5F3F23090707830100006D030C002000384420006001020367D85DC401788003E305E301E60607016262006D1A0000020130B4E6000000000057A280A070381E403040350056502100001E8B6F80A0703840403020350056502100001E00000000000000000000000000000000AA", OMEN = "00FFFFFFFFFFFF00220E103B0000000007230104B53C22783B2B15AF4F45A7250F5054A54B00D1C081C0A9C09500B300810081800101023A801871382D40582C450055502100001E000000FD0032B4D7D732010A202020202020000000FC004F4D454E2032372047320A2020000000FF004F5A505445535430303030300A012E020334B14C3F40901F2904130302121101741A0000030132B4000000000000B4000000008D00E305C301E6060501655A16E200EAD09480A070381E403040350055502100001A5A8780A070384D403020350055502100001A44AC80A0703862403020350055502100001E000000000000000000000000000000000000000000CE", HW = "00FFFFFFFFFFFF0022F60224010101011120010380351E782A1DD5A75650A026105054A54B008180A9C0714F010101010101010101012A4480A070382740302035000F282100001A000000FF004F5A505445535430303030300A000000FD00304C0F5512000A202020202020000000FC004855415745492041443830485701E5020321324801139F129004020365030C0010006D1A00000201304BED0000000000023A801871382D40582C45000F282100001E011D007251D01E206E2855000F282100001E8C0AD08A20E02D10103E96000F28210000180000000000000000000000000000000000000000000000000000000000000000000000000000000092", NVS = "00FFFFFFFFFFFF003AD3DE10030101000F17010485B422783B2B15AF4F45A7250F5054200000D1C081C0A9C09500B300810081800101F140D0A072E8263080D03A00085471000014000000FD0032B4D7D732010A202020202020000000FC004E5620537572726F756E640A20000000FF004F5A505445535430303030300A0114";
                Func<bool, List<Edid.Inst>> rig = surround => new List<Edid.Inst> {
                    new Edid.Inst { Id = "AOC", Edid = B(AOC), Address = 0x112, Present = !surround },
                    new Edid.Inst { Id = "X-old1", Edid = B(X128), Address = 0 },
                    new Edid.Inst { Id = "X-old2", Edid = B(X128), Address = 0 },
                    new Edid.Inst { Id = "X", Edid = B(X256), Address = 0x115, Present = !surround },
                    new Edid.Inst { Id = "OMEN", Edid = B(OMEN), Address = 0x113, Present = !surround },
                    new Edid.Inst { Id = "HW", Edid = B(HW), Address = 0x110, Present = true },
                    new Edid.Inst { Id = "NVS", Edid = B(NVS), Address = 0x1404, Present = surround } };
                Func<List<object>, string, int> cnt = (l, n) => l.Cast<Dictionary<string, object>>().Count(m => ((string)m["name"]).Contains(n));
                var s1 = Edid.Group(rig(true), true);
                Check(s1.Count == 4 && cnt(s1, "X27c") == 1 && cnt(s1, "27G4") == 1 && cnt(s1, "OMEN") == 1 && cnt(s1, "Surround") == 0, "EDID/Surround: cada monitor aparece uma vez, sem o ecrã virtual");
                Check(s1.Cast<Dictionary<string, object>>().Count(m => m["present"] == null) == 3, "EDID/Surround: os 3 do Surround ficam com estado desconhecido, não 'desligado'");
                var s2 = Edid.Group(rig(false), false);
                Check(s2.Count == 4 && cnt(s2, "X27c") == 1 && s2.Cast<Dictionary<string, object>>().All(m => (bool?)m["present"] == true), "EDID/sem Surround: 4 ligados, sem repetidos");
                var tri = new List<Edid.Inst> { new Edid.Inst { Edid = B(X256), Address = 0x110 }, new Edid.Inst { Edid = B(X256), Address = 0x111 }, new Edid.Inst { Edid = B(X128), Address = 0 }, new Edid.Inst { Edid = B(X256), Address = 0x112 } };
                var s3 = Edid.Group(tri, true);
                Check(s3.Count == 3 && s3.Cast<Dictionary<string, object>>().Select(m => (string)m["id"]).Distinct().Count() == 3, "EDID/Surround: três monitores iguais sem série continuam a ser três");
            }
            // ---------- Relatório anonimizado ----------
            {
                var s = Report.Anonymize(@"C:\Users\Fulano\Documents\x.cfg erro de fulano@example.com");
                Check(!s.Contains("Fulano") && !s.Contains("@example.com"), "Relatório: caminhos e email anonimizados");
            }
        }
    }
}
