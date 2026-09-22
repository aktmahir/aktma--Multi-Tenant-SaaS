using MultiTenantSaaS.Application;
using Microsoft.AspNetCore.Mvc;

namespace MultiTenantSaaS.Api;

public sealed class TenantMiddleware(RequestDelegate next, ILogger<TenantMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, ITenantResolver resolver, ITenantContext tenantContext)
    {
        var resolution = await resolver.ResolveAsync(context, context.RequestAborted);
        if (resolution is null && context.Request.Path.StartsWithSegments("/api") && !context.Request.Path.StartsWithSegments("/api/health"))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new ProblemDetails { Title = "Tenant context is required", Status = 400 });
            return;
        }

        using (logger.BeginScope(new Dictionary<string, object?> { ["TenantId"] = tenantContext.TenantId }))
        {
            await next(context);
        }
    }
}