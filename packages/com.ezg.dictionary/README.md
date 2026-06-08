# com.ezg.dictionary

Generic `SerializableDictionary<K, V>` with full Unity serialization support and custom Editor property drawers.

## Source

Extracted from `Assets/_Project/Core/Localize/Dictionary` in the Merge Two game project.

## Assemblies

| Assembly | Purpose |
|---|---|
| `Ezg.Dictionary` | Runtime — `SerializableDictionary<K,V>`, `StringIntDictionary`, `StringStringDictionary`, `StringSpriteDictionary` |
| `Ezg.Dictionary.Editor` | Editor-only — property drawers for Inspector display |

## Dependencies

None — pure Unity/BCL module. No `com.ezg.*` dependencies.

## Peer Requirements

None.

## Install

Add to your project's `Packages/manifest.json`:

```json
"scopedRegistries": [{
  "name": "Easygoing code base",
  "url": "https://upm-registry-worker.developer-a1f.workers.dev",
  "scopes": ["com.ezg"]
}],
"dependencies": {
  "com.ezg.dictionary": "0.1.0"
}
```
