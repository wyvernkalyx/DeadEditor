namespace SetlistFetcher;

/// <summary>
/// Thrown by <see cref="ArgParser.Parse"/> on any malformed argument (missing
/// value, a value that is itself a flag, or an unknown token). The caller owns
/// all I/O and exit behavior — this carries only the message.
/// </summary>
public sealed class ArgParserException : Exception
{
    public ArgParserException(string message) : base(message) { }
}

/// <summary>Result of a successful parse. Either path may be null (default applies).</summary>
public sealed record ParsedArgs(string? DataDir, string? ConcertsDir);

/// <summary>
/// Pure command-line argument parser for SetlistFetcher. Side-effect-free: no
/// Console, no Environment.Exit, no file access. Validates that each flag
/// (<c>--output</c>, <c>--concerts</c>) is followed by a real path value and
/// never swallows the next flag as a value — the 2026-06-10 silent-misfire.
/// </summary>
public static class ArgParser
{
    public static ParsedArgs Parse(string[] args)
    {
        string? dataDir = null;
        string? concertsDir = null;

        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i];
            switch (token)
            {
                case "--output":
                    dataDir = ConsumeValue(args, ref i, token);
                    break;
                case "--concerts":
                    concertsDir = ConsumeValue(args, ref i, token);
                    break;
                default:
                    throw new ArgParserException($"unknown argument '{token}'");
            }
        }

        return new ParsedArgs(dataDir, concertsDir);
    }

    /// <summary>
    /// Reads the value token that must follow <paramref name="flag"/> at
    /// <c>args[i + 1]</c>, then advances <paramref name="i"/> past it so the
    /// value is never re-examined as a flag (i += 2 semantics with the loop's
    /// own i++). Rejects a missing value or a value that starts with <c>--</c>.
    /// </summary>
    private static string ConsumeValue(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new ArgParserException($"flag '{flag}' expects a path value but got none");

        string value = args[i + 1];
        if (value.StartsWith("--", StringComparison.Ordinal))
            throw new ArgParserException($"flag '{flag}' expects a path value but got '{value}'");

        i++; // skip past the consumed value
        return value;
    }
}
