using Microsoft.Data.Sqlite;

namespace ZeepCut.Core.History;

/// <summary>
/// 历史库的表结构与版本管理（T23）。
/// </summary>
/// <remarks>
/// <para>
/// <b>版本机制</b>：版本号存在 SQLite 的 <c>PRAGMA user_version</c> 里（本版 <see cref="CurrentVersion"/> = 1）。
/// 建库/迁移全部由 <see cref="EnsureSchema"/> 在同一个事务里完成：
/// 新库（<c>user_version=0</c>）直接建表并写版本；已有库版本低于当前版本时按
/// <c>Migrations</c> 顺序补齐；版本**高于**当前版本（用户装过更新的版本又回退）时
/// 只写 WARN 日志、不降级、不改数据 —— 这种情况下一版必须能容忍多出来的列。
/// </para>
/// <para>
/// 加迁移的方式（T25+ 要加列时）：把 <see cref="CurrentVersion"/> 加 1，在
/// <see cref="Migrations"/> 里补一条 "从 vN 到 vN+1" 的 SQL；**不要**改 v1 的建表语句，
/// 否则老用户的库与新用户的库结构会分叉。
/// </para>
/// <para>
/// 索引按查询需求建：<c>CreatedAtUtc</c>（默认列表排序）、<c>IsFavorite</c>（收藏过滤）、
/// <c>Name</c>（名称搜索/排序）。名称搜索用 <c>LIKE '%x%'</c>，前面的通配符让索引
/// 用不上 —— 单机图库规模（几千条）下无所谓，等 T26 搜索变慢再考虑 FTS 或前缀索引。
/// </para>
/// </remarks>
public static class HistorySchema
{
    /// <summary>当前数据库版本（写入 <c>PRAGMA user_version</c>）。</summary>
    public const int CurrentVersion = 1;

    /// <summary>表名。</summary>
    public const string Table = "history";

    /// <summary>v1 建表语句（新库直接执行）。</summary>
    public const string CreateTableV1 = """
        CREATE TABLE IF NOT EXISTS history (
            Id TEXT PRIMARY KEY NOT NULL,
            Name TEXT NOT NULL,
            CreatedAtUtc TEXT NOT NULL,
            UpdatedAtUtc TEXT NOT NULL,
            IsFavorite INTEGER NOT NULL DEFAULT 0,
            RelativeImagePath TEXT NOT NULL,
            RelativeThumbnailPath TEXT NULL,
            Width INTEGER NOT NULL,
            Height INTEGER NOT NULL,
            Source TEXT NOT NULL
        );
        """;

    /// <summary>v1 索引语句（幂等，每次初始化都跑一遍，避免老库缺索引）。</summary>
    public static readonly string[] CreateIndexesV1 =
    {
        "CREATE INDEX IF NOT EXISTS IX_history_CreatedAtUtc ON history (CreatedAtUtc DESC);",
        "CREATE INDEX IF NOT EXISTS IX_history_IsFavorite ON history (IsFavorite);",
        "CREATE INDEX IF NOT EXISTS IX_history_Name ON history (Name);",
        "CREATE INDEX IF NOT EXISTS IX_history_Favorite_Created ON history (IsFavorite, CreatedAtUtc DESC);",
    };

    /// <summary>
    /// 迁移脚本：<c>Migrations[n]</c> 表示"从版本 n 升到 n+1"。本版只有 v1，
    /// 所以这里是空的；T25+ 加列时按注释说明追加。
    /// </summary>
    public static readonly IReadOnlyList<string> Migrations = Array.Empty<string>();

    /// <summary>
    /// 让数据库结构达到 <see cref="CurrentVersion"/>。幂等：重复调用不会丢数据、不会报错。
    /// </summary>
    /// <param name="connection">已打开的连接。方法内部自己开事务（调用方不要嵌套事务）。</param>
    /// <returns>初始化后的版本与是否需要迁移，供调用方写日志。</returns>
    public static HistorySchemaResult EnsureSchema(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var before = ReadUserVersion(connection);
        if (before > CurrentVersion)
        {
            return new HistorySchemaResult(before, CurrentVersion, Created: false, MigratedSteps: 0);
        }

        var isNewDatabase = before == 0 && !TableExists(connection);
        var migrated = 0;

        if (before < CurrentVersion)
        {
            using var transaction = connection.BeginTransaction();

            Execute(connection, transaction, CreateTableV1);
            foreach (var index in CreateIndexesV1)
            {
                Execute(connection, transaction, index);
            }

            for (var version = Math.Max(1, before); version < CurrentVersion; version++)
            {
                var sql = Migrations.Count >= version ? Migrations[version - 1] : null;
                if (string.IsNullOrWhiteSpace(sql))
                {
                    continue;
                }

                Execute(connection, transaction, sql);
                migrated++;
            }

            Execute(connection, transaction, $"PRAGMA user_version = {CurrentVersion};");
            transaction.Commit();
        }

        return new HistorySchemaResult(
            CurrentVersion,
            CurrentVersion,
            Created: isNewDatabase,
            MigratedSteps: migrated);
    }

    /// <summary>
    /// 打开一个连接并保证库结构已就绪。连接由调用方负责 Dispose。
    /// </summary>
    /// <remarks>
    /// 打开后立刻执行 <c>PRAGMA journal_mode=WAL</c>（写入时读者不阻塞，图库 UI 与
    /// 后台写入可以共存）和 <c>PRAGMA foreign_keys=ON</c>。
    /// <c>busy_timeout</c> 由连接字符串的 <c>Default Timeout</c> 与
    /// <see cref="HistoryRepositoryConnection.Open"/> 共同设置。
    /// </remarks>
    /// <param name="databasePath">数据库文件绝对路径。</param>
    public static SqliteConnection OpenAndEnsure(string databasePath)
    {
        var connection = HistoryRepositoryConnection.Open(databasePath);
        try
        {
            EnsureSchema(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>读取 <c>PRAGMA user_version</c>（没有设置过时为 0）。</summary>
    public static int ReadUserVersion(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        var value = command.ExecuteScalar();
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>表是否已存在（新库判断用）。</summary>
    public static bool TableExists(SqliteConnection connection)
    {
        ArgumentNullException.ThrowIfNull(connection);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(1) FROM sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", Table);
        var value = command.ExecuteScalar();
        return Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

/// <summary><see cref="HistorySchema.EnsureSchema"/> 的结果，供启动日志使用。</summary>
/// <param name="VersionBefore">初始化前读到的版本（0 = 新库或从来没有版本号）。</param>
/// <param name="VersionAfter">初始化后的版本。</param>
/// <param name="Created">本次是否新建了表（首次运行）。</param>
/// <param name="MigratedSteps">实际执行的迁移步数。</param>
public sealed record HistorySchemaResult(int VersionBefore, int VersionAfter, bool Created, int MigratedSteps)
{
    /// <summary>数据库版本高于程序支持的版本（用户装过更新的版本）。</summary>
    public bool IsNewerThanSupported => VersionBefore > VersionAfter;
}

/// <summary>
/// 连接字符串与打开逻辑集中处：所有仓储都从这里拿连接，避免各处参数不一致。
/// </summary>
public static class HistoryRepositoryConnection
{
    /// <summary>
    /// 构造连接字符串。<c>Mode=ReadWriteCreate</c> 让首次运行自动建库；
    /// <c>Cache=Private</c> 让每个连接看到自己的视图（WAL 下安全）。
    /// <c>Default Timeout=10</c> 秒：图库与后台写入争锁时不至于立刻抛 SQLITE_BUSY。
    /// </summary>
    public static string BuildConnectionString(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("数据库路径不能为空。", nameof(databasePath));
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            DefaultTimeout = 10,
        };

        return builder.ToString();
    }

    /// <summary>打开连接（不创建表；结构由 <see cref="HistorySchema.OpenAndEnsure"/> 保证）。</summary>
    public static SqliteConnection Open(string databasePath)
    {
        var connectionString = BuildConnectionString(databasePath);
        var connection = new SqliteConnection(connectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=10000;";
            command.ExecuteNonQuery();
        }

        return connection;
    }
}
