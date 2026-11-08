using System.IO;
using System.IO.Compression;

namespace YRT.Core;

/// <summary>
/// 资源包打包/解包（移植自 RustPackGen packer.rs）。
///
/// 关键点：zip 条目路径必须强制使用 '/' 分隔符，Minecraft 才能识别！
/// 因此手动构造条目名（相对路径把 '\\' 全部替换为 "/"），
/// 绝不能让 zip 库直接拿 Windows 反斜杠路径当条目名。
/// </summary>
public static class PackPacker
{
    /// <summary>把 <paramref name="srcDir"/> 整个目录打包为 <paramref name="outZip"/>，返回打包的文件个数。</summary>
    public static int PackDir(string srcDir, string outZip)
        => PackDir(srcDir, outZip, autoMcmeta: false, packName: "");

    /// <summary>
    /// 打包目录；autoMcmeta=true 且源目录根没有 pack.mcmeta 时，
    /// 在 zip 内补一份自动生成的清单（不修改源目录本身）。
    /// </summary>
    public static int PackDir(string srcDir, string outZip, bool autoMcmeta, string packName)
    {
        srcDir = Path.GetFullPath(srcDir);
        outZip = Path.GetFullPath(outZip);

        var parent = Path.GetDirectoryName(outZip);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

        using var fs = File.Create(outZip);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        var count = 0;
        if (autoMcmeta && !File.Exists(Path.Combine(srcDir, "pack.mcmeta")))
        {
            var e = zip.CreateEntry("pack.mcmeta", CompressionLevel.Optimal);
            using var w = new StreamWriter(e.Open());
            w.Write(PackMeta.BuildJson(
                string.IsNullOrWhiteSpace(packName) ? "我的资源包" : packName.Trim() + " - by YHCT 的资源包工具箱",
                34));
        }
        AddDir(zip, srcDir, srcDir, ref count);
        return count;
    }

    /// <summary>递归遍历目录写入 zip。条目名相对 baseDir 且强制 '/' 分隔；固定排序保证产物可复现。</summary>
    private static void AddDir(ZipArchive zip, string baseDir, string dir, ref int count)
    {
        // Ordinal 排序，与 Rust 版按文件名排序对齐，保证产物可复现
        var entries = new DirectoryInfo(dir)
            .EnumerateFileSystemInfos()
            .OrderBy(e => e.Name, StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            // 条目名 = 相对路径，'\' 全部替换为 '/'（Minecraft 只认正斜杠）
            var entryName = Path.GetRelativePath(baseDir, entry.FullName).Replace('\\', '/');

            if (entry is DirectoryInfo sub)
            {
                zip.CreateEntry(entryName + "/"); // 目录占位条目（对齐 Rust 版）
                AddDir(zip, baseDir, sub.FullName, ref count);
            }
            else
            {
                var e = zip.CreateEntry(entryName, CompressionLevel.Optimal);
                using var src = File.OpenRead(entry.FullName);
                using var dst = e.Open();
                src.CopyTo(dst);
                count++;
            }
        }
    }

    /// <summary>
    /// 判断文件是否为资源包压缩档：Java 版 zip 与基岩/网易 mcpack（后者本质是改后缀的 zip）。
    /// </summary>
    public static bool IsPackArchive(string path) =>
        path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".mcpack", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 解包 zip/mcpack 到 <paramref name="destDir"/>（自动新建目录），返回解出的文件个数。
    /// 供"解包现有资源包 → 编辑 → 重新打包"工作流使用。
    /// </summary>
    public static int Unzip(string zipPath, string destDir)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var fullDest = Path.GetFullPath(destDir);
        Directory.CreateDirectory(fullDest);

        var count = 0;
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue; // 目录条目，跳过

            var target = Path.GetFullPath(
                Path.Combine(fullDest, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));

            // 防 Zip Slip：条目路径不得逃出目标目录
            if (!target.StartsWith(fullDest + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            count++;
        }
        return count;
    }
}
