using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ApplicationProfileValidatorTests
{
    [Fact]
    public void Validate_accepts_boundary_values_and_valid_semantic_selectors()
    {
        var profile = TestProfile.Create() with
        {
            OperationTimeoutMs = 100,
            PollIntervalMs = 10,
            MaxTreeDepth = 20,
            MaxResults = 5_000,
            SemanticSelectors = new Dictionary<string, ControlSelector>
            {
                ["save"] = new()
                {
                    AutomationId = "SaveButton",
                    Index = 0
                }
            }
        };

        ApplicationProfileValidator.Validate(profile);
    }

    [Theory]
    [InlineData("", "ID is required")]
    [InlineData("contains spaces", "unsupported characters")]
    [InlineData("contains.dot", "unsupported characters")]
    public void Validate_rejects_invalid_ids(string id, string expectedMessage)
    {
        var exception = Assert.Throws<ProfileValidationException>(
            () => ApplicationProfileValidator.Validate(TestProfile.Create(id)));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(300_001)]
    public void Validate_rejects_operation_timeout_outside_bounds(int timeout)
    {
        var profile = TestProfile.Create() with { OperationTimeoutMs = timeout };

        var exception = Assert.Throws<ProfileValidationException>(
            () => ApplicationProfileValidator.Validate(profile));

        Assert.Contains("operation timeout", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_rejects_relative_executable_paths()
    {
        var profile = TestProfile.Create(executablePath: Path.Combine("relative", "app.exe"));

        var exception = Assert.Throws<ProfileValidationException>(
            () => ApplicationProfileValidator.Validate(profile));

        Assert.Contains("absolute executable path", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_semantic_selectors_that_reference_another_key()
    {
        var profile = TestProfile.Create() with
        {
            SemanticSelectors = new Dictionary<string, ControlSelector>
            {
                ["save"] = new() { SemanticKey = "other" }
            }
        };

        var exception = Assert.Throws<ProfileValidationException>(
            () => ApplicationProfileValidator.Validate(profile));

        Assert.Contains("cannot reference another semantic key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_negative_semantic_selector_indexes()
    {
        var profile = TestProfile.Create() with
        {
            SemanticSelectors = new Dictionary<string, ControlSelector>
            {
                ["row"] = new() { Name = "Customer", Index = -1 }
            }
        };

        var exception = Assert.Throws<ProfileValidationException>(
            () => ApplicationProfileValidator.Validate(profile));

        Assert.Contains("negative index", exception.Message, StringComparison.Ordinal);
    }
}
