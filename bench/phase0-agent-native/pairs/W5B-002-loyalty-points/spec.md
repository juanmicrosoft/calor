# Task: award cap support

The `Loyalty` module computes floored loyalty awards. `AwardWithFloor`
returns earned + bonus, and the returned award is **never below
`minPoints`** — the program's tier statements are printed against it
directly.

Add cap support:

1. `AwardWithCap(earned, bonus, minPoints, maxPoints)` — the floored
   award (exactly as `AwardWithFloor` computes it), but never above
   `maxPoints`. When `maxPoints < minPoints`, `maxPoints` wins.
2. `FormatAward(points)` — returns `"points: <points>"`. Pure formatting.

Rules:

- Public surface: keep every existing public function's name and signature
  unchanged, and its behavior as declared and documented; add the two new public functions.
- Keep the smoke tests green (`dotnet test smoke` from the workspace
  root); they cover the existing surface and must stay passing.
- Do not add dependencies.

Definition of done: the project builds, the smoke tests pass, and the two
new functions behave exactly as specified.
