using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Ezg.Package.Factory
{
    /// <summary>
    ///     Generic factory class to create instances of modules derived from DataWithOption mapped by their enum key.
    /// </summary>
    /// <typeparam name="TKeyType">The enum type defining the module key.</typeparam>
    /// <typeparam name="TData">The data option type derived from DataWithOption.</typeparam>
    public class FactoryGeneric<TKeyType, TData> where TKeyType : Enum where TData : DataWithOption<TKeyType>
    {
        #region Fields

        private static readonly Dictionary<TKeyType, Type> registeredModules;

        #endregion

        #region Initialize

        /// <summary>
        ///     Static constructor that scans assemblies to map each module type to its enum key.
        /// </summary>
        static FactoryGeneric()
        {
            var dataTypes = Assembly.GetAssembly(typeof(TData)).GetTypes()
                .Where(e => e.IsClass && !e.IsAbstract && e.IsSubclassOf(typeof(TData)));

            registeredModules = new Dictionary<TKeyType, Type>();

            foreach (var type in dataTypes)
            {
                var data = (TData)Activator.CreateInstance(type);
                if (data != null)
                {
                    if (!registeredModules.ContainsKey(data.type))
                        registeredModules.Add(data.type, type);
                    else
                        Debug.LogError("The same key: " + data.type + "\nRight: " + registeredModules[data.type] +
                                       "\n!!!Wrong: " + type);
                }
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Creates and returns a new instance of the module matching the specified enum type key.
        /// </summary>
        /// <param name="type">The enum identifier key of the module.</param>
        /// <returns>A new module instance of type TData, or null if not found.</returns>
        public static TData Get(TKeyType type)
        {
            if (registeredModules.ContainsKey(type))
            {
                var data = (TData)Activator.CreateInstance(registeredModules[type]);

                if (data.type != null) return data;
            }

            //Debug.LogError($"No exist type: {type.GetType()} {type}");
            return null;
        }

        #endregion
    }
}