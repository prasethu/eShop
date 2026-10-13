using eShop.WebAppComponents.Services;

namespace eShop.Application.UnitTests;

[TestClass]
public sealed class CatalogNavigationTests
{
    [TestMethod]
    [DataRow("https://store.test/?page=2&brand=1&type=3", "/?page=2&brand=1&type=3")]
    [DataRow("https://STORE.test/?brand=1", "/?brand=1")]
    [DataRow("https://store.test/", "")]
    [DataRow("https://other.test/?page=2", "")]
    [DataRow("https://store.test:1234/?page=2", "")]
    [DataRow("https://store.test/item/12?page=2", "")]
    [DataRow("https://store.test/compare", "")]
    [DataRow("/?page=2", "")]
    [DataRow("not a URL", "")]
    [DataRow(null, "")]
    public void GetReturnPath_PreservesOnlySameStoreCatalogQuery(string? referer, string expected)
    {
        Assert.AreEqual(expected, CatalogNavigation.GetReturnPath("https://store.test/", referer));
    }
}
