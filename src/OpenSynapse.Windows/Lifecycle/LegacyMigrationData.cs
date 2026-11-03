namespace OpenSynapse.Windows.Lifecycle;

/// <summary>Preserves both versions of a migrated user file without replacing the newer destination.</summary>
internal static class LegacyMigrationData
{
    internal static IEnumerable<string> EnumerateFiles(string directory)
    {
        RejectLink(directory);
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            RejectLink(file);
            yield return file;
        }
        foreach (var child in Directory.EnumerateDirectories(directory))
            foreach (var file in EnumerateFiles(child)) yield return file;
    }

    internal static void PreserveFile(string source, string destination)
    {
        RejectLink(source);
        EnsureDirectory(Path.GetDirectoryName(destination)!);
        using var input = OpenRead(source);
        for (var version = -1; ; version++)
        {
            var candidate = Candidate(destination, version);
            if (File.Exists(candidate))
            {
                if (EqualsContents(input, candidate)) return;
                continue;
            }
            var temporary = candidate + ".migration-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    input.Position = 0;
                    input.CopyTo(output);
                    output.Flush(flushToDisk: true);
                }
                File.Move(temporary, candidate);
                return;
            }
            catch (IOException) when (File.Exists(candidate)) { version--; } // Another migration won; compare the same candidate.
            finally { File.Delete(temporary); }
        }
    }

    internal static bool HasPreservedCopy(string source, string destination)
    {
        RejectLink(source);
        using var input = OpenRead(source);
        if (File.Exists(destination) && EqualsContents(input, destination)) return true;
        var directory = Path.GetDirectoryName(destination)!;
        if (!Directory.Exists(directory)) return false;
        EnsureDirectory(directory);
        foreach (var candidate in Directory.EnumerateFiles(directory, Path.GetFileName(destination) + ".legacy-backup*"))
            if (EqualsContents(input, candidate)) return true;
        return false;
    }

    private static string Candidate(string destination, int version) => version < 0 ? destination
        : destination + ".legacy-backup" + (version == 0 ? string.Empty : "." + version);

    private static bool EqualsContents(FileStream source, string destination)
    {
        RejectLink(destination);
        using var other = OpenRead(destination);
        if (source.Length != other.Length) return false;
        source.Position = 0;
        Span<byte> left = stackalloc byte[8192], right = stackalloc byte[8192];
        while (source.Position < source.Length)
        {
            var count = source.Read(left);
            if (count == 0) return false;
            other.ReadExactly(right[..count]);
            if (!left[..count].SequenceEqual(right[..count])) return false;
        }
        return true;
    }

    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read);

    internal static void EnsureDirectory(string directory)
    {
        var parent = Path.GetDirectoryName(directory);
        if (parent is not null) EnsureDirectory(parent);
        if (Directory.Exists(directory)) RejectLink(directory);
        else Directory.CreateDirectory(directory);
    }

    internal static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Migration does not follow reparse points.");
    }
}
