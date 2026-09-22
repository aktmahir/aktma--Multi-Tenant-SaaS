using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MultiTenantSaaS.Infrastructure;

namespace MultiTenantSaaS.Api;

public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/audit-log", async (
            string? action,
            int? page,
            int? pageSize,
            TenantDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            var currentPage = Math.Max(page ?? 1, 1);
            var currentPageSize = Math.Clamp(pageSize ?? 25, 1, 100);
            var query = dbContext.AuditEntries.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(action))
            {
                query = query.Where(entry => entry.Action == action.Trim());
            }

            var total = await query.CountAsync(cancellationToken);
            var entries = await query
                .OrderByDescending(entry => entry.OccurredAt)
                .Skip((currentPage - 1) * currentPageSize)
                .Take(currentPageSize)
                .Select(entry => new AuditEntryResponse(
                    entry.Id,
                    entry.OccurredAt,
                    entry.ActorId,
                    entry.Action,
                    entry.Resource,
                    entry.IpAddress))
                .ToListAsync(cancellationToken);

            return Results.Ok(new AuditLogResponse(currentPage, currentPageSize, total, entries));
        })
        .RequireAuthorization()
        .WithName("ListTenantAuditLog");

        return endpoints;
    }
}

public sealed record AuditLogResponse(int Page, int PageSize, int Total, IReadOnlyList<AuditEntryResponse> Entries);
public sealed record AuditEntryResponse(Guid Id, DateTimeOffset OccurredAt, Guid? ActorId, string Action, string Resource, string? IpAddress);