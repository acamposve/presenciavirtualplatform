namespace PresenciaVirtual.Modules.Restaurant.Tables.CreateTable;

/// <summary>TenantId is deliberately not included (FR5, AC1): it is persisted and enforced server-side, not part of the response.</summary>
public sealed record CreateTableResult(Guid TableId, string Label);
