using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

namespace Ezg.Package.Factory
{
    public abstract class DataPlayer : CacheFactoryGenericString<DataPlayerBase>
    {
        #region Fields

        private const string ALL_DATA = "ALL_DATA";

        public static bool IsNewPlayer;

        #endregion

        #region Initialize

        /// <summary>
        ///     Initializes the DataPlayer system by registering all DataPlayerBase modules.
        /// </summary>
        public new static void Init()
        {
            // Unsubscribe previously registered instances so iOS soft-restart flows
            // (ResetIOS) don't leave zombie EventManager listeners that respond to
            // the new session's events and overwrite freshly synced data.
            if (registeredModules != null)
                foreach (var existing in registeredModules.Values)
                    try
                    {
                        existing?.OnRemoveSaveLoadEvent();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[DataPlayer.Init] Dispose previous module failed: " + ex.Message);
                    }

            var coreAssemblyName = typeof(DataPlayerBase).Assembly.GetName().Name;
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
                .Where(e => e.IsClass && !e.IsAbstract && e.IsSubclassOf(typeof(DataPlayerBase)));

            registeredModules = new Dictionary<string, DataPlayerBase>();
            typeDict = new Dictionary<Type, string>();
            foreach (var type in dataTypes)
                try
                {
                    var data = (DataPlayerBase)Activator.CreateInstance(type);
                    if (data != null)
                    {
                        data.OnInitSaveLoadEvent();
                        if (!registeredModules.ContainsKey(data.type))
                        {
                            registeredModules.Add(data.type, data);
                            typeDict.Add(type, data.type);
                        }
                        else
                        {
                            registeredModules[data.type].OnRemoveSaveLoadEvent();
                            registeredModules.Remove(data.type);
                            registeredModules.Add(data.type, data);
                            Debug.LogError("The same key: " + data.type + "\nRight: " + registeredModules[data.type] +
                                           "\nWrong: " + type);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError(type + ": " + e.Message + "\n" + e.StackTrace);
                }
        }

        /// <summary>
        ///     Re-initializes registered DataPlayerBase modules.
        /// </summary>
        public static void ReInit()
        {
            foreach (var item in typeDict)
            {
                var data = (DataPlayerBase)Activator.CreateInstance(item.Key);
                if (data != null)
                {
                    data.OnInitSaveLoadEvent();
                    if (!registeredModules.ContainsKey(data.type))
                    {
                        registeredModules.Add(data.type, data);
                        typeDict.Add(item.Key, data.type);
                    }
                    else
                    {
                        registeredModules[data.type].OnRemoveSaveLoadEvent();
                        registeredModules.Remove(data.type);
                        registeredModules.Add(data.type, data);
                        Debug.LogError("The same key: " + data.type + "\nRight: " + registeredModules[data.type] +
                                       "\nWrong: " + item.Key);
                    }
                }
            }
        }

        /// <summary>
        ///     Clears data for all modules by creating fresh instances of each registered module type (used for cheat/debugging).
        /// </summary>
        public static void CheatClearData()
        {
            foreach (var type in typeDict)
            {
                var data = (DataPlayerBase)Activator.CreateInstance(type.Key);
                if (data != null)
                {
                    data.OnInitSaveLoadEvent();
                    if (!registeredModules.ContainsKey(data.type))
                    {
                        registeredModules.Add(data.type, data);
                    }
                    else
                    {
                        registeredModules[data.type].OnRemoveSaveLoadEvent();
                        registeredModules.Remove(data.type);
                        registeredModules.Add(data.type, data);
                        Debug.LogError("The same key: " + data.type + "\nRight: " + registeredModules[data.type] +
                                       "\nWrong: " + type);
                    }
                }
            }
        }

        /// <summary>
        ///     Clears all registered modules and removes their event subscriptions.
        /// </summary>
        public static void ClearData()
        {
            var dict = GetRegisteredModules();
            foreach (var playerData in dict) playerData.Value.OnRemoveSaveLoadEvent();

            dict.Clear();

            CheatClearData();
        }

        /// <summary>
        ///     Removes EventManager subscriptions of all currently registered modules
        ///     without clearing the lookup dictionary. Called before scene teardown so
        ///     stale instances stop reacting to events; Init() will replace them with
        ///     fresh instances afterwards.
        /// </summary>
        public static void DisposeAllModuleSubscriptions()
        {
            if (registeredModules == null) return;
            foreach (var module in registeredModules.Values)
                try
                {
                    module?.OnRemoveSaveLoadEvent();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[DataPlayer.DisposeAllModuleSubscriptions] " + ex.Message);
                }
        }

        #endregion

        #region Public Methods

        #region Save/Load Operations

        /// <summary>
        ///     Saves all registered player data modules to local storage (PlayerPrefs).
        /// </summary>
        public static void SaveAllData()
        {
            var allData = GetRegisteredModules();
            foreach (var data in allData) data.Value?.Save();
        }

        /// <summary>
        ///     Loads all registered player data modules from local storage (PlayerPrefs).
        /// </summary>
        public static void LoadAllData()
        {
            var allData = GetRegisteredModules();
            foreach (var data in allData) data.Value?.Load();
        }

        /// <summary>
        ///     Saves the data of a single specific module type.
        /// </summary>
        /// <param name="type">The module type key to save.</param>
        public static void SaveData(string type)
        {
            GetModule(type).Save();
        }

        /// <summary>
        ///     Reloads all registered modules from local storage (PlayerPrefs) and saves the registry state.
        /// </summary>
        public static void ReloadAllData()
        {
            foreach (var sav in GetRegisteredModules())
            {
                var module = GetModule(sav.Key);
                if (module != null) module.Load();
            }

            PlayerPrefs.Save();
        }

        /// <summary>
        ///     Clears all PlayerPrefs data and unlinks active modules.
        /// </summary>
        public static void ClearAllData()
        {
            foreach (var sav in GetRegisteredModules())
            {
                var module = GetModule(sav.Key);
                if (module != null) module = null;
            }

            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
        }

        #endregion

        #region JSON & Serialization Helpers

        /// <summary>
        ///     Generates and returns a single JSON string containing all serialized module data.
        /// </summary>
        /// <returns>A JSON string containing the serialized modules.</returns>
        public static string GetJson()
        {
            var data = new Dictionary<string, string>();
            var resgisteredModules = GetRegisteredModules();
            foreach (var _data in resgisteredModules)
            {
                //data.Add(_data.Key, _data.Value.GetDataJson());
            }

            var json = JsonConvert.SerializeObject(data, DataPlayerBase.JsonConvertSettings);
            PlayerPrefs.SetString(ALL_DATA, json);
            return json;
        }

        /// <summary>
        ///     Restores the player data from a JSON string and synchronizes it.
        /// </summary>
        /// <param name="json">The JSON string containing the serialized modules.</param>
        public static void SetJson(string json)
        {
            PlayerPrefs.SetString(ALL_DATA, json);
            LoadDataFromServer();
        }

        /// <summary>
        ///     Converts a raw JSON string of all player data into a dictionary of module types and their raw JSON strings.
        /// </summary>
        /// <param name="dataJson">The JSON string containing all player data.</param>
        /// <returns>A dictionary of module type keys to raw JSON string values.</returns>
        public static Dictionary<string, string> ConvertToDataDict(string dataJson)
        {
            return JsonConvert.DeserializeObject<Dictionary<string, string>>(dataJson,
                DataPlayerBase.JsonConvertSettings);
        }

        /// <summary>
        ///     Retrieves the JSON string data for a specific module type from the deserialized player data dictionary.
        /// </summary>
        /// <param name="allDataDict">The dictionary containing all deserialized player data modules.</param>
        /// <param name="type">The type string of the target module.</param>
        /// <returns>The matching DataPlayerBase instance or null if not found.</returns>
        public static DataPlayerBase GetDataFromJson(Dictionary<string, string> allDataDict, string type)
        {
            foreach (var _data in allDataDict)
            {
                var jsonData = _data.Value;

                var dataPlayer =
                    _data.Key;
                if (dataPlayer != type) continue;

                if (jsonData == "") continue;
                // var dataSynchronize = GetModule(dataPlayer);
                // dataSynchronize.SynchronizeData(jsonData);
                // return dataSynchronize;
            }

            return null;
        }

        #endregion

        #region Server Synchronization

        /// <summary>
        ///     Loads and synchronizes all player data from the server.
        /// </summary>
        public static void LoadDataFromServer()
        {
            // var allDataDict =
            //     JsonConvert.DeserializeObject<Dictionary<DataPlayerType, string>>(PlayerPrefs.GetString(ALL_DATA));
            //
            // foreach (var _data in allDataDict)
            // {
            //     var jsonData = _data.Value;
            //
            //     DataPlayerType dataPlayer = (DataPlayerType)Enum.Parse(typeof(DataPlayerType),
            //         _data.Key.ToString());
            //
            //     if (jsonData == "") continue;
            //     var dataSynchronize = GetModule(dataPlayer);
            //     dataSynchronize.SynchronizeData(jsonData);
            // }
        }

        /// <summary>
        ///     Updates all data in preparation to save to the server.
        /// </summary>
        public static void UpdateAllDataToSave()
        {
            // Dictionary<DataPlayerType, string> data = new Dictionary<DataPlayerType, string>();
            // var resgisteredModules = GetRegisteredModules();
            // foreach (var _data in resgisteredModules)
            // {
            //     data.Add(_data.Key, _data.Value.GetDataJson());
            // }
            //
            // PlayerPrefs.SetString(ALL_DATA, JsonConvert.SerializeObject(data));
        }

        /// <summary>
        ///     Retrieves all player data in JSON string format.
        /// </summary>
        /// <returns>A JSON string representing all player data stored in PlayerPrefs.</returns>
        public static string GetAllData()
        {
            UpdateAllDataToSave();
            return PlayerPrefs.GetString(ALL_DATA);
        }

        /// <summary>
        ///     Saves all player data as a JSON string to PlayerPrefs.
        /// </summary>
        /// <param name="data">The JSON string representation of all player data.</param>
        public static void SetAllData(string data)
        {
            PlayerPrefs.SetString(ALL_DATA, data);
        }

        #endregion

        #endregion
    }

    /// <summary>
    ///     Abstract base class representing user data modules.
    /// </summary>
    public abstract class DataBase
    {
    }

    /// <summary>
    ///     Generic abstract base class for player data modules managing serialization and persistence of a specific database
    ///     type.
    /// </summary>
    /// <typeparam name="T">The type of the underlying database representation.</typeparam>
    public abstract class DataPlayerBaseGeneric<T> : DataPlayerBase
    {
        #region Fields

        /// <summary>
        ///     The database object containing the actual player data.
        /// </summary>
        [JsonProperty("database")] public T dataBase;

        #endregion

        #region Initialize

        /// <summary>
        ///     Initializes a fresh instance of the database object, sets default data, and saves it.
        /// </summary>
        protected virtual void InitData()
        {
            dataBase = Activator.CreateInstance<T>();
            SetupDefaultData();
            Save();
        }

        /// <summary>
        ///     Hook method to configure default data properties.
        /// </summary>
        protected virtual void SetupDefaultData()
        {
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Serializes the database object into a JSON string.
        /// </summary>
        /// <returns>A JSON string representing the database object.</returns>
        public override string GetDataJson()
        {
            return JsonConvert.SerializeObject(dataBase, JsonConvertSettings);
        }

        /// <summary>
        ///     Synchronizes the local database object with the JSON data string provided.
        /// </summary>
        /// <param name="data">The JSON string containing the data to synchronize.</param>
        public override void SynchronizeData(string data)
        {
            PlayerPrefs.SetString(type, data);
            try
            {
                dataBase = JsonConvert.DeserializeObject<T>(data, JsonConvertSettings);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[DataPlayer] SynchronizeData FAILED for module '{type}': {e.Message}\nData length: {data?.Length}");
            }

            AfterSynchroData();
        }

        /// <summary>
        ///     Loads the database object from PlayerPrefs local storage.
        /// </summary>
        public override void Load()
        {
            try
            {
                dataBase = JsonConvert.DeserializeObject<T>(PlayerPrefs.GetString(type), JsonConvertSettings);
            }
            catch (Exception)
            {
                // Suppress deserialization exceptions and handle recovery below
            }

            if (dataBase == null) InitData();

            AfterLoad();
        }

        /// <summary>
        ///     Saves the database object to PlayerPrefs local storage.
        /// </summary>
        public override void Save()
        {
            PlayerPrefs.SetString(type, JsonConvert.SerializeObject(dataBase, JsonConvertSettings));
            PlayerPrefs.Save();
        }

        /// <summary>
        ///     Clears the stored database object from local storage and re-initializes it.
        /// </summary>
        public override void Clear()
        {
            PlayerPrefs.DeleteKey(type);
            InitData();
        }

        #endregion

        #region Private Methods

        /// <summary>
        ///     Hook method called immediately after data synchronization.
        /// </summary>
        protected virtual void AfterSynchroData()
        {
        }

        /// <summary>
        ///     Hook method called immediately after loading the data.
        /// </summary>
        protected virtual void AfterLoad()
        {
        }

        #endregion
    }

    public abstract class DataPlayerBase : DataWithOptionString
    {
        #region Fields

        private static JsonSerializerSettings _jsonConvertSettings;

        #endregion

        #region Initialize

        /// <summary>
        ///     Initializes a new instance of the DataPlayerBase class and assigns its type name.
        /// </summary>
        public DataPlayerBase()
        {
            type = GetType().ToString();
        }

        /// <summary>
        ///     Initializes the module by loading its data and subscribing to events.
        /// </summary>
        public void OnInitSaveLoadEvent()
        {
            RemoveEvent();
            Load();
            InitEvent();
        }

        /// <summary>
        ///     Cleans up the module by unsubscribing from all events.
        /// </summary>
        public void OnRemoveSaveLoadEvent()
        {
            RemoveEvent();
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Gets or sets the custom JSON serializer settings used for player data persistence.
        /// </summary>
        public static JsonSerializerSettings JsonConvertSettings
        {
            get
            {
                if (_jsonConvertSettings == null)
                    JsonConvertSettings = new JsonSerializerSettings
                    {
                        TypeNameHandling = TypeNameHandling.All,
                        MissingMemberHandling = MissingMemberHandling.Ignore,
                        SerializationBinder = new CrossAssemblySerializationBinder(),
                        Error = (sender, args) =>
                        {
                            Debug.LogWarning(
                                $"[DataPlayer] JSON error at '{args.ErrorContext.Path}': {args.ErrorContext.Error.Message}");
                            args.ErrorContext.Handled = true;
                        }
                    };

                return _jsonConvertSettings;
            }
            set => _jsonConvertSettings = value;
        }

        /// <summary>
        ///     Synchronizes raw JSON data string.
        /// </summary>
        /// <param name="data">The JSON data string.</param>
        public abstract void SynchronizeData(string data);

        /// <summary>
        ///     Retrieves the serialized JSON representation of the module's data.
        /// </summary>
        /// <returns>A JSON string containing the serialized module data.</returns>
        public abstract string GetDataJson();

        /// <summary>
        ///     Saves the module data.
        /// </summary>
        public abstract void Save();

        /// <summary>
        ///     Loads the module data.
        /// </summary>
        public abstract void Load();

        /// <summary>
        ///     Clears the module data.
        /// </summary>
        public abstract void Clear();

        #endregion

        #region Private Methods

        /// <summary>
        ///     Registers event listeners for this module.
        /// </summary>
        protected virtual void StartListening()
        {
        }

        /// <summary>
        ///     Unregisters event listeners for this module.
        /// </summary>
        protected virtual void StopListening()
        {
        }

        /// <summary>
        ///     Subscribes to application status and custom events.
        /// </summary>
        private void InitEvent()
        {
            //EventManager.StartListening(EventName.ApplicationStatus.Pause, Save);
            //EventManager.StartListening(EventName.ApplicationStatus.Quit, Save);
            //EventManager.StartListening(EventName.ApplicationStatus.Destroy, Save);
            StartListening();
        }

        /// <summary>
        ///     Unsubscribes from application status and custom events.
        /// </summary>
        private void RemoveEvent()
        {
            //EventManager.StopListening(EventName.ApplicationStatus.Pause, Save);
            //EventManager.StopListening(EventName.ApplicationStatus.Quit, Save);
            //EventManager.StopListening(EventName.ApplicationStatus.Destroy, Save);
            StopListening();
        }

        #endregion
    }
}