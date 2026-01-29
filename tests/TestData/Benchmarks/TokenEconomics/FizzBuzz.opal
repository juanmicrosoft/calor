§M[m001:FizzBuzz]
§F[f001:Run:pub]
  §O[void]
  §E[cw]
  §L[for1:i:1:100:1]
    §IF[if1] (== (% i 15) 0) → §P "FizzBuzz"
    §EI (== (% i 3) 0) → §P "Fizz"
    §EI (== (% i 5) 0) → §P "Buzz"
    §EL → §P i
    §/I[if1]
  §/L[for1]
§/F[f001]
§/M[m001]
