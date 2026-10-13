using System.Net;
using System.Text;
using System.Text.Json;
using eShop.WebAppComponents.Services;

namespace eShop.Application.UnitTests;

[TestClass]
public sealed class CatalogComparisonClientTests
{
    [TestMethod]
    public async Task GetComparisonItems_ReadsExistingEndpoint_AndKeepsMissingPriceDistinctFromZero()
    {
        var handler = new CatalogResponseHandler("""
            [{"id":34,"name":"Free sample","price":0,"catalogBrand":{"id":1,"brand":"AdventureWorks"}},
             {"id":12,"name":"Incomplete product"}]
            """);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://catalog.test/") };
        var catalog = new CatalogService(client);

        var result = await catalog.GetComparisonItems([34, 12]);

        Assert.AreEqual("/api/catalog/items/by?ids=34&ids=12", handler.RequestedPath);
        Assert.HasCount(2, result);
        Assert.AreEqual(0m, result[0].Price);
        Assert.AreEqual("AdventureWorks", result[0].CatalogBrand?.Brand);
        Assert.IsNull(result[1].Price);
        Assert.IsNull(result[1].CatalogBrand);
    }

    [TestMethod]
    public async Task GetComparisonItems_NullResponse_ThrowsExplicitError()
    {
        using var client = new HttpClient(new CatalogResponseHandler("null")) { BaseAddress = new Uri("https://catalog.test/") };
        var catalog = new CatalogService(client);

        var exception = await Assert.ThrowsExactlyAsync<JsonException>(() => catalog.GetComparisonItems([12, 34]));

        Assert.AreEqual("The catalog returned no product collection for comparison.", exception.Message);
    }

    [TestMethod]
    public async Task GetComparisonItems_HttpError_IsNotConvertedIntoAnEmptyResult()
    {
        using var client = new HttpClient(new CatalogResponseHandler("", HttpStatusCode.ServiceUnavailable)) { BaseAddress = new Uri("https://catalog.test/") };
        var catalog = new CatalogService(client);

        var exception = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => catalog.GetComparisonItems([12, 34]));

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
    }

    [TestMethod]
    public async Task GetCatalogItems_ExistingCaller_StillUsesSameEndpointAndCatalogShape()
    {
        var handler = new CatalogResponseHandler("""
            [{"id":12,"name":"Boots","price":75.50,"catalogBrand":{"id":1,"brand":"AdventureWorks"}}]
            """);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://catalog.test/") };
        var catalog = new CatalogService(client);

        var result = await catalog.GetCatalogItems([12]);

        Assert.AreEqual("/api/catalog/items/by?ids=12", handler.RequestedPath);
        var item = Assert.ContainsSingle(result);
        Assert.AreEqual(12, item.Id);
        Assert.AreEqual("Boots", item.Name);
        Assert.AreEqual(75.50m, item.Price);
    }

    private sealed class CatalogResponseHandler(string body, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string? RequestedPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestedPath = request.RequestUri?.PathAndQuery;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
