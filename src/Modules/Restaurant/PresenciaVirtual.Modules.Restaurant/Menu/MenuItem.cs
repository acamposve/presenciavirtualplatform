namespace PresenciaVirtual.Modules.Restaurant.Menu;

/// <summary>
/// Minimal reference concept, not a full Menu Management aggregate (specs/restaurant/ordering/add-item.md):
/// there is no capability to create or edit a MenuItem yet, only to read one that already exists.
/// </summary>
public sealed record MenuItem(Guid Id, Guid TenantId, string Name, decimal Price, bool IsAlcoholic);
