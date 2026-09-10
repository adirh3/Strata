using System.Text.RegularExpressions;

namespace StrataTheme.Diff;

internal readonly record struct CodeToken(int Start, int Length, string Kind);

/// <summary>Native-free lexical highlighting for Android and WebAssembly diff lines.</summary>
internal static class PortableCodeHighlighter
{
    private static readonly Regex Tokens = new(
        """(?<string>@?"(?:""|\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|`(?:\\.|[^`\\])*`)|(?<comment>//.*|/\*.*?(?:\*/|$)|\#.*|<!--.*?(?:-->|$))|(?<tag></?[A-Za-z][A-Za-z0-9:.-]*)|(?<number>\b(?:0[xX][0-9a-fA-F]+|\d+(?:\.\d+)?)\b)|(?<identifier>[$@]?[A-Za-z_][A-Za-z_0-9]*)""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));

    private static readonly HashSet<string> Keywords = new(
        ("abstract as async await base bool boolean break byte case catch char class const continue " +
         "decimal default def delegate delete do double elif else enum event except export extends " +
         "false False final finally float fn for foreach from func function get global if implements import in " +
         "int interface internal is lambda let lock long match namespace new None not null object of or " +
         "out override package partial pass private protected public raise readonly record ref return " +
         "sealed set short sizeof static string struct super switch this throw throws true try type " +
         "True typeof uint ulong unchecked undefined union unsafe using var virtual void volatile when where " +
         "while with yield").Split(' '), StringComparer.Ordinal);
    private static readonly HashSet<string> Languages = new(
        "cs csharp js jsx javascript ts tsx typescript json jsonc py python java kt kotlin rs rust go c cpp h hpp swift ps1 powershell sh bash rb ruby yml yaml xml html xaml axaml css scss sql toml".Split(' '),
        StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> SqlKeywords = new(
        "select from where join left right inner outer on as insert into values update set delete create alter drop table view index order by group having union all distinct null case when then else end and or not asc desc".Split(' '),
        StringComparer.OrdinalIgnoreCase);

    internal static IReadOnlyList<CodeToken> Tokenize(string text, string language)
    {
        if (text.Length == 0 || text.Length > 16_384 || !Languages.Contains(language))
            return [];

        var hashComments = language.ToLowerInvariant() is "py" or "python" or "sh" or "bash"
            or "ps1" or "powershell" or "yaml" or "yml" or "toml" or "rb" or "ruby";
        var tokens = new List<CodeToken>();
        try
        {
            foreach (Match match in Tokens.Matches(text))
            {
                if (match.Groups["comment"].Success)
                {
                    if (match.Value[0] != '#' || hashComments)
                    {
                        tokens.Add(new(match.Index, match.Length, "comment"));
                        if (match.Value.StartsWith("//", StringComparison.Ordinal) || match.Value[0] == '#')
                            break;
                    }
                }
                else if (match.Groups["string"].Success)
                    tokens.Add(new(match.Index, match.Length, "string"));
                else if (match.Groups["number"].Success)
                    tokens.Add(new(match.Index, match.Length, "number"));
                else if (match.Groups["tag"].Success)
                    tokens.Add(new(match.Index, match.Length, "type"));
                else if (Keywords.Contains(match.Value)
                    || language.Equals("sql", StringComparison.OrdinalIgnoreCase) && SqlKeywords.Contains(match.Value))
                    tokens.Add(new(match.Index, match.Length, "keyword"));
                else if (match.Value[0] == '$')
                    tokens.Add(new(match.Index, match.Length, "variable"));
                else if (char.IsUpper(match.Value[0]))
                    tokens.Add(new(match.Index, match.Length, "type"));
            }
        }
        catch (RegexMatchTimeoutException)
        {
            System.Diagnostics.Trace.TraceWarning(
                "Diff syntax highlighting exceeded its time budget; showing the original source text.");
            return [];
        }
        return tokens;
    }

    internal static string ColorFor(string kind, bool dark) => kind switch
    {
        "keyword" => dark ? "#C586C0" : "#7A1FA2",
        "string" => dark ? "#CE9178" : "#A31515",
        "number" => dark ? "#B5CEA8" : "#176B32",
        "comment" => dark ? "#84A96B" : "#387A35",
        "type" => dark ? "#4EC9B0" : "#007F78",
        "variable" => dark ? "#9CDCFE" : "#001080",
        _ => dark ? "#D4D4D4" : "#202020"
    };
}
