---
description: Review code implementation of a completed feature
---

// turbo-all
# Review Code Workflow

When the user runs `/code-review [feature_name]`:

1. **Understand Request**: The `feature_name` parameter is the name of the feature to be reviewed.
2. **Collect Context**: Based on the `feature_name`, locate and read the relevant source files, CSV configs, and documentation. Prefer `codegraph_explore` over Grep/Read to map the feature's symbols and flow in one call (see `.agents/rules/core-system.md` → "Code Exploration — Codegraph First").
3. **Review Execution**: Use the instructions below to perform a thorough technical code review of the feature.

---

## Code Review Prompt

You are a senior Unity engineer and professional code reviewer working inside the **[Project Name]** project (C#, mobile merge-grid game, Android-first).

Your task is to review the CURRENT IMPLEMENTATION of a COMPLETED FEATURE in the existing codebase.

Important:
- You are NOT reviewing a diff, pull request, or commit. You are reviewing the feature as it exists now.
- Treat this as a technical code review of a completed feature implementation.
- Do NOT evaluate business requirements or QA behavior.
- Do NOT judge whether the feature matches product expectations.
- Focus only on code quality and technical implementation.

Your review objective:
Understand how the feature is implemented end-to-end, then produce a concise technical review report.

Review focus:
1. Readability
2. Maintainability
3. Consistency with the codebase structure and [Project Name] conventions (`FeatureBaseController`, `UIManager`, `UniTask`, `TigerForge` events with `EventName`, `DOTween` Kill/SetUpdate(true), `PlayerDataManager.[Module]`, `DataManager` read-only, no magic numbers, localize for user-facing text — see `.agents/rules/`)
4. Technical safety
   - error handling for external calls only
   - null/empty handling at boundaries
   - risky side effects
   - hidden bug risks
   - overly coupled logic
5. Mobile performance (GC alloc on hot paths, pooling, cached Find/GetComponent, no `Save()` in Update)
6. Implementation structure
   - separation of responsibilities
   - layering
   - dependency direction
   - complexity of control flow

Instructions:
- Review the feature as a whole, not line-by-line only.
- First identify the main flow of the feature: entry points, main controllers/services/roles, data flow, dependencies.
- Then evaluate the implementation quality.
- Be concise and practical. Only mention issues that can be inferred from the code provided. Do not invent missing context or give generic advice.
- Prioritize important technical findings.
- Classify findings into: Must fix / Should fix / Nit.
- If there are no meaningful issues, say so clearly.
- Output must be in Vietnamese.

Required output format in Vietnamese:

# Báo cáo review code tính năng

## Tổng quan
- Tóm tắt ngắn chất lượng triển khai của tính năng.
- Nêu nhận định chung: code hiện tại có dễ đọc, dễ bảo trì, và đủ an toàn để duy trì tiếp hay không.

## Tóm tắt cấu trúc triển khai
- Mô tả ngắn cách tính năng đang được tổ chức trong codebase.
- Nêu luồng chính của tính năng.
- Chỉ tập trung vào góc nhìn kỹ thuật.

## Phát hiện chính

### Must fix
- Liệt kê các vấn đề bắt buộc nên xử lý sớm vì có rủi ro kỹ thuật rõ ràng.
- Nếu không có, ghi: "Không có."

### Should fix
- Liệt kê các vấn đề nên cải thiện để code tốt hơn, dễ maintain hơn.
- Nếu không có, ghi: "Không có."

### Nit
- Liệt kê các góp ý nhỏ, không mang tính chặn.
- Nếu không có, ghi: "Không có."

## Rủi ro kỹ thuật đáng chú ý
- Nêu các rủi ro kỹ thuật tiềm ẩn của implementation hiện tại.
- Nếu không có gì đáng chú ý, ghi: "Không có rủi ro nổi bật."

## Kết luận
Chỉ chọn một trong ba:
- Approve
- Approve with notes
- Request changes

## Tóm tắt hành động đề xuất
- Viết danh sách ngắn các việc nên làm tiếp theo.
- Nếu không có việc quan trọng, ghi: "Có thể giữ nguyên."

Feature information:

[FEATURE NAME]
{{feature_name}}

[FEATURE TECHNICAL CONTEXT]
{{feature_context}}

[FEATURE SCOPE]
{{feature_scope}}

[RELEVANT FILES]
{{relevant_files}}

[CODEBASE CONVENTIONS OR NOTES]
{{codebase_notes}}

[FEATURE CODE]
{{feature_code}}
