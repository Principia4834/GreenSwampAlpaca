# 1. Publish the app for win-x64 first
dotnet publish GreenSwamp.Alpaca.Server/GreenSwamp.Alpaca.Server.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64

# 2. Build the MSI (x64)
dotnet build GreenSwamp.Alpaca.Installer/GreenSwamp.Alpaca.Installer.wixproj -c Release -p:Platform=win-x64 -p:UpgradeCode="0BFB5E9F-4475-4BFA-A8A5-1F26CD982B1C" "-p:PublishDir=$PWD/publish/win-x64/"

# 3. The MSI will be at:
#    Installer\Windows\bin\win-x64\Release\GreenSwamp.Alpaca.Server_<version>_win-x64.msi
#    Install it and verify the service starts and the Start Menu shortcut opens localhost:31426