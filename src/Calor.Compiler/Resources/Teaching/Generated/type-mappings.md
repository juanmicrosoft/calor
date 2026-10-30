# C# → Calor Type Mappings

| C# Type | Calor Type | Notes |
|---------|------------|-------|
| `int` | `i32` | 32-bit signed integer |
| `long` | `i64` | 64-bit signed integer |
| `short` | `i16` | 16-bit signed integer |
| `byte` | `u8` | 8-bit unsigned integer |
| `float` | `f32` | 32-bit floating point |
| `double` | `f64` | 64-bit floating point |
| `decimal` | `dec` | 128-bit decimal |
| `string` | `str` | UTF-16 string |
| `bool` | `bool` | Boolean |
| `char` | `char` | Unicode character |
| `void` | `void` | No return value |
| `object` | `object` | Base type |
| `int?` / `Nullable<int>` | `?i32` | Nullable value |
| `Task<T>` | use `§AF`/`§AMT` | Async function/method |
| `List<T>` | `§LIST{name:type}` | List collection |
| `Dictionary<K,V>` | `§DICT{name:key:val}` | Dictionary collection |
| `HashSet<T>` | `§HSET{name:type}` | HashSet collection |
