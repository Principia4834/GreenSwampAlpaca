# .NET 8.0 Upgrade Plan

## Execution Steps

Execute steps below sequentially one by one in the order they are listed.

1. Validate that an .NET 8.0 SDK required for this upgrade is installed on the machine and if not, help to get it installed.
2. Ensure that the SDK version specified in global.json files is compatible with the .NET 8.0 upgrade.
3. Upgrade GreenSwamp.Principles\GreenSwamp.Principles.csproj
4. Upgrade GreenSwamp.Shared\GreenSwamp.Shared.csproj

## Settings

This section contains settings and data used by execution steps.

### Excluded projects

| Project name                                   | Description                 |
|:-----------------------------------------------|:---------------------------:|

### Aggregate NuGet packages modifications across all projects

NuGet packages used across all selected projects or their dependencies that need version update in projects that reference them.

| Package Name                        | Current Version | New Version | Description                                   |
|:------------------------------------|:---------------:|:-----------:|:----------------------------------------------|
| ASCOM7.BuildEnvironment.Support      |   7.0.0         |             | No supported version for .NET 8.0, must be removed |
| System.Data.DataSetExtensions        |   4.5.0         |             | Functionality included with framework reference |
| System.Net.Http                      |   4.3.4         |             | Functionality included with framework reference |

### Project upgrade details

#### GreenSwamp.Principles\GreenSwamp.Principles.csproj modifications

Project properties changes:
  - Target framework should be changed from `net472` to `net8.0`

NuGet packages changes:
  - ASCOM7.BuildEnvironment.Support should be removed (no supported version for .NET 8.0)
  - System.Data.DataSetExtensions should be removed (functionality included with framework reference)
  - System.Net.Http should be removed (functionality included with framework reference)

Other changes:
  - Review code for any direct usage of removed packages and update as needed.

#### GreenSwamp.Shared\GreenSwamp.Shared.csproj modifications

Project properties changes:
  - Target framework should be changed from `net472` to `net8.0-windows`

NuGet packages changes:
  - ASCOM7.BuildEnvironment.Support should be removed (no supported version for .NET 8.0)

Other changes:
  - Review code for any direct usage of removed package and update as needed.
