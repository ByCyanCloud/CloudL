using System.Text.Json;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CloudL.AspNetCore.HttpApi.Swagger;

/// <summary>
/// 把 Swagger 文档中的 query 参数名改写为 snake_case。
/// <para>否则 Swagger UI 的 "Try it out" 会按 camelCase 生成请求，与实际绑定约定不一致，
/// 调用方照文档发请求会静默拿到默认值。</para>
/// </summary>
public sealed class SnakeCaseQueryParameterOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (operation.Parameters is null || operation.Parameters.Count == 0)
            return;

        foreach (var parameter in operation.Parameters)
        {
            // 只改 query 参数；路径参数（如 {id}）与头部参数不动
            if (parameter.In != ParameterLocation.Query || string.IsNullOrEmpty(parameter.Name))
                continue;

            parameter.Name = JsonNamingPolicy.SnakeCaseLower.ConvertName(parameter.Name);
        }
    }
}
