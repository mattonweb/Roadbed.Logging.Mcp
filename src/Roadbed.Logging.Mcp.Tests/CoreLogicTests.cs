namespace Roadbed.Logging.Mcp.Tests;

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Roadbed.Logging.Mcp.Configuration;
using Roadbed.Logging.Mcp.Lib;

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

    [DataTestMethod]
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
    [DataTestMethod]
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

    [DataTestMethod]
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

        Assert.ThrowsException<InvalidOperationException>(() => config.Validate());
    }

    [TestMethod]
    public void Validate_Throws_WhenMultipleDefaults()
    {
        var config = new McpConfig();
        config.Sources.Add(new SourceConfig { Name = "a", ConnectionString = "x", Default = true });
        config.Sources.Add(new SourceConfig { Name = "b", ConnectionString = "y", Default = true });

        Assert.ThrowsException<InvalidOperationException>(() => config.Validate());
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
