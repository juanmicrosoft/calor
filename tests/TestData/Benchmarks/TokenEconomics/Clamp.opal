§M[m001:ValueOps]
§F[f001:Clamp:pub]
  §I[i32:value]
  §I[i32:min]
  §I[i32:max]
  §O[i32]
  §Q (<= min max)
  §S (>= result min)
  §S (<= result max)
  §IF[if1] (< value min) → §R min
  §EI (> value max) → §R max
  §EL → §R value
  §/I[if1]
§/F[f001]
§/M[m001]
