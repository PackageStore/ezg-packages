#if UNITY_IOS && !UNITY_EDITOR
#define EZG_IAP_FIREBASE_SK2_NATIVE
#endif

using System;
using System.Collections.Generic;
using UnityEngine;
#if EZG_IAP_FIREBASE_SK2_NATIVE
using System.Runtime.InteropServices;
#endif

namespace Ezg.Feature.IAP
{
    /// <summary>
    /// Cầu nối tới plugin Swift <c>Plugins/iOS/EzgIapFirebaseStoreKit2.swift</c>: ghi <c>in_app_purchase</c> lên Firebase
    /// Analytics cho giao dịch StoreKit 2 bằng API native <c>Analytics.logTransaction</c> (FirebaseAnalytics iOS ≥ 10.17.0).
    /// Không cần Firebase Unity SDK ≥ 13.12.0 (<c>LogAppleTransactionAsync</c>), module cũng không tham chiếu assembly
    /// Firebase nào — app không có Firebase thì plugin trả <see cref="RESULT_FIREBASE_UNAVAILABLE"/>.
    /// <para>Kết quả native về qua callback (luôn trên main thread) và chỉ được GHI vào bảng kết quả; caller đọc bằng
    /// <see cref="TryTakeResult"/> mỗi frame — không chạy logic game bên trong reverse P/Invoke.</para>
    /// </summary>
    internal static class FirebaseStoreKit2Bridge
    {
        #region Fields

        // Mã kết quả — phải khớp enum EzgIapSK2Result trong EzgIapFirebaseStoreKit2.swift.
        internal const int RESULT_NOT_FOUND = 0;
        internal const int RESULT_LOGGED = 1;
        internal const int RESULT_OS_UNSUPPORTED = -1;
        internal const int RESULT_FIREBASE_UNAVAILABLE = -2;
        internal const int RESULT_INVALID_TRANSACTION_ID = -3;
        internal const int RESULT_FIREBASE_NOT_CONFIGURED = -4;

        private static readonly object s_Lock = new object();

        // requestId đang chờ native trả lời. Lượt bị bỏ (quá hạn) xoá khỏi đây → callback về muộn bị bỏ qua.
        private static readonly HashSet<int> s_InFlight = new HashSet<int>();
        private static readonly Dictionary<int, int> s_Results = new Dictionary<int, int>();

#if EZG_IAP_FIREBASE_SK2_NATIVE
        private static int s_LastRequestId;

        // App không link FirebaseAnalytics / gọi plugin lỗi → không hoãn confirm ở các lượt mua sau nữa.
        private static volatile bool s_Disabled;
#endif

        #endregion

        #region Public

        /// <summary>Plugin dùng được trên nền tảng hiện tại (iOS device) và chưa bị tắt do thiếu Firebase.</summary>
        internal static bool IsAvailable
        {
            get
            {
#if EZG_IAP_FIREBASE_SK2_NATIVE
                return !s_Disabled;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// Gửi yêu cầu ghi giao dịch có <c>Transaction.id</c> = <paramref name="transactionId"/>. Plugin tra lịch sử
        /// StoreKit 2 tối đa <paramref name="maxWaitMs"/> ms (giao dịch vừa mua chưa hiện ngay).
        /// </summary>
        /// <returns>requestId &gt; 0 để đọc kết quả; 0 nếu không gửi được (không phải iOS device / plugin lỗi).</returns>
        internal static int Begin(string transactionId, int maxWaitMs)
        {
#if EZG_IAP_FIREBASE_SK2_NATIVE
            if (s_Disabled)
            {
                return 0;
            }

            int requestId;
            lock (s_Lock)
            {
                s_LastRequestId = s_LastRequestId >= int.MaxValue ? 1 : s_LastRequestId + 1;
                requestId = s_LastRequestId;
                s_InFlight.Add(requestId);
            }

            try
            {
                EzgIap_LogFirebaseStoreKit2Transaction(requestId, transactionId, Math.Max(0, maxWaitMs), s_Callback);
                return requestId;
            }
            catch (Exception e)
            {
                lock (s_Lock)
                {
                    s_InFlight.Remove(requestId);
                }

                s_Disabled = true;
                Debug.LogWarning("[IAP][FirebaseSK2] Không gọi được plugin native, tắt ghi Firebase: " + e.Message);
                return 0;
            }
#else
            return 0;
#endif
        }

        /// <summary>Lấy (và xoá) kết quả của <paramref name="requestId"/> nếu native đã trả lời.</summary>
        internal static bool TryTakeResult(int requestId, out int resultCode)
        {
            lock (s_Lock)
            {
                if (s_Results.TryGetValue(requestId, out resultCode))
                {
                    s_Results.Remove(requestId);
#if EZG_IAP_FIREBASE_SK2_NATIVE
                    if (resultCode == RESULT_FIREBASE_UNAVAILABLE)
                    {
                        s_Disabled = true;
                    }
#endif

                    return true;
                }
            }

            resultCode = RESULT_NOT_FOUND;
            return false;
        }

        /// <summary>Bỏ lượt đã quá hạn — callback về sau sẽ bị bỏ qua.</summary>
        internal static void Abandon(int requestId)
        {
            lock (s_Lock)
            {
                s_InFlight.Remove(requestId);
                s_Results.Remove(requestId);
            }
        }

        #endregion

        #region Native

#if EZG_IAP_FIREBASE_SK2_NATIVE
        private delegate void NativeResultCallback(int requestId, int resultCode);

        // Giữ tham chiếu tĩnh để delegate không bị GC khi native còn giữ con trỏ hàm.
        private static readonly NativeResultCallback s_Callback = OnNativeResult;

        [DllImport("__Internal")]
        private static extern void EzgIap_LogFirebaseStoreKit2Transaction(int requestId,
            [MarshalAs(UnmanagedType.LPStr)] string transactionId, int maxWaitMs, NativeResultCallback callback);

        [AOT.MonoPInvokeCallback(typeof(NativeResultCallback))]
        private static void OnNativeResult(int requestId, int resultCode)
        {
            // Exception không được lọt ngược vào native (crash) → chỉ ghi kết quả, không làm gì khác.
            try
            {
                lock (s_Lock)
                {
                    if (s_InFlight.Remove(requestId))
                    {
                        s_Results[requestId] = resultCode;
                    }
                }
            }
            catch
            {
                // ignored
            }
        }
#endif

        #endregion
    }

    /// <summary>Phía native thật của <see cref="StoreKit2ConfirmGate{TOrder}"/>.</summary>
    internal sealed class FirebaseStoreKit2Native : IStoreKit2FirebaseNative
    {
        public int Begin(string transactionId, int maxWaitMs)
        {
            return FirebaseStoreKit2Bridge.Begin(transactionId, maxWaitMs);
        }

        public bool TryTakeResult(int requestId, out int resultCode)
        {
            return FirebaseStoreKit2Bridge.TryTakeResult(requestId, out resultCode);
        }

        public void Abandon(int requestId)
        {
            FirebaseStoreKit2Bridge.Abandon(requestId);
        }
    }
}
