namespace PresenciaVirtual.Modules.Restaurant.Menu.CreateMenuItem;

public sealed record CreateMenuItemCommand(string Name, decimal Price, bool IsAlcoholic);
