§M[m001:PrimeCheck]
§F[f001:IsPrime:pub]
  §I[i32:n]
  §O[bool]
  §Q (> n 0)
  §IF[if1] (<= n 1) → §R false
  §/I[if1]
  §IF[if2] (<= n 3) → §R true
  §/I[if2]
  §IF[if3] (== (% n 2) 0) → §R false
  §/I[if3]
  §L[while1:i:3:1000:2]
    §IF[if4] (> (* i i) n) → §R true
    §/I[if4]
    §IF[if5] (== (% n i) 0) → §R false
    §/I[if5]
  §/L[while1]
  §R true
§/F[f001]
§/M[m001]
