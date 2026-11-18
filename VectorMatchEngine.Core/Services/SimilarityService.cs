using System.Numerics;
using VectorMatchEngine.Core.Models;

namespace VectorMatchEngine.Core.Services;

/// <summary>
/// Matches two vectorized datasets: scores candidate pairs (whole rows by cosine, or field by field), keeps those at
/// or above the threshold, notes each pair's runner-up, and optionally keeps each record's best match only.
/// Without blocking every record in A is compared with every one in B; with it, only pairs that share an exact
/// field value or an LSH bucket are.
/// </summary>
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
        float dot = Dot(a, b);
        if (float.IsNaN(dot) || float.IsInfinity(dot))
            return 0d;

        // Clamp away floating-point drift just past the [0,1] boundary.
        return Math.Clamp((double)dot, 0d, 1d);
    }

    private static float Dot(float[] a, float[] b)
    {
        int length = Math.Min(a.Length, b.Length);
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
        return dot;
    }

    /// <summary>
    /// Finds the pairs whose score meets <paramref name="threshold"/>, as <paramref name="settings"/> says to score
    /// and pick them. Results are sorted by score, highest first; progress is reported as 0-100.
    /// </summary>
    public Task<MatchRun> FindMatchesAsync(
        List<MatchRecord> datasetA,
        List<MatchRecord> datasetB,
        double threshold,
        MatchSettings? settings = null,
        IProgress<int>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(datasetA);
        ArgumentNullException.ThrowIfNull(datasetB);
        return Task.Run(() => FindMatches(datasetA, datasetB, threshold, settings ?? new MatchSettings(), progress));
    }

    private sealed record Found(int A, int B, double Score, double?[]? FieldScores);

    /// <summary>A record's two best-scoring partners (indices stored plus one, so 0 is none).</summary>
    private struct Top2
    {
        public double S1, S2;
        public int K1, K2;

        public void Add(double score, int index)
        {
            if (K1 == 0 || score > S1)
            {
                S2 = S1; K2 = K1;
                S1 = score; K1 = index + 1;
            }
            else if (K2 == 0 || score > S2)
            {
                S2 = score; K2 = index + 1;
            }
        }

        public void Merge(Top2 other)
        {
            if (other.K1 != 0) Add(other.S1, other.K1 - 1);
            if (other.K2 != 0) Add(other.S2, other.K2 - 1);
        }

        /// <summary>The best score with anyone but <paramref name="index"/>.</summary>
        public double? OtherThan(int index) =>
            K1 != 0 && K1 - 1 != index ? S1 : K2 != 0 && K2 - 1 != index ? S2 : null;
    }

    private sealed class WorkerState(int countB, int fieldCount)
    {
        public readonly Top2[] TopB = new Top2[countB];
        public readonly int[] Seen = new int[countB];
        public readonly List<int> Candidates = new();
        public readonly double?[] FieldScores = new double?[fieldCount];
        public readonly List<Found> Found = new();
        public long Compared;
    }

    private MatchRun FindMatches(
        List<MatchRecord> a, List<MatchRecord> b, double threshold, MatchSettings settings, IProgress<int>? progress)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            progress?.Report(100);
            return new MatchRun();
        }

        var fields = settings.Fields;
        bool haveFields = fields.Count > 0 && a.All(r => r.Fields is not null) && b.All(r => r.Fields is not null);
        if (!haveFields && (settings.ScoreByField || settings.UsesFieldBlocking || fields.Any(f => f.MustMatch)))
            throw new InvalidOperationException(
                "Scoring, matching or blocking by field needs field vectors, which datasets imported before this " +
                "version don't have. Import them again.");
        if (haveFields)
        {
            int widthA = a.Min(r => r.Fields!.Length), widthB = b.Min(r => r.Fields!.Length);
            if (fields.FirstOrDefault(f => f.IndexA < 0 || f.IndexA >= widthA || f.IndexB < 0 || f.IndexB >= widthB) is { } bad)
                throw new InvalidOperationException($"The field pairing {bad.Label} doesn't fit the datasets' columns.");
        }
        double totalWeight = fields.Sum(f => Math.Max(0, f.Weight));
        if (settings.ScoreByField && totalWeight <= 0)
            throw new InvalidOperationException("Scoring field by field needs at least one paired field with a weight above 0.");

        var blocking = settings.UsesBlocking ? Blocking.Build(a, b, fields, settings.ApproximateBlocking, threshold) : null;

        var topA = new Top2[a.Count];
        var topB = new Top2[b.Count];
        var found = new List<Found>();
        long compared = 0;
        var gate = new object();
        int rowsDone = 0, lastPercent = -1;

        Parallel.For(0, a.Count,
            () => new WorkerState(b.Count, fields.Count),
            (i, _, state) =>
            {
                var recordA = a[i];
                var top = new Top2();

                void Score(int j)
                {
                    var recordB = b[j];
                    var scores = state.FieldScores;
                    if (haveFields)
                    {
                        for (int k = 0; k < fields.Count; k++)
                        {
                            var fieldA = recordA.Fields![fields[k].IndexA];
                            var fieldB = recordB.Fields![fields[k].IndexB];
                            scores[k] = fieldA.IsEmpty || fieldB.IsEmpty ? null : fieldA.Dot(fieldB);
                        }
                    }
                    state.Compared++;

                    // A must-match field below the threshold, or blank on either side, rules the pair out entirely:
                    // it isn't even a runner-up.
                    if (haveFields)
                        for (int k = 0; k < fields.Count; k++)
                            if (fields[k].MustMatch && (scores[k] is not { } s || s < threshold))
                                return;

                    double score;
                    if (settings.ScoreByField)
                    {
                        double sum = 0, weights = 0;
                        for (int k = 0; k < fields.Count; k++)
                            if (scores[k] is { } s && fields[k].Weight > 0)
                            {
                                sum += s * fields[k].Weight;
                                weights += fields[k].Weight;
                            }
                        // Blank fields are left out of the average, so missing data doesn't count against a pair.
                        score = weights > 0 ? sum / weights : 0;
                    }
                    else
                    {
                        score = CosineSimilarity(recordA.Vector, recordB.Vector);
                    }

                    top.Add(score, j);
                    state.TopB[j].Add(score, i);
                    if (score >= threshold)
                        state.Found.Add(new Found(i, j, score, haveFields ? (double?[])scores.Clone() : null));
                }

                if (blocking is null)
                {
                    for (int j = 0; j < b.Count; j++)
                        Score(j);
                }
                else
                {
                    blocking.CandidatesFor(i, state.Seen, state.Candidates);
                    foreach (var j in state.Candidates)
                        Score(j);
                }
                topA[i] = top;

                if (progress is not null)
                {
                    int done = Interlocked.Increment(ref rowsDone);
                    int percent = (int)(done * 100L / a.Count);
                    if (percent > Volatile.Read(ref lastPercent))
                        lock (gate)
                            if (percent > lastPercent)
                            {
                                lastPercent = percent;
                                progress.Report(Math.Min(percent, 99));
                            }
                }
                return state;
            },
            state =>
            {
                lock (gate)
                {
                    found.AddRange(state.Found);
                    for (int j = 0; j < topB.Length; j++)
                        topB[j].Merge(state.TopB[j]);
                    compared += state.Compared;
                }
            });

        var pairs = found
            .Select(f => (Found: f, RunnerUp: Max(topA[f.A].OtherThan(f.B), topB[f.B].OtherThan(f.A))))
            .OrderByDescending(p => p.Found.Score)
            .ThenByDescending(p => p.Found.Score - (p.RunnerUp ?? 0))
            .ToList();

        if (settings.OneToOne)
        {
            // Greedy: highest score first, each record used once. A pair that loses its record to a better one is
            // dropped; its record's other matches can still be kept.
            var usedA = new bool[a.Count];
            var usedB = new bool[b.Count];
            pairs = pairs.Where(p =>
            {
                if (usedA[p.Found.A] || usedB[p.Found.B])
                    return false;
                usedA[p.Found.A] = usedB[p.Found.B] = true;
                return true;
            }).ToList();
        }

        progress?.Report(100);
        return new MatchRun
        {
            PairsCompared = compared,
            Pairs = pairs.Select(p => new MatchedPair
            {
                RecordAId = a[p.Found.A].Id,
                RecordBId = b[p.Found.B].Id,
                SimilarityScore = p.Found.Score,
                FieldScores = p.Found.FieldScores,
                RunnerUpScore = p.RunnerUp,
                DatasetAPreserved = a[p.Found.A].Preserved,
                DatasetBPreserved = b[p.Found.B].Preserved
            }).ToList()
        };
    }

    private static double? Max(double? x, double? y) => x is null ? y : y is null ? x : Math.Max(x.Value, y.Value);

    // ── Blocking ──────────────────────────────────────────────────────────

    /// <summary>
    /// Which of B's records each A record is compared with: those with exactly the same value in a block field (the
    /// same field vector, so the same cleaned value or sounds-alike codes; blanks never block together), and, with
    /// LSH, those whose whole-row vector falls in the same bucket in any of the hash tables.
    /// </summary>
    private sealed class Blocking
    {
        private readonly List<(int IndexA, Dictionary<ulong, List<int>> ByValue)> _fieldBlocks = new();
        private readonly List<MatchRecord> _a;
        private HyperplaneLsh? _lsh;
        private Dictionary<int, List<int>>[] _lshTables = Array.Empty<Dictionary<int, List<int>>>();

        private Blocking(List<MatchRecord> a) => _a = a;

        public static Blocking Build(
            List<MatchRecord> a, List<MatchRecord> b, List<FieldPairing> fields, bool lsh, double threshold)
        {
            var blocking = new Blocking(a);
            foreach (var field in fields.Where(f => f.Block))
            {
                var byValue = new Dictionary<ulong, List<int>>();
                for (int j = 0; j < b.Count; j++)
                {
                    var vector = b[j].Fields![field.IndexB];
                    if (vector.IsEmpty)
                        continue;
                    var key = vector.Fingerprint();
                    if (!byValue.TryGetValue(key, out var list))
                        byValue[key] = list = new List<int>();
                    list.Add(j);
                }
                blocking._fieldBlocks.Add((field.IndexA, byValue));
            }

            if (lsh)
            {
                var hasher = HyperplaneLsh.ForThreshold(threshold, a[0].Vector.Length);
                var tables = new Dictionary<int, List<int>>[hasher.Tables];
                for (int t = 0; t < tables.Length; t++)
                    tables[t] = new Dictionary<int, List<int>>();
                var signatures = new int[b.Count][];
                Parallel.For(0, b.Count, j => signatures[j] = hasher.Signature(b[j].Vector));
                for (int j = 0; j < b.Count; j++)
                {
                    var signature = signatures[j];
                    if (signature.Length == 0)
                        continue;
                    for (int t = 0; t < tables.Length; t++)
                    {
                        if (!tables[t].TryGetValue(signature[t], out var list))
                            tables[t][signature[t]] = list = new List<int>();
                        list.Add(j);
                    }
                }
                blocking._lsh = hasher;
                blocking._lshTables = tables;
            }
            return blocking;
        }

        /// <summary>Fills <paramref name="candidates"/> with A record <paramref name="i"/>'s candidates, each once.</summary>
        public void CandidatesFor(int i, int[] seen, List<int> candidates)
        {
            candidates.Clear();
            int mark = i + 1;
            void Take(List<int> list)
            {
                foreach (var j in list)
                    if (seen[j] != mark)
                    {
                        seen[j] = mark;
                        candidates.Add(j);
                    }
            }

            var record = _a[i];
            foreach (var (indexA, byValue) in _fieldBlocks)
            {
                var vector = record.Fields![indexA];
                if (!vector.IsEmpty && byValue.TryGetValue(vector.Fingerprint(), out var list))
                    Take(list);
            }
            if (_lsh is not null)
            {
                var signature = _lsh.Signature(record.Vector);
                if (signature.Length > 0)
                    for (int t = 0; t < _lshTables.Length; t++)
                        if (_lshTables[t].TryGetValue(signature[t], out var list))
                            Take(list);
            }
        }
    }

    /// <summary>
    /// Random-hyperplane LSH for cosine similarity. Each table hashes a vector to K bits, one per hyperplane (which
    /// side of it the vector is on); two vectors at angle θ agree on a bit with probability 1 − θ/π, so pairs above
    /// a similarity land in the same bucket of at least one of L tables with high probability. K and L are chosen
    /// so pairs at 0.1 below the threshold are caught 99% of the time with at most 64 tables.
    /// </summary>
    private sealed class HyperplaneLsh
    {
        private const int MaxTables = 64;
        private const double TargetRecall = 0.99;
        private readonly float[][] _planes;

        public int Bits { get; }
        public int Tables { get; }

        private HyperplaneLsh(int bits, int tables, int dimensions)
        {
            Bits = bits;
            Tables = tables;
            // A fixed seed, so the same job buckets the same way every time.
            var random = new Random(20261118);
            _planes = new float[bits * tables][];
            for (int p = 0; p < _planes.Length; p++)
            {
                var plane = new float[dimensions];
                for (int d = 0; d < dimensions; d++)
                {
                    // Box-Muller: a normal deviate, so the hyperplanes' directions are uniform.
                    double u1 = 1.0 - random.NextDouble(), u2 = random.NextDouble();
                    plane[d] = (float)(Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
                }
                _planes[p] = plane;
            }
        }

        public static HyperplaneLsh ForThreshold(double threshold, int dimensions)
        {
            double target = Math.Clamp(threshold - 0.1, 0.3, 0.97);
            double agree = 1 - Math.Acos(target) / Math.PI;
            for (int bits = 16; bits >= 4; bits--)
            {
                double hit = Math.Pow(agree, bits);
                int tables = (int)Math.Ceiling(Math.Log(1 - TargetRecall) / Math.Log(1 - hit));
                if (tables <= MaxTables)
                    return new HyperplaneLsh(bits, Math.Max(tables, 1), dimensions);
            }
            return new HyperplaneLsh(4, MaxTables, dimensions);
        }

        /// <summary>The vector's bucket in each table; empty for an all-zero vector, which never matches anything.</summary>
        public int[] Signature(float[] vector)
        {
            if (Array.TrueForAll(vector, x => x == 0))
                return Array.Empty<int>();
            var signature = new int[Tables];
            for (int t = 0; t < Tables; t++)
            {
                int key = 0;
                for (int k = 0; k < Bits; k++)
                    if (Dot(_planes[t * Bits + k], vector) >= 0)
                        key |= 1 << k;
                signature[t] = key;
            }
            return signature;
        }
    }
}
