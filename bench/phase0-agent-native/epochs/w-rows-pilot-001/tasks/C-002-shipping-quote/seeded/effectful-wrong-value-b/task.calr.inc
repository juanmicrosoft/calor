    §MT{mt101:Cheapest:pub} (i32:weight) -> i32
      §E{}
      §B{near:i32} §C{this.quote} §A INT:1 §A weight §/C
      §B{far:i32} §C{this.quote} §A INT:2 §A weight §/C
      §R (- (+ near far) (+ near far))
