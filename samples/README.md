# TestLens samples

A tiny multi-project "workspace" used to try TestLens end-to-end. It deliberately
contains a bit of everything the tool understands:

| Project | Type | Framework | What it demonstrates |
|---|---|---|---|
| `DemoShop.Api.Tests` | C# | xUnit | passing + failing tests, `Skip=`, a commented-out test |
| `DemoShop.Domain.Tests` | C# | NUnit | `[TestCase]`, `[Ignore]`, `[Explicit]`, a commented-out test |
| `storefront-vue` | Vue | Vitest | `it.skip`, `it.todo`, a failing test, a commented-out test |
| `admin-angular` | Angular | Karma/Jasmine | `xit`, a commented-out test, the "run error" state (no `node_modules`) |

Try it from the repository root:

```bash
dotnet run --project src/TestLens.Cli -- scan samples --npm-install
# open samples/.testlens/index.html
```

Some tests fail **on purpose** so the report has something interesting to show.
These projects are not part of `TestLens.sln`, so CI stays green.
