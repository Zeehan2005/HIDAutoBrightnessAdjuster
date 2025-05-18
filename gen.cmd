dotnet clean
dotnet restore
dotnet build
dotnet publish -c Release -r win-x64
dotnet run