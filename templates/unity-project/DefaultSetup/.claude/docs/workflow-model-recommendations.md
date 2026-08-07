# Workflow Model Recommendations

This document provides guidance on selecting the most appropriate AI model for each workflow in the project to balance efficiency, quality, and quota usage.

## Recommendation Matrix

| Workflow | Recommended Model | Rationale |
|----------|-------------------|-----------|
| **/format-code** | **Gemini 3 Flash** | Simple, repetitive task focusing on code structure and XML documentation. Fast and quota-efficient. |
| **/new-class** | **Gemini 3 Flash** | Boilerplate generation based on a clear template. Requires minimal reasoning. |
| **/new-enemy-skill** | **Gemini 3 Pro (Low)** / **Claude Sonnet** | Moderate complexity. Needs to follow specific patterns and read documentation. |
| **/new-skill** | **Claude Sonnet** | High complexity due to multiple CSV configurations and detailed documentation requirements. |
| **/new-feature** | **Claude Sonnet (Thinking)** | Highest complexity. Requires architectural reasoning, multi-file creation, and deep system integration. |
| **/new-package** | **Claude Sonnet** / **Thinking** | complex setup involving data models, controllers, and managers. Thinking mode recommended for complex packages. |
| **/new-ui** | **Gemini 3 Pro (Low)** | Assembly-focused task using existing prefab templates with predictable scripting requirements. |

## Model Selection Tiers

### 🚀 Tier 1: Fast & Repetitive (**Gemini 3 Flash**)
- **Usage**: Batch operations, simple formatting, boilerplate, string processing.
- **Project Examples**: `/format-code`, `/new-class`, renaming, adding comments, minor cleanup.

### 🔧 Tier 2: Standard Development (**Claude Sonnet**)
- **Usage**: Implementing features with clear patterns, standard logic, and well-defined requirements.
- **Project Examples**: `/new-skill`, `/new-package`, most ad-hoc coding requests.

### 🧠 Tier 3: Complex Reasoning (**Claude Sonnet Thinking**)
- **Usage**: Designing new systems, handling complex integrations, or when requirements are large and detailed.
- **Project Examples**: `/new-feature`, complex bug fixing, architectural changes.

### 🎯 Tier 4: Edge Cases & Research (**Claude Opus Thinking**)
- **Usage**: Situations where Sonnet is "stuck," novel system architecture, or extremely deep codebase analysis.
- **Note**: Avoid for standard workflows to preserve quota.

## Quota Optimization Tips
- **Batch Tasks**: Use Flash for bulk edits.
- **Iterative Work**: Start with Sonnet/Flash for the foundation, then use Thinking modes only for the complex parts.
- **Context Management**: Provide clear references to minimize reasoning effort and allow lighter models to perform effectively.
