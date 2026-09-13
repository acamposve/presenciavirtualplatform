namespace PresenciaVirtual.Modules.Restaurant.Ordering;

/// <summary>
/// "Open" and "Closed" are reachable as of specs/restaurant/ordering/close-order.md. Other
/// values are introduced by future specifications (e.g. CancelOrder).
/// </summary>
public enum OrderStatus
{
    Open,
    Closed,
}
