§M{m001:CatalogPreview}
  §F{f001:SumFormatted:pub}<eff e> (i32:value, Func<i32,i32>:format §E{e}) -> i32
    §E{e}
    §B{first:i32} §C{format} §A value §/C
    §B{second:i32} §C{format} §A (+ value INT:1) §/C
    §R (+ first second)

  §F{f002:Calculate:pub} (i32:value) -> i32
    §E{}
    §R (* value INT:2)

  §F{f003:Render:pub} (i32:value) -> i32
    §E{cw}
    §P "render"
    §R §C{Calculate} §A value §/C

  §CL{c001:Catalog:pub}
    §PROP{p001:Formatter:Func<i32,i32>:pub:get,set}
    §CTOR{ctor001:pub} ()
      §ASSIGN Formatter CatalogPreviewModule.Render
