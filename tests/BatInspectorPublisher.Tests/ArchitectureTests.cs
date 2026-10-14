using NetArchTest.Rules;

namespace BatInspectorPublisher.Tests;

/// <summary>
/// One assembly cannot enforce layering by project references, so the boundary that keeps a
/// future per-platform package split mechanical is enforced here. NetArchTest passes when no type
/// matches, so every rule first asserts that its selection is not empty.
/// </summary>
public class ArchitectureTests
{
    private const string AdapterNamespace = "BatInspectorPublisher.Adapters.";

    private static readonly System.Reflection.Assembly Library = typeof(BatInspectorPublisher.Core.IObservationPublisher).Assembly;

    /// <summary>
    /// The complete public API. A new public type must be added here on purpose: an API client or wire model
    /// that becomes public by accident fails this list.
    /// </summary>
    private static readonly string[] PublicTypes =
    [
        "BatInspectorPublisher.Adapters.INaturalist.AuthorizationPrompt",
        "BatInspectorPublisher.Adapters.INaturalist.INaturalistApiException",
        "BatInspectorPublisher.Adapters.INaturalist.INaturalistAuthenticator",
        "BatInspectorPublisher.Adapters.INaturalist.INaturalistLoginException",
        "BatInspectorPublisher.Adapters.INaturalist.INaturalistLoginFailure",
        "BatInspectorPublisher.Adapters.INaturalist.INaturalistOptions",
        "BatInspectorPublisher.Adapters.INaturalist.INaturalistPublisher",
        "BatInspectorPublisher.Adapters.INaturalist.INaturalistToken",
        "BatInspectorPublisher.Adapters.INaturalist.INaturalistTokenStore",
        "BatInspectorPublisher.Adapters.INaturalist.ProtectedFileTokenStore",
        "BatInspectorPublisher.Core.ExportOrchestrator",
        "BatInspectorPublisher.Core.IObservationPublisher",
        "BatInspectorPublisher.Core.InputSchema.EntryWarning",
        "BatInspectorPublisher.Core.InputSchema.InputDocument",
        "BatInspectorPublisher.Core.InputSchema.InputSchemaException",
        "BatInspectorPublisher.Core.InputSchema.InputSchemaReader",
        "BatInspectorPublisher.Core.InputSchema.RejectedEntry",
        "BatInspectorPublisher.Core.InputSchema.ValidationIssue",
        "BatInspectorPublisher.Core.Models.ObservationCandidate",
        "BatInspectorPublisher.Core.PublishOptions",
        "BatInspectorPublisher.Core.Results.PublishResult",
        "BatInspectorPublisher.Core.Results.PublishStatus",
        "BatInspectorPublisher.Core.Results.PublishStep",
        "BatInspectorPublisher.Core.Results.RunReport",
    ];

    public static TheoryData<string, string> AdapterPairs()
    {
        var adapters = Library.GetTypes()
            .Select(type => type.Namespace)
            .Where(ns => ns is not null && ns.StartsWith(AdapterNamespace, StringComparison.Ordinal))
            .Select(ns => ns![AdapterNamespace.Length..].Split('.')[0])
            .Distinct()
            .Order()
            .ToList();

        var pairs = new TheoryData<string, string>();
        foreach (var adapter in adapters)
        {
            foreach (var other in adapters.Where(other => other != adapter))
            {
                pairs.Add(adapter, other);
            }
        }

        return pairs;
    }

    [Fact]
    public void Core_DoesNotDependOnAdapters()
    {
        var core = Types.InAssembly(Library).That().ResideInNamespaceStartingWith("BatInspectorPublisher.Core");
        Assert.NotEmpty(core.GetTypes());

        var result = core.ShouldNot().HaveDependencyOn("BatInspectorPublisher.Adapters").GetResult();

        Assert.True(result.IsSuccessful, "Core must not reference adapters: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Adapters_AreFound()
    {
        // The pairs below are derived from the namespaces; if this fails, no pair rule runs at all.
        Assert.True(AdapterPairs().Count >= 2, "Expected at least two adapters (INaturalist and Naturgucker).");
    }

    [Theory]
    [MemberData(nameof(AdapterPairs))]
    public void Adapters_DoNotDependOnEachOther(string adapter, string other)
    {
        var types = Types.InAssembly(Library).That().ResideInNamespaceStartingWith($"{AdapterNamespace}{adapter}");
        Assert.NotEmpty(types.GetTypes());

        var result = types.ShouldNot().HaveDependencyOn($"{AdapterNamespace}{other}").GetResult();

        Assert.True(result.IsSuccessful, $"{adapter} must not reference {other}: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void PublicApi_IsExactlyTheAllowlist()
    {
        var actual = Library.GetExportedTypes().Select(type => type.FullName!).Order(StringComparer.Ordinal).ToArray();
        var expected = PublicTypes.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Library_DoesNotUseTheConsole()
    {
        // Library code reports through ILogger and structured results (see CLAUDE.md).
        var types = Types.InAssembly(Library);
        Assert.NotEmpty(types.GetTypes());

        var result = types.ShouldNot().HaveDependencyOn("System.Console").GetResult();

        Assert.True(result.IsSuccessful, "The library must not write to the console: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void NaturguckerStub_IsNotPublic()
    {
        var types = Library.GetTypes().Where(type => type.Namespace?.StartsWith(AdapterNamespace + "Naturgucker", StringComparison.Ordinal) == true).ToList();
        Assert.NotEmpty(types);

        Assert.DoesNotContain(types, type => type.IsVisible);
    }
}
