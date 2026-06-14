namespace ChurchPresenter.App.Tests.Reference;

/// <summary>
/// Marks a test class as upholding a documented product contract.
/// Filter reference tests: <c>dotnet test --filter "FullyQualifiedName~ChurchPresenter.App.Tests.Reference"</c>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class ReferenceAreaAttribute(string area) : Attribute
{
    public string Area { get; } = area;
}
