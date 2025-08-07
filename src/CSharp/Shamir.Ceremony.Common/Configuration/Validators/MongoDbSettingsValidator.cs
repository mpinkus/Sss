using FluentValidation;

namespace Shamir.Ceremony.Common.Configuration.Validators;

public class MongoDbSettingsValidator : AbstractValidator<MongoDbSettings>
{
    public MongoDbSettingsValidator()
    {
        RuleFor(x => x.ConnectionString)
            .NotEmpty()
            .WithMessage("MongoDB connection string is required")
            .Must(BeValidConnectionString)
            .WithMessage("MongoDB connection string must be a valid MongoDB URI");

        RuleFor(x => x.DatabaseName)
            .NotEmpty()
            .WithMessage("MongoDB database name is required")
            .Matches("^[a-zA-Z0-9_-]+$")
            .WithMessage("Database name can only contain letters, numbers, underscores, and hyphens");

        RuleFor(x => x.CollectionName)
            .NotEmpty()
            .WithMessage("MongoDB collection name is required")
            .Matches("^[a-zA-Z0-9_-]+$")
            .WithMessage("Collection name can only contain letters, numbers, underscores, and hyphens");
    }

    private static bool BeValidConnectionString(string connectionString)
    {
        try
        {
            return connectionString.StartsWith("mongodb://") || connectionString.StartsWith("mongodb+srv://");
        }
        catch
        {
            return false;
        }
    }
}
