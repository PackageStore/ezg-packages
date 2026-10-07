---
name: create-sfx
description: Tạo sound effect (SFX) cho game bằng cách tổng hợp 100% bằng code (numpy + scipy, không AI audio, không tốn tiền, không cần mạng). Gồm âm UI (click, select, popup mở/đóng, whoosh, tick, coin, error, unlock, reward, level up, fanfare) và âm gameplay (bắn/projectile, cast/charge, trúng đòn, chém, nổ, sét, băng vỡ, lửa phụt, hồi máu, triệu hồi, aura loop). Có 26 recipe × 10 nguyên tố (neutral, fire, gold, frost, arcane, toxic, holy, shadow, electric, rose), nhiều biến thể ngẫu nhiên cho âm phát liên tục, master về đúng độ to (LUFS) theo chuẩn mobile hoặc theo âm có sẵn của game khai ở ArtStyle.md §6c. QA tự động: độ to, cụt đuôi, click, onset trễ, mất tiếng trên loa điện thoại, chồng âm khi cảnh đông. Kèm ảnh review waveform + spectrogram đặt cạnh âm gốc và file nghe A/B. Xuất WAV + .meta đúng chuẩn import (Vorbis, Decompress On Load, giữ GUID khi làm lại) vào `<sfxRoot>/<Name>/`, gắn thẳng vào field `SoundConfig` (OpenPopup, ClosePopup, ButtonSelect, PurchaseItem…) hoặc vào list `SoundPlayController` của com.ezg.audio trên prefab (pool biến thể + cooldown chống chồng, play on enable). Dùng khi user nói "tạo sfx …", "tạo sound / âm thanh / tiếng …", "làm sound cho skill / đạn / quái X", "âm bắn / âm trúng / âm nổ / âm chém / âm sét", "sound UI / click / mở popup / nhận thưởng / level up", "thay tiếng bấm nút", "create sfx", "make a sound effect". KHÔNG dùng cho nhạc nền (music), giọng nói / voice, hay âm cần thu thật (ambience thật, foley thật).
---

# Create SFX: âm thanh tổng hợp bằng code → WAV + gắn vào Unity

Cùng triết lý với [create-vfx](../create-vfx/SKILL.md): **dựng bằng code, không AI, không tốn tiền**, style lấy từ âm
có sẵn của game, QA tự động trước khi ship. Âm được dựng như một FX phát sáng:
- **transient** đọc được ngay sample 0 (lõi trắng nóng);
- **body** mang cao độ và trọng lượng;
- **tail** gồm các mảnh sáng nhỏ;
- cộng lớp **tint** của nguyên tố.

Skill này không chứa giá trị style của game nào. Âm gốc để so, nguyên tố mặc định, độ lệch độ to, âm đã duyệt và hướng
đã loại đều nằm ở `.claude/docs/ArtStyle.md` §6c.

> **Agent không nghe được.** Tai của agent là số đo (LUFS, onset, độ sáng, mất tiếng trên loa điện thoại) cộng ảnh
> spectrogram đặt cạnh âm gốc. Vì vậy báo cáo **luôn** kèm file nghe (`audition.wav`, `burst.wav`) để user duyệt bằng
> tai, và nói rõ "chưa ai nghe" cho tới khi user xác nhận.

`<skill>` = `.claude/skills/create-sfx`. `M = python3 <skill>/scripts/make_sfx.py` (Windows: `py`), chạy từ root
project. Cần Python 3 + `numpy`, `scipy`, `pillow` (`pip install numpy scipy pillow`). Muốn so với âm gốc MP3/OGG thì
cần `afconvert` (có sẵn trên macOS) hoặc `ffmpeg`; WAV đọc trực tiếp. Lệnh nghe thử `$M play` tự chọn `afplay`
(macOS), `winsound` (Windows) hoặc `paplay`/`aplay`.

## Đọc trước

| Khi | Đọc |
|---|---|
| Luôn luôn (3 phút) | [reference/style-guide.md](reference/style-guide.md): game phát âm thế nào, từ vựng âm, độ to chuẩn, nhịp, loa điện thoại, những gì cấm |
| Luôn luôn | `.claude/docs/ArtStyle.md` §6c (SFX) + §9 (hướng đã loại) + rule `art-style`. `$M config` in ra cấu hình đã đọc (§6c, `SoundConfig` field nào đang trống, bundle) |
| Request khớp recipe có sẵn | [reference/recipes.md](reference/recipes.md) §1–§2 (map request → recipe + nguyên tố + event) |
| Cần âm mới, không recipe nào khớp | recipes.md §4 (viết recipe mới) + [reference/synth-api.md](reference/synth-api.md) |
| Gắn vào SoundConfig / prefab, debug Unity | [reference/unity-integration.md](reference/unity-integration.md) |

## Cổng trước khi làm

**Style §6c.** Nếu ArtStyle.md thiếu, hoặc khối `sfx-style` của §6c chưa có dòng `ref` nào, thì chưa có mốc âm của
game. Làm theo § Bootstrap của ArtStyle (bước SFX):
1. Tìm âm đang ship: `find Assets -iname '*.wav' -o -iname '*.mp3' -o -iname '*.ogg'`, bỏ qua sample của plugin/SDK.
2. Chạy `$M analyze` trên các file đó, chọn 1–3 file cho mỗi loại (`ui_short`, `ui_jingle`, `shot`, `hit`, `explode`,
   `cast`, `loop`) rồi khai `ref` trong §6c.
3. Âm gốc to/nhỏ lệch hẳn chuẩn của style-guide §4 thì khai `offset`.
4. Đặt `Status: draft` và báo dev trong report.

Project chưa có âm nào thì dùng chuẩn mặc định của style-guide. Ghi vào §6c rằng chưa có REF và nói rõ trong report.
Không cần hỏi dev, vì âm làm lại rất rẻ và dev duyệt bằng tai.

## Kết quả

| Request | Lệnh | File (trong `<sfxRoot>/<Name>/`) |
|---|---|---|
| Âm UI / âm đơn phát từ code | `make … --name <Name> [--config <Field>]` | `sfx_<name>.wav` (1 biến thể) hoặc `sfx_<name>_<i>.wav`, cùng `sfx_<name>.sfx.json` |
| Âm phát lặp lại (đạn, trúng đòn, nổ…) cần pool biến thể | `make … --name <Name> --event fire\|shot\|hit [--prefab <p>]` | `sfx_<name>_<event>_<i>.wav`, cùng `sfx_<name>_<event>.sfx.json` |
| Thêm lớp thứ hai cho cùng event (vd hit = impact + nổ nhỏ) | thêm `--part <tag> --append` | `sfx_<name>_<event>_<tag>_<i>.wav` |

- `sfxRoot` lấy từ `.claude/project-profile.json`, mặc định `Assets/_Project/Visual/ArtAsset/Shared/Sounds/Generated`.
  Dùng `--out` để đặt chỗ khác, ví dụ `Visuals/Sounds` của một feature. `<Name>` theo PascalCase của thứ phát âm
  (`OpenPopup`, `Fireball`, `EnemySlime`).
- **File nghe/duyệt** (`<base>.review.png`, `<base>.audition.wav`, `<base>.burst.wav`) ghi vào
  `Temp/CreateSfx/<base>/`. Thư mục này git-ignore, dùng xong bỏ.
- `.meta` có sẵn thì **giữ nguyên**: cùng GUID nên mọi prefab/asset đang tham chiếu vẫn sống khi làm lại âm.

## Quy trình

**0. Hiểu request → recipe + nguyên tố + event + nơi phát.** Dùng bảng map ở recipes.md §1.
- **Nơi phát:** field của `SoundConfig` thì dùng `--config` (`$M config` liệt kê field và field nào đang trống).
  `SoundPlayController` trên prefab (nút, popup, VFX, đạn, quái) thì dùng `--prefab`. Code của game tự phát thì chỉ
  sinh file và báo path.
- **Event** chỉ cần cho âm phát lặp lại. `fire` = mỗi lần kích hoạt, `shot` = mỗi projectile/spawn, `hit` = mỗi lần
  trúng. Mỗi event có pool ≥ 3 biến thể và cooldown mặc định riêng. Cần cả 3 event thì chạy `make` 3 lần, mỗi event
  một recipe.
- **Nguyên tố**, theo thứ tự ưu tiên:
  1. lời dev;
  2. màu/tên VFX của thứ phát âm (cùng tên nguyên tố với create-vfx);
  3. `default` của §6c, chỉ áp cho âm gameplay;
  4. mặc định của recipe.

  Nguyên tố nằm ngoài `allow` của §6c thì hỏi dev.
- User nêu số liệu (ngắn hơn, trầm hơn, to hơn, nhiều biến thể hơn) thì dùng `--life / --pitch (semitone) / --lufs /
  --variants / --seed / --tint`. Chỉ hỏi lại khi không suy ra được loại âm.
- **Ưu tiên nguồn có sẵn:** project đã có âm khớp hẳn request (kể cả `ref` ở §6c) thì gắn lại bằng `wire --file`,
  không cần tổng hợp mới.

**1. Tổng hợp + QA (chỉ Python, không đụng Unity):**
```bash
$M make --recipe <r> --name <Name> [--element <e>] [--event <ev>] [--config <Field> | --prefab <p> [--field F]] [...]
```
1. Đọc JSON in ra: `qa` phải là `PASS`. Không ship khi còn các flag chặn: `LOUDNESS`, `CLICK_START`, `CUT_TAIL`,
   `LATE_ONSET`, `TOO_LONG`, `SILENT`, `LOOP_SEAM`, `PHONE_WEAK`. Bảng sửa ở recipes.md §5.
   `qa_warn` (`BASS_HEAVY`, `HARSH`, `LIMITED`, `SAME_VARIANTS`, `BURST_LOUD`, `DC`) và `style_warn` thì cân nhắc sửa
   và **nêu trong báo cáo**.
2. **Tự mở** `Temp/CreateSfx/<base>/<base>.review.png` xem bằng mắt: hàng REF (âm gốc cùng loại, từ §6c) ở trên, NEW
   ở dưới. So độ dài, hình bao (transient sắc / đuôi) và dải tần trên spectrogram. Lệch rõ so với REF thì chỉnh tham
   số hoặc recipe rồi chạy lại. Vòng này rẻ, lặp tới khi đạt. Không có REF thì so với số của style-guide §3–§4.
3. `--config` / `--prefab` sửa `SoundConfig.asset` / prefab ngay trong lệnh `make`. Lệnh chỉ sửa YAML của đúng một
   field trong MonoBehaviour, không đụng Transform. Muốn xem trước thì thêm `--dry-run`. Prefab chưa có
   `SoundPlayController` thì làm theo unity-integration.md §4.

**2. Import vào Unity** (Unity MCP; không có C# nên không recompile):
- Trước khi import, gọi `unity_editor_state` + `unity_agents_list`. Session khác đang Play thì chờ, hoặc để Editor tự
  import khi được focus.
- Gọi `unity_execute_code`: `AssetDatabase.ImportAsset("<thư mục kết quả>", ImportAssetOptions.ForceSynchronousImport |
  ImportAssetOptions.ImportRecursive)`, rồi import lại prefab / `SoundConfig.asset` đã sửa. Kiểm theo
  unity-integration.md §5: clip load được, Vorbis, field/list không null.
- Không có Unity MCP thì dừng ở bước 1, báo `import: chưa (Unity MCP không kết nối) — Editor sẽ tự import khi mở`.

**3. Báo cáo** (rule output-format):
- Liệt kê file kết quả (WAV, spec), file đã sửa (prefab / `SoundConfig.asset` / ArtStyle.md) và file biến thể cũ đã
  xoá.
- Kèm đường dẫn `review.png`, `audition.wav` (và `burst.wav` với âm phát liên tục), cùng lệnh nghe `$M play <base>` để
  user duyệt.
- Nêu recipe × nguyên tố, event + cooldown, LUFS, kết quả QA, REF đã so (hoặc "chưa có REF"), và các bước đã bỏ qua
  (chưa import, chưa Play-test, **chưa ai nghe**).

## Luật

- **Chỉ tổng hợp bằng code** (numpy/scipy). Không dùng AI audio, API trả phí hay sample tải từ mạng. Báo cáo khẳng
  định "tổng hợp 100% bằng code".
- **Style lấy từ âm của game, không từ trí nhớ:** so với `ref` ở §6c. Mọi recipe mới phải chạy `review` cạnh REF cùng
  loại.
- **Dev phản hồi bằng tai thì cập nhật ArtStyle.md ngay trong lượt đó** (rule art-style mục 5):
  - Duyệt âm nào → thêm dòng vào bảng "Âm đã duyệt" của §6c, chép `command` từ `.sfx.json`.
  - Chê hướng nào → thêm vào §9 (nguyên văn lý do + ngày) và dòng "Cấm" của §6c.

  Không ghi vào file của skill: skill được Feature Hub cập nhật đè, ArtStyle.md thì không.
- **Không sửa C# khi thêm âm.** Mọi thứ đi qua recipe (Python) + `.meta` + field YAML. Thiếu component thì thêm
  `SoundPlayController` có sẵn của com.ezg.audio (unity-integration.md §4), không tự viết component mới.
- **Không sửa dáng các recipe đã có** khi user không yêu cầu, trừ khi để sửa lỗi QA. Muốn biến thể thì dùng
  `--element/--pitch/--life/--seed/--tint`, hoặc tạo recipe mới.
- Âm phát liên tục (hit, shot) phải có **≥ 3 biến thể** + cooldown (mặc định hit 0.06 s, shot 0.05 s) để không lặp tai
  và không chồng vỡ tiếng khi cảnh đông.
- Âm loop (`aura_loop`) chỉ gắn khi chắc nơi gọi tắt được nó (unity-integration.md §6).
- Chưa nghe / chưa Play-test thì nói rõ là chưa.
