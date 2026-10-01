#!/usr/bin/env python3
"""Pack a clean source copy and execute a package-only consumer with isolated caches."""

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile
import xml.etree.ElementTree as ET


ROOT = Path(__file__).resolve().parents[1]
VERSION = "1.0.0-stage1-validation"


def run(args, cwd, env, expect_failure=False):
    print("+ " + " ".join(map(str, args)), flush=True)
    result = subprocess.run(list(map(str, args)), cwd=cwd, env=env, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    print(result.stdout, flush=True)
    if expect_failure:
        if result.returncode == 0 or "MapperForge generator assembly is missing" not in result.stdout:
            raise RuntimeError("Pack must explicitly reject a missing generator assembly")
    elif result.returncode:
        raise RuntimeError(f"Command failed with exit code {result.returncode}")


def main():
    with tempfile.TemporaryDirectory(prefix="mapperforge-package-") as temp:
        workspace = Path(temp)
        repo = workspace / "repo"
        repo.mkdir()
        shutil.copytree(ROOT / "src", repo / "src", ignore=shutil.ignore_patterns("bin", "obj"))
        for name in ("Directory.Build.props", "README.md", "LICENSE"):
            shutil.copy2(ROOT / name, repo / name)
        env = dict(os.environ, NUGET_PACKAGES=str(workspace / "build-cache"),
                   NUGET_HTTP_CACHE_PATH=str(workspace / "http-cache"),
                   DOTNET_CLI_TELEMETRY_OPTOUT="1")
        config = workspace / "NuGet.Config"
        config.write_text('<configuration><packageSources><clear />'
                          '<add key="nuget" value="https://api.nuget.org/v3/index.json" />'
                          '</packageSources></configuration>')
        shutil.copy2(config, repo / "NuGet.Config")
        project = repo / "src/MapperForge/MapperForge.csproj"
        common = ["-c", "Release", f"-p:PackageVersion={VERSION}"]

        for mode in ("clean", "no-build"):
            packages = workspace / f"packages-{mode}"
            if mode == "no-build":
                run(["dotnet", "build", project, *common], repo, env)
            run(["dotnet", "pack", project, *common, "-o", packages,
                 *(["--no-build"] if mode == "no-build" else [])], repo, env)
            package = packages / f"MapperForge.{VERSION}.nupkg"
            with zipfile.ZipFile(package) as archive:
                analyzers = [name for name in archive.namelist() if name.startswith("analyzers/")]
                if analyzers != ["analyzers/dotnet/cs/MapperForge.Generator.dll"]:
                    raise RuntimeError(f"Unexpected analyzer contents: {analyzers}")
                if not archive.read(analyzers[0]):
                    raise RuntimeError("Generator assembly is empty")
            print(f"{mode}: exactly one generator DLL in analyzers/dotnet/cs", flush=True)
            consumer = workspace / f"consumer-{mode}"
            shutil.copytree(ROOT / "tests/fixtures/MapperForge.PackageConsumer", consumer,
                            ignore=shutil.ignore_patterns("bin", "obj"))
            consumer_env = dict(env, NUGET_PACKAGES=str(workspace / f"consumer-cache-{mode}"))
            consumer_config = consumer / "NuGet.Config"
            configuration = ET.Element("configuration")
            sources = ET.SubElement(configuration, "packageSources")
            ET.SubElement(sources, "clear")
            ET.SubElement(sources, "add", key="local", value=str(packages))
            ET.SubElement(sources, "add", key="nuget", value="https://api.nuget.org/v3/index.json")
            mapping = ET.SubElement(configuration, "packageSourceMapping")
            ET.SubElement(mapping, "clear")
            ET.SubElement(ET.SubElement(mapping, "packageSource", key="local"), "package", pattern="MapperForge")
            ET.SubElement(ET.SubElement(mapping, "packageSource", key="nuget"), "package", pattern="*")
            ET.ElementTree(configuration).write(consumer_config, encoding="unicode")
            run(["dotnet", "restore", consumer, "--configfile", consumer_config,
                 f"-p:MapperForgePackageVersion={VERSION}"], workspace, consumer_env)
            run(["dotnet", "run", "--project", consumer, "-c", "Release", "--no-restore",
                 f"-p:MapperForgePackageVersion={VERSION}"], workspace, consumer_env)

        # Keep this failure test in the temporary copy; never alter the working tree's build output.
        generator = repo / "src/MapperForge.Generator/bin/Release/netstandard2.0/MapperForge.Generator.dll"
        generator.rename(generator.with_suffix(".missing"))
        failed_output = workspace / "missing-generator"
        run(["dotnet", "pack", project, *common, "--no-build", "-o", failed_output],
            repo, env, expect_failure=True)
        if list(failed_output.glob("*.nupkg")):
            raise RuntimeError("Pack produced a package despite the missing generator")
        print("Package validation passed (clean pack, build/pack --no-build, missing analyzer).", flush=True)


if __name__ == "__main__":
    main()
