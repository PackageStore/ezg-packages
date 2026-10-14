using Ezg.BugLogger.Editor;
using NUnit.Framework;

namespace Ezg.BugLogger.Tests
{
    /// <summary>Baker: commit + branch rơi về biến CI khi git không cho được.</summary>
    public class BugLoggerBuildInfoBakerTests
    {
        private const string SHA = "0123456789abcdef0123456789abcdef01234567";
        private const string CI_SHA = "fedcba9876543210fedcba9876543210fedcba98";


        [Test]
        public void CommitSha_PrefersGitThenCi()
        {
            Assert.AreEqual(SHA, BugLoggerBuildInfoBaker.PickCommitSha(SHA.ToUpperInvariant(), CI_SHA));
            Assert.AreEqual(CI_SHA, BugLoggerBuildInfoBaker.PickCommitSha(null, CI_SHA));
            Assert.AreEqual(CI_SHA, BugLoggerBuildInfoBaker.PickCommitSha("fatal: not a git repository", CI_SHA));
            Assert.AreEqual(BugLoggerBuildInfo.UNKNOWN, BugLoggerBuildInfoBaker.PickCommitSha(null, "not-a-sha"));
        }

        [Test]
        public void Branch_DetachedHeadUsesCiRefName()
        {
            Assert.AreEqual("develop_AnhNT", BugLoggerBuildInfoBaker.PickBranch("develop_AnhNT", "main"));
            Assert.AreEqual("Android/AutoBuild", BugLoggerBuildInfoBaker.PickBranch("HEAD", "Android/AutoBuild"));
            Assert.AreEqual("Android/AutoBuild", BugLoggerBuildInfoBaker.PickBranch(null, " Android/AutoBuild "));
            Assert.AreEqual(BugLoggerBuildInfo.UNKNOWN, BugLoggerBuildInfoBaker.PickBranch("HEAD", null));
        }
    }
}
