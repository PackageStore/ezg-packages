# Sổ nguồn gốc và nợ: cách đọc, cách xác nhận

Luật ở QC 3 (sổ) và QC 10.2 (nợ). File này nói cách làm với file JSON.

## GameIPLedger.json

```json
{
  "version": 1,
  "generator": "game-ip init 0.1.0 (2026-10-23)",
  "entries": [
    {
      "id": "vendor-coolpack",
      "kind": "asset-store",
      "title": "Cool Pack",
      "author": "Cool Studio",
      "source": "https://assetstore.unity.com/packages/…",
      "license": "unity-asset-store-eula",
      "licenseFile": "",
      "scope": { "game": true, "ads": "unknown", "trailer": "unknown", "store": "unknown", "redistributeSource": false },
      "proof": "",
      "attribution": "",
      "limits": "",
      "expires": null,
      "paths": ["Assets/ThirdParty/CoolPack/**"],
      "status": "draft",
      "evidence": ["nằm trong folder ThirdParty", "readme nhắc Asset Store"],
      "notes": ""
    }
  ]
}
```

- **Mục nào phủ asset nào**: `guids` khớp trước; không thì glob trong `paths` cụ thể nhất thắng (đường dẫn đầy đủ thắng
  `folder/**`, folder sâu thắng folder nông). Nên mục font riêng thắng mục cả pack chứa font đó, mục pack thắng mục "tự làm" của
  folder cha.
- **`kind`**: `inhouse`, `commission`, `asset-store`, `marketplace`, `stock`, `oss`, `package`, `font`, `ai`, `sdk`,
  `publisher-sdk`, `middleware`, `public-domain`, `licensed-ip`, `unity`, `third-party` (chưa rõ loại).
- **`license`**: mã trong `<skill>/data/licenses.json` (MIT, OFL-1.1, CC-BY-NC-4.0, unity-asset-store-eula, inhouse, custom,
  unknown…). License riêng: `custom` + `licenseFile` + tóm tắt trong `notes`.
- **`evidence`**: máy tìm ra mục này nhờ đâu. Không sửa; người thêm bằng chứng vào `proof`, `notes`.
- **Mục `inhouse` máy sinh** ("Tự làm? (Assets/…)") chỉ là đoán: phần còn lại vào build chưa khớp nguồn nào khác. Phải mở folder
  xem có lẫn asset mua, lấy từ web, AI không; có thì tách thành mục riêng.

## Xác nhận một mục

Người có trách nhiệm (QC 3.5) xem, rồi sửa:

1. `kind`, `license`, `author`, `source` đúng chưa.
2. `proof`: tài khoản mua của công ty và số đơn; số hợp đồng; ngày tải và bản chụp trang license (thư viện miễn phí). Mục
   `inhouse`: người làm, nơi giữ file gốc (.psd, .spine, .blend, project nhạc), commit đầu tiên hoặc ngày tạo. Đây là thứ luật sư
   cần đầu tiên khi bị khiếu nại "chép" (UngPho mục 2), và khi mình đi khiếu nại người khác (QC 9.1).
3. `scope.ads`, `trailer`, `store`: `true` chỉ khi license nói rõ; không rõ thì để `"unknown"` (chặn creative, không chặn game).
4. `attribution`: câu ghi công đúng như license đòi (lệnh `notices` đưa vào màn Credits).
5. `limits` (ngưỡng doanh thu, seat, CCU), `expires` (hạn license nhạc, footage).
6. `status: "confirmed"`, `confirmedBy`, `confirmedAt`.

Claude điền được 1–5 theo lời user (ghi "theo <người>, <ngày>" vào `notes`), **không** tự đặt bước 6.

## GameIPDebt.json (nợ)

```json
[
  { "gate": "IP-8", "path": "Assets/Art/old_bg.png", "reason": "đang vẽ lại, bản 1.3 thay", "approvedBy": "Tên người duyệt", "until": "2026-12-31" },
  { "gate": "IP-5", "entry": "vendor-somelib", "reason": "…", "approvedBy": "…", "until": "…" }
]
```

- Khớp theo `gate` + `path` (đường dẫn asset hoặc folder như check in ra) hoặc `entry` (id mục sổ).
- Còn hạn: dòng ✗ / ! thành `·` kèm lý do. Hết hạn: trở lại mức gốc.
- Không có nợ cho (QC 10.2): asset không rõ nguồn (IP-1), copyleft mạnh (IP-5), logo thật (IP-15), footage của game khác (IP-17).
- Chỉ [DUYỆT] thêm nợ. Claude chỉ ghi khi user nói rõ ai duyệt.
