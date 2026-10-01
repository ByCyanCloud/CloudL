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
    /// 标识符是否自动转大写（SqlSugar 默认 <c>true</c>）。
    /// </summary>
    /// <remarks>
    /// <para>达梦实例 <c>CASE_SENSITIVE=1</c> 且库表为小写时<strong>必须关掉</strong>：否则 ORM 会去找
    /// <c>"ORGANIZATIONS"</c> 而库里是 <c>"organizations"</c>，报「无效的表或视图名」——
    /// 表现为 ORM 完全读不了表，而原生 SQL（自己写引号小写）却正常，很容易误判成「数据没同步」。</para>
    /// <para><strong>默认值保持 SqlSugar 的原默认值 <c>true</c></strong>，不在这里暗中改变行为：
    /// 是否转大写取决于目标库的实例参数（<c>CASE_SENSITIVE</c>）与建表时用的大小写，只有业务侧知道，
    /// 因此由业务侧显式关闭。</para>
    /// </remarks>
    public bool IsAutoToUpper { get; set; } = true;

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
