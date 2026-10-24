using System.IO.Compression;

internal static class ArchiveChecks
{
    internal static void Run(string variant, string assemblyPath, string archivePath, string companionPath)
    {
        if (variant != "standalone" && variant != "api") throw new Exception("Unknown archive variant");
        var files = new List<string> { "Nivalis.Cigarette.dll", "Nivalis.ModCompanion.dll" };
        if (variant == "api") files.Add("Nivalis.Cigarette.Api.dll");
        var expected = files.Select(name => "BepInEx/plugins/" + name).OrderBy(name => name).ToArray();
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries.Where(entry => !entry.FullName.EndsWith('/')).ToArray();
        if (!entries.Select(entry => entry.FullName).OrderBy(name => name).SequenceEqual(expected))
            throw new Exception("Archive contents do not match the selected variant");
        foreach (var entry in entries)
        {
            using var source = entry.Open();
            using var bytes = new MemoryStream();
            source.CopyTo(bytes);
            var built = File.ReadAllBytes(entry.Name == "Nivalis.ModCompanion.dll" ? companionPath :
                Path.Combine(Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!, entry.Name));
            if (!bytes.ToArray().SequenceEqual(built)) throw new Exception("Archive contains stale/wrong DLL: " + entry.FullName);
        }
        Console.WriteLine($"PASS: {variant} archive contains exactly the expected DLLs with byte-identical build output.");
    }
}
