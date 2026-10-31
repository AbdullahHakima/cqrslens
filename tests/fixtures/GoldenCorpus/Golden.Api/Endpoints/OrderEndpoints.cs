using Golden.Application.Orders;

namespace Golden.Api.Endpoints;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(IEndpointRouteBuilder app)
    {
        app.MapPost("/api/orders", (CreateOrderCommand command) => Results.Ok());
        app.MapGet("/api/orders/{id:guid}", (Guid id) => new GetOrderQuery(id));
        app.MapDelete("/api/orders/{id:guid}", (Guid id) => new CancelOrderCommand(id));

        var v2 = app.MapGroup("/api/v2/orders");
        v2.MapPost("/", (CreateOrderCommand command) => Results.Ok());
    }
}

public static class EndpointDecoys
{
    public const string FakeSource =
        "app.MapDelete(\"/api/orders/999\", (CancelOrderCommand command) => Results.Ok());";

    // app.MapPost("/api/fake", (CreateOrderCommand command) => Results.Ok());
}
