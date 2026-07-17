# Model Assignment theo từng stage

| Stage | File | Nhiệm vụ chính | Model | Lý do |
| :--- | :--- | :--- | :--- | :--- |
| **01** | `01-gdd-concept.md` | Sáng tạo concept, hook, novelty | **Gemini 3.1 Pro** | Reasoning sâu + creative synthesis. Cần model hiểu nuance để concept không generic |
| **02** | `02-gdd-production.md` | Số hóa, công thức, bảng economy | **Opus** | Cần instruction following cực kỳ chặt với lượng lớn ràng buộc đồng thời. Opus giữ constraint tốt hơn qua output dài |
| **03** | `03-gdd-final.md` | Adversarial audit, phát hiện exploit | **Opus** | Audit đòi hỏi adversarial reasoning và khả năng tự mâu thuẫn với output trước. Opus ít sycophantic hơn |
| **04** | `04-feature-analysis.md` | Phân rã atomic unit, edge case | **Sonnet** | Đây là bước phân tích có cấu trúc rõ, không cần reasoning quá sâu. Sonnet nhanh và chính xác cho structured decomposition |
| **05** | `05-tech-spec.md` | Generate + Audit tech spec | **Opus** | Bước này có embedded audit (3.5) đòi hỏi tự phát hiện và tự sửa lỗi trong cùng một output — cần model mạnh nhất |
| **06** | `06-implementation-mapping.md` | Mapping ra bảng terminal-ready | **Sonnet** | Output có format cứng, ít ambiguity. Sonnet đủ mạnh và nhanh hơn đáng kể, tiết kiệm cost ở bước cuối |
