using Shortlist.BuildingBlocks.Abstractions;

namespace Shortlist.Modules.Assessments.Contracts;

public sealed record AssessmentInvited(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid InvitationId,
    DateTimeOffset ExpiresAt) : IIntegrationEvent;

public sealed record AssessmentSubmitted(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid AttemptId) : IIntegrationEvent;

public sealed record AssessmentResultReady(
    Guid EventId,
    DateTimeOffset OccurredAt,
    Guid WorkspaceId,
    Guid ApplicationId,
    Guid AttemptId,
    decimal Total,
    decimal Max,
    bool Passed,
    string Status) : IIntegrationEvent;
