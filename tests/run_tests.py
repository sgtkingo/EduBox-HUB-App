"""Compile and run VSCP App integration checks using local .NET Framework tools."""
from pathlib import Path
import os
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
compiler = shutil.which("csc")
if not compiler:
    candidates = Path(os.environ.get("ProgramFiles(x86)", "C:/Program Files (x86)")) / "Microsoft Visual Studio"
    compiler = next(candidates.glob("*/BuildTools/MSBuild/Current/Bin/Roslyn/csc.exe"), None)
if not compiler:
    raise SystemExit("Install Visual Studio Build Tools or put csc on PATH.")

dependencies = list((ROOT / "packages").glob("*/lib/net4*/*.dll"))
sources = [ROOT / "NewGUI" / name for name in (
    "VscpProtocol.cs", "SerialParser.cs", "SerialManager.cs",
    "SerialController.cs", "VirtualDeviceSimulator.cs", "Komponenty.cs", "RequestBuilder.cs",
)]
with tempfile.TemporaryDirectory(prefix="edubox-app-vscp-") as directory:
    output = Path(directory) / "VscpProtocolTests.exe"
    subprocess.run([
        str(compiler), "/nologo", "/target:exe", f"/out:{output}",
        "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll", "/r:System.Core.dll",
        *(f"/r:{path}" for path in dependencies),
        *map(str, sources), str(ROOT / "tests/VscpProtocolTests.cs"),
    ], check=True)
    for path in dependencies:
        shutil.copy2(path, Path(directory) / path.name)
    subprocess.run([str(output)], check=True, timeout=15)
