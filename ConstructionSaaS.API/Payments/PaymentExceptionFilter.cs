using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace ConstructionSaaS.API.Payments;
public class PaymentExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var error = context.Exception;
        if (error is DbUpdateException { InnerException: Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 } }) {
            context.Result = new ConflictObjectResult(new { message = "Another billing request completed at the same time. Refresh status before trying again." });
            context.ExceptionHandled = true;
            return;
        }
        if (error is not (PaymentUnavailableException or DbUpdateConcurrencyException or HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)) return;
        context.Result = new ObjectResult(new { message = error is PaymentUnavailableException ? error.Message : error is DbUpdateConcurrencyException ? "Payment details changed. Refresh and try again." : "Payment verification is temporarily unavailable. Please retry later." })
        { StatusCode = error is DbUpdateConcurrencyException ? 409 : 503 };
        context.ExceptionHandled = true;
    }
}
