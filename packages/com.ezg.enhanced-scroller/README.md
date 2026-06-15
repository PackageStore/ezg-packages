# com.ezg.enhanced-scroller

High-performance virtual scroller for Unity UI. Recycles cell views to support massive lists with minimal memory overhead.

## Source

Packaged from `Assets/_Project/3rdParty/EnhancedScroller v2/Plugins/` in the Merge Two source repository.

Original author: Echo Ridge Games — EnhancedScroller v2.

## Contents

| Package path | Source path |
|---|---|
| `Runtime/` | `Assets/_Project/3rdParty/EnhancedScroller v2/Plugins/` |

Demos, Documentation, and Tutorials from the original asset are excluded. Refer to the original EnhancedScroller v2 asset for demo scenes.

## Assembly

`Ezg.EnhancedScroller` — add this to `references` in your asmdef.

## Dependencies

None (pure `UnityEngine` / `UnityEngine.UI` / `System`).

## Peer Requirements

None — no third-party libraries required.

## Install

Add to `Packages/manifest.json`:

```json
"scopedRegistries": [{
  "name": "Easygoing code base",
  "url": "https://upm-registry-worker.developer-a1f.workers.dev",
  "scopes": ["com.ezg"]
}],
"dependencies": {
  "com.ezg.enhanced-scroller": "0.1.0"
}
```

## Phase 2 — Switch game to consumer

After this package is published and smoke-tested, Phase 2 removes
`Assets/_Project/3rdParty/EnhancedScroller v2/` from the source repo
and adds the `com.ezg.enhanced-scroller` dependency to `Packages/manifest.json`.
Consumer asmdefs (`ListItemInfo.cs`, `ListItemChefsBook.cs`, `ChefsStateType.cs` area)
need `Ezg.EnhancedScroller` added to their `references`.
