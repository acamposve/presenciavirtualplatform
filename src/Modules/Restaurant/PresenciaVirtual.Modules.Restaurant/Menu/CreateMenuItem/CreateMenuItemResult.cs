namespace PresenciaVirtual.Modules.Restaurant.Menu.CreateMenuItem;

/// <summary>TenantId is deliberately not included (FR7, AC1): it is persisted and enforced server-side, not part of the response — consistent with CreateTable's response shape.</summary>
public sealed record CreateMenuItemResult(Guid MenuItemId, string Name, decimal Price, bool IsAlcoholic);
