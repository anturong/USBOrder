type Pending = {
  resolve: (value: any) => void
  reject: (error: Error) => void
}

const pending = new Map<string, Pending>()
let sequence = 0

type HostWindow = Window & {
  chrome?: {
    webview?: {
      postMessage: (message: unknown) => void
      addEventListener: (type: 'message', listener: (event: { data: any }) => void) => void
    }
  }
}

function host() {
  return (window as HostWindow).chrome?.webview
}

export function isHosted() {
  return Boolean(host())
}

export function call<T = any>(method: string, params: Record<string, unknown> = {}): Promise<T> {
  const webview = host()
  if (!webview) {
    return Promise.reject(new Error('未运行在编辑器宿主内，无法调用后端。'))
  }

  const id = String(++sequence)

  return new Promise<T>((resolve, reject) => {
    pending.set(id, { resolve, reject })
    webview.postMessage({ id, method, params })
  })
}

host()?.addEventListener('message', (event) => {
  const data = event.data
  if (!data || data.id === undefined) return

  const entry = pending.get(String(data.id))
  if (!entry) return

  pending.delete(String(data.id))
  if (data.ok) {
    entry.resolve(data.result)
  } else {
    entry.reject(new Error(data.error ?? '未知错误'))
  }
})
