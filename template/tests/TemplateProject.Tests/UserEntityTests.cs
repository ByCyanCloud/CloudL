using TemplateProject.Domain.Entities;
using TemplateProject.Domain.Events;
using TemplateProject.Domain.Shared.Enums;

namespace TemplateProject.Tests;

/// <summary>
/// 用户聚合根行为测试（不依赖数据库，纯领域逻辑）。
/// </summary>
public class UserEntityTests
{
    private static User CreateUser(string userName = "alice") =>
        new(
            code: userName,
            userName: userName,
            passwordHash: "pbkdf2-sha512$220000$AABB$CCDD",
            email: $"{userName}@example.com");

    [Fact]
    public void NewUser_ShouldBeActive_AndRaiseRegisteredEvent()
    {
        var user = CreateUser();

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Contains(user.DomainEvents, domainEvent => domainEvent is UserRegisteredEvent);
    }

    [Fact]
    public void AssignRole_ShouldNotCreateDuplicateAssociation()
    {
        var user = CreateUser();
        var role = new Role("admin", "管理员");

        user.AssignRole(role);
        user.AssignRole(role);

        Assert.Single(user.UserRoles);
        Assert.Contains("admin", user.RoleCodes);
    }

    [Fact]
    public void RemoveRole_ShouldDropAssociation()
    {
        var user = CreateUser();
        var role = new Role("admin", "管理员");
        user.AssignRole(role);

        user.RemoveRole(role.Id);

        Assert.Empty(user.UserRoles);
    }

    [Fact]
    public void UpdateProfile_ShouldOnlyChangeProvidedFields()
    {
        var user = CreateUser();

        user.UpdateProfile(email: null, phoneNumber: "13800000000");

        Assert.Equal("alice@example.com", user.Email);
        Assert.Equal("13800000000", user.PhoneNumber);
        Assert.NotNull(user.UpdatedAt);
    }

    [Fact]
    public void UpdatePasswordHash_ShouldReplaceHashAndMarkUpdated()
    {
        var user = CreateUser();

        user.UpdatePasswordHash("pbkdf2-sha512$220000$EEFF$0011");

        Assert.Equal("pbkdf2-sha512$220000$EEFF$0011", user.PasswordHash);
        Assert.NotNull(user.UpdatedAt);
    }

    [Fact]
    public void TwoEntitiesWithSameId_ShouldBeEqual()
    {
        var id = Guid.NewGuid();
        var first = new SampleEntity(id);
        var second = new SampleEntity(id);

        Assert.Equal(first, second);
        Assert.True(first == second);
    }

    private sealed class SampleEntity : Livia.Domain.Entities.Entity<Guid>
    {
        public SampleEntity(Guid id)
            : base(id)
        {
        }
    }
}
