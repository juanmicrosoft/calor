§M[m001:Factorial]
§F[f001:Calculate:pub]
  §I[i32:n]
  §O[i32]
  §IF[if1] (<= n 1) → §R 1
  §EL
    §B[prev] §C[Calculate] §A (- n 1) §/C
    §R (* n prev)
  §/I[if1]
§/F[f001]
§/M[m001]
