// OZPanorama · Zuka Force — servidor local, janela e relatório de problemas (Beta 0.1.3)
// Lê a lista de ecrãs. Só muda o Windows quando a pessoa carrega em Ligar/Desligar Surround (perfis gravados pelo próprio
// driver da NVIDIA, com confirmação e reposição automática em 20 s; ver app/surround.ps1).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace OZPanorama
{
    public static class Displays
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DISPLAY_DEVICE { public int cb; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString; public int StateFlags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName; public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra; public int dmFields;
            public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput; public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName; public short dmLogPixels; public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string dev, uint i, ref DISPLAY_DEVICE dd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

        /// <summary>Ecrãs ativos agora: [largura, altura, x, y, principal(1/0)] em píxeis reais.</summary>
        public static List<int[]> Screens()
        {
            var list = new List<int[]>();
            try
            {
                for (uint i = 0; i < 32; i++)
                {
                    var dd = new DISPLAY_DEVICE(); dd.cb = Marshal.SizeOf(dd);
                    if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
                    if ((dd.StateFlags & 1) == 0) continue;   // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
                    var dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(dm);
                    if (EnumDisplaySettings(dd.DeviceName, -1, ref dm))
                        list.Add(new[] { dm.dmPelsWidth, dm.dmPelsHeight, dm.dmPositionX, dm.dmPositionY, (dd.StateFlags & 4) != 0 ? 1 : 0 });
                }
            }
            catch { }
            return list;
        }
    }

    /// <summary>Nome e tamanho físico de cada monitor (EDID). Só leitura: registo do Windows e WMI, sem administrador.
    /// Com o Surround ligado o Windows só vê um ecrã virtual; os monitores físicos continuam no registo (Enum\DISPLAY),
    /// por isso juntam-se as duas fontes.</summary>
    public static class Edid
    {
        /// <summary>EDID → [nome, largura mm, altura mm, chave (fabricante+produto+série)]. Nome do descritor 0xFC.</summary>
        public static object[] Parse(byte[] e)
        {
            if (e == null || e.Length < 23) return null;
            int w = 0, h = 0;
            if (e.Length >= 72) { w = e[66] + ((e[68] & 0xF0) << 4); h = e[67] + ((e[68] & 0x0F) << 8); }
            if (w <= 0 || h <= 0) { w = e[21] * 10; h = e[22] * 10; }
            string name = "";
            for (int o = 54; o + 18 <= e.Length && o <= 108; o += 18)
                if (e[o] == 0 && e[o + 1] == 0 && e[o + 2] == 0 && e[o + 3] == 0xFC)
                {
                    var sb = new StringBuilder();
                    for (int i = o + 5; i < o + 18 && e[i] != 0x0A && e[i] != 0; i++) sb.Append((char)e[i]);
                    name = sb.ToString().Trim();
                }
            var key = BitConverter.ToString(e, 8, 8);
            return new object[] { name, w, h, key };
        }

        // Ecrã virtual do NVIDIA Surround / Mosaic: reconhece-se pelo nome (não pela largura: uma TV grande também passa de 1 m).
        static bool Virtual(string name) { var n = name ?? ""; return n.IndexOf("surround", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("mosaic", StringComparison.OrdinalIgnoreCase) >= 0; }

        public static List<object> Read()
        {
            var list = new List<object>(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Chave = instância do monitor no registo (DISPLAY\<modelo>\<instância>): três monitores iguais, mesmo com
            // número de série 0 no EDID, são três instâncias diferentes. A mesma instância vista pelo WMI e pelo registo é uma só.
            Action<string, int, int, string, bool, string> add = (name, w, h, src, present, key) =>
            {
                if (w <= 0 || h <= 0 || Virtual(name)) return;
                if (!seen.Add(key)) return;
                list.Add(new Dictionary<string, object> { { "id", "m" + Hash.Sha256(Encoding.UTF8.GetBytes(key)).Substring(0, 12) }, { "name", name }, { "w_mm", w }, { "h_mm", h }, { "source", src }, { "present", present } });
            };
            // 1) WMI: os monitores que o Windows vê agora (sem Surround, todos).
            try
            {
                using (var q = new System.Management.ManagementObjectSearcher(@"root\wmi", "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID"))
                    foreach (System.Management.ManagementObject m in q.Get())
                    {
                        var inst = Regex.Replace((string)m["InstanceName"] ?? "", @"_\d+$", ""); string name = "";
                        var chars = m["UserFriendlyName"] as ushort[];
                        if (chars != null) name = new string(chars.Where(ch => ch != 0).Select(ch => (char)ch).ToArray()).Trim();
                        try
                        {
                            using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\" + inst + @"\Device Parameters"))
                            {
                                var p = Parse(k == null ? null : k.GetValue("EDID") as byte[]);
                                if (p != null) add(name.Length > 0 ? name : (string)p[0], (int)p[1], (int)p[2], "EDID", true, inst.ToUpperInvariant());
                            }
                        }
                        catch { }
                    }
            }
            catch (Exception e) { AppInfo.Log("EDID WMI: " + e.Message); }
            // 2) Registo: todos os monitores com EDID (inclui os que estão dentro do Surround).
            try
            {
                using (var disp = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY"))
                    if (disp != null)
                        foreach (var model in disp.GetSubKeyNames())
                            using (var mk = disp.OpenSubKey(model))
                                foreach (var inst in mk.GetSubKeyNames())
                                    try
                                    {
                                        using (var ik = mk.OpenSubKey(inst))
                                        using (var dp = ik.OpenSubKey("Device Parameters"))
                                        {
                                            var p = Parse(dp == null ? null : dp.GetValue("EDID") as byte[]);
                                            if (p == null) continue;
                                            bool present;
                                            using (var ctl = ik.OpenSubKey("Control")) present = ctl != null;   // subchave volátil: só existe com o monitor ligado
                                            var name = (string)p[0]; if (name.Length == 0) name = model;
                                            add(name, (int)p[1], (int)p[2], "EDID (registo)", present, ("DISPLAY\\" + model + "\\" + inst).ToUpperInvariant());
                                        }
                                    }
                                    catch { }
            }
            catch (Exception e) { AppInfo.Log("EDID registo: " + e.Message); }
            // Ligados primeiro; os antigos (já desligados) ficam no fim, para escolher à mão se for preciso.
            return list.OrderBy(o => (bool)((Dictionary<string, object>)o)["present"] ? 0 : 1).ToList();
        }
    }

    /// <summary>Relatório para "Reportar problema": a pessoa vê tudo antes de enviar; caminhos pessoais anonimizados.</summary>
    public static class Report
    {
        public static string Anonymize(string s)
        {
            if (s == null) return null;
            var user = Environment.UserName; var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(profile)) s = Regex.Replace(s, Regex.Escape(profile), "%USERPROFILE%", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"(?i)[A-Z]:\\Users\\[^\\""\s]+", "%USERPROFILE%");
            s = Regex.Replace(s, @"[\w.+-]+@[\w-]+\.[\w.]+", "<email>");
            if (!string.IsNullOrEmpty(user) && user.Length >= 3) s = Regex.Replace(s, Regex.Escape(user), "<utilizador>", RegexOptions.IgnoreCase);
            return s;
        }

        public static string Build(Dictionary<string, object> ui, bool includeLog)
        {
            var sb = new StringBuilder();
            sb.AppendLine("### Versão"); sb.AppendLine("OZPanorama " + AppInfo.Version);
            sb.AppendLine("Windows " + Environment.OSVersion.Version + (Environment.Is64BitOperatingSystem ? " 64 bits" : ""));
            sb.AppendLine(); sb.AppendLine("### Ecrãs (Windows)");
            foreach (var s in Displays.Screens()) sb.AppendLine("- " + s[0] + "×" + s[1] + " em (" + s[2] + "," + s[3] + ")" + (s[4] == 1 ? " principal" : ""));
            sb.AppendLine(); sb.AppendLine("### Simuladores");
            foreach (var g in Registry.All)
                sb.AppendLine("- " + g.Name + ": " + g.Status + ", ficheiros " + (g.Paths().Length == 0 ? "—" : string.Join(", ", g.Paths().Select(p => (File.Exists(p) ? "encontrado " : "em falta ") + Path.GetFileName(p)).ToArray())) + (g.Running() ? ", ABERTO" : ""));
            object o;
            if (ui != null && ui.TryGetValue("rig", out o) && o != null) { sb.AppendLine(); sb.AppendLine("### Monitores e posição (introduzidos na app)"); sb.AppendLine("```"); sb.AppendLine(Convert.ToString(o)); sb.AppendLine("```"); }
            if (ui != null && ui.TryGetValue("result", out o) && o != null) { sb.AppendLine(); sb.AppendLine("### Resultado calculado"); sb.AppendLine("```"); sb.AppendLine(Convert.ToString(o)); sb.AppendLine("```"); }
            if (ui != null && ui.TryGetValue("lastError", out o) && o != null) { sb.AppendLine(); sb.AppendLine("### Última mensagem de erro"); sb.AppendLine(Convert.ToString(o)); }
            if (includeLog)
            {
                var log = Path.Combine(AppInfo.DataDir, "log.txt");
                if (File.Exists(log))
                {
                    var lines = File.ReadAllLines(log); sb.AppendLine(); sb.AppendLine("### Registo (últimas 40 linhas)"); sb.AppendLine("```");
                    foreach (var l in lines.Skip(Math.Max(0, lines.Length - 40))) sb.AppendLine(l);
                    sb.AppendLine("```");
                }
            }
            return Anonymize(sb.ToString());
        }
    }

    /// <summary>NVIDIA Surround: perfis "com" e "sem" gravados pelo próprio driver (app/surround.ps1, o mesmo da v0.3 validada no rig).
    /// Ligar/desligar corre num processo à parte, que mostra a janela "Manter?" e repõe o anterior sem resposta.</summary>
    public static class Surround
    {
        public static string Dir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OZPanorama", "surround"); } }
        static readonly object gate = new object();
        static Process applying;   // só uma troca de modo de cada vez

        static string Script()
        {
            byte[] b;
            using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("OZPanorama.surround.ps1"))
            {
                if (st == null) throw new OzError("surround", "O módulo do Surround não está incluído neste programa.");
                var ms = new MemoryStream(); st.CopyTo(ms); b = ms.ToArray();
            }
            var dir = Path.Combine(AppInfo.LocalDir, "bin"); Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, "surround.ps1");
            if (!File.Exists(p) || !File.ReadAllBytes(p).SequenceEqual(b)) File.WriteAllBytes(p, b);
            return p;
        }

        static readonly string[] Langs = { "en", "pt-BR", "es", "fr" };

        static ProcessStartInfo Psi(string action, string profile, string rid, string lang)
        {
            // Todos os argumentos vêm de listas fechadas (ação, perfil, língua) ou de um GUID gerado aqui.
            if (lang == "pt") lang = "pt-BR";   // código da interface (i18n.js) → código do script
            if (lang != null && Array.IndexOf(Langs, lang) < 0) lang = "en";
            var cmd = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; & '" + Script().Replace("'", "''") + "' -Action " + action
                + (profile != null ? " -Perfil " + profile : "") + (rid != null ? " -RequestId " + rid : "") + (lang != null ? " -Lang " + lang : "");
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe");
            if (!File.Exists(exe)) exe = "powershell.exe";
            return new ProcessStartInfo(exe, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"" + cmd + "\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        }

        /// <summary>status ou save: espera pela resposta JSON do script.</summary>
        public static Dictionary<string, object> Call(string action, string profile, JavaScriptSerializer json)
        {
            lock (gate)
            {
                using (var pr = Process.Start(Psi(action, profile, null, null)))
                {
                    // As duas saídas são lidas em paralelo: assim o tempo limite funciona mesmo que o PowerShell encrave.
                    var errTask = pr.StandardError.ReadToEndAsync();
                    var outTask = pr.StandardOutput.ReadToEndAsync();
                    if (!pr.WaitForExit(60000)) { try { pr.Kill(); } catch { } throw new OzError("surround", "O Surround não respondeu a tempo."); }
                    var output = outTask.Result;
                    var i = output.IndexOf('{');
                    if (i < 0) { AppInfo.Log("surround " + action + ": " + output + " | " + errTask.Result); throw new OzError("surround", "Não consegui falar com o driver da NVIDIA.", errTask.Result); }
                    return json.Deserialize<Dictionary<string, object>>(output.Substring(i));
                }
            }
        }

        /// <summary>apply: processo à parte (a mudança de modo e a janela de confirmação não bloqueiam o servidor).</summary>
        public static string Apply(string profile, string lang)
        {
            lock (gate)
            {
                if (applying != null && !applying.HasExited) throw new OzError("surround-busy", "Já está a decorrer uma mudança do Surround. Espere que termine.");
                if (applying != null) { applying.Dispose(); applying = null; }
                var rid = Guid.NewGuid().ToString("N");
                var psi = Psi("apply", profile, rid, lang); psi.RedirectStandardOutput = psi.RedirectStandardError = false; psi.StandardOutputEncoding = psi.StandardErrorEncoding = null;
                applying = Process.Start(psi);
                return rid;
            }
        }

        /// <summary>Resultado da última troca (ultimo.json, escrito pelo script). Leitura direta, sem arrancar o PowerShell.</summary>
        public static Dictionary<string, object> Last(JavaScriptSerializer json)
        {
            var f = Path.Combine(Dir, "ultimo.json"); object last = null;
            try { if (File.Exists(f)) last = json.DeserializeObject(File.ReadAllText(f, Encoding.UTF8).TrimStart('\uFEFF')); } catch { last = null; }
            bool running; lock (gate) running = applying != null && !applying.HasExited;
            return new Dictionary<string, object> { { "ok", true }, { "last", last }, { "running", running } };
        }

        public static void Keep(string rid)
        {
            if (rid == null || !Regex.IsMatch(rid, "^[0-9a-f]{32}$")) throw new OzError("input", "Pedido de confirmação inválido.");
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, "manter_" + rid + ".flag"), "manter", Encoding.ASCII);
        }
    }

    public class Server
    {
        public int Port; public string Token = Guid.NewGuid().ToString("N");
        HttpListener http; DateTime lastSeen = DateTime.UtcNow; volatile bool quit;
        // Pedidos atendidos em paralelo; tudo o que lê ou escreve ficheiros de jogos e backups passa por esta fila única.
        // As chamadas ao Surround (PowerShell, segundos) ficam fora dela, para não bloquearem a janela.
        readonly object opsGate = new object();
        // Uma instância por uso: os pedidos correm em paralelo e o JavaScriptSerializer não é garantidamente thread-safe.
        JavaScriptSerializer json { get { return new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 }; } }
        public const string IssuesUrl = "https://github.com/OptimusZuka/OZPanorama/issues/new";

        public void Start()
        {
            for (int p = 47777; p < 47800 && http == null; p++)
            {
                var h = new HttpListener(); h.Prefixes.Add("http://localhost:" + p + "/");
                try { h.Start(); http = h; Port = p; } catch (HttpListenerException) { h.Close(); }
            }
            if (http == null) throw new OzError("port", "Não foi possível abrir a interface (portas 47777–47799 ocupadas).");
        }

        static string Resource(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("OZPanorama." + name))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }
        static byte[] ResourceBytes(string name)
        {
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("OZPanorama." + name))
            {
                if (s == null) return null; var ms = new MemoryStream(); s.CopyTo(ms); return ms.ToArray();
            }
        }

        public void Run()
        {
            // Fecha sozinho se a janela deixar de falar com o servidor (a interface pergunta o estado a cada 5 s).
            new Thread(() => { while (!quit) { Thread.Sleep(5000); if ((DateTime.UtcNow - lastSeen).TotalSeconds > 90) quit = true; } if (quit) try { http.Stop(); } catch { } }) { IsBackground = true }.Start();
            while (!quit)
            {
                HttpListenerContext ctx;
                try { ctx = http.GetContext(); } catch { break; }
                var c = ctx;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try { Handle(c); }
                    catch (Exception e) { AppInfo.Log("erro interno: " + e); try { Send(c, 500, new Dictionary<string, object> { { "ok", false }, { "error", "Erro interno. Use \"Reportar problema\"." } }); } catch { } }
                });
            }
        }

        void Send(HttpListenerContext c, int code, object body)
        {
            var b = Encoding.UTF8.GetBytes(json.Serialize(body));
            c.Response.StatusCode = code; c.Response.ContentType = "application/json; charset=utf-8";
            c.Response.Headers["Cache-Control"] = "no-store";
            c.Response.OutputStream.Write(b, 0, b.Length); c.Response.Close();
        }
        void SendRaw(HttpListenerContext c, string type, byte[] b)
        {
            c.Response.ContentType = type; c.Response.Headers["Cache-Control"] = "no-store";
            c.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'";
            c.Response.OutputStream.Write(b, 0, b.Length); c.Response.Close();
        }

        static Dictionary<string, object> ErrorBody(OzError e) { return new Dictionary<string, object> { { "ok", false }, { "code", e.Code }, { "error", e.Message }, { "detail", Report.Anonymize(e.Detail) } }; }

        static object ChangeJson(Change c) { return new Dictionary<string, object> { { "ficheiro", c.File }, { "chave", c.Key }, { "atual", c.Current }, { "novo", c.New }, { "muda", c.Changes } }; }

        static object BackupJson(Backup b, bool withStates)
        {
            return new Dictionary<string, object> {
                { "id", b.Id }, { "jogo", b.Game }, { "nomeJogo", b.GameName }, { "operacao", b.Operation }, { "estado", b.Status }, { "criado", b.CreatedLocal }, { "versao", b.AppVersion },
                { "ficheiros", b.Files.Select(f => (object)new Dictionary<string, object> { { "nome", Path.GetFileName(f.Original) }, { "caminho", Report.Anonymize(f.Original) }, { "existia", f.Existed },
                    { "situacao", withStates ? SafeState(f) : null } }).ToList() } };
        }
        static string SafeState(BackupFile f) { try { return Ops.FileState(f); } catch { return "ilegivel"; } }

        void Handle(HttpListenerContext c)
        {
            var path = c.Request.Url.AbsolutePath; var method = c.Request.HttpMethod;
            // Anti DNS-rebinding: só aceitamos o nosso próprio anfitrião.
            var host = c.Request.Headers["Host"] ?? "";
            if (host != "localhost:" + Port) { c.Response.StatusCode = 403; c.Response.Close(); return; }

            if (method == "GET" && (path == "/" || path == "/index.html"))
            {
                var html = Resource("index.html").Replace("__OZP_TOKEN__", Token);
                SendRaw(c, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html)); return;
            }
            if (method == "GET" && (path == "/engine.js" || path == "/i18n.js")) { SendRaw(c, "text/javascript; charset=utf-8", Encoding.UTF8.GetBytes(Resource(path.Substring(1)))); return; }
            if (method == "GET" && path == "/assets/icone.png") { var b = ResourceBytes("icone.png"); if (b != null) { SendRaw(c, "image/png", b); return; } }
            if (!path.StartsWith("/api/")) { c.Response.StatusCode = 404; c.Response.Close(); return; }

            if (c.Request.Headers["X-OZP-Token"] != Token) { Send(c, 403, new Dictionary<string, object> { { "ok", false }, { "error", "Pedido recusado." } }); return; }
            lastSeen = DateTime.UtcNow;
            Dictionary<string, object> body = null;
            if (method == "POST")
            {
                if (!(c.Request.ContentType ?? "").StartsWith("application/json")) { Send(c, 415, new Dictionary<string, object> { { "ok", false }, { "error", "Pedido inválido." } }); return; }
                if (c.Request.ContentLength64 > 4 * 1024 * 1024) { Send(c, 413, new Dictionary<string, object> { { "ok", false }, { "error", "Pedido demasiado grande." } }); return; }
                try
                {
                    using (var r = new StreamReader(c.Request.InputStream, Encoding.UTF8)) { var t = r.ReadToEnd(); body = string.IsNullOrWhiteSpace(t) ? new Dictionary<string, object>() : json.Deserialize<Dictionary<string, object>>(t); }
                }
                catch (Exception) { Send(c, 400, new Dictionary<string, object> { { "ok", false }, { "error", "Pedido inválido." } }); return; }
                if (body == null) body = new Dictionary<string, object>();
            }
            try
            {
                if (path.StartsWith("/api/surround")) Route(c, method, path, body);
                else lock (opsGate) Route(c, method, path, body);
            }
            catch (OzError e) { AppInfo.Log(path + " recusado: " + e.Code + " " + e.Message + " | " + e.Detail); Send(c, 409, ErrorBody(e)); }
            catch (Exception e) { AppInfo.Log(path + " erro: " + e); Send(c, 500, new Dictionary<string, object> { { "ok", false }, { "error", "Erro inesperado; nada foi alterado sem verificação. Use \"Reportar problema\"." }, { "detail", Report.Anonymize(e.Message) } }); }
        }

        static string S(Dictionary<string, object> b, string k) { object o; return b != null && b.TryGetValue(k, out o) && o != null ? Convert.ToString(o) : null; }
        static bool B(Dictionary<string, object> b, string k) { object o; return b != null && b.TryGetValue(k, out o) && o is bool && (bool)o; }

        void Route(HttpListenerContext c, string method, string path, Dictionary<string, object> b)
        {
            if (method == "GET" && path == "/api/info")
            {
                Send(c, 200, new Dictionary<string, object> {
                    { "ok", true }, { "version", AppInfo.Version }, { "issuesUrl", IssuesUrl }, { "backupDir", Report.Anonymize(BackupStore.Root) },
                    { "screens", Displays.Screens().Select(s => (object)new Dictionary<string, object> { { "w", s[0] }, { "h", s[1] }, { "x", s[2] }, { "y", s[3] }, { "primary", s[4] == 1 } }).ToList() },
                    { "games", Registry.All.Select(g => (object)new Dictionary<string, object> { { "id", g.Id }, { "name", g.Name }, { "status", g.Status }, { "canApply", g.CanApply }, { "running", g.Running() },
                        { "files", g.Paths().Select(p => (object)new Dictionary<string, object> { { "nome", Path.GetFileName(p) }, { "existe", File.Exists(p) } }).ToList() } }).ToList() } });
                return;
            }
            if (method == "POST" && path == "/api/preview")
            {
                var values = b.ContainsKey("values") ? b["values"] as Dictionary<string, object> : null;
                var pv = Ops.MakePreview(S(b, "game"), values);
                Send(c, 200, new Dictionary<string, object> {
                    { "ok", true }, { "previewId", pv.Id }, { "game", pv.Game.Id }, { "gameName", pv.Game.Name }, { "running", pv.Game.Running() },
                    { "changes", pv.Changes.Select(ChangeJson).ToList() }, { "warnings", pv.Plan.Warnings }, { "info", pv.Plan.Info },
                    { "files", pv.Plan.Files.Select(f => (object)new Dictionary<string, object> { { "nome", f.Label }, { "caminho", Report.Anonymize(f.Path) }, { "existe", pv.Snaps[f.Path].Exists },
                        { "codificacao", pv.Snaps[f.Path].Exists ? TextFile.Decode(pv.Snaps[f.Path].Bytes).EncodingName : "novo (UTF-8)" } }).ToList() } });
                return;
            }
            if (method == "POST" && path == "/api/prepare")
            {
                var bk = Ops.Prepare(S(b, "previewId"));
                Send(c, 200, new Dictionary<string, object> { { "ok", true }, { "backup", bk.Id } }); return;
            }
            if (method == "POST" && path == "/api/cancel")
            {
                Ops.Cancel(S(b, "previewId"));
                Send(c, 200, new Dictionary<string, object> { { "ok", true } }); return;
            }
            if (method == "GET" && path == "/api/monitors")
            {
                Send(c, 200, new Dictionary<string, object> { { "ok", true }, { "monitors", Edid.Read() },
                    { "screens", Displays.Screens().Select(x => (object)new Dictionary<string, object> { { "w", x[0] }, { "h", x[1] }, { "x", x[2] }, { "y", x[3] }, { "primary", x[4] == 1 } }).ToList() } });
                return;
            }
            if (method == "POST" && path == "/api/apply")
            {
                var r = Ops.Apply(S(b, "previewId"), B(b, "confirm"));
                Send(c, 200, new Dictionary<string, object> { { "ok", r.Ok }, { "status", r.Status }, { "message", r.Message }, { "backup", r.Backup == null ? null : r.Backup.Id },
                    { "changes", r.Changes.Select(ChangeJson).ToList() }, { "detail", r.Detail.Select(Report.Anonymize).ToList() } });
                return;
            }
            if (method == "GET" && path == "/api/backups")
            {
                Send(c, 200, new Dictionary<string, object> { { "ok", true }, { "backups", BackupStore.List().Select(x => BackupJson(x, true)).ToList() } }); return;
            }
            if (method == "POST" && path == "/api/restore")
            {
                var r = Ops.Restore(S(b, "id"), B(b, "confirm"), B(b, "acceptChanged"));
                Send(c, 200, new Dictionary<string, object> { { "ok", r.Ok }, { "message", r.Message }, { "safetyBackup", r.SafetyBackup == null ? null : r.SafetyBackup.Id }, { "detail", r.Detail.Select(Report.Anonymize).ToList() } });
                return;
            }
            if (method == "POST" && path == "/api/backups/open")
            {
                // Recuperação assistida: abre a pasta do backup no Explorador (só pastas dentro da pasta de backups).
                var bk = BackupStore.Find(S(b, "id"));
                Process.Start("explorer.exe", "\"" + bk.Dir + "\"");
                Send(c, 200, new Dictionary<string, object> { { "ok", true } }); return;
            }
            if (method == "POST" && path == "/api/report")
            {
                var ui = b.ContainsKey("ui") ? b["ui"] as Dictionary<string, object> : null;
                Send(c, 200, new Dictionary<string, object> { { "ok", true }, { "text", Report.Build(ui, B(b, "includeLog")) }, { "issuesUrl", IssuesUrl } }); return;
            }
            if (method == "POST" && path == "/api/open-issue")
            {
                // Abre o GitHub no browser com título e texto que a pessoa já reviu. Nada é enviado automaticamente.
                var title = S(b, "title") ?? "Problema no OZPanorama"; var text = S(b, "text") ?? "";
                if (text.Length > 6000) text = text.Substring(0, 6000) + "\n…(cortado; cole o resto à mão)";
                var url = IssuesUrl + "?labels=beta&title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(text);
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                Send(c, 200, new Dictionary<string, object> { { "ok", true } }); return;
            }
            if (method == "GET" && path == "/api/surround") { Send(c, 200, Surround.Call("status", null, json)); return; }
            if (method == "GET" && path == "/api/surround/last") { Send(c, 200, Surround.Last(json)); return; }
            if (method == "POST" && (path == "/api/surround/save" || path == "/api/surround/apply"))
            {
                var p = S(b, "profile");
                if (p != "com" && p != "sem") throw new OzError("input", "Perfil desconhecido.");
                if (path.EndsWith("/save")) { var r = Surround.Call("save", p, json); AppInfo.Log("Surround guardado " + p); Send(c, 200, r); return; }
                AppInfo.Log("Surround: aplicar " + p);
                Send(c, 200, new Dictionary<string, object> { { "ok", true }, { "started", true }, { "id", Surround.Apply(p, S(b, "lang")) } }); return;
            }
            if (method == "POST" && path == "/api/surround/keep") { Surround.Keep(S(b, "id")); AppInfo.Log("Surround: manter"); Send(c, 200, new Dictionary<string, object> { { "ok", true } }); return; }
            if (method == "POST" && path == "/api/quit") { Send(c, 200, new Dictionary<string, object> { { "ok", true } }); quit = true; try { http.Stop(); } catch { } return; }
            Send(c, 404, new Dictionary<string, object> { { "ok", false }, { "error", "Pedido desconhecido." } });
        }
    }

    public static class Program
    {
        static string FindEdge()
        {
            foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) })
            {
                var p = Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        [STAThread]
        public static int Main(string[] args)
        {
            try { Displays.SetProcessDPIAware(); } catch { }
            bool created;
            using (var mutex = new Mutex(true, "OZPanorama.ZukaForce.Beta", out created))
            {
                if (!created)
                {
                    System.Windows.Forms.MessageBox.Show("O OZPanorama já está aberto.", "OZPanorama", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Information);
                    return 0;
                }
                var s = new Server();
                try { s.Start(); }
                catch (OzError e) { System.Windows.Forms.MessageBox.Show(e.Message, "OZPanorama"); return 1; }
                AppInfo.Log("início " + AppInfo.Version + " porta " + s.Port);
                var url = "http://localhost:" + s.Port + "/";
                var edge = FindEdge();
                try
                {
                    // Janela própria do Edge (modo aplicação), com perfil separado para não misturar com a navegação da pessoa.
                    var profile = Path.Combine(AppInfo.LocalDir, "janela");   // perfil do Edge fora dos Documentos (OneDrive)
                    if (edge != null) Process.Start(edge, "--app=" + url + " --user-data-dir=\"" + profile + "\" --no-first-run --window-size=1280,860");
                    else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                catch (Exception e) { AppInfo.Log("janela: " + e.Message); Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                s.Run();
                AppInfo.Log("fim");
                return 0;
            }
        }
    }
}
