using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace eShop.WebAppComponents.Services;

public enum ShortlistChange
{
    Added,
    Removed,
    Unchanged,
    LimitReached
}

public sealed class ProductShortlist(IJSRuntime js, ILogger<ProductShortlist> logger) : IDisposable
{
    public const int MaximumProducts = 3;
    public const string StorageKey = "eshop.product-shortlist.v1";

    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyList<int> selectedIds = Array.AsReadOnly(Array.Empty<int>());

    public IReadOnlyList<int> SelectedIds => selectedIds;
    public bool IsInitialized { get; private set; }
    public bool CanCompare => IsInitialized && selectedIds.Count >= 2;
    public string? RecoveryMessage { get; private set; }
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        await gate.WaitAsync();
        try
        {
            await LoadAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<ShortlistChange> SetSelectedAsync(int productId, bool selected)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(productId);
        await gate.WaitAsync();
        try
        {
            await LoadAsync();
            if (selectedIds.Contains(productId) == selected)
            {
                return ShortlistChange.Unchanged;
            }

            if (selected && selectedIds.Count == MaximumProducts)
            {
                return ShortlistChange.LimitReached;
            }

            var next = selected
                ? selectedIds.Append(productId).ToArray()
                : selectedIds.Where(id => id != productId).ToArray();

            // Publish only after storage succeeds so a failed write cannot look successful.
            await js.InvokeVoidAsync("sessionStorage.setItem", StorageKey, JsonSerializer.Serialize(next));
            selectedIds = Array.AsReadOnly(next);
            RecoveryMessage = null;
            Changed?.Invoke();
            return selected ? ShortlistChange.Added : ShortlistChange.Removed;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task LoadAsync()
    {
        if (IsInitialized)
        {
            return;
        }

        var saved = await js.InvokeAsync<string?>("sessionStorage.getItem", StorageKey);
        int[] ids = [];
        if (saved is not null)
        {
            try
            {
                ids = JsonSerializer.Deserialize<int[]>(saved)
                    ?? throw new JsonException("The stored shortlist must be an array.");
                if (ids.Length > MaximumProducts || ids.Any(id => id <= 0) || ids.Distinct().Count() != ids.Length)
                {
                    throw new JsonException("The stored shortlist contains invalid selections.");
                }
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Resetting an invalid product shortlist.");
                await js.InvokeVoidAsync("sessionStorage.setItem", StorageKey, "[]");
                ids = [];
                RecoveryMessage = "Your saved shortlist was invalid and has been cleared. Please select your products again.";
            }
        }

        selectedIds = Array.AsReadOnly(ids);
        IsInitialized = true;
        Changed?.Invoke();
    }

    public void Dispose() => gate.Dispose();
}
