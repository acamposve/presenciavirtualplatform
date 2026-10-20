namespace PresenciaVirtual.Modules.Restaurant.Ordering;

/// <summary>
/// The order lifecycle is closed as of specs/restaurant/ordering/cancel-order.md BR6: "Open" is
/// the only non-terminal value, and "Closed" (specs/restaurant/ordering/close-order.md) and
/// "Cancelled" (specs/restaurant/ordering/cancel-order.md) are the two terminal outcomes,
/// reachable only from "Open" and never from each other.
/// </summary>
public enum OrderStatus
{
    Open,
    Closed,
    Cancelled,
}
