using System.Text.Json;
using Shouldly;

namespace Shortlist.IntegrationTests;

[Collection("api")]
public class HiringJourneyTests(ShortlistApi api)
{
    [Fact]
    public async Task A_candidate_completes_a_pinned_test_and_an_older_save_is_ignored()
    {
        var session = new ApiSession(api.CreateCookieClient());
        await session.SignIn();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var unit = await session.Post("/api/core/v1/hiring-units", new { type = "Department", name = $"Engineering {suffix}", ownerUserId = (Guid?)null, externalId = (string?)null }, idempotent: true);
        var function = await session.Post("/api/core/v1/functions", new { name = $"Platform {suffix}", ownerUserId = (Guid?)null }, idempotent: true);
        var opening = await session.Post("/api/core/v1/openings", new
        {
            title = $"Platform engineer {suffix}",
            hiringUnitId = unit.GetProperty("id").GetGuid(),
            functionId = function.GetProperty("id").GetGuid(),
            positions = 1,
            location = "Remote",
            seniority = "Mid",
            reason = "Growth",
            targetDate = (DateOnly?)null,
            hiringManagerId = (Guid?)null,
            recruiterId = (Guid?)null,
            externalId = (string?)null
        }, idempotent: true);
        await session.Post($"/api/core/v1/openings/{opening.GetProperty("id").GetGuid()}/status", new { status = "Open" });

        var correct = Guid.NewGuid();
        var wrong = Guid.NewGuid();
        var question = await session.Post("/api/assessments/v1/questions", new
        {
            functionId = function.GetProperty("id").GetGuid(),
            points = 4,
            timeLimitSeconds = (int?)null,
            skillIds = Array.Empty<Guid>(),
            content = new
            {
                type = "single_choice",
                prompt = "Which layer owns the opening?",
                attachments = Array.Empty<object>(),
                options = new[]
                {
                    new { id = correct, text = "Core", isCorrect = true },
                    new { id = wrong, text = "The browser", isCorrect = false }
                }
            }
        }, idempotent: true);
        var questionId = question.GetProperty("id").GetGuid();
        await session.Post($"/api/assessments/v1/questions/{questionId}/submit", new { });
        var publishedQuestion = await session.Post($"/api/assessments/v1/questions/{questionId}/publish", new { });
        var versionId = publishedQuestion.GetProperty("currentVersionId").GetGuid();

        var assessment = await session.Post("/api/assessments/v1/assessments", new
        {
            functionId = function.GetProperty("id").GetGuid(),
            name = $"Screen {suffix}",
            passMark = 50,
            desktopOnly = false,
            sections = new[]
            {
                new
                {
                    name = "Core",
                    minutes = 20,
                    weight = 1,
                    source = "fixed",
                    questionVersionIds = new[] { versionId },
                    drawCount = (int?)null,
                    drawTypes = (string[]?)null,
                    skillIds = (Guid[]?)null
                }
            }
        }, idempotent: true);
        var published = await session.Post($"/api/assessments/v1/assessments/{assessment.GetProperty("id").GetGuid()}/publish", new { });
        var assessmentVersionId = published.GetProperty("current").GetProperty("id").GetGuid();
        var assessmentStage = opening.GetProperty("stages").EnumerateArray().Single(stage => stage.GetProperty("type").GetString() == "Assessment");
        await session.Post("/api/assessments/v1/bindings", new
        {
            openingStageId = assessmentStage.GetProperty("id").GetGuid(),
            assessmentVersionId,
            passMarkOverride = (decimal?)null,
            validDays = 7,
            extraTimePercent = 0
        }, idempotent: true);

        var email = $"ada.{suffix}@example.com";
        var candidate = await session.Post("/api/core/v1/candidates", new
        {
            name = "Ada Lovelace",
            email,
            phone = (string?)null,
            location = "London",
            source = "Referral",
            externalId = (string?)null,
            consent = true,
            consentText = "I agree to the assessment."
        }, idempotent: true);
        var application = await session.Post($"/api/core/v1/openings/{opening.GetProperty("id").GetGuid()}/applications", new { candidateId = candidate.GetProperty("id").GetGuid() }, idempotent: true);
        await session.Post($"/api/core/v1/openings/{opening.GetProperty("id").GetGuid()}/applications/{application.GetProperty("id").GetGuid()}/stage", new { toStageId = assessmentStage.GetProperty("id").GetGuid() });

        var invite = await WaitForEmail(session, email, "Open your assessment:");
        var token = invite.Split("/take/", StringSplitOptions.None)[1].Split('\n', StringSplitOptions.None)[0].Trim();
        await session.Post($"/api/assessments/v1/take/{token}/code", new { });
        var codeMail = await WaitForEmail(session, email, "Your code is");
        var code = codeMail.Split("Your code is ", StringSplitOptions.None)[1].Split('.', StringSplitOptions.None)[0].Trim();
        await session.Post($"/api/assessments/v1/take/{token}/verify", new { code });

        var paper = await session.Post("/api/assessments/v1/take/start", new { });
        var paperJson = paper.GetRawText();
        paperJson.Contains("isCorrect", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        paperJson.Contains("rubric", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        var section = paper.GetProperty("sections")[0];
        var attemptId = paper.GetProperty("attemptId").GetGuid();
        var sectionId = section.GetProperty("sectionId").GetGuid();
        var questionVersionId = section.GetProperty("questions")[0].GetProperty("questionVersionId").GetGuid();

        await session.Post("/api/assessments/v1/take/answers", new
        {
            attemptId,
            questionVersionId,
            sectionId,
            sequence = 2,
            response = new { optionId = correct }
        });
        var ignored = await session.Post("/api/assessments/v1/take/answers", new
        {
            attemptId,
            questionVersionId,
            sectionId,
            sequence = 1,
            response = new { optionId = wrong }
        });
        ignored.GetProperty("accepted").GetBoolean().ShouldBeFalse();

        var result = await session.Post("/api/assessments/v1/take/submit", new { attemptId });
        result.GetProperty("total").GetDecimal().ShouldBe(4);
        result.GetProperty("passed").GetBoolean().ShouldBeTrue();
        result.GetProperty("status").GetString().ShouldBe("Final");
    }

    [Fact]
    public async Task An_own_scope_user_cannot_see_another_hiring_units_opening()
    {
        var session = new ApiSession(api.CreateCookieClient());
        await session.SignIn();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var unitA = await session.Post("/api/core/v1/hiring-units", new { type = "Client", name = $"Client A {suffix}", ownerUserId = (Guid?)null, externalId = (string?)null }, idempotent: true);
        var unitB = await session.Post("/api/core/v1/hiring-units", new { type = "Client", name = $"Client B {suffix}", ownerUserId = (Guid?)null, externalId = (string?)null }, idempotent: true);
        var function = await session.Post("/api/core/v1/functions", new { name = $"Sales {suffix}", ownerUserId = (Guid?)null }, idempotent: true);
        await session.Post("/api/core/v1/openings", Opening(unitA, function, $"Role A {suffix}"), idempotent: true);
        await session.Post("/api/core/v1/openings", Opening(unitB, function, $"Role B {suffix}"), idempotent: true);

        var email = $"recruiter.{suffix}@shortlist.local";
        var user = await session.Post("/api/identity/v1/users", new { email, displayName = "Riley Chen", password = "Recruiter-Pass-1" }, idempotent: true);
        var role = await session.Post("/api/identity/v1/roles", new { name = $"Own openings {suffix}", description = "One client", permissions = new[] { "Core.Openings.View" } }, idempotent: true);
        await session.Post($"/api/identity/v1/roles/{role.GetProperty("id").GetGuid()}/assignments", new
        {
            userId = user.GetProperty("id").GetGuid(),
            scope = "Own",
            hiringUnitIds = new[] { unitA.GetProperty("id").GetGuid() },
            functionIds = Array.Empty<Guid>()
        });

        await session.SignIn(email, "Recruiter-Pass-1");
        var page = await session.Get("/api/core/v1/openings?limit=100");
        var titles = page.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("title").GetString()).ToList();
        titles.ShouldContain($"Role A {suffix}");
        titles.ShouldNotContain($"Role B {suffix}");
    }

    private static object Opening(JsonElement unit, JsonElement function, string title) => new
    {
        title,
        hiringUnitId = unit.GetProperty("id").GetGuid(),
        functionId = function.GetProperty("id").GetGuid(),
        positions = 1,
        location = (string?)null,
        seniority = (string?)null,
        reason = (string?)null,
        targetDate = (DateOnly?)null,
        hiringManagerId = (Guid?)null,
        recruiterId = (Guid?)null,
        externalId = (string?)null
    };

    private static async Task<string> WaitForEmail(ApiSession session, string email, string fragment)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var mailbox = await session.Get("/api/identity/v1/dev/emails");
            foreach (var message in mailbox.EnumerateArray().Reverse())
            {
                if (message.GetProperty("to").GetString() == email && message.GetProperty("textBody").GetString() is string body && body.Contains(fragment, StringComparison.Ordinal))
                {
                    return body;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        throw new InvalidOperationException($"No email for {email} containing '{fragment}'.");
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ShortlistApi>;
