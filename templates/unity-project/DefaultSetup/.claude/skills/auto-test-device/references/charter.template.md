# Charter — <Tính năng>

**Status:** draft
<!-- draft = agent soạn từ GDD/code, chưa ai duyệt · reviewed = dev/GD đã đọc. Kết quả từng phiên KHÔNG ghi vào
     đây (nằm ở .git/auto-test-device/sessions/<phiên>/cases.jsonl) — file này chỉ giữ oracle + ca, commit cùng repo. -->
**Cập nhật:** <YYYY-MM-DD>

Mục tiêu: <tính năng làm gì cho người chơi, 1–2 câu>
Đường vào: <từ màn chính bấm gì để tới; điều kiện mở khoá; cheat mở nhanh nếu có>
Trạng thái/điều kiện liên quan: <tiền, level, item, flag tutorial, thời gian…>
Nguồn: <GDD/TechSpec/file code/CSV đã đọc>

## Oracle (giá trị kỳ vọng — mỗi dòng phải có nguồn; suy luận thì ghi "suy luận")

| Hành động | Kỳ vọng | Nguồn |
|---|---|---|
| <Mua gói 1> | <-100 kim cương, +1.000 vàng> | <CoinPack.csv:2 — `qadevice.py csv CoinPack --where id=1`> |
| <…> | <…> | <…> |

## Ca test

Mã ca `P<ưu tiên>-<số>` là **cố định** (thêm số mới, không đánh lại). Agent ghi kết quả bằng
`qadevice.py note --case <tên-file-này>/P0-01 --result pass|fail|blocked|skip [--bug N] "<ghi chú>"`.
Ưu tiên: P0 happy path · P1 biên/chuyển trạng thái · P2 gián đoạn · P3 lưu trữ/nhất quán · P4 visual/môi trường · P5 hiệu năng.

| # | Ca | Bước chính | Kỳ vọng | Oracle |
|---|---|---|---|---|
| P0-01 | <Đi trọn vòng chính lần 1> | <…> | <…> | <dòng oracle> |
| P0-02 | <Đi trọn vòng chính lần 2 (state reset?)> | <…> | <…> | <…> |
| P1-01 | <Biên: tài nguyên = 0 / thiếu 1 đơn vị / đúng giá / dư> | <…> | <nút đổi trạng thái đúng, không âm tiền> | <…> |
| P1-02 | <Chuyển trạng thái không hợp lệ (nhận khi chưa xong, mua khi đang xử lý)> | <…> | <bị chặn, không nhân đôi> | <…> |
| P2-01 | <Back hệ thống ở popup chính> | `key back` | <đóng đúng 1 lớp> | — |
| P2-02 | <Nền 5 s / 60 s giữa luồng> | `app background --secs 5` | <quay lại đúng màn, timer đúng> | <…> |
| P2-03 | <Kill + mở lại sau khi "chốt" (mua/nhận)> | `app restart` | <không mất, không nhân đôi> | <…> |
| P2-04 | <Spam nút chính 5 lần> | `tap … --times 5` | <xử lý 1 lần> | <…> |
| P2-05 | <Mất mạng khi hành động cần server> | `net off` | <báo lỗi tử tế, có thử lại, không trao thưởng> | <…> |
| P3-01 | <Cùng con số ở HUD/popup/tooltip khớp nhau> | <…> | <…> | <…> |
| P3-02 | <Cập nhật build giữ data> | `build update latest` | <state giữ nguyên> | qa.verified.statePaths |
| P4-01 | <Audit visual mọi màn của feature (auto-audit + `audit`)> | <…> | <không tràn/cắt chữ, không key lộ, không nút bị che> | — |
| P4-02 | <Ma trận môi trường> | `env matrix --quick` · `pseudoloc on` | <layout không vỡ ở cỡ chữ lớn / dark / màn khác> | — |
| P5-01 | <Perf khi vào/ra feature 3 lần> | `perf` | <RAM không tăng > 150 MB, FPS ≥ 25> | — |

## Ghi chú thiết kế ca (vì sao chọn các ca trên, ca nào cố ý bỏ)

- <…>
