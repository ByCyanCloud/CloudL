using System;
using System.Text.Json;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace CloudL.AspNetCore.HttpApi.Swagger;

/// <summary>
/// 把 Swagger 文档里的 query 参数名显示为 <strong>camelCase</strong>（小驼峰）。
/// </summary>
/// <remarks>
/// <para>query 参数的<strong>绑定是大小写不敏感的</strong>，所以文档显示成哪种形式并不影响能否调用；
/// 但 C# 的参数名与 DTO 属性名是 PascalCase，若不处理，Swagger UI 会显示 <c>PageIndex</c>，
/// 与对外约定（camelCase）不一致 —— 使用者照着文档拼参数时会困惑。</para>
/// <para>只改 <c>In == Query</c> 的参数；请求体/响应体的 JSON 命名不受影响（仍为 snake_case）。</para>
/// <para><strong>注意</strong>：Microsoft.OpenApi 2.x 起 <c>IOpenApiParameter.Name</c> 是<strong>只读</strong>的，
/// 因此这里不能直接赋值，而是<strong>替换集合中的元素</strong>（复制原有属性、只改名字）。</para>
/// </remarks>
public sealed class CamelCaseQueryParameterOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(context);

        var parameters = operation.Parameters;

        if (parameters is null)
            return;

        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];

            if (parameter.In != ParameterLocation.Query || string.IsNullOrEmpty(parameter.Name))
                continue;

            var camelCaseName = JsonNamingPolicy.CamelCase.ConvertName(parameter.Name);

            if (string.Equals(camelCaseName, parameter.Name, StringComparison.Ordinal))
                continue;

            parameters[index] = new OpenApiParameter
            {
                Name = camelCaseName,
                In = parameter.In,
                Description = parameter.Description,
                Required = parameter.Required,
                Deprecated = parameter.Deprecated,
                Schema = parameter.Schema,
                Style = parameter.Style,
                Explode = parameter.Explode,
                AllowEmptyValue = parameter.AllowEmptyValue,
                Example = parameter.Example,
                Examples = parameter.Examples
            };
        }
    }
}
