using System.Text.Json;
using eShop.WebAppComponents.Catalog;
using eShop.WebAppComponents.Services;

namespace eShop.Application.UnitTests;

[TestClass]
public sealed class ShortlistCatalogTests
{
    [TestMethod]
    public async Task GetProductsAsync_EmptyShortlist_DoesNotRequestCatalog()
    {
        var catalog = Substitute.For<ICatalogService>();
        var service = new ShortlistCatalog(catalog);

        var result = await service.GetProductsAsync([]);

        Assert.IsEmpty(result);
        await catalog.DidNotReceive().GetCatalogItems(Arg.Any<IEnumerable<int>>());
    }

    [TestMethod]
    public async Task GetProductsAsync_CatalogReturnsDifferentOrder_PreservesSelectionOrderAndNames()
    {
        var catalog = Substitute.For<ICatalogService>();
        var first = Product(12, "Trail boots");
        var second = Product(34, "Camping tent");
        catalog.GetCatalogItems(Arg.Any<IEnumerable<int>>()).Returns(Task.FromResult(new List<CatalogItem> { first, second }));
        var service = new ShortlistCatalog(catalog);

        var result = await service.GetProductsAsync([34, 12]);

        Assert.HasCount(2, result);
        Assert.AreEqual(34, result[0].Id);
        Assert.AreEqual("Camping tent", result[0].DisplayName);
        Assert.AreSame(second, result[0].Item);
        Assert.AreEqual(12, result[1].Id);
        Assert.AreEqual("Trail boots", result[1].DisplayName);
        Assert.AreSame(first, result[1].Item);
        await catalog.Received(1).GetCatalogItems(Arg.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 34, 12 })));
    }

    [TestMethod]
    public async Task GetProductsAsync_ProductMissing_PreservesItsIdForRemoval()
    {
        var catalog = Substitute.For<ICatalogService>();
        catalog.GetCatalogItems(Arg.Any<IEnumerable<int>>()).Returns(Task.FromResult(new List<CatalogItem> { Product(34, "Camping tent") }));
        var service = new ShortlistCatalog(catalog);

        var result = await service.GetProductsAsync([12, 34]);

        Assert.HasCount(2, result);
        Assert.AreEqual(12, result[0].Id);
        Assert.IsNull(result[0].Item);
        Assert.AreEqual("Product 12", result[0].DisplayName);
        Assert.AreEqual(34, result[1].Id);
        Assert.AreEqual("Camping tent", result[1].DisplayName);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow(" ")]
    [DataRow("\t")]
    public async Task GetProductsAsync_NameMissing_UsesIdentifiableFallback(string name)
    {
        var catalog = Substitute.For<ICatalogService>();
        var product = Product(12, name);
        catalog.GetCatalogItems(Arg.Any<IEnumerable<int>>()).Returns(Task.FromResult(new List<CatalogItem> { product }));
        var service = new ShortlistCatalog(catalog);

        var result = await service.GetProductsAsync([12]);

        var entry = Assert.ContainsSingle(result);
        Assert.AreEqual("Product 12", entry.DisplayName);
        Assert.AreSame(product, entry.Item);
    }

    [TestMethod]
    public async Task GetProductsAsync_CatalogReturnsUnselectedProduct_DoesNotAddItToTray()
    {
        var catalog = Substitute.For<ICatalogService>();
        catalog.GetCatalogItems(Arg.Any<IEnumerable<int>>()).Returns(Task.FromResult(new List<CatalogItem>
        {
            Product(12, "Trail boots"), Product(99, "Unselected jacket")
        }));
        var service = new ShortlistCatalog(catalog);

        var result = await service.GetProductsAsync([12]);

        var entry = Assert.ContainsSingle(result);
        Assert.AreEqual(12, entry.Id);
        Assert.AreEqual("Trail boots", entry.DisplayName);
    }

    [TestMethod]
    public async Task GetProductsAsync_RequestFails_PropagatesErrorInsteadOfPretendingProductsAreMissing()
    {
        var catalog = Substitute.For<ICatalogService>();
        var failure = new HttpRequestException("Catalog unavailable.");
        catalog.GetCatalogItems(Arg.Any<IEnumerable<int>>()).Returns(Task.FromException<List<CatalogItem>>(failure));
        var service = new ShortlistCatalog(catalog);

        var result = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => service.GetProductsAsync([12]));

        Assert.AreSame(failure, result);
    }

    [TestMethod]
    public async Task GetProductsAsync_NullResponse_ReportsInvalidCatalogData()
    {
        var catalog = Substitute.For<ICatalogService>();
        catalog.GetCatalogItems(Arg.Any<IEnumerable<int>>()).Returns(Task.FromResult<List<CatalogItem>>(null!));
        var service = new ShortlistCatalog(catalog);

        var exception = await Assert.ThrowsExactlyAsync<JsonException>(() => service.GetProductsAsync([12]));

        Assert.AreEqual("The catalog returned no product collection for the shortlist.", exception.Message);
    }

    [TestMethod]
    public async Task GetProductsAsync_SelectionChangesDuringRequest_UsesOriginalSnapshot()
    {
        var catalog = Substitute.For<ICatalogService>();
        var completion = new TaskCompletionSource<List<CatalogItem>>(TaskCreationOptions.RunContinuationsAsynchronously);
        catalog.GetCatalogItems(Arg.Any<IEnumerable<int>>()).Returns(completion.Task);
        var service = new ShortlistCatalog(catalog);
        var selection = new List<int> { 34, 12 };

        var request = service.GetProductsAsync(selection);
        selection.Remove(34);
        completion.SetResult([Product(12, "Trail boots"), Product(34, "Camping tent")]);
        var result = await request;

        Assert.AreEqual("34,12", string.Join(',', result.Select(product => product.Id)));
        await catalog.Received(1).GetCatalogItems(Arg.Is<IEnumerable<int>>(ids => ids.SequenceEqual(new[] { 34, 12 })));
    }

    private static CatalogItem Product(int id, string name) => new(
        id, name, "Adventure gear", 100m, "image.png", 1, new CatalogBrand(1, "AdventureWorks"), 1, new CatalogItemType(1, "Equipment"));
}
