§M[m001:TempConvert]
§F[f001:CelsiusToFahrenheit:pub]
  §I[f64:c]
  §O[f64]
  §R §OP[kind=add] §OP[kind=mul] §REF[name=c] 1.8 32
§/F[f001]
§F[f002:FahrenheitToCelsius:pub]
  §I[f64:f]
  §O[f64]
  §R §OP[kind=div] §OP[kind=sub] §REF[name=f] 32 1.8
§/F[f002]
§F[f003:CelsiusToKelvin:pub]
  §I[f64:c]
  §O[f64]
  §R §OP[kind=add] §REF[name=c] 273.15
§/F[f003]
§/M[m001]
