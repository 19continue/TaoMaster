using System.Text.RegularExpressions;

namespace TaoMaster.Core.Utilities;

public sealed class VersionStringComparer : IComparer<string>
{
    public static VersionStringComparer Instance { get; } = new();

    private static readonly Regex TokenRegex = new(@"(\d+|[A-Za-z]+)", RegexOptions.Compiled);

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        var xTokens = Tokenize(x);
        var yTokens = Tokenize(y);
        var max = Math.Max(xTokens.Count, yTokens.Count);

        for (var index = 0; index < max; index++)
        {
            if (index >= xTokens.Count)
            {
                return -1;
            }

            if (index >= yTokens.Count)
            {
                return 1;
            }

            var comparison = CompareToken(xTokens[index], yTokens[index]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static List<string> Tokenize(string value) =>
        TokenRegex.Matches(value)
            .Select(match => match.Value)
            .ToList();

    private static int CompareToken(string x, string y)
    {
        var xIsNumber = int.TryParse(x, out var xNumber);
        var yIsNumber = int.TryParse(y, out var yNumber);

        if (xIsNumber && yIsNumber)
        {
            return xNumber.CompareTo(yNumber);
        }

        if (xIsNumber)
        {
            return 1;
        }

        if (yIsNumber)
        {
            return -1;
        }

        return string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
    }
}
