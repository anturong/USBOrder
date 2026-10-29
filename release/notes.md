## USBOrder 0.1.4

Windows 无人值守机器用的 U 盘盘符管理工具：按设备指纹（VID/PID + 序列号）把固定盘符分配给指定 U 盘，插上就是你要的盘符。

### 0.1.4 变更

- **新增带界面的安装程序** `USBOrder-0.1.4-setup.exe`：欢迎 → 许可 → 选择桌面快捷方式 → 进度 → 完成页可勾选“启动”。不再是双击一闪而过。
- **MSI 保留**（适合静默/批量部署），但 MSI 没有交互界面，双击只会闪一下进度条后完成。
- 修复：MSI 加 `Codepage=65001`，中文名称不再依赖系统代码页。

### 界面

**安装程序**：欢迎、许可、进度、完成页（可勾选启动）。

**编辑器**：

![概览](https://cdn.jsdelivr.net/gh/anturong/USBOrder@main/docs/screenshots/overview.png)

![安装向导](https://cdn.jsdelivr.net/gh/anturong/USBOrder@main/docs/screenshots/wizard.png)

### 包含

- **UsbDriveService.exe** —— Windows 服务：监听插拔、按规则分配盘符；竞争先到先得，真冲突拒绝并弹窗
- **UsbConfigEditor.exe** —— WebView2 + React 配置器：新建服务账号向导、规则编辑、手动改盘符
- 两者均为 win-x64 自包含，目标机不需要安装 .NET 8

### 安装方式

| 方式 | 文件 | 说明 |
|---|---|---|
| 图形安装（推荐） | `USBOrder-0.1.4-setup.exe` | 有向导、桌面/开始菜单快捷方式、卸载入口 |
| MSI（静默） | `USBOrder-0.1.4-win-x64.msi` | 适合批量部署，无交互界面 |
| 绿色版 | `USBOrder-0.1.4-portable-win-x64.zip` | 解压即用，不写注册表 |

安装后在编辑器里点「安装服务」，按向导创建服务账号（`UsbDriveMgrSvc` + 密码）。

### 校验

| 文件 | SHA256 |
|---|---|
| `USBOrder-0.1.4-setup.exe` | `2BCC7FCD7756304F1941C19D6A79361F6F54EF39CA7F3BF074970CD281875073` |
| `USBOrder-0.1.4-win-x64.msi` | `8C018508D70E3DFCCD99D36DB4C02468854D3E71A72E8E79A340CC5BB9D22C65` |
| `USBOrder-0.1.4-portable-win-x64.zip` | `04201E1A94E8958569D3C7D953DC0903A734FEC4E90611DEA80FCDCCFDAC355C` |

### 后续计划

- **多分区识别**：按分区分配盘符，一个盘多个分区各自固定盘符（当前只支持单分区设备）
- **不可读分区屏蔽**：未格式化 / RAW / 无法读取的分区不参与分配，直接跳过并记日志
- 手动指定盘符时的强制覆盖（当前遇到竞争只能等）
- 测试匹配预演（不插盘先看会分到哪个盘符）
