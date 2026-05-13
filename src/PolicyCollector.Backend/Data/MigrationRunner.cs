using System.Reflection;
using DbUp;

namespace PolicyCollector.Backend.Data;

public static class MigrationRunner
{
    public static Task RunAsync(string connectionString)
    {
        var upgrader = DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(
                Assembly.GetExecutingAssembly(),
                s => s.Contains("Migrations") && s.EndsWith(".sql"))
            .WithTransactionPerScript()
            .WithVariablesDisabled()
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
            throw new InvalidOperationException(
                $"Database migration failed: {result.Error?.Message}", result.Error);

        return Task.CompletedTask;
    }
}
