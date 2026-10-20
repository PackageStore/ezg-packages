---
description: Decision layer for /new-ui — ground truth + spec-sheet gate (Checkpoint 0), templates, layout, layout-group-first assembly phases, script binding, final verify. Read by the create-ui skill, /refactor-ui, ui-visual-reviewer and /new-package. MCP tool sequences live in ui-mcp-playbook.md.
---

# New UI Guide (decision layer)

This is the **what-to-build** layer for [`/new-ui`](../commands/new-ui.md) (executed through the
[`create-ui`](../skills/create-ui/SKILL.md) skill). The deterministic **how** (Unity MCP tool sequence,
`m_*` paths, wiring, screenshot loop) is in [`ui-mcp-playbook.md`](ui-mcp-playbook.md); the verified
`screen_template` hierarchy and per-template catalog are in the create-ui skill's
[`references/prefab-templates.md`](../skills/create-ui/references/prefab-templates.md) and
[`references/mcp-playbook.md`](../skills/create-ui/references/mcp-playbook.md) §6.

Drive Unity through MCP tools, not the Inspector. Do not improvise where the playbook covers the operation.
The template's real hierarchy is the judge: when this file and a live prefab disagree, inspect the prefab
(`unity_prefab_info` / `unity_execute_code`), follow it, and fix this file.

---

## 0. Ground truth + spec-sheet + template + layout decision

### 0a. Ground truth — before touching Unity

Do not build from a text description alone; that is the main reason results drift from what the user wants.

**Preferred source — approved mockup from `/ui-mockup`** (`groundTruth=TechSpec/Mockups/<F>/<S>.png`): use sibling `<S>.ui-spec.json` as the authoritative numeric contract and `<S>.html` as its generated review. Run `python3 .claude/scripts/ui-spec-validator.py <spec-or-html> --mode build` before Unity. New `specVersion: 1` failures block the build. Legacy HTML containing only `<script id="spec">` remains compatible. If `groundTruth` is still `PENDING-*`: interactive → offer `/ui-mockup`; autonomous → log it and use the existing fallback with a capped visual ceiling.

**User supplied a reference** (mockup, Figma export, screenshot of a similar screen) → that image is the **visual truth** for layout, spacing, and color. Every phase checkpoint (§3) is graded against it, not against improvised judgment.

How the image reaches each mode:

| Mode | How to load ground truth |
|------|--------------------------|
| **Interactive** | User pastes/attaches the image in chat when invoking `/new-ui` — already in session context. If they give a file path, `Read` it once at the start so it is loaded, not just named. |
| **Autonomous (`/run-backlog`)** | No live chat. Image must exist as a file in the repo — normally the approved mockup `TechSpec/Mockups/<F>/<S>.png`, recorded at planning time in the task's `**Workflow args:**` as `<Feature> \| groundTruth=<path>`. Orchestrator passes that path as `groundTruth` to `ui-visual-reviewer`, which `Read`s it before comparing (see that agent's Step 1). `groundTruth=clone:<Prefab>` or no image → the §0c spec-sheet (extracted from that existing prefab) is the ground truth. |

**Art direction — `.claude/docs/ArtStyle.md`, read it before any reference hunt** (+ `Read` the board images it lists under `.claude/docs/ArtStyle/`). An approved mockup / artist PSD outranks it; everything else ranks below it: its palette tokens, type table, kit and layout rules (close/back position, popup vs full-screen, offer-popup family, rejected directions) beat colours and font sizes copied from an old prefab — an old prefab supplies **geometry only**. Missing or `Status: template` → run its § Bootstrap first (rule `art-style`).

**No reference supplied** → find one existing prefab of the same layout kind (popup vs full-screen, same content type — item list, purchase pack, settings rows…) under `<featuresRoot>/*/*/Resources/` (the template ships e.g. `screen_settings`, `screen_shop`, `screen_reward_popup`, `screen_change_name`), and inspect it live with `unity_prefab_info` + `unity_component_get_properties` to extract **real numbers**: sizes, `m_AnchoredPosition`, spacing between siblings, `m_FontSize`, colors. These real numbers are the raw material for the §0c spec-sheet — they replace vague "match existing conventions" with numbers the agent copies instead of guesses.

### 0b. Branch by name suffix

| Condition | Branch |
|-----------|--------|
| `[FeatureName]` ends with `Pack` (e.g. `WeeklyGemPack`) | **Package branch** |
| Otherwise | **Feature branch** |

### Layout mode

`screen_template` (`Popup_Template/screen_template.prefab`) has three root children, in this order —
`FeatureBaseController` depends on it (`GetChild(0)` = background, `GetChild(1)` = `MainUI` fallback):

| Sibling | Name | Role |
|---------|------|------|
| 0 | `background_button` | Dim background (`Image` + `Button`) — tap-outside-close when `ClickBackgroundToExit` |
| 1 | `popup_template` | Popup body — default **active** (nested prefab: `popup_container` → `BG`, `container_content`, `button_container`, `top_container_popup`) |
| 2 | `full_screen_template` | Full-screen body — default **inactive** (`SafeArea`; `bg_fullscreen`, `_bg_Pattern_FullScreen_Normal`, `content`, `top_view`, `botview`) |

Rules:

- **Package branch → popup** (or no screen at all, §1). Leave `full_screen_template` inactive.
- **Feature branch → choose one.** Popup: keep the defaults. Full-screen: `SetActive(true)` on `full_screen_template` **and** `SetActive(false)` on `popup_template`. Exactly one active, never both, never neither. Full-screen splits into a navigation page (player backs out) and a persistent HUD/shell (never dismissed) — close-button wiring differs, see §4 and create-ui `references/mcp-playbook.md` §6a–§6e.

#### `MainUI` — leave it EMPTY (both layouts)

`MainUI` (serialized on `FeatureBaseController`, TabGroup "Cấu hình chung") is only the node `UITransition` animates. Empty → `Awake()` falls back to `transform.GetChild(1)` = `popup_template`, and the default transition (no `TransitionAnimationObject` assigned — `screen_template` assigns none) plays:

| Layout | `MainUI` empty → what happens | Result |
|--------|-------------------------------|--------|
| Popup | `popup_template` scales 0.8 → 1 (`OutBack`, 0.3 s) while the root `CanvasGroup` and the dim background fade in (0.3 s); close = scale → 0.8 `InBack` + fades, 0.2 s; all `SetUpdate(true)` | pop-in — the intended popup feel |
| Full-screen | the scale tween runs on the **inactive** `popup_template` (invisible); only the fades show | fade-in — what every full-screen screen the template ships does (`screen_shop`, `screen_admin`, `screen_currency_bar` all keep `MainUI` empty) |

So a full-screen screen gets its fade **because** `MainUI` is empty. Pointing `MainUI` at `full_screen_template` makes the page zoom in like a dialog — off-convention. The close tween on the inactive node still completes (`OnComplete` → `UIManager.EnableTouch(true)` + close), so touch is not stuck — do not "repair" it.

Only assign `MainUI` when the task explicitly asks for a full-screen body that scales, and record it as a deviation at §0d.

Inspect the base template live first (`unity_select_instance` if multiple instances, then `unity_prefab_info` / `unity_execute_code`) — never guess hierarchy.

### 0c. Spec-sheet — mandatory artifact, written before any Unity mutation

Whatever the ground-truth source, distill it into a **spec-sheet** before the first Unity call: one row per element; every later property write copies its numbers from here. The image (if any) stays the *visual* truth checkpoints are graded against; the sheet is the *numeric* truth the build executes.

| Element | Parent (container per §3c) | Template (§3d) | Anchor preset | Size px | Position px | Font px | Color (ArtStyle token) |
|---------|----------------------------|----------------|---------------|---------|-------------|---------|-------|

- **From an approved v1 mockup** (preferred) — copy directly from sibling `.ui-spec.json`; generated HTML embeds identical JSON and validation rejects drift. The PNG is only the visual truth for checkpoint grading. The spec's `contentRoot` maps to `container_content` (popup) or `full_screen_template/content` (full-screen).
- **From a legacy approved mockup** — read the HTML's embedded `<script id="spec">`; validator warnings do not block grandfathered tasks.
- **From a reference image** (no spec block) — convert proportions to pixels at the design resolution (`python3 .claude/scripts/ui_spec_common.py design-resolution`; EZG standard **1080×2400 portrait**, matching `screen_template`'s CanvasScaler — Scale With Screen Size, Expand, reference 1080×2400): an element spanning ~80 % of the mockup width → `864` wide; centered ~¼ down from the top → top-anchored, `y ≈ -600`. A design drawn at 1x on a ~375 pt-wide phone frame scales by 1080 / 375 ≈ 2.88 (then snap to the 4 px grid). An approved spec carries its own `designResolution` (legacy specs: 1080×1920) — build and capture at that one. Do this estimation once, up front, for every element — far more reliable than eyeballing positions mid-build.
- **From an existing prefab** (no image, or `clone:<Prefab>`) — copy the real numbers extracted in §0a into the sheet.
- Elements arranged in a row/column/grid get **no per-element positions** — record the container's layout-group values instead (spacing, padding, cell size — §3e).
- A container carrying `"scroll": "vertical" | "horizontal" | "loop"` becomes a spec-sheet row with Template = `ScrollViewTemplate` (or `ScrollViewLoopTemplate` for `loop`); its children's Parent column is that instance's `Viewport/Content`, not the container itself (§3d). No `scroll` field but the stack is obviously taller than the container → treat it as scroll and record the deviation here rather than improvising a `ScrollRect` at build time.

**Do the arithmetic on every row/grid before leaving this section.** `N × cardWidth + (N-1) × spacing` must fit the container's real usable width — a mockup is a drawing and can depict a row that does not fit. Two shipped examples of skipping it: three 410 px cards in one row (`3×410 + 2×30 = 1290`) inside a ~860 px popup body — impossible, shipped as 2+1; four reward slots sized for the mockup while the CSV drives five (`5×180 + gaps = 996` in an 820 px row) — overflowed at runtime. Both were caught late by the reviewer when one multiplication at spec-sheet time would have caught them for free. `popup_container` is a fixed 960 px wide column (its `VerticalLayoutGroup` pads 180 top / 100 bottom and its `ContentSizeFitter` sizes the height to the content), so the popup body's usable width is ≤ 960.

**Polish floor on every row** (`.claude/docs/PolishRules.md` §9, rule `game-polish`; thresholds in `project-profile.json` → `polish`): Font px ≥ 32 (never < 30), every tappable element ≥ 128 px on both axes (a smaller visual gets an invisible raycast pad — the spec's `hitArea`), at most one primary CTA, an action pair laid out side by side (secondary left, primary right), text boxes sized for ~30 % longer translations, no feature-added tappable in the top 100 px / bottom 60 px of the canvas (safe zone, P42 — template chrome exempt); soft: primary CTA height 152–176, every spacing / padding / gap on the 4 px grid (scale 4/8/12/16/24/32/48/64), full-width panels on the 6-column grid (6 × 150, gutter 24, margin 30) with ≥ 16 px inner padding. Template-owned chrome (close / back / title, `top_container_popup`, `top_view` / `botview`, `CheatMenu`) keeps the template's size — never resize it for polish. The approved v1 spec already passed the measurable ones in `ui-spec-validator` — a reference-image / clone sheet has not, so check them here. Also check the **worst case, not the mockup case**: a mockup that draws 4 slots while the CSV drives up to 5 must be sized for 5 — count from the data, then size for the maximum. Doesn't fit → either drive the container with `childControlWidth` + `LayoutElement{min,preferred}` so cells shrink, or record the deviation now (§0d) instead of discovering it after Phase C.

### 0d. Checkpoint 0 — spec-sheet approval gate

| Mode | Gate |
|------|------|
| **Interactive** | Present the spec-sheet + the branch/layout decision, and wait for explicit user OK **before** §1/§2/Phase A. A wrong number costs one table edit here; after Phase C it costs a rebuild. |
| **Autonomous (`/run-backlog`)** | No live user — no ask, but the sheet is still mandatory and is passed (alongside the image path, if any) as `groundTruth` to every `ui-visual-reviewer` spawn. |

Carry both forward through §3: **image = visual truth, spec-sheet = numeric truth.**

---

## 1. Base template

### Feature branch

Path: `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/Popup_Template/screen_template.prefab` (guid `56fe2a4fb9f5141488c53da9e7819aa1`).

Use only as the structural baseline for the root prefab whose controller inherits `FeatureBaseController`. Other child/reusable UI prefabs are created new from the reusable templates (§3d), not copied from this template. `screen_template` ships **no controller** — the feature's `FeatureBaseController` subclass is added to the root (§4).

### Package branch

The template has **no separate pack screen template**. A pack is a `purchase_template` card
(`Purchase_Template/purchase_template.prefab`, guid `a433b9d31d495694d99f8ff2ec69c12e`, component
`PurchaseTemplate`) driven by a small controller that holds a `PurchaseTemplate` reference and calls
`InitButtons(...)` + `SetData` / `SetPurchaseType` / `SetRewards`… from a `PackTemplateModel` CSV row —
the shipped reference is `ShopItemRawPack` (`<featuresRoot>/Monetization/Shop/Store/Scripts/Controller/`),
listed in the shop by `ShopPackageLogic`. Two placements:

- **Shop cell** — a plain prefab (no `FeatureBaseController`) in the pack's `Resources/`, instantiated by the
  shop list. No screen variant.
- **Pack popup** — a `screen_template` Variant on the popup branch (§2) whose `container_content` holds the
  `purchase_template` instance; the controller inherits `FeatureBaseController` and wires the card.

`PurchaseTemplate` owns the buy buttons (`button_purchase_iap` / `_ads` / `_free` / `_currency`, sold-out
state) and the rewards view — never add a second buy button next to it and never pre-fill its icon /
price / reward fields on the prefab (they are set from CSV at runtime). A time-limited pack instantiates
`cooldown_timer_template` (`UI_CooldownTimeView`) or `OfferExpriesTimer_Template` inside the content
container — never hand-build `UI_CooldownTimeView` on a raw GameObject.

---

## 2. Create the prefab variant

1. Folder: `<featuresRoot>/<Domain>/[Name]/Resources/` (create if missing; `<Domain>` per rule `project-structure`).
2. Create a **Prefab Variant** of the base template inside it — never a plain duplicate, so future template edits propagate. See playbook §7 for the MCP sequence that actually yields a Variant (source must be an instance of the base, not a blank GameObject).
3. Name it `screen_<snake_case>.prefab`, where `<snake_case>` is the `GameEnums.Features` member run through `ToSnakeCase` (`MyNewScreen` → `screen_my_new_screen`): `UIManager.Show()` loads `screen_{feature.ToSnakeCase()}` from the root of any `Resources/` folder (not a sub-folder). Add the enum member with an unused number — never renumber existing ones (playbook §6).

---

## 3. Assemble the UI — 3 checkpoint phases

Use the five-move MCP loop + property cheatsheet in playbook §1–§3. Split the build into 3 phases; each ends in a **checkpoint** you do not skip past. Root screens then get a fourth, **Phase D — designer pass**, after the Phase C gate (§5 step 3b). This is what actually catches drift — writing "verify" in prose does not, once the tool-call chain runs 40–50 calls deep for a full screen.

| Phase | Scope |
|-------|--------|
| **A — skeleton** | §3a only (layout mode + empty content containers). No buttons/text/images yet. |
| **B — elements** | §3b–§3e — every button/text/image/list from the catalog, positioned from the §0c spec-sheet numbers (layout-group-first, §3e). |
| **C — wiring** | Step 4 (script binding + reference wiring) + Step 5 save. Checkpoint after Step 4 / final gate in §5. |
| **D — designer pass** | Root screens only, after the Phase C `pass`: art, FX and motion laid **over** the frozen Phase C layout — never moving it — proven by `ui-layout-lock.py`. Procedure, skips (`polish=off`, dev-only screens…) and the revert fallback: [`ui-designer-pass.md`](ui-designer-pass.md). Runs inside §5 (step 3b), before the evidence report. |

### Checkpoint mode

| Mode | After each phase |
|------|------------------|
| **Interactive** (user in session) | Screenshot (`unity_screenshot_game`), show it, ask "matches what you want — continue?" — do **not** self-decide "looks fine" and continue unprompted. |
| **Autonomous** (`/run-backlog`) | Spawn [`ui-visual-reviewer`](../agents/ui-visual-reviewer.md) (fresh context — it takes its own screenshot) with `phase`, `targetPath`, and Step 0 `groundTruth`. `block` → fix findings, re-stage, re-spawn — max **2 rounds** per phase. Independent check, not builder self-grading. Phase D adds `before`, `lockReport` and the aspect/state list ([`ui-designer-pass.md`](ui-designer-pass.md) D6) and, unlike A–C, ends in a revert to Phase C instead of a block when it cannot pass. |

### 3a. Layout mode

Apply the §0 decision (popup is the default state; full-screen needs the explicit enable/disable of both bodies).

### 3b. Localize — `LocalizeHelper` rules (every text node, not just the title)

The template's localize component is **`LocalizeHelper`** (`Features/_Shared/Localize/LocalizeHelper.cs`, fields `Key` + `Category`). At `Start`, on every `OnEnable` and on every language change it **overwrites** the `Text` with `GameSystems.Localize(Key, Category)`; an empty `Key` makes it use the label's current text as the key. So the component is mandatory on static labels and forbidden on logic-bound ones. Every text template (`text_title`, `text_head`, `text_body`, `text_button_CTA`, `text_template`…) ships with it already. Classify every text node:

| Loại label | Ví dụ | Quy tắc |
|---|---|---|
| **STATIC** — caption cố định hiển thị trên UI | Title, button caption (Xác nhận/Hủy/Mua), tier label (Free/Premium), benefit line, badge | PHẢI có `LocalizeHelper` với `Key` (lowercase, **không** `#`) + `Category` (tab CSV, enum `LocalizeCategory`). Node từ text template đã ship sẵn component → chỉ set `Key` / `Category`, **không gắn thêm cái thứ hai**. Node tự dựng không có → `AddComponent<LocalizeHelper>` rồi set. |
| **DYNAMIC** — text do logic bind lúc runtime | Số lượng/giá trị (gem cost, ticket count), tên/ngày ("Bù điểm danh Ngày 10"), giá IAP từ SDK, countdown | **KHÔNG** có `LocalizeHelper` (nó ghi đè text logic vừa set). Node lấy từ text template mang sẵn component → **remove** nó. |

- **Key: tái dụng trước, tạo mới sau.** Grep các CSV localize đã generate (`<localize.csvRoot>/<lang>/<tab>.csv`, `project-profile.json` → `localize`) — key generic có sẵn (`claim`, `exit`, `buy`…) dùng lại, không đẻ key trùng nghĩa. Key mới theo family `<featurename>_<element>` (lowercase), đăng ký qua skill [`/add-localize`](../skills/add-localize/SKILL.md) với VI+EN — key chưa đăng ký hiện `Category: key` lúc runtime.
- Title: popup → `text_title` trong `top_container_popup` (đã có `LocalizeHelper`) → `Key = <featurename>_title`; full-screen → `text_title` dưới `top_view/title_sale`.
- **UI spec là nguồn phân loại:** mỗi text element mang `"localize": "#key"` (static — bỏ `#` khi gán `Key`), `"dynamic"`, hoặc `"none"` cho visual glyph — builder gắn đúng theo đó, reviewer Phase C check lại.

### 3c. Content containment (critical)

Every new element — timer, item rows, buttons, text, anything from §3d — must nest inside the active body's **content container**.

| Body | Template-owned children (do not add siblings among them) | Content container for new UI |
|-------|--------------------------------------|------------------------------|
| `popup_template` | `popup_container` → `BG`, `button_tooltips`, `container_content`, `button_container`, `top_container_popup` | Everything inside `popup_container/container_content` (a `VerticalLayoutGroup` column; its empty child `container` is a second column you may fill). Title + close stay in `top_container_popup`. |
| `full_screen_template` | `bg_fullscreen`, `_bg_Pattern_FullScreen_Normal`, `content`, `top_view`, `botview` | Everything inside `full_screen_template/content` (no layout group — add one, §3e). `top_view` holds the title, `botview` the close/back button — chrome, not a place for feature content. **Never** add children directly to `full_screen_template` or `popup_container`. |

Siblings elsewhere break the template's centered layout (popup sizing, safe area, dim background).

**A Variant cannot reorder the base's children — so `container_content` always draws under `top_container_popup`.** `popup_container`'s child order belongs to the base template, and Unity does not let a Prefab Variant override the sibling order it inherits (`SetSiblingIndex` on the instance and via `LoadPrefabContents` both revert on the asset). Consequence for the spec-sheet: anything inside `container_content` that reaches up into the title bar's band is drawn underneath it. A mockup showing an element (badge, ribbon, hero art) overflowing onto the title bar is not buildable in a Variant. Keep the element fully inside the content and record the deviation at §0d. Before copying a "precedent" a spec cites, open it — specs routinely name a prefab that does not exist in this repo.

### 3d. Template catalog

> Bảng này là nửa human-facing của contract. Nửa machine-facing là `.claude/ui-kit/ui-kit.json`
> (số đo, sinh tự động) + `.claude/ui-kit/ui-kit-usage.json` (luật ghép template của base) +
> `.claude/project/ui-kit-usage.json` (luật riêng của project) — `mockup-drafter` đọc. Thêm/sửa
> luật ở đây thì thêm luôn bên kia, nếu không nó chỉ được tuân thủ một nửa. Vòng đời kit: skill
> [ui-kit](../skills/ui-kit/SKILL.md). Mô tả chi tiết từng prefab (hierarchy, controller, cách dùng):
> create-ui [`references/prefab-templates.md`](../skills/create-ui/references/prefab-templates.md).

Reuse from `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/` (ưu tiên reuse, đừng vẽ lại). Số px là kích thước gốc trong `ui-kit.json` — giữ nguyên cho chrome cùng loại (§3e; validator cảnh báo `inconsistent_chrome_size` nếu 2 instance khác cỡ).

| Category | Prefabs |
|----------|---------|
| Buttons | `button_template` (chỉ feel + container rỗng), `button_template_common_txt`, `button_template_common_txt_icon`, `button_template_CTA_txt` (CTA chính), `button_template_icon`, `button_close`, `button_back_left` / `button_back_right` (chỉ full-screen), `button_info`, `button_next`, `button_play`, `button_home_bottom` / `button_home_side` |
| Purchase | `purchase_template` (thẻ gói, `PurchaseTemplate`), `button_purchase_iap` (tiền thật, có `discount_container`), `button_purchase_ads` (xem quảng cáo, có `ads_tag`), `button_purchase_free`, `button_purchase_currency` (tiền trong game, giá bằng `money_bar_require_view`), `DiscountBadgeLabel`, `DiscountRibbonLabel`, `tag_best_seller`, `rewards_view` |
| Popups | `popup_notice_positive` / `popup_notice_negative` (notice dẹt + yes / no / confirm), `popup_template_notice`, `popup_template_divided_gradient` — chỉ dùng đúng loại của nó |
| Frames / panels | `FrameTemplate` (khung trơn), `panel_template` (khung + Title `text_head` — khối có tiêu đề), `DiviveBar`, `glow_top` / `glow_bottom` |
| Badges | `notification_count` (có số), `notification_dot` (chỉ chấm), `notification_text` (chữ ngắn: Mới / Free), `notification_arrowUp` — đặt **bên trong** nút / tab nó đánh dấu, góc trên-phải; logic qua `RedDotBadge` |
| Currency / resource | `Money_Bar` (thanh tiền tệ đầu màn — gồm `button_money_bar`), `money_bar_require_view` (icon + số cho **mọi** chỗ hiện giá / yêu cầu), `ResourceViewer` (chip một loại tiền + nút +), `CurrencyPreview` / `CurrenciesPreview` |
| Items | `ItemElement`, `ItemElementRewardPopup`, `ItemPreview` (dải item sinh lúc runtime) |
| Lists / scroll | `ScrollViewTemplate` (vùng cuộn — **bắt buộc**, xem dưới), `ScrollViewLoopTemplate` (list dài / số item do CSV-runtime quyết định, `LoopListView2`) |
| Tabs | `tab_template` + `toggle_tab_template`; `TabTogglerGroupBar` + `TabToggle_template_cell` — xem "Tab" dưới |
| Text | `text_title`, `text_head`, `text_body`, `text_button_CTA`, `textbox_template`, `TapToClose`, `text_TapToClose` / `text_TapToContinue` |
| Input / slider / timer | `InputField01_Basic_White_NormalText`, `SliderTemplate`, `LevelSlider`, `cooldown_timer_template`, `OfferExpriesTimer_Template` |

Luật dùng từng prefab (đồng bộ với `ui-kit-usage.json`):

- **Notice:** `popup_notice_positive` khi CTA dẫn tới hành động tích cực (đồng ý mua, đồng ý xem quảng cáo); `popup_notice_negative` khi CTA dẫn tới hành động tiêu cực (thoát, từ chối, mất tiến độ) — hậu quả nói rõ trong nội dung. Popup chỉ hiện thông tin (info của tiền tệ / item, cách kiếm) **không** nhét nút hành động.
- **Back / close:** `button_back_*` chỉ ở màn full-screen; popup dùng `button_close` có sẵn trong `top_container_popup`. Không thêm nút đóng thứ hai — nút nào đóng màn phải nằm trong `_closeButtons`.
- **Giá:** mọi chỗ hiện giá / yêu cầu tài nguyên dùng `money_bar_require_view`; nút mua đã chứa sẵn một cái — không lồng thêm, không đặt khối giá rời cạnh nút.
- **Nút mua theo loại:** tiền thật → `button_purchase_iap`; xem quảng cáo → `button_purchase_ads` (role `ads`, dấu quảng cáo riêng — PolishRules P34); tiền trong game → `button_purchase_currency`; miễn phí → `button_purchase_free` (hết lượt thì khoá + lý do).
- **Badge:** có số → `notification_count`; chỉ báo có → `notification_dot`; "Mới" / "Free" → `notification_text`. Bật / tắt bằng một lớp kế thừa `RedDotBadge` (skill `base-notification`), không tự `SetActive` rải rác.
- **Thanh tiền tệ:** `Money_Bar` là nested prefab dùng chung — **CẤM sửa prefab gốc** để thêm tiền tệ của một feature (sẽ đẩy nó lên mọi màn). Cần thêm chip thì thêm vào bản instance trong chính prefab của feature (override "Added GameObject"), template gốc giữ nguyên byte.
- **List sinh lúc runtime:** `ItemPreview` / `CurrenciesPreview` / `ScrollViewLoopTemplate` tự sinh con từ template item của chúng — không đặt tay từng ô.

**KHÔNG dùng cho UI feature thường** (liệt kê để agent nhận diện, tránh nhầm): `CheatMenu`, `ButtonCheatTemplate`, `ToggleCheatTemplate` — chrome cheat dev-only, chỉ khi task có `[CHEAT]` (§4 "Cheat buttons"); `TextTemplate_old` — template chữ cũ, dùng `text_*`; `popup_template`, `top_container_popup` — chrome do `screen_template` sở hữu, KHÔNG instantiate lẻ vào một màn khác.

#### Vùng cuộn — bắt buộc instantiate template, CẤM tự gắn `ScrollRect`

Nội dung cao hơn khung chứa ⇒ `unity_asset_instantiate_prefab` `Templates/ScrollViewTemplate.prefab` (guid `33918c2017ad04b49b7d5f76578e30ff`) rồi đặt nội dung vào `ScrollViewTemplate/Viewport/Content`. **Không bao giờ** `AddComponent<ScrollRect>` / `RectMask2D` lên `container_content` / `content` (hoặc bất kỳ node nào của base template) để tự dựng scroll.

- Template ship đủ 3 tầng chuẩn: root (`Image` trong suốt + `ScrollRect`, Elastic) → `Viewport` (`Image` + `Mask`, `showMaskGraphic` off) → `Content`. Tự gắn tay luôn thiếu tầng Viewport. `ScrollRect` của template bật cả hai trục — tắt trục không cuộn (`m_Horizontal` / `m_Vertical`) theo `scroll` của spec.
- Đặt instance làm con của content container (§3c), anchor `stretch` offsets `0,0,0,0` (hoặc size của viewport trong spec). Giữ root `Image` `raycastTarget = true` để bắt drag trên vùng trống.
- `Content` là nơi gắn `VerticalLayoutGroup` / `GridLayoutGroup` + `ContentSizeFitter` (`PreferredSize` trên trục cuộn) — **không** tạo thêm một node trung gian.
- List dài, số item do CSV/runtime quyết định, hoặc cần recycle → `ScrollView_Template/ScrollViewLoopTemplate.prefab` (guid `7fc82672ea683a4459b61e77f629d1e9`, `LoopListView2`, mặc định cuộn ngang) thay vì `ScrollViewTemplate`.
- Verify: mọi `ScrollRect` trong prefab có `PrefabUtility.GetCorrespondingObjectFromSource` trỏ về một trong hai template trên. Thiếu luật này, một màn đã ship `ScrollRect` gắn tay và lọt hết mọi gate.

#### Tab — toggle nằm trong một tab strip, CẤM dựng row tab tự chế

Tab đổi page **trong cùng một màn** = một tab strip: `Templates/tab_template.prefab` (guid `a1b7405904d500e4ab2d80e252d3f448` — `UI_TabExtensions` + `ToggleGroup` + `HorizontalLayoutGroup`, ship sẵn một `toggle_tab_template`) hoặc `Tab_Template/TabToggle/TabToggleGroup/TabTogglerGroupBar.prefab` (+ `TabToggle_template_cell`). Đặt strip trong content container; các page là container anh em trong cùng content.

- **Spec:** strip là **container** mang `"tabBar": true`, `type: "row"`, children chỉ là element toggle (`toggle_tab_template` / `TabToggle_template_cell`). Toggle nằm ngoài một container `tabBar` → validator chặn `tabs_outside_bottom_bar`; vẽ `tab_template` thành element khi màn có toggle → chặn `tabbar_as_element` (element không chứa được con, y hệt luật scroll).
- **Build:** instantiate strip vào content container → nhân bản / instantiate toggle làm con **trực tiếp** của strip (đúng thứ tự trái → phải; không thêm layout group thứ hai) → mỗi tab một page container trong content.
- **Wire:** `UI_TabExtensions` có sẵn trên strip: `_toggleList` = toggle theo thứ tự, `_objectList` = page **cùng index**, `_mainCanvasScale` = `CanvasScaler` ở root, `_indexOnOpen` = tab mở mặc định (`-1` = giữ nguyên), `_useAnimSwap` = trượt khi đổi tab. Controller chỉ gọi `RegisterOnchangeAction(i, action)` / `JumpToIndex(i)` / `SetTabIndex(i)` — **KHÔNG** tự `toggle.onValueChanged.AddListener(...)`. API đầy đủ: create-ui `references/prefab-templates.md` → `tab_template`.
- **Ngoại lệ — thanh launcher:** thanh mà mỗi nút mở một màn khác (`UIManager.Show`) không phải tab strip: `UI_TabExtensions` không áp dụng (`_objectList` phải là object cùng màn) — mỗi nút một `Button.onClick`. Ghi vào `assumptions[]`.
- Verify: mọi toggle là con trực tiếp của một strip; `_toggleList.Count == _objectList.Count ==` số tab; controller không có `onValueChanged.AddListener` tự viết.

#### Khối thông tin có tiêu đề — `panel_template`, CẤM label rời trên text trần

Màn xếp nhiều khối nội dung mà mỗi khối cần một tiêu đề (luật chơi, phần thưởng, nhóm chỉ số) thì **mỗi khối là một instance `Frame_Template/panel_template.prefab`** (guid `5413dfc08ef4edf48a0857e56ca34370` — `Image` + `VerticalLayoutGroup` + `LayoutElement`, con `Title` = `text_head`), thân khối là các con còn lại của nó. Xếp một `text_head` làm nhãn rồi thả text trần bên dưới là ship một bức tường chữ phẳng.

- **Spec:** khối là **container** mang `"section": {"title": "...", "localize": "#key"}`, `type` là `row` / `col` / `grid`. Đừng emit `panel_template` / `FrameTemplate` làm con của container `section` (validator chặn `section_frame_as_element`). `localize` là `"#key"` hoặc `"none"` — tiêu đề là nhãn STATIC, KHÔNG bao giờ `"dynamic"` (`section_localize`).
- **Build:** instantiate `panel_template` làm con của list container → set `Title` (`LocalizeHelper` `Key` / `Category`) → đưa thân khối vào làm các con còn lại; chỉnh `padding` / `spacing` của `VerticalLayoutGroup` theo spec (lưới 4 px). Khối không cần tiêu đề → `FrameTemplate` (chỉ `Image`) làm con đầu tiên, anchor stretch.
- Verify: mọi khối có tiêu đề carry `m_SourcePrefab` của `panel_template`; tiêu đề có `LocalizeHelper` + key đã đăng ký; không `Image` nào bị recolor tay để giả khung.

Match `.claude/docs/ArtStyle.md` (palette tokens, kit, type table) and the §0c spec-sheet numbers.

### 3e. Positioning strategy — layout-group-first (critical for visual quality)

Hand-computing `m_AnchoredPosition` per sibling is this workflow's main visual failure mode: N elements = 4N numbers that must all be simultaneously right, and screenshot review only catches gross errors — not uneven spacing or drifting alignment. Layout groups collapse that to 2–3 spec-sheet numbers and guarantee alignment.

- Any container arranging **≥2 siblings in a row / column / grid** gets a `HorizontalLayoutGroup` / `VerticalLayoutGroup` / `GridLayoutGroup`; spacing/padding/cell size come from the §0c spec-sheet (multiples of 4). Auto-sized content → add `ContentSizeFitter` (`PreferredSize` on the auto axis).
- Absolute `m_AnchoredPosition` is for **single free-floating elements only** (a close button, one banner) or when cloning an existing absolutely-positioned prefab's exact numbers — never for chains of hand-spaced siblings.
- `popup_container` (VLG + `ContentSizeFitter`) and `container_content` (VLG) already carry layout groups — tune them, don't stack a second one on the same node. `full_screen_template/content` has none → add one via `unity_component_add`; exact `m_*` names and value formats in playbook §3. Templates that own an internal layout (`purchase_template`, `ItemPreview`, tab strips) keep it — don't fight it.
- Under a layout group with child-control on, size children via `LayoutElement` (`m_PreferredWidth/Height`), not `m_SizeDelta` — the group overrides it.

---

## 4. Bind scripts — start of Phase C

See playbook §4 for exact `get_referenceable` → `batch_wire` / `set_property` calls.

### Feature branch

- Assign `[FeatureName]Controller : FeatureBaseController` to the root if it already exists; otherwise keep the prefab clean and leave a binding note for the implementation step.
- **Leave `MainUI` unassigned** (§0 "Layout mode → `MainUI`") — for both layouts.
- Set `FeatureType` to the matching `GameEnums.Features` member (`none` if the feature pushes no open/close event).
- `_closeButtons`: popup → `top_container_popup/button_close`; full-screen navigation page → `botview/button_close`; full-screen persistent HUD/shell → deactivate `botview/button_close`, keep the list empty.
- `ClickBackgroundToExit = true` on popups (default is **false** — PolishRules §9.4 lists the exceptions); `false` on full-screen.
- `_closeWithBackey = true` except for a full-screen persistent HUD/shell. Special close steps go in an override of `CloseMe()` (+ `base`), never only on the X button's `onClick` — the Android back key goes through `CloseMe` too.
- Review `_backgroundAlpha` and `_stopTimeWhenOpen` (sets `Time.timeScale = 0` — every tween on that screen must then use `SetUpdate(true)`).

### Package branch

Pack popup (§1): the controller inherits `FeatureBaseController` and holds `[SerializeField] PurchaseTemplate _purchase` wired to the `purchase_template` instance inside `container_content`; it calls `_purchase.InitButtons(onAds, onCurrency, onIap, onFree)` once and sets the view from its `PackTemplateModel` row (`SetPurchaseType`, `SetRewards`, `SetSale`, `SetProductId`, `SetCurrencyRequire`, `SetFirstTimeBonus` — mirror `ShopItemRawPack.UpdateView`). Rewards go through `RewardsService` / `PurchaseManager` before any animation. Also set `FeatureType`, `ClickBackgroundToExit = true` and `_closeButtons` as in the Feature branch. Time-limited: wire the `UI_CooldownTimeView` of the instantiated `cooldown_timer_template` / `OfferExpriesTimer_Template`. Shop cell (§1): same wiring on the cell's own controller, no `FeatureBaseController`.

Never pre-fill `purchase_template`'s icon/price/reward fields on the prefab — `PurchaseTemplate` overwrites them from CSV at runtime.

If the Controller isn't generated yet, leave the root unassigned with a binding note.

### Cheat buttons (cả 2 branch) — chỉ khi task yêu cầu

Task mang tag `[CHEAT]` (hoặc `**Custom delta:**` liệt kê cheat dạng `name · label · method`) thì Phase C wire luôn cheat menu. **KHÔNG tự bịa cheat khi task không yêu cầu** — đó là quyết định của planning.

Chi tiết đầy đủ (guid, size, code pattern, recipe `unity_execute_code` để wire persistent `onClick`): [`.claude/skills/feature-cheat/SKILL.md`](../skills/feature-cheat/SKILL.md). Rút gọn:

- `screen_template` **KHÔNG** ship sẵn cheat menu → instantiate `Templates/CheatMenu.prefab` (guid `ec2aa73aad0a4ea4ab74e7da72c63287`) làm con của prefab root, cạnh `full_screen_template`. Giữ nguyên component trên root (`CheatMenuController` + `GameCheatObjectController`), trỏ `_targetMenu` vào `Menu`.
- Mỗi cheat = 1 instance `Button_Template/ButtonCheatTemplate.prefab` (guid `7322ef5c5cec00a4fab0c80fca752b4b`; bản on/off dùng `ToggleCheatTemplate.prefab`, guid `df02728a597bbdc4b901a2de81e50c4b`) làm con **trực tiếp** của `CheatMenu/Menu` (layout group có sẵn — không thêm cái mới).
- Tên GameObject = PascalCase ngắn theo hành động; `content` Text = nhãn tiếng Anh ngắn; **không gắn `LocalizeHelper`** (ngoại lệ duy nhất của luật localize ở §3b — cheat là dev-only).
- Cùng một width cho mọi nút trong menu; giữ size kế thừa từ prefab, đừng tự đặt số.
- Xoá/đổi 2 nút mẫu `Add Point` / `Remove Point` có sẵn trong `CheatMenu` — không ship nguyên trạng.
- `onClick` → đúng method `public Cheat_*` trên Controller; verify YAML có `m_MethodName` + `m_TargetAssemblyTypeName: <Feature>Controller, <assembly>` (tên assembly lấy từ asmdef chứa controller đó, đừng đoán).

---

## 5. Save and verify — Phase C checkpoint (final gate)

1. Save the prefab. Screenshot-verify (`unity_screenshot_game`, or `unity_play_mode` for true open-animation state) — playbook §5. Don't rely on hierarchy inspection alone.
2. Run playbook §8 runnable checks (`unity_gameobject_info`, `unity_component_get_properties`, `unity_search_missing_references`); if any `.cs` changed, recompile and read errors before done.
3. **Phase C checkpoint** — same "Checkpoint mode" as Phases A/B: show user and wait (interactive), or spawn `ui-visual-reviewer` with `phase: "C"` and get `pass` (autonomous). Do not mark complete on your own say-so.
3b. **Designer pass (Phase D)** — root screens: follow [`ui-designer-pass.md`](ui-designer-pass.md) end to end (backup + layout lock → brief → art → overlay + motion → lock diff, captures, ArtStyle gate, `ui-visual-reviewer` `phase: "D"` → record). Skipped or reverted → say which and why in the report/DONE summary; the build continues with Phase C either way. Phase D starts with `python3 .claude/scripts/ui-layout-lock.py begin --prefab <prefab>`, which also reports pieces this project lacks (partial Feature Hub update); that script or `.claude/docs/ui-designer-pass.md` missing, or `begin` answering `skip` → skip Phase D and record `designer-pass: skipped (not installed: <file>)` — install the missing items (Docs/ui-designer-pass, Scripts/ui-layout-lock.py, Agents/ui-visual-reviewer) from the Hub.
4. Reopen the prefab — hierarchy, references, and bindings must survive; no missing-script or missing-reference warnings on instantiate.
5. **v1 only — evidence report:** save the final clean screenshot at the spec's `designResolution` (standard 1080×2400; legacy specs 1080×1920) — after Phase D when it ran — as sibling `<Screen>.unity.png`, then create and validate `<Screen>.ui-build-report.json` (`--visual` = the last reviewer verdict: phase D when it ran, else phase C). When Phase D ran, keep its `<Screen>.ui-layout-lock.json` (D6.1) beside the report:

```bash
python3 .claude/scripts/ui-visual-diff.py \
  TechSpec/Mockups/<F>/<S>.png TechSpec/Mockups/<F>/<S>.unity.png \
  --output TechSpec/Mockups/<F>/<S>.ui-visual-diff.json
python3 .claude/scripts/ui-build-report.py create \
  --spec TechSpec/Mockups/<F>/<S>.ui-spec.json \
  --prefab Assets/.../<Feature>.prefab \
  --screenshot TechSpec/Mockups/<F>/<S>.unity.png \
  --visual-diff TechSpec/Mockups/<F>/<S>.ui-visual-diff.json \
  --structural pass --visual pass --localization pass --missing-references 0 \
  --output TechSpec/Mockups/<F>/<S>.ui-build-report.json
python3 .claude/scripts/ui-build-report.py validate TechSpec/Mockups/<F>/<S>.ui-build-report.json
```

Legacy tasks without `specVersion: 1` do not require this report.

### Hard checklist

| Check | Pass criteria |
|-------|---------------|
| **Layout mode** | Exactly one of `popup_template` (sib 1) / `full_screen_template` (sib 2) active, never both/neither (pack popup → `popup_template`). Root child order intact (`background_button` 0). |
| **Containment** | No new node is a direct child of `popup_container` / `full_screen_template`; new UI sits in `container_content` (popup) or `content` (full-screen); timers are template instances inside that container. |
| **Open animation (`MainUI`)** | Controller's `MainUI` is **None** — `grep -n "MainUI:" <your>.prefab` → `MainUI: {fileID: 0}` (or no override at all). A full-screen screen with `MainUI` = `full_screen_template` is a **fail** unless the task asked for it and §0d records it: it pops instead of fading (§0 "Layout mode → `MainUI`"). |
| **Canvas render mode** | Root Canvas must **inherit** the base `screen_template` canvas — the base serializes `m_RenderMode: 1` (Screen Space – Camera) with a null `m_Camera`, so the variant must carry **no** `propertyPath: m_RenderMode` override (`grep -n "propertyPath: m_RenderMode" <your>.prefab` → no hits). **Never Screen Space – Overlay.** Check the *serialized* value, not `Canvas.renderMode`: with a null camera the runtime getter reads back `ScreenSpaceOverlay`, so "restoring" the mode from that getter silently creates the override (playbook §5 has the revert snippet). The §5 RenderTexture helper must restore the *original* mode — a hardcoded Overlay restore bakes Overlay into the prefab. A Feature-root prefab is always a **Variant** of `screen_template`, never a fresh Canvas. |
| **Panel framing** | Every visible panel/card/frame is a **template instance** (`FrameTemplate` / `panel_template` / `ItemElement` / `purchase_template`, i.e. carries `m_SourcePrefab`), never a raw `Image` recoloured by hand. Spec containers carry no visible `background`/`border`/`boxShadow` (validator `container_style`); framing comes from a frame-template element anchored stretch. |
| **Scrolling** | Mọi vùng cuộn là **template instance** (`ScrollViewTemplate` guid `33918c2017ad04b49b7d5f76578e30ff`, hoặc `ScrollViewLoopTemplate` guid `7fc82672ea683a4459b61e77f629d1e9`), nội dung nằm trong `Viewport/Content`. Không `ScrollRect`/`RectMask2D` nào được gắn tay ngoài một instance đó — kiểm bằng `unity_execute_code`: mọi `ScrollRect` phải có `PrefabUtility.GetCorrespondingObjectFromSource` trỏ về một scroll template (§3d). |
| **Titled sections** | Mỗi khối nội dung có tiêu đề là một instance `panel_template` bọc thân khối, `Title` có `LocalizeHelper` + key đã đăng ký. Không nhãn `text_*` rời đứng trên text trần (§3d). |
| **Tabs** | Feature có tab → mọi toggle (`toggle_tab_template` / `TabToggle_template_cell`) là con trực tiếp của một tab strip (`tab_template` / `TabTogglerGroupBar`), `UI_TabExtensions` trên strip có `_toggleList`/`_objectList` cùng độ dài và controller không tự `AddListener(onValueChanged)` (§3d). |
| **Cheat** | Task có `[CHEAT]` → prefab có một instance `CheatMenu` ở root, mọi cheat button là instance `ButtonCheatTemplate` **con trực tiếp** của `CheatMenu/Menu`, cùng width, nhãn KHÔNG có `LocalizeHelper`, `onClick` trỏ tới một `public Cheat_*` có thật trên Controller (`grep -n "m_MethodName" <your>.prefab`), 2 nút mẫu `Add Point`/`Remove Point` đã bị xoá/đổi. Task KHÔNG có `[CHEAT]` → prefab không được mọc thêm nút cheat nào. |
| **Localize** | Mọi STATIC label có `LocalizeHelper` với `Key` + `Category` đã đăng ký (tái dụng key generic khi có; title = `<featurename>_title`); mọi DYNAMIC label KHÔNG có `LocalizeHelper` (§3b). Không node nào gắn 2 component. **Ngoại lệ:** nhãn cheat trong `CheatMenu/Menu` là dev-only → KHÔNG localize. |
| **Close / back** | Popup: `top_container_popup/button_close` trong `_closeButtons` + `ClickBackgroundToExit = true` (trừ ngoại lệ PolishRules §9.4). Full-screen navigation page: `botview/button_close` trong `_closeButtons`. Persistent HUD/shell: nút đó tắt, `_closeWithBackey = false`. Bước đóng đặc biệt nằm trong `CloseMe` override. |
| **Spec-sheet gate** | Spec-sheet existed before the first Unity mutation (§0c); interactive: user approved it at Checkpoint 0 (§0d). |
| **Layout groups** | Every row/column/grid of ≥2 siblings is driven by a layout group with spec-sheet spacing/padding on the 4 px grid (§3e) — no hand-spaced sibling chains. |
| **ArtStyle** | Every row of the `ArtStyle.md` §11 checklist passes with evidence (palette tokens, kit, locked family §4b, type table, state language §4c, rejected directions §9, family match against §8 approved images) (§0a). An approved mockup / artist PSD that disagrees wins — record it at §0d. |
| **Pinned view** | Every checkpoint screenshot was taken at the Game view pinned to the spec's `designResolution` (standard 1080×2400 — playbook §0). |
| **Polish** | PolishRules checklist rows for UI pass with evidence read off the prefab, not the mockup (template-owned chrome and `CheatMenu` excluded): **P15** text never < 30 px, touch targets ≥ 128 px (or a raycast pad) · **P16** no overflow at the design resolution and the lock sizes, with each static label set to its **longest** translation read from `<localize.csvRoot>/<lang>/<tab>.csv` (the `refactor-ui` §6 method — `LocalizeHelper` does not run in edit mode); no CSV to read → record it as a manual step, never as a pass · **P17** at most one primary CTA, role colours, action pair side by side · **P23** empty state = image + one line · **P26** item states (bought / selected / locked + reason) · **P04** no colour-only information · **P42** no feature-added tappable in the top 100 px / bottom 60 px. One of these failing is a Phase C fail. CTA height and the 4 px grid are soft (P14). |
| **Designer pass** | Root screen: Phase D ran — `ui-layout-lock.py diff` and `scope` `ok: true` after the last fix round and `ui-visual-reviewer` phase D `pass` (interactive: the dev's OK) — or the report/DONE summary states `designer-pass: skipped (<reason from ui-designer-pass.md D0>)` / `reverted (<reason>)`. Nothing Phase C built moved, resized, re-texted or re-tinted. |
| **v1 evidence** | `.ui-build-report.json` validates against current spec/kit hashes and `.ui-visual-diff.json`; `.unity.png` is clean at the spec's `designResolution`; structural, visual, localization all `pass`; missing references = 0. |
| **Package branch** | Pack popup is still a **Variant** of `screen_template.prefab` (Variant Parent populated); the `purchase_template` instance is wired into the controller (not stale/null); the IAP product id resolves against the pack's CSV row in `<featuresRoot>/<Domain>/[PackageName]/CsvConfig/[PackageName].csv`. |
