using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Oes.Application.Abstractions;
using Oes.Application.Security;

namespace Oes.Web.Infrastructure;

/// <summary>
/// Declares the permission a page, handler or API action needs:
/// <c>[HasPermission(Permissions.UsersManage)]</c>. Resolved per request from the
/// user's roles in the current tenant. There are no role-name checks anywhere.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public const string Prefix = "perm:";

    public HasPermissionAttribute(string permissionKey)
    {
        if (!Permissions.IsKnown(permissionKey)) throw new ArgumentException($"Unknown permission '{permissionKey}'.");
        Policy = Prefix + permissionKey;
    }
}

public sealed record PermissionRequirement(string PermissionKey) : IAuthorizationRequirement;

public sealed class PermissionHandler(ICurrentUser user) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (await user.HasPermissionAsync(requirement.PermissionKey)) context.Succeed(requirement);
    }
}

/// <summary>Builds "perm:{key}" policies on demand so each permission doesn't need registering.</summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(HasPermissionAttribute.Prefix, StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(policyName[HasPermissionAttribute.Prefix.Length..]))
            .Build();
    }
}
