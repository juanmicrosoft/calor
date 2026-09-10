§M{m001:RetryBudget}
  §F{f001:WithRetry:pub}<eff e> (Func<i32>:attempt §E{e}) -> i32
    §E{e}
    §B{first:i32} §C{attempt}
    §IF{if001} (< first INT:0)
      §R §C{attempt}
    §R first

  §F{f002:Budget:pub} () -> i32
    §E{}
    §R INT:2

  §F{f003:StandardAttempt:pub} () -> i32
    §E{cw}
    §P "attempt"
    §R §C{Budget}

  §CL{c001:RetryPolicy:pub}
    §FLD{Func<i32>:attempt:pri} §E{cw}
    §CTOR{ctor001:pub} ()
      §ASSIGN attempt RetryBudgetModule.StandardAttempt
