namespace Roadbed.Logging.Mcp.Tests;

using System;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Roadbed.Logging.Mcp.Configuration;
using Roadbed.Logging.Mcp.Data;
using Roadbed.Logging.Mcp.Lib;
using Roadbed.Logging.Mcp.Models;

[TestClass]
public sealed class KeysetCursorTests
{
    [TestMethod]
    public void RoundTrips_TimestampAndId()
    {
        var ts = new DateTime(2026, 3, 15, 8, 30, 0, DateTimeKind.Utc);
        var token = KeysetCursor.Encode(ts, "019f067a-d176-77d4-8b82-641bee246b19");

        var ok = KeysetCursor.TryDecode(token, out var decodedTs, out var decodedId);

        Assert.IsTrue(ok);
        Assert.AreEqual(ts, decodedTs);
        Assert.AreEqual("019f067a-d176-77d4-8b82-641bee246b19", decodedId);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not-base64-!!")]
    [DataRow(null)]
    public void Rejects_BadTokens(string? token)
    {
        Assert.IsFalse(KeysetCursor.TryDecode(token, out _, out _));
    }
}

[TestClass]
public sealed class LogLevelsTests
{
    [TestMethod]
    [DataRow("Trace", 0)]
    [DataRow("information", 2)]
    [DataRow("WARNING", 3)]
    [DataRow("Critical", 5)]
    public void TryGetValue_ResolvesNames(string name, int expected)
    {
        Assert.IsTrue(LogLevels.TryGetValue(name, out var value));
        Assert.AreEqual(expected, value);
    }

    [TestMethod]
    public void TryGetValue_RejectsUnknown()
    {
        Assert.IsFalse(LogLevels.TryGetValue("Verbose", out _));
    }

    [TestMethod]
    public void ToName_MapsNumbers()
    {
        Assert.AreEqual("Error", LogLevels.ToName(4));
        Assert.AreEqual("Unknown", LogLevels.ToName(99));
    }
}

[TestClass]
public sealed class Uuid7TimestampTests
{
    [TestMethod]
    public void Decodes_EmbeddedTimestampToMillisecond()
    {
        // 019f067a-d176-... carries 0x019f067ad176 ms in its first 48 bits =
        // 2026-06-27T00:29:00.150Z, matching the row's created_on to the millisecond.
        var actual = Uuid7Timestamp.TryGetTimestampUtc("019f067a-d176-77d4-8b82-641bee246b19");

        Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(1782520140150).UtcDateTime, actual);
    }

    [TestMethod]
    public void Decodes_OrdersByTimestamp()
    {
        var earlier = Uuid7Timestamp.TryGetTimestampUtc("019f0677-27f4-76ee-84f6-663938a80398");
        var later = Uuid7Timestamp.TryGetTimestampUtc("019f067a-d176-77d4-8b82-641bee246b19");

        Assert.IsNotNull(earlier);
        Assert.IsNotNull(later);
        Assert.IsTrue(later > earlier);
    }

    [TestMethod]
    public void Returns_NullForNonVersion7()
    {
        // A v4 (random) UUID has no embedded timestamp, so the version nibble guard rejects it.
        Assert.IsNull(Uuid7Timestamp.TryGetTimestampUtc("00000000-0000-4000-8000-000000000000"));
        Assert.IsNull(Uuid7Timestamp.TryGetTimestampUtc(Guid.NewGuid().ToString()));
    }

    [TestMethod]
    [DataRow("not-a-guid")]
    [DataRow("01KVPJQBWF37G6Y1F1CA5AW8PZ")] // a legacy ULID no longer parses as a UUID
    [DataRow("")]
    [DataRow(null)]
    public void Returns_NullForInvalid(string? value)
    {
        Assert.IsNull(Uuid7Timestamp.TryGetTimestampUtc(value));
    }
}

[TestClass]
public sealed class McpConfigTests
{
    [TestMethod]
    public void Validate_Throws_WhenNoSources()
    {
        var config = new McpConfig();

        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }

    [TestMethod]
    public void Validate_Throws_WhenMultipleDefaults()
    {
        var config = new McpConfig();
        config.Sources.Add(new SourceConfig { Name = "a", ConnectionString = "x", Default = true });
        config.Sources.Add(new SourceConfig { Name = "b", ConnectionString = "y", Default = true });

        Assert.Throws<InvalidOperationException>(() => config.Validate());
    }

    [TestMethod]
    public void ResolveDefault_UsesSingleSource()
    {
        var config = new McpConfig();
        config.Sources.Add(new SourceConfig { Name = "only", ConnectionString = "x" });

        config.Validate();

        Assert.AreEqual("only", config.ResolveDefaultSourceName());
    }
}

[TestClass]
public sealed class ConfigLoaderTests
{
    [TestMethod]
    public void ResolvePath_IsHomeDotfile()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".Roadbed.Logging.Mcp");

        Assert.AreEqual(expected, ConfigLoader.ResolvePath());
    }
}

[TestClass]
public sealed class SuccessRateTests
{
    [TestMethod]
    public void ExcludesSkipped_RateOverFailureClassesOnly()
    {
        // 4 succeeded, 1 failed, 0 canceled → 4/5 = 0.8. Any number of 'skipped' on the
        // side is irrelevant; the helper takes (s, f, c) and that's the rule's enforcement.
        Assert.AreEqual(0.8, LoggingRepository.SuccessRate(succeeded: 4, failed: 1, canceled: 0));
    }

    [TestMethod]
    public void NoCompleted_ReturnsNull()
    {
        // Stand-in for an "all-skipped" window: caller did not pass any s/f/c.
        Assert.IsNull(LoggingRepository.SuccessRate(succeeded: 0, failed: 0, canceled: 0));
    }

    [TestMethod]
    public void AllFailed_ReturnsZero()
    {
        Assert.AreEqual(0.0, LoggingRepository.SuccessRate(succeeded: 0, failed: 5, canceled: 0));
    }
}

[TestClass]
public sealed class ComputeStatsTests
{
    [TestMethod]
    public void SkippedNotCountedInSuccessRate_ButSurfacedSeparately()
    {
        var rows = new[]
        {
            new ActivityRow { Status = "succeeded" },
            new ActivityRow { Status = "succeeded" },
            new ActivityRow { Status = "failed" },
            new ActivityRow { Status = "skipped" },
            new ActivityRow { Status = "skipped" },
        };

        var stats = LoggingRepository.ComputeStats(rows);

        Assert.AreEqual(5, stats.Count);
        Assert.AreEqual(2, stats.Skipped);
        // 2 succeeded / (2 succeeded + 1 failed) = 0.6667; skipped neither helps nor hurts.
        Assert.AreEqual(0.6667, stats.SuccessRate);
    }

    [TestMethod]
    public void AllSkipped_SuccessRateNull()
    {
        var rows = new[]
        {
            new ActivityRow { Status = "skipped" },
            new ActivityRow { Status = "skipped" },
        };

        var stats = LoggingRepository.ComputeStats(rows);

        Assert.AreEqual(2, stats.Count);
        Assert.AreEqual(2, stats.Skipped);
        Assert.IsNull(stats.SuccessRate);
    }
}

[TestClass]
public sealed class SkippedJsonShapeTests
{
    [TestMethod]
    public void FleetOverviewRow_SerializesSkippedKey()
    {
        var row = new FleetOverviewRow { Application = "x", Runs = 3, Skipped = 1 };

        using var doc = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(row));

        Assert.AreEqual(1, doc.RootElement.GetProperty("skipped").GetInt64());
    }

    [TestMethod]
    public void HistoryStats_SerializesSkippedKey()
    {
        var stats = new HistoryStats { Count = 4, Skipped = 2, SuccessRate = 1.0 };

        using var doc = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(stats));

        Assert.AreEqual(2, doc.RootElement.GetProperty("skipped").GetInt64());
    }
}

[TestClass]
public sealed class IdColumnTests
{
    [TestMethod]
    public void Guid_RendersCanonicalLowercase()
    {
        // A UUIDv7 id arrives from MySqlConnector as a Guid; emit the canonical
        // lowercase 8-4-4-4-12 form regardless of the source casing.
        var guid = Guid.Parse("019F067A-D176-77D4-8B82-641BEE246B19");

        Assert.AreEqual("019f067a-d176-77d4-8b82-641bee246b19", IdColumn.ToCanonicalString(guid));
    }

    [TestMethod]
    public void UlidString_PassesThrough()
    {
        // A pre-2026-06-23 ULID row still reads: the string is preserved verbatim
        // (Crockford base32 is not a uuid and must not be reformatted).
        const string ulid = "01KVPJQBWF37G6Y1F1CA5AW8PZ";

        Assert.AreEqual(ulid, IdColumn.ToCanonicalString(ulid));
    }

    [TestMethod]
    public void NullDbNullAndEmpty_ReturnNull()
    {
        Assert.IsNull(IdColumn.ToCanonicalString(null));
        Assert.IsNull(IdColumn.ToCanonicalString(DBNull.Value));
        Assert.IsNull(IdColumn.ToCanonicalString(string.Empty));
    }

    [TestMethod]
    public void OrEmpty_CoalescesNullToEmpty()
    {
        Assert.AreEqual(string.Empty, IdColumn.ToCanonicalStringOrEmpty(null));
        Assert.AreEqual(
            "019f067a-d176-77d4-8b82-641bee246b19",
            IdColumn.ToCanonicalStringOrEmpty(Guid.Parse("019f067a-d176-77d4-8b82-641bee246b19")));
    }
}
