using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using VectorMatchEngine.Core.Services;

namespace VectorMatchEngine.Data;

/// <summary>
/// Matching inside SQL Server with its native <c>VECTOR</c> type and <c>VECTOR_DISTANCE</c> (SQL Server 2025,
/// Azure SQL). Vectors are copied from <c>DatasetRecords.VectorData</c> into <c>dbo.DatasetRecordVectors</c>, one
/// <c>VECTOR(512)</c> column per record, and pairs are scored and written to <c>MatchResults</c> by the server, so
/// no vectors cross the network during a match.
/// </summary>
/// <remarks>
/// The table is not in the EF model or a migration: an older SQL Server can't create a <c>VECTOR</c> column, so it
/// is created here, at run time, only on a server that passes <see cref="IsSupportedAsync"/>. <c>VarBinary</c>
/// <c>VectorData</c> stays the record's vector of record; this table is a copy, filled when a dataset is first
/// matched this way, and its foreign key removes its rows when their records go.
/// </remarks>
public class SqlVectorStore
{
    public const string TableName = "DatasetRecordVectors";

    /// <summary>Records of dataset A scored per statement, so progress can be reported and no statement runs long.</summary>
    private const int MatchChunkSize = 500;

    /// <summary>Records copied per statement when filling the table.</summary>
    private const int FillBatchSize = 500;

    private static readonly TimeSpan StatementTimeout = TimeSpan.FromMinutes(10);

    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ILogger<SqlVectorStore> _logger;

    // Keyed by connection string: the factory rereads it on each call, so the server can change under us.
    private readonly ConcurrentDictionary<string, bool> _support = new();

    public SqlVectorStore(IDbContextFactory<AppDbContext> contextFactory, ILogger<SqlVectorStore>? logger = null)
    {
        _contextFactory = contextFactory;
        _logger = logger ?? NullLogger<SqlVectorStore>.Instance;
    }

    private static int Dimensions => VectorizationService.VectorDimensions;

    /// <summary>
    /// Whether the configured server has the <c>VECTOR</c> type, <c>VECTOR_DISTANCE</c> and <c>OPENJSON</c>
    /// (which the fill uses). Asks the server once per connection string.
    /// </summary>
    public async Task<bool> IsSupportedAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var key = context.Database.GetConnectionString() ?? string.Empty;
        if (_support.TryGetValue(key, out var known))
            return known;

        // Opened outside the try: a server that can't be reached is an error, not an answer to cache.
        var connection = await OpenAsync(context, cancellationToken);

        bool supported;
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT CAST(VECTOR_DISTANCE('dot', CAST(N'[1,0]' AS VECTOR(2)), CAST(N'[1,0]' AS VECTOR(2))) AS FLOAT), " +
                "(SELECT COUNT(*) FROM OPENJSON(N'[1]'));";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            // VECTOR_DISTANCE's 'dot' is the negated dot product: -1 for these two.
            supported = await reader.ReadAsync(cancellationToken) && Math.Abs(reader.GetDouble(0) + 1d) < 1e-6;
        }
        catch (SqlException ex)
        {
            // Typically 243 ("VECTOR is not a defined system type") or 195 (unknown function) on older servers,
            // or 208 for OPENJSON under a compatibility level below 130.
            _logger.LogInformation("SQL Server VECTOR matching is not available: {Message}", ex.Message);
            supported = false;
        }

        _support[key] = supported;
        return supported;
    }

    /// <summary>
    /// Scores every record of dataset A against every record of dataset B inside SQL Server and inserts the pairs
    /// at or above <paramref name="threshold"/> into <c>MatchResults</c> under <paramref name="matchJobId"/>.
    /// Returns how many it inserted. Call only when <see cref="IsSupportedAsync"/> is true.
    /// </summary>
    public async Task<int> MatchIntoResultsAsync(
        int matchJobId,
        int datasetAId,
        int datasetBId,
        double threshold,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = await OpenAsync(context, cancellationToken);

        await EnsureTableAsync(connection, cancellationToken);
        await EnsureFilledAsync(connection, datasetAId, cancellationToken);
        if (datasetBId != datasetAId)
            await EnsureFilledAsync(connection, datasetBId, cancellationToken);

        // Dataset A's record ids, in order, so it can be scored in chunks.
        var idsA = new List<long>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT RecordId FROM dbo.{TableName} WHERE DatasetId = @a ORDER BY RecordId;";
            AddParameter(command, "@a", datasetAId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                idsA.Add(reader.GetInt64(0));
        }

        // 'dot' returns the negated dot product; the vectors are L2-normalized, so its negation is the cosine
        // similarity, the same number SimilarityService computes (and clamped the same way). 'cosine' isn't used
        // because a row with nothing in its vectorized columns has an all-zero vector.
        var sql = $"""
            INSERT INTO dbo.MatchResults (MatchJobId, RecordAId, RecordBId, SimilarityScore)
            SELECT @job, a.RecordId, b.RecordId, CASE WHEN s.Score > 1 THEN 1 ELSE s.Score END
            FROM dbo.{TableName} AS a
            JOIN dbo.{TableName} AS b ON b.DatasetId = @b
            CROSS APPLY (SELECT -CAST(VECTOR_DISTANCE('dot', a.Vector, b.Vector) AS FLOAT) AS Score) AS s
            WHERE a.DatasetId = @a AND a.RecordId BETWEEN @lo AND @hi AND s.Score >= @t;
            """;

        int inserted = 0;
        progress?.Report(0);
        for (int start = 0; start < idsA.Count; start += MatchChunkSize)
        {
            int end = Math.Min(start + MatchChunkSize, idsA.Count) - 1;
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = (int)StatementTimeout.TotalSeconds;
            AddParameter(command, "@job", matchJobId);
            AddParameter(command, "@a", datasetAId);
            AddParameter(command, "@b", datasetBId);
            AddParameter(command, "@lo", idsA[start]);
            AddParameter(command, "@hi", idsA[end]);
            AddParameter(command, "@t", threshold);
            inserted += await command.ExecuteNonQueryAsync(cancellationToken);

            progress?.Report((int)((end + 1) * 100L / idsA.Count));
        }

        progress?.Report(100);
        return inserted;
    }

    /// <summary>Creates the vectors table if it isn't there. Its rows go when their DatasetRecords row does.</summary>
    private static async Task EnsureTableAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            IF OBJECT_ID(N'dbo.{TableName}', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.{TableName} (
                    RecordId  BIGINT NOT NULL CONSTRAINT PK_{TableName} PRIMARY KEY,
                    DatasetId INT NOT NULL,
                    Vector    VECTOR({Dimensions}) NOT NULL,
                    CONSTRAINT FK_{TableName}_DatasetRecords FOREIGN KEY (RecordId)
                        REFERENCES dbo.DatasetRecords (Id) ON DELETE CASCADE);
                CREATE INDEX IX_{TableName}_DatasetId ON dbo.{TableName} (DatasetId) INCLUDE (RecordId);
            END
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Copies a dataset's vectors into the table unless every record is already there. Records are only ever
    /// added with their dataset, so a full count means the copy is current.
    /// </summary>
    private async Task EnsureFilledAsync(DbConnection connection, int datasetId, CancellationToken cancellationToken)
    {
        await using (var count = connection.CreateCommand())
        {
            count.CommandText = $"""
                SELECT (SELECT COUNT_BIG(*) FROM dbo.DatasetRecords WHERE DatasetId = @d),
                       (SELECT COUNT_BIG(*) FROM dbo.{TableName} WHERE DatasetId = @d);
                """;
            AddParameter(count, "@d", datasetId);
            await using var reader = await count.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            if (reader.GetInt64(0) == reader.GetInt64(1))
                return;
        }

        _logger.LogInformation("Copying dataset {DatasetId}'s vectors into {Table}.", datasetId, TableName);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = $"DELETE FROM dbo.{TableName} WHERE DatasetId = @d;";
            AddParameter(clear, "@d", datasetId);
            await clear.ExecuteNonQueryAsync(cancellationToken);
        }

        // The vectors are read in full first: SQL Server allows one open reader per connection (no MARS here).
        var records = new List<(long Id, byte[] Data)>();
        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT Id, VectorData FROM dbo.DatasetRecords WHERE DatasetId = @d ORDER BY Id;";
            AddParameter(read, "@d", datasetId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                records.Add((reader.GetInt64(0), (byte[])reader[1]));
        }

        // Each batch goes as one JSON document that OPENJSON unpacks and CAST turns into VECTOR values; this needs
        // nothing newer than the SqlClient the app already uses.
        var insertSql = $"""
            INSERT INTO dbo.{TableName} (RecordId, DatasetId, Vector)
            SELECT j.r, @d, CAST(j.v AS VECTOR({Dimensions}))
            FROM OPENJSON(@batch) WITH (r BIGINT '$.r', v NVARCHAR(MAX) '$.v' AS JSON) AS j;
            """;

        var json = new StringBuilder();
        for (int start = 0; start < records.Count; start += FillBatchSize)
        {
            json.Clear().Append('[');
            int end = Math.Min(start + FillBatchSize, records.Count);
            for (int i = start; i < end; i++)
            {
                if (i > start)
                    json.Append(',');
                json.Append("{\"r\":").Append(records[i].Id.ToString(CultureInfo.InvariantCulture)).Append(",\"v\":");
                AppendVector(json, records[i].Data);
                json.Append('}');
            }
            json.Append(']');

            await using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = insertSql;
            insert.CommandTimeout = (int)StatementTimeout.TotalSeconds;
            AddParameter(insert, "@d", datasetId);
            var batch = AddParameter(insert, "@batch", json.ToString());
            batch.DbType = DbType.String;
            batch.Size = -1;   // NVARCHAR(MAX)
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Writes a stored vector (raw float32s, as <see cref="VectorizationService.SerializeVector"/> makes them) as a
    /// JSON array of exactly <see cref="Dimensions"/> numbers, padding with zeros if it is short.
    /// </summary>
    private static void AppendVector(StringBuilder json, byte[] data)
    {
        var values = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(
            data.AsSpan(0, data.Length - data.Length % sizeof(float)));
        if (values.Length > Dimensions)
            throw new InvalidOperationException(
                $"A stored vector has {values.Length} numbers; this version uses {Dimensions}. Re-import the dataset.");

        json.Append('[');
        for (int i = 0; i < Dimensions; i++)
        {
            if (i > 0)
                json.Append(',');
            float value = i < values.Length ? values[i] : 0f;
            if (value == 0f || !float.IsFinite(value))
                json.Append('0');
            else
                // Fixed-point, never exponent form, so the server's JSON-to-VECTOR conversion reads it plainly.
                json.Append(value.ToString("0.#########", CultureInfo.InvariantCulture));
        }
        json.Append(']');
    }

    private static async Task<DbConnection> OpenAsync(AppDbContext context, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static DbParameter AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
        return parameter;
    }
}
