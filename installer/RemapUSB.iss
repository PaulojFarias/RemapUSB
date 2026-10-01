; Instalador do RemapUSB (Inno Setup 6).
;
; 1. Visual Studio: botão direito em RemapUSB.App > Publicar > perfil win-x64 > Publicar.
;    O app sai em installer\publish.
; 2. Inno Setup: abrir este arquivo > Build > Compile.
;    O instalador sai em installer\Output\RemapUSB-Setup-<versão>.exe.
;
; A versão vem do RemapUSB.exe publicado (<Version> do RemapUSB.App.csproj).

#define AppName "RemapUSB"
#define AppExe "RemapUSB.exe"
#define PublishDir AddBackslash(SourcePath) + "publish"
#define AppVersion GetVersionNumbersString(AddBackslash(PublishDir) + AppExe)

; Códigos de saída do "RemapUSB.exe --desfazer-teclas" (ver NeutralCleanup.cs).
#define UndoNothing 0
#define UndoFailed 1
#define UndoRestart 10

[Setup]
; Identifica o app para o Windows: nunca mudar, senão a versão nova não substitui a antiga.
AppId={{6F3B2A8C-41D7-4E59-9C0A-7B2E5D1F8A43}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=PaulojFarias
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Instala só para o usuário, sem pedir administrador. O administrador só é pedido na
; desinstalação, e só se o app tiver neutralizado alguma tecla.
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
; Fecha o app aberto antes de atualizar os arquivos.
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
; Mesmo valor que a opção "Iniciar com o Windows" do app usa.
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
  { usUninstall: antes de apagar os arquivos, enquanto o RemapUSB.exe ainda existe. }
  if CurUninstallStep <> usUninstall then
    exit;

  { Fecha o app, se estiver aberto. Sem ele rodando, nenhum arquivo fica preso. }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/im {#AppExe} /f', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  { Tira o "iniciar com o Windows", inclusive se foi ligado pela tela do app (não pelo instalador). }
  RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'RemapUSB');

  { Desfaz as teclas neutralizadas. Pede administrador só se houver alguma. }
  if not Exec(ExpandConstant('{app}\{#AppExe}'), '--desfazer-teclas', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    ResultCode := {#UndoFailed};

  if ResultCode = {#UndoRestart} then
    RestartNeeded := True
  else if ResultCode <> {#UndoNothing} then
    MsgBox('As teclas neutralizadas pelo RemapUSB continuam trocadas no Windows, porque a permissão de administrador não foi concedida.' + #13#10#13#10 +
           'Para desfazer depois: instale o RemapUSB de novo, use Configurações > Desfazer tudo e reinicie o computador.',
           mbInformation, MB_OK);
end;

{ Pergunta se quer reiniciar no fim: a tecla só volta ao normal depois do reinício. }
function UninstallNeedRestart(): Boolean;
begin
  Result := RestartNeeded;
end;
