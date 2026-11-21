# C# types in Calor

Use the same C# names: `int`, `long`, `short`, `sbyte`, `byte`, `ushort`, `uint`,
`ulong`, `float`, `double`, `decimal`, `string`, `bool`, `char`, `object`, `void`.
Arrays use `T[]` (jagged arrays: `T[][]`); nullable values/references use `T?`.
Use ordinary .NET generic types such as `List<T>`, `Dictionary<K,V>`, `HashSet<T>`,
`IEnumerable<T>`, `Task<T>` and `Func<T,R>`, not dedicated absence/failure types.
Declare an async function with `func Name async (...) → Task<T>` and use `await` in its body.

Types and value construction are distinct: `let List<int>:items = new List<int>()`
binds a collection value. A collection initializer such as `new List<int>{1, 2}`
is an expression, not a type name. `File.ReadAllLines(path)` returns `string[]`,
not `List<string>`.
