// Api controller: base for the API's controllers, giving them the signed-in user and a 403 with a message
using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApi.Controllers;

public abstract class ApiController : ControllerBase
{
    protected AppUser Caller => HttpContext.Caller();

    protected ObjectResult Forbidden(string message) => StatusCode(StatusCodes.Status403Forbidden, new { message });
}
