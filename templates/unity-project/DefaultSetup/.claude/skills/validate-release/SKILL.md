---
name: validate-release
description: Kiểm tra toàn bộ setting + tích hợp SDK của project Unity mobile đã sẵn sàng release chưa — bundle id, keystore/signing, Firebase, Ads/mediation, AppsFlyer/Facebook, Remote Config, build settings, cờ debug còn bật, secrets, localization, hằng số sót lại từ project cũ. Chạy deterministic bằng Python thuần, KHÔNG cần Unity Editor và KHÔNG cần MCP. Dùng khi user nói "check release", "validate release", "project đã sẵn sàng build chưa", "kiểm tra SDK đã cài đủ chưa", "còn thiếu gì để lên store", hoặc trước khi bấm build production. KHÔNG phải tool build (đó là /auto-build-setup) và KHÔNG phải compile check (đó là /compile-check).
---

# Validate Release — project đã đủ điều kiện lên store chưa

Trả lời một câu hỏi: **bấm build production bây giờ thì hỏng ở đâu.**

Skill này đọc thẳng file trên đĩa (`ProjectSettings/*.asset`, settings asset của từng SDK,
`AndroidManifest.xml`, JSON remote-config, CSV localize, source `.cs`) và đối chiếu chéo giữa chúng.
Không mở Unity, không gọi MCP, không cần mạng — nên chạy được cả khi Editor đang đóng, đang compile,
hay trên máy CI.

Mọi thứ skill cần đều nằm trong thư mục này — deploy sang project khác chỉ cần copy nguyên thư mục.
Hai file ngoài là *gợi ý tuỳ chọn*, thiếu thì script tự dò tiếp chứ không lỗi:
`.claude/project-profile.json` (lấy `sourceRoot`) và `.claude/validate-release.json` (override rule).

## Chạy

```bash
python3 .claude/skills/validate-release/scripts/validate_release.py
```

| Cờ | Dùng khi |
|---|---|
| `--fails-only` | chỉ hiện mục chưa đạt |
| `-v` | kèm đường dẫn file và cách sửa |
| `--only ads,build` | soi một mảng cụ thể (nhóm: xem `--list-checks`) |
| `--format json` | CI, hoặc khi cần parse lại kết quả |
| `--strict` | cảnh báo cũng làm exit ≠ 0 (dùng cho pipeline chặn build) |
| `--ascii` | terminal không hiện được icon unicode |
| `--project-root <path>` | chạy từ ngoài repo |

Exit code: `0` không mục hỏng · `1` có mục hỏng (hoặc có cảnh báo khi `--strict`) · `2` lỗi chạy script.

Script tự dò project root, tự dò `sourceRoot`, tự dò platform trong scope từ build script. Chạy được
ngay ở project mới mà không cần cấu hình gì.

## Đọc kết quả

Output là checklist, mỗi mục một dòng:

```
FIREBASE
  ✓ google-services.json                  Assets/google-services.json
  ✓ google-services.json khớp bundle id
  ✗ GoogleService-Info.plist              không tìm thấy
```

| Icon | Nghĩa |
|---|---|
| `✓` | Đã kiểm và đạt |
| `✗` | Hỏng thật — build ra sẽ crash / SDK không init / store từ chối / ký fail |
| `!` | Gần như luôn là bug, nhưng có project cố ý. Đọc rồi quyết |
| `·` | Sự thật cần biết, không phán xét (mức stripping, mediation đang chạy…) |
| `–` | Không áp dụng cho project này (không tính là hỏng) |

**Dòng `✓` quan trọng ngang dòng `✗`.** Không có nó thì không phân biệt được "đã kiểm và đạt" với
"chưa kiểm bao giờ" — chính khoảng mù đó từng giấu hai check hỏng âm thầm (so Facebook App ID và
đối chiếu key localization) suốt một phiên bản.

`–` cũng phải đọc: `– Facebook App ID khớp manifest · không đọc được giá trị để so` nghĩa là check
KHÔNG chạy, không phải đã qua.

Mỗi mục có `id` ổn định (vd `ads.admob-appid-match`) — dùng để chỉnh mức hoặc bỏ qua.

## Việc của bạn khi chạy skill này

1. Chạy script, **đưa nguyên checklist cho user** — đừng diễn giải lại thành văn xuôi, cả bảng đã là
   câu trả lời rồi.
2. **Với mỗi `✗`: xác minh lại bằng file thật trước khi kết luận.** Script là heuristic có chủ đích
   thiên về báo thừa hơn báo sót.
3. Với `–`: nói rõ mục đó CHƯA được kiểm, đừng để user tưởng là đã qua.
4. Phân loại `!` thành "phải sửa trước release" và "đã biết, chấp nhận" — hỏi user khi không chắc.
5. **Không tự sửa** setting, key, hay bundle id. Đây là tool chẩn đoán; sửa một thứ trong
   ProjectSettings là đụng cả build đang chạy — để user quyết từng cái.

## Cấu hình per-project

Không sửa file trong thư mục skill — skill được deploy từ server và sẽ bị ghi đè khi update.
Tạo `<project>/.claude/validate-release.json`, nó được deep-merge lên
`config/default-rules.json`:

```jsonc
{
  // Tắt hẳn một check đã xác nhận là chủ đích ở project này
  "ignore": ["secrets.credential-tracked", "build.unity-splash-*"],

  // Hoặc đổi mức thay vì tắt
  "severity": {
    "identity.version-code-drift": "off",
    "remote.missing-default": "blocker"
  },

  // Ép platform thay vì để script tự dò
  "platforms": ["Android"],

  "android": { "minTargetSdk": 35 }
}
```

`ignore` nhận wildcard. `severity` nhận `blocker` / `warn` / `info` / `off`.

Danh sách đầy đủ check id + ý nghĩa: [references/checks.md](references/checks.md).

## Giới hạn — đừng hứa quá

Skill chỉ nhìn **file cấu hình**. Nó KHÔNG:

- mở prefab/scene để tìm missing reference,
- chạy game để xem SDK có init thật không,
- build thử để đo size hay bắt lỗi shader variant.

Ba việc đó là *deep check*, nằm ngoài phạm vi có chủ đích. Nếu user cần, hướng họ sang Unity MCP
hoặc build thử — đừng cố mở rộng skill này sang đó.

Một cái bẫy đã có thật: **danh sách scene trong build script mới là nguồn sự thật**, không phải
`EditorBuildSettings.asset`. Script đọc build script trước và chỉ báo lệch ở mức `info`. Đừng "sửa"
Build Settings vì thấy dòng info này.
