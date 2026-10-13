using System.Text.Json;
using eShop.WebAppComponents.Catalog;
using eShop.WebAppComponents.Services;

namespace eShop.Application.UnitTests;

[TestClass]
public sealed class ProductComparisonTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task GetComparisonAsync_InsufficientSelections_DoesNotRequestCatalog(int count)
    {
        var catalog = Substitute.For<ICatalogService>();
        var service = new ShortlistCatalog(catalog);

        var result = await service.GetComparisonAsync(new[] { 12 }.Take(count));

        Assert.IsEmpty(result);
        await catalog.DidNotReceive().GetComparisonItems(Arg.Any<IEnumerable<int>>());
    }

    [TestMethod]
    [DataRow(2)]
    [DataRow(3)]
    public async Task GetComparisonAsync_TwoOrThreeSelections_PreservesOrderAndRequiredInformation(int count)
    {
        var catalog = Substitute.For<ICatalogService>();
        var ids = new[] { 34, 12, 56 }.Take(count).ToArray();
        var items = ids.Reverse().Select(id => new ComparisonItem(id, $"Gear {id}", 1234.50m, new CatalogBrand(1, "AdventureWorks"))).ToList();
        catalog.GetComparisonItems(Arg.Any<IEnumerable<int>>()).Returns(Task.FromResult(items));
        var service = new ShortlistCatalog(catalog);

        var result = await service.GetComparisonAsync(ids);

        Assert.HasCount(count, result);
        Assert.AreEqual(string.Join(',', ids), string.Join(',', result.Select(product => product.Id)));
        Assert.AreEqual("Gear 34", result[0].DisplayName);
        Assert.AreEqual("AdventureWorks", result[0].BrandDisplay);
        Assert.AreEqual("$1,234.50", result[0].PriceDisplay);
        Assert.AreEqual(1234.50m, result[0].Item?.Price);
        await catalog.Received(1).GetComparisonItems(Arg.Is<IEnumerable<int>>(requested => requested.SequenceEqual(ids)));
    }

    [TestMethod]
    public async Task GetComparisonAsync_ProductMissing_PreservesColumnWithExplicitMissingInformation()
    {
        var catalog = Substitute.For<ICatalogService>();
        catalog.GetComparisonItems(Arg.Any<IEnumerable<int>>()).Returns(Task.FromResult(new List<ComparisonItem>
        {
            new(34, "Camping tent", 100m, new CatalogBrand(1, "AdventureWorks"))
        }));
        var service = new ShortlistCatalog(catalog);

        var result = await service.GetComparisonAsync([12, 34]);

        Assert.HasCount(2, result);
        Assert.AreEqual(12, result[0].Id);
        Assert.IsNull(result[0].Item);
        Assert.AreEqual("Name unavailable (product 12)", result[0].DisplayName);
        Assert.AreEqual("Brand unavailable", result[0].BrandDisplay);
        Assert.AreEqual("Price unavailable", result[0].PriceDisplay);
        Assert.AreEqual("Camping tent", result[1].DisplayName);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow(" ")]
    public void ComparedProduct_MissingMetadata_DisplaysExplicitLabels(string? name)
    {
        var product = new ComparedProduct(12, new ComparisonItem(12, name, null, new CatalogBrand(1, " ")));

        Assert.AreEqual("Name unavailable (product 12)", product.DisplayName);
        Assert.AreEqual("Brand unavailable", product.BrandDisplay);
        Assert.AreEqual("Price unavailable", product.PriceDisplay);
    }

    [TestMethod]
    public void ComparedProduct_NullBrandAndZeroPrice_DoesNotTreatZeroAsMissing()
    {
        var product = new ComparedProduct(12, new ComparisonItem(12, "Free sample", 0m, null));

        Assert.AreEqual("Free sample", product.DisplayName);
        Assert.AreEqual("Brand unavailable", product.BrandDisplay);
        Assert.AreEqual("$0.00", product.PriceDisplay);
    }

    [TestMethod]
    public async Task GetComparisonAsync_CatalogFailure_PropagatesFailureInsteadOfShowingUnavailableProducts()
    {
        var catalog = Substitute.For<ICatalogService>();
        var failure = new HttpRequestException("Catalog unavailable.");
        catalog.GetComparisonItems(Arg.Any<IEnumerable<int>>()).Returns(Task.FromException<List<ComparisonItem>>(failure));
        var service = new ShortlistCatalog(catalog);

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => service.GetComparisonAsync([12, 34]));

        Assert.AreSame(failure, exception);
    }

    [TestMethod]
    public async Task GetComparisonAsync_NullCollection_ReportsInvalidResponse()
    {
        var catalog = Substitute.For<ICatalogService>();
        catalog.GetComparisonItems(Arg.Any<IEnumerable<int>>()).Returns(Task.FromResult<List<ComparisonItem>>(null!));
        var service = new ShortlistCatalog(catalog);

        var exception = await Assert.ThrowsExactlyAsync<JsonException>(() => service.GetComparisonAsync([12, 34]));

        Assert.AreEqual("The catalog returned no product collection for comparison.", exception.Message);
    }

    [TestMethod]
    public async Task GetComparisonAsync_SelectionChangesDuringRequest_KeepsItsOriginalSnapshot()
    {
        var catalog = Substitute.For<ICatalogService>();
        var completion = new TaskCompletionSource<List<ComparisonItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.GetComparisonItems(Arg.Any<IEnumerable<int>>()).Returns(completion.Task);
        var service = new ShortlistCatalog(catalog);
        var ids = new List<int> { 34, 12 };

        var task = service.GetComparisonAsync(ids);
        ids.Remove(34);
        completion.SetResult([new(34, "Tent", 100m, null), new(12, "Boots", 75m, null)]);
        var result = await task;

        Assert.AreEqual("34,12", string.Join(',', result.Select(product => product.Id)));
        await catalog.Received(1).GetComparisonItems(Arg.Is<IEnumerable<int>>(requested => requested.SequenceEqual(new[] { 34, 12 })));
    }
}
