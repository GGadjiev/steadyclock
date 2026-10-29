using Microsoft.Data.Sqlite;
using MyTimeTracker.Core.Models;
using MyTimeTracker.Core.Timer;
using MyTimeTracker.Data;

namespace MyTimeTracker.Tests;

public sealed class RepositoryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "MyTimeTrackerTests",
        Guid.NewGuid().ToString("N"));

    public RepositoryTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task Repository_round_trips_sessions_and_segments()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();
        var start = new DateTimeOffset(2026, 2, 2, 8, 0, 0, TimeSpan.Zero);
        var session = new TimeSession
        {
            StartUtc = start,
            EndUtc = start.AddMinutes(25),
            Comment = "Проверка",
            Source = SessionSource.Manual,
            State = TimerStatus.Stopped,
            CreatedUtc = start,
            UpdatedUtc = start.AddMinutes(25)
        };
        session.Segments.Add(new SessionSegment
        {
            StartUtc = start,
            EndUtc = start.AddMinutes(25),
            DurationMs = 25 * 60_000,
            CreatedUtc = start
        });

        var id = await repository.SaveSessionAsync(session);
        Assert.True(id > 0);

        var loaded = await repository.GetSessionAsync(id);
        Assert.NotNull(loaded);
        Assert.Equal("Проверка", loaded!.Comment);
        Assert.Equal(SessionSource.Manual, loaded.Source);
        Assert.Single(loaded.Segments);
        Assert.Equal(25 * 60_000, loaded.Segments[0].DurationMs);

        await repository.DeleteSessionAsync(id);
        Assert.Null(await repository.GetSessionAsync(id));
    }

    [Fact]
    public async Task Repository_uses_wal_foreign_keys_and_user_version()
    {
        var repository = CreateRepository();
        await repository.InitializeAsync();

        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode; PRAGMA user_version;";
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.Equal("wal", reader.GetString(0).ToLowerInvariant());
        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal(1, reader.GetInt32(0));
    }

    [Fact]
    public async Task Active_session_survives_restart_and_gap_is_not_counted()
    {
        var start = new DateTimeOffset(2026, 2, 10, 9, 0, 0, TimeSpan.Zero);
        var clockSet = new TestClockSet(start);
        var engine = new TimerEngine(clockSet.Wall, clockSet.Monotonic, clockSet.Idle);
        engine.Start();
        clockSet.AdvanceActive(TimeSpan.FromSeconds(42));

        var repository = CreateRepository();
        await repository.InitializeAsync();
        var snapshot = engine.CreateSnapshot()!;
        var id = await repository.SaveSessionAsync(snapshot);
        engine.AssignActiveSessionId(id);
        Assert.Equal(id, engine.ActiveSession!.Id);

        // Simulate process death. One hour later a new process starts.
        var restartAt = start.AddHours(1);
        var restartedClocks = new TestClockSet(restartAt);
        var restartedEngine = new TimerEngine(
            restartedClocks.Wall,
            restartedClocks.Monotonic,
            restartedClocks.Idle);

        var active = await repository.GetActiveSessionAsync();
        Assert.NotNull(active);
        var recovery = restartedEngine.Restore(active);
        Assert.True(recovery.HadActiveSession);
        Assert.Equal(TimerStatus.Running, restartedEngine.Status);
        Assert.InRange(restartedEngine.CurrentDuration.TotalSeconds, 41.9, 42.1);
        Assert.Equal(2, restartedEngine.ActiveSession!.Segments.Count);

        var recoveredSnapshot = restartedEngine.CreateSnapshot()!;
        await repository.SaveSessionAsync(recoveredSnapshot);

        var all = await repository.GetSessionsOverlappingAsync(start.AddDays(-1), restartAt.AddHours(2));
        Assert.Single(all);
        Assert.Null(all[0].EndUtc);
        Assert.InRange(all[0].Duration.TotalSeconds, 41.9, 42.1);
    }

    private string DatabasePath => Path.Combine(_directory, "test.db");

    private SqliteTimeSessionRepository CreateRepository() => new(DatabasePath);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // Temporary test files are safe to leave behind if still locked.
        }
    }
}
