namespace PresenciaVirtual.Modules.Restaurant.Ordering;

/// <summary>
/// Only "Open" is relevant to the CreateOrder specification. Other values are introduced by
/// future specifications (AddItem, CloseOrder, CancelOrder).
/// </summary>
public enum OrderStatus
{
    Open,
}
