---
description: Rules for using core design pattern packages (Pooling, Factory, Singleton, EventManager).
---
# Design Pattern Rule

Follow these patterns to ensure performance and maintainability. The core patterns now live in `com.ezg.*` UPM packages (Package Manager), **not** in an in-project `Core/` folder.

## 1. Object Pooling
- **Goal**: Optimize performance by reusing objects.
- **Rule**: Use the `com.ezg.pooling` package — `PoolingManager.Instance.Show(...)` (namespace `Ezg.Package.Pooling`).
- **Action**: Do NOT implement custom pooling logic.

## 2. Factory Pattern
- **Goal**: Standardize object creation.
- **Rule**: Use the `com.ezg.factory` / `com.ezg.instance-factory` packages.
- **Action**: Do NOT code factories from scratch; inherit from the base implementation.

## 3. Singleton Pattern
- **Goal**: Access global managers.
- **Rule**: Inherit from `Singleton<T>` from the `com.ezg.singleton` package (e.g. `class UIManager : Singleton<UIManager>`).
- **Action**: Only use when absolutely necessary.

## 4. De-coupling with Event Manager
- **Goal**: Reduce code coupling.
- **Rule**: Use the Easy Event Manager (`com.ezg.easy-event-manager`) — the TigerForge `EventManager` (`using TigerForge;`, `EventManager.EmitEvent`/`StartListening`, names in `EventName`) — as the primary communication method between features.
- **Action**: Favor `EventManager` over Singletons whenever possible to keep features independent.
