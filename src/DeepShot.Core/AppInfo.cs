namespace DeepShot.Core;

/// <summary>应用元信息与发布配置。</summary>
public static class AppInfo
{
    public const string Name = "DeepShot";
    public const string DataFolderName = "DeepShot";

    /// <summary>
    /// 更新渠道地址。发布前填入真实仓库/版本清单地址；
    /// 为空时必须向用户明确提示"尚未配置更新渠道"，不得显示"已是最新版"。
    /// </summary>
    public const string UpdateManifestUrl = "";
}
