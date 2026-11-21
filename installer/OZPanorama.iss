; OZPanorama - Inno Setup 6 (free). Build: ISCC /DAppVersion=x.y.z installer\OZPanorama.iss  (after build.cmd)
; Installs for the current user only, without administrator rights. Uninstalling never deletes the backups
; in Documents\OZPanorama\backups (they are the user's original game files).

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef NumVersion
  #define NumVersion "0.0.0.0"
#endif
#define AppName "OZPanorama"
#define Repo "https://github.com/OptimusZuka/OZPanorama"

[Setup]
AppId={{3F7A2C91-5B4D-4E8A-9C1F-0A2B6D7E0001}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=OptimusZuka (Zuka Force)
AppPublisherURL={#Repo}
AppSupportURL={#Repo}/issues
AppUpdatesURL={#Repo}/releases
VersionInfoVersion={#NumVersion}
VersionInfoProductName={#AppName}
VersionInfoDescription=OZPanorama Installer
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
SetupIconFile=..\app\icone.ico
UninstallDisplayIcon={app}\OZPanorama.exe
UninstallDisplayName={#AppName}
OutputDir=..\dist
OutputBaseFilename=OZPanorama_Setup_{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
; the app uses this mutex: the installer asks to close it before updating
AppMutex=OZPanorama.ZukaForce.Beta
SetupLogging=yes
ShowLanguageDialog=yes
LanguageDetectionMethod=uilanguage

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "pt"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
en.Welcome2=Triple monitors done right in MSFS 2024, ETS2, ATS, iRacing and RaceRoom, from the real measurements of your rig.%n%nNothing in your games is changed by this installer. OZPanorama only writes a game's settings when you press Apply, always with a backup.
pt.Welcome2=Triplos monitores certos no MSFS 2024, ETS2, ATS, iRacing e RaceRoom, a partir das medidas reais do seu rig.%n%nEste instalador não muda nada nos seus jogos. O OZPanorama só escreve as configurações de um jogo quando você aperta Aplicar, sempre com backup.
es.Welcome2=Triple monitor bien configurado en MSFS 2024, ETS2, ATS, iRacing y RaceRoom, a partir de las medidas reales de tu rig.%n%nEste instalador no cambia nada en tus juegos. OZPanorama solo escribe los ajustes de un juego cuando pulsas Aplicar, siempre con copia de seguridad.
fr.Welcome2=Le triple écran bien réglé dans MSFS 2024, ETS2, ATS, iRacing et RaceRoom, à partir des vraies mesures de votre rig.%n%nCet installateur ne modifie rien dans vos jeux. OZPanorama n'écrit les réglages d'un jeu que lorsque vous appuyez sur Appliquer, toujours avec une sauvegarde.
en.TaskDesktop=Create a desktop shortcut
pt.TaskDesktop=Criar atalho na área de trabalho
es.TaskDesktop=Crear acceso directo en el escritorio
fr.TaskDesktop=Créer un raccourci sur le bureau
en.RunNow=Open OZPanorama now
pt.RunNow=Abrir o OZPanorama agora
es.RunNow=Abrir OZPanorama ahora
fr.RunNow=Ouvrir OZPanorama maintenant
en.Backups=Backups of your game files stay in Documents\OZPanorama\backups (they are not deleted).
pt.Backups=Os backups dos ficheiros dos seus jogos ficam em Documentos\OZPanorama\backups (não são apagados).
es.Backups=Las copias de seguridad de los archivos de tus juegos se quedan en Documentos\OZPanorama\backups (no se borran).
fr.Backups=Les sauvegardes des fichiers de vos jeux restent dans Documents\OZPanorama\backups (elles ne sont pas supprimées).

[Messages]
SetupWindowTitle=OZPanorama Installer

[Tasks]
Name: "desktopicon"; Description: "{cm:TaskDesktop}"

[Files]
Source: "..\build\OZPanorama.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "README.md"; Flags: ignoreversion
Source: "..\docs\LIMITATIONS.md"; DestDir: "{app}"; DestName: "LIMITATIONS.md"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\OZPanorama.exe"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\OZPanorama.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\OZPanorama.exe"; Description: "{cm:RunNow}"; Flags: nowait postinstall skipifsilent

[Code]
procedure InitializeWizard;
begin
  WizardForm.WelcomeLabel2.Caption := CustomMessage('Welcome2');
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  // Nunca apaga Documents\OZPanorama: lá estão os backups dos ficheiros originais dos jogos.
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent then
    MsgBox(CustomMessage('Backups'), mbInformation, MB_OK);
end;
