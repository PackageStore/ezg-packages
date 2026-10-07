"""sfxsynth — DSP building blocks of the create-sfx skill (numpy + scipy, mono float64 at 44.1 kHz).

Every generator returns a 1-D float array; recipes layer them with `mix`, then make_sfx normalises loudness,
limits the peak and writes 16-bit WAV. Nothing here touches Unity. API summary: reference/synth-api.md.
"""
import wave

import numpy as np
from scipy.signal import butter, fftconvolve, lfilter, sosfilt

SR = 44100
CEILING_DB = -1.0           # final true-ish peak ceiling (Unity Vorbis re-encode overshoots a little)


# ============================================================================ time
def ns(d):
    """Samples in d seconds (at least 1)."""
    return max(1, int(round(d * SR)))


def tt(d):
    """Time axis of d seconds."""
    return np.arange(ns(d)) / SR


def fit(x, n):
    """Pad with zeros or cut to exactly n samples."""
    return x[:n] if len(x) >= n else np.pad(x, (0, n - len(x)))


def curve(n, values):
    """Broadcast a scalar or per-sample array to n samples."""
    if np.ndim(values) == 0:
        return np.full(n, float(values))
    return fit(np.asarray(values, float), n)


# ============================================================================ envelopes
def perc(d, a=0.002, k=5.0, hold=0.0):
    """Percussive envelope: linear attack a, optional hold, exponential decay reaching e^-k at the end."""
    n = ns(d)
    t = np.arange(n) / SR
    e = np.exp(-k * np.maximum(t - a - hold, 0) / max(d - a - hold, 1e-4))
    if a > 0:
        e *= np.minimum(t / a, 1.0)
    return e


def ar(d, a, r_shape=2.0):
    """Attack to 1 at fraction a of d, then a power-curve release to 0 (smooth swell / swoosh shapes)."""
    n = ns(d)
    u = np.linspace(0, 1, n)
    e = np.where(u < a, (u / max(a, 1e-6)) ** 1.5, ((1 - u) / max(1 - a, 1e-6)) ** r_shape)
    return np.clip(e, 0, 1)


def pts(d, points, log=False):
    """Breakpoint envelope: points = [(time_fraction, value), ...]; log=True interpolates in dB-like space."""
    n = ns(d)
    u = np.linspace(0, 1, n)
    xs = [p[0] for p in points]
    ys = np.array([p[1] for p in points], float)
    if log:
        return np.exp(np.interp(u, xs, np.log(np.maximum(ys, 1e-5))))
    return np.interp(u, xs, ys)


# ============================================================================ pitch
def glide(f0, f1, d, mode="exp", tau=None):
    """Frequency curve: 'exp' geometric sweep, 'lin' linear, 'fast' exponential approach (f0 snaps toward f1, tau s)."""
    n = ns(d)
    if mode == "lin":
        return np.linspace(f0, f1, n)
    if mode == "fast":
        t = np.arange(n) / SR
        return f1 + (f0 - f1) * np.exp(-t / (tau or d / 4))
    return np.geomspace(max(f0, 1e-3), max(f1, 1e-3), n)


def semis(st):
    return 2.0 ** (st / 12.0)


# ============================================================================ oscillators
def _polyblep(t, dt):
    """PolyBLEP residual for a phase t in [0,1) with step dt (vectorised)."""
    r = np.zeros_like(t)
    m = t < dt
    x = t[m] / dt[m]
    r[m] = x + x - x * x - 1.0
    m2 = t > 1.0 - dt
    x = (t[m2] - 1.0) / dt[m2]
    r[m2] = x * x + x + x + 1.0
    return r


def osc(freq, d, shape="sine", phase=0.0, duty=0.5):
    """Band-limited oscillator. freq: Hz scalar or per-sample array. shape: sine | tri | saw | square | pulse."""
    n = ns(d)
    f = curve(n, freq)
    dt = np.clip(f / SR, 1e-9, 0.49)
    ph = (phase + np.cumsum(dt) - dt[0]) % 1.0
    if shape == "sine":
        return np.sin(2 * np.pi * ph)
    if shape == "tri":
        return 2.0 * np.abs(2.0 * ph - 1.0) - 1.0
    if shape == "saw":
        return (2.0 * ph - 1.0) - _polyblep(ph, dt)
    if shape in ("square", "pulse"):
        w = 0.5 if shape == "square" else duty
        s = np.where(ph < w, 1.0, -1.0)
        s += _polyblep(ph, dt)
        s -= _polyblep((ph + (1 - w)) % 1.0, dt)
        return s
    raise ValueError(f"osc: unknown shape {shape}")


def fm(freq, d, ratio=2.0, index=1.0):
    """2-operator FM: carrier freq (scalar/array), modulator at ratio×carrier, index scalar/array (radians)."""
    n = ns(d)
    f = curve(n, freq)
    idx = curve(n, index)
    pm = np.cumsum(2 * np.pi * f * ratio / SR)
    pc = np.cumsum(2 * np.pi * f / SR)
    return np.sin(pc + idx * np.sin(pm))


def noise(d, rng, color="white"):
    """white | pink | brown noise, roughly unit peak."""
    n = ns(d)
    w = rng.uniform(-1, 1, n)
    if color == "white":
        return w
    if color == "pink":
        b = [0.049922035, -0.095993537, 0.050612699, -0.004408786]
        a = [1, -2.494956002, 2.017265875, -0.522189400]
        p = lfilter(b, a, w)
        return p / (np.max(np.abs(p)) + 1e-9)
    if color == "brown":
        br = lfilter([1.0], [1.0, -0.995], w)
        br -= np.mean(br)
        return br / (np.max(np.abs(br)) + 1e-9)
    raise ValueError(f"noise: unknown color {color}")


def partials(f0, d, ratios, amps=None, decays=None, rng=None, detune=0.0):
    """Additive / modal synthesis: sines at f0×ratio, each with its own exponential decay time (s).
    Inharmonic ratios = bell / metal / glass; harmonic ratios = organ-like tone."""
    n = ns(d)
    t = np.arange(n) / SR
    out = np.zeros(n)
    amps = amps or [1.0 / (i + 1) for i in range(len(ratios))]
    decays = decays or [d / (1 + 0.6 * i) for i in range(len(ratios))]
    for i, r in enumerate(ratios):
        f = f0 * r * (1 + (rng.uniform(-detune, detune) if (rng is not None and detune) else 0))
        if f >= SR * 0.45:
            continue
        ph = rng.uniform(0, 2 * np.pi) if rng is not None else 0.0
        out += amps[i] * np.sin(2 * np.pi * f * t + ph) * np.exp(-t / max(decays[i], 1e-3))
    return out


def crackle(d, rng, density=60.0, bright=4000.0, decay=0.004):
    """Sparse pops (fire crackle, debris, sparks): density = pops per second."""
    n = ns(d)
    imp = np.zeros(n)
    k = rng.poisson(density * d)
    if k:
        idx = rng.integers(0, n, k)
        imp[idx] = rng.uniform(0.3, 1.0, k) * rng.choice([-1, 1], k)
    ker = np.exp(-np.arange(ns(decay * 6)) / (decay * SR)) * rng.uniform(-1, 1, ns(decay * 6))
    return hp(np.convolve(imp, ker)[:n], bright * 0.5)


def sample_hold(d, rng, rate, lo=0.0, hi=1.0):
    """Stepped random values changing `rate` times per second (gating, glitch)."""
    n = ns(d)
    step = max(1, int(SR / rate))
    v = rng.uniform(lo, hi, n // step + 2)
    return np.repeat(v, step)[:n]


# ============================================================================ filters
def _sos(kind, fc, order):
    nyq = SR * 0.5
    if kind == "bandpass":
        lo, hi = fc
        return butter(order, [max(lo, 20) / nyq, min(hi, nyq * 0.95) / nyq], btype="band", output="sos")
    return butter(order, min(max(fc, 20), nyq * 0.95) / nyq, btype=kind, output="sos")


def lp(x, fc, order=2):
    return sosfilt(_sos("low", fc, order), x)


def hp(x, fc, order=2):
    return sosfilt(_sos("high", fc, order), x)


def bp(x, lo, hi, order=2):
    return sosfilt(_sos("bandpass", (lo, hi), order), x)


def _biquad(kind, f, q):
    w0 = 2 * np.pi * f / SR
    c, s = np.cos(w0), np.sin(w0)
    al = s / (2 * q)
    if kind == "lp":
        b = [(1 - c) / 2, 1 - c, (1 - c) / 2]
    elif kind == "hp":
        b = [(1 + c) / 2, -(1 + c), (1 + c) / 2]
    elif kind == "bp":
        b = [al, 0.0, -al]
    else:
        raise ValueError(kind)
    a = [1 + al, -2 * c, 1 - al]
    return np.array(b) / a[0], np.array(a) / a[0]


def vfilter(x, kind, fc, q=0.707, block=64):
    """Time-varying resonant biquad (lp | hp | bp), cutoff fc scalar or per-sample array (filter sweeps)."""
    fcs = curve(len(x), fc)
    y = np.empty_like(x)
    zi = np.zeros(2)
    for s in range(0, len(x), block):
        b, a = _biquad(kind, float(np.clip(fcs[s], 30, SR * 0.45)), q)
        y[s:s + block], zi = lfilter(b, a, x[s:s + block], zi=zi)
    return y


# ============================================================================ effects
def drive(x, k=2.0):
    """tanh saturation, gain-compensated."""
    return np.tanh(k * x) / np.tanh(k)


def ring(x, f):
    return x * np.sin(2 * np.pi * f * np.arange(len(x)) / SR)


def reverb(x, rng, decay=0.8, wet=0.2, damp=5000.0, pre=0.012):
    """Small synthetic room: exponentially decaying, progressively darker noise IR + early reflections.
    Extends the signal by `decay` seconds; wet is the reverb level relative to the dry signal."""
    n = ns(decay)
    t = np.arange(n) / SR
    ir = rng.uniform(-1, 1, n) * np.exp(-6.9 * t / decay)
    ir = 0.6 * lp(ir, damp) + 0.4 * lp(ir, damp * 0.35)
    for k in range(6):                                  # early reflections
        i = int((pre + rng.uniform(0.003, 0.03)) * SR)
        if i < n:
            ir[i] += rng.uniform(0.3, 0.7) * rng.choice([-1, 1])
    ir[: int(pre * SR)] = 0
    ir /= np.sqrt(np.sum(ir ** 2)) + 1e-9
    w = fftconvolve(x, ir)
    out = np.zeros(len(w))
    out[: len(x)] += x
    return out + wet * w * (np.max(np.abs(x)) / (np.max(np.abs(w)) + 1e-9))


def fade(x, fin=0.0005, fout=0.008):
    x = x.copy()
    a, b = min(len(x), ns(fin)), min(len(x), ns(fout))
    if fin > 0:
        x[:a] *= np.linspace(0, 1, a) ** 2
    x[-b:] *= np.linspace(1, 0, b) ** 2
    return x


def mix(*layers, length=None):
    """mix((sig, at_seconds, gain), ...) or plain arrays (at 0, gain 1) into one buffer."""
    items = []
    for L in layers:
        if isinstance(L, tuple):
            sig, at, g = (L + (1.0,))[:3] if len(L) == 2 else L
        else:
            sig, at, g = L, 0.0, 1.0
        items.append((np.asarray(sig, float), ns(at) if at > 0 else 0, g))
    n = length or max(o + len(s) for s, o, _ in items)
    out = np.zeros(n)
    for s, o, g in items:
        if o >= n:
            continue
        m = min(len(s), n - o)
        out[o:o + m] += g * s[:m]
    return out


def loop_seam(x, xfade):
    """Make a seamless loop: x holds loop_len + xfade samples; the tail is equal-power crossfaded into the head."""
    m = ns(xfade)
    L = len(x) - m
    y = x[:L].copy()
    u = np.linspace(0, 1, m)
    y[:m] = x[:m] * np.sin(0.5 * np.pi * u) + x[L:L + m] * np.cos(0.5 * np.pi * u)
    return y


def resample(x, ratio):
    """Pitch/tempo shift by linear-interp resampling (ratio > 1 = higher and shorter)."""
    n = max(2, int(len(x) / ratio))
    return np.interp(np.arange(n) * ratio, np.arange(len(x)), x)


# ============================================================================ loudness / mastering
def kweight(x):
    """ITU-R BS.1770 K-weighting (coefficients for 48 kHz, close enough at 44.1 kHz for game SFX)."""
    b1 = [1.53512485958697, -2.69169618940638, 1.19839281085285]
    a1 = [1, -1.69065929318241, 0.73248077421585]
    b2 = [1.0, -2.0, 1.0]
    a2 = [1, -1.99004745483398, 0.99007225036621]
    return lfilter(b2, a2, lfilter(b1, a1, x))


def loudness(x):
    """Momentary-max loudness (LUFS, 400 ms window, 100 ms hop): the standard number for short game SFX."""
    k = kweight(x) ** 2
    win, hop = ns(0.4), ns(0.1)
    if len(k) < win:
        k = np.pad(k, (0, win - len(k)))
    cs = np.concatenate([[0.0], np.cumsum(k)])
    ms = (cs[win::hop][: (len(k) - win) // hop + 1] - cs[0:len(k) - win + 1:hop]) / win
    return -0.691 + 10 * np.log10(np.max(ms) + 1e-12)


def db(v):
    return 20 * np.log10(max(abs(v), 1e-12))


def master(x, target_lufs, ceiling_db=CEILING_DB):
    """Loudness-normalise to target, soft-limit peaks to the ceiling, re-trim the gain. Returns (y, shaved_db)."""
    x = hp(x, 25)                                   # DC / sub-rumble removal (never subtract the mean: that
    ceil = 10 ** (ceiling_db / 20)                  # turns a decaying DC into a constant floor across the tail)
    shaved = 0.0
    for _ in range(3):
        x = x * 10 ** ((target_lufs - loudness(x)) / 20)
        pk = np.max(np.abs(x))
        if pk > ceil:
            knee = 0.6 * ceil
            over = np.abs(x) > knee
            y = x.copy()
            y[over] = np.sign(x[over]) * (knee + (ceil - knee) * np.tanh((np.abs(x[over]) - knee) / (ceil - knee)))
            shaved = max(shaved, db(pk) - ceiling_db)
            x = y
    pk = np.max(np.abs(x))
    if pk > ceil:
        x *= ceil / pk
    return x, shaved


def trim_tail(x, floor_db=-60.0, keep=0.02):
    """Cut trailing near-silence (keeps `keep` s after the last sample above floor_db re peak)."""
    thr = np.max(np.abs(x)) * 10 ** (floor_db / 20)
    idx = np.where(np.abs(x) > thr)[0]
    if not len(idx):
        return x
    return x[: min(len(x), idx[-1] + ns(keep))]


# ============================================================================ analysis
def envelope(x, win=0.005):
    w = ns(win)
    return np.convolve(np.abs(x), np.ones(w) / w, "same")


def analyze(x):
    """Objective numbers used by QA and review (the agent cannot listen; these + the spectrogram are its ears)."""
    e = envelope(x)
    ep = np.max(e) + 1e-12
    ipk = int(np.argmax(e))
    above = np.where(e > ep * 0.03)[0]
    onset = above[0] / SR if len(above) else 0.0
    tail_idx = np.where(e[ipk:] > ep * 0.01)[0]
    spec = np.abs(np.fft.rfft(x * np.hanning(len(x)))) ** 2
    f = np.fft.rfftfreq(len(x), 1 / SR)
    end = x[-ns(0.016):-ns(0.006)] if len(x) > ns(0.03) else x[-ns(0.006):]   # just before the final 6 ms fade
    peak_rms = np.sqrt(np.max(np.convolve(x ** 2, np.ones(ns(0.025)) / ns(0.025), "same"))) + 1e-12
    return {
        "dur": round(len(x) / SR, 3),
        "peak_db": round(db(np.max(np.abs(x))), 2),
        "lufs": round(float(loudness(x)), 2),
        "onset_ms": round(onset * 1000, 1),
        "attack_ms": round((np.argmax(e >= ep * 0.7) / SR - onset) * 1000, 1),
        "tail_s": round(tail_idx[-1] / SR if len(tail_idx) else 0.0, 3),
        "centroid_hz": int((spec * f).sum() / (spec.sum() + 1e-12)),     # power-weighted brightness
        "phone_loss_db": round(float(loudness(x) - loudness(hp(x, 300, 4))), 1),
        "dc": round(float(np.mean(x)), 5),
        "start_abs": round(float(abs(x[0])), 4),
        "end_abs": round(float(abs(x[-1])), 4),
        "end_level_db": round(db(np.sqrt(np.mean(end ** 2)) / peak_rms), 1),
    }


# ============================================================================ io
def write_wav(path, x):
    """16-bit mono PCM at SR (TPDF-dithered)."""
    rng = np.random.default_rng(0)
    d = (rng.uniform(-0.5, 0.5, len(x)) + rng.uniform(-0.5, 0.5, len(x))) / 32768
    q = np.clip(np.round((x + d) * 32767), -32768, 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(q.tobytes())


def read_wav(path):
    """RIFF WAV reader: PCM 8/16/24/32-bit and IEEE float 32/64, any channel count -> mono float, resampled to SR."""
    import struct
    data = open(path, "rb").read()
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise ValueError(f"{path}: not a RIFF/WAVE file")
    pos, fmt, raw = 12, None, None
    while pos + 8 <= len(data):
        cid, size = data[pos:pos + 4], struct.unpack("<I", data[pos + 4:pos + 8])[0]
        body = data[pos + 8:pos + 8 + size]
        if cid == b"fmt ":
            tag, ch, sr, _, _, bits = struct.unpack("<HHIIHH", body[:16])
            if tag == 0xFFFE and len(body) >= 26:
                tag = struct.unpack("<H", body[24:26])[0]
            fmt = (tag, ch, sr, bits)
        elif cid == b"data":
            raw = body
        pos += 8 + size + (size & 1)
    if not fmt or raw is None:
        raise ValueError(f"{path}: missing fmt/data chunk")
    tag, ch, sr, bits = fmt
    if tag == 3:
        x = np.frombuffer(raw[: len(raw) // (bits // 8) * (bits // 8)], "<f4" if bits == 32 else "<f8").astype(float)
    elif bits == 8:
        x = (np.frombuffer(raw, np.uint8).astype(float) - 128) / 128
    elif bits == 16:
        x = np.frombuffer(raw[: len(raw) // 2 * 2], "<i2").astype(float) / 32768
    elif bits == 24:
        b = np.frombuffer(raw[: len(raw) // 3 * 3], np.uint8).reshape(-1, 3)
        v = b[:, 0].astype(np.int32) | (b[:, 1].astype(np.int32) << 8) | (b[:, 2].astype(np.int32) << 16)
        x = np.where(v >= 1 << 23, v - (1 << 24), v).astype(float) / (1 << 23)
    else:
        x = np.frombuffer(raw[: len(raw) // 4 * 4], "<i4").astype(float) / 2 ** 31
    x = x[: len(x) // ch * ch].reshape(-1, ch).mean(axis=1)
    if sr != SR:
        x = resample(x, sr / SR)
    return x
