using System.Text.Json;
using FluentAssertions;
using OpenApiMcp.Services;

namespace OpenApiMcp.Cli.Tests;

/// <summary>
/// Write command tests. Each test creates its own fresh ContractStore so write
/// operations cannot bleed between tests.
/// </summary>
[Collection("cli")]
public sealed class WriteCommandTests
{
    private static ContractStore FreshStore()
    {
        var store = new ContractStore();
        store.RegisterFromContent(CliFixture.ContractId, CliFixture.PetstoreYaml, "yaml");
        return store;
    }

    private static Task<CliResult> Run(ContractStore store, params string[] args)
        => CliRunner.RunAsync(store, args);

    // ── add-schema ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddSchema_CreatesSchema()
    {
        var store  = FreshStore();
        var result = await Run(store, "add-schema", CliFixture.ContractId, "Widget",
            """{"type":"object","properties":{"id":{"type":"integer"}}}""");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("action").GetString().Should().Be("created");
        json.RootElement.GetProperty("pointer").GetString().Should().Be("/components/schemas/Widget");
    }

    [Fact]
    public async Task AddSchema_ReplacesExistingSchema()
    {
        var store = FreshStore();
        await Run(store, "add-schema", CliFixture.ContractId, "Widget",
            """{"type":"object"}""");

        var result = await Run(store, "add-schema", CliFixture.ContractId, "Widget",
            """{"type":"string"}""");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("action").GetString().Should().Be("replaced");
    }

    // ── delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_RemovesExistingNode()
    {
        var store = FreshStore();
        // Add something first
        await Run(store, "add-schema", CliFixture.ContractId, "Temp",
            """{"type":"string"}""");

        var result = await Run(store, "delete", CliFixture.ContractId, "/components/schemas/Temp");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("deleted").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Delete_ReturnsFalseForMissingPointer()
    {
        var store  = FreshStore();
        var result = await Run(store, "delete", CliFixture.ContractId,
            "/components/schemas/NeverExisted");

        // Delete of non-existent node commits fine but reports deleted=false
        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("deleted").GetBoolean().Should().BeFalse();
    }

    // ── set ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Set_CreatesNewNode()
    {
        var store  = FreshStore();
        var result = await Run(store, "set", CliFixture.ContractId, "/info/x-owner",
            """{"team":"platform"}""");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("action").GetString().Should().Be("created");
    }

    [Fact]
    public async Task Set_ReplacesExistingNode()
    {
        var store  = FreshStore();
        var result = await Run(store, "set", CliFixture.ContractId, "/info/title",
            "\"Updated Title\"");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("action").GetString().Should().Be("replaced");
    }

    // ── rename-schema ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RenameSchema_MovesSchemaAndUpdatesRefs()
    {
        var store  = FreshStore();
        var result = await Run(store, "rename-schema", CliFixture.ContractId, "PetList", "AnimalList");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("new_pointer").GetString()
            .Should().Be("/components/schemas/AnimalList");
        // PetList is referenced by /pets GET response → at least 1 ref updated
        json.RootElement.GetProperty("refs_updated").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RenameSchema_ErrorForUnknownSchema()
    {
        var store  = FreshStore();
        var result = await Run(store, "rename-schema", CliFixture.ContractId, "NoSuch", "Whatever");

        result.ExitCode.Should().NotBe(0);
    }

    // ── add-path ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddPath_CreatesNewPath()
    {
        var store  = FreshStore();
        var result = await Run(store, "add-path", CliFixture.ContractId, "/orders",
            """{"get":{"operationId":"listOrders","summary":"List orders","responses":{"200":{"description":"OK"}}}}""");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("action").GetString().Should().Be("created");
        json.RootElement.GetProperty("operations").GetArrayLength().Should().Be(1);
    }

    // ── add-operation ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AddOperation_CreatesOperation()
    {
        var store  = FreshStore();
        var result = await Run(store, "add-operation", CliFixture.ContractId, "/pets", "post",
            """{"operationId":"createPet","summary":"Create pet","responses":{"201":{"description":"Created"}}}""");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("action").GetString().Should().Be("created");
        json.RootElement.GetProperty("pointer").GetString().Should().Contain("~1pets");
    }

    // ── patch ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Patch_MergePatchUpdatesInfo()
    {
        var store  = FreshStore();
        var result = await Run(store, "patch", CliFixture.ContractId, "/info",
            """{"x-custom":"value"}""");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("patch_type").GetString().Should().Be("merge");
    }
}
