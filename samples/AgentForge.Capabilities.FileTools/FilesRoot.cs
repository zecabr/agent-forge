namespace AgentForge.Capabilities.FileTools;

/// <summary>
/// Root path do filesystem exposto. Encapsula resolução segura de caminhos —
/// qualquer path que resolve pra fora do root é rejeitado.
/// </summary>
internal sealed class FilesRoot
{
    /// <summary>Bytes máximos lidos por arquivo. Acima disso, tool falha com mensagem clara.</summary>
    public const int MaxBytesPerFile = 200 * 1024;

    /// <summary>Root canônico (absoluto, sem trailing slash).</summary>
    public string CanonicalPath { get; }

    public FilesRoot(string rootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var absolute = Path.GetFullPath(rootPath);
        if (!Directory.Exists(absolute))
        {
            throw new DirectoryNotFoundException($"Files root not found: {absolute}");
        }

        CanonicalPath = absolute.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Resolve um path relativo em absoluto DENTRO do root. Rejeita path traversal
    /// (<c>..</c>, absolute paths que escapam) com <see cref="ArgumentException"/>.
    /// </summary>
    public string ResolveSafely(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var candidate = Path.GetFullPath(Path.Combine(CanonicalPath, relativePath));
        var expectedPrefix = CanonicalPath + Path.DirectorySeparatorChar;

        if (!candidate.Equals(CanonicalPath, StringComparison.Ordinal)
            && !candidate.StartsWith(expectedPrefix, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Path '{relativePath}' resolves outside files root.",
                nameof(relativePath));
        }

        return candidate;
    }

    /// <summary>
    /// Diretórios ignorados por padrão em <see cref="EnumerateFiles"/> — mesmo espírito
    /// do <c>rg</c>/<c>fd</c>: filesystem tem dezenas de milhares de arquivos de build,
    /// controle de versão e IDE que poluem qualquer listagem útil pro agente.
    /// </summary>
    public static readonly IReadOnlySet<string> IgnoredDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".hg", ".svn",
        "bin", "obj",
        "node_modules",
        ".vs", ".vscode", ".idea",
    };

    /// <summary>Enumera arquivos sob o root, paths relativos com <c>/</c>, pulando diretórios ignorados.</summary>
    public IEnumerable<string> EnumerateFiles(string? searchPattern = null)
    {
        var pattern = string.IsNullOrWhiteSpace(searchPattern) ? "*" : searchPattern;
        foreach (var abs in Directory.EnumerateFiles(CanonicalPath, pattern, SearchOption.AllDirectories))
        {
            var relative = ToRelative(abs);
            if (IsInIgnoredDirectory(relative))
            {
                continue;
            }

            yield return relative;
        }
    }

    private static bool IsInIgnoredDirectory(string relativePath)
    {
        // relativePath usa '/' como separador (ver ToRelative)
        var segments = relativePath.Split('/');
        // Arquivos na raiz têm apenas 1 segmento — não podem estar em subdiretório ignorado
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (IgnoredDirectories.Contains(segments[i]))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Converte absolute path (dentro do root) pra relativo com <c>/</c>.</summary>
    public string ToRelative(string absolutePath)
    {
        var rel = Path.GetRelativePath(CanonicalPath, absolutePath);
        return rel.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>
    /// Heurística de "é texto" — arquivos com bytes NULL (0x00) nos primeiros 8 KB
    /// são tratados como binário e recusados por <c>read_file</c>/<c>grep</c>.
    /// Não é infalível (UTF-16 legitimo tem NULLs), mas evita 99% dos falsos positivos.
    /// </summary>
    public static bool LooksBinary(byte[] head)
    {
        var sample = head.Length > 8192 ? 8192 : head.Length;
        for (var i = 0; i < sample; i++)
        {
            if (head[i] == 0x00)
            {
                return true;
            }
        }

        return false;
    }
}
