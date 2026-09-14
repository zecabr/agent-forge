using AgentForge.Capabilities.FileTools;
using Xunit;

namespace AgentForge.Capabilities.FileTools.Tests;

public class FilesRootTests : IDisposable
{
    private readonly string _tempDir;

    public FilesRootTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "files-root-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, "src"));
        File.WriteAllText(Path.Combine(_tempDir, "README.md"), "# root");
        File.WriteAllText(Path.Combine(_tempDir, "src", "foo.cs"), "// foo");
        File.WriteAllText(Path.Combine(_tempDir, "src", "bar.cs"), "// bar");
        File.WriteAllText(Path.Combine(_tempDir, "app.config"), "config");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Ctor_Rejects_Missing_Directory()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            new FilesRoot(Path.Combine(_tempDir, "does-not-exist")));
    }

    [Fact]
    public void Ctor_Rejects_Null_Or_Whitespace()
    {
        Assert.Throws<ArgumentException>(() => new FilesRoot(""));
        Assert.Throws<ArgumentException>(() => new FilesRoot("   "));
    }

    [Fact]
    public void ResolveSafely_Accepts_Path_Inside_Root()
    {
        var root = new FilesRoot(_tempDir);
        var resolved = root.ResolveSafely("src/foo.cs");
        Assert.StartsWith(root.CanonicalPath, resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveSafely_Rejects_ParentTraversal()
    {
        var root = new FilesRoot(_tempDir);
        Assert.Throws<ArgumentException>(() => root.ResolveSafely("../escape.txt"));
        Assert.Throws<ArgumentException>(() => root.ResolveSafely("src/../../escape.txt"));
    }

    [Fact]
    public void ResolveSafely_Rejects_AbsolutePath_Outside_Root()
    {
        var root = new FilesRoot(_tempDir);
        var outsideAbsolute = OperatingSystem.IsWindows() ? "C:\\Windows\\notepad.exe" : "/etc/passwd";
        Assert.Throws<ArgumentException>(() => root.ResolveSafely(outsideAbsolute));
    }

    [Fact]
    public void EnumerateFiles_All_Returns_Everything_With_Relative_Paths()
    {
        var root = new FilesRoot(_tempDir);
        var files = root.EnumerateFiles().OrderBy(p => p, StringComparer.Ordinal).ToList();

        Assert.Contains("README.md", files);
        Assert.Contains("app.config", files);
        Assert.Contains("src/foo.cs", files);
        Assert.Contains("src/bar.cs", files);
        Assert.Equal(4, files.Count);
    }

    [Fact]
    public void EnumerateFiles_With_Pattern_Filters()
    {
        var root = new FilesRoot(_tempDir);
        var csFiles = root.EnumerateFiles("*.cs").OrderBy(p => p, StringComparer.Ordinal).ToList();

        Assert.Equal(2, csFiles.Count);
        Assert.Contains("src/foo.cs", csFiles);
        Assert.Contains("src/bar.cs", csFiles);
    }

    [Fact]
    public void LooksBinary_Detects_Null_Bytes()
    {
        var withNull = new byte[] { 0x48, 0x65, 0x00, 0x6c, 0x6f };
        var textOnly = new byte[] { 0x48, 0x65, 0x6c, 0x6c, 0x6f };

        Assert.True(FilesRoot.LooksBinary(withNull));
        Assert.False(FilesRoot.LooksBinary(textOnly));
    }

    [Fact]
    public void LooksBinary_Handles_Empty_And_Short_Buffers()
    {
        Assert.False(FilesRoot.LooksBinary(Array.Empty<byte>()));
        Assert.False(FilesRoot.LooksBinary(new byte[] { 0x41 }));
    }

    [Fact]
    public void EnumerateFiles_Skips_Ignored_Directories()
    {
        // adiciona subdiretórios que deveriam ser ignorados
        Directory.CreateDirectory(Path.Combine(_tempDir, ".git", "objects"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "bin", "Debug", "net9.0"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "obj"));
        Directory.CreateDirectory(Path.Combine(_tempDir, "node_modules", "foo"));
        File.WriteAllText(Path.Combine(_tempDir, ".git", "HEAD"), "ref: main");
        File.WriteAllText(Path.Combine(_tempDir, ".git", "objects", "abc.pack"), "pack");
        File.WriteAllText(Path.Combine(_tempDir, "bin", "Debug", "net9.0", "app.dll"), "binary");
        File.WriteAllText(Path.Combine(_tempDir, "obj", "cache.txt"), "cache");
        File.WriteAllText(Path.Combine(_tempDir, "node_modules", "foo", "package.json"), "{}");

        var root = new FilesRoot(_tempDir);
        var files = root.EnumerateFiles().ToList();

        Assert.DoesNotContain(files, f => f.StartsWith(".git/", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.StartsWith("bin/", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.StartsWith("obj/", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.StartsWith("node_modules/", StringComparison.Ordinal));

        // arquivos normais fora dessas pastas continuam listados
        Assert.Contains("README.md", files);
        Assert.Contains("src/foo.cs", files);
    }

    [Fact]
    public void EnumerateFiles_Ignored_Match_Is_Case_Insensitive()
    {
        // Windows: pastas .Git / BIN existem em algum canto — precisa pegar mesmo assim
        Directory.CreateDirectory(Path.Combine(_tempDir, "BIN"));
        File.WriteAllText(Path.Combine(_tempDir, "BIN", "release.dll"), "binary");

        var root = new FilesRoot(_tempDir);
        var files = root.EnumerateFiles().ToList();

        Assert.DoesNotContain(files, f => f.StartsWith("BIN/", StringComparison.OrdinalIgnoreCase));
    }
}
