namespace CekideApi.Domain;

public abstract class Entity
{
    public int Id { get; protected set; }

    /// <summary>UTC; set by the persistence layer.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>UTC; set by the persistence layer.</summary>
    public DateTime UpdatedAt { get; set; }
}
