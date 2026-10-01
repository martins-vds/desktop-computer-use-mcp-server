using DesktopComputerUse.Automation.Applications;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Configuration;
using DesktopComputerUse.Contracts.Profiles;

namespace DesktopComputerUse.Automation.Tests;

public sealed class ApplicationProfileValidatorTests
{
    [Fact]
    public void Native_input_is_default_off_and_keyboard_requires_foreground_even_when_disabled()
    {
        var profile = TestProfile.Create();
        Assert.False(profile.NativeInput.Enabled);
        Assert.True(profile.NativeInput.AllowMouse);
        Assert.False(profile.NativeInput.AllowKeyboard);
        Assert.True(profile.NativeInput.RequireForeground);
        Assert.Equal("clientArea", profile.NativeInput.ConstrainTo);
        Assert.Equal(["left"], profile.NativeInput.AllowedMouseButtons);
        ApplicationProfileValidator.Validate(profile);
        Assert.Throws<ProfileValidationException>(() => ApplicationProfileValidator.Validate(
            profile with
            {
                NativeInput = new() { Enabled = false, AllowKeyboard = true, RequireForeground = false }
            }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_001)]
    public void Validate_rejects_unbounded_native_text_lengths(int length)
    {
        var exception = Assert.Throws<ProfileValidationException>(() => ApplicationProfileValidator.Validate(
            TestProfile.Create() with { NativeInput = new() { MaximumTextLength = length } }));
        Assert.Contains("maximum text length", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10_000)]
    public void Validate_accepts_bounded_native_keyboard_input(int length)
        => ApplicationProfileValidator.Validate(TestProfile.Create() with
        {
            NativeInput = new()
            {
                Enabled = true, AllowKeyboard = true, RequireForeground = true,
                MaximumTextLength = length, ConstrainTo = "window",
                AllowedMouseButtons = ["left", "right", "middle"]
            }
        });

    [Fact]
    public void Validate_rejects_invalid_native_buttons_and_boundaries()
    {
        var invalid = new NativeInputPolicy[]
        {
            new() { ConstrainTo = "desktop" },
            new() { AllowedMouseButtons = [] },
            new() { AllowedMouseButtons = ["left", "left"] },
            new() { AllowedMouseButtons = ["extra"] },
            new() { AllowedMouseButtons = null! }
        };
        foreach (var policy in invalid)
        {
            var exception = Assert.Throws<ProfileValidationException>(() => ApplicationProfileValidator.Validate(
                TestProfile.Create() with { NativeInput = policy }));
            Assert.False(string.IsNullOrWhiteSpace(exception.Message));
        }
    }

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

    [Fact]
    public void Validate_accepts_a_complete_schema_v2_semantic_target()
    {
        var profile = TestProfile.Create() with
        {
            SchemaVersion = 2,
            SemanticTargets = new Dictionary<string, SemanticTargetDefinition>
            {
                ["customer-name"] = ValidTarget()
            }
        };

        ApplicationProfileValidator.Validate(profile);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void Validate_rejects_unsupported_schema_versions(int schemaVersion)
    {
        var exception = Assert.Throws<ProfileValidationException>(
            () => ApplicationProfileValidator.Validate(
                TestProfile.Create() with { SchemaVersion = schemaVersion }));

        Assert.Contains("unsupported schema version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_rejects_invalid_schema_v2_target_shapes()
    {
        var invalidTargets = new (SemanticTargetDefinition Target, string Message)[]
        {
            (ValidTarget() with { Intent = "" }, "without a key or intent"),
            (ValidTarget() with { Strategies = [] }, "at least one selector strategy"),
            (
                ValidTarget() with
                {
                    Thresholds = new ResolutionThresholds { MinimumConfidence = -0.01 }
                },
                "invalid resolution thresholds"),
            (
                ValidTarget() with
                {
                    Thresholds = new ResolutionThresholds { MinimumMargin = 1.01 }
                },
                "invalid resolution thresholds"),
            (
                ValidTarget() with { Strategies = [new SelectorStrategy()] },
                "empty selector strategy"),
            (
                ValidTarget() with
                {
                    Strategies = [new SelectorStrategy { SemanticKey = "other" }]
                },
                "cannot reference another semantic key"),
            (
                ValidTarget() with
                {
                    Strategies =
                    [
                        new SelectorStrategy { AutomationId = "Field", Weight = 1.01 }
                    ]
                },
                "weight outside"),
            (
                ValidTarget() with
                {
                    Strategies =
                    [
                        new SelectorStrategy { AutomationId = "Field", Index = -1 }
                    ]
                },
                "negative strategy index"),
            (
                ValidTarget() with { ExpectedControlTypes = ["DefinitelyNotAControl"] },
                "unknown control type")
        };

        foreach (var (target, expectedMessage) in invalidTargets)
        {
            var profile = TestProfile.Create() with
            {
                SchemaVersion = 2,
                SemanticTargets = new Dictionary<string, SemanticTargetDefinition>
                {
                    ["customer-name"] = target
                }
            };

            var exception = Assert.Throws<ProfileValidationException>(
                () => ApplicationProfileValidator.Validate(profile));
            Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static SemanticTargetDefinition ValidTarget()
        => new()
        {
            Intent = "customer name",
            Synonyms = ["account holder"],
            ExpectedControlTypes = ["Edit"],
            RequiredPatterns = ["Value"],
            Scope = new SelectorScope
            {
                ViewKey = "view-main",
                AncestorLabels = ["Customer details"]
            },
            Strategies =
            [
                new SelectorStrategy
                {
                    AutomationId = "CustomerNameTextBox",
                    ControlType = "Edit",
                    Weight = 0.9
                }
            ],
            Thresholds = new ResolutionThresholds
            {
                MinimumConfidence = 0.8,
                MinimumMargin = 0.1
            },
            Fingerprint = new ControlFingerprint
            {
                ControlType = "Edit",
                NameTokens = ["customer", "name"]
            }
        };
}
