using MyTimeTracker.Core.Models;

namespace MyTimeTracker.Core.Abstractions;

public interface ITimeSessionRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<TimeSession?> GetActiveSessionAsync(CancellationToken cancellationToken = default);

    Task<TimeSession?> GetSessionAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Inserts a new session or replaces an existing one transactionally. Returns the session id.</summary>
    Task<long> SaveSessionAsync(TimeSession session, CancellationToken cancellationToken = default);

    Task DeleteSessionAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>Returns sessions whose overall range intersects the UTC half-open range.</summary>
    Task<IReadOnlyList<TimeSession>> GetSessionsOverlappingAsync(
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        CancellationToken cancellationToken = default);
}
