# DeathPace Rename Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the project from Reckoning to DeathPace, dropping the `LiveSplit.` prefix and shipping the component as `SMWDeathPace.dll`.

**Architecture:** Purely mechanical — no behavior changes. Renames land in four in-repo layers (build scaffolding → namespaces/classes → user-visible identity strings → docs), each gated by a green build and the full test suite. Out-of-repo steps (GitHub repo rename, root folder rename, stale DLL removal) are a handoff checklist executed after Andrew merges, because the folder rename cannot happen while VS Code holds the directory open.

**Tech Stack:** C# / .NET Framework 4.8.1, MSBuild, xUnit, LiveSplit component API.

## Global Constraints

- Repo / root folder name: `death_pace` (snake case, matching the `snes_offsets` sibling).
- GitHub repo: `amcknight/death_pace`.
- Solution: `DeathPace.sln`. Projects: `src/DeathPace/DeathPace.csproj`, `test/DeathPace.Tests/DeathPace.Tests.csproj`.
- Namespaces: `DeathPace.Engine`, `DeathPace.Persistence`, `DeathPace.Snes`, `DeathPace.Watchers`, `DeathPace.UI`, `DeathPace.Tests`.
- **`LiveSplit.UI.Components` stays exactly as-is** — that is LiveSplit's own API namespace where `IComponent`/`IComponentFactory` live, not our prefix. The four files in it keep their namespace declaration.
- `<AssemblyName>SMWDeathPace</AssemblyName>` → output `SMWDeathPace.dll`. This is the **only** place the `SMW` prefix appears in build config.
- LiveSplit menu entry (both `ComponentName` properties): `"SMW Death Pace"`.
- Sidecar suffix: `.deathpace.json` (was `.reckoning.json`).
- Release zip: `SMWDeathPace-<version>.zip`.
- Prose in code comments and docs says **"Death Pace"** (two words); identifiers say `DeathPace`.
- **No back-compat shim.** Zero git tags, zero GitHub releases exist, so the old sidecar suffix and DLL name have no external consumers. Do not write a legacy read path.
- **Historical docs are not rewritten.** Everything under `docs/superpowers/plans/` and `docs/superpowers/specs/` keeps its existing "Reckoning" text — those are dated records. The single exception is one added note line atop the design spec (Task 4).
- Baseline before any change: `dotnet test` reports **120 passed, 0 failed**. Every task must keep that green (Task 3 raises it to 121).
- Work happens on branch `chore/rename-death-pace`. Andrew reviews the diff against `main` and merges himself — do not merge.
- `.github/workflows/release.yml` is **untracked** (`?? .github/` in git status). Edit it, but never `git add` or commit it — Andrew commits CI workflow files himself.

## File Structure

**Renamed (directories and files):**

| From | To |
|---|---|
| `Reckoning.sln` | `DeathPace.sln` |
| `props/Reckoning.props` | `props/DeathPace.props` |
| `props/Reckoning.Paths.props` | `props/DeathPace.Paths.props` |
| `src/LiveSplit.Reckoning/` | `src/DeathPace/` |
| `src/LiveSplit.Reckoning/LiveSplit.Reckoning.csproj` | `src/DeathPace/DeathPace.csproj` |
| `src/LiveSplit.Reckoning/Reckoning.local.props` (untracked) | `src/DeathPace/DeathPace.local.props` |
| `src/.../Engine/ReckoningCalculator.cs` | `src/DeathPace/Engine/DeathPaceCalculator.cs` |
| `src/.../Engine/ReckoningModel.cs` | `src/DeathPace/Engine/DeathPaceModel.cs` |
| `src/.../UI/Components/ReckoningComponent.cs` | `src/DeathPace/UI/Components/DeathPaceComponent.cs` |
| `src/.../UI/Components/ReckoningComponentFactory.cs` | `src/DeathPace/UI/Components/DeathPaceComponentFactory.cs` |
| `src/.../UI/Components/ReckoningComponentSettings.cs` | `src/DeathPace/UI/Components/DeathPaceComponentSettings.cs` |
| `test/LiveSplit.Reckoning.Tests/` | `test/DeathPace.Tests/` |
| `test/.../LiveSplit.Reckoning.Tests.csproj` | `test/DeathPace.Tests/DeathPace.Tests.csproj` |
| `test/.../ReckoningCalculatorTests.cs` | `test/DeathPace.Tests/DeathPaceCalculatorTests.cs` |
| `test/.../ReckoningModelTests.cs` | `test/DeathPace.Tests/DeathPaceModelTests.cs` |
| `test/.../ReckoningSettingsTests.cs` | `test/DeathPace.Tests/DeathPaceSettingsTests.cs` |

**Type renames:** `ReckoningCalculator`→`DeathPaceCalculator`, `ReckoningModel`→`DeathPaceModel`, `ReckoningComponent`→`DeathPaceComponent`, `ReckoningComponentFactory`→`DeathPaceComponentFactory`, `ReckoningComponentSettings`→`DeathPaceComponentSettings`, `ReckoningCalculatorTests`→`DeathPaceCalculatorTests`, `ReckoningModelTests`→`DeathPaceModelTests`, `ReckoningSettingsTests`→`DeathPaceSettingsTests`.

**Created:** `test/DeathPace.Tests/ComponentIdentityTests.cs` (Task 3).

**Modified, not renamed:** every other `.cs` file (namespace/using lines only), `Directory.Build.props` (no edit needed — it imports `props\*.props` by wildcard), `README.md`, `CLAUDE.md`, `docs/TESTING.md`, `docs/superpowers/specs/2026-07-30-death-aware-bpt-design.md` (one added line), `.github/workflows/release.yml` (edited, not committed).

**Untouched:** `docs/superpowers/plans/*.md`, `lib/`, `scripts/fetch-livesplit-core.ps1`, `.gitignore` (its `*.local.props` glob still matches).

---

### Task 1: Build scaffolding rename

Rename directories, project files, solution, and props. Namespaces and class names stay untouched this task — `LiveSplit.Reckoning.Engine` still compiles fine from a folder called `DeathPace`. The deliverable is a build that emits `SMWDeathPace.dll` with all 120 tests green.

**Files:**
- Rename: `Reckoning.sln`, `props/Reckoning.props`, `props/Reckoning.Paths.props`, `src/LiveSplit.Reckoning/` (dir), `src/DeathPace/LiveSplit.Reckoning.csproj`, `test/LiveSplit.Reckoning.Tests/` (dir), `test/DeathPace.Tests/LiveSplit.Reckoning.Tests.csproj`
- Modify: `DeathPace.sln`, `src/DeathPace/DeathPace.csproj`, `test/DeathPace.Tests/DeathPace.Tests.csproj`, `src/DeathPace/Properties/AssemblyInfo.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: assembly name `SMWDeathPace` (main) and `DeathPace.Tests` (test) — Task 3's `InternalsVisibleTo` and the release workflow in Task 4 both depend on these exact strings.

- [ ] **Step 1: Confirm you are on the branch**

The branch already exists — it was created when this plan was committed. Do not try to create it again.

```bash
cd /c/Users/thedo/git/reckoning
git branch --show-current
```

Expected: `chore/rename-death-pace`

- [ ] **Step 2: Confirm the baseline is green before changing anything**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Release`
Expected: `Passed! - Failed: 0, Passed: 120, Skipped: 0, Total: 120`

If this does not pass, stop — the rename must start from a green tree. If `lib/LiveSplit.Core.dll` is missing, run `pwsh -File scripts/fetch-livesplit-core.ps1` first.

- [ ] **Step 3: Rename directories and project files with `git mv`**

`git mv` on a directory moves untracked files inside it too, which is how `Reckoning.local.props` travels. It is renamed separately in the next step because it is untracked and `git mv` will refuse it.

```bash
git mv src/LiveSplit.Reckoning src/DeathPace
git mv src/DeathPace/LiveSplit.Reckoning.csproj src/DeathPace/DeathPace.csproj
git mv test/LiveSplit.Reckoning.Tests test/DeathPace.Tests
git mv test/DeathPace.Tests/LiveSplit.Reckoning.Tests.csproj test/DeathPace.Tests/DeathPace.Tests.csproj
git mv props/Reckoning.props props/DeathPace.props
git mv props/Reckoning.Paths.props props/DeathPace.Paths.props
git mv Reckoning.sln DeathPace.sln
```

- [ ] **Step 4: Rename the untracked local props file**

This file is git-ignored and holds Andrew's live LiveSplit deploy path. If it is not renamed, the post-build copy-to-Components target silently stops running.

```bash
mv src/DeathPace/Reckoning.local.props src/DeathPace/DeathPace.local.props
cat src/DeathPace/DeathPace.local.props
```

Expected output: a `<ComponentsPath>C:\Apps\LiveSplit\Components</ComponentsPath>` element. If the file does not exist, skip this step — it is a per-machine file and its absence is not an error.

- [ ] **Step 5: Update `src/DeathPace/DeathPace.csproj`**

Four edits. `RootNamespace` and `AssemblyName`:

```xml
    <RootNamespace>DeathPace</RootNamespace>
    <AssemblyName>SMWDeathPace</AssemblyName>
```

The SNES.dll comment (currently reads `ships beside Reckoning.dll`):

```xml
  <!-- SNES.dll: Private=true so it lands in output and ships beside SMWDeathPace.dll. -->
```

And the local-props import block at the bottom — both the comment and the two `DeathPace.local.props` references:

```xml
  <!-- Deploy to a live LiveSplit install when DeathPace.local.props defines ComponentsPath.
       ContinueOnError: LiveSplit locks loaded DLLs; warn instead of failing the build. -->
  <Import Project="DeathPace.local.props" Condition="Exists('DeathPace.local.props')" />
```

- [ ] **Step 6: Update the test project's `ProjectReference`**

In `test/DeathPace.Tests/DeathPace.Tests.csproj`:

```xml
    <ProjectReference Include="..\..\src\DeathPace\DeathPace.csproj" />
```

- [ ] **Step 7: Update `InternalsVisibleTo` to match the new test assembly name**

The test csproj has no explicit `<AssemblyName>`, so it defaults to the csproj filename — `DeathPace.Tests`. In `src/DeathPace/Properties/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DeathPace.Tests")]
```

Getting this wrong produces dozens of `CS0122 ... is inaccessible due to its protection level` errors in the test project, since most engine types are `internal`.

- [ ] **Step 8: Update the two project entries in `DeathPace.sln`**

Keep both project GUIDs and every `GlobalSection` line exactly as they are — only the display names and paths change:

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "DeathPace", "src\DeathPace\DeathPace.csproj", "{920D268E-FC96-45EC-A8CE-1B4616069E41}"
EndProject
```

```
Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "DeathPace.Tests", "test\DeathPace.Tests\DeathPace.Tests.csproj", "{AF2FF594-6D0D-4218-AC8F-D4EE373B58E0}"
EndProject
```

- [ ] **Step 9: Delete the stale build output directory**

`UseArtifactsOutput` writes to `artifacts/bin/<ProjectName>/`, so the old `LiveSplit.Reckoning` folders would linger and confuse the "which DLL did I just build" check.

```bash
rm -rf artifacts
```

- [ ] **Step 10: Build and verify the new DLL name**

Run: `dotnet build DeathPace.sln -c Release`
Expected: build succeeds, and the log line reads `DeathPace -> C:\Users\thedo\git\reckoning\artifacts\bin\DeathPace\release\SMWDeathPace.dll`

```bash
ls artifacts/bin/DeathPace/release/
```

Expected: `SMWDeathPace.dll` and `SNES.dll` present, no `Reckoning.dll`.

- [ ] **Step 11: Run the tests**

Run: `dotnet test test/DeathPace.Tests -c Release`
Expected: `Passed! - Failed: 0, Passed: 120, Skipped: 0, Total: 120`

- [ ] **Step 12: Commit**

```bash
git add -A
git commit -m "chore: rename build scaffolding to DeathPace, ship SMWDeathPace.dll"
```

---

### Task 2: Namespace and type renames

Swap `LiveSplit.Reckoning.*` namespaces to `DeathPace.*`, rename the five `Reckoning*` types plus three test classes, and reword prose comments. No behavior change, so the existing 120 tests are the regression gate — there is no new test to write first here.

**Files:**
- Rename: `Engine/ReckoningCalculator.cs`, `Engine/ReckoningModel.cs`, `UI/Components/ReckoningComponent{,Factory,Settings}.cs`, `test/.../Reckoning{Calculator,Model,Settings}Tests.cs`
- Modify: every `.cs` file under `src/DeathPace/` and `test/DeathPace.Tests/`

**Interfaces:**
- Consumes: the `src/DeathPace/` and `test/DeathPace.Tests/` layout from Task 1.
- Produces: type `DeathPaceComponentFactory` (referenced by the `[assembly: ComponentFactory(...)]` attribute), type `DeathPaceComponent` (constructed by the factory), namespaces `DeathPace.Engine` / `.Persistence` / `.Snes` / `.Watchers` / `.UI`. Task 3 adds a test that constructs `DeathPaceComponentFactory` and `DeathPaceComponent`.

- [ ] **Step 1: Rename the eight type-bearing files**

```bash
git mv src/DeathPace/Engine/ReckoningCalculator.cs src/DeathPace/Engine/DeathPaceCalculator.cs
git mv src/DeathPace/Engine/ReckoningModel.cs src/DeathPace/Engine/DeathPaceModel.cs
git mv src/DeathPace/UI/Components/ReckoningComponent.cs src/DeathPace/UI/Components/DeathPaceComponent.cs
git mv src/DeathPace/UI/Components/ReckoningComponentFactory.cs src/DeathPace/UI/Components/DeathPaceComponentFactory.cs
git mv src/DeathPace/UI/Components/ReckoningComponentSettings.cs src/DeathPace/UI/Components/DeathPaceComponentSettings.cs
git mv test/DeathPace.Tests/ReckoningCalculatorTests.cs test/DeathPace.Tests/DeathPaceCalculatorTests.cs
git mv test/DeathPace.Tests/ReckoningModelTests.cs test/DeathPace.Tests/DeathPaceModelTests.cs
git mv test/DeathPace.Tests/ReckoningSettingsTests.cs test/DeathPace.Tests/DeathPaceSettingsTests.cs
```

- [ ] **Step 2: Replace the namespace prefix across all C# sources**

`LiveSplit.Reckoning` is a unique substring — it never overlaps `LiveSplit.UI.Components`, `LiveSplit.Model`, or `LiveSplit.Core`, so this replace cannot damage the LiveSplit API namespaces the Global Constraints protect.

```bash
grep -rl "LiveSplit\.Reckoning" --include=*.cs src test \
  | xargs sed -i 's/LiveSplit\.Reckoning/DeathPace/g'
```

Verify the LiveSplit API namespace survived untouched:

```bash
grep -rn "^namespace" --include=*.cs src test | sort -u
```

Expected: `DeathPace.Engine`, `DeathPace.Persistence`, `DeathPace.Snes`, `DeathPace.Tests`, `DeathPace.UI`, `DeathPace.Watchers`, and `LiveSplit.UI.Components` — the last one must still be present, on four files.

- [ ] **Step 3: Replace the remaining bare `Reckoning` identifiers**

After Step 2 the only `Reckoning` occurrences left are the eight type names, their usages, and four prose comments.

```bash
grep -rl "Reckoning" --include=*.cs src test \
  | xargs sed -i 's/Reckoning/DeathPace/g'
grep -rn "Reckoning" --include=*.cs src test
```

Expected from the second command: no output.

- [ ] **Step 4: Fix the prose comments to read "Death Pace"**

Step 3 turned four English sentences into `DeathPace`, which reads wrong in prose. Fix each:

`src/DeathPace/UI/Components/ComparisonNaming.cs` line 7:

```csharp
/// <summary>Label tables ported from LiveSplit's RunPrediction component (MIT)
/// so Death Pace presents identically for every comparison.
```

`src/DeathPace/UI/Components/DeathPaceComponentSettings.cs` line 14:

```csharp
/// same fields, XML keys, and defaults — plus Death Pace's ShowStatusDot.</summary>
```

`src/DeathPace/UI/Components/DeathPaceComponent.cs` around line 121 — this comment describes the `ComponentName` property, whose value Task 3 changes; reword the comment now and leave the string literal alone:

```csharp
    // stays "SMW Death Pace" regardless of comparison. The on-layout row label still
```

Then re-scan for any other prose hit:

```bash
grep -rn "DeathPace" --include=*.cs src test | grep "///\|// "
```

Review each result: identifiers inside comments (e.g. `DeathPaceCalculator`) are correct as-is; sentences reading "DeathPace" as a product name are not.

- [ ] **Step 5: Build**

Run: `dotnet build DeathPace.sln -c Release`
Expected: build succeeds with no errors. A `CS0246 type or namespace not found` here means a `using` line was missed — re-run the Step 3 grep.

- [ ] **Step 6: Run the tests**

Run: `dotnet test test/DeathPace.Tests -c Release`
Expected: `Passed! - Failed: 0, Passed: 120, Skipped: 0, Total: 120`

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "chore: rename namespaces and types from Reckoning to DeathPace"
```

---

### Task 3: User-visible identity strings (test-first)

Two strings escape the codebase into Andrew's filesystem and LiveSplit's UI: the sidecar suffix and the component name. Both get a failing test first.

**Files:**
- Create: `test/DeathPace.Tests/ComponentIdentityTests.cs`
- Modify: `test/DeathPace.Tests/SidecarStoreTests.cs:17,33`, `src/DeathPace/Persistence/SidecarStore.cs:16`, `src/DeathPace/UI/Components/DeathPaceComponentFactory.cs:11`, `src/DeathPace/UI/Components/DeathPaceComponent.cs:123`

**Interfaces:**
- Consumes: `DeathPaceComponentFactory` (parameterless constructor, `string ComponentName` property) from Task 2; `SidecarStore.PathFor(string lssPath)` returning `string`.
- Produces: sidecar suffix `.deathpace.json`; `ComponentName == "SMW Death Pace"` on both the factory and the component.

- [ ] **Step 1: Update the two sidecar path expectations to the new suffix**

In `test/DeathPace.Tests/SidecarStoreTests.cs`, line 17:

```csharp
    private string SidecarPath => Path.Combine(dir, "run.lss.deathpace.json");
```

and line 33:

```csharp
        Assert.Equal(@"C:\s\run.lss.deathpace.json", SidecarStore.PathFor(@"C:\s\run.lss"));
```

- [ ] **Step 2: Write the failing component-identity test**

Create `test/DeathPace.Tests/ComponentIdentityTests.cs`. This guards the layout-menu clumping decision — the `SMW` prefix exists so the component sorts next to Andrew's other SMW components, and a silent edit would break that without any other test noticing.

Cover the **factory only**. `DeathPaceComponent`'s constructor subscribes to `LiveSplitState` events and starts a live `Timer` with `Enabled = true`; no test in this suite constructs a `LiveSplitState`, and starting a polling component inside a unit test would be wrong. The factory's `ComponentName` is the string LiveSplit actually shows in the layout-editor menu, which is the decision worth guarding.

```csharp
using LiveSplit.UI.Components;
using Xunit;

namespace DeathPace.Tests;

public class ComponentIdentityTests
{
    [Fact]
    public void FactoryReportsTheSmwPrefixedMenuName()
    {
        Assert.Equal("SMW Death Pace", new DeathPaceComponentFactory().ComponentName);
    }
}
```

- [ ] **Step 3: Run the tests to verify all three assertions fail**

Run: `dotnet test test/DeathPace.Tests -c Release`
Expected: FAIL. `ComponentIdentityTests` fails with `Assert.Equal() Failure: Expected: SMW Death Pace, Actual: DeathPace`, and two `SidecarStoreTests` fail on `.deathpace.json` vs `.reckoning.json`.

- [ ] **Step 4: Change the sidecar suffix**

In `src/DeathPace/Persistence/SidecarStore.cs` line 16:

```csharp
    private const string Suffix = ".deathpace.json";
```

- [ ] **Step 5: Change both `ComponentName` values**

In `src/DeathPace/UI/Components/DeathPaceComponentFactory.cs` line 11:

```csharp
    public string ComponentName => "SMW Death Pace";
```

In `src/DeathPace/UI/Components/DeathPaceComponent.cs` line 123:

```csharp
    public string ComponentName => "SMW Death Pace";
```

`UpdateName => ComponentName` in the factory follows automatically — leave it.

The component's copy is the layout-editor *settings tab* title rather than the menu entry, so it is not covered by the Step 2 test. It still must change: the comment right above it (reworded in Task 2) promises one stable findable name, and leaving it as `DeathPace` would make the settings tab disagree with the menu.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test test/DeathPace.Tests -c Release`
Expected: `Passed! - Failed: 0, Passed: 121, Skipped: 0, Total: 121`

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: rename sidecar suffix and LiveSplit menu entry to Death Pace"
```

---

### Task 4: Documentation and release workflow

Rewrite the living docs. The README also loses its *dead reckoning* etymology paragraph, which no longer explains anything — it justified the old name.

**Files:**
- Modify: `README.md`, `CLAUDE.md`, `docs/TESTING.md`, `docs/superpowers/specs/2026-07-30-death-aware-bpt-design.md` (one added line), `.github/workflows/release.yml` (**edit but do not commit**)

**Interfaces:**
- Consumes: every name settled in Tasks 1–3 — `DeathPace.sln`, `test/DeathPace.Tests`, `SMWDeathPace.dll`, `"SMW Death Pace"`, `.deathpace.json`, `src/DeathPace/DeathPace.local.props`.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Rewrite `README.md`**

The README is the front door for someone finding the component, so it carries the SMW branding. Specific changes:

- Title `# Reckoning` → `# SMW Death Pace`.
- Delete the etymology sentence in the intro: *"The name is from *dead reckoning*: navigating forward from a known past position."* It explained the old name and is now false. Do not invent a replacement etymology.
- Every remaining prose "Reckoning" → "Death Pace", except "Reckoning-only" which reads better as "Death Pace-only".
- Install section: `` `Reckoning.dll` `` → `` `SMWDeathPace.dll` ``, and `add "Reckoning" (Information category)` → `add "SMW Death Pace" (Information category)`.
- Sidecar section: `` `<splits>.lss.reckoning.json` `` → `` `<splits>.lss.deathpace.json` ``.
- Build section: `dotnet build Reckoning.sln -c Release` → `dotnet build DeathPace.sln -c Release`; `dotnet test test/LiveSplit.Reckoning.Tests` → `dotnet test test/DeathPace.Tests`; `src/LiveSplit.Reckoning/Engine/` → `src/DeathPace/Engine/`.

- [ ] **Step 2: Rewrite `CLAUDE.md`**

Three hits. Line 1 title:

```markdown
# death_pace — project instructions
```

And the convention bullet at lines 36–37:

```markdown
- C# LiveSplit component, project name `DeathPace`, output
  `SMWDeathPace.dll` (the `SMW` prefix is for Components-folder clumping
  with `SMWCounters`; it appears only on the shipped artifact and the
  LiveSplit menu entry, never in the repo name or namespaces).
```

Leave the `docs/superpowers/` path references alone — those files keep their current filenames.

- [ ] **Step 3: Rewrite `docs/TESTING.md`**

- Line 1 title → `# SMW Death Pace — live testing & iteration guide`.
- Line 15: `` `src/LiveSplit.Reckoning/Reckoning.local.props` `` → `` `src/DeathPace/DeathPace.local.props` ``.
- Line 24: `dotnet build Reckoning.sln -c Release` → `dotnet build DeathPace.sln -c Release`.
- Line 25: `` `Reckoning.dll` `` → `` `SMWDeathPace.dll` ``.
- Line 28: `add "Reckoning" (Information category)` → `add "SMW Death Pace" (Information category)`.
- Lines 31, 43, 46, 56: prose "Reckoning" → "Death Pace".
- Lines 69, 100: `` `<splits>.lss.reckoning.json` `` → `` `<splits>.lss.deathpace.json` ``.
- Lines 111, 113, 123, 159: source paths `Engine/ReckoningCalculator.cs` → `Engine/DeathPaceCalculator.cs`, `Engine/ReckoningModel.cs` → `Engine/DeathPaceModel.cs`, `UI/Components/ReckoningComponent.cs` → `UI/Components/DeathPaceComponent.cs`.
- Line 135: `dotnet test test/LiveSplit.Reckoning.Tests` → `dotnet test test/DeathPace.Tests`.
- **Fix the stale test count**: the doc says 119; the suite is 121 after Task 3. Search for `119` and correct it.

- [ ] **Step 4: Add the rename note to the design spec**

The spec's body stays as written — it is a dated record and its "Reckoning" text is correct history. Insert one line directly under the `# Reckoning — Death-aware Best Possible Time (design)` heading:

```markdown
> **Renamed 2026-08-01:** the project this spec describes is now **Death Pace**
> (repo `death_pace`, shipping `SMWDeathPace.dll`). Body text below predates the
> rename and still says "Reckoning"; see `docs/superpowers/plans/2026-08-01-death-pace-rename.md`.
```

- [ ] **Step 5: Edit `.github/workflows/release.yml` — do not stage or commit it**

This file is untracked and Andrew commits CI workflow changes himself. Edit it in place and leave it untracked.

```yaml
      - name: Test
        run: dotnet test test/DeathPace.Tests -c Release
      - name: Build
        run: dotnet build src/DeathPace/DeathPace.csproj -c Release -p:Version=${{ steps.ver.outputs.version }}
```

In the Stage step, the DLL lookup and zip name:

```yaml
          $dll = Get-ChildItem -Recurse artifacts -Filter SMWDeathPace.dll | Select-Object -First 1
          if (-not $dll) { throw "SMWDeathPace.dll not found" }
          $snes = Get-ChildItem -Recurse artifacts -Filter SNES.dll | Select-Object -First 1
          if (-not $snes) { throw "SNES.dll missing from build output" }
          Copy-Item $dll.FullName, $snes.FullName, "README.md" $staging
          Compress-Archive -Path "$staging\*" -DestinationPath "SMWDeathPace-${{ steps.ver.outputs.version }}.zip"
```

The release body and both artifact-name/path fields:

```yaml
          body: |
            Copy **both** `SMWDeathPace.dll` and `SNES.dll` into `LiveSplit/Components/`,
            then add the "SMW Death Pace" component (Information category) to your layout.
          files: |
            SMWDeathPace-${{ steps.ver.outputs.version }}.zip
```

```yaml
          name: SMWDeathPace-${{ steps.ver.outputs.version }}
          path: SMWDeathPace-${{ steps.ver.outputs.version }}.zip
```

- [ ] **Step 6: Verify the workflow file is still untracked**

```bash
git status --short .github/
```

Expected: `?? .github/` — untracked. If it shows as staged (`A `), unstage it with `git restore --staged .github/`.

- [ ] **Step 7: Commit the docs only**

```bash
git add README.md CLAUDE.md docs/
git commit -m "docs: rename Reckoning to Death Pace across living docs"
```

---

### Task 5: Final verification sweep

Prove no stray "reckoning" survives outside the historical record, and that the tree builds clean from scratch.

**Files:** none modified unless the sweep finds something.

**Interfaces:**
- Consumes: the complete renamed tree from Tasks 1–4.
- Produces: nothing.

- [ ] **Step 1: Prove no tracked file outside the historical docs mentions "reckoning"**

```bash
git ls-files | xargs grep -ril "reckoning" \
  | grep -v "^docs/superpowers/plans/\|^docs/superpowers/specs/"
```

Expected: no output. Any file listed must be fixed and re-committed before continuing.

- [ ] **Step 2: Check the untracked workflow file separately**

`git ls-files` cannot see it, so it needs its own pass.

```bash
grep -in "reckoning" .github/workflows/release.yml
```

Expected: no output.

- [ ] **Step 3: Confirm no stray filenames survived**

```bash
git ls-files | grep -i "reckoning"
```

Expected: no output. (The plan and spec *filenames* contain no "reckoning", only their contents do.)

- [ ] **Step 4: Clean rebuild from scratch**

```bash
rm -rf artifacts
dotnet build DeathPace.sln -c Release
```

Expected: succeeds; output line names `artifacts\bin\DeathPace\release\SMWDeathPace.dll`.

- [ ] **Step 5: Full test run**

Run: `dotnet test test/DeathPace.Tests -c Release`
Expected: `Passed! - Failed: 0, Passed: 121, Skipped: 0, Total: 121`

- [ ] **Step 6: Review the whole diff against main**

```bash
git diff main --stat
```

Expected: renames across `src/`, `test/`, `props/`, the solution file, and edits to `README.md`, `CLAUDE.md`, `docs/TESTING.md`, the design spec, plus the new `ComponentIdentityTests.cs`. Nothing under `docs/superpowers/plans/` except this plan file. No `.github/` entries.

- [ ] **Step 7: Confirm line endings before handing off**

```bash
git ls-files --eol | grep -v "w/crlf\|w/lf" | head
```

Any file reporting `mixed` needs `git add --renormalize <file>` and an amend commit.

- [ ] **Step 8: Push the branch**

```bash
git push -u origin chore/rename-death-pace
```

Then stop. Andrew reviews the diff against `main` and merges himself — do not merge.

---

## Out-of-repo handoff checklist

**These run after Andrew merges to `main`, not during implementation.** They touch his live LiveSplit install, his GitHub account, and the folder this session is running inside — none of which can be done safely mid-branch.

- [ ] **1. Rename the GitHub repo** (needs Andrew's explicit go-ahead — this is outward-facing). GitHub sets up an automatic redirect from the old URL, so existing clones and links keep working.

```bash
gh api -X PATCH repos/amcknight/reckoning -f name=death_pace
```

- [ ] **2. Point the local remote at the new name** (optional — the redirect works either way, but this keeps `git remote -v` honest).

```bash
git remote set-url origin https://github.com/amcknight/death_pace.git
```

- [ ] **3. Close LiveSplit, then delete the stale DLL.** Without this, LiveSplit lists *both* "Reckoning" and "SMW Death Pace" in the layout editor and loads two copies of the component. LiveSplit locks the file while running, so it must be closed first.

```bash
rm "C:/Apps/LiveSplit/Components/Reckoning.dll"
```

- [ ] **4. Rename the learned-data sidecar.** This file holds Andrew's live-tested hot/cold bests — the rename is the only thing keeping them. **Ask Andrew for his splits path**, then:

```bash
mv "<splits>.lss.reckoning.json" "<splits>.lss.deathpace.json"
```

- [ ] **5. Re-add the component to the layout.** LiveSplit stores layout components by DLL filename, so the saved layout still points at `Reckoning.dll`. In LiveSplit: Edit Layout → remove the now-missing entry → add "SMW Death Pace" (Information category) → re-apply any non-default settings → save the layout.

- [ ] **6. Rename the root folder** — last, and only after VS Code has the workspace closed. Windows refuses to rename a directory any process holds open, and this Claude Code session's working directory is inside it.

```bash
mv /c/Users/thedo/git/reckoning /c/Users/thedo/git/death_pace
```

- [ ] **7. Carry over the Claude Code project state.** Claude keys per-project memory and history to the sanitized path, so the folder rename orphans both (`snes_offsets` → `c--Users-thedo-git-snes-offsets` confirms underscores become hyphens).

```bash
cp -r "/c/Users/thedo/.claude/projects/c--Users-thedo-git-reckoning" \
      "/c/Users/thedo/.claude/projects/c--Users-thedo-git-death-pace"
```

Verify `memory/MEMORY.md` and the four memory files landed, then delete the old directory once a session in the new folder confirms it picked them up.

- [ ] **8. Commit the release workflow.** `.github/workflows/release.yml` was edited in Task 4 but deliberately left untracked. Andrew commits it himself.

- [ ] **9. Rebuild and smoke-test live.** With LiveSplit closed, `dotnet build DeathPace.sln -c Release` deploys `SMWDeathPace.dll` + `SNES.dll` to the Components folder. Relaunch LiveSplit and walk step 1 of `docs/TESTING.md` (deathless value matches stock Run Prediction digit-for-digit) to confirm nothing broke in transit.

## Deferred

- **`SMWCounters` rename.** Andrew raised it; it is a sibling repo and CLAUDE.md forbids editing siblings from here. Separate task, separate repo.
- **Snake → kebab.** `death_pace` is snake for now, matching `snes_offsets`. Andrew likes kebab and may switch later; that is a second rename, not this one.
