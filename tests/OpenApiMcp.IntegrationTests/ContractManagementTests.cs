using System.Text.Json;
using FluentAssertions;
using OpenApiMcp.Tools;

namespace OpenApiMcp.IntegrationTests;

/// <summary>
/// Tests for list_contracts, get_contract_info, get_contract_index,
/// register_contract, and unregister_contract.
/// </summary>
public sealed class ContractManagementTests(PetstoreFixture fx) : IClassFixture<PetstoreFixture>
{
    private ContractManagementTools Tools => new(fx.Store);

    // ── list_contracts ────────────────────────────────────────────────────────

    [Fact]
    public void list_contracts_returns_petstore_entry()
    {
        var json = Json.Parse(Tools.list_contracts());

        json.HasError().Should().BeFalse();
        var contracts = json.Prop("contracts").EnumerateArray().ToList();
        contracts.Should().NotBeEmpty();

        var petstore = contracts.First(c => c.Str("id") == PetstoreFixture.ContractId);
        petstore.Str("title").Should().Be("Petstore Sample API");
        petstore.Str("openapi").Should().Be("3.0.3");
        petstore.Str("version").Should().Be("1.0.0");
        petstore.Int("size_lines").Should().BeGreaterThan(0);
    }

    // ── get_contract_info ─────────────────────────────────────────────────────

    [Fact]
    public void get_contract_info_returns_info_servers_and_tags()
    {
        var json = Json.Parse(Tools.get_contract_info(PetstoreFixture.ContractId));

        json.HasError().Should().BeFalse();
        json.Has("info").Should().BeTrue();
        json.Has("servers").Should().BeTrue();
        json.Has("tags").Should().BeTrue();
        json.Prop("info").Str("title").Should().Be("Petstore Sample API");
    }

    [Fact]
    public void get_contract_info_unknown_contract_returns_error()
    {
        var json = Json.Parse(Tools.get_contract_info("does-not-exist"));

        json.HasError().Should().BeTrue();
        json.ErrorCode().Should().Be("CONTRACT_NOT_FOUND");
    }

    // ── get_contract_index ────────────────────────────────────────────────────

    [Fact]
    public void get_contract_index_lists_paths_and_schemas()
    {
        var json = Json.Parse(Tools.get_contract_index(PetstoreFixture.ContractId));

        json.HasError().Should().BeFalse();

        var paths = json.Prop("paths").EnumerateArray().ToList();
        paths.Should().HaveCountGreaterThanOrEqualTo(2);

        var pathValues = paths.Select(p => p.Str("path")).ToList();
        pathValues.Should().Contain("/pets");
        pathValues.Should().Contain("/pets/{petId}");

        var schemas = json.Prop("components").Prop("schemas").EnumerateArray().ToList();
        schemas.Select(s => s.GetString()).Should().Contain("Pet").And.Contain("PetList");
    }

    [Fact]
    public void get_contract_index_include_tags_groups_by_pets_tag()
    {
        var json = Json.Parse(Tools.get_contract_index(PetstoreFixture.ContractId, include_tags: true));

        json.HasError().Should().BeFalse();
        json.Has("paths_by_tag").Should().BeTrue("include_tags groups paths under tag names");
        json.Prop("paths_by_tag").Has("pets").Should().BeTrue();
    }

    [Fact]
    public void get_contract_index_each_path_has_methods_with_metadata()
    {
        var json = Json.Parse(Tools.get_contract_index(PetstoreFixture.ContractId));

        var petsPath = json.Prop("paths").EnumerateArray()
            .First(p => p.Str("path") == "/pets");

        var methods = petsPath.Prop("methods").EnumerateArray().ToList();
        methods.Should().ContainSingle(m => m.Str("method") == "get");

        var getMethod = methods.First(m => m.Str("method") == "get");
        getMethod.Str("operationId").Should().Be("listPets");
        getMethod.Str("summary").Should().Be("List all pets");
    }

    // ── register_contract ─────────────────────────────────────────────────────

    [Fact]
    public void register_contract_inline_content_adds_new_contract()
    {
        const string minimalYaml = """
            openapi: "3.1.0"
            info:
              title: Minimal Test API
              version: "0.1.0"
            paths: {}
            """;

        // Use a separate store so this doesn't pollute the shared fixture
        var store = new OpenApiMcp.Services.ContractStore();
        var tools = new ContractManagementTools(store);

        var json = Json.Parse(tools.register_contract(content: minimalYaml, format: "yaml"));

        json.HasError().Should().BeFalse();
        json.Str("title").Should().Be("Minimal Test API");
        json.Str("version").Should().Be("0.1.0");
        json.Str("openapi").Should().Be("3.1.0");
        json.Str("id").Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void register_contract_without_file_or_content_returns_error()
    {
        var json = Json.Parse(Tools.register_contract());

        json.HasError().Should().BeTrue();
        json.ErrorCode().Should().Be("INVALID_CONTENT");
    }

    // ── unregister_contract ───────────────────────────────────────────────────

    [Fact]
    public void unregister_contract_removes_the_contract()
    {
        // Isolate from shared fixture
        var store = new OpenApiMcp.Services.ContractStore();
        var tools = new ContractManagementTools(store);
        store.RegisterFromContent("temp-api", PetstoreFixture.PetstoreYaml, "yaml");

        var remove = Json.Parse(tools.unregister_contract("temp-api"));
        remove.HasError().Should().BeFalse();
        remove.Bool("removed").Should().BeTrue();

        // Contract must no longer appear in list
        var list = Json.Parse(tools.list_contracts());
        var ids  = list.Prop("contracts").EnumerateArray().Select(c => c.Str("id")).ToList();
        ids.Should().NotContain("temp-api");
    }

    [Fact]
    public void unregister_contract_unknown_id_returns_error()
    {
        var json = Json.Parse(Tools.unregister_contract("ghost-api"));

        json.HasError().Should().BeTrue();
        json.ErrorCode().Should().Be("CONTRACT_NOT_FOUND");
    }
}
