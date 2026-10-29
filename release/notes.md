## USBOrder 0.1.3

Windows 无人值守机器用的 U 盘盘符管理工具：按设备指纹（VID/PID + 序列号）把固定盘符分配给指定 U 盘，插上就是你要的盘符。

### 0.1.3 变更

- **修复“账号验证失败（错误 1385）”**：服务账号默认禁止交互登录，之前用交互式登录验密码会必然失败；改用网络/服务登录方式校验，已验证通过。
- **安装流程简化**：只保留「新建专用账号」一种方式，去掉“使用已有账号”分支，避免两套流程都出问题。
- 保留 0.1.2 的：两步向导（下一步 → 确认 → 安装并启动 → 显示已登录账号）、设置页「账号与服务」与「退出登录」、服务装到 `service` 子目录、自动刷新。

### 界面

**安装向导**（两步：填账号密码 → 确认安装）：

![安装向导](https://cdn.jsdelivr.net/gh/anturong/USBOrder@main/docs/screenshots/wizard.png)

**概览**：服务状态、规则数、当前插入设备、一键改盘符。

![概览](https://cdn.jsdelivr.net/gh/anturong/USBOrder@main/docs/screenshots/overview.png)

**设置**：路径、账号与服务。

![设置](https://cdn.jsdelivr.net/gh/anturong/USBOrder@main/docs/screenshots/settings.png)

### 包含

- **UsbDriveService.exe** —— Windows 服务：监听插拔、按规则分配盘符；竞争先到先得，真冲突拒绝并弹窗
- **UsbConfigEditor.exe** —— WebView2 + React 配置器：服务安装向导、规则编辑、手动改盘符
- 两者均为 win-x64 自包含，目标机不需要安装 .NET 8

### 安装

- **绿色版**：解压 `USBOrder-0.1.3-portable-win-x64.zip`，运行 `UsbConfigEditor.exe`（需管理员），按向导安装服务
- **MSI**：双击安装到 `C:\Program Files\UsbDriveManager`，生成开始菜单/桌面快捷方式；服务仍由向导安装

### 校验

| 文件 | SHA256 |
|---|---|
| `USBOrder-0.1.3-portable-win-x64.zip` | `04201E1A94E8958569D3C7D953DC0903A734FEC4E90611DEA80FCDCCFDAC355C` |
| `USBOrder-0.1.3-win-x64.msi` | `E448A85856959FC6B324BBA681E7AC84644EAB9C307C7ABEE91DF150D11E3778` |

### 后续计划

- **多分区识别**：按分区分配盘符，一个盘多个分区各自固定盘符（当前只支持单分区设备）
- **不可读分区屏蔽**：未格式化 / RAW / 无法读取的分区不参与分配，直接跳过并记日志
- 手动指定盘符时的强制覆盖（当前遇到竞争只能等）
- 界面忙碌遮罩、测试匹配预演（不插盘先看会分到哪个盘符）
