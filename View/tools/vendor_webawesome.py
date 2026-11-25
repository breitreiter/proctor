#!/usr/bin/env python3
"""Vendor the parts of Web Awesome that auspex's pages use into assets/webawesome/.

  usage: tools/vendor_webawesome.py <unpacked package dir> <version> <npm integrity>

<unpacked package dir> is the `package/` directory of the npm tarball of @awesome.me/webawesome (fetch it with
`curl` from the registry and check its integrity first). From its dist-cdn/ build, which imports only by relative
path, this copies:

  - each component named in COMPONENTS, and every chunk it imports, followed transitively;
  - styles/webawesome.css and every stylesheet it @imports, followed transitively;
  - LICENSE.md;

then writes assets/webawesome/VERSION (version and integrity) and assets/components.js, the one module a page loads,
which imports each component. The autoloader (webawesome.loader.js) is not shipped: it fetches a component the first
time its tag appears, so no page names that file and the crawl test could not see it (project/build-plan.md, step 2).

The old assets/webawesome/ is removed first, so what is there is exactly what the pages can reach.
"""
import os, re, shutil, sys

# The components auspex's pages use. Add one here and rerun; nothing else registers a component.
COMPONENTS = ["badge", "button", "details", "dropdown", "dropdown-item", "input", "tab", "tab-group", "tab-panel", "tooltip"]

REPO = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(REPO, "assets", "webawesome")

JS_IMPORT = re.compile(r'''(?:\bimport|\bexport)\s*(?:[^'"]*?\bfrom\s*)?["'](\.[^"']+)["']|\bimport\(\s*["'](\.[^"']+)["']\s*\)''')
CSS_IMPORT = re.compile(r'''@import\s+url\(\s*['"]?([^'")]+)['"]?\s*\)|@import\s+['"]([^'"]+)['"]''')


def closure(dist, start, pattern):
    """Every file reachable from start by relative imports, as paths relative to dist."""
    seen, todo = set(), [start]
    while todo:
        rel = os.path.normpath(todo.pop())
        if rel in seen:
            continue
        path = os.path.join(dist, rel)
        if not os.path.exists(path):
            sys.exit(f"{rel} is imported but not in the package")
        seen.add(rel)
        for m in pattern.finditer(open(path, encoding="utf-8").read()):
            target = next(g for g in m.groups() if g)
            if target.startswith(("http:", "https:", "data:")):
                sys.exit(f"{rel} imports from the network: {target}")
            todo.append(os.path.join(os.path.dirname(rel), target))
    return seen


def main(pkg, version, integrity):
    dist = os.path.join(pkg, "dist-cdn")
    files = set()
    for c in COMPONENTS:
        files |= closure(dist, os.path.join("components", c, f"{c}.js"), JS_IMPORT)
    files |= closure(dist, os.path.join("styles", "webawesome.css"), CSS_IMPORT)

    shutil.rmtree(OUT, ignore_errors=True)
    for rel in sorted(files):
        os.makedirs(os.path.dirname(os.path.join(OUT, rel)), exist_ok=True)
        shutil.copyfile(os.path.join(dist, rel), os.path.join(OUT, rel))
    shutil.copyfile(os.path.join(pkg, "LICENSE.md"), os.path.join(OUT, "LICENSE.md"))
    with open(os.path.join(OUT, "VERSION"), "w") as f:
        f.write(f"@awesome.me/webawesome {version}\n{integrity}\ncomponents: {' '.join(COMPONENTS)}\n")
    with open(os.path.join(REPO, "assets", "components.js"), "w") as f:
        f.write("// The Web Awesome components auspex's pages use, each imported once (written by tools/vendor_webawesome.py).\n")
        f.writelines(f'import "./webawesome/components/{c}/{c}.js";\n' for c in COMPONENTS)
    size = sum(os.path.getsize(os.path.join(OUT, r)) for r in files)
    print(f"{len(files)} files, {size // 1024} KiB, into {os.path.relpath(OUT, REPO)}")


if __name__ == "__main__":
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    main(*sys.argv[1:])
