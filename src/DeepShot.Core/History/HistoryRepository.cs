using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace DeepShot.Core.History;

/// <summary>
/// 历史记录的一句话构建：默认名称 + 相对路径 + 时间戳（T23）。
/// </summary>
/// <remarks>
/// 存储层只认 <see cref="HistoryRecord"/>；调用方（T24 的 sink）用这个工厂
/// 把"刚截了一张图"变成一条记录，不必自己拼路径与名称。
/// </remarks>
public static class HistoryRecordFactory
{
    /// <summary>
    /// 为一条新记录生成元数据（**不写文件、不访问数据库**）。
    /// </summary>
    /// <param name="name">显示名称；为空时按来源生成默认名称。</param>
    /// <param name="createdAtLocal">创建时间（本地时间，同时用于默认名称与文件名）。</param>
    /// <param name="width">图片宽度（物理像素）。</param>
    /// <param name="height">图片高度（物理像素）。</param>
    /// <param name="source">来源。</param>
    public static HistoryRecord Create(
        string? name,
        DateTime createdAtLocal,
        int width,
        int height,
        HistorySource source)
    {
        var id = Guid.NewGuid();
        var displayName = string.IsNullOrWhiteSpace(name)
            ? HistoryName.CreateDefaultFor(source, createdAtLocal)
            : HistoryName.Sanitize(name);

        var imagePath = HistoryPaths.CreateImageRelativePath(createdAtLocal, id);
        var createdUtc = HistoryRecord.NormalizeUtc(createdAtLocal);

        return new HistoryRecord(
            id,
            displayName,
            createdUtc,
            createdUtc,
            isFavorite: false,
            imagePath,
            HistoryPaths.CreateThumbnailRelativePath(imagePath),
            width,
            height,
            source);
    }
}

/// <summary>
/// 历史存储层（T23）：SQLite 元数据 + <c>images/</c> 文件 + <c>thumbs/</c> 缩略图。
/// </summary>
/// <remarks>
/// <para>
/// <b>职责边界</b>：本类只管"把字节写进文件、把元数据写进库、按条件读回来"。
/// **缩略图的像素处理在 App 层**（WPF 有图像编解码），这里只接收已编码好的字节；
/// 这样存储层可以在没有桌面的单元测试里跑（用任意字节即可）。
/// </para>
/// <para>
/// <b>文件句柄</b>：所有写文件都用 <see cref="FileShare.None"/> 的短生命周期
/// <see cref="FileStream"/> 并在 <c>using</c> 里关闭；读文件一次性读成字节数组再返回，
/// **不返回长期持有的流或 <c>BitmapImage</c>**。这样记录写完后图片文件可以被删除/覆盖，
/// 不会出现"历史里的图被锁住"（PLAN：读图注意释放句柄）。
/// </para>
/// <para>
/// <b>失败语义</b>：文件写成功但入库失败时，尽量把刚写的文件删掉（避免孤儿文件）；
/// 删除失败只记在异常信息里，不让主操作失败。所有异常都抛
/// <see cref="HistoryStoreException"/>，调用方（T24 的 sink）捕获后只写日志。
/// </para>
/// <para>
/// <b>线程</b>：每次操作自己开一条连接（SQLite + WAL，10 秒 busy_timeout），
/// 因此可以从 UI 线程与后台线程并发调用；同一个 <see cref="HistoryRepository"/>
/// 实例可跨线程复用（内部不共享连接）。
/// </para>
/// </remarks>
public sealed class HistoryRepository
{
    /// <summary>数据库文件名。</summary>
    public const string DatabaseFileName = "history.db";

    /// <summary>
    /// 用数据根目录构造（<c>%LOCALAPPDATA%\DeepShot</c>）；数据库固定为
    /// <c>&lt;root&gt;/history.db</c>。目录不存在时构造会创建它（含 images/thumbs）。
    /// </summary>
    public HistoryRepository(string dataRoot)
        : this(dataRoot, Path.Combine(dataRoot, DatabaseFileName))
    {
    }

    /// <summary>
    /// 用显式数据库路径构造（测试用临时目录时最方便）。
    /// </summary>
    /// <param name="dataRoot">图片/缩略图所在的数据根目录。</param>
    /// <param name="databasePath">数据库文件路径。</param>
    public HistoryRepository(string dataRoot, string databasePath)
    {
        if (string.IsNullOrWhiteSpace(dataRoot))
        {
            throw new ArgumentException("数据根目录不能为空。", nameof(dataRoot));
        }

        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("数据库路径不能为空。", nameof(databasePath));
        }

        DataRoot = Path.GetFullPath(dataRoot);
        DatabasePath = Path.GetFullPath(databasePath);
    }

    /// <summary>数据根目录（图片与缩略图的父目录）。</summary>
    public string DataRoot { get; }

    /// <summary>数据库文件完整路径。</summary>
    public string DatabasePath { get; }

    /// <summary>图片目录完整路径。</summary>
    public string ImagesDirectory => Path.Combine(DataRoot, HistoryPaths.ImagesFolder);

    /// <summary>缩略图目录完整路径。</summary>
    public string ThumbsDirectory => Path.Combine(DataRoot, HistoryPaths.ThumbsFolder);

    /// <summary>
    /// 创建目录并保证库结构就绪（首次运行建库建表；已有库做版本检查）。
    /// 应用启动时调用一次；重复调用是幂等的。
    /// </summary>
    /// <returns>版本与迁移信息，供启动日志记录。</returns>
    public HistorySchemaResult Initialize()
    {
        try
        {
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(ImagesDirectory);
            Directory.CreateDirectory(ThumbsDirectory);

            using var connection = HistoryRepositoryConnection.Open(DatabasePath);
            return HistorySchema.EnsureSchema(connection);
        }
        catch (Exception ex) when (ex is not HistoryStoreException)
        {
            throw new HistoryStoreException($"初始化历史库失败：{ex.Message}", ex);
        }
    }

    /// <summary>库结构版本（读 <c>PRAGMA user_version</c>）。</summary>
    public int GetSchemaVersion()
    {
        using var connection = Open();
        try
        {
            return HistorySchema.ReadUserVersion(connection);
        }
        catch (Exception ex) when (ex is not HistoryStoreException)
        {
            throw new HistoryStoreException($"读取历史库版本失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 新增一条记录：先写图片（可选缩略图）文件，再插元数据。
    /// </summary>
    /// <param name="record">记录元数据（相对路径会被占用检查顺延）。</param>
    /// <param name="imageBytes">PNG 字节（历史内部固定 PNG）。</param>
    /// <param name="thumbnailBytes">缩略图字节；null 表示不生成缩略图（记录里缩略图路径置空）。</param>
    /// <returns>真正入库的记录（可能因为文件名被占用而换了相对路径）。</returns>
    public HistoryRecord Add(HistoryRecord record, byte[] imageBytes, byte[]? thumbnailBytes = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0)
        {
            throw new HistoryStoreException("图片字节为空，未写入历史。");
        }

        Directory.CreateDirectory(ImagesDirectory);

        var stored = record;
        var writtenImagePath = string.Empty;
        var writtenThumbPath = string.Empty;

        try
        {
            var relativeImagePath = HistoryPaths.EnsureUniqueRelativePath(
                record.RelativeImagePath,
                candidate => File.Exists(HistoryPaths.ToAbsolute(DataRoot, candidate)));
            var relativeThumbPath = thumbnailBytes is { Length: > 0 }
                ? HistoryPaths.EnsureUniqueRelativePath(
                    record.RelativeThumbnailPath ?? HistoryPaths.CreateThumbnailRelativePath(relativeImagePath),
                    candidate => File.Exists(HistoryPaths.ToAbsolute(DataRoot, candidate)))
                : null;

            writtenImagePath = HistoryPaths.ToAbsolute(DataRoot, relativeImagePath);
            WriteAllBytes(writtenImagePath, imageBytes);

            if (relativeThumbPath is not null)
            {
                Directory.CreateDirectory(ThumbsDirectory);
                writtenThumbPath = HistoryPaths.ToAbsolute(DataRoot, relativeThumbPath);
                WriteAllBytes(writtenThumbPath, thumbnailBytes!);
            }

            stored = record with
            {
                RelativeImagePath = relativeImagePath,
                RelativeThumbnailPath = relativeThumbPath,
            };

            using var connection = Open();
            using var transaction = connection.BeginTransaction();
            Insert(connection, transaction, stored);
            transaction.Commit();

            return stored;
        }
        catch (Exception ex)
        {
            DeleteQuietly(writtenImagePath);
            DeleteQuietly(writtenThumbPath);
            throw ex as HistoryStoreException ?? new HistoryStoreException($"写入历史失败：{ex.Message}", ex);
        }
    }

    /// <summary>按 Id 读一条记录；不存在返回 null。</summary>
    public HistoryRecord? GetById(Guid id)
    {
        using var connection = Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT {SelectColumns} FROM history WHERE Id = $id LIMIT 1;";
            command.Parameters.AddWithValue("$id", FormatId(id));

            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadRecord(reader) : null;
        }
        catch (Exception ex) when (ex is not HistoryStoreException)
        {
            throw new HistoryStoreException($"读取历史记录失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 按查询条件读一页（名称子串搜索、收藏过滤、排序、分页）。
    /// 返回本页记录与过滤后的总数（总数用于图库显示"共 N 条"）。
    /// </summary>
    public HistoryPage GetList(HistoryQuery? query = null)
    {
        var effective = query ?? HistoryQuery.Default;

        using var connection = Open();
        try
        {
            using var transaction = connection.BeginTransaction();

            var (where, parameters) = BuildWhere(effective);

            var total = 0;
            using (var countCommand = connection.CreateCommand())
            {
                countCommand.Transaction = transaction;
                countCommand.CommandText = "SELECT COUNT(1) FROM history" + where + ";";
                foreach (var (name, value) in parameters)
                {
                    countCommand.Parameters.AddWithValue(name, value);
                }

                total = Convert.ToInt32(countCommand.ExecuteScalar(), CultureInfo.InvariantCulture);
            }

            var items = new List<HistoryRecord>();
            if (total > 0)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText =
                    $"SELECT {SelectColumns} FROM history{where} " +
                    $"ORDER BY {OrderBy(effective.Sort)} LIMIT $limit OFFSET $offset;";
                foreach (var (name, value) in parameters)
                {
                    command.Parameters.AddWithValue(name, value);
                }

                command.Parameters.AddWithValue("$limit", effective.NormalizedLimit);
                command.Parameters.AddWithValue("$offset", effective.NormalizedOffset);

                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    items.Add(ReadRecord(reader));
                }
            }

            transaction.Commit();
            return new HistoryPage(items, total, effective);
        }
        catch (Exception ex) when (ex is not HistoryStoreException)
        {
            throw new HistoryStoreException($"查询历史列表失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 日期索引（图库右侧时间轴 / 日期跳转用）：按**本地日期**汇总当前过滤条件下的记录。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>过滤 / 排序 / 分页</b>：日期索引只尊重 <paramref name="query"/> 里的搜索与收藏过滤
    /// （与 <see cref="GetList"/> 用同一个 <see cref="BuildWhere"/>），**忽略排序与分页** ——
    /// 无论界面怎么排序，"跳到某天"的偏移始终对应创建时间倒序。
    /// </para>
    /// <para>
    /// <b>为什么不在 SQL 里按日期分组</b>：<c>CreatedAtUtc</c> 是 ISO-8601 UTC 文本，
    /// <c>date()</c> 只能给 UTC 日期，而图库按本地日期分组（与
    /// <see cref="HistoryGrouping.GroupByDay"/> 一致）。SQL 只按
    /// <c>CreatedAtUtc DESC, Id DESC</c> 把时间列流式吐出来，本地日期与偏移在 C# 里算。
    /// </para>
    /// <para>
    /// <b>不物化整表</b>：只读 <c>CreatedAtUtc</c> 一列并逐行喂给构建器，内存里每天只剩
    /// 一个（日期, 条数, 偏移），几千条记录的开销可以忽略；同步查询，调用方决定是否放到后台线程。
    /// </para>
    /// </remarks>
    /// <param name="query">查询条件（null = 默认视图：不过滤）；只有搜索/收藏过滤生效。</param>
    public HistoryDateIndex GetDateIndex(HistoryQuery? query = null)
    {
        var effective = query ?? HistoryQuery.Default;

        using var connection = Open();
        try
        {
            var (where, parameters) = BuildWhere(effective);

            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT CreatedAtUtc FROM history" + where + " ORDER BY CreatedAtUtc DESC, Id DESC;";
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            var builder = HistoryDateIndex.CreateBuilder();
            var rowIndex = 0;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (HistoryRecord.TryParseUtc(reader.IsDBNull(0) ? null : reader.GetString(0), out var createdAtUtc))
                {
                    // 行号 = 该记录在结果集里的位置；某天首次出现时的行号就是它的 Offset。
                    builder.AddRow(createdAtUtc, rowIndex);
                }

                // 时间列损坏的行仍占一个行号，后面的 Offset 不会因此前移。
                rowIndex++;
            }

            return builder.Build();
        }
        catch (Exception ex) when (ex is not HistoryStoreException)
        {
            throw new HistoryStoreException($"读取历史日期索引失败：{ex.Message}", ex);
        }
    }

    /// <summary>列出全部名称（T24 生成不重名的默认名称用）。</summary>
    public IReadOnlyList<string> GetAllNames()
    {
        using var connection = Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Name FROM history ORDER BY CreatedAtUtc DESC;";
            using var reader = command.ExecuteReader();

            var names = new List<string>();
            while (reader.Read())
            {
                names.Add(reader.GetString(0));
            }

            return names;
        }
        catch (Exception ex) when (ex is not HistoryStoreException)
        {
            throw new HistoryStoreException($"读取历史名称失败：{ex.Message}", ex);
        }
    }

    /// <summary>总记录数（图库/清理统计用）。</summary>
    public int Count()
    {
        using var connection = Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(1) FROM history;";
            return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is not HistoryStoreException)
        {
            throw new HistoryStoreException($"统计历史记录失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 更新记录的全部元数据列（名称/收藏/时间/路径/尺寸/来源）。图片文件本身不动；
    /// 覆盖编辑的图片替换走 <see cref="UpdateImage"/>（T28）。
    /// </summary>
    /// <returns>是否真的更新了一行（Id 不存在时 false）。</returns>
    public bool Update(HistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        using var connection = Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE history SET
                    Name = $name,
                    CreatedAtUtc = $created,
                    UpdatedAtUtc = $updated,
                    IsFavorite = $favorite,
                    RelativeImagePath = $image,
                    RelativeThumbnailPath = $thumb,
                    Width = $width,
                    Height = $height,
                    Source = $source
                WHERE Id = $id;
                """;
            AddRecordParameters(command, record);
            return command.ExecuteNonQuery() > 0;
        }
        catch (Exception ex) when (ex is not HistoryStoreException)
        {
            throw new HistoryStoreException($"更新历史记录失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 收藏开关（T26 用）。内部走 <see cref="Update"/>，返回更新后的记录。
    /// </summary>
    public HistoryRecord? SetFavorite(Guid id, bool favorite, DateTime updatedAtUtc)
    {
        var existing = GetById(id);
        if (existing is null)
        {
            return null;
        }

        var updated = existing.WithFavorite(favorite, updatedAtUtc);
        return Update(updated) ? updated : null;
    }

    /// <summary>
    /// 覆盖图片（T28 用）：先写临时文件 → 校验长度 → 替换旧文件 → 更新元数据。
    /// 写新文件失败时旧文件保持不动（PLAN：保存失败不先销毁原图）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>覆盖语义</b>（PLAN「旧标注已属于图片像素，只能追加」）：**保留** Id / 名称 /
    /// 创建时间 / 收藏状态 / 来源 / 相对路径，只换图片字节、缩略图、宽高与
    /// <see cref="HistoryRecord.UpdatedAtUtc"/>。**不生成新记录**，所以图库条数不变；
    /// 创建时间不变也意味着 30 天期限不会因为"又编辑了一次"而延长（T27 规则）。
    /// </para>
    /// <para>
    /// 替换顺序刻意如此：先把新图完整写到 <c>xxx.png.tmp</c> 并核对长度，再
    /// <see cref="File.Move(string, string, bool)"/> 覆盖正式文件，最后才写缩略图与行。
    /// 中途任何一步失败，旧图要么还在（写 tmp 失败），要么已被完整的新图替换（Move 之后），
    /// 不会出现"半个文件"。
    /// </para>
    /// <para>
    /// 记录在编辑期间被删掉时返回 <c>null</c>（不抛异常），调用方提示"记录已不存在"。
    /// </para>
    /// </remarks>
    /// <param name="id">记录 Id。</param>
    /// <param name="imageBytes">新的 PNG 字节。</param>
    /// <param name="width">新宽度。</param>
    /// <param name="height">新高度。</param>
    /// <param name="thumbnailBytes">新缩略图字节；null 表示沿用原缩略图路径（文件不动）。</param>
    /// <param name="updatedAtUtc">修改时间；null 表示取 <see cref="DateTime.UtcNow"/>（测试可注入）。</param>
    public HistoryRecord? UpdateImage(
        Guid id,
        byte[] imageBytes,
        int width,
        int height,
        byte[]? thumbnailBytes = null,
        DateTime? updatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        if (imageBytes.Length == 0)
        {
            throw new HistoryStoreException("新图片字节为空，放弃覆盖（原图未改动）。");
        }

        var existing = GetById(id);
        if (existing is null)
        {
            return null;
        }

        var imagePath = HistoryPaths.ToAbsolute(DataRoot, existing.RelativeImagePath);
        var temporaryPath = imagePath + ".tmp";

        try
        {
            WriteAllBytes(temporaryPath, imageBytes);
            if (new FileInfo(temporaryPath).Length != imageBytes.Length)
            {
                throw new HistoryStoreException("临时文件长度与预期不一致，放弃覆盖。");
            }

            // 到这一步为止旧图一直没动；Move 是替换语义，失败时旧图还在。
            File.Move(temporaryPath, imagePath, overwrite: true);

            string? thumbRelative = existing.RelativeThumbnailPath;
            if (thumbnailBytes is { Length: > 0 })
            {
                if (string.IsNullOrWhiteSpace(thumbRelative))
                {
                    thumbRelative = HistoryPaths.CreateThumbnailRelativePath(existing.RelativeImagePath);
                }

                Directory.CreateDirectory(ThumbsDirectory);
                WriteAllBytes(HistoryPaths.ToAbsolute(DataRoot, thumbRelative), thumbnailBytes);
            }

            var updated = existing.WithImage(
                existing.RelativeImagePath,
                thumbRelative,
                width,
                height,
                updatedAtUtc ?? DateTime.UtcNow);

            return Update(updated) ? updated : null;
        }
        catch (Exception ex)
        {
            DeleteQuietly(temporaryPath);
            throw ex as HistoryStoreException ?? new HistoryStoreException($"覆盖历史图片失败：{ex.Message}", ex);
        }
    }

    /// <summary>
    /// 删除一条记录及其图片/缩略图文件（T25 的删除入口）。
    /// 记录不存在返回 false；文件已不存在不算失败。
    /// </summary>
    public bool Delete(Guid id)
    {
        // T27 顺序修正：**先在同一个事务里读行 + 删行**，提交后再删文件。
        // 之前是"先单独 GetById（第二条连接）再开事务"，在 WAL 下多一条连接会改变快照，
        // 真实桌面实测出现过：行已经删掉、却判定"没删掉"从而没删文件（留下孤儿图片），
        // 返回给调用方的"行已删除=False"也与事实相反。这里读与删在同一事务内，结果必然一致。
        string? imagePath = null;
        string? thumbPath = null;
        bool removed;

        using var connection = Open();
        try
        {
            using var transaction = connection.BeginTransaction();

            using (var select = connection.CreateCommand())
            {
                select.Transaction = transaction;
                select.CommandText =
                    "SELECT RelativeImagePath, RelativeThumbnailPath FROM history WHERE Id = $id LIMIT 1;";
                select.Parameters.AddWithValue("$id", FormatId(id));
                using var reader = select.ExecuteReader();
                if (!reader.Read())
                {
                    transaction.Rollback();
                    return false;
                }

                imagePath = reader.GetString(0);
                thumbPath = reader.IsDBNull(1) ? null : reader.GetString(1);
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM history WHERE Id = $id;";
                command.Parameters.AddWithValue("$id", FormatId(id));
                removed = command.ExecuteNonQuery() > 0;
            }

            transaction.Commit();
        }
        catch (Exception ex) when (ex is not HistoryStoreException)
        {
            throw new HistoryStoreException($"删除历史记录失败：{ex.Message}", ex);
        }

        // 行已经删掉了，文件删不掉只是留了个孤儿，不影响数据正确性（DeleteQuietly 不抛）。
        if (removed)
        {
            if (!string.IsNullOrWhiteSpace(imagePath))
            {
                DeleteQuietly(HistoryPaths.ToAbsolute(DataRoot, imagePath));
            }

            if (!string.IsNullOrWhiteSpace(thumbPath))
            {
                DeleteQuietly(HistoryPaths.ToAbsolute(DataRoot, thumbPath));
            }
        }

        return removed;
    }

    /// <summary>
    /// 按保留策略清理过期记录（T27）：先按 <see cref="HistoryRetentionPolicy.SelectExpired"/>
    /// 选出 Id，再逐条走 <see cref="Delete(Guid)"/>（**同一条删除路径**：删行 + 图片 + 缩略图）。
    /// </summary>
    /// <remarks>
    /// <para>
    /// **只管应用内部文件**：删除的路径由记录里的相对路径决定，只可能落在
    /// <c>&lt;数据根&gt;/images</c> 与 <c>thumbs</c>；用户"另存为"到别处的文件不在库里，
    /// 永远不会被这里碰到（PLAN：自动清理不影响用户另存为的文件）。
    /// </para>
    /// <para>
    /// **文件已缺失也能清理**：<see cref="Delete(Guid)"/> 对不存在的文件视为成功，
    /// 只删数据库行，不抛异常。单条删除失败（例如库被别的进程锁住）只记进
    /// <see cref="HistoryRetentionResult.Failures"/>，继续处理后面的记录。
    /// </para>
    /// </remarks>
    /// <param name="nowUtc">"现在"（UTC）。</param>
    /// <param name="retentionDays">保留天数（默认 30）。</param>
    public HistoryRetentionResult PurgeExpired(
        DateTime nowUtc,
        int retentionDays = HistoryRetentionPolicy.DefaultRetentionDays)
    {
        var all = GetAll();
        var expiredIds = HistoryRetentionPolicy.SelectExpired(all, nowUtc, retentionDays);

        var deleted = new List<Guid>();
        var failures = new List<(Guid Id, string Reason)>();

        foreach (var id in expiredIds)
        {
            try
            {
                if (Delete(id))
                {
                    deleted.Add(id);
                }
                else
                {
                    // 并发场景：选完之后被别处删掉了 —— 结果一致，不算失败。
                    failures.Add((id, "记录已不存在"));
                }
            }
            catch (HistoryStoreException ex)
            {
                failures.Add((id, ex.Message));
            }
        }

        return new HistoryRetentionResult(
            TotalBefore: all.Count,
            ExpiredCount: expiredIds.Count,
            DeletedIds: deleted,
            Failures: failures,
            NowUtc: HistoryRecord.NormalizeUtc(nowUtc),
            RetentionDays: retentionDays);
    }

    /// <summary>读出全部记录（清理与统计用；按创建时间倒序）。</summary>
    public IReadOnlyList<HistoryRecord> GetAll()
    {
        using var connection = Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                $"SELECT {SelectColumns} FROM history ORDER BY CreatedAtUtc DESC, Id DESC;";
            using var reader = command.ExecuteReader();

            var records = new List<HistoryRecord>();
            while (reader.Read())
            {
                records.Add(ReadRecord(reader));
            }

            return records;
        }
        catch (Exception ex) when (ex is not HistoryStoreException)
        {
            throw new HistoryStoreException($"读取全部历史记录失败：{ex.Message}", ex);
        }
    }

    /// <summary>读取记录对应的图片字节；文件不存在时抛 <see cref="HistoryStoreException"/>。</summary>
    public byte[] ReadImageBytes(Guid id)
    {
        var record = GetById(id) ?? throw new HistoryStoreException($"历史记录 {id} 不存在。");
        return ReadFile(HistoryPaths.ToAbsolute(DataRoot, record.RelativeImagePath), "图片");
    }

    /// <summary>读取缩略图字节；没有缩略图时返回 null（记录仍可用）。</summary>
    public byte[]? ReadThumbnailBytes(Guid id)
    {
        var record = GetById(id);
        if (record is null || string.IsNullOrWhiteSpace(record.RelativeThumbnailPath))
        {
            return null;
        }

        var path = HistoryPaths.ToAbsolute(DataRoot, record.RelativeThumbnailPath);
        return File.Exists(path) ? ReadFile(path, "缩略图") : null;
    }

    /// <summary>图片文件的绝对路径（图库/贴图直接读文件时用；调用方负责释放句柄）。</summary>
    public string GetImageAbsolutePath(HistoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return HistoryPaths.ToAbsolute(DataRoot, record.RelativeImagePath);
    }

    public override string ToString() => $"HistoryRepository({DatabasePath})";

    // --- 内部实现 ---

    private const string SelectColumns =
        "Id, Name, CreatedAtUtc, UpdatedAtUtc, IsFavorite, RelativeImagePath, " +
        "RelativeThumbnailPath, Width, Height, Source";

    private SqliteConnection Open()
    {
        try
        {
            Directory.CreateDirectory(DataRoot);
            var connection = HistoryRepositoryConnection.Open(DatabasePath);

            // 每次开连接都确保结构就绪（幂等、只读 user_version 一次，开销可忽略）；
            // 这样即使数据库文件被删掉，下一次写入也能自愈。
            HistorySchema.EnsureSchema(connection);
            return connection;
        }
        catch (Exception ex) when (ex is not HistoryStoreException)
        {
            throw new HistoryStoreException($"打开历史库失败：{ex.Message}", ex);
        }
    }

    private static (string Where, List<(string Name, object Value)> Parameters) BuildWhere(HistoryQuery query)
    {
        var clauses = new List<string>();
        var parameters = new List<(string, object)>();

        if (query.NormalizedSearchText is { } keyword)
        {
            clauses.Add("Name LIKE $pattern ESCAPE '\\'");
            parameters.Add(("$pattern", HistoryQuery.BuildLikePattern(keyword)));
        }

        switch (query.FavoriteFilter)
        {
            case HistoryFavoriteFilter.FavoritesOnly:
                clauses.Add("IsFavorite = 1");
                break;

            case HistoryFavoriteFilter.NotFavorite:
                clauses.Add("IsFavorite = 0");
                break;
        }

        var where = clauses.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", clauses);
        return (where, parameters);
    }

    private static string OrderBy(HistorySort sort) => sort switch
    {
        HistorySort.CreatedAtAscending => "CreatedAtUtc ASC, Id ASC",
        HistorySort.NameAscending => "Name COLLATE NOCASE ASC, CreatedAtUtc DESC",
        _ => "CreatedAtUtc DESC, Id DESC",
    };

    private static void Insert(SqliteConnection connection, SqliteTransaction transaction, HistoryRecord record)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO history (
                Id, Name, CreatedAtUtc, UpdatedAtUtc, IsFavorite,
                RelativeImagePath, RelativeThumbnailPath, Width, Height, Source)
            VALUES (
                $id, $name, $created, $updated, $favorite,
                $image, $thumb, $width, $height, $source);
            """;
        AddRecordParameters(command, record);
        command.ExecuteNonQuery();
    }

    private static void AddRecordParameters(SqliteCommand command, HistoryRecord record)
    {
        command.Parameters.AddWithValue("$id", FormatId(record.Id));
        command.Parameters.AddWithValue("$name", record.Name);
        command.Parameters.AddWithValue("$created", HistoryRecord.FormatUtc(record.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated", HistoryRecord.FormatUtc(record.UpdatedAtUtc));
        command.Parameters.AddWithValue("$favorite", record.IsFavorite ? 1 : 0);
        command.Parameters.AddWithValue("$image", record.RelativeImagePath);
        command.Parameters.AddWithValue(
            "$thumb",
            (object?)record.RelativeThumbnailPath ?? DBNull.Value);
        command.Parameters.AddWithValue("$width", record.Width);
        command.Parameters.AddWithValue("$height", record.Height);
        command.Parameters.AddWithValue("$source", HistorySourceText.ToStorageValue(record.Source));
    }

    private static HistoryRecord ReadRecord(SqliteDataReader reader)
    {
        var id = Guid.TryParse(reader.GetString(0), out var parsed) ? parsed : Guid.Empty;
        var createdAt = HistoryRecord.TryParseUtc(reader.GetString(2), out var created)
            ? created
            : DateTime.UnixEpoch;
        var updatedAt = HistoryRecord.TryParseUtc(reader.GetString(3), out var updated)
            ? updated
            : createdAt;
        var source = HistorySourceText.TryParse(reader.GetString(9), out var parsedSource)
            ? parsedSource
            : HistorySource.Screenshot;

        return new HistoryRecord(
            id == Guid.Empty ? Guid.NewGuid() : id,
            reader.GetString(1),
            createdAt,
            updatedAt,
            reader.GetInt32(4) != 0,
            reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.GetInt32(7),
            reader.GetInt32(8),
            source);
    }

    private static string FormatId(Guid id) => id.ToString("D");

    private static void WriteAllBytes(string path, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // FileShare.None：写完即关闭句柄，这个文件随后可以被删除/覆盖（PLAN 要求）。
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush(flushToDisk: true);
    }

    private static byte[] ReadFile(string path, string what)
    {
        try
        {
            // 一次性读成字节数组，句柄立刻释放；不要返回流给 UI 长期持有。
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var memory = new MemoryStream((int)Math.Min(stream.Length, int.MaxValue));
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch (Exception ex)
        {
            throw new HistoryStoreException($"读取历史{what}失败：{ex.Message}", ex);
        }
    }

    private static void DeleteQuietly(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 清孤儿文件失败不影响主流程（数据库里已经没有指向它的记录了）。
        }
    }
}

/// <summary>
/// 一次 30 天清理的结果（T27）：给调用方写日志与查证用。
/// </summary>
/// <param name="TotalBefore">清理前库里记录总数。</param>
/// <param name="ExpiredCount">策略判定为过期的记录数（不含收藏）。</param>
/// <param name="DeletedIds">真正删掉的记录 Id。</param>
/// <param name="Failures">删除失败（或选完之后已不存在）的 Id 与原因。</param>
/// <param name="NowUtc">本次清理使用的"现在"。</param>
/// <param name="RetentionDays">本次使用的保留天数。</param>
public sealed record HistoryRetentionResult(
    int TotalBefore,
    int ExpiredCount,
    IReadOnlyList<Guid> DeletedIds,
    IReadOnlyList<(Guid Id, string Reason)> Failures,
    DateTime NowUtc,
    int RetentionDays)
{
    /// <summary>删掉的记录数。</summary>
    public int DeletedCount => DeletedIds.Count;

    /// <summary>是否什么都没删（没有过期记录，或全部都失败了）。</summary>
    public bool IsEmpty => DeletedIds.Count == 0 && Failures.Count == 0;

    /// <summary>清理后的记录数（按清理前总数推算，供日志用）。</summary>
    public int TotalAfter => Math.Max(0, TotalBefore - DeletedCount);

    public override string ToString() =>
        $"清理过期历史：保留={RetentionDays} 天 现在={HistoryRecord.FormatUtc(NowUtc)} " +
        $"清理前={TotalBefore} 过期={ExpiredCount} 已删除={DeletedCount} 失败={Failures.Count} " +
        $"清理后={TotalAfter}";
}

/// <summary>
/// 历史存储层的异常：调用方（T24 的 sink / 图库）捕获后只写日志并提示，
/// 绝不能因为历史失败而让复制/保存/贴图失败。
/// </summary>
public sealed class HistoryStoreException : Exception
{
    public HistoryStoreException(string message)
        : base(message)
    {
    }

    public HistoryStoreException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
