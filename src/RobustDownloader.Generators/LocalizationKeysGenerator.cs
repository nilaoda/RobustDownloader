using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace RobustDownloader.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class LocalizationKeysGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor MissingKeyDescriptor = new(
        id: "RDL001",
        title: "Localization key missing from resource file",
        messageFormat: "Localization key '{0}' exists in '{1}' but is missing from '{2}'.",
        category: "Localization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor IdentifierCollisionDescriptor = new(
        id: "RDL002",
        title: "Localization key identifier collision",
        messageFormat: "Localization keys generate the same C# identifier '{0}': {1}.",
        category: "Localization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor PlaceholderMismatchDescriptor = new(
        id: "RDL003",
        title: "Localization format placeholder mismatch",
        messageFormat: "Localization key '{0}' has placeholders {{{1}}} in '{2}' but {{{3}}} in '{4}'.",
        category: "Localization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor InvalidResxDescriptor = new(
        id: "RDL004",
        title: "Localization resource file is invalid",
        messageFormat: "Localization resource file '{0}' could not be parsed: {1}.",
        category: "Localization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingLanguageDescriptor = new(
        id: "RDL005",
        title: "Localization language resource file is missing",
        messageFormat: "Localization language resource file for '{0}' is missing.",
        category: "Localization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor DuplicateKeyDescriptor = new(
        id: "RDL006",
        title: "Localization resource key is duplicated",
        messageFormat: "Localization key '{0}' is duplicated in '{1}'.",
        category: "Localization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor MissingDynamicResourceDescriptor = new(
        id: "RDL007",
        title: "Localized DynamicResource key is missing",
        messageFormat: "Localized DynamicResource key '{0}' in '{1}' does not exist in localization resources.",
        category: "Localization",
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly RequiredLanguage[] RequiredLanguages =
    {
        new("ZhHans", "Strings.zh-Hans.resx"),
        new("En", "Strings.en.resx"),
        new("ZhHant", "Strings.zh-Hant.resx")
    };

    private static readonly Regex PlaceholderRegex = new(@"\{(\d+)(?:[^{}]*)\}", RegexOptions.Compiled);
    private static readonly Regex DynamicResourceRegex = new(@"\{DynamicResource\s+([A-Za-z0-9_.]+)\}", RegexOptions.Compiled);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var additionalFiles = context.AdditionalTextsProvider
            .Select(static (file, token) => new ResxSource(file.Path, file.GetText(token)?.ToString() ?? ""));

        context.RegisterSourceOutput(additionalFiles.Collect(), Generate);
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<ResxSource> sources)
    {
        if (sources.IsDefaultOrEmpty)
            return;

        var resxSources = sources
            .Where(static source => Path.GetFileName(source.Path).StartsWith("Strings.", StringComparison.OrdinalIgnoreCase) &&
                                    source.Path.EndsWith(".resx", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var axamlSources = sources
            .Where(static source => source.Path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var dictionaries = new List<LocalizationDictionary>();
        foreach (var source in resxSources)
        {
            var languageName = GetLanguageName(source.Path);
            if (languageName == null)
                continue;

            if (TryParseResx(context, source, languageName, out var dictionary))
                dictionaries.Add(dictionary);
        }

        var byName = dictionaries
            .GroupBy(static dictionary => dictionary.Name, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First(), StringComparer.Ordinal);

        foreach (var language in RequiredLanguages)
        {
            if (!byName.ContainsKey(language.Name))
            {
                context.ReportDiagnostic(Diagnostic.Create(MissingLanguageDescriptor, Location.None, language.FileName));
            }
        }

        if (!byName.TryGetValue("En", out var baseline))
            baseline = dictionaries.OrderByDescending(static dictionary => dictionary.Entries.Count).FirstOrDefault();

        if (baseline == null)
            return;

        ReportCompletenessDiagnostics(context, dictionaries, baseline);

        var keys = dictionaries
            .SelectMany(static dictionary => dictionary.Entries.Keys)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();

        var identifierGroups = keys
            .GroupBy(ToIdentifier, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .ToArray();

        foreach (var group in identifierGroups)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                IdentifierCollisionDescriptor,
                Location.None,
                group.Key,
                string.Join(", ", group.OrderBy(static key => key, StringComparer.Ordinal))));
        }

        if (identifierGroups.Length > 0)
            return;

        ReportPlaceholderDiagnostics(context, dictionaries, baseline);
        ReportDynamicResourceDiagnostics(context, axamlSources, keys);
        context.AddSource("LocalizationKeys.g.cs", SourceText.From(RenderSource(keys, baseline, byName), Encoding.UTF8));
    }

    private static string? GetLanguageName(string path)
    {
        var fileName = Path.GetFileName(path);
        foreach (var language in RequiredLanguages)
        {
            if (string.Equals(fileName, language.FileName, StringComparison.OrdinalIgnoreCase))
                return language.Name;
        }

        return null;
    }

    private static bool TryParseResx(
        SourceProductionContext context,
        ResxSource source,
        string languageName,
        out LocalizationDictionary dictionary)
    {
        dictionary = new LocalizationDictionary(languageName, source.Path, new Dictionary<string, LocalizedEntry>(StringComparer.Ordinal));
        try
        {
            var document = XDocument.Parse(source.Content, LoadOptions.PreserveWhitespace);
            var entries = new Dictionary<string, LocalizedEntry>(StringComparer.Ordinal);
            foreach (var data in document.Root?.Elements("data") ?? Enumerable.Empty<XElement>())
            {
                var key = data.Attribute("name")?.Value;
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                var resourceKey = key!;
                if (entries.ContainsKey(resourceKey))
                {
                    context.ReportDiagnostic(Diagnostic.Create(DuplicateKeyDescriptor, Location.None, resourceKey, source.Path));
                    continue;
                }

                var value = data.Element("value")?.Value ?? "";
                entries.Add(resourceKey, new LocalizedEntry(value));
            }

            dictionary = new LocalizationDictionary(languageName, source.Path, entries);
            return true;
        }
        catch (Exception ex)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                InvalidResxDescriptor,
                Location.None,
                Path.GetFileName(source.Path),
                ex.Message));
            return false;
        }
    }

    private static void ReportCompletenessDiagnostics(
        SourceProductionContext context,
        IReadOnlyList<LocalizationDictionary> dictionaries,
        LocalizationDictionary baseline)
    {
        foreach (var dictionary in dictionaries)
        {
            foreach (var key in baseline.Entries.Keys)
            {
                if (!dictionary.Entries.ContainsKey(key))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MissingKeyDescriptor,
                        Location.None,
                        key,
                        Path.GetFileName(baseline.Path),
                        Path.GetFileName(dictionary.Path)));
                }
            }

            foreach (var key in dictionary.Entries.Keys)
            {
                if (!baseline.Entries.ContainsKey(key))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        MissingKeyDescriptor,
                        Location.None,
                        key,
                        Path.GetFileName(dictionary.Path),
                        Path.GetFileName(baseline.Path)));
                }
            }
        }
    }

    private static void ReportPlaceholderDiagnostics(
        SourceProductionContext context,
        IReadOnlyList<LocalizationDictionary> dictionaries,
        LocalizationDictionary baseline)
    {
        foreach (var pair in baseline.Entries)
        {
            var key = pair.Key;
            var baselineEntry = pair.Value;
            var baselinePlaceholders = GetPlaceholderIndexes(baselineEntry.Value);
            foreach (var dictionary in dictionaries)
            {
                if (!dictionary.Entries.TryGetValue(key, out var entry))
                    continue;

                var placeholders = GetPlaceholderIndexes(entry.Value);
                if (!baselinePlaceholders.SequenceEqual(placeholders))
                {
                    context.ReportDiagnostic(Diagnostic.Create(
                        PlaceholderMismatchDescriptor,
                        Location.None,
                        key,
                        FormatPlaceholders(baselinePlaceholders),
                        Path.GetFileName(baseline.Path),
                        FormatPlaceholders(placeholders),
                        Path.GetFileName(dictionary.Path)));
                }
            }
        }
    }

    private static void ReportDynamicResourceDiagnostics(
        SourceProductionContext context,
        IReadOnlyList<ResxSource> axamlSources,
        IReadOnlyList<string> keys)
    {
        var knownKeys = new HashSet<string>(keys, StringComparer.Ordinal);
        foreach (var source in axamlSources)
        {
            foreach (Match match in DynamicResourceRegex.Matches(source.Content))
            {
                var key = match.Groups[1].Value;
                if (!key.Contains(".", StringComparison.Ordinal) || knownKeys.Contains(key))
                    continue;

                context.ReportDiagnostic(Diagnostic.Create(
                    MissingDynamicResourceDescriptor,
                    Location.None,
                    key,
                    Path.GetFileName(source.Path)));
            }
        }
    }

    private static int[] GetPlaceholderIndexes(string value)
    {
        return PlaceholderRegex.Matches(value)
            .Cast<Match>()
            .Select(static match => int.Parse(match.Groups[1].Value))
            .Distinct()
            .OrderBy(static placeholder => placeholder)
            .ToArray();
    }

    private static string FormatPlaceholders(IReadOnlyList<int> placeholders)
    {
        return string.Join(", ", placeholders.Select(static placeholder => placeholder.ToString()));
    }

    private static string RenderSource(
        IReadOnlyList<string> keys,
        LocalizationDictionary baseline,
        IReadOnlyDictionary<string, LocalizationDictionary> dictionaries)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine();
        builder.AppendLine("namespace RobustDownloader.Services;");
        builder.AppendLine();
        RenderLocKeys(builder, keys);
        builder.AppendLine();
        RenderAccessors(builder, keys, baseline);
        builder.AppendLine();
        RenderCatalog(builder, dictionaries);
        return builder.ToString();
    }

    private static void RenderLocKeys(StringBuilder builder, IReadOnlyList<string> keys)
    {
        builder.AppendLine("public static class LocKeys");
        builder.AppendLine("{");
        foreach (var key in keys)
        {
            builder.Append("    public const string ");
            builder.Append(ToIdentifier(key));
            builder.Append(" = ");
            builder.Append(ToLiteral(key));
            builder.AppendLine(";");
        }

        builder.AppendLine("}");
    }

    private static void RenderAccessors(StringBuilder builder, IReadOnlyList<string> keys, LocalizationDictionary baseline)
    {
        builder.AppendLine("public static class L");
        builder.AppendLine("{");
        foreach (var key in keys)
        {
            var identifier = ToIdentifier(key);
            var placeholders = baseline.Entries.TryGetValue(key, out var entry)
                ? GetPlaceholderIndexes(entry.Value)
                : Array.Empty<int>();
            if (placeholders.Length == 0)
            {
                builder.Append("    public static string ");
                builder.Append(identifier);
                builder.Append(" => LocalizationService.Get(LocKeys.");
                builder.Append(identifier);
                builder.AppendLine(");");
            }
            else
            {
                var argumentCount = placeholders.Max() + 1;
                builder.Append("    public static string ");
                builder.Append(identifier);
                builder.Append("(");
                for (var i = 0; i < argumentCount; i++)
                {
                    if (i > 0)
                        builder.Append(", ");
                    builder.Append("object? arg");
                    builder.Append(i);
                }

                builder.Append(") => LocalizationService.Format(LocKeys.");
                builder.Append(identifier);
                for (var i = 0; i < argumentCount; i++)
                {
                    builder.Append(", arg");
                    builder.Append(i);
                }

                builder.AppendLine(");");
            }
        }

        builder.AppendLine("}");
    }

    private static void RenderCatalog(StringBuilder builder, IReadOnlyDictionary<string, LocalizationDictionary> dictionaries)
    {
        builder.AppendLine("internal static class LocalizationCatalog");
        builder.AppendLine("{");
        foreach (var language in RequiredLanguages)
        {
            dictionaries.TryGetValue(language.Name, out var dictionary);
            builder.Append("    internal static global::System.Collections.Generic.IReadOnlyDictionary<string, string> ");
            builder.Append(language.Name);
            builder.AppendLine(" { get; } = new global::System.Collections.Generic.Dictionary<string, string>(global::System.StringComparer.Ordinal)");
            builder.AppendLine("    {");

            foreach (var pair in (dictionary?.Entries ?? EmptyEntries()).OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                builder.Append("        [");
                builder.Append(ToLiteral(pair.Key));
                builder.Append("] = ");
                builder.Append(ToLiteral(pair.Value.Value));
                builder.AppendLine(",");
            }

            builder.AppendLine("    };");
        }

        builder.AppendLine("}");
    }

    private static IReadOnlyDictionary<string, LocalizedEntry> EmptyEntries()
    {
        return new Dictionary<string, LocalizedEntry>(StringComparer.Ordinal);
    }

    private static string ToIdentifier(string key)
    {
        var builder = new StringBuilder(key.Length);
        foreach (var c in key)
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '_');
        }

        if (builder.Length == 0 || char.IsDigit(builder[0]))
            builder.Insert(0, '_');

        var identifier = builder.ToString();
        return IsKeyword(identifier) ? identifier + "_" : identifier;
    }

    private static bool IsKeyword(string identifier)
    {
        return SyntaxFacts.GetKeywordKind(identifier) != SyntaxKind.None ||
               SyntaxFacts.GetContextualKeywordKind(identifier) != SyntaxKind.None;
    }

    private static string ToLiteral(string value)
    {
        return SymbolDisplay.FormatLiteral(value, quote: true);
    }

    private sealed class ResxSource
    {
        public ResxSource(string path, string content)
        {
            Path = path;
            Content = content;
        }

        public string Path { get; }
        public string Content { get; }
    }

    private sealed class RequiredLanguage
    {
        public RequiredLanguage(string name, string fileName)
        {
            Name = name;
            FileName = fileName;
        }

        public string Name { get; }
        public string FileName { get; }
    }

    private sealed class LocalizationDictionary
    {
        public LocalizationDictionary(string name, string path, IReadOnlyDictionary<string, LocalizedEntry> entries)
        {
            Name = name;
            Path = path;
            Entries = entries;
        }

        public string Name { get; }
        public string Path { get; }
        public IReadOnlyDictionary<string, LocalizedEntry> Entries { get; }
    }

    private sealed class LocalizedEntry
    {
        public LocalizedEntry(string value)
        {
            Value = value;
        }

        public string Value { get; }
    }
}
