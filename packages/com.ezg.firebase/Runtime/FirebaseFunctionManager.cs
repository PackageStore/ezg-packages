using Firebase.Functions;

namespace Ezg.Core.Firebase
{
    /// <summary>
    ///     Manages the Firebase Functions default instance.
    /// </summary>
    public static class FirebaseFunctionManager
    {
        #region Fields

        public static FirebaseFunctions Function;

        #endregion

        #region Initialize

        static FirebaseFunctionManager()
        {
            Function = FirebaseFunctions.DefaultInstance;
        }

        #endregion
    }
}