using IntegraOps.Application;
using IntegraOps.Infrastructure;
using Xunit;

namespace IntegraOps.Tests;

public sealed class OperationsTests
{
    [Fact]
    public void Csv_quotes_and_neutralises_formulas()
    {
        var csv = new CsvWriter(["a", "b"]);
        csv.Row("plain", "has,comma");
        csv.Row("say \"hi\"", "=SUM(A1)");
        csv.Row(null, DateTimeOffset.Parse("2026-09-22T13:00:00+03:00"));
        var lines = csv.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("﻿a,b", lines[0]);
        Assert.Equal("plain,\"has,comma\"", lines[1]);
        Assert.Equal("\"say \"\"hi\"\"\",'=SUM(A1)", lines[2]);
        Assert.Equal(",2026-09-22T10:00:00Z", lines[3]);
    }

    [Theory]
    [InlineData("2026-09-22", "2026-09-22T00:00:00+00:00")]
    [InlineData("2026-09-22T10:30:00+03:00", "2026-09-22T07:30:00+00:00")]
    public void Date_filter_accepts_days_and_instants(string input, string expected)
    {
        var errors = new Dictionary<string, string[]>();
        var value = InputValidation.DateFilter(errors, "from", input);
        Assert.Empty(errors);
        Assert.Equal(DateTimeOffset.Parse(expected), value);
    }

    [Theory]
    [InlineData("22.09.2026")]
    [InlineData("2026-13-01")]
    [InlineData("2026-09-22T10:30:00")]
    public void Date_filter_rejects_ambiguous_values(string input)
    {
        var errors = new Dictionary<string, string[]>();
        Assert.Null(InputValidation.DateFilter(errors, "from", input));
        Assert.Contains("from", errors.Keys);
    }

    [Fact]
    public void List_query_defaults_exclude_test_data_and_checks_range()
    {
        var query = InputValidation.TransactionList(null, null, null);
        Assert.Equal("exclude", query.Test);
        var error = Assert.Throws<RequestValidationException>(() =>
            InputValidation.TransactionList(null, null, null, from: "2026-09-22", to: "2026-09-21"));
        Assert.Contains("to", error.Errors.Keys);
        Assert.Throws<RequestValidationException>(() => InputValidation.TransactionList(null, null, null, test: "all"));
    }

    [Fact]
    public void Like_escape_makes_wildcards_literal()
    {
        Assert.Equal("a\\%b\\_c\\\\d", PostgresTransactionQueries.EscapeLike("a%b_c\\d"));
    }
}
