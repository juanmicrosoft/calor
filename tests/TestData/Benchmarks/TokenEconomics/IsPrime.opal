§M[m001:PrimeCheck]
§F[f001:IsPrime:pub]
  §I[i32:n]
  §O[bool]
  §Q §OP[kind=gt] §REF[name=n] 0
  §IF[if1] §OP[kind=lte] §REF[name=n] 1
    §R false
  §/I[if1]
  §IF[if2] §OP[kind=lte] §REF[name=n] 3
    §R true
  §/I[if2]
  §IF[if3] §OP[kind=eq] §OP[kind=mod] §REF[name=n] 2 0
    §R false
  §/I[if3]
  §L[while1:i:3:1000:2]
    §IF[if4] §OP[kind=gt] §OP[kind=mul] §REF[name=i] §REF[name=i] §REF[name=n]
      §R true
    §/I[if4]
    §IF[if5] §OP[kind=eq] §OP[kind=mod] §REF[name=n] §REF[name=i] 0
      §R false
    §/I[if5]
  §/L[while1]
  §R true
§/F[f001]
§/M[m001]
