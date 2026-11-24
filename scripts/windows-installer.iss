; Inno Setup script for Tammoa's Windows installer.
; Built by scripts/package-windows.ps1, which passes the real app version via
; /DAppVersion=X.Y.Z (read live from Tammoa.csproj, same source of truth as
; the macOS packaging script) — never hardcode a version here.
;
; Unsigned: no code-signing certificate is configured for this project (same
; already-accepted tradeoff as the macOS build's ad-hoc signing). Windows
; SmartScreen will warn on first run until/unless that changes.

#define AppName "Tammoa"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define PublishDir "..\Tammoa\bin\Release\net8.0\win-x64\publish"

[Setup]
; Fixed once, kept stable across versions so upgrades/uninstalls work correctly.
; The doubled "{{" escapes Inno's constant syntax - a lone "{" here would make
; the compiler try (and fail) to resolve the GUID as a named constant.
AppId={{8302069B-0554-43EC-93AD-E8CEC0B4DABB}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..\Deployed\Windows
OutputBaseFilename=Tammoa-{#AppVersion}-win-x64-setup
SetupIconFile=..\Tammoa\AppIcon\app.ico
UninstallDisplayIcon={app}\Tammoa.exe
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
; The whole publish output, recursively — Program.cs resolves both AppIcon/
; and wwwroot/ relative to the executable's own directory, so they need to
; sit next to it, same approach as the macOS .app bundle's loose copies.
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\Tammoa.exe"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\Tammoa.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Tammoa.exe"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
