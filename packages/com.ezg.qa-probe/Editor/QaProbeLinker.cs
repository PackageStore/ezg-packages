using System.IO;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace Ezg.QaProbe.Editor
{
    /// <summary>
    ///     Giữ nguyên assembly Ezg.QaProbe khi Managed Stripping cao (endpoint đi qua reflection). link.xml nằm trong package
    ///     không được UnityLinker tự đọc nên đưa vào qua IUnityLinkerProcessor.
    /// </summary>
    internal sealed class QaProbeLinker : IUnityLinkerProcessor
    {
        private const string LINK_XML = "<linker>\n    <assembly fullname=\"Ezg.QaProbe\" preserve=\"all\"/>\n</linker>\n";

        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            var dir = Path.Combine(Path.GetFullPath("Library"), "EzgQaProbe");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "link.xml");
            File.WriteAllText(path, LINK_XML);
            return path;
        }
    }
}
