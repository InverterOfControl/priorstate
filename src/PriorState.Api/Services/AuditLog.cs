using System.Security.Claims;
using PriorState.Data;
using PriorState.Domain.Entities;

namespace PriorState.Api.Services;

/// <summary>
/// Writes to the access log.
///
/// Reads are recorded, not just writes. The process documentation asserts that access to the
/// archive is logged; if only modifications were recorded, that claim would be false in the one
/// direction that matters — who looked at what, and when.
/// </summary>
public sealed class AuditLog
{
    private readonly PriorStateDbContext _db;
    private readonly IHttpContextAccessor _httpContext;

    public AuditLog(PriorStateDbContext db, IHttpContextAccessor httpContext)
    {
        _db = db;
        _httpContext = httpContext;
    }

    public async Task RecordAsync(
        AuditAction action,
        string subjectType,
        string? subjectId = null,
        string? detail = null,
        CancellationToken cancellationToken = default)
    {
        var context = _httpContext.HttpContext;
        var user = context?.User;

        _db.AuditLog.Add(new AuditLogEntry
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Action = action,
            SubjectType = subjectType,
            SubjectId = subjectId,
            Detail = detail,
            UserId = user?.FindFirstValue(ClaimTypes.NameIdentifier),
            UserName = user?.Identity?.Name,
            RemoteAddress = context?.Connection.RemoteIpAddress?.ToString(),
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
