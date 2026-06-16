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
        var token = KeysetCursor.Encode(ts, "01ARZ3NDEKTSV4RRFFQ69G5FAV");

        var ok = KeysetCursor.TryDecode(token, out var decodedTs, out var decodedId);

        Assert.IsTrue(ok);
        Assert.AreEqual(ts, decodedTs);
        Assert.AreEqual("01ARZ3NDEKTSV4RRFFQ69G5FAV", decodedId);
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
public sealed class UlidTimestampTests
{
    [TestMethod]
    public void Decodes_MinUlidToEpoch()
    {
        // All-zero timestamp prefix decodes to the Unix epoch.
        var actual = UlidTimestamp.TryGetTimestampUtc("00000000000000000000000000");

        Assert.AreEqual(DateTimeOffset.FromUnixTimeMilliseconds(0).UtcDateTime, actual);
    }

    [TestMethod]
    public void Decodes_MaxUlid_OutOfRange_ReturnsNull()
    {
        // "7ZZZZZZZZZ" is the maximum 48-bit ms timestamp (year ~10889), beyond
        // DateTime's range, so the decoder returns null rather than throwing.
        Assert.IsNull(UlidTimestamp.TryGetTimestampUtc("7ZZZZZZZZZZZZZZZZZZZZZZZZZZ"));
    }

    [TestMethod]
    public void Decodes_OrdersByTimestamp()
    {
        var earlier = UlidTimestamp.TryGetTimestampUtc("01ARZ3NDEKTSV4RRFFQ69G5FAV");
        var later = UlidTimestamp.TryGetTimestampUtc("01ARZ3NDEMTSV4RRFFQ69G5FAV");

        Assert.IsNotNull(earlier);
        Assert.IsNotNull(later);
        Assert.IsTrue(later > earlier);
    }

    [TestMethod]
    [DataRow("too-short")]
    [DataRow("")]
    [DataRow(null)]
    public void Returns_NullForInvalid(string? value)
    {
        Assert.IsNull(UlidTimestamp.TryGetTimestampUtc(value));
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
