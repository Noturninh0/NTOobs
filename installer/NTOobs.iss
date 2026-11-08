#define AppName "NTOobs"
#define AppVersion "1.3.0"
#define AppExe "NTOobs.exe"

[Setup]
AppId={{B7E3C1A2-5D4F-4E8A-9C21-6F0A7D3E5B19}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=NTO
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=NTOobs-Setup
SetupIconFile=..\NTOobs\icon.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
; Esta é a pergunta exibida durante a instalação (marcada por padrão).
Name: "desktopicon"; Description: "Deseja criar um atalho na Área de Trabalho?"; GroupDescription: "Atalhos:"

[Files]
Source: "..\dist\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir o NTOobs agora"; Flags: nowait postinstall skipifsilent
