using FluentAssertions;
using OpenApiMcp.Tools;

namespace OpenApiMcp.IntegrationTests;

/// <summary>
/// Tests for validate_session, diff_session, commit_session, and export_contract.
/// </summary>
public sealed class ValidationToolTests(PetstoreFixture fx) : IClassFixture<PetstoreFixture>
{
    private ValidationTools Tools => new(fx.Store, fx.Sessions);
    private WriteTools       Write => new(fx.Sessions);

    // ── validate_session ──────────────────────────────────────────────────────

    [Fact]
    public void validate_session_unmodified_petstore_is_valid()
    {
        var sid  = fx.OpenSession();
        var json = Json.Parse(Tools.validate_session(sid));

        json.HasError().Should().BeFalse();
        json.Bool("valid").Should().BeTrue();
        json.Prop("errors").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public void validate_session_strict_mode_returns_valid_shape()
    {
        var sid  = fx.OpenSession();
        var json = Json.Parse(Tools.validate_session(sid, strict: true));

        json.HasError().Should().BeFalse();
        // Result must have valid/errors/warnings regardless of strict outcome
        json.Has("valid").Should().BeTrue();
        json.Has("errors").Should().BeTrue();
        json.Has("warnings").Should().BeTrue();
    }

    [Fact]
    public void validate_session_unknown_session_returns_error()
    {
        var json = Json.Parse(Tools.validate_session("no-such-session"));

        json.HasError().Should().BeTrue();
        json.ErrorCode().Should().Be("SESSION_NOT_FOUND");
    }

    [Fact]
    public void validate_session_after_removing_required_openapi_field_reports_invalid()
    {
        var sid = fx.OpenSession();
        // Removing the 'info' key makes the document fail OpenAPI validation
        Write.delete_fragment(sid, "/info");
        var json = Json.Parse(Tools.validate_session(sid));

        json.HasError().Should().BeFalse();
        // Removing /info should produce validation errors
        json.Bool("valid").Should().BeFalse();
        json.Prop("errors").GetArrayLength().Should().BeGreaterThan(0);
    }

    // ── diff_session ──────────────────────────────────────────────────────────

    [Fact]
    public void diff_session_no_changes_returns_no_changes_text()
    {
        var sid  = fx.OpenSession();
        var json = Json.Parse(Tools.diff_session(sid));

        json.HasError().Should().BeFalse();
        json.Int("patch_count").Should().Be(0);
        json.Str("diff").Should().Be("No changes.");
    }

    [Fact]
    public void diff_session_summary_shows_modified_key_after_change()
    {
        var sid = fx.OpenSession();
        Write.set_fragment(sid, "/info", """{ "title": "Changed", "version": "99.0.0" }""");

        var json = Json.Parse(Tools.diff_session(sid, format: "summary"));

        json.HasError().Should().BeFalse();
        json.Int("patch_count").Should().Be(1);
        json.Str("diff").Should().Contain("info").And.Contain("modified");
    }

    [Fact]
    public void diff_session_full_format_returns_plus_minus_lines()
    {
        var sid = fx.OpenSession();
        Write.set_fragment(sid, "/info/title", "\"Full Diff Title\"");

        var json = Json.Parse(Tools.diff_session(sid, format: "full"));

        json.HasError().Should().BeFalse();
        var diff = json.Str("diff");

        diff.Should().NotBeNullOrEmpty();

        // Full diff should contain some +/- marker lines
        (diff.Contains("+") || diff.Contains("-")).Should().BeTrue("full diff should have changed lines");
    }

    [Fact]
    public void diff_session_scoped_to_pointer_only_diffs_subtree()
    {
        var sid = fx.OpenSession();
        Write.set_fragment(sid, "/info/title", "\"Scoped Change\"");

        var json = Json.Parse(Tools.diff_session(sid, format: "summary", pointer: "/info"));

        json.HasError().Should().BeFalse();
        // scoping to /info; since info was replaced the diff content is returned
        json.Has("diff").Should().BeTrue();
    }

    [Fact]
    public void diff_session_unknown_session_returns_error()
    {
        var json = Json.Parse(Tools.diff_session("ghost-session"));

        json.HasError().Should().BeTrue();
        json.ErrorCode().Should().Be("SESSION_NOT_FOUND");
    }

    // ── commit_session ────────────────────────────────────────────────────────

    [Fact]
    public void commit_session_persists_staged_changes_to_store()
    {
        var sid = fx.OpenSession();
        Write.set_fragment(sid, "/info/x-committed", "\"yes\"");

        var json = Json.Parse(Tools.commit_session(sid, message: "test commit"));

        json.HasError().Should().BeFalse();
        json.Bool("committed").Should().BeTrue();
        json.Str("contract_id").Should().Be(PetstoreFixture.ContractId);
        json.Has("commit_ref").Should().BeTrue();

        // Verify the change is actually in the committed document
        var entry = fx.Store.Get(PetstoreFixture.ContractId);
        OpenApiMcp.Services.JsonPointerHelper.Navigate(entry.Document, "/info/x-committed")
            .Should().NotBeNull("committed change should be in the store");
    }

    [Fact]
    public void commit_session_closes_the_session()
    {
        var sid  = fx.OpenSession();
        Write.set_fragment(sid, "/info/x-close-test", "\"true\"");
        Tools.commit_session(sid);

        // After commit, session should be gone
        var listJson = Json.Parse(new SessionTools(fx.Store, fx.Sessions).list_sessions());
        var sessions = listJson.Prop("sessions").EnumerateArray().ToList();
        sessions.Any(s => s.GetProperty("session_id").GetString() == sid).Should().BeFalse();
    }

    [Fact]
    public void commit_session_with_validate_true_rejects_invalid_document()
    {
        var sid = fx.OpenSession();
        Write.delete_fragment(sid, "/info"); // makes the document invalid

        var json = Json.Parse(Tools.commit_session(sid, validate_before_commit: true));

        json.HasError().Should().BeFalse();   // no MCP error — just committed=false
        json.Bool("committed").Should().BeFalse();
        json.Prop("errors").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public void commit_session_unknown_session_returns_error()
    {
        var json = Json.Parse(Tools.commit_session("absent-session"));

        json.HasError().Should().BeTrue();
        json.ErrorCode().Should().Be("SESSION_NOT_FOUND");
    }

    [Fact]
    public void commit_session_detects_optimistic_concurrency_conflict()
    {
        // Open two sessions against the same contract
        var sidA = fx.OpenSession("session-a");
        var sidB = fx.OpenSession("session-b");

        // Make a valid change in both sessions
        Write.set_fragment(sidA, "/info/x-from-a", "\"a\"");
        Write.set_fragment(sidB, "/info/x-from-b", "\"b\"");

        // Commit session A — this updates the contract's hash
        var commitA = Json.Parse(Tools.commit_session(sidA, validate_before_commit: false));
        commitA.Bool("committed").Should().BeTrue("session A should commit successfully");

        // Session B's BaseContentHash is now stale → should get CONFLICT
        var commitB = Json.Parse(Tools.commit_session(sidB, validate_before_commit: false));
        commitB.HasError().Should().BeTrue();
        commitB.ErrorCode().Should().Be("CONFLICT");
    }

    // ── export_contract ───────────────────────────────────────────────────────

    [Fact]
    public void export_contract_default_format_returns_yaml_content()
    {
        var json = Json.Parse(Tools.export_contract(PetstoreFixture.ContractId));

        json.HasError().Should().BeFalse();
        var content = json.Str("content");
        content.Should().Contain("openapi:").And.Contain("paths:");
    }

    [Fact]
    public void export_contract_json_format_returns_json_content()
    {
        var json = Json.Parse(Tools.export_contract(PetstoreFixture.ContractId, format: "json"));

        json.HasError().Should().BeFalse();
        var content = json.Str("content");
        
        content.Should().NotBeNullOrEmpty();
        content.TrimStart().Should().StartWith("{");
        content.Should().Contain("\"openapi\"");
    }

    [Fact]
    public void export_contract_with_session_id_exports_staged_version()
    {
        var sid = fx.OpenSession();
        Write.set_fragment(sid, "/info/title", "\"Staged Export Test\"");

        var json = Json.Parse(Tools.export_contract(PetstoreFixture.ContractId, session_id: sid, format: "json"));

        json.HasError().Should().BeFalse();
        var content = json.Str("content");
        content.Should().Contain("Staged Export Test", "staged title should appear in the export");
    }

    [Fact]
    public void export_contract_unknown_contract_returns_error()
    {
        var json = Json.Parse(Tools.export_contract("no-such-contract"));

        json.HasError().Should().BeTrue();
        json.ErrorCode().Should().Be("CONTRACT_NOT_FOUND");
    }
}
