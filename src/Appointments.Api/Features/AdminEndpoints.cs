using Appointments.Api.Auth;
using Appointments.Api.Data;
using Appointments.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Appointments.Api.Features;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin").WithTags("Admin").RequireAuthorization(Policies.Admin);

        group.MapPost("/slots", GenerateSlots);

        return app;
    }

    /// <summary>
    /// Opens a day of slots for one service at one branch. Safe to call twice: slots that already exist are skipped
    /// (and the unique index on branch, service and start time backs that up).
    /// </summary>
    private static async Task<GenerateSlotsResponse> GenerateSlots(
        GenerateSlotsRequest request, AppointmentsDb db, TimeProvider clock, CancellationToken ct)
    {
        if (request.Closes <= request.Opens)
        {
            throw DomainException.Invalid("invalid_hours", "Closing time must be after opening time.");
        }

        if (request.Date < BookingRules.LocalDate(clock.GetUtcNow()))
        {
            throw DomainException.Invalid("date_in_past", "Slots can't be opened for a past date.");
        }

        if (!await db.Branches.AnyAsync(b => b.Id == request.BranchId, ct))
        {
            throw DomainException.NotFound("branch_not_found", "Branch not found.");
        }

        var service = await db.ServiceTypes.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.ServiceTypeId, ct)
            ?? throw DomainException.NotFound("service_not_found", "Service not found.");

        var (dayStart, dayEnd) = BookingRules.LocalDay(request.Date);
        var existing = await db.Slots.AsNoTracking()
            .Where(s => s.BranchId == request.BranchId
                && s.ServiceTypeId == request.ServiceTypeId
                && s.StartsAt >= dayStart
                && s.StartsAt < dayEnd)
            .Select(s => s.StartsAt)
            .ToListAsync(ct);
        var taken = existing.Select(s => s.UtcDateTime).ToHashSet();

        var step = TimeSpan.FromMinutes(service.DurationMinutes);
        var start = new DateTimeOffset(request.Date.ToDateTime(request.Opens), BookingRules.LocalOffset);
        var close = new DateTimeOffset(request.Date.ToDateTime(request.Closes), BookingRules.LocalOffset);

        int created = 0, skipped = 0;
        for (var at = start; at + step <= close; at += step)
        {
            if (taken.Contains(at.UtcDateTime))
            {
                skipped++;
                continue;
            }

            db.Slots.Add(new Slot(request.BranchId, request.ServiceTypeId, at, service.DurationMinutes, request.CapacityPerSlot));
            created++;
        }

        await db.SaveChangesAsync(ct);
        return new GenerateSlotsResponse(created, skipped);
    }
}
