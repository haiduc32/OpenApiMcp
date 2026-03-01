using System.CommandLine;
using OpenApiMcp.Cli.Helpers;
using OpenApiMcp.Services;

namespace OpenApiMcp.Cli.Commands;

/// <summary>
/// Read-only commands: contracts, info, index, get, operation, schema, search,
/// tag-operations, and export. None of these requires a session.
/// </summary>
internal static class ReadCommands
{
    public static void Register(RootCommand root, ContractStore store)
    {
        root.AddCommand(ContractsCmd(store));
        root.AddCommand(InfoCmd(store));
        root.AddCommand(IndexCmd(store));
        root.AddCommand(GetCmd(store));
        root.AddCommand(OperationCmd(store));
        root.AddCommand(SchemaCmd(store));
        root.AddCommand(SearchCmd(store));
        root.AddCommand(TagOperationsCmd(store));
        root.AddCommand(ExportCmd(store));
    }

    // ── contracts ─────────────────────────────────────────────────────────────

    private static Command ContractsCmd(ContractStore store)
    {
        var cmd = new Command("contracts", "List all registered contracts.");
        cmd.SetHandler(() =>
        {
            CliHelper.Ok(new
            {
                contracts = store.GetAll().Select(e => new
                {
                    id         = e.Id,
                    title      = e.Title,
                    version    = e.Version,
                    openapi    = e.OpenApiVersion,
                    format     = e.Format,
                    size_lines = e.SizeLines
                })
            });
        });
        return cmd;
    }

    // ── info ──────────────────────────────────────────────────────────────────

    private static Command InfoCmd(ContractStore store)
    {
        var cmd   = new Command("info", "Show the info, servers, security, and tags blocks of a contract.");
        var idArg = new Argument<string>("contract-id", "The contract ID.");
        cmd.AddArgument(idArg);
        cmd.SetHandler(id =>
        {
            try
            {
                var doc = store.Get(id).Document;
                CliHelper.Ok(new
                {
                    contract_id = id,
                    openapi     = doc["openapi"]?.GetValue<string>(),
                    info        = doc["info"],
                    servers     = doc["servers"],
                    security    = doc["security"],
                    tags        = doc["tags"]
                });
            }
            catch (McpException ex) { Environment.ExitCode = CliHelper.Error(ex.Code, ex.Message, ex.Pointer); }
        }, idArg);
        return cmd;
    }

    // ── index ─────────────────────────────────────────────────────────────────

    private static Command IndexCmd(ContractStore store)
    {
        var cmd     = new Command("index", "List all paths and component names in a contract.");
        var idArg   = new Argument<string>("contract-id", "The contract ID.");
        var tagsOpt = new Option<bool>("--tags", "Group paths by tag.");
        cmd.AddArgument(idArg);
        cmd.AddOption(tagsOpt);
        cmd.SetHandler((id, includeTags) =>
        {
            try
            {
                var doc     = store.Get(id).Document;
                var paths   = doc["paths"] as System.Text.Json.Nodes.JsonObject;
                var schemas = (doc["components"]?["schemas"] as System.Text.Json.Nodes.JsonObject)?
                              .Select(kv => kv.Key).ToList() ?? [];

                object pathsResult;
                if (includeTags)
                {
                    var byTag = new Dictionary<string, List<object>>();
                    if (paths is not null)
                    {
                        foreach (var (path, item) in paths)
                        {
                            if (item is not System.Text.Json.Nodes.JsonObject pi) continue;
                            foreach (var method in new[] { "get","post","put","delete","patch","options","head","trace" })
                            {
                                var op = pi[method];
                                if (op is null) continue;
                                var tags = op["tags"]?.AsArray().Select(t => t?.GetValue<string>() ?? "untagged").ToList()
                                           ?? ["untagged"];
                                foreach (var tag in tags)
                                {
                                    if (!byTag.TryGetValue(tag, out var list)) byTag[tag] = list = [];
                                    list.Add(new { path, method, operationId = op["operationId"]?.GetValue<string>() });
                                }
                            }
                        }
                    }
                    pathsResult = byTag;
                }
                else
                {
                    pathsResult = paths?.Select(kv => new
                    {
                        path    = kv.Key,
                        methods = (kv.Value as System.Text.Json.Nodes.JsonObject)?
                                  .Select(m => m.Key)
                                  .Where(m => new[] { "get","post","put","delete","patch","options","head","trace" }.Contains(m))
                                  .ToList() ?? []
                    }).ToList() ?? [];
                }

                CliHelper.Ok(new { contract_id = id, paths = pathsResult, schemas });
            }
            catch (McpException ex) { Environment.ExitCode = CliHelper.Error(ex.Code, ex.Message, ex.Pointer); }
        }, idArg, tagsOpt);
        return cmd;
    }

    // ── get (get-fragment) ────────────────────────────────────────────────────

    private static Command GetCmd(ContractStore store)
    {
        var cmd       = new Command("get", "Read a specific subtree of a contract by JSON Pointer.");
        var idArg     = new Argument<string>("contract-id", "The contract ID.");
        var ptrArg    = new Argument<string>("pointer", "RFC 6901 JSON Pointer (e.g. /paths/~1pets).");
        var noResolve = new Option<bool>("--no-resolve", "Do not inline $ref targets.");
        var formatOpt = new Option<string>("--format", () => "json", "Output format: json | yaml.");
        cmd.AddArgument(idArg); cmd.AddArgument(ptrArg);
        cmd.AddOption(noResolve); cmd.AddOption(formatOpt);
        cmd.SetHandler((id, ptr, noRes, fmt) =>
        {
            try
            {
                var doc  = store.Get(id).Document;
                var node = ptr == "" ? doc : JsonPointerHelper.Navigate(doc, ptr);

                if (node is null)
                {
                    CliHelper.Ok(new { pointer = ptr, exists = false, content = (string?)null });
                    return;
                }

                List<string> unresolved = [];
                var content = noRes ? node.DeepClone() : JsonPointerHelper.ResolveRefsShallow(doc, node, out unresolved);
                CliHelper.Ok(new
                {
                    pointer     = ptr,
                    exists      = true,
                    content     = ContentSerializer.Serialise(content, fmt == "yaml" ? "yaml" : "json"),
                    ref_targets = unresolved.Distinct().ToArray()
                });
            }
            catch (McpException ex) { Environment.ExitCode = CliHelper.Error(ex.Code, ex.Message, ex.Pointer); }
        }, idArg, ptrArg, noResolve, formatOpt);
        return cmd;
    }

    // ── operation ─────────────────────────────────────────────────────────────

    private static Command OperationCmd(ContractStore store)
    {
        var cmd     = new Command("operation", "Read a single operation with $ref values resolved.");
        var idArg   = new Argument<string>("contract-id", "The contract ID.");
        var pathArg = new Argument<string>("path", "API path, e.g. /pets.");
        var methArg = new Argument<string>("method", "HTTP method, e.g. get.");
        cmd.AddArgument(idArg); cmd.AddArgument(pathArg); cmd.AddArgument(methArg);
        cmd.SetHandler((id, path, method) =>
        {
            try
            {
                var doc     = store.Get(id).Document;
                var escaped = path.TrimStart('/').Replace("~", "~0").Replace("/", "~1");
                var opNode  = JsonPointerHelper.Navigate(doc, $"/paths/~1{escaped}/{method.ToLowerInvariant()}");

                if (opNode is null)
                    throw new McpException("NOT_FOUND", $"Operation {method.ToUpper()} {path} not found.");

                List<string> unresolved = [];
                var resolved = JsonPointerHelper.ResolveRefsShallow(doc, opNode, out unresolved);
                CliHelper.Ok(new
                {
                    path, method = method.ToLowerInvariant(),
                    operation   = ContentSerializer.Serialise(resolved, "json"),
                    ref_targets = unresolved.Distinct().ToArray()
                });
            }
            catch (McpException ex) { Environment.ExitCode = CliHelper.Error(ex.Code, ex.Message, ex.Pointer); }
        }, idArg, pathArg, methArg);
        return cmd;
    }

    // ── schema ────────────────────────────────────────────────────────────────

    private static Command SchemaCmd(ContractStore store)
    {
        var cmd      = new Command("schema", "Read a named schema from components/schemas.");
        var idArg    = new Argument<string>("contract-id", "The contract ID.");
        var nameArg  = new Argument<string>("name", "Schema name.");
        var depthOpt = new Option<int>("--depth", () => 1, "How many $ref levels to inline (1-3).");
        cmd.AddArgument(idArg); cmd.AddArgument(nameArg); cmd.AddOption(depthOpt);
        cmd.SetHandler((id, name, depth) =>
        {
            try
            {
                var doc     = store.Get(id).Document;
                var ptr     = $"/components/schemas/{name}";
                var schNode = JsonPointerHelper.Navigate(doc, ptr)
                              ?? throw new McpException("NOT_FOUND", $"Schema '{name}' not found.", ptr);

                List<string> unresolved = [];
                var resolved = depth <= 1
                    ? JsonPointerHelper.ResolveRefsShallow(doc, schNode, out unresolved)
                    : JsonPointerHelper.ResolveRefsDepth(doc, schNode, Math.Clamp(depth, 1, 3), out unresolved);

                CliHelper.Ok(new
                {
                    name, pointer = ptr,
                    schema      = ContentSerializer.Serialise(resolved, "json"),
                    ref_targets = unresolved.Distinct().ToArray()
                });
            }
            catch (McpException ex) { Environment.ExitCode = CliHelper.Error(ex.Code, ex.Message, ex.Pointer); }
        }, idArg, nameArg, depthOpt);
        return cmd;
    }

    // ── search ────────────────────────────────────────────────────────────────

    private static Command SearchCmd(ContractStore store)
    {
        var cmd      = new Command("search", "Search paths, operations, and schemas for a keyword.");
        var idArg    = new Argument<string>("contract-id", "The contract ID.");
        var queryArg = new Argument<string>("query", "Search term or regex.");
        var scopeOpt = new Option<string>("--scope", () => "all", "Scope: all | paths | schemas.");
        var maxOpt   = new Option<int>("--max", () => 20, "Maximum results.");
        cmd.AddArgument(idArg); cmd.AddArgument(queryArg);
        cmd.AddOption(scopeOpt); cmd.AddOption(maxOpt);
        cmd.SetHandler((id, query, scope, max) =>
        {
            try
            {
                var doc     = store.Get(id).Document;
                var results = new List<object>();
                var rx      = new System.Text.RegularExpressions.Regex(query,
                              System.Text.RegularExpressions.RegexOptions.IgnoreCase |
                              System.Text.RegularExpressions.RegexOptions.Compiled);

                if ((scope == "all" || scope == "paths") && results.Count < max)
                {
                    if (doc["paths"] is System.Text.Json.Nodes.JsonObject paths)
                        foreach (var (path, item) in paths)
                        {
                            if (results.Count >= max) break;
                            if (item is not System.Text.Json.Nodes.JsonObject pi) continue;
                            foreach (var method in new[] { "get","post","put","delete","patch","options","head","trace" })
                            {
                                if (results.Count >= max) break;
                                var op = pi[method]; if (op is null) continue;
                                var text = op.ToString();
                                if (rx.IsMatch(path) || rx.IsMatch(text))
                                    results.Add(new { type = "operation", path, method, pointer = $"/paths/{path.TrimStart('/').Replace("~","~0").Replace("/","~1")}/{method}" });
                            }
                            if (results.Count < max && rx.IsMatch(path) && !results.Any(r => r.GetType().GetProperty("path")?.GetValue(r)?.ToString() == path))
                                results.Add(new { type = "path", path, pointer = $"/paths/{path.TrimStart('/').Replace("~","~0").Replace("/","~1")}" });
                        }
                }

                if ((scope == "all" || scope == "schemas") && results.Count < max)
                {
                    if (doc["components"]?["schemas"] is System.Text.Json.Nodes.JsonObject schemas)
                        foreach (var (name, schema) in schemas)
                        {
                            if (results.Count >= max) break;
                            var text = schema?.ToString() ?? "";
                            if (rx.IsMatch(name) || rx.IsMatch(text))
                                results.Add(new { type = "schema", name, pointer = $"/components/schemas/{name}" });
                        }
                }

                CliHelper.Ok(new { query, scope, total = results.Count, results });
            }
            catch (McpException ex) { Environment.ExitCode = CliHelper.Error(ex.Code, ex.Message, ex.Pointer); }
        }, idArg, queryArg, scopeOpt, maxOpt);
        return cmd;
    }

    // ── tag-operations ────────────────────────────────────────────────────────

    private static Command TagOperationsCmd(ContractStore store)
    {
        var cmd    = new Command("tag-operations", "List all operations associated with a specific tag.");
        var idArg  = new Argument<string>("contract-id", "The contract ID.");
        var tagArg = new Argument<string>("tag", "Tag name.");
        cmd.AddArgument(idArg); cmd.AddArgument(tagArg);
        cmd.SetHandler((id, tag) =>
        {
            try
            {
                var doc = store.Get(id).Document;
                var ops = new List<object>();

                if (doc["paths"] is System.Text.Json.Nodes.JsonObject paths)
                    foreach (var (path, item) in paths)
                    {
                        if (item is not System.Text.Json.Nodes.JsonObject pi) continue;
                        foreach (var method in new[] { "get","post","put","delete","patch","options","head","trace" })
                        {
                            var op = pi[method]; if (op is null) continue;
                            var tags = op["tags"]?.AsArray().Select(t => t?.GetValue<string>()).ToList() ?? [];
                            if (tags.Contains(tag))
                                ops.Add(new
                                {
                                    path, method,
                                    operationId = op["operationId"]?.GetValue<string>(),
                                    summary     = op["summary"]?.GetValue<string>()
                                });
                        }
                    }

                CliHelper.Ok(new { tag, operations = ops });
            }
            catch (McpException ex) { Environment.ExitCode = CliHelper.Error(ex.Code, ex.Message, ex.Pointer); }
        }, idArg, tagArg);
        return cmd;
    }

    // ── export ────────────────────────────────────────────────────────────────

    private static Command ExportCmd(ContractStore store)
    {
        var cmd       = new Command("export", "Export a full contract document.");
        var idArg     = new Argument<string>("contract-id", "The contract ID.");
        var formatOpt = new Option<string>("--format", () => "yaml", "Output format: yaml | json.");
        cmd.AddArgument(idArg); cmd.AddOption(formatOpt);
        cmd.SetHandler((id, fmt) =>
        {
            try
            {
                var entry   = store.Get(id);
                var content = ContentSerializer.Serialise(entry.Document, fmt == "json" ? "json" : "yaml");
                CliHelper.Ok(new { contract_id = id, format = fmt, content });
            }
            catch (McpException ex) { Environment.ExitCode = CliHelper.Error(ex.Code, ex.Message, ex.Pointer); }
        }, idArg, formatOpt);
        return cmd;
    }
}
