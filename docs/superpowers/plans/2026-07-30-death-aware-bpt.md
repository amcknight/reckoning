# Reckoning — Death-aware BPT Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A LiveSplit layout component (`Reckoning.dll`) that shows a Death-aware Best Possible Time (DR-BPT) and a Sunk line for SMW kaizo runs, driven by emulator WRAM via SNES.dll.

**Architecture:** Three seams mirroring the approved spec: (1) a pure calc engine (marker model, hot/cold bests, DR-BPT math — no LiveSplit/WRAM types), (2) a watcher layer that turns WRAM byte reads into death/checkpoint/respawn events using kaizosplits' proven semantics, (3) a thin component shell (LiveSplit lifecycle, settings, rendering, sidecar JSON IO). Build layout is a faithful copy of SMWCounters.

**Tech Stack:** C# 12 on net481 (PolySharp), WinForms, xUnit 2.9.2, LiveSplit.Core 1.8.37 (fetched, not shipped), SNES.dll 1.6.0 (pinned in `lib/`, ships in release zip).

## Global Constraints

- Project name `LiveSplit.Reckoning`, assembly name `Reckoning`, output `Reckoning.dll` (project CLAUDE.md).
- Calc engine stays pure: no LiveSplit or WRAM types anywhere under `src/LiveSplit.Reckoning/Engine/` (spec §Architecture).
- Red-green TDD for every task; commit per task (house style).
- Sibling repos (`../snes_offsets`, `../kaizosplits`, `../SMWCounters`, `../spinlab`) are read-only reference. Friction becomes a `TODO(snes_offsets):` / `TODO(kaizosplits):` comment, never an edit over there.
- No magic numbers / fudge factors: every numeric constant is named with a why-comment; thresholds must be earned (spinlab rules, spec §Testing).
- Bests only update on a completed marker→exit observation ending in a real split — never skip/undo/reset (spec §Persistence).
- Sidecar path is exactly `<splits>.reckoning.json` next to the `.lss` file; corrupt/missing sidecar degrades to unlearned, never crashes (spec §Persistence).
- Sunk is exactly 0 while deathless in the current segment (spec §Core calculation).
- Fallback chain when data is unlearned: cold → hot → full best segment (standard BPT), with the row visually flagged (spec §Hot/cold).
- All WRAM addresses are console-space offsets (SNES `$7E0000` → offset `0`, `$7F0000` → offset `0x10000`), matching `SNES.Emu.Read1`.
- Work happens on branch `feature/dr-bpt`; Andrew merges to main himself.

## Reference facts (mined from siblings — cite these, do not re-research)

**SNES.dll public API** (`SNES` namespace, netstandard2.0, v1.6.0 — pinned copy at `../SMWCounters/lib/SNES.dll`):
- `Emu()` / `bool Attach(Process p)` / `bool Ready()` (throws = not ready) / `long GetOffset()` (throws while discovering) / `EmuStatus Status()` (never throws) / `string Describe()`.
- `byte Read1(int wramOffset)` — offset range `0..0x1FFFF`; throws if unresolved or emulator gone.
- `int Generation` (bump = rebind → re-baseline everything), `string Smc()` / `bool SmcChanged()` (ROM identity), `bool IsAttached`, `string StateName`.
- `EmuState { Detached, Searching, Discovering, Resolved, Degraded, NoContent, Held }`; `EmuStatus` has `StateName`, `IsCoolingDown`, `WitnessVerdict`, `WitnessBase`, `WramBase`.
- Consumer idiom (status-first): per tick `try { emu.Ready(); } catch { ready = false; }`; while not ready `try { emu.GetOffset(); ready = true; } catch { }`; render from `Status()`.

**WRAM addresses** (kaizosplits `Components/SMW/SMW/Memory.cs`, console-space offsets):

| Name | Offset | Absolute | Meaning |
|---|---|---|---|
| playerAnimation | `0x0071` | `$7E0071` | `9` = death animation |
| gameMode | `0x0100` | `$7E0100` | `18` = prepare level (spawn point) |
| roomNum | `0x010B` | `$7E010B` | current room |
| levelNum | `0x13BF` | `$7E13BF` | current level |
| midway | `0x13CE` | `$7E13CE` | steps 0→1 on midway tape |
| levelStart | `0x1935` | `$7E1935` | `1` = in level |
| io | `0x1DFB` | `$7E1DFB` | finish flags: 3=orb, 4=goal, 7=key, 8=fadeout |
| cpEntrance | `0x1B403` | `$7FB403` | retry-hack respawn entrance (bank $7F) |

**Detection semantics** (kaizosplits `Watchers.cs`, `Compare.cs`):
- `DiedNow = ShiftTo(playerAnimation, 9)`; `died` latch persists until Spawn.
- `Spawn = ShiftTo(gameMode, 18) && died` (respawn); `Put = same && !died` (fresh entry); Spawn clears `died`.
- `ToMidway = StepTo(midway, 1)` (exact +1 step, not any write); suppressed when a finish flag fired.
- `CPEntrance = InLevel && Shifted(cpEntrance) && !ShiftTo(cpEntrance, firstRoom) && !finish flags` — retry hacks rewrite `$7FB403` on every checkpoint touch; `firstRoom` (captured as `roomNum` when `levelNum` changes, cleared to 0 after a real CP) guards against the false fire at level entry.
- `InLevel = levelStart == 1`. `io` quirk: P-switch/star music transiently zeroes it, so keep the last non-zero value as the comparison baseline.
- Edge primitives: `ShiftTo(prev,curr,to) = prev!=to && curr==to`; `StepTo = curr==to && prev+1==curr`; `Shifted = prev!=curr`.

**SMWCounters template facts:** dual-mode references (ProjectReference when `LsSrcPath`/`SnesSrcPath` set, else `lib\*.dll` HintPath; LiveSplit.Core/UpdateManager `Private=false`, SNES.dll `Private=true`); `[assembly: ComponentFactory(typeof(...))]` registration; component class in `namespace LiveSplit.UI.Components`; WinForms `Timer { Interval = 15 }` polls WRAM (Update() only redraws); `state.OnReset` hook signature `(object sender, TimerPhase phase)`; GraphicsCache + `invalidator.Invalidate(0,0,width,height)` when changed; 5×5 px status dot at x=3; release workflow fetches LiveSplit 1.8.37 zip, stages component DLL + SNES.dll + README into the zip.

**spinlab hot/cold:** hot = state carried alive across the checkpoint (mid-stride, resources/momentum intact); cold = the deterministic post-respawn state after a death. Cold is what the runner actually has after dying, so post-death lookups prefer cold.

---

### Task 1: Repo scaffold and build skeleton

**Files:**
- Create: `Directory.Build.props`, `props/Reckoning.props`, `props/Reckoning.Paths.props`, `Reckoning.sln`, `.gitignore`, `scripts/fetch-livesplit-core.ps1`, `lib/.gitkeep`, `lib/SNES.dll` (copied from `../SMWCounters/lib/SNES.dll`), `src/LiveSplit.Reckoning/LiveSplit.Reckoning.csproj`, `src/LiveSplit.Reckoning/Properties/AssemblyInfo.cs`, `test/LiveSplit.Reckoning.Tests/LiveSplit.Reckoning.Tests.csproj`, `test/LiveSplit.Reckoning.Tests/SmokeTest.cs`

**Interfaces:**
- Consumes: nothing (first task).
- Produces: a building solution; `dotnet test` runs xUnit on net481; `InternalsVisibleTo("LiveSplit.Reckoning.Tests")` so later tasks can use `internal` types; `$(ComponentsPath)` post-build copy hook.

- [ ] **Step 1: Create the feature branch**

```bash
git checkout -b feature/dr-bpt
```

- [ ] **Step 2: Write build plumbing**

`Directory.Build.props` (SMWCounters pattern — super-repo import + props glob):

```xml
<Project>
  <PropertyGroup>
    <_SuperRepoBuildProps>$([MSBuild]::GetPathOfFileAbove(Directory.Build.props, $(MSBuildThisFileDirectory)..))</_SuperRepoBuildProps>
  </PropertyGroup>
  <Import Project="$(_SuperRepoBuildProps)" Condition="'$(_SuperRepoBuildProps)' != ''" />
  <Import Project="$(MSBuildThisFileDirectory)props\*.props" />
</Project>
```

`props/Reckoning.props`:

```xml
<Project>
  <PropertyGroup>
    <LangVersion>12</LangVersion>
    <Nullable>disable</Nullable>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <UseArtifactsOutput>true</UseArtifactsOutput>
  </PropertyGroup>
</Project>
```

`props/Reckoning.Paths.props`:

```xml
<Project>
  <PropertyGroup>
    <RootPath>$(MSBuildThisFileDirectory)..\</RootPath>
    <SrcPath>$(RootPath)src\</SrcPath>
    <TestPath>$(RootPath)test\</TestPath>
  </PropertyGroup>
</Project>
```

`.gitignore`:

```
artifacts/
bin/
obj/
lib/*.dll
!lib/SNES.dll
*.local.props
*.user
.superpowers/
```

- [ ] **Step 3: Copy the pinned SNES.dll and write the LiveSplit fetch script**

```powershell
Copy-Item ..\SMWCounters\lib\SNES.dll lib\SNES.dll
```

`scripts/fetch-livesplit-core.ps1` (same contract as SMWCounters' script):

```powershell
param([string]$Version = "1.8.37")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$zip = Join-Path $root "lib\LiveSplit_$Version.zip"
$url = "https://github.com/LiveSplit/LiveSplit/releases/download/$Version/LiveSplit_$Version.zip"
Invoke-WebRequest -Uri $url -OutFile $zip
$extract = Join-Path $root "lib\_ls_extract"
Expand-Archive -Path $zip -DestinationPath $extract -Force
foreach ($dll in "LiveSplit.Core.dll", "UpdateManager.dll") {
  Copy-Item (Get-ChildItem -Recurse $extract -Filter $dll | Select-Object -First 1).FullName (Join-Path $root "lib\$dll") -Force
}
Remove-Item $extract -Recurse -Force
Remove-Item $zip -Force
Write-Host "Fetched LiveSplit.Core.dll + UpdateManager.dll ($Version) into lib\"
```

Run it: `pwsh -File scripts/fetch-livesplit-core.ps1` and confirm `lib\LiveSplit.Core.dll` and `lib\UpdateManager.dll` exist.

- [ ] **Step 4: Write the two csproj files and AssemblyInfo**

`src/LiveSplit.Reckoning/LiveSplit.Reckoning.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net481</TargetFramework>
    <RootNamespace>LiveSplit.Reckoning</RootNamespace>
    <AssemblyName>Reckoning</AssemblyName>
    <UseWindowsForms>true</UseWindowsForms>
    <EnableDynamicLoading>true</EnableDynamicLoading>
    <DisableFastUpToDateCheck>true</DisableFastUpToDateCheck>
    <Version>0.1.0</Version>
    <!-- Kept in sync with scripts/fetch-livesplit-core.ps1 -->
    <LiveSplitVersion>1.8.37</LiveSplitVersion>
  </PropertyGroup>

  <!-- LiveSplit refs: ProjectReference in super-repo dev mode, lib dlls otherwise.
       Private=false: LiveSplit provides these at runtime, never ship them. -->
  <ItemGroup Condition="'$(LsSrcPath)' != ''">
    <ProjectReference Include="$(LsSrcPath)\LiveSplit.Core\LiveSplit.Core.csproj" Private="false" ExcludeAssets="runtime" />
  </ItemGroup>
  <ItemGroup Condition="'$(LsSrcPath)' == ''">
    <Reference Include="LiveSplit.Core">
      <HintPath>$(MSBuildThisFileDirectory)..\..\lib\LiveSplit.Core.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UpdateManager">
      <HintPath>$(MSBuildThisFileDirectory)..\..\lib\UpdateManager.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>

  <!-- SNES.dll: Private=true so it lands in output and ships beside Reckoning.dll. -->
  <ItemGroup Condition="'$(SnesSrcPath)' != ''">
    <ProjectReference Include="$(SnesSrcPath)\src\SNES\SNES.csproj" />
  </ItemGroup>
  <ItemGroup Condition="'$(SnesSrcPath)' == ''">
    <Reference Include="SNES">
      <HintPath>$(MSBuildThisFileDirectory)..\..\lib\SNES.dll</HintPath>
      <Private>true</Private>
    </Reference>
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="PolySharp" Version="1.14.1" PrivateAssets="all" />
    <Reference Include="System.Web.Extensions" />
  </ItemGroup>

  <!-- Deploy to a live LiveSplit install when Reckoning.local.props defines ComponentsPath.
       ContinueOnError: LiveSplit locks loaded DLLs; warn instead of failing the build. -->
  <Import Project="Reckoning.local.props" Condition="Exists('Reckoning.local.props')" />
  <Target Name="CopyToLiveSplitComponents" AfterTargets="Build" Condition="Exists('$(ComponentsPath)')">
    <ItemGroup>
      <_DeployPair Include="$(TargetPath);$(TargetDir)SNES.dll" />
    </ItemGroup>
    <Copy SourceFiles="@(_DeployPair)" DestinationFolder="$(ComponentsPath)" ContinueOnError="WarnAndContinue" />
    <Touch Files="@(_DeployPair->'$(ComponentsPath)\%(Filename)%(Extension)')" ContinueOnError="WarnAndContinue" />
  </Target>
</Project>
```

`src/LiveSplit.Reckoning/Properties/AssemblyInfo.cs`:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("LiveSplit.Reckoning.Tests")]
```

`test/LiveSplit.Reckoning.Tests/LiveSplit.Reckoning.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net481</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\LiveSplit.Reckoning\LiveSplit.Reckoning.csproj" />
  </ItemGroup>
  <!-- Private=true: tests need these present at runtime (LiveSplit isn't hosting us). -->
  <ItemGroup Condition="'$(LsSrcPath)' == ''">
    <Reference Include="LiveSplit.Core">
      <HintPath>..\..\lib\LiveSplit.Core.dll</HintPath>
      <Private>true</Private>
    </Reference>
    <Reference Include="UpdateManager">
      <HintPath>..\..\lib\UpdateManager.dll</HintPath>
      <Private>true</Private>
    </Reference>
  </ItemGroup>
</Project>
```

Create `Reckoning.sln` with both projects under `src`/`test` solution folders:

```bash
dotnet new sln -n Reckoning
dotnet sln add src/LiveSplit.Reckoning/LiveSplit.Reckoning.csproj --solution-folder src
dotnet sln add test/LiveSplit.Reckoning.Tests/LiveSplit.Reckoning.Tests.csproj --solution-folder test
```

- [ ] **Step 5: Write the smoke test**

`test/LiveSplit.Reckoning.Tests/SmokeTest.cs`:

```csharp
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class SmokeTest
{
    [Fact]
    public void TestFrameworkRuns() => Assert.True(true);
}
```

- [ ] **Step 6: Build and run tests**

Run: `dotnet build Reckoning.sln -c Debug` then `dotnet test test/LiveSplit.Reckoning.Tests -c Debug`
Expected: build succeeds; 1 test passes. (If the LiveSplit dlls are missing, re-run the fetch script first.)

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "build: scaffold LiveSplit.Reckoning solution mirroring SMWCounters layout"
```

---

### Task 2: Calc engine — BestsStore (hot/cold bests, attempts)

**Files:**
- Create: `src/LiveSplit.Reckoning/Engine/Variant.cs`, `src/LiveSplit.Reckoning/Engine/MarkerKey.cs`, `src/LiveSplit.Reckoning/Engine/BestEntry.cs`, `src/LiveSplit.Reckoning/Engine/BestsStore.cs`
- Test: `test/LiveSplit.Reckoning.Tests/BestsStoreTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces (used by Tasks 4, 5, 8, 9):
  - `enum Variant { Hot, Cold }` (namespace `LiveSplit.Reckoning.Engine`)
  - `readonly record struct MarkerKey(int SegmentIndex, int MarkerIndex, Variant Variant)`
  - `sealed record BestEntry(long BestMs, int Attempts)`
  - `sealed class BestsStore` with:
    - `void Record(int segmentIndex, int markerIndex, Variant variant, TimeSpan duration)` — min-merge best, `Attempts + 1`
    - `bool TryGetBest(int segmentIndex, int markerIndex, Variant variant, out TimeSpan best)`
    - `int GetAttempts(int segmentIndex, int markerIndex, Variant variant)` — 0 when absent
    - `bool TryGetEntry(MarkerKey key, out BestEntry entry)` / `void SetEntry(MarkerKey key, BestEntry entry)` / `void RemoveEntry(MarkerKey key)` — raw access for persistence load and the undo journal
    - `IReadOnlyCollection<MarkerKey> Keys { get; }`

- [ ] **Step 1: Write the failing tests**

`test/LiveSplit.Reckoning.Tests/BestsStoreTests.cs`:

```csharp
using System;
using LiveSplit.Reckoning.Engine;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class BestsStoreTests
{
    private static TimeSpan Ms(long ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void UnknownKeyHasNoBestAndZeroAttempts()
    {
        var store = new BestsStore();
        Assert.False(store.TryGetBest(0, 0, Variant.Hot, out _));
        Assert.Equal(0, store.GetAttempts(0, 0, Variant.Hot));
    }

    [Fact]
    public void RecordSetsBestAndCountsAttempt()
    {
        var store = new BestsStore();
        store.Record(2, 1, Variant.Cold, Ms(41_500));
        Assert.True(store.TryGetBest(2, 1, Variant.Cold, out var best));
        Assert.Equal(Ms(41_500), best);
        Assert.Equal(1, store.GetAttempts(2, 1, Variant.Cold));
    }

    [Fact]
    public void SlowerObservationKeepsBestButCountsAttempt()
    {
        var store = new BestsStore();
        store.Record(0, 0, Variant.Hot, Ms(30_000));
        store.Record(0, 0, Variant.Hot, Ms(45_000));
        Assert.True(store.TryGetBest(0, 0, Variant.Hot, out var best));
        Assert.Equal(Ms(30_000), best);
        Assert.Equal(2, store.GetAttempts(0, 0, Variant.Hot));
    }

    [Fact]
    public void FasterObservationImprovesBest()
    {
        var store = new BestsStore();
        store.Record(0, 0, Variant.Hot, Ms(30_000));
        store.Record(0, 0, Variant.Hot, Ms(28_250));
        Assert.True(store.TryGetBest(0, 0, Variant.Hot, out var best));
        Assert.Equal(Ms(28_250), best);
    }

    [Fact]
    public void VariantsAreIndependent()
    {
        var store = new BestsStore();
        store.Record(1, 1, Variant.Hot, Ms(20_000));
        store.Record(1, 1, Variant.Cold, Ms(25_000));
        store.TryGetBest(1, 1, Variant.Hot, out var hot);
        store.TryGetBest(1, 1, Variant.Cold, out var cold);
        Assert.Equal(Ms(20_000), hot);
        Assert.Equal(Ms(25_000), cold);
    }

    [Fact]
    public void SetEntryAndRemoveEntryRoundTrip()
    {
        var store = new BestsStore();
        var key = new MarkerKey(3, 2, Variant.Cold);
        store.SetEntry(key, new BestEntry(12_345, 7));
        Assert.True(store.TryGetEntry(key, out var entry));
        Assert.Equal(12_345, entry.BestMs);
        Assert.Equal(7, entry.Attempts);
        store.RemoveEntry(key);
        Assert.False(store.TryGetEntry(key, out _));
        Assert.Empty(store.Keys);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter BestsStoreTests`
Expected: FAIL — compile error, `BestsStore`/`Variant` not defined.

- [ ] **Step 3: Write the implementation**

`src/LiveSplit.Reckoning/Engine/Variant.cs`:

```csharp
namespace LiveSplit.Reckoning.Engine;

/// <summary>Spinlab's variant concept: Hot = crossed the marker alive and in
/// flow (resources/momentum intact); Cold = respawned at the marker after a
/// death. Marker→exit times differ between the two, so bests are kept apart.</summary>
public enum Variant
{
    Hot,
    Cold,
}
```

`src/LiveSplit.Reckoning/Engine/MarkerKey.cs`:

```csharp
namespace LiveSplit.Reckoning.Engine;

public readonly record struct MarkerKey(int SegmentIndex, int MarkerIndex, Variant Variant);
```

`src/LiveSplit.Reckoning/Engine/BestEntry.cs`:

```csharp
namespace LiveSplit.Reckoning.Engine;

public sealed record BestEntry(long BestMs, int Attempts);
```

`src/LiveSplit.Reckoning/Engine/BestsStore.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace LiveSplit.Reckoning.Engine;

/// <summary>Learned marker→exit bests, keyed (segment, marker, variant).
/// Pure data — no LiveSplit or WRAM types.</summary>
public sealed class BestsStore
{
    private readonly Dictionary<MarkerKey, BestEntry> entries = new();

    public IReadOnlyCollection<MarkerKey> Keys => entries.Keys;

    public void Record(int segmentIndex, int markerIndex, Variant variant, TimeSpan duration)
    {
        var key = new MarkerKey(segmentIndex, markerIndex, variant);
        long ms = (long)duration.TotalMilliseconds;
        entries[key] = entries.TryGetValue(key, out var prior)
            ? new BestEntry(Math.Min(prior.BestMs, ms), prior.Attempts + 1)
            : new BestEntry(ms, 1);
    }

    public bool TryGetBest(int segmentIndex, int markerIndex, Variant variant, out TimeSpan best)
    {
        if (entries.TryGetValue(new MarkerKey(segmentIndex, markerIndex, variant), out var entry))
        {
            best = TimeSpan.FromMilliseconds(entry.BestMs);
            return true;
        }
        best = default;
        return false;
    }

    public int GetAttempts(int segmentIndex, int markerIndex, Variant variant) =>
        entries.TryGetValue(new MarkerKey(segmentIndex, markerIndex, variant), out var entry) ? entry.Attempts : 0;

    public bool TryGetEntry(MarkerKey key, out BestEntry entry) => entries.TryGetValue(key, out entry);

    public void SetEntry(MarkerKey key, BestEntry entry) => entries[key] = entry;

    public void RemoveEntry(MarkerKey key) => entries.Remove(key);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter BestsStoreTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/LiveSplit.Reckoning/Engine test/LiveSplit.Reckoning.Tests/BestsStoreTests.cs
git commit -m "feat: BestsStore with per-variant marker bests and attempt counts"
```

---

### Task 3: Calc engine — SegmentTracker (marker model + observations)

**Files:**
- Create: `src/LiveSplit.Reckoning/Engine/SegmentTracker.cs`, `src/LiveSplit.Reckoning/Engine/Observation.cs`
- Test: `test/LiveSplit.Reckoning.Tests/SegmentTrackerTests.cs`

**Interfaces:**
- Consumes: `Variant` (Task 2).
- Produces (used by Task 5):
  - `sealed record Observation(int MarkerIndex, Variant Variant, TimeSpan Duration)`
  - `sealed class SegmentTracker` with:
    - `int CurrentMarker { get; }` / `Variant CurrentVariant { get; }` / `bool DiedThisSegment { get; }` / `bool IsActive { get; }`
    - `void StartSegment(TimeSpan elapsed)` — marker 0, Hot, opens the hot-0 observation
    - `void OnCheckpoint(TimeSpan elapsed)` — marker+1, Hot, opens hot observation at the new marker
    - `void OnDeath()` — sets `DiedThisSegment`, `CurrentVariant = Cold` (spec: runner is assumed to respawn at the last touched marker)
    - `void OnRespawn(TimeSpan elapsed)` — opens/overwrites the Cold observation at the current marker (cold clock starts at respawn)
    - `IReadOnlyList<Observation> CompleteSegment(TimeSpan splitElapsed)` — closes every open observation as `splitElapsed - start`, returns them, deactivates the tracker
    - `void Discard()` — drops all in-flight observations and marker state (undo/skip/reset path)

**Semantics to encode** (the observation model, derived from spec §Marker model + §Persistence):
- An *arrival* at a situation (marker, variant) opens an observation stamped with run-elapsed. Arrivals: segment start (hot 0), checkpoint touch (hot M+1), respawn (cold M).
- Re-arrival at the same (marker, variant) — e.g. dying twice at the same checkpoint — *overwrites* the open observation: the later arrival yields the shorter, still fully-achieved span, which is the only one min() could keep anyway.
- Observations survive deaths: being hot at marker 1, dying, and eventually exiting still completes hot-1→exit with the death time included. That is a true achieved time; min() over attempts keeps the honest best. No fudge.
- Nothing is recorded here — `CompleteSegment` just returns observations; the caller (Task 5) records them, so discard paths never touch the store.

- [ ] **Step 1: Write the failing tests**

`test/LiveSplit.Reckoning.Tests/SegmentTrackerTests.cs`:

```csharp
using System;
using System.Linq;
using LiveSplit.Reckoning.Engine;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class SegmentTrackerTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void StartSegmentIsHotMarkerZero()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(10));
        Assert.Equal(0, t.CurrentMarker);
        Assert.Equal(Variant.Hot, t.CurrentVariant);
        Assert.False(t.DiedThisSegment);
        Assert.True(t.IsActive);
    }

    [Fact]
    public void DeathlessSegmentCompletesSingleHotZeroObservation()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(10));
        var obs = t.CompleteSegment(S(45));
        var o = Assert.Single(obs);
        Assert.Equal(new Observation(0, Variant.Hot, S(35)), o);
        Assert.False(t.IsActive);
    }

    [Fact]
    public void CheckpointsAreOrderedMarkers()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(0));
        t.OnCheckpoint(S(20));   // marker 1
        t.OnCheckpoint(S(35));   // marker 2 (multi-checkpoint retry hack)
        Assert.Equal(2, t.CurrentMarker);
        Assert.Equal(Variant.Hot, t.CurrentVariant);
        var obs = t.CompleteSegment(S(60));
        Assert.Equal(3, obs.Count);
        Assert.Contains(new Observation(0, Variant.Hot, S(60)), obs);
        Assert.Contains(new Observation(1, Variant.Hot, S(40)), obs);
        Assert.Contains(new Observation(2, Variant.Hot, S(25)), obs);
    }

    [Fact]
    public void DeathFlipsVariantColdAtCurrentMarker()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(0));
        t.OnCheckpoint(S(20));
        t.OnDeath();
        Assert.True(t.DiedThisSegment);
        Assert.Equal(1, t.CurrentMarker);
        Assert.Equal(Variant.Cold, t.CurrentVariant);
    }

    [Fact]
    public void RespawnOpensColdObservationAtRespawnTime()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(0));
        t.OnCheckpoint(S(20));
        t.OnDeath();
        t.OnRespawn(S(26));
        var obs = t.CompleteSegment(S(70));
        Assert.Contains(new Observation(1, Variant.Cold, S(44)), obs);   // 70 - 26
        Assert.Contains(new Observation(1, Variant.Hot, S(50)), obs);    // hot obs survives the death
        Assert.Contains(new Observation(0, Variant.Hot, S(70)), obs);
    }

    [Fact]
    public void SecondDeathAtSameMarkerOverwritesColdObservation()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(0));
        t.OnCheckpoint(S(20));
        t.OnDeath();
        t.OnRespawn(S(26));
        t.OnDeath();
        t.OnRespawn(S(40));
        var obs = t.CompleteSegment(S(70));
        Assert.Single(obs.Where(o => o.Variant == Variant.Cold));
        Assert.Contains(new Observation(1, Variant.Cold, S(30)), obs);   // latest respawn wins
    }

    [Fact]
    public void CheckpointAfterColdRespawnIsHotAgain()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(0));
        t.OnDeath();             // death at marker 0
        t.OnRespawn(S(8));       // cold at marker 0 (spec: marker 0 has both variants)
        Assert.Equal(Variant.Cold, t.CurrentVariant);
        t.OnCheckpoint(S(30));   // reached checkpoint alive -> hot at marker 1
        Assert.Equal(1, t.CurrentMarker);
        Assert.Equal(Variant.Hot, t.CurrentVariant);
        var obs = t.CompleteSegment(S(50));
        Assert.Contains(new Observation(0, Variant.Cold, S(42)), obs);
        Assert.Contains(new Observation(1, Variant.Hot, S(20)), obs);
    }

    [Fact]
    public void DiscardDropsEverything()
    {
        var t = new SegmentTracker();
        t.StartSegment(S(0));
        t.OnCheckpoint(S(20));
        t.OnDeath();
        t.Discard();
        Assert.False(t.IsActive);
        t.StartSegment(S(30));
        Assert.Equal(0, t.CurrentMarker);
        Assert.False(t.DiedThisSegment);
        var obs = t.CompleteSegment(S(40));
        Assert.Equal(new Observation(0, Variant.Hot, S(10)), Assert.Single(obs));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter SegmentTrackerTests`
Expected: FAIL — `SegmentTracker`/`Observation` not defined.

- [ ] **Step 3: Write the implementation**

`src/LiveSplit.Reckoning/Engine/Observation.cs`:

```csharp
using System;

namespace LiveSplit.Reckoning.Engine;

/// <summary>A completed marker→exit span within one segment attempt.</summary>
public sealed record Observation(int MarkerIndex, Variant Variant, TimeSpan Duration);
```

`src/LiveSplit.Reckoning/Engine/SegmentTracker.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace LiveSplit.Reckoning.Engine;

/// <summary>Ordered-progress-marker state for the segment in flight.
/// Markers are identified by order-within-segment (marker 0 = segment start);
/// observations open on arrival at a (marker, variant) situation and complete
/// only when the caller reports a real split.</summary>
public sealed class SegmentTracker
{
    // Open observations: (marker, variant) -> run-elapsed at arrival.
    // Re-arrival overwrites: the later arrival is the shorter achieved span,
    // which is the only one a min-merge could keep.
    private readonly Dictionary<(int Marker, Variant Variant), TimeSpan> open = new();

    public int CurrentMarker { get; private set; }
    public Variant CurrentVariant { get; private set; }
    public bool DiedThisSegment { get; private set; }
    public bool IsActive { get; private set; }

    public void StartSegment(TimeSpan elapsed)
    {
        open.Clear();
        CurrentMarker = 0;
        CurrentVariant = Variant.Hot;
        DiedThisSegment = false;
        IsActive = true;
        open[(0, Variant.Hot)] = elapsed;
    }

    public void OnCheckpoint(TimeSpan elapsed)
    {
        if (!IsActive) return;
        CurrentMarker++;
        CurrentVariant = Variant.Hot;
        open[(CurrentMarker, Variant.Hot)] = elapsed;
    }

    public void OnDeath()
    {
        if (!IsActive) return;
        DiedThisSegment = true;
        // Spec: on death the runner is assumed to respawn at the last touched
        // marker — the situation is cold from this moment even before respawn.
        CurrentVariant = Variant.Cold;
    }

    public void OnRespawn(TimeSpan elapsed)
    {
        if (!IsActive) return;
        CurrentVariant = Variant.Cold;
        open[(CurrentMarker, Variant.Cold)] = elapsed;
    }

    public IReadOnlyList<Observation> CompleteSegment(TimeSpan splitElapsed)
    {
        var result = open
            .Select(kv => new Observation(kv.Key.Marker, kv.Key.Variant, splitElapsed - kv.Value))
            .ToList();
        open.Clear();
        IsActive = false;
        return result;
    }

    public void Discard()
    {
        open.Clear();
        CurrentMarker = 0;
        CurrentVariant = Variant.Hot;
        DiedThisSegment = false;
        IsActive = false;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter SegmentTrackerTests`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/LiveSplit.Reckoning/Engine test/LiveSplit.Reckoning.Tests/SegmentTrackerTests.cs
git commit -m "feat: SegmentTracker marker model with hot/cold observations"
```

---

### Task 4: Calc engine — ReckoningCalculator (DR-BPT + Sunk + fallback chain)

**Files:**
- Create: `src/LiveSplit.Reckoning/Engine/ReckoningCalculator.cs`, `src/LiveSplit.Reckoning/Engine/ReckoningResult.cs`
- Test: `test/LiveSplit.Reckoning.Tests/ReckoningCalculatorTests.cs`

**Interfaces:**
- Consumes: `Variant` (Task 2).
- Produces (used by Tasks 5, 9):
  - `enum BestSource { StandardBpt, ColdBest, HotBest }`
  - `sealed record ReckoningResult(TimeSpan? DrBpt, TimeSpan? Sunk, bool Unlearned, BestSource Source)`
  - `static class ReckoningCalculator` with:
    ```csharp
    public static ReckoningResult Compute(
        TimeSpan elapsed,                  // run elapsed now (current timing method)
        TimeSpan segmentStartElapsed,      // run elapsed when current segment began
        TimeSpan? currentSegmentFullBest,  // LiveSplit best segment for the current split; null = unlearned
        TimeSpan? remainingFullBestsSum,   // sum of best segments strictly after current; null = any missing; Zero on last segment
        bool diedThisSegment,
        int currentMarker,
        Func<int, Variant, TimeSpan?> markerBest)  // marker→exit lookup for CURRENT segment only
    ```

**Math to encode** (spec §Core calculation, §Hot/cold):
- Standard BPT (naive) = `max(segmentStartElapsed + fullBest, elapsed) + remainingSum`. The `max` mirrors LiveSplit: a segment can't finish in the past. Null if `fullBest` or `remainingSum` is null.
- **Deathless:** DR-BPT ≡ standard BPT, Sunk = 0 exactly, `Source = StandardBpt`, `Unlearned = false`. This is what makes "Sunk is zero while deathless" hold by construction.
- **After a death:** current-segment finish = `elapsed + best(currentMarker, Cold)`; fallback chain: Cold → Hot (`Unlearned = true`) → standard-BPT current term (`Unlearned = true`). DR-BPT = finish + remainingSum. Null-propagate: if the chain bottoms out at standard and `fullBest` is null, DR-BPT is null.
- Sunk = DR-BPT − standard BPT (null if either is null). No clamping — with consistent data it is ≥ 0; a negative value would expose an inconsistency, not hide it (spinlab: no fudge factors).

- [ ] **Step 1: Write the failing tests**

`test/LiveSplit.Reckoning.Tests/ReckoningCalculatorTests.cs`:

```csharp
using System;
using LiveSplit.Reckoning.Engine;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class ReckoningCalculatorTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);
    private static Func<int, Variant, TimeSpan?> NoBests => (_, _) => null;

    private static Func<int, Variant, TimeSpan?> Bests(params (int Marker, Variant V, double Secs)[] entries) =>
        (m, v) =>
        {
            foreach (var e in entries)
                if (e.Marker == m && e.V == v) return S(e.Secs);
            return null;
        };

    [Fact]
    public void DeathlessEqualsStandardBptWithZeroSunk()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(100), segmentStartElapsed: S(90),
            currentSegmentFullBest: S(30), remainingFullBestsSum: S(200),
            diedThisSegment: false, currentMarker: 1, markerBest: Bests((1, Variant.Hot, 10)));
        Assert.Equal(S(90 + 30 + 200), r.DrBpt);   // marker data ignored while deathless
        Assert.Equal(TimeSpan.Zero, r.Sunk);
        Assert.False(r.Unlearned);
        Assert.Equal(BestSource.StandardBpt, r.Source);
    }

    [Fact]
    public void DeathlessClampsToElapsedWhenBehindBestSegment()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(130), segmentStartElapsed: S(90),
            currentSegmentFullBest: S(30), remainingFullBestsSum: S(200),
            diedThisSegment: false, currentMarker: 0, markerBest: NoBests);
        Assert.Equal(S(130 + 200), r.DrBpt);   // max(90+30, 130) = 130
        Assert.Equal(TimeSpan.Zero, r.Sunk);
    }

    [Fact]
    public void AfterDeathUsesColdBestFromCurrentMarker()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(140), segmentStartElapsed: S(90),
            currentSegmentFullBest: S(30), remainingFullBestsSum: S(200),
            diedThisSegment: true, currentMarker: 1,
            markerBest: Bests((1, Variant.Cold, 22), (1, Variant.Hot, 18)));
        Assert.Equal(S(140 + 22 + 200), r.DrBpt);
        // standard = max(90+30, 140) + 200 = 340 ; sunk = 362 - 340
        Assert.Equal(S(22), r.Sunk);
        Assert.False(r.Unlearned);
        Assert.Equal(BestSource.ColdBest, r.Source);
    }

    [Fact]
    public void MissingColdFallsBackToHotFlaggedUnlearned()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(140), segmentStartElapsed: S(90),
            currentSegmentFullBest: S(30), remainingFullBestsSum: S(200),
            diedThisSegment: true, currentMarker: 1,
            markerBest: Bests((1, Variant.Hot, 18)));
        Assert.Equal(S(140 + 18 + 200), r.DrBpt);
        Assert.True(r.Unlearned);
        Assert.Equal(BestSource.HotBest, r.Source);
    }

    [Fact]
    public void NoMarkerDataDegradesToStandardBptFlaggedUnlearned()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(140), segmentStartElapsed: S(90),
            currentSegmentFullBest: S(30), remainingFullBestsSum: S(200),
            diedThisSegment: true, currentMarker: 1, markerBest: NoBests);
        Assert.Equal(S(140 + 200), r.DrBpt);   // max(120, 140) + 200
        Assert.Equal(TimeSpan.Zero, r.Sunk);   // identical to standard by definition
        Assert.True(r.Unlearned);
        Assert.Equal(BestSource.StandardBpt, r.Source);
    }

    [Fact]
    public void NullBestSegmentMakesResultNull()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(140), segmentStartElapsed: S(90),
            currentSegmentFullBest: null, remainingFullBestsSum: S(200),
            diedThisSegment: false, currentMarker: 0, markerBest: NoBests);
        Assert.Null(r.DrBpt);
        Assert.Null(r.Sunk);
    }

    [Fact]
    public void ColdBestStillWorksWhenLiveSplitBestsMissing()
    {
        // Learned cold data answers even when LiveSplit has no best segment yet,
        // but Sunk needs standard BPT so it stays null.
        var r = ReckoningCalculator.Compute(
            elapsed: S(140), segmentStartElapsed: S(90),
            currentSegmentFullBest: null, remainingFullBestsSum: S(200),
            diedThisSegment: true, currentMarker: 0,
            markerBest: Bests((0, Variant.Cold, 25)));
        Assert.Equal(S(140 + 25 + 200), r.DrBpt);
        Assert.Null(r.Sunk);
        Assert.Equal(BestSource.ColdBest, r.Source);
    }

    [Fact]
    public void NullRemainingSumMakesResultNull()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(140), segmentStartElapsed: S(90),
            currentSegmentFullBest: S(30), remainingFullBestsSum: null,
            diedThisSegment: true, currentMarker: 0,
            markerBest: Bests((0, Variant.Cold, 25)));
        Assert.Null(r.DrBpt);
        Assert.Null(r.Sunk);
    }

    [Fact]
    public void LastSegmentUsesZeroRemainingSum()
    {
        var r = ReckoningCalculator.Compute(
            elapsed: S(300), segmentStartElapsed: S(280),
            currentSegmentFullBest: S(40), remainingFullBestsSum: TimeSpan.Zero,
            diedThisSegment: true, currentMarker: 0,
            markerBest: Bests((0, Variant.Cold, 35)));
        Assert.Equal(S(335), r.DrBpt);
        Assert.Equal(S(335 - 320), r.Sunk);    // standard = max(320, 300) = 320
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter ReckoningCalculatorTests`
Expected: FAIL — `ReckoningCalculator`/`BestSource` not defined.

- [ ] **Step 3: Write the implementation**

`src/LiveSplit.Reckoning/Engine/ReckoningResult.cs`:

```csharp
using System;

namespace LiveSplit.Reckoning.Engine;

/// <summary>Which rung of the fallback chain answered the current-segment term.</summary>
public enum BestSource
{
    StandardBpt,
    ColdBest,
    HotBest,
}

public sealed record ReckoningResult(TimeSpan? DrBpt, TimeSpan? Sunk, bool Unlearned, BestSource Source);
```

`src/LiveSplit.Reckoning/Engine/ReckoningCalculator.cs`:

```csharp
using System;

namespace LiveSplit.Reckoning.Engine;

public static class ReckoningCalculator
{
    public static ReckoningResult Compute(
        TimeSpan elapsed,
        TimeSpan segmentStartElapsed,
        TimeSpan? currentSegmentFullBest,
        TimeSpan? remainingFullBestsSum,
        bool diedThisSegment,
        int currentMarker,
        Func<int, Variant, TimeSpan?> markerBest)
    {
        // Standard BPT's current-segment finish: can't finish in the past,
        // hence the max with elapsed (mirrors LiveSplit's own BPT).
        TimeSpan? standardFinish = currentSegmentFullBest is TimeSpan fb
            ? Max(segmentStartElapsed + fb, elapsed)
            : null;
        TimeSpan? standardBpt = Add(standardFinish, remainingFullBestsSum);

        if (!diedThisSegment)
        {
            // Deathless: the naive calculation is still achievable. DR-BPT is
            // standard BPT by definition, which makes Sunk exactly zero.
            return new ReckoningResult(standardBpt, standardBpt is null ? null : TimeSpan.Zero, false, BestSource.StandardBpt);
        }

        // Fallback chain: cold -> hot -> standard BPT (spec §Hot/cold).
        TimeSpan? finish;
        BestSource source;
        bool unlearned;
        if (markerBest(currentMarker, Variant.Cold) is TimeSpan cold)
        {
            finish = elapsed + cold;
            source = BestSource.ColdBest;
            unlearned = false;
        }
        else if (markerBest(currentMarker, Variant.Hot) is TimeSpan hot)
        {
            finish = elapsed + hot;
            source = BestSource.HotBest;
            unlearned = true;
        }
        else
        {
            finish = standardFinish;
            source = BestSource.StandardBpt;
            unlearned = true;
        }

        TimeSpan? drBpt = Add(finish, remainingFullBestsSum);
        // Sunk is honest arithmetic, not clamped: with consistent data it is
        // >= 0, and a negative value would expose an inconsistency worth seeing.
        TimeSpan? sunk = drBpt is TimeSpan d && standardBpt is TimeSpan s ? d - s : null;
        return new ReckoningResult(drBpt, sunk, unlearned, source);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a >= b ? a : b;

    private static TimeSpan? Add(TimeSpan? a, TimeSpan? b) =>
        a is TimeSpan x && b is TimeSpan y ? x + y : null;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter ReckoningCalculatorTests`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/LiveSplit.Reckoning/Engine test/LiveSplit.Reckoning.Tests/ReckoningCalculatorTests.cs
git commit -m "feat: ReckoningCalculator with DR-BPT math and cold->hot->standard fallback"
```

---

### Task 5: Calc engine — ReckoningModel (run orchestration + undo journal)

**Files:**
- Create: `src/LiveSplit.Reckoning/Engine/ReckoningModel.cs`
- Test: `test/LiveSplit.Reckoning.Tests/ReckoningModelTests.cs`

**Interfaces:**
- Consumes: `BestsStore`, `SegmentTracker`, `Observation`, `ReckoningCalculator`, `ReckoningResult`, `MarkerKey`, `BestEntry`, `Variant` (Tasks 2–4).
- Produces (used by Task 9 — this is the only Engine type the component shell drives):
  - `sealed class ReckoningModel` with:
    - `ReckoningModel(BestsStore store)`
    - `int CurrentSegmentIndex { get; }` / `bool IsRunning { get; }` / `bool DiedThisSegment { get; }` / `int CurrentMarker { get; }`
    - `void OnStart(TimeSpan elapsed)` — segment 0, starts tracking
    - `void OnDeath()` / `void OnCheckpoint(TimeSpan elapsed)` / `void OnRespawn(TimeSpan elapsed)` — forwarded to the tracker (no-ops when not running)
    - `void OnSplit(TimeSpan elapsed)` — completes observations, records them into the store for the finished segment (journaled), advances to the next segment
    - `void OnUndoSplit(TimeSpan elapsed)` — reverts the store records of the undone split (journal pop), steps the segment index back, restarts tracking at marker 0 (spec: affected segment's state is discarded)
    - `void OnSkipSplit(TimeSpan elapsed)` — discards observations (skip is not a real split), advances the index, restarts tracking
    - `void OnReset()` — discards everything, stops running
    - `ReckoningResult Compute(TimeSpan elapsed, TimeSpan segmentStartElapsed, TimeSpan? currentSegmentFullBest, TimeSpan? remainingFullBestsSum)` — closes over the store for the current segment's marker lookups

**Undo journal semantics:** the spec says undone splits are "never recorded as bests", but the record happens at split time. So `OnSplit` journals, per record, the key and its *prior* `BestEntry` (or absence); `OnUndoSplit` pops the journal and restores priors (removing entries that didn't exist). Multi-level undo pops repeatedly; an empty journal (e.g. component loaded mid-run) just discards in-flight state.

- [ ] **Step 1: Write the failing tests**

`test/LiveSplit.Reckoning.Tests/ReckoningModelTests.cs`:

```csharp
using System;
using LiveSplit.Reckoning.Engine;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class ReckoningModelTests
{
    private static TimeSpan S(double s) => TimeSpan.FromSeconds(s);

    [Fact]
    public void SplitRecordsObservationsForFinishedSegmentAndAdvances()
    {
        var store = new BestsStore();
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnCheckpoint(S(20));
        m.OnSplit(S(50));
        Assert.Equal(1, m.CurrentSegmentIndex);
        Assert.True(store.TryGetBest(0, 0, Variant.Hot, out var hot0));
        Assert.Equal(S(50), hot0);
        Assert.True(store.TryGetBest(0, 1, Variant.Hot, out var hot1));
        Assert.Equal(S(30), hot1);
        Assert.False(m.DiedThisSegment);   // new segment starts clean
    }

    [Fact]
    public void DeathRespawnSplitRecordsColdBest()
    {
        var store = new BestsStore();
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnCheckpoint(S(20));
        m.OnDeath();
        m.OnRespawn(S(26));
        m.OnSplit(S(70));
        Assert.True(store.TryGetBest(0, 1, Variant.Cold, out var cold));
        Assert.Equal(S(44), cold);
    }

    [Fact]
    public void UndoSplitRevertsRecordsAndStepsBack()
    {
        var store = new BestsStore();
        store.SetEntry(new MarkerKey(0, 0, Variant.Hot), new BestEntry(60_000, 3));
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnSplit(S(50));            // improves hot-0 best to 50s, attempts 4
        Assert.Equal(1, m.CurrentSegmentIndex);
        m.OnUndoSplit(S(55));
        Assert.Equal(0, m.CurrentSegmentIndex);
        Assert.True(store.TryGetEntry(new MarkerKey(0, 0, Variant.Hot), out var entry));
        Assert.Equal(60_000, entry.BestMs);   // prior best restored
        Assert.Equal(3, entry.Attempts);
    }

    [Fact]
    public void UndoSplitRemovesRecordsThatDidNotExistBefore()
    {
        var store = new BestsStore();
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnSplit(S(50));
        m.OnUndoSplit(S(55));
        Assert.False(store.TryGetEntry(new MarkerKey(0, 0, Variant.Hot), out _));
    }

    [Fact]
    public void SkipSplitRecordsNothingButAdvances()
    {
        var store = new BestsStore();
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnCheckpoint(S(20));
        m.OnSkipSplit(S(30));
        Assert.Equal(1, m.CurrentSegmentIndex);
        Assert.Empty(store.Keys);
    }

    [Fact]
    public void ResetDiscardsInFlightObservations()
    {
        var store = new BestsStore();
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnCheckpoint(S(20));
        m.OnReset();
        Assert.False(m.IsRunning);
        Assert.Empty(store.Keys);
        m.OnStart(S(0));
        Assert.Equal(0, m.CurrentSegmentIndex);
    }

    [Fact]
    public void EventsBeforeStartAreIgnored()
    {
        var store = new BestsStore();
        var m = new ReckoningModel(store);
        m.OnDeath();
        m.OnCheckpoint(S(5));
        m.OnSplit(S(10));
        Assert.False(m.IsRunning);
        Assert.Equal(0, m.CurrentSegmentIndex);
        Assert.Empty(store.Keys);
    }

    [Fact]
    public void ComputeUsesCurrentSegmentBests()
    {
        var store = new BestsStore();
        store.SetEntry(new MarkerKey(1, 1, Variant.Cold), new BestEntry(22_000, 2));
        var m = new ReckoningModel(store);
        m.OnStart(S(0));
        m.OnSplit(S(90));            // now in segment 1
        m.OnCheckpoint(S(110));
        m.OnDeath();
        var r = m.Compute(S(140), segmentStartElapsed: S(90),
            currentSegmentFullBest: S(30), remainingFullBestsSum: S(200));
        Assert.Equal(S(140 + 22 + 200), r.DrBpt);
        Assert.Equal(BestSource.ColdBest, r.Source);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter ReckoningModelTests`
Expected: FAIL — `ReckoningModel` not defined.

- [ ] **Step 3: Write the implementation**

`src/LiveSplit.Reckoning/Engine/ReckoningModel.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace LiveSplit.Reckoning.Engine;

/// <summary>Run-scoped orchestrator: maps timer lifecycle events onto the
/// tracker and store, and answers DR-BPT queries. Pure — the component shell
/// is the only place LiveSplit types appear.</summary>
public sealed class ReckoningModel
{
    private readonly BestsStore store;
    private readonly SegmentTracker tracker = new();
    // One journal frame per real split: (key, prior entry or null) per record,
    // so an undone split's records can be reverted exactly.
    private readonly Stack<List<(MarkerKey Key, BestEntry Prior)>> journal = new();

    public ReckoningModel(BestsStore store) => this.store = store;

    public int CurrentSegmentIndex { get; private set; }
    public bool IsRunning { get; private set; }
    public bool DiedThisSegment => tracker.DiedThisSegment;
    public int CurrentMarker => tracker.CurrentMarker;

    public void OnStart(TimeSpan elapsed)
    {
        CurrentSegmentIndex = 0;
        journal.Clear();
        IsRunning = true;
        tracker.StartSegment(elapsed);
    }

    public void OnDeath() { if (IsRunning) tracker.OnDeath(); }
    public void OnCheckpoint(TimeSpan elapsed) { if (IsRunning) tracker.OnCheckpoint(elapsed); }
    public void OnRespawn(TimeSpan elapsed) { if (IsRunning) tracker.OnRespawn(elapsed); }

    public void OnSplit(TimeSpan elapsed)
    {
        if (!IsRunning) return;
        var frame = new List<(MarkerKey, BestEntry)>();
        foreach (var obs in tracker.CompleteSegment(elapsed))
        {
            var key = new MarkerKey(CurrentSegmentIndex, obs.MarkerIndex, obs.Variant);
            store.TryGetEntry(key, out var prior);   // prior is null when absent
            frame.Add((key, prior));
            store.Record(CurrentSegmentIndex, obs.MarkerIndex, obs.Variant, obs.Duration);
        }
        journal.Push(frame);
        CurrentSegmentIndex++;
        tracker.StartSegment(elapsed);
    }

    public void OnUndoSplit(TimeSpan elapsed)
    {
        if (!IsRunning) return;
        if (journal.Count > 0)
        {
            foreach (var (key, prior) in journal.Pop())
            {
                if (prior is null) store.RemoveEntry(key);
                else store.SetEntry(key, prior);
            }
        }
        if (CurrentSegmentIndex > 0) CurrentSegmentIndex--;
        // Spec: the affected segment's marker state and in-flight observations
        // are discarded — we restart it at marker 0.
        tracker.Discard();
        tracker.StartSegment(elapsed);
    }

    public void OnSkipSplit(TimeSpan elapsed)
    {
        if (!IsRunning) return;
        tracker.Discard();                       // skip is not a real split: record nothing
        journal.Push(new List<(MarkerKey, BestEntry)>());   // keep journal aligned with undo depth
        CurrentSegmentIndex++;
        tracker.StartSegment(elapsed);
    }

    public void OnReset()
    {
        tracker.Discard();
        journal.Clear();
        IsRunning = false;
        CurrentSegmentIndex = 0;
    }

    public ReckoningResult Compute(
        TimeSpan elapsed,
        TimeSpan segmentStartElapsed,
        TimeSpan? currentSegmentFullBest,
        TimeSpan? remainingFullBestsSum)
    {
        int segment = CurrentSegmentIndex;
        return ReckoningCalculator.Compute(
            elapsed, segmentStartElapsed, currentSegmentFullBest, remainingFullBestsSum,
            tracker.DiedThisSegment, tracker.CurrentMarker,
            (marker, variant) => store.TryGetBest(segment, marker, variant, out var b) ? b : null);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter ReckoningModelTests`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/LiveSplit.Reckoning/Engine test/LiveSplit.Reckoning.Tests/ReckoningModelTests.cs
git commit -m "feat: ReckoningModel orchestration with undo-safe best recording"
```

---

### Task 6: Watcher layer — SmwEventDetector (death / checkpoint / respawn from WRAM)

**Files:**
- Create: `src/LiveSplit.Reckoning/Watchers/ISnesMemory.cs`, `src/LiveSplit.Reckoning/Watchers/SmwAddresses.cs`, `src/LiveSplit.Reckoning/Watchers/SmwEventDetector.cs`
- Test: `test/LiveSplit.Reckoning.Tests/FakeSnesMemory.cs`, `test/LiveSplit.Reckoning.Tests/SmwEventDetectorTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks (the watcher layer is independent of Engine).
- Produces (used by Task 9):
  - `internal interface ISnesMemory { bool IsAttached { get; } bool ReadWramByte(int wramOffset, out byte value); }` (same seam as SMWCounters)
  - `internal readonly record struct DetectorTick(bool Death, bool Checkpoint, bool Respawn)` with `static DetectorTick None => default`
  - `internal sealed class SmwEventDetector` with `DetectorTick Poll(ISnesMemory memory)` and `void Reset()`
  - `internal static class SmwAddresses` — named console-space offsets (below)
- Test infra produced: `internal sealed class FakeSnesMemory : ISnesMemory` with `bool Attached` and `void SetByte(int offset, byte value)` (used again in Task 9's wiring tests if needed).

**Detection contract to port from kaizosplits** (see Reference facts — every rule below is the proven Kaizo.asl behavior, not an invention):
- Reads per poll: `playerAnimation(0x0071)`, `gameMode(0x0100)`, `roomNum(0x010B)`, `levelNum(0x13BF)`, `midway(0x13CE)`, `levelStart(0x1935)`, `io(0x1DFB)`, `cpEntrance(0x1B403)`.
- **Death:** `playerAnimation` shifts TO `9`. Sets the internal `died` latch.
- **Respawn:** `gameMode` shifts TO `18` (prepare level) while `died` is set; clears `died`. (`gameMode`→18 with `died` clear is a fresh level entry — no event.)
- **Checkpoint:** either
  - midway tape: `midway` steps exactly `0→1` (`prev+1==curr && curr==1`), or
  - retry-hack entrance: in level (`levelStart==1`) and `cpEntrance` changed and the new value is not `firstRoom` (the entry-room guard);
  - both suppressed on a tick where a finish flag fired (`io` newly equals 3, 4, 7, or 8 vs. its last *non-zero* value — the io byte is transiently zeroed by P-switch/star music, so zero never updates the baseline).
- **firstRoom guard:** when `levelNum` changes, `firstRoom = roomNum` (current); after a real checkpoint fires, `firstRoom = 0`.
- **First poll / reattach:** no previous values ⇒ no edges, just baseline. Detached or any failed read ⇒ return `DetectorTick.None` and drop all previous values and latches (so a reattach can't false-edge). `Reset()` does the same (called on timer start and on emulator rebind).

- [ ] **Step 1: Write the fake and the failing tests**

`test/LiveSplit.Reckoning.Tests/FakeSnesMemory.cs`:

```csharp
using System.Collections.Generic;
using LiveSplit.Reckoning.Watchers;

namespace LiveSplit.Reckoning.Tests;

internal sealed class FakeSnesMemory : ISnesMemory
{
    private readonly Dictionary<int, byte> bytes = new();

    public bool Attached { get; set; } = true;
    public bool IsAttached => Attached;

    public void SetByte(int offset, byte value) => bytes[offset] = value;

    public bool ReadWramByte(int wramOffset, out byte value)
    {
        value = 0;
        if (!Attached) return false;
        bytes.TryGetValue(wramOffset, out value);
        return true;
    }
}
```

`test/LiveSplit.Reckoning.Tests/SmwEventDetectorTests.cs`:

```csharp
using LiveSplit.Reckoning.Watchers;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class SmwEventDetectorTests
{
    private readonly FakeSnesMemory mem = new();
    private readonly SmwEventDetector det = new();

    private DetectorTick Poll() => det.Poll(mem);

    private void EnterLevel(byte level = 5, byte room = 2)
    {
        mem.SetByte(SmwAddresses.LevelStart, 1);
        mem.SetByte(SmwAddresses.LevelNum, level);
        mem.SetByte(SmwAddresses.RoomNum, room);
        mem.SetByte(SmwAddresses.CpEntrance, room);
        Poll();   // baseline tick (also captures firstRoom via levelNum change)
    }

    public SmwEventDetectorTests()
    {
        Poll();       // very first poll: baseline only, all zeros
        EnterLevel();
    }

    [Fact]
    public void FirstPollEmitsNothing()
    {
        var fresh = new SmwEventDetector();
        mem.SetByte(SmwAddresses.PlayerAnimation, 9);   // already dead at attach
        Assert.Equal(DetectorTick.None, fresh.Poll(mem));
    }

    [Fact]
    public void AnimationShiftToNineIsDeath()
    {
        mem.SetByte(SmwAddresses.PlayerAnimation, 9);
        Assert.True(Poll().Death);
        // staying at 9 is not another death
        Assert.False(Poll().Death);
    }

    [Fact]
    public void PrepareLevelAfterDeathIsRespawn()
    {
        mem.SetByte(SmwAddresses.PlayerAnimation, 9);
        Poll();
        mem.SetByte(SmwAddresses.GameMode, 18);
        var tick = Poll();
        Assert.True(tick.Respawn);
        // died latch cleared: a second prepare-level without a death is not a respawn
        mem.SetByte(SmwAddresses.GameMode, 20);
        Poll();
        mem.SetByte(SmwAddresses.GameMode, 18);
        Assert.False(Poll().Respawn);
    }

    [Fact]
    public void PrepareLevelWithoutDeathIsNotRespawn()
    {
        mem.SetByte(SmwAddresses.GameMode, 18);
        Assert.False(Poll().Respawn);
    }

    [Fact]
    public void MidwayStepFiresCheckpoint()
    {
        mem.SetByte(SmwAddresses.Midway, 1);
        Assert.True(Poll().Checkpoint);
    }

    [Fact]
    public void MidwayJumpToOneFromGarbageDoesNotFire()
    {
        mem.SetByte(SmwAddresses.Midway, 3);
        Poll();
        mem.SetByte(SmwAddresses.Midway, 1);
        Assert.False(Poll().Checkpoint);   // StepTo requires exactly prev+1
    }

    [Fact]
    public void CpEntranceChangeInLevelFiresCheckpoint()
    {
        mem.SetByte(SmwAddresses.CpEntrance, 7);
        Assert.True(Poll().Checkpoint);
    }

    [Fact]
    public void CpEntranceRearmAfterLevelEntryIsSuppressed()
    {
        // Entering a new level with a stale cpEntrance (previous level's
        // checkpoint) that re-arms to the entry room a tick later: neither the
        // entry tick nor the lagged re-arm is a checkpoint touch.
        var fresh = new SmwEventDetector();
        var m2 = new FakeSnesMemory();
        m2.SetByte(SmwAddresses.LevelStart, 1);
        m2.SetByte(SmwAddresses.LevelNum, 5);
        m2.SetByte(SmwAddresses.RoomNum, 2);
        m2.SetByte(SmwAddresses.CpEntrance, 9);    // stale checkpoint from a previous level
        fresh.Poll(m2);                             // baseline
        m2.SetByte(SmwAddresses.LevelNum, 6);       // enter new level
        m2.SetByte(SmwAddresses.RoomNum, 11);
        Assert.False(fresh.Poll(m2).Checkpoint);    // entry tick: levelChanged suppresses
        m2.SetByte(SmwAddresses.CpEntrance, 11);    // re-arm lags one tick
        Assert.False(fresh.Poll(m2).Checkpoint);    // now equals firstRoom: suppressed
    }

    [Fact]
    public void CpEntranceGuardDisarmsAfterRealCheckpoint()
    {
        mem.SetByte(SmwAddresses.CpEntrance, 7);
        Poll();                                    // real CP -> firstRoom = 0
        mem.SetByte(SmwAddresses.CpEntrance, 2);   // back to the old entry room
        Assert.True(Poll().Checkpoint);            // guard no longer suppresses
    }

    [Fact]
    public void LevelEntryRearmDoesNotFireCheckpoint()
    {
        // Entering a new level changes levelNum/roomNum/cpEntrance on one tick;
        // that re-arm must not read as a checkpoint touch.
        mem.SetByte(SmwAddresses.LevelNum, 6);
        mem.SetByte(SmwAddresses.RoomNum, 11);
        mem.SetByte(SmwAddresses.CpEntrance, 11);
        Assert.False(Poll().Checkpoint);
    }

    [Fact]
    public void CpEntranceChangeOutsideLevelIsIgnored()
    {
        mem.SetByte(SmwAddresses.LevelStart, 0);
        Poll();
        mem.SetByte(SmwAddresses.CpEntrance, 7);
        Assert.False(Poll().Checkpoint);
    }

    [Fact]
    public void CheckpointSuppressedOnFinishFlagTick()
    {
        mem.SetByte(SmwAddresses.Midway, 1);
        mem.SetByte(SmwAddresses.Io, 4);   // goal fired same tick
        Assert.False(Poll().Checkpoint);
    }

    [Fact]
    public void IoZeroDoesNotResetFinishBaseline()
    {
        mem.SetByte(SmwAddresses.Io, 4);
        Poll();
        mem.SetByte(SmwAddresses.Io, 0);   // P-switch music transient
        Poll();
        mem.SetByte(SmwAddresses.Io, 4);   // back to the same value: no NEW finish
        mem.SetByte(SmwAddresses.Midway, 1);
        Assert.True(Poll().Checkpoint);    // not suppressed — 4 was already the baseline
    }

    [Fact]
    public void DetachDropsEdgesAndLatches()
    {
        mem.SetByte(SmwAddresses.PlayerAnimation, 9);
        Poll();                            // died latch set
        mem.Attached = false;
        Assert.Equal(DetectorTick.None, Poll());
        mem.Attached = true;
        mem.SetByte(SmwAddresses.GameMode, 18);
        Poll();                            // baseline-only tick after reattach
        Assert.False(Poll().Respawn);      // latch was dropped on detach
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter SmwEventDetectorTests`
Expected: FAIL — `SmwEventDetector`/`SmwAddresses`/`ISnesMemory` not defined.

- [ ] **Step 3: Write the implementation**

`src/LiveSplit.Reckoning/Watchers/ISnesMemory.cs`:

```csharp
namespace LiveSplit.Reckoning.Watchers;

/// <summary>The WRAM seam. Offsets are console-space ($7E0000 -> 0,
/// $7F0000 -> 0x10000), matching SNES.Emu.Read1.</summary>
internal interface ISnesMemory
{
    bool IsAttached { get; }
    bool ReadWramByte(int wramOffset, out byte value);
}
```

`src/LiveSplit.Reckoning/Watchers/SmwAddresses.cs`:

```csharp
namespace LiveSplit.Reckoning.Watchers;

/// <summary>Console-space WRAM offsets, mirrored from kaizosplits
/// Components/SMW/SMW/Memory.cs (the proven address set).</summary>
internal static class SmwAddresses
{
    public const int PlayerAnimation = 0x0071;   // $7E0071: 9 = death animation
    public const int GameMode = 0x0100;          // $7E0100: 18 = prepare level (spawn point)
    public const int RoomNum = 0x010B;           // $7E010B
    public const int LevelNum = 0x13BF;          // $7E13BF
    public const int Midway = 0x13CE;            // $7E13CE: steps 0->1 on midway tape
    public const int LevelStart = 0x1935;        // $7E1935: 1 = in level
    public const int Io = 0x1DFB;                // $7E1DFB: 3=orb 4=goal 7=key 8=fadeout
    public const int CpEntrance = 0x1B403;       // $7FB403: retry-hack respawn entrance

    // Value meanings (named so no magic numbers appear in logic):
    public const byte DeathAnimation = 9;
    public const byte GameModePrepareLevel = 18;
    public const byte InLevel = 1;
}
```

`src/LiveSplit.Reckoning/Watchers/SmwEventDetector.cs`:

```csharp
namespace LiveSplit.Reckoning.Watchers;

internal readonly record struct DetectorTick(bool Death, bool Checkpoint, bool Respawn)
{
    public static DetectorTick None => default;
}

/// <summary>Turns per-tick WRAM reads into death/checkpoint/respawn events,
/// porting kaizosplits' Watchers.cs semantics (see plan Reference facts).</summary>
internal sealed class SmwEventDetector
{
    private struct Snapshot
    {
        public byte PlayerAnimation, GameMode, RoomNum, LevelNum, Midway, LevelStart, Io, CpEntrance;
    }

    private bool hasPrev;
    private Snapshot prev;
    private bool died;          // set on death animation, cleared on respawn
    private byte firstRoom;     // cpEntrance guard: level entry room, 0 once disarmed
    private byte lastNonZeroIo; // io finish baseline; io transiently zeroes on P-switch/star music

    public void Reset()
    {
        hasPrev = false;
        died = false;
        firstRoom = 0;
        lastNonZeroIo = 0;
    }

    public DetectorTick Poll(ISnesMemory memory)
    {
        if (!memory.IsAttached || !TryRead(memory, out var cur))
        {
            Reset();   // never edge across a gap in visibility
            return DetectorTick.None;
        }

        if (!hasPrev)
        {
            Baseline(cur);
            return DetectorTick.None;
        }

        bool death = prev.PlayerAnimation != SmwAddresses.DeathAnimation
                  && cur.PlayerAnimation == SmwAddresses.DeathAnimation;
        if (death) died = true;

        bool respawn = false;
        bool toPrepareLevel = prev.GameMode != SmwAddresses.GameModePrepareLevel
                           && cur.GameMode == SmwAddresses.GameModePrepareLevel;
        if (toPrepareLevel && died)
        {
            respawn = true;
            died = false;
        }

        // Finish flags fire against the last non-zero io value.
        bool finishFired = cur.Io is 3 or 4 or 7 or 8 && cur.Io != lastNonZeroIo;

        // Level transition: capture the entry room BEFORE evaluating checkpoint
        // logic — on the entry tick levelNum/roomNum/cpEntrance all change
        // together, and treating the cpEntrance re-arm as a checkpoint would
        // false-fire (kaizosplits Watchers.cs:198-202 ordering).
        bool levelChanged = cur.LevelNum != prev.LevelNum;
        if (levelChanged) firstRoom = cur.RoomNum;

        bool inLevel = cur.LevelStart == SmwAddresses.InLevel;
        bool midwayStep = cur.Midway == 1 && prev.Midway + 1 == cur.Midway;   // StepTo: exact 0->1
        bool cpEntranceChange = inLevel && !levelChanged
            && cur.CpEntrance != prev.CpEntrance
            && cur.CpEntrance != firstRoom;   // re-arm to the entry room is not a checkpoint
        bool checkpoint = (midwayStep || cpEntranceChange) && !finishFired;
        if (checkpoint) firstRoom = 0;   // real CP disarms the entry-room guard

        if (cur.Io != 0) lastNonZeroIo = cur.Io;
        prev = cur;
        return new DetectorTick(death, checkpoint, respawn);
    }

    private void Baseline(Snapshot cur)
    {
        prev = cur;
        hasPrev = true;
        firstRoom = cur.RoomNum;
        if (cur.Io != 0) lastNonZeroIo = cur.Io;
    }

    private static bool TryRead(ISnesMemory m, out Snapshot s)
    {
        s = default;
        return m.ReadWramByte(SmwAddresses.PlayerAnimation, out s.PlayerAnimation)
            && m.ReadWramByte(SmwAddresses.GameMode, out s.GameMode)
            && m.ReadWramByte(SmwAddresses.RoomNum, out s.RoomNum)
            && m.ReadWramByte(SmwAddresses.LevelNum, out s.LevelNum)
            && m.ReadWramByte(SmwAddresses.Midway, out s.Midway)
            && m.ReadWramByte(SmwAddresses.LevelStart, out s.LevelStart)
            && m.ReadWramByte(SmwAddresses.Io, out s.Io)
            && m.ReadWramByte(SmwAddresses.CpEntrance, out s.CpEntrance);
    }
}
```

Note for the implementer: the test constructor's `EnterLevel` polls once with `levelNum` changed from 0→5, which routes through the `cur.LevelNum != prev.LevelNum` branch and captures `firstRoom = 2`. The baseline tick itself also seeds `firstRoom` for the component-loaded-mid-level case.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter SmwEventDetectorTests`
Expected: PASS (14 tests).

- [ ] **Step 5: Commit**

```bash
git add src/LiveSplit.Reckoning/Watchers test/LiveSplit.Reckoning.Tests/FakeSnesMemory.cs test/LiveSplit.Reckoning.Tests/SmwEventDetectorTests.cs
git commit -m "feat: SmwEventDetector porting kaizosplits death/checkpoint/respawn semantics"
```

---

### Task 7: SNES bridge — SnesConnection, EmulatorProcessFinder, StatusDot

**Files:**
- Create: `src/LiveSplit.Reckoning/Snes/SnesConnection.cs`, `src/LiveSplit.Reckoning/Snes/EmulatorProcessFinder.cs`, `src/LiveSplit.Reckoning/Snes/StatusDot.cs`
- Test: `test/LiveSplit.Reckoning.Tests/StatusDotTests.cs`

**Interfaces:**
- Consumes: `ISnesMemory` (Task 6); `SNES.Emu`/`EmuStatus` from the pinned SNES.dll.
- Produces (used by Task 9):
  - `internal sealed class SnesConnection : ISnesMemory` with `void Tick()`, `bool IsAttached { get; }`, `System.Drawing.Color DotColor { get; }`, `int Generation { get; }` (exposed so the component can `detector.Reset()` on rebind), `bool ReadWramByte(int, out byte)`
  - `internal static class EmulatorProcessFinder` with `static Process Find()`
  - `internal static class StatusDot` with `static Color ColorFor(string stateName, bool isCoolingDown)`

This is a near-verbatim port of SMWCounters' proven bridge (its `Snes/` folder) — the status-first idiom, 1000 ms reacquire throttle, generation watch, and state→color mapping are all pinned there. Keep `StatusDot` free of SNES.dll types (string state names) so it stays CI-testable.

- [ ] **Step 1: Write the failing StatusDot tests**

`test/LiveSplit.Reckoning.Tests/StatusDotTests.cs`:

```csharp
using System.Drawing;
using LiveSplit.Reckoning.Snes;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class StatusDotTests
{
    // State names are raw strings on purpose: they pin the SNES.dll wire
    // contract (EmuState names) independently of the assembly.
    [Theory]
    [InlineData("Resolved", false, 0x39, 0x8F, 0xE5)]   // blue
    [InlineData("Held", false, 0x9B, 0x59, 0xB6)]        // purple
    [InlineData("Degraded", false, 0x2E, 0xCC, 0x40)]    // green
    [InlineData("Detached", false, 0xE5, 0x3E, 0x3E)]    // red
    [InlineData("Searching", false, 0xFF, 0xDC, 0x00)]   // yellow
    [InlineData("Discovering", false, 0xFF, 0xDC, 0x00)] // yellow
    [InlineData("Searching", true, 0x9A, 0x9A, 0x9A)]    // gray while cooling down
    [InlineData("NoContent", false, 0xFF, 0x85, 0x1B)]   // orange
    [InlineData("NoContent", true, 0x9A, 0x9A, 0x9A)]    // gray while cooling down
    [InlineData("SomethingNew", false, 0xFF, 0xDC, 0x00)]// unknown state -> yellow
    public void MapsStateToColor(string state, bool coolingDown, int r, int g, int b)
    {
        Assert.Equal(Color.FromArgb(r, g, b), StatusDot.ColorFor(state, coolingDown));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter StatusDotTests`
Expected: FAIL — `StatusDot` not defined.

- [ ] **Step 3: Write the implementation**

`src/LiveSplit.Reckoning/Snes/StatusDot.cs`:

```csharp
using System.Drawing;

namespace LiveSplit.Reckoning.Snes;

/// <summary>Pure EmuState-name -> dot color mapping (SMWCounters pattern).
/// String-keyed so this file has no SNES.dll dependency.</summary>
internal static class StatusDot
{
    private static readonly Color Blue = Color.FromArgb(0x39, 0x8F, 0xE5);    // resolved
    private static readonly Color Purple = Color.FromArgb(0x9B, 0x59, 0xB6);  // held (paused)
    private static readonly Color Green = Color.FromArgb(0x2E, 0xCC, 0x40);   // degraded (working)
    private static readonly Color Yellow = Color.FromArgb(0xFF, 0xDC, 0x00);  // searching
    private static readonly Color Orange = Color.FromArgb(0xFF, 0x85, 0x1B);  // attached, no content
    private static readonly Color Gray = Color.FromArgb(0x9A, 0x9A, 0x9A);    // retry cooldown
    private static readonly Color Red = Color.FromArgb(0xE5, 0x3E, 0x3E);     // detached

    public static Color ColorFor(string stateName, bool isCoolingDown) => stateName switch
    {
        "Resolved" => Blue,
        "Held" => Purple,
        "Degraded" => Green,
        "Detached" => Red,
        "Searching" or "Discovering" => isCoolingDown ? Gray : Yellow,
        "NoContent" => isCoolingDown ? Gray : Orange,
        _ => Yellow,
    };
}
```

`src/LiveSplit.Reckoning/Snes/EmulatorProcessFinder.cs`:

```csharp
using System.Diagnostics;

namespace LiveSplit.Reckoning.Snes;

/// <summary>Ordered process scan mirroring the kaizosplits autosplitter's
/// emulator list (same order, same names).</summary>
internal static class EmulatorProcessFinder
{
    private static readonly string[] Names =
    {
        "snes9x", "snes9x-x64", "bsnes", "retroarch", "higan",
        "snes9x-rr", "mesen", "emuhawk", "ares", "mednafen",
    };

    public static Process Find()
    {
        foreach (var name in Names)
        {
            Process winner = null;
            foreach (var p in Process.GetProcessesByName(name))
            {
                if (winner == null && !p.HasExited) winner = p;
                else p.Dispose();
            }
            if (winner != null) return winner;
        }
        return null;
    }
}
```

`src/LiveSplit.Reckoning/Snes/SnesConnection.cs` (SMWCounters' status-first idiom):

```csharp
using System;
using System.Diagnostics;
using System.Drawing;
using LiveSplit.Reckoning.Watchers;
using SNES;

namespace LiveSplit.Reckoning.Snes;

internal sealed class SnesConnection : ISnesMemory
{
    // Reacquire at most once a second: process scans are not free, and the
    // 15 ms poll tick must stay cheap when no emulator is running.
    private const int AcquireIntervalMs = 1000;

    private readonly Emu emu = new();
    private Process process;
    private bool ready;
    private int lastGeneration;
    private long lastAcquireTick;

    public EmuStatus Status { get; private set; }

    public bool IsAttached => ready && process != null && !process.HasExited;

    public int Generation => emu.Generation;

    public Color DotColor
    {
        get
        {
            var s = Status;
            return s == null
                ? StatusDot.ColorFor("Detached", false)
                : StatusDot.ColorFor(s.StateName, s.IsCoolingDown);
        }
    }

    public void Tick()
    {
        if (process != null && process.HasExited)
        {
            process.Dispose();
            process = null;
            ready = false;
        }

        if (process == null)
        {
            long now = Environment.TickCount64;
            if (now - lastAcquireTick >= AcquireIntervalMs)
            {
                lastAcquireTick = now;
                process = EmulatorProcessFinder.Find();
                if (process != null)
                {
                    emu.Attach(process);
                    ready = false;
                }
            }
        }

        bool wasReady = ready;
        if (ready && emu.Generation != lastGeneration) ready = false;   // rebind: re-baseline
        try { emu.Ready(); } catch { ready = false; }                   // the throw IS "not ready"
        if (!ready && !wasReady && process != null)
        {
            // Skipped for one tick right after a drop so IsAttached reads false
            // exactly once and the detector flushes its edge state.
            try { emu.GetOffset(); ready = true; lastGeneration = emu.Generation; } catch { }
        }

        Status = emu.Status();
    }

    public bool ReadWramByte(int wramOffset, out byte value)
    {
        value = 0;
        if (!IsAttached) return false;
        try { value = emu.Read1(wramOffset); return true; }
        catch { return false; }
    }
}
```

Note: `Environment.TickCount64` exists on net481 via PolySharp? It does **not** — it is a .NET Core API. Use `Environment.TickCount` with unchecked subtraction instead (`int now = Environment.TickCount; if (unchecked(now - lastAcquireTickInt) >= AcquireIntervalMs)` with `int lastAcquireTickInt` — wrap-safe because the difference is what's compared). The implementer should make that substitution; the test suite doesn't exercise the throttle.

- [ ] **Step 4: Run tests and full build**

Run: `dotnet build Reckoning.sln -c Debug && dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter StatusDotTests`
Expected: build succeeds (SnesConnection compiles against SNES.dll), 10 StatusDot facts pass.

- [ ] **Step 5: Commit**

```bash
git add src/LiveSplit.Reckoning/Snes test/LiveSplit.Reckoning.Tests/StatusDotTests.cs
git commit -m "feat: SNES.dll bridge with status-first connection and status dot"
```

---

### Task 8: Persistence — sidecar JSON (atomic, corruption-safe)

**Files:**
- Create: `src/LiveSplit.Reckoning/Persistence/SidecarStore.cs`
- Test: `test/LiveSplit.Reckoning.Tests/SidecarStoreTests.cs`

**Interfaces:**
- Consumes: `BestsStore`, `MarkerKey`, `BestEntry`, `Variant` (Task 2).
- Produces (used by Task 9):
  - `internal static class SidecarStore` with:
    - `static string PathFor(string lssPath)` → `lssPath + ".reckoning.json"`
    - `static BestsStore Load(string sidecarPath)` — missing, unreadable, or corrupt file → empty store (never throws)
    - `static void Save(string sidecarPath, BestsStore store, string lssPath, string game, string category, IReadOnlyList<string> segmentNames)` — atomic write-temp-then-rename

**Schema v1** (spec §Persistence: per segment → per marker → per variant → `{bestMs, attempts}`; identity = lss path with run name/category fallback):

```json
{
  "version": 1,
  "lss": "C:\\splits\\grandpoobear.lss",
  "game": "SMW Kaizo",
  "category": "Any%",
  "segments": [
    { "index": 0, "name": "Yump 1",
      "markers": [
        { "marker": 0, "variant": "hot",  "bestMs": 51230, "attempts": 12 },
        { "marker": 1, "variant": "cold", "bestMs": 22050, "attempts": 4 }
      ] }
  ]
}
```

Serialization uses `System.Web.Script.Serialization.JavaScriptSerializer` (in-box on net481 via the `System.Web.Extensions` reference added in Task 1 — zero shipped dependencies, unlike System.Text.Json/Newtonsoft which would drop extra DLLs into LiveSplit's Components dir). Serialize from `Dictionary<string, object>` / `List<object>`; deserialize with `DeserializeObject` and defensive casts — any cast/shape failure is "corrupt" and yields an empty store.

- [ ] **Step 1: Write the failing tests**

`test/LiveSplit.Reckoning.Tests/SidecarStoreTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using LiveSplit.Reckoning.Engine;
using LiveSplit.Reckoning.Persistence;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class SidecarStoreTests : IDisposable
{
    private readonly string dir = Path.Combine(Path.GetTempPath(), "reckoning-tests-" + Guid.NewGuid().ToString("N"));

    public SidecarStoreTests() => Directory.CreateDirectory(dir);
    public void Dispose() => Directory.Delete(dir, true);

    private string SidecarPath => Path.Combine(dir, "run.lss.reckoning.json");

    private static readonly string[] SegmentNames = { "Yump 1", "Yump 2" };

    private static BestsStore SampleStore()
    {
        var store = new BestsStore();
        store.SetEntry(new MarkerKey(0, 0, Variant.Hot), new BestEntry(51_230, 12));
        store.SetEntry(new MarkerKey(0, 1, Variant.Cold), new BestEntry(22_050, 4));
        store.SetEntry(new MarkerKey(1, 0, Variant.Cold), new BestEntry(63_000, 1));
        return store;
    }

    [Fact]
    public void PathForAppendsSuffix()
    {
        Assert.Equal(@"C:\s\run.lss.reckoning.json", SidecarStore.PathFor(@"C:\s\run.lss"));
    }

    [Fact]
    public void RoundTripPreservesEveryEntry()
    {
        SidecarStore.Save(SidecarPath, SampleStore(), @"C:\s\run.lss", "SMW Kaizo", "Any%", SegmentNames);
        var loaded = SidecarStore.Load(SidecarPath);
        Assert.Equal(3, loaded.Keys.Count);
        Assert.True(loaded.TryGetEntry(new MarkerKey(0, 0, Variant.Hot), out var e));
        Assert.Equal(new BestEntry(51_230, 12), e);
        Assert.True(loaded.TryGetEntry(new MarkerKey(0, 1, Variant.Cold), out e));
        Assert.Equal(new BestEntry(22_050, 4), e);
        Assert.True(loaded.TryGetEntry(new MarkerKey(1, 0, Variant.Cold), out e));
        Assert.Equal(new BestEntry(63_000, 1), e);
    }

    [Fact]
    public void MissingFileLoadsEmpty()
    {
        var loaded = SidecarStore.Load(SidecarPath);
        Assert.Empty(loaded.Keys);
    }

    [Fact]
    public void CorruptJsonLoadsEmpty()
    {
        File.WriteAllText(SidecarPath, "{ this is not json");
        Assert.Empty(SidecarStore.Load(SidecarPath).Keys);
    }

    [Fact]
    public void WrongShapeLoadsEmpty()
    {
        File.WriteAllText(SidecarPath, "{ \"version\": 1, \"segments\": \"nope\" }");
        Assert.Empty(SidecarStore.Load(SidecarPath).Keys);
    }

    [Fact]
    public void UnknownVariantEntriesAreSkippedNotFatal()
    {
        SidecarStore.Save(SidecarPath, SampleStore(), @"C:\s\run.lss", "g", "c", SegmentNames);
        var text = File.ReadAllText(SidecarPath).Replace("\"cold\"", "\"tepid\"");
        File.WriteAllText(SidecarPath, text);
        var loaded = SidecarStore.Load(SidecarPath);
        Assert.True(loaded.TryGetEntry(new MarkerKey(0, 0, Variant.Hot), out _));   // hot survives
        Assert.False(loaded.TryGetEntry(new MarkerKey(0, 1, Variant.Cold), out _)); // tepid skipped
    }

    [Fact]
    public void SaveOverwritesAtomicallyLeavingNoTempFile()
    {
        SidecarStore.Save(SidecarPath, SampleStore(), @"C:\s\run.lss", "g", "c", SegmentNames);
        SidecarStore.Save(SidecarPath, SampleStore(), @"C:\s\run.lss", "g", "c", SegmentNames);
        Assert.Single(Directory.GetFiles(dir));   // no stray .tmp
        Assert.NotEmpty(SidecarStore.Load(SidecarPath).Keys);
    }

    [Fact]
    public void SaveRecordsIdentityAndSegmentNames()
    {
        SidecarStore.Save(SidecarPath, SampleStore(), @"C:\s\run.lss", "SMW Kaizo", "Any%", SegmentNames);
        var text = File.ReadAllText(SidecarPath);
        Assert.Contains("\"SMW Kaizo\"", text);
        Assert.Contains("\"Any%\"", text);
        Assert.Contains("\"Yump 2\"", text);
        Assert.Contains("\"version\":1", text.Replace(" ", ""));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter SidecarStoreTests`
Expected: FAIL — `SidecarStore` not defined.

- [ ] **Step 3: Write the implementation**

`src/LiveSplit.Reckoning/Persistence/SidecarStore.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using LiveSplit.Reckoning.Engine;

namespace LiveSplit.Reckoning.Persistence;

/// <summary>Sidecar JSON next to the splits file. Learned data is precious but
/// replaceable: any load problem degrades to an empty (unlearned) store and a
/// fresh save rebuilds the file — never crash the component over it.</summary>
internal static class SidecarStore
{
    private const int SchemaVersion = 1;
    private const string Suffix = ".reckoning.json";

    public static string PathFor(string lssPath) => lssPath + Suffix;

    public static BestsStore Load(string sidecarPath)
    {
        var store = new BestsStore();
        try
        {
            if (!File.Exists(sidecarPath)) return store;
            var root = new JavaScriptSerializer().DeserializeObject(File.ReadAllText(sidecarPath))
                as Dictionary<string, object>;
            if (root == null || !(root.TryGetValue("segments", out var segsObj) && segsObj is object[] segs))
                return store;
            foreach (var segObj in segs)
            {
                if (segObj is not Dictionary<string, object> seg) continue;
                if (!(seg.TryGetValue("index", out var idxObj) && idxObj is int segIndex)) continue;
                if (!(seg.TryGetValue("markers", out var marksObj) && marksObj is object[] marks)) continue;
                foreach (var markObj in marks)
                {
                    if (markObj is not Dictionary<string, object> mark) continue;
                    if (!(mark.TryGetValue("marker", out var mObj) && mObj is int marker)) continue;
                    if (!(mark.TryGetValue("bestMs", out var bObj) && bObj is int or long)) continue;
                    if (!(mark.TryGetValue("attempts", out var aObj) && aObj is int attempts)) continue;
                    Variant variant;
                    switch (mark.TryGetValue("variant", out var vObj) ? vObj as string : null)
                    {
                        case "hot": variant = Variant.Hot; break;
                        case "cold": variant = Variant.Cold; break;
                        default: continue;   // unknown variant: skip entry, keep the rest
                    }
                    store.SetEntry(new MarkerKey(segIndex, marker, variant),
                        new BestEntry(Convert.ToInt64(bObj), attempts));
                }
            }
        }
        catch
        {
            // Corrupt sidecar: degrade to unlearned (spec §Persistence).
            return new BestsStore();
        }
        return store;
    }

    public static void Save(string sidecarPath, BestsStore store, string lssPath,
        string game, string category, IReadOnlyList<string> segmentNames)
    {
        var segments = new List<object>();
        foreach (var group in store.Keys.GroupBy(k => k.SegmentIndex).OrderBy(g => g.Key))
        {
            var markers = new List<object>();
            foreach (var key in group.OrderBy(k => k.MarkerIndex).ThenBy(k => k.Variant))
            {
                store.TryGetEntry(key, out var entry);
                markers.Add(new Dictionary<string, object>
                {
                    ["marker"] = key.MarkerIndex,
                    ["variant"] = key.Variant == Variant.Hot ? "hot" : "cold",
                    ["bestMs"] = entry.BestMs,
                    ["attempts"] = entry.Attempts,
                });
            }
            segments.Add(new Dictionary<string, object>
            {
                ["index"] = group.Key,
                ["name"] = group.Key < (segmentNames?.Count ?? 0) ? segmentNames[group.Key] : "",
                ["markers"] = markers,
            });
        }
        var root = new Dictionary<string, object>
        {
            ["version"] = SchemaVersion,
            ["lss"] = lssPath ?? "",
            ["game"] = game ?? "",
            ["category"] = category ?? "",
            ["segments"] = segments,
        };
        string json = new JavaScriptSerializer().Serialize(root);

        // Atomic write: temp file in the same directory, then swap.
        string tmp = sidecarPath + ".tmp";
        File.WriteAllText(tmp, json);
        if (File.Exists(sidecarPath)) File.Replace(tmp, sidecarPath, null);
        else File.Move(tmp, sidecarPath);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter SidecarStoreTests`
Expected: PASS (8 tests). If `JavaScriptSerializer` deserializes integers as `int` vs `long` differently than the casts assume, adjust the defensive casts (`is int or long` + `Convert.ToInt64`) — the tests pin the observable behavior either way.

- [ ] **Step 5: Commit**

```bash
git add src/LiveSplit.Reckoning/Persistence test/LiveSplit.Reckoning.Tests/SidecarStoreTests.cs
git commit -m "feat: atomic corruption-safe sidecar JSON persistence"
```

---

### Task 9: Component shell — factory, settings, rendering, wiring

**Files:**
- Create: `src/LiveSplit.Reckoning/UI/TimeText.cs`, `src/LiveSplit.Reckoning/UI/Components/ReckoningComponentFactory.cs`, `src/LiveSplit.Reckoning/UI/Components/ReckoningComponentSettings.cs`, `src/LiveSplit.Reckoning/UI/Components/ReckoningComponent.cs`
- Test: `test/LiveSplit.Reckoning.Tests/TimeTextTests.cs`, `test/LiveSplit.Reckoning.Tests/ReckoningSettingsTests.cs`

**Interfaces:**
- Consumes: `ReckoningModel`, `BestsStore`, `ReckoningResult`, `BestSource` (Tasks 2–5); `SmwEventDetector`, `DetectorTick` (Task 6); `SnesConnection`, `StatusDot` (Task 7); `SidecarStore` (Task 8); LiveSplit.Core (`IComponent`, `IComponentFactory`, `LiveSplitState`, `SimpleLabel`, `GraphicsCache`, `SettingsHelper`, `TimerPhase`, `LayoutMode`).
- Produces: the shippable component. Factory `ComponentName = "Reckoning"`.
  - `internal enum RowAccuracy { Seconds, Tenths, Hundredths }` (in `LiveSplit.Reckoning.UI`)
  - `internal static class TimeText` with `static string Format(TimeSpan? time, RowAccuracy accuracy)` and `static string FormatSunk(TimeSpan? sunk, RowAccuracy accuracy)`
  - `class ReckoningComponentSettings : UserControl` (namespace `LiveSplit.UI.Components`) with `bool ShowSunkRow` (default true), `bool ShowStatusDot` (default true), `RowAccuracy Accuracy` (default Tenths), `XmlNode GetSettings(XmlDocument)`, `void SetSettings(XmlNode)`, `int GetSettingsHashCode()`

**Display decisions locked in** (resolving the spec's open settings/flag questions for v1):
- Row 1 label "Reckoning" (DR-BPT value), row 2 label "Sunk" (toggleable). Missing data renders an em dash `—`.
- Unlearned flag: the value text renders at half opacity. `UnlearnedValueAlpha = 128` — the midpoint between invisible and full: clearly distinguishable at a glance yet still legible against both light and dark layouts.
- Sunk formats with a leading `+` when positive (`+3.4`), plain when zero.
- Settings v1: `ShowSunkRow`, `ShowStatusDot`, `Accuracy` — nothing else.

- [ ] **Step 1: Write the failing TimeText tests**

`test/LiveSplit.Reckoning.Tests/TimeTextTests.cs`:

```csharp
using System;
using LiveSplit.Reckoning.UI;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class TimeTextTests
{
    [Fact]
    public void NullRendersEmDash()
    {
        Assert.Equal("—", TimeText.Format(null, RowAccuracy.Seconds));
        Assert.Equal("—", TimeText.FormatSunk(null, RowAccuracy.Tenths));
    }

    [Theory]
    [InlineData(83.0, RowAccuracy.Seconds, "1:23")]
    [InlineData(83.46, RowAccuracy.Tenths, "1:23.4")]
    [InlineData(83.46, RowAccuracy.Hundredths, "1:23.46")]
    [InlineData(3723.0, RowAccuracy.Seconds, "1:02:03")]
    [InlineData(3723.5, RowAccuracy.Tenths, "1:02:03.5")]
    [InlineData(7.25, RowAccuracy.Hundredths, "0:07.25")]
    public void FormatsMinutesSecondsHours(double seconds, RowAccuracy acc, string expected)
    {
        Assert.Equal(expected, TimeText.Format(TimeSpan.FromSeconds(seconds), acc));
    }

    [Theory]
    [InlineData(3.42, RowAccuracy.Tenths, "+0:03.4")]
    [InlineData(0.0, RowAccuracy.Tenths, "0:00.0")]
    [InlineData(75.0, RowAccuracy.Seconds, "+1:15")]
    public void SunkGetsPlusPrefixWhenPositive(double seconds, RowAccuracy acc, string expected)
    {
        Assert.Equal(expected, TimeText.FormatSunk(TimeSpan.FromSeconds(seconds), acc));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter TimeTextTests`
Expected: FAIL — `TimeText` not defined.

- [ ] **Step 3: Implement TimeText**

`src/LiveSplit.Reckoning/UI/TimeText.cs`:

```csharp
using System;

namespace LiveSplit.Reckoning.UI;

internal enum RowAccuracy
{
    Seconds,
    Tenths,
    Hundredths,
}

/// <summary>Deterministic time formatting for the two rows. Local rather than
/// LiveSplit's TimeFormatters so the exact strings are unit-pinned.</summary>
internal static class TimeText
{
    private const string NoValue = "—";   // em dash: data unavailable

    public static string Format(TimeSpan? time, RowAccuracy accuracy)
    {
        if (time is not TimeSpan t) return NoValue;
        string frac = accuracy switch
        {
            RowAccuracy.Tenths => "." + (t.Milliseconds / 100),
            RowAccuracy.Hundredths => "." + (t.Milliseconds / 10).ToString("00"),
            _ => "",
        };
        long totalMinutes = (long)t.TotalMinutes;
        return t.TotalHours >= 1
            ? $"{(long)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}{frac}"
            : $"{totalMinutes}:{t.Seconds:00}{frac}";
    }

    public static string FormatSunk(TimeSpan? sunk, RowAccuracy accuracy)
    {
        if (sunk is not TimeSpan s) return NoValue;
        string body = Format(s.Duration(), accuracy);
        return s > TimeSpan.Zero ? "+" + body : s < TimeSpan.Zero ? "-" + body : body;
    }
}
```

- [ ] **Step 4: Run TimeText tests**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter TimeTextTests`
Expected: PASS (10 cases).

- [ ] **Step 5: Write the failing settings tests**

`test/LiveSplit.Reckoning.Tests/ReckoningSettingsTests.cs`:

```csharp
using System.Xml;
using LiveSplit.Reckoning.UI;
using LiveSplit.UI.Components;
using Xunit;

namespace LiveSplit.Reckoning.Tests;

public class ReckoningSettingsTests
{
    [Fact]
    public void DefaultsAreSpecCompliant()
    {
        using var s = new ReckoningComponentSettings();
        Assert.True(s.ShowSunkRow);
        Assert.True(s.ShowStatusDot);
        Assert.Equal(RowAccuracy.Tenths, s.Accuracy);
    }

    [Fact]
    public void SettingsRoundTripThroughXml()
    {
        using var a = new ReckoningComponentSettings
        {
            ShowSunkRow = false,
            ShowStatusDot = false,
            Accuracy = RowAccuracy.Hundredths,
        };
        var doc = new XmlDocument();
        var node = a.GetSettings(doc);

        using var b = new ReckoningComponentSettings();
        b.SetSettings(node);
        Assert.False(b.ShowSunkRow);
        Assert.False(b.ShowStatusDot);
        Assert.Equal(RowAccuracy.Hundredths, b.Accuracy);
    }

    [Fact]
    public void GarbageAccuracyFallsBackToDefault()
    {
        using var a = new ReckoningComponentSettings();
        var doc = new XmlDocument();
        var node = a.GetSettings(doc);
        node.SelectSingleNode("Accuracy").InnerText = "Nanoseconds";
        using var b = new ReckoningComponentSettings();
        b.SetSettings(node);
        Assert.Equal(RowAccuracy.Tenths, b.Accuracy);
    }

    [Fact]
    public void HashChangesWhenASettingChanges()
    {
        using var a = new ReckoningComponentSettings();
        int before = a.GetSettingsHashCode();
        a.ShowSunkRow = false;
        Assert.NotEqual(before, a.GetSettingsHashCode());
    }
}
```

- [ ] **Step 6: Run settings tests to verify they fail, then implement the settings control**

Run: `dotnet test test/LiveSplit.Reckoning.Tests -c Debug --filter ReckoningSettingsTests` → FAIL (type missing).

`src/LiveSplit.Reckoning/UI/Components/ReckoningComponentSettings.cs`:

```csharp
using System;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Reckoning.UI;
using LiveSplit.UI;

namespace LiveSplit.UI.Components;

public class ReckoningComponentSettings : UserControl
{
    private readonly CheckBox sunkBox;
    private readonly CheckBox dotBox;
    private readonly ComboBox accuracyBox;

    public bool ShowSunkRow { get; set; } = true;
    public bool ShowStatusDot { get; set; } = true;
    internal RowAccuracy Accuracy { get; set; } = RowAccuracy.Tenths;

    public ReckoningComponentSettings()
    {
        sunkBox = new CheckBox { Text = "Show Sunk row", AutoSize = true, Left = 8, Top = 8 };
        dotBox = new CheckBox { Text = "Show connection status dot", AutoSize = true, Left = 8, Top = 34 };
        accuracyBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Left = 8, Top = 60, Width = 140 };
        accuracyBox.Items.AddRange(new object[] { "Seconds", "Tenths", "Hundredths" });
        Controls.Add(sunkBox);
        Controls.Add(dotBox);
        Controls.Add(accuracyBox);
        Load += (_, _) => ModelToView();
        sunkBox.CheckedChanged += (_, _) => ShowSunkRow = sunkBox.Checked;
        dotBox.CheckedChanged += (_, _) => ShowStatusDot = dotBox.Checked;
        accuracyBox.SelectedIndexChanged += (_, _) => Accuracy = (RowAccuracy)accuracyBox.SelectedIndex;
    }

    private void ModelToView()
    {
        sunkBox.Checked = ShowSunkRow;
        dotBox.Checked = ShowStatusDot;
        accuracyBox.SelectedIndex = (int)Accuracy;
    }

    public XmlNode GetSettings(XmlDocument document)
    {
        var parent = document.CreateElement("Settings");
        SettingsHelper.CreateSetting(document, parent, "Version", "1");
        SettingsHelper.CreateSetting(document, parent, "ShowSunkRow", ShowSunkRow);
        SettingsHelper.CreateSetting(document, parent, "ShowStatusDot", ShowStatusDot);
        SettingsHelper.CreateSetting(document, parent, "Accuracy", Accuracy.ToString());
        return parent;
    }

    public void SetSettings(XmlNode settings)
    {
        ShowSunkRow = SettingsHelper.ParseBool(settings["ShowSunkRow"], true);
        ShowStatusDot = SettingsHelper.ParseBool(settings["ShowStatusDot"], true);
        Accuracy = Enum.TryParse(SettingsHelper.ParseString(settings["Accuracy"], "Tenths"), out RowAccuracy acc)
            ? acc : RowAccuracy.Tenths;
        if (IsHandleCreated) ModelToView();
    }

    public int GetSettingsHashCode()
    {
        // 397: standard odd prime for hash folding (matches SMWCounters).
        int hash = ShowSunkRow.GetHashCode();
        hash = hash * 397 ^ ShowStatusDot.GetHashCode();
        hash = hash * 397 ^ Accuracy.GetHashCode();
        return hash;
    }
}
```

Run the filter again → PASS (4 tests).

- [ ] **Step 7: Implement factory and component (no new unit tests — the logic they wrap is already pinned; live verification happens in Task 10)**

`src/LiveSplit.Reckoning/UI/Components/ReckoningComponentFactory.cs`:

```csharp
using System;
using LiveSplit.Model;
using LiveSplit.UI.Components;

[assembly: ComponentFactory(typeof(ReckoningComponentFactory))]

namespace LiveSplit.UI.Components;

public class ReckoningComponentFactory : IComponentFactory
{
    public string ComponentName => "Reckoning";
    public string Description => "Death-aware Best Possible Time for SMW kaizo: what finish is actually still possible from where death left you.";
    public ComponentCategory Category => ComponentCategory.Information;
    public IComponent Create(LiveSplitState state) => new ReckoningComponent(state);
    public string UpdateName => ComponentName;
    public string XMLURL => string.Empty;
    public string UpdateURL => string.Empty;
    public Version Version => Version.Parse("0.1.0");
}
```

`src/LiveSplit.Reckoning/UI/Components/ReckoningComponent.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using LiveSplit.Model;
using LiveSplit.Reckoning.Engine;
using LiveSplit.Reckoning.Persistence;
using LiveSplit.Reckoning.Snes;
using LiveSplit.Reckoning.UI;
using LiveSplit.Reckoning.Watchers;
using LiveSplit.UI;

namespace LiveSplit.UI.Components;

public class ReckoningComponent : IComponent
{
    // 15 ms poll (SMWCounters cadence): under one 60 fps frame, so no
    // death/checkpoint edge can slip between polls.
    private const int PollIntervalMs = 15;
    // Half opacity for unlearned values: clearly dimmed, still legible on
    // light and dark layouts (spec: subtle visual flag).
    private const int UnlearnedValueAlpha = 128;
    // Matches LiveSplit's InfoTextComponent default row height.
    private const float RowHeightPx = 25f;
    private const float StatusDotSizePx = 5f;

    private readonly LiveSplitState state;
    private readonly SnesConnection connection = new();
    private readonly SmwEventDetector detector = new();
    private readonly Timer pollTimer;
    private readonly GraphicsCache cache = new();
    private readonly SimpleLabel[] nameLabels = { new(), new() };
    private readonly SimpleLabel[] valueLabels = { new(), new() };

    private BestsStore store = new();
    private ReckoningModel model;
    private string loadedLssPath;
    private int lastGeneration = -1;
    private ReckoningResult lastResult = new(null, null, false, BestSource.StandardBpt);

    public ReckoningComponentSettings Settings { get; } = new();

    public ReckoningComponent(LiveSplitState state)
    {
        this.state = state;
        model = new ReckoningModel(store);
        state.OnStart += OnStart;
        state.OnSplit += OnSplit;
        state.OnUndoSplit += OnUndoSplit;
        state.OnSkipSplit += OnSkipSplit;
        state.OnReset += OnReset;
        pollTimer = new Timer { Interval = PollIntervalMs };
        pollTimer.Tick += (_, _) => Poll();
        pollTimer.Enabled = true;
    }

    public string ComponentName => "Reckoning";
    public float VerticalHeight => Settings.ShowSunkRow ? RowHeightPx * 2 : RowHeightPx;
    public float MinimumHeight => VerticalHeight;
    public float HorizontalWidth => 220f;
    public float MinimumWidth => 120f;
    public float PaddingTop => 0f;
    public float PaddingBottom => 0f;
    public float PaddingLeft => 7f;
    public float PaddingRight => 7f;
    public IDictionary<string, Action> ContextMenuControls => null;

    private TimeSpan? Elapsed() => state.CurrentTime[state.CurrentTimingMethod];

    private void Poll()
    {
        connection.Tick();
        if (connection.Generation != lastGeneration)
        {
            lastGeneration = connection.Generation;
            detector.Reset();   // rebind: never edge across a base change
        }

        bool timerActive = state.CurrentPhase == TimerPhase.Running || state.CurrentPhase == TimerPhase.Paused;
        var tick = detector.Poll(connection);
        if (!timerActive || !model.IsRunning) return;
        if (Elapsed() is not TimeSpan elapsed) return;

        if (tick.Death) model.OnDeath();
        if (tick.Checkpoint) model.OnCheckpoint(elapsed);
        if (tick.Respawn) model.OnRespawn(elapsed);
    }

    private void ReloadSidecarIfPathChanged()
    {
        string lss = state.Run.FilePath;
        if (lss == loadedLssPath) return;
        loadedLssPath = lss;
        store = string.IsNullOrEmpty(lss) ? new BestsStore() : SidecarStore.Load(SidecarStore.PathFor(lss));
        model = new ReckoningModel(store);
    }

    private void SaveSidecar()
    {
        string lss = loadedLssPath;
        if (string.IsNullOrEmpty(lss)) return;
        try
        {
            SidecarStore.Save(SidecarStore.PathFor(lss), store, lss,
                state.Run.GameName, state.Run.CategoryName,
                state.Run.Select(seg => seg.Name).ToList());
        }
        catch
        {
            // A failed save must never take down LiveSplit; next split retries.
        }
    }

    private void OnStart(object sender, EventArgs e)
    {
        detector.Reset();
        model.OnStart(Elapsed() ?? TimeSpan.Zero);
    }

    private void OnSplit(object sender, EventArgs e)
    {
        if (Elapsed() is TimeSpan t) model.OnSplit(t);
        SaveSidecar();
    }

    private void OnUndoSplit(object sender, EventArgs e) => model.OnUndoSplit(Elapsed() ?? TimeSpan.Zero);
    private void OnSkipSplit(object sender, EventArgs e) => model.OnSkipSplit(Elapsed() ?? TimeSpan.Zero);
    private void OnReset(object sender, TimerPhase phase) => model.OnReset();

    private ReckoningResult ComputeNow()
    {
        if (!model.IsRunning || state.CurrentSplitIndex < 0 || Elapsed() is not TimeSpan elapsed)
            return new ReckoningResult(null, null, false, BestSource.StandardBpt);

        var method = state.CurrentTimingMethod;
        int index = state.CurrentSplitIndex;

        // Segment start = last non-null earlier split time (skips leave nulls).
        TimeSpan segmentStart = TimeSpan.Zero;
        for (int i = index - 1; i >= 0; i--)
        {
            if (state.Run[i].SplitTime[method] is TimeSpan st) { segmentStart = st; break; }
        }

        TimeSpan? fullBest = state.Run[index].BestSegmentTime[method];
        TimeSpan? remaining = TimeSpan.Zero;
        for (int i = index + 1; i < state.Run.Count; i++)
        {
            if (state.Run[i].BestSegmentTime[method] is TimeSpan b) remaining += b;
            else { remaining = null; break; }
        }

        return model.Compute(elapsed, segmentStart, fullBest, remaining);
    }

    public void Update(IInvalidator invalidator, LiveSplitState state, float width, float height, LayoutMode mode)
    {
        ReloadSidecarIfPathChanged();
        lastResult = ComputeNow();

        cache.Restart();
        cache["reckoning"] = TimeText.Format(lastResult.DrBpt, Settings.Accuracy);
        cache["sunk"] = TimeText.FormatSunk(lastResult.Sunk, Settings.Accuracy);
        cache["unlearned"] = lastResult.Unlearned;
        cache["sunkRow"] = Settings.ShowSunkRow;
        cache["dot"] = Settings.ShowStatusDot ? connection.DotColor.ToArgb() : 0;
        if (cache.HasChanged) invalidator?.Invalidate(0, 0, width, height);
    }

    public void DrawVertical(Graphics g, LiveSplitState state, float width, Region clipRegion) =>
        DrawGeneral(g, state, width, VerticalHeight);

    public void DrawHorizontal(Graphics g, LiveSplitState state, float height, Region clipRegion) =>
        DrawGeneral(g, state, HorizontalWidth, height);

    private void DrawGeneral(Graphics g, LiveSplitState state, float width, float height)
    {
        var textColor = state.LayoutSettings.TextColor;
        var valueColor = lastResult.Unlearned
            ? Color.FromArgb(UnlearnedValueAlpha, textColor)
            : textColor;
        int rows = Settings.ShowSunkRow ? 2 : 1;
        float rowHeight = height / rows;

        DrawRow(g, state, 0, rowHeight, width, "Reckoning",
            TimeText.Format(lastResult.DrBpt, Settings.Accuracy), textColor, valueColor);
        if (Settings.ShowSunkRow)
        {
            DrawRow(g, state, 1, rowHeight, width, "Sunk",
                TimeText.FormatSunk(lastResult.Sunk, Settings.Accuracy), textColor, valueColor);
        }

        if (Settings.ShowStatusDot)
        {
            using var dotBrush = new SolidBrush(connection.DotColor);
            g.FillRectangle(dotBrush, 3f, (height - StatusDotSizePx) / 2f, StatusDotSizePx, StatusDotSizePx);
        }
    }

    private void DrawRow(Graphics g, LiveSplitState state, int row, float rowHeight, float width,
        string name, string value, Color nameColor, Color valueColor)
    {
        float y = row * rowHeight;
        var font = state.LayoutSettings.TextFont;
        var nameLabel = nameLabels[row];
        nameLabel.Text = name;
        nameLabel.Font = font;
        nameLabel.ForeColor = nameColor;
        nameLabel.HorizontalAlignment = StringAlignment.Near;
        nameLabel.VerticalAlignment = StringAlignment.Center;
        nameLabel.X = PaddingLeft + StatusDotSizePx;
        nameLabel.Y = y;
        nameLabel.Width = width / 2;
        nameLabel.Height = rowHeight;
        nameLabel.Draw(g);

        var valueLabel = valueLabels[row];
        valueLabel.Text = value;
        valueLabel.Font = state.LayoutSettings.TimesFont;
        valueLabel.ForeColor = valueColor;
        valueLabel.HorizontalAlignment = StringAlignment.Far;
        valueLabel.VerticalAlignment = StringAlignment.Center;
        valueLabel.X = width / 2;
        valueLabel.Y = y;
        valueLabel.Width = width / 2 - PaddingRight;
        valueLabel.Height = rowHeight;
        valueLabel.Draw(g);
    }

    public Control GetSettingsControl(LayoutMode mode) => Settings;
    public XmlNode GetSettings(XmlDocument document) => Settings.GetSettings(document);
    public void SetSettings(XmlNode settings) => Settings.SetSettings(settings);
    public int GetSettingsHashCode() => Settings.GetSettingsHashCode();

    public void Dispose()
    {
        SaveSidecar();   // spec: persist on LiveSplit shutdown too
        pollTimer.Dispose();
        state.OnStart -= OnStart;
        state.OnSplit -= OnSplit;
        state.OnUndoSplit -= OnUndoSplit;
        state.OnSkipSplit -= OnSkipSplit;
        state.OnReset -= OnReset;
    }
}
```

Implementer notes:
- LiveSplit event delegate types vary (`EventHandler` vs `EventHandlerT<TimerPhase>` for `OnReset` — SMWCounters' handler is `(object sender, TimerPhase phase)`); match whatever `LiveSplitState` declares, the compiler will tell you.
- `state.Run[i].SplitTime[method]`/`BestSegmentTime[method]` return `TimeSpan?` via LiveSplit's `Time` indexer.
- If `SimpleLabel`/`GraphicsCache`/`SettingsHelper` member names differ from this sketch, mirror SMWCounters `SmwCountersComponent.cs` — it is the working reference for all of them.

- [ ] **Step 8: Full build and test run**

Run: `dotnet build Reckoning.sln -c Debug && dotnet test test/LiveSplit.Reckoning.Tests -c Debug`
Expected: build succeeds, all tests pass (~60).

- [ ] **Step 9: Commit**

```bash
git add src/LiveSplit.Reckoning/UI test/LiveSplit.Reckoning.Tests/TimeTextTests.cs test/LiveSplit.Reckoning.Tests/ReckoningSettingsTests.cs
git commit -m "feat: Reckoning component shell with two-row display and status dot"
```

---

### Task 10: Release packaging, README, wrap-up

**Files:**
- Create: `.github/workflows/release.yml`, `README.md`

**Interfaces:**
- Consumes: the built solution (all prior tasks).
- Produces: tag-triggered GitHub release with `Reckoning-<version>.zip` containing `Reckoning.dll` + `SNES.dll` + `README.md`.

- [ ] **Step 1: Write the release workflow** (SMWCounters' proven shape)

`.github/workflows/release.yml`:

```yaml
name: release
on:
  push:
    tags: ["v*.*.*"]
  workflow_dispatch:
    inputs:
      version:
        description: "Version (e.g. 0.1.0)"
        required: true

permissions:
  contents: write

jobs:
  release:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 9.0.x
      - name: Resolve version
        id: ver
        shell: pwsh
        run: |
          $v = if ("${{ github.event_name }}" -eq "push") { "${{ github.ref_name }}" } else { "${{ inputs.version }}" }
          "version=$($v -replace '^v','')" >> $env:GITHUB_OUTPUT
      - name: Fetch LiveSplit.Core
        shell: pwsh
        run: pwsh -File scripts/fetch-livesplit-core.ps1
      - name: Build
        run: dotnet build src/LiveSplit.Reckoning/LiveSplit.Reckoning.csproj -c Release -p:Version=${{ steps.ver.outputs.version }}
      - name: Test
        run: dotnet test test/LiveSplit.Reckoning.Tests -c Release
      - name: Stage
        shell: pwsh
        run: |
          $staging = "release-staging"
          New-Item -ItemType Directory $staging | Out-Null
          $dll = Get-ChildItem -Recurse artifacts -Filter Reckoning.dll | Select-Object -First 1
          if (-not $dll) { throw "Reckoning.dll not found" }
          $snes = Get-ChildItem -Recurse artifacts -Filter SNES.dll | Select-Object -First 1
          if (-not $snes) { throw "SNES.dll missing from build output" }
          Copy-Item $dll.FullName, $snes.FullName, "README.md" $staging
          Compress-Archive -Path "$staging\*" -DestinationPath "Reckoning-${{ steps.ver.outputs.version }}.zip"
      - name: Publish release
        if: github.event_name == 'push'
        uses: softprops/action-gh-release@v2
        with:
          body: |
            Copy **both** `Reckoning.dll` and `SNES.dll` into `LiveSplit/Components/`,
            then add the "Reckoning" component (Information category) to your layout.
          files: |
            Reckoning-${{ steps.ver.outputs.version }}.zip
      - name: Upload artifact (manual run)
        if: github.event_name == 'workflow_dispatch'
        uses: actions/upload-artifact@v4
        with:
          name: Reckoning-${{ steps.ver.outputs.version }}
          path: Reckoning-${{ steps.ver.outputs.version }}.zip
```

- [ ] **Step 2: Write README.md**

Content requirements (write real prose, ~40 lines): what Reckoning is (death-aware BPT, one paragraph from the spec overview); the two rows and what Sunk means; install instructions (both DLLs into `LiveSplit/Components/`); supported emulators (the ten process names); the sidecar file (`<splits>.reckoning.json` — safe to delete, relearns); note that bests are learned per splits file per marker per hot/cold variant; status dot color legend (blue resolved / yellow searching / red detached / purple held / green degraded / orange no content / gray cooldown); building from source (`scripts/fetch-livesplit-core.ps1` then `dotnet build`). No LICENSE file exists yet — leave licensing to Andrew rather than inventing one (the release zip therefore ships without it; add a line to the PR description flagging this).

- [ ] **Step 3: Full verification**

Run: `dotnet build Reckoning.sln -c Release && dotnet test test/LiveSplit.Reckoning.Tests -c Release`
Expected: clean build, all tests pass. Verify `artifacts` contains `Reckoning.dll` with `SNES.dll` beside it.

- [ ] **Step 4: Commit and hand off**

```bash
git add .github README.md
git commit -m "build: release workflow and README"
git push -u origin feature/dr-bpt
```

Do not merge — Andrew reviews the diff against main and merges himself. Optional manual smoke test for Andrew: create `src/LiveSplit.Reckoning/Reckoning.local.props` with `<Project><PropertyGroup><ComponentsPath>C:\Apps\LiveSplit\Components</ComponentsPath></PropertyGroup></Project>`, rebuild, and add the component to a layout with an emulator running.

---

## Self-Review

**Spec coverage:**
- Core calculation (DR-BPT + Sunk, timing-method basis, LiveSplit best segments) → Tasks 4, 9. ✓
- Marker model (ordered markers, multi-checkpoint, order-identity, respawn-at-marker, undo/skip/reset discard) → Tasks 3, 5. ✓
- Hot/cold (separate bests, marker-0 variants, cold→hot→standard fallback with unlearned flag) → Tasks 2, 4; display flag Task 9. ✓
- Detection seam (SNES.dll untouched, own watcher layer on kaizosplits semantics, LiveSplit state reads) → Tasks 6, 7, 9. ✓
- Persistence (sidecar path, schema with attempts, real-split-only recording, atomic write on split + shutdown, corrupt→unlearned) → Tasks 5, 8, 9. ✓
- Display (two rows, BPT styling, unlearned flag, status pixel) → Task 9. ✓
- Architecture (SMWCounters layout, three seams) → Task 1 + namespace discipline. ✓
- Testing (red-green everywhere, contract-style watcher tests, no magic numbers) → every task. ✓
- Out of scope respected: no segments-model export, no probabilistic BPT, no manual death input.
- Open questions resolved in-plan: WRAM signals (mined from Kaizo.asl — Reference facts); v1 settings surface (three settings, Task 9); unlearned flag treatment (half-opacity value). Marker-identity-across-skipped-checkpoints is accepted as-is for v1 per spec's order-within-segment decision; if a route skips a checkpoint some attempts, those attempts learn under different marker indices — the fallback chain degrades gracefully. Noted for the future `segments` model work.

**Placeholder scan:** no TBD/TODO-later steps; the two "implementer notes" (LiveSplit delegate types, `Environment.TickCount64` → `TickCount`) are compile-time-checkable substitutions with the working reference named, not deferred design.

**Type consistency:** `BestsStore.SetEntry/TryGetEntry/RemoveEntry/Keys` used by Tasks 5, 8; `SegmentTracker.CompleteSegment` returns `IReadOnlyList<Observation>` consumed in Task 5; `ReckoningModel.Compute(TimeSpan, TimeSpan, TimeSpan?, TimeSpan?)` matches Task 9's call; `DetectorTick(Death, Checkpoint, Respawn)` matches Task 9's usage; `StatusDot.ColorFor(string, bool)` matches Tasks 7 and 9. Verified consistent.

## Amendments (final review)

Three design-level corrections discovered by the final whole-branch review, applied in the fix wave (Andrew: these amend the spec's literal formulas — review before merge):

1. **Anchored post-death term.** The spec's `DR-BPT = elapsed + best(marker→exit)` is only valid at the instant the situation is entered; implemented as `max(situationArrival + best, elapsed)` so the estimate holds steady during post-respawn play instead of ramping 1s/s.
2. **Variant follows the tracked situation.** After death → respawn → next checkpoint reached alive, the runner is Hot at that marker; the calculator now prices the tracked (marker, variant) with the other variant then standard BPT as fallback, rather than hard-preferring Cold whenever a death occurred this segment.
3. **Unanchored resume after undo/skip.** Restarting the tracker at undo/skip time opened a marker-0 observation with a mid-segment anchor, which recorded impossibly fast bests. Undo/skip now resume tracking with no marker-0 observation; only anchored arrivals (checkpoints, respawns) record for that segment.

