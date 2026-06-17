# GreenSwamp Alpaca Developer Guide

**2026-06-16 08:15**

## Table of Contents

1. [Purpose of the Solution](#1-purpose-of-the-solution)
2. [Audience and Scope](#2-audience-and-scope)
3. [Development Environment](#3-development-environment)
4. [Getting Started](#4-getting-started)
5. [Solution Structure](#5-solution-structure)
6. [Project Overview](#6-project-overview)
7. [Core Development Workflow](#7-core-development-workflow)
8. [Local Build and Packaging](#8-local-build-and-packaging)
9. [Release Process](#9-release-process)
10. [Configuration and Secrets](#10-configuration-and-secrets)
11. [Testing and Validation](#11-testing-and-validation)
12. [Troubleshooting](#12-troubleshooting)
13. [Contribution Guidelines](#13-contribution-guidelines)
14. [Appendix](#14-appendix)

---

## 1. Purpose of the Solution

GreenSwamp Alpaca is a .NET 10 Blazor server application that hosts Alpaca-compatible telescope endpoints, a browser-based management UI, and the supporting services needed to drive either real SkyWatcher hardware or a simulator. It is designed to let local and network clients connect through the ASCOM Alpaca protocol without requiring a traditional COM-based driver install on every client machine.

### 1.1 What this application does

The application combines three main responsibilities:

- **Expose Alpaca telescope services** through the `ASCOM.Alpaca.Razor` and server projects.
- **Manage telescope state and motion** through the mount-control layer.
- **Provide a web UI** for setup, monitoring, and settings maintenance.

The server is not just an API host. It also includes configuration editors, health checks, log monitoring, and operational pages that make the system usable during both development and deployment.

### 1.2 Intended users

This guide is written for:

- Developers working on the Blazor UI, server host, or shared libraries.
- Maintainers who need to understand the project layout and solution-level build behavior.
- Release engineers who package and publish server binaries or MSI installers.
- Testers who need to validate the simulator, local publish output, or release artifacts.

### 1.3 High-level architecture

The solution is organized around a few layers that move from user-facing UI down to hardware access:

1. **`GreenSwamp.Alpaca.Server`** — the main Blazor web application and ASP.NET Core host.
2. **`ASCOM.Alpaca.Razor`** — shared Alpaca controller, discovery, auth, and helper code.
3. **`GreenSwamp.Alpaca.MountControl`** — mount state, motion, tracking, and low-level control logic.
4. **`GreenSwamp.Alpaca.Mount.SkyWatcher`** — hardware-specific SkyWatcher support.
5. **`GreenSwamp.Alpaca.Mount.Simulator`** — simulator support for development and validation.
6. **`GreenSwamp.Alpaca.Settings`** — versioned settings models, templates, and persistence.
7. **`GreenSwamp.Alpaca.Shared`** and **`GreenSwamp.Alpaca.Principles`** — shared primitives, helpers, and cross-cutting logic.
8. **`GreenSwamp.Alpaca.Installer`** — WiX/MSI packaging for release builds.

---

## 2. Audience and Scope

This document is intentionally developer-focused. It describes the pieces you need to understand to work on the solution, how the projects fit together, and how to get from source code to a local build or release artifact.

### 2.1 Who should use this guide

Use this guide if you are:

- Setting up the repository for the first time.
- Changing the Blazor UI or the server host.
- Working on mount control, simulator behavior, or hardware integration.
- Packaging a release or investigating build output.

### 2.2 What this guide covers

- Prerequisites and environment setup.
- Solution structure and project responsibilities.
- Local development and debugging.
- Local publish and installer creation.
- GitHub release workflow and versioning.
- Configuration, settings, and troubleshooting.

### 2.3 What this guide does not cover

- End-user feature walkthroughs.
- Telescope hardware setup instructions.
- Vendor-specific driver manuals.
- ASCOM protocol reference material beyond what is needed for development.

---

## 3. Development Environment

The solution targets **.NET 10**, so the local SDK and IDE should match that generation. Visual Studio Professional 2026 is the preferred environment for day-to-day work because it supports the Blazor server app, solution files, launch profiles, and WiX project editing in one place.

### 3.1 Required tools

Install the following before working in the repository:

- **.NET 10 SDK** — required to restore, build, and publish the solution.
- **Visual Studio Professional 2026** — preferred IDE for the Blazor app and installer project.
- **Git** — required for MinVer versioning and normal source control workflows.
- **PowerShell** — used by local helper scripts and release tasks.

If you work on installer output, also install the **WiX Toolset v5 extension/tooling** used by the `.wixproj` project.

### 3.2 Visual Studio setup

Use the **ASP.NET and web development** workload for the server application. The solution opens as a `.slnx` file, so no conversion is needed.

Recommended IDE features for this repo:

- Razor/Blazor tooling for `.razor` components.
- XML editing support for `.wxs`, `.props`, `.targets`, and publish profile files.
- Git integration for tag-based versioning and release work.
- WiX project support if you maintain the installer.

The main server project launches with the `GreenSwamp.Alpaca.Server` profile from `Properties\launchSettings.json`. In Development, Visual Studio opens the browser at `http://localhost:31416`.

### 3.3 NuGet dependencies

Restore uses the normal NuGet ecosystem plus the repo's own `NuGet.config` in the server project. The file currently includes the standard NuGet feed and an ASCOM MyGet feed:

- `https://www.nuget.org/api/v2/`
- `https://www.myget.org/F/ascom-initiative/`

Key package references in the solution include:

- `MudBlazor` for the Blazor UI.
- `ASCOM.AstrometryTools` for astronomy calculations.
- `MinVer` for git-tag-based versioning.
- `WixToolset.*` packages for installer builds.
- `Microsoft.Extensions.Hosting.WindowsServices` and `Microsoft.Extensions.Hosting.Systemd` for service hosting.

### 3.4 Optional tooling

Optional tools are useful when you are preparing a release or validating packaged output:

- WiX command-line tooling.
- GitHub CLI for release inspection or manual release work.
- A signing toolchain if MSI signing is enabled in the future.
- Browser developer tools for debugging UI behavior.

---

## 4. Getting Started

The fastest way to get productive is to open the solution in Visual Studio, restore packages, and run the server project with the default development profile.

### 4.1 Clone the repository

Clone the repository as usual and open it at the solution root. The solution file is:

- `GreenSwampAlpaca.slnx`

### 4.2 Open the solution

Open `GreenSwampAlpaca.slnx` in Visual Studio. The solution includes the main server project, shared libraries, the installer project, and the release/support files under **Solution Items**.

### 4.3 Restore dependencies

Let Visual Studio restore packages automatically, or run a restore from the command line if needed:

```powershell
dotnet restore GreenSwampAlpaca.slnx
```

If restore fails, confirm that your network can reach the NuGet feed and the ASCOM package source listed in `GreenSwamp.Alpaca.Server\NuGet.config`.

### 4.4 Build the solution

Build the full solution in **Debug** first. A healthy local build confirms that the .NET 10 SDK, package sources, and project references are all working together.

### 4.5 Run the application locally

Start `GreenSwamp.Alpaca.Server` from Visual Studio using the `GreenSwamp.Alpaca.Server` launch profile. The browser opens automatically at `http://localhost:31416` in Development.

The server itself typically exposes Alpaca services on its configured Alpaca port, while the development web host runs on the launch profile URL. That separation is normal in this solution.

---

## 5. Solution Structure

The solution is organized around a central server app, a set of shared domain and infrastructure libraries, and a WiX installer project. A few solution-level files control versioning, build behavior, and release automation for everything in the repository.

### 5.1 Solution items

The **Solution Items** folder contains files that affect the entire repo rather than a single project:

- `.github/workflows/release.yml` — GitHub Actions release pipeline.
- `BuildRelease.ps1` — local PowerShell helper for publish/package work.
- `Directory.Build.props` — central MinVer and solution-wide MSBuild properties.
- `Directory.Build.targets` — late-bound targets, including WiX version wiring.

### 5.2 Projects in the solution

The solution currently contains these projects:

- `GreenSwamp.Alpaca.Server` — the main Blazor server host and UI.
- `ASCOM.Alpaca.Razor` — shared Alpaca controller and discovery infrastructure.
- `GreenSwamp.Alpaca.MountControl` — mount motion, tracking, and control logic.
- `GreenSwamp.Alpaca.Mount.Commands` — command definitions and command-handling support.
- `GreenSwamp.Alpaca.Mount.SkyWatcher` — SkyWatcher hardware integration.
- `GreenSwamp.Alpaca.Mount.Simulator` — software simulator for development and validation.
- `GreenSwamp.Alpaca.Settings` — settings models, templates, and persistence services.
- `GreenSwamp.Alpaca.Shared` — shared helpers, transport, logging, and utilities.
- `GreenSwamp.Alpaca.Principles` — common math, coordinate, time, and timing primitives.
- `GreenSwamp.Alpaca.Installer` — WiX installer that packages the published server output.

### 5.3 Platform and configuration notes

- The solution targets **net10.0**.
- The installer project is built with WiX Toolset v7 and uses `x64` / `x86` platform values rather than `win-x64` / `win-x86`.
- The release workflow publishes Windows and Linux artifacts from the same source tree.
- MinVer derives version numbers from git tags that start with `v`.

---

## 6. Project Overview

### 6.1 `GreenSwamp.Alpaca.Server`

This is the main application project and the entry point for developers. It is an ASP.NET Core web host that uses Blazor components for the UI and includes pages, controllers, services, middleware, and a telescope driver layer.

Notable areas in this project include:

- `Pages/` — Blazor pages such as Mount Status, Mount Settings, Monitor, Settings Explorer, Setup, and health-check screens.
- `Components/` — reusable dialogs, settings editors, and UI helpers.
- `Controllers/` — backup, configuration, and device setup endpoints.
- `Services/` — runtime state, device registry, and monitor services.
- `TelescopeDriver/` — the Alpaca telescope-facing implementation.
- `Properties/PublishProfiles/` — local publish profiles such as `win-x64.pubxml`.

### 6.2 `ASCOM.Alpaca.Razor`

This project contains the shared Alpaca web infrastructure used by the server and other ASCOM device types. It includes controller base classes, discovery support, login/auth helpers, logging adapters, and reusable UI pieces such as the login control.

### 6.3 `GreenSwamp.Alpaca.MountControl`

This library contains the core mount behavior: initialization, lifecycle, motion, position, tracking, serial communication, auto-home logic, pulse guiding, and coordinate transforms. It is the central domain layer for mount control and is intentionally separated from the web UI.

### 6.4 `GreenSwamp.Alpaca.Mount.Commands`

This project groups command-related types used by the mount-control stack. Treat it as the place for command definitions, command parsing, and command coordination helpers that are shared between mount implementations.

### 6.5 `GreenSwamp.Alpaca.Mount.SkyWatcher`

This project implements the SkyWatcher hardware path. It is the code path to use when the server talks to a physical mount over serial / USB-serial using the SkyWatcher protocol.

### 6.6 `GreenSwamp.Alpaca.Mount.Simulator`

This project provides a software-only mount implementation for development, regression testing, and UI validation when hardware is not available.

### 6.7 `GreenSwamp.Alpaca.Settings`

This project owns the versioned settings system. It includes settings models, template JSON, schema files, settings backups, and the services used to load and save user settings.

### 6.8 `GreenSwamp.Alpaca.Shared`

This project holds shared infrastructure that is needed by more than one assembly: transport helpers, serial helpers, monitor buffers, environment helpers, numeric helpers, and general-purpose utilities.

### 6.9 `GreenSwamp.Alpaca.Principles`

This project contains foundational domain primitives such as time, coordinate, unit, range, media timer, and conversion helpers. These types support the telescope math and timing logic used throughout the solution.

### 6.10 `GreenSwamp.Alpaca.Installer`

This project builds the WiX installer. It consumes published server output and bundles it into MSI packages with custom UI resources, upgrade metadata, and platform-specific build settings.

---

## 7. Core Development Workflow

### 7.1 Debugging locally

For normal feature work, start the `GreenSwamp.Alpaca.Server` launch profile in Visual Studio and use the browser-based UI to exercise the app. The simulator is the safest starting point when you need to validate behavior without hardware attached.

Typical debugging tasks include:

- Watching Blazor UI state changes.
- Inspecting controller responses.
- Verifying mount state transitions.
- Checking monitor output and configuration edits.

### 7.2 Editing and rebuilding

The preferred workflow is small and incremental:

1. Make a focused change.
2. Rebuild the affected project or the solution.
3. Run the server again.
4. Validate the feature in the browser.

When a change affects the mount control layer or settings service, review sibling classes and related partial files before assuming logic is isolated.

### 7.3 Working with configuration

Configuration comes from a combination of application JSON, versioned user settings, and runtime state. The settings project owns templates and schemas, while the server project exposes the editors and validation UI.

Keep the following separation in mind:

- **App configuration** — checked-in defaults and schema-backed settings.
- **User settings** — machine-specific and versioned.
- **Runtime state** — telemetry, monitor buffers, and active device state.

### 7.4 Common developer tasks

- Add or update a Blazor component.
- Adjust a settings editor or a schema-backed setting.
- Change how a mount command is translated or validated.
- Test the simulator path before touching physical hardware code.
- Update release or installer metadata when packaging behavior changes.

---

## 8. Local Build and Packaging

### 8.1 Build for local debugging

The normal debug build is the full solution build from Visual Studio or from the command line:

```powershell
dotnet build GreenSwampAlpaca.slnx -c Debug
```

A successful build confirms that the solution can restore, compile, and link the server, shared libraries, and installer project together.

### 8.2 Local publish

The server project includes a Windows publish profile at `GreenSwamp.Alpaca.Server\Properties\PublishProfiles\win-x64.pubxml`. That profile publishes to `GreenSwamp.Alpaca.Server\publish\win-x64\`, uses `net10.0`, and produces a single-file Windows x64 output.

For manual publish work, use the same project and runtime settings as the profile:

```powershell
dotnet publish GreenSwamp.Alpaca.Server/GreenSwamp.Alpaca.Server.csproj `
  -c Release `
  -r win-x64 `
  --self-contained false `
  -p:PublishSingleFile=true `
  -o GreenSwamp.Alpaca.Server/publish/win-x64
```

Use the published output when you want to inspect the deployed file set or feed the installer project.

### 8.3 Local installer build

The installer project is `GreenSwamp.Alpaca.Installer\GreenSwamp.Alpaca.Installer.wixproj`. It packages the published server output into a WiX-built MSI and is designed to work in two modes:

1. **Self-contained local packaging** — let the WiX project publish the server first. Use VS project rebuild  or clean / build
2. **Prepublished packaging** — point WiX at an existing `publish\<RID>\` folder and skip the publish step.

The project defaults and release pipeline are based on the following values from the `.wixproj` file:

- `Platform=x64` or `x86` to select the MSI family.
- `PublishRid` derived from the platform.
- `PublishDir` defaulting to `..\publish\$(PublishRid)\` when not supplied.
- `UpgradeCode` identifying the product family for upgrades.
- `OutputName` set to `GreenSwamp.Alpaca.Server_$(Platform)`.
- `SuppressValidation=true` and selected ICE suppressions for local developer builds.

A local build that reuses an already published server output typically looks like this:

```powershell
dotnet build GreenSwamp.Alpaca.Installer/GreenSwamp.Alpaca.Installer.wixproj `
  -c Release `
  -p:Platform=x64 `
  -p:UpgradeCode=0BFB5E9F-4475-4BFA-A8A5-1F26CD982B1C `
  -p:PublishDir="$(Resolve-Path .\GreenSwamp.Alpaca.Server\publish\win-x64)\" `
  -p:SkipPublish=true `
  -p:BuildProjectReferences=false
```

If you want the installer project to publish the server for you, omit `SkipPublish=true` and let the `_PublishServerBeforeWix` target run.

### 8.4 Windows installer options

The WiX project is set up to produce a standard MSI with platform-specific upgrade tracking.

Key packaging options:

- **x64 / x86 package families** — the installer uses different `UpgradeCode` values for each architecture.
- **Prepublished input** — CI and release builds reuse the server publish output rather than rebuilding it twice.
- **Local dev validation** — validation is suppressed for convenience on non-elevated developer machines.
- **Custom branding** — banner, dialog, icon, and license resources are bundled from `GreenSwamp.Alpaca.Installer\Resources`.

For install-time behavior, the package is intended to install the server binaries into the application folder, create the expected shortcuts/resources, and preserve a stable upgrade path across releases.

### 8.5 Verifying local artifacts

After publish or packaging, check the following locations:

- `GreenSwamp.Alpaca.Server\publish\win-x64\` — publish output.
- `GreenSwamp.Alpaca.Installer\bin\Release\` — WiX build output.
- `publish\` under the repo root if you are following the release workflow.

Verify that the main EXE, static web assets, configuration files, and MSI outputs are present before handing the build off to someone else.

---

## 9. Release Process

Release automation is driven by git tags, MinVer, GitHub Actions, and the installer/publish artifacts produced from the same source tree.

### 9.1 Versioning strategy

Version numbers are derived from git tags with the prefix `v`.

- `v1.2.3` becomes version `1.2.3`.
- Commits ahead of the tag get a pre-release suffix such as `1.2.3-dev.4`.
- `Directory.Build.props` and `Directory.Build.targets` centralize the MinVer settings used by the solution.

### 9.2 How tags trigger CI/CD

The CI/CD pipeline is triggered by **pushing a tag**, not just creating one locally.

A typical beta or release flow looks like this:

```powershell
git tag -a v0.1.4-beta -m "Beta release version 0.1.4"
git push origin v0.1.4-beta
```

When the tag reaches GitHub, the workflow in `.github/workflows/release.yml` starts because it listens for `push` events whose ref matches `v*`.

Important details:

- `git tag -a` creates an annotated tag locally.
- `git push origin v0.1.4-beta` is the step that actually triggers GitHub Actions.
- Pull requests only run the validation path; they do not publish release assets.
- `workflow_dispatch` can be used to run the workflow manually if needed.

### 9.3 GitHub release automation

The release workflow lives in `.github/workflows/release.yml` and is split into these jobs:

- **publish** — builds publish output for `win-x64`, `win-x86`, `linux-x64`, `linux-arm64`, and `linux-arm`.
- **build-deb** — packages the Linux publish output into Debian packages.
- **build-msi** — packages the Windows publish output into MSI installers.
- **release** — creates or updates the GitHub Release and uploads all packaged artifacts.

The workflow uses `fetch-depth: 0` so MinVer can inspect the full git history and derive the same version locally and in CI.

### 9.4 Release artifacts

Expected release artifacts include:

- Published server output for each runtime identifier.
- MSI packages for Windows x64 and x86.
- DEB packages for supported Linux targets.
- GitHub Release notes and attached assets.

The Windows installer artifacts come from the WiX project. The Linux artifacts come from `Installer/Linux/build-deb.sh` and its supporting packaging files.

### 9.5 Manual release checklist

- Create the annotated git tag.
- Push the tag to `origin`.
- Confirm GitHub Actions starts automatically.
- Verify the publish, MSI, DEB, and release jobs succeed.
- Download and inspect the release assets.
- Confirm the release page shows the intended version number.

---

## 10. Configuration and Secrets

### 10.1 Application configuration

The server uses checked-in configuration files and schema files for the baseline configuration experience.

Relevant files include:

- `GreenSwamp.Alpaca.Server\appsettings.json`
- `GreenSwamp.Alpaca.Server\appsettings.Development.json`
- `GreenSwamp.Alpaca.Server\appsettings.schema.json`
- `GreenSwamp.Alpaca.Server\NuGet.config`
- `GreenSwamp.Alpaca.Settings\Templates\*.json`

The settings project also owns the versioned settings services and backup behavior, so application settings should be changed through the normal UI and service path rather than by editing generated files directly.

### 10.2 Sensitive values

Do not commit machine-specific or secret values. Treat the following as sensitive or environment-specific:

- API keys or authentication secrets.
- Signing certificates and certificate passwords.
- User-specific settings files.
- Temporary publish or build output.

If a value must vary by machine, keep it outside source control and document the expected location instead.

### 10.3 Release signing

The repository currently has signing-related placeholders in the workflow and local scripts, but signing is not part of the everyday development loop. If signing is enabled later, the documented process should include:

- Restoring the certificate from a secure secret store.
- Importing it into the build environment only for the release job.
- Signing the MSI after packaging and before publishing the GitHub Release.
- Removing the temporary certificate material immediately after use.

### 10.4 Linux package configuration

The Debian packaging files under `Installer/Linux` are part of the release story and should be kept in sync with the server runtime:

- `build-deb.sh` — stages publish output and assembles the `.deb` package.
- `debian/control.template` — package metadata template filled in at build time.
- `debian/postinst` — creates the system user, prepares `/var/lib/greenswamp-alpaca`, and enables the service.
- `debian/prerm` — stops and disables the service before removal.
- `debian/postrm` — refreshes caches and removes shared files during purge.
- `systemd/greenswamp-alpaca.service` — runs the server as the `greenswamp` user.
- `greenswamp-alpaca.desktop` — adds a desktop launcher that opens the local web UI.
- `astronomy.directory` — places the launcher in an Astronomy menu category.
- `greenswamp1.png` — desktop icon used by the launcher and menus.

These files describe both the package layout and the install-time behavior, so changes to the server's start-up path, port, or service identity should be reflected here as well.

---

## 11. Testing and Validation

The solution currently relies on build validation and manual smoke testing for most developer workflows. Add automated tests where a change is stable enough to verify repeatedly, but do not skip the basic build and runtime checks.

### 11.1 Build validation

Before merging changes, confirm that:

- The solution restores successfully.
- The solution builds in Debug.
- Any touched project also builds in Release if the change affects packaging or release output.
- The installer project still compiles when publish output is available.

### 11.2 Functional smoke tests

Use the following smoke tests after a local run or publish:

- The Blazor UI loads in the browser.
- The server starts with the Development launch profile.
- The simulator device appears and responds.
- Mount Status and Settings pages render without errors.
- Monitor output and settings editing behave normally.

### 11.3 Installer validation

When validating installer output, check that:

- The MSI installs successfully.
- The application starts after installation.
- The expected files land in the install location.
- The installed service uses the expected user and working directory.
- Upgrade behavior preserves the correct version family.
- Uninstall removes the installed product cleanly.

### 11.4 Release validation

For release assets, confirm that:

- The published artifacts match the intended runtime identifiers.
- The MSI and DEB assets are attached to the GitHub Release.
- The release version matches the git tag.
- The assets you downloaded are the ones generated by the workflow.
- The package metadata reflects the intended release channel and architecture.

---

## 12. Troubleshooting

### 12.1 Restore problems

If restore fails, check the following first:

- The `.NET 10` SDK is installed.
- `GreenSwamp.Alpaca.Server\NuGet.config` still points to valid feeds.
- The machine can reach `nuget.org` and the ASCOM feed.

### 12.2 Build failures

If a build fails, verify:

- You are building the correct configuration and platform.
- The solution-level `Directory.Build.props` and `Directory.Build.targets` files are present.
- MinVer can read git history and tags.

### 12.3 Publish failures

If publishing fails:

- Confirm the target runtime identifier matches the publish profile or command line.
- Verify that any `PublishDir` path exists and is writable.
- Check whether the output is framework-dependent or self-contained.

### 12.4 Installer build failures

If the MSI build fails:

- Make sure publish output exists before the WiX build starts.
- Verify the WiX platform setting is `x64` or `x86`.
- Confirm the `UpgradeCode` matches the intended product family.
- Check the WiX extension package references in the installer project.
- Confirm the installer is consuming the same publish folder that the server build produced.

### 12.5 Debian package build failures

If the `.deb` build fails, inspect the Linux packaging files under `Installer/Linux`:

- `build-deb.sh` must be executable and must be run from the repo root so it can find `publish/<RID>/`.
- The RID must be one of `linux-x64`, `linux-arm64`, or `linux-arm`.
- The publish output must already exist before the script runs.
- The `debian/control.template` placeholders must still match the values produced by the script.
- The maintainer scripts in `debian/` must remain executable.
- The systemd unit must point to the published executable at `/opt/greenswamp/alpaca-server/GreenSwamp.Alpaca.Server`.

### 12.6 GitHub Actions failures

If the release workflow fails, inspect the job that failed first:

- `publish` for restore or cross-RID publish issues.
- `build-deb` for Linux packaging problems.
- `build-msi` for Windows installer packaging problems.
- `release` for GitHub Release permissions or asset upload issues.

### 12.7 Versioning problems

If the version number looks wrong, confirm that:

- The tag starts with `v`.
- The tag is reachable from the current commit.
- `fetch-depth: 0` is enabled in the workflow.
- MinVer is resolving the same history in CI and locally.

### 12.8 Common environment issues

Common local setup issues include:

- Opening the solution with an older SDK.
- Missing WiX support when editing the installer project.
- Stale publish output from a previous build.
- Launching the wrong profile in Visual Studio.

---

## 13. Contribution Guidelines

### 13.1 Branching and workflow

Use short-lived branches for focused changes. Keep the scope small enough that a reviewer can understand the effect on the server, shared libraries, or installer without reading unrelated work.

### 13.2 Commit message style

Use concise, conventional commit messages such as:

- `feat: add mount status refresh control`
- `fix: prevent invalid settings save`
- `docs: expand developer guide`

### 13.3 Pull request expectations

A good pull request should include:

- A clear description of what changed and why.
- Any screenshots or reproduction steps for UI changes.
- Notes about release, installer, or settings impacts.
- Updates to related documentation when behavior changes.

### 13.4 Code review checklist

Reviewers should look for:

- Minimal, targeted edits.
- Correct handling of .NET 10 / Blazor conventions.
- No accidental changes to generated or release files.
- Consistent behavior across related code paths.

### 13.5 Documentation updates

If you change a user-facing workflow, configuration setting, build step, or release step, update this guide or the user guide at the same time.

---

## 14. Appendix

### 14.1 Useful paths

- `GreenSwamp.Alpaca.Server\Program.cs` — application entry point.
- `GreenSwamp.Alpaca.Server\Pages\` — Blazor pages and route components.
- `GreenSwamp.Alpaca.Server\Components\` — reusable UI components and dialogs.
- `GreenSwamp.Alpaca.Server\Properties\launchSettings.json` — local run profiles.
- `GreenSwamp.Alpaca.Server\Properties\PublishProfiles\` — local publish profiles.
- `GreenSwamp.Alpaca.Server\docs\USER-GUIDE.md` — user-facing documentation.
- `GreenSwamp.Alpaca.Server\docs\API-REFERENCE.md` — API reference material.

### 14.2 Common commands

```powershell
dotnet restore GreenSwampAlpaca.slnx
dotnet build GreenSwampAlpaca.slnx -c Debug
dotnet publish GreenSwamp.Alpaca.Server/GreenSwamp.Alpaca.Server.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o GreenSwamp.Alpaca.Server/publish/win-x64
dotnet build GreenSwamp.Alpaca.Installer/GreenSwamp.Alpaca.Installer.wixproj -c Release -p:Platform=x64 -p:SkipPublish=true
```

### 14.3 Build and release references

- `.github/workflows/release.yml`
- `BuildRelease.ps1`
- `Directory.Build.props`
- `Directory.Build.targets`
- `GreenSwamp.Alpaca.Server\Properties\PublishProfiles\win-x64.pubxml`

### 14.4 External documentation links

- [ASCOM Standards](https://ascom-standards.org)
- [Blazor documentation](https://learn.microsoft.com/aspnet/core/blazor)
- [WiX Toolset documentation](https://wixtoolset.org/)
