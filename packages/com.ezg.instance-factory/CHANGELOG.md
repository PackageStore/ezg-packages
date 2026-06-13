# Changelog

## [0.1.0] - 2026-06-13

Initial publish. Extracted from `Assets/_Project/Core/Patterns/InstanceFactory`.

- `InstanceFactory`: compiled expression-tree constructor cache (0–3 typed args + params overload)
- `InstanceFactoryGeneric<TArg1,TArg2,TArg3>`: generic helper used internally
- `InstanceManager.GetEnumerableOfType<T>`: enumerate and instantiate all concrete subclasses
- `TypeToIgnore`: sentinel type for ignored constructor slots
