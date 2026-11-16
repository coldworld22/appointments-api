using Appointments.Api.Domain;

namespace Appointments.Tests;

/// <summary>Domain rules, no database: fast and exhaustive about the time windows.</summary>
public sealed class BookingRulesTests
{
    private static readonly DateTimeOffset Now = new(2030, 3, 1, 5, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Tomorrow = Now.AddDays(1);

    private static Slot NewSlot(DateTimeOffset startsAt, int capacity = 2) => new(1, 1, startsAt, 15, capacity);

    [Fact]
    public void Booking_takes_a_seat_until_the_slot_is_full()
    {
        var slot = NewSlot(Tomorrow, capacity: 2);

        Appointment.Book(slot, "citizen-a", Now);
        Appointment.Book(slot, "citizen-b", Now);
        var error = Assert.Throws<DomainException>(() => Appointment.Book(slot, "citizen-c", Now));

        Assert.Equal("slot_full", error.Code);
        Assert.Equal(2, slot.Booked);
        Assert.Equal(0, slot.Available);
    }

    [Fact]
    public void New_appointment_is_booked_with_a_readable_reference()
    {
        var appointment = Appointment.Book(NewSlot(Tomorrow), "citizen-a", Now);

        Assert.Equal(AppointmentStatus.Booked, appointment.Status);
        Assert.Matches("^APT-[A-HJ-NP-Z2-9]{8}$", appointment.Reference);
        Assert.Equal(Tomorrow, appointment.StartsAt);
    }

    [Theory]
    [InlineData(29, "too_late_to_book")] // under 30 minutes' notice
    [InlineData(-60, "too_late_to_book")] // already started
    [InlineData(60 * 24 * 31, "too_early_to_book")] // beyond the 30-day horizon
    public void Booking_outside_the_booking_window_is_refused(int minutesAhead, string expectedCode)
    {
        var slot = NewSlot(Now.AddMinutes(minutesAhead));

        var error = Assert.Throws<DomainException>(() => Appointment.Book(slot, "citizen-a", Now));

        Assert.Equal(expectedCode, error.Code);
        Assert.Equal(0, slot.Booked);
    }

    [Fact]
    public void Cancelling_frees_the_seat()
    {
        var slot = NewSlot(Tomorrow, capacity: 1);
        var appointment = Appointment.Book(slot, "citizen-a", Now);

        appointment.Cancel(slot, Now.AddHours(1));

        Assert.Equal(AppointmentStatus.Cancelled, appointment.Status);
        Assert.Equal(1, slot.Available);
    }

    [Fact]
    public void Cancelling_less_than_two_hours_before_is_refused()
    {
        var slot = NewSlot(Tomorrow);
        var appointment = Appointment.Book(slot, "citizen-a", Now);

        var error = Assert.Throws<DomainException>(() => appointment.Cancel(slot, Tomorrow.AddMinutes(-119)));

        Assert.Equal("too_late_to_cancel", error.Code);
        Assert.Equal(AppointmentStatus.Booked, appointment.Status);
    }

    [Theory]
    [InlineData(-31, "too_early_to_check_in")]
    [InlineData(16, "check_in_closed")]
    public void Check_in_outside_its_window_is_refused(int minutesFromStart, string expectedCode)
    {
        var appointment = Appointment.Book(NewSlot(Tomorrow), "citizen-a", Now);

        var error = Assert.Throws<DomainException>(() => appointment.CheckIn("staff-1", Tomorrow.AddMinutes(minutesFromStart)));

        Assert.Equal(expectedCode, error.Code);
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(0)]
    [InlineData(15)]
    public void Check_in_inside_its_window_is_accepted(int minutesFromStart)
    {
        var appointment = Appointment.Book(NewSlot(Tomorrow), "citizen-a", Now);

        appointment.CheckIn("staff-1", Tomorrow.AddMinutes(minutesFromStart));

        Assert.Equal(AppointmentStatus.CheckedIn, appointment.Status);
        Assert.Equal("staff-1", appointment.CheckedInBy);
    }

    [Fact]
    public void Only_checked_in_appointments_can_be_completed()
    {
        var appointment = Appointment.Book(NewSlot(Tomorrow), "citizen-a", Now);

        Assert.Equal("not_checked_in", Assert.Throws<DomainException>(() => appointment.Complete(Tomorrow)).Code);

        appointment.CheckIn("staff-1", Tomorrow);
        appointment.Complete(Tomorrow.AddMinutes(10));
        Assert.Equal(AppointmentStatus.Completed, appointment.Status);
    }

    [Fact]
    public void A_missed_appointment_stops_counting_as_active_after_check_in_closes()
    {
        var appointment = Appointment.Book(NewSlot(Tomorrow), "citizen-a", Now);

        Assert.True(appointment.IsActiveAt(Tomorrow.AddMinutes(15)));
        Assert.False(appointment.IsActiveAt(Tomorrow.AddMinutes(16)));
    }

    [Fact]
    public void Local_day_is_midnight_to_midnight_in_Saudi_time()
    {
        var (from, to) = BookingRules.LocalDay(new DateOnly(2030, 3, 2));

        Assert.Equal(new DateTimeOffset(2030, 3, 1, 21, 0, 0, TimeSpan.Zero), from.ToUniversalTime());
        Assert.Equal(TimeSpan.FromDays(1), to - from);
    }
}
