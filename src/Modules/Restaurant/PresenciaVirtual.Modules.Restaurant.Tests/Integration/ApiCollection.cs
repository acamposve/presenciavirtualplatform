using Xunit;

namespace PresenciaVirtual.Modules.Restaurant.Tests.Integration;

/// <summary>
/// Every integration test class shares exactly one ApiFixture instance. ApiFixture configures
/// the host via process-wide environment variables (see its own comment for why); with more
/// than one instance alive at once, two fixtures race to set those variables and can end up
/// running migrations concurrently against whichever container the other last pointed at
/// (surfaced as spurious "duplicate key"/RLS errors) — a single shared instance removes the
/// race instead of papering over it.
/// </summary>
[CollectionDefinition(Name)]
public class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "Api";
}
