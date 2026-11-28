// OZPanorama · Zuka Force — integrações com os simuladores e operações Aplicar / Restaurar (Beta 0.1.3)
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
        public bool CanApply { get { return Status == "testado" || Status == "experimental"; } }
        public abstract string[] Paths();
        /// <summary>Todos os ficheiros que este jogo pode ter escrito (o restauro só aceita estes caminhos).</summary>
        public virtual string[] AllPaths() { return Paths(); }
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
            // OZP-04: com as instalações Steam e Microsoft Store, usa a que o simulador gravou por último (a que está em uso).
            var found = Candidates().Where(File.Exists).OrderByDescending(c => File.GetLastWriteTimeUtc(c)).ToList();
            return new[] { found.Count > 0 ? found[0] : Candidates()[0] };
        }
        public override string[] AllPaths() { return OverridePath != null ? new[] { OverridePath } : Candidates(); }

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
            double left = Num(v, "rotLeft", 0, 180), right = Num(v, "rotRight", 0, 180);   // > 90 com "esconder atrás das molduras"
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
            if (OverridePath == null && Candidates().Count(File.Exists) > 1)
                p.Warnings.Add("Encontrei o MSFS 2024 instalado no Steam e na Microsoft Store. Vou alterar o que foi usado por último (" + (path.Contains("Packages") ? "Microsoft Store" : "Steam") + "). Se joga na outra versão, abra-a uma vez e saia antes de aplicar.");
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

        // O .sii vem calculado pela interface: só se aceitam as linhas exatas que o motor gera (engine.js scsSii),
        // com números e nomes de ponteiros válidos. Qualquer outra coisa é recusada antes de chegar ao disco.
        static readonly Regex SiiNum = new Regex(@"^-?\d{1,6}(\.\d{1,6})?$");
        static readonly HashSet<string> SiiNumKeys = new HashSet<string> { "normalized_ui_x", "normalized_ui_width", "normalized_x", "normalized_y", "normalized_width", "normalized_height",
            "horizontal_fov_relative_offset", "vertical_fov_relative_offset", "heading_offset", "pitch_offset", "roll_offset", "horizontal_fov_override", "vertical_fov_override",
            "frustum_subrect_x", "frustum_subrect_y", "frustum_subrect_width", "frustum_subrect_height" };
        public static bool SiiValid(string text)
        {
            var lines = text.Split('\n'); int depth = 0, monitors = 0;
            if (lines.Length < 4 || lines[0] != "SiiNunit" || lines[1] != "{" || lines[lines.Length - 1] != "}") return false;
            for (int i = 2; i < lines.Length - 1; i++)
            {
                var l = lines[i]; Match m;
                if (l == "") continue;
                if (l == "}") { if (--depth < 0) return false; continue; }
                if (depth == 0)
                {
                    if (l == "multimon_config : _nameless.ozp.view {" || Regex.IsMatch(l, @"^monitor_config : _nameless\.ozp\.mon\d{1,2} \{$")) { depth = 1; continue; }
                    return false;
                }
                if ((m = Regex.Match(l, @"^ ([a-z_]+): (.+)$")).Success)
                {
                    string k = m.Groups[1].Value, val = m.Groups[2].Value;
                    if (SiiNumKeys.Contains(k)) { if (!SiiNum.IsMatch(val)) return false; continue; }
                    if (k == "monitors") { if (!int.TryParse(val, out monitors) || monitors < 1 || monitors > 16) return false; continue; }
                    if (k == "name") { if (!Regex.IsMatch(val, @"^[a-z0-9_]{1,24}$")) return false; continue; }
                    if (k == "camera_space_offset") { if (val != "(0.000000, 0.000000, 0.000000)") return false; continue; }
                    if (k == "render_interior" || k == "render_exterior") { if (val != "true" && val != "false") return false; continue; }
                    return false;
                }
                if (Regex.IsMatch(l, @"^ monitors\[\d{1,2}\]: _nameless\.ozp\.mon\d{1,2}$")) continue;
                return false;
            }
            return depth == 0;
        }

        public override Plan BuildPlan(Dictionary<string, object> v, Dictionary<string, Snapshot> snaps)
        {
            var paths = Paths(); var sii = snaps[paths[0]]; var cfg = snaps[paths[1]];
            if (!Directory.Exists(Dir)) throw new OzError("missing", "Não encontrei a pasta do " + Name + " nos Documentos. Abra o jogo uma vez antes.", Dir);
            if (!cfg.Exists) throw new OzError("missing", "Não encontrei o config.cfg do " + Name + ". Abra o jogo uma vez e saia antes.", paths[1]);
            var siiText = Str(v, "sii").Replace("\r\n", "\n").Trim();
            if (!siiText.StartsWith("SiiNunit") || !siiText.EndsWith("}") || siiText.Length > 20000 || !SiiValid(siiText))
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

    // ---------------- RaceRoom ----------------
    // Validado no rig a 21/11 (Surround com correção de molduras, 56°, 71 cm). O menu "Field of view" grava estes valores
    // no graphics_options.xml, em centímetros e graus. Só se escrevem as chaves de triplos; o resto do ficheiro fica igual.
    public class RaceRoom : Game
    {
        public string Root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        public RaceRoom() { Id = "raceroom"; Name = "RaceRoom Racing Experience"; Status = "testado"; Processes = new[] { "RRRE64", "RRRE" }; }
        public override string[] Paths() { return new[] { System.IO.Path.Combine(Root, "My Games", "SimBin", "RaceRoom Racing Experience", "UserData", "graphics_options.xml") }; }

        // chave, mínimo, máximo (float32, cm ou graus)
        static readonly object[][] Floats = {
            new object[] { "distanceToScreen", 20.0, 400.0 },
            new object[] { "screenSizeMiddle", 25.0, 400.0 }, new object[] { "screenSizeLeft", 25.0, 400.0 }, new object[] { "screenSizeRight", 25.0, 400.0 },
            new object[] { "screenBezelMiddle", 0.0, 10.0 }, new object[] { "screenBezelLeft", 0.0, 10.0 }, new object[] { "screenBezelRight", 0.0, 10.0 },
            new object[] { "screenAngleLeft", 0.0, 90.0 }, new object[] { "screenAngleRight", 0.0, 90.0 } };

        static LineRule Rule(string key, string type, string val)
        {
            return new LineRule
            {
                Key = key,
                Match = new Regex(@"^(\s*<" + Regex.Escape(key) + @" type=""" + type + @""">)([^<]*)(</" + Regex.Escape(key) + @">.*)$"),
                Rewrite = m => m.Groups[1].Value + val + m.Groups[3].Value,
                Current = m => m.Groups[2].Value, NewValue = val
            };
        }

        public override Plan BuildPlan(Dictionary<string, object> v, Dictionary<string, Snapshot> snaps)
        {
            var path = Paths()[0];
            if (!snaps[path].Exists) throw new OzError("missing", "Não encontrei o graphics_options.xml. Abra o RaceRoom uma vez e saia antes.", path);
            var p = new Plan { Game = Id, GameName = Name };
            var fp = new FilePlan { Path = path, Label = "graphics_options.xml" };
            fp.Rules.Add(Rule("tripleScreen", "bool", "true"));
            fp.Rules.Add(Rule("advancedFov", "bool", "true"));
            fp.Rules.Add(Rule("tripleScreenSeparateSizes", "bool", Str(v, "tripleScreenSeparateSizes") == "true" ? "true" : "false"));
            foreach (var k in Floats)
            {
                string key = (string)k[0];
                double d = Num(v, key, (double)k[1], (double)k[2]);
                fp.Rules.Add(Rule(key, "float32", d.ToString("F8", CultureInfo.InvariantCulture)));
            }
            p.Files.Add(fp);
            return p;
        }
    }

    // ---------------- Assetto Corsa ----------------
    // Testado pelo autor no rig a 23/11/2026 com o Content Manager. Ficheiros em Documentos\Assetto Corsa\cfg (os mesmos que o Content Manager usa).
    //  - video.ini: só [CAMERA] MODE=TRIPLE (o Content Manager lê e escreve este mesmo campo).
    //  - triple_screen.ini: gerado por inteiro (só tem a geometria dos triplos); cada linha é validada antes de chegar ao disco.
    // A resolução do jogo ([VIDEO] WIDTH/HEIGHT) não é escrita na Beta: só se avisa quando não bate com o Surround.
    public class AssettoCorsa : Game
    {
        public string Root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        public Func<List<int[]>> ScreensProvider = Displays.Screens;
        public AssettoCorsa() { Id = "ac"; Name = "Assetto Corsa"; Status = "testado"; Processes = new[] { "acs", "acs_x86", "AssettoCorsa" }; }
        string Dir { get { return System.IO.Path.Combine(Root, "Assetto Corsa", "cfg"); } }
        public override string[] Paths() { return new[] { System.IO.Path.Combine(Dir, "video.ini"), System.IO.Path.Combine(Dir, "triple_screen.ini") }; }

        static readonly Regex PointNum = new Regex(@"^-?\d{1,2}\.\d{1,6}$");
        static readonly string[] Sections = { "LEFT_SCREEN", "CENTER_SCREEN", "RIGHT_SCREEN" };

        /// <summary>Só aceita exatamente o ficheiro que o motor gera (engine.js acPlan), com números plausíveis (metros).</summary>
        public static bool TripleValid(string text)
        {
            var l = text.Split('\n');
            if (l.Length != 20 || l[0] != "[TRIPLE_SCREEN]" || l[1] != "ACTIVE=1" || l[2] != "") return false;
            for (int k = 0; k < 3; k++)
            {
                int b = 3 + k * 4;
                if (l[b] != "[" + Sections[k] + "]" || l[b + 3] != "") return false;
                for (int j = 0; j < 2; j++)
                {
                    var m = Regex.Match(l[b + 1 + j], "^" + (j == 0 ? "LEFT" : "RIGHT") + @"_POINT=([^,]+),([^,]+)$");
                    if (!m.Success || !PointNum.IsMatch(m.Groups[1].Value) || !PointNum.IsMatch(m.Groups[2].Value)) return false;
                    double x = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), y = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    if (Math.Abs(x) > 5 || Math.Abs(y) > 5 || (k == 1 && y < 0.1)) return false;
                }
            }
            if (l[15] != "[SPINNERS]") return false;
            var keys = new[] { "SP_MARGINS", "SP_ROTATION", "SP_DISTANCE", "SP_WIDTH" };
            for (int k = 0; k < 4; k++)
            {
                if (!Regex.IsMatch(l[16 + k], "^" + keys[k] + @"=-?\d{1,4}$")) return false;
            }
            return true;
        }

        static string Ini(string text, string section, string key)
        {
            string cur = null;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.TrimEnd('\r'); var h = Regex.Match(line, @"^\s*\[([^\]]+)\]\s*$");
                if (h.Success) { cur = h.Groups[1].Value.Trim(); continue; }
                var m = Regex.Match(line, @"^\s*" + Regex.Escape(key) + @"\s*=\s*(.*?)\s*$");
                if (m.Success && string.Equals(cur, section, StringComparison.OrdinalIgnoreCase)) return m.Groups[1].Value;
            }
            return null;
        }

        public override Plan BuildPlan(Dictionary<string, object> v, Dictionary<string, Snapshot> snaps)
        {
            var paths = Paths(); var video = snaps[paths[0]];
            if (!Directory.Exists(Dir)) throw new OzError("missing", "Não encontrei a pasta Documentos\\Assetto Corsa\\cfg. Abra o Assetto Corsa uma vez antes.", Dir);
            if (!video.Exists) throw new OzError("missing", "Não encontrei o video.ini do Assetto Corsa. Abra o jogo uma vez e saia antes.", paths[0]);
            var tri = Str(v, "triple").Replace("\r\n", "\n").TrimEnd('\n');
            if (tri.Length > 4000 || !TripleValid(tri)) throw new OzError("input", "O conteúdo calculado do triple_screen.ini não é válido. Volte a calcular.");
            var vt = TextFile.Decode(video.Bytes).Text;
            var p = new Plan { Game = Id, GameName = Name };
            p.Files.Add(new FilePlan { Path = paths[1], Label = "triple_screen.ini", WholeText = tri.Replace("\n", "\r\n") + "\r\n", MustExist = false });
            var fv = new FilePlan { Path = paths[0], Label = "video.ini" };
            fv.Rules.Add(new LineRule
            {
                Key = "[CAMERA] MODE", Section = "CAMERA",
                Match = new Regex(@"^(\s*MODE\s*=\s*)(\S*)(.*)$"),
                Rewrite = m => m.Groups[1].Value + "TRIPLE" + m.Groups[3].Value,
                Current = m => m.Groups[2].Value, NewValue = "TRIPLE"
            });
            p.Files.Add(fv);

            string w = Ini(vt, "VIDEO", "WIDTH"), h = Ini(vt, "VIDEO", "HEIGHT"), fs = Ini(vt, "VIDEO", "FULLSCREEN");
            p.Info.Add(new Dictionary<string, object> { { "WIDTH", w }, { "HEIGHT", h }, { "FULLSCREEN", fs } });
            object ow, oh; int tw, th;
            if (v.TryGetValue("width", out ow) && v.TryGetValue("height", out oh) && int.TryParse(Convert.ToString(ow, CultureInfo.InvariantCulture), out tw) && int.TryParse(Convert.ToString(oh, CultureInfo.InvariantCulture), out th))
            {
                var screens = ScreensProvider();
                if (screens.Count > 0 && !screens.Any(x => x[0] == tw && x[1] == th))
                    p.Warnings.Add("O Windows não tem nenhum ecrã com " + tw + "×" + th + ". Confirme que o Surround está ligado com a correção das molduras.");
                if (w != null && h != null && (w != tw.ToString() || h != th.ToString()))
                    p.Warnings.Add("O Assetto Corsa está configurado para " + w + "×" + h + " e a área do Surround é " + tw + "×" + th + ". Na Beta o OZPanorama não altera a resolução: escolha " + tw + "×" + th + " no Content Manager (Definições → Assetto Corsa → Vídeo) ou no launcher do jogo.");
            }
            p.Warnings.Add("Content Manager: se mudar as definições de vídeo depois de aplicar, deixe o modo de ecrã (Camera) em Triple.");
            return p;
        }
    }

    // ---------------- Le Mans Ultimate ----------------
    // Experimental. Config_DX11.ini fica na pasta UserData da instalação do Steam (não em Documentos).
    //  [COMPONENTS] SubViews=2 (três vistas = "Multiview"), UseSubViewParams=2 (vistas esquerda/direita próprias),
    //  ViewParams/LeftView/RightView=(largura m, altura m, distância dos olhos m, ângulo °, folga m). O comentário
    //  no fim da linha ViewParams é preservado. Os valores vêm do motor (engine.js lmuPlan) e são validados aqui.
    public class LeMansUltimate : Game
    {
        public Func<string> InstallProvider = FindInstall;
        public Func<List<int[]>> ScreensProvider = Displays.Screens;
        public LeMansUltimate() { Id = "lmu"; Name = "Le Mans Ultimate"; Status = "testado"; Processes = new[] { "Le Mans Ultimate", "LMU" }; }

        /// <summary>Pastas de bibliotecas do Steam: SteamPath do registo + libraryfolders.vdf. Só leitura.</summary>
        public static List<string> SteamLibraries()
        {
            var res = new List<string>();
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                {
                    var sp = k == null ? null : k.GetValue("SteamPath") as string;
                    if (!string.IsNullOrEmpty(sp))
                    {
                        sp = sp.Replace('/', '\\'); res.Add(sp);
                        var vdf = System.IO.Path.Combine(sp, "steamapps", "libraryfolders.vdf");
                        if (File.Exists(vdf))
                            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                                res.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
                    }
                }
            }
            catch { }
            res.Add(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
            return res.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        public static string FindInstall()
        {
            foreach (var lib in SteamLibraries())
            {
                var d = System.IO.Path.Combine(lib, "steamapps", "common", "Le Mans Ultimate");
                if (Directory.Exists(d)) return d;
            }
            return null;
        }
        public override string[] Paths()
        {
            var d = InstallProvider();
            return d == null ? new string[0] : new[] { System.IO.Path.Combine(d, "UserData", "Config_DX11.ini") };
        }

        static readonly string[] Keys = { "ViewParams", "LeftView", "RightView" };
        /// <summary>Exige exatamente "w, h, eye, ang, gap" com 3 casas decimais e valores plausíveis.</summary>
        public static string Tuple(Dictionary<string, object> v, string key)
        {
            var t = Str(v, key).Trim();
            var m = Regex.Match(t, @"^(-?\d\.\d{3}), (\d\.\d{3}), (\d\.\d{3}), (\d{1,2}\.\d{3}), (-?\d\.\d{3})$");
            if (!m.Success) throw new OzError("input", "Os valores calculados não são válidos. Volte a calcular.", key);
            Func<int, double> g = i => double.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture);
            if (g(1) < 0.1 || g(1) > 3 || g(2) < 0.05 || g(2) > 2 || g(3) < 0.1 || g(3) > 3 || g(4) > 90 || g(5) < -0.1 || g(5) > 0.2)
                throw new OzError("input", "Valor calculado fora do intervalo permitido (" + key + "). Volte a calcular.");
            return t;
        }

        public override Plan BuildPlan(Dictionary<string, object> v, Dictionary<string, Snapshot> snaps)
        {
            var paths = Paths();
            if (paths.Length == 0) throw new OzError("missing", "Não encontrei a instalação do Le Mans Ultimate no Steam.");
            var snap = snaps[paths[0]];
            if (!snap.Exists) throw new OzError("missing", "Não encontrei o Config_DX11.ini do Le Mans Ultimate. Abra o jogo uma vez e saia pelo menu antes.", paths[0]);
            var vals = new Dictionary<string, string>();
            foreach (var k in Keys) vals[k] = Tuple(v, k);
            var p = new Plan { Game = Id, GameName = Name };
            var f = new FilePlan { Path = paths[0], Label = "Config_DX11.ini" };
            foreach (var kv in new[] { new[] { "SubViews", "2" }, new[] { "UseSubViewParams", "2" } })
            {
                var key = kv[0]; var nv = kv[1];
                f.Rules.Add(new LineRule { Key = key, Section = "COMPONENTS", Match = new Regex(@"^(" + key + @"=)(\S*)(.*)$"), Rewrite = m => m.Groups[1].Value + nv + m.Groups[3].Value, Current = m => m.Groups[2].Value, NewValue = nv });
            }
            foreach (var k in Keys)
            {
                var nv = vals[k];
                f.Rules.Add(new LineRule { Key = k, Section = "COMPONENTS", Match = new Regex(@"^(" + k + @"=\()([^)]*)(\).*)$"), Rewrite = m => m.Groups[1].Value + nv + m.Groups[3].Value, Current = m => m.Groups[2].Value, NewValue = nv });
            }
            p.Files.Add(f);

            var text = TextFile.Decode(snap.Bytes).Text;
            Func<string, string> ini = key => { var m = Regex.Match(text, @"(?m)^" + key + @"=(.*?)\s*$"); return m.Success ? m.Groups[1].Value : null; };
            string rw = ini("VideoResW"), rh = ini("VideoResH"), wm = ini("WindowedMode"), cr = ini("CustomVidRes");
            p.Info.Add(new Dictionary<string, object> { { "VideoResW", rw }, { "VideoResH", rh }, { "WindowedMode", wm }, { "CustomVidRes", cr } });
            object ow, oh; int tw, th;
            if (v.TryGetValue("width", out ow) && v.TryGetValue("height", out oh) && int.TryParse(Convert.ToString(ow, CultureInfo.InvariantCulture), out tw) && int.TryParse(Convert.ToString(oh, CultureInfo.InvariantCulture), out th))
            {
                var screens = ScreensProvider();
                if (screens.Count > 0 && !screens.Any(x => x[0] == tw && x[1] == th))
                    p.Warnings.Add("O Windows não tem nenhum ecrã com " + tw + "×" + th + ". Confirme que o Surround está ligado com a correção das molduras.");
                bool windowed = wm == "1";
                if (!windowed && rw != null && rh != null && (rw != tw.ToString() || rh != th.ToString()))
                    p.Warnings.Add("O Le Mans Ultimate está configurado para " + rw + "×" + rh + " e a área do Surround é " + tw + "×" + th + ". Na Beta o OZPanorama não altera a resolução: escolha " + tw + "×" + th + " nas definições gráficas do jogo.");
                if (windowed && cr != null && cr.Replace(" ", "") != "(" + tw + "," + th + ")")
                    p.Warnings.Add("O jogo está em janela com CustomVidRes=" + cr + ". A área do Surround é " + tw + "×" + th + ".");
            }
            p.Warnings.Add("No jogo, a opção Multiview (definições gráficas) corresponde a SubViews=2. Não use o modo sem borda (Borderless), que estica a imagem.");
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
            new RaceRoom(),
            new AssettoCorsa(),   // testado no rig a 23/11 com o Content Manager (SP_MARGINS=0 com o Surround)
            new LeMansUltimate(), // experimental
            // ACC fica fora da Beta (pedido do autor: só o que foi testado).
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
            bool appliedOk = false; string writing = null;   // ficheiro a meio de ser escrito quando houve a falha
            try
            {
                // Fica registado antes da primeira escrita: se o programa for interrompido a meio, o backup mostra-o.
                bk.Status = "a-aplicar"; bk.SaveManifest();
                foreach (var f in pv.Plan.Files)
                {
                    // Última confirmação imediatamente antes de substituir.
                    Faults.HitAction(written.Count > 0 ? "between-files" : "before-first-write");
                    var again = Snapshot.Read(f.Path);
                    if (again.Sha != pv.Snaps[f.Path].Sha) throw new OzError("changed", "O ficheiro " + f.Label + " mudou durante a aplicação.", f.Path);
                    var data = pv.NewBytes[f.Path];
                    if (data.Length == 0 && again.Exists) throw new OzError("verify", "O conteúdo novo ficou vazio; nada foi alterado.", f.Path);
                    writing = f.Path;
                    SafeIO.AtomicWrite(f.Path, data);
                    written.Add(f.Path); writing = null;
                    Faults.Hit("after-replace");
                    // 3) Verificação: releitura byte a byte e regras.
                    var back = File.ReadAllBytes(f.Path);
                    if (Faults.Is("verify-tamper")) back = new byte[0];
                    if (Hash.Sha256(back) != Hash.Sha256(data)) throw new OzError("verify", "A releitura do ficheiro " + f.Label + " não confere.", f.Path);
                    var bf = bk.Files.First(x => x.Original == f.Path); bf.ShaApplied = Hash.Sha256(back);
                }
                appliedOk = true;
                bk.Status = "aplicado";
                res.Ok = true; res.Status = "aplicado"; res.Message = "Configuração aplicada e verificada.";
                // Uma falha só a gravar o registo do backup não desfaz uma aplicação correta e verificada.
                try { bk.SaveManifest(); } catch (Exception me) { res.Detail.Add("registo do backup não gravado: " + me.Message); AppInfo.Log("aplicar " + g.Id + ": registo do backup não gravado: " + me.Message); }
                AppInfo.Log("aplicar " + g.Id + " ok backup=" + bk.Id);
                return res;
            }
            catch (Exception e)
            {
                if (appliedOk) throw;
                // OZP-02: nada chegou a ser escrito (ex.: o jogo gravou o ficheiro entre a confirmação e a escrita).
                // Não há nada a repor; não se pede ao utilizador que restaure, o que apagaria a alteração do outro programa.
                if (written.Count == 0 && writing == null)
                {
                    bk.Status = "falhou-nada-escrito"; try { bk.SaveManifest(); } catch { }
                    res.Detail.Add(e.Message); res.Status = "falhou-nada-escrito";
                    res.Message = "Nada foi escrito (" + (e is OzError ? e.Message : "erro antes de escrever") + "). Os ficheiros ficaram como estavam; volte a carregar em Aplicar.";
                    AppInfo.Log("aplicar " + g.Id + " FALHOU antes de escrever: " + e.Message + (e is OzError ? " | " + ((OzError)e).Detail : ""));
                    return res;
                }
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
                        // Só se desfaz o que o OZPanorama escreveu nesta operação. Um ficheiro que outro programa (o jogo, o
                        // Steam Cloud...) mudou entretanto fica como está: a alteração dele não é apagada.
                        byte[] ours;
                        if (!written.Contains(bf.Original) || !pv.NewBytes.TryGetValue(bf.Original, out ours) || cur.Sha != Hash.Sha256(ours))
                        { failed.Add(System.IO.Path.GetFileName(bf.Original) + ": alterado por outro programa durante a aplicação; não foi reposto para não apagar essa alteração"); continue; }
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
            var allowed = new HashSet<string>(g.AllPaths(), StringComparer.OrdinalIgnoreCase);
            foreach (var f in b.Files)
                if (!allowed.Contains(f.Original)) throw new OzError("backup", "O registo do backup aponta para um ficheiro que não é do " + g.Name + "; por segurança nada foi alterado.", f.Original);
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
            var done = new List<BackupFile>();
            try
            {
                foreach (var f in todo)
                {
                    if (done.Count > 0) Faults.HitAction("restore-between-files");
                    done.Add(f);
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
                // Reposição automática do estado de antes do restauro (backup de segurança acabado de validar).
                var failed = new List<string>();
                foreach (var f in done)
                {
                    try
                    {
                        var sf = safety.Files.First(x => string.Equals(x.Original, f.Original, StringComparison.OrdinalIgnoreCase));
                        var cur = Snapshot.Read(f.Original);
                        if (sf.Existed ? (cur.Exists && cur.Sha == sf.ShaOriginal) : !cur.Exists) continue;
                        if (sf.Existed)
                        {
                            var data = File.ReadAllBytes(sf.Copy);
                            if (Hash.Sha256(data) != sf.ShaOriginal) throw new IOException("cópia de segurança danificada");
                            SafeIO.AtomicWrite(f.Original, data);
                            if (Hash.Sha256(File.ReadAllBytes(f.Original)) != sf.ShaOriginal) throw new IOException("reposição não confere");
                        }
                        else if (File.Exists(f.Original)) File.Delete(f.Original);
                    }
                    catch (Exception re) { failed.Add(System.IO.Path.GetFileName(f.Original) + ": " + re.Message); }
                }
                r.Detail.AddRange(failed);
                safety.Status = failed.Count == 0 ? "restauro-revertido" : "restauro-incompleto"; try { safety.SaveManifest(); } catch { }
                r.Message = failed.Count == 0
                    ? "O restauro não terminou. Os ficheiros voltaram automaticamente ao estado de antes do restauro (verificado)."
                    : "O restauro não terminou e não foi possível voltar atrás em tudo. O estado anterior está no backup " + safety.Id + "; pode restaurá-lo a partir da lista.";
                AppInfo.Log("restaurar " + b.Id + " FALHOU: " + e.Message + (failed.Count == 0 ? " (revertido)" : " (reversão incompleta)"));
            }
            return r;
        }
    }
}
