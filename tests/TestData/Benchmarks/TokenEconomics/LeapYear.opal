§M[m001:DateUtils]
§F[f001:IsLeapYear:pub]
  §I[i32:year]
  §O[bool]
  §IF[if1] §OP[kind=eq] §OP[kind=mod] §REF[name=year] 400 0
    §R true
  §ELSEIF §OP[kind=eq] §OP[kind=mod] §REF[name=year] 100 0
    §R false
  §ELSEIF §OP[kind=eq] §OP[kind=mod] §REF[name=year] 4 0
    §R true
  §ELSE
    §R false
  §/I[if1]
§/F[f001]
§/M[m001]
