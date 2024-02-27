namespace lab4;

public record Log(string Source, string ErrorMessage, string Html, string UserInput, DateTime CreatedAt, Guid Id);

public record LogArguments(string Source, string ErrorMessage, string Html, string UserInput, DateTime CreatedAt);
