# USBOrder · USB 盘符管理器

给无人值守 Windows 机器用的 U 盘盘符自动管理工具。

仓库：<https://github.com/anturong/USBOrder>

- **UsbDriveService.exe** —— Windows 服务，监听插拔、按规则分配固定盘符（已完成）
- **UsbConfigEditor.exe** —— WebView2 + React 编辑器，唯一管理入口：服务安装、规则、手动改盘符（已完成）
- **UsbConfig.Core.dll** —— 共享库：配置模型、设备指纹、冲突检测、改盘符（**已完成**）

## 界面预览

![概览](https://raw.githubusercontent.com/anturong/USBOrder/main/docs/screenshots/overview.png)

![设置](https://raw.githubusercontent.com/anturong/USBOrder/main/docs/screenshots/settings.png)

```
UsbDriveManager/
├── UsbDriveManager.sln
├── src/UsbConfig.Core/          共享库（net8.0-windows）
├── src/UsbDriveService/         Windows 服务
├── src/UsbConfigEditor/         WebView2 编辑器（自包含发布，manifest 要求管理员）
├── tests/UsbConfig.Core.Tests/  xUnit 单测
├── tools/CoreSmoke/             只读自检工具（现场排障用）
└── deploy/                      服务安装/卸载脚本（编辑器之外的等价实现）
```

## 当前进度

| 模块 | 状态 |
|---|---|
| Core：设备指纹（VID/PID + 序列号解析、可靠性判定） | ✅ |
| Core：USB 磁盘枚举（含 UASP/SCSI 桥、无盘符分区） | ✅ |
| Core：盘符占用反查（USB / 本机 / 网络 / 光驱） | ✅ |
| Core：冲突决策（空闲 / 规则内竞争 / 真冲突） | ✅ |
| Core：改盘符（卷 GUID 定位 + 跨进程互斥） | ✅ |
| Core：config.json 读写（原子替换）+ 静态校验 | ✅ |
| Service：WMI 监听、分配、竞争拒绝、兜底盘符、弹窗、10 秒巡检 | ✅ |
| Editor A1：服务状态/安装/卸载/启停（自包含、管理员） | ✅ |
| Editor A2：规则编辑、从设备导入、指纹只读、校验、保存并重启（WebView2 + React） | ✅ |
| Editor A3：现场手动改盘符 | ✅（强制覆盖竞争、多分区选择未做） |
| 打包：`deploy/publish.ps1` 一键产出 `dist\` | ✅ |
| 自检：`UsbConfigEditor.exe --self-test` | ✅ |

**当前交付形态**：`dist\UsbConfigEditor.exe`（编辑器，自包含）+ `dist\service\UsbDriveService.exe`（服务，自包含）。


## 从源码复现构建

### 环境要求

| 依赖 | 版本 | 用途 |
|---|---|---|
| Windows | 10 / 11 x64 | 目标平台 |
| .NET SDK | 8.0+ | 编译 Core / 服务 / 编辑器 |
| Node.js + npm | 20+ | 构建 WebView2 前端（`src/UsbConfigEditor.Web`） |
| Inno Setup | 7.x（可选） | 只有生成 `setup.exe` 需要；`ISCC.exe` 在 PATH 或默认安装目录 |

NuGet 直连慢时可给所有脚本加 `-NugetSource https://repo.huaweicloud.com/repository/nuget/v3/index.json`，
前端可用 `npm config set registry https://registry.npmmirror.com`。

### 一条命令出发布目录

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\publish.ps1
```

脚本会：`npm install`（首次）+ `npm run build` → 把前端产物写进
`src/UsbConfigEditor/wwwroot` → 发布编辑器与服务（win-x64 自包含）到 `dist\`。
已有 `node_modules` 或只想重发后端时加 `-SkipFrontend`。

### 出安装包

```powershell
# MSI（静默部署，无交互界面）
powershell -ExecutionPolicy Bypass -File .\deploy\build-msi.ps1 -Version 0.2.5

# setup.exe（Inno Setup 图形安装包，需先装 Inno Setup 7）
powershell -ExecutionPolicy Bypass -File .\deploy\build-inno.ps1 -Version 0.2.5
```

产物都在 `release\`：

```
release\
├── USBOrder-<版本>-setup.exe          图形安装包（推荐）
├── USBOrder-<版本>-win-x64.msi        MSI（静默）
└── USBOrder-<版本>-portable-win-x64.zip  绿色版（用 tar/压缩工具把 dist 打包）
```

版本号只传 `-Version`，WiX 通过 `ProductVersion` 属性注入，不再改源码。

> 仓库里**不含**这些自动生成物：`dist/`、`bin/`、`obj/`、`node_modules/`、
> `src/UsbConfigEditor/wwwroot/`、`installer/Generated.wxs`、`*.msi`、`*.exe`、`*.zip`。
> 它们都能由上面的命令重新生成。

## 构建

```powershell
dotnet build UsbDriveManager.sln -c Release
```

产物：

```
src/UsbConfig.Core/bin/Release/net8.0-windows/UsbConfig.Core.dll
tools/CoreSmoke/bin/Release/net8.0-windows/CoreSmoke.exe
```

## 打包（交付）

```powershell
powershell -ExecutionPolicy Bypass -File .\deploy\publish.ps1
# NuGet 直连慢时：追加 -NugetSource https://repo.huaweicloud.com/repository/nuget/v3/index.json
```

产物 `dist\`：

```
dist\
├── UsbConfigEditor.exe     编辑器（含运行时与 wwwroot）
└── service\
    └── UsbDriveService.exe 服务（编辑器安装时复制到 Program Files）
```

两个程序都是 **win-x64 自包含**，目标机不需要装 .NET 8；编辑器安装服务时从同目录的
`service\` 复制程序文件，所以分发时这两个目录要一起拷。

> 构建时会出现一条 `MSB3277`（WindowsBase 版本冲突）警告：WebView2 包同时带了 WPF 程序集，
> 与 WinForms 引用的版本号不同。实际只用 WinForms 那套，警告可忽略。

## 打包（MSI 安装包）

```powershell
# 先出 dist
powershell -ExecutionPolicy Bypass -File .\deploy\publish.ps1
# 再生成 MSI（需要联网还原 WixToolset.Sdk，可加 -NugetSource 走镜像）
powershell -ExecutionPolicy Bypass -File .\deploy\build-msi.ps1
```

产物：项目根目录 `UsbDriveManager-0.1.0.msi`（约 69 MB，cab 内嵌）。

安装内容：
- 文件解到 `C:\Program Files\UsbDriveManager`（编辑器 + `service\` + `wwwroot\`）；
- **开始菜单**和**桌面**各一个「USB 盘符管理器」快捷方式，指向编辑器（manifest 要求管理员，双击会弹 UAC）；
- 不注册服务——服务仍由编辑器里的「安装服务」按钮注册，这样账号/密码选择留在界面上。

> 如果机器上已经注册过服务，`Program Files\UsbDriveManager\UsbDriveService.exe` 可能正被占用：
> 先停服务再装 MSI，或让安装程序按提示重启后替换。


## 自检（不写任何系统状态）

```powershell
dotnet run --project tools/CoreSmoke -c Release
```

会打印当前插入的 USB 磁盘、盘符占用情况，并跑一遍决策分支与配置读写的自检。
退出码 0 表示全部通过。

### 编辑器桥接层自检

```powershell
# 需要管理员（编辑器 manifest 要求），会弹一次 UAC
& "src\UsbConfigEditor\bin\Release\net8.0-windows\win-x64\UsbConfigEditor.exe" --self-test
```

不开界面，直接跑 `getStatus → listDevices → loadConfig → saveConfig(原样回写, 不重启服务) → 回读`，
打印 PASS/FAIL，退出码 0 表示全过。结果同时写到
`%TEMP%\usbdrive-editor-selftest-yyyyMMdd-HHmmss.log`（重定向/无人值守场景取这个文件）。

## 单元测试

```powershell
dotnet test tests/UsbConfig.Core.Tests -c Release
```

> 注意：如果终端处于受限/沙箱环境，`dotnet test` 的 testhost 可能因拿不到父进程句柄
> 报 `Win32Exception (5): 拒绝访问`。此时用普通终端再跑，或用上面的 CoreSmoke 自检。

## 支持的设备类型

- **U 盘、USB 移动硬盘、USB 硬盘盒、读卡器**：只要以磁盘设备出现就能识别。
- 判据：`Win32_DiskDrive.InterfaceType='USB'` **或** `PNPDeviceID` 以 `USBSTOR\` 开头
  （后者覆盖少数把接口类型报成 `SCSI` 的 USB-SATA 桥接芯片/UASP 驱动）。
- 多分区磁盘：当前按“第一个有盘符的分区”改派；多分区建议在编辑器里手动指定具体分区。
- 未分区/未初始化的磁盘：能识别、但不分配（没有盘符可改），需先分区格式化。

## Core 关键约定

- **盘符只允许 D–Z**：A/B 为软驱保留、C 为系统盘，`DriveLetters.IsAssignable` 统一拦截。
- **序列号可靠性**：空串、全零、`&` 连接的总线相对实例号（如 `6&29B12556`）、
  以及固件垃圾值（如某些读卡器上报的 `[`）都会被判为不可靠并清空，
  退化为 VID/PID 匹配；编辑器应据此提示精度风险。
- **先到先得**：目标盘符被规则内另一台设备占用时，后插者保持系统临时盘符，只记日志 + 弹窗。
- **真冲突即拒**：被无关卷（本地盘/网络盘/光驱）占用时拒绝分配，不做自动 fallback。
- **不做热加载**：改配置必须重启服务。
- **改盘符临界区**：`WmiVolumeAssigner` 内用命名互斥锁（优先 `Global\`）包住“检查 → 赋值”，
  服务与编辑器现场操作共用同一把锁。

## 配置

默认路径 `C:\ProgramData\UsbDriveManager\config.json`
（可用环境变量 `USBDRIVEMANAGER_DATA` 覆盖根目录，便于测试）。

```json
{
  "version": 1,
  "rules": [
    {
      "id": "0f0c1f...",
      "name": "产线 U 盘",
      "vid": "0951",
      "pid": "1666",
      "serial": "4C530001234567890123",
      "targetLetter": "F",
      "enabled": true
    }
  ]
}
```

日志目录：`C:\ProgramData\UsbDriveManager\logs\`

## 部署与使用

1. 双击 `dist\UsbConfigEditor.exe`（会提权），点「安装服务」：
   - **使用已有账号**：填一个管理员账号 + 密码。只验证（`LogonUser`）、检查是否在 Administrators、
     补授“作为服务登录”，**不会修改该账号**；密码没设永不过期只会警告。
   - **新建专用账号**：建号、加组、授服务登录权、禁止交互登录。
   - 两种模式都会把 `service\` 复制到 `C:\Program Files\UsbDriveManager`，注册服务并启动。
2. 插入 U 盘/移动硬盘 → 设备表选中 → 选目标盘符 → 「应用到选中设备」。
   「同时保存为规则」默认勾选：会按指纹 upsert 规则、保存并重启服务。
3. 规则表里指纹（名称/VID/PID/序列号）只读，可改目标盘符与启用；保存前自动校验 + 去重。
4. 卸载：设置页「卸载服务」；配置、日志、账号都保留。

命令行等价方式（不经过界面）见 [docs/service-startup.md](docs/service-startup.md)，
脚本在 `deploy\`。

> 本机执行策略为 Restricted，`.ps1` 要用 `powershell -ExecutionPolicy Bypass -File ...` 运行。

## 本机实测记录（2026-10-28）

- 机器上插着一个 Genesys Logic 读卡器（`VID_05E3&PID_0751`，T: 盘）：
  - 其 `Win32_DiskDrive.SerialNumber` 为 `[`（单字节 0x5B，固件垃圾），
    USBSTOR 实例名尾部为 `6&29B12556&0`（总线相对实例号），两者都不可靠；
  - Core 判定 `序列号可用=False`，指纹退化为 `VID_05E3&PID_0751`（空序列号）。
- WQL 注意点：`ASSOCIATORS OF {Win32_DiskDrive.DeviceID="\\\\.\\PHYSICALDRIVE0"}` 必须用
  **双引号**且反斜杠成对；单引号写法会报 `Not found`。
