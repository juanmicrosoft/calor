| Opener | Closer | TokenKind | Treatment | Dispatch | Rationale |
|--------|--------|-----------|-----------|----------|-----------|
| `§AF` | `§/AF` | `AsyncFunc` | dedent-closed | closer_consumed,module_member | body-bearing construct; dedent closes the block |
| `§AMT` | `§/AMT` | `AsyncMethod` | dedent-closed | closer_consumed | body-bearing construct; dedent closes the block |
| `§ANON` | `§/ANON` | `AnonymousObject` | not-applicable | closer_consumed,expression | expression form; appears inside expressions, not as a block |
| `§ARR` | `§/ARR` | `Array` | dedent-closed | closer_consumed,expression,statement | body-bearing construct; dedent closes the block |
| `§ARR2D` | `§/ARR2D` | `Array2D` | dedent-closed | closer_consumed,expression,statement | body-bearing construct; dedent closes the block |
| `§BASE` | `§/BASE` | `Base` | not-applicable | closer_consumed,expression | expression form; appears inside expressions, not as a block |
| `§C` | `§/C` | `Call` | not-applicable | closer_consumed,expression,statement | expression form; appears inside expressions, not as a block |
| `§CL` | `§/CL` | `Class` | dedent-closed | closer_consumed,module_member | body-bearing construct; dedent closes the block |
| `§CT` | `§/CT` | `Context` | dedent-closed | closer_consumed,module_member | body-bearing construct; dedent closes the block |
| `§CTOR` | `§/CTOR` | `Constructor` | dedent-closed | closer_consumed | body-bearing construct; dedent closes the block |
| `§D` | `§/D` | `Record` | not-applicable | expression | expression-position only; TokenKind.Record not consumed in statement/declaration context |
| `§DC` | `§/DC` | `Decision` | dedent-closed | closer_consumed,module_member | body-bearing construct; dedent closes the block |
| `§DEL` | `§/DEL` | `Delegate` | dedent-closed | closer_consumed,module_member | body-bearing construct; dedent closes the block |
| `§DICT` | `§/DICT` | `Dict` | dedent-closed | closer_consumed,expression,statement | body-bearing construct; dedent closes the block |
| `§DO` | `§/DO` | `Do` | dedent-closed | closer_consumed,statement | body-bearing construct; dedent closes the block |
| `§EACH` | `§/EACH` | `Foreach` | dedent-closed | closer_consumed,statement | body-bearing construct; dedent closes the block |
| `§EACHKV` | `§/EACHKV` | `EachKV` | dedent-closed | closer_consumed,statement | body-bearing construct; dedent closes the block |
| `§EADD` | `§/EADD` | `EventAdd` | dedent-closed | closer_consumed | body-bearing construct; dedent closes the block |
| `§EEXT` | `§/EEXT` | `EnumExtension` | dedent-closed | closer_consumed,module_member | body-bearing construct; dedent closes the block |
| `§EN` | `§/EN` | `Enum` | dedent-closed | closer_consumed,module_member | body-bearing construct; dedent closes the block |
| `§EREM` | `§/EREM` | `EventRemove` | dedent-closed | closer_consumed | body-bearing construct; dedent closes the block |
| `§EVT` | `§/EVT` | `Event` | dedent-closed | closer_consumed | body-bearing construct; dedent closes the block |
| `§F` | `§/F` | `Func` | dedent-closed | closer_consumed,module_member | body-bearing construct; dedent closes the block |
| `§FIXED` | `§/FIXED` | `Fixed` | dedent-closed | closer_consumed,statement | body-bearing construct; dedent closes the block |
| `§GET` | `§/GET` | `Get` | dedent-closed | closer_consumed | body-bearing construct; dedent closes the block |
| `§HD` | `§/HD` | `HiddenSection` | dedent-closed | closer_consumed | body-bearing construct; dedent closes the block |
| `§HSET` | `§/HSET` | `HashSet` | dedent-closed | closer_consumed,expression,statement | body-bearing construct; dedent closes the block |
| `§I` | `§/I` | `In` | not-applicable | closer_consumed | single-line atomic form; no body to dedent-close |
| `§IF` | `§/I` | `If` | dedent-closed | closer_consumed,expression,statement | if/elseif/else chain — see RFC §4.2 subtlety 1; chain closer §/I retained as legacy-form alias |
| `§IFACE` | `§/IFACE` | `Interface` | dedent-closed | closer_consumed,module_member | body-bearing construct; dedent closes the block |
| `§INIT` | `§/INIT` | `Init` | dedent-closed | closer_consumed | body-bearing construct; dedent closes the block |
| `§INTERP` | `§/INTERP` | `Interpolate` | not-applicable | closer_consumed,expression | expression form; appears inside expressions, not as a block |
| `§ITYPE` | `§/ITYPE` | `IndexedType` | dedent-closed | module_member | body-bearing construct; dedent closes the block |
| `§IXER` | `§/IXER` | `Indexer` | dedent-closed | closer_consumed | body-bearing construct; dedent closes the block |
| `§L` | `§/L` | `For` | dedent-closed | closer_consumed,statement | body-bearing construct; dedent closes the block |
| `§LAM` | `§/LAM` | `Lambda` | not-applicable | closer_consumed,expression | expression form; appears inside expressions, not as a block |
| `§LIST` | `§/LIST` | `List` | dedent-closed | closer_consumed,expression,statement | body-bearing construct; dedent closes the block |
| `§M` | `§/M` | `Module` | dedent-closed | closer_consumed,module_member | body-bearing construct; dedent closes the block |
| `§MT` | `§/MT` | `Method` | dedent-closed | closer_consumed | body-bearing construct; dedent closes the block |
| `§NEW` | `§/NEW` | `New` | not-applicable | closer_consumed,expression | expression form; appears inside expressions, not as a block |
| `§OP` | `§/OP` | `OperatorOverload` | dedent-closed | closer_consumed | body-bearing construct; dedent closes the block |
| `§PP` | `§/PP` | `Preprocessor` | closer-retained | closer_consumed,module_member,statement | preprocessor directive; closer carries the condition string for grep/audit |
| `§PPE` | — | `PreprocessorElse` | closer-retained | — | preprocessor directive; closer carries the condition string for grep/audit |
| `§PROP` | `§/PROP` | `Property` | dedent-closed | closer_consumed | body-bearing construct; dedent closes the block |
| `§RTYPE` | `§/RTYPE` | `RefinedType` | dedent-closed | module_member | body-bearing construct; dedent closes the block |
| `§SALLOC` | `§/SALLOC` | `StackAlloc` | dedent-closed | closer_consumed,expression | body-bearing construct; dedent closes the block |
| `§SET` | `§/SET` | `Set` | dedent-closed | closer_consumed | body-bearing construct; dedent closes the block |
| `§SYNC` | `§/SYNC` | `SyncBlock` | dedent-closed | closer_consumed,statement | body-bearing construct; dedent closes the block |
| `§T` | `§/T` | `Type` | not-applicable | — | no dispatch or closer-consumption evidence for TokenKind.Type |
| `§THIS` | `§/THIS` | `This` | not-applicable | closer_consumed,expression | expression form; appears inside expressions, not as a block |
| `§TR` | `§/TR` | `Try` | dedent-closed | closer_consumed,statement | body-bearing construct; dedent closes the block |
| `§UB` | `§/UB` | `UsedBy` | not-applicable | — | no dispatch or closer-consumption evidence for TokenKind.UsedBy |
| `§UNSAFE` | `§/UNSAFE` | `Unsafe` | dedent-closed | closer_consumed,statement | body-bearing construct; dedent closes the block |
| `§US` | `§/US` | `Uses` | not-applicable | — | no dispatch or closer-consumption evidence for TokenKind.Uses |
| `§USE` | `§/USE` | `Use` | dedent-closed | closer_consumed,statement | body-bearing construct; dedent closes the block |
| `§VS` | `§/VS` | `Visible` | dedent-closed | closer_consumed | body-bearing construct; dedent closes the block |
| `§W` | `§/W` | `Match` | dedent-closed | closer_consumed,expression,statement | body-bearing construct; dedent closes the block |
| `§WH` | `§/WH` | `While` | dedent-closed | closer_consumed,statement | body-bearing construct; dedent closes the block |
| `§WITH` | `§/WITH` | `With` | dedent-closed | closer_consumed,expression | body-bearing construct; dedent closes the block |
