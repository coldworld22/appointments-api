using Appointments.Api.Domain;

namespace Appointments.Api.Features;

public sealed record BranchDto(int Id, string Name, string City);

public sealed record ServiceTypeDto(int Id, string Code, string Name, int DurationMinutes);

public sealed record SlotDto(long Id, DateTimeOffset StartsAt, DateTimeOffset EndsAt, int Available);

public sealed record BookAppointmentRequest(long SlotId);

public sealed record AppointmentDto(
    Guid Id,
    string Reference,
    long SlotId,
    int BranchId,
    int ServiceTypeId,
    DateTimeOffset StartsAt,
    AppointmentStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CheckedInAt,
    DateTimeOffset? CompletedAt)
{
    public static AppointmentDto From(Appointment a) =>
        new(a.Id, a.Reference, a.SlotId, a.BranchId, a.ServiceTypeId, a.StartsAt, a.Status, a.CreatedAt, a.CheckedInAt, a.CompletedAt);
}

/// <summary>Opens a day of slots: one every service duration from <c>Opens</c> until <c>Closes</c> (Saudi time).</summary>
public sealed record GenerateSlotsRequest(int BranchId, int ServiceTypeId, DateOnly Date, TimeOnly Opens, TimeOnly Closes, int CapacityPerSlot);

public sealed record GenerateSlotsResponse(int Created, int Skipped);
