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
- Private GitHub repository created; source upload remains pending.

## Open issues
- Continue Windows-data + Linux-runner control beyond name-entry screen.
- Obtain and test a different Windows-only mod; UTRY alone cannot establish generality.
- Complete broader gameplay checks; initial encounter is not full-game validation.
- Review source payload and publish reproducible development snapshot to private GitHub.
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

CAUTION: the probe currently overwrites case-name log/launch/result files on rerun. Preserve previous logs or add unique attempt paths before rerunning. Screenshots are uniquely named. All experiment files are excluded from GitHub; no game archives, runners, media, saves, or downloaded patches should be committed.

## Technical conclusion at this checkpoint
UTRY supplies a real compatible native GMS2 runner and identical Windows/Linux game data. Therefore Windows-origin GameMaker data executing under Linux is technically demonstrated for this specific archive at startup/input level. This does not establish GMS2→GMS1 lowering, runtime universality, portability of Windows DLL extensions or distribution rights for a donor runner. Existing GMMT transplantation remains insufficient for general engine downgrade. Further work should measure runner matching and extension requirements with additional mods before committing to a universal converter design.

### 2026-10-04 — resumed testing, second mod
Undertale Together official package downloaded successfully using European mirror DBolical EU #3 in the browser. Completed ZIP 9846774 bytes, MD5 32cef5b68b0e5a3446a1a596503ff3b2 exactly matches official page. Validated ZIP paths and extracted locally. Package contains SpaghettiInstaller.exe, UndertaleTogether.xdelta, GOG-to-Steam normalization patch, optional OGG/splash files and Windows installation instructions; no native Linux runner or separate Linux patch in this ZIP. Installer not executed. The page has a community Linux tag; existence/absence of a separate port elsewhere has NOT been established.

Applied UndertaleTogether.xdelta with system xdelta3 to cached Windows Steam 1.08 baseline, with normal checksum validation. Output together-windows.win: 64310594 bytes, MD5 778e043668cbb623dfe3350d00cbe8cf, SHA256 e50dae1388f1016864e5b9be0e5bf7bd4e1e36850aa25ac2b6c1c60ed9c23381, BC16, engine header [1,0,0,1539]. This is GMS1-family control, not a GMS2-downconversion test.

Case runs/together-windows-native: original Steam native-baseline runner; assets/game.unx is unchanged Windows mod archive; original Linux external media plus optional media from Together ZIP; no third-party installer or new runner used. Fresh private config overlay. Symlink preparation failed: exFAT filesystem rejects symlinks. Preparation script accidentally continued into launch on incomplete case (attempt 20261004T055814265900Z, exit1); disregard as archive compatibility evidence. Replaced symlinks with actual media copies, completed preparation, then reran known-good scout library selection, attempt 20261004T055907190205Z. Valid screenshot together-first.png shows mod-specific `First human:` name-entry UI. User independently observed the mod working. Subsequent screenshot captured another active app after user changed focus; deleted that generated non-game screenshot and do not treat it as evidence. Pause keyboard automation while user is using desktop. No full-game/network or two-player movement check yet.

Classifier executed on Together patch with original Windows baseline and native target; results in metadata/together-classification.json and logs/together-classification.log.

### Reproducible source snapshot preparation
Added .gitmodules for existing pinned UndertaleModTool gitlink; captured only real .csproj framework edits as patches, excluding exFAT-induced executable-bit noise. Nested Underanalyzer revision remains 94cc220ee9f738d6a79d1a46563ab1caa54ad243. setup.sh now checks .NET10, initializes dependencies, applies framework patches with forward/reverse checks, builds CLI sequentially. Added README with truthful limitations and no invented source license. Native probe now names logs with unique UTC attempt timestamps to preserve retries. Static shell/Python checks passed; reverse patch checks match existing dependency edits. Reviewed 77 candidate files (578011 bytes at initial review) plus original Git history: no archive FORM blobs, >2MB payloads or credential-pattern matches. Existing game/media/binary experiments remain ignored.
