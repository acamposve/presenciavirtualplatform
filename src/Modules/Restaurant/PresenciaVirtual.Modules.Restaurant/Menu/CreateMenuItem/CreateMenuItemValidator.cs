namespace PresenciaVirtual.Modules.Restaurant.Menu.CreateMenuItem;

public static class CreateMenuItemValidator
{
    /// <summary>Returns validation error messages; empty when the command is valid.</summary>
    public static IReadOnlyList<string> Validate(CreateMenuItemCommand command)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(command.Name))
        {
            errors.Add("name is required.");
        }
        else if (command.Name.Length > MenuItem.MaxNameLength)
        {
            errors.Add($"name must not exceed {MenuItem.MaxNameLength} characters.");
        }

        if (command.Price <= 0)
        {
            errors.Add("price must be a positive value.");
        }
        else if (command.Price > MenuItem.MaxPrice)
        {
            errors.Add($"price must not exceed {MenuItem.MaxPrice}.");
        }
        else if (decimal.Round(command.Price, 2) != command.Price)
        {
            errors.Add("price must not have more than 2 decimal places.");
        }

        return errors;
    }
}
