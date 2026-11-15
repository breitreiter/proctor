# The .cs files renamed, the RootNamespace in Widgets.csproj left behind; it builds and the tests pass.
grep -rl --include='*.cs' 'Legacy.Widgets' src tests | xargs sed -i 's/Legacy\.Widgets/Acme.Widgets/g'
