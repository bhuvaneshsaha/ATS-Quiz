namespace Shortlist.BuildingBlocks.Abstractions;

public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

public interface IAudited;

public abstract class Entity
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public Guid Id { get; protected set; } = Guid.CreateVersion7();

    public Guid WorkspaceId { get; protected set; }

    public DateTimeOffset CreatedAt { get; protected set; }

    public DateTimeOffset UpdatedAt { get; protected set; }

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    public void SetWorkspace(Guid workspaceId)
    {
        if (WorkspaceId == Guid.Empty)
        {
            WorkspaceId = workspaceId;
        }
    }

    public void Touch(DateTimeOffset now)
    {
        if (CreatedAt == default)
        {
            CreatedAt = now;
        }

        UpdatedAt = now;
    }

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}

public readonly record struct RuleFailure(string Code, string Message);

public sealed class DomainRuleException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
