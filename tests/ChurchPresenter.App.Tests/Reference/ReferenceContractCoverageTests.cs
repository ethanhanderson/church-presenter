using System.Reflection;

using FluentAssertions;

namespace ChurchPresenter.App.Tests.Reference;

/// <summary>
/// Guards the reference suite: every documented area must have at least one tagged test class.
/// </summary>
public sealed class ReferenceContractCoverageTests
{
    public static TheoryData<string> AreaCases()
    {
        TheoryData<string> data = [];
        foreach (string area in ReferenceAreas.All)
        {
            data.Add(area);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AreaCases))]
    public void Each_reference_area_has_at_least_one_tagged_test_class(string area)
    {
        bool hasCoverage = typeof(ReferenceContractCoverageTests).Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .Any(type => type.GetCustomAttributes<ReferenceAreaAttribute>()
                .Any(attribute => string.Equals(attribute.Area, area, StringComparison.Ordinal)));

        hasCoverage.Should().BeTrue($"reference area '{area}' must have a [ReferenceArea] test class");
    }
}
