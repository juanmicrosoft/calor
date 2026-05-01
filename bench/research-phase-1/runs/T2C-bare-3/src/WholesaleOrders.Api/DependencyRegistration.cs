using WholesaleOrders.Infra.Logging;
using WholesaleOrders.Infra.Persistence;
using WholesaleOrders.Services;
using WholesaleOrders.Services.Validators;

namespace WholesaleOrders.Api;

public static class DependencyRegistration
{
    public static IServiceCollection AddWholesaleOrders(this IServiceCollection services)
    {
        services.AddSingleton<AppDbContext>();
        services.AddSingleton<IStructuredLogger, StructuredLogger>();

        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IShipmentRepository, ShipmentRepository>();

        services.AddScoped<IOrderValidator, OrderValidator>();
        services.AddScoped<IInventoryValidator, InventoryValidator>();
        services.AddScoped<IPaymentValidator, PaymentValidator>();

        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddScoped<IShipmentService, ShipmentService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IOrderService>(sp => new OrderService(
            sp.GetRequiredService<IOrderRepository>(),
            sp.GetRequiredService<IOrderValidator>(),
            sp.GetRequiredService<IStructuredLogger>(),
            sp.GetRequiredService<IInventoryRepository>(),
            sp.GetRequiredService<INotificationService>()));

        return services;
    }
}
