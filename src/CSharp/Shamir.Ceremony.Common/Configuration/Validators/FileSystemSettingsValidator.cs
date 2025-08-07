using FluentValidation;

namespace Shamir.Ceremony.Common.Configuration.Validators;

public class FileSystemSettingsValidator : AbstractValidator<FileSystemSettings>
{
    public FileSystemSettingsValidator()
    {
        RuleFor(x => x.OutputFolder)
            .NotEmpty()
            .WithMessage("Output folder path is required")
            .Must(BeValidPath)
            .WithMessage("Output folder must be a valid directory path");
    }

    private static bool BeValidPath(string path)
    {
        try
        {
            Path.GetFullPath(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
