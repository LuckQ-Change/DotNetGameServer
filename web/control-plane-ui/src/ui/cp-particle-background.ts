import { LitElement, css, html } from 'lit'

type Particle = {
  x: number
  y: number
  vx: number
  vy: number
  r: number
  hue: number
}

export class CpParticleBackground extends LitElement {
  static styles = css`
    :host {
      position: fixed;
      inset: 0;
      z-index: 0;
      pointer-events: none;
    }

    canvas {
      width: 100%;
      height: 100%;
      display: block;
    }
  `

  private canvas?: HTMLCanvasElement
  private ctx?: CanvasRenderingContext2D
  private dpr = 1
  private raf = 0
  private particles: Particle[] = []
  private pointer = { x: -9999, y: -9999, active: false }

  render() {
    return html`<canvas></canvas>`
  }

  protected firstUpdated() {
    const canvas = this.renderRoot.querySelector('canvas')
    if (!(canvas instanceof HTMLCanvasElement)) return
    const ctx = canvas.getContext('2d')
    if (!ctx) return

    this.canvas = canvas
    this.ctx = ctx

    this.dpr = Math.max(1, Math.min(2, window.devicePixelRatio || 1))
    this.resize()
    this.seed()

    window.addEventListener('resize', this.onResize, { passive: true })
    window.addEventListener('pointermove', this.onPointerMove, { passive: true })
    window.addEventListener('pointerdown', this.onPointerDown, { passive: true })
    window.addEventListener('pointerleave', this.onPointerLeave, { passive: true })

    this.raf = window.requestAnimationFrame(this.tick)
  }

  disconnectedCallback() {
    super.disconnectedCallback()
    window.removeEventListener('resize', this.onResize)
    window.removeEventListener('pointermove', this.onPointerMove)
    window.removeEventListener('pointerdown', this.onPointerDown)
    window.removeEventListener('pointerleave', this.onPointerLeave)
    if (this.raf) window.cancelAnimationFrame(this.raf)
  }

  private onResize = () => this.resize()

  private onPointerMove = (ev: PointerEvent) => {
    this.pointer.x = ev.clientX
    this.pointer.y = ev.clientY
    this.pointer.active = true
  }

  private onPointerDown = (ev: PointerEvent) => {
    this.pointer.x = ev.clientX
    this.pointer.y = ev.clientY
    this.pointer.active = true
    this.burst(ev.clientX, ev.clientY)
  }

  private onPointerLeave = () => {
    this.pointer.active = false
    this.pointer.x = -9999
    this.pointer.y = -9999
  }

  private resize() {
    if (!this.canvas || !this.ctx) return
    const w = Math.max(1, window.innerWidth)
    const h = Math.max(1, window.innerHeight)
    this.canvas.width = Math.floor(w * this.dpr)
    this.canvas.height = Math.floor(h * this.dpr)
    this.ctx.setTransform(this.dpr, 0, 0, this.dpr, 0, 0)

    const targetCount = Math.floor(Math.min(140, Math.max(70, (w * h) / 16000)))
    if (this.particles.length > targetCount) this.particles.length = targetCount
    if (this.particles.length < targetCount) this.addParticles(targetCount - this.particles.length)
  }

  private seed() {
    this.particles = []
    const w = Math.max(1, window.innerWidth)
    const h = Math.max(1, window.innerHeight)
    const count = Math.floor(Math.min(140, Math.max(70, (w * h) / 16000)))
    for (let i = 0; i < count; i++) this.particles.push(this.newParticle(w, h))
  }

  private addParticles(n: number) {
    const w = Math.max(1, window.innerWidth)
    const h = Math.max(1, window.innerHeight)
    for (let i = 0; i < n; i++) this.particles.push(this.newParticle(w, h))
  }

  private newParticle(w: number, h: number): Particle {
    const speed = 0.22 + Math.random() * 0.55
    const angle = Math.random() * Math.PI * 2
    return {
      x: Math.random() * w,
      y: Math.random() * h,
      vx: Math.cos(angle) * speed,
      vy: Math.sin(angle) * speed,
      r: 1.2 + Math.random() * 1.9,
      hue: Math.random() < 0.55 ? 185 + Math.random() * 25 : 255 + Math.random() * 25,
    }
  }

  private burst(x: number, y: number) {
    const w = Math.max(1, window.innerWidth)
    const h = Math.max(1, window.innerHeight)
    const extra = 10
    for (let i = 0; i < extra; i++) {
      const p = this.newParticle(w, h)
      p.x = x + (Math.random() - 0.5) * 20
      p.y = y + (Math.random() - 0.5) * 20
      const angle = Math.random() * Math.PI * 2
      const speed = 0.9 + Math.random() * 1.4
      p.vx = Math.cos(angle) * speed
      p.vy = Math.sin(angle) * speed
      p.r = 1.1 + Math.random() * 2.6
      this.particles.push(p)
    }
    const max = Math.floor(Math.min(180, Math.max(90, (w * h) / 14000)))
    if (this.particles.length > max) this.particles.splice(0, this.particles.length - max)
  }

  private tick = () => {
    this.raf = window.requestAnimationFrame(this.tick)
    this.draw()
  }

  private draw() {
    const ctx = this.ctx
    if (!ctx) return

    const w = Math.max(1, window.innerWidth)
    const h = Math.max(1, window.innerHeight)

    ctx.clearRect(0, 0, w, h)

    const px = this.pointer.x
    const py = this.pointer.y
    const influence = this.pointer.active ? 140 : 0
    const influence2 = influence * influence

    for (const p of this.particles) {
      p.x += p.vx
      p.y += p.vy

      if (p.x < -20) p.x = w + 20
      if (p.x > w + 20) p.x = -20
      if (p.y < -20) p.y = h + 20
      if (p.y > h + 20) p.y = -20

      if (influence > 0) {
        const dx = p.x - px
        const dy = p.y - py
        const d2 = dx * dx + dy * dy
        if (d2 < influence2) {
          const d = Math.max(1, Math.sqrt(d2))
          const pull = (1 - d / influence) * 0.045
          p.vx += (-dx / d) * pull
          p.vy += (-dy / d) * pull
        }
      }

      const sp = Math.sqrt(p.vx * p.vx + p.vy * p.vy)
      const lim = 1.15
      if (sp > lim) {
        p.vx = (p.vx / sp) * lim
        p.vy = (p.vy / sp) * lim
      }
    }

    for (let i = 0; i < this.particles.length; i++) {
      const a = this.particles[i]
      for (let j = i + 1; j < this.particles.length; j++) {
        const b = this.particles[j]
        const dx = a.x - b.x
        const dy = a.y - b.y
        const d2 = dx * dx + dy * dy
        if (d2 > 160 * 160) continue
        const d = Math.sqrt(d2)
        const alpha = Math.max(0, 0.16 - d / 1000)
        if (alpha <= 0) continue
        const hue = (a.hue + b.hue) * 0.5
        ctx.strokeStyle = `hsla(${hue}, 95%, 65%, ${alpha})`
        ctx.lineWidth = 1
        ctx.beginPath()
        ctx.moveTo(a.x, a.y)
        ctx.lineTo(b.x, b.y)
        ctx.stroke()
      }
    }

    for (const p of this.particles) {
      ctx.fillStyle = `hsla(${p.hue}, 95%, 70%, 0.70)`
      ctx.beginPath()
      ctx.arc(p.x, p.y, p.r, 0, Math.PI * 2)
      ctx.fill()
    }

    const glow = ctx.createRadialGradient(px, py, 0, px, py, 220)
    glow.addColorStop(0, 'rgba(0,229,255,0.09)')
    glow.addColorStop(1, 'rgba(0,229,255,0)')
    ctx.fillStyle = glow
    ctx.fillRect(0, 0, w, h)
  }
}

customElements.define('cp-particle-background', CpParticleBackground)

declare global {
  interface HTMLElementTagNameMap {
    'cp-particle-background': CpParticleBackground
  }
}
