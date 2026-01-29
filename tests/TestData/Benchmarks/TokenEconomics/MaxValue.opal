§M[m001:ArrayOps]
§F[f001:Max:pub]
  §I[i32:a]
  §I[i32:b]
  §O[i32]
  §IF[if1] (> a b) → §R a
  §EL → §R b
  §/I[if1]
§/F[f001]
§F[f002:Max3:pub]
  §I[i32:a]
  §I[i32:b]
  §I[i32:c]
  §O[i32]
  §IF[if1] (> a b)
    §IF[if2] (> a c) → §R a
    §EL → §R c
    §/I[if2]
  §EL
    §IF[if3] (> b c) → §R b
    §EL → §R c
    §/I[if3]
  §/I[if1]
§/F[f002]
§/M[m001]
