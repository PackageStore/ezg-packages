#!/usr/bin/env python3
"""fxpaint — FX painting kit (Pillow/numpy) for the create-vfx skill.

Paints luminous hand-painted-looking FX sheets. API reference: ../reference/painter-api.md

The look it produces (../reference/style-guide.md): luminous flipbooks played on Mobile/Particles/Alpha
Blended (the shared `_mat_ab`): white-hot cores, saturated colour, soft glow baked into alpha, needle
rays, thin glowing rings, sparkly specks, scalloped shells, thin soot rims when fire dies.

Painting model (gradient map, like hand-painted FX): primitives add scalar light intensity into a
named layer; `finish()` blooms each layer, maps 1 - exp(-E) through that layer's colour ramp (dim =
deep hue, bright = white-hot) and derives alpha from intensity, composites layers in order, then lays
the `ink` layer (dark soot) over it. Coordinates are normalised [-1, 1] with y up; supersampled 2x.
"""
import math

import numpy as np
from PIL import Image
from scipy import ndimage


def hexc(h, gain=1.0):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255 for i in (0, 2, 4)], np.float32) * gain


def ease_out(t, k=4.0):
    t = min(max(t, 0.0), 1.0)
    return (1 - math.exp(-k * t)) / (1 - math.exp(-k))


def ease_in(t, p=2.0):
    t = min(max(t, 0.0), 1.0)
    return t ** p


def smooth(t):
    t = min(max(t, 0.0), 1.0)
    return t * t * (3 - 2 * t)


def window(t, a, b, c, d):
    """0 before a, ramps to 1 by b, holds, ramps to 0 from c to d."""
    if t <= a or t >= d:
        return 0.0
    if t < b:
        return smooth((t - a) / max(b - a, 1e-6))
    if t > c:
        return 1 - smooth((t - c) / max(d - c, 1e-6))
    return 1.0


def cyc(t, k=1.0, phase=0.0):
    """Periodic 0..1 wave for seamless loops: same value at t=0 and t=1 when k is an integer."""
    return 0.5 - 0.5 * math.cos(2 * math.pi * (k * t + phase))


def twinkles(i, n=48, speed=1.7):
    """Per-speck brightness multipliers for frame i (sparkle shimmer)."""
    return [0.55 + 0.45 * math.sin(i * speed + k * 2.3) for k in range(n)]


def ramp(stops, n=256):
    """[(pos 0..1, hex)] -> (n,3) lookup table."""
    u = np.linspace(0, 1, n)
    pos = np.array([p for p, _ in stops], np.float32)
    cols = np.stack([hexc(h) for _, h in stops])
    return np.stack([np.interp(u, pos, cols[:, c]) for c in range(3)], -1).astype(np.float32)


class Layer:
    def __init__(self, n, lut, alpha_gain=2.0, bloom=((0.03, 0.5), (0.1, 0.25)), exposure=1.0):
        self.E = np.zeros((n, n), np.float32)
        self.lut = lut
        self.alpha_gain = alpha_gain
        self.bloom = bloom
        self.exposure = exposure


class Frame:
    def __init__(self, size=256, ss=2, layers=None):
        """layers: ordered {name: Layer kwargs} (bottom first)."""
        self.size = size
        self.ss = ss
        self.n = size * ss
        self.layers = {k: Layer(self.n, **v) for k, v in (layers or {}).items()}
        self.ink = np.zeros((self.n, self.n, 4), np.float32)   # straight-alpha RGBA painted over the light
        self.px = self.n / 2.0

    # ---------------------------------------------------------------- coordinate helpers
    def _box(self, cx, cy, rad):
        n = self.n
        x0 = int(max(0, math.floor((cx - rad + 1) * self.px)))
        x1 = int(min(n, math.ceil((cx + rad + 1) * self.px) + 1))
        y0 = int(max(0, math.floor((1 - (cy + rad)) * self.px)))
        y1 = int(min(n, math.ceil((1 - (cy - rad)) * self.px) + 1))
        if x1 <= x0 or y1 <= y0:
            return None
        ys, xs = np.mgrid[y0:y1, x0:x1].astype(np.float32)
        X = (xs + 0.5) / self.px - 1
        Y = 1 - (ys + 0.5) / self.px
        return (slice(y0, y1), slice(x0, x1)), X, Y

    def _add(self, sl, field, layer, intensity):
        if intensity <= 0:
            return
        self.layers[layer].E[sl] += field * intensity

    # ---------------------------------------------------------------- glowing primitives
    def dot(self, cx, cy, r, layer, intensity, hard=0.0):
        """Gaussian light blob; `hard` > 0 adds a crisp disc core."""
        b = self._box(cx, cy, r * 3)
        if b is None:
            return
        sl, X, Y = b
        d = np.sqrt((X - cx) ** 2 + (Y - cy) ** 2)
        f = np.exp(-(d / r) ** 2)
        if hard > 0:
            f = np.maximum(f, np.clip((r * hard - d) / (r * 0.15) + 0.5, 0, 1))
        self._add(sl, f, layer, intensity)

    def oval(self, cx, cy, rx, ry, layer, intensity, hard=0.0):
        """Anisotropic Gaussian blob (rx along x, ry along y); `hard` > 0 adds a crisp elliptic core.
        Used for sprites that Unity stretches (stretched billboards): paint pre-compressed along x."""
        b = self._box(cx, cy, max(rx, ry) * 3)
        if b is None:
            return
        sl, X, Y = b
        d = np.sqrt(((X - cx) / max(rx, 1e-5)) ** 2 + ((Y - cy) / max(ry, 1e-5)) ** 2)
        f = np.exp(-d ** 2)
        if hard > 0:
            f = np.maximum(f, np.clip((hard - d) / 0.15 + 0.5, 0, 1))
        self._add(sl, f, layer, intensity)

    def needle(self, cx, cy, ang, length, width, layer, intensity, back=0.0, power=1.0):
        """Tapered light ray from (cx,cy) along `ang` (rad); thickest at the root, sharp tip."""
        ca, sa = math.cos(ang), math.sin(ang)
        mx, my = cx + ca * (length - back) / 2, cy + sa * (length - back) / 2
        b = self._box(mx, my, (length + back) / 2 + width * 3)
        if b is None:
            return
        sl, X, Y = b
        dx, dy = X - cx, Y - cy
        along = dx * ca + dy * sa
        perp = np.abs(-dx * sa + dy * ca)
        t = np.where(along >= 0, along / max(length, 1e-6), -along / max(back, 1e-6) if back > 0 else 9)
        taper = np.clip(1 - t, 0, 1) ** power
        w = width * taper + 1e-5
        f = np.exp(-(perp / w) ** 2) * (taper > 0)
        self._add(sl, f, layer, intensity)

    def ring(self, cx, cy, R, w, layer, intensity, squash=1.0, mod=None, rot=0.0, sx=1.0):
        """Glowing ring; ellipse via `squash` (y) or `sx` (x). `mod(theta)` -> per-angle multiplier."""
        b = self._box(cx, cy, R * max(sx, 1.0) + w * 4)
        if b is None:
            return
        sl, X, Y = b
        dx, dy = (X - cx) / max(sx, 1e-3), (Y - cy) / max(squash, 1e-3)
        r = np.sqrt(dx * dx + dy * dy)
        f = np.exp(-((r - R) / w) ** 2)
        if mod is not None:
            f = f * mod(np.arctan2(dy, dx) - rot)
        self._add(sl, f, layer, intensity)

    def disc(self, cx, cy, R, layer, intensity, edge=0.04, core=0.35, bumps=None, squash=1.0, power=1.5, rim=0.0, rimw=0.06):
        """Filled glowing body; scalloped silhouette via `bumps(theta)` (multiplier on R).

        Interior brightens toward the centre (`core` = brightness at the rim relative to centre);
        `rim` adds a brighter band hugging the edge (expanding-shell look)."""
        b = self._box(cx, cy, R * 1.6 + edge * 3)
        if b is None:
            return
        sl, X, Y = b
        dx, dy = X - cx, (Y - cy) / max(squash, 1e-3)
        r = np.sqrt(dx * dx + dy * dy)
        Rt = R * (bumps(np.arctan2(dy, dx)) if bumps is not None else 1.0)
        inside = np.clip((Rt - r) / edge + 0.5, 0, 1)
        q = np.clip(r / np.maximum(Rt, 1e-4), 0, 1)
        f = inside * (core + (1 - core) * (1 - q) ** power)
        if rim > 0:
            f = f + inside * rim * np.exp(-((Rt - r) / rimw) ** 2)
        self._add(sl, f, layer, intensity)

    def crescent(self, cx, cy, R, a0, a1, w, layer, intensity, squash=1.0, peak=0.5):
        """Arc stroke from angle a0 to a1 (rad, CCW), sharp at both ends; thickest at `peak` (0..1 along
        the arc: 0.5 = moon crescent, ~0.8 = slash with a heavy leading edge and a thin trailing tail)."""
        b = self._box(cx, cy, R + w * 3)
        if b is None:
            return
        sl, X, Y = b
        dx, dy = X - cx, (Y - cy) / max(squash, 1e-3)
        r = np.sqrt(dx * dx + dy * dy)
        th = np.mod(np.arctan2(dy, dx) - a0, 2 * math.pi)
        span = (a1 - a0) % (2 * math.pi) or 2 * math.pi
        u = th / span
        e = math.log(0.5) / math.log(min(max(peak, 0.05), 0.95))
        prof = np.where(u <= 1, np.clip(np.sin(np.clip(u, 0, 1) ** e * math.pi), 0, 1) ** 0.8, 0)
        ww = w * prof + 1e-5
        f = np.exp(-((r - R) / ww) ** 2) * (prof > 0)
        self._add(sl, f, layer, intensity)

    def petal(self, cx, cy, ang, length, width, layer, intensity, hot=0.5):
        """Teardrop flame petal from (cx,cy) along `ang`: round root, pointed tip, hotter root."""
        ca, sa = math.cos(ang), math.sin(ang)
        mx, my = cx + ca * length / 2, cy + sa * length / 2
        b = self._box(mx, my, length / 2 + width * 2)
        if b is None:
            return
        sl, X, Y = b
        dx, dy = X - cx, Y - cy
        along = (dx * ca + dy * sa) / max(length, 1e-6)
        perp = np.abs(-dx * sa + dy * ca)
        t = np.clip(along, 0, 1)
        half = width * np.sin(np.clip(t, 0, 1) ** 0.55 * math.pi) * (along >= -0.02) * (along <= 1)
        inside = np.clip((half - perp) / (width * 0.12) + 0.5, 0, 1)
        # clamp: where half -> 0 (root / tip line) the falloff term blew up to large NEGATIVE light that
        # carved dark lines through everything underneath (dark spokes / streaks)
        f = inside * (hot + (1 - hot) * (1 - t)) * np.clip(1 - 0.5 * (perp / np.maximum(half, 1e-4)) ** 2, 0, 1)
        self._add(sl, f, layer, intensity)

    def beam(self, x0, y0, x1, y1, width, layer, intensity, fade=1.0):
        """Soft constant-width light column from (x0,y0) to (x1,y1), fading toward the far end."""
        ang = math.atan2(y1 - y0, x1 - x0)
        ln = math.hypot(x1 - x0, y1 - y0)
        ca, sa = math.cos(ang), math.sin(ang)
        b = self._box((x0 + x1) / 2, (y0 + y1) / 2, ln / 2 + width * 3)
        if b is None:
            return
        sl, X, Y = b
        dx, dy = X - x0, Y - y0
        along = (dx * ca + dy * sa) / max(ln, 1e-6)
        perp = np.abs(-dx * sa + dy * ca)
        f = np.exp(-(perp / width) ** 2) * np.clip(1 - along * fade, 0, 1) * (along >= 0) * (along <= 1)
        f = f * np.clip(along / 0.06, 0, 1)
        self._add(sl, f, layer, intensity)

    def polyline(self, pts, width, layer, intensity):
        """Thin glowing zigzag (lightning, cracks): constant width with sharp ends."""
        for (x0, y0), (x1, y1) in zip(pts[:-1], pts[1:]):
            ang = math.atan2(y1 - y0, x1 - x0)
            ln = math.hypot(x1 - x0, y1 - y0)
            self.needle(x0, y0, ang, ln, width, layer, intensity, back=width, power=0.15)

    def specks(self, rng_state, count, cx, cy, r_in, r_out, size, layer, intensity, squash=1.0, twinkle=None):
        """Sparkly dust: `rng_state` = (np.random.Generator seeded per effect) so positions stay put across frames."""
        rng = np.random.default_rng(rng_state)
        for i in range(count):
            a = rng.uniform(0, 2 * math.pi)
            d = math.sqrt(rng.uniform(r_in ** 2, r_out ** 2))
            s = size * rng.uniform(0.6, 1.3)
            k = intensity * rng.uniform(0.6, 1.2) * (twinkle[i % len(twinkle)] if twinkle is not None else 1)
            self.dot(cx + math.cos(a) * d, cy + math.sin(a) * d * squash, s, layer, k, hard=0.5)

    def star4(self, x, y, ang, length, width, layer, intensity, short=0.55, diag=0.0):
        """Four-point needle star (long pair + short pair), optional small diagonal pair."""
        for k in range(4):
            self.needle(x, y, ang + k * math.pi / 2, length if k % 2 == 0 else length * short, width, layer, intensity)
        if diag > 0:
            for k in range(4):
                self.needle(x, y, ang + math.pi / 4 + k * math.pi / 2, length * diag, width * 0.6, layer, intensity * 0.7)

    # ---------------------------------------------------------------- ink (alpha-composited, not light)
    def ink_ring(self, cx, cy, R, w, color, alpha, bumps=None, squash=1.0, breakup=None):
        b = self._box(cx, cy, R * 1.6 + w * 4)
        if b is None:
            return
        sl, X, Y = b
        dx, dy = X - cx, (Y - cy) / max(squash, 1e-3)
        r = np.sqrt(dx * dx + dy * dy)
        th = np.arctan2(dy, dx)
        Rt = R * (bumps(th) if bumps is not None else 1.0)
        f = np.exp(-((r - Rt) / w) ** 2) * alpha
        if breakup is not None:
            f = f * breakup(th)
        self._ink(sl, f, color)

    def ink_blob(self, cx, cy, R, color, alpha, bumps=None, edge=0.03, squash=1.0):
        b = self._box(cx, cy, R * 1.6)
        if b is None:
            return
        sl, X, Y = b
        dx, dy = X - cx, (Y - cy) / max(squash, 1e-3)
        r = np.sqrt(dx * dx + dy * dy)
        Rt = R * (bumps(np.arctan2(dy, dx)) if bumps is not None else 1.0)
        f = np.clip((Rt - r) / edge + 0.5, 0, 1) * alpha
        self._ink(sl, f, color)

    def _ink(self, sl, a, color):
        a = np.clip(a, 0, 1)[..., None]
        dst = self.ink[sl]
        out_a = a + dst[..., 3:4] * (1 - a)
        out_rgb = (np.asarray(color, np.float32) * a + dst[..., :3] * dst[..., 3:4] * (1 - a)) / np.maximum(out_a, 1e-5)
        self.ink[sl] = np.concatenate([out_rgb, out_a], -1)

    # ---------------------------------------------------------------- output
    def finish(self, ink_over=True):
        rgba = np.zeros((self.n, self.n, 4), np.float32)
        for lay in self.layers.values():
            E = np.nan_to_num(np.maximum(lay.E, 0), nan=0.0, posinf=50.0)
            if E.max() <= 0:
                continue
            for radius, strength in lay.bloom:
                if strength > 0:
                    E = E + ndimage.gaussian_filter(E, radius * self.px) * strength
            u = 1 - np.exp(-E * lay.exposure)
            idx = np.clip((u * (len(lay.lut) - 1)).astype(int), 0, len(lay.lut) - 1)
            col = lay.lut[idx]
            a = (1 - np.exp(-E * lay.alpha_gain))[..., None]
            out_a = a + rgba[..., 3:4] * (1 - a)
            out_rgb = (col * a + rgba[..., :3] * rgba[..., 3:4] * (1 - a)) / np.maximum(out_a, 1e-5)
            rgba = np.concatenate([out_rgb, out_a], -1)
        if ink_over and self.ink[..., 3].max() > 0:
            ia = self.ink[..., 3:4]
            out_a = ia + rgba[..., 3:4] * (1 - ia)
            out_rgb = (self.ink[..., :3] * ia + rgba[..., :3] * rgba[..., 3:4] * (1 - ia)) / np.maximum(out_a, 1e-5)
            rgba = np.concatenate([out_rgb, out_a], -1)
        # premultiplied box downsample (no dark/bright fringes), then un-premultiply
        pm = np.concatenate([rgba[..., :3] * rgba[..., 3:4], rgba[..., 3:4]], -1)
        s = self.ss
        pm = pm.reshape(self.size, s, self.size, s, 4).mean((1, 3))
        a2 = pm[..., 3:4]
        rgb2 = np.clip(pm[..., :3] / np.maximum(a2, 1e-5), 0, 1)
        out = np.concatenate([rgb2, a2], -1)
        return Image.fromarray(np.clip(out * 255 + 0.5, 0, 255).astype(np.uint8), "RGBA")


def sheet(frames, cols):
    """Row-major grid from the top-left (Unity texture-sheet order)."""
    w, h = frames[0].size
    rows = (len(frames) + cols - 1) // cols
    out = Image.new("RGBA", (w * cols, h * rows), (0, 0, 0, 0))
    for i, f in enumerate(frames):
        out.paste(f, ((i % cols) * w, (i // cols) * h))
    return out


def bumpy(rng, count=9, amp=0.12, sharp=1.0):
    """Random scalloped radius multiplier: theta -> 1 + bumps."""
    phases = rng.uniform(0, 2 * math.pi, 3)
    knots = rng.uniform(1 - amp, 1 + amp, count)

    def f(th):
        u = np.mod(th / (2 * math.pi), 1) * count
        i0 = np.floor(u).astype(int) % count
        i1 = (i0 + 1) % count
        fr = u - np.floor(u)
        s = np.abs(np.sin(fr * math.pi)) ** sharp                     # scallop: round lobes between knots
        base = knots[i0] * (1 - fr) + knots[i1] * fr
        return base * (1 - amp * 0.6 + amp * 0.6 * s) + 0.015 * np.sin(th * 5 + phases[0])
    return f


def spiky(rng, count=14, amp=0.35, width=0.35):
    """Theta -> 1 + sharp spikes (starburst silhouettes)."""
    centers = np.sort(rng.uniform(0, 2 * math.pi, count))
    heights = rng.uniform(0.4, 1.0, count) * amp

    def f(th):
        th = np.mod(th, 2 * math.pi)
        out = np.ones_like(th)
        for c, h in zip(centers, heights):
            d = np.abs(np.angle(np.exp(1j * (th - c))))
            out = out + h * np.clip(1 - d / (width * 2 * math.pi / count), 0, 1) ** 2
        return out
    return f


def breakup(rng, count=24, lo=0.0, hi=1.0, sharp=2.0):
    """Theta -> [0,1] gappy multiplier for rings that break apart."""
    knots = rng.uniform(lo, hi, count)

    def f(th):
        u = np.mod(th / (2 * math.pi), 1) * count
        i0 = np.floor(u).astype(int) % count
        i1 = (i0 + 1) % count
        fr = u - np.floor(u)
        v = knots[i0] * (1 - fr) + knots[i1] * fr
        return np.clip(v, 0, 1) ** sharp
    return f
