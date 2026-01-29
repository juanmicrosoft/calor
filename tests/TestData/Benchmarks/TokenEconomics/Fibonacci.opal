§M[m001:Fibonacci]
§F[f001:Calculate:pub]
  §I[i32:n]
  §O[i32]
  §IF[if1] §OP[kind=lte] §REF[name=n] 1
    §R §REF[name=n]
  §ELSE
    §B[a] §C[Calculate] §A §OP[kind=sub] §REF[name=n] 1 §/C
    §B[b] §C[Calculate] §A §OP[kind=sub] §REF[name=n] 2 §/C
    §R §OP[kind=add] §REF[name=a] §REF[name=b]
  §/I[if1]
§/F[f001]
§/M[m001]
