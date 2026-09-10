§M{m001:CheckpointReader}
  §F{f001:AfterVersion:pub}<eff e> (i32:version, Func<i32,i32>:read §E{e}) -> i32
    §E{e}
    §B{next:i32} §C{read} §A version §/C
    §IF{if001} (> next version)
      §R next
    §R INT:0

  §F{f002:NextVersion:pub} (i32:version) -> i32
    §E{}
    §R (+ version INT:1)

  §F{f003:ReadVersion:pub} (i32:version) -> i32
    §E{cw}
    §P "checkpoint"
    §R §C{NextVersion} §A version §/C

  §CL{c001:Checkpoint:pub}
    §FLD{Func<i32,i32>:readVersion:pri} §E{cw}
    §CTOR{ctor001:pub} ()
      §ASSIGN readVersion CheckpointReaderModule.ReadVersion
