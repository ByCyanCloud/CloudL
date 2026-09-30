using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SqlSugar;

namespace CloudL.SqlSugar;

/// <summary>
/// 开发期自动建表：当 <see cref="SqlSugarOptions.EnableInitTables"/> 为 <c>true</c> 时，
/// 在宿主启动时对 <see cref="SqlSugarOptions.InitTablesEntityTypes"/> 列出的实体执行
/// <c>CodeFirst.InitTables</c>。
/// </summary>
/// <remarks>
/// <strong>仅供开发期</strong>：它没有版本链、无法审查生成的 DDL，改列/删列风险高。
/// 生产环境请用版本化 SQL 脚本（见 <see cref="SqlSugarMigrationRunner"/>）。
/// </remarks>
internal sealed class SqlSugarInitTablesHostedService : IHostedService
{
    private readonly ISqlSugarClient _client;
    private readonly SqlSugarOptions _options;

    public SqlSugarInitTablesHostedService(ISqlSugarClient client, SqlSugarOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        _client = client;
        _options = options;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var types = _options.InitTablesEntityTypes.Distinct().ToArray();

        if (types.Length == 0)
        {
            // 模块注册时已经 fail-fast；这里只是兜底，避免将来有人绕过那条校验
            return Task.CompletedTask;
        }

        _client.CodeFirst.InitTables(types);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
