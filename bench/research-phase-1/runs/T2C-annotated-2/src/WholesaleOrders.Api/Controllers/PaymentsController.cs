using Microsoft.AspNetCore.Mvc;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Services;

namespace WholesaleOrders.Api.Controllers;

[ApiController]
[Route("api/payments")]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _payments;

    public PaymentsController(IPaymentService payments) => _payments = payments;

    public sealed record ChargeRequest(Guid OrderId, decimal Amount, string Currency, string CustomerToken, string Source);

    [HttpPost("charge")]
    public async Task<IActionResult> Charge([FromBody] ChargeRequest req, [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest(new { error = "Idempotency-Key header is required for payment charges." });
        var payment = await _payments.ChargeAsync(
            req.OrderId,
            new Money(req.Amount, req.Currency),
            req.CustomerToken,
            idempotencyKey,
            req.Source,
            ct);
        return Ok(payment);
    }
}
