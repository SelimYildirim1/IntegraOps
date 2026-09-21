using IntegraOps.Application;
using IntegraOps.Domain;
using Xunit;

namespace IntegraOps.Tests;

public sealed class TransactionListTests
{
    [Fact]
    public void Defaults_do_not_filter_out_pending_transactions()
    {
        var query = InputValidation.TransactionList(null, null, null);
        Assert.Null(query.Flow);
        Assert.Null(query.Status);
        Assert.Null(query.Delay);
        Assert.Equal(1, query.Page);
        Assert.Equal(20, query.PageSize);
    }

    [Theory]
    [InlineData("inProgress", TransactionStatus.InProgress)]
    [InlineData(" COMPLETED ", TransactionStatus.Completed)]
    public void Public_status_is_mapped_to_the_database_enum(string input, TransactionStatus expected)
    {
        var query = InputValidation.TransactionList(" delay-learning ", input, " OVERDUE ", 2, 10);
        Assert.Equal(expected, query.Status);
        Assert.Equal("delay-learning", query.Flow);
        Assert.Equal("overdue", query.Delay);
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(-1, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public void Invalid_pagination_is_rejected(int page, int size, string field)
    {
        var ex = Assert.Throws<RequestValidationException>(() =>
            InputValidation.TransactionList(null, null, null, page, size));
        Assert.Contains(field, ex.Errors.Keys);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("unknown")]
    public void Numeric_or_unknown_status_cannot_silently_select_an_enum(string status) =>
        Assert.Throws<RequestValidationException>(() =>
            InputValidation.TransactionList(null, status, null));

    [Fact]
    public void Invalid_flow_and_delay_are_both_reported()
    {
        var ex = Assert.Throws<RequestValidationException>(() =>
            InputValidation.TransactionList("bad'flow", null, "late"));
        Assert.Contains("flow", ex.Errors.Keys);
        Assert.Contains("delay", ex.Errors.Keys);
    }

    [Theory]
    [InlineData(0, 1, 0, false)]
    [InlineData(20, 1, 1, false)]
    [InlineData(21, 1, 2, true)]
    [InlineData(21, 2, 2, false)]
    [InlineData(21, 99, 2, false)]
    public void Page_metadata_handles_empty_and_out_of_range_pages(
        long total, int page, long pages, bool hasNext)
    {
        var result = new TransactionPage(page, 20, total, []);
        Assert.Equal(pages, result.TotalPages);
        Assert.Equal(hasNext, result.HasNextPage);
    }

    [Fact]
    public void Large_page_does_not_overflow_next_page_calculation()
    {
        var query = InputValidation.TransactionList(null, null, null, int.MaxValue, 100);
        var result = new TransactionPage(query.Page, query.PageSize, 500, []);
        Assert.False(result.HasNextPage);
    }
}
