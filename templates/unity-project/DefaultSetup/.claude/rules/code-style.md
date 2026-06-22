---
trigger: always_on
---

# CODE STYLE

**Inherit:** UI→`FeatureBaseController`, Notif→`RedDotBadge`. Ref `FeatureBaseController.cs`.
**Names:** `_camelPrivate`, `PascalPublic`, `SCREAMING_CONST`. No magic numbers.
**Regions:** Fields → Initialize(Awake..Disable) → Public → Private → Events.
**Overrides:** Use `override`, keep `base.Method()`.
**Async:** **MUST** use `UniTask` (no Coroutine/Task). `await UniTask.Delay`. No `async void` (except handlers).
**Perf:** Cache `Find`/`GetComponent` in Awake. Use `StringBuilder` in loops.
**Docs:** XML `///` for public. `// TODO: [Name] - Desc`.
