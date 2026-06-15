using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Firebase.Extensions;
using Firebase.Firestore;
using UnityEngine;
using UnityEngine.Events;
using Random = System.Random;

namespace Ezg.Core.Firebase
{
    /// <summary>
    ///     Manages Firebase Firestore database operations, collections, and document retrieval.
    /// </summary>
    public static class FirebaseFirestoreManager
    {
        #region Initialize

        /// <summary>
        ///     Initializes the default Firestore database instance.
        /// </summary>
        public static void Init()
        {
            db = FirebaseFirestore.DefaultInstance;
        }

        #endregion

        #region Fields

        /// <summary>
        ///     Represents the current execution status of a Firestore operation.
        /// </summary>
        public enum FirestoreStatus
        {
            None,
            Waiting,
            Success,
            Faulted
        }

        public static FirestoreStatus WaitingStatus;

        private static FirebaseFirestore db;
        private static readonly Random random = new();

        #endregion

        #region Public Methods

        /// <summary>
        ///     Gets the active FirebaseFirestore instance.
        /// </summary>
        /// <returns>The active FirebaseFirestore database instance.</returns>
        public static FirebaseFirestore Data()
        {
            return db;
        }

        /// <summary>
        ///     Gets the active FirebaseFirestore instance.
        /// </summary>
        /// <returns>The active FirebaseFirestore database instance.</returns>
        public static FirebaseFirestore GetSource()
        {
            return db;
        }

        /// <summary>
        ///     Retrieves all documents in a collection and converts them to the specified type.
        /// </summary>
        /// <typeparam name="T">The type to convert the documents into.</typeparam>
        /// <param name="result">The list to populate with the retrieved documents.</param>
        /// <param name="link">The collection path.</param>
        /// <param name="onSuccess">Callback executed on successful retrieval.</param>
        /// <param name="onFailed">Callback executed on query failure.</param>
        public static void GetAllDocuments<T>(this List<T> result, string link, UnityAction onSuccess,
            UnityAction onFailed)
        {
            if (db == null) return;

            var citiesRef = db.Collection(link);
            citiesRef.GetSnapshotAsync().ContinueWithOnMainThread(events =>
            {
                if (events.Status != TaskStatus.Faulted)
                {
                    WaitingStatus = FirestoreStatus.Success;
                    foreach (var e in events.Result.Documents) result.Add(e.ConvertTo<T>());

                    onSuccess?.Invoke();
                }
                else
                {
                    WaitingStatus = FirestoreStatus.Faulted;
                    onFailed?.Invoke();
                }
            });
        }

        /// <summary>
        ///     Retrieves a single document and converts it to the specified type.
        /// </summary>
        /// <typeparam name="T">The type to convert the document into.</typeparam>
        /// <param name="result">The object to store the retrieved document data.</param>
        /// <param name="link">The collection path.</param>
        /// <param name="doc">The document ID.</param>
        /// <param name="onSuccess">Callback executed on successful retrieval.</param>
        /// <param name="onFailed">Callback executed on query failure.</param>
        public static void GetDocument<T>(this T result, string link, string doc, UnityAction onSuccess,
            UnityAction onFailed)
        {
            if (db == null) return;

            var citiesRef = db.Collection(link).Document(doc);
            citiesRef.GetSnapshotAsync().ContinueWithOnMainThread(x =>
            {
                if (x.Status != TaskStatus.Faulted)
                {
                    WaitingStatus = FirestoreStatus.Success;
                    var a = x.Result;
                    result = a.ConvertTo<T>();

                    onSuccess?.Invoke();
                }
                else
                {
                    WaitingStatus = FirestoreStatus.Faulted;
                    onFailed?.Invoke();
                }
            });
        }

        /// <summary>
        ///     Generates a random alphanumeric string of the specified length.
        /// </summary>
        /// <param name="length">Length of the generated string.</param>
        /// <returns>A random alphanumeric string.</returns>
        public static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        #endregion
    }
}