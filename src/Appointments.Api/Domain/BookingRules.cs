namespace Appointments.Api.Domain;

/// <summary>The service-centre policy, in one place.</summary>
public static class BookingRules
{
    /// <summary>Saudi Arabia is UTC+3 all year (no daylight saving), so a fixed offset is exact.</summary>
    public static readonly TimeSpan LocalOffset = TimeSpan.FromHours(3);

    /// <summary>A slot can't be booked less than this long before it starts.</summary>
    public static readonly TimeSpan MinimumNotice = TimeSpan.FromMinutes(30);

    /// <summary>How far ahead citizens may book.</summary>
    public static readonly TimeSpan BookingHorizon = TimeSpan.FromDays(30);

    /// <summary>Cancelling is allowed until this long before the start, so the seat can be offered again.</summary>
    public static readonly TimeSpan CancellationCutoff = TimeSpan.FromHours(2);

    /// <summary>Check-in at the counter opens this long before the start...</summary>
    public static readonly TimeSpan CheckInOpensBefore = TimeSpan.FromMinutes(30);

    /// <summary>...and closes this long after it. Later than that the appointment counts as a no-show.</summary>
    public static readonly TimeSpan CheckInClosesAfter = TimeSpan.FromMinutes(15);

    public const int MaxCapacityPerSlot = 50;

    /// <summary>Start (inclusive) and end (exclusive) of a calendar day in Saudi time.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) LocalDay(DateOnly date)
    {
        var from = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), LocalOffset);
        return (from, from.AddDays(1));
    }

    public static DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(instant.ToOffset(LocalOffset).DateTime);
}
