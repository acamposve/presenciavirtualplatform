namespace PresenciaVirtual.Modules.Restaurant.Infrastructure.Ordering;

/// <summary>
/// Shared per-order advisory lock key (specs/restaurant/ordering/close-order.md BR7): both
/// AddItem's merge (<see cref="OrderItemRepository"/>) and CloseOrder's transition
/// (<see cref="OrderRepository"/>) MUST acquire this same lock, before checking or writing the
/// order's Status, so the two operations serialize against each other. It is acquired first,
/// ahead of any other lock either operation takes (e.g. AddItem's own per-line lock), to keep a
/// single, consistent lock ordering across the module.
/// </summary>
internal static class OrderLock
{
    public static string Key(Guid tenantId, Guid orderId) => $"order:{tenantId}:{orderId}";
}
