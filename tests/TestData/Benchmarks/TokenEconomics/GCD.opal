§M[m001:MathUtils]
§F[f001:GCD:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §IF[if1] §OP[kind=eq] §REF[name=b] 0
    §R §REF[name=a]
  §ELSE
    §R §C[GCD] §A §REF[name=b] §A §OP[kind=mod] §REF[name=a] §REF[name=b] §/C
  §/I[if1]
§/F[f001]
§/M[m001]
