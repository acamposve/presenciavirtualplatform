namespace PresenciaVirtual.Modules.Restaurant.Settings.UpdateRestaurantSettings;

public static class UpdateRestaurantSettingsValidator
{
    /// <summary>
    /// Returns validation error messages; empty when the command is valid. Whether the field was
    /// present at all, and whether it was a JSON integer/null vs. some other type (AC4c/AC4d), is
    /// resolved by the endpoint before this is ever called (a plain int? can't distinguish
    /// "omitted" from "explicit null") - this only checks the range once a value is known (BR2).
    /// The upper bound (2,147,483,647) is already enforced by parsing into an Int32 upstream, so
    /// only positivity needs checking here.
    /// </summary>
    public static IReadOnlyList<string> Validate(UpdateRestaurantSettingsCommand command)
    {
        var errors = new List<string>();

        if (command.MaxAlcoholicItemQuantityPerLine is <= 0)
        {
            errors.Add("maxAlcoholicItemQuantityPerLine must be a positive value.");
        }

        return errors;
    }
}
