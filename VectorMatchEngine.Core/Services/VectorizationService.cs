using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Transforms;
using Microsoft.ML.Transforms.Text;

namespace VectorMatchEngine.Core.Services;

/// <summary>
/// Turns a row's selected text columns into an L2-normalized character-trigram vector.
///
/// The pipeline hashes trigrams into a fixed 2^<see cref="NumberOfBits"/> space rather than
/// learning a per-dataset vocabulary. That matters: a fitted vocabulary would assign different
/// meanings to the same vector index in different datasets, so vectors from Dataset A and
/// Dataset B could not be compared. Hashing gives every dataset the same axes, which is what
/// makes cross-dataset cosine similarity meaningful.
/// </summary>
public class VectorizationService
{
    /// <summary>Hash space size, in bits. 9 bits => 512 dimensions (2 KB per stored record).</summary>
    public const int NumberOfBits = 9;

    /// <summary>Dimensionality of every vector this service produces.</summary>
    public const int VectorDimensions = 1 << NumberOfBits;

    /// <summary>Character n-gram length.</summary>
    public const int NgramLength = 3;

    private readonly ILogger<VectorizationService> _logger;

    public VectorizationService(ILogger<VectorizationService>? logger = null)
        => _logger = logger ?? NullLogger<VectorizationService>.Instance;

    /// <summary>
    /// Vectorizes every row. The returned list is in the same order as <paramref name="rows"/>
    /// and always has the same length.
    /// </summary>
    public List<float[]> VectorizeRows(
        List<Dictionary<string, string>> rows,
        List<string> vectorizedColumns)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(vectorizedColumns);

        var result = new List<float[]>(rows.Count);
        if (rows.Count == 0)
            return result;

        if (vectorizedColumns.Count == 0)
            throw new ArgumentException(
                "At least one column must be selected for vectorization.", nameof(vectorizedColumns));

        var textRows = rows
            .Select(row => new TextRow { CombinedText = BuildCombinedText(row, vectorizedColumns) })
            .ToList();

        try
        {
            var mlContext = new MLContext(seed: 0);
            var dataView = mlContext.Data.LoadFromEnumerable(textRows);

            var pipeline =
                mlContext.Transforms.Text.NormalizeText(
                        outputColumnName: "NormalizedText",
                        inputColumnName: nameof(TextRow.CombinedText),
                        caseMode: TextNormalizingEstimator.CaseMode.Lower,
                        keepDiacritics: false,
                        keepPunctuations: false,
                        keepNumbers: true)
                    .Append(mlContext.Transforms.Text.TokenizeIntoCharactersAsKeys(
                        outputColumnName: "Chars",
                        inputColumnName: "NormalizedText",
                        useMarkerCharacters: true))
                    .Append(mlContext.Transforms.Text.ProduceHashedNgrams(
                        outputColumnName: "HashedNgrams",
                        inputColumnName: "Chars",
                        numberOfBits: NumberOfBits,
                        ngramLength: NgramLength,
                        skipLength: 0,
                        useAllLengths: false))
                    .Append(mlContext.Transforms.NormalizeLpNorm(
                        outputColumnName: "Features",
                        inputColumnName: "HashedNgrams",
                        norm: LpNormNormalizingEstimatorBase.NormFunction.L2));

            var model = pipeline.Fit(dataView);
            var transformed = model.Transform(dataView);

            foreach (var vectorRow in mlContext.Data.CreateEnumerable<VectorRow>(transformed, reuseRowObject: false))
                result.Add(Normalize(vectorRow.Features));
        }
        catch (Exception ex)
        {
            // Featurization is all-or-nothing inside a single ML.NET pass, so a failure here
            // falls back to zero vectors: the batch still ingests, those rows simply never match.
            _logger.LogError(ex, "ML.NET featurization failed for a batch of {RowCount} rows.", rows.Count);
            result.Clear();
        }

        // Guarantee a 1:1 alignment with the input rows regardless of what the pipeline returned.
        if (result.Count < rows.Count)
        {
            if (result.Count > 0)
                _logger.LogWarning(
                    "Featurization returned {Actual} vectors for {Expected} rows; padding with zero vectors.",
                    result.Count, rows.Count);

            while (result.Count < rows.Count)
                result.Add(new float[VectorDimensions]);
        }
        else if (result.Count > rows.Count)
        {
            result.RemoveRange(rows.Count, result.Count - rows.Count);
        }

        return result;
    }

    /// <summary>
    /// Concatenates the selected columns (in the given order), separated by a single space,
    /// lowercased and trimmed.
    /// </summary>
    public static string BuildCombinedText(Dictionary<string, string> row, List<string> vectorizedColumns)
    {
        var parts = vectorizedColumns
            .Select(column => row.TryGetValue(column, out var value) ? value : string.Empty)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim());

        return string.Join(' ', parts).ToLowerInvariant().Trim();
    }

    /// <summary>Serializes float[] to byte[] for VARBINARY(MAX) storage.</summary>
    public byte[] SerializeVector(float[] vector)
    {
        if (vector is null || vector.Length == 0)
            return Array.Empty<byte>();

        return MemoryMarshal.AsBytes<float>(vector.AsSpan()).ToArray();
    }

    /// <summary>Deserializes byte[] back into float[].</summary>
    public float[] DeserializeVector(byte[] bytes)
    {
        if (bytes is null || bytes.Length < sizeof(float))
            return Array.Empty<float>();

        return MemoryMarshal.Cast<byte, float>(bytes.AsSpan()).ToArray();
    }

    /// <summary>Guards against a null/short/NaN vector reaching the similarity loop.</summary>
    private static float[] Normalize(float[]? features)
    {
        if (features is null || features.Length == 0)
            return new float[VectorDimensions];

        for (int i = 0; i < features.Length; i++)
        {
            if (float.IsNaN(features[i]) || float.IsInfinity(features[i]))
                features[i] = 0f;
        }

        return features;
    }

    private sealed class TextRow
    {
        public string CombinedText { get; set; } = string.Empty;
    }

    private sealed class VectorRow
    {
        [VectorType(VectorDimensions)]
        public float[]? Features { get; set; }
    }
}
