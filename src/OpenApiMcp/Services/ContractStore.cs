using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi.Readers;
using OpenApiMcp.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OpenApiMcp.Services;

/// <summary>
/// In-memory store for registered contracts. Contracts can be loaded from a
/// contracts directory on startup and registered at runtime via helper methods.
/// </summary>
public sealed class ContractStore
{
    private readonly Dictionary<string, ContractEntry> _contracts = new(StringComparer.OrdinalIgnoreCase);

    // ── Registration ──────────────────────────────────────────────────────────

    public ContractEntry Register(string source)
    {
        var raw = File.ReadAllText(source, Encoding.UTF8);
        var format = source.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "json" : "yaml";
        var jsonRaw = format == "json" ? raw : YamlToJson(raw);

        var doc = JsonNode.Parse(jsonRaw) ?? throw new InvalidOperationException("Failed to parse contract document.");

        var info = doc["info"];
        var title = info?["title"]?.GetValue<string>() ?? Path.GetFileNameWithoutExtension(source);
        var version = info?["version"]?.GetValue<string>() ?? "unknown";
        var openapi = doc["openapi"]?.GetValue<string>()
                   ?? doc["swagger"]?.GetValue<string>()
                   ?? "unknown";

        var id = SanitiseId(title);
        // Ensure uniqueness
        var candidate = id;
        int suffix = 2;
        while (_contracts.ContainsKey(candidate))
            candidate = $"{id}-{suffix++}";
        id = candidate;

        var entry = new ContractEntry
        {
            Id = id,
            Title = title,
            Version = version,
            OpenApiVersion = openapi,
            Source = source,
            SizeLines = raw.Split('\n').Length,
            Format = format,
            Document = doc,
            RawContent = raw
        };

        _contracts[id] = entry;
        return entry;
    }

    public ContractEntry RegisterFromContent(string id, string content, string format = "yaml")
    {
        var jsonRaw = format == "json" ? content : YamlToJson(content);
        var doc = JsonNode.Parse(jsonRaw) ?? throw new InvalidOperationException("Failed to parse contract document.");

        var info = doc["info"];
        var title = info?["title"]?.GetValue<string>() ?? id;
        var version = info?["version"]?.GetValue<string>() ?? "unknown";
        var openapi = doc["openapi"]?.GetValue<string>() ?? doc["swagger"]?.GetValue<string>() ?? "unknown";

        var entry = new ContractEntry
        {
            Id = id,
            Title = title,
            Version = version,
            OpenApiVersion = openapi,
            Source = $"[in-memory:{id}]",
            SizeLines = content.Split('\n').Length,
            Format = format,
            Document = doc,
            RawContent = content
        };

        _contracts[id] = entry;
        return entry;
    }

    // ── Retrieval ─────────────────────────────────────────────────────────────

    public IReadOnlyCollection<ContractEntry> GetAll() => _contracts.Values;

    public ContractEntry Get(string contractId)
    {
        if (_contracts.TryGetValue(contractId, out var entry))
            return entry;
        throw new McpException("CONTRACT_NOT_FOUND", $"No contract with ID '{contractId}' exists.");
    }

    public bool TryGet(string contractId, out ContractEntry? entry)
        => _contracts.TryGetValue(contractId, out entry);

    public void Unregister(string contractId)
        => _contracts.Remove(contractId);

    // ── Commit ────────────────────────────────────────────────────────────────

    public void Commit(string contractId, JsonNode updatedDoc)
    {
        var entry = Get(contractId);
        entry.Document = updatedDoc;
        var json = updatedDoc.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        if (entry.Format == "json")
        {
            entry.RawContent = json;
            if (!entry.Source.StartsWith("[in-memory"))
                File.WriteAllText(entry.Source, json, Encoding.UTF8);
        }
        else
        {
            // Write as YAML
            var yaml = JsonToYaml(json);
            entry.RawContent = yaml;
            if (!entry.Source.StartsWith("[in-memory"))
                File.WriteAllText(entry.Source, yaml, Encoding.UTF8);
        }

        entry.SizeLines = entry.RawContent.Split('\n').Length;
    }

    // ── Hashing ───────────────────────────────────────────────────────────────

    public static string HashDocument(JsonNode doc)
    {
        var json = doc.ToJsonString();
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes)[..16];
    }

    // ── YAML ↔ JSON helpers ───────────────────────────────────────────────────

    public static string YamlToJson(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        var obj = deserializer.Deserialize<object>(yaml)
                  ?? throw new InvalidOperationException("YAML document is null.");
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = false });
    }

    public static string JsonToYaml(string json)
    {
        // Use YamlDotNet to parse the JSON string (JSON is valid YAML 1.2) so that we
        // get native YamlDotNet types (Dictionary / List / string) rather than a boxed
        // JsonElement, which YamlDotNet would otherwise serialise as "ValueKind: Object".
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        var obj = deserializer.Deserialize<object>(json);
        var serializer = new SerializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();
        return serializer.Serialize(obj);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string SanitiseId(string title)
        => new string(title.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');

    // ── Auto-load ─────────────────────────────────────────────────────────────

    /// <summary>Scan a directory and register all .json/.yaml/.yml files.</summary>
    public void LoadDirectory(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                     .Where(f => f.EndsWith(".json") || f.EndsWith(".yaml") || f.EndsWith(".yml")))
        {
            try { Register(file); }
            catch { /* skip invalid files */ }
        }
    }
}
