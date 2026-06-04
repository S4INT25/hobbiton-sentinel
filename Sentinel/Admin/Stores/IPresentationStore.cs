using Sentinel.Admin.Models;

namespace Sentinel.Admin.Stores;

public interface IPresentationStore
{
    Task<PresentationDeck> GetOrCreateDeckAsync(string userId);
    Task<PresentationDeck> SaveDeckAsync(PresentationDeck deck);
    Task<PresentationDeck> AddSlideAsync(string userId, PresentationSlide slide);
    Task<PresentationDeck> RemoveSlideAsync(string userId, string slideId);
    Task<PresentationDeck> MoveSlideAsync(string userId, string slideId, int direction);
}
