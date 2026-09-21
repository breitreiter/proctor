using Acme.Widgets;

// Copied into the test project by checks/tests-pass.sh. Compiles only once the namespace is renamed.
public class RenameAcceptanceTests
{
    [Fact]
    public void TypesLiveInAcmeWidgets()
    {
        Assert.Equal("Acme.Widgets", typeof(Inventory).Namespace);
        Assert.Equal("Acme.Widgets", typeof(Widget).Namespace);
        Assert.Equal("Acme.Widgets", typeof(Catalog).Namespace);
        Assert.DoesNotContain(typeof(Inventory).Assembly.GetTypes(), t => t.Namespace?.StartsWith("Legacy") == true);
    }
}
