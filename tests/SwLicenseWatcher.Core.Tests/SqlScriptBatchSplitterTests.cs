using SwLicenseWatcher.Core;

namespace SwLicenseWatcher.Core.Tests;

public class SqlScriptBatchSplitterTests
{
    [Fact]
    public void Split_returns_empty_for_blank_script()
    {
        Assert.Empty(SqlScriptBatchSplitter.Split(null));
        Assert.Empty(SqlScriptBatchSplitter.Split(""));
        Assert.Empty(SqlScriptBatchSplitter.Split(" \r\n "));
    }

    [Fact]
    public void Split_keeps_a_single_batch_when_there_is_no_go()
    {
        var batches = SqlScriptBatchSplitter.Split("CREATE TABLE t(id INT);\nCREATE INDEX ix ON t(id);");

        var batch = Assert.Single(batches);
        Assert.Contains("CREATE TABLE t(id INT);", batch, StringComparison.Ordinal);
        Assert.Contains("CREATE INDEX ix ON t(id);", batch, StringComparison.Ordinal);
    }

    [Fact]
    public void Split_divides_on_go_lines_case_insensitively()
    {
        var sql = """
            CREATE SCHEMA s;
            GO
            CREATE TABLE t(id INT);
            go
            CREATE INDEX ix ON t(id);
            """;

        var batches = SqlScriptBatchSplitter.Split(sql);

        Assert.Equal(3, batches.Count);
        Assert.Equal("CREATE SCHEMA s;", batches[0]);
        Assert.Equal("CREATE TABLE t(id INT);", batches[1]);
        Assert.Equal("CREATE INDEX ix ON t(id);", batches[2]);
    }

    [Fact]
    public void Split_repeats_a_batch_when_go_has_a_count()
    {
        var batches = SqlScriptBatchSplitter.Split("SELECT 1;\nGO 3\n");

        Assert.Equal(["SELECT 1;", "SELECT 1;", "SELECT 1;"], batches);
    }

    [Fact]
    public void Split_ignores_empty_batches_and_does_not_treat_goto_as_go()
    {
        var sql = """
            GO
            SELECT 1;
            GOTO Skip
            GO
            """;

        var batches = SqlScriptBatchSplitter.Split(sql);

        var batch = Assert.Single(batches);
        Assert.Contains("SELECT 1;", batch, StringComparison.Ordinal);
        Assert.Contains("GOTO Skip", batch, StringComparison.Ordinal);
    }
}

public class DatabaseOptionsTests
{
    [Fact]
    public void ApplySchemaOnStartup_defaults_to_false()
    {
        Assert.False(new DatabaseOptions().ApplySchemaOnStartup);
    }
}
