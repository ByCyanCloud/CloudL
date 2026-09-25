using Mapster;
using TemplateProject.Application.Contracts.Dtos;
using TemplateProject.Domain.Entities;

namespace TemplateProject.Application.Mappings;

/// <summary>
/// 业务对象映射注册。
/// 框架在 <c>AddCloudLCore(typeof(UserMappingRegister).Assembly)</c> 时会扫描本程序集内的
/// <see cref="IRegister"/> 实现，因此新增映射只需在此集中声明，无需手工调用。
/// </summary>
public sealed class UserMappingRegister : IRegister
{
    /// <inheritdoc />
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<User, UserDto>()
            .Map(destination => destination.Roles, source => source.RoleCodes.ToList());
    }
}
