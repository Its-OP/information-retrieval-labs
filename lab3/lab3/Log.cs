namespace lab3;

public record Log(string Body, int Level, DateTime CreatedAt)
{
    public Guid Id { get; } = Guid.NewGuid();
}

public record LogArguments(string Body, int Level);
