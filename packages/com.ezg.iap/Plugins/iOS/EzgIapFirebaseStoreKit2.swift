// com.ezg.iap — ghi in_app_purchase lên Firebase Analytics cho giao dịch StoreKit 2.
//
// Unity IAP 5 mua bằng StoreKit 2 trên iOS ≥ 15, mà Firebase chỉ TỰ thu in_app_purchase qua observer StoreKit 1 → doanh
// thu IAP iOS hụt trong báo cáo Firebase/GA4. API native `Analytics.logTransaction(_:)` có sẵn trong FirebaseAnalytics
// iOS ≥ 10.17.0 (Firebase Unity SDK 13.3.0 kèm pod 12.2.0 đã có) → không cần nâng Firebase Unity SDK lên 13.12.0 chỉ
// để có `LogAppleTransactionAsync` (hàm đó cũng chỉ bọc đúng API này).
//
// C# (FirebaseStoreKit2Bridge.cs) gọi hàm này TRƯỚC ConfirmPurchase: confirm = finish, consumable đã finish biến khỏi
// lịch sử StoreKit 2 nên không tra lại được. Giao dịch vừa mua cũng KHÔNG hiện ngay trong Transaction.unfinished/all
// (đo bằng StoreKit Test: ~0.9 s sau khi purchase() trả về) → phải tra lại nhiều lần trong `maxWaitMs`.
//
// - App không link FirebaseAnalytics → file vẫn biên dịch, trả FirebaseUnavailable.
// - App dùng FirebaseAnalytics < 10.17.0 (chưa có logTransaction) → thêm EZG_IAP_DISABLE_FIREBASE_SK2 vào
//   "Swift Active Compilation Conditions" của target UnityFramework bằng script PostProcessBuild (Unity sinh lại project
//   Xcode mỗi lần build). Không được xoá file này: IL2CPP link thẳng tới symbol bên dưới.

import Foundation
import StoreKit
#if canImport(FirebaseAnalytics) && !EZG_IAP_DISABLE_FIREBASE_SK2
import FirebaseAnalytics
#if canImport(FirebaseCore)
import FirebaseCore
#endif
#endif

/// Mã kết quả trả về C# — phải khớp hằng số trong FirebaseStoreKit2Bridge.cs.
private enum EzgIapSK2Result: Int32 {
    case notFound = 0
    case logged = 1
    case osUnsupported = -1
    case firebaseUnavailable = -2
    case invalidTransactionId = -3
    case firebaseNotConfigured = -4
}

/// Callback sang C#: (requestId, mã kết quả). Luôn gọi trên main thread, đúng 1 lần cho mỗi lượt.
public typealias EzgIapSK2Callback = @convention(c) (Int32, Int32) -> Void

/// Ghi giao dịch StoreKit 2 có `Transaction.id` = `transactionId` (chuỗi số thập phân — `order.Info.TransactionID`).
/// `maxWaitMs`: thời gian tối đa chờ giao dịch hiện trong lịch sử StoreKit 2 trước khi trả NotFound.
@_cdecl("EzgIap_LogFirebaseStoreKit2Transaction")
public func EzgIap_LogFirebaseStoreKit2Transaction(_ requestId: Int32,
                                                   _ transactionId: UnsafePointer<CChar>?,
                                                   _ maxWaitMs: Int32,
                                                   _ callback: EzgIapSK2Callback?) {
    // Chép chuỗi NGAY: bộ nhớ IL2CPP marshal chỉ sống trong lượt gọi này.
    let idString = transactionId.map { String(cString: $0) } ?? ""
    ezgIapLogTransaction(idString, max(0, maxWaitMs)) { result in
        DispatchQueue.main.async {
            callback?(requestId, result.rawValue)
        }
    }
}

private func ezgIapLogTransaction(_ idString: String,
                                  _ maxWaitMs: Int32,
                                  _ completion: @escaping @Sendable (EzgIapSK2Result) -> Void) {
#if canImport(FirebaseAnalytics) && !EZG_IAP_DISABLE_FIREBASE_SK2
    guard let transactionId = UInt64(idString) else {
        completion(.invalidTransactionId)
        return
    }

    guard #available(iOS 15.0, *) else {
        // StoreKit 1 — Firebase đã tự thu.
        completion(.osUnsupported)
        return
    }

#if canImport(FirebaseCore)
    // Firebase chưa configure (vd đơn treo được giao lại ngay lúc mở app) → C# thử lại sau.
    guard FirebaseApp.app() != nil else {
        completion(.firebaseNotConfigured)
        return
    }
#endif

    Task.detached(priority: .utility) {
        if let transaction = await ezgIapFindVerifiedTransaction(transactionId, maxWaitMs) {
            Analytics.logTransaction(transaction)
            completion(.logged)
        } else {
            completion(.notFound)
        }
    }
#else
    completion(.firebaseUnavailable)
#endif
}

#if canImport(FirebaseAnalytics) && !EZG_IAP_DISABLE_FIREBASE_SK2
@available(iOS 15.0, *)
private func ezgIapFindVerifiedTransaction(_ transactionId: UInt64, _ maxWaitMs: Int32) async -> Transaction? {
    let start = DispatchTime.now().uptimeNanoseconds
    let budget = UInt64(maxWaitMs) * 1_000_000
    var delayNs: UInt64 = 50_000_000

    // Đường chính: đơn chưa finish (C# giữ ConfirmPurchase tới khi có kết quả). Tra lại với nhịp 50 → 100 → 200 → 250 ms
    // vì giao dịch vừa mua chưa hiện ngay trong lịch sử.
    while true {
        if let transaction = await ezgIapFirstVerified(in: Transaction.unfinished, transactionId) {
            return transaction
        }

        let elapsed = DispatchTime.now().uptimeNanoseconds &- start
        if elapsed >= budget {
            break
        }

        try? await Task.sleep(nanoseconds: min(delayNs, budget - elapsed))
        delayNs = min(delayNs * 2, 250_000_000)
    }

    // Dự phòng: non-consumable / subscription vẫn nằm trong lịch sử sau khi finish.
    return await ezgIapFirstVerified(in: Transaction.all, transactionId)
}

@available(iOS 15.0, *)
private func ezgIapFirstVerified(in sequence: Transaction.Transactions, _ transactionId: UInt64) async -> Transaction? {
    // Iterator thủ công thay cho `for await`: bản build bằng Xcode 16+ có thể crash dyld (`next(isolation:)`) trên
    // iOS 16 — Firebase C++ SDK cũng tránh đúng lỗi này.
    var iterator = sequence.makeAsyncIterator()
    while let result = await iterator.next() {
        // Chỉ ghi giao dịch đã được StoreKit verify (giống LogAppleTransactionAsync của Firebase).
        if case .verified(let transaction) = result, transaction.id == transactionId {
            return transaction
        }
    }

    return nil
}
#endif
