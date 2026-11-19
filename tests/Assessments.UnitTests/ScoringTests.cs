using System.Text.Json;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.Modules.Assessments.Domain;
using Shouldly;

namespace Shortlist.Assessments.UnitTests;

public class ScoringTests
{
    [Fact]
    public void Single_choice_scores_the_correct_option_only()
    {
        var correct = Guid.CreateVersion7();
        var wrong = Guid.CreateVersion7();
        var content = new SingleChoiceContent("Pick one", [], [new(correct, "Yes", true), new(wrong, "No", false)]);
        ScoreCalculator.Score(content, 2, Response(("optionId", correct))).Score.ShouldBe(2);
        ScoreCalculator.Score(content, 2, Response(("optionId", wrong))).Score.ShouldBe(0);
        ScoreCalculator.Score(content, 2, "{}").Graded.ShouldBeTrue();
    }

    [Fact]
    public void Multiple_choice_supports_all_or_nothing_and_partial_credit()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();
        var c = Guid.CreateVersion7();
        var options = new ChoiceOption[] { new(a, "A", true), new(b, "B", true), new(c, "C", false) };
        var allOrNothing = new MultipleChoiceContent("Pick", [], options, PartialCredit.None);
        ScoreCalculator.Score(allOrNothing, 4, Ids("optionIds", a, b)).Score.ShouldBe(4);
        ScoreCalculator.Score(allOrNothing, 4, Ids("optionIds", a)).Score.ShouldBe(0);

        var partial = new MultipleChoiceContent("Pick", [], options, PartialCredit.PerCorrectOption);
        ScoreCalculator.Score(partial, 4, Ids("optionIds", a)).Score.ShouldBe(2);
        ScoreCalculator.Score(partial, 4, Ids("optionIds", a, c)).Score.ShouldBe(0);
        ScoreCalculator.Score(partial, 4, Ids("optionIds", a, b, c)).Score.ShouldBe(2);
    }

    [Fact]
    public void Numeric_answers_use_the_stored_tolerance()
    {
        var content = new NumericContent("How many", [], 10m, 0.5m);
        ScoreCalculator.Score(content, 3, """{"value":10.4}""").Score.ShouldBe(3);
        ScoreCalculator.Score(content, 3, """{"value":11}""").Score.ShouldBe(0);
    }

    [Fact]
    public void Ordering_scores_exact_positions_and_adjacent_pairs()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var third = Guid.CreateVersion7();
        var items = new OrderingItem[] { new(first, "1"), new(second, "2"), new(third, "3") };
        var positions = new OrderingContent("Order", [], items, PartialCredit.PerPosition);
        ScoreCalculator.Score(positions, 3, Ids("itemIds", first, third, second)).Score.ShouldBe(1);

        var adjacent = new OrderingContent("Order", [], items, PartialCredit.PerAdjacentPair);
        ScoreCalculator.Score(adjacent, 3, Ids("itemIds", second, third, first)).Score.ShouldBe(1.5m);
        ScoreCalculator.Score(adjacent, 3, Ids("itemIds", first, second, third)).Score.ShouldBe(3);
    }

    [Fact]
    public void Matching_scores_pairs_by_shared_identity()
    {
        var one = Guid.CreateVersion7();
        var two = Guid.CreateVersion7();
        var pairs = new MatchingPair[] { new(one, "Left 1", "Right 1"), new(two, "Left 2", "Right 2") };
        var partial = new MatchingContent("Match", [], pairs, PartialCredit.PerCorrectOption);
        ScoreCalculator.Score(partial, 4, Pairs((one, one))).Score.ShouldBe(2);
        var exact = new MatchingContent("Match", [], pairs, PartialCredit.None);
        ScoreCalculator.Score(exact, 4, Pairs((one, one), (two, two))).Score.ShouldBe(4);
        ScoreCalculator.Score(exact, 4, Pairs((one, one))).Score.ShouldBe(0);
    }

    [Fact]
    public void Long_answers_are_not_auto_graded()
    {
        var content = new LongAnswerContent("Explain", [], "Clarity and accuracy");
        var outcome = ScoreCalculator.Score(content, 5, """{"text":"Because"}""");
        outcome.Graded.ShouldBeFalse();
        outcome.Score.ShouldBe(0);
    }

    [Fact]
    public void Extra_time_extends_the_server_deadline()
    {
        var started = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
        Deadlines.Compute(started, 30, 0).ShouldBe(started.AddMinutes(30));
        Deadlines.Compute(started, 30, 50).ShouldBe(started.AddMinutes(45));
    }

    [Fact]
    public void A_closed_attempt_rejects_a_late_save()
    {
        var questionId = Guid.CreateVersion7();
        var sectionId = Guid.CreateVersion7();
        var started = DateTimeOffset.UtcNow.AddMinutes(-10);
        var attempt = Attempt.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), started, started.AddMinutes(5), new Paper([new PaperSection(sectionId, "One", 5, 1, [questionId])]));
        var failure = attempt.EnsureOpen(DateTimeOffset.UtcNow, sectionId);
        failure.ShouldNotBeNull();
        failure.Value.Code.ShouldBe("attempt.closed");
    }

    [Fact]
    public void Publishing_a_question_freezes_that_version()
    {
        var original = Choice("First wording");
        var question = Question.Create(Guid.CreateVersion7(), original, 1, null, []);
        question.SubmitForReview().ShouldBeNull();
        question.Publish(DateTimeOffset.UtcNow).ShouldBeNull();
        var published = question.Current();
        var act = () => published.ReplaceDraft(Choice("Edited"), 1, null, []);
        var error = Should.Throw<DomainRuleException>(act);
        error.Code.ShouldBe("question.version_published");

        question.Revise(Choice("Second wording"), 2, null, []).ShouldBeNull();
        question.Versions.Single(version => version.Number == 1).Content.ShouldBe(original);
        question.Current().Number.ShouldBe(2);
        question.Status.ShouldBe(QuestionStatus.Draft);
    }

    [Fact]
    public void Presented_questions_omit_answer_keys()
    {
        var content = new SingleChoiceContent("Pick", [], [new(Guid.CreateVersion7(), "Yes", true), new(Guid.CreateVersion7(), "No", false)]);
        var question = Question.Create(Guid.CreateVersion7(), content, 1, null, []);
        var presented = QuestionPresenter.Present(question.Current(), Guid.CreateVersion7());
        var json = JsonSerializer.Serialize(presented, QuestionJson.Options);
        json.Contains("isCorrect", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        json.Contains("rubric", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        json.Contains("correctValue", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        json.Contains("answerKey", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        presented.Options.Select(option => option.Text).ShouldBe(["Yes", "No"], ignoreOrder: true);
    }

    [Fact]
    public void Section_deadlines_share_extra_time_across_the_paper()
    {
        var first = Guid.CreateVersion7();
        var second = Guid.CreateVersion7();
        var started = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
        var deadline = Deadlines.Compute(started, 15, 50);
        var attempt = Attempt.Start(Guid.CreateVersion7(), Guid.CreateVersion7(), started, deadline, new Paper(
        [
            new PaperSection(first, "Knowledge", 10, 1, []),
            new PaperSection(second, "Writing", 5, 1, [])
        ]));

        attempt.SectionDeadline(first).ShouldBe(started.AddMinutes(15));
        attempt.SectionDeadline(second).ShouldBe(started.AddMinutes(22.5));
        var closed = attempt.EnsureOpen(started.AddMinutes(16), first);
        closed.ShouldNotBeNull();
        closed.Value.Code.ShouldBe("attempt.section_closed");
        attempt.EnsureOpen(started.AddMinutes(16), second).ShouldBeNull();
    }

    [Fact]
    public void A_submit_after_the_grace_period_is_expired()
    {
        var deadline = DateTimeOffset.UtcNow;
        Attempt.StatusForClose(AttemptStatus.Submitted, deadline.AddSeconds(2), deadline).ShouldBe(AttemptStatus.Submitted);
        Attempt.StatusForClose(AttemptStatus.Submitted, deadline.AddSeconds(6), deadline).ShouldBe(AttemptStatus.Expired);
        Attempt.StatusForClose(AttemptStatus.Expired, deadline.AddMinutes(1), deadline).ShouldBe(AttemptStatus.Expired);
    }

    [Fact]
    public void Camel_case_partial_credit_deserializes()
    {
        var id = Guid.CreateVersion7();
        var json = $$"""
            {"type":"multiple_choice","prompt":"Pick","attachments":[],"partialCredit":"none","options":[{"id":"{{id}}","text":"A","isCorrect":true},{"id":"{{Guid.CreateVersion7()}}","text":"B","isCorrect":false}]}
            """;
        var content = JsonSerializer.Deserialize<QuestionContent>(json, QuestionJson.Options);
        var multiple = content.ShouldBeOfType<MultipleChoiceContent>();
        multiple.PartialCredit.ShouldBe(PartialCredit.None);
        multiple.Options.Count(option => option.IsCorrect).ShouldBe(1);
    }

    private static SingleChoiceContent Choice(string prompt) =>
        new(prompt, [], [new(Guid.CreateVersion7(), "Yes", true), new(Guid.CreateVersion7(), "No", false)]);

    private static string Response(params (string Name, Guid Id)[] fields)
    {
        var payload = fields.ToDictionary(field => field.Name, field => field.Id.ToString());
        return JsonSerializer.Serialize(payload);
    }

    private static string Ids(string name, params Guid[] ids) =>
        JsonSerializer.Serialize(new Dictionary<string, string[]> { [name] = ids.Select(id => id.ToString()).ToArray() });

    private static string Pairs(params (Guid Left, Guid Right)[] pairs) =>
        JsonSerializer.Serialize(new { pairs = pairs.Select(pair => new { leftId = pair.Left, rightId = pair.Right }) });
}
