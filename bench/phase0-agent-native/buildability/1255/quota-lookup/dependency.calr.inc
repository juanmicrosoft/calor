§M{m001:QuotaLookup}
  §F{f001:Calculate:pub} (i32:requested) -> i32
    §E{}
    §IF{if001} (< requested INT:0)
      §R INT:0
    §IF{if002} (> requested INT:10)
      §R INT:50
    §R (* requested INT:5)

  §F{f002:Lookup:pub} (i32:requested) -> i32
    §E{cw}
    §P "lookup"
    §R §C{Calculate} §A requested §/C

  §CL{c001:QuotaService:pub}
    §FLD{Func<i32,i32>:lookup:pri} §E{cw}
    §CTOR{ctor001:pub} ()
      §ASSIGN lookup QuotaLookupModule.Lookup
