---
description: Decision layer for /new-ui — ground truth + spec-sheet gate (Checkpoint 0), templates, layout, layout-group-first assembly phases, script binding, final verify. Loaded by .claude/commands/new-ui.md. MCP tool sequences live in ui-mcp-playbook.md.
---

# New UI Guide (decision layer)

This is the **what-to-build** layer for [`new-ui.md`](../workflows/new-ui.md). The deterministic **how** (Unity MCP tool sequence, `m_*` paths, wiring, screenshot loop) is in [`ui-mcp-playbook.md`](ui-mcp-playbook.md).

Drive Unity through MCP tools, not the Inspector. Do not improvise where the playbook covers the operation.

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

**No reference supplied** → find one existing prefab of the same layout kind (Popup vs Full-screen, same content type — item-preview list, purchase pack, etc.) under `<featuresRoot>/*/Resources/`, and inspect it live with `unity_prefab_info` + `unity_component_get_properties` to extract **real numbers**: sizes, `m_AnchoredPosition`, spacing between siblings, `m_FontSize`, colors. These real numbers are the raw material for the §0c spec-sheet — they replace vague "match existing conventions" with numbers the agent copies instead of guesses.

### 0b. Branch by name suffix

| Condition | Branch |
|-----------|--------|
| `[FeatureName]` ends with `Pack` (e.g. `WeeklyGemPack`) | **Package branch** |
| Otherwise | **Feature branch** |

### Layout mode

Both templates share 3 root children:

| Sibling | Name | Role |
|---------|------|------|
| 0 | `BackgroundButton` | Tap-outside-close |
| 1 | `Popup` | Default **active** |
| 2 | `FullScreen` | Default **inactive** |

Rules:

- **Package branch → always Popup.** `PurchaseTemplate` lives inside `Popup/content/`. Leave `FullScreen` disabled; never enable both.
- **Feature branch → choose one.** Pop-up: keep defaults (no toggle needed). Full-screen: enable `FullScreen` **and** disable `Popup`. Exactly one active, never both.

#### `MainUI` — leave it EMPTY (both layouts)

`MainUI` (serialized on `FeatureBaseController`, TabGroup "Cấu hình chung") is the node the open animation scales. **It is not meant to be assigned** — `Awake()` falls back to `transform.GetChild(1)` = `Popup` and plays `DOScale(0→1, .3f, OutBack)` on it:

| Layout | `MainUI` empty → what happens | Result |
|--------|-------------------------------|--------|
| Popup | scales `Popup` (active) | pop-in — the intended popup feel |
| Full-screen | scales `Popup` (**inactive**) → tween invisible; only the root `CanvasGroup.DOFade(0→1, .3f)` shows | fade-in — the convention every full-screen screen in the game follows |

So a full-screen screen gets its fade **because** `MainUI` is empty. Wiring `MainUI` = `FullScreen` "so the animation targets the visible node" is the bug, not the fix: the screen then zooms in like a popup, off-convention with the ~95 other screens. The fallback tween on the inactive `Popup` still fires `OnComplete` → `UIManager.EnableTouch(...)`, so touch is not stuck — do not "repair" it either.

Only assign `MainUI` when the task explicitly asks for a non-default scale target, and record it as a deviation at §0d.

Inspect the base template live first (`unity_select_instance` if multiple instances, then `unity_prefab_info` / `unity_execute_code`) — never guess hierarchy.

### 0c. Spec-sheet — mandatory artifact, written before any Unity mutation

Whatever the ground-truth source, distill it into a **spec-sheet** before the first Unity call: one row per element; every later property write copies its numbers from here. The image (if any) stays the *visual* truth checkpoints are graded against; the sheet is the *numeric* truth the build executes.

| Element | Parent (container per §3c) | Template (§3d) | Anchor preset | Size px | Position px | Font px | Color |
|---------|----------------------------|----------------|---------------|---------|-------------|---------|-------|

- **From an approved v1 mockup** (preferred) — copy directly from sibling `.ui-spec.json`; generated HTML embeds identical JSON and validation rejects drift. The PNG is only the visual truth for checkpoint grading.
- **From a legacy approved mockup** — read the HTML's embedded `<script id="spec">`; validator warnings do not block grandfathered tasks.
- **From a reference image** (no spec block) — convert proportions to pixels at the design resolution **1080×1920 portrait** (project CanvasScaler: 1080×1920, match height): an element spanning ~80 % of the mockup width → `864` wide; centered ~¼ down from the top → top-anchored, `y ≈ -480`. Do this estimation once, up front, for every element — far more reliable than eyeballing positions mid-build.
- **From an existing prefab** (no image, or `clone:<Prefab>`) — copy the real numbers extracted in §0a into the sheet.
- Elements arranged in a row/column/grid get **no per-element positions** — record the container's layout-group values instead (spacing, padding, cell size — §3e).
- A container carrying `"scroll": "vertical" | "horizontal" | "loop"` becomes a spec-sheet row with Template = `ScrollViewTemplate` (or `ScrollLoopTemplate` for `loop`); its children's Parent column is that instance's `Viewport/Content`, not the container itself (§3d). No `scroll` field but the stack is obviously taller than the container → treat it as scroll and record the deviation here rather than improvising a `ScrollRect` at build time.

**Do the arithmetic on every row/grid before leaving this section.** `N × cardWidth + (N-1) × spacing` must fit the container's real usable width — a mockup is a drawing and can depict a row that does not fit. Two independent tasks shipped this bug to a reviewer:

| Task | Mockup asked for | Arithmetic | Container | Outcome |
|------|------------------|-----------|-----------|---------|
| 048 (MiniShop) | 3 cards in one centred row | `3×410 + 2×30 = 1290` | ~860 usable popup width | Impossible — shipped as 2+1 |
| 060 (ChapterPack) | 4 fixed reward slots | `5×180 + gaps = 996` | 820 row | Overflowed at milestone 50 (CSV has 5 rewards) |

Both were caught late (ui-visual-reviewer, Phase B/C) when one multiplication at spec-sheet time would have caught them for free. Also check the **worst case, not the mockup case**: 060's mockup drew 4 slots but the CSV drives up to 5 — count from the data, then size for the maximum. Doesn't fit → either drive the container with `childControlWidth` + `LayoutElement{min,preferred}` so cells shrink, or record the deviation now (§0d) instead of discovering it after Phase C.

### 0d. Checkpoint 0 — spec-sheet approval gate

| Mode | Gate |
|------|------|
| **Interactive** | Present the spec-sheet + the branch/layout decision, and wait for explicit user OK **before** §1/§2/Phase A. A wrong number costs one table edit here; after Phase C it costs a rebuild. |
| **Autonomous (`/run-backlog`)** | No live user — no ask, but the sheet is still mandatory and is passed (alongside the image path, if any) as `groundTruth` to every `ui-visual-reviewer` spawn. |

Carry both forward through §3: **image = visual truth, spec-sheet = numeric truth.**

---

## 1. Base template

### Feature branch

Path: `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/Popup_Template/screen_template.prefab`

Use only as the structural baseline for the root prefab whose controller inherits `FeatureBaseController`. Other child/reusable UI prefabs are created new, not copied from this template.

### Package branch

Path: `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/PackageTemplate.prefab`

Ships a pre-wired `Popup/content/PurchaseTemplate` (`PurchaseTemplateController`) — never duplicate or replace it; the variant just reuses it.

Key nodes (verify live with `unity_execute_code`):

- `BackgroundButton`
- `Popup/content/PurchaseTemplate`
- `Popup/Top/Title` (`LocalizesUI`)
- `Popup/ButtonClose`
- `FullScreen` (must stay disabled — Package layout is always Popup)

No built-in cooldown view — if the controller has `_cooldownTime`, instantiate `TimeLayoutTemplate.prefab` as a child of `Popup/content` (not directly under `Popup`, per §3c containment). Never hand-build `UI_CooldownTimeView` on a raw GameObject — it lacks the required `Text` child and renders nothing.

---

## 2. Create the prefab variant

1. Folder: `<featuresRoot>/[Name]/Resources/` (create if missing).
2. Create a **Prefab Variant** of the base template inside it — never a plain duplicate, so future template edits propagate. See playbook §7 for the MCP sequence that actually yields a Variant (source must be an instance of the base, not a blank GameObject).
3. Rename to `[Name].prefab`, matching the PascalCase used by the controller/manager/folder.

---

## 3. Assemble the UI — 3 checkpoint phases

Use the five-move MCP loop + property cheatsheet in playbook §1–§3. Split the build into 3 phases; each ends in a **checkpoint** you do not skip past. This is what actually catches drift — writing "verify" in prose does not, once the tool-call chain runs 40–50 calls deep for a full screen.

| Phase | Scope |
|-------|--------|
| **A — skeleton** | §3a only (layout mode + empty content containers). No buttons/text/images yet. |
| **B — elements** | §3b–§3e — every button/text/image/list from the catalog, positioned from the §0c spec-sheet numbers (layout-group-first, §3e). |
| **C — wiring** | Step 4 (script binding + reference wiring) + Step 5 save. Checkpoint after Step 4 / final gate in §5. |

### Checkpoint mode

| Mode | After each phase |
|------|------------------|
| **Interactive** (user in session) | Screenshot (`unity_screenshot_game`), show it, ask "matches what you want — continue?" — do **not** self-decide "looks fine" and continue unprompted. |
| **Autonomous** (`/run-backlog`) | Spawn [`ui-visual-reviewer`](../agents/ui-visual-reviewer.md) (fresh context — it takes its own screenshot) with `phase`, `targetPath`, and Step 0 `groundTruth`. `block` → fix findings, re-stage, re-spawn — max **2 rounds** per phase. Independent check, not builder self-grading. |

### 3a. Layout mode

Apply the §0 decision (Popup is the default state; Full-screen needs explicit enable/disable of both layers).

### 3b. Localize — LocalizesUI rules (every text node, not just the title)

`LocalizesUI.Awake()` **always overwrites** the Text with `GetContent(LangKey)` — so the component is mandatory on static labels and forbidden on logic-bound ones. Classify every text node:

| Loại label | Ví dụ | Quy tắc |
|---|---|---|
| **STATIC** — caption cố định hiển thị trên UI | Title, button caption (Xác nhận/Hủy/Mua), tier label (Free/Premium), benefit line, badge | PHẢI có `LocalizesUI` + `LangKey` (`#lowercase_key`). Node đã ship sẵn component (Title, `ButtonClose/content`, `TapToCloseText` của `screen_template`) → chỉ set `LangKey`, **không gắn thêm cái thứ hai**. Node từ `TextTemplate` / `ButtonNormal`/`ButtonActive` `content` **không ship sẵn** → `AddComponent<LocalizesUI>` rồi set key. |
| **DYNAMIC** — text do logic bind lúc runtime | Số lượng/giá trị (gem cost, ticket count), tên/ngày ("Bù điểm danh Ngày 10"), giá IAP từ SDK, countdown | **KHÔNG** có `LocalizesUI` (Awake sẽ clobber text logic vừa set). Node nguồn lỡ có sẵn component → remove nó. |

- **Key: tái dụng trước, tạo mới sau.** Grep các file localize đã generate (`Assets/_Project/Localize/LocalizationData/{lang}/`) — các key generic có sẵn (`#free`, `#cancel`, `#buy`, `#unlock`…) dùng lại, không đẻ key trùng nghĩa. Key mới theo family `#[featurename]_[element]` (lowercase), đăng ký qua [`add-localize.md`](../workflows/add-localize.md) với VI+EN — key chưa đăng ký hiện raw key lúc runtime.
- Title mặc định: `Popup/Top/Title` (có sẵn `LocalizesUI`) → `LangKey = #[featurename]_title`. Full-screen không có Title built-in; thêm dưới `FullScreen/Mid` nếu cần.
- **UI spec là nguồn phân loại:** mỗi text element mang `"localize": "#key"` (static), `"dynamic"`, hoặc `"none"` cho visual glyph — builder gắn đúng theo đó, reviewer Phase C check lại.

### 3c. Content containment (critical)

Every new element — cooldown view, ItemPreviews, buttons, text, anything from §3d — must nest inside the layer's **content container**.

| Layer | Allowed direct children of root layer | Content container for new UI |
|-------|--------------------------------------|------------------------------|
| `Popup` | Only base template nodes: `Top`, `content`, `ButtonClose` | Everything inside `Popup/content` (has `Image + UIGradient`). Package: add as siblings alongside existing `PurchaseTemplate` — don't remove it. |
| `FullScreen` | Only `Top` / `Mid` / `Bot` | Everything inside `FullScreen/Mid`, as siblings below existing `MenuBar` (sib=0). `Top` (Gold/Gem) keeps defaults. `Bot` giữ `ButtonBack` mặc định, **trừ tab điều hướng**: feature có tab thì bật `Bot/TabBottomTemplate` và đặt toggle vào trong nó — CẤM dựng row tab trong `Mid` (§3d "Tab điều hướng"). **Never** add children directly to `FullScreen`. |

Siblings elsewhere break the popup's centered layout (no scaling, no safe-area, no dark background).

**A Variant cannot reorder the base's children — so `content` always draws under `Top`.** `Popup`'s child order (`background`, `content`, `Top`, `ButtonClose`) belongs to the base template, and Unity does not let a Prefab Variant override the sibling order it inherits. Task 060 verified this the expensive way: `SetSiblingIndex` on the instance **and** via `LoadPrefabContents` both revert on the asset — it burned a full `ui-visual-reviewer` round whose round-2 verdict was still "badge occluded 33%".

Consequence for the spec-sheet: `Top` renders after `content`, so **anything inside `content` that reaches up into `Top`'s band is drawn underneath it**, no matter what you set. A mockup showing an element (badge, ribbon, hero art) overflowing onto the title bar is not buildable in a Variant. Keep the element fully inside `content` and record the deviation at §0d. Before copying a "precedent" the spec cites, open it — specs routinely name a prefab that does not exist in this repo, and the real precedent usually sits inside `content` and does **not** overflow either.

### 3d. Template catalog

> Bảng này là nửa human-facing của contract. Nửa machine-facing là `.claude/ui-kit/ui-kit.json`
> (số đo, sinh tự động) + `.claude/ui-kit/ui-kit-usage.json` (luật ghép template, viết tay —
> `mockup-drafter` đọc). Thêm/sửa luật ở đây thì thêm luôn bên kia, nếu không nó chỉ được tuân
> thủ một nửa. Vòng đời kit: skill [ui-kit](../skills/ui-kit/SKILL.md).

Reuse from `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/` (ưu tiên reuse, đừng vẽ lại). Số px là kích thước gốc — giữ nguyên cho chrome cùng loại (§3e; mockup-drafter cảnh báo `inconsistent_chrome_size` nếu 2 instance khác cỡ).

| Category | Prefabs |
|----------|---------|
| Buttons | `ButtonNormal`, `ButtonActive`, `ButtonViewAds`, `ButtonCurrency` (IAP), `ButtonClaim`, `ButtonCheat`, `ButtonIcon`, `ButtonInfo` / `ButtonInfoCircle` (nút "i" 100×100 mở popup thông tin), `ButtonTitleTemplate` (pill tiêu đề/header có text, auto-width × 60) |
| Frames | `FrameTemplate`, `FrameTemplateInside` (khối nội dung có tiêu đề = `FrameTemplateInside` + pill `ButtonTitleTemplate`: xem mục "Khối thông tin có tiêu đề" ngay dưới — **bắt buộc**) |
| Badges / Banners | `GameNotification` (badge số thông báo — đặt **bên trong** button cần hiển thị, anchor **top-right**, `PosX = PosY = -20`), `LimitBannerTemplate` (ribbon "giới hạn"/limited-time cho shop/pack, auto-width × 53) |
| Input | `InputFieldTemplate` |
| Toggles | `ToggleTemplate` (bật/tắt dạng icon 100×100), `ToggleTextTemplate` (toggle kèm nhãn text 331×100), `RadioTemplate` (chọn 1-trong-N, 172×60) |
| Currency / Resource | `CurrencyPreview` (1 currency: icon + value), `ResourceViewTemplate` (thanh tài nguyên đầu màn 750×140 — ships sẵn Energy/Gold/Gem; **dùng cái này cho top bar chuẩn, không tự ghép chip lẻ**; thêm `MoneyTypes` mới xem note dưới bảng), `ResourceHomeTemplate` (1 chip tài nguyên đơn 250×60 — chip con của `ResourceViewTemplate`, cũng dùng lẻ cho resource ngoài chuẩn) |
| Items | `ItemElement`, `ItemPreview` |
| Package | `PurchaseTemplate`, `ProfitLabelTemplate`, `LayoutTemplatePackage` (khung panel nội dung pack 960×1306) |
| Lists / Scroll | `ScrollViewTemplate` (vùng cuộn nội dung tĩnh — **bắt buộc**, xem dưới), `ScrollLoopTemplate` (list dài/vô hạn qua `LoopListView2`) |
| Other | `SliderTemplate`, `SliderDrag` (slider kéo có tay cầm 330×52), `TextTemplate`, `TimeLayoutTemplate` (cooldown), `TimeLayoutTemplate_small` (cooldown bản nhỏ, cao 46) |
| Tabs | `TabBottomTemplate` (thanh tab đáy 1080×148.73 — ships `ToggleGroup` + `HorizontalLayoutGroup` + `UI_TabExtensions`) + `TabToggleIconTemplate` (tab icon 200×200) / `TabToggleTextTemplate` (tab chữ 350×200). Luật đặt tab: xem mục "Tab điều hướng" ngay dưới — **bắt buộc**. |

**Thêm `MoneyTypes` mới lên top bar — CẤM sửa `ResourceViewTemplate.prefab` gốc**

`ResourceViewTemplate` là **nested prefab instance** nằm trong `screen_template.prefab` và `PackageTemplate.prefab`, nên **mỗi feature prefab sở hữu top bar riêng của nó**. Thêm chip thẳng vào file template gốc `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/ResourceViewTemplate.prefab` là đẩy currency của một feature vào **mọi màn hình trong game** (dù để default-inactive thì mỗi màn vẫn phải nhớ bật/tắt) → **KHÔNG làm**. File template gốc chỉ đổi khi có quyết định riêng ở tầm toàn game, không phải khi ship một feature.

Đúng: instantiate `ResourceHomeTemplate` làm **child của bản instance `ResourceViewTemplate` bên trong chính feature prefab đó** (`<featuresRoot>/<Feature>/Resources/<Feature>.prefab` → `FullScreen/Top/ResourceViewTemplate`), lưu lại thành prefab override "Added GameObject" của feature. Template gốc **giữ nguyên byte**.
1. Instantiate `Assets/_Project/Visual/ArtAsset/Shared/Resources/Prefabs/Templates/ResourceHomeTemplate.prefab` vào node `ResourceViewTemplate` của feature prefab (cùng cỡ với Energy/Gold/Gem — cao 64), đặt đúng thứ tự trong `HorizontalLayoutGroup`.
2. Set `HomeResourceItemController._type` = `MoneyTypes` mới + gán `_value` / `_currencyImage` / `_buyButton`. Chip tự bám `EventId.ResourcesChanged` **và** `PlayerResource.GetCurrencyChangedEvent(_type)` trong `Start()` → tự live-update, KHÔNG cần viết code cập nhật.
3. `OnClickBuy` (`HomeResourceItemController.cs`) chưa có nhánh cho currency mới ⇒ **tắt child `BuyButton` ("+")** của chip, tránh affordance chết.
4. Currency cần hiện ở nhiều feature ⇒ lặp bước 1–3 ở từng feature prefab. Đó là chủ ý: phạm vi hiển thị là **structural** (chip chỉ tồn tại ở màn cần), không phải runtime.
5. **KHÔNG** map vào `HomeResourceViewController._listResource` và **KHÔNG** gọi `HomeManager.ShowCurrencies(...)`: `HomeResourceViewController` không gắn vào prefab/scene nào trong project (dead code — guid `65b067b956c987f41939008ec17ca132` chỉ khớp `.cs.meta` của chính nó), `HomeManager._resourceView` luôn null → `NullReferenceException`. Verified ở task 141.

Currency **không phải** `MoneyTypes` (state riêng của feature, vd `TorchBalance` trong `PlayerDungeonCoreData`) thì `HomeResourceItemController` không đọc được — dùng chip `ResourceHomeTemplate` / `CurrencyPreview` lẻ như bảng trên, đặt trong content của feature.

**KHÔNG dùng cho UI feature thường** (liệt kê để agent nhận diện, tránh nhầm): `HpBar` / `MpBar` — thanh máu/mana world-space gắn trên nhân vật/enemy (gameplay HUD, không phải UI màn hình); `UnMaskTemplate` — lớp phủ full-screen khoét lỗ highlight, chỉ cho tutorial spotlight; `CheatMenu` — chrome cheat/dev, **đã có sẵn** trong `screen_template` nên KHÔNG instantiate lẻ và KHÔNG override transform của nó; thêm cheat = thả `ButtonNormal` vào `CheatMenu/Menu` (xem §4 "Cheat buttons"); `EventUITemplate`, `MenuBar`, `CheatItem`, `ItemLoopGridViewElement`, `ButtonClose` — chrome nội bộ do template cha sở hữu, KHÔNG instantiate lẻ (`ButtonClose` đã có sẵn trong `screen_template`; `MenuBar` nằm trong `FullScreen/Mid`+`Bot`; `ItemLoopGridViewElement` là item mẫu của `ScrollLoopTemplate`).

#### Vùng cuộn — bắt buộc instantiate template, CẤM tự gắn `ScrollRect`

Nội dung cao hơn khung chứa ⇒ `unity_asset_instantiate_prefab` `ScrollViewTemplate.prefab` (guid `8eb29c9ddda20e949a8fbcc106b669b1`) rồi đặt nội dung vào `ScrollView/Viewport/Content`. **Không bao giờ** `AddComponent<ScrollRect>` / `RectMask2D` lên `Popup/content` (hoặc bất kỳ node nào của base template) để tự dựng scroll — giống luật `TimeLayoutTemplate` ở §1/§4.

- Template ships đủ 3 tầng chuẩn `ScrollViewTemplate` (`Image` + `ScrollRect`, vertical, Elastic) → `Viewport` (`Image` + `Mask`) → `Content` (top-stretch, pivot `(0,1)`). Tự gắn tay luôn thiếu tầng Viewport, làm `content` kiêm cả viewport lẫn nền panel.
- Đặt instance làm con của content container (§3c), anchor `stretch` offsets `0,0,0,0`. Chỉnh 2 override bắt buộc: root `Image` `alpha = 0` (giữ `raycastTarget = true` để bắt drag trên vùng trống), `Viewport.Mask.showMaskGraphic = false`.
- `Content` là nơi gắn `VerticalLayoutGroup`/`GridLayoutGroup` + `ContentSizeFitter` (`PreferredSize` trên trục cuộn) — **không** tạo thêm một node `Column` trung gian.
- List dài, số item do CSV/runtime quyết định, hoặc cần recycle → dùng `ScrollLoopTemplate` (recycling qua `com.ezg.enhanced-scroller`) thay vì `ScrollViewTemplate`.
- Verify: `m_SourcePrefab.guid: 8eb29c9ddda20e949a8fbcc106b669b1` tồn tại trong prefab, và **không** `ScrollRect`/`RectMask2D` nào nằm ngoài một instance scroll-template. Task 064 (StageOverview) ship đúng lỗi này và lọt hết mọi gate vì luật này chưa tồn tại.

#### Tab điều hướng — bắt buộc nằm trong `Bot/TabBottomTemplate`, CẤM dựng row tab trong `Mid`

`screen_template` ship sẵn `FullScreen/Bot/TabBottomTemplate` (guid `61748bf3b3de6be4eaaf0dcb6907dcd2` — `Image` + `ToggleGroup` + `HorizontalLayoutGroup` + `UI_TabExtensions`, kèm 2 tab mẫu `Tab1`/`Tab2` dạng `TabToggleTextTemplate`) nhưng để **`m_IsActive = 0`**. Feature có tab phải **bật node này lên** và đặt toggle vào trong nó — không tự dựng một row tab khác ở chỗ khác.

**Màn KHÔNG có tab thì để nguyên `TabBottomTemplate` inactive** — `Bot` chỉ còn `ButtonBack`. Bật thanh rỗng lên là ship một dải nền nâu chắn đáy màn (`BattleResultDungeon` dính đúng lỗi này). Mockup của màn không tab cũng KHÔNG được vẽ thanh đó (validator warn `tabbar_empty_chrome`) — vẽ thanh trong PNG chính là thứ khiến người build bật nó lên.

- **Spec:** vùng tab là **container** mang `"tabBar": true` (cùng cơ chế với `scroll`), `type: "row"`, nằm trong chrome `Bot` (ngoài `contentRoot`), children là các element `TabToggleIconTemplate` / `TabToggleTextTemplate`. Toggle nằm ngoài một container `tabBar` → validator chặn `tabs_outside_bottom_bar`; emit `TabBottomTemplate` trong `elements[]` khi màn có tab thật → chặn `tabbar_as_element` (element không chứa được con, y hệt luật scroll).
- **Build:** `SetActive(true)` cho `FullScreen/Bot/TabBottomTemplate` → đổi tên/nhân bản `Tab1`/`Tab2`, hoặc instantiate `TabToggleIconTemplate` khi tab là icon, làm con **trực tiếp** của nó (đúng thứ tự trái→phải) → mỗi tab một page container tương ứng đặt trong `Mid`. Tab thừa của template thì xoá, đừng để `Tab1`/`Tab2` sót lại.
- **Wire:** dùng `UI_TabExtensions` có sẵn trên chính node đó (`Glob **/UI_TabExtensions.cs`): `_toggleList` = danh sách toggle theo thứ tự, `_objectList` = danh sách page trong `Mid` **cùng index**, `_mainCanvasScale` = `CanvasScaler` ở root, `_indexOnOpen` = tab mở mặc định (`-1` = giữ nguyên trạng thái). Controller chỉ gọi `RegisterOnchangeAction(i, action)` / `JumpToIndex(i)` — **KHÔNG** tự `toggle.onValueChanged.AddListener(...)`, **KHÔNG** tự `SetActive` node `Focus` (`ToggleGroup` + `UI_TabExtensions` lo phần đó, kèm anim swap trái/phải). Mẫu chuẩn: `EquipmentController.Start()` (`<featuresRoot>/Equipment/Equipment/Scripts/Controller/EquipmentController.cs`), tab bar thật: `Equipment.prefab`, `Shop.prefab`.
- **Tab phụ** (filter row nằm trong nội dung, vd `EquipmentFilter` của Equipment) vẫn dùng `TabBottomTemplate` nhưng đặt trong `Mid` — hợp lệ, ghi lại ở `assumptions[]`; validator chỉ warn `tabbar_in_content` khi màn chỉ có đúng một tab bar và nó nằm trong nội dung.
- **2 ngoại lệ của bước wire** (vị trí tab thì KHÔNG có ngoại lệ — luôn nằm trong `TabBottomTemplate`):
  - *Nav bar cross-screen* — toggle mở sang một feature khác (`UIManager.Show`) chứ không đổi page trong cùng màn: `UI_TabExtensions` không áp dụng (`_objectList` phải là object cùng màn), controller tự bind toggle. Mỗi màn trong nhóm ship cùng một bộ toggle, toggle của chính màn đó `isOn = true`. Ghi vào `assumptions[]`.
  - *Pager data-driven* — mọi tab dùng chung MỘT page, chỉ đổi data (vd DungeonGuide: 4 hầm, 1 khung nội dung): không có map 1-1 để đổ vào `_objectList`, controller tự bind. Vẫn giữ `ToggleGroup` của bar và để node `Focus` của `TabToggle*` tự chạy — không tự `SetActive` nó.
  - Cả hai ngoại lệ vẫn phải bật `Bot/TabBottomTemplate` và đặt toggle vào trong nó; `_toggleList`/`_objectList` để trống thì gỡ luôn giá trị thừa, đừng để list lệch độ dài (`UI_TabExtensions.OnChange` index thẳng vào `_objectList[i]`).
- Verify: `FullScreen/Bot/TabBottomTemplate` active; mọi `TabToggle*` là con trực tiếp của một instance `TabBottomTemplate`; `UI_TabExtensions._toggleList.Count == _objectList.Count ==` số tab; controller không có `onValueChanged.AddListener` tự viết. DungeonGuide ship đúng lỗi này (`tabRow` 1000×190 trong `Mid`, thanh đáy để inactive, controller tự wire `Toggle[]` + tự bật `Focus`) và lọt hết mọi gate vì luật này chưa tồn tại.

#### Khối thông tin có tiêu đề — `FrameTemplateInside` + pill `ButtonTitleTemplate`, CẤM label rời trên text trần

Màn xếp nhiều khối nội dung mà mỗi khối cần một tiêu đề (lore/rule/reward section, nhóm chỉ số, trang storyboard) thì **mỗi khối là một instance `FrameTemplateInside` bọc lấy thân khối**, tiêu đề là một instance `ButtonTitleTemplate` cưỡi lên mép trên của frame. Xếp `TextTemplate` làm nhãn rồi thả text trần bên dưới là ship một bức tường chữ phẳng — `DungeonGuide` ship đúng lỗi này (`loreLabel`/`arcLabel`/`bossLabel` + 9 text trần, không frame nào).

- **Spec:** khối là **container** mang `"section": {"title": "...", "localize": "#key"}` (cùng cơ chế với `scroll`/`tabBar`), `type` phải là `row`/`col`/`grid`. Frame lẫn pill đều **bọc** thân khối nên element không dựng được: emit `FrameTemplateInside`/`ButtonTitleTemplate` làm con của container `section` → validator chặn `section_frame_as_element` / `section_title_as_element`. `localize` là `"#key"` hoặc `"none"` — pill là nhãn STATIC do `LocalizesUI` sở hữu, KHÔNG bao giờ `"dynamic"` (`section_localize`). Pill lẻ ngoài mọi `section` chỉ warn `section_title_without_frame` (header đứng một mình là hợp lệ — ghi `assumptions[]`).
- **Build:** instantiate `FrameTemplateInside` (guid `a0063bf64c7c3484dab0575aeb846528`) làm con của list container → thêm `VerticalLayoutGroup` (`padding 20,20,50,20`, `spacing 12`, `childAlignment UpperCenter`, `childControlWidth ✓`, `childForceExpandWidth ✓`, `childForceExpandHeight ✗`) + `ContentSizeFitter` (`Unconstrained` / `PreferredSize`) → instantiate `ButtonTitleTemplate` làm con **đầu tiên** → gắn `LocalizesUI` + `LangKey` lên `TitleText` của nó (template ship KHÔNG có sẵn component này) → đưa thân khối vào làm các con còn lại.
- **`childControlHeight` chọn theo thân khối:** con tự mang `ContentSizeFitter` (text dài co giãn) → để `✗`, VLG đọc rect height của con (`DungeonGuide`). Con không có CSF (`ItemPreview`, `Grid`) → bật `✓` để VLG cấp height từ preferred height của con (`StageOverview`).
- **Hình học của pill là bắt buộc, không ước lượng:** `LayoutElement.ignoreLayout = true` (đứng ngoài dòng VLG), anchor + pivot `top-center` `(0.5, 1)`, `anchoredPosition (0, 30)`, `sizeDelta (0, 60)`. Pill ship sẵn `HorizontalLayoutGroup(pad 15,15)` + `ContentSizeFitter(PreferredSize / Unconstrained)` nên tự co ngang theo text — đừng set width tay.
- **Khoảng cách của list cha:** pill nhô 30px lên trên frame, nên container xếp các khối phải có `spacing ≥ 40` và `padding.bottom ≥ 30`. Spacing chật là title đè lên khối phía trên (validator warn `section_parent_gap` / `section_padding_top`).
- Verify: mọi khối có tiêu đề đều carry `m_SourcePrefab` `FrameTemplateInside`; pill là instance `ButtonTitleTemplate` với `ignoreLayout = true`; `TitleText` có `LocalizesUI` + key đã đăng ký; không `Image` nào bị recolor tay để giả frame. Mẫu chuẩn: `StageOverview.prefab` (`Popup/content/ScrollView/Viewport/Content`, 4 khối), `DungeonGuide.prefab` (`FullScreen/Mid/pageArea/pageScroll/Viewport/Content`, 3 khối).

Match existing UI conventions (or the §0c spec-sheet numbers).

### 3e. Positioning strategy — layout-group-first (critical for visual quality)

Hand-computing `m_AnchoredPosition` per sibling is this workflow's main visual failure mode: N elements = 4N numbers that must all be simultaneously right, and screenshot review only catches gross errors — not uneven spacing or drifting alignment. Layout groups collapse that to 2–3 spec-sheet numbers and guarantee alignment.

- Any container arranging **≥2 siblings in a row / column / grid** gets a `HorizontalLayoutGroup` / `VerticalLayoutGroup` / `GridLayoutGroup`; spacing/padding/cell size come from the §0c spec-sheet. Auto-sized content → add `ContentSizeFitter` (`PreferredSize` on the auto axis).
- Absolute `m_AnchoredPosition` is for **single free-floating elements only** (a close button, one banner) or when cloning an existing absolutely-positioned prefab's exact numbers — never for chains of hand-spaced siblings.
- The base templates ship with **no layout groups** on their containers (`screen_template`, `PackageTemplate` — verified) → add them via `unity_component_add`; exact `m_*` names and value formats in playbook §3. Templates that own an internal layout (`PurchaseTemplate`) keep it — don't fight it.
- Under a layout group with child-control on, size children via `LayoutElement` (`m_PreferredWidth/Height`), not `m_SizeDelta` — the group overrides it.

---

## 4. Bind scripts — start of Phase C

See playbook §4 for exact `get_referenceable` → `batch_wire` / `set_property` calls.

### Feature branch

- Assign `[FeatureName]Controller.cs` to the root if it already exists.
- Otherwise keep the prefab clean and leave a binding note for the implementation step.
- **Leave `MainUI` unassigned** (§0 "Layout mode → `MainUI`") — it is not part of the wiring list for either layout. Full-screen especially: never point it at `FullScreen`.
- Set `FeatureType` (inherited from `GameFeatureBaseController`) to the matching `EnumBase.Features` member, or `None` if the feature pushes no open/close event.

### Package branch

Assign `[PackageName]Controller.cs` (from `/new-package`, inherits `GameFeatureBaseController`) to the variant **root** (template root has no controller by default). Wire `[Required]` fields:

| Field | Binding |
|-------|---------|
| `_purchase` | Existing `Popup/content/PurchaseTemplate` controller instance. Never add a second one. |
| `_packIndex` | Leave at `0` on the prefab; per-instance value must match CSV row index (note in prefab comment if multiple packs share one Controller asset). |
| `_cooldownTime` (time-limited only) | Instantiate `TimeLayoutTemplate.prefab` under `Popup/content` via `unity_asset_instantiate_prefab` — never hand-build — then wire its `TimeText` child (where `UI_CooldownTimeView` lives). Verify real instance: YAML shows `m_SourcePrefab.guid: 873bdc6cafdccad4d9c86b8eaedada4d` and path is `[PackageName]/Popup/content/CooldownTime`. Missing `m_SourcePrefab`, or parent of `Popup` instead of `Popup/content` → fix before proceeding. |
| No duration | Remove `_cooldownTime` from controller, or leave empty and null-guard `InitCustomCooldown` in `LoadData()`. |

Also set on the controller (inherited, **not** `[Required]` — so easy to miss): `FeatureType` = the matching `EnumBase.Features` member (TabGroup "Common", from `GameFeatureBaseController`) and `ClickBackgroundToExit = true` (from `FeatureBaseController`) so tapping the dark background closes the popup. Tool-call formats: playbook §4.

Never pre-fill `PurchaseTemplate`'s icon/price/reward fields on the prefab — `PurchaseTemplateController.InitData(PackageTemplateModel)` overwrites them from CSV at runtime.

If the Controller isn't generated yet, leave the root unassigned with a binding note.

### Cheat buttons (cả 2 branch) — chỉ khi task yêu cầu

Task mang tag `[CHEAT]` (hoặc `**Custom delta:**` liệt kê cheat dạng `name · label · method`) thì Phase C wire luôn cheat menu. **KHÔNG tự bịa cheat khi task không yêu cầu** — đó là quyết định của planning.

Chi tiết đầy đủ (guid, size, code pattern, recipe `unity_execute_code` để wire persistent `onClick`): [`.claude/skills/feature-cheat/SKILL.md`](../skills/feature-cheat/SKILL.md). Rút gọn:

- `screen_template` **KHÔNG** ship sẵn cheat menu → instantiate `Templates/CheatMenu.prefab` (guid `ec2aa73aad0a4ea4ab74e7da72c63287`) làm con của prefab root, cạnh `full_screen_template`. Giữ nguyên component trên root (`CheatMenuController` + `GameCheatObjectController`), trỏ `_targetMenu` vào `Menu`.
- Mỗi cheat = 1 instance `ButtonCheatTemplate.prefab` (guid `7322ef5c5cec00a4fab0c80fca752b4b`; bản on/off dùng `ToggleCheatTemplate.prefab`, guid `df02728a597bbdc4b901a2de81e50c4b`) làm con **trực tiếp** của `CheatMenu/Menu` (layout group có sẵn — không thêm cái mới).
- Tên GameObject = PascalCase ngắn theo hành động; `content` Text = nhãn tiếng Anh ngắn; **không gắn `LocalizesUI`** (ngoại lệ duy nhất của luật localize ở §3b — cheat là dev-only).
- Cùng một width cho mọi nút trong menu; giữ size kế thừa từ prefab, đừng tự đặt số.
- Xoá/đổi 2 nút mẫu `Add Point` / `Remove Point` có sẵn trong `CheatMenu` — không ship nguyên trạng.
- `onClick` → đúng method `public Cheat_*` trên Controller; verify YAML có `m_MethodName` + `m_TargetAssemblyTypeName: <Feature>Controller, <assembly>` (tên assembly lấy từ asmdef chứa controller đó, đừng đoán).

---

## 5. Save and verify — Phase C checkpoint (final gate)

1. Save the prefab. Screenshot-verify (`unity_screenshot_game`, or `unity_play_mode` for true open-animation state) — playbook §5. Don't rely on hierarchy inspection alone.
2. Run playbook §8 runnable checks (`unity_gameobject_info`, `unity_component_get_properties`, `unity_search_missing_references`); if any `.cs` changed, recompile and read errors before done.
3. **Phase C checkpoint** — same "Checkpoint mode" as Phases A/B: show user and wait (interactive), or spawn `ui-visual-reviewer` with `phase: "C"` and get `pass` (autonomous). Do not mark complete on your own say-so.
4. Reopen the prefab — hierarchy, references, and bindings must survive; no missing-script or missing-reference warnings on instantiate.
5. **v1 only — evidence report:** save the final clean 1080×1920 screenshot as sibling `<Screen>.unity.png`, then create and validate `<Screen>.ui-build-report.json`:

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
| **Layout mode** | Exactly one of `Popup` (sib=1) / `FullScreen` (sib=2) active, never both/neither (Package → always `Popup`). |
| **Containment** | No new node is a direct child of `Popup`/`FullScreen`; cooldown (if any) is at `Popup/content/CooldownTime`, not `Popup/CooldownTime`. |
| **Open animation (`MainUI`)** | Controller's `MainUI` is **None** — `grep -n "MainUI:" <your>.prefab` → `MainUI: {fileID: 0}` (or no override at all). A full-screen screen with `MainUI` = `FullScreen` is a **fail**: it pops instead of fading (§0 "Layout mode → `MainUI`"). Assigned on purpose → deviation must be recorded at §0d. |
| **Canvas render mode** | Root Canvas must **inherit** the base `screen_template` canvas — the base serializes `m_RenderMode: 1` (Screen Space – Camera) with a null `m_Camera`, so the variant must carry **no** `propertyPath: m_RenderMode` override (`grep -n "propertyPath: m_RenderMode" <your>.prefab` → no hits). **Never Screen Space – Overlay.** Check the *serialized* value, not `Canvas.renderMode`: with a null camera the runtime getter reads back `ScreenSpaceOverlay`, so "restoring" the mode from that getter silently creates the override (task 111 shipped exactly this; playbook §5 has the revert snippet). The §5 RenderTexture helper must restore the *original* mode — a hardcoded Overlay restore bakes Overlay into the prefab (DungeonGuide shipped this). A Feature-root prefab is always a **Variant** of `screen_template`, never a fresh Canvas. |
| **Panel framing** | Every visible panel/card/frame is a **template instance** (`FrameTemplate`/`FrameTemplateInside`/`LayoutTemplate`/`ItemElement`, i.e. carries `m_SourcePrefab`), never a raw `Image` recoloured by hand. Spec containers carry no visible `background`/`border`/`boxShadow` (validator `container_style`); framing comes from a frame-template element anchored stretch. |
| **Scrolling** | Mọi vùng cuộn là **template instance** (`ScrollViewTemplate` guid `8eb29c9ddda20e949a8fbcc106b669b1`, hoặc `ScrollLoopTemplate`), nội dung nằm trong `Viewport/Content`. Không `ScrollRect`/`RectMask2D` nào được gắn tay ngoài một instance đó — kiểm bằng `unity_execute_code`: mọi `ScrollRect` phải có `PrefabUtility.GetCorrespondingObjectFromSource` trỏ về một scroll template (§3d). |
| **Titled sections** | Mỗi khối nội dung có tiêu đề là một instance `FrameTemplateInside` bọc thân khối (`VerticalLayoutGroup` + `ContentSizeFitter`), tiêu đề là instance `ButtonTitleTemplate` với `LayoutElement.ignoreLayout = true`, pivot/anchor `top-center`, `pos (0,30)`, `size (0,60)`, `TitleText` có `LocalizesUI` + key đã đăng ký; list cha `spacing ≥ 40`. Không nhãn `TextTemplate` rời đứng trên text trần (§3d). |
| **Tabs** | Feature có tab điều hướng → `FullScreen/Bot/TabBottomTemplate` **active**, mọi `TabToggleIconTemplate`/`TabToggleTextTemplate` là con trực tiếp của một instance `TabBottomTemplate` (không có row tab tự dựng trong `Mid`), `UI_TabExtensions` trên thanh đó có `_toggleList`/`_objectList` cùng độ dài và controller không tự `AddListener(onValueChanged)` (§3d). |
| **Cheat** | Task có `[CHEAT]` → prefab có một instance `CheatMenu` ở root, mọi cheat button là instance `ButtonCheatTemplate` **con trực tiếp** của `CheatMenu/Menu`, cùng width, nhãn KHÔNG có `LocalizesUI`, `onClick` trỏ tới một `public Cheat_*` có thật trên Controller (`grep -n "m_MethodName" <your>.prefab`), 2 nút mẫu `Add Point`/`Remove Point` đã bị xoá/đổi. Task KHÔNG có `[CHEAT]` → prefab không được mọc thêm nút cheat nào. |
| **Localize** | Mọi STATIC label có `LocalizesUI` + `LangKey` đăng ký (tái dụng key generic khi có; title = `#[featurename]_title`); mọi DYNAMIC label KHÔNG có `LocalizesUI` (§3b). Không node nào gắn 2 component. **Ngoại lệ:** nhãn cheat trong `CheatMenu/Menu` là dev-only → KHÔNG localize. |
| **Spec-sheet gate** | Spec-sheet existed before the first Unity mutation (§0c); interactive: user approved it at Checkpoint 0 (§0d). |
| **Layout groups** | Every row/column/grid of ≥2 siblings is driven by a layout group with spec-sheet spacing/padding (§3e) — no hand-spaced sibling chains. |
| **Pinned view** | Every checkpoint screenshot was taken at the pinned 1080×1920 Game view (playbook §0). |
| **v1 evidence** | `.ui-build-report.json` validates against current spec/kit hashes and `.ui-visual-diff.json`; `.unity.png` is clean 1080×1920; structural, visual, localization all `pass`; missing references = 0. |
| **Package branch** | Prefab is still a **Variant** of `PackageTemplate.prefab` (Variant Parent populated); `Popup/content/PurchaseTemplate/PurchaseTemplateController` wired into `_purchase` (not stale/null); IAP product id resolves against `pack_id` in `<featuresRoot>/[PackageName]/CsvConfig/[PackageName].csv` (not the legacy `Assets/Csv/Collection/Packages/…`). |
