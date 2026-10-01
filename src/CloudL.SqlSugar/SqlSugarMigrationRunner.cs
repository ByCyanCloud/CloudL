using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudL.Domain.Shared.Time;
using SqlSugar;

namespace CloudL.SqlSugar;

/// <summary>
/// 版本化 SQL 脚本的迁移执行器（<strong>方案 C 的生产侧</strong>）。
/// </summary>
/// <remarks>
/// <para>SqlSugar 没有 EF 那样的迁移版本链，因此这里用最朴素也最可控的方式：
/// <strong>版本化 SQL 脚本 + 一张历史表</strong>。脚本文件名即版本号（如 <c>0001_init.sql</c>），
/// 按文件名<strong>序数排序</strong>逐个执行；已在历史表里的版本会被跳过（<strong>可重复执行</strong>）。</para>
/// <para>开发期也可以用 <c>SqlSugarOptions.EnableInitTables</c> 自动建表，但生产请走本执行器 ——
/// 它可审查（脚本是人写的 SQL）、可回滚（脚本自己写补偿）、有版本链（历史表）。</para>
/// <para>每个脚本<strong>单独一个事务</strong>：失败即回滚该脚本，且不会留下历史记录。</para>
/// </remarks>
public class SqlSugarMigrationRunner
{
    private readonly ISqlSugarClient _client;

    /// <summary>构造迁移执行器。</summary>
    public SqlSugarMigrationRunner(ISqlSugarClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        _client = client;
    }

    /// <summary>
    /// 应用指定目录下所有尚未应用的 <c>*.sql</c> 脚本。
    /// </summary>
    /// <param name="scriptDirectory">脚本目录（不存在时抛异常，不静默跳过）。</param>
    /// <param name="cancellationToken">取消标记。</param>
    /// <returns>本次实际应用的版本（文件名，不含扩展名），按执行顺序。</returns>
    public async Task<IReadOnlyList<string>> MigrateAsync(
        string scriptDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scriptDirectory))
            throw new ArgumentException("脚本目录不能为空。", nameof(scriptDirectory));

        if (!Directory.Exists(scriptDirectory))
            throw new DirectoryNotFoundException($"迁移脚本目录不存在：{scriptDirectory}");

        await EnsureHistoryTableAsync().ConfigureAwait(false);

        var applied = await _client.Queryable<SchemaHistoryRow>()
            .Select(row => row.Version)
            .ToListAsync()
            .ConfigureAwait(false);

        var appliedSet = new HashSet<string>(applied, StringComparer.Ordinal);

        var scripts = Directory
            .GetFiles(scriptDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.Ordinal)
            .ToArray();

        var executed = new List<string>();

        foreach (var script in scripts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var version = Path.GetFileNameWithoutExtension(script);

            if (appliedSet.Contains(version))
                continue;

            var sql = await File.ReadAllTextAsync(script, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(sql))
                continue;

            await _client.Ado.BeginTranAsync().ConfigureAwait(false);

            try
            {
                await _client.Ado.ExecuteCommandAsync(sql).ConfigureAwait(false);

                await _client.Insertable(new SchemaHistoryRow
                {
                    Version = version,
                    // 与框架其它时间列同口径：墙上钟、Kind=Unspecified
                    AppliedAt = CloudLTime.Now()
                }).ExecuteCommandAsync().ConfigureAwait(false);

                await _client.Ado.CommitTranAsync().ConfigureAwait(false);
            }
            catch
            {
                await _client.Ado.RollbackTranAsync().ConfigureAwait(false);
                throw;
            }

            executed.Add(version);
        }

        return executed;
    }

    /// <summary>已应用的版本（升序）。</summary>
    public async Task<IReadOnlyList<string>> GetAppliedVersionsAsync()
    {
        await EnsureHistoryTableAsync().ConfigureAwait(false);

        var versions = await _client.Queryable<SchemaHistoryRow>()
            .Select(row => row.Version)
            .ToListAsync()
            .ConfigureAwait(false);

        return versions.OrderBy(version => version, StringComparer.Ordinal).ToArray();
    }

    /// <summary>确保历史表存在（幂等）。</summary>
    private Task EnsureHistoryTableAsync()
    {
        _client.CodeFirst.InitTables<SchemaHistoryRow>();

        return Task.CompletedTask;
    }
}

/// <summary>迁移历史表的一行。</summary>
[SugarTable(SchemaHistoryTableName)]
public sealed class SchemaHistoryRow
{
    /// <summary>历史表名。</summary>
    public const string SchemaHistoryTableName = "__cloudl_schema_history";

    /// <summary>版本列的最大长度（字符数）。</summary>
    /// <remarks>
    /// <strong>脚本文件名即版本号</strong>：版本号长度 = 文件名长度（如 <c>0001_init</c>），
    /// 长文件名不能被静默截断成默认的 <c>AppConstants.DefaultStringMaxLength</c>（256）——
    /// 截断后两个不同脚本会撞成同一个版本号，迁移记录直接失真。故显式放大到 512。
    /// </remarks>
    public const int VersionMaxLength = 512;

    /// <summary>版本号（脚本文件名，不含扩展名）。</summary>
    /// <remarks>
    /// <para><strong>长度必须写在这里</strong>：<c>SqlSugarModule.EntityService</c> 的列约定会把它
    /// 拼进完整类型串（达梦 <c>NVARCHAR2(512)</c>、PG <c>character varying(512)</c>…），
    /// 然后把 <c>column.Length</c> 归零；不写 <c>Length</c> 就只能是默认的 256。</para>
    /// <para><strong>归零是必须的</strong>：类型串自带括号时若还留着 <c>Length</c>，SqlSugar 建表会再拼一次 →
    /// <c>"VERSION" NVARCHAR2(512)(512)</c> → 达梦报「语法分析出错」，<strong>迁移执行器连自己的历史表都建不出来</strong>
    /// （方案 C 第一步即崩）。这条规则由 <c>EntityService</c> 统一兜住：
    /// 框架写下的类型串与作者 <c>ColumnDataType</c> 写下的类型串一视同仁。</para>
    /// </remarks>
    [SugarColumn(IsPrimaryKey = true, Length = VersionMaxLength)]
    public string Version { get; set; } = string.Empty;

    /// <summary>应用时间（墙上钟，<c>Kind=Unspecified</c>）。</summary>
    public DateTime AppliedAt { get; set; }
}
