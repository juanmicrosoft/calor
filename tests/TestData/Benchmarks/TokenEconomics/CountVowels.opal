§M[m001:StringAnalysis]
§F[f001:IsVowel:pub]
  §I[str:ch]
  §O[bool]
  §IF[if1] §OP[kind=eq] §REF[name=ch] "a"
    §R true
  §ELSEIF §OP[kind=eq] §REF[name=ch] "e"
    §R true
  §ELSEIF §OP[kind=eq] §REF[name=ch] "i"
    §R true
  §ELSEIF §OP[kind=eq] §REF[name=ch] "o"
    §R true
  §ELSEIF §OP[kind=eq] §REF[name=ch] "u"
    §R true
  §ELSE
    §R false
  §/I[if1]
§/F[f001]
§/M[m001]
