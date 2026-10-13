namespace eShop.WebAppComponents.Services;

public static class CatalogNavigation
{
    public static string GetReturnPath(string baseAddress, string? referer)
    {
        if (!string.IsNullOrEmpty(referer)
            && Uri.TryCreate(referer, UriKind.Absolute, out var refUri)
            && Uri.TryCreate(baseAddress, UriKind.Absolute, out var baseUri)
            && string.Equals(refUri.Authority, baseUri.Authority, StringComparison.OrdinalIgnoreCase)
            && string.Equals(refUri.AbsolutePath.TrimEnd('/'), baseUri.AbsolutePath.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(refUri.Query))
        {
            return refUri.PathAndQuery;
        }

        return "";
    }
}
