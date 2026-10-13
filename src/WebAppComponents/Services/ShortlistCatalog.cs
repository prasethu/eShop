using eShop.WebAppComponents.Catalog;
using System.Text.Json;

namespace eShop.WebAppComponents.Services;

public sealed record ShortlistedProduct(int Id, CatalogItem? Item)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Item?.Name) ? $"Product {Id}" : Item.Name;
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
}
