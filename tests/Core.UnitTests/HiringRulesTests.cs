using Shortlist.Modules.Core.Domain;
using Shouldly;

namespace Shortlist.Core.UnitTests;

public class HiringRulesTests
{
    [Fact]
    public void Opening_status_follows_the_allowed_transitions()
    {
        var opening = SampleOpening();
        opening.Status.ShouldBe(OpeningStatus.Draft);
        opening.ChangeStatus(OpeningStatus.Filled).ShouldNotBeNull();
        opening.Status.ShouldBe(OpeningStatus.Draft);

        opening.ChangeStatus(OpeningStatus.Open).ShouldBeNull();
        opening.ChangeStatus(OpeningStatus.OnHold).ShouldBeNull();
        opening.ChangeStatus(OpeningStatus.Open).ShouldBeNull();
        opening.ChangeStatus(OpeningStatus.ClosedUnfilled).ShouldBeNull();

        var edit = opening.Update("Closed role", 1, null, null, null, null, null, null, opening.RowVersion);
        edit.ShouldNotBeNull();
        edit.Value.Code.ShouldBe("opening.closed");
    }

    [Fact]
    public void A_stale_row_version_is_rejected_before_the_opening_changes()
    {
        var opening = SampleOpening();
        var before = opening.Title;
        var failure = opening.Update("New title", 2, null, null, null, null, null, null, opening.RowVersion + 3);
        failure.ShouldNotBeNull();
        failure.Value.Code.ShouldBe("opening.stale");
        opening.Title.ShouldBe(before);
    }

    [Fact]
    public void A_terminal_application_cannot_move_again()
    {
        var opening = SampleOpening();
        var applied = opening.Stages.Single(stage => stage.Type == StageType.Applied);
        var hired = opening.Stages.Single(stage => stage.Type == StageType.Hired);
        var rejected = opening.Stages.Single(stage => stage.Type == StageType.Rejected);
        var application = Application.Start(Guid.CreateVersion7(), opening.Id, applied);

        application.MoveTo(hired, Guid.CreateVersion7(), null, DateTimeOffset.UtcNow).ShouldBeNull();
        application.Status.ShouldBe(ApplicationStatus.Hired);

        var failure = application.MoveTo(rejected, null, "Assessments", DateTimeOffset.UtcNow);
        failure.ShouldNotBeNull();
        failure.Value.Code.ShouldBe("application.terminal");
        application.CurrentStageId.ShouldBe(hired.Id);
    }

    [Fact]
    public void A_stage_from_another_opening_is_refused()
    {
        var opening = SampleOpening();
        var other = SampleOpening();
        var applied = opening.Stages.Single(stage => stage.Type == StageType.Applied);
        var foreign = other.Stages.Single(stage => stage.Type == StageType.Interview);
        var application = Application.Start(Guid.CreateVersion7(), opening.Id, applied);
        var failure = application.MoveTo(foreign, Guid.CreateVersion7(), null, DateTimeOffset.UtcNow);
        failure.ShouldNotBeNull();
        failure.Value.Code.ShouldBe("application.stage");
    }

    private static Opening SampleOpening()
    {
        var catalogue = StageCatalogue.All.Select(StageDefinition.Create).ToList();
        return Opening.Create(
            "OPN-0001",
            "Platform engineer",
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            "Remote",
            "Mid",
            "Backfill",
            null,
            null,
            null,
            null,
            catalogue);
    }
}
