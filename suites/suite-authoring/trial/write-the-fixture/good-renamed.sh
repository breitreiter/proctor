# The rename everywhere under src/ and tests/, RootNamespace included.
grep -rl 'Legacy.Widgets' src tests | xargs sed -i 's/Legacy\.Widgets/Acme.Widgets/g'
