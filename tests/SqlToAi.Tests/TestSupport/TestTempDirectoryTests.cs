#nullable enable

using System.Text;

namespace SqlToAi.Tests.TestSupport;

// @covers SqlToAi.Tests.TestSupport.TestTempDirectory
// @covers SqlToAi.Tests.TestSupport.SolutionRootLocator
public sealed class TestTempDirectoryTests
{
    [Fact]
    public void SolutionRootLocator_Find_ReturnsExistingRootWithSolutionFile()
    {
        // Act
        var root = SolutionRootLocator.Find();

        // Assert
        Assert.NotNull(root);
        Assert.True(Directory.Exists(root));
        var slnxPath = Path.Combine(root, "SqlToAi.slnx");
        var slnPath = Path.Combine(root, "SqlToAi.sln");
        Assert.True(File.Exists(slnxPath) || File.Exists(slnPath));
    }

    [Fact]
    public void TestTempDirectory_RootTempDirectory_PointsToSolutionTempFolder()
    {
        // Act
        var rootTemp = TestTempDirectory.RootTempDirectory;
        var solutionRoot = SolutionRootLocator.Find();

        // Assert
        Assert.Equal(Path.Combine(solutionRoot, "temp"), rootTemp);
    }

    [Fact]
    public void TestTempDirectory_Create_CreatesIsolatedSubdirectoryInTemp()
    {
        // Act
        using var temp = TestTempDirectory.Create();

        // Assert
        Assert.True(Directory.Exists(temp.DirectoryPath));
        Assert.StartsWith(TestTempDirectory.RootTempDirectory, temp.DirectoryPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("sql-ai-test-", Path.GetFileName(temp.DirectoryPath), StringComparison.Ordinal);

        string implicitPath = temp;
        Assert.Equal(temp.DirectoryPath, implicitPath);
        Assert.Equal(temp.DirectoryPath, temp.ToString());
    }

    [Fact]
    public void TestTempDirectory_Create_SupportsCustomPrefix()
    {
        // Arrange
        const string prefix = "custom-unit-";

        // Act
        using var temp = TestTempDirectory.Create(prefix);

        // Assert
        Assert.StartsWith(prefix, Path.GetFileName(temp.DirectoryPath), StringComparison.Ordinal);
        Assert.True(Directory.Exists(temp.DirectoryPath));
    }

    [Fact]
    public void TestTempDirectory_CreateFile_CreatesFileWithContentAndIntermediateDirectories()
    {
        // Arrange
        using var temp = TestTempDirectory.Create();
        const string relativePath = "sub/folder/sample.txt";
        const string content = "Sample content for testing temp files\r\nLine 2";

        // Act
        var absolutePath = temp.CreateFile(relativePath, content);

        // Assert
        Assert.True(File.Exists(absolutePath));
        Assert.True(temp.FileExists(relativePath));
        Assert.Equal(temp.GetPath(relativePath), absolutePath);
        Assert.Equal(content, temp.ReadAllText(relativePath));
    }

    [Fact]
    public void TestTempDirectory_CreateBinaryFile_WritesRawBytesCorrectly()
    {
        // Arrange
        using var temp = TestTempDirectory.Create();
        const string relativePath = "bin/data.bin";
        byte[] expectedBytes = [0x00, 0x01, 0xFE, 0xFF, 0x42];

        // Act
        var absolutePath = temp.CreateBinaryFile(relativePath, expectedBytes);

        // Assert
        Assert.True(File.Exists(absolutePath));
        Assert.True(temp.FileExists(relativePath));
        var actualBytes = temp.ReadAllBytes(relativePath);
        Assert.Equal(expectedBytes, actualBytes);
    }

    [Fact]
    public void TestTempDirectory_CreateSubdirectory_CreatesNestedDirectory()
    {
        // Arrange
        using var temp = TestTempDirectory.Create();
        const string relativePath = "first/second/third";

        // Act
        var absolutePath = temp.CreateSubdirectory(relativePath);

        // Assert
        Assert.True(Directory.Exists(absolutePath));
        Assert.True(temp.DirectoryExists(relativePath));
        Assert.Equal(temp.GetPath(relativePath), absolutePath);
    }

    [Fact]
    public void TestTempDirectory_Dispose_DeletesDirectoryAndOwnerMarker()
    {
        // Arrange
        string directoryPath;
        string markerPath;
        string filePath;

        using (var temp = TestTempDirectory.Create("dispose-check-"))
        {
            directoryPath = temp.DirectoryPath;
            filePath = temp.CreateFile("nested/test.txt", "will be deleted");
            markerPath = Path.Combine(
                TestTempDirectory.RootTempDirectory,
                ".sql-ai-test-owner-" + Path.GetFileName(directoryPath));

            Assert.True(Directory.Exists(directoryPath));
            Assert.True(File.Exists(filePath));
            Assert.True(File.Exists(markerPath));
        }

        // Assert
        Assert.False(Directory.Exists(directoryPath));
        Assert.False(File.Exists(filePath));
        Assert.False(File.Exists(markerPath));
    }

    [Fact]
    public void TestTempDirectory_Dispose_HandlesReadOnlyFiles()
    {
        // Arrange
        string directoryPath;
        string readOnlyFilePath;

        using (var temp = TestTempDirectory.Create("readonly-check-"))
        {
            directoryPath = temp.DirectoryPath;
            readOnlyFilePath = temp.CreateFile("locked.txt", "locked read only content");
            File.SetAttributes(readOnlyFilePath, FileAttributes.ReadOnly);

            Assert.True(File.Exists(readOnlyFilePath));
            Assert.True(File.GetAttributes(readOnlyFilePath).HasFlag(FileAttributes.ReadOnly));
        }

        // Assert
        Assert.False(Directory.Exists(directoryPath));
        Assert.False(File.Exists(readOnlyFilePath));
    }

    [Fact]
    public void TestTempDirectory_MultipleInstances_AreIsolatedAndIndependent()
    {
        // Arrange & Act
        using var tempA = TestTempDirectory.Create("isolate-a-");
        using var tempB = TestTempDirectory.Create("isolate-b-");

        tempA.CreateFile("shared-name.txt", "A's content");
        tempB.CreateFile("shared-name.txt", "B's content");

        // Assert
        Assert.NotEqual(tempA.DirectoryPath, tempB.DirectoryPath);
        Assert.Equal("A's content", tempA.ReadAllText("shared-name.txt"));
        Assert.Equal("B's content", tempB.ReadAllText("shared-name.txt"));

        var pathA = tempA.DirectoryPath;
        tempA.Dispose();

        Assert.False(Directory.Exists(pathA));
        Assert.True(Directory.Exists(tempB.DirectoryPath));
        Assert.Equal("B's content", tempB.ReadAllText("shared-name.txt"));
    }

    [Fact]
    public void TestTempDirectory_GetPath_ReturnsNormalizedAbsolutePath()
    {
        // Arrange
        using var temp = TestTempDirectory.Create();

        // Act
        var path = temp.GetPath("nested/../direct.txt");

        // Assert
        var expected = Path.Combine(temp.DirectoryPath, "direct.txt");
        Assert.Equal(expected, path);
    }
}
