using DesktopComputerUse.Automation.Resolution;

namespace DesktopComputerUse.Automation.Tests;

public sealed class TextNormalizerTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Tokens_returns_empty_for_missing_text(string? value)
    {
        Assert.Empty(TextNormalizer.Tokens(value));
    }

    [Fact]
    public void Tokens_normalizes_camel_case_punctuation_prefixes_and_duplicates()
    {
        var tokens = TextNormalizer.Tokens("btnCustomerName--customer_name");

        Assert.Equal(["customer", "name"], tokens);
    }

    [Fact]
    public void Tokens_removes_a_prefix_from_a_single_compact_token()
    {
        Assert.Equal(["savebutton"], TextNormalizer.Tokens("btnsavebutton"));
    }

    [Theory]
    [InlineData("txtCustomer", "customer")]
    [InlineData("btnSave", "save")]
    [InlineData("cmbCountry", "country")]
    [InlineData("ddlRegion", "region")]
    [InlineData("lblStatus", "status")]
    public void Tokens_removes_each_known_control_prefix(
        string value,
        string expected)
    {
        Assert.Equal([expected], TextNormalizer.Tokens(value));
    }

    [Fact]
    public void Tokens_does_not_remove_a_prefix_that_is_the_entire_token()
    {
        Assert.Equal(["btn"], TextNormalizer.Tokens("btn"));
    }

    [Fact]
    public void Tokens_only_removes_a_compact_prefix_when_more_than_two_characters_remain()
    {
        Assert.Equal(["btnab"], TextNormalizer.Tokens("btnab"));
        Assert.Equal(["abc"], TextNormalizer.Tokens("btnabc"));
    }

    [Fact]
    public void Similarity_is_order_insensitive_and_handles_prefix_and_partial_matches()
    {
        Assert.Equal(1, TextNormalizer.Similarity("Customer Name", "name customer"));
        Assert.Equal(0.9, TextNormalizer.Similarity("customer", "customers"), 10);
        Assert.Equal(0.45, TextNormalizer.Similarity("customer name", "customer address"), 10);
        Assert.Equal(0, TextNormalizer.Similarity(null, "customer"));
    }

    [Fact]
    public void Similarity_uses_the_union_for_a_proper_subset()
    {
        Assert.Equal(
            0.5,
            TextNormalizer.Similarity(
                ["customer"],
                ["customer", "name"]),
            10);
    }

    [Fact]
    public void Similarity_returns_the_larger_of_jaccard_and_prefix_scores()
    {
        Assert.Equal(
            0.45,
            TextNormalizer.Similarity(
                ["customer", "address"],
                ["customers", "postal"]),
            10);
    }
}
