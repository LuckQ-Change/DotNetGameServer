import { LitElement, css, html } from 'lit'
import './cp-particle-background'
import {
  controlPlaneApi,
  type GmCommandItem,
  type GmCommandsResponse,
  type RoutingQueryResponse,
  type ServersResponse,
} from '../api/controlPlane'

type NavKey = 'overview' | 'servers' | 'routing' | 'gm'

export class CpApp extends LitElement {
  static styles = css`
    :host {
      display: block;
      min-height: 100vh;
      color: rgba(231, 240, 255, 0.92);
      font-family: ui-sans-serif, system-ui, -apple-system, Segoe UI, Roboto, Helvetica, Arial,
        'Apple Color Emoji', 'Segoe UI Emoji';
      background: radial-gradient(1200px 800px at 30% 20%, rgba(0, 229, 255, 0.10), transparent 55%),
        radial-gradient(900px 700px at 80% 70%, rgba(124, 77, 255, 0.12), transparent 55%),
        #050814;
      overflow: hidden;
    }

    .frame {
      position: relative;
      min-height: 100vh;
    }

    .topbar {
      position: sticky;
      top: 0;
      z-index: 5;
      height: 56px;
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 0 18px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.06);
      background: linear-gradient(
        to bottom,
        rgba(8, 12, 26, 0.90),
        rgba(8, 12, 26, 0.65)
      );
      backdrop-filter: blur(10px);
    }

    .brand {
      display: flex;
      align-items: baseline;
      gap: 10px;
      letter-spacing: 0.5px;
    }

    .brand .title {
      font-weight: 700;
      font-size: 15px;
    }

    .brand .sub {
      font-size: 12px;
      color: rgba(170, 200, 255, 0.7);
    }

    .grid {
      position: relative;
      display: grid;
      grid-template-columns: 260px 1fr;
      min-height: calc(100vh - 56px);
    }

    .sidebar {
      position: sticky;
      top: 56px;
      height: calc(100vh - 56px);
      padding: 14px;
      border-right: 1px solid rgba(255, 255, 255, 0.06);
      background: linear-gradient(
        to bottom,
        rgba(8, 12, 26, 0.70),
        rgba(8, 12, 26, 0.35)
      );
      backdrop-filter: blur(10px);
    }

    .nav {
      display: flex;
      flex-direction: column;
      gap: 8px;
      margin-top: 10px;
    }

    .nav button {
      all: unset;
      cursor: pointer;
      user-select: none;
      border-radius: 12px;
      padding: 10px 12px;
      border: 1px solid rgba(255, 255, 255, 0.06);
      background: rgba(255, 255, 255, 0.03);
      transition: transform 160ms ease, border-color 160ms ease, box-shadow 160ms ease,
        background 160ms ease;
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .nav button:hover {
      transform: translateY(-1px);
      border-color: rgba(0, 229, 255, 0.28);
      box-shadow: 0 0 0 1px rgba(0, 229, 255, 0.12), 0 16px 40px rgba(0, 0, 0, 0.35);
      background: rgba(0, 229, 255, 0.05);
    }

    .nav button[data-active='true'] {
      border-color: rgba(124, 77, 255, 0.38);
      box-shadow: 0 0 0 1px rgba(124, 77, 255, 0.18), 0 18px 50px rgba(0, 0, 0, 0.42);
      background: linear-gradient(135deg, rgba(124, 77, 255, 0.18), rgba(0, 229, 255, 0.10));
    }

    .nav .label {
      display: flex;
      flex-direction: column;
      gap: 2px;
    }

    .nav .label b {
      font-size: 13px;
      font-weight: 650;
    }

    .nav .label span {
      font-size: 11px;
      color: rgba(170, 200, 255, 0.66);
    }

    .nav .chip {
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono',
        'Courier New', monospace;
      font-size: 11px;
      padding: 2px 8px;
      border-radius: 999px;
      border: 1px solid rgba(255, 255, 255, 0.08);
      color: rgba(210, 230, 255, 0.82);
      background: rgba(255, 255, 255, 0.03);
    }

    .content {
      position: relative;
      padding: 18px;
    }

    .panel {
      border-radius: 18px;
      border: 1px solid rgba(255, 255, 255, 0.08);
      background: linear-gradient(
        to bottom right,
        rgba(255, 255, 255, 0.06),
        rgba(255, 255, 255, 0.03)
      );
      backdrop-filter: blur(14px);
      box-shadow: 0 24px 80px rgba(0, 0, 0, 0.45);
      overflow: hidden;
    }

    .panelHeader {
      padding: 16px 16px 12px;
      border-bottom: 1px solid rgba(255, 255, 255, 0.06);
      display: flex;
      align-items: baseline;
      justify-content: space-between;
      gap: 12px;
    }

    .panelHeader h1 {
      margin: 0;
      font-size: 16px;
      letter-spacing: 0.3px;
    }

    .panelHeader p {
      margin: 0;
      font-size: 12px;
      color: rgba(170, 200, 255, 0.7);
    }

    .statusLine {
      min-height: 16px;
    }

    .panelBody {
      padding: 16px;
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 12px;
    }

    .row {
      display: flex;
      gap: 10px;
      align-items: center;
      justify-content: space-between;
      flex-wrap: wrap;
    }

    .input {
      all: unset;
      display: inline-flex;
      align-items: center;
      padding: 10px 12px;
      border-radius: 12px;
      border: 1px solid rgba(255, 255, 255, 0.10);
      background: rgba(255, 255, 255, 0.04);
      color: rgba(231, 240, 255, 0.92);
      min-width: 260px;
    }

    .input::placeholder {
      color: rgba(170, 200, 255, 0.55);
    }

    .btn {
      all: unset;
      cursor: pointer;
      user-select: none;
      border-radius: 12px;
      padding: 10px 12px;
      border: 1px solid rgba(255, 255, 255, 0.10);
      background: rgba(255, 255, 255, 0.04);
      transition: transform 160ms ease, border-color 160ms ease, box-shadow 160ms ease,
        background 160ms ease;
    }

    .btn:hover {
      transform: translateY(-1px);
      border-color: rgba(0, 229, 255, 0.28);
      box-shadow: 0 0 0 1px rgba(0, 229, 255, 0.12), 0 16px 40px rgba(0, 0, 0, 0.35);
      background: rgba(0, 229, 255, 0.05);
    }

    .mono {
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono',
        'Courier New', monospace;
    }

    pre.out {
      margin: 0;
      padding: 12px;
      border-radius: 14px;
      border: 1px solid rgba(255, 255, 255, 0.08);
      background: rgba(0, 0, 0, 0.25);
      overflow: auto;
      max-height: 360px;
      white-space: pre-wrap;
      word-break: break-word;
    }

    .card {
      border-radius: 16px;
      border: 1px solid rgba(255, 255, 255, 0.06);
      background: rgba(3, 6, 18, 0.35);
      padding: 14px;
      transition: transform 160ms ease, border-color 160ms ease, box-shadow 160ms ease;
    }

    .card:hover {
      transform: translateY(-2px);
      border-color: rgba(0, 229, 255, 0.25);
      box-shadow: 0 0 0 1px rgba(0, 229, 255, 0.12), 0 20px 60px rgba(0, 0, 0, 0.4);
    }

    .metric {
      display: flex;
      flex-direction: column;
      gap: 8px;
    }

    .metric .k {
      font-size: 11px;
      color: rgba(170, 200, 255, 0.72);
    }

    .metric .v {
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, 'Liberation Mono',
        'Courier New', monospace;
      font-size: 22px;
      letter-spacing: 0.4px;
    }

    .metric .hint {
      font-size: 11px;
      color: rgba(170, 200, 255, 0.6);
    }

    @media (max-width: 980px) {
      .grid {
        grid-template-columns: 1fr;
      }

      .sidebar {
        position: relative;
        top: 0;
        height: auto;
      }

      .panelBody {
        grid-template-columns: 1fr;
      }
    }
  `

  static properties = {
    nav: { state: true },
    serversRes: { state: true },
    routingRes: { state: true },
    gmRes: { state: true },
    gmSearch: { state: true },
    gmCmdText: { state: true },
    gmExecLoading: { state: true },
    gmExecOut: { state: true },
    error: { state: true },
    lastUpdated: { state: true },
    loading: { state: true },
  }

  declare nav: NavKey
  declare serversRes?: ServersResponse
  declare routingRes?: RoutingQueryResponse
  declare gmRes?: GmCommandsResponse
  declare gmSearch: string
  declare gmCmdText: string
  declare gmExecLoading: boolean
  declare gmExecOut?: string
  declare error?: string
  declare lastUpdated?: string
  declare loading: boolean
  private refreshTimer = 0

  constructor() {
    super()
    this.nav = 'overview'
    this.loading = false
    this.gmSearch = ''
    this.gmCmdText = ''
    this.gmExecLoading = false
  }

  private setNav(next: NavKey) {
    this.nav = next
    if (next === 'gm') this.loadGmCommands()
  }

  connectedCallback(): void {
    super.connectedCallback()
    this.refresh()
    this.refreshTimer = window.setInterval(() => this.refresh(), 3000)
  }

  disconnectedCallback(): void {
    super.disconnectedCallback()
    if (this.refreshTimer) window.clearInterval(this.refreshTimer)
  }

  private async refresh() {
    this.loading = true
    try {
      const [servers, routing] = await Promise.all([
        controlPlaneApi.servers(),
        controlPlaneApi.routingQuery({ strategy: 'load', limit: 12 }),
      ])
      this.serversRes = servers
      this.routingRes = routing
      this.error = undefined
      this.lastUpdated = new Date().toLocaleTimeString()
    } catch (e) {
      this.error = e instanceof Error ? e.message : String(e)
    } finally {
      this.loading = false
    }
  }

  private async loadGmCommands() {
    if (this.gmRes?.ok && this.gmRes.result?.items?.length) return
    try {
      this.gmRes = await controlPlaneApi.gmCommands()
    } catch (e) {
      this.gmRes = { ok: false, error: e instanceof Error ? e.message : String(e) }
    }
  }

  private setGmCmdText(v: string) {
    this.gmCmdText = v
  }

  private async executeGm() {
    const cmd = this.gmCmdText.trim()
    if (!cmd) return
    this.gmExecLoading = true
    try {
      const res = await controlPlaneApi.gmExecute(cmd)
      this.gmExecOut = JSON.stringify(res, null, 2)
    } catch (e) {
      this.gmExecOut = JSON.stringify(
        { ok: false, error: e instanceof Error ? e.message : String(e) },
        null,
        2,
      )
    } finally {
      this.gmExecLoading = false
    }
  }

  private renderNavButton(key: NavKey, title: string, sub: string, chip: string) {
    return html`
      <button data-active=${this.nav === key} @click=${() => this.setNav(key)}>
        <div class="label">
          <b>${title}</b>
          <span>${sub}</span>
        </div>
        <div class="chip">${chip}</div>
      </button>
    `
  }

  private headerText() {
    if (this.nav === 'overview') return { h: '总览', p: '服务健康与选服候选' }
    if (this.nav === 'servers') return { h: '实例', p: '服务健康明细' }
    if (this.nav === 'routing') return { h: '分流', p: '目录服务选服结果（load 策略）' }
    return { h: 'GM', p: '指令列表、分组与检索' }
  }

  private renderOverview() {
    const servers = this.serversRes
    const routing = this.routingRes
    const total = servers?.total ?? 0
    const healthy = servers?.healthy ?? 0
    const unhealthy = Math.max(0, total - healthy)
    const logicCount = routing?.servers?.length ?? 0

    return html`
      <div class="panelBody">
        <div class="card">
          <div class="metric">
            <div class="k">服务数量</div>
            <div class="v">${total || '—'}</div>
            <div class="hint">基于配置 + 健康检查聚合</div>
          </div>
        </div>
        <div class="card">
          <div class="metric">
            <div class="k">健康实例</div>
            <div class="v">${servers ? healthy : '—'}</div>
            <div class="hint">异常实例：${servers ? unhealthy : '—'}</div>
          </div>
        </div>
        <div class="card">
          <div class="metric">
            <div class="k">逻辑服候选</div>
            <div class="v">${routing ? logicCount : '—'}</div>
            <div class="hint">Directory：query_servers(load)</div>
          </div>
        </div>
      </div>
    `
  }

  private renderServers() {
    const items = this.serversRes?.items ?? []
    if (items.length === 0) return html`<div class="panelBody"><div class="card">暂无数据</div></div>`

    const routing = this.routingRes?.servers ?? []
    return html`
      <div class="panelBody" style="grid-template-columns: 1fr;">
        <div class="card">
          <div class="mono">服务健康</div>
        </div>
        <div class="panelBody" style="padding: 0; grid-template-columns: repeat(2, minmax(0, 1fr));">
          ${items.map(
            (s) => html`
              <div class="card">
                <div class="metric">
                  <div class="k">${s.id} • ${s.type} • ${s.region}</div>
                  <div class="v">${s.health.ok ? '正常' : '异常'}</div>
                  <div class="hint">
                    ${s.host}:${s.tcpPort} | 健康端口:${s.healthPort} | ${s.health.latencyMs}ms
                    ${s.health.error ? ` | ${s.health.error}` : ''}
                  </div>
                </div>
              </div>
            `,
          )}
        </div>
        <div class="card">
          <div class="mono">目录服务返回的逻辑服（load）</div>
        </div>
        <div class="panelBody" style="padding: 0; grid-template-columns: repeat(2, minmax(0, 1fr));">
          ${routing.length === 0
            ? html`<div class="card">暂无数据</div>`
            : routing.map(
                (s) => html`
                  <div class="card">
                    <div class="metric">
                      <div class="k">${s.id} • ${s.region}</div>
                      <div class="v">${s.playerCount}/${s.capacity}</div>
                      <div class="hint">${s.host}:${s.port} | 状态=${s.status} | RTT=${s.avgRttMs}ms</div>
                    </div>
                  </div>
                `,
              )}
        </div>
      </div>
    `
  }

  private renderRouting() {
    const servers = this.routingRes?.servers ?? []
    if (servers.length === 0) return html`<div class="panelBody"><div class="card">暂无数据</div></div>`

    return html`
      <div class="panelBody" style="grid-template-columns: repeat(2, minmax(0, 1fr));">
        ${servers.map(
          (s) => html`
            <div class="card">
              <div class="metric">
                <div class="k">${s.id} • ${s.region}</div>
                <div class="v">${s.playerCount}/${s.capacity}</div>
                <div class="hint">
                  ${s.host}:${s.port} | 状态=${s.status} | RTT=${s.avgRttMs}ms
                </div>
              </div>
            </div>
          `,
        )}
      </div>
    `
  }

  private renderGm() {
    const items = this.gmRes?.result?.items ?? []
    const search = this.gmSearch.trim().toLowerCase()
    const filtered = search
      ? items.filter((c) => {
          if (c.name.toLowerCase().includes(search)) return true
          return c.aliases?.some((a) => a.toLowerCase().includes(search)) ?? false
        })
      : items

    const groups = new Map<string, GmCommandItem[]>()
    for (const c of filtered) {
      const k = (c.name.split(' ')[0] || c.name).toLowerCase()
      const arr = groups.get(k) ?? []
      arr.push(c)
      groups.set(k, arr)
    }

    const groupKeys = Array.from(groups.keys()).sort((a, b) => a.localeCompare(b))
    return html`
      <div class="panelBody" style="grid-template-columns: 1fr;">
        <div class="card">
          <div class="row">
            <input
              class="input mono"
              placeholder="按名称/别名搜索 GM 指令…"
              .value=${this.gmSearch}
              @input=${(e: Event) => (this.gmSearch = (e.target as HTMLInputElement).value)}
            />
            <input
              class="input mono"
              style="min-width: 420px;"
              placeholder="输入 GM 指令，例如：status / handlers / blacklist list"
              .value=${this.gmCmdText}
              @input=${(e: Event) => this.setGmCmdText((e.target as HTMLInputElement).value)}
              @keydown=${(e: KeyboardEvent) => {
                if (e.key === 'Enter' && !e.shiftKey) this.executeGm()
              }}
            />
            <button class="btn" @click=${() => this.executeGm()}>
              ${this.gmExecLoading ? '执行中…' : '执行'}
            </button>
            <button class="btn" @click=${() => (this.gmRes = undefined, this.loadGmCommands())}>刷新指令</button>
            <div class="mono">
              ${items.length ? `共 ${items.length} 条` : this.gmRes ? '暂无指令' : '加载中…'}
            </div>
          </div>
        </div>
        <div class="card">
          <div class="metric">
            <div class="k mono">执行结果</div>
            <pre class="out mono">${this.gmExecOut ?? '尚未执行'}</pre>
          </div>
        </div>
        ${this.gmRes?.ok === false
          ? html`<div class="card">加载失败：${this.gmRes.error ?? 'unknown'}</div>`
          : ''}
        ${groupKeys.map((g) => {
          const list = groups.get(g) ?? []
          return html`
            <div class="card">
              <div class="row">
                <div class="mono">${g}</div>
                <div class="mono">(${list.length})</div>
              </div>
              <div style="margin-top: 10px; display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 10px;">
                ${list
                  .sort((a, b) => a.name.localeCompare(b.name))
                  .map(
                    (c) => html`
                      <div class="card" style="padding: 12px;">
                        <div class="metric">
                          <div class="k mono">
                            <button class="btn mono" style="padding: 6px 10px;" @click=${() => this.setGmCmdText(c.name)}>
                              ${c.name}
                            </button>
                          </div>
                          <div class="hint mono">
                            ${c.aliases?.length ? `别名：${c.aliases.join(' / ')}` : '别名：—'}
                          </div>
                        </div>
                      </div>
                    `,
                  )}
              </div>
            </div>
          `
        })}
      </div>
    `
  }

  render() {
    const header = this.headerText()
    return html`
      <div class="frame">
        <cp-particle-background></cp-particle-background>
        <div class="topbar">
          <div class="brand">
            <div class="title">ControlPlane</div>
            <div class="sub">运维 • 可观测 • 分流</div>
          </div>
          <div class="sub">自动刷新 3s</div>
        </div>
        <div class="grid">
          <aside class="sidebar">
            <div class="sub">导航</div>
            <div class="nav">
              ${this.renderNavButton('overview', '总览', '服务概览', '概览')}
              ${this.renderNavButton('servers', '实例', '健康明细', '实例')}
              ${this.renderNavButton('routing', '分流', '选服结果', '分流')}
              ${this.renderNavButton('gm', 'GM', '指令查询', 'GM')}
            </div>
          </aside>
          <main class="content">
            <section class="panel">
              <header class="panelHeader">
                <div>
                  <h1>${header.h}</h1>
                  <p class="statusLine">${header.p}</p>
                </div>
                <p class="mono statusLine">
                  ${this.lastUpdated ? `更新 ${this.lastUpdated}` : '等待首次更新'}
                  ${this.error ? ` | 错误：${this.error}` : ''}
                </p>
              </header>
              ${this.nav === 'overview'
                ? this.renderOverview()
                : this.nav === 'servers'
                  ? this.renderServers()
                  : this.nav === 'routing'
                    ? this.renderRouting()
                    : this.renderGm()}
            </section>
          </main>
        </div>
      </div>
    `
  }
}

customElements.define('cp-app', CpApp)

declare global {
  interface HTMLElementTagNameMap {
    'cp-app': CpApp
  }
}
