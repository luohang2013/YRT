using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace YRT.Core;

/// <summary>
/// 一个参与混搭的「来源包」（zip 或目录）。加载时按分类规则把文件归桶。
/// </summary>
public sealed class SourcePack
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public bool IsZip { get; init; }
    /// <summary>zip 内公共前缀 / 目录内根子目录下的相对起点（空串 = 包根即根）</summary>
    public string RootPrefix { get; init; } = "";
    public int FileCount { get; private set; }
    public long TotalBytes { get; private set; }

    public string BytesText => PackAnalyzer.FmtBytes(TotalBytes);
    public string KindText => IsZip ? " · zip" : " · 目录";

    /// <summary>分类 → (相对条目名, 实际来源路径/zip条目名) 列表</summary>
    public Dictionary<string, List<(string rel, string source)>> Categories { get; } = new();

    public override string ToString() => $"{Name}（{FileCount} 个文件）";

    // ============ 加载 ============

    public static SourcePack Load(string path)
    {
        var pack = Directory.Exists(path)
            ? LoadDir(path)
            : File.Exists(path) && PackPacker.IsPackArchive(path)
                ? LoadZip(path)
                : throw new FileNotFoundException("路径不存在或不是 zip/mcpack/目录：" + path);
        return pack;
    }

    private static SourcePack LoadDir(string dir)
    {
        var root = dir;
        if (!File.Exists(System.IO.Path.Combine(dir, "pack.mcmeta")))
        {
            var subs = Directory.GetDirectories(dir);
            if (subs.Length == 1 && File.Exists(System.IO.Path.Combine(subs[0], "pack.mcmeta")))
                root = subs[0];
        }
        var name = System.IO.Path.GetFileName(dir.TrimEnd(System.IO.Path.DirectorySeparatorChar)) ?? dir;
        var pack = new SourcePack { Name = name, Path = root, IsZip = false };
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                var rel = System.IO.Path.GetRelativePath(root, f).Replace('\\', '/');
                pack.Add(rel, f, new FileInfo(f).Length);
            }
            catch { /* 跳过被占用文件 */ }
        }
        return pack;
    }

    private static SourcePack LoadZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .Where(e => !e.FullName.EndsWith("/", StringComparison.Ordinal))
            .ToList();
        var prefix = FindPrefix(entries.Select(e => e.FullName.Replace('\\', '/')));
        var name = System.IO.Path.GetFileNameWithoutExtension(zipPath);
        var pack = new SourcePack
        {
            Name = name,
            Path = zipPath,
            IsZip = true,
            RootPrefix = prefix,
        };
        foreach (var e in entries)
            pack.Add(e.FullName.Replace('\\', '/')[prefix.Length..], e.FullName, e.Length);
        return pack;
    }

    private static string FindPrefix(IEnumerable<string> entryNames)
    {
        var names = entryNames.ToList();
        if (names.Count == 0) return "";
        var first = names[0];
        var slash = first.IndexOf('/');
        if (slash <= 0) return "";
        var candidate = first[..(slash + 1)];
        return names.All(n => n.StartsWith(candidate, StringComparison.Ordinal)) ? candidate : "";
    }

    private void Add(string rel, string source, long size = 0)
    {
        FileCount++;
        TotalBytes += size;
        var cat = PackAnalyzer.Classify(rel.ToLowerInvariant());
        if (!Categories.TryGetValue(cat, out var list))
            Categories[cat] = list = new List<(string, string)>();
        list.Add((rel, source));
    }

    // ============ 混搭导出 ============

    /// <summary>
    /// 按选择聚合导出 zip：每个分类从指定来源包复制文件。
    /// 返回写入的文件个数。
    /// </summary>
    public static int ExportMerged(
        IReadOnlyDictionary<string, SourcePack?> picks,
        string outZip,
        int defaultPackFormat = 34)
    {
        outZip = System.IO.Path.GetFullPath(outZip);
        var parent = System.IO.Path.GetDirectoryName(outZip);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        var used = picks.Where(kv => kv.Value is not null).Select(kv => (kv.Key, Pack: kv.Value!)).ToList();
        if (used.Count == 0) throw new InvalidOperationException("没有选择任何内容来源");

        // pack_format 取各来源包 mcmeta 的最小值（兼容性最好），解析不出用默认
        var fmt = defaultPackFormat;
        var fmts = new List<int>();
        foreach (var g in used.GroupBy(u => u.Pack))
        {
            var f = TryReadFormat(g.Key);
            if (f is not null) fmts.Add(f.Value);
        }
        if (fmts.Count > 0) fmt = fmts.Min();

        using var fs = File.Create(outZip);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        const string metaCat = "清单 pack.mcmeta / pack.png";

        // 清单：来源包有就拷，否则生成默认 mcmeta
        var wroteMcmeta = false;
        if (picks.TryGetValue(metaCat, out var metaPack) && metaPack is not null)
        {
            foreach (var (rel, source) in metaPack.Categories[metaCat])
            {
                CopyEntry(zip, metaPack, rel, source);
                if (rel.EndsWith("pack.mcmeta", StringComparison.OrdinalIgnoreCase)) wroteMcmeta = true;
            }
        }
        if (!wroteMcmeta)
        {
            var mcmeta = JsonSerializer.Serialize(new
            {
                pack = new
                {
                    pack_format = fmt,
                    description = $"YRT 混搭资源包（{string.Join(" + ", used.Select(u => u.Pack.Name).Distinct())}）",
                },
            });
            var e = zip.CreateEntry("pack.mcmeta", CompressionLevel.Optimal);
            using (var w = new StreamWriter(e.Open()))
            {
                w.Write(mcmeta);
            }
        }

        // 各分类内容：按「分类 → 所选来源包」精确复制，每个分类只取一次
        var count = 0;
        foreach (var (cat, pack) in used)
        {
            if (cat == metaCat) continue; // 清单上面已处理
            if (!pack.Categories.TryGetValue(cat, out var list)) continue;
            foreach (var (rel, source) in list)
            {
                CopyEntry(zip, pack, rel, source);
                count++;
            }
        }
        return count;
    }

    /// <summary>供分析页分类导出复用的单条目复制（保持原可见性封装）。</summary>
    public static void CopyEntryPublic(System.IO.Compression.ZipArchive zip, SourcePack pack, string rel, string source)
        => CopyEntry(zip, pack, rel, source);

    /// <summary>把包内一个条目写入目标 zip（zip 来源直接复制压缩字节；目录来源读文件）。</summary>
    private static void CopyEntry(ZipArchive zip, SourcePack pack, string rel, string source)
    {
        rel = rel.Replace('\\', '/');
        var entry = zip.CreateEntry(rel, CompressionLevel.Optimal);
        using var dst = entry.Open();
        if (pack.IsZip)
        {
            using var z = ZipFile.OpenRead(pack.Path);
            var src = z.GetEntry(source) ?? throw new FileNotFoundException("zip 条目丢失：" + source);
            using var s = src.Open();
            s.CopyTo(dst);
        }
        else
        {
            using var s = File.OpenRead(source);
            s.CopyTo(dst);
        }
    }

    private static int? TryReadFormat(SourcePack pack)
    {
        try
        {
            string text;
            if (pack.IsZip)
            {
                using var z = ZipFile.OpenRead(pack.Path);
                var entryName = pack.RootPrefix + "pack.mcmeta";
                var e = z.GetEntry(entryName) ?? z.Entries.FirstOrDefault(x =>
                    x.FullName.Replace('\\', '/').Equals(entryName, StringComparison.OrdinalIgnoreCase));
                if (e is null) return null;
                using var s = new StreamReader(e.Open());
                text = s.ReadToEnd();
            }
            else
            {
                var p = System.IO.Path.Combine(pack.Path, "pack.mcmeta");
                if (!File.Exists(p)) return null;
                text = File.ReadAllText(p);
            }
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("pack", out var pk) &&
                pk.TryGetProperty("pack_format", out var f) &&
                f.ValueKind == JsonValueKind.Number &&
                f.TryGetInt32(out var v))
                return v;
        }
        catch
        {
            // 解析失败按缺失处理
        }
        return null;
    }
}
