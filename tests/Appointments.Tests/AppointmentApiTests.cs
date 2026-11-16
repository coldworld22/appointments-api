using System.Net;
using System.Net.Http.Json;
using Appointments.Api.Domain;
using Appointments.Api.Features;

namespace Appointments.Tests;

/// <summary>
/// End to end through HTTP, auth, EF Core and SQL Server. Each test uses its own date or service and fresh user IDs,
/// so tests don't share slots, and sets the clock before it starts.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AppointmentApiTests(ApiFactory api)
{
    private const int AbhaBranch = 1;
    private const int IdRenewal = 1;            // 15-minute service
    private const int VehicleRegistration = 2;  // 30-minute service

    [Fact]
    public async Task Citizen_books_cannot_double_book_cancels_and_books_again()
    {
        api.Clock.Now = ApiFactory.StartOfTests;
        var date = new DateOnly(2030, 3, 2);
        await OpenSlots(date, IdRenewal, capacity: 2);
        var citizen = api.ClientFor(NewUser());

        var slots = await (await citizen.GetAsync($"/branches/{AbhaBranch}/slots?serviceTypeId={IdRenewal}&date={date:yyyy-MM-dd}"))
            .Read<List<SlotDto>>();
        Assert.Equal(16, slots.Count); // 08:00–12:00 in 15-minute steps
        Assert.All(slots, s => Assert.Equal(2, s.Available));

        var booked = await citizen.PostAsJsonAsync("/appointments", new BookAppointmentRequest(slots[0].Id));
        Assert.Equal(HttpStatusCode.Created, booked.StatusCode);
        var appointment = await booked.Read<AppointmentDto>();
        Assert.Equal(AppointmentStatus.Booked, appointment.Status);
        Assert.Equal($"/appointments/{appointment.Id}", booked.Headers.Location?.OriginalString);

        var second = await citizen.PostAsJsonAsync("/appointments", new BookAppointmentRequest(slots[1].Id));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("already_booked", await second.ProblemCode());

        var cancelled = await citizen.PostAsync($"/appointments/{appointment.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
        Assert.Equal(AppointmentStatus.Cancelled, (await cancelled.Read<AppointmentDto>()).Status);

        var slotsAfter = await (await citizen.GetAsync($"/branches/{AbhaBranch}/slots?serviceTypeId={IdRenewal}&date={date:yyyy-MM-dd}"))
            .Read<List<SlotDto>>();
        Assert.Equal(2, slotsAfter.Single(s => s.Id == slots[0].Id).Available);

        var rebooked = await citizen.PostAsJsonAsync("/appointments", new BookAppointmentRequest(slots[1].Id));
        Assert.Equal(HttpStatusCode.Created, rebooked.StatusCode);

        var mine = await (await citizen.GetAsync("/appointments/mine")).Read<List<AppointmentDto>>();
        Assert.Equal(2, mine.Count);
    }

    [Fact]
    public async Task Last_seat_goes_to_exactly_one_of_many_simultaneous_bookings()
    {
        api.Clock.Now = ApiFactory.StartOfTests;
        var date = new DateOnly(2030, 3, 3);
        await OpenSlots(date, IdRenewal, capacity: 1);
        var slot = (await (await api.ClientFor(NewUser()).GetAsync($"/branches/{AbhaBranch}/slots?serviceTypeId={IdRenewal}&date={date:yyyy-MM-dd}"))
            .Read<List<SlotDto>>())[0];

        var attempts = Enumerable.Range(0, 8)
            .Select(_ => api.ClientFor(NewUser()).PostAsJsonAsync("/appointments", new BookAppointmentRequest(slot.Id)));
        var responses = await Task.WhenAll(attempts);

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        var queue = await (await api.Staff().GetAsync($"/branches/{AbhaBranch}/queue?date={date:yyyy-MM-dd}"))
            .Read<List<AppointmentDto>>();
        Assert.Single(queue, a => a.SlotId == slot.Id);
    }

    [Fact]
    public async Task Counter_staff_check_in_inside_the_window_and_complete()
    {
        api.Clock.Now = ApiFactory.StartOfTests;
        var date = new DateOnly(2030, 3, 4);
        await OpenSlots(date, VehicleRegistration, capacity: 1);
        var citizen = api.ClientFor(NewUser());
        var slot = (await (await citizen.GetAsync($"/branches/{AbhaBranch}/slots?serviceTypeId={VehicleRegistration}&date={date:yyyy-MM-dd}"))
            .Read<List<SlotDto>>())[0];
        var appointment = await (await citizen.PostAsJsonAsync("/appointments", new BookAppointmentRequest(slot.Id)))
            .Read<AppointmentDto>();
        var staff = api.Staff();

        api.Clock.Now = slot.StartsAt.AddHours(-1);
        var early = await staff.PostAsync($"/appointments/{appointment.Id}/check-in", null);
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal("too_early_to_check_in", await early.ProblemCode());

        api.Clock.Now = slot.StartsAt.AddMinutes(-10);
        var citizenTriesToCheckIn = await citizen.PostAsync($"/appointments/{appointment.Id}/check-in", null);
        Assert.Equal(HttpStatusCode.Forbidden, citizenTriesToCheckIn.StatusCode);

        var checkedIn = await staff.PostAsync($"/appointments/{appointment.Id}/check-in", null);
        Assert.Equal(HttpStatusCode.OK, checkedIn.StatusCode);
        Assert.Equal(AppointmentStatus.CheckedIn, (await checkedIn.Read<AppointmentDto>()).Status);

        var tooLateToCancel = await citizen.PostAsync($"/appointments/{appointment.Id}/cancel", null);
        Assert.Equal(HttpStatusCode.Conflict, tooLateToCancel.StatusCode);

        api.Clock.Now = slot.StartsAt.AddMinutes(20);
        var completed = await staff.PostAsync($"/appointments/{appointment.Id}/complete", null);
        Assert.Equal(AppointmentStatus.Completed, (await completed.Read<AppointmentDto>()).Status);
    }

    [Fact]
    public async Task Citizens_cannot_see_or_cancel_each_others_appointments()
    {
        api.Clock.Now = ApiFactory.StartOfTests;
        var date = new DateOnly(2030, 3, 5);
        await OpenSlots(date, IdRenewal, capacity: 1);
        var owner = api.ClientFor(NewUser());
        var stranger = api.ClientFor(NewUser());
        var slot = (await (await owner.GetAsync($"/branches/{AbhaBranch}/slots?serviceTypeId={IdRenewal}&date={date:yyyy-MM-dd}"))
            .Read<List<SlotDto>>())[0];
        var appointment = await (await owner.PostAsJsonAsync("/appointments", new BookAppointmentRequest(slot.Id)))
            .Read<AppointmentDto>();

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/appointments/{appointment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/appointments/{appointment.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/appointments/{appointment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await api.Staff().GetAsync($"/appointments/{appointment.Id}")).StatusCode);
    }

    [Fact]
    public async Task Endpoints_require_sign_in_and_the_right_role()
    {
        var anonymous = api.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/branches")).StatusCode);

        var openSlotsAsStaff = await api.Staff().PostAsJsonAsync("/admin/slots", SlotsRequest(new DateOnly(2030, 3, 6), IdRenewal, 1), ApiFactory.Json);
        Assert.Equal(HttpStatusCode.Forbidden, openSlotsAsStaff.StatusCode);

        var queueAsCitizen = await api.ClientFor(NewUser()).GetAsync($"/branches/{AbhaBranch}/queue?date=2030-03-06");
        Assert.Equal(HttpStatusCode.Forbidden, queueAsCitizen.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync("/health/ready")).StatusCode);
    }

    [Fact]
    public async Task Opening_the_same_day_twice_skips_existing_slots()
    {
        api.Clock.Now = ApiFactory.StartOfTests;
        var date = new DateOnly(2030, 3, 7);

        var first = await (await api.Admin().PostAsJsonAsync("/admin/slots", SlotsRequest(date, VehicleRegistration, 2), ApiFactory.Json))
            .Read<GenerateSlotsResponse>();
        var second = await (await api.Admin().PostAsJsonAsync("/admin/slots", SlotsRequest(date, VehicleRegistration, 2), ApiFactory.Json))
            .Read<GenerateSlotsResponse>();

        Assert.Equal(new GenerateSlotsResponse(Created: 8, Skipped: 0), first); // 08:00–12:00 in 30-minute steps
        Assert.Equal(new GenerateSlotsResponse(Created: 0, Skipped: 8), second);
    }

    private async Task OpenSlots(DateOnly date, int serviceTypeId, int capacity)
    {
        var response = await api.Admin().PostAsJsonAsync("/admin/slots", SlotsRequest(date, serviceTypeId, capacity), ApiFactory.Json);
        response.EnsureSuccessStatusCode();
    }

    private static GenerateSlotsRequest SlotsRequest(DateOnly date, int serviceTypeId, int capacity) =>
        new(AbhaBranch, serviceTypeId, date, new TimeOnly(8, 0), new TimeOnly(12, 0), capacity);

    private static string NewUser() => $"citizen-{Guid.NewGuid():N}";
}
