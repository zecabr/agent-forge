using AgentForge.Capabilities.DocsReader;
using Xunit;

namespace AgentForge.Capabilities.DocsReader.Tests;

public class DocsRootTests : IDisposable
{
    private readonly string _tempDir;

    public DocsRootTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "docs-root-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "adr"));
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "# root readme");
        File.WriteAllText(Path.Combine(_tempDir, "adr", "001.md"), "# adr 1");
        File.WriteAllText(Path.Combine(_tempDir, "adr", "002.md"), "# adr 2");
        File.WriteAllText(Path.Combine(_tempDir, "notes.txt"), "not markdown");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void Ctor_Rejects_Missing_Directory()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            new DocsRoot(Path.Combine(_tempDir, "does-not-exist")));
    }

    [Fact]
    public void Ctor_Rejects_Null_Or_Whitespace()
    {
        Assert.Throws<ArgumentException>(() => new DocsRoot(""));
        Assert.Throws<ArgumentException>(() => new DocsRoot("   "));
    }

    [Fact]
    public void CanonicalPath_Is_Absolute_And_Trim_Slash()
    {
        var root = new DocsRoot(_tempDir);
        Assert.Equal(Path.GetFullPath(_tempDir).TrimEnd(Path.DirectorySeparatorChar), root.CanonicalPath);
    }

    [Fact]
    public void ResolveSafely_Accepts_Path_Inside_Root()
    {
        var root = new DocsRoot(_tempDir);
        var resolved = root.ResolveSafely("adr/001.md");
        Assert.StartsWith(root.CanonicalPath, resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveSafely_Rejects_ParentTraversal()
    {
        var root = new DocsRoot(_tempDir);
        Assert.Throws<ArgumentException>(() => root.ResolveSafely("../escape.md"));
        Assert.Throws<ArgumentException>(() => root.ResolveSafely("adr/../../escape.md"));
    }

    [Fact]
    public void ResolveSafely_Rejects_AbsolutePath_Outside_Root()
    {
        var root = new DocsRoot(_tempDir);
        // Path.Combine with an absolute second arg REPLACES the first — Path.GetFullPath
        // then keeps the absolute, which resolves outside root. Must be rejected.
        var outsideAbsolute = OperatingSystem.IsWindows() ? "C:\\Windows\\notepad.exe" : "/etc/passwd";
        Assert.Throws<ArgumentException>(() => root.ResolveSafely(outsideAbsolute));
    }

    [Fact]
    public void EnumerateMarkdown_Returns_Only_Md_Files_With_Relative_Paths()
    {
        var root = new DocsRoot(_tempDir);
        var files = root.EnumerateMarkdown().OrderBy(p => p, StringComparer.Ordinal).ToList();

        Assert.Contains("README.md", files);
        Assert.Contains("adr/001.md", files);
        Assert.Contains("adr/002.md", files);
        Assert.DoesNotContain("notes.txt", files);
        Assert.Equal(3, files.Count);
    }
}
