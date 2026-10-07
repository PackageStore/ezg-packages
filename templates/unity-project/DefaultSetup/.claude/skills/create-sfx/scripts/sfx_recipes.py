"""sfx_recipes — elements (timbre palettes) + recipes (one function per sound) of the create-sfx skill.

A recipe draws ONE variant: fn(c) -> float array (any level; make_sfx masters it). Every recipe is built the way
a luminous FX is painted: a TRANSIENT that reads on sample 0 (the "white-hot core"), a BODY that carries pitch /
weight, and a TAIL of small bright bits (the "specks"), plus the element's tint layer. Use c.hz() for every pitch
so --pitch, the element's register and per-variant jitter apply; use c.dur() for every duration so --life scales.
"""
import numpy as np

from sfxsynth import (ar, bp, crackle, drive, fm, glide, hp, loop_seam, lp, mix, noise, ns, osc, partials, perc,
                      pts, ring, sample_hold, semis, vfilter)

BELL = [1.0, 2.76, 5.40, 8.93]                 # struck bar / bell (inharmonic)
GLASS = [1.0, 2.32, 4.25, 6.63, 9.38]          # thin glass / ice
COIN = [1.0, 2.0, 2.76, 4.07]                  # small coin / metal ping
HARM = [1.0, 2.0, 3.0, 4.0]                    # harmonic (organ-like)

# ============================================================================ elements
# pitch: register multiplier · bright: filter-cutoff multiplier · ratios: partials of tonal layers ·
# color: noise colour of noisy layers · tint: extra layer (see TINTS) · amt: default tint level
ELEMENTS = {
    "neutral":  dict(pitch=1.00, bright=1.00, ratios=HARM,  color="pink",  tint=None,      amt=0.0,
                     desc="no colour: UI, physical hits"),
    "fire":     dict(pitch=0.85, bright=0.80, ratios=HARM,  color="pink",  tint="crackle", amt=0.6,
                     desc="warm noise, crackle pops, low rumble"),
    "gold":     dict(pitch=1.25, bright=1.25, ratios=COIN,  color="white", tint="coin",    amt=0.5,
                     desc="bright metal pings, coins, rewards"),
    "frost":    dict(pitch=1.30, bright=1.40, ratios=GLASS, color="white", tint="glass",   amt=0.6,
                     desc="glassy high partials, crystal ticks"),
    "arcane":   dict(pitch=1.00, bright=1.10, ratios=BELL,  color="pink",  tint="shimmer", amt=0.6,
                     desc="detuned FM bells, tremolo shimmer"),
    "toxic":    dict(pitch=0.80, bright=0.60, ratios=HARM,  color="brown", tint="bubble",  amt=0.7,
                     desc="low wet bubbles, gloopy low-pass"),
    "holy":     dict(pitch=1.20, bright=1.20, ratios=BELL,  color="white", tint="choir",   amt=0.5,
                     desc="major-chord pad, airy sparkle"),
    "shadow":   dict(pitch=0.70, bright=0.50, ratios=BELL,  color="brown", tint="void",    amt=0.6,
                     desc="low detuned drone, reverse swell"),
    "electric": dict(pitch=1.10, bright=1.30, ratios=HARM,  color="white", tint="buzz",    amt=0.7,
                     desc="gated saw buzz, arc crackle"),
    "rose":     dict(pitch=1.15, bright=1.10, ratios=BELL,  color="pink",  tint="petal",   amt=0.5,
                     desc="soft high chimes, breathy air"),
}


class Ctx:
    """What a recipe sees: rng, element params, scaling helpers."""

    def __init__(self, rng, element, life_scale=1.0, pitch_st=0.0, jitter=0.0, tint=None):
        self.rng = rng
        self.el = ELEMENTS[element]
        self.element = element
        self.k = life_scale
        self.p = self.el["pitch"] * semis(pitch_st) * (1 + jitter)
        self.amt = self.el["amt"] if tint is None else tint

    def hz(self, f):
        return f * self.p

    def dur(self, d):
        return d * self.k

    def cut(self, f):
        """Filter cutoff scaled by the element brightness, clamped to a safe range."""
        return float(np.clip(f * self.el["bright"], 60, 18000))

    def noise(self, d, color=None):
        return noise(d, self.rng, color or self.el["color"])

    def j(self, lo, hi):
        """Uniform random in [lo, hi] (variant-to-variant variation)."""
        return self.rng.uniform(lo, hi)

    def tint(self, d, gain=1.0):
        """The element's signature layer, d seconds, scaled by the tint amount."""
        name = self.el["tint"]
        if not name or self.amt <= 0:
            return np.zeros(ns(d))
        n = ns(d)
        tail = np.ones(n)
        z = min(ns(0.02), n // 4)                    # silent last ~20 ms: the sound's own decay ends it, not the tint
        k = max(1, int((n - z) * 0.35))              # tints die with the sound: no truncated ringing
        tail[n - z - k:n - z] = np.cos(np.linspace(0, np.pi / 2, k)) ** 2
        tail[n - z:] = 0.0
        return TINTS[name](self, d)[:n] * self.amt * gain * tail


# ============================================================================ tints
def _t_crackle(c, d):
    pops = lp(crackle(d, c.rng, density=90, bright=2500), 5500) * 1.8
    rumble = lp(c.noise(d, "brown"), 260) * 0.5
    return (pops + rumble) * ar(d, 0.08, 1.5)


def _t_coin(c, d):
    out = np.zeros(ns(d))
    for i in range(3):
        at = c.j(0, d * 0.4)
        p = partials(c.hz(c.j(2600, 3600)), d * 0.5, COIN, [1, .4, .3, .15], [.12, .08, .06, .04], c.rng)
        out = mix((out, 0, 1), (p, at, c.j(.2, .45)), length=ns(d))
    return out


def _t_glass(c, d):
    out = partials(c.hz(c.j(2400, 3400)), d, GLASS, [.6, .5, .4, .3, .2], [.25, .18, .12, .08, .06], c.rng, 0.01)
    ticks = hp(crackle(d, c.rng, density=40, bright=6000, decay=0.0015), 5000) * 0.8
    return out + ticks


def _t_shimmer(c, d):
    n = ns(d)
    t = np.arange(n) / ns(1)
    trem = 0.6 + 0.4 * np.sin(2 * np.pi * 9 * t)
    a = fm(c.hz(1320), d, ratio=3.5, index=pts(d, [(0, 2.5), (1, 0.3)]))
    b = fm(c.hz(1320) * 1.012, d, ratio=3.5, index=pts(d, [(0, 2.0), (1, 0.2)]))
    return (a + b) * 0.35 * trem * perc(d, 0.01, 3.0)


def _t_bubble(c, d):
    out = np.zeros(ns(d))
    for _ in range(int(6 + 10 * d)):
        bd = c.j(0.025, 0.06)
        f0 = c.hz(c.j(260, 520))
        b = osc(glide(f0, f0 * c.j(1.8, 2.6), bd), bd) * perc(bd, 0.003, 4)
        out = mix((out, 0, 1), (b, c.j(0, max(d - bd, 0.01)), c.j(.3, .8)), length=ns(d))
    return lp(out, 1800)


def _t_choir(c, d):
    chord = sum(osc(c.hz(523.25) * r, d, "sine") * a for r, a in ((1, 1), (1.26, .7), (1.5, .7), (2, .4)))
    air = hp(c.noise(d, "white"), 6000) * 0.15
    return (chord * 0.25 + air) * ar(d, 0.35, 1.2)


def _t_void(c, d):
    drone = lp(osc(c.hz(55), d, "saw") + osc(c.hz(55) * 1.013, d, "saw"), 380) * 0.5
    swell = lp(c.noise(d, "pink"), 900) * np.linspace(0, 1, ns(d)) ** 3 * 0.6
    return (drone + swell) * ar(d, 0.5, 1.0)


def _t_buzz(c, d):
    gate = (sample_hold(d, c.rng, 320) > 0.35).astype(float)
    b = bp(osc(c.hz(c.j(80, 120)), d, "saw") * gate, 600, 5000)
    arcs = hp(crackle(d, c.rng, density=140, bright=5000, decay=0.001), 3500)
    return (b * 0.7 + arcs) * perc(d, 0.001, 3.5)


def _t_petal(c, d):
    ch = partials(c.hz(c.j(2200, 2800)), d, [1, 2.0, 3.01], [1, .3, .1], [.3, .2, .1], c.rng)
    breath = bp(c.noise(d, "white"), 2000, 5000) * 0.3 * ar(d, 0.3, 1.5)
    return ch * 0.5 + breath


TINTS = {"crackle": _t_crackle, "coin": _t_coin, "glass": _t_glass, "shimmer": _t_shimmer, "bubble": _t_bubble,
         "choir": _t_choir, "void": _t_void, "buzz": _t_buzz, "petal": _t_petal}

# ============================================================================ recipes
RECIPES = {}

# kind -> loudness target is per recipe; kind drives QA limits + compare references (make_sfx.KINDS)


def recipe(name, kind, life, lufs, desc, element="neutral", event=None, variants=1, reverb=None, loop=False,
           vary=0.03, soft=False):
    """Register a recipe. reverb = (decay_s, wet) or None. vary = per-variant pitch jitter (fraction).
    soft = the sound swells in on purpose (whoosh, charge): QA skips the LATE_ONSET check."""
    def deco(fn):
        RECIPES[name] = dict(fn=fn, kind=kind, life=life, lufs=lufs, desc=desc, element=element, event=event,
                             variants=variants, reverb=reverb, loop=loop, vary=vary, soft=soft)
        return fn
    return deco


def tick(c, d=0.004, f=3500, g=1.0):
    """Very short high noise click: the transient that makes a sound read on frame 0."""
    return hp(c.noise(d, "white"), f) * perc(d, 0.0002, 6) * g


def note(c, f, d, shape="sine", a=0.002, k=5.0, harm=(1.0,), amps=None):
    """A pitched note: sum of shape oscillators at f×harm with a percussive envelope."""
    amps = amps or [1.0 / (i + 1) ** 1.2 for i in range(len(harm))]
    s = sum(osc(f * h, d, shape) * a_ for h, a_ in zip(harm, amps))
    return s * perc(d, a, k)


# ---------------------------------------------------------------------------- UI
@recipe("ui_click", "ui_short", 0.07, -21, "button tap: bright blip + tick + tiny body")
def ui_click(c):
    d = c.dur(0.07)
    blip = osc(glide(c.hz(2300), c.hz(1250), d * 0.7, "fast", d * 0.15), d * 0.7) * perc(d * 0.7, 0.0008, 7)
    body = osc(glide(c.hz(430), c.hz(260), 0.03), 0.03) * perc(0.03, 0.001, 6) * 0.5
    return mix(blip, tick(c, 0.004, 4000, 0.5), body, c.tint(d, 0.3))


@recipe("ui_select", "ui_short", 0.14, -21, "select / toggle: two quick rising blips")
def ui_select(c):
    a = note(c, c.hz(1320), 0.07, "sine", 0.001, 6, (1, 2), (1, .25))
    b = note(c, c.hz(1760), 0.08, "sine", 0.001, 6, (1, 2), (1, .25))
    return mix(a, (b, c.dur(0.055), 0.9), tick(c, 0.003, 4500, 0.35), c.tint(0.14, 0.3))


@recipe("ui_tick", "ui_short", 0.045, -24, "spin / roll / counter tick (plays many times in a row)", vary=0.06)
def ui_tick(c):
    d = c.dur(0.045)
    ping = osc(c.hz(c.j(2900, 3200)), d) * perc(d, 0.0004, 10)
    return mix(ping, tick(c, 0.0025, 5000, 0.6))


@recipe("ui_pop", "ui_short", 0.16, -20, "popup open: bubbly rising pop")
def ui_pop(c):
    d = c.dur(0.16)
    pop = fm(glide(c.hz(380), c.hz(1150), d * 0.6, "exp"), d * 0.6, 2.0, pts(d * 0.6, [(0, 1.2), (1, 0.1)]))
    pop *= pts(d * 0.6, [(0, 0), (0.04, 1), (0.5, 0.6), (1, 0)])
    air = lp(c.noise(0.03, "pink"), 3000) * perc(0.03, 0.001, 5) * 0.4
    return mix(pop, air, c.tint(d, 0.3))


@recipe("ui_close", "ui_short", 0.15, -22, "popup close / back: soft falling blip")
def ui_close(c):
    d = c.dur(0.15)
    s = osc(glide(c.hz(950), c.hz(330), d * 0.7, "exp"), d * 0.7, "tri") * perc(d * 0.7, 0.002, 6.5)
    return mix(lp(s, c.cut(4000)), tick(c, 0.003, 3000, 0.25))


@recipe("ui_whoosh", "ui_short", 0.35, -22, "panel slide / screen transition swoosh", soft=True)
def ui_whoosh(c):
    d = c.dur(0.35)
    fc = pts(d, [(0, 500), (0.55, 3600), (1, 1400)], log=True) * c.el["bright"]
    s = vfilter(c.noise(d, "pink"), "bp", fc, q=1.4) * ar(d, 0.55, 2.6)
    return mix(s * 1.5, c.tint(d, 0.2))


@recipe("ui_coin", "ui_short", 0.45, -20, "coin / gem pickup: classic two-note ping", element="gold", vary=0.02)
def ui_coin(c):
    n1 = lp(osc(c.hz(988), 0.07, "square"), 5000) * perc(0.07, 0.001, 2.5) * 0.5
    n2d = c.dur(0.38)
    n2 = lp(osc(c.hz(1319), n2d, "square"), 5000) * perc(n2d, 0.001, 6) * 0.5
    bell = partials(c.hz(1319), n2d, COIN, [1, .35, .25, .1], [n2d * .2, n2d * .14, n2d * .1, n2d * .07], c.rng)
    return mix(n1, (n2 + bell * 0.6, 0.07), c.tint(0.45, 0.4))


@recipe("ui_error", "ui_short", 0.32, -20, "denied / not enough currency: low double buzz")
def ui_error(c):
    a = lp(osc(c.hz(392), 0.12, "square") + osc(c.hz(395), 0.12, "square"), c.cut(2000)) * perc(0.12, 0.003, 3)
    b = lp(osc(c.hz(294), 0.16, "square") + osc(c.hz(296), 0.16, "square"), c.cut(1800)) * perc(0.16, 0.003, 5.5)
    return mix(a * 0.5, (b * 0.5, c.dur(0.14)))


@recipe("ui_unlock", "ui_jingle", 0.7, -15, "unlock / equip: mechanical click + rising chime", element="gold",
        reverb=(0.6, 0.18))
def ui_unlock(c):
    clk = mix(tick(c, 0.004, 2500), (partials(c.hz(2200), 0.06, COIN, None, [.03, .02, .015, .01], c.rng) * .5, 0),
              (tick(c, 0.004, 3000, 0.8), 0.05))
    ch1 = partials(c.hz(1568), 0.4, BELL, [1, .3, .15, .05], [.35, .2, .1, .05], c.rng)
    ch2 = partials(c.hz(2093), c.dur(0.55), BELL, [1, .3, .15, .05], [.45, .25, .12, .06], c.rng)
    return mix(clk, (ch1 * 0.6, 0.11), (ch2 * 0.7, 0.19), (c.tint(0.5, 0.5), 0.15))


@recipe("ui_reward", "ui_jingle", 0.9, -13, "claim reward: bright arpeggio + shimmer", element="gold",
        reverb=(0.8, 0.22))
def ui_reward(c):
    layers = []
    for i, f in enumerate((1047, 1319, 1568, 2093)):
        d = c.dur(0.6) if i == 3 else 0.3
        tone = note(c, c.hz(f), d, "sine", 0.003, 4.5, (1, 2, 3), (1, .3, .12))
        layers.append((tone, 0.07 * i, 0.8))
    sh = hp(c.noise(0.6, "white"), c.cut(7000)) * ar(0.6, 0.35, 1.5) * 0.18
    return mix(*layers, (sh, 0.2), (c.tint(0.6, 0.6), 0.2))


@recipe("ui_levelup", "ui_jingle", 1.2, -14, "level up: rising run into a held major chord", element="gold",
        reverb=(0.9, 0.25))
def ui_levelup(c):
    layers = []
    for i, f in enumerate((784, 1047, 1319, 1568)):
        tone = lp(osc(c.hz(f), 0.16, "pulse", duty=0.25), c.cut(4500)) * perc(0.16, 0.002, 3)
        layers.append((tone * 0.45, 0.06 * i))
    cd = c.dur(0.8)
    chord = sum(note(c, c.hz(f), cd, "sine", 0.004, 5.5, (1, 2), (1, .2)) for f in (1047, 1319, 1568, 2093))
    sweep = vfilter(c.noise(0.3, "pink"), "bp", glide(c.cut(800), c.cut(6000), 0.3), 2.0) * ar(0.3, 0.85, 3) * 0.5
    return mix(*layers, (chord * 0.35, 0.24), sweep, (c.tint(0.8, 0.7), 0.24))


@recipe("ui_fanfare", "ui_jingle", 1.4, -12, "celebration / tier up / rank up: brass chord stab + cymbal",
        element="gold", reverb=(1.1, 0.28))
def ui_fanfare(c):
    d = c.dur(1.2)
    voices = []
    for f in (523.25, 784, 1046.5, 1318.5):
        for cents in (-7, 0, 7):
            voices.append(osc(c.hz(f) * semis(cents / 100), d, "saw", phase=c.j(0, 1)))
    brass = vfilter(sum(voices) / len(voices), "lp", pts(d, [(0, c.cut(700)), (0.04, c.cut(5200)), (1, c.cut(1600))],
                                                         log=True), q=0.9)
    brass *= pts(d, [(0, 0), (0.02, 1), (0.25, 0.7), (1, 0)])
    boom = osc(glide(c.hz(140), c.hz(70), 0.3), 0.3) * perc(0.3, 0.002, 5)
    cym = hp(c.noise(d, "white"), 6500) * perc(d, 0.002, 4) * 0.35
    return mix(brass * 1.6, boom * 0.8, cym, (c.tint(d, 0.5), 0.05))


# ---------------------------------------------------------------------------- gameplay: shots / casts
@recipe("shot_energy", "shot", 0.22, -20, "energy bolt / blaster pew (projectile spawn)", element="arcane",
        event="shot", variants=4, vary=0.05)
def shot_energy(c):
    d = c.dur(0.22)
    f = glide(c.hz(c.j(1500, 1800)), c.hz(260), d, "fast", d * 0.25)
    body = lp(osc(f, d, "saw") * 0.6 + osc(f * 0.5, d, "square") * 0.3, c.cut(6000)) * perc(d, 0.001, 5)
    return mix(body, tick(c, 0.006, 2000, 0.6), c.tint(d, 0.5))


@recipe("shot_throw", "shot", 0.2, -21, "thrown blade / arrow / dagger swish (projectile spawn)", event="shot",
        variants=4, vary=0.06)
def shot_throw(c):
    d = c.dur(0.2)
    fc = pts(d, [(0, c.cut(900)), (0.3, c.cut(c.j(3500, 4500))), (1, c.cut(1800))], log=True)
    sw = vfilter(c.noise(d, "pink"), "bp", fc, q=2.5) * ar(d, 0.3, 2.2)
    whistle = osc(fc * 0.5, d) * ar(d, 0.3, 3) * 0.08
    return mix(sw * 2.0, whistle, c.tint(d, 0.3))


@recipe("shot_heavy", "shot", 0.4, -18, "cannon / launcher / big projectile launch", element="fire", event="shot",
        variants=3, reverb=(0.3, 0.1), vary=0.04)
def shot_heavy(c):
    d = c.dur(0.4)
    boom = osc(glide(c.hz(c.j(360, 420)), c.hz(190), d * 0.5, "fast", 0.04), d * 0.5, "tri") * perc(d * 0.5, 0.001, 5)
    burst = vfilter(c.noise(d * 0.6, "pink"), "lp", glide(c.cut(3000), c.cut(700), d * 0.6), 0.9) * perc(d * 0.6, 0.001, 5)
    crack = bp(c.noise(0.02, "white"), 1500, 4500) * perc(0.02, 0.0003, 5)
    rumble = lp(c.noise(d, "brown"), 220) * perc(d, 0.01, 5) * 0.08
    return mix(drive(mix(boom * 0.7, hp(burst, 250) * 1.3), 2.0), crack * 0.8, rumble, c.tint(d, 0.25))


@recipe("cast_charge", "cast", 0.8, -18, "charge-up / cast build-up then release (skill start)", element="arcane",
        event="fire", reverb=(0.6, 0.15))
def cast_charge(c):
    d = c.dur(0.8)
    rel = 0.82
    tone = fm(glide(c.hz(220), c.hz(880), d, "exp"), d, 2.0, pts(d, [(0, 0.5), (rel, 3.0), (1, 0.5)]))
    noi = vfilter(c.noise(d, "pink"), "bp", glide(c.cut(400), c.cut(5000), d), q=1.8)
    amp = pts(d, [(0, 0), (rel, 1), (1, 0)]) ** 1.6
    spark = partials(c.hz(1760), d * 0.3, BELL, [1, .4, .2, .1], [.2, .12, .08, .05], c.rng)
    return mix(tone * amp * 0.6, noi * amp * 0.8, (spark * 0.6, d * rel), c.tint(d, 0.7))


@recipe("flame_burst", "cast", 0.5, -18, "fire breath / flame burst whoosh", element="fire", event="fire",
        variants=2, vary=0.04)
def flame_burst(c):
    d = c.dur(0.5)
    fc = pts(d, [(0, c.cut(600)), (0.25, c.cut(4500)), (1, c.cut(1200))], log=True)
    roar = vfilter(c.noise(d, "pink"), "lp", fc, q=1.1) * ar(d, 0.18, 2.4)
    low = lp(c.noise(d, "brown"), 180) * ar(d, 0.15, 2.4) * 0.35
    return mix(roar * 1.4, low, c.tint(d, 1.0))


@recipe("heal", "cast", 0.85, -18, "heal / buff / blessing: soft upward chimes", element="holy", event="fire",
        reverb=(0.9, 0.3))
def heal(c):
    layers = []
    for i, f in enumerate((1047, 1175, 1397, 1568, 2093)):
        tone = note(c, c.hz(f), 0.5, "sine", 0.01, 4, (1, 2), (1, .15))
        layers.append((tone * 0.5, c.dur(0.06) * i))
    air = hp(c.noise(c.dur(0.7), "white"), c.cut(5000)) * ar(c.dur(0.7), 0.4, 1.5) * 0.15
    return mix(*layers, air, c.tint(c.dur(0.7), 0.6))


@recipe("spawn", "cast", 0.6, -19, "summon / spawn / teleport: reverse swell into a pop", element="arcane",
        event="fire", reverb=(0.6, 0.2))
def spawn(c):
    d = c.dur(0.6)
    sw = 0.35 * d / 0.6
    swell = vfilter(c.noise(sw, "pink"), "bp", glide(c.cut(300), c.cut(3200), sw), 1.6) * np.linspace(0, 1, ns(sw)) ** 3
    pop = osc(glide(c.hz(300), c.hz(900), 0.06), 0.06) * perc(0.06, 0.001, 4)
    sp = partials(c.hz(1900), d - sw, BELL, [1, .4, .2, .1], [.18, .1, .07, .04], c.rng)
    return mix(swell, (pop, sw), (sp * 0.6, sw), (c.tint(d - sw, 0.6), sw))


# ---------------------------------------------------------------------------- gameplay: hits
@recipe("hit_impact", "hit", 0.22, -21, "generic punchy hit (bullet, fist, blunt)", event="hit", variants=4,
        vary=0.06)
def hit_impact(c):
    # phone speakers drop everything under ~250 Hz: the weight lives in the 150-400 Hz knock + a gritty 1-4 kHz body
    d = c.dur(0.22)
    knock = osc(glide(c.hz(c.j(340, 440)), c.hz(c.j(150, 190)), d * 0.45, "fast", c.j(0.015, 0.03)), d * 0.45, "tri")
    knock *= perc(d * 0.45, 0.0005, c.j(5, 7))
    crack = bp(c.noise(0.03, "white"), c.cut(c.j(900, 1300)), c.cut(c.j(2800, 4200))) * perc(0.03, 0.0004, 5)
    body = vfilter(c.noise(d, "pink"), "lp", glide(c.cut(c.j(4000, 6000)), c.cut(800), d), 0.8)
    body *= perc(d, 0.001, c.j(5.5, 7))
    snap = osc(c.hz(c.j(1300, 1900)), 0.02) * perc(0.02, 0.0004, 6)
    return mix(knock * 0.5, crack * 1.2, body * c.j(0.8, 1.1), snap * c.j(0.2, 0.4), tick(c, 0.003, 3500, 0.6),
               c.tint(d, 0.3))


@recipe("hit_slash", "hit", 0.22, -21, "blade hit: short swish + metallic ring", event="hit", variants=4,
        vary=0.05)
def hit_slash(c):
    d = c.dur(0.22)
    sw = vfilter(c.noise(0.07, "white"), "bp", glide(c.cut(2500), c.cut(6500), 0.07), 2.0) * ar(0.07, 0.6, 1.5)
    rd = d - 0.05
    ringing = partials(c.hz(c.j(2000, 2500)), rd, [1, 2.41, 3.87, 5.3], [1, .5, .3, .15],
                       [rd * .22, rd * .15, rd * .1, rd * .07], c.rng)
    return mix(sw * 1.2, (ringing * 0.45, 0.05), (tick(c, 0.003, 4000, 0.6), 0.05), c.tint(d, 0.3))


@recipe("hit_magic", "hit", 0.3, -21, "magic projectile hit: sparkly burst", element="arcane", event="hit",
        variants=4, vary=0.06)
def hit_magic(c):
    d = c.dur(0.3)
    bell = partials(c.hz(c.j(1100, 1300)), d, c.el["ratios"], [1, .5, .3, .15, .1][:len(c.el["ratios"])],
                    [d * .2, d * .14, d * .1, d * .07, d * .05][:len(c.el["ratios"])], c.rng)
    burst = hp(c.noise(0.04, "white"), c.cut(3000)) * perc(0.04, 0.0005, 5)
    sub = osc(glide(c.hz(130), c.hz(60), 0.08), 0.08) * perc(0.08, 0.0005, 5)
    return mix(bell * 0.5, burst * 0.7, sub * 0.6, c.tint(d, 0.7))


@recipe("explode", "explode", 1.0, -15, "explosion / blast / meteor impact", element="fire", event="hit",
        variants=3, reverb=(0.7, 0.16), vary=0.05)
def explode(c):
    d = c.dur(1.0)
    thump = osc(glide(c.hz(c.j(200, 240)), c.hz(60), d * 0.4, "fast", 0.07), d * 0.4) * perc(d * 0.4, 0.002, 4)
    body = vfilter(c.noise(d, "pink"), "lp", pts(d, [(0, c.cut(4000)), (0.25, c.cut(1300)), (1, c.cut(350))],
                                                    log=True), q=0.8)
    body *= perc(d, 0.003, 5.5) * 1.6
    crunch = drive(bp(c.noise(d * 0.35, "white"), c.cut(500), c.cut(2800)), 3.0) * perc(d * 0.35, 0.001, 5) * 0.8
    crack = hp(c.noise(0.04, "white"), 1500) * perc(0.04, 0.0005, 5)
    debris = lp(crackle(d * 0.8, c.rng, density=70, bright=3000), c.cut(6000)) * ar(d * 0.8, 0.15, 2.4) * 1.2
    return mix(drive(mix(thump, body, crunch), 1.6), crack * 0.8, (debris, d * 0.1), c.tint(d, 0.5))


@recipe("zap", "hit", 0.35, -19, "electric zap / lightning strike", element="electric", event="hit", variants=3,
        vary=0.08)
def zap(c):
    d = c.dur(0.35)
    gate = (sample_hold(d, c.rng, 300) > 0.3).astype(float)
    arc = osc(glide(c.hz(2200), c.hz(160), d, "fast", d * 0.3), d, "saw") * gate
    hiss = hp(c.noise(d, "white"), 3000) * 0.6
    s = bp(ring(arc + hiss, c.j(60, 90)) + arc * 0.5, 250, c.cut(7000)) * perc(d, 0.0008, 5)
    return mix(s * 1.3, tick(c, 0.004, 2500, 0.7), c.tint(d, 0.6))


@recipe("shatter", "hit", 0.65, -18, "ice / glass / crystal shatter", element="frost", event="hit", variants=3,
        vary=0.05)
def shatter(c):
    d = c.dur(0.65)
    out = []
    for _ in range(14):
        f = c.hz(c.j(1500, 4500))
        if f > 16000:
            continue
        out.append((osc(f, d, phase=c.j(0, 1)) * perc(d, 0.0005, max(6.0, d / c.j(0.04, 0.18))) * c.j(.3, 1),
                    c.j(0, .06)))
    crack = hp(c.noise(0.03, "white"), 3500) * perc(0.03, 0.0003, 5) * 2
    tinkle = []
    for _ in range(6):
        tinkle.append((partials(c.hz(c.j(2200, 4500)), 0.12, GLASS[:3], [1, .4, .2], [.03, .02, .015], c.rng),
                       c.j(0.08, d * 0.6), c.j(.15, .4)))
    return mix(*out, crack, *tinkle, length=ns(d)) * 0.5 * perc(d, 0.0, 1.5) + mix(c.tint(d, 0.3), length=ns(d))


# ---------------------------------------------------------------------------- loops
@recipe("aura_loop", "loop", 1.0, -24, "seamless aura / beam / shield hum (loop)", element="arcane", event="fire",
        loop=True, vary=0.0)
def aura_loop(c):
    d = c.dur(1.0)
    xf = min(0.15, d * 0.25)
    n = ns(d + xf)
    total = n / ns(1)
    t = np.arange(n) / ns(1)
    cyc = max(1, round(3 * d))                     # integer LFO cycles per loop -> seam-free
    lfo = 0.75 + 0.25 * np.sin(2 * np.pi * cyc * t / d)
    base = c.hz(165)
    drone = osc(base, total, "saw") + osc(base * 1.006, total, "saw") * 0.8 + osc(base * 2.01, total, "tri") * 0.4
    drone = vfilter(drone, "lp", c.cut(900) * (0.8 + 0.2 * lfo), q=1.2)
    air = hp(noise(total, c.rng, "white"), c.cut(5000)) * 0.08
    return loop_seam((drone * 0.5 + air) * lfo + mix(c.tint(total, 0.4), length=n), xf)
