§MODULE[id=m001][name=SdkSample]

§FUNC[id=f001][name=Main][visibility=public]
  §OUT[type=VOID]
  §EFFECTS[io=console_write]
  §BODY
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"=== OPAL SDK Sample ==="
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:""
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"This project was built using the OPAL MSBuild SDK!"
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"The .opal files are automatically compiled to C# during build."
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:""
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"Build command: dotnet build SdkSample.csproj"
    §END_CALL
  §END_BODY
§END_FUNC[id=f001]

§END_MODULE[id=m001]
