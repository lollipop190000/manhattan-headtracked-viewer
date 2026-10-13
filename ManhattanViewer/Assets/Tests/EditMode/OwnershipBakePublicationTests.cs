using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Manhattan.Editor;
using NUnit.Framework;

public sealed class OwnershipBakePublicationTests
{
    private string directory;
    private const string Json = "{\"sourceVersion\":\"publication-validation-한글\",\"groups\":[]}";

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "ManhattanOwnershipPublication-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private string DataPath => Path.Combine(directory, "CityOwnership.json");
    private string HashPath => Path.Combine(directory, "CityOwnershipHash.txt");

    private static string Digest(byte[] bytes)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    private void Publish()
    {
        var method = typeof(SkylineOwnershipBake).GetMethod("PublishOutput", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "Exercise the generator's real publication implementation.");
        method.Invoke(null, new object[] { directory, Json, Digest(Encoding.UTF8.GetBytes(Json)), false });
    }

    private void AssertNoStagingFiles()
    {
        Assert.That(Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories), Is.Empty,
            "Successful or failed publication must clean both staged files.");
    }

    [TestCase("Assets")]
    [TestCase("Assets/Generated/Validation")]
    public void ValidationRejectsProjectAssetOutputBeforeLoading(string output)
    {
        var task = SkylineOwnershipBake.ValidateBuiltGroupAsync("unused-catalog.bin", "Skyline_1_6", output);
        Assert.That(task.IsCompleted, Is.True, "Invalid output must fail before asynchronous asset loading begins.");
        Assert.That(task.IsFaulted, Is.True);
        Assert.That(task.Exception.InnerException, Is.InstanceOf<ArgumentException>());
        Assert.That(task.Exception.InnerException.Message, Does.Contain("outside Assets"));
    }

    [Test]
    [Platform("Win")]
    public void ValidationRejectsAssetsRootWithDifferentWindowsCasing()
    {
        ValidationRejectsProjectAssetOutputBeforeLoading(Path.GetFullPath("Assets").ToUpperInvariant());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void CompletePairPublishesExactUtf8AndDigest(bool previousPair)
    {
        if (previousPair)
        {
            File.WriteAllBytes(DataPath, Encoding.UTF8.GetBytes("old sidecar\r\n"));
            File.WriteAllBytes(HashPath, Encoding.UTF8.GetBytes("old hash\r\n"));
        }

        Publish();

        var data = File.ReadAllBytes(DataPath);
        var hash = File.ReadAllBytes(HashPath);
        Assert.That(data, Is.EqualTo(Encoding.UTF8.GetBytes(Json)), "The published JSON must use UTF-8 without an added BOM.");
        Assert.That(hash, Is.EqualTo(Encoding.UTF8.GetBytes(Digest(data))), "The companion digest must match the exact published bytes.");
        Assert.That(Directory.GetFiles(directory).Length, Is.EqualTo(2));
        AssertNoStagingFiles();
    }

    [Test]
    [Platform("Win")]
    public void LockedSecondFileRestoresExactPreviousDataAndLeavesHashUnchanged()
    {
        // Preserve deliberately different byte encoding and line endings. A text
        // rewrite during rollback would lose these bytes and fail this assertion.
        var oldData = new byte[] { 0xef, 0xbb, 0xbf, 0x7b, 0x7d, 0x0d, 0x0a };
        var oldHash = Encoding.UTF8.GetBytes("original hash bytes\r\n");
        File.WriteAllBytes(DataPath, oldData);
        File.WriteAllBytes(HashPath, oldHash);

        // FileShare.Read allows the publication snapshot to read the hash while
        // denying the delete/replace access required for its second publication.
        using (var heldHash = new FileStream(HashPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = Assert.Throws<TargetInvocationException>(() => Publish());
            Assert.That(failure.InnerException, Is.InstanceOf<IOException>(), "The real locked-file publication must fail.");
            Assert.That(File.ReadAllBytes(DataPath), Is.EqualTo(oldData), "Rollback must restore every original data byte.");
            Assert.That(File.ReadAllBytes(HashPath), Is.EqualTo(oldHash), "The locked original hash must remain unchanged.");
            AssertNoStagingFiles();
        }
        Assert.That(Directory.GetFiles(directory).Length, Is.EqualTo(2));
    }

    [Test]
    [Platform("Win")]
    public void LockedSecondFileRemovesNewDataWhenNoPreviousDataExisted()
    {
        var oldHash = Encoding.UTF8.GetBytes("retained original hash\r\n");
        File.WriteAllBytes(HashPath, oldHash);
        Assert.That(File.Exists(DataPath), Is.False);

        using (var heldHash = new FileStream(HashPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = Assert.Throws<TargetInvocationException>(() => Publish());
            Assert.That(failure.InnerException, Is.InstanceOf<IOException>());
            Assert.That(File.Exists(DataPath), Is.False, "Failed pair publication must remove its newly created first file.");
            Assert.That(File.ReadAllBytes(HashPath), Is.EqualTo(oldHash));
            AssertNoStagingFiles();
        }
        Assert.That(Directory.GetFiles(directory), Is.EqualTo(new[] { HashPath }));
    }
}
