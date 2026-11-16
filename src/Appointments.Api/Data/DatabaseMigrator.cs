using System.Reflection;
using DbUp;

namespace Appointments.Api.Data;

/// <summary>
/// Applies the embedded SQL scripts with DbUp. Each script runs once, in name order, in its own transaction,
/// and is recorded in dbo.SchemaVersions.
/// </summary>
public static class DatabaseMigrator
{
    public static void Migrate(string connectionString, bool seedDemoData, ILogger logger)
    {
        EnsureDatabase.For.SqlDatabase(connectionString);

        Apply(connectionString, ".Data.Scripts.", logger);
        if (seedDemoData)
        {
            Apply(connectionString, ".Data.Demo.", logger);
        }
    }

    private static void Apply(string connectionString, string folder, ILogger logger)
    {
        var result = DeployChanges.To
            .SqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly(), name => name.Contains(folder, StringComparison.Ordinal))
            .WithTransactionPerScript()
            .LogToNowhere()
            .Build()
            .PerformUpgrade();

        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }

        foreach (var script in result.Scripts)
        {
            logger.LogInformation("Applied database script {Script}", script.Name);
        }
    }
}
