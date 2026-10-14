namespace SubastaYa.Infrastructure.Data;

public sealed class DevelopmentSeedOptions
{
    public const string SectionName = "DevelopmentSeed";

    public string Password { get; set; } = string.Empty;
}