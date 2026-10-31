using System;
using System.Collections.Generic;

namespace Golden.Domain;

public sealed class Customer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Order> Orders { get; set; } = new();
}

public sealed class Order
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
    public List<OrderLine> Lines { get; set; } = new();
    public OrderStatus Status { get; set; }
}

public sealed class OrderLine
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public int Quantity { get; set; }
}

public enum OrderStatus
{
    Draft,
    Placed,
}
