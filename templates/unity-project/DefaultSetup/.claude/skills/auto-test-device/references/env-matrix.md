# Environment matrix — same feature, different conditions

Read at STEP 4 of SKILL.md. Each step is a real-device condition players have but the default QA device does not. Do it
after P0–P3, on the feature's 2–3 main screens; `env matrix` runs the steps declared in `qa.environments` itself, the rest
is done by hand. Every step is **reverted automatically** (`env matrix`) or you revert it right after; `stop` cleans up
leftovers.

| Step | Command | Which players hit it | What it exposes | Target |
|---|---|---|---|---|
| **Large system font** | `env font-scale 1.3` → text-heavy screens → `audit` → `env font-scale reset` | older players, accessibility settings (very common) | TMP without `autoSize` overflows buttons; LayoutGroups stretch over each other; long money amounts break the HUD; two-line buttons | Android |
| **Dark mode** | `env dark on` → screens with WebView/native popup/consent → `env dark off` | players at night | WebView/consent/native ads with black text on a black background; splash/system theme | Android |
| **Tall / short / tablet aspect** | `aspect tall` (20:9+) · `aspect short` (16:9) · `aspect tablet` · `aspect <WxH>` → walk the screens → `aspect reset` | a wide range of devices | bad anchoring (safe area, notch), odd gaps, buttons falling off screen, background not covering, wrong CanvasScaler match | Android (`wm size`) |
| **System animations off** | `env animator 0` → tap quickly through the flow → `env animator 1` | weak devices, accessibility; also exposes logic that waits for animations | tween `OnComplete` callbacks do not run → button stuck disabled, reward not shown, popup does not close; count-up jumps to a wrong number | Android |
| **OS kills the activity in the background** | `env kill-activities on` → `app background --secs 5` → come back → `env kill-activities off` | low-RAM devices, multitasking | open popup lost, ad being watched pays no reward, temporary state lost, black screen on return | Android |
| **Memory pressure** | `env trim-memory [level]` → play 1 minute → `logs`, `perf` | weak devices, many background apps | lost textures/atlases (white/black cells), native crash, LMK kills the app mid-transaction | Android |
| **Device clock change** | `env clock +1d` → daily/timers/events → `env clock reset` | players cheating with time | daily reward claimable again, timer back to 0, event expires while open, huge offline earnings — a game that guards against it must block it or use server time | emulator / root |
| **System language** | `env locale de-DE` (emulator) → reopen the app → text | players in other countries | wrong language auto-detection, untranslated text, number/date format | emulator |
| **Pseudo-loc** | `pseudoloc on` → every screen/popup → `audit` → `pseudoloc off` | every long-text language (German, Russian, Vietnamese with diacritics) | overflow/clipping/ugly wrapping; text that **does not change** = hardcoded text bypassing localize; glyphs missing for diacritics | probe ≥ 1.3 |
| **Network loss** | `net off` during a network/ad/IAP call → `net on` | subway, elevator | stuck loading, reward granted before the request succeeded, no retry button | Android |
| **Screen rotation** | `rotate landscape` → `rotate portrait` | games that allow rotation | broken layout, misplaced popups | Android |

A step not supported on the target → the tool prints `unsupported`, mark the matching case `skip`; on iOS Simulator switch
the device instead of `aspect`, a real iPhone cannot be changed.

## Pseudo-loc — why, and how to read the results

Pseudo-loc turns every string that goes through the localize system into a version ~30% longer + accents + brackets
`[Ṁüä Ğöĺď…]`. Benefits:

- **No need to change the game's language** (no touching the player's save/settings) and you still see the longest text.
- **Hardcoded text shows up at once:** any text that does not change does not go through localize (hardcoded in a prefab/
  code) → medium bug (`--type visual`, "chữ cứng không localize"). Except: numbers, proper names, symbols.
- Accents expose glyphs missing from the font (boxes) → missing fallback font.
- `mode expand` only lengthens (checks layout), `accent` only adds accents (checks fonts), `both` is the default.

How: turn it on → go through **every** screen/popup/state of the feature (including toasts, tooltips, error popups) →
`audit` on each screen (auto-audit already ran on screen change) → turn it off. For each `text-overflow` finding under
pseudo-loc: check whether it also happens in the longest real language the game supports — 30% pseudo-loc expansion
approximates German; for a game with only English + Vietnamese, a slight overflow under 10% can be lowered to low. Text that
overflows in the default language (no pseudo-loc) is always a bug.

Probe < 1.3 has no pseudo-loc: switch the in-game language instead (the longest-text language), switch back when done, write
"build lại với probe ≥ 1.3" in the report.

## Logging environment bugs

- Title (Vietnamese, ≤ 70 characters) includes the condition: `Popup nhiệm vụ: nút "Nhận" tràn khung khi cỡ chữ hệ thống 130%`.
- `--precondition` records the command/condition (`font-scale 1.3`, `aspect 720x1280`, `kill-activities on`).
- Reproduce: revert → turn it on again → redo once (is the bug caused by the condition or by the screen?). The bug also
  happens after reverting → not an environment bug, drop the condition from the title.
- Severity: usually **medium** (clearly wrong display under a real condition); flow stuck/reward lost under
  `kill-activities`/`animator 0` → **high**; a gain from changing the device clock when the game guards against it →
  **high**, the game does not guard against it → `gd`.
- The session's `env-matrix.md` lists step × screen × result — paste a summary into the report.
