using System.Text.RegularExpressions;
using MChub.Localization;

namespace MChub.Core.Minecraft.Instance.Java;

/// <summary>
/// 崩溃日志的本地（离线）Java 兼容性诊断。
///
/// 识别「Java 版本不合适」这一常见崩溃原因——典型如：当前 Java 缺少游戏需要的
/// <c>java.*</c> 标准库成员（例如 <c>java.lang.NoSuchMethodError: java.lang.Math.powExact</c>，
/// 常见于使用了过旧或早期预览/测试版 Java），或类文件主版本不受支持。
/// 命中时给出一段可读结论与修复建议，直接展示在崩溃报告里。
/// </summary>
public static partial class JavaCompatibilityDiagnostics
{
    public sealed record Result(string Title, string Detail);

    /// <param name="logText">崩溃报告 / 日志全文。</param>
    /// <param name="demandedMajorVersion">该实例所需的 Java 大版本（用于给出建议）。</param>
    public static Result? Analyse(string? logText, int demandedMajorVersion)
    {
        if (string.IsNullOrWhiteSpace(logText))
            return null;

        var unsupportedClassVersion = UnsupportedClassVersionRegex().Match(logText);
        if (unsupportedClassVersion.Success)
            return new Result(
                CommonLanguageManager.Instance.javaDiagnostics_title.CurrentValue(),
                string.Format(CommonLanguageManager.Instance.javaDiagnostics_unsupportedClassVersion.CurrentValue(),
                    unsupportedClassVersion.Value.Trim(), demandedMajorVersion));

        // 启动阶段的模块解析冲突：典型是启动器给的模块路径 / 忽略列表不正确，
        // 让原版客户端 jar 之类的文件被当成自动模块，和加载器的模块互相抢包。
        // 它和 Java 版本无关，必须给出不同结论，否则会把用户引向错误的排查方向。
        if (ModuleConflictRegex().IsMatch(logText))
            return new Result(
                CommonLanguageManager.Instance.javaDiagnostics_moduleConflictTitle.CurrentValue(),
                CommonLanguageManager.Instance.javaDiagnostics_moduleConflict.CurrentValue());

        var missing = MissingStandardLibraryRegex().Match(logText);
        if (missing.Success)
        {
            var detail = (missing.Groups["detail"].Value ?? string.Empty).Trim();
            var signature = detail.Length > 0 ? detail : missing.Value.Trim();

            // 只有缺失/异常的成员属于 java.* 标准库时，才判定为「Java 版本不合适」，
            // 以免把模组/加载器自身的 NoSuchMethodError 误判为 Java 问题。
            if (signature.Contains("java.", StringComparison.OrdinalIgnoreCase) ||
                signature.Contains("java/", StringComparison.OrdinalIgnoreCase))
                return new Result(
                    CommonLanguageManager.Instance.javaDiagnostics_title.CurrentValue(),
                    string.Format(CommonLanguageManager.Instance.javaDiagnostics_missingStandardApi.CurrentValue(),
                        signature, demandedMajorVersion));
        }

        return null;
    }

    /// <summary>把诊断结果格式化成一段可直接插入崩溃报告顶部的文本块。</summary>
    public static string Format(Result result)
    {
        var header = CommonLanguageManager.Instance.javaDiagnostics_blockHeader.CurrentValue();
        return $"""
                ==================== {header} ====================
                [{result.Title}] {result.Detail}
                =================================================

                """;
    }

    [GeneratedRegex(
        @"(?im)^\s*java\.lang\.(?:NoSuchMethodError|NoSuchFieldError|NoClassDefFoundError|ClassNotFoundException)\s*:\s*(?<detail>[^\r\n]*)")]
    private static partial Regex MissingStandardLibraryRegex();

    [GeneratedRegex(
        @"(?im)UnsupportedClassVersionError|Unsupported class file major version|has been compiled by a more recent version of the Java Runtime")]
    private static partial Regex UnsupportedClassVersionRegex();

    /// <summary>
    /// 模块系统冲突：ModLauncher / Bootstraplauncher 构建模块层时的典型报错，
    /// 以及「Modules A and B export package ...」这种双模块抢同一包的消息。
    /// </summary>
    [GeneratedRegex(
        @"(?im)(?:java\.lang\.)?(?:module\.(?:ResolutionException|FindException)|LayerInstantiationException)|Modules\s+\S+\s+and\s+\S+\s+export package")]
    private static partial Regex ModuleConflictRegex();
}
