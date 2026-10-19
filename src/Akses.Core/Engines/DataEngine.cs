using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;

namespace Akses.Core.Engines;

/// <summary>
/// JSON, YAML, XML and CSV/TSV into each other, through a JSON tree:
/// XML attributes become "@name" keys and mixed text "#text"; CSV rows become objects keyed by the
/// header row, and nested objects are flattened to "a.b" columns on the way back to CSV.
/// </summary>
public static class DataEngine
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static async Task ConvertAsync(string input, string output, TargetFormat target, CancellationToken ct)
    {
        string text = await File.ReadAllTextAsync(input, ct).ConfigureAwait(false);
        JsonNode? tree = Read(text, Path.GetExtension(input).TrimStart('.').ToLowerInvariant());
        (string result, Encoding encoding) = target.Id switch
        {
            "json" => (tree?.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "null", Utf8NoBom),
            "yaml" => (ToYaml(tree), Utf8NoBom),
            "xml" => (ToXml(tree, Path.GetFileNameWithoutExtension(input)), Utf8NoBom),
            // With a BOM so Excel opens accented text correctly.
            "csv" => (ToCsv(tree), new UTF8Encoding(true)),
            _ => throw new NotSupportedException(target.Id),
        };
        await File.WriteAllTextAsync(output, result, encoding, ct).ConfigureAwait(false);
    }

    internal static JsonNode? Read(string text, string ext) => ext switch
    {
        "json" or "jsonc" or "geojson" => JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }),
        "yaml" or "yml" => FromYaml(text),
        "xml" => FromXml(text),
        "csv" => FromCsv(text, null),
        "tsv" or "tab" => FromCsv(text, '\t'),
        _ => throw new NotSupportedException(ext),
    };

    // ---- YAML ------------------------------------------------------------------------------

    private static JsonNode? FromYaml(string text)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(text));
        return stream.Documents.Count == 0 ? null : FromYamlNode(stream.Documents[0].RootNode);
    }

    private static JsonNode? FromYamlNode(YamlNode node) => node switch
    {
        YamlMappingNode map => new JsonObject(map.Children.Select(p =>
            KeyValuePair.Create(((YamlScalarNode)p.Key).Value ?? "", FromYamlNode(p.Value)))),
        YamlSequenceNode seq => new JsonArray(seq.Children.Select(FromYamlNode).ToArray()),
        YamlScalarNode scalar => Scalar(scalar.Value, scalar.Style is YamlDotNet.Core.ScalarStyle.Plain),
        _ => null,
    };

    /// <summary>Typed value for an unquoted scalar (numbers, booleans, null); quoted text stays a string.</summary>
    private static JsonNode? Scalar(string? value, bool plain)
    {
        if (value is null) return null;
        if (!plain) return JsonValue.Create(value);
        switch (value)
        {
            case "" or "~" or "null" or "Null" or "NULL": return null;
            case "true" or "True" or "TRUE": return JsonValue.Create(true);
            case "false" or "False" or "FALSE": return JsonValue.Create(false);
        }
        if (long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l) && !HasLeadingZero(value))
            return JsonValue.Create(l);
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && !HasLeadingZero(value) && double.IsFinite(d))
            return JsonValue.Create(d);
        return JsonValue.Create(value);
    }

    // "007" or a phone number is text, not 7.
    private static bool HasLeadingZero(string s)
    {
        string t = s.TrimStart('-', '+');
        return t.Length > 1 && t[0] == '0' && t[1] != '.';
    }

    private static string ToYaml(JsonNode? tree) =>
        new SerializerBuilder().DisableAliases().Build().Serialize(ToPlain(tree));

    private static object? ToPlain(JsonNode? node) => node switch
    {
        JsonObject obj => obj.ToDictionary(p => p.Key, p => ToPlain(p.Value)),
        JsonArray arr => arr.Select(ToPlain).ToList(),
        JsonValue v when v.TryGetValue(out string? s) => s,
        JsonValue v when v.TryGetValue(out bool b) => b,
        JsonValue v when v.TryGetValue(out long l) => l,
        JsonValue v when v.TryGetValue(out double d) => d,
        JsonValue v => v.ToJsonString(),
        _ => null,
    };

    // ---- XML -------------------------------------------------------------------------------

    private static JsonNode? FromXml(string text)
    {
        XElement root = XDocument.Parse(text).Root ?? throw new InvalidDataException("empty XML");
        return new JsonObject { [root.Name.LocalName] = FromElement(root) };
    }

    private static JsonNode? FromElement(XElement e)
    {
        bool hasChildren = e.HasElements, hasAttributes = e.HasAttributes;
        if (!hasChildren && !hasAttributes) return e.IsEmpty ? null : Scalar(e.Value, true);

        var obj = new JsonObject();
        foreach (XAttribute a in e.Attributes().Where(a => !a.IsNamespaceDeclaration))
            obj["@" + a.Name.LocalName] = Scalar(a.Value, true);
        foreach (IGrouping<string, XElement> group in e.Elements().GroupBy(c => c.Name.LocalName))
        {
            var items = group.ToList();
            obj[group.Key] = items.Count == 1 ? FromElement(items[0]) : new JsonArray(items.Select(FromElement).ToArray());
        }
        string ownText = string.Concat(e.Nodes().OfType<XText>().Select(t => t.Value)).Trim();
        if (ownText.Length > 0) obj["#text"] = Scalar(ownText, true);
        return obj;
    }

    private static string ToXml(JsonNode? tree, string fallbackRoot)
    {
        XElement root = tree is JsonObject obj && obj.Count == 1 && obj.First().Value is not JsonArray
            ? ToElement(obj.First().Key, obj.First().Value)
            : ToElement(SafeName(fallbackRoot), tree);
        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(new StringWriter(sb), new XmlWriterSettings { Indent = true, Encoding = Utf8NoBom }))
            doc.Save(writer);
        return sb.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");
    }

    private static XElement ToElement(string name, JsonNode? node)
    {
        var e = new XElement(SafeName(name));
        switch (node)
        {
            case JsonObject obj:
                foreach ((string key, JsonNode? value) in obj)
                {
                    if (key.StartsWith('@')) e.SetAttributeValue(SafeName(key[1..]), Text(value));
                    else if (key == "#text") e.Add(new XText(Text(value)));
                    else if (value is JsonArray arr) foreach (JsonNode? item in arr) e.Add(ToElement(key, item));
                    else e.Add(ToElement(key, value));
                }
                break;
            case JsonArray arr:
                foreach (JsonNode? item in arr) e.Add(ToElement("item", item));
                break;
            case null:
                break;
            default:
                e.Value = Text(node);
                break;
        }
        return e;
    }

    private static string SafeName(string name) => XmlConvert.EncodeLocalName(string.IsNullOrWhiteSpace(name) ? "item" : name.Replace(' ', '_'));

    private static string Text(JsonNode? node) => node switch
    {
        null => "",
        JsonValue v when v.TryGetValue(out string? s) => s,
        JsonValue v when v.TryGetValue(out bool b) => b ? "true" : "false",
        _ => node.ToJsonString(),
    };

    // ---- CSV -------------------------------------------------------------------------------

    private static JsonNode FromCsv(string text, char? delimiter)
    {
        List<List<string>> rows = ParseCsv(text, delimiter ?? DetectDelimiter(text));
        if (rows.Count == 0) return new JsonArray();
        List<string> header = rows[0];
        var result = new JsonArray();
        foreach (List<string> row in rows.Skip(1))
        {
            if (row.Count == 1 && row[0].Length == 0) continue;
            var obj = new JsonObject();
            for (int i = 0; i < Math.Max(header.Count, row.Count); i++)
            {
                string key = i < header.Count && header[i].Length > 0 ? header[i] : $"column{i + 1}";
                obj[key] = i < row.Count ? Scalar(row[i], true) : null;
            }
            result.Add(obj);
        }
        return result;
    }

    /// <summary>Comma, semicolon (Excel in Italian, German, French…) or tab, whichever the header uses most.</summary>
    internal static char DetectDelimiter(string text)
    {
        string first = text.Split('\n', 2)[0];
        char[] candidates = [',', ';', '\t', '|'];
        return candidates.OrderByDescending(c => first.Count(ch => ch == c)).First();
    }

    internal static List<List<string>> ParseCsv(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        int start = text.Length > 0 && text[0] == '﻿' ? 1 : 0;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else field.Append(c);
            }
            else if (c == '"' && field.Length == 0) quoted = true;
            else if (c == delimiter) { row.Add(field.ToString()); field.Clear(); }
            else if (c == '\r') { }
            else if (c == '\n') { row.Add(field.ToString()); field.Clear(); rows.Add(row); row = []; }
            else field.Append(c);
        }
        if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); rows.Add(row); }
        return rows;
    }

    private static string ToCsv(JsonNode? tree)
    {
        // Find the list of records: the tree itself, or the first array inside a single-key wrapper
        // (e.g. {"people": [...]}) — the usual shape of JSON exports.
        JsonNode? records = tree;
        while (records is JsonObject o && o.Count == 1 && o.First().Value is JsonObject or JsonArray)
            records = o.First().Value;
        IEnumerable<JsonNode?> items = records is JsonArray arr ? arr : [records];

        var flatRows = items.Select(item =>
        {
            var flat = new Dictionary<string, string>();
            Flatten(item, "", flat);
            return flat;
        }).ToList();
        var columns = new List<string>();
        foreach (var r in flatRows)
            foreach (string key in r.Keys)
                if (!columns.Contains(key)) columns.Add(key);

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(",", columns.Select(Quote)));
        foreach (var r in flatRows)
            sb.AppendLine(string.Join(",", columns.Select(c => Quote(r.GetValueOrDefault(c, "")))));
        return sb.ToString();
    }

    private static void Flatten(JsonNode? node, string prefix, Dictionary<string, string> into)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach ((string key, JsonNode? value) in obj) Flatten(value, prefix.Length == 0 ? key : $"{prefix}.{key}", into);
                break;
            case JsonArray arr when arr.All(x => x is JsonValue or null):
                into[prefix.Length == 0 ? "value" : prefix] = string.Join("; ", arr.Select(Text));
                break;
            case JsonArray arr:
                for (int i = 0; i < arr.Count; i++) Flatten(arr[i], $"{(prefix.Length == 0 ? "item" : prefix)}.{i + 1}", into);
                break;
            default:
                into[prefix.Length == 0 ? "value" : prefix] = Text(node);
                break;
        }
    }

    private static string Quote(string s) =>
        s.IndexOfAny([',', '"', '\n', '\r']) >= 0 || s.StartsWith(' ') || s.EndsWith(' ') ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
}
