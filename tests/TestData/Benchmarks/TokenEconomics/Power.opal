§M{m001:MathPower}
§F{f001:Power:pub}
  §I{i32:base}
  §I{i32:exp}
  §O{i32}
  §Q (>= exp 0)
  §IF{if1} (== exp 0) → §R 1
  §EI (== exp 1) → §R base
  §EL
    §B{prev} §C{Power} §A base §A (- exp 1) §/C
    §R (* base prev)
  §/I{if1}
§/F{f001}
§/M{m001}
