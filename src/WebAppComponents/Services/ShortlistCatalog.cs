using eShop.WebAppComponents.Catalog;
using System.Text.Json;

namespace eShop.WebAppComponents.Services;

public sealed record ShortlistedProduct(int Id, CatalogItem? Item)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Item?.Name) ? $"Product {Id}" : Item.Name;
}

public sealed record ComparedProduct(int Id, ComparisonItem? Item)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Item?.Name) ? $"Name unavailable (product {Id})" : Item.Name;
    public string BrandDisplay => string.IsNullOrWhiteSpace(Item?.CatalogBrand?.Brand) ? "Brand unavailable" : Item.CatalogBrand.Brand;
    public string PriceDisplay => Item?.Price is decimal price ? $"${price.ToPriceDisplay()}" : "Price unavailable";
}

public sealed class ShortlistCatalog(ICatalogService catalog)
{
    public async Task<IReadOnlyList<ShortlistedProduct>> GetProductsAsync(IEnumerable<int> selectedIds)
    {
        var ids = selectedIds.ToArray();
        if (ids.Length == 0)
        {
            return Array.Empty<ShortlistedProduct>();
        }

        var products = await catalog.GetCatalogItems(ids)
            ?? throw new JsonException("The catalog returned no product collection for the shortlist.");
        return ids.Select(id => new ShortlistedProduct(id, products.FirstOrDefault(product => product.Id == id))).ToArray();
    }

    public async Task<IReadOnlyList<ComparedProduct>> GetComparisonAsync(IEnumerable<int> selectedIds)
    {
        var ids = selectedIds.ToArray();
        if (ids.Length < 2)
        {
            return Array.Empty<ComparedProduct>();
        }

        var products = await catalog.GetComparisonItems(ids)
            ?? throw new JsonException("The catalog returned no product collection for comparison.");
        return ids.Select(id => new ComparedProduct(id, products.FirstOrDefault(product => product.Id == id))).ToArray();
    }
}
