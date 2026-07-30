#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;

namespace Ezg.VoodooSdk.Editor
{
    /// <summary>
    /// Tiện ích sửa manifest XML tại chỗ mà không làm bẩn phần còn lại của file.
    ///
    /// Hai chi tiết dễ sai nếu dùng thẳng <c>File.ReadAllText</c>/<c>WriteAllText</c>:
    ///
    /// 1. <b>BOM bị mất.</b> Manifest do Unity/Voodoo sinh có UTF-8 BOM;
    ///    <c>File.WriteAllText</c> mặc định ghi không BOM, khiến diff nhiễu ở dòng đầu.
    /// 2. <b>Thụt lề bị phá.</b> Chèn tại vị trí của <c>"&lt;application"</c> sẽ đẩy phần
    ///    whitespace đầu dòng sang thẻ mới và thẻ cũ mất thụt lề. Phải chèn ở <b>đầu dòng</b>
    ///    và mượn đúng thụt lề của dòng đó.
    /// </summary>
    public static class VoodooSdkXml
    {
        #region Public

        /// <summary>File có UTF-8 BOM ở đầu hay không.</summary>
        public static bool HasUtf8Bom(string path)
        {
            using FileStream stream = File.OpenRead(path);
            Span<byte> head = stackalloc byte[3];
            return stream.Read(head) == 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF;
        }

        /// <summary>Ghi file, giữ nguyên trạng thái BOM ban đầu.</summary>
        public static void Write(string path, string content, bool withBom)
        {
            File.WriteAllText(path, content, new UTF8Encoding(withBom));
        }

        /// <summary>
        /// Chèn <paramref name="xml"/> vào ngay trước dòng chứa <paramref name="marker"/>,
        /// tự áp dụng đúng thụt lề của dòng đó cho mọi dòng được chèn.
        /// </summary>
        /// <param name="last">true = tìm lần xuất hiện cuối (vd thẻ đóng).</param>
        public static string InsertBeforeLineOf(string content, string marker, string xml, bool last = false)
        {
            int markerIndex = last
                ? content.LastIndexOf(marker, StringComparison.Ordinal)
                : content.IndexOf(marker, StringComparison.Ordinal);
            if (markerIndex < 0)
                return content;

            int lineStart = content.LastIndexOf('\n', markerIndex) + 1;
            string indent = content.Substring(lineStart, markerIndex - lineStart);

            return content.Insert(lineStart, Reindent(xml, indent));
        }

        #endregion

        #region Private

        /// <summary>Bỏ thụt lề sẵn có trong khối rồi áp lại theo <paramref name="indent"/>.</summary>
        private static string Reindent(string xml, string indent)
        {
            string[] lines = xml.TrimEnd('\n').Split('\n');
            var builder = new StringBuilder();

            // Dòng đầu quyết định mức gốc; các dòng sau giữ thụt lề tương đối so với nó.
            int baseIndent = CountLeadingSpaces(lines[0]);

            foreach (string line in lines)
            {
                if (line.Trim().Length == 0)
                {
                    builder.Append('\n');
                    continue;
                }

                int extra = Math.Max(0, CountLeadingSpaces(line) - baseIndent);
                builder.Append(indent).Append(' ', extra).Append(line.TrimStart()).Append('\n');
            }

            return builder.ToString();
        }

        private static int CountLeadingSpaces(string line)
        {
            int count = 0;
            while (count < line.Length && line[count] == ' ')
                count++;
            return count;
        }

        #endregion
    }
}
#endif
