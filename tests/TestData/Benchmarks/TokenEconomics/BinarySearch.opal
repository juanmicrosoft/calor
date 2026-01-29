§M[m001:Search]
§F[f001:Find:pub]
  §I[i32:target]
  §I[i32:val1]
  §I[i32:val2]
  §I[i32:val3]
  §O[i32]
  §IF[if1] §OP[kind=eq] §REF[name=val1] §REF[name=target]
    §R 0
  §ELSEIF §OP[kind=eq] §REF[name=val2] §REF[name=target]
    §R 1
  §ELSEIF §OP[kind=eq] §REF[name=val3] §REF[name=target]
    §R 2
  §ELSE
    §R -1
  §/I[if1]
§/F[f001]
§/M[m001]
