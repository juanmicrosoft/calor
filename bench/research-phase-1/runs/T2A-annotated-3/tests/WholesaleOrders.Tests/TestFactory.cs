using WholesaleOrders.Domain.Entities;
using WholesaleOrders.Domain.ValueObjects;
using WholesaleOrders.Infra.Logging;
using WholesaleOrders.Infra.Persistence;
using WholesaleOrders.Services;
using WholesaleOrders.Services.Validators;

namespace WholesaleOrders.Tests;

public sealed class TestFactory
{
    public AppDbContext Db { get; }
    public IStructuredLogger Logger { get; }
    public IOrderRepository OrderRepo { get; }
    public IInventoryRepository InventoryRepo { get; }
    public ICustomerRepository CustomerRepo { get; }
    public IPaymentRepository PaymentRepo { get; }
    public IShipmentRepository ShipmentRepo { get; }
    public IOrderValidator OrderValidator { get; }
    public IInventoryValidator InventoryValidator { get; }
    public IPaymentValidator PaymentValidator { get; }
    public IPromoCodeService PromoCodes { get; }
    public IOrderService Orders { get; }
    public IInventoryService Inventory { get; }
    public IPaymentService Payments { get; }
    public IShipmentService Shipments { get; }
    public INotificationService Notifications { get; }

    public TestFactory()
    {
        Db = new AppDbContext();
        Logger = new StructuredLogger();
        OrderRepo = new OrderRepository(Db);
        InventoryRepo = new InventoryRepository(Db);
        CustomerRepo = new CustomerRepository(Db);
        PaymentRepo = new PaymentRepository(Db);
        ShipmentRepo = new ShipmentRepository(Db);
        OrderValidator = new OrderValidator();
        InventoryValidator = new InventoryValidator();
        PaymentValidator = new PaymentValidator();
        PromoCodes = new PromoCodeService();
        Orders = new OrderService(OrderRepo, OrderValidator, PromoCodes, Logger);
        Inventory = new InventoryService(InventoryRepo, InventoryValidator, Logger);
        Payments = new PaymentService(OrderRepo, PaymentRepo, PaymentValidator, Logger);
        Shipments = new ShipmentService(ShipmentRepo, OrderRepo, Logger);
        Notifications = new NotificationService(Logger);
    }

    public async Task<Customer> CreateCustomerAsync(string name = "Acme Corp", string email = "ops@acme.test")
    {
        var c = new Customer { Name = name, Email = email, BillingAddress = "1 Acme Way", ShippingAddress = "1 Acme Way" };
        await CustomerRepo.AddAsync(c);
        return c;
    }

    public async Task<InventoryItem> SeedInventoryAsync(string sku, string name, int onHand, decimal unitPrice = 10m)
    {
        return await Inventory.AddItemAsync(Sku.Parse(sku), name, onHand, unitPrice);
    }

    public async Task<Order> CreateOrderWithItemsAsync(Guid customerId, params (string Sku, int Qty, decimal Price)[] items)
    {
        var order = await Orders.CreateDraftAsync(customerId);
        foreach (var (sku, qty, price) in items)
            order = await Orders.AddLineItemAsync(order.Id, Sku.Parse(sku), qty, price);
        return order;
    }
}
