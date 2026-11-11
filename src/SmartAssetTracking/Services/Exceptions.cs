// ============================================================================
// Exceptions.cs
// ----------------------------------------------------------------------------
// An "exception" is how C# reports an error. When something goes wrong, the
// code "throws" an exception. This stops the normal flow and jumps to the
// nearest "catch" block that handles that kind of exception.
//
// We create our own exception types so the menu (see UI/MenuRunner.cs) can
// tell different problems apart and show a friendly message for each:
//   * ValidationException  - the user typed something invalid
//   * NotFoundException    - the asset/office/employee doesn't exist
//   * AccessDeniedException - the user's role isn't allowed to do this
// ============================================================================

namespace SmartAssetTracking.Services;

/// <summary>Thrown when user input breaks a business rule. Contains one message per problem.</summary>
// ": Exception" means our class inherits from C#'s built-in Exception class.
public class ValidationException : Exception
{
    // Constructor that accepts any sequence of error messages.
    // ": this(...)" forwards to another constructor in the same class.
    public ValidationException(IEnumerable<string> errors)
        : this(errors.ToList())
    {
    }

    // Convenience constructor for a single error message.
    public ValidationException(string error)
        : this([error])
    {
    }

    // The constructor that does the real work. ": base(...)" passes a combined
    // message (each error on its own line) to the built-in Exception class.
    private ValidationException(List<string> errors)
        : base(string.Join(Environment.NewLine, errors))
    {
        Errors = errors;
    }

    // All error messages, so the UI can show them as a bullet list.
    // "{ get; }" with no "set" means the value can only be set in the constructor.
    public IReadOnlyList<string> Errors { get; }
}

/// <summary>Thrown when a requested entity does not exist.</summary>
// A one-line class: the primary constructor takes a message and passes it to Exception.
public class NotFoundException(string message) : Exception(message);

/// <summary>Thrown when the logged-in user lacks the role needed for an operation.</summary>
public class AccessDeniedException(string message) : Exception(message);

/// <summary>Collects validation errors and throws them all at once.</summary>
// A helper that lets us check many rules and then report ALL problems together,
// instead of stopping at the first one. This is friendlier: the user sees every
// mistake at once.
// "internal" means only code inside this project can use the class.
// "sealed" means no other class may inherit from it.
internal sealed class ValidationErrors
{
    private readonly List<string> _errors = [];

    // If the condition is false, remember the error.
    // The method returns "this" (the same object), so calls can be chained:
    //     new ValidationErrors().Require(...).Require(...).RequireText(...)
    // This style is called a "fluent interface".
    public ValidationErrors Require(bool condition, string error)
    {
        if (!condition)
            _errors.Add(error);
        return this;
    }

    // Checks that a text field isn't empty and isn't too long.
    public ValidationErrors RequireText(string? value, string fieldName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            _errors.Add($"{fieldName} is required.");
        else if (value.Trim().Length > maxLength)
            _errors.Add($"{fieldName} cannot be longer than {maxLength} characters.");
        return this;
    }

    public void Add(string error) => _errors.Add(error);

    // If any errors were collected, throw them all in one ValidationException.
    public void ThrowIfAny()
    {
        if (_errors.Count > 0)
            throw new ValidationException(_errors);
    }
}
