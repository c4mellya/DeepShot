namespace DeepShot.Core;

/// <summary>应用元信息与发布配置。</summary>
public static class AppInfo
{
    public const string Name = "DeepShot";
    public const string DataFolderName = "DeepShot";

    /// <summary>GitHub 最新正式 Release 的 API 地址（保留原渠道常量名供现有入口使用）。</summary>
    public const string UpdateManifestUrl = "https://api.github.com/repos/c4mellya/DeepShot/releases/latest";
}
