using System.Security.Claims;
using Appointments.Api.Auth;
using Appointments.Api.Data;
using Appointments.Api.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Appointments.Api.Features;

public static class AppointmentEndpoints
{
    public const string BookingRateLimit = "booking";

    public static IEndpointRouteBuilder MapAppointmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/appointments").WithTags("Appointments").RequireAuthorization();

        group.MapPost("", Book).RequireRateLimiting(BookingRateLimit);
        group.MapGet("/mine", Mine);
        group.MapGet("/{id:guid}", Get);
        group.MapPost("/{id:guid}/cancel", Cancel);
        group.MapPost("/{id:guid}/check-in", CheckIn).RequireAuthorization(Policies.Staff);
        group.MapPost("/{id:guid}/complete", Complete).RequireAuthorization(Policies.Staff);

        app.MapGet("/branches/{branchId:int}/queue", Queue)
            .WithTags("Counter")
            .RequireAuthorization(Policies.Staff);

        return app;
    }

    /// <summary>
    /// Takes a seat. Two citizens racing for the last seat both read Booked = Capacity - 1, but only one save matches the
    /// slot's rowversion; the other is retried, re-reads the full slot and gets 409 slot_full.
    /// </summary>
    private static async Task<Created<AppointmentDto>> Book(
        BookAppointmentRequest request, ClaimsPrincipal user, AppointmentsDb db, TimeProvider clock, CancellationToken ct)
    {
        var citizenId = user.GetUserId();

        var appointment = await db.WithConcurrencyRetry(async () =>
        {
            var now = clock.GetUtcNow();
            var slot = await db.Slots.FirstOrDefaultAsync(s => s.Id == request.SlotId, ct)
                ?? throw DomainException.NotFound("slot_not_found", "Slot not found.");

            // Same rule as Appointment.IsActiveAt, written as a query.
            var noShowCutoff = now - BookingRules.CheckInClosesAfter;
            var alreadyBooked = await db.Appointments.AnyAsync(
                a => a.CitizenId == citizenId
                    && a.ServiceTypeId == slot.ServiceTypeId
                    && (a.Status == AppointmentStatus.CheckedIn
                        || (a.Status == AppointmentStatus.Booked && a.StartsAt >= noShowCutoff)),
                ct);
            if (alreadyBooked)
            {
                throw DomainException.Conflict("already_booked", "You already have an active appointment for this service.");
            }

            var booked = Appointment.Book(slot, citizenId, now);
            db.Appointments.Add(booked);
            await db.SaveChangesAsync(ct);
            return booked;
        });

        return TypedResults.Created($"/appointments/{appointment.Id}", AppointmentDto.From(appointment));
    }

    private static async Task<List<AppointmentDto>> Mine(ClaimsPrincipal user, AppointmentsDb db, CancellationToken ct)
    {
        var citizenId = user.GetUserId();
        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.CitizenId == citizenId)
            .OrderByDescending(a => a.StartsAt)
            .Take(50)
            .ToListAsync(ct);
        return appointments.Select(AppointmentDto.From).ToList();
    }

    private static async Task<AppointmentDto> Get(Guid id, ClaimsPrincipal user, AppointmentsDb db, CancellationToken ct)
    {
        var appointment = await FindVisible(id, user, db, ct);
        return AppointmentDto.From(appointment);
    }

    /// <summary>The owner (or an admin) can cancel up to 2 hours before; the seat goes back on sale.</summary>
    private static async Task<AppointmentDto> Cancel(Guid id, ClaimsPrincipal user, AppointmentsDb db, TimeProvider clock, CancellationToken ct)
    {
        var cancelled = await db.WithConcurrencyRetry(async () =>
        {
            var appointment = await FindVisible(id, user, db, ct, tracked: true);
            if (appointment.CitizenId != user.GetUserId() && !user.IsInRole(Roles.Admin))
            {
                throw new DomainException("forbidden", "Only the citizen who booked can cancel.", StatusCodes.Status403Forbidden);
            }

            var slot = await db.Slots.SingleAsync(s => s.Id == appointment.SlotId, ct);
            appointment.Cancel(slot, clock.GetUtcNow());
            await db.SaveChangesAsync(ct);
            return appointment;
        });

        return AppointmentDto.From(cancelled);
    }

    private static async Task<AppointmentDto> CheckIn(Guid id, ClaimsPrincipal user, AppointmentsDb db, TimeProvider clock, CancellationToken ct)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw NotFound();
        appointment.CheckIn(user.GetUserId(), clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return AppointmentDto.From(appointment);
    }

    private static async Task<AppointmentDto> Complete(Guid id, AppointmentsDb db, TimeProvider clock, CancellationToken ct)
    {
        var appointment = await db.Appointments.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw NotFound();
        appointment.Complete(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return AppointmentDto.From(appointment);
    }

    /// <summary>Counter view: everyone expected at a branch on a day, in time order.</summary>
    private static async Task<List<AppointmentDto>> Queue(int branchId, DateOnly date, AppointmentsDb db, CancellationToken ct)
    {
        var (from, to) = BookingRules.LocalDay(date);
        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.BranchId == branchId
                && a.StartsAt >= from
                && a.StartsAt < to
                && a.Status != AppointmentStatus.Cancelled)
            .OrderBy(a => a.StartsAt)
            .ToListAsync(ct);
        return appointments.Select(AppointmentDto.From).ToList();
    }

    /// <summary>
    /// Citizens only see their own appointments; anyone else's ID answers 404, not 403,
    /// so appointment IDs can't be probed. Staff see all.
    /// </summary>
    private static async Task<Appointment> FindVisible(
        Guid id, ClaimsPrincipal user, AppointmentsDb db, CancellationToken ct, bool tracked = false)
    {
        var query = tracked ? db.Appointments : db.Appointments.AsNoTracking();
        var appointment = await query.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (appointment is null || (appointment.CitizenId != user.GetUserId() && !user.IsStaff()))
        {
            throw NotFound();
        }

        return appointment;
    }

    private static DomainException NotFound() => DomainException.NotFound("appointment_not_found", "Appointment not found.");
}
