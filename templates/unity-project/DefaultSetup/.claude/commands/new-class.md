---
description: Create a new class following the project's FeatureBaseController style
---

Read the file `Assets/_Project/Features/_Shared/UI/Framework/FeatureBaseController.cs` to understand the architecture, naming conventions, and code structure.
Ensure the new class follows the exact style, comment formatting, and logic flow of `FeatureBaseController`.
If `FeatureBaseController` uses specific namespaces or base classes, include them.

Apply the Merge Two coding rules from `.agents/rules/code-style.md`:
- Private fields `_camelCase`, public/methods `PascalCase`, constants `SCREAMING_SNAKE_CASE`.
- Region order: Fields → Initialize (Awake→OnEnable→Start→OnDisable) → Public Methods → Private Methods → Event Handlers.
- Use `UniTask` (not Coroutine/Task) for async; add XML `///` docs on public API.
