using FolderSyncr.Services;

namespace FolderSyncr.Tests;

[TestClass]
public sealed class SampleDataGeneratorTests
{
    [TestMethod]
    public void CreateBuildsDisposableFolderPairWithExpectedCases()
    {
        var sample = new SampleDataGenerator().Create();
        try
        {
            Assert.IsTrue(Directory.Exists(sample.LeftPath));
            Assert.IsTrue(Directory.Exists(sample.RightPath));
            Assert.IsTrue(File.Exists(Path.Combine(sample.LeftPath, "equal.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(sample.RightPath, "equal.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(sample.LeftPath, "left-only.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(sample.RightPath, "right-only.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(sample.LeftPath, "conflict.txt")));
            Assert.IsTrue(File.Exists(Path.Combine(sample.RightPath, "conflict.txt")));
        }
        finally
        {
            if (Directory.Exists(sample.RootPath))
            {
                Directory.Delete(sample.RootPath, recursive: true);
            }
        }
    }
}
