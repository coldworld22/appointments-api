using System.Security.Cryptography;

namespace Appointments.Api.Domain;

public enum AppointmentStatus
{
    Booked = 0,
    CheckedIn = 1,
    Completed = 2,
    Cancelled = 3,
}

/// <summary>
/// One citizen's booking of one seat in a slot. The life cycle is
/// <c>Booked → CheckedIn → Completed</c>, or <c>Booked → Cancelled</c>.
/// Every transition checks the time rules in <see cref="BookingRules"/>.
/// </summary>
public sealed class Appointment
{
    // Unambiguous characters only: no 0/O or 1/I, because people read references out at the counter.
    private const string ReferenceAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private Appointment()
    {
    }

    public Guid Id { get; private set; }

    /// <summary>Short code shown to the citizen and read out at the counter.</summary>
    public string Reference { get; private set; } = "";

    public long SlotId { get; private set; }

    public int BranchId { get; private set; }

    public int ServiceTypeId { get; private set; }

    /// <summary>The citizen's identity from the token (<c>oid</c> or <c>sub</c>).</summary>
    public string CitizenId { get; private set; } = "";

    public DateTimeOffset StartsAt { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public DateTimeOffset? CheckedInAt { get; private set; }

    public string? CheckedInBy { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public static Appointment Book(Slot slot, string citizenId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(citizenId);

        slot.Reserve(now);

        return new Appointment
        {
            Id = Guid.CreateVersion7(now),
            Reference = NewReference(),
            SlotId = slot.Id,
            BranchId = slot.BranchId,
            ServiceTypeId = slot.ServiceTypeId,
            CitizenId = citizenId,
            StartsAt = slot.StartsAt,
            Status = AppointmentStatus.Booked,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Still holds a seat: checked in, or booked and not yet past the check-in window.
    /// A booking whose window has closed is a no-show and no longer blocks a new booking.
    /// </summary>
    public bool IsActiveAt(DateTimeOffset now) =>
        Status == AppointmentStatus.CheckedIn
        || (Status == AppointmentStatus.Booked && now <= StartsAt + BookingRules.CheckInClosesAfter);

    public void Cancel(Slot slot, DateTimeOffset now)
    {
        if (slot.Id != SlotId)
        {
            throw new InvalidOperationException("Wrong slot for this appointment.");
        }

        if (Status != AppointmentStatus.Booked)
        {
            throw DomainException.Conflict("not_cancellable", $"Only booked appointments can be cancelled; this one is {Status}.");
        }

        if (StartsAt - now < BookingRules.CancellationCutoff)
        {
            throw DomainException.Conflict("too_late_to_cancel", "Appointments can be cancelled up to 2 hours before they start.");
        }

        Status = AppointmentStatus.Cancelled;
        CancelledAt = now;
        slot.Release();
    }

    public void CheckIn(string staffId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(staffId);

        if (Status != AppointmentStatus.Booked)
        {
            throw DomainException.Conflict("not_checkin_ready", $"Only booked appointments can be checked in; this one is {Status}.");
        }

        if (now < StartsAt - BookingRules.CheckInOpensBefore)
        {
            throw DomainException.Conflict("too_early_to_check_in", "Check-in opens 30 minutes before the appointment.");
        }

        if (now > StartsAt + BookingRules.CheckInClosesAfter)
        {
            throw DomainException.Conflict("check_in_closed", "Check-in closed 15 minutes after the start; the appointment is a no-show.");
        }

        Status = AppointmentStatus.CheckedIn;
        CheckedInAt = now;
        CheckedInBy = staffId;
    }

    public void Complete(DateTimeOffset now)
    {
        if (Status != AppointmentStatus.CheckedIn)
        {
            throw DomainException.Conflict("not_checked_in", $"Only checked-in appointments can be completed; this one is {Status}.");
        }

        Status = AppointmentStatus.Completed;
        CompletedAt = now;
    }

    private static string NewReference()
    {
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = ReferenceAlphabet[RandomNumberGenerator.GetInt32(ReferenceAlphabet.Length)];
        }

        return "APT-" + new string(chars);
    }
}
