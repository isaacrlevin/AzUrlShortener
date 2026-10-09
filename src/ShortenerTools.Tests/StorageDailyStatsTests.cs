using Azure;
using Azure.Data.Tables;
using ShortenerTools.Core.Domain;
using ShortenerTools.Core.Messages;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace ShortenerTools.Tests;

[TestClass]
public class StorageDailyStatsTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("a'b")]
    public async Task QueriesOnlyDateColumnAndEscapesVanityAcrossPages(string? vanity)
    {
        var table = new Mock<TableClient>();
        var service = new Mock<TableServiceClient>();
        service.Setup(x => x.GetTableClient("ClickStats")).Returns(table.Object);
        var pages = new[]
        {
            Page<TableEntity>.FromValues(new[] { Click("2026-10-01 12:00") }, "next", Mock.Of<Response>()),
            Page<TableEntity>.FromValues(new[] { Click("2026-10-01 13:00"), Click("2026-10-02 00:00") },
                null, Mock.Of<Response>())
        };
        var expectedFilter = string.IsNullOrEmpty(vanity)
            ? null : TableClient.CreateQueryFilter($"PartitionKey eq {vanity}");
        table.Setup(x => x.QueryAsync<TableEntity>(expectedFilter, null,
                It.Is<IEnumerable<string>>(columns => columns.SequenceEqual(new[] { "Datetime" })),
                It.IsAny<CancellationToken>()))
            .Returns(AsyncPageable<TableEntity>.FromPages(pages));

        var result = await new StorageTableHelper(service.Object).GetDailyStats(
            new UrlClickStatsRequest(vanity) { TimeZoneId = "UTC" });

        Assert.AreEqual(2, result.Items.Count);
        Assert.AreEqual(2, result.Items[0].Count);
        Assert.AreEqual(1, result.Items[1].Count);
        Assert.AreEqual(string.Empty, result.Url);
        table.VerifyAll();
    }

    private static TableEntity Click(string date) => new() { ["Datetime"] = date };
}
