using Appointments.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Appointments.Api.Data;

/// <summary>
/// EF Core mapping. The schema itself is owned by the SQL scripts in Data/Scripts (applied by DbUp),
/// so this class only describes it; the integration tests run against the real schema and catch drift.
/// </summary>
public sealed class AppointmentsDb(DbContextOptions<AppointmentsDb> options) : DbContext(options)
{
    public DbSet<Branch> Branches => Set<Branch>();

    public DbSet<ServiceType> ServiceTypes => Set<ServiceType>();

    public DbSet<Slot> Slots => Set<Slot>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Branch>(e =>
        {
            e.ToTable("Branches");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.City).HasMaxLength(100);
        });

        modelBuilder.Entity<ServiceType>(e =>
        {
            e.ToTable("ServiceTypes");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(100);
        });

        modelBuilder.Entity<Slot>(e =>
        {
            e.ToTable("Slots");
            e.HasKey(x => x.Id);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.Ignore(x => x.Available);
        });

        modelBuilder.Entity<Appointment>(e =>
        {
            e.ToTable("Appointments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Reference).HasMaxLength(12);
            e.Property(x => x.CitizenId).HasMaxLength(64);
            e.Property(x => x.CheckedInBy).HasMaxLength(64);
        });
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> again (up to 3 times) when SaveChanges hits an optimistic-concurrency conflict,
    /// starting each retry from a clean change tracker so it re-reads current data.
    /// </summary>
    public async Task<T> WithConcurrencyRetry<T>(Func<Task<T>> attempt)
    {
        const int maxAttempts = 3;
        for (var i = 1; ; i++)
        {
            try
            {
                return await attempt();
            }
            catch (DbUpdateConcurrencyException) when (i < maxAttempts)
            {
                ChangeTracker.Clear();
            }
        }
    }
}
