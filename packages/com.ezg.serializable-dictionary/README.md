# EZG Serializable Dictionary

Unity serializable dictionary with Inspector support.

## Classes

| Class | Description |
|---|---|
| `SerializableDictionary<K, V>` | Generic base, supports Unity serialization via `ISerializationCallbackReceiver` |
| `StringIntDictionary` | `string → int` |
| `StringStringDictionary` | `string → string` |
| `StringSpriteDictionary` | `string → Sprite` |

Namespace: `Easygoing.Packages.Dictionary`

## Source mapping

`Assets/_Project/Core/Localize/Dictionary` in game repo `m1`.

## Peer requirements

None — only depends on `UnityEngine` and `UnityEditor` (standard).
