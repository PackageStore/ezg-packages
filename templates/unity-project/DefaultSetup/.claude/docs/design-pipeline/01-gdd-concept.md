# Stage 01 — GDD Concept (Design Pipeline)

Stage sáng tạo đầu chuỗi: từ ý tưởng thô → concept doc (hook, mechanic, invariants). Stage sau: [02-gdd-production.md](02-gdd-production.md).

> **Chỉ chạy standalone** (user yêu cầu) — `/planning-system` KHÔNG spawn stage này; nó nhận input từ concept/doc đã có. Model khuyến nghị: Gemini 3.1 Pro (xem [model-assignment.md](model-assignment.md)).

> ⚠️ **Variant-specific:** Prompt chuẩn bên dưới được viết cho **mini-game LiveOps event** (event giới hạn thời gian, có leaderboard/monetization). Với loại feature khác (hệ thống gameplay thuần, meta progression, QoL...): giữ KHUNG output (Core Concept → Core Mechanic → Emotional Loop → Retention → Monetization Direction → Design Invariants) nhưng thay khối "MỤC TIÊU" cho khớp loại feature, mục không áp dụng ghi `N/A` — hoặc bỏ qua hẳn stage này khi đã có concept trong tay.

## Prompt chuẩn

Dựa vào tài liệu GDD/roadmap được user cung cấp (hoặc các file phù hợp dưới `GDD/`), hãy đề xuất concept phù hợp với loại feature được yêu cầu. Không giả định tên game hay đường dẫn GDD cố định.

MỤC TIÊU:

- Không liên quan gameplay chính.

- Mang tính thư giãn.

- Có khả năng tăng D1/D7 retention.

- Có leaderboard hoặc competitive layer nếu phù hợp.

- Có tiềm năng monetization nhưng không được phá integrity.

YÊU CẦU OUTPUT:

=====================

I. CORE CONCEPT

=====================

- Event Name

- Theme & Fantasy

- Target emotion (ví dụ: tension, chill, greed, risk, mastery...)

- Differentiation: Vì sao event này khác biệt so với mini game phổ biến trên mobile?

=====================

II. CORE MECHANIC

=====================

- Luật chơi cụ thể (không mơ hồ)

- Core action của player

- Risk/Reward dynamic (nếu có)

- Skill vs RNG ratio

- Một vòng chơi kéo dài bao lâu?

- Điều gì khiến player muốn chơi thêm 1 lần nữa?

=====================

III. EMOTIONAL & DOPAMINE LOOP

=====================

- 30-second hook đầu tiên là gì?

- Peak moment trong 1 session là gì?

- Có near-miss mechanic không?

- Có escalation mechanic không?

- Tension curve trong 7 ngày event như thế nào?

=====================

IV. RETENTION STRUCTURE (HIGH LEVEL)

=====================

- Vì sao player quay lại mỗi ngày?

- Có streak mechanic không?

- Có milestone reveal không?

- Leaderboard reset logic (high level)?

=====================

V. MONETIZATION DIRECTION (KHÔNG CẦN SỐ CỤ THỂ)

=====================

- Monetization angle là gì?

- Whale hook là gì?

- F2P vẫn có trải nghiệm hoàn chỉnh không?

- Monetization dựa vào time save, cosmetic, multiplier hay risk insurance?

=====================

VI. DESIGN INVARIANTS (QUAN TRỌNG)

=====================

Liệt kê những yếu tố KHÔNG được phá khi chuyển sang production:

- Core mechanic invariant

- Emotional invariant

- Competitive invariant

- Monetization boundary invariant

LƯU Ý:

- Không cần bảng reward chi tiết.

- Không cần economy math.

- Không cần tech spec.

- Tập trung vào hook và novelty.

- Phải có 1 mechanic độc đáo rõ ràng.
