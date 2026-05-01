# T1.B Grader — Setup

This grader requires `Microsoft.AspNetCore.Mvc.Testing` on the test project. Add it before running:

```bash
dotnet add tests/WholesaleOrders.Tests/WholesaleOrders.Tests.csproj package Microsoft.AspNetCore.Mvc.Testing
```

Then drop `AcceptanceTests.cs` into `tests/WholesaleOrders.Tests/Acceptance/T1B/`:

```bash
mkdir -p tests/WholesaleOrders.Tests/Acceptance/T1B
cp graders/T1.B/AcceptanceTests.cs tests/WholesaleOrders.Tests/Acceptance/T1B/
dotnet test --filter "FullyQualifiedName~T1B"
```

## Expected results on a correct implementation

All 7 tests pass:

- 4 acceptance: `Reservation_Expires_After_30_Minutes_If_Not_Confirmed`, `Expired_Reservation_Releases_Inventory`, `Expired_Reservation_Sends_Notification`, `Confirmed_Reservation_Does_Not_Expire`
- 3 adversarial: `Already_Released_Reservation_Stays_Released`, `Fulfilled_Reservation_Never_Expires`, `Inventory_Stays_Consistent_During_Sweep`

## Failure modes that mean correctness=0

- The grader cannot find an expiry trigger via reflection / hosted-service / HTTP probing → model didn't expose a testable mechanism → `correctness=0` per pre-committed rule.
- Acceptance tests compile but fail with `XUnit Assert.Equal` mismatches → model implemented something but it's wrong → `correctness=0`.

## Failure modes that may not be the model's fault

- The grader compile fails due to missing types the model didn't add (`OrderPriority` etc. — that's T1.A territory). Adapt the grader file by removing references that don't apply, or skip the grader run for that prompt.
