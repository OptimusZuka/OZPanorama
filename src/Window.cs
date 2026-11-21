// OZPanorama · Zuka Force — janela própria do programa (WebView2 do Windows), sem depender do navegador.
// Usa o WebView2 Runtime que já vem com o Windows 10/11 e não traz DLLs extra: carrega o EmbeddedBrowserWebView.dll do
// próprio runtime (como o WebView2Loader oficial) e fala com ele pelas tabelas COM (vtables), sem depender de IIDs.
// Se alguma coisa falhar, o programa volta à janela do Edge em modo aplicação, como antes.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OZPanorama
{
    public static class WebWindow
    {
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QIFn(IntPtr self, ref Guid riid, out IntPtr ppv);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint RefFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int DoneFn(IntPtr self, int hr, IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetStrFn(IntPtr self, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetBoolFn(IntPtr self, out int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int PutFn(IntPtr self, IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateEnvFn(int unknown, int runtimeType, [MarshalAs(UnmanagedType.LPWStr)] string userDataDir, IntPtr options, IntPtr handler);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateCtlFn(IntPtr self, IntPtr hwnd, IntPtr handler);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int IntFn(IntPtr self, int value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int RectFn(IntPtr self, RECT value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int VoidFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int OutPtrFn(IntPtr self, out IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int StrFn(IntPtr self, [MarshalAs(UnmanagedType.LPWStr)] string value);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);

        // Vtables (ordem do WebView2.h). IUnknown ocupa 0–2.
        const int EnvCreateController = 3;
        const int CtlPutIsVisible = 4, CtlPutBounds = 6, CtlNotifyParentMoved = 23, CtlClose = 24, CtlGetCoreWebView2 = 25;
        const int WvGetSettings = 3, WvNavigate = 5;
        const int SetPutStatusBar = 10, SetPutDevTools = 12, SetPutZoomControl = 18;
        const int E_NOINTERFACE = unchecked((int)0x80004002);
        static readonly Guid IID_IUnknown = new Guid("00000000-0000-0000-C000-000000000046");
        static readonly Guid IID_Options = new Guid("2fde08a8-1e9a-4766-8c05-95a9ceb9d1c5");

        static readonly List<object> keep = new List<object>();   // delegados e memória nativa vivos enquanto o programa corre

        static T Fn<T>(IntPtr obj, int slot) where T : class
        {
            var vt = Marshal.ReadIntPtr(obj);
            return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(vt, slot * IntPtr.Size), typeof(T)) as T;
        }
        static void Release(IntPtr obj) { if (obj != IntPtr.Zero) Fn<RefFn>(obj, 2)(obj); }
        static void Check(int hr, string what) { if (hr < 0) throw new Exception(what + " falhou (0x" + hr.ToString("X8") + ")"); }

        /// <summary>Objeto COM mínimo feito à mão: um ponteiro para uma vtable de delegados.</summary>
        static IntPtr NativeObject(Guid? onlyIid, params Delegate[] methods)
        {
            IntPtr self = IntPtr.Zero;
            QIFn qi = (IntPtr s, ref Guid riid, out IntPtr ppv) =>
            {
                if (onlyIid == null || riid == IID_IUnknown || riid == onlyIid.Value) { ppv = s; return 0; }
                ppv = IntPtr.Zero; return E_NOINTERFACE;
            };
            RefFn addRef = s => 1, release = s => 1;   // vivem até ao fim do programa
            var all = new List<Delegate> { qi, addRef, release }; all.AddRange(methods);
            var vt = Marshal.AllocHGlobal(IntPtr.Size * all.Count);
            for (int i = 0; i < all.Count; i++) Marshal.WriteIntPtr(vt, i * IntPtr.Size, Marshal.GetFunctionPointerForDelegate(all[i]));
            self = Marshal.AllocHGlobal(IntPtr.Size); Marshal.WriteIntPtr(self, vt);
            keep.AddRange(all); return self;
        }

        static string Arch() { return IntPtr.Size == 4 ? "x86" : (Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE") ?? "").ToUpperInvariant() == "ARM64" ? "arm64" : "x64"; }

        /// <summary>Caminho do EmbeddedBrowserWebView.dll do WebView2 Runtime instalado (estável primeiro), ou null.</summary>
        public static string FindRuntime()
        {
            var channels = new[] { "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}", "{2CD8A007-E189-409D-A2C8-9AF4EF3C72AA}", "{0D50BFEC-CD6A-4F9A-964C-C7416E3ACB10}", "{65C35B14-6C1D-4122-AC46-7148CC9D6497}" };
            foreach (var ch in channels)
                foreach (var hive in new[] { Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryHive.CurrentUser })
                    try
                    {
                        using (var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, Microsoft.Win32.RegistryView.Registry32))
                        using (var k = root.OpenSubKey(@"Software\Microsoft\EdgeUpdate\ClientState\" + ch))
                        {
                            var dir = k == null ? null : k.GetValue("EBWebView") as string;
                            if (string.IsNullOrEmpty(dir)) continue;
                            Version v; if (!Version.TryParse(Path.GetFileName(dir.TrimEnd('\\')), out v) || v < new Version(86, 0, 616, 0)) continue;
                            var dll = Path.Combine(dir, "EBWebView", Arch(), "EmbeddedBrowserWebView.dll");
                            if (File.Exists(dll)) return dll;
                        }
                    }
                    catch { }
            return null;
        }

        /// <summary>Abre a janela e só volta quando ela fecha. false = não foi possível criar a vista (usar o Edge).</summary>
        public static bool Run(string url, string userDataDir, Func<bool> serverStopped)
        {
            Application.EnableVisualStyles();
            var dll = FindRuntime();
            if (dll == null) { AppInfo.Log("janela: WebView2 Runtime não encontrado"); return false; }
            var mod = LoadLibraryW(dll);
            if (mod == IntPtr.Zero) { AppInfo.Log("janela: não carregou " + dll); return false; }
            var proc = GetProcAddress(mod, "CreateWebViewEnvironmentWithOptionsInternal");
            if (proc == IntPtr.Zero) { AppInfo.Log("janela: entrada do WebView2 não encontrada"); return false; }
            var create = (CreateEnvFn)Marshal.GetDelegateForFunctionPointer(proc, typeof(CreateEnvFn));
            keep.Add(create);

            // Sem variáveis de ambiente que mudem o comportamento do runtime.
            foreach (var v in new[] { "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "WEBVIEW2_BROWSER_EXECUTABLE_FOLDER", "WEBVIEW2_USER_DATA_FOLDER", "WEBVIEW2_PIPE_FOR_SCRIPT_DEBUGGER" }) Environment.SetEnvironmentVariable(v, null);

            bool ready = false, failed = false; IntPtr controller = IntPtr.Zero;
            var form = new Form
            {
                Text = "OZPanorama · Zuka Force", StartPosition = FormStartPosition.CenterScreen, BackColor = Color.FromArgb(14, 15, 18),
                Size = new Size(1280, 860), MinimumSize = new Size(900, 600)
            };
            try { form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            try
            {
                var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
                form.Size = new Size(Math.Min(1280, wa.Width - 40), Math.Min(860, wa.Height - 40));
            }
            catch { }

            Action fit = () =>
            {
                if (controller == IntPtr.Zero) return;
                var r = form.ClientRectangle;
                Fn<RectFn>(controller, CtlPutBounds)(controller, new RECT { Left = 0, Top = 0, Right = r.Width, Bottom = r.Height });
            };
            Action<string> fail = why =>
            {
                AppInfo.Log("janela: " + why);
                if (!ready) { failed = true; form.BeginInvoke(new Action(form.Close)); }
            };

            Func<string, IntPtr> str = s => Marshal.StringToCoTaskMemUni(s ?? "");
            var options = NativeObject(IID_Options,
                (GetStrFn)((IntPtr s, out IntPtr v) => { v = str(""); return 0; }), (PutFn)((s, v) => 0),            // AdditionalBrowserArguments
                (GetStrFn)((IntPtr s, out IntPtr v) => { v = str(""); return 0; }), (PutFn)((s, v) => 0),            // Language
                (GetStrFn)((IntPtr s, out IntPtr v) => { v = str("86.0.616.0"); return 0; }), (PutFn)((s, v) => 0),  // TargetCompatibleBrowserVersion
                (GetBoolFn)((IntPtr s, out int v) => { v = 0; return 0; }), (PutFn)((s, v) => 0));                   // AllowSingleSignOnUsingOSPrimaryAccount

            var controllerDone = NativeObject(null, (DoneFn)((s, hr, ctl) =>
            {
                try
                {
                    if (hr < 0 || ctl == IntPtr.Zero) { fail("controlador 0x" + hr.ToString("X8")); return 0; }
                    Fn<RefFn>(ctl, 1)(ctl); controller = ctl;
                    IntPtr wv; Check(Fn<OutPtrFn>(ctl, CtlGetCoreWebView2)(ctl, out wv), "CoreWebView2");
                    IntPtr st;
                    if (Fn<OutPtrFn>(wv, WvGetSettings)(wv, out st) >= 0 && st != IntPtr.Zero)
                    {
                        Fn<IntFn>(st, SetPutStatusBar)(st, 0); Fn<IntFn>(st, SetPutDevTools)(st, 0); Fn<IntFn>(st, SetPutZoomControl)(st, 0);
                        Release(st);
                    }
                    fit();
                    Fn<IntFn>(ctl, CtlPutIsVisible)(ctl, 1);
                    Check(Fn<StrFn>(wv, WvNavigate)(wv, url), "Navigate");
                    Release(wv);
                    ready = true; AppInfo.Log("janela: WebView2 pronta");
                }
                catch (Exception e) { fail(e.Message); }
                return 0;
            }));

            var envDone = NativeObject(null, (DoneFn)((s, hr, env) =>
            {
                try
                {
                    if (hr < 0 || env == IntPtr.Zero) { fail("ambiente 0x" + hr.ToString("X8")); return 0; }
                    Check(Fn<CreateCtlFn>(env, EnvCreateController)(env, form.Handle, controllerDone), "CreateCoreWebView2Controller");
                }
                catch (Exception e) { fail(e.Message); }
                return 0;
            }));

            form.Shown += (o, e) =>
            {
                try { Check(create(1, 0, userDataDir, options, envDone), "CreateWebViewEnvironment"); }
                catch (Exception ex) { fail(ex.Message); }
            };
            form.Resize += (o, e) => { try { fit(); } catch { } };
            form.Move += (o, e) => { try { if (controller != IntPtr.Zero) Fn<VoidFn>(controller, CtlNotifyParentMoved)(controller); } catch { } };
            form.FormClosed += (o, e) => { try { if (controller != IntPtr.Zero) { Fn<VoidFn>(controller, CtlClose)(controller); Release(controller); controller = IntPtr.Zero; } } catch { } };
            // Se a interface pedir "Sair" (ou o servidor parar), a janela fecha também.
            var timer = new Timer { Interval = 500 };
            timer.Tick += (o, e) => { if (serverStopped()) { timer.Stop(); form.Close(); } };
            timer.Start();

            Application.Run(form);
            timer.Dispose();
            return !failed;
        }
    }
}
