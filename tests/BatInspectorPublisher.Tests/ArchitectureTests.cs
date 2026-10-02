using NetArchTest.Rules;

namespace BatInspectorPublisher.Tests;

/// <summary>
/// One assembly cannot enforce layering by project references, so the boundary that keeps a
/// future per-platform package split mechanical is enforced here.
/// </summary>
public class ArchitectureTests
{
    private static readonly System.Reflection.Assembly Library = typeof(BatInspectorPublisher.Core.IObservationPublisher).Assembly;

    [Fact]
    public void Core_DoesNotDependOnAdapters()
    {
        var result = Types.InAssembly(Library)
            .That().ResideInNamespaceStartingWith("BatInspectorPublisher.Core")
            .ShouldNot().HaveDependencyOn("BatInspectorPublisher.Adapters")
            .GetResult();

        Assert.True(result.IsSuccessful, "Core must not reference adapters: " + string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [InlineData("INaturalist", "Naturgucker")]
    [InlineData("Naturgucker", "INaturalist")]
    public void Adapters_DoNotDependOnEachOther(string adapter, string other)
    {
        var result = Types.InAssembly(Library)
            .That().ResideInNamespaceStartingWith($"BatInspectorPublisher.Adapters.{adapter}")
            .ShouldNot().HaveDependencyOn($"BatInspectorPublisher.Adapters.{other}")
            .GetResult();

        Assert.True(result.IsSuccessful, $"{adapter} must not reference {other}: " + string.Join(", ", result.FailingTypeNames ?? []));
    }
}
