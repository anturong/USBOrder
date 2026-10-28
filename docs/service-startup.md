# 服务启动配置（UsbDriveService）

本文件是 `UsbDriveService.exe` 的服务注册与启动参数定义。编辑器的「安装服务」按钮会实现同一套配置，
`deploy/Install-UsbDriveService.ps1` 是现在的等价参考实现（服务还没写，脚本先就位）。

## 1. 服务标识

| 项 | 值 | 说明 |
|---|---|---|
| 服务名（Name） | `UsbDriveService` | 命令行/脚本里用的短名 |
| 显示名（DisplayName） | `USB 盘符自动管理服务` | 服务管理器里看到的名称 |
| 描述 | `监听 U 盘/移动硬盘插拔，按规则分配固定盘符；冲突时拒绝并记录日志。` | |
| 可执行文件 | `C:\Program Files\UsbDriveManager\UsbDriveService.exe` | 安装时复制；开发期脚本允许指向 `bin\Release` 输出 |
| 工作目录 | 可执行文件所在目录 | 不使用相对路径读配置 |
| 依赖服务 | 无 | WMI（`Winmgmt`）由系统自动启动，延迟启动已足够 |

## 2. 启动类型

```
StartType = Automatic（自动）
DelayedAutoStart = true（延迟启动）
```

- 用**延迟启动**：开机时先让系统服务（含 WMI）就绪，避免服务在 `Win32_VolumeChangeEvent` 订阅时拿到
  “WMI 不可用”而进入重试。
- 不用 `Automatic`（立即）的原因：立即启动在低配机上经常与 WMI 初始化抢跑。

对应命令（PowerShell）：

```powershell
sc.exe config UsbDriveService start= delayed-auto
```

> 注意 `sc.exe` 的 `start= ` 后面必须有空格，`delayed-auto` 是连写。

## 3. 登录账号

```
Account = .\UsbDriveMgrSvc      （本机账号，非域账号）
Group   = Administrators
PasswordNeverExpires = true
UserMayNotChangePassword = true
Right   = SeServiceLogonRight（作为服务登录）
```

- **不用 LocalSystem**：改盘符需要管理员权限，`LocalSystem` 虽然权限够，但会把服务暴露在网络登录上下文，
  且方案要求“服务账号只干活、操作员账号跑编辑器”的角色分离。
- **密码永不过期**：服务账号密码过期会导致服务在下次重启时 1069 登录失败，这是无人值守机器最常见的坑。
- 账号只需 `SeServiceLogonRight` 与 Administrators 组成员身份；不需要交互式登录。

## 4. 失败恢复

```
第一次失败：60 秒后重启
第二次失败：60 秒后重启
第三次失败：120 秒后重启
重置失败计数：86400 秒（1 天）
```

```powershell
sc.exe failure UsbDriveService reset= 86400 actions= restart/60000/restart/60000/restart/120000
```

- 不做“失败后运行程序”动作（不弹窗、不重启机器）。
- 恢复动作只重启服务本身；服务内部对单次分配失败只记日志，不退出进程。

## 5. 数据与权限

| 路径 | 权限 |
|---|---|
| `C:\ProgramData\UsbDriveManager\` | `Administrators` 完全控制；`UsbDriveMgrSvc` 修改（写日志/账本） |
| `C:\ProgramData\UsbDriveManager\config.json` | 服务只读、编辑器（提权后）可写 |
| `C:\ProgramData\UsbDriveManager\logs\` | 服务写入 |

- 目录由安装脚本创建，ACL 显式设置，不依赖继承。
- 配置不放在服务账号的 profile 下，避免“服务账号换密码/重建后配置丢失”。

## 6. 运行模式

| 模式 | 启动方式 | 用途 |
|---|---|---|
| 服务模式 | SCM 启动 | 生产 |
| 控制台模式 | `UsbDriveService.exe --console` | 联调、看实时日志（不注册服务） |

控制台模式下所有分配逻辑一致，只是日志同时输出到控制台；用于现场排查“为什么没分配”。

## 7. 安装/卸载

> 本机默认执行策略是 Restricted，直接右键运行 `.ps1` 会被拦。用下面的方式跑，
> 或先 `Set-ExecutionPolicy -Scope Process Bypass`。

```powershell
# 安装（会创建账号、授权利、注册服务、启动）
powershell -ExecutionPolicy Bypass -File .\deploy\Install-UsbDriveService.ps1

# 指定构建输出（开发期）
powershell -ExecutionPolicy Bypass -File .\deploy\Install-UsbDriveService.ps1 `
    -ServiceExe .\src\UsbDriveService\bin\Release\net8.0-windows\UsbDriveService.exe

# 卸载（保留 ProgramData 配置与日志）
powershell -ExecutionPolicy Bypass -File .\deploy\Uninstall-UsbDriveService.ps1
```

## 8. 排查

| 现象 | 可能原因 | 处理 |
|---|---|---|
| 启动报 1069（登录失败） | 密码过期/错误、账号不在 Administrators | 重跑安装脚本重置密码；检查组 |
| 启动报 1053（超时） | `OnStart` 里做了耗时操作 | `OnStart` 只起后台线程，WMI 订阅放线程内 |
| 服务起但插盘无反应 | WMI 订阅失败、规则未命中、盘符被占 | 看 `logs\service-*.log` 与事件日志源 `UsbDriveService` |
| 分配失败 | 目标盘符被无关卷占用 | 日志会写占用者；编辑器里改配置后重启服务 |
