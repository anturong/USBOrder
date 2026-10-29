## USBOrder 0.1.2

Windows 无人值守机器用的 U 盘盘符管理工具：按设备指纹（VID/PID + 序列号）把固定盘符分配给指定 U 盘，插上就是你要的盘符。

### 0.1.2 变更

- **安装服务改成两步向导**：先选「使用已有账号」或「新建专用账号」，填好账号密码后「下一步」确认，再点「安装并启动」；成功后直接显示已登录的账号，不用再登录一次。
- **设置页新增「账号与服务」**：显示当前运行服务的账号，提供「退出登录」——卸载服务后用另一个账号重新安装。
- **修复“复制文件失败：clrjit.dll 被占用”**：服务改为安装到 `Program Files\UsbDriveManager\service` 子目录，不再和编辑器自己的运行时 dll 互相覆盖。
- **自动刷新**：服务被重启、窗口重新获得焦点、应用盘符或保存规则之后，界面自动刷新盘符占用。

### 界面

**安装向导**（0.1.2 新增）：

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

- **绿色版**：解压 `USBOrder-0.1.2-portable-win-x64.zip`，运行 `UsbConfigEditor.exe`（需管理员），按向导安装服务
- **MSI**：双击安装到 `C:\Program Files\UsbDriveManager`，生成开始菜单/桌面快捷方式；服务仍由向导安装

### 校验

| 文件 | SHA256 |
|---|---|
| `USBOrder-0.1.2-portable-win-x64.zip` | `135244F53C24D292371136A2B0F478A9FD75364EF76AFF6123F2F7AF924B04FF` |
| `USBOrder-0.1.2-win-x64.msi` | `EA472ED81123EBBDB08D7775B416847A9836DE8CE51BCE751F531E15BD3E666A` |

### 后续计划

- **多分区识别**：按分区分配盘符，一个盘多个分区各自固定盘符（当前只支持单分区设备）
- **不可读分区屏蔽**：未格式化 / RAW / 无法读取的分区不参与分配，直接跳过并记日志
- 手动指定盘符时的强制覆盖（当前遇到竞争只能等）
- 界面忙碌遮罩、测试匹配预演（不插盘先看会分到哪个盘符）
