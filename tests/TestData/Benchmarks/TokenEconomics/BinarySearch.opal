§M[m001:Search]
§F[f001:Find:pub]
  §I[i32:target]
  §I[i32:val1]
  §I[i32:val2]
  §I[i32:val3]
  §O[i32]
  §IF[if1] (== val1 target) → §R 0
  §EI (== val2 target) → §R 1
  §EI (== val3 target) → §R 2
  §EL → §R -1
  §/I[if1]
§/F[f001]
§/M[m001]
