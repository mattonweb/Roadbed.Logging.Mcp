namespace Roadbed.Logging.Mcp.Tests;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Roadbed.Logging.Mcp.Tools;

[TestClass]
public sealed class AdHocSqlValidatorTests
{
    [TestMethod]
    [DataRow("SELECT 1")]
    [DataRow("select a.id from activity a where a.application = 'x'")]
    [DataRow("WITH t AS (SELECT id FROM activity) SELECT * FROM t")]
    [DataRow("  (SELECT 1)  ")]
    public void Accepts_ReadOnlyQueries(string sql)
    {
        var (ok, wrapped, error) = AdHocSqlValidator.Validate(sql, 200);

        Assert.IsTrue(ok, error);
        StringAssert.Contains(wrapped, "LIMIT 201");
    }

    [TestMethod]
    [DataRow("DROP TABLE activity")]
    [DataRow("INSERT INTO activity (id) VALUES ('x')")]
    [DataRow("UPDATE activity SET status = 'x'")]
    [DataRow("DELETE FROM activity")]
    [DataRow("SELECT 1; SELECT 2")]
    [DataRow("SELECT 1 -- comment")]
    [DataRow("SELECT 1 /* block */")]
    [DataRow("SELECT 1 # hash")]
    [DataRow("SELECT * INTO OUTFILE '/tmp/x' FROM activity")]
    [DataRow("CALL some_proc()")]
    [DataRow("")]
    public void Rejects_DangerousOrNonSelect(string sql)
    {
        var (ok, _, error) = AdHocSqlValidator.Validate(sql, 200);

        Assert.IsFalse(ok);
        Assert.IsFalse(string.IsNullOrWhiteSpace(error));
    }

    [TestMethod]
    public void Strips_TrailingSemicolon()
    {
        var (ok, wrapped, _) = AdHocSqlValidator.Validate("SELECT 1;", 10);

        Assert.IsTrue(ok);
        Assert.IsFalse(wrapped.Contains("1;", System.StringComparison.Ordinal));
    }
}
