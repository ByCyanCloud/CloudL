using System.Collections.Generic;
using System;

namespace CloudL.SqlSugar;

/// <summary>
/// CloudL.SqlSugar 的配置项。
/// </summary>
public sealed class SqlSugarOptions
{
    /// <summary>连接字符串。</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 数据库类型，对应 <c>SqlSugar.DbType</c> 的枚举名，默认 <c>Dm</c>（达梦）。
    /// 也接受常见别名：<c>Npgsql</c> → <c>PostgreSQL</c>、<c>MSSQL</c> → <c>SqlServer</c>、<c>MySQL</c> → <c>MySql</c>。
    /// </summary>
    public string DbType { get; set; } = "Dm";

    /// <summary>
    /// 是否在启动时自动建表/加列（<c>CodeFirst.InitTables</c>）。
    /// </summary>
    /// <remarks>
    /// <strong>仅供开发期使用</strong>：它没有版本链、无法审查生成的 DDL，改列/删列风险高。
    /// 生产环境请用版本化 SQL 脚本 + 迁移记录表（方案 C，见 CONTRACT.md）。
    /// </remarks>
    public bool EnableInitTables { get; set; }

    /// <summary>
    /// 需要自动建表的实体类型（仅在 <see cref="EnableInitTables"/> 为 true 时使用）。
    /// <strong>开启却留空会启动即失败</strong> —— 那样什么表都不会建，属于静默失效。
    /// </summary>
    public IList<Type> InitTablesEntityTypes { get; } = new List<Type>();

    /// <summary>是否输出 SqlSugar 的 SQL 日志（通常仅开发期开启）。</summary>
    public bool EnableSqlLog { get; set; }
}
