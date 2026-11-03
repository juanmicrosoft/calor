# C# types in Calor

Use the same C# names: `int`, `long`, `short`, `sbyte`, `byte`, `ushort`, `uint`,
`ulong`, `float`, `double`, `decimal`, `string`, `bool`, `char`, `object`, `void`.
Arrays use `T[]` (jagged arrays: `T[][]`); nullable values/references use `T?`.
Use ordinary .NET generic types such as `List<T>`, `Dictionary<K,V>`, `HashSet<T>`,
`IEnumerable<T>`, `Task<T>` and `Func<T,R>`, not dedicated absence/failure types.
Async functions and methods use the currently implemented `§AF` / `§AMT` headers.

Types and value construction are distinct: `§LIST{name:type}`, `§DICT{name:key:val}`
and `§HSET{name:type}` bind collection values; their headers are not type names.
`File.ReadAllLines(path)` returns `string[]`, not `List<string>`.
