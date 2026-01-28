§MODULE[id=m001][name=TypeSystem]

§FUNC[id=f001][name=Main][visibility=public]
  §OUT[type=VOID]
  §EFFECTS[io=console_write]
  §BODY
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"=== OPAL Type System Demo ==="
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:""
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"Testing Option.Some(42)..."
    §END_CALL
    §CALL[target=TestSome][fallible=false]
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:""
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"Testing Option.None()..."
    §END_CALL
    §CALL[target=TestNone][fallible=false]
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:""
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"Testing Result.Ok(100)..."
    §END_CALL
    §CALL[target=TestOk][fallible=false]
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:""
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"Testing Result.Err(error)..."
    §END_CALL
    §CALL[target=TestErr][fallible=false]
    §END_CALL
  §END_BODY
§END_FUNC[id=f001]

§FUNC[id=f002][name=TestSome][visibility=private]
  §OUT[type=VOID]
  §EFFECTS[io=console_write]
  §BODY
    §BIND[name=opt] §SOME INT:42
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"  Created Some(42)"
    §END_CALL
  §END_BODY
§END_FUNC[id=f002]

§FUNC[id=f003][name=TestNone][visibility=private]
  §OUT[type=VOID]
  §EFFECTS[io=console_write]
  §BODY
    §BIND[name=opt] §NONE[type=INT]
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"  Created None"
    §END_CALL
  §END_BODY
§END_FUNC[id=f003]

§FUNC[id=f004][name=TestOk][visibility=private]
  §OUT[type=VOID]
  §EFFECTS[io=console_write]
  §BODY
    §BIND[name=result] §OK INT:100
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"  Created Ok(100)"
    §END_CALL
  §END_BODY
§END_FUNC[id=f004]

§FUNC[id=f005][name=TestErr][visibility=private]
  §OUT[type=VOID]
  §EFFECTS[io=console_write]
  §BODY
    §BIND[name=result] §ERR STR:"Something went wrong"
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"  Created Err(Something went wrong)"
    §END_CALL
  §END_BODY
§END_FUNC[id=f005]

§END_MODULE[id=m001]
