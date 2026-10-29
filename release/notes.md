## USBOrder 0.1.1

Windows 无人值守机器用的 U 盘盘符管理工具：按设备指纹（VID/PID + 序列号）把固定盘符分配给指定 U 盘，插上就是你要的盘符。

### 0.1.1 修复

- **修复 MSI 安装后无法安装服务**：0.1.0 的 MSI 里 `System.ServiceProcess.ServiceController.dll` 打进了引用程序集（33 KB），点「安装服务」会抛 `PlatformNotSupportedException`。0.1.1 已换成 Windows 实现（87 KB），绿色版不受影响。
- **0.1.0 的 MSI 请勿使用**，已标记为 pre-release。

### 界面

**概览**：服务状态、规则数、当前插入设备、一键改盘符。

![概览](https://cdn.jsdelivr.net/gh/anturong/USBOrder@main/docs/screenshots/overview.png)

**设置**：配置/日志/服务路径、卸载服务。

![设置](https://cdn.jsdelivr.net/gh/anturong/USBOrder@main/docs/screenshots/settings.png)

### 包含

- **UsbDriveService.exe** —— Windows 服务：监听插拔、按规则分配盘符；竞争先到先得，真冲突拒绝并弹窗
- **UsbConfigEditor.exe** —— WebView2 + React 配置器：服务安装（已有账号/新建）、规则编辑、手动改盘符
- 两者均为 win-x64 自包含，目标机不需要安装 .NET 8

### 安装

- **绿色版**：解压 `USBOrder-0.1.1-portable-win-x64.zip`，运行 `UsbConfigEditor.exe`（需管理员），点「安装服务」
- **MSI**：双击安装到 `C:\Program Files\UsbDriveManager`，生成开始菜单/桌面快捷方式；服务仍在编辑器里安装

### 校验

| 文件 | SHA256 |
|---|---|
| `USBOrder-0.1.1-portable-win-x64.zip` | `D18A9D4D19D7EA14DB60D635D1A9E34046D20BCAA447C8954538B847DAF0BA21` |
| `USBOrder-0.1.1-win-x64.msi` | `AA6E6271FA814F694961503DE76FDB7C5CC216E0C55F1BCE141A9EAA12BFB4A8` |

### 后续计划

- **多分区识别**：按分区分配盘符，一个盘多个分区各自固定盘符（当前 0.1.1 只支持单分区设备，导入多分区盘会被拦下）
- **不可读分区屏蔽**：未格式化 / RAW / 无法读取的分区不参与分配，直接跳过并记日志，避免误判和盘符浪费
- 手动指定盘符时的强制覆盖（当前遇到竞争只能等）
- 界面忙碌遮罩、测试匹配预演（不插盘先看会分到哪个盘符）
