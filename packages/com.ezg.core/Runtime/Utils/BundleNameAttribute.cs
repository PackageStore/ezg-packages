using System;

namespace Ezg.Core.Utils
{
    /// <summary>
    ///     Declares the bundle name that contains this feature's prefab screen.
    ///     Use this when the bundle name differs from `featureType.ToString().ToLower()`.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class BundleNameAttribute : Attribute
    {
        /// <summary>
        ///     Creates a new bundle name attribute.
        /// </summary>
        /// <param name="name">The bundle name.</param>
        public BundleNameAttribute(string name)
        {
            Name = name;
        }

        /// <summary>
        ///     Gets the bundle name.
        /// </summary>
        public string Name { get; }
    }
}