# Workflow Model Recommendations

Hướng dẫn chọn model cho các workflow chạy **thủ công** (`/new-*`, `/format-code`…), cân giữa độ chính xác, tốc độ và quota.

> **Phạm vi:** doc này chỉ áp cho lúc bạn tự gọi một workflow trong session tương tác. Pipeline tự động (`/planning-task`, `/planning-system`, `/run-backlog`) KHÔNG đọc file này — model của nó nằm trong `model:` của `.claude/agents/*.md` và map theo tier trong `.claude/scripts/run-backlog-loop.sh`. Xem bảng tổng ở [backlog-system.md](backlog-system.md) §11.

> **Sonnet đã bị loại khỏi mọi workflow của project này** (2026-09-10) vì độ chính xác không đạt. Sàn hiện tại là **Opus** ở mọi vai trò. **Fable chưa được đưa vào bất kỳ mặc định nào** — nó chỉ là lựa chọn bạn tự bật khi thấy Opus không đủ, vì đắt hơn Opus ~1.9× mà chưa có số đo chứng minh chất lượng hơn trong pipeline này.

## Recommendation Matrix

| Workflow | Recommended Model | Rationale |
|----------|-------------------|-----------|
| **/format-code** | **Gemini 3 Flash** | Tác vụ lặp, đơn giản, xoay quanh cấu trúc code + XML doc. Nhanh và tiết kiệm quota. |
| **/new-class** | **Gemini 3 Flash** | Sinh boilerplate theo template rõ ràng. Gần như không cần reasoning. |
| **/new-ui** | **Opus** (effort `medium`) | Lắp ráp từ prefab template có sẵn, nhưng sai một reference là hỏng prefab — cần độ chính xác của Opus, không cần reasoning sâu. |
| **/new-package** | **Opus** (effort `high`) | Setup data model + controller + manager đan nhau. Nâng effort thay vì nâng model. |
| **/new-feature** | **Opus** (effort `high`) — **Fable** nếu đụng nhiều subsystem | Độ phức tạp cao nhất: reasoning kiến trúc, tạo nhiều file, tích hợp sâu. Fable khi feature cắt ngang IAP / save migration / nhiều domain bucket. |

## Model Selection Tiers

### 🚀 Tier 1: Fast & Repetitive (**Gemini 3 Flash**)
- **Usage**: Batch operation, format đơn giản, boilerplate, xử lý chuỗi.
- **Ví dụ**: `/format-code`, `/new-class`, rename hàng loạt, thêm comment, dọn dẹp nhỏ.

### 🔧 Tier 2: Standard Development (**Opus**, effort `medium`–`high`)
- **Usage**: Implement feature theo pattern rõ, logic chuẩn, requirement đã xác định.
- **Ví dụ**: `/new-ui`, `/new-package`, phần lớn request code ad-hoc.
- **Đây là sàn mặc định** — khi phân vân, chọn mức này.

### 🧠 Tier 3: Complex Reasoning (**Opus**, effort `xhigh`)
- **Usage**: Thiết kế hệ thống mới, tích hợp phức tạp, requirement dài và nhiều ràng buộc đồng thời.
- **Ví dụ**: `/new-feature`, bug khó, thay đổi kiến trúc.
- Nâng **effort** trước khi nâng model — rẻ hơn nhiều và thường là đủ.

### 🎯 Tier 4: Edge Cases & Deep Audit (**Fable**, effort `xhigh`)
- **Usage**: Opus "bí", kiến trúc hệ thống chưa có tiền lệ, adversarial audit, phân tích codebase cực sâu.
- **Ví dụ**: một feature cắt ngang IAP + save migration + nhiều domain bucket; audit bảng economy đã bị Opus bỏ sót; L tier của backlog loop khi bạn muốn thử (`--l-model fable`).
- **Lưu ý**: đây là lựa chọn thủ công, KHÔNG có trong mặc định nào. Đắt ~1.9× Opus và wall-clock dài hơn — bật cho một lần chạy cụ thể rồi đối chiếu kết quả, đừng bật cả loạt.

## Quota Optimization Tips
- **Batch Tasks**: dùng Flash cho sửa hàng loạt.
- **Nâng effort trước, nâng model sau**: `--effort xhigh` trên Opus gần như luôn rẻ hơn chuyển sang Fable, và giải quyết được phần lớn ca khó.
- **Iterative Work**: dựng nền bằng Flash/Opus, chỉ đẩy phần thực sự phức tạp lên Fable.
- **Context Management**: đưa reference rõ ràng để giảm reasoning effort cần thiết.
