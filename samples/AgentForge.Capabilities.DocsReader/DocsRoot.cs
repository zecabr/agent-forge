namespace AgentForge.Capabilities.DocsReader;

/// <summary>
/// Root path das docs Markdown. Encapsula resolução segura de caminhos relativos
/// — qualquer path resolvido pra fora do root é rejeitado com <see cref="ArgumentException"/>.
/// </summary>
internal sealed class DocsRoot
{
    /// <summary>Root canônico (absoluto, resolvido, sem trailing slash).</summary>
    public string CanonicalPath { get; }

    public DocsRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var absolute = Path.GetFullPath(rootPath);
        if (!Directory.Exists(absolute))
        {
            throw new DirectoryNotFoundException($"Docs root not found: {absolute}");
        }

        CanonicalPath = absolute.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Resolve um path relativo em absoluto DENTRO do root. Rejeita path traversal
    /// (<c>..</c>, absolute paths, links simbólicos que escapem) com <see cref="ArgumentException"/>.
    /// </summary>
    public string ResolveSafely(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        // Combine + GetFullPath resolve '..' e normaliza. Se sair do root, rejeita.
        var candidate = Path.GetFullPath(Path.Combine(CanonicalPath, relativePath));

        // Precisa ser CanonicalPath ou algo abaixo dele.
        // Comparação por prefixo string, com separador garantido, evita "roota" bater "root".
        var expectedPrefix = CanonicalPath + Path.DirectorySeparatorChar;
        if (!candidate.Equals(CanonicalPath, StringComparison.Ordinal)
            && !candidate.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Path '{relativePath}' resolves outside docs root.",
                nameof(relativePath));
        }

        return candidate;
    }

    /// <summary>Enumera todos os .md sob o root, retornando paths RELATIVOS ao root com forward slashes.</summary>
    public IEnumerable<string> EnumerateMarkdown()
    {
        foreach (var abs in Directory.EnumerateFiles(CanonicalPath, "*.md", SearchOption.AllDirectories))
        {
            yield return ToRelative(abs);
        }
    }

    /// <summary>Converte um path absoluto (dentro do root) pra relativo com <c>/</c>.</summary>
    public string ToRelative(string absolutePath)
    {
        var rel = Path.GetRelativePath(CanonicalPath, absolutePath);
        return rel.Replace(Path.DirectorySeparatorChar, '/');
    }
}
