using FluentAssertions;
using OpenApiMcp.Tools;

namespace OpenApiMcp.IntegrationTests;

/// <summary>
/// Tests for get_fragment, get_operation, get_schema,
/// search_contract, and get_tag_operations.
/// </summary>
public sealed class FragmentToolTests(PetstoreFixture fx) : IClassFixture<PetstoreFixture>
{
    private FragmentTools Tools => new(fx.Store);

    // ── get_fragment ──────────────────────────────────────────────────────────

    [Fact]
    public void get_fragment_root_pointer_returns_whole_document()
    {
        var json = Json.Parse(Tools.get_fragment(PetstoreFixture.ContractId, pointer: ""));

        json.HasError().Should().BeFalse();
        json.Bool("exists").Should().BeTrue();
        var content = Json.Parse(json.Prop("content").GetString()!);
        content.Has("info").Should().BeTrue();
        content.Has("paths").Should().BeTrue();
    }

    [Fact]
    public void get_fragment_paths_pointer_returns_paths_object()
    {
        var json = Json.Parse(Tools.get_fragment(PetstoreFixture.ContractId, "/paths"));

        json.HasError().Should().BeFalse();
        json.Bool("exists").Should().BeTrue();
        var content = Json.Parse(json.Prop("content").GetString()!);
        content.Has("/pets").Should().BeTrue();
        content.Has("/pets/{petId}").Should().BeTrue();
    }

    [Fact]
    public void get_fragment_pet_schema_pointer_returns_schema()
    {
        var json = Json.Parse(Tools.get_fragment(PetstoreFixture.ContractId, "/components/schemas/Pet"));

        json.HasError().Should().BeFalse();
        json.Bool("exists").Should().BeTrue();
        var content = Json.Parse(json.Prop("content").GetString()!);
        content.Str("type").Should().Be("object");
        content.Prop("properties").Has("id").Should().BeTrue();
        content.Prop("properties").Has("name").Should().BeTrue();
    }

    [Fact]
    public void get_fragment_petlist_schema_resolves_item_ref_one_level_deep()
    {
        // PetList has items.$ref=#/components/schemas/Pet; with resolve_refs=true, Pet should be inlined
        var json = Json.Parse(Tools.get_fragment(PetstoreFixture.ContractId, "/components/schemas/PetList", resolve_refs: true));

        json.HasError().Should().BeFalse();
        // The original $ref should be replaced by the Pet object
        var content = Json.Parse(json.Prop("content").GetString()!);
        // After resolution, items should have "type":"object" (the Pet schema)
        content.Prop("items").Str("type").Should().Be("object", "the $ref to Pet was inlined");
    }

    [Fact]
    public void get_fragment_with_resolve_refs_false_preserves_dollar_ref()
    {
        var json = Json.Parse(Tools.get_fragment(PetstoreFixture.ContractId, "/components/schemas/PetList", resolve_refs: false));

        json.HasError().Should().BeFalse();
        var refTargets = json.Prop("ref_targets").EnumerateArray().Select(e => e.GetString()).ToList();
        refTargets.Should().Contain("#/components/schemas/Pet");
    }

    [Fact]
    public void get_fragment_nonexistent_pointer_returns_exists_false()
    {
        var json = Json.Parse(Tools.get_fragment(PetstoreFixture.ContractId, "/paths/~1does-not-exist/get"));

        json.HasError().Should().BeFalse();
        json.Bool("exists").Should().BeFalse();
    }

    [Fact]
    public void get_fragment_format_yaml_returns_yaml_content()
    {
        var json = Json.Parse(Tools.get_fragment(PetstoreFixture.ContractId, "/info", format: "yaml"));

        json.HasError().Should().BeFalse();
        var content = json.Prop("content").GetString()!;
        content.Should().Contain("title:", "YAML uses 'key:' syntax");
        content.Should().NotStartWith("{", "JSON starts with '{', YAML does not");
    }

    // ── get_operation ─────────────────────────────────────────────────────────

    [Fact]
    public void get_operation_list_pets_returns_operation_details()
    {
        var json = Json.Parse(Tools.get_operation(PetstoreFixture.ContractId, "/pets", "get"));

        json.HasError().Should().BeFalse();
        json.Str("pointer").Should().Be("/paths/~1pets/get");
        var op = Json.Parse(json.Prop("operation").GetString()!);
        op.Str("operationId").Should().Be("listPets");
        op.Str("summary").Should().Be("List all pets");
    }

    [Fact]
    public void get_operation_get_pet_returns_operation_with_parameters()
    {
        var json = Json.Parse(Tools.get_operation(PetstoreFixture.ContractId, "/pets/{petId}", "get"));

        json.HasError().Should().BeFalse();
        var op = Json.Parse(json.Prop("operation").GetString()!);
        op.Str("operationId").Should().Be("getPet");

        var parameters = op.Prop("parameters").EnumerateArray().ToList();
        parameters.Should().ContainSingle(p => p.Str("name") == "petId");
    }

    [Fact]
    public void get_operation_nonexistent_method_returns_error()
    {
        var json = Json.Parse(Tools.get_operation(PetstoreFixture.ContractId, "/pets", "delete"));

        json.HasError().Should().BeTrue();
        json.ErrorCode().Should().Be("INVALID_POINTER");
    }

    // ── get_schema ────────────────────────────────────────────────────────────

    [Fact]
    public void get_schema_pet_returns_schema_with_pointer()
    {
        var json = Json.Parse(Tools.get_schema(PetstoreFixture.ContractId, "Pet"));

        json.HasError().Should().BeFalse();
        json.Str("pointer").Should().Be("/components/schemas/Pet");
        var schema = Json.Parse(json.Prop("content").GetString()!);
        schema.Str("type").Should().Be("object");
    }

    [Fact]
    public void get_schema_petlist_depth1_inlines_pet_schema()
    {
        var json = Json.Parse(Tools.get_schema(PetstoreFixture.ContractId, "PetList", resolve_depth: 1));

        json.HasError().Should().BeFalse();
        var schema = Json.Parse(json.Prop("content").GetString()!);
        // items should be the inlined Pet schema, not a $ref
        schema.Prop("items").Has("$ref").Should().BeFalse("$ref should be resolved");
        schema.Prop("items").Str("type").Should().Be("object");
    }

    [Fact]
    public void get_schema_unknown_name_returns_error()
    {
        var json = Json.Parse(Tools.get_schema(PetstoreFixture.ContractId, "NoSuchSchema"));

        json.HasError().Should().BeTrue();
        json.ErrorCode().Should().Be("INVALID_POINTER");
    }

    // ── search_contract ───────────────────────────────────────────────────────

    [Fact]
    public void search_contract_keyword_pets_finds_operations_and_schemas()
    {
        var json = Json.Parse(Tools.search_contract(PetstoreFixture.ContractId, "pets"));

        json.HasError().Should().BeFalse();
        var results = json.Prop("results").EnumerateArray().ToList();
        results.Should().NotBeEmpty();
    }

    [Fact]
    public void search_contract_scope_schemas_only_returns_schemas()
    {
        var json = Json.Parse(Tools.search_contract(PetstoreFixture.ContractId, "Pet", scope: "schemas"));

        json.HasError().Should().BeFalse();
        var results = json.Prop("results").EnumerateArray().ToList();
        results.Should().NotBeEmpty();
        results.All(r => r.Str("kind") == "schema").Should().BeTrue();
    }

    [Fact]
    public void search_contract_scope_paths_only_returns_paths_and_operations()
    {
        var json = Json.Parse(Tools.search_contract(PetstoreFixture.ContractId, "pets", scope: "paths"));

        json.HasError().Should().BeFalse();
        var results = json.Prop("results").EnumerateArray().ToList();
        results.Should().NotBeEmpty();
        results.All(r => r.Str("kind") == "operation" || r.Str("kind") == "path")
               .Should().BeTrue();
    }

    [Fact]
    public void search_contract_max_results_limits_output()
    {
        var json = Json.Parse(Tools.search_contract(PetstoreFixture.ContractId, "e", max_results: 1));

        json.HasError().Should().BeFalse();
        json.Prop("results").EnumerateArray().Count().Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void search_contract_no_match_returns_empty_results()
    {
        var json = Json.Parse(Tools.search_contract(PetstoreFixture.ContractId, "zzz-no-match-xyz"));

        json.HasError().Should().BeFalse();
        json.Prop("results").EnumerateArray().Should().BeEmpty();
    }

    // ── get_tag_operations ────────────────────────────────────────────────────

    [Fact]
    public void get_tag_operations_pets_returns_both_operations()
    {
        var json = Json.Parse(Tools.get_tag_operations(PetstoreFixture.ContractId, "pets"));

        json.HasError().Should().BeFalse();
        json.Str("tag").Should().Be("pets");

        var ops = json.Prop("operations").EnumerateArray().ToList();
        ops.Should().HaveCount(2);
        ops.Select(o => o.Str("operationId")).Should().Contain("listPets").And.Contain("getPet");
    }

    [Fact]
    public void get_tag_operations_unknown_tag_returns_empty_list()
    {
        var json = Json.Parse(Tools.get_tag_operations(PetstoreFixture.ContractId, "orders"));

        json.HasError().Should().BeFalse();
        json.Prop("operations").EnumerateArray().Should().BeEmpty();
    }
}
