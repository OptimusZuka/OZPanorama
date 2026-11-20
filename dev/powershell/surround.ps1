# OZPanorama · Zuka Force — perfis do NVIDIA Surround (Windows PowerShell 5.1, sem instalar nada)
#
# Guarda a configuração de ecrãs atual TAL COMO O DRIVER A DESCREVE (NvAPI_Mosaic_EnumDisplayGrids) e repõe-na
# mais tarde (NvAPI_Mosaic_ValidateDisplayGrids + NvAPI_Mosaic_SetDisplayGrids). Nada é construído à mão: o
# programa só repõe o que o próprio driver devolveu, com as molduras incluídas.
# Segurança: valida antes de aplicar; depois de aplicar pede confirmação e, sem resposta em 15 s, repõe o anterior.
#
# Fontes (docs.nvidia.com/nvapi, release 590; github.com/NVIDIA/nvapi, nvapi_interface.h):
#   NV_MOSAIC_GRID_TOPO_V2 = version, rows, columns, displayCount, bitfields (32 bits),
#     displays[NV_MOSAIC_MAX_DISPLAYS = 64] de NV_MOSAIC_GRID_TOPO_DISPLAY_V2 (7 × 4 bytes), displaySettings V1 (5 × 4 bytes)
#     → 20 + 64×28 + 20 = 1832 bytes; versão = 1832 | (2 << 16). pack(8), todos os campos de 4 bytes.
#   NV_MOSAIC_DISPLAY_TOPO_STATUS = version, errorFlags, warningFlags, displayCount,
#     displays[NVAPI_MAX_DISPLAYS = 32 × 4 = 128] de (displayId, errorFlags, warningFlags, bitfields) → 16 + 128×16 = 2064; versão 1.
#   IDs nvapi_QueryInterface: Initialize 0x0150E828, EnumDisplayGrids 0xDF2887AF,
#     ValidateDisplayGrids 0xCF43903D, SetDisplayGrids 0x4D959A89.
#   "Enumerates the current active grid topologies [...] as well as single displays" → sem Surround, cada monitor é uma grelha 1×1.
#
# Uso: powershell -File surround.ps1 -Action status
#      powershell -File surround.ps1 -Action save  -Perfil com|sem
#      powershell -File surround.ps1 -Action apply -Perfil com|sem [-RequestId x]
param([string]$Action = '', [string]$Perfil = '', [string]$RequestId = '')

$ErrorActionPreference = 'Stop'
$SurDocs = [Environment]::GetFolderPath('MyDocuments')
$SurDir  = Join-Path (Join-Path $SurDocs 'OZPanorama') 'surround'
New-Item -ItemType Directory -Force -Path $SurDir | Out-Null
$SurProfiles = @('com', 'sem')
Add-Type -AssemblyName System.Windows.Forms, System.Drawing
if (-not ('OzSurDpi' -as [type])) {
  Add-Type -TypeDefinition 'using System.Runtime.InteropServices; public static class OzSurDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }'
}
[void][OzSurDpi]::SetProcessDPIAware()   # larguras do ambiente de trabalho em píxeis reais

# Posição dos ecrãs no ambiente de trabalho (o driver da NVIDIA não a guarda; é do Windows).
# ChangeDisplaySettingsEx com DM_POSITION, primeiro só no registo (CDS_NORESET) e depois aplicado de uma vez.
if (-not ('OzDisp' -as [type])) {
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class OzDisp {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct DEVMODE {
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
    public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
    public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
    public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
    public short dmLogPixels;
    public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
    public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
  }
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string dev, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr param);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int ChangeDisplaySettingsEx(string dev, IntPtr dm, IntPtr hwnd, int flags, IntPtr param);
  const int ENUM_CURRENT_SETTINGS = -1, DM_POSITION = 0x20, CDS_UPDATEREGISTRY = 0x1, CDS_SET_PRIMARY = 0x10, CDS_NORESET = 0x10000000;
  // Ecrã principal: o Windows exige-o em (0, 0); os outros posicionam-se em relação a ele.
  public static void SetPrimary(string dev) {
    DEVMODE dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
    if (!EnumDisplaySettings(dev, ENUM_CURRENT_SETTINGS, ref dm)) throw new Exception("Não consegui ler " + dev + ".");
    dm.dmFields = DM_POSITION; dm.dmPositionX = 0; dm.dmPositionY = 0;
    int r = ChangeDisplaySettingsEx(dev, ref dm, IntPtr.Zero, CDS_SET_PRIMARY | CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero);
    if (r != 0) throw new Exception("O Windows recusou tornar " + dev + " o ecrã principal (código " + r + ").");
  }
  public static void SetPos(string dev, int x, int y) {
    DEVMODE dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
    if (!EnumDisplaySettings(dev, ENUM_CURRENT_SETTINGS, ref dm)) throw new Exception("Não consegui ler " + dev + ".");
    dm.dmFields = DM_POSITION; dm.dmPositionX = x; dm.dmPositionY = y;
    int r = ChangeDisplaySettingsEx(dev, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY | CDS_NORESET, IntPtr.Zero);
    if (r != 0) throw new Exception("O Windows recusou mudar a posição de " + dev + " (código " + r + ").");
  }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct DISPLAY_DEVICE {
    public int cb;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
    public int StateFlags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
  }
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string dev, uint i, ref DISPLAY_DEVICE dd, uint flags);
  // Lista atual dos ecrãs do ambiente de trabalho, lida ao Windows em cada chamada.
  // (System.Windows.Forms.Screen guarda a lista em cache num processo sem ciclo de mensagens: ficava desatualizada.)
  public static string[] Screens() {
    var list = new System.Collections.Generic.List<string>();
    for (uint i = 0; i < 64; i++) {
      DISPLAY_DEVICE dd = new DISPLAY_DEVICE(); dd.cb = Marshal.SizeOf(typeof(DISPLAY_DEVICE));
      if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
      if ((dd.StateFlags & 0x1) == 0) continue;   // DISPLAY_DEVICE_ATTACHED_TO_DESKTOP
      DEVMODE dm = new DEVMODE(); dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
      if (!EnumDisplaySettings(dd.DeviceName, ENUM_CURRENT_SETTINGS, ref dm)) continue;
      bool primary = (dd.StateFlags & 0x4) != 0;   // DISPLAY_DEVICE_PRIMARY_DEVICE
      list.Add(dd.DeviceName + "|" + dm.dmPositionX + "|" + dm.dmPositionY + "|" + dm.dmPelsWidth + "|" + dm.dmPelsHeight + "|" + (primary ? "1" : "0"));
    }
    return list.ToArray();
  }
  public static void Commit() {
    int r = ChangeDisplaySettingsEx(null, IntPtr.Zero, IntPtr.Zero, 0, IntPtr.Zero);
    if (r != 0) throw new Exception("O Windows recusou aplicar as posições (código " + r + ").");
  }
}
"@
}

if (-not ('OzNvapi' -as [type])) {
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class OzNvapi {
  [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
  static extern IntPtr QueryInterface(uint id);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FInit();
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FEnum(IntPtr grids, ref uint count);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FValidate(uint flags, IntPtr grids, IntPtr status, uint count);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int FSet(IntPtr grids, uint count, uint flags);

  public const int GridSize = 1832;
  public const uint GridVer = 1832u | (2u << 16);
  public const int StatusSize = 2064;
  public const uint StatusVer = 2064u | (1u << 16);
  static bool ready = false;

  static Delegate Fn(uint id, Type t) {
    IntPtr p = QueryInterface(id);
    if (p == IntPtr.Zero) throw new Exception("Função NVAPI 0x" + id.ToString("X8") + " não disponível neste driver.");
    return Marshal.GetDelegateForFunctionPointer(p, t);
  }
  static void Check(int r, string what) {
    if (r != 0) throw new Exception(what + " devolveu o código NVAPI " + r + ".");
  }
  public static void Init() {
    if (ready) return;
    FInit f = (FInit)Fn(0x0150E828u, typeof(FInit));
    Check(f(), "NvAPI_Initialize");
    ready = true;
  }
  // Devolve as grelhas ativas como blocos de GridSize bytes, exatamente como o driver as escreveu.
  public static byte[] EnumGrids(out uint count) {
    Init();
    FEnum f = (FEnum)Fn(0xDF2887AFu, typeof(FEnum));
    count = 0;
    Check(f(IntPtr.Zero, ref count), "NvAPI_Mosaic_EnumDisplayGrids (contagem)");
    if (count == 0 || count > 64) throw new Exception("O driver indicou " + count + " grelhas.");
    uint n = count;
    IntPtr buf = Marshal.AllocHGlobal(GridSize * (int)n);
    try {
      Marshal.Copy(new byte[GridSize * (int)n], 0, buf, GridSize * (int)n);
      for (int i = 0; i < n; i++) Marshal.WriteInt32(buf, i * GridSize, (int)GridVer);
      Check(f(buf, ref count), "NvAPI_Mosaic_EnumDisplayGrids");
      if (count > n) throw new Exception("O número de grelhas mudou durante a leitura.");
      byte[] outb = new byte[GridSize * (int)count];
      Marshal.Copy(buf, outb, 0, outb.Length);
      return outb;
    } finally { Marshal.FreeHGlobal(buf); }
  }
  // Valida as grelhas. Devolve null se o driver aceita; senão uma descrição dos erros.
  public static string Validate(byte[] grids, uint count) {
    Init();
    FValidate f = (FValidate)Fn(0xCF43903Du, typeof(FValidate));
    IntPtr g = Marshal.AllocHGlobal(grids.Length);
    int stBytes = StatusSize * (int)count + 4096;   // folga: nunca escrever para lá do buffer
    IntPtr st = Marshal.AllocHGlobal(stBytes);
    try {
      Marshal.Copy(grids, 0, g, grids.Length);
      Marshal.Copy(new byte[stBytes], 0, st, stBytes);
      for (int i = 0; i < count; i++) Marshal.WriteInt32(st, i * StatusSize, (int)StatusVer);
      Check(f(0, g, st, count), "NvAPI_Mosaic_ValidateDisplayGrids");
      string err = null;
      for (int i = 0; i < count; i++) {
        int b = i * StatusSize, ef = Marshal.ReadInt32(st, b + 4), dc = Marshal.ReadInt32(st, b + 12);
        if (ef != 0) err = (err ?? "") + "grelha " + i + ": erro 0x" + ef.ToString("X") + "; ";
        for (int d = 0; d < dc && d < 128; d++) {
          int de = Marshal.ReadInt32(st, b + 16 + d * 16 + 4);
          if (de != 0) err = (err ?? "") + "grelha " + i + " ecrã " + d + ": erro 0x" + de.ToString("X") + "; ";
        }
      }
      return err;
    } finally { Marshal.FreeHGlobal(g); Marshal.FreeHGlobal(st); }
  }
  // Devolve o código do driver em vez de lançar: NVAPI_TIMEOUT (-191) acontece quando a mudança de modo
  // demora mais do que o driver espera, mas a mudança pode ter sido feita. Quem chama verifica o estado real.
  public static int SetGrids(byte[] grids, uint count) {
    Init();
    FSet f = (FSet)Fn(0x4D959A89u, typeof(FSet));
    IntPtr g = Marshal.AllocHGlobal(grids.Length);
    try {
      Marshal.Copy(grids, 0, g, grids.Length);
      return f(g, count, 0);
    } finally { Marshal.FreeHGlobal(g); }
  }
}
"@
}

$GridSize = 1832

function SurU32([byte[]]$b, [int]$o) { [BitConverter]::ToUInt32($b, $o) }
function SurS32([byte[]]$b, [int]$o) { [BitConverter]::ToInt32($b, $o) }

# Descreve as grelhas em linguagem legível e confirma que os bytes fazem sentido antes de os usar.
function SurDecode([byte[]]$blob) {
  if (-not $blob -or ($blob.Length % $GridSize) -ne 0) { throw "Perfil com tamanho inválido ($($blob.Length) bytes)." }
  $grids = @()
  for ($i = 0; $i -lt $blob.Length / $GridSize; $i++) {
    $o = $i * $GridSize
    $ver = SurU32 $blob $o
    if ($ver -ne (1832 -bor (2 -shl 16))) { throw "Grelha $i com versão inesperada (0x$('{0:X}' -f $ver))." }
    $rows = SurU32 $blob ($o + 4); $cols = SurU32 $blob ($o + 8); $n = SurU32 $blob ($o + 12); $bits = SurU32 $blob ($o + 16)
    if ($n -lt 1 -or $n -gt 64 -or $rows * $cols -ne $n) { throw "Grelha $i incoerente: $rows×$cols com $n ecrãs." }
    $disp = @()
    for ($d = 0; $d -lt $n; $d++) {
      $p = $o + 20 + $d * 28
      $disp += @{ id = (SurU32 $blob ($p + 4)); overlapX = (SurS32 $blob ($p + 8)); overlapY = (SurS32 $blob ($p + 12)); rotation = (SurU32 $blob ($p + 16)) }
    }
    $s = $o + 20 + 64 * 28
    $w = SurU32 $blob ($s + 4); $h = SurU32 $blob ($s + 8); $hz = SurU32 $blob ($s + 16)
    if ($w -lt 320 -or $w -gt 16384 -or $h -lt 240 -or $h -gt 16384) { throw "Grelha $i com resolução impossível ($w×$h)." }
    $grids += @{ rows = $rows; columns = $cols; displays = $disp; width = $w; height = $h; hz = $hz; bezelCorrect = [bool]($bits -band 1) }
  }
  return ,$grids
}

function SurText($grids) {
  ($grids | ForEach-Object {
    $ov = @($_.displays | ForEach-Object { $_.overlapX } | Where-Object { $_ -ne 0 } | Sort-Object -Unique)
    $mold = if ($ov.Count) { ' · molduras ' + (($ov | ForEach-Object { "$(-$_) px" }) -join '/') } else { '' }
    $kind = if ($_.displays.Count -gt 1) { "Surround $($_.columns)×$($_.rows)" } else { 'ecrã separado' }
    "$kind · $($_.width)×$($_.height) @ $($_.hz) Hz$mold"
  }) -join ' | '
}

# Assinatura da disposição: mesmos ecrãs, mesma grelha e mesmas molduras = mesma configuração.
function SurKey($grids) {
  ($grids | ForEach-Object { "$($_.rows)x$($_.columns):" + (($_.displays | ForEach-Object { "$($_.id)/$($_.overlapX)/$($_.overlapY)" }) -join ',') + ":$($_.width)x$($_.height)@$($_.hz)" } | Sort-Object) -join ';'
}

function SurScreens {
  @([OzDisp]::Screens() | ForEach-Object { $f = $_ -split '\|'; @{ device = $f[0]; x = [int]$f[1]; y = [int]$f[2]; w = [int]$f[3]; h = [int]$f[4]; primary = ($f[5] -eq '1') } })
}

# Repõe o ecrã principal e a posição dos ecrãs guardados com o perfil (o driver da NVIDIA não os guarda).
# Cada ecrã guardado é procurado pelo tamanho; só se mexe quando a correspondência é inequívoca
# (um único ecrã com aquele tamanho agora e no perfil). Nos outros casos só informa.
function SurFixPositions($saved) {
  if (-not $saved) { return '' }
  $saved = @($saved); $cur = @(SurScreens); $notes = @(); $plan = @()
  foreach ($s in $saved) {
    $cands = @($cur | Where-Object { $_.w -eq $s.w -and $_.h -eq $s.h })
    $same = @($saved | Where-Object { $_.w -eq $s.w -and $_.h -eq $s.h })
    if ($cands.Count -ne 1 -or $same.Count -ne 1) { $notes += "ecrã $($s.w)×$($s.h) ambíguo: posição não reposta"; continue }
    $plan += @{ s = $s; c = $cands[0] }
  }
  $sp = @($plan | Where-Object { $_.s.primary })
  $needPrimary = $sp.Count -eq 1 -and -not $sp[0].c.primary
  $needPos = @($plan | Where-Object { $_.c.x -ne $_.s.x -or $_.c.y -ne $_.s.y }).Count -gt 0
  if (-not $needPrimary -and -not $needPos) { return ($notes -join '; ') }
  if ($sp.Count -ne 1) { return (($notes + 'sem ecrã principal identificável no perfil: posições não repostas') -join '; ') }
  # Coordenadas guardadas já são relativas ao principal guardado (que estava em 0,0).
  [OzDisp]::SetPrimary($sp[0].c.device)
  foreach ($e in $plan) { if (-not $e.s.primary) { [OzDisp]::SetPos($e.c.device, [int]$e.s.x, [int]$e.s.y) } }
  [OzDisp]::Commit(); Start-Sleep -Seconds 1
  $notes += "ecrã principal: $($sp[0].s.w)×$($sp[0].s.h)"
  return ($notes -join '; ')
}

# Perfis guardados antes de existir a disposição: o ecrã mais largo (o Surround) passa a principal e os outros
# mantêm a posição relativa. Regra declarada na mensagem; depois do "Manter" a disposição real fica gravada.
function SurWidestPrimary {
  $cur = @(SurScreens); $wide = $cur | Sort-Object { $_.w } -Descending | Select-Object -First 1
  if (-not $wide -or $wide.primary -or $cur.Count -lt 2) { return '' }
  [OzDisp]::SetPrimary($wide.device)
  foreach ($c in $cur) { if ($c.device -ne $wide.device) { [OzDisp]::SetPos($c.device, [int]($c.x - $wide.x), [int]($c.y - $wide.y)) } }
  [OzDisp]::Commit(); Start-Sleep -Seconds 1
  return "ecrã principal: $($wide.w)×$($wide.h) (o mais largo; o perfil ainda não tinha a disposição)"
}

# Depois de o utilizador confirmar, grava no perfil a disposição real (ecrã principal e posições).
function SurRememberScreens($p) {
  $meta = "$(SurProfilePath $p).json"
  $m = Get-Content -Raw -LiteralPath $meta | ConvertFrom-Json
  $o = @{ saved = $m.saved; sha256 = $m.sha256; text = $m.text; bezelForced = $m.bezelForced; desktop = (SurDesktop); screens = @(SurScreens) }
  ($o | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath $meta -Encoding UTF8
}

function SurDesktop {
  (@(SurScreens | Sort-Object { $_.x } | ForEach-Object { "$($_.w)×$($_.h)" })) -join ' + '
}

function SurCurrent {
  $count = [uint32]0
  $blob = [OzNvapi]::EnumGrids([ref]$count)
  $g = SurDecode $blob
  return @{ blob = $blob; count = $count; grids = $g; text = (SurText $g); key = (SurKey $g); surround = [bool](@($g | Where-Object { $_.displays.Count -gt 1 }).Count) }
}

function SurProfilePath($p) {
  if ($SurProfiles -notcontains $p) { throw "Perfil desconhecido: $p" }
  return (Join-Path $SurDir "$p.bin")
}

function SurLoad($p) {
  $bin = SurProfilePath $p; $meta = "$bin.json"
  if (-not (Test-Path -LiteralPath $bin) -or -not (Test-Path -LiteralPath $meta)) { return $null }
  $m = Get-Content -Raw -LiteralPath $meta | ConvertFrom-Json
  $blob = [IO.File]::ReadAllBytes($bin)
  $sha = ([BitConverter]::ToString((New-Object Security.Cryptography.SHA256Managed).ComputeHash($blob)) -replace '-', '').ToLower()
  if ($sha -ne $m.sha256) { throw "O perfil '$p' foi alterado fora do OZPanorama (hash não confere). Guarde-o de novo." }
  $g = SurDecode $blob
  return @{ blob = $blob; count = [uint32]$g.Count; grids = $g; text = (SurText $g); key = (SurKey $g); saved = $m.saved; screens = $m.screens }
}

function SurSave($p) {
  $cur = SurCurrent
  $isOn = $cur.surround
  if ($p -eq 'com' -and -not $isOn) { throw 'O Surround está desligado agora. Ligue-o no painel da NVIDIA com as suas molduras e guarde de novo.' }
  if ($p -eq 'sem' -and $isOn) { throw 'O Surround está ligado agora. Desligue-o no painel da NVIDIA e guarde de novo.' }
  $bin = SurProfilePath $p
  # Com molduras (sobreposição negativa), pede ao driver a resolução com correção de molduras ao aplicar.
  # Campo documentado: applyWithBezelCorrect = "When enabling and doing the modeset, do we switch to the bezel-corrected resolution".
  $forced = $false
  for ($i = 0; $i -lt $cur.grids.Count; $i++) {
    $gr = $cur.grids[$i]
    if ($gr.displays.Count -gt 1 -and @($gr.displays | Where-Object { $_.overlapX -lt 0 -or $_.overlapY -lt 0 }).Count -and -not $gr.bezelCorrect) {
      $o = $i * $GridSize + 16
      $cur.blob[$o] = [byte]($cur.blob[$o] -bor 1); $forced = $true
    }
  }
  [IO.File]::WriteAllBytes($bin, $cur.blob)
  $sha = ([BitConverter]::ToString((New-Object Security.Cryptography.SHA256Managed).ComputeHash($cur.blob)) -replace '-', '').ToLower()
  (@{ saved = (Get-Date).ToString('yyyy-MM-dd HH:mm'); sha256 = $sha; text = $cur.text; bezelForced = $forced; desktop = (SurDesktop); screens = @(SurScreens) } | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath "$bin.json" -Encoding UTF8
  return @{ ok = $true; profile = $p; text = $cur.text }
}

function SurStatus {
  $out = @{ ok = $true; available = $false; profiles = @{}; last = $null }
  try { $cur = SurCurrent; $out.available = $true; $out.current = @{ text = $cur.text; surround = $cur.surround } }
  catch { $out.error = "$($_.Exception.Message)" }
  $out.desktop = SurDesktop
  foreach ($p in $SurProfiles) {
    try { $l = SurLoad $p; if ($l) { $out.profiles[$p] = @{ text = $l.text; saved = $l.saved; active = [bool]($out.available -and $l.key -eq $cur.key) } } }
    catch { $out.profiles[$p] = @{ error = "$($_.Exception.Message)" } }
  }
  $lf = Join-Path $SurDir 'ultimo.json'
  if (Test-Path -LiteralPath $lf) { $out.last = Get-Content -Raw -LiteralPath $lf | ConvertFrom-Json }
  return $out
}

# Aplica grelhas e confirma pelo estado real. 0 = OK; -191 = NVAPI_TIMEOUT (docs.nvidia.com/nvapi, NvAPI Status Values):
# nesse caso espera até 15 s que o driver fique com a configuração pedida.
function SurSetAndVerify($blob, $count, $key) {
  $code = [OzNvapi]::SetGrids($blob, $count)
  if ($code -ne 0 -and $code -ne -191) { throw "O driver recusou a mudança (código NVAPI $code)." }
  for ($i = 0; $i -lt 15; $i++) {
    Start-Sleep -Seconds 1
    try { if ((SurCurrent).key -eq $key) { return $code } } catch { }
  }
  if ($code -eq -191) { throw 'O driver esgotou o tempo (NVAPI_TIMEOUT) e os ecrãs não ficaram como o perfil.' }
  return $code   # aceite pelo driver; a comparação seguinte diz se ficou diferente
}

function SurWriteLast($obj) {
  ($obj | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath (Join-Path $SurDir 'ultimo.json') -Encoding UTF8
}

# Janela "Manter esta configuração?" com contagem decrescente, sempre por cima, no ecrã principal.
# Confirmação por dois caminhos: a janela do Windows OU o botão "Manter" na interface do OZPanorama
# (o servidor cria o ficheiro manter_<id>.flag). Sem nenhum dos dois, repõe-se o anterior.
function SurAskKeep([int]$seconds, [string]$rid) {
  $flag = Join-Path $SurDir "manter_$rid.flag"
  Remove-Item -LiteralPath $flag -ErrorAction SilentlyContinue
  Add-Type -AssemblyName System.Windows.Forms
  $f = New-Object Windows.Forms.Form
  $f.Text = 'OZPanorama · Surround'; $f.TopMost = $true; $f.StartPosition = 'CenterScreen'
  $f.FormBorderStyle = 'FixedDialog'; $f.MaximizeBox = $false; $f.MinimizeBox = $false
  $f.ClientSize = New-Object Drawing.Size(420, 150); $f.BackColor = [Drawing.Color]::FromArgb(11, 11, 12); $f.ForeColor = [Drawing.Color]::White
  $lbl = New-Object Windows.Forms.Label; $lbl.SetBounds(16, 16, 390, 60); $lbl.Font = New-Object Drawing.Font('Segoe UI', 11)
  $keep = New-Object Windows.Forms.Button; $keep.Text = 'Manter'; $keep.SetBounds(196, 96, 100, 34); $keep.BackColor = [Drawing.Color]::FromArgb(245, 196, 0); $keep.ForeColor = [Drawing.Color]::Black; $keep.FlatStyle = 'Flat'
  $back = New-Object Windows.Forms.Button; $back.Text = 'Repor'; $back.SetBounds(306, 96, 100, 34); $back.FlatStyle = 'Flat'
  $state = @{ left = $seconds; keep = $false }
  $upd = { $lbl.Text = "Os ecrãs estão bem?`nSem resposta, o OZPanorama repõe a configuração anterior em $($state.left) s." }
  & $upd
  $t = New-Object Windows.Forms.Timer; $t.Interval = 1000
  $t.Add_Tick({
    $state.left = $state.left - 1; & $upd
    if ($rid -and (Test-Path -LiteralPath $flag)) { $state.keep = $true; $t.Stop(); $f.Close(); return }
    if ($state.left -le 0) { $t.Stop(); $f.Close() }
  })
  # Uma janela de um processo em segundo plano nem sempre ganha o primeiro plano: força-a.
  $f.Add_Shown({ $f.TopMost = $true; $f.Activate(); $f.BringToFront() })
  $keep.Add_Click({ $state.keep = $true; $t.Stop(); $f.Close() })
  $back.Add_Click({ $t.Stop(); $f.Close() })
  $f.Controls.AddRange(@($lbl, $keep, $back)); $f.AcceptButton = $keep
  $t.Start(); [void]$f.ShowDialog(); $t.Dispose(); $f.Dispose()
  Remove-Item -LiteralPath $flag -ErrorAction SilentlyContinue
  return $state.keep
}

function SurApply($p, $rid) {
  $res = @{ id = $rid; profile = $p; at = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss'); ok = $false }
  $prev = $null
  try {
    $target = SurLoad $p
    if (-not $target) { throw "Ainda não há perfil '$p'. Guarde-o primeiro." }
    $prev = SurCurrent
    if ($prev.key -eq $target.key) {
      # O driver já está assim, mas a disposição do Windows (principal/posições) pode não estar.
      $pos = if ($target.screens) { SurFixPositions $target.screens } elseif ($p -eq 'com') { SurWidestPrimary } else { '' }
      if ($pos) { try { SurRememberScreens $p } catch { } }
      $res.ok = $true; $res.message = if ($pos) { "Surround já ligado; $pos." } else { 'Já estava assim; nada mudou.' }
      SurWriteLast $res; return $res
    }
    $v = [OzNvapi]::Validate($target.blob, $target.count)
    if ($v) { throw "O driver recusou o perfil ($v). Os ecrãs ou as ligações podem ter mudado: guarde o perfil de novo." }
    $code = SurSetAndVerify $target.blob $target.count $target.key
    if ($code -eq -191) { $res.note = 'O driver demorou mais do que o normal (NVAPI_TIMEOUT), mas a mudança foi feita.' }
    $pos = ''
    try { $pos = if ($target.screens) { SurFixPositions $target.screens } elseif ($p -eq 'com') { SurWidestPrimary } else { '' } } catch { $pos = "posições: $($_.Exception.Message)" }
    $now = SurCurrent
    $res.waiting = $true; $res.message = 'À espera de confirmação (20 s).'; SurWriteLast $res; $res.Remove('waiting')
    if (SurAskKeep 20 $rid) {
      $res.ok = $true; $res.message = "Aplicado: $($now.text). Windows: $(SurDesktop)."
      try { SurRememberScreens $p } catch { $res.message += " (não consegui gravar a disposição no perfil: $($_.Exception.Message))" }
      if ($pos) { $res.message += " Posição: $pos." }
      if ($now.key -ne $target.key) { $res.message += " (atenção: o driver não ficou exatamente igual ao perfil)" }
    } else {
      [void](SurSetAndVerify $prev.blob $prev.count $prev.key)
      $res.message = "Reposto o anterior: $($prev.text)"
    }
  } catch {
    $res.message = "$($_.Exception.Message)"
    # Se os ecrãs já mudaram e algo falhou depois, volta ao que estava (só se soubermos o que era).
    if ($prev) { try { if ((SurCurrent).key -ne $prev.key) { [void](SurSetAndVerify $prev.blob $prev.count $prev.key); $res.message += " Reposto o anterior." } } catch { $res.message += " Não consegui repor o anterior: $($_.Exception.Message)" } }
  }
  if ($res.note) { $res.message += " $($res.note)"; $res.Remove('note') }
  SurWriteLast $res
  return $res
}

if ($Action) {
  switch ($Action) {
    'status' { SurStatus | ConvertTo-Json -Depth 5 }
    'diag'   {
      # Só leitura: confirma tamanhos e versões contra o driver real, sem mudar nada.
      "OZPanorama · diagnóstico do Surround · $(Get-Date)"
      "Windows: $([Environment]::OSVersion.VersionString) · 64 bits: $([Environment]::Is64BitProcess)"
      "Ambiente de trabalho: $(SurDesktop)"
      foreach ($sc in SurScreens) { "   $($sc.device): $($sc.w)×$($sc.h) em ($($sc.x), $($sc.y))$(if ($sc.primary) { ' · principal' })" }
      try {
        $c = SurCurrent
        "Grelhas: $($c.count) · Surround ativo: $($c.surround)"
        "Descrição: $($c.text)"
        for ($i = 0; $i -lt $c.grids.Count; $i++) {
          $g = $c.grids[$i]
          "Grelha ${i}: $($g.columns)×$($g.rows) · $($g.width)×$($g.height) @ $($g.hz) Hz · bezelCorrect=$($g.bezelCorrect)"
          foreach ($d in $g.displays) { "   ecrã 0x$('{0:X8}' -f $d.id) overlapX=$($d.overlapX) overlapY=$($d.overlapY) rotação=$($d.rotation)" }
        }
        "Validação do estado atual: $(if ($v = [OzNvapi]::Validate($c.blob, $c.count)) { $v } else { 'aceite pelo driver' })"
      } catch { "ERRO: $($_.Exception.Message)" }
      foreach ($p in $SurProfiles) { try { $l = SurLoad $p; "Perfil ${p}: $(if ($l) { $l.text } else { 'não guardado' })" } catch { "Perfil ${p}: ERRO $($_.Exception.Message)" } }
    }
    'save'   { SurSave $Perfil | ConvertTo-Json -Depth 5 }
    'apply'  { SurApply $Perfil $RequestId | ConvertTo-Json -Depth 5 }
    default  { throw "Ação desconhecida: $Action" }
  }
}
