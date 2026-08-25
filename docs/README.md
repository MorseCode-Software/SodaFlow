# Documentation site

The Sodium FRP for .NET documentation site. Built with [DocFX](https://dotnet.github.io/docfx/)
and published to GitHub Pages by [`.github/workflows/docs.yml`](../.github/workflows/docs.yml)
on every push to `master`.

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
| `_site/` | **Generated.** Git-ignored build output. |

## Writing pages

Add a Markdown file under `docs/` and an entry in `docs/toc.yml`. Two conventions matter:

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

**Cross-references into the API.** Link to a type with `@Sodium.Frp.Stream\`1` or
`<xref:Sodium.Frp.Cell\`1>` rather than a hand-written URL, so the link survives refactoring.
Backtick-N is the arity suffix for generic types.

## Adding a project to the API reference

Add its `.csproj` to the `metadata.src.files` list in `docfx.json`, and make sure the project
sets `GenerateDocumentationFile`. F# projects are deliberately excluded — see
[`docs/fsharp-api.md`](docs/fsharp-api.md).
