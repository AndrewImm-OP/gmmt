# Archived archive-transplant implementation

Preserved from commit 63e0aa3 before the runner-packaging pivot. Contains the old CLI, desktop UI, classifier, diff/transplant pipeline and debugging scripts. No conversion algorithms were deleted. The only source adjustment is Gmmt.Core.csproj: its UndertaleModTool reference points to the shared dependency at the repository root.

From the repository root, initialize dependencies with the current setup.sh, then:

```sh
dotnet restore old/translation/src/Gmmt.Cli/Gmmt.Cli.csproj
dotnet build old/translation/src/Gmmt.Cli/Gmmt.Cli.csproj -m:1
dotnet old/translation/src/Gmmt.Cli/bin/Debug/net10.0/gmmt.dll --help
```

The saved setup.sh is historical; use the root setup for shared dependencies. Generated bin/obj files remain ignored. Unsafe/unfinished semantic substitutions remain here for research, not in the new packaging path.
