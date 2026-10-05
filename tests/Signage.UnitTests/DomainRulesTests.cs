using Signage.Application;
using Signage.Domain;

namespace Signage.UnitTests;

public sealed class DomainRulesTests
{
    [Theory]
    [InlineData(null, 10_000)]
    [InlineData(500, 2_000)]
    [InlineData(3_500, 3_500)]
    [InlineData(300_000, 120_000)]
    public void Slide_duration_is_defaulted_and_clamped(int? source, int expected) =>
        Assert.Equal(expected, SlideTiming.Normalize(source, 10_000, 2_000, 120_000));

    [Fact]
    public void Active_publication_uses_priority_then_publish_time()
    {
        var now = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        var lower = Publication(now.AddHours(-1), priority: 1, published: now.AddMinutes(-5));
        var higherOld = Publication(now.AddHours(-1), priority: 2, published: now.AddMinutes(-10));
        var higherNew = Publication(now.AddHours(-1), priority: 2, published: now.AddMinutes(-2));
        var future = Publication(now.AddMinutes(5), priority: 9, published: now);
        Assert.Same(higherNew, PublicationRules.SelectActive([lower, higherOld, higherNew, future], now));
    }

    [Fact]
    public void End_time_is_exclusive()
    {
        var now = DateTimeOffset.UtcNow;
        var item = Publication(now.AddHours(-1), 0, now.AddHours(-1));
        item.EndsUtc = now;
        Assert.Null(PublicationRules.SelectActive([item], now));
    }

    [Fact]
    public void Pairing_session_must_be_unexpired_and_unconsumed()
    {
        var now = DateTimeOffset.UtcNow;
        var session = new PairingSession { Id = Guid.NewGuid(), CodeHash = "a", TemporaryTokenHash = "b", ExpiresUtc = now.AddMinutes(1) };
        Assert.True(PairingRules.IsUsable(session, now));
        session.ConsumedUtc = now;
        Assert.False(PairingRules.IsUsable(session, now));
    }

    [Fact]
    public void Token_hashing_is_constant_time_compatible_and_not_plaintext()
    {
        var token = TokenUtility.CreateToken();
        var hash = TokenUtility.Hash(token);
        Assert.DoesNotContain(token, hash, StringComparison.Ordinal);
        Assert.True(TokenUtility.FixedTimeMatches(token, hash));
        Assert.False(TokenUtility.FixedTimeMatches(token + "x", hash));
    }

    [Fact]
    public void Content_identifier_is_deterministic()
    {
        var value = new { schemaVersion = 1, items = new[] { new { asset = "slide.png", hash = "abc" } } };
        Assert.Equal(TokenUtility.CalculateContentId(value), TokenUtility.CalculateContentId(value));
        Assert.Equal(64, TokenUtility.CalculateContentId(value).Length);
    }

    [Fact]
    public void Europe_London_dst_boundaries_are_explicit()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
        Assert.True(zone.IsInvalidTime(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Unspecified)));
        Assert.True(zone.IsAmbiguousTime(new DateTime(2026, 10, 25, 1, 30, 0, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void Retention_keeps_newest_versions_and_every_protected_version()
    {
        var presentationId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var newest = new VersionRetentionCandidate(Guid.NewGuid(), presentationId, now, false);
        var second = new VersionRetentionCandidate(Guid.NewGuid(), presentationId, now.AddMinutes(-1), false);
        var protectedOld = new VersionRetentionCandidate(Guid.NewGuid(), presentationId, now.AddMinutes(-2), true);
        var removable = new VersionRetentionCandidate(Guid.NewGuid(), presentationId, now.AddMinutes(-3), false);

        var result = RetentionRules.SelectRemovable([newest, second, protectedOld, removable], 2);

        Assert.Equal([removable.Id], result);
    }

    private static Publication Publication(DateTimeOffset starts, int priority, DateTimeOffset published) => new()
    {
        Id = Guid.NewGuid(), ScreenGroupId = Guid.NewGuid(), PresentationVersionId = Guid.NewGuid(), StartsUtc = starts,
        Priority = priority, IsEnabled = true, PublishedBySubject = "test", PublishedUtc = published
    };
}
