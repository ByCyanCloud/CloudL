using System.Reflection;
using Mapster;

namespace Livia.Application.Mapping;

/// <summary>
/// Mapster 全局约定。
/// 框架只负责全局设置；业务映射由业务程序集中的 <see cref="IRegister"/> 实现描述，
/// 通过 <c>AddLiviaCore(typeof(Program).Assembly)</c> 传入程序集即可被扫描。
/// </summary>
public static class MapsterFrameworkConfig
{
    private static readonly object SyncRoot = new();
    private static bool _configured;

    /// <summary>应用框架级 Mapster 约定，并扫描业务映射注册器。</summary>
    /// <param name="mappingAssemblies">包含 <see cref="IRegister"/> 实现的业务程序集。</param>
    public static void Configure(params Assembly[]? mappingAssemblies)
    {
        lock (SyncRoot)
        {
            if (!_configured)
            {
                TypeAdapterConfig.GlobalSettings.Default.IgnoreNullValues(true);
                _configured = true;
            }

            var assemblies = (mappingAssemblies ?? [])
                .Where(static assembly => assembly is not null)
                .Distinct()
                .ToArray();

            if (assemblies.Length > 0)
            {
                TypeAdapterConfig.GlobalSettings.Scan(assemblies);
            }
        }
    }
}