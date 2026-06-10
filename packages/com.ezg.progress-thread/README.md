# EZG Progress Thread (`com.ezg.progress-thread`)

Reusable Unity progress-thread and timeline helpers built on DOTween. The package provides repeat and linear timeline abstractions, stopwatch helpers, observable cooldown progress, and a small lifecycle update dispatcher.

## Package <-> source

Vendored from `Assets/_Project/Core/Modules/Threads` of the Merge Two project.
Assembly: **`Ezg.ProgressThread`**. Namespace: `Ezg.Package.ProgressThread`.

## Peer requirements

These are referenced by the assembly but are not `package.json` dependencies. The consuming project must provide them.

| Lib | Required? | Why |
|-----|-----------|-----|
| **DOTween** (assembly `DOTween.Modules`) | **Required** | Timeline, sequence, interval, update, and kill operations. |
| **Unity UI** (`UnityEngine.UI`) | **Required if using `ProgressView`** | Binds progress values to a `Slider`. |
| **Odin Inspector** (Sirenix) | Optional | Inspector display only. Odin attributes are guarded with `#if ODIN_INSPECTOR`. |

## Notes

The packaged copy is self-contained: `U` uses the module-local `SingletonTemporary<T>` instead of the host project's singleton helper.

## Basic usage

```csharp
using Ezg.Package.ProgressThread;

ProgressThread timer = null;
timer.Timer(5f, "example")
    .Subscribe(() => UnityEngine.Debug.Log("done"))
    .Start();
```
