using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace YRT.Core;

/// <summary>分类统计条目</summary>
public sealed class CategoryStat
{
    public string Name { get; init; } = "";
    public int Count { get; set; }
    public long Bytes { get; set; }
    /// <summary>相对占比 0-1（由 ViewModel 归一化后填充）</summary>
    public double Ratio { get; set; }

    public string BytesText => PackAnalyzer.FmtBytes(Bytes);
}

/// <summary>诊断条目</summary>
public sealed class DiagItem
{
    /// <summary>error / warn / info</summary>
    public string Level { get; init; } = "info";
    public string Message { get; init; } = "";

    public string LevelIcon => Level switch
    {
        "error" => "\uE783", // Error
        "warn" => "\uE7BA",  // Warning
        _ => "\uE946",       // Info
    };

    public System.Windows.Media.Brush LevelBrush => Level switch
    {
        "error" => new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xC4, 0x55, 0x4D)),
        "warn" => new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0xB0, 0x7D, 0x3A)),
        _ => System.Windows.Media.Brushes.Gray,
    };
}

/// <summary>目录树节点</summary>
public sealed class TreeNode
{
    public string Name { get; init; } = "";
    public List<TreeNode> Children { get; } = new();
    public bool IsDir => Children.Count > 0 || string.IsNullOrEmpty(SuffixOf(Name));
    public long Bytes { get; set; }

    public string BytesText => Children.Count > 0 ? $"{Children.Count} 项" : PackAnalyzer.FmtBytes(Bytes);

    public static string? SuffixOf(string name) =>
        name.Contains('.') ? name[(name.LastIndexOf('.') + 1)..] : null;

    public override string ToString() => Name;
}

/// <summary>
/// 资源包分析结果（图六：分析页开发）。
/// 输入 zip 或目录 → pack.mcmeta 解析、目录树、分类占用统计、诊断。
/// </summary>
public sealed class PackAnalysis
{
    public string Source { get; init; } = "";
    public bool IsZip { get; init; }
    /// <summary>包内根（zip 内前缀或目录内子目录），空串表示无</summary>
    public string RootPrefix { get; init; } = "";
    public string Description { get; init; } = "(未提供)";
    public string PackFormat { get; init; } = "(未提供)";
    public int FileCount { get; init; }
    public long TotalBytes { get; init; }
    public string TotalBytesText => PackAnalyzer.FmtBytes(TotalBytes);
    public List<CategoryStat> Stats { get; init; } = new();
    public List<DiagItem> Diagnostics { get; init; } = new();
    public TreeNode TreeRoot { get; init; } = new() { Name = "(root)" };
}

public static class PackAnalyzer
{
    /// <summary>字节量格式化。</summary>
    public static string FmtBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / 1073741824.0:F2} GB",
        >= 1L << 20 => $"{bytes / 1048576.0:F1} MB",
        >= 1L << 10 => $"{bytes / 1024.0:F1} KB",
        _ => $"{bytes} B",
    };

    /// <summary>入口：分析 zip/mcpack 或目录。</summary>
    public static PackAnalysis Analyze(string path)
    {
        if (Directory.Exists(path)) return AnalyzeDir(path);
        if (File.Exists(path) && PackPacker.IsPackArchive(path))
            return AnalyzeZip(path);
        throw new FileNotFoundException("路径不存在或不是 zip/mcpack/目录：" + path);
    }

    // ================= 通用分类规则 =================

    /// <summary>按条目相对路径（小写、正斜杠）归类。</summary>
    public static string Classify(string relLower)
    {
        bool Has(string seg) => relLower.Contains(seg);
        if (relLower.EndsWith("pack.mcmeta") || relLower.EndsWith("pack.png")) return "清单 pack.mcmeta / pack.png";
        if (Has("textures/block") || Has("textures/blocks")) return "方块贴图";
        if (Has("textures/item") || Has("textures/items")) return "物品贴图";
        if (Has("textures/entity")) return "实体贴图";
        if (Has("textures/gui")) return "GUI 贴图";
        if (Has("textures/")) return "其他贴图";
        if (relLower.EndsWith("sounds.json")) return "音效索引 sounds.json";
        if (Has("sounds/")) return "音效";
        if (Has("font/")) return "字体";
        if (Has("lang/")) return "语言文件";
        if (Has("models/")) return "模型";
        if (Has("shaders/")) return "着色器";
        if (Has("particles/")) return "粒子";
        if (Has("texts/") || relLower.EndsWith(".json") || relLower.EndsWith(".mcmeta")) return "其他数据";
        return "其他文件";
    }

    private static readonly string[] KnownOrder =
    {
        "清单 pack.mcmeta / pack.png",
        "方块贴图", "物品贴图", "实体贴图", "GUI 贴图", "其他贴图",
        "音效索引 sounds.json", "音效", "字体", "语言文件", "模型", "着色器", "粒子",
        "其他数据", "其他文件",
    };

    // ================= zip 输入 =================

    private static PackAnalysis AnalyzeZip(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .Where(e => !e.FullName.EndsWith("/", StringComparison.Ordinal))
            .ToList();

        // 找包根：所有条目的公共前缀（处理 zip 内多包一层目录的情况）
        var prefix = FindPrefix(entries.Select(e => e.FullName.Replace('\\', '/')));

        var analysis = BuildCommon(
            source: zipPath, isZip: true, prefix: prefix,
            files: entries.Select(e =>
                (rel: e.FullName.Replace('\\', '/')[prefix.Length..], size: e.Length)),
            mcmetaText: () => entries.FirstOrDefault(e =>
                e.FullName.Replace('\\', '/').Equals(prefix + "pack.mcmeta", StringComparison.OrdinalIgnoreCase))
                is { } me ? new StreamReader(me.Open()).ReadToEnd() : null,
            isMcpack: zipPath.EndsWith(".mcpack", StringComparison.OrdinalIgnoreCase),
            manifestText: () => entries.FirstOrDefault(e =>
                e.FullName.Replace('\\', '/').Equals(prefix + "manifest.json", StringComparison.OrdinalIgnoreCase))
                is { } mf ? new StreamReader(mf.Open()).ReadToEnd() : null);

        // zip 特有诊断：反斜杠条目
        foreach (var e in entries)
        {
            if (!e.FullName.Contains('\\')) continue;
            analysis.Diagnostics.Add(new DiagItem
            {
                Level = "error",
                Message = "zip 内含反斜杠路径条目（Minecraft 无法识别）：" + e.FullName,
            });
        }
        return analysis;
    }

    // ================= 目录输入 =================

    private static PackAnalysis AnalyzeDir(string dir)
    {
        // 目录根同样下探一层：若 pack.mcmeta 不在顶层而唯一子目录里有，则取子目录
        var root = dir;
        if (!File.Exists(Path.Combine(dir, "pack.mcmeta")))
        {
            var subs = Directory.GetDirectories(dir);
            if (subs.Length == 1 && File.Exists(Path.Combine(subs[0], "pack.mcmeta")))
                root = subs[0];
        }

        var files = new List<(string rel, long size)>();
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try { files.Add((Path.GetRelativePath(root, f).Replace('\\', '/'), new FileInfo(f).Length)); }
            catch { /* 跳过被占用文件 */ }
        }

        return BuildCommon(
            source: dir, isZip: false, prefix: "",
            files: files,
            mcmetaText: () => File.Exists(Path.Combine(root, "pack.mcmeta"))
                ? File.ReadAllText(Path.Combine(root, "pack.mcmeta")) : null);
    }

    // ================= 公共装配 =================

    private static PackAnalysis BuildCommon(
        string source, bool isZip, string prefix,
        IEnumerable<(string rel, long size)> files,
        Func<string?> mcmetaText,
        bool isMcpack = false,
        Func<string?>? manifestText = null)
    {
        var list = files.ToList();
        var total = list.Sum(f => f.size);

        // 分类统计
        var stats = new Dictionary<string, CategoryStat>();
        foreach (var (rel, size) in list)
        {
            var cat = Classify(rel.ToLowerInvariant());
            if (!stats.TryGetValue(cat, out var s))
                stats[cat] = s = new CategoryStat { Name = cat };
            s.Count++;
            s.Bytes += size;
        }
        var ordered = stats.Values
            .OrderBy(s => Array.IndexOf(KnownOrder, s.Name) is var i && i >= 0 ? i : 99)
            .ToList();
        foreach (var s in ordered) s.Ratio = total > 0 ? (double)s.Bytes / total : 0;

        // 目录树（限深 6 层，防止病态结构）
        var treeRoot = new TreeNode { Name = prefix.Length == 0 ? "(包根)" : prefix.TrimEnd('/') };
        foreach (var (rel, size) in list.OrderBy(f => f.rel, StringComparer.Ordinal))
        {
            var segs = rel.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segs.Length == 0) continue;
            var cur = treeRoot;
            var depth = Math.Min(segs.Length, 6);
            for (var i = 0; i < depth; i++)
            {
                var leaf = i == segs.Length - 1;
                var node = cur.Children.FirstOrDefault(c => c.Name == segs[i]);
                if (node is null)
                {
                    node = new TreeNode { Name = segs[i], Bytes = leaf ? size : 0 };
                    cur.Children.Add(node);
                }
                else
                {
                    if (leaf) node.Bytes = size;
                }
                cur = node;
            }
        }
        AccumBytes(treeRoot);

        // mcmeta 解析（mcpack 基岩包无 pack.mcmeta 时回退读 manifest.json）
        string description = "(未提供)", packFormat = "(未提供)";
        string? metaRaw = null, manifestRaw = null;
        try { metaRaw = mcmetaText(); }
        catch { /* 读取失败按缺失处理 */ }
        if (isMcpack && manifestText is not null)
        {
            try { manifestRaw = manifestText(); }
            catch { /* 读取失败按缺失处理 */ }
        }
        if (metaRaw is not null)
        {
            try
            {
                using var doc = JsonDocument.Parse(metaRaw);
                if (doc.RootElement.TryGetProperty("pack", out var p))
                {
                    if (p.TryGetProperty("description", out var d))
                        description = d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : d.ToString();
                    if (p.TryGetProperty("pack_format", out var f))
                        packFormat = f.ValueKind == JsonValueKind.Number ? f.GetRawText() : f.ToString();
                }
            }
            catch
            {
                description = "(pack.mcmeta 解析失败)";
            }
        }
        else if (manifestRaw is not null)
        {
            // 基岩包 manifest.json：header.name / header.description
            try
            {
                using var doc = JsonDocument.Parse(manifestRaw);
                if (doc.RootElement.TryGetProperty("header", out var h))
                {
                    if (h.TryGetProperty("description", out var d))
                        description = d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : d.ToString();
                    if (description.Length == 0 && h.TryGetProperty("name", out var n))
                        description = n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : n.ToString();
                }
                packFormat = "(基岩包)";
            }
            catch
            {
                description = "(manifest.json 解析失败)";
                packFormat = "(基岩包)";
            }
        }

        var analysis = new PackAnalysis
        {
            Source = source,
            IsZip = isZip,
            RootPrefix = prefix,
            Description = description,
            PackFormat = packFormat,
            FileCount = list.Count,
            TotalBytes = total,
            Stats = ordered,
            TreeRoot = treeRoot,
        };

        // 通用诊断
        var diags = analysis.Diagnostics;
        if (metaRaw is null && manifestRaw is not null)
            diags.Add(new DiagItem { Level = "info", Message = "基岩包（mcpack）：使用 manifest.json 清单，Java 版不识别此格式，仅供查看/混搭。" });
        else if (metaRaw is null && isMcpack)
            diags.Add(new DiagItem { Level = "error", Message = "缺少清单文件（基岩包需 manifest.json，Java 包需 pack.mcmeta）" });
        else if (metaRaw is null)
            diags.Add(new DiagItem { Level = "error", Message = "缺少 pack.mcmeta（Minecraft 不识别该包）" });
        else if (packFormat == "(未提供)")
            diags.Add(new DiagItem { Level = "warn", Message = "pack.mcmeta 缺少 pack.pack_format 字段" });

        var hasPackPng = list.Any(f => f.rel.Equals("pack.png", StringComparison.OrdinalIgnoreCase));
        if (!hasPackPng)
            diags.Add(new DiagItem { Level = "warn", Message = "缺少 pack.png（资源包列表中不会显示图标）" });

        foreach (var (rel, _) in list)
        {
            if (rel.Contains('\\'))
                diags.Add(new DiagItem { Level = "warn", Message = "路径含反斜杠（目录来源常见问题）：" + rel });
            var name = rel[(rel.LastIndexOf('/') + 1)..];
            if (name.Any(c => c >= 'A' && c <= 'Z'))
                diags.Add(new DiagItem { Level = "info", Message = "文件名含大写字母（部分版本敏感）：" + rel });
        }
        if (list.Count == 0)
            diags.Add(new DiagItem { Level = "error", Message = "包内没有任何文件" });
        if (diags.Count == 0)
            diags.Add(new DiagItem { Level = "info", Message = "未发现问题，该资源包结构健康。" });
        return analysis;
    }

    /// <summary>推断包内公共前缀（zip 整体多包一层目录时返回 "dir/"，否则 ""）。</summary>
    private static string FindPrefix(IEnumerable<string> entryNames)
    {
        var names = entryNames.ToList();
        if (names.Count == 0) return "";
        var first = names[0];
        var slash = first.IndexOf('/');
        if (slash <= 0) return "";
        var candidate = first[..(slash + 1)];
        var all = names.All(n => n.StartsWith(candidate, StringComparison.Ordinal));
        return all ? candidate : "";
    }

    private static long AccumBytes(TreeNode node)
    {
        if (node.Children.Count == 0) return node.Bytes;
        long sum = 0;
        foreach (var c in node.Children) sum += AccumBytes(c);
        node.Bytes = sum;
        return sum;
    }
}
