# SDK sample

Run `dotnet run --project samples/SdkSample/SdkSample.csproj` from the repository root.
The project uses the real Calor MSBuild task to compile the linked current-syntax
source at `tests/TestData/ActiveSyntax/samples/SdkSample/program.calr`. Edit that
file to change the executable. Its tooling-owned declaration IDs live beside it
in `.calor/ids.json`.

The local `program.calr` is a frozen historical bound-type replay input, not a
build input. Keep its original bytes unchanged; the historical replay runs it
with its original compiler.
