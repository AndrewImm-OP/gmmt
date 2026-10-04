#!/usr/bin/env python3
"""Launch an isolated native game copy; bind a private config dir over ~/.config.
Does not overwrite the installed game or user saves. Captures runner output.
"""
import argparse, json, os, subprocess, time
from datetime import datetime, timezone
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("case", type=Path)
parser.add_argument("--seconds", type=int, default=45)
parser.add_argument("--runner", type=Path, help="Read-only runner override for a controlled runtime comparison")
parser.add_argument("--config-dir", type=Path, help="Separate private config directory for this comparison")
parser.add_argument("--entrypoint", choices=["runner", "launch.sh", "run.sh"], default="runner")
parser.add_argument("--scout-libs", action="store_true")
parser.add_argument("--scout-selected", action="store_true")
args = parser.parse_args()
case = args.case.resolve()
config = args.config_dir.resolve() if args.config_dir else case.parent / (case.name + "-config")
config.mkdir(exist_ok=True)
logdir = case.parent.parent / "logs"
logdir.mkdir(exist_ok=True)
attempt = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S%fZ")
logprefix = case.name + "-" + attempt
private_config_target = Path.home() / ".config"
command = ["bwrap", "--die-with-parent", "--ro-bind", "/", "/",
           "--dev-bind", "/dev", "/dev", "--bind", str(case), str(case),
           "--bind", str(config), str(private_config_target),
           "--chdir", str(case)]
if args.runner:
    runner = args.runner.resolve(strict=True)
    if not runner.is_file():
        parser.error("--runner must be a file")
    command += ["--ro-bind", str(runner), str(case / "runner")]
libpaths = [str(case / "lib")]
if args.scout_libs:
    scout = Path.home()/".local/share/Steam/steamapps/common/SteamLinuxRuntime/steam-runtime"
    libpaths += [str(scout/"lib/i386-linux-gnu"), str(scout/"usr/lib/i386-linux-gnu")]
if args.scout_selected:
    runtime_script = Path.home()/".local/share/Steam/steamapps/common/SteamLinuxRuntime/steam-runtime/run.sh"
    selected = subprocess.check_output([str(runtime_script), "--print-steam-runtime-library-paths"], text=True).strip()
    libpaths += selected.split(":")
command += ["--setenv", "LD_LIBRARY_PATH", ":".join(libpaths)]
command += ["./runner"] if args.entrypoint == "runner" else ["/bin/sh", "./" + args.entrypoint]
result = {"case":str(case),"command":command,"seconds":args.seconds,"entrypoint":args.entrypoint,
          "runner_override":str(args.runner.resolve()) if args.runner else None,
          "save_overlay":str(config),"attempt":attempt,"log_prefix":str(logdir/logprefix),"start_unix":time.time()}
with (logdir/(logprefix+"-runner.log")).open("w") as log:
    process = subprocess.Popen(command, stdout=log, stderr=subprocess.STDOUT)
    result["pid"] = process.pid
    (logdir/(logprefix+"-launch.json")).write_text(json.dumps(result,indent=2))
    print(json.dumps(result), flush=True)
    try:
        result["exit_code"] = process.wait(timeout=args.seconds)
        result["timed_out"] = False
    except subprocess.TimeoutExpired:
        result["timed_out"] = True
        process.terminate()
        try:result["exit_code"] = process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            process.kill();result["exit_code"] = process.wait()
result["end_unix"] = time.time()
(logdir/(logprefix+"-result.json")).write_text(json.dumps(result,indent=2))
print(json.dumps(result),flush=True)
