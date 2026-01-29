§M[m001:FizzBuzz]
§F[f001:Run:pub]
  §O[void]
  §E[cw]
  §L[for1:i:1:100:1]
    §IF[if1] §OP[kind=eq] §OP[kind=mod] §REF[name=i] 15 0
      §C[Console.WriteLine]
        §A "FizzBuzz"
      §/C
    §ELSEIF §OP[kind=eq] §OP[kind=mod] §REF[name=i] 3 0
      §C[Console.WriteLine]
        §A "Fizz"
      §/C
    §ELSEIF §OP[kind=eq] §OP[kind=mod] §REF[name=i] 5 0
      §C[Console.WriteLine]
        §A "Buzz"
      §/C
    §ELSE
      §C[Console.WriteLine]
        §A §REF[name=i]
      §/C
    §/I[if1]
  §/L[for1]
§/F[f001]
§/M[m001]
