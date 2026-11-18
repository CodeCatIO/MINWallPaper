; MINWallPaper 安装包脚本
; 构建前先执行:
;   dotnet publish ..\MINWallPaper.csproj -c Release --self-contained false -o ..\publish
; 然后用 Inno Setup 6 编译本文件:
;   ISCC.exe MINWallPaper.iss
; 输出在 dist\MINWallPaper-<版本>-setup.exe

#define AppName "MINWallPaper"
#define AppVersion "1.1.2"
#define AppPublisher "CodeCat"
#define AppExe "MINWallPaper.exe"

[Setup]
AppId={{A7F3C1D2-5B4E-4C9A-9F21-7D3E6B8C4A10}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\Programs\MINWallPaper
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\dist
OutputBaseFilename=MINWallPaper-{#AppVersion}-setup
SetupIconFile=..\Assets\Micon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
VersionInfoVersion=1.1.2.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} Setup

; 升级时如果程序还在托盘里运行，提示关闭后再覆盖文件
AppMutex=Local\MINWallPaper.SingleInstance
CloseApplications=force

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; 只清理程序自身生成在安装目录里的缓存；用户数据在 data 目录，卸载时保留
Type: filesandordirs; Name: "{app}\data\previews"
