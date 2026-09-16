# User Segmentation & Action Engine
## MVP Design v0.4 — bản tích hợp review kỹ thuật, chốt để code

**Version:** 0.4, cập nhật review vòng 4 (action `frequency` one-shot / repeatable; lịch sử action lưu vào player data của game; vòng 3 bổ sung §13 Measurement & Evaluation; vòng 2 ngày 2026-09-11 thay v0.3 ngày 2026-09-10)
**Date:** 2026-09-14
**Scope:** Mobile game portfolio — Puzzle, Mid-core, Action, Idle/Tycoon, ...
**Trạng thái:** Bản hoàn chỉnh sau hai vòng review. Trả lời xong bảng 1.1 và bước 0 (§12.1, gồm 0(f) A/B hardcode) thì code.

**Bổ sung ở review vòng 4 (2026-09-14; bảng đối chiếu ở Phụ lục B.4)**

Hai điểm Product yêu cầu sau khi đọc bản vòng 3. Không thêm endpoint hay service; thêm một field trên action, một interface nhỏ game implement, và tách state trên client thành hai tầng.

- **Action có `frequency: one_shot | repeatable`** (mặc định `repeatable`). One-shot = tối đa **một lần `selected` trong đời user**, bất kể rule nào chọn, bất kể reinstall. Khoá tại `selected`, hoàn lại khi `action_failed` vì `offline` / `no_permission`, giống cooldown. Dùng cho starter pack, quà lần đầu, onboarding hint (§8.3, §8.7).
- **Tách state trên client thành hai tầng (§4.5).** Tầng **lịch sử action** (action nào, mấy lần, lần đầu, lần cuối) là dữ liệu theo *user*, lưu vào **player data của game** qua interface `IActionHistoryStore` để đi theo save + backup sẵn có của game, sống qua reinstall / đổi máy. Tầng còn lại (counter, bucket, edge state, cooldown / cap, assignment, cache) là dữ liệu vận hành của SDK, vẫn ở file riêng của SDK. One-shot đọc từ tầng lịch sử; cooldown sau reinstall cũng lấy `last_at` từ đó làm sàn.
- SDK giữ **bản mirror** của lịch sử trong file riêng và merge hai bản lúc init (quy tắc merge như Seed: `count` max, `first_at` min khác 0, `last_at` max), nên save của game flush muộn hay restore cloud về máy đã chơi đều không mở lại one-shot đã dùng (§4.5).
- Logic game sẵn có kiểu "một lần" (starter pack đã bật) chuyển sang engine bằng `frequency: one_shot` cộng `MarkActionUsed()` trong bước 7 để user cũ không nhận lần hai (§4.5, §8.2, §12.1).
- Tracking: `dropped` có reason `one_shot`; `action_selected` mang `nth`; `exp_exposure` mang `history_used` để DWH tách user đã dùng hết one-shot trước experiment (§11.1, §9.3). CLI lint `frequency` (§10.3).
- **Không** cho AST đọc lịch sử action trong MVP; rule cần "đã nhận X rồi thì…" dùng `custom.*` do game ghi (§5.1).

**Bổ sung ở review vòng 3 (2026-09-14; bảng đối chiếu ở Phụ lục B.3)**

Tài liệu đã có control / treatment, exposure, cỡ mẫu, A/A, tiêu chí đi tiếp, nhưng rải ở §9, §11, §12.4 và không có chỗ nào gom lại câu hỏi "hệ thống có tự chứng minh hiệu quả được không". Vòng 3 thêm **§13 Measurement & Evaluation** để gom và bổ sung sáu thứ còn thiếu. **Không thêm thành phần hệ thống**: mọi phép đo chạy trên DWH và BI hiện có.

- **Ba tầng câu hỏi:** segment có đúng không → rule có lift không → engine có đáng không. Giữ lập trường cũ: MVP đo engine bằng per-experiment control cộng ba tiêu chí §12.4; global holdout là công cụ Phase 2 (§13.1).
- **Kiểm định segment ở tuần shadow**, không tốn mẫu: D7 theo `seg_lifecycle` phải tách đơn điệu, không tách thì sửa formula trước khi bật action (§13.1, §13.6).
- **Bảng engine health khi live** với ngưỡng và hành động: adoption, execution ratio, fire rate so với bước 0, SRM, exposure tích luỹ, latency. Thêm field `eval_ms` vào `seg_decision` (§13.2, §11.1).
- **`result` của `action_executed` có bộ giá trị cố định theo action type** để phân biệt user không thấy / bỏ qua / dùng mà vẫn rời (§13.3, §8.6).
- **Pre-registration trong PR** kèm **guardrail** theo group của action; guardrail vỡ thì không ship dù D7 tăng. Ad revenue / user vào guardrail vì tracking hiện có đã đếm ở cấp user (§13.4, §9.3).
- **Holdback 0.9 / 0.1 trong 4–8 tuần sau khi thắng**, thay cho "sửa base, xoá experiment" ngay, để bắt lift phai vì novelty (§13.5, §9.1).
- Phase 2 thêm CUPED và sequential test khai trước (§12.2, §13.7).

**Bổ sung ở review vòng 2 (2026-09-11, sửa thẳng vào bản này; bảng đối chiếu ở Phụ lục B.2)**

*Bớt thêm*

- **Bỏ canary channel khỏi MVP.** Worker trả một config duy nhất kèm `server_time`. Config sai cú pháp / ref bị CLI chặn; sai nghiệp vụ được `then: "none"` + experiment chặn; exception được sandbox chặn; client từ chối config thì tự dùng cache, tức rollback tự động. Với một game, canary không còn việc để làm mà kéo theo hash rollout, fallback hai tầng, `config_channel`, lệnh `promote`. Giữ `history/{version}` + `rollback`. Canary → Phase 2 khi ≥ 3 game (§10.1).
- **Một KV key `envelope` thay ba key.** Ở 1M DAU, ba key là cỡ trăm đô/tháng phí KV read, không phải "vài đô" (§10.2).
- **Gộp `seg_decision` của `SESSION_START` vào `seg_snapshot`.** Rule `level` tại `SESSION_START` làm hai event này luôn đi đôi; bớt một event mỗi session (§11.1).
- **Bỏ window `1d`, thêm window `session`.** "Hôm nay UTC" reset lúc 7h sáng với user Việt Nam nên `1d` làm rule in-session mất nghĩa; `@session` là một counter reset tại `SESSION_START`, rẻ hơn bucket (§4.3).

*Hiệu quả hơn với cùng chi phí*

- **`allocation` mặc định 1.0.** Mỗi layer chỉ có một experiment nên user ngoài allocation nhận `none` mà không phải control, tức mẫu mất trắng. Số user cần chạm trigger giảm nửa (§9.1, §9.3).
- **Bước 0(f) bắt buộc:** hardcode rule ứng viên số một chạy A/B 2 tuần **trước khi code engine**. Lift +10–20% trong bảng mẫu là mức lạc quan; can thiệp in-game thường cho +2–5%, cần mẫu gấp bốn. Effect size để tính mẫu phải lấy từ A/B này (§9.3, §12.1).
- **Chốt ngày readout trước khi bật treatment.** Không peeking; kiểm SRM ở tuần A/A (§9.3).
- **`compile` bắt buộc**, không tuỳ chọn. Không có nó thì không ai ngoài dev soạn được rule và tiêu chí "Product tự publish" không thực tế (§10.3).
- **Tiêu chí đi tiếp thêm một dòng đo *engine*:** rule thứ hai đi từ ý tưởng đến live ≤ 1 tuần không release. Hai tiêu chí cũ đo *rule*, hardcode + Remote Config cũng đạt được (§12.4).
- **CLI `replay`** chạy event thật của N user qua engine để đo fire rate chính xác trước publish (tuỳ chọn, sau bước 7; §10.3).

*Vá lỗ hổng*

- **Logic game sẵn có trùng action type** (notification tự đặt, starter pack, difficulty knob): base `then` = action tái tạo hành vi cũ, **không phải `none`**. Ship `none` rồi gỡ code cũ là làm cả base mất notification suốt shadow / A/A / control (§8.2, §8.4).
- **`PURCHASE` chỉ emit từ luồng mua mới, không từ restore.** Sau reinstall + `Seed()`, giao dịch cũ chưa có trong ring buffer sẽ bị cộng lần hai (§4.1).
- **Seed:** thêm `LastActiveAt`; `Initialize()` nhận seed provider; áp khi `seeded == false` bất kể state trống, merge bằng `max` (§4.6).
- **Persist `context.actions_shown_today` + ngày UTC** (cap ngày đang reset mỗi lần kill app); serialize trên main thread, ghi file ở thread nền (§4.5).
- **File state không đọc được → reset, log `state_reset`, `NeedsSeed = true`** (§4.5).
- **Đồng hồ nhảy tiến** phát hiện bằng đối chiếu monotonic trong cùng khoảng foreground (§4.4).
- **Hoàn lại cooldown / cap** khi `action_failed` với reason `offline` / `no_permission` (§8.3).
- **Lint:** rule `on: SESSION_START` chỉ được trỏ action group `notification` (§10.3).
- **Manifest trong repo do SDK export**, không viết tay (§8.5, §11.3).
- **Giờ của team game cho bước 7** tính riêng, không nằm trong 2 engineer (§12.1, bảng 1.1).

**Thay đổi chính so với v0.3** (bảng đối chiếu đầy đủ ở Phụ lục B)

*Bớt bộ phận — đơn giản hơn, không mất hiệu quả*

- **Bỏ pending queue, `deliver_on`, `ttl_s`, ring buffer dedupe, `action_expired`.** Action luôn thực thi ngay tại trigger. Rule cần hiển thị ở màn nào thì viết trigger tại màn đó: `on: "SCREEN_OPEN:<screen_id>"`. State đã nhớ điều vừa xảy ra nên không cần "giữ chỗ" action.
- **Bỏ `null` và logic ba trị, bỏ `COALESCE`.** Mọi state có giá trị mặc định toàn phần; chia 0 ra 0. Config sai ref / sai kiểu bị **từ chối toàn bộ** ở CLI và ở client, thay cho partial degradation.
- **Bỏ "known-good" trên client, bỏ ETag, bỏ edge cache.** Server trả một envelope `{ stable, canary, canary_rollout, server_time }`; client chọn channel theo hash. New install ngoài rollout vẫn có `stable`. Rollback = publish lại `stable`. *(Review vòng 2: bỏ luôn canary trong MVP, envelope chỉ còn `{ config, server_time }`; xem khối trên và §10.1.)*
- **Bỏ `rule_fired`, `decision_trace`, `seg_changed`.** Thay bằng một event `seg_decision` mỗi trigger có match, cộng user property cho partition và assignment.
- **Holdout giữ cơ chế, `allocation` mặc định 0** trong MVP. Không tiêu 5% mẫu khi chỉ có một hai rule có action thật.
- **Bỏ debounce persist.** Ghi sau mỗi trigger và mỗi lần app pause.

*Vá lỗ hổng*

- **Seed state từ dữ liệu game sẵn có** cho user hiện hữu và sau restore cloud save. Không seed thì whale thành `non_payer` và cả base thành `new` suốt kỳ MVP.
- **Ranh giới session** định nghĩa bằng `session_timeout_s`, đặt khớp tracking SDK.
- **Thời gian:** `now = giờ thiết bị + offset`. Không dùng monotonic làm nguồn vì nó dừng khi iOS suspend app.
- Sửa reducer `PROGRESS_QUIT`; `unit_id` là NUMBER; `PURCHASE` có `transaction_id` và dedupe.
- **Thêm `CUSTOM_EVENT`** có whitelist trong manifest cho genre không map được vào `PROGRESS_*` (idle / tycoon).
- **Sandbox engine:** exception không bao giờ lan ra game.
- Tag hysteresis reset theo hash định nghĩa; cấm `exp.*` trong AST; `custom.*` có schema trong manifest; group `notification` không tính vào cap ngày; `SESSION_START` chỉ chạy sau khi fetch config xong hoặc timeout.
- Token cho `env` ngoài prod; fetch lại config khi resume.

*Thêm để hiệu quả hơn*

- **Bước 0 trước khi code:** ước lượng exposure rate, inventory logic sẵn có của pilot, kiểm genre, kiểm giới hạn tracking SDK. Khuyến nghị hardcode rule ứng viên số một chạy A/B 2 tuần để có baseline lift và test pipeline readout.
- **Tuần shadow đầu chạy A/A** (hai variant đều `none`).
- **Outcome window tính từ lần exposure đầu**, không tính từ install.
- CLI nhận string expression và compile ra AST (client chỉ đọc AST). *(Review vòng 2: `compile` thành bắt buộc.)*

---

## 0. Tóm tắt một trang

**Câu hỏi hệ thống trả lời:** Với user này, ở thời điểm này trong game, nên làm gì để tăng retention / engagement / revenue?

**Một câu mô tả kiến trúc:** Server định nghĩa cách suy nghĩ (config), client suy nghĩ và hành động ngay trong session. Không có server realtime.

```
Cloudflare (Control Plane)            Client SDK (Decision + Execution)
  { config, server_time }  ──▶        Event → Reducer → State → Formula
                                      → Segment → Rule → Resolver → Executor
                                      → Tracking (đi qua SDK tracking hiện có)
                                      → Evaluation trên DWH (§13, không có thành phần mới)
```

**Phạm vi MVP (Phase 1):**

| Hạng mục | Số lượng |
|---|---|
| Game pilot | 1 (game có DAU lớn nhất, **đã kiểm exposure rate ở bước 0 và effect size ở bước 0(f)**) |
| Event chuẩn | 10 + `CUSTOM_STATE` + `CUSTOM_EVENT` (whitelist ≤ 10 tên) |
| Primitive state | ~30 |
| Formula | 3–6 |
| Segment | 5–8 |
| Rule | 6–10 (đa số `then: "none"`, trừ rule thay logic game sẵn có, §8.2) |
| Action type | 5 (pilot có thể chỉ hỗ trợ 3–4) |
| Experiment đồng thời | 1–2, mỗi cái **2 nhánh**, `allocation` 1.0; holdout 0% |

**Không làm trong MVP:** ML, Config Editor UI, server-side decision, state sync cross-device, action có giá trị kinh tế cao grant từ client, realtime push, engine ngoài Unity, window theo giờ, patch config generic, experiment trên ngưỡng segment, **action trì hoãn (pending queue), holdout > 0, `null` trong formula, nhiều experiment mỗi layer, canary channel / rollout theo %, window `1d`**.

**Định nghĩa thành công của MVP:** Trong 3 tháng sau khi live trên game pilot, ít nhất một rule cho thấy lift D7 (tính từ lần exposure đầu) có ý nghĩa thống kê so với control **mà không vỡ guardrail đã khai trước (§13.4)**, Product/Data đổi được segment / rule / experiment mà **không cần release game**, và **rule thứ hai đi từ ý tưởng đến live trong ≤ 1 tuần** không release (§12.4). Không đạt → dừng và review lại trước khi mở rộng.

**Điều kiện tiên quyết (bước 0, §12.1):** Data ước lượng được tỉ lệ user chạm điều kiện của 2–3 rule ứng viên trên DWH, **rule ứng viên số một đã chạy A/B hardcode 2 tuần** cho effect size thật, và hai con số đó kết luận pilot đủ mẫu trong 6–8 tuần. Không đủ → đổi rule, đổi game hoặc không làm.

---

## 1. Hiện trạng và lý do tự xây

### 1.1 Hiện trạng (điền trước khi review kỹ thuật)

| Mục | Giá trị | Ảnh hưởng |
|---|---|---|
| Tracking SDK hiện tại | *[điền: Firebase / AppsFlyer / SDK riêng]* | Event mới đi qua đây. Giới hạn số param / độ dài string / số user property quyết định cách gói `seg_decision` và `seg_snapshot` (§11). Nếu là Firebase → kiểm **trần export BigQuery 1M event/ngày** của property standard |
| Data warehouse | *[điền: BigQuery / ...]* | Nơi tính retention, `const.*`, experiment readout, **exposure rate** (§9.3) |
| Identity | *[điền: install id / account id]* | Assignment có sticky qua reinstall không. Kèm **reinstall rate** của game pilot |
| **Genre game pilot** | *[điền: puzzle / idle / ...]* | Quyết định `PROGRESS_*` có map được không hay cần `CUSTOM_EVENT` (§4.1). Idle / tycoon không có fail / streak → rule mẫu §8.2 vô nghĩa, phải chọn rule khác |
| **Logic sẵn có trong pilot trùng với action type** | *[điền: game đã tự đặt local notification? tự bật starter pack? có knob difficulty?]* | Bước 7 chuyển logic đó sang engine theo §8.2 "ngoại lệ": base `then` = action tái tạo đúng hành vi cũ, gỡ code cũ trong cùng bản update. Hai bên cùng làm một việc là bug; ship `none` rồi gỡ code cũ là regression cho cả base |
| **Dữ liệu seed sẵn có trong pilot** | *[điền: ngày cài, số lần mua, tổng chi, level max lấy được từ đâu]* | Cần cho `Seed()` (§4.6). Không có → segment monetization / lifecycle sai cho toàn bộ user cũ |
| **Save system của pilot** | *[điền: local save format, có cloud backup / restore không, flush khi nào (mỗi N giây / pause / sự kiện), restore chạy trước hay sau init SDK]* | Nơi gắn `IActionHistoryStore` (§4.5): lịch sử action và one-shot đi theo save của game. Flush thưa → SDK mirror bù; restore sau init → game gọi `ImportActionHistory()` |
| **Số user property tracking còn trống** | *[điền: đang dùng x/25]* | Engine cần 4–6 slot (§11.2). Trần 25 của Firebase là **chung cho cả game trong project**, không phải riêng engine |
| **Giờ team game cho bước 7** | *[điền: dev-tuần team pilot cam kết; ai viết executor / map event / QA]* | Không nằm trong 2 engineer của dự án; là chỗ hay trượt gấp đôi (§12.1) |
| Engine game trong portfolio | *[điền: % Unity, engine khác]* | Mỗi engine thêm = 1 SDK + bộ test vector chung |
| Backend hiện có | *[điền: Cloudflare Worker / Supabase / ...]* | Nơi đặt endpoint config |

### 1.2 Vì sao không mua sẵn

| Giải pháp | Thiếu gì so với yêu cầu |
|---|---|
| Firebase Remote Config + A/B | User property cập nhật chậm, không quyết định được ngay sau level fail. Không có formula / segment tuỳ biến |
| Statsig / Amplitude Experiment | Local evaluation có, nhưng không có state reducer trong game; chi phí theo MAU đắt ở quy mô vài chục game |
| Braze / CleverTap | Mạnh về out-of-session (push, in-app message), yếu về gameplay action (booster, difficulty) |

**Kết luận:** tự xây phần **engine trong game + config delivery**. Phần tracking, DWH, dashboard, A/B readout **dùng lại hạ tầng hiện có**. Riêng việc **kiểm chứng giả thuyết đầu tiên** (rule ứng viên số một có tác dụng không) thì **bắt buộc** làm bằng Remote Config A/B hoặc một hash đơn giản trong game ở bước 0(f), không đợi engine: nó cho effect size thật để tính mẫu và cho luôn query readout. Giá trị riêng của engine so với "hardcode + Remote Config" không phải lift của một rule mà là tốc độ lặp và tái dùng qua nhiều game; §12.4 đo đúng thứ đó.

---

## 2. Kiến trúc tổng thể

### 2.1 Hai plane

```
┌──────────────────────── CONTROL PLANE (Cloudflare) ─────────────────────────┐
│  Git repo config (JSON) → CLI compile + validate + simulate → publish → KV  │
│    config/{game}/{env}/envelope (một key)     .../history/{version}         │
│  GET /config?game=&env=   (Worker đọc 1 KV key, trả config + server_time,   │
│                            KHÔNG edge cache, KHÔNG ETag)                    │
└─────────────────────────────────────┬───────────────────────────────────────┘
                                      │ session start / resume
┌─────────────────────────────────────▼───────────────────────────────────────┐
│  CLIENT SDK                                                                 │
│  validate → holdout → assignment                                            │
│  Event queue ─▶ Reducer ─▶ State ─▶ Formula ─▶ Segment ─▶ Rule ─▶ Resolver  │
│                                                              │              │
│                                              Action Executor (game đăng ký) │
│                                                              │              │
│  Tracking events + user properties ─▶ SDK tracking hiện có ─▶ DWH           │
└─────────────────────────────────────────────────────────────────────────────┘
```

Endpoint mới duy nhất trong MVP: `GET /config`. Không có `/events`, `/state-sync`, `/decision-trace` riêng — tất cả đi qua tracking hiện có. Request `/config` **không mang** manifest hay bất kỳ tham số theo user nào.

### 2.2 Hai loại quyết định

| | In-session | Out-of-session |
|---|---|---|
| Ai quyết | Client SDK | Server batch trên DWH |
| Khi nào | Ngay tại trigger event trong game | Hàng ngày / theo lịch |
| Ví dụ | Fail 3 lần liên tiếp → tặng booster. Mở shop lần đầu → offer starter | User 3 ngày không mở app → push. Churned quay lại → comeback reward |
| Phase | **MVP** | Phase 2 |

Lý do tách: client chỉ chạy khi app mở, **không thể hành động với user vắng mặt**. Cầu nối duy nhất trong MVP là action `SCHEDULE_LOCAL_NOTIFICATION`, đặt lịch tại **`SESSION_START`**: mỗi lần mở app đặt lại lịch cho lần vắng kế tiếp; user quay lại trước giờ hẹn thì lịch bị thay. Không phụ thuộc `SESSION_END` có bắn được hay không.

### 2.3 Thứ tự quyết định khi load config

```
1. Fetch { config, server_time } (≤ 3 giây) hoặc lấy cache
2. Validate config (schema, ref, kiểu, min_sdk_version)
   fail → log config_rejected → dùng cache → không có cache → config_source = none
3. Disable từng rule dùng action / reward / screen / custom event ngoài manifest (rule_unsupported)
4. Holdout: hash(user_id, "holdout") → exp.holdout
5. Từng layer: experiment còn hạn → assignment (sticky, persist)
6. Mở event queue
```

Không có bước chọn channel: MVP chỉ có một config mỗi `game / env` (§10.1). Client từ chối config thì dùng cache, tức là rollback tự động không cần server làm gì.

---

## 3. Pipeline và nguyên tắc

### 3.1 Pipeline

```
Event (chuẩn hoá, vào queue)
  ↓  Reducer      — SDK cập nhật raw state, game không phải code
State (primitive)
  ↓  Formula      — AST JSON từ config
Feature (derived)
  ↓  Segment      — Tag (bool) và Partition (một giá trị / dimension)
  ↓  Rule         — on + when → candidate action (hoặc "none")
  ↓  Resolver     — filter → priority → một action mỗi conflict group → cap ngày
Action              (thực thi NGAY, không có hàng chờ)
  ↓  Executor     — game đăng ký theo action type
Tracking
  ↓  Evaluation   — trên DWH, ngoài client: segment đúng không, rule có lift không, engine có đáng không (§13)
```

### 3.2 Nguyên tắc (9)

1. **Tracking cung cấp dữ liệu, segmentation tạo interpretation.** Không thay tracking hiện có.
2. **Segment mô tả user, rule quyết định action.** Không có tag → action trực tiếp.
3. **Raw state + derived feature, không bùng nổ tag.** Segment chỉ là kết quả evaluation.
4. **Game chỉ implement: emit event chuẩn + seed + action executor.** Mọi logic nghiệp vụ nằm trong config.
5. **Client quyết định in-session, server quyết định out-of-session.** Không có server realtime.
6. **Personalization không phải authority.** Tiền, inventory giá trị cao, entitlement vẫn do backend hiện có quyết.
7. **Mọi action phải đo được.** Selected / presented / executed / outcome tách riêng.
8. **Rule trước, ML sau.** Rule mới ship với `then: "none"` và chỉ nhận action thật qua experiment.
9. **Config không bao giờ làm game crash.** Config sai bị từ chối toàn bộ; exception trong engine tắt engine, không lan ra game.

---

## 4. Event Schema và State Reducer

### 4.1 Event chuẩn

| Event | Payload bắt buộc | Ghi chú |
|---|---|---|
| `SESSION_START` | — | SDK tự emit khi initialize và khi resume sau background ≥ `session_timeout_s` (§4.4) |
| `SESSION_END` | — | SDK tự emit khi đóng session (quit, resume quá timeout, hoặc initialize thấy session cũ chưa đóng). **Chỉ để persist, không phải trigger** |
| `PROGRESS_START` | `unit_id` (NUMBER) | unit = level / stage / mission tuỳ game. **`unit_id` là số nguyên**; game có id dạng chuỗi thì map sang index |
| `PROGRESS_COMPLETE` | `unit_id`, `duration_s` | |
| `PROGRESS_FAIL` | `unit_id`, `duration_s` | |
| `PROGRESS_QUIT` | `unit_id`, `duration_s` | Thoát giữa chừng |
| `PURCHASE` | `product_id`, `usd`, `transaction_id`, `execution_id?` | Từ callback **mua mới** thành công. **Restore purchase KHÔNG emit `PURCHASE`:** sau reinstall + `Seed()`, giao dịch cũ chưa có trong ring buffer của bản cài mới sẽ bị cộng lần hai vào `total_spend_usd` và kéo `last_purchase_at` về `now`; ring buffer chỉ chặn callback đôi trong cùng bản cài. `usd` = giá catalog USD của product trong config game, không phải số tiền local. **`transaction_id` bắt buộc để dedupe** callback đôi. `execution_id` tuỳ chọn; thiếu thì SDK tự gắn last-touch (§8.7) |
| `AD_REWARDED` | `placement` | |
| `AD_INTERSTITIAL` | `placement` | |
| `SCREEN_OPEN` | `screen_id` | `screen_id` nên nằm trong manifest để rule tham chiếu được |
| `CUSTOM_STATE` | `key`, `value` | Game set `custom.*` trực tiếp, không qua reducer. `key` và kiểu khai trong manifest |
| `CUSTOM_EVENT` | `name` | **Trigger tuỳ game**, `name` phải nằm trong whitelist `custom_events` của manifest (≤ 10 tên). Reducer đếm `state.custom_event_count.<name>` có window |

Game map khái niệm của mình vào `unit_id` (puzzle → level, mid-core → stage / quest). `PROGRESS_*` là xương sống cho game có vòng lặp thử–thất bại. **Game không có vòng lặp đó (idle / tycoon / sim) dùng `CUSTOM_EVENT`** với tên có nghĩa (`offline_claim`, `upgrade`, `currency_stall`) thay cho hack `SCREEN_OPEN` giả. Whitelist ≤ 10 giữ schema portfolio không bùng nổ; tên dùng chung trong genre thì đưa vào `base/` của repo config.

### 4.2 Reducer chuẩn (SDK thực hiện)

| Event | Cập nhật state |
|---|---|
| `SESSION_START` | Nếu state trống và chưa seed → `install_at = now`. Tính `days_since_last_active = utcDay(now) − utcDay(last_active_at)` **trước khi** set `last_active_at = now` (lần đầu → 0); giữ giá trị này suốt session. `session_count += 1` (bucket ngày). `session_open = true`, `foreground_anchor = now`. **Reset mọi counter `@session` về 0** (§4.3) |
| `SESSION_END` | Đóng session: `session_open = false`. Playtime đã được cộng dồn ở mỗi persist (§4.5) nên không cộng thêm |
| `PROGRESS_START` | Nếu `unit_id ≠ progress.current` → `attempt_count_current_unit = 0`. `progress.current = unit_id`; `attempt_count += 1`; `attempt_count_current_unit += 1` |
| `PROGRESS_COMPLETE` | `complete_count += 1`; `win_streak += 1`; `fail_streak = 0`; `progress.max = max(progress.max, unit_id)`; `attempt_count_current_unit = 0` |
| `PROGRESS_FAIL` | `fail_count += 1`; `fail_streak += 1`; `win_streak = 0`; `last_fail_at = now` |
| `PROGRESS_QUIT` | `quit_count += 1`; **nếu `now − last_fail_at ≤ 60`** → `quit_after_fail_count += 1` (v0.3 viết "event trước là FAIL" — sai, vì giữa FAIL và QUIT luôn có `PROGRESS_START`) |
| `PURCHASE` | `transaction_id` có trong ring buffer 50 id gần nhất → **bỏ event** (không reducer, không trigger). Ngược lại: ghi id vào buffer; `purchase_count += 1`; `total_spend_usd += usd`; `last_purchase_at = now`; `first_purchase_at = now` nếu bằng 0 |
| `AD_REWARDED` / `AD_INTERSTITIAL` | `ad_rewarded_count += 1` / `ad_interstitial_count += 1` |
| `SCREEN_OPEN` | `context.screen = screen_id` |
| `CUSTOM_EVENT` | `custom_event_count.<name> += 1` (bucket ngày); `context.last_event = "CUSTOM_EVENT:<name>"` |
| `CUSTOM_STATE` | `custom.<key> = value` (kiểm kiểu theo manifest; sai kiểu → bỏ, log một lần) |

Mọi event đều cập nhật `last_event_at` (nội bộ) và `context.last_event`. Mọi counter `*_count` và `playtime_s` được ghi thêm vào bucket ngày để hỗ trợ window (§4.3).

### 4.3 Windowed counter

- **Một loại bucket duy nhất: theo ngày UTC, giữ 30 bucket.** Không có bucket giờ. Cộng **một counter `session`** cho mỗi loại counter có window; đây là counter thường, không phải bucket.
- Window hỗ trợ: `session`, `7d`, `30d`.
  - `7d` / `30d` **sliding theo ngày lịch UTC** (`7d` = 7 bucket ngày gần nhất, kể cả hôm nay).
  - `session` = từ `SESSION_START` của phiên hiện tại; reducer reset counter này về 0 tại `SESSION_START` (§4.2). Đây là window cho rule in-session ("3 lần stall trong phiên này").
  - **Không có `1d`.** "Hôm nay UTC" reset lúc 7h sáng với user Việt Nam (UTC+7): `currency_stall@1d >= 3` đúng lúc 6:59 và sai lúc 7:01 mà user không làm gì khác. Rule cần "gần đây" dùng `@session`; cần "tuần này" dùng `@7d`. Window `1d` theo giờ địa phương → Phase 2 nếu có rule thật cần.
- Counter có window: `fail_count`, `complete_count`, `attempt_count`, `quit_count`, `session_count`, `playtime_s`, `purchase_count`, `ad_rewarded_count`, `ad_interstitial_count`, và `custom_event_count.<name>` cho mỗi tên trong whitelist. State khác không có window.
- Dung lượng: (9 + ≤ 10) counter × (30 bucket + 1 counter session) × 8 byte ≈ 4.7 KB tối đa.
- **User seed (§4.6) có bucket rỗng** trong 30 ngày đầu → giá trị window thấp giả. Rule dùng window nên kèm `TIME_SINCE(state.seeded_at) > 604800` hoặc chấp nhận.
- Cần window theo giờ thật → Phase 2. Cooldown và cap của action **không** dùng bucket (§8.3).

### 4.4 Thời gian và ranh giới session

**Nguồn thời gian**

- `now = device_utc_now + offset`. `offset = server_time − device_now` học lại tại **mỗi** response `/config` thành công (`server_time` trong body, Worker đặt lúc trả — không có edge cache nên luôn tươi). Persist offset.
- Chưa từng có offset (cài mới, offline) → `offset = 0`, `context.clock_suspect = true` cho tới khi học được.
- **Không dùng monotonic clock làm nguồn.** `Time.realtimeSinceStartup` và `Stopwatch` **không chạy khi iOS suspend app** (Android tương tự với `CLOCK_MONOTONIC`), nên "mốc server + monotonic" lệch đúng bằng thời gian nằm nền.
- **Monotonic chỉ dùng để đối chiếu trong foreground.** Trong một khoảng foreground liên tục (giữa hai event không có pause) nó đáng tin: nếu `Δnow − Δrealtime > 60` giây → đồng hồ vừa bị chỉnh tiến → `context.clock_suspect = true` hết session. Không có bước này thì nhảy tiến chỉ bị phát hiện ở lần fetch sau (offset lệch > 3600), còn trong lúc đó `TIME_SINCE` phồng, cooldown hết sớm và `lifecycle` có thể thành `returning` giả.
- Phát hiện đồng hồ đáng ngờ, đặt `context.clock_suspect = true` hết session khi: `|offset mới − offset cũ| > 3600` giữa hai lần fetch; hoặc `now < last_event_at` (thời gian chạy ngược) — khi đó clamp `now = last_event_at`; hoặc nhảy tiến trong foreground theo đối chiếu monotonic ở trên.
- Mọi timestamp trong state là giờ đã hiệu chỉnh. `NOW` trong formula trả `now`.
- **`days_since_install` và `days_since_last_active` tính theo ngày lịch UTC** (`utcDay(now) − utcDay(ts)`), khớp với bucket. Cần khoảng cách thật theo giây thì dùng `TIME_SINCE(ts)`.

**Ranh giới session** (`session_timeout_s` trong config, mặc định 1800; **đặt bằng session timeout của tracking SDK** để `session_count` khớp DWH)

| Sự kiện app | SDK làm gì |
|---|---|
| Initialize | Đọc state. Nếu `session_open = true` (crash / OS kill lần trước) → emit `SESSION_END` (mốc kết thúc = `max(last_event_at, last_pause_at)`). Emit `SESSION_START`. Fetch config (§10.2). **Queue chỉ mở sau khi fetch xong hoặc timeout 3 giây** — event emit trước đó nằm chờ |
| Pause (background) | `playtime_s += now − foreground_anchor` (bucket `utcDay(now)`); `last_pause_at = now`; persist |
| Resume | `now − last_pause_at ≥ session_timeout_s` → emit `SESSION_END` rồi `SESSION_START` (session mới; notification được đặt lại). Ngược lại `foreground_anchor = now`, tiếp tục session. Fetch lại config nếu lần fetch cuối > 5 phút (kill-switch không phải đợi tới session sau) |
| Quit | Như pause + emit `SESSION_END` |

`context.session_time_s` = tổng giây foreground của session hiện tại.

### 4.5 Persist

State trên client tách **hai tầng**, theo bản chất dữ liệu:

| Tầng | Chứa gì | Lưu ở đâu | Sống qua reinstall / đổi máy? |
|---|---|---|---|
| **Lịch sử action** (theo *user*) | `action_history[action_id] = { count, first_at, last_at }`: user đã trải qua action nào, mấy lần, lần đầu, lần cuối. Nguồn sự thật cho `frequency: one_shot` (§8.3) và sàn cooldown sau reinstall | **Player data của game**, qua `IActionHistoryStore` game implement; đi theo save + backup sẵn có của game. SDK giữ **mirror** trong file riêng | **Có**, theo save của game |
| **Vận hành SDK** (theo *thiết bị / cài đặt*) | state + bucket, edge state, tag hysteresis, cooldown / cap timestamp, `actions_shown_today`, assignment / holdout, cờ exposure, ring buffer, last-touch, offset giờ, config cache | **File JSON riêng của SDK** trong storage app | Không (Phase 2: state-sync; user cũ dùng `Seed()` §4.6) |

Lý do tách: cái gì trả lời "user này *đã nhận* gì" phải theo user và không được mất khi đổi máy; cái gì chỉ để engine chạy đúng trong vài ngày (bucket, cooldown, edge) mất đi thì tự hồi, không đáng kéo vào save của game. Game đã có backup save nên SDK không tự dựng cơ chế sync riêng.

**Tầng lịch sử action (player data):**

```csharp
public interface IActionHistoryStore {
    string Load();              // blob JSON đã lưu trong save của game, hoặc null / "" nếu chưa có
    void   Save(string blob);   // ghi vào save; game quyết định khi nào flush xuống disk / cloud
}
SegmentationSdk.Initialize(new SdkOptions { ActionHistoryStore = new MySaveBackedStore() });
SegmentationSdk.ImportActionHistory(blob);        // sau restore cloud save về máy đã chơi: merge, không ghi đè
SegmentationSdk.MarkActionUsed("starter_pack_offer", at: <epoch>);  // migrate logic "một lần" có sẵn (bước 7)
```

- Blob JSON `{ "v": 1, "actions": { "<action_id>": { "n": 2, "f": 1759000000, "l": 1759200000 } } }`. Khoảng 50 byte mỗi action; ≤ 100 action → ≤ 5 KB. Chỉ có action **đã `selected` ít nhất một lần**; không có entry cho action chưa dùng.
- **Ghi lúc `selected`**, cùng lúc với timestamp cooldown / cap: cập nhật trong bộ nhớ → `Save(blob)` đồng bộ trên main thread (blob nhỏ) → game flush theo cadence của save system. `action_failed` với reason `offline` / `no_permission` → `n -= 1`, `n == 0` thì xoá entry (`l` giữ nguyên nếu `n > 0`; lệch nhỏ, chấp nhận).
- **Mirror:** SDK ghi cùng nội dung vào file riêng. Lúc init: `history = merge(Load(), mirror)`; lúc `ImportActionHistory(blob)`: `history = merge(history, blob)`. **Merge = quy tắc Seed:** `n` lấy max, `f` lấy min khác 0, `l` lấy max. Hệ quả: save của game flush muộn rồi app bị kill → mirror còn; reinstall → player data còn; restore cloud về máy đã chơi vài phiên → hợp hai bản, one-shot đã dùng ở bất kỳ bản nào vẫn là đã dùng. Không có trường hợp user nhận lại one-shot vì lỗi lưu, chỉ có trường hợp bỏ lỡ (chấp nhận, giá trị thấp).
- Blob không parse được → coi tầng này trống, log `state_reset` với reason `history`, **không** xoá mirror, không `NeedsSeed`. Sau merge với mirror, SDK `Save()` lại bản đúng.
- Không có `ActionHistoryStore` trong `SdkOptions` → SDK chỉ dùng mirror và log `rule_unsupported` cho mọi rule trỏ action `one_shot` (one-shot không có chỗ lưu bền thì không được hứa). CLI không kiểm được điều này; debug overlay hiển thị "history store: none".
- `MarkActionUsed(action_id, at)`: ghi `n = max(n, 1)`, `f = l = at` nếu chưa có. Dùng ở bước 7 khi logic "một lần" của game (starter pack đã bật, quà tân thủ đã nhận) chuyển sang engine: user đã nhận theo dữ liệu game → gọi một lần lúc seed, không nhận lần hai. Đây là phần tương ứng của `Seed()` cho tầng lịch sử.
- AST **không** đọc được `action_history` trong MVP (§5.1). Lịch sử đầy đủ để phân tích nằm trên DWH qua `action_selected` / `action_executed` (§11.1); blob chỉ đủ cho quyết định trên client.

**Tầng vận hành SDK (file riêng):**

- Một file JSON trong storage app, ghi **atomic** (file tạm rồi rename). Ghi **sau mỗi trigger event được xử lý**, mỗi lần app pause, và ngay khi assignment / holdout đổi. Trigger cách nhau hàng chục giây, file < 25 KB; không có debounce. **Serialize trên main thread, ghi file trên thread nền** (một worker, luôn ghi bản mới nhất, bỏ bản cũ chưa kịp ghi): 25 KB kèm fsync trên Android tầm thấp có thể vượt một frame, đúng lúc mở màn result. Lúc pause / quit ghi đồng bộ vì app có thể bị kill ngay sau đó.
- Tại mỗi persist: `playtime_s += now − foreground_anchor; foreground_anchor = now` → crash mất tối đa playtime từ trigger cuối.
- Có `schema_version`; SDK mới đọc file cũ bằng migration đơn giản (thêm field default).
- **File không parse được, hoặc `schema_version` không migrate được → xoá file, log `state_reset` (một event, kèm reason), coi state trống, `NeedsSeed = true`** để game seed lại (§4.6). Không cố đọc một phần; state nửa vời sinh segment sai khó truy hơn state mới.
- File chứa: `user_id` (GUID SDK sinh), state + bucket, offset, giá trị tag hiện tại kèm hash định nghĩa (hysteresis), edge state theo rule id kèm hash `on + when`, assignment + holdout, timestamp cap / cooldown theo action id, **`actions_shown_today` + ngày UTC của nó** (không persist thì cap ngày reset mỗi lần kill app), ring buffer `transaction_id`, last-touch `SHOW_OFFER`, cờ exposure đã log theo experiment id, config cache, **mirror của `action_history`**. State + bucket bao gồm cả counter `session`.
- **Reinstall / đổi máy → mất tầng này.** Tầng lịch sử action đi theo save của game nên còn. Nếu game có cloud save, sau restore game gọi `Seed()` (§4.6) để lấy lại phần lifetime và `ImportActionHistory()` nếu restore chạy sau init SDK. Không có → user coi như mới về state, nhưng one-shot đã dùng vẫn không lặp. Phase 2 thêm state-sync qua backend hiện có cho tầng này.

### 4.6 Seed state cho user hiện hữu

SDK vào game qua bản update, nghĩa là **toàn bộ user hiện hữu có state trống** đúng lúc MVP đo. Không seed: `install_at` = ngày update → mọi user là `new` rồi `active`; `total_spend_usd = 0` → whale thành `non_payer`, `potential_payer` nổ nhầm, `SHOW_OFFER` starter pack bắn vào người đã chi tiền.

```csharp
// Cách 1 (khuyến nghị): seed provider, SDK gọi TRƯỚC SESSION_START đầu tiên khi NeedsSeed == true.
SegmentationSdk.Initialize(new SdkOptions {
    SeedProvider = () => saveLoaded ? BuildSeedData() : null,   // null = chưa có dữ liệu, chờ Seed() gọi sau
});
// Cách 2: save của game load muộn, hoặc ngay sau restore cloud save → gọi khi có dữ liệu.
// SDK áp ngay, segment cập nhật ở trigger kế.
SegmentationSdk.Seed(new SeedData {
    InstallAt        = <ngày cài từ dữ liệu game>,      // bắt buộc nếu có
    LastActiveAt     = <lần mở app cuối trước bản update>,  // thiếu → user vắng 10 ngày thành `active` thay vì `returning`
    PurchaseCount    = <số lần mua lifetime>,
    TotalSpendUsd    = <tổng chi USD catalog>,
    FirstPurchaseAt  = ..., LastPurchaseAt = ...,
    ProgressMax      = <level / stage cao nhất>,
    SessionCount     = <nếu game có>,
});
```

- Áp khi `state.seeded == false`, **bất kể state đã trống hay chưa** (cloud restore trên máy đã chơi vài phiên vẫn phải seed được). Merge: `install_at` lấy giá trị khác 0 nhỏ hơn; counter, spend, `progress.max`, timestamp lấy `max`. Sau khi `seeded == true` các lần gọi sau bị bỏ. Field không truyền giữ default.
- **Thứ tự:** SDK expose `NeedsSeed` (state trống, hoặc vừa `state_reset` §4.5). `SESSION_START` đầu tiên không chờ seed (queue mở theo §4.4), nên seed đến muộn thì `lifecycle` / `payer_tier` của snapshot đầu có thể sai một session; chấp nhận, DWH tách bằng `seeded`.
- Restore purchase sau seed **không** emit `PURCHASE` (§4.1), nếu không `total_spend_usd` bị cộng đôi.
- Đặt `state.seeded = true`, `state.seeded_at = now`. `seg_snapshot` mang `seeded` để DWH tách cohort.
- Bucket window **không** seed (không có dữ liệu theo ngày). Hệ quả ở §4.3.
- Bước 0 phải xác nhận pilot lấy được những giá trị này từ đâu (PlayerPrefs, save local, backend).
- `Seed()` chỉ seed tầng vận hành. Logic "một lần" có sẵn của game chuyển sang engine thì seed tầng lịch sử bằng `MarkActionUsed()` (§4.5) cùng lúc, để user đã nhận không nhận lại.

### 4.7 Vòng xử lý một event và sandbox

Engine xử lý **tuần tự từ một queue** trên main thread. Event do executor emit trong lúc engine đang chạy (ví dụ `GIVE_REWARD` khiến game emit `CUSTOM_STATE`, hoặc `SHOW_POPUP` emit `SCREEN_OPEN`) được **nối vào cuối queue**, không đệ quy.

```
1. Reducer cập nhật state (PURCHASE trùng transaction_id → dừng ở đây)
2. Nếu event là trigger (§8.1):
   formulas → segments (topo) → rules (on, fire) → resolver → executor (ngay)
3. Tracking
4. Persist (nếu là trigger)
5. Lấy event kế tiếp trong queue
```

**Sandbox:** toàn bộ bước 1–4 nằm trong `try/catch`. Exception → log `engine_error` (một lần mỗi session, kèm stage), **tắt evaluate hết session** (reducer + persist vẫn chạy nếu exception không nằm trong reducer; nằm trong reducer thì bỏ event đó). Game không bao giờ thấy exception của engine. CLI và client cùng giới hạn AST: độ sâu ≤ 32, ≤ 500 node mỗi formula.

---

## 5. State Model

### 5.1 Namespace (chốt)

| Prefix | Ai ghi | Tham chiếu trong AST | Ví dụ |
|---|---|---|---|
| `state.` | Reducer / Seed | có | `state.fail_streak` |
| `feature.` | Formula engine | có | `feature.frustration_score` |
| `segment.` | Segment engine | có | `segment.frustrated` (bool), `segment.lifecycle` (string) |
| `context.` | SDK runtime | có | `context.screen`, `context.clock_suspect` |
| `custom.` | Game qua `CUSTOM_STATE` | có (key khai trong manifest) | `custom.hint_used` |
| `const.` | Config | có (phải có giá trị) | `const.playtime_7d_p90` |
| `exp.` | Assignment | **không** — chỉ xuất hiện trong tracking | `exp.gameplay_help = "booster_v1:control"` |

`exp.*` bị cấm trong AST để giữ bất biến "experiment chỉ đổi `then`" (§9.1); rule không được rẽ nhánh theo variant. **Lịch sử action (§4.5) cũng không có trong AST ở MVP:** one-shot là việc của resolver, không phải của điều kiện rule. Rule cần "user đã nhận X rồi thì…" để game ghi cờ qua `CUSTOM_STATE` (`custom.starter_pack_claimed`) và tham chiếu `custom.*`; mở `action.<id>.count` cho AST là việc Phase 2 nếu có rule thật cần. Window trong AST là field riêng trên node `ref`; trong tài liệu viết tắt `state.fail_count@7d`.

### 5.2 Primitive MVP (~30)

```
Lifecycle      state.install_at, state.days_since_install, state.session_count,
               state.days_since_last_active, state.last_active_at,
               state.seeded, state.seeded_at
Engagement     state.playtime_s (window), state.session_count (window)
Progression    state.progress.current, state.progress.max, state.attempt_count (window),
               state.complete_count (window), state.attempt_count_current_unit
Behavior       state.fail_count (window), state.fail_streak, state.win_streak,
               state.quit_count (window), state.quit_after_fail_count, state.last_fail_at
Monetization   state.purchase_count (window), state.total_spend_usd, state.first_purchase_at,
               state.last_purchase_at, state.ad_rewarded_count (window),
               state.ad_interstitial_count (window)
Custom event   state.custom_event_count.<name> (window) — theo whitelist manifest
Context        context.screen, context.last_event, context.session_time_s,
               context.actions_shown_today, context.clock_suspect, context.config_stale
```

`context.actions_shown_today` tăng khi resolver **chọn** action (`action_selected`) thuộc group khác `notification`, reset theo ngày UTC, **persist cùng ngày UTC** (§4.5). Đây là bộ đếm cho `max_actions_per_day` (§8.7).

### 5.3 Kiểu dữ liệu và giá trị mặc định

| Kiểu | Mặc định | Ghi chú |
|---|---|---|
| `NUMBER` | `0` | double |
| `BOOL` | `false` | dùng như số → 0 / 1 |
| `STRING` | `""` | enum = STRING với danh sách giá trị hợp lệ trong schema; chỉ so `EQ` / `NEQ` |
| `TIMESTAMP` | `0` | epoch giây. `0` = "chưa từng" → `TIME_SINCE` trả giá trị rất lớn, đúng nghĩa "chưa từng" cho mọi so sánh `≥ N` |

**Không có `null`.** Mọi ref luôn có giá trị. Không có `ARRAY` / `OBJECT`.

---

## 6. Formula và Feature

### 6.1 Dạng biểu diễn

AST JSON là **định dạng vận chuyển**: client **chỉ đọc AST**. File nguồn trong repo viết **string expression**, CLI `compile` ra AST lúc publish (§10.3, bắt buộc). Người soạn config không bao giờ viết JSON lồng; hai khối AST dưới là bản đã compile.

```json
{
  "id": "fail_rate",
  "expr": {
    "op": "DIV",
    "args": [
      { "ref": "state.fail_count", "window": "7d" },
      { "op": "MAX", "args": [ { "ref": "state.attempt_count", "window": "7d" }, { "value": 1 } ] }
    ]
  }
}
```

```json
{
  "id": "frustration_score",
  "expr": {
    "op": "ADD",
    "args": [
      { "op": "MUL", "args": [ { "ref": "feature.fail_rate" }, { "value": 0.5 } ] },
      { "op": "MUL", "args": [ { "ref": "feature.retry_rate" }, { "value": 0.3 } ] },
      { "op": "MUL", "args": [ { "ref": "feature.quit_after_fail_rate" }, { "value": 0.2 } ] }
    ]
  }
}
```

Dạng string trong file nguồn: `state.fail_count@7d / max(state.attempt_count@7d, 1)` và `feature.fail_rate * 0.5 + feature.retry_rate * 0.3 + feature.quit_after_fail_rate * 0.2`.

### 6.2 Operator MVP

| Nhóm | Op |
|---|---|
| Số học | `ADD SUB MUL DIV MOD` |
| So sánh | `GT GTE LT LTE EQ NEQ` |
| Logic | `AND OR NOT` |
| Điều kiện | `IF` (cond, then, else) |
| Tiện ích | `MIN MAX ABS CLAMP` |
| Thời gian | `NOW`, `TIME_SINCE(ts)` → giây, `DAYS_BETWEEN(a, b)` → ngày lịch UTC |

Không có `SUM AVG COUNT` — không có array để aggregate; window đã thay vai trò này. Không có `COALESCE` — không có `null` để thay.

### 6.3 Semantic (chốt)

- `NUMBER` là double. `EQ` với số dùng epsilon 1e-9.
- **Chia cho 0 → 0. `MOD` cho 0 → 0.** Không có `null`, không có ba trị.
- **Kiểu tĩnh, CLI kiểm tra toàn bộ AST:** số học và `GT/GTE/LT/LTE` chỉ trên NUMBER / TIMESTAMP; `AND/OR/NOT` chỉ trên BOOL; `IF` có cond BOOL và hai nhánh cùng kiểu; `EQ/NEQ` trên cùng kiểu; BOOL đứng chỗ NUMBER được hiểu là 0 / 1; STRING chỉ `EQ/NEQ` với `value` STRING. Sai kiểu → config invalid.
- **`ref` không có trong schema, `const.*` không có giá trị, `custom.*` không có trong manifest → config invalid.** Client validate lại lúc load; invalid → **từ chối toàn bộ config**, fallback theo §2.3, log `config_rejected`. Không có partial degradation — CLI đã chặn trước khi publish, client chỉ là lưới an toàn cho lệch version.
- **DAG phụ thuộc bao cả formula và segment.** Formula tham chiếu được formula khác và segment (tag → 0/1; partition chỉ `EQ/NEQ`); segment tham chiếu formula. CLI sort topo và cấm vòng. **Hysteresis không tạo vòng:** tag đọc giá trị *đã persist* của chính nó để chọn `enter` hay `exit`, không đọc giá trị của chu kỳ hiện tại.
- **Evaluate toàn bộ theo thứ tự topo tại mỗi trigger.** Không incremental. Với < 10 formula và < 15 rule, chi phí tính bằng micro giây.

### 6.4 `const.*` — hằng số population

Client không có thống kê population nên không có `normalize()`. Thay bằng hằng số **tính từ DWH lúc soạn config**, ghi thẳng vào file config, refresh mỗi lần publish có chủ đích:

```json
"const": { "playtime_7d_p50": 1800, "playtime_7d_p90": 7200, "spend_p90_usd": 9.99 }
```

Mọi `const.*` được tham chiếu **phải có giá trị** trong config (CLI reject nếu thiếu). Không có pipeline tự động DWH → config trong MVP. Phase 2 khi ≥ 3 game.

Feature tương đối: `feature.heavy_player = state.playtime_s@7d > const.playtime_7d_p90`. Lưu ý user mới 1 ngày luôn có `playtime_s@7d` thấp → segment kiểu này nên kèm `state.days_since_install >= 7`, nếu không sẽ trộn tenure với engagement.

---

## 7. Segment

### 7.1 Hai loại

| Loại | Giá trị | Dùng cho | Điều kiện |
|---|---|---|---|
| **Tag** | `true / false` độc lập | frustrated, potential_payer, heavy_player | `enter` (bắt buộc), `exit` (tuỳ chọn, mặc định = `NOT enter`) |
| **Partition** | đúng một `STRING` mỗi dimension | lifecycle, engagement_level, payer_tier | `cases` theo thứ tự, case đầu match thắng, có `default` |

`exit` riêng cho tag là **hysteresis**: `frustrated` enter khi score > 0.7, exit khi score < 0.5, tránh flapping quanh ngưỡng. Giá trị tag hiện tại persist **kèm hash của `enter + exit`**; hash đổi (config sửa định nghĩa) → reset về `false`. Partition không có state.

```json
{
  "id": "frustrated", "kind": "tag",
  "enter": { "op": "GT", "args": [ { "ref": "feature.frustration_score" }, { "value": 0.7 } ] },
  "exit":  { "op": "LT", "args": [ { "ref": "feature.frustration_score" }, { "value": 0.5 } ] }
}
```

```json
{
  "id": "lifecycle", "kind": "partition", "default": "active",
  "cases": [
    { "value": "new",       "when": { "op": "LT",  "args": [ { "op": "TIME_SINCE", "args": [ { "ref": "state.install_at" } ] }, { "value": 86400 } ] } },
    { "value": "returning", "when": { "op": "GTE", "args": [ { "ref": "state.days_since_last_active" }, { "value": 7 } ] } },
    { "value": "at_risk",   "when": { "op": "GTE", "args": [ { "ref": "state.days_since_last_active" }, { "value": 3 } ] } }
  ]
}
```

`new` dùng `TIME_SINCE` thay `days_since_install < 1` vì theo ngày lịch UTC, user cài lúc 23:59 chỉ "new" trong một phút.

Lưu ý semantic trên client: `at_risk` nghĩa là **vừa quay lại sau 3–6 ngày vắng**, `returning` là sau 7+ ngày. Đây là proxy hành vi, không phải dự đoán churn. `churned` không tồn tại trên client — user churned không mở app; `churned` thuộc server batch (Phase 2).

### 7.2 Segment set MVP

```
Partition  lifecycle        : new | active | at_risk | returning
           engagement_level : low | mid | high          (theo const.playtime_7d_p50 / p90, chỉ khi days_since_install >= 7)
           payer_tier       : non_payer | payer | high_value
Tag        frustrated       : frustration_score với hysteresis
           stuck            : attempt_count_current_unit >= N
           potential_payer  : non_payer AND ad_rewarded_count@7d >= N AND engagement_level != low
           ad_heavy         : ad_rewarded_count@7d > const.ad_rewarded_7d_p90
```

Game idle / tycoon thay `frustrated` / `stuck` bằng tag trên `custom_event_count.<name>@7d` (ví dụ `stalled: custom_event_count.currency_stall@session >= 3`). Muốn thử hai ngưỡng khác nhau cho một tag → khai hai tag (`frustrated_a`, `frustrated_b`) và hai rule, experiment chọn rule nào có action thật. Không có experiment trên ngưỡng trong MVP.

### 7.3 Ghi nhận segment ra ngoài

- Đánh giá lại toàn bộ segment sau mọi trigger. **Không còn `seg_changed`.** Thay bằng:
  - **User property** của tracking SDK cho từng partition (`seg_<partition_id>`), cập nhật khi giá trị đổi → mọi event tracking đều mang partition hiện tại, DWH cohort bằng một `GROUP BY`.
  - `seg_snapshot` tại `SESSION_START` (sau khi evaluate) chứa toàn bộ tag + partition + assignment + `config_version` + `config_source` + `sdk_version` + `seeded`, cộng `rules_matched` / `selected` / `dropped` của trigger `SESSION_START` (gộp từ `seg_decision`, §11.1).
  - `seg_decision` (§11.1) mang chuỗi segment tại mỗi trigger có match.
- Tag không lên user property (giới hạn slot); phân tích tag qua `seg_snapshot` và `seg_decision`.

---

## 8. Rule, Action, Resolver

### 8.1 Trigger event

Chỉ các event sau chạy engine (formula → segment → rule → resolver). Event khác chỉ cập nhật state.

```
SESSION_START, PROGRESS_COMPLETE, PROGRESS_FAIL, PROGRESS_QUIT, PURCHASE,
SCREEN_OPEN:<screen_id>, CUSTOM_EVENT:<name>
```

- Phần tử trong `on` là `EVENT` hoặc `EVENT:qualifier`. Qualifier của `SCREEN_OPEN` là `screen_id`, của `CUSTOM_EVENT` là `name`; CLI kiểm qualifier tồn tại trong manifest. `SCREEN_OPEN` không qualifier khớp mọi screen (hiếm khi cần).
- `SESSION_END`, `PROGRESS_START`, `AD_*`, `CUSTOM_STATE` không phải trigger.
- **Rule cần hiển thị ở màn nào thì trigger tại màn đó.** Ví dụ tặng booster sau 3 fail: trigger là `SCREEN_OPEN:result`, không phải `PROGRESS_FAIL`, vì `state.fail_streak` đã được cập nhật từ `PROGRESS_FAIL` trước đó và action thực thi ngay khi màn result mở. Đây là thay thế của pending queue (v0.3).

### 8.2 Rule schema

```json
{
  "id": "booster_when_frustrated",
  "enabled": true,
  "priority": 100,
  "on": [ "SCREEN_OPEN:result" ],
  "fire": "edge",
  "when": {
    "op": "AND",
    "args": [
      { "ref": "segment.frustrated" },
      { "op": "EQ",  "args": [ { "ref": "segment.lifecycle" }, { "value": "at_risk" } ] },
      { "op": "GTE", "args": [ { "ref": "state.fail_streak" }, { "value": 3 } ] }
    ]
  },
  "then": "none",
  "valid_until": "2026-12-31T00:00:00Z"
}
```

- `on`: rule chỉ được xét tại các trigger này. Bắt buộc, không có wildcard.
- `fire`:
  - `edge` (mặc định): fire khi `when` chuyển **false → true** giữa hai lần evaluate liên tiếp *tại trigger trong `on`*. Trạng thái `when` lần trước được persist theo rule id **kèm hash của `on + when`**; hash đổi → reset về `false`. Rule mới chưa có state → coi lần trước là `false`.
  - `level`: fire mỗi trigger khi `when = true`; cooldown của action giới hạn tần suất.
- `then`: **một `action_id` hoặc `"none"`.**
  - `"none"` = rule vẫn evaluate, vẫn xuất hiện trong `rules_matched` của `seg_decision` và vẫn tạo `exp_exposure`, **không tạo candidate**, không chiếm conflict group, không tính vào cap. Đây là trạng thái **mặc định của mọi rule mới** (shadow: đo fire rate trên prod trước khi cho action thật) và là **control** trong experiment.
  - Rule đã chứng minh hiệu quả → đổi `then` sang action thật ở base, gỡ experiment.
  - **Ngoại lệ: rule thay logic game đang chạy** (notification tự đặt, starter pack tự bật, difficulty knob sẵn có). Base `then` = action tái tạo **đúng hành vi cũ**, không phải `none`; code cũ gỡ trong cùng bản update. Logic cũ kiểu "một lần" (starter pack) → action `frequency: one_shot` (§8.3) và `MarkActionUsed()` cho user đã nhận (§4.5). Nhờ đó không có giai đoạn nào user mất thứ họ đang có, và shadow / A/A / control đều là "hành vi cũ", experiment chỉ override sang biến thể mới. Ship `none` rồi gỡ code cũ là tự tạo regression cho cả base trong nhiều tuần.
- `enabled: false` = **không evaluate**, không log. Chỉ dùng để tắt hẳn rule mà chưa xoá. Không dùng cho control.
- `valid_until` **bắt buộc** → không có rule sống vĩnh viễn mà không ai review. CLI cảnh báo rule đã hết hạn còn trong repo.
- Cùng `priority` → tie-break theo `id` tăng dần. Deterministic.

### 8.3 Action schema

```json
{
  "id": "give_booster_small",
  "type": "GIVE_REWARD",
  "params": { "reward_id": "booster_hammer", "amount": 1 },
  "group": "reward",
  "frequency": "repeatable",
  "cooldown_s": 86400,
  "cap": { "count": 2, "window_s": 604800 }
}
```

- `params` **tĩnh** trong MVP. Phase 2 cho phép AST trong params.
- `frequency`: `repeatable` (mặc định) hoặc `one_shot`.
  - `repeatable`: dùng lại được; tần suất do `cooldown_s`, `cap`, `fire` của rule và `max_actions_per_day` giới hạn.
  - `one_shot`: tối đa **một lần `selected` trong đời user**, bất kể rule nào chọn, bất kể reinstall / đổi máy. Resolver kiểm `action_history[action_id].n ≥ 1` (§4.5) và loại với `dropped reason=one_shot`. Khoá **tại `selected`**, hoàn lại khi `action_failed` vì `offline` / `no_permission`, giống cooldown; app chết giữa `selected` và `executed` → coi như đã dùng, chấp nhận mất. `cooldown_s` / `cap` thừa với one-shot, CLI cảnh báo nếu khai. Không dùng cho `SCHEDULE_LOCAL_NOTIFICATION` (đặt lại mỗi session; CLI báo lỗi).
  - Đổi `repeatable` → `one_shot` khi action đang chạy: user đã có `n ≥ 1` bị khoá ngay, không có "một lần nữa". Đổi ngược lại: mở ra bình thường. `action_id` là khoá của lịch sử, đổi id là thành action mới.
  - Trong experiment: control nhận `none` nên không tiêu one-shot; user treatment tiêu rồi sang holdback / base vẫn không nhận lại. User đã dùng one-shot **trước** experiment (qua `MarkActionUsed` hoặc rule khác) vẫn được exposed theo intent-to-treat nhưng không nhận gì; `exp_exposure.history_used` cho DWH tách nhóm này (§9.3).
- `group` là conflict group: `reward | difficulty | offer | message | notification`. Resolver lấy tối đa một action mỗi group mỗi trigger. Group `notification` chỉ dành cho `SCHEDULE_LOCAL_NOTIFICATION` (CLI kiểm) và **không tính vào `max_actions_per_day`**.
- `cooldown_s` và `cap` tính trên **danh sách timestamp các lần `action_selected`** của action đó (giữ tối đa `cap.count` mốc gần nhất) trong file SDK. `window_s` là **rolling theo giây**, không phải ngày lịch, không dùng bucket. Cooldown lấy thêm `action_history[action_id].l` (§4.5) làm **sàn**: reinstall mất file SDK thì action vẫn không nổ lại trong `cooldown_s` kể từ lần cuối theo lịch sử. Cap không có sàn này (chỉ giữ một `l`), reinstall mở lại cap; chấp nhận vì action repeatable có giá trị thấp.
- **Không có `deliver_on`, `ttl_s`, pending queue.** Action được đưa cho executor **ngay** sau resolver, trên main thread, theo thứ tự priority. Executor có thể trì hoãn hiển thị trong nội bộ (đợi animation kết thúc) nhưng phải báo kết quả trong session.
- **Không persist vòng đời action.** App chết giữa `selected` và `executed` → action mất, chấp nhận vì giá trị thấp; cooldown / cap / lịch sử đã ghi lúc `selected` nên không lặp.
- **Hoàn lại khi fail vì môi trường:** executor báo `action_failed` với reason `offline` hoặc `no_permission` → SDK gỡ timestamp vừa ghi, trừ `actions_shown_today` và trừ `action_history[action_id].n` (mở lại one-shot), để user không mất offer suốt một ngày, hay mất hẳn starter pack, chỉ vì rớt mạng. Reason khác (`exception`, lỗi game) giữ nguyên để không lặp vô hạn.
- Không có `offline_ok`. Action cần network (ví dụ `SHOW_OFFER` cần IAP sẵn) tự fail với `action_failed reason=offline`.
- Cooldown, cap, edge state: lưu trong file SDK (client-owned, chấp nhận tamper vì giá trị thấp). Lịch sử action và one-shot: lưu trong player data của game (§4.5), mức chống tamper bằng save của game.
- Một `action_id` được nhiều rule chọn trong cùng trigger → một candidate duy nhất, credit cho rule priority cao hơn.

### 8.4 Action type MVP (5)

| Type | Params | Ghi chú |
|---|---|---|
| `GIVE_REWARD` | `reward_id`, `amount` | Gộp booster / revive / energy / coin nhỏ. `reward_id` phải nằm trong **whitelist của capability manifest** |
| `SHOW_POPUP` | `popup_id`, `text_key?` | Gộp message / hint / recommend content. Nội dung do game localize |
| `SHOW_OFFER` | `offer_id` | Trỏ vào offer có sẵn trong shop; mua bán vẫn qua IAP + backend hiện có. SDK ghi last-touch để attribution (§8.7). Starter pack / offer lần đầu → `frequency: one_shot` (§8.3) |
| `CHANGE_DIFFICULTY` | `delta` (−2..+2), `scope`: `next_unit` | Game tự quyết cách áp. **Chỉ có nghĩa khi pilot có knob difficulty runtime** (bảng 1.1); không có → không đăng ký executor |
| `SCHEDULE_LOCAL_NOTIFICATION` | `template_id`, `delay_h` | Đặt tại `SESSION_START` với rule `fire: level`, `cooldown_s: 0`, group `notification`, luôn `repeatable` (CLI báo lỗi nếu `one_shot`). Executor **thay** lịch cũ cùng `template_id`. Chưa có permission → `action_failed reason=no_permission` (cooldown hoàn lại, §8.3). Game đã tự đặt notification → chuyển sang engine theo §8.2 "ngoại lệ": base `then` = template tương đương hành vi cũ, gỡ code cũ cùng bản update; **không có giai đoạn nào user mất notification** |

### 8.5 Capability manifest

Mỗi build game khai báo. **Chỉ dùng local**, không gửi lên server:

```json
{
  "sdk": "1.2.0",
  "actions": [ "GIVE_REWARD", "SHOW_POPUP", "SHOW_OFFER", "CHANGE_DIFFICULTY" ],
  "rewards": [ "booster_hammer", "revive", "coin_50" ],
  "screens": [ "home", "result", "shop" ],
  "custom_events": [ "offline_claim", "upgrade" ],
  "custom_state": { "hint_used": "NUMBER", "vip": "BOOL" }
}
```

- `actions` SDK **tự suy ra** từ danh sách executor game đã đăng ký; game chỉ khai `rewards`, `screens`, `custom_events`, `custom_state`.
- Server **không cần registry theo version**: client tự **disable rule** dùng action / reward / screen / custom event ngoài manifest và log `rule_unsupported`. Config vẫn hợp lệ với client khác. Muốn biết manifest nào đang ngoài kia → `sdk_version` trong `seg_snapshot` cộng app version đã có trong tracking.
- Bản manifest của mỗi game nằm trong repo config (`games/{game}/manifest.json`) để CLI validate `on`, `params.reward_id`, `custom.*` ref lúc soạn. **Do SDK export, không viết tay:** debug overlay (§11.3) có nút "Copy manifest JSON" lấy đúng executor đã đăng ký + `rewards` / `screens` / `custom_*` đã khai; dev paste vào repo mỗi bản build. Hết drift giữa repo và build.
- Lời hứa "không cần release" chỉ đúng **bên trong** manifest. Thêm action type, reward, screen, custom event hay custom key mới = release game. Ghi rõ điều này cho Product.
- **Trước khi đưa `reward_id` vào whitelist:** kiểm giá trị kinh tế. Reward cũng bán qua IAP thì local tamper (reset cooldown / cap) thành farming cannibalize doanh thu → không whitelist, hoặc cap rất chặt.

### 8.6 Executor contract

```csharp
public interface IActionExecutor {
    string ActionType { get; }                 // "GIVE_REWARD"
    void Execute(ActionRequest request);       // gọi trên main thread, ngay sau resolver
}
// request: ActionId, ExecutionId (8 hex), Type, Params, RuleId
// request.ReportPresented();  request.ReportExecuted(result);  request.ReportFailed(reason);
```

- Executor **phải** gọi một trong `ReportExecuted` / `ReportFailed` trong session. `action_selected` mà không có `action_executed` là bug executor, không phải hiệu quả rule (§11).
- `result` của `ReportExecuted` lấy từ **bộ giá trị cố định theo action type** (§13.3). `ReportPresented` gọi lúc bắt đầu hiển thị / áp; `ReportExecuted(result)` gọi khi user đã phản hồi xong (đóng popup, đóng offer), để `result` nói được user click, bỏ qua hay mua.
- Executor không cần idempotent, không cần persist gì. Grant đôi chỉ xảy ra nếu game tự retry.
- Executor ném exception → SDK bắt, log `action_failed reason=exception`, engine tiếp tục.

### 8.7 Resolver

```
candidates = rule match (đúng `on`, `when` theo fire mode, chưa hết valid_until, then ≠ none)
  → gộp trùng action_id (credit rule priority cao hơn)
  → bỏ action one_shot đã có trong action_history (n ≥ 1)          [dropped: one_shot]
  → bỏ action đang cooldown (kể cả sàn history.l) / vượt cap         [dropped: cooldown | cap]
  → sort priority desc, tie-break rule id
  → lấy action đầu tiên của mỗi group                                 [dropped: group]
  → group ≠ notification: cắt tại max_actions_per_day (context.actions_shown_today)   [dropped: daily_cap]
  → ghi timestamp selected (cooldown / cap), tăng actions_shown_today,
    ghi action_history[action_id] (n += 1, f, l) → store.Save() + mirror (§4.5)
  → giao executor theo thứ tự
  → action_failed reason ∈ { offline, no_permission } → hoàn lại timestamp + actions_shown_today + history.n (§8.3)
  → output: danh sách action (thường 0–2)
```

- Kiểm one-shot đứng **trước** cooldown / cap và trước chọn theo group: action one-shot đã dùng không được chiếm chỗ của action khác cùng group trong trigger đó.

- Mỗi action được chọn có `execution_id` (8 ký tự hex ngẫu nhiên, duy nhất theo user; DWH khoá bằng `(user, execution_id)`). `PURCHASE` sinh ra từ `SHOW_OFFER` mang `execution_id` để attribution.
- **Last-touch tự động:** SDK ghi `SHOW_OFFER` gần nhất đã `executed` (`execution_id`, `offer_id`, `at`). `PURCHASE` không mang `execution_id` → SDK gắn `last_offer_execution_id`, `last_offer_id`, `last_offer_age_s` nếu trong 7 ngày. DWH quyết định có tính attribution hay không theo offer / product khớp. Game **không phải** xuyên id qua shop UI; muốn chính xác hơn thì tự gắn `execution_id`.

### 8.8 Ranh giới security

- Client tự grant: chỉ `reward_id` trong whitelist, giá trị thấp, có cap. Bị hack thì thiệt hại là vài booster.
- Tiền thật, currency lớn, entitlement: **không có action type nào grant trực tiếp**. Đường duy nhất là `SHOW_OFFER` dẫn vào IAP / backend hiện có.
- Lịch sử action nằm trong player data của game (§4.5): sửa được ở mức sửa được save. `frequency: one_shot` **không** là lý do để whitelist reward giá trị cao; one-shot là ràng buộc trải nghiệm, không phải ràng buộc kinh tế. Xoá save để nhận lại starter pack là chuyện đã có sẵn ở game, engine không làm tệ hơn.
- Config đi qua HTTPS, client validate schema toàn bộ (§6.3). Chưa ký số trong MVP; nâng lên Ed25519 khi có action grant giá trị đáng kể.
- `/config` cho `env=prod` là public (config không chứa bí mật). `env` khác yêu cầu header `X-Config-Token` chỉ có trong dev build, để không lộ experiment sắp chạy.

---

## 9. Experiment

### 9.1 Mô hình

Experiment chỉ làm **một việc**: đổi `then` của một hoặc nhiều rule theo variant. Không patch formula, segment, action hay bất kỳ field nào khác. Engine chạy một config đã resolve; không có logic experiment trong rule hay resolver; AST không đọc được `exp.*`.

```json
{
  "id": "booster_when_frustrated_v1",
  "layer": "gameplay_help",
  "allocation": 1.0,
  "ends_at": "2026-11-30T00:00:00Z",
  "variants": [
    { "id": "control", "weight": 0.5, "rule_actions": {} },
    { "id": "booster", "weight": 0.5, "rule_actions": { "booster_when_frustrated": "give_booster_small" } }
  ]
}
```

- **Pattern rule mới:** ship ở base với `then: "none"`. Control giữ `none` (rule vẫn evaluate → có exposure). Treatment override sang action thật. User **ngoài allocation** cũng nhận `none` → không ai nhận rule mới mà không được đo. Experiment có kết quả dương và guardrail không vỡ → **không về base ngay**: publish lại cùng `experiment_id` với `weight` 0.9 / 0.1, control 10% giữ 4–8 tuần làm holdback (§13.5), rồi mới sửa `then` ở base và xoá experiment. Kết quả âm hoặc guardrail vỡ → về base `none` ngay.
- **`allocation` mặc định 1.0.** Mỗi layer chỉ có một experiment (bên dưới) nên user ngoài allocation không phục vụ gì: nhận `none` mà không phải control, tức là mẫu mất trắng, và `users_cần_chạm_trigger` (§9.3) tăng đúng bằng `1 / allocation`. Chỉ hạ allocation khi action có rủi ro thật cần giới hạn phạm vi; MVP không có action như vậy. CLI cảnh báo khi `allocation < 1.0`.
- **A/A trước A/B:** tuần đầu chạy experiment với **cả hai variant `rule_actions: {}`**. Không tiêu mẫu, nhưng kiểm được assignment cân, `exp_exposure` log đúng, user property lên đúng, và query readout chạy. Xong tuần A/A mới đổi variant treatment sang action thật (publish config mới, assignment giữ nguyên).
- **Assignment:** `hash(user_id, experiment_id) mod 10000`; trong `allocation` thì chọn variant theo `weight` trên dải đó. Kết quả lưu vào local state → sticky kể cả khi `allocation` / `weight` đổi. Persist ngay khi assign. `user_id` là GUID SDK sinh và persist cùng state (install id); có trong `seg_snapshot` để DWH tái tính assignment khi cần audit.
- **Layer:** MVP có 2 layer `gameplay_help`, `monetization`. **CLI ép mỗi layer chỉ có đúng một experiment chưa hết `ends_at`.** Nhờ đó không cần chia dải hash theo layer; hai experiment không bao giờ đụng một user hay một rule. Nhiều experiment mỗi layer → Phase 2.
- **`ends_at` bắt buộc.** Qua `ends_at`, experiment bị xoá khỏi config, hoặc variant id đã lưu không còn → user về base, assignment bị prune. CLI check `ends_at ≤ valid_until` của mọi rule trong `rule_actions`, và mọi `action_id` trong `rule_actions` tồn tại.
- **Mỗi experiment 2 nhánh** trong MVP. Muốn so 3 phương án → hai experiment nối tiếp. Lý do ở §9.3.

### 9.2 Global holdout

Holdout **không phải experiment**, là field cấp config:

```json
"holdout": { "allocation": 0.0 }
```

- `hash(user_id, "holdout") mod 10000 < allocation × 10000` → `exp.holdout = true`, sticky, persist.
- User holdout: reducer, formula, segment, `seg_snapshot` **vẫn chạy** để có dữ liệu so sánh; **không rule nào evaluate**, không assign vào experiment nào.
- **MVP để `allocation = 0.`** Holdout đo tổng lift của *hệ thống* theo cohort toàn bộ user; khi chỉ có một hai rule có action thật thì per-experiment control đã đo đúng thứ đó, còn 5% holdout là 5% mẫu mất đi đúng lúc mẫu là rủi ro số một (§9.3). Nâng lên 5% khi ≥ 3 rule có action thật chạy đồng thời ở base (thường là Phase 2).

**Thứ tự quyết định khi load config:** validate (§2.3) → holdout → từng layer.

### 9.3 Exposure, outcome, quy mô mẫu

- Tập rule của experiment = hợp các key trong `rule_actions` của mọi variant. `exp_exposure` log **lần đầu** một rule trong tập đó có `when = true` tại trigger, cho **cả control**. Không log lúc assignment. Cờ "đã log" persist theo experiment id.
- **Exposure là intent-to-treat.** Treatment user được tính exposed dù action sau đó bị resolver loại (one-shot, cooldown, cap, group, cap ngày) hay executor fail. Đây là readout chính. Readout per-protocol (chỉ user có `action_executed`) dùng để chẩn đoán, không dùng để kết luận. Riêng action `one_shot`: `exp_exposure.history_used = true` nếu tại lúc exposure user đã có action đó trong lịch sử (§4.5). Cờ này tính được cho **cả control** vì lịch sử không phụ thuộc assignment, nên lọc `history_used = false` ở cả hai nhánh vẫn là intent-to-treat trên nhóm đủ điều kiện; dùng khi experiment trên action đã có user nhận trước đó qua `MarkActionUsed`.
- **Outcome window tính từ lần exposure đầu:** D1 / D7 / D14 = có session trong ngày thứ 1 / 7 / 14 **sau ngày exposure đầu**. Không dùng D7 theo install, vì rule nổ ở nhiều tenure khác nhau. Metric phụ: session/user, playtime, IAP revenue / user, **ad revenue / user** (tracking hiện có đã đếm ở cấp user), tổng revenue / user trong 14 ngày sau exposure. **Guardrail** khai trước theo group của action (§13.4); guardrail vỡ thì không ship dù primary thắng. Attribution action → purchase: 7 ngày, nối bằng `execution_id` hoặc last-touch (§8.7).
- Readout dùng pipeline A/B hiện có; nếu chưa có, một query cohort theo `exp_exposure` join user property là đủ cho MVP. **Query này viết và chạy thử trên dữ liệu A/A trước khi bật experiment thật** (§12.1 bước 8).

| Baseline D7 (sau exposure) | Lift tương đối muốn thấy | User exposed mỗi nhánh (α 0.05, power 0.8) |
|---|---|---|
| 15% | +10% (→ 16.5%) | ~9 200 |
| 15% | +20% (→ 18%) | ~2 400 |
| 25% | +10% (→ 27.5%) | ~4 900 |
| 15% | +5% (→ 15.75%) | ~36 000 |
| 25% | +5% (→ 26.25%) | ~19 000 |

**Lift +10–20% là mức lạc quan.** Can thiệp in-game kiểu tặng booster, nới độ khó, gợi ý offer thường cho +2–5% tương đối trên retention; ở +5% mẫu cần gấp bốn dòng đầu. **Effect size để tính mẫu lấy từ A/B hardcode ở bước 0(f)**, không giả định.

Bảng trên tính theo **user exposed**, không phải DAU. User phải **chạm điều kiện `when`** mới thành exposed:

```
users_cần_chạm_trigger = exposed_mỗi_nhánh × số_nhánh / allocation
ví dụ: 9 200 × 2 / 1.0 = 18 400   (allocation 0.5: 36 800; 3 nhánh: 27 600)
```

**Bước 0 (§12.1)**, trước khi code engine, Data ước lượng tỉ lệ user chạm điều kiện của 2–3 rule ứng viên từ event hiện có trên DWH (ví dụ: bao nhiêu % user tuần có ≥ 3 fail liên tiếp rồi mở màn result), và A/B hardcode 0(f) cho effect size. Điều kiện quá hẹp → nới điều kiện, chọn rule khác, hoặc chọn game khác (allocation đã là 1.0, không còn nút này để vặn). Đây là lý do chọn game DAU lớn nhất và chỉ 2 nhánh.

**Chốt ngày readout trước khi bật treatment**, ghi vào PR của config version có action thật cùng primary metric, MDE, guardrail và quyết định nếu thắng / thua (pre-registration, §13.4): ví dụ 6 tuần sau publish, cộng 7 ngày để D7 của exposure cuối chín. Không đọc kết quả giữa chừng để quyết định dừng hay tiếp; đọc hàng tuần và dừng khi "thấy có ý nghĩa" thổi false positive lên gấp nhiều lần α. Muốn dừng sớm thì dùng sequential test (mSPRT hoặc alpha-spending) khai báo trước; MVP chỉ cần một mốc cố định. Tuần A/A là lúc kiểm SRM (sample ratio mismatch) giữa hai nhánh.

---

## 10. Config Delivery

### 10.1 Cấu trúc

Worker trả một **envelope** gồm `server_time` và **một** config phẳng:

```json
{
  "server_time": 1759287600,
  "config": { "...": "config version 42" }
}
```

Config:

```json
{
  "schema_version": 3,
  "min_sdk_version": "1.0.0",
  "game_id": "puzzle_x",
  "env": "prod",
  "version": 42,
  "published_at": "2026-10-01T03:00:00Z",
  "enabled": true,
  "session_timeout_s": 1800,
  "max_actions_per_day": 3,
  "holdout": { "allocation": 0.0 },
  "const": { },
  "formulas": [ ],
  "segments": [ ],
  "actions": [ ],
  "rules": [ ],
  "experiments": [ ]
}
```

- `enabled: false` = kill-switch toàn hệ thống: reducer và persist vẫn chạy, không rule nào evaluate.
- **Không có canary channel trong MVP.** Từng lớp lỗi đã có lưới riêng: sai cú pháp / ref / kiểu → CLI `validate` chặn trước publish; sai nghiệp vụ → `then: "none"` + experiment; exception → sandbox; client từ chối config (lệch `min_sdk_version`, schema) → tự dùng cache, tức rollback tự động với blast radius bằng 0. Với một game, canary không còn việc để làm mà kéo theo hash rollout, fallback hai tầng, `config_channel` trong tracking, lệnh `promote`. Rollback = `rollback --to <version>` publish lại nội dung bản cũ từ `history` (§10.3). Canary + rollout theo % → Phase 2 khi ≥ 3 game và config đổi hàng tuần.
- `version` tăng đơn điệu; `rollback` cấp version **mới** với nội dung bản cũ (v44 = nội dung v41) để dashboard không thấy version lùi. `min_sdk_version` cao hơn SDK → config bị từ chối, fallback cache theo §2.3.
- `session_timeout_s`: ranh giới session (§4.4), khớp tracking SDK.
- `max_actions_per_day`: chặn spam khi nhiều rule cùng nổ; conflict group một mình cho phép tới 4 action mỗi trigger (không tính `notification`).
- Một config **phẳng** cho mỗi `game_id / env`. Kế thừa portfolio base → game override làm **ở CLI lúc publish**; client không biết inheritance.

### 10.2 Delivery

- `GET /config?game=&env=`. Worker đọc **một** KV key `config/{game}/{env}/envelope` (publish ghi sẵn toàn bộ body trừ `server_time`), đặt `server_time` lúc trả. **Không edge cache, không ETag**: mỗi request là một Worker invocation + một KV read (KV đã cache tại edge ≥ 60 giây, đó cũng là độ trễ tối đa của kill-switch), đổi lại `server_time` luôn tươi và không có 304 phải xử lý. Chi phí: ở 1M DAU × 3 session, ba key là ~270M KV read/tháng, cỡ trăm đô/tháng chứ không phải "vài đô"; một key còn một phần ba. Cloudflare không tính egress; envelope < 100 KB.
- `env ≠ prod` yêu cầu header `X-Config-Token` (§8.8).
- Client fetch tại **Initialize** (timeout 3 giây, **queue chỉ mở sau khi fetch xong hoặc timeout**, nên `SESSION_START` luôn evaluate trên config và offset mới nhất có thể) và tại **Resume** nếu lần fetch cuối > 5 phút. Config mới áp dụng **từ trigger kế tiếp**. Edge state, assignment sống qua lần đổi config (rule / experiment bị xoá xử lý theo §8.2, §9.1).
- Cache persistent: config cuối cùng đã chấp nhận. Fetch fail → dùng cache. Cache quá 7 ngày → vẫn dùng (stale ok), đánh `context.config_stale = true`.
- `seg_snapshot.config_source ∈ { fresh, cache, none }`.

### 10.3 Authoring và publish (không có UI)

```
Git repo `segmentation-config/`
  base/            formulas, segments, actions dùng chung toàn portfolio (và theo genre)
  games/{game}/    override + rules + experiments + manifest.json (SDK export, §8.5) + fixtures/
  → PR review → CI: validate + simulate → merge → publish CLI → KV
```

CLI dùng **chính engine code** của SDK (thư viện C# thuần, không dính Unity API) để:

- `validate`: schema; ref tồn tại; **kiểu tĩnh toàn AST**; `const.*` có giá trị; vòng phụ thuộc trên formula ∪ segment; độ sâu / số node AST; action / reward / screen / custom event / custom key tồn tại trong `manifest.json` của game; qualifier trong `on` tồn tại; **không có `exp.*` trong AST**; group `notification` chỉ cho `SCHEDULE_LOCAL_NOTIFICATION`; **mỗi layer ≤ 1 experiment active; `ends_at` ≤ `valid_until` của rule bị override; `rule_actions` trỏ vào rule và action tồn tại**; mọi rule có `valid_until`; cảnh báo rule đã hết hạn. **Thêm ở vòng 2:** rule có `SESSION_START` trong `on` chỉ được `then` trỏ action group `notification` (UI game chưa sẵn lúc đó; là lỗi, không phải cảnh báo); cảnh báo `allocation < 1.0`. **Thêm ở vòng 4:** `frequency` ∈ { `repeatable`, `one_shot` }; `one_shot` trên `SCHEDULE_LOCAL_NOTIFICATION` là lỗi; `one_shot` kèm `cooldown_s > 0` hoặc `cap` là cảnh báo (thừa); đổi `frequency` của action đang có rule live → cảnh báo trong PR kèm ghi chú "user đã có `n ≥ 1` bị khoá ngay".
- `compile` (**bắt buộc**, nằm trong bước 6): file nguồn viết `"expr": "state.fail_count@7d / max(state.attempt_count@7d, 1)"`, CLI compile ra AST trước khi validate / publish. Client không bao giờ thấy string. Parser cho ~20 operator, `@window` và `ref` là 1–2 ngày (Pratt parser hoặc recursive descent); đổi lại fixture đọc được, PR review được, và người không phải dev soạn được rule. Không có bước này thì **tiêu chí "Product tự publish" (§12.4) không thực tế** và mọi rule vẫn phải qua dev.
- `simulate --state fixture.json --event "SCREEN_OPEN:result" [--variant exp:variant]`: in trace features / segments / rules matched / actions. Fixture nằm cạnh config, chạy trong CI như test.
- `replay --events sample.jsonl [--variant exp:variant]` (tuỳ chọn, chỉ khả thi sau khi bước 7 chốt event map): chạy chuỗi event chuẩn của N user (export từ DWH, map qua đúng bảng event map của game) qua engine, in fire rate từng rule, phân bố segment cuối, số action bị drop theo reason. Là bản có hệ thống của bước 0(b): đo exposure rate chính xác và bắt lỗi rule trên dữ liệu thật trước publish, không tiêu mẫu. Engine là C# thuần nên phần thêm chỉ là adapter đọc jsonl.
- `publish --game --env`: compile → validate → ghi một KV key `config/{game}/{env}/envelope`, tăng version, lưu bản đã publish ở `config/{game}/{env}/history/{version}`. `rollback --game --env --to <version>`: publish lại nội dung bản cũ dưới version mới. Không có `promote` vì không có canary (§10.1).

Config Editor UI để Phase 2, khi đã có ≥ 3 game và Product thực sự sửa config hàng tuần.

---

## 11. Tracking và Observability

Toàn bộ đi qua SDK tracking hiện có. Nguyên tắc gói: **ít event, mỗi event đủ để phân tích một mình**; partition và assignment lên **user property** để mọi event khác của game cũng cohort được.

### 11.1 Event mới

| Event | Khi nào | Field chính |
|---|---|---|
| `seg_snapshot` | `SESSION_START`, sau khi evaluate | `user_id` (SDK GUID), `config_version`, `config_source`, `sdk_version`, `seeded`, `holdout`, mỗi partition một param, `tags` (một chuỗi `a,b,c` các tag đang true), `exps` (một chuỗi `exp:variant;...`), **cộng `rules_matched`, `selected`, `dropped` của chính trigger `SESSION_START`** (gộp từ `seg_decision`; rule `level` tại `SESSION_START` làm hai event này luôn đi đôi, gộp bớt một event mỗi session) |
| `seg_decision` | mỗi trigger **khác `SESSION_START`** có **≥ 1 rule match hoặc ≥ 1 action**; cộng **5% sample** các trigger còn lại với `sampled = true` | `trigger`, `rules_matched` (chuỗi id, kể cả `then: none`), `selected` (chuỗi `action_id:execution_id`), `dropped` (chuỗi `action_id:reason` — one_shot / cooldown / cap / group / daily_cap), `segments` (chuỗi), `features` (chuỗi, làm tròn 2 chữ số), `config_version`, `eval_ms` (số nguyên ms từ lúc nhận trigger đến khi giao xong executor; §13.2) |
| `action_selected` | resolver chọn | `action_id`, `execution_id`, `rule_id`, `group`, `nth` (lần thứ mấy user nhận action này theo `action_history`, one-shot luôn là 1) |
| `action_presented` | executor bắt đầu hiển thị / áp | `execution_id` |
| `action_executed` | executor báo thành công, khi user đã phản hồi xong | `execution_id`, `result` (bộ giá trị cố định theo action type, §13.3) |
| `action_failed` | executor báo lỗi hoặc ném exception | `execution_id`, `reason` (`offline`, `no_permission`, `exception`, …) |
| `exp_exposure` | lần đầu rule trong experiment có `when = true` | `exp_id`, `variant`, `rule_id`, `history_used` (true nếu action `one_shot` mà experiment trỏ tới đã có trong lịch sử; §9.3) |
| `config_rejected` | client từ chối config | `version`, `reason` |
| `rule_unsupported` | load config | `rule_id`, `reason` |
| `engine_error` | một lần mỗi session | `stage`, `message` (cắt 100) |
| `state_reset` | file state không đọc được hoặc không migrate được; blob lịch sử action không parse được (§4.5) | `reason` (`parse` / `schema` / `history`) |

- `seg_decision` thay cho cả `rule_fired` (v0.3, một event mỗi rule) và `decision_trace`. `rules_matched` với rule `then: none` là dữ liệu shadow: fire rate thật của rule trước khi bật action.
- `action_selected` và `action_executed` **phải tách**: lệch nhau là bug executor, không phải hiệu quả rule.
- **Giới hạn nếu tracking SDK là Firebase:** tên event ≤ 40 ký tự, ≤ 25 param mỗi event, giá trị string ≤ 100 ký tự, ≤ 25 user property mỗi project, giá trị user property ≤ 36 ký tự. Vì thế `execution_id` là 8 hex (không phải UUID 36 ký tự), các danh sách gói thành một chuỗi cắt ở 100 ký tự, ưu tiên giữ `rules_matched` và `selected`. Dev build log 100%, không cắt, ra console.
- **Ngân sách volume (tính ở bước 0):** `event_thêm/ngày ≈ DAU × trigger_ngoài_SESSION_START/user/ngày × P(≥ 1 rule match) + 5% × phần còn lại + DAU × session/user (snapshot, đã gộp decision của SESSION_START)`. Mục tiêu ≤ 20% số event hiện tại của game. Firebase property standard có **trần export BigQuery daily 1M event/ngày**; vượt → bật streaming export hoặc hạ sample. Rule `fire: level` với `then: none` nổ mỗi trigger → cân nhắc khi ước lượng.

### 11.2 User property

| Property | Giá trị | Cập nhật |
|---|---|---|
| `seg_<partition_id>` (mỗi partition một slot, ví dụ `seg_lifecycle`, `seg_engagement`, `seg_payer`) | giá trị partition hiện tại | khi đổi |
| `exp_<layer>` (`exp_gameplay_help`, `exp_monetization`) | `<exp_id>:<variant>` hoặc rỗng | khi assign / prune |
| `seg_holdout` | `1` / rỗng | khi assign |

Ngân sách ≤ 6 slot. Trần 25 user property của Firebase là **chung cho cả game trong project**, không phải riêng engine; bảng 1.1 đếm slot còn trống thật, kể cả slot game và AppsFlyer đang dùng. Nhờ user property, readout experiment là `GROUP BY exp_<layer>` trên bảng event hiện có, không phải tái dựng membership từ `seg_snapshot`.

### 11.3 Debug overlay

Trong dev build: xem state, feature, segment, rule matched, action, config version / source, envelope thô; **ép** được giá trị `state.*` / `custom.*`, tag, partition và assignment để QA test từng nhánh; nút "chạy trigger X" để mô phỏng, kèm `eval_ms` của trigger vừa chạy; nút **"Copy manifest JSON"** xuất manifest thật của build cho repo config (§8.5); xem **lịch sử action** (blob từ player data và mirror, kèm "history store: none" nếu game chưa gắn), xoá một entry để QA lại one-shot. Đây là công cụ QA chính, không cần dashboard riêng.

---

## 12. Phase và định nghĩa MVP

### 12.1 Phase 1 — MVP (3–4 tháng tới live, 2 engineer + 1 data)

| Bước | Deliverable | Ước lượng |
|---|---|---|
| **0** | **Data + 1 dev game, song song với bước 1–4:** (a) điền bảng 1.1; (b) ước lượng exposure rate của 2–3 rule ứng viên từ event hiện có trên DWH, tính `users_cần_chạm_trigger` (§9.3); (c) inventory logic sẵn có của pilot trùng action type; (d) chốt event map theo genre (`PROGRESS_*` hay `CUSTOM_EVENT`); (e) ước lượng ngân sách volume tracking; **(f) bắt buộc:** hardcode rule ứng viên số một vào pilot với Remote Config A/B hoặc hash đơn giản, chạy 2 tuần. Đầu ra của (f): effect size thật thay vào bảng §9.3, query readout đã chạy trên dữ liệu thật, và câu trả lời "in-session personalization có nhúc nhích được retention của game này không" trước khi tiêu 3 tháng dev. Kết quả âm không tự huỷ engine (chỉ huỷ rule đó) nhưng phải tìm được rule khác có exposure × effect size đủ mẫu trước khi qua bước 5–7. (b) + (f) → kết luận pilot có đủ mẫu trong 6–8 tuần không | 3–4 tuần |
| 1 | Event queue + schema + reducer + bucket ngày + seed + persist atomic hai tầng (`IActionHistoryStore` + mirror + merge, §4.5) + sandbox (C# thuần, test vector JSON) | 2 tuần |
| 2 | Formula engine AST không null + type-check tĩnh + giới hạn AST | 1 tuần |
| 3 | Segment (tag / partition / hysteresis có hash) + rule (on qualifier / fire / none) + resolver (one-shot theo lịch sử, group, cap timestamp, cap ngày, last-touch) | 1–1.5 tuần |
| 4 | Channel select + holdout + experiment `rule_actions` + assignment sticky + `ends_at` / prune | 1 tuần |
| 5 | Unity SDK wrapper: session boundary, thời gian, executor registry, manifest tự suy, tracking events + user properties, debug overlay có override | 2 tuần |
| 6 | Cloudflare `/config` + KV 1 key `envelope` + `history` + token env + CLI compile / validate / simulate / publish / rollback + CI | 2 tuần |
| 7 | Tích hợp game pilot: map event, `Seed()` (kèm `LastActiveAt`, không emit `PURCHASE` từ restore), **`IActionHistoryStore` gắn vào save của game + `ImportActionHistory()` sau restore + `MarkActionUsed()` cho logic "một lần" đã có** (§4.5), 3–4 executor, export manifest, kiểm giá trị kinh tế reward, chuyển logic trùng sang engine theo §8.2 "ngoại lệ". **Cần dev-tuần của team game** (map event, executor, QA từng nhánh qua debug overlay), ghi vào bảng 1.1, không tính vào 2 engineer của dự án; đây là chỗ hay trượt gấp đôi | 2–3 tuần + giờ team game |
| 8 | Config đầu tiên: ≤ 5 formula, ≤ 6 segment, ≤ 6 rule (`then: none`, trừ rule thay logic sẵn có), 1 experiment **A/A** với `allocation` 1.0, holdout 0; **pre-registration trong PR: primary metric, MDE, guardrail, ngày readout (§13.4)**; dashboard engine health trên BI hiện có (§13.2, Data 1–2 ngày); query readout đã chạy từ bước 0(f) chỉ cần trỏ sang `exp_exposure` và thêm guardrail; `replay` trên event thật nếu kịp | 1 tuần |
| 9 | Live: publish version 1 cho 100% (không có canary; kill-switch `enabled` + cache fallback là lưới); 1 tuần shadow + A/A theo checklist §13.6 (SRM, exposure, user property, **kiểm định segment**); đổi treatment sang action thật; đọc readout **tại ngày đã chốt**; thắng → holdback 0.9 / 0.1 thêm 4–8 tuần (§13.5) rồi về base | 6–8 tuần |

Bước 0 và 1–4 và 6 chạy song song được. Readout của engine vào khoảng tháng thứ 5 kể từ khi bắt đầu; bước 0(f) cho readout thô của rule số một vào tháng thứ 2, và đó là mốc quyết định có đi tiếp bước 5–7 hay không.

### 12.2 Phase 2 (sau readout MVP)

State-sync qua backend hiện có cho tầng vận hành SDK (tầng lịch sử action đã theo save của game từ MVP, §4.5); `action.<id>.count` trong AST nếu có rule thật cần; server-side out-of-session decision (push, comeback) chạy batch trên DWH; `params` dạng AST; nhiều experiment mỗi layer (chia dải hash theo layer); experiment trên ngưỡng segment; holdout 5% khi ≥ 3 rule live; CUPED (covariate hoạt động trước exposure) để giảm 20–40% mẫu và sequential test khai trước, nếu pipeline A/B hỗ trợ (§13.7); **canary channel + rollout theo %** khi ≥ 3 game và config đổi hàng tuần; window `1d` theo giờ địa phương hoặc theo giờ nếu có rule cần; pipeline `const.*` tự động; action trì hoãn (`deliver_on`) **chỉ nếu** có rule không viết được bằng trigger tại màn; Config Editor UI; ký số config; mở rộng 3–5 game với template theo genre.

### 12.3 Phase 3

Churn / payer prediction rồi uplift modeling, chỉ khi đã có ≥ 6 tháng log `exp_exposure` + outcome. ML là thêm một nguồn candidate / score cho resolver, không thay kiến trúc.

### 12.4 Tiêu chí đi tiếp / dừng

- **Đi tiếp Phase 2** khi: ≥ 1 rule có lift D7-sau-exposure ý nghĩa thống kê **và không vỡ guardrail (§13.4)** trong game pilot, **và** Product / Data đã tự publish ≥ 3 lần config không cần release game, **và** rule thứ hai (khác rule đã A/B hardcode ở bước 0(f)) đi từ ý tưởng đến live trong ≤ 1 tuần không release. Hai tiêu chí đầu đo *rule*, hardcode + Remote Config cũng đạt được; tiêu chí thứ ba đo *engine*, thứ Phase 2 thật sự đặt cược vào.
- **Dừng và review** khi: bước 0 kết luận pilot không đủ mẫu và không có game thay thế; hoặc 0(f) không tìm được rule nào có exposure × effect size đủ mẫu; hoặc sau 3 tháng live không experiment nào tách được khỏi noise tại ngày readout đã chốt; hoặc tích hợp game pilot vượt 2× ước lượng (kể cả giờ team game).

### 12.5 Câu hỏi mở còn lại (trả lời từ bảng 1.1 và bước 0)

1. `user_id` là install id hay account id → assignment có sống qua reinstall không. Kèm **reinstall rate** của game pilot.
2. Tracking SDK hiện tại có giới hạn số field / độ dài string / số user property không → cách gói `seg_decision`, còn bao nhiêu slot user property.
3. Game pilot là game nào, genre gì, DAU bao nhiêu, **tỉ lệ user chạm điều kiện của rule ứng viên** → kiểm tra §9.3.
4. Pilot lấy được dữ liệu seed (ngày cài, tổng chi, level max) từ đâu?
5. Pilot đang tự làm gì trùng với 5 action type (notification, offer, difficulty)? Chuyển sang engine theo §8.2 "ngoại lệ": base `then` tái tạo hành vi cũ, gỡ code cũ cùng bản update.
6. Portfolio có engine ngoài Unity không → có cần SDK thứ hai ngay không.
7. Team game pilot cam kết bao nhiêu dev-tuần cho bước 7, ai viết executor và map event?
8. Bước 0(f): rule ứng viên số một là gì, đo bằng Remote Config A/B hay hash trong game, ngày bắt đầu và ngày readout?
9. Save system của pilot: format, có cloud backup / restore không, flush khi nào, restore chạy trước hay sau init SDK → cách gắn `IActionHistoryStore` (§4.5). Pilot có logic "một lần" nào (starter pack, quà tân thủ) cần `MarkActionUsed()` khi migrate?

---

## 13. Measurement & Evaluation

Mục này gom về một chỗ câu trả lời cho "hệ thống có hiệu quả không, đo bằng gì, và ai đọc số nào". Phần lớn cơ chế đã có ở §9 (control / treatment, exposure, cỡ mẫu), §11 (event) và §12.4 (tiêu chí); §13 chỉ thêm những gì còn thiếu: kiểm định segment, engine health khi live, `result` theo action type, guardrail và pre-registration, holdback sau khi thắng, checklist trước khi bật treatment.

**Không phải một thành phần hệ thống mới.** Mọi phép đo chạy trên DWH bằng event và user property ở §11, qua pipeline A/B và BI hiện có. Không có endpoint, service hay SDK mới. Thứ duy nhất phải dựng là một dashboard engine health (§13.2) và một query readout (§9.3), cả hai do Data làm trong bước 8.

### 13.1 Ba tầng câu hỏi

| Tầng | Câu hỏi | Đo bằng | Dữ liệu | Khi nào |
|---|---|---|---|---|
| **Segment** | Segment có mô tả đúng user không? `at_risk` có churn nhiều hơn `active` không? `frustrated` có quit nhiều hơn không? | Outcome theo partition / tag, **không cần control**: `GROUP BY seg_lifecycle` trên D7; quit rate session kế tiếp theo tag `frustrated`; revenue theo `seg_payer` đối chiếu DWH | `seg_snapshot`, user property `seg_*` | Tuần shadow, trước khi bật action. Không tốn mẫu |
| **Rule** | Action này có làm user tốt hơn user giống hệt nhưng không nhận action không? | Per-experiment control (§9.1), intent-to-treat, outcome từ exposure đầu (§9.3), guardrail (§13.4) | `exp_exposure`, user property `exp_<layer>`, event outcome hiện có | Tại ngày readout đã chốt |
| **Engine** | Có đáng xây engine thay vì hardcode + Remote Config không? | MVP: tổng các readout rule **cộng** ba tiêu chí §12.4 (lift, số lần publish, thời gian rule thứ hai ra live). Phase 2: global holdout 5% (§9.2) đo tổng lift khi ≥ 3 rule live và bắt đầu cannibalize nhau | §12.4; sau này `seg_holdout` | Cuối MVP; holdout từ Phase 2 |

**Thứ tự đọc khi một rule không lift: kiểm tầng segment trước.** Segment sai thì action đúng cũng vô nghĩa, và sửa formula rẻ hơn thử action khác. Tiêu chí tối thiểu cho partition: outcome tách **đơn điệu** theo thứ tự giá trị (D7 của `new` > `active` > `at_risk` > `churned`, hoặc ngược lại đúng nghĩa của partition); không tách → sửa formula / ngưỡng trước khi bật treatment. Đây là lý do tuần shadow phải có, không chỉ để đo fire rate.

**Vì sao system-level không phải KPI cuối cùng của MVP.** Với 1 đến 2 rule live, per-experiment control đo đúng tổng lift của hệ thống; holdout lúc này chỉ lấy đi 5% mẫu (§9.2). Giá trị riêng của engine so với hardcode là tốc độ lặp và tái dùng (§1.2), nên tiêu chí thứ ba của §12.4 mới là phép đo engine. Holdout trở thành cần thiết khi nhiều rule chạy cùng lúc ở base, vì tổng lift từng rule không bằng lift tổng: booster tặng miễn phí giảm mua booster, offer đẩy IAP giảm xem ad.

### 13.2 Engine health khi live

Dashboard trên BI hiện có, Data dựng ở bước 8 (1–2 ngày). Xem **hàng ngày trong 2 tuần đầu sau mỗi publish**, sau đó hàng tuần. Mọi metric tính từ event §11.1; field mới duy nhất là `eval_ms`.

| Metric | Cách tính | Ngưỡng | Vượt ngưỡng thì |
|---|---|---|---|
| Config adoption | % `seg_snapshot` có `config_version` = version mới nhất, 24 h sau publish | ≥ 90% | Fetch lỗi hoặc client kẹt cache; kiểm Worker / KV |
| Config rejected | `config_rejected` / `seg_snapshot` theo version | = 0 ở prod | `rollback` ngay; tìm lỗi CLI validate lọt |
| Rule unsupported | `rule_unsupported` theo `sdk_version` × `rule_id` | chỉ xuất hiện ở build cũ | Rule dùng thứ ngoài manifest của build đang live |
| Engine error | session có `engine_error` / tổng session | < 0.1% | Sandbox đã tắt engine cả session cho user đó; đọc `stage`, `message` |
| State reset | `state_reset` / session | < 0.1% | Persist hỏng; kiểm migrate schema |
| Seeded | % `seg_snapshot` có `seeded = true` sau 7 ngày live | ≥ 95% | `Seed()` không được gọi hoặc provider thiếu dữ liệu; segment monetization / lifecycle sai cho user cũ |
| Fire rate mỗi rule | trigger có rule trong `rules_matched` / tổng trigger cùng `on` (nhân ngược 5% qua `sampled`) | lệch ≤ 30% so với ước lượng bước 0 hoặc `replay` | Rule viết sai hoặc số bước 0 sai; **tính lại cỡ mẫu**, không dời ngày readout |
| Execution ratio | `action_executed` / `action_selected` | ≥ 95% | Bug executor, không phải hiệu quả rule (§8.6). Xem `action_failed` theo `reason`; `exception` > 1% là bug game |
| Drop rate theo reason | `dropped` / (`selected` + `dropped`), tách `cooldown` / `cap` / `group` / `daily_cap` | không có ngưỡng cứng | `daily_cap` chiếm đa số → cap ngày đang bóp treatment; ITT vẫn đúng nhưng effect size bị pha loãng |
| Exposure tích luỹ | user có `exp_exposure` mỗi nhánh theo ngày, so với đường cần để đủ mẫu tại ngày readout | bám đường kế hoạch | Chậm → **không** dời ngày readout để "chờ đủ" (peeking); ghi nhận, kết luận tại ngày đã chốt với power thật |
| SRM | chi-square số user mỗi nhánh của `exp_exposure` so với `weight` | p ≥ 0.001 | Assignment hoặc exposure log lệch; **kết quả experiment không dùng được** cho tới khi tìm ra nguyên nhân |
| Decision latency | p95 `eval_ms` trong `seg_decision` theo `trigger` và theo tầng thiết bị | p95 < 5 ms; không có mẫu > 16 ms | AST quá sâu hoặc quá nhiều rule `level`; nhìn cùng persist trên Android tầm thấp (§4.5) |
| Volume tracking | event `seg_*` / `action_*` / `exp_*` so với tổng event của game | ≤ 20% (§11.1) | Hạ sample `seg_decision`, gộp rule `level` |

`eval_ms`: số nguyên ms từ lúc engine nhận trigger đến khi giao xong cho executor, chỉ trong `seg_decision` (đã có sample 5% cho trigger không match) và hiển thị ở debug overlay (§11.3).

### 13.3 Action KPI: `result` theo action type

`action_executed` đã có field `result` (§11.1) nhưng chưa quy định giá trị. Không có nó thì khi rule không lift không biết user **không thấy** action, **thấy mà bỏ qua**, hay **dùng mà vẫn rời đi**. Executor báo `result` từ bộ giá trị cố định; phản hồi sau đó DWH nối bằng `execution_id` hoặc theo thời gian.

| Action type | `result` executor báo (trong session) | Phản hồi DWH nối thêm | Action KPI |
|---|---|---|---|
| `GIVE_REWARD` | `granted` · `inventory_full` | Event dùng item của game (nếu có) trong session; `PROGRESS_START` kế tiếp | % tiếp tục chơi trong 5 phút sau grant; % dùng reward trong session |
| `SHOW_POPUP` | `clicked` · `dismissed` · `timeout` (báo khi popup **đóng**) | `SCREEN_OPEN` kế tiếp; `SESSION_END` trong 60 s | Click rate; quit rate ngay sau popup (guardrail) |
| `SHOW_OFFER` | `purchased` · `closed` · `not_loaded` (báo khi offer đóng) | `PURCHASE` mang `execution_id` hoặc last-touch 7 ngày (§8.7) | Offer → purchase; revenue trên mỗi lần hiển thị |
| `CHANGE_DIFFICULTY` | `applied` · `no_unit_pending` | Kết quả unit kế tiếp: `PROGRESS_COMPLETE` / `_FAIL` / `_QUIT` | Pass rate unit kế tiếp; quit rate; ad impression / session (guardrail) |
| `SCHEDULE_LOCAL_NOTIFICATION` | `scheduled` · `replaced` | `SESSION_START` trong 2 h sau giờ hẹn; event mở từ notification của tracking SDK nếu có | Open rate; permission revoke rate (guardrail) |

Quy ước: `ReportPresented` lúc bắt đầu hiển thị / áp; `ReportExecuted(result)` khi user đã phản hồi xong. Giá trị ngoài bộ trên → SDK ghi `other` và dev build log cảnh báo; CLI không kiểm được vì đây là runtime. Action KPI dùng để **chẩn đoán**, không thay primary metric: popup click rate cao mà D7 không đổi vẫn là rule không hiệu quả.

### 13.4 Pre-registration và guardrail

Trước khi publish config có action thật, PR phải có khối sau (CLI kiểm PR description có khối này; nội dung do người review kiểm):

```text
experiment_id, layer
primary metric        : D7 sau exposure (mặc định); D1 nếu rule nhắm session đầu
MDE                   : lift tương đối tối thiểu muốn thấy, lấy từ 0(f)
exposed cần / nhánh   : theo bảng §9.3 với MDE trên
guardrail             : 2–3 metric, mỗi cái một biên non-inferiority
ngày readout          : cố định, cộng 7 ngày cho D7 của exposure cuối chín
nếu thắng             : holdback 0.9 / 0.1 trong N tuần (§13.5)
nếu thua              : về base none; hoặc biến thể nào thử tiếp
```

Guardrail là metric **không được xấu đi** dù primary thắng. Khai bằng biên non-inferiority tương đối trên user exposed, ví dụ "tổng revenue / user 14 ngày (IAP + ad) không giảm quá 3%, CI 90%". Guardrail vỡ có ý nghĩa thống kê → **không ship**, dù D7 tăng. Mặc định theo group của action:

| Group | Guardrail mặc định | Vì sao |
|---|---|---|
| `reward` | IAP conversion 14 ngày; **chi phí reward** = giá trị quy đổi của reward đã grant trên mỗi user exposed, so với incremental revenue | Booster tặng miễn phí ăn vào booster bán; lift retention phải trả được chi phí |
| `difficulty` | Ad impression / session; playtime / session | Dễ hơn → qua nhanh → ít ad, ít thời gian chơi |
| `offer` | Ad revenue / user; quit rate ngay sau offer | Offer đẩy IAP có thể giảm xem ad; offer sai lúc làm user rời |
| `message` | Quit rate trong 60 s sau popup; D1 | Popup là thứ dễ gây khó chịu nhất |
| `notification` | Permission revoke / opt-out rate; uninstall nếu tracking có | Notification quá tay mất cả kênh |
| Mọi experiment | D1 không âm; **tổng revenue / user 14 ngày** (IAP + ad) không âm | Ad revenue đo ở cấp user vì tracking hiện có đã có sẵn field này |

Incremental metric báo cáo tại readout: D1 / D7 / D14, session / user, playtime, IAP revenue / user, ad revenue / user, tổng revenue / user, cộng Action KPI (§13.3) để chẩn đoán. Mọi số là **treatment trừ control** trên user exposed, kèm CI; không báo số tuyệt đối của một nhánh.

### 13.5 Holdback sau khi thắng

§9.1 trước đây: có kết quả thì sửa `then` ở base và xoá experiment. Làm vậy mất control đúng lúc cần nó nhất: lift đo ở tuần 1–6 có thể là **novelty**, phai dần khi user quen với booster miễn phí hoặc học được cách "farm" điều kiện.

- Readout dương và guardrail không vỡ → publish config mới **cùng `experiment_id`**, `weight` 0.9 / 0.1 (treatment / control). Assignment sticky nên user cũ giữ nhánh; user mới chia 90 / 10.
- Giữ **4–8 tuần** (ít nhất hai cửa sổ D14 đầy). So lift của cohort exposure tuần 1–2 với cohort exposure tuần 5–8. Lift giữ ≥ 50% giá trị ban đầu → sửa `then` ở base, xoá experiment. Phai về gần 0 → về base `none` và ghi lại; đây là kết quả có giá trị, không phải thất bại.
- 10% control ở giai đoạn này chỉ cần phát hiện lift **biến mất**, không cần MDE ban đầu, nên mẫu không phải vấn đề.
- **Trade-off:** layer vẫn bị chiếm trong lúc holdback (mỗi layer một experiment, §9.1). Cần layer cho experiment kế tiếp → rút holdback về 4 tuần hoặc bỏ qua, ghi lý do trong PR. Không mở hai experiment trong một layer để giữ holdback.

### 13.6 Checklist tuần shadow và tuần A/A

Chạy ở bước 9 trước khi đổi treatment sang action thật. Mỗi mục có người ký (Data); không có mục "sẽ xem sau".

**Tuần shadow (config đầu tiên, chưa có experiment):**

- Config adoption ≥ 90% sau 24 h; `config_rejected` = 0; `engine_error`, `state_reset` < 0.1%.
- `seeded` ≥ 95% trên user cũ; phân bố `seg_payer` khớp DWH (± 2 điểm %).
- Fire rate mỗi rule lệch ≤ 30% so với bước 0 / `replay`; tính lại `users_cần_chạm_trigger`.
- **Kiểm định segment (§13.1):** D7 theo `seg_lifecycle` tách đơn điệu; quit rate session kế tiếp của tag `frustrated` cao hơn base.
- Rule thay logic sẵn có (§8.2): `action_executed` / `action_selected` ≥ 95%; hành vi cũ không mất (số notification đặt / user không giảm so với trước migrate).
- Action `one_shot`: `action_selected` có `nth > 1` = 0 trên toàn bộ user; `state_reset reason=history` < 0.1%; QA đã chạy kịch bản reinstall + restore save và restore về máy đã chơi, one-shot không lặp (§4.5).
- Volume tracking ≤ 20%; `eval_ms` p95 < 5 ms.

**Tuần A/A (experiment, hai nhánh `rule_actions: {}`):**

- SRM: p ≥ 0.001 trên `exp_exposure` theo nhánh.
- Mỗi user đúng **một** `exp_exposure` mỗi experiment; user property `exp_<layer>` khớp variant trong `exp_exposure` ≥ 99%.
- Query readout chạy trên dữ liệu A/A, ra primary, guardrail, Action KPI cho cả hai nhánh; khác biệt nằm trong CI (kiểm query, không phải kiểm hiệu quả).
- Exposure tích luỹ theo ngày bám đường kế hoạch tới ngày readout.
- PR pre-registration (§13.4) đã merge.

### 13.7 Không làm trong MVP

- Holdout > 0 (Phase 2, §9.2). Dashboard hay service đo riêng ngoài BI / DWH hiện có.
- Sequential test (mSPRT, alpha-spending): một mốc readout cố định là đủ; muốn dừng sớm thì Phase 2 khai trước.
- CUPED hoặc covariate hoạt động trước exposure để giảm 20–40% mẫu: đáng làm ở Phase 2 nếu pipeline A/B hỗ trợ, vì mẫu là rủi ro số một; MVP không thêm việc cho Data.
- Attribution đa chạm, uplift model, LTV dự đoán làm outcome. Outcome là số đếm được trong 14 ngày.
- Đo trên user không exposed. Mọi số của engine tính trên user exposed; DAU chỉ dùng cho engine health.

---

## Phụ lục A — Config mẫu tối thiểu (chạy được)

### A.1 Envelope Worker trả

```json
{
  "server_time": 1759287600,
  "config": { "xem": "A.2" }
}
```

### A.2 Config (bản đã compile; file nguồn trong repo viết `expr` dạng string, §6.1)

```json
{
  "schema_version": 3,
  "min_sdk_version": "1.0.0",
  "game_id": "puzzle_x",
  "env": "prod",
  "version": 1,
  "published_at": "2026-10-01T03:00:00Z",
  "enabled": true,
  "session_timeout_s": 1800,
  "max_actions_per_day": 3,
  "holdout": { "allocation": 0.0 },
  "const": { "playtime_7d_p90": 7200 },
  "formulas": [
    {
      "id": "fail_rate",
      "expr": {
        "op": "DIV",
        "args": [
          { "ref": "state.fail_count", "window": "7d" },
          { "op": "MAX", "args": [ { "ref": "state.attempt_count", "window": "7d" }, { "value": 1 } ] }
        ]
      }
    },
    {
      "id": "frustration_score",
      "expr": {
        "op": "ADD",
        "args": [
          { "op": "MUL", "args": [ { "ref": "feature.fail_rate" }, { "value": 0.7 } ] },
          {
            "op": "MUL",
            "args": [
              { "op": "MIN", "args": [ { "op": "DIV", "args": [ { "ref": "state.fail_streak" }, { "value": 5 } ] }, { "value": 1 } ] },
              { "value": 0.3 }
            ]
          }
        ]
      }
    }
  ],
  "segments": [
    {
      "id": "frustrated",
      "kind": "tag",
      "enter": { "op": "GT", "args": [ { "ref": "feature.frustration_score" }, { "value": 0.7 } ] },
      "exit":  { "op": "LT", "args": [ { "ref": "feature.frustration_score" }, { "value": 0.5 } ] }
    },
    {
      "id": "lifecycle",
      "kind": "partition",
      "default": "active",
      "cases": [
        { "value": "new",       "when": { "op": "LT",  "args": [ { "op": "TIME_SINCE", "args": [ { "ref": "state.install_at" } ] }, { "value": 86400 } ] } },
        { "value": "returning", "when": { "op": "GTE", "args": [ { "ref": "state.days_since_last_active" }, { "value": 7 } ] } },
        { "value": "at_risk",   "when": { "op": "GTE", "args": [ { "ref": "state.days_since_last_active" }, { "value": 3 } ] } }
      ]
    }
  ],
  "actions": [
    {
      "id": "give_booster_small",
      "type": "GIVE_REWARD",
      "params": { "reward_id": "booster_hammer", "amount": 1 },
      "group": "reward",
      "cooldown_s": 86400,
      "cap": { "count": 2, "window_s": 604800 }
    },
    {
      "id": "ease_next_unit",
      "type": "CHANGE_DIFFICULTY",
      "params": { "delta": -1, "scope": "next_unit" },
      "group": "difficulty",
      "cooldown_s": 3600,
      "cap": { "count": 3, "window_s": 86400 }
    },
    {
      "id": "starter_pack_offer",
      "type": "SHOW_OFFER",
      "params": { "offer_id": "starter_pack" },
      "group": "offer",
      "frequency": "one_shot"
    },
    {
      "id": "notify_comeback_23h",
      "type": "SCHEDULE_LOCAL_NOTIFICATION",
      "params": { "template_id": "comeback_daily", "delay_h": 23 },
      "group": "notification",
      "cooldown_s": 0,
      "cap": { "count": 1, "window_s": 1 }
    }
  ],
  "rules": [
    {
      "id": "booster_when_frustrated",
      "enabled": true,
      "priority": 100,
      "on": [ "SCREEN_OPEN:result" ],
      "fire": "edge",
      "when": {
        "op": "AND",
        "args": [
          { "ref": "segment.frustrated" },
          { "op": "GTE", "args": [ { "ref": "state.fail_streak" }, { "value": 3 } ] }
        ]
      },
      "then": "none",
      "valid_until": "2026-12-31T00:00:00Z"
    },
    {
      "id": "comeback_notification",
      "enabled": true,
      "priority": 10,
      "on": [ "SESSION_START" ],
      "fire": "level",
      "when": { "op": "GTE", "args": [ { "ref": "state.days_since_install" }, { "value": 1 } ] },
      "then": "notify_comeback_23h",
      "valid_until": "2026-12-31T00:00:00Z"
    }
  ],
  "experiments": [
    {
      "id": "booster_when_frustrated_v1",
      "layer": "gameplay_help",
      "allocation": 1.0,
      "ends_at": "2026-11-30T00:00:00Z",
      "variants": [
        { "id": "control", "weight": 0.5, "rule_actions": {} },
        { "id": "booster", "weight": 0.5, "rule_actions": {} }
      ]
    }
  ]
}
```

Đọc config này:

- `booster_when_frustrated` ở base là shadow (`none`), trigger tại màn result: user fail lần 3 → `PROGRESS_FAIL` cập nhật `fail_streak = 3` → màn result mở → `SCREEN_OPEN:result` chạy engine → `when` false → true → fire. Không có pending queue; nếu có action thì booster hiện ngay trên màn result vừa mở.
- Experiment đang ở **tuần A/A**: cả hai variant `rule_actions: {}`. `allocation` 1.0 nên 100% user vào experiment, chia đôi, cả hai đều `none` nhưng có `exp_exposure` và user property `exp_gameplay_help`. Sau khi kiểm SRM, assignment cân và query readout chạy, chốt ngày readout rồi publish version 2 với variant `booster` đổi thành `{ "booster_when_frustrated": "give_booster_small" }`; assignment giữ nguyên.
- Holdout 0%, `allocation` 1.0: không có user nào ngoài experiment. Mẫu cần chạm trigger bằng nửa so với allocation 0.5 (§9.3).
- `comeback_notification` là rule `level` tại `SESSION_START` và là **ví dụ của §8.2 "ngoại lệ"**: pilot đang tự đặt notification 23 giờ, nên base `then` = `notify_comeback_23h` với template đúng hành vi cũ ngay từ version 1, code notification cũ gỡ trong cùng bản update. Không có giai đoạn nào user mất notification; muốn thử template / giờ khác thì mở experiment trên layer riêng override `then`. Mỗi lần mở app đặt lại lịch 23 giờ; group `notification` không tính vào cap 3 action/ngày. CLI cho phép rule này ở `SESSION_START` vì action thuộc group `notification`.
- `ease_next_unit` khai sẵn cho experiment kế tiếp trên cùng layer sau `ends_at`.
- Không có canary: mọi user nhận config này ở lần fetch kế (Initialize, hoặc Resume sau > 5 phút). Publish version 2 → 100% nhận; có vấn đề → `rollback --to 1` publish lại nội dung version 1 dưới version 3. Tuần A/A với `rule_actions: {}` ở cả hai nhánh là lưới an toàn cho nghiệp vụ, `enabled: false` là kill-switch cho mọi thứ khác.

---

## Phụ lục B — Đối chiếu v0.3 → v0.4

| v0.3 | v0.4 | Lý do |
|---|---|---|
| Pending queue: `deliver_on: screen:<id>`, `ttl_s`, status `selected → presented → executed`, ring buffer `execution_id`, `action_expired`, group bị chiếm bởi pending | Bỏ hết. Action thực thi ngay; rule viết trigger tại màn cần hiển thị qua `on: "SCREEN_OPEN:<id>"` | State đã nhớ điều vừa xảy ra; 5 action type đều chạy được với deliver ngay. Bỏ được khối lớn nhất của bước 3 và mọi edge case restart |
| `null` lan truyền, ba trị, `COALESCE`; chia 0 → `null`; formula invalid → disable phần phụ thuộc (partial degradation) | Không có `null`. Default toàn phần (0 / false / "" / 0); chia 0 → 0; type-check tĩnh ở CLI; config invalid → **từ chối toàn bộ**, fallback channel / cache | Ba trị là nguồn bug tinh vi; CLI đã chặn trước publish nên client chỉ cần lưới an toàn thô |
| Rollout trong config; user ngoài rollout chạy "known-good trước đó"; cài mới ngoài rollout → `config_source = none` | Envelope `{ stable, canary, canary_rollout }`; client chọn theo hash; không có known-good trên client | New install luôn có `stable`; rollback = publish lại stable; bớt bookkeeping ở phía khó sửa (client) |
| ETag + edge cache 5 phút; server time từ header `Date` | Không cache, không ETag; `server_time` trong body | `Date` của response cache lệch tới 5 phút; Worker + KV rẻ, Cloudflare không tính egress |
| `rule_fired` mỗi rule, `decision_trace` sample, `seg_changed` | Một event `seg_decision` mỗi trigger có match (+ 5% sample); partition & assignment lên **user property**; bỏ `seg_changed` | Bớt loại event và số event; readout thành `GROUP BY` user property; tránh trần export BigQuery |
| Holdout 5% | Cơ chế giữ, `allocation = 0` trong MVP | Sample là rủi ro số một; per-experiment control đã đo đúng khi chỉ có 1–2 rule live |
| Persist tại `SESSION_END` + sau action (debounce 5 giây) | Persist sau mỗi trigger, mỗi lần pause, atomic write | Trigger cách nhau hàng chục giây; bớt một cơ chế; crash mất ít hơn |
| Không có seed; user cũ = state trống | `Seed()` từ dữ liệu game sẵn có; `state.seeded`, `seeded_at` | Không seed thì whale thành `non_payer`, cả base thành `new` suốt kỳ MVP |
| `SESSION_START` "khi initialize"; không định nghĩa resume | `session_timeout_s` khớp tracking SDK; pause / resume / quit định nghĩa rõ | `session_count` phải khớp DWH; notification đặt lại đúng lúc |
| Server time + monotonic clock trong session | `now = device_utc + offset`; monotonic không dùng | `realtimeSinceStartup` / Stopwatch dừng khi iOS suspend → lệch bằng thời gian nền |
| Queue chạy ngay, config mới áp từ trigger kế | Queue mở sau khi fetch xong hoặc timeout 3 giây | `SESSION_START` (notification, offset) chạy trên config mới nhất |
| `PROGRESS_QUIT`: "event trước là `PROGRESS_FAIL` trong 60 giây" | `now − last_fail_at ≤ 60` | Giữa FAIL và QUIT luôn có `PROGRESS_START` nên điều kiện cũ không bao giờ đúng |
| `unit_id` không kiểu | `unit_id` là NUMBER | `progress.max = max(...)` chỉ đúng với số |
| `PURCHASE` không dedupe | `transaction_id` bắt buộc, ring buffer 50 | Restore purchase và callback đôi nhân đôi `total_spend_usd` |
| Không có `CUSTOM_EVENT`; "khoảnh khắc lạ" dùng `SCREEN_OPEN` giả | `CUSTOM_EVENT` whitelist ≤ 10 tên trong manifest, có counter window | Idle / tycoon không có fail / streak; hack `SCREEN_OPEN` làm bẩn semantic screen |
| Không nói exception runtime | Sandbox: exception → tắt engine hết session, log một lần; giới hạn độ sâu / node AST | Config không bao giờ làm game crash |
| Edge state có hash `when`; tag hysteresis không | Tag persist kèm hash `enter + exit`; edge state hash `on + when` | Cùng lớp lỗi: định nghĩa đổi mà state cũ còn |
| `exp.*` đọc được trong AST | Cấm trong AST, chỉ có trong tracking | Giữ bất biến "experiment chỉ đổi `then`" |
| `custom.*` không có schema | Manifest khai `custom_state: { key: type }` | CLI validate được ref và kiểu |
| Manifest khai `actions` tay | SDK tự suy từ executor đã đăng ký | Bớt một chỗ lệch giữa khai báo và thực tế |
| `max_actions_per_day` tính mọi action | Group `notification` không tính | Đặt lịch notification mỗi session không được ăn hết cap ngày |
| `/config` public mọi env | `env ≠ prod` cần `X-Config-Token` | Không lộ experiment sắp chạy |
| Fetch chỉ tại session start | Thêm fetch khi resume nếu > 5 phút | Kill-switch không phải đợi session sau, quan trọng với idle game session dài |
| Sample size theo exposed; ước lượng exposure rate ở bước 8 | Ước lượng ở **bước 0**, trước khi code; khuyến nghị hardcode rule số một chạy A/B 2 tuần | Quyết định pilot có đủ mẫu không phải biết trước khi tiêu 3 tháng dev; có baseline lift sớm |
| Bật experiment thật ngay sau tuần shadow | Tuần A/A trước A/B | Kiểm assignment, exposure, user property, query readout mà không tiêu mẫu |
| Outcome D7 không nói mốc | D1 / D7 / D14 tính từ lần exposure đầu; exposure là intent-to-treat | Rule nổ ở nhiều tenure; ITT là readout chính |
| Chỉ có AST JSON | CLI `compile` string expression → AST (tuỳ chọn) | "Product tự publish" chỉ thực tế khi không phải viết JSON lồng |
| `execution_id` uuid | 8 hex, duy nhất theo user | Vừa giới hạn 100 ký tự khi gói chuỗi |
| Tiêu chí "không cần dev" | "không cần release game" | Đúng với thứ hệ thống thật sự hứa |

Đối chiếu v0.2 → v0.3 xem Phụ lục B của bản v0.3.

### B.2 — Đối chiếu v0.4 → v0.4 cập nhật review vòng 2 (2026-09-11)

| v0.4 sáng 2026-09-11 | v0.4 cập nhật | Lý do |
|---|---|---|
| Envelope `{ stable, canary, canary_rollout }`, client chọn channel theo hash, `promote`, `config_channel` | Envelope `{ config, server_time }`; không canary trong MVP; giữ `history` + `rollback` | CLI validate, `then: none` + experiment, sandbox và cache fallback đã che hết các lớp lỗi canary che; với một game canary chỉ còn chi phí |
| Worker đọc 3 KV key | Một key `envelope` | Ở 1M DAU, 3 key là cỡ trăm đô/tháng phí KV read, không phải "vài đô" |
| `allocation` mặc định 0.5 | Mặc định 1.0; CLI cảnh báo < 1.0 | Mỗi layer một experiment nên user ngoài allocation là mẫu mất trắng; `users_cần_chạm_trigger` giảm nửa |
| Bảng mẫu chỉ có lift +10% / +20% | Thêm +5% (~36 000 exposed/nhánh ở baseline 15%) | Can thiệp in-game thường +2–5%; +10–20% là lạc quan |
| Hardcode rule số một chạy A/B 2 tuần là "khuyến nghị mạnh" | Bước 0(f) **bắt buộc**, effect size để tính mẫu lấy từ đây | Quyết định tiêu 3 tháng dev phải dựa trên effect size thật, không giả định |
| Không nói về thời điểm đọc readout | Chốt ngày readout trước khi bật treatment; không peeking; SRM check ở tuần A/A | Đọc hàng tuần và dừng khi "thấy có ý nghĩa" thổi false positive |
| Rule thay logic game sẵn có cũng ship `then: none`, "một trong hai phải nhường" | Base `then` = action tái tạo hành vi cũ, gỡ code cũ cùng bản update (§8.2 ngoại lệ) | Ship `none` rồi gỡ code cũ làm cả base mất notification / starter pack suốt shadow, A/A và control |
| `PURCHASE` từ mọi callback IAP, dedupe bằng ring buffer kể cả restore | Chỉ emit từ luồng mua mới; restore không emit | Sau reinstall + Seed, giao dịch cũ chưa có trong ring buffer bị cộng lần hai |
| Seed chỉ khi state trống; không có `LastActiveAt`; thứ tự gọi so với `Initialize` không rõ | Seed khi `seeded == false` bất kể state, merge `max`; thêm `LastActiveAt`; `SeedProvider` trong `Initialize()`, `NeedsSeed` | Cloud restore trên máy đã chơi; user vắng lâu bị xếp `active`; save game thường load sau SDK init |
| Window `1d` = hôm nay UTC | Bỏ `1d`, thêm `session` (counter reset tại `SESSION_START`) | UTC+7 reset lúc 7h sáng làm rule in-session mất nghĩa; `@session` rẻ hơn bucket |
| `context.actions_shown_today` là runtime, không trong danh sách persist | Persist cùng ngày UTC | Cap ngày đang reset mỗi lần kill app |
| Persist đồng bộ trên main thread mỗi trigger | Serialize main thread, ghi thread nền; pause / quit ghi đồng bộ | 25 KB + fsync trên Android tầm thấp vượt một frame đúng lúc mở màn result |
| File state hỏng chưa định nghĩa | Xoá, log `state_reset`, `NeedsSeed = true` | State nửa vời sinh segment sai khó truy hơn state mới |
| Đồng hồ nhảy tiến chỉ phát hiện ở lần fetch sau | Đối chiếu monotonic trong cùng khoảng foreground, lệch > 60 giây → `clock_suspect` | Trong lúc chờ fetch, `TIME_SINCE` phồng, cooldown hết sớm, `lifecycle` sai |
| Cooldown / cap ghi lúc `selected`, không hoàn | Hoàn lại khi `action_failed` reason `offline` / `no_permission` | User không mất offer một ngày vì rớt mạng; vẫn không lặp vô hạn với lỗi khác |
| `compile` tuỳ chọn | Bắt buộc, trong bước 6 | 1–2 ngày parser đổi lấy việc người không phải dev soạn được rule; không có thì §12.4 không thực tế |
| `seg_snapshot` và `seg_decision` đều bắn tại `SESSION_START` | Gộp `rules_matched` / `selected` / `dropped` vào snapshot; `seg_decision` bỏ qua `SESSION_START` | Rule `level` tại `SESSION_START` làm hai event luôn đi đôi; bớt một event mỗi session |
| Không có lint cho rule tại `SESSION_START` | Rule có `SESSION_START` trong `on` chỉ được trỏ action group `notification` | UI game chưa sẵn lúc đó; popup / offer sẽ nổ trên màn loading |
| `manifest.json` trong repo viết tay | SDK export qua debug overlay "Copy manifest JSON" | Hết drift giữa repo và build |
| Không có replay | CLI `replay --events` (tuỳ chọn, sau bước 7) | Đo fire rate chính xác và bắt lỗi rule trên event thật trước publish, không tiêu mẫu |
| Tiêu chí đi tiếp đo lift rule + số lần publish | Thêm "rule thứ hai từ ý tưởng đến live ≤ 1 tuần không release" | Hai tiêu chí cũ hardcode + Remote Config cũng đạt; tiêu chí mới đo đúng giá trị của engine |
| Ước lượng không tính giờ team game | Bước 7 ghi rõ dev-tuần team game; bảng 1.1 thêm dòng | Chỗ hay trượt gấp đôi nhất trong kế hoạch |

### B.3 — Đối chiếu v0.4 review vòng 2 → review vòng 3 (2026-09-14)

| v0.4 review vòng 2 | v0.4 review vòng 3 | Lý do |
|---|---|---|
| Không có mục gom "đo hiệu quả"; cơ chế rải ở §9, §11, §12.4 | **§13 Measurement & Evaluation**, không thêm thành phần hệ thống | Người đọc không thấy hệ thống có tự chứng minh hiệu quả được không; gom lại và thêm sáu thứ thiếu |
| Không kiểm định segment | Tầng segment §13.1; mục bắt buộc trong checklist tuần shadow §13.6 | Rule không lift có thể do segment sai; sửa formula rẻ hơn thử action khác và không tốn mẫu |
| Có event nhưng không có metric / ngưỡng khi live; "không cần dashboard riêng" | Bảng engine health §13.2 trên BI hiện có; thêm `eval_ms` vào `seg_decision` | Debug overlay chỉ là QA dev build; live cần execution ratio, fire rate so với bước 0, SRM, adoption, latency |
| `result` của `action_executed` không định nghĩa | Bộ giá trị cố định theo action type §13.3; `ReportExecuted` gọi khi user phản hồi xong | Không phân biệt được user không thấy / bỏ qua / dùng mà vẫn rời |
| Chỉ chốt ngày readout | Pre-registration trong PR: primary, MDE, guardrail, quyết định thắng / thua §13.4 | Guardrail chặn ship rule tăng D7 nhưng ăn revenue; mặc định theo group của action |
| Ad impression là metric phụ | Ad revenue / user vào guardrail và incremental metric | Tracking hiện có đã đếm ad revenue ở cấp user |
| Thắng → sửa base, xoá experiment ngay | Holdback `weight` 0.9 / 0.1 trong 4–8 tuần §13.5, rồi mới về base | Lift tuần đầu có thể là novelty; về base là mất control đúng lúc cần |
| A/A kiểm SRM, exposure, user property | Checklist shadow + A/A đủ mục, có người ký §13.6 | Gom điều kiện bật treatment vào một chỗ |
| Phase 2 chưa nói giảm mẫu | Thêm CUPED và sequential test khai trước vào §12.2, §13.7 | Mẫu là rủi ro số một; CUPED giảm 20–40% |
| Tiêu chí đi tiếp: lift D7 ý nghĩa | Thêm "và không vỡ guardrail" (§0, §12.4) | Lift có chi phí thì chưa phải thắng |

### B.4 — Đối chiếu v0.4 review vòng 3 → review vòng 4 (2026-09-14)

| v0.4 review vòng 3 | v0.4 review vòng 4 | Lý do |
|---|---|---|
| Action không phân loại tần suất; "một lần" phải mô phỏng bằng `cap` dài, mất khi reinstall | `frequency: one_shot \| repeatable` trên action, mặc định `repeatable`; one-shot khoá tại `selected`, hoàn lại khi `offline` / `no_permission` (§8.3, §8.7) | Starter pack, quà lần đầu là nhu cầu thật; cap không diễn tả được "một lần trong đời" |
| Toàn bộ state trong một file JSON riêng của SDK; reinstall / đổi máy mất hết, kể cả "đã nhận gì" | Hai tầng (§4.5): lịch sử action vào **player data của game** qua `IActionHistoryStore`, có mirror trong file SDK, merge theo quy tắc Seed; tầng vận hành giữ nguyên ở file SDK | "User đã trải qua action nào" là dữ liệu theo user, phải đi theo save + backup sẵn có của game; bucket / cooldown / edge mất thì tự hồi, không đáng kéo vào save |
| Không có cách seed "đã nhận" cho logic một lần có sẵn | `MarkActionUsed()` ở bước 7 (§4.5, §8.2, §12.1) | Migrate starter pack sang engine mà không phát lần hai cho user cũ |
| Cooldown mất hẳn khi reinstall | `history.l` làm sàn cooldown (§8.3) | Rẻ, không thêm dữ liệu; cap vẫn reset, chấp nhận |
| `dropped` reason: cooldown / cap / group / daily_cap | Thêm `one_shot`; `action_selected.nth`; `exp_exposure.history_used`; `state_reset reason=history` (§11.1) | Đo được one-shot; tách user đã dùng hết one-shot trước experiment mà vẫn giữ intent-to-treat (§9.3) |
| CLI không biết `frequency` | Lint enum, cấm trên `SCHEDULE_LOCAL_NOTIFICATION`, cảnh báo cooldown / cap thừa, cảnh báo đổi `frequency` khi rule đang live (§10.3) | Đổi sang `one_shot` khoá ngay user có `n ≥ 1`, người review phải thấy |
| Bảng 1.1, §12.5 không hỏi về save system | Thêm dòng "Save system của pilot" và câu hỏi 9 | Nơi gắn store và thứ tự restore / init quyết định bước 7 |
| AST không nói gì về lịch sử action | Nói rõ **không** có trong AST ở MVP; dùng `custom.*`; `action.<id>.count` để Phase 2 (§5.1, §12.2) | Giữ bất biến resolver lo tần suất, rule lo điều kiện; tránh thêm namespace khi chưa có rule thật cần |
