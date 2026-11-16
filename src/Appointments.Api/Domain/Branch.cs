namespace Appointments.Api.Domain;

public sealed class Branch
{
    public int Id { get; private set; }

    public string Name { get; private set; } = "";

    public string City { get; private set; } = "";
}

public sealed class ServiceType
{
    public int Id { get; private set; }

    public string Code { get; private set; } = "";

    public string Name { get; private set; } = "";

    public int DurationMinutes { get; private set; }
}
