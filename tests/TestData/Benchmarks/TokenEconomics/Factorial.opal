§M[m001:Factorial]
§F[f001:Calculate:pub]
  §I[i32:n]
  §O[i32]
  §IF[if1] §OP[kind=lte] §REF[name=n] 1
    §R 1
  §ELSE
    §B[prev] §C[Calculate] §A §OP[kind=sub] §REF[name=n] 1 §/C
    §R §OP[kind=mul] §REF[name=n] §REF[name=prev]
  §/I[if1]
§/F[f001]
§/M[m001]
