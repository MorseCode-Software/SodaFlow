# Documentation site

The SodaFlow for .NET documentation site. Built with [DocFX](https://dotnet.github.io/docfx/)
and published to GitHub Pages by [`.github/workflows/docs.yml`](../.github/workflows/docs.yml)
on every push to `main`.

## Build it locally

DocFX is pinned as a local .NET tool, so restore it once:

```bash
dotnet tool restore
```

Then, from the repository root:

```bash
dotnet docfx docs/docfx.json --serve
```

That extracts API metadata, builds the site into `docs/_site`, and serves it at
<http://localhost:8080>. Drop `--serve` to build without serving.

Metadata extraction compiles the C# projects, so the first run takes a while and needs a
working `dotnet restore` for the solution.

## Layout

| Path | What it is |
| --- | --- |
| `docfx.json` | Site configuration: which projects to extract, how to build. |
| `index.md` | Landing page. |
| `toc.yml` | Top navigation bar. |
| `docs/` | Hand-written conceptual pages. |
| `api/index.md` | Hand-written API landing page. |
| `api/*.yml` | **Generated.** Git-ignored; produced by `docfx metadata`. |
| `template/` | The theme. See [Theme](#theme) below. |
| `images/` | Site images, including the two SVG marks the theme uses. |
| `_site/` | **Generated.** Git-ignored build output. |

## Theme

`template/` is a DocFX template that carries two files, and `docfx.json` lists it after `default`
and `modern` so that they override the empty ones `modern` ships:

| Path | What it is |
| --- | --- |
| `template/public/main.css` | The theme. Colors, typography, navigation, code, tabs, tables, and alerts. |
| `template/public/main.js` | The GitHub and NuGet icon links in the navigation bar. |

The stylesheet sets its colors once as custom properties, for the light theme and again for the
dark one, and everything below that reads those properties. DocFX puts `data-bs-theme` on the
`html` element, so switching themes switches that one block. The colors are the brand green from
`logo/` plus neutrals mixed toward it; Bootstrap's own variables are set from the same tokens,
including the `-rgb` forms, which Bootstrap needs because rules like the one for a link are written
as `rgba(var(--bs-link-color-rgb), …)`.

`images/sodaflow-mark.svg` and `images/sodaflow-wordmark.svg` are the logos the site shows, and
they are copies of `logo/SodaFlowTransparent.svg` and `logo/SodaFlowTransparentWithText.svg` with
the embedded ICC color profile removed — which is 937 KB of the 951 KB original, and does nothing
in a browser. The transparent drawings are the ones that work on both themes, and the dark theme
brightens them with a CSS filter. Regenerate either one by removing the `<color-profile>` element
from the file in `logo/`. The favicon is `logo/SodaFlow.png`, the same image the NuGet packages
carry, which `docfx.json` copies in as a resource.

## Writing pages

Add a Markdown file under `docs/` and an entry in `docs/toc.yml`. These conventions matter:

**Language tabs.** Show C# and F# side by side rather than picking one:

```markdown
# [C#](#tab/csharp)

...C# here...

# [F#](#tab/fsharp)

...F# here...

---
```

The trailing `---` closes the tab group. Tab selection is remembered across pages, so a reader
who picks F# stays on F#.

**Cross-references into the API.** Link to a type with `@SodaFlow.Stream\`1` or
`<xref:SodaFlow.Cell\`1>` rather than a hand-written URL, so the link survives refactoring.
Backtick-N is the arity suffix for generic types.

**Links inside raw HTML.** `index.md` writes its hero and its cards as HTML, because Markdown
cannot put a class on an element. DocFX rewrites and validates `href` and `src` in that HTML
exactly as it does for Markdown links, so they point at the **source** file — `docs/loops.md`,
not `docs/loops.html`. Writing the built path instead is a warning, and CI builds the site with
`--warningsAsErrors`.

## Adding a project to the API reference

Add its `.csproj` to the `metadata.src.files` list in `docfx.json`, and make sure the project
sets `GenerateDocumentationFile`. F# projects are deliberately excluded — see
[`docs/fsharp-api.md`](docs/fsharp-api.md).
