namespace PresenciaVirtual.Modules.Restaurant.Tables.CreateTable;

public static class CreateTableValidator
{
    /// <summary>Returns validation error messages; empty when the command is valid.</summary>
    public static IReadOnlyList<string> Validate(CreateTableCommand command)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(command.Label))
        {
            errors.Add("label is required.");
        }
        else if (command.Label.Length > Table.MaxLabelLength)
        {
            errors.Add($"label must not exceed {Table.MaxLabelLength} characters.");
        }

        return errors;
    }
}
