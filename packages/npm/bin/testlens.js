#!/usr/bin/env node
"use strict";

/*
 * npm wrapper for TestLens.
 * The tool itself is a .NET 8 application (dist/testlens.dll); this script
 * locates the .NET runtime and forwards all arguments to it.
 */

const { spawn, spawnSync } = require("child_process");
const { existsSync } = require("fs");
const path = require("path");

const dll = path.join(__dirname, "..", "dist", "testlens.dll");

if (!existsSync(dll)) {
  console.error("testlens: dist/testlens.dll is missing from this installation.");
  console.error("Try reinstalling: npm install -g testlens-cli");
  process.exit(1);
}

const probe = spawnSync("dotnet", ["--list-runtimes"], { encoding: "utf8" });
if (probe.error || probe.status !== 0) {
  console.error("testlens: the .NET runtime was not found on your PATH.");
  console.error("");
  console.error("TestLens runs on .NET 8. Install the (free, cross-platform) runtime from:");
  console.error("  https://dotnet.microsoft.com/download/dotnet/8.0");
  console.error("");
  console.error("Alternatively, install TestLens as a dotnet tool instead:");
  console.error("  dotnet tool install --global TestLens");
  process.exit(1);
}

const runtimes = [...probe.stdout.matchAll(/Microsoft\.NETCore\.App (\d+)\./g)].map((m) => +m[1]);
if (!runtimes.some((major) => major >= 8)) {
  console.error("testlens: a .NET 8 (or newer) runtime is required, but only older runtimes were found.");
  console.error("Install it from https://dotnet.microsoft.com/download/dotnet/8.0");
  process.exit(1);
}

const child = spawn("dotnet", [dll, ...process.argv.slice(2)], { stdio: "inherit" });
child.on("exit", (code, signal) => process.exit(signal ? 1 : code ?? 1));
child.on("error", (err) => {
  console.error("testlens: failed to start dotnet: " + err.message);
  process.exit(1);
});
