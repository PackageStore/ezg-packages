# Local Notification

A self-contained local/scheduled notification engine for Unity mobile games (Android + iOS).
It wraps Unity's *Mobile Notifications* package behind a small, fluent, platform-agnostic API and
keeps **all game-specific scheduling rules outside the package** so the engine can be shipped as a
standalone module.

- **Namespace:** `Ezg.Feature.LocalNotification`
- **Editor namespace:** `Ezg.Feature.LocalNotification.EditorTools`
- **Location:** `Assets/_Project/Features/_Shared/LocalNotification/`

---

## Table of contents

1. [What it does](#what-it-does)
2. [Package vs. project split](#package-vs-project-split)
3. [File map](#file-map)
4. [Architecture & flow](#architecture--flow)
5. [Quick start](#quick-start)
6. [Public API](#public-api)
7. [Scheduling modes](#scheduling-modes)
8. [Content & localization](#content--localization)
9. [The `AppPaused` hook (decoupling)](#the-apppaused-hook-decoupling)
10. [Project setup menu (template generator)](#project-setup-menu-template-generator)
11. [Permissions](#permissions)
12. [Deep-link / launch payload](#deep-link--launch-payload)
13. [Immediate native notification (`NotificationNativeBridge`)](#immediate-native-notification-notificationnativebridge)
14. [External dependencies](#external-dependencies)
15. [Turning this into a standalone package](#turning-this-into-a-standalone-package)
16. [Troubleshooting](#troubleshooting)

---

## What it does

- Schedules **local notifications** by delay, by an absolute UTC time, or by an absolute device-local
  time — with optional repeat intervals.
- Registers a notification under a **string id**; re-registering the same id replaces the previous one
  (`RegisterOrReplace`), so you never get duplicates.
- Re-evaluates ("refreshes") a notification when a gameplay **event** fires (via TigerForge
  `EventManager`) or when the app is paused, using an optional **enabled predicate** to cancel it when
  it no longer applies.
- Handles **runtime permission** flow on Android 13+ and iOS, exposes the current status, and can open
  the OS app-notification settings screen.
- Captures the **payload** of the notification the user tapped to launch the app (deep-link support).
- Auto-selects the correct platform backend at compile time (`UNITY_ANDROID` / `UNITY_IOS`), and
  degrades to a safe no-op provider on unsupported platforms (Editor, Standalone).

---

## Package vs. project split

| Part | Belongs to | Why |
|------|-----------|-----|
| Engine (Manager, Service, Models, Platform, NativeBridge, Editor menu) | **Package** | Generic, reusable, no game knowledge. |
| `LocalNotificationScriptInfo.cs` (`LocalNotificationScriptInfo` + `LocalNotificationScriptInfoRepository` + `LocalNotificationRules`) | **Project** | Game-specific ids, copy, and scheduling rules. Regenerate via the setup menu. |

The package **never references** the project file. The project file subscribes to the package's
[`AppPaused` hook](#the-apppaused-hook-decoupling) at startup, so the dependency only points
**project → package** (the safe direction).

---

## File map

| File | Scope | Responsibility |
|------|-------|----------------|
| `LocalNotificationManager.cs` | Package | Thin static **facade** — the recommended entry point. |
| `LocalNotificationService.cs` | Package | Core service: registry, refresh logic, permission gating, launch payload, `AppPaused` event, and the hidden `LocalNotificationRuntime` MonoBehaviour. |
| `LocalNotificationModels.cs` | Package | Fluent builders & DTOs: `NotificationDefinition`, `NotificationContentTemplate`, `NotificationScheduleRequest`, `NotificationPlatformOptions`, `NotificationPayload`, `RuntimeNotificationRequest`, enums. |
| `LocalNotificationPlatform.cs` | Package | `INotificationPlatformProvider` / `INotificationPermissionService` and the Android / iOS / Unsupported implementations. |
| `NotificationNativeBridge.cs` | Package | Optional **immediate** native Android notification via a custom java lib (separate from the scheduling engine). |
| `Editor/LocalNotificationSetupMenu.cs` | Package (Editor) | `Create > Ezg > Local Notification > Project setup` — generates the project template. |
| `LocalNotificationScriptInfo.cs` | **Project** | Game ids (`PNxxx`), CSV copy parsing, and the concrete `LocalNotificationRules`. |

---

## Architecture & flow

```
        Game code (Shop, PiggyBank, StarterPack, Settings, Splash, …)
                 │ Init() / Register…/Stop…                    ▲
                 ▼                                             │ AppPaused (event)
        LocalNotificationManager  ──►  LocalNotificationService  ──►  LocalNotificationRules
        (facade, package)              (core registry, package)       (rules, PROJECT)
                                              │
                                              ▼
                              INotificationPlatformProvider
                          ┌──────────────┬───────────────┬───────────────┐
                          ▼              ▼               ▼
                    Android provider  iOS provider   Unsupported (no-op)
                  (Unity Mobile Notifications)        (Editor / Standalone)
```

- A hidden, `DontDestroyOnLoad` MonoBehaviour (`LocalNotificationRuntime`) is created on first
  `Initialize()`. It forwards `OnApplicationPause` / `OnApplicationFocus` into the service, which
  raises `AppPaused` and re-captures any launch payload.
- `RegisterOrReplace` stores a `NotificationDefinition`, binds its trigger events, and (by default)
  immediately calls `Refresh`. `Refresh` checks permission → enabled predicate → schedule validity,
  then asks the active provider to `ScheduleOrUpdate` or `Cancel`.

---

## Quick start

```csharp
using Ezg.Feature.LocalNotification;

// 1) Initialize once, early (e.g. SplashSceneController).
LocalNotificationManager.Init();

// 2) Simplest registration: title/content + a delay (seconds).
LocalNotificationManager.RegisterOrReplace(
    "ENERGY_FULL",
    title: "Energy full!",
    content: "Your energy is full — come back and play.",
    delaySeconds: 3600);

// 3) Stop / cancel it.
LocalNotificationManager.Stop("ENERGY_FULL");

// 4) Permission status.
if (!LocalNotificationManager.AreNotificationsEnabledBySystem())
    LocalNotificationManager.OpenAppNotificationSettings();
```

Advanced registration with the fluent `NotificationDefinition` (used by `LocalNotificationRules`):

```csharp
LocalNotificationService.RegisterOrReplace(
    "PIGGY_REMINDER",
    new NotificationDefinition("PIGGY_REMINDER")
        .WithContent(new NotificationContentTemplate()
            .WithTitleKey("noti_piggy_bank_title")    // localized via LocalizeCategory.Notification
            .WithBodyKey("noti_piggy_bank_content"))
        .WithEnabledPredicate(() => PiggyIsStillActiveAndUnpurchased())
        .WithTriggerEvents(nameof(EventName.SomeGameEvent)) // re-evaluated when this event fires
        .WithSchedule(() => NotificationScheduleRequest.CreateDelay(remindInSeconds)));
```

---

## Public API

### `LocalNotificationManager` (facade — start here)

| Member | Description |
|--------|-------------|
| `Init()` | Initialize the engine (idempotent). |
| `RegisterOrReplace(id, RuntimeNotificationRequest)` | Register from a plain request object. |
| `RegisterOrReplace(id, title, content, delaySeconds, loop=false, loopDurationSeconds=0, triggerEvents=null, payload=null, platformOptions=null)` | Convenience overload. |
| `Stop(id)` | Cancel & unbind a notification. |
| `RefreshPermissionStatus()` → `NotificationPermissionStatus` | Re-read OS permission. |
| `GetPermissionStatus()` → `NotificationPermissionStatus` | Cached status. |
| `AreNotificationsEnabledBySystem()` → `bool` | `true` when status is `Granted`. |
| `OpenAppNotificationSettings()` → `bool` | Open the OS settings screen. |

### `LocalNotificationService` (core — for rule code)

Everything on the facade, plus: `Initialize()`, `RegisterOrReplace(id, NotificationDefinition)`,
`Refresh(id)`, `StopAll()`, `TryConsumeLaunchPayload(out NotificationPayload)`,
`DumpActiveRegistry()`, and the **`public static event Action AppPaused`** hook.

### Builders (`LocalNotificationModels.cs`)

- **`NotificationDefinition(id)`** — `WithContent`, `WithSchedule`, `WithTriggerEvents(params string[])`,
  `WithEnabledPredicate(Func<bool>)`, `WithPayload(Func<NotificationPayload>)`,
  `WithPermissionPolicy`, `WithPlatformOptions`, `WithRefreshOnRegister(bool)`.
- **`NotificationContentTemplate`** — `WithTitleKey/WithBodyKey` (localized),
  `WithTitleText/WithBodyText` (literal), `WithTitleResolver/WithBodyResolver` (dynamic).
- **`NotificationScheduleRequest`** — `CreateDelay`, `CreateFireAtUtc`, `CreateFireAtLocal`,
  `WithCancelIfConditionFalse`.
- **`NotificationPlatformOptions.Default()`** — `WithAndroidChannel`, `WithAndroidIcons`,
  `WithIOSIdentifiers`, `WithForegroundPresentation`.
- **`NotificationPayload.Create(id, route, data)`** — `Serialize()` / `Deserialize()`.

### Enums

```csharp
NotificationPermissionStatus { Unknown, Granted, Denied, Unavailable }
NotificationPermissionPolicy { None, RequireAuthorization }   // default: RequireAuthorization
NotificationScheduleMode     { DelaySeconds, FireAtUtc, FireAtLocal }
```

---

## Scheduling modes

| Factory | Meaning | Repeat |
|---------|---------|--------|
| `NotificationScheduleRequest.CreateDelay(seconds, repeatInterval?)` | Fire `seconds` after registration. | Optional `TimeSpan`. |
| `CreateFireAtUtc(dateTimeUtc, repeatInterval?)` | Fire at an absolute UTC instant. | Optional. |
| `CreateFireAtLocal(dateTimeLocal, repeatInterval?)` | Fire at an absolute device-local instant. | Optional. |

A schedule is **valid** only when its delay `> 0` or its fire time is set; otherwise the notification is
cancelled on refresh.

> **iOS note:** repeating *delay* triggers reuse the initial delay as their interval (a Unity iOS
> constraint); a different `repeatInterval` on a delay schedule is logged and ignored.

---

## Content & localization

`NotificationContentTemplate` resolves text in three ways, checked in order:

1. **Resolver** (`WithTitleResolver` / `WithBodyResolver`, or the literal `WithTitleText` /
   `WithBodyText`) — fully dynamic.
2. **Localized key** (`WithTitleKey` / `WithBodyKey`) — resolved through
   `GameSystems.Localize(key, LocalizeCategory.Notification)`.
3. Empty string if neither is set.

> ⚠️ **Coupling:** key-based resolution calls `GameSystems.Localize` (project) and
> `Ezg.Package.Localize.LocalizeCategory`. If you extract this module into a fully standalone package,
> replace this with an injectable `Func<string,string>` localizer (see
> [packaging](#turning-this-into-a-standalone-package)). Literal text resolvers have no such coupling.

---

## The `AppPaused` hook (decoupling)

The engine fires an event when the app is backgrounded; **the project subscribes its rules to it.**
This is the seam that keeps the package free of game knowledge.

```csharp
// Package side (LocalNotificationService):
public static event Action AppPaused;     // raised from OnApplicationPause

// Project side (LocalNotificationRules, generated template):
[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
private static void RegisterHooks()
{
    LocalNotificationService.AppPaused -= RegisterPauseNotifications;
    LocalNotificationService.AppPaused += RegisterPauseNotifications;
}
```

`[RuntimeInitializeOnLoadMethod]` is invoked by Unity regardless of references, so the wiring happens
automatically without the package ever naming the project type.

---

## Project setup menu (template generator)

Menu: **`Create > Ezg > Local Notification > Project setup`**
(`Assets/_Project/Features/_Shared/LocalNotification/Editor/LocalNotificationSetupMenu.cs`)

- Writes `LocalNotificationScriptInfo.cs` into the module folder (resolved relative to the package, so
  it works wherever the package is installed; falls back to
  `Assets/_Project/Features/_Shared/LocalNotification`).
- If the file **already exists**, a dialog asks you to confirm **overwrite** — your existing
  implementation is otherwise preserved.
- The generated file is a **skeleton**: a sample `LocalNotificationScriptInfo`, the `AppPaused` wiring,
  a `RegisterPauseNotifications()` stub, and one sample rule with `// TODO:` markers.

Use it to scaffold a fresh project, or to regenerate a clean starting point. In **this** project the
file is already implemented (the full `PN001`–`PN010` rule set), so decline the overwrite unless you
intend to reset it.

---

## Permissions

- Android 13+ (`POST_NOTIFICATIONS`) and iOS authorization are requested on `Initialize()` /
  `RefreshPermissionStatus()`.
- A notification whose `PermissionPolicy` is `RequireAuthorization` (the default) is **cancelled** while
  the status is not `Granted`, and re-scheduled automatically once permission is granted.
- `OpenAppNotificationSettings()` deep-links to the OS settings page (Android intent / iOS
  `app-settings:`).

---

## Deep-link / launch payload

Attach a payload when registering, then read it after launch:

```csharp
new NotificationDefinition(id)
    .WithPayload(() => NotificationPayload.Create(id, route: "Shop", data: "starter_pack"));

// After the app starts from a notification tap:
if (LocalNotificationService.TryConsumeLaunchPayload(out var payload))
    Router.Open(payload.Route, payload.Data);
```

The payload is captured on `Start` / focus / resume and de-duplicated by fingerprint so it is delivered
once.

---

## Immediate native notification (`NotificationNativeBridge`)

Separate from the scheduling engine, `NotificationNativeBridge.ShowNotification(title, message,
smallIconName, largeIcon)` posts a notification **immediately** on Android through a custom java lib
(`com.mynoti.lib.NotificationHelper`). On non-Android platforms it logs a mock message. Use this only
for instant, here-and-now notifications; for anything scheduled, use the engine above.

---

## External dependencies

| Dependency | Used for | Where |
|------------|----------|-------|
| **Unity Mobile Notifications** (`com.unity.mobile.notifications`) | Android/iOS scheduling, channels, permission | `LocalNotificationPlatform.cs` |
| **UniTask** (Cysharp) | async permission/launch-payload flows | Service & Platform |
| **TigerForge `EventManager`** | event-driven `Refresh` triggers | Service (`WithTriggerEvents`) |
| **`Ezg.Package.Localize` + `GameSystems.Localize`** | key-based title/body localization | `LocalNotificationModels.cs` *(project coupling)* |
| Custom Android lib `com.mynoti.lib.NotificationHelper` | immediate native notification | `NotificationNativeBridge.cs` |

---

## Turning this into a standalone package

1. **Keep** in the package: `LocalNotificationManager/Service/Models/Platform.cs`,
   `NotificationNativeBridge.cs`, `Editor/LocalNotificationSetupMenu.cs`.
2. **Exclude** `LocalNotificationScriptInfo.cs` (it stays in the consumer project; the menu regenerates
   it).
3. **Break the localization coupling:** replace the `GameSystems.Localize` / `LocalizeCategory` calls in
   `NotificationContentTemplate` with an injectable localizer
   (e.g. `LocalNotificationService.Localizer = (key) => …;`) so the package compiles with no project
   types.
4. **Add assembly definitions:**
   - Runtime `Ezg.Feature.LocalNotification.asmdef` referencing UniTask, TigerForge, and
     `Unity.Notifications`.
   - Editor `Ezg.Feature.LocalNotification.Editor.asmdef` (Editor platform only) referencing the runtime
     asmdef.
   - Once an asmdef is present, the `internal` members in the service become assembly-private — the
     `AppPaused` event is already `public`, so the project wiring keeps working.
5. Add a `package.json` (UPM) if distributing via the Package Manager.

---

## Troubleshooting

| Symptom | Likely cause / fix |
|---------|--------------------|
| Nothing fires in the Editor | Expected — the Editor uses the **Unsupported** no-op provider. Test on device. |
| Notification never appears on Android 13+ | Permission not granted. Check `GetPermissionStatus()`; call `OpenAppNotificationSettings()`. |
| Duplicate notifications | Use the **same id** for the same logical notification; `RegisterOrReplace` dedupes by id. |
| Scheduled notification silently dropped | Schedule invalid (delay ≤ 0 or unset fire time), or the enabled predicate returned `false`. Check the `[LocalNotifications]` logs. |
| Pause-time rules don't run | The project `LocalNotificationRules.RegisterHooks` bootstrap didn't run — ensure `LocalNotificationScriptInfo.cs` exists in the project and compiles. |
| Compile error: duplicate `LocalNotificationScriptInfo` | You generated the template while the implemented file still exists. Keep a single `LocalNotificationScriptInfo.cs`. |
| `Localize` errors after extraction | You removed the project but kept key-based content. Provide an injectable localizer (step 3 above). |
```
