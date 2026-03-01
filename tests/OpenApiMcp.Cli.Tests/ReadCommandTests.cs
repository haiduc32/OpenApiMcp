using System.Text.Json;
using FluentAssertions;
using OpenApiMcp.Services;

namespace OpenApiMcp.Cli.Tests;

[Collection("cli")]
public sealed class ReadCommandTests : IClassFixture<CliFixture>
{
    private readonly ContractStore _store;

    public ReadCommandTests(CliFixture fixture) => _store = fixture.Store;

    private Task<CliResult> Run(params string[] args) => CliRunner.RunAsync(_store, args);

    [Fact]
    public async Task Contracts_ReturnsAtLeastOneContract()
    {
        var result = await Run("contracts");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("contracts").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Contracts_IncludesRegisteredId()
    {
        var result = await Run("contracts");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        var ids = json.RootElement.GetProperty("contracts")
                      .EnumerateArray()
                      .Select(e => e.GetProperty("id").GetString())
                      .ToList();
        ids.Should().Contain(CliFixture.ContractId);
    }

    [Fact]
    public async Task Info_ReturnsInfoBlock()
    {
        var result = await Run("info", CliFixture.ContractId);

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("contract_id").GetString().Should().Be(CliFixture.ContractId);
        json.RootElement.GetProperty("info").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task Info_ErrorForUnknownContract()
    {
        var result = await Run("info", "does-not-exist");

        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task Index_ReturnsPaths()
    {
        var result = await Run("index", CliFixture.ContractId);

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("paths").ValueKind.Should().Be(JsonValueKind.Array);
        json.RootElement.GetProperty("schemas").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Index_WithTags_GroupsByTag()
    {
        var result = await Run("index", CliFixture.ContractId, "--tags");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        // grouped by tag → paths is an object, not an array
        json.RootElement.GetProperty("paths").ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task Schema_ReturnsSchemaForKnownName()
    {
        var result = await Run("schema", CliFixture.ContractId, "Pet");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("name").GetString().Should().Be("Pet");
        json.RootElement.GetProperty("pointer").GetString().Should().Be("/components/schemas/Pet");
    }

    [Fact]
    public async Task Schema_ErrorForUnknownName()
    {
        var result = await Run("schema", CliFixture.ContractId, "DoesNotExist");

        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public async Task Get_ReturnsNodeAtPointer()
    {
        var result = await Run("get", CliFixture.ContractId, "/info");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("exists").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Get_ReturnsFalseForMissingPointer()
    {
        var result = await Run("get", CliFixture.ContractId, "/does/not/exist");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("exists").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Operation_ReturnsOperationNode()
    {
        var result = await Run("operation", CliFixture.ContractId, "/pets", "get");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("method").GetString().Should().Be("get");
    }

    [Fact]
    public async Task Search_FindsResultsByKeyword()
    {
        var result = await Run("search", CliFixture.ContractId, "Pet");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("total").GetInt32().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task TagOperations_ReturnsOpsForTag()
    {
        var result = await Run("tag-operations", CliFixture.ContractId, "pets");

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("operations").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Export_ReturnsYamlByDefault()
    {
        var result = await Run("export", CliFixture.ContractId);

        result.ExitCode.Should().Be(0);
        var json = JsonDocument.Parse(result.Stdout);
        json.RootElement.GetProperty("format").GetString().Should().Be("yaml");
        json.RootElement.GetProperty("content").GetString().Should().Contain("openapi:");
    }
}
