§M[m001:Contracts]

§F[f001:Main:pub]
  §O[void]
  §E[cw]
  §C[Console.WriteLine]
    §A "Testing contracts..."
  §/C
§/F[f001]

§F[f002:Square:pub]
  §I[i32:x]
  §O[i32]
  §Q §OP[kind=gte] §REF[name=x] 0
  §S §OP[kind=gte] §REF[name=result] 0
  §R §OP[kind=mul] §REF[name=x] §REF[name=x]
§/F[f002]

§F[f003:Divide:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §Q[message="divisor must not be zero"] §OP[kind=neq] §REF[name=b] 0
  §R §OP[kind=div] §REF[name=a] §REF[name=b]
§/F[f003]

§/M[m001]
