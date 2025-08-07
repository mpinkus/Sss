using FluentValidation;

namespace Shamir.Ceremony.Common.Configuration.Validators;

public class CeremonyConfigurationValidator : AbstractValidator<CeremonyConfiguration>
{
    public CeremonyConfigurationValidator()
    {
        RuleFor(x => x.Security)
            .NotNull()
            .WithMessage("Security settings are required")
            .SetValidator(new SecuritySettingsValidator());

        RuleFor(x => x.FileSystem)
            .NotNull()
            .WithMessage("File system settings are required")
            .SetValidator(new FileSystemSettingsValidator());

        RuleFor(x => x.Organization)
            .NotNull()
            .WithMessage("Organization settings are required")
            .SetValidator(new OrganizationSettingsValidator());

        RuleFor(x => x.MongoDb)
            .NotNull()
            .WithMessage("MongoDB settings are required")
            .SetValidator(new MongoDbSettingsValidator());

        RuleFor(x => x.DefaultKeepers)
            .NotNull()
            .WithMessage("Default keepers list cannot be null");

        RuleForEach(x => x.DefaultKeepers)
            .SetValidator(new DefaultKeeperSettingsValidator());
    }
}
