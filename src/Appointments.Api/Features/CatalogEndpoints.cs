using Appointments.Api.Data;
using Appointments.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Appointments.Api.Features;

/// <summary>Read-only lookups a citizen needs to pick a slot: branches, services, open slots.</summary>
public static class CatalogEndpoints
{
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/branches", async (AppointmentsDb db, CancellationToken ct) =>
            await db.Branches.AsNoTracking()
                .OrderBy(b => b.Name)
                .Select(b => new BranchDto(b.Id, b.Name, b.City))
                .ToListAsync(ct))
            .WithTags("Catalog")
            .RequireAuthorization();

        app.MapGet("/services", async (AppointmentsDb db, CancellationToken ct) =>
            await db.ServiceTypes.AsNoTracking()
                .OrderBy(s => s.Name)
                .Select(s => new ServiceTypeDto(s.Id, s.Code, s.Name, s.DurationMinutes))
                .ToListAsync(ct))
            .WithTags("Catalog")
            .RequireAuthorization();

        // GET /branches/1/slots?serviceTypeId=1&date=2030-03-02 → slots that still have a free seat.
        app.MapGet("/branches/{branchId:int}/slots", async (
            int branchId, int serviceTypeId, DateOnly date, AppointmentsDb db, TimeProvider clock, CancellationToken ct) =>
        {
            var (from, to) = BookingRules.LocalDay(date);
            var earliest = clock.GetUtcNow() + BookingRules.MinimumNotice;

            return await db.Slots.AsNoTracking()
                .Where(s => s.BranchId == branchId
                    && s.ServiceTypeId == serviceTypeId
                    && s.StartsAt >= from
                    && s.StartsAt < to
                    && s.StartsAt >= earliest
                    && s.Booked < s.Capacity)
                .OrderBy(s => s.StartsAt)
                .Select(s => new SlotDto(s.Id, s.StartsAt, s.EndsAt, s.Capacity - s.Booked))
                .ToListAsync(ct);
        })
            .WithTags("Catalog")
            .RequireAuthorization();

        return app;
    }
}
