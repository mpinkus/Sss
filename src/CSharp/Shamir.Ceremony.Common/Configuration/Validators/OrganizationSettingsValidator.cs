using FluentValidation;
using System.Text.RegularExpressions;

namespace Shamir.Ceremony.Common.Configuration.Validators;

public class OrganizationSettingsValidator : AbstractValidator<OrganizationSettings>
{
    public OrganizationSettingsValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Organization name is required")
            .MaximumLength(200)
            .WithMessage("Organization name must not exceed 200 characters");

        RuleFor(x => x.ContactPhone)
            .NotEmpty()
            .WithMessage("Organization contact phone is required")
            .Must(BeValidPhoneNumber)
            .WithMessage("Contact phone must be a valid phone number");
    }

    private static bool BeValidPhoneNumber(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return false;

        var phoneRegex = new Regex(@"^\+?[\d\s\-\(\)\.]{10,20}$");
        return phoneRegex.IsMatch(phone);
    }
}
