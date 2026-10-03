import { test } from "node:test"
import assert from "node:assert/strict"
import http from "node:http"
import crypto from "node:crypto"
import { connect, BridgeUnavailable } from "../scripts/bridge-client.mjs"

const GUID = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"

function encode(text) {
  const body = Buffer.from(text)
  let head
  if (body.length < 126) head = Buffer.from([0x81, body.length])
  else if (body.length < 65536) head = Buffer.from([0x81, 126, body.length >> 8, body.length & 255])
  else {
    head = Buffer.alloc(10)
    head[0] = 0x81
    head[1] = 127
    head.writeBigUInt64BE(BigInt(body.length), 2)
  }
  return Buffer.concat([head, body])
}

function decodeFrames(state, chunk) {
  state.buf = Buffer.concat([state.buf, chunk])
  const out = []
  for (;;) {
    const b = state.buf
    if (b.length < 2) break
    const op = b[0] & 15
    let len = b[1] & 127
    let off = 2
    if (len === 126) {
      if (b.length < 4) break
      len = b.readUInt16BE(2)
      off = 4
    } else if (len === 127) {
      if (b.length < 10) break
      len = Number(b.readBigUInt64BE(2))
      off = 10
    }
    if (b.length < off + 4 + len) break
    const mask = b.subarray(off, off + 4)
    const data = Buffer.from(b.subarray(off + 4, off + 4 + len))
    for (let i = 0; i < data.length; i++) data[i] ^= mask[i % 4]
    state.buf = b.subarray(off + 4 + len)
    out.push({ op, text: data.toString() })
  }
  return out
}

async function fakeHub({ files = [], welcome = true, onRequest } = {}) {
  const hub = { sockets: new Set(), requests: [] }
  const server = http.createServer()
  server.on("upgrade", (req, socket) => {
    const key = req.headers["sec-websocket-key"]
    hub.origin = req.headers.origin
    hub.path = req.url
    const accept = crypto.createHash("sha1").update(key + GUID).digest("base64")
    socket.write(
      `HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: ${accept}\r\n\r\n`
    )
    hub.sockets.add(socket)
    const state = { buf: Buffer.alloc(0) }
    const send = (obj) => socket.write(encode(JSON.stringify(obj)))
    hub.send = send
    socket.on("error", () => {})
    socket.on("close", () => hub.sockets.delete(socket))
    socket.on("data", (chunk) => {
      for (const f of decodeFrames(state, chunk)) {
        if (f.op === 8) return socket.end()
        if (f.op !== 1) continue
        const msg = JSON.parse(f.text)
        if (msg.kind === "agent-hello") {
          hub.hello = msg
          if (!welcome) return
          send({ kind: "agent-welcome", hubId: "h" })
          files.forEach((file, i) =>
            send({ kind: "plugin", connectionId: `c${i}`, port: 1, frame: { kind: "hello", protocol: 1, file } })
          )
        } else if (msg.kind === "send") {
          hub.requests.push(msg)
          onRequest?.(msg, send)
        }
      }
    })
  })
  await new Promise((r) => server.listen(0, "127.0.0.1", r))
  hub.port = server.address().port
  hub.close = () =>
    new Promise((r) => {
      for (const s of hub.sockets) s.destroy()
      server.close(r)
    })
  return hub
}

const fileA = { name: "A", fileKey: "keyA", clientId: "ca" }
const fileB = { name: "B", fileKey: "keyB", clientId: "cb" }
const reply = (msg, extra) => ({
  kind: "plugin",
  connectionId: msg.connectionId,
  frame: { kind: "reply", id: msg.frame.id, ...extra },
})

test("handshake sends agent-hello protocol 1 on /agent", async () => {
  const hub = await fakeHub({ files: [fileA] })
  const c = await connect({ port: hub.port })
  assert.deepEqual(hub.hello, { kind: "agent-hello", protocol: 1 })
  assert.equal(hub.path, "/agent")
  c.close()
  await hub.close()
})

test("picks the file by key", async () => {
  const hub = await fakeHub({ files: [fileA, fileB] })
  const c = await connect({ port: hub.port, fileKey: "keyB" })
  assert.equal(c.file.name, "B")
  c.close()
  await hub.close()
})

test("matches clientId when the file has no key", async () => {
  const draft = { name: "D", fileKey: null, clientId: "draft-1" }
  const hub = await fakeHub({ files: [draft, fileA] })
  const c = await connect({ port: hub.port, fileKey: "draft-1" })
  assert.equal(c.file.name, "D")
  c.close()
  await hub.close()
})

test("single file is the default", async () => {
  const hub = await fakeHub({ files: [fileA] })
  const c = await connect({ port: hub.port })
  assert.equal(c.file.fileKey, "keyA")
  c.close()
  await hub.close()
})

test("several files without a key reject and list them", async () => {
  const hub = await fakeHub({ files: [fileA, fileB] })
  await assert.rejects(connect({ port: hub.port }), (e) => e instanceof BridgeUnavailable && /keyA/.test(e.message) && /keyB/.test(e.message))
  await hub.close()
})

test("unknown key rejects with BridgeUnavailable", async () => {
  const hub = await fakeHub({ files: [fileA] })
  await assert.rejects(connect({ port: hub.port, fileKey: "nope" }), BridgeUnavailable)
  await hub.close()
})

test("no file connected rejects with no file message", async () => {
  const hub = await fakeHub({ files: [] })
  await assert.rejects(connect({ port: hub.port }), (e) => e instanceof BridgeUnavailable && /no file/.test(e.message))
  await hub.close()
})

test("call resolves the reply result and sends a request frame", async () => {
  const hub = await fakeHub({
    files: [fileA],
    onRequest: (m, send) => send(reply(m, { ok: true, result: { n: 1 } })),
  })
  const c = await connect({ port: hub.port })
  const res = await c.call("lint", { rules: ["x"] })
  assert.deepEqual(res, { n: 1 })
  assert.deepEqual(hub.requests[0], {
    kind: "send",
    connectionId: "c0",
    frame: { kind: "request", id: "1", op: "lint", payload: { rules: ["x"] } },
  })
  c.close()
  await hub.close()
})

test("large reply is not capped", async () => {
  const big = "x".repeat(300000)
  const hub = await fakeHub({ files: [fileA], onRequest: (m, send) => send(reply(m, { ok: true, result: big })) })
  const c = await connect({ port: hub.port })
  assert.equal((await c.call("eval", {})).length, big.length)
  c.close()
  await hub.close()
})

test("error reply rejects with message and data", async () => {
  const hub = await fakeHub({
    files: [fileA],
    onRequest: (m, send) => send(reply(m, { ok: false, error: { message: "boom", data: { line: 3 } } })),
  })
  const c = await connect({ port: hub.port })
  await assert.rejects(c.call("eval", {}), (e) => e.message === "boom" && e.data.line === 3)
  c.close()
  await hub.close()
})

test("progress extends the deadline", async () => {
  const hub = await fakeHub({
    files: [fileA],
    onRequest: (m, send) => {
      setTimeout(
        () => send({ kind: "plugin", connectionId: m.connectionId, frame: { kind: "progress", id: m.frame.id, extendMs: 1000 } }),
        100
      )
      setTimeout(() => send(reply(m, { ok: true, result: "late" })), 500)
    },
  })
  const c = await connect({ port: hub.port })
  assert.equal(await c.call("eval", {}, 300), "late")
  c.close()
  await hub.close()
})

test("call times out without a reply", async () => {
  const hub = await fakeHub({ files: [fileA] })
  const c = await connect({ port: hub.port })
  await assert.rejects(c.call("eval", {}, 150), /timed out/)
  c.close()
  await hub.close()
})

test("bye rejects pending calls", async () => {
  const hub = await fakeHub({ files: [fileA], onRequest: (m, send) => setTimeout(() => send({ kind: "bye", reason: "shutdown" }), 50) })
  const c = await connect({ port: hub.port })
  await assert.rejects(c.call("eval", {}, 5000), BridgeUnavailable)
  await assert.rejects(c.call("eval", {}, 5000), BridgeUnavailable)
  c.close()
  await hub.close()
})

test("closed for the picked connection rejects pending calls", async () => {
  const hub = await fakeHub({
    files: [fileA],
    onRequest: (m, send) => setTimeout(() => send({ kind: "closed", connectionId: m.connectionId }), 50),
  })
  const c = await connect({ port: hub.port })
  await assert.rejects(c.call("eval", {}, 5000), /disconnected/)
  c.close()
  await hub.close()
})

test("closed for another connection leaves calls pending", async () => {
  const hub = await fakeHub({
    files: [fileA, fileB],
    onRequest: (m, send) => {
      send({ kind: "closed", connectionId: "c1" })
      setTimeout(() => send(reply(m, { ok: true, result: "ok" })), 50)
    },
  })
  const c = await connect({ port: hub.port, fileKey: "keyA" })
  assert.equal(await c.call("eval", {}, 2000), "ok")
  c.close()
  await hub.close()
})

test("closed port rejects with BridgeUnavailable within timeoutMs", async () => {
  const probe = http.createServer()
  await new Promise((r) => probe.listen(0, "127.0.0.1", r))
  const { port } = probe.address()
  await new Promise((r) => probe.close(r))
  const t0 = Date.now()
  await assert.rejects(connect({ port, timeoutMs: 1000 }), BridgeUnavailable)
  assert.ok(Date.now() - t0 < 1500)
})

test("hub that never welcomes rejects within timeoutMs", async () => {
  const hub = await fakeHub({ welcome: false })
  const t0 = Date.now()
  await assert.rejects(connect({ port: hub.port, timeoutMs: 400 }), BridgeUnavailable)
  assert.ok(Date.now() - t0 < 1000)
  await hub.close()
})
