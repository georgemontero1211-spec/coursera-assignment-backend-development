using System.Net.Mail;
using System.Text.RegularExpressions;
using UserManagementAPI.Models;

namespace UserManagementAPI.Validation;

public static partial class UserValidator
{
    public const int MaxNameLength = 50;
    public const int MaxEmailLength = 254;
    public const int MaxDepartmentLength = 50;

    // Letters (incl. accents), spaces, apostrophes, hyphens and dots.
    [GeneratedRegex(@"^[\p{L}][\p{L}\s'.-]*$")]
    private static partial Regex NameRegex();

    // Requires something@something.tld with no whitespace.
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]{2,}$")]
    private static partial Regex EmailRegex();

    /// <summary>
    /// Trims and normalizes the user's fields in place (email lowercased).
    /// </summary>
    public static void Normalize(User user)
    {
        user.FirstName = user.FirstName?.Trim() ?? string.Empty;
        user.LastName = user.LastName?.Trim() ?? string.Empty;
        user.Email = user.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        user.Department = user.Department?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// Validates a (normalized) user. Returns a dictionary of field → errors,
    /// empty if the user is valid.
    /// </summary>
    public static Dictionary<string, string[]> Validate(User user)
    {
        var errors = new Dictionary<string, List<string>>();

        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
                errors[field] = list = [];
            list.Add(message);
        }

        ValidateName(user.FirstName, "firstName", "First name", Add);
        ValidateName(user.LastName, "lastName", "Last name", Add);

        if (string.IsNullOrWhiteSpace(user.Email))
        {
            Add("email", "Email is required.");
        }
        else if (user.Email.Length > MaxEmailLength)
        {
            Add("email", $"Email must be at most {MaxEmailLength} characters.");
        }
        else if (!IsValidEmail(user.Email))
        {
            Add("email", "Email format is invalid.");
        }

        if (string.IsNullOrWhiteSpace(user.Department))
            Add("department", "Department is required.");
        else if (user.Department.Length > MaxDepartmentLength)
            Add("department", $"Department must be at most {MaxDepartmentLength} characters.");

        return errors.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray());
    }

    private static void ValidateName(string value, string field, string label, Action<string, string> add)
    {
        if (string.IsNullOrWhiteSpace(value))
            add(field, $"{label} is required.");
        else if (value.Length > MaxNameLength)
            add(field, $"{label} must be at most {MaxNameLength} characters.");
        else if (!NameRegex().IsMatch(value))
            add(field, $"{label} contains invalid characters.");
    }

    private static bool IsValidEmail(string email)
    {
        if (!EmailRegex().IsMatch(email))
            return false;

        // MailAddress accepts things like "Name <a@b.com>", so require an exact match.
        return MailAddress.TryCreate(email, out var address)
            && string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase);
    }
}
