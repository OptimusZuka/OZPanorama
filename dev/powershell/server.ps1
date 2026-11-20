# OZPanorama · Zuka Force — servidor local (só PowerShell do Windows, sem instalar nada)
# Abre a interface no browser em http://localhost:47777/ e grava as configurações dos jogos
# com: jogo fechado, backup, escrita só das chaves necessárias, releitura e hash.

$ErrorActionPreference = 'Stop'
$Port    = 47777
$AppDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$Docs    = [Environment]::GetFolderPath('MyDocuments')
$DataDir = Join-Path $Docs 'OZPanorama'
$BackDir = Join-Path $DataDir 'backups'
$LogFile = Join-Path $DataDir 'log.txt'
New-Item -ItemType Directory -Force -Path $BackDir | Out-Null
$Utf8 = New-Object System.Text.UTF8Encoding($false)

# C1: token de sessão. Só a página servida por este servidor o conhece (é injetado no index.html).
# Pedidos /api/* sem o cabeçalho X-OZP-Token correto são recusados. Um cabeçalho próprio obriga o browser a um
# preflight CORS que este servidor nunca autoriza, por isso uma página externa não consegue enviá-lo.
$Token = [guid]::NewGuid().ToString('N')
$TokenFile = Join-Path $DataDir 'servidor.token'

function Log($m) { Add-Content -Path $LogFile -Value ("{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $m) }

# Píxeis reais (sem escala do Windows)
Add-Type -TypeDefinition @"
using System; using System.Runtime.InteropServices;
public static class OzDpi { [DllImport("user32.dll")] public static extern bool SetProcessDPIAware(); }
"@
[void][OzDpi]::SetProcessDPIAware()
Add-Type -AssemblyName System.Windows.Forms
. (Join-Path $AppDir 'surround.ps1')   # funções Sur* (perfis do NVIDIA Surround)

$Games = @{
  ets2    = @{ proc = @('eurotrucks2');              file = (Join-Path $Docs 'Euro Truck Simulator 2\multimon_config.sii'); cfg = (Join-Path $Docs 'Euro Truck Simulator 2\config.cfg') }
  ats     = @{ proc = @('amtrucks');                 file = (Join-Path $Docs 'American Truck Simulator\multimon_config.sii'); cfg = (Join-Path $Docs 'American Truck Simulator\config.cfg') }
  iracing = @{ proc = @('iRacingSim64DX11','iRacingSim64'); file = (Join-Path $Docs 'iRacing\rendererDX11Monitor.ini') }
  msfs    = @{ proc = @('FlightSimulator2024');      file = $null }
}
$msfsCandidates = @(
  (Join-Path $env:APPDATA 'Microsoft Flight Simulator 2024\UserCfg.opt'),
  (Join-Path $env:LOCALAPPDATA 'Packages\Microsoft.Limitless_8wekyb3d8bbwe\LocalCache\UserCfg.opt'))
foreach ($c in $msfsCandidates) { if (Test-Path $c) { $Games.msfs.file = $c; break } }

function Sha($path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLower() }
function Running($g) { @($Games[$g].proc | ForEach-Object { Get-Process -Name $_ -ErrorAction SilentlyContinue }).Count -gt 0 }

function Backup($game, [string[]]$paths) {
  $dir = Join-Path $BackDir ("{0}\{1:yyyyMMdd_HHmmss}" -f $game, (Get-Date))
  New-Item -ItemType Directory -Force -Path $dir | Out-Null
  $map = @()
  foreach ($p in $paths) {
    if (Test-Path -LiteralPath $p) {
      $dst = Join-Path $dir (Split-Path -Leaf $p)
      Copy-Item -LiteralPath $p -Destination $dst -Force
      if ((Sha $p) -ne (Sha $dst)) { throw "O backup de $p não confere; nada foi escrito." }
      $map += @{ original = $p; copy = $dst }
    } else { $map += @{ original = $p; copy = $null } }
  }
  ($map | ConvertTo-Json -Depth 3) | Set-Content -LiteralPath (Join-Path $dir 'manifesto.json') -Encoding UTF8
  return $dir
}

function WriteText($path, $text) {
  $tmp = "$path.ozp_tmp"
  [IO.File]::WriteAllText($tmp, $text, $Utf8)
  Move-Item -LiteralPath $tmp -Destination $path -Force
}

# Substitui o valor de uma chave que tem de existir exatamente uma vez.
function PatchKey([string]$text, [string]$pattern, [string]$replacement, [string]$label) {
  $rx = New-Object System.Text.RegularExpressions.Regex($pattern, [System.Text.RegularExpressions.RegexOptions]::Multiline)
  $n = $rx.Matches($text).Count
  if ($n -ne 1) { throw "$label encontrada $n vezes (esperado 1); nada foi escrito." }
  return $rx.Replace($text, $replacement, 1)
}

# Define uma variável "uset nome "valor"" do config.cfg: substitui se existir uma vez, acrescenta se não existir.
function SetCvar([string]$text, [string]$name, [string]$value) {
  $rx = New-Object System.Text.RegularExpressions.Regex(('^(\s*uset\s+' + [regex]::Escape($name) + '\s+)"[^"]*"'), [System.Text.RegularExpressions.RegexOptions]::Multiline)
  $n = $rx.Matches($text).Count
  if ($n -gt 1) { throw "$name aparece $n vezes no config.cfg; nada foi escrito." }
  if ($n -eq 1) { return $rx.Replace($text, ('${1}"' + $value + '"'), 1) }
  $nl = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
  $sep = if ($text.EndsWith("`n") -or -not $text) { '' } else { $nl }
  return $text + $sep + "uset $name `"$value`"" + $nl
}
function GetCvar([string]$text, [string]$name) {
  $m = [regex]::Match($text, ('(?m)^\s*uset\s+' + [regex]::Escape($name) + '\s+"([^"]*)"'))
  if ($m.Success) { return $m.Groups[1].Value } else { return $null }
}

# C3: além do modo multimonitor, o jogo tem de desenhar com o tamanho exato da área calculada
# (r_mode_width/r_mode_height). Visto no rig a 15/11: Surround a 5838 px e o ETS2 a 5760 → vistas desalinhadas.
function ApplyScs($game, $sii, $mode) {
  $g = $Games[$game]
  if (-not (Test-Path (Split-Path -Parent $g.file))) { throw "Pasta do jogo não encontrada: $(Split-Path -Parent $g.file)" }
  if (-not (Test-Path $g.cfg)) { throw "config.cfg não encontrado. Abra o jogo uma vez antes." }
  $w = [int]$mode.w; $h = [int]$mode.h
  if ($w -lt 640 -or $w -gt 32768 -or $h -lt 480 -or $h -gt 16384) { throw "Resolução calculada inválida ($w×$h); nada foi escrito." }
  # O jogo em ecrã inteiro só consegue esse tamanho se existir um ecrã do Windows com ele (o Surround ligado).
  # Lista lida ao Windows agora (não a cache de System.Windows.Forms.Screen, que neste processo ficava desatualizada).
  $span = @(SurScreens | Where-Object { $_.w -eq $w -and $_.h -eq $h })
  if (-not $span.Count) {
    $have = (@(SurScreens | ForEach-Object { "$($_.w)×$($_.h)" })) -join ', '
    throw "O Windows não tem nenhum ecrã com $w×$h (tem: $have). Ligue o Surround com as molduras antes de aplicar; nada foi escrito."
  }
  $bk = Backup $game @($g.file, $g.cfg)
  $cfg = [IO.File]::ReadAllText($g.cfg)
  $changes = @()
  $cfg2 = $cfg
  foreach ($kv in @(@('r_multimon_mode', '4'), @('r_mode_width', "$w"), @('r_mode_height', "$h"))) {
    $old = GetCvar $cfg2 $kv[0]
    if ($old -ne $kv[1]) { $changes += "$($kv[0]) $old → $($kv[1])" }
    $cfg2 = SetCvar $cfg2 $kv[0] $kv[1]
  }
  WriteText $g.file ($sii.Trim() + "`r`n")
  if ($cfg2 -ne $cfg) { WriteText $g.cfg $cfg2 }
  $back = [IO.File]::ReadAllText($g.cfg)
  $ok = ([IO.File]::ReadAllText($g.file).Trim() -eq $sii.Trim()) -and ((GetCvar $back 'r_multimon_mode') -eq '4') -and ((GetCvar $back 'r_mode_width') -eq "$w") -and ((GetCvar $back 'r_mode_height') -eq "$h")
  return @{ ok = $ok; backup = $bk; changes = $changes; files = @(@{ path = $g.file; sha256 = (Sha $g.file) }, @{ path = $g.cfg; sha256 = (Sha $g.cfg) }) }
}

function ApplyIracing($lines, $dist) {
  $f = $Games.iracing.file
  if (-not (Test-Path $f)) { throw "rendererDX11Monitor.ini não encontrado. Abra o iRacing uma vez antes." }
  $bk = Backup 'iracing' @($f)
  $t = [IO.File]::ReadAllText($f)
  $kv = [ordered]@{}
  foreach ($l in ($lines -split "`n")) { if ($l -match '^\s*([A-Za-z0-9]+)=(.+?)\s*$') { $kv[$Matches[1]] = $Matches[2] } }
  if ($dist) { $kv['ViewingDist'] = "$dist" }
  foreach ($k in $kv.Keys) {
    $v = $kv[$k]
    $t = PatchKey $t "^($k=)[^\s;]*" ('${1}' + $v) $k
  }
  WriteText $f $t
  $back = [IO.File]::ReadAllText($f)
  $ok = $true
  foreach ($k in $kv.Keys) { if ($back -notmatch "(?m)^$k=$([regex]::Escape($kv[$k]))(\s|$)") { $ok = $false } }
  return @{ ok = $ok; backup = $bk; files = @(@{ path = $f; sha256 = (Sha $f) }) }
}

function ApplyMsfs($lines) {
  $f = $Games.msfs.file
  if (-not $f) { throw "UserCfg.opt do MSFS 2024 não encontrado." }
  $t = [IO.File]::ReadAllText($f)
  $kv = [ordered]@{}
  foreach ($l in ($lines -split "`n")) { if ($l -match '^\s*([A-Za-z0-9_.]+)\s+(.+?)\s*$') { $kv[$Matches[1]] = $Matches[2] } }
  foreach ($k in $kv.Keys) {
    if (([regex]::Matches($t, "(?m)^[ \t]*$([regex]::Escape($k)) ")).Count -ne 1) {
      if ($k -like 'WindowCustom*') { throw "O MSFS ainda não tem as janelas extra. Abra o jogo uma vez, crie 2 janelas em Opções Avançadas > Multijanelas e saia pelo menu." }
      throw "$k não encontrada no UserCfg.opt; nada foi escrito."
    }
  }
  $bk = Backup 'msfs' @($f)
  foreach ($k in $kv.Keys) { $t = PatchKey $t "^([ \t]*$([regex]::Escape($k)) ).*?(\r?)$" ('${1}' + $kv[$k] + '${2}') $k }
  WriteText $f $t
  $back = [IO.File]::ReadAllText($f)
  $ok = $true
  foreach ($k in $kv.Keys) { if ($back -notmatch "(?m)^[ \t]*$([regex]::Escape($k)) $([regex]::Escape($kv[$k]))\r?$") { $ok = $false } }
  return @{ ok = $ok; backup = $bk; files = @(@{ path = $f; sha256 = (Sha $f) }) }
}

function Undo($game) {
  $dirs = Get-ChildItem -Directory -LiteralPath (Join-Path $BackDir $game) -ErrorAction SilentlyContinue | Sort-Object Name -Descending
  if (-not $dirs) { throw "Não há backups do OZPanorama para este jogo." }
  if (Running $game) { throw "Feche o jogo antes de desfazer." }
  $man = Get-Content -Raw -LiteralPath (Join-Path $dirs[0].FullName 'manifesto.json') | ConvertFrom-Json
  foreach ($e in @($man)) { if ($e.copy) { Copy-Item -LiteralPath $e.copy -Destination $e.original -Force } }
  Rename-Item -LiteralPath $dirs[0].FullName -NewName ($dirs[0].Name + '_reposto')
  return @{ ok = $true; restored = $dirs[0].Name }
}

function Monitors {
  $screens = @(SurScreens)   # lida ao Windows em cada pedido
  $mons = @()
  try {
    $ids = @(Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorID -ErrorAction Stop)
    $basic = @(Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorBasicDisplayParams -ErrorAction SilentlyContinue)
    $desc = @(Get-CimInstance -Namespace root\wmi -ClassName WmiMonitorDescriptorMethods -ErrorAction SilentlyContinue)
    foreach ($m in $ids) {
      $name = (($m.UserFriendlyName | Where-Object { $_ -ne 0 } | ForEach-Object { [char]$_ }) -join '').Trim()
      $o = @{ name = $name; instance = $m.InstanceName; w_mm = $null; h_mm = $null; source = $null }
      # 1) EDID no registo (não precisa de administrador)
      try {
        $inst = $m.InstanceName -replace '_\d+$', ''
        $e = (Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Enum\$inst\Device Parameters" -Name EDID -ErrorAction Stop).EDID
        if ($e -and $e.Count -ge 72) { $o.w_mm = $e[66] + (($e[68] -band 0xF0) -shl 4); $o.h_mm = $e[67] + (($e[68] -band 0x0F) -shl 8); $o.source = 'EDID (mm)' }
      } catch { }
      $d = $desc | Where-Object { $_.InstanceName -eq $m.InstanceName } | Select-Object -First 1
      if (-not $o.w_mm -and $d) {
        try {
          $r = Invoke-CimMethod -InputObject $d -MethodName WmiGetMonitorRawEEdidV1Block -Arguments @{ BlockId = [byte]0 }
          $e = $r.BlockContent
          if ($e -and $e.Count -ge 72) { $o.w_mm = $e[66] + (($e[68] -band 0xF0) -shl 4); $o.h_mm = $e[67] + (($e[68] -band 0x0F) -shl 8); $o.source = 'EDID (mm)' }
        } catch { }
      }
      if (-not $o.w_mm) {
        $b = $basic | Where-Object { $_.InstanceName -eq $m.InstanceName } | Select-Object -First 1
        if ($b) { $o.w_mm = 10 * $b.MaxHorizontalImageSize; $o.h_mm = 10 * $b.MaxVerticalImageSize; $o.source = 'EDID (cm, aproximado)' }
      }
      $mons += $o
    }
  } catch { Log "WMI: $_" }
  # Com Surround, o Windows esconde os monitores físicos: lê todos os EDID guardados no registo.
  $seen = @{}
  foreach ($m in $mons) { if ($m.name) { $seen[$m.name] = $true } }
  try {
    Get-ChildItem -Path 'HKLM:\SYSTEM\CurrentControlSet\Enum\DISPLAY' -ErrorAction Stop | ForEach-Object {
      Get-ChildItem -LiteralPath $_.PSPath -ErrorAction SilentlyContinue | ForEach-Object {
        $e = (Get-ItemProperty -LiteralPath (Join-Path $_.PSPath 'Device Parameters') -Name EDID -ErrorAction SilentlyContinue).EDID
        if ($e -and $e.Count -ge 128) {
          $name = $null
          foreach ($off in 54, 72, 90, 108) {
            if ($e[$off] -eq 0 -and $e[$off + 1] -eq 0 -and $e[$off + 3] -eq 0xFC) {
              $name = (($e[($off + 5)..($off + 17)] | Where-Object { $_ -ne 10 -and $_ -ne 0 } | ForEach-Object { [char]$_ }) -join '').Trim()
            }
          }
          $w = $e[66] + (($e[68] -band 0xF0) -shl 4); $h = $e[67] + (($e[68] -band 0x0F) -shl 8)
          $key = "$name|$w|$h"
          if ($name -and $w -gt 100 -and $w -lt 2000 -and -not $seen[$name] -and -not $seen[$key]) {
            $seen[$key] = $true
            $mons += @{ name = $name; instance = $_.PSChildName; w_mm = $w; h_mm = $h; source = 'EDID (mm), registo' }
          }
        }
      }
    }
  } catch { Log "Registo EDID: $_" }
  $mons = @($mons | Where-Object { -not ($_.name -match 'Surround') -and -not ($_.w_mm -gt 1500) })
  return @{ screens = $screens; monitors = $mons }
}

function Info {
  $out = @{}
  foreach ($k in $Games.Keys) {
    $f = $Games[$k].file
    $out[$k] = @{ file = $f; exists = [bool]($f -and (Test-Path -LiteralPath $f)); running = (Running $k);
      backups = @(Get-ChildItem -Directory -LiteralPath (Join-Path $BackDir $k) -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike '*_reposto' }).Count }
  }
  return @{ app = 'OZPanorama'; version = '0.3'; games = $out; data = $DataDir }
}

function Send($ctx, $code, $body, $type) {
  $bytes = if ($body -is [byte[]]) { $body } else { $Utf8.GetBytes([string]$body) }
  $ctx.Response.StatusCode = $code
  $ctx.Response.ContentType = $type
  $ctx.Response.Headers['Cache-Control'] = 'no-store'
  $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
  $ctx.Response.Close()
}
function SendJson($ctx, $obj, $code = 200) { Send $ctx $code ($obj | ConvertTo-Json -Depth 6 -Compress) 'application/json; charset=utf-8' }

$types = @{ '.html' = 'text/html; charset=utf-8'; '.js' = 'text/javascript; charset=utf-8'; '.png' = 'image/png'; '.ico' = 'image/x-icon'; '.css' = 'text/css; charset=utf-8'; '.json' = 'application/json' }

$listener = New-Object System.Net.HttpListener
$listener.Prefixes.Add("http://localhost:$Port/")
try { $listener.Start() } catch {
  # Já há um OZPanorama aberto: pede-lhe para fechar e ocupa o lugar.
  # Versão nova: POST com o token que ela deixou em Documentos\OZPanorama. Versão antiga (0.2): GET.
  try {
    $old = (Get-Content -Raw -LiteralPath $TokenFile -ErrorAction Stop).Trim()
    Invoke-WebRequest -UseBasicParsing -TimeoutSec 3 -Method Post -ContentType 'application/json' -Body '{}' -Headers @{ 'X-OZP-Token' = $old } "http://localhost:$Port/api/quit" | Out-Null
  } catch { }
  try { Invoke-WebRequest -UseBasicParsing -TimeoutSec 3 "http://localhost:$Port/api/quit" | Out-Null } catch { }
  Start-Sleep -Seconds 2
  $listener = New-Object System.Net.HttpListener
  $listener.Prefixes.Add("http://localhost:$Port/")
  try { $listener.Start() } catch { Log "Porta $Port ocupada: $_"; Start-Process "http://localhost:$Port/"; exit }
}
Set-Content -LiteralPath $TokenFile -Value $Token -Encoding ASCII
Log "Iniciado em http://localhost:$Port/ (app: $AppDir)"
Start-Process "http://localhost:$Port/"

$running = $true
while ($running -and $listener.IsListening) {
  $ctx = $listener.GetContext()
  $req = $ctx.Request
  $path = $req.Url.AbsolutePath
  try {
    if ($path.StartsWith('/api/')) {
      # C1: token obrigatório; pedidos que mudam alguma coisa só por POST com JSON.
      if ($req.Headers['X-OZP-Token'] -ne $Token) { SendJson $ctx @{ ok = $false; error = 'Pedido recusado: não veio da janela do OZPanorama.' } 403; continue }
      $readOnly = @('/api/info', '/api/monitors', '/api/surround')
      if ($readOnly -notcontains $path) {
        if ($req.HttpMethod -ne 'POST' -or -not ([string]$req.ContentType).StartsWith('application/json')) { SendJson $ctx @{ ok = $false; error = 'Método não permitido.' } 405; continue }
      }
    }
    if ($path -eq '/api/info') { SendJson $ctx (Info) }
    elseif ($path -eq '/api/monitors') { SendJson $ctx (Monitors) }
    elseif ($path -eq '/api/apply' -and $req.HttpMethod -eq 'POST') {
      $sr = New-Object IO.StreamReader($req.InputStream, $Utf8); $body = $sr.ReadToEnd() | ConvertFrom-Json
      $game = [string]$body.game
      if (-not $Games.ContainsKey($game)) { throw "Este jogo ainda não tem aplicação automática. Copie a configuração à mão." }
      if (Running $game) { throw "O jogo está aberto. Feche-o primeiro: ele regravaria os ficheiros ao sair." }
      $res = switch ($game) {
        'ets2'    { ApplyScs 'ets2' $body.text $body.mode }
        'ats'     { ApplyScs 'ats' $body.text $body.mode }
        'iracing' { ApplyIracing $body.text $body.viewingDist }
        'msfs'    { ApplyMsfs $body.text }
      }
      Log "Aplicado $game ok=$($res.ok) backup=$($res.backup)"
      SendJson $ctx $res
    }
    elseif ($path -eq '/api/undo' -and $req.HttpMethod -eq 'POST') {
      $sr = New-Object IO.StreamReader($req.InputStream, $Utf8); $body = $sr.ReadToEnd() | ConvertFrom-Json
      $r = Undo ([string]$body.game); Log "Desfeito $($body.game) $($r.restored)"; SendJson $ctx $r
    }
    elseif ($path -eq '/api/surround') { SendJson $ctx (SurStatus) }
    elseif ($path -eq '/api/surround/save') {
      $sr = New-Object IO.StreamReader($req.InputStream, $Utf8); $body = $sr.ReadToEnd() | ConvertFrom-Json
      $r = SurSave ([string]$body.profile); Log "Surround guardado $($body.profile): $($r.text)"; SendJson $ctx $r
    }
    elseif ($path -eq '/api/surround/apply') {
      $sr = New-Object IO.StreamReader($req.InputStream, $Utf8); $body = $sr.ReadToEnd() | ConvertFrom-Json
      $p = [string]$body.profile
      if ($SurProfiles -notcontains $p) { throw "Perfil desconhecido: $p" }
      if (-not (SurLoad $p)) { throw "Ainda não há perfil '$p'. Guarde-o primeiro." }
      # Processo à parte: a mudança de ecrãs e a janela de confirmação (15 s) não bloqueiam o servidor.
      $rid = [guid]::NewGuid().ToString('N')
      Start-Process powershell.exe -WindowStyle Hidden -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$(Join-Path $AppDir 'surround.ps1')`"", '-Action', 'apply', '-Perfil', $p, '-RequestId', $rid)
      Log "Surround: aplicar $p ($rid)"; SendJson $ctx @{ ok = $true; started = $true; id = $rid }
    }
    elseif ($path -eq '/api/surround/keep') {
      $sr = New-Object IO.StreamReader($req.InputStream, $Utf8); $body = $sr.ReadToEnd() | ConvertFrom-Json
      $rid = [string]$body.id
      if ($rid -notmatch '^[0-9a-f]{32}$') { throw 'Pedido de confirmação inválido.' }
      Set-Content -LiteralPath (Join-Path $SurDir "manter_$rid.flag") -Value 'manter' -Encoding ASCII
      Log "Surround: manter ($rid)"; SendJson $ctx @{ ok = $true }
    }
    elseif ($path -eq '/api/quit') { SendJson $ctx @{ ok = $true }; $running = $false }
    else {
      if ($path -eq '/') { $path = '/index.html' }
      $local = [IO.Path]::GetFullPath((Join-Path $AppDir ($path.TrimStart('/') -replace '/', '\')))
      $root = $AppDir.TrimEnd('\') + '\'   # com separador: "app2\" não passa por "app\"
      if ($local.StartsWith($root, [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetExtension($local) -ne '.ps1' -and (Test-Path -LiteralPath $local -PathType Leaf)) {
        $ext = [IO.Path]::GetExtension($local).ToLower()
        $t = if ($types.ContainsKey($ext)) { $types[$ext] } else { 'application/octet-stream' }
        if ($ext -eq '.html') { Send $ctx 200 ([IO.File]::ReadAllText($local, $Utf8).Replace('__OZP_TOKEN__', $Token)) $t }
        else { Send $ctx 200 ([IO.File]::ReadAllBytes($local)) $t }
      } else { Send $ctx 404 'Não encontrado' 'text/plain; charset=utf-8' }
    }
  } catch {
    Log "Erro em ${path}: $($_.Exception.Message)"
    try { SendJson $ctx @{ ok = $false; error = $_.Exception.Message } 400 } catch { }
  }
}
$listener.Stop()
Remove-Item -LiteralPath $TokenFile -ErrorAction SilentlyContinue
Log 'Terminado'
