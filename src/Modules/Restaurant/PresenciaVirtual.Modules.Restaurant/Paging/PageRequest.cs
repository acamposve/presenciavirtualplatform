namespace PresenciaVirtual.Modules.Restaurant.Paging;

/// <summary>
/// The single offset-paging contract the Restaurant read capabilities share
/// (specs/restaurant/menu/get-menu-items.md FR3–FR7 and
/// specs/restaurant/tables/list-tables.md FR3–FR7): a 1-based <c>page</c>, a
/// <paramref name="pageSize"/> defaulting to 50 and capped at 200, and a total count over the
/// tenant scope. Deliberately one shared definition rather than two identical copies — the two
/// specifications promise the same contract, so drift between them would be a defect.
/// </summary>
public static class PageRequest
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public static int ResolvePage(int? page) => page ?? 1;

    public static int ResolvePageSize(int? pageSize) => pageSize ?? DefaultPageSize;

    /// <summary>
    /// The SQL <c>OFFSET</c> for a 1-based page, computed in 64-bit arithmetic so that a large
    /// (but individually valid) page number cannot overflow into a negative offset and silently
    /// return the first page again.
    /// </summary>
    public static long Offset(int page, int pageSize) => ((long)page - 1) * pageSize;

    /// <summary>Returns validation error messages; empty when the paging values are usable.</summary>
    public static IReadOnlyList<string> Validate(int page, int pageSize)
    {
        var errors = new List<string>();

        if (page < 1)
        {
            errors.Add("page must be at least 1.");
        }

        if (pageSize < 1)
        {
            errors.Add("pageSize must be at least 1.");
        }
        else if (pageSize > MaxPageSize)
        {
            errors.Add($"pageSize must not exceed {MaxPageSize}.");
        }

        return errors;
    }
}
