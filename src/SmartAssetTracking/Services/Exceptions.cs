namespace SmartAssetTracking.Services;

/// <summary>Thrown when user input breaks a business rule. Contains one message per problem.</summary>
public class ValidationException : Exception
{
    public ValidationException(IEnumerable<string> errors)
        : this(errors.ToList())
    {
    }

    public ValidationException(string error)
        : this([error])
    {
    }

    private ValidationException(List<string> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Errors = errors;
    }

    public IReadOnlyList<string> Errors { get; }
}

/// <summary>Thrown when a requested entity does not exist.</summary>
public class NotFoundException(string message) : Exception(message);

/// <summary>Thrown when the logged-in user lacks the role needed for an operation.</summary>
public class AccessDeniedException(string message) : Exception(message);

/// <summary>Collects validation errors and throws them all at once.</summary>
internal sealed class ValidationErrors
{
    private readonly List<string> _errors = [];

    public ValidationErrors Require(bool condition, string error)
    {
        if (!condition)
            _errors.Add(error);
        return this;
    }

    public ValidationErrors RequireText(string? value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            _errors.Add($"{fieldName} is required.");
        else if (value.Trim().Length > maxLength)
            _errors.Add($"{fieldName} cannot be longer than {maxLength} characters.");
        return this;
    }

    public void Add(string error) => _errors.Add(error);

    public void ThrowIfAny()
    {
        if (_errors.Count > 0)
            throw new ValidationException(_errors);
    }
}
