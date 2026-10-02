; RemapUSB installer (Inno Setup 6).
;
; Easiest way: double-click installer\gerar-instalador.cmd (publishes and compiles).
; By hand:
; 1. dotnet publish src/RemapUSB.App -p:PublishProfile=win-x64
;    The app goes to installer\publish.
; 2. Inno Setup: open this file > Build > Compile.
;    The installer goes to installer\Output\RemapUSB-Setup-<version>.exe.
;
; The version is read from the published RemapUSB.exe (<Version> in RemapUSB.App.csproj).
; The installer and its messages are in Brazilian Portuguese, like the app.

#define AppName "RemapUSB"
#define AppExe "RemapUSB.exe"
; Where to look for the published app: the win-x64 profile folder and, if missing, the default
; folder Visual Studio uses when publishing with a profile created by its wizard.
#define PerfilDir AddBackslash(SourcePath) + "publish"
#define PadraoVsDir AddBackslash(SourcePath) + "..\src\RemapUSB.App\bin\Release\net10.0-windows\win-x64\publish"

#if FileExists(AddBackslash(PerfilDir) + AppExe)
  #define PublishDir PerfilDir
#elif FileExists(AddBackslash(PadraoVsDir) + AppExe)
  #define PublishDir PadraoVsDir
#else
  ; Without the published .exe the version comes out empty and Inno only complains about
  ; AppVersion; better to say what is missing.
  #error Não achei o RemapUSB.exe publicado. No Visual Studio: botão direito em RemapUSB.App > Publicar, escolha o perfil win-x64 e clique em Publicar.
#endif

; A publish that depends on an installed .NET has RemapUSB.dll next to the .exe. That installer
; would not run on a machine without .NET 10, so stop here.
#if FileExists(AddBackslash(PublishDir) + "RemapUSB.dll")
  #error A publicação encontrada depende do .NET instalado (tem RemapUSB.dll ao lado do .exe). Publique com o perfil win-x64, que é autossuficiente e em arquivo único.
#endif

#define AppVersion GetVersionNumbersString(AddBackslash(PublishDir) + AppExe)
#if AppVersion == ""
  #error O RemapUSB.exe publicado não tem versão. Publique de novo com o perfil win-x64.
#endif

; Exit codes of "RemapUSB.exe --desfazer-teclas" (see NeutralCleanup.cs).
#define UndoNothing 0
#define UndoFailed 1
#define UndoRestart 10

[Setup]
; Identifies the app to Windows: never change it, or a new version will not replace the old one.
AppId={{6F3B2A8C-41D7-4E59-9C0A-7B2E5D1F8A43}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=PaulojFarias
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Installs for the current user only, without administrator rights. Administrator rights are only
; requested on uninstall, and only if the app neutralized some key.
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=RemapUSB-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
; Closes the running app before updating its files.
CloseApplications=yes

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "startup"; Description: "Iniciar o RemapUSB com o Windows"
Name: "desktopicon"; Description: "Criar atalho na área de trabalho"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Same value the app's "Iniciar com o Windows" (start with Windows) option uses.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "RemapUSB"; ValueData: """{app}\{#AppExe}"" --tray"; Tasks: startup

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir o RemapUSB"; Flags: nowait postinstall skipifsilent

[Code]
var
  RestartNeeded: Boolean;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  { usUninstall: before the files are deleted, while RemapUSB.exe still exists. }
  if CurUninstallStep <> usUninstall then
    exit;

  { Closes the app if it is running, so no file stays locked. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/im {#AppExe} /f', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  { Removes "start with Windows", even if it was turned on in the app (not by the installer). }
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'RemapUSB');

  { Undoes neutralized keys. Asks for administrator rights only if there is any. }
  if not Exec(ExpandConstant('{app}\{#AppExe}'), '--desfazer-teclas', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := {#UndoFailed};

  if ResultCode = {#UndoRestart} then
    RestartNeeded := True
  else if ResultCode <> {#UndoNothing} then
    MsgBox('As teclas neutralizadas pelo RemapUSB continuam trocadas no Windows, porque a permissão de administrador não foi concedida.' + #13#10#13#10 +
           'Para desfazer depois: instale o RemapUSB de novo, use Configurações > Desfazer tudo e reinicie o computador.',
           mbInformation, MB_OK);
end;

{ Offers to restart at the end: the key only goes back to normal after a restart. }
function UninstallNeedRestart(): Boolean;
begin
  Result := RestartNeeded;
end;
