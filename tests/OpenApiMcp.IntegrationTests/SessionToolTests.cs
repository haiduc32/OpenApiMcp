using FluentAssertions;
using OpenApiMcp.Tools;

namespace OpenApiMcp.IntegrationTests;

/// <summary>Tests for open_session, close_session, and list_sessions.</summary>
public sealed class SessionToolTests(PetstoreFixture fx) : IClassFixture<PetstoreFixture>
{
    private SessionTools Tools => new(fx.Store, fx.Sessions);

    // ── open_session ──────────────────────────────────────────────────────────

    [Fact]
    public void open_session_returns_session_id_and_contract_id()
    {
        var json = Json.Parse(Tools.open_session(PetstoreFixture.ContractId, "Integration test session"));

        json.HasError().Should().BeFalse();
        json.Str("session_id").Should().NotBeNullOrEmpty();
        json.Str("contract_id").Should().Be(PetstoreFixture.ContractId);
        json.Str("opened_at").Should().NotBeNullOrEmpty();

        // Cleanup
        fx.Sessions.Close(json.Str("session_id")!);
    }

    [Fact]
    public void open_session_unknown_contract_returns_error()
    {
        var json = Json.Parse(Tools.open_session("ghost-contract"));

        json.HasError().Should().BeTrue();
        json.ErrorCode().Should().Be("CONTRACT_NOT_FOUND");
    }

    [Fact]
    public void open_session_without_description_still_succeeds()
    {
        var json = Json.Parse(Tools.open_session(PetstoreFixture.ContractId));

        json.HasError().Should().BeFalse();
        json.Str("session_id").Should().NotBeNullOrEmpty();

        fx.Sessions.Close(json.Str("session_id")!);
    }

    // ── list_sessions ─────────────────────────────────────────────────────────

    [Fact]
    public void list_sessions_shows_open_session()
    {
        var sessionId = fx.OpenSession("listed session");

        var json = Json.Parse(Tools.list_sessions());

        json.HasError().Should().BeFalse();
        var sessions = json.Prop("sessions").EnumerateArray().ToList();
        sessions.Should().Contain(s => s.Str("session_id") == sessionId);

        fx.Sessions.Close(sessionId);
    }

    [Fact]
    public void list_sessions_filtered_by_contract_id()
    {
        var sessionId = fx.OpenSession("filtered session");

        var json = Json.Parse(Tools.list_sessions(PetstoreFixture.ContractId));

        json.HasError().Should().BeFalse();
        var sessions = json.Prop("sessions").EnumerateArray().ToList();
        sessions.Should().OnlyContain(s => s.Str("contract_id") == PetstoreFixture.ContractId);

        fx.Sessions.Close(sessionId);
    }

    [Fact]
    public void list_sessions_shows_patch_count_zero_for_new_session()
    {
        var sessionId = fx.OpenSession("patch count test");

        var json = Json.Parse(Tools.list_sessions(PetstoreFixture.ContractId));
        var session = json.Prop("sessions").EnumerateArray()
            .First(s => s.Str("session_id") == sessionId);

        session.Int("patch_count").Should().Be(0);

        fx.Sessions.Close(sessionId);
    }

    // ── close_session ─────────────────────────────────────────────────────────

    [Fact]
    public void close_session_returns_discarded_true()
    {
        var sessionId = fx.OpenSession("to be closed");

        var json = Json.Parse(Tools.close_session(sessionId));

        json.HasError().Should().BeFalse();
        json.Str("session_id").Should().Be(sessionId);
        json.Bool("discarded").Should().BeTrue();
    }

    [Fact]
    public void close_session_removes_session_from_list()
    {
        var sessionId = fx.OpenSession("will be removed");
        Tools.close_session(sessionId);

        var json = Json.Parse(Tools.list_sessions());
        var ids  = json.Prop("sessions").EnumerateArray().Select(s => s.Str("session_id")).ToList();
        ids.Should().NotContain(sessionId);
    }

    [Fact]
    public void close_session_unknown_id_returns_discarded_false()
    {
        var json = Json.Parse(Tools.close_session("nonexistent-session-id-xyz"));

        // The tool wraps this gracefully — discarded:false rather than an error
        json.HasError().Should().BeFalse();
        json.Bool("discarded").Should().BeFalse();
    }
}
