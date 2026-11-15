using System.Text.Json.Serialization;
using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Modules.Assessments.Domain;

public enum QuestionStatus
{
    Draft,
    InReview,
    Published,
    Archived
}

public enum AssessmentStatus
{
    Draft,
    Published,
    Archived
}

public enum PartialCredit
{
    None,
    PerPosition,
    PerAdjacentPair,
    PerCorrectOption
}

public enum SectionSource
{
    Fixed,
    RandomDraw
}

public enum InvitationStatus
{
    Pending,
    Sent,
    Started,
    Submitted,
    Expired,
    Cancelled
}

public enum AttemptStatus
{
    InProgress,
    Submitted,
    Expired
}

public enum ResultStatus
{
    Provisional,
    Final
}

public sealed record Attachment(string FileKey, string FileName, string ContentType);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SingleChoiceContent), "single_choice")]
[JsonDerivedType(typeof(MultipleChoiceContent), "multiple_choice")]
[JsonDerivedType(typeof(NumericContent), "numeric")]
[JsonDerivedType(typeof(LongAnswerContent), "long_answer")]
[JsonDerivedType(typeof(OrderingContent), "ordering")]
[JsonDerivedType(typeof(MatchingContent), "matching")]
public abstract record QuestionContent(string Prompt, IReadOnlyList<Attachment> Attachments);

public sealed record ChoiceOption(Guid Id, string Text, bool IsCorrect);

public sealed record SingleChoiceContent(
    string Prompt,
    IReadOnlyList<Attachment> Attachments,
    IReadOnlyList<ChoiceOption> Options) : QuestionContent(Prompt, Attachments);

public sealed record MultipleChoiceContent(
    string Prompt,
    IReadOnlyList<Attachment> Attachments,
    IReadOnlyList<ChoiceOption> Options,
    PartialCredit PartialCredit) : QuestionContent(Prompt, Attachments);

public sealed record NumericContent(
    string Prompt,
    IReadOnlyList<Attachment> Attachments,
    decimal CorrectValue,
    decimal Tolerance) : QuestionContent(Prompt, Attachments);

public sealed record LongAnswerContent(
    string Prompt,
    IReadOnlyList<Attachment> Attachments,
    string Rubric) : QuestionContent(Prompt, Attachments);

public sealed record OrderingItem(Guid Id, string Text);

public sealed record OrderingContent(
    string Prompt,
    IReadOnlyList<Attachment> Attachments,
    IReadOnlyList<OrderingItem> Items,
    PartialCredit PartialCredit) : QuestionContent(Prompt, Attachments);

public sealed record MatchingPair(Guid Id, string Left, string Right);

public sealed record MatchingContent(
    string Prompt,
    IReadOnlyList<Attachment> Attachments,
    IReadOnlyList<MatchingPair> Pairs,
    PartialCredit PartialCredit) : QuestionContent(Prompt, Attachments);

public static class QuestionRules
{
    public static RuleFailure? Validate(QuestionContent content, decimal points)
    {
        if (string.IsNullOrWhiteSpace(content.Prompt))
        {
            return new RuleFailure("question.prompt", "A question needs a prompt.");
        }

        if (points <= 0)
        {
            return new RuleFailure("question.points", "Points must be greater than zero.");
        }

        return content switch
        {
            SingleChoiceContent single when single.Options.Count(option => option.IsCorrect) != 1
                => new RuleFailure("question.options", "A single-choice question needs exactly one correct option."),
            SingleChoiceContent single when single.Options.Count < 2
                => new RuleFailure("question.options", "A single-choice question needs at least two options."),
            MultipleChoiceContent multiple when multiple.Options.Count(option => option.IsCorrect) < 1
                => new RuleFailure("question.options", "A multiple-choice question needs a correct option."),
            NumericContent numeric when numeric.Tolerance < 0
                => new RuleFailure("question.tolerance", "Tolerance cannot be negative."),
            OrderingContent ordering when ordering.Items.Count < 2
                => new RuleFailure("question.items", "An ordering question needs at least two items."),
            MatchingContent matching when matching.Pairs.Count < 2
                => new RuleFailure("question.pairs", "A matching question needs at least two pairs."),
            _ => null
        };
    }

    public static string TypeName(QuestionContent content) => content switch
    {
        SingleChoiceContent => "single_choice",
        MultipleChoiceContent => "multiple_choice",
        NumericContent => "numeric",
        LongAnswerContent => "long_answer",
        OrderingContent => "ordering",
        MatchingContent => "matching",
        _ => "unknown"
    };

    public static bool IsAutoGraded(QuestionContent content) => content is not LongAnswerContent;
}
