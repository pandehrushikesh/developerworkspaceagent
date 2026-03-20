using System.Text.RegularExpressions;
using ProjectLens.Application.Abstractions;

namespace ProjectLens.Infrastructure.SemanticSearch;

public sealed class DeterministicEmbeddingService : IEmbeddingService
{
    private const int VectorSize = 192;

    private static readonly Dictionary<string, string[]> SemanticFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        ["auth"] = ["auth", "authenticate", "authentication", "signin", "sign", "login", "credential", "identity"],
        ["token"] = ["token", "jwt", "bearer", "refresh", "session", "claim"],
        ["validate"] = ["validate", "validation", "verify", "verification", "check", "guard"],
        ["save"] = ["save", "saved", "persist", "store", "write", "insert", "commit"],
        ["content"] = ["content", "body", "text", "post", "article", "entry", "page"],
        ["publish"] = ["publish", "publishing", "post", "release", "submit"],
        ["handle"] = ["handle", "handler", "controller", "endpoint", "route", "action"]
    };

    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(
        IReadOnlyList<string> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        var vectors = new List<float[]>(inputs.Count);
        foreach (var input in inputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            vectors.Add(CreateVector(input));
        }

        return Task.FromResult<IReadOnlyList<float[]>>(vectors);
    }

    private static float[] CreateVector(string input)
    {
        var vector = new float[VectorSize];
        foreach (var token in ExpandSemanticTokens(Tokenize(input)))
        {
            var normalizedToken = NormalizeToken(token);
            if (string.IsNullOrWhiteSpace(normalizedToken))
            {
                continue;
            }

            AddWeightedSignal(vector, normalizedToken, 1f);

            foreach (var trigram in BuildCharacterTrigrams(normalizedToken))
            {
                AddWeightedSignal(vector, trigram, 0.35f);
            }
        }

        Normalize(vector);
        return vector;
    }

    private static IEnumerable<string> ExpandSemanticTokens(IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
        {
            yield return token;

            foreach (var family in SemanticFamilies)
            {
                if (family.Value.Any(member => token.Equals(member, StringComparison.OrdinalIgnoreCase)))
                {
                    yield return family.Key;
                    break;
                }
            }
        }
    }

    private static IEnumerable<string> Tokenize(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            yield break;
        }

        var expanded = Regex.Replace(input, "([a-z])([A-Z])", "$1 $2");
        foreach (Match match in Regex.Matches(expanded.ToLowerInvariant(), "[a-z0-9]{3,}"))
        {
            yield return match.Value;
        }
    }

    private static string NormalizeToken(string token)
    {
        return token
            .Trim()
            .TrimEnd('s')
            .Replace("ing", string.Empty, StringComparison.Ordinal)
            .Replace("tion", "te", StringComparison.Ordinal)
            .Replace("ed", string.Empty, StringComparison.Ordinal);
    }

    private static IEnumerable<string> BuildCharacterTrigrams(string token)
    {
        if (token.Length <= 3)
        {
            yield return token;
            yield break;
        }

        for (var index = 0; index <= token.Length - 3; index++)
        {
            yield return token.Substring(index, 3);
        }
    }

    private static void AddWeightedSignal(float[] vector, string token, float weight)
    {
        var hash = StringComparer.OrdinalIgnoreCase.GetHashCode(token);
        var index = Math.Abs(hash % VectorSize);
        var sign = (hash & 1) == 0 ? 1f : -1f;
        vector[index] += sign * weight;
    }

    private static void Normalize(float[] vector)
    {
        var magnitude = Math.Sqrt(vector.Sum(value => value * value));
        if (magnitude <= 0)
        {
            return;
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] / magnitude);
        }
    }
}
