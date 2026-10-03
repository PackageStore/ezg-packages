const AGENT_PATH = "/agent"
const PEER_PROTOCOL = 1
const FILE_WINDOW_MS = 300
const PROGRESS_MAX_MS = 60000

export class BridgeUnavailable extends Error {
  constructor(message) {
    super(message)
    this.name = "BridgeUnavailable"
  }
}

export async function connect({ port, fileKey, timeoutMs = 5000 } = {}) {
  const resolvedPort = Number(port ?? process.env.EZG_FIGMA_BRIDGE_PORT ?? 39410)
  const ws = new WebSocket(`ws://127.0.0.1:${resolvedPort}${AGENT_PATH}`)
  const files = new Map()
  const pending = new Map()
  let connectionId = null
  let counter = 0
  let dead = null

  const failAll = (error) => {
    dead ??= error
    for (const [id, p] of pending) {
      clearTimeout(p.timer)
      pending.delete(id)
      p.reject(error)
    }
  }

  const arm = (id, p, delayMs) => {
    clearTimeout(p.timer)
    p.timer = setTimeout(() => {
      pending.delete(id)
      p.reject(new Error("Figma call timed out"))
    }, delayMs)
  }

  const onPluginFrame = (msg) => {
    const f = msg.frame
    if (!f) return
    if (f.kind === "hello") {
      if (f.file) files.set(msg.connectionId, f.file)
      return
    }
    if (msg.connectionId !== connectionId) return
    const p = pending.get(f.id)
    if (!p) return
    if (f.kind === "reply") {
      clearTimeout(p.timer)
      pending.delete(f.id)
      if (f.ok) p.resolve(f.result)
      else {
        const err = new Error(f.error?.message ?? "remote error")
        err.data = f.error?.data
        err.code = f.error?.code
        p.reject(err)
      }
    } else if (f.kind === "progress") {
      const extend = Number.isFinite(f.extendMs) ? f.extendMs : 0
      const next = Date.now() + Math.min(Math.max(extend, 0), PROGRESS_MAX_MS)
      if (next > p.deadline) {
        p.deadline = next
        arm(f.id, p, next - Date.now())
      }
    }
  }

  let handshake = null
  const welcomed = new Promise((resolve, reject) => {
    handshake = { resolve, reject }
  })
  let welcomeSeen = false

  ws.addEventListener("open", () =>
    ws.send(JSON.stringify({ kind: "agent-hello", protocol: PEER_PROTOCOL }))
  )
  ws.addEventListener("message", (ev) => {
    let msg
    try {
      msg = JSON.parse(typeof ev.data === "string" ? ev.data : String(ev.data))
    } catch {
      return
    }
    if (!msg || typeof msg !== "object") return
    switch (msg.kind) {
      case "agent-welcome":
        welcomeSeen = true
        handshake.resolve()
        break
      case "plugin":
        onPluginFrame(msg)
        break
      case "closed":
        files.delete(msg.connectionId)
        if (msg.connectionId === connectionId)
          failAll(new Error("Figma file disconnected"))
        break
      case "bye": {
        const error = new BridgeUnavailable(`bridge hub closed the link: ${msg.reason ?? "bye"}`)
        if (!welcomeSeen) handshake.reject(error)
        failAll(error)
        break
      }
    }
  })
  ws.addEventListener("error", () => {
    if (!welcomeSeen)
      handshake.reject(new BridgeUnavailable(`cannot reach bridge on 127.0.0.1:${resolvedPort}`))
  })
  ws.addEventListener("close", () => {
    const error = new BridgeUnavailable("bridge connection closed")
    if (!welcomeSeen) handshake.reject(error)
    failAll(error)
  })

  const abort = (error) => {
    try {
      ws.close()
    } catch {}
    return error
  }

  let timer
  try {
    await Promise.race([
      welcomed,
      new Promise((_, reject) => {
        timer = setTimeout(
          () => reject(new BridgeUnavailable(`bridge on 127.0.0.1:${resolvedPort} did not answer within ${timeoutMs} ms`)),
          timeoutMs
        )
      }),
    ])
  } catch (e) {
    clearTimeout(timer)
    throw abort(e)
  }
  clearTimeout(timer)

  await new Promise((r) => setTimeout(r, FILE_WINDOW_MS))
  if (dead) throw abort(dead)

  const list = [...files.entries()]
  const describe = () =>
    list.length
      ? list.map(([, f]) => `${f.name ?? "?"} (${f.fileKey ?? f.clientId ?? "no key"})`).join(", ")
      : "no file connected"
  let picked
  if (fileKey != null) {
    picked = list.find(([, f]) => f.fileKey === fileKey || f.clientId === fileKey)
    if (!picked) throw abort(new BridgeUnavailable(`no file matches ${fileKey}: ${describe()}`))
  } else if (list.length === 1) {
    picked = list[0]
  } else {
    throw abort(
      new BridgeUnavailable(
        list.length ? `fileKey required, ${list.length} files connected: ${describe()}` : "no file connected to the bridge"
      )
    )
  }
  connectionId = picked[0]

  return {
    file: picked[1],
    call(op, payload, callTimeoutMs = 120000) {
      if (dead) return Promise.reject(dead)
      return new Promise((resolve, reject) => {
        const id = String(++counter)
        const p = { resolve, reject, timer: null, deadline: Date.now() + callTimeoutMs }
        pending.set(id, p)
        arm(id, p, callTimeoutMs)
        try {
          ws.send(JSON.stringify({ kind: "send", connectionId, frame: { kind: "request", id, op, payload } }))
        } catch (e) {
          clearTimeout(p.timer)
          pending.delete(id)
          reject(new BridgeUnavailable(e instanceof Error ? e.message : "send failed"))
        }
      })
    },
    close() {
      failAll(new BridgeUnavailable("client closed"))
      try {
        ws.close()
      } catch {}
    },
  }
}
