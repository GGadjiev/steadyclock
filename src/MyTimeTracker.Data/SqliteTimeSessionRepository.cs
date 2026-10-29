using System.Globalization;
using Microsoft.Data.Sqlite;
using MyTimeTracker.Core.Abstractions;
using MyTimeTracker.Core.Models;

namespace MyTimeTracker.Data;

public sealed class SqliteTimeSessionRepository : ITimeSessionRepository
{
    private const string IsoFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private readonly string _databasePath;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _initialized;

    public SqliteTimeSessionRepository(string databasePath)
    {
        _databasePath = databasePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _initialized) == 1)
        {
            return;
        }

        await Task.Run(InitializeCore, cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _initialized, 1);
    }

    public Task<TimeSession?> GetActiveSessionAsync(CancellationToken cancellationToken = default) =>
        RunReadAsync(GetActiveSessionCore, cancellationToken);

    public Task<TimeSession?> GetSessionAsync(long id, CancellationToken cancellationToken = default) =>
        RunReadAsync(() => GetSessionCore(id), cancellationToken);

    public async Task<IReadOnlyList<TimeSession>> GetSessionsOverlappingAsync(
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc,
        CancellationToken cancellationToken = default) =>
        await RunReadAsync(() => GetSessionsOverlappingCore(rangeStartUtc, rangeEndUtc), cancellationToken)
            .ConfigureAwait(false);

    public async Task<long> SaveSessionAsync(
        TimeSession session,
        CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => SaveSessionCore(session, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async Task DeleteSessionAsync(long id, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                using var connection = OpenConnection();
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM sessions WHERE id = $id;";
                command.Parameters.AddWithValue("$id", id);
                command.ExecuteNonQuery();
                transaction.Commit();
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void InitializeCore()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_databasePath)!);
        using var connection = OpenConnection();

        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode=WAL;";
            pragma.ExecuteNonQuery();
        }

        var version = GetUserVersion(connection);
        if (version > 1)
        {
            throw new InvalidOperationException(
                $"Версия базы данных ({version}) новее, чем поддерживает приложение.");
        }

        using var transaction = connection.BeginTransaction();
        if (version < 1)
        {
            Execute(connection, transaction, """
                CREATE TABLE IF NOT EXISTS sessions (
                    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                    start_utc          TEXT NOT NULL,
                    end_utc            TEXT NULL,
                    comment            TEXT NOT NULL DEFAULT '',
                    source             INTEGER NOT NULL DEFAULT 0,
                    state              INTEGER NOT NULL DEFAULT 0,
                    last_heartbeat_utc TEXT NULL,
                    created_utc        TEXT NOT NULL,
                    updated_utc        TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS segments (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    session_id  INTEGER NOT NULL,
                    start_utc   TEXT NOT NULL,
                    end_utc     TEXT NULL,
                    duration_ms INTEGER NOT NULL DEFAULT 0,
                    ordinal     INTEGER NOT NULL DEFAULT 0,
                    created_utc TEXT NOT NULL,
                    FOREIGN KEY (session_id) REFERENCES sessions(id) ON DELETE CASCADE
                );

                CREATE INDEX IF NOT EXISTS idx_sessions_start ON sessions(start_utc);
                CREATE INDEX IF NOT EXISTS idx_sessions_end ON sessions(end_utc);
                CREATE INDEX IF NOT EXISTS idx_segments_session ON segments(session_id);
                """);
            SetUserVersion(connection, transaction, 1);
        }

        transaction.Commit();
    }

    private List<TimeSession> GetSessionsOverlappingCore(
        DateTimeOffset rangeStartUtc,
        DateTimeOffset rangeEndUtc)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, start_utc, end_utc, comment, source, state,
                   last_heartbeat_utc, created_utc, updated_utc
            FROM sessions
            WHERE start_utc < $rangeEnd
              AND (end_utc IS NULL OR end_utc > $rangeStart)
            ORDER BY start_utc DESC;
            """;
        command.Parameters.AddWithValue("$rangeStart", ToIso(rangeStartUtc));
        command.Parameters.AddWithValue("$rangeEnd", ToIso(rangeEndUtc));
        var sessions = ReadSessions(connection, command);
        LoadSegments(connection, sessions);
        return sessions;
    }

    private TimeSession? GetSessionCore(long id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, start_utc, end_utc, comment, source, state,
                   last_heartbeat_utc, created_utc, updated_utc
            FROM sessions
            WHERE id = $id
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$id", id);
        var sessions = ReadSessions(connection, command);
        LoadSegments(connection, sessions);
        return sessions.FirstOrDefault();
    }

    private TimeSession? GetActiveSessionCore()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, start_utc, end_utc, comment, source, state,
                   last_heartbeat_utc, created_utc, updated_utc
            FROM sessions
            WHERE end_utc IS NULL AND state <> 0
            ORDER BY start_utc DESC
            LIMIT 1;
            """;
        var sessions = ReadSessions(connection, command);
        LoadSegments(connection, sessions);
        return sessions.FirstOrDefault();
    }

    private long SaveSessionCore(TimeSession session, CancellationToken cancellationToken)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        long id = session.Id;
        if (id == 0)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO sessions
                    (start_utc, end_utc, comment, source, state,
                     last_heartbeat_utc, created_utc, updated_utc)
                VALUES
                    ($start, $end, $comment, $source, $state,
                     $heartbeat, $created, $updated);
                """;
            AddSessionParameters(insert, session);
            insert.ExecuteNonQuery();

            using var lastId = connection.CreateCommand();
            lastId.Transaction = transaction;
            lastId.CommandText = "SELECT last_insert_rowid();";
            id = Convert.ToInt64(lastId.ExecuteScalar(), CultureInfo.InvariantCulture);
            session.Id = id;
        }
        else
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE sessions SET
                    start_utc = $start,
                    end_utc = $end,
                    comment = $comment,
                    source = $source,
                    state = $state,
                    last_heartbeat_utc = $heartbeat,
                    created_utc = $created,
                    updated_utc = $updated
                WHERE id = $id;
                """;
            update.Parameters.AddWithValue("$id", id);
            AddSessionParameters(update, session);
            var affected = update.ExecuteNonQuery();
            if (affected == 0)
            {
                throw new InvalidOperationException("Запись не найдена. Возможно, она была удалена.");
            }
        }

        using (var deleteSegments = connection.CreateCommand())
        {
            deleteSegments.Transaction = transaction;
            deleteSegments.CommandText = "DELETE FROM segments WHERE session_id = $sessionId;";
            deleteSegments.Parameters.AddWithValue("$sessionId", id);
            deleteSegments.ExecuteNonQuery();
        }

        var ordinal = 0;
        foreach (var segment in session.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var segmentCommand = connection.CreateCommand();
            segmentCommand.Transaction = transaction;
            segmentCommand.CommandText = """
                INSERT INTO segments
                    (session_id, start_utc, end_utc, duration_ms, ordinal, created_utc)
                VALUES
                    ($sessionId, $start, $end, $duration, $ordinal, $created);
                """;
            segmentCommand.Parameters.AddWithValue("$sessionId", id);
            segmentCommand.Parameters.AddWithValue("$start", ToIso(segment.StartUtc));
            segmentCommand.Parameters.AddWithValue(
                "$end",
                segment.EndUtc.HasValue ? ToIso(segment.EndUtc.Value) : DBNull.Value);
            segmentCommand.Parameters.AddWithValue("$duration", Math.Max(0, segment.DurationMs));
            segmentCommand.Parameters.AddWithValue("$ordinal", ordinal++);
            segmentCommand.Parameters.AddWithValue("$created", ToIso(segment.CreatedUtc));
            segmentCommand.ExecuteNonQuery();
            segment.SessionId = id;
        }

        transaction.Commit();
        return id;
    }

    private static void AddSessionParameters(SqliteCommand command, TimeSession session)
    {
        command.Parameters.AddWithValue("$start", ToIso(session.StartUtc));
        command.Parameters.AddWithValue(
            "$end",
            session.EndUtc.HasValue ? ToIso(session.EndUtc.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$comment", session.Comment ?? string.Empty);
        command.Parameters.AddWithValue("$source", (int)session.Source);
        command.Parameters.AddWithValue("$state", (int)session.State);
        command.Parameters.AddWithValue(
            "$heartbeat",
            session.LastHeartbeatUtc.HasValue ? ToIso(session.LastHeartbeatUtc.Value) : DBNull.Value);
        command.Parameters.AddWithValue("$created", ToIso(session.CreatedUtc));
        command.Parameters.AddWithValue("$updated", ToIso(session.UpdatedUtc));
    }

    private static List<TimeSession> ReadSessions(SqliteConnection connection, SqliteCommand command)
    {
        var result = new List<TimeSession>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var session = new TimeSession
            {
                Id = reader.GetInt64(0),
                StartUtc = FromIso(reader.GetString(1)),
                EndUtc = reader.IsDBNull(2) ? null : FromIso(reader.GetString(2)),
                Comment = reader.GetString(3),
                Source = (SessionSource)reader.GetInt32(4),
                State = (TimerStatus)reader.GetInt32(5),
                LastHeartbeatUtc = reader.IsDBNull(6) ? null : FromIso(reader.GetString(6)),
                CreatedUtc = FromIso(reader.GetString(7)),
                UpdatedUtc = FromIso(reader.GetString(8))
            };
            result.Add(session);
        }

        return result;
    }

    private static void LoadSegments(SqliteConnection connection, List<TimeSession> sessions)
    {
        if (sessions.Count == 0)
        {
            return;
        }

        var byId = sessions.ToDictionary(session => session.Id);
        foreach (var chunk in sessions.Chunk(400))
        {
            using var command = connection.CreateCommand();
            var placeholders = new List<string>(chunk.Length);
            for (var index = 0; index < chunk.Length; index++)
            {
                var name = "$id" + index.ToString(CultureInfo.InvariantCulture);
                placeholders.Add(name);
                command.Parameters.AddWithValue(name, chunk[index].Id);
            }

            command.CommandText = $"""
                SELECT id, session_id, start_utc, end_utc, duration_ms, ordinal, created_utc
                FROM segments
                WHERE session_id IN ({string.Join(", ", placeholders)})
                ORDER BY session_id, ordinal, start_utc;
                """;

            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var segment = new SessionSegment
                {
                    Id = reader.GetInt64(0),
                    SessionId = reader.GetInt64(1),
                    StartUtc = FromIso(reader.GetString(2)),
                    EndUtc = reader.IsDBNull(3) ? null : FromIso(reader.GetString(3)),
                    DurationMs = reader.GetInt64(4),
                    Ordinal = reader.GetInt32(5),
                    CreatedUtc = FromIso(reader.GetString(6))
                };
                if (byId.TryGetValue(segment.SessionId, out var session))
                {
                    session.Segments.Add(segment);
                }
            }
        }
    }

    private SqliteConnection OpenConnection()
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true,
            DefaultTimeout = 5
        };
        var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = """
            PRAGMA foreign_keys=ON;
            PRAGMA busy_timeout=5000;
            PRAGMA synchronous=FULL;
            """;
        pragma.ExecuteNonQuery();
        return connection;
    }

    private async Task<T> RunReadAsync<T>(Func<T> action, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _initialized) == 0)
        {
            throw new InvalidOperationException("Хранилище не инициализировано.");
        }

        return await Task.Run(action, cancellationToken).ConfigureAwait(false);
    }

    private static int GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void SetUserVersion(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int version)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static string ToIso(DateTimeOffset value) =>
        value.UtcDateTime.ToString(IsoFormat, CultureInfo.InvariantCulture);

    private static DateTimeOffset FromIso(string value) =>
        DateTimeOffset.ParseExact(
            value,
            IsoFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
