namespace OpenApiMcp.Services;

/// <summary>
/// Scans well-known "contracts/" directories and loads every .json/.yaml/.yml
/// file into the given <see cref="ContractStore"/>.
///
/// Search order:
///   1. Next to the binary (AppContext.BaseDirectory)
///   2. Current working directory
///   3. Up to 3 parent directories from the current working directory
/// </summary>
public static class ContractAutoLoader
{
    public static void LoadContracts(ContractStore store, string? explicitDir = null)
    {
        if (explicitDir is not null)
        {
            store.LoadDirectory(explicitDir);
            return;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void TryLoad(string dir)
        {
            var full = Path.GetFullPath(dir);
            if (visited.Add(full))
                store.LoadDirectory(full);
        }

        TryLoad(Path.Combine(AppContext.BaseDirectory, "contracts"));
        TryLoad(Path.Combine(Directory.GetCurrentDirectory(), "contracts"));

        var search = Directory.GetCurrentDirectory();
        for (int i = 0; i < 3; i++)
        {
            var parent = Path.GetDirectoryName(search);
            if (parent is null) break;
            search = parent;
            TryLoad(Path.Combine(search, "contracts"));
        }
    }
}
