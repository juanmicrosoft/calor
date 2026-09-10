    §MT{mt201:Fingerprint:pub} (i32:first, i32:second) -> i32
      §E{}
      §B{left:i32} §C{NormalizeValue} §A first §/C
      §B{right:i32} §C{NormalizeValue} §A second §/C
      §R (+ (* left INT:101) right)
