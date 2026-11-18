namespace VectorMatchEngine.Core.Models;

/// <summary>
/// What a vectorized column holds, which decides how its values are cleaned up before they're vectorized
/// (<see cref="Services.FieldCleaner"/>). <see cref="Text"/> leaves a value as it is.
/// </summary>
public enum FieldKind
{
    Text,
    Name,
    Company,
    Address,
    DateMonthFirst,
    DateDayFirst,
    Phone,
    PostalCode,
    Code
}

public static class FieldKinds
{
    /// <summary>Every kind, in the order the import wizard lists them.</summary>
    public static IReadOnlyList<FieldKind> All { get; } = Enum.GetValues<FieldKind>();

    public static string Label(FieldKind kind) => kind switch
    {
        FieldKind.Text => "Text (as is)",
        FieldKind.Name => "Person's name",
        FieldKind.Company => "Company name",
        FieldKind.Address => "Street address",
        FieldKind.DateMonthFirst => "Date (month first)",
        FieldKind.DateDayFirst => "Date (day first)",
        FieldKind.Phone => "Phone number",
        FieldKind.PostalCode => "ZIP or postal code",
        FieldKind.Code => "Code or ID number",
        _ => kind.ToString()
    };

    /// <summary>The labels in <see cref="All"/>'s order, for a combo box bound by index.</summary>
    public static IReadOnlyList<string> Labels { get; } = All.Select(Label).ToList();

    /// <summary>Parses a kind stored by name (the database and the vectors workbook keep the enum name).</summary>
    public static FieldKind? Parse(string? text) =>
        Enum.TryParse<FieldKind>(text?.Trim(), ignoreCase: true, out var kind) && Enum.IsDefined(kind) ? kind : null;
}
