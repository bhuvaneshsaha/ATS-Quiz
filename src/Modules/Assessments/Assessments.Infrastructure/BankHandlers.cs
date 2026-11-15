using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shortlist.BuildingBlocks.Abstractions;
using Shortlist.BuildingBlocks.Application;
using Shortlist.Modules.Assessments.Application;
using Shortlist.Modules.Assessments.Domain;

namespace Shortlist.Modules.Assessments.Infrastructure;

internal static class QuestionMapper
{
    public static QuestionResponse Map(Question question)
    {
        var version = question.Current();
        return new QuestionResponse(
            question.Id,
            question.FunctionId,
            question.Status.ToString(),
            version.Id,
            version.Number,
            version.Type,
            version.Points,
            version.IsPublished,
            JsonSerializer.SerializeToElement(version.Content, version.Content.GetType(), QuestionJson.Options));
    }

    public static QuestionContent Parse(JsonElement content)
    {
        var parsed = JsonSerializer.Deserialize<QuestionContent>(content.GetRawText(), QuestionJson.Options);
        return parsed ?? throw new DomainRuleException("question.content", "The question content is not valid.");
    }
}

internal sealed class CreateQuestionHandler(AssessmentsDbContext db) : ICommandHandler<CreateQuestion, Result<QuestionResponse>>
{
    public async Task<Result<QuestionResponse>> Handle(CreateQuestion command, CancellationToken cancellationToken)
    {
        var question = Question.Create(command.FunctionId, QuestionMapper.Parse(command.Content), command.Points, command.TimeLimitSeconds, command.SkillIds);
        db.Questions.Add(question);
        await db.SaveChangesAsync(cancellationToken);
        return QuestionMapper.Map(question);
    }
}

internal sealed class UpdateQuestionDraftHandler(AssessmentsDbContext db) : ICommandHandler<UpdateQuestionDraft, Result<QuestionResponse>>
{
    public async Task<Result<QuestionResponse>> Handle(UpdateQuestionDraft command, CancellationToken cancellationToken)
    {
        var question = await db.Questions.Include(item => item.Versions).SingleOrDefaultAsync(item => item.Id == command.QuestionId, cancellationToken);
        if (question is null)
        {
            return Error.NotFoundError("question.not_found", "Question was not found.");
        }

        question.Current().ReplaceDraft(QuestionMapper.Parse(command.Content), command.Points, command.TimeLimitSeconds, command.SkillIds);
        await db.SaveChangesAsync(cancellationToken);
        return QuestionMapper.Map(question);
    }
}

internal sealed class SubmitQuestionHandler(AssessmentsDbContext db) : ICommandHandler<SubmitQuestion, Result<QuestionResponse>>
{
    public async Task<Result<QuestionResponse>> Handle(SubmitQuestion command, CancellationToken cancellationToken)
    {
        var question = await Load(db, command.QuestionId, cancellationToken);
        if (question is null)
        {
            return Error.NotFoundError("question.not_found", "Question was not found.");
        }

        var failure = question.SubmitForReview();
        if (failure is not null)
        {
            return Error.ConflictError(failure.Value.Code, failure.Value.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return QuestionMapper.Map(question);
    }

    internal static Task<Question?> Load(AssessmentsDbContext db, Guid id, CancellationToken cancellationToken) =>
        db.Questions.Include(item => item.Versions).SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
}

internal sealed class PublishQuestionHandler(AssessmentsDbContext db, IClock clock) : ICommandHandler<PublishQuestion, Result<QuestionResponse>>
{
    public async Task<Result<QuestionResponse>> Handle(PublishQuestion command, CancellationToken cancellationToken)
    {
        var question = await SubmitQuestionHandler.Load(db, command.QuestionId, cancellationToken);
        if (question is null)
        {
            return Error.NotFoundError("question.not_found", "Question was not found.");
        }

        var failure = question.Publish(clock.UtcNow);
        if (failure is not null)
        {
            return Error.ConflictError(failure.Value.Code, failure.Value.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return QuestionMapper.Map(question);
    }
}

internal sealed class ReviseQuestionHandler(AssessmentsDbContext db) : ICommandHandler<ReviseQuestion, Result<QuestionResponse>>
{
    public async Task<Result<QuestionResponse>> Handle(ReviseQuestion command, CancellationToken cancellationToken)
    {
        var question = await SubmitQuestionHandler.Load(db, command.QuestionId, cancellationToken);
        if (question is null)
        {
            return Error.NotFoundError("question.not_found", "Question was not found.");
        }

        var failure = question.Revise(QuestionMapper.Parse(command.Content), command.Points, command.TimeLimitSeconds, command.SkillIds);
        if (failure is not null)
        {
            return Error.ConflictError(failure.Value.Code, failure.Value.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return QuestionMapper.Map(question);
    }
}

internal sealed class ListQuestionsHandler(AssessmentsDbContext db) : IQueryHandler<ListQuestions, Result<IReadOnlyList<QuestionResponse>>>
{
    public async Task<Result<IReadOnlyList<QuestionResponse>>> Handle(ListQuestions query, CancellationToken cancellationToken)
    {
        var questions = db.Questions.Include(question => question.Versions).AsQueryable();
        if (query.FunctionId is Guid functionId)
        {
            questions = questions.Where(question => question.FunctionId == functionId);
        }

        if (!string.IsNullOrWhiteSpace(query.Status) && Enum.TryParse<QuestionStatus>(query.Status, true, out var status))
        {
            questions = questions.Where(question => question.Status == status);
        }

        var rows = await questions.OrderByDescending(question => question.CreatedAt).ToListAsync(cancellationToken);
        return rows.Select(QuestionMapper.Map).ToList();
    }
}

internal sealed class GetQuestionHandler(AssessmentsDbContext db) : IQueryHandler<GetQuestion, Result<QuestionResponse>>
{
    public async Task<Result<QuestionResponse>> Handle(GetQuestion query, CancellationToken cancellationToken)
    {
        var question = await SubmitQuestionHandler.Load(db, query.QuestionId, cancellationToken);
        return question is null
            ? Error.NotFoundError("question.not_found", "Question was not found.")
            : QuestionMapper.Map(question);
    }
}

internal sealed class CreateAssessmentHandler(AssessmentsDbContext db) : ICommandHandler<CreateAssessment, Result<AssessmentResponse>>
{
    public async Task<Result<AssessmentResponse>> Handle(CreateAssessment command, CancellationToken cancellationToken)
    {
        var assessment = Assessment.Create(command.FunctionId, command.Name);
        assessment.AddDraft(command.PassMark, command.DesktopOnly, command.Sections.Select(Map).ToList());
        db.Assessments.Add(assessment);
        await db.SaveChangesAsync(cancellationToken);
        return AssessmentView.MapAssessment(assessment);
    }

    internal static SectionDraft Map(SectionInput input) =>
        new(
            input.Name,
            input.Minutes,
            input.Weight,
            input.Source.Equals("random", StringComparison.OrdinalIgnoreCase) ? SectionSource.RandomDraw : SectionSource.Fixed,
            input.QuestionVersionIds,
            input.DrawCount is int count
                ? new RandomDraw(count, input.DrawTypes ?? [], input.SkillIds ?? [])
                : null);
}

internal sealed class PublishAssessmentHandler(AssessmentsDbContext db, IClock clock) : ICommandHandler<PublishAssessment, Result<AssessmentResponse>>
{
    public async Task<Result<AssessmentResponse>> Handle(PublishAssessment command, CancellationToken cancellationToken)
    {
        var assessment = await db.Assessments.Include(item => item.Versions).ThenInclude(version => version.Sections)
            .SingleOrDefaultAsync(item => item.Id == command.AssessmentId, cancellationToken);
        if (assessment is null)
        {
            return Error.NotFoundError("assessment.not_found", "Assessment was not found.");
        }

        var failure = assessment.Publish(clock.UtcNow);
        if (failure is not null)
        {
            return Error.ConflictError(failure.Value.Code, failure.Value.Message);
        }

        await db.SaveChangesAsync(cancellationToken);
        return AssessmentView.MapAssessment(assessment);
    }
}

internal sealed class ListAssessmentsHandler(AssessmentsDbContext db) : IQueryHandler<ListAssessments, Result<IReadOnlyList<AssessmentResponse>>>
{
    public async Task<Result<IReadOnlyList<AssessmentResponse>>> Handle(ListAssessments query, CancellationToken cancellationToken)
    {
        var assessments = db.Assessments.Include(item => item.Versions).ThenInclude(version => version.Sections).AsQueryable();
        if (query.FunctionId is Guid functionId)
        {
            assessments = assessments.Where(item => item.FunctionId == functionId);
        }

        var rows = await assessments.OrderBy(item => item.Name).ToListAsync(cancellationToken);
        return rows.Select(AssessmentView.MapAssessment).ToList();
    }
}

internal static class AssessmentView
{
    public static AssessmentResponse MapAssessment(Assessment assessment)
    {
        var current = assessment.CurrentVersionId is Guid id ? assessment.Versions.SingleOrDefault(version => version.Id == id) : null;
        return new AssessmentResponse(assessment.Id, assessment.FunctionId, assessment.Name, assessment.Status.ToString(), current is null ? null : MapVersion(current));
    }

    public static AssessmentVersionResponse MapVersion(AssessmentVersion version) =>
        new(
            version.Id,
            version.Number,
            version.PassMark,
            version.DesktopOnly,
            version.IsPublished,
            version.Sections.OrderBy(section => section.Order).Select(section => new SectionResponse(
                section.Id,
                section.Order,
                section.Name,
                section.Minutes,
                section.Weight,
                section.Source.ToString(),
                section.QuestionVersionIds,
                section.Draw?.Count)).ToList());
}
