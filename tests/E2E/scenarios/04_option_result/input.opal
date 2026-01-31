§M{m001:TypeSystem}

§F{f001:Main:pub}
  §O{void}
  §E{cw}
  §C{Console.WriteLine}
    §A "Testing Option and Result types..."
  §/C
  §C{TestSome}
  §/C
  §C{TestNone}
  §/C
  §C{TestOk}
  §/C
  §C{TestErr}
  §/C
§/F{f001}

§F{f002:TestSome:pri}
  §O{void}
  §E{cw}
  §B{opt} §SOME 42
  §C{Console.WriteLine}
    §A "  Created Some(42)"
  §/C
§/F{f002}

§F{f003:TestNone:pri}
  §O{void}
  §E{cw}
  §B{opt} §NONE{type=INT}
  §C{Console.WriteLine}
    §A "  Created None"
  §/C
§/F{f003}

§F{f004:TestOk:pri}
  §O{void}
  §E{cw}
  §B{result} §OK 100
  §C{Console.WriteLine}
    §A "  Created Ok(100)"
  §/C
§/F{f004}

§F{f005:TestErr:pri}
  §O{void}
  §E{cw}
  §B{result} §ERR "Something went wrong"
  §C{Console.WriteLine}
    §A "  Created Err"
  §/C
§/F{f005}

§/M{m001}
