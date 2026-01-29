§M[m001:MathPower]
§F[f001:Power:pub]
  §I[i32:base]
  §I[i32:exp]
  §O[i32]
  §Q §OP[kind=gte] §REF[name=exp] 0
  §IF[if1] §OP[kind=eq] §REF[name=exp] 0
    §R 1
  §ELSEIF §OP[kind=eq] §REF[name=exp] 1
    §R §REF[name=base]
  §ELSE
    §B[prev] §C[Power] §A §REF[name=base] §A §OP[kind=sub] §REF[name=exp] 1 §/C
    §R §OP[kind=mul] §REF[name=base] §REF[name=prev]
  §/I[if1]
§/F[f001]
§/M[m001]
