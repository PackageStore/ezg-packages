#!/usr/bin/env node
import fs from "node:fs"
import os from "node:os"
import path from "node:path"
import { spawnSync } from "node:child_process"
import { fileURLToPath } from "node:url"

const packagesRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..")
const project = path.resolve(
  process.env.EZG_UNITY_PROJECT || path.join(packagesRoot, "..", "archer-incremental")
)
const csprojPath = path.join(project, "UnityFigmaBridgeEditor.csproj")
const runtimeDll = path.join(project, "Library", "ScriptAssemblies", "UnityFigmaBridgeRuntime.dll")
const editorSources = path.join(
  packagesRoot,
  "packages",
  "com.ezg.figma-bridge",
  "UnityFigmaBridge",
  "Editor",
  "**",
  "*.cs"
)

const xmlEscape = (s) => s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;")

function missing(what, hint) {
  console.error(`check-figma-bridge-cs: ${what}`)
  console.error(hint)
  process.exit(2)
}

if (spawnSync("dotnet", ["--version"], { stdio: "ignore" }).error) {
  missing("dotnet not found on PATH.", "Install the .NET SDK (https://dotnet.microsoft.com/download).")
}
if (!fs.existsSync(csprojPath)) {
  missing(
    `missing ${csprojPath}`,
    "Unity generates it. Open the project in Unity once with an IDE package installed (Visual Studio or VS Code), or set EZG_UNITY_PROJECT."
  )
}
if (!fs.existsSync(runtimeDll)) {
  missing(
    `missing ${runtimeDll}`,
    "Unity writes it on compile. Open the project in Unity once and let it compile."
  )
}

const rewrite = (text) =>
  text
    .replace(/<Compile\b[^>]*\/>/g, "")
    .replace(/<Compile\b[^>]*[^/]>[\s\S]*?<\/Compile>/g, "")
    .replace(/<HintPath>(Library\/[^<]*)<\/HintPath>/g, (_, rel) => {
      return `<HintPath>${xmlEscape(path.join(project, rel))}</HintPath>`
    })
    .replace(
      /<ProjectReference\s+Include="UnityFigmaBridgeRuntime\.csproj"\s*\/>/,
      `<Reference Include="UnityFigmaBridgeRuntime"><HintPath>${xmlEscape(runtimeDll)}</HintPath><Private>False</Private></Reference>`
    )
    .replace(
      /<BaseIntermediateOutputPath>[^<]*<\/BaseIntermediateOutputPath>/,
      "<BaseIntermediateOutputPath>obj/</BaseIntermediateOutputPath>"
    )
    .replace(/<OutputPath>[^<]*<\/OutputPath>/, "<OutputPath>bin/</OutputPath>")
    .replace(
      /(<ItemGroup>)(\s*<ProjectCapability Include="Unity" \/>)/,
      `<ItemGroup><Compile Include="${xmlEscape(editorSources)}" /></ItemGroup>\n  $1$2`
    )

const csproj = rewrite(fs.readFileSync(csprojPath, "utf8"))
if (!csproj.includes(xmlEscape(editorSources))) {
  missing("could not add the Compile item to the csproj.", "Unity's csproj layout changed; update this script.")
}

const dir = fs.mkdtempSync(path.join(os.tmpdir(), "figma-bridge-cs-"))
let code = 1
try {
  const file = path.join(dir, "Check.csproj")
  fs.writeFileSync(file, csproj)
  const result = spawnSync("dotnet", ["build", file, "-nologo", "-v", "q", "-clp:ErrorsOnly"], {
    cwd: dir,
    encoding: "utf8",
  })
  if (result.error) {
    console.error(String(result.error))
  } else {
    process.stdout.write(result.stdout || "")
    process.stderr.write(result.stderr || "")
    code = result.status ?? 1
  }
} finally {
  fs.rmSync(dir, { recursive: true, force: true })
}
process.exit(code)
