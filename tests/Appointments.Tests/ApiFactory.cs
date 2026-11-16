using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MsSql;

namespace Appointments.Tests;

/// <summary>
/// Runs the real API in memory against a real SQL Server in Docker (Testcontainers), with the schema created by the
/// same DbUp scripts production uses. The clock is controllable, so time-window rules can be tested exactly.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly DateTimeOffset StartOfTests = new(2030, 3, 1, 5, 0, 0, TimeSpan.Zero); // 08:00 in Riyadh

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public TestClock Clock { get; } = new(StartOfTests);

    public HttpClient ClientFor(string userId, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", userId);
        if (roles.Length > 0)
        {
            client.DefaultRequestHeaders.Add("X-Dev-Roles", string.Join(',', roles));
        }

        return client;
    }

    public HttpClient Admin() => ClientFor("admin-1", "Admin");

    public HttpClient Staff() => ClientFor("staff-1", "Staff");

    async Task IAsyncLifetime.InitializeAsync() => await _sql.StartAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _sql.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var connectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString())
        {
            InitialCatalog = "appointments",
        }.ConnectionString;

        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Appointments", connectionString);
        builder.UseSetting("Auth:Mode", "Dev");
        builder.UseSetting("Database:SeedDemoData", "true");
        builder.UseSetting("RateLimits:BookingsPerMinute", "100");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
        });
    }
}

/// <summary>A clock the tests set by hand. Unlike FakeTimeProvider it may move backwards, so tests don't depend on order.</summary>
public sealed class TestClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

public static class HttpExtensions
{
    public static async Task<T> Read<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<T>(ApiFactory.Json);
        return body ?? throw new InvalidOperationException("Empty response body.");
    }

    /// <summary>The <c>code</c> field of a problem response, e.g. <c>slot_full</c>.</summary>
    public static async Task<string?> ProblemCode(this HttpResponseMessage response)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
