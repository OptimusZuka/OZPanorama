// OZPanorama · Zuka Force — núcleo seguro de escrita (Beta 0.1)
// Autor: OptimusZuka. C# 5 (.NET Framework 4.x, compilável com o csc.exe que vem com o Windows).
//
// Regras (docs/PLANO_SEGURANCA_BETA01.md):
//  - Nunca escrever sem: pré-visualização com hash, backup validado, confirmação do hash no momento de escrever.
//  - Bytes preservados fora das linhas-alvo (codificação, BOM, fins de linha).
//  - Escrita atómica: temporário na mesma pasta + File.Replace. Verificação pela releitura. Recuperação automática.
//  - Backups nunca sobrescritos; restauro distingue original / aplicado / alterado depois.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace OZPanorama
{
    public static class AppInfo
    {
        public const string Version = "0.1.2-beta";
        public static string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "OZPanorama");
        public static string BackupDir { get { return Path.Combine(DataDir, "backups"); } }
        public static void Log(string msg)
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                File.AppendAllText(Path.Combine(DataDir, "log.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg + Environment.NewLine, new UTF8Encoding(false));
            }
            catch { }
        }
    }

    /// <summary>Erro com mensagem para o utilizador (sem jargão) e detalhe técnico para o diagnóstico.</summary>
    public class OzError : Exception
    {
        public string Code; public string Detail;
        public OzError(string code, string message, string detail) : base(message) { Code = code; Detail = detail; }
        public OzError(string code, string message) : this(code, message, null) { }
    }

    /// <summary>Pontos de falha para os testes (Etapa 4). Em produção ficam a null.</summary>
    public static class Faults
    {
        public static string Active;   // ex.: "tmp-write", "replace", "after-replace", "backup-copy", "restore-write", "verify-tamper"
        // Cada falha dispara uma só vez (a recuperação que vem a seguir tem de poder escrever).
        public static void Hit(string point) { if (Active == point) { Active = null; throw new IOException("Falha injetada: " + point); } }
        public static bool Is(string point) { if (Active != point) return false; Active = null; return true; }
    }

    public static class Hash
    {
        public static string Sha256(byte[] data)
        {
            using (var sha = SHA256.Create())
            {
                var h = sha.ComputeHash(data); var sb = new StringBuilder(64);
                foreach (var b in h) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    /// <summary>Texto com a codificação original: decodifica e volta a codificar exatamente igual.</summary>
    public class TextFile
    {
        public Encoding Enc; public byte[] Preamble; public string Text; public string EncodingName;

        public static TextFile Decode(byte[] raw)
        {
            var t = new TextFile();
            if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) { t.Enc = new UTF8Encoding(false, true); t.Preamble = new byte[] { 0xEF, 0xBB, 0xBF }; t.EncodingName = "UTF-8 com BOM"; }
            else if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE) { t.Enc = new UnicodeEncoding(false, false, true); t.Preamble = new byte[] { 0xFF, 0xFE }; t.EncodingName = "UTF-16 LE"; }
            else if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF) { t.Enc = new UnicodeEncoding(true, false, true); t.Preamble = new byte[] { 0xFE, 0xFF }; t.EncodingName = "UTF-16 BE"; }
            else
            {
                t.Preamble = new byte[0];
                try { new UTF8Encoding(false, true).GetString(raw); t.Enc = new UTF8Encoding(false, true); t.EncodingName = "UTF-8"; }
                catch (DecoderFallbackException) { t.Enc = Encoding.GetEncoding(1252); t.EncodingName = "Windows-1252"; }
            }
            t.Text = t.Enc.GetString(raw, t.Preamble.Length, raw.Length - t.Preamble.Length);
            // Garantia: a ida e volta tem de dar os mesmos bytes; senão não mexemos no ficheiro.
            if (Hash.Sha256(t.Encode(t.Text)) != Hash.Sha256(raw))
                throw new OzError("encoding", "O formato do ficheiro não é reconhecido com segurança; nada foi alterado.", "ida e volta da codificação " + t.EncodingName + " não reproduz os bytes");
            return t;
        }

        public byte[] Encode(string text)
        {
            var body = Enc.GetBytes(text); var all = new byte[Preamble.Length + body.Length];
            Buffer.BlockCopy(Preamble, 0, all, 0, Preamble.Length); Buffer.BlockCopy(body, 0, all, Preamble.Length, body.Length);
            return all;
        }
    }

    /// <summary>Alteração de linhas: cada regra procura exatamente UMA linha e reescreve só essa linha.</summary>
    public class LineRule
    {
        public string Key;            // nome mostrado ao utilizador
        public Regex Match;           // aplicado ao conteúdo da linha, sem o fim de linha
        public Func<Match, string> Rewrite;   // devolve a linha nova (sem fim de linha)
        public Func<Match, string> Current;   // valor atual legível
        public string NewValue;       // valor novo legível
        public bool AppendIfMissing;  // só para chaves que o jogo aceita acrescentadas (ex.: uset no config.cfg)
        public string AppendLine;     // linha a acrescentar quando falta
    }

    public class Change { public string File; public string Key; public string Current; public string New; public bool Changes; }

    public static class LinePatcher
    {
        // Divide mantendo os fins de linha: cada elemento é o conteúdo e o separador original (\n, \r\n ou nada).
        public static List<KeyValuePair<string, string>> Split(string text)
        {
            var list = new List<KeyValuePair<string, string>>(); int i = 0;
            while (i < text.Length)
            {
                int n = text.IndexOf('\n', i);
                if (n < 0) { list.Add(new KeyValuePair<string, string>(text.Substring(i), "")); break; }
                int end = (n > i && text[n - 1] == '\r') ? n - 1 : n;
                list.Add(new KeyValuePair<string, string>(text.Substring(i, end - i), text.Substring(end, n + 1 - end)));
                i = n + 1;
            }
            return list;
        }

        /// <summary>Aplica as regras. Devolve o texto novo e a lista de mudanças. Lança OzError se uma chave
        /// aparecer 0 (sem AppendIfMissing) ou mais de 1 vez.</summary>
        public static string Apply(string text, IList<LineRule> rules, string fileLabel, List<Change> changes)
        {
            var lines = Split(text);
            var appends = new List<string>();
            foreach (var r in rules)
            {
                int hit = -1, count = 0; Match m = null;
                for (int i = 0; i < lines.Count; i++)
                {
                    var mm = r.Match.Match(lines[i].Key);
                    if (mm.Success) { count++; hit = i; m = mm; }
                }
                if (count > 1) throw new OzError("format", "O ficheiro tem a definição \"" + r.Key + "\" repetida. Por segurança nada foi alterado.", fileLabel + ": " + r.Key + " aparece " + count + " vezes");
                if (count == 0)
                {
                    if (!r.AppendIfMissing) throw new OzError("format", "O ficheiro não tem a definição \"" + r.Key + "\". Abra o jogo uma vez e configure-o antes; nada foi alterado.", fileLabel + ": " + r.Key + " não encontrada");
                    appends.Add(r.AppendLine);
                    changes.Add(new Change { File = fileLabel, Key = r.Key, Current = "(não existe)", New = r.NewValue, Changes = true });
                    continue;
                }
                var cur = r.Current(m); var nl = r.Rewrite(m);
                changes.Add(new Change { File = fileLabel, Key = r.Key, Current = cur, New = r.NewValue, Changes = nl != lines[hit].Key });
                lines[hit] = new KeyValuePair<string, string>(nl, lines[hit].Value);
            }
            var sb = new StringBuilder();
            foreach (var l in lines) sb.Append(l.Key).Append(l.Value);
            if (appends.Count > 0)
            {
                string eol = text.Contains("\r\n") ? "\r\n" : "\n";
                if (sb.Length > 0 && !text.EndsWith("\n")) sb.Append(eol);
                foreach (var a in appends) sb.Append(a).Append(eol);
            }
            return sb.ToString();
        }

        /// <summary>Validação estrutural: fora das linhas-alvo, tudo igual; cada regra encontra o valor novo.</summary>
        public static void Validate(string before, string after, IList<LineRule> rulesAfter, string fileLabel)
        {
            var a = Split(before); var b = Split(after);
            var targetsA = new HashSet<int>(); var targetsB = new HashSet<int>();
            foreach (var r in rulesAfter)
            {
                for (int i = 0; i < a.Count; i++) if (r.Match.IsMatch(a[i].Key)) targetsA.Add(i);
                int hits = 0;
                for (int i = 0; i < b.Count; i++) if (r.Match.IsMatch(b[i].Key)) { targetsB.Add(i); hits++; }
                if (hits != 1) throw new OzError("verify", "A verificação do ficheiro novo falhou; nada foi alterado.", fileLabel + ": " + r.Key + " encontrada " + hits + " vezes depois");
            }
            var restA = new List<string>(); var restB = new List<string>();
            for (int i = 0; i < a.Count; i++) if (!targetsA.Contains(i)) restA.Add(a[i].Key + a[i].Value);
            for (int i = 0; i < b.Count; i++) if (!targetsB.Contains(i)) restB.Add(b[i].Key + b[i].Value);
            if (restA.Count != restB.Count)
                throw new OzError("verify", "A verificação mostrou linhas a mais ou a menos; nada foi alterado.", fileLabel + ": " + restA.Count + " vs " + restB.Count);
            for (int i = 0; i < restA.Count; i++)
            {
                string x = restA[i], y = restB[i];
                // Única diferença tolerada: a última linha original ganhou um fim de linha porque se acrescentou uma chave depois dela.
                bool lastGotEol = i == restA.Count - 1 && (y == x + "\n" || y == x + "\r\n");
                if (x != y && !lastGotEol)
                    throw new OzError("verify", "A verificação mostrou alterações fora das definições do OZPanorama; nada foi alterado.", fileLabel + ": linha " + (i + 1) + " diferente");
            }
        }
    }

    public class Snapshot
    {
        public string Path; public bool Exists; public byte[] Bytes; public string Sha; public string EncodingName;
        public static Snapshot Read(string path)
        {
            var s = new Snapshot { Path = path, Exists = File.Exists(path) };
            if (s.Exists)
            {
                try { s.Bytes = File.ReadAllBytes(path); }
                catch (UnauthorizedAccessException e) { throw new OzError("perm", "O Windows não deixa ler o ficheiro de configuração. Verifique as permissões da pasta.", path + ": " + e.Message); }
                catch (IOException e) { throw new OzError("locked", "O ficheiro está a ser usado por outro programa. Feche o jogo e tente outra vez.", path + ": " + e.Message); }
                s.Sha = Hash.Sha256(s.Bytes);
            }
            return s;
        }
    }

    public static class SafeIO
    {
        /// <summary>Confirma que o ficheiro pode ser aberto para escrita exclusiva (sem escrever nada).</summary>
        public static void CheckWritable(string path)
        {
            if (!File.Exists(path))
            {
                var dir = System.IO.Path.GetDirectoryName(path);
                if (!Directory.Exists(dir)) throw new OzError("missing", "A pasta de configuração do jogo não existe. Abra o jogo uma vez antes.", dir);
                return;
            }
            if ((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0) throw new OzError("perm", "O ficheiro de configuração está marcado como só de leitura. Retire essa opção nas propriedades do ficheiro e tente outra vez.", path);
            try { using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { } }
            catch (UnauthorizedAccessException e) { throw new OzError("perm", "O Windows não deixa alterar o ficheiro de configuração.", path + ": " + e.Message); }
            catch (IOException e) { throw new OzError("locked", "O ficheiro está a ser usado por outro programa (provavelmente o jogo). Feche-o e tente outra vez.", path + ": " + e.Message); }
        }

        /// <summary>Escrita atómica: temporário na mesma pasta, Flush para o disco, releitura, File.Replace.
        /// Se algo falhar antes da substituição, o original fica intacto e o temporário é apagado.</summary>
        public static void AtomicWrite(string path, byte[] data)
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            var tmp = System.IO.Path.Combine(dir, "." + System.IO.Path.GetFileName(path) + ".ozp-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            try
            {
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    if (Faults.Is("tmp-write")) { fs.Write(data, 0, data.Length / 2); fs.Flush(true); throw new IOException("Falha injetada: tmp-write"); }
                    fs.Write(data, 0, data.Length); fs.Flush(true);
                }
                if (Hash.Sha256(File.ReadAllBytes(tmp)) != Hash.Sha256(data)) throw new IOException("O ficheiro temporário não confere depois de escrito.");
                Faults.Hit("replace");
                if (File.Exists(path)) File.Replace(tmp, path, null, true);
                else File.Move(tmp, path);
            }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }
    }

    // ---------------- Backups ----------------

    public class BackupFile { public string Original; public bool Existed; public string Copy; public string ShaOriginal; public string ShaApplied; public long Bytes; }

    public class Backup
    {
        public string Id; public string Dir; public string Game; public string GameName; public string Operation; public string Status;
        public string CreatedUtc; public string CreatedLocal; public string AppVersion; public List<BackupFile> Files = new List<BackupFile>();

        public Dictionary<string, object> ToJson()
        {
            return new Dictionary<string, object> {
                { "schema", "ozpanorama.backup/1" }, { "id", Id }, { "jogo", Game }, { "nomeJogo", GameName }, { "operacao", Operation },
                { "estado", Status }, { "criadoEmUtc", CreatedUtc }, { "criadoEmLocal", CreatedLocal }, { "versaoApp", AppVersion },
                { "ficheiros", Files.Select(f => (object)new Dictionary<string, object> {
                    { "caminho", f.Original }, { "existia", f.Existed }, { "copia", f.Copy == null ? null : System.IO.Path.GetFileName(f.Copy) },
                    { "shaOriginal", f.ShaOriginal }, { "shaAplicado", f.ShaApplied }, { "bytes", f.Bytes } }).ToList() }
            };
        }

        public void SaveManifest()
        {
            var json = new JavaScriptSerializer().Serialize(ToJson());
            SafeIO.AtomicWrite(System.IO.Path.Combine(Dir, "manifesto.json"), new UTF8Encoding(false).GetBytes(json));
        }

        public static Backup Load(string dir)
        {
            var m = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(File.ReadAllText(System.IO.Path.Combine(dir, "manifesto.json"), Encoding.UTF8));
            var b = new Backup
            {
                Dir = dir, Id = (string)m["id"], Game = (string)m["jogo"], GameName = (string)m["nomeJogo"], Operation = (string)m["operacao"],
                Status = (string)m["estado"], CreatedUtc = (string)m["criadoEmUtc"], CreatedLocal = (string)m["criadoEmLocal"], AppVersion = (string)m["versaoApp"]
            };
            foreach (Dictionary<string, object> f in (object[])m["ficheiros"])
                b.Files.Add(new BackupFile
                {
                    Original = (string)f["caminho"], Existed = (bool)f["existia"], Copy = f["copia"] == null ? null : System.IO.Path.Combine(dir, (string)f["copia"]),
                    ShaOriginal = (string)f["shaOriginal"], ShaApplied = (string)f["shaAplicado"], Bytes = Convert.ToInt64(f["bytes"])
                });
            return b;
        }
    }

    public static class BackupStore
    {
        public static string Root = AppInfo.BackupDir;

        /// <summary>Cria um backup novo (pasta única, nunca sobrescreve), copia, relê e confirma cada cópia.</summary>
        public static Backup Create(string game, string gameName, string operation, IList<Snapshot> snaps)
        {
            var now = DateTime.UtcNow; string dir = null, id = null;
            for (int attempt = 0; attempt < 20 && dir == null; attempt++)
            {
                id = now.ToString("yyyyMMdd-HHmmss-fff") + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
                var cand = System.IO.Path.Combine(Root, game, id);
                if (!Directory.Exists(cand)) { Directory.CreateDirectory(cand); dir = cand; }
            }
            if (dir == null) throw new OzError("backup", "Não foi possível criar a pasta do backup; nada foi alterado.");
            var b = new Backup { Id = game + "/" + id, Dir = dir, Game = game, GameName = gameName, Operation = operation, Status = "criado",
                CreatedUtc = now.ToString("o"), CreatedLocal = now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"), AppVersion = AppInfo.Version };
            int n = 0;
            foreach (var s in snaps)
            {
                var f = new BackupFile { Original = s.Path, Existed = s.Exists };
                if (s.Exists)
                {
                    f.Copy = System.IO.Path.Combine(dir, (++n) + "_" + System.IO.Path.GetFileName(s.Path));
                    Faults.Hit("backup-copy");
                    using (var fs = new FileStream(f.Copy, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { fs.Write(s.Bytes, 0, s.Bytes.Length); fs.Flush(true); }
                    if (Hash.Sha256(File.ReadAllBytes(f.Copy)) != s.Sha) throw new OzError("backup", "O backup não ficou igual ao original; nada foi alterado.", f.Copy);
                    f.ShaOriginal = s.Sha; f.Bytes = s.Bytes.LongLength;
                }
                b.Files.Add(f);
            }
            b.SaveManifest();
            // Releitura do manifesto: o backup tem de ser recuperável antes de continuarmos.
            var check = Backup.Load(dir);
            if (check.Files.Count != b.Files.Count) throw new OzError("backup", "O registo do backup não ficou legível; nada foi alterado.", dir);
            return b;
        }

        public static List<Backup> List()
        {
            var all = new List<Backup>();
            if (!Directory.Exists(Root)) return all;
            foreach (var g in Directory.GetDirectories(Root))
                foreach (var d in Directory.GetDirectories(g))
                    if (File.Exists(System.IO.Path.Combine(d, "manifesto.json")))
                        try { all.Add(Backup.Load(d)); } catch { all.Add(new Backup { Id = System.IO.Path.GetFileName(g) + "/" + System.IO.Path.GetFileName(d), Dir = d, Game = System.IO.Path.GetFileName(g), Status = "manifesto-ilegivel" }); }
            return all.OrderByDescending(b => b.CreatedUtc ?? "").ToList();
        }

        public static Backup Find(string id)
        {
            if (id == null || !Regex.IsMatch(id, "^[a-z0-9]+/[0-9]{8}-[0-9]{6}-[0-9]{3}_[0-9a-f]{6}$")) throw new OzError("backup", "Backup desconhecido.", id);
            var dir = System.IO.Path.Combine(Root, id.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!File.Exists(System.IO.Path.Combine(dir, "manifesto.json"))) throw new OzError("backup", "Backup não encontrado.", id);
            return Backup.Load(dir);
        }
    }
}
