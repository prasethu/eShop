using eShop.WebAppComponents.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.JSInterop;

namespace eShop.Application.UnitTests;

[TestClass]
public sealed class ProductShortlistTests
{
    [TestMethod]
    public async Task InitializeAsync_EmptySession_LoadsOnceWithoutWriting()
    {
        var storage = new SessionStorageFake();
        using var shortlist = CreateShortlist(storage);

        await Task.WhenAll(shortlist.InitializeAsync(), shortlist.InitializeAsync());

        Assert.IsTrue(shortlist.IsInitialized);
        Assert.IsEmpty(shortlist.SelectedIds);
        Assert.AreEqual(1, storage.ReadCount);
        Assert.AreEqual(0, storage.WriteCount);
    }

    [TestMethod]
    public async Task SetSelectedAsync_ThreeDistinctProducts_RejectsFourthWithoutChangingStorage()
    {
        var storage = new SessionStorageFake();
        using var shortlist = CreateShortlist(storage);

        Assert.AreEqual(ShortlistChange.Added, await shortlist.SetSelectedAsync(12, true));
        Assert.AreEqual(ShortlistChange.Added, await shortlist.SetSelectedAsync(34, true));
        Assert.AreEqual(ShortlistChange.Added, await shortlist.SetSelectedAsync(56, true));
        Assert.AreEqual(ShortlistChange.LimitReached, await shortlist.SetSelectedAsync(78, true));

        Assert.AreEqual("12,34,56", string.Join(',', shortlist.SelectedIds));
        Assert.AreEqual("[12,34,56]", storage.Stored);
        Assert.AreEqual(3, storage.WriteCount);
    }

    [TestMethod]
    public async Task SetSelectedAsync_DuplicateAtCapacity_DoesNotConsumeAnotherSlotOrWrite()
    {
        var storage = new SessionStorageFake { Stored = "[12,34,56]" };
        using var shortlist = CreateShortlist(storage);

        Assert.AreEqual(ShortlistChange.Unchanged, await shortlist.SetSelectedAsync(34, true));

        Assert.AreEqual("12,34,56", string.Join(',', shortlist.SelectedIds));
        Assert.AreEqual("[12,34,56]", storage.Stored);
        Assert.AreEqual(0, storage.WriteCount);
    }

    [TestMethod]
    public async Task SetSelectedAsync_RemoveThenReplace_PreservesRemainingOrderAndFreesSlot()
    {
        var storage = new SessionStorageFake { Stored = "[12,34,56]" };
        using var shortlist = CreateShortlist(storage);

        Assert.AreEqual(ShortlistChange.Removed, await shortlist.SetSelectedAsync(34, false));
        Assert.AreEqual("12,56", string.Join(',', shortlist.SelectedIds));
        Assert.AreEqual("[12,56]", storage.Stored);
        Assert.AreEqual(ShortlistChange.Added, await shortlist.SetSelectedAsync(78, true));
        Assert.AreEqual(ShortlistChange.Unchanged, await shortlist.SetSelectedAsync(99, false));

        Assert.AreEqual("12,56,78", string.Join(',', shortlist.SelectedIds));
        Assert.AreEqual("[12,56,78]", storage.Stored);
        Assert.AreEqual(2, storage.WriteCount);
    }

    [TestMethod]
    public async Task InitializeAsync_NewCircuit_RestoresPersistedSelectionOrder()
    {
        var storage = new SessionStorageFake();
        using (var first = CreateShortlist(storage))
        {
            await first.SetSelectedAsync(34, true);
            await first.SetSelectedAsync(12, true);
        }

        using var restored = CreateShortlist(storage);
        await restored.InitializeAsync();

        Assert.AreEqual("34,12", string.Join(',', restored.SelectedIds));
        Assert.AreEqual(2, storage.ReadCount);
        Assert.AreEqual(2, storage.WriteCount);
        Assert.IsNull(restored.RecoveryMessage);
    }

    [TestMethod]
    [DataRow("not json")]
    [DataRow("null")]
    [DataRow("[1,1]")]
    [DataRow("[1,2,3,4]")]
    [DataRow("[0]")]
    [DataRow("[-1]")]
    [DataRow("[\"1\"]")]
    public async Task InitializeAsync_InvalidStorage_ClearsItWithAnExplicitNotice(string saved)
    {
        var storage = new SessionStorageFake { Stored = saved };
        using var shortlist = CreateShortlist(storage);

        await shortlist.InitializeAsync();

        Assert.IsEmpty(shortlist.SelectedIds);
        Assert.AreEqual("[]", storage.Stored);
        Assert.AreEqual(1, storage.WriteCount);
        Assert.AreEqual("Your saved shortlist was invalid and has been cleared. Please select your products again.", shortlist.RecoveryMessage);
    }

    [TestMethod]
    public async Task SetSelectedAsync_ConcurrentAdds_PreservesAllThreeAndRejectsFourth()
    {
        var storage = new SessionStorageFake();
        using var shortlist = CreateShortlist(storage);

        var results = await Task.WhenAll(
            shortlist.SetSelectedAsync(12, true),
            shortlist.SetSelectedAsync(34, true),
            shortlist.SetSelectedAsync(56, true),
            shortlist.SetSelectedAsync(78, true));

        Assert.AreEqual(3, results.Count(result => result == ShortlistChange.Added));
        Assert.AreEqual(1, results.Count(result => result == ShortlistChange.LimitReached));
        Assert.HasCount(3, shortlist.SelectedIds);
        Assert.Contains(12, shortlist.SelectedIds);
        Assert.AreEqual(System.Text.Json.JsonSerializer.Serialize(shortlist.SelectedIds), storage.Stored);
        Assert.AreEqual(1, storage.ReadCount);
        Assert.AreEqual(3, storage.WriteCount);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task SetSelectedAsync_WriteFails_PreservesStateAndDoesNotNotify(bool selected)
    {
        var storage = new SessionStorageFake { Stored = "[12]" };
        using var shortlist = CreateShortlist(storage);
        await shortlist.InitializeAsync();
        var notifications = 0;
        shortlist.Changed += () => notifications++;
        storage.FailWrite = true;

        await Assert.ThrowsExactlyAsync<JSException>(() => shortlist.SetSelectedAsync(selected ? 34 : 12, selected));

        Assert.AreEqual("12", string.Join(',', shortlist.SelectedIds));
        Assert.AreEqual("[12]", storage.Stored);
        Assert.AreEqual(0, notifications);
    }

    [TestMethod]
    public async Task InitializeAsync_ReadFails_CanRetryWithoutPublishingAnEmptyShortlist()
    {
        var storage = new SessionStorageFake { Stored = "[34,12]", FailRead = true };
        using var shortlist = CreateShortlist(storage);

        await Assert.ThrowsExactlyAsync<JSException>(() => shortlist.InitializeAsync());
        Assert.IsFalse(shortlist.IsInitialized);
        Assert.AreEqual(0, storage.WriteCount);

        storage.FailRead = false;
        await shortlist.InitializeAsync();

        Assert.IsTrue(shortlist.IsInitialized);
        Assert.AreEqual("34,12", string.Join(',', shortlist.SelectedIds));
        Assert.AreEqual("[34,12]", storage.Stored);
    }

    [TestMethod]
    public async Task SetSelectedAsync_Success_NotifiesWithAlreadyPersistedState()
    {
        var storage = new SessionStorageFake();
        using var shortlist = CreateShortlist(storage);
        await shortlist.InitializeAsync();
        string? observedStorage = null;
        string? observedState = null;
        shortlist.Changed += () =>
        {
            observedStorage = storage.Stored;
            observedState = string.Join(',', shortlist.SelectedIds);
        };

        await shortlist.SetSelectedAsync(34, true);

        Assert.AreEqual("[34]", observedStorage);
        Assert.AreEqual("34", observedState);
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public async Task SetSelectedAsync_InvalidProductId_RejectsBeforeAccessingStorage(int productId)
    {
        var storage = new SessionStorageFake();
        using var shortlist = CreateShortlist(storage);

        var exception = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => shortlist.SetSelectedAsync(productId, true));

        Assert.AreEqual("productId", exception.ParamName);
        Assert.AreEqual(0, storage.ReadCount);
        Assert.AreEqual(0, storage.WriteCount);
    }

    private static ProductShortlist CreateShortlist(SessionStorageFake storage) =>
        new(storage, NullLogger<ProductShortlist>.Instance);

    [TestMethod]
    [DataRow("[]", false)]
    [DataRow("[12]", false)]
    [DataRow("[12,34]", true)]
    [DataRow("[12,34,56]", true)]
    public async Task CanCompare_RestoredSelection_RequiresAtLeastTwoProducts(string saved, bool expected)
    {
        using var shortlist = CreateShortlist(new SessionStorageFake { Stored = saved });
        Assert.IsFalse(shortlist.CanCompare);

        await shortlist.InitializeAsync();

        Assert.AreEqual(expected, shortlist.CanCompare);
    }

    [TestMethod]
    public async Task CanCompare_TrayRemovalAndReplacement_UpdatesImmediately()
    {
        using var shortlist = CreateShortlist(new SessionStorageFake { Stored = "[12,34]" });
        await shortlist.InitializeAsync();
        Assert.IsTrue(shortlist.CanCompare);

        await shortlist.SetSelectedAsync(34, false);
        Assert.IsFalse(shortlist.CanCompare);

        await shortlist.SetSelectedAsync(56, true);
        Assert.IsTrue(shortlist.CanCompare);
    }

    [TestMethod]
    public async Task SetSelectedAsync_TrayRemoval_RestoresRemainingSelectionsAfterNavigation()
    {
        var storage = new SessionStorageFake { Stored = "[12,34,56]" };
        using (var traySession = CreateShortlist(storage))
        {
            await traySession.SetSelectedAsync(34, false);
        }

        using var nextPage = CreateShortlist(storage);
        await nextPage.InitializeAsync();

        Assert.AreEqual("12,56", string.Join(',', nextPage.SelectedIds));
        Assert.DoesNotContain(34, nextPage.SelectedIds);
        Assert.AreEqual("[12,56]", storage.Stored);
    }

    [TestMethod]
    public async Task SetSelectedAsync_SeparateBrowserSessions_DoesNotShareSelections()
    {
        var firstStorage = new SessionStorageFake();
        var secondStorage = new SessionStorageFake();
        using var firstSession = CreateShortlist(firstStorage);
        using var secondSession = CreateShortlist(secondStorage);

        await firstSession.SetSelectedAsync(12, true);
        await secondSession.InitializeAsync();

        Assert.Contains(12, firstSession.SelectedIds);
        Assert.IsEmpty(secondSession.SelectedIds);
        Assert.IsNull(secondStorage.Stored);
    }

    private sealed class SessionStorageFake : IJSRuntime
    {
        public string? Stored { get; set; }
        public int ReadCount { get; private set; }
        public int WriteCount { get; private set; }
        public bool FailRead { get; set; }
        public bool FailWrite { get; set; }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public async ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            Assert.IsNotNull(args);
            Assert.AreEqual(ProductShortlist.StorageKey, args[0]);
            switch (identifier)
            {
                case "sessionStorage.getItem":
                    ReadCount++;
                    if (FailRead)
                    {
                        throw new JSException("Session storage read denied.");
                    }
                    return (TValue)(object?)Stored!;
                case "sessionStorage.setItem":
                    if (FailWrite)
                    {
                        throw new JSException("Session storage write denied.");
                    }
                    Stored = Assert.IsInstanceOfType<string>(args[1]);
                    WriteCount++;
                    return default!;
                default:
                    throw new InvalidOperationException($"Unexpected JS invocation: {identifier}");
            }
        }
    }
}
