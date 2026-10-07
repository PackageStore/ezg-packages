#!/usr/bin/env python3
"""vfx_recipes — element palettes + flipbook recipes for the game-vfx skill.

A recipe is a function (pal, seed, n, px) -> list[PIL.Image] frames, registered in RECIPES with the
Unity defaults its prefab needs (kind, grid, life, world size, offsets, extra particle layers).
Every recipe paints with fxpaint on a single light layer "fx" mapped through the element ramp,
so any recipe x any element works (explode_burst + frost, slash_arc + toxic, ...).

Add new recipes following ../reference/recipes.md.

Sorting: `sorting_layer="ground"` puts a layer on the game's ground VFX layer (below characters); anything else
uses the main VFX layer. make_vfx resolves both names from ArtStyle.md §6b (defaults FX_Ground / FX).
Roles: `role` names the prefab layer after the game-vfx naming table 8.4 (impact, glow, flash, ring, shockwave,
wave, fire, smoke, dust, spark, debris, trail, lightning, halfsphere, decal). `group` is the prefab name group
of 8.2 (hit, proj, muzzle, slash, cast, skill, aoe, buff, debuff, heal, shield, aura...).
"""
import math
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from fxpaint import (Frame, breakup, bumpy, cyc, ease_in, ease_out, hexc, ramp, smooth, spiky,  # noqa: E402
                     twinkles, window)

TAU = 2 * math.pi
BLOOM = ((0.025, 0.45), (0.09, 0.22))

# ============================================================================ element palettes
# ramp: dim (left) -> white-hot (right). spark: 3 colours for spark/mote gradients after white.
PALETTES = {
    "fire": dict(ramp=[(0.0, "#b8240f"), (0.22, "#ff4a17"), (0.42, "#ff8a1e"), (0.6, "#ffbe2e"),
                       (0.76, "#ffe35a"), (0.9, "#fff6c4"), (1.0, "#ffffff")],
                 spark=["#ffdb66", "#ff801f", "#f2380f"], soot="#5e2a14",
                 haze=[(0.0, "#ffc870"), (1.0, "#fff0c8")]),
    "gold": dict(ramp=[(0.0, "#c2400f"), (0.25, "#ff8a1e"), (0.45, "#ffc22e"), (0.65, "#ffe866"),
                       (0.85, "#fff8cc"), (1.0, "#ffffff")],
                 spark=["#ffeb80", "#ffa633", "#f25914"], soot="#6a3a10",
                 haze=[(0.0, "#ffd77a"), (1.0, "#fff6d6")]),
    "frost": dict(ramp=[(0.0, "#1d3fd0"), (0.25, "#2f86ff"), (0.48, "#4fd2ff"), (0.68, "#9ef4ff"),
                        (0.86, "#e4fdff"), (1.0, "#ffffff")],
                  spark=["#a6f2ff", "#4da6ff", "#3359f2"], soot="#1a2f66",
                  haze=[(0.0, "#a8e8ff"), (1.0, "#f0fdff")]),
    "arcane": dict(ramp=[(0.0, "#5a1fc4"), (0.25, "#9a3cff"), (0.48, "#e05cf0"), (0.68, "#ff9cf0"),
                         (0.86, "#ffe0fb"), (1.0, "#ffffff")],
                   spark=["#ffa6f2", "#bf59ff", "#7333e6"], soot="#2e1050",
                   haze=[(0.0, "#e0a8ff"), (1.0, "#fbefff")]),
    "toxic": dict(ramp=[(0.0, "#1f7a1a"), (0.25, "#3fbf2a"), (0.48, "#8ae63a"), (0.68, "#cfff6a"),
                        (0.86, "#f3ffd0"), (1.0, "#ffffff")],
                  spark=["#d9ff73", "#73e033", "#2e9e1f"], soot="#1c3d10",
                  haze=[(0.0, "#c8f080"), (1.0, "#f6ffe0")]),
    "holy": dict(ramp=[(0.0, "#c98a12"), (0.28, "#ffcf45"), (0.52, "#fff08a"), (0.76, "#fffbe0"),
                       (1.0, "#ffffff")],
                 spark=["#fffbd0", "#ffe066", "#ffb31f"], soot="#7a5410",
                 haze=[(0.0, "#fff0b0"), (1.0, "#fffef4")]),
    "shadow": dict(ramp=[(0.0, "#2a0f4a"), (0.3, "#5a1fa0"), (0.55, "#9b4dff"), (0.8, "#d7b0ff"),
                         (1.0, "#ffffff")],
                   spark=["#d1b3ff", "#8c4dff", "#4d1f99"], soot="#14061f",
                   haze=[(0.0, "#9a70d0"), (1.0, "#e8dcff")]),
    "electric": dict(ramp=[(0.0, "#2b3fe0"), (0.3, "#4f9cff"), (0.55, "#a8ecff"), (0.8, "#f4fdff"),
                           (1.0, "#ffffff")],
                     spark=["#d9f7ff", "#73bfff", "#3366ff"], soot="#101a4d",
                     haze=[(0.0, "#a0d8ff"), (1.0, "#f4fbff")]),
    # hot pink (blade / energy weapon accent)
    "rose": dict(ramp=[(0.0, "#9e0f4c"), (0.24, "#e0236a"), (0.46, "#ff4f93"), (0.66, "#ff94c2"),
                       (0.85, "#ffe2ef"), (1.0, "#ffffff")],
                 spark=["#ffb8d6", "#ff4d93", "#c2185e"], soot="#4a0a26",
                 haze=[(0.0, "#ffa8cc"), (1.0, "#fff0f6")]),
}


def palette(name):
    p = PALETTES[name]
    return dict(name=name, lut=ramp(p["ramp"]), haze=ramp(p["haze"]), soot=hexc(p["soot"]), spark=p["spark"])


def register_palette(name, stops, spark=None, soot=None, haze=None):
    """Adds or overrides an element ramp (project ramps declared in ArtStyle.md §6b). Missing parts are derived
    from the ramp: spark = 3 colours sampled bright -> deep, soot = the darkest stop darkened, haze = the two
    brightest stops."""
    stops = sorted((float(p), c) for p, c in stops)
    if len(stops) < 2:
        raise ValueError(f"ramp {name}: needs at least 2 stops")
    lut = ramp(stops)

    def to_hex(rgb, gain=1.0):
        return "#" + "".join(f"{int(round(min(1.0, v * gain) * 255)):02x}" for v in rgb[:3])

    def at(u):
        return to_hex(lut[min(len(lut) - 1, int(u * (len(lut) - 1)))])
    if not soot:
        soot = to_hex(hexc(stops[0][1]), 0.35)
    PALETTES[name] = dict(ramp=stops, spark=list(spark or [at(0.8), at(0.5), at(0.25)]), soot=soot,
                          haze=haze or [(0.0, stops[-2][1]), (1.0, stops[-1][1])])


def frame(pal, px, haze=False):
    lay = {}
    if haze:
        lay["haze"] = dict(lut=pal["haze"], alpha_gain=0.9, bloom=((0.04, 0.3),))
    lay["fx"] = dict(lut=pal["lut"], alpha_gain=2.2, bloom=BLOOM)
    return Frame(px, layers=lay)


# ============================================================================ IMPACT
def impact_star(pal, seed, n, px):
    """Hit spark: white core, 4-point needle star, expanding ring, 2 crescents, shards, specks."""
    rng = np.random.default_rng(seed)
    rot = rng.uniform(0.15, 0.6)
    cres = [(rng.uniform(0.22, 0.32), a, rng.uniform(1.2, 1.7), rng.uniform(0.022, 0.03))
            for a in (rng.uniform(0, 1), rng.uniform(3.2, 4.2))]
    shards = [(rng.uniform(0, TAU), rng.uniform(0.45, 0.85), rng.uniform(0.08, 0.16)) for _ in range(11)]
    burst = spiky(rng, count=10, amp=0.9, width=0.25)
    out = []
    for i in range(n):
        t = i / (n - 1)
        f = frame(pal, px)
        fl = window(t, -0.01, 0.0, 0.1, 0.4)
        f.dot(0, 0, 0.13 * (1 + 0.6 * ease_out(t / 0.2)), "fx", 3.4 * fl)
        f.disc(0, 0, 0.16 + 0.16 * ease_out(t / 0.2), "fx", 1.3 * window(t, -0.01, 0.0, 0.06, 0.32), edge=0.01,
               core=0.5, bumps=burst)
        st = window(t, -0.01, 0.0, 0.14, 0.42)
        f.star4(0, 0, rot, 0.5 + 0.38 * ease_out(t / 0.3), 0.034 * (1 - 0.5 * t), "fx", 2.4 * st, diag=0.45)
        rg = window(t, 0.0, 0.05, 0.2, 0.5)
        f.ring(0, 0, 0.16 + 0.42 * ease_out(t / 0.5), 0.016 + 0.01 * (1 - t), "fx", 1.1 * rg)
        cr = window(t, 0.04, 0.12, 0.34, 0.62)
        for r0, a0, span, w in cres:
            a = a0 + 1.4 * ease_out(t / 0.6)
            f.crescent(0, 0, r0 + 0.3 * ease_out(t / 0.55), a, a + span, w * (1 - 0.4 * t), "fx", 1.8 * cr)
        sh = window(t, 0.02, 0.08, 0.45, 0.85)
        for ang, spd, ln in shards:
            d = 0.08 + spd * ease_out(t / 0.7, 3)
            f.needle(math.cos(ang) * d, math.sin(ang) * d, ang + math.pi, ln * 1.3 * (1 - 0.6 * t), 0.009, "fx", 2.0 * sh)
        sp = window(t, 0.12, 0.3, 0.55, 1.0)
        f.specks(seed, 20, 0, 0, 0.08, 0.22 + 0.5 * ease_out(t / 0.8), 0.012, "fx", 2.4 * sp, twinkle=twinkles(i))
        out.append(f.finish())
    return out


def impact_shatter(pal, seed, n, px):
    """Crisp hit: lens streak (long diagonal needle), double ring pulse, diamond shards, specks."""
    rng = np.random.default_rng(seed)
    rot = rng.uniform(0.5, 0.8)
    shards = [(rng.uniform(0, TAU), rng.uniform(0.45, 0.8), rng.uniform(0.07, 0.12)) for _ in range(9)]
    out = []
    for i in range(n):
        t = i / (n - 1)
        f = frame(pal, px)
        f.dot(0, 0, 0.12 + 0.08 * ease_out(t / 0.2), "fx", 3.6 * window(t, -0.01, 0.0, 0.1, 0.35), hard=0.6)
        ls = window(t, -0.01, 0.0, 0.18, 0.5)
        L = 0.62 + 0.3 * ease_out(t / 0.3)
        for a in (rot, rot + math.pi):
            f.needle(0, 0, a, L, 0.03 * (1 - 0.4 * t), "fx", 2.6 * ls)
        for a in (rot + math.pi / 2, rot - math.pi / 2):
            f.needle(0, 0, a, L * 0.35, 0.022, "fx", 2.0 * ls)
        for k, (delay, rmax) in enumerate(((0.0, 0.62), (0.12, 0.42))):
            u = (t - delay) / 0.6
            if 0 < u < 1:
                f.ring(0, 0, 0.12 + rmax * ease_out(u, 3.5), 0.014 + 0.012 * (1 - u), "fx",
                       1.4 * (1 - smooth(u)) * (1.0 - 0.3 * k))
        sh = window(t, 0.03, 0.1, 0.5, 0.9)
        for ang, spd, ln in shards:
            d = 0.12 + spd * ease_out(t / 0.75, 3)
            f.needle(math.cos(ang) * d, math.sin(ang) * d, ang, ln * 0.6, 0.02, "fx", 1.8 * sh, back=ln * 0.6, power=1.2)
        sp = window(t, 0.1, 0.28, 0.55, 1.0)
        f.specks(seed, 18, 0, 0, 0.1, 0.25 + 0.45 * ease_out(t / 0.8), 0.011, "fx", 2.3 * sp, twinkle=twinkles(i))
        out.append(f.finish())
    return out


# ============================================================================ MUZZLE (points +X)
MUZZLE_X = -0.62   # barrel tip inside the frame; prefab offset 0.31 * size puts it on the pivot


def muzzle_flower(pal, seed, n, px):
    """Flame-petal flower (5 petals + back licks), forward needles, shock ring, forward sparks."""
    rng = np.random.default_rng(seed)
    sparks = [(rng.uniform(-0.45, 0.45), rng.uniform(0.7, 1.4), rng.uniform(0.05, 0.1)) for _ in range(7)]
    out = []
    x0 = MUZZLE_X
    for i in range(n):
        t = i / (n - 1)
        f = frame(pal, px)
        pk = window(t, -0.01, 0.0, 0.18, 0.62)
        grow = 0.65 + 0.35 * ease_out(t / 0.15)
        shrink = 1 - 0.35 * t
        f.dot(x0, 0, 0.11, "fx", 3.0 * window(t, -0.01, 0.0, 0.12, 0.45), hard=0.5)
        f.petal(x0 - 0.03, 0, 0.0, 1.0 * grow * shrink, 0.13, "fx", 1.25 * pk, hot=0.3)
        for s in (-1, 1):
            f.petal(x0 - 0.02, 0, s * 0.34, 0.7 * grow * shrink, 0.09, "fx", 1.05 * pk, hot=0.3)
            f.petal(x0 - 0.01, 0, s * 0.78, 0.42 * grow * shrink, 0.075, "fx", 0.95 * pk, hot=0.35)
            f.petal(x0, 0, s * 1.95, 0.17 * grow, 0.05, "fx", 0.9 * pk, hot=0.5)
            f.needle(x0, 0, s * 0.2, 0.85 * grow, 0.014, "fx", 1.4 * pk)
        f.needle(x0, 0, 0.0, 1.35 * grow, 0.02, "fx", 1.8 * pk)
        rg = window(t, 0.0, 0.08, 0.3, 0.75)
        f.ring(x0 + 0.18 + 0.25 * ease_out(t / 0.6), 0, 0.22 + 0.12 * ease_out(t / 0.6), 0.016, "fx", 1.3 * rg, sx=0.32)
        sp = window(t, 0.05, 0.2, 0.5, 1.0)
        for ang, spd, ln in sparks:
            d = 0.2 + spd * ease_out(t / 0.9, 3)
            f.needle(x0 + math.cos(ang) * d, math.sin(ang) * d, ang + math.pi, ln, 0.012, "fx", 1.8 * sp)
        out.append(f.finish())
    return out


def muzzle_beam(pal, seed, n, px):
    """Energy discharge: bright orb, long forward beam needle, cone of rings marching forward."""
    out = []
    x0 = MUZZLE_X
    for i in range(n):
        t = i / (n - 1)
        f = frame(pal, px)
        pk = window(t, -0.01, 0.0, 0.2, 0.65)
        f.dot(x0, 0, 0.1 + 0.04 * ease_out(t / 0.2), "fx", 3.2 * window(t, -0.01, 0.0, 0.15, 0.5), hard=0.6)
        f.needle(x0, 0, 0.0, 1.45 * (0.7 + 0.3 * ease_out(t / 0.15)), 0.04 * (1 - 0.5 * t), "fx", 2.4 * pk)
        f.needle(x0, 0, math.pi, 0.16, 0.03, "fx", 1.6 * pk)
        f.star4(x0, 0, 0.0, 0.24, 0.022, "fx", 1.8 * window(t, -0.01, 0.0, 0.1, 0.35), short=0.9)
        for k in range(3):
            u = (t - k * 0.08) / 0.55
            if 0 < u < 1:
                xr = x0 + 0.2 + 0.32 * k + 0.25 * ease_out(u, 3)
                rr = (0.14 + 0.08 * k) * (1 + 0.5 * ease_out(u, 3))
                f.ring(xr, 0, rr, 0.016, "fx", 1.5 * (1 - smooth(u)), sx=0.3)
        cr = window(t, 0.05, 0.15, 0.4, 0.75)
        for s in (-1, 1):
            a = s * 0.55 + (-s) * 0.4 * ease_out(t / 0.6)
            a0, a1 = (a - 0.6, a) if s > 0 else (a, a + 0.6)
            f.crescent(x0 + 0.1, 0, 0.32 + 0.18 * ease_out(t / 0.6), a0, a1, 0.03, "fx", 1.6 * cr)
        sp = window(t, 0.08, 0.25, 0.5, 1.0)
        f.specks(seed, 14, x0 + 0.45, 0, 0.0, 0.35 + 0.25 * t, 0.012, "fx", 2.2 * sp, squash=0.6, twinkle=twinkles(i))
        out.append(f.finish())
    return out


# ============================================================================ EXPLODE
def explode_burst(pal, seed, n, px):
    """Fireball: spiky flash -> scalloped ball with rays -> hollow shell -> thin soot rim + specks."""
    rng = np.random.default_rng(seed)
    body_bumps = bumpy(rng, count=11, amp=0.10)
    rim_bumps = bumpy(rng, count=13, amp=0.12)
    flash_spikes = spiky(rng, count=14, amp=1.1, width=0.22)
    rays = [(rng.uniform(0, TAU), rng.uniform(0.55, 0.95), rng.uniform(0.008, 0.016)) for _ in range(24)]
    soot_break = breakup(rng, count=30, lo=-0.4, hi=1.2, sharp=1.0)
    embers = [(rng.uniform(0, TAU), rng.uniform(0.9, 1.6), rng.uniform(0.012, 0.02)) for _ in range(14)]
    out = []
    for i in range(n):
        t = i / (n - 1)
        f = frame(pal, px, haze=True)
        R = 0.24 + 0.42 * ease_out(t / 0.45, 4)
        heat = max(0.0, 1 - t / 0.72)
        fl = window(t, -0.01, 0.0, 0.07, 0.2)
        if fl > 0:
            rr = 0.22 + 0.22 * ease_out(t / 0.15)
            f.disc(0, 0, rr, "fx", 1.6 * fl, edge=0.012, core=0.6, bumps=flash_spikes)
            f.disc(0, 0, rr * 0.55, "fx", 3.0 * fl, edge=0.03, core=0.7)
        body = window(t, 0.0, 0.05, 0.4, 0.72)
        if body > 0:
            hollow = smooth((t - 0.15) / 0.4)
            f.disc(0, 0, R, "fx", (1.0 * heat + 0.35) * body, edge=0.02,
                   core=max(0.6 - 0.58 * hollow, 0.02), power=1.0 + 2.5 * hollow, bumps=body_bumps,
                   rim=0.6 + 0.9 * hollow, rimw=0.045 + 0.02 * hollow)
            f.disc(0, 0, R * (0.7 - 0.2 * hollow), "fx", 1.6 * heat * body * (1 - 0.9 * hollow),
                   edge=0.12, core=0.2, power=1.2, bumps=body_bumps)
            f.disc(0, 0, R * 0.95, "haze", 0.3 * body * hollow, edge=0.05, core=0.85, bumps=body_bumps)
        ray = window(t, 0.02, 0.07, 0.28, 0.5)
        for ang, ln, w in rays:
            reach = min(R * 0.7 + ln * 0.6 * ease_out(t / 0.35), 0.86 - R * 0.45)
            f.needle(math.cos(ang) * R * 0.45, math.sin(ang) * R * 0.45, ang, reach, w, "fx", 0.9 * ray)
        sp = window(t, 0.1, 0.28, 0.65, 1.0)
        f.specks(seed + 90, 34, 0, 0, 0.04, R * 0.92, 0.015, "fx", 3.0 * sp, twinkle=twinkles(i))
        em = window(t, 0.06, 0.15, 0.6, 0.95)
        for ang, spd, sz in embers:
            d = min(0.25 + spd * 0.4 * ease_out(t / 0.8, 3), 0.86)
            f.dot(math.cos(ang) * d, math.sin(ang) * d, sz, "fx", 1.4 * em, hard=0.6)
        so = window(t, 0.32, 0.5, 0.72, 1.0)
        if so > 0:
            f.ink_ring(0, 0, R * 1.0, 0.012, pal["soot"], 0.9 * so, bumps=rim_bumps, breakup=soot_break)
            f.ring(0, 0, R * 0.97, 0.016, "fx", 0.6 * so * (1 - smooth((t - 0.55) / 0.35)), mod=soot_break)
        out.append(f.finish())
    return out


def explode_nova(pal, seed, n, px):
    """Energy nova: spiky flash -> spiky shell with bright rim, crystal needles, outer ring -> broken ring + specks."""
    rng = np.random.default_rng(seed)
    shell = spiky(rng, count=18, amp=0.28, width=0.3)
    flash = spiky(rng, count=12, amp=1.2, width=0.2)
    crystals = [(rng.uniform(0, TAU), rng.uniform(0.6, 1.0), rng.uniform(0.1, 0.2)) for _ in range(12)]
    shell_break = breakup(rng, count=36, lo=-0.3, hi=1.3, sharp=1.0)
    out = []
    for i in range(n):
        t = i / (n - 1)
        f = frame(pal, px)
        R = 0.2 + 0.46 * ease_out(t / 0.5, 4)
        fl = window(t, -0.01, 0.0, 0.07, 0.2)
        if fl > 0:
            rr = 0.2 + 0.25 * ease_out(t / 0.15)
            f.disc(0, 0, rr, "fx", 1.7 * fl, edge=0.01, core=0.6, bumps=flash)
            f.dot(0, 0, rr * 0.5, "fx", 3.2 * fl)
        body = window(t, 0.0, 0.05, 0.45, 0.8)
        if body > 0:
            hollow = smooth((t - 0.1) / 0.35)
            f.disc(0, 0, R, "fx", (1.3 - 0.5 * t) * body, edge=0.012,
                   core=max(0.55 - 0.5 * hollow, 0.03), power=1.0 + 3.0 * hollow, bumps=shell,
                   rim=0.7 + 1.0 * hollow, rimw=0.04)
        late = window(t, 0.45, 0.6, 0.8, 1.0)
        if late > 0:
            f.ring(0, 0, R * 1.02, 0.02, "fx", 1.3 * late, mod=shell_break)
        rg = window(t, 0.02, 0.08, 0.35, 0.7)
        f.ring(0, 0, R * 1.18 + 0.04, 0.012, "fx", 1.0 * rg)
        cr = window(t, 0.03, 0.1, 0.4, 0.75)
        for ang, spd, ln in crystals:
            d = R * 0.6 + spd * 0.25 * ease_out(t / 0.6)
            f.needle(math.cos(ang) * d, math.sin(ang) * d, ang, min(ln, 0.86 - d), 0.024, "fx", 1.8 * cr, back=ln * 0.4, power=1.1)
        sp = window(t, 0.12, 0.3, 0.7, 1.0)
        f.specks(seed, 34, 0, 0, 0.05, R * 0.95, 0.014, "fx", 2.8 * sp, twinkle=twinkles(i))
        out.append(f.finish())
    return out


# ============================================================================ CAST
SIGIL_Y, SIGIL_SQ = -0.36, 0.42   # ground circle centre (prefab offset -SIGIL_Y/2 * size puts it on the pivot) / 3/4 squash


def cast_sigil(pal, seed, n, px):
    """Ground magic circle (3/4 ellipse) drawing itself in, runes, hexagram, light pillar, rim flames, rising embers."""
    rng = np.random.default_rng(seed)
    cy, sq = SIGIL_Y, SIGIL_SQ
    flames = [(rng.uniform(0, TAU), rng.uniform(0.18, 0.32), rng.uniform(0, TAU)) for _ in range(12)]
    out = []
    for i in range(n):
        t = i / (n - 1)
        f = frame(pal, px)
        draw = 0.06 + 0.94 * ease_out(t / 0.3, 3)
        on = window(t, -0.01, 0.0, 0.8, 1.0)
        boost = 1 + 0.5 * window(t, 0.62, 0.7, 0.72, 0.85)
        spin = 0.9 * t

        def arc_mask(th, d=draw):
            u = np.mod(th + math.pi / 2, TAU) / TAU
            return np.clip((d - u) / 0.04 + 0.5, 0, 1)
        f.ring(0, cy, 0.8, 0.018, "fx", 1.6 * on * boost, squash=sq, mod=arc_mask)
        f.ring(0, cy, 0.62, 0.012, "fx", 1.3 * on * boost, squash=sq, mod=lambda th: arc_mask(-th))
        for k in range(24):
            a = k * TAU / 24 + spin
            if (np.mod(a + math.pi / 2, TAU) / TAU) > draw:
                continue
            r0, r1 = 0.66, (0.76 if k % 3 else 0.79)
            xa, ya = math.cos(a) * r0, cy + math.sin(a) * r0 * sq
            xb, yb = math.cos(a) * r1, cy + math.sin(a) * r1 * sq
            f.needle(xa, ya, math.atan2(yb - ya, xb - xa), math.hypot(xb - xa, yb - ya), 0.012, "fx",
                     1.2 * on * boost, back=0.01, power=0.3)
        hx = window(t, 0.15, 0.35, 0.8, 1.0)
        for tri in range(2):
            pts = []
            for k in range(4):
                a = -spin * 0.6 + tri * math.pi / 3 + k * TAU / 3 + math.pi / 2
                pts.append((math.cos(a) * 0.58, cy + math.sin(a) * 0.58 * sq))
            f.polyline(pts, 0.01, "fx", 1.1 * hx * boost)
        pl = window(t, 0.25, 0.45, 0.75, 1.0)
        f.beam(0, cy, 0, 0.84, 0.2, "fx", 0.5 * pl * boost, fade=1.0)
        f.beam(0, cy, 0, 0.75, 0.05, "fx", 1.0 * pl * boost, fade=1.0)
        f.dot(0, cy, 0.28, "fx", 0.6 * pl * boost)
        fm = window(t, 0.3, 0.45, 0.78, 0.98)
        for a, ln, ph in flames:
            flick = 0.7 + 0.3 * math.sin(i * 1.3 + ph)
            f.petal(math.cos(a) * 0.78, cy + math.sin(a) * 0.78 * sq, math.pi / 2, ln * flick, 0.05, "fx",
                    0.85 * fm * boost, hot=0.4)
        rs = window(t, 0.3, 0.45, 0.8, 1.0)
        rr = np.random.default_rng(seed + 7)
        for k in range(16):
            a, r, v = rr.uniform(0, TAU), rr.uniform(0.1, 0.8), rr.uniform(0.6, 1.2)
            y = cy + math.sin(a) * r * sq + v * max(t - 0.3, 0) * 1.6
            edge = min(max((0.84 - y) / 0.2, 0.0), 1.0)          # fade out before leaving the frame
            f.dot(math.cos(a) * r, y, 0.014, "fx", 2.2 * rs * edge * (0.6 + 0.4 * math.sin(i + k)), hard=0.6)
        out.append(f.finish())
    return out


def cast_charge(pal, seed, n, px):
    """Charge & release: motes spiral into a growing orb with gyroscope rings, then star burst + ring."""
    rng = np.random.default_rng(seed)
    motes = [(rng.uniform(0, TAU), rng.uniform(0.55, 0.95), rng.uniform(0.8, 1.6)) for _ in range(26)]
    out = []
    for i in range(n):
        t = i / (n - 1)
        f = frame(pal, px)
        charge = smooth(t / 0.62)
        rel = window(t, 0.6, 0.66, 0.72, 0.95)
        after = window(t, 0.66, 0.75, 0.85, 1.0)
        pre = window(t, -0.01, 0.0, 0.62, 0.7)
        for a0, r0, spd in motes:
            u = min(t / 0.62, 1)
            r = r0 * (1 - ease_in(u, 1.6))
            a = a0 + spd * 2.2 * u
            if r > 0.03:
                x, y = math.cos(a) * r, math.sin(a) * r
                f.dot(x, y, 0.014, "fx", 2.2 * pre * window(u, -0.01, 0.12, 0.9, 1.0), hard=0.6)
                f.needle(x, y, a + math.pi / 2 + 0.3, 0.06 + 0.06 * u, 0.008, "fx", 1.0 * pre)
        f.dot(0, 0, 0.08 + 0.12 * charge, "fx", (1.2 + 2.6 * charge) * pre + 3.5 * rel, hard=0.55)
        for k in range(2):
            sx = 0.28 + 0.12 * math.sin(i * 0.9 + k * 2.1)
            f.ring(0, 0, 0.34 + 0.06 * charge, 0.012, "fx", 1.3 * charge * pre,
                   sx=sx if k == 0 else 1.0, squash=1.0 if k == 0 else sx)
        for k in range(3):
            a = k * TAU / 3 + 3.2 * t
            f.crescent(0, 0, 0.5 - 0.15 * charge, a, a + 1.2, 0.022, "fx", 1.3 * pre * window(t, 0.05, 0.2, 0.55, 0.65))
        if rel > 0 or after > 0:
            u = (t - 0.6) / 0.4
            f.star4(0, 0, 0.3, 0.55 + 0.4 * ease_out(u), 0.04, "fx", 2.6 * rel, diag=0.5)
            f.ring(0, 0, 0.2 + 0.6 * ease_out(u, 3), 0.02, "fx", 1.6 * (rel + after))
            f.specks(seed, 26, 0, 0, 0.1, 0.25 + 0.6 * ease_out(u, 3), 0.013, "fx", 2.4 * after, twinkle=twinkles(i))
        out.append(f.finish())
    return out


# ============================================================================ NEW: SLASH / ORB LOOP / AURA LOOP
def slash_arc(pal, seed, n, px):
    """Melee swipe facing +X: a wide crescent sweeps from bottom to top with a heavy bright leading
    edge and a thin trailing tail, an inner echo arc, a flare on the tip, sparkles peeling off the rim.
    Rotate the prefab to aim it."""
    rng = np.random.default_rng(seed)
    a_lo, a_hi, R, cx = -1.4, 1.4, 0.64, -0.1
    peel = [(rng.uniform(a_lo, a_hi), rng.uniform(0.1, 0.3), rng.uniform(0.01, 0.018)) for _ in range(18)]
    out = []
    for i in range(n):
        t = i / (n - 1)
        f = frame(pal, px)
        head = a_lo + (a_hi - a_lo) * (0.18 + 0.82 * ease_out(t / 0.38, 4))
        tail = a_lo + (a_hi - a_lo) * ease_in(min(t / 0.85, 1), 1.8)
        live = window(t, -0.01, 0.0, 0.5, 0.95)
        if head - tail > 0.05:
            f.crescent(cx, 0, R, tail, head, 0.2, "fx", 0.8 * live, peak=0.62)
            f.crescent(cx, 0, R, tail, head, 0.085, "fx", 2.6 * live, peak=0.66)
            f.crescent(cx, 0, R - 0.16, tail + 0.25, head - 0.08, 0.03, "fx", 1.3 * live, peak=0.6)
            hx, hy = cx + math.cos(head) * R, math.sin(head) * R
            f.star4(hx, hy, head, 0.2, 0.018, "fx", 1.8 * window(t, -0.01, 0.0, 0.3, 0.55), short=0.6)
        for a, out_d, sz in peel:
            if a > head:
                continue
            k = window(t, 0.05, 0.2, 0.55, 1.0)
            d = min(R + out_d * ease_out(t / 0.9, 3), 0.9)
            f.dot(cx + math.cos(a) * d, math.sin(a) * d, sz, "fx", 2.2 * k, hard=0.6)
        out.append(f.finish())
    return out


def orb_loop(pal, seed, n, px):
    """Seamless looping projectile body: pulsing core, swirl crescents and orbiting specks
    (everything advances exactly one turn per loop, so frame n wraps to frame 0)."""
    rng = np.random.default_rng(seed)
    orbit = [(rng.uniform(0, TAU), rng.uniform(0.3, 0.55), rng.uniform(0.012, 0.02)) for _ in range(8)]
    out = []
    for i in range(n):
        t = i / n                       # loop: frame n == frame 0
        f = frame(pal, px)
        pulse = 0.85 + 0.15 * cyc(t, 2)
        f.dot(0, 0, 0.3 * pulse, "fx", 0.7)
        f.dot(0, 0, 0.14 * pulse, "fx", 3.4, hard=0.55)
        for k in range(2):
            a = k * math.pi + TAU * t
            f.crescent(0, 0, 0.36, a, a + 1.5, 0.035, "fx", 1.6)
        f.ring(0, 0, 0.42, 0.012, "fx", 0.6 + 0.4 * cyc(t, 1))
        for a0, r, sz in orbit:
            a = a0 - TAU * t
            f.dot(math.cos(a) * r, math.sin(a) * r, sz, "fx", 2.0, hard=0.6)
        f.star4(0, 0, TAU * t / 4, 0.3 + 0.06 * cyc(t, 2), 0.016, "fx", 1.2)
        out.append(f.finish())
    return out


def aura_loop(pal, seed, n, px):
    """Seamless looping ground aura (buff / zone) in 3/4 view: steady double ring with turning rune
    ticks, a pulse ring that expands and fades every loop, motes rising off the ring."""
    rng = np.random.default_rng(seed)
    cy, sq = SIGIL_Y, SIGIL_SQ
    risers = [(rng.uniform(0, TAU), rng.uniform(0.35, 0.8), rng.uniform(0, 1), rng.uniform(0.6, 1.0)) for _ in range(14)]
    out = []
    for i in range(n):
        t = i / n
        f = frame(pal, px)
        f.ring(0, cy, 0.8, 0.02, "fx", 1.3, squash=sq)
        f.ring(0, cy, 0.66, 0.011, "fx", 1.0, squash=sq)
        f.disc(0, cy, 0.8, "fx", 0.14 + 0.08 * cyc(t, 1), edge=0.05, core=0.6, squash=sq)
        for k in range(18):
            a = k * TAU / 18 + (TAU / 18) * t          # one tick spacing per loop -> seamless
            xa, ya = math.cos(a) * 0.69, cy + math.sin(a) * 0.69 * sq
            xb, yb = math.cos(a) * 0.77, cy + math.sin(a) * 0.77 * sq
            f.needle(xa, ya, math.atan2(yb - ya, xb - xa), math.hypot(xb - xa, yb - ya), 0.011, "fx", 1.0,
                     back=0.01, power=0.3)
        u = t
        f.ring(0, cy, 0.3 + 0.55 * ease_out(u, 2.5), 0.02, "fx", 1.4 * math.sin(math.pi * u), squash=sq)
        for a, r, ph, h in risers:
            fr = (t + ph) % 1.0
            x = math.cos(a) * r
            y = cy + math.sin(a) * r * sq + fr * h
            f.dot(x, y, 0.016, "fx", 2.2 * math.sin(math.pi * fr), hard=0.6)
            f.needle(x, y, -math.pi / 2, 0.08, 0.008, "fx", 1.0 * math.sin(math.pi * fr))
        out.append(f.finish())
    return out


def aura_ascend(pal, seed, n, px):
    """Seamless looping growth buff (EXP / level gain) in 3/4 view: ground ring with a turning dashed inner
    ring and a pulse, two columns of up-chevrons climbing beside the hero, diamond crystals rising off the
    ring and twinkling at their peak. Pivot = feet."""
    rng = np.random.default_rng(seed)
    cy, sq = SIGIL_Y, SIGIL_SQ
    crystals = []
    for k in range(10):
        side = -1 if k % 2 else 1                                   # alternate sides, keep the centre (hero) clear
        a = rng.uniform(-0.75, 0.75) + (math.pi if side < 0 else 0.0)
        r = rng.uniform(0.5, 0.78)
        crystals.append((a, r, (k + rng.uniform(0, 0.6)) / 10, rng.uniform(0.75, 1.05), rng.uniform(0.95, 1.35)))
    step, y0, y1 = 0.24, cy + 0.14, 0.76                            # chevron spacing / spawn line / fade-out top
    out = []
    for i in range(n):
        t = i / n
        f = frame(pal, px)
        f.disc(0, cy, 0.8, "fx", 0.08 + 0.05 * cyc(t, 1), edge=0.05, core=0.6, squash=sq)
        f.ring(0, cy, 0.8, 0.02, "fx", 1.7 + 0.25 * cyc(t, 1), squash=sq)
        f.ring(0, cy, 0.64, 0.012, "fx", 1.1, squash=sq,
               mod=lambda th, t=t: 0.25 + 0.375 * (1 - np.cos(12 * th - TAU * t)))  # dashes turn one step per loop
        u = t
        f.ring(0, cy, 0.25 + 0.6 * ease_out(u, 2.5), 0.018, "fx", 1.3 * math.sin(math.pi * u), squash=sq)
        # up-chevrons: positions move exactly one spacing per loop and fade by height -> seamless
        for col, (x, ph) in enumerate(((-0.6, 0.0), (0.6, 0.5))):
            for k in range(-1, 6):
                y = y0 + (k + t + ph) * step
                env = window(y, y0 - 0.01, y0 + 0.2, y1 - 0.22, y1)
                if env <= 0:
                    continue
                w, h = 0.085, 0.065
                f.polyline([(x - w, y - h), (x, y), (x + w, y - h)], 0.017, "fx", 1.5 * env)
        # rising diamond crystals (EXP shards), each a phase-shifted loop
        for a, r, ph, hgt, sz in crystals:
            fr = (t + ph) % 1.0
            x = math.cos(a) * r
            y = cy + math.sin(a) * r * sq + fr * hgt
            edge = min(max((0.84 - y) / 0.18, 0.0), 1.0)
            env = math.sin(math.pi * fr) * edge
            if env <= 0.01:
                continue
            ln = 0.05 * sz
            f.needle(x, y, math.pi / 2, ln, 0.03 * sz, "fx", 2.1 * env, back=ln, power=1.2)
            f.dot(x, y, 0.03 * sz, "fx", 0.5 * env)
            tw = window(fr, 0.45, 0.62, 0.7, 0.88)
            if tw > 0:
                f.star4(x, y, 0.0, 0.11 * sz * tw, 0.008, "fx", 1.8 * tw * edge)
        out.append(f.finish())
    return out


# ============================================================================ METEOR STRIKE (multi-sheet)
# Pivot = impact point on the ground. Timeline: the head (looping comet, stretched billboard) falls for
# METEOR_FALL s while a ground mark tightens under it -> the impact sheet (main flipbook, delayed by
# METEOR_FALL) bursts in 3/4 view -> a molten crater (ground layer) cools down.
METEOR_FALL = 0.42                 # seconds from spawn to impact
METEOR_FROM = (3.0, 10.0)          # head start relative to the impact point (units, at the recipe size)
METEOR_W, METEOR_LS = 3.2, 2.2     # head quad width (units) and stretched-billboard length scale
HEAD_X = -0.55                     # head centre in the head sheet (U = 0.225 behind the particle)
HEAD_OVAL = 1.35                   # how elongated the head stays after Unity stretches the sheet LS x
GROUND_SQ = 0.42                   # 3/4 ground ellipse squash (same as the sigil / aura)
METEOR_BY = 0.12                   # fireball dome centre above the ground contact (normalised)


def meteor_head(pal, seed, n, px):
    """Seamless looping comet for a STRETCHED billboard: painted horizontally, white-hot head on the LEFT
    (Unity puts U=0 on the particle and stretches the quad METEOR_LS x behind it), so every round shape is
    pre-compressed along x. Flickering flame tongues + streaming specks wrap exactly once per loop."""
    rng = np.random.default_rng(seed)
    k = HEAD_OVAL / METEOR_LS                       # x compression of round features
    tongues = [(rng.uniform(-0.22, 0.22), rng.uniform(0.8, 1.35), rng.uniform(0.07, 0.12), rng.uniform(0, 1),
                int(rng.integers(1, 3))) for _ in range(7)]
    streams = [(rng.uniform(-0.2, 0.2), rng.uniform(0, 1), rng.uniform(0.016, 0.026)) for _ in range(16)]
    hx = HEAD_X
    out = []
    for i in range(n):
        t = i / n                                   # loop: frame n == frame 0
        f = frame(pal, px)
        pulse = 0.9 + 0.1 * cyc(t, 2)
        f.oval(hx + 0.12, 0, 0.25, 0.46, "fx", 0.4 * pulse)                     # soft orange halo
        f.petal(hx - 0.04, 0, 0.0, 1.4, 0.27, "fx", 0.7, hot=0.08)             # main tail body
        for dy, ln, w, ph, kk in tongues:                                      # flame tongues licking back
            fl = 0.75 + 0.25 * cyc(t, kk, ph)
            f.petal(hx + 0.02, dy * 0.5, dy * 0.35, ln * fl * 0.95, w, "fx", 0.42, hot=0.15)
        f.needle(hx, 0, 0.0, 1.1, 0.06, "fx", 1.5)                             # hot inner streak
        for s_ in (-1, 1):
            f.needle(hx + 0.05, s_ * 0.12, s_ * 0.05, 0.85, 0.02, "fx", 1.1)
        f.ring(hx + 0.08, 0, 0.34, 0.028, "fx", 1.1, sx=k,                     # bow shock on the leading side
               mod=lambda th: np.clip(-np.cos(th), 0, 1) ** 1.5)
        f.oval(hx, 0, 0.27 * k, 0.27, "fx", 1.1 * pulse)                        # head body
        f.oval(hx, 0, 0.17 * k, 0.17, "fx", 3.6, hard=0.55)                     # white-hot core
        for dy, ph, sz in streams:                                             # specks streaming off the tail
            fr = (t + ph) % 1.0
            x = hx + 0.12 + 1.35 * fr
            edge = min(1.0, max(0.0, (0.88 - x) / 0.15))
            f.dot(x, dy * (1 + 1.6 * fr), sz, "fx", 2.2 * math.sin(math.pi * fr) * edge, hard=0.5)
        out.append(f.finish())
    return out


def meteor_mark(pal, seed, n, px):
    """Ground warning under the falling head (3/4 ellipse at the pivot): a soft glow that heats up, a target
    ring, and a second ring + 4 chevrons tightening onto it, peaking at impact (~0.8 of life), then fading."""
    sq = GROUND_SQ
    tp = 0.8                                         # impact time as a fraction of the sheet life
    out = []
    for i in range(n):
        t = i / (n - 1)
        f = frame(pal, px)
        heat = ease_in(min(t / tp, 1.0), 1.6)
        live = window(t, -0.01, 0.12, tp, 1.0)
        f.disc(0, 0, 0.55, "fx", (0.12 + 0.5 * heat) * live, edge=0.08, core=0.25, squash=sq)
        f.ring(0, 0, 0.55, 0.014, "fx", (0.7 + 0.7 * heat) * live, squash=sq)
        u = min(t / tp, 1.0)
        R = 0.88 - 0.33 * ease_in(u, 1.4)
        tight = window(t, -0.01, 0.1, tp - 0.04, tp + 0.08)
        f.ring(0, 0, R, 0.012, "fx", 1.2 * tight, squash=sq)
        for a in (0.25 * math.pi, 0.75 * math.pi, 1.25 * math.pi, 1.75 * math.pi):   # inward-pointing ticks
            x0, y0 = math.cos(a) * (R + 0.1), math.sin(a) * (R + 0.1) * sq
            ang = math.atan2(-math.sin(a) * sq, -math.cos(a))
            f.needle(x0, y0, ang, 0.1, 0.028, "fx", 1.4 * tight, power=1.2)
        f.dot(0, 0, 0.2, "fx", 1.6 * window(t, tp - 0.15, tp, tp + 0.02, 1.0))
        out.append(f.finish())
    return out


def meteor_impact(pal, seed, n, px):
    """Meteor hitting the ground (3/4 view, pivot = contact point): white flash, light pillar and a lens streak
    along the ground; ground shock rings + a skirt of flame licks racing out; the fireball dome rises, hollows
    into a shell and dies as a thin soot rim; shards and specks last."""
    rng = np.random.default_rng(seed)
    sq, by = GROUND_SQ, METEOR_BY
    body_bumps = bumpy(rng, count=12, amp=0.10)
    rim_bumps = bumpy(rng, count=13, amp=0.12)
    spikes = spiky(rng, count=16, amp=0.5, width=0.22)          # <= 0.55: disc() boxes at 1.6 R, longer spikes get cut flat

    def flash_spikes(th):                                       # spikes pointing into the ground stay short
        return 1 + (spikes(th) - 1) * (0.25 + 0.75 * np.clip(np.sin(th) + 0.4, 0, 1))
    rays = [(rng.uniform(0, TAU), rng.uniform(0.55, 0.95), rng.uniform(0.008, 0.016)) for _ in range(24)]
    soot_break = breakup(rng, count=30, lo=-0.4, hi=1.2, sharp=1.0)
    skirt = [(a + rng.uniform(-0.1, 0.1), rng.uniform(0.6, 1.0), rng.uniform(0.022, 0.034))
             for a in np.linspace(0, TAU, 22, endpoint=False)]
    shards = [(rng.uniform(0.2, math.pi - 0.2), rng.uniform(2.2, 3.2), rng.uniform(0.08, 0.13)) for _ in range(10)]
    embers = [(rng.uniform(0, TAU), rng.uniform(0.9, 1.6), rng.uniform(0.012, 0.02)) for _ in range(14)]
    out = []
    for i in range(n):
        t = i / (n - 1)
        f = frame(pal, px, haze=True)
        # flash + pillar + ground lens streak (frame 0)
        fl = window(t, -0.01, 0.0, 0.06, 0.2)
        if fl > 0:
            rr = 0.24 + 0.2 * ease_out(t / 0.15)
            f.disc(0, by * 0.5, rr, "fx", 1.7 * fl, edge=0.012, core=0.6, bumps=flash_spikes)
            f.disc(0, by * 0.5, rr * 0.55, "fx", 3.2 * fl, edge=0.03, core=0.7)
        pil = window(t, -0.01, 0.0, 0.1, 0.32)
        f.beam(0, 0, 0, 0.9, 0.17 * (1 - 0.4 * t), "fx", 0.8 * pil)
        f.beam(0, 0, 0, 0.82, 0.05, "fx", 1.8 * pil)
        ls = window(t, -0.01, 0.0, 0.12, 0.35)
        for a in (0.0, math.pi):
            f.needle(0, 0, a, 0.62 + 0.24 * ease_out(t / 0.3), 0.03 * (1 - 0.5 * t), "fx", 2.4 * ls)
        # ground shock rings (3/4)
        for delay, rmax, inten in ((0.0, 0.84, 1.6), (0.1, 0.66, 1.1)):
            u = (t - delay) / 0.55
            if 0 < u < 1:
                R = 0.18 + (rmax - 0.18) * ease_out(u, 3.5)
                f.ring(0, 0, R, 0.018 + 0.014 * (1 - u), "fx", inten * (1 - smooth(u)), squash=sq)
                f.disc(0, 0, R, "fx", 0.25 * inten * (1 - smooth(u)), edge=0.05, core=0.0, power=3.0,
                       squash=sq, rim=1.0, rimw=0.06)
        # skirt of light streaks racing out along the ground
        sk = window(t, -0.01, 0.04, 0.22, 0.5)
        if sk > 0:
            d0 = 0.14 + 0.4 * ease_out(t / 0.45)
            for a, ln, w in skirt:
                ca, sa = math.cos(a), math.sin(a) * sq
                ang, m = math.atan2(sa, ca), math.hypot(ca, sa)
                L = min(ln * (0.22 + 0.16 * ease_out(t / 0.4)) * m, 0.86 - d0 * m)
                f.needle(ca * d0, sa * d0, ang, L, w * (1 - 0.4 * t), "fx", 1.6 * sk, back=0.05 * m, power=1.2)
        # fireball dome (rises, hollows into a shell)
        R = 0.2 + 0.34 * ease_out(t / 0.45, 4)
        heat = max(0.0, 1 - t / 0.7)
        cy = by + 0.06 * ease_out(t / 0.6)
        body = window(t, 0.0, 0.05, 0.4, 0.74)
        if body > 0:
            hollow = smooth((t - 0.15) / 0.4)
            f.disc(0, cy, R, "fx", (1.0 * heat + 0.35) * body, edge=0.02,
                   core=max(0.6 - 0.58 * hollow, 0.02), power=1.0 + 2.5 * hollow, bumps=body_bumps,
                   rim=0.6 + 0.9 * hollow, rimw=0.045 + 0.02 * hollow)
            f.disc(0, cy, R * (0.7 - 0.2 * hollow), "fx", 1.6 * heat * body * (1 - 0.9 * hollow),
                   edge=0.12, core=0.2, power=1.2, bumps=body_bumps)
            f.disc(0, cy, R * 0.95, "haze", 0.25 * body * hollow, edge=0.05, core=0.85, bumps=body_bumps)
        ray = window(t, 0.02, 0.07, 0.28, 0.5)
        for ang, ln, w in rays:
            r0 = R * 0.45
            reach = min(R * 0.7 + ln * 0.5 * ease_out(t / 0.35), 0.84 - cy - r0)
            f.needle(math.cos(ang) * r0, cy + math.sin(ang) * r0, ang, reach, w, "fx", 0.9 * ray)
        # shards thrown up and out, falling back (light, not rocks)
        sh = window(t, -0.01, 0.03, 0.22, 0.42)
        for a, v, ln in shards:
            tt = 0.12 + 0.6 * t
            x = math.cos(a) * v * tt * 0.5
            y = by + math.sin(a) * v * tt * 0.5 - 1.2 * tt * tt
            vx, vy = math.cos(a) * v * 0.5, math.sin(a) * v * 0.5 - 2.4 * tt
            edge = min(1.0, max(0.0, (0.86 - math.hypot(x, y)) / 0.12))
            f.needle(x, y, math.atan2(-vy, -vx), ln, 0.016, "fx", 1.8 * sh * edge)
        sp = window(t, 0.1, 0.28, 0.65, 1.0)
        f.specks(seed + 90, 34, 0, cy, 0.04, R * 0.95, 0.015, "fx", 3.0 * sp, twinkle=twinkles(i))
        em = window(t, 0.06, 0.15, 0.6, 0.95)
        for ang, spd, sz in embers:
            d = min(0.25 + spd * 0.38 * ease_out(t / 0.8, 3), 0.84)
            f.dot(math.cos(ang) * d, math.sin(ang) * d * (0.55 + 0.45 * sq), sz, "fx", 1.4 * em, hard=0.6)
        so = window(t, 0.32, 0.5, 0.74, 1.0)
        if so > 0:
            f.ink_ring(0, cy, R, 0.012, pal["soot"], 0.85 * so, bumps=rim_bumps, breakup=soot_break)
            f.ring(0, cy, R * 0.97, 0.016, "fx", 0.6 * so * (1 - smooth((t - 0.55) / 0.35)), mod=soot_break)
        out.append(f.finish())
    return out


def meteor_crater(pal, seed, n, px):
    """Molten crater left on the ground (ground layer, 3/4 view): glowing pool + jagged branching cracks that cool from
    white to deep red, a faint scorch and a thin rim that fade out."""
    rng = np.random.default_rng(seed)
    sq = GROUND_SQ
    cracks = []
    for k in range(8):
        a = k * TAU / 8 + rng.uniform(-0.25, 0.25)
        pts, r = [(0.0, 0.0)], 0.0
        L = rng.uniform(0.55, 0.8)
        while r < L:
            r = min(r + rng.uniform(0.09, 0.15), L)
            aa = a + rng.uniform(-0.28, 0.28)
            pts.append((math.cos(aa) * r, math.sin(aa) * r * sq))
        br = None
        if rng.uniform() < 0.6:                       # one side branch
            j = int(rng.integers(1, max(2, len(pts) - 1)))
            bx, by_ = pts[j]
            ba = a + rng.choice([-1, 1]) * rng.uniform(0.5, 0.9)
            bl = rng.uniform(0.12, 0.22)
            br = [(bx, by_), (bx + math.cos(ba) * bl, by_ + math.sin(ba) * bl * sq)]
        cracks.append((pts, br))
    def crack(pts, w0, inten):
        """Jagged crack that tapers from w0 at its root to a sharp tip (needle per segment)."""
        nseg = len(pts) - 1
        for j, ((x0, y0), (x1, y1)) in enumerate(zip(pts[:-1], pts[1:])):
            w = w0 * (1 - 0.75 * j / nseg)
            ang, ln = math.atan2(y1 - y0, x1 - x0), math.hypot(x1 - x0, y1 - y0)
            f.needle(x0, y0, ang, ln + w * 0.5, w, "fx", inten, back=w * 0.5,
                     power=0.25 if j < nseg - 1 else 0.9)
    out = []
    for i in range(n):
        t = i / (n - 1)
        f = frame(pal, px)
        cool = 1 - ease_out(t / 0.85, 2.5)                 # 1 = white-hot, 0 = cold
        live = window(t, -0.01, 0.0, 0.45, 1.0)
        f.disc(0, 0, 0.32, "fx", (0.35 + 0.9 * cool) * live, edge=0.08, core=0.1, power=1.8, squash=sq)
        f.oval(0, 0, 0.13, 0.13 * sq, "fx", (0.6 + 2.2 * cool) * live, hard=0.4)
        for pts, br in cracks:
            crack(pts, 0.013 + 0.005 * cool, (0.75 + 1.1 * cool) * live)
            if br is not None:
                crack(br, 0.009, (0.6 + 0.9 * cool) * live)
        f.ring(0, 0, 0.66, 0.016, "fx", 0.9 * window(t, -0.01, 0.0, 0.15, 0.45), squash=sq)
        out.append(f.finish())
    return out


def _meteor_head_layer():
    """Head layer: the particle starts so that the painted head centre travels METEOR_FROM -> impact point in
    METEOR_FALL s (stretched billboards put the particle at U=0, i.e. ahead of the head centre)."""
    sx, sy = METEOR_FROM
    dist = math.hypot(sx, sy)
    dx, dy = -sx / dist, -sy / dist
    lag = (HEAD_X + 1) / 2 * METEOR_W * METEOR_LS
    return dict(name="Head", role="fire", fn=meteor_head, loop=True, frames=8, cols=4, px=256, life=METEOR_FALL, cycle=0.21,
                size=METEOR_W, position=[round(sx + dx * lag, 4), round(sy + dy * lag, 4)],
                velocity=[round(-sx / METEOR_FALL, 4), round(-sy / METEOR_FALL, 4)], lengthScale=METEOR_LS,
                fade=[0.14, 0.0], grow=[0.8, 1.0], sorting_order=14, trailBack=round(lag, 4),
                trail=[_motes("birth", (0.3, 0.55), (0.6, 1.0), rate=60, speed=(0.4, 1.6), radius=0.35,
                              gravity=-0.1, drag=2.0, noise=0.5),
                       _motes("birth", (0.14, 0.28), (0.26, 0.4), rate=36, speed=(3.0, 7.0), radius=0.3,
                              drag=3.0, stretch=True)])


# ============================================================================ BLADE STORM (spinning ultimate zone)
# Painted TOP-DOWN as a circle and NOT rotated in the frames: the prefab squashes the flipbook to the 3/4 ground
# ellipse (spec `squash`) and turns the particle (spec `spin`, deg/s CCW), so the whirl spins perfectly smoothly at
# any speed with a few frames. Unity applies the transform scale after the particle rotation (verified 6000.3), so a
# turning circle stays a correct ground ellipse. Blades lead counter-clockwise: keep `spin` > 0.
STORM_R = 0.7          # blade radius (normalised); the broken boundary ring sits at 0.85 = the hit zone edge


def _loop_twinkle(t, k, phase):
    """Seamless per-speck shimmer for loops (integer k)."""
    return 0.55 + 0.45 * math.sin(TAU * (k * t + phase))


def blade_storm(pal, seed, n, px):
    """Looping blade whirl around a spinning hero: 3 swoosh blades heavy at their CCW-leading heads with a
    white-hot outer edge, inner echo arcs, wind streaks, a broken boundary ring (hit zone), tip glints, specks.
    The frames only breathe and shimmer; the spin comes from the particle rotation."""
    rng = np.random.default_rng(seed)
    K, R = 3, STORM_R
    gaps = breakup(rng, 28, 0.25, 1.0, 1.5)
    winds = [(rng.uniform(0, TAU), rng.uniform(0.5, 0.82), rng.uniform(0.7, 1.4), rng.uniform(0, 1)) for _ in range(9)]
    specks = [(rng.uniform(0, TAU), rng.uniform(0.32, 0.86), rng.uniform(0.008, 0.015), rng.uniform(0, 1),
               int(rng.integers(1, 3))) for _ in range(24)]
    out = []
    for i in range(n):
        t = i / n                       # loop: frame n == frame 0
        f = frame(pal, px)
        f.ring(0, 0, 0.85, 0.007, "fx", 0.9 + 0.2 * cyc(t, 2), mod=gaps)
        f.ring(0, 0, 0.85, 0.028, "fx", 0.16)
        f.ring(0, 0, 0.44, 0.005, "fx", 0.4 + 0.15 * cyc(t, 1, 0.5), mod=gaps, rot=1.3)
        for a0, r, span, ph in winds:
            f.crescent(0, 0, r, a0, a0 + span, 0.012, "fx", 0.9 * (0.35 + 0.65 * cyc(t, 1, ph)), peak=0.7)
        for k in range(K):
            br = 0.88 + 0.12 * cyc(t, 1, k / K)
            head = k * TAU / K + 0.9
            tail = head - (1.9 + 0.22 * cyc(t, 1, k / K + 0.25))
            f.crescent(0, 0, R - 0.015, tail, head, 0.075, "fx", 0.6 * br, peak=0.86)         # soft blade body
            f.crescent(0, 0, R + 0.02, tail + 0.45, head, 0.026, "fx", 2.9 * br, peak=0.88)   # white-hot edge
            f.crescent(0, 0, R + 0.07, tail + 1.0, head - 0.04, 0.009, "fx", 1.8 * br, peak=0.82)  # razor line
            ah = head - 0.62                                                                   # afterimage blade
            f.crescent(0, 0, R - 0.035, ah - 1.0, ah, 0.02, "fx", 0.9 * br, peak=0.85)
            eh = head - TAU / (2 * K) + 0.15                                                   # echo, half a step back
            f.crescent(0, 0, R - 0.19, eh - 1.1, eh, 0.04, "fx", 0.45 * br, peak=0.8)
            f.crescent(0, 0, R - 0.18, eh - 0.85, eh, 0.014, "fx", 1.6 * br, peak=0.82)
            hx, hy = math.cos(head + 0.05) * (R + 0.03), math.sin(head + 0.05) * (R + 0.03)
            f.star4(hx, hy, head + 0.6, 0.12 * (0.75 + 0.25 * cyc(t, 2, k / K)), 0.011, "fx",
                    1.7 * _loop_twinkle(t, 2, k / K), short=0.5)
        for a, d, s, ph, kk in specks:
            f.dot(math.cos(a) * d, math.sin(a) * d, s, "fx", 2.2 * _loop_twinkle(t, kk, ph), hard=0.6)
        out.append(f.finish())
    return out


# ============================================================================ POWER-UP PACK
# Heal zone, time-slow pulse, shield bubble, invincibility aura, ground slam. Same light vocabulary as the
# approved pack.

def aura_heal(pal, seed, n, px):
    """Seamless looping healing zone on the ground (3/4 view, pivot = feet / zone centre): soft fill, bright edge
    ring = the heal radius (0.8), dashed inner ring turning one dash per loop, a pulse that draws INWARD, plus-crosses
    rising from inside the zone and fading before the frame top, motes riding up off the edge."""
    rng = np.random.default_rng(seed)
    cy, sq = SIGIL_Y, SIGIL_SQ
    crosses = []
    for k in range(9):
        a, r = rng.uniform(0, TAU), rng.uniform(0.12, 0.66)
        crosses.append((a, r, (k + rng.uniform(0, 0.7)) / 9, rng.uniform(0.62, 0.95), rng.uniform(0.85, 1.25)))
    risers = [(rng.uniform(0, TAU), rng.uniform(0, 1), rng.uniform(0.45, 0.8)) for _ in range(12)]
    out = []
    for i in range(n):
        t = i / n                       # loop: frame n == frame 0
        f = frame(pal, px)
        f.disc(0, cy, 0.8, "fx", 0.2 + 0.07 * cyc(t, 1), edge=0.05, core=0.55, squash=sq)
        f.ring(0, cy, 0.8, 0.022, "fx", 1.9 + 0.3 * cyc(t, 1), squash=sq)
        f.ring(0, cy, 0.67, 0.012, "fx", 1.1, squash=sq,
               mod=lambda th, t=t: 0.2 + 0.4 * (1 - np.cos(16 * th - TAU * t)))   # 16 dashes, one dash per loop
        u = t                                                                       # inward pulse (healing gathers)
        f.ring(0, cy, 0.78 - 0.56 * ease_out(u, 2.2), 0.018, "fx", 1.2 * math.sin(math.pi * u), squash=sq)
        for a, r, ph, h, sz in crosses:
            fr = (t + ph) % 1.0
            x = math.cos(a) * r
            y = cy + math.sin(a) * r * sq + fr * h
            edge = min(max((0.84 - y) / 0.18, 0.0), 1.0)
            env = math.sin(math.pi * fr) * edge
            if env <= 0.01:
                continue
            L, w = 0.05 * sz, 0.022 * sz
            f.needle(x, y, 0.0, L, w, "fx", 1.9 * env, back=L, power=0.35)            # plus: horizontal bar
            f.needle(x, y, math.pi / 2, L, w, "fx", 1.9 * env, back=L, power=0.35)    # plus: vertical bar
            f.dot(x, y, 0.05 * sz, "fx", 0.35 * env)
        for a, ph, h in risers:
            fr = (t + ph) % 1.0
            x, y = math.cos(a) * 0.8, cy + math.sin(a) * 0.8 * sq + fr * h
            env = math.sin(math.pi * fr) * min(max((0.84 - y) / 0.18, 0.0), 1.0)
            f.dot(x, y, 0.015, "fx", 2.2 * env, hard=0.6)
            f.needle(x, y, -math.pi / 2, 0.07, 0.008, "fx", 0.9 * env)
        out.append(f.finish())
    return out


def time_ripple(pal, seed, n, px):
    """One-shot 'time slows down' pulse (pivot = centre): flash + lens star on frame 0, a clock face whose two
    hands whirl BACKWARDS and visibly brake to a stop, ticks drifting back, three thin rings rolling outward one
    after another (each slower than the last), sand specks drifting off."""
    out = []
    hands = ((0.34, 0.022, 0.0, 1.0), (0.24, 0.03, 2.1, 0.45))           # length, width, start angle, spin share
    for i in range(n):
        t = i / (n - 1)
        f = frame(pal, px)
        fl = window(t, -0.01, 0.0, 0.06, 0.22)
        if fl > 0:
            f.dot(0, 0, 0.17, "fx", 3.2 * fl)
            f.star4(0, 0, 0.0, 0.22 + 0.4 * ease_out(t / 0.15), 0.02, "fx", 2.0 * fl)
        face = window(t, -0.01, 0.05, 0.5, 0.82)
        f.disc(0, 0, 0.42, "fx", 0.05 * face, edge=0.04, core=0.45)
        f.ring(0, 0, 0.42, 0.016, "fx", 1.5 * face)
        back = 0.45 * ease_out(t, 3)                                      # ticks drift counter-clockwise
        for k in range(12):
            a = math.pi / 2 + k * TAU / 12 + back
            r0 = 0.29 if k % 3 == 0 else 0.33
            f.needle(math.cos(a) * r0, math.sin(a) * r0, a, 0.39 - r0, 0.02 if k % 3 == 0 else 0.013, "fx",
                     1.3 * face, power=0.4)
        spin = TAU * 1.5 * ease_out(t / 0.7, 3)                           # hands whirl backwards, then brake
        for L, w, a0, share in hands:
            a = math.pi / 2 + a0 + spin * share
            f.needle(0, 0, a, L, w, "fx", 1.8 * face, back=0.04, power=0.8)
        f.dot(0, 0, 0.045, "fx", 2.6 * face, hard=0.5)
        for d, inten, dur in ((0.0, 1.7, 0.55), (0.12, 1.25, 0.62), (0.24, 0.9, 0.7)):
            u = (t - d) / dur
            if 0 <= u < 1:
                R = 0.2 + 0.66 * ease_out(u, 2.6)
                f.ring(0, 0, R, 0.02 + 0.012 * (1 - u), "fx", inten * (1 - smooth(u)) * min(1.0, 0.35 + u / 0.08))
        sp = window(t, 0.05, 0.25, 0.65, 1.0)
        f.specks(seed, 40, 0, 0, 0.1, 0.28 + 0.52 * ease_out(t, 2), 0.013, "fx", 2.6 * sp, twinkle=twinkles(i))
        out.append(f.finish())
    return out


def shield_bubble(pal, seed, n, px):
    """Seamless looping energy shield around the hero (pivot = body centre, drawn in front of him): clear centre,
    faint hexagon facets lit by a highlight band that sweeps one turn per loop, crisp rim with a breathing inner
    glow, three lightning arcs crawling one third of a turn per loop, a fixed specular crescent, orbiting specks."""
    rng = np.random.default_rng(seed)
    R, s = 0.74, 0.16
    segs = []                                                           # flat-top hex lattice, 3 edges per cell
    for q in range(-7, 8):
        for r in range(-7, 8):
            cx, cy = s * 1.5 * q, s * math.sqrt(3) * (r + q / 2)
            for k in range(3):
                a0, a1 = k * math.pi / 3, (k + 1) * math.pi / 3
                p0 = (cx + s * math.cos(a0), cy + s * math.sin(a0))
                p1 = (cx + s * math.cos(a1), cy + s * math.sin(a1))
                if math.hypot(*p0) < R - 0.04 and math.hypot(*p1) < R - 0.04:
                    mx, my = (p0[0] + p1[0]) / 2, (p0[1] + p1[1]) / 2
                    segs.append((p0, p1, math.atan2(my, mx), math.hypot(mx, my)))
    orbit = [(rng.uniform(0, TAU), rng.uniform(R + 0.02, R + 0.07), rng.uniform(0.01, 0.017)) for _ in range(10)]
    jag = [rng.uniform(-1, 1, 9) for _ in range(n)]                      # per-frame arc zigzag (flicker)
    out = []
    for i in range(n):
        t = i / n
        f = frame(pal, px)
        f.disc(0, 0, R, "fx", 0.22 + 0.06 * cyc(t, 2), edge=0.03, core=0.0, power=3.5, rim=1.0, rimw=0.1)
        f.ring(0, 0, R, 0.018, "fx", 1.7 + 0.3 * cyc(t, 2))
        band = TAU * t
        for (x0, y0), (x1, y1), ang, rad in segs:
            d = math.atan2(math.sin(ang - band), math.cos(ang - band))
            lit = (0.22 + 1.1 * math.exp(-(d / 0.55) ** 2)) * smooth((rad - 0.12) / 0.38)   # centre stays clear
            if lit < 0.02:
                continue
            f.needle(x0, y0, math.atan2(y1 - y0, x1 - x0), math.hypot(x1 - x0, y1 - y0), 0.008, "fx", lit,
                     back=0.004, power=0.25)
        for k in range(3):
            a = k * TAU / 3 + TAU / 3 * t
            pts = []
            for j in range(9):
                aa = a - 0.38 + 0.76 * j / 8
                rr = R + 0.012 + 0.06 * jag[i][j] * math.sin(math.pi * j / 8)
                pts.append((math.cos(aa) * rr, math.sin(aa) * rr))
            f.polyline(pts, 0.01, "fx", 2.7 * (0.7 + 0.3 * _loop_twinkle(t, 3, k / 3)))
        f.crescent(0, 0, R * 0.84, 1.95, 2.75, 0.032, "fx", 0.95, peak=0.5)       # specular highlight (top-left)
        f.crescent(0, 0, R * 0.84, 5.2, 5.6, 0.016, "fx", 0.5, peak=0.5)
        for a0, r, sz in orbit:
            a = a0 + TAU * t
            f.dot(math.cos(a) * r, math.sin(a) * r, sz, "fx", 2.0 * _loop_twinkle(t, 2, a0 / TAU), hard=0.6)
        out.append(f.finish())
    return out


def divine_aura(pal, seed, n, px):
    """Seamless looping invincibility aura drawn BEHIND the hero (pivot = body centre): breathing body glow, a
    sunburst of long/short god-rays turning one ray step per loop, a bright halo ring above the head, a 3/4 ground
    ring at the feet with a dashed inner ring and an outward pulse, 4-point sparkles rising and twinkling."""
    rng = np.random.default_rng(seed)
    nr, hy, gy = 16, 0.62, -0.6
    sparkles = [(rng.uniform(-0.6, 0.6), (k + rng.uniform(0, 0.6)) / 10, rng.uniform(0.8, 1.25)) for k in range(10)]
    out = []
    for i in range(n):
        t = i / n
        f = frame(pal, px)
        f.oval(0, 0.05, 0.22, 0.42, "fx", 0.14 + 0.05 * cyc(t, 2))
        rot = (TAU / nr) * 2 * t                                        # two ray steps per loop (rays alternate)
        for k in range(nr):
            a = rot + k * TAU / nr
            longray = k % 2 == 0
            r0 = 0.3
            L = min(0.52 if longray else 0.32, 0.84 - r0)
            down = smooth((math.sin(a) + 0.75) / 0.55)                 # rays fade out pointing into the ground ring
            inten = (1.15 if longray else 0.8) * (0.7 + 0.3 * cyc(t, 2, k / nr)) * down
            if inten < 0.02:
                continue
            f.needle(math.cos(a) * r0, math.sin(a) * r0, a, L, 0.032 if longray else 0.02, "fx", inten, power=1.4)
        f.disc(0, hy, 0.18, "fx", 0.35, edge=0.04, core=0.25, squash=0.32)
        f.ring(0, hy, 0.18, 0.017, "fx", 2.1 + 0.4 * cyc(t, 2), squash=0.32)
        f.ring(0, gy, 0.5, 0.018, "fx", 1.5, squash=SIGIL_SQ)
        f.ring(0, gy, 0.37, 0.011, "fx", 1.0, squash=SIGIL_SQ,
               mod=lambda th, t=t: 0.2 + 0.4 * (1 - np.cos(12 * th + TAU * t)))
        f.ring(0, gy, 0.15 + 0.38 * ease_out(t, 2.5), 0.016, "fx", 1.1 * math.sin(math.pi * t), squash=SIGIL_SQ)
        for x0, ph, sz in sparkles:
            fr = (t + ph) % 1.0
            y = gy + 0.05 + fr * 1.3
            env = math.sin(math.pi * fr) * min(max((0.84 - y) / 0.15, 0.0), 1.0)
            if env <= 0.01:
                continue
            f.star4(x0, y, 0.0, 0.075 * sz * env, 0.008, "fx", 1.9 * env, short=0.5)
            f.dot(x0, y, 0.012, "fx", 1.8 * env, hard=0.6)
        out.append(f.finish())
    return out


def ground_slam(pal, seed, n, px):
    """One-shot stomp (pivot = stomp centre): flash + lens streak along the ground on frame 0, jagged cracks racing
    out on the ground (3/4), a crisp ROUND shock ring rolling out to the push radius (0.82) then fading, a squashed
    inner wave, light shards thrown up and falling back, specks. Light only, no rocks."""
    rng = np.random.default_rng(seed)
    sq = GROUND_SQ
    cracks = []
    for k in range(7):
        a = k * TAU / 7 + rng.uniform(-0.3, 0.3)
        pts, r, L = [(0.0, 0.0)], 0.0, rng.uniform(0.38, 0.58)
        while r < L:
            r = min(r + rng.uniform(0.08, 0.13), L)
            aa = a + rng.uniform(-0.3, 0.3)
            pts.append((math.cos(aa) * r, math.sin(aa) * r * sq))
        cracks.append(pts)
    shards = [(rng.uniform(0.35, math.pi - 0.35), rng.uniform(2.0, 3.0), rng.uniform(0.09, 0.14)) for _ in range(9)]
    out = []
    for i in range(n):
        t = i / (n - 1)
        f = frame(pal, px)
        fl = window(t, -0.01, 0.0, 0.06, 0.22)
        if fl > 0:
            f.oval(0, 0, 0.3, 0.3 * sq, "fx", 2.4 * fl)
            f.dot(0, 0, 0.12, "fx", 3.2 * fl)
        ls = window(t, -0.01, 0.0, 0.1, 0.32)
        for a in (0.0, math.pi):
            f.needle(0, 0, a, 0.5 + 0.2 * ease_out(t / 0.3), 0.028 * (1 - 0.5 * t), "fx", 2.2 * ls)
        u = t / 0.55
        R = 0.16 + 0.66 * ease_out(u, 2.8)                                 # 0.82 = push radius
        ring_on = window(t, -0.01, 0.0, 0.32, 0.78)
        f.ring(0, 0, R, 0.022 + 0.016 * (1 - min(u, 1.0)), "fx", 1.9 * ring_on)
        f.ring(0, 0, R, 0.04, "fx", 0.18 * ring_on)
        u2 = (t - 0.06) / 0.5
        if 0 < u2 < 1:
            R2 = 0.12 + 0.5 * ease_out(u2, 3)
            f.ring(0, 0, R2, 0.02, "fx", 1.2 * (1 - smooth(u2)), squash=sq)
            f.disc(0, 0, R2, "fx", 0.3 * (1 - smooth(u2)), edge=0.05, core=0.0, power=3.0, squash=sq, rim=1.0,
                   rimw=0.06)
        reveal = ease_out(t / 0.22, 3)
        cr = window(t, -0.01, 0.02, 0.4, 0.85)
        for pts in cracks:
            nseg = len(pts) - 1
            run = reveal * nseg                                         # segments drawn so far (continuous)
            for j, ((x0, y0), (x1, y1)) in enumerate(zip(pts[:-1], pts[1:])):
                part = min(max(run - j, 0.0), 1.0)
                if part <= 0:
                    break
                w = 0.016 * (1 - 0.6 * j / max(1, nseg - 1))
                f.needle(x0, y0, math.atan2(y1 - y0, x1 - x0), math.hypot(x1 - x0, y1 - y0) * part + w * 0.5, w,
                         "fx", 1.7 * cr, back=w * 0.5, power=0.3)
        sh = window(t, -0.01, 0.03, 0.3, 0.55)
        for a, v, ln in shards:
            tt = 0.1 + 0.6 * t
            x = math.cos(a) * v * tt * 0.45
            y = math.sin(a) * v * tt * 0.45 - 1.3 * tt * tt
            vx, vy = math.cos(a) * v * 0.45, math.sin(a) * v * 0.45 - 2.6 * tt
            edge = min(1.0, max(0.0, (0.86 - math.hypot(x, y)) / 0.12))
            f.needle(x, y, math.atan2(-vy, -vx), ln, 0.01, "fx", 1.9 * sh * edge)
        sp = window(t, 0.08, 0.25, 0.6, 1.0)
        f.specks(seed, 30, 0, 0, 0.1, min(R, 0.84), 0.014, "fx", 2.6 * sp, squash=0.7, twinkle=twinkles(i))
        out.append(f.finish())
    return out


def dagger_sprite(pal, px=128):
    """Thrown energy dagger for a stretched billboard: tip on the LEFT (U=0 = the particle), blade in the element
    colour, gold guard + grip, dim streak behind. Unity stretches x by lengthScale, so it is painted x-compressed."""
    gold = palette("gold")
    f = Frame(px, layers={"fx": dict(lut=pal["lut"], alpha_gain=2.2, bloom=((0.03, 0.45), (0.09, 0.2))),
                          "hilt": dict(lut=gold["lut"], alpha_gain=2.4, bloom=((0.03, 0.35),))})
    f.needle(0.32, 0, 0.0, 0.56, 0.09, "fx", 0.5, power=1.4)          # streak behind
    f.needle(-0.02, 0, math.pi, 0.86, 0.36, "fx", 1.05, power=0.75)    # blade glow body
    f.needle(-0.02, 0, math.pi, 0.84, 0.15, "fx", 3.2, power=0.85)     # white-hot blade core
    f.oval(0.02, 0, 0.03, 0.42, "hilt", 1.9)                           # cross-guard
    f.oval(0.14, 0, 0.1, 0.11, "hilt", 1.3)                            # grip
    f.dot(0.27, 0, 0.075, "hilt", 2.2, hard=0.4)                       # pommel
    return f.finish()


# ============================================================================ particle sprites (white, tinted in Unity)
WHITE = ramp([(0.0, "#ffffff"), (1.0, "#ffffff")])


def spark_sprite(px=128):
    """Needle for stretched billboards: Unity maps U along the velocity with the particle at U=0,
    so the hot head sits on the LEFT and the tail tapers to the right."""
    f = Frame(px, layers={"fx": dict(lut=WHITE, alpha_gain=2.0, bloom=((0.04, 0.35),))})
    f.needle(-0.86, 0, 0.0, 1.72, 0.14, "fx", 2.2, power=1.3)
    f.dot(-0.8, 0, 0.12, "fx", 1.6)
    return f.finish()


def mote_sprite(px=64):
    f = Frame(px, layers={"fx": dict(lut=WHITE, alpha_gain=2.0, bloom=((0.08, 0.5),))})
    f.dot(0, 0, 0.22, "fx", 2.6, hard=0.45)
    f.dot(0, 0, 0.55, "fx", 0.35)
    return f.finish()


# ============================================================================ registry
def _sparks(count, life, speed, size, arc=360.0, radius=0.2, delay=0.0, drag=4.0, gravity=0.0, blend="ab"):
    return dict(type="sparks", count=count, life=list(life), speed=list(speed), size=list(size), arc=arc,
                radius=radius, delay=delay, drag=drag, gravity=gravity, blend=blend)


def _motes(mode, life, size, count=0, rate=0.0, speed=(0.0, 0.0), radius=0.5, scaleY=1.0, delay=0.0,
           duration=0.0, rise=(0.0, 0.0), gravity=0.0, drag=0.0, noise=0.0, stretch=False, blend="ab"):
    # blend: "ab" = lop nen (_mat_ab), "add" = lop cong sang phu len tren (_mat_add) - game-vfx 8.4
    return dict(type="motes", mode=mode, count=count, rate=rate, life=list(life), speed=list(speed), size=list(size),
                radius=radius, scaleY=scaleY, delay=delay, duration=duration, rise=list(rise), gravity=gravity,
                drag=drag, noise=noise, stretch=stretch, blend=blend)


def _stream(name, fn, rate, life, speed, size, radius=0.5, arc_speed=0.0, orbital=0.0, drag=0.0, length_scale=2.5,
            squash=1.0, fade=(0.1, 0.35), sorting_order=12, role="debris"):
    """Looping emitter of a painted sprite `fn(pal)` (thrown daggers…) flung out of a circle, stretched along its
    velocity. arc_speed > 0: the emission point sweeps the circle CCW (loops/s); orbital: CCW curve of the flight (deg/s)."""
    return dict(type="streams", name=name, role=role, fn=fn, rate=rate, life=list(life), speed=list(speed), size=list(size),
                radius=radius, arcSpeed=arc_speed, orbital=orbital, drag=drag, lengthScale=length_scale, squash=squash,
                fade=list(fade), sortingOrder=sorting_order)


# kind: impact | muzzle | explode | cast | slash | projectile | aura  (preview framing, QA, compare references)
# group: prefab name group (game-vfx 8.2, overridable with --group); role: main flipbook layer role (8.4)
# offset: flipbook quad offset in units of size so the effect's anchor sits on the prefab pivot
RECIPES = {
    "impact_star": dict(fn=impact_star, group="hit", role="impact", seed=21, kind="impact", frames=12, cols=4, px=256, life=0.34, size=4.2,
                        random_rotation=True, extras=[_sparks(7, (0.16, 0.3), (9, 16), (0.14, 0.22))],
                        desc="Hit spark: needle star, ring, crescents, shards"),
    "impact_shatter": dict(fn=impact_shatter, group="hit", role="impact", seed=22, kind="impact", frames=12, cols=4, px=256, life=0.36, size=4.2,
                           random_rotation=True, extras=[_sparks(7, (0.16, 0.3), (9, 16), (0.14, 0.22))],
                           desc="Crisp hit: lens streak, double ring, diamond shards"),
    "muzzle_flower": dict(fn=muzzle_flower, group="muzzle", role="fire", seed=31, kind="muzzle", frames=8, cols=4, px=256, life=0.16, size=4.0,
                          offset=[0.31, 0.0], align_local=True,
                          extras=[_sparks(5, (0.08, 0.18), (12, 20), (0.12, 0.18), arc=40, radius=0.05)],
                          desc="Gun flash pointing +X: 5 flame petals, needles, shock ring"),
    "muzzle_beam": dict(fn=muzzle_beam, group="muzzle", role="flash", seed=32, kind="muzzle", frames=8, cols=4, px=256, life=0.18, size=4.0,
                        offset=[0.31, 0.0], align_local=True,
                        extras=[_sparks(5, (0.08, 0.18), (12, 20), (0.12, 0.18), arc=40, radius=0.05)],
                        desc="Energy discharge pointing +X: orb, beam needle, ring cone"),
    "explode_burst": dict(fn=explode_burst, group="hit", role="fire", seed=11, kind="explode", frames=16, cols=4, px=320, life=0.72, size=8.5,
                          random_rotation=True,
                          extras=[_sparks(12, (0.3, 0.55), (10, 20), (0.18, 0.28)),
                                  _motes("drift", (0.6, 1.0), (0.2, 0.34), count=10, speed=(1, 3.5), radius=1.5,
                                         delay=0.08, gravity=-0.15, drag=1.5, noise=0.6)],
                          desc="Fireball: flash, scalloped ball, rays, hollow shell, soot rim"),
    "explode_nova": dict(fn=explode_nova, group="hit", role="shockwave", seed=12, kind="explode", frames=16, cols=4, px=320, life=0.72, size=8.5,
                         random_rotation=True,
                         extras=[_sparks(12, (0.3, 0.55), (10, 20), (0.18, 0.28)),
                                 _motes("drift", (0.6, 1.0), (0.2, 0.34), count=10, speed=(1, 3.5), radius=1.5,
                                        delay=0.08, gravity=-0.15, drag=1.5, noise=0.6)],
                         desc="Energy nova: spiky shell, crystal needles, outer ring"),
    "cast_sigil": dict(fn=cast_sigil, group="cast", role="ring", seed=41, kind="cast", frames=20, cols=5, px=320, life=1.1, size=7.5,
                       offset=[0.0, -SIGIL_Y / 2],
                       extras=[_motes("rise", (0.6, 0.9), (0.15, 0.28), rate=22, radius=2.7, scaleY=0.42,
                                      delay=0.22, duration=0.6, rise=(1.6, 2.8))],
                       desc="Ground magic circle + pillar + rim flames; pivot = feet"),
    "cast_charge": dict(fn=cast_charge, group="cast", role="glow", seed=42, kind="cast", frames=16, cols=4, px=320, life=0.9, size=6.5,
                        extras=[_motes("rise", (0.6, 0.9), (0.15, 0.28), rate=22, radius=1.4, delay=0.18,
                                       duration=0.5, rise=(1.6, 2.8)),
                                _sparks(12, (0.25, 0.45), (8, 14), (0.16, 0.24), delay=0.54)],
                        desc="Charge & release: spiral motes into orb, gyroscope rings, star burst"),
    "slash_arc": dict(fn=slash_arc, group="slash", role="trail", seed=51, kind="slash", frames=10, cols=5, px=320, life=0.26, size=5.0,
                      offset=[0.0, 0.0], align_local=True,
                      extras=[_sparks(6, (0.12, 0.25), (8, 14), (0.12, 0.2), arc=120, radius=1.4)],
                      desc="Melee swipe facing +X: sweeping crescent, peeling sparkles"),
    "orb_loop": dict(fn=orb_loop, group="proj", role="glow", seed=61, kind="projectile", frames=8, cols=4, px=256, life=0.4, size=2.2, loop=True,
                     extras=[_motes("trail", (0.25, 0.45), (0.14, 0.24), rate=14, radius=0.15)],
                     desc="Looping projectile body + world-space trail motes"),
    "aura_loop": dict(fn=aura_loop, group="aura", role="ring", seed=71, kind="aura", frames=12, cols=4, px=320, life=1.2, size=5.0, loop=True,
                      offset=[0.0, -SIGIL_Y / 2], sorting_layer="ground",
                      extras=[_motes("rise", (0.7, 1.1), (0.12, 0.22), rate=8, radius=1.8, scaleY=0.42,
                                     rise=(1.0, 1.8))],
                      desc="Looping ground aura / buff zone in 3/4 view; pivot = feet"),
    "aura_ascend": dict(fn=aura_ascend, group="buff", role="ring", seed=72, kind="aura", frames=16, cols=4, px=320, life=1.2, size=5.0, loop=True,
                        offset=[0.0, -SIGIL_Y / 2], sorting_layer="ground", intro=0.3,
                        extras=[_motes("rise", (0.8, 1.2), (0.12, 0.22), rate=9, radius=1.8, scaleY=0.42,
                                       rise=(1.4, 2.4)),
                                _motes("rise", (0.45, 0.7), (0.1, 0.16), rate=6, radius=1.6, scaleY=0.42,
                                       rise=(3.0, 4.2), stretch=True)],
                        desc="Looping growth buff (EXP / level gain) in 3/4 view: ground ring, climbing up-chevrons, "
                             "rising diamond crystals that twinkle; pivot = feet"),
    "meteor_strike": dict(fn=meteor_impact, group="skill", role="impact", seed=81, kind="explode", frames=20, cols=5, px=320, life=0.9, size=10.5,
                          delay=METEOR_FALL,
                          layers=[_meteor_head_layer(),
                                  dict(name="Mark", role="decal", fn=meteor_mark, frames=12, cols=4, px=256, life=METEOR_FALL / 0.8,
                                       size=6.0, sorting_layer="ground", sorting_order=9),
                                  dict(name="Crater", role="decal", fn=meteor_crater, frames=12, cols=4, px=256, life=1.2, size=6.5,
                                       delay=METEOR_FALL, sorting_layer="ground", sorting_order=8)],
                          extras=[_sparks(16, (0.35, 0.6), (9, 20), (0.2, 0.32), delay=METEOR_FALL, drag=3.0,
                                          gravity=1.2),
                                  _sparks(10, (0.15, 0.3), (16, 26), (0.12, 0.2), delay=METEOR_FALL),
                                  _motes("drift", (0.7, 1.1), (0.22, 0.36), count=14, speed=(1, 4), radius=2.0,
                                         delay=METEOR_FALL + 0.06, gravity=-0.2, drag=1.5, noise=0.6),
                                  _motes("rise", (0.6, 0.9), (0.15, 0.26), rate=18, radius=2.4, scaleY=GROUND_SQ,
                                         delay=METEOR_FALL + 0.15, duration=0.7, rise=(1.2, 2.4))],
                          desc="Meteor falls (stretched comet + ember trail, ground mark) -> 3/4 ground impact "
                               "-> molten crater; pivot = impact point"),
    # Spinning ultimate zone around the hero. Size 10 puts the boundary ring at radius 4.25 u (squash 0.62); nest
    # the prefab under the zone's hitbox root so it scales with it. The zone root follows the hero.
    "blade_storm": dict(fn=blade_storm, group="aoe", role="trail", seed=101, kind="aura", frames=6, cols=3, px=320, life=0.3, size=10.0,
                        loop=True, spin=576.0, squash=0.62, intro=0.22,
                        streams=[_stream("Dagger", dagger_sprite, rate=16, life=(0.42, 0.52), speed=(11, 14),
                                         size=(0.72, 0.82), radius=1.6, arc_speed=1.6, orbital=110, drag=2.5,
                                         length_scale=2.4, squash=0.62)],
                        # start burst only: the sheet already carries the specks, so no mote layer (one draw call less)
                        extras=[_sparks(16, (0.25, 0.45), (12, 22), (0.2, 0.3), radius=0.6, drag=3.5)],
                        desc="Spinning blade-storm zone (loop): top-down whirl squashed to 3/4 and turned by the "
                             "particle (smooth at any speed) + daggers flung out in a sweeping spiral"),
    # ---- power-up pack
    "aura_heal": dict(fn=aura_heal, group="heal", role="ring", seed=73, kind="aura", frames=16, cols=4, px=320, life=1.2, size=5.0, loop=True,
                      offset=[0.0, -SIGIL_Y / 2], sorting_layer="ground", intro=0.35,
                      extras=[_motes("rise", (0.7, 1.1), (0.1, 0.18), rate=7, radius=1.8, scaleY=0.42,
                                     rise=(1.0, 1.8))],
                      desc="Looping healing zone in 3/4 view: edge ring = heal radius (0.8), inward pulse, rising "
                           "plus-crosses; pivot = zone centre / feet"),
    "time_ripple": dict(fn=time_ripple, group="skill", role="wave", seed=74, kind="explode", frames=20, cols=5, px=320, life=0.9, size=12.0,
                        extras=[_motes("drift", (0.6, 1.0), (0.18, 0.3), count=12, speed=(1.5, 4.0), radius=1.4,
                                       delay=0.05, drag=1.5, noise=0.4)],
                        desc="Time-slow pulse: flash, clock face whose hands whirl backwards and brake, three rings "
                             "rolling out, sand specks; pivot = centre"),
    "shield_bubble": dict(fn=shield_bubble, group="shield", role="glow", seed=75, kind="aura", frames=16, cols=4, px=320, life=1.0, size=6.8,
                          loop=True, intro=0.25,
                          extras=[_motes("rise", (0.6, 1.0), (0.1, 0.16), rate=5, radius=2.2, rise=(0.6, 1.2))],
                          desc="Looping energy shield bubble around the hero: hex facets lit by a sweeping band, "
                               "crisp rim, crawling lightning arcs, specular crescent; pivot = body centre, front"),
    "divine_aura": dict(fn=divine_aura, group="buff", role="glow", seed=76, kind="aura", frames=16, cols=4, px=320, life=1.2, size=7.0,
                        loop=True, sorting_layer="ground", intro=0.3,
                        extras=[_motes("rise", (0.5, 0.8), (0.1, 0.16), rate=6, radius=1.4, scaleY=0.42,
                                       rise=(2.6, 3.6), stretch=True)],
                        desc="Looping invincibility aura BEHIND the hero: turning god-ray sunburst, halo above the "
                             "head, ground ring at the feet, rising sparkles; pivot = body centre"),
    "ground_slam": dict(fn=ground_slam, group="hit", role="shockwave", seed=77, kind="explode", frames=20, cols=5, px=320, life=0.6, size=9.75,
                        extras=[_sparks(10, (0.25, 0.45), (8, 16), (0.16, 0.24), drag=3.0),
                                _motes("drift", (0.5, 0.8), (0.16, 0.26), count=8, speed=(1, 3), radius=1.5,
                                       delay=0.05, drag=1.5, noise=0.5)],
                        desc="Stomp: ground cracks, ROUND shock ring to the push radius (0.82 = 4 u at size 9.75), "
                             "inner 3/4 wave, light shards; pivot = stomp centre"),
}

SPRITES = {"Spark": spark_sprite, "Mote": mote_sprite}
