using Golden.Application.Orders;
using Microsoft.AspNetCore.Mvc;

namespace Golden.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class OrdersController : ControllerBase
{
    [HttpPost("archive")]
    public IActionResult Archive([FromBody] ArchiveOrderCommand command) => Ok();

    [HttpGet("details/{id:guid}")]
    public IActionResult Get(Guid id) => Ok(new GetOrderQuery(id));
}
