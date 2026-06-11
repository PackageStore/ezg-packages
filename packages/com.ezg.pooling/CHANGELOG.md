# Changelog

All notable changes to **com.ezg.pooling** are documented here.

## [0.1.0] - 2026-06-11

### Added
- Initial extraction of the pooling module from the game project into a standalone UPM package.
- `PoolService` static facade, `PoolingManager`, `SpawnerManager`, `PoolingComponent`,
  `PoolingDataModel<T>`, and `IPoolingModule`.
- Vendored `Singleton<T>` and a `PoolResources` prefab-resolver hook so the package is self-contained
  (no host `Core` assembly dependency). `PoolResources.Loader` defaults to `Resources` and is overridable.
