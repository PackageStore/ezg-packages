# Model Assignment theo từng bước

| Bước | File | Nhiệm vụ chính | Model | Lý do |
| :--- | :--- | :--- | :--- | :--- |
| **GDD 1** | `01-gdd-concept-generate` | Sáng tạo concept, hook, novelty | **Opus** | Reasoning sâu + creative synthesis. Cần model hiểu nuance để concept không generic |
| **GDD 2** | `02-gdd-production-generate` | Số hóa, công thức, bảng economy | **Opus** | Cần instruction following cực kỳ chặt với lượng lớn ràng buộc đồng thời. Opus giữ constraint tốt hơn qua output dài |
| **GDD 3** | `03-gdd-final-generate` | Adversarial audit, phát hiện exploit | **Opus** | Audit đòi hỏi adversarial reasoning và khả năng tự mâu thuẫn với output trước. Opus ít sycophantic hơn |
| **Tech 1** | `01-feature-analysis-prompt` | Phân rã atomic unit, edge case | **Sonnet** | Đây là bước phân tích có cấu trúc rõ, không cần reasoning quá sâu. Sonnet nhanh và chính xác cho structured decomposition |
| **Tech 2** | `02-tech-spec-prompt` | Generate + Audit tech spec | **Opus** | Bước này có embedded audit (3.5) đòi hỏi tự phát hiện và tự sửa lỗi trong cùng một output — cần model mạnh nhất |
| **Tech 3** | `03-implementation-mapping-prompt` | Mapping ra bảng terminal-ready | **Sonnet** | Output có format cứng, ít ambiguity. Sonnet đủ mạnh và nhanh hơn đáng kể, tiết kiệm cost ở bước cuối |
