  §CL{c002:QuoteCalculator:pub}
    §MT{mt002:Preview:pub} (QuoteFeed:feed) -> i32
      §E{}
      §B{rate:i32} feed.CurrentRate
      §B{sample:Func<i32>} §LAM{lam001} §E{}
        §R rate
      §/LAM{lam001}
      §R §C{SumSamples} §A sample §/C
