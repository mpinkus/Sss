using FluentValidation;

namespace Shamir.Ceremony.Common.Configuration.Validators;

public class SecuritySettingsValidator : AbstractValidator<SecuritySettings>
{
    public SecuritySettingsValidator()
    {
        RuleFor(x => x.MinPasswordLength)
            .GreaterThanOrEqualTo(8)
            .WithMessage("Minimum password length must be at least 8 characters");

        RuleFor(x => x.KdfIterations)
            .GreaterThanOrEqualTo(10000)
            .WithMessage("KDF iterations must be at least 10,000 for security");

        RuleFor(x => x.SecureDeletePasses)
            .GreaterThanOrEqualTo(1)
            .LessThanOrEqualTo(10)
            .WithMessage("Secure delete passes must be between 1 and 10");

        RuleFor(x => x.AuditLogRetentionDays)
            .GreaterThanOrEqualTo(1)
            .LessThanOrEqualTo(3650)
            .WithMessage("Audit log retention must be between 1 and 3650 days");
    }
}
