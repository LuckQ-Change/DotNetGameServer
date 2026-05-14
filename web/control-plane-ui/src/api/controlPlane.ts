export type ServerHealth = {
  ok: boolean
  statusCode: number
  latencyMs: number
  error?: string | null
}

export type ServerStatus = {
  id: string
  type: string
  region: string
  host: string
  tcpPort: number
  healthPort: number
  health: ServerHealth
}

export type ServersResponse = {
  total: number
  healthy: number
  items: ServerStatus[]
}

export type DirectoryServerInfo = {
  id: string
  type: string
  region: string
  host: string
  port: number
  playerCount: number
  capacity: number
  status: string
  lastHeartbeat: number
  avgRttMs: number
}

export type RoutingQueryResponse = {
  count: number
  servers: DirectoryServerInfo[]
}

export type GmCommandItem = {
  name: string
  aliases: string[]
}

export type GmCommandsResponse = {
  ok: boolean
  result?: {
    count: number
    items: GmCommandItem[]
  }
  error?: string
  name?: string
}

async function getJson<T>(path: string): Promise<T> {
  const res = await fetch(path, { headers: { Accept: 'application/json' } })
  if (!res.ok) throw new Error(`http ${res.status}`)
  return (await res.json()) as T
}

async function postJson<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(path, {
    method: 'POST',
    headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!res.ok) throw new Error(`http ${res.status}`)
  return (await res.json()) as T
}

export const controlPlaneApi = {
  health: () => getJson<{ status: string }>('/api/health'),
  servers: () => getJson<ServersResponse>('/api/servers'),
  routingQuery: (q: { strategy?: string; region?: string; limit?: number }) => {
    const sp = new URLSearchParams()
    if (q.strategy) sp.set('strategy', q.strategy)
    if (q.region) sp.set('region', q.region)
    if (q.limit != null) sp.set('limit', String(q.limit))
    const qs = sp.toString()
    return getJson<RoutingQueryResponse>(`/api/routing/query${qs ? `?${qs}` : ''}`)
  },
  gmCommands: () => getJson<GmCommandsResponse>('/api/gm/commands'),
  gmExecute: (cmd: string) => postJson<unknown>('/api/gm/execute', { cmd }),
}
