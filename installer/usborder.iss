; USBOrder 安装脚本（Inno Setup 7）
; 编译：ISCC.exe usborder.iss

#ifndef AppVersion
  #define AppVersion "0.2.6"
#endif
#define AppName "USB 盘符管理器"
#define AppPublisher "USBOrder"
#define AppExe "UsbConfigEditor.exe"

[Setup]
AppId={{B7E1C0F2-3D44-4C0E-9A1B-2F6E8C5D7A31}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/anturong/USBOrder
AppSupportURL=https://github.com/anturong/USBOrder
AppUpdatesURL=https://github.com/anturong/USBOrder/releases
DefaultDirName={autopf}\UsbDriveManager
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
LicenseFile=License.rtf
OutputDir=..\release
OutputBaseFilename=USBOrder-{#AppVersion}-setup
SetupIconFile=..\src\UsbConfigEditor\app.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加任务:"

[Files]
Source: "..\dist\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\卸载 {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "启动 {#AppName}"; Flags: nowait postinstall skipifsilent
