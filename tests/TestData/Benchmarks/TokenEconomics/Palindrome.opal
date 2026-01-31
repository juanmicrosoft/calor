§M{m001:StringCheck}
§F{f001:IsPalindrome3:pub}
  §I{str:a}
  §I{str:b}
  §I{str:c}
  §O{bool}
  §IF{if1} (== a c) → §R true
  §EL → §R false
  §/I{if1}
§/F{f001}
§F{f002:AreEqual:pub}
  §I{str:a}
  §I{str:b}
  §O{bool}
  §IF{if1} (== a b) → §R true
  §EL → §R false
  §/I{if1}
§/F{f002}
§/M{m001}
