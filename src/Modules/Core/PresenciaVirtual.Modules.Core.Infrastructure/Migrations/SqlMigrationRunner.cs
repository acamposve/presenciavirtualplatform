using System.Reflection;
using DbUp;

namespace PresenciaVirtual.Modules.Core.Infrastructure.Migrations;

/// <summary>
/// Applies embedded plain-SQL migration scripts from one or more module assemblies, in
/// deterministic order, tracking applied scripts in the database (DbUp's own bookkeeping
/// table). Each module owns and embeds its own scripts (technology.md: DbUp).
/// </summary>
public static class SqlMigrationRunner
{
    public static void Run(string connectionString, IReadOnlyDictionary<string, string>? variables, params Assembly[] scriptAssemblies)
    {
        var builder = DeployChanges.To.PostgresqlDatabase(connectionString);

        foreach (var assembly in scriptAssemblies)
        {
            builder = builder.WithScriptsEmbeddedInAssembly(assembly);
        }

        if (variables is { Count: > 0 })
        {
            // Lets scripts reference $VariableName$ (e.g. the least-privilege app role's
            // password) without hardcoding secrets into committed SQL — see 0000_app_role.sql.
            builder = builder.WithVariables(variables.ToDictionary(kv => kv.Key, kv => kv.Value));
        }

        var upgrader = builder
            .LogToConsole()
            .Build();

        var result = upgrader.PerformUpgrade();

        if (!result.Successful)
        {
            throw new InvalidOperationException("Database migration failed.", result.Error);
        }
    }
}
