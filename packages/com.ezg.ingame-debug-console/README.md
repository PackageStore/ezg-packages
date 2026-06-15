# EZG Ingame Debug Console

Runtime debug console for viewing logs and executing commands in-game. Based on
[yasirkula's In-game Debug Console](https://github.com/yasirkula/UnityIngameDebugConsole).

This package ↔ source folder: `Assets/Plugins/IngameDebugConsole`.

## Usage

Drag the **`IngameDebugConsole`** prefab into your scene. It is located in the
Project window under `Packages/EZG Ingame Debug Console/IngameDebugConsole.prefab`.

Package contents are immutable. To customize the prefab, create a **prefab variant**
in your own `Assets/` folder (see the included `DecorVariants/` example).

## Dependencies

No `com.ezg.*` dependencies.

## Peer requirements (consumer project must already provide)

- `com.unity.inputsystem` — used by `DebugLogManager` (the runtime asmdef
  auto-references `Unity.InputSystem`).
- `com.unity.ugui` — Unity UI.
- TextMeshPro — if the console prefab is configured to use TMP text.

## Platform plugins included

- Android (`IngameDebugConsole.aar`)
- iOS (`IngameDebugConsole.mm`)
- WebGL (`IngameDebugConsole.jslib`)
