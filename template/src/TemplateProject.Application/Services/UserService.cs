using CloudL.Application.Contracts.Dtos;
using CloudL.Application.Contracts.IServices;
using CloudL.Domain.Repositories;
using CloudL.Domain.Shared.Constants;
using CloudL.Domain.Shared.Exceptions;
using Mapster;
using Microsoft.Extensions.Logging;
using TemplateProject.Application.Contracts.Dtos;
using TemplateProject.Application.Contracts.IServices;
using TemplateProject.Domain.Entities;
using TemplateProject.Domain.Repositories;
using TemplateProject.Domain.Shared.Enums;

namespace TemplateProject.Application.Services;

/// <summary>
/// 用户服务实现（示例用例编排）。
/// 依赖的都是框架提供的契约：仓储、工作单元、密码哈希、JWT 与刷新令牌存储。
/// </summary>
public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IRepository<Role, Guid> _roleRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IRefreshTokenStore _refreshTokenStore;
    private readonly ILoginAttemptGuard _loginAttemptGuard;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository userRepository,
        IRepository<Role, Guid> roleRepository,
        IUnitOfWork unitOfWork,
        IJwtTokenService jwtTokenService,
        IPasswordHasher passwordHasher,
        IRefreshTokenStore refreshTokenStore,
        ILoginAttemptGuard loginAttemptGuard,
        ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _unitOfWork = unitOfWork;
        _jwtTokenService = jwtTokenService;
        _passwordHasher = passwordHasher;
        _refreshTokenStore = refreshTokenStore;
        _loginAttemptGuard = loginAttemptGuard;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UserDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdWithRolesAsync(id, cancellationToken).ConfigureAwait(false);
        return user?.Adapt<UserDto>();
    }

    /// <inheritdoc />
    public async Task<PagedResponseDto<UserDto>> GetPagedListAsync(
        PagedRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var pageIndex = Math.Max(request.PageIndex, 1);
        var pageSize = Math.Clamp(request.PageSize, 1, AppConstants.MaxPageSize);
        var (orderBy, descending) = ResolveSort(request);

        var paged = await _userRepository.GetPagedAsync(
            pageIndex,
            pageSize,
            orderBy: orderBy,
            descending: descending,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return new PagedResponseDto<UserDto>
        {
            Items = paged.Items.Adapt<List<UserDto>>(),
            TotalCount = paged.TotalCount,
            PageIndex = paged.PageIndex,
            PageSize = paged.PageSize
        };
    }

    /// <inheritdoc />
    public async Task<UserDto> CreateAsync(CreateUserDto input, CancellationToken cancellationToken = default)
    {
        if (await _userRepository.ExistsAsync(user => user.UserName == input.UserName, cancellationToken).ConfigureAwait(false))
        {
            throw new BusinessConflictException("用户名已存在");
        }

        if (!string.IsNullOrWhiteSpace(input.Email)
            && await _userRepository.ExistsAsync(user => user.Email == input.Email, cancellationToken).ConfigureAwait(false))
        {
            throw new BusinessConflictException("邮箱已被注册");
        }

        var user = new User(
            code: input.UserName,
            userName: input.UserName,
            passwordHash: _passwordHasher.Hash(input.Password),
            email: input.Email,
            phoneNumber: input.PhoneNumber);

        await AssignRolesAsync(user, input.Roles, cancellationToken).ConfigureAwait(false);

        await _userRepository.AddAsync(user, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await ReloadAsDtoAsync(user.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UserDto> UpdateAsync(
        Guid id,
        UpdateUserDto input,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("用户不存在");

        if (user.RowVersion != input.RowVersion)
        {
            throw new ConcurrencyConflictException("数据已被其他用户修改，请刷新后重试");
        }

        if (!string.IsNullOrWhiteSpace(input.Email)
            && !string.Equals(input.Email, user.Email, StringComparison.OrdinalIgnoreCase)
            && await _userRepository.ExistsAsync(
                item => item.Email == input.Email && item.Id != id,
                cancellationToken).ConfigureAwait(false))
        {
            throw new BusinessConflictException("邮箱已被其他用户使用");
        }

        user.UpdateProfile(input.Email, input.PhoneNumber);

        await _userRepository.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await ReloadAsDtoAsync(user.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("用户不存在");

        await _userRepository.DeleteAsync(user, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _refreshTokenStore.RevokeAllForUserAsync(id, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<LoginResponseDto> LoginAsync(
        LoginDto input,
        CancellationToken cancellationToken = default)
    {
        // 账号维度：锁定期内直接拒绝，连数据库都不必查
        _loginAttemptGuard.EnsureNotLocked(input.UserName);

        var user = await _userRepository
            .FindByUserNameWithRolesAsync(input.UserName, cancellationToken)
            .ConfigureAwait(false);

        // 用户不存在与密码错误返回同一提示，避免暴露账号是否存在
        if (user is null || !_passwordHasher.Verify(input.Password, user.PasswordHash))
        {
            _loginAttemptGuard.RecordFailure(input.UserName);
            _logger.LogWarning("登录失败: {UserName}", input.UserName);
            throw new UnauthorizedBusinessException(ErrorCodes.CredentialsError, "用户名或密码错误");
        }

        if (user.Status != UserStatus.Active)
        {
            throw new ForbiddenBusinessException("账号不可用，请联系管理员");
        }

        // 哈希参数偏弱时透明升级（例如框架提升了 PBKDF2 迭代次数）
        if (_passwordHasher.NeedsRehash(user.PasswordHash))
        {
            var reloaded = await _userRepository.GetByIdAsync(user.Id, cancellationToken).ConfigureAwait(false);
            if (reloaded is not null)
            {
                reloaded.UpdatePasswordHash(_passwordHasher.Hash(input.Password));
                await _userRepository.UpdateAsync(reloaded, cancellationToken).ConfigureAwait(false);
                await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                user = await _userRepository.GetByIdWithRolesAsync(user.Id, cancellationToken).ConfigureAwait(false) ?? user;
            }
        }

        _loginAttemptGuard.Reset(input.UserName);

        return await IssueTokensAsync(user, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<LoginResponseDto> RefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        var userId = await _refreshTokenStore.ConsumeAsync(refreshToken, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedBusinessException(ErrorCodes.Unauthorized, "Refresh Token 无效或已过期");

        var user = await _userRepository.GetByIdWithRolesAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new UnauthorizedBusinessException(ErrorCodes.Unauthorized, "用户不存在或已注销");

        if (user.Status != UserStatus.Active)
        {
            throw new ForbiddenBusinessException("账号不可用，请联系管理员");
        }

        return await IssueTokensAsync(user, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task LogoutAsync(string refreshToken, CancellationToken cancellationToken = default) =>
        _refreshTokenStore.RevokeAsync(refreshToken, cancellationToken);

    /// <inheritdoc />
    public async Task ChangePasswordAsync(
        Guid userId,
        ChangePasswordDto input,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("用户不存在");

        if (!_passwordHasher.Verify(input.CurrentPassword, user.PasswordHash))
        {
            throw new UnauthorizedBusinessException(ErrorCodes.CredentialsError, "当前密码不正确");
        }

        if (string.Equals(input.CurrentPassword, input.NewPassword, StringComparison.Ordinal))
        {
            throw new BusinessException(ErrorCodes.InvalidInput, "新密码不能与当前密码相同");
        }

        user.UpdatePasswordHash(_passwordHasher.Hash(input.NewPassword));

        await _userRepository.UpdateAsync(user, cancellationToken).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // 改密后强制下线：撤销该用户所有刷新令牌
        await _refreshTokenStore.RevokeAllForUserAsync(userId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<LoginResponseDto> IssueTokensAsync(User user, CancellationToken cancellationToken)
    {
        var accessToken = _jwtTokenService.GenerateAccessToken(
            user.Id,
            user.Code,
            user.UserName,
            user.RoleCodes,
            user.OrganizationCode);

        var refreshToken = _jwtTokenService.GenerateRefreshToken();

        await _refreshTokenStore.StoreAsync(
            refreshToken,
            user.Id,
            DateTimeOffset.UtcNow.AddDays(_jwtTokenService.RefreshTokenExpirationDays),
            cancellationToken).ConfigureAwait(false);

        return new LoginResponseDto
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(_jwtTokenService.AccessTokenExpirationHours),
            User = user.Adapt<UserDto>()
        };
    }

    private async Task AssignRolesAsync(
        User user,
        IReadOnlyCollection<string>? roleCodes,
        CancellationToken cancellationToken)
    {
        if (roleCodes is null || roleCodes.Count == 0)
            return;

        var distinctCodes = roleCodes
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (distinctCodes.Count == 0)
            return;

        var roles = await _roleRepository
            .FindAsync(role => distinctCodes.Contains(role.Code), cancellationToken)
            .ConfigureAwait(false);

        if (roles.Count != distinctCodes.Count)
        {
            var found = roles.Select(role => role.Code).ToHashSet(StringComparer.Ordinal);
            var missing = distinctCodes.Where(code => !found.Contains(code));
            throw new BusinessException(ErrorCodes.InvalidInput, $"角色不存在: {string.Join(", ", missing)}");
        }

        foreach (var role in roles)
        {
            user.AssignRole(role);
        }
    }

    private async Task<UserDto> ReloadAsDtoAsync(Guid userId, CancellationToken cancellationToken)
    {
        var reloaded = await _userRepository.GetByIdWithRolesAsync(userId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("用户不存在");

        return reloaded.Adapt<UserDto>();
    }

    private static (System.Linq.Expressions.Expression<Func<User, object>> OrderBy, bool Descending)
        ResolveSort(PagedRequestDto request)
    {
        var descending = !string.Equals(request.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        System.Linq.Expressions.Expression<Func<User, object>> orderBy =
            request.SortBy?.Trim().ToLowerInvariant() switch
            {
                "user_name" => user => user.UserName,
                "email" => user => user.Email!,
                "status" => user => user.Status,
                "created_at" => user => user.CreatedAt,
                _ => user => user.CreatedAt
            };

        return (orderBy, descending);
    }
}
