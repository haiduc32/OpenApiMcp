using FluentAssertions;
using OpenApiMcp.Tools;

namespace OpenApiMcp.IntegrationTests;

/// <summary>
/// Tests for set_fragment, patch_fragment, delete_fragment,
/// rename_schema, add_path, add_operation, and add_schema.
/// Each test opens its own session to stay isolated.
/// </summary>
public sealed class WriteToolTests(PetstoreFixture fx) : IClassFixture<PetstoreFixture>
{
    private WriteTools Tools => new(fx.Sessions);

    // ── set_fragment ──────────────────────────────────────────────────────────

    [Fact]
    public void set_fragment_creates_new_path_and_reports_created()
    {
        var sid = fx.OpenSession();
        var newPath = """
            {
              "get": {
                "operationId": "listOrders",
                "summary": "List orders",
                "responses": { "200": { "description": "OK" } }
              }
            }
            """;

        var json = Json.Parse(Tools.set_fragment(sid, "/paths/~1orders", newPath));

        json.HasError().Should().BeFalse();
        json.Str("action").Should().Be("created");
        json.Bool("prev_existed").Should().BeFalse();

        // Verify the node actually exists in the staged document
        var session = fx.Sessions.Get(sid);
        OpenApiMcp.Services.JsonPointerHelper.Navigate(session.StagedDocument, "/paths/~1orders")
            .Should().NotBeNull();
    }

    [Fact]
    public void set_fragment_replaces_existing_node_and_reports_replaced()
    {
        var sid = fx.OpenSession();
        var updatedInfo = """{ "title": "Updated Petstore", "version": "2.0.0" }""";

        var json = Json.Parse(Tools.set_fragment(sid, "/info", updatedInfo));

        json.HasError().Should().BeFalse();
        json.Str("action").Should().Be("replaced");
        json.Bool("prev_existed").Should().BeTrue();
    }

    [Fact]
    public void set_fragment_yaml_content_is_accepted()
    {
        var sid = fx.OpenSession();
        const string yamlSchema = """
            type: object
            properties:
              name:
                type: string
            """;

        var json = Json.Parse(Tools.set_fragment(sid, "/components/schemas/NewSchema", yamlSchema, format: "yaml"));

        json.HasError().Should().BeFalse();
        json.Str("action").Should().Be("created");
    }

    [Fact]
    public void set_fragment_unknown_session_returns_error()
    {
        var json = Json.Parse(Tools.set_fragment("bad-session-id", "/info", "{}"));

        json.HasError().Should().BeTrue();
        json.ErrorCode().Should().Be("SESSION_NOT_FOUND");
    }

    // ── patch_fragment ────────────────────────────────────────────────────────

    [Fact]
    public void patch_fragment_merge_updates_info_fields()
    {
        var sid  = fx.OpenSession();
        var patch = """{ "title": "Patched Petstore API", "contact": { "name": "Team" } }""";

        var json = Json.Parse(Tools.patch_fragment(sid, "/info", patch, "merge"));

        json.HasError().Should().BeFalse();
        json.Str("patch_type").Should().Be("merge");

        // Verify in staged document
        var session = fx.Sessions.Get(sid);
        var info    = OpenApiMcp.Services.JsonPointerHelper.Navigate(session.StagedDocument, "/info")!;
        info["title"]!.GetValue<string>().Should().Be("Patched Petstore API");
        // version should still be present (merge patch preserves unmentioned keys)
        info["version"].Should().NotBeNull();
    }

    [Fact]
    public void patch_fragment_json_patch_add_operation_succeeds()
    {
        var sid = fx.OpenSession();
        var patch = """
            [
              {
                "op": "add",
                "path": "/paths/~1pets/post",
                "value": {
                  "operationId": "createPet",
                  "summary": "Create a pet",
                  "responses": { "201": { "description": "Created" } }
                }
              }
            ]
            """;

        var json = Json.Parse(Tools.patch_fragment(sid, "", patch, "json-patch"));

        json.HasError().Should().BeFalse();
        json.Str("patch_type").Should().Be("json-patch");

        var session = fx.Sessions.Get(sid);
        OpenApiMcp.Services.JsonPointerHelper.Navigate(session.StagedDocument, "/paths/~1pets/post")
            .Should().NotBeNull("POST /pets was added via JSON Patch");
    }

    [Fact]
    public void patch_fragment_json_patch_remove_operation_succeeds()
    {
        var sid = fx.OpenSession();
        var patch = """[{ "op": "remove", "path": "/tags" }]""";

        var json = Json.Parse(Tools.patch_fragment(sid, "", patch, "json-patch"));

        json.HasError().Should().BeFalse();

        var session = fx.Sessions.Get(sid);
        OpenApiMcp.Services.JsonPointerHelper.Navigate(session.StagedDocument, "/tags")
            .Should().BeNull("tags array was removed");
    }

    [Fact]
    public void patch_fragment_invalid_patch_type_returns_error()
    {
        var sid  = fx.OpenSession();
        var json = Json.Parse(Tools.patch_fragment(sid, "", "{}", "bogus-type"));

        json.HasError().Should().BeTrue();
        json.ErrorCode().Should().Be("INVALID_CONTENT");
    }

    // ── delete_fragment ───────────────────────────────────────────────────────

    [Fact]
    public void delete_fragment_removes_existing_node()
    {
        var sid  = fx.OpenSession();
        var json = Json.Parse(Tools.delete_fragment(sid, "/tags"));

        json.HasError().Should().BeFalse();
        json.Bool("deleted").Should().BeTrue();

        var session = fx.Sessions.Get(sid);
        OpenApiMcp.Services.JsonPointerHelper.Navigate(session.StagedDocument, "/tags")
            .Should().BeNull();
    }

    [Fact]
    public void delete_fragment_nonexistent_node_returns_deleted_false()
    {
        var sid  = fx.OpenSession();
        var json = Json.Parse(Tools.delete_fragment(sid, "/paths/~1does-not-exist"));

        json.HasError().Should().BeFalse();
        json.Bool("deleted").Should().BeFalse();
    }

    // ── rename_schema ─────────────────────────────────────────────────────────

    [Fact]
    public void rename_schema_moves_schema_and_updates_all_refs()
    {
        var sid  = fx.OpenSession();
        var json = Json.Parse(Tools.rename_schema(sid, "Pet", "Animal"));

        json.HasError().Should().BeFalse();
        json.Str("old_pointer").Should().Be("/components/schemas/Pet");
        json.Str("new_pointer").Should().Be("/components/schemas/Animal");
        json.Int("refs_updated").Should().BeGreaterThan(0, "at least PetList.items.$ref points at Pet");

        var session = fx.Sessions.Get(sid);
        // Old pointer gone
        OpenApiMcp.Services.JsonPointerHelper.Navigate(session.StagedDocument, "/components/schemas/Pet")
            .Should().BeNull();
        // New pointer present
        OpenApiMcp.Services.JsonPointerHelper.Navigate(session.StagedDocument, "/components/schemas/Animal")
            .Should().NotBeNull();
    }

    [Fact]
    public void rename_schema_unknown_old_name_returns_error()
    {
        var sid  = fx.OpenSession();
        var json = Json.Parse(Tools.rename_schema(sid, "NoSuchSchema", "NewName"));

        json.HasError().Should().BeTrue();
        json.ErrorCode().Should().Be("INVALID_POINTER");
    }

    // ── add_path ──────────────────────────────────────────────────────────────

    [Fact]
    public void add_path_creates_new_path_with_operations()
    {
        var sid = fx.OpenSession();
        var pathItem = """
            {
              "get":  { "operationId": "listUsers",  "summary": "List users",  "responses": { "200": { "description": "OK" } } },
              "post": { "operationId": "createUser", "summary": "Create user", "responses": { "201": { "description": "Created" } } }
            }
            """;

        var json = Json.Parse(Tools.add_path(sid, "/users", pathItem));

        json.HasError().Should().BeFalse();
        json.Str("pointer").Should().Be("/paths/~1users");
        json.Str("action").Should().Be("created");

        var ops = json.Prop("operations").EnumerateArray().Select(e => e.GetString()).ToList();
        ops.Should().Contain("get").And.Contain("post");
    }

    [Fact]
    public void add_path_replaces_existing_path()
    {
        var sid = fx.OpenSession();
        var replacement = """
            {
              "get": { "operationId": "listPetsV2", "summary": "List pets v2", "responses": { "200": { "description": "OK" } } }
            }
            """;

        var json = Json.Parse(Tools.add_path(sid, "/pets", replacement));

        json.HasError().Should().BeFalse();
        json.Str("action").Should().Be("replaced");
    }

    // ── add_operation ─────────────────────────────────────────────────────────

    [Fact]
    public void add_operation_post_to_pets_creates_operation()
    {
        var sid = fx.OpenSession();
        var operation = """
            {
              "operationId": "createPet",
              "summary": "Create a new pet",
              "requestBody": {
                "required": true,
                "content": { "application/json": { "schema": { "$ref": "#/components/schemas/Pet" } } }
              },
              "responses": { "201": { "description": "Created" } }
            }
            """;

        var json = Json.Parse(Tools.add_operation(sid, "/pets", "post", operation));

        json.HasError().Should().BeFalse();
        json.Str("pointer").Should().Be("/paths/~1pets/post");
        json.Str("action").Should().Be("created");
    }

    [Fact]
    public void add_operation_updates_existing_operation()
    {
        var sid = fx.OpenSession();
        var updatedOp = """
            {
              "operationId": "listPets",
              "summary": "List all pets (updated)",
              "responses": { "200": { "description": "OK" } }
            }
            """;

        var json = Json.Parse(Tools.add_operation(sid, "/pets", "get", updatedOp));

        json.HasError().Should().BeFalse();
        json.Str("action").Should().Be("replaced");
    }

    // ── add_schema ────────────────────────────────────────────────────────────

    [Fact]
    public void add_schema_creates_new_schema_in_components()
    {
        var sid = fx.OpenSession();
        const string schema = """
            {
              "type": "object",
              "required": ["id", "total"],
              "properties": {
                "id":    { "type": "integer" },
                "total": { "type": "number" }
              }
            }
            """;

        var json = Json.Parse(Tools.add_schema(sid, "Order", schema));

        json.HasError().Should().BeFalse();
        json.Str("pointer").Should().Be("/components/schemas/Order");
        json.Str("action").Should().Be("created");

        var session = fx.Sessions.Get(sid);
        OpenApiMcp.Services.JsonPointerHelper.Navigate(session.StagedDocument, "/components/schemas/Order")
            .Should().NotBeNull();
    }

    [Fact]
    public void add_schema_replaces_existing_schema()
    {
        var sid = fx.OpenSession();
        const string newPet = """{ "type": "object", "properties": { "name": { "type": "string" } } }""";

        var json = Json.Parse(Tools.add_schema(sid, "Pet", newPet));

        json.HasError().Should().BeFalse();
        json.Str("action").Should().Be("replaced");
    }
}
