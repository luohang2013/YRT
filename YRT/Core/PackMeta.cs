using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace YRT.Core;

/// <summary>pack.mcmeta 解析结果。</summary>
public sealed record McmetaInfo
{
    public bool Exists { get; init; }
    public string Description { get; init; } = "";
    /// <summary>解析失败时为 -1</summary>
    public int PackFormat { get; init; } = -1;
    public bool FormatValid => PackFormat >= 0;
    /// <summary>原始 JSON 文本（不存在时为空串）</summary>
    public string Raw { get; init; } = "";
}

/// <summary>
/// pack.mcmeta 读写工具：支持目录与 zip（zip 直接改写压缩包内条目），
/// 供分析页「编辑清单」与生成页「打包时自动补齐」复用。
/// </summary>
public static class PackMeta
{
    /// <summary>生成默认 mcmeta JSON（紧凑单行，与 Minecraft 官方格式一致）。</summary>
    public static string BuildJson(string description, int packFormat) =>
        "{\"pack\":{\"pack_format\":" + packFormat + ",\"description\":" +
        JsonSerializer.Serialize(description ?? "") + "}}";

    /// <summary>读取 pack.mcmeta（zip 或目录；不存在返回 Exists=false）。</summary>
    public static McmetaInfo Read(string path)
    {
        var raw = ReadRaw(path);
        if (raw is null) return new McmetaInfo { Exists = false };
        var info = new McmetaInfo { Exists = true, Raw = raw };
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("pack", out var p))
            {
                if (p.TryGetProperty("description", out var d))
                    info = info with { Description = d.ValueKind == JsonValueKind.String ? d.GetString() ?? "" : d.ToString() };
                if (p.TryGetProperty("pack_format", out var f) &&
                    f.ValueKind == JsonValueKind.Number && f.TryGetInt32(out var v))
                    info = info with { PackFormat = v };
            }
        }
        catch
        {
            // JSON 损坏：保留原文，格式按 -1（无效）处理
            info = info with { PackFormat = -1 };
        }
        return info;
    }

    /// <summary>把 description / pack_format 写回 zip 或目录，返回写入路径描述。</summary>
    public static string Write(string path, string description, int packFormat)
    {
        var json = BuildJson(description, packFormat);
        if (Directory.Exists(path))
        {
            var file = System.IO.Path.Combine(path, "pack.mcmeta");
            File.WriteAllText(file, json, new UTF8Encoding(false));
            return file;
        }
        // zip：Update 模式删除旧条目再新建（注意包内多一层目录的前缀）
        var prefix = FindZipPrefix(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        foreach (var e in zip.Entries
                     .Where(e => e.FullName.Replace('\\', '/')
                         .Equals(prefix + "pack.mcmeta", StringComparison.OrdinalIgnoreCase))
                     .ToList())
            e.Delete();
        var entry = zip.CreateEntry(prefix + "pack.mcmeta", CompressionLevel.Optimal);
        using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            w.Write(json);
        return path + " → " + prefix + "pack.mcmeta";
    }

    /// <summary>读取原文（zip/目录；不存在返回 null）。</summary>
    private static string? ReadRaw(string path)
    {
        if (Directory.Exists(path))
        {
            // 目录来源：与 LoadDir 一致，若根目录没有则下探唯一子目录
            var root = path;
            if (!File.Exists(System.IO.Path.Combine(path, "pack.mcmeta")))
            {
                var subs = Directory.GetDirectories(path);
                if (subs.Length == 1 && File.Exists(System.IO.Path.Combine(subs[0], "pack.mcmeta")))
                    root = subs[0];
            }
            var f = System.IO.Path.Combine(root, "pack.mcmeta");
            return File.Exists(f) ? File.ReadAllText(f) : null;
        }
        if (!File.Exists(path) || !PackPacker.IsPackArchive(path))
            throw new FileNotFoundException("路径不存在或不是 zip/mcpack/目录：" + path);
        using var zip = ZipFile.OpenRead(path);
        var prefix = FindZipPrefix(path);
        var entry = zip.Entries.FirstOrDefault(e =>
            e.FullName.Replace('\\', '/').Equals(prefix + "pack.mcmeta", StringComparison.OrdinalIgnoreCase));
        return entry is null ? null : new StreamReader(entry.Open()).ReadToEnd();
    }

    /// <summary>zip 包内公共前缀（与分析器一致：整体多包一层目录时返回 "dir/"）。</summary>
    private static string FindZipPrefix(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var names = zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name))
            .Select(e => e.FullName.Replace('\\', '/'))
            .ToList();
        if (names.Count == 0) return "";
        var slash = names[0].IndexOf('/');
        if (slash <= 0) return "";
        var candidate = names[0][..(slash + 1)];
        return names.All(n => n.StartsWith(candidate, StringComparison.Ordinal)) ? candidate : "";
    }

    /// <summary>
    /// 把分析结果生成为 Markdown 报告文本（概要 / 分类统计 / 诊断 / 目录树）。
    /// </summary>
    public static string BuildReport(PackAnalysis a)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 资源包分析报告");
        sb.AppendLine();
        sb.AppendLine($"> 由 YHCT 的资源包工具箱 生成于 {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        sb.AppendLine("## 概要");
        sb.AppendLine();
        sb.AppendLine($"- 来源：{a.Source}{(a.IsZip ? "（zip）" : "（目录）")}");
        sb.AppendLine($"- 包描述：{a.Description}");
        sb.AppendLine($"- 格式版本：{a.PackFormat}");
        sb.AppendLine($"- 文件总数：{a.FileCount}");
        sb.AppendLine($"- 总大小：{a.TotalBytesText}");
        sb.AppendLine();
        sb.AppendLine("## 分类占用");
        sb.AppendLine();
        sb.AppendLine("| 分类 | 文件数 | 大小 | 占比 |");
        sb.AppendLine("| --- | ---: | ---: | ---: |");
        foreach (var s in a.Stats)
            sb.AppendLine($"| {s.Name} | {s.Count} | {s.BytesText} | {s.Ratio:P1} |");
        sb.AppendLine();
        sb.AppendLine("## 诊断");
        sb.AppendLine();
        foreach (var d in a.Diagnostics)
        {
            var tag = d.Level switch { "error" => "❌ 错误", "warn" => "⚠️ 警告", _ => "ℹ️ 提示" };
            sb.AppendLine($"- **{tag}** {d.Message}");
        }
        sb.AppendLine();
        sb.AppendLine("## 目录结构");
        sb.AppendLine();
        sb.AppendLine("```");
        AppendTree(sb, a.TreeRoot, 0);
        sb.AppendLine("```");
        return sb.ToString();
    }

    private static void AppendTree(StringBuilder sb, TreeNode node, int depth)
    {
        if (depth > 0)
            sb.Append(new string(' ', (depth - 1) * 2)).Append("├ ")
              .Append(node.Name).Append('/').AppendLine();
        foreach (var c in node.Children.Take(60))
        {
            if (c.IsDir) AppendTree(sb, c, depth + 1);
            else sb.Append(new string(' ', depth * 2)).Append("├ ")
                   .Append(c.Name).Append("  (").Append(c.BytesText).Append(')').AppendLine();
        }
    }
}
