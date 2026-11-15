using System.Text.Json;

namespace Shortlist.Modules.Assessments.Domain;

public readonly record struct ScoreOutcome(decimal Score, bool Graded, string? Comment);

public static class ScoreCalculator
{
    public static ScoreOutcome Score(QuestionContent content, decimal points, string responseJson)
    {
        JsonElement response;
        try
        {
            response = JsonDocument.Parse(string.IsNullOrWhiteSpace(responseJson) ? "{}" : responseJson).RootElement;
        }
        catch (JsonException)
        {
            return content is LongAnswerContent
                ? new ScoreOutcome(0, false, null)
                : new ScoreOutcome(0, true, null);
        }

        return content switch
        {
            SingleChoiceContent single => new ScoreOutcome(ScoreSingle(single, points, response), true, null),
            MultipleChoiceContent multiple => new ScoreOutcome(ScoreMultiple(multiple, points, response), true, null),
            NumericContent numeric => new ScoreOutcome(ScoreNumeric(numeric, points, response), true, null),
            OrderingContent ordering => new ScoreOutcome(ScoreOrdering(ordering, points, response), true, null),
            MatchingContent matching => new ScoreOutcome(ScoreMatching(matching, points, response), true, null),
            LongAnswerContent => new ScoreOutcome(0, false, null),
            _ => new ScoreOutcome(0, false, null)
        };
    }

    private static decimal ScoreSingle(SingleChoiceContent content, decimal points, JsonElement response)
    {
        if (!TryGuid(response, "optionId", out var selected))
        {
            return 0;
        }

        var correct = content.Options.Single(option => option.IsCorrect).Id;
        return selected == correct ? points : 0;
    }

    private static decimal ScoreMultiple(MultipleChoiceContent content, decimal points, JsonElement response)
    {
        var selected = ReadGuids(response, "optionIds").ToHashSet();
        var correct = content.Options.Where(option => option.IsCorrect).Select(option => option.Id).ToHashSet();
        if (correct.Count == 0)
        {
            return 0;
        }

        if (content.PartialCredit == PartialCredit.None)
        {
            return selected.SetEquals(correct) ? points : 0;
        }

        var right = selected.Count(correct.Contains);
        var wrong = selected.Count(id => !correct.Contains(id));
        var raw = (right - wrong) / (decimal)correct.Count;
        return points * Math.Max(0, raw);
    }

    private static decimal ScoreNumeric(NumericContent content, decimal points, JsonElement response)
    {
        if (!response.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Number)
        {
            return 0;
        }

        var answered = value.GetDecimal();
        return Math.Abs(answered - content.CorrectValue) <= content.Tolerance ? points : 0;
    }

    private static decimal ScoreOrdering(OrderingContent content, decimal points, JsonElement response)
    {
        var submitted = ReadGuids(response, "itemIds");
        var correct = content.Items.Select(item => item.Id).ToList();
        if (submitted.Count == 0 || correct.Count == 0)
        {
            return 0;
        }

        return content.PartialCredit switch
        {
            PartialCredit.PerPosition => points * correct.Select((id, index) => index < submitted.Count && submitted[index] == id).Count(match => match) / (decimal)correct.Count,
            PartialCredit.PerAdjacentPair => ScoreAdjacent(correct, submitted, points),
            _ => submitted.SequenceEqual(correct) ? points : 0
        };
    }

    private static decimal ScoreAdjacent(IReadOnlyList<Guid> correct, IReadOnlyList<Guid> submitted, decimal points)
    {
        if (correct.Count < 2)
        {
            return 0;
        }

        var correctPairs = new HashSet<(Guid, Guid)>();
        for (var i = 0; i < correct.Count - 1; i++)
        {
            correctPairs.Add((correct[i], correct[i + 1]));
        }

        var matched = 0;
        for (var i = 0; i < submitted.Count - 1; i++)
        {
            if (correctPairs.Contains((submitted[i], submitted[i + 1])))
            {
                matched++;
            }
        }

        return points * matched / (decimal)(correct.Count - 1);
    }

    private static decimal ScoreMatching(MatchingContent content, decimal points, JsonElement response)
    {
        var submitted = new Dictionary<Guid, Guid>();
        if (response.TryGetProperty("pairs", out var pairs) && pairs.ValueKind == JsonValueKind.Array)
        {
            foreach (var pair in pairs.EnumerateArray())
            {
                if (TryGuid(pair, "leftId", out var left) && TryGuid(pair, "rightId", out var right))
                {
                    submitted[left] = right;
                }
            }
        }

        var total = content.Pairs.Count;
        if (total == 0)
        {
            return 0;
        }

        var matched = content.Pairs.Count(pair => submitted.TryGetValue(pair.Id, out var right) && right == pair.Id);
        if (content.PartialCredit == PartialCredit.None)
        {
            return matched == total ? points : 0;
        }

        return points * matched / (decimal)total;
    }

    private static bool TryGuid(JsonElement element, string name, out Guid id)
    {
        id = default;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
            && Guid.TryParse(property.GetString(), out id);
    }

    private static List<Guid> ReadGuids(JsonElement element, string name)
    {
        var ids = new List<Guid>();
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return ids;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && Guid.TryParse(item.GetString(), out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}

public static class Deadlines
{
    public static DateTimeOffset Compute(DateTimeOffset startedAt, int totalMinutes, int extraTimePercent)
    {
        var percent = Math.Max(0, extraTimePercent);
        var seconds = totalMinutes * 60d * (100 + percent) / 100d;
        return startedAt.AddSeconds(seconds);
    }
}

public static class StableShuffle
{
    public static IReadOnlyList<T> Shuffle<T>(IReadOnlyList<T> items, Guid seed)
    {
        var copy = items.ToList();
        var state = BitConverter.ToUInt32(seed.ToByteArray(), 0);
        if (state == 0)
        {
            state = 1;
        }

        for (var i = copy.Count - 1; i > 0; i--)
        {
            state = unchecked(state * 1664525 + 1013904223);
            var index = (int)(state % (uint)(i + 1));
            (copy[i], copy[index]) = (copy[index], copy[i]);
        }

        return copy;
    }
}
