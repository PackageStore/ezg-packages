# Changelog

## [0.1.0] - 2026-06-15

- Initial publish. Vendors the Supabase C# client (0.16.2) and its transitive
  dependencies as a self-contained DLL bundle (15 `netstandard2.0` assemblies).
- Stripped NuGet-restore artifacts (`*.nupkg`, `*.signature.p7s`) — runtime DLLs and
  their Unity `.meta` files preserved verbatim.
- Added empty `Ezg.Supabase` marker asmdef to satisfy registry validation.
- Newtonsoft.Json 13.x is documented as a peer requirement (not bundled).
