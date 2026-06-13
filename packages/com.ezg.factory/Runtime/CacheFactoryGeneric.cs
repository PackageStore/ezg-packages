using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Ezg.Package.Factory
{
    /// <summary>
    ///     Data wrapper that contains an enum type key for module categorization.
    /// </summary>
    /// <typeparam name="KeyCollection">The enum type defining the module key.</typeparam>
    public class DataWithOption<KeyCollection> where KeyCollection : Enum
    {
        #region Fields

        /// <summary>
        ///     The unique module identifier of enum type KeyCollection.
        /// </summary>
        [JsonProperty("type")] public KeyCollection type;

        #endregion
    }

    /// <summary>
    ///     Data wrapper that contains a string type key for module categorization.
    /// </summary>
    public class DataWithOptionString
    {
        #region Fields

        /// <summary>
        ///     The unique module identifier string.
        /// </summary>
        [JsonProperty("type")] public string type;

        #endregion
    }

    /// <summary>
    ///     Generic factory class to cache, register, and retrieve modules that use string type keys.
    /// </summary>
    /// <typeparam name="TData">The data option type derived from DataWithOptionString.</typeparam>
    public class CacheFactoryGenericString<TData> where TData : DataWithOptionString
    {
        #region Initialize

        /// <summary>
        ///     Scans the assembly to dynamically locate, instantiate, and register all concrete module types.
        /// </summary>
        public static void Init()
        {
            var coreAssemblyName = typeof(TData).Assembly.GetName().Name;
            var dataTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == coreAssemblyName ||
                            a.GetReferencedAssemblies().Any(r => r.Name == coreAssemblyName))
                .SelectMany(a =>
                {
                    try
                    {
                        return a.GetTypes();
                    }
                    catch
                    {
                        return Array.Empty<Type>();
                    }
                })
                .Where(e => e.IsClass && !e.IsAbstract && e.IsSubclassOf(typeof(TData)));

            registeredModules = new Dictionary<string, TData>();
            typeDict = new Dictionary<Type, string>();
            foreach (var type in dataTypes)
            {
                var data = (TData)Activator.CreateInstance(type);
                if (data != null)
                    if (!registeredModules.ContainsKey(data.type))
                    {
                        registeredModules.Add(data.type, data);
                        typeDict.Add(type, data.type);
                    }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Ensures that the module registries are initialized.
        /// </summary>
        private static void ValidInit()
        {
            if (typeDict == null) Init();
        }

        #endregion

        #region Fields

        /// <summary>
        ///     Cache of registered modules mapped by their string type key.
        /// </summary>
        protected static Dictionary<string, TData> registeredModules;

        /// <summary>
        ///     Cache mapping module C# Types to their registered string keys.
        /// </summary>
        protected static Dictionary<Type, string> typeDict;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Gets all registered modules in a dictionary.
        /// </summary>
        /// <returns>A dictionary of string type keys to module instances.</returns>
        public static Dictionary<string, TData> GetRegisteredModules()
        {
            return registeredModules;
        }

        /// <summary>
        ///     Retrieves a specific module instance by its generic type.
        /// </summary>
        /// <typeparam name="T">The concrete module type to retrieve.</typeparam>
        /// <returns>The registered module instance of type T, or null if not found.</returns>
        public static T GetModule<T>() where T : TData
        {
            ValidInit();
            var type = typeof(T);
            if (typeDict.ContainsKey(type))
                if (registeredModules.ContainsKey(typeDict[type]))
                    return (T)registeredModules[typeDict[type]];

            return null;
        }

        /// <summary>
        ///     Retrieves a specific module instance by its registered string type key.
        /// </summary>
        /// <param name="type">The string identifier key of the module.</param>
        /// <returns>The registered module instance, or null if not found.</returns>
        public static TData GetModule(string type)
        {
            ValidInit();

            if (registeredModules.ContainsKey(type)) return registeredModules[type];

            return null;
        }

        #endregion
    }

    /// <summary>
    ///     Generic factory class to cache, register, and retrieve modules that use enum type keys.
    /// </summary>
    /// <typeparam name="TKeyType">The enum type defining the module key.</typeparam>
    /// <typeparam name="TData">The data option type derived from DataWithOption.</typeparam>
    public class CacheFactoryGeneric<TKeyType, TData> where TKeyType : Enum where TData : DataWithOption<TKeyType>
    {
        #region Initialize

        /// <summary>
        ///     Scans the assembly to dynamically locate, instantiate, and register all concrete module types.
        /// </summary>
        public static void Init()
        {
            var coreAssemblyName = typeof(TData).Assembly.GetName().Name;
            var dataTypes = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == coreAssemblyName ||
                            a.GetReferencedAssemblies().Any(r => r.Name == coreAssemblyName))
                .SelectMany(a =>
                {
                    try
                    {
                        return a.GetTypes();
                    }
                    catch
                    {
                        return Array.Empty<Type>();
                    }
                })
                .Where(e => e.IsClass && !e.IsAbstract && e.IsSubclassOf(typeof(TData)));

            registeredModules = new Dictionary<TKeyType, TData>();
            typeDict = new Dictionary<Type, TKeyType>();
            foreach (var type in dataTypes)
            {
                var data = (TData)Activator.CreateInstance(type);
                if (data != null)
                    if (!registeredModules.ContainsKey(data.type))
                    {
                        registeredModules.Add(data.type, data);
                        typeDict.Add(type, data.type);
                    }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Ensures that the module registries are initialized.
        /// </summary>
        private static void ValidInit()
        {
            if (typeDict == null) Init();
        }

        #endregion

        #region Fields

        /// <summary>
        ///     Cache of registered modules mapped by their enum type key.
        /// </summary>
        protected static Dictionary<TKeyType, TData> registeredModules;

        /// <summary>
        ///     Cache mapping module C# Types to their registered enum keys.
        /// </summary>
        protected static Dictionary<Type, TKeyType> typeDict;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Gets all registered modules in a dictionary.
        /// </summary>
        /// <returns>A dictionary of enum type keys to module instances.</returns>
        public static Dictionary<TKeyType, TData> GetRegisteredModules()
        {
            return registeredModules;
        }

        /// <summary>
        ///     Retrieves a specific module instance by its generic type.
        /// </summary>
        /// <typeparam name="T">The concrete module type to retrieve.</typeparam>
        /// <returns>The registered module instance of type T, or null if not found.</returns>
        public static T GetModule<T>() where T : TData
        {
            var type = typeof(T);

            ValidInit();

            if (typeDict.ContainsKey(type))
                if (registeredModules.ContainsKey(typeDict[type]))
                    return (T)registeredModules[typeDict[type]];

            return null;
        }

        /// <summary>
        ///     Retrieves a specific module instance by its registered enum type key.
        /// </summary>
        /// <param name="type">The enum identifier key of the module.</param>
        /// <returns>The registered module instance, or null if not found.</returns>
        public static TData GetModule(TKeyType type)
        {
            ValidInit();

            if (registeredModules.ContainsKey(type)) return registeredModules[type];

            return null;
        }

        #endregion
    }
}