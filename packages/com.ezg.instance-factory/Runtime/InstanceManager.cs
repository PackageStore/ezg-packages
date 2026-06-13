using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Ezg.Packages.InstanceFactory
{
    /// <summary>
    ///     Utility class for retrieving instances of classes implementing or inheriting from a specified type.
    /// </summary>
    public static class InstanceManager
    {
        #region Initialize

        /// <summary>
        ///     Static constructor for the InstanceManager class.
        /// </summary>
        static InstanceManager()
        {
            //GetEnumerableOfType<InstanceManager>(null);
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Retrieves an enumerable collection of instantiated objects of subclasses of type T.
        /// </summary>
        /// <typeparam name="T">The base type or class.</typeparam>
        /// <param name="constructorArgs">Optional constructor arguments for instantiation.</param>
        /// <returns>A collection of instantiated objects of subclasses of type T.</returns>
        public static IEnumerable<T> GetEnumerableOfType<T>(params object[] constructorArgs) where T : class
        {
            var objects = new List<T>();
            foreach (var type in
                     Assembly.GetAssembly(typeof(T)).GetTypes()
                         .Where(myType => myType.IsClass && !myType.IsAbstract && myType.IsSubclassOf(typeof(T))))
                objects.Add((T)Activator.CreateInstance(type, constructorArgs));
            objects.Sort();
            return objects;
        }

        #endregion
    }
}