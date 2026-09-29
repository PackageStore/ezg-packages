using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ezg.AutoTest.Editor
{
    /// <summary>
    ///     Sao lưu / khôi phục PlayerPrefs của dữ liệu người chơi quanh các suite có sửa data (economy, button
    ///     sweep…) để test không làm hỏng save của dev. Bản sao lưu ghi ra đĩa → nếu Editor crash giữa chừng, lần mở
    ///     sau tự khôi phục.
    /// </summary>
    [InitializeOnLoad]
    public static class PlayerDataSandbox
    {
        const string BACKUP_FILE = "Library/EZGAutoTest/prefs-backup.json";
        const string STRING_SENTINEL = "\u0001__ezg_autotest_missing__\u0001";
        const int INT_SENTINEL = int.MinValue + 7;

        [Serializable]
        class Entry
        {
            public string key;
            public bool existed;

            /// <summary>string / int / float.</summary>
            public string kind;

            public string s;
            public int i;
            public float f;
        }

        [Serializable]
        class Backup
        {
            public string createdAt;
            public List<Entry> entries = new();
        }

        static PlayerDataSandbox()
        {
            // Chờ runner khởi động xong rồi mới quyết định có cần khôi phục sót không.
            EditorApplication.delayCall += RestoreOrphanedBackup;
        }

        public static bool HasBackup => File.Exists(BackupPath());

        static string BackupPath()
        {
            return Path.Combine(Path.GetDirectoryName(Application.dataPath)!, BACKUP_FILE);
        }

        /// <summary>Chụp giá trị hiện tại của các key (key chưa tồn tại cũng ghi lại để xoá khi khôi phục).</summary>
        public static int TakeBackup(IEnumerable<string> keys)
        {
            if (HasBackup) return -1; // đã có bản sao lưu đang chờ khôi phục → không ghi đè bản gốc
            var backup = new Backup { createdAt = DateTime.Now.ToString("o") };
            var seen = new HashSet<string>();
            foreach (var key in keys)
            {
                if (string.IsNullOrEmpty(key) || !seen.Add(key)) continue;
                var e = new Entry { key = key, existed = PlayerPrefs.HasKey(key) };
                if (e.existed) ReadTyped(e);
                backup.entries.Add(e);
            }

            var path = BackupPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonUtility.ToJson(backup, true));
            AutoTestLog.Info($"Sandbox: đã sao lưu {backup.entries.Count} key dữ liệu người chơi.");
            return backup.entries.Count;
        }

        /// <summary>Ghi trả lại giá trị đã sao lưu và xoá file sao lưu. An toàn khi gọi nhiều lần.</summary>
        public static bool Restore()
        {
            var path = BackupPath();
            if (!File.Exists(path)) return false;
            Backup backup;
            try
            {
                backup = JsonUtility.FromJson<Backup>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Debug.LogError(AutoTestLog.PREFIX + "File sao lưu dữ liệu hỏng, không khôi phục được: " + e.Message);
                return false;
            }

            foreach (var e in backup.entries)
            {
                if (!e.existed)
                {
                    PlayerPrefs.DeleteKey(e.key);
                    continue;
                }

                switch (e.kind)
                {
                    case "int": PlayerPrefs.SetInt(e.key, e.i); break;
                    case "float": PlayerPrefs.SetFloat(e.key, e.f); break;
                    default: PlayerPrefs.SetString(e.key, e.s ?? ""); break;
                }
            }

            PlayerPrefs.Save();
            File.Delete(path);
            AutoTestLog.Info($"Sandbox: đã khôi phục {backup.entries.Count} key dữ liệu người chơi.");
            return true;
        }

        static void ReadTyped(Entry e)
        {
            // PlayerPrefs không cho biết kiểu → thử string, rồi int, rồi float (GetX trả default nếu sai kiểu).
            var s = PlayerPrefs.GetString(e.key, STRING_SENTINEL);
            if (s != STRING_SENTINEL)
            {
                e.kind = "string";
                e.s = s;
                return;
            }

            var i = PlayerPrefs.GetInt(e.key, INT_SENTINEL);
            if (i != INT_SENTINEL)
            {
                e.kind = "int";
                e.i = i;
                return;
            }

            e.kind = "float";
            e.f = PlayerPrefs.GetFloat(e.key, 0f);
        }

        static void RestoreOrphanedBackup()
        {
            if (!HasBackup || AutoTestRunner.IsRunning || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (Restore())
                Debug.LogWarning(AutoTestLog.PREFIX +
                                 "Đã khôi phục dữ liệu người chơi từ lần chạy auto test trước bị gián đoạn.");
        }
    }
}
