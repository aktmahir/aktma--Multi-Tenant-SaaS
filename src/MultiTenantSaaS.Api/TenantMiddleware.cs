using MultiTenantSaaS.Application;
using Microsoft.AspNetCore.Mvc;

namespace MultiTenantSaaS.Api;

public sealed class TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ITenantResolver resolver, ITenantContext tenantContext, ITenantMembershipService membershipService)
    {
        var resolution = await resolver.ResolveAsync(context, context.RequestAborted);
        var isTenantApiRequest = context.Request.Path.StartsWithSegments("/api")
            && !context.Request.Path.StartsWithSegments("/api/health")
            && !context.Request.Path.StartsWithSegments("/api/tenants")
            && !context.Request.Path.StartsWithSegments("/api/session");

        if (resolution is null && isTenantApiRequest)
        {
            var status = context.User.Identity?.IsAuthenticated == true
                ? StatusCodes.Status403Forbidden
                : StatusCodes.Status401Unauthorized;
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Title = status == StatusCodes.Status401Unauthorized
                    ? "Authentication is required"
                    : "A valid tenant context is required",
                Status = status
            });
            return;
        }

        if (resolution is not null && context.User.Identity?.IsAuthenticated == true)
        {
            var userId = context.User.GetUserId();
            var membershipRole = userId is not null
                ? await membershipService.GetRoleAsync(userId.Value, resolution.TenantId, context.RequestAborted)
                : null;

            if (!TenantClaimValidator.IsAuthorizedForTenant(context.User, resolution.TenantId, membershipRole))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new ProblemDetails
                {
                    Title = "The authenticated user is not authorized for this tenant",
                    Status = StatusCodes.Status403Forbidden
                });
                return;
            }
        }

        using (logger.BeginScope(new Dictionary<string, object?> { ["TenantId"] = tenantContext.TenantId }))
        {
            await next(context);
        }
    }
}