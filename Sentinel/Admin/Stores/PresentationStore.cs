using Sentinel.Admin.Models;
using ZiggyCreatures.Caching.Fusion;

namespace Sentinel.Admin.Stores;

public class PresentationStore(IFusionCache cache) : IPresentationStore
{
    private static readonly TimeSpan DeckTtl = TimeSpan.FromDays(180);

    private static string DeckKey(string userId) => $"sentinel:presentation:deck:{userId}";

    public async Task<PresentationDeck> GetOrCreateDeckAsync(string userId)
    {
        var deck = await cache.GetOrDefaultAsync<PresentationDeck>(DeckKey(userId));
        if (deck is not null) return deck;

        deck = new PresentationDeck { UserId = userId };
        await cache.SetAsync(DeckKey(userId), deck, o => o.SetDuration(DeckTtl));
        return deck;
    }

    public async Task<PresentationDeck> SaveDeckAsync(PresentationDeck deck)
    {
        deck.UpdatedAt = DateTime.UtcNow;
        await cache.SetAsync(DeckKey(deck.UserId), deck, o => o.SetDuration(DeckTtl));
        return deck;
    }

    public async Task<PresentationDeck> AddSlideAsync(string userId, PresentationSlide slide)
    {
        var deck = await GetOrCreateDeckAsync(userId);
        deck.Slides.Add(slide);
        return await SaveDeckAsync(deck);
    }

    public async Task<PresentationDeck> RemoveSlideAsync(string userId, string slideId)
    {
        var deck = await GetOrCreateDeckAsync(userId);
        deck.Slides.RemoveAll(s => s.Id == slideId);
        return await SaveDeckAsync(deck);
    }

    public async Task<PresentationDeck> MoveSlideAsync(string userId, string slideId, int direction)
    {
        var deck = await GetOrCreateDeckAsync(userId);
        var idx = deck.Slides.FindIndex(s => s.Id == slideId);
        if (idx < 0) return deck;

        var newIdx = idx + direction;
        if (newIdx < 0 || newIdx >= deck.Slides.Count) return deck;

        var slide = deck.Slides[idx];
        deck.Slides.RemoveAt(idx);
        deck.Slides.Insert(newIdx, slide);
        return await SaveDeckAsync(deck);
    }
}
