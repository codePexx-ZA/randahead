using BudgetApi.Models;
using BudgetApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace BudgetApi.Controllers;

public abstract class ApiController : ControllerBase
{
    protected AppUser Caller => HttpContext.Caller();

    protected ObjectResult Forbidden(string message) => StatusCode(StatusCodes.Status403Forbidden, new { message });
}
