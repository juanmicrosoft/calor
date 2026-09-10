§M{m001:BatchPrice}
  §F{f001:PriceBatch:pub}<eff e> (i32:quantity, Func<i32,i32>:price §E{e}) -> i32
    §E{e}
    §B{total:i32} §C{price} §A quantity §/C
    §IF{if001} (> quantity INT:10)
      §R (- total INT:5)
    §R total

  §F{f002:CalculatePrice:pub} (i32:quantity) -> i32
    §E{}
    §R (* quantity INT:3)

  §F{f003:StandardPrice:pub} (i32:quantity) -> i32
    §E{cw}
    §P "price"
    §R §C{CalculatePrice} §A quantity §/C
