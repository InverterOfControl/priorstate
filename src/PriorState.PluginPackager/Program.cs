using System.IO.Compression;
using System.Text.Json;

if (args.Length < 2) throw new ArgumentException("Usage: packager OUTPUT_DIRECTORY PLUGIN_BUILD_DIRECTORY ...");
Directory.CreateDirectory(args[0]);
foreach (var input in args.Skip(1))
{
    using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(input, "plugin.json")));
    var id = manifest.RootElement.GetProperty("id").GetString()!;
    using var bytes = new MemoryStream();
    using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
    {
        foreach (var file in Directory.EnumerateFiles(input, "*", SearchOption.AllDirectories)
            .Where(p => !p.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) && !p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => Path.GetRelativePath(input, p).Replace('\\', '/'), StringComparer.Ordinal))
        {
            var entry = archive.CreateEntry(Path.GetRelativePath(input, file).Replace('\\', '/'), CompressionLevel.Optimal);
            entry.LastWriteTime = new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            using var target = entry.Open();
            using var source = File.OpenRead(file);
            source.CopyTo(target);
        }
    }
    File.WriteAllBytes(Path.Combine(args[0], id + ".psplugin"), bytes.ToArray());
}
