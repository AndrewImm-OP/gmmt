# GMMT: Undertale native Linux feasibility — development and testing handoff

## Objective and authorization
User requested on 2026-10-04 (Europe/Ulyanovsk): download an Undertale mod (suggested Red & Yellow), locate installed Steam game, conduct tests autonomously, and maintain an exceptionally detailed handoff. Work is authorized on local test copies. Do not modify the installed Steam game or real saves. No redistribution of game archives is authorized.

## Current status
Investigation started. No successful mod launch claimed yet. Update this document after every meaningful experiment. Raw evidence lives in `experiments/2026-10-04/`.

## Locations
- Project: `/run/media/andrew/Expansion/.AI DISK/projects/Suspended/gmmt [SUSPENDED+]`
- Steam Undertale: `/home/andrew/.local/share/Steam/steamapps/common/Undertale`
- Steam app ID: 391540
- Official suggested mod: https://gamejolt.com/games/undertale-red-yellow/877387 (Shinix)

## Critical pre-existing state
The project has extensive uncommitted modifications, deletions and untracked files from prior development. Preserve all; do not reset, clean, revert, or stage indiscriminately. The Undertale installation is ALSO already modified: active assets/game.unx is 104056916 bytes; game.unx.vanilla and gameog.unx are both 62960358 bytes; gmmt-test-full.unx is 103713236 bytes; backup-before-gmmt-roomfix exists. Names do not prove provenance; hash and inspect them before choosing baseline.

## Initial environment
Available: dotnet, xdelta3, wine, 7z, unzip, curl, spectacle, xdotool, timeout. Not found: grim, scrot, ydotool, xwininfo. Native computer APIs in cua are disabled. Explore shell-based screenshots/input if supported; do not claim visual verification from process survival alone.

## Working hypotheses
H1: Native Linux GMS1 runner cannot execute engine-specific GMS2 semantics merely by inserting symbol names.
H2: Running Windows GMS2 data under a compatible Linux GMS2 runtime could avoid downgrading, but runtime match and portability MUST be tested, not assumed.
H3: Transfer of restricted mod edits into native Linux baseline may work even if full bytecode transplantation does not.

## Existing code observations
- CompatChecker blocks differing bytecode and major/minor engine versions.
- CodeTransplanter clones many instruction fields and remaps references; it is not a complete GMS2→GMS1 semantic translator.
- __argument_relative reads become constant zero.
- Unresolved positive instruction instance targets can fall back to Self.
- Existing root-code replacement can import Windows baseline engine adaptations together with mod changes.
- Missing function list is heuristic; matching bytecode version does NOT guarantee matching engine generation.

## Evidence standard
Record command, working directory, input hash/version, environment differences, output path, exit status, stderr, screenshots, observation, interpretation, confidence, and next experiment. Distinguish: archive parses / runner starts / title screen visible / interactive gameplay / mod behavior verified. A timeout with alive process is not a passed gameplay test.

## Test sequence
1. Inventory, hashes, clean baseline identification and existing mod discovery.
2. Obtain official patch and inspect its contents before applying.
3. Build/execute inspection tooling without altering old code.
4. Baseline Linux launch in an isolated copy and isolated save environment.
5. Reconstruct modded Windows archive using required vanilla Windows input if available.
6. Inspect exact engine and bytecode versions; compare resources and missing features.
7. Test runtime compatibility or smallest transplant; collect root cause before fixing.

## Activity log
### 2026-10-04 — initial discovery
- Found installed Steam Undertale and prior GMMT test artifacts inside assets.
- Found extensive pre-existing modifications to GMMT.
- Web search located official Red & Yellow page; web fetch of SSR page returned 403. A download has not yet succeeded.

## Next-agent checklist
Read this document and raw logs first. Check Latest results / Open issues below. Preserve installed game and private saves. Avoid treating earlier Gemini claims (95% runner swap success, BC16==GMS2, instance_create_depth polyfill for GMS1) as established facts.

## Latest results
- Native baseline renders intro with Steam scout selected library paths.
- Official Linux UTRY 2.1.4 reaches interactive Flowey encounter (mod-interactive-04.png).
- Windows and Linux patch reconstructions are byte-identical. Separate Windows-data case renders title and name-entry screens and accepts keys; full gameplay in that case not yet verified.
- GMMT CLI builds; classifier runs. No GMS2→GMS1 code changes made.
- Source snapshot 461392d is uploaded to private GitHub repository; later controlled-runtime updates recorded below.

## Open issues
- Continue Windows-data + Linux-runner control beyond name-entry screen.
- Obtain and test a different Windows-only mod; UTRY alone cannot establish generality.
- Complete broader gameplay checks; initial encounter is not full-game validation.
- Verify setup from a fresh clone; local CLI build and dependency patch checks passed.
- Determine donor runtime compatibility limits and eventual distribution permissions.

### Official Linux package discovery (major result)
Browser on official GameJolt page selected Linux build 1977016, version 2.1.4, 104 MB. Download saved at `/home/andrew/Загрузки/utry-2.1.4-linux.zip` and copied into experiments/downloads. SHA256: 117a4386a95ac3e49d65d1dccc5d01ab151122e00d5e68d91a9f38ac2e25c4c1. Validated ZIP paths before extraction. Package includes Linux i386 ELF runner (5254272 bytes), patch.xdelta, extra media, instructions and installer. Did NOT run installer: it copies/modifies files and expects manual Steam library configuration.

Native Linux baseline MD5 e996649a751e5dc5182b8416168042b5 and cached Windows baseline MD5 5903fc5cb042a728d4ad8ee9e949c6eb match documented Undertale 1.08. Both header BC16 and version [1,0,0,1539]; header is not the full human-readable IDE version. Existing Windows UTRY xdelta and freshly downloaded Linux UTRY patch both apply successfully with xdelta checksum validation. BOTH reconstruct EXACTLY the same mod archive: MD5 0e9f93ff7f41390b4ee31fba50983fbd, BC16, engine header [2,0,0,0]. This is decisive evidence that an official runner replacement approach exists for this mod. Runtime execution not verified yet.

Package instructions require Steam Linux Runtime 1.0 (scout), removing copied libsteam_api.so in test copy, and Steam launch; save directory UNDERTALERY. System xdelta3 was used rather than executing bundled xdelta3. Original Steam directory remains unchanged.

### Build tooling
Initial dotnet build failed writing first-use sentinel in read-only /home/andrew/.dotnet. Retried with DOTNET_CLI_HOME pointing into experiment directory; then missing cached NuGet dependency PropertyChanged.Fody. Explicit restore with --packages experiments/2026-10-04/nuget-packages completed. NuGet HTTP cache reports invalid argument because filesystem rejects cache directory names containing colon; package restore itself completed. Build retry in progress. No source fixes yet.

### Additional user request
User requested creation of GitHub project repository if absent. Check existing repo first; do not publish game data, downloaded mod media, private saves, NuGet packages or binaries. Repository visibility not specified; choose private for initial development unless existing repo indicates otherwise.

### Runtime/library experiments
Native baseline first launch without scout exited 127: missing libopenal.so.1. Loading all scout i386 paths directly caused baseline and mod to exit 139 without useful logs. This is an ENVIRONMENTAL result, not archive incompatibility: host-preferred library selection matters. Retried using Steam's `steam-runtime/run.sh --print-steam-runtime-library-paths` output. Native baseline then produced OpenGL logs and visible Undertale intro (screenshot baseline-selected.png). Official Linux mod likewise rendered a visible window (official-linux-first.png) showing centered AAA text on gray background with window title `First we Under, then we Tale. Let's UNDERTALE!`. Need interact and establish if this is expected first-run room; not yet a gameplay pass. The first input attempt missed the 55-second timeout, so no keyboard input was sent. Screenshot official-linux-after-input.png is NOT valid game evidence (game already exited).

### GMMT build and classifier
`dotnet build ... --no-restore -m:1` succeeded (843 warnings, 0 errors); parallel build failed with no diagnostic errors in sandbox. Existing code compiled without edits. Classifier reconstructed and parsed mod, detected GMS2 2.0.6.0 vs GMS1 baseline; 2959 new code entries, 1139 modified, 1046 new sprites, 114 objects, 267 scripts, 23 rooms. 215 changed code entries reference recognized GMS2-only operations (241 view-internal refs, 100 layer refs, 16 camera refs, 7 joystick refs). See classification.json and classification.log. This is a large mod and not a suitable trivial full-transplant baseline.

### GitHub
Created PRIVATE empty repository https://github.com/AndrewImm-OP/gmmt and configured origin manually. Initial gh command with --source . was rejected by automatic reviewer (possible unreviewed payload); safer empty-repo command succeeded. Added ignore rules for experiments, game archives and dumps. No files pushed yet.

### Interactive control and corrected interpretation
Official Linux UTRY was launched for 180 seconds under the selected scout library paths. Window title changes during startup; searching only the initial title misses the window. Searching `Undertale Red & Yellow`, activating exactly one matched window, and sending Return/Z reached Flowey dialogue and the heart/HUD battle screen. `logs/mod-interactive-04.png` is valid visual evidence. Centered AAA on earlier startup screenshots was transient; it is not evidence of a confirmed rendering bug. `mod-interactive-01.png` and `mod-interactive-02.png` show the desktop/app after a missed or expired launch and must not be used as game evidence. Saves were created only inside `runs/official-linux-config/UNDERTALERY`.

### User steering: Windows mods remain the goal
User clarified that UTRY Linux did not exist when development began, and many mods are Windows-only. Official package file dates are June 2026; this is package evidence, not a verified first release date. Use Linux UTRY as a runtime/control donor, not as proof that GMMT is unnecessary. A separate case `runs/windows-data-linux-runner` copies the official Linux control package and replaces only assets/game.unx with the archive reconstructed from the old Windows UTRY patch. This tests Windows archive execution with Linux supporting assets; it does not yet test assembling all supporting media solely from the Windows package. Next required test is a different Windows-only mod.

### Windows-data control: completed initial smoke test
Command, project working directory: `python scripts/testing/run_native_probe.py experiments/2026-10-04/runs/windows-data-linux-runner --scout-selected --seconds 180`. Private config overlay: runs/windows-data-linux-runner-config. Input reconstructed Windows archive MD5 0e9f93ff7f41390b4ee31fba50983fbd, matching official Linux control. Launch timestamp 1791091926.3375695. Program remained alive until intentional 180-second timeout; termination exit -15 is harness shutdown, not an observed spontaneous crash.

Two X11 windows matched broad title: main 640x480, and a 480x240 helper `Undertale Red & Yellow: Settling Liquids`. First input attempt aborted safely because match was ambiguous. Exact regex `^Undertale Red & Yellow$` selected one main window ID33554435. Window title changes again later; a confirmed main ID can be reused only within that same live launch. Keys Return/Z produced the Undertale title screen (`windows-data-interactive-01.png`) and name entry (`windows-data-interactive-02.png`, name AAA); both screenshots viewed and confirmed. This is a passed startup and input smoke test, NOT a separate Flowey/full-game pass. Official Linux control had already reached Flowey, using byte-identical game data.

### Additional mod download attempt
Official source selected: https://www.moddb.com/mods/undertale-together/downloads/undertale-together10 . File Undertale_Together.38.zip, 9846774 bytes; published MD5 32cef5b68b0e5a3446a1a596503ff3b2. Page includes a Linux community tag; do not claim Windows-only until actual package and instructions inspected. Browser download timed out/reset after an unexpectedly long wait. `/home/andrew/Загрузки/Не подтвержден 301699.crdownload` was only 6291456 bytes at inspection. Do not extract/test partial data; validate completed size/hash first. No second-mod execution performed.

## Reproduction commands and restart guidance
Run from the project directory. Installations and saves are left unchanged; experiment copies are local ignored artifacts.

```sh
# Existing working-tree CLI builds (dependencies already restored locally).
DOTNET_CLI_HOME="$PWD/experiments/2026-10-04/dotnet-home" DOTNET_CLI_TELEMETRY_OPTOUT=1 dotnet build src/Gmmt.Cli/Gmmt.Cli.csproj --no-restore -m:1
# Archive header/hash inspector; inspect --help/source for argument details.
python scripts/testing/inspect_gen8.py --help
# Interactive native control, isolated saves, known-good library order.
python scripts/testing/run_native_probe.py experiments/2026-10-04/runs/official-linux --scout-selected --seconds 180
# Separate case whose archive originates from Windows xdelta.
python scripts/testing/run_native_probe.py experiments/2026-10-04/runs/windows-data-linux-runner --scout-selected --seconds 180
```

Historical caution: initial probe overwrote case-name logs. The current probe uses unique UTC attempt names; older results retain their original names. Screenshots are uniquely named. All experiment files are excluded from GitHub; no game archives, runners, media, saves, or downloaded patches should be committed.

## Technical conclusion at this checkpoint
UTRY supplies a real compatible native GMS2 runner and identical Windows/Linux game data. Therefore Windows-origin GameMaker data executing under Linux is technically demonstrated for this specific archive at startup/input level. This does not establish GMS2→GMS1 lowering, runtime universality, portability of Windows DLL extensions or distribution rights for a donor runner. Existing GMMT transplantation remains insufficient for general engine downgrade. Further work should measure runner matching and extension requirements with additional mods before committing to a universal converter design.

### 2026-10-04 — resumed testing, second mod
Undertale Together official package downloaded successfully using European mirror DBolical EU #3 in the browser. Completed ZIP 9846774 bytes, MD5 32cef5b68b0e5a3446a1a596503ff3b2 exactly matches official page. Validated ZIP paths and extracted locally. Package contains SpaghettiInstaller.exe, UndertaleTogether.xdelta, GOG-to-Steam normalization patch, optional OGG/splash files and Windows installation instructions; no native Linux runner or separate Linux patch in this ZIP. Installer not executed. The page has a community Linux tag; existence/absence of a separate port elsewhere has NOT been established.

Applied UndertaleTogether.xdelta with system xdelta3 to cached Windows Steam 1.08 baseline, with normal checksum validation. Output together-windows.win: 64310594 bytes, MD5 778e043668cbb623dfe3350d00cbe8cf, SHA256 e50dae1388f1016864e5b9be0e5bf7bd4e1e36850aa25ac2b6c1c60ed9c23381, BC16, engine header [1,0,0,1539]. This is GMS1-family control, not a GMS2-downconversion test.

Case runs/together-windows-native: original Steam native-baseline runner; assets/game.unx is unchanged Windows mod archive; original Linux external media plus optional media from Together ZIP; no third-party installer or new runner used. Fresh private config overlay. Symlink preparation failed: exFAT filesystem rejects symlinks. Preparation script accidentally continued into launch on incomplete case (attempt 20261004T055814265900Z, exit1); disregard as archive compatibility evidence. Replaced symlinks with actual media copies, completed preparation, then reran known-good scout library selection, attempt 20261004T055907190205Z. Valid screenshot together-first.png shows mod-specific `First human:` name-entry UI. User independently observed the mod working. Subsequent screenshot captured another active app after user changed focus; deleted that generated non-game screenshot and do not treat it as evidence. Pause keyboard automation while user is using desktop. No full-game/network or two-player movement check yet.

Classifier executed on Together patch with original Windows baseline and native target; results in metadata/together-classification.json and logs/together-classification.log.

### Reproducible source snapshot preparation
Added .gitmodules for existing pinned UndertaleModTool gitlink; captured only real .csproj framework edits as patches, excluding exFAT-induced executable-bit noise. Nested Underanalyzer revision remains 94cc220ee9f738d6a79d1a46563ab1caa54ad243. setup.sh now checks .NET10, initializes dependencies, applies framework patches with forward/reverse checks, builds CLI sequentially. Added README with truthful limitations and no invented source license. Native probe now names logs with unique UTC attempt timestamps to preserve retries. Static shell/Python checks passed; reverse patch checks match existing dependency edits. Reviewed 77 candidate files (578011 bytes at initial review) plus original Git history: no archive FORM blobs, >2MB payloads or credential-pattern matches. Existing game/media/binary experiments remain ignored.

### Controlled GMS2 archive vs GMS1 runner (critical negative control)
First minimal case utry-windows-gms1-runner omitted external media and exited139 (attempt 20261004T060347791657Z). That result alone was confounded by incomplete media; do not use it as the decisive comparison.

Added --runner read-only bind override and --config-dir to native probe. Repeated using FULL existing windows-data-linux-runner case (the same Windows archive, official supporting assets and scout-selected library paths that had rendered title/name input). Only runner changed to native-baseline/runner; private config was fresh windows-data-gms1-control-config. Command:

```sh
python scripts/testing/run_native_probe.py experiments/2026-10-04/runs/windows-data-linux-runner --runner experiments/2026-10-04/runs/native-baseline/runner --config-dir experiments/2026-10-04/runs/windows-data-gms1-control-config --scout-selected --seconds 20
```

Attempt 20261004T060438996765Z, start1791093879.1478968, end1791093881.974122, exit139, timed_out=false, empty stderr/stdout log. This is a runtime crash consistent with incompatibility; exact crash location remains unknown and requires debugger work. It does NOT identify a specific missing variable/function and does not prove that a full semantic converter is impossible. Positive controls: same GMS1 runner renders original Undertale and Windows Together archive under the selected libraries; same UTRY archive renders/accepts keys under official GMS2 runner.

## Current evidence matrix

| Data | Native runner | Verified outcome |
| --- | --- | --- |
| Original Linux Undertale 1.08 | Original GMS1 | Intro rendered |
| Windows Together patch output | Original GMS1 | Mod-specific First human name UI; user reports working |
| Windows UTRY patch output | Official UTRY GMS2 | Title/name entry and input verified |
| Official Linux UTRY patch output (identical data) | Official UTRY GMS2 | Interactive Flowey encounter |
| Same full Windows UTRY case | Original GMS1 | Crash139 before gameplay |

Together successful launch was intentionally ended by harness after240seconds, exit-15, not a spontaneous crash. Classifier labels it MostlyPortable and estimates100%; that percentage is a heuristic, NOT measured whole-game success. Scope:287new code entries,605modified,246sprites,57objects,90scripts,2rooms.

### GitHub publication and authorization
Automatic review rejected first source push, stating that repository creation did not explicitly authorize source/journal payload upload. Requested precise authorization for commit461392d, destination private https://github.com/AndrewImm-OP/gmmt, excluding games/mods/saves/binaries. User replied `Да, отправляй`. Push succeeded; GitHub readback confirmed isPrivate=true, default branch master. Existing local executable-bit differences and untracked historical debugging scripts remain untouched. Initial snapshot includes original development changes as a checkpoint, not as new agent-authored conversion functionality.

## Recommended next development step
Implement a runtime compatibility/packaging plan as a distinct strategy: exact archive metadata, selected locally supplied runner, extension dependencies, external assets, source hashes, and evidence level. Keep GMS2-to-GMS1 lowering labeled experimental/unsupported until meaningful semantic translation tests pass. Do not automatically label a mod Windows-only based on one package, and do not select a mod solely because it lacks a Linux download: first establish its engine generation and dependency demands. A third case using GMS2 data without an existing ready native package is still required to test generality. Runner redistribution is not assumed authorized; consume a user-provided compatible runner in the initial design.

# 2026-10-04 — implementation pivot requested by user

User explicitly requested implementing native-runner selection/packaging, preserving the previous idea in `old` rather than discarding it. Earlier source/journal publication to the private gmmt repository was authorized.

## Migration and preserved implementation
Moved previous src tree to old/translation/src; copied original solution, build props and setup; moved historical auto-bisect.sh, bisect-unwrap.sh and fix.patch there. Added old/translation/README.md with restoration/build instructions. The historical setup is saved for reference; root setup manages shared dependencies. Retained common UndertaleModTool/Underanalyzer submodules and framework patches at repository root.

Compared all66 tracked files from original src at pre-pivot HEAD63e0aa3 against old/translation/src: no missing files, only Gmmt.Core.csproj changed (relative reference becomes four levels up to root extern instead of two). Conversion, classifier, transplanter and desktop algorithms are preserved byte-for-byte. New active Core reuses only archive loader/metadata; new active CLI/Desktop depend on new Gmmt.Runtime, not the transplant pipeline.

Initial archive build failed CS0012 (Underanalyzer reference) because moved obj/project.assets.json still pointed to original src paths. The first parallel restore silently stopped at Determining projects to restore under sandbox. Root cause was stale generated restore artifacts, not damaged archived source. Retried dotnet restore old/translation/src/Gmmt.Cli/Gmmt.Cli.csproj --force -m:1 with experiment-local NuGet packages; sequential build succeeded843warnings0errors. No changes to old conversion algorithms.

## New architecture and behavior
- RuntimeCatalog: local JSON profiles registered using user-supplied Linux ELF and known compatible reference archive. Records exact runner SHA256, ELF class/machine, full parsed engine version, bytecode and reference archive hash; optional locally installed scout script. Registration itself does not run or certify a runner. Duplicate IDs rejected.
- ArchiveInspector: UndertaleModLib parsed metadata, archive SHA256 and native extension filenames. YYC rejected.
- Planner: exact engine/bytecode match, reference checksum preference, explicit selection for ambiguity, host architecture check, runner hash/header revalidation. SameArchiveAsReference and MatchingMetadataOnly are evidence labels, neither means gameplay passed.
- ArchiveInput: prepared archive or xdelta reconstruction from clean Windows archive using system xdelta3. ArgumentList avoids shell/quote interpolation; ordinary xdelta checksums remain enabled;120second timeout and temporary cleanup. Hashes original patch and baseline.
- PackageBuilder: brand-new output only; stages sibling temporary dir and publishes by rename. Copies input data byte-for-byte to assets/game.unx, merges original/mod media in supplied order, copies runner and optional user libraries, verifies data/runner SHA256 and records per-file hashes. Source installations and saves untouched. Rejects symlink resource inputs and output inside input resource tree. Excludes installers/scripts/patch/main archive duplicates from resource merge. Native extensions require explicit manual dependency-review acknowledgement.
- Launcher: relative package directory, local lib path, optional Steam scout selected host/runtime library order, GMMT_STEAM_RUNTIME relocation override. Copied executable permission attempted on Linux; falls back to system ELF loader if permission bits unavailable. Requires matching host multilib/system libraries; does not bundle scout/system libraries.
- Manifest: input hashes, selected profile/metadata/evidence, skipped resources and output hashes. GameplayVerified=false; packaging alone cannot mark verified playability.
- CLI: inspect, register-runner, runners, plan, package. Native package can originate directly from data.win or from Windows xdelta+vanilla. --catalog and GMMT_CATALOG override default user local application data/gmmt/runners.json. plan blocked exit2; input/package errors exit1.
- Desktop: new Avalonia form with Packaging and Runners tabs, file/folder selectors, patch+Windows baseline input, resource overlays, library directories, output path, optional runner ID and dependency-review checkbox. Shares same runtime services. Long parse/copy operations off UI thread. No automatic engine download, distribution or game launch inside desktop.

## Build and automated tests
New CLI build succeeded. Avalonia dependencies restored and new Desktop build succeeded. New solution includes Core, Runtime, CLI, Desktop and standalone console tests; root setup updated to .NET10/sequential solution restore+build. Existing upstream nullable/Fody/NuGet audit-cache warnings remain; new solution build826warnings0errors at observed check.

Standalone tests under tests/Gmmt.Runtime.Tests require no new test-framework NuGet dependency. First run failed because test expected InvalidDataException to inherit IOException; corrected the explicit rejection filter and aligned planner exception handling. Added full ELF minimum-header length validation. Passed21selection/packaging checks: PE rejection, exact reference preference, engine mismatch, YYC, absent ID, candidate-only evidence, ambiguity, mutated runner, missing scout script, existing output preservation, recursive output refusal, native extension acknowledgement, raw archive preservation, resources, installer exclusion, false gameplay certification, repeat output refusal, changed input cleanup and symlink input rejection. Tests run via sequential build then --no-build run to avoid previously observed parallel MSBuild sandbox issue.

## Real-package integration inputs and commands
Catalog: experiments/2026-10-04/metadata/runtime-catalog.json (ignored local artifact). Registered undertale-gms1 using native-baseline/runner + native-baseline/assets/game.unx; registered utry-gms2 using official-linux/runner + official-linux/assets/game.unx. Both use existing SteamLinuxRuntime/steam-runtime/run.sh. Runner bytes and references are earlier hashed inputs.

Planner automatically selects undertale-gms1 for together-windows.win with MatchingMetadataOnly; utry-gms2 for utry-2.1.4-windows.win with SameArchiveAsReference. Both have zero detected native extension files. Explicit UTRY --runner-id undertale-gms1 returns2 with engine/bytecode mismatch blocker, without launching the crashing runner. Raw plan/register outputs: logs/new-register-gms1.log, new-register-gms2.log, new-plan-together.log, new-plan-utry.log, new-incompatible-plan.log.

Together package was built using --patch together-package/Undertale Together/UndertaleTogether.xdelta --vanilla cached Windows1.08 --assets native-baseline/assets --assets Together/Optional Files --output packages/together.227hashed output files. UTRY package built with --archive runs/utry-2.1.4-windows.win --assets runs/official-linux/assets --output packages/utry.329hashed output files. Both package commands exited0 and launch.sh passed sh -n. Windows data hashes match earlier reconstructions; originals not rewritten. See logs/new-package-together.log and new-package-utry.log and local output manifests.

## Generated-launcher smoke tests
Native harness now supports --entrypoint launch.sh to test package launchers instead of bypassing them. Private config overlays still used.

```sh
python scripts/testing/run_native_probe.py experiments/2026-10-04/packages/together --entrypoint launch.sh --seconds 30
python scripts/testing/run_native_probe.py experiments/2026-10-04/packages/utry --entrypoint launch.sh --seconds 45
```

Together attempt20261004T062328482252Z and UTRY attempt20261004T062517250532Z survived until intentional timeout (exit-15). Valid viewed screenshots new-package-together-screen.png (FFFFF transient startup text) and new-package-utry-screen.png (AAAAA transient startup text) prove rendered startup windows, NOT menu/gameplay for these newly generated packages. The identical archives/runners previously reached name entry and Flowey in other cases, but do not silently upgrade these startup smoke-test evidence levels. Full gameplay, sound/video, two-player movement and end-to-end desktop file-picker workflow remain unverified. To avoid retaining screenshots of unrelated active apps, pre/post-capture active title is checked and changed-focus captures discarded.

New Desktop first launch under bwrap read-only root/private config reached35second timeout with no exception log; no screenshot taken because user was active in ChatGPT when capture checked. Second90second visual check was started separately; record result below. Actual desktop service operations are exercised through CLI/shared-service tests; do not claim completed manual UI workflow without evidence.

## Remaining limitations
A locally supplied reference is a user assertion, not a compatibility certificate. Exact metadata does not prove every engine build matches; external scripts and shaders can still be platform-specific. Asset and library directories are supplied explicitly rather than fully inferred. No shipped runner pool, automated download, runtime redistribution permission or universal GMS2-to-GMS1 lowering. Required next scope: test a GMS2 mod without ready native package, broaden gameplay validation, and add independently measured verified runner/archive associations if desired.

### Desktop visual verification and final checks
Bwrap launches with entirely read-only / (including temporary/cache paths) survived but did not expose an X11 window in bounded searches. This harness is more restrictive than normal desktop startup; do not count those launches as successful UI verification. Ordinary timed launch without bwrap, with GMMT_CATALOG pointing to the local test catalog, produced exactly one GMMT window (33554447), title GMMT · Linux runner packages. Captured new-desktop-screen.png after verifying focus before/after. This verifies real initial UI rendering; file-picker/build workflow remains tested through shared services/CLI, not fully exercised via UI automation. Initial isolated desktop issue not yet reduced to a specific temporary/cache dependency.

Final sequential solution build:826warnings0errors; final standalone tests PASS21. Archived CLI sequential build843warnings0errors. No game archive or runner binary is added to source control. Preserved legacy source check no missing files; only shared dependency relative path changed.

## 2026-10-04 — Desktop refinement and Linux distribution packages (0.2.0)

### Request and implementation

The user requested a prettier interface and actual AppImage/deb/rpm/another distro-friendly format, then a detailed GitHub README and a short Telegram announcement. Existing native-runner packaging remains the main implementation; old/translation remains untouched.

The Avalonia desktop retains its dark Fluent foundation with charcoal surfaces, mint primary action, consistent field spacing and a compact header. The main tab now presents the mod archive, resources and output first. xdelta baseline, optional libraries, runner override and native-extension acknowledgement are inside an expander. Runner registration and catalog path are in the runners tab. Result/status stay pinned while the form scrolls at smaller sizes. Busy state disables form actions and displays indeterminate progress; exceptions use an explicit error status. Analysis blockers now use an amber blocked status rather than a misleading green completion label. TextBox accessibility names use field labels. No engine/downconversion functionality was altered.

Added packaging/gmmt.svg (original geometric monogram) and gmmt.desktop. scripts/build-linux.py performs Release self-contained linux-x64 publication of desktop and CLI, merges shared files, preserves dependency notices, creates all four formats and hashes the outputs. Source-controlled app screenshot: docs/images/desktop.png. README is now a detailed Russian user/developer guide; docs/packaging.md records technical packaging behavior and limits. dist/ is ignored; no game inputs or binaries were added to Git.

### Environment and build commands

Host: CachyOS (Arch family), x86_64. SDK: 10.0.111; published .NET runtime pack 10.0.11. Existing local tools: dpkg-deb, rpmbuild, zstd, appimagetool. appimagetool itself is an AppImage: APPIMAGE_EXTRACT_AND_RUN=1 avoids the host's unavailable FUSE mount. Packaging uses /tmp for staging and NUGET_HTTP_CACHE_PATH, because NuGet HTTP cache names contain a colon, unsupported by the checkout's exFAT filesystem. Ordinary NuGet package files remain under ignored experiments/2026-10-04/nuget-packages. First restore may require network. Do not run two publish processes against this checkout concurrently.

Final build command from repository root:

```sh
DOTNET_CLI_HOME="$PWD/experiments/2026-10-04/dotnet-home" \
  python scripts/build-linux.py --version 0.2.0
python scripts/verify-linux.py --version 0.2.0
```

The appimagetool runtime download succeeded; its bundled mksquashfs is limited to two workers through --mksquashfs-opt. RPM construction disables host-specific automatic dependency scanning and post-install stripping of managed payloads. Dependencies are explicitly listed. No installation/root actions were performed. deb uses --root-owner-group. Linux package wrappers are /usr/bin/gmmt (GUI) and /usr/bin/gmmt-cli (CLI); both point to /opt/gmmt. AppImage/portable AppRun starts GUI by default and accepts --cli. xdelta3 remains a system dependency and is recommended by deb/rpm.

Actual files in dist/:

- GMMT-0.2.0-x86_64.AppImage
- gmmt_0.2.0_amd64.deb
- gmmt-0.2.0-1.x86_64.rpm
- gmmt-0.2.0-linux-x86_64.tar.zst
- SHA256SUMS
- build-info.json

Build-info truthfully records the previous source HEAD plus dirty=true: this batch was built before committing its source/UI changes. It does not claim a clean tagged release or bit-identical reproducibility. SHA256SUMS records actual packaged files; use it for exact identity rather than approximate sizes.

### Validation results

scripts/verify-linux.py passed for all four formats. It validates SHA256SUMS, exercises the AppImage extract-and-run CLI path, extracts all formats into separate temporary directories, compares every payload file by SHA256, verifies executable apphosts and installed launchers, validates desktop entries and runs each CLI with DOTNET_ROOT and DOTNET_ROOT_X64 pointing to a nonexistent directory. That confirms the included runtime is usable without the system SDK. ldd resolves apphost, SkiaSharp, HarfBuzzSharp and ImageMagick dependencies on this host. The first validation script mistakenly expected libAvaloniaNative.so, which is not the Linux backend; the corrected script checks the actual Linux native libraries. Optional .NET tracing library libcoreclrtraceptprovider.so can lack liblttng-ust on this host; normal CLI/GUI launch does not require that tracing feature.

Final regression build restored with network permission and temporary HTTP cache:

```sh
DOTNET_CLI_HOME="$PWD/experiments/2026-10-04/dotnet-home" \
NUGET_HTTP_CACHE_PATH=/tmp/gmmt-test-http-cache \
DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  dotnet build tests/Gmmt.Runtime.Tests/Gmmt.Runtime.Tests.csproj \
  -m:1 -p:NuGetAudit=false --packages "$PWD/experiments/2026-10-04/nuget-packages"
DOTNET_CLI_HOME="$PWD/experiments/2026-10-04/dotnet-home" \
  dotnet run --project tests/Gmmt.Runtime.Tests --no-build --no-restore
```

Result: 819 existing dependency warnings, zero errors; PASS: 21 runtime selection and packaging checks. An earlier restricted-network restore failed; that failed attempt is not counted as a successful build.

Visual inspection of the desktop at 980×860 and 700×640 shows preserved field alignment, scrolling at minimum size, and a pinned readable result area. Retained local images: logs/refined-desktop-wide.png and logs/refined-desktop-narrow.png. The source-controlled screenshot includes only GMMT with empty inputs, no private filesystem paths/game assets. The packaged AppImage also creates an actual native window titled GMMT · Моды для Linux in extract-and-run mode. GUI screenshots are only retained when the verified GMMT window is active; focus can move to the user's desktop while tools execute, so a skipped screenshot is not evidence of a crash. Mechanical skill detector ran once against the two edited Avalonia files and returned an empty finding array; actual screenshots remain the visual evidence.

Logs under ignored experiments/2026-10-04/logs:

- linux-packaging-0.2.0.log
- linux-packages-verified.log
- linux-final-tests-build.log

### Remaining platform checks and release constraints

This verifies the current CachyOS host and extracted package content, not package-manager dependency resolution or desktop behavior on every Debian/Ubuntu/Fedora version. Test real installation/uninstallation in clean VMs next. Docker is present as a client, but no usable server was detected in this session. All artifacts are x86_64/glibc; ARM, musl, Flatpak and Windows builds were not produced. deb/rpm are unsigned. AppImage still relies on standard OS GUI/native libraries; it does not bundle a whole distribution or game runner dependencies.

No GitHub Release was created: the task requested builds and source/README updates, and local downloadable artifacts are concrete. Code/documentation may be pushed to the already authorized private AndrewImm-OP/gmmt repository. Never push dist/ or experiments/ through git. The user's desired Telegram description should accurately describe experimental native runner packaging and four application formats without claiming universal compatibility or full gameplay validation.

## 2026-10-04 — Optional Steam replacement and restoration (0.2.1)

### User-facing choice

The user requested optional replacement of installed originals, with a checkbox for separate-directory mode. Added "Собирать в отдельную папку", checked by default. Checked mode keeps the previous standalone output behavior. Unchecked mode shows the native Linux game folder, explains closing the game/backups and exposes restoration. The primary button becomes "Установить мод в Steam". The UI suggests the locally present standard Undertale Steam folder but never installs until the user selects that mode and presses the action. Source-controlled screenshot refreshed to show the checkbox in its safe default state.

Steam mode prepares a temporary package before modifying installed files. Original game assets and lib directories are automatically prepended to the selected resource/library overlays. This preserves original external resources unless the mod intentionally replaces the same relative paths. Restore invokes the shared service off the UI thread. CLI gained `install --package DIR --game-dir DIR` and `restore --game-dir DIR`.

### Installer contract

New `src/Gmmt.Runtime/SteamInstaller.cs` targets the observed native Linux layout: runner, assets/game.unx and run.sh. The actual installed Undertale run.sh was read and contains `LD_LIBRARY_PATH=./lib:$LD_LIBRARY_PATH ./runner` preceded by chmod. No UNDERTALE named executable exists in this installation. Therefore the integration replaces run.sh with a forwarding script that uses `sh ./launch.sh` and forwards arguments. This exercises the package's chosen runtime/library selector rather than inheriting the original game's incompatible runner environment. Custom Steam launch options that bypass run.sh and Windows/Proton layouts are outside this contract.

Managed entries: runner, assets, lib, run.sh, launch.sh and gmmt-package.json. Unrelated top-level files (including the installed root libsteam_api.so) are left alone. Existing managed entries are moved into `.gmmt-original` inside the game directory, preserving the pre-install state and Unix permissions. That state is not necessarily vanilla: this user's installed game already had prior modifications. Any existing `.gmmt-original` blocks another installation. A file lock prevents overlapping GMMT operations. User saves outside the game directory are not modified by this service.

Preflight rejects non-Linux use, invalid game layouts, intersecting package/game paths, symlinks in managed input/backup paths, unavailable runner ELF, changed or unlisted package files and missing required manifest entries. Copied staging files are hashed again before moving the first original. Original and installed file/directory hashes are recorded in an installation journal. Installation exceptions attempt rollback; the Installing/Restoring journal supports resuming interrupted transitions through Restore. A process interruption is covered; this is not a guarantee of durability under filesystem corruption or power loss. Game/Steam updates are not coordinated by the GMMT lock: the UI instructs closing the game and restoring before integrity checks/updates.

Ordinary restoration checks installed and original-backup hashes before changing anything. If the mod tree or backup changed, it refuses and preserves the backup rather than silently discarding changes. Restoration removes added managed entries and returns backed-up entries. Interrupted restoration recognizes originals already moved back and resumes. Do not remove the backup manually to get past a failure; inspect hashes and the journal instead.

### Tests and evidence

Final sequential solution build: 818 upstream warnings, zero errors. Final standalone tests: PASS 41 runtime selection and packaging checks (21 previous +20 install/restore checks). New cases cover archive/launcher replacement, backing up original media, duplicate-install refusal, refusing modified installations, backup retention on refusal, exact original data and executable permissions, removal of mod-only files, preservation of unrelated files, overlapping paths, non-native runners, tampered/unlisted packages, no backup left by preflight rejection, symlink rejection, tampered-backup refusal without partial writes, interrupted restoration and lock contention.

Real integration used a fresh copy at `experiments/2026-10-04/runs/steam-install-copy`, copied from the real Steam Undertale directory. The already generated real Together package was installed through the new CLI. Its archive SHA256 matched the input package. Full source-tree hashes of the actual installed Steam game remained unchanged. The test harness was extended to accept `--entrypoint run.sh` and invoked the new forwarding launcher with a private ~/.config overlay:

```sh
dotnet src/Gmmt.Cli/bin/Debug/net10.0/gmmt.dll install \
  --package experiments/2026-10-04/packages/together \
  --game-dir experiments/2026-10-04/runs/steam-install-copy
python scripts/testing/run_native_probe.py \
  experiments/2026-10-04/runs/steam-install-copy --entrypoint run.sh --seconds 20
dotnet src/Gmmt.Cli/bin/Debug/net10.0/gmmt.dll restore \
  --game-dir experiments/2026-10-04/runs/steam-install-copy
```

Launch attempt 20261004T071429841225Z survived until intentional timeout (exit -15). This establishes execution through the new Steam-shaped run.sh path, not full gameplay or pressing Play in the Steam client. Restored copy matched EVERY pre-install file SHA256; the actual installed Steam folder also matched the saved source-tree snapshot. After adding stricter full-manifest/staged-copy verification, install/restore was repeated against the same restored copy and again matched every original file.

Logs/metadata: logs/steam-copy-install.log, logs/steam-copy-launch.log, logs/steam-install-copy-20261004T071429841225Z-*.json/log, logs/steam-install-build-final.log, metadata/steam-copy-before.json. No original Steam files were replaced in this session.

Desktop arrival was visually checked with the new checkbox checked (logs/steam-choice-ui.png). Further focus-sensitive snapshots may be skipped when the user's desktop takes focus; do not count a skipped unchecked-mode screenshot as visual evidence. The installation logic and restore are exercised through the shared service/CLI rather than a claimed complete native file-picker workflow.

### Distribution update

README and packaging guide describe 0.2.1 and the new mode, its native-Linux restriction, backup semantics and CLI. Build/verification scripts default to 0.2.1. Updated artifacts are generated under dist/0.2.1 to retain the preceding 0.2.0 builds. Rebuild and extraction verification commands:

```sh
python scripts/build-linux.py --version 0.2.1 --output dist/0.2.1
python scripts/verify-linux.py --version 0.2.1 --dist dist/0.2.1
```

The installation feature itself was tested in a copied folder, not installed into the user's Steam game. Source code and documentation may be pushed to the already authorized private repository. Full Steam client Play-button validation and clean Debian/Fedora installation remain pending tests.

## 2026-10-04 — Automatic discovery of Steam games and runner candidates (0.2.1)

The user's next steering requested automatic game-folder and runner discovery. Added SteamDiscovery.Scan, automatic background scan on desktop opening, game and runner candidate ComboBoxes, repeat-search actions and optional additional runner search folders. Selection fills the game directory or donor runner/reference/runtime/ID fields. Candidate profiles are not silently registered: users confirm a known-working reference before Add runner. Existing catalog entries and manually filled paths remain intact; a finishing scan does not overwrite the busy build/install result.

Discovery checks ~/.local/share/Steam, ~/.steam/steam, ~/.steam/root and Flatpak Steam's ~/.var/app/com.valvesoftware.Steam/.local/share/Steam. It resolves known Steam/library aliases to canonical directories, reads modern and legacy libraryfolders.vdf paths, deduplicates libraries/candidates and enumerates common game directories. Native installable games must have runner, assets/game.unx and run.sh plus a detected runner. SteamLinuxRuntime/Proton/Steamworks runtime folders are skipped rather than recursively crawling their OS trees.

Runner scans are read-only, depth-limited (three levels, maximum 256 directory visits per game/search root, bounded files/children). They look for local archive filenames beside executables or under assets. No ZIP extraction, execution, download or archive metadata certification is performed. ELF headers/architecture are validated; ET_DYN must have PT_INTERP to avoid ordinary shared libraries. A bounded 64-MiB binary marker scan requires GameMaker/YoYo strings plus game.unx or instance_create, so a neighboring xdelta3 installer tool is not mistaken for a runner. Missing these markers means the user must provide the runner manually; this is discovery heuristics, not engine compatibility proof. Symlinked internal trees and active GMMT backup/staging directories are skipped. ~/Downloads and ~/Загрузки are searched by default; explicit additional folders replace those defaults for a scan.

The real UTRY directory initially exposed an important discovery bug: both its actual ELF32 runner and adjacent ELF64 xdelta3 helper were considered candidates when only ELF format was checked. Binary inspection showed YoYo Games/GameMaker/game.unx markers in both measured game runners and none in xdelta3. The corrected scanner filters the helper, and the synthetic test fixture now contains valid ELF helpers without GameMaker markers as a regression case. Runtime library trees previously triggered scan-limit warnings; skipping runtime/tool directories removed that unrelated work.

CLI: `gmmt-cli discover [--steam-root DIR ...] [--runner-root DIR ...]`. Output is JSON with libraries, native game folders, candidate runner/archive pairs and nonfatal warnings. The desktop combines Steam discovery with extra directories and provides manual file selection when no result matches.

Validation: sequential solution build 818 upstream warnings, zero errors; PASS 48 checks. Seven additional discovery assertions cover an external library with spaces, native Steam game detection, correct runner versus helper/shared object, deduplication through Steam aliases, separately downloaded native mods, legacy VDF paths and Windows executable rejection. The prior 41 install/runtime checks still pass.

Actual source scan found two Steam libraries and native Undertale. It reports separate candidate references for game.unx, game.unx.vanilla and gameog.unx: the first can already contain previous modifications, so none is automatically certified. Adding the local runs/official-linux directory finds the UTRY GameMaker runner while excluding its xdelta3 helper. Exact local output: metadata/discovered-steam-and-utry.json. These paths are not committed. README now explains discovery, extra roots, confirmation and limits. The app still does not change the installed Steam game until explicit install action.

Updated 0.2.1 packages are rebuilt after the discovery fixes, not reused from the earlier checkbox-only build. Final package extraction/CLI checks must be rerun against dist/0.2.1 before reporting them to the user. Full Play-button validation, broad gameplay tests and Debian/Fedora VM installation remain outstanding platform evidence, not blockers for the implemented local discovery/installation features.

### Final 0.2.1 verification

The final four distribution artifacts passed scripts/verify-linux.py again: identical payload hashes across formats, autonomous CLI, executable launchers, desktop entries and native dependency resolution. The final AppImage CLI also ran discover against the actual Steam roots and the explicit local UTRY folder: two libraries, one installable Steam game, four runner/reference pairs, zero warnings, and no xdelta3 candidate. It reads through the bundled runtime; no runner is executed by discovery. Final logs: discovery-packaging-0.2.1.log and discovery-packages-verified.log.

Automatic startup discovery was visually confirmed in the real desktop window (logs/discovery-default-ui.png): main form remains usable and its result area reports two libraries, one native game and three Steam-only candidate references. The source screenshot contains no filesystem paths or private game media. Final UI source removes the old hard-coded Steam-folder guess so discovery can fill the canonical path when the Steam root itself is a symlink. External/manual paths are not overwritten by discovery.

---

## 2026-10-04 — v0.2.2 to v0.2.4 updates and Runner Matching Handoff

### 1. Work completed (v0.2.2 — v0.2.4)
- **Desktop Localization (v0.2.2)**:
  - Added Russian (`ru.json`) and English (`en.json`) embedded resources in `src/Gmmt.Desktop/Localization/`.
  - Added live language switcher dropdown (`ComboBox`) with immediate UI update without application restart.
  - Added persistent language settings saved in `~/.config/gmmt/settings.json`.
- **Steam Depots, Deltarune Donor, & UI Tabs (v0.2.3 — v0.2.4)**:
  - Added Steam depot discovery in `SteamDiscovery.cs` scanning `steamapps/content` and `ubuntu12_32/steamapps/content/` for `app_391540/depot_391541/data.win`.
  - Added 1-click depot download: directly executes `steam -console +download_depot 391540 391541`.
  - Added 1-click Deltarune install: opens `steam steam://install/1671210` to fetch a free native GMS2 Linux runner.
  - Automatically auto-populates `vanilla` with discovered `data.win` or `game.unx`.
  - Reorganized `MainWindow.cs` into 3 separate tabs: `Сборка мода` (`tab.build`), `Steam и депоты` (`tab.steam`), and `Раннеры` (`tab.runners`).
  - Added ToolTips across all UI elements (`ru.json` and `en.json`).
  - Releases v0.2.2, v0.2.3, v0.2.4 built, verified with `scripts/verify-linux.py`, and committed to Git (`e581120`).

---

### 2. Root Cause Analysis: "No matching local Linux runner" for UNDERTALERY (GMS 2.0.6.0, BC16)

#### Problem statement
When attempting to build or verify Undertale Red & Yellow (`UNDERTALERY`), GMMT inspects the mod archive:
- Engine: `GMS 2.0.6.0`
- Bytecode: `BC16`
- Flag: `IsGMS2 = true`

GMMT outputs:
```
UNDERTALERY: GMS 2.0.6.0, BC16
Runner: не найден
No matching local Linux runner. Register a runner together with its known-compatible reference archive.
```

#### Why Undertale's runner was rejected
Undertale in Steam (`~/.local/share/Steam/steamapps/common/Undertale/runner`) is GameMaker: Studio 1 (`1.0.0.1539`, `IsGMS2 = false`, BC16).
In `RuntimeCatalog.Plan`:
```csharp
candArchive.Metadata.BytecodeVersion == archive.Metadata.BytecodeVersion &&
candArchive.Metadata.IsGMS2 == archive.Metadata.IsGMS2
```
Because `IsGMS2` does not match, Undertale's GMS1 runner was rejected. This rejection is correct: GMS1 runners crash when executing GMS2 bytecode due to differences in array memory representations and VM opcodes.

#### Why the mod's bundled runner was missed
The downloaded archive `/home/andrew/Загрузки/utry-2.1.4-linux.zip` **already contains a native Linux GMS2 runner** (`GMGreen`, ELF 32-bit i386, 5,254,272 bytes).
However, `SteamDiscovery.cs` had this check:
```csharp
var runnerEntry = zip.Entries.FirstOrDefault(e => e.Name.Equals("runner", StringComparison.OrdinalIgnoreCase) && e.Length > 50_000);
var archiveEntry = zip.Entries.FirstOrDefault(e => (e.Name.EndsWith(".unx", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith(".win", StringComparison.OrdinalIgnoreCase)) && e.Length > 50_000);
if (runnerEntry != null && archiveEntry != null)
```
`utry-2.1.4-linux.zip` contains `runner` and `patch.xdelta`, but NO `.unx` or `.win` file! Therefore `archiveEntry == null`, and `SteamDiscovery` completely ignored the zip!

#### Why Cyrillic characters become '?' on copy
In Linux X11, copying text via standard Avalonia `TextBox` text selection can negotiate `STRING` (ISO-8859-1) target atom instead of `UTF8_STRING` depending on the receiving application. Adding an explicit "Копировать лог" button using `Clipboard.SetTextAsync` sends UTF-8 directly.

---

### 3. Immediate Implementation Plan for Next Agent

1. **Automatic Runner Cloud Cache / Bundling (CRITICAL)**:
   - Provide pre-cached/downloadable standard GameMaker Linux runners (GMS 1.4 BC16, GMS 2.0 BC16, GMS 2.3+ BC17) hosted on GitHub Releases or embedded in GMMT data.
   - On first launch or on missing runner, GMMT automatically downloads/provisions the needed runner into `~/.local/share/gmmt/runners/`.

2. **Runner Discovery from ZIP without bundled .unx/.win**:
   - In `SteamDiscovery.cs`: extract `runner` even if `archiveEntry == null`.
   - Inspect runner binary for GM engine markers:
     - `GMGreen` or `GameMaker Studio 2` => `IsGMS2 = true, BytecodeVersion = 16`.
     - `GMRed` => `IsGMS2 = false, BytecodeVersion = 16`.
   - Allow registering runner with synthetic profile or using the mod archive being built as the reference archive.

3. **Direct .zip Mod Support**:
   - In `ArchiveInput.cs`: allow user to select `*.zip` directly.
   - Auto-extract patch (`*.xdelta`), vanilla base, bundled `runner`, and extra `assets/`.
   - Use the bundled `runner` automatically for that build.

4. **Automatic xdelta Base Fallback**:
   - If patching fails with checksum mismatch (`XD3_INVALID_INPUT`), try alternate base (`game.unx` vs `data.win`).

5. **Copy Log Button**:
   - Add "Копировать лог" button calling `Clipboard.SetTextAsync` with UTF-8 text.


## 2026-10-04 — Autonomous ZIP/runner workflow (0.2.5)

The user clarified that the earlier handoff is advisory and that autonomy/convenience are the objective. Another agent had stopped with uncommitted ArchiveInput, CloudRunnerProvider and RunnerElfInspector files, an ignored runner ZIP, and an already published gmmt-runners.zip asset in v0.2.4. Those files did not compile together: the provider called a DiscoveredRunner constructor that did not exist. It also invented precise engine/BC values from marker strings and suggested recording the target mod as the compatibility reference. These assumptions were replaced before use. Previously published v0.2.2–v0.2.4 application releases were verified as existing; they were not recreated.

### Implementation and evidence semantics

ArchiveInput accepts a ZIP with one archive or patch, extracts its Linux runner and assets, cleans its temporary files and retains input/base hashes. Ambiguous mod inputs are rejected. ZipInput centralizes bounded extraction, traversal/symlink rejection and destination checks. SteamDiscovery can cache a runner from a patch-only ZIP; cache directories are keyed by whole-ZIP SHA256, avoiding same-name/same-size collisions. Discovery does not execute runners. The existing source paths are preserved.

RunnerElfInspector returns a nullable engine-family hint with overlapping bounded reads; it no longer invents an engine version or bytecode revision. RuntimeCatalog may produce an ephemeral UnverifiedRunnerCandidate for a historical BC16 family hint or a matching pinned manifest. ReferenceArchiveSha256 is empty, the target archive is not used as a fake reference, and this ephemeral profile is not persisted. GameplayVerified remains false. Older discovered runner/reference auto-registration behavior is retained. Registered profiles and real reference candidates take precedence.

RunnerResolver is shared by desktop and CLI packaging. It tries registered/local/bundled candidates, then an optional downloaded cache. CloudRunnerProvider downloads the existing v0.2.4 asset with pinned SHA256 0245afb8bc62ff4d9689ef872f3f1416323ef45449315e12cc24ddfd2e4fa519, bounded network size/timeout, a temporary file and atomic replacement; every cache read checks the ZIP checksum. Only manifest-listed runners are extracted and ELF/family/bits checked. Download candidates require exact manifest engine and BC metadata. No binary ZIP is embedded in application packages or committed to source. The existing published ZIP is not re-uploaded by this change. It contains two x86/BC16 entries: GMS1 1.0.0.1539 and GMS2 2.0.6.0. Do not describe it as supporting all BC14–16 or all GMS2 revisions. No GMS2.3+/BC17 bundle is supplied.

ArchiveInput's xdelta fallback keeps checksum verification enabled, tries explicit/discovered/standard base candidates, verifies that the successful base did not change during decoding and records that base's hash. Native fallback is opportunistic: Windows/Linux bases are not interchangeable unless the patch actually validates. UI resource fields can remain empty when the selected discovered game's assets are available; mod ZIP assets overlay last. Advanced base controls are collapsed to keep the build action visible. Copy log calls Clipboard.SetTextAsync; headless UI checks validate language changes and existing state preservation, not cross-application clipboard negotiation.

### Validation performed

Sequential solution build completed with zero errors. Runtime suite: PASS 77 checks, including marker chunk boundary, missing marker, direct ZIP bytes/runner/assets, extraction cleanup, traversal, ambiguous inputs, runner-only patch ZIP discovery, target reference not persisted, wrong-family/BC17 rejection, pinned bundle checksum rejection and wrong explicit xdelta base falling through to a valid alternate. Desktop headless suite: PASS 8 interactions; English/Russian wide/minimum renders are under logs/autonomy-ui. Public screenshots are updated from these inspected renders.

The real /home/andrew/Загрузки/utry-2.1.4-linux.zip was packaged with an empty catalog, explicit isolated native baseline and original assets into runs/autonomy-zip-package. Output evidence: UnverifiedRunnerCandidate. Its launch.sh ran inside the existing bwrap harness with an isolated config/save overlay for 15 seconds, survived until deliberate termination (exit -15), and logged active texture processing. This validates initial startup only; it is the Linux UTRY control and does not prove universal Windows-only mod portability or complete gameplay. The installed Steam game and user saves were not changed.

CLI fetch-runners successfully downloaded/read the pinned published bundle and returned both manifest candidates. Logs: autonomy-zip-package.log, autonomy-zip-launch.log, autonomy-cloud-runners.json, /tmp/gmmt-autonomy-final-build.log. The package has its own detailed gmmt-package.json. Application distribution verification and publication results are appended after the final build.

Next work: a Windows-only mod without an existing Linux port, longer gameplay/input/save/extension coverage, a real Steam Play-button test, clean Debian/Fedora installation tests, and acquiring valid GMS2.3+/BC17 donor/reference evidence. The family hints do not replace that evidence.


### Final 0.2.5 distribution verification

`python scripts/build-linux.py --version 0.2.5 --output dist/0.2.5` completed. `python scripts/verify-linux.py --version 0.2.5 --dist dist/0.2.5` passed all four smoke tests: identical extracted payload hashes, standalone CLI, executable launchers, menu entries and resolved native dependencies on this host. No system package was installed. Verification was initially started before the builder produced SHA256SUMS, so that premature invocation failed with a missing-file error; it was rerun successfully after builder completion. Logs: /tmp/gmmt-build-0.2.5.log and /tmp/gmmt-verify-0.2.5.log.

Artifacts: GMMT-0.2.5-x86_64.AppImage, gmmt_0.2.5_amd64.deb, gmmt-0.2.5-1.x86_64.rpm, gmmt-0.2.5-linux-x86_64.tar.zst, SHA256SUMS and build-info.json. Build provenance names source commit b70774e47ab9c04950dcf67720eaa02c8eae5693, SDK 10.0.111, RID linux-x64, self-contained true. The dirty flag is true because the dependency submodule still has the previously recorded framework patches; they were deliberately preserved.

Source commit b70774e was pushed to master using the existing authenticated GitHub CLI credential helper. The first plain git push stalled; the duplicate was stopped after the credential-helper push succeeded. Release publication had one pre-creation GitHub TLS handshake timeout and was retried; publication verification is recorded below after the upload completes.


### GitHub publication completed

Published https://github.com/AndrewImm-OP/gmmt/releases/tag/v0.2.5 targeting b70774e47ab9c04950dcf67720eaa02c8eae5693. The final API read confirms published (not draft) and exactly six uploaded assets: all four application formats, SHA256SUMS and build-info.json. Each GitHub asset digest was compared against the actual local file SHA256 and matched.

The TLS failures were diagnosed as the configured local HTTP proxy timing out during HTTPS handshakes. A direct API request succeeded. The failed create attempt did not leave a release visible by tag, ID or release list, so publication was repeated using per-process NO_PROXY for api.github.com and uploads.github.com. Global proxy settings were not changed. No duplicate release or additional runner upload was created. The successful release URL was returned by gh and independently reread for asset/digest verification.
