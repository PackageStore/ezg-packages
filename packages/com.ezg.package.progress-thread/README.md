# EZG Progress Thread (`com.ezg.package.progress-thread`)

DOTween-backed runtime utilities for timelines, stopwatches, repeat timers, and observable progress values.

## Package source

Vendored from `Assets/_Project/Core/Modules/Threads` of the Merge Two project.
Assembly: **`Ezg.Package.ProgressThread`**. Namespace: **`Ezg.Package.ProgressThread`**.

## Peer requirements

These are referenced by the assembly but are not `package.json` dependencies. The consuming project must provide them:

| Lib | Required? | Why |
|-----|-----------|-----|
| DOTween (`DOTween.Modules`) | Required | Timeline and stopwatch sequences. |
| Unity UI (`UnityEngine.UI`) | Required | `ProgressView` uses `Image`. |
| Odin Inspector | Optional | Inspector attributes are wrapped in `#if ODIN_INSPECTOR`. |

## Notes

The package copy is self-contained: `U` uses the module-local `SingletonTemporary<U>` instead of the source project's singleton helper.