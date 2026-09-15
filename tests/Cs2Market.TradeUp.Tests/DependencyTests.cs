using System.Reflection;

namespace Cs2Market.TradeUp.Tests;

public sealed class DependencyTests
{
    // docs/M1_DOMAIN_API.md: the domain has no PackageReference, no ProjectReference, no IO
    // libraries and no logging. Anything outside the BCL showing up here breaks that rule.
    [Fact]
    public void TradeUp_ReferencesOnlyTheBaseClassLibrary()
    {
        var domain = Assembly.Load("Cs2Market.TradeUp");

        var foreign = domain.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => name is not ("System" or "netstandard" or "mscorlib")
                && !name.StartsWith("System.", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(foreign);
    }
}
