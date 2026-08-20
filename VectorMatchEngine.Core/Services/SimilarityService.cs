using System.Collections.Concurrent;
using System.Numerics;
using VectorMatchEngine.Core.Models;

namespace VectorMatchEngine.Core.Services;

/// <summary>Pairwise cosine-similarity matching between two vectorized datasets.</summary>
public class SimilarityService
{
    /// <summary>
    /// Cosine similarity of two L2-normalized vectors. Because the inputs are already
    /// L2-normalized, cosine similarity reduces to the plain dot product.
    /// Returns a value in [0, 1].
    /// </summary>
    public double CosineSimilarity(float[] a, float[] b)
    {
        if (a is null || b is null)
            return 0d;

        int length = Math.Min(a.Length, b.Length);
        if (length == 0)
            return 0d;

        float dot = 0f;
        int i = 0;

        // SIMD fast path: this runs once per candidate pair, so it dominates match-job time.
        int width = Vector<float>.Count;
        if (Vector.IsHardwareAccelerated && length >= width)
        {
            var accumulator = Vector<float>.Zero;
            for (; i <= length - width; i += width)
                accumulator += new Vector<float>(a, i) * new Vector<float>(b, i);
            dot = Vector.Dot(accumulator, Vector<float>.One);
        }

        for (; i < length; i++)
            dot += a[i] * b[i];

        if (float.IsNaN(dot) || float.IsInfinity(dot))
            return 0d;

        // Clamp away floating-point drift just past the [0,1] boundary.
        return Math.Clamp((double)dot, 0d, 1d);
    }

    /// <summary>
    /// Finds every (A, B) pair whose cosine similarity meets <paramref name="threshold"/>.
    /// Results are sorted by similarity descending; progress is reported as 0-100.
    /// </summary>
    public Task<List<MatchedPair>> FindMatchesAsync(
        List<(long id, float[] vector, Dictionary<string, string> preserved)> datasetA,
        List<(long id, float[] vector, Dictionary<string, string> preserved)> datasetB,
        double threshold,
        IProgress<int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(datasetA);
        ArgumentNullException.ThrowIfNull(datasetB);

        return Task.Run(() =>
        {
            if (datasetA.Count == 0 || datasetB.Count == 0)
            {
                progress?.Report(100);
                return new List<MatchedPair>();
            }

            var matches = new ConcurrentBag<MatchedPair>();
            int rowsCompleted = 0;
            int lastReportedPercent = -1;
            var reportGate = new object();

            Parallel.For(0, datasetA.Count, i =>
            {
                var (idA, vectorA, preservedA) = datasetA[i];

                for (int j = 0; j < datasetB.Count; j++)
                {
                    var (idB, vectorB, preservedB) = datasetB[j];
                    double score = CosineSimilarity(vectorA, vectorB);

                    if (score >= threshold)
                    {
                        matches.Add(new MatchedPair
                        {
                            RecordAId = idA,
                            RecordBId = idB,
                            SimilarityScore = score,
                            DatasetAPreserved = preservedA,
                            DatasetBPreserved = preservedB
                        });
                    }
                }

                if (progress is null)
                    return;

                int completed = Interlocked.Increment(ref rowsCompleted);
                int percent = (int)(completed * 100L / datasetA.Count);

                // Only forward whole-percent transitions so the UI is not flooded.
                lock (reportGate)
                {
                    if (percent > lastReportedPercent)
                    {
                        lastReportedPercent = percent;
                        progress.Report(percent);
                    }
                }
            });

            var ordered = matches.OrderByDescending(pair => pair.SimilarityScore).ToList();
            progress?.Report(100);
            return ordered;
        });
    }
}
