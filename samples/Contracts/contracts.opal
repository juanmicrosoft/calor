§MODULE[id=m001][name=Contracts]

§FUNC[id=f001][name=Main][visibility=public]
  §OUT[type=VOID]
  §EFFECTS[io=console_write]
  §BODY
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"=== OPAL Contracts Demo ==="
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:""
    §END_CALL

    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"Testing Square(5) - has REQUIRES x >= 0 and ENSURES result >= 0"
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"  If precondition fails, throws ArgumentException"
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"  If postcondition fails, throws InvalidOperationException"
    §END_CALL

    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:""
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"Testing Divide(10, 2) - has REQUIRES b != 0 with custom message"
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"  Custom message: divisor must not be zero"
    §END_CALL

    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:""
    §END_CALL
    §CALL[target=Console.WriteLine][fallible=false]
      §ARG STR:"Contracts are enforced at runtime with clear error messages."
    §END_CALL
  §END_BODY
§END_FUNC[id=f001]

§FUNC[id=f002][name=Square][visibility=public]
  §IN[name=x][type=INT]
  §OUT[type=INT]
  §REQUIRES §OP[kind=gte] §REF[name=x] INT:0
  §ENSURES §OP[kind=gte] §REF[name=result] INT:0
  §BODY
    §RETURN §OP[kind=mul] §REF[name=x] §REF[name=x]
  §END_BODY
§END_FUNC[id=f002]

§FUNC[id=f003][name=Divide][visibility=public]
  §IN[name=a][type=INT]
  §IN[name=b][type=INT]
  §OUT[type=INT]
  §REQUIRES[message="divisor must not be zero"] §OP[kind=neq] §REF[name=b] INT:0
  §BODY
    §RETURN §OP[kind=div] §REF[name=a] §REF[name=b]
  §END_BODY
§END_FUNC[id=f003]

§END_MODULE[id=m001]
