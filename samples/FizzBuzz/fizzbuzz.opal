§MODULE[id=m001][name=FizzBuzz]
§FUNC[id=f001][name=Main][visibility=public]
  §OUT[type=VOID]
  §EFFECTS[io=console_write]
  §BODY
    §FOR[id=for1][var=i][from=1][to=100][step=1]
      §IF[id=if1] §OP[kind=EQ] §OP[kind=MOD] §REF[name=i] INT:15 INT:0
        §CALL[target=Console.WriteLine][fallible=false]
          §ARG STR:"FizzBuzz"
        §END_CALL
      §ELSEIF §OP[kind=EQ] §OP[kind=MOD] §REF[name=i] INT:3 INT:0
        §CALL[target=Console.WriteLine][fallible=false]
          §ARG STR:"Fizz"
        §END_CALL
      §ELSEIF §OP[kind=EQ] §OP[kind=MOD] §REF[name=i] INT:5 INT:0
        §CALL[target=Console.WriteLine][fallible=false]
          §ARG STR:"Buzz"
        §END_CALL
      §ELSE
        §CALL[target=Console.WriteLine][fallible=false]
          §ARG §REF[name=i]
        §END_CALL
      §END_IF[id=if1]
    §END_FOR[id=for1]
  §END_BODY
§END_FUNC[id=f001]
§END_MODULE[id=m001]
