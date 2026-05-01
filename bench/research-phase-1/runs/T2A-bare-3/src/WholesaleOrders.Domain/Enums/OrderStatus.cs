namespace WholesaleOrders.Domain.Enums;

public enum OrderStatus
{
    Draft = 0,
    Submitted = 1,
    Paid = 2,
    Shipped = 3,
    Delivered = 4,
    Cancelled = 5,
    Returned = 6,
}
