import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  AlertTriangle,
  CheckCircle2,
  CircleSlash,
  FolderOpen,
  HardDrive,
  LayoutDashboard,
  ListChecks,
  LogOut,
  MemoryStick,
  Play,
  Plus,
  RefreshCw,
  Settings,
  ShieldCheck,
  Square,
  Trash2,
  Usb,
  X,
} from 'lucide-react'
import { call, isHosted } from './bridge'

type Status = {
  installed: boolean
  state: string
  exePath: string
  account: string
  configPath: string
  logPath: string
  ruleCount: number
}

type Device = {
  id: string
  name: string
  vid: string
  pid: string
  serial: string
  serialUsable: boolean
  letters: string[]
  volumeId: string
  partitions: number
  sizeBytes: number
  status: string
  note: string
  targetLetter: string
}

type Rule = {
  id: string
  name: string
  vid: string
  pid: string
  serial: string
  target: string
  enabled: boolean
}

type Volume = {
  letter: string
  driveType: number
  label: string
  sizeBytes: number
  freeBytes: number
  isUsb: boolean
  diskModel: string
  fileSystem: string
}

type Toast = { kind: 'ok' | 'err'; text: string }

const LETTERS = 'DEFGHIJKLMNOPQRSTUVWXYZ'.split('')

function cx(...parts: Array<string | false | null | undefined>) {
  return parts.filter(Boolean).join(' ')
}

function Button({
  variant = 'secondary',
  size = 'md',
  className,
  children,
  ...rest
}: React.ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'primary' | 'secondary' | 'ghost' | 'danger'
  size?: 'sm' | 'md'
}) {
  const base =
    'inline-flex items-center justify-center gap-1.5 rounded-md border text-sm font-medium transition-colors disabled:pointer-events-none disabled:opacity-50'
  const sizes = { sm: 'h-8 px-2.5', md: 'h-9 px-3.5' }
  const variants = {
    primary: 'border-neutral-900 bg-neutral-900 text-white hover:bg-neutral-800',
    secondary: 'border-neutral-200 bg-white text-neutral-800 hover:bg-neutral-50',
    ghost: 'border-transparent bg-transparent text-neutral-600 hover:bg-neutral-100',
    danger: 'border-transparent bg-transparent text-neutral-400 hover:bg-red-50 hover:text-red-600',
  }
  return (
    <button className={cx(base, sizes[size], variants[variant], className)} {...rest}>
      {children}
    </button>
  )
}

function Badge({
  tone = 'neutral',
  children,
}: {
  tone?: 'neutral' | 'green' | 'amber' | 'red' | 'blue' | 'purple'
  children: React.ReactNode
}) {
  const tones = {
    neutral: 'bg-neutral-100 text-neutral-600 ring-neutral-200',
    green: 'bg-emerald-50 text-emerald-700 ring-emerald-200',
    amber: 'bg-amber-50 text-amber-700 ring-amber-200',
    red: 'bg-red-50 text-red-700 ring-red-200',
    blue: 'bg-sky-50 text-sky-700 ring-sky-200',
    purple: 'bg-violet-50 text-violet-700 ring-violet-200',
  }
  return (
    <span
      className={cx(
        'inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-xs font-medium ring-1 ring-inset',
        tones[tone],
      )}
    >
      {children}
    </span>
  )
}

function DeviceBadge({ device }: { device: Device }) {
  const letter = device.letters[0] ?? ''
  if (device.status === 'assigned')
    return (
      <Badge tone="green">
        <CheckCircle2 className="h-3 w-3" />
        已分配 {letter}:
      </Badge>
    )
  if (device.status === 'ready')
    return (
      <Badge tone="blue">
        <CheckCircle2 className="h-3 w-3" />
        待分配 {device.targetLetter}:
      </Badge>
    )
  if (device.status === 'competition')
    return (
      <Badge tone="amber">
        <AlertTriangle className="h-3 w-3" />
        占用分配{letter ? ` ${letter}:` : ''}
      </Badge>
    )
  if (device.status === 'conflict')
    return (
      <Badge tone="red">
        <AlertTriangle className="h-3 w-3" />
        冲突拒绝
      </Badge>
    )
  return (
    <Badge tone="purple">
      <CircleSlash className="h-3 w-3" />
      自由分配{letter ? ` ${letter}:` : ''}
    </Badge>
  )
}

// 设备图标颜色：与状态徽章同色系，保证图标和状态文字视觉一致。
function statusIconClass(status: string) {
  switch (status) {
    case 'assigned':
      return 'text-emerald-600'
    case 'ready':
      return 'text-sky-600'
    case 'competition':
      return 'text-amber-600'
    case 'conflict':
      return 'text-red-600'
    default:
      return 'text-violet-600'
  }
}

function Card({
  title,
  description,
  action,
  children,
}: {
  title: string
  description?: string
  action?: React.ReactNode
  children: React.ReactNode
}) {
  return (
    <section className="overflow-hidden rounded-lg border border-neutral-200 bg-white">
      <header className="flex items-start justify-between gap-4 border-b border-neutral-100 px-5 py-4">
        <div>
          <h2 className="text-sm font-semibold text-neutral-900">{title}</h2>
          {description ? <p className="mt-0.5 text-xs text-neutral-500">{description}</p> : null}
        </div>
        {action}
      </header>
      {children}
    </section>
  )
}

function StatCard({
  label,
  value,
  hint,
  icon,
  valueClass,
  hintClass,
}: {
  label: string
  value: string
  hint: string
  icon: React.ReactNode
  valueClass?: string
  hintClass?: string
}) {
  return (
    <div className="rounded-lg border border-neutral-200 bg-white p-4">
      <div className="flex items-center justify-between">
        <span className="text-xs font-medium text-neutral-500">{label}</span>
        <span className="text-neutral-400">{icon}</span>
      </div>
      <div className={cx('mt-2 text-2xl font-semibold tracking-tight tabular', valueClass ?? 'text-neutral-900')}>
        {value}
      </div>
      <div className={cx('mt-1 text-xs', hintClass ?? 'text-neutral-500')}>{hint}</div>
    </div>
  )
}

function InstallDialog({
  open,
  onClose,
  onSubmit,
}: {
  open: boolean
  onClose: () => void
  onSubmit: (account: string, password: string) => Promise<void>
}) {
  const [step, setStep] = useState<1 | 2 | 3>(1)
  const [account, setAccount] = useState('UsbDriveMgrSvc')
  const [password, setPassword] = useState('')
  const [confirm, setConfirm] = useState('')
  const [error, setError] = useState('')
  const [installing, setInstalling] = useState(false)

  useEffect(() => {
    if (open) {
      setStep(1)
      setError('')
      setInstalling(false)
    }
  }, [open])

  if (!open) return null

  const validate = () => {
    if (!account.trim()) return '账号不能为空'
    if (password.length < 12) return '密码至少 12 位'
    if (password !== confirm) return '两次输入的密码不一致'
    return ''
  }

  const next = () => {
    const message = validate()
    if (message) {
      setError(message)
      return
    }
    setError('')
    setStep(2)
  }

  const install = async () => {
    setInstalling(true)
    setError('')
    try {
      await onSubmit(account.trim(), password)
      setStep(3)
    } catch (failure) {
      setError(failure instanceof Error ? failure.message : String(failure))
    } finally {
      setInstalling(false)
    }
  }

  const title = step === 3 ? '安装完成' : step === 2 ? '确认安装' : '新建专用账号'

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-neutral-950/30 p-6">
      <div className="w-[460px] rounded-lg border border-neutral-200 bg-white shadow-xl">
        <div className="flex items-center justify-between border-b border-neutral-100 px-5 py-3.5">
          <div className="flex items-center gap-2">
            <h3 className="text-sm font-semibold text-neutral-900">{title}</h3>
            {step < 3 && <span className="text-[11px] text-neutral-400">第 {step} / 2 步</span>}
          </div>
          <button onClick={onClose} className="rounded p-1 text-neutral-400 hover:bg-neutral-100">
            <X className="h-4 w-4" />
          </button>
        </div>

        {step === 1 && (
          <div className="space-y-3 px-5 py-4">
            <p className="text-[11px] leading-relaxed text-neutral-500">
              会新建一个专供服务使用的本地账号：加入 Administrators、授予“作为服务登录”、禁止交互登录，密码永不过期。
            </p>

            <label className="block text-xs text-neutral-500">
              专用账号名
              <input
                value={account}
                onChange={(event) => setAccount(event.target.value)}
                placeholder="例如 UsbDriveMgrSvc"
                className="mt-1 h-9 w-full rounded-md border border-neutral-200 px-2.5 text-[13px] text-neutral-900 outline-none focus:border-neutral-400"
              />
            </label>

            <label className="block text-xs text-neutral-500">
              密码（至少 12 位）
              <input
                type="password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
                className="mt-1 h-9 w-full rounded-md border border-neutral-200 px-2.5 text-[13px] text-neutral-900 outline-none focus:border-neutral-400"
              />
            </label>

            <label className="block text-xs text-neutral-500">
              确认密码
              <input
                type="password"
                value={confirm}
                onChange={(event) => setConfirm(event.target.value)}
                className="mt-1 h-9 w-full rounded-md border border-neutral-200 px-2.5 text-[13px] text-neutral-900 outline-none focus:border-neutral-400"
              />
            </label>

            {error ? <div className="text-xs text-red-600">{error}</div> : null}

            <p className="text-[11px] leading-relaxed text-neutral-500">
              密码只用于服务登录，改密码需要重新安装服务；建议记在密码管理器里。
            </p>
          </div>
        )}

        {step === 2 && (
          <div className="space-y-3 px-5 py-4">
            <div className="divide-y divide-neutral-100 rounded-lg border border-neutral-200 text-[13px]">
              <div className="flex items-center justify-between px-3 py-2">
                <span className="text-neutral-500">方式</span>
                <span className="text-neutral-900">新建专用账号</span>
              </div>
              <div className="flex items-center justify-between px-3 py-2">
                <span className="text-neutral-500">账号</span>
                <span className="font-mono text-xs text-neutral-900">{account.trim()}</span>
              </div>
              <div className="flex items-center justify-between px-3 py-2">
                <span className="text-neutral-500">服务目录</span>
                <span className="font-mono text-[11px] text-neutral-700">
                  C:\Program Files\UsbDriveManager\service
                </span>
              </div>
            </div>

            <p className="text-[11px] leading-relaxed text-neutral-500">
              安装会先停掉旧服务，再按上面的账号注册并启动；配置和日志不受影响。
            </p>

            {error ? <div className="whitespace-pre-wrap text-xs text-red-600">{error}</div> : null}
          </div>
        )}

        {step === 3 && (
          <div className="space-y-3 px-5 py-6 text-center">
            <CheckCircle2 className="mx-auto h-10 w-10 text-emerald-500" />
            <div className="text-sm font-medium text-neutral-900">已登录：{account.trim()}</div>
            <div className="text-xs text-neutral-500">服务已安装并启动，插拔 U 盘会按规则分配盘符。</div>
          </div>
        )}

        <div className="flex justify-end gap-2 border-t border-neutral-100 px-5 py-3">
          {step === 1 && (
            <>
              <Button size="sm" onClick={onClose}>
                取消
              </Button>
              <Button size="sm" variant="primary" onClick={next}>
                下一步
              </Button>
            </>
          )}

          {step === 2 && (
            <>
              <Button size="sm" disabled={installing} onClick={() => setStep(1)}>
                上一步
              </Button>
              <Button size="sm" variant="primary" disabled={installing} onClick={() => void install()}>
                {installing ? <RefreshCw className="h-3.5 w-3.5 animate-spin" /> : null}
                {installing ? '正在安装…' : '安装并启动'}
              </Button>
            </>
          )}

          {step === 3 && (
            <Button size="sm" variant="primary" onClick={onClose}>
              完成
            </Button>
          )}
        </div>
      </div>
    </div>
  )
}

function formatSize(bytes: number) {
  if (!bytes || bytes <= 0) return '—'
  const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB']
  let value = bytes
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }
  return `${value >= 100 || unit === 0 ? Math.round(value) : value.toFixed(1)} ${units[unit]}`
}

function upsertRule(current: Rule[], device: Device, letter: string): Rule[] {
  const index = current.findIndex(
    (rule) =>
      rule.vid.toUpperCase() === device.vid.toUpperCase() &&
      rule.pid.toUpperCase() === device.pid.toUpperCase() &&
      (rule.serial || '') === (device.serial || ''),
  )

  if (index >= 0) {
    const next = [...current]
    next[index] = { ...next[index], target: letter, enabled: true }
    return next
  }

  return [
    ...current,
    {
      id: crypto.randomUUID(),
      name: device.name,
      vid: device.vid,
      pid: device.pid,
      serial: device.serial,
      target: letter,
      enabled: true,
    },
  ]
}

export default function App() {
  const [tab, setTab] = useState<'overview' | 'devices' | 'settings'>('overview')
  const [status, setStatus] = useState<Status | null>(null)
  const lastServiceState = useRef('')
  const [devices, setDevices] = useState<Device[]>([])
  const [usedLetters, setUsedLetters] = useState<string[]>([])
  const [rules, setRules] = useState<Rule[]>([])
  const [volumes, setVolumes] = useState<Volume[]>([])
  const [selectedId, setSelectedId] = useState('')
  const [draftLetter, setDraftLetter] = useState('U')
  const [saveAsRule, setSaveAsRule] = useState(true)
  const [busy, setBusy] = useState(false)
  const [toast, setToast] = useState<Toast | null>(null)
  const [installOpen, setInstallOpen] = useState(false)

  const selected = devices.find((device) => device.id === selectedId) ?? null

  const deviceForRule = (rule: Rule) =>
    devices.find(
      (device) =>
        device.vid.toUpperCase() === rule.vid.toUpperCase() &&
        device.pid.toUpperCase() === rule.pid.toUpperCase() &&
        (device.serial || '') === (rule.serial || ''),
    )

  const notify = useCallback((kind: Toast['kind'], text: string) => {
    setToast({ kind, text })
    window.setTimeout(() => setToast(null), 4200)
  }, [])

  const refresh = useCallback(async () => {
    if (!isHosted()) {
      notify('err', '未运行在编辑器宿主内，无法读取后端数据。')
      return
    }

    const [nextStatus, deviceResult, configResult, volumeResult] = await Promise.all([
      call<Status>('getStatus'),
      call<{ devices: Device[]; usedLetters: string[] }>('listDevices'),
      call<{ rules: Rule[] }>('loadConfig'),
      call<{ volumes: Volume[] }>('listVolumes'),
    ])

    setStatus(nextStatus)
    lastServiceState.current = nextStatus.state
    setDevices(deviceResult.devices)
    setUsedLetters(deviceResult.usedLetters)
    setRules(configResult.rules)
    setVolumes(volumeResult.volumes)

    const firstFree = LETTERS.find((letter) => !deviceResult.usedLetters.includes(letter))
    if (firstFree) {
      setDraftLetter((current) => (deviceResult.usedLetters.includes(current) ? firstFree : current))
    }
    setSelectedId((current) =>
      current && deviceResult.devices.some((device) => device.id === current)
        ? current
        : deviceResult.devices[0]?.id ?? '',
    )
  }, [notify])

  const run = useCallback(
    async (action: () => Promise<void>) => {
      setBusy(true)
      try {
        await action()
      } catch (error) {
        notify('err', error instanceof Error ? error.message : String(error))
      } finally {
        setBusy(false)
      }
    },
    [notify],
  )

  useEffect(() => {
    void run(refresh)
  }, [run, refresh])

  // 服务被重启（不管是谁触发的）→ 状态变化时自动刷新设备与盘符占用。
  useEffect(() => {
    let cancelled = false

    const check = async () => {
      try {
        const next = await call<Status>('getStatus')
        if (cancelled) {
          return
        }

        const previous = lastServiceState.current
        lastServiceState.current = next.state

        if (previous && previous !== next.state) {
          await refresh()
        }
      } catch {
        // 轮询失败就等下一轮，不打扰用户。
      }
    }

    const timer = window.setInterval(check, 4000)

    return () => {
      cancelled = true
      window.clearInterval(timer)
    }
  }, [refresh])

  // 切回窗口时也刷新一次（外部改盘符、插拔后回来能看到最新状态）。
  useEffect(() => {
    const onFocus = () => {
      void refresh().catch(() => undefined)
    }

    window.addEventListener('focus', onFocus)
    return () => window.removeEventListener('focus', onFocus)
  }, [refresh])

  const letterOptions = useMemo(
    () => LETTERS.map((letter) => ({ letter, used: usedLetters.includes(letter) })),
    [usedLetters],
  )

  const serviceRunning = status?.state === 'Running'
  const serviceInstalled = Boolean(status?.installed)

  const applyLetter = () =>
    run(async () => {
      if (!selected) throw new Error('先在上表选中一台设备。')
      const result = await call<{ message: string }>('assignLetter', {
        deviceId: selected.id,
        letter: draftLetter,
        force: false,
      })

      if (saveAsRule) {
        const nextRules = upsertRule(rules, selected, draftLetter)
        await call('saveConfig', { rules: nextRules })
        setRules(nextRules)
        notify('ok', `已应用并保存规则：${selected.name} → ${draftLetter}:`)
      } else {
        notify('ok', result.message)
      }

      // 等服务把卷信息和盘符占用稳定下来，再刷新一次，避免还显示旧的“占用中”。
      await new Promise((resolve) => window.setTimeout(resolve, 1200))
      await refresh()
    })

  const saveRules = () =>
    run(async () => {
      const result = await call<{ restarted: boolean; warnings: string[] }>('saveConfig', { rules })
      notify(
        'ok',
        result.warnings.length > 0
          ? `已保存${result.restarted ? '并重启服务' : ''}：${result.warnings[0]}`
          : `已保存${result.restarted ? '并重启服务' : ''}`,
      )

      if (result.restarted) {
        await new Promise((resolve) => window.setTimeout(resolve, 1200))
      }

      await refresh()
    })

  const importRule = () =>
    run(async () => {
      if (!selected) throw new Error('先在上表选中一台设备。')
      if (selected.partitions > 1) throw new Error('第一版只支持单分区设备，这台有多个分区。')
      setRules((current) => upsertRule(current, selected, draftLetter))
      notify('ok', '已加入或更新规则，记得点“保存并重启服务”。')
    })

  const patchRule = (id: string, patch: Partial<Rule>) =>
    setRules((current) => current.map((rule) => (rule.id === id ? { ...rule, ...patch } : rule)))

  const nav = [
    { id: 'overview', label: '概览', icon: LayoutDashboard },
    { id: 'devices', label: '设备与规则', icon: HardDrive },
    { id: 'settings', label: '设置', icon: Settings },
  ] as const

  return (
    <div className="flex h-full min-h-screen">
      <aside className="flex w-[236px] shrink-0 flex-col border-r border-neutral-200 bg-white">
        <div className="flex items-center gap-2.5 px-4 py-4">
          <div className="flex h-8 w-8 items-center justify-center rounded-md bg-neutral-900 text-white">
            <Usb className="h-4 w-4" />
          </div>
          <div className="leading-tight">
            <div className="text-[13px] font-semibold text-neutral-900">USB 盘符管理</div>
            <div className="text-[11px] text-neutral-500">配置编辑器 · v0.2.0</div>
          </div>
        </div>

        <nav className="px-2 py-1">
          {nav.map((item) => {
            const Icon = item.icon
            const active = tab === item.id
            return (
              <button
                key={item.id}
                onClick={() => setTab(item.id)}
                className={cx(
                  'mb-0.5 flex w-full items-center gap-2 rounded-md px-2.5 py-1.5 text-[13px] transition-colors',
                  active ? 'bg-neutral-100 font-medium text-neutral-900' : 'text-neutral-600 hover:bg-neutral-50',
                )}
              >
                <Icon className="h-4 w-4" />
                {item.label}
              </button>
            )
          })}
        </nav>

        <div className="mt-auto m-3 rounded-lg border border-neutral-200 bg-neutral-50 p-3">
          <div className="flex items-center gap-1.5 text-xs font-medium text-neutral-700">
            <span
              className={cx(
                'h-2 w-2 rounded-full',
                serviceRunning ? 'bg-emerald-500' : serviceInstalled ? 'bg-amber-500' : 'bg-neutral-300',
              )}
            />
            {serviceRunning ? '服务运行中' : serviceInstalled ? '服务已停止' : '服务未安装'}
          </div>
          <div className="mt-1 text-[11px] text-neutral-500">
            {serviceInstalled ? 'UsbDriveService · 延迟自启' : '点“安装服务”开始'}
          </div>
          <button
            onClick={() => run(async () => void (await call('openFolder', { path: status?.logPath ?? '' })))}
            className="mt-2 inline-flex items-center gap-1 text-[11px] text-neutral-500 hover:text-neutral-800"
          >
            <FolderOpen className="h-3 w-3" />
            打开日志目录
          </button>
        </div>
      </aside>

      <main className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-16 shrink-0 items-center justify-between border-b border-neutral-200 bg-white px-6">
          <div>
            <h1 className="text-[15px] font-semibold text-neutral-900">
              {tab === 'overview' && '概览'}
              {tab === 'devices' && '设备与规则'}
              {tab === 'settings' && '设置'}
            </h1>
            <p className="text-xs text-neutral-500">
              {status?.installed ? `账号 ${status.account || '—'} · ${status.exePath}` : '服务未安装'}
            </p>
          </div>
          <div className="flex items-center gap-2">
            <span
              className={cx(
                'inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium ring-1 ring-inset',
                serviceRunning
                  ? 'bg-emerald-50 text-emerald-700 ring-emerald-200'
                  : serviceInstalled
                    ? 'bg-amber-50 text-amber-700 ring-amber-200'
                    : 'bg-neutral-100 text-neutral-600 ring-neutral-200',
              )}
            >
              <span
                className={cx(
                  'h-1.5 w-1.5 rounded-full',
                  serviceRunning ? 'bg-emerald-500' : serviceInstalled ? 'bg-amber-500' : 'bg-neutral-400',
                )}
              />
              {serviceRunning ? '运行中' : serviceInstalled ? '已停止' : '未安装'}
            </span>
            <Button
              size="sm"
              variant="ghost"
              disabled={!serviceInstalled || serviceRunning || busy}
              onClick={() => run(async () => void (await call('startService')) ?? (await refresh()))}
            >
              <Play className="h-3.5 w-3.5" />
              启动
            </Button>
            <Button
              size="sm"
              variant="ghost"
              disabled={!serviceRunning || busy}
              onClick={() => run(async () => void (await call('stopService')) ?? (await refresh()))}
            >
              <Square className="h-3.5 w-3.5" />
              停止
            </Button>
            <Button size="sm" variant="primary" disabled={busy} onClick={() => setInstallOpen(true)}>
              安装服务
            </Button>
            <Button size="sm" disabled={busy} onClick={() => run(refresh)}>
              <RefreshCw className={cx('h-3.5 w-3.5', busy && 'animate-spin')} />
            </Button>
          </div>
        </header>

        <div className="flex-1 overflow-auto px-6 py-5">
          {tab === 'overview' && (
            <>
              <div className="grid grid-cols-3 gap-4">
                <StatCard
                  label="服务状态"
                  value={serviceRunning ? '运行中' : serviceInstalled ? '已停止' : '未安装'}
                  hint={status?.installed ? 'UsbDriveService · 延迟自动启动' : '尚未注册服务'}
                  valueClass={
                    serviceRunning ? 'text-emerald-600' : serviceInstalled ? 'text-amber-500' : 'text-neutral-900'
                  }
                  hintClass={
                    serviceRunning ? 'text-emerald-600' : serviceInstalled ? 'text-amber-500' : 'text-neutral-500'
                  }
                  icon={
                    <ShieldCheck
                      className={cx(
                        'h-4 w-4',
                        serviceRunning ? 'text-emerald-600' : serviceInstalled ? 'text-amber-500' : 'text-neutral-400',
                      )}
                    />
                  }
                />
                <StatCard
                  label="规则"
                  value={String(rules.length)}
                  hint={`${rules.filter((rule) => rule.enabled).length} 条启用`}
                  icon={<ListChecks className="h-4 w-4" />}
                />
                <StatCard
                  label="当前 USB 设备"
                  value={String(devices.length)}
                  hint={devices.length ? devices.map((device) => device.letters[0] ?? '—').join(' / ') : '未插入'}
                  icon={<Usb className="h-4 w-4" />}
                />
              </div>

              <div className="mt-5">
                <Card title="所有磁盘" description="系统上已挂载的卷：内置硬盘与 U 盘用不同图标区分">
                  <table className="w-full text-[13px]">
                    <thead>
                      <tr className="border-b border-neutral-100 text-left text-xs text-neutral-500">
                        <th className="px-5 py-2 font-medium">盘符</th>
                        <th className="px-3 py-2 font-medium">名称</th>
                        <th className="px-3 py-2 font-medium">占用 / 总容量</th>
                      </tr>
                    </thead>
                    <tbody>
                      {volumes.length === 0 && (
                        <tr>
                          <td colSpan={3} className="px-5 py-8 text-center text-xs text-neutral-500">
                            未获取到磁盘信息
                          </td>
                        </tr>
                      )}
                      {volumes.map((volume) => {
                        const used = volume.sizeBytes > 0 ? volume.sizeBytes - volume.freeBytes : 0
                        const ratio = volume.sizeBytes > 0 ? used / volume.sizeBytes : 0
                        return (
                          <tr key={volume.letter} className="group border-b border-neutral-50 last:border-0">
                            <td
                              className="cursor-pointer px-5 py-3"
                              onDoubleClick={() => run(async () => void (await call('openFolder', { path: `${volume.letter}:\\` })))}
                              title="双击打开资源管理器"
                            >
                              <div className="flex items-center gap-2.5">
                                <span className="font-mono text-[13px] font-medium text-neutral-900 group-hover:text-sky-600">
                                  {volume.letter}:
                                </span>
                                {volume.isUsb ? (
                                  <MemoryStick className="h-4 w-4 text-sky-500" />
                                ) : (
                                  <HardDrive className="h-4 w-4 text-neutral-500" />
                                )}
                              </div>
                            </td>
                            <td className="px-3 py-3 text-neutral-800">
                              {volume.label || volume.diskModel || '—'}
                            </td>
                            <td className="px-3 py-3">
                              <div className="relative flex items-center gap-2.5">
                                <div className="h-1.5 w-24 shrink-0 overflow-hidden rounded-full bg-neutral-100">
                                  <div
                                    className="h-full rounded-full bg-sky-500"
                                    style={{ width: `${Math.round(ratio * 100)}%` }}
                                  />
                                </div>
                                <span className="whitespace-nowrap text-xs text-neutral-600 tabular">
                                  占用 {formatSize(used)} / {formatSize(volume.sizeBytes)}
                                </span>
                                <div className="pointer-events-none absolute bottom-full left-1/2 z-20 -translate-x-1/2 -translate-y-1 whitespace-nowrap opacity-0 transition-opacity group-hover:opacity-100">
                                  <div className="rounded-md bg-white px-2 py-1 text-xs text-neutral-700 shadow-lg ring-1 ring-neutral-200">
                                    可用 {formatSize(volume.freeBytes)}
                                  </div>
                                </div>
                              </div>
                            </td>
                          </tr>
                        )
                      })}
                    </tbody>
                  </table>
                </Card>
              </div>
            </>
          )}

          {tab === 'devices' && (
            <div className="mt-5">
              <Card
                title="当前插入的设备"
                description="选中一行后在下方指定盘符；只对当次生效，勾选后才会写入规则"
                action={
                  <Button size="sm" disabled={busy} onClick={() => run(refresh)}>
                    <RefreshCw className={cx('h-3.5 w-3.5', busy && 'animate-spin')} />
                    刷新
                  </Button>
                }
              >
                <table className="w-full text-[13px]">
                  <thead>
                    <tr className="border-b border-neutral-100 text-left text-xs text-neutral-500">
                      <th className="px-5 py-2 font-medium">设备</th>
                      <th className="px-3 py-2 font-medium">容量</th>
                      <th className="px-3 py-2 font-medium">序列号</th>
                    </tr>
                  </thead>
                  <tbody>
                    {devices.length === 0 && (
                      <tr>
                        <td colSpan={3} className="px-5 py-8 text-center text-xs text-neutral-500">
                          当前没有插入 USB 设备
                        </td>
                      </tr>
                    )}
                    {devices.map((device) => {
                      const active = device.id === selectedId
                      return (
                        <tr
                          key={device.id}
                          onClick={() => setSelectedId(device.id)}
                          className={cx(
                            'group cursor-pointer border-b border-neutral-100 last:border-0',
                            active ? 'bg-sky-100' : 'hover:bg-neutral-50',
                          )}
                        >
                          <td
                            className="relative cursor-pointer px-5 py-3"
                            title={device.letters[0] ? '双击打开资源管理器' : undefined}
                            onDoubleClick={() => {
                              const letter = device.letters[0]
                              if (letter) {
                                void run(async () => void (await call('openFolder', { path: `${letter}:\\` })))
                              }
                            }}
                          >
                            {active && (
                              <span className="absolute inset-y-0 left-0 w-[3px] rounded-r bg-sky-600" />
                            )}
                            <div className="pointer-events-none absolute left-1/2 top-0 z-20 -translate-x-1/2 -translate-y-1 opacity-0 transition-opacity group-hover:opacity-100">
                              <div className="rounded-md bg-white px-2 py-1 shadow-lg ring-1 ring-neutral-200">
                                <DeviceBadge device={device} />
                              </div>
                            </div>
                            <div className="flex items-center gap-2.5">
                              <span
                                className={cx(
                                  'font-mono text-[13px] font-medium leading-none',
                                  statusIconClass(device.status),
                                )}
                              >
                                {device.letters[0] ? `${device.letters[0]}:` : '—'}
                              </span>
                              <div className="flex h-7 w-7 items-center justify-center rounded-md border border-neutral-200 bg-white">
                                <HardDrive className={cx('h-3.5 w-3.5', statusIconClass(device.status))} />
                              </div>
                              <div>
                                <div
                                  className="flex items-center gap-1.5 font-medium text-neutral-900"
                                  title={`${device.vid}:${device.pid}${device.serial ? ` · ${device.serial}` : ''}`}
                                >
                                  {device.name}
                                  {active && <CheckCircle2 className="h-3.5 w-3.5 text-sky-600" />}
                                </div>
                                <div className="text-[11px] text-neutral-500">{device.note}</div>
                              </div>
                            </div>
                          </td>
                          <td className="px-3 py-3 text-xs text-neutral-600 tabular">
                            {formatSize(device.sizeBytes)}
                          </td>
                          <td className="px-3 py-3 font-mono text-xs text-neutral-600">
                            {device.serial || '—'}
                          </td>
                        </tr>
                      )
                    })}
                  </tbody>
                </table>

                <div className="flex flex-wrap items-center gap-3 border-t border-neutral-100 bg-neutral-50/60 px-5 py-3">
                  <span className="text-xs text-neutral-500">
                    目标盘符
                    {selected ? <span className="ml-1 text-neutral-700">（选中：{selected.name}）</span> : null}
                  </span>
                  <select
                    value={draftLetter}
                    onChange={(event) => setDraftLetter(event.target.value)}
                    className="h-8 rounded-md border border-neutral-200 bg-white px-2 text-[13px] text-neutral-800 outline-none focus:border-neutral-400"
                  >
                    {letterOptions.map(({ letter, used }) => (
                      <option key={letter} value={letter} disabled={used}>
                        {letter}:{used ? '　已被占用' : '　空闲'}
                      </option>
                    ))}
                  </select>
                  <Button size="sm" variant="primary" disabled={!selected || busy} onClick={applyLetter}>
                    <CheckCircle2 className="h-3.5 w-3.5" />
                    应用到选中设备
                  </Button>
                  <label className="inline-flex items-center gap-1.5 text-xs text-neutral-600">
                    <input
                      type="checkbox"
                      checked={saveAsRule}
                      onChange={(event) => setSaveAsRule(event.target.checked)}
                      className="h-3.5 w-3.5 rounded border-neutral-300"
                    />
                    同时保存为规则
                  </label>
                </div>
              </Card>
            </div>
          )}

          {tab === 'devices' && (
            <div className="mt-5">
              <Card
                title="规则"
                description="设备指纹只读（来自设备导入），可改名称、目标盘符与启用；保存后自动重启服务"
                action={
                  <div className="flex gap-2">
                    <Button size="sm" disabled={!selected || busy} onClick={importRule}>
                      <Usb className="h-3.5 w-3.5" />
                      从设备导入
                    </Button>
                  </div>
                }
              >
                <table className="w-full text-[13px]">
                  <thead>
                    <tr className="border-b border-neutral-100 text-left text-xs text-neutral-500">
                      <th className="px-5 py-2 font-medium">设备</th>
                      <th className="px-3 py-2 font-medium">容量</th>
                      <th className="px-3 py-2 font-medium">序列号</th>
                      <th className="px-3 py-2 font-medium">目标盘符</th>
                      <th className="px-3 py-2 font-medium">启用</th>
                      <th className="px-3 py-2" />
                    </tr>
                  </thead>
                  <tbody>
                    {rules.length === 0 && (
                      <tr>
                        <td colSpan={6} className="px-5 py-8 text-center text-xs text-neutral-500">
                          还没有规则，先选中设备再点“从设备导入”
                        </td>
                      </tr>
                    )}
                    {rules.map((rule) => (
                      <tr key={rule.id} className="border-b border-neutral-50 last:border-0">
                        <td className="px-5 py-3">
                          <div className="flex items-center gap-2.5">
                            <div className="flex h-7 w-7 items-center justify-center rounded-md border border-neutral-200 bg-white">
                              <HardDrive
                                className={cx(
                                  'h-3.5 w-3.5',
                                  deviceForRule(rule) ? 'text-emerald-600' : 'text-neutral-400',
                                )}
                              />
                            </div>
                            <div
                              className="font-medium text-neutral-900"
                              title={`${rule.vid}:${rule.pid}${rule.serial ? ` · ${rule.serial}` : ''}`}
                            >
                              {rule.name || '(未命名)'}
                            </div>
                          </div>
                        </td>
                        <td className="px-3 py-3 text-xs text-neutral-600 tabular">
                          {formatSize(deviceForRule(rule)?.sizeBytes ?? 0)}
                        </td>
                        <td className="px-3 py-3 font-mono text-xs text-neutral-600">
                          {rule.serial || '*'}
                        </td>
                        <td className="px-3 py-3">
                          <select
                            value={rule.target}
                            onChange={(event) => patchRule(rule.id, { target: event.target.value })}
                            className="h-8 rounded-md border border-neutral-200 bg-white px-2 text-[13px] text-neutral-800 outline-none focus:border-neutral-400"
                          >
                            {letterOptions.map(({ letter, used }) => {
                              const current = letter === rule.target
                              return (
                                <option key={letter} value={letter} disabled={used && !current}>
                                  {letter}: {current ? '当前' : used ? '占用中' : '空闲'}
                                </option>
                              )
                            })}
                          </select>
                        </td>
                        <td className="px-3 py-3">
                          <button
                            onClick={() => patchRule(rule.id, { enabled: !rule.enabled })}
                            className={cx(
                              'relative h-5 w-9 rounded-full transition-colors',
                              rule.enabled ? 'bg-neutral-900' : 'bg-neutral-200',
                            )}
                          >
                            <span
                              className={cx(
                                'absolute top-0.5 h-4 w-4 rounded-full bg-white transition-all',
                                rule.enabled ? 'left-[18px]' : 'left-0.5',
                              )}
                            />
                          </button>
                        </td>
                        <td className="px-3 py-3 text-right">
                          <Button
                            size="sm"
                            variant="danger"
                            onClick={() => setRules((current) => current.filter((item) => item.id !== rule.id))}
                          >
                            <Trash2 className="h-3.5 w-3.5" />
                          </Button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>

                <div className="flex items-center justify-between gap-3 border-t border-neutral-100 bg-neutral-50/60 px-5 py-3">
                  <div className="flex items-center gap-1.5 text-xs text-neutral-500">
                    <AlertTriangle className="h-3.5 w-3.5 text-amber-500" />
                    保存前会跑一遍校验；同一盘符多条规则按先到先得
                  </div>
                  <div className="flex gap-2">
                    <Button size="sm" disabled={busy} onClick={() => run(refresh)}>
                      放弃修改
                    </Button>
                    <Button size="sm" variant="primary" disabled={busy} onClick={saveRules}>
                      保存并重启服务
                    </Button>
                  </div>
                </div>
              </Card>
            </div>
          )}

          {tab === 'settings' && (
            <div className="mt-5 flex flex-col gap-5">
              <Card title="路径" description="配置与日志固定在 ProgramData，卸载时保留">
                <div className="divide-y divide-neutral-100 text-[13px]">
                  {[
                    ['配置文件', status?.configPath ?? '—'],
                    ['日志目录', status?.logPath ?? '—'],
                    ['服务程序', status?.exePath ?? '—'],
                    ['服务账号', status?.account || '—'],
                  ].map(([label, value]) => (
                    <div key={label} className="flex items-center justify-between gap-6 px-5 py-3">
                      <span className="shrink-0 text-neutral-500">{label}</span>
                      <span className="truncate font-mono text-xs text-neutral-800">{value}</span>
                    </div>
                  ))}
                </div>
              </Card>

              <Card
                title="账号与服务"
                description="当前运行服务的账号；退出登录会卸载服务，然后用另一个账号重新安装"
              >
                <div className="divide-y divide-neutral-100 text-[13px]">
                  <div className="flex items-center justify-between gap-6 px-5 py-3">
                    <span className="shrink-0 text-neutral-500">已登录账号</span>
                    <span className="truncate font-mono text-xs text-neutral-900">
                      {status?.account || '—'}
                    </span>
                  </div>
                  <div className="flex items-center justify-between gap-6 px-5 py-3">
                    <span className="shrink-0 text-neutral-500">服务状态</span>
                    <span className="text-xs text-neutral-800">
                      {serviceInstalled ? (serviceRunning ? '运行中' : '已停止') : '未安装'}
                    </span>
                  </div>
                </div>

                <div className="flex items-center justify-between gap-6 border-t border-neutral-100 px-5 py-4">
                  <div className="text-xs text-neutral-500">
                    {serviceInstalled
                      ? '退出登录 = 停止并删除服务；配置、日志和账号都保留。'
                      : '当前未登录（没有安装服务）。'}
                  </div>
                  <div className="flex gap-2">
                    <Button
                      size="sm"
                      disabled={!serviceInstalled || busy}
                      className="border-red-200 text-red-600 hover:border-red-300 hover:bg-red-50"
                      onClick={() =>
                        run(async () => {
                          if (!window.confirm('将停止并删除服务，然后用另一个账号重新安装。继续？')) {
                            return
                          }
                          await call('uninstallService')
                          await refresh()
                          setInstallOpen(true)
                        })
                      }
                    >
                      <LogOut className="h-3.5 w-3.5" />
                      退出登录
                    </Button>
                  </div>
                </div>
              </Card>
            </div>
          )}
        </div>
      </main>

      <InstallDialog
        open={installOpen}
        onClose={() => setInstallOpen(false)}
        onSubmit={async (account, password) => {
          await call('installService', { mode: 'create', account, password })
          await refresh()
        }}
      />

      {toast && (
        <div
          className={cx(
            'fixed bottom-5 right-5 z-50 max-w-md rounded-lg border px-4 py-3 text-[13px] shadow-lg',
            toast.kind === 'ok'
              ? 'border-emerald-200 bg-emerald-50 text-emerald-800'
              : 'border-red-200 bg-red-50 text-red-800',
          )}
        >
          {toast.text}
        </div>
      )}
    </div>
  )
}
