# Phụ lục C — Interface & Schema Contract

**Thuộc:** User Segmentation & Action Engine, MVP Design v0.4 (review vòng 4, 2026-09-14)
**Bản:** draft 1, 2026-09-16
**Trạng thái:** chờ review. Sau review, các mục **Chốt** merge vào tài liệu chính; các mục **Đề xuất** thành Chốt hoặc bị thay.

**Cách đọc**

- **[Chốt]** = tài liệu chính đã ngầm định hoặc để trống, phụ lục này điền cho đủ; không mâu thuẫn với v0.4. Dev code theo đây.
- **[Đề xuất]** = tài liệu chính chưa quyết; phụ lục đưa một giá trị cụ thể kèm lý do để review chọn. Đổi giá trị không làm vỡ phần khác.
- Mọi ví dụ JSON ở đây là **bản đã compile** (thứ client đọc). File nguồn trong repo viết `expr` dạng string (§C.3).

## C.0 Bảng tra: thiếu gì, chốt ở đâu

| Thiếu (theo review 2026-09-16) | Mục |
|---|---|
| JSON Schema chính thức của config, field bắt buộc / default | C.1 |
| Schema node AST, arity, kiểu | C.2 |
| Grammar string expression cho `compile` | C.3 |
| Bảng kiểu đầy đủ của mọi `ref`, cách lưu counter có window | C.4 |
| Hàm hash assignment / holdout, hash định nghĩa, `user_id`, `execution_id`, sample | C.5 |
| Public API SDK, `SdkOptions`, adapter interface, executor contract, thread | C.6 |
| Format chính xác param tracking, giới hạn Firebase, lint độ dài | C.7 |
| Enum đóng cho reason / stage / result | C.8 |
| Layout repo config, quy tắc kế thừa `base/` → `games/` | C.9 |
| Schema file state SDK, cache config, fixture, test vector, replay | C.10 |
| Contract Worker `/config`, KV, publish | C.11 |
| Giá trị đề xuất cho các semantic chưa chốt (Nhóm 2) | C.12 |

---

## C.1 Config schema

### C.1.1 Quy ước chung [Chốt]

- **Config đã compile là tường minh toàn phần.** CLI điền mọi default khi compile; client **không có default**, thiếu field bắt buộc → `config_rejected reason=schema`. Default chỉ tồn tại ở file nguồn (cột "Default nguồn" bên dưới).
- **Identifier** (`id` của formula / segment / action / rule / experiment / variant, `layer`, tên custom event, key custom state, `screen_id`, `reward_id`): `^[a-z][a-z0-9_]{0,39}$`. Unique trong từng danh sách; `formula.id` và `segment.id` unique chung (cùng namespace DAG).
- **Thời gian trong config** (`published_at`, `valid_until`, `ends_at`): chuỗi ISO 8601 UTC dạng `YYYY-MM-DDTHH:MM:SSZ`, không offset. Client parse thành epoch giây rồi so với `now` (§4.4). `server_time`, mọi timestamp trong state và tracking: epoch giây, số nguyên.
- **`min_sdk_version`, `sdk`:** semver `MAJOR.MINOR.PATCH`, ba số nguyên, không pre-release. So sánh theo bộ ba số. `min_sdk_version > sdk_version` → `config_rejected reason=min_sdk`.
- **`schema_version`:** số nguyên. SDK khai `SupportedConfigSchemas = { 3 }`; khác → `reason=schema`.
- Số: JSON number, đọc thành double. Không có `null` ở bất kỳ đâu trong config.
- Field lạ (không trong schema) ở bất kỳ object nào → `reason=schema`. Lý do: bắt lỗi gõ sai `cooldown` thay `cooldown_s` ngay ở CLI và client.

### C.1.2 Top-level [Chốt]

| Field | Kiểu | Bắt buộc (compiled) | Default nguồn | Ràng buộc |
|---|---|---|---|---|
| `schema_version` | int | có | 3 | ∈ SupportedConfigSchemas |
| `min_sdk_version` | semver | có | `"1.0.0"` | |
| `game_id` | ident | có | từ thư mục `games/{game}` | phải bằng `SdkOptions.GameId` |
| `env` | `"prod"` \| `"dev"` \| `"staging"` | có | từ lệnh publish | phải bằng `SdkOptions.Env` |
| `version` | int ≥ 1 | có | KV counter (§C.11) | tăng đơn điệu |
| `published_at` | ISO 8601 | có | giờ CLI lúc publish | |
| `source_commit` | string | không | git SHA ngắn | chỉ để truy vết; client bỏ qua |
| `enabled` | bool | có | true | kill-switch §10.1 |
| `session_timeout_s` | int | có | 1800 | 60..86400 |
| `max_actions_per_day` | int | có | 3 | 0..20; 0 = không action nào ngoài `notification` |
| `holdout` | object | có | `{ "allocation": 0.0 }` | §C.1.8 |
| `const` | object\<ident, number\> | có | `{}` | chỉ NUMBER |
| `formulas` | array | có | `[]` | ≤ 50 |
| `segments` | array | có | `[]` | ≤ 30; partition ≤ 8 (slot user property) |
| `actions` | array | có | `[]` | ≤ 100 (blob lịch sử ≤ 5 KB, §4.5) |
| `rules` | array | có | `[]` | ≤ 100 |
| `experiments` | array | có | `[]` | mỗi `layer` ≤ 1 experiment có `ends_at > now` lúc compile |

### C.1.3 Formula [Chốt]

| Field | Kiểu | Bắt buộc | Ràng buộc |
|---|---|---|---|
| `id` | ident | có | |
| `expr` | AST (§C.2) | có | kiểu kết quả ∈ { NUMBER, BOOL } (§C.12 mục 16) |

### C.1.4 Segment [Chốt]

Tag:

| Field | Kiểu | Bắt buộc | Default nguồn | Ràng buộc |
|---|---|---|---|---|
| `id` | ident | có | | |
| `kind` | `"tag"` | có | | |
| `enter` | AST | có | | kiểu BOOL |
| `exit` | AST | có (compiled) | `{ "op": "NOT", "args": [ enter ] }` | kiểu BOOL |
| `def_hash` | hex16 | có (compiled) | CLI tính (§C.5.2) | |

Partition:

| Field | Kiểu | Bắt buộc | Ràng buộc |
|---|---|---|---|
| `id` | ident ≤ 20 ký tự | có | `seg_` + id ≤ 24 (tên user property Firebase) |
| `kind` | `"partition"` | có | |
| `default` | string ≤ 36 | có | không trùng `cases[].value` |
| `cases` | array ≥ 1 | có | `value` string ≤ 36, unique; `when` AST kiểu BOOL |

### C.1.5 Action [Chốt]

| Field | Kiểu | Bắt buộc (compiled) | Default nguồn | Ràng buộc |
|---|---|---|---|---|
| `id` | ident | có | | |
| `type` | enum 5 type §8.4 | có | | |
| `params` | object | có | | đúng bộ key theo `type` (bảng dưới); key lạ → lỗi |
| `group` | `reward` \| `difficulty` \| `offer` \| `message` \| `notification` | có | | `notification` ⇔ `type = SCHEDULE_LOCAL_NOTIFICATION` |
| `frequency` | `repeatable` \| `one_shot` | có | `repeatable` | `one_shot` cấm với `SCHEDULE_LOCAL_NOTIFICATION` |
| `cooldown_s` | int ≥ 0 | có | 0 | 0 = không cooldown |
| `cap` | object \| `false` | có | `false` | `false` = không cap; object `{ "count": int ≥ 1, "window_s": int ≥ 1 }` |

Params theo type:

| `type` | Key | Kiểu | Ràng buộc |
|---|---|---|---|
| `GIVE_REWARD` | `reward_id` | ident | ∈ `manifest.rewards` (CLI); client kiểm lúc load |
| | `amount` | int | 1..1000 |
| `SHOW_POPUP` | `popup_id` | ident | không có whitelist trong MVP; executor không biết id → `action_failed reason=unknown_id` |
| | `text_key` | string | tuỳ chọn; compiled ghi `""` nếu không có |
| `SHOW_OFFER` | `offer_id` | ident | như `popup_id` |
| `CHANGE_DIFFICULTY` | `delta` | int | −2..2, ≠ 0 |
| | `scope` | `"next_unit"` | enum một giá trị trong MVP |
| `SCHEDULE_LOCAL_NOTIFICATION` | `template_id` | ident | như `popup_id` |
| | `delay_h` | number | 0.5..168 |

### C.1.6 Rule [Chốt]

| Field | Kiểu | Bắt buộc (compiled) | Default nguồn | Ràng buộc |
|---|---|---|---|---|
| `id` | ident | có | | |
| `enabled` | bool | có | true | |
| `priority` | int | có | 0 | −1000..1000; tie-break `id` tăng dần |
| `on` | array ≥ 1 string | có | | mỗi phần tử `EVENT` hoặc `EVENT:qualifier` theo §8.1; unique |
| `fire` | `edge` \| `level` | có | `edge` | |
| `when` | AST | có | | kiểu BOOL; không `exp.*` |
| `then` | ident \| `"none"` | có | `"none"` | ident phải ∈ `actions[].id` |
| `valid_until` | ISO 8601 | có | | CLI cảnh báo nếu ≤ now |
| `def_hash` | hex16 | có (compiled) | CLI tính trên `{ on, when }` | |

### C.1.7 Experiment [Chốt]

| Field | Kiểu | Bắt buộc | Default nguồn | Ràng buộc |
|---|---|---|---|---|
| `id` | ident | có | | `len(id) + 1 + max(len(variant.id)) ≤ 36` (giá trị user property) |
| `layer` | ident ≤ 20 | có | | `exp_` + layer ≤ 24 |
| `allocation` | number | có | 1.0 | 0 < a ≤ 1; < 1 → cảnh báo |
| `ends_at` | ISO 8601 | có | | ≤ `valid_until` của mọi rule trong `rule_actions` |
| `variants` | array | có | | **đúng 2** trong MVP; `id` unique; `weight` > 0; tổng weight = 1 ± 1e-6 |
| `variants[].rule_actions` | object\<rule_id, action_id \| "none"\> | có | `{}` | rule_id ∈ `rules`, action ∈ `actions` |

Một `rule_id` chỉ được xuất hiện trong **một** experiment active trên toàn config, bất kể layer (§C.12 mục 3).

### C.1.8 Holdout và const [Chốt]

- `holdout: { "allocation": number }`, 0 ≤ a ≤ 1. MVP = 0.
- `const`: map ident → number. Mọi `const.<k>` được tham chiếu phải có key; key không được tham chiếu → CLI cảnh báo.

### C.1.9 Thứ tự validate ở client và reason [Chốt]

Dừng ở lỗi đầu tiên, log `config_rejected` với `reason`:

1. Parse JSON → `parse`
2. `schema_version` được hỗ trợ → `schema`
3. Field bắt buộc, kiểu, field lạ, enum, ràng buộc số → `schema`
4. `min_sdk_version` → `min_sdk`
5. `game_id`, `env` khớp `SdkOptions` → `game_env_mismatch`
6. Id unique; mọi `ref`, `then`, `rule_actions`, `params.reward_id`, `const.*` trỏ tới thứ tồn tại → `ref`
7. Type-check toàn AST (§C.2.2) → `type`
8. DAG formula ∪ segment không có vòng → `cycle`
9. AST sâu ≤ 32, ≤ 500 node mỗi expr → `limit`

Qua bước 9 → config chấp nhận. Sau đó, **từng rule** bị disable (không reject config) khi dùng thứ ngoài manifest hoặc không có chỗ lưu one-shot → `rule_unsupported` (§C.8).

---

## C.2 AST

### C.2.1 Node [Chốt]

Ba loại node, phân biệt bằng key có mặt; một node có đúng một trong ba bộ key:

```json
{ "op": "ADD", "args": [ node, node, ... ] }
{ "ref": "state.fail_count", "window": "7d" }
{ "value": 3 }            // number → NUMBER, true/false → BOOL, "..." → STRING
```

- `window` chỉ có trên node `ref`, chỉ với counter có window (§C.4), giá trị ∈ { `session`, `7d`, `30d` }. `ref` counter có window mà **không** có `window` = giá trị **lifetime**.
- `value` không được là `null`, array, object.

### C.2.2 Hệ kiểu [Chốt]

Bốn kiểu: `NUMBER`, `BOOL`, `STRING`, `TIMESTAMP`.

- **`TIMESTAMP` là alias của `NUMBER` trong type-check** (epoch giây). Nhãn riêng chỉ để tài liệu và lint: CLI **cảnh báo** khi `TIME_SINCE` / `DAYS_BETWEEN` nhận đối số không phải `ref` kiểu TIMESTAMP hoặc `NOW`.
- `BOOL` đứng ở vị trí cần `NUMBER` → hiểu là 0 / 1. **`NUMBER` đứng ở vị trí cần `BOOL` → lỗi** (không có truthiness).
- `STRING` chỉ xuất hiện ở: `ref` partition, `ref custom.*` kiểu STRING, `ref context.screen` / `context.last_event`, và `value` chuỗi; chỉ dùng được trong `EQ` / `NEQ` với một `STRING` khác.
- `EQ` / `NEQ`: hai vế cùng kiểu sau coercion (NUMBER ~ BOOL ~ TIMESTAMP so sánh số với epsilon 1e-9; STRING so ordinal).
- Kiểu của `formula.expr` ∈ { NUMBER, BOOL }. `when` / `enter` / `exit` / `cases[].when`: BOOL.

### C.2.3 Operator [Chốt]

| Op | Arity | Kiểu đối số → kết quả | Semantic biên |
|---|---|---|---|
| `ADD`, `MUL` | n ≥ 2 | NUMBER… → NUMBER | |
| `SUB` | 2 | NUMBER, NUMBER → NUMBER | |
| `DIV` | 2 | NUMBER, NUMBER → NUMBER | chia 0 → 0 |
| `MOD` | 2 | NUMBER, NUMBER → NUMBER | mod 0 → 0; dấu theo C# `%` (dấu của vế trái) |
| `GT` `GTE` `LT` `LTE` | 2 | NUMBER, NUMBER → BOOL | |
| `EQ` `NEQ` | 2 | cùng kiểu → BOOL | NUMBER: \|a − b\| ≤ 1e-9 |
| `AND`, `OR` | n ≥ 2 | BOOL… → BOOL | evaluate hết mọi arg (không short-circuit; không có side effect nên không khác kết quả) |
| `NOT` | 1 | BOOL → BOOL | |
| `IF` | 3 | BOOL, T, T → T | hai nhánh cùng kiểu sau coercion; T ∈ { NUMBER, BOOL } |
| `MIN`, `MAX` | n ≥ 2 | NUMBER… → NUMBER | |
| `ABS` | 1 | NUMBER → NUMBER | |
| `CLAMP` | 3 | x, lo, hi → NUMBER | `lo > hi` → trả `lo` (CLI cảnh báo khi cả hai là literal) |
| `NOW` | 0 | → TIMESTAMP | `now` đã hiệu chỉnh §4.4 |
| `TIME_SINCE` | 1 | TIMESTAMP → NUMBER | `max(0, now − ts)` (§C.12 mục 14); `ts = 0` → `now`, tức "rất lớn" |
| `DAYS_BETWEEN` | 2 | TIMESTAMP a, TIMESTAMP b → NUMBER | `utcDay(b) − utcDay(a)`, `utcDay(t) = floor(t / 86400)`; đọc là "từ a đến b", có dấu |

Số học trên double; không kiểm overflow; `NaN` / `Infinity` không phát sinh vì chia 0 đã chặn và không có `SQRT` / `LOG`.

### C.2.4 `ref` [Chốt]

Namespace và kiểu lấy từ bảng §C.4. Quy tắc:

- `state.*`, `context.*`: tên phải có trong bảng §C.4; `state.custom_event_count.<name>` với `<name>` ∈ `manifest.custom_events`.
- `feature.<id>`: id ∈ `formulas`. `segment.<id>`: id ∈ `segments`; tag → BOOL, partition → STRING.
- `custom.<key>`: key ∈ `manifest.custom_state`, kiểu theo manifest.
- `const.<key>`: key ∈ `config.const`, kiểu NUMBER.
- `exp.*`, `action.*`: **lỗi** ở CLI và client (`reason=ref`).
- `window` trên ref không phải counter có window → lỗi `type`.

### C.2.5 Giới hạn [Chốt]

Độ sâu ≤ 32 (node gốc = 1), ≤ 500 node mỗi `expr`. Đếm ở CLI và client giống nhau: mỗi object là một node.

---

## C.3 Grammar string expression [Chốt]

File nguồn viết `"expr": "<string>"`; CLI `compile` ra AST §C.2. Client không có parser.

### C.3.1 EBNF

```
expr      := or_expr
or_expr   := and_expr { "or" and_expr }
and_expr  := not_expr { "and" not_expr }
not_expr  := "not" not_expr | cmp_expr
cmp_expr  := add_expr [ cmp_op add_expr ]          ; không kết chuỗi: a < b < c là lỗi
cmp_op    := "==" | "!=" | "<" | "<=" | ">" | ">="
add_expr  := mul_expr { ("+" | "-") mul_expr }
mul_expr  := unary { ("*" | "/" | "%") unary }
unary     := "-" unary | primary
primary   := number | string | "true" | "false" | ref | call | "(" expr ")"
call      := func "(" [ expr { "," expr } ] ")"
func      := "if" | "min" | "max" | "abs" | "clamp" | "now" | "time_since" | "days_between"
ref       := ns "." ident { "." ident } [ "@" window ]
ns        := "state" | "feature" | "segment" | "context" | "custom" | "const"
window    := "session" | "7d" | "30d"
ident     := [a-z_] [a-z0-9_]*
number    := digit+ [ "." digit+ ] [ ("e" | "E") ["+" | "-"] digit+ ]
string    := '"' { any-char-except-quote-backslash | '\"' | '\\' } '"'
comment   := "#" { any-char } end-of-line              ; bỏ qua
```

- Từ khoá `and or not true false` và tên hàm là **chữ thường, phân biệt hoa thường**. Không có alias `&& || ! =`: gặp là lỗi cú pháp kèm gợi ý.
- Khoảng trắng và xuống dòng tự do. Chuỗi không có escape khác ngoài `\"` và `\\`.

### C.3.2 Precedence (thấp → cao)

`or` < `and` < `not` < so sánh < `+ -` < `* / %` < `-` một ngôi < primary. Cùng mức: trái sang phải.

### C.3.3 Ánh xạ sang AST

| Nguồn | AST |
|---|---|
| `a + b + c`, `a * b * c`, `a and b and c`, `a or b or c` | **phẳng**: một node `ADD` / `MUL` / `AND` / `OR` với 3 args |
| `a - b - c`, `a / b / c`, `a % b % c` | lồng trái: `SUB(SUB(a, b), c)` |
| `-3`, `-0.5` | literal gộp: `{ "value": -3 }` |
| `-x` (không phải literal) | `SUB({ "value": 0 }, x)` |
| `==` `!=` `<` `<=` `>` `>=` | `EQ NEQ LT LTE GT GTE` |
| `if(c, a, b)` | `IF` 3 args; `min(a, b, c)` → `MIN` 3 args; `now()` → `{ "op": "NOW", "args": [] }` |
| `state.fail_count@7d` | `{ "ref": "state.fail_count", "window": "7d" }` |
| `"at_risk"` | `{ "value": "at_risk" }` |

Sai arity hàm, ref sai namespace, window trên ref không hợp lệ → lỗi **compile** với vị trí cột; type-check chạy sau compile trên AST (§C.1.9 bước 7) để CLI và client dùng chung một bộ kiểm.

### C.3.4 Ví dụ

```
state.fail_count@7d / max(state.attempt_count@7d, 1)
segment.frustrated and segment.lifecycle == "at_risk" and state.fail_streak >= 3
if(state.days_since_install >= 7, state.playtime_s@7d > const.playtime_7d_p90, false)
time_since(state.install_at) < 86400
not segment.stuck or custom.hint_used > 0
```

---

## C.4 State model: bảng kiểu và cách lưu

### C.4.1 Bảng ref [Chốt]

W = counter có window (lifetime + bucket 30 ngày + counter session). S = `Seed()` ghi được. D = derived, tính lúc evaluate, không lưu.

| Ref | Kiểu | W | S | D | Ghi chú |
|---|---|---|---|---|---|
| `state.install_at` | TIMESTAMP | | S | | |
| `state.days_since_install` | NUMBER | | | D | `utcDay(now) − utcDay(install_at)`; `install_at = 0` → 0 |
| `state.session_count` | NUMBER | W | S | | seed ghi lifetime |
| `state.days_since_last_active` | NUMBER | | | | set tại `SESSION_START`, giữ hết session |
| `state.last_active_at` | TIMESTAMP | | S | | |
| `state.seeded` | BOOL | | | | |
| `state.seeded_at` | TIMESTAMP | | | | |
| `state.playtime_s` | NUMBER | W | | | double |
| `state.progress.current` | NUMBER | | | | |
| `state.progress.max` | NUMBER | | S | | |
| `state.attempt_count` | NUMBER | W | | | |
| `state.complete_count` | NUMBER | W | | | |
| `state.attempt_count_current_unit` | NUMBER | | | | |
| `state.fail_count` | NUMBER | W | | | |
| `state.fail_streak` | NUMBER | | | | |
| `state.win_streak` | NUMBER | | | | |
| `state.quit_count` | NUMBER | W | | | |
| `state.quit_after_fail_count` | NUMBER | | | | không window (§5.2) |
| `state.last_fail_at` | TIMESTAMP | | | | |
| `state.purchase_count` | NUMBER | W | S | | |
| `state.total_spend_usd` | NUMBER | | S | | |
| `state.first_purchase_at` | TIMESTAMP | | S | | |
| `state.last_purchase_at` | TIMESTAMP | | S | | |
| `state.ad_rewarded_count` | NUMBER | W | | | |
| `state.ad_interstitial_count` | NUMBER | W | | | |
| `state.custom_event_count.<name>` | NUMBER | W | | | `<name>` ∈ manifest |
| `context.screen` | STRING | | | | `SCREEN_OPEN` cuối |
| `context.last_event` | STRING | | | | `EVENT` hoặc `CUSTOM_EVENT:<name>` |
| `context.session_time_s` | NUMBER | | | D | giây foreground session hiện tại |
| `context.actions_shown_today` | NUMBER | | | | persist kèm ngày UTC |
| `context.clock_suspect` | BOOL | | | | |
| `context.config_stale` | BOOL | | | | |
| `feature.<id>` | NUMBER \| BOOL | | | D | kiểu suy từ `expr` |
| `segment.<tag>` | BOOL | | | | giá trị đã persist (hysteresis) |
| `segment.<partition>` | STRING | | | D | |
| `custom.<key>` | theo manifest | | | | NUMBER \| BOOL \| STRING |
| `const.<key>` | NUMBER | | | | |

### C.4.2 Counter có window [Chốt]

Mỗi counter W lưu **ba phần**:

```json
"fail_count": { "life": 120, "session": 3, "head_day": 20720, "days": [3, 0, 1, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] }
```

- `life`: tổng lifetime; là giá trị của `ref` không có `window`; `Seed()` chỉ ghi vào đây.
- `session`: reset về 0 tại `SESSION_START`.
- `days[0]` là bucket của ngày `head_day` (= `utcDay`), `days[i]` là ngày `head_day − i`. Luôn đúng 30 phần tử.
- `@7d = Σ days[0..6]`, `@30d = Σ days[0..29]`, `@session = session`.
- Tăng `x`: `life += x; session += x; days[0] += x`.

**Rollover**, chạy **trước reducer** ở mỗi event, một lần cho mọi counter (§C.12 mục 9):

```
d = utcDay(now)
if d > head_day:  shift = min(d − head_day, 30); days = [0]*shift ++ days[0 .. 30−shift); head_day = d
if d < head_day:  không shift, ghi vào days[0]   (đồng hồ chạy ngược; clock_suspect đã bật ở §4.4)
```

Counter mới xuất hiện (custom event vừa thêm vào manifest) → tạo với `head_day = utcDay(now)`, toàn 0.

---

## C.5 Hash, id, ngẫu nhiên

### C.5.1 Hash assignment và holdout [Chốt]

```
bucket(user_id, key) = uint32_BE( SHA256( UTF8( user_id + ":" + key ) )[0..3] ) mod 10000
```

- Experiment: `key = experiment_id`. Holdout: `key = "holdout"`.
- Holdout: `bucket < holdout.allocation × 10000` → `exp.holdout = true`.
- Experiment: `bucket ≥ allocation × 10000` → ngoài allocation, không assign. Trong allocation: `u = bucket / (allocation × 10000)` ∈ [0, 1); chọn variant đầu tiên theo thứ tự khai có `cum_weight > u` (`cum_weight` = tổng weight tới và gồm variant đó).
- Chọn SHA-256 thay murmur vì có sẵn trong C#, BigQuery, Python, không cần thư viện; gọi một lần mỗi experiment lúc load config nên chi phí không đáng kể. Tái tính trên BigQuery:

```sql
MOD( CAST( CONCAT('0x', SUBSTR(TO_HEX(SHA256(CONCAT(user_id, ':', 'booster_when_frustrated_v1'))), 1, 8)) AS INT64 ), 10000 )
```

### C.5.2 `def_hash` cho hysteresis và edge state [Chốt]

Client **không** canonicalize JSON. CLI tính lúc compile và ghi vào config:

- Tag: `def_hash = hex16( SHA256( canonical({ "enter": enter, "exit": exit }) ) )`.
- Rule: `def_hash = hex16( SHA256( canonical({ "on": sorted(on), "when": when }) ) )`.
- `canonical` = JSON không whitespace, key sắp theo ordinal, số in dạng round-trip ngắn nhất (`double.ToString("R")`, invariant), chuỗi escape tối thiểu. Chỉ CLI làm; client so sánh chuỗi `def_hash` đã persist với chuỗi trong config; khác → reset về `false`.
- `hex16` = 16 ký tự hex thường của 8 byte đầu.

### C.5.3 `user_id`, `execution_id`, sample [Chốt / Đề xuất]

- **[Chốt] `user_id`:** GUID v4, `Guid.NewGuid().ToString("N")` → 32 hex thường; sinh lần đầu Initialize khi file SDK trống; persist trong file SDK.
- **[Đề xuất] mirror `user_id` vào blob lịch sử** dưới key `uid` (§C.10.3). Lúc init, file SDK trống mà blob có `uid` → dùng lại `uid` đó. Assignment theo đó sống qua reinstall khi save game còn. Chờ câu hỏi §12.5 (1); nếu identity là account id thì thay `uid` bằng account id.
- **[Chốt] `execution_id`:** `exec_seq` là uint32 persist trong file SDK, tăng 1 mỗi lần `selected`; `execution_id = exec_seq.ToString("x8")`. Không va chạm trong cùng `user_id`; file SDK mất thì `user_id` cũng mất nên cặp `(user_id, execution_id)` vẫn unique. Không dùng random.
- **[Chốt] sample `seg_decision`:** `System.Random` mặc định, `NextDouble() < 0.05` mỗi trigger không match; param `sampled` luôn có, `1` khi là mẫu, `0` khi là trigger có match.

---

## C.6 SDK API

### C.6.1 Hai tầng [Chốt]

| Tầng | Assembly | Phụ thuộc | Ai dùng |
|---|---|---|---|
| **Engine core** | `Seg.Engine` (netstandard2.1, C# thuần) | không Unity | SDK Unity, CLI, test |
| **Unity SDK** | `Seg.Unity` | UnityEngine, `Seg.Engine` | game |

Engine core nhận mọi thứ qua interface (§C.6.4). Unity SDK cung cấp implementation mặc định và API tĩnh bên dưới.

### C.6.2 Public API cho game (Unity) [Chốt]

```csharp
namespace Seg
{
    public static class SegmentationSdk
    {
        // Khởi tạo — gọi một lần, main thread, càng sớm càng tốt sau khi game biết GameId / Env.
        public static void Initialize(SdkOptions options);
        public static bool IsInitialized { get; }
        public static string SdkVersion { get; }          // "1.0.0"

        // Executor — đăng ký TRƯỚC Initialize (manifest.actions suy từ đây lúc load config).
        // Đăng ký sau Initialize: có hiệu lực từ lần load config kế; dev build cảnh báo.
        public static void RegisterExecutor(IActionExecutor executor);

        // Seed (§4.6) và lịch sử action (§4.5)
        public static bool NeedsSeed { get; }
        public static void Seed(SeedData data);
        public static void ImportActionHistory(string blob);
        public static void MarkActionUsed(string actionId, long atEpochSeconds);

        // Event chuẩn (§4.1). Kiểu tham số là contract; SDK đổi sang double nội bộ.
        public static void ProgressStart(long unitId);
        public static void ProgressComplete(long unitId, double durationS);
        public static void ProgressFail(long unitId, double durationS);
        public static void ProgressQuit(long unitId, double durationS);
        public static void Purchase(string productId, double usd, string transactionId, string executionId = null);
        public static void AdRewarded(string placement);
        public static void AdInterstitial(string placement);
        public static void ScreenOpen(string screenId);
        public static void CustomEvent(string name);
        public static void SetCustomState(string key, double value);
        public static void SetCustomState(string key, bool value);
        public static void SetCustomState(string key, string value);

        // Manifest (§8.5) — chuỗi JSON để paste vào repo config
        public static string ExportManifestJson();

        // Debug overlay (§11.3) — chỉ dev build; release trả null
        public static ISdkDebug Debug { get; }
    }
}
```

- `SESSION_START` / `SESSION_END` không có API public: SDK tự emit theo §4.4.
- Pause / resume / quit: Unity SDK tự bắt qua một `MonoBehaviour` ẩn `DontDestroyOnLoad` (`OnApplicationPause`, `OnApplicationQuit`, `Update` để tick timeout fetch và drain queue). Game **không** phải gọi gì. Engine core exposes `Engine.OnPause(now)`, `OnResume(now)`, `OnQuit(now)`, `Tick(now)` cho CLI và test.
- Gọi bất kỳ API event trước `Initialize` → bỏ, dev build log lỗi.

### C.6.3 `SdkOptions` [Chốt]

| Field | Kiểu | Bắt buộc | Default | Ghi chú |
|---|---|---|---|---|
| `GameId` | string | có | | khớp `config.game_id` |
| `Env` | string | có | | `prod` / `dev` / `staging` |
| `ConfigBaseUrl` | string | có | | `https://…/config`; SDK thêm `?game=&env=` |
| `ConfigToken` | string | khi `Env ≠ prod` | null | header `X-Config-Token`; **không** đưa vào release build |
| `Tracking` | `ITrackingSink` | có | | adapter sang Firebase / AppsFlyer / SDK riêng |
| `ActionHistoryStore` | `IActionHistoryStore` | không | null | null → mọi rule trỏ action `one_shot` bị `rule_unsupported` |
| `SeedProvider` | `Func<SeedData>` | không | null | gọi trước `SESSION_START` đầu khi `NeedsSeed`; trả null = chưa có |
| `Rewards` | `string[]` | không | `[]` | manifest.rewards |
| `Screens` | `string[]` | không | `[]` | manifest.screens |
| `CustomEvents` | `string[]` | không | `[]` | ≤ 10 |
| `CustomState` | `Dictionary<string, CustomType>` | không | `{}` | `CustomType ∈ { Number, Bool, String }` |
| `FetchTimeoutMs` | int | không | 3000 | |
| `RefetchAfterResumeS` | int | không | 300 | §10.2 |
| `Storage` | `IStateStorage` | không | file trong `Application.persistentDataPath/seg/` | |
| `Fetcher` | `IConfigFetcher` | không | `UnityWebRequest` | |
| `Clock` | `ITimeSource` | không | `DateTime.UtcNow` + `Stopwatch` | |
| `Logger` | `ILogger` | không | `UnityEngine.Debug` | |
| `DebugBuild` | bool | không | `Debug.isDebugBuild` | bật overlay, log 100%, assert thread |

### C.6.4 Adapter interface (engine core) [Chốt]

```csharp
public interface ITrackingSink
{
    // params: giá trị chỉ là long, double, string (bool đã đổi thành long 0/1 trước khi tới đây)
    void LogEvent(string name, IReadOnlyDictionary<string, object> parameters);
    void SetUserProperty(string name, string value);   // value == null → xoá property
}

public interface IConfigFetcher
{
    // Trả về khi có response, lỗi mạng, hoặc hết timeout. Không retry bên trong.
    Task<FetchResult> FetchAsync(string url, IReadOnlyDictionary<string, string> headers, int timeoutMs, CancellationToken ct);
}
public readonly struct FetchResult { public int StatusCode; public string Body; public string Error; }   // Error != null ⇒ lỗi mạng / timeout

public interface IStateStorage
{
    string Read(string key);                       // null nếu không có
    void WriteAtomic(string key, string content);  // tmp + rename
    void Delete(string key);
}
// key dùng: "state" (§C.10.1), "config_cache" (§C.10.2)

public interface ITimeSource
{
    long DeviceUtcNowSeconds();      // đồng hồ thiết bị, chưa hiệu chỉnh
    double MonotonicSeconds();       // chỉ để đối chiếu trong foreground (§4.4)
}

public interface ILogger { void Log(LogLevel level, string message); }
public enum LogLevel { Debug, Info, Warn, Error }

public interface IActionHistoryStore { string Load(); void Save(string blob); }   // §4.5, giữ nguyên
```

### C.6.5 Executor contract [Chốt]

```csharp
public interface IActionExecutor
{
    ActionType ActionType { get; }
    void Execute(ActionRequest request);      // main thread, ngay sau resolver
}

public enum ActionType { GIVE_REWARD, SHOW_POPUP, SHOW_OFFER, CHANGE_DIFFICULTY, SCHEDULE_LOCAL_NOTIFICATION }

public sealed class ActionRequest
{
    public string ActionId { get; }
    public string ExecutionId { get; }            // 8 hex
    public ActionType Type { get; }
    public ActionParams Params { get; }
    public string RuleId { get; }
    public int Nth { get; }                       // lần thứ mấy user nhận action này (theo lịch sử)

    public void ReportPresented();
    public void ReportExecuted(string result);    // result ∈ bộ giá trị §C.8.6 theo Type
    public void ReportFailed(string reason);      // reason ∈ §C.8.4
}

public sealed class ActionParams            // typed view + raw
{
    public string RewardId; public int Amount;                    // GIVE_REWARD
    public string PopupId;  public string TextKey;                // SHOW_POPUP
    public string OfferId;                                        // SHOW_OFFER
    public int Delta;       public string Scope;                  // CHANGE_DIFFICULTY
    public string TemplateId; public double DelayH;               // SCHEDULE_LOCAL_NOTIFICATION
    public IReadOnlyDictionary<string, object> Raw { get; }
}
```

Semantic báo cáo (§C.12 mục 7):

| Tình huống | SDK làm gì |
|---|---|
| `ReportExecuted` hoặc `ReportFailed` lần đầu | ghi tracking; đóng request |
| Báo terminal lần thứ hai (bất kỳ loại) | bỏ; dev build `Warn` |
| `ReportPresented` sau khi đã terminal | bỏ; dev build `Warn` |
| `ReportPresented` hai lần | lần hai bỏ |
| `ReportFailed` không có `Presented` trước | hợp lệ (fail sớm) |
| `result` ngoài bộ giá trị | ghi `other`; dev build `Warn` |
| `reason` ngoài enum | ghi `other`; **không** hoàn cooldown / cap / one-shot |
| Hết session mà không terminal | không có event; DWH thấy `selected` không có `executed` (§8.6) |
| `Execute` ném exception | SDK bắt, `ReportFailed("exception")` thay executor |

### C.6.6 Thread và lifecycle [Chốt]

- **Mọi API public và mọi callback vào executor chạy trên main thread Unity.** Gọi từ thread khác: dev build ném `InvalidOperationException`; release build bỏ lời gọi và log `Error` một lần mỗi session. SDK không tự marshal.
- Ghi file state: serialize main thread, ghi ở một worker thread duy nhất, chỉ giữ bản mới nhất (§4.5). Pause / quit ghi đồng bộ.
- `IActionHistoryStore.Save` gọi đồng bộ trên main thread.
- `ITrackingSink.LogEvent` gọi trên main thread; adapter tự lo nếu SDK tracking cần thread khác.

### C.6.7 Manifest [Chốt]

`ExportManifestJson()` trả đúng schema §8.5: `sdk` = `SdkVersion`; `actions` = danh sách `ActionType` của executor đã đăng ký, sắp theo tên; `rewards` / `screens` / `custom_events` sắp theo tên; `custom_state` key sắp, giá trị `"NUMBER" | "BOOL" | "STRING"`. Deterministic để diff trong PR có nghĩa.

---

## C.7 Tracking: format param

### C.7.1 Quy tắc mã hoá [Chốt]

- Kiểu param gửi vào `ITrackingSink`: `long`, `double`, `string`. **Bool → long 0/1.**
- Danh sách → một chuỗi, phần tử nối bằng `,`; cặp nối bằng `:` (`action_id:execution_id`, `action_id:reason`); `key=value` dùng `=`; danh sách experiment nối bằng `;` (`exp_id:variant;exp_id:variant`).
- Không escape: mọi id là ident (`[a-z0-9_]`) nên không chứa ký tự nối.
- Thứ tự: `rules_matched` theo thứ tự evaluate (priority giảm, id tăng); `selected` theo thứ tự giao executor; `dropped` theo thứ tự bị loại trong resolver; `segments` = partition theo thứ tự khai (`id=value`), rồi tag đang `true` theo thứ tự khai; `features` theo thứ tự khai, giá trị NUMBER làm tròn 2 chữ số `F2` invariant, BOOL là `1` / `0`.
- **Cắt 100 ký tự** (release build): nếu chuỗi > 100, cắt tại ký tự nối cuối cùng trước vị trí 100 để không có phần tử cụt; phần tử đầu đã > 100 → cắt cứng tại 100. Dev build không cắt.
- `trigger`: `EVENT` hoặc `EVENT:qualifier`, ví dụ `SCREEN_OPEN:result`.

### C.7.2 Param từng event [Chốt, trừ chỗ đánh dấu]

`seg_snapshot` (tại `SESSION_START`, luôn bắn kể cả `enabled = false` hay `config_source = none`, §C.12 mục 5):

| Param | Kiểu | Ghi chú |
|---|---|---|
| `user_id` | string 32 | |
| `config_version` | long | 0 khi `config_source = none` |
| `config_source` | string | `fresh` / `cache` / `none` |
| `sdk_version` | string | |
| `seeded` | long 0/1 | |
| `holdout` | long 0/1 | |
| `engine_enabled` | long 0/1 | **[Đề xuất]** `config.enabled`; để dashboard tách kill-switch với lỗi |
| `seg_<partition_id>` | string | một param mỗi partition; rỗng khi không có config |
| `tags` | string | tag đang `true`, nối `,` |
| `exps` | string | `exp_id:variant;…` |
| `rules_matched` | string | của trigger `SESSION_START` |
| `selected` | string | `action_id:execution_id,…` |
| `dropped` | string | `action_id:reason,…` |

Với 3 partition: 16 param, dưới trần 25. Lint: partition ≤ 8.

`seg_decision`:

| Param | Kiểu |
|---|---|
| `trigger` | string |
| `rules_matched` | string |
| `selected` | string |
| `dropped` | string |
| `segments` | string |
| `features` | string |
| `config_version` | long |
| `eval_ms` | long |
| `sampled` | long 0/1 |

`action_*`:

| Event | Param |
|---|---|
| `action_selected` | `action_id`, `execution_id`, `rule_id`, `group`, `nth` (long), `config_version` (long) **[Đề xuất thêm `config_version`]** |
| `action_presented` | `execution_id`, `action_id` **[Đề xuất thêm `action_id`]** |
| `action_executed` | `execution_id`, `action_id` **[Đề xuất]**, `result` |
| `action_failed` | `execution_id`, `action_id` **[Đề xuất]**, `reason` |

Lý do đề xuất thêm `action_id`: nguyên tắc §11 "mỗi event đủ để phân tích một mình"; execution ratio theo action không cần join.

Còn lại:

| Event | Param |
|---|---|
| `exp_exposure` | `exp_id`, `variant`, `rule_id`, `history_used` (long 0/1), `trigger` **[Đề xuất thêm]** |
| `config_rejected` | `version` (long), `reason`, `sdk_version` |
| `rule_unsupported` | `rule_id`, `reason`, `config_version` (long) |
| `engine_error` | `stage`, `message` (≤ 100) |
| `state_reset` | `reason` |

### C.7.3 User property và giới hạn Firebase [Chốt]

| Property | Giá trị | Giới hạn |
|---|---|---|
| `seg_<partition_id>` | giá trị partition | tên ≤ 24 → `partition_id` ≤ 20; giá trị ≤ 36 |
| `exp_<layer>` | `<exp_id>:<variant>`; null khi prune | tên ≤ 24 → `layer` ≤ 20; giá trị ≤ 36 → `len(exp_id) + 1 + len(variant_id) ≤ 36` |
| `seg_holdout` | `"1"` hoặc null | |

Bốn giới hạn trên là **lỗi** ở CLI `validate`. Ví dụ hiện có trong tài liệu chính: `booster_when_frustrated_v1:control` = 34 ký tự, đạt nhưng sát trần.

---

## C.8 Enum đóng [Chốt]

### C.8.1 `config_rejected.reason`
`parse` · `schema` · `min_sdk` · `game_env_mismatch` · `ref` · `type` · `cycle` · `limit`

### C.8.2 `rule_unsupported.reason`
`action_type` (không có executor) · `reward` · `screen` (qualifier trong `on`) · `custom_event` · `custom_state` · `one_shot_no_store` · `expired` (**[Đề xuất]**, §C.12 mục 2)

### C.8.3 `engine_error.stage`
`config_load` · `rollover` · `reducer` · `formula` · `segment` · `rule` · `resolver` · `executor` · `tracking` · `persist`

### C.8.4 `action_failed.reason`
`offline` · `no_permission` · `exception` · `unknown_id` · `invalid_params` · `not_available` · `other`
Chỉ `offline` và `no_permission` hoàn cooldown / cap / one-shot (§8.3).

### C.8.5 Khác
- `dropped` reason: `one_shot` · `cooldown` · `cap` · `group` · `daily_cap`
- `state_reset.reason`: `parse` · `schema` · `history`
- `config_source`: `fresh` · `cache` · `none`
- `context.last_event`: tên event §4.1, hoặc `CUSTOM_EVENT:<name>`

### C.8.6 `action_executed.result` theo type (từ §13.3, cộng `other`)

| Type | result |
|---|---|
| `GIVE_REWARD` | `granted` · `inventory_full` |
| `SHOW_POPUP` | `clicked` · `dismissed` · `timeout` |
| `SHOW_OFFER` | `purchased` · `closed` · `not_loaded` |
| `CHANGE_DIFFICULTY` | `applied` · `no_unit_pending` |
| `SCHEDULE_LOCAL_NOTIFICATION` | `scheduled` · `replaced` |
| mọi type | `other` (SDK ghi khi giá trị lạ) |

---

## C.9 Repo config và kế thừa

### C.9.1 Layout [Chốt]

```
segmentation-config/
  base/
    const.json                     { "playtime_7d_p50": 1800, ... }
    formulas/<id>.json             một entity mỗi file, tên file = id
    segments/<id>.json
    actions/<id>.json
  games/<game_id>/
    manifest.json                  SDK export (§8.5), không viết tay
    game.json                      top-level: min_sdk_version, session_timeout_s, max_actions_per_day, holdout, extends
    const.json
    formulas/  segments/  actions/ override theo id
    rules/<id>.json                chỉ ở game
    experiments/<id>.json          chỉ ở game
    env/<env>.json                 overlay theo env (§C.9.3)
    fixtures/*.json                §C.10.4
    tests/*.json                   test vector §C.10.5
```

`game.json`:

```json
{ "extends": ["base"], "min_sdk_version": "1.0.0", "session_timeout_s": 1800, "max_actions_per_day": 3, "holdout": { "allocation": 0.0 } }
```

`extends` là danh sách thư mục dưới `base/` áp theo thứ tự (MVP chỉ có `["base"]`; Phase 2 thêm `base/genre_puzzle`).

### C.9.2 Quy tắc merge [Chốt]

| Loại | Quy tắc |
|---|---|
| `formulas` / `segments` / `actions` | **Thay cả object theo `id`.** Game có file cùng id → object của game thay hoàn toàn object của base; không deep merge field. |
| Xoá entity của base | File ở game: `{ "id": "<id>", "remove": true }`. CLI lỗi nếu entity bị xoá còn được tham chiếu. |
| `const` | Merge theo key; key game ghi đè key base. |
| `rules` / `experiments` | Chỉ có ở game; base có thư mục này → lỗi. |
| Top-level scalar | `game.json` ghi đè default nguồn (§C.1.2). |
| Không prune | Mọi entity sau merge đều vào compiled config; entity không được tham chiếu → cảnh báo, không lỗi (partition luôn có nghĩa vì lên user property). |

### C.9.3 Overlay theo env [Chốt]

`env/<env>.json` chỉ được chứa: `enabled`, `holdout`, `session_timeout_s`, `max_actions_per_day`, và `experiments` (array, thay theo id). Field khác → lỗi. Không có file → env dùng nguyên game. Mục đích: dev bật experiment sớm hoặc `enabled: false` mà không tách repo.

### C.9.4 Pipeline compile [Chốt]

```
đọc base theo extends → áp game override → áp env overlay → điền default nguồn
→ compile expr string → AST (§C.3) → tính def_hash (§C.5.2) → validate (§C.1.9 + lint §10.3 + §C.7.3)
→ gán version, published_at, source_commit → JSON compiled một dòng → publish (§C.11.3)
```

CI chạy tới `validate` rồi `simulate` mọi fixture và chạy mọi test vector; `publish` chạy tay hoặc từ job bảo vệ trên `main`.

---

## C.10 Persist, fixture, test vector, replay

### C.10.1 File state SDK (`key = "state"`) [Chốt]

```json
{
  "schema_version": 1,
  "user_id": "3f2a9c…",
  "created_at": 1759000000,
  "exec_seq": 26,
  "seeded": true, "seeded_at": 1759000100,
  "clock": { "offset_s": -3, "has_offset": true, "last_event_at": 1759287000, "last_pause_at": 1759286000, "foreground_anchor": 1759287000 },
  "session": { "open": true, "started_at": 1759286500, "days_since_last_active": 0 },
  "scalars": {
    "install_at": 1750000000, "last_active_at": 1759286500,
    "progress_current": 41, "progress_max": 41, "attempt_count_current_unit": 2,
    "fail_streak": 2, "win_streak": 0, "quit_after_fail_count": 3, "last_fail_at": 1759286900,
    "total_spend_usd": 9.99, "first_purchase_at": 1752000000, "last_purchase_at": 1752000000
  },
  "counters": {
    "fail_count": { "life": 120, "session": 3, "head_day": 20362, "days": [3,0,1,0,0,2,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0] },
    "custom_event_count.upgrade": { "life": 5, "session": 0, "head_day": 20362, "days": [0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0] }
  },
  "custom": { "hint_used": 3, "vip": false },
  "context": { "screen": "result", "last_event": "PROGRESS_FAIL", "clock_suspect": false, "config_stale": false },
  "shown_today": { "day": 20362, "count": 1 },
  "tags": { "frustrated": { "value": true, "def_hash": "9f1c0a7b3e5d2c48" } },
  "edges": { "booster_when_frustrated": { "prev": false, "def_hash": "0a3b5c7d9e1f2233" } },
  "action_ts": { "give_booster_small": [ 1759200000 ] },
  "assignments": { "gameplay_help": { "exp_id": "booster_when_frustrated_v1", "variant": "control", "at": 1759000000 } },
  "holdout": { "decided": true, "value": false },
  "exposed": [ "booster_when_frustrated_v1" ],
  "tx_ring": [ "GPA.3301-1234-5678-90123" ],
  "last_offer": { "execution_id": "0000001a", "offer_id": "starter_pack", "at": 1759100000 },
  "history_mirror": { "v": 1, "actions": { "starter_pack_offer": { "n": 1, "f": 1759100000, "l": 1759100000 } } }
}
```

- `counters` chỉ chứa counter W của §C.4.1; `custom_event_count.<name>` là key phẳng với dấu chấm.
- `action_ts[action_id]`: danh sách epoch `selected`, giữ tối đa `cap.count` mốc gần nhất (1 mốc nếu không cap, để tính cooldown).
- Migration: `schema_version` cũ hơn → thêm field thiếu bằng default; mới hơn SDK → `state_reset reason=schema`.
- Ghi atomic: `state.json.tmp` → `File.Replace`/rename.

### C.10.2 Cache config (`key = "config_cache"`) [Chốt]

```json
{ "fetched_at": 1759287600, "server_time": 1759287600, "config": { "…": "config đã chấp nhận" } }
```

`context.config_stale = (now − fetched_at) > 604800`.

### C.10.3 Blob lịch sử action (player data) [Chốt + Đề xuất]

```json
{ "v": 1, "uid": "3f2a9c…", "actions": { "starter_pack_offer": { "n": 1, "f": 1759100000, "l": 1759100000 } } }
```

`uid` là **[Đề xuất]** §C.5.3; các field còn lại theo §4.5. Merge không đụng `uid`: bản đã có `uid` giữ, bản trống lấy của bên kia.

### C.10.4 Fixture cho `simulate` [Chốt]

Fixture = **file state §C.10.1 rút gọn**: mọi field tuỳ chọn, thiếu → default; thêm `now` (epoch) để engine không dùng đồng hồ máy; `user_id` mặc định `"fixture"`.

```json
{ "now": 1759287600, "scalars": { "install_at": 1750000000, "fail_streak": 3 },
  "counters": { "fail_count": { "life": 12, "session": 3, "days": [3,2,1] } },
  "tags": { "frustrated": { "value": true } }, "context": { "screen": "result" } }
```

`days` thiếu phần tử → điền 0 tới 30; `head_day` thiếu → `utcDay(now)`; `def_hash` thiếu → lấy từ config đang simulate (coi là khớp).

### C.10.5 Test vector [Chốt]

Một file = một kịch bản; engine core chạy trong CI của SDK và của repo config.

```json
{
  "name": "edge fires once on third fail at result screen",
  "config": "fixtures/config_min.json",
  "fixture": { "now": 1759287600 },
  "manifest": { "actions": ["GIVE_REWARD"], "rewards": ["booster_hammer"], "screens": ["result"], "custom_events": [], "custom_state": {} },
  "variant": { "booster_when_frustrated_v1": "booster" },
  "events": [
    { "at": 1759287600, "type": "SESSION_START" },
    { "at": 1759287610, "type": "PROGRESS_START", "unit_id": 41 },
    { "at": 1759287640, "type": "PROGRESS_FAIL",  "unit_id": 41, "duration_s": 30 },
    { "at": 1759287641, "type": "SCREEN_OPEN",    "screen_id": "result" }
  ],
  "expect": {
    "state":     { "scalars.fail_streak": 1, "counters.fail_count.session": 1 },
    "features":  { "fail_rate": 1.0 },
    "segments":  { "frustrated": false, "lifecycle": "active" },
    "decisions": [ { "trigger": "SCREEN_OPEN:result", "rules_matched": [], "selected": [], "dropped": [] } ],
    "tracking":  [ { "name": "seg_snapshot" }, { "name": "seg_decision", "params": { "trigger": "SCREEN_OPEN:result" } } ],
    "executor":  [ ]
  }
}
```

- `expect.state` dùng đường dẫn chấm vào file state; so số với epsilon 1e-6.
- `expect.decisions` là danh sách theo thứ tự trigger; chỉ trigger có trong danh sách mới bị kiểm.
- `expect.tracking`: so theo tên và tập param khai; param không khai không kiểm. `expect.executor`: danh sách `{ action_id, type, params }` theo thứ tự `Execute`; test harness tự `ReportExecuted("granted")` trừ khi vector ghi `"report": "failed:offline"`.
- `variant`: ép assignment thay cho hash, để test không phụ thuộc `user_id`.

### C.10.6 Replay (`sample.jsonl`) [Chốt]

Một dòng một event, sắp theo `uid` rồi `at`:

```
{"uid":"u1","at":1759000000,"type":"SEED","payload":{"InstallAt":1750000000,"PurchaseCount":2,"TotalSpendUsd":9.99,"ProgressMax":40}}
{"uid":"u1","at":1759000005,"type":"SESSION_START"}
{"uid":"u1","at":1759000060,"type":"PROGRESS_FAIL","payload":{"unit_id":41,"duration_s":55}}
{"uid":"u1","at":1759000061,"type":"SCREEN_OPEN","payload":{"screen_id":"result"}}
{"uid":"u1","at":1759003700,"type":"PAUSE"}
```

- `SEED` và `PAUSE` / `RESUME` / `QUIT` là pseudo-event của replay, không phải event chuẩn. `SESSION_START` / `SESSION_END` để engine tự sinh từ `PAUSE` / `RESUME` theo `session_timeout_s`; dòng `SESSION_START` tường minh vẫn được chấp nhận và bỏ qua nếu session đã mở.
- Đầu ra: fire rate mỗi rule (`rules_matched` / tổng trigger cùng `on`), phân bố segment cuối mỗi user, `dropped` theo reason, số `exp_exposure` mỗi nhánh nếu có `--variant`.

---

## C.11 Worker `/config` [Chốt]

### C.11.1 Request

```
GET {ConfigBaseUrl}?game=<game_id>&env=<env>
X-Config-Token: <token>        chỉ khi env ≠ prod
Accept-Encoding: gzip
```

Không có param theo user, không cookie, không body.

### C.11.2 Response

| Trường hợp | Status | Body |
|---|---|---|
| OK | 200 | `{ "server_time": <epoch>, "config": { … } }`, `Content-Type: application/json`, `Cache-Control: no-store` |
| Thiếu / sai format `game` hoặc `env` | 400 | `{ "error": "bad_request" }` |
| `env ≠ prod` và token sai / thiếu | 401 | `{ "error": "unauthorized" }` |
| Không có key envelope cho `game/env` | 404 | `{ "error": "not_found" }` |
| KV lỗi | 500 | `{ "error": "internal" }` |

Client: bất kỳ status ≠ 200, body không parse được, `Error != null` → coi là fetch fail → cache (§2.3). **Không retry** trong cùng lần fetch; lần kế là Resume sau `RefetchAfterResumeS` hoặc Initialize kế. `server_time` chỉ được học khi 200 và config được chấp nhận.

### C.11.3 KV và publish

| Key | Giá trị |
|---|---|
| `config/{game}/{env}/envelope` | chuỗi JSON của **`config`** (không bọc); Worker nối chuỗi `{"server_time":N,"config":` + giá trị + `}`, không parse |
| `config/{game}/{env}/history/{version}` | cùng nội dung, bất biến |
| `config/{game}/{env}/version` | số nguyên version hiện tại |

`publish`: đọc `version` → `v + 1` → ghi `history/{v+1}` → ghi `envelope` → ghi `version`. Chạy **tuần tự** (một job CI hoặc một người); không lock phân tán trong MVP. `rollback --to k`: đọc `history/k`, đổi `version` trong nội dung thành `v + 1`, `published_at` = now, rồi đi qua ba bước ghi như publish.

---

## C.12 Đề xuất cho Nhóm 2 (chờ chọn)

| # | Vấn đề | Đề xuất | Lý do | Chạm § |
|---|---|---|---|---|
| 1 | Counter có window: lifetime riêng hay tổng bucket? | **Ba phần `life / session / days`** (§C.4.2). `ref` không window = `life`. Seed ghi `life`. | `Seed()` cần chỗ ghi lifetime; bucket chỉ 30 ngày nên tổng bucket không thể là lifetime | §4.3, §4.6 |
| 2 | Rule quá `valid_until` | Client coi như `enabled: false`: **không evaluate, không vào `rules_matched`, không exposure**; log `rule_unsupported reason=expired` một lần lúc load. CLI cảnh báo (đã có). | Rule hết hạn còn nổ vào shadow data làm fire rate sai; §8.7 chỉ nói không thành candidate | §8.2, §8.7, §9.3 |
| 3 | Một rule nằm trong hai experiment ở hai layer | **CLI lỗi** (§C.1.7) | Hai override cùng rule không có thứ tự ưu tiên; bất biến "experiment chỉ đổi `then`" thành mơ hồ | §9.1, §10.3 |
| 4 | Trần 36 / 24 ký tự Firebase | Bốn lint **lỗi** ở §C.7.3 | Vi phạm là Firebase cắt lặng, readout `GROUP BY exp_<layer>` sai | §11.2, §10.3 |
| 5 | `seg_snapshot` khi `enabled: false` hay `config_source: none` | **Vẫn bắn**; `enabled: false` → formula / segment vẫn evaluate, chỉ rule bỏ; `none` → không có formula, partition rỗng, `config_version = 0`. Thêm param `engine_enabled`. | Adoption và kill-switch phải quan sát được đúng lúc engine tắt | §7.3, §10.1, §11.1 |
| 6 | Event payload thiếu / sai kiểu, event lạ, `CUSTOM_EVENT` ngoài whitelist, `CUSTOM_STATE` key lạ | **Bỏ event**, không reducer, không trigger; dev build log `Error`; release không log tracking; debug overlay đếm `dropped_events` theo loại | Không phải lỗi engine nên không dùng `engine_error` (nó tắt engine); volume tracking không nên phụ thuộc bug game | §4.1, §4.2 |
| 7 | `ReportX` gọi lặp hoặc sai thứ tự | Bảng §C.6.5: terminal đầu tiên thắng, phần sau bỏ + warn | Executor không cần idempotent (§8.6) thì SDK phải chịu | §8.6 |
| 8 | Thread | Main thread only; dev ném, release bỏ + log một lần (§C.6.6) | Marshal ẩn che bug thứ tự event; Unity API vốn main-thread | §4.7 |
| 9 | Bucket rollover | Trước reducer mỗi event; ngược ngày → ghi `days[0]`, không shift (§C.4.2) | Một chỗ duy nhất, chạy trước mọi thứ đọc counter | §4.3 |
| 10 | Worker lỗi và retry | Bảng §C.11.2; client không retry | Retry trong 3 giây chỉ kéo dài lúc queue đóng; cache là fallback đủ | §10.2 |
| 11 | Counter `version` khi publish | KV key `version`, publish tuần tự (§C.11.3) | Một game, một người publish; lock phân tán là chi phí Phase 2 | §10.3 |
| 12 | `execution_id` unique | Counter `exec_seq` persist, không random (§C.5.3) | Random 32-bit không đảm bảo "duy nhất theo user"; counter miễn phí | §8.7 |
| 13 | `user_id` qua reinstall | Mirror `uid` vào blob lịch sử; file SDK trống mà blob có `uid` → dùng lại | Assignment sticky qua reinstall khi save còn, không thêm cơ chế; chờ §12.5 (1) | §4.5, §9.1 |
| 14 | `TIME_SINCE` với ts ở tương lai | `max(0, now − ts)` | Không clamp thì `time_since(x) < 86400` đúng với mọi timestamp tương lai (đồng hồ lệch) | §6.2 |
| 15 | Segment tham chiếu segment trực tiếp | **Cho phép** (DAG §6.3 đã bao cả hai) | Tag `potential_payer` ở §7.2 đọc partition `engagement_level` | §6.3 |
| 16 | Kiểu kết quả formula | Chỉ NUMBER hoặc BOOL; STRING → lỗi | STRING chỉ có nghĩa qua partition; giữ `features` trong tracking là số | §6.3 |
| 17 | Partition `default` trùng một `case.value` | CLI lỗi | Hai đường ra cùng giá trị làm `cases` sau đó vô nghĩa | §7.1 |
| 18 | `@session` trên `session_count` | CLI cảnh báo (luôn = 1) | Hợp lệ về kiểu nhưng vô nghĩa | §4.3 |
| 19 | Hoàn `actions_shown_today` khi `action_failed` sau khi đã sang ngày UTC | Chỉ trừ nếu `shown_today.day == utcDay(now)`; khác ngày thì bỏ qua | Không để counter âm | §8.3 |
| 20 | Executor báo `offline` nhưng đã grant một phần | Không có trạng thái trung gian; executor phải chọn `Executed` hoặc `Failed` | Giữ contract đơn giản; §8.6 đã nói grant đôi là do game retry | §8.6 |

---

## C.13 Sau review: chỗ cần sửa trong tài liệu chính

Khi các mục trên được chốt, sửa tối thiểu ở v0.4:

- §4.3, §4.6: thêm câu "counter có window lưu `life / session / days`; seed ghi `life`".
- §6.2: thêm arity, semantic `DAYS_BETWEEN`, `TIME_SINCE` clamp; §6.3 thêm "TIMESTAMP là alias NUMBER khi type-check; NUMBER không thành BOOL".
- §8.2: thêm hành vi rule hết `valid_until` (mục 2). §8.3: `cap: false`, `cooldown_s` default 0.
- §8.6: chỉ ra §C.6.5 cho semantic báo cáo.
- §9.1: thêm "một rule chỉ thuộc một experiment active" và công thức hash §C.5.1.
- §10.3: thêm lint §C.7.3, `def_hash` do CLI tính, quy tắc merge §C.9.2.
- §11.1: thêm `sampled`, `engine_enabled`, `action_id` trên `action_*`, format chuỗi §C.7.1.
- §12.5 câu 1: ghi phương án `uid` trong blob.
- Phụ lục A.2: thêm `def_hash`, `frequency`, `cooldown_s`, `cap` tường minh cho mọi action / rule / tag để ví dụ là bản compiled hợp lệ.
