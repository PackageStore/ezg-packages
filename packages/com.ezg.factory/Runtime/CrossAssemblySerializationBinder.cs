using System;
using Newtonsoft.Json.Serialization;

namespace Ezg.Package.Factory
{
    /// <summary>
    ///     Handles Newtonsoft JSON type resolution when types move between assemblies.
    ///     Required after introducing Ezg.Core.asmdef — types previously in Assembly-CSharp
    ///     (e.g. EnumBase) are now in Ezg.Core, but existing save data still references
    ///     the old assembly name. This binder falls back across known assemblies.
    /// </summary>
    public class CrossAssemblySerializationBinder : DefaultSerializationBinder
    {
        #region Fields

        private static readonly string[] FallbackAssemblies =
        {
            "Assembly-CSharp",
            "Ezg.Core",
            "Assembly-CSharp-firstpass"
        };

        #endregion

        #region Public Methods

        /// <summary>
        ///     Binds the specified assembly name and type name to a resolved Type instance, falling back across known assemblies
        ///     if necessary.
        /// </summary>
        /// <param name="assemblyName">The name of the assembly containing the type.</param>
        /// <param name="typeName">The name of the type to bind.</param>
        /// <returns>The resolved Type, or null if it cannot be found.</returns>
        public override Type BindToType(string assemblyName, string typeName)
        {
            // base.BindToType returns null on failure (does NOT throw) — check for null
            try
            {
                var result = base.BindToType(assemblyName, typeName);
                if (result != null) return result;
            }
            catch
            {
            }

            // Try fallback assemblies
            foreach (var asm in FallbackAssemblies)
            {
                if (asm == assemblyName) continue;
                try
                {
                    var result = base.BindToType(asm, typeName);
                    if (result != null) return result;
                }
                catch
                {
                }
            }

            // Last resort: scan all loaded assemblies by type name only
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                try
                {
                    var t = assembly.GetType(typeName);
                    if (t != null) return t;
                }
                catch
                {
                }

            return null;
        }

        #endregion
    }
}