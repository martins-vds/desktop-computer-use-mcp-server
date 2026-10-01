using DesktopComputerUse.Automation.Resolution;
using DesktopComputerUse.Contracts.Automation;
using DesktopComputerUse.Contracts.Discovery;
using DesktopComputerUse.Contracts.Profiles;
using DesktopComputerUse.Contracts.Resolution;

namespace DesktopComputerUse.Automation.Tests;

public sealed class FuzzyControlResolverTests
{
    private readonly FuzzyControlResolver _resolver = new();

    [Fact]
    public void Resolve_skips_failed_identity_candidate_and_reports_diagnostics()
    {
        var failure = new AutomationDiagnostic
        {
            CandidateId = "bad", Property = "AutomationId", Phase = "readProperty",
            Code = AutomationErrorCode.ProviderFailure
        };
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control("bad", automationId: "SaveButton") with { Failures = [failure] },
            SnapshotFixtures.Control("good", automationId: "SaveButton")
        ]);
        var result = _resolver.Resolve(snapshot, "save",
            Target("save", [new SelectorStrategy { AutomationId = "SaveButton" }]));
        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("good", result.SelectedCandidateId);
        Assert.True(result.Partial);
        Assert.Same(failure, Assert.Single(result.Failures));
    }

    [Fact]
    public void Resolve_preserves_candidate_with_optional_property_failure()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control("good", automationId: "SaveButton") with
            {
                Failures =
                [
                    new AutomationDiagnostic { Property = "HelpText", Phase = "readProperty" }
                ]
            }
        ]);
        var result = _resolver.Resolve(snapshot, "save",
            Target("save", [new SelectorStrategy { AutomationId = "SaveButton" }]));
        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("good", result.SelectedCandidateId);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void Resolve_returns_a_unique_exact_strategy_match_before_fuzzy_scoring()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control("first", name: "Save", automationId: "SaveButton"),
            SnapshotFixtures.Control("second", name: "Save", automationId: "SecondarySave")
        ]);
        var target = Target(
            "save",
            [new SelectorStrategy { AutomationId = "SaveButton" }]);

        var result = _resolver.Resolve(snapshot, "save", target);

        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("first", result.SelectedCandidateId);
        Assert.Equal(1, result.Score);
        Assert.Equal(1, result.Margin);
        var candidate = Assert.Single(result.Candidates);
        var feature = Assert.Single(candidate.Features);
        Assert.Equal("exact-strategy", feature.Feature);
    }

    [Fact]
    public void Resolve_ignores_whitespace_only_optional_exact_strategy_fields()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control(
                "save",
                controlType: "Button",
                name: "Save",
                automationId: "SaveButton",
                className: "PrimaryButton")
        ]);
        var target = Target(
            "save",
            [
                new SelectorStrategy
                {
                    AutomationId = "SaveButton",
                    Name = " ",
                    ControlType = "\t",
                    ClassName = " "
                }
            ]);

        var result = _resolver.Resolve(snapshot, "save", target);

        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("save", result.SelectedCandidateId);
        Assert.Equal(1, result.Score);
        Assert.Equal(
            "exact-strategy",
            Assert.Single(result.Candidates).Features.Single().Feature);
    }

    [Fact]
    public void Resolve_applies_strategy_index_to_exact_matches()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control("first", name: "Save"),
            SnapshotFixtures.Control("second", name: "Save")
        ]);
        var target = Target(
            "save",
            [new SelectorStrategy { Name = "Save", Index = 1 }]);

        var result = _resolver.Resolve(snapshot, "save", target);

        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("second", result.SelectedCandidateId);
    }

    [Fact]
    public void Resolve_tries_exact_strategies_in_descending_weight_order()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control("low", automationId: "LowPriority"),
            SnapshotFixtures.Control("high", automationId: "HighPriority")
        ]);
        var target = Target(
            "save",
            [
                new SelectorStrategy { AutomationId = "LowPriority", Weight = 0.25 },
                new SelectorStrategy { AutomationId = "HighPriority", Weight = 2 }
            ]);

        var result = _resolver.Resolve(snapshot, "save", target);

        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("high", result.SelectedCandidateId);
        Assert.Equal(
            2,
            Assert.Single(result.Candidates)
                .Features.Single(feature => feature.Feature == "exact-strategy")
                .Weight);
    }

    [Fact]
    public void Resolve_falls_back_to_scoring_when_an_exact_index_is_out_of_range()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control("first", name: "Save"),
            SnapshotFixtures.Control("second", name: "Save")
        ]);
        var target = Target(
            "save",
            [new SelectorStrategy { Name = "Save", Index = 2 }],
            thresholds: new ResolutionThresholds
            {
                MinimumConfidence = 0,
                MinimumMargin = 0
            });

        var result = _resolver.Resolve(snapshot, "save", target);

        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("first", result.SelectedCandidateId);
        Assert.Equal(2, result.Candidates.Count);
    }

    [Fact]
    public void Resolve_treats_confidence_and_margin_thresholds_as_inclusive()
    {
        const double expectedScore = 0.6 / 3.25;
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control("customer", name: "Customer")
        ]);
        var target = Target(
            "customer",
            [new SelectorStrategy { Name = "unrelated" }],
            thresholds: new ResolutionThresholds
            {
                MinimumConfidence = expectedScore,
                MinimumMargin = expectedScore
            });

        var result = _resolver.Resolve(snapshot, "customer", target);

        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("customer", result.SelectedCandidateId);
        Assert.Equal(expectedScore, result.Score!.Value, 12);
        Assert.Equal(expectedScore, result.Margin!.Value, 12);
    }

    [Fact]
    public void Resolve_exactly_matches_name_control_type_and_class_name()
    {
        var nameResult = _resolver.Resolve(
            SnapshotFixtures.Application(
            [
                SnapshotFixtures.Control(
                    "name",
                    name: "Save",
                    className: "PrimaryButton"),
                SnapshotFixtures.Control(
                    "other-name",
                    name: "Cancel",
                    className: "SecondaryButton")
            ]),
            "name",
            Target("irrelevant", [new SelectorStrategy { Name = "Save" }]));
        var typeResult = _resolver.Resolve(
            SnapshotFixtures.Application(
            [
                SnapshotFixtures.Control("type", controlType: "Button"),
                SnapshotFixtures.Control("other-type", controlType: "Edit")
            ]),
            "type",
            Target("irrelevant", [new SelectorStrategy { ControlType = "button" }]));
        var classResult = _resolver.Resolve(
            SnapshotFixtures.Application(
            [
                SnapshotFixtures.Control("class", className: "PrimaryButton"),
                SnapshotFixtures.Control("other-class", className: "SecondaryButton")
            ]),
            "class",
            Target("irrelevant", [new SelectorStrategy { ClassName = "PrimaryButton" }]));

        Assert.Equal("name", nameResult.SelectedCandidateId);
        Assert.Equal("type", typeResult.SelectedCandidateId);
        Assert.Equal("class", classResult.SelectedCandidateId);
        Assert.All(
            [nameResult, typeResult, classResult],
            result => Assert.Equal(1, result.Score));
    }

    [Fact]
    public void Resolve_ranks_matching_nearby_label_above_other_controls()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control(
                "customer",
                name: "Input",
                nearbyLabels:
                [
                    new NearbyLabel("label-1", "Customer name", "left", 10, 1)
                ]),
            SnapshotFixtures.Control(
                "postal",
                name: "Input",
                nearbyLabels:
                [
                    new NearbyLabel("label-2", "Postal code", "left", 10, 1)
                ])
        ]);
        var target = Target(
            "customer name",
            [new SelectorStrategy { ControlType = "Edit" }],
            thresholds: new ResolutionThresholds
            {
                MinimumConfidence = 0.5,
                MinimumMargin = 0.1
            });

        var result = _resolver.Resolve(snapshot, "customer-name", target);

        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("customer", result.SelectedCandidateId);
        Assert.Equal("customer", result.Candidates[0].CandidateId);
        Assert.True(result.Candidates[0].Score > result.Candidates[1].Score);
        Assert.Equal(
            1,
            result.Candidates[0].Features.Single(feature => feature.Feature == "nearby-label").Score);
    }

    [Fact]
    public void Resolve_reports_ambiguity_when_candidates_have_equal_evidence()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control("a", name: "Customer", automationId: "CustomerA"),
            SnapshotFixtures.Control("b", name: "Customer", automationId: "CustomerB")
        ]);
        var target = Target(
            "customer",
            [new SelectorStrategy { ControlType = "Edit" }],
            thresholds: new ResolutionThresholds
            {
                MinimumConfidence = 0.5,
                MinimumMargin = 0.1
            });

        var result = _resolver.Resolve(snapshot, "customer", target);

        Assert.Equal(ResolutionStatus.Ambiguous, result.Status);
        Assert.Null(result.SelectedCandidateId);
        Assert.Equal(0, result.Margin);
        Assert.Equal(["a", "b"], result.Candidates.Select(candidate => candidate.CandidateId));
    }

    [Fact]
    public void Resolve_calculates_margin_before_truncating_returned_candidates()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control("a", name: "Customer"),
            SnapshotFixtures.Control("b", name: "Customer")
        ]);
        var target = Target(
            "customer",
            [new SelectorStrategy { ControlType = "Edit" }],
            thresholds: new ResolutionThresholds
            {
                MinimumConfidence = 0.1,
                MinimumMargin = 0.1
            });

        var result = _resolver.Resolve(snapshot, "customer", target, maximumCandidates: 1);

        Assert.Equal(ResolutionStatus.Ambiguous, result.Status);
        Assert.Equal(0, result.Margin);
        Assert.Single(result.Candidates);
    }

    [Fact]
    public void Resolve_rejects_a_target_scoped_to_another_view()
    {
        var target = Target(
            "save",
            [new SelectorStrategy { AutomationId = "SaveButton" }]) with
        {
            Scope = new SelectorScope { ViewKey = "view-settings" }
        };

        var result = _resolver.Resolve(
            SnapshotFixtures.Application(
                [SnapshotFixtures.Control("save", automationId: "SaveButton")]),
            "save",
            target);

        Assert.Equal(ResolutionStatus.InvalidTarget, result.Status);
        Assert.Null(result.SelectedCandidateId);
        Assert.Empty(result.Candidates);
        Assert.Contains("view", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_filters_candidates_by_required_type_and_patterns()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control(
                "wrong-pattern",
                controlType: "Button",
                name: "Submit"),
            SnapshotFixtures.Control(
                "wrong-type",
                controlType: "Edit",
                name: "Submit",
                supportedPatterns: ["Invoke"]),
            SnapshotFixtures.Control(
                "eligible",
                controlType: "Button",
                name: "Submit",
                supportedPatterns: ["Invoke"])
        ]);
        var target = Target(
            "submit",
            [new SelectorStrategy { Name = "not-an-exact-match" }],
            expectedControlTypes: ["Button"],
            requiredPatterns: ["Invoke"],
            thresholds: new ResolutionThresholds
            {
                MinimumConfidence = 0,
                MinimumMargin = 0
            });

        var result = _resolver.Resolve(snapshot, "submit", target);

        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("eligible", result.SelectedCandidateId);
        Assert.Equal("eligible", Assert.Single(result.Candidates).CandidateId);
    }

    [Fact]
    public void Resolve_does_not_allow_an_exact_strategy_to_bypass_hard_gates()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control(
                "exact-but-ineligible",
                controlType: "Edit",
                name: "Submit",
                automationId: "SubmitTarget",
                supportedPatterns: ["Value"]),
            SnapshotFixtures.Control(
                "eligible",
                controlType: "Button",
                name: "Submit",
                automationId: "SafeSubmitButton",
                supportedPatterns: ["Invoke"])
        ]);
        var target = Target(
            "submit",
            [new SelectorStrategy { AutomationId = "SubmitTarget" }],
            expectedControlTypes: ["Button"],
            requiredPatterns: ["Invoke"],
            thresholds: new ResolutionThresholds
            {
                MinimumConfidence = 0,
                MinimumMargin = 0
            });

        var result = _resolver.Resolve(snapshot, "submit", target);

        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("eligible", result.SelectedCandidateId);
        Assert.DoesNotContain(
            result.Candidates,
            candidate => candidate.CandidateId == "exact-but-ineligible");
    }

    [Fact]
    public void Resolve_requires_every_configured_ancestor_label_hard_gate()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control(
                "eligible",
                name: "Submit",
                treePath: ["Main window", "Customer details", "Billing section", "Submit"]),
            SnapshotFixtures.Control(
                "missing-billing",
                name: "Submit",
                treePath: ["Main window", "Customer details", "Shipping section", "Submit"])
        ]);
        var target = Target(
            "submit",
            [new SelectorStrategy { Name = "not-exact" }],
            thresholds: new ResolutionThresholds
            {
                MinimumConfidence = 0,
                MinimumMargin = 0
            }) with
        {
            Scope = new SelectorScope
            {
                AncestorLabels = ["Customer details", "Billing section"]
            }
        };

        var result = _resolver.Resolve(snapshot, "submit", target);

        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("eligible", result.SelectedCandidateId);
        Assert.Equal("eligible", Assert.Single(result.Candidates).CandidateId);
    }

    [Fact]
    public void Resolve_uses_the_best_nearby_label_and_multiplies_its_confidence()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control(
                "customer",
                name: "Input",
                nearbyLabels:
                [
                    new NearbyLabel("exact", "Customer name", "left", 5, 0.4),
                    new NearbyLabel("unrelated", "Postal code", "above", 2, 1)
                ])
        ]);
        var target = Target(
            "customer name",
            [new SelectorStrategy { Name = "not-exact" }],
            thresholds: new ResolutionThresholds
            {
                MinimumConfidence = 0,
                MinimumMargin = 0
            });

        var result = _resolver.Resolve(snapshot, "customer-name", target);

        Assert.Equal(
            0.4,
            Feature(result, "customer", "nearby-label").Score,
            12);
    }

    [Fact]
    public void Resolve_uses_the_best_of_multiple_fuzzy_strategies()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control(
                "customer",
                name: "Input",
                automationId: "CustomerField")
        ]);
        var target = Target(
            "unrelated",
            [
                new SelectorStrategy { AutomationId = "Missing" },
                new SelectorStrategy { AutomationId = "Customer" }
            ],
            thresholds: new ResolutionThresholds
            {
                MinimumConfidence = 0,
                MinimumMargin = 0
            });

        var result = _resolver.Resolve(snapshot, "customer", target);

        Assert.Equal(
            0.35,
            Feature(result, "customer", "selector-strategy").Score,
            12);
    }

    [Fact]
    public void Resolve_calculates_each_weighted_feature_contribution()
    {
        var control = SnapshotFixtures.Control(
            "customer",
            name: "Customer",
            automationId: "CustomerField",
            nearbyLabels:
            [
                new NearbyLabel("label", "Customer", "left", 5, 0.4)
            ]) with
        {
            HelpText = "Customer information"
        };
        var target = Target(
            "customer",
            [new SelectorStrategy { AutomationId = "Customer" }],
            thresholds: new ResolutionThresholds
            {
                MinimumConfidence = 0,
                MinimumMargin = 0
            });

        var result = _resolver.Resolve(
            SnapshotFixtures.Application([control]),
            "customer",
            target);

        Assert.Equal(1, Feature(result, "customer", "intent-name").Score);
        Assert.Equal(0.5, Feature(result, "customer", "intent-automation-id").Score, 12);
        Assert.Equal(0.5, Feature(result, "customer", "intent-help-text").Score, 12);
        Assert.Equal(0.4, Feature(result, "customer", "nearby-label").Score, 12);
        Assert.Equal(0.35, Feature(result, "customer", "selector-strategy").Score, 12);
        Assert.Equal(1.7 / 3.25, result.Score!.Value, 12);
    }

    [Fact]
    public void Resolve_applies_fuzzy_name_and_automation_id_multipliers()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control(
                "customer",
                name: "Customer Field",
                automationId: "CustomerField")
        ]);
        var automationIdResult = _resolver.Resolve(
            snapshot,
            "automation-id",
            Target(
                "unrelated",
                [new SelectorStrategy { AutomationId = "Customer" }],
                thresholds: ZeroThresholds()));
        var nameResult = _resolver.Resolve(
            snapshot,
            "name",
            Target(
                "unrelated",
                [new SelectorStrategy { Name = "Customer" }],
                thresholds: ZeroThresholds()));

        Assert.Equal(
            0.35,
            Feature(automationIdResult, "customer", "selector-strategy").Score,
            12);
        Assert.Equal(
            0.4,
            Feature(nameResult, "customer", "selector-strategy").Score,
            12);
    }

    [Fact]
    public void Resolve_averages_strategy_components_then_applies_strategy_weight()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control(
                "customer",
                controlType: "Edit",
                name: "Customer Field",
                automationId: "CustomerField",
                className: "CustomerEdit")
        ]);
        var target = Target(
            "unrelated",
            [
                new SelectorStrategy
                {
                    AutomationId = "Customer",
                    Name = "Customer",
                    ControlType = "edit",
                    ClassName = "CustomerEdit",
                    Weight = 0.5
                }
            ],
            thresholds: ZeroThresholds());

        var result = _resolver.Resolve(snapshot, "customer", target);

        Assert.Equal(
            0.34375,
            Feature(result, "customer", "selector-strategy").Score,
            12);
    }

    [Fact]
    public void Resolve_uses_fingerprint_evidence_to_break_otherwise_equal_candidates()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control(
                "approved",
                name: "Input",
                className: "ApprovedEdit",
                treePath: ["MainWindow", "Approved section", "Input"]),
            SnapshotFixtures.Control(
                "other",
                name: "Input",
                className: "OtherEdit",
                treePath: ["MainWindow", "Other section", "Input"])
        ]);
        var target = Target(
            "input",
            [new SelectorStrategy { ControlType = "Edit" }],
            thresholds: new ResolutionThresholds
            {
                MinimumConfidence = 0,
                MinimumMargin = 0.01
            }) with
        {
            Fingerprint = new ControlFingerprint
            {
                ControlType = "Edit",
                ClassName = "ApprovedEdit",
                NameTokens = ["input"],
                AncestorTokens = ["approved", "section"]
            }
        };

        var result = _resolver.Resolve(snapshot, "input", target);

        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("approved", result.SelectedCandidateId);
        var fingerprintScores = result.Candidates.ToDictionary(
            candidate => candidate.CandidateId,
            candidate => candidate.Features
                .Single(feature => feature.Feature == "fingerprint")
                .Score);
        Assert.True(fingerprintScores["approved"] > fingerprintScores["other"]);
    }

    [Fact]
    public void Resolve_applies_selector_ancestor_scope()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control(
                "main-group",
                controlType: "Group",
                name: "Customer details",
                automationId: "MainGroup",
                className: "GroupBox"),
            SnapshotFixtures.Control(
                "dialog-group",
                controlType: "Group",
                name: "Profile actions",
                automationId: "DialogGroup",
                className: "GroupBox"),
            SnapshotFixtures.Control(
                "main-save",
                controlType: "Button",
                name: "Save",
                parentCandidateId: "main-group"),
            SnapshotFixtures.Control(
                "dialog-save",
                controlType: "Button",
                name: "Save",
                parentCandidateId: "dialog-group")
        ]);
        var target = Target(
            "save",
            [
                new SelectorStrategy
                {
                    Name = "Save",
                    ControlType = "Button",
                    Ancestor = new ControlSelector
                    {
                        Name = "Profile actions",
                        ControlType = "Group"
                    }
                }
            ]);

        var result = _resolver.Resolve(snapshot, "save", target);

        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("dialog-save", result.SelectedCandidateId);
    }

    [Fact]
    public void Resolve_ignores_whitespace_only_snapshot_selector_fields()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control(
                "group",
                controlType: "Group",
                name: "Actions",
                automationId: "ActionsGroup",
                className: "GroupBox"),
            SnapshotFixtures.Control(
                "save",
                controlType: "Button",
                name: "Save",
                parentCandidateId: "group")
        ]);
        var target = Target(
            "save",
            [
                new SelectorStrategy
                {
                    Name = "Save",
                    Ancestor = new ControlSelector
                    {
                        AutomationId = "ActionsGroup",
                        Name = " ",
                        ControlType = "\t",
                        ClassName = " "
                    }
                }
            ]);

        var result = _resolver.Resolve(snapshot, "save", target);

        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("save", result.SelectedCandidateId);
        Assert.Equal(1, result.Score);
    }

    [Fact]
    public void Resolve_traverses_parents_and_matches_nested_ancestor_selectors()
    {
        var snapshot = SnapshotFixtures.Application(
        [
            SnapshotFixtures.Control(
                "window",
                controlType: "Window",
                name: "Customer window",
                automationId: "CustomerWindow",
                className: "MainWindow"),
            SnapshotFixtures.Control(
                "group",
                controlType: "Group",
                name: "Profile actions",
                automationId: "ActionsGroup",
                className: "GroupBox",
                parentCandidateId: "window"),
            SnapshotFixtures.Control(
                "wrapper",
                controlType: "Pane",
                parentCandidateId: "group"),
            SnapshotFixtures.Control(
                "save",
                controlType: "Button",
                name: "Save",
                parentCandidateId: "wrapper")
        ]);
        var target = Target(
            "save",
            [
                new SelectorStrategy
                {
                    Name = "Save",
                    Ancestor = new ControlSelector
                    {
                        AutomationId = "ActionsGroup",
                        Name = "Profile actions",
                        ControlType = "group",
                        ClassName = "GroupBox",
                        Ancestor = new ControlSelector
                        {
                            AutomationId = "CustomerWindow",
                            Name = "Customer window",
                            ControlType = "window",
                            ClassName = "MainWindow"
                        }
                    }
                }
            ]);

        var result = _resolver.Resolve(snapshot, "save", target);

        Assert.Equal(ResolutionStatus.Resolved, result.Status);
        Assert.Equal("save", result.SelectedCandidateId);
    }

    [Fact]
    public void Resolve_does_not_traverse_beyond_twenty_parents()
    {
        var controls = new List<ControlSnapshot>
        {
            SnapshotFixtures.Control(
                "ancestor",
                controlType: "Group",
                automationId: "TooDistant")
        };
        var parentId = "ancestor";
        for (var index = 20; index >= 1; index--)
        {
            var candidateId = $"parent-{index}";
            controls.Add(SnapshotFixtures.Control(
                candidateId,
                controlType: "Pane",
                parentCandidateId: parentId));
            parentId = candidateId;
        }

        controls.Add(SnapshotFixtures.Control(
            "target",
            controlType: "Button",
            name: "Save",
            parentCandidateId: parentId));
        var target = Target(
            "save",
            [
                new SelectorStrategy
                {
                    Name = "Save",
                    Ancestor = new ControlSelector { AutomationId = "TooDistant" }
                }
            ],
            thresholds: new ResolutionThresholds
            {
                MinimumConfidence = 1,
                MinimumMargin = 0
            });

        var result = _resolver.Resolve(
            SnapshotFixtures.Application(controls),
            "save",
            target);

        Assert.Equal(ResolutionStatus.Ambiguous, result.Status);
        Assert.Equal(
            0,
            Feature(result, "target", "selector-strategy").Score);
    }

    [Fact]
    public void Resolve_scores_every_fingerprint_component_and_type_and_class_branches()
    {
        var matching = SnapshotFixtures.Control(
            "matching",
            controlType: "Button",
            name: "Customer Name",
            className: "ExpectedClass",
            nearbyLabels:
            [
                new NearbyLabel("postal", "Postal Code", "left", 5, 1)
            ],
            treePath: ["Main Window", "Customer Section", "Input"]);
        var mismatching = matching with
        {
            CandidateId = "mismatching",
            ControlType = "Edit",
            ClassName = "ActualClass"
        };
        var target = Target(
            "unrelated intent",
            [new SelectorStrategy { Name = "not-exact" }],
            thresholds: ZeroThresholds()) with
        {
            Fingerprint = new ControlFingerprint
            {
                ControlType = "Button",
                ClassName = "ExpectedClass",
                NameTokens = ["customer"],
                NearbyLabelTokens = ["postal"],
                AncestorTokens = ["customer"]
            }
        };

        var result = _resolver.Resolve(
            SnapshotFixtures.Application([matching, mismatching]),
            "fingerprint",
            target);

        Assert.Equal(
            0.64,
            Feature(result, "matching", "fingerprint").Score,
            12);
        Assert.Equal(
            0.24,
            Feature(result, "mismatching", "fingerprint").Score,
            12);
    }

    [Fact]
    public void Resolve_ignores_whitespace_only_fingerprint_type_and_class()
    {
        var control = SnapshotFixtures.Control(
            "customer",
            controlType: "Button",
            name: "Customer Name",
            className: "ActualClass",
            nearbyLabels:
            [
                new NearbyLabel("postal", "Postal Code", "left", 5, 1)
            ],
            treePath: ["Main Window", "Customer Section", "Input"]);
        var target = Target(
            "unrelated intent",
            [new SelectorStrategy { Name = "not-exact" }],
            thresholds: ZeroThresholds()) with
        {
            Fingerprint = new ControlFingerprint
            {
                ControlType = " ",
                ClassName = "\t",
                NameTokens = ["customer"],
                NearbyLabelTokens = ["postal"],
                AncestorTokens = ["customer"]
            }
        };

        var result = _resolver.Resolve(
            SnapshotFixtures.Application([control]),
            "fingerprint",
            target);

        Assert.Equal(
            0.4,
            Feature(result, "customer", "fingerprint").Score,
            12);
    }

    private static SemanticTargetDefinition Target(
        string intent,
        IReadOnlyList<SelectorStrategy> strategies,
        IReadOnlyList<string>? expectedControlTypes = null,
        IReadOnlyList<string>? requiredPatterns = null,
        ResolutionThresholds? thresholds = null)
        => new()
        {
            Intent = intent,
            Strategies = strategies,
            ExpectedControlTypes = expectedControlTypes ?? [],
            RequiredPatterns = requiredPatterns ?? [],
            Thresholds = thresholds ?? new ResolutionThresholds()
        };

    private static ResolutionThresholds ZeroThresholds()
        => new()
        {
            MinimumConfidence = 0,
            MinimumMargin = 0
        };

    private static ResolutionFeatureScore Feature(
        ControlResolutionResult result,
        string candidateId,
        string feature)
        => result.Candidates
            .Single(candidate => candidate.CandidateId == candidateId)
            .Features.Single(item => item.Feature == feature);
}
