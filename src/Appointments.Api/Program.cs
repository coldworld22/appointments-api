using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Appointments.Api.Auth;
using Appointments.Api.Data;
using Appointments.Api.Features;
using Appointments.Api.Infrastructure;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// In Azure this is "...;Authentication=Active Directory Managed Identity" — no password exists anywhere.
var connectionString = builder.Configuration.GetConnectionString("Appointments")
    ?? throw new InvalidOperationException("ConnectionStrings:Appointments is not configured.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<AppointmentsDb>(options =>
    options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure()));

builder.Services.AddAppointmentsAuth(builder.Configuration, builder.Environment);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppointmentsDb>("database", tags: ["ready"]);

var bookingsPerMinute = builder.Configuration.GetValue("RateLimits:BookingsPerMinute", 10);

// Per-user limit on booking, so one account (or a script) can't hold every seat hostage by booking and cancelling.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AppointmentEndpoints.BookingRateLimit, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst("oid")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = bookingsPerMinute,
                Window = TimeSpan.FromMinutes(1),
            }));
});

// Traces, metrics and logs to Application Insights when the connection string is present (it is in Azure).
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

var app = builder.Build();

if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    DatabaseMigrator.Migrate(connectionString, app.Configuration.GetValue("Database:SeedDemoData", false), app.Logger);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapOpenApi();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });

app.MapCatalogEndpoints();
app.MapAppointmentEndpoints();
app.MapAdminEndpoints();

app.Run();

/// <summary>Visible to the integration tests' WebApplicationFactory.</summary>
public partial class Program
{
}
