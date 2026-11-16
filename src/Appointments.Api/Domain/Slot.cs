namespace Appointments.Api.Domain;

/// <summary>
/// A time window at a branch for one service, with a number of seats (counters).
/// <see cref="RowVersion"/> is a SQL Server rowversion: two people taking the last seat at the same time
/// can't both succeed, because the second save fails the concurrency check and is retried against fresh data.
/// </summary>
public sealed class Slot
{
    private Slot()
    {
    }

    public Slot(int branchId, int serviceTypeId, DateTimeOffset startsAt, int durationMinutes, int capacity)
    {
        if (capacity is < 1 or > BookingRules.MaxCapacityPerSlot)
        {
            throw DomainException.Invalid("invalid_capacity", $"Capacity must be between 1 and {BookingRules.MaxCapacityPerSlot}.");
        }

        if (durationMinutes <= 0)
        {
            throw DomainException.Invalid("invalid_duration", "Duration must be positive.");
        }

        BranchId = branchId;
        ServiceTypeId = serviceTypeId;
        StartsAt = startsAt;
        EndsAt = startsAt.AddMinutes(durationMinutes);
        Capacity = capacity;
    }

    public long Id { get; private set; }

    public int BranchId { get; private set; }

    public int ServiceTypeId { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public int Capacity { get; private set; }

    public int Booked { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public int Available => Capacity - Booked;

    internal void Reserve(DateTimeOffset now)
    {
        if (StartsAt - now < BookingRules.MinimumNotice)
        {
            throw DomainException.Conflict("too_late_to_book", "This slot starts too soon to be booked.");
        }

        if (StartsAt - now > BookingRules.BookingHorizon)
        {
            throw DomainException.Conflict("too_early_to_book", "Slots can be booked up to 30 days ahead.");
        }

        if (Booked >= Capacity)
        {
            throw DomainException.Conflict("slot_full", "This slot is fully booked.");
        }

        Booked++;
    }

    internal void Release()
    {
        if (Booked > 0)
        {
            Booked--;
        }
    }
}
