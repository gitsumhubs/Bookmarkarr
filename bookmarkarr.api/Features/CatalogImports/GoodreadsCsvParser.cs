/* Bookmarkarr is licensed under the GNU AGPL v3 or later. */
using System.Text;

namespace Bookmarkarr.Api.Features.CatalogImports;

internal static class GoodreadsCsvParser
{
    public const long MaximumBytes = 10 * 1024 * 1024;
    public const int MaximumRows = 25_000;

    public static async Task<List<Dictionary<string, string>>> ParseAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true, 64 * 1024, leaveOpen: true);
        var text = await reader.ReadToEndAsync(ct);
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        var records = ParseRecords(text);
        if (records.Count == 0) throw new FormatException("The CSV does not contain a header row.");
        if (records.Count - 1 > MaximumRows) throw new FormatException($"Goodreads imports are limited to {MaximumRows:N0} rows.");

        var headers = records[0].Select(NormalizeHeader).ToList();
        if (!headers.Contains("book id") || !headers.Contains("title") ||
            !(headers.Contains("author") || headers.Contains("author l-f")))
            throw new FormatException("The file is not a Goodreads export: Book Id, Title, and Author columns are required.");

        var result = new List<Dictionary<string, string>>(Math.Max(0, records.Count - 1));
        foreach (var fields in records.Skip(1))
        {
            if (fields.All(string.IsNullOrWhiteSpace)) continue;
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count; i++) row[headers[i]] = i < fields.Count ? fields[i].Trim() : string.Empty;
            result.Add(row);
        }
        return result;
    }

    internal static List<List<string>> ParseRecords(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
                continue;
            }
            if (c == '"' && field.Length == 0) quoted = true;
            else if (c == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\r' || c == '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear(); rows.Add(row); row = [];
            }
            else field.Append(c);
        }
        if (quoted) throw new FormatException("The CSV ends inside a quoted field.");
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    private static string NormalizeHeader(string value) => value.Trim().TrimStart('\uFEFF').ToLowerInvariant();

    public static string NormalizeIsbn(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var value = raw.Trim();
        if (value.StartsWith("=\"", StringComparison.Ordinal) && value.EndsWith('"')) value = value[2..^1];
        return new string(value.Where(c => char.IsDigit(c) || c is 'x' or 'X').ToArray()).ToUpperInvariant();
    }

    public static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var normalized = value.Normalize(NormalizationForm.FormD);
        return string.Join(' ', new string(normalized.Where(c =>
                char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark &&
                (char.IsLetterOrDigit(c) || char.IsWhiteSpace(c))).ToArray())
            .ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
