namespace ServiceMap.Platform.Linux;

/// <summary>
/// Best-effort mapping of socket inode -> (pid, process name) by scanning
/// /proc/[pid]/fd for "socket:[inode]" symlinks. Requires permission to read
/// other processes' fd tables (root for full coverage); silently skips what it
/// cannot read.
/// </summary>
internal sealed class SocketInodeResolver
{
    private readonly Dictionary<long, (int Pid, string Name, string? Path)> _byInode = new();

    public SocketInodeResolver()
    {
        try { Build(); }
        catch { /* partial map is acceptable */ }
    }

    public (int Pid, string Name, string? Path) Resolve(long inode)
    {
        if (inode != 0 && _byInode.TryGetValue(inode, out var info))
            return info;
        return (0, "unknown", null);
    }

    private void Build()
    {
        foreach (var pidDir in Directory.EnumerateDirectories("/proc"))
        {
            var leaf = Path.GetFileName(pidDir);
            if (!int.TryParse(leaf, out int pid)) continue;

            string fdDir = Path.Combine(pidDir, "fd");
            string[] fds;
            try { fds = Directory.GetFileSystemEntries(fdDir); }
            catch { continue; }

            string? name = null;
            string? exe = null;

            foreach (var fd in fds)
            {
                string target;
                try { target = ReadLink(fd); }
                catch { continue; }

                // Looks like "socket:[12345]".
                if (target.StartsWith("socket:[", StringComparison.Ordinal))
                {
                    int start = target.IndexOf('[') + 1;
                    int end = target.IndexOf(']', start);
                    if (end > start &&
                        long.TryParse(target.AsSpan(start, end - start), out long inode))
                    {
                        name ??= ReadComm(pidDir);
                        exe ??= TryReadExe(pidDir);
                        _byInode[inode] = (pid, name ?? leaf, exe);
                    }
                }
            }
        }
    }

    private static string ReadLink(string path)
    {
        var info = new FileInfo(path);
        // .NET resolves symlink targets via LinkTarget.
        return info.LinkTarget ?? string.Empty;
    }

    private static string? ReadComm(string pidDir)
    {
        try { return File.ReadAllText(Path.Combine(pidDir, "comm")).Trim(); }
        catch { return null; }
    }

    private static string? TryReadExe(string pidDir)
    {
        try
        {
            var info = new FileInfo(Path.Combine(pidDir, "exe"));
            return info.LinkTarget;
        }
        catch { return null; }
    }
}
