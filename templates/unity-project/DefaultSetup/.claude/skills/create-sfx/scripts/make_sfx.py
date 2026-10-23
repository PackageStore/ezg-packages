#!/usr/bin/env python3
"""make_sfx — CLI of the create-sfx skill. Run from the Unity project root.

  py make_sfx.py list
  py make_sfx.py config                                 # resolved roots, SoundConfig fields, event fields, §6c style
  py make_sfx.py make --recipe ui_reward --name PurchaseDone [--config PurchaseSuccess] [...]       # UI / free sound
  py make_sfx.py make --recipe hit_impact --element fire --name Fireball --event hit [--part P] [--variants 4]
                      [--life 0.2] [--pitch -2] [--seed 7] [--tint 0.5] [--lufs -21]
                      [--prefab <path.prefab> [--field F] [--cooldown 0.06] [--append] [--play-on-enable]]
  py make_sfx.py wire --prefab <path.prefab> --name Fireball --event hit [--part P] [--field F] [--append]
  py make_sfx.py wire --config PurchaseSuccess --file <sfxRoot>/PurchaseDone/sfx_purchase_done.wav [--asset <so>]
  py make_sfx.py ingest --src <a.mp3[,b.ogg…]> --name OpenPopup (--like ui_pop | --kind ui_short) [--event E]
                      [--variants N] [--pitch ST] [--start S] [--end S] [--lufs L] [--credits sfx-credits.json]
                      [--config <Field> | --prefab <path.prefab> [...]]   # found sounds (MCP ezg-sfx / Freesound)
  py make_sfx.py review <base | spec.sfx.json>          # rebuild the review PNG + audition WAVs
  py make_sfx.py analyze <audio ...>                    # loudness / timing / brightness numbers
  py make_sfx.py play <base | wav ...>                  # listen (afplay / winsound / paplay|aplay)

`make` synthesises N variants 100% in code (numpy + scipy), masters each to the recipe's loudness target, writes
16-bit mono WAV + a Unity .meta (Vorbis, Decompress On Load, the project's sound asset bundle if it uses one) +
<base>.sfx.json (exact command, files, GUIDs, QA), then a review sheet + audition WAVs to Temp/CreateSfx/<base>/
(git-ignored, throwaway).
`ingest` does the same for sounds that already exist (Freesound previews fetched through the ezg-sfx MCP, or a
file the dev hands over): decode, cut the leading silence, master to the loudness target of the matching recipe
(--like) or kind (--kind), the same QA / .meta / review / wiring as `make`, plus the license of every source
(sfx-credits.json next to the source, as the MCP writes it) in the spec and in <sfxRoot>/<Name>/sfx-credits.json.
One source and --variants N > 1 makes the pool by small pitch offsets of that source.
  Folder: <sfxRoot>/<Name>/ — `sfxRoot` in .claude/project-profile.json, else DEFAULT_SFX_ROOT; --out replaces it.
  --name <Name>             -> sfx_<name>[_<part>][_<i>].wav ; --config <Field> wires one AudioClip field of the
                               SoundConfig asset (`soundConfigAsset` in the profile) or of --asset
  --name <Name> --event E   -> sfx_<name>_<E>[_<part>]_<i>.wav (a variant pool) ; --prefab puts the pool into a
                               List<SoundPlayCustomModel> field of that prefab: com.ezg.audio SoundPlayController
                               `_soundCustomList` by default, `sfxEventFields` in the profile or --field otherwise
Project style (ArtStyle.md §6c, fenced block ```sfx-style```): default / allowed elements, asset bundle, loudness
offsets, and the game's own clips that `review` puts above every new sound.
Existing .meta files are kept (same GUID: every reference survives a re-make).
"""
import argparse
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import uuid
import zlib

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import sfxsynth as S  # noqa: E402
from sfx_recipes import ELEMENTS, RECIPES, Ctx  # noqa: E402

DEFAULT_SFX_ROOT = "Assets/_Project/Visual/ArtAsset/Shared/Sounds/Generated"
DEFAULT_SOUND_CONFIG = "Assets/_Project/Features/_Shared/Resources/SoundConfig.asset"
DEFAULT_LIST_FIELD = "_soundCustomList"        # com.ezg.audio SoundPlayController, Customs list
PREVIEW_ROOT = "Temp/CreateSfx"                # review files only: git-ignored, safe to lose
ART_STYLE = ".claude/docs/ArtStyle.md"
PROFILE = ".claude/project-profile.json"
EVENTS = ("fire", "shot", "hit")               # once per activation / once per projectile or spawn / once per hit
DEFAULT_COOLDOWN = {"fire": 0.0, "shot": 0.05, "hit": 0.06}
AUDIO_EXT = (".wav", ".mp3", ".ogg", ".aif", ".aiff")
REF_SCAN = (".prefab", ".asset", ".unity", ".playable", ".controller")

# kind -> QA limits (the game's reference clips per kind come from ArtStyle.md §6c `ref <kind>:` lines)
KINDS = {
    "ui_short": dict(max_dur=0.6, frequent=False, burst=None),
    "ui_jingle": dict(max_dur=2.5, frequent=False, burst=None),
    "shot": dict(max_dur=0.7, frequent=True, burst=6.0),
    "hit": dict(max_dur=0.8, frequent=True, burst=10.0),
    "explode": dict(max_dur=2.0, frequent=False, burst=3.0),
    "cast": dict(max_dur=1.8, frequent=False, burst=None),
    "loop": dict(max_dur=4.0, frequent=False, burst=None),
}
BLOCKING = ("LOUDNESS", "CLICK_START", "CUT_TAIL", "LATE_ONSET", "TOO_LONG", "SILENT", "LOOP_SEAM", "PHONE_WEAK",
            "LICENSE")
# ingest --kind without --like: loudness target per kind (style-guide §4, middle of each range)
KIND_LUFS = {"ui_short": -21, "ui_jingle": -14, "shot": -20, "hit": -21, "explode": -15, "cast": -18, "loop": -24}
DEFAULT_LICENSE = "cc0"                        # profile `sfxLicense`: cc0 (no credits screen) | any (CC0 + CC BY)
CREDITS_FILE = "sfx-credits.json"              # the ezg-sfx MCP manifest name (get_download_links)
VARIANT_SPREAD = (0.0, 0.7, -0.7, 1.4, -1.4, 2.1, -2.1, 2.8, -2.8)  # semitones: pool from fewer sources than variants

META_TEMPLATE = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 8
  defaultSettings:
    serializedVersion: 2
    loadType: 0
    sampleRateSetting: 0
    sampleRateOverride: 44100
    compressionFormat: 1
    quality: 0.29999998
    conversionMode: 0
    preloadAudioData: 0
  platformSettingOverrides: {{}}
  forceToMono: 0
  normalize: 0
  loadInBackground: 0
  ambisonic: 0
  3D: 1
  userData:
  assetBundleName: {bundle}
  assetBundleVariant:
"""


# ============================================================================ project config
def load_profile():
    try:
        return json.load(open(PROFILE, encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def sfx_root(args_out=None):
    return (args_out or load_profile().get("sfxRoot") or DEFAULT_SFX_ROOT).rstrip("/").replace("\\", "/")


def sound_config():
    return (load_profile().get("soundConfigAsset") or DEFAULT_SOUND_CONFIG).replace("\\", "/")


def event_field(event, override=None):
    """List field that receives the pool: --field, else profile `sfxEventFields[event]`, else SoundPlayController's."""
    return override or (load_profile().get("sfxEventFields") or {}).get(event or "") or DEFAULT_LIST_FIELD


def parse_style():
    """The ```sfx-style``` block of ArtStyle.md §6c. Lines `key: value`; `#` starts a comment. Keys:
    default, allow, bundle, `ref <kind>` (comma-separated clip paths), `offset <kind|all>` (dB added to targets)."""
    cfg = dict(default="", allow=[], bundle=None, refs={}, offsets={}, source=None, errors=[])
    if not os.path.exists(ART_STYLE):
        return cfg
    text = open(ART_STYLE, encoding="utf-8").read()
    m = re.search(r"^```sfx-style[ \t]*\n(.*?)^```", text, re.M | re.S)
    if not m:
        return cfg
    cfg["source"] = ART_STYLE
    for raw in m.group(1).splitlines():
        line = "" if re.match(r"\s*#(\s|$)", raw) else re.sub(r"\s+#\s.*$", "", raw).strip()
        if not line or ":" not in line:
            continue
        key, val = (s.strip() for s in line.split(":", 1))
        try:
            if key == "default":
                cfg["default"] = val
            elif key == "allow":
                cfg["allow"] = [v.strip() for v in val.split(",") if v.strip()]
            elif key == "bundle":
                cfg["bundle"] = "" if val.lower() == "none" else val
            elif key.startswith("ref "):
                kind = key[4:].strip()
                if kind not in KINDS:
                    raise ValueError(f"ref <{'|'.join(KINDS)}>: <clip path>[, <clip path>…]")
                cfg["refs"].setdefault(kind, []).extend(p.strip() for p in val.split(",") if p.strip())
            elif key.startswith("offset "):
                kind = key[7:].strip()
                if kind != "all" and kind not in KINDS:
                    raise ValueError(f"offset <all|{'|'.join(KINDS)}>: <±dB>")
                cfg["offsets"][kind] = float(val.lower().replace("db", "").strip())
            else:
                raise ValueError("unknown key (default, allow, bundle, ref <kind>, offset <kind>)")
        except ValueError as e:
            cfg["errors"].append(f"{raw.strip()}  ->  {e}")
    for el in ([cfg["default"]] if cfg["default"] else []) + cfg["allow"]:
        if el not in ELEMENTS:
            cfg["errors"].append(f"unknown element '{el}' (see `list`)")
    return cfg


def lufs_target(R, style, override=None):
    if override is not None:
        return override
    off = style["offsets"]
    return R["lufs"] + off.get(R["kind"], off.get("all", 0.0))


# ============================================================================ helpers
def check_root():
    if not os.path.isdir("Assets"):
        sys.exit("make_sfx: run from the Unity project root (no Assets/ here).")


def snake(name):
    """PurchaseDone -> purchase_done (a leading sfx_ is dropped: files are always prefixed sfx_)."""
    s = re.sub(r"(?<=[a-z0-9])(?=[A-Z])", "_", name)
    s = re.sub(r"[^a-z0-9]+", "_", s.lower()).strip("_")
    return s[4:] if s.startswith("sfx_") else s


def rel(p):
    return os.path.relpath(p).replace("\\", "/")


def detect_bundle(style):
    """Asset bundle for new clips: §6c `bundle`, else the majority over the project's audio .meta files under
    Assets/ ('' when they use none)."""
    if style["bundle"] is not None:
        return style["bundle"]
    votes = {}
    for root, _, files in os.walk("Assets"):
        for fn in files:
            if not (fn.endswith(".meta") and fn[:-5].lower().endswith(AUDIO_EXT)):
                continue
            for line in open(os.path.join(root, fn), encoding="utf-8", errors="ignore"):
                if line.startswith("  assetBundleName:"):
                    b = line.split(":", 1)[1].strip()
                    votes[b] = votes.get(b, 0) + 1
                    break
    return max(votes, key=votes.get) if votes else ""


def meta_guid(path):
    m = path + ".meta"
    if not os.path.exists(m):
        return None
    for line in open(m, encoding="utf-8", errors="ignore"):
        if line.startswith("guid:"):
            return line.split()[1]
    return None


def ensure_meta(path, bundle):
    """Write the AudioImporter .meta for a new clip; keep an existing one (GUID + any hand tweaks)."""
    g = meta_guid(path)
    if g:
        return g, False
    g = uuid.uuid4().hex
    with open(path + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write(META_TEMPLATE.format(guid=g, bundle=bundle))
    return g, True


def load_audio(path):
    """WAV directly; MP3/OGG/AIFF through macOS afconvert or ffmpeg when present (else None)."""
    if path.lower().endswith(".wav"):
        return S.read_wav(path)
    os.makedirs(PREVIEW_ROOT, exist_ok=True)
    tmp = os.path.join(PREVIEW_ROOT, "_decode.wav")
    for cmd in (["afconvert", "-f", "WAVE", "-d", "LEI16@44100", "-c", "1", path, tmp],
                ["ffmpeg", "-y", "-loglevel", "error", "-i", path, "-ac", "1", "-ar", "44100", "-c:a", "pcm_s16le",
                 tmp]):
        if shutil.which(cmd[0]) and subprocess.run(cmd, capture_output=True).returncode == 0:
            return S.read_wav(tmp)
    return None


def guid_refs(guids):
    """{guid: [files referencing it]} over every serialized asset under Assets/ (one pass)."""
    hits = {g: [] for g in guids}
    if not guids:
        return hits
    pat = re.compile("|".join(guids))
    for root, _, files in os.walk("Assets"):
        for fn in files:
            if fn.endswith(REF_SCAN):
                p = os.path.join(root, fn)
                try:
                    txt = open(p, encoding="utf-8", errors="ignore").read()
                except OSError:
                    continue
                for g in set(pat.findall(txt)):
                    hits[g].append(rel(p))
    return hits


def find_spec(name):
    if name.endswith(".sfx.json"):
        return name
    for root in (sfx_root(), "Assets"):
        hits = glob.glob(os.path.join(root, "**", f"{name}.sfx.json"), recursive=True)
        if hits:
            return hits[0]
    sys.exit(f"no spec {name}.sfx.json under {sfx_root()} or Assets/")


def pool_files(folder, base):
    """<base>.wav / <base>_<i>.wav in folder, in variant order (never another part's files)."""
    pat = re.compile(rf"{re.escape(base)}(?:_(\d+))?\.wav")
    hits = [(int(m.group(1) or 0), p) for p in glob.glob(os.path.join(folder, f"{base}*.wav"))
            if (m := pat.fullmatch(os.path.basename(p)))]
    return [p.replace("\\", "/") for _, p in sorted(hits)]


# ============================================================================ layout
def layout(a):
    """(out_dir, base, file namer) of a make run."""
    if re.search(r"[\\/]", a.name or "") or not snake(a.name or ""):
        sys.exit(f"make: --name must be a plain name like PurchaseDone or Fireball (got {a.name})")
    part = (a.part or "").strip().lower()
    if part and not re.fullmatch(r"[a-z0-9_]+", part):
        sys.exit(f"make: --part must be letters/digits/_ (got {a.part})")
    out_dir = os.path.join(sfx_root(a.out), a.name).replace("\\", "/")
    if a.event:
        base = f"sfx_{snake(a.name)}_{a.event}" + (f"_{part}" if part else "")
        return out_dir, base, lambda i, n: f"{base}_{i + 1}.wav"
    base = f"sfx_{snake(a.name)}" + (f"_{part}" if part else "")
    return out_dir, base, lambda i, n: f"{base}.wav" if n == 1 else f"{base}_{i + 1}.wav"


# ============================================================================ synthesis
def render(recipe, element, variants, life, pitch, seed, tint, lufs):
    """Master N variants. Returns [(signal, shaved_db)]."""
    R = RECIPES[recipe]
    k = (life / R["life"]) if life else 1.0
    out = []
    jit_rng = np.random.default_rng(seed * 7919 + 1)
    for i in range(variants):
        rng = np.random.default_rng(seed * 1000 + i)
        jitter = 0.0 if i == 0 else float(jit_rng.uniform(-R["vary"], R["vary"]))
        c = Ctx(rng, element, k, pitch, jitter, tint)
        x = np.asarray(R["fn"](c), float)
        if R["loop"]:
            y, shaved = S.master(x, lufs)
        else:
            x = S.fade(x, 0.0005, 0.006)
            if R["reverb"]:
                dec, wet = R["reverb"]
                x = S.reverb(x, rng, decay=dec * min(max(k, 0.6), 1.6), wet=wet)
            y, shaved = S.master(x, lufs)
            y = S.fade(S.trim_tail(y), 0.0005, 0.006)
        out.append((y, shaved))
    return out


def qa(sigs, R, lufs):
    """Per-variant findings + set-level findings. Returns (rows, set_flags)."""
    kind = KINDS[R["kind"]]
    rows = []
    for y, shaved in sigs:
        a = S.analyze(y)
        f = []
        if abs(a["lufs"] - lufs) > 1.5:
            f.append("LOUDNESS")
        if a["peak_db"] < -30:
            f.append("SILENT")
        if not R["loop"]:
            if a["start_abs"] > 0.02:
                f.append("CLICK_START")
            if a["end_level_db"] > -35:             # still sounding when the buffer ends = audible truncation
                f.append("CUT_TAIL")
            if not R["soft"] and R["kind"] in ("ui_short", "shot", "hit", "explode") and a["onset_ms"] > 12:
                f.append("LATE_ONSET")
        else:
            step = np.median(np.abs(np.diff(y))) + 1e-4
            if abs(y[0] - y[-1]) > 6 * step + 0.01:
                f.append("LOOP_SEAM")
        # phone speakers reproduce ~nothing under 300 Hz: hits/shots/UI must keep their loudness above it
        if a["phone_loss_db"] > (4 if kind["frequent"] or R["kind"] == "ui_short" else 8):
            f.append("PHONE_WEAK")
        elif a["phone_loss_db"] > (2.5 if kind["frequent"] or R["kind"] == "ui_short" else 5):
            f.append("BASS_HEAVY")
        if a["dur"] > kind["max_dur"]:
            f.append("TOO_LONG")
        if abs(a["dc"]) > 0.003:
            f.append("DC")
        if shaved > 6:
            f.append("LIMITED")
        if kind["frequent"] and a["centroid_hz"] > 5000:      # frequent sounds above 5 kHz fatigue at 10 per second
            f.append("HARSH")
        a["shaved_db"] = round(shaved, 1)
        rows.append((a, f))
    set_flags = []
    if len(sigs) > 1:
        worst = 0.0
        for i in range(len(sigs)):
            for j in range(i + 1, len(sigs)):
                p, q = sigs[i][0], sigs[j][0]
                n = min(len(p), len(q))
                cc = np.dot(p[:n], q[:n]) / (np.linalg.norm(p[:n]) * np.linalg.norm(q[:n]) + 1e-12)
                worst = max(worst, abs(cc))
        if worst > 0.97:
            set_flags.append(f"SAME_VARIANTS(corr {worst:.2f})")
    return rows, set_flags


# ============================================================================ review images / audition
def _spectrogram(x, w, h):
    nfft, hop = 1024, 256
    if len(x) < nfft:
        x = np.pad(x, (0, nfft - len(x)))
    frames = 1 + (len(x) - nfft) // hop
    win = np.hanning(nfft)
    spec = np.array([np.abs(np.fft.rfft(x[i * hop:i * hop + nfft] * win)) for i in range(frames)]).T
    freqs = np.fft.rfftfreq(nfft, 1 / S.SR)
    rows = np.geomspace(50, 20000, h)[::-1]
    idx = np.clip(np.searchsorted(freqs, rows), 0, len(freqs) - 1)
    mag = 20 * np.log10(spec[idx] + 1e-9)
    mag = np.clip((mag - (mag.max() - 70)) / 70, 0, 1)
    cols = np.clip((np.arange(w) * frames / w).astype(int), 0, frames - 1)
    m = mag[:, cols]
    stops = np.array([[8, 8, 16], [70, 20, 110], [200, 60, 80], [250, 150, 40], [255, 250, 200]], float)
    pos = m * (len(stops) - 1)
    lo = np.floor(pos).astype(int).clip(0, len(stops) - 2)
    t = (pos - lo)[..., None]
    return (stops[lo] * (1 - t) + stops[lo + 1] * t).astype(np.uint8)


def review_sheet(rows, out_png, title):
    """rows = [(label, signal, stats, flags, is_ref)] -> waveform + spectrogram strips on one PNG."""
    from PIL import Image, ImageDraw, ImageFont
    try:
        font = ImageFont.truetype("Arial.ttf", 13)
    except Exception:
        font = ImageFont.load_default()
    tmax = max(len(r[1]) for r in rows) / S.SR
    LW, WW, SW, H, PAD = 360, 330, 460, 92, 6
    img = Image.new("RGB", (LW + WW + SW + PAD * 4, 30 + len(rows) * (H + PAD)), (22, 24, 30))
    d = ImageDraw.Draw(img)
    d.text((PAD, 8), f"{title}   (time axis = {tmax:.2f}s for every row)", fill=(230, 230, 230), font=font)
    for r, (label, x, st, flags, is_ref) in enumerate(rows):
        y0 = 30 + r * (H + PAD)
        d.rectangle([PAD, y0, LW, y0 + H], fill=(40, 34, 30) if is_ref else (28, 38, 32))
        lines = [("REF  " if is_ref else "NEW  ") + label,
                 f"{st['dur']:.2f}s  LUFS {st['lufs']:.1f}  peak {st['peak_db']:.1f}",
                 f"onset {st['onset_ms']:.0f}ms tail {st['tail_s']:.2f}s bright {st['centroid_hz']} phone-{st['phone_loss_db']}dB"]
        if flags:
            lines.append("!! " + " ".join(flags))
        for i, ln in enumerate(lines):
            col = (255, 120, 110) if ln.startswith("!!") else (225, 225, 225)
            d.text((PAD + 6, y0 + 6 + i * 19), ln[:56], fill=col, font=font)
        wx = LW + PAD * 2
        d.rectangle([wx, y0, wx + WW, y0 + H], fill=(14, 16, 20))
        n = len(x)
        px = max(1, int(WW * (n / S.SR) / tmax))
        for i in range(px):
            seg = x[int(i * n / px):max(int((i + 1) * n / px), int(i * n / px) + 1)]
            v = float(np.max(np.abs(seg)))
            d.line([wx + i, y0 + H / 2 - v * H / 2, wx + i, y0 + H / 2 + v * H / 2],
                   fill=(230, 160, 80) if is_ref else (110, 220, 140))
        sx = wx + WW + PAD
        spw = max(2, int(SW * (n / S.SR) / tmax))
        img.paste(Image.fromarray(_spectrogram(x, spw, H)), (sx, y0))
    img.save(out_png)


def audition(refs, sigs, out_wav, gap=0.45):
    """refs then new variants, separated by silence: A/B by ear."""
    parts = []
    for x in refs + [""] + sigs:
        if isinstance(x, str):
            parts.append(np.zeros(S.ns(0.6)))
            continue
        parts += [x, np.zeros(S.ns(gap))]
    S.write_wav(out_wav, np.clip(np.concatenate(parts), -1, 1))


def burst(sigs, rate, cooldown, seed, out_wav, d=1.6):
    """Busy-scene stacking test: random triggers at `rate`/s through the cooldown, summed like PlayOneShot."""
    rng = np.random.default_rng(seed)
    out = np.zeros(S.ns(d) + max(len(s) for s in sigs))
    t, last = 0.0, -1e9
    while t < d:
        if t - last >= cooldown:
            s = sigs[rng.integers(len(sigs))]
            o = S.ns(t) if t > 0 else 0
            out[o:o + len(s)] += s
            last = t
        t += rng.exponential(1.0 / rate)
    pk = S.db(np.max(np.abs(out)))
    S.write_wav(out_wav, np.tanh(out))
    return round(pk, 1)


def reference_rows(kind, style):
    """The game's own clips for this kind (ArtStyle.md §6c `ref <kind>:`); missing / undecodable files are skipped."""
    rows, missing = [], []
    for p in style["refs"].get(kind, []):
        x = load_audio(p) if os.path.exists(p) else None
        if x is not None and len(x):
            rows.append((os.path.basename(p), x))
        else:
            missing.append(p)
    return rows, missing


def write_review(spec, sigs, rows, set_flags, R):
    base = spec["base"]
    pdir = os.path.join(PREVIEW_ROOT, base)
    os.makedirs(pdir, exist_ok=True)
    refs, missing = reference_rows(R["kind"], parse_style())
    sheet_rows = [(n, x, S.analyze(x), [], True) for n, x in refs]
    for (y, _), (st, fl), f in zip(sigs, rows, spec["files"]):
        sheet_rows.append((os.path.basename(f["path"]), y, st, fl, False))
    png = os.path.join(pdir, f"{base}.review.png")
    title = spec.get("title") or f"{spec['recipe']} × {spec['element']}"
    review_sheet(sheet_rows, png, f"{base}  ·  {title}  ·  target {spec['lufs']} LUFS")
    aud = os.path.join(pdir, f"{base}.audition.wav")
    audition([x for _, x in refs], [y for y, _ in sigs], aud)
    out = {"review_png": rel(png), "audition_wav": rel(aud),
           "refs": [n for n, _ in refs] or f"(none: no `ref {R['kind']}:` in {ART_STYLE} §6c)"}
    if missing:
        out["refs_missing"] = missing
    rate = KINDS[R["kind"]]["burst"]
    if rate:
        bw = os.path.join(pdir, f"{base}.burst.wav")
        cd = spec.get("cooldown")
        cd = DEFAULT_COOLDOWN.get(spec.get("event") or "hit", 0.06) if cd is None else cd
        pk = burst([y for y, _ in sigs], rate, cd, 3, bw)
        out["burst_wav"] = rel(bw)
        out["burst_peak_db"] = pk
        if refs:
            ref_pk = burst([x for _, x in refs], rate, cd, 3, os.path.join(pdir, "_ref_burst.wav"))
            out["ref_burst_peak_db"] = ref_pk
            if pk > ref_pk + 3:
                set_flags.append(f"BURST_LOUD(stacked peak {pk}dB vs refs {ref_pk}dB: raise --cooldown or lower --lufs)")
    return out


# ============================================================================ wiring
def _clip(g):
    return f"    - {{fileID: 8300000, guid: {g}, type: 3}}"


def wire_list(prefab, field, guids, cooldown, append=False, dry=False, play_on_enable=False):
    """Put the clips into a List<SoundPlayCustomModel> field of the prefab (YAML edit of that MonoBehaviour only:
    never touches Transform/children). cooldown None = keep the replaced entry's Cooldown (else 0). Returns a report."""
    if not os.path.exists(prefab):
        sys.exit(f"wire: no prefab {prefab}")
    text = open(prefab, encoding="utf-8", newline="").read()
    nl = "\r\n" if "\r\n" in text else "\n"
    eof_nl = text.endswith(nl)
    lines = (text[: -len(nl)] if eof_nl else text).split(nl)
    starts = [i for i, ln in enumerate(lines) if ln == f"  {field}:" or ln == f"  {field}: []"]
    if not starts:
        sys.exit(f"wire: {prefab} has no '{field}' list. Add com.ezg.audio SoundPlayController to the object that "
                 f"plays the sound (or give the game's own sound component's list with --field / profile "
                 f"sfxEventFields), save the prefab, then re-run (unity-integration.md §4). A component inside a "
                 f"nested prefab must be wired in that prefab.")
    if len(starts) > 1:
        sys.exit(f"wire: {prefab} has {len(starts)} '{field}' fields (several sound components); wire it by hand.")
    i = starts[0]
    j = i + 1
    while j < len(lines) and not re.match(r"^  [A-Za-z_]", lines[j]) and not lines[j].startswith("---"):
        j += 1
    old = lines[i + 1:j] if lines[i].endswith(":") else []
    if cooldown is None:
        kept = [float(m.group(1)) for ln in old if (m := re.fullmatch(r"    Cooldown: ([-0-9.eE]+)", ln))]
        cooldown = kept[0] if kept and not append else 0.0
    entry = ["  - Clip:"] + [_clip(g) for g in guids] + ["    DelayStart: 0", f"    Cooldown: {cooldown:g}",
                                                          "    IsLoop: 0"]
    new = [f"  {field}:"] + (old if append else []) + entry
    lines[i:j] = new
    # same MonoBehaviour document: SoundPlayController shows the Customs list in the Inspector only when
    # _soundTypes = 1 (flip it only if the Available list is empty); --play-on-enable sets _playOnEnable
    doc_start = max(k for k in range(i + 1) if lines[k].startswith("---"))
    doc_end = next((k for k in range(i + 1, len(lines)) if lines[k].startswith("---")), len(lines))
    flipped, poe = False, None
    for k in range(doc_start, doc_end):
        if lines[k] == "  _soundTypes: 0":
            avail = [ln for ln in lines[doc_start:doc_end] if ln.startswith("  _soundList:")]
            if avail and avail[0].strip() == "_soundList: []":
                lines[k] = "  _soundTypes: 1"
                flipped = True
        if play_on_enable and lines[k].startswith("  _playOnEnable:"):
            poe = lines[k].strip() != "_playOnEnable: 1"
            lines[k] = "  _playOnEnable: 1"
    old_guids = re.findall(r"guid: (\w{32})", "\n".join(old))
    if not dry:
        with open(prefab, "w", encoding="utf-8", newline="") as f:
            f.write(nl.join(lines) + (nl if eof_nl else ""))
    r = dict(prefab=prefab, field=field, clips=len(guids), cooldown=cooldown, append=append,
             replaced=[] if append else old_guids, soundTypes_set_customs=flipped, dry_run=dry)
    if play_on_enable:
        r["playOnEnable"] = "set" if poe else ("already on" if poe is False else "no _playOnEnable field here")
    return r


def _script_declares_clip(asset_text, field):
    """True when the asset's script (m_Script GUID -> .cs) declares `AudioClip <field>` (field added to the class after
    the asset was last saved, so Unity has not written it yet)."""
    m = re.search(r"m_Script: \{fileID: 11500000, guid: (\w{32})", asset_text)
    if not m:
        return False
    for root in ("Assets", "Packages"):
        for meta in glob.glob(os.path.join(root, "**", "*.cs.meta"), recursive=True):
            if meta_guid(meta[:-5]) == m.group(1):
                src = open(meta[:-5], encoding="utf-8", errors="ignore").read()
                return re.search(rf"\bAudioClip\s+{re.escape(field)}\s*[;=]", src) is not None
    return False


def wire_field(asset, field, guid, dry=False):
    """Set one scalar AudioClip field (SoundConfig.<Field> or any ScriptableObject / MonoBehaviour field). A field the
    asset has not serialized yet is appended when the script declares it (single-object assets only)."""
    if not os.path.exists(asset):
        sys.exit(f"wire: no asset {asset} (set soundConfigAsset in {PROFILE} or pass --asset)")
    text = open(asset, encoding="utf-8", newline="").read()
    ref = f"{{fileID: 8300000, guid: {guid}, type: 3}}"
    pat = re.compile(rf"^(  {re.escape(field)}: )\{{fileID: [^}}]*\}}", re.M)
    m = pat.search(text)
    if m:
        old = re.findall(r"guid: (\w{32})", m.group(0))
        text = pat.sub(lambda mm: mm.group(1) + ref, text, count=1)
        added = False
    else:
        if text.count("\n--- !u!") != 1 or not _script_declares_clip(text, field):
            sys.exit(f"wire: {asset} has no scalar AudioClip field '{field}' (`config` lists them; arrays / nested / "
                     f"prefab fields: set them in Unity)")
        nl = "\r\n" if "\r\n" in text else "\n"
        text = text.rstrip("\r\n") + f"{nl}  {field}: {ref}{nl}"
        old, added = [], True
    if not dry:
        with open(asset, "w", encoding="utf-8", newline="") as f:
            f.write(text)
    return dict(asset=asset, field=field, replaced=old, added_field=added, dry_run=dry)


def clip_fields(asset):
    """{field: 'set' | 'empty'} for the scalar object-reference fields of a single-object asset."""
    if not os.path.exists(asset):
        return None
    out = {}
    for m in re.finditer(r"^  ([A-Za-z_]\w*): \{fileID: (-?\d+)", open(asset, encoding="utf-8", errors="ignore").read(),
                         re.M):
        if not m.group(1).startswith("m_"):
            out[m.group(1)] = "empty" if m.group(2) == "0" else "set"
    return out


def cleanup_stale(out_dir, base, keep):
    """Delete variants of the same base left over from an earlier, larger make — only when nothing references them."""
    stale = [p for p in pool_files(out_dir, base) if os.path.basename(p) not in keep]
    if not stale:
        return [], []
    gmap = {meta_guid(p): p for p in stale if meta_guid(p)}
    refs = guid_refs(list(gmap))
    deleted, kept = [], []
    for p in stale:
        g = meta_guid(p)
        if g and refs.get(g):
            kept.append(f"{rel(p)} (still used by {', '.join(refs[g][:3])})")
            continue
        for q in (p, p + ".meta"):
            if os.path.exists(q):
                os.remove(q)
        deleted.append(rel(p))
    return deleted, kept


# ============================================================================ ingest (found sounds)
def sfx_license():
    v = str(load_profile().get("sfxLicense") or DEFAULT_LICENSE).lower()
    return v if v in ("cc0", "any") else DEFAULT_LICENSE


def ingest_recipe(like, kind, soft):
    """QA profile of an ingested sound: the matching recipe (--like) or a bare kind (--kind)."""
    if like:
        if like not in RECIPES:
            sys.exit(f"ingest: unknown recipe {like} for --like (see `list`)")
        return RECIPES[like]
    if kind not in KINDS:
        sys.exit(f"ingest: --like <recipe> or --kind <{'|'.join(KINDS)}>")
    return dict(kind=kind, loop=kind == "loop", soft=bool(soft), lufs=KIND_LUFS[kind], variants=3, event=None,
                element="-")


def spec_recipe(spec):
    if spec.get("source") == "ingest":
        return ingest_recipe(spec.get("like"), spec.get("kind"), spec.get("soft"))
    return RECIPES[spec["recipe"]]


def load_credits(paths):
    """{file name | str(id): manifest entry} from the ezg-sfx sfx-credits.json files given / next to the sources."""
    by = {}
    for p in paths:
        if not p or not os.path.exists(p):
            continue
        try:
            data = json.load(open(p, encoding="utf-8"))
        except ValueError as e:
            sys.exit(f"ingest: {p} is not valid JSON ({e})")
        for e in data if isinstance(data, list) else (data.get("sounds") or data.get("items") or []):
            if isinstance(e, dict):
                if e.get("file"):
                    by[os.path.basename(e["file"])] = e
                if e.get("id") is not None:
                    by[str(e["id"])] = e
    return by


def credit_for(src, by):
    b = os.path.basename(src)
    if b in by:
        return by[b]
    m = re.search(r"_(\d+)\.\w+$", b)               # MCP file names end in _<freesound id>.<ext>
    return by.get(m.group(1)) if m else None


def trim_head(x, floor_db, pre=0.002):
    """Cut leading silence (keeps `pre` s before the first sample above floor_db re peak)."""
    thr = np.max(np.abs(x)) * 10 ** (floor_db / 20)
    idx = np.where(np.abs(x) > thr)[0]
    return x if not len(idx) else x[max(0, idx[0] - S.ns(pre)):]


def conform(x, R, lufs, pitch):
    """A found sound -> the same shape `render` gives a synthesised one: no lead-in silence, pitch offset, loudness
    target, trimmed tail, 0.5 ms / 6 ms fades. A source that stops while still sounding (cut preview, --end) gets a
    short fade-out to silence instead of an audible cut. Loops keep their length (the seam is the source's)."""
    if not R["loop"]:
        x = trim_head(x, -55.0 if R["soft"] else -40.0)
    if pitch:
        x = S.resample(x, 2 ** (pitch / 12))
    if R["loop"]:
        return S.master(x, lufs)
    y, shaved = S.master(S.fade(x, 0.0005, 0.006), lufs)
    y = S.fade(S.trim_tail(y), 0.0005, 0.006)
    if S.analyze(y)["end_level_db"] > -35:
        y, more = S.master(S.fade(y, 0.0005, min(0.04, 0.3 * len(y) / S.SR)), lufs)
        y, shaved = np.concatenate([y, np.zeros(S.ns(0.02))]), max(shaved, more)
    return y, shaved


def merge_credits(out_dir, base, files, sources, dry=False):
    """<out_dir>/sfx-credits.json: one entry per Freesound id with the clips of this folder that use it."""
    path = os.path.join(out_dir, CREDITS_FILE)
    try:
        old = json.load(open(path, encoding="utf-8")) if os.path.exists(path) else []
    except ValueError:
        old = []
    pat = re.compile(rf"{re.escape(base)}(?:_\d+)?\.wav")
    entries = {}
    for e in old if isinstance(old, list) else []:
        e = dict(e)
        e["clips"] = [c for c in e.get("clips", []) if not pat.fullmatch(c)]
        if e["clips"]:
            entries[str(e.get("id"))] = e
    for f, src in zip(files, sources):
        if not src.get("id"):
            continue
        e = entries.setdefault(str(src["id"]), {k: v for k, v in src.items() if k not in ("src", "pitch")})
        e.setdefault("clips", [])
        if os.path.basename(f["path"]) not in e["clips"]:
            e["clips"].append(os.path.basename(f["path"]))
    if not entries and not os.path.exists(path):
        return None
    if not dry:
        with open(path, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(sorted(entries.values(), key=lambda e: str(e.get("id"))), fh, indent=2, ensure_ascii=False)
    return rel(path)


def finish(a, R, spec, report, sigs, files, set_flags, style_warn):
    """Shared tail of make / ingest: report lines, review, wiring, stale variants, spec, QA verdict."""
    out_dir, base = report["dir"], report["base"]
    for f in files:
        report["files"].append(f"{f['path']}  {f['stats']['dur']:.2f}s  LUFS {f['stats']['lufs']:.1f}  "
                               f"peak {f['stats']['peak_db']:.1f}  bright {f['stats']['centroid_hz']}Hz  phone -{f['stats']['phone_loss_db']}dB  "
                               f"{'meta:new' if f['new_meta'] else 'meta:kept'}" + (
                                   f"  !! {' '.join(f['flags'])}" if f['flags'] else ""))
    if style_warn:
        report["style_warn"] = style_warn
    if not a.no_review:
        report["review"] = write_review(spec, sigs, [(f["stats"], f["flags"]) for f in files], set_flags, R)
    spec["set_flags"] = set_flags

    if a.prefab:
        report["wire"] = wire_list(a.prefab, event_field(a.event, a.field), [f["guid"] for f in files],
                                   spec["cooldown"], a.append, a.dry_run, a.play_on_enable)
        spec["wired"] = report["wire"]
        spec["cooldown"] = report["wire"]["cooldown"]
    if a.config:
        report["wire"] = wire_field(a.asset or sound_config(), a.config, files[0]["guid"], a.dry_run)
        spec["wired"] = report["wire"]
    if (a.prefab or a.config) and not a.dry_run:
        deleted, kept = cleanup_stale(out_dir, base, {os.path.basename(f["path"]) for f in files})
        if deleted or kept:
            report["stale_variants"] = {"deleted": deleted, "kept": kept}
    with open(os.path.join(out_dir, f"{base}.sfx.json"), "w", encoding="utf-8", newline="\n") as fh:
        json.dump(spec, fh, indent=2, ensure_ascii=False)
    if a.event:
        others = sorted(p for p in glob.glob(os.path.join(out_dir, "*")) if not p.endswith(".meta")
                        and not os.path.basename(p).startswith(base) and os.path.basename(p) != CREDITS_FILE)
        if others:
            report["other_files_in_folder"] = [rel(p) for p in others]

    blocking = sorted({fl for f in files for fl in f["flags"] if fl in BLOCKING})
    report["qa"] = ("FAIL " + " ".join(blocking)) if blocking else "PASS"
    warns = sorted({fl for f in files for fl in f["flags"] if fl not in BLOCKING}) + set_flags
    if warns:
        report["qa_warn"] = warns
    print(json.dumps(report, indent=2, ensure_ascii=False))
    return 1 if blocking else 0


# ============================================================================ commands
def cmd_list(_):
    print("recipes (name · kind · default element · default event · life · LUFS · variants):")
    for n, R in RECIPES.items():
        print(f"  {n:13s} {R['kind']:9s} {R['element']:8s} {R['event'] or '-':5s} {R['life']:5.2f}s "
              f"{R['lufs']:4d}  x{R['variants']}  {R['desc']}")
    print("\nelements:")
    for n, E in ELEMENTS.items():
        print(f"  {n:9s} pitch x{E['pitch']:.2f}  bright x{E['bright']:.2f}  {E['desc']}")
    print("\nevents (--event): fire = once per activation (cooldown 0) · shot = once per projectile / spawn (0.05 s) · "
          "hit = once per hit (0.06 s)")


def cmd_config(_):
    check_root()
    style = parse_style()
    sc = sound_config()
    out = dict(sfxRoot=sfx_root(), soundConfigAsset=sc, soundConfigFields=clip_fields(sc) or "(asset not found)",
               listField=DEFAULT_LIST_FIELD, sfxEventFields=load_profile().get("sfxEventFields") or {},
               bundle=detect_bundle(style) or "(none)", sfxLicense=sfx_license(),
               style=dict(source=style["source"] or f"(no ```sfx-style``` block in {ART_STYLE}: defaults, no REF)",
                          default=style["default"] or "(recipe default)", allow=style["allow"] or "(all)",
                          offsets=style["offsets"],
                          refs={k: [p + ("" if os.path.exists(p) else "  (MISSING)") for p in v]
                                for k, v in style["refs"].items()},
                          kinds_without_ref=[k for k in KINDS if not style["refs"].get(k)]))
    if style["errors"]:
        out["style"]["errors"] = style["errors"]
    print(json.dumps(out, indent=2, ensure_ascii=False))
    return 1 if style["errors"] else 0


def cmd_make(a):
    check_root()
    if a.recipe not in RECIPES:
        sys.exit(f"make: unknown recipe {a.recipe} (see `list`)")
    R = RECIPES[a.recipe]
    style = parse_style()
    a.element = a.element or R["element"]
    if a.element not in ELEMENTS:
        sys.exit(f"make: unknown element {a.element} (see `list`)")
    if a.prefab and not a.event:
        a.event = R["event"]
    if a.prefab and a.config:
        sys.exit("make: --prefab (variant pool in a prefab list) and --config (one AudioClip field) are exclusive")
    out_dir, base, namer = layout(a)
    n = a.variants or (R["variants"] if a.event else 1)
    if a.config and n != 1:
        sys.exit("make: --config wires ONE clip; use --variants 1")
    seed = a.seed if a.seed is not None else zlib.crc32(base.encode()) % 10000
    lufs = lufs_target(R, style, a.lufs)
    bundle = detect_bundle(style) if a.bundle is None else ("" if a.bundle == "none" else a.bundle)
    # no --cooldown: the event's default; free mode replacing a prefab list keeps the old entry's cooldown
    cooldown = a.cooldown if a.cooldown is not None else (DEFAULT_COOLDOWN[a.event] if a.event else None)

    sigs = render(a.recipe, a.element, n, a.life, a.pitch, seed, a.tint, lufs)
    rows, set_flags = qa(sigs, R, lufs)

    os.makedirs(out_dir, exist_ok=True)
    files = []
    for i, ((y, _), (st, fl)) in enumerate(zip(sigs, rows)):
        p = os.path.join(out_dir, namer(i, n)).replace("\\", "/")
        S.write_wav(p, y)
        g, new_meta = ensure_meta(p, bundle)
        files.append(dict(path=p, guid=g, new_meta=new_meta, stats=st, flags=fl))
    command = "make " + " ".join(sys.argv[2:])
    spec = dict(base=base, recipe=a.recipe, element=a.element, kind=R["kind"], name=a.name, event=a.event,
                part=a.part, variants=n, life=a.life or R["life"], pitch=a.pitch, seed=seed, tint=a.tint,
                lufs=lufs, cooldown=cooldown, bundle=bundle, loop=R["loop"], command=command, files=files)

    report = {"base": base, "dir": out_dir, "recipe": a.recipe, "element": a.element, "event": a.event,
              "lufs_target": lufs, "seed": seed, "bundle": bundle or "(none)", "files": []}
    style_warn = list(style["errors"])
    if style["allow"] and a.element not in style["allow"]:
        style_warn.append(f"element {a.element} is not in ArtStyle.md §6c `allow` ({', '.join(style['allow'])})")
    return finish(a, R, spec, report, sigs, files, set_flags, style_warn)


def cmd_ingest(a):
    check_root()
    srcs = [p.strip() for p in (a.src or "").split(",") if p.strip()]
    if not srcs:
        sys.exit("ingest: --src <file>[,<file>…] (the downloaded previews, or a file the dev gave)")
    for p in srcs:
        if not os.path.exists(p):
            sys.exit(f"ingest: {p} not found")
    if a.like and a.kind:
        sys.exit("ingest: --like <recipe> or --kind <kind>, not both")
    R = ingest_recipe(a.like, a.kind, a.soft)
    if a.prefab and a.config:
        sys.exit("ingest: --prefab (variant pool in a prefab list) and --config (one AudioClip field) are exclusive")
    if a.prefab and not a.event:
        a.event = R["event"]
    style = parse_style()
    out_dir, base, namer = layout(a)
    n = a.variants or (len(srcs) if len(srcs) > 1 else (max(3, R["variants"]) if a.event else 1))
    if a.config and n != 1:
        sys.exit("ingest: --config wires ONE clip; give one --src and --variants 1")
    if n > len(srcs) * len(VARIANT_SPREAD):
        sys.exit(f"ingest: at most {len(srcs) * len(VARIANT_SPREAD)} variants from {len(srcs)} source(s)")
    lufs = lufs_target(R, style, a.lufs)
    bundle = detect_bundle(style) if a.bundle is None else ("" if a.bundle == "none" else a.bundle)
    cooldown = a.cooldown if a.cooldown is not None else (DEFAULT_COOLDOWN[a.event] if a.event else None)
    lic = sfx_license()
    credits = load_credits([a.credits] + [os.path.join(os.path.dirname(p), CREDITS_FILE) for p in srcs])

    decoded, sources, sigs = {}, [], []
    for i in range(n):
        src = srcs[i % len(srcs)]
        if src not in decoded:
            x = load_audio(src)
            if x is None or not len(x):
                sys.exit(f"ingest: cannot decode {src} (OGG needs ffmpeg; macOS afconvert reads MP3/WAV/AIFF: fetch "
                         f"the previews with format mp3, or install ffmpeg)")
            s0 = S.ns(a.start) if a.start else 0
            s1 = S.ns(a.end) if a.end else len(x)
            if s1 - s0 < S.ns(0.005):
                sys.exit(f"ingest: --start/--end leave nothing of {src} ({len(x) / S.SR:.2f}s long)")
            decoded[src] = x[s0:s1]
        pitch = a.pitch + VARIANT_SPREAD[i // len(srcs)]
        sigs.append(conform(decoded[src], R, lufs, pitch))
        c = credit_for(src, credits) or {}
        sources.append(dict(src=rel(src), pitch=round(pitch, 2), id=c.get("id"), name=c.get("name"),
                            author=c.get("author"), license=c.get("license"), credit_required=c.get("credit_required"),
                            attribution=c.get("attribution"), url=c.get("source")))
    rows, set_flags = qa(sigs, R, lufs)

    license_warn = []
    for src_info, (_, fl) in zip(sources, rows):
        if src_info["id"] is None:
            license_warn.append(f"{src_info['src']}: no license record (no {CREDITS_FILE} entry): only for a file "
                                f"the dev owns")
        elif src_info["credit_required"] and lic == "cc0":
            fl.append("LICENSE")
    if any("LICENSE" in fl for _, fl in rows):
        license_warn.append(f"CC BY source while {PROFILE} `sfxLicense` is cc0 (no credits screen): pick a CC0 sound, "
                            f"or set sfxLicense \"any\" once the game shows credits")

    os.makedirs(out_dir, exist_ok=True)
    files = []
    for i, ((y, _), (st, fl)) in enumerate(zip(sigs, rows)):
        p = os.path.join(out_dir, namer(i, n)).replace("\\", "/")
        S.write_wav(p, y)
        g, new_meta = ensure_meta(p, bundle)
        files.append(dict(path=p, guid=g, new_meta=new_meta, stats=st, flags=fl, source=sources[i]["src"]))
    ids = sorted({s["id"] for s in sources if s["id"] is not None})
    title = (("freesound " + " ".join(f"#{i}" for i in ids)) if ids else "file " + ", ".join(
        os.path.basename(p) for p in srcs)) + (f" · like {a.like}" if a.like else f" · {R['kind']}")
    spec = dict(base=base, source="ingest", like=a.like, kind=R["kind"], soft=bool(R["soft"]), recipe=a.like or "-",
                element="-", name=a.name, event=a.event, part=a.part, variants=n, pitch=a.pitch, start=a.start,
                end=a.end, lufs=lufs, cooldown=cooldown, bundle=bundle, loop=R["loop"], license_policy=lic,
                title=title, command="ingest " + " ".join(sys.argv[2:]), sources=sources, files=files)
    report = {"base": base, "dir": out_dir, "source": title, "kind": R["kind"], "event": a.event, "lufs_target": lufs,
              "bundle": bundle or "(none)", "license_policy": lic, "files": []}
    credited = [s for s in sources if s["credit_required"]]
    if credited:
        report["credit_required"] = sorted({s["attribution"] or s["url"] for s in credited})
    report["credits_file"] = merge_credits(out_dir, base, files, sources)
    if license_warn:
        report["license_warn"] = license_warn
    return finish(a, R, spec, report, sigs, files, set_flags, list(style["errors"]))


def cmd_wire(a):
    check_root()
    if a.config:
        if not a.file:
            sys.exit("wire --config needs --file <clip.wav>")
        g = meta_guid(a.file)
        if not g:
            sys.exit(f"wire: {a.file} has no .meta yet (run make, or let Unity import it first)")
        print(json.dumps(wire_field(a.asset or sound_config(), a.config, g, a.dry_run), indent=2))
        return 0
    if not a.prefab:
        sys.exit("wire: --prefab <path> (--name <Name> [--event E] [--part P] | --file a.wav,b.wav)  "
                 "or  --config <Field> --file <clip.wav>")
    if a.file:
        paths = a.file.split(",")
    elif a.name:
        base = f"sfx_{snake(a.name)}" + (f"_{a.event}" if a.event else "") + (f"_{a.part}" if a.part else "")
        paths = pool_files(os.path.join(sfx_root(a.out), a.name), base)
    else:
        sys.exit("wire --prefab needs --name <Name> [--event E] or --file <clips>")
    guids = [meta_guid(p) for p in paths]
    if not paths or not all(guids):
        sys.exit(f"wire: no clips with .meta found ({paths or 'nothing matched'})")
    cd = a.cooldown if a.cooldown is not None else (DEFAULT_COOLDOWN[a.event] if a.event else None)
    r = wire_list(a.prefab, event_field(a.event, a.field), guids, cd, a.append, a.dry_run, a.play_on_enable)
    r["files"] = [rel(p) for p in paths]
    print(json.dumps(r, indent=2))
    return 0


def cmd_review(a):
    check_root()
    spec = json.load(open(find_spec(a.target), encoding="utf-8"))
    R = spec_recipe(spec)
    sigs = [(S.read_wav(f["path"]), 0.0) for f in spec["files"]]
    rows, set_flags = qa(sigs, R, spec["lufs"])
    print(json.dumps(write_review(spec, sigs, rows, set_flags, R) | {"set_flags": set_flags}, indent=2,
                     ensure_ascii=False))
    return 0


def cmd_analyze(a):
    for p in a.paths:
        x = load_audio(p)
        if x is None:
            print(f"{p}: cannot decode (MP3/OGG need macOS afconvert or ffmpeg)")
            continue
        st = S.analyze(x)
        print(f"{os.path.basename(p):40s} {st['dur']:5.2f}s  LUFS {st['lufs']:6.1f}  peak {st['peak_db']:6.1f}  "
              f"onset {st['onset_ms']:4.0f}ms  attack {st['attack_ms']:4.0f}ms  tail {st['tail_s']:.2f}s  "
              f"bright {st['centroid_hz']:5d}Hz  phone -{st['phone_loss_db']}dB")
    return 0


def cmd_play(a):
    paths = []
    for t in a.targets:
        if t.lower().endswith(AUDIO_EXT):
            paths.append(t)
        else:
            paths.append(os.path.join(PREVIEW_ROOT, t, f"{t}.audition.wav"))
    for p in paths:
        if not os.path.exists(p):
            sys.exit(f"play: {p} not found (run make / review first)")
        print("play", p)
        if sys.platform == "darwin":
            subprocess.run(["afplay", p])
        elif os.name == "nt":
            import winsound
            winsound.PlaySound(p, winsound.SND_FILENAME)
        else:
            player = shutil.which("paplay") or shutil.which("aplay")
            if not player:
                sys.exit("play: no audio player (paplay/aplay)")
            subprocess.run([player, p])
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("list")
    sub.add_parser("config")
    m = sub.add_parser("make")
    m.add_argument("--recipe", required=True)
    m.add_argument("--name", required=True, help="folder <sfxRoot>/<Name>/ + file names sfx_<name>…")
    m.add_argument("--element")
    m.add_argument("--event", choices=EVENTS, help="variant pool for a repeated trigger (default with --prefab: "
                                                   "the recipe's event)")
    m.add_argument("--part")
    m.add_argument("--variants", type=int)
    m.add_argument("--life", type=float, help="seconds (scales every duration of the recipe)")
    m.add_argument("--pitch", type=float, default=0.0, help="semitones")
    m.add_argument("--seed", type=int)
    m.add_argument("--tint", type=float, help="element tint amount 0..1.5 (default per element)")
    m.add_argument("--lufs", type=float, help="loudness target override")
    m.add_argument("--out", help="root instead of sfxRoot")
    m.add_argument("--bundle", help="asset bundle name, 'none' for no bundle (default: §6c `bundle`, else what the "
                                    "project's audio uses)")
    m.add_argument("--prefab", help="wire the pool into this prefab's List<SoundPlayCustomModel> field")
    m.add_argument("--field", help=f"list field to wire (default: profile sfxEventFields[event], else "
                                   f"{DEFAULT_LIST_FIELD})")
    m.add_argument("--cooldown", type=float, help="seconds between plays (default fire 0, shot .05, hit .06)")
    m.add_argument("--append", action="store_true", help="add as an extra layer instead of replacing the list")
    m.add_argument("--play-on-enable", action="store_true", help="also set SoundPlayController._playOnEnable")
    m.add_argument("--config", help="AudioClip field of the SoundConfig asset to wire (e.g. OpenPopup)")
    m.add_argument("--asset", help="asset holding the --config field (default: profile soundConfigAsset)")
    m.add_argument("--dry-run", action="store_true")
    m.add_argument("--no-review", action="store_true")
    g = sub.add_parser("ingest", help="master + QA + wire found sounds (ezg-sfx MCP previews, files from the dev)")
    g.add_argument("--src", required=True, help="source file(s), comma-separated: one per variant, in order")
    g.add_argument("--name", required=True, help="folder <sfxRoot>/<Name>/ + file names sfx_<name>…")
    g.add_argument("--like", help="recipe whose kind + loudness target + soft/loop flags apply (recipes.md §1)")
    g.add_argument("--kind", choices=list(KINDS), help="kind when no recipe matches (target: KIND_LUFS)")
    g.add_argument("--soft", action="store_true", help="with --kind: the sound swells in on purpose (no LATE_ONSET)")
    g.add_argument("--event", choices=EVENTS, help="variant pool for a repeated trigger (default with --prefab: "
                                                   "the --like recipe's event)")
    g.add_argument("--part")
    g.add_argument("--variants", type=int, help="pool size (default: one per --src; one source + --event: 3, "
                                                "made by small pitch offsets)")
    g.add_argument("--pitch", type=float, default=0.0, help="semitones on every variant")
    g.add_argument("--start", type=float, help="seconds: cut the source from here (a file holding several sounds)")
    g.add_argument("--end", type=float, help="seconds: cut the source here (faded out when still sounding)")
    g.add_argument("--lufs", type=float, help="loudness target override")
    g.add_argument("--credits", help=f"license manifest (default: {CREDITS_FILE} next to each source)")
    g.add_argument("--out", help="root instead of sfxRoot")
    g.add_argument("--bundle", help="asset bundle name, 'none' for no bundle")
    g.add_argument("--prefab")
    g.add_argument("--field")
    g.add_argument("--cooldown", type=float)
    g.add_argument("--append", action="store_true")
    g.add_argument("--play-on-enable", action="store_true")
    g.add_argument("--config")
    g.add_argument("--asset")
    g.add_argument("--dry-run", action="store_true")
    g.add_argument("--no-review", action="store_true")
    w = sub.add_parser("wire")
    w.add_argument("--prefab")
    w.add_argument("--name")
    w.add_argument("--event", choices=EVENTS)
    w.add_argument("--part")
    w.add_argument("--file", help="clip path(s), comma-separated")
    w.add_argument("--out")
    w.add_argument("--field")
    w.add_argument("--cooldown", type=float)
    w.add_argument("--append", action="store_true")
    w.add_argument("--play-on-enable", action="store_true")
    w.add_argument("--config")
    w.add_argument("--asset")
    w.add_argument("--dry-run", action="store_true")
    r = sub.add_parser("review")
    r.add_argument("target")
    an = sub.add_parser("analyze")
    an.add_argument("paths", nargs="+")
    p = sub.add_parser("play")
    p.add_argument("targets", nargs="+")
    a = ap.parse_args()
    return {"list": cmd_list, "config": cmd_config, "make": cmd_make, "ingest": cmd_ingest, "wire": cmd_wire,
            "review": cmd_review, "analyze": cmd_analyze, "play": cmd_play}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main() or 0)
