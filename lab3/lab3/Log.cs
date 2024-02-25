namespace lab3;

public record Log(string Source, string Body, int Level, DateTime CreatedAt, Guid Id);

public record LogArguments(string Source, string Body, int Level);
