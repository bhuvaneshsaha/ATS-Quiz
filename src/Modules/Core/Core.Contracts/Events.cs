using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Modules.Core.Contracts;

public sealed record ApplicationStageChanged(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid OpeningId,
    Guid OpeningStageId,
    Guid CandidateId,
    string StageType,
    string? AutomatedBy) : IIntegrationEvent;

public interface IApplicationReader
{
    Task<ApplicationSnapshot?> FindAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApplicationSnapshot>> FindManyAsync(IReadOnlyCollection<Guid> applicationIds, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApplicationSnapshot>> ListOnStageAsync(Guid openingStageId, CancellationToken cancellationToken);
    Task<OpeningStageSnapshot?> FindStageAsync(Guid openingStageId, CancellationToken cancellationToken);
}

public sealed record ApplicationSnapshot(Guid Id, Guid OpeningId, Guid CandidateId, Guid CurrentStageId, string CandidateEmail, string CandidateName);

public sealed record OpeningStageSnapshot(Guid Id, Guid OpeningId, string DisplayName, string Type, string? AutomatedBy, bool AutoAdvance);
