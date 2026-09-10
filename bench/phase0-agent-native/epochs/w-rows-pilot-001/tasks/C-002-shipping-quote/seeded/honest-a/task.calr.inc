    §MT{mt101:Cheapest:pub} (i32:weight) -> i32
      §E{}
      §B{near:i32} §C{Calculate} §A INT:1 §A weight §/C
      §B{far:i32} §C{Calculate} §A INT:2 §A weight §/C
      §IF{if103} (< near far)
        §R near
      §R far
