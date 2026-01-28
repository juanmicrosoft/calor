§MODULE[id=m001][name=Hello]
§FUNC[id=f001][name=Main][visibility=public]
  §OUT[type=VOID]
  §EFFECTS[io=console_write]
  §BODY
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"Hello from OPAL!"
    §END_CALL
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
