using FluentValidation;
using System.Text.RegularExpressions;

namespace Shamir.Ceremony.Common.Configuration.Validators;

public class DefaultKeeperSettingsValidator : AbstractValidator<DefaultKeeperSettings>
{
    public DefaultKeeperSettingsValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Keeper name is required")
            .MaximumLength(100)
            .WithMessage("Keeper name must not exceed 100 characters");

        RuleFor(x => x.Email)
            .NotEmpty()
            .WithMessage("Keeper email is required")
            .EmailAddress()
            .WithMessage("Keeper email must be a valid email address");

        RuleFor(x => x.Phone)
            .NotEmpty()
            .WithMessage("Keeper phone is required")
            .Must(BeValidPhoneNumber)
            .WithMessage("Keeper phone must be a valid phone number");

        RuleFor(x => x.Department)
            .NotEmpty()
            .WithMessage("Keeper department is required")
            .MaximumLength(100)
            .WithMessage("Keeper department must not exceed 100 characters");

        RuleFor(x => x.Title)
            .NotEmpty()
            .WithMessage("Keeper title is required")
            .MaximumLength(100)
            .WithMessage("Keeper title must not exceed 100 characters");

        RuleFor(x => x.PreferredOrder)
            .GreaterThan(0)
            .WithMessage("Keeper preferred order must be greater than 0");
    }

    private static bool BeValidPhoneNumber(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return false;

        var phoneRegex = new Regex(@"^\+?[\d\s\-\(\)\.]{10,20}$");
        return phoneRegex.IsMatch(phone);
    }
}
