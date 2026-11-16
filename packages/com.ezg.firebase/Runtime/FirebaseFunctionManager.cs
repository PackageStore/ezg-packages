// Bỏ hẳn Cloud Functions khỏi bản build: thêm EZG_FIREBASE_NO_FUNCTIONS vào Scripting Define Symbols rồi xoá
// Firebase Functions SDK khỏi project (xem README, mục "Bỏ Firestore / Functions").
#if !EZG_FIREBASE_NO_FUNCTIONS
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
#endif
